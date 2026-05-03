using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CotizadorInterno.Web.Models.Calculator;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public sealed class AzureOpenAIQuoteProposalService : IAzureOpenAIQuoteProposalService
{
    private static readonly CultureInfo EsCoCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly TimeSpan BogotaOffset = TimeSpan.FromHours(-5);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AzureOpenAIOptions _azureOpenAIOptions;
    private readonly ILogger<AzureOpenAIQuoteProposalService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        WriteIndented = true
    };

    public AzureOpenAIQuoteProposalService(
        IHttpClientFactory httpClientFactory,
        IOptions<AzureOpenAIOptions> azureOpenAIOptions,
        ILogger<AzureOpenAIQuoteProposalService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _azureOpenAIOptions = azureOpenAIOptions.Value;
        _logger = logger;
    }

    public async Task<string> GenerateProposalHtmlAsync(
        QuoteProposalGenerationInput input,
        CancellationToken ct = default)
    {
        if (input is null)
            throw new ArgumentNullException(nameof(input));

        var payload = BuildProposalPayload(input);
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var html = await GenerateHtmlWithAzureOpenAIAsync(payloadJson, ct);

        _logger.LogInformation(
            "Propuesta HTML generada para escenario {ScenarioName}.",
            input.Scenario.ScenarioName);

        return html;
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
                        "Genera la propuesta comercial HTML usando exclusivamente este JSON de cotizacion. " +
                        "No inventes productos, precios, descuentos, horas ni datos del cliente. JSON:\n" +
                        payloadJson
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
        ValidateGeneratedHtml(html);
        return html;
    }

    private static object BuildProposalPayload(QuoteProposalGenerationInput input)
    {
        var scenario = input.Scenario;
        var result = input.Result;
        var generatedAt = input.GeneratedAt == default
            ? DateTimeOffset.UtcNow
            : input.GeneratedAt;
        var generatedAtBogota = generatedAt.ToOffset(BogotaOffset);
        var lines = BuildLinePayloads(scenario.Lines);
        var vatBase = lines.Where(line => line.TieneIva).Sum(line => line.VentaTotal);
        var estimatedVat = RoundMoney(vatBase * 0.19m);
        var totalWithEstimatedVat = RoundMoney(result.TotalSale + estimatedVat);
        var scenarioName = (scenario.ScenarioName ?? "").Trim();

        return new
        {
            documento = new
            {
                tipo = "Propuesta comercial",
                ano = generatedAtBogota.Year,
                fechaGeneracion = generatedAtBogota.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                fechaGeneracionTexto = generatedAtBogota.ToString("dd 'de' MMMM 'de' yyyy", EsCoCulture),
                vigenciaDias = 15
            },
            marca = new
            {
                empresa = "Digital Tech Colombia",
                sitioWeb = "www.digitaltechcolombia.com",
                ciudad = "Bogota DC",
                nit = "59171582566",
                colorPrincipal = "#061943",
                colorAcento = "#18bdd7",
                colorSecundario = "#28c76f"
            },
            destinatario = new
            {
                nombreCliente = string.IsNullOrWhiteSpace(scenarioName) ? "Cliente por confirmar" : scenarioName,
                contacto = "Por confirmar"
            },
            preparadoPor = new
            {
                nombre = input.PreparedByName?.Trim() ?? "",
                correo = input.PreparedByEmail?.Trim() ?? ""
            },
            cotizacion = new
            {
                escenario = string.IsNullOrWhiteSpace(scenarioName) ? "Cotizacion" : scenarioName,
                tipoNegocio = ResolveDealTypeLabel(scenario.DealType),
                requiereProrrateo = scenario.RequiresProration,
                fechaInicio = FormatDate(scenario.StartDate),
                fechaFinal = FormatDate(scenario.EndDate),
                prorrateo = new
                {
                    dias = result.ProrationDays,
                    factor = RoundFactor(result.ProrationFactor),
                    texto = BuildProrationText(result)
                },
                resumen = new
                {
                    lineas = lines.Count,
                    ventaMensualTotal = RoundMoney(result.TotalMonthlySale),
                    ventaTotalContrato = RoundMoney(result.TotalSale),
                    baseIvaEstimado = RoundMoney(vatBase),
                    ivaEstimado19 = estimatedVat,
                    totalContratoConIvaEstimado = totalWithEstimatedVat,
                    ventaMensualTotalCop = FormatCop(result.TotalMonthlySale),
                    ventaTotalContratoCop = FormatCop(result.TotalSale),
                    baseIvaEstimadoCop = FormatCop(vatBase),
                    ivaEstimado19Cop = FormatCop(estimatedVat),
                    totalContratoConIvaEstimadoCop = FormatCop(totalWithEstimatedVat),
                    notaIva = "Los valores de IVA son estimados al 19% solo para lineas marcadas con IVA. Validar tratamiento tributario antes de emitir oferta final."
                },
                lineas = lines
            },
            reglas = new
            {
                noMostrar = new[]
                {
                    "Costo unitario",
                    "Margen",
                    "Acelerador",
                    "Puntaje",
                    "Comision interna"
                },
                formato = "HTML autonomo, imprimible, sin scripts, sin CDN, sin imagenes externas"
            }
        };
    }

    private static IReadOnlyList<QuoteProposalLinePayload> BuildLinePayloads(IReadOnlyList<QuoteLineInput> lines)
    {
        return lines
            .Select((line, index) =>
            {
                var saleUnit = RoundMoney(line.CostUnit * (1m + (line.MarginPercent / 100m)));
                var monthly = RoundMoney(saleUnit * line.Quantity);
                var total = RoundMoney(monthly * line.ContractMonths);
                return new QuoteProposalLinePayload
                {
                    Item = index + 1,
                    Tipo = line.BusinessType.ToString(),
                    Producto = (line.ProductDescription ?? "").Trim(),
                    Cantidad = line.Quantity,
                    DuracionMeses = line.ContractMonths,
                    VentaUnitaria = saleUnit,
                    VentaMensual = monthly,
                    VentaTotal = total,
                    TieneIva = line.HasVat,
                    PrecioSugerido = RoundMoney(line.SuggestedRetailPrice),
                    VentaUnitariaCop = FormatCop(saleUnit),
                    VentaMensualCop = FormatCop(monthly),
                    VentaTotalCop = FormatCop(total),
                    PrecioSugeridoCop = FormatCop(line.SuggestedRetailPrice),
                    NotaIva = line.HasVat ? "Aplica IVA" : "No marcado con IVA"
                };
            })
            .ToList();
    }

    private static string BuildSystemPrompt()
    {
        return """
Eres un consultor comercial senior de Digital Tech Colombia.
Debes generar una propuesta comercial ejecutiva en espanol para un cliente empresarial, a partir de una cotizacion interna.

Reglas obligatorias:
- Responde exclusivamente con HTML completo. No uses Markdown ni fences.
- El HTML debe iniciar con <!DOCTYPE html> e incluir html, head y body.
- Incluye CSS embebido moderno, sobrio, responsive e imprimible en tamano A4.
- No incluyas scripts, dependencias CDN, imagenes externas ni fuentes externas.
- Usa una identidad visual inspirada en la propuesta de referencia: portada azul marino, textos blancos, logo textual DIGITAL TECH, acentos cyan y verde, paginas blancas con franja superior azul, titulos grandes en mayuscula y tablas con encabezados azul marino.
- Usa los colores del JSON de marca: azul principal #061943, cyan #18bdd7 y verde #28c76f.
- No inventes productos, precios, descuentos, horas, fechas, contactos, clientes ni alcance no enviado.
- Nunca muestres informacion interna: costo unitario, margen, acelerador, puntaje, comision ni formulas internas.
- Usa solo valores comerciales publicos: producto, tipo, cantidad, duracion, venta unitaria, venta mensual, venta total, IVA y totales.
- Formatea moneda como COP en estilo colombiano.
- Si falta cliente o contacto, usa "Por confirmar" sin inventar nombres.
- Si hay IVA marcado en algunas lineas, aclara que el IVA estimado usa 19% y debe validarse antes de emitir la oferta final.
- La propuesta debe contener estas secciones: portada, sobre Digital Tech, por que elegirnos, resumen ejecutivo, alcance de la cotizacion, detalle tecnico/comercial de productos o servicios, oferta economica, forma de pago sugerida, supuestos y exclusiones, vigencia, aceptacion y datos de contacto.
- La forma de pago sugerida no debe inventar descuentos: muestra contado 100% o esquema referencial 50% anticipo / 50% cierre, indicando que esta sujeto a aprobacion comercial.
- Manten el HTML listo para descargar y abrir directamente en navegador.
""";
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

    private static void ValidateGeneratedHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html) || !html.TrimStart().StartsWith("<!DOCTYPE html>", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Azure OpenAI no devolvio un HTML completo con <!DOCTYPE html>.");

        if (html.Contains("<script", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Azure OpenAI devolvio HTML con scripts. La propuesta debe contener solo HTML y CSS embebido.");

        var forbiddenTerms = new[]
        {
            "Costo UND",
            "Costo unitario",
            "Margen %",
            "Acelerador",
            "Puntaje",
            "Comision interna",
            "Comision final",
            "Comisi\u00f3n interna",
            "Comisi\u00f3n final"
        };

        foreach (var term in forbiddenTerms)
        {
            if (html.Contains(term, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Azure OpenAI incluyo informacion interna no permitida: {term}.");
        }
    }

    private void ValidateAzureOpenAIOptions()
    {
        if (string.IsNullOrWhiteSpace(_azureOpenAIOptions.Endpoint))
            throw new InvalidOperationException("AzureOpenAI:Endpoint no esta configurado.");

        if (string.IsNullOrWhiteSpace(_azureOpenAIOptions.ApiKey))
            throw new InvalidOperationException("AzureOpenAI:ApiKey no esta configurado. Usa user secrets, variables de entorno o configuracion segura.");

        if (string.IsNullOrWhiteSpace(_azureOpenAIOptions.DeploymentName))
            throw new InvalidOperationException("AzureOpenAI:DeploymentName no esta configurado.");

        if (string.IsNullOrWhiteSpace(_azureOpenAIOptions.ApiVersion))
            throw new InvalidOperationException("AzureOpenAI:ApiVersion no esta configurado.");
    }

    private static string NormalizeTokenParameterName(string? raw)
    {
        var value = (raw ?? "").Trim();
        return string.Equals(value, "max_completion_tokens", StringComparison.OrdinalIgnoreCase)
            ? "max_completion_tokens"
            : "max_tokens";
    }

    private static string ResolveDealTypeLabel(DealType dealType)
    {
        return dealType switch
        {
            DealType.ClienteNuevo => "Cliente nuevo",
            DealType.CrossSale => "Cross sale",
            DealType.Renovacion1 => "Renovacion 1 vez",
            DealType.Renovacion2 => "Renovacion 2 veces",
            DealType.Renovacion3Plus => "Renovacion 3 veces o mas",
            _ => "Cliente nuevo"
        };
    }

    private static string BuildProrationText(QuoteScenarioResult result)
    {
        return result.ProrationDays > 0 && result.ProrationFactor != 1m
            ? $"{result.ProrationDays} dias ({RoundFactor(result.ProrationFactor):0.####})"
            : "No aplica";
    }

    private static string FormatDate(DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "";
    }

    private static string FormatCop(decimal value)
    {
        return RoundMoney(value).ToString("C0", EsCoCulture);
    }

    private static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal RoundFactor(decimal value) =>
        Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private sealed class QuoteProposalLinePayload
    {
        public int Item { get; set; }
        public string Tipo { get; set; } = "";
        public string Producto { get; set; } = "";
        public int Cantidad { get; set; }
        public int DuracionMeses { get; set; }
        public decimal VentaUnitaria { get; set; }
        public decimal VentaMensual { get; set; }
        public decimal VentaTotal { get; set; }
        public bool TieneIva { get; set; }
        public decimal PrecioSugerido { get; set; }
        public string VentaUnitariaCop { get; set; } = "";
        public string VentaMensualCop { get; set; } = "";
        public string VentaTotalCop { get; set; } = "";
        public string PrecioSugeridoCop { get; set; } = "";
        public string NotaIva { get; set; } = "";
    }
}
