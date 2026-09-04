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
using System.IO.Compression;
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
    private readonly IAzureOpenAIDashboardAgentService _agent;
    private readonly ITaxesReteFuenteReportService _reteFuenteReportService;

    public DashboardController(
        IDataverseService dataverse,
        ISiigoService siigo,
        IAzureOpenAIDashboardAgentService agent,
        ITaxesReteFuenteReportService reteFuenteReportService)
    {
        _dataverse = dataverse;
        _siigo = siigo;
        _agent = agent;
        _reteFuenteReportService = reteFuenteReportService;
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
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Today(CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            var currentStart = new DateOnly(today.Year, today.Month, 1);
            var previousStart = currentStart.AddMonths(-1);
            var previousEnd = previousStart.AddDays(
                Math.Min(today.Day, DateTime.DaysInMonth(previousStart.Year, previousStart.Month)) - 1);

            var currentSupportTask = _dataverse.GetSoporteCloudBoardAsync(currentStart, today, ct);
            var previousSupportTask = _dataverse.GetSoporteCloudBoardAsync(previousStart, previousEnd, ct);
            var copiersEquipmentTask = _dataverse.GetCopiersEquipmentDashboardAsync(ct);
            var portfolioTask = _dataverse.GetPortfolioDashboardSummaryAsync(ct);
            var currentYtdTask = _dataverse.GetYtdDashboardAsync(today.Year, ct);
            var cloudProductsTotalBusinessTask = _dataverse.GetCloudProductsTotalBusinessUsdAsync(ct);
            await Task.WhenAll(
                currentSupportTask,
                previousSupportTask,
                copiersEquipmentTask,
                portfolioTask,
                currentYtdTask,
                cloudProductsTotalBusinessTask);

            YtdDashboardDto? previousYtd = null;
            if (previousStart.Year != today.Year)
            {
                previousYtd = await _dataverse.GetYtdDashboardAsync(previousStart.Year, ct);
            }

            return Json(DashboardTodaySummaryBuilder.Build(
                today,
                await portfolioTask,
                await currentYtdTask,
                previousYtd,
                await currentSupportTask,
                await previousSupportTask,
                await copiersEquipmentTask,
                await cloudProductsTotalBusinessTask));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el resumen ejecutivo de hoy.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Agent([FromBody] DashboardAgentChatRequestDto request, CancellationToken ct)
    {
        try
        {
            return Json(await _agent.AskAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible responder con el agente del dashboard.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public IActionResult AgentExport([FromQuery] string id)
    {
        if (string.IsNullOrWhiteSpace(id)
            || id.Length > 64
            || id.Any(static ch => !char.IsLetterOrDigit(ch) && ch != '-'))
        {
            return NotFound();
        }

        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "dashboard-agent-exports", $"{id}.xlsx");
        if (!System.IO.File.Exists(filePath))
            return NotFound();

        var fileName = $"dashboard-agent-{id}.xlsx";
        return File(
            System.IO.File.OpenRead(filePath),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> AgentFeedback([FromBody] DashboardAgentFeedbackRequestDto request, CancellationToken ct)
    {
        try
        {
            return Json(await _dataverse.CreateDashboardAgentFeedbackAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible enviar la solicitud de aprendizaje.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> AgentLearning(CancellationToken ct)
    {
        try
        {
            return Json(await _dataverse.GetDashboardAgentLearningFeedbackAsync(ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar la bandeja de aprendizaje.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> AgentLearningStatus([FromBody] DashboardAgentLearningStatusUpdateRequestDto request, CancellationToken ct)
    {
        try
        {
            return Json(await _dataverse.UpdateDashboardAgentLearningFeedbackStatusAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible actualizar el aprendizaje.");
        }
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
    public async Task<IActionResult> BillingInvoices(
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] bool duplicatesOnly = false,
        CancellationToken ct = default)
    {
        try
        {
            var today = ResolveBogotaToday();
            return Json(await _dataverse.GetBillingInvoicesPageAsync(
                year ?? today.Year,
                month ?? today.Month,
                page,
                pageSize,
                duplicatesOnly,
                ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar la tabla de facturacion.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> BillingCurrentMonth(CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            var monthStart = new DateOnly(today.Year, today.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var dashboard = await _dataverse.GetCloudBillingCurrentMonthDashboardAsync(ct);
            await EnrichCloudBillingCurrentMonthWithSiigoAsync(dashboard, monthStart, monthEnd, ct);

            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar la auditoria de facturacion Cloud del mes actual.");
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

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> YtdBillingRecord([FromBody] YtdBillingRecordUpdateRequestDto request, CancellationToken ct)
    {
        try
        {
            return Json(await _dataverse.UpdateYtdBillingRecordAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible actualizar la factura YTD.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> YtdRecords([FromBody] YtdRecordsUpdateRequestDto request, CancellationToken ct)
    {
        try
        {
            return Json(await _dataverse.UpdateYtdRecordsAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible guardar los cambios YTD.");
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
                var exportAmount = Math.Round(requested.ExportAmount ?? invoice.NetTotalInvoice, 2, MidpointRounding.AwayFromZero);

                if (exportAmount < 0m || exportAmount > invoice.NetTotalInvoice)
                {
                    return BadRequest($"El valor a exportar de la factura {invoice.InvoiceNumber} debe estar entre 0 y el total neto de la factura.");
                }

                var fraction = invoice.NetTotalInvoice == 0m
                    ? 0m
                    : Math.Round(exportAmount / invoice.NetTotalInvoice, 4, MidpointRounding.AwayFromZero);
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
                "IVA bruto",
                "IVA nota credito",
                "IVA neto",
                "Total bruto",
                "Notas credito",
                "Total neto",
                "Valor reportado",
                "Fraccion del neto",
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
                worksheet.Cell(rowIndex, 6).Value = row.Invoice.CreditNoteVat;
                worksheet.Cell(rowIndex, 7).Value = row.Invoice.NetVatValue;
                worksheet.Cell(rowIndex, 8).Value = row.Invoice.TotalInvoice;
                worksheet.Cell(rowIndex, 9).Value = row.Invoice.CreditNoteTotal;
                worksheet.Cell(rowIndex, 10).Value = row.Invoice.NetTotalInvoice;
                worksheet.Cell(rowIndex, 11).Value = row.ExportAmount;
                worksheet.Cell(rowIndex, 12).Value = row.Fraction;
                worksheet.Cell(rowIndex, 13).Value = row.Invoice.EmissionDateDisplay;
                worksheet.Cell(rowIndex, 14).Value = row.Invoice.VerticalLabel;
                worksheet.Cell(rowIndex, 15).Value = row.Invoice.ContractTypeLabel;
                worksheet.Cell(rowIndex, 16).Value = row.Invoice.PublicUrl;

                if (Uri.TryCreate(row.Invoice.PublicUrl, UriKind.Absolute, out _))
                {
                    worksheet.Cell(rowIndex, 16).SetHyperlink(new XLHyperlink(row.Invoice.PublicUrl));
                }

                rowIndex++;
            }

            worksheet.Cell(rowIndex, 1).Value = "Total";
            worksheet.Cell(rowIndex, 2).Value = $"{exportRows.Count:N0} facturas";
            worksheet.Cell(rowIndex, 8).Value = exportRows.Sum(static row => row.Invoice.TotalInvoice);
            worksheet.Cell(rowIndex, 9).Value = exportRows.Sum(static row => row.Invoice.CreditNoteTotal);
            worksheet.Cell(rowIndex, 10).Value = exportRows.Sum(static row => row.Invoice.NetTotalInvoice);
            worksheet.Cell(rowIndex, 11).Value = totalExported;

            var usedRange = worksheet.Range(1, 1, rowIndex, headers.Length);
            usedRange.Style.Font.FontName = "Aptos";
            worksheet.Range(1, 1, 1, headers.Length).Merge().Style.Font.Bold = true;
            worksheet.Range(6, 1, 6, headers.Length).Style.Font.Bold = true;
            worksheet.Range(rowIndex, 1, rowIndex, headers.Length).Style.Font.Bold = true;
            worksheet.Range(rowIndex, 1, rowIndex, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F9FF");
            worksheet.Range(4, 2, 4, 2).Style.NumberFormat.Format = "$ #,##0";
            worksheet.Range(7, 5, rowIndex, 11).Style.NumberFormat.Format = "$ #,##0";
            worksheet.Range(7, 12, rowIndex, 12).Style.NumberFormat.Format = "0.00%";
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
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
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
    public async Task<IActionResult> AccountStatementClientSearch([FromQuery] string q, CancellationToken ct)
    {
        try
        {
            var items = await _dataverse.SearchClientsAsync(q, top: 12, ct: ct);
            return Json(items);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible buscar clientes para el estado de cuenta.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> AccountStatement([FromQuery] string clientId, [FromQuery] string? clientName, CancellationToken ct)
    {
        try
        {
            var statement = await _dataverse.GetAccountStatementAsync(clientId, clientName, ct);
            return Json(statement);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible generar el estado de cuenta.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> AccountStatementPdf([FromQuery] string clientId, [FromQuery] string? clientName, CancellationToken ct)
    {
        try
        {
            var statement = await _dataverse.GetAccountStatementAsync(clientId, clientName, ct);
            var content = BuildAccountStatementPdf(statement);
            var clientToken = BuildSafeFileName(FirstNonEmpty(statement.ClientName, clientName, "cliente"));
            var fileName = $"estado-de-cuenta-{clientToken}-{ResolveBogotaToday():yyyyMMdd}.pdf";

            return File(content, "application/pdf", fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible generar el PDF del estado de cuenta.");
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
    public async Task<IActionResult> BillingCreditNotes(CancellationToken ct)
    {
        try
        {
            return Json(await _dataverse.GetBillingCreditNotesAsync(ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar la tabla de notas credito.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> BusinessBilling([FromQuery] DateOnly? start, [FromQuery] DateOnly? end, [FromQuery] string? granularity, CancellationToken ct)
    {
        try
        {
            var dashboard = await _dataverse.GetBusinessBillingDashboardAsync(start, end, granularity, ct);
            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar la facturacion de negocio.");
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
    public async Task<IActionResult> CopiersEquipmentMovements(CancellationToken ct)
    {
        try
        {
            var dashboard = await _dataverse.GetCopiersEquipmentMovementsDashboardAsync(ct);
            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar los movimientos de equipos.");
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
    public async Task<IActionResult> CopiersEquipmentMovementAttachment([FromQuery] string movementId, CancellationToken ct)
    {
        try
        {
            var file = await _dataverse.DownloadCopiersEquipmentMovementAttachmentAsync(movementId, ct);
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
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible descargar el acta de entrega del movimiento.");
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

    [HttpDelete]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SupportCloudTicketDelete([FromQuery] string recordId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recordId))
            return BadRequest("Debes indicar el ticket a eliminar.");

        try
        {
            return Json(await _dataverse.DeleteSoporteCloudTicketAsync(recordId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible eliminar el ticket de soporte cloud.");
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
            var report = await _reteFuenteReportService.BuildAsync(request, today, ct);

            return File(
                report.ExcelContent,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                report.FileName);
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
    public async Task<IActionResult> TaxesReteIcaExport([FromQuery] TaxesDashboardRequestDto request, CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            request ??= new TaxesDashboardRequestDto();
            request.Year ??= today.Year;
            var dashboard = await _dataverse.GetTaxesDashboardAsync(request, ct);
            var content = BuildTaxesReteIcaExcel(dashboard);
            var periodToken = BuildSafeFileName(FirstNonEmpty(dashboard.ReteIca.Filter.ValueLabel, dashboard.ReteIca.PeriodLabel, "reteica"));
            var fileName = $"reporte-reteica-{dashboard.ReteIca.Filter.Year}-{periodToken}-{ResolveBogotaToday():yyyyMMdd}.xlsx";

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
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible generar el reporte de Rete ICA.");
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

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Ytd([FromQuery] int? year, CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            var resolvedYear = year is < 2000 or > 2100 ? today.Year : year ?? today.Year;
            return Json(await _dataverse.GetYtdDashboardAsync(resolvedYear, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el dashboard YTD.");
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
        var generatedTable = FindVatTable(vatSection, "generated") ?? new TaxVatTableDto { Label = "IVA generado", DateColumnLabel = "Fecha emision", ValueLabel = "IVA", NameColumnLabel = "Cliente" };
        var spentTable = FindVatTable(vatSection, "spent") ?? new TaxVatTableDto { Label = "IVA gastado", DateColumnLabel = "Fecha emision", ValueLabel = "IVA", NameColumnLabel = "Nombre emisor", ShowRetentionRateColumns = true };
        var reteIvaTable = FindVatTable(vatSection, "reteiva") ?? new TaxVatTableDto { Label = "ReteIVA a favor", DateColumnLabel = "Fecha pago", ValueLabel = "Valor reteiva", NameColumnLabel = "Cliente" };

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
            TotalColumnLabel = "Total factura",
            BaseColumnLabel = "Base antes de IVA",
            AmountColumnLabel = "ReteFuente",
            CategoryColumnLabel = "Tipo persona",
            ShowBaseColumn = true,
            ShowReteFuentePercentColumn = true,
            ShowReteIcaPercentColumn = true,
            ShowCategoryColumn = true
        };
        var creditNotesTable = FindTaxReportTable(section, "notas-credito") ?? new TaxReportTableDto
        {
            Label = "Notas credito",
            DateColumnLabel = "Fecha creacion",
            DocumentColumnLabel = "Nota credito",
            NameColumnLabel = "Factura relacionada",
            CustomerIdentificationColumnLabel = "NIT cliente",
            TotalColumnLabel = "Total nota credito",
            AmountColumnLabel = "IVA nota credito",
            ShowCustomerIdentificationColumn = true
        };

        AddReteFuenteSummaryWorksheet(workbook, section, autoTable, expensesTable);
        AddTaxReportWorksheet(workbook, "Autofuente", autoTable);
        AddTaxReportWorksheet(workbook, "ReteFuente gastos", expensesTable);
        AddTaxReportWorksheet(workbook, "Notas credito", creditNotesTable);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] BuildTaxesReteIcaExcel(TaxesDashboardDto dashboard)
    {
        using var workbook = new XLWorkbook();
        var section = dashboard.ReteIca;
        var generatedTable = FindTaxReportTable(section, "reteica-generado") ?? new TaxReportTableDto
        {
            Label = "Rete ICA generado",
            DateColumnLabel = "Fecha emision",
            NameColumnLabel = "Cliente",
            TotalColumnLabel = "Total factura",
            BaseColumnLabel = "Base antes de IVA",
            AmountColumnLabel = "Rete ICA generado",
            ShowBaseColumn = true,
            ShowReteIcaPercentColumn = true
        };
        var favorTable = FindTaxReportTable(section, "reteica-favor") ?? new TaxReportTableDto
        {
            Label = "Rete ICA a favor",
            DateColumnLabel = "Fecha pago",
            NameColumnLabel = "Cliente",
            TotalColumnLabel = "Valor pago",
            BaseColumnLabel = "Total factura",
            AmountColumnLabel = "Rete ICA a favor",
            ShowBaseColumn = true,
            ShowReteIcaPercentColumn = true
        };

        AddReteIcaSummaryWorksheet(workbook, section, generatedTable, favorTable);
        AddTaxReportWorksheet(workbook, "Rete ICA generado", generatedTable);
        AddTaxReportWorksheet(workbook, "Rete ICA a favor", favorTable);

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

    private static void AddReteIcaSummaryWorksheet(
        XLWorkbook workbook,
        TaxesSectionDto section,
        TaxReportTableDto generatedTable,
        TaxReportTableDto favorTable)
    {
        var worksheet = workbook.Worksheets.Add("Resumen del calculo");

        worksheet.Cell(1, 1).Value = "Resumen del calculo Rete ICA";
        worksheet.Cell(2, 1).Value = section.PeriodLabel;
        worksheet.Cell(2, 2).Value = section.DateRangeLabel;
        worksheet.Cell(4, 1).Value = "Concepto";
        worksheet.Cell(4, 2).Value = "Valor";
        worksheet.Cell(5, 1).Value = "Base antes de IVA";
        worksheet.Cell(5, 2).Value = generatedTable.TotalBaseValue;
        worksheet.Cell(6, 1).Value = "Rete ICA generado";
        worksheet.Cell(6, 2).Value = generatedTable.TotalAmountValue;
        worksheet.Cell(7, 1).Value = "Rete ICA a favor";
        worksheet.Cell(7, 2).Value = favorTable.TotalAmountValue;
        worksheet.Cell(8, 1).Value = "Total ICA a pagar";
        worksheet.Cell(8, 2).Value = section.TotalValue;
        worksheet.Cell(10, 1).Value = "Formula";
        worksheet.Cell(10, 2).Value = "Rete ICA generado - Rete ICA a favor";
        worksheet.Cell(11, 1).Value = "Registros generado";
        worksheet.Cell(11, 2).Value = generatedTable.Rows.Count;
        worksheet.Cell(12, 1).Value = "Registros a favor";
        worksheet.Cell(12, 2).Value = favorTable.Rows.Count;

        var usedRange = worksheet.Range(1, 1, 12, 2);
        usedRange.Style.Font.FontName = "Aptos";
        var titleRange = worksheet.Range(1, 1, 1, 2).Merge();
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 16;
        worksheet.Range(4, 1, 4, 2).Style.Font.Bold = true;
        worksheet.Range(4, 1, 4, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF3FF");
        worksheet.Range(8, 1, 8, 2).Style.Font.Bold = true;
        worksheet.Range(8, 1, 8, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F9FF");
        worksheet.Range(5, 2, 8, 2).Style.NumberFormat.Format = "$ #,##0";
        worksheet.Range(11, 2, 12, 2).Style.NumberFormat.Format = "#,##0";
        worksheet.Columns().AdjustToContents();
    }

    private static void AddTaxReportWorksheet(XLWorkbook workbook, string sheetName, TaxReportTableDto table)
    {
        var worksheet = workbook.Worksheets.Add(sheetName);
        var headers = new List<string>
        {
            table.DateColumnLabel,
            string.IsNullOrWhiteSpace(table.DocumentColumnLabel) ? "Numero factura" : table.DocumentColumnLabel,
            table.NameColumnLabel
        };

        if (table.ShowCustomerIdentificationColumn)
            headers.Add(string.IsNullOrWhiteSpace(table.CustomerIdentificationColumnLabel) ? "Identificacion" : table.CustomerIdentificationColumnLabel);

        if (table.ShowCategoryColumn)
            headers.Add(table.CategoryColumnLabel);

        headers.Add(table.TotalColumnLabel);

        if (table.ShowBaseColumn)
            headers.Add(table.BaseColumnLabel);

        headers.Add(table.AmountColumnLabel);

        if (table.ShowReteFuentePercentColumn)
            headers.Add("% rte fuente");

        if (table.ShowReteIcaPercentColumn)
            headers.Add("% rte ica");

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

            if (table.ShowCustomerIdentificationColumn)
                worksheet.Cell(rowIndex, columnIndex++).Value = row.CustomerIdentification;

            if (table.ShowCategoryColumn)
                worksheet.Cell(rowIndex, columnIndex++).Value = row.Category;

            worksheet.Cell(rowIndex, columnIndex++).Value = row.TotalValue;

            if (table.ShowBaseColumn)
                worksheet.Cell(rowIndex, columnIndex++).Value = row.BaseValue;

            worksheet.Cell(rowIndex, columnIndex++).Value = row.AmountValue;

            if (table.ShowReteFuentePercentColumn)
                worksheet.Cell(rowIndex, columnIndex++).Value = row.ReteFuentePercent;

            if (table.ShowReteIcaPercentColumn)
                worksheet.Cell(rowIndex, columnIndex++).Value = row.ReteIcaPercent;

            rowIndex++;
        }

        var totalColumnIndex = 4
            + (table.ShowCustomerIdentificationColumn ? 1 : 0)
            + (table.ShowCategoryColumn ? 1 : 0);
        var amountColumnIndex = totalColumnIndex + (table.ShowBaseColumn ? 1 : 0) + 1;
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
        if (table.ShowReteFuentePercentColumn || table.ShowReteIcaPercentColumn)
            worksheet.Range(4, amountColumnIndex + 1, rowIndex, headers.Count).Style.NumberFormat.Format = "0.00\"%\"";
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
        var headers = new List<string>
        {
            string.IsNullOrWhiteSpace(table.DateColumnLabel) ? "Fecha" : table.DateColumnLabel,
            "Numero factura",
            table.NameColumnLabel,
            "Total factura",
            table.ValueLabel
        };
        if (table.ShowRetentionRateColumns)
        {
            headers.Add("% rte fuente");
            headers.Add("% rte ica");
        }

        worksheet.Cell(1, 1).Value = table.Label;
        for (var index = 0; index < headers.Count; index++)
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
            if (table.ShowRetentionRateColumns)
            {
                worksheet.Cell(rowIndex, 6).Value = row.ReteFuentePercent;
                worksheet.Cell(rowIndex, 7).Value = row.ReteIcaPercent;
            }

            rowIndex++;
        }

        worksheet.Cell(rowIndex, 1).Value = "Total";
        worksheet.Cell(rowIndex, 2).Value = $"{table.Rows.Count:N0} registros";
        worksheet.Cell(rowIndex, 4).Value = table.Rows.Sum(static row => row.TotalValue);
        worksheet.Cell(rowIndex, 5).Value = SumVatTableTax(table);

        var usedRange = worksheet.Range(1, 1, rowIndex, headers.Count);
        usedRange.Style.Font.FontName = "Aptos";
        var titleRange = worksheet.Range(1, 1, 1, headers.Count).Merge();
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 16;
        worksheet.Range(3, 1, 3, headers.Count).Style.Font.Bold = true;
        worksheet.Range(3, 1, 3, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF3FF");
        worksheet.Range(rowIndex, 1, rowIndex, headers.Count).Style.Font.Bold = true;
        worksheet.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F9FF");
        worksheet.Range(4, 4, rowIndex, 5).Style.NumberFormat.Format = "$ #,##0";
        if (table.ShowRetentionRateColumns)
            worksheet.Range(4, 6, rowIndex, 7).Style.NumberFormat.Format = "0.00\"%\"";
        worksheet.SheetView.FreezeRows(3);
        worksheet.Columns().AdjustToContents();
    }

    private static decimal SumVatTableTax(TaxVatTableDto table) =>
        table.Rows.Sum(static row => row.TaxValue);

    private async Task EnrichCloudBillingCurrentMonthWithSiigoAsync(
        CloudBillingCurrentMonthDashboardDto dashboard,
        DateOnly monthStart,
        DateOnly monthEnd,
        CancellationToken ct)
    {
        var rows = (dashboard.Rows ?? Array.Empty<CloudBillingCurrentMonthRowDto>()).ToList();
        if (rows.Count == 0)
        {
            RefreshCloudBillingAuditCounters(dashboard);
            return;
        }

        try
        {
            var siigoInvoices = await _siigo.GetInvoicesByDateRangeAsync(monthStart, monthEnd, ct);
            dashboard.SiigoInvoicesCheckedCount = siigoInvoices.Count;

            var lookup = BuildSiigoInvoiceLookup(siigoInvoices);
            var matchedInvoiceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                var matches = FindSiigoInvoiceMatches(row, lookup);
                var invoice = SelectBestSiigoInvoiceMatch(matches);
                if (invoice is null)
                {
                    ApplyMissingSiigoAudit(row);
                    continue;
                }

                matchedInvoiceIds.Add(FirstNonEmpty(invoice.Id, invoice.Name));
                ApplySiigoAudit(row, invoice);
            }

            dashboard.SiigoMatchedInvoiceCount = matchedInvoiceIds.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            dashboard.SiigoValidationError = $"No fue posible validar DIAN/correo en Siigo: {ex.Message}";
            foreach (var row in rows)
            {
                row.DianStatusLabel = row.IsBilled ? "No validado" : "Sin factura";
                row.DianStatusTone = row.IsBilled ? "warning" : "neutral";
                row.MailStatusLabel = row.IsBilled ? "No validado" : "Sin factura";
                row.MailStatusTone = row.IsBilled ? "warning" : "neutral";
            }
        }

        RefreshCloudBillingAuditCounters(dashboard);
    }

    private static Dictionary<string, List<SiigoInvoiceRowDto>> BuildSiigoInvoiceLookup(IReadOnlyList<SiigoInvoiceRowDto> invoices)
    {
        var lookup = new Dictionary<string, List<SiigoInvoiceRowDto>>(StringComparer.OrdinalIgnoreCase);
        foreach (var invoice in invoices)
        {
            AddSiigoInvoiceLookupKey(lookup, invoice.Id, invoice);
            AddSiigoInvoiceLookupKey(lookup, invoice.Name, invoice);

            if (invoice.Number is not null)
            {
                AddSiigoInvoiceLookupKey(lookup, invoice.Number.Value.ToString(CultureInfo.InvariantCulture), invoice);
                if (!string.IsNullOrWhiteSpace(invoice.Prefix))
                {
                    AddSiigoInvoiceLookupKey(lookup, $"{invoice.Prefix}{invoice.Number.Value}", invoice);
                    AddSiigoInvoiceLookupKey(lookup, $"{invoice.Prefix}-{invoice.Number.Value}", invoice);
                }
            }
        }

        return lookup;
    }

    private static void AddSiigoInvoiceLookupKey(
        Dictionary<string, List<SiigoInvoiceRowDto>> lookup,
        string? value,
        SiigoInvoiceRowDto invoice)
    {
        var key = NormalizeSiigoInvoiceReference(value);
        if (string.IsNullOrWhiteSpace(key))
            return;

        if (!lookup.TryGetValue(key, out var matches))
        {
            matches = new List<SiigoInvoiceRowDto>();
            lookup[key] = matches;
        }

        if (!matches.Any(item => string.Equals(FirstNonEmpty(item.Id, item.Name), FirstNonEmpty(invoice.Id, invoice.Name), StringComparison.OrdinalIgnoreCase)))
            matches.Add(invoice);
    }

    private static List<SiigoInvoiceRowDto> FindSiigoInvoiceMatches(
        CloudBillingCurrentMonthRowDto row,
        IReadOnlyDictionary<string, List<SiigoInvoiceRowDto>> lookup)
    {
        var matchesByKey = new Dictionary<string, SiigoInvoiceRowDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in BuildCloudBillingSiigoReferenceCandidates(row))
        {
            var key = NormalizeSiigoInvoiceReference(reference);
            if (string.IsNullOrWhiteSpace(key) || !lookup.TryGetValue(key, out var matches))
                continue;

            foreach (var match in matches)
            {
                matchesByKey.TryAdd(FirstNonEmpty(match.Id, match.Name), match);
            }
        }

        return matchesByKey.Values.ToList();
    }

    private static IEnumerable<string> BuildCloudBillingSiigoReferenceCandidates(CloudBillingCurrentMonthRowDto row)
    {
        yield return row.LastSiigoInvoiceId;

        foreach (var invoice in row.MonthInvoices ?? Array.Empty<CloudBillingInvoiceReferenceDto>())
        {
            yield return invoice.SiigoInvoiceId;
            yield return invoice.SiigoInvoiceName;
            yield return invoice.InvoiceNumber;
            yield return invoice.InvoiceCode;

            if (!string.IsNullOrWhiteSpace(invoice.InvoicePrefix) && !string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
            {
                yield return $"{invoice.InvoicePrefix}{invoice.InvoiceNumber}";
                yield return $"{invoice.InvoicePrefix}-{invoice.InvoiceNumber}";
            }
        }

        var monthInvoiceNumbers = (row.MonthInvoiceNumbers ?? "").Trim();
        if (!monthInvoiceNumbers.Contains(',', StringComparison.Ordinal)
            && !monthInvoiceNumbers.Contains('+', StringComparison.Ordinal))
        {
            yield return monthInvoiceNumbers;
        }
    }

    private static SiigoInvoiceRowDto? SelectBestSiigoInvoiceMatch(IReadOnlyList<SiigoInvoiceRowDto> matches) =>
        matches
            .OrderBy(static invoice => invoice.Annulled ? 1 : 0)
            .ThenByDescending(static invoice => IsSiigoDianAccepted(invoice.StampStatus))
            .ThenByDescending(static invoice => IsSiigoMailSent(invoice.MailStatus))
            .ThenByDescending(static invoice => invoice.DateValue, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private static void ApplyMissingSiigoAudit(CloudBillingCurrentMonthRowDto row)
    {
        row.HasSiigoInvoice = false;
        row.IsDianAccepted = false;
        row.IsDianRejected = false;
        row.IsEmailSent = false;
        row.IsBillingComplete = false;
        row.DianStatusLabel = row.IsBilled ? "Sin match Siigo" : "Sin factura";
        row.DianStatusTone = row.IsBilled ? "warning" : "neutral";
        row.MailStatusLabel = row.IsBilled ? "Sin match Siigo" : "Sin factura";
        row.MailStatusTone = row.IsBilled ? "warning" : "neutral";

        if (row.IsBilled)
        {
            row.StatusKey = "siigo-missing";
            row.StatusLabel = "Sin validar Siigo";
            row.StatusTone = "warning";
            row.EvidenceLabel = FirstNonEmpty(row.EvidenceLabel, "Log/Dataverse");
        }
    }

    private static void ApplySiigoAudit(CloudBillingCurrentMonthRowDto row, SiigoInvoiceRowDto invoice)
    {
        row.HasSiigoInvoice = true;
        row.IsBilled = true;
        row.MatchedSiigoInvoiceId = invoice.Id;
        row.MatchedSiigoInvoiceName = invoice.Name;
        row.IsSiigoInvoiceAnnulled = invoice.Annulled;
        row.DianStatus = invoice.StampStatus;
        row.DianObservations = invoice.StampObservations;
        row.DianErrors = invoice.StampErrors;
        row.MailStatus = invoice.MailStatus;
        row.MailObservations = invoice.MailObservations;
        row.IsDianAccepted = !invoice.Annulled && IsSiigoDianAccepted(invoice.StampStatus);
        row.IsDianRejected = !invoice.Annulled && IsSiigoDianRejected(invoice.StampStatus);
        row.IsEmailSent = !invoice.Annulled && IsSiigoMailSent(invoice.MailStatus);
        row.IsBillingComplete = row.IsDianAccepted && row.IsEmailSent;
        row.DianStatusLabel = ResolveDianStatusLabel(invoice.StampStatus, invoice.Annulled);
        row.DianStatusTone = ResolveDianStatusTone(invoice.StampStatus, invoice.Annulled);
        row.MailStatusLabel = ResolveMailStatusLabel(invoice.MailStatus, invoice.Annulled);
        row.MailStatusTone = ResolveMailStatusTone(invoice.MailStatus, invoice.Annulled);
        row.EvidenceLabel = ResolveSiigoAuditEvidence(row);

        if (string.IsNullOrWhiteSpace(row.BillingError) && !string.IsNullOrWhiteSpace(invoice.StampErrors))
        {
            row.BillingError = invoice.StampErrors.Trim();
        }
        else if (string.IsNullOrWhiteSpace(row.BillingError) && row.IsDianRejected)
        {
            row.BillingError = FirstNonEmpty(invoice.StampObservations, "Factura rechazada por DIAN.");
        }
        else if (string.IsNullOrWhiteSpace(row.BillingError)
            && string.Equals(row.MailStatusTone, "danger", StringComparison.OrdinalIgnoreCase))
        {
            row.BillingError = FirstNonEmpty(invoice.MailObservations, "Error de correo en Siigo.");
        }

        if (invoice.Annulled)
        {
            row.StatusKey = "annulled";
            row.StatusLabel = "Anulada";
            row.StatusTone = "danger";
            row.HasBillingError = true;
            return;
        }

        if (!row.IsDianAccepted)
        {
            row.StatusKey = row.IsDianRejected ? "dian-rejected" : "dian-pending";
            row.StatusLabel = row.IsDianRejected ? "DIAN rechazada" : "DIAN pendiente";
            row.StatusTone = row.IsDianRejected ? "danger" : "warning";
            row.HasBillingError = row.HasBillingError || row.IsDianRejected;
            return;
        }

        if (!row.IsEmailSent)
        {
            row.StatusKey = "mail-pending";
            row.StatusLabel = "Correo pendiente";
            row.StatusTone = "warning";
            return;
        }

        row.StatusKey = "billed";
        row.StatusLabel = "Facturado";
        row.StatusTone = "success";
    }

    private static string ResolveSiigoAuditEvidence(CloudBillingCurrentMonthRowDto row)
    {
        if (row.IsBillingComplete)
            return "DIAN + correo";

        if (row.IsDianAccepted)
            return "DIAN aceptada";

        if (row.HasSiigoInvoice)
            return "Factura Siigo";

        return FirstNonEmpty(row.EvidenceLabel, "");
    }

    private static void RefreshCloudBillingAuditCounters(CloudBillingCurrentMonthDashboardDto dashboard)
    {
        var rows = (dashboard.Rows ?? Array.Empty<CloudBillingCurrentMonthRowDto>()).ToList();
        var completeRows = rows.Where(static row => row.IsBillingComplete).ToList();
        var dianPendingRows = rows
            .Where(static row => row.IsBilled && !row.IsBillingComplete && !row.IsDianAccepted && !row.IsSiigoInvoiceAnnulled)
            .ToList();
        var mailPendingRows = rows
            .Where(static row => row.IsDianAccepted && !row.IsEmailSent && !row.IsSiigoInvoiceAnnulled)
            .ToList();
        var pendingRows = rows.Where(static row => row.IsPending).ToList();
        var dueTodayRows = rows.Where(static row => row.IsDueToday).ToList();
        var overdueRows = rows.Where(static row => row.IsOverdue).ToList();
        var errorRows = rows
            .Where(static row => row.HasBillingError || row.IsDianRejected || row.IsSiigoInvoiceAnnulled)
            .ToList();

        dashboard.BilledCount = completeRows.Count;
        dashboard.PendingCount = pendingRows.Count;
        dashboard.DueTodayCount = dueTodayRows.Count;
        dashboard.OverdueCount = overdueRows.Count;
        dashboard.ErrorCount = errorRows.Count;
        dashboard.DianAcceptedCount = rows.Count(static row => row.IsDianAccepted);
        dashboard.DianPendingCount = dianPendingRows.Count;
        dashboard.EmailSentCount = rows.Count(static row => row.IsEmailSent);
        dashboard.EmailPendingCount = mailPendingRows.Count;
        dashboard.BilledMonthlyUsd = RoundDashboardCurrency(completeRows.Sum(static row => row.MonthlyBillingUsd));
        dashboard.PendingMonthlyUsd = RoundDashboardCurrency(pendingRows.Sum(static row => row.MonthlyBillingUsd));
        dashboard.DueTodayMonthlyUsd = RoundDashboardCurrency(dueTodayRows.Sum(static row => row.MonthlyBillingUsd));
        dashboard.OverdueMonthlyUsd = RoundDashboardCurrency(overdueRows.Sum(static row => row.MonthlyBillingUsd));
        dashboard.Kpis = new[]
        {
            BuildCloudBillingAuditKpi("billed", "Completadas", "Aceptadas por DIAN y con correo enviado.", completeRows),
            BuildCloudBillingAuditKpi("dian-pending", "DIAN pendiente", "Creadas en Siigo sin aceptacion DIAN confirmada.", dianPendingRows),
            BuildCloudBillingAuditKpi("mail-pending", "Correo pendiente", "Aceptadas por DIAN, pero sin correo enviado.", mailPendingRows),
            BuildCloudBillingAuditKpi("today", "Hoy", "Sin factura completa y con dia de facturacion igual al corte.", dueTodayRows),
            BuildCloudBillingAuditKpi("overdue", "Vencidos", "Sin factura completa y con dia de facturacion vencido.", overdueRows),
            BuildCloudBillingAuditKpi("pending", "Por llegar", "Sin factura completa cuyo dia de facturacion aun no llega.", pendingRows)
        };
    }

    private static PortfolioKpiDto BuildCloudBillingAuditKpi(
        string key,
        string label,
        string hint,
        IReadOnlyList<CloudBillingCurrentMonthRowDto> rows) =>
        new()
        {
            Key = key,
            Label = label,
            Hint = hint,
            Value = rows.Count,
            ValueFormat = "number",
            SecondaryLabel = "Valor mensual",
            SecondaryValue = FormatDashboardUsd(rows.Sum(static row => row.MonthlyBillingUsd))
        };

    private static string ResolveDianStatusLabel(string? status, bool annulled)
    {
        if (annulled)
            return "Factura anulada";

        var normalized = NormalizeSiigoStatus(status);
        return normalized switch
        {
            "" => "Sin estado DIAN",
            "accepted" => "DIAN aceptada",
            "rejected" => "DIAN rechazada",
            "pending" => "DIAN pendiente",
            "draft" => "Borrador DIAN",
            _ => $"DIAN {status?.Trim()}"
        };
    }

    private static string ResolveDianStatusTone(string? status, bool annulled)
    {
        if (annulled)
            return "danger";

        var normalized = NormalizeSiigoStatus(status);
        return normalized switch
        {
            "accepted" => "success",
            "rejected" => "danger",
            "" or "pending" or "draft" => "warning",
            _ => "info"
        };
    }

    private static string ResolveMailStatusLabel(string? status, bool annulled)
    {
        if (annulled)
            return "Factura anulada";

        if (IsSiigoMailSent(status))
            return "Correo enviado";

        var normalized = NormalizeSiigoStatus(status);
        return normalized switch
        {
            "" => "Sin estado correo",
            "notsent" => "No enviado",
            "pending" => "Correo pendiente",
            "failed" or "error" => "Error correo",
            _ => $"Correo {status?.Trim()}"
        };
    }

    private static string ResolveMailStatusTone(string? status, bool annulled)
    {
        if (annulled)
            return "danger";

        if (IsSiigoMailSent(status))
            return "success";

        var normalized = NormalizeSiigoStatus(status);
        return normalized switch
        {
            "failed" or "error" => "danger",
            "" or "notsent" or "pending" => "warning",
            _ => "info"
        };
    }

    private static bool IsSiigoDianAccepted(string? status) =>
        string.Equals(NormalizeSiigoStatus(status), "accepted", StringComparison.OrdinalIgnoreCase);

    private static bool IsSiigoDianRejected(string? status) =>
        string.Equals(NormalizeSiigoStatus(status), "rejected", StringComparison.OrdinalIgnoreCase);

    private static bool IsSiigoMailSent(string? status)
    {
        var normalized = NormalizeSiigoStatus(status);
        return normalized is "sent" or "sended" or "enviado" or "enviada";
    }

    private static string NormalizeSiigoStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var chars = value
            .Trim()
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();

        return new string(chars);
    }

    private static string NormalizeSiigoInvoiceReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var chars = value
            .Trim()
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();

        return new string(chars);
    }

    private static decimal RoundDashboardCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string FormatDashboardUsd(decimal value) =>
        $"USD {RoundDashboardCurrency(value).ToString("N0", PdfCulture)}";


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

    private static byte[] BuildAccountStatementPdf(AccountStatementDto statement)
    {
        const double pageWidth = 841.89;
        const double pageHeight = 595.28;
        const double marginX = 34;
        const double headerHeight = 72;
        const double tableTopY = 392;
        const double bottomY = 44;
        const double tableHeaderHeight = 24;
        const double rowHeight = 22;

        var rows = (statement.Invoices ?? Array.Empty<AccountStatementInvoiceDto>()).ToList();
        var columns = new[]
        {
            new AccountStatementPdfColumn("NUMERO DE FACTURA", 145, false, row => FirstNonEmpty(row.InvoiceNumber, "-")),
            new AccountStatementPdfColumn("VALOR NETO", 116, true, row => FormatPdfCurrency(row.NetTotalInvoice)),
            new AccountStatementPdfColumn("FECHA EMISION", 104, false, row => FirstNonEmpty(row.EmissionDateDisplay, "-")),
            new AccountStatementPdfColumn("FECHA VENCIMIENTO", 122, false, row => FirstNonEmpty(row.DueDateDisplay, "-")),
            new AccountStatementPdfColumn("ESTADO", 92, false, row => FirstNonEmpty(row.StateLabel, "-")),
            new AccountStatementPdfColumn("DIAS", 190, false, row => FirstNonEmpty(row.DaysDisplay, "-"))
        };

        var tableWidth = columns.Sum(static column => column.Width);
        var tableX = Math.Max(marginX, (pageWidth - tableWidth) / 2);
        var rowsPerPage = Math.Max(1, (int)Math.Floor((tableTopY - tableHeaderHeight - bottomY) / rowHeight));
        var totalPages = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)rowsPerPage));
        var pages = new List<string>(totalPages);
        var logo = TryLoadPngPdfImage(
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "img", "digital-tech-white-logo.png"),
            "DigitalTechLogo");
        var pdfImages = logo is null
            ? Array.Empty<PdfImageResource>()
            : new[] { logo };

        for (var pageIndex = 0; pageIndex < totalPages; pageIndex++)
        {
            var content = new StringBuilder();
            var pageRows = rows
                .Skip(pageIndex * rowsPerPage)
                .Take(rowsPerPage)
                .ToList();

            AppendPdfRect(content, 0, pageHeight - headerHeight, pageWidth, headerHeight, "0.02 0.10 0.26", fill: true);
            if (logo is not null)
            {
                AppendPdfImage(content, logo.ResourceName, marginX, pageHeight - 58, 42, 42);
            }

            AppendPdfText(content, "Digital Tech", marginX + 52, pageHeight - 38, 13, "F2", 160, color: "1 1 1");
            AppendPdfText(content, "Dashboard financiero", marginX + 52, pageHeight - 53, 7.5, "F1", 160, color: "0.82 0.92 1");
            AppendPdfText(content, "Estado de cuenta", pageWidth - marginX - 230, pageHeight - 34, 16, "F2", 230, alignRight: true, color: "1 1 1");
            AppendPdfText(
                content,
                $"Pagina {pageIndex + 1} de {totalPages}",
                pageWidth - marginX - 120,
                pageHeight - 53,
                8,
                "F1",
                120,
                alignRight: true,
                color: "0.86 0.92 1");

            AppendPdfText(content, "Cliente", marginX, pageHeight - 104, 7.5, "F2", 80);
            AppendPdfText(content, FirstNonEmpty(statement.ClientName, "Cliente"), marginX, pageHeight - 121, 12, "F2", 360);
            AppendPdfText(content, "Corte", marginX + 385, pageHeight - 104, 7.5, "F2", 70);
            AppendPdfText(content, FirstNonEmpty(statement.AsOfDateLabel, "-"), marginX + 385, pageHeight - 121, 10, "F1", 100);
            AppendPdfText(content, "Facturas pendientes", marginX + 515, pageHeight - 104, 7.5, "F2", 120);
            AppendPdfText(content, rows.Count.ToString("N0", PdfCulture), marginX + 515, pageHeight - 121, 10, "F1", 90);
            AppendPdfText(content, "Total neto estado de cuenta", marginX + 650, pageHeight - 104, 7.5, "F2", 150);
            AppendPdfText(content, FormatPdfCurrency(statement.TotalAmount), marginX + 650, pageHeight - 121, 12, "F2", 150);

            AppendPdfRect(content, tableX, tableTopY - tableHeaderHeight, tableWidth, tableHeaderHeight, "0.90 0.95 1", fill: true);

            var currentX = tableX;
            foreach (var column in columns)
            {
                AppendPdfCellBorder(content, currentX, tableTopY - tableHeaderHeight, column.Width, tableHeaderHeight);
                AppendPdfText(
                    content,
                    column.Header,
                    currentX + 4,
                    tableTopY - 15,
                    6.8,
                    "F2",
                    column.Width - 8,
                    alignRight: column.AlignRight);
                currentX += column.Width;
            }

            var currentY = tableTopY - tableHeaderHeight;
            if (rows.Count == 0)
            {
                var rowY = currentY - rowHeight;
                AppendPdfCellBorder(content, tableX, rowY, tableWidth, rowHeight);
                AppendPdfText(
                    content,
                    FirstNonEmpty(statement.EmptyStateTitle, "No hay facturas pendientes para este cliente."),
                    tableX + 6,
                    rowY + 7,
                    8,
                    "F1",
                    tableWidth - 12);
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
                            column.ValueSelector(row),
                            currentX + 4,
                            rowY + 7,
                            7,
                            "F1",
                            column.Width - 8,
                            alignRight: column.AlignRight);
                        currentX += column.Width;
                    }

                    currentY = rowY;
                }
            }

            AppendPdfText(
                content,
                "Documento generado desde Dashboard > Cartera > Generar estado de cuenta.",
                marginX,
                24,
                7,
                "F1",
                pageWidth - (marginX * 2));

            pages.Add(content.ToString());
        }

        return BuildPdfDocument(pages, pageWidth, pageHeight, pdfImages);
    }

    private static byte[] BuildCopiersCountersPdf(CopiersCountersDashboardDto dashboard)
    {
        const double pageWidth = 1190.55;
        const double pageHeight = 841.89;
        const double marginX = 18;
        const double topY = 816;
        const double tableTopY = 738;
        const double bottomY = 34;
        const double headerHeight = 19;
        const double rowHeight = 15.5;

        var rows = (dashboard.EquipmentRows ?? Array.Empty<CopiersCountersEquipmentRowDto>()).ToList();
        var totalCopies = rows.Sum(static row => row.CopiesConsumption ?? 0);
        var totalScans = rows.Sum(static row => row.ScansConsumption ?? 0);
        var columns = new[]
        {
            new PdfTableColumn("EQUIPO", 92, false, row => FirstNonEmpty(row.EquipmentName, "Sin equipo")),
            new PdfTableColumn("FECHA TOMA ANTERIOR", 108, false, row => FirstNonEmpty(row.PreviousDateDisplay, "-")),
            new PdfTableColumn("FECHA TOMA ACTUAL", 102, false, row => FirstNonEmpty(row.CurrentDateDisplay, "-")),
            new PdfTableColumn("CONTADOR ACTUAL COPIAS", 118, true, row => FormatPdfNumber(row.CurrentCopiesCounter)),
            new PdfTableColumn("CONTADOR ANTERIOR COPIAS", 126, true, row => FormatPdfNumber(row.PreviousCopiesCounter)),
            new PdfTableColumn("COPIAS", 68, true, row => FormatPdfNumber(row.CopiesConsumption)),
            new PdfTableColumn("CONTADOR ACTUAL ESCANEOS", 132, true, row => FormatPdfNumber(row.CurrentScansCounter)),
            new PdfTableColumn("CONTADOR ANTERIOR ESCANEOS", 140, true, row => FormatPdfNumber(row.PreviousScansCounter)),
            new PdfTableColumn("ESCANEOS", 76, true, row => FormatPdfNumber(row.ScansConsumption)),
            new PdfTableColumn("DIAS ENTRE TOMAS", 96, true, row => FormatPdfNumber(row.DaysBetweenReadings)),
            new PdfTableColumn("TOTAL EQUIPO", 86, true, row => FormatPdfNumber(row.TotalConsumption))
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
                $"Totales: copias {FormatPdfNumber(totalCopies)} | escaneos {FormatPdfNumber(totalScans)} | total equipo {FormatPdfNumber(totalCopies + totalScans)}",
                marginX,
                topY - 43,
                7.5,
                "F2",
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

    private static byte[] BuildPdfDocument(
        IReadOnlyList<string> pageContents,
        double pageWidth,
        double pageHeight,
        IReadOnlyList<PdfImageResource>? imageResources = null)
    {
        var pageCount = Math.Max(1, pageContents.Count);
        var images = (imageResources ?? Array.Empty<PdfImageResource>())
            .Where(static image => !string.IsNullOrWhiteSpace(image.ResourceName)
                && image.Width > 0
                && image.Height > 0
                && image.RgbBytes.Length > 0)
            .ToList();
        var nextObjectNumber = 5;
        var imageObjects = new List<PdfImageObject>(images.Count);
        foreach (var image in images)
        {
            var alphaObjectNumber = image.AlphaBytes is { Length: > 0 }
                ? nextObjectNumber++
                : (int?)null;
            var imageObjectNumber = nextObjectNumber++;
            imageObjects.Add(new PdfImageObject(image, imageObjectNumber, alphaObjectNumber));
        }

        var firstPageObjectNumber = nextObjectNumber;
        var objectCount = (firstPageObjectNumber - 1) + (pageCount * 2);
        var offsets = new long[objectCount + 1];

        using var stream = new MemoryStream();
        WritePdfString(stream, "%PDF-1.4\n");

        WritePdfObject(stream, offsets, 1, "<< /Type /Catalog /Pages 2 0 R >>");

        var kids = string.Join(" ", Enumerable.Range(0, pageCount).Select(index => $"{firstPageObjectNumber + (index * 2)} 0 R"));
        WritePdfObject(stream, offsets, 2, $"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>");
        WritePdfObject(stream, offsets, 3, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        WritePdfObject(stream, offsets, 4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

        foreach (var imageObject in imageObjects)
        {
            if (imageObject.AlphaObjectNumber is int alphaObjectNumber && imageObject.Resource.AlphaBytes is { Length: > 0 } alphaBytes)
            {
                WritePdfStreamObject(
                    stream,
                    offsets,
                    alphaObjectNumber,
                    CompressPdfStream(alphaBytes),
                    FormattableString.Invariant(
                        $"/Type /XObject /Subtype /Image /Width {imageObject.Resource.Width} /Height {imageObject.Resource.Height} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode"));
            }

            var smask = imageObject.AlphaObjectNumber is int smaskObjectNumber
                ? FormattableString.Invariant($" /SMask {smaskObjectNumber} 0 R")
                : "";
            WritePdfStreamObject(
                stream,
                offsets,
                imageObject.ImageObjectNumber,
                CompressPdfStream(imageObject.Resource.RgbBytes),
                FormattableString.Invariant(
                    $"/Type /XObject /Subtype /Image /Width {imageObject.Resource.Width} /Height {imageObject.Resource.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode{smask}"));
        }

        var resources = BuildPdfPageResources(imageObjects);
        for (var index = 0; index < pageCount; index++)
        {
            var pageObjectNumber = firstPageObjectNumber + (index * 2);
            var contentObjectNumber = pageObjectNumber + 1;
            WritePdfObject(
                stream,
                offsets,
                pageObjectNumber,
                FormattableString.Invariant(
                    $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth:0.##} {pageHeight:0.##}] /Resources {resources} /Contents {contentObjectNumber} 0 R >>"));
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

    private static string BuildPdfPageResources(IReadOnlyList<PdfImageObject> imageObjects)
    {
        var resources = new StringBuilder("<< /Font << /F1 3 0 R /F2 4 0 R >>");
        if (imageObjects.Count > 0)
        {
            resources.Append(" /XObject <<");
            foreach (var imageObject in imageObjects)
            {
                resources.AppendFormat(
                    CultureInfo.InvariantCulture,
                    " /{0} {1} 0 R",
                    imageObject.Resource.ResourceName,
                    imageObject.ImageObjectNumber);
            }

            resources.Append(" >>");
        }

        resources.Append(" >>");
        return resources.ToString();
    }

    private static void WritePdfObject(MemoryStream stream, long[] offsets, int objectNumber, string body)
    {
        offsets[objectNumber] = stream.Position;
        WritePdfString(stream, $"{objectNumber} 0 obj\n{body}\nendobj\n");
    }

    private static void WritePdfStreamObject(MemoryStream stream, long[] offsets, int objectNumber, string content)
    {
        WritePdfStreamObject(stream, offsets, objectNumber, PdfEncoding.GetBytes(content), "");
    }

    private static void WritePdfStreamObject(MemoryStream stream, long[] offsets, int objectNumber, byte[] contentBytes, string dictionaryEntries)
    {
        offsets[objectNumber] = stream.Position;
        var dictionary = string.IsNullOrWhiteSpace(dictionaryEntries)
            ? $"<< /Length {contentBytes.Length} >>"
            : $"<< /Length {contentBytes.Length} {dictionaryEntries} >>";
        WritePdfString(stream, $"{objectNumber} 0 obj\n{dictionary}\nstream\n");
        stream.Write(contentBytes, 0, contentBytes.Length);
        WritePdfString(stream, "\nendstream\nendobj\n");
    }

    private static void WritePdfString(MemoryStream stream, string value)
    {
        var bytes = PdfEncoding.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static PdfImageResource? TryLoadPngPdfImage(string path, string resourceName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                return null;

            return DecodePngForPdf(System.IO.File.ReadAllBytes(path), resourceName);
        }
        catch
        {
            return null;
        }
    }

    private static PdfImageResource DecodePngForPdf(byte[] pngBytes, string resourceName)
    {
        if (pngBytes.Length < 8
            || pngBytes[0] != 0x89
            || pngBytes[1] != 0x50
            || pngBytes[2] != 0x4E
            || pngBytes[3] != 0x47
            || pngBytes[4] != 0x0D
            || pngBytes[5] != 0x0A
            || pngBytes[6] != 0x1A
            || pngBytes[7] != 0x0A)
        {
            throw new InvalidDataException("The file is not a valid PNG image.");
        }

        var offset = 8;
        var width = 0;
        var height = 0;
        var bitDepth = 0;
        var colorType = 0;
        var compressionMethod = 0;
        var filterMethod = 0;
        var interlaceMethod = 0;
        byte[]? palette = null;
        byte[]? transparency = null;
        using var idat = new MemoryStream();

        while (offset + 8 <= pngBytes.Length)
        {
            var chunkLength = ReadPngInt32(pngBytes, offset);
            offset += 4;
            if (chunkLength < 0 || offset + 4 + chunkLength + 4 > pngBytes.Length)
                throw new InvalidDataException("PNG chunk length is invalid.");

            var chunkType = Encoding.ASCII.GetString(pngBytes, offset, 4);
            offset += 4;
            var chunkDataOffset = offset;

            switch (chunkType)
            {
                case "IHDR":
                    if (chunkLength < 13)
                        throw new InvalidDataException("PNG header is invalid.");

                    width = ReadPngInt32(pngBytes, chunkDataOffset);
                    height = ReadPngInt32(pngBytes, chunkDataOffset + 4);
                    bitDepth = pngBytes[chunkDataOffset + 8];
                    colorType = pngBytes[chunkDataOffset + 9];
                    compressionMethod = pngBytes[chunkDataOffset + 10];
                    filterMethod = pngBytes[chunkDataOffset + 11];
                    interlaceMethod = pngBytes[chunkDataOffset + 12];
                    break;
                case "IDAT":
                    idat.Write(pngBytes, chunkDataOffset, chunkLength);
                    break;
                case "PLTE":
                    palette = new byte[chunkLength];
                    Buffer.BlockCopy(pngBytes, chunkDataOffset, palette, 0, chunkLength);
                    break;
                case "tRNS":
                    transparency = new byte[chunkLength];
                    Buffer.BlockCopy(pngBytes, chunkDataOffset, transparency, 0, chunkLength);
                    break;
            }

            offset = chunkDataOffset + chunkLength + 4;
            if (chunkType == "IEND")
                break;
        }

        if (width <= 0 || height <= 0 || idat.Length == 0)
            throw new InvalidDataException("PNG image data is incomplete.");
        if (bitDepth != 8 || compressionMethod != 0 || filterMethod != 0 || interlaceMethod != 0)
            throw new NotSupportedException("Only non-interlaced 8-bit PNG images are supported.");

        var bytesPerPixel = GetPngBytesPerPixel(colorType);
        var decompressed = InflatePngData(idat.ToArray());
        var pixels = UnfilterPngRows(decompressed, width, height, bytesPerPixel);
        var (rgbBytes, alphaBytes) = ConvertPngPixelsForPdf(pixels, width, height, colorType, palette, transparency);
        return new PdfImageResource(resourceName, width, height, rgbBytes, alphaBytes);
    }

    private static int GetPngBytesPerPixel(int colorType) =>
        colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new NotSupportedException("PNG color type is not supported.")
        };

    private static byte[] InflatePngData(byte[] compressedBytes)
    {
        using var input = new MemoryStream(compressedBytes);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] UnfilterPngRows(byte[] decompressedBytes, int width, int height, int bytesPerPixel)
    {
        var stride = checked(width * bytesPerPixel);
        var pixels = new byte[checked(stride * height)];
        var sourceOffset = 0;

        for (var row = 0; row < height; row++)
        {
            if (sourceOffset >= decompressedBytes.Length)
                throw new InvalidDataException("PNG scanline data is incomplete.");

            var filterType = decompressedBytes[sourceOffset++];
            var rowOffset = row * stride;
            if (sourceOffset + stride > decompressedBytes.Length)
                throw new InvalidDataException("PNG scanline data is incomplete.");

            Buffer.BlockCopy(decompressedBytes, sourceOffset, pixels, rowOffset, stride);
            sourceOffset += stride;

            for (var index = 0; index < stride; index++)
            {
                var left = index >= bytesPerPixel ? pixels[rowOffset + index - bytesPerPixel] : 0;
                var up = row > 0 ? pixels[rowOffset - stride + index] : 0;
                var upperLeft = row > 0 && index >= bytesPerPixel ? pixels[rowOffset - stride + index - bytesPerPixel] : 0;
                var raw = pixels[rowOffset + index];
                var value = filterType switch
                {
                    0 => raw,
                    1 => raw + left,
                    2 => raw + up,
                    3 => raw + ((left + up) / 2),
                    4 => raw + PaethPngPredictor(left, up, upperLeft),
                    _ => throw new InvalidDataException("PNG scanline filter is invalid.")
                };
                pixels[rowOffset + index] = (byte)(value & 0xFF);
            }
        }

        return pixels;
    }

    private static (byte[] RgbBytes, byte[]? AlphaBytes) ConvertPngPixelsForPdf(
        byte[] pixels,
        int width,
        int height,
        int colorType,
        byte[]? palette,
        byte[]? transparency)
    {
        var pixelCount = checked(width * height);
        var rgbBytes = new byte[checked(pixelCount * 3)];
        byte[]? alphaBytes = null;
        var hasTransparency = false;

        switch (colorType)
        {
            case 0:
            {
                var transparentGray = transparency is { Length: >= 2 }
                    ? ReadPngInt16(transparency, 0)
                    : (int?)null;
                if (transparentGray.HasValue)
                {
                    alphaBytes = new byte[pixelCount];
                    Array.Fill(alphaBytes, (byte)255);
                }

                for (var pixel = 0; pixel < pixelCount; pixel++)
                {
                    var gray = pixels[pixel];
                    var target = pixel * 3;
                    rgbBytes[target] = gray;
                    rgbBytes[target + 1] = gray;
                    rgbBytes[target + 2] = gray;

                    if (transparentGray.HasValue && gray == transparentGray.Value)
                    {
                        alphaBytes![pixel] = 0;
                        hasTransparency = true;
                    }
                }

                break;
            }
            case 2:
            {
                Buffer.BlockCopy(pixels, 0, rgbBytes, 0, rgbBytes.Length);
                if (transparency is { Length: >= 6 })
                {
                    var transparentRed = ReadPngInt16(transparency, 0);
                    var transparentGreen = ReadPngInt16(transparency, 2);
                    var transparentBlue = ReadPngInt16(transparency, 4);
                    alphaBytes = new byte[pixelCount];
                    Array.Fill(alphaBytes, (byte)255);

                    for (var pixel = 0; pixel < pixelCount; pixel++)
                    {
                        var source = pixel * 3;
                        if (pixels[source] == transparentRed
                            && pixels[source + 1] == transparentGreen
                            && pixels[source + 2] == transparentBlue)
                        {
                            alphaBytes[pixel] = 0;
                            hasTransparency = true;
                        }
                    }
                }

                break;
            }
            case 3:
            {
                if (palette is null || palette.Length < 3)
                    throw new InvalidDataException("Indexed PNG image has no palette.");

                if (transparency is { Length: > 0 })
                {
                    alphaBytes = new byte[pixelCount];
                    Array.Fill(alphaBytes, (byte)255);
                }

                for (var pixel = 0; pixel < pixelCount; pixel++)
                {
                    var paletteIndex = pixels[pixel];
                    var paletteOffset = paletteIndex * 3;
                    if (paletteOffset + 2 >= palette.Length)
                        throw new InvalidDataException("PNG palette index is invalid.");

                    var target = pixel * 3;
                    rgbBytes[target] = palette[paletteOffset];
                    rgbBytes[target + 1] = palette[paletteOffset + 1];
                    rgbBytes[target + 2] = palette[paletteOffset + 2];

                    if (alphaBytes is not null && paletteIndex < transparency!.Length)
                    {
                        alphaBytes[pixel] = transparency[paletteIndex];
                        hasTransparency |= transparency[paletteIndex] != 255;
                    }
                }

                break;
            }
            case 4:
            {
                alphaBytes = new byte[pixelCount];
                for (var pixel = 0; pixel < pixelCount; pixel++)
                {
                    var source = pixel * 2;
                    var gray = pixels[source];
                    var alpha = pixels[source + 1];
                    var target = pixel * 3;
                    rgbBytes[target] = gray;
                    rgbBytes[target + 1] = gray;
                    rgbBytes[target + 2] = gray;
                    alphaBytes[pixel] = alpha;
                    hasTransparency |= alpha != 255;
                }

                break;
            }
            case 6:
            {
                alphaBytes = new byte[pixelCount];
                for (var pixel = 0; pixel < pixelCount; pixel++)
                {
                    var source = pixel * 4;
                    var target = pixel * 3;
                    rgbBytes[target] = pixels[source];
                    rgbBytes[target + 1] = pixels[source + 1];
                    rgbBytes[target + 2] = pixels[source + 2];
                    alphaBytes[pixel] = pixels[source + 3];
                    hasTransparency |= pixels[source + 3] != 255;
                }

                break;
            }
            default:
                throw new NotSupportedException("PNG color type is not supported.");
        }

        return (rgbBytes, hasTransparency ? alphaBytes : null);
    }

    private static byte[] CompressPdfStream(byte[] contentBytes)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(contentBytes, 0, contentBytes.Length);
        }

        return output.ToArray();
    }

    private static int ReadPngInt32(byte[] bytes, int offset) =>
        (bytes[offset] << 24)
        | (bytes[offset + 1] << 16)
        | (bytes[offset + 2] << 8)
        | bytes[offset + 3];

    private static int ReadPngInt16(byte[] bytes, int offset) =>
        (bytes[offset] << 8) | bytes[offset + 1];

    private static int PaethPngPredictor(int left, int up, int upperLeft)
    {
        var prediction = left + up - upperLeft;
        var distanceLeft = Math.Abs(prediction - left);
        var distanceUp = Math.Abs(prediction - up);
        var distanceUpperLeft = Math.Abs(prediction - upperLeft);

        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpperLeft)
            return left;
        return distanceUp <= distanceUpperLeft ? up : upperLeft;
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

    private static void AppendPdfImage(StringBuilder content, string resourceName, double x, double y, double width, double height)
    {
        if (string.IsNullOrWhiteSpace(resourceName) || width <= 0 || height <= 0)
            return;

        AppendPdfCommand(
            content,
            "q {0:0.###} 0 0 {1:0.###} {2:0.###} {3:0.###} cm /{4} Do Q",
            width,
            height,
            x,
            y,
            resourceName);
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
        bool alignRight = false,
        string color = "0.05 0.09 0.15")
    {
        var text = FitPdfText(CleanPdfText(value), maxWidth, fontSize);
        var textX = x;
        if (alignRight)
        {
            textX = x + Math.Max(0, maxWidth - EstimatePdfTextWidth(text, fontSize));
        }

        AppendPdfCommand(
            content,
            "BT /{0} {1:0.###} Tf {2} rg 1 0 0 1 {3:0.###} {4:0.###} Tm ({5}) Tj ET",
            fontResource,
            fontSize,
            color,
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

    private sealed record AccountStatementPdfColumn(
        string Header,
        double Width,
        bool AlignRight,
        Func<AccountStatementInvoiceDto, string> ValueSelector);

    private sealed record PdfImageResource(
        string ResourceName,
        int Width,
        int Height,
        byte[] RgbBytes,
        byte[]? AlphaBytes);

    private sealed record PdfImageObject(
        PdfImageResource Resource,
        int ImageObjectNumber,
        int? AlphaObjectNumber);
}
