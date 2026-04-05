using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Calculator;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.Calculator;
using System.Globalization;
using System.IO;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Calculator)]
public sealed class CalculatorController : Controller
{
    private readonly IDataverseService _dataverse;
    private readonly IQuoteCalculator _calculator;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CalculatorOptions _calculatorOptions;
    private readonly ILogger<CalculatorController> _logger;
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";

    public CalculatorController(
        IDataverseService dataverse,
        IQuoteCalculator calculator,
        IHttpClientFactory httpClientFactory,
        IOptions<CalculatorOptions> calculatorOptions,
        ILogger<CalculatorController> logger)
    {
        _dataverse = dataverse;
        _calculator = calculator;
        _httpClientFactory = httpClientFactory;
        _calculatorOptions = calculatorOptions.Value;
        _logger = logger;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var currentUser = await _dataverse.GetCurrentUserAsync(ct);
        var storedScenarios = await _dataverse.GetScenariosForUserAsync(ct);

        ViewData["CurrentUser"] = currentUser;
        ViewData["StoredScenarios"] = storedScenarios;
        return View();
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ProductSearch([FromQuery] string q, CancellationToken ct)
    {
        var items = await _dataverse.SearchProductsAsync(q, top: 12, ct: ct);
        return Json(items);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ClientSearch([FromQuery] string q, CancellationToken ct)
    {
        var items = await _dataverse.SearchClientsAsync(q, top: 12, ct: ct);
        return Json(items);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ClientRenewalDates([FromQuery] string clientId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return BadRequest(new
            {
                message = "Debes seleccionar un cliente valido.",
                detail = "El parametro clientId llego vacio.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        try
        {
            var items = await _dataverse.SearchRenewalDatesByClientAsync(clientId, top: 250, ct: ct);
            return Json(items);
        }
        catch (InvalidOperationException ex)
        {
            var traceId = HttpContext.TraceIdentifier;

            _logger.LogError(
                ex,
                "Error consultando fechas de renovacion para cliente {ClientId}. TraceId: {TraceId}.",
                clientId,
                traceId);

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "No se pudieron consultar las fechas de renovacion.",
                detail = BuildDiagnosticMessage(ex),
                traceId
            });
        }
        catch (Exception ex)
        {
            var traceId = HttpContext.TraceIdentifier;
            var detail = CompactDiagnosticMessage(ex.Message);

            _logger.LogError(
                ex,
                "Error inesperado consultando fechas de renovacion para cliente {ClientId}. TraceId: {TraceId}.",
                clientId,
                traceId);

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Ocurrio un error inesperado consultando las fechas de renovacion.",
                detail = string.IsNullOrWhiteSpace(detail)
                    ? "No se recibio detalle adicional del servidor."
                    : detail,
                traceId
            });
        }
    }

    [HttpPost]
    public IActionResult Calculate([FromBody] QuoteScenarioInput input)
    {
        if (input is null)
            return BadRequest("Payload invÃ¡lido.");

        NormalizeProrationRules(input);

        var licenseValidation = ValidateLicenseCaps(input);
        if (!string.IsNullOrWhiteSpace(licenseValidation))
            return BadRequest(licenseValidation);

        var result = _calculator.Calculate(input);

        return Json(new
        {
            points = result.Points,
            commission = result.Commission,
            prorationDays = result.ProrationDays,
            prorationFactor = result.ProrationFactor,
            totalMonthlySale = result.TotalMonthlySale,
            totalSale = result.TotalSale
        });
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SaveScenario([FromBody] ScenarioSaveRequest input, CancellationToken ct)
    {
        if (input is null)
            return BadRequest("Payload invÃ¡lido.");

        NormalizeProrationRules(input);

        if (string.IsNullOrWhiteSpace(input.ScenarioId))
            return BadRequest("ScenarioId requerido.");

        if (input.Lines is null || input.Lines.Count == 0)
            return BadRequest("Debe incluir lÃ­neas.");

        await _dataverse.UpsertScenarioAsync(input, ct);
        return Ok(new { ok = true });
    }

    [HttpDelete]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> DeleteScenario([FromQuery] string scenarioId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
            return BadRequest("ScenarioId requerido.");

        await _dataverse.DeleteScenarioAsync(scenarioId, ct);
        return Ok(new { ok = true });
    }

    [HttpPost]
    public IActionResult Export([FromBody] QuoteScenarioInput input)
    {
        if (input is null)
            return BadRequest("Payload invÃ¡lido.");

        NormalizeProrationRules(input);

        if (input.Lines is null || input.Lines.Count == 0)
            return BadRequest("No hay lÃ­neas para exportar.");

        var licenseValidation = ValidateLicenseCaps(input);
        if (!string.IsNullOrWhiteSpace(licenseValidation))
            return BadRequest(licenseValidation);

        var productValidation = ValidateSelectedProducts(input.Lines, "exportar el Excel");
        if (!string.IsNullOrWhiteSpace(productValidation))
            return BadRequest(productValidation);

        var fileName = BuildFileName(input.ScenarioName);
        using var workbook = BuildWorkbook(input);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    [HttpPost]
    public IActionResult ValidateProvisioning([FromBody] ProvisioningRequestInput? input)
    {
        if (input is null)
            return BadRequest("Payload invÃƒÂ¡lido.");

        var validationError = ValidateProvisioningPayload(input);
        if (!string.IsNullOrWhiteSpace(validationError))
            return BadRequest(validationError);

        return Ok(new { ok = true });
    }

    [HttpPost]
    public async Task<IActionResult> SubmitProvisioning([FromBody] ProvisioningRequestInput? input, CancellationToken ct)
    {
        if (input is null)
            return BadRequest("Payload invÃƒÂ¡lido.");

        var validationError = ValidateProvisioningPayload(input);
        if (!string.IsNullOrWhiteSpace(validationError))
            return BadRequest(validationError);

        if (string.IsNullOrWhiteSpace(_calculatorOptions.ProvisioningRequestFlowUrl))
        {
            return BadRequest("Configura la URL del flujo en Calculator:ProvisioningRequestFlowUrl antes de enviar la solicitud.");
        }

        var payload = BuildProvisioningFlowPayload(input);
        var client = _httpClientFactory.CreateClient();
        using var response = await client.PostAsJsonAsync(_calculatorOptions.ProvisioningRequestFlowUrl, payload, cancellationToken: ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return BadRequest(string.IsNullOrWhiteSpace(body)
                ? $"El flujo respondiÃ³ con error HTTP {(int)response.StatusCode}."
                : body);
        }

        return Ok(new { ok = true });
    }

    private static XLWorkbook BuildWorkbook(QuoteScenarioInput input)
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("CotizaciÃ³n");

        var row = 1;
        sheet.Cell(row, 1).Value = "Escenario";
        sheet.Cell(row, 2).Value = input.ScenarioName;
        row++;

        sheet.Cell(row, 1).Value = "Tipo de negocio";
        sheet.Cell(row, 2).Value = input.DealType.ToString();
        row++;

        if (input.RequiresProration)
        {
            sheet.Cell(row, 1).Value = "Prorrateo";
            sheet.Cell(row, 2).Value = input.StartDate.HasValue && input.EndDate.HasValue
                ? $"{input.StartDate:yyyy-MM-dd} al {input.EndDate:yyyy-MM-dd}"
                : "Pendiente fechas de prorrateo";
            row++;
        }

        row++;

        var headers = new List<string>
        {
            "Tipo",
            "Producto",
            "Margen %",
            "DuraciÃ³n (meses)",
            "Venta UND",
            "Cantidad",
            "Venta Mensual",
            "Venta Total",
            "Precio Sugerido"
        };

        var headerRow = row;
        for (var i = 0; i < headers.Count; i++)
            sheet.Cell(headerRow, i + 1).Value = headers[i];

        sheet.Range(headerRow, 1, headerRow, headers.Count).Style.Font.Bold = true;
        row++;

        var idxSaleUnit = headers.IndexOf("Venta UND") + 1;
        var idxMonthly = headers.IndexOf("Venta Mensual") + 1;
        var idxTotal = headers.IndexOf("Venta Total") + 1;
        var idxSuggested = headers.IndexOf("Precio Sugerido") + 1;

        decimal tSaleUnit = 0m, tMonthly = 0m, tTotal = 0m, tSuggested = 0m;

        foreach (var line in input.Lines)
        {
            var computed = ComputeLine(line);

            sheet.Cell(row, 1).Value = line.BusinessType.ToString();
            sheet.Cell(row, 2).Value = line.ProductDescription;
            sheet.Cell(row, 3).Value = Round2(line.MarginPercent);
            sheet.Cell(row, 4).Value = line.ContractMonths;
            sheet.Cell(row, idxSaleUnit).Value = computed.SaleUnit;
            sheet.Cell(row, 6).Value = line.Quantity;
            sheet.Cell(row, idxMonthly).Value = computed.Monthly;
            sheet.Cell(row, idxTotal).Value = computed.Total;
            sheet.Cell(row, idxSuggested).Value = Round2(line.SuggestedRetailPrice);

            tSaleUnit += computed.SaleUnit * line.Quantity;
            tMonthly += computed.Monthly;
            tTotal += computed.Total;
            tSuggested += line.SuggestedRetailPrice * line.Quantity;

            row++;
        }

        sheet.Cell(row, 1).Value = "Totales";
        sheet.Cell(row, 3).Value = "â€”";
        sheet.Cell(row, 4).Value = "â€”";
        sheet.Cell(row, idxSaleUnit).Value = Round2(tSaleUnit);
        sheet.Cell(row, 6).Value = "â€”";
        sheet.Cell(row, idxMonthly).Value = Round2(tMonthly);
        sheet.Cell(row, idxTotal).Value = Round2(tTotal);
        sheet.Cell(row, idxSuggested).Value = Round2(tSuggested);

        sheet.Range(headerRow + 1, 1, row, headers.Count).Style.NumberFormat.Format = "#,##0.00";
        sheet.Column(6).Style.NumberFormat.Format = "0";
        sheet.Column(4).Style.NumberFormat.Format = "0";
        sheet.Column(3).Style.NumberFormat.Format = "#,##0.00";
        sheet.Columns().AdjustToContents();

        return workbook;
    }

    private static string? ValidateProvisioningPayload(ProvisioningRequestInput input)
    {
        if (input.LineItems is null || input.LineItems.Count == 0)
            return "No hay lÃ­neas para enviar.";

        var productValidation = ValidateSelectedProducts(input.LineItems, "enviar la solicitud de aprovisionamiento");
        if (!string.IsNullOrWhiteSpace(productValidation))
            return productValidation;

        var attachment = input.Attachment;
        if (attachment is null)
            return "Debes adjuntar la oferta autorizada o correo de aprobaciÃ³n.";

        if (string.IsNullOrWhiteSpace(attachment.FileName) || string.IsNullOrWhiteSpace(attachment.Base64))
            return "Debes adjuntar la oferta autorizada o correo de aprobaciÃ³n.";

        var extension = Path.GetExtension(attachment.FileName).ToLowerInvariant().TrimStart('.');
        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pdf", "jpg", "jpeg", "doc", "docx"
        };
        if (!allowedExtensions.Contains(extension))
            return "El adjunto debe ser PDF, JPG/JPEG o DOC/DOCX.";

        var allowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf",
            "image/jpeg",
            "image/jpg",
            "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        };

        if (string.IsNullOrWhiteSpace(attachment.ContentType) || !allowedContentTypes.Contains(attachment.ContentType))
            return "El adjunto debe ser PDF, JPG/JPEG o DOC/DOCX.";

        try
        {
            _ = Convert.FromBase64String(attachment.Base64);
        }
        catch (FormatException)
        {
            return "El adjunto no es vÃ¡lido.";
        }

        return null;
    }

    private static object BuildProvisioningFlowPayload(ProvisioningRequestInput input)
    {
        var requester = input.Requester;
        var cliente = input.Cliente;
        var aprovisionamiento = input.Aprovisionamiento;
        var scenario = input.Scenario;
        var resultado = input.Resultado;
        var attachment = input.Attachment;
        var dealTypeLabel = ResolveDealTypeLabel(scenario);
        var normalizedScenarioStartDate = NormalizeDateLikeValue(scenario?.StartDate);
        var normalizedScenarioEndDate = NormalizeDateLikeValue(scenario?.EndDate);
        var lineItems = input.LineItems.Select(item => new ProvisioningFlowLinePayload
        {
            LineId = item.LineId?.Trim() ?? "",
            ProductoId = item.ProductoId?.Trim() ?? "",
            ProductoNombre = item.ProductoNombre?.Trim() ?? "",
            Cantidad = Round2(item.Cantidad),
            Number = Round2(item.Number),
            CostoUnd = Round2(item.CostoUnd),
            VentaUnd = Round2(item.VentaUnd),
            MargenPorcentaje = Round2(item.MargenPorcentaje),
            DuracionMeses = item.DuracionMeses,
            VentaMensual = Round2(item.VentaMensual),
            VentaTotal = Round2(item.VentaTotal),
            TieneIva = item.TieneIva,
            Tipo = item.Tipo?.Trim() ?? "",
            RequiereProrrateo = scenario?.RequiresProration ?? false,
            Inicio = normalizedScenarioStartDate,
            Final = normalizedScenarioEndDate
        }).ToList();
        var descriptionText = BuildProvisioningDescription(cliente, aprovisionamiento, scenario, resultado, lineItems, dealTypeLabel);

        return new
        {
            source = input.Source?.Trim() ?? "",
            businessId = input.BusinessId?.Trim() ?? "",
            requester = requester is null ? null : new
            {
                systemUserId = requester.SystemUserId?.Trim() ?? "",
                displayName = requester.DisplayName?.Trim() ?? "",
                email = requester.Email?.Trim() ?? ""
            },
            cliente = cliente is null ? null : new
            {
                clienteId = cliente.ClienteId?.Trim() ?? "",
                nombre = cliente.Nombre?.Trim() ?? ""
            },
            aprovisionamiento = aprovisionamiento is null ? null : new
            {
                fecha = aprovisionamiento.Fecha?.Trim() ?? "",
                tipoContratoCode = aprovisionamiento.TipoContratoCode?.Trim() ?? "",
                tipoContratoLabel = aprovisionamiento.TipoContratoLabel?.Trim() ?? ""
            },
            scenario = scenario is null ? null : new
            {
                dealTypeValue = scenario.DealTypeValue,
                dealTypeLabel,
                requiresProration = scenario.RequiresProration,
                startDate = normalizedScenarioStartDate,
                endDate = normalizedScenarioEndDate
            },
            resultado = resultado is null ? null : new
            {
                puntaje = resultado.Puntaje,
                comision = resultado.Comision,
                prorrateoDias = resultado.ProrrateoDias,
                prorrateoFactor = resultado.ProrrateoFactor,
                prorrateoTexto = resultado.ProrrateoTexto?.Trim() ?? "",
                ventaMensualTotal = resultado.VentaMensualTotal,
                ventaTotal = resultado.VentaTotal,
                ventaTotalAnual = resultado.VentaTotalAnual
            },
            descriptionText,
            lineItems = lineItems.Select(item => new
            {
                lineId = item.LineId,
                productoId = item.ProductoId,
                productoNombre = item.ProductoNombre,
                cantidad = item.Cantidad,
                number = item.Number,
                costoUnd = item.CostoUnd,
                ventaUnd = item.VentaUnd,
                margenPorcentaje = item.MargenPorcentaje,
                duracionMeses = item.DuracionMeses,
                ventaMensual = item.VentaMensual,
                ventaTotal = item.VentaTotal,
                tieneIva = item.TieneIva,
                tipo = item.Tipo,
                requiereProrrateo = item.RequiereProrrateo,
                inicio = item.Inicio,
                final = item.Final
            }),
            attachment = attachment is null ? null : new
            {
                fileName = attachment.FileName?.Trim() ?? "",
                contentType = attachment.ContentType?.Trim() ?? "",
                base64 = attachment.Base64 ?? ""
            }
        };
    }

    private static string BuildProvisioningDescription(
        ProvisioningClient? cliente,
        ProvisioningAprovisionamiento? aprovisionamiento,
        ProvisioningScenarioContext? scenario,
        ProvisioningResultado? resultado,
        IReadOnlyList<ProvisioningFlowLinePayload> lineItems,
        string dealTypeLabel)
    {
        var builder = new StringBuilder();
        var normalizedProvisioningDate = NormalizeDateLikeValue(aprovisionamiento?.Fecha, preferIsoWhenPossible: true);
        var normalizedScenarioStartDate = NormalizeDateLikeValue(scenario?.StartDate);
        var normalizedScenarioEndDate = NormalizeDateLikeValue(scenario?.EndDate);
        var requiresProration = scenario?.RequiresProration == true;

        builder.AppendLine($"Cliente: {cliente?.Nombre?.Trim() ?? ""}");
        builder.AppendLine($"Fecha aprovisionamiento: {normalizedProvisioningDate}");
        builder.AppendLine($"Tipo negocio: {dealTypeLabel}");
        builder.AppendLine($"Requiere prorrateo: {(requiresProration ? "Si" : "No")}");
        if (!string.IsNullOrWhiteSpace(normalizedScenarioStartDate))
            builder.AppendLine($"Inicio: {normalizedScenarioStartDate}");
        if (!string.IsNullOrWhiteSpace(normalizedScenarioEndDate))
            builder.AppendLine($"Final: {normalizedScenarioEndDate}");
        builder.AppendLine($"Puntaje: {FormatDecimalText(resultado?.Puntaje ?? 0m)}");
        builder.AppendLine($"Comisión: {FormatDecimalText(resultado?.Comision ?? 0m)}");
        builder.AppendLine($"Prorrateo: {(resultado?.ProrrateoTexto?.Trim() ?? (requiresProration ? "Si" : "No"))}");
        builder.AppendLine($"Venta mensual total: {FormatDecimalText(resultado?.VentaMensualTotal ?? 0m)}");
        builder.AppendLine($"Venta total anual: {FormatDecimalText(resultado?.VentaTotalAnual ?? resultado?.VentaTotal ?? 0m)}");
        builder.AppendLine();
        builder.AppendLine("Líneas:");
        builder.Append(JsonSerializer.Serialize(lineItems.Select(item => new
        {
            lineId = item.LineId,
            productoId = item.ProductoId,
            productoNombre = item.ProductoNombre,
            cantidad = item.Cantidad,
            number = item.Number,
            costoUnd = item.CostoUnd,
            ventaUnd = item.VentaUnd,
            margenPorcentaje = item.MargenPorcentaje,
            duracionMeses = item.DuracionMeses,
            ventaMensual = item.VentaMensual,
            ventaTotal = item.VentaTotal,
            tieneIva = item.TieneIva,
            tipo = item.Tipo,
            requiereProrrateo = item.RequiereProrrateo,
            inicio = item.Inicio,
            final = item.Final
        })));

        return builder.ToString();
    }

    private static string ResolveDealTypeLabel(ProvisioningScenarioContext? scenario)
    {
        if (!string.IsNullOrWhiteSpace(scenario?.DealTypeLabel))
            return scenario.DealTypeLabel.Trim();

        return scenario?.DealTypeValue switch
        {
            0 => "ClienteNuevo",
            1 => "CrossSale",
            2 => "Renovacion 1 vez",
            3 => "Renovacion 2 veces",
            4 => "Renovacion 3 veces o mas",
            _ => "ClienteNuevo"
        };
    }

    private static string NormalizeDateLikeValue(string? raw, bool preferIsoWhenPossible = false)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var trimmed = raw.Trim();
        if (!DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            && !DateTimeOffset.TryParse(trimmed, CultureInfo.GetCultureInfo("es-CO"), DateTimeStyles.AssumeUniversal, out parsed))
        {
            return trimmed;
        }

        return preferIsoWhenPossible
            ? parsed.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
            : parsed.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string FormatDecimalText(decimal value) =>
        Round2(value).ToString("0.##", CultureInfo.InvariantCulture);

    private static string? ValidateLicenseCaps(QuoteScenarioInput input)
    {
        return null;
    }

    private static string? ValidateSelectedProducts(IReadOnlyList<QuoteLineInput> lines, string actionLabel)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var productDescription = line.ProductDescription?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(productDescription))
                return $"La linea {index + 1} no tiene producto.";

