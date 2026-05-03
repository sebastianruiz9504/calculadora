using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.SoporteCloud;
using CotizadorInterno.Web.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Dashboard)]
public sealed class DashboardController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
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
    public async Task<IActionResult> CopiersCounters([FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? clientId, CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            var dashboard = await _dataverse.GetCopiersCountersDashboardAsync(
                year ?? today.Year,
                month ?? today.Month,
                clientId,
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
}
