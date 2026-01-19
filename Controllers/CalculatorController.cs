using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Calculator;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.Calculator;
using System.IO;

namespace CotizadorInterno.Web.Controllers;

public sealed class CalculatorController : Controller
{
    private readonly IDataverseService _dataverse;
    private readonly IQuoteCalculator _calculator;
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private const int SmbLicenseCap = 300;
    private const int CorporateMinimumLicenses = 300;
    public CalculatorController(IDataverseService dataverse, IQuoteCalculator calculator)
    {
        _dataverse = dataverse;
        _calculator = calculator;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var currentUser = await _dataverse.GetCurrentUserAsync(ct);
        var segment = currentUser?.Segment ?? UserSegment.Unknown;

        ViewData["Segment"] = segment;
        ViewData["CurrentUser"] = currentUser;
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

    [HttpPost]
    public async Task<IActionResult> Calculate([FromBody] QuoteScenarioInput input, CancellationToken ct)
    {
        // Segmento desde Dataverse (puedes cachearlo luego)
        var segment = await _dataverse.GetCurrentUserSegmentAsync(ct);
  var licenseValidation = ValidateLicenseCaps(input, segment);
        if (!string.IsNullOrWhiteSpace(licenseValidation))
        {
            return BadRequest(licenseValidation);
        }

        // Calcula (utility oculto, devuelve puntos+comisión)
        var result = _calculator.Calculate(input, segment);

        return Json(new
        {
            points = result.Points,
            commission = result.Commission,
            prorationDays = result.ProrationDays,
            prorationFactor = result.ProrationFactor,
            totalMonthlySale = result.TotalMonthlySale,
            totalSale = result.TotalSale,
            segment = segment.ToString()
        });
    }
    
    [HttpPost]
    public async Task<IActionResult> Export([FromBody] QuoteScenarioInput input, CancellationToken ct)
    {
        if (input?.Lines is null || input.Lines.Count == 0)
            return BadRequest("No hay líneas para exportar.");

        var segment = await _dataverse.GetCurrentUserSegmentAsync(ct);
           var licenseValidation = ValidateLicenseCaps(input, segment);
        if (!string.IsNullOrWhiteSpace(licenseValidation))
        {
            return BadRequest(licenseValidation);
        }
        var fileName = BuildFileName(input.ScenarioName);
        using var workbook = BuildWorkbook(input, segment);
        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

[HttpPost]
    public IActionResult ValidateProvisioning([FromBody] ProvisioningRequestInput input)
    {
        var validationError = ValidateProvisioningPayload(input);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return BadRequest(validationError);
        }

        return Ok(new { ok = true });
    }

    private static XLWorkbook BuildWorkbook(QuoteScenarioInput input, UserSegment segment)
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Cotización");

        var row = 1;
        sheet.Cell(row, 1).Value = "Escenario";
        sheet.Cell(row, 2).Value = input.ScenarioName;
        row++;

        sheet.Cell(row, 1).Value = "Segmento";
        sheet.Cell(row, 2).Value = segment.ToString();
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
            "Duración (meses)",
            "Venta UND",
            "Cantidad",
            "Venta Mensual",
            "Venta Total"
        };

        if (segment == UserSegment.Corporate)
        {
            headers.AddRange(new[]
            {
                "Desc. Corp UND",
                "Desc. Corp Mes",
                "Desc. Corp Año",
                "Ahorro Anual"
            });
        }

        headers.Add("Precio Sugerido");

        var headerRow = row;
        for (var i = 0; i < headers.Count; i++)
        {
            sheet.Cell(headerRow, i + 1).Value = headers[i];
        }

        sheet.Range(headerRow, 1, headerRow, headers.Count).Style.Font.Bold = true;
        row++;

        var idxSaleUnit = headers.IndexOf("Venta UND") + 1;
        var idxMonthly = headers.IndexOf("Venta Mensual") + 1;
        var idxTotal = headers.IndexOf("Venta Total") + 1;
        var idxSuggested = headers.IndexOf("Precio Sugerido") + 1;
        var idxDiscUnit = headers.IndexOf("Desc. Corp UND") + 1;
        var idxDiscMonth = headers.IndexOf("Desc. Corp Mes") + 1;
        var idxDiscYear = headers.IndexOf("Desc. Corp Año") + 1;
        var idxAhorro = headers.IndexOf("Ahorro Anual") + 1;

        decimal tSaleUnit = 0m, tMonthly = 0m, tTotal = 0m, tSuggested = 0m;
        decimal tDiscUnit = 0m, tDiscMonth = 0m, tDiscYear = 0m, tAhorro = 0m;

        foreach (var line in input.Lines)
        {
            var computed = ComputeLine(line, segment);

            sheet.Cell(row, 1).Value = line.BusinessType.ToString();
            sheet.Cell(row, 2).Value = line.ProductDescription;
            sheet.Cell(row, 3).Value = Round2(line.MarginPercent);
            sheet.Cell(row, 4).Value = line.ContractMonths;
            sheet.Cell(row, idxSaleUnit).Value = computed.SaleUnit;
            sheet.Cell(row, 6).Value = line.Quantity;
            sheet.Cell(row, idxMonthly).Value = computed.Monthly;
            sheet.Cell(row, idxTotal).Value = computed.Total;

            if (segment == UserSegment.Corporate)
            {
                sheet.Cell(row, idxDiscUnit).Value = computed.DiscUnit;
                sheet.Cell(row, idxDiscMonth).Value = computed.DiscMonth;
                sheet.Cell(row, idxDiscYear).Value = computed.DiscYear;
                sheet.Cell(row, idxAhorro).Value = computed.Ahorro;
            }

            sheet.Cell(row, idxSuggested).Value = Round2(line.SuggestedRetailPrice);

            tSaleUnit += computed.SaleUnit * line.Quantity;
            tMonthly += computed.Monthly;
            tTotal += computed.Total;
            tSuggested += line.SuggestedRetailPrice * line.Quantity;

            tDiscUnit += computed.DiscUnit * line.Quantity;
            tDiscMonth += computed.DiscMonth;
            tDiscYear += computed.DiscYear;
            tAhorro += computed.Ahorro;

            row++;
        }

