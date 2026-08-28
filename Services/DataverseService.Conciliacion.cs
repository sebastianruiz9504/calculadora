using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Automation;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Tasks;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string ConciliacionCashFlowPendingReviewStatus = "PendienteRevision";
    private const string ConciliacionCashFlowOmittedStatus = "Omitido";
    private const string ConciliacionCreatedOnField = "createdon";
    private const string ConciliacionModifiedOnField = "modifiedon";
    private const string ClientPaymentMatchPreflightStatusField = "cr07a_preflightestado";
    private const string ClientPaymentMatchPreflightMessageField = "cr07a_preflightmensaje";
    private const string ClientPaymentMatchPreflightValidatedOnField = "cr07a_preflightfecha";
    private const string ClientPaymentMatchPreflightDebitField = "cr07a_preflightdebito";
    private const string ClientPaymentMatchPreflightCreditField = "cr07a_preflightcredito";
    private const string ConciliacionDianDocumentTypeField = "cr07a_tipodocumento";
    private const string ConciliacionDianPrefixField = "cr07a_prefijo";
    private const string ConciliacionDianFolioField = "cr07a_folio";
    private const string ConciliacionDianIssuerNitField = "cr07a_nitemisor";
    private const string ConciliacionDianSourceField = "cr07a_fuenteautomatizacion";
    private const string ConciliacionDianExcelKeyField = "cr07a_excelkey";
    private const string ConciliacionDianSiigoDocumentIdField = "cr07a_siigodocumentid";
    private const string ConciliacionDianSiigoDocumentNameField = "cr07a_siigodocumentname";
    private const int ConciliacionSiigoIncomeJournalDocumentFallbackId = 31321;
    private const string ConciliacionSiigoIncomeJournalDocumentFallbackName = "Comprobante de ingreso";
    private static readonly CultureInfo ConciliacionCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly Regex ConciliacionInvoiceTokenRegex = new(
        @"\b(?:FV|FVE|FEV|FEM|FE|FEDT|FEKT)[-\s]*\d+(?:[-\s]*\d+)?\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public async Task<ConciliacionBoardDto> GetConciliacionBoardAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        if (year < 2020 || month is < 1 or > 12)
            throw new InvalidOperationException("El periodo de conciliacion no es valido.");

        var start = new DateOnly(year, month, 1);
        var endExclusive = start.AddMonths(1);
        await RefreshConciliacionClientPaymentMatchesForBoardAsync(start, endExclusive, ct);

        var cashFlowRowsTask = GetConciliacionCashFlowRowsAsync(start, endExclusive, ct);
        var clientPaymentsTask = GetConciliacionClientPaymentsAsync(start, endExclusive, ct);
        var dianSupplierInvoicesTask = GetConciliacionDianSupplierInvoiceRowsAsync(start, endExclusive, ct);
        var dianExpenseAccountsTask = GetConciliacionDianExpenseAccountOptionsAsync(ct);
        var accountingAccountsTask = GetConciliacionAccountingAccountOptionsAsync(ct);
        var bankOpeningBalancesTask = GetConciliacionBankOpeningBalanceIndexAsync(year, month, ct);
        await Task.WhenAll(
            cashFlowRowsTask,
            clientPaymentsTask,
            dianSupplierInvoicesTask,
            dianExpenseAccountsTask,
            accountingAccountsTask,
            bankOpeningBalancesTask);

        await CloseConciliacionTerminalClientPaymentMatchesAsync(
            cashFlowRowsTask.Result,
            clientPaymentsTask.Result,
            ct);

        var clientPayments = BuildConciliacionClientPaymentSummary(clientPaymentsTask.Result);
        var dianSupplierInvoices = BuildConciliacionDianSupplierInvoiceSummary(dianSupplierInvoicesTask.Result);
        var cashFlow = BuildConciliacionCashFlowSummary(cashFlowRowsTask.Result, clientPayments.Rows);
        cashFlow.BankBalances = BuildConciliacionCashFlowBankBalances(
            cashFlow.Rows,
            year,
            month,
            bankOpeningBalancesTask.Result);
        var cuentasCobro = await GetConciliacionCuentaCobroSummaryAsync(start, endExclusive, cashFlow.Rows, ct);
        var phases = BuildConciliacionPhases(cashFlow, clientPayments, dianSupplierInvoices, cuentasCobro);
        var pending = clientPayments.PendingReview
            + cashFlow.PendingValidationRows
            + dianSupplierInvoices.ProviderPending
            + dianSupplierInvoices.ClassificationPending;
        var suggested = clientPayments.Suggested;
        var approved = clientPayments.Approved;

        return new ConciliacionBoardDto
        {
            Year = year,
            Month = month,
            PeriodLabel = start.ToString("MMMM yyyy", ConciliacionCulture),
            StatusLabel = pending > 0 ? "Con pendientes" : suggested > 0 ? "Listo para aprobacion" : "En preparacion",
            StatusTone = pending > 0 ? "warning" : suggested > 0 ? "info" : "neutral",
            TotalPendingReview = pending,
            TotalSuggested = suggested,
            TotalApproved = approved,
            ClientPaymentEntries = clientPayments.TotalEntries,
            Phases = phases,
            CashFlow = cashFlow,
            ClientPayments = clientPayments,
            DianSupplierInvoices = dianSupplierInvoices,
            CuentasCobro = cuentasCobro,
            DianCategoryOptions = BuildPnlCategoryOptions()
                .Select(static option => new ConciliacionOptionDto { Value = option.Value?.ToString(CultureInfo.InvariantCulture) ?? option.Key, Label = option.Label })
                .Where(static option => !string.IsNullOrWhiteSpace(option.Value) && !string.IsNullOrWhiteSpace(option.Label))
                .ToArray(),
            DianExpenseAccountOptions = dianExpenseAccountsTask.Result,
            AccountingAccountOptions = accountingAccountsTask.Result
        };
    }

    private async Task RefreshConciliacionClientPaymentMatchesForBoardAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct)
    {
        try
        {
            await MatchCashFlowClientPaymentsAsync(
                startInclusive,
                endExclusive.AddDays(-1),
                dryRun: false,
                ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "No fue posible refrescar automaticamente los cruces de entradas FV para Conciliacion {StartDate} - {EndDate}.",
                startInclusive,
                endExclusive.AddDays(-1));
        }
    }

    private async Task CloseConciliacionTerminalClientPaymentMatchesAsync(
        IReadOnlyList<ConciliacionCashFlowRowDto> cashFlowRows,
        IReadOnlyList<ConciliacionClientPaymentRowDto> clientPayments,
        CancellationToken ct)
    {
        var terminalMovementsByExternalKey = cashFlowRows
            .Where(IsConciliacionClientPaymentMovementCandidate)
            .Where(IsConciliacionCashFlowTerminal)
            .Where(static row => !string.IsNullOrWhiteSpace(row.ExternalKey))
            .GroupBy(static row => row.ExternalKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (terminalMovementsByExternalKey.Count == 0)
            return;

        var staleMatches = clientPayments
            .Where(static match => !IsConciliacionClientPaymentTerminalStatus(match.Status))
            .Where(static match => !string.IsNullOrWhiteSpace(match.RecordId))
            .Where(static match => !string.IsNullOrWhiteSpace(match.MovementExternalKey))
            .Select(match => new
            {
                Match = match,
                Movement = terminalMovementsByExternalKey.GetValueOrDefault(match.MovementExternalKey.Trim())
            })
            .Where(static item => item.Movement is not null)
            .ToArray();
        if (staleMatches.Length == 0)
            return;

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);

        using var throttler = new SemaphoreSlim(4);
        var tasks = staleMatches.Select(async item =>
        {
            ct.ThrowIfCancellationRequested();
            await throttler.WaitAsync(ct);
            try
            {
                var movement = item.Movement!;
                var match = item.Match;
                var status = ResolveConciliacionTerminalClientPaymentStatus(movement);
                var invoiceDetail = string.IsNullOrWhiteSpace(match.InvoiceNumbers)
                    ? ""
                    : $" Factura(s): {match.InvoiceNumbers}.";
                var siigoReference = FirstNonEmpty(
                    movement.SiigoDocumentName,
                    movement.SiigoDocumentId,
                    "checkpoint confirmado");
                var detail = TruncateAccountCatalogText(
                    $"Cruce cerrado desde el checkpoint terminal del movimiento.{invoiceDetail} Comprobante Siigo: {siigoReference}.",
                    1000);
                var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                SetAccountCatalogValue(payload, attributes, ClientPaymentMatchStatusField, null, status, force: true);
                SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReasonField, null, detail, force: true);
                SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightStatusField, null, status, force: true);
                SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightMessageField, null, detail, force: true);
                SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightValidatedOnField, (DateTimeOffset?)null, DateTimeOffset.UtcNow, force: true);
                if (payload.Count == 0)
                    return;

                await CallDataverseAppSendAsync(
                    $"/api/data/v9.2/{metadata.EntitySetName}({NormalizeGuid(match.RecordId, nameof(match.RecordId))})",
                    "PATCH",
                    payload,
                    ct);

                match.Status = status;
                match.StatusLabel = ResolveConciliacionStatusLabel(status);
                match.StatusTone = ResolveConciliacionStatusTone(status);
                match.Reason = detail;
                match.PreflightStatus = status;
                match.PreflightStatusLabel = ResolveConciliacionPreflightStatusLabel(status);
                match.PreflightStatusTone = ResolveConciliacionPreflightStatusTone(status);
                match.PreflightMessage = detail;
                match.PreflightValidatedOnDisplay = FormatConciliacionDateTimeDisplay(DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "No fue posible cerrar el cruce obsoleto {MatchId} del movimiento terminal {MovementId}.",
                    item.Match.RecordId,
                    item.Movement!.RecordId);
            }
            finally
            {
                throttler.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
    }

    public async Task<ConciliacionMonthValidationStateDto> GetConciliacionCashFlowMonthValidationAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        ValidateConciliacionMonth(year, month);
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var task = await FindTaskByUniqueKeyAsync(BuildConciliacionMonthValidationKey(year, month), httpContext.User, ct);
        return BuildConciliacionMonthValidationState(task);
    }

    public async Task<ConciliacionMonthValidationResultDto> MarkConciliacionCashFlowMonthValidatedAsync(
        int year,
        int month,
        string periodLabel,
        string comments,
        CancellationToken ct = default)
    {
        ValidateConciliacionMonth(year, month);
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var currentUser = await GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
        var user = httpContext.User;
        var key = BuildConciliacionMonthValidationKey(year, month);
        var periodKey = $"{year:D4}-{month:D2}";
        var label = FirstNonEmpty(periodLabel, periodKey);
        var dueDate = new DateOnly(year, month, 1).AddMonths(1).AddDays(-1);
        var closedComments = FirstNonEmpty(
            comments,
            $"Mes validado manualmente desde Conciliacion para {label}.");

        var existing = await FindTaskByUniqueKeyAsync(key, user, ct);
        if (existing is null)
        {
            var rule = new TaskRuleDefinition
            {
                UniqueKey = key,
                Title = $"Conciliacion flujo de caja {label}",
                Module = "Conciliacion",
                TaskType = "Cierre mensual flujo de caja",
                SourceId = periodKey,
                AssigneeId = currentUser.SystemUserId,
                AssigneeEmail = currentUser.Email,
                AssigneeName = ResolveUserDisplayName(currentUser),
                DueDate = dueDate,
                Description = $"Validacion manual del flujo de caja, Siigo y Dataverse para {label}.",
                ActionUrl = $"/Conciliacion?year={year}&month={month}#tab=flujo-caja&vertical=Cloud",
                PeriodKey = periodKey,
                PendingCount = 0,
                ShouldBeOpen = true,
                IsManual = true
            };
            existing = await CreateTaskFromRuleAsync(rule, currentUser, user, ct);
        }
        else
        {
            var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [TaskNameField] = TruncateTaskText($"Conciliacion flujo de caja {label}", 200),
                [TaskUniqueKeyField] = TruncateTaskText(key, 300),
                [TaskModuleField] = "Conciliacion",
                [TaskTypeField] = "Cierre mensual flujo de caja",
                [TaskSourceIdField] = periodKey,
                [TaskAssigneeIdField] = TruncateTaskText(NormalizeOptionalGuid(currentUser.SystemUserId), 100),
                [TaskAssigneeEmailField] = TruncateTaskText(currentUser.Email, 200),
                [TaskAssigneeNameField] = TruncateTaskText(ResolveUserDisplayName(currentUser), 200),
                [TaskDueDateField] = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                [TaskDescriptionField] = TruncateTaskText($"Validacion manual del flujo de caja, Siigo y Dataverse para {label}.", 4000),
                [TaskActionUrlField] = TruncateTaskText(BuildTaskAbsoluteUrl($"/Conciliacion?year={year}&month={month}#tab=flujo-caja&vertical=Cloud"), 600),
                [TaskPeriodKeyField] = periodKey,
                [TaskPendingCountField] = 0,
                [TaskIsManualField] = true
            };

            await CallDataverseSendAsync(
                $"/api/data/v9.2/{_tasksTableSetName}({NormalizeGuid(existing.TaskId, nameof(existing.TaskId))})",
                "PATCH",
                payload,
                user,
                ct);
        }

        await CloseAutomaticTaskAsync(existing.TaskId, closedComments, currentUser, user, ct);
        var refreshed = await GetTaskByIdAsync(existing.TaskId, user, ct) ?? existing;
        return new ConciliacionMonthValidationResultDto
        {
            Message = $"Mes {label} marcado como validado.",
            State = BuildConciliacionMonthValidationState(refreshed)
        };
    }

    private static void ValidateConciliacionMonth(int year, int month)
    {
        if (year < 2020 || month is < 1 or > 12)
            throw new InvalidOperationException("El periodo de conciliacion no es valido.");
    }

    private static string BuildConciliacionMonthValidationKey(int year, int month) =>
        $"conciliacion:flujo-caja:cierre:{year:D4}-{month:D2}";

    private static ConciliacionMonthValidationStateDto BuildConciliacionMonthValidationState(TaskBoardItemDto? task)
    {
        if (task is null || task.StatusValue != TaskStatusValues.Closed)
            return new ConciliacionMonthValidationStateDto();

        return new ConciliacionMonthValidationStateDto
        {
            IsValidated = true,
            TaskId = task.TaskId,
            ValidatedOnDisplay = task.ClosedOnDisplay,
            ValidatedBy = FirstNonEmpty(task.AssigneeName, task.AssigneeEmail),
            Comments = task.CloseComments
        };
    }

    public async Task<ConciliacionPreflightResultDto> ValidateConciliacionClientPaymentPreflightAsync(
        string recordId,
        CancellationToken ct = default)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var row = await GetConciliacionClientPaymentByIdAsync(metadata, normalizedRecordId, ct)
            ?? throw new InvalidOperationException("No encontramos el cruce de pago a validar.");

        var catalog = await GetConciliacionAccountCatalogAsync(ct);
        var preflight = ValidateConciliacionClientPaymentDraft(row, catalog);
        var isTechnicallyReady = preflight.Issues.Count == 0;
        var isApprovedForSiigo = IsConciliacionApprovedForSiigo(row.Status);
        var isReadyForSiigo = isTechnicallyReady && isApprovedForSiigo;
        var preflightStatus = isReadyForSiigo
            ? "ListoSiigo"
            : isTechnicallyReady
                ? "ValidadoPendienteAprobacion"
                : "BloqueadoSiigo";
        var nextStatus = row.Status;
        if (isReadyForSiigo)
        {
            nextStatus = "ListoSiigo";
        }
        else if (!isTechnicallyReady && IsConciliacionSiigoCandidateStatus(row.Status))
        {
            nextStatus = "BloqueadoSiigo";
        }

        var message = isReadyForSiigo
            ? "Prevalidacion correcta. El cruce queda listo para enviar el comprobante de ingreso a Siigo."
            : isTechnicallyReady
                ? "Prevalidacion contable correcta. Falta aprobar el cruce antes de dejarlo listo para Siigo."
                : "Prevalidacion bloqueada: corrige los puntos indicados antes de enviar a Siigo.";
        var detailMessage = preflight.Issues.Count == 0
            ? message
            : $"{message} {string.Join(" ", preflight.Issues)}";

        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchStatusField, null, nextStatus, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReasonField, null, detailMessage, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightStatusField, null, preflightStatus, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightMessageField, null, detailMessage, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightValidatedOnField, (DateTimeOffset?)null, DateTimeOffset.UtcNow, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightDebitField, (decimal?)null, preflight.DebitTotal, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightCreditField, (decimal?)null, preflight.CreditTotal, force: true);

        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para guardar la prevalidacion.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})",
            "PATCH",
            payload,
            ct);

        var updatedRow = await GetConciliacionClientPaymentByIdAsync(metadata, normalizedRecordId, ct);
        return new ConciliacionPreflightResultDto
        {
            Message = message,
            IsReadyForSiigo = isReadyForSiigo,
            Issues = preflight.Issues,
            Row = updatedRow
        };
    }

    public async Task<ConciliacionSiigoDryRunResultDto> SimulateConciliacionClientPaymentSiigoSendAsync(
        string recordId,
        CancellationToken ct = default)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var row = await GetConciliacionClientPaymentByIdAsync(metadata, normalizedRecordId, ct)
            ?? throw new InvalidOperationException("No encontramos el cruce de pago a simular.");

        var catalog = await GetConciliacionAccountCatalogAsync(ct);
        var preflight = ValidateConciliacionClientPaymentDraft(row, catalog);
        var issues = new List<string>(preflight.Issues);
        if (!string.Equals(row.Status, "ListoSiigo", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(string.Equals(row.Status, "Aprobado", StringComparison.OrdinalIgnoreCase)
                ? "Haz clic en Validar pre-Siigo. Si la prevalidacion no encuentra errores, el cruce pasa a Listo Siigo."
                : "El cruce debe estar aprobado y luego validado pre-Siigo antes de habilitar el envio real.");
        }

        var payloadJson = "";
        var lineCount = 0;
        try
        {
            var payload = BuildConciliacionClientPaymentSiigoDryRunPayload(row, preflight, out lineCount);
            payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonOptions) { WriteIndented = true });
        }
        catch (InvalidOperationException ex)
        {
            issues.Add(ex.Message);
        }
        catch (JsonException)
        {
            issues.Add("El JSON de borrador Siigo no es valido y no se puede simular.");
        }

        var ready = issues.Count == 0;
        return new ConciliacionSiigoDryRunResultDto
        {
            Message = ready
                ? "Simulacion correcta. El payload esta completo y aun no se envio nada a Siigo."
                : "Simulacion con pendientes. Corrige los puntos indicados antes del envio real.",
            IsReadyForSiigo = ready,
            TargetEndpoint = "DRY-RUN /v1/journals",
            PayloadJson = payloadJson,
            LineCount = lineCount,
            DebitTotal = preflight.DebitTotal,
            CreditTotal = preflight.CreditTotal,
            Issues = issues,
            Row = row
        };
    }

    public async Task<ConciliacionActionResultDto> UpdateConciliacionClientPaymentStatusAsync(
        ConciliacionClientPaymentStatusRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var recordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var status = NormalizeConciliacionClientPaymentStatus(request.Status);
        var reason = (request.Reason ?? "").Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = status switch
            {
                "Aprobado" => "Aprobado desde modulo Conciliacion.",
                "Rechazado" => "Rechazado desde modulo Conciliacion.",
                "RevisionManual" => "Marcado para revision manual desde modulo Conciliacion.",
                _ => "Estado actualizado desde modulo Conciliacion."
            };
        }

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchStatusField, null, status, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReasonField, null, reason, force: true);

        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para actualizar el cruce.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({recordId})",
            "PATCH",
            payload,
            ct);

        var row = await GetConciliacionClientPaymentByIdAsync(metadata, recordId, ct);
        return new ConciliacionActionResultDto
        {
            Message = $"Cruce marcado como {ResolveConciliacionStatusLabel(status)}.",
            Row = row
        };
    }

    public Task<ConciliacionActionResultDto> MarkConciliacionClientPaymentManualSiigoAsync(
        string recordId,
        string reason = "",
        CancellationToken ct = default)
    {
        var message = string.IsNullOrWhiteSpace(reason)
            ? "Registrada manualmente en Siigo desde Conciliacion. No se envio payload desde la app."
            : reason.Trim();

        return MarkConciliacionClientPaymentSiigoSendResultAsync(
            recordId,
            success: true,
            message: message,
            siigoName: "Subida manualmente en Siigo",
            statusOverride: "Conciliado",
            ct: ct);
    }

    public async Task<ConciliacionCashFlowActionResultDto> MarkConciliacionCashFlowManualSiigoAsync(
        ConciliacionCashFlowManualRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.RecordId) && string.IsNullOrWhiteSpace(request.MovementExternalKey))
            throw new InvalidOperationException("No encontramos la fila del flujo de caja para marcarla como manual.");

        var detail = TruncateAccountCatalogText(
            string.IsNullOrWhiteSpace(request.Reason)
                ? "Movimiento marcado como subido manualmente en Siigo y conciliado desde Conciliacion."
                : request.Reason.Trim(),
            1000);

        if (!string.IsNullOrWhiteSpace(request.ClientPaymentRecordId))
        {
            await MarkConciliacionClientPaymentManualSiigoAsync(
                request.ClientPaymentRecordId,
                detail,
                ct);
        }

        return string.Equals(request.SourceKind, "Traslado", StringComparison.OrdinalIgnoreCase)
            ? await MarkConciliacionCashFlowTransferManualSiigoAsync(request, detail, ct)
            : await MarkConciliacionCashFlowMovementManualSiigoAsync(request, detail, ct);
    }

    private async Task<ConciliacionCashFlowActionResultDto> MarkConciliacionCashFlowMovementManualSiigoAsync(
        ConciliacionCashFlowManualRequest request,
        string detail,
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowMovementAttributeSet(metadata, attributes);
        var movementId = await ResolveConciliacionCashFlowMovementIdAsync(metadata, request.RecordId, request.MovementExternalKey, ct);
        var siigoName = "Subida manualmente en Siigo";

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, CashFlowStatusField, null, "Conciliado", force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSiigoStatusField, null, "Conciliado", force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSiigoDocumentIdField, null, "", force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSiigoDocumentNameField, null, siigoName, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowReviewReasonField, null, detail, force: true);

        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para marcar el flujo de caja como manual.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({movementId})",
            "PATCH",
            payload,
            ct);

        var updated = await GetConciliacionCashFlowMovementByIdAsync(metadata, attributes, movementId, ct);
        return new ConciliacionCashFlowActionResultDto
        {
            Message = detail,
            IsSuccess = true,
            IsReadyForSiigo = false,
            TargetEndpoint = "MANUAL Siigo",
            SiigoName = siigoName,
            Row = updated
        };
    }

    private async Task<ConciliacionCashFlowActionResultDto> MarkConciliacionCashFlowTransferManualSiigoAsync(
        ConciliacionCashFlowManualRequest request,
        string detail,
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowTransferLogicalName,
            CashFlowTransferSetName,
            CashFlowTransferIdField,
            CashFlowTransferPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowTransferAttributeSet(metadata, attributes);
        var transferId = await ResolveConciliacionCashFlowTransferIdAsync(metadata, request.RecordId, request.MovementExternalKey, ct);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferStatusField, null, "Conciliado", force: true);
        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para marcar el traslado como manual.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({transferId})",
            "PATCH",
            payload,
            ct);

        var updated = await GetConciliacionCashFlowTransferByIdAsync(metadata, attributes, transferId, ct);
        return new ConciliacionCashFlowActionResultDto
        {
            Message = detail,
            IsSuccess = true,
            IsReadyForSiigo = false,
            TargetEndpoint = "MANUAL Siigo",
            SiigoName = "Subida manualmente en Siigo",
            Row = updated
        };
    }

    public async Task<ConciliacionCashFlowCategoryResultDto> UpdateConciliacionCashFlowCategoryAsync(
        ConciliacionCashFlowCategoryRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var category = ResolveConciliacionManualCashFlowCategory(request.CategoryValue)
            ?? throw new InvalidOperationException("La categoria solicitada no es valida.");
        var sourceKind = FirstNonEmpty(request.SourceKind, "Movimiento");
        if (sourceKind.Equals("Traslado", StringComparison.OrdinalIgnoreCase))
            return await UpdateConciliacionCashFlowTransferCategoryAsync(request, category, ct);

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowMovementAttributeSet(metadata, attributes);

        var movementId = Guid.TryParse(request.RecordId, out var parsedRecordId)
            ? parsedRecordId.ToString("D")
            : "";
        if (string.IsNullOrWhiteSpace(movementId))
            movementId = await FindConciliacionCashFlowMovementIdByExternalKeyAsync(metadata, request.MovementExternalKey, ct);
        if (string.IsNullOrWhiteSpace(movementId))
            throw new InvalidOperationException("No encontramos la fila del flujo de caja para guardar la categoria.");

        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? $"Categoria reasignada manualmente a {category.Label} desde Conciliacion."
            : request.Reason.Trim();
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, CashFlowMovementTypeField, null, category.Key, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowReviewReasonField, null, TruncateAccountCatalogText(reason, 1000), force: true);

        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para guardar la categoria del flujo.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({movementId})",
            "PATCH",
            payload,
            ct);

        if (!string.IsNullOrWhiteSpace(request.ClientPaymentRecordId)
            && !string.Equals(category.Key, "entrada-fe", StringComparison.OrdinalIgnoreCase))
        {
            await MarkConciliacionClientPaymentReassignedAsync(
                request.ClientPaymentRecordId,
                $"Cruce removido de Registro de Entradas FE porque el flujo se reasigno a {category.Label}. {reason}",
                ct);
        }

        return new ConciliacionCashFlowCategoryResultDto
        {
            Message = $"Categoria guardada en Dataverse: {category.Label}.",
            CategoryValue = category.Key,
            CategoryLabel = category.Label,
            CategoryTone = category.Tone
        };
    }

    public async Task<ConciliacionCashFlowDescriptionResultDto> UpdateConciliacionCashFlowDescriptionAsync(
        ConciliacionCashFlowDescriptionRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var description = TruncateAccountCatalogText((request.Description ?? "").Trim(), 4000);
        var sourceKind = FirstNonEmpty(request.SourceKind, "Movimiento");
        if (sourceKind.Equals("Traslado", StringComparison.OrdinalIgnoreCase))
            return await UpdateConciliacionCashFlowTransferDescriptionAsync(request, description, ct);

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowMovementAttributeSet(metadata, attributes);
        var movementId = await ResolveConciliacionCashFlowMovementIdAsync(
            metadata,
            request.RecordId,
            request.MovementExternalKey,
            ct);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, CashFlowDescriptionField, null, description, force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            CashFlowReviewReasonField,
            null,
            TruncateAccountCatalogText("Descripcion registrada manualmente desde Conciliacion 2.", 1000),
            force: true);
        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para guardar la descripcion.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({movementId})",
            "PATCH",
            payload,
            ct);

        var updated = await GetConciliacionCashFlowMovementByIdAsync(metadata, attributes, movementId, ct);
        return new ConciliacionCashFlowDescriptionResultDto
        {
            Message = "Descripcion guardada en Dataverse.",
            Description = description,
            Row = updated
        };
    }

    public async Task<ConciliacionCashFlowDescriptionResultDto> MarkConciliacionCashFlowPendingAsync(
        ConciliacionCashFlowPendingRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var reason = TruncateAccountCatalogText((request.Reason ?? "").Trim(), 1000);
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Escribe el motivo por el cual la conciliacion queda pendiente.");

        var sourceKind = FirstNonEmpty(request.SourceKind, "Movimiento");
        if (sourceKind.Equals("Traslado", StringComparison.OrdinalIgnoreCase))
            return await MarkConciliacionCashFlowTransferPendingAsync(request, reason, ct);

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowMovementAttributeSet(metadata, attributes);
        var movementId = await ResolveConciliacionCashFlowMovementIdAsync(
            metadata,
            request.RecordId,
            request.MovementExternalKey,
            ct);
        var current = await GetConciliacionCashFlowMovementByIdAsync(metadata, attributes, movementId, ct)
            ?? throw new InvalidOperationException("No encontramos el movimiento en Dataverse.");
        EnsureConciliacionCashFlowCanBeLeftPending(current);
        var description = AppendConciliacionPendingReason(current.Description, reason);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, CashFlowDescriptionField, null, description, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowStatusField, null, ConciliacionCashFlowPendingReviewStatus, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowReviewReasonField, null, reason, force: true);
        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para dejar pendiente el movimiento.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({movementId})",
            "PATCH",
            payload,
            ct);

        var updated = await GetConciliacionCashFlowMovementByIdAsync(metadata, attributes, movementId, ct);
        if (updated is null
            || !string.Equals(updated.DataverseStatus, ConciliacionCashFlowPendingReviewStatus, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(updated.Description, description, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Dataverse no confirmo correctamente el movimiento pendiente.");
        }

        return new ConciliacionCashFlowDescriptionResultDto
        {
            Message = "Movimiento dejado pendiente para verificacion posterior.",
            Description = description,
            Row = updated
        };
    }

    public async Task<ConciliacionCashFlowDescriptionResultDto> MarkConciliacionCashFlowOmittedAsync(
        ConciliacionCashFlowPendingRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var reason = TruncateAccountCatalogText((request.Reason ?? "").Trim(), 1000);
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Escribe la observacion por la cual el movimiento se omite.");

        var sourceKind = FirstNonEmpty(request.SourceKind, "Movimiento");
        if (sourceKind.Equals("Traslado", StringComparison.OrdinalIgnoreCase))
            return await MarkConciliacionCashFlowTransferOmittedAsync(request, reason, ct);

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowMovementAttributeSet(metadata, attributes);
        var movementId = await ResolveConciliacionCashFlowMovementIdAsync(
            metadata,
            request.RecordId,
            request.MovementExternalKey,
            ct);
        var current = await GetConciliacionCashFlowMovementByIdAsync(metadata, attributes, movementId, ct)
            ?? throw new InvalidOperationException("No encontramos el movimiento en Dataverse.");
        EnsureConciliacionCashFlowCanBeLeftPending(current);
        var description = AppendConciliacionOmittedReason(current.Description, reason);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, CashFlowDescriptionField, null, description, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowStatusField, null, ConciliacionCashFlowOmittedStatus, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowReviewReasonField, null, reason, force: true);
        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para omitir el movimiento.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({movementId})",
            "PATCH",
            payload,
            ct);

        var updated = await GetConciliacionCashFlowMovementByIdAsync(metadata, attributes, movementId, ct);
        if (updated is null
            || !string.Equals(updated.DataverseStatus, ConciliacionCashFlowOmittedStatus, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(updated.Description, description, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Dataverse no confirmo correctamente el movimiento omitido.");
        }

        await MarkConciliacionClientPaymentMatchesOmittedAsync(
            FirstNonEmpty(updated.ExternalKey, request.MovementExternalKey),
            reason,
            ct);

        return new ConciliacionCashFlowDescriptionResultDto
        {
            Message = "Movimiento omitido y observacion guardada en Dataverse.",
            Description = description,
            Row = updated
        };
    }

    private async Task<ConciliacionCashFlowDescriptionResultDto> MarkConciliacionCashFlowTransferPendingAsync(
        ConciliacionCashFlowPendingRequest request,
        string reason,
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowTransferLogicalName,
            CashFlowTransferSetName,
            CashFlowTransferIdField,
            CashFlowTransferPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowTransferAttributeSet(metadata, attributes);
        var transferId = await ResolveConciliacionCashFlowTransferIdAsync(
            metadata,
            request.RecordId,
            request.MovementExternalKey,
            ct);
        var current = await GetConciliacionCashFlowTransferByIdAsync(metadata, attributes, transferId, ct)
            ?? throw new InvalidOperationException("No encontramos el traslado en Dataverse.");
        EnsureConciliacionCashFlowCanBeLeftPending(current);
        var description = AppendConciliacionPendingReason(current.Description, reason);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferDescriptionField, null, description, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferStatusField, null, ConciliacionCashFlowPendingReviewStatus, force: true);
        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para dejar pendiente el traslado.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({transferId})",
            "PATCH",
            payload,
            ct);

        var updated = await GetConciliacionCashFlowTransferByIdAsync(metadata, attributes, transferId, ct);
        if (updated is null
            || !string.Equals(updated.DataverseStatus, ConciliacionCashFlowPendingReviewStatus, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(updated.Description, description, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Dataverse no confirmo correctamente el traslado pendiente.");
        }

        return new ConciliacionCashFlowDescriptionResultDto
        {
            Message = "Traslado dejado pendiente para verificacion posterior.",
            Description = description,
            Row = updated
        };
    }

    private async Task<ConciliacionCashFlowDescriptionResultDto> MarkConciliacionCashFlowTransferOmittedAsync(
        ConciliacionCashFlowPendingRequest request,
        string reason,
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowTransferLogicalName,
            CashFlowTransferSetName,
            CashFlowTransferIdField,
            CashFlowTransferPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowTransferAttributeSet(metadata, attributes);
        var transferId = await ResolveConciliacionCashFlowTransferIdAsync(
            metadata,
            request.RecordId,
            request.MovementExternalKey,
            ct);
        var current = await GetConciliacionCashFlowTransferByIdAsync(metadata, attributes, transferId, ct)
            ?? throw new InvalidOperationException("No encontramos el traslado en Dataverse.");
        EnsureConciliacionCashFlowCanBeLeftPending(current);
        var description = AppendConciliacionOmittedReason(current.Description, reason);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferDescriptionField, null, description, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferStatusField, null, ConciliacionCashFlowOmittedStatus, force: true);
        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para omitir el traslado.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({transferId})",
            "PATCH",
            payload,
            ct);

        var updated = await GetConciliacionCashFlowTransferByIdAsync(metadata, attributes, transferId, ct);
        if (updated is null
            || !string.Equals(updated.DataverseStatus, ConciliacionCashFlowOmittedStatus, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(updated.Description, description, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Dataverse no confirmo correctamente el traslado omitido.");
        }

        return new ConciliacionCashFlowDescriptionResultDto
        {
            Message = "Traslado omitido y observacion guardada en Dataverse.",
            Description = description,
            Row = updated
        };
    }

    private async Task MarkConciliacionClientPaymentMatchesOmittedAsync(
        string? movementExternalKey,
        string reason,
        CancellationToken ct)
    {
        var externalKey = (movementExternalKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(externalKey))
            return;

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildConciliacionClientPaymentAttributeSet(metadata, attributes);
        if (!attributes.Contains(ClientPaymentMatchMovementExternalKeyField)
            || !attributes.Contains(ClientPaymentMatchStatusField))
        {
            return;
        }

        var select = Uri.EscapeDataString(string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            ClientPaymentMatchStatusField
        }));
        var filter = $"{ClientPaymentMatchMovementExternalKeyField} eq '{EscapeOdataLiteral(externalKey)}'";
        var rows = await GetDataverseAppEntitiesAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=10",
            ct);
        var detail = TruncateAccountCatalogText(
            $"Movimiento omitido desde Conciliacion. Observacion: {reason}",
            1000);

        foreach (var item in rows)
        {
            var matchId = FirstNonEmpty(
                ReadString(item, metadata.PrimaryIdField),
                ReadString(item, ClientPaymentMatchIdField)).Trim();
            var currentStatus = ReadString(item, ClientPaymentMatchStatusField).Trim();
            if (!Guid.TryParse(matchId, out var parsedMatchId)
                || IsConciliacionClientPaymentTerminalStatus(currentStatus))
            {
                continue;
            }

            var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            SetAccountCatalogValue(payload, attributes, ClientPaymentMatchStatusField, null, ConciliacionCashFlowOmittedStatus, force: true);
            SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReasonField, null, detail, force: true);
            SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightStatusField, null, ConciliacionCashFlowOmittedStatus, force: true);
            SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightMessageField, null, detail, force: true);
            SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightValidatedOnField, (DateTimeOffset?)null, DateTimeOffset.UtcNow, force: true);

            await CallDataverseAppSendAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}({parsedMatchId:D})",
                "PATCH",
                payload,
                ct);

            var readBack = await GetConciliacionClientPaymentByIdAsync(metadata, parsedMatchId.ToString("D"), ct);
            if (readBack is null
                || !string.Equals(readBack.Status, ConciliacionCashFlowOmittedStatus, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(readBack.Reason, detail, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Dataverse no confirmo el cruce asociado al movimiento omitido.");
            }
        }
    }

    private static void EnsureConciliacionCashFlowCanBeLeftPending(ConciliacionCashFlowRowDto row)
    {
        if (!string.IsNullOrWhiteSpace(row.SiigoDocumentId)
            || string.Equals(row.DataverseStatus, "Conciliado", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.SiigoStatus, "Conciliado", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.SiigoStatus, "EnviadoSiigo", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El movimiento ya fue enviado o conciliado y no puede devolverse a pendiente.");
        }
    }

    internal static string AppendConciliacionPendingReason(string? existingDescription, string? reason)
    {
        const int maxLength = 4000;
        const string prefix = "[PENDIENTE] ";
        var normalizedReason = (reason ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason))
            throw new InvalidOperationException("Escribe el motivo por el cual la conciliacion queda pendiente.");

        var marker = prefix + normalizedReason;
        if (marker.Length > maxLength)
            marker = marker[..maxLength];

        var existing = (existingDescription ?? "").Trim();
        if (existing
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line => string.Equals(line, marker, StringComparison.OrdinalIgnoreCase)))
        {
            return existing.Length <= maxLength ? existing : existing[..maxLength];
        }

        if (string.IsNullOrWhiteSpace(existing))
            return marker;

        var availableExistingLength = Math.Max(0, maxLength - marker.Length - 1);
        if (existing.Length > availableExistingLength)
            existing = existing[..availableExistingLength].TrimEnd();

        return string.IsNullOrWhiteSpace(existing)
            ? marker
            : $"{existing}\n{marker}";
    }

    internal static string AppendConciliacionOmittedReason(string? existingDescription, string? reason)
    {
        const int maxLength = 4000;
        const string prefix = "[OMITIDO] ";
        var normalizedReason = (reason ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason))
            throw new InvalidOperationException("Escribe la observacion por la cual el movimiento se omite.");

        var marker = prefix + normalizedReason;
        if (marker.Length > maxLength)
            marker = marker[..maxLength];

        var existing = (existingDescription ?? "").Trim();
        if (existing
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line => string.Equals(line, marker, StringComparison.OrdinalIgnoreCase)))
        {
            return existing.Length <= maxLength ? existing : existing[..maxLength];
        }

        if (string.IsNullOrWhiteSpace(existing))
            return marker;

        var availableExistingLength = Math.Max(0, maxLength - marker.Length - 1);
        if (existing.Length > availableExistingLength)
            existing = existing[..availableExistingLength].TrimEnd();

        return string.IsNullOrWhiteSpace(existing)
            ? marker
            : $"{existing}\n{marker}";
    }

    private async Task<ConciliacionCashFlowDescriptionResultDto> UpdateConciliacionCashFlowTransferDescriptionAsync(
        ConciliacionCashFlowDescriptionRequest request,
        string description,
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowTransferLogicalName,
            CashFlowTransferSetName,
            CashFlowTransferIdField,
            CashFlowTransferPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowTransferAttributeSet(metadata, attributes);
        var transferId = await ResolveConciliacionCashFlowTransferIdAsync(
            metadata,
            request.RecordId,
            request.MovementExternalKey,
            ct);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferDescriptionField, null, description, force: true);
        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para guardar la descripcion del traslado.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({transferId})",
            "PATCH",
            payload,
            ct);

        var updated = await GetConciliacionCashFlowTransferByIdAsync(metadata, attributes, transferId, ct);
        return new ConciliacionCashFlowDescriptionResultDto
        {
            Message = "Descripcion guardada en Dataverse.",
            Description = description,
            Row = updated
        };
    }

    private async Task<ConciliacionCashFlowCategoryResultDto> UpdateConciliacionCashFlowTransferCategoryAsync(
        ConciliacionCashFlowCategoryRequest request,
        (string Key, string Label, string Tone, string TargetKey) category,
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowTransferLogicalName,
            CashFlowTransferSetName,
            CashFlowTransferIdField,
            CashFlowTransferPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowTransferAttributeSet(metadata, attributes);

        var transferId = Guid.TryParse(request.RecordId, out var parsedRecordId)
            ? parsedRecordId.ToString("D")
            : "";
        if (string.IsNullOrWhiteSpace(transferId))
            transferId = await FindConciliacionCashFlowRecordIdByExternalKeyAsync(metadata, CashFlowTransferExternalKeyField, request.MovementExternalKey, ct);
        if (string.IsNullOrWhiteSpace(transferId))
            throw new InvalidOperationException("No encontramos el traslado del flujo de caja para guardar la categoria.");

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferStatusField, null, category.Key, force: true);
        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para guardar la categoria del traslado.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({transferId})",
            "PATCH",
            payload,
            ct);

        return new ConciliacionCashFlowCategoryResultDto
        {
            Message = $"Categoria guardada en Dataverse: {category.Label}.",
            CategoryValue = category.Key,
            CategoryLabel = category.Label,
            CategoryTone = category.Tone
        };
    }

    private async Task MarkConciliacionClientPaymentReassignedAsync(
        string recordId,
        string reason,
        CancellationToken ct)
    {
        if (!Guid.TryParse(recordId, out var parsedRecordId))
            return;

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildConciliacionClientPaymentAttributeSet(metadata, attributes);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchStatusField, null, "ReasignadoCategoria", force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReasonField, null, TruncateAccountCatalogText(reason, 1000), force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightStatusField, null, "ReasignadoCategoria", force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightMessageField, null, TruncateAccountCatalogText(reason, 1000), force: true);

        if (payload.Count == 0)
            return;

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({parsedRecordId:D})",
            "PATCH",
            payload,
            ct);
    }

    public async Task<ConciliacionInvoiceSearchResultDto> SearchConciliacionDataverseInvoicesAsync(
        ConciliacionInvoiceSearchRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var query = (request.Query ?? "").Trim();
        var top = Math.Clamp(request.Top <= 0 ? 20 : request.Top, 1, 50);
        var value = request.Value is > 0m ? RoundCurrency(request.Value.Value) : (decimal?)null;
        if (string.IsNullOrWhiteSpace(query) && value is null)
            throw new InvalidOperationException("Busca por cliente, numero de factura o valor de factura.");

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            ct);
        var rows = await GetCashFlowClientPaymentBillingRowsAsync(metadata, ct);
        var queryKey = NormalizeConciliacionLookupKey(query);
        var queryText = NormalizeConciliacionLookupText(query);
        var queryDigits = NormalizeConciliacionDigits(query);

        var items = rows
            .Select(row => new
            {
                Row = row,
                Score = ScoreConciliacionInvoiceLookup(row, queryKey, queryText, queryDigits, value)
            })
            .Where(static item => item.Score > 0)
            .OrderByDescending(static item => item.Score)
            .ThenByDescending(static item => item.Row.EmissionDate)
            .ThenBy(static item => item.Row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .Take(top)
            .Select(item => BuildConciliacionInvoiceLookupDto(item.Row, value))
            .ToArray();

        return new ConciliacionInvoiceSearchResultDto
        {
            Message = items.Length == 0
                ? "No encontramos facturas con esos criterios."
                : $"Encontramos {items.Length:N0} factura{(items.Length == 1 ? "" : "s")} en Dataverse.",
            Items = items
        };
    }

    public async Task<ConciliacionActionResultDto> AssignConciliacionClientPaymentInvoiceAsync(
        ConciliacionAssignInvoiceRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var recordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var invoiceRecordIds = (request.InvoiceRecordIds ?? new List<string>())
            .Concat(string.IsNullOrWhiteSpace(request.InvoiceRecordId) ? Array.Empty<string>() : new[] { request.InvoiceRecordId })
            .Select((value, index) => NormalizeGuid(value, $"invoiceRecordIds[{index}]"))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (invoiceRecordIds.Length == 0)
            throw new InvalidOperationException("Selecciona al menos una factura para asignar al pago.");
        var matchMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var matchAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(matchMetadata.LogicalName, ct);
        matchAttributes = BuildCashFlowClientPaymentMatchAttributeSet(matchMetadata, matchAttributes);

        var current = await GetConciliacionClientPaymentByIdAsync(matchMetadata, recordId, ct)
            ?? throw new InvalidOperationException("No encontramos el cruce de pago a editar.");

        var billingMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            ct);
        var invoices = await GetConciliacionBillingRecordsByIdsAppAsync(billingMetadata, invoiceRecordIds, ct);
        if (invoices.Count != invoiceRecordIds.Length)
            throw new InvalidOperationException("Una o mas facturas seleccionadas no se encontraron en Dataverse.");
        if (invoices.Count == 0)
            throw new InvalidOperationException("No encontramos las facturas seleccionadas en Dataverse.");

        var distinctCustomers = invoices
            .Select(static invoice => NormalizeConciliacionIdentificationDigits(invoice.CompanyTaxId))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinctCustomers.Length > 1)
            throw new InvalidOperationException("Selecciona facturas de un solo cliente. Las facturas elegidas tienen NIT diferentes.");

        var matchRow = BuildConciliacionManualClientPaymentMatchRow(current, invoices);
        var payload = BuildCashFlowClientPaymentMatchPayload(matchMetadata, matchAttributes, matchRow);
        SetAccountCatalogValue(payload, matchAttributes, ClientPaymentMatchPreflightStatusField, null, "", force: true);
        SetAccountCatalogValue(payload, matchAttributes, ClientPaymentMatchPreflightMessageField, null, "Factura reasignada. Falta validar pre-Siigo.", force: true);
        SetAccountCatalogValue(payload, matchAttributes, ClientPaymentMatchPreflightValidatedOnField, (DateTimeOffset?)null, null, force: true);
        SetAccountCatalogValue(payload, matchAttributes, ClientPaymentMatchPreflightDebitField, (decimal?)null, 0m, force: true);
        SetAccountCatalogValue(payload, matchAttributes, ClientPaymentMatchPreflightCreditField, (decimal?)null, 0m, force: true);

        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para asignar la factura.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{matchMetadata.EntitySetName}({recordId})",
            "PATCH",
            payload,
            ct);

        var updated = await GetConciliacionClientPaymentByIdAsync(matchMetadata, recordId, ct);
        var invoiceLabel = string.Join(", ", invoices
            .Select(static invoice => invoice.InvoiceNumber)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        return new ConciliacionActionResultDto
        {
            Message = invoices.Count == 1
                ? $"Factura {invoiceLabel} asignada al cruce. Revisa y aprueba la sugerencia para pasar a prevalidacion."
                : $"Facturas {invoiceLabel} asignadas al cruce. La suma quedo en {matchRow.InvoiceTotal:N0}; revisa y aprueba para pasar a prevalidacion.",
            Row = updated
        };
    }

    public async Task<ConciliacionSiigoSendPreparedDto> PrepareConciliacionClientPaymentSiigoSendAsync(
        string recordId,
        CancellationToken ct = default,
        IReadOnlyList<SiigoTaxLookupDto>? siigoTaxes = null,
        SiigoDocumentTypeLookupDto? journalDocument = null,
        IReadOnlyList<SiigoInvoiceRowDto>? siigoInvoices = null)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var row = await GetConciliacionClientPaymentByIdAsync(metadata, normalizedRecordId, ct)
            ?? throw new InvalidOperationException("No encontramos el cruce de pago a enviar.");

        var catalog = await GetConciliacionAccountCatalogAsync(ct);
        var preflight = ValidateConciliacionClientPaymentDraft(row, catalog);
        var issues = new List<string>(preflight.Issues);
        if (!IsConciliacionReadyForRealSendStatus(row.Status))
            issues.Add("El cruce debe estar en estado Listo Siigo o Error Siigo antes de habilitar el envio real.");
        if (journalDocument is null || journalDocument.Id <= 0)
            issues.Add("No se encontro en Siigo el tipo de comprobante Comprobante de ingreso.");
        else if (!journalDocument.Active)
            issues.Add($"El tipo de comprobante Siigo {journalDocument.Name} ({journalDocument.Id}) no esta activo.");

        var payloadJson = "";
        var customerIdentification = "";
        var invoiceNumbers = Array.Empty<string>();
        object? payload = null;
        try
        {
            var invoiceRecordIds = ExtractConciliacionInvoiceRecordIds(row);
            if (invoiceRecordIds.Count == 0)
            {
                issues.Add("No hay identificador de factura Dataverse asociado al cruce.");
            }

            var billingMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
                _dashboardBillingTableLogicalName,
                _dashboardBillingTableSetName,
                _dashboardBillingIdField,
                _dashboardBillingPrimaryNameField,
                ct);
            var invoices = invoiceRecordIds.Count == 0
                ? Array.Empty<BillingRecordRow>()
                : await GetConciliacionBillingRecordsByIdsAppAsync(billingMetadata, invoiceRecordIds, ct);
            if (invoiceRecordIds.Count > 0 && invoices.Count != invoiceRecordIds.Count)
                issues.Add("Una o mas facturas asociadas ya no se encontraron en Dataverse.");
            if (invoices.Count == 0)
                issues.Add("No hay facturas Dataverse disponibles para armar el comprobante de ingreso.");

            var exactSnapshotInvoices = ReadConciliacionExactClientPaymentInvoices(row.DraftJson);
            var useExactSnapshot = exactSnapshotInvoices.Count > 0;
            if (useExactSnapshot && invoices.Any(invoice => !exactSnapshotInvoices.ContainsKey(invoice.RecordId)))
                issues.Add("El detalle exacto guardado no contiene todas las facturas asociadas al cruce.");

            var customerIdentifications = invoices
                .Select(static invoice => NormalizeConciliacionIdentificationDigits(invoice.CompanyTaxId))
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            customerIdentification = customerIdentifications.Length == 1 ? customerIdentifications[0] : "";
            invoiceNumbers = invoices
                .Select(static invoice => FirstNonEmpty(invoice.SiigoInvoiceName, invoice.InvoiceNumber).Trim())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (customerIdentifications.Length == 0)
                issues.Add("La factura Dataverse no tiene NIT del cliente para buscarlo en Siigo.");
            else if (customerIdentifications.Length > 1)
                issues.Add("Las facturas asociadas tienen NIT de cliente diferentes; envia un comprobante por cliente.");

            var siigoInvoiceLookup = BuildConciliacionSiigoInvoiceLookup(siigoInvoices ?? Array.Empty<SiigoInvoiceRowDto>());
            var requireLiveSiigoInvoice = siigoInvoices is not null;
            var invoiceValues = invoices
                .Select(invoice => new
                {
                    Invoice = invoice,
                    Value = useExactSnapshot && exactSnapshotInvoices.TryGetValue(invoice.RecordId, out var exactInvoice)
                        ? exactInvoice.GrossValue
                        : ResolveConciliacionSiigoInvoiceAccountingValue(
                            invoice,
                            siigoInvoiceLookup,
                            requireLiveSiigoInvoice,
                            issues)
                })
                .ToArray();
            var invoiceTotal = RoundCurrency(invoiceValues.Sum(static item => item.Value));
            var dataverseInvoiceTotal = RoundCurrency(invoices.Sum(static invoice => invoice.NetTotalInvoice));
            var actualRetentions = useExactSnapshot
                ? RoundCurrency(exactSnapshotInvoices.Values.Sum(static invoice => invoice.RetentionTaxes.Sum(static retention => retention.Value)))
                : RoundCurrency(invoices.Sum(ResolveConciliacionInvoiceRetentionsTotal));
            if (!useExactSnapshot && invoices.Count > 0 && Math.Abs(dataverseInvoiceTotal - row.InvoiceTotal) > 1m)
                issues.Add($"El total de facturas Dataverse ({dataverseInvoiceTotal:N2}) no coincide con el total del cruce ({row.InvoiceTotal:N2}).");
            if (invoices.Count > 0 && Math.Abs(actualRetentions - row.RetentionsTotal) > (useExactSnapshot ? 0.02m : 1m))
                issues.Add($"Las retenciones calculadas desde la factura ({actualRetentions:N2}) no coinciden con las del cruce ({row.RetentionsTotal:N2}).");
            var siigoAdjustment = useExactSnapshot
                ? RoundCurrency(exactSnapshotInvoices.Values.Sum(static invoice => invoice.AdjustmentValue))
                : RoundCurrency(invoiceTotal - row.EntryValue - actualRetentions);
            var accountingDifference = RoundCurrency(siigoAdjustment - row.DifferenceValue);
            if (invoices.Count > 0 && Math.Abs(accountingDifference) > 1m)
                issues.Add($"El ajuste requerido contra Siigo ({siigoAdjustment:N2}) no coincide con el ajuste del cruce ({row.DifferenceValue:N2}). Diferencia residual {accountingDifference:N2}.");

            var movementDate = row.MovementDateValue.Trim();
            if (!DateOnly.TryParseExact(movementDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                issues.Add("La fecha del movimiento no tiene formato valido para Siigo.");

            var invoiceDues = new List<ConciliacionSiigoInvoiceDueItem>();
            foreach (var invoice in invoices)
            {
                var liveSiigoInvoice = FindConciliacionSiigoInvoice(invoice, siigoInvoiceLookup);
                if (!TryBuildConciliacionSiigoDue(
                    invoice,
                    liveSiigoInvoice,
                    requireLiveSiigoInvoice,
                    out var due,
                    out var dueIssue))
                {
                    issues.Add(dueIssue);
                    continue;
                }

                var retentionTaxes = useExactSnapshot
                    && exactSnapshotInvoices.TryGetValue(invoice.RecordId, out var exactInvoice)
                        ? exactInvoice.RetentionTaxes
                        : ResolveConciliacionInvoiceRetentionTaxes(invoice, siigoTaxes ?? Array.Empty<SiigoTaxLookupDto>(), issues);
                var invoiceValue = invoiceValues
                    .FirstOrDefault(value => string.Equals(value.Invoice.RecordId, invoice.RecordId, StringComparison.OrdinalIgnoreCase))
                    ?.Value ?? invoice.NetTotalInvoice;
                invoiceDues.Add(new ConciliacionSiigoInvoiceDueItem(invoice, due, RoundCurrency(invoiceValue), retentionTaxes));
            }

            if (issues.Count == 0)
            {
                payload = BuildConciliacionClientPaymentSiigoSendPayload(
                    row,
                    invoiceDues,
                    movementDate,
                    customerIdentifications[0],
                    journalDocument!,
                    siigoAdjustment);
                payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonOptions) { WriteIndented = true });
            }
        }
        catch (JsonException)
        {
            issues.Add("El JSON de borrador Siigo no es valido y no se pudo preparar el envio real.");
        }
        catch (InvalidOperationException ex)
        {
            issues.Add(ex.Message);
        }

        var canSend = issues.Count == 0 && payload is not null;
        return new ConciliacionSiigoSendPreparedDto
        {
            Message = canSend
                ? "Listo para envio real a Siigo."
                : "Envio real bloqueado. Corrige los pendientes visibles antes de enviar.",
            CanSend = canSend,
            TargetEndpoint = "/v1/journals",
            CustomerIdentification = customerIdentification,
            InvoiceNumbers = invoiceNumbers,
            Payload = payload,
            PayloadJson = payloadJson,
            Issues = issues,
            Row = row
        };
    }

    public async Task<ConciliacionActionResultDto> MarkConciliacionClientPaymentSiigoSendResultAsync(
        string recordId,
        bool success,
        string message,
        string siigoId = "",
        string siigoName = "",
        string responseJson = "",
        string statusOverride = "",
        CancellationToken ct = default)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var status = !string.IsNullOrWhiteSpace(statusOverride)
            ? statusOverride.Trim()
            : success ? "EnviadoSiigo" : "ErrorSiigo";
        var detailParts = new[]
            {
                message,
                string.IsNullOrWhiteSpace(siigoName) ? "" : $"Documento Siigo: {siigoName}.",
                string.IsNullOrWhiteSpace(siigoId) ? "" : $"Id Siigo: {siigoId}.",
                success || string.IsNullOrWhiteSpace(responseJson) ? "" : $"Detalle: {responseJson}"
            }
            .Where(static value => !string.IsNullOrWhiteSpace(value));
        var detailMessage = TruncateAccountCatalogText(string.Join(" ", detailParts), 1000);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchStatusField, null, status, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReasonField, null, detailMessage, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightStatusField, null, status, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightMessageField, null, detailMessage, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightValidatedOnField, (DateTimeOffset?)null, DateTimeOffset.UtcNow, force: true);

        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para marcar el resultado del envio a Siigo.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})",
            "PATCH",
            payload,
            ct);

        var row = await GetConciliacionClientPaymentByIdAsync(metadata, normalizedRecordId, ct);
        if (success && row is not null)
            await MarkConciliacionCashFlowMovementSiigoResultAsync(row, siigoId, siigoName, detailMessage, status, ct);

        return new ConciliacionActionResultDto
        {
            Message = detailMessage,
            Row = row
        };
    }

    public async Task<ConciliacionClientPaymentRowDto> SaveConciliacionClientPaymentDataverseSnapshotAsync(
        ConciliacionClientPaymentDataverseSnapshotRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.PaymentValue <= 0m)
            throw new InvalidOperationException("El valor pagado debe ser mayor a cero.");
        if (string.IsNullOrWhiteSpace(request.InvoiceRecordIds)
            || string.IsNullOrWhiteSpace(request.InvoiceNumbers))
        {
            throw new InvalidOperationException("No encontramos las facturas que deben quedar asociadas al pago.");
        }
        if (string.IsNullOrWhiteSpace(request.SnapshotJson))
            throw new InvalidOperationException("No se genero el detalle exacto del pago para Dataverse.");
        if (request.SnapshotJson.Length > 95000)
            throw new InvalidOperationException("El detalle del pago supera el tamano permitido por Dataverse.");

        try
        {
            using var snapshot = JsonDocument.Parse(request.SnapshotJson);
            if (!string.Equals(
                    ReadString(snapshot.RootElement, "type"),
                    "ComprobanteIngresoSiigoBorrador",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("El detalle exacto del pago no tiene el formato esperado.");
            }
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("El detalle exacto del pago no es un JSON valido.");
        }

        var invoiceTotal = RoundCurrency(request.InvoiceTotal);
        var paymentValue = RoundCurrency(request.PaymentValue);
        var reteFuenteValue = RoundCurrency(request.ReteFuenteValue);
        var reteIcaValue = RoundCurrency(request.ReteIcaValue);
        var rteIvaValue = RoundCurrency(request.RteIvaValue);
        var differenceValue = RoundCurrency(request.DifferenceValue);
        var retentionsTotal = RoundCurrency(reteFuenteValue + reteIcaValue + rteIvaValue);
        if (Math.Abs(invoiceTotal - paymentValue - retentionsTotal - differenceValue) > 0.02m)
        {
            throw new InvalidOperationException("El detalle exacto del pago no cuadra con la cartera aplicada.");
        }

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildConciliacionClientPaymentAttributeSet(metadata, attributes);
        var matchRecordId = await ResolveConciliacionClientPaymentMatchIdAsync(
            metadata,
            request.MatchRecordId,
            request.MovementExternalKey,
            ct);

        var requiredFields = new[]
        {
            ClientPaymentMatchInvoiceIdsField,
            ClientPaymentMatchInvoiceNumbersField,
            ClientPaymentMatchClientField,
            ClientPaymentMatchInvoiceTotalField,
            ClientPaymentMatchPaymentValueField,
            ClientPaymentMatchReteFteField,
            ClientPaymentMatchReteIcaField,
            ClientPaymentMatchRteIvaField,
            ClientPaymentMatchDifferenceField,
            ClientPaymentMatchDraftJsonField
        };
        var missingFields = requiredFields.Where(field => !attributes.Contains(field)).ToArray();
        if (missingFields.Length > 0)
        {
            throw new InvalidOperationException(
                $"Dataverse no tiene disponibles todos los campos del pago exacto: {string.Join(", ", missingFields)}.");
        }

        var debitTotal = RoundCurrency(paymentValue + retentionsTotal + Math.Max(differenceValue, 0m));
        var creditTotal = RoundCurrency(invoiceTotal + Math.Max(-differenceValue, 0m));
        var detail = TruncateAccountCatalogText(
            $"Aplicacion exacta confirmada en Dataverse para {request.InvoiceNumbers}. Lista para enviar a Siigo.",
            1000);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchStatusField, null, "ListoSiigo", force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReasonField, null, detail, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchMovementIdField, null, request.MovementRecordId, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchMovementExternalKeyField, null, request.MovementExternalKey, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchInvoiceIdsField, null, request.InvoiceRecordIds, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchInvoiceNumbersField, null, request.InvoiceNumbers, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchClientField, null, request.ClientNames, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchInvoiceTotalField, (decimal?)null, invoiceTotal, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPaymentValueField, (decimal?)null, paymentValue, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReteFteField, (decimal?)null, reteFuenteValue, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReteIcaField, (decimal?)null, reteIcaValue, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchRteIvaField, (decimal?)null, rteIvaValue, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchDifferenceField, (decimal?)null, differenceValue, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchDraftJsonField, null, request.SnapshotJson, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightStatusField, null, "ListoSiigo", force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightMessageField, null, detail, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightValidatedOnField, (DateTimeOffset?)null, DateTimeOffset.UtcNow, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightDebitField, (decimal?)null, debitTotal, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightCreditField, (decimal?)null, creditTotal, force: true);

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({matchRecordId})",
            "PATCH",
            payload,
            ct);

        var updated = await GetConciliacionClientPaymentByIdAsync(metadata, matchRecordId, ct)
            ?? throw new InvalidOperationException("Dataverse guardo el pago, pero no pudimos releer el cruce.");
        var exactValuesPersisted = ConciliacionExactValueMatches(updated.PaymentValue, paymentValue)
            && ConciliacionExactValueMatches(updated.ReteFuenteValue, reteFuenteValue)
            && ConciliacionExactValueMatches(updated.ReteIcaValue, reteIcaValue)
            && ConciliacionExactValueMatches(updated.RteIvaValue, rteIvaValue)
            && ConciliacionExactValueMatches(updated.InvoiceTotal, invoiceTotal)
            && ConciliacionExactValueMatches(updated.DifferenceValue, differenceValue)
            && ConciliacionDelimitedValuesMatch(updated.InvoiceRecordIds, request.InvoiceRecordIds)
            && ConciliacionDelimitedValuesMatch(updated.InvoiceNumbers, request.InvoiceNumbers)
            && string.Equals(updated.DraftJson, request.SnapshotJson, StringComparison.Ordinal);
        if (!exactValuesPersisted)
            throw new InvalidOperationException("Dataverse no confirmo el detalle exacto del pago y sus retenciones.");

        return updated;
    }

    private async Task<string> ResolveConciliacionClientPaymentMatchIdAsync(
        RhEntityMetadata metadata,
        string? matchRecordId,
        string? movementExternalKey,
        CancellationToken ct)
    {
        if (Guid.TryParse(matchRecordId, out var parsedMatchRecordId))
            return parsedMatchRecordId.ToString("D");
        if (string.IsNullOrWhiteSpace(movementExternalKey))
            throw new InvalidOperationException("No encontramos el cruce de Dataverse asociado al movimiento.");

        var filter = $"{ClientPaymentMatchMovementExternalKeyField} eq '{EscapeOdataLiteral(movementExternalKey.Trim())}'";
        var select = Uri.EscapeDataString(metadata.PrimaryIdField);
        var orderBy = Uri.EscapeDataString($"{ConciliacionModifiedOnField} desc");
        var rows = await GetDataverseAppEntitiesAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={orderBy}&$top=1",
            ct);
        var resolved = rows
            .Select(row => ReadString(row, metadata.PrimaryIdField).Trim())
            .FirstOrDefault(static value => Guid.TryParse(value, out _));
        return !string.IsNullOrWhiteSpace(resolved)
            ? resolved
            : throw new InvalidOperationException("No encontramos el cruce de Dataverse asociado al movimiento.");
    }

    private static bool ConciliacionExactValueMatches(decimal actual, decimal expected) =>
        Math.Abs(RoundCurrency(actual) - RoundCurrency(expected)) <= 0.01m;

    private static bool ConciliacionDelimitedValuesMatch(string? actual, string? expected)
    {
        static string[] Normalize(string? value) => (value ?? "")
            .Split(new[] { ';', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Normalize(actual).SequenceEqual(Normalize(expected), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ConciliacionCashFlowRowDto> GetConciliacionCashFlowMovementAsync(
        ConciliacionSupplierPaymentPurchaseSearchRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowMovementAttributeSet(metadata, attributes);
        var select = BuildConciliacionCashFlowMovementSelect(metadata, attributes);

        string filter;
        if (Guid.TryParse(request.RecordId, out var parsedRecordId))
        {
            filter = $"{metadata.PrimaryIdField} eq {parsedRecordId:D}";
        }
        else if (!string.IsNullOrWhiteSpace(request.MovementExternalKey))
        {
            filter = $"{CashFlowExternalKeyField} eq '{EscapeOdataLiteral(request.MovementExternalKey.Trim())}'";
        }
        else
        {
            throw new InvalidOperationException("No encontramos el identificador de la salida del flujo de caja.");
        }

        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=1";
        var rows = await GetDataverseAppEntitiesAsync(url, ct);
        var row = rows
            .Select(item => ParseConciliacionCashFlowMovementRow(item, metadata))
            .FirstOrDefault(static item => item is not null)
            ?? throw new InvalidOperationException("No encontramos la salida del flujo de caja en Dataverse.");

        if (!string.Equals(row.Direction, "Salida", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La fila seleccionada no es una salida bancaria.");

        return row;
    }

    public async Task<ConciliacionCashFlowActionResultDto> MarkConciliacionSupplierPaymentSiigoResultAsync(
        ConciliacionSupplierPaymentSendRequest request,
        bool success,
        string message,
        string siigoId = "",
        string siigoName = "",
        string responseJson = "",
        string payloadJson = "",
        string statusOverride = "",
        string targetEndpoint = "/v1/journals",
        string messagePrefix = "Comprobante de egreso de proveedor enviado a Siigo",
        CancellationToken ct = default)
    {
        var lookup = new ConciliacionSupplierPaymentPurchaseSearchRequest
        {
            RecordId = request.RecordId,
            MovementExternalKey = request.MovementExternalKey
        };
        var row = await GetConciliacionCashFlowMovementAsync(lookup, ct);
        var status = string.IsNullOrWhiteSpace(statusOverride)
            ? success ? "EnviadoSiigo" : "ErrorSiigo"
            : statusOverride.Trim();
        var detailParts = new[]
            {
                message,
                string.IsNullOrWhiteSpace(request.PurchaseName) ? "" : $"Factura proveedor: {request.PurchaseName}.",
                string.IsNullOrWhiteSpace(siigoName) ? "" : $"Pago Siigo: {siigoName}.",
                string.IsNullOrWhiteSpace(siigoId) ? "" : $"Id Siigo: {siigoId}.",
                success || string.IsNullOrWhiteSpace(responseJson) ? "" : $"Detalle: {responseJson}"
            }
            .Where(static value => !string.IsNullOrWhiteSpace(value));
        var detailMessage = TruncateAccountCatalogText(string.Join(" ", detailParts), 1000);

        await MarkConciliacionCashFlowMovementSiigoResultAsync(
            row,
            siigoId,
            siigoName,
            detailMessage,
            status,
            FirstNonEmpty(messagePrefix, "Pago proveedor enviado a Siigo"),
            ct);

        var updated = await GetConciliacionCashFlowMovementAsync(lookup, ct);
        return new ConciliacionCashFlowActionResultDto
        {
            Message = detailMessage,
            IsSuccess = success,
            IsReadyForSiigo = false,
            TargetEndpoint = targetEndpoint,
            PayloadJson = payloadJson,
            ResponseJson = responseJson,
            SiigoId = siigoId,
            SiigoName = siigoName,
            Row = updated
        };
    }

    private async Task MarkConciliacionCashFlowMovementSiigoResultAsync(
        ConciliacionClientPaymentRowDto row,
        string siigoId,
        string siigoName,
        string detailMessage,
        string status,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(row.MovementExternalKey)
            && string.IsNullOrWhiteSpace(row.MovementId))
        {
            return;
        }

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowMovementAttributeSet(metadata, attributes);

        var movementId = Guid.TryParse(row.MovementId, out var parsedMovementId)
            ? parsedMovementId.ToString("D")
            : await FindConciliacionCashFlowMovementIdByExternalKeyAsync(metadata, row.MovementExternalKey, ct);
        if (string.IsNullOrWhiteSpace(movementId))
            return;

        var siigoReference = FirstNonEmpty(siigoName, siigoId, "Comprobante enviado a Siigo");
        var message = TruncateAccountCatalogText(
            $"Pago cliente enviado a Siigo: {siigoReference}. {detailMessage}".Trim(),
            1000);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, CashFlowStatusField, null, status, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSiigoStatusField, null, status, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSiigoDocumentIdField, null, siigoId, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSiigoDocumentNameField, null, siigoName, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowReviewReasonField, null, message, force: true);

        if (payload.Count == 0)
            return;

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({movementId})",
            "PATCH",
            payload,
            ct);
    }

    private async Task MarkConciliacionCashFlowMovementSiigoResultAsync(
        ConciliacionCashFlowRowDto row,
        string siigoId,
        string siigoName,
        string detailMessage,
        string status,
        string messagePrefix,
        CancellationToken ct)
    {
        if (!string.Equals(row.SourceKind, "Movimiento", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(row.RecordId) && string.IsNullOrWhiteSpace(row.ExternalKey)))
        {
            return;
        }

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowMovementAttributeSet(metadata, attributes);

        var movementId = Guid.TryParse(row.RecordId, out var parsedMovementId)
            ? parsedMovementId.ToString("D")
            : await FindConciliacionCashFlowMovementIdByExternalKeyAsync(metadata, row.ExternalKey, ct);
        if (string.IsNullOrWhiteSpace(movementId))
            return;

        var siigoReference = FirstNonEmpty(siigoName, siigoId, "Comprobante enviado a Siigo");
        var message = TruncateAccountCatalogText(
            $"{messagePrefix}: {siigoReference}. {detailMessage}".Trim(),
            1000);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, CashFlowStatusField, null, status, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSiigoStatusField, null, status, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSiigoDocumentIdField, null, siigoId, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSiigoDocumentNameField, null, siigoName, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowReviewReasonField, null, message, force: true);

        if (payload.Count == 0)
            return;

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({movementId})",
            "PATCH",
            payload,
            ct);
    }

    private async Task<string> FindConciliacionCashFlowMovementIdByExternalKeyAsync(
        RhEntityMetadata metadata,
        string externalKey,
        CancellationToken ct)
    {
        return await FindConciliacionCashFlowRecordIdByExternalKeyAsync(metadata, CashFlowExternalKeyField, externalKey, ct);
    }

    private async Task<string> FindConciliacionCashFlowRecordIdByExternalKeyAsync(
        RhEntityMetadata metadata,
        string externalKeyField,
        string externalKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(externalKey))
            return "";

        var filter = $"{externalKeyField} eq '{EscapeOdataLiteral(externalKey.Trim())}'";
        var select = Uri.EscapeDataString(metadata.PrimaryIdField);
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=1";
        var rows = await GetDataverseAppEntitiesAsync(url, ct);
        return rows
            .Select(row => ReadString(row, metadata.PrimaryIdField).Trim())
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private async Task<IReadOnlyList<ConciliacionClientPaymentRowDto>> GetConciliacionClientPaymentsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildConciliacionClientPaymentAttributeSet(metadata, attributes);
        var select = BuildConciliacionClientPaymentSelect(metadata, attributes);
        var filter = BuildBillingDateFilter(ClientPaymentMatchMovementDateField, "date-only", startInclusive, endExclusive);
        var orderBy = Uri.EscapeDataString($"{ClientPaymentMatchMovementDateField} desc");
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={orderBy}";
        var rows = await GetDataverseAppEntitiesAsync(url, ct, AddFormattedValueHeaders);
        var parsedRows = rows
            .Select(item => ParseConciliacionClientPaymentRow(item, metadata))
            .Where(static row => row is not null)
            .Cast<ConciliacionClientPaymentRowDto>()
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderByDescending(static row => row.MovementDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.SourceFlow, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.ClientNames, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await AutoValidateConciliacionClientPaymentRowsAsync(metadata, attributes, parsedRows, ct);
        return parsedRows;
    }

    private async Task<ConciliacionClientPaymentRowDto?> GetConciliacionClientPaymentByIdAsync(
        RhEntityMetadata metadata,
        string recordId,
        CancellationToken ct)
    {
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildConciliacionClientPaymentAttributeSet(metadata, attributes);
        var select = BuildConciliacionClientPaymentSelect(metadata, attributes);
        var json = await CallDataverseAppGetJsonAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({recordId})?$select={select}",
            ct,
            AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        return ParseConciliacionClientPaymentRow(doc.RootElement, metadata);
    }

    private async Task AutoValidateConciliacionClientPaymentRowsAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        IReadOnlyList<ConciliacionClientPaymentRowDto> rows,
        CancellationToken ct)
    {
        var candidates = rows
            .Where(IsAutoReadyConciliacionClientPaymentCandidate)
            .ToArray();
        if (candidates.Length == 0)
            return;

        var catalog = await GetConciliacionAccountCatalogAsync(ct);
        using var throttler = new SemaphoreSlim(4);
        var tasks = candidates.Select(async row =>
        {
            ct.ThrowIfCancellationRequested();
            await throttler.WaitAsync(ct);
            try
            {
                await TryAutoValidateConciliacionClientPaymentRowAsync(metadata, attributes, row, catalog, ct);
            }
            finally
            {
                throttler.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
    }

    private async Task TryAutoValidateConciliacionClientPaymentRowAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        ConciliacionClientPaymentRowDto row,
        IReadOnlyDictionary<string, ConciliacionAccountCatalogItem> catalog,
        CancellationToken ct)
    {
        var preflight = ValidateConciliacionClientPaymentDraft(row, catalog);
        if (preflight.Issues.Count > 0)
            return;

        var message = BuildConciliacionAutoReadyMessage(row);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchStatusField, null, "ListoSiigo", force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReasonField, null, message, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightStatusField, null, "ListoSiigo", force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightMessageField, null, message, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightValidatedOnField, (DateTimeOffset?)null, DateTimeOffset.UtcNow, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightDebitField, (decimal?)null, preflight.DebitTotal, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightCreditField, (decimal?)null, preflight.CreditTotal, force: true);

        if (payload.Count == 0)
            return;

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({row.RecordId})",
            "PATCH",
            payload,
            ct);

        row.Status = "ListoSiigo";
        row.StatusLabel = ResolveConciliacionStatusLabel(row.Status);
        row.StatusTone = ResolveConciliacionStatusTone(row.Status);
        row.Reason = message;
        row.PreflightStatus = "ListoSiigo";
        row.PreflightStatusLabel = ResolveConciliacionPreflightStatusLabel(row.PreflightStatus);
        row.PreflightStatusTone = ResolveConciliacionPreflightStatusTone(row.PreflightStatus);
        row.PreflightMessage = message;
        row.PreflightDebitTotal = preflight.DebitTotal;
        row.PreflightCreditTotal = preflight.CreditTotal;
        row.PreflightValidatedOnDisplay = FormatConciliacionDateTimeDisplay(DateTimeOffset.UtcNow);
    }

    private static bool IsAutoReadyConciliacionClientPaymentCandidate(ConciliacionClientPaymentRowDto row)
    {
        if (!string.Equals(row.Status, "Sugerido", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(row.PreflightStatus, "ListoSiigo", StringComparison.OrdinalIgnoreCase))
            return false;
        if (row.Confidence < 90)
            return false;
        if (row.EntryValue <= 0m || row.InvoiceTotal <= 0m)
            return false;
        if (Math.Abs(row.DifferenceValue) > 5m)
            return false;
        if (string.IsNullOrWhiteSpace(row.RecordId)
            || string.IsNullOrWhiteSpace(row.InvoiceRecordIds)
            || string.IsNullOrWhiteSpace(row.InvoiceNumbers)
            || string.IsNullOrWhiteSpace(row.ClientNames)
            || string.IsNullOrWhiteSpace(row.BankAccountCode)
            || string.IsNullOrWhiteSpace(row.DraftJson))
        {
            return false;
        }

        return true;
    }

    private static string BuildConciliacionAutoReadyMessage(ConciliacionClientPaymentRowDto row)
    {
        var adjustment = Math.Abs(row.DifferenceValue) > 0m
            ? $" Ajuste al peso: {row.DifferenceValue:N2}."
            : "";
        return TruncateAccountCatalogText(
            $"Auto-validado: factura, cliente, banco, retenciones y comprobante contable completos.{adjustment}",
            1000);
    }

    private async Task<BillingRecordRow?> GetConciliacionBillingRecordByIdAppAsync(
        RhEntityMetadata metadata,
        string recordId,
        CancellationToken ct)
    {
        var select = BuildBillingSelectClause(metadata);
        var json = await CallDataverseAppGetJsonAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({recordId})?$select={select}",
            ct,
            AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        return ParseBillingRecord(doc.RootElement, metadata.PrimaryIdField, metadata.PrimaryNameField);
    }

    private async Task<IReadOnlyList<BillingRecordRow>> GetConciliacionBillingRecordsByIdsAppAsync(
        RhEntityMetadata metadata,
        IReadOnlyList<string> recordIds,
        CancellationToken ct)
    {
        var rows = new List<BillingRecordRow>();
        foreach (var recordId in recordIds)
        {
            var row = await GetConciliacionBillingRecordByIdAppAsync(metadata, recordId, ct);
            if (row is not null)
                rows.Add(row);
        }

        return rows
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private CashFlowClientPaymentMatchRowDto BuildConciliacionManualClientPaymentMatchRow(
        ConciliacionClientPaymentRowDto current,
        IReadOnlyList<BillingRecordRow> invoices)
    {
        if (invoices is null || invoices.Count == 0)
            throw new InvalidOperationException("Selecciona al menos una factura para asignar al pago.");

        var tokens = ExtractCashFlowClientPaymentInvoiceTokens(current.Description).ToList();
        foreach (var invoice in invoices)
        {
            var token = NormalizeDocumentToken(invoice.InvoiceNumber);
            if (!string.IsNullOrWhiteSpace(token)
                && !tokens.Contains(token, StringComparer.OrdinalIgnoreCase))
            {
                tokens.Add(token);
            }
        }

        var invoiceTotal = RoundCurrency(invoices.Sum(static invoice => invoice.NetTotalInvoice));
        var reteFteValue = RoundCurrency(invoices.Sum(ResolveCashFlowClientPaymentReteFteValue));
        var reteIcaValue = RoundCurrency(invoices.Sum(ResolveCashFlowClientPaymentReteIcaValue));
        var rteIvaValue = RoundCurrency(invoices.Sum(ResolveCashFlowClientPaymentRteIvaValue));
        var retentions = RoundCurrency(reteFteValue + reteIcaValue + rteIvaValue);
        var difference = RoundCurrency(invoiceTotal - current.EntryValue - retentions);
        var inTolerance = Math.Abs(difference) <= RegistroPagosClientesBalancedTolerance;
        var movementDate = DateOnly.TryParseExact(
            current.MovementDateValue,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedMovementDate)
            ? parsedMovementDate
            : (DateOnly?)null;

        var row = new CashFlowClientPaymentMatchRowDto
        {
            MovementId = current.MovementId,
            MovementExternalKey = current.MovementExternalKey,
            MovementDate = movementDate,
            SourceFlow = current.SourceFlow,
            BankAccountCode = current.BankAccountCode,
            BankAccountName = current.BankAccountName,
            Description = current.Description,
            EntryValue = current.EntryValue,
            InvoiceTokens = tokens,
            InvoiceTotal = invoiceTotal,
            ReteFteValue = reteFteValue,
            ReteIcaValue = reteIcaValue,
            RteIvaValue = rteIvaValue,
            RetentionsTotal = retentions,
            DifferenceValue = difference,
            Confidence = inTolerance ? (invoices.Count == 1 ? 90 : 88) : 70,
            Status = inTolerance ? "Sugerido" : "DiferenciaFueraRango",
            Reason = inTolerance
                ? (invoices.Count == 1
                    ? "Factura asignada manualmente y diferencia dentro del rango."
                    : "Facturas asignadas manualmente y diferencia agregada dentro del rango.")
                : (invoices.Count == 1
                    ? $"Factura asignada manualmente, pero la diferencia supera {RegistroPagosClientesBalancedTolerance:N0}."
                    : $"Facturas asignadas manualmente, pero la diferencia agregada supera {RegistroPagosClientesBalancedTolerance:N0}.")
        };

        return FinalizeCashFlowClientPaymentMatchRow(row, invoices);
    }

    private static int ScoreConciliacionInvoiceLookup(
        BillingRecordRow row,
        string queryKey,
        string queryText,
        string queryDigits,
        decimal? value)
    {
        var score = 0;
        var invoiceKey = NormalizeConciliacionLookupKey(row.InvoiceNumber);
        var clientKey = NormalizeConciliacionLookupKey(row.ClientName);
        var taxIdDigits = NormalizeConciliacionDigits(row.CompanyTaxId);
        var clientText = NormalizeConciliacionLookupText(row.ClientName);

        if (!string.IsNullOrWhiteSpace(queryKey))
        {
            if (string.Equals(invoiceKey, queryKey, StringComparison.OrdinalIgnoreCase))
                score += 120;
            else if (invoiceKey.Contains(queryKey, StringComparison.OrdinalIgnoreCase) || queryKey.Contains(invoiceKey, StringComparison.OrdinalIgnoreCase))
                score += 85;

            if (clientKey.Contains(queryKey, StringComparison.OrdinalIgnoreCase))
                score += 45;
        }

        if (!string.IsNullOrWhiteSpace(queryText) && clientText.Contains(queryText, StringComparison.OrdinalIgnoreCase))
            score += 35;

        if (!string.IsNullOrWhiteSpace(queryDigits))
        {
            if (NormalizeConciliacionDigits(row.InvoiceNumber).Contains(queryDigits, StringComparison.OrdinalIgnoreCase))
                score += 55;
            if (!string.IsNullOrWhiteSpace(taxIdDigits) && taxIdDigits.Contains(queryDigits, StringComparison.OrdinalIgnoreCase))
                score += 20;
        }

        if (value.HasValue)
        {
            var difference = Math.Abs(row.NetTotalInvoice - value.Value);
            if (difference <= 1m)
                score += 80;
            else if (difference <= RegistroPagosClientesBalancedTolerance)
                score += 55;
            else if (difference <= 50000m)
                score += 25;
        }

        return score;
    }

    private static ConciliacionInvoiceLookupDto BuildConciliacionInvoiceLookupDto(
        BillingRecordRow row,
        decimal? searchedValue) =>
        new()
        {
            RecordId = row.RecordId,
            InvoiceNumber = row.InvoiceNumber,
            ClientName = row.ClientName,
            EmissionDateDisplay = row.EmissionDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
            TotalInvoice = row.NetTotalInvoice,
            PaymentValue = row.PaymentValue,
            ReteFteValue = ResolveCashFlowClientPaymentReteFteValue(row),
            ReteIcaValue = ResolveCashFlowClientPaymentReteIcaValue(row),
            RteIvaValue = ResolveCashFlowClientPaymentRteIvaValue(row),
            DifferenceWithEntry = searchedValue.HasValue
                ? RoundCurrency(row.NetTotalInvoice - searchedValue.Value)
                : 0m
        };

    private static string NormalizeConciliacionLookupText(string? value)
    {
        var decomposed = (value ?? "").Normalize(System.Text.NormalizationForm.FormD);
        var withoutDiacritics = new string(decomposed
            .Where(static character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .ToArray())
            .Normalize(System.Text.NormalizationForm.FormC);

        return Regex.Replace(
            withoutDiacritics.Trim().ToUpperInvariant(),
            @"\s+",
            " ",
            RegexOptions.CultureInvariant);
    }

    private static string NormalizeConciliacionLookupKey(string? value) =>
        Regex.Replace((value ?? "").ToUpperInvariant(), @"[^A-Z0-9]", "", RegexOptions.CultureInvariant);

    private static string NormalizeConciliacionDigits(string? value) =>
        Regex.Replace(value ?? "", @"\D", "", RegexOptions.CultureInvariant);

    private static int ParseConciliacionCashFlowSourceRowNumber(string? externalKey)
    {
        var match = Regex.Match(externalKey ?? "", @":(?<row>\d+)$", RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups["row"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNumber)
            ? rowNumber
            : 0;
    }

    private static int ParseConciliacionDianSourceRowNumber(string? reviewReason, string? excelKey)
    {
        var reasonMatch = Regex.Match(reviewReason ?? "", @"\bfila\s+(?<row>\d+)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (reasonMatch.Success && int.TryParse(reasonMatch.Groups["row"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNumber))
            return rowNumber;

        var keyMatch = Regex.Match(excelKey ?? "", @":(?<row>\d+)$", RegexOptions.CultureInvariant);
        return keyMatch.Success && int.TryParse(keyMatch.Groups["row"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out rowNumber)
            ? rowNumber
            : 0;
    }

    private async Task<IReadOnlyList<ConciliacionCashFlowRowDto>> GetConciliacionCashFlowRowsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct)
    {
        var movementMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var movementAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(movementMetadata.LogicalName, ct);
        var movementSelect = BuildConciliacionCashFlowMovementSelect(movementMetadata, movementAttributes);
        var movementFilter = BuildBillingDateFilter(CashFlowDateField, "date-only", startInclusive, endExclusive);
        var movementUrl = $"/api/data/v9.2/{movementMetadata.EntitySetName}?$select={movementSelect}&$filter={Uri.EscapeDataString(movementFilter)}&$orderby={CashFlowDateField} desc";
        var movementRows = await GetDataverseAppEntitiesAsync(movementUrl, ct);

        var transferMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowTransferLogicalName,
            CashFlowTransferSetName,
            CashFlowTransferIdField,
            CashFlowTransferPrimaryNameField,
            ct);
        var transferAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(transferMetadata.LogicalName, ct);
        var transferSelect = BuildConciliacionSelectClause(transferMetadata, transferAttributes, new[]
        {
            transferMetadata.PrimaryIdField,
            transferMetadata.PrimaryNameField,
            CashFlowTransferDateField,
            CashFlowTransferValueField,
            CashFlowTransferSourceFlowField,
            CashFlowTransferFromField,
            CashFlowTransferToField,
            CashFlowTransferEntryField,
            CashFlowTransferExitField,
            CashFlowTransferDescriptionField,
            CashFlowTransferRecipientField,
            CashFlowTransferDestinationBankField,
            CashFlowTransferDocumentTypeField,
            CashFlowTransferObservationsField,
            CashFlowTransferStatusField,
            CashFlowTransferExternalKeyField,
            CashFlowTransferSourceRowField,
            ConciliacionModifiedOnField
        });
        var transferFilter = BuildBillingDateFilter(CashFlowTransferDateField, "date-only", startInclusive, endExclusive);
        var transferUrl = $"/api/data/v9.2/{transferMetadata.EntitySetName}?$select={transferSelect}&$filter={Uri.EscapeDataString(transferFilter)}&$orderby={CashFlowTransferDateField} desc";
        var transferRows = await GetDataverseAppEntitiesAsync(transferUrl, ct);

        return movementRows
            .Select(item => ParseConciliacionCashFlowMovementRow(item, movementMetadata))
            .Concat(transferRows.Select(item => ParseConciliacionCashFlowTransferRow(item, transferMetadata)))
            .Where(static row => row is not null)
            .Cast<ConciliacionCashFlowRowDto>()
            .OrderByDescending(static row => row.MovementDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.SourceFlow, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Description, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ConciliacionCashFlowSummaryDto BuildConciliacionCashFlowSummary(
        IReadOnlyList<ConciliacionCashFlowRowDto> rows,
        IReadOnlyList<ConciliacionClientPaymentRowDto> clientPayments)
    {
        var matchByExternalKey = clientPayments
            .Where(static row => !string.IsNullOrWhiteSpace(row.MovementExternalKey))
            .GroupBy(static row => row.MovementExternalKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(row.ExternalKey)
                && ShouldApplyConciliacionClientPaymentMatch(row)
                && matchByExternalKey.TryGetValue(row.ExternalKey.Trim(), out var match))
            {
                ApplyConciliacionClientPaymentMatch(row, match);
            }
        }

        var lastRun = rows
            .Select(static row => ParseConciliacionDateTimeOffset(row.ModifiedOnValue))
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .DefaultIfEmpty()
            .Max();

        var conciliableRows = rows
            .Where(static row => !IsConciliacionNoIncludedCashFlow(row) && !IsConciliacionCashFlowOmitted(row))
            .ToArray();

        return new ConciliacionCashFlowSummaryDto
        {
            TotalRows = rows.Count,
            MovementRows = rows.Count(static row => string.Equals(row.SourceKind, "Movimiento", StringComparison.OrdinalIgnoreCase)),
            TransferRows = rows.Count(static row => string.Equals(row.SourceKind, "Traslado", StringComparison.OrdinalIgnoreCase)),
            EntryRows = conciliableRows.Count(static row => string.Equals(row.Direction, "Entrada", StringComparison.OrdinalIgnoreCase)),
            ExitRows = conciliableRows.Count(static row => string.Equals(row.Direction, "Salida", StringComparison.OrdinalIgnoreCase)),
            OutgoingInvoiceRows = rows.Count(static row => string.Equals(row.DetectedTypeKey, "salida-fe", StringComparison.OrdinalIgnoreCase)),
            IncomingInvoiceRows = rows.Count(static row => string.Equals(row.DetectedTypeKey, "entrada-fe", StringComparison.OrdinalIgnoreCase)),
            CollectionAccountRows = rows.Count(static row => string.Equals(row.DetectedTypeKey, "cuenta-cobro", StringComparison.OrdinalIgnoreCase)),
            AccountingVoucherRows = rows.Count(static row =>
                string.Equals(row.DetectedTypeKey, "comprobante-contable", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.DetectedTypeKey, "entrada-comprobante", StringComparison.OrdinalIgnoreCase)),
            OrphanRows = rows.Count(static row => string.Equals(row.DetectedTypeKey, "no-incluida-conciliacion", StringComparison.OrdinalIgnoreCase)),
            PendingValidationRows = conciliableRows.Count(static row => string.Equals(row.ValidationStatus, "Pendiente validar", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.ValidationStatus, "Revisar", StringComparison.OrdinalIgnoreCase)),
            PendingSiigoRows = conciliableRows.Count(static row => row.RegistrationStatus.Contains("Siigo pendiente", StringComparison.OrdinalIgnoreCase)),
            TotalEntries = RoundCurrency(conciliableRows.Sum(static row => row.EntryValue)),
            TotalExits = RoundCurrency(conciliableRows.Sum(static row => row.ExitValue)),
            TotalTransfers = RoundCurrency(conciliableRows.Where(static row => string.Equals(row.SourceKind, "Traslado", StringComparison.OrdinalIgnoreCase)).Sum(static row => row.Amount)),
            LastRunLabel = FormatConciliacionDateTimeDisplay(lastRun),
            BankSummaries = BuildConciliacionCashFlowBankSummaries(rows),
            AccountingVoucherGroups = BuildConciliacionAccountingVoucherGroups(rows),
            Rows = rows
        };
    }

    private static bool IsConciliacionClientPaymentMovementCandidate(ConciliacionCashFlowRowDto row) =>
        !string.Equals(row.SourceKind, "Traslado", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(row.DetectedTypeKey, "traslado-interno", StringComparison.OrdinalIgnoreCase)
        && !IsConciliacionCashFlowPendingReview(row)
        && !IsConciliacionCashFlowOmitted(row);

    internal static bool ShouldApplyConciliacionClientPaymentMatch(ConciliacionCashFlowRowDto row) =>
        IsConciliacionClientPaymentMovementCandidate(row)
        && !IsConciliacionCashFlowTerminal(row);

    internal static bool IsConciliacionCashFlowPendingReview(ConciliacionCashFlowRowDto row) =>
        string.Equals(row.DataverseStatus, ConciliacionCashFlowPendingReviewStatus, StringComparison.OrdinalIgnoreCase);

    internal static bool IsConciliacionCashFlowOmitted(ConciliacionCashFlowRowDto row) =>
        string.Equals(row.DataverseStatus, ConciliacionCashFlowOmittedStatus, StringComparison.OrdinalIgnoreCase);

    internal static bool IsConciliacionCashFlowTerminal(ConciliacionCashFlowRowDto row) =>
        !string.IsNullOrWhiteSpace(row.SiigoDocumentId)
        || IsConciliacionClientPaymentTerminalStatus(row.DataverseStatus)
        || IsConciliacionClientPaymentTerminalStatus(row.SiigoStatus);

    internal static bool IsConciliacionCashFlowFinal(ConciliacionCashFlowRowDto row) =>
        string.Equals(row.DataverseStatus, "Conciliado", StringComparison.OrdinalIgnoreCase)
        || string.Equals(row.SiigoStatus, "Conciliado", StringComparison.OrdinalIgnoreCase);

    private static bool IsConciliacionClientPaymentTerminalStatus(string? status) =>
        string.Equals(status, "Conciliado", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "EnviadoSiigo", StringComparison.OrdinalIgnoreCase);

    private static string ResolveConciliacionTerminalClientPaymentStatus(ConciliacionCashFlowRowDto row) =>
        string.Equals(row.DataverseStatus, "Conciliado", StringComparison.OrdinalIgnoreCase)
        || string.Equals(row.SiigoStatus, "Conciliado", StringComparison.OrdinalIgnoreCase)
            ? "Conciliado"
            : "EnviadoSiigo";

    private async Task<IReadOnlyList<ConciliacionDianSupplierInvoiceRowDto>> GetConciliacionDianSupplierInvoiceRowsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct,
        bool includeDataverseOnlyDocuments = false)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        EnsureDianSupplierDocumentDurabilitySchema(attributes);
        if (!await HasActiveDianSupplierDocumentExcelKeyAsync(metadata.LogicalName, ct))
        {
            throw new InvalidOperationException(
                $"Dataverse no tiene activa la clave unica sobre {ConciliacionDianExcelKeyField}; "
                + "la automatizacion DIAN/Siigo se detuvo para evitar duplicados.");
        }
        if (!await HasActiveDianSupplierDocumentSiigoDocumentIdKeyAsync(metadata.LogicalName, ct))
        {
            throw new InvalidOperationException(
                $"Dataverse no tiene activa la clave unica sobre {ConciliacionDianSiigoDocumentIdField}; "
                + "la automatizacion DIAN/Siigo se detuvo para impedir que dos CUFE se vinculen a la misma compra.");
        }
        if (!await HasActiveDianSupplierDocumentSiigoBusinessKeyAsync(metadata.LogicalName, ct))
        {
            throw new InvalidOperationException(
                $"Dataverse no tiene activa la clave unica sobre {DianSupplierDocumentSiigoBusinessKeyField}; "
                + "la automatizacion DIAN/Siigo se detuvo para impedir publicaciones concurrentes de la misma factura.");
        }
        var fields = ResolveTaxExpenseFieldMap(metadata, attributes);
        if (string.IsNullOrWhiteSpace(fields.EmissionDateField.FieldName))
            return Array.Empty<ConciliacionDianSupplierInvoiceRowDto>();

        var cufeField = ResolveTaxExpenseField(
            attributes,
            DianSupplierDocumentCufeField,
            "cr07a_cufecude",
            "cr07a_cufe",
            "cr07a_cude");
        var baseAmountField = ResolveTaxExpenseField(
            attributes,
            DashboardExpenseTotalBeforeVatField,
            "cr07a_base",
            "cr07a_baseiva",
            "cr07a_totalantesdeimpuestos");

        var select = BuildConciliacionSelectClause(metadata, attributes, new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            fields.InvoiceNumberField,
            fields.EmissionDateField.FieldName,
            fields.PaymentDateField.FieldName,
            fields.PaymentValueField,
            fields.TotalField,
            fields.VatField,
            fields.ReteFuenteField,
            fields.ReteIcaField,
            fields.IssuerNameField,
            ConciliacionDianIssuerNitField,
            fields.RecipientNameField,
            fields.RecipientNitField,
            fields.CloudField,
            fields.CopiersField,
            ConciliacionDianDocumentTypeField,
            ConciliacionDianPrefixField,
            ConciliacionDianFolioField,
            cufeField,
            baseAmountField,
            DianSupplierDocumentReceptionDateField,
            DianSupplierDocumentStatusField,
            DianSupplierDocumentGroupField,
            DianSupplierDocumentPaymentFormField,
            DianSupplierDocumentPaymentMethodField,
            DianSupplierDocumentCurrencyField,
            DianSupplierDocumentReteIvaField,
            DianSupplierDocumentSiigoSupplierIdField,
            DianSupplierDocumentSiigoSupplierNameField,
            DashboardExpenseCategoryField,
            ExpenseAccountCodeField,
            ExpenseAccountNameField,
            ExpenseAutomationStateField,
            ExpenseAutomationConfidenceField,
            ExpenseReviewReasonField,
            ConciliacionDianSourceField,
            ConciliacionDianExcelKeyField,
            DianSupplierDocumentSiigoBusinessKeyField,
            ConciliacionDianSiigoDocumentIdField,
            ConciliacionDianSiigoDocumentNameField,
            ConciliacionCreatedOnField,
            ConciliacionModifiedOnField
        });

        var periodField = DianSupplierDocumentReceptionDateField;
        var filter = BuildConciliacionReceptionDateFilter(periodField, startInclusive, endExclusive);
        if (includeDataverseOnlyDocuments)
        {
            var emissionFilter = BuildBillingDateFilter(
                fields.EmissionDateField.FieldName,
                "date-only",
                startInclusive,
                endExclusive);
            filter = $"({filter}) or ({emissionFilter})";
        }
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={periodField} desc";
        var items = await GetDataverseAppEntitiesAsync(url, ct, AddFormattedValueHeaders);

        var rows = items
            .Select(item => ParseConciliacionDianSupplierInvoiceRow(item, metadata, fields, cufeField, baseAmountField))
            .Where(row => row is not null
                && (IsConciliacionDianSupplierImportableDocument(row)
                    || (includeDataverseOnlyDocuments && IsConciliacionDianDataverseOnlyDocument(row))))
            .Cast<ConciliacionDianSupplierInvoiceRowDto>()
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderByDescending(static row => row.EmissionDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.SupplierName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var incomplete = rows
            .Where(static row => string.IsNullOrWhiteSpace(row.Cufe)
                || string.IsNullOrWhiteSpace(row.ExcelKey)
                || (!IsConciliacionDianDataverseOnlyDocument(row) && string.IsNullOrWhiteSpace(row.ReceptionDateValue))
                || string.IsNullOrWhiteSpace(row.AutomationSource)
                || string.IsNullOrWhiteSpace(row.ConcurrencyToken))
            .Select(static row => FirstNonEmpty(row.RecordId, row.InvoiceNumber, "sin identificador"))
            .Take(10)
            .ToArray();
        if (incomplete.Length > 0)
        {
            throw new InvalidOperationException(
                "Hay documentos DIAN sin CUFE/CUDE, ExcelKey, fecha de recepcion requerida, fuente DIAN o ETag durable. "
                + $"Registros: {string.Join(", ", incomplete)}. No se publicara nada en Siigo.");
        }

        var duplicateCufes = rows
            .GroupBy(static row => NormalizeConciliacionCufeCude(row.Cufe), StringComparer.OrdinalIgnoreCase)
            .Where(static group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(static group => group.Key)
            .Take(10)
            .ToArray();
        if (duplicateCufes.Length > 0)
        {
            throw new InvalidOperationException(
                "Dataverse contiene CUFE/CUDE duplicados en el periodo. La automatizacion se detuvo antes de Siigo. "
                + $"CUFE/CUDE: {string.Join(", ", duplicateCufes)}.");
        }

        return rows;
    }

    private static ConciliacionDianSupplierInvoiceSummaryDto BuildConciliacionDianSupplierInvoiceSummary(
        IReadOnlyList<ConciliacionDianSupplierInvoiceRowDto> rows)
    {
        var lastRun = rows
            .Select(static row => ParseConciliacionDateTimeOffset(row.ModifiedOnDisplay))
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .DefaultIfEmpty()
            .Max();

        return new ConciliacionDianSupplierInvoiceSummaryDto
        {
            TotalRows = rows.Count,
            ProviderPending = rows.Count(static row => string.Equals(row.Stage, "proveedor", StringComparison.OrdinalIgnoreCase)),
            ClassificationPending = rows.Count(static row => string.Equals(row.Stage, "clasificacion", StringComparison.OrdinalIgnoreCase)),
            ReadyForPurchase = rows.Count(static row => string.Equals(row.Stage, "prevalidacion", StringComparison.OrdinalIgnoreCase)),
            SentToSiigo = rows.Count(static row => string.Equals(row.Stage, "enviadas", StringComparison.OrdinalIgnoreCase)),
            WithErrors = rows.Count(static row => string.Equals(row.StageTone, "danger", StringComparison.OrdinalIgnoreCase)),
            TotalValue = RoundCurrency(rows.Sum(static row => row.TotalValue)),
            LastRunLabel = FormatConciliacionDateTimeDisplay(lastRun),
            Rows = rows
        };
    }

    private ConciliacionDianSupplierInvoiceRowDto? ParseConciliacionDianSupplierInvoiceRow(
        JsonElement item,
        RhEntityMetadata metadata,
        TaxExpenseFieldMap fields,
        string cufeField,
        string baseAmountField)
    {
        var taxRow = ParseTaxExpenseRow(item, metadata.PrimaryIdField, fields);
        if (taxRow is null)
            return null;

        var prefix = ReadString(item, ConciliacionDianPrefixField).Trim();
        var folio = ReadString(item, ConciliacionDianFolioField).Trim();
        var invoiceNumber = BuildConciliacionDianInvoiceNumber(prefix, folio, taxRow.InvoiceNumber);
        var baseAmount = RoundCurrency(ReadDecimal(item, baseAmountField) ?? Math.Max(0m, taxRow.TotalValue - taxRow.VatValue));
        var modifiedOn = ParseConciliacionDateTimeOffset(ReadString(item, ConciliacionModifiedOnField));
        var createdOn = ParseConciliacionDateTimeOffset(ReadString(item, ConciliacionCreatedOnField));
        var receptionDate = ParseConciliacionDateTimeOffset(ReadString(item, DianSupplierDocumentReceptionDateField));
        var documentType = FirstNonEmpty(
            ReadString(item, $"{ConciliacionDianDocumentTypeField}{FormattedValueAnnotationSuffix}"),
            ReadString(item, ConciliacionDianDocumentTypeField),
            "Documento proveedor");
        var dianGroup = ReadString(item, DianSupplierDocumentGroupField).Trim();
        var supplierName = taxRow.IssuerName;
        var supplierNit = ReadString(item, ConciliacionDianIssuerNitField).Trim();
        var recipientName = taxRow.RecipientName;
        var recipientNit = taxRow.RecipientNit;
        if (ShouldFlipConciliacionDianSupportSupplier(documentType, dianGroup, supplierName, recipientName))
        {
            (supplierName, recipientName) = (recipientName, supplierName);
            (supplierNit, recipientNit) = (recipientNit, supplierNit);
        }
        var reviewReason = RepairSpanishMojibakeText(ReadString(item, ExpenseReviewReasonField)).Trim();
        var excelKey = ReadString(item, ConciliacionDianExcelKeyField).Trim();
        var siigoBusinessKey = ReadString(item, DianSupplierDocumentSiigoBusinessKeyField).Trim();
        var automationSource = ReadString(item, ConciliacionDianSourceField).Trim();

        var row = new ConciliacionDianSupplierInvoiceRowDto
        {
            RecordId = taxRow.RecordId,
            DocumentType = documentType.Trim(),
            Prefix = prefix,
            Folio = folio,
            InvoiceNumber = invoiceNumber,
            Cufe = ReadString(item, cufeField).Trim(),
            EmissionDateValue = taxRow.EmissionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            EmissionDateDisplay = taxRow.EmissionDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
            ReceptionDateValue = receptionDate?.ToOffset(TimeSpan.FromHours(-5)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            ReceptionDateDisplay = FormatConciliacionDateTimeDisplay(receptionDate),
            DianStatus = ReadString(item, DianSupplierDocumentStatusField).Trim(),
            DianGroup = dianGroup,
            PaymentForm = ReadString(item, DianSupplierDocumentPaymentFormField).Trim(),
            PaymentMethod = ReadString(item, DianSupplierDocumentPaymentMethodField).Trim(),
            Currency = ReadString(item, DianSupplierDocumentCurrencyField).Trim(),
            SupplierNit = supplierNit,
            SupplierName = supplierName,
            RecipientNit = recipientNit,
            RecipientName = recipientName,
            BaseAmount = baseAmount,
            VatValue = taxRow.VatValue,
            ReteFuenteValue = taxRow.ReteFuenteValue,
            ReteIcaValue = taxRow.ReteIcaValue,
            ReteIvaValue = RoundCurrency(ReadDecimal(item, DianSupplierDocumentReteIvaField) ?? 0m),
            TotalValue = taxRow.TotalValue,
            PaymentValue = taxRow.PaymentValue,
            CloudValue = taxRow.CloudValue,
            CopiersValue = taxRow.CopiersValue,
            VerticalLabel = ResolveConciliacionDianVerticalLabel(taxRow.CloudValue, taxRow.CopiersValue),
            CategoryValue = ReadString(item, DashboardExpenseCategoryField).Trim(),
            CategoryLabel = RepairSpanishMojibakeText(FirstNonEmpty(
                ReadString(item, $"{DashboardExpenseCategoryField}{FormattedValueAnnotationSuffix}"),
                ReadString(item, DashboardExpenseCategoryField),
                "Sin categoria")).Trim(),
            AccountCode = ReadString(item, ExpenseAccountCodeField).Trim(),
            AccountName = ResolveAccountCatalogName(ReadString(item, ExpenseAccountCodeField), ReadString(item, ExpenseAccountNameField)),
            AutomationState = ReadString(item, ExpenseAutomationStateField).Trim(),
            ReviewReason = reviewReason,
            SiigoDocumentId = ReadString(item, ConciliacionDianSiigoDocumentIdField).Trim(),
            SiigoDocumentName = ReadString(item, ConciliacionDianSiigoDocumentNameField).Trim(),
            SiigoSupplierId = ReadString(item, DianSupplierDocumentSiigoSupplierIdField).Trim(),
            SiigoSupplierName = ReadString(item, DianSupplierDocumentSiigoSupplierNameField).Trim(),
            AutomationSource = automationSource,
            ExcelKey = excelKey,
            SiigoBusinessKey = siigoBusinessKey,
            SourceLabel = FirstNonEmpty(automationSource, excelKey, "Dataverse").Trim(),
            SourceRowNumber = ParseConciliacionDianSourceRowNumber(reviewReason, excelKey),
            ConcurrencyToken = ReadString(item, "@odata.etag").Trim(),
            CreatedAt = createdOn,
            ModifiedAt = modifiedOn,
            ModifiedOnDisplay = FormatConciliacionDateTimeDisplay(modifiedOn)
        };

        CompleteConciliacionDianSupplierInvoiceRow(row);
        return row;
    }

    private static string BuildConciliacionReceptionDateFilter(
        string fieldName,
        DateOnly startInclusive,
        DateOnly endExclusive)
    {
        var colombiaOffset = TimeSpan.FromHours(-5);
        var startUtc = new DateTimeOffset(startInclusive.ToDateTime(TimeOnly.MinValue), colombiaOffset).ToUniversalTime();
        var endUtc = new DateTimeOffset(endExclusive.ToDateTime(TimeOnly.MinValue), colombiaOffset).ToUniversalTime();
        return $"{fieldName} ge {startUtc:yyyy-MM-ddTHH:mm:ssZ} and {fieldName} lt {endUtc:yyyy-MM-ddTHH:mm:ssZ}";
    }

    private static bool IsConciliacionDianSupplierImportableDocument(ConciliacionDianSupplierInvoiceRowDto? row)
    {
        if (row is null)
            return false;

        var type = NormalizeConciliacionLookupText(row.DocumentType);
        var group = NormalizeConciliacionLookupText(row.DianGroup);
        var isInvoice = type.Contains("FACTURA ELECTRONICA", StringComparison.OrdinalIgnoreCase)
            && !type.Contains("NOTA", StringComparison.OrdinalIgnoreCase);
        var isSupplierCreditNote = type.Contains("NOTA DE CREDITO", StringComparison.OrdinalIgnoreCase)
            || type.Contains("CREDIT NOTE", StringComparison.OrdinalIgnoreCase);
        return (isInvoice || isSupplierCreditNote)
            && !type.Contains("APPLICATION RESPONSE", StringComparison.OrdinalIgnoreCase)
            && group.Contains("RECIBID", StringComparison.OrdinalIgnoreCase)
            && !group.Contains("EMITID", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConciliacionDianPayroll(ConciliacionDianSupplierInvoiceRowDto? row)
    {
        if (row is null)
            return false;

        var type = NormalizeConciliacionLookupText(row.DocumentType);
        var group = NormalizeConciliacionLookupText(row.DianGroup);
        return type.Contains("NOMINA INDIVIDUAL", StringComparison.OrdinalIgnoreCase)
            && group.Contains("EMITID", StringComparison.OrdinalIgnoreCase)
            && !group.Contains("RECIBID", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConciliacionDianSupportDocument(ConciliacionDianSupplierInvoiceRowDto? row)
    {
        if (row is null)
            return false;

        var type = NormalizeConciliacionLookupText(row.DocumentType);
        var group = NormalizeConciliacionLookupText(row.DianGroup);
        var isSupportDocument = type.Contains("DOCUMENTO SOPORTE", StringComparison.OrdinalIgnoreCase)
            || type.Contains("DOC SOPORTE", StringComparison.OrdinalIgnoreCase)
            || type.Contains("SOPORTE CON NO OBLIGADOS", StringComparison.OrdinalIgnoreCase);
        return isSupportDocument
            && !type.Contains("NOTA", StringComparison.OrdinalIgnoreCase)
            && group.Contains("EMITID", StringComparison.OrdinalIgnoreCase)
            && !group.Contains("RECIBID", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsConciliacionDianDataverseOnlyDocument(ConciliacionDianSupplierInvoiceRowDto? row) =>
        IsConciliacionDianPayroll(row) || IsConciliacionDianSupportDocument(row);

    private static bool IsConciliacionDianSupplierInvoice(ConciliacionDianSupplierInvoiceRowDto? row)
    {
        if (row is null)
            return false;

        var type = NormalizeConciliacionLookupText(row.DocumentType);
        var group = NormalizeConciliacionLookupText(row.DianGroup);
        return type.Contains("FACTURA ELECTRONICA", StringComparison.OrdinalIgnoreCase)
            && !type.Contains("NOTA", StringComparison.OrdinalIgnoreCase)
            && !type.Contains("APPLICATION RESPONSE", StringComparison.OrdinalIgnoreCase)
            && group.Contains("RECIBID", StringComparison.OrdinalIgnoreCase)
            && !group.Contains("EMITID", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldFlipConciliacionDianSupportSupplier(
        string documentType,
        string group,
        string issuerName,
        string recipientName)
    {
        var type = NormalizeConciliacionLookupText(documentType);
        var normalizedGroup = NormalizeConciliacionLookupText(group);
        if (!type.Contains("SOPORTE", StringComparison.OrdinalIgnoreCase)
            || !normalizedGroup.Contains("EMITIDO", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var issuerIsCompany = issuerName.Contains("DIGITAL", StringComparison.OrdinalIgnoreCase)
            || issuerName.Contains("COPIERS", StringComparison.OrdinalIgnoreCase)
            || issuerName.Contains("CLOUD", StringComparison.OrdinalIgnoreCase);
        var recipientIsCompany = recipientName.Contains("DIGITAL", StringComparison.OrdinalIgnoreCase)
            || recipientName.Contains("COPIERS", StringComparison.OrdinalIgnoreCase)
            || recipientName.Contains("CLOUD", StringComparison.OrdinalIgnoreCase);

        return issuerIsCompany && !recipientIsCompany;
    }

    private static void CompleteConciliacionDianSupplierInvoiceRow(ConciliacionDianSupplierInvoiceRowDto row)
    {
        var documentType = NormalizeConciliacionLookupText(row.DocumentType);
        var isSupplierCreditNote = documentType.Contains("NOTA DE CREDITO", StringComparison.OrdinalIgnoreCase)
            || documentType.Contains("CREDIT NOTE", StringComparison.OrdinalIgnoreCase);
        var ambiguousSiigoWrite = row.AutomationState.Equals("VerificacionSiigoPendiente", StringComparison.OrdinalIgnoreCase)
            || row.ReviewReason.Contains("[SIIGO_WRITE_AMBIGUOUS]", StringComparison.OrdinalIgnoreCase);
        var hasSupplierData = !string.IsNullOrWhiteSpace(row.SupplierNit) && !string.IsNullOrWhiteSpace(row.SupplierName);
        var hasSiigoSupplier = !string.IsNullOrWhiteSpace(row.SiigoSupplierId);
        var classified = !string.IsNullOrWhiteSpace(row.AccountCode);
        var sentToSiigo = !string.IsNullOrWhiteSpace(row.SiigoDocumentId);

        row.ProviderStatusLabel = !hasSupplierData
            ? "Proveedor incompleto"
            : hasSiigoSupplier
                ? "Proveedor Siigo OK"
                : "Proveedor pendiente Siigo";
        row.ProviderStatusTone = hasSupplierData && hasSiigoSupplier ? "success" : "warning";
        row.ClassificationStatusLabel = classified ? "Clasificacion OK" : "Pendiente clasificacion";
        row.ClassificationStatusTone = classified ? "success" : "warning";
        row.SiigoStatusLabel = ambiguousSiigoWrite
            ? "Confirmacion Siigo pendiente"
            : sentToSiigo
                ? isSupplierCreditNote ? "Nota aplicada en Siigo" : "Documento Siigo OK"
                : isSupplierCreditNote ? "Pendiente aplicar nota" : "Pendiente documento Siigo";
        row.SiigoStatusTone = sentToSiigo && !ambiguousSiigoWrite ? "success" : "warning";

        if (sentToSiigo && !ambiguousSiigoWrite)
        {
            row.Stage = "enviadas";
            row.StageLabel = isSupplierCreditNote ? "Nota aplicada en Siigo" : "Subida a Siigo";
            row.StageTone = "success";
            return;
        }

        if (!hasSupplierData || !hasSiigoSupplier)
        {
            row.Stage = "proveedor";
            row.StageLabel = "Proveedor pendiente";
            row.StageTone = "warning";
            return;
        }

        if (!classified)
        {
            row.Stage = "clasificacion";
            row.StageLabel = "Clasificacion pendiente";
            row.StageTone = "warning";
            return;
        }

        row.Stage = "prevalidacion";
        row.StageLabel = ambiguousSiigoWrite
            ? "Verificacion Siigo pendiente"
            : isSupplierCreditNote
                ? "Lista para aplicar"
                : "Lista para compra";
        row.StageTone = ambiguousSiigoWrite ? "warning" : "info";
    }

    private static bool IsConciliacionExpenseClassificationPending(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return true;

        return state.Contains("Pendiente", StringComparison.OrdinalIgnoreCase)
            || state.Contains("SinRegla", StringComparison.OrdinalIgnoreCase)
            || state.Contains("ReglaInvalida", StringComparison.OrdinalIgnoreCase)
            || state.Contains("Revision", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildConciliacionDianInvoiceNumber(string prefix, string folio, string fallback)
    {
        var joined = string.Join("-", new[] { prefix, folio }.Where(static value => !string.IsNullOrWhiteSpace(value)));
        return FirstNonEmpty(joined, fallback, "Sin factura").Trim();
    }

    private static string ResolveConciliacionDianVerticalLabel(decimal cloud, decimal copiers)
    {
        if (cloud > 0m && copiers > 0m)
            return "Cloud / Copiers";

        if (cloud > 0m)
            return "Cloud";

        if (copiers > 0m)
            return "Copiers";

        return "Sin vertical";
    }

    private static string BuildConciliacionSelectClause(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        IEnumerable<string> fields)
    {
        var selected = fields
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Where(field => attributes.Count == 0
                || attributes.Contains(field)
                || string.Equals(field, metadata.PrimaryIdField, StringComparison.OrdinalIgnoreCase)
                || string.Equals(field, metadata.PrimaryNameField, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return selected.Length > 0 ? string.Join(",", selected) : metadata.PrimaryIdField;
    }

    private static ConciliacionCashFlowRowDto? ParseConciliacionCashFlowMovementRow(
        JsonElement item,
        RhEntityMetadata metadata)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, metadata.PrimaryIdField),
            ReadString(item, CashFlowMovementIdField)).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var date = ReadDateOnly(item, CashFlowDateField);
        var entry = RoundCurrency(ReadDecimal(item, CashFlowEntryField) ?? 0m);
        var exit = RoundCurrency(ReadDecimal(item, CashFlowExitField) ?? 0m);
        var row = new ConciliacionCashFlowRowDto
        {
            RecordId = recordId,
            SourceKind = "Movimiento",
            SourceKindLabel = "Movimiento",
            MovementDateValue = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            MovementDateDisplay = date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
            SourceFlow = ReadString(item, CashFlowSourceFlowField).Trim(),
            BankAccountCode = ReadString(item, CashFlowBankAccountCodeField).Trim(),
            BankAccountName = FirstNonEmpty(
                ReadString(item, CashFlowBankAccountNameField),
                ReadString(item, CashFlowBankField)).Trim(),
            EntryValue = entry,
            ExitValue = exit,
            Amount = RoundCurrency(Math.Max(entry, exit)),
            Description = ReadString(item, CashFlowDescriptionField).Trim(),
            Recipient = ReadString(item, CashFlowRecipientField).Trim(),
            DestinationBank = ReadString(item, CashFlowDestinationBankField).Trim(),
            DocumentType = ReadString(item, CashFlowDocumentTypeField).Trim(),
            Observations = ReadString(item, CashFlowObservationsField).Trim(),
            ExcelMovementType = ReadString(item, CashFlowMovementTypeField).Trim(),
            SourceRowNumber = ReadInt(item, CashFlowSourceRowField),
            DataverseStatus = FirstNonEmpty(ReadString(item, CashFlowStatusField), "Importado").Trim(),
            ReviewReason = ReadString(item, CashFlowReviewReasonField).Trim(),
            SiigoStatus = ReadString(item, CashFlowSiigoStatusField).Trim(),
            SiigoDocumentId = ReadString(item, CashFlowSiigoDocumentIdField).Trim(),
            SiigoDocumentName = ReadString(item, CashFlowSiigoDocumentNameField).Trim(),
            AccountCode = ReadString(item, CashFlowAccountingAccountCodeField).Trim(),
            AccountName = ResolveAccountCatalogName(ReadString(item, CashFlowAccountingAccountCodeField), ReadString(item, CashFlowAccountingAccountNameField)),
            ThirdPartyId = ReadString(item, CashFlowThirdPartyKeyField).Trim(),
            ThirdPartyIdentification = ReadString(item, CashFlowThirdPartyIdentificationField).Trim(),
            ThirdPartyName = ReadString(item, CashFlowThirdPartyNameField).Trim(),
            ThirdPartyBranchOffice = ReadInt(item, CashFlowThirdPartyBranchOfficeField),
            ExternalKey = ReadString(item, CashFlowExternalKeyField).Trim(),
            ModifiedOnValue = ReadString(item, ConciliacionModifiedOnField).Trim()
        };

        row.Direction = entry > 0m ? "Entrada" : exit > 0m ? "Salida" : "Sin valor";
        row.DirectionTone = entry > 0m ? "success" : exit > 0m ? "danger" : "neutral";
        CompleteConciliacionCashFlowRow(
            row,
            row.SiigoDocumentId,
            row.SiigoStatus);
        return row;
    }

    private static ConciliacionCashFlowRowDto? ParseConciliacionCashFlowTransferRow(
        JsonElement item,
        RhEntityMetadata metadata)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, metadata.PrimaryIdField),
            ReadString(item, CashFlowTransferIdField)).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var date = ReadDateOnly(item, CashFlowTransferDateField);
        var entry = RoundCurrency(ReadDecimal(item, CashFlowTransferEntryField) ?? 0m);
        var exit = RoundCurrency(ReadDecimal(item, CashFlowTransferExitField) ?? 0m);
        var value = RoundCurrency(ReadDecimal(item, CashFlowTransferValueField) ?? Math.Max(entry, exit));
        var transferFrom = ReadString(item, CashFlowTransferFromField).Trim();
        var transferTo = ReadString(item, CashFlowTransferToField).Trim();
        var sourceFlow = ReadString(item, CashFlowTransferSourceFlowField).Trim();
        var currentBank = ResolveConciliacionCashFlowBankAccount(sourceFlow);
        var counterpartFlow = ResolveConciliacionTransferCounterpartFlow(sourceFlow, transferFrom, transferTo, entry, exit);
        var counterpartBank = ResolveConciliacionCashFlowBankAccount(counterpartFlow);
        var direction = entry > 0m ? "Entrada" : exit > 0m ? "Salida" : "Sin valor";
        var row = new ConciliacionCashFlowRowDto
        {
            RecordId = recordId,
            SourceKind = "Traslado",
            SourceKindLabel = "Traslado interno",
            MovementDateValue = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            MovementDateDisplay = date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
            SourceFlow = sourceFlow,
            BankAccountCode = currentBank.Code,
            BankAccountName = currentBank.Name,
            Direction = direction,
            DirectionTone = entry > 0m ? "success" : exit > 0m ? "danger" : "neutral",
            EntryValue = entry,
            ExitValue = exit,
            Amount = value,
            Description = FirstNonEmpty(
                ReadString(item, CashFlowTransferDescriptionField),
                $"Traslado interno {transferFrom} a {transferTo}").Trim(),
            Recipient = ReadString(item, CashFlowTransferRecipientField).Trim(),
            DestinationBank = ReadString(item, CashFlowTransferDestinationBankField).Trim(),
            DocumentType = ReadString(item, CashFlowTransferDocumentTypeField).Trim(),
            Observations = ReadString(item, CashFlowTransferObservationsField).Trim(),
            ExcelMovementType = FirstNonEmpty(ReadString(item, CashFlowTransferStatusField), "TRASLADO").Trim(),
            SourceRowNumber = ReadInt(item, CashFlowTransferSourceRowField),
            DataverseStatus = FirstNonEmpty(ReadString(item, CashFlowTransferStatusField), "InternoNoSiigo").Trim(),
            SiigoStatus = FirstNonEmpty(ReadString(item, CashFlowTransferStatusField), "").Trim(),
            AccountCode = counterpartBank.Code,
            AccountName = counterpartBank.Name,
            ExternalKey = ReadString(item, CashFlowTransferExternalKeyField).Trim(),
            ModifiedOnValue = ReadString(item, ConciliacionModifiedOnField).Trim()
        };

        CompleteConciliacionCashFlowRow(row, "", row.SiigoStatus);
        return row;
    }

    internal static void CompleteConciliacionCashFlowRow(
        ConciliacionCashFlowRowDto row,
        string siigoDocumentId,
        string siigoStatus)
    {
        var detection = ResolveConciliacionCashFlowDetectedType(row);
        row.DetectedTypeKey = detection.Key;
        row.DetectedTypeLabel = detection.Label;
        row.DetectedTypeTone = detection.Tone;
        row.ActionTargetKey = detection.TargetKey;
        if (string.Equals(detection.Key, "traslado-interno", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(row.AccountCode))
        {
            var counterpartFlow = ResolveConciliacionTransferCounterpartFlow(
                row.SourceFlow,
                FirstNonEmpty(row.Description, row.Observations),
                row.DestinationBank,
                row.EntryValue,
                row.ExitValue);
            var counterpartBank = ResolveConciliacionCashFlowBankAccount(counterpartFlow);
            row.AccountCode = counterpartBank.Code;
            row.AccountName = counterpartBank.Name;
        }
        row.CanValidate = !string.Equals(detection.Key, "no-incluida-conciliacion", StringComparison.OrdinalIgnoreCase);

        if (IsConciliacionCashFlowOmitted(row))
        {
            row.CanValidate = false;
            row.ValidationStatus = "Omitido";
            row.ValidationTone = "neutral";
            row.RegistrationStatus = "Dataverse OK / omitido";
            row.RegistrationTone = "neutral";
            row.InvoiceStatus = "No aplica";
            row.InvoiceStatusTone = "neutral";
            row.SiigoDocumentStatus = "No aplica";
            row.SiigoDocumentTone = "neutral";
            row.SiigoPaymentStatus = "No aplica";
            row.SiigoPaymentTone = "neutral";
            row.InvoiceBalanceStatus = "No aplica";
            row.DataversePaymentStatus = "Observacion de omision guardada";
            row.DataversePaymentTone = "neutral";
            return;
        }

        if (IsConciliacionCashFlowPendingReview(row))
        {
            row.ValidationStatus = "Pendiente por verificar";
            row.ValidationTone = "warning";
            row.RegistrationStatus = "Dataverse OK / conciliacion pendiente";
            row.RegistrationTone = "warning";
            row.InvoiceStatus = "Por verificar";
            row.InvoiceStatusTone = "warning";
            row.SiigoDocumentStatus = "No enviado";
            row.SiigoDocumentTone = "warning";
            row.SiigoPaymentStatus = "No enviado";
            row.SiigoPaymentTone = "warning";
            row.InvoiceBalanceStatus = "Por verificar";
            row.DataversePaymentStatus = "Motivo pendiente guardado";
            row.DataversePaymentTone = "warning";
            return;
        }

        if (IsConciliacionCashFlowPostSendChange(row.DataverseStatus, siigoStatus))
        {
            ApplyConciliacionCashFlowPostSendChange(row);
            return;
        }

        if (string.Equals(detection.Key, "traslado-interno", StringComparison.OrdinalIgnoreCase))
        {
            var transferConciliated = IsConciliacionCashFlowFinal(row);
            var transferRegistered = IsConciliacionSiigoRegistered(siigoDocumentId, siigoStatus);
            if (transferConciliated)
            {
                row.ValidationStatus = "Validada";
                row.ValidationTone = "success";
                row.RegistrationStatus = "Dataverse OK / Siigo OK";
                row.RegistrationTone = "success";
                row.InvoiceStatus = "No aplica";
                row.InvoiceStatusTone = "success";
                row.SiigoDocumentStatus = "Siigo OK";
                row.SiigoDocumentTone = "success";
                row.SiigoPaymentStatus = "Comprobante de traslado enviado";
                row.SiigoPaymentTone = "success";
                row.InvoiceBalanceStatus = "No aplica";
                row.DataversePaymentStatus = "Traslado conciliado";
                row.DataversePaymentTone = "success";
                return;
            }

            if (transferRegistered)
            {
                row.ValidationStatus = "Pendiente cierre Dataverse";
                row.ValidationTone = "warning";
                row.RegistrationStatus = "Dataverse pendiente de cierre / Siigo OK";
                row.RegistrationTone = "warning";
                row.InvoiceStatus = "No aplica";
                row.InvoiceStatusTone = "warning";
                row.SiigoDocumentStatus = "Siigo OK";
                row.SiigoDocumentTone = "success";
                row.SiigoPaymentStatus = "Comprobante de traslado enviado";
                row.SiigoPaymentTone = "success";
                row.InvoiceBalanceStatus = "No aplica";
                row.DataversePaymentStatus = "Pendiente cierre Dataverse";
                row.DataversePaymentTone = "warning";
                return;
            }

            row.ValidationStatus = "Interno / fase Siigo pendiente";
            row.ValidationTone = "info";
            row.RegistrationStatus = "Dataverse OK / Siigo pendiente";
            row.RegistrationTone = "info";
            row.InvoiceStatus = "No aplica";
            row.InvoiceStatusTone = "info";
            row.SiigoDocumentStatus = "Pendiente siguiente fase";
            row.SiigoDocumentTone = "info";
            row.SiigoPaymentStatus = "No aplica";
            row.SiigoPaymentTone = "info";
            row.InvoiceBalanceStatus = "No aplica";
            row.DataversePaymentStatus = "Traslado guardado";
            row.DataversePaymentTone = "info";
            return;
        }

        if (string.Equals(detection.Key, "no-incluida-conciliacion", StringComparison.OrdinalIgnoreCase))
        {
            row.ValidationStatus = "No incluida";
            row.ValidationTone = "neutral";
            row.RegistrationStatus = "Dataverse OK / no aplica Siigo";
            row.RegistrationTone = "neutral";
            row.InvoiceStatus = "No aplica";
            row.InvoiceStatusTone = "neutral";
            row.SiigoDocumentStatus = "No aplica";
            row.SiigoDocumentTone = "neutral";
            row.SiigoPaymentStatus = "No aplica";
            row.SiigoPaymentTone = "neutral";
            row.InvoiceBalanceStatus = "No aplica";
            row.DataversePaymentStatus = "Excluida de conciliacion";
            row.DataversePaymentTone = "neutral";
            return;
        }

        var finalConciliated = IsConciliacionCashFlowFinal(row);
        var siigoRegistered = finalConciliated || IsConciliacionSiigoRegistered(siigoDocumentId, siigoStatus);
        row.ValidationStatus = finalConciliated ? "Validada" : "Pendiente validar";
        row.ValidationTone = finalConciliated ? "success" : "warning";
        row.RegistrationStatus = finalConciliated
            ? "Dataverse OK / Siigo OK"
            : siigoRegistered
                ? "Dataverse pendiente de cierre / Siigo OK"
                : "Dataverse OK / Siigo pendiente";
        row.RegistrationTone = finalConciliated ? "success" : "warning";
        row.InvoiceStatus = ResolveDefaultInvoiceStatus(row.DetectedTypeKey);
        row.InvoiceStatusTone = row.InvoiceStatus.Contains("OK", StringComparison.OrdinalIgnoreCase) ? "success" : "warning";
        row.SiigoDocumentStatus = siigoRegistered ? "Siigo OK" : "Pendiente Siigo";
        row.SiigoDocumentTone = siigoRegistered ? "success" : "warning";
        row.SiigoPaymentStatus = siigoRegistered ? "Pago/registro Siigo detectado" : "Pendiente envio Siigo";
        row.SiigoPaymentTone = siigoRegistered ? "success" : "warning";
        row.InvoiceBalanceStatus = string.Equals(row.DetectedTypeKey, "salida-fe", StringComparison.OrdinalIgnoreCase)
            ? "Saldo sin calcular"
            : "No aplica";
        row.DataversePaymentStatus = finalConciliated ? "Conciliacion Dataverse OK" : "Pendiente cierre Dataverse";
        row.DataversePaymentTone = finalConciliated ? "success" : "warning";
    }

    private static IReadOnlyList<ConciliacionCashFlowBankSummaryDto> BuildConciliacionCashFlowBankSummaries(
        IReadOnlyList<ConciliacionCashFlowRowDto> rows)
    {
        return rows
            .Where(static row => !string.Equals(row.Direction, "Traslado", StringComparison.OrdinalIgnoreCase))
            .Where(static row => !IsConciliacionNoIncludedCashFlow(row))
            .GroupBy(static row => BuildConciliacionCashFlowBankKey(row), StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
            {
                var first = group.First();
                var reported = group.Count(IsConciliacionCashFlowReportedToSiigo);
                return new ConciliacionCashFlowBankSummaryDto
                {
                    BankKey = group.Key,
                    BankLabel = BuildConciliacionCashFlowBankLabel(first),
                    RowsFound = group.Count(),
                    ReportedToSiigo = reported,
                    PendingConciliation = group.Count(row =>
                        !IsConciliacionNoIncludedCashFlow(row)
                        && !IsConciliacionCashFlowOmitted(row)
                        && (!IsConciliacionCashFlowReportedToSiigo(row)
                        || row.ValidationStatus.Contains("Pendiente", StringComparison.OrdinalIgnoreCase)
                        || row.ValidationStatus.Contains("Revisar", StringComparison.OrdinalIgnoreCase))),
                    TotalEntries = RoundCurrency(group.Sum(static row => row.EntryValue)),
                    TotalExits = RoundCurrency(group.Sum(static row => row.ExitValue))
                };
            })
            .OrderBy(static row => row.BankLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsConciliacionCashFlowReportedToSiigo(ConciliacionCashFlowRowDto row)
    {
        return row.RegistrationStatus.Contains("Siigo OK", StringComparison.OrdinalIgnoreCase)
            || row.SiigoDocumentStatus.Contains("Siigo OK", StringComparison.OrdinalIgnoreCase)
            || row.SiigoPaymentStatus.Contains("Enviado", StringComparison.OrdinalIgnoreCase)
            || row.SiigoPaymentStatus.Contains("detectado", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.SiigoStatus, "EnviadoSiigo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.SiigoStatus, "Conciliado", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConciliacionNoIncludedCashFlow(ConciliacionCashFlowRowDto row) =>
        string.Equals(row.DetectedTypeKey, "no-incluida-conciliacion", StringComparison.OrdinalIgnoreCase);

    private static string BuildConciliacionCashFlowBankKey(ConciliacionCashFlowRowDto row) =>
        string.Join("|", new[]
        {
            FirstNonEmpty(row.SourceFlow, "Sin vertical"),
            FirstNonEmpty(row.BankAccountCode, row.BankAccountName, "Sin banco")
        });

    private static string BuildConciliacionCashFlowBankLabel(ConciliacionCashFlowRowDto row)
    {
        var bank = FirstNonEmpty(row.BankAccountName, row.BankAccountCode, row.SourceFlow, "Sin banco");
        var account = string.IsNullOrWhiteSpace(row.BankAccountCode)
            ? ""
            : $" ({row.BankAccountCode})";
        return $"{FirstNonEmpty(row.SourceFlow, "Sin vertical")} - {bank}{account}";
    }

    private static IReadOnlyList<ConciliacionAccountingVoucherGroupDto> BuildConciliacionAccountingVoucherGroups(
        IReadOnlyList<ConciliacionCashFlowRowDto> rows)
    {
        var voucherRows = rows
            .Where(static row => !IsConciliacionCashFlowOmitted(row))
            .Where(static row =>
                string.Equals(row.DetectedTypeKey, "comprobante-contable", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.DetectedTypeKey, "entrada-comprobante", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (voucherRows.Length == 0)
            return Array.Empty<ConciliacionAccountingVoucherGroupDto>();

        return voucherRows
            .GroupBy(static row => BuildConciliacionAccountingVoucherGroupKey(row), StringComparer.OrdinalIgnoreCase)
            .Select(static group => BuildConciliacionAccountingVoucherGroup(group.ToArray()))
            .OrderByDescending(static group => ResolveConciliacionAccountingVoucherSortDate(group))
            .ThenBy(static group => group.BankAccountName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static group => group.GroupLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildConciliacionAccountingVoucherGroupKey(ConciliacionCashFlowRowDto row)
    {
        var monthlyGroup = ResolveConciliacionMonthlyAccountingVoucherGroup(row);
        if (monthlyGroup is null)
            return $"fila|{FirstNonEmpty(row.RecordId, row.ExternalKey, Guid.NewGuid().ToString("N"))}";

        return string.Join("|", new[]
        {
            "mensual",
            monthlyGroup.Value.Kind,
            ResolveConciliacionAccountingVoucherPeriodKey(row),
            BuildConciliacionCashFlowBankKey(row)
        });
    }

    private static ConciliacionAccountingVoucherGroupDto BuildConciliacionAccountingVoucherGroup(
        IReadOnlyList<ConciliacionCashFlowRowDto> rows)
    {
        var orderedRows = rows
            .OrderBy(static row => row.MovementDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.SourceRowNumber)
            .ThenBy(static row => row.Description, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var first = orderedRows[0];
        var monthlyGroup = ResolveConciliacionMonthlyAccountingVoucherGroup(first);
        var isMonthlyCloseGroup = monthlyGroup is not null;
        var groupKind = monthlyGroup?.Kind ?? "comprobante-individual";
        var groupLabel = monthlyGroup?.Label ?? FirstNonEmpty(first.Description, first.Recipient, "Comprobante contable");
        var groupDetail = monthlyGroup?.Detail ?? "Movimiento individual de comprobante contable.";
        var dates = orderedRows
            .Select(static row => ParseConciliacionDateOnlyValue(row.MovementDateValue))
            .Where(static date => date.HasValue)
            .Select(static date => date!.Value)
            .ToArray();
        var closeDate = dates.Length > 0
            ? new DateOnly(dates.Max().Year, dates.Max().Month, DateTime.DaysInMonth(dates.Max().Year, dates.Max().Month))
            : (DateOnly?)null;
        var displayDate = isMonthlyCloseGroup && closeDate.HasValue
            ? $"Cierre {closeDate.Value:dd/MM/yyyy}"
            : first.MovementDateDisplay;
        var amount = RoundCurrency(orderedRows.Sum(static row => row.Amount));
        var accountLines = BuildConciliacionAccountingVoucherAccountLines(orderedRows);

        return new ConciliacionAccountingVoucherGroupDto
        {
            GroupKey = BuildConciliacionAccountingVoucherGroupKey(first),
            GroupKind = groupKind,
            GroupLabel = groupLabel,
            GroupDetail = groupDetail,
            MovementDateDisplay = displayDate,
            SourceFlow = first.SourceFlow,
            BankAccountCode = first.BankAccountCode,
            BankAccountName = first.BankAccountName,
            Direction = first.Direction,
            DirectionTone = first.DirectionTone,
            EntryValue = RoundCurrency(orderedRows.Sum(static row => row.EntryValue)),
            ExitValue = RoundCurrency(orderedRows.Sum(static row => row.ExitValue)),
            Amount = amount,
            IsMonthlyCloseGroup = isMonthlyCloseGroup,
            IsGrouped = orderedRows.Length > 1 || isMonthlyCloseGroup,
            RowCount = orderedRows.Length,
            HasMissingAccounts = accountLines.Any(static line => !line.HasAccount),
            RecordIds = orderedRows
                .Select(static row => row.RecordId)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            MovementExternalKeys = orderedRows
                .Select(static row => row.ExternalKey)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            AccountLines = accountLines,
            Rows = orderedRows
        };
    }

    private static IReadOnlyList<ConciliacionAccountingVoucherAccountLineDto> BuildConciliacionAccountingVoucherAccountLines(
        IReadOnlyList<ConciliacionCashFlowRowDto> rows)
    {
        return rows
            .GroupBy(static row =>
            {
                var concept = ResolveConciliacionAccountingVoucherConcept(row);
                return string.Join("|", concept.Key, row.AccountCode, row.AccountName);
            }, StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
            {
                var first = group.First();
                var concept = ResolveConciliacionAccountingVoucherConcept(first);
                return new ConciliacionAccountingVoucherAccountLineDto
                {
                    ConceptKey = concept.Key,
                    ConceptLabel = concept.Label,
                    AccountCode = first.AccountCode,
                    AccountName = first.AccountName,
                    Amount = RoundCurrency(group.Sum(static row => row.Amount)),
                    RowCount = group.Count(),
                    HasAccount = !string.IsNullOrWhiteSpace(first.AccountCode)
                };
            })
            .OrderBy(static line => line.ConceptLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static line => line.AccountCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DateOnly ResolveConciliacionAccountingVoucherSortDate(ConciliacionAccountingVoucherGroupDto group)
    {
        return group.Rows
            .Select(static row => ParseConciliacionDateOnlyValue(row.MovementDateValue))
            .Where(static date => date.HasValue)
            .Select(static date => date!.Value)
            .DefaultIfEmpty(DateOnly.MinValue)
            .Max();
    }

    private static string ResolveConciliacionAccountingVoucherPeriodKey(ConciliacionCashFlowRowDto row)
    {
        var date = ParseConciliacionDateOnlyValue(row.MovementDateValue);
        return date.HasValue
            ? date.Value.ToString("yyyyMM", CultureInfo.InvariantCulture)
            : "sin-fecha";
    }

    private static DateOnly? ParseConciliacionDateOnlyValue(string? value)
    {
        return DateOnly.TryParseExact(value ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static (string Kind, string Label, string Detail)? ResolveConciliacionMonthlyAccountingVoucherGroup(
        ConciliacionCashFlowRowDto row)
    {
        var concept = ResolveConciliacionAccountingVoucherConcept(row);
        if (string.Equals(row.Direction, "Entrada", StringComparison.OrdinalIgnoreCase)
            && string.Equals(concept.Key, "abono-intereses-ahorros", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "abono-intereses-ahorros-ingreso",
                "Abono intereses ahorros",
                "Acumulado mensual de intereses de ahorros con valor positivo.");
        }

        if (string.Equals(row.Direction, "Salida", StringComparison.OrdinalIgnoreCase)
            && IsConciliacionMonthlyBankExpenseConcept(concept.Key))
        {
            return (
                "gastos-bancarios-cierre",
                "Gastos bancarios de cierre",
                "Acumulado mensual de intereses negativos, comisiones, GMF, IVA y cuota de manejo.");
        }

        return null;
    }

    private static bool IsConciliacionMonthlyBankExpenseConcept(string conceptKey)
    {
        return string.Equals(conceptKey, "abono-intereses-ahorros", StringComparison.OrdinalIgnoreCase)
            || string.Equals(conceptKey, "comision-traslado-otros-bancos", StringComparison.OrdinalIgnoreCase)
            || string.Equals(conceptKey, "ajuste-intereses-ahorros", StringComparison.OrdinalIgnoreCase)
            || string.Equals(conceptKey, "impuesto-4x1000", StringComparison.OrdinalIgnoreCase)
            || string.Equals(conceptKey, "iva-comision-traslado-otros-bancos", StringComparison.OrdinalIgnoreCase)
            || string.Equals(conceptKey, "cuota-manejo", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Key, string Label) ResolveConciliacionAccountingVoucherConcept(ConciliacionCashFlowRowDto row)
    {
        var text = NormalizeConciliacionAccountingVoucherText(BuildConciliacionCashFlowSearchText(row));

        if (ContainsConciliacionAll(text, "IVA", "COMISION", "TRASLADO")
            && ContainsConciliacionAny(text, "OTROS BANCOS", "OTRO BANCO"))
            return ("iva-comision-traslado-otros-bancos", "IVA comision traslado otros bancos");

        if (ContainsConciliacionAll(text, "COMISION", "TRASLADO")
            && ContainsConciliacionAny(text, "OTROS BANCOS", "OTRO BANCO"))
            return ("comision-traslado-otros-bancos", "Comision traslado otros bancos");

        if (ContainsConciliacionAll(text, "AJUSTE", "INTERES")
            && ContainsConciliacionAny(text, "AHORRO", "AHORROS"))
            return ("ajuste-intereses-ahorros", "Ajuste intereses ahorros");

        if (ContainsConciliacionAny(text, "4X1000", "4 X 1000", "GMF", "GRAVAMEN"))
            return ("impuesto-4x1000", "Impuesto 4x1000");

        if (ContainsConciliacionAll(text, "CUOTA", "MANEJO"))
            return ("cuota-manejo", "Cuota manejo");

        if (ContainsConciliacionAll(text, "ABONO", "INTERES")
            && ContainsConciliacionAny(text, "AHORRO", "AHORROS"))
            return ("abono-intereses-ahorros", "Abono intereses ahorros");

        return ("comprobante-contable", FirstNonEmpty(row.Description, row.Recipient, "Comprobante contable"));
    }

    private static bool ContainsConciliacionAll(string text, params string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return tokens.All(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeConciliacionAccountingVoucherText(string? value)
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

    private static void ApplyConciliacionClientPaymentMatch(
        ConciliacionCashFlowRowDto row,
        ConciliacionClientPaymentRowDto match)
    {
        row.MatchRecordId = match.RecordId;
        row.MatchStatus = match.Status;
        row.ActionTargetKey = "entradas-fe";
        row.CanValidate = true;

        if (IsConciliacionCashFlowPostSendChange(row.DataverseStatus, row.SiigoStatus))
        {
            ApplyConciliacionCashFlowPostSendChange(row);
            return;
        }

        if (!string.IsNullOrWhiteSpace(match.InvoiceNumbers))
        {
            row.DetectedTypeKey = "entrada-fe";
            row.DetectedTypeLabel = "Factura cliente";
            row.DetectedTypeTone = "success";
        }

        row.ValidationStatus = match.Status switch
        {
            "Aprobado" or "ListoSiigo" or "EnviadoSiigo" or "Conciliado" => "Validada",
            "Sugerido" => "Pendiente validar",
            "Rechazado" => "Rechazada",
            _ => "Revisar"
        };
        row.ValidationTone = match.Status switch
        {
            "Aprobado" or "ListoSiigo" or "EnviadoSiigo" or "Conciliado" => "success",
            "Sugerido" => "info",
            "Rechazado" => "danger",
            _ => "warning"
        };
        row.InvoiceStatus = string.IsNullOrWhiteSpace(match.InvoiceNumbers)
            ? "Factura no encontrada"
            : "Factura Dataverse OK";
        row.InvoiceStatusTone = string.IsNullOrWhiteSpace(match.InvoiceNumbers) ? "danger" : "success";
        row.DataversePaymentStatus = match.RetentionsTotal > 0m
            ? $"Pago Dataverse OK con retenciones {match.RetentionsTotal:N0}"
            : "Pago Dataverse OK sin retenciones";
        row.DataversePaymentTone = "success";
        var sentToSiigo = string.Equals(match.Status, "EnviadoSiigo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(match.Status, "Conciliado", StringComparison.OrdinalIgnoreCase);
        var readyForSiigo = string.Equals(match.Status, "ListoSiigo", StringComparison.OrdinalIgnoreCase);
        row.SiigoPaymentStatus = sentToSiigo
            ? "Enviado Siigo"
            : readyForSiigo
                ? "Listo para envio Siigo"
                : "Pendiente envio Siigo";
        row.SiigoPaymentTone = sentToSiigo ? "success" : readyForSiigo ? "info" : "warning";
        row.RegistrationStatus = sentToSiigo
            ? "Dataverse OK / Siigo OK"
            : readyForSiigo
                ? "Dataverse OK / listo Siigo"
                : "Dataverse OK / Siigo pendiente";
        row.RegistrationTone = sentToSiigo ? "success" : readyForSiigo ? "info" : "warning";
    }

    private static void ApplyConciliacionCashFlowPostSendChange(ConciliacionCashFlowRowDto row)
    {
        row.ValidationStatus = "Cambio posterior";
        row.ValidationTone = "danger";
        row.RegistrationStatus = "Cambio en Excel despues de Siigo";
        row.RegistrationTone = "danger";
        row.InvoiceStatus = "Revisar cambio";
        row.InvoiceStatusTone = "danger";
        row.SiigoDocumentStatus = "Siigo ya tenia registro";
        row.SiigoDocumentTone = "warning";
        row.SiigoPaymentStatus = "Bloqueado por cambio";
        row.SiigoPaymentTone = "danger";
        row.InvoiceBalanceStatus = "Revisar manual";
        row.DataversePaymentStatus = "No sobreescrito";
        row.DataversePaymentTone = "warning";
    }

    private static bool IsConciliacionCashFlowPostSendChange(string status, string siigoStatus)
    {
        return string.Equals(status, "CambioPostEnvio", StringComparison.OrdinalIgnoreCase)
            || string.Equals(siigoStatus, "CambioPostEnvio", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Key, string Label, string Tone, string TargetKey) ResolveConciliacionCashFlowDetectedType(
        ConciliacionCashFlowRowDto row)
    {
        var manualCategory = ResolveConciliacionManualCashFlowCategory(row.ExcelMovementType);
        if (manualCategory is not null)
            return manualCategory.Value;

        if (IsConciliacionPocketTransfer(row))
            return ("no-incluida-conciliacion", "No incluida", "neutral", "huerfanos");

        if (string.Equals(row.SourceKind, "Traslado", StringComparison.OrdinalIgnoreCase))
            return ("traslado-interno", "Traslado interno entre cuentas", "info", "flujo-caja");

        if (IsConciliacionInternalBankTransfer(row))
            return ("traslado-interno", "Traslado interno entre cuentas", "info", "flujo-caja");

        var text = BuildConciliacionCashFlowSearchText(row);
        if (string.Equals(row.Direction, "Entrada", StringComparison.OrdinalIgnoreCase))
        {
            if (ConciliacionInvoiceTokenRegex.IsMatch(text))
                return ("entrada-fe", "Factura cliente", "success", "entradas-fe");

            if (ContainsConciliacionAny(text, "ABONO INTERES", "APERTURA INVERSION", "INTERES", "RENDIMIENTO", "CANCELACION INVERSION", "CANCELACION INVERCION"))
                return ("entrada-comprobante", "Comprobante contable", "info", "comprobantes");

            return ("entrada-comprobante", "Comprobante contable", "info", "comprobantes");
        }

        if (string.Equals(row.Direction, "Salida", StringComparison.OrdinalIgnoreCase))
        {
            if (ContainsConciliacionAny(text, "CUENTA DE COBRO", "CUENTAS DE COBRO", "DOCUMENTO SOPORTE", "DOC SOPORTE", "DS "))
                return ("cuenta-cobro", "Documento soporte", "info", "cuentas-cobro");

            if (ContainsConciliacionAny(text, "FACTURA ELECTRONICA", "FACTURA ELECTR", "FACTURA", "FEV", "FVE", "FE "))
                return ("salida-fe", "Factura proveedor", "success", "salidas-fe");

            if (ContainsConciliacionAny(
                text,
                "MI PLANILLA",
                "MIPLANILLA",
                "PLANILLA",
                "ETB",
                "ENEL",
                "CANCELACION INVERSION",
                "CANCELACION INVERCION",
                "GRAVAMEN",
                "GMF",
                "4X1000",
                "4 X 1000",
                "COMISION",
                "GASTO BANCARIO",
                "INTERES",
                "DIAN",
                "IMPUESTO"))
            {
                return ("comprobante-contable", "Comprobante contable", "info", "comprobantes");
            }

            return ("comprobante-contable", "Comprobante contable", "info", "comprobantes");
        }

        return ("comprobante-contable", "Comprobante contable sin clasificar", "info", "comprobantes");
    }

    private static (string Key, string Label, string Tone, string TargetKey)? ResolveConciliacionManualCashFlowCategory(string? value)
    {
        var key = (value ?? "").Trim().ToLowerInvariant();
        return key switch
        {
            "entrada-fe" => ("entrada-fe", "Factura cliente", "success", "entradas-fe"),
            "entrada-comprobante" => ("entrada-comprobante", "Comprobante contable", "info", "comprobantes"),
            "traslado-interno" => ("traslado-interno", "Traslado interno entre cuentas", "info", "flujo-caja"),
            "no-incluida-conciliacion" => ("no-incluida-conciliacion", "No incluida", "neutral", "huerfanos"),
            "salida-fe" => ("salida-fe", "Factura proveedor", "success", "salidas-fe"),
            "cuenta-cobro" => ("cuenta-cobro", "Documento soporte", "info", "cuentas-cobro"),
            "comprobante-contable" => ("comprobante-contable", "Comprobante contable", "info", "comprobantes"),
            _ => null
        };
    }

    private static string ResolveDefaultInvoiceStatus(string detectedTypeKey)
    {
        return detectedTypeKey switch
        {
            "salida-fe" => "Pendiente cruce Dataverse",
            "cuenta-cobro" => "Se creara desde flujo",
            "comprobante-contable" => "No requiere factura",
            "entrada-comprobante" => "No requiere factura",
            "entrada-fe" => "Pendiente cruce factura",
            "no-incluida-conciliacion" => "No aplica",
            _ => "Pendiente clasificar"
        };
    }

    private static bool IsConciliacionPocketTransfer(ConciliacionCashFlowRowDto? row)
    {
        if (row is null)
            return false;

        return ContainsConciliacionAny(BuildConciliacionCashFlowSearchText(row), "BOLSILLO");
    }

    private static bool IsConciliacionInternalBankTransfer(ConciliacionCashFlowRowDto row)
    {
        var text = BuildConciliacionCashFlowSearchText(row);
        if (!ContainsConciliacionAny(text, "TRANSFERENCIA DE FONDOS", "TRASLADO"))
            return false;

        var sourceBank = ResolveConciliacionCashFlowBankAccount(row.SourceFlow);
        return (string.Equals(sourceBank.Code, "11100504", StringComparison.OrdinalIgnoreCase)
                && text.Contains("7316", StringComparison.OrdinalIgnoreCase))
            || (string.Equals(sourceBank.Code, "11100505", StringComparison.OrdinalIgnoreCase)
                && text.Contains("8100", StringComparison.OrdinalIgnoreCase));
    }

    private static (string Code, string Name) ResolveConciliacionCashFlowBankAccount(string? flow)
    {
        var normalized = NormalizeConciliacionAccountingVoucherText(flow ?? "");
        if (normalized.Contains("COPIERS", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("7316", StringComparison.OrdinalIgnoreCase))
        {
            return ("11100505", "Bancolombia Copiers 7316");
        }

        if (normalized.Contains("CLOUD", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("8100", StringComparison.OrdinalIgnoreCase))
        {
            return ("11100504", "Bancolombia Cloud 8100");
        }

        return ("", FirstNonEmpty(flow, "Banco no identificado"));
    }

    private static string ResolveConciliacionTransferCounterpartFlow(
        string sourceFlow,
        string transferFrom,
        string transferTo,
        decimal entry,
        decimal exit)
    {
        if (entry > 0m)
            return FirstNonEmpty(transferFrom, transferTo);

        if (exit > 0m)
            return FirstNonEmpty(transferTo, transferFrom);

        if (string.Equals(sourceFlow, transferFrom, StringComparison.OrdinalIgnoreCase))
            return transferTo;

        if (string.Equals(sourceFlow, transferTo, StringComparison.OrdinalIgnoreCase))
            return transferFrom;

        return FirstNonEmpty(transferTo, transferFrom);
    }

    private static bool IsConciliacionSiigoRegistered(string siigoDocumentId, string siigoStatus)
    {
        if (!string.IsNullOrWhiteSpace(siigoDocumentId))
            return true;

        var status = (siigoStatus ?? "").Trim();
        if (status.Equals("si", StringComparison.OrdinalIgnoreCase)
            || status.Equals("sí", StringComparison.OrdinalIgnoreCase)
            || status.Equals("ok", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalized = status.ToUpperInvariant();
        return normalized.Contains("REGISTR", StringComparison.Ordinal)
            || normalized.Contains("SUBID", StringComparison.Ordinal)
            || normalized.Contains("ENVIAD", StringComparison.Ordinal)
            || normalized.Contains("CREAD", StringComparison.Ordinal)
            || normalized.Contains("CONCILIAD", StringComparison.Ordinal);
    }

    private static string BuildConciliacionCashFlowSearchText(ConciliacionCashFlowRowDto row) =>
        string.Join(" ", new[]
        {
            row.Description,
            row.Recipient,
            row.DestinationBank,
            row.DocumentType,
            row.Observations,
            row.ExcelMovementType,
            row.BankAccountName,
            row.AccountName,
            row.SourceFlow
        }).ToUpperInvariant();

    private static bool ContainsConciliacionAny(string text, params string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static ConciliacionClientPaymentSummaryDto BuildConciliacionClientPaymentSummary(
        IReadOnlyList<ConciliacionClientPaymentRowDto> rows)
    {
        var pendingRows = rows.Where(static row => IsConciliacionPendingReviewStatus(row.Status)).ToArray();
        var suggestedRows = rows.Where(static row => string.Equals(row.Status, "Sugerido", StringComparison.OrdinalIgnoreCase)).ToArray();
        var readyRows = rows.Where(static row => string.Equals(row.Status, "ListoSiigo", StringComparison.OrdinalIgnoreCase)).ToArray();
        var lastRun = rows
            .Select(static row => ParseConciliacionDateTimeOffset(row.ModifiedOnDisplay))
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .DefaultIfEmpty()
            .Max();

        return new ConciliacionClientPaymentSummaryDto
        {
            TotalRows = rows.Count,
            Suggested = suggestedRows.Length,
            Approved = rows.Count(static row => string.Equals(row.Status, "Aprobado", StringComparison.OrdinalIgnoreCase)),
            ReadyForSiigo = readyRows.Length,
            PreflightOk = rows.Count(static row => string.Equals(row.PreflightStatus, "ListoSiigo", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.PreflightStatus, "ValidadoPendienteAprobacion", StringComparison.OrdinalIgnoreCase)),
            PreflightBlocked = rows.Count(static row => string.Equals(row.PreflightStatus, "BloqueadoSiigo", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.Status, "BloqueadoSiigo", StringComparison.OrdinalIgnoreCase)),
            Rejected = rows.Count(static row => string.Equals(row.Status, "Rechazado", StringComparison.OrdinalIgnoreCase)),
            PendingReview = pendingRows.Length,
            DifferenceOutOfTolerance = rows.Count(static row => string.Equals(row.Status, "DiferenciaFueraRango", StringComparison.OrdinalIgnoreCase)),
            NoInvoiceToken = rows.Count(static row => string.Equals(row.Status, "SinFacturaDescripcion", StringComparison.OrdinalIgnoreCase)),
            NoInvoiceMatch = rows.Count(static row => string.Equals(row.Status, "FacturaNoEncontrada", StringComparison.OrdinalIgnoreCase)),
            AmbiguousInvoice = rows.Count(static row => string.Equals(row.Status, "FacturaAmbigua", StringComparison.OrdinalIgnoreCase)),
            TotalEntries = RoundCurrency(rows.Sum(static row => row.EntryValue)),
            SuggestedEntries = RoundCurrency(suggestedRows.Sum(static row => row.EntryValue)),
            ReadyForSiigoEntries = RoundCurrency(readyRows.Sum(static row => row.EntryValue)),
            PendingReviewEntries = RoundCurrency(pendingRows.Sum(static row => row.EntryValue)),
            LastRunLabel = FormatConciliacionDateTimeDisplay(lastRun),
            Rows = rows
        };
    }

    private static IReadOnlyList<ConciliacionPhaseDto> BuildConciliacionPhases(
        ConciliacionCashFlowSummaryDto cashFlow,
        ConciliacionClientPaymentSummaryDto clientPayments,
        ConciliacionDianSupplierInvoiceSummaryDto dianSupplierInvoices,
        ConciliacionCuentaCobroSummaryDto cuentasCobro)
    {
        return new[]
        {
            BuildStaticConciliacionPhase(
                "flujo-caja",
                "Flujo de caja por banco",
                cashFlow.TotalRows > 0 ? "Activo" : "Sin datos",
                cashFlow.TotalRows > 0 ? "success" : "neutral",
                "Diario y cierre mensual",
                cashFlow.LastRunLabel,
                "Validar cada fila antes de enviarla a Siigo y cruzar el extracto bancario al cierre.",
                new[]
                {
                    Step("Filas importadas", "Listo", "success", $"{cashFlow.TotalRows:N0} filas del periodo."),
                    Step("Tipo detectado", "Parcial", "info", "Clasificacion inicial por entrada/salida y texto."),
                    Step("Validacion", cashFlow.PendingValidationRows > 0 ? "Pendiente" : "Lista", cashFlow.PendingValidationRows > 0 ? "warning" : "success", $"{cashFlow.PendingValidationRows:N0} filas por validar."),
                    Step("Extracto mensual", "Falta", "warning", "Cruce banco vs flujo y tabla de cierre.")
                },
                $"Entradas {cashFlow.TotalEntries:N0}; salidas {cashFlow.TotalExits:N0}; traslados {cashFlow.TotalTransfers:N0}.",
                new[]
                {
                    "Importacion de flujo de caja Cloud/Copiers a Dataverse.",
                    "Traslados internos visibles como comprobantes contables y omision de traslados de bolsillos.",
                    "Columna visual de tipo de comprobante detectado."
                },
                new[]
                {
                    "Persistir la categoria reasignada desde el popup.",
                    "Cruzar mensualmente contra extractos bancarios y saldos finales por banco.",
                    "Bloquear envio a Siigo hasta que la fila este validada y completa."
                }),
            BuildStaticConciliacionPhase(
                "registro-dian",
                "Registro DIAN / documentos proveedor",
                dianSupplierInvoices.TotalRows > 0 ? "Detectado" : "Sin filas",
                dianSupplierInvoices.TotalRows > 0 ? "info" : "neutral",
                "Semanal",
                dianSupplierInvoices.LastRunLabel,
                "Importar facturas electronicas y documentos soporte recibidos, validar proveedor, clasificacion y documento Siigo antes del cruce con salidas.",
                new[]
                {
                    Step("Documentos recibidos", "Importados", dianSupplierInvoices.TotalRows > 0 ? "success" : "neutral", $"{dianSupplierInvoices.TotalRows:N0} facturas/documentos soporte del periodo."),
                    Step("Proveedor Siigo", dianSupplierInvoices.ProviderPending > 0 ? "Pendiente" : "OK", dianSupplierInvoices.ProviderPending > 0 ? "warning" : "success", $"{dianSupplierInvoices.ProviderPending:N0} con datos de proveedor incompletos."),
                    Step("Clasificacion", dianSupplierInvoices.ClassificationPending > 0 ? "Pendiente" : "OK", dianSupplierInvoices.ClassificationPending > 0 ? "warning" : "success", $"{dianSupplierInvoices.ClassificationPending:N0} por categorizar o asignar cuenta."),
                    Step("Documento Siigo", dianSupplierInvoices.SentToSiigo > 0 ? "Parcial" : "Falta", dianSupplierInvoices.SentToSiigo > 0 ? "info" : "warning", $"{dianSupplierInvoices.ReadyForPurchase:N0} listos para crear FC/DS.")
                },
                $"Total documentos proveedor {dianSupplierInvoices.TotalValue:N0}. Listos Siigo {dianSupplierInvoices.ReadyForPurchase:N0}; enviados {dianSupplierInvoices.SentToSiigo:N0}.",
                new[]
                {
                    "Lectura de facturas y documentos soporte desde gastos de la empresa en Dataverse.",
                    "Separacion por proveedor, clasificacion, prevalidacion y documento Siigo."
                },
                new[]
                {
                    "Crear importador DIAN desde SharePoint con CUFE como clave externa.",
                    "Guardar proveedor Siigo seleccionado/creado en Dataverse.",
                    "Crear FC para factura electronica o DS para documento soporte y guardar el id Siigo."
                }),
            BuildStaticConciliacionPhase(
                "salidas-fe",
                "Registro de salidas FC",
                cashFlow.OutgoingInvoiceRows > 0 ? "Detectado" : "Sin filas",
                cashFlow.OutgoingInvoiceRows > 0 ? "info" : "neutral",
                "Por periodo",
                cashFlow.LastRunLabel,
                "Cruzar pagos de banco contra documentos proveedor ya importados desde DIAN y creados en Siigo.",
                new[]
                {
                    Step("Filas candidatas", "Detectadas", "info", $"{cashFlow.OutgoingInvoiceRows:N0} salidas FE."),
                    Step("Documento Dataverse", dianSupplierInvoices.TotalRows > 0 ? "Disponible" : "Falta", dianSupplierInvoices.TotalRows > 0 ? "info" : "warning", $"{dianSupplierInvoices.TotalRows:N0} documentos proveedor en DIAN/Dataverse."),
                    Step("Documento Siigo", dianSupplierInvoices.SentToSiigo > 0 ? "Parcial" : "Falta", dianSupplierInvoices.SentToSiigo > 0 ? "info" : "warning", $"{dianSupplierInvoices.SentToSiigo:N0} documentos proveedor con id Siigo."),
                    Step("Pago Siigo", "Falta", "warning", "Registro de pago pendiente.")
                },
                "",
                new[]
                {
                    "Filtro lateral y tabla de salidas con factura electronica.",
                    "Estado visual para documento Dataverse, documento Siigo, pago Siigo y saldo."
                },
                new[]
                {
                    "Conectar cruce real contra el nuevo Registro DIAN.",
                    "Consultar saldo de compra y pago en Siigo.",
                    "Crear prevalidacion de egreso antes del envio a Siigo."
                }),
            BuildStaticConciliacionPhase(
                "entradas-fe",
                "Registro de entradas FV",
                clientPayments.PendingReview > 0 ? "Con pendientes" : clientPayments.Suggested > 0 ? "Listo para aprobar" : "Sin pendientes",
                clientPayments.PendingReview > 0 ? "warning" : clientPayments.Suggested > 0 ? "info" : "success",
                "Diario",
                clientPayments.LastRunLabel,
                "Validar pagos de clientes, retenciones y borrador contable antes del envio a Siigo.",
                new[]
                {
                    Step("Entradas", "Importadas", "success", $"{clientPayments.TotalRows:N0} cruces."),
                    Step("Factura Dataverse", "Parcial", "info", $"{clientPayments.Suggested:N0} sugeridos."),
                    Step("Pago Dataverse", "Activo", "success", "Cruce guarda retenciones calculadas."),
                    Step("Subida Siigo", "Falta", "warning", $"{clientPayments.ReadyForSiigo:N0} listos para envio futuro.")
                },
                $"Valor revisado {clientPayments.TotalEntries:N0}; sugerido {clientPayments.SuggestedEntries:N0}; listo Siigo {clientPayments.ReadyForSiigoEntries:N0}.",
                new[]
                {
                    "Cruce de entradas contra facturacion Dataverse.",
                    "Aprobacion, revision, rechazo y prevalidacion pre-Siigo.",
                    "Borrador contable con retenciones y balance debito/credito."
                },
                new[]
                {
                    "Envio real a Siigo de los registros `ListoSiigo`.",
                    "Confirmar marca de pago registrado en Dataverse cuando el comprobante quede creado.",
                    "Reflejar cambios posteriores de Siigo hacia Dataverse."
                }),
            BuildStaticConciliacionPhase(
                "cuentas-cobro",
                "Registro de cuentas de cobro",
                cuentasCobro.WithErrors > 0 ? "Con errores" : cuentasCobro.ReadyForSiigo > 0 ? "Listo Siigo" : cashFlow.CollectionAccountRows > 0 ? "Detectado" : "Sin filas",
                cuentasCobro.WithErrors > 0 ? "danger" : cuentasCobro.ReadyForSiigo > 0 ? "info" : cashFlow.CollectionAccountRows > 0 ? "info" : "neutral",
                "Por actualizacion de flujo",
                string.IsNullOrWhiteSpace(cuentasCobro.LastRunLabel) ? cashFlow.LastRunLabel : cuentasCobro.LastRunLabel,
                "Cruzar salida bancaria contra cuenta de cobro de la app, validar retenciones/cuenta contable y crear documento soporte en Siigo.",
                new[]
                {
                    Step("Filas candidatas", "Detectadas", "info", $"{cuentasCobro.DetectedCashFlowRows:N0} salidas del flujo."),
                    Step("Cruce app", cuentasCobro.MatchedRows > 0 ? "Activo" : "Pendiente", cuentasCobro.MatchedRows > 0 ? "info" : "warning", $"{cuentasCobro.MatchedRows:N0} cruzadas contra cuenta de cobro."),
                    Step("Retenciones", cuentasCobro.PendingRows > 0 ? "Revisar" : "OK", cuentasCobro.PendingRows > 0 ? "warning" : "success", $"ReteFuente total {cuentasCobro.TotalReteFuenteValue:N0}."),
                    Step("Documento soporte", cuentasCobro.SentToSiigo > 0 ? "Parcial" : "Pendiente", cuentasCobro.SentToSiigo > 0 ? "success" : "warning", $"{cuentasCobro.ReadyForSiigo:N0} listos; {cuentasCobro.SentToSiigo:N0} enviados.")
                },
                $"Valor pago {cuentasCobro.TotalPaidValue:N0}; valor bruto {cuentasCobro.TotalGrossValue:N0}; retefuente {cuentasCobro.TotalReteFuenteValue:N0}.",
                new[]
                {
                    "Filtro y deteccion inicial desde flujo de caja.",
                    "Modulo de cuentas de cobro ya permite capturar valor total, pago y retefuente.",
                    "Cruce por valor, NIT/nombre y fecha contra la salida bancaria."
                },
                new[]
                {
                    "Completar cuenta contable antes del envio.",
                    "Crear proveedores faltantes en Siigo cuando aplique.",
                    "Marcar documento soporte Siigo en cuenta de cobro y flujo de caja."
                }),
            BuildStaticConciliacionPhase(
                "comprobantes",
                "Registro de comprobantes contables",
                cashFlow.AccountingVoucherRows > 0 ? "Detectado" : "Sin filas",
                cashFlow.AccountingVoucherRows > 0 ? "info" : "neutral",
                "Diario",
                cashFlow.LastRunLabel,
                "Validar comprobantes sin factura/documento soporte, incluidos traslados internos entre bancos, y preparar asiento completo.",
                new[]
                {
                    Step("Filas candidatas", "Detectadas", "info", $"{cashFlow.AccountingVoucherRows:N0} comprobantes."),
                    Step("Cuenta contable", "Editable", "info", "Se selecciona desde el catalogo Siigo antes de enviar."),
                    Step("Siigo", "Listo", "success", "Crea comprobante de ingreso o egreso segun entrada/salida.")
                },
                "",
                new[]
                {
                    "Deteccion de traslados internos, MI PLANILLA, ENEL, ETB, intereses, inversiones, gravamen y gastos bancarios.",
                    "Catalogo de cuentas Siigo disponible para seleccionar cuenta por fila.",
                    "Envio masivo secuencial a Siigo con log de enviados y errores."
                },
                new[]
                {
                    "Consolidar gravamen mensual por banco en un solo comprobante.",
                    "Partir MI PLANILLA por salud, pension, ARL y caja con cuentas contables separadas.",
                    "Crear plantillas multi-linea para casos que no sean un debito/credito simple."
                }),
            BuildStaticConciliacionPhase(
                "huerfanos",
                "No incluidas conciliacion",
                cashFlow.OrphanRows > 0 ? "Detectadas" : "Sin filas",
                cashFlow.OrphanRows > 0 ? "neutral" : "success",
                "Continuo",
                cashFlow.LastRunLabel,
                "Revisar movimientos que la autoclasificacion dejo por fuera y reasignarlos si realmente deben ir a Siigo o Dataverse.",
                new[]
                {
                    Step("No incluidas", cashFlow.OrphanRows > 0 ? "Detectado" : "Sin filas", cashFlow.OrphanRows > 0 ? "neutral" : "success", $"{cashFlow.OrphanRows:N0} registros."),
                    Step("Reasignacion", "Editable", "info", "Puede enviarse a factura electronica, cuenta de cobro o comprobante contable.")
                },
                "",
                new[]
                {
                    "Traslados de bolsillos y exclusiones visibles para auditoria.",
                    "Categoria corregible desde el popup de reasignacion."
                },
                new[]
                {
                    "Agregar reglas nuevas cuando una autoclasificacion falle de forma repetida."
                })
        };
    }

    private static ConciliacionPhaseDto BuildStaticConciliacionPhase(
        string key,
        string label,
        string status,
        string tone,
        string cadence,
        string lastRun,
        string nextStep,
        IReadOnlyList<ConciliacionFlowStepDto> steps,
        string runSummary = "",
        IReadOnlyList<string>? readyItems = null,
        IReadOnlyList<string>? missingItems = null) =>
        new()
        {
            Key = key,
            Label = label,
            StatusLabel = status,
            StatusTone = tone,
            CadenceLabel = cadence,
            LastRunLabel = string.IsNullOrWhiteSpace(lastRun) ? "Sin log" : lastRun,
            RunSummary = string.IsNullOrWhiteSpace(runSummary) ? "Resumen pendiente de conectar a logs historicos." : runSummary,
            NextStep = nextStep,
            ReadyItems = readyItems ?? Array.Empty<string>(),
            MissingItems = missingItems ?? Array.Empty<string>(),
            Steps = steps
        };

    private static ConciliacionFlowStepDto Step(string label, string status, string tone, string summary) =>
        new()
        {
            Label = label,
            StatusLabel = status,
            StatusTone = tone,
            Summary = summary
        };

    private static ConciliacionClientPaymentRowDto? ParseConciliacionClientPaymentRow(
        JsonElement item,
        RhEntityMetadata metadata)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, metadata.PrimaryIdField),
            ReadString(item, ClientPaymentMatchIdField)).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var status = FirstNonEmpty(ReadString(item, ClientPaymentMatchStatusField), "Sin estado");
        var movementDate = ReadDateOnly(item, ClientPaymentMatchMovementDateField);
        var modifiedOn = ParseConciliacionDateTimeOffset(ReadString(item, ConciliacionModifiedOnField));
        var preflightStatus = ReadString(item, ClientPaymentMatchPreflightStatusField).Trim();
        var preflightValidatedOn = ParseConciliacionDateTimeOffset(ReadString(item, ClientPaymentMatchPreflightValidatedOnField));
        var movementExternalKey = ReadString(item, ClientPaymentMatchMovementExternalKeyField).Trim();

        return new ConciliacionClientPaymentRowDto
        {
            RecordId = recordId,
            Status = status,
            StatusLabel = ResolveConciliacionStatusLabel(status),
            StatusTone = ResolveConciliacionStatusTone(status),
            Confidence = ReadInt(item, ClientPaymentMatchConfidenceField),
            Reason = ReadString(item, ClientPaymentMatchReasonField).Trim(),
            MovementId = ReadString(item, ClientPaymentMatchMovementIdField).Trim(),
            MovementExternalKey = movementExternalKey,
            SourceRowNumber = ParseConciliacionCashFlowSourceRowNumber(movementExternalKey),
            MovementDateValue = movementDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            MovementDateDisplay = movementDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
            SourceFlow = ReadString(item, ClientPaymentMatchSourceFlowField).Trim(),
            BankAccountCode = ReadString(item, ClientPaymentMatchBankCodeField).Trim(),
            BankAccountName = ReadString(item, ClientPaymentMatchBankNameField).Trim(),
            Description = ReadString(item, ClientPaymentMatchDescriptionField).Trim(),
            InvoiceRecordIds = ReadString(item, ClientPaymentMatchInvoiceIdsField).Trim(),
            InvoiceNumbers = ReadString(item, ClientPaymentMatchInvoiceNumbersField).Trim(),
            ClientNames = ReadString(item, ClientPaymentMatchClientField).Trim(),
            EntryValue = RoundCurrency(ReadDecimal(item, ClientPaymentMatchEntryField) ?? 0m),
            InvoiceTotal = RoundCurrency(ReadDecimal(item, ClientPaymentMatchInvoiceTotalField) ?? 0m),
            PaymentValue = RoundCurrency(ReadDecimal(item, ClientPaymentMatchPaymentValueField) ?? 0m),
            ReteFuenteValue = RoundCurrency(ReadDecimal(item, ClientPaymentMatchReteFteField) ?? 0m),
            ReteIcaValue = RoundCurrency(ReadDecimal(item, ClientPaymentMatchReteIcaField) ?? 0m),
            RteIvaValue = RoundCurrency(ReadDecimal(item, ClientPaymentMatchRteIvaField) ?? 0m),
            RetentionsTotal = RoundCurrency((ReadDecimal(item, ClientPaymentMatchReteFteField) ?? 0m)
                + (ReadDecimal(item, ClientPaymentMatchReteIcaField) ?? 0m)
                + (ReadDecimal(item, ClientPaymentMatchRteIvaField) ?? 0m)),
            DifferenceValue = RoundCurrency(ReadDecimal(item, ClientPaymentMatchDifferenceField) ?? 0m),
            DraftJson = ReadString(item, ClientPaymentMatchDraftJsonField).Trim(),
            PreflightStatus = preflightStatus,
            PreflightStatusLabel = ResolveConciliacionPreflightStatusLabel(preflightStatus),
            PreflightStatusTone = ResolveConciliacionPreflightStatusTone(preflightStatus),
            PreflightMessage = ReadString(item, ClientPaymentMatchPreflightMessageField).Trim(),
            PreflightDebitTotal = RoundCurrency(ReadDecimal(item, ClientPaymentMatchPreflightDebitField) ?? 0m),
            PreflightCreditTotal = RoundCurrency(ReadDecimal(item, ClientPaymentMatchPreflightCreditField) ?? 0m),
            PreflightValidatedOnDisplay = FormatConciliacionDateTimeDisplay(preflightValidatedOn),
            ModifiedOnDisplay = modifiedOn?.ToString("O", CultureInfo.InvariantCulture) ?? ""
        };
    }

    private static HashSet<string> BuildConciliacionClientPaymentAttributeSet(
        RhEntityMetadata metadata,
        ISet<string> attributes)
    {
        var values = attributes.Count > 0
            ? new HashSet<string>(attributes, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            ClientPaymentMatchIdField,
            ClientPaymentMatchStatusField,
            ClientPaymentMatchConfidenceField,
            ClientPaymentMatchReasonField,
            ClientPaymentMatchMovementIdField,
            ClientPaymentMatchMovementExternalKeyField,
            ClientPaymentMatchMovementDateField,
            ClientPaymentMatchSourceFlowField,
            ClientPaymentMatchBankCodeField,
            ClientPaymentMatchBankNameField,
            ClientPaymentMatchDescriptionField,
            ClientPaymentMatchEntryField,
            ClientPaymentMatchInvoiceIdsField,
            ClientPaymentMatchInvoiceNumbersField,
            ClientPaymentMatchClientField,
            ClientPaymentMatchInvoiceTotalField,
            ClientPaymentMatchPaymentValueField,
            ClientPaymentMatchReteFteField,
            ClientPaymentMatchReteIcaField,
            ClientPaymentMatchRteIvaField,
            ClientPaymentMatchDifferenceField,
            ClientPaymentMatchDraftJsonField,
            ClientPaymentMatchPreflightStatusField,
            ClientPaymentMatchPreflightMessageField,
            ClientPaymentMatchPreflightValidatedOnField,
            ClientPaymentMatchPreflightDebitField,
            ClientPaymentMatchPreflightCreditField,
            ConciliacionCreatedOnField,
            ConciliacionModifiedOnField
        })
        {
            if (!string.IsNullOrWhiteSpace(field))
                values.Add(field);
        }

        return values;
    }

    private static string BuildConciliacionClientPaymentSelect(
        RhEntityMetadata metadata,
        ISet<string> attributes)
    {
        return string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            ClientPaymentMatchIdField,
            ClientPaymentMatchStatusField,
            ClientPaymentMatchConfidenceField,
            ClientPaymentMatchReasonField,
            ClientPaymentMatchMovementIdField,
            ClientPaymentMatchMovementExternalKeyField,
            ClientPaymentMatchMovementDateField,
            ClientPaymentMatchSourceFlowField,
            ClientPaymentMatchBankCodeField,
            ClientPaymentMatchBankNameField,
            ClientPaymentMatchDescriptionField,
            ClientPaymentMatchEntryField,
            ClientPaymentMatchInvoiceIdsField,
            ClientPaymentMatchInvoiceNumbersField,
            ClientPaymentMatchClientField,
            ClientPaymentMatchInvoiceTotalField,
            ClientPaymentMatchPaymentValueField,
            ClientPaymentMatchReteFteField,
            ClientPaymentMatchReteIcaField,
            ClientPaymentMatchRteIvaField,
            ClientPaymentMatchDifferenceField,
            ClientPaymentMatchDraftJsonField,
            ClientPaymentMatchPreflightStatusField,
            ClientPaymentMatchPreflightMessageField,
            ClientPaymentMatchPreflightValidatedOnField,
            ClientPaymentMatchPreflightDebitField,
            ClientPaymentMatchPreflightCreditField,
            ConciliacionCreatedOnField,
            ConciliacionModifiedOnField
        }
        .Where(field => !string.IsNullOrWhiteSpace(field) && attributes.Contains(field))
        .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyDictionary<string, ConciliacionAccountCatalogItem>> GetConciliacionAccountCatalogAsync(
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            AccountCatalogLogicalName,
            AccountCatalogSetName,
            AccountCatalogIdField,
            AccountCatalogPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildAccountCatalogAttributeSet(metadata, attributes);
        var rows = await GetAccountCatalogRowsAsync(metadata, attributes, ct);

        var accounts = rows
            .Where(static row => !string.IsNullOrWhiteSpace(row.Code))
            .GroupBy(static row => row.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group =>
                {
                    var row = group.First();
                    return new ConciliacionAccountCatalogItem(row.Code.Trim(), ResolveAccountCatalogName(row.Code, row.Name), row.Active);
                },
                StringComparer.OrdinalIgnoreCase);
        AddConciliacionRequiredExpenseAccounts(accounts);
        return accounts;
    }

    private static void AddConciliacionRequiredExpenseAccounts(
        IDictionary<string, ConciliacionAccountCatalogItem> accounts)
    {
        foreach (var account in RequiredConciliacionExpenseAccounts)
        {
            if (!accounts.TryGetValue(account.Code, out var current) || !current.Active || string.IsNullOrWhiteSpace(current.Name))
                accounts[account.Code] = account;
        }
    }

    private static readonly IReadOnlyList<ConciliacionAccountCatalogItem> RequiredConciliacionExpenseAccounts = new[]
    {
        new ConciliacionAccountCatalogItem("511030", "Asesoria contable", true),
        new ConciliacionAccountCatalogItem("511036", "Asesoria comercial", true)
    };

    private static ConciliacionPreflightValidation ValidateConciliacionClientPaymentDraft(
        ConciliacionClientPaymentRowDto row,
        IReadOnlyDictionary<string, ConciliacionAccountCatalogItem> accountCatalog)
    {
        var issues = new List<string>();
        var debitTotal = 0m;
        var creditTotal = 0m;

        if (!IsConciliacionSiigoCandidateStatus(row.Status))
            issues.Add("El estado actual debe resolverse antes de preparar envio a Siigo.");
        if (row.EntryValue <= 0m)
            issues.Add("El movimiento no tiene valor de entrada.");
        if (row.InvoiceTotal <= 0m)
            issues.Add("No hay total de factura asociado.");
        if (string.IsNullOrWhiteSpace(row.InvoiceNumbers))
            issues.Add("No hay numero de factura asociado.");
        if (string.IsNullOrWhiteSpace(row.ClientNames))
            issues.Add("No hay cliente asociado.");
        if (string.IsNullOrWhiteSpace(row.BankAccountCode))
            issues.Add("No hay cuenta bancaria contable.");
        if (Math.Abs(row.DifferenceValue) > RegistroPagosClientesBalancedTolerance)
            issues.Add($"La diferencia supera la tolerancia de {RegistroPagosClientesBalancedTolerance:N0}.");

        if (string.IsNullOrWhiteSpace(row.DraftJson))
        {
            issues.Add("No existe JSON de borrador Siigo.");
            return new ConciliacionPreflightValidation(RoundCurrency(debitTotal), RoundCurrency(creditTotal), issues);
        }

        try
        {
            using var doc = JsonDocument.Parse(row.DraftJson);
            var root = doc.RootElement;
            var type = ReadString(root, "type");
            if (!string.Equals(type, "ComprobanteIngresoSiigoBorrador", StringComparison.OrdinalIgnoreCase))
                issues.Add("El borrador no corresponde al tipo ComprobanteIngresoSiigoBorrador.");

            if (!root.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array || lines.GetArrayLength() == 0)
            {
                issues.Add("El borrador no tiene lineas contables.");
                return new ConciliacionPreflightValidation(RoundCurrency(debitTotal), RoundCurrency(creditTotal), issues);
            }

            var lineNumber = 0;
            foreach (var line in lines.EnumerateArray())
            {
                lineNumber++;
                var accountCode = ReadString(line, "accountCode").Trim();
                var accountName = ReadString(line, "accountName").Trim();
                var debit = RoundCurrency(ReadDecimal(line, "debit") ?? 0m);
                var credit = RoundCurrency(ReadDecimal(line, "credit") ?? 0m);
                debitTotal = RoundCurrency(debitTotal + debit);
                creditTotal = RoundCurrency(creditTotal + credit);

                if (ReadBool(line, "requiresAccountMapping"))
                    issues.Add($"Linea {lineNumber}: falta mapear cuenta contable para {FirstNonEmpty(accountName, "la linea")}.");
                if (debit < 0m || credit < 0m)
                    issues.Add($"Linea {lineNumber}: debito/credito no puede ser negativo.");
                if (debit > 0m && credit > 0m)
                    issues.Add($"Linea {lineNumber}: no puede tener debito y credito al mismo tiempo.");
                if (debit == 0m && credit == 0m)
                    continue;
                if (string.IsNullOrWhiteSpace(accountCode))
                {
                    issues.Add($"Linea {lineNumber}: falta codigo de cuenta.");
                    continue;
                }
                if (!accountCatalog.TryGetValue(accountCode, out var account))
                {
                    issues.Add($"Linea {lineNumber}: la cuenta {accountCode} no esta en el catalogo contable Siigo de Dataverse.");
                    continue;
                }
                if (!account.Active)
                    issues.Add($"Linea {lineNumber}: la cuenta {accountCode} esta inactiva.");
            }

            if (Math.Abs(debitTotal - creditTotal) > 1m)
                issues.Add($"El asiento no cuadra: debito {debitTotal:N2} vs credito {creditTotal:N2}.");
        }
        catch (JsonException)
        {
            issues.Add("El JSON de borrador Siigo no es valido.");
        }

        return new ConciliacionPreflightValidation(RoundCurrency(debitTotal), RoundCurrency(creditTotal), issues);
    }

    private static object BuildConciliacionClientPaymentSiigoDryRunPayload(
        ConciliacionClientPaymentRowDto row,
        ConciliacionPreflightValidation preflight,
        out int lineCount)
    {
        if (string.IsNullOrWhiteSpace(row.DraftJson))
            throw new InvalidOperationException("No existe JSON de borrador Siigo para armar la simulacion.");

        using var doc = JsonDocument.Parse(row.DraftJson);
        var root = doc.RootElement;
        var type = ReadString(root, "type");
        if (!string.Equals(type, "ComprobanteIngresoSiigoBorrador", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El borrador no corresponde al tipo ComprobanteIngresoSiigoBorrador.");
        if (!root.TryGetProperty("lines", out var linesElement)
            || linesElement.ValueKind != JsonValueKind.Array
            || linesElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("El borrador no tiene lineas contables para simular.");
        }

        var lines = new List<object>();
        foreach (var line in linesElement.EnumerateArray())
        {
            var debit = RoundCurrency(ReadDecimal(line, "debit") ?? 0m);
            var credit = RoundCurrency(ReadDecimal(line, "credit") ?? 0m);
            if (debit == 0m && credit == 0m)
                continue;

            var accountCode = ReadString(line, "accountCode").Trim();
            lines.Add(new
            {
                account = new
                {
                    code = accountCode,
                    name = ReadString(line, "accountName").Trim()
                },
                description = FirstNonEmpty(
                    ReadString(line, "description"),
                    ReadString(line, "detail"),
                    row.InvoiceNumbers,
                    row.Description).Trim(),
                thirdParty = FirstNonEmpty(ReadString(line, "thirdParty"), row.ClientNames).Trim(),
                detail = FirstNonEmpty(ReadString(line, "detail"), row.InvoiceNumbers).Trim(),
                debit,
                credit
            });
        }

        lineCount = lines.Count;
        if (lineCount == 0)
            throw new InvalidOperationException("El borrador no tiene lineas con debito o credito.");

        var movementDate = FirstNonEmpty(row.MovementDateValue, ReadString(root, "movement.date")).Trim();
        var invoices = ReadConciliacionDraftInvoices(root);

        return new
        {
            dryRun = true,
            targetEndpoint = "/v1/journals",
            note = "Payload de prueba generado por Conciliacion. No fue enviado a Siigo.",
            document = new
            {
                type = "CC",
                id = ConciliacionSiigoIncomeJournalDocumentFallbackId,
                code = "17",
                name = ConciliacionSiigoIncomeJournalDocumentFallbackName
            },
            date = movementDate,
            customer = new
            {
                name = row.ClientNames,
                invoices = row.InvoiceNumbers
            },
            movement = new
            {
                id = row.MovementId,
                externalKey = row.MovementExternalKey,
                sourceFlow = row.SourceFlow,
                bankAccountCode = row.BankAccountCode,
                bankAccountName = row.BankAccountName,
                description = row.Description,
                entry = row.EntryValue
            },
            totals = new
            {
                invoiceTotal = row.InvoiceTotal,
                payment = row.EntryValue,
                retentions = row.RetentionsTotal,
                difference = row.DifferenceValue,
                debit = preflight.DebitTotal,
                credit = preflight.CreditTotal
            },
            invoices,
            items = lines
        };
    }

    private static object BuildConciliacionClientPaymentSiigoSendPayload(
        ConciliacionClientPaymentRowDto row,
        IReadOnlyList<ConciliacionSiigoInvoiceDueItem> invoiceDues,
        string movementDate,
        string customerIdentification,
        SiigoDocumentTypeLookupDto journalDocument,
        decimal siigoAdjustment)
    {
        var items = new List<Dictionary<string, object?>>();
        var customer = new
        {
            identification = customerIdentification,
            branch_office = 0
        };
        if (row.EntryValue > 0m)
        {
            items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["account"] = new
                {
                    code = row.BankAccountCode,
                    movement = "Debit"
                },
                ["customer"] = customer,
                ["description"] = TruncateAccountCatalogText(
                    FirstNonEmpty($"Pago {row.InvoiceNumbers} {row.BankAccountName}", row.BankAccountName, "Banco"),
                    200),
                ["value"] = row.EntryValue
            });
        }

        foreach (var item in invoiceDues)
        {
            items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["account"] = new
                {
                    code = "13050501",
                    movement = "Credit"
                },
                ["customer"] = customer,
                ["description"] = TruncateAccountCatalogText($"Clientes nacionales {item.Invoice.InvoiceNumber}".Trim(), 200),
                ["due"] = new
                {
                    prefix = item.Due.Prefix,
                    consecutive = item.Due.Consecutive,
                    quote = item.Due.Quote,
                    date = string.IsNullOrWhiteSpace(item.Due.DateValue)
                        ? movementDate
                        : item.Due.DateValue
                },
                ["value"] = RoundCurrency(item.Value)
            });

            foreach (var retention in item.RetentionTaxes)
            {
                items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account"] = new
                    {
                        code = retention.AccountCode,
                        movement = "Debit"
                    },
                    ["customer"] = customer,
                    ["tax"] = new
                    {
                        id = retention.TaxId
                    },
                    ["description"] = TruncateAccountCatalogText($"{retention.Kind} {item.Invoice.InvoiceNumber}".Trim(), 200),
                    ["value"] = retention.Value
                });
            }
        }

        var adjustment = RoundCurrency(siigoAdjustment);
        if (Math.Abs(adjustment) > 0.009m)
        {
            items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["account"] = new
                {
                    code = "42958101",
                    movement = adjustment > 0m ? "Debit" : "Credit"
                },
                ["customer"] = customer,
                ["description"] = TruncateAccountCatalogText($"Ajuste al peso {row.InvoiceNumbers}".Trim(), 200),
                ["value"] = Math.Abs(adjustment)
            });
        }

        return new
        {
            document = new
            {
                id = journalDocument.Id
            },
            date = movementDate,
            items,
            observations = TruncateAccountCatalogText(
                $"{journalDocument.Name} - Conciliacion flujo caja {row.SourceFlow} {row.MovementExternalKey} {row.Description}".Trim(),
                500)
        };
    }

    private static IReadOnlyDictionary<string, SiigoInvoiceRowDto> BuildConciliacionSiigoInvoiceLookup(
        IReadOnlyList<SiigoInvoiceRowDto> invoices)
    {
        var lookup = new Dictionary<string, SiigoInvoiceRowDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var invoice in invoices ?? Array.Empty<SiigoInvoiceRowDto>())
        {
            AddConciliacionSiigoInvoiceLookupKey(lookup, invoice.Id, invoice);
            AddConciliacionSiigoInvoiceLookupKey(lookup, invoice.Name, invoice);
            if (!string.IsNullOrWhiteSpace(invoice.Prefix) && invoice.Number is > 0)
                AddConciliacionSiigoInvoiceLookupKey(lookup, $"{invoice.Prefix}-{invoice.Number}", invoice);
        }

        return lookup;
    }

    private static void AddConciliacionSiigoInvoiceLookupKey(
        IDictionary<string, SiigoInvoiceRowDto> lookup,
        string? key,
        SiigoInvoiceRowDto invoice)
    {
        var normalized = NormalizeDocumentKey(key);
        if (!string.IsNullOrWhiteSpace(normalized) && !lookup.ContainsKey(normalized))
            lookup[normalized] = invoice;
    }

    private static decimal ResolveConciliacionSiigoInvoiceAccountingValue(
        BillingRecordRow invoice,
        IReadOnlyDictionary<string, SiigoInvoiceRowDto> siigoInvoiceLookup,
        bool requireLiveSiigoInvoice,
        ICollection<string> issues)
    {
        var siigoInvoice = FindConciliacionSiigoInvoice(invoice, siigoInvoiceLookup);
        if (siigoInvoice is null)
        {
            if (requireLiveSiigoInvoice)
            {
                issues.Add($"No encontre en Siigo la factura {FirstNonEmpty(invoice.SiigoInvoiceName, invoice.InvoiceNumber)} para confirmar saldo actual.");
            }

            return RoundCurrency(invoice.NetTotalInvoice);
        }

        var balance = RoundCurrency(
            siigoInvoice.GrossBalance != 0m || siigoInvoice.Balance == 0m
                ? siigoInvoice.GrossBalance
                : siigoInvoice.Balance);
        if (balance <= 0m)
        {
            issues.Add($"La factura {siigoInvoice.Name} aparece sin saldo pendiente en Siigo.");
            return 0m;
        }

        return balance;
    }

    private static SiigoInvoiceRowDto? FindConciliacionSiigoInvoice(
        BillingRecordRow invoice,
        IReadOnlyDictionary<string, SiigoInvoiceRowDto> siigoInvoiceLookup)
    {
        foreach (var key in new[]
        {
            invoice.SiigoInvoiceId,
            invoice.SiigoInvoiceName,
            invoice.InvoiceNumber,
            !string.IsNullOrWhiteSpace(invoice.InvoicePrefix) && !string.IsNullOrWhiteSpace(invoice.InvoiceCode)
                ? $"{invoice.InvoicePrefix}-{invoice.InvoiceCode}"
                : ""
        })
        {
            if (siigoInvoiceLookup.TryGetValue(NormalizeDocumentKey(key), out var siigoInvoice))
                return siigoInvoice;
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractConciliacionInvoiceRecordIds(ConciliacionClientPaymentRowDto row)
    {
        var values = SplitConciliacionRecordIdList(row.InvoiceRecordIds)
            .Concat(ExtractConciliacionDraftInvoiceRecordIds(row.DraftJson))
            .Select(static value => Guid.TryParse(value, out var parsed) ? parsed.ToString("D") : "")
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values;
    }

    private static IEnumerable<string> SplitConciliacionRecordIdList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        return raw
            .Split(new[] { '|', ';', ',', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static value => !string.IsNullOrWhiteSpace(value));
    }

    private static IReadOnlyList<string> ExtractConciliacionDraftInvoiceRecordIds(string? draftJson)
    {
        if (string.IsNullOrWhiteSpace(draftJson))
            return Array.Empty<string>();

        using var doc = JsonDocument.Parse(draftJson);
        if (!doc.RootElement.TryGetProperty("invoices", out var invoices)
            || invoices.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return invoices
            .EnumerateArray()
            .Select(static invoice => ReadString(invoice, "recordId").Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static bool TryBuildConciliacionSiigoDue(
        BillingRecordRow invoice,
        SiigoInvoiceRowDto? liveSiigoInvoice,
        bool requireLiveSiigoInvoice,
        out ConciliacionSiigoDue due,
        out string issue)
    {
        due = new ConciliacionSiigoDue("", 0, 0, "");
        issue = "";

        if (liveSiigoInvoice is not null)
        {
            if (liveSiigoInvoice.HasExactDueReference
                && !string.IsNullOrWhiteSpace(liveSiigoInvoice.DuePrefix)
                && liveSiigoInvoice.DueConsecutive > 0
                && liveSiigoInvoice.DueQuote > 0
                && DateOnly.TryParseExact(
                    liveSiigoInvoice.DueDateValue,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var dueDate))
            {
                due = new ConciliacionSiigoDue(
                    liveSiigoInvoice.DuePrefix.Trim(),
                    liveSiigoInvoice.DueConsecutive,
                    liveSiigoInvoice.DueQuote,
                    dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                return true;
            }

            issue = FirstNonEmpty(
                liveSiigoInvoice.DueReferenceIssue,
                $"No se pudo confirmar el vencimiento existente de {liveSiigoInvoice.Name} en Siigo.");
            return false;
        }

        if (requireLiveSiigoInvoice)
        {
            issue = $"No se encontro en Siigo la factura {FirstNonEmpty(invoice.SiigoInvoiceName, invoice.InvoiceNumber)} para confirmar su vencimiento exacto.";
            return false;
        }

        var label = FirstNonEmpty(invoice.SiigoInvoiceName, invoice.InvoiceNumber);
        if (!TryParseConciliacionDueLabel(label, out var parsedDue)
            && IsConciliacionInvoiceDuePrefix(invoice.InvoicePrefix)
            && int.TryParse(invoice.InvoiceCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var siigoCode))
        {
            parsedDue = new ConciliacionSiigoDue(invoice.InvoicePrefix.Trim(), siigoCode, 1, "");
        }

        due = parsedDue;
        if (!string.IsNullOrWhiteSpace(due.Prefix) && due.Consecutive > 0)
            return true;

        issue = $"No se pudo separar prefijo y consecutivo Siigo para la factura {FirstNonEmpty(label, invoice.InvoicePrefix, invoice.InvoiceCode)}.";
        return false;
    }

    private static bool TryParseConciliacionDueLabel(string label, out ConciliacionSiigoDue due)
    {
        due = new ConciliacionSiigoDue("", 0, 0, "");
        var normalized = Regex.Replace((label ?? "").Trim().ToUpperInvariant(), @"\s+", "-", RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"-+", "-", RegexOptions.CultureInvariant).Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var match = Regex.Match(normalized, @"^(?<prefix>.*?)[-]?(?<consecutive>\d+)$", RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups["consecutive"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var consecutive))
            return false;

        var prefix = match.Groups["prefix"].Value.Trim('-');
        if (string.IsNullOrWhiteSpace(prefix) || !IsConciliacionInvoiceDuePrefix(prefix))
            return false;

        due = new ConciliacionSiigoDue(prefix, consecutive, 1, "");
        return true;
    }

    private static bool IsConciliacionInvoiceDuePrefix(string? value)
    {
        var prefix = (value ?? "").Trim().ToUpperInvariant();
        return Regex.IsMatch(prefix, @"^(?:FV|FVE|FEV|FEM|FE|FEDT|FEKT)(?:-\d+)?$", RegexOptions.CultureInvariant);
    }

    private static string NormalizeConciliacionIdentificationDigits(string? value) =>
        NormalizeConciliacionDigits(value);

    private static decimal ResolveConciliacionInvoiceRetentionsTotal(BillingRecordRow invoice) =>
        RoundCurrency(
            ResolveCashFlowClientPaymentReteFteValue(invoice)
            + ResolveCashFlowClientPaymentReteIcaValue(invoice)
            + ResolveCashFlowClientPaymentRteIvaValue(invoice));

    private static IReadOnlyList<ConciliacionRetentionTax> ResolveConciliacionInvoiceRetentionTaxes(
        BillingRecordRow invoice,
        IReadOnlyList<SiigoTaxLookupDto> siigoTaxes,
        ICollection<string> issues)
    {
        var result = new List<ConciliacionRetentionTax>();
        AddConciliacionRetentionTax(
            result,
            issues,
            siigoTaxes,
            invoice.InvoiceNumber,
            kind: "ReteFte",
            label: "retefuente",
            value: ResolveCashFlowClientPaymentReteFteValue(invoice),
            baseValue: ResolveConciliacionRetentionBase(invoice));
        AddConciliacionRetentionTax(
            result,
            issues,
            siigoTaxes,
            invoice.InvoiceNumber,
            kind: "ReteIca",
            label: "ReteICA",
            value: ResolveCashFlowClientPaymentReteIcaValue(invoice),
            baseValue: ResolveConciliacionRetentionBase(invoice));
        AddConciliacionRetentionTax(
            result,
            issues,
            siigoTaxes,
            invoice.InvoiceNumber,
            kind: "RteIva",
            label: "RteIVA",
            value: ResolveCashFlowClientPaymentRteIvaValue(invoice),
            baseValue: invoice.NetVatValue);

        return result;
    }

    private static IReadOnlyDictionary<string, ConciliacionExactClientPaymentInvoice> ReadConciliacionExactClientPaymentInvoices(
        string? draftJson)
    {
        if (string.IsNullOrWhiteSpace(draftJson))
            return new Dictionary<string, ConciliacionExactClientPaymentInvoice>(StringComparer.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(draftJson);
        var root = document.RootElement;
        if (!string.Equals(ReadString(root, "type"), "ComprobanteIngresoSiigoBorrador", StringComparison.OrdinalIgnoreCase)
            || !root.TryGetProperty("invoices", out var invoicesElement)
            || invoicesElement.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, ConciliacionExactClientPaymentInvoice>(StringComparer.OrdinalIgnoreCase);
        }

        var hasExactValues = invoicesElement
            .EnumerateArray()
            .Any(static invoice => invoice.TryGetProperty("gross", out _)
                || invoice.TryGetProperty("payment", out _)
                || invoice.TryGetProperty("retentions", out _));
        if (!hasExactValues)
            return new Dictionary<string, ConciliacionExactClientPaymentInvoice>(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, ConciliacionExactClientPaymentInvoice>(StringComparer.OrdinalIgnoreCase);
        foreach (var invoice in invoicesElement.EnumerateArray())
        {
            var recordId = ReadString(invoice, "recordId").Trim();
            var invoiceNumber = FirstNonEmpty(ReadString(invoice, "number"), recordId).Trim();
            var grossValue = RoundCurrency(ReadDecimal(invoice, "gross") ?? 0m);
            var adjustmentValue = RoundCurrency(ReadDecimal(invoice, "adjustment") ?? 0m);
            if (!Guid.TryParse(recordId, out _) || grossValue <= 0m)
                throw new InvalidOperationException($"El detalle exacto de {invoiceNumber} esta incompleto.");
            if (result.ContainsKey(recordId))
                throw new InvalidOperationException($"El detalle exacto repite la factura {invoiceNumber}.");

            var retentions = new List<ConciliacionRetentionTax>();
            if (invoice.TryGetProperty("retentions", out var retentionsElement)
                && retentionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var retention in retentionsElement.EnumerateArray())
                {
                    var value = RoundCurrency(ReadDecimal(retention, "value") ?? 0m);
                    if (value <= 0m)
                        continue;

                    var kind = FirstNonEmpty(ReadString(retention, "kind"), ReadString(retention, "label")).Trim();
                    var taxId = ReadInt(retention, "taxId");
                    var accountCode = ReadString(retention, "accountCode").Trim();
                    var rate = ReadDecimal(retention, "rate") ?? 0m;
                    if (taxId <= 0 || string.IsNullOrWhiteSpace(accountCode) || string.IsNullOrWhiteSpace(kind))
                        throw new InvalidOperationException($"El detalle exacto de {invoiceNumber} tiene una retencion incompleta.");

                    retentions.Add(new ConciliacionRetentionTax(kind, taxId, accountCode, value, rate));
                }
            }

            result[recordId] = new ConciliacionExactClientPaymentInvoice(
                recordId,
                grossValue,
                adjustmentValue,
                retentions);
        }

        return result;
    }

    private static void AddConciliacionRetentionTax(
        ICollection<ConciliacionRetentionTax> result,
        ICollection<string> issues,
        IReadOnlyList<SiigoTaxLookupDto> siigoTaxes,
        string invoiceNumber,
        string kind,
        string label,
        decimal value,
        decimal baseValue)
    {
        value = RoundCurrency(value);
        if (value <= 0m)
            return;

        if (baseValue <= 0m)
        {
            issues.Add($"La factura {invoiceNumber} tiene {label}, pero no hay base para calcular el porcentaje.");
            return;
        }

        var siigoRate = CalculateConciliacionRetentionSiigoRate(kind, value, baseValue);
        var tax = FindConciliacionRetentionTax(siigoTaxes, kind, siigoRate);
        if (tax is null)
        {
            issues.Add($"No encontre impuesto Siigo activo para {label} {FormatConciliacionRetentionSiigoRate(kind, siigoRate)} de la factura {invoiceNumber}.");
            return;
        }

        var accountCode = ResolveConciliacionRetentionAccountCode(kind, tax, siigoRate);
        if (string.IsNullOrWhiteSpace(accountCode))
        {
            issues.Add($"La factura {invoiceNumber} tiene {label} {FormatConciliacionRetentionSiigoRate(kind, siigoRate)} con impuesto Siigo {tax.Name}, pero falta mapear la cuenta contable de esa tarifa para comprobante de ingreso.");
            return;
        }

        result.Add(new ConciliacionRetentionTax(kind, tax.Id, accountCode, value, siigoRate));
    }

    private static decimal CalculateConciliacionRetentionSiigoRate(string kind, decimal value, decimal baseValue)
    {
        var multiplier = string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase)
            ? 1000m
            : 100m;
        return Math.Round(value / baseValue * multiplier, 4, MidpointRounding.AwayFromZero);
    }

    private static string FormatConciliacionRetentionSiigoRate(string kind, decimal rate) =>
        string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase)
            ? $"{rate:N4} por mil"
            : $"{rate:N4}%";

    private static string ResolveConciliacionRetentionAccountCode(
        string kind,
        SiigoTaxLookupDto tax,
        decimal siigoRate) =>
        ConciliacionRetentionMapping.ResolveAccountCode(kind, tax, siigoRate);

    private static SiigoTaxLookupDto? FindConciliacionRetentionTax(
        IReadOnlyList<SiigoTaxLookupDto> siigoTaxes,
        string kind,
        decimal siigoRate)
    {
        return ConciliacionRetentionMapping.FindClientPaymentTax(siigoTaxes, kind, siigoRate);
    }

    private static bool MatchesConciliacionRetentionTaxKind(SiigoTaxLookupDto tax, string kind)
    {
        return ConciliacionRetentionMapping.MatchesKind(tax, kind);
    }

    private static decimal ResolveConciliacionRetentionBase(BillingRecordRow invoice)
    {
        var baseValue = RoundCurrency(invoice.NetTotalInvoice - invoice.NetVatValue);
        return baseValue > 0m ? baseValue : invoice.NetTotalInvoice;
    }

    private static string NormalizeConciliacionTaxText(string value)
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

    private static object ResolveConciliacionClientPaymentType(string sourceFlow, string bankAccountCode)
    {
        var id = ResolveConciliacionClientPaymentTypeId(sourceFlow, bankAccountCode);
        return new
        {
            documentType = "RC",
            id,
            name = id == 13568 ? "Bancolombia Copiers Ventas" : "Bancolombia Cloud Ventas"
        };
    }

    private static int ResolveConciliacionClientPaymentTypeId(string sourceFlow, string bankAccountCode)
    {
        var isCopiers = sourceFlow.Contains("Copiers", StringComparison.OrdinalIgnoreCase)
            || bankAccountCode.Contains("11100505", StringComparison.OrdinalIgnoreCase);

        return isCopiers ? 13568 : 13566;
    }

    private static IReadOnlyList<object> ReadConciliacionDraftInvoices(JsonElement root)
    {
        if (!root.TryGetProperty("invoices", out var invoicesElement)
            || invoicesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<object>();
        }

        return invoicesElement
            .EnumerateArray()
            .Select(invoice => new
            {
                recordId = ReadString(invoice, "recordId").Trim(),
                number = ReadString(invoice, "number").Trim(),
                client = ReadString(invoice, "client").Trim(),
                total = RoundCurrency(ReadDecimal(invoice, "total") ?? 0m),
                vat = RoundCurrency(ReadDecimal(invoice, "vat") ?? 0m)
            })
            .Cast<object>()
            .ToArray();
    }

    private static string NormalizeConciliacionClientPaymentStatus(string? rawStatus)
    {
        var status = (rawStatus ?? "").Trim();
        var allowed = new HashSet<string>(new[]
        {
            "Sugerido",
            "Aprobado",
            "Rechazado",
            "RevisionManual"
        }, StringComparer.OrdinalIgnoreCase);

        if (!allowed.Contains(status))
            throw new InvalidOperationException("El estado solicitado no es valido.");

        return allowed.First(value => string.Equals(value, status, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsConciliacionPendingReviewStatus(string status)
    {
        return status switch
        {
            "DiferenciaFueraRango" => true,
            "SinFacturaDescripcion" => true,
            "FacturaNoEncontrada" => true,
            "FacturaAmbigua" => true,
            "RevisionManual" => true,
            "BloqueadoSiigo" => true,
            _ => false
        };
    }

    private static bool IsConciliacionApprovedForSiigo(string status) =>
        string.Equals(status, "Aprobado", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "ListoSiigo", StringComparison.OrdinalIgnoreCase);

    private static bool IsConciliacionReadyForRealSendStatus(string status) =>
        string.Equals(status, "ListoSiigo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "ErrorSiigo", StringComparison.OrdinalIgnoreCase);

    private static bool IsConciliacionSiigoCandidateStatus(string status) =>
        string.Equals(status, "Sugerido", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Aprobado", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "ListoSiigo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "BloqueadoSiigo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "ErrorSiigo", StringComparison.OrdinalIgnoreCase);

    private static string ResolveConciliacionStatusLabel(string status)
    {
        return status switch
        {
            "Sugerido" => "Sugerido",
            "Aprobado" => "Aprobado",
            "Rechazado" => "Rechazado",
            "RevisionManual" => "Revision manual",
            "ListoSiigo" => "Listo Siigo",
            "EnviadoSiigo" => "Enviado Siigo",
            "ErrorSiigo" => "Error Siigo",
            "Conciliado" => "Conciliado",
            "BloqueadoSiigo" => "Bloqueado pre-Siigo",
            "ReasignadoCategoria" => "Reasignado a otra categoria",
            "Omitido" => "Omitido",
            "DiferenciaFueraRango" => "Diferencia fuera de rango",
            "SinFacturaDescripcion" => "Sin factura en descripcion",
            "FacturaNoEncontrada" => "Factura no encontrada",
            "FacturaAmbigua" => "Factura ambigua",
            _ => status
        };
    }

    private static string ResolveConciliacionStatusTone(string status)
    {
        return status switch
        {
            "Sugerido" => "info",
            "Aprobado" => "success",
            "Rechazado" => "danger",
            "RevisionManual" => "warning",
            "ListoSiigo" => "success",
            "EnviadoSiigo" => "success",
            "ErrorSiigo" => "danger",
            "Conciliado" => "success",
            "BloqueadoSiigo" => "danger",
            "ReasignadoCategoria" => "neutral",
            "Omitido" => "neutral",
            "DiferenciaFueraRango" => "warning",
            "SinFacturaDescripcion" => "neutral",
            "FacturaNoEncontrada" => "danger",
            "FacturaAmbigua" => "warning",
            _ => "neutral"
        };
    }

    private static string ResolveConciliacionPreflightStatusLabel(string status)
    {
        return status switch
        {
            "ListoSiigo" => "Listo Siigo",
            "EnviadoSiigo" => "Enviado Siigo",
            "ErrorSiigo" => "Error Siigo",
            "ValidadoPendienteAprobacion" => "OK, falta aprobar",
            "BloqueadoSiigo" => "Bloqueado",
            "ReasignadoCategoria" => "Reasignado",
            "Omitido" => "Omitido",
            _ => string.IsNullOrWhiteSpace(status) ? "Sin validar" : status
        };
    }

    private static string ResolveConciliacionPreflightStatusTone(string status)
    {
        return status switch
        {
            "ListoSiigo" => "success",
            "EnviadoSiigo" => "success",
            "ErrorSiigo" => "danger",
            "ValidadoPendienteAprobacion" => "info",
            "BloqueadoSiigo" => "danger",
            "ReasignadoCategoria" => "neutral",
            "Omitido" => "neutral",
            _ => "neutral"
        };
    }

    private static DateTimeOffset? ParseConciliacionDateTimeOffset(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            || DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value))
        {
            return value;
        }

        return null;
    }

    private static string FormatConciliacionDateTimeDisplay(DateTimeOffset? value)
    {
        if (!value.HasValue || value.Value == default)
            return "Sin log";

        var bogota = TimeZoneInfo.ConvertTime(value.Value, MonthlyFinancialReconciliationHostedService.ResolveTimeZone("SA Pacific Standard Time"));
        return bogota.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    private sealed record ConciliacionPreflightValidation(
        decimal DebitTotal,
        decimal CreditTotal,
        IReadOnlyList<string> Issues);

    private sealed record ConciliacionSiigoDue(
        string Prefix,
        int Consecutive,
        int Quote,
        string DateValue);

    private sealed record ConciliacionSiigoInvoiceDueItem(
        BillingRecordRow Invoice,
        ConciliacionSiigoDue Due,
        decimal Value,
        IReadOnlyList<ConciliacionRetentionTax> RetentionTaxes);

    private sealed record ConciliacionRetentionTax(
        string Kind,
        int TaxId,
        string AccountCode,
        decimal Value,
        decimal Percentage);

    private sealed record ConciliacionExactClientPaymentInvoice(
        string RecordId,
        decimal GrossValue,
        decimal AdjustmentValue,
        IReadOnlyList<ConciliacionRetentionTax> RetentionTaxes);

    private sealed record ConciliacionAccountCatalogItem(
        string Code,
        string Name,
        bool Active);
}
