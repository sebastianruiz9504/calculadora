using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.SoporteCloud;
using CotizadorInterno.Web.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using System.Globalization;
using System.Text;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Dashboard)]
public sealed class DashboardController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private static readonly CultureInfo PdfCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly Encoding PdfEncoding = Encoding.Latin1;
    private readonly IDataverseService _dataverse;
    private readonly ISiigoService _siigo;

    public DashboardController(IDataverseService dataverse, ISiigoService siigo)
    {
        _dataverse = dataverse;
        _siigo = siigo;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var today = ResolveBogotaToday();
        var model = new DashboardPageViewModel
        {
            CurrentUser = await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo(),
            InitialYear = today.Year,
            InitialPeriodKind = BillingPeriodKind.Month,
            InitialPeriodValue = today.Month,
            InitialSupportStartDate = new DateOnly(today.Year, today.Month, 1).ToString("yyyy-MM-dd"),
            InitialSupportEndDate = today.ToString("yyyy-MM-dd")
        };

        return View(model);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Billing([FromQuery] int? year, [FromQuery] string? period, [FromQuery] int? value, CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            var dashboard = await _dataverse.GetBillingDashboardAsync(
                year ?? today.Year,
                BillingPeriodKindExtensions.ParseOrDefault(period),
                value,
                ct);

            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el dashboard de facturacion.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> BillingInvoices(CancellationToken ct)
    {
        try
        {
            return Json(await _dataverse.GetBillingInvoicesAsync(ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar la tabla completa de facturacion.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> BillingInvoice([FromBody] BillingInvoiceSaveRequestDto request, CancellationToken ct)
    {
        try
        {
            return Json(await _dataverse.SaveBillingInvoiceAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible actualizar la factura en Dataverse.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> BillingInvoicesDelete([FromBody] BillingInvoicesDeleteRequestDto request, CancellationToken ct)
    {
        try
        {
            return Json(await _dataverse.DeleteBillingInvoicesAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible eliminar las facturas seleccionadas.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> BillingInvoicesContractType([FromBody] BillingInvoicesContractTypeUpdateRequestDto request, CancellationToken ct)
    {
        try
        {
            return Json(await _dataverse.UpdateBillingInvoicesContractTypeAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cambiar el tipo de contrato.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> BillingClientReport([FromQuery] string clientId, [FromQuery] string? clientName, CancellationToken ct)
    {
        try
        {
            var detail = await _dataverse.GetBillingClientReportAsync(clientId, clientName, ct);
            return Json(detail);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar las facturas del cliente.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> BillingClientReportExport([FromBody] BillingClientReportExportRequestDto request, CancellationToken ct)
    {
        try
        {
            if (request is null || request.Items is null || request.Items.Count == 0)
                return BadRequest("Selecciona al menos una factura para exportar.");

            var detail = await _dataverse.GetBillingClientReportAsync(request.ClientId, request.ClientName, ct);
            var requestedItems = request.Items
                .Where(static item => !string.IsNullOrWhiteSpace(item.RecordId))
                .GroupBy(static item => item.RecordId.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

            var selectedInvoices = detail.Invoices
                .Where(invoice => requestedItems.ContainsKey(invoice.RecordId))
                .ToList();

            if (selectedInvoices.Count == 0)
                return BadRequest("No encontramos las facturas seleccionadas para este cliente.");

            var exportRows = new List<(BillingClientReportInvoiceDto Invoice, decimal ExportAmount, decimal Fraction)>();
            foreach (var invoice in selectedInvoices)
            {
                var requested = requestedItems[invoice.RecordId];
                var exportAmount = Math.Round(requested.ExportAmount ?? invoice.TotalInvoice, 2, MidpointRounding.AwayFromZero);

                if (exportAmount < 0m || exportAmount > invoice.TotalInvoice)
                {
                    return BadRequest($"El valor a exportar de la factura {invoice.InvoiceNumber} debe estar entre 0 y el total de la factura.");
                }

                var fraction = invoice.TotalInvoice == 0m
                    ? 0m
                    : Math.Round(exportAmount / invoice.TotalInvoice, 4, MidpointRounding.AwayFromZero);
                exportRows.Add((invoice, exportAmount, fraction));
            }

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Facturas cliente");
            var totalExported = exportRows.Sum(static row => row.ExportAmount);

            worksheet.Cell(1, 1).Value = "Reporte de facturas por cliente";
            worksheet.Cell(2, 1).Value = "Cliente";
            worksheet.Cell(2, 2).Value = detail.ClientName;
            worksheet.Cell(3, 1).Value = "Facturas seleccionadas";
            worksheet.Cell(3, 2).Value = exportRows.Count;
            worksheet.Cell(4, 1).Value = "Total reportado";
            worksheet.Cell(4, 2).Value = totalExported;

            var headers = new[]
            {
                "Factura",
                "Cliente",
                "NIT empresa",
                "% IVA",
                "Valor IVA",
                "Total factura",
                "Valor reportado",
                "Fraccion",
                "Fecha emision",
                "Vertical",
                "Contrato",
                "URL factura"
            };

            for (var index = 0; index < headers.Length; index++)
            {
                worksheet.Cell(6, index + 1).Value = headers[index];
            }

            var rowIndex = 7;
            foreach (var row in exportRows)
            {
                worksheet.Cell(rowIndex, 1).Value = row.Invoice.InvoiceNumber;
                worksheet.Cell(rowIndex, 2).Value = row.Invoice.ClientName;
                worksheet.Cell(rowIndex, 3).Value = row.Invoice.CompanyTaxId;
                worksheet.Cell(rowIndex, 4).Value = row.Invoice.VatPercent;
                worksheet.Cell(rowIndex, 5).Value = row.Invoice.VatValue;
                worksheet.Cell(rowIndex, 6).Value = row.Invoice.TotalInvoice;
                worksheet.Cell(rowIndex, 7).Value = row.ExportAmount;
                worksheet.Cell(rowIndex, 8).Value = row.Fraction;
                worksheet.Cell(rowIndex, 9).Value = row.Invoice.EmissionDateDisplay;
                worksheet.Cell(rowIndex, 10).Value = row.Invoice.VerticalLabel;
                worksheet.Cell(rowIndex, 11).Value = row.Invoice.ContractTypeLabel;
                worksheet.Cell(rowIndex, 12).Value = row.Invoice.PublicUrl;

                if (Uri.TryCreate(row.Invoice.PublicUrl, UriKind.Absolute, out _))
                {
                    worksheet.Cell(rowIndex, 12).SetHyperlink(new XLHyperlink(row.Invoice.PublicUrl));
                }

                rowIndex++;
            }

            worksheet.Cell(rowIndex, 1).Value = "Total";
            worksheet.Cell(rowIndex, 2).Value = $"{exportRows.Count:N0} facturas";
            worksheet.Cell(rowIndex, 6).Value = exportRows.Sum(static row => row.Invoice.TotalInvoice);
            worksheet.Cell(rowIndex, 7).Value = totalExported;

            var usedRange = worksheet.Range(1, 1, rowIndex, headers.Length);
            usedRange.Style.Font.FontName = "Aptos";
            worksheet.Range(1, 1, 1, headers.Length).Merge().Style.Font.Bold = true;
            worksheet.Range(6, 1, 6, headers.Length).Style.Font.Bold = true;
            worksheet.Range(rowIndex, 1, rowIndex, headers.Length).Style.Font.Bold = true;
            worksheet.Range(rowIndex, 1, rowIndex, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F9FF");
            worksheet.Range(4, 2, 4, 2).Style.NumberFormat.Format = "$ #,##0";
            worksheet.Range(7, 5, rowIndex, 7).Style.NumberFormat.Format = "$ #,##0";
            worksheet.Range(7, 8, rowIndex, 8).Style.NumberFormat.Format = "0.00%";
            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var fileName = $"reporte-facturas-{BuildSafeFileName(detail.ClientName)}-{ResolveBogotaToday():yyyyMMdd}.xlsx";

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible exportar el reporte de facturas.");
        }
    }

    [HttpGet]
    public async Task<IActionResult> SiigoCustomers(CancellationToken ct)
    {
        try
        {
            var items = await _siigo.GetCustomersAsync(ct);
            return Json(items);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest($"No fue posible cargar el listado de clientes desde Siigo. Detalle: {ex.Message}");
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                $"No fue posible cargar el listado de clientes desde Siigo. Detalle tecnico: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [HttpGet]
    public async Task<IActionResult> SiigoCustomerSearch([FromQuery] string q, CancellationToken ct)
    {
        try
        {
            var items = await _siigo.SearchCustomersAsync(q, top: 12, ct);
            return Json(items);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible buscar clientes en Siigo.");
        }
    }

    [HttpGet]
    public async Task<IActionResult> SiigoInvoices(
        [FromQuery] string? customerId,
        [FromQuery] string? customerQuery,
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        CancellationToken ct)
    {
        try
        {
            if (startDate is null || endDate is null)
                return BadRequest("Selecciona la fecha inicial y final para consultar Siigo.");

            var detail = await _siigo.GetInvoicesAsync(customerId, customerQuery, startDate.Value, endDate.Value, ct);
            return Json(detail);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible consultar facturas en Siigo.");
        }
    }

    [HttpPost]
    public async Task<IActionResult> SiigoInvoicesDownload([FromBody] SiigoInvoiceDownloadRequestDto request, CancellationToken ct)
    {
        try
        {
            if (request is null || request.Invoices is null || request.Invoices.Count == 0)
                return BadRequest("Selecciona al menos una factura para descargar.");

            var download = await _siigo.DownloadInvoicePdfsAsync(request.Invoices, ct);
            return File(download.Content, download.ContentType, download.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible descargar las facturas desde Siigo.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Portfolio(CancellationToken ct)
    {
        try
        {
            var dashboard = await _dataverse.GetPortfolioDashboardAsync(ct);
            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el dashboard de cartera.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Business(CancellationToken ct)
    {
        try
        {
            var dashboard = await _dataverse.GetBusinessDashboardAsync(ct);
            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el dashboard de negocios.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Copiers(CancellationToken ct)
    {
        try
        {
            var dashboard = await _dataverse.GetCopiersDashboardAsync(ct);
            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el dashboard de facturacion copiers.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CopiersClientInvoices([FromQuery] string clientId, [FromQuery] string? clientName, CancellationToken ct)
    {
        try
        {
            var detail = await _dataverse.GetCopiersClientInvoicesAsync(clientId, clientName, ct);
            return Json(detail);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar las facturas emitidas del cliente.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CopiersEquipment(CancellationToken ct)
    {
        try
        {
            var dashboard = await _dataverse.GetCopiersEquipmentDashboardAsync(ct);
            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el dashboard de equipos copiers.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CopiersInventory(CancellationToken ct)
    {
        try
        {
            var dashboard = await _dataverse.GetCopiersCommercialInventoryAsync(ct);
            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el inventario comercial copiers.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CopiersInventoryExport(CancellationToken ct)
    {
        try
        {
            var dashboard = await _dataverse.GetCopiersCommercialInventoryAsync(ct);
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Inventario");

            worksheet.Cell(1, 1).Value = "Inventario comercial Copiers";
            worksheet.Cell(2, 1).Value = "Corte";
            worksheet.Cell(2, 2).Value = dashboard.AsOfDateLabel;
            worksheet.Cell(3, 1).Value = "Total equipos";
            worksheet.Cell(3, 2).Value = dashboard.RecordsCount;
            worksheet.Cell(3, 3).Value = "Valor comercial total";
            worksheet.Cell(3, 4).Value = dashboard.TotalCommercialValue;

            var headers = new[]
            {
                "Serial",
                "Referencia",
                "Valor comercial",
                "Fuente",
                "Cliente / estado",
                "Categoria"
            };

            for (var index = 0; index < headers.Length; index++)
            {
                worksheet.Cell(5, index + 1).Value = headers[index];
            }

            var rowIndex = 6;
            foreach (var row in dashboard.Records)
            {
                worksheet.Cell(rowIndex, 1).Value = row.Serial;
                worksheet.Cell(rowIndex, 2).Value = row.Reference;
                if (row.EffectiveCommercialValue.HasValue)
                    worksheet.Cell(rowIndex, 3).Value = row.EffectiveCommercialValue.Value;
                worksheet.Cell(rowIndex, 4).Value = row.CommercialValueSource;
                worksheet.Cell(rowIndex, 5).Value = row.InStock ? "Stock" : row.ClientName;
                worksheet.Cell(rowIndex, 6).Value = row.CategoryLabel;
                rowIndex++;
            }

            if (dashboard.Records.Count == 0)
            {
                worksheet.Cell(rowIndex, 1).Value = "No hay equipos registrados.";
                worksheet.Range(rowIndex, 1, rowIndex, headers.Length).Merge();
            }

            var lastRow = Math.Max(rowIndex - 1, 6);
            worksheet.Range(1, 1, 1, headers.Length).Merge().Style.Font.Bold = true;
            worksheet.Range(5, 1, 5, headers.Length).Style.Font.Bold = true;
            worksheet.Range(5, 1, 5, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F9FF");
            worksheet.Range(3, 4, Math.Max(3, lastRow), 4).Style.NumberFormat.Format = "$ #,##0";
            worksheet.Range(6, 3, Math.Max(6, lastRow), 3).Style.NumberFormat.Format = "$ #,##0";
            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var fileName = $"inventario-comercial-copiers-{ResolveBogotaToday():yyyyMMdd}.xlsx";

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible exportar el inventario comercial copiers.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CopiersCounters([FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? clientId, [FromQuery] string? clientName, CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            var dashboard = await _dataverse.GetCopiersCountersDashboardAsync(
                year ?? today.Year,
                month ?? today.Month,
                clientId,
                clientName,
                ct);

            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el consumo de contadores copiers.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CopiersCountersPdf([FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? clientId, [FromQuery] string? clientName, CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            var dashboard = await _dataverse.GetCopiersCountersDashboardAsync(
                year ?? today.Year,
                month ?? today.Month,
                clientId,
                clientName,
                ct);

            var content = BuildCopiersCountersPdf(dashboard);
            var periodToken = string.IsNullOrWhiteSpace(dashboard.PeriodValue)
                ? $"{dashboard.Year:D4}-{dashboard.Month:D2}"
                : dashboard.PeriodValue;
            var clientToken = BuildSafeFileName(FirstNonEmpty(dashboard.SelectedClientName, "todos-los-clientes"));
            var fileName = $"consumo-copiers-{periodToken}-{clientToken}.pdf";

            return File(content, "application/pdf", fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible exportar el reporte mensual de consumo copiers.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CopiersEquipmentDetail([FromQuery] string equipmentId, CancellationToken ct)
    {
        try
        {
            var detail = await _dataverse.GetCopiersEquipmentDetailAsync(equipmentId, ct);
            return Json(detail);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el detalle del equipo.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CopiersLineEquipmentAssignment([FromQuery] string lineId, [FromQuery] string? clientId, CancellationToken ct)
    {
        try
        {
            var detail = await _dataverse.GetCopiersLineEquipmentAssignmentAsync(lineId, clientId, ct);
            return Json(detail);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar la asignacion de equipos de la linea.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CopiersClientSearch([FromQuery] string q, CancellationToken ct)
    {
        var items = await _dataverse.SearchClientsAsync(q, top: 12, ct: ct);
        return Json(items);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CopiersProductSearch([FromQuery] string q, CancellationToken ct)
    {
        var items = await _dataverse.SearchProductsAsync(q, top: 12, ct: ct);
        return Json(items);
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CopiersRecord([FromBody] CopiersRecordSaveRequestDto request, CancellationToken ct)
    {
        try
        {
            var result = await _dataverse.SaveCopiersRecordAsync(request, ct);
            return Json(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible guardar el registro de facturacion copiers.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CopiersLineEquipmentAssignment([FromBody] CopiersLineEquipmentAssignmentSaveRequestDto request, CancellationToken ct)
    {
        try
        {
            var result = await _dataverse.SaveCopiersLineEquipmentAssignmentAsync(request, ct);
            return Json(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible guardar la asignacion de equipos a la linea.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CopiersEquipmentAssignment([FromBody] CopiersEquipmentAssignmentRequestDto request, CancellationToken ct)
    {
        try
        {
            var result = await _dataverse.SaveCopiersEquipmentAssignmentAsync(request, ct);
            return Json(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible actualizar la asignacion del equipo.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CopiersMaintenanceAttachment([FromQuery] string maintenanceId, CancellationToken ct)
    {
        try
        {
            var file = await _dataverse.DownloadCopiersMaintenanceAttachmentAsync(maintenanceId, ct);
            if (file is null)
                return NotFound();

            return File(file.Content, file.ContentType, file.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible descargar el adjunto del mantenimiento.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SupportCloud([FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate, CancellationToken ct)
    {
        try
        {
            var dashboard = await _dataverse.GetSoporteCloudBoardAsync(startDate, endDate, ct);
            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el dashboard de soporte cloud.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SupportCloudTrainings([FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate, CancellationToken ct)
    {
        try
        {
            var dashboard = await _dataverse.GetSoporteCloudTrainingsBoardAsync(startDate, endDate, ct);
            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar las capacitaciones de soporte cloud.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SupportCloudClientSearch([FromQuery] string q, CancellationToken ct)
    {
        var items = await _dataverse.SearchClientsAsync(q, top: 12, ct: ct);
        return Json(items);
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SupportCloudTicket([FromBody] SoporteCloudSaveRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _dataverse.SaveSoporteCloudTicketAsync(request, ct);
            return Json(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible guardar el ticket de soporte cloud.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [RequestSizeLimit(134217728)]
    [RequestFormLimits(MultipartBodyLengthLimit = 134217728)]
    public async Task<IActionResult> SupportCloudUploadFile(string recordId, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
            return BadRequest("Debes seleccionar un archivo valido.");

        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);

            var result = await _dataverse.UploadSoporteCloudAttachmentAsync(
                recordId,
                file.FileName,
                file.ContentType,
                buffer.ToArray(),
                ct);

            return Json(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el adjunto del ticket.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SupportCloudDownloadFile(string recordId, CancellationToken ct)
    {
        try
        {
            var file = await _dataverse.DownloadSoporteCloudAttachmentAsync(recordId, ct);
            if (file is null || file.Content.Length == 0)
                return NotFound();

            return File(file.Content, file.ContentType, file.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible descargar el adjunto del ticket.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Taxes([FromQuery] int? year, [FromQuery] string? period, [FromQuery] int? value, CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            var dashboard = await _dataverse.GetTaxesDashboardAsync(
                year ?? today.Year,
                BillingPeriodKindExtensions.ParseOrDefault(period),
                value,
                ct);

            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el dashboard de impuestos.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> TaxesRetentionsExport([FromQuery] int? year, [FromQuery] string? period, [FromQuery] int? value, CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            var dashboard = await _dataverse.GetTaxesDashboardAsync(
                year ?? today.Year,
                BillingPeriodKindExtensions.ParseOrDefault(period),
                value,
                ct);

            var rows = dashboard.ReteFuente.RetentionDetails;
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Retenciones");

            worksheet.Cell(1, 1).Value = "Detalle retenciones retefuente";
            worksheet.Cell(2, 1).Value = dashboard.ReteFuente.PeriodLabel;
            worksheet.Cell(2, 2).Value = dashboard.ReteFuente.DateRangeLabel;

            var headers = new[]
            {
                "Fecha pago",
                "Valor pago",
                "Retefuente",
                "Tipo persona",
                "Receptor",
                "NIT receptor",
                "Cloud",
                "Copiers"
            };

            for (var index = 0; index < headers.Length; index++)
            {
                worksheet.Cell(4, index + 1).Value = headers[index];
            }

            var rowIndex = 5;
            foreach (var row in rows)
            {
                worksheet.Cell(rowIndex, 1).Value = row.PaymentDateDisplay;
                worksheet.Cell(rowIndex, 2).Value = row.PaymentValue;
                worksheet.Cell(rowIndex, 3).Value = row.ReteFuenteValue;
                worksheet.Cell(rowIndex, 4).Value = row.PersonTypeLabel;
                worksheet.Cell(rowIndex, 5).Value = row.RecipientName;
                worksheet.Cell(rowIndex, 6).Value = row.RecipientNit;
                worksheet.Cell(rowIndex, 7).Value = row.CloudValue;
                worksheet.Cell(rowIndex, 8).Value = row.CopiersValue;
                rowIndex++;
            }

            worksheet.Cell(rowIndex, 1).Value = "Total";
            worksheet.Cell(rowIndex, 2).Value = rows.Sum(static row => row.PaymentValue);
            worksheet.Cell(rowIndex, 3).Value = rows.Sum(static row => row.ReteFuenteValue);
            worksheet.Cell(rowIndex, 4).Value = $"{rows.Count:N0} registros";
            worksheet.Cell(rowIndex, 7).Value = rows.Sum(static row => row.CloudValue);
            worksheet.Cell(rowIndex, 8).Value = rows.Sum(static row => row.CopiersValue);

            var usedRange = worksheet.Range(1, 1, rowIndex, headers.Length);
            usedRange.Style.Font.FontName = "Aptos";
            worksheet.Range(4, 1, 4, headers.Length).Style.Font.Bold = true;
            worksheet.Range(rowIndex, 1, rowIndex, headers.Length).Style.Font.Bold = true;
            worksheet.Range(rowIndex, 1, rowIndex, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F9FF");
            worksheet.Range(5, 2, rowIndex, 3).Style.NumberFormat.Format = "$ #,##0";
            worksheet.Range(5, 7, rowIndex, 8).Style.NumberFormat.Format = "$ #,##0";
            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var fileName = $"retenciones-retefuente-{dashboard.Year}-{dashboard.ReteFuente.PeriodLabel}.xlsx";

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible descargar el detalle de retenciones.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Licenciamiento([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            var dashboard = await _dataverse.GetLicenciamientoDashboardAsync(
                year ?? Math.Max(today.Year - 1, 2000),
                month,
                ct);

            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el dashboard de licenciamiento.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Pnl([FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? vertical, CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            var dashboard = await _dataverse.GetPnlDashboardAsync(
                year ?? today.Year,
                month,
                vertical,
                ct);

            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el dashboard P&L.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> PnlDetail(
        [FromQuery] int? year,
        [FromQuery(Name = "cutoffMonth")] int? monthCutoff,
        [FromQuery] string? vertical,
        [FromQuery] string? rowKey,
        [FromQuery] int? cellMonth,
        CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            var detail = await _dataverse.GetPnlCellDetailAsync(
                year ?? today.Year,
                monthCutoff,
                vertical,
                rowKey ?? "",
                cellMonth,
                ct);

            return Json(detail);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el detalle de la celda P&L.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> PnlDetailRecord([FromBody] PnlDetailRecordUpdateRequestDto request, CancellationToken ct)
    {
        try
        {
            var result = await _dataverse.UpdatePnlDetailRecordAsync(request, ct);
            return Json(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible actualizar el registro del detalle P&L.");
        }
    }

    private static DateOnly ResolveBogotaToday()
    {
        var utcNow = DateTimeOffset.UtcNow;
        foreach (var timeZoneId in new[] { "SA Pacific Standard Time", "America/Bogota" })
        {
            try
            {
                var timezone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, timezone).DateTime);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return DateOnly.FromDateTime(utcNow.UtcDateTime);
    }

    private static byte[] BuildCopiersCountersPdf(CopiersCountersDashboardDto dashboard)
    {
        const double pageWidth = 841.89;
        const double pageHeight = 595.28;
        const double marginX = 24;
        const double topY = 572;
        const double tableTopY = 505;
        const double bottomY = 34;
        const double headerHeight = 19;
        const double rowHeight = 15.5;

        var rows = (dashboard.EquipmentRows ?? Array.Empty<CopiersCountersEquipmentRowDto>()).ToList();
        var columns = new[]
        {
            new PdfTableColumn("Equipo", 90, false, row => FirstNonEmpty(row.EquipmentName, "Sin equipo")),
            new PdfTableColumn("Ubicacion", 90, false, row => row.Area),
            new PdfTableColumn("Fecha ant.", 58, false, row => FirstNonEmpty(row.PreviousDateDisplay, "-")),
            new PdfTableColumn("Fecha act.", 58, false, row => FirstNonEmpty(row.CurrentDateDisplay, "-")),
            new PdfTableColumn("Act. copias", 65, true, row => FormatPdfNumber(row.CurrentCopiesCounter)),
            new PdfTableColumn("Ant. copias", 65, true, row => FormatPdfNumber(row.PreviousCopiesCounter)),
            new PdfTableColumn("Copias", 55, true, row => FormatPdfNumber(row.CopiesConsumption)),
            new PdfTableColumn("Act. esc.", 65, true, row => FormatPdfNumber(row.CurrentScansCounter)),
            new PdfTableColumn("Ant. esc.", 65, true, row => FormatPdfNumber(row.PreviousScansCounter)),
            new PdfTableColumn("Escaneos", 55, true, row => FormatPdfNumber(row.ScansConsumption)),
            new PdfTableColumn("Dias", 40, true, row => FormatPdfNumber(row.DaysBetweenReadings)),
            new PdfTableColumn("Total", 54, true, row => FormatPdfNumber(row.TotalConsumption))
        };

        var tableWidth = columns.Sum(static column => column.Width);
        var tableX = Math.Max(marginX, (pageWidth - tableWidth) / 2);
        var rowsPerPage = Math.Max(1, (int)Math.Floor((tableTopY - headerHeight - bottomY) / rowHeight));
        var totalPages = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)rowsPerPage));
        var pages = new List<string>(totalPages);

        for (var pageIndex = 0; pageIndex < totalPages; pageIndex++)
        {
            var content = new StringBuilder();
            var pageRows = rows
                .Skip(pageIndex * rowsPerPage)
                .Take(rowsPerPage)
                .ToList();

            AppendPdfText(content, "Reporte mensual de consumo Copiers", marginX, topY, 13, "F2", 360);
            AppendPdfText(
                content,
                $"Periodo: {FirstNonEmpty(dashboard.PeriodLabel, $"{dashboard.Year:D4}-{dashboard.Month:D2}")} | Rango: {FirstNonEmpty(dashboard.DateRangeLabel, "-")}",
                marginX,
                topY - 16,
                7.5,
                "F1",
                470);
            AppendPdfText(
                content,
                $"Cliente: {FirstNonEmpty(dashboard.SelectedClientName, "Todos los clientes")} | Equipos: {rows.Count.ToString("N0", PdfCulture)} | Corte: {FirstNonEmpty(dashboard.AsOfDateLabel, "-")}",
                marginX,
                topY - 29,
                7.5,
                "F1",
                620);
            AppendPdfText(
                content,
                $"Pagina {pageIndex + 1} de {totalPages}",
                pageWidth - marginX - 80,
                topY - 29,
                7.5,
                "F1",
                80,
                alignRight: true);

            AppendPdfRect(content, tableX, tableTopY - headerHeight, tableWidth, headerHeight, "0.91 0.95 1", fill: true);

            var currentX = tableX;
            foreach (var column in columns)
            {
                AppendPdfCellBorder(content, currentX, tableTopY - headerHeight, column.Width, headerHeight);
                AppendPdfText(
                    content,
                    column.Header,
                    currentX + 3,
                    tableTopY - 12.5,
                    6.4,
                    "F2",
                    column.Width - 6,
                    alignRight: column.AlignRight);
                currentX += column.Width;
            }

            var currentY = tableTopY - headerHeight;
            if (rows.Count == 0)
            {
                var rowY = currentY - rowHeight;
                AppendPdfCellBorder(content, tableX, rowY, tableWidth, rowHeight);
                AppendPdfText(content, "No hay equipos con lecturas para el periodo seleccionado.", tableX + 4, rowY + 5, 7, "F1", tableWidth - 8);
            }
            else
            {
                for (var rowIndex = 0; rowIndex < pageRows.Count; rowIndex++)
                {
                    var row = pageRows[rowIndex];
                    var rowY = currentY - rowHeight;
                    if (rowIndex % 2 == 1)
                    {
                        AppendPdfRect(content, tableX, rowY, tableWidth, rowHeight, "0.98 0.99 1", fill: true);
                    }

                    currentX = tableX;
                    foreach (var column in columns)
                    {
                        AppendPdfCellBorder(content, currentX, rowY, column.Width, rowHeight);
                        AppendPdfText(
                            content,
                            FirstNonEmpty(column.ValueSelector(row), "-"),
                            currentX + 3,
                            rowY + 5,
                            6,
                            "F1",
                            column.Width - 6,
                            alignRight: column.AlignRight);
                        currentX += column.Width;
                    }

                    currentY = rowY;
                }
            }

            AppendPdfText(
                content,
                "Tabla generada desde Dashboard > Copiers > Contadores.",
                marginX,
                18,
                6.5,
                "F1",
                pageWidth - (marginX * 2));

            pages.Add(content.ToString());
        }

        return BuildPdfDocument(pages, pageWidth, pageHeight);
    }

    private static byte[] BuildPdfDocument(IReadOnlyList<string> pageContents, double pageWidth, double pageHeight)
    {
        var pageCount = Math.Max(1, pageContents.Count);
        var objectCount = 4 + (pageCount * 2);
        var offsets = new long[objectCount + 1];

        using var stream = new MemoryStream();
        WritePdfString(stream, "%PDF-1.4\n");

        WritePdfObject(stream, offsets, 1, "<< /Type /Catalog /Pages 2 0 R >>");

        var kids = string.Join(" ", Enumerable.Range(0, pageCount).Select(static index => $"{5 + (index * 2)} 0 R"));
        WritePdfObject(stream, offsets, 2, $"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>");
        WritePdfObject(stream, offsets, 3, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        WritePdfObject(stream, offsets, 4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

        for (var index = 0; index < pageCount; index++)
        {
            var pageObjectNumber = 5 + (index * 2);
            var contentObjectNumber = pageObjectNumber + 1;
            WritePdfObject(
                stream,
                offsets,
                pageObjectNumber,
                FormattableString.Invariant(
                    $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth:0.##} {pageHeight:0.##}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObjectNumber} 0 R >>"));
            WritePdfStreamObject(stream, offsets, contentObjectNumber, pageContents[index]);
        }

        var xrefOffset = stream.Position;
        WritePdfString(stream, $"xref\n0 {objectCount + 1}\n");
        WritePdfString(stream, "0000000000 65535 f \n");
        for (var index = 1; index <= objectCount; index++)
        {
            WritePdfString(stream, $"{offsets[index]:D10} 00000 n \n");
        }

        WritePdfString(stream, $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
        return stream.ToArray();
    }

    private static void WritePdfObject(MemoryStream stream, long[] offsets, int objectNumber, string body)
    {
        offsets[objectNumber] = stream.Position;
        WritePdfString(stream, $"{objectNumber} 0 obj\n{body}\nendobj\n");
    }

    private static void WritePdfStreamObject(MemoryStream stream, long[] offsets, int objectNumber, string content)
    {
        offsets[objectNumber] = stream.Position;
        var contentBytes = PdfEncoding.GetBytes(content);
        WritePdfString(stream, $"{objectNumber} 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        stream.Write(contentBytes, 0, contentBytes.Length);
        WritePdfString(stream, "\nendstream\nendobj\n");
    }

    private static void WritePdfString(MemoryStream stream, string value)
    {
        var bytes = PdfEncoding.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void AppendPdfRect(StringBuilder content, double x, double y, double width, double height, string color, bool fill)
    {
        AppendPdfCommand(
            content,
            fill
                ? "{0} rg {1:0.###} {2:0.###} {3:0.###} {4:0.###} re f"
                : "{0} RG {1:0.###} {2:0.###} {3:0.###} {4:0.###} re S",
            color,
            x,
            y,
            width,
            height);
    }

    private static void AppendPdfCellBorder(StringBuilder content, double x, double y, double width, double height)
    {
        AppendPdfCommand(content, "0.82 0.87 0.93 RG 0.35 w {0:0.###} {1:0.###} {2:0.###} {3:0.###} re S", x, y, width, height);
    }

    private static void AppendPdfText(
        StringBuilder content,
        string? value,
        double x,
        double y,
        double fontSize,
        string fontResource,
        double maxWidth,
        bool alignRight = false)
    {
        var text = FitPdfText(CleanPdfText(value), maxWidth, fontSize);
        var textX = x;
        if (alignRight)
        {
            textX = x + Math.Max(0, maxWidth - EstimatePdfTextWidth(text, fontSize));
        }

        AppendPdfCommand(
            content,
            "BT /{0} {1:0.###} Tf 0.05 0.09 0.15 rg 1 0 0 1 {2:0.###} {3:0.###} Tm ({4}) Tj ET",
            fontResource,
            fontSize,
            textX,
            y,
            EscapePdfText(text));
    }

    private static void AppendPdfCommand(StringBuilder content, string format, params object[] args)
    {
        content.AppendFormat(CultureInfo.InvariantCulture, format, args);
        content.Append('\n');
    }

    private static string FormatPdfNumber(long? value) =>
        value.HasValue ? value.Value.ToString("N0", PdfCulture) : "-";

    private static string FormatPdfNumber(int? value) =>
        value.HasValue ? value.Value.ToString("N0", PdfCulture) : "-";

    private static string FormatPdfNumber(long value) =>
        value.ToString("N0", PdfCulture);

    private static string FitPdfText(string value, double maxWidth, double fontSize)
    {
        if (string.IsNullOrWhiteSpace(value) || maxWidth <= 0)
            return value;

        var maxChars = Math.Max(1, (int)Math.Floor(maxWidth / Math.Max(fontSize * 0.52, 1)));
        if (value.Length <= maxChars)
            return value;

        if (maxChars <= 3)
            return value[..Math.Min(value.Length, maxChars)];

        return $"{value[..(maxChars - 3)].TrimEnd()}...";
    }

    private static double EstimatePdfTextWidth(string value, double fontSize) =>
        CleanPdfText(value).Length * fontSize * 0.52;

    private static string CleanPdfText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var cleaned = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();

        return string.Join(" ", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string EscapePdfText(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string BuildSafeFileName(string? value)
    {
        var cleaned = string.Join("-", (value ?? "cliente")
            .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        cleaned = cleaned
            .Replace(" ", "-", StringComparison.OrdinalIgnoreCase)
            .Trim('-');

        return string.IsNullOrWhiteSpace(cleaned)
            ? "cliente"
            : cleaned.ToLowerInvariant();
    }

    private sealed record PdfTableColumn(
        string Header,
        double Width,
        bool AlignRight,
        Func<CopiersCountersEquipmentRowDto, string> ValueSelector);
}
