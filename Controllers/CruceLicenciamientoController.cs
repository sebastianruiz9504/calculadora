using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Licenciamiento;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using System.Globalization;
using System.Text;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.CruceLicenciamiento)]
public sealed class CruceLicenciamientoController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly CultureInfo ExportCulture = CultureInfo.GetCultureInfo("es-CO");
    private readonly IDataverseService _dataverse;

    public CruceLicenciamientoController(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        return View(new LicenciamientoCrucePageViewModel
        {
            CurrentUser = await GetCurrentUserAsync(ct),
            DefaultYear = 0,
            DefaultMonth = 0,
            DefaultPeriodMode = "month"
        });
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Graficos(CancellationToken ct)
    {
        return View(new LicenciamientoCrucePageViewModel
        {
            CurrentUser = await GetCurrentUserAsync(ct),
            DefaultYear = 0,
            DefaultMonth = 0,
            DefaultPeriodMode = "month"
        });
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Data(
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] string periodMode = "month",
        CancellationToken ct = default)
    {
        try
        {
            return Ok(await _dataverse.GetLicenciamientoCruceDashboardAsync(
                year,
                month,
                periodMode,
                ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible construir el cruce de licenciamiento.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Export(
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] string periodMode = "month",
        [FromQuery] string segmentKey = "",
        [FromQuery] string sortKey = "",
        [FromQuery] string sortDirection = "",
        CancellationToken ct = default)
    {
        try
        {
            var dashboard = await _dataverse.GetLicenciamientoCruceDashboardAsync(
                year,
                month,
                periodMode,
                ct);
            var segment = ResolveExportSegment(dashboard, segmentKey);
            if (segment is null || segment.Rows.Count == 0)
                return BadRequest(CreateErrorPayload("No hay registros para descargar"));

            var rows = SortExportRows(segment.Rows, sortKey, sortDirection).ToList();
            if (rows.Count == 0)
                return BadRequest(CreateErrorPayload("No hay registros para descargar"));

            using var workbook = BuildExportWorkbook(dashboard, segment, rows);
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return File(
                stream.ToArray(),
                ExcelContentType,
                BuildExportFileName(dashboard, segment));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible descargar el listado de cruce de licenciamiento.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UpdateCostContractType([FromBody] LicenciamientoUpdateContractTypeRequestDto? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Solicitud invalida."));

        try
        {
            return Ok(await _dataverse.UpdateLicenciamientoContractTypeAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible actualizar el tipo de contrato del consumo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UpdateBillingContractType([FromBody] BillingInvoicesContractTypeUpdateRequestDto? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Solicitud invalida."));

        try
        {
            return Ok(await _dataverse.UpdateBillingInvoicesContractTypeAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible actualizar el tipo de contrato de la factura.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UpdateBillingVertical([FromBody] LicenciamientoCruceUpdateBillingVerticalRequestDto? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Solicitud invalida."));

        try
        {
            return Ok(await _dataverse.UpdateLicenciamientoCruceBillingVerticalAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible actualizar la vertical de la factura.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UpdateCostAccount([FromBody] LicenciamientoCruceUpdateCostAccountRequestDto? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Solicitud invalida."));

        try
        {
            return Ok(await _dataverse.UpdateLicenciamientoCruceCostAccountAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible actualizar el Account ID del consumo.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SearchAccounts([FromQuery] string query = "", [FromQuery] int top = 12, CancellationToken ct = default)
    {
        try
        {
            return Ok(await _dataverse.SearchLicenciamientoCruceAccountsAsync(query, top, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible buscar Account IDs de licenciamiento.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SaveAccountMapping([FromBody] LicenciamientoCruceSaveAccountMappingRequestDto? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Solicitud invalida."));

        try
        {
            return Ok(await _dataverse.SaveLicenciamientoCruceAccountMappingAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible guardar el mapeo de Account ID.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UpdateCostInvoiceDate([FromBody] LicenciamientoCruceUpdateCostInvoiceDateRequestDto? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Solicitud invalida."));

        try
        {
            return Ok(await _dataverse.UpdateLicenciamientoCruceCostInvoiceDateAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible mover el costo al mes seleccionado.", ex));
        }
    }

    private async Task<CurrentUserInfo> GetCurrentUserAsync(CancellationToken ct) =>
        await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo();

    private static XLWorkbook BuildExportWorkbook(
        LicenciamientoCruceDashboardDto dashboard,
        LicenciamientoCruceMatrixSegmentDto segment,
        IReadOnlyList<LicenciamientoCruceMatrixClientRowDto> rows)
    {
        var workbook = new XLWorkbook();
        var generatedAt = ResolveBogotaNow().DateTime;
        AddExportListWorksheet(workbook, dashboard, segment, rows, generatedAt);
        AddExportSummaryWorksheet(workbook, dashboard, segment, rows, generatedAt);
        AddExportDetailWorksheet(workbook, dashboard, segment, generatedAt);
        return workbook;
    }

    private static void AddExportListWorksheet(
        XLWorkbook workbook,
        LicenciamientoCruceDashboardDto dashboard,
        LicenciamientoCruceMatrixSegmentDto segment,
        IReadOnlyList<LicenciamientoCruceMatrixClientRowDto> rows,
        DateTime generatedAt)
    {
        var worksheet = workbook.Worksheets.Add("Listado");
        var headers = new[]
        {
            "Cliente",
            "ID cliente",
            "NIT cliente",
            "Grupo empresarial",
            "Periodo cargado",
            "Tipo",
            "Vertical",
            "Producto / licencia",
            "Mes consumo",
            "Mes facturacion",
            "Costo",
            "Venta sin IVA",
            "% utilidad",
            "Utilidad",
            "Estado cruce",
            "Estado margen",
            "Fecha generacion"
        };

        WriteHeaders(worksheet, headers);
        var rowIndex = 2;
        var periodRange = ResolveConsumptionRange(dashboard);
        var consumptionLabel = FormatMonthRange(periodRange.Start, periodRange.End);
        var billingLabel = FormatMonthRange(periodRange.Start.AddMonths(1), periodRange.End.AddMonths(1));

        foreach (var row in rows)
        {
            var detailRows = GetSegmentRowsForClient(dashboard, segment.Key, row.RowKey);
            worksheet.Cell(rowIndex, 1).Value = FirstNonEmpty(row.Cliente, "Cliente sin nombre");
            worksheet.Cell(rowIndex, 2).Value = row.ClienteId;
            worksheet.Cell(rowIndex, 3).Value = row.NitCliente;
            worksheet.Cell(rowIndex, 4).Value = FirstNonEmpty(row.GrupoEmpresarial, row.GrupoEmpresarialId);
            worksheet.Cell(rowIndex, 5).Value = dashboard.PeriodLabel;
            worksheet.Cell(rowIndex, 6).Value = segment.Label;
            worksheet.Cell(rowIndex, 7).Value = ResolveMostCommonText(detailRows.Select(static item => item.Vertical), "");
            worksheet.Cell(rowIndex, 8).Value = BuildProductSummary(detailRows);
            worksheet.Cell(rowIndex, 9).Value = consumptionLabel;
            worksheet.Cell(rowIndex, 10).Value = billingLabel;
            worksheet.Cell(rowIndex, 11).Value = row.TotalCostoLicenciamiento;
            worksheet.Cell(rowIndex, 12).Value = row.TotalFacturacionSinIva;
            SetPercentCell(worksheet.Cell(rowIndex, 13), row.TotalUtilidadPct);
            worksheet.Cell(rowIndex, 14).Value = row.TotalUtilidad;
            worksheet.Cell(rowIndex, 15).Value = ResolveCrossStatus(detailRows);
            worksheet.Cell(rowIndex, 16).Value = ResolveMarginStatus(row.TotalUtilidad, row.TotalUtilidadPct);
            worksheet.Cell(rowIndex, 17).Value = generatedAt;
            rowIndex++;
        }

        var totalRow = rowIndex;
        worksheet.Cell(totalRow, 1).Value = "Total";
        worksheet.Cell(totalRow, 2).Value = $"{rows.Count:N0} cliente(s)";
        worksheet.Cell(totalRow, 11).Value = rows.Sum(static row => row.TotalCostoLicenciamiento);
        worksheet.Cell(totalRow, 12).Value = rows.Sum(static row => row.TotalFacturacionSinIva);
        SetPercentCell(worksheet.Cell(totalRow, 13), segment.Totals.MargenBrutoPct);
        worksheet.Cell(totalRow, 14).Value = rows.Sum(static row => row.TotalUtilidad);

        StyleExportWorksheet(worksheet, totalRow, headers.Length, headerRow: 1);
        worksheet.Range(2, 11, totalRow, 12).Style.NumberFormat.Format = "$ #,##0";
        worksheet.Range(2, 14, totalRow, 14).Style.NumberFormat.Format = "$ #,##0";
        worksheet.Range(2, 17, totalRow, 17).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
    }

    private static void AddExportSummaryWorksheet(
        XLWorkbook workbook,
        LicenciamientoCruceDashboardDto dashboard,
        LicenciamientoCruceMatrixSegmentDto segment,
        IReadOnlyList<LicenciamientoCruceMatrixClientRowDto> rows,
        DateTime generatedAt)
    {
        var worksheet = workbook.Worksheets.Add("Resumen");
        var periodRange = ResolveConsumptionRange(dashboard);
        var billingStart = periodRange.Start.AddMonths(1);
        var billingEnd = periodRange.End.AddMonths(1);

        worksheet.Cell(1, 1).Value = "Cruce Licenciamiento";
        worksheet.Cell(2, 1).Value = "Periodo cargado";
        worksheet.Cell(2, 2).Value = dashboard.PeriodLabel;
        worksheet.Cell(3, 1).Value = "Tipo";
        worksheet.Cell(3, 2).Value = segment.Label;
        worksheet.Cell(4, 1).Value = "Mes consumo";
        worksheet.Cell(4, 2).Value = FormatMonthRange(periodRange.Start, periodRange.End);
        worksheet.Cell(5, 1).Value = "Mes facturacion";
        worksheet.Cell(5, 2).Value = FormatMonthRange(billingStart, billingEnd);
        worksheet.Cell(6, 1).Value = "Fecha generacion";
        worksheet.Cell(6, 2).Value = generatedAt;
        worksheet.Cell(8, 1).Value = "Clientes";
        worksheet.Cell(8, 2).Value = rows.Count;
        worksheet.Cell(9, 1).Value = "Costo";
        worksheet.Cell(9, 2).Value = segment.Totals.TotalCostosLicenciamiento;
        worksheet.Cell(10, 1).Value = "Venta sin IVA";
        worksheet.Cell(10, 2).Value = segment.Totals.TotalFacturacionRelacionada;
        worksheet.Cell(11, 1).Value = "Utilidad";
        worksheet.Cell(11, 2).Value = segment.Totals.MargenBrutoTotal;
        worksheet.Cell(12, 1).Value = "% utilidad";
        SetPercentCell(worksheet.Cell(12, 2), segment.Totals.MargenBrutoPct);
        worksheet.Cell(13, 1).Value = "Margen negativo";
        worksheet.Cell(13, 2).Value = segment.NegativeMarginCount;

        worksheet.Range(1, 1, 1, 2).Merge().Style.Font.Bold = true;
        worksheet.Range(1, 1, 1, 2).Style.Font.FontSize = 16;
        worksheet.Range(2, 1, 13, 1).Style.Font.Bold = true;
        worksheet.Range(9, 2, 11, 2).Style.NumberFormat.Format = "$ #,##0";
        worksheet.Cell(6, 2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        worksheet.Columns().AdjustToContents();
    }

    private static void AddExportDetailWorksheet(
        XLWorkbook workbook,
        LicenciamientoCruceDashboardDto dashboard,
        LicenciamientoCruceMatrixSegmentDto segment,
        DateTime generatedAt)
    {
        var rows = dashboard.Rows
            .Where(row => SegmentMatches(row, segment.Key))
            .OrderBy(static row => row.MesCierre, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Cliente, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (rows.Count == 0)
            return;

        var worksheet = workbook.Worksheets.Add("Detalle cruce");
        var headers = new[]
        {
            "Cliente",
            "ID cliente",
            "NIT cliente",
            "Grupo empresarial",
            "Vertical",
            "Tipo de licenciamiento",
            "Producto / licencia",
            "Mes consumo",
            "Mes facturacion",
            "Costo neto",
            "Venta sin IVA",
            "Utilidad",
            "% utilidad",
            "Estado cruce",
            "Estado margen",
            "Registros costo",
            "Registros facturacion",
            "Match score",
            "Fuente costo",
            "Fuente facturacion",
            "Fecha generacion"
        };

        WriteHeaders(worksheet, headers);
        var rowIndex = 2;
        foreach (var row in rows)
        {
            worksheet.Cell(rowIndex, 1).Value = FirstNonEmpty(row.Cliente, "Cliente sin nombre");
            worksheet.Cell(rowIndex, 2).Value = FirstNonEmpty(row.Trace?.BillingClientId, row.Trace?.CostClientId);
            worksheet.Cell(rowIndex, 3).Value = row.NitCliente;
            worksheet.Cell(rowIndex, 4).Value = FirstNonEmpty(row.GrupoEmpresarial, row.GrupoEmpresarialId);
            worksheet.Cell(rowIndex, 5).Value = row.Vertical;
            worksheet.Cell(rowIndex, 6).Value = row.TipoContrato;
            worksheet.Cell(rowIndex, 7).Value = BuildProductSummary(new[] { row });
            worksheet.Cell(rowIndex, 8).Value = row.MesCosto;
            worksheet.Cell(rowIndex, 9).Value = row.MesFacturacion;
            worksheet.Cell(rowIndex, 10).Value = row.CostoLicenciamiento;
            worksheet.Cell(rowIndex, 11).Value = row.FacturacionSinIva;
            worksheet.Cell(rowIndex, 12).Value = row.MargenBruto;
            SetPercentCell(worksheet.Cell(rowIndex, 13), row.MargenBrutoPct);
            worksheet.Cell(rowIndex, 14).Value = row.EstadoCruce;
            worksheet.Cell(rowIndex, 15).Value = ResolveMarginStatus(row.MargenBruto, row.MargenBrutoPct);
            worksheet.Cell(rowIndex, 16).Value = row.CostRecordCount;
            worksheet.Cell(rowIndex, 17).Value = row.BillingRecordCount;
            worksheet.Cell(rowIndex, 18).Value = row.MatchScore / 100m;
            worksheet.Cell(rowIndex, 19).Value = row.FuenteCosto;
            worksheet.Cell(rowIndex, 20).Value = row.FuenteFacturacion;
            worksheet.Cell(rowIndex, 21).Value = generatedAt;
            rowIndex++;
        }

        StyleExportWorksheet(worksheet, rowIndex - 1, headers.Length, headerRow: 1, highlightLastRow: false);
        worksheet.Range(2, 10, rowIndex - 1, 12).Style.NumberFormat.Format = "$ #,##0";
        worksheet.Range(2, 18, rowIndex - 1, 18).Style.NumberFormat.Format = "0.00%";
        worksheet.Range(2, 21, rowIndex - 1, 21).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
    }

    private static void WriteHeaders(IXLWorksheet worksheet, IReadOnlyList<string> headers)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            worksheet.Cell(1, index + 1).Value = headers[index];
        }
    }

    private static void StyleExportWorksheet(IXLWorksheet worksheet, int lastRow, int lastColumn, int headerRow, bool highlightLastRow = true)
    {
        var usedRange = worksheet.Range(headerRow, 1, lastRow, lastColumn);
        usedRange.Style.Font.FontName = "Aptos";
        worksheet.Range(headerRow, 1, headerRow, lastColumn).Style.Font.Bold = true;
        worksheet.Range(headerRow, 1, headerRow, lastColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF3FF");
        if (highlightLastRow)
        {
            worksheet.Range(lastRow, 1, lastRow, lastColumn).Style.Font.Bold = true;
            worksheet.Range(lastRow, 1, lastRow, lastColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F9FF");
        }
        worksheet.SheetView.FreezeRows(headerRow);
        worksheet.Range(headerRow, 1, lastRow, lastColumn).SetAutoFilter();
        worksheet.Columns().AdjustToContents();
    }

    private static void SetPercentCell(IXLCell cell, decimal? percent)
    {
        if (!percent.HasValue)
        {
            cell.Value = "N/A";
            return;
        }

        cell.Value = percent.Value / 100m;
        cell.Style.NumberFormat.Format = "0.00%";
    }

    private static LicenciamientoCruceMatrixSegmentDto? ResolveExportSegment(
        LicenciamientoCruceDashboardDto dashboard,
        string? segmentKey)
    {
        var segments = dashboard.MatrixSegments ?? Array.Empty<LicenciamientoCruceMatrixSegmentDto>();
        var key = (segmentKey ?? "").Trim();
        return segments.FirstOrDefault(segment => string.Equals(segment.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? segments.FirstOrDefault(segment => string.Equals(segment.Key, "all", StringComparison.OrdinalIgnoreCase))
            ?? segments.FirstOrDefault(segment => segment.RecordsCount > 0)
            ?? segments.FirstOrDefault();
    }

    private static IReadOnlyList<LicenciamientoCruceMatrixClientRowDto> SortExportRows(
        IReadOnlyList<LicenciamientoCruceMatrixClientRowDto> rows,
        string? sortKey,
        string? sortDirection)
    {
        var key = (sortKey ?? "").Trim();
        var direction = (sortDirection ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(key) || (direction != "asc" && direction != "desc"))
            return rows;

        var descending = direction == "desc";
        var indexedRows = rows.Select((row, index) => new ExportRow(row, index)).ToList();
        indexedRows.Sort((left, right) =>
        {
            var compare = CompareExportRows(left.Row, right.Row, key, descending);
            return compare != 0 ? compare : left.Index.CompareTo(right.Index);
        });

        return indexedRows.Select(static item => item.Row).ToList();
    }

    private static int CompareExportRows(
        LicenciamientoCruceMatrixClientRowDto left,
        LicenciamientoCruceMatrixClientRowDto right,
        string sortKey,
        bool descending)
    {
        if (string.Equals(sortKey, "client", StringComparison.OrdinalIgnoreCase))
        {
            var compare = string.Compare(left.Cliente, right.Cliente, ExportCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
            return descending ? -compare : compare;
        }

        return CompareNullableDecimal(
            ResolveExportSortValue(left, sortKey),
            ResolveExportSortValue(right, sortKey),
            descending);
    }

    private static decimal? ResolveExportSortValue(LicenciamientoCruceMatrixClientRowDto row, string sortKey)
    {
        if (string.Equals(sortKey, "totalCost", StringComparison.OrdinalIgnoreCase))
            return row.TotalCostoLicenciamiento;
        if (string.Equals(sortKey, "totalBilling", StringComparison.OrdinalIgnoreCase))
            return row.TotalFacturacionSinIva;
        if (string.Equals(sortKey, "totalPct", StringComparison.OrdinalIgnoreCase))
            return row.TotalUtilidadPct;
        if (string.Equals(sortKey, "totalUtility", StringComparison.OrdinalIgnoreCase))
            return row.TotalUtilidad;

        if (!sortKey.StartsWith("cell:", StringComparison.OrdinalIgnoreCase))
            return null;

        var parts = sortKey.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return null;

        var cell = row.Cells.FirstOrDefault(item => string.Equals(item.Mes, parts[1], StringComparison.OrdinalIgnoreCase));
        if (cell is null)
            return string.Equals(parts[2], "pct", StringComparison.OrdinalIgnoreCase) ? null : 0m;

        return parts[2].ToLowerInvariant() switch
        {
            "cost" => cell.CostoLicenciamiento,
            "billing" => cell.FacturacionSinIva,
            "pct" => cell.UtilidadPct,
            "utility" => cell.UtilidadValor,
            _ => null
        };
    }

    private static int CompareNullableDecimal(decimal? left, decimal? right, bool descending)
    {
        if (!left.HasValue && !right.HasValue)
            return 0;
        if (!left.HasValue)
            return 1;
        if (!right.HasValue)
            return -1;

        var compare = left.Value.CompareTo(right.Value);
        return descending ? -compare : compare;
    }

    private static IReadOnlyList<LicenciamientoCruceRowDto> GetSegmentRowsForClient(
        LicenciamientoCruceDashboardDto dashboard,
        string segmentKey,
        string clientKey) =>
        dashboard.Rows
            .Where(row => SegmentMatches(row, segmentKey)
                && string.Equals(row.MatrixClientKey, clientKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static bool SegmentMatches(LicenciamientoCruceRowDto row, string segmentKey) =>
        string.IsNullOrWhiteSpace(segmentKey)
        || string.Equals(segmentKey, "all", StringComparison.OrdinalIgnoreCase)
        || string.Equals(row.TipoContratoKey, segmentKey, StringComparison.OrdinalIgnoreCase);

    private static string ResolveCrossStatus(IReadOnlyList<LicenciamientoCruceRowDto> rows)
    {
        var statuses = rows
            .Select(static row => row.EstadoCruce)
            .Where(static status => !string.IsNullOrWhiteSpace(status))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (statuses.Count == 0)
            return "";
        if (statuses.Count == 1)
            return statuses[0];
        if (statuses.Any(status => status.Contains("sin", StringComparison.OrdinalIgnoreCase)))
            return "Con registros sin match";

        return "Multiple";
    }

    private static string ResolveMarginStatus(decimal utility, decimal? utilityPct)
    {
        if (utility < 0m)
            return "Margen negativo";
        if (!utilityPct.HasValue)
            return "N/A";
        if (utility > 0m)
            return "Margen positivo";

        return "Sin utilidad";
    }

    private static string BuildProductSummary(IEnumerable<LicenciamientoCruceRowDto> rows)
    {
        var products = rows
            .SelectMany(static row => row.Trace?.CostItems ?? Array.Empty<LicenciamientoCruceTraceItemDto>())
            .Select(static item => FirstNonEmpty(item.Producto, item.ProductoId))
            .Where(static product => !string.IsNullOrWhiteSpace(product))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static product => product, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        return products.Count == 0 ? "" : string.Join(" | ", products);
    }

    private static string ResolveMostCommonText(IEnumerable<string?> values, string fallback)
    {
        return values
            .Select(static value => (value ?? "").Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Key)
            .FirstOrDefault() ?? fallback;
    }

    private static (DateOnly Start, DateOnly End) ResolveConsumptionRange(LicenciamientoCruceDashboardDto dashboard)
    {
        var selected = new DateOnly(dashboard.SelectedYear, dashboard.SelectedMonth, 1);
        var start = (dashboard.PeriodMode ?? "month").Trim().ToLowerInvariant() switch
        {
            "quarter" => new DateOnly(selected.Year, (((selected.Month - 1) / 3) * 3) + 1, 1),
            "ytd" => new DateOnly(selected.Year, 1, 1),
            _ => selected
        };
        var end = (dashboard.PeriodMode ?? "month").Trim().ToLowerInvariant() switch
        {
            "quarter" => start.AddMonths(2),
            "ytd" => selected,
            _ => selected
        };

        return (start, end);
    }

    private static string FormatMonthRange(DateOnly start, DateOnly end) =>
        start == end
            ? FormatLongMonth(start)
            : $"{FormatLongMonth(start)} a {FormatLongMonth(end)}";

    private static string FormatLongMonth(DateOnly value)
    {
        var text = value.ToString("MMMM yyyy", ExportCulture);
        return ExportCulture.TextInfo.ToTitleCase(text);
    }

    private static string BuildExportFileName(
        LicenciamientoCruceDashboardDto dashboard,
        LicenciamientoCruceMatrixSegmentDto segment)
    {
        var selected = new DateOnly(dashboard.SelectedYear, dashboard.SelectedMonth, 1);
        var periodToken = (dashboard.PeriodMode ?? "month").Trim().ToLowerInvariant() switch
        {
            "quarter" => "trimestre",
            "ytd" => "acumulado",
            _ => "mes"
        };
        var consumptionMonth = BuildFileToken(selected.ToString("MMMM", ExportCulture));
        var billingMonth = BuildFileToken(selected.AddMonths(1).ToString("MMMM", ExportCulture));
        var typeToken = BuildFileToken(FirstNonEmpty(segment.Key, segment.Label, "tipo"));

        return $"cruce_licenciamiento_{periodToken}_{selected.Year}_{consumptionMonth}_factura_{billingMonth}_{typeToken}.xlsx";
    }

    private static string BuildFileToken(string value)
    {
        var normalized = (value ?? "")
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_');
        }

        var token = builder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .Trim('_');
        while (token.Contains("__", StringComparison.Ordinal))
        {
            token = token.Replace("__", "_", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(token) ? "sin_tipo" : token;
    }

    private static DateTimeOffset ResolveBogotaNow()
    {
        var utcNow = DateTimeOffset.UtcNow;
        foreach (var timeZoneId in new[] { "SA Pacific Standard Time", "America/Bogota" })
        {
            try
            {
                var timezone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return TimeZoneInfo.ConvertTime(utcNow, timezone);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return utcNow.ToOffset(TimeSpan.FromHours(-5));
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private sealed record ExportRow(LicenciamientoCruceMatrixClientRowDto Row, int Index);

    private object CreateErrorPayload(string message, Exception? ex = null)
    {
        var detail = BuildExceptionDetail(ex);
        return new
        {
            message,
            detail = string.Equals(detail, message, StringComparison.Ordinal) ? "" : detail,
            traceId = HttpContext.TraceIdentifier
        };
    }

    private static string BuildExceptionDetail(Exception? ex)
    {
        if (ex is null)
            return "";

        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (string.IsNullOrWhiteSpace(current.Message))
                continue;

            var trimmedMessage = current.Message.Trim();
            if (!messages.Contains(trimmedMessage, StringComparer.OrdinalIgnoreCase))
                messages.Add(trimmedMessage);
        }

        return string.Join(" | ", messages);
    }
}
