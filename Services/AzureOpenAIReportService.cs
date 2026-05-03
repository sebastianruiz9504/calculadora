using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CotizadorInterno.Web.Models.Reportes;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public sealed class AzureOpenAIReportService : IAzureOpenAIReportService
{
    private static readonly TimeSpan BogotaOffset = TimeSpan.FromHours(-5);
    private readonly IReportesDataverseRepository _repository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AzureOpenAIOptions _azureOpenAIOptions;
    private readonly ReportesOptions _reportesOptions;
    private readonly ILogger<AzureOpenAIReportService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        WriteIndented = true
    };

    public AzureOpenAIReportService(
        IReportesDataverseRepository repository,
        IHttpClientFactory httpClientFactory,
        IOptions<AzureOpenAIOptions> azureOpenAIOptions,
        IOptions<ReportesOptions> reportesOptions,
        ILogger<AzureOpenAIReportService> logger)
    {
        _repository = repository;
        _httpClientFactory = httpClientFactory;
        _azureOpenAIOptions = azureOpenAIOptions.Value;
        _reportesOptions = reportesOptions.Value;
        _logger = logger;
    }

    public async Task<ReporteGenerarResult> GenerateReportAsync(
        ReporteGenerarRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var clienteId = NormalizeGuid(request.ClienteId, nameof(request.ClienteId));
        var period = ResolvePeriod(request.Periodo);
        var input = await _repository.LoadMonthlyInputAsync(
            clienteId,
            period.Value,
            period.StartDate,
            period.EndExclusiveDate,
            ct);
        var payload = BuildConsolidatedPayload(input);
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var fechaGeneracion = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        try
        {
            var html = await GenerateHtmlWithAzureOpenAIAsync(payloadJson, ct);
            var saved = await _repository.UpsertGeneratedReportAsync(new ReporteHtmlGeneradoRecord
            {
                ClienteId = clienteId,
                Periodo = period.Value,
                HtmlGenerado = html,
                Estado = "Generado",
                FechaGeneracion = fechaGeneracion,
                PromptVersion = _reportesOptions.PromptVersion,
                Errores = ""
            }, ct);

            _logger.LogInformation(
                "Informe HTML generado para cliente {ClienteId}, periodo {Periodo}.",
                clienteId,
                period.Value);

            return new ReporteGenerarResult
            {
                IdReporte = saved.RecordId,
                Html = string.IsNullOrWhiteSpace(saved.HtmlGenerado) ? html : saved.HtmlGenerado,
                Estado = string.IsNullOrWhiteSpace(saved.Estado) ? "Generado" : saved.Estado
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var error = BuildExceptionDetail(ex);
            _logger.LogWarning(
                ex,
                "Generacion HTML con Azure OpenAI fallida para cliente {ClienteId}, periodo {Periodo}.",
                clienteId,
                period.Value);

            var saved = await _repository.UpsertGeneratedReportAsync(new ReporteHtmlGeneradoRecord
            {
                ClienteId = clienteId,
                Periodo = period.Value,
                HtmlGenerado = "",
                Estado = "Error",
                FechaGeneracion = fechaGeneracion,
                PromptVersion = _reportesOptions.PromptVersion,
                Errores = Truncate(error, 3900)
            }, ct);

            return new ReporteGenerarResult
            {
                IdReporte = saved.RecordId,
                Html = "",
                Estado = string.IsNullOrWhiteSpace(saved.Estado) ? "Error" : saved.Estado,
                Error = string.IsNullOrWhiteSpace(saved.Errores) ? error : saved.Errores
            };
        }
    }

    private async Task<string> GenerateHtmlWithAzureOpenAIAsync(string payloadJson, CancellationToken ct)
    {
        ValidateAzureOpenAIOptions();

        var requestBody = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["messages"] = new object[]
            {
                new
                {
                    role = "system",
                    content = BuildSystemPrompt()
                },
                new
                {
                    role = "user",
                    content =
                        "Genera el informe HTML mensual usando exclusivamente este JSON consolidado. " +
                        "No inventes metricas ni tickets. JSON:\n" + payloadJson
                }
            },
        };
        var tokenParameterName = NormalizeTokenParameterName(_azureOpenAIOptions.TokenParameterName);
        requestBody[tokenParameterName] = _azureOpenAIOptions.MaxTokens;
        if (_azureOpenAIOptions.IncludeTemperature)
            requestBody["temperature"] = _azureOpenAIOptions.Temperature;

        if (!string.IsNullOrWhiteSpace(_azureOpenAIOptions.ReasoningEffort))
            requestBody["reasoning_effort"] = _azureOpenAIOptions.ReasoningEffort.Trim();

        if (!string.IsNullOrWhiteSpace(_azureOpenAIOptions.Verbosity))
            requestBody["verbosity"] = _azureOpenAIOptions.Verbosity.Trim();

        var endpoint = _azureOpenAIOptions.Endpoint.TrimEnd('/');
        var deployment = Uri.EscapeDataString(_azureOpenAIOptions.DeploymentName.Trim());
        var apiVersion = Uri.EscapeDataString(_azureOpenAIOptions.ApiVersion.Trim());
        var uri = $"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(_azureOpenAIOptions.TimeoutSeconds, 30, 600));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri);
        httpRequest.Headers.TryAddWithoutValidation("api-key", _azureOpenAIOptions.ApiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(requestBody, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(httpRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Azure OpenAI error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        var html = ExtractChatCompletionContent(body);
        html = NormalizeHtmlResponse(html);
        if (string.IsNullOrWhiteSpace(html) || !html.TrimStart().StartsWith("<!DOCTYPE html>", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Azure OpenAI no devolvio un HTML completo con <!DOCTYPE html>.");

        return html;
    }

    private ReporteConsolidadoPayload BuildConsolidatedPayload(ReporteMonthlyInput input)
    {
        var tickets = input.Tickets ?? Array.Empty<ReporteTicketData>();
        var totalTickets = tickets.Count;
        var totalHours = RoundDecimal(tickets.Sum(ticket => ticket.HoursTaken));
        var averageHours = totalTickets == 0 ? 0m : RoundDecimal(totalHours / totalTickets);
        var snapshot = input.SecuritySnapshot;

        return new ReporteConsolidadoPayload
        {
            Cliente = input.Cliente,
            Periodo = new ReportePeriodoPayload
            {
                Valor = input.Periodo,
                FechaInicio = input.FechaInicio.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                FechaFin = input.FechaFinExclusiva.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            },
            ResumenTickets = new ReporteTicketSummaryPayload
            {
                TotalTickets = totalTickets,
                TotalHoras = totalHours,
                PromedioHoras = averageHours,
                Resumen = BuildTicketSummaryText(totalTickets, totalHours, averageHours)
            },
            MetricasTickets = new ReporteTicketMetricsPayload
            {
                PorEstado = BuildBreakdown(tickets, ticket => ticket.StateLabel, totalTickets),
                PorTipo = BuildBreakdown(tickets, ticket => ticket.TypeLabel, totalTickets),
                PorCategoria = BuildBreakdown(tickets, ticket => ticket.CategoryLabel, totalTickets),
                PorMetodo = BuildBreakdown(tickets, ticket => ticket.MethodLabel, totalTickets),
                PorCreador = BuildBreakdown(tickets, ticket => ticket.CreatorName, totalTickets)
            },
            TicketsRelevantes = BuildRelevantTickets(tickets),
            SeguridadMicrosoft365 = BuildSecurityPayload(snapshot)
        };
    }

    private ReporteSecurityPromptPayload BuildSecurityPayload(ReporteSecuritySnapshotData? snapshot)
    {
        if (snapshot is null)
        {
            return new ReporteSecurityPromptPayload
            {
                TieneSnapshot = false,
                EstadoConsulta = "Sin snapshot",
                ErrorConsulta = "No se encontro snapshot mensual de seguridad Microsoft 365 para el periodo."
            };
        }

        var maxSecurityItems = Math.Clamp(_reportesOptions.MaxSecurityItemsInPrompt, 5, 100);
        return new ReporteSecurityPromptPayload
        {
            TieneSnapshot = true,
            EstadoConsulta = snapshot.EstadoConsulta,
            ErrorConsulta = snapshot.ErrorConsulta,
            TenantId = snapshot.TenantId,
            SecureScoreActual = snapshot.SecureScoreActual,
            SecureScoreMaximo = snapshot.SecureScoreMaximo,
            SecureScorePorcentaje = snapshot.SecureScoreMaximo <= 0m
                ? 0m
                : RoundDecimal((snapshot.SecureScoreActual * 100m) / snapshot.SecureScoreMaximo),
            AlertasHigh = snapshot.AlertasHigh,
            AlertasMedium = snapshot.AlertasMedium,
            AlertasLow = snapshot.AlertasLow,
            IncidentesActivos = snapshot.IncidentesActivos,
            IncidentesResueltos = snapshot.IncidentesResueltos,
            Recomendaciones = ParseJsonArray(snapshot.RecomendacionesTopJson, maxSecurityItems),
            Alertas = ParseJsonArray(snapshot.AlertasJson, maxSecurityItems),
            Incidentes = ParseJsonArray(snapshot.IncidentesJson, maxSecurityItems)
        };
    }

    private IReadOnlyList<ReporteTicketPromptItem> BuildRelevantTickets(IReadOnlyList<ReporteTicketData> tickets)
    {
        var maxTickets = Math.Clamp(_reportesOptions.MaxTicketsInPrompt, 10, 200);
        return tickets
            .OrderByDescending(ticket => IsOpenTicket(ticket.StateLabel))
            .ThenByDescending(ticket => ticket.HoursTaken)
            .ThenByDescending(ticket => ticket.CreationDateValue, StringComparer.OrdinalIgnoreCase)
            .Take(maxTickets)
            .Select(ticket => new ReporteTicketPromptItem
            {
                RecordId = ticket.RecordId,
                Titulo = Truncate(ticket.Title, 180),
                Fecha = ticket.CreationDateDisplay,
                Estado = ticket.StateLabel,
                Tipo = ticket.TypeLabel,
                Categoria = ticket.CategoryLabel,
                Metodo = ticket.MethodLabel,
                Creador = ticket.CreatorName,
                Horas = ticket.HoursTaken,
                Descripcion = Truncate(ticket.Description, 420),
                Solucion = Truncate(ticket.Solution, 420)
            })
            .ToList();
    }

    private static IReadOnlyList<ReporteBreakdownItem> BuildBreakdown(
        IReadOnlyList<ReporteTicketData> tickets,
        Func<ReporteTicketData, string> labelSelector,
        int totalTickets)
    {
        return tickets
            .GroupBy(ticket => NormalizeGroupLabel(labelSelector(ticket)), StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReporteBreakdownItem
            {
                Label = group.Key,
                Total = group.Count(),
                Horas = RoundDecimal(group.Sum(ticket => ticket.HoursTaken)),
                Porcentaje = totalTickets == 0
                    ? 0m
                    : RoundDecimal((group.Count() * 100m) / totalTickets)
            })
            .OrderByDescending(item => item.Total)
            .ThenByDescending(item => item.Horas)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<JsonElement> ParseJsonArray(string? rawJson, int maxItems)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return Array.Empty<JsonElement>();

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return doc.RootElement
                    .EnumerateArray()
                    .Take(maxItems)
                    .Select(item => item.Clone())
                    .ToList();
            }

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                return new[] { doc.RootElement.Clone() };
        }
        catch (JsonException)
        {
            return Array.Empty<JsonElement>();
        }

        return Array.Empty<JsonElement>();
    }

    private static string ExtractChatCompletionContent(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
            {
                return content.GetString() ?? "";
            }
        }

        throw new InvalidOperationException("La respuesta de Azure OpenAI no contiene choices[0].message.content.");
    }

    private static string NormalizeHtmlResponse(string html)
    {
        var value = (html ?? "").Trim();
        if (value.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineBreak = value.IndexOf('\n');
            if (firstLineBreak >= 0)
                value = value[(firstLineBreak + 1)..];

            var closingFence = value.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
                value = value[..closingFence];
        }

        return value.Trim();
    }

    private static string BuildSystemPrompt()
    {
        return """
Eres un consultor senior de soporte cloud y seguridad Microsoft 365.
Debes generar un informe mensual ejecutivo en espanol para un cliente empresarial.

Reglas obligatorias:
- Responde exclusivamente con HTML completo. No uses Markdown ni fences.
- El HTML debe iniciar con <!DOCTYPE html> e incluir html, head y body.
- Incluye CSS embebido moderno, sobrio y responsive.
- Usa el logo del cliente si el JSON trae logo. Si no hay logo, usa el nombre del cliente como marca textual.
- Usa el color corporativo del JSON como color principal.
- Incluye tarjetas KPI, resumen ejecutivo, hallazgos, recomendaciones, tabla de tickets relevantes, seccion de seguridad Microsoft 365 y conclusion.
- No inventes datos. Si falta el snapshot de seguridad, dilo como limitacion operativa y recomienda recolectarlo.
- No incluyas scripts externos, imagenes externas inventadas, ni dependencias CDN.
- Evita texto de relleno. Prioriza conclusiones accionables y lenguaje claro.
""";
    }

    private void ValidateAzureOpenAIOptions()
    {
        if (string.IsNullOrWhiteSpace(_azureOpenAIOptions.Endpoint))
            throw new ReportesConfigurationException("AzureOpenAI:Endpoint no esta configurado.");

        if (string.IsNullOrWhiteSpace(_azureOpenAIOptions.ApiKey))
            throw new ReportesConfigurationException("AzureOpenAI:ApiKey no esta configurado. Usa user secrets, variables de entorno o configuracion segura.");

        if (string.IsNullOrWhiteSpace(_azureOpenAIOptions.DeploymentName))
            throw new ReportesConfigurationException("AzureOpenAI:DeploymentName no esta configurado.");

        if (string.IsNullOrWhiteSpace(_azureOpenAIOptions.ApiVersion))
            throw new ReportesConfigurationException("AzureOpenAI:ApiVersion no esta configurado.");
    }

    private static string NormalizeTokenParameterName(string? raw)
    {
        var value = (raw ?? "").Trim();
        return string.Equals(value, "max_completion_tokens", StringComparison.OrdinalIgnoreCase)
            ? "max_completion_tokens"
            : "max_tokens";
    }

    private static ReportPeriod ResolvePeriod(string? rawPeriod)
    {
        var value = rawPeriod?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(value))
        {
            var nowBogota = DateTimeOffset.UtcNow.ToOffset(BogotaOffset);
            var previousMonth = new DateOnly(nowBogota.Year, nowBogota.Month, 1).AddMonths(-1);
            return BuildPeriod(previousMonth.Year, previousMonth.Month);
        }

        if (!DateTime.TryParseExact(
                value,
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            throw new InvalidOperationException("El periodo debe tener formato yyyy-MM.");
        }

        return BuildPeriod(parsed.Year, parsed.Month);
    }

    private static ReportPeriod BuildPeriod(int year, int month)
    {
        var start = new DateOnly(year, month, 1);
        var endExclusive = start.AddMonths(1);
        return new ReportPeriod(
            start.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            start,
            endExclusive);
    }

    private static string BuildTicketSummaryText(int totalTickets, decimal totalHours, decimal averageHours)
    {
        if (totalTickets == 0)
            return "No se registraron tickets de soporte cloud para el periodo.";

        return $"Se registraron {totalTickets} ticket(s), con {totalHours:N2} hora(s) reportadas y un promedio de {averageHours:N2} hora(s) por ticket.";
    }

    private static string NormalizeGroupLabel(string? label)
    {
        var normalized = (label ?? "").Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "Sin clasificar" : normalized;
    }

    private static bool IsOpenTicket(string? stateLabel)
    {
        var value = (stateLabel ?? "").Trim().ToLowerInvariant();
        return value.Contains("abierto", StringComparison.Ordinal)
            || value.Contains("pendiente", StringComparison.Ordinal)
            || value.Contains("proceso", StringComparison.Ordinal)
            || value.Contains("active", StringComparison.Ordinal)
            || value.Contains("open", StringComparison.Ordinal);
    }

    private static string NormalizeGuid(string? raw, string paramName)
    {
        if (!Guid.TryParse(raw, out var parsed))
            throw new InvalidOperationException($"El valor de {paramName} no es valido.");

        return parsed.ToString("D");
    }

    private static decimal RoundDecimal(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string BuildExceptionDetail(Exception ex)
    {
        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (string.IsNullOrWhiteSpace(current.Message))
                continue;

            var trimmed = current.Message.Trim();
            if (!messages.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                messages.Add(trimmed);
        }

        return string.Join(" | ", messages);
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private sealed record ReportPeriod(
        string Value,
        DateOnly StartDate,
        DateOnly EndExclusiveDate);
}
