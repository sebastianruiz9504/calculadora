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
                        "La informacion del cliente, periodo, tickets y tenant Microsoft 365 ya viene en el JSON. " +
                        "No inventes metricas, tickets, datos de seguridad ni datos del cliente. JSON:\n" + payloadJson
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

        if (html.Contains("<script", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Azure OpenAI devolvio HTML con scripts. El informe debe contener solo HTML y CSS embebido.");

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
        var value = (html ?? "").Trim().Trim('\uFEFF');
        if (value.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineBreak = value.IndexOf('\n');
            if (firstLineBreak >= 0)
                value = value[(firstLineBreak + 1)..];

            var closingFence = value.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
                value = value[..closingFence];
        }

        var doctypeIndex = value.IndexOf("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase);
        if (doctypeIndex >= 0)
        {
            value = value[doctypeIndex..];
            var doctypeEnd = value.IndexOf('>');
            if (doctypeEnd >= 0)
                value = "<!DOCTYPE html>" + value[(doctypeEnd + 1)..];
        }
        else
        {
            var htmlIndex = value.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
            if (htmlIndex >= 0)
                value = "<!DOCTYPE html>\n" + value[htmlIndex..];
        }

        var trailingFence = value.LastIndexOf("```", StringComparison.Ordinal);
        if (trailingFence >= 0)
            value = value[..trailingFence];

        var htmlEnd = value.LastIndexOf("</html>", StringComparison.OrdinalIgnoreCase);
        if (htmlEnd >= 0)
            value = value[..(htmlEnd + "</html>".Length)];

        return value.Trim().Trim('\uFEFF');
    }

    private static string BuildSystemPrompt()
    {
        return """
## Rol y proposito
Eres un agente especializado en generar informes tecnicos ejecutivos de auditoria, soporte cloud y seguridad Microsoft 365 en formato HTML. Tu funcion es recibir un JSON consolidado de la aplicacion y producir un unico archivo HTML final, completo, visualmente sofisticado y listo para abrir en navegador o exportar a PDF.

El enfoque critico es visual: el informe debe parecerse lo mas posible a una plantilla corporativa premium de Digital Tech, con sidebar fijo, portada hero, tarjetas KPI, tablas elegantes, gauges de seguridad, barras comparativas, secciones alternadas, animaciones CSS sutiles y estilos de impresion. El contenido debe basarse estrictamente en el JSON recibido.

## Fuente de datos
El usuario NO adjuntara archivos manuales en este flujo. La aplicacion ya entrega toda la informacion disponible en el JSON:

- `cliente`: nombre, logo y color corporativo.
- `periodo`: valor `yyyy-MM`, fechaInicio y fechaFin.
- `resumenTickets`: totalTickets, totalHoras, promedioHoras y resumen.
- `metricasTickets`: agrupaciones por estado, tipo, categoria, metodo y creador.
- `ticketsRelevantes`: tickets reales del periodo con titulo, fecha, estado, tipo, categoria, metodo, creador, horas, descripcion y solucion.
- `seguridadMicrosoft365`: snapshot real del tenant, Secure Score, alertas, incidentes y recomendaciones, o una limitacion si no existe snapshot.

Debes usar exclusivamente esos campos. No inventes metricas, tickets, porcentajes, nombres, certificaciones, logos, alertas, incidentes, controles, fechas, horas ni datos de contacto.

## Formato de salida obligatorio
- Responde exclusivamente con HTML completo. No uses Markdown, fences ni explicaciones fuera del HTML.
- El documento debe iniciar exactamente con `<!DOCTYPE html>` e incluir `html`, `head` y `body`.
- Todo debe estar en un solo archivo HTML con CSS embebido en `<style>`.
- No incluyas ningun `<script>`, JavaScript inline, `onclick`, `oninput`, `onchange`, `javascript:` ni dependencias externas. El backend rechazara HTML con scripts.
- No uses CDN, Google Fonts, Font Awesome externo, imagenes externas inventadas ni recursos remotos no presentes en el JSON.
- Si `cliente.logo` viene informado, usalo como `src` exactamente como llega. Si no hay logo, muestra una marca textual fuerte con el nombre del cliente.
- Usa el color `cliente.colorCorporativo` como acento principal cuando exista. Si no existe, usa `#103975`.
- El HTML debe ser responsive, imprimible y apto para PDF A4.

## Plantilla visual de referencia
Debes recrear una experiencia visual muy cercana a esta estructura:

- Sidebar fijo de 270px a la izquierda, fondo en gradiente vertical negro a azul `#0F5094`, logo/titulo arriba, navegacion interna y footer compacto.
- Contenido principal con margen izquierdo igual al sidebar.
- Portada hero a pantalla ancha, fondo en gradiente horizontal negro a azul, texto blanco, badge superior tipo pill, H1 grande, subtitulo ejecutivo y tarjetas meta con "Entregado a", "Periodo", "Tenant" y "Generado por".
- Secciones con padding amplio, fondo blanco o gris alternado, titulos con icono dentro de cuadro azul degradado, subtitulo gris, divisor verde/azul corto y contenido aireado.
- Tablas dentro de `.table-wrapper` con bordes redondeados, sombra, thead azul oscuro, filas alternadas y badges de estado/tipo/categoria.
- Tarjetas KPI en `.stats-grid`: fondo blanco, borde sutil, sombra suave, numero grande Montserrat-like, etiqueta y subtexto.
- Seccion de seguridad con gauge circular SVG estatico, porcentaje grande al centro, barras de progreso y tarjetas de alertas/incidentes.
- Listas de implementacion y recomendaciones con borde izquierdo de acento, icono o marcador visual, fondo blanco y sombra.
- Footer final con gradiente azul/negro y texto corporativo.
- Watermark muy sutil "DIGITAL TECH" o "INFORME MENSUAL" fijo o al final, sin molestar la lectura.

Puedes crear iconos con elementos HTML/CSS simples, caracteres seguros o SVG inline pequenos. No dependas de librerias externas. No uses emojis como iconos principales.

## CSS esperado
El CSS debe ser detallado y consistente. Debe incluir, como minimo:

- Variables `:root` para `--dark1`, `--dark2`, `--dark3`, `--accent`, `--accent-light`, `--accent-dark`, `--white`, `--gray-light`, `--gray`, `--gray-dark`, `--sidebar-w` y `--transition`.
- Reset basico con `box-sizing: border-box`.
- Tipografia tipo Montserrat/Open Sans usando fuentes del sistema: `font-family: 'Segoe UI', Arial, sans-serif` para cuerpo y una pila fuerte para titulos.
- `.sidebar`, `.sidebar-logo`, `.sidebar nav a`, `.main`, `.hero`, `.hero-badge`, `.hero-meta`, `.hero-meta-item`.
- `.section`, `.section-alt`, `.section-title`, `.section-subtitle`, `.section-text`, `.divider`.
- `.stats-grid`, `.stat-card`, `.stat-number`, `.stat-label`, `.stat-sub`, `.stat-change`.
- `.table-wrapper`, `table`, `thead`, `tbody`, `th`, `td`, `.badge` y variantes utiles como `.badge-resuelto`, `.badge-abierto`, `.badge-incidente`, `.badge-implementacion`, `.badge-consultoria`, `.badge-security`, `.badge-neutral`.
- `.gauge-container`, `.gauge`, `.gauge-bg`, `.gauge-fill`, `.gauge-text`, `.gauge-info`, `.progress-section`, `.progress-bar-bg`, `.progress-bar-fill`.
- `.impl-list`, `.finding-list`, `.contact-card`, `.footer`, `.watermark`.
- `@media (max-width: 900px)` para ocultar o transformar el sidebar y dejar `.main` sin margen izquierdo.
- `@media print` para A4: ocultar sidebar si estorba, quitar sombras excesivas, forzar fondos blancos donde convenga, evitar cortes internos en tablas/tarjetas con `break-inside: avoid`, y asegurar colores de impresion con `print-color-adjust: exact`.

Usa animaciones CSS suaves si ayudan al aspecto visual: `fadeInDown`, `fadeSlideUp`, `scaleIn`, `expandWidth`. No requieren JavaScript.

## Secciones obligatorias e IDs
Genera estas secciones con estos IDs exactos y en este orden. La navegacion del sidebar debe apuntar a estos IDs:

1. `#portada` - Portada hero.
2. `#marco-iso` - Alcance y marco normativo.
3. `#resumen` - Resumen ejecutivo.
4. `#soportes` - Soportes tecnicos realizados.
5. `#cumplimiento-iso` - Cumplimiento ISO 27001:2022.
6. `#implementacion` - Implementacion y actividades ejecutadas.
7. `#seguridad` - Reporte de seguridad Microsoft 365.
8. `#hallazgos` - Hallazgos de auditoria.
9. `#conclusiones` - Conclusiones.
10. `#recomendaciones` - Recomendaciones.
11. `#contacto` - Contacto.

No agregues secciones nuevas salvo que sean visualmente necesarias y no dupliquen informacion. Si agregas un bloque auxiliar, debe estar dentro de una de las secciones anteriores.

## Mapeo de contenido

### Portada `#portada`
- H1: "Informe Mensual de Soporte Cloud y Seguridad Microsoft 365".
- Badge: "Informe Mensual".
- Subtitulo: resumen del periodo, cliente y alcance.
- Meta cards:
  - Entregado a: `cliente.nombre`.
  - Periodo: rango `periodo.fechaInicio` a `periodo.fechaFin` y/o `periodo.valor`.
  - Tenant: `seguridadMicrosoft365.tenantId` si existe; si no, "No disponible".
  - Generado por: "Digital Tech Copiers S.A.S."

### Alcance y marco normativo `#marco-iso`
- Explica que el informe consolida evidencias de soporte cloud, continuidad operativa, gestion de tickets y postura de seguridad M365.
- Alinea el analisis a ISO/IEC 27001:2022 sin afirmar certificacion ni cumplimiento total si el JSON no lo prueba.
- Incluye una tabla con dominios/controles interpretativos: gestion de incidentes, control de accesos, monitoreo, mejora continua y evidencia operativa. Cada fila debe derivarse de tickets, metricas o snapshot.

### Resumen ejecutivo `#resumen`
- Redacta 2 a 4 parrafos ejecutivos basados en `resumenTickets`, `metricasTickets` y `seguridadMicrosoft365`.
- Incluye una grilla KPI con:
  - Total tickets.
  - Horas reportadas.
  - Promedio horas por ticket.
  - Secure Score porcentual si hay snapshot; si no, "Sin snapshot".
  - Alertas altas.
  - Incidentes activos.
- Evita frases genericas. Cada afirmacion debe conectarse con datos reales.

### Soportes tecnicos realizados `#soportes`
- Incluye una tabla amplia y elegante con tickets reales de `ticketsRelevantes`.
- Columnas recomendadas: Fecha, Ticket, Tipo, Categoria, Metodo, Creador, Horas, Estado, Solucion/resultado.
- Usa badges visuales para estado, tipo y categoria.
- Si no hay tickets, muestra una tarjeta o fila que diga que no se registraron tickets en el periodo.
- No crees tickets ficticios ni fusiones tickets sin indicarlo.

### Cumplimiento ISO 27001:2022 `#cumplimiento-iso`
- Presenta el cumplimiento como "alineacion operativa observada", no como auditoria formal.
- Usa tarjetas y/o tabla para clasificar:
  - Evidencia disponible.
  - Riesgo observado.
  - Nivel de madurez estimado cualitativo: Alto, Medio, Bajo o Sin evidencia.
  - Recomendacion.
- Deriva todo de tickets, estado de seguridad, incidentes, alertas y recomendaciones M365.
- Si faltan datos, declara la limitacion claramente.

### Implementacion `#implementacion`
- Resume actividades de implementacion o cambios ejecutados usando tickets cuyo tipo/categoria/descripcion/solucion sugiera implementacion, configuracion, ajustes, migracion, seguridad o administracion.
- Usa `.impl-list` con tarjetas/list items visuales.
- Si no hay implementaciones explicitas, habla de actividades operativas y de soporte evidenciadas, sin inventar proyectos.

### Reporte de seguridad `#seguridad`
- Si `seguridadMicrosoft365.tieneSnapshot` es true:
  - Muestra un gauge circular con `secureScorePorcentaje`.
  - Muestra Secure Score actual/maximo.
  - Muestra tarjetas para alertas high/medium/low, incidentes activos e incidentes resueltos.
  - Lista recomendaciones top, alertas e incidentes si vienen en arrays.
  - Usa barras de progreso y comparativas visuales.
- Si `tieneSnapshot` es false:
  - Muestra una seccion visual de limitacion operativa.
  - Usa `estadoConsulta` y `errorConsulta`.
  - Recomienda recolectar el snapshot mensual antes del siguiente comite.
- No inventes porcentajes ni severidades.

### Hallazgos `#hallazgos`
- Genera hallazgos accionables a partir de:
  - Tickets abiertos, pendientes o con mayor consumo de horas.
  - Concentracion por categoria, creador, metodo o tipo.
  - Alertas altas/medias, incidentes activos o bajo Secure Score.
- Cada hallazgo debe tener evidencia, impacto y accion sugerida.
- Si la evidencia es insuficiente, dilo de forma ejecutiva.

### Conclusiones `#conclusiones`
- Redacta conclusiones breves, ejecutivas y conectadas a los datos.
- Deben mencionar continuidad del servicio, gestion de tickets, postura de seguridad y limitaciones si aplica.

### Recomendaciones `#recomendaciones`
- Usa una tabla o lista visual priorizada.
- Columnas sugeridas: Prioridad, Recomendacion, Evidencia, Responsable sugerido, Plazo sugerido.
- Los plazos pueden ser cualitativos ("Corto plazo", "Mediano plazo") pero la recomendacion debe nacer de los datos.
- No prometas resultados ni inventes responsables nominales.

### Contacto `#contacto`
- Usa una tarjeta visual corporativa.
- Como no hay contacto especifico en el JSON, usa:
  - Digital Tech Copiers S.A.S.
  - Equipo de Servicios Cloud y Seguridad
  - contacto@digitaltechcolombia.com
  - www.digitaltechcolombia.com
- No agregues telefonos si no vienen en el JSON.

## Reglas de fidelidad visual
- El resultado debe sentirse como un informe grafico premium, no como una pagina simple.
- No uses una paleta plana de un solo color: combina negro, azul profundo, blanco, gris claro y el acento del cliente.
- No pongas todo en tarjetas. Alterna secciones de ancho completo con tablas, listas y KPI cards.
- Mantén titulos grandes y jerarquia visual clara.
- Las tablas deben ser escaneables y profesionales.
- Los textos deben caber en sus contenedores en desktop y mobile.
- Usa `overflow-wrap: anywhere` donde pueda haber tenant IDs, URLs o nombres largos.
- Evita hero generico de marketing: la portada debe identificar claramente el cliente, periodo y alcance del informe.

## Manejo de informacion faltante
- Si una seccion no tiene datos suficientes, no la omitas: muestra una limitacion clara dentro de esa seccion.
- Usa frases como "No se encontro evidencia suficiente en el periodo para concluir este punto." o "No se encontro snapshot mensual de seguridad para este periodo.".
- Nunca pidas al usuario informacion adicional dentro del HTML. Este es un flujo automatico.

## Tono y estilo
- Profesional, tecnico, ejecutivo y orientado a resultados.
- Espanol empresarial claro.
- Evita relleno, exageraciones y afirmaciones absolutas.
- No menciones que recibiste un JSON, ni nombres de campos internos, ni reglas del prompt.
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
