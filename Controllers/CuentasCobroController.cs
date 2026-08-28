using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.CuentasCobro;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using System.Globalization;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.CuentasCobro)]
public sealed class CuentasCobroController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private readonly IDataverseService _dataverse;

    public CuentasCobroController(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var now = ResolveColombiaNow();
        var model = new CuentasCobroPageViewModel
        {
            CurrentUser = await GetCurrentUserAsync(ct),
            InitialYear = now.Year,
            InitialMonth = now.Month
        };

        return View(model);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Data([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        try
        {
            var result = await _dataverse.GetCuentasCobroBoardAsync(year, month, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar las cuentas de cobro.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public IActionResult DownloadReport([FromBody] CuentaCobroReportDownloadRequest? request)
    {
        if (request is null || request.Rows is null || request.Rows.Count == 0)
            return BadRequest(CreateErrorPayload("No hay filas en pantalla para exportar."));

        try
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Cuentas de cobro");
            var rows = request.Rows.Select(row => RecomputeReportRow(row)).ToList();

            worksheet.Cell(1, 1).Value = "Reporte de cuentas de cobro";
            worksheet.Cell(2, 1).Value = "Periodo";
            worksheet.Cell(2, 2).Value = FirstNonEmpty(request.PeriodLabel, $"{request.Year:D4}-{request.Month:D2}");
            worksheet.Cell(3, 1).Value = "Filas exportadas";
            worksheet.Cell(3, 2).Value = rows.Count;
            worksheet.Cell(4, 1).Value = "Generado";
            worksheet.Cell(4, 2).Value = ResolveColombiaNow().ToString("dd/MM/yyyy HH:mm");

            var headers = new[]
            {
                "Receptor",
                "NIT o Cedula",
                "Fecha emision",
                "Fecha pago",
                "Valor total",
                "Total retenciones",
                "Valor pago",
                "Rete fuente % (legado)",
                "Rete fuente valor (legado)",
                "Detalle retenciones",
                "Validacion",
                "Impresion",
                "Adjunto",
                "Archivo adjunto",
                "Observaciones",
                "Creado",
                "Modificado",
                "Id registro"
            };

            for (var index = 0; index < headers.Length; index++)
            {
                worksheet.Cell(6, index + 1).Value = headers[index];
            }

            var rowIndex = 7;
            foreach (var row in rows)
            {
                worksheet.Cell(rowIndex, 1).Value = row.Receptor;
                worksheet.Cell(rowIndex, 2).Value = row.NitOCedula;
                WriteReportDateCell(worksheet.Cell(rowIndex, 3), row.FechaEmisionValue, row.FechaEmisionDisplay);
                WriteReportDateCell(worksheet.Cell(rowIndex, 4), row.FechaPagoValue, row.FechaPagoDisplay);
                worksheet.Cell(rowIndex, 5).Value = row.ValorTotal;
                worksheet.Cell(rowIndex, 6).Value = row.TotalRetentionsValue;
                worksheet.Cell(rowIndex, 7).Value = row.ValorPago;
                worksheet.Cell(rowIndex, 8).Value = row.ReteFuentePorcentaje / 100m;
                worksheet.Cell(rowIndex, 9).Value = row.ReteFuenteValor;
                worksheet.Cell(rowIndex, 10).Value = BuildRetentionSummary(row.Retentions);
                worksheet.Cell(rowIndex, 11).Value = row.TotalesCuadran ? "Cuadra" : "No cuadra";
                worksheet.Cell(rowIndex, 12).Value = row.Impresa ? "Impresa" : "Pendiente";
                worksheet.Cell(rowIndex, 13).Value = row.HasAdjunto ? "Si" : "No";
                worksheet.Cell(rowIndex, 14).Value = row.AdjuntoFileName;
                worksheet.Cell(rowIndex, 15).Value = row.Observaciones;
                worksheet.Cell(rowIndex, 16).Value = row.CreatedOnDisplay;
                worksheet.Cell(rowIndex, 17).Value = row.ModifiedOnDisplay;
                worksheet.Cell(rowIndex, 18).Value = row.RecordId;
                rowIndex++;
            }

            worksheet.Cell(rowIndex, 1).Value = "Total";
            worksheet.Cell(rowIndex, 2).Value = $"{rows.Count:N0} filas";
            worksheet.Cell(rowIndex, 5).Value = rows.Sum(static row => row.ValorTotal);
            worksheet.Cell(rowIndex, 6).Value = rows.Sum(static row => row.TotalRetentionsValue);
            worksheet.Cell(rowIndex, 7).Value = rows.Sum(static row => row.ValorPago);
            worksheet.Cell(rowIndex, 9).Value = rows.Sum(static row => row.ReteFuenteValor);

            var lastDataRow = Math.Max(rowIndex, 7);
            worksheet.Range(1, 1, 1, headers.Length).Merge().Style.Font.Bold = true;
            worksheet.Range(1, 1, 1, headers.Length).Style.Font.FontSize = 15;
            worksheet.Range(2, 1, 4, 1).Style.Font.Bold = true;
            worksheet.Range(6, 1, 6, headers.Length).Style.Font.Bold = true;
            worksheet.Range(6, 1, 6, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF2FF");
            worksheet.Range(rowIndex, 1, rowIndex, headers.Length).Style.Font.Bold = true;
            worksheet.Range(rowIndex, 1, rowIndex, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F9FF");
            worksheet.Range(7, 5, lastDataRow, 5).Style.NumberFormat.Format = "$ #,##0.00";
            worksheet.Range(7, 6, lastDataRow, 7).Style.NumberFormat.Format = "$ #,##0.00";
            worksheet.Range(7, 8, Math.Max(rowIndex - 1, 7), 8).Style.NumberFormat.Format = "0.0000%";
            worksheet.Range(7, 9, lastDataRow, 9).Style.NumberFormat.Format = "$ #,##0.00";
            worksheet.Column(10).Style.Alignment.WrapText = true;
            worksheet.Range(6, 1, Math.Max(rowIndex, 7), headers.Length).SetAutoFilter();
            worksheet.SheetView.FreezeRows(6);
            worksheet.Columns().AdjustToContents();
            CreateRetentionsReportWorksheet(workbook, rows);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            var periodToken = BuildSafeReportFileName(FirstNonEmpty(request.PeriodLabel, $"{request.Year:D4}-{request.Month:D2}"));
            var fileName = $"cuentas-cobro-{periodToken}-{ResolveColombiaNow():yyyyMMdd}.xlsx";
            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible descargar el reporte de cuentas de cobro.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Save([FromBody] CuentaCobroSaveRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar la fila a guardar."));

        try
        {
            var result = await _dataverse.SaveCuentaCobroAsync(request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar la cuenta de cobro.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [RequestSizeLimit(134217728)]
    [RequestFormLimits(MultipartBodyLengthLimit = 134217728)]
    public async Task<IActionResult> UploadFile(string recordId, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
            return BadRequest(CreateErrorPayload("Debes seleccionar un archivo valido."));

        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);

            var result = await _dataverse.UploadCuentaCobroAttachmentAsync(
                recordId,
                file.FileName,
                file.ContentType,
                buffer.ToArray(),
                ct);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar el adjunto de la cuenta de cobro.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> DownloadFile(string recordId, CancellationToken ct)
    {
        try
        {
            var file = await _dataverse.DownloadCuentaCobroAttachmentAsync(recordId, ct);
            if (file is null || file.Content.Length == 0)
                return NotFound();

            return File(file.Content, file.ContentType, file.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible descargar el adjunto de la cuenta de cobro.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> MarkPrinted([FromBody] string? recordId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recordId))
            return BadRequest(CreateErrorPayload("Debes indicar la cuenta de cobro a imprimir."));

        try
        {
            var result = await _dataverse.MarkCuentaCobroAsPrintedAsync(recordId, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible marcar la cuenta de cobro como impresa.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Print(string recordId, int autoprint = 0, CancellationToken ct = default)
    {
        try
        {
            var model = new CuentaCobroPrintViewModel
            {
                CurrentUser = await GetCurrentUserAsync(ct),
                Record = await _dataverse.GetCuentaCobroByIdAsync(recordId, ct),
                AutoPrint = autoprint == 1,
                PrintedAtDisplay = ResolveColombiaNow().ToString("dd/MM/yyyy HH:mm")
            };

            return View(model);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible generar la vista de impresion de la cuenta de cobro.", ex));
        }
    }

    private async Task<CurrentUserInfo> GetCurrentUserAsync(CancellationToken ct)
    {
        return await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
    }

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

    private static CuentaCobroRowDto RecomputeReportRow(CuentaCobroRowDto row)
    {
        row.ValorTotal = RoundCurrency(row.ValorTotal);
        row.ValorPago = RoundCurrency(row.ValorPago);
        row.Retentions = NormalizeReportRetentions(row);
        row.TotalRetentionsValue = RoundCurrency(row.Retentions.Sum(static item => item.Value));
        var reteFuente = row.Retentions.FirstOrDefault(item =>
            string.Equals(item.Kind, "ReteFuente", StringComparison.OrdinalIgnoreCase));
        row.ReteFuentePorcentaje = reteFuente?.Rate ?? 0m;
        row.ReteFuenteValor = reteFuente?.Value ?? 0m;
        row.TotalesCuadran = Math.Abs(row.ValorTotal - (row.ValorPago + row.TotalRetentionsValue)) <= 0.01m;
        return row;
    }

    private static List<CuentaCobroRetentionDto> NormalizeReportRetentions(CuentaCobroRowDto row)
    {
        var source = row.Retentions ?? new List<CuentaCobroRetentionDto>();
        var normalized = source
            .Where(item => item is not null)
            .Select(item =>
            {
                var kind = FirstNonEmpty(item.Kind, "Otra");
                var baseValue = RoundCurrency(item.BaseValue);
                var rate = Math.Round(item.Rate, 4, MidpointRounding.AwayFromZero);
                var value = RoundCurrency(item.Value);
                if (value == 0m && baseValue > 0m && rate > 0m)
                {
                    var divisor = string.Equals(kind, "ReteICA", StringComparison.OrdinalIgnoreCase) ? 1000m : 100m;
                    value = RoundCurrency(baseValue * rate / divisor);
                }

                return new CuentaCobroRetentionDto
                {
                    Kind = kind,
                    Label = FirstNonEmpty(item.Label, ResolveRetentionLabel(kind)),
                    TaxId = item.TaxId?.Trim() ?? "",
                    AccountCode = item.AccountCode?.Trim() ?? "",
                    BaseValue = baseValue,
                    Rate = rate,
                    Value = value
                };
            })
            .Where(item => item.Value > 0m)
            .ToList();

        if (normalized.Count == 0 && (row.ReteFuentePorcentaje > 0m || row.ReteFuenteValor > 0m))
        {
            var rate = Math.Round(row.ReteFuentePorcentaje, 4, MidpointRounding.AwayFromZero);
            var value = RoundCurrency(row.ReteFuenteValor);
            if (value == 0m && rate > 0m)
                value = RoundCurrency(row.ValorTotal * rate / 100m);

            normalized.Add(new CuentaCobroRetentionDto
            {
                Kind = "ReteFuente",
                Label = ResolveRetentionLabel("ReteFuente"),
                BaseValue = row.ValorTotal,
                Rate = rate,
                Value = value
            });
        }

        return normalized;
    }

    private static string ResolveRetentionLabel(string kind)
    {
        return kind switch
        {
            "ReteFuente" => "Retencion en la fuente",
            "ReteICA" => "Retencion ICA",
            "RteIVA" => "IVA retenido",
            _ => "Otra retencion"
        };
    }

    private static string BuildRetentionSummary(IReadOnlyList<CuentaCobroRetentionDto>? retentions)
    {
        if (retentions is null || retentions.Count == 0)
            return "Sin retenciones";

        return string.Join(
            Environment.NewLine,
            retentions.Select(item =>
                $"{FirstNonEmpty(item.Label, ResolveRetentionLabel(item.Kind))}: base {item.BaseValue:N2}; tasa {item.Rate:N4}{(string.Equals(item.Kind, "ReteICA", StringComparison.OrdinalIgnoreCase) ? "‰" : "%")}; valor {item.Value:N2}; cuenta {FirstNonEmpty(item.AccountCode, "sin cuenta")}; impuesto {FirstNonEmpty(item.TaxId, "sin ID")}"));
    }

    private static void CreateRetentionsReportWorksheet(
        XLWorkbook workbook,
        IReadOnlyList<CuentaCobroRowDto> rows)
    {
        var worksheet = workbook.Worksheets.Add("Retenciones");
        var headers = new[]
        {
            "Receptor",
            "NIT o Cedula",
            "Fecha emision",
            "Tipo",
            "Etiqueta",
            "ID impuesto Siigo",
            "Cuenta contable",
            "Base",
            "Tasa",
            "Unidad",
            "Valor",
            "Id registro"
        };

        for (var index = 0; index < headers.Length; index++)
            worksheet.Cell(1, index + 1).Value = headers[index];

        var rowIndex = 2;
        foreach (var row in rows)
        {
            foreach (var retention in row.Retentions)
            {
                worksheet.Cell(rowIndex, 1).Value = row.Receptor;
                worksheet.Cell(rowIndex, 2).Value = row.NitOCedula;
                WriteReportDateCell(worksheet.Cell(rowIndex, 3), row.FechaEmisionValue, row.FechaEmisionDisplay);
                worksheet.Cell(rowIndex, 4).Value = retention.Kind;
                worksheet.Cell(rowIndex, 5).Value = retention.Label;
                worksheet.Cell(rowIndex, 6).Value = retention.TaxId;
                worksheet.Cell(rowIndex, 7).Value = retention.AccountCode;
                worksheet.Cell(rowIndex, 8).Value = retention.BaseValue;
                worksheet.Cell(rowIndex, 9).Value = retention.Rate;
                worksheet.Cell(rowIndex, 10).Value = string.Equals(retention.Kind, "ReteICA", StringComparison.OrdinalIgnoreCase) ? "Por mil" : "Por ciento";
                worksheet.Cell(rowIndex, 11).Value = retention.Value;
                worksheet.Cell(rowIndex, 12).Value = row.RecordId;
                rowIndex++;
            }
        }

        worksheet.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
        worksheet.Range(1, 1, 1, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF2FF");
        if (rowIndex > 2)
        {
            worksheet.Range(2, 8, rowIndex - 1, 8).Style.NumberFormat.Format = "$ #,##0.00";
            worksheet.Range(2, 9, rowIndex - 1, 9).Style.NumberFormat.Format = "0.0000";
            worksheet.Range(2, 11, rowIndex - 1, 11).Style.NumberFormat.Format = "$ #,##0.00";
            worksheet.Range(1, 1, rowIndex - 1, headers.Length).SetAutoFilter();
        }

        worksheet.SheetView.FreezeRows(1);
        worksheet.Columns().AdjustToContents();
    }

    private static void WriteReportDateCell(IXLCell cell, string value, string display)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            cell.Value = date.ToDateTime(TimeOnly.MinValue);
            cell.Style.DateFormat.Format = "dd/mm/yyyy";
            return;
        }

        cell.Value = display;
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string BuildSafeReportFileName(string value)
    {
        var cleaned = string.Join("-", (value ?? "reporte")
            .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        cleaned = cleaned
            .Replace(" ", "-", StringComparison.OrdinalIgnoreCase)
            .Trim('-');

        return string.IsNullOrWhiteSpace(cleaned)
            ? "reporte"
            : cleaned.ToLowerInvariant();
    }

    private static DateTimeOffset ResolveColombiaNow()
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

        return utcNow;
    }
}
