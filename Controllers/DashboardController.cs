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

            if (!dashboard.CanExport)
            {
                var details = dashboard.ExportBlockers?.Select(static item => item.Message).Where(static value => !string.IsNullOrWhiteSpace(value)).Take(8).ToList()
                    ?? new List<string>();
                var message = details.Count == 0
                    ? "Hay pendientes por solucionar antes de exportar el reporte de contadores."
                    : $"Soluciona estos pendientes antes de exportar: {string.Join(" ", details)}";
                return BadRequest(message);
            }

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
    public async Task<IActionResult> Taxes([FromQuery] TaxesDashboardRequestDto request, CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            request ??= new TaxesDashboardRequestDto();
            request.Year ??= today.Year;
            var dashboard = await _dataverse.GetTaxesDashboardAsync(request, ct);

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
    public async Task<IActionResult> TaxesRetentionsExport([FromQuery] TaxesDashboardRequestDto request, CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            request ??= new TaxesDashboardRequestDto();
            request.Year ??= today.Year;
            var dashboard = await _dataverse.GetTaxesDashboardAsync(request, ct);

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
    public async Task<IActionResult> TaxesReteFuenteExport([FromQuery] TaxesDashboardRequestDto request, CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            request ??= new TaxesDashboardRequestDto();
            request.Year ??= today.Year;
            var dashboard = await _dataverse.GetTaxesDashboardAsync(request, ct);
            var content = BuildTaxesReteFuenteExcel(dashboard);
            var periodToken = BuildSafeFileName(FirstNonEmpty(dashboard.ReteFuente.Filter.ValueLabel, dashboard.ReteFuente.PeriodLabel, "retefuente"));
            var fileName = $"reporte-retefuente-{dashboard.ReteFuente.Filter.Year}-{periodToken}-{ResolveBogotaToday():yyyyMMdd}.xlsx";

            return File(
                content,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible generar el reporte de retefuente.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> TaxesVatExport([FromQuery] TaxesDashboardRequestDto request, CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            request ??= new TaxesDashboardRequestDto();
            request.Year ??= today.Year;
            var dashboard = await _dataverse.GetTaxesDashboardAsync(request, ct);
            var content = BuildTaxesVatExcel(dashboard);
            var periodToken = BuildSafeFileName(FirstNonEmpty(dashboard.ReteIva.Filter.ValueLabel, dashboard.ReteIva.PeriodLabel, "iva"));
            var fileName = $"reporte-iva-{dashboard.ReteIva.Filter.Year}-{periodToken}-{ResolveBogotaToday():yyyyMMdd}.xlsx";

            return File(
                content,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible generar el reporte de IVA.");
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
    public async Task<IActionResult> Utility(CancellationToken ct)
    {
        try
        {
            return Json(await _dataverse.GetUtilityDashboardAsync(ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el dashboard de utilidad.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UtilityAssignment([FromBody] UtilityAssignmentRequestDto request, CancellationToken ct)
    {
        try
        {
            return Json(await _dataverse.AssignUtilityRowAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible asignar la fila de utilidad.");
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

    private static byte[] BuildTaxesVatExcel(TaxesDashboardDto dashboard)
    {
        using var workbook = new XLWorkbook();
        var vatSection = dashboard.ReteIva;
        var generatedTable = FindVatTable(vatSection, "generated") ?? new TaxVatTableDto { Label = "IVA generado", ValueLabel = "IVA", NameColumnLabel = "Cliente" };
        var spentTable = FindVatTable(vatSection, "spent") ?? new TaxVatTableDto { Label = "IVA gastado", ValueLabel = "IVA", NameColumnLabel = "Nombre emisor" };
        var reteIvaTable = FindVatTable(vatSection, "reteiva") ?? new TaxVatTableDto { Label = "ReteIVA a favor", ValueLabel = "Valor reteiva", NameColumnLabel = "Cliente" };

        AddVatSummaryWorksheet(workbook, vatSection, generatedTable, spentTable, reteIvaTable);
        AddVatDetailWorksheet(workbook, "Iva generado", generatedTable);
        AddVatDetailWorksheet(workbook, "Iva gastado", spentTable);
        AddVatDetailWorksheet(workbook, "Reteiva a favor", reteIvaTable);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] BuildTaxesReteFuenteExcel(TaxesDashboardDto dashboard)
    {
        using var workbook = new XLWorkbook();
        var section = dashboard.ReteFuente;
        var autoTable = FindTaxReportTable(section, "autofuente") ?? new TaxReportTableDto
        {
            Label = "Autofuente",
            DateColumnLabel = "Fecha emision",
            NameColumnLabel = "Cliente",
            TotalColumnLabel = "Total factura",
            BaseColumnLabel = "Base antes de IVA",
            AmountColumnLabel = "Autofuente",
            ShowBaseColumn = true
        };
        var expensesTable = FindTaxReportTable(section, "retefuente-gastos") ?? new TaxReportTableDto
        {
            Label = "ReteFuente gastos",
            DateColumnLabel = "Fecha pago",
            NameColumnLabel = "Receptor",
            TotalColumnLabel = "Valor pago",
            AmountColumnLabel = "ReteFuente",
            CategoryColumnLabel = "Tipo persona",
            ShowCategoryColumn = true
        };

        AddReteFuenteSummaryWorksheet(workbook, section, autoTable, expensesTable);
        AddTaxReportWorksheet(workbook, "Autofuente", autoTable);
        AddTaxReportWorksheet(workbook, "ReteFuente gastos", expensesTable);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static TaxReportTableDto? FindTaxReportTable(TaxesSectionDto section, string key) =>
        section.ReportDetails.Tables.FirstOrDefault(table => string.Equals(table.Key, key, StringComparison.OrdinalIgnoreCase));

    private static void AddReteFuenteSummaryWorksheet(
        XLWorkbook workbook,
        TaxesSectionDto section,
        TaxReportTableDto autoTable,
        TaxReportTableDto expensesTable)
    {
        var worksheet = workbook.Worksheets.Add("Resumen");

        worksheet.Cell(1, 1).Value = "Resumen Retefuente";
        worksheet.Cell(2, 1).Value = section.PeriodLabel;
        worksheet.Cell(2, 2).Value = section.DateRangeLabel;
        worksheet.Cell(4, 1).Value = "Concepto";
        worksheet.Cell(4, 2).Value = "Valor";
        worksheet.Cell(5, 1).Value = "Autofuente";
        worksheet.Cell(5, 2).Value = autoTable.TotalAmountValue;
        worksheet.Cell(6, 1).Value = "ReteFuente gastos";
        worksheet.Cell(6, 2).Value = expensesTable.TotalAmountValue;
        worksheet.Cell(7, 1).Value = "Total retefuente a pagar";
        worksheet.Cell(7, 2).Value = section.TotalValue;
        worksheet.Cell(9, 1).Value = "Formula";
        worksheet.Cell(9, 2).Value = "Autofuente + ReteFuente gastos";

        var usedRange = worksheet.Range(1, 1, 9, 2);
        usedRange.Style.Font.FontName = "Aptos";
        var titleRange = worksheet.Range(1, 1, 1, 2).Merge();
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 16;
        worksheet.Range(4, 1, 4, 2).Style.Font.Bold = true;
        worksheet.Range(4, 1, 4, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF3FF");
        worksheet.Range(7, 1, 7, 2).Style.Font.Bold = true;
        worksheet.Range(7, 1, 7, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F9FF");
        worksheet.Range(5, 2, 7, 2).Style.NumberFormat.Format = "$ #,##0";
        worksheet.Columns().AdjustToContents();
    }

    private static void AddTaxReportWorksheet(XLWorkbook workbook, string sheetName, TaxReportTableDto table)
    {
        var worksheet = workbook.Worksheets.Add(sheetName);
        var headers = new List<string>
        {
            table.DateColumnLabel,
            "Numero factura",
            table.NameColumnLabel
        };

        if (table.ShowCategoryColumn)
            headers.Add(table.CategoryColumnLabel);

        headers.Add(table.TotalColumnLabel);

        if (table.ShowBaseColumn)
            headers.Add(table.BaseColumnLabel);

        headers.Add(table.AmountColumnLabel);

        worksheet.Cell(1, 1).Value = table.Label;
        for (var index = 0; index < headers.Count; index++)
        {
            worksheet.Cell(3, index + 1).Value = headers[index];
        }

        var rowIndex = 4;
        foreach (var row in table.Rows)
        {
            var columnIndex = 1;
            worksheet.Cell(rowIndex, columnIndex++).Value = row.DateDisplay;
            worksheet.Cell(rowIndex, columnIndex++).Value = row.InvoiceNumber;
            worksheet.Cell(rowIndex, columnIndex++).Value = row.Name;

            if (table.ShowCategoryColumn)
                worksheet.Cell(rowIndex, columnIndex++).Value = row.Category;

            worksheet.Cell(rowIndex, columnIndex++).Value = row.TotalValue;

            if (table.ShowBaseColumn)
                worksheet.Cell(rowIndex, columnIndex++).Value = row.BaseValue;

            worksheet.Cell(rowIndex, columnIndex).Value = row.AmountValue;
            rowIndex++;
        }

        var totalColumnIndex = 4 + (table.ShowCategoryColumn ? 1 : 0);
        var amountColumnIndex = headers.Count;
        worksheet.Cell(rowIndex, 1).Value = "Total";
        worksheet.Cell(rowIndex, 2).Value = $"{table.Rows.Count:N0} registros";
        worksheet.Cell(rowIndex, totalColumnIndex).Value = table.TotalValue;

        if (table.ShowBaseColumn)
            worksheet.Cell(rowIndex, totalColumnIndex + 1).Value = table.TotalBaseValue;

        worksheet.Cell(rowIndex, amountColumnIndex).Value = table.TotalAmountValue;

        var usedRange = worksheet.Range(1, 1, rowIndex, headers.Count);
        usedRange.Style.Font.FontName = "Aptos";
        var titleRange = worksheet.Range(1, 1, 1, headers.Count).Merge();
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 16;
        worksheet.Range(3, 1, 3, headers.Count).Style.Font.Bold = true;
        worksheet.Range(3, 1, 3, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF3FF");
        worksheet.Range(rowIndex, 1, rowIndex, headers.Count).Style.Font.Bold = true;
        worksheet.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F9FF");
        worksheet.Range(4, totalColumnIndex, rowIndex, amountColumnIndex).Style.NumberFormat.Format = "$ #,##0";
        worksheet.SheetView.FreezeRows(3);
        worksheet.Columns().AdjustToContents();
    }

    private static TaxVatTableDto? FindVatTable(TaxesSectionDto section, string key) =>
        section.VatDetails.Tables.FirstOrDefault(table => string.Equals(table.Key, key, StringComparison.OrdinalIgnoreCase));

    private static void AddVatSummaryWorksheet(
        XLWorkbook workbook,
        TaxesSectionDto vatSection,
        TaxVatTableDto generatedTable,
        TaxVatTableDto spentTable,
        TaxVatTableDto reteIvaTable)
    {
        var worksheet = workbook.Worksheets.Add("Resumen");
        var generated = SumVatTableTax(generatedTable);
        var spent = SumVatTableTax(spentTable);
        var reteIva = SumVatTableTax(reteIvaTable);

        worksheet.Cell(1, 1).Value = "Resumen IVA";
        worksheet.Cell(2, 1).Value = vatSection.PeriodLabel;
        worksheet.Cell(2, 2).Value = vatSection.DateRangeLabel;
        worksheet.Cell(4, 1).Value = "Concepto";
        worksheet.Cell(4, 2).Value = "Valor";
        worksheet.Cell(5, 1).Value = "IVA generado";
        worksheet.Cell(5, 2).Value = generated;
        worksheet.Cell(6, 1).Value = "IVA gastado";
        worksheet.Cell(6, 2).Value = spent;
        worksheet.Cell(7, 1).Value = "ReteIVA a favor";
        worksheet.Cell(7, 2).Value = reteIva;
        worksheet.Cell(8, 1).Value = "IVA total a pagar";
        worksheet.Cell(8, 2).Value = vatSection.TotalValue;
        worksheet.Cell(10, 1).Value = "Formula";
        worksheet.Cell(10, 2).Value = "IVA generado - (IVA gastado + ReteIVA a favor)";

        var usedRange = worksheet.Range(1, 1, 10, 2);
        usedRange.Style.Font.FontName = "Aptos";
        var titleRange = worksheet.Range(1, 1, 1, 2).Merge();
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 16;
        worksheet.Range(4, 1, 4, 2).Style.Font.Bold = true;
        worksheet.Range(4, 1, 4, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF3FF");
        worksheet.Range(8, 1, 8, 2).Style.Font.Bold = true;
        worksheet.Range(8, 1, 8, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F9FF");
        worksheet.Range(5, 2, 8, 2).Style.NumberFormat.Format = "$ #,##0";
        worksheet.Columns().AdjustToContents();
    }

    private static void AddVatDetailWorksheet(XLWorkbook workbook, string sheetName, TaxVatTableDto table)
    {
        var worksheet = workbook.Worksheets.Add(sheetName);
        var headers = new[]
        {
            "Fecha emision",
            "Numero factura",
            table.NameColumnLabel,
            "Total factura",
            table.ValueLabel
        };

        worksheet.Cell(1, 1).Value = table.Label;
        for (var index = 0; index < headers.Length; index++)
        {
            worksheet.Cell(3, index + 1).Value = headers[index];
        }

        var rowIndex = 4;
        foreach (var row in table.Rows)
        {
            worksheet.Cell(rowIndex, 1).Value = row.DateDisplay;
            worksheet.Cell(rowIndex, 2).Value = row.InvoiceNumber;
            worksheet.Cell(rowIndex, 3).Value = row.Name;
            worksheet.Cell(rowIndex, 4).Value = row.TotalValue;
            worksheet.Cell(rowIndex, 5).Value = row.TaxValue;
            rowIndex++;
        }

        worksheet.Cell(rowIndex, 1).Value = "Total";
        worksheet.Cell(rowIndex, 2).Value = $"{table.Rows.Count:N0} registros";
        worksheet.Cell(rowIndex, 4).Value = table.Rows.Sum(static row => row.TotalValue);
        worksheet.Cell(rowIndex, 5).Value = SumVatTableTax(table);

        var usedRange = worksheet.Range(1, 1, rowIndex, headers.Length);
        usedRange.Style.Font.FontName = "Aptos";
        var titleRange = worksheet.Range(1, 1, 1, headers.Length).Merge();
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 16;
        worksheet.Range(3, 1, 3, headers.Length).Style.Font.Bold = true;
        worksheet.Range(3, 1, 3, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF3FF");
        worksheet.Range(rowIndex, 1, rowIndex, headers.Length).Style.Font.Bold = true;
        worksheet.Range(rowIndex, 1, rowIndex, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F9FF");
        worksheet.Range(4, 4, rowIndex, 5).Style.NumberFormat.Format = "$ #,##0";
        worksheet.SheetView.FreezeRows(3);
        worksheet.Columns().AdjustToContents();
    }

    private static decimal SumVatTableTax(TaxVatTableDto table) =>
        table.Rows.Sum(static row => row.TaxValue);

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
        const double tableTopY = 485;
        const double bottomY = 34;
        const double headerHeight = 19;
        const double rowHeight = 15.5;

        var rows = (dashboard.EquipmentRows ?? Array.Empty<CopiersCountersEquipmentRowDto>()).ToList();
        var summaries = dashboard.ClientSummaries ?? Array.Empty<CopiersCountersClientSummaryDto>();
        var totalIncludedOperations = summaries.Sum(static row => row.IncludedOperations);
        var totalExcessQuantity = summaries.Sum(static row => row.ExcessQuantity);
        var totalExcessValue = summaries.Sum(static row => row.ExcessTotal);
        var columns = new[]
        {
            new PdfTableColumn("Equipo", 76, false, row => FirstNonEmpty(row.EquipmentName, "Sin equipo")),
            new PdfTableColumn("Ubicacion", 68, false, row => FirstNonEmpty(row.Area, row.Site)),
            new PdfTableColumn("Linea", 86, false, row => row.IsBackup ? "Backup" : FirstNonEmpty(row.ProductLineName, row.AssignmentStatus)),
            new PdfTableColumn("Fecha ant.", 49, false, row => FirstNonEmpty(row.PreviousDateDisplay, "-")),
            new PdfTableColumn("Fecha act.", 49, false, row => FirstNonEmpty(row.CurrentDateDisplay, "-")),
            new PdfTableColumn("Copias", 48, true, row => FormatPdfNumber(row.CopiesConsumption)),
            new PdfTableColumn("Escaneos", 52, true, row => FormatPdfNumber(row.ScansConsumption)),
            new PdfTableColumn("Total ops.", 54, true, row => FormatPdfNumber(row.TotalConsumption)),
            new PdfTableColumn("Incluidas", 58, true, row => FormatPdfDecimal(row.IncludedOperations)),
            new PdfTableColumn("Exced.", 50, true, row => row.ExcessQuantity > 0 ? FormatPdfNumber(row.ExcessQuantity) : "-"),
            new PdfTableColumn("Unitario", 60, true, row => row.UnitExcessCost > 0m ? FormatPdfCurrency(row.UnitExcessCost) : "-"),
            new PdfTableColumn("Total exc.", 64, true, row => row.ExcessTotal > 0m ? FormatPdfCurrency(row.ExcessTotal) : "-")
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
                $"Operaciones incluidas: {FormatPdfDecimal(totalIncludedOperations)} | Cantidad de excedentes: {FormatPdfNumber(totalExcessQuantity)} | Valor total excedentes: {FormatPdfCurrency(totalExcessValue)}",
                marginX,
                topY - 43,
                7.5,
                "F2",
                640);
            AppendPdfText(
                content,
                $"Costo unitario de excedentes: {BuildCopiersCountersPdfUnitCostSummary(summaries)}",
                marginX,
                topY - 56,
                7.5,
                "F1",
                640);
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

    private static string FormatPdfDecimal(decimal value) =>
        value == 0m ? "-" : value.ToString("N0", PdfCulture);

    private static string FormatPdfCurrency(decimal value) =>
        value == 0m ? "$0" : value.ToString("C0", PdfCulture);

    private static string BuildCopiersCountersPdfUnitCostSummary(IReadOnlyList<CopiersCountersClientSummaryDto> summaries)
    {
        var values = summaries
            .Select(static row => row.UnitExcessCost)
            .Where(static value => value > 0m)
            .Distinct()
            .OrderBy(static value => value)
            .ToList();

        return values.Count switch
        {
            0 => "$0",
            1 => FormatPdfCurrency(values[0]),
            _ => string.Join(", ", values.Select(FormatPdfCurrency))
        };
    }

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
