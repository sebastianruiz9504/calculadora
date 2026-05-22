using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.Reconciliation;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Conciliacion)]
public sealed class ConciliacionController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private readonly IDataverseService _dataverse;
    private readonly IFinancialReconciliationService _financialReconciliation;
    private readonly ISiigoService _siigo;

    public ConciliacionController(
        IDataverseService dataverse,
        IFinancialReconciliationService financialReconciliation,
        ISiigoService siigo)
    {
        _dataverse = dataverse;
        _financialReconciliation = financialReconciliation;
        _siigo = siigo;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        var (resolvedYear, resolvedMonth) = ResolvePeriod(year, month);
        var model = new ConciliacionPageViewModel
        {
            CurrentUser = await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo(),
            Board = await _dataverse.GetConciliacionBoardAsync(resolvedYear, resolvedMonth, ct)
        };

        return View(model);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SyncHealth([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        try
        {
            var (resolvedYear, resolvedMonth) = ResolvePeriod(year, month);
            var snapshot = await _financialReconciliation.BuildSnapshotAsync(resolvedYear, resolvedMonth, ct);
            return Ok(BuildSyncHealth(snapshot));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible consultar la salud de sincronizacion.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UpdateClientPaymentStatus(
        [FromBody] ConciliacionClientPaymentStatusRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar el cruce a actualizar."));

        try
        {
            return Ok(await _dataverse.UpdateConciliacionClientPaymentStatusAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible actualizar el cruce.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ValidateClientPaymentPreflight(
        [FromBody] ConciliacionClientPaymentStatusRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el cruce a validar."));

        try
        {
            return Ok(await _dataverse.ValidateConciliacionClientPaymentPreflightAsync(request.RecordId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible validar el borrador pre-Siigo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SearchDataverseInvoices(
        [FromBody] ConciliacionInvoiceSearchRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Indica el texto o valor para buscar facturas."));

        try
        {
            return Ok(await _dataverse.SearchConciliacionDataverseInvoicesAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible buscar facturas en Dataverse.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> AssignClientPaymentInvoice(
        [FromBody] ConciliacionAssignInvoiceRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId) || string.IsNullOrWhiteSpace(request.InvoiceRecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el cruce y la factura a asignar."));

        try
        {
            return Ok(await _dataverse.AssignConciliacionClientPaymentInvoiceAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible asignar la factura.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SimulateClientPaymentSiigoSend(
        [FromBody] ConciliacionClientPaymentStatusRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el cruce a simular."));

        try
        {
            return Ok(await _dataverse.SimulateConciliacionClientPaymentSiigoSendAsync(request.RecordId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible simular el envio a Siigo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SendClientPaymentToSiigo(
        [FromBody] ConciliacionClientPaymentStatusRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el cruce a enviar."));

        ConciliacionSiigoSendPreparedDto prepared;
        try
        {
            var taxes = await _siigo.GetTaxesAsync(ct);
            prepared = await _dataverse.PrepareConciliacionClientPaymentSiigoSendAsync(request.RecordId, ct, taxes);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible preparar el envio real a Siigo.", ex));
        }

        if (!prepared.CanSend || prepared.Payload is null)
        {
            return Ok(new ConciliacionSiigoSendResultDto
            {
                Message = prepared.Message,
                IsSuccess = false,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                Issues = prepared.Issues,
                Row = prepared.Row
            });
        }

        try
        {
            var siigoResult = await _siigo.CreateVoucherAsync(
                prepared.Payload,
                BuildSiigoIdempotencyKey(request.RecordId),
                ct);
            var documentLabel = string.IsNullOrWhiteSpace(siigoResult.Name)
                ? siigoResult.Id
                : siigoResult.Name;
            var message = string.IsNullOrWhiteSpace(documentLabel)
                ? "Pago enviado a Siigo."
                : $"Pago enviado a Siigo: {documentLabel}.";
            var dataverseResult = await _dataverse.MarkConciliacionClientPaymentSiigoSendResultAsync(
                request.RecordId,
                success: true,
                message: message,
                siigoId: siigoResult.Id,
                siigoName: siigoResult.Name,
                responseJson: siigoResult.RawJson,
                ct);

            return Ok(new ConciliacionSiigoSendResultDto
            {
                Message = dataverseResult.Message,
                IsSuccess = true,
                SiigoId = siigoResult.Id,
                SiigoName = siigoResult.Name,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                ResponseJson = siigoResult.RawJson,
                Row = dataverseResult.Row
            });
        }
        catch (InvalidOperationException ex)
        {
            var message = BuildExceptionDetail(ex);
            var dataverseResult = await _dataverse.MarkConciliacionClientPaymentSiigoSendResultAsync(
                request.RecordId,
                success: false,
                message: "Siigo rechazo el envio real.",
                responseJson: message,
                ct: ct);

            return Ok(new ConciliacionSiigoSendResultDto
            {
                Message = "Siigo rechazo el envio real. Revisa el detalle visible en la fila.",
                IsSuccess = false,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                Issues = new[] { message },
                Row = dataverseResult.Row
            });
        }
        catch (Exception ex)
        {
            var message = BuildExceptionDetail(ex);
            var dataverseResult = await _dataverse.MarkConciliacionClientPaymentSiigoSendResultAsync(
                request.RecordId,
                success: false,
                message: "No fue posible completar el envio real a Siigo.",
                responseJson: message,
                ct: ct);

            return StatusCode(StatusCodes.Status500InternalServerError, new ConciliacionSiigoSendResultDto
            {
                Message = "No fue posible completar el envio real a Siigo.",
                IsSuccess = false,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                Issues = new[] { message },
                Row = dataverseResult.Row
            });
        }
    }

    private static (int Year, int Month) ResolvePeriod(int? year, int? month)
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, MonthlyFinancialReconciliationHostedService.ResolveTimeZone("SA Pacific Standard Time"));
        var resolvedYear = year.GetValueOrDefault(now.Year);
        var resolvedMonth = month.GetValueOrDefault(now.Month);
        if (resolvedMonth is < 1 or > 12)
            resolvedMonth = now.Month;
        if (resolvedYear < 2020)
            resolvedYear = now.Year;

        return (resolvedYear, resolvedMonth);
    }

    private static ConciliacionSyncHealthDto BuildSyncHealth(FinancialReconciliationSnapshotResult snapshot)
    {
        var summary = snapshot.Summary;
        var items = new[]
        {
            BuildSyncHealthItem(
                key: "facturacion",
                label: "Facturacion",
                description: "Facturas de venta netas: facturas menos notas credito.",
                dataverseTotal: summary.DataverseBillingNet,
                siigoTotal: summary.SiigoBillingNet,
                differenceTotal: summary.BillingDifference,
                dataverseVat: summary.DataverseVatNet,
                siigoVat: summary.SiigoVatNet,
                vatDifference: summary.BillingVatDifference,
                dataverseCount: summary.DataverseBillingInvoiceCount,
                siigoCount: summary.SiigoBillingInvoiceCount,
                differenceRows: summary.BillingDifferenceCount,
                notes: $"NC Dataverse: {summary.DataverseBillingCreditNoteCount:N0}. NC Siigo: {summary.SiigoBillingCreditNoteCount:N0}."),
            BuildSyncHealthItem(
                key: "gastos",
                label: "Gastos",
                description: "Compras y gastos del periodo comparados por documento/proveedor/fecha/valor.",
                dataverseTotal: summary.PowerAppsExpenses,
                siigoTotal: summary.SiigoExpenses,
                differenceTotal: summary.PowerAppsExpenses - summary.SiigoExpenses,
                dataverseVat: summary.PowerAppsExpenseVat,
                siigoVat: summary.SiigoExpenseVat,
                vatDifference: summary.PowerAppsExpenseVat - summary.SiigoExpenseVat,
                dataverseCount: summary.PowerAppsExpenseCount,
                siigoCount: summary.SiigoExpenseCount,
                differenceRows: summary.ExpenseDifferenceCount,
                notes: "Dataverse corresponde a Power Apps/tabla de gastos.")
        };
        var totalDifferenceRows = items.Sum(static item => item.DifferenceRows);

        return new ConciliacionSyncHealthDto
        {
            Year = snapshot.Year,
            Month = snapshot.Month,
            PeriodLabel = snapshot.PeriodLabel,
            GeneratedAtDisplay = FormatSyncHealthDateTime(snapshot.GeneratedAt),
            StatusLabel = totalDifferenceRows == 0 ? "Sincronizado" : "Con diferencias",
            StatusTone = totalDifferenceRows == 0 ? "success" : "warning",
            TotalDifferenceRows = totalDifferenceRows,
            Items = items
        };
    }

    private static ConciliacionSyncHealthItemDto BuildSyncHealthItem(
        string key,
        string label,
        string description,
        decimal dataverseTotal,
        decimal siigoTotal,
        decimal differenceTotal,
        decimal dataverseVat,
        decimal siigoVat,
        decimal vatDifference,
        int dataverseCount,
        int siigoCount,
        int differenceRows,
        string notes)
    {
        var countDifference = dataverseCount - siigoCount;
        return new ConciliacionSyncHealthItemDto
        {
            Key = key,
            Label = label,
            Description = description,
            DataverseTotal = dataverseTotal,
            SiigoTotal = siigoTotal,
            DifferenceTotal = differenceTotal,
            DataverseVat = dataverseVat,
            SiigoVat = siigoVat,
            VatDifference = vatDifference,
            DataverseCount = dataverseCount,
            SiigoCount = siigoCount,
            CountDifference = countDifference,
            DifferenceRows = differenceRows,
            StatusLabel = differenceRows == 0 ? "Conciliado" : "Revisar",
            StatusTone = differenceRows == 0 ? "success" : "warning",
            Notes = notes
        };
    }

    private static string FormatSyncHealthDateTime(DateTimeOffset value)
    {
        var timeZone = MonthlyFinancialReconciliationHostedService.ResolveTimeZone("SA Pacific Standard Time");
        var local = TimeZoneInfo.ConvertTime(value, timeZone);
        return local.ToString("yyyy-MM-dd HH:mm");
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

    private static string BuildSiigoIdempotencyKey(string recordId)
    {
        var compact = new string((recordId ?? "")
            .Where(char.IsLetterOrDigit)
            .ToArray());
        if (compact.Length == 0)
            compact = Guid.NewGuid().ToString("N");

        var key = $"CNC{compact}";
        return key[..Math.Min(30, key.Length)];
    }
}