            if (string.IsNullOrWhiteSpace(line.ProductId))
                return $"La linea {index + 1} debe seleccionar un producto valido de la lista antes de {actionLabel}.";
        }

        return null;
    }

    private static string? ValidateSelectedProducts(IReadOnlyList<ProvisioningLineItem> lines, string actionLabel)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var productName = line.ProductoNombre?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(productName))
                return $"La linea {index + 1} no tiene producto.";

            if (string.IsNullOrWhiteSpace(line.ProductoId))
                return $"La linea {index + 1} debe seleccionar un producto valido de la lista antes de {actionLabel}.";
        }

        return null;
    }

    private static void NormalizeProrationRules(QuoteScenarioInput input)
    {
        if (input.RequiresProration)
            input.DealType = DealType.CrossSale;
    }

    private static void NormalizeProrationRules(ScenarioSaveRequest input)
    {
        if (input.RequiresProration)
            input.DealType = (int)DealType.CrossSale;
    }

    private static ExportLine ComputeLine(QuoteLineInput line)
    {
        var saleUnit = Round2(line.CostUnit * (1m + (line.MarginPercent / 100m)));
        var monthly = Round2(saleUnit * line.Quantity);
        var total = Round2(monthly * line.ContractMonths);

        return new ExportLine(saleUnit, monthly, total);
    }

    private static decimal Round2(decimal v) =>
        Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static string BuildFileName(string? scenarioName)
    {
        var safe = string.Join("_", (scenarioName ?? "Cotizacion").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safe))
            safe = "Cotizacion";
        return $"{safe}.xlsx";
    }

    private sealed class ProvisioningFlowLinePayload
    {
        public string LineId { get; set; } = "";
        public string ProductoId { get; set; } = "";
        public string ProductoNombre { get; set; } = "";
        public decimal Cantidad { get; set; }
        public decimal Number { get; set; }
        public decimal CostoUnd { get; set; }
        public decimal VentaUnd { get; set; }
        public decimal MargenPorcentaje { get; set; }
        public int DuracionMeses { get; set; }
        public decimal VentaMensual { get; set; }
        public decimal VentaTotal { get; set; }
        public bool TieneIva { get; set; }
        public string Tipo { get; set; } = "";
        public bool RequiereProrrateo { get; set; }
        public string Inicio { get; set; } = "";
        public string Final { get; set; } = "";
    }

    private static string BuildDiagnosticMessage(Exception ex)
    {
        var messages = new List<string>();

        for (var current = ex; current is not null && messages.Count < 3; current = current.InnerException)
        {
            var message = CompactDiagnosticMessage(current.Message);
            if (string.IsNullOrWhiteSpace(message))
                continue;

            if (messages.Contains(message, StringComparer.OrdinalIgnoreCase))
                continue;

            messages.Add(message);
        }

        return messages.Count == 0
            ? "No se recibio detalle adicional del backend."
            : string.Join(" | ", messages);
    }

    private static string CompactDiagnosticMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var compact = string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return compact.Length > 500
            ? $"{compact[..497]}..."
            : compact;
    }

    private sealed record ExportLine(decimal SaleUnit, decimal Monthly, decimal Total);
}
