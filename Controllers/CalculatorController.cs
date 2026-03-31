using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Calculator;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.Calculator;
using System.IO;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Controllers;

public sealed class CalculatorController : Controller
{
    private readonly IDataverseService _dataverse;
    private readonly IQuoteCalculator _calculator;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CalculatorOptions _calculatorOptions;
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";

    public CalculatorController(
        IDataverseService dataverse,
        IQuoteCalculator calculator,
        IHttpClientFactory httpClientFactory,
        IOptions<CalculatorOptions> calculatorOptions)
    {
        _dataverse = dataverse;
        _calculator = calculator;
        _httpClientFactory = httpClientFactory;
        _calculatorOptions = calculatorOptions.Value;
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
        try
        {
            var items = await _dataverse.SearchRenewalDatesByClientAsync(clientId, top: 250, ct: ct);
            return Json(items);
        }
        catch (Exception)
        {
            return BadRequest("No se pudieron consultar las fechas de renovaciÃ³n.");
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
        var resultado = input.Resultado;
        var attachment = input.Attachment;

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
            lineItems = input.LineItems.Select(item => new
            {
                lineId = item.LineId?.Trim() ?? "",
                productoId = item.ProductoId?.Trim() ?? "",
                productoNombre = item.ProductoNombre?.Trim() ?? "",
                cantidad = RoundWholeNumber(item.Cantidad),
                number = RoundWholeNumber(item.Number),
                costoUnd = RoundWholeNumber(item.CostoUnd),
                ventaUnd = RoundWholeNumber(item.VentaUnd),
                margenPorcentaje = item.MargenPorcentaje,
                duracionMeses = item.DuracionMeses,
                ventaMensual = RoundWholeNumber(item.VentaMensual),
                ventaTotal = RoundWholeNumber(item.VentaTotal)
            }),
            attachment = attachment is null ? null : new
            {
                fileName = attachment.FileName?.Trim() ?? "",
                contentType = attachment.ContentType?.Trim() ?? "",
                base64 = attachment.Base64 ?? ""
            }
        };
    }

    private static string? ValidateLicenseCaps(QuoteScenarioInput input)
    {
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

    private static int RoundWholeNumber(decimal value) =>
        (int)Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static string BuildFileName(string? scenarioName)
    {
        var safe = string.Join("_", (scenarioName ?? "Cotizacion").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safe))
            safe = "Cotizacion";
        return $"{safe}.xlsx";
    }

    private sealed record ExportLine(decimal SaleUnit, decimal Monthly, decimal Total);
}