        sheet.Cell(row, 1).Value = "Totales";
        sheet.Cell(row, 3).Value = "—";
        sheet.Cell(row, 4).Value = "—";
        sheet.Cell(row, idxSaleUnit).Value = Round2(tSaleUnit);
        sheet.Cell(row, 6).Value = "—";
        sheet.Cell(row, idxMonthly).Value = Round2(tMonthly);
        sheet.Cell(row, idxTotal).Value = Round2(tTotal);

        if (segment == UserSegment.Corporate)
        {
            sheet.Cell(row, idxDiscUnit).Value = Round2(tDiscUnit);
            sheet.Cell(row, idxDiscMonth).Value = Round2(tDiscMonth);
            sheet.Cell(row, idxDiscYear).Value = Round2(tDiscYear);
            sheet.Cell(row, idxAhorro).Value = Round2(tAhorro);
        }

        sheet.Cell(row, idxSuggested).Value = Round2(tSuggested);
        sheet.Range(headerRow + 1, 1, row, headers.Count).Style.NumberFormat.Format = "#,##0.00";
        sheet.Column(6).Style.NumberFormat.Format = "0";
        sheet.Column(4).Style.NumberFormat.Format = "0";
        sheet.Column(3).Style.NumberFormat.Format = "#,##0.00";
        sheet.Columns().AdjustToContents();

        return workbook;
    }

private static string? ValidateProvisioningPayload(ProvisioningRequestInput input)    {
        if (input.LineItems is null || input.LineItems.Count == 0)
        {
            return "No hay líneas para enviar.";
        }

        var attachment = input.Attachment;
        if (attachment is null)
        {
            return "Debes adjuntar la oferta autorizada o correo de aprobación.";
        }

        if (string.IsNullOrWhiteSpace(attachment.FileName) || string.IsNullOrWhiteSpace(attachment.Base64))
        {
            return "Debes adjuntar la oferta autorizada o correo de aprobación.";
        }

        var extension = Path.GetExtension(attachment.FileName).ToLowerInvariant().TrimStart('.');
        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pdf", "jpg", "jpeg", "doc", "docx"
        };
        if (!allowedExtensions.Contains(extension))
        {
            return "El adjunto debe ser PDF, JPG/JPEG o DOC/DOCX.";
        }

        var allowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf",
            "image/jpeg",
            "image/jpg",
            "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        };

        if (string.IsNullOrWhiteSpace(attachment.ContentType) || !allowedContentTypes.Contains(attachment.ContentType))
        {
            return "El adjunto debe ser PDF, JPG/JPEG o DOC/DOCX.";
        }

        try
        {
            _ = Convert.FromBase64String(attachment.Base64);
        }
        catch (FormatException)
        {
            return "El adjunto no es válido.";
        }

        return null;
    }

  private static string? ValidateLicenseCaps(QuoteScenarioInput input, UserSegment segment)
    {
        if (input.DealType == DealType.CrossSale)
            return null;

        if (input.Lines is null || input.Lines.Count == 0)
            return null;

        var restrictedTotal = input.Lines
            .Where(line => IsRestrictedProduct(line.ProductDescription))
            .Sum(line => line.Quantity);

if (segment == UserSegment.SMB && restrictedTotal >= SmbLicenseCap)        {
            return $"Para usuarios SMB, la suma de licencias con productos que contengan \"business\" o \"Microsoft 365\" no puede ser igual o mayor a {SmbLicenseCap}. Total actual: {restrictedTotal}.";
        }
if (segment == UserSegment.Corporate && restrictedTotal <= CorporateMinimumLicenses)
        {
            return $"Para usuarios Corporate, la suma de licencias con productos que contengan \"business\" o \"Microsoft 365\" debe ser mayor a {CorporateMinimumLicenses}. Total actual: {restrictedTotal}.";
        }

        return null;
    }

    private static bool IsRestrictedProduct(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return false;

        return description.Contains("business", StringComparison.OrdinalIgnoreCase)
            || description.Contains("microsoft 365", StringComparison.OrdinalIgnoreCase);
    }
    private static ExportLine ComputeLine(QuoteLineInput line, UserSegment segment)
    {
        var saleUnit = Round2(line.CostUnit * (1m + (line.MarginPercent / 100m)));
        var monthly = Round2(saleUnit * line.Quantity);
        var total = Round2(monthly * line.ContractMonths);

        decimal discUnit = 0m, discMonth = 0m, discYear = 0m, ahorro = 0m;
        if (segment == UserSegment.Corporate && line.BusinessType == BusinessType.ModernWork)
        {
            discUnit = Round2(saleUnit * 0.9m);
            discMonth = Round2(discUnit * line.Quantity);
            discYear = Round2(discMonth * line.ContractMonths);
            ahorro = Round2((saleUnit * line.Quantity * line.ContractMonths) - discYear);
        }

        return new ExportLine(saleUnit, monthly, total, discUnit, discMonth, discYear, ahorro);
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

    private sealed record ExportLine(decimal SaleUnit, decimal Monthly, decimal Total, decimal DiscUnit, decimal DiscMonth, decimal DiscYear, decimal Ahorro);
}
