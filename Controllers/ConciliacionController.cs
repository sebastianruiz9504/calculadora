using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Automation;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Models.Contracts;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.Reconciliation;
using CotizadorInterno.Web.Models.RegistroPagosClientes;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Conciliacion)]
public sealed class ConciliacionController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    internal const decimal ClientPaymentInvoiceDifferenceTolerance = 2000m;
    private const string DianSupplierPurchaseVatAccountCode = "240803";
    private const string DianSupplierPurchaseVatDescription = "Iva Descontable";
    private const string AccountingVoucherDefaultThirdPartyIdentification = "900399875";
    private const string InternalTransferBancolombiaIdentification = "890903938";
    private const string CuentaCobroSupportDocumentProcessingState = "ProcesandoDocumentoSoporteSiigo";
    private const string CuentaCobroSupportDocumentVerificationState = "VerificacionDocumentoSoporteSiigoPendiente";
    private const string CuentaCobroSupportDocumentPendingPaymentState = "DocumentoSoporteCreadoPagoPendiente";
    private const string CuentaCobroSupportDocumentAmbiguousMarker = "[SIIGO_SUPPORT_DOCUMENT_WRITE_AMBIGUOUS]";
    private const long DianSupplierRutAnalysisLimit = 25L * 1024L * 1024L;
    private static readonly SemaphoreSlim DianSupplierCreationGate = new(1, 1);
    private static readonly HashSet<string> SupplierPaymentStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "PAGO",
        "FACTURA",
        "DOCUMENTO",
        "COMPRA",
        "PROVEEDOR",
        "TRANSFERENCIA",
        "BANCO",
        "CUENTA",
        "RETENCION",
        "RETEFUENTE",
        "RETEICA"
    };
    private readonly IDataverseService _dataverse;
    private readonly IFinancialReconciliationService _financialReconciliation;
    private readonly ICashFlowImportService _cashFlowImportService;
    private readonly IDianSupplierDocumentImportService _dianSupplierDocumentImportService;
    private readonly IDianSupplierInvoiceAutomationService _dianSupplierInvoiceAutomationService;
    private readonly IDeduccionesIvaSharePointStorageService _deduccionesIvaSharePointStorage;
    private readonly IDeduccionesIvaImportHistoryService _deduccionesIvaHistory;
    private readonly DeduccionesIvaImportOptions _deduccionesIvaOptions;
    private readonly IContractsAiService _contractsAi;
    private readonly ISiigoService _siigo;
    private readonly ILogger<ConciliacionController> _logger;

    public ConciliacionController(
        IDataverseService dataverse,
        IFinancialReconciliationService financialReconciliation,
        ICashFlowImportService cashFlowImportService,
        IDianSupplierDocumentImportService dianSupplierDocumentImportService,
        IDianSupplierInvoiceAutomationService dianSupplierInvoiceAutomationService,
        IDeduccionesIvaSharePointStorageService deduccionesIvaSharePointStorage,
        IDeduccionesIvaImportHistoryService deduccionesIvaHistory,
        IOptions<DeduccionesIvaImportOptions> deduccionesIvaOptions,
        IContractsAiService contractsAi,
        ISiigoService siigo,
        ILogger<ConciliacionController> logger)
    {
        _dataverse = dataverse;
        _financialReconciliation = financialReconciliation;
        _cashFlowImportService = cashFlowImportService;
        _dianSupplierDocumentImportService = dianSupplierDocumentImportService;
        _dianSupplierInvoiceAutomationService = dianSupplierInvoiceAutomationService;
        _deduccionesIvaSharePointStorage = deduccionesIvaSharePointStorage;
        _deduccionesIvaHistory = deduccionesIvaHistory;
        _deduccionesIvaOptions = deduccionesIvaOptions.Value;
        _contractsAi = contractsAi;
        _siigo = siigo;
        _logger = logger;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        var (resolvedYear, resolvedMonth) = ResolvePeriod(year, month);
        var currentUserTask = _dataverse.GetCurrentUserAsync(ct);
        var boardTask = _dataverse.GetConciliacionBoardAsync(resolvedYear, resolvedMonth, ct);
        var deduccionesHistoryTask = _deduccionesIvaHistory.GetHistoryAsync(25, ct);
        var monthValidationTask = _dataverse.GetConciliacionCashFlowMonthValidationAsync(resolvedYear, resolvedMonth, ct);
        var snapshotTask = _financialReconciliation.BuildSnapshotAsync(resolvedYear, resolvedMonth, ct);

        FinancialReconciliationSnapshotResult? snapshot = null;
        string snapshotError = "";
        try
        {
            snapshot = await snapshotTask;
        }
        catch (Exception ex)
        {
            snapshotError = BuildExceptionDetail(ex);
        }

        var board = await boardTask;
        try
        {
            board.DeduccionesIvaImports = await deduccionesHistoryTask;
        }
        catch (Exception ex)
        {
            board.DeduccionesIvaHistoryError = BuildExceptionDetail(ex);
        }
        ApplyCashFlowMonthCloseStatus(board, snapshot, await monthValidationTask, snapshotError);

        var model = new ConciliacionPageViewModel
        {
            CurrentUser = await currentUserTask ?? new CurrentUserInfo(),
            Board = board
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

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> BillingDifferences([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        try
        {
            var (resolvedYear, resolvedMonth) = ResolvePeriod(year, month);
            var context = await BuildBillingDifferencesContextAsync(resolvedYear, resolvedMonth, ct);
            return Ok(context.Differences);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible consultar las diferencias de facturacion.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CreateMissingBillingInvoices(
        [FromBody] ConciliacionBillingCreateRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes seleccionar al menos una factura de Siigo para crear en Dataverse."));

        try
        {
            var (resolvedYear, resolvedMonth) = ResolvePeriod(request.Year, request.Month);
            var context = await BuildBillingDifferencesContextAsync(resolvedYear, resolvedMonth, ct);
            var requestedKeys = BuildControllerKeySet(request.InvoiceKeys);
            var allowedKeys = context.Differences.MissingInDataverse
                .Where(row => BillingDifferenceRowRequested(row, requestedKeys))
                .Select(row => FirstNonEmpty(row.SiigoInvoiceId, row.Key, row.InvoiceNumber))
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (allowedKeys.Count == 0)
                return BadRequest(CreateErrorPayload("Las facturas seleccionadas ya no aparecen como faltantes en Dataverse. Consulta la diferencia nuevamente."));

            var result = await _dataverse.CreateFinancialReconciliationMissingBillingInvoicesAsync(
                context.Start,
                context.EndExclusive,
                context.DataverseBilling,
                context.DataverseCreditNotes,
                context.Siigo,
                allowedKeys,
                ct);
            var refreshed = await BuildBillingDifferencesContextAsync(resolvedYear, resolvedMonth, ct);
            return Ok(BuildBillingDifferenceActionResult(
                $"Se aplicaron {result.Applied:N0} cambio(s) en Dataverse.",
                result,
                refreshed.Differences));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible crear las facturas faltantes en Dataverse.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> DeleteDataverseBillingInvoices(
        [FromBody] ConciliacionBillingDeleteRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes seleccionar al menos una factura de Dataverse para eliminar."));

        try
        {
            var (resolvedYear, resolvedMonth) = ResolvePeriod(request.Year, request.Month);
            var context = await BuildBillingDifferencesContextAsync(resolvedYear, resolvedMonth, ct);
            var requestedIds = BuildControllerKeySet(request.RecordIds);
            var allowedIds = context.Differences.OnlyDataverse
                .Where(row => !string.IsNullOrWhiteSpace(row.RecordId) && requestedIds.Contains(row.RecordId))
                .Select(static row => row.RecordId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (allowedIds.Count == 0)
                return BadRequest(CreateErrorPayload("Las facturas seleccionadas ya no aparecen como sobrantes en Dataverse. Consulta la diferencia nuevamente."));

            var result = await _dataverse.DeleteFinancialReconciliationBillingRowsAsync(allowedIds, ct);
            var refreshed = await BuildBillingDifferencesContextAsync(resolvedYear, resolvedMonth, ct);
            return Ok(BuildBillingDifferenceActionResult(
                $"Se aplicaron {result.Applied:N0} cambio(s) en Dataverse.",
                result,
                refreshed.Differences));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible eliminar las facturas seleccionadas de Dataverse.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ValidateCashFlowMonth(
        [FromBody] ConciliacionMonthValidationRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar el periodo a validar."));

        try
        {
            var (resolvedYear, resolvedMonth) = ResolvePeriod(request.Year, request.Month);
            var board = await _dataverse.GetConciliacionBoardAsync(resolvedYear, resolvedMonth, ct);
            var snapshot = await _financialReconciliation.BuildSnapshotAsync(resolvedYear, resolvedMonth, ct);
            ApplyCashFlowMonthCloseStatus(board, snapshot, new ConciliacionMonthValidationStateDto(), "");
            if (!board.CashFlow.CanValidateMonth)
            {
                var issues = board.CashFlow.MonthCloseIssues.Count == 0
                    ? "Aun hay diferencias pendientes en el cierre del mes."
                    : string.Join(" ", board.CashFlow.MonthCloseIssues);
                return BadRequest(CreateErrorPayload($"No se puede marcar el mes como validado. {issues}"));
            }

            return Ok(await _dataverse.MarkConciliacionCashFlowMonthValidatedAsync(
                resolvedYear,
                resolvedMonth,
                board.PeriodLabel,
                request.Comments,
                ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible validar el mes de flujo de caja.", ex));
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
    public async Task<IActionResult> MarkClientPaymentManualSiigo(
        [FromBody] ConciliacionClientPaymentStatusRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el cruce a marcar como registrado manualmente."));

        try
        {
            return Ok(await _dataverse.MarkConciliacionClientPaymentManualSiigoAsync(
                request.RecordId,
                request.Reason,
                ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible marcar el cruce como registrado manualmente en Siigo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UpdateCashFlowCategory(
        [FromBody] ConciliacionCashFlowCategoryRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CategoryValue))
            return BadRequest(CreateErrorPayload("Debes indicar la categoria del flujo de caja."));

        try
        {
            return Ok(await _dataverse.UpdateConciliacionCashFlowCategoryAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar la categoria del flujo de caja.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> ImportBancolombiaCashFlowStatement(
        [FromForm] IFormFile? file,
        [FromForm] string accountKey,
        [FromForm] int? year,
        [FromForm] int? month,
        [FromForm] bool dryRun,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(CreateErrorPayload("Selecciona el Excel exportado de Bancolombia."));

        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".xls", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(CreateErrorPayload("El extracto debe ser un archivo Excel (.xlsx, .xlsm o .xls)."));
        }

        try
        {
            var (resolvedYear, resolvedMonth) = ResolvePeriod(year, month);
            var periodStart = new DateOnly(resolvedYear, resolvedMonth, 1);
            await using var stream = file.OpenReadStream();
            return Ok(await _cashFlowImportService.ImportBancolombiaStatementAsync(
                stream,
                file.FileName,
                accountKey,
                periodStart,
                dryRun,
                ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible importar el extracto Bancolombia.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UpdateCashFlowDescription(
        [FromBody] ConciliacionCashFlowDescriptionRequest? request,
        CancellationToken ct)
    {
        if (request is null
            || (string.IsNullOrWhiteSpace(request.RecordId) && string.IsNullOrWhiteSpace(request.MovementExternalKey)))
        {
            return BadRequest(CreateErrorPayload("Debes indicar la fila del flujo de caja para guardar la descripcion."));
        }

        try
        {
            return Ok(await _dataverse.UpdateConciliacionCashFlowDescriptionAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar la descripcion del flujo de caja.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> MarkCashFlowPending(
        [FromBody] ConciliacionCashFlowPendingRequest? request,
        CancellationToken ct)
    {
        if (request is null
            || (string.IsNullOrWhiteSpace(request.RecordId) && string.IsNullOrWhiteSpace(request.MovementExternalKey)))
        {
            return BadRequest(CreateErrorPayload("Debes indicar la fila del flujo de caja que quedara pendiente."));
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(CreateErrorPayload("Escribe el motivo por el cual la conciliacion queda pendiente."));

        try
        {
            return Ok(await _dataverse.MarkConciliacionCashFlowPendingAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible dejar pendiente el movimiento del flujo de caja.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> MarkCashFlowOmitted(
        [FromBody] ConciliacionCashFlowPendingRequest? request,
        CancellationToken ct)
    {
        if (request is null
            || (string.IsNullOrWhiteSpace(request.RecordId) && string.IsNullOrWhiteSpace(request.MovementExternalKey)))
        {
            return BadRequest(CreateErrorPayload("Debes indicar la fila del flujo de caja que se omitira."));
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(CreateErrorPayload("Escribe la observacion por la cual se omite el movimiento."));

        try
        {
            return Ok(await _dataverse.MarkConciliacionCashFlowOmittedAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible omitir el movimiento del flujo de caja.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> MarkCashFlowManualSiigo(
        [FromBody] ConciliacionCashFlowManualRequest? request,
        CancellationToken ct)
    {
        if (request is null
            || (string.IsNullOrWhiteSpace(request.RecordId) && string.IsNullOrWhiteSpace(request.MovementExternalKey)))
        {
            return BadRequest(CreateErrorPayload("Debes indicar la fila del flujo de caja a marcar como subida manualmente."));
        }

        try
        {
            return Ok(await _dataverse.MarkConciliacionCashFlowManualSiigoAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible marcar el flujo de caja como subido manualmente.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UpdateCashFlowAccountingAccount(
        [FromBody] ConciliacionCashFlowAccountingAccountRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.AccountCode))
            return BadRequest(CreateErrorPayload("Debes indicar la cuenta contable del comprobante."));

        try
        {
            return Ok(await _dataverse.UpdateConciliacionCashFlowAccountingAccountAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar la cuenta contable.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SendAccountingVoucherToSiigo(
        [FromBody] ConciliacionCashFlowAccountingVoucherRequest? request,
        CancellationToken ct)
    {
        if (request is null
            || (string.IsNullOrWhiteSpace(request.RecordId)
                && string.IsNullOrWhiteSpace(request.MovementExternalKey)
                && (request.RecordIds is null || request.RecordIds.Count == 0)
                && (request.MovementExternalKeys is null || request.MovementExternalKeys.Count == 0)))
        {
            return BadRequest(CreateErrorPayload("Debes indicar el comprobante contable a enviar."));
        }

        PreparedAccountingVoucher prepared;
        try
        {
            prepared = await PrepareAccountingVoucherForSiigoAsync(request, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible preparar el comprobante contable.", ex));
        }

        if (!prepared.CanSend || prepared.Payload is null)
        {
            return Ok(new ConciliacionCashFlowActionResultDto
            {
                Message = "Envio real bloqueado. Corrige los pendientes visibles antes de enviar.",
                IsSuccess = false,
                IsReadyForSiigo = false,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                Issues = prepared.Issues,
                Row = prepared.Row
            });
        }

        try
        {
            var siigoResult = await _siigo.CreateJournalAsync(
                prepared.Payload,
                BuildSiigoIdempotencyKey($"comprobante-{FirstNonEmpty(request.GroupKey, request.RecordId, request.MovementExternalKey)}"),
                ct);
            var documentLabel = FirstNonEmpty(siigoResult.Name, siigoResult.Id);
            var message = string.IsNullOrWhiteSpace(documentLabel)
                ? $"Comprobante contable enviado a Siigo ({prepared.Rows.Count:N0} movimiento(s))."
                : $"Comprobante contable enviado a Siigo: {documentLabel} ({prepared.Rows.Count:N0} movimiento(s)).";
            var result = await _dataverse.MarkConciliacionCashFlowAccountingVoucherSiigoResultAsync(
                request,
                success: true,
                message: message,
                siigoId: siigoResult.Id,
                siigoName: siigoResult.Name,
                responseJson: siigoResult.RawJson,
                payloadJson: prepared.PayloadJson,
                ct: ct);

            result.TargetEndpoint = prepared.TargetEndpoint;
            result.PayloadJson = prepared.PayloadJson;
            result.ResponseJson = siigoResult.RawJson;
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            var detail = BuildExceptionDetail(ex);
            var result = await _dataverse.MarkConciliacionCashFlowAccountingVoucherSiigoResultAsync(
                request,
                success: false,
                message: "Siigo rechazo el comprobante contable.",
                responseJson: detail,
                payloadJson: prepared.PayloadJson,
                ct: ct);
            result.TargetEndpoint = prepared.TargetEndpoint;
            result.PayloadJson = prepared.PayloadJson;
            result.Issues = new[] { detail };
            return Ok(result);
        }
        catch (Exception ex)
        {
            var detail = BuildExceptionDetail(ex);
            var result = await _dataverse.MarkConciliacionCashFlowAccountingVoucherSiigoResultAsync(
                request,
                success: false,
                message: "No fue posible completar el envio real a Siigo.",
                responseJson: detail,
                payloadJson: prepared.PayloadJson,
                ct: ct);
            result.TargetEndpoint = prepared.TargetEndpoint;
            result.PayloadJson = prepared.PayloadJson;
            result.Issues = new[] { detail };
            return StatusCode(StatusCodes.Status500InternalServerError, result);
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
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el cruce a actualizar."));
        if (string.IsNullOrWhiteSpace(request.InvoiceRecordId)
            && (request.InvoiceRecordIds is null || request.InvoiceRecordIds.Count == 0))
            return BadRequest(CreateErrorPayload("Debes seleccionar al menos una factura para asignar."));

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
    public async Task<IActionResult> SearchSiigoSuppliersForPayment(
        [FromBody] ConciliacionSiigoSupplierSearchRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(CreateErrorPayload("Escribe el proveedor o NIT a buscar en Siigo."));

        try
        {
            var top = Math.Clamp(request.Top <= 0 ? 12 : request.Top, 1, 30);
            var suppliers = await _siigo.SearchCustomersAsync(request.Query, top, ct);
            var items = suppliers
                .Where(static supplier => supplier.Active)
                .Select(MapSupplierLookup)
                .ToArray();

            return Ok(new
            {
                message = items.Length == 0
                    ? "No encontramos terceros/proveedores con ese texto."
                    : $"Encontramos {items.Length:N0} tercero{(items.Length == 1 ? "" : "s")} en Siigo.",
                items
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible buscar proveedores en Siigo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SearchSiigoCustomersForPayment(
        [FromBody] ConciliacionSiigoSupplierSearchRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(CreateErrorPayload("Escribe el cliente o NIT a buscar en Siigo."));

        try
        {
            var top = Math.Clamp(request.Top <= 0 ? 12 : request.Top, 1, 30);
            var customers = await _siigo.SearchCustomersAsync(request.Query, top, ct);
            var items = customers
                .Where(static customer => customer.Active)
                .Select(MapSupplierLookup)
                .ToArray();

            return Ok(new
            {
                message = items.Length == 0
                    ? "No encontramos clientes con ese texto."
                    : $"Encontramos {items.Length:N0} cliente{(items.Length == 1 ? "" : "s")} en Siigo.",
                items
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible buscar clientes en Siigo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SearchClientOpenInvoicesForPayment(
        [FromBody] ConciliacionClientPaymentInvoiceSearchRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar la entrada del flujo de caja."));

        try
        {
            return Ok(await SearchClientOpenInvoicesForPaymentAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible consultar facturas de cliente en Siigo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ApplyClientInvoicePaymentToDataverse(
        [FromBody] ConciliacionClientInvoicePaymentApplyRequest? request,
        CancellationToken ct)
    {
        if (request is null
            || (request.Allocations.Count == 0 && request.Allocation is null))
        {
            return BadRequest(CreateErrorPayload("Debes seleccionar al menos una factura para aplicar."));
        }

        try
        {
            return Ok(await ApplyClientInvoicePaymentToDataverseAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible guardar las facturas seleccionadas en Dataverse.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SendClientInvoicePaymentToSiigo(
        [FromBody] ConciliacionClientInvoicePaymentSendRequest? request,
        CancellationToken ct)
    {
        if (request is null || request.Allocations.Count == 0)
            return BadRequest(CreateErrorPayload("Debes seleccionar al menos una factura para aplicar."));

        PreparedClientInvoicePayment prepared;
        try
        {
            prepared = await PrepareClientInvoicePaymentForSiigoAsync(request, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible preparar el pago de cliente.", ex));
        }

        if (!prepared.CanSend || prepared.Payload is null)
        {
            var blockingDetail = string.Join(
                " ",
                prepared.Issues
                    .Where(static issue => !string.IsNullOrWhiteSpace(issue))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            return Ok(new ConciliacionSiigoSendResultDto
            {
                Message = string.IsNullOrWhiteSpace(blockingDetail)
                    ? "Pago de cliente bloqueado. Revisa los valores aplicados."
                    : $"Pago de cliente bloqueado: {blockingDetail}",
                IsSuccess = false,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                Issues = prepared.Issues
            });
        }

        try
        {
            foreach (var invoice in prepared.Invoices)
                await SaveClientInvoicePaymentToDataverseAsync(prepared.CashFlowRow, invoice, ct);

            var persistedSnapshot = await PersistClientInvoicePaymentSnapshotAsync(
                request.MatchRecordId,
                prepared.CashFlowRow,
                prepared.Invoices,
                ct);
            request.MatchRecordId = persistedSnapshot.RecordId;
        }
        catch (Exception ex)
        {
            var detail = BuildExceptionDetail(ex);
            return Ok(new ConciliacionSiigoSendResultDto
            {
                Message = "No se envio a Siigo porque Dataverse no confirmo todas las facturas.",
                IsSuccess = false,
                DataversePaymentsSucceeded = false,
                SiigoSucceeded = false,
                DataverseReconciliationSucceeded = false,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                Issues = new[] { detail }
            });
        }

        SiigoVoucherCreateResultDto siigoResult;
        try
        {
            siigoResult = await _siigo.CreateJournalAsync(
                prepared.Payload,
                BuildSiigoIdempotencyKey($"client-invoice-{request.MatchRecordId}"),
                ct);
        }
        catch (InvalidOperationException ex)
        {
            var detail = BuildExceptionDetail(ex);
            var failureMessage = ResolveSiigoUserMessage(
                detail,
                "Siigo rechazo el comprobante de ingreso.");
            ConciliacionActionResultDto? dataverseResult = null;
            try
            {
                dataverseResult = await _dataverse.MarkConciliacionClientPaymentSiigoSendResultAsync(
                    request.MatchRecordId,
                    success: false,
                    message: failureMessage,
                    responseJson: detail,
                    ct: ct);
            }
            catch
            {
                // El rechazo principal sigue siendo el de Siigo; se informa sin ocultarlo.
            }

            return Ok(new ConciliacionSiigoSendResultDto
            {
                Message = failureMessage,
                IsSuccess = false,
                DataversePaymentsSucceeded = true,
                SiigoSucceeded = false,
                DataverseReconciliationSucceeded = false,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                Issues = new[] { detail },
                Row = dataverseResult?.Row
            });
        }
        catch (Exception ex)
        {
            var detail = BuildExceptionDetail(ex);
            ConciliacionActionResultDto? dataverseResult = null;
            try
            {
                dataverseResult = await _dataverse.MarkConciliacionClientPaymentSiigoSendResultAsync(
                    request.MatchRecordId,
                    success: false,
                    message: "No fue posible completar el pago de cliente.",
                    responseJson: detail,
                    ct: ct);
            }
            catch
            {
                // Se conserva el error original del envio.
            }

            return StatusCode(StatusCodes.Status500InternalServerError, new ConciliacionSiigoSendResultDto
            {
                Message = "No fue posible completar el pago de cliente.",
                IsSuccess = false,
                DataversePaymentsSucceeded = true,
                SiigoSucceeded = false,
                DataverseReconciliationSucceeded = false,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                Issues = new[] { detail },
                Row = dataverseResult?.Row
            });
        }

        var documentLabel = FirstNonEmpty(siigoResult.Name, siigoResult.Id);
        IReadOnlyList<string> balanceVerificationIssues;
        try
        {
            balanceVerificationIssues = await VerifyClientInvoicePaymentAppliedAsync(prepared.Invoices, ct);
        }
        catch (Exception ex)
        {
            balanceVerificationIssues = new[]
            {
                $"Siigo creo el comprobante, pero no fue posible releer los saldos de las facturas: {BuildExceptionDetail(ex)}"
            };
        }

        if (balanceVerificationIssues.Count > 0)
        {
            var verificationMessage =
                $"Siigo creo {FirstNonEmpty(documentLabel, "el comprobante")}, pero no confirmo el cruce exacto de cartera. No vuelvas a enviarlo; revisa el documento creado.";
            ConciliacionActionResultDto? dataverseResult = null;
            try
            {
                dataverseResult = await _dataverse.MarkConciliacionClientPaymentSiigoSendResultAsync(
                    request.MatchRecordId,
                    success: false,
                    message: verificationMessage,
                    siigoId: siigoResult.Id,
                    siigoName: siigoResult.Name,
                    responseJson: string.Join(" | ", balanceVerificationIssues),
                    ct: ct);
            }
            catch
            {
                // El comprobante ya existe en Siigo; se devuelve su identidad para impedir un reenvio ciego.
            }

            return Ok(new ConciliacionSiigoSendResultDto
            {
                Message = verificationMessage,
                IsSuccess = false,
                DataversePaymentsSucceeded = true,
                SiigoSucceeded = true,
                DataverseReconciliationSucceeded = false,
                SiigoId = siigoResult.Id,
                SiigoName = siigoResult.Name,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                ResponseJson = siigoResult.RawJson,
                Issues = balanceVerificationIssues,
                Row = dataverseResult?.Row
            });
        }

        var successMessage = string.IsNullOrWhiteSpace(documentLabel)
            ? "Comprobante de ingreso enviado a Siigo."
            : $"Comprobante de ingreso enviado a Siigo: {documentLabel}.";
        try
        {
            var dataverseResult = await _dataverse.MarkConciliacionClientPaymentSiigoSendResultAsync(
                request.MatchRecordId,
                success: true,
                message: successMessage,
                siigoId: siigoResult.Id,
                siigoName: siigoResult.Name,
                responseJson: siigoResult.RawJson,
                statusOverride: "Conciliado",
                ct: ct);

            return Ok(new ConciliacionSiigoSendResultDto
            {
                Message = dataverseResult.Message,
                IsSuccess = true,
                DataversePaymentsSucceeded = true,
                SiigoSucceeded = true,
                DataverseReconciliationSucceeded = true,
                SiigoId = siigoResult.Id,
                SiigoName = siigoResult.Name,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                ResponseJson = siigoResult.RawJson,
                Row = dataverseResult.Row
            });
        }
        catch (Exception ex)
        {
            var detail = BuildExceptionDetail(ex);
            return StatusCode(StatusCodes.Status500InternalServerError, new ConciliacionSiigoSendResultDto
            {
                Message = $"Siigo creo {FirstNonEmpty(documentLabel, "el comprobante")}, pero Dataverse no pudo marcar la conciliacion. No vuelvas a enviarlo.",
                IsSuccess = false,
                DataversePaymentsSucceeded = true,
                SiigoSucceeded = true,
                DataverseReconciliationSucceeded = false,
                SiigoId = siigoResult.Id,
                SiigoName = siigoResult.Name,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                ResponseJson = siigoResult.RawJson,
                Issues = new[] { detail }
            });
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SearchSupplierOpenPurchasesForPayment(
        [FromBody] ConciliacionSupplierPaymentPurchaseSearchRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar la salida del flujo de caja."));

        try
        {
            return Ok(await SearchSupplierOpenPurchasesForPaymentAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible buscar facturas de proveedor en Siigo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SendSupplierPaymentToSiigo(
        [FromBody] ConciliacionSupplierPaymentSendRequest? request,
        CancellationToken ct)
    {
        if (request is null || request.Allocations.Count != 1)
            return BadRequest(CreateErrorPayload("Aplica una sola factura de proveedor por cada envio."));

        PreparedSupplierPayment prepared;
        try
        {
            prepared = await PrepareSupplierPaymentForSiigoAsync(request, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible preparar el pago proveedor.", ex));
        }

        if (!prepared.CanSend || prepared.Payload is null)
        {
            return Ok(new ConciliacionCashFlowActionResultDto
            {
                Message = "Pago proveedor bloqueado. Corrige los pendientes visibles antes de enviar.",
                IsSuccess = false,
                DataverseChangesSucceeded = false,
                SiigoSucceeded = false,
                DataverseReconciliationSucceeded = false,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                Issues = prepared.Issues,
                Row = prepared.Row
            });
        }

        var preparedPurchase = prepared.Purchases.Single();
        var allocation = preparedPurchase.Allocation;
        var reteFuente = preparedPurchase.Retentions.FirstOrDefault(static retention =>
            string.Equals(retention.Kind, "ReteFte", StringComparison.OrdinalIgnoreCase));
        var reteIca = preparedPurchase.Retentions.FirstOrDefault(static retention =>
            string.Equals(retention.Kind, "ReteIca", StringComparison.OrdinalIgnoreCase));
        request.PurchaseId = allocation.DocumentId;
        request.PurchaseName = allocation.DocumentName;
        request.ReteFuenteValue = reteFuente?.Value ?? 0m;
        request.ReteFuenteRate = reteFuente?.Rate ?? 0m;
        request.ReteIcaValue = reteIca?.Value ?? 0m;
        request.ReteIcaRate = reteIca?.Rate ?? 0m;
        try
        {
            await _dataverse.UpdateConciliacionSupplierExpenseAllocationAsync(
                new ConciliacionSupplierExpenseAllocationRequest
                {
                    RecordId = allocation.DataverseRecordId,
                    InvoiceNumber = allocation.DataverseInvoiceNumber,
                    CufeCude = allocation.CufeCude,
                    PaymentValue = allocation.AppliedValue,
                    CloudValue = allocation.CloudValue,
                    CopiersValue = allocation.CopiersValue,
                    CategoryValue = allocation.CategoryValue
                },
                ct);
        }
        catch (Exception ex)
        {
            var detail = BuildExceptionDetail(ex);
            return Ok(new ConciliacionCashFlowActionResultDto
            {
                Message = "No se envio a Siigo porque Dataverse no confirmo Cloud, Copiers y Categoria.",
                IsSuccess = false,
                DataverseChangesSucceeded = false,
                SiigoSucceeded = false,
                DataverseReconciliationSucceeded = false,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                Issues = new[] { detail },
                Row = prepared.Row
            });
        }

        SiigoVoucherCreateResultDto siigoResult;
        try
        {
            siigoResult = await _siigo.CreateJournalAsync(
                prepared.Payload,
                BuildSiigoIdempotencyKey($"supplier-cc12-{FirstNonEmpty(request.RecordId, request.MovementExternalKey)}-{allocation.DocumentId}"),
                ct);
        }
        catch (InvalidOperationException ex)
        {
            var detail = BuildExceptionDetail(ex);
            var failureMessage = ResolveSiigoUserMessage(
                detail,
                "Siigo rechazo el comprobante de egreso del proveedor.");
            ConciliacionCashFlowActionResultDto? result = null;
            try
            {
                result = await _dataverse.MarkConciliacionSupplierPaymentSiigoResultAsync(
                    request,
                    success: false,
                    message: failureMessage,
                    responseJson: detail,
                    payloadJson: prepared.PayloadJson,
                    ct: ct);
            }
            catch
            {
                result = null;
            }

            result ??= new ConciliacionCashFlowActionResultDto();
            result.Message = failureMessage;
            result.IsSuccess = false;
            result.DataverseChangesSucceeded = true;
            result.SiigoSucceeded = false;
            result.DataverseReconciliationSucceeded = false;
            result.TargetEndpoint = prepared.TargetEndpoint;
            result.PayloadJson = prepared.PayloadJson;
            result.Issues = new[] { detail };
            return Ok(result);
        }
        catch (Exception ex)
        {
            var detail = BuildExceptionDetail(ex);
            ConciliacionCashFlowActionResultDto? result = null;
            try
            {
                result = await _dataverse.MarkConciliacionSupplierPaymentSiigoResultAsync(
                    request,
                    success: false,
                    message: "No fue posible completar el pago proveedor.",
                    responseJson: detail,
                    payloadJson: prepared.PayloadJson,
                    ct: ct);
            }
            catch
            {
                result = null;
            }

            result ??= new ConciliacionCashFlowActionResultDto();
            result.Message = "No fue posible completar el pago proveedor.";
            result.IsSuccess = false;
            result.DataverseChangesSucceeded = true;
            result.SiigoSucceeded = false;
            result.DataverseReconciliationSucceeded = false;
            result.TargetEndpoint = prepared.TargetEndpoint;
            result.PayloadJson = prepared.PayloadJson;
            result.Issues = new[] { detail };
            return StatusCode(StatusCodes.Status500InternalServerError, result);
        }

        var paymentLabel = FirstNonEmpty(siigoResult.Name, siigoResult.Id);
        var successMessage = string.IsNullOrWhiteSpace(paymentLabel)
            ? "Comprobante de egreso de proveedor enviado a Siigo."
            : $"Comprobante de egreso de proveedor enviado a Siigo: {paymentLabel}.";
        try
        {
            var result = await _dataverse.MarkConciliacionSupplierPaymentSiigoResultAsync(
                request,
                success: true,
                message: successMessage,
                siigoId: siigoResult.Id,
                siigoName: siigoResult.Name,
                responseJson: siigoResult.RawJson,
                payloadJson: prepared.PayloadJson,
                statusOverride: "Conciliado",
                ct: ct);
            result.IsSuccess = true;
            result.DataverseChangesSucceeded = true;
            result.SiigoSucceeded = true;
            result.DataverseReconciliationSucceeded = true;
            result.ResponseJson = siigoResult.RawJson;
            return Ok(result);
        }
        catch (Exception ex)
        {
            var detail = BuildExceptionDetail(ex);
            return StatusCode(StatusCodes.Status500InternalServerError, new ConciliacionCashFlowActionResultDto
            {
                Message = $"Siigo creo {FirstNonEmpty(paymentLabel, "el pago")}, pero Dataverse no pudo marcar la conciliacion. No vuelvas a enviarlo.",
                IsSuccess = false,
                DataverseChangesSucceeded = true,
                SiigoSucceeded = true,
                DataverseReconciliationSucceeded = false,
                SiigoId = siigoResult.Id,
                SiigoName = siigoResult.Name,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                ResponseJson = siigoResult.RawJson,
                Issues = new[] { detail }
            });
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> MarkSupplierPaymentManualSiigo(
        [FromBody] ConciliacionSupplierPaymentSendRequest? request,
        CancellationToken ct)
    {
        if (request is null || (string.IsNullOrWhiteSpace(request.RecordId) && string.IsNullOrWhiteSpace(request.MovementExternalKey)))
            return BadRequest(CreateErrorPayload("Debes indicar la salida del flujo de caja a marcar como conciliada."));

        try
        {
            var result = await _dataverse.MarkConciliacionSupplierPaymentSiigoResultAsync(
                request,
                success: true,
                message: "Pago proveedor marcado como subido manualmente y conciliado desde Conciliacion.",
                siigoName: "Subida manualmente en Siigo",
                responseJson: "",
                payloadJson: "",
                statusOverride: "Conciliado",
                targetEndpoint: "MANUAL Siigo",
                messagePrefix: "Pago proveedor subido manualmente",
                ct: ct);

            result.IsReadyForSiigo = false;
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible marcar la salida como subida manualmente.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ImportDeduccionesIva(
        IFormFile? file,
        [FromForm] int? year,
        [FromForm] int? month,
        [FromQuery] bool dryRun = false,
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest(CreateErrorPayload("Adjunta el ZIP o Excel DIAN de deducciones IVA."));
        var extension = Path.GetExtension(file.FileName);
        if (!DeduccionesIvaFileExtensionAllowed(extension))
            return BadRequest(CreateErrorPayload("El archivo debe ser un ZIP .zip o Excel .xlsx/.xlsm exportado desde DIAN."));

        var maxFileBytes = _deduccionesIvaOptions.MaxFileBytes <= 0
            ? 50L * 1024L * 1024L
            : _deduccionesIvaOptions.MaxFileBytes;
        if (file.Length > maxFileBytes)
            return BadRequest(CreateErrorPayload($"El archivo supera el tamano maximo configurado ({maxFileBytes / 1024L / 1024L:N0} MB)."));

        try
        {
            var selectedPeriodIsValid = year is >= 2020 and <= 2100 && month is >= 1 and <= 12;
            var (resolvedYear, resolvedMonth) = ResolvePeriod(
                selectedPeriodIsValid ? year : null,
                selectedPeriodIsValid ? month : null);
            var periodStart = new DateOnly(resolvedYear, resolvedMonth, 1);
            await using var memory = new MemoryStream();
            await file.CopyToAsync(memory, ct);
            memory.Position = 0;

            var upload = await _deduccionesIvaSharePointStorage.UploadAsync(
                file.FileName,
                file.ContentType,
                memory,
                ct);
            memory.Position = 0;

            var import = await _dianSupplierDocumentImportService.ImportAsync(
                memory,
                upload.StoredFileName,
                dryRun,
                ct);
            var historyWarning = "";
            try
            {
                await _deduccionesIvaHistory.RecordAsync(
                    file.FileName,
                    upload,
                    periodStart,
                    FirstNonEmpty(User.Identity?.Name, "Usuario autenticado"),
                    import,
                    ct);
            }
            catch (Exception historyException)
            {
                historyWarning = " La importacion termino, pero no fue posible registrar su historico.";
                _logger.LogWarning(
                    historyException,
                    "No se pudo guardar el historico de la importacion DIAN {StoredFileName}.",
                    upload.StoredFileName);
            }
            var changed = import.Created + import.Updated;
            var message = import.DryRun
                ? $"Simulacion de deducciones IVA finalizada para {import.PeriodLabel}: {import.ImportableRows:N0} fila(s) importables."
                : $"Deducciones IVA importadas para {import.PeriodLabel}: {import.Created:N0} nueva(s), {import.Updated:N0} actualizada(s), {import.Unchanged:N0} sin cambios. Nominas guardadas solo en Dataverse: {import.PayrollRows:N0}. {import.SiigoAutomation?.Message}".Trim();
            message += historyWarning;

            return Ok(new
            {
                message,
                changed,
                historyRecorded = string.IsNullOrWhiteSpace(historyWarning),
                period = import.PeriodLabel,
                periods = import.Periods,
                selectedPeriod = periodStart.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                sharePoint = new
                {
                    upload.Uploaded,
                    upload.StoredFileName,
                    upload.FolderPath,
                    upload.WebUrl
                },
                import
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible importar deducciones IVA.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [RequestSizeLimit(DianSupplierRutAnalysisLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = DianSupplierRutAnalysisLimit)]
    public async Task<IActionResult> AnalyzeDianSupplierRut(
        IFormFile? file,
        [FromForm] string? recordId,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(CreateErrorPayload("Adjunta el RUT del proveedor."));
        if (file.Length > DianSupplierRutAnalysisLimit)
            return BadRequest(CreateErrorPayload("El RUT supera el tamano maximo permitido de 25 MB."));
        if (string.IsNullOrWhiteSpace(recordId))
            return BadRequest(CreateErrorPayload("No se encontro la factura DIAN asociada al proveedor."));

        try
        {
            var row = await _dataverse.GetConciliacionDianSupplierDocumentAsync(recordId, ct);
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            var rut = await _contractsAi.AnalyzeRutAsync(
                file.FileName,
                file.ContentType,
                stream.ToArray(),
                ct);
            var supplierName = FirstNonEmpty(rut.LegalName, row.SupplierName).Trim();
            var personType = ResolveSiigoSupplierPersonType(rut.LegalForm, supplierName);
            if (!IsAllowedDianSupplierIdentificationEdit(
                    row.SupplierNit,
                    rut.Nit,
                    personType,
                    rut.VerificationDigit))
            {
                return BadRequest(CreateErrorPayload(
                    $"El RUT analizado corresponde al NIT {FirstNonEmpty(rut.Nit, "sin identificar")}, "
                    + $"pero la factura DIAN pertenece al NIT {row.SupplierNit}. Adjunta el RUT correcto."));
            }

            var city = ResolveDianRutSiigoCity(rut.City, rut.Department);
            var fiscalResponsibility = ResolveDianRutFiscalResponsibility(rut.TaxResponsibilities);
            return Ok(new
            {
                message = city.Matched
                    ? "RUT analizado. Revisa los datos extraidos y crea el proveedor."
                    : "RUT analizado. Revisa los datos y selecciona manualmente la ciudad Siigo.",
                supplierName,
                supplierNit = FirstNonEmpty(rut.Nit, row.SupplierNit),
                personType,
                idType = ResolveSiigoSupplierIdType("", personType.Equals("Company", StringComparison.OrdinalIgnoreCase)),
                checkDigit = rut.VerificationDigit,
                vatResponsible = IsDianRutVatResponsible(rut.TaxResponsibilities),
                fiscalResponsibilityCode = fiscalResponsibility,
                address = FirstNonEmpty(rut.NotificationAddress, rut.MainAddress),
                rut.City,
                rut.Department,
                countryCode = city.CountryCode,
                stateCode = city.StateCode,
                cityCode = city.CityCode,
                cityLabel = city.Label,
                cityMappingFound = city.Matched,
                rut.Email,
                rut.Phone,
                rut.Confidence,
                rut.TaxResponsibilities,
                rut.SourceNotes
            });
        }
        catch (TimeoutException ex)
        {
            return StatusCode(
                StatusCodes.Status504GatewayTimeout,
                CreateErrorPayload("El analisis del RUT tardo demasiado. Intenta nuevamente.", ex));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible analizar el RUT con IA.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> RetryDianSupplierPurchases(
        [FromBody] ConciliacionDianPeriodRequest? request,
        CancellationToken ct)
    {
        try
        {
            var periods = (request?.Periods ?? Array.Empty<string>())
                .Select(static value => DateOnly.TryParseExact(
                    $"{(value ?? "").Trim()}-01",
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var period)
                    ? (DateOnly?)period
                    : null)
                .Where(static period => period.HasValue && period.Value.Year is >= 2020 and <= 2100)
                .Select(static period => period!.Value)
                .Distinct()
                .OrderBy(static period => period)
                .ToArray();
            if (periods.Length == 0)
            {
                var (year, month) = ResolvePeriod(request?.Year, request?.Month);
                periods = [new DateOnly(year, month, 1)];
            }

            var externalKeys = (request?.ExternalKeys ?? Array.Empty<string>())
                .Where(static key => !string.IsNullOrWhiteSpace(key))
                .Select(static key => key.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var supplierLookups = new List<DianSupplierDocumentSupplierLookupRunResultDto>(periods.Length);
            var classifications = new List<ExpenseAccountingRuleApplyResultDto>(periods.Length);
            var automations = new List<DianSupplierInvoiceAutomationResultDto>(periods.Length);
            foreach (var periodStart in periods)
            {
                var periodEnd = periodStart.AddMonths(1).AddDays(-1);
                supplierLookups.Add(await _dianSupplierDocumentImportService.ResolvePendingSuppliersAsync(
                    periodStart,
                    periodEnd,
                    dryRun: false,
                    ct));
                classifications.Add(await _dataverse.ApplyExpenseAccountingRulesAsync(
                    periodStart,
                    periodEnd,
                    movementType: "Compra",
                    overwrite: false,
                    ct));
                automations.Add(await _dianSupplierInvoiceAutomationService.ProcessPeriodAsync(
                    periodStart,
                    dryRun: false,
                    externalKeys: externalKeys,
                    ct: ct));
            }

            var automation = DianSupplierDocumentImportService.AggregateInvoiceAutomationResults(automations);
            var supplierLookup = new DianSupplierDocumentSupplierLookupRunResultDto
            {
                StartDate = periods[0],
                EndDate = periods[^1].AddMonths(1).AddDays(-1),
                PendingRowsReviewed = supplierLookups.Sum(static item => item.PendingRowsReviewed),
                SupplierLookupReviewed = supplierLookups.Sum(static item => item.SupplierLookupReviewed),
                SupplierLookupFound = supplierLookups.Sum(static item => item.SupplierLookupFound),
                SupplierLookupMissing = supplierLookups.Sum(static item => item.SupplierLookupMissing),
                SupplierLookupFailed = supplierLookups.Sum(static item => item.SupplierLookupFailed),
                SupplierLookupRowsUpdated = supplierLookups.Sum(static item => item.SupplierLookupRowsUpdated),
                AutoClassificationReviewed = supplierLookups.Sum(static item => item.AutoClassificationReviewed),
                AutoClassificationUpdated = supplierLookups.Sum(static item => item.AutoClassificationUpdated),
                AutoClassificationAlreadyAssigned = supplierLookups.Sum(static item => item.AutoClassificationAlreadyAssigned),
                AutoClassificationNoRule = supplierLookups.Sum(static item => item.AutoClassificationNoRule),
                AutoClassificationInvalidRule = supplierLookups.Sum(static item => item.AutoClassificationInvalidRule),
                AutoClassificationMessage = string.Join(" ", supplierLookups
                    .Select(static item => item.AutoClassificationMessage)
                    .Where(static message => !string.IsNullOrWhiteSpace(message)))
            };
            var classification = new ExpenseAccountingRuleApplyResultDto
            {
                StartDate = periods[0],
                EndDate = periods[^1].AddMonths(1).AddDays(-1),
                MovementType = "Compra",
                Reviewed = classifications.Sum(static item => item.Reviewed),
                Updated = classifications.Sum(static item => item.Updated),
                AlreadyAssigned = classifications.Sum(static item => item.AlreadyAssigned),
                NoRule = classifications.Sum(static item => item.NoRule),
                InvalidRule = classifications.Sum(static item => item.InvalidRule),
                Rows = classifications.SelectMany(static item => item.Rows).ToArray()
            };

            return Ok(new
            {
                message = automation.Message,
                supplierLookup,
                classification,
                automation
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible reintentar las facturas de compra DIAN en Siigo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ResolveDianSupplierDocumentsSuppliers(
        [FromQuery] int? year,
        [FromQuery] int? month,
        CancellationToken ct)
    {
        try
        {
            var (resolvedYear, resolvedMonth) = ResolvePeriod(year, month);
            var startDate = new DateOnly(resolvedYear, resolvedMonth, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);
            var result = await _dianSupplierDocumentImportService.ResolvePendingSuppliersAsync(
                startDate,
                endDate,
                dryRun: false,
                ct);
            var automation = await _dianSupplierInvoiceAutomationService.ProcessPeriodAsync(
                startDate,
                dryRun: false,
                ct: ct);

            var message = result.PendingRowsReviewed == 0
                ? $"No habia proveedores pendientes por validar contra Siigo en este periodo. {automation.Message}"
                : $"Validacion de proveedores finalizada. Encontrados {result.SupplierLookupFound:N0}; actualizados {result.SupplierLookupRowsUpdated:N0}; faltantes reales {result.SupplierLookupMissing:N0}. {automation.Message}";

            return Ok(new
            {
                message,
                result.DryRun,
                startDate = result.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                endDate = result.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                result.PendingRowsReviewed,
                result.SupplierLookupReviewed,
                result.SupplierLookupFound,
                result.SupplierLookupMissing,
                result.SupplierLookupFailed,
                result.SupplierLookupRowsUpdated,
                result.AutoClassificationReviewed,
                result.AutoClassificationUpdated,
                result.AutoClassificationAlreadyAssigned,
                result.AutoClassificationNoRule,
                result.AutoClassificationInvalidRule,
                result.AutoClassificationMessage,
                automation
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible validar proveedores DIAN contra Siigo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UpdateDianSupplierDocumentClassification(
        [FromBody] ConciliacionDianClassificationRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el documento DIAN a actualizar."));

        try
        {
            return Ok(await _dataverse.UpdateConciliacionDianSupplierDocumentClassificationAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar la cuenta gasto DIAN.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CreateDianSupplierInSiigo(
        [FromBody] ConciliacionDianSupplierDocumentRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el documento DIAN."));
        if (string.IsNullOrWhiteSpace(request.SupplierName)
            || ExtractDigits(request.SupplierNit).Length < 5
            || string.IsNullOrWhiteSpace(request.PersonType)
            || string.IsNullOrWhiteSpace(request.IdType)
            || request.VatResponsible is null
            || string.IsNullOrWhiteSpace(request.FiscalResponsibilityCode)
            || string.IsNullOrWhiteSpace(request.Address)
            || request.Address.Trim().Equals("Sin direccion", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(request.CountryCode)
            || string.IsNullOrWhiteSpace(request.StateCode)
            || string.IsNullOrWhiteSpace(request.CityCode))
        {
            return BadRequest(CreateErrorPayload("Completa y revisa todos los datos fiscales y de ubicacion del proveedor antes de crearlo en Siigo."));
        }

        ConciliacionDianSupplierInvoiceRowDto? row = null;
        ConciliacionDianSupplierInvoiceRowDto? supplierClaimRow = null;
        var supplierCreationClaimed = false;
        var supplierAssociated = false;
        var supplierPostSucceeded = false;
        try
        {
            row = await _dataverse.GetConciliacionDianSupplierDocumentAsync(request.RecordId, ct);
            var requestedPersonType = ResolveSiigoSupplierPersonType(request.PersonType, request.SupplierName);
            var isCompany = requestedPersonType.Equals("Company", StringComparison.OrdinalIgnoreCase);
            if (!IsAllowedDianSupplierIdentificationEdit(
                    row.SupplierNit,
                    request.SupplierNit,
                    requestedPersonType,
                    request.CheckDigit))
            {
                throw new InvalidOperationException(
                    "El NIT no puede cambiarse por uno distinto al informado por DIAN. "
                    + "Solo se admite ajustar formato o digito de verificacion; corrige la fuente antes de crear el proveedor.");
            }

            var (year, month) = ResolvePeriod(request.Year, request.Month);
            var supplierTaxId = ResolveSupplierTaxId(row.SupplierNit, isCompany);
            supplierClaimRow = row;
            var supplier = await EnsureDianSupplierInSiigoAsync(row, allowCreate: false, ct, request);
            if (!supplier.ExistsInSiigo)
            {
                // El representante es global por NIT, no mensual. Todas las instancias y
                // periodos compiten por el mismo ETag antes de ejecutar POST /customers.
                var allDianRows = await _dataverse.GetConciliacionDianSupplierDocumentsForAutomationAsync(
                    new DateOnly(2000, 1, 1),
                    new DateOnly(2100, 1, 1),
                    ct);
                supplierClaimRow = allDianRows
                    .Where(candidate => IsSameSupplierIdentification(candidate.SupplierNit, supplierTaxId, isCompany))
                    .Where(static candidate => !string.IsNullOrWhiteSpace(candidate.SiigoBusinessKey))
                    .OrderBy(static candidate => candidate.CreatedAt ?? DateTimeOffset.MaxValue)
                    .ThenBy(static candidate => candidate.RecordId, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException(
                        "No hay una factura durable del proveedor con SiigoBusinessKey. Ejecuta el aprovisionamiento/backfill y reimporta el Excel DIAN antes de crear el proveedor.");

                if (!IsDianSupplierCreationWriteHold(supplierClaimRow))
                {
                    supplierCreationClaimed = await _dataverse.TryClaimConciliacionDianSupplierCreationAsync(
                        supplierClaimRow.RecordId,
                        supplierClaimRow.ConcurrencyToken,
                        ct);
                    if (!supplierCreationClaimed)
                    {
                        throw new InvalidOperationException(
                            "El proveedor o una factura del mismo NIT cambiaron mientras iniciaba el proceso; recarga la bandeja antes de reintentar.");
                    }
                }

                supplier = await EnsureDianSupplierInSiigoAsync(supplierClaimRow, allowCreate: true, ct, request);
                supplierPostSucceeded = supplier.Created;
            }
            var supplierLabel = FirstNonEmpty(supplier.Customer.DisplayName, supplier.Customer.Name, supplier.Customer.Identification);
            var message = supplier.Created
                ? $"Proveedor creado en Siigo: {supplierLabel}."
                : $"Proveedor encontrado en Siigo y asociado: {supplierLabel}.";

            var claimAssociation = await _dataverse.MarkConciliacionDianSupplierAsync(
                supplierClaimRow.RecordId,
                supplier.Customer.Id,
                supplierLabel,
                message,
                ct);
            var association = string.Equals(supplierClaimRow.RecordId, request.RecordId, StringComparison.OrdinalIgnoreCase)
                ? claimAssociation
                : await _dataverse.MarkConciliacionDianSupplierAsync(
                    request.RecordId,
                    supplier.Customer.Id,
                    supplierLabel,
                    message,
                    ct);
            supplierAssociated = true;
            var automation = await _dianSupplierInvoiceAutomationService.ProcessPeriodAsync(
                new DateOnly(year, month, 1),
                dryRun: false,
                ct: ct);
            var processed = automation.Created
                + automation.ExistingLinked
                + automation.AlreadyImported
                + automation.PurchasesRecoveredAfterAmbiguousError;
            var finalMessage = automation.Completed
                ? $"{message} Las {processed:N0} factura(s) elegibles del periodo quedaron importadas en Siigo."
                : $"{message} Se procesaron {processed:N0} factura(s); quedan {automation.PendingSupplierInvoices:N0} sin proveedor, {automation.PendingClassification:N0} sin cuenta, {automation.AmbiguousWritePending:N0} esperando confirmacion segura de Siigo y {automation.Failed:N0} con error.";

            return Ok(new
            {
                message = finalMessage,
                isSuccess = true,
                siigoId = supplier.Customer.Id,
                siigoName = supplierLabel,
                row = association.Row,
                automation
            });
        }
        catch (SiigoSupplierCreateException ex)
        {
            var detail = BuildExceptionDetail(ex.InnerException ?? ex);
            var ambiguous = IsAmbiguousSupplierCreateFailure(ex)
                || ex.Message.Contains("no confirmo", StringComparison.OrdinalIgnoreCase);
            if (supplierClaimRow is not null && supplierCreationClaimed && !supplierAssociated)
            {
                try
                {
                    await _dataverse.MarkConciliacionDianSupplierDocumentSiigoResultAsync(
                        supplierClaimRow.RecordId,
                        success: false,
                        message: ambiguous
                            ? $"[SIIGO_SUPPLIER_WRITE_AMBIGUOUS] Siigo no confirmo la creacion del proveedor. No se repetira el POST hasta que el tercero aparezca en la consulta. {detail}"
                            : $"Siigo rechazo la creacion del proveedor. {detail}",
                        ownsProcessingClaim: true,
                        releaseProcessingClaim: !ambiguous,
                        ct: ct);
                }
                catch
                {
                    // La reserva atomica queda activa y evita un segundo POST si falla esta persistencia.
                }
            }

            return Ok(new ConciliacionDianActionResultDto
            {
                Message = "Siigo rechazo la creacion del proveedor.",
                IsSuccess = false,
                TargetEndpoint = "/v1/customers",
                PayloadJson = ex.PayloadJson,
                ResponseJson = detail,
                Issues = new[] { detail },
                Row = row
            });
        }
        catch (InvalidOperationException ex)
        {
            if (supplierClaimRow is not null && supplierCreationClaimed && !supplierAssociated)
            {
                try
                {
                    await _dataverse.MarkConciliacionDianSupplierDocumentSiigoResultAsync(
                        supplierClaimRow.RecordId,
                        success: false,
                        message: supplierPostSucceeded
                            ? $"[SIIGO_SUPPLIER_WRITE_AMBIGUOUS] Siigo creo o confirmo el proveedor, pero no fue posible guardar toda la asociacion en Dataverse. No se repetira el POST. {ex.Message}"
                            : ex.Message,
                        ownsProcessingClaim: true,
                        releaseProcessingClaim: !supplierPostSucceeded,
                        ct: ct);
                }
                catch
                {
                    // Si Dataverse no responde, la reserva permanece y el reintento sigue bloqueado.
                }
            }

            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            if (supplierClaimRow is not null && supplierCreationClaimed && !supplierAssociated)
            {
                try
                {
                    await _dataverse.MarkConciliacionDianSupplierDocumentSiigoResultAsync(
                        supplierClaimRow.RecordId,
                        success: false,
                        message: $"[SIIGO_SUPPLIER_WRITE_AMBIGUOUS] Resultado incierto creando/asociando el proveedor. No se repetira el POST automaticamente. {BuildExceptionDetail(ex)}",
                        ownsProcessingClaim: true,
                        ct: ct);
                }
                catch
                {
                    // La reserva atomica es el ultimo candado si no se puede guardar el detalle.
                }
            }

            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible crear/asociar el proveedor en Siigo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SimulateDianSupplierPurchaseSiigoSend(
        [FromBody] ConciliacionDianSupplierDocumentRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el documento DIAN a simular."));

        try
        {
            var prepared = await PrepareDianSupplierPurchaseForSiigoAsync(request.RecordId, createMissingSupplier: false, ct);
            return Ok(new ConciliacionDianActionResultDto
            {
                Message = prepared.CanSend
                    ? "Simulacion correcta. El payload de factura esta completo y no se envio nada a Siigo."
                    : "Simulacion con pendientes. Corrige los puntos indicados antes del envio real.",
                IsReadyForSiigo = prepared.CanSend,
                TargetEndpoint = $"DRY-RUN {prepared.TargetEndpoint}",
                PayloadJson = prepared.PayloadJson,
                Issues = prepared.Issues,
                Row = prepared.Row
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible simular la factura de compra Siigo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SendDianSupplierPurchaseToSiigo(
        [FromBody] ConciliacionDianSupplierDocumentRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el documento DIAN a enviar."));

        try
        {
            var row = await _dataverse.GetConciliacionDianSupplierDocumentAsync(request.RecordId, ct);
            if (!DateOnly.TryParseExact(
                    row.ReceptionDateValue,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var receptionDate))
            {
                return BadRequest(CreateErrorPayload("La factura DIAN no tiene una fecha de recepcion valida."));
            }

            var automation = await _dianSupplierInvoiceAutomationService.ProcessPeriodAsync(
                new DateOnly(receptionDate.Year, receptionDate.Month, 1),
                dryRun: false,
                supplierKey: row.SupplierNit,
                ct: ct);
            var document = automation.Rows.FirstOrDefault(item =>
                string.Equals(item.RecordId, request.RecordId, StringComparison.OrdinalIgnoreCase));
            var successful = document is not null
                && (document.Status.Equals("AlreadyImported", StringComparison.OrdinalIgnoreCase)
                    || document.Status.Equals("ExistingLinked", StringComparison.OrdinalIgnoreCase)
                    || document.Status.Equals("Created", StringComparison.OrdinalIgnoreCase)
                    || document.Status.Equals("RecoveredAfterAmbiguousError", StringComparison.OrdinalIgnoreCase));

            return Ok(new ConciliacionDianActionResultDto
            {
                Message = document?.Message ?? automation.Message,
                IsSuccess = successful,
                IsReadyForSiigo = false,
                TargetEndpoint = "/v1/purchases",
                ResponseJson = System.Text.Json.JsonSerializer.Serialize(automation),
                SiigoId = document?.SiigoId ?? "",
                SiigoName = document?.SiigoName ?? "",
                Issues = document?.Issues ?? (successful ? Array.Empty<string>() : new[] { automation.Message }),
                Row = await _dataverse.GetConciliacionDianSupplierDocumentAsync(request.RecordId, ct)
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible procesar la factura de compra Siigo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> OpenCuentaCobroExpenseEditor(
        [FromBody] ConciliacionCuentaCobroDocumentRequest? request,
        CancellationToken ct)
    {
        if (request is null
            || (string.IsNullOrWhiteSpace(request.RecordId)
                && string.IsNullOrWhiteSpace(request.CashFlowRecordId)
                && string.IsNullOrWhiteSpace(request.CashFlowExternalKey)))
        {
            return BadRequest(CreateErrorPayload("Debes indicar la salida bancaria de la cuenta de cobro."));
        }

        try
        {
            var rowTask = _dataverse.GetConciliacionCuentaCobroDocumentAsync(request, ct);
            var taxesTask = _siigo.GetTaxesAsync(ct);
            await Task.WhenAll(rowTask, taxesTask);
            var taxes = taxesTask.Result;

            return Ok(new ConciliacionCuentaCobroEditorDto
            {
                Row = rowTask.Result,
                ReteFuenteOptions = BuildCuentaCobroRetentionOptions(taxes, "ReteFuente"),
                ReteIcaOptions = BuildCuentaCobroRetentionOptions(taxes, "ReteICA"),
                RteIvaOptions = BuildCuentaCobroRetentionOptions(taxes, "RteIVA")
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible abrir el formulario del documento soporte.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SaveCuentaCobroExpense(
        [FromBody] ConciliacionCuentaCobroExpenseSaveRequest? request,
        CancellationToken ct)
    {
        if (request is null
            || (string.IsNullOrWhiteSpace(request.CashFlowRecordId)
                && string.IsNullOrWhiteSpace(request.CashFlowExternalKey)))
        {
            return BadRequest(CreateErrorPayload("Debes indicar la salida bancaria que origina la cuenta de cobro."));
        }

        try
        {
            var taxes = await _siigo.GetTaxesAsync(ct);
            var issues = new List<string>();
            request.Retentions = ResolveCuentaCobroExpenseRetentions(request, taxes, issues);
            var expectedPayment = RoundCurrency(
                request.ValorTotal - request.Retentions.Sum(static retention => retention.Value));

            if (request.ValorTotal <= 0m)
                issues.Add("El valor total de la cuenta de cobro debe ser mayor a cero.");
            if (request.ValorIva < 0m || request.ValorIva > request.ValorTotal)
                issues.Add("El valor IVA debe estar entre cero y el valor total de la cuenta de cobro.");
            if (expectedPayment <= 0m)
                issues.Add("Las retenciones no pueden ser iguales o superiores al valor total.");
            if (request.ValorPago > 0m && Math.Abs(request.ValorPago - expectedPayment) > 0.01m)
                issues.Add("El pago recibido no coincide con total menos retenciones.");
            request.ValorPago = expectedPayment;

            if (issues.Count > 0)
            {
                return BadRequest(new ConciliacionCuentaCobroActionResultDto
                {
                    Message = "No se guardo el gasto porque los valores o las retenciones no son validos.",
                    IsSuccess = false,
                    Issues = issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                });
            }

            return Ok(await _dataverse.SaveConciliacionCuentaCobroExpenseAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible registrar la cuenta de cobro en gastos de la empresa.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UpdateCuentaCobroClassification(
        [FromBody] ConciliacionCuentaCobroClassificationRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar la cuenta de cobro a actualizar."));

        try
        {
            return Ok(await _dataverse.UpdateConciliacionCuentaCobroClassificationAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar la cuenta contable.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> MarkCuentaCobroManualSiigo(
        [FromBody] ConciliacionCuentaCobroDocumentRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar la cuenta de cobro a marcar como conciliada."));

        try
        {
            var result = await _dataverse.MarkConciliacionCuentaCobroSiigoResultAsync(
                request,
                success: true,
                message: "Cuenta de cobro marcada como subida manualmente y conciliada desde Conciliacion.",
                siigoId: "",
                siigoName: "",
                siigoPaymentId: "",
                siigoPaymentName: "Subida manualmente en Siigo",
                responseJson: "",
                payloadJson: "",
                stateOverride: "Conciliado",
                targetEndpoint: "MANUAL Siigo",
                ct: ct);

            result.IsReadyForSiigo = false;
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible marcar la cuenta de cobro como subida manualmente.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ValidateCuentaCobroSupportDocumentPreflight(
        [FromBody] ConciliacionCuentaCobroDocumentRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar la cuenta de cobro a validar."));

        try
        {
            var supportPrepared = await PrepareCuentaCobroSupportDocumentForSiigoAsync(request, createMissingSupplier: false, ct);
            var paymentPrepared = await PrepareCuentaCobroPaymentReceiptForSiigoAsync(
                request,
                supportDocumentId: "",
                supportDocumentName: "PREVALIDACION-1",
                ct);
            var issues = supportPrepared.Issues
                .Concat(paymentPrepared.Issues)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var canSend = supportPrepared.CanSend && paymentPrepared.CanSend && issues.Length == 0;
            var payloadJson = CombinePayloads(supportPrepared.PayloadJson, paymentPrepared.PayloadJson);
            var message = canSend
                ? "Prevalidacion correcta. Documento soporte, retenciones, cuenta bancaria y pago quedan listos para Siigo."
                : "Prevalidacion bloqueada. Corrige los pendientes visibles antes del envio real.";
            var result = await _dataverse.MarkConciliacionCuentaCobroPreflightAsync(
                request,
                canSend,
                message,
                issues,
                payloadJson,
                ct);

            result.TargetEndpoint = "DRY-RUN /v1/purchase-support-documents + /v1/journals";
            result.PayloadJson = payloadJson;
            result.Issues = issues;
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible validar el documento soporte.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SendCuentaCobroSupportDocumentToSiigo(
        [FromBody] ConciliacionCuentaCobroDocumentRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar la cuenta de cobro a enviar."));

        ConciliacionCuentaCobroRowDto currentRow;
        try
        {
            currentRow = await _dataverse.GetConciliacionCuentaCobroDocumentAsync(request, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible consultar la cuenta de cobro.", ex));
        }

        var currentHasDocument = !string.IsNullOrWhiteSpace(currentRow.SiigoDocumentId)
            || !string.IsNullOrWhiteSpace(currentRow.SiigoDocumentName);
        var currentHasPayment = !string.IsNullOrWhiteSpace(currentRow.SiigoPaymentId)
            || !string.IsNullOrWhiteSpace(currentRow.SiigoPaymentName);
        if (currentHasDocument && !currentHasPayment)
        {
            return await SendCuentaCobroSupportPaymentToSiigoCoreAsync(request, ct);
        }
        if (currentHasDocument && currentHasPayment)
        {
            return Ok(new ConciliacionCuentaCobroActionResultDto
            {
                Message = "La cuenta de cobro ya tiene documento soporte y pago confirmados en Siigo.",
                IsSuccess = true,
                IsReadyForSiigo = true,
                TargetEndpoint = "/v1/purchase-support-documents + /v1/journals",
                SiigoId = FirstNonEmpty(currentRow.SiigoPaymentId, currentRow.SiigoDocumentId),
                SiigoName = FirstNonEmpty(currentRow.SiigoPaymentName, currentRow.SiigoDocumentName),
                Row = currentRow
            });
        }
        if (string.Equals(currentRow.RecordSource, "cuenta-cobro", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(CreateErrorPayload(
                "Los registros historicos no pueden crear un documento soporte real. "
                + "Primero registra la salida como gasto canonico en cr07a_gastodelaempresa."));
        }

        var currentNeedsVerification = currentRow.AutomationState.Equals(
                CuentaCobroSupportDocumentVerificationState,
                StringComparison.OrdinalIgnoreCase)
            || currentRow.ReviewReason.Contains(
                CuentaCobroSupportDocumentAmbiguousMarker,
                StringComparison.OrdinalIgnoreCase);
        var currentIsProcessing = currentRow.AutomationState.Equals(
            CuentaCobroSupportDocumentProcessingState,
            StringComparison.OrdinalIgnoreCase);
        if (currentNeedsVerification || currentIsProcessing)
        {
            var holdMessage = currentNeedsVerification
                ? "No se repetira el envio: verifica manualmente en Siigo si el documento soporte fue creado."
                : "La cuenta de cobro ya esta reservada por otra ejecucion. No se repetira el documento soporte.";
            return Conflict(new ConciliacionCuentaCobroActionResultDto
            {
                Message = holdMessage,
                IsSuccess = false,
                IsReadyForSiigo = false,
                TargetEndpoint = "/v1/purchase-support-documents",
                Issues = new[] { holdMessage },
                Row = currentRow
            });
        }

        PreparedCuentaCobroSupportDocument prepared;
        try
        {
            prepared = await PrepareCuentaCobroSupportDocumentForSiigoAsync(request, createMissingSupplier: false, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible preparar el documento soporte Siigo.", ex));
        }

        if (!prepared.CanSend || prepared.Payload is null)
        {
            return Ok(new ConciliacionCuentaCobroActionResultDto
            {
                Message = "Envio real bloqueado. Corrige los pendientes visibles antes de enviar.",
                IsSuccess = false,
                IsReadyForSiigo = false,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                Issues = prepared.Issues,
                Row = prepared.Row
            });
        }

        PreparedCuentaCobroPaymentReceipt paymentPreflight;
        try
        {
            paymentPreflight = await PrepareCuentaCobroPaymentReceiptForSiigoAsync(
                request,
                supportDocumentId: "",
                supportDocumentName: "PREVALIDACION-1",
                ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible validar el pago antes de crear el documento soporte.", ex));
        }

        if (!paymentPreflight.CanSend || paymentPreflight.Payload is null)
        {
            return Ok(new ConciliacionCuentaCobroActionResultDto
            {
                Message = "Envio real bloqueado. El documento soporte no se creo porque el pago aun tiene pendientes.",
                IsSuccess = false,
                IsReadyForSiigo = false,
                TargetEndpoint = "/v1/purchase-support-documents + /v1/journals",
                PayloadJson = CombinePayloads(prepared.PayloadJson, paymentPreflight.PayloadJson),
                Issues = paymentPreflight.Issues,
                Row = paymentPreflight.Row
            });
        }

        bool claimed;
        try
        {
            claimed = await _dataverse.TryClaimConciliacionCuentaCobroSupportDocumentForSiigoAsync(request, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible reservar atomicamente la cuenta de cobro antes del envio.", ex));
        }

        if (!claimed)
        {
            ConciliacionCuentaCobroRowDto latest;
            try
            {
                latest = await _dataverse.GetConciliacionCuentaCobroDocumentAsync(request, ct);
            }
            catch (Exception ex)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    CreateErrorPayload("No fue posible confirmar por que la cuenta de cobro no obtuvo el bloqueo de envio.", ex));
            }

            var hasDocument = !string.IsNullOrWhiteSpace(latest.SiigoDocumentId)
                || !string.IsNullOrWhiteSpace(latest.SiigoDocumentName);
            var hasPayment = !string.IsNullOrWhiteSpace(latest.SiigoPaymentId)
                || !string.IsNullOrWhiteSpace(latest.SiigoPaymentName);
            if (hasDocument && !hasPayment)
                return await SendCuentaCobroSupportPaymentToSiigoCoreAsync(request, ct);
            if (hasDocument && hasPayment)
            {
                return Ok(new ConciliacionCuentaCobroActionResultDto
                {
                    Message = "La cuenta de cobro ya tiene documento soporte y pago confirmados en Siigo.",
                    IsSuccess = true,
                    IsReadyForSiigo = true,
                    TargetEndpoint = "/v1/purchase-support-documents + /v1/journals",
                    SiigoId = FirstNonEmpty(latest.SiigoPaymentId, latest.SiigoDocumentId),
                    SiigoName = FirstNonEmpty(latest.SiigoPaymentName, latest.SiigoDocumentName),
                    Row = latest
                });
            }

            var needsManualVerification = latest.AutomationState.Equals(
                    CuentaCobroSupportDocumentVerificationState,
                    StringComparison.OrdinalIgnoreCase)
                || latest.ReviewReason.Contains(
                    CuentaCobroSupportDocumentAmbiguousMarker,
                    StringComparison.OrdinalIgnoreCase);
            var claimMessage = needsManualVerification
                ? "No se repetira el envio: el resultado del documento soporte debe verificarse manualmente en Siigo."
                : latest.AutomationState.Equals(
                    CuentaCobroSupportDocumentProcessingState,
                    StringComparison.OrdinalIgnoreCase)
                    ? "Otra ejecucion ya reservo esta cuenta de cobro. No se repetira el documento soporte."
                    : "La cuenta de cobro cambio o ya fue tomada por otra ejecucion. Recarga Conciliacion antes de reintentar.";
            return Conflict(new ConciliacionCuentaCobroActionResultDto
            {
                Message = claimMessage,
                IsSuccess = false,
                IsReadyForSiigo = false,
                TargetEndpoint = "/v1/purchase-support-documents",
                Issues = new[] { claimMessage },
                Row = latest
            });
        }

        var durableCheckpointCt = CancellationToken.None;
        SiigoVoucherCreateResultDto siigoResult;
        try
        {
            siigoResult = await _siigo.CreatePurchaseSupportDocumentAsync(
                prepared.Payload,
                BuildSiigoIdempotencyKey($"cuenta-cobro-{BuildCuentaCobroSiigoIdempotencyIdentity(request)}"),
                ct);
        }
        catch (InvalidOperationException ex) when (!IsAmbiguousSupplierCreateFailure(ex))
        {
            var detail = BuildExceptionDetail(ex);
            var result = await _dataverse.MarkConciliacionCuentaCobroSiigoResultAsync(
                request,
                success: false,
                message: "Siigo rechazo el documento soporte; el claim se libero para una correccion y un nuevo intento.",
                responseJson: detail,
                payloadJson: prepared.PayloadJson,
                stateOverride: "ErrorSiigo",
                targetEndpoint: "/v1/purchase-support-documents",
                ct: durableCheckpointCt);
            result.PayloadJson = prepared.PayloadJson;
            result.Issues = new[] { detail };
            return Ok(result);
        }
        catch (Exception ex)
        {
            var detail = BuildExceptionDetail(ex);
            try
            {
                var result = await _dataverse.MarkConciliacionCuentaCobroSiigoResultAsync(
                    request,
                    success: false,
                    message: "El resultado del documento soporte es ambiguo. No se repetira el POST hasta verificarlo manualmente en Siigo.",
                    responseJson: detail,
                    payloadJson: prepared.PayloadJson,
                    stateOverride: CuentaCobroSupportDocumentVerificationState,
                    targetEndpoint: "/v1/purchase-support-documents",
                    ct: durableCheckpointCt);
                result.PayloadJson = prepared.PayloadJson;
                result.Issues = new[]
                {
                    detail,
                    "Verifica en Siigo si el documento soporte fue creado antes de realizar cualquier nuevo intento."
                };
                return Ok(result);
            }
            catch (Exception checkpointException)
            {
                _logger.LogError(
                    checkpointException,
                    "No fue posible guardar el hold de verificacion del documento soporte para {RecordId}. Error original: {OriginalError}",
                    request.RecordId,
                    detail);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new ConciliacionCuentaCobroActionResultDto
                    {
                        Message = "El resultado del documento soporte es ambiguo y Dataverse no pudo guardar el detalle. El claim se conserva: no reintentes; verifica Siigo manualmente.",
                        IsSuccess = false,
                        IsReadyForSiigo = false,
                        TargetEndpoint = "/v1/purchase-support-documents",
                        PayloadJson = prepared.PayloadJson,
                        ResponseJson = detail,
                        Issues = new[] { detail, BuildExceptionDetail(checkpointException) },
                        Row = prepared.Row
                    });
            }
        }

        try
        {
            await _dataverse.MarkConciliacionCuentaCobroSiigoResultAsync(
                request,
                success: false,
                message: "Documento soporte creado en Siigo. Pago pendiente de enviar.",
                siigoId: siigoResult.Id,
                siigoName: siigoResult.Name,
                responseJson: siigoResult.RawJson,
                payloadJson: prepared.PayloadJson,
                stateOverride: CuentaCobroSupportDocumentPendingPaymentState,
                targetEndpoint: "/v1/purchase-support-documents",
                ct: durableCheckpointCt);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Siigo confirmo el documento soporte {SiigoDocumentId} para {RecordId}, pero Dataverse no guardo el checkpoint.",
                siigoResult.Id,
                request.RecordId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ConciliacionCuentaCobroActionResultDto
                {
                    Message = "Siigo confirmo el documento soporte, pero Dataverse no pudo guardar su identificador. El claim se conserva: no reintentes; verifica y corrige el checkpoint.",
                    IsSuccess = false,
                    IsReadyForSiigo = false,
                    TargetEndpoint = "/v1/purchase-support-documents",
                    PayloadJson = prepared.PayloadJson,
                    ResponseJson = siigoResult.RawJson,
                    SiigoId = siigoResult.Id,
                    SiigoName = siigoResult.Name,
                    Issues = new[] { BuildExceptionDetail(ex) },
                    Row = prepared.Row
                });
        }

        PreparedCuentaCobroPaymentReceipt paymentPrepared;
        try
        {
            paymentPrepared = await PrepareCuentaCobroPaymentReceiptForSiigoAsync(
                request,
                siigoResult.Id,
                siigoResult.Name,
                ct);
        }
        catch (Exception ex)
        {
            var detail = BuildExceptionDetail(ex);
            var failedResult = await _dataverse.MarkConciliacionCuentaCobroSiigoResultAsync(
                request,
                success: false,
                message: "Documento soporte creado, pero no fue posible preparar el pago.",
                siigoId: siigoResult.Id,
                siigoName: siigoResult.Name,
                responseJson: CombineSiigoResponses(siigoResult.RawJson, detail),
                payloadJson: prepared.PayloadJson,
                stateOverride: CuentaCobroSupportDocumentPendingPaymentState,
                targetEndpoint: "/v1/purchase-support-documents + /v1/journals",
                ct: durableCheckpointCt);
            failedResult.Issues = new[] { detail };
            return Ok(failedResult);
        }

        if (!paymentPrepared.CanSend || paymentPrepared.Payload is null)
        {
            var failedResult = await _dataverse.MarkConciliacionCuentaCobroSiigoResultAsync(
                request,
                success: false,
                message: "Documento soporte creado, pero el pago quedo bloqueado.",
                siigoId: siigoResult.Id,
                siigoName: siigoResult.Name,
                responseJson: CombineSiigoResponses(siigoResult.RawJson, string.Join(Environment.NewLine, paymentPrepared.Issues)),
                payloadJson: CombinePayloads(prepared.PayloadJson, paymentPrepared.PayloadJson),
                stateOverride: CuentaCobroSupportDocumentPendingPaymentState,
                targetEndpoint: "/v1/purchase-support-documents + /v1/journals",
                ct: durableCheckpointCt);
            failedResult.Issues = paymentPrepared.Issues;
            return Ok(failedResult);
        }

        try
        {
            try
            {
                var paymentResult = await _siigo.CreateJournalAsync(
                    paymentPrepared.Payload,
                    BuildSiigoIdempotencyKey($"cuenta-cobro-pago-{BuildCuentaCobroSiigoIdempotencyIdentity(request)}"),
                    ct);
                var documentLabel = FirstNonEmpty(siigoResult.Name, siigoResult.Id);
                var paymentLabel = FirstNonEmpty(paymentResult.Name, paymentResult.Id);
                var message = $"Documento soporte {documentLabel} y pago {paymentLabel} enviados a Siigo.";
                var result = await _dataverse.MarkConciliacionCuentaCobroSiigoResultAsync(
                    request,
                    success: true,
                    message: message,
                    siigoId: siigoResult.Id,
                    siigoName: siigoResult.Name,
                    siigoPaymentId: paymentResult.Id,
                    siigoPaymentName: paymentResult.Name,
                    responseJson: CombineSiigoResponses(siigoResult.RawJson, paymentResult.RawJson),
                    payloadJson: CombinePayloads(prepared.PayloadJson, paymentPrepared.PayloadJson),
                    targetEndpoint: "/v1/purchase-support-documents + /v1/journals",
                    ct: durableCheckpointCt);

                result.PayloadJson = CombinePayloads(prepared.PayloadJson, paymentPrepared.PayloadJson);
                result.ResponseJson = CombineSiigoResponses(siigoResult.RawJson, paymentResult.RawJson);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                var detail = BuildExceptionDetail(ex);
                var failedResult = await _dataverse.MarkConciliacionCuentaCobroSiigoResultAsync(
                    request,
                    success: false,
                    message: "Documento soporte creado, pero Siigo rechazo el pago.",
                    siigoId: siigoResult.Id,
                    siigoName: siigoResult.Name,
                    responseJson: CombineSiigoResponses(siigoResult.RawJson, detail),
                    payloadJson: CombinePayloads(prepared.PayloadJson, paymentPrepared.PayloadJson),
                    stateOverride: CuentaCobroSupportDocumentPendingPaymentState,
                    targetEndpoint: "/v1/purchase-support-documents + /v1/journals",
                    ct: durableCheckpointCt);
                failedResult.Issues = new[] { detail };
                return Ok(failedResult);
            }
            catch (Exception ex)
            {
                var detail = BuildExceptionDetail(ex);
                var failedResult = await _dataverse.MarkConciliacionCuentaCobroSiigoResultAsync(
                    request,
                    success: false,
                    message: "Documento soporte creado, pero no fue posible enviar el pago.",
                    siigoId: siigoResult.Id,
                    siigoName: siigoResult.Name,
                    responseJson: CombineSiigoResponses(siigoResult.RawJson, detail),
                    payloadJson: CombinePayloads(prepared.PayloadJson, paymentPrepared.PayloadJson),
                    stateOverride: CuentaCobroSupportDocumentPendingPaymentState,
                    targetEndpoint: "/v1/purchase-support-documents + /v1/journals",
                    ct: durableCheckpointCt);
                failedResult.Issues = new[] { detail };
                return StatusCode(StatusCodes.Status500InternalServerError, failedResult);
            }
        }
        catch (Exception ex)
        {
            var detail = BuildExceptionDetail(ex);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("El documento soporte quedo creado, pero no fue posible guardar el estado pendiente de pago.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SendCuentaCobroSupportPaymentToSiigo(
        [FromBody] ConciliacionCuentaCobroDocumentRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar la cuenta de cobro a pagar."));

        return await SendCuentaCobroSupportPaymentToSiigoCoreAsync(request, ct);
    }

    private async Task<IActionResult> SendCuentaCobroSupportPaymentToSiigoCoreAsync(
        ConciliacionCuentaCobroDocumentRequest request,
        CancellationToken ct)
    {
        ConciliacionCuentaCobroRowDto currentRow;
        try
        {
            currentRow = await _dataverse.GetConciliacionCuentaCobroDocumentAsync(request, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible consultar el documento soporte antes de pagar.", ex));
        }

        var hasDocumentWriteHold = currentRow.AutomationState.Equals(
                CuentaCobroSupportDocumentProcessingState,
                StringComparison.OrdinalIgnoreCase)
            || currentRow.AutomationState.Equals(
                CuentaCobroSupportDocumentVerificationState,
                StringComparison.OrdinalIgnoreCase)
            || currentRow.ReviewReason.Contains(
                CuentaCobroSupportDocumentAmbiguousMarker,
                StringComparison.OrdinalIgnoreCase);
        if (hasDocumentWriteHold)
        {
            const string holdMessage =
                "El documento soporte conserva una reserva o verificacion pendiente. "
                + "No se enviara el pago hasta resolverla explicitamente.";
            return Conflict(new ConciliacionCuentaCobroActionResultDto
            {
                Message = holdMessage,
                IsSuccess = false,
                IsReadyForSiigo = false,
                TargetEndpoint = "/v1/journals",
                Issues = new[] { holdMessage },
                Row = currentRow
            });
        }

        PreparedCuentaCobroPaymentReceipt prepared;
        try
        {
            prepared = await PrepareCuentaCobroPaymentReceiptForSiigoAsync(request, "", "", ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible preparar el pago del documento soporte.", ex));
        }

        if (!prepared.CanSend || prepared.Payload is null)
        {
            return Ok(new ConciliacionCuentaCobroActionResultDto
            {
                Message = "Pago bloqueado. Corrige los pendientes visibles antes de reintentar.",
                IsSuccess = false,
                IsReadyForSiigo = false,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                Issues = prepared.Issues,
                Row = prepared.Row
            });
        }

        try
        {
            var paymentResult = await _siigo.CreateJournalAsync(
                prepared.Payload,
                BuildSiigoIdempotencyKey($"cuenta-cobro-pago-{BuildCuentaCobroSiigoIdempotencyIdentity(request)}"),
                ct);
            var paymentLabel = FirstNonEmpty(paymentResult.Name, paymentResult.Id);
            var result = await _dataverse.MarkConciliacionCuentaCobroSiigoResultAsync(
                request,
                success: true,
                message: string.IsNullOrWhiteSpace(paymentLabel)
                    ? "Pago del documento soporte enviado a Siigo."
                    : $"Pago del documento soporte enviado a Siigo: {paymentLabel}.",
                siigoPaymentId: paymentResult.Id,
                siigoPaymentName: paymentResult.Name,
                responseJson: paymentResult.RawJson,
                payloadJson: prepared.PayloadJson,
                targetEndpoint: prepared.TargetEndpoint,
                ct: ct);

            result.ResponseJson = paymentResult.RawJson;
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            var detail = BuildExceptionDetail(ex);
            var result = await _dataverse.MarkConciliacionCuentaCobroSiigoResultAsync(
                request,
                success: false,
                message: "Siigo rechazo el pago del documento soporte.",
                responseJson: detail,
                payloadJson: prepared.PayloadJson,
                targetEndpoint: prepared.TargetEndpoint,
                ct: ct);
            result.Issues = new[] { detail };
            return Ok(result);
        }
        catch (Exception ex)
        {
            var detail = BuildExceptionDetail(ex);
            var result = await _dataverse.MarkConciliacionCuentaCobroSiigoResultAsync(
                request,
                success: false,
                message: "No fue posible completar el pago del documento soporte.",
                responseJson: detail,
                payloadJson: prepared.PayloadJson,
                targetEndpoint: prepared.TargetEndpoint,
                ct: ct);
            result.Issues = new[] { detail };
            return StatusCode(StatusCodes.Status500InternalServerError, result);
        }
    }

    private async Task<ConciliacionSiigoOpenInvoiceSearchResultDto> SearchClientOpenInvoicesForPaymentAsync(
        ConciliacionClientPaymentInvoiceSearchRequest request,
        CancellationToken ct)
    {
        var row = await _dataverse.GetConciliacionCashFlowMovementAsync(
            new ConciliacionCashFlowAccountingVoucherRequest
            {
                RecordId = request.RecordId,
                MovementExternalKey = request.MovementExternalKey,
                SourceKind = "Movimiento"
            },
            ct);
        if (!string.Equals(row.Direction, "Entrada", StringComparison.OrdinalIgnoreCase) || row.EntryValue <= 0m)
            throw new InvalidOperationException("La fila seleccionada no es una entrada bancaria con valor.");

        var customerQuery = (request.CustomerQuery ?? "").Trim();
        if (string.IsNullOrWhiteSpace(request.CustomerId) && string.IsNullOrWhiteSpace(customerQuery))
            throw new InvalidOperationException("Busca y selecciona un cliente de Siigo.");

        var movementDate = DateOnly.TryParseExact(
            row.MovementDateValue,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedDate)
            ? parsedDate
            : DateOnly.FromDateTime(DateTime.UtcNow);
        var months = Math.Clamp(request.LookbackMonths <= 0 ? 60 : request.LookbackMonths, 1, 120);
        var endDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        if (endDate < movementDate)
            endDate = movementDate.AddDays(1);

        var invoicesTask = _siigo.GetInvoicesAsync(
            request.CustomerId,
            customerQuery,
            movementDate.AddMonths(-months),
            endDate,
            ct);
        var taxesTask = _siigo.GetTaxesAsync(ct);
        var billingTask = _dataverse.GetRegistroPagosClientesBoardAsync(ct);
        await Task.WhenAll(invoicesTask, taxesTask, billingTask);

        var result = await invoicesTask;
        var taxes = await taxesTask;
        var billing = await billingTask;
        var customer = new ConciliacionSiigoSupplierLookupDto
        {
            Id = result.CustomerId,
            DisplayName = result.CustomerDisplayName,
            Name = result.CustomerDisplayName,
            Identification = result.CustomerIdentification,
            BranchOffice = result.CustomerBranchOffice,
            Active = true
        };
        var customerBilling = billing.Invoices
            .Where(invoice => ClientPaymentBillingCustomerMatches(invoice, customer))
            .ToArray();
        var invoices = result.Invoices
            .Where(static invoice => !invoice.Annulled && RoundCurrency(invoice.Balance) > 0m)
            .Select(invoice =>
            {
                var billingInvoice = FindClientPaymentBillingInvoice(customerBilling, invoice);
                var vat = RoundCurrency(invoice.Vat > 0m
                    ? invoice.Vat
                    : billingInvoice?.VatValue ?? 0m);
                var total = RoundCurrency(invoice.GrossTotal > 0m
                    ? invoice.GrossTotal
                    : invoice.Total);
                var balance = RoundCurrency(invoice.GrossBalance > 0m
                    ? invoice.GrossBalance
                    : invoice.Balance);
                var taxBase = RoundCurrency(Math.Max(total - vat, 0m));
                if (taxBase <= 0m)
                    taxBase = total;

                return new ConciliacionSiigoOpenInvoiceDto
                {
                    Id = invoice.Id,
                    DataverseRecordId = billingInvoice?.RecordId ?? "",
                    Name = invoice.Name,
                    Prefix = invoice.Prefix,
                    Number = invoice.Number,
                    DateValue = invoice.DateValue,
                    DateDisplay = invoice.DateDisplay,
                    CustomerId = customer.Id,
                    CustomerName = customer.DisplayName,
                    CustomerIdentification = invoice.CustomerIdentification,
                    CustomerBranchOffice = invoice.CustomerBranchOffice,
                    Total = total,
                    Vat = vat,
                    TaxBase = taxBase,
                    Balance = balance,
                    SiigoBalance = RoundCurrency(invoice.Balance),
                    DuePrefix = invoice.DuePrefix,
                    DueConsecutive = invoice.DueConsecutive,
                    DueQuote = invoice.DueQuote,
                    DueDateValue = invoice.DueDateValue,
                    DueDateDisplay = invoice.DueDateDisplay,
                    HasExactDueReference = invoice.HasExactDueReference,
                    DueReferenceIssue = invoice.DueReferenceIssue
                };
            })
            .OrderBy(static invoice => invoice.DateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static invoice => invoice.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var paidInvoices = BuildClientPaymentPaidInvoiceHistory(result.Invoices, customerBilling);
        var reteFuenteOptions = BuildClientPaymentRetentionOptions(taxes, "ReteFte");
        var reteIcaOptions = BuildClientPaymentRetentionOptions(taxes, "ReteIca");
        var rteIvaOptions = BuildClientPaymentRetentionOptions(taxes, "RteIva");

        return new ConciliacionSiigoOpenInvoiceSearchResultDto
        {
            Message = invoices.Length == 0
                ? $"No encontramos facturas con saldo para {result.CustomerDisplayName}."
                : $"Encontramos {invoices.Length:N0} factura{(invoices.Length == 1 ? "" : "s")} con saldo.",
            Customer = customer,
            Invoices = invoices,
            PaidInvoices = paidInvoices,
            ReteFuenteOptions = reteFuenteOptions,
            ReteIcaOptions = reteIcaOptions,
            RteIvaOptions = rteIvaOptions
        };
    }

    private static bool ClientPaymentBillingCustomerMatches(
        RegistroPagosClientesInvoiceDto invoice,
        ConciliacionSiigoSupplierLookupDto customer)
    {
        var invoiceIdentification = ExtractDigits(invoice.CompanyTaxId);
        var customerIdentification = ExtractDigits(customer.Identification);
        if (invoiceIdentification.Length >= 5 && customerIdentification.Length >= 5)
            return IsSameSupplierIdentificationDigits(invoiceIdentification, customerIdentification);

        var invoiceName = NormalizeSupplierPaymentMatchText(invoice.ClientName);
        var customerName = NormalizeSupplierPaymentMatchText(FirstNonEmpty(customer.DisplayName, customer.Name));
        return !string.IsNullOrWhiteSpace(invoiceName)
            && string.Equals(invoiceName, customerName, StringComparison.OrdinalIgnoreCase);
    }

    private static RegistroPagosClientesInvoiceDto? FindClientPaymentBillingInvoice(
        IReadOnlyList<RegistroPagosClientesInvoiceDto> billingInvoices,
        SiigoInvoiceRowDto invoice)
    {
        return billingInvoices.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(item.SiigoInvoiceId)
                && string.Equals(item.SiigoInvoiceId, invoice.Id, StringComparison.OrdinalIgnoreCase))
            || SupplierPaymentInvoiceKeysMatch(
                BuildSupplierPaymentInvoiceKeys(
                    item.SiigoInvoiceName,
                    item.InvoiceNumber,
                    $"{item.InvoicePrefix}-{item.InvoiceCode}"),
                BuildSupplierPaymentInvoiceKeys(
                    invoice.Name,
                    $"{invoice.Prefix}-{invoice.Number}")));
    }

    private static SiigoInvoiceRowDto? FindClientPaymentSiigoInvoice(
        IReadOnlyList<SiigoInvoiceRowDto> siigoInvoices,
        RegistroPagosClientesInvoiceDto invoice)
    {
        return siigoInvoices.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(invoice.SiigoInvoiceId)
                && string.Equals(invoice.SiigoInvoiceId, item.Id, StringComparison.OrdinalIgnoreCase))
            || SupplierPaymentInvoiceKeysMatch(
                BuildSupplierPaymentInvoiceKeys(
                    invoice.SiigoInvoiceName,
                    invoice.InvoiceNumber,
                    $"{invoice.InvoicePrefix}-{invoice.InvoiceCode}"),
                BuildSupplierPaymentInvoiceKeys(
                    item.Name,
                    $"{item.Prefix}-{item.Number}")));
    }

    private static IReadOnlyList<ConciliacionSiigoPaidInvoiceDto> BuildClientPaymentPaidInvoiceHistory(
        IReadOnlyList<SiigoInvoiceRowDto> siigoInvoices,
        IReadOnlyList<RegistroPagosClientesInvoiceDto> billingInvoices)
    {
        var paidSiigoInvoices = siigoInvoices
            .Where(static invoice => !invoice.Annulled && RoundCurrency(invoice.Balance) <= 0m)
            .OrderByDescending(static invoice => invoice.DateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static invoice => invoice.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var result = new List<ConciliacionSiigoPaidInvoiceDto>(5);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var billingInvoice in billingInvoices
            .Where(static invoice => string.Equals(invoice.PaymentStatusKey, "paid", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static invoice => invoice.PaymentDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(static invoice => invoice.EmissionDateValue, StringComparer.OrdinalIgnoreCase))
        {
            var siigoInvoice = FindClientPaymentSiigoInvoice(paidSiigoInvoices, billingInvoice);
            if (siigoInvoice is null)
                continue;

            var key = FirstNonEmpty(siigoInvoice.Id, siigoInvoice.Name);
            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                continue;

            result.Add(BuildClientPaymentPaidInvoice(siigoInvoice, billingInvoice));
            if (result.Count == 5)
                return result;
        }

        foreach (var siigoInvoice in paidSiigoInvoices)
        {
            var billingInvoice = FindClientPaymentBillingInvoice(billingInvoices, siigoInvoice);
            if (billingInvoice is null)
                continue;

            var key = FirstNonEmpty(siigoInvoice.Id, siigoInvoice.Name);
            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                continue;

            result.Add(BuildClientPaymentPaidInvoice(siigoInvoice, billingInvoice));
            if (result.Count == 5)
                break;
        }

        return result;
    }

    private static ConciliacionSiigoPaidInvoiceDto BuildClientPaymentPaidInvoice(
        SiigoInvoiceRowDto? siigoInvoice,
        RegistroPagosClientesInvoiceDto? billingInvoice)
    {
        var total = RoundCurrency(siigoInvoice is null
            ? billingInvoice?.TotalInvoice ?? 0m
            : siigoInvoice.GrossTotal > 0m
                ? siigoInvoice.GrossTotal
                : siigoInvoice.Total);
        var vat = RoundCurrency(siigoInvoice?.Vat > 0m
            ? siigoInvoice.Vat
            : billingInvoice?.VatValue ?? 0m);
        var taxBase = RoundCurrency(Math.Max(total - vat, 0m));
        if (taxBase <= 0m)
            taxBase = total;

        var storedReteFuente = billingInvoice?.ReteFtePercent ?? 0m;
        var storedReteIca = billingInvoice?.ReteIcaPercent ?? 0m;
        var storedRteIva = billingInvoice?.RteIvaPercent ?? 0m;
        var reteFuenteValue = ResolveClientPaymentHistoricalValue(
            "ReteFte",
            storedReteFuente,
            billingInvoice?.ReteFteValue ?? 0m);
        var reteIcaValue = ResolveClientPaymentHistoricalValue(
            "ReteIca",
            storedReteIca,
            billingInvoice?.ReteIcaValue ?? 0m);
        var rteIvaValue = ResolveClientPaymentHistoricalValue(
            "RteIva",
            storedRteIva,
            billingInvoice?.RteIvaValue ?? 0m);
        return new ConciliacionSiigoPaidInvoiceDto
        {
            Id = FirstNonEmpty(siigoInvoice?.Id, billingInvoice?.SiigoInvoiceId, billingInvoice?.RecordId),
            Name = FirstNonEmpty(siigoInvoice?.Name, billingInvoice?.SiigoInvoiceName, billingInvoice?.InvoiceNumber),
            InvoiceDateDisplay = FirstNonEmpty(siigoInvoice?.DateDisplay, billingInvoice?.EmissionDateDisplay),
            PaymentDateValue = billingInvoice?.PaymentDateValue ?? "",
            PaymentDateDisplay = billingInvoice?.PaymentDateDisplay ?? "",
            Total = total,
            PaymentValue = RoundCurrency(billingInvoice?.PaymentValue ?? total),
            ReteFuenteRate = ResolveClientPaymentHistoricalRate(
                "ReteFte",
                storedReteFuente,
                reteFuenteValue,
                taxBase),
            ReteFuenteValue = reteFuenteValue,
            ReteIcaRate = ResolveClientPaymentHistoricalRate(
                "ReteIca",
                storedReteIca,
                reteIcaValue,
                taxBase),
            ReteIcaValue = reteIcaValue,
            RteIvaRate = ResolveClientPaymentHistoricalRate(
                "RteIva",
                storedRteIva,
                rteIvaValue,
                vat),
            RteIvaValue = rteIvaValue
        };
    }

    private static decimal ResolveClientPaymentHistoricalValue(
        string kind,
        decimal storedValue,
        decimal calculatedValue)
    {
        var storesCurrency = string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase)
            ? storedValue > 50m
            : storedValue > 1m;
        return RoundCurrency(storesCurrency ? storedValue : calculatedValue);
    }

    private static decimal ResolveClientPaymentHistoricalRate(
        string kind,
        decimal storedRate,
        decimal retentionValue,
        decimal taxBase)
    {
        if (storedRate > 0m)
        {
            if (string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase) && storedRate <= 50m)
                return Math.Round(storedRate, 4, MidpointRounding.AwayFromZero);
            if (!string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase) && storedRate <= 1m)
                return Math.Round(storedRate * 100m, 4, MidpointRounding.AwayFromZero);
        }

        if (retentionValue <= 0m || taxBase <= 0m)
            return 0m;

        var multiplier = string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase)
            ? 1000m
            : 100m;
        return Math.Round(retentionValue / taxBase * multiplier, 4, MidpointRounding.AwayFromZero);
    }

    private static IReadOnlyList<ConciliacionSiigoRetentionOptionDto> BuildClientPaymentRetentionOptions(
        IReadOnlyList<SiigoTaxLookupDto> taxes,
        string kind)
    {
        return taxes
            .Where(tax => tax.Active
                && tax.Id > 0
                && tax.Percentage > 0m
                && ConciliacionRetentionMapping.MatchesKind(tax, kind)
                && !string.IsNullOrWhiteSpace(ConciliacionRetentionMapping.ResolveAccountCode(kind, tax, tax.Percentage)))
            .GroupBy(tax => Math.Round(tax.Percentage, 4, MidpointRounding.AwayFromZero))
            .Select(group => group
                .OrderBy(static tax => tax.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static tax => tax.Id)
                .First())
            .OrderBy(static tax => tax.Percentage)
            .Select(tax => new ConciliacionSiigoRetentionOptionDto
            {
                TaxId = tax.Id,
                Kind = kind,
                Name = tax.Name,
                Rate = Math.Round(tax.Percentage, 4, MidpointRounding.AwayFromZero),
                RateLabel = string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase)
                    ? $"{FormatClientPaymentRate(tax.Percentage)} x mil"
                    : $"{FormatClientPaymentRate(tax.Percentage)}%"
            })
            .ToArray();
    }

    private static IReadOnlyList<ConciliacionSiigoRetentionOptionDto> BuildCuentaCobroRetentionOptions(
        IReadOnlyList<SiigoTaxLookupDto> taxes,
        string kind)
    {
        return (taxes ?? Array.Empty<SiigoTaxLookupDto>())
            .Where(tax => tax.Active
                && tax.Id > 0
                && tax.Percentage > 0m
                && ConciliacionRetentionMapping.MatchesKind(tax, kind)
                && !string.IsNullOrWhiteSpace(ResolveCuentaCobroPaymentRetentionAccountCode(
                    new ConciliacionCuentaCobroRetentionDto
                    {
                        Kind = kind,
                        TaxId = tax.Id,
                        Rate = tax.Percentage
                    })))
            .GroupBy(tax => Math.Round(tax.Percentage, 4, MidpointRounding.AwayFromZero))
            .Select(group => group
                .OrderBy(static tax => tax.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static tax => tax.Id)
                .First())
            .OrderBy(static tax => tax.Percentage)
            .Select(tax => new ConciliacionSiigoRetentionOptionDto
            {
                TaxId = tax.Id,
                Kind = kind,
                Name = tax.Name,
                Rate = Math.Round(tax.Percentage, 4, MidpointRounding.AwayFromZero),
                RateLabel = string.Equals(kind, "ReteICA", StringComparison.OrdinalIgnoreCase)
                    ? $"{FormatClientPaymentRate(tax.Percentage)} x mil"
                    : $"{FormatClientPaymentRate(tax.Percentage)}%"
            })
            .ToArray();
    }

    internal static IReadOnlyList<ConciliacionCuentaCobroRetentionDto> ResolveCuentaCobroExpenseRetentions(
        ConciliacionCuentaCobroExpenseSaveRequest request,
        IReadOnlyList<SiigoTaxLookupDto> taxes,
        ICollection<string> issues)
    {
        var resolved = new List<ConciliacionCuentaCobroRetentionDto>();
        var usedKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var total = RoundCurrency(request.ValorTotal);
        var vat = RoundCurrency(request.ValorIva);
        var taxBase = RoundCurrency(total - vat);

        if (vat < 0m || vat > total)
        {
            issues.Add("El valor IVA debe estar entre cero y el valor total de la cuenta de cobro.");
            return resolved;
        }

        foreach (var requested in request.Retentions ?? Array.Empty<ConciliacionCuentaCobroRetentionDto>())
        {
            var kind = NormalizeCuentaCobroRetentionKind(requested.Kind);
            if (requested.TaxId <= 0)
            {
                if (requested.Value > 0m || requested.Rate > 0m)
                    issues.Add($"Selecciona una tarifa Siigo valida para {FirstNonEmpty(requested.Label, requested.Kind, "la retencion")}.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(kind))
            {
                issues.Add($"El tipo de retencion {requested.Kind} no esta permitido para una cuenta de cobro.");
                continue;
            }
            if (!usedKinds.Add(kind))
            {
                issues.Add($"Solo puedes seleccionar una tarifa de {kind}.");
                continue;
            }

            var tax = (taxes ?? Array.Empty<SiigoTaxLookupDto>())
                .FirstOrDefault(candidate => candidate.Id == requested.TaxId);
            if (tax is null || !tax.Active || tax.Percentage <= 0m)
            {
                issues.Add($"El impuesto Siigo {requested.TaxId} ya no existe o no esta activo.");
                continue;
            }
            if (!ConciliacionRetentionMapping.MatchesKind(tax, kind))
            {
                issues.Add($"El impuesto Siigo {tax.Id} no corresponde al tipo {kind}.");
                continue;
            }

            var rate = Math.Round(tax.Percentage, 4, MidpointRounding.AwayFromZero);
            var accountCode = ResolveCuentaCobroPaymentRetentionAccountCode(
                new ConciliacionCuentaCobroRetentionDto
                {
                    Kind = kind,
                    TaxId = tax.Id,
                    Rate = rate
                });
            if (string.IsNullOrWhiteSpace(accountCode))
            {
                issues.Add($"La tarifa {tax.Name} no tiene una cuenta contable aprobada para el pago.");
                continue;
            }

            var isReteIca = string.Equals(kind, "ReteICA", StringComparison.OrdinalIgnoreCase);
            var isRteIva = string.Equals(kind, "RteIVA", StringComparison.OrdinalIgnoreCase);
            var retentionBase = isRteIva ? vat : taxBase;
            if (isRteIva && retentionBase <= 0m)
            {
                issues.Add($"No se puede calcular {FirstNonEmpty(tax.Name, kind)} porque la cuenta de cobro no tiene valor IVA.");
                continue;
            }

            var divisor = isReteIca ? 1000m : 100m;
            var value = retentionBase > 0m ? RoundCurrency(retentionBase * rate / divisor) : 0m;
            if (value <= 0m)
            {
                issues.Add($"La tarifa {tax.Name} produce una retencion en cero.");
                continue;
            }

            resolved.Add(new ConciliacionCuentaCobroRetentionDto
            {
                Kind = kind,
                Label = FirstNonEmpty(tax.Name, kind),
                TaxId = tax.Id,
                AccountCode = accountCode,
                BaseValue = retentionBase,
                Rate = rate,
                Value = value
            });
        }

        return resolved;
    }

    private static string NormalizeCuentaCobroRetentionKind(string? kind)
    {
        if (string.Equals(kind, "ReteFuente", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "ReteFte", StringComparison.OrdinalIgnoreCase))
        {
            return "ReteFuente";
        }
        if (string.Equals(kind, "ReteICA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase))
        {
            return "ReteICA";
        }
        if (string.Equals(kind, "RteIVA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "ReteIVA", StringComparison.OrdinalIgnoreCase))
        {
            return "RteIVA";
        }

        return "";
    }

    private static string FormatClientPaymentRate(decimal rate) =>
        rate.ToString("0.####", CultureInfo.InvariantCulture).Replace('.', ',');

    private async Task<ConciliacionClientInvoicePaymentApplyResultDto> ApplyClientInvoicePaymentToDataverseAsync(
        ConciliacionClientInvoicePaymentApplyRequest request,
        CancellationToken ct)
    {
        var row = await _dataverse.GetConciliacionCashFlowMovementAsync(
            new ConciliacionCashFlowAccountingVoucherRequest
            {
                RecordId = request.RecordId,
                MovementExternalKey = request.MovementExternalKey,
                SourceKind = "Movimiento"
            },
            ct);
        var issues = new List<string>();
        IReadOnlyCollection<ConciliacionPaymentAllocationRequest> requestedAllocations = request.Allocations.Count > 0
            ? request.Allocations
            : request.Allocation is null
                ? Array.Empty<ConciliacionPaymentAllocationRequest>()
                : new[] { request.Allocation };
        var resolved = await ResolveClientPaymentAllocationsAsync(
            request.RecordId,
            request.MovementExternalKey,
            request.CustomerId,
            request.CustomerIdentification,
            request.CustomerName,
            requestedAllocations,
            issues,
            ct);
        var allocatedInvoices = resolved.Invoices.ToList();

        if (allocatedInvoices.Count == 0)
            issues.Add("Selecciona al menos una factura e indica el valor pagado.");
        ValidateClientInvoicePaymentTotal(row.EntryValue, allocatedInvoices, issues);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(string.Join(
                " ",
                issues.DefaultIfEmpty("No fue posible validar las facturas seleccionadas.")
                    .Distinct(StringComparer.OrdinalIgnoreCase)));
        }

        var savedItems = new List<ConciliacionClientInvoicePaymentApplyItemDto>(allocatedInvoices.Count);
        RegistroPagosClientesPaymentSaveResult? firstSaved = null;
        foreach (var allocatedInvoice in allocatedInvoices)
        {
            var saved = await SaveClientInvoicePaymentToDataverseAsync(row, allocatedInvoice, ct);
            firstSaved ??= saved;
            savedItems.Add(new ConciliacionClientInvoicePaymentApplyItemDto
            {
                DocumentId = allocatedInvoice.Invoice.Id,
                DataverseRecordId = saved.Invoice.RecordId,
                InvoiceNumber = saved.Invoice.InvoiceNumber,
                CustomerId = allocatedInvoice.Customer?.Id ?? "",
                CustomerIdentification = allocatedInvoice.Customer?.Identification ?? "",
                CustomerName = FirstNonEmpty(allocatedInvoice.Customer?.DisplayName, allocatedInvoice.Customer?.Name),
                PaymentValue = allocatedInvoice.PaymentValue
            });
        }

        var persistedSnapshot = await PersistClientInvoicePaymentSnapshotAsync(
            request.MatchRecordId,
            row,
            allocatedInvoices,
            ct);

        var firstInvoice = allocatedInvoices[0];
        var reteFuente = firstInvoice.Retentions.FirstOrDefault(static retention =>
            string.Equals(retention.Kind, "ReteFte", StringComparison.OrdinalIgnoreCase));
        var reteIca = firstInvoice.Retentions.FirstOrDefault(static retention =>
            string.Equals(retention.Kind, "ReteIca", StringComparison.OrdinalIgnoreCase));
        var rteIva = firstInvoice.Retentions.FirstOrDefault(static retention =>
            string.Equals(retention.Kind, "RteIva", StringComparison.OrdinalIgnoreCase));
        var invoiceCount = allocatedInvoices.Count;

        return new ConciliacionClientInvoicePaymentApplyResultDto
        {
            Message = invoiceCount == 1
                ? $"Aplicacion guardada en Dataverse para {firstInvoice.Invoice.Name}."
                : $"Aplicacion guardada en Dataverse para {invoiceCount:N0} facturas.",
            IsSuccess = true,
            SavedCount = invoiceCount,
            MatchRecordId = persistedSnapshot.RecordId,
            Items = savedItems,
            DataverseRecordId = firstSaved!.Invoice.RecordId,
            InvoiceNumber = firstSaved.Invoice.InvoiceNumber,
            PaymentDateValue = row.MovementDateValue,
            InvoiceTotal = firstInvoice.Invoice.Total,
            TaxBase = firstInvoice.Invoice.TaxBase,
            PaymentValue = firstInvoice.PaymentValue,
            ReteFuenteRate = reteFuente?.Rate ?? 0m,
            ReteFuenteValue = reteFuente?.Value ?? 0m,
            ReteIcaRate = reteIca?.Rate ?? 0m,
            ReteIcaValue = reteIca?.Value ?? 0m,
            RteIvaRate = rteIva?.Rate ?? 0m,
            RteIvaValue = rteIva?.Value ?? 0m,
            AdjustmentValue = firstInvoice.AdjustmentValue,
            FinalBalance = RoundCurrency(firstInvoice.Invoice.Balance - firstInvoice.GrossValue)
        };
    }

    private async Task<RegistroPagosClientesPaymentSaveResult> SaveClientInvoicePaymentToDataverseAsync(
        ConciliacionCashFlowRowDto row,
        AllocatedSiigoInvoice allocatedInvoice,
        CancellationToken ct)
    {
        if (allocatedInvoice.PaymentValue <= 0m)
            throw new InvalidOperationException($"El pago de {allocatedInvoice.Invoice.Name} debe ser mayor a cero para guardarlo en Dataverse.");
        if (!Guid.TryParse(allocatedInvoice.Invoice.DataverseRecordId, out _))
            throw new InvalidOperationException($"No encontramos {allocatedInvoice.Invoice.Name} en la base de facturacion de Dataverse.");

        var reteFuente = allocatedInvoice.Retentions.FirstOrDefault(static retention =>
            string.Equals(retention.Kind, "ReteFte", StringComparison.OrdinalIgnoreCase));
        var reteIca = allocatedInvoice.Retentions.FirstOrDefault(static retention =>
            string.Equals(retention.Kind, "ReteIca", StringComparison.OrdinalIgnoreCase));
        var rteIva = allocatedInvoice.Retentions.FirstOrDefault(static retention =>
            string.Equals(retention.Kind, "RteIva", StringComparison.OrdinalIgnoreCase));
        var saved = await _dataverse.SaveRegistroPagosClientePaymentAsync(
            new RegistroPagosClientesPaymentSaveRequest
            {
                RecordId = allocatedInvoice.Invoice.DataverseRecordId,
                PaymentDateValue = row.MovementDateValue,
                PaymentValue = allocatedInvoice.PaymentValue,
                ReteFtePercent = (reteFuente?.Rate ?? 0m) / 100m,
                ReteIcaPercent = reteIca?.Rate ?? 0m,
                RteIvaPercent = (rteIva?.Rate ?? 0m) / 100m,
                RteIvaBaseValue = allocatedInvoice.Invoice.Vat,
                ReteFteValue = reteFuente?.Value ?? 0m,
                ReteIcaValue = reteIca?.Value ?? 0m,
                RteIvaValue = rteIva?.Value ?? 0m,
                ExpectedInvoiceTotal = allocatedInvoice.Invoice.Balance
            },
            ct);

        var mismatches = new List<string>();
        if (!string.Equals(saved.Invoice.RecordId, allocatedInvoice.Invoice.DataverseRecordId, StringComparison.OrdinalIgnoreCase))
            mismatches.Add("el identificador de la factura");
        if (!ClientPaymentDataverseValueMatches(
                allocatedInvoice.PaymentValue,
                saved.PersistedPaymentValue))
        {
            mismatches.Add("el valor pagado");
        }
        if (mismatches.Count > 0)
        {
            throw new InvalidOperationException(
                $"Dataverse no confirmo correctamente {string.Join(", ", mismatches)} de {allocatedInvoice.Invoice.Name}.");
        }

        return saved;
    }

    private async Task<ConciliacionClientPaymentRowDto> PersistClientInvoicePaymentSnapshotAsync(
        string matchRecordId,
        ConciliacionCashFlowRowDto row,
        IReadOnlyCollection<AllocatedSiigoInvoice> allocatedInvoices,
        CancellationToken ct)
    {
        if (allocatedInvoices is null || allocatedInvoices.Count == 0)
            throw new InvalidOperationException("Selecciona al menos una factura para guardar el detalle del pago.");

        var invoiceRecordIds = JoinClientPaymentSnapshotValues(
            allocatedInvoices.Select(static item => item.Invoice.DataverseRecordId));
        var invoiceNumbers = JoinClientPaymentSnapshotValues(
            allocatedInvoices.Select(static item => item.Invoice.Name));
        var clientNames = JoinClientPaymentSnapshotValues(
            allocatedInvoices.Select(item => FirstNonEmpty(
                item.Customer?.DisplayName,
                item.Customer?.Name,
                item.Invoice.CustomerName)));
        var invoiceTotal = RoundCurrency(allocatedInvoices.Sum(static item => item.GrossValue));
        var paymentValue = RoundCurrency(allocatedInvoices.Sum(static item => item.PaymentValue));
        var reteFuenteValue = RoundCurrency(allocatedInvoices.Sum(static item => item.Retentions
            .Where(static retention => string.Equals(retention.Kind, "ReteFte", StringComparison.OrdinalIgnoreCase))
            .Sum(static retention => retention.Value)));
        var reteIcaValue = RoundCurrency(allocatedInvoices.Sum(static item => item.Retentions
            .Where(static retention => string.Equals(retention.Kind, "ReteIca", StringComparison.OrdinalIgnoreCase))
            .Sum(static retention => retention.Value)));
        var rteIvaValue = RoundCurrency(allocatedInvoices.Sum(static item => item.Retentions
            .Where(static retention => string.Equals(retention.Kind, "RteIva", StringComparison.OrdinalIgnoreCase))
            .Sum(static retention => retention.Value)));
        var differenceValue = RoundCurrency(allocatedInvoices.Sum(static item => item.AdjustmentValue));
        var snapshotJson = BuildClientInvoicePaymentDataverseSnapshotJson(row, allocatedInvoices);

        return await _dataverse.SaveConciliacionClientPaymentDataverseSnapshotAsync(
            new ConciliacionClientPaymentDataverseSnapshotRequest
            {
                MatchRecordId = matchRecordId,
                MovementRecordId = row.RecordId,
                MovementExternalKey = row.ExternalKey,
                InvoiceRecordIds = invoiceRecordIds,
                InvoiceNumbers = invoiceNumbers,
                ClientNames = clientNames,
                InvoiceTotal = invoiceTotal,
                PaymentValue = paymentValue,
                ReteFuenteValue = reteFuenteValue,
                ReteIcaValue = reteIcaValue,
                RteIvaValue = rteIvaValue,
                DifferenceValue = differenceValue,
                SnapshotJson = snapshotJson
            },
            ct);
    }

    private static string BuildClientInvoicePaymentDataverseSnapshotJson(
        ConciliacionCashFlowRowDto row,
        IReadOnlyCollection<AllocatedSiigoInvoice> allocatedInvoices)
    {
        var invoiceTotal = RoundCurrency(allocatedInvoices.Sum(static item => item.GrossValue));
        var paymentValue = RoundCurrency(allocatedInvoices.Sum(static item => item.PaymentValue));
        var reteFuenteValue = RoundCurrency(allocatedInvoices.Sum(static item => item.Retentions
            .Where(static retention => string.Equals(retention.Kind, "ReteFte", StringComparison.OrdinalIgnoreCase))
            .Sum(static retention => retention.Value)));
        var reteIcaValue = RoundCurrency(allocatedInvoices.Sum(static item => item.Retentions
            .Where(static retention => string.Equals(retention.Kind, "ReteIca", StringComparison.OrdinalIgnoreCase))
            .Sum(static retention => retention.Value)));
        var rteIvaValue = RoundCurrency(allocatedInvoices.Sum(static item => item.Retentions
            .Where(static retention => string.Equals(retention.Kind, "RteIva", StringComparison.OrdinalIgnoreCase))
            .Sum(static retention => retention.Value)));
        var differenceValue = RoundCurrency(allocatedInvoices.Sum(static item => item.AdjustmentValue));
        var lines = new List<object>();

        if (paymentValue > 0m)
        {
            lines.Add(new
            {
                accountCode = row.BankAccountCode,
                accountName = row.BankAccountName,
                description = row.BankAccountName,
                debit = paymentValue,
                credit = 0m
            });
        }

        foreach (var item in allocatedInvoices)
        {
            var customerName = FirstNonEmpty(
                item.Customer?.DisplayName,
                item.Customer?.Name,
                item.Invoice.CustomerName);
            foreach (var retention in item.Retentions.Where(static retention => retention.Value > 0m))
            {
                lines.Add(new
                {
                    accountCode = retention.AccountCode,
                    accountName = retention.Label,
                    thirdParty = customerName,
                    detail = item.Invoice.Name,
                    description = retention.Label,
                    debit = RoundCurrency(retention.Value),
                    credit = 0m
                });
            }

            lines.Add(new
            {
                accountCode = "13050501",
                accountName = "Clientes nacionales",
                thirdParty = customerName,
                detail = item.Invoice.Name,
                description = "Clientes nacionales",
                debit = 0m,
                credit = RoundCurrency(item.GrossValue)
            });

            if (item.AdjustmentValue != 0m)
            {
                lines.Add(new
                {
                    accountCode = "42958101",
                    accountName = "Ajuste al peso",
                    thirdParty = customerName,
                    detail = item.Invoice.Name,
                    description = "Ajuste al peso",
                    debit = item.AdjustmentValue > 0m ? RoundCurrency(item.AdjustmentValue) : 0m,
                    credit = item.AdjustmentValue < 0m ? RoundCurrency(Math.Abs(item.AdjustmentValue)) : 0m
                });
            }
        }

        var snapshot = new
        {
            type = "ComprobanteIngresoSiigoBorrador",
            source = "cash-flow-client-payment-manual",
            status = "ListoSiigo",
            movement = new
            {
                id = row.RecordId,
                externalKey = row.ExternalKey,
                date = row.MovementDateValue,
                sourceFlow = row.SourceFlow,
                description = row.Description,
                entry = paymentValue,
                bankAccountCode = row.BankAccountCode,
                bankAccountName = row.BankAccountName
            },
            invoices = allocatedInvoices.Select(item => new
            {
                recordId = item.Invoice.DataverseRecordId,
                documentId = item.Invoice.Id,
                number = item.Invoice.Name,
                customerId = FirstNonEmpty(item.Customer?.Id, item.Invoice.CustomerId),
                customerIdentification = FirstNonEmpty(
                    item.Customer?.Identification,
                    item.Invoice.CustomerIdentification),
                customerName = FirstNonEmpty(
                    item.Customer?.DisplayName,
                    item.Customer?.Name,
                    item.Invoice.CustomerName),
                customerBranchOffice = item.Customer?.BranchOffice ?? item.Invoice.CustomerBranchOffice,
                total = item.Invoice.Total,
                balance = item.Invoice.Balance,
                payment = item.PaymentValue,
                gross = item.GrossValue,
                adjustment = item.AdjustmentValue,
                retentions = item.Retentions.Select(retention => new
                {
                    kind = retention.Kind,
                    label = retention.Label,
                    taxId = retention.TaxId,
                    accountCode = retention.AccountCode,
                    rate = retention.Rate,
                    value = retention.Value
                }).ToArray()
            }).ToArray(),
            totals = new
            {
                invoiceTotal,
                payment = paymentValue,
                reteFte = reteFuenteValue,
                reteIca = reteIcaValue,
                rteIva = rteIvaValue,
                retentions = RoundCurrency(reteFuenteValue + reteIcaValue + rteIvaValue),
                difference = differenceValue
            },
            lines
        };

        return JsonSerializer.Serialize(snapshot);
    }

    private static string JoinClientPaymentSnapshotValues(IEnumerable<string?> values) =>
        string.Join(
            "; ",
            values
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));

    internal static bool ClientPaymentAdjustmentMatches(
        decimal expectedAdjustment,
        decimal savedDifference)
    {
        if (Math.Abs(expectedAdjustment) <= 0.009m)
            return true;

        return Math.Abs(savedDifference - expectedAdjustment) <= 1m;
    }

    internal static bool ClientPaymentDataverseValueMatches(
        decimal expectedValue,
        decimal savedValue)
    {
        if (Math.Abs(savedValue - expectedValue) <= 0.01m)
            return true;

        return expectedValue >= 0m
            && savedValue >= 0m
            && savedValue == decimal.Truncate(savedValue)
            && Math.Abs(expectedValue - savedValue) < 1m;
    }

    private async Task<ResolvedClientPaymentAllocations> ResolveClientPaymentAllocationsAsync(
        string recordId,
        string movementExternalKey,
        string fallbackCustomerId,
        string fallbackCustomerIdentification,
        string fallbackCustomerName,
        IReadOnlyCollection<ConciliacionPaymentAllocationRequest> allocations,
        ICollection<string> issues,
        CancellationToken ct)
    {
        var selected = (allocations ?? Array.Empty<ConciliacionPaymentAllocationRequest>())
            .Where(static allocation =>
                allocation.AppliedValue != 0m
                || allocation.ReteFuenteTaxId > 0
                || allocation.ReteIcaTaxId > 0
                || allocation.RteIvaTaxId > 0)
            .Select(allocation => new
            {
                Allocation = allocation,
                CustomerId = FirstNonEmpty(allocation.CustomerId, fallbackCustomerId).Trim(),
                CustomerIdentification = FirstNonEmpty(
                    allocation.CustomerIdentification,
                    fallbackCustomerIdentification).Trim(),
                CustomerName = FirstNonEmpty(allocation.CustomerName, fallbackCustomerName).Trim(),
                BranchOffice = Math.Max(0, allocation.CustomerBranchOffice)
            })
            .ToArray();

        var result = new List<AllocatedSiigoInvoice>(selected.Length);
        ConciliacionSiigoSupplierLookupDto? primaryCustomer = null;
        var seenInvoices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in selected.GroupBy(item => string.Join("|", new[]
                 {
                     item.CustomerId,
                     ExtractDigits(item.CustomerIdentification),
                     item.BranchOffice.ToString(CultureInfo.InvariantCulture),
                     item.CustomerName
                 }), StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            if (string.IsNullOrWhiteSpace(first.CustomerId)
                && string.IsNullOrWhiteSpace(first.CustomerIdentification)
                && string.IsNullOrWhiteSpace(first.CustomerName))
            {
                issues.Add("Cada factura debe conservar la razon social seleccionada en Siigo.");
                continue;
            }

            ConciliacionSiigoOpenInvoiceSearchResultDto search;
            try
            {
                search = await SearchClientOpenInvoicesForPaymentAsync(
                    new ConciliacionClientPaymentInvoiceSearchRequest
                    {
                        RecordId = recordId,
                        MovementExternalKey = movementExternalKey,
                        CustomerId = first.CustomerId,
                        CustomerQuery = FirstNonEmpty(first.CustomerIdentification, first.CustomerName),
                        LookbackMonths = 60
                    },
                    ct);
            }
            catch (InvalidOperationException ex)
            {
                issues.Add($"{FirstNonEmpty(first.CustomerName, first.CustomerIdentification, "Cliente")}: {ex.Message}");
                continue;
            }

            var customer = search.Customer;
            if (customer is null)
            {
                issues.Add($"Siigo no confirmo la razon social {FirstNonEmpty(first.CustomerName, first.CustomerIdentification)}.");
                continue;
            }
            primaryCustomer ??= customer;

            foreach (var item in group)
            {
                var invoiceKey = FirstNonEmpty(item.Allocation.DocumentId, item.Allocation.DocumentName);
                var uniqueKey = $"{group.Key}|{invoiceKey}";
                if (string.IsNullOrWhiteSpace(invoiceKey) || !seenInvoices.Add(uniqueKey))
                {
                    issues.Add("Cada factura y razon social debe aparecer una sola vez en la aplicacion.");
                    continue;
                }

                var allocatedInvoice = BuildClientPaymentAllocatedInvoice(
                    item.Allocation,
                    search,
                    issues,
                    customer);
                if (allocatedInvoice is not null)
                    result.Add(allocatedInvoice);
            }
        }

        return new ResolvedClientPaymentAllocations(primaryCustomer, result);
    }

    private async Task<PreparedClientInvoicePayment> PrepareClientInvoicePaymentForSiigoAsync(
        ConciliacionClientInvoicePaymentSendRequest request,
        CancellationToken ct)
    {
        var row = await _dataverse.GetConciliacionCashFlowMovementAsync(
            new ConciliacionCashFlowAccountingVoucherRequest
            {
                RecordId = request.RecordId,
                MovementExternalKey = request.MovementExternalKey,
                SourceKind = "Movimiento"
            },
            ct);
        var issues = new List<string>();
        if (!string.Equals(row.Direction, "Entrada", StringComparison.OrdinalIgnoreCase))
            issues.Add("La fila seleccionada no es una entrada bancaria.");
        if (row.EntryValue <= 0m)
            issues.Add("La entrada no tiene valor para aplicar.");
        if (string.IsNullOrWhiteSpace(row.BankAccountCode))
            issues.Add("Falta la cuenta bancaria contable para registrar el ingreso.");
        if (!Guid.TryParse(request.MatchRecordId, out _))
            issues.Add("No encontramos el cruce de entrada asociado al movimiento.");
        if (string.IsNullOrWhiteSpace(request.CustomerId) && string.IsNullOrWhiteSpace(request.CustomerIdentification))
            issues.Add("Selecciona el cliente de Siigo.");

        var resolved = await ResolveClientPaymentAllocationsAsync(
            request.RecordId,
            request.MovementExternalKey,
            request.CustomerId,
            request.CustomerIdentification,
            request.CustomerName,
            request.Allocations,
            issues,
            ct);
        var customer = resolved.PrimaryCustomer;
        var allocatedInvoices = resolved.Invoices.ToList();

        if (allocatedInvoices.Count == 0)
            issues.Add("Aplica un valor al menos a una factura con saldo.");
        ValidateClientInvoicePaymentTotal(row.EntryValue, allocatedInvoices, issues);

        object? payload = null;
        var payloadJson = "";
        try
        {
            var documentTypes = await _siigo.GetDocumentTypesAsync("CC", ct);
            var document = ResolveIncomeJournalDocumentType(documentTypes);
            if (customer is not null && allocatedInvoices.Count > 0)
            {
                payload = BuildClientInvoicePaymentJournalPayload(row, customer, allocatedInvoices, document, issues);
                payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            }
        }
        catch (InvalidOperationException ex)
        {
            issues.Add(ex.Message);
        }

        return new PreparedClientInvoicePayment(
            CashFlowRow: row,
            Customer: customer,
            Invoices: allocatedInvoices,
            CanSend: issues.Count == 0 && payload is not null,
            TargetEndpoint: "/v1/journals",
            Payload: issues.Count == 0 ? payload : null,
            PayloadJson: payloadJson,
            Issues: issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    internal static void ValidateClientInvoicePaymentTotal(
        decimal movementValue,
        IReadOnlyCollection<AllocatedSiigoInvoice> allocatedInvoices,
        ICollection<string> issues)
    {
        var targetValue = RoundCurrency(movementValue);
        var appliedTotal = RoundCurrency(allocatedInvoices.Sum(static item => item.PaymentValue));
        if (Math.Abs(targetValue - appliedTotal) > ClientPaymentInvoiceDifferenceTolerance)
        {
            issues.Add(
                $"La diferencia entre los pagos seleccionados ({appliedTotal:N2}) y el movimiento bancario ({targetValue:N2}) supera la tolerancia permitida de +/- {ClientPaymentInvoiceDifferenceTolerance:N0}.");
        }

        var retentionTotal = RoundCurrency(allocatedInvoices.Sum(static item =>
            item.Retentions.Sum(static retention => retention.Value)));
        var grossTotal = RoundCurrency(allocatedInvoices.Sum(static item => item.GrossValue));
        var journalAdjustment = CalculateClientPaymentJournalAdjustment(
            targetValue,
            grossTotal,
            retentionTotal);
        var debitTotal = RoundCurrency(
            targetValue
            + retentionTotal
            + Math.Max(journalAdjustment, 0m));
        var creditTotal = RoundCurrency(
            grossTotal
            + Math.Max(-journalAdjustment, 0m));
        if (Math.Abs(debitTotal - creditTotal) > 0.01m)
            issues.Add("El comprobante no cuadra entre banco, retenciones, ajuste al peso y cartera.");
    }

    internal static decimal CalculateClientPaymentJournalAdjustment(
        decimal movementValue,
        decimal grossValue,
        decimal retentionValue) =>
        RoundCurrency(grossValue - movementValue - retentionValue);

    internal static AllocatedSiigoInvoice? BuildClientPaymentAllocatedInvoice(
        ConciliacionPaymentAllocationRequest allocation,
        ConciliacionSiigoOpenInvoiceSearchResultDto search,
        ICollection<string> issues,
        ConciliacionSiigoSupplierLookupDto? customer = null)
    {
        var invoice = search.Invoices.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(allocation.DocumentId)
                && string.Equals(item.Id, allocation.DocumentId, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(allocation.DocumentName)
                && string.Equals(item.Name, allocation.DocumentName, StringComparison.OrdinalIgnoreCase)));
        if (invoice is null)
        {
            issues.Add($"La factura {FirstNonEmpty(allocation.DocumentName, allocation.DocumentId)} ya no tiene saldo en Siigo.");
            return null;
        }
        if (!Guid.TryParse(invoice.DataverseRecordId, out _))
            issues.Add($"No encontramos la factura {invoice.Name} por numero en Dataverse.");
        if (!string.IsNullOrWhiteSpace(allocation.DataverseRecordId)
            && !string.Equals(allocation.DataverseRecordId, invoice.DataverseRecordId, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"El cruce de {invoice.Name} con Dataverse cambio. Consulta nuevamente las facturas.");
        }

        var paymentValue = RoundCurrency(allocation.AppliedValue);
        if (paymentValue < 0m)
        {
            issues.Add($"El pago aplicado a {invoice.Name} no puede ser negativo.");
            return null;
        }

        var retentions = new List<AllocatedClientRetention>(3);
        AddClientPaymentAllocationRetention(
            retentions,
            issues,
            invoice,
            invoice.TaxBase,
            search.ReteFuenteOptions,
            allocation.ReteFuenteTaxId,
            "ReteFte",
            "ReteFuente");
        AddClientPaymentAllocationRetention(
            retentions,
            issues,
            invoice,
            invoice.TaxBase,
            search.ReteIcaOptions,
            allocation.ReteIcaTaxId,
            "ReteIca",
            "ReteICA");
        AddClientPaymentAllocationRetention(
            retentions,
            issues,
            invoice,
            invoice.Vat,
            search.RteIvaOptions,
            allocation.RteIvaTaxId,
            "RteIva",
            "RteIVA");

        var tenderedValue = RoundCurrency(paymentValue + retentions.Sum(static retention => retention.Value));
        if (tenderedValue <= 0m)
        {
            issues.Add($"Aplica un pago o una retencion a {invoice.Name}.");
            return null;
        }
        if (tenderedValue - invoice.Balance > ClientPaymentInvoiceDifferenceTolerance)
            issues.Add($"Pago y retenciones de {invoice.Name} superan su saldo actual ({invoice.Balance:N2}).");

        var closesInvoice = Math.Abs(invoice.Balance - tenderedValue) <= ClientPaymentInvoiceDifferenceTolerance;
        var grossValue = closesInvoice
            ? RoundCurrency(invoice.Balance)
            : tenderedValue;
        var adjustmentValue = closesInvoice
            ? RoundCurrency(grossValue - tenderedValue)
            : 0m;

        return new AllocatedSiigoInvoice(
            invoice,
            paymentValue,
            grossValue,
            adjustmentValue,
            retentions,
            customer ?? search.Customer);
    }

    private static void AddClientPaymentAllocationRetention(
        ICollection<AllocatedClientRetention> target,
        ICollection<string> issues,
        ConciliacionSiigoOpenInvoiceDto invoice,
        decimal baseValue,
        IReadOnlyList<ConciliacionSiigoRetentionOptionDto> options,
        int requestedTaxId,
        string kind,
        string label)
    {
        if (requestedTaxId <= 0)
            return;

        var option = options.FirstOrDefault(item => item.TaxId == requestedTaxId);
        if (option is null)
        {
            issues.Add($"La tarifa de {label} seleccionada para {invoice.Name} ya no esta activa o no tiene cuenta contable asociada.");
            return;
        }
        if (baseValue <= 0m)
        {
            issues.Add($"La factura {invoice.Name} no tiene base valida para calcular {label}.");
            return;
        }

        var tax = new SiigoTaxLookupDto
        {
            Id = option.TaxId,
            Name = option.Name,
            Type = option.Kind,
            Percentage = option.Rate,
            Active = true
        };
        var accountCode = ConciliacionRetentionMapping.ResolveAccountCode(kind, tax, option.Rate);
        if (string.IsNullOrWhiteSpace(accountCode))
        {
            issues.Add($"La tarifa de {label} seleccionada para {invoice.Name} no tiene cuenta contable asociada.");
            return;
        }

        var divisor = string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase)
            ? 1000m
            : 100m;
        var value = RoundCurrency(baseValue * option.Rate / divisor);
        if (value <= 0m)
        {
            issues.Add($"La tarifa de {label} seleccionada para {invoice.Name} produce un valor en cero.");
            return;
        }

        target.Add(new AllocatedClientRetention(kind, label, option.TaxId, accountCode, option.Rate, value));
    }

    internal static object BuildClientInvoicePaymentJournalPayload(
        ConciliacionCashFlowRowDto row,
        ConciliacionSiigoSupplierLookupDto customer,
        IReadOnlyList<AllocatedSiigoInvoice> allocatedInvoices,
        SiigoDocumentTypeLookupDto document,
        ICollection<string> issues)
    {
        var paymentDate = DateOnly.TryParseExact(
            row.MovementDateValue,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedPaymentDate)
            ? parsedPaymentDate
            : DateOnly.FromDateTime(DateTime.UtcNow);
        var primaryIdentification = ExtractDigits(customer.Identification);
        if (primaryIdentification.Length < 3)
            issues.Add("El cliente seleccionado no tiene identificacion valida.");

        var primaryCustomerParty = new
        {
            identification = primaryIdentification,
            branch_office = customer.BranchOffice
        };
        var items = new List<Dictionary<string, object?>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["account"] = new
                {
                    code = row.BankAccountCode.Trim(),
                    movement = "Debit"
                },
                ["customer"] = primaryCustomerParty,
                ["description"] = TruncateControllerText(FirstNonEmpty(row.Description, row.BankAccountName, "Pago cliente"), 200),
                ["value"] = RoundCurrency(row.EntryValue)
            }
        };

        foreach (var allocation in allocatedInvoices)
        {
            var invoice = allocation.Invoice;
            var allocationCustomer = allocation.Customer ?? customer;
            var allocationIdentification = ExtractDigits(allocationCustomer.Identification);
            if (allocationIdentification.Length < 3)
            {
                issues.Add($"La razon social de {invoice.Name} no tiene identificacion valida en Siigo.");
                continue;
            }
            var allocationCustomerParty = new
            {
                identification = allocationIdentification,
                branch_office = allocationCustomer.BranchOffice
            };
            if (!invoice.HasExactDueReference
                || string.IsNullOrWhiteSpace(invoice.DuePrefix)
                || invoice.DueConsecutive <= 0
                || invoice.DueQuote <= 0
                || !DateOnly.TryParseExact(
                    invoice.DueDateValue,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var dueDate))
            {
                issues.Add(FirstNonEmpty(
                    invoice.DueReferenceIssue,
                    $"No se pudo confirmar el vencimiento existente de {invoice.Name} en Siigo. No se enviara el comprobante para evitar crear un saldo nuevo."));
                continue;
            }

            items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["account"] = new
                {
                    code = "13050501",
                    movement = "Credit"
                },
                ["customer"] = allocationCustomerParty,
                ["description"] = TruncateControllerText($"Clientes nacionales {invoice.Name}", 200),
                ["due"] = new
                {
                    prefix = invoice.DuePrefix.Trim(),
                    consecutive = invoice.DueConsecutive,
                    quote = invoice.DueQuote,
                    date = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                },
                ["value"] = allocation.GrossValue
            });

            foreach (var retention in allocation.Retentions)
            {
                items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account"] = new
                    {
                        code = retention.AccountCode,
                        movement = "Debit"
                    },
                    ["customer"] = allocationCustomerParty,
                    ["tax"] = new
                    {
                        id = retention.TaxId
                    },
                    ["description"] = TruncateControllerText($"{retention.Label} {invoice.Name}", 200),
                    ["value"] = retention.Value
                });
            }
        }

        var adjustmentValue = CalculateClientPaymentJournalAdjustment(
            row.EntryValue,
            allocatedInvoices.Sum(static allocation => allocation.GrossValue),
            allocatedInvoices.Sum(static allocation =>
                allocation.Retentions.Sum(static retention => retention.Value)));
        if (Math.Abs(adjustmentValue) > 0.009m)
        {
            items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["account"] = new
                {
                    code = "42958101",
                    movement = adjustmentValue > 0m ? "Debit" : "Credit"
                },
                ["customer"] = primaryCustomerParty,
                ["description"] = TruncateControllerText("Ajuste al peso pago cliente", 200),
                ["value"] = Math.Abs(adjustmentValue)
            });
        }

        var invoiceNames = string.Join(", ", allocatedInvoices.Select(item =>
            $"{item.Invoice.Name} ({FirstNonEmpty(item.Customer?.DisplayName, item.Customer?.Name, customer.DisplayName, customer.Name)})"));
        return new
        {
            document = new
            {
                id = document.Id
            },
            date = paymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            items,
            observations = TruncateControllerText(
                $"Pago cliente desde Conciliacion. Facturas {invoiceNames}. Flujo caja: {row.SourceFlow} {row.BankAccountName} {row.Description}.",
                500)
        };
    }

    private async Task<IReadOnlyList<string>> VerifyClientInvoicePaymentAppliedAsync(
        IReadOnlyList<AllocatedSiigoInvoice> allocatedInvoices,
        CancellationToken ct)
    {
        IReadOnlyList<string> lastIssues = Array.Empty<string>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var issues = new List<string>();
            foreach (var allocation in allocatedInvoices)
            {
                var invoice = allocation.Invoice;
                if (string.IsNullOrWhiteSpace(invoice.Id) || invoice.SiigoBalance <= 0m)
                {
                    issues.Add($"No fue posible establecer el saldo anterior de {invoice.Name} para confirmar el cruce.");
                    continue;
                }

                var latest = await _siigo.GetInvoiceByIdAsync(invoice.Id, ct);
                if (latest is null)
                {
                    issues.Add($"Siigo no devolvio la factura {invoice.Name} despues de crear el comprobante.");
                    continue;
                }

                var expectedDecrease = RoundCurrency(Math.Min(invoice.SiigoBalance, allocation.GrossValue));
                var actualDecrease = RoundCurrency(invoice.SiigoBalance - latest.Balance);
                if (actualDecrease <= 0.01m)
                {
                    issues.Add($"Siigo creo el comprobante, pero el saldo de {invoice.Name} no disminuyo.");
                    continue;
                }

                if (Math.Abs(actualDecrease - expectedDecrease) > 1m)
                {
                    issues.Add(
                        $"Siigo disminuyo {actualDecrease:N2} de {invoice.Name}, pero se esperaba aplicar {expectedDecrease:N2}.");
                }
            }

            if (issues.Count == 0)
                return Array.Empty<string>();

            lastIssues = issues;
            if (attempt < 2)
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), ct);
        }

        return lastIssues;
    }

    private async Task<ConciliacionSiigoOpenPurchaseSearchResultDto> SearchSupplierOpenPurchasesForPaymentAsync(
        ConciliacionSupplierPaymentPurchaseSearchRequest request,
        CancellationToken ct)
    {
        var row = await _dataverse.GetConciliacionCashFlowMovementAsync(request, ct);
        var supplierQueries = BuildSupplierPaymentSupplierQueries(request, row);
        var supplierQuery = supplierQueries.FirstOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(request.SupplierId) && string.IsNullOrWhiteSpace(supplierQuery))
            throw new InvalidOperationException("La salida no tiene proveedor sugerido. Busca y selecciona un proveedor de Siigo.");

        var supplierId = request.SupplierId?.Trim() ?? "";
        var supplierCandidates = new Dictionary<string, SiigoCustomerLookupItemDto>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(supplierId))
        {
            SiigoCustomerLookupItemDto? resolved = null;
            foreach (var query in supplierQueries)
            {
                var candidates = (await _siigo.SearchCustomersAsync(query, top: 12, ct))
                    .Where(static supplier => supplier.Active)
                    .ToArray();
                foreach (var candidate in candidates)
                {
                    var key = FirstNonEmpty(candidate.Id, $"{candidate.Identification}:{candidate.BranchOffice}", candidate.DisplayName);
                    if (!string.IsNullOrWhiteSpace(key))
                        supplierCandidates.TryAdd(key, candidate);
                }

                resolved = ResolveSupplierPaymentCandidate(candidates, query);
                if (resolved is not null)
                {
                    supplierQuery = query;
                    break;
                }
            }

            if (resolved is null)
            {
                var candidates = supplierCandidates.Values
                    .OrderByDescending(candidate => ScoreSupplierPaymentCandidate(candidate, supplierQuery))
                    .ThenBy(static candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Take(12)
                    .ToArray();
                return new ConciliacionSiigoOpenPurchaseSearchResultDto
                {
                    Message = candidates.Length == 0
                        ? "No encontramos proveedor en Siigo. Usa el buscador escribible para seleccionar un tercero/proveedor."
                        : "Selecciona el proveedor correcto para consultar sus facturas con saldo.",
                    SupplierCandidates = candidates.Select(MapSupplierLookup).ToArray()
                };
            }

            supplierId = resolved.Id;
            supplierQuery = resolved.Identification;
        }

        var (startDate, endDate) = ResolveSupplierPaymentPurchaseWindow(row, request.LookbackMonths);
        var result = await _siigo.GetOpenPurchasesAsync(
            supplierId,
            supplierQuery,
            startDate,
            endDate,
            ct);
        var dataverseDocuments = await _dataverse.GetConciliacionDianSupplierDocumentsForPaymentAsync(
            FirstNonEmpty(result.Supplier?.Identification, supplierQuery),
            startDate,
            endDate,
            ct);
        var taxes = await _siigo.GetTaxesAsync(ct);
        result.Purchases = EnrichSupplierPaymentOpenPurchases(result.Purchases, dataverseDocuments, row);
        result.ReteFuenteOptions = BuildSupplierPaymentRetentionOptions(taxes, "ReteFte");
        result.ReteIcaOptions = BuildSupplierPaymentRetentionOptions(taxes, "ReteIca");

        var matched = result.Purchases.Count(static purchase =>
            string.Equals(purchase.DataverseMatchTone, "success", StringComparison.OrdinalIgnoreCase));
        result.Message = $"{result.Message} Pago banco: {row.ExitValue:N2}. Cruces Dataverse OK: {matched:N0}.";
        return result;
    }

    private async Task<PreparedSupplierPayment> PrepareSupplierPaymentForSiigoAsync(
        ConciliacionSupplierPaymentSendRequest request,
        CancellationToken ct)
    {
        var lookup = new ConciliacionSupplierPaymentPurchaseSearchRequest
        {
            RecordId = request.RecordId,
            MovementExternalKey = request.MovementExternalKey,
            SupplierId = request.SupplierId,
            SupplierQuery = FirstNonEmpty(request.SupplierIdentification, request.SupplierName),
            LookbackMonths = 60
        };
        var row = await _dataverse.GetConciliacionCashFlowMovementAsync(lookup, ct);
        var issues = ValidateSupplierPaymentBase(row, request).ToList();
        var targetEndpoint = "/v1/journals";
        object? payload = null;
        var payloadJson = "";

        var allocatedPurchases = new List<AllocatedSiigoPurchase>();
        ConciliacionSiigoSupplierLookupDto? supplier = null;
        try
        {
            var search = await SearchSupplierOpenPurchasesForPaymentAsync(lookup, ct);
            supplier = search.Supplier;
            var requestedAllocations = request.Allocations
                .Where(static allocation => allocation.AppliedValue > 0m)
                .ToList();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var allocation in requestedAllocations)
            {
                var key = FirstNonEmpty(allocation.DocumentId, allocation.DocumentName);
                if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                {
                    issues.Add("Cada factura debe aparecer una sola vez en la aplicacion.");
                    continue;
                }

                var purchase = search.Purchases.FirstOrDefault(item =>
                    (!string.IsNullOrWhiteSpace(allocation.DocumentId)
                        && string.Equals(item.Id, allocation.DocumentId, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(allocation.DocumentName)
                        && string.Equals(item.Name, allocation.DocumentName, StringComparison.OrdinalIgnoreCase)));
                if (purchase is null)
                {
                    issues.Add($"La factura {FirstNonEmpty(allocation.DocumentName, allocation.DocumentId)} ya no tiene saldo en Siigo.");
                    continue;
                }

                var paymentValue = RoundCurrency(allocation.AppliedValue);
                if (paymentValue <= 0m)
                    issues.Add($"El valor pagado de {purchase.Name} debe ser mayor a cero.");
                var hasDataverseVerificationKey = !string.IsNullOrWhiteSpace(purchase.DataverseCufeCude)
                    || !string.IsNullOrWhiteSpace(purchase.DataverseInvoiceNumber);
                if (!string.Equals(purchase.DataverseMatchTone, "success", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(purchase.DataverseRecordId)
                    || !hasDataverseVerificationKey)
                {
                    issues.Add($"La factura {purchase.Name} no tiene un cruce verificable por CUFE/CUDE o numero de factura en Dataverse.");
                }
                var verificationKeyMatches = !string.IsNullOrWhiteSpace(purchase.DataverseCufeCude)
                    ? string.Equals(
                        NormalizeSupplierPaymentCufeCude(purchase.DataverseCufeCude),
                        NormalizeSupplierPaymentCufeCude(allocation.CufeCude),
                        StringComparison.OrdinalIgnoreCase)
                    : !string.IsNullOrWhiteSpace(purchase.DataverseInvoiceNumber)
                        && string.Equals(
                            NormalizeSupplierPaymentInvoiceKey(purchase.DataverseInvoiceNumber),
                            NormalizeSupplierPaymentInvoiceKey(allocation.DataverseInvoiceNumber),
                            StringComparison.OrdinalIgnoreCase);
                if (!string.Equals(purchase.DataverseRecordId, allocation.DataverseRecordId, StringComparison.OrdinalIgnoreCase)
                    || !verificationKeyMatches)
                {
                    issues.Add($"El cruce DIAN de {purchase.Name} cambio. Vuelve a buscar el proveedor antes de aplicar.");
                }
                if (allocation.CloudValue < 0m || allocation.CopiersValue < 0m)
                    issues.Add($"Cloud y Copiers no pueden ser negativos en {purchase.Name}.");
                if (Math.Abs(RoundCurrency(allocation.CloudValue + allocation.CopiersValue) - paymentValue) > 1m)
                    issues.Add($"Cloud y Copiers deben sumar el valor pagado de {paymentValue:N2} en {purchase.Name}.");
                if (string.IsNullOrWhiteSpace(allocation.CategoryValue))
                    issues.Add($"Selecciona la categoria de {purchase.Name}.");

                var retentions = new List<AllocatedSupplierRetention>(2);
                AddSupplierPaymentAllocationRetention(
                    retentions,
                    issues,
                    purchase,
                    search.ReteFuenteOptions,
                    allocation.ReteFuenteTaxId,
                    "ReteFte",
                    "ReteFuente");
                AddSupplierPaymentAllocationRetention(
                    retentions,
                    issues,
                    purchase,
                    search.ReteIcaOptions,
                    allocation.ReteIcaTaxId,
                    "ReteIca",
                    "ReteICA");
                var grossValue = RoundCurrency(paymentValue + retentions.Sum(static retention => retention.Value));
                if (grossValue - purchase.Balance > 1m)
                    issues.Add($"Pago y retenciones de {purchase.Name} superan su saldo actual ({purchase.Balance:N2}).");

                allocatedPurchases.Add(new AllocatedSiigoPurchase(
                    purchase,
                    allocation,
                    paymentValue,
                    grossValue,
                    retentions));
            }
        }
        catch (InvalidOperationException ex)
        {
            issues.Add(ex.Message);
        }

        if (allocatedPurchases.Count == 0)
            issues.Add("Aplica un valor al menos a una factura con saldo.");
        var targetValue = RoundCurrency(row.ExitValue);
        var appliedTotal = RoundCurrency(allocatedPurchases.Sum(static item => item.PaymentValue));
        if (Math.Abs(targetValue - appliedTotal) > 0.01m)
            issues.Add($"El valor pagado ({appliedTotal:N2}) debe ser igual al movimiento bancario ({targetValue:N2}).");

        try
        {
            var documentTypes = await _siigo.GetDocumentTypesAsync("CC", ct);
            var document = ResolveExpenseJournalDocumentType(documentTypes);
            if (allocatedPurchases.Count > 0)
            {
                payload = BuildSupplierPaymentJournalPayload(row, request, supplier, allocatedPurchases, document, issues);
            }
        }
        catch (InvalidOperationException ex)
        {
            issues.Add(ex.Message);
        }

        if (payload is not null)
            payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        return new PreparedSupplierPayment(
            Row: row,
            Purchases: allocatedPurchases,
            CanSend: issues.Count == 0 && payload is not null,
            TargetEndpoint: targetEndpoint,
            Payload: issues.Count == 0 ? payload : null,
            PayloadJson: payloadJson,
            Issues: issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static IReadOnlyList<string> ValidateSupplierPaymentBase(
        ConciliacionCashFlowRowDto row,
        ConciliacionSupplierPaymentSendRequest request)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(row.RecordId) && string.IsNullOrWhiteSpace(row.ExternalKey))
            issues.Add("Falta identificar la salida del flujo de caja.");
        if (!string.Equals(row.Direction, "Salida", StringComparison.OrdinalIgnoreCase))
            issues.Add("La fila seleccionada no es una salida bancaria.");
        if (row.ExitValue <= 0m)
            issues.Add("La salida no tiene valor de pago.");
        if (string.IsNullOrWhiteSpace(row.BankAccountCode))
            issues.Add("Falta cuenta bancaria contable para acreditar el pago.");
        if (string.IsNullOrWhiteSpace(request.SupplierId) && string.IsNullOrWhiteSpace(request.SupplierIdentification))
            issues.Add("Selecciona el proveedor Siigo antes de enviar.");
        if (request.Allocations.Count != 1)
            issues.Add("Aplica una sola factura Siigo por cada envio.");
        return issues;
    }

    internal static object BuildSupplierPaymentJournalPayload(
        ConciliacionCashFlowRowDto row,
        ConciliacionSupplierPaymentSendRequest request,
        ConciliacionSiigoSupplierLookupDto? supplier,
        IReadOnlyList<AllocatedSiigoPurchase> allocatedPurchases,
        SiigoDocumentTypeLookupDto document,
        List<string> issues)
    {
        var paymentDate = DateOnly.TryParseExact(row.MovementDateValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedPaymentDate)
            ? parsedPaymentDate
            : DateOnly.FromDateTime(DateTime.UtcNow);
        var paymentValue = RoundCurrency(row.ExitValue);
        var reteFuenteValue = RoundCurrency(allocatedPurchases.Sum(static allocation => allocation.Retentions
            .Where(static retention => string.Equals(retention.Kind, "ReteFte", StringComparison.OrdinalIgnoreCase))
            .Sum(static retention => retention.Value)));
        var reteIcaValue = RoundCurrency(allocatedPurchases.Sum(static allocation => allocation.Retentions
            .Where(static retention => string.Equals(retention.Kind, "ReteIca", StringComparison.OrdinalIgnoreCase))
            .Sum(static retention => retention.Value)));
        var grossPayment = RoundCurrency(allocatedPurchases.Sum(static allocation => allocation.GrossValue));
        if (grossPayment <= 0m)
            issues.Add("El valor a aplicar debe ser mayor a cero.");

        var firstPurchase = allocatedPurchases[0].Purchase;
        var identification = ExtractDigits(FirstNonEmpty(
            supplier?.Identification,
            firstPurchase.SupplierIdentification));
        if (identification.Length < 3)
            issues.Add("El proveedor seleccionado no tiene identificacion valida.");
        if (supplier is null || string.IsNullOrWhiteSpace(supplier.Id))
        {
            issues.Add("No fue posible confirmar el proveedor real contra Siigo.");
        }
        else if (!string.IsNullOrWhiteSpace(request.SupplierId)
            && !string.Equals(request.SupplierId.Trim(), supplier.Id.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("El ID del proveedor solicitado no coincide con el proveedor resuelto en Siigo.");
        }

        var requestedIdentification = ExtractDigits(request.SupplierIdentification);
        if (requestedIdentification.Length >= 3
            && identification.Length >= 3
            && !IsSameSupplierIdentificationDigits(requestedIdentification, identification))
        {
            issues.Add("El NIT solicitado no coincide con el proveedor resuelto en Siigo.");
        }

        var purchaseIdentification = ExtractDigits(firstPurchase.SupplierIdentification);
        if (purchaseIdentification.Length >= 3
            && identification.Length >= 3
            && !IsSameSupplierIdentificationDigits(purchaseIdentification, identification))
        {
            issues.Add("La factura seleccionada pertenece a un proveedor diferente del tercero resuelto en Siigo.");
        }

        var supplierParty = new
        {
            identification,
            branch_office = supplier?.BranchOffice ?? firstPurchase.SupplierBranchOffice
        };
        var items = new List<Dictionary<string, object?>>();
        foreach (var allocation in allocatedPurchases)
        {
            var purchase = allocation.Purchase;
            if (!TryParseSiigoDueLabel(purchase.Name, out var duePrefix, out var dueConsecutive))
                issues.Add($"No se pudo separar prefijo y consecutivo del documento Siigo {purchase.Name}.");

            items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["account"] = new
                {
                    code = "22050501",
                    movement = "Debit"
                },
                ["customer"] = supplierParty,
                ["description"] = TruncateControllerText($"Proveedor {FirstNonEmpty(purchase.ProviderInvoiceFullNumber, purchase.Name)}", 200),
                ["due"] = new
                {
                    prefix = duePrefix,
                    consecutive = dueConsecutive,
                    quote = 1,
                    date = paymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                },
                ["value"] = RoundCurrency(allocation.GrossValue)
            });
        }

        var purchaseLabel = string.Join(", ", allocatedPurchases.Select(static allocation =>
            FirstNonEmpty(allocation.Purchase.ProviderInvoiceFullNumber, allocation.Purchase.Name)));

        foreach (var retention in allocatedPurchases.SelectMany(static allocation => allocation.Retentions))
            AddSupplierPaymentRetentionItem(items, retention, purchaseLabel, supplierParty);

        items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["account"] = new
            {
                code = row.BankAccountCode.Trim(),
                movement = "Credit"
            },
            ["customer"] = supplierParty,
            ["description"] = TruncateControllerText(FirstNonEmpty(row.BankAccountName, $"Pago banco {purchaseLabel}"), 200),
            ["value"] = paymentValue
        });

        return new
        {
            document = new
            {
                id = document.Id
            },
            date = paymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            items,
            observations = TruncateControllerText(
                $"Pago proveedor desde Conciliacion. Facturas {purchaseLabel}. " +
                $"Aplicado {grossPayment:N2}; pago banco {paymentValue:N2}; retefuente {reteFuenteValue:N2}; reteica {reteIcaValue:N2}. " +
                $"Flujo caja: {row.SourceFlow} {row.BankAccountName} {row.Description}.",
                500)
        };
    }

    private static void AddSupplierPaymentRetentionItem(
        ICollection<Dictionary<string, object?>> items,
        AllocatedSupplierRetention retention,
        string purchaseLabel,
        object supplierParty)
    {
        if (retention.Value <= 0m)
            return;

        items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["account"] = new
            {
                code = retention.AccountCode,
                movement = "Credit"
            },
            ["tax"] = new
            {
                id = retention.TaxId
            },
            ["customer"] = supplierParty,
            ["description"] = TruncateControllerText($"{retention.Label} {purchaseLabel}", 200),
            ["value"] = retention.Value
        });
    }

    private static IReadOnlyList<ConciliacionSiigoRetentionOptionDto> BuildSupplierPaymentRetentionOptions(
        IReadOnlyList<SiigoTaxLookupDto> taxes,
        string kind)
    {
        return taxes
            .Where(tax => tax.Active
                && tax.Id > 0
                && tax.Percentage > 0m
                && ConciliacionRetentionMapping.MatchesKind(tax, kind)
                && !string.IsNullOrWhiteSpace(ResolveSupplierPaymentRetentionAccountCode(kind, tax, tax.Percentage)))
            .GroupBy(tax => Math.Round(tax.Percentage, 4, MidpointRounding.AwayFromZero))
            .Select(group => group
                .OrderBy(static tax => tax.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static tax => tax.Id)
                .First())
            .OrderBy(static tax => tax.Percentage)
            .Select(tax => new ConciliacionSiigoRetentionOptionDto
            {
                TaxId = tax.Id,
                Kind = kind,
                Name = tax.Name,
                Rate = Math.Round(tax.Percentage, 4, MidpointRounding.AwayFromZero),
                RateLabel = string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase)
                    ? $"{FormatClientPaymentRate(tax.Percentage)} x mil"
                    : $"{FormatClientPaymentRate(tax.Percentage)}%"
            })
            .ToArray();
    }

    private static void AddSupplierPaymentAllocationRetention(
        ICollection<AllocatedSupplierRetention> target,
        ICollection<string> issues,
        ConciliacionSiigoOpenPurchaseDto purchase,
        IReadOnlyList<ConciliacionSiigoRetentionOptionDto> options,
        int requestedTaxId,
        string kind,
        string label)
    {
        if (requestedTaxId <= 0)
            return;

        var option = options.FirstOrDefault(item => item.TaxId == requestedTaxId);
        if (option is null)
        {
            issues.Add($"La tarifa de {label} seleccionada para {purchase.Name} ya no esta activa o no tiene cuenta contable asociada.");
            return;
        }
        if (purchase.DataverseBaseAmount <= 0m)
        {
            issues.Add($"La factura {purchase.Name} no tiene base DIAN para calcular {label}.");
            return;
        }

        var tax = new SiigoTaxLookupDto
        {
            Id = option.TaxId,
            Name = option.Name,
            Type = option.Kind,
            Percentage = option.Rate,
            Active = true
        };
        var accountCode = ResolveSupplierPaymentRetentionAccountCode(kind, tax, option.Rate);
        if (string.IsNullOrWhiteSpace(accountCode))
        {
            issues.Add($"La tarifa de {label} seleccionada para {purchase.Name} no tiene cuenta contable asociada.");
            return;
        }

        var divisor = string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase)
            ? 1000m
            : 100m;
        var value = RoundCurrency(purchase.DataverseBaseAmount * option.Rate / divisor);
        if (value <= 0m)
        {
            issues.Add($"La tarifa de {label} seleccionada para {purchase.Name} produce un valor en cero.");
            return;
        }

        target.Add(new AllocatedSupplierRetention(kind, label, option.TaxId, accountCode, option.Rate, value));
    }

    private static string ResolveSupplierPaymentRetentionAccountCode(
        string kind,
        SiigoTaxLookupDto tax,
        decimal requestedRate)
    {
        if (string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase))
        {
            return tax.Id switch
            {
                4028 => "23680501",
                4030 => "23680501",
                4033 => "23680501",
                4034 => "23680501",
                _ => ""
            };
        }

        var rate = requestedRate > 0m ? requestedRate : tax.Percentage;
        return tax.Id switch
        {
            4027 => "23654001",
            4038 => "23651505",
            4026 => "23652503",
            4039 => "23651503",
            4023 => "23651515",
            _ when Math.Abs(rate - 2.5m) <= 0.1m => "23654001",
            _ when Math.Abs(rate - 3.5m) <= 0.1m => "23651505",
            _ when Math.Abs(rate - 4m) <= 0.1m => "23652503",
            _ when Math.Abs(rate - 7m) <= 0.1m => "23651503",
            _ when Math.Abs(rate - 11m) <= 0.1m => "23651515",
            _ => ""
        };
    }

    private static (DateOnly StartDate, DateOnly EndDate) ResolveSupplierPaymentPurchaseWindow(
        ConciliacionCashFlowRowDto row,
        int lookbackMonths)
    {
        var movementDate = DateOnly.TryParseExact(row.MovementDateValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
            ? parsedDate
            : DateOnly.FromDateTime(DateTime.UtcNow);
        var months = Math.Clamp(lookbackMonths <= 0 ? 60 : lookbackMonths, 1, 120);
        var endDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1);
        if (endDate < movementDate)
            endDate = movementDate.AddMonths(1);

        return (movementDate.AddMonths(-months), endDate);
    }

    private static IReadOnlyList<string> BuildSupplierPaymentSupplierQueries(
        ConciliacionSupplierPaymentPurchaseSearchRequest request,
        ConciliacionCashFlowRowDto row)
    {
        var values = new[]
            {
                request.SupplierQuery,
                row.Recipient,
                row.Description,
                row.Observations,
                row.DocumentType
            }
            .Select(SanitizeSupplierPaymentQuery)
            .Where(static value => value.Length >= 3);

        return values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private static string SanitizeSupplierPaymentQuery(string? value)
    {
        var normalized = NormalizeSupplierPaymentMatchText(value);
        normalized = Regex.Replace(normalized, @"\b(?:PAGO|FACTURA|FC|FE|FCE|FVE|DOCUMENTO|COMPRA|PROVEEDOR|RETENCION|RETEFUENTE|RETEICA|TRANSFERENCIA)\b", " ", RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b", " ", RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"\b\d{5,}\b", " ", RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        return normalized.Length >= 3 ? normalized : NormalizeSupplierPaymentMatchText(value);
    }

    private static IReadOnlyList<ConciliacionSiigoOpenPurchaseDto> EnrichSupplierPaymentOpenPurchases(
        IReadOnlyList<ConciliacionSiigoOpenPurchaseDto> purchases,
        IReadOnlyList<ConciliacionDianSupplierInvoiceRowDto> dataverseDocuments,
        ConciliacionCashFlowRowDto row)
    {
        foreach (var purchase in purchases)
        {
            var candidates = dataverseDocuments
                .Select(document => new
                {
                    Document = document,
                    Priority = GetSupplierPaymentDataverseMatchPriority(purchase, document),
                    Score = ScoreSupplierPaymentDataverseMatch(purchase, document, row)
                })
                .Where(static item => item.Priority > 0)
                .ToArray();
            var bestPriority = candidates.Length == 0 ? 0 : candidates.Max(static item => item.Priority);
            var finalists = candidates
                .Where(item => item.Priority == bestPriority)
                .ToArray();
            var distinctCufes = finalists
                .Select(item => NormalizeSupplierPaymentCufeCude(item.Document.Cufe))
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (finalists.Length > 1 && distinctCufes.Length != 1)
            {
                purchase.MatchScore = finalists.Max(static item => item.Score);
                purchase.DataverseMatchLabel = "Cruce DIAN ambiguo";
                purchase.DataverseMatchTone = "warning";
                continue;
            }

            var match = finalists
                .OrderByDescending(static item => !string.IsNullOrWhiteSpace(item.Document.Cufe))
                .ThenByDescending(static item => item.Score)
                .ThenBy(static item => item.Document.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            purchase.MatchScore = Math.Max(purchase.MatchScore, match?.Score ?? ScoreSupplierPaymentPurchaseAgainstCashFlow(purchase, row));
            if (match is null)
            {
                purchase.DataverseMatchLabel = "Sin cruce Dataverse";
                purchase.DataverseMatchTone = "neutral";
                continue;
            }

            var dataverseInvoiceNumber = FirstNonEmpty(
                match.Document.InvoiceNumber,
                $"{match.Document.Prefix}{match.Document.Folio}",
                match.Document.Folio);
            purchase.DataverseRecordId = match.Document.RecordId;
            purchase.DataverseInvoiceNumber = dataverseInvoiceNumber;
            purchase.DataverseSupplierName = match.Document.SupplierName;
            purchase.DataverseSupplierNit = match.Document.SupplierNit;
            purchase.DataverseTotal = RoundCurrency(match.Document.TotalValue);
            purchase.DataversePaymentValue = RoundCurrency(match.Document.PaymentValue);
            purchase.DataverseReteFuenteValue = RoundCurrency(match.Document.ReteFuenteValue);
            purchase.DataverseReteIcaValue = RoundCurrency(match.Document.ReteIcaValue);
            purchase.DataverseCufeCude = match.Document.Cufe;
            purchase.DataverseBaseAmount = RoundCurrency(match.Document.BaseAmount);
            purchase.DataverseCloudValue = RoundCurrency(match.Document.CloudValue);
            purchase.DataverseCopiersValue = RoundCurrency(match.Document.CopiersValue);
            purchase.DataverseCategoryValue = match.Document.CategoryValue;
            purchase.DataverseCategoryLabel = match.Document.CategoryLabel;
            var hasCufeCude = !string.IsNullOrWhiteSpace(match.Document.Cufe);
            var hasInvoiceNumber = !string.IsNullOrWhiteSpace(dataverseInvoiceNumber);
            purchase.DataverseMatchLabel = hasCufeCude
                ? $"Dataverse verificada: {dataverseInvoiceNumber}"
                : hasInvoiceNumber
                    ? $"Dataverse verificada por factura: {dataverseInvoiceNumber}"
                    : "Dataverse sin CUFE/CUDE ni numero de factura";
            purchase.DataverseMatchTone = hasCufeCude || hasInvoiceNumber ? "success" : "warning";
        }

        return purchases
            .OrderByDescending(static purchase => string.Equals(purchase.DataverseMatchTone, "success", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(static purchase => purchase.MatchScore)
            .ThenBy(purchase => SupplierPaymentAmountDelta(purchase, row))
            .ThenBy(static purchase => purchase.DateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static purchase => purchase.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int GetSupplierPaymentDataverseMatchPriority(
        ConciliacionSiigoOpenPurchaseDto purchase,
        ConciliacionDianSupplierInvoiceRowDto document)
    {
        if (!string.IsNullOrWhiteSpace(document.SiigoDocumentId)
            && string.Equals(document.SiigoDocumentId, purchase.Id, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (!string.IsNullOrWhiteSpace(document.SiigoDocumentName)
            && string.Equals(
                NormalizeSupplierPaymentInvoiceKey(document.SiigoDocumentName),
                NormalizeSupplierPaymentInvoiceKey(purchase.Name),
                StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        var sameSupplier = IsSameSupplierIdentificationDigits(
            ExtractDigits(purchase.SupplierIdentification),
            ExtractDigits(document.SupplierNit));
        var sameInvoice = SupplierPaymentInvoiceKeysMatch(
            BuildSupplierPaymentInvoiceKeys(
                purchase.ProviderInvoiceFullNumber,
                purchase.ProviderInvoicePrefix + purchase.ProviderInvoiceNumber,
                purchase.ProviderInvoiceNumber,
                purchase.Name),
            BuildSupplierPaymentInvoiceKeys(
                document.InvoiceNumber,
                document.Prefix + document.Folio,
                document.Folio,
                document.SiigoDocumentName));
        return sameSupplier && sameInvoice ? 1 : 0;
    }

    private static int ScoreSupplierPaymentDataverseMatch(
        ConciliacionSiigoOpenPurchaseDto purchase,
        ConciliacionDianSupplierInvoiceRowDto document,
        ConciliacionCashFlowRowDto row)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(document.SiigoDocumentId)
            && string.Equals(document.SiigoDocumentId, purchase.Id, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (!string.IsNullOrWhiteSpace(document.SiigoDocumentName)
            && string.Equals(NormalizeSupplierPaymentInvoiceKey(document.SiigoDocumentName), NormalizeSupplierPaymentInvoiceKey(purchase.Name), StringComparison.OrdinalIgnoreCase))
        {
            score += 90;
        }

        if (SupplierPaymentInvoiceKeysMatch(
            BuildSupplierPaymentInvoiceKeys(purchase.ProviderInvoiceFullNumber, purchase.ProviderInvoicePrefix + purchase.ProviderInvoiceNumber, purchase.ProviderInvoiceNumber, purchase.Name),
            BuildSupplierPaymentInvoiceKeys(document.InvoiceNumber, document.Prefix + document.Folio, document.Folio, document.SiigoDocumentName)))
        {
            score += 80;
        }

        if (IsSameSupplierIdentificationDigits(ExtractDigits(purchase.SupplierIdentification), ExtractDigits(document.SupplierNit)))
            score += 35;

        if (SupplierPaymentTextLooksRelated(document.SupplierName, row)
            || SupplierPaymentTextContainsTokens(FirstNonEmpty(row.Recipient, row.Description, row.Observations), document.SupplierName))
        {
            score += 25;
        }

        if (SupplierPaymentAmountLooksClose(row.ExitValue, document.PaymentValue > 0m ? document.PaymentValue : document.TotalValue))
            score += 35;

        if (SupplierPaymentAmountLooksClose(purchase.Balance, document.TotalValue)
            || SupplierPaymentAmountLooksClose(purchase.Balance, document.PaymentValue))
        {
            score += 20;
        }

        return score;
    }

    private static int ScoreSupplierPaymentPurchaseAgainstCashFlow(
        ConciliacionSiigoOpenPurchaseDto purchase,
        ConciliacionCashFlowRowDto row)
    {
        var score = 0;
        var rowKeys = BuildSupplierPaymentInvoiceKeys(row.Description, row.Recipient, row.Observations, row.DocumentType);
        var purchaseKeys = BuildSupplierPaymentInvoiceKeys(purchase.ProviderInvoiceFullNumber, purchase.ProviderInvoiceNumber, purchase.Name);
        if (SupplierPaymentInvoiceKeysMatch(rowKeys, purchaseKeys))
            score += 60;
        if (SupplierPaymentAmountLooksClose(row.ExitValue, purchase.Balance))
            score += 35;
        if (SupplierPaymentTextContainsTokens(FirstNonEmpty(row.Recipient, row.Description, row.Observations), purchase.SupplierIdentification))
            score += 10;

        return score;
    }

    private static decimal SupplierPaymentAmountDelta(
        ConciliacionSiigoOpenPurchaseDto purchase,
        ConciliacionCashFlowRowDto row)
    {
        var candidates = new[]
        {
            Math.Abs(RoundCurrency(purchase.Balance) - RoundCurrency(row.ExitValue)),
            purchase.DataversePaymentValue > 0m ? Math.Abs(RoundCurrency(purchase.DataversePaymentValue) - RoundCurrency(row.ExitValue)) : decimal.MaxValue,
            purchase.DataverseTotal > 0m ? Math.Abs(RoundCurrency(purchase.DataverseTotal) - RoundCurrency(purchase.Balance)) : decimal.MaxValue
        };

        return candidates.Min();
    }

    private static bool SupplierPaymentAmountLooksClose(decimal left, decimal right)
    {
        left = RoundCurrency(left);
        right = RoundCurrency(right);
        if (left <= 0m || right <= 0m)
            return false;

        var delta = Math.Abs(left - right);
        var tolerance = Math.Max(1000m, Math.Min(left, right) * 0.015m);
        return delta <= tolerance;
    }

    private static bool SupplierPaymentTextLooksRelated(string value, ConciliacionCashFlowRowDto row)
    {
        return SupplierPaymentTextContainsTokens(row.Recipient, value)
            || SupplierPaymentTextContainsTokens(row.Description, value)
            || SupplierPaymentTextContainsTokens(row.Observations, value);
    }

    private static bool SupplierPaymentTextContainsTokens(string haystack, string needles)
    {
        var normalizedHaystack = NormalizeSupplierPaymentMatchText(haystack);
        var tokens = NormalizeSupplierPaymentMatchText(needles)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static token => token.Length >= 4)
            .Where(static token => !SupplierPaymentStopWords.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();

        return tokens.Length > 0
            && tokens.Count(token => normalizedHaystack.Contains(token, StringComparison.OrdinalIgnoreCase)) >= Math.Min(2, tokens.Length);
    }

    private static bool SupplierPaymentInvoiceKeysMatch(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        return left.Count > 0
            && right.Count > 0
            && left.Any(key => right.Contains(key));
    }

    private static IReadOnlySet<string> BuildSupplierPaymentInvoiceKeys(params string?[] values)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var key = NormalizeSupplierPaymentInvoiceKey(value);
            if (key.Length >= 3)
                keys.Add(key);

            var digits = ExtractDigits(value ?? "");
            if (digits.Length >= 4)
                keys.Add(digits);
        }

        return keys;
    }

    private static string NormalizeSupplierPaymentInvoiceKey(string? value)
    {
        var normalized = NormalizeSupplierPaymentMatchText(value);
        return Regex.Replace(normalized, @"[^A-Z0-9]+", "", RegexOptions.CultureInvariant);
    }

    private static string NormalizeSupplierPaymentCufeCude(string? value) =>
        Regex.Replace((value ?? "").Trim().ToUpperInvariant(), @"[^A-Z0-9]+", "", RegexOptions.CultureInvariant);

    private static int ScoreSupplierPaymentCandidate(SiigoCustomerLookupItemDto supplier, string query)
    {
        var normalizedQuery = NormalizeSupplierPaymentMatchText(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return 0;

        var queryDigits = ExtractDigits(query);
        var supplierDigits = ExtractDigits(supplier.Identification);
        if (queryDigits.Length >= 5 && IsSameSupplierIdentificationDigits(supplierDigits, queryDigits))
            return 100;

        var names = new[]
        {
            NormalizeSupplierPaymentMatchText(supplier.DisplayName),
            NormalizeSupplierPaymentMatchText(supplier.Name),
            NormalizeSupplierPaymentMatchText(supplier.CommercialName)
        };

        if (names.Any(name => string.Equals(name, normalizedQuery, StringComparison.OrdinalIgnoreCase)))
            return 95;
        if (names.Any(name => name.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase)))
            return 85;
        if (names.Any(name => name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)))
            return 70;

        var queryTokens = normalizedQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static token => token.Length >= 4)
            .Where(static token => !SupplierPaymentStopWords.Contains(token))
            .ToArray();
        if (queryTokens.Length == 0)
            return 0;

        var bestTokenCount = names
            .Select(name => queryTokens.Count(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .DefaultIfEmpty(0)
            .Max();

        return bestTokenCount >= Math.Min(2, queryTokens.Length) ? 60 + bestTokenCount : 0;
    }

    private static string NormalizeSupplierPaymentMatchText(string? value)
    {
        var text = (value ?? "").Trim().ToUpperInvariant();
        text = text
            .Replace("Á", "A", StringComparison.Ordinal)
            .Replace("É", "E", StringComparison.Ordinal)
            .Replace("Í", "I", StringComparison.Ordinal)
            .Replace("Ó", "O", StringComparison.Ordinal)
            .Replace("Ú", "U", StringComparison.Ordinal)
            .Replace("Ü", "U", StringComparison.Ordinal)
            .Replace("Ñ", "N", StringComparison.Ordinal);
        return Regex.Replace(text, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
    }

    private static SiigoCustomerLookupItemDto? ResolveSupplierPaymentCandidate(
        IReadOnlyList<SiigoCustomerLookupItemDto> candidates,
        string query)
    {
        var activeCandidates = candidates.Where(static supplier => supplier.Active).ToArray();
        var queryDigits = ExtractDigits(query);
        if (queryDigits.Length >= 5)
        {
            var exact = activeCandidates.FirstOrDefault(candidate =>
                IsSameSupplierIdentificationDigits(ExtractDigits(candidate.Identification), queryDigits));
            if (exact is not null)
                return exact;
        }

        var scored = activeCandidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = ScoreSupplierPaymentCandidate(candidate, query)
            })
            .Where(static item => item.Score >= 70)
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.Candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (scored.Length == 1 || (scored.Length > 1 && scored[0].Score >= scored[1].Score + 15))
            return scored[0].Candidate;

        return activeCandidates.Length == 1 ? activeCandidates[0] : null;
    }

    private static bool IsSameSupplierIdentificationDigits(string leftDigits, string rightDigits)
    {
        if (string.IsNullOrWhiteSpace(leftDigits) || string.IsNullOrWhiteSpace(rightDigits))
            return false;
        if (string.Equals(leftDigits, rightDigits, StringComparison.OrdinalIgnoreCase))
            return true;

        return (leftDigits.Length >= 9 && leftDigits.Length == rightDigits.Length + 1 && leftDigits.StartsWith(rightDigits, StringComparison.OrdinalIgnoreCase))
            || (rightDigits.Length >= 9 && rightDigits.Length == leftDigits.Length + 1 && rightDigits.StartsWith(leftDigits, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAllowedDianSupplierIdentificationEdit(
        string dianIdentification,
        string requestedIdentification,
        string personType,
        string? requestedCheckDigit)
    {
        var dianDigits = ExtractDigits(dianIdentification);
        var requestedDigits = ExtractDigits(requestedIdentification);
        if (!personType.Equals("Company", StringComparison.OrdinalIgnoreCase))
            return string.Equals(dianDigits, requestedDigits, StringComparison.Ordinal);

        if (!TryCanonicalColombianNit(dianDigits, out var dianBaseNit)
            || !TryCanonicalColombianNit(requestedDigits, out var requestedBaseNit)
            || !string.Equals(dianBaseNit, requestedBaseNit, StringComparison.Ordinal))
        {
            return false;
        }

        var checkDigit = ExtractDigits(requestedCheckDigit ?? "");
        return checkDigit.Length == 0
            || checkDigit[^1] - '0' == CalculateColombianCheckDigit(requestedBaseNit);
    }

    private static bool TryCanonicalColombianNit(string digits, out string baseNit)
    {
        baseNit = "";
        if (digits.Length == 9)
        {
            baseNit = digits;
            return true;
        }

        if (digits.Length == 10
            && digits[^1] - '0' == CalculateColombianCheckDigit(digits[..^1]))
        {
            baseNit = digits[..^1];
            return true;
        }

        return false;
    }

    private static ConciliacionSiigoSupplierLookupDto MapSupplierLookup(SiigoCustomerLookupItemDto supplier) =>
        new()
        {
            Id = supplier.Id,
            DisplayName = supplier.DisplayName,
            Name = supplier.Name,
            CommercialName = supplier.CommercialName,
            Identification = supplier.Identification,
            Type = supplier.Type,
            BranchOffice = supplier.BranchOffice,
            Active = supplier.Active
        };

    private async Task<PreparedDianSupplierPurchase> PrepareDianSupplierPurchaseForSiigoAsync(
        string recordId,
        bool createMissingSupplier,
        CancellationToken ct)
    {
        var row = await _dataverse.GetConciliacionDianSupplierDocumentAsync(recordId, ct);
        var issues = ValidateDianSupplierPurchaseBase(row).ToList();
        var targetEndpoint = ResolveDianSupplierDocumentEndpoint(row);
        object? supplierPayload = null;

        SiigoCustomerLookupItemDto? supplier = null;
        if (issues.Count == 0 || issues.All(static issue => !issue.Contains("proveedor", StringComparison.OrdinalIgnoreCase)))
        {
            var supplierResult = await EnsureDianSupplierInSiigoAsync(row, createMissingSupplier, ct);
            supplier = supplierResult.Customer;
            if (supplierResult.Created || supplierResult.WouldCreate)
                supplierPayload = supplierResult.Payload;
            if (!supplierResult.ExistsInSiigo && !createMissingSupplier)
                issues.Add("El proveedor no existe aun en Siigo; debes crearlo desde la bandeja de proveedores pendientes.");

            if (createMissingSupplier && supplierResult.Created)
            {
                var supplierLabel = FirstNonEmpty(supplier.DisplayName, supplier.Name, supplier.Identification);
                await _dataverse.MarkConciliacionDianSupplierAsync(
                    row.RecordId,
                    supplier.Id,
                    supplierLabel,
                    $"Proveedor creado automaticamente antes de crear la factura: {supplierLabel}.",
                    ct);
                row = await _dataverse.GetConciliacionDianSupplierDocumentAsync(recordId, ct);
            }
        }

        var documentTypes = await _siigo.GetDocumentTypesAsync("FC", ct);
        var paymentTypes = await _siigo.GetPaymentTypesAsync("FC", ct);
        var taxes = await _siigo.GetTaxesAsync(ct);
        var purchaseDocument = ResolvePurchaseDocumentType(documentTypes);
        var paymentType = ResolveSupplierPurchasePaymentType(paymentTypes);
        var payloadIssues = new List<string>();
        var purchasePayload = BuildDianSupplierPurchasePayload(row, purchaseDocument, paymentType, taxes, payloadIssues);
        issues.AddRange(payloadIssues);

        var wrapperPayload = supplierPayload is null
            ? purchasePayload
            : new { supplier = supplierPayload, purchase = purchasePayload };
        var payloadJson = JsonSerializer.Serialize(wrapperPayload, new JsonSerializerOptions { WriteIndented = true });

        return new PreparedDianSupplierPurchase(
            Row: row,
            CanSend: issues.Count == 0,
            TargetEndpoint: targetEndpoint,
            Payload: issues.Count == 0 ? purchasePayload : null,
            PayloadJson: payloadJson,
            Issues: issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private async Task<PreparedCuentaCobroSupportDocument> PrepareCuentaCobroSupportDocumentForSiigoAsync(
        ConciliacionCuentaCobroDocumentRequest request,
        bool createMissingSupplier,
        CancellationToken ct)
    {
        var row = await _dataverse.GetConciliacionCuentaCobroDocumentAsync(request, ct);
        var issues = ValidateCuentaCobroSupportDocumentBase(row).ToList();
        var targetEndpoint = "/v1/purchase-support-documents";
        object? supplierPayload = null;

        if (issues.Count == 0 || issues.All(static issue => !issue.Contains("proveedor", StringComparison.OrdinalIgnoreCase)))
        {
            var supplierRow = BuildDianSupplierRowFromCuentaCobro(row);
            var supplierResult = await EnsureDianSupplierInSiigoAsync(supplierRow, createMissingSupplier, ct);
            if (supplierResult.Created || supplierResult.WouldCreate)
                supplierPayload = supplierResult.Payload;
            if (!supplierResult.ExistsInSiigo && !createMissingSupplier)
                issues.Add("El proveedor/persona no existe aun en Siigo. Crealo o activalo en Siigo y vuelve a validar; este flujo no lo creara automaticamente para evitar duplicados ambiguos.");
        }

        var payloadIssues = new List<string>();
        object? payload = null;
        var payloadJson = "";
        try
        {
            var documentTypes = await _siigo.GetDocumentTypesAsync("DS", ct);
            var paymentTypes = await GetSupportDocumentPaymentTypesAsync(ct);
            var document = ResolveSupportDocumentType(documentTypes);
            var paymentType = ResolveSupportDocumentPaymentType(paymentTypes);
            payload = BuildCuentaCobroSupportDocumentPayload(row, document, paymentType, payloadIssues);
        }
        catch (InvalidOperationException ex)
        {
            payloadIssues.Add(ex.Message);
        }

        issues.AddRange(payloadIssues);
        var wrapperPayload = supplierPayload is null
            ? payload
            : new { supplier = supplierPayload, supportDocument = payload };
        if (wrapperPayload is not null)
            payloadJson = JsonSerializer.Serialize(wrapperPayload, new JsonSerializerOptions { WriteIndented = true });

        return new PreparedCuentaCobroSupportDocument(
            Row: row,
            CanSend: issues.Count == 0 && payload is not null,
            TargetEndpoint: targetEndpoint,
            Payload: issues.Count == 0 ? payload : null,
            PayloadJson: payloadJson,
            Issues: issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private async Task<IReadOnlyList<SiigoPaymentTypeLookupDto>> GetSupportDocumentPaymentTypesAsync(CancellationToken ct)
    {
        var supportTypes = await _siigo.GetPaymentTypesAsync("DS", ct);
        if (supportTypes.Count == 0)
        {
            throw new InvalidOperationException(
                "Siigo no devolvio formas de pago para Documento soporte (DS). "
                + "No se creara el documento hasta configurar un credito de proveedores DS.");
        }

        return supportTypes;
    }

    private static IReadOnlyList<string> ValidateCuentaCobroSupportDocumentBase(ConciliacionCuentaCobroRowDto row)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(row.RecordId))
            issues.Add("Falta asociar la salida bancaria con una cuenta de cobro de la app.");
        if (string.IsNullOrWhiteSpace(row.CashFlowRecordId) && string.IsNullOrWhiteSpace(row.CashFlowExternalKey))
            issues.Add("Falta la salida bancaria del flujo de caja.");
        if (string.IsNullOrWhiteSpace(row.Receptor) || string.IsNullOrWhiteSpace(row.NitOCedula))
            issues.Add("Falta nombre o NIT/cedula del proveedor/persona.");
        DateOnly? emissionDate = null;
        DateOnly? paymentDate = null;
        if (string.IsNullOrWhiteSpace(row.FechaEmisionValue))
            issues.Add("Falta fecha de emision de la cuenta de cobro.");
        else if (!TryParseSiigoDate(row.FechaEmisionValue, out var parsedEmissionDate))
            issues.Add("La fecha de emision de la cuenta de cobro no tiene formato valido para Siigo (yyyy-MM-dd).");
        else
            emissionDate = parsedEmissionDate;
        if (!string.IsNullOrWhiteSpace(row.FechaPagoValue))
        {
            if (!TryParseSiigoDate(row.FechaPagoValue, out var parsedPaymentDate))
                issues.Add("La fecha de pago de la cuenta de cobro no tiene formato valido para Siigo (yyyy-MM-dd).");
            else
                paymentDate = parsedPaymentDate;
        }
        if (emissionDate.HasValue && paymentDate.HasValue && emissionDate.Value > paymentDate.Value)
            issues.Add("La fecha de emision de la cuenta de cobro no puede ser posterior a la fecha de pago.");
        if (row.ValorTotal <= 0m)
            issues.Add("El valor total debe ser mayor a cero.");
        if (row.ValorPago <= 0m)
            issues.Add("El valor pago debe ser mayor a cero.");
        if (!row.TotalesCuadran)
            issues.Add("El valor total debe ser igual a valor pago mas todas las retenciones.");
        if (Math.Abs(row.DifferenceValue) > 1m)
            issues.Add($"La salida del flujo de caja no coincide con el valor pago. Diferencia: {row.DifferenceValue:N2}.");
        if (string.IsNullOrWhiteSpace(row.AccountCode))
            issues.Add("Falta cuenta contable de gasto para el item del documento soporte.");
        if (string.Equals(row.RecordSource, "cuenta-cobro", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(
                "El registro historico debe migrarse primero a cr07a_gastodelaempresa; "
                + "no se enviara un documento soporte desde la tabla heredada.");
        }
        if (!string.IsNullOrWhiteSpace(row.SiigoDocumentId) || !string.IsNullOrWhiteSpace(row.SiigoDocumentName))
            issues.Add("Esta cuenta de cobro ya tiene documento soporte Siigo asociado; usa el reintento de pago.");

        return issues;
    }

    private static ConciliacionDianSupplierInvoiceRowDto BuildDianSupplierRowFromCuentaCobro(ConciliacionCuentaCobroRowDto row)
    {
        var folio = BuildCuentaCobroSupplierReceiptNumber(row);
        return new ConciliacionDianSupplierInvoiceRowDto
        {
            RecordId = row.RecordId,
            DocumentType = "Documento soporte",
            Prefix = "CC",
            Folio = folio,
            InvoiceNumber = $"CC-{folio}",
            EmissionDateValue = row.FechaEmisionValue,
            SupplierNit = row.NitOCedula,
            SupplierName = row.Receptor,
            BaseAmount = row.ValorTotal,
            TotalValue = row.ValorTotal,
            PaymentValue = row.ValorPago,
            ReteFuenteValue = row.ReteFuenteValor,
            CategoryLabel = "Cuenta de cobro",
            AccountCode = row.AccountCode,
            AccountName = row.AccountName
        };
    }

    private static object BuildCuentaCobroSupportDocumentPayload(
        ConciliacionCuentaCobroRowDto row,
        SiigoDocumentTypeLookupDto document,
        SiigoPaymentTypeLookupDto paymentType,
        List<string> issues)
    {
        if (!TryParseSiigoDate(row.FechaEmisionValue, out var emissionDate))
        {
            issues.Add("La fecha de emision de la cuenta de cobro no tiene formato valido para Siigo (yyyy-MM-dd).");
            emissionDate = ResolveSiigoCurrentDate();
        }

        var dueDate = TryParseSiigoDate(row.FechaPagoValue, out var parsedPaymentDate)
            ? parsedPaymentDate
            : emissionDate;
        if (!string.IsNullOrWhiteSpace(row.FechaPagoValue) && dueDate == emissionDate && row.FechaPagoValue != row.FechaEmisionValue)
            issues.Add("La fecha de pago de la cuenta de cobro no tiene formato valido para Siigo (yyyy-MM-dd).");
        var identification = ExtractDigits(row.NitOCedula);
        var retentions = GetCuentaCobroPaymentRetentions(row)
            .Where(static retention => retention.Value > 0m)
            .ToArray();
        var totalRetentions = RoundCurrency(retentions.Sum(static retention => retention.Value));
        var retentionDetail = retentions.Length == 0
            ? "sin retenciones"
            : string.Join(", ", retentions.Select(retention =>
                $"{FirstNonEmpty(retention.Label, retention.Kind, "retencion")} {retention.Value:N2}"));
        var payment = new Dictionary<string, object?>
        {
            ["id"] = paymentType.Id,
            ["value"] = row.ValorTotal
        };
        if (paymentType.DueDate)
            payment["due_date"] = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var payload = new Dictionary<string, object?>
        {
            ["document"] = new { id = document.Id },
            ["date"] = emissionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["supplier"] = new
            {
                identification,
                branch_office = 0
            },
            ["supplier_receipt_number"] = new
            {
                prefix = "CC",
                number = BuildCuentaCobroSupplierReceiptNumber(row)
            },
            ["items"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "Account",
                    ["code"] = row.AccountCode.Trim(),
                    ["description"] = TruncateControllerText(FirstNonEmpty(row.AccountName, row.Observaciones, $"Cuenta de cobro {row.Receptor}"), 100),
                    ["quantity"] = 1,
                    ["price"] = row.ValorTotal
                }
            },
            ["payments"] = new[] { payment },
            ["observations"] = TruncateControllerText(
                $"Cuenta de cobro automatizada desde Conciliacion. Proveedor: {row.Receptor} {row.NitOCedula}. " +
                $"Documento soporte por valor bruto {row.ValorTotal:N2}. El pago se registra en comprobante de egreso " +
                $"por {row.ValorPago:N2} y retenciones {totalRetentions:N2} ({retentionDetail}). " +
                $"Flujo caja: {row.SourceFlow} {row.BankAccountName} {row.CashFlowDescription}.",
                500)
        };

        return payload;
    }

    private static bool TryParseSiigoDate(string? value, out DateOnly date)
    {
        return DateOnly.TryParseExact(
            value?.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static DateOnly ResolveSiigoCurrentDate()
    {
        var utcNow = DateTimeOffset.UtcNow;
        foreach (var timeZoneId in new[] { "SA Pacific Standard Time", "America/Bogota" })
        {
            try
            {
                var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, timeZone).DateTime);
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

    private async Task<PreparedCuentaCobroPaymentReceipt> PrepareCuentaCobroPaymentReceiptForSiigoAsync(
        ConciliacionCuentaCobroDocumentRequest request,
        string supportDocumentId,
        string supportDocumentName,
        CancellationToken ct)
    {
        var row = await _dataverse.GetConciliacionCuentaCobroDocumentAsync(request, ct);
        var issues = ValidateCuentaCobroPaymentReceiptBase(row, supportDocumentId, supportDocumentName).ToList();
        var targetEndpoint = "/v1/journals";
        object? payload = null;
        var payloadJson = "";

        try
        {
            var documentTypesTask = _siigo.GetDocumentTypesAsync("CC", ct);
            var taxesTask = _siigo.GetTaxesAsync(ct);
            var accountOptionsTask = _dataverse.GetConciliacionAccountingAccountOptionsAsync(ct);
            await Task.WhenAll(documentTypesTask, taxesTask, accountOptionsTask);

            var document = ResolveExpenseJournalDocumentType(documentTypesTask.Result);
            row.Retentions = ValidateAndResolveCuentaCobroPaymentRetentions(
                row,
                taxesTask.Result,
                accountOptionsTask.Result.Select(static option => option.Value).ToArray(),
                issues);
            var payloadIssues = new List<string>();
            payload = BuildCuentaCobroPaymentReceiptPayload(
                row,
                document,
                FirstNonEmpty(supportDocumentName, row.SiigoDocumentName),
                payloadIssues);
            issues.AddRange(payloadIssues);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            issues.Add(BuildExceptionDetail(ex));
        }

        if (payload is not null)
            payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        return new PreparedCuentaCobroPaymentReceipt(
            Row: row,
            CanSend: issues.Count == 0 && payload is not null,
            TargetEndpoint: targetEndpoint,
            Payload: issues.Count == 0 ? payload : null,
            PayloadJson: payloadJson,
            Issues: issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static IReadOnlyList<string> ValidateCuentaCobroPaymentReceiptBase(
        ConciliacionCuentaCobroRowDto row,
        string supportDocumentId,
        string supportDocumentName)
    {
        var issues = new List<string>();
        var retentions = GetCuentaCobroPaymentRetentions(row);
        if (string.IsNullOrWhiteSpace(row.RecordId))
            issues.Add("Falta asociar la salida bancaria con una cuenta de cobro de la app.");
        if (string.IsNullOrWhiteSpace(row.CashFlowRecordId) && string.IsNullOrWhiteSpace(row.CashFlowExternalKey))
            issues.Add("Falta la salida bancaria del flujo de caja para armar el pago.");
        if (string.IsNullOrWhiteSpace(row.Receptor) || string.IsNullOrWhiteSpace(row.NitOCedula))
            issues.Add("Falta nombre o NIT/cedula del proveedor/persona.");
        if (string.IsNullOrWhiteSpace(row.FechaPagoValue) && string.IsNullOrWhiteSpace(row.MovementDateValue))
            issues.Add("Falta fecha de pago.");
        if (row.ValorTotal <= 0m)
            issues.Add("El valor total del documento soporte debe ser mayor a cero.");
        if (row.ValorPago <= 0m)
            issues.Add("El valor pagado debe ser mayor a cero.");
        if (!row.TotalesCuadran)
            issues.Add("El valor total debe ser igual a valor pago mas la suma de todas las retenciones.");
        foreach (var retention in retentions)
        {
            if (retention.Value <= 0m)
                issues.Add($"El valor de {FirstNonEmpty(retention.Label, retention.Kind, "la retencion")} debe ser mayor a cero.");
            var accountCode = ResolveCuentaCobroPaymentRetentionAccountCode(retention);
            if (string.IsNullOrWhiteSpace(accountCode))
            {
                issues.Add(
                    $"La retencion {FirstNonEmpty(retention.Label, retention.Kind, "sin nombre")} no tiene una cuenta contable aprobada. Configurala antes de enviar.");
            }
            else if (!Regex.IsMatch(accountCode, @"^\d{4,20}$", RegexOptions.CultureInvariant))
            {
                issues.Add($"La cuenta contable {accountCode} de {FirstNonEmpty(retention.Label, retention.Kind)} no es valida.");
            }
        }
        if (Math.Abs(row.DifferenceValue) > 1m)
            issues.Add($"La salida del flujo de caja no coincide con el valor pago. Diferencia: {row.DifferenceValue:N2}.");
        if (string.IsNullOrWhiteSpace(row.BankAccountCode))
            issues.Add("Falta codigo de cuenta bancaria para acreditar el pago.");
        if (string.IsNullOrWhiteSpace(FirstNonEmpty(supportDocumentName, row.SiigoDocumentName)))
            issues.Add("Falta el consecutivo del documento soporte Siigo para aplicar el pago.");
        if (!string.IsNullOrWhiteSpace(row.SiigoPaymentId) || !string.IsNullOrWhiteSpace(row.SiigoPaymentName))
            issues.Add("Esta cuenta de cobro ya tiene pago Siigo asociado.");

        return issues;
    }

    internal static IReadOnlyList<ConciliacionCuentaCobroRetentionDto> ValidateAndResolveCuentaCobroPaymentRetentions(
        ConciliacionCuentaCobroRowDto row,
        IReadOnlyList<SiigoTaxLookupDto> taxes,
        IReadOnlyCollection<string> activeAccountCodes,
        ICollection<string> issues)
    {
        var taxCatalog = taxes ?? Array.Empty<SiigoTaxLookupDto>();
        var activeAccounts = new HashSet<string>(
            (activeAccountCodes ?? Array.Empty<string>())
                .Where(static code => !string.IsNullOrWhiteSpace(code))
                .Select(static code => code.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var reportedAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedRetentions = new List<ConciliacionCuentaCobroRetentionDto>();

        void ValidateAccount(string? code, string label)
        {
            var normalizedCode = (code ?? "").Trim();
            if (string.IsNullOrWhiteSpace(normalizedCode) || !reportedAccounts.Add(normalizedCode))
                return;

            if (!Regex.IsMatch(normalizedCode, @"^\d{4,20}$", RegexOptions.CultureInvariant))
            {
                issues.Add($"La cuenta contable {normalizedCode} de {label} no tiene un formato valido.");
                return;
            }
            if (!activeAccounts.Contains(normalizedCode))
                issues.Add($"La cuenta contable {normalizedCode} de {label} no existe o no esta activa en el catalogo contable aprobado.");
        }

        ValidateAccount("22050501", "proveedores nacionales");
        ValidateAccount(row.BankAccountCode, "la cuenta bancaria");

        foreach (var retention in GetCuentaCobroPaymentRetentions(row))
        {
            var label = FirstNonEmpty(retention.Label, retention.Kind, "la retencion");
            var knownKind = string.Equals(retention.Kind, "ReteFuente", StringComparison.OrdinalIgnoreCase)
                || string.Equals(retention.Kind, "ReteFte", StringComparison.OrdinalIgnoreCase)
                || string.Equals(retention.Kind, "ReteICA", StringComparison.OrdinalIgnoreCase)
                || string.Equals(retention.Kind, "RteIVA", StringComparison.OrdinalIgnoreCase);
            var tax = retention.TaxId > 0
                ? taxCatalog.FirstOrDefault(candidate => candidate.Id == retention.TaxId)
                : knownKind
                    ? ConciliacionRetentionMapping.FindTax(taxCatalog, retention.Kind, retention.Rate)
                    : null;

            if (retention.TaxId > 0 && tax is null)
            {
                issues.Add($"El impuesto Siigo {retention.TaxId} de {label} no existe en el catalogo actual.");
            }
            else if (knownKind && tax is null)
            {
                issues.Add($"No existe un impuesto Siigo activo de tipo {retention.Kind} con tarifa {retention.Rate:N4} para {label}.");
            }
            else if (tax is not null && !tax.Active)
            {
                issues.Add($"El impuesto Siigo {tax.Id} de {label} esta inactivo.");
            }
            if (tax is not null && knownKind && !ConciliacionRetentionMapping.MatchesKind(tax, retention.Kind))
                issues.Add($"El impuesto Siigo {tax.Id} no corresponde al tipo {retention.Kind} de {label}.");
            if (tax is not null && retention.Rate > 0m && Math.Abs(tax.Percentage - retention.Rate) > 0.1m)
            {
                issues.Add(
                    $"La tarifa {retention.Rate:N4} de {label} no coincide con el impuesto Siigo {tax.Id} ({tax.Percentage:N4}).");
            }

            var mappedAccount = ResolveCuentaCobroPaymentRetentionAccountCode(new ConciliacionCuentaCobroRetentionDto
            {
                Kind = retention.Kind,
                TaxId = tax?.Id ?? retention.TaxId,
                Rate = retention.Rate,
                AccountCode = ""
            });
            var explicitAccount = (retention.AccountCode ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(mappedAccount)
                && !string.IsNullOrWhiteSpace(explicitAccount)
                && !string.Equals(mappedAccount, explicitAccount, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"La cuenta explicita {explicitAccount} de {label} no coincide con la cuenta aprobada {mappedAccount}.");
            }
            var accountCode = FirstNonEmpty(mappedAccount, explicitAccount);
            if (string.IsNullOrWhiteSpace(accountCode))
                issues.Add($"La retencion {label} no tiene una cuenta contable aprobada.");
            else
                ValidateAccount(accountCode, label);

            if (retention.BaseValue <= 0m)
                issues.Add($"La base de {label} debe ser mayor a cero.");
            if (knownKind && retention.Rate <= 0m)
                issues.Add($"La tarifa de {label} debe ser mayor a cero.");
            if (retention.BaseValue > 0m && retention.Rate > 0m)
            {
                var divisor = string.Equals(retention.Kind, "ReteICA", StringComparison.OrdinalIgnoreCase) ? 1000m : 100m;
                var expectedValue = RoundCurrency(retention.BaseValue * retention.Rate / divisor);
                if (Math.Abs(expectedValue - retention.Value) > 0.01m)
                {
                    issues.Add(
                        $"El valor {retention.Value:N2} de {label} no coincide con base {retention.BaseValue:N2} y tarifa {retention.Rate:N4} (esperado {expectedValue:N2}).");
                }
            }

            resolvedRetentions.Add(new ConciliacionCuentaCobroRetentionDto
            {
                Kind = retention.Kind,
                Label = label,
                TaxId = tax?.Id ?? retention.TaxId,
                AccountCode = accountCode,
                BaseValue = retention.BaseValue,
                Rate = retention.Rate,
                Value = retention.Value
            });
        }

        var totalRetentions = RoundCurrency(resolvedRetentions.Sum(static retention => retention.Value));
        var adjustment = RoundCurrency(row.ValorTotal - row.ValorPago - totalRetentions);
        if (Math.Abs(adjustment) > 0.009m && Math.Abs(adjustment) <= 1m)
            ValidateAccount("42958101", "el ajuste al peso");

        return resolvedRetentions;
    }

    internal static object BuildCuentaCobroPaymentReceiptPayload(
        ConciliacionCuentaCobroRowDto row,
        SiigoDocumentTypeLookupDto document,
        string supportDocumentName,
        List<string> issues)
    {
        var paymentDate = DateOnly.TryParseExact(row.FechaPagoValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedPaymentDate)
            ? parsedPaymentDate
            : DateOnly.TryParseExact(row.MovementDateValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedMovementDate)
                ? parsedMovementDate
                : DateOnly.FromDateTime(DateTime.UtcNow);
        var identification = ExtractDigits(row.NitOCedula);
        if (!TryParseSiigoDueLabel(supportDocumentName, out var duePrefix, out var dueConsecutive))
            issues.Add($"No se pudo separar prefijo y consecutivo del documento soporte Siigo {supportDocumentName}.");

        var supplier = new
        {
            identification,
            branch_office = 0
        };
        var retentions = GetCuentaCobroPaymentRetentions(row);
        var items = new List<Dictionary<string, object?>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["account"] = new
                {
                    code = "22050501",
                    movement = "Debit"
                },
                ["customer"] = supplier,
                ["description"] = "Proveedores nacionales",
                ["due"] = new
                {
                    prefix = duePrefix,
                    consecutive = dueConsecutive,
                    quote = 1,
                    date = paymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                },
                ["value"] = row.ValorTotal
            }
        };

        foreach (var retention in retentions)
        {
            var retentionAccountCode = ResolveCuentaCobroPaymentRetentionAccountCode(retention);
            if (string.IsNullOrWhiteSpace(retentionAccountCode))
            {
                issues.Add(
                    $"La retencion {FirstNonEmpty(retention.Label, retention.Kind, "sin nombre")} no tiene una cuenta contable aprobada.");
                continue;
            }
            if (retention.Value <= 0m)
            {
                issues.Add($"El valor de {FirstNonEmpty(retention.Label, retention.Kind, "la retencion")} debe ser mayor a cero.");
                continue;
            }

            var retentionLine = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["account"] = new
                {
                    code = retentionAccountCode,
                    movement = "Credit"
                },
                ["customer"] = supplier,
                ["description"] = TruncateControllerText(BuildCuentaCobroPaymentRetentionDescription(retention), 200),
                ["value"] = retention.Value
            };
            if (retention.TaxId > 0)
                retentionLine["tax"] = new { id = retention.TaxId };
            items.Add(retentionLine);
        }

        items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["account"] = new
            {
                code = row.BankAccountCode.Trim(),
                movement = "Credit"
            },
            ["customer"] = supplier,
            ["description"] = TruncateControllerText(FirstNonEmpty(row.BankAccountName, $"Pago banco {supportDocumentName}"), 200),
            ["value"] = row.ValorPago
        });

        var totalRetentions = RoundCurrency(retentions.Sum(static retention => retention.Value));
        var difference = RoundCurrency(row.ValorTotal - row.ValorPago - totalRetentions);
        if (Math.Abs(difference) > 0.009m && Math.Abs(difference) <= 1m)
        {
            items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["account"] = new
                {
                    code = "42958101",
                    movement = difference > 0m ? "Credit" : "Debit"
                },
                ["customer"] = supplier,
                ["description"] = TruncateControllerText($"Ajuste al peso {supportDocumentName}".Trim(), 200),
                ["value"] = Math.Abs(difference)
            });
        }

        return new
        {
            document = new
            {
                id = document.Id
            },
            date = paymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            items,
            observations = TruncateControllerText(
                $"Comprobante de egreso cuenta de cobro desde Conciliacion. Documento soporte {supportDocumentName}. " +
                $"Valor total {row.ValorTotal:N2}; pago banco {row.ValorPago:N2}; retenciones {totalRetentions:N2}. " +
                $"Flujo caja: {row.SourceFlow} {row.BankAccountName} {row.CashFlowDescription}.",
                500)
        };
    }

    private static IReadOnlyList<ConciliacionCuentaCobroRetentionDto> GetCuentaCobroPaymentRetentions(
        ConciliacionCuentaCobroRowDto row)
    {
        if (row.Retentions.Count > 0)
            return row.Retentions;

        if (row.ReteFuenteValor <= 0m && row.ReteFuentePorcentaje <= 0m)
            return Array.Empty<ConciliacionCuentaCobroRetentionDto>();

        var rate = row.ReteFuentePorcentaje > 0m
            ? row.ReteFuentePorcentaje
            : row.ValorTotal > 0m
                ? Math.Round(row.ReteFuenteValor / row.ValorTotal * 100m, 4, MidpointRounding.AwayFromZero)
                : 0m;
        return new[]
        {
            new ConciliacionCuentaCobroRetentionDto
            {
                Kind = "ReteFuente",
                Label = "ReteFuente cuenta de cobro",
                BaseValue = row.ValorTotal,
                Rate = rate,
                Value = row.ReteFuenteValor
            }
        };
    }

    private static string ResolveCuentaCobroPaymentRetentionAccountCode(
        ConciliacionCuentaCobroRetentionDto retention)
    {
        var explicitAccount = (retention.AccountCode ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(explicitAccount))
            return explicitAccount;

        if (string.Equals(retention.Kind, "ReteICA", StringComparison.OrdinalIgnoreCase))
        {
            return retention.TaxId is 4028 or 4030 or 4033 or 4034
                ? "23680501"
                : "";
        }

        if (string.Equals(retention.Kind, "RteIVA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(retention.Kind, "ReteIVA", StringComparison.OrdinalIgnoreCase))
        {
            return "23670101";
        }

        if (!string.Equals(retention.Kind, "ReteFuente", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(retention.Kind, "ReteFte", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        var rate = retention.Rate;

        return rate switch
        {
            _ when Math.Abs(rate - 2.5m) <= 0.1m => "23654001",
            _ when Math.Abs(rate - 3.5m) <= 0.1m => "23651505",
            _ when Math.Abs(rate - 4m) <= 0.1m => "23652503",
            _ when Math.Abs(rate - 7m) <= 0.1m => "23651503",
            _ when Math.Abs(rate - 11m) <= 0.1m => "23651515",
            _ => ""
        };
    }

    private static string BuildCuentaCobroPaymentRetentionDescription(
        ConciliacionCuentaCobroRetentionDto retention)
    {
        var label = FirstNonEmpty(retention.Label, retention.Kind, "Retencion cuenta de cobro");
        var rate = retention.Rate;
        var rateLabel = string.Equals(retention.Kind, "ReteICA", StringComparison.OrdinalIgnoreCase)
            ? $"{rate:N2} por mil"
            : $"{rate:N2}%";

        return rate > 0m
            ? $"{label} {rateLabel}"
            : label;
    }

    private static IReadOnlyList<SiigoTaxLookupDto> ResolveCuentaCobroSupportDocumentRetentions(
        ConciliacionCuentaCobroRowDto row,
        IReadOnlyList<SiigoTaxLookupDto> taxes,
        List<string> issues)
    {
        if (row.ReteFuenteValor <= 0m && row.ReteFuentePorcentaje <= 0m)
            return Array.Empty<SiigoTaxLookupDto>();

        var targetPercent = row.ReteFuentePorcentaje;
        var candidates = taxes
            .Where(static tax => tax.Active && tax.Id > 0)
            .Where(static tax =>
                tax.Type.Contains("Retencion", StringComparison.OrdinalIgnoreCase)
                || tax.Name.Contains("Retefuente", StringComparison.OrdinalIgnoreCase)
                || tax.Name.Contains("Rete fuente", StringComparison.OrdinalIgnoreCase)
                || tax.Name.Contains("Rte Fte", StringComparison.OrdinalIgnoreCase))
            .Select(tax => new
            {
                Tax = tax,
                Delta = Math.Abs(tax.Percentage - targetPercent),
                NameScore = tax.Name.Contains("Retefuente", StringComparison.OrdinalIgnoreCase)
                    || tax.Name.Contains("Rete fuente", StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : 1
            })
            .Where(static item => item.Delta <= 0.05m)
            .OrderBy(static item => item.NameScore)
            .ThenBy(static item => item.Delta)
            .ToArray();
        var selected = candidates.FirstOrDefault()?.Tax;
        if (selected is null)
        {
            issues.Add($"No encontre impuesto Siigo activo para ReteFuente {targetPercent:N2}% de la cuenta de cobro.");
            return Array.Empty<SiigoTaxLookupDto>();
        }

        return new[] { selected };
    }

    internal static string BuildCuentaCobroSiigoIdempotencyIdentity(
        ConciliacionCuentaCobroDocumentRequest request)
    {
        var identity = FirstNonEmpty(
            request.CashFlowExternalKey,
            request.CashFlowRecordId,
            request.RecordId).Trim();
        if (string.IsNullOrWhiteSpace(identity))
            throw new InvalidOperationException("Falta la identidad estable de la cuenta de cobro.");

        return identity;
    }

    private static string BuildCuentaCobroSupplierReceiptNumber(ConciliacionCuentaCobroRowDto row)
    {
        var digits = ExtractDigits(row.RecordId);
        if (digits.Length < 11)
            digits = ExtractDigits($"{row.FechaEmisionValue}{row.ValorPago:0}{row.NitOCedula}{row.RecordId}");

        if (digits.Length >= 11)
            return digits[^11..];

        return digits.PadLeft(11, '0');
    }

    private async Task<SiigoSupplierEnsureResult> EnsureDianSupplierInSiigoAsync(
        ConciliacionDianSupplierInvoiceRowDto row,
        bool allowCreate,
        CancellationToken ct,
        ConciliacionDianSupplierDocumentRequest? request = null)
    {
        var supplierName = FirstNonEmpty(request?.SupplierName, row.SupplierName).Trim();
        var supplierNit = FirstNonEmpty(request?.SupplierNit, row.SupplierNit).Trim();
        var personType = ResolveSiigoSupplierPersonType(request?.PersonType, supplierName);
        var isCompany = string.Equals(personType, "Company", StringComparison.OrdinalIgnoreCase);
        var identification = ExtractDigits(supplierNit);
        if (identification.Length < 5)
            throw new InvalidOperationException("El documento DIAN no tiene un NIT/identificacion de proveedor valido.");

        var taxId = ResolveSupplierTaxId(supplierNit, isCompany);
        var requestedCheckDigit = ExtractDigits(request?.CheckDigit ?? "");
        if (isCompany && requestedCheckDigit.Length > 0)
            taxId = taxId with { CheckDigit = requestedCheckDigit[^1].ToString() };
        var existing = await _siigo.SearchCustomersAsync(taxId.Identification, top: 10, ct);
        var exactAnyState = existing.FirstOrDefault(customer =>
            IsSameSupplierIdentification(customer.Identification, taxId, isCompany));
        var exact = exactAnyState?.Active == true ? exactAnyState : null;
        if (exact is not null)
        {
            return new SiigoSupplierEnsureResult(exact, ExistsInSiigo: true, Created: false, WouldCreate: false, Payload: null);
        }

        if (!string.IsNullOrWhiteSpace(row.SiigoSupplierId))
        {
            throw new InvalidOperationException(
                $"Dataverse ya tiene asociado el proveedor Siigo {row.SiigoSupplierId}, pero la consulta por NIT aun no lo confirma. "
                + "No se repetira el POST /customers; verifica la sincronizacion o corrige el vinculo manualmente.");
        }

        if (allowCreate && IsDianSupplierCreationWriteHold(row))
        {
            throw new InvalidOperationException(
                "La creacion anterior del proveedor no tuvo una respuesta concluyente. "
                + "Se consulto nuevamente y el tercero aun no aparece; no se repetira el POST para evitar duplicados.");
        }

        var payload = BuildSiigoSupplierPayload(row, request);
        if (!allowCreate)
        {
            return new SiigoSupplierEnsureResult(new SiigoCustomerLookupItemDto
            {
                Id = "",
                DisplayName = $"{supplierName} - {identification}",
                Name = supplierName,
                CommercialName = supplierName,
                Identification = identification,
                Type = "Supplier",
                BranchOffice = 0,
                Active = true
            }, ExistsInSiigo: false, Created: false, WouldCreate: true, Payload: payload);
        }

        if (exactAnyState is not null)
            throw new InvalidOperationException("El proveedor ya existe en Siigo, pero esta inactivo. Reactivalo en Siigo antes de continuar; no se creara un duplicado.");

        await DianSupplierCreationGate.WaitAsync(ct);
        try
        {
            existing = await _siigo.SearchCustomersAsync(taxId.Identification, top: 50, ct);
            exactAnyState = existing.FirstOrDefault(customer =>
                IsSameSupplierIdentification(customer.Identification, taxId, isCompany));
            if (exactAnyState?.Active == true)
                return new SiigoSupplierEnsureResult(exactAnyState, ExistsInSiigo: true, Created: false, WouldCreate: false, Payload: null);
            if (exactAnyState is not null)
                throw new InvalidOperationException("El proveedor ya existe en Siigo, pero esta inactivo. Reactivalo en Siigo antes de continuar; no se creara un duplicado.");

            SiigoCustomerLookupItemDto created;
            var attemptedPayloads = new List<string>();
            Exception? lastCreateException = null;
            foreach (var candidatePayload in BuildSiigoSupplierPayloadCandidates(row, request))
            {
                var candidateJson = JsonSerializer.Serialize(candidatePayload, new JsonSerializerOptions { WriteIndented = true });
                if (attemptedPayloads.Contains(candidateJson, StringComparer.OrdinalIgnoreCase))
                    continue;

                attemptedPayloads.Add(candidateJson);
                try
                {
                    created = await _siigo.CreateCustomerAsync(candidatePayload, idempotencyKey: null, ct: ct);

                    return new SiigoSupplierEnsureResult(created, ExistsInSiigo: true, Created: true, WouldCreate: false, Payload: candidatePayload);
                }
                catch (InvalidOperationException ex)
                {
                    lastCreateException = ex;
                    IReadOnlyList<SiigoCustomerLookupItemDto> recovered;
                    try
                    {
                        recovered = await _siigo.SearchCustomersAsync(taxId.Identification, top: 50, ct);
                    }
                    catch (Exception recoveryException) when (IsAmbiguousSupplierCreateFailure(ex))
                    {
                        throw new SiigoSupplierCreateException(
                            "Siigo no confirmo de forma concluyente la creacion del proveedor y fallo la consulta de verificacion. No se intentara otro POST para evitar duplicados.",
                            candidateJson,
                            new AggregateException(ex, recoveryException),
                            isAmbiguous: true);
                    }

                    var recoveredExact = recovered.FirstOrDefault(customer =>
                        customer.Active && IsSameSupplierIdentification(customer.Identification, taxId, isCompany));
                    if (recoveredExact is not null)
                    {
                        return new SiigoSupplierEnsureResult(
                            recoveredExact,
                            ExistsInSiigo: true,
                            Created: true,
                            WouldCreate: false,
                            Payload: candidatePayload);
                    }

                    if (IsAmbiguousSupplierCreateFailure(ex))
                    {
                        throw new SiigoSupplierCreateException(
                            "Siigo no confirmo de forma concluyente la creacion del proveedor. No se intentara otro POST para evitar duplicados.",
                            candidateJson,
                            ex,
                            isAmbiguous: true);
                    }
                }
            }

            throw new SiigoSupplierCreateException(
                "Siigo rechazo la creacion del proveedor.",
                string.Join(
                    $"{Environment.NewLine}{Environment.NewLine}--- intento alternativo ---{Environment.NewLine}{Environment.NewLine}",
                    attemptedPayloads),
                lastCreateException ?? new InvalidOperationException("Siigo rechazo la creacion del proveedor."));
        }
        finally
        {
            DianSupplierCreationGate.Release();
        }
    }

    private static IReadOnlyList<string> ValidateDianSupplierPurchaseBase(ConciliacionDianSupplierInvoiceRowDto row)
    {
        var issues = new List<string>();
        if (!IsDianSupplierInvoice(row))
            issues.Add("El envio automatico inicial esta habilitado solo para facturas electronicas de proveedor. Los documentos soporte se conectan en el siguiente paso.");
        if (!string.IsNullOrWhiteSpace(row.SiigoDocumentId) || !string.IsNullOrWhiteSpace(row.SiigoDocumentName))
            issues.Add("Este documento ya tiene documento Siigo asociado.");
        if (string.IsNullOrWhiteSpace(row.SupplierNit) || string.IsNullOrWhiteSpace(row.SupplierName))
            issues.Add("Falta NIT o nombre del proveedor.");
        if (string.IsNullOrWhiteSpace(row.InvoiceNumber) || string.IsNullOrWhiteSpace(row.Folio))
            issues.Add("Falta numero de factura del proveedor.");
        if (string.IsNullOrWhiteSpace(row.EmissionDateValue))
            issues.Add("Falta fecha de emision.");
        if (row.TotalValue <= 0m)
            issues.Add("El total de la factura debe ser mayor a cero.");
        if (string.IsNullOrWhiteSpace(row.AccountCode))
            issues.Add("Falta cuenta gasto.");
        if (row.BaseAmount <= 0m && row.TotalValue <= row.VatValue)
            issues.Add("No hay base valida para crear la linea de compra.");

        return issues;
    }

    private static object BuildDianSupplierPurchasePayload(
        ConciliacionDianSupplierInvoiceRowDto row,
        SiigoDocumentTypeLookupDto purchaseDocument,
        SiigoPaymentTypeLookupDto paymentType,
        IReadOnlyList<SiigoTaxLookupDto> taxes,
        List<string> issues)
    {
        var identification = ExtractDigits(row.SupplierNit);
        var providerInvoiceNumber = ExtractDigits(FirstNonEmpty(row.Folio, row.InvoiceNumber));
        var prefix = (row.Prefix ?? "").Trim();
        if (prefix.Length > 6)
            issues.Add("El prefijo de la factura supera 6 caracteres; Siigo puede rechazarlo.");
        if (string.IsNullOrWhiteSpace(providerInvoiceNumber))
            issues.Add("El consecutivo de la factura del proveedor debe tener numeros.");

        var emissionDate = DateOnly.TryParseExact(row.EmissionDateValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
            ? parsedDate
            : DateOnly.FromDateTime(DateTime.UtcNow);
        var purchaseItems = BuildDianSupplierPurchaseItems(row, taxes, issues);
        var purchaseTotal = CalculateDianSupplierPurchaseItemsTotal(purchaseItems);
        if (Math.Abs(purchaseTotal - row.TotalValue) > 1m)
        {
            issues.Add($"El total calculado para Siigo ({purchaseTotal:N2}) no coincide con el total DIAN ({row.TotalValue:N2}).");
        }

        var payment = new Dictionary<string, object?>
        {
            ["id"] = paymentType.Id,
            ["value"] = purchaseTotal
        };
        if (paymentType.DueDate)
            payment["due_date"] = emissionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new Dictionary<string, object?>
        {
            ["document"] = new { id = purchaseDocument.Id },
            ["date"] = emissionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["supplier"] = new
            {
                identification,
                branch_office = 0
            },
            ["provider_invoice"] = new
            {
                prefix = prefix.Length > 6 ? prefix[..6] : prefix,
                number = providerInvoiceNumber
            },
            ["items"] = purchaseItems.Select(static item => item.Payload).ToArray(),
            ["payments"] = new[] { payment },
            ["observations"] = TruncateControllerText(
                $"Importado desde DIAN. CUFE/CUDE: {row.Cufe}. Cuenta: {row.AccountCode} {row.AccountName}.",
                500)
        };
    }

    private static IReadOnlyList<DianSupplierPurchaseItemDraft> BuildDianSupplierPurchaseItems(
        ConciliacionDianSupplierInvoiceRowDto row,
        IReadOnlyList<SiigoTaxLookupDto> taxes,
        List<string> issues)
    {
        var baseAmount = row.BaseAmount > 0m
            ? row.BaseAmount
            : Math.Max(0m, row.TotalValue - row.VatValue);
        if (baseAmount <= 0m)
            baseAmount = row.TotalValue;

        var description = TruncateControllerText(FirstNonEmpty(row.AccountName, row.AccountCode, "Cuenta contable"), 100);
        var vatItem = BuildDianSupplierPurchaseItemDraft(
            DianSupplierPurchaseVatAccountCode,
            DianSupplierPurchaseVatDescription,
            RoundUnitPrice(row.VatValue),
            tax: null);

        if (row.VatValue <= 0m)
        {
            return new[]
            {
                BuildDianSupplierPurchaseItemDraft(row.AccountCode, description, RoundUnitPrice(baseAmount), tax: null)
            };
        }

        var taxMatch = ResolveDianSupplierPurchaseVatTax(row, baseAmount, taxes, issues);
        if (taxMatch is null)
        {
            return new[]
            {
                BuildDianSupplierPurchaseItemDraft(row.AccountCode, description, RoundUnitPrice(baseAmount), tax: null),
                vatItem
            };
        }

        var taxableBase = taxMatch.TaxableBase;
        var nonTaxedBase = taxMatch.NonTaxedBase;
        var drafts = new List<DianSupplierPurchaseItemDraft>
        {
            BuildDianSupplierPurchaseItemDraft(
                row.AccountCode,
                description,
                RoundUnitPrice(taxableBase),
                tax: null)
        };

        if (nonTaxedBase > 0.01m)
        {
            drafts.Add(BuildDianSupplierPurchaseItemDraft(
                row.AccountCode,
                TruncateControllerText($"{description} sin IVA", 100),
                RoundUnitPrice(nonTaxedBase),
                tax: null));
        }

        drafts.Add(vatItem);

        var total = CalculateDianSupplierPurchaseItemsTotal(drafts);
        var difference = RoundCurrency(row.TotalValue - total);
        if (Math.Abs(difference) <= 1m && difference != 0m)
        {
            var last = drafts[^1];
            drafts[^1] = last with
            {
                Price = RoundUnitPrice(last.Price + difference),
                Payload = BuildDianSupplierPurchaseItemPayload(
                    last.AccountCode,
                    last.Description,
                    RoundUnitPrice(last.Price + difference),
                    last.Tax)
            };
        }

        return drafts;
    }

    private static DianSupplierPurchaseVatMatch? ResolveDianSupplierPurchaseVatTax(
        ConciliacionDianSupplierInvoiceRowDto row,
        decimal baseAmount,
        IReadOnlyList<SiigoTaxLookupDto> taxes,
        List<string> issues)
    {
        var candidates = taxes
            .Where(static tax => tax.Active
                && tax.Id > 0
                && tax.Percentage > 0m
                && tax.Type.Equals("IVA", StringComparison.OrdinalIgnoreCase))
            .Select(tax =>
            {
                var taxableBase = row.VatValue / (tax.Percentage / 100m);
                var nonTaxedBase = baseAmount - taxableBase;
                return new DianSupplierPurchaseVatMatch(
                    tax,
                    TaxableBase: taxableBase,
                    NonTaxedBase: nonTaxedBase,
                    Difference: Math.Abs(nonTaxedBase));
            })
            .Where(static match => match.TaxableBase > 0m && match.NonTaxedBase >= -1m)
            .OrderBy(static match => match.NonTaxedBase < 0m ? 0m : match.NonTaxedBase)
            .ThenByDescending(static match => match.Tax.Percentage)
            .ToArray();

        var selected = candidates.FirstOrDefault();
        if (selected is null)
        {
            var effectivePercent = baseAmount > 0m
                ? Math.Round(row.VatValue / baseAmount * 100m, 2, MidpointRounding.AwayFromZero)
                : 0m;
            issues.Add($"No encontre en Siigo un IVA activo que permita cuadrar IVA {row.VatValue:N2} sobre base {baseAmount:N2}. Tasa efectiva {effectivePercent:N2}%.");
            return null;
        }

        if (selected.NonTaxedBase < 0m && Math.Abs(selected.NonTaxedBase) <= 1m)
        {
            selected = selected with
            {
                TaxableBase = baseAmount,
                NonTaxedBase = 0m
            };
        }

        return selected;
    }

    private static DianSupplierPurchaseItemDraft BuildDianSupplierPurchaseItemDraft(
        string accountCode,
        string description,
        decimal price,
        SiigoTaxLookupDto? tax)
    {
        return new DianSupplierPurchaseItemDraft(
            AccountCode: accountCode,
            Price: price,
            Description: description,
            Tax: tax,
            Payload: BuildDianSupplierPurchaseItemPayload(accountCode, description, price, tax));
    }

    private static Dictionary<string, object?> BuildDianSupplierPurchaseItemPayload(
        string accountCode,
        string description,
        decimal price,
        SiigoTaxLookupDto? tax)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "Account",
            ["code"] = accountCode.Trim(),
            ["description"] = description,
            ["quantity"] = 1,
            ["price"] = price
        };
        if (tax is not null)
            payload["taxes"] = new object[] { new { id = tax.Id } };

        return payload;
    }

    private static decimal CalculateDianSupplierPurchaseItemsTotal(IEnumerable<DianSupplierPurchaseItemDraft> items)
    {
        return RoundCurrency(items.Sum(static item =>
            item.Tax is null
                ? item.Price
                : item.Price + item.Price * item.Tax.Percentage / 100m));
    }

    private static object BuildSiigoSupplierPayload(
        ConciliacionDianSupplierInvoiceRowDto row,
        ConciliacionDianSupplierDocumentRequest? request = null,
        bool useCompanyNameAsString = false,
        bool forceVatNotResponsible = false)
    {
        var supplierName = FirstNonEmpty(request?.SupplierName, row.SupplierName).Trim();
        var supplierNit = FirstNonEmpty(request?.SupplierNit, row.SupplierNit).Trim();
        var personType = ResolveSiigoSupplierPersonType(request?.PersonType, supplierName);
        var isCompany = string.Equals(personType, "Company", StringComparison.OrdinalIgnoreCase);
        var taxId = ResolveSupplierTaxId(supplierNit, isCompany);
        var requestedCheckDigit = ExtractDigits(request?.CheckDigit ?? "");
        if (isCompany && requestedCheckDigit.Length > 0)
            taxId = taxId with { CheckDigit = requestedCheckDigit[^1].ToString() };
        var idType = ResolveSiigoSupplierIdType(request?.IdType, isCompany);
        var fiscalResponsibility = FirstNonEmpty(request?.FiscalResponsibilityCode, "R-99-PN").Trim();
        var vatResponsible = (request?.VatResponsible ?? false)
            && !forceVatNotResponsible;
        var countryCode = FirstNonEmpty(request?.CountryCode, "CO").Trim();
        var stateCode = FirstNonEmpty(request?.StateCode, "11").Trim();
        var cityCode = FirstNonEmpty(request?.CityCode, "11001").Trim();
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "Supplier",
            ["person_type"] = personType,
            ["id_type"] = idType,
            ["identification"] = taxId.Identification,
            ["name"] = useCompanyNameAsString && isCompany
                ? TruncateControllerText(FirstNonEmpty(supplierName, "Proveedor DIAN"), 100)
                : BuildSiigoSupplierName(supplierName, isCompany),
            ["commercial_name"] = TruncateControllerText(supplierName, 100),
            ["branch_office"] = 0,
            ["active"] = true,
            ["vat_responsible"] = vatResponsible,
            ["fiscal_responsibilities"] = new[] { new { code = fiscalResponsibility } },
            ["address"] = new
            {
                address = TruncateControllerText(FirstNonEmpty(request?.Address, "Sin direccion").Trim(), 100),
                city = new
                {
                    country_code = countryCode,
                    state_code = stateCode,
                    city_code = cityCode
                }
            }
        };
        if (isCompany && !string.IsNullOrWhiteSpace(taxId.CheckDigit))
            payload["check_digit"] = taxId.CheckDigit;

        return payload;
    }

    private static IReadOnlyList<object> BuildSiigoSupplierPayloadCandidates(
        ConciliacionDianSupplierInvoiceRowDto row,
        ConciliacionDianSupplierDocumentRequest? request)
    {
        var supplierName = FirstNonEmpty(request?.SupplierName, row.SupplierName).Trim();
        var personType = ResolveSiigoSupplierPersonType(request?.PersonType, supplierName);
        var isCompany = string.Equals(personType, "Company", StringComparison.OrdinalIgnoreCase);
        var requestedVat = request?.VatResponsible == true;
        var payloads = new List<object>
        {
            BuildSiigoSupplierPayload(row, request)
        };

        if (isCompany)
            payloads.Add(BuildSiigoSupplierPayload(row, request, useCompanyNameAsString: true));

        if (requestedVat)
        {
            payloads.Add(BuildSiigoSupplierPayload(row, request, forceVatNotResponsible: true));
            if (isCompany)
                payloads.Add(BuildSiigoSupplierPayload(row, request, useCompanyNameAsString: true, forceVatNotResponsible: true));
        }

        return payloads;
    }

    private static string ResolveSiigoSupplierPersonType(string? rawPersonType, string supplierName)
    {
        var value = (rawPersonType ?? "").Trim();
        if (value.Equals("Company", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Empresa", StringComparison.OrdinalIgnoreCase))
        {
            return "Company";
        }

        if (value.Equals("Person", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Persona", StringComparison.OrdinalIgnoreCase))
        {
            return "Person";
        }

        return LooksLikeCompany(supplierName) ? "Company" : "Person";
    }

    private static string ResolveSiigoSupplierIdType(string? rawIdType, bool isCompany)
    {
        var value = ExtractDigits(rawIdType ?? "");
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        return isCompany ? "31" : "13";
    }

    private static IReadOnlyList<string> BuildSiigoSupplierName(string supplierName, bool isCompany)
    {
        var cleanName = TruncateControllerText(FirstNonEmpty(supplierName, "Proveedor DIAN"), 100);
        if (isCompany)
            return new[] { cleanName };

        var parts = cleanName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length <= 1)
            return new[] { cleanName, "Proveedor" };

        var lastName = string.Join(" ", parts.Skip(Math.Max(1, parts.Length - 2)));
        var firstName = string.Join(" ", parts.Take(Math.Max(1, parts.Length - 2)));
        return new[] { firstName, lastName };
    }

    private static SiigoDocumentTypeLookupDto ResolvePurchaseDocumentType(IReadOnlyList<SiigoDocumentTypeLookupDto> documentTypes)
    {
        var active = documentTypes.Where(static item => item.Active).ToArray();
        return active.FirstOrDefault(static item =>
                item.Type.Equals("FC", StringComparison.OrdinalIgnoreCase)
                && item.Code.Equals("1", StringComparison.OrdinalIgnoreCase)
                && NormalizeSiigoDocumentTypeText($"{item.Name} {item.Description}").Contains("COMPRA", StringComparison.OrdinalIgnoreCase))
            ?? active.FirstOrDefault(static item => item.Type.Equals("FC", StringComparison.OrdinalIgnoreCase) && item.Code.Equals("1", StringComparison.OrdinalIgnoreCase))
            ?? active.FirstOrDefault(static item => item.Type.Equals("FC", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No encontre en Siigo un tipo de documento FC activo para crear compras.");
    }

    private static SiigoPaymentTypeLookupDto ResolveSupplierPurchasePaymentType(IReadOnlyList<SiigoPaymentTypeLookupDto> paymentTypes)
    {
        var active = paymentTypes.Where(static item => item.Active).ToArray();
        return active.FirstOrDefault(static item =>
                item.Name.Contains("Credito proveedores", StringComparison.OrdinalIgnoreCase)
                || item.Name.Contains("Credito proveedor", StringComparison.OrdinalIgnoreCase))
            ?? active.FirstOrDefault(static item => item.Id == 1726)
            ?? active.FirstOrDefault()
            ?? new SiigoPaymentTypeLookupDto
            {
                Id = 1726,
                Name = "Credito proveedores",
                Type = "Proveedor",
                Active = true,
                DueDate = true
            };
    }

    private static SiigoDocumentTypeLookupDto ResolveSupportDocumentType(IReadOnlyList<SiigoDocumentTypeLookupDto> documentTypes)
    {
        var active = documentTypes.Where(static item => item.Active).ToArray();
        return active.FirstOrDefault(static item =>
                item.Type.Equals("DS", StringComparison.OrdinalIgnoreCase)
                && NormalizeSiigoDocumentTypeText($"{item.Name} {item.Description}").Contains("SOPORTE", StringComparison.OrdinalIgnoreCase))
            ?? active.FirstOrDefault(static item => item.Type.Equals("DS", StringComparison.OrdinalIgnoreCase))
            ?? active.FirstOrDefault(static item =>
                NormalizeSiigoDocumentTypeText($"{item.Name} {item.Description}").Contains("SOPORTE", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No encontre en Siigo un tipo de documento DS activo para crear documentos soporte.");
    }

    internal static SiigoDocumentTypeLookupDto ResolveExpenseJournalDocumentType(IReadOnlyList<SiigoDocumentTypeLookupDto> documentTypes)
    {
        var active = documentTypes.Where(static item => item.Active).ToArray();
        return active.FirstOrDefault(static item =>
                item.Type.Equals("CC", StringComparison.OrdinalIgnoreCase)
                && item.Code.Equals("12", StringComparison.OrdinalIgnoreCase)
                && NormalizeSiigoDocumentTypeText($"{item.Name} {item.Description}")
                    .Contains("COMPROBANTE DE EGRESO", StringComparison.OrdinalIgnoreCase))
            ?? active.FirstOrDefault(static item =>
                item.Type.Equals("CC", StringComparison.OrdinalIgnoreCase)
                && item.Code.Equals("12", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No encontre en Siigo el tipo CC-12 activo para Comprobante de egreso.");
    }

    internal static SiigoPaymentTypeLookupDto ResolveSupportDocumentPaymentType(IReadOnlyList<SiigoPaymentTypeLookupDto> paymentTypes)
    {
        var credit = paymentTypes
            .Where(static item => item.Active && item.DueDate && item.Id > 0)
            .FirstOrDefault(static item =>
            {
                var normalized = NormalizeSiigoDocumentTypeText($"{item.Name} {item.Type}");
                return normalized.Contains("CREDITO", StringComparison.OrdinalIgnoreCase)
                    && (normalized.Contains("PROVEEDOR", StringComparison.OrdinalIgnoreCase)
                        || normalized.Contains("DOCUMENTO SOPORTE", StringComparison.OrdinalIgnoreCase));
            });
        return credit
            ?? throw new InvalidOperationException(
                "No encontre en el catalogo DS de Siigo una forma de pago activa de credito a proveedores con fecha de vencimiento. "
                + "No se creara el documento soporte para evitar pagarlo dos veces.");
    }

    private static bool TryParseSiigoDueLabel(string label, out string prefix, out int consecutive)
    {
        prefix = "";
        consecutive = 0;
        var normalized = Regex.Replace((label ?? "").Trim().ToUpperInvariant(), @"\s+", "-", RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"-+", "-", RegexOptions.CultureInvariant).Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var match = Regex.Match(normalized, @"^(?<prefix>.*?)-(?<consecutive>\d+)$", RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups["consecutive"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out consecutive))
            return false;

        prefix = match.Groups["prefix"].Value.Trim('-');
        return !string.IsNullOrWhiteSpace(prefix) && consecutive > 0;
    }

    private static bool IsDianSupplierInvoice(ConciliacionDianSupplierInvoiceRowDto row)
    {
        var type = NormalizeSiigoDocumentTypeText(row.DocumentType);
        return type.Contains("FACTURA", StringComparison.OrdinalIgnoreCase)
            && !type.Contains("SOPORTE", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDianSupplierDocumentEndpoint(ConciliacionDianSupplierInvoiceRowDto row) =>
        IsDianSupplierInvoice(row) ? "/v1/purchases" : "/v1/purchase-support-documents";

    private static bool LooksLikeCompany(string name)
    {
        var normalized = Regex.Replace(NormalizeSiigoDocumentTypeText(name), @"[^A-Z0-9]+", " ", RegexOptions.CultureInvariant).Trim();
        return Regex.IsMatch(normalized, @"\bS\s*A\s*S\b", RegexOptions.CultureInvariant)
            || Regex.IsMatch(normalized, @"\bS\s*A\b", RegexOptions.CultureInvariant)
            || normalized.Contains(" LTDA", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("LIMITADA", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("SUCURSAL", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("SOCIEDAD", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDianRutFiscalResponsibility(IReadOnlyList<string> responsibilities)
    {
        var allowed = new[] { "O-13", "O-15", "O-23", "O-47" };
        foreach (var responsibility in responsibilities ?? Array.Empty<string>())
        {
            var normalized = NormalizeSiigoDocumentTypeText(responsibility);
            var match = allowed.FirstOrDefault(code =>
                normalized.Contains(code, StringComparison.OrdinalIgnoreCase)
                || normalized.Contains(code.Replace("-", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
                return match;
        }

        return "R-99-PN";
    }

    private static bool IsDianRutVatResponsible(IReadOnlyList<string> responsibilities) =>
        (responsibilities ?? Array.Empty<string>())
        .Select(NormalizeSiigoDocumentTypeText)
        .Any(value => Regex.IsMatch(value, @"\b(?:O\s*-\s*)?48\b", RegexOptions.CultureInvariant)
            || value.Contains("RESPONSABLE DE IVA", StringComparison.OrdinalIgnoreCase)
            || value.Contains("IMPUESTO SOBRE LAS VENTAS", StringComparison.OrdinalIgnoreCase));

    private static SiigoCitySelection ResolveDianRutSiigoCity(string city, string department)
    {
        var normalized = Regex.Replace(
            NormalizeSiigoDocumentTypeText(city),
            @"[^A-Z0-9]+",
            " ",
            RegexOptions.CultureInvariant).Trim();

        return normalized switch
        {
            "BOGOTA" or "BOGOTA D C" or "SANTA FE DE BOGOTA" => new("CO", "11", "11001", "Bogota D.C.", true),
            "MEDELLIN" => new("CO", "05", "05001", "Medellin", true),
            "CALI" or "SANTIAGO DE CALI" => new("CO", "76", "76001", "Cali", true),
            "BARRANQUILLA" => new("CO", "08", "08001", "Barranquilla", true),
            "BUCARAMANGA" => new("CO", "68", "68001", "Bucaramanga", true),
            "SANTA MARTA" => new("CO", "47", "47001", "Santa Marta", true),
            "CARTAGENA" or "CARTAGENA DE INDIAS" => new("CO", "13", "13001", "Cartagena", true),
            "CUCUTA" or "SAN JOSE DE CUCUTA" => new("CO", "54", "54001", "Cucuta", true),
            "PEREIRA" => new("CO", "66", "66001", "Pereira", true),
            "MANIZALES" => new("CO", "17", "17001", "Manizales", true),
            "ARMENIA" => new("CO", "63", "63001", "Armenia", true),
            "IBAGUE" => new("CO", "73", "73001", "Ibague", true),
            "VILLAVICENCIO" => new("CO", "50", "50001", "Villavicencio", true),
            "TUNJA" => new("CO", "15", "15001", "Tunja", true),
            "NEIVA" => new("CO", "41", "41001", "Neiva", true),
            "MONTERIA" => new("CO", "23", "23001", "Monteria", true),
            "VALLEDUPAR" => new("CO", "20", "20001", "Valledupar", true),
            "PASTO" or "SAN JUAN DE PASTO" => new("CO", "52", "52001", "Pasto", true),
            "SINCELEJO" => new("CO", "70", "70001", "Sincelejo", true),
            "POPAYAN" => new("CO", "19", "19001", "Popayan", true),
            "RIOHACHA" => new("CO", "44", "44001", "Riohacha", true),
            "QUIBDO" => new("CO", "27", "27001", "Quibdo", true),
            "YOPAL" => new("CO", "85", "85001", "Yopal", true),
            "FLORENCIA" => new("CO", "18", "18001", "Florencia", true),
            "ARAUCA" => new("CO", "81", "81001", "Arauca", true),
            _ => new("", "", "", FirstNonEmpty(city, department), false)
        };
    }

    private static SupplierTaxId ResolveSupplierTaxId(string rawIdentification, bool isCompany)
    {
        var digits = ExtractDigits(rawIdentification);
        if (!isCompany)
            return new SupplierTaxId(digits, "");

        if (digits.Length >= 10)
        {
            var baseNit = digits[..^1];
            var checkDigit = digits[^1].ToString();
            if (CalculateColombianCheckDigit(baseNit).ToString(CultureInfo.InvariantCulture) == checkDigit)
                return new SupplierTaxId(baseNit, checkDigit);
        }

        return new SupplierTaxId(
            digits,
            CalculateColombianCheckDigit(digits).ToString(CultureInfo.InvariantCulture));
    }

    private static bool IsSameSupplierIdentification(string siigoIdentification, SupplierTaxId expected, bool isCompany)
    {
        var siigoDigits = ExtractDigits(siigoIdentification);
        if (string.Equals(siigoDigits, expected.Identification, StringComparison.OrdinalIgnoreCase))
            return true;

        return isCompany
            && !string.IsNullOrWhiteSpace(expected.CheckDigit)
            && string.Equals(siigoDigits, $"{expected.Identification}{expected.CheckDigit}", StringComparison.OrdinalIgnoreCase);
    }

    private static int CalculateColombianCheckDigit(string identification)
    {
        var digits = ExtractDigits(identification);
        var weights = new[] { 71, 67, 59, 53, 47, 43, 41, 37, 29, 23, 19, 17, 13, 7, 3 };
        var offset = Math.Max(0, weights.Length - digits.Length);
        var sum = 0;
        for (var i = 0; i < digits.Length && i + offset < weights.Length; i++)
            sum += (digits[i] - '0') * weights[i + offset];

        var remainder = sum % 11;
        return remainder > 1 ? 11 - remainder : remainder;
    }

    private static string ExtractDigits(string value) =>
        Regex.Replace(value ?? "", @"\D+", "", RegexOptions.CultureInvariant);

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal RoundUnitPrice(decimal value) =>
        Math.Round(value, 6, MidpointRounding.AwayFromZero);

    private static string TruncateControllerText(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value ?? "";

        return value[..maxLength];
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static bool DeduccionesIvaFileExtensionAllowed(string? extension) =>
        string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)
        || string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase)
        || string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase);

    private sealed record BillingDifferencesContext(
        DateOnly Start,
        DateOnly EndExclusive,
        IReadOnlyList<ReconciliationDataverseBillingRow> DataverseBilling,
        IReadOnlyList<ReconciliationDataverseCreditNoteRow> DataverseCreditNotes,
        SiigoFinancialReconciliationData Siigo,
        ConciliacionBillingDifferencesDto Differences);

    private sealed record SiigoSupplierEnsureResult(
        SiigoCustomerLookupItemDto Customer,
        bool ExistsInSiigo,
        bool Created,
        bool WouldCreate,
        object? Payload);

    private sealed record SupplierTaxId(string Identification, string CheckDigit);

    private sealed record SiigoCitySelection(
        string CountryCode,
        string StateCode,
        string CityCode,
        string Label,
        bool Matched);

    private sealed record DianSupplierPurchaseVatMatch(
        SiigoTaxLookupDto Tax,
        decimal TaxableBase,
        decimal NonTaxedBase,
        decimal Difference);

    private sealed record DianSupplierPurchaseItemDraft(
        string AccountCode,
        decimal Price,
        string Description,
        SiigoTaxLookupDto? Tax,
        Dictionary<string, object?> Payload);

    private sealed record PreparedDianSupplierPurchase(
        ConciliacionDianSupplierInvoiceRowDto Row,
        bool CanSend,
        string TargetEndpoint,
        object? Payload,
        string PayloadJson,
        IReadOnlyList<string> Issues);

    private sealed record PreparedCuentaCobroSupportDocument(
        ConciliacionCuentaCobroRowDto Row,
        bool CanSend,
        string TargetEndpoint,
        object? Payload,
        string PayloadJson,
        IReadOnlyList<string> Issues);

    private sealed record PreparedCuentaCobroPaymentReceipt(
        ConciliacionCuentaCobroRowDto Row,
        bool CanSend,
        string TargetEndpoint,
        object? Payload,
        string PayloadJson,
        IReadOnlyList<string> Issues);

    internal sealed record AllocatedSiigoInvoice(
        ConciliacionSiigoOpenInvoiceDto Invoice,
        decimal PaymentValue,
        decimal GrossValue,
        decimal AdjustmentValue,
        IReadOnlyList<AllocatedClientRetention> Retentions,
        ConciliacionSiigoSupplierLookupDto? Customer = null);

    internal sealed record AllocatedClientRetention(
        string Kind,
        string Label,
        int TaxId,
        string AccountCode,
        decimal Rate,
        decimal Value);

    private sealed record ResolvedClientPaymentAllocations(
        ConciliacionSiigoSupplierLookupDto? PrimaryCustomer,
        IReadOnlyList<AllocatedSiigoInvoice> Invoices);

    private sealed record PreparedClientInvoicePayment(
        ConciliacionCashFlowRowDto CashFlowRow,
        ConciliacionSiigoSupplierLookupDto? Customer,
        IReadOnlyList<AllocatedSiigoInvoice> Invoices,
        bool CanSend,
        string TargetEndpoint,
        object? Payload,
        string PayloadJson,
        IReadOnlyList<string> Issues);

    internal sealed record AllocatedSiigoPurchase(
        ConciliacionSiigoOpenPurchaseDto Purchase,
        ConciliacionSupplierPaymentAllocationRequest Allocation,
        decimal PaymentValue,
        decimal GrossValue,
        IReadOnlyList<AllocatedSupplierRetention> Retentions);

    internal sealed record AllocatedSupplierRetention(
        string Kind,
        string Label,
        int TaxId,
        string AccountCode,
        decimal Rate,
        decimal Value);

    private sealed record PreparedSupplierPayment(
        ConciliacionCashFlowRowDto Row,
        IReadOnlyList<AllocatedSiigoPurchase> Purchases,
        bool CanSend,
        string TargetEndpoint,
        object? Payload,
        string PayloadJson,
        IReadOnlyList<string> Issues);

    private sealed record PreparedAccountingVoucher(
        ConciliacionCashFlowRowDto Row,
        IReadOnlyList<ConciliacionCashFlowRowDto> Rows,
        bool CanSend,
        string TargetEndpoint,
        object? Payload,
        string PayloadJson,
        IReadOnlyList<string> Issues);

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
            var prepared = await PrepareClientPaymentForSiigoSendAsync(request.RecordId, ct);
            var totals = CalculatePreparedJournalTotals(prepared.PayloadJson);

            return Ok(new ConciliacionSiigoDryRunResultDto
            {
                Message = prepared.CanSend
                    ? "Simulacion correcta. El payload real esta completo y aun no se envio nada a Siigo."
                    : prepared.Message,
                IsReadyForSiigo = prepared.CanSend,
                TargetEndpoint = string.IsNullOrWhiteSpace(prepared.TargetEndpoint)
                    ? "DRY-RUN /v1/journals"
                    : $"DRY-RUN {prepared.TargetEndpoint}",
                PayloadJson = prepared.PayloadJson,
                LineCount = totals.LineCount,
                DebitTotal = totals.Debit,
                CreditTotal = totals.Credit,
                Issues = prepared.Issues,
                Row = prepared.Row
            });
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
            prepared = await PrepareClientPaymentForSiigoSendAsync(request.RecordId, ct);
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
            var siigoResult = await _siigo.CreateJournalAsync(
                prepared.Payload,
                BuildSiigoIdempotencyKey(request.RecordId),
                ct);
            var documentLabel = string.IsNullOrWhiteSpace(siigoResult.Name)
                ? siigoResult.Id
                : siigoResult.Name;
            var message = string.IsNullOrWhiteSpace(documentLabel)
                ? "Comprobante de ingreso enviado a Siigo."
                : $"Comprobante de ingreso enviado a Siigo: {documentLabel}.";
            var dataverseResult = await _dataverse.MarkConciliacionClientPaymentSiigoSendResultAsync(
                request.RecordId,
                success: true,
                message: message,
                siigoId: siigoResult.Id,
                siigoName: siigoResult.Name,
                responseJson: siigoResult.RawJson,
                ct: ct);

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

    private async Task<BillingDifferencesContext> BuildBillingDifferencesContextAsync(int year, int month, CancellationToken ct)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            throw new InvalidOperationException("El periodo de conciliacion financiera no es valido.");

        var start = new DateOnly(year, month, 1);
        var endExclusive = start.AddMonths(1);
        var dataverseBillingTask = _dataverse.GetFinancialReconciliationBillingRowsAsync(start, endExclusive, ct);
        var dataverseCreditNotesTask = _dataverse.GetFinancialReconciliationCreditNoteRowsAsync(start, endExclusive, ct);
        var siigoTask = _siigo.GetFinancialReconciliationDocumentsAsync(start, endExclusive.AddDays(-1), ct);

        await Task.WhenAll(dataverseBillingTask, dataverseCreditNotesTask, siigoTask);

        var dataverseBilling = dataverseBillingTask.Result;
        var dataverseCreditNotes = dataverseCreditNotesTask.Result;
        var siigo = siigoTask.Result;
        var importableSiigoInvoices = siigo.Invoices
            .Where(IsImportableBillingDifferenceInvoice)
            .GroupBy(static invoice => FirstNonEmpty(invoice.Id, invoice.Name), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static invoice => invoice.Date)
            .ThenBy(static invoice => invoice.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var dataverseIndex = BuildDataverseBillingDifferenceIndex(dataverseBilling);
        var matchedDataverseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingInDataverse = new List<ConciliacionBillingDifferenceRowDto>();
        var amountDifferences = new List<ConciliacionBillingDifferenceRowDto>();

        foreach (var invoice in importableSiigoInvoices)
        {
            var match = FindDataverseBillingDifferenceMatch(invoice, dataverseIndex, matchedDataverseIds);
            if (match is null)
            {
                missingInDataverse.Add(BuildMissingDataverseBillingDifferenceRow(invoice));
                continue;
            }

            matchedDataverseIds.Add(match.RecordId);
            var totalDifference = RoundCurrency(match.Total - ResolveBillingDifferenceSiigoGrossTotal(invoice));
            var vatDifference = RoundCurrency(match.Vat - invoice.Vat);
            if (HasBillingDifference(totalDifference) || HasBillingDifference(vatDifference))
                amountDifferences.Add(BuildAmountBillingDifferenceRow(invoice, match, totalDifference, vatDifference));
        }

        var onlyDataverse = dataverseBilling
            .Where(row => !matchedDataverseIds.Contains(row.RecordId))
            .OrderBy(static row => row.EmissionDate)
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .Select(BuildOnlyDataverseBillingDifferenceRow)
            .ToList();
        var periodLabel = ToBillingPeriodLabel(start);
        var generatedAt = DateTimeOffset.UtcNow;
        var totalDifferences = missingInDataverse.Count + onlyDataverse.Count + amountDifferences.Count;
        var differences = new ConciliacionBillingDifferencesDto
        {
            Year = year,
            Month = month,
            PeriodLabel = periodLabel,
            GeneratedAtDisplay = FormatSyncHealthDateTime(generatedAt),
            StatusLabel = totalDifferences == 0 ? "Sin diferencias" : "Con diferencias",
            StatusTone = totalDifferences == 0 ? "success" : "warning",
            MissingInDataverseCount = missingInDataverse.Count,
            OnlyDataverseCount = onlyDataverse.Count,
            AmountDifferenceCount = amountDifferences.Count,
            MissingInDataverse = missingInDataverse,
            OnlyDataverse = onlyDataverse,
            AmountDifferences = amountDifferences
        };

        return new BillingDifferencesContext(
            start,
            endExclusive,
            dataverseBilling,
            dataverseCreditNotes,
            siigo,
            differences);
    }

    private static ConciliacionBillingDifferenceActionResultDto BuildBillingDifferenceActionResult(
        string message,
        FinancialReconciliationCorrectionResult result,
        ConciliacionBillingDifferencesDto differences)
    {
        return new ConciliacionBillingDifferenceActionResultDto
        {
            Message = message,
            Applied = result.Applied,
            Errors = result.Errors,
            Actions = result.Actions.Select(static action => new ConciliacionBillingDifferenceActionDto
            {
                Entity = action.Entity,
                Action = action.Action,
                Document = action.Document,
                RecordId = action.RecordId,
                Notes = action.Notes
            }).ToList(),
            Differences = differences
        };
    }

    private static Dictionary<string, List<ReconciliationDataverseBillingRow>> BuildDataverseBillingDifferenceIndex(
        IEnumerable<ReconciliationDataverseBillingRow> rows)
    {
        var index = new Dictionary<string, List<ReconciliationDataverseBillingRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            foreach (var key in EnumerateDataverseBillingDifferenceKeys(row).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!index.TryGetValue(key, out var matches))
                {
                    matches = new List<ReconciliationDataverseBillingRow>();
                    index[key] = matches;
                }

                matches.Add(row);
            }
        }

        return index;
    }

    private static ReconciliationDataverseBillingRow? FindDataverseBillingDifferenceMatch(
        SiigoReconciliationInvoice invoice,
        IReadOnlyDictionary<string, List<ReconciliationDataverseBillingRow>> index,
        ISet<string> matchedDataverseIds)
    {
        foreach (var key in EnumerateSiigoBillingDifferenceKeys(invoice).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!index.TryGetValue(key, out var candidates))
                continue;

            var match = candidates.FirstOrDefault(row => !matchedDataverseIds.Contains(row.RecordId));
            if (match is not null)
                return match;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSiigoBillingDifferenceKeys(SiigoReconciliationInvoice invoice)
    {
        if (!string.IsNullOrWhiteSpace(invoice.Id))
            yield return $"id:{invoice.Id.Trim()}";

        var nameKey = NormalizeBillingDifferenceDocumentKey(invoice.Name);
        if (!string.IsNullOrWhiteSpace(nameKey))
            yield return $"doc:{nameKey}";

        var prefixNumberKey = BuildBillingDifferencePrefixNumberKey(invoice.Prefix, invoice.Number?.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(prefixNumberKey))
            yield return $"prefix:{prefixNumberKey}";
    }

    private static IEnumerable<string> EnumerateDataverseBillingDifferenceKeys(ReconciliationDataverseBillingRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.SiigoInvoiceId))
            yield return $"id:{row.SiigoInvoiceId.Trim()}";

        var siigoNameKey = NormalizeBillingDifferenceDocumentKey(row.SiigoInvoiceName);
        if (!string.IsNullOrWhiteSpace(siigoNameKey))
            yield return $"doc:{siigoNameKey}";

        var invoiceNameKey = NormalizeBillingDifferenceDocumentKey(row.InvoiceNumber);
        if (!string.IsNullOrWhiteSpace(invoiceNameKey))
            yield return $"doc:{invoiceNameKey}";

        var prefixNumberKey = BuildBillingDifferencePrefixNumberKey(row.InvoicePrefix, row.InvoiceCode);
        if (!string.IsNullOrWhiteSpace(prefixNumberKey))
            yield return $"prefix:{prefixNumberKey}";
    }

    private static ConciliacionBillingDifferenceRowDto BuildMissingDataverseBillingDifferenceRow(SiigoReconciliationInvoice invoice)
    {
        var siigoGrossTotal = ResolveBillingDifferenceSiigoGrossTotal(invoice);
        return new ConciliacionBillingDifferenceRowDto
        {
            Key = BuildBillingDifferenceSiigoSelectionKey(invoice),
            Source = "Siigo",
            StatusLabel = "Falta en Dataverse",
            StatusTone = "warning",
            SiigoInvoiceId = invoice.Id,
            InvoiceNumber = FirstNonEmpty(invoice.Name, BuildBillingDifferenceInvoiceName(invoice.Prefix, invoice.Number), invoice.Id),
            Prefix = invoice.Prefix,
            Number = invoice.Number?.ToString(CultureInfo.InvariantCulture) ?? "",
            DateValue = invoice.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            DateDisplay = FormatBillingDifferenceDate(invoice.Date),
            CustomerIdentification = invoice.CustomerIdentification,
            SiigoTotal = siigoGrossTotal,
            SiigoVat = invoice.Vat,
            Difference = RoundCurrency(0m - siigoGrossTotal),
            VatDifference = RoundCurrency(0m - invoice.Vat),
            CanCreateInDataverse = true
        };
    }

    private static ConciliacionBillingDifferenceRowDto BuildOnlyDataverseBillingDifferenceRow(ReconciliationDataverseBillingRow row)
    {
        return new ConciliacionBillingDifferenceRowDto
        {
            Key = row.RecordId,
            Source = "Dataverse",
            StatusLabel = "No existe en Siigo",
            StatusTone = "danger",
            RecordId = row.RecordId,
            SiigoInvoiceId = row.SiigoInvoiceId,
            InvoiceNumber = FirstNonEmpty(row.SiigoInvoiceName, row.InvoiceNumber, row.RecordId),
            Prefix = row.InvoicePrefix,
            Number = row.InvoiceCode,
            DateValue = row.EmissionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            DateDisplay = FormatBillingDifferenceDate(row.EmissionDate),
            CustomerIdentification = row.CompanyTaxId,
            ClientName = row.ClientName,
            DataverseTotal = row.Total,
            DataverseVat = row.Vat,
            Difference = row.Total,
            VatDifference = row.Vat,
            CanDeleteFromDataverse = true
        };
    }

    private static ConciliacionBillingDifferenceRowDto BuildAmountBillingDifferenceRow(
        SiigoReconciliationInvoice invoice,
        ReconciliationDataverseBillingRow row,
        decimal totalDifference,
        decimal vatDifference)
    {
        var siigoGrossTotal = ResolveBillingDifferenceSiigoGrossTotal(invoice);
        return new ConciliacionBillingDifferenceRowDto
        {
            Key = FirstNonEmpty(invoice.Id, row.RecordId, invoice.Name),
            Source = "Ambos",
            StatusLabel = HasBillingDifference(totalDifference) && HasBillingDifference(vatDifference)
                ? "Diferencia total e IVA"
                : HasBillingDifference(totalDifference) ? "Diferencia total" : "Diferencia IVA",
            StatusTone = "warning",
            RecordId = row.RecordId,
            SiigoInvoiceId = invoice.Id,
            InvoiceNumber = FirstNonEmpty(invoice.Name, row.InvoiceNumber, row.SiigoInvoiceName, invoice.Id),
            Prefix = FirstNonEmpty(invoice.Prefix, row.InvoicePrefix),
            Number = FirstNonEmpty(invoice.Number?.ToString(CultureInfo.InvariantCulture), row.InvoiceCode),
            DateValue = (invoice.Date ?? row.EmissionDate)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            DateDisplay = FormatBillingDifferenceDate(invoice.Date ?? row.EmissionDate),
            CustomerIdentification = FirstNonEmpty(invoice.CustomerIdentification, row.CompanyTaxId),
            ClientName = row.ClientName,
            SiigoTotal = siigoGrossTotal,
            DataverseTotal = row.Total,
            SiigoVat = invoice.Vat,
            DataverseVat = row.Vat,
            Difference = totalDifference,
            VatDifference = vatDifference
        };
    }

    private static HashSet<string> BuildControllerKeySet(IEnumerable<string> values)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? Array.Empty<string>())
        {
            var trimmed = value?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            result.Add(trimmed);
            var normalized = NormalizeBillingDifferenceDocumentKey(trimmed);
            if (!string.IsNullOrWhiteSpace(normalized))
                result.Add(normalized);
        }

        return result;
    }

    private static bool BillingDifferenceRowRequested(
        ConciliacionBillingDifferenceRowDto row,
        ISet<string> requestedKeys)
    {
        var keys = new[]
        {
            row.Key,
            row.SiigoInvoiceId,
            row.RecordId,
            row.InvoiceNumber,
            NormalizeBillingDifferenceDocumentKey(row.InvoiceNumber),
            BuildBillingDifferencePrefixNumberKey(row.Prefix, row.Number)
        };

        return keys.Any(key => !string.IsNullOrWhiteSpace(key) && requestedKeys.Contains(key));
    }

    private static bool IsImportableBillingDifferenceInvoice(SiigoReconciliationInvoice invoice) =>
        !invoice.Annulled
        && string.Equals(invoice.StampStatus?.Trim(), "Accepted", StringComparison.OrdinalIgnoreCase);

    private static decimal ResolveBillingDifferenceSiigoGrossTotal(SiigoReconciliationInvoice invoice)
    {
        var calculated = RoundCurrency(invoice.Total + invoice.SuggestedWithholdingTotal);
        return invoice.GrossTotal == 0m && calculated != 0m
            ? calculated
            : RoundCurrency(invoice.GrossTotal);
    }

    private static bool HasBillingDifference(decimal value) =>
        Math.Abs(value) > 1m;

    private static string BuildBillingDifferenceSiigoSelectionKey(SiigoReconciliationInvoice invoice) =>
        FirstNonEmpty(
            invoice.Id,
            invoice.Name,
            BuildBillingDifferencePrefixNumberKey(invoice.Prefix, invoice.Number?.ToString(CultureInfo.InvariantCulture)),
            invoice.Number?.ToString(CultureInfo.InvariantCulture));

    private static string BuildBillingDifferencePrefixNumberKey(string? prefix, string? number)
    {
        var normalizedPrefix = NormalizeBillingDifferenceDocumentKey(prefix);
        var normalizedNumber = NormalizeBillingDifferenceDocumentKey(number);
        return string.IsNullOrWhiteSpace(normalizedPrefix) || string.IsNullOrWhiteSpace(normalizedNumber)
            ? ""
            : $"{normalizedPrefix}:{normalizedNumber}";
    }

    private static string BuildBillingDifferenceInvoiceName(string? prefix, long? number)
    {
        if (!number.HasValue)
            return "";

        return string.IsNullOrWhiteSpace(prefix)
            ? number.Value.ToString(CultureInfo.InvariantCulture)
            : $"{prefix.Trim()}-{number.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string NormalizeBillingDifferenceDocumentKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToUpperInvariant(ch));
        }

        return builder.ToString();
    }

    private static string FormatBillingDifferenceDate(DateOnly? value) =>
        value.HasValue
            ? value.Value.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("es-CO"))
            : "";

    private static string ToBillingPeriodLabel(DateOnly start)
    {
        var culture = CultureInfo.GetCultureInfo("es-CO");
        return culture.TextInfo.ToTitleCase(start.ToString("MMMM yyyy", culture));
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

    private static void ApplyCashFlowMonthCloseStatus(
        ConciliacionBoardDto board,
        FinancialReconciliationSnapshotResult? snapshot,
        ConciliacionMonthValidationStateDto monthValidation,
        string snapshotError)
    {
        var rows = BuildCashFlowMonthCloseComparisons(board, snapshot, snapshotError);
        var issues = new List<string>();
        if (!string.IsNullOrWhiteSpace(snapshotError))
            issues.Add("No se pudo consultar el comparativo financiero Siigo/Dataverse.");

        var pendingRows = board.CashFlow.Rows.Count(IsCashFlowPendingForMonthClose);
        if (pendingRows > 0)
            issues.Add($"Hay {pendingRows:N0} registros del flujo de caja pendientes por conciliar.");

        var differenceRows = rows
            .Where(static row => !string.Equals(row.StatusLabel, "Referencia", StringComparison.OrdinalIgnoreCase)
                && Math.Abs(row.DifferenceValue) > 1m)
            .ToArray();
        if (differenceRows.Length > 0)
            issues.Add($"Hay {differenceRows.Length:N0} comparativos con diferencia.");

        var canValidate = issues.Count == 0;
        board.CashFlow.MonthCloseComparisons = rows;
        board.CashFlow.MonthCloseIssues = issues;
        board.CashFlow.CanValidateMonth = canValidate;
        board.CashFlow.MonthValidated = monthValidation.IsValidated;

        if (monthValidation.IsValidated && canValidate)
        {
            board.CashFlow.MonthValidationLabel = "Mes validado";
            board.CashFlow.MonthValidationTone = "success";
            board.CashFlow.MonthValidationDetail = string.IsNullOrWhiteSpace(monthValidation.ValidatedOnDisplay)
                ? "Cierre marcado manualmente."
                : $"Validado el {monthValidation.ValidatedOnDisplay}.";
        }
        else if (monthValidation.IsValidated)
        {
            board.CashFlow.MonthValidationLabel = "Validado con cambios";
            board.CashFlow.MonthValidationTone = "danger";
            board.CashFlow.MonthValidationDetail = "El mes ya estaba validado, pero ahora hay diferencias o pendientes nuevos.";
        }
        else if (canValidate)
        {
            board.CashFlow.MonthValidationLabel = "Listo para validar";
            board.CashFlow.MonthValidationTone = "success";
            board.CashFlow.MonthValidationDetail = "Todos los comparativos estan en cero y no hay pendientes.";
        }
        else
        {
            board.CashFlow.MonthValidationLabel = "Pendiente";
            board.CashFlow.MonthValidationTone = "warning";
            board.CashFlow.MonthValidationDetail = string.Join(" ", issues);
        }
    }

    private static IReadOnlyList<ConciliacionCashFlowComparisonRowDto> BuildCashFlowMonthCloseComparisons(
        ConciliacionBoardDto board,
        FinancialReconciliationSnapshotResult? snapshot,
        string snapshotError)
    {
        var rows = new List<ConciliacionCashFlowComparisonRowDto>();
        if (snapshot is not null)
        {
            var summary = snapshot.Summary;
            rows.Add(BuildComparisonRow(
                "Facturacion",
                siigo: summary.SiigoBillingGross,
                dataverse: summary.DataverseBillingGross,
                cashFlow: null,
                detail: $"Facturas Siigo {summary.SiigoBillingInvoiceCount:N0} / Dataverse {summary.DataverseBillingInvoiceCount:N0}."));
            rows.Add(BuildComparisonRow(
                "NC",
                siigo: summary.SiigoBillingCreditNotes,
                dataverse: summary.DataverseBillingCreditNotes,
                cashFlow: null,
                detail: $"Notas credito Siigo {summary.SiigoBillingCreditNoteCount:N0} / Dataverse {summary.DataverseBillingCreditNoteCount:N0}."));
            rows.Add(BuildComparisonRow(
                "FE Registros facturas",
                siigo: summary.SiigoExpenses,
                dataverse: summary.PowerAppsExpenses,
                cashFlow: null,
                detail: $"Documentos proveedor Siigo {summary.SiigoExpenseCount:N0} / Dataverse {summary.PowerAppsExpenseCount:N0}."));
        }
        else
        {
            rows.Add(new ConciliacionCashFlowComparisonRowDto
            {
                Concept = "Facturacion / NC / FE Registros facturas",
                StatusLabel = "Sin consultar",
                StatusTone = "warning",
                Detail = string.IsNullOrWhiteSpace(snapshotError)
                    ? "No se consulto Siigo/Dataverse para el comparativo financiero."
                    : snapshotError
            });
        }

        var entradaFeRows = board.CashFlow.Rows
            .Where(static row => string.Equals(row.DetectedTypeKey, "entrada-fe", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        rows.Add(BuildComparisonRow(
            "FE Entrada",
            siigo: entradaFeRows.Where(IsCashFlowReportedForMonthClose).Sum(static row => row.EntryValue),
            dataverse: null,
            cashFlow: entradaFeRows.Sum(static row => row.EntryValue),
            detail: "Pagos de clientes detectados en flujo de caja contra registros reportados/manuales en Siigo."));

        var entradaComprobanteRows = board.CashFlow.Rows
            .Where(static row => string.Equals(row.Direction, "Entrada", StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.DetectedTypeKey, "entrada-comprobante", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        rows.Add(BuildComparisonRow(
            "Entradas - Comprobantes contables",
            siigo: entradaComprobanteRows.Where(IsCashFlowReportedForMonthClose).Sum(static row => row.EntryValue),
            dataverse: null,
            cashFlow: entradaComprobanteRows.Sum(static row => row.EntryValue),
            detail: "Entradas bancarias sin factura, incluidos traslados internos, contra comprobantes contables reportados."));

        rows.Add(BuildOutgoingComparison(board, "FE Salidas - Cuentas de cobro", "cuenta-cobro"));
        rows.Add(BuildOutgoingComparison(board, "FE Salidas - Comprobantes contables", "comprobante-contable", "entrada-comprobante"));
        rows.Add(BuildOutgoingComparison(board, "FE Salidas - Facturas", "salida-fe"));

        return rows;
    }

    private static ConciliacionCashFlowComparisonRowDto BuildOutgoingComparison(
        ConciliacionBoardDto board,
        string concept,
        params string[] categoryKeys)
    {
        var keySet = categoryKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = board.CashFlow.Rows
            .Where(row => string.Equals(row.Direction, "Salida", StringComparison.OrdinalIgnoreCase)
                && keySet.Contains(row.DetectedTypeKey))
            .ToArray();

        return BuildComparisonRow(
            concept,
            siigo: rows.Where(IsCashFlowReportedForMonthClose).Sum(static row => row.ExitValue),
            dataverse: null,
            cashFlow: rows.Sum(static row => row.ExitValue),
            detail: "Salidas del flujo de caja contra pagos/comprobantes reportados o marcados manualmente.");
    }

    private static ConciliacionCashFlowComparisonRowDto BuildComparisonRow(
        string concept,
        decimal? siigo,
        decimal? dataverse,
        decimal? cashFlow,
        string detail)
    {
        var difference = 0m;
        var differenceLabel = "Referencia";
        if (siigo.HasValue && dataverse.HasValue)
        {
            difference = dataverse.Value - siigo.Value;
            differenceLabel = "Dataverse - Siigo";
        }
        else if (siigo.HasValue && cashFlow.HasValue)
        {
            difference = cashFlow.Value - siigo.Value;
            differenceLabel = "Flujo caja - Siigo";
        }
        else if (dataverse.HasValue && cashFlow.HasValue)
        {
            difference = cashFlow.Value - dataverse.Value;
            differenceLabel = "Flujo caja - Dataverse";
        }

        var hasComparison = !string.Equals(differenceLabel, "Referencia", StringComparison.OrdinalIgnoreCase);
        var hasDifference = hasComparison && Math.Abs(difference) > 1m;
        return new ConciliacionCashFlowComparisonRowDto
        {
            Concept = concept,
            ShowSiigo = siigo.HasValue,
            SiigoValue = siigo.GetValueOrDefault(),
            ShowDataverse = dataverse.HasValue,
            DataverseValue = dataverse.GetValueOrDefault(),
            ShowCashFlow = cashFlow.HasValue,
            CashFlowValue = cashFlow.GetValueOrDefault(),
            DifferenceValue = difference,
            DifferenceLabel = differenceLabel,
            StatusLabel = hasComparison ? hasDifference ? "Diferencia" : "OK" : "Referencia",
            StatusTone = hasComparison ? hasDifference ? "danger" : "success" : "neutral",
            Detail = detail
        };
    }

    private static bool IsCashFlowPendingForMonthClose(ConciliacionCashFlowRowDto row)
    {
        if (IsCashFlowNoIncludedForMonthClose(row))
            return false;

        if (string.Equals(row.SourceKind, "Traslado", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.DetectedTypeKey, "traslado-interno", StringComparison.OrdinalIgnoreCase))
        {
            return !IsCashFlowReportedForMonthClose(row);
        }

        return !IsCashFlowReportedForMonthClose(row)
            || row.ValidationStatus.Contains("Pendiente", StringComparison.OrdinalIgnoreCase)
            || row.ValidationStatus.Contains("Revisar", StringComparison.OrdinalIgnoreCase)
            || row.RegistrationStatus.Contains("pendiente", StringComparison.OrdinalIgnoreCase)
            || row.SiigoPaymentStatus.Contains("Pendiente", StringComparison.OrdinalIgnoreCase)
            || row.RegistrationStatus.Contains("Cambio", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCashFlowReportedForMonthClose(ConciliacionCashFlowRowDto row)
    {
        return row.RegistrationStatus.Contains("Siigo OK", StringComparison.OrdinalIgnoreCase)
            || row.RegistrationStatus.Contains("no aplica Siigo", StringComparison.OrdinalIgnoreCase)
            || row.SiigoDocumentStatus.Contains("Siigo OK", StringComparison.OrdinalIgnoreCase)
            || row.SiigoPaymentStatus.Contains("Enviado", StringComparison.OrdinalIgnoreCase)
            || row.SiigoPaymentStatus.Contains("detectado", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.SiigoStatus, "EnviadoSiigo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.SiigoStatus, "Conciliado", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCashFlowNoIncludedForMonthClose(ConciliacionCashFlowRowDto row) =>
        string.Equals(row.DetectedTypeKey, "no-incluida-conciliacion", StringComparison.OrdinalIgnoreCase);

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

    internal const string TransientSiigoUserMessage =
        "Siigo no esta disponible temporalmente. La conciliacion no pudo finalizar. Espera unos minutos y vuelve a intentarlo.";

    private object CreateErrorPayload(string message, Exception? ex = null)
    {
        var detail = BuildExceptionDetail(ex);
        var isTransientSiigoFailure = IsTransientSiigoFailure(detail);
        return new
        {
            message = ResolveSiigoUserMessage(detail, message),
            detail = isTransientSiigoFailure || string.Equals(detail, message, StringComparison.Ordinal) ? "" : detail,
            traceId = HttpContext.TraceIdentifier,
            provider = isTransientSiigoFailure ? "Siigo" : "",
            isTransientSiigoFailure
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

    internal static string ResolveSiigoUserMessage(string detail, string fallbackMessage) =>
        IsTransientSiigoFailure(detail) ? TransientSiigoUserMessage : fallbackMessage;

    internal static bool IsTransientSiigoFailure(string detail) =>
        detail.Contains("document_query_service", StringComparison.OrdinalIgnoreCase)
        || detail.Contains("currently unavailable", StringComparison.OrdinalIgnoreCase)
        || detail.Contains("respondio 408", StringComparison.OrdinalIgnoreCase)
        || detail.Contains("respondio 429", StringComparison.OrdinalIgnoreCase)
        || detail.Contains("respondio 500", StringComparison.OrdinalIgnoreCase)
        || detail.Contains("respondio 502", StringComparison.OrdinalIgnoreCase)
        || detail.Contains("respondio 503", StringComparison.OrdinalIgnoreCase)
        || detail.Contains("respondio 504", StringComparison.OrdinalIgnoreCase);

    private static bool IsAmbiguousSupplierCreateFailure(Exception exception)
    {
        if (exception is SiigoSupplierCreateException { IsAmbiguous: true })
            return true;

        if (exception is HttpRequestException or TaskCanceledException or TimeoutException)
            return true;

        var detail = BuildExceptionDetail(exception);
        return IsTransientSiigoFailure(detail)
            || detail.Contains("creo el tercero", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("created the customer", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("interpretar la respuesta", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("conexion", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDianSupplierCreationWriteHold(ConciliacionDianSupplierInvoiceRowDto row) =>
        row.AutomationState.Equals("ProcesandoProveedorSiigo", StringComparison.OrdinalIgnoreCase)
        || row.AutomationState.Equals("VerificacionProveedorSiigoPendiente", StringComparison.OrdinalIgnoreCase)
        || row.AutomationState.Equals("ProveedorSiigoAsociado", StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrWhiteSpace(row.SiigoSupplierId)
        || row.ReviewReason.Contains("[SIIGO_SUPPLIER_WRITE_AMBIGUOUS]", StringComparison.OrdinalIgnoreCase);

    internal static string BuildSiigoIdempotencyKey(string recordId)
    {
        var canonical = (recordId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(canonical))
            throw new InvalidOperationException("No se puede crear una clave idempotente sin identidad canonica.");

        var digest = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
        return $"CNCJ{digest[..26]}";
    }

    private static string CombinePayloads(string supportDocumentPayloadJson, string paymentPayloadJson) =>
        JsonSerializer.Serialize(new
        {
            supportDocument = TryParseJsonElement(supportDocumentPayloadJson),
            paymentReceipt = TryParseJsonElement(paymentPayloadJson)
        }, new JsonSerializerOptions { WriteIndented = true });

    private static string CombineSiigoResponses(string supportDocumentResponseJson, string paymentResponseJson) =>
        JsonSerializer.Serialize(new
        {
            supportDocument = TryParseJsonElement(supportDocumentResponseJson),
            paymentReceipt = TryParseJsonElement(paymentResponseJson)
        }, new JsonSerializerOptions { WriteIndented = true });

    private static object? TryParseJsonElement(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return value;
        }
    }

    private async Task<PreparedAccountingVoucher> PrepareAccountingVoucherForSiigoAsync(
        ConciliacionCashFlowAccountingVoucherRequest request,
        CancellationToken ct)
    {
        var rows = (await _dataverse.GetConciliacionCashFlowMovementsAsync(request, ct))
            .OrderBy(static row => row.MovementDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.SourceRowNumber)
            .ThenBy(static row => row.Description, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var row = rows.FirstOrDefault()
            ?? throw new InvalidOperationException("No encontramos la fila del flujo de caja.");
        var issues = new List<string>();
        var targetEndpoint = "/v1/journals";
        var isGrouped = rows.Length > 1 || !string.IsNullOrWhiteSpace(request.GroupKey);
        var isInternalTransfer = rows.All(static item =>
            string.Equals(item.SourceKind, "Traslado", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.DetectedTypeKey, "traslado-interno", StringComparison.OrdinalIgnoreCase));

        var isEntry = rows.All(static item => string.Equals(item.Direction, "Entrada", StringComparison.OrdinalIgnoreCase) || item.EntryValue > 0m);
        var isExit = rows.All(static item => string.Equals(item.Direction, "Salida", StringComparison.OrdinalIgnoreCase) || item.ExitValue > 0m);
        if (!isInternalTransfer && !isEntry && !isExit)
            issues.Add("Los movimientos deben ser todos de entrada o todos de salida para armar comprobante contable.");
        if (!isInternalTransfer
            && rows.Any(static item =>
                !string.Equals(item.DetectedTypeKey, "comprobante-contable", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(item.DetectedTypeKey, "entrada-comprobante", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add("Todas las filas deben estar clasificadas como comprobante contable.");
        }
        if (RoundCurrency(rows.Sum(static item => item.Amount)) <= 0m)
            issues.Add("El comprobante no tiene valor para enviar a Siigo.");
        if (rows.Any(static item => string.IsNullOrWhiteSpace(item.BankAccountCode)))
            issues.Add("Hay movimientos sin cuenta contable del banco.");
        var bankAccountCount = rows
            .Select(static item => item.BankAccountCode)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (!isInternalTransfer && bankAccountCount > 1)
            issues.Add("El comprobante agrupado no puede mezclar cuentas contables de banco distintas.");
        if (rows.Any(static item => string.IsNullOrWhiteSpace(item.AccountCode)))
            issues.Add("Selecciona la cuenta contable de todas las lineas antes de enviarlo.");
        var dates = rows
            .Select(static item => DateOnly.TryParseExact(item.MovementDateValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : (DateOnly?)null)
            .ToArray();
        if (dates.Any(static date => !date.HasValue))
            issues.Add("Todas las fechas del comprobante deben tener formato valido para Siigo.");
        var movementDate = ResolveAccountingVoucherDate(
            dates.Where(static date => date.HasValue).Select(static date => date!.Value).ToArray(),
            isGrouped);
        if (rows.Any(static item =>
            !string.IsNullOrWhiteSpace(item.SiigoDocumentId)
            || string.Equals(item.SiigoStatus, "EnviadoSiigo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.SiigoStatus, "Conciliado", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add("Una o mas lineas ya aparecen enviadas o conciliadas en Siigo.");
        }

        ConciliacionSiigoSupplierLookupDto? selectedThirdParty = null;
        ConciliacionSiigoSupplierLookupDto? internalTransferThirdParty = null;
        if (isInternalTransfer)
        {
            internalTransferThirdParty = await ResolveInternalTransferThirdPartyAsync(issues, ct);
        }
        if (!isInternalTransfer && isExit)
        {
            selectedThirdParty = await ResolveAccountingVoucherThirdPartyAsync(
                request,
                rows,
                issues,
                ct);
        }

        SiigoDocumentTypeLookupDto? document = null;
        try
        {
            var documentTypes = await _siigo.GetDocumentTypesAsync("CC", ct);
            document = isInternalTransfer || isEntry
                ? ResolveIncomeJournalDocumentType(documentTypes)
                : ResolveExpenseJournalDocumentType(documentTypes);
        }
        catch (Exception ex)
        {
            issues.Add(BuildExceptionDetail(ex));
        }

        if (issues.Count == 0 && selectedThirdParty is not null)
        {
            try
            {
                await PersistAccountingVoucherThirdPartyAsync(rows, selectedThirdParty, ct);
            }
            catch (Exception ex)
            {
                issues.Add($"No fue posible guardar y verificar el tercero real en Dataverse. {BuildExceptionDetail(ex)}");
            }
        }

        object? payload = null;
        var payloadJson = "";
        if (issues.Count == 0 && document is not null)
        {
            payload = isInternalTransfer
                ? BuildInternalTransferAccountingVoucherPayload(
                    rows,
                    document,
                    movementDate,
                    request.GroupLabel,
                    internalTransferThirdParty!)
                : isGrouped
                ? BuildGroupedAccountingVoucherPayload(rows, document, movementDate, isEntry, request.GroupLabel, selectedThirdParty)
                : BuildAccountingVoucherPayload(row, document, movementDate, isEntry, selectedThirdParty);
            payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        }

        return new PreparedAccountingVoucher(
            row,
            rows,
            issues.Count == 0 && payload is not null,
            targetEndpoint,
            payload,
            payloadJson,
            issues);
    }

    private async Task PersistAccountingVoucherThirdPartyAsync(
        IReadOnlyList<ConciliacionCashFlowRowDto> rows,
        ConciliacionSiigoSupplierLookupDto selectedThirdParty,
        CancellationToken ct)
    {
        var thirdPartyName = FirstNonEmpty(
            selectedThirdParty.CommercialName,
            selectedThirdParty.Name,
            selectedThirdParty.DisplayName);
        if (string.IsNullOrWhiteSpace(thirdPartyName))
            throw new InvalidOperationException("El tercero validado en Siigo no tiene nombre.");

        foreach (var row in rows)
        {
            await _dataverse.UpdateConciliacionCashFlowAccountingAccountAsync(
                new ConciliacionCashFlowAccountingAccountRequest
                {
                    RecordId = row.RecordId,
                    SourceKind = row.SourceKind,
                    MovementExternalKey = row.ExternalKey,
                    AccountCode = row.AccountCode,
                    ThirdPartyId = selectedThirdParty.Id,
                    ThirdPartyIdentification = selectedThirdParty.Identification,
                    ThirdPartyName = thirdPartyName,
                    ThirdPartyBranchOffice = selectedThirdParty.BranchOffice
                },
                ct);

            row.ThirdPartyId = selectedThirdParty.Id;
            row.ThirdPartyIdentification = selectedThirdParty.Identification;
            row.ThirdPartyName = thirdPartyName;
            row.ThirdPartyBranchOffice = selectedThirdParty.BranchOffice;
        }
    }

    private async Task<ConciliacionSiigoSupplierLookupDto?> ResolveAccountingVoucherThirdPartyAsync(
        ConciliacionCashFlowAccountingVoucherRequest request,
        IReadOnlyList<ConciliacionCashFlowRowDto> rows,
        ICollection<string> issues,
        CancellationToken ct)
    {
        var persistedRows = rows
            .Where(static row =>
                !string.IsNullOrWhiteSpace(row.ThirdPartyId)
                || !string.IsNullOrWhiteSpace(row.ThirdPartyIdentification)
                || !string.IsNullOrWhiteSpace(row.ThirdPartyName))
            .ToArray();
        var persistedIds = persistedRows
            .Select(static row => row.ThirdPartyId.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (persistedIds.Length > 1)
        {
            issues.Add("El comprobante agrupado mezcla terceros de Siigo diferentes. Separa los movimientos por tercero.");
            return null;
        }

        var persistedIdentifications = persistedRows
            .Select(static row => ExtractDigits(row.ThirdPartyIdentification))
            .Where(static value => value.Length >= 3)
            .ToArray();
        if (persistedIdentifications.Skip(1).Any(value =>
            !IsSameSupplierIdentificationDigits(persistedIdentifications[0], value)))
        {
            issues.Add("El comprobante agrupado mezcla identificaciones de terceros diferentes. Separa los movimientos por tercero.");
            return null;
        }

        var selectedId = FirstNonEmpty(request.ThirdPartyId, persistedRows.FirstOrDefault()?.ThirdPartyId);
        var selectedIdentification = FirstNonEmpty(
            request.ThirdPartyIdentification,
            persistedRows.FirstOrDefault()?.ThirdPartyIdentification);
        var selectedName = FirstNonEmpty(request.ThirdPartyName, persistedRows.FirstOrDefault()?.ThirdPartyName);
        var selectedBranchOffice = !string.IsNullOrWhiteSpace(request.ThirdPartyId)
            || !string.IsNullOrWhiteSpace(request.ThirdPartyIdentification)
            ? Math.Max(0, request.ThirdPartyBranchOffice)
            : Math.Max(0, persistedRows.FirstOrDefault()?.ThirdPartyBranchOffice ?? 0);
        var selectedIdentificationDigits = ExtractDigits(selectedIdentification);

        if (string.IsNullOrWhiteSpace(selectedId)
            || selectedIdentificationDigits.Length < 3
            || string.IsNullOrWhiteSpace(selectedName))
        {
            issues.Add("Selecciona obligatoriamente el tercero real de Siigo (ID, identificacion y nombre) para el comprobante de egreso.");
            return null;
        }

        if (persistedRows.Any(row =>
            (!string.IsNullOrWhiteSpace(row.ThirdPartyId)
                && !string.Equals(row.ThirdPartyId.Trim(), selectedId, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(row.ThirdPartyIdentification)
                && !IsSameSupplierIdentificationDigits(
                    ExtractDigits(row.ThirdPartyIdentification),
                    selectedIdentificationDigits))))
        {
            issues.Add("El tercero seleccionado no coincide con el tercero guardado en todas las lineas del comprobante.");
            return null;
        }

        try
        {
            var candidates = await _siigo.SearchCustomersAsync(selectedIdentificationDigits, top: 50, ct);
            var exact = candidates.FirstOrDefault(candidate =>
                candidate.Active
                && string.Equals(candidate.Id?.Trim(), selectedId, StringComparison.OrdinalIgnoreCase)
                && IsSameSupplierIdentificationDigits(
                    ExtractDigits(candidate.Identification),
                    selectedIdentificationDigits)
                && candidate.BranchOffice == selectedBranchOffice);
            if (exact is null)
            {
                issues.Add("El tercero seleccionado ya no esta activo en Siigo o no coincide exactamente con su ID, identificacion y sucursal.");
                return null;
            }

            return MapSupplierLookup(exact);
        }
        catch (Exception ex)
        {
            issues.Add($"No fue posible revalidar el tercero seleccionado en Siigo. {BuildExceptionDetail(ex)}");
            return null;
        }
    }

    private async Task<ConciliacionSiigoSupplierLookupDto?> ResolveInternalTransferThirdPartyAsync(
        ICollection<string> issues,
        CancellationToken ct)
    {
        try
        {
            var candidates = await _siigo.SearchCustomersAsync(
                InternalTransferBancolombiaIdentification,
                top: 30,
                ct);
            var bancolombia = candidates.FirstOrDefault(candidate =>
                candidate.Active
                && IsSameSupplierIdentificationDigits(
                    ExtractDigits(candidate.Identification),
                    InternalTransferBancolombiaIdentification)
                && NormalizeAccountingVoucherText(
                        FirstNonEmpty(candidate.CommercialName, candidate.Name, candidate.Identification))
                    .Contains("BANCOLOMBIA", StringComparison.OrdinalIgnoreCase));
            if (bancolombia is null)
            {
                issues.Add(
                    $"No encontre en Siigo el tercero activo Bancolombia con NIT {InternalTransferBancolombiaIdentification}.");
                return null;
            }

            return MapSupplierLookup(bancolombia);
        }
        catch (Exception ex)
        {
            issues.Add($"No fue posible validar el tercero Bancolombia en Siigo. {BuildExceptionDetail(ex)}");
            return null;
        }
    }

    internal static object BuildInternalTransferAccountingVoucherPayload(
        IReadOnlyList<ConciliacionCashFlowRowDto> rows,
        SiigoDocumentTypeLookupDto document,
        DateOnly movementDate,
        string groupLabel,
        ConciliacionSiigoSupplierLookupDto thirdParty)
    {
        var row = rows.First();
        var amount = RoundCurrency(rows.Count == 1
            ? row.Amount
            : rows.Max(static item => item.Amount));
        var isEntry = row.EntryValue > 0m || string.Equals(row.Direction, "Entrada", StringComparison.OrdinalIgnoreCase);
        var sourceCode = isEntry ? row.AccountCode : row.BankAccountCode;
        var sourceName = isEntry ? row.AccountName : row.BankAccountName;
        var destinationCode = isEntry ? row.BankAccountCode : row.AccountCode;
        var destinationName = isEntry ? row.BankAccountName : row.AccountName;
        var thirdPartyPayload = new Dictionary<string, object?>
        {
            ["identification"] = ExtractDigits(thirdParty.Identification),
            ["branch_office"] = Math.Max(0, thirdParty.BranchOffice)
        };
        var detail = TruncateControllerText(
            FirstNonEmpty(groupLabel, row.Description, $"Traslado interno {sourceName} a {destinationName}"),
            200);

        return new
        {
            document = new { id = document.Id },
            date = movementDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            items = new[]
            {
                new Dictionary<string, object?>
                {
                    ["account"] = new { code = sourceCode, movement = "Credit" },
                    ["customer"] = thirdPartyPayload,
                    ["description"] = TruncateControllerText(FirstNonEmpty(sourceName, sourceCode, "Banco origen"), 200),
                    ["detail"] = detail,
                    ["value"] = amount
                },
                new Dictionary<string, object?>
                {
                    ["account"] = new { code = destinationCode, movement = "Debit" },
                    ["customer"] = thirdPartyPayload,
                    ["description"] = TruncateControllerText(FirstNonEmpty(destinationName, destinationCode, "Banco destino"), 200),
                    ["detail"] = detail,
                    ["value"] = amount
                }
            },
            observations = TruncateControllerText(
                $"Conciliacion traslado interno. {row.SourceFlow} fila Excel {row.SourceRowNumber}. {row.Description}. Clave {row.ExternalKey}.".Trim(),
                500)
        };
    }

    internal static object BuildAccountingVoucherPayload(
        ConciliacionCashFlowRowDto row,
        SiigoDocumentTypeLookupDto document,
        DateOnly movementDate,
        bool isEntry,
        ConciliacionSiigoSupplierLookupDto? selectedThirdParty)
    {
        var amount = RoundCurrency(row.Amount);
        var thirdParty = new Dictionary<string, object?>
        {
            ["identification"] = selectedThirdParty is null
                ? ResolveAccountingVoucherThirdPartyIdentification(row)
                : ExtractDigits(selectedThirdParty.Identification),
            ["branch_office"] = selectedThirdParty?.BranchOffice ?? 0
        };
        var detail = TruncateControllerText(FirstNonEmpty(row.Description, row.Recipient, "Comprobante contable"), 200);
        var bankDescription = TruncateControllerText(FirstNonEmpty(row.BankAccountName, row.BankAccountCode, "Banco"), 200);
        var accountDescription = TruncateControllerText(FirstNonEmpty(row.AccountName, row.AccountCode, detail), 200);
        var bankMovement = isEntry ? "Debit" : "Credit";
        var selectedAccountMovement = isEntry ? "Credit" : "Debit";

        return new
        {
            document = new { id = document.Id },
            date = movementDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            items = new[]
            {
                new Dictionary<string, object?>
                {
                    ["account"] = new { code = row.BankAccountCode, movement = bankMovement },
                    ["customer"] = thirdParty,
                    ["description"] = bankDescription,
                    ["detail"] = detail,
                    ["value"] = amount
                },
                new Dictionary<string, object?>
                {
                    ["account"] = new { code = row.AccountCode, movement = selectedAccountMovement },
                    ["customer"] = thirdParty,
                    ["description"] = accountDescription,
                    ["detail"] = detail,
                    ["value"] = amount
                }
            },
            observations = TruncateControllerText(
                $"Conciliacion flujo de caja. {row.SourceFlow} fila Excel {row.SourceRowNumber}. {row.Description}. Cuenta {row.AccountCode} - {row.AccountName}. Clave {row.ExternalKey}.".Trim(),
                500)
        };
    }

    private static DateOnly ResolveAccountingVoucherDate(IReadOnlyList<DateOnly> dates, bool isGrouped)
    {
        if (dates.Count == 0)
            return DateOnly.FromDateTime(DateTime.Today);

        var maxDate = dates.Max();
        return isGrouped
            ? new DateOnly(maxDate.Year, maxDate.Month, DateTime.DaysInMonth(maxDate.Year, maxDate.Month))
            : maxDate;
    }

    private static object BuildGroupedAccountingVoucherPayload(
        IReadOnlyList<ConciliacionCashFlowRowDto> rows,
        SiigoDocumentTypeLookupDto document,
        DateOnly movementDate,
        bool isEntry,
        string groupLabel,
        ConciliacionSiigoSupplierLookupDto? selectedThirdParty)
    {
        var first = rows.First();
        var amount = RoundCurrency(rows.Sum(static row => row.Amount));
        var thirdParty = new Dictionary<string, object?>
        {
            ["identification"] = selectedThirdParty is null
                ? ResolveAccountingVoucherThirdPartyIdentification(first)
                : ExtractDigits(selectedThirdParty.Identification),
            ["branch_office"] = selectedThirdParty?.BranchOffice ?? 0
        };
        var bankMovement = isEntry ? "Debit" : "Credit";
        var selectedAccountMovement = isEntry ? "Credit" : "Debit";
        var label = TruncateControllerText(FirstNonEmpty(groupLabel, ResolveAccountingVoucherConcept(first).Label, "Comprobante contable mensual"), 200);
        var bankDescription = TruncateControllerText(FirstNonEmpty(first.BankAccountName, first.BankAccountCode, "Banco"), 200);
        var detail = TruncateControllerText($"{label}. Acumulado mensual {movementDate:yyyy-MM}.", 200);
        var items = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["account"] = new { code = first.BankAccountCode, movement = bankMovement },
                ["customer"] = thirdParty,
                ["description"] = bankDescription,
                ["detail"] = detail,
                ["value"] = amount
            }
        };

        foreach (var group in rows.GroupBy(static row =>
                 {
                     var concept = ResolveAccountingVoucherConcept(row);
                     return string.Join("|", concept.Key, concept.Label, row.AccountCode, row.AccountName);
                 }, StringComparer.OrdinalIgnoreCase))
        {
            var lineRow = group.First();
            var concept = ResolveAccountingVoucherConcept(lineRow);
            var lineAmount = RoundCurrency(group.Sum(static row => row.Amount));
            items.Add(new Dictionary<string, object?>
            {
                ["account"] = new { code = lineRow.AccountCode, movement = selectedAccountMovement },
                ["customer"] = thirdParty,
                ["description"] = TruncateControllerText(FirstNonEmpty(lineRow.AccountName, lineRow.AccountCode, concept.Label), 200),
                ["detail"] = TruncateControllerText($"{concept.Label}. {group.Count():N0} movimiento(s).", 200),
                ["value"] = lineAmount
            });
        }

        var excelRows = string.Join(", ", rows
            .Select(static row => row.SourceRowNumber)
            .Where(static value => value > 0)
            .Distinct()
            .Take(30));
        var concepts = string.Join("; ", rows
            .Select(static row => ResolveAccountingVoucherConcept(row).Label)
            .Distinct(StringComparer.OrdinalIgnoreCase));

        return new
        {
            document = new { id = document.Id },
            date = movementDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            items,
            observations = TruncateControllerText(
                $"Conciliacion cierre mensual. {label}. {rows.Count:N0} movimiento(s). {first.SourceFlow}. Conceptos: {concepts}. Filas Excel: {excelRows}.".Trim(),
                500)
        };
    }

    private static (string Key, string Label) ResolveAccountingVoucherConcept(ConciliacionCashFlowRowDto row)
    {
        var text = NormalizeAccountingVoucherText(string.Join(" ", new[]
        {
            row.Description,
            row.Recipient,
            row.DestinationBank,
            row.DocumentType,
            row.Observations,
            row.ExcelMovementType,
            row.BankAccountName,
            row.SourceFlow
        }));

        if (ContainsAccountingVoucherAll(text, "IVA", "COMISION", "TRASLADO")
            && ContainsAccountingVoucherAny(text, "OTROS BANCOS", "OTRO BANCO"))
            return ("iva-comision-traslado-otros-bancos", "IVA comision traslado otros bancos");

        if (ContainsAccountingVoucherAll(text, "COMISION", "TRASLADO")
            && ContainsAccountingVoucherAny(text, "OTROS BANCOS", "OTRO BANCO"))
            return ("comision-traslado-otros-bancos", "Comision traslado otros bancos");

        if (ContainsAccountingVoucherAll(text, "AJUSTE", "INTERES")
            && ContainsAccountingVoucherAny(text, "AHORRO", "AHORROS"))
            return ("ajuste-intereses-ahorros", "Ajuste intereses ahorros");

        if (ContainsAccountingVoucherAny(text, "4X1000", "4 X 1000", "GMF", "GRAVAMEN"))
            return ("impuesto-4x1000", "Impuesto 4x1000");

        if (ContainsAccountingVoucherAll(text, "CUOTA", "MANEJO"))
            return ("cuota-manejo", "Cuota manejo");

        if (ContainsAccountingVoucherAll(text, "ABONO", "INTERES")
            && ContainsAccountingVoucherAny(text, "AHORRO", "AHORROS"))
            return ("abono-intereses-ahorros", "Abono intereses ahorros");

        return ("comprobante-contable", FirstNonEmpty(row.Description, row.Recipient, "Comprobante contable"));
    }

    private static bool ContainsAccountingVoucherAny(string text, params string[] tokens) =>
        tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAccountingVoucherAll(string text, params string[] tokens) =>
        tokens.All(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeAccountingVoucherText(string? value)
    {
        return (value ?? "")
            .ToUpperInvariant()
            .Replace('Á', 'A')
            .Replace('É', 'E')
            .Replace('Í', 'I')
            .Replace('Ó', 'O')
            .Replace('Ú', 'U')
            .Replace('Ü', 'U')
            .Replace('Ñ', 'N');
    }

    private static string ResolveAccountingVoucherThirdPartyIdentification(ConciliacionCashFlowRowDto row)
    {
        var selectedIdentification = ExtractDigits(row.ThirdPartyIdentification);
        if (selectedIdentification.Length >= 3)
            return selectedIdentification;

        foreach (var value in new[] { row.Recipient, row.Description, row.Observations, row.DocumentType })
        {
            var match = Regex.Match(value ?? "", @"\b(?:NIT|CC|CEDULA|CEDULA)\D*(?<id>\d{5,12})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
            {
                var digits = ExtractDigits(match.Groups["id"].Value);
                if (digits.Length >= 5)
                    return digits.Length >= 10 && CalculateColombianCheckDigit(digits[..^1]).ToString(CultureInfo.InvariantCulture) == digits[^1].ToString()
                        ? digits[..^1]
                        : digits;
            }
        }

        return AccountingVoucherDefaultThirdPartyIdentification;
    }

    private async Task<ConciliacionSiigoSendPreparedDto> PrepareClientPaymentForSiigoSendAsync(
        string recordId,
        CancellationToken ct)
    {
        var taxes = await _siigo.GetTaxesAsync(ct);
        var documentTypes = await _siigo.GetDocumentTypesAsync("CC", ct);
        var incomeJournalDocument = ResolveIncomeJournalDocumentType(documentTypes);
        var prepared = await _dataverse.PrepareConciliacionClientPaymentSiigoSendAsync(
            recordId,
            ct,
            taxes,
            incomeJournalDocument);

        return await RefreshPreparedClientPaymentWithSiigoBalancesAsync(
            recordId,
            prepared,
            taxes,
            incomeJournalDocument,
            ct);
    }

    private async Task<ConciliacionSiigoSendPreparedDto> RefreshPreparedClientPaymentWithSiigoBalancesAsync(
        string recordId,
        ConciliacionSiigoSendPreparedDto prepared,
        IReadOnlyList<SiigoTaxLookupDto> taxes,
        SiigoDocumentTypeLookupDto incomeJournalDocument,
        CancellationToken ct)
    {
        if (!prepared.CanSend
            || prepared.Row is null
            || string.IsNullOrWhiteSpace(prepared.CustomerIdentification)
            || prepared.InvoiceNumbers.Count == 0)
        {
            return prepared;
        }

        var movementDate = DateOnly.FromDateTime(DateTime.UtcNow);
        if (DateOnly.TryParseExact(
            prepared.Row.MovementDateValue,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedMovementDate))
        {
            movementDate = parsedMovementDate;
        }

        var startDate = movementDate.AddMonths(-18);
        var endDate = movementDate.AddDays(1);
        var siigoInvoices = await _siigo.GetInvoicesAsync(
            customerId: null,
            customerQuery: prepared.CustomerIdentification,
            startDate,
            endDate,
            ct);

        return await _dataverse.PrepareConciliacionClientPaymentSiigoSendAsync(
            recordId,
            ct,
            taxes,
            incomeJournalDocument,
            siigoInvoices.Invoices);
    }

    private static (int LineCount, decimal Debit, decimal Credit) CalculatePreparedJournalTotals(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return (0, 0m, 0m);

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (!document.RootElement.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return (0, 0m, 0m);
            }

            var lineCount = 0;
            var debit = 0m;
            var credit = 0m;
            foreach (var item in items.EnumerateArray())
            {
                var value = ReadDecimal(item, "value");
                if (value == 0m)
                    continue;

                lineCount++;
                var movement = "";
                if (item.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object)
                    movement = ReadString(account, "movement");

                if (string.Equals(movement, "Debit", StringComparison.OrdinalIgnoreCase))
                    debit += value;
                else if (string.Equals(movement, "Credit", StringComparison.OrdinalIgnoreCase))
                    credit += value;
            }

            return (lineCount, Math.Round(debit, 2, MidpointRounding.AwayFromZero), Math.Round(credit, 2, MidpointRounding.AwayFromZero));
        }
        catch (JsonException)
        {
            return (0, 0m, 0m);
        }
    }

    private static decimal ReadDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return 0m;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var number) => number,
            _ => 0m
        };
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return "";

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : value.ToString();
    }

    internal static SiigoDocumentTypeLookupDto ResolveIncomeJournalDocumentType(
        IReadOnlyList<SiigoDocumentTypeLookupDto> documentTypes)
    {
        var activeDocuments = documentTypes
            .Where(static documentType => documentType.Active
                && string.Equals(documentType.Type, "CC", StringComparison.OrdinalIgnoreCase)
                && string.Equals(documentType.Code, "17", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var byName = activeDocuments.FirstOrDefault(static documentType =>
            NormalizeSiigoDocumentTypeText($"{documentType.Name} {documentType.Description}")
                .Contains("COMPROBANTE DE INGRESO", StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
            return byName;

        var byCode = activeDocuments.FirstOrDefault();
        if (byCode is not null)
            return byCode;

        throw new InvalidOperationException("No encontre en Siigo el tipo CC-17 activo para Comprobante de ingreso.");
    }

    private static string NormalizeSiigoDocumentTypeText(string value)
    {
        var text = (value ?? "").Trim().ToUpperInvariant();
        return text
            .Replace("Á", "A", StringComparison.Ordinal)
            .Replace("É", "E", StringComparison.Ordinal)
            .Replace("Í", "I", StringComparison.Ordinal)
            .Replace("Ó", "O", StringComparison.Ordinal)
            .Replace("Ú", "U", StringComparison.Ordinal)
            .Replace("Ü", "U", StringComparison.Ordinal)
            .Replace("Ñ", "N", StringComparison.Ordinal);
    }
}
