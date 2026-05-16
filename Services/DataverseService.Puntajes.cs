using System.Globalization;
using System.IO;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.Calculator;
using CotizadorInterno.Web.Models.Puntajes;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private static readonly Regex ExtendedScoreDescriptionFieldRegex = new(
        "(?<key>Cliente|Fecha aprovisionamiento|Tipo contrato|Tipo negocio|Requiere prorrateo|Requiere prorateo|Inicio|Final|Puntaje|Comisi(?:\\u00F3|o)n|BusinessId|Prorrateo|Prorateo|Venta mensual total|Venta total anual|Venta total)\\s*:\\s*(?<value>.*?)(?=(Cliente|Fecha aprovisionamiento|Tipo contrato|Tipo negocio|Requiere prorrateo|Requiere prorateo|Inicio|Final|Puntaje|Comisi(?:\\u00F3|o)n|BusinessId|Prorrateo|Prorateo|Venta mensual total|Venta total anual|Venta total|L(?:\\u00ED|i)neas)\\s*:|$)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex ProrationTextRegex = new(
        "(?<days>\\d+)\\s*d(?:\\u00ED|i)as?.*?\\((?<factor>[\\d\\.,]+)\\)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly HashSet<int> AllowedFirstContractOptionValues = new() { 1, 2 };
    private static readonly HashSet<int> AllowedLineOptionValues = new() { 645250000, 645250001, 645250002, 645250003, 645250004, 645250005, 645250006, 645250007 };
    private static readonly HashSet<int> AllowedVerticalOptionValues = new() { 645250000, 645250001 };
    private static readonly HashSet<int> AllowedBinaryOptionValues = new() { 0, 1 };
    private static readonly HashSet<int> AllowedProductLineOptionValues = new() { 0, 1, 2, 3 };
    private static readonly HashSet<int> AllowedContractTypeOptionValues = new() { 0, 1 };
    private static readonly HashSet<int> AllowedContractKindOptionValues = new() { ScoreContractKindNewBusinessValue, ScoreContractKindRenewalValue };
    private const int ScoreContractKindNewBusinessValue = 645250000;
    private const int ScoreContractKindRenewalValue = 645250001;

    public async Task<ScoreBoardDto> GetScoreBoardAsync(ScorePeriodFilter filter, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var filterParts = new List<string>
        {
            $"{_scoresContractStartDateField} ne null"
        };

        var periodFilter = BuildScorePeriodFilter(filter);
        if (!string.IsNullOrWhiteSpace(periodFilter))
        {
            filterParts.Add(periodFilter);
        }

        var monthInfo = GetScoreMonthInfo(filter);
        var relativeUrl = $"/api/data/v9.2/{_scoresTableSetName}?$filter={Uri.EscapeDataString(string.Join(" and ", filterParts))}&$orderby={_scoresContractStartDateField} asc";
        var rawRecords = await GetDataverseEntitiesAsync(relativeUrl, httpContext.User, ct, AddFormattedValueHeaders);

        var contexts = rawRecords
            .Select(item => ParseScoreRecordContext(item, monthInfo.PeriodKey))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
        var records = contexts
            .Select(item => item.Record)
            .OrderBy(item => item.ContractStartDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Offer, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var latestBatch = ResolveLatestMonthCloseBatch(contexts, monthInfo.PeriodKey);

        var groups = records
            .GroupBy(GetScoreClientGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var orderedRecords = group
                    .OrderBy(item => item.ContractStartDateValue, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Offer, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.SalesPerson, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var first = orderedRecords[0];
                return new ScoreClientGroupDto
                {
                    ClientId = first.ClientId,
                    ClientName = first.ClientName,
                    OwnerId = first.OwnerId,
                    OwnerName = first.OwnerName,
                    SalesPerson = orderedRecords
                        .Select(item => item.SalesPerson)
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                        ?? "Sin vendedor",
                    AllVerified = orderedRecords.Count > 0 && orderedRecords.All(item => item.IsVerified),
                    RecordCount = orderedRecords.Count,
                    ProductLinesCount = orderedRecords.Sum(item => item.ProductLinesCount),
                    TotalCommission = RoundCurrency(orderedRecords.Sum(item => item.Commission)),
                    TotalScore = RoundCurrency(orderedRecords.Sum(item => item.Score)),
                    TotalMonthlyValue = RoundCurrency(orderedRecords.Sum(item => item.MonthlyValue)),
                    TotalValue = RoundCurrency(orderedRecords.Sum(item => item.TotalValue)),
                    TotalAnnualValue = RoundCurrency(orderedRecords.Sum(item => item.TotalValue)),
                    Records = orderedRecords
                };
            })
            .OrderBy(group => group.ClientName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Records[0].ContractStartDateValue, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ScoreBoardDto
        {
            Filter = filter.ToKey(),
            FilterLabel = filter.ToLabel(),
            ClientsCount = groups.Count,
            RecordsCount = records.Count,
            ProductLinesCount = records.Sum(item => item.ProductLinesCount),
            TotalCommission = RoundCurrency(records.Sum(item => item.Commission)),
            TotalScore = RoundCurrency(records.Sum(item => item.Score)),
            TotalMonthlyValue = RoundCurrency(records.Sum(item => item.MonthlyValue)),
            TotalValue = RoundCurrency(records.Sum(item => item.TotalValue)),
            TotalAnnualValue = RoundCurrency(records.Sum(item => item.TotalValue)),
            VerifiedRecordsCount = records.Count(item => item.IsVerified),
            ClosedRecordsCount = records.Count(item => item.IsClosedForActivePeriod),
            SupportsMonthClose = monthInfo.SupportsClose,
            MonthClosePeriodKey = monthInfo.PeriodKey,
            MonthClosePeriodLabel = monthInfo.PeriodLabel,
            CanUndoMonthClose = latestBatch is not null,
            UndoMonthCloseLabel = latestBatch is null
                ? ""
                : string.IsNullOrWhiteSpace(latestBatch.ClosedBy)
                    ? $"Ultimo cierre: {FormatDateTimeDisplay(latestBatch.ClosedAt)}"
                    : $"Ultimo cierre: {FormatDateTimeDisplay(latestBatch.ClosedAt)} por {latestBatch.ClosedBy}",
            Groups = groups
        };
    }

    public async Task<ScoreVerificationDetailDto> GetScoreVerificationDetailAsync(string recordId, ScorePeriodFilter filter, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var monthInfo = GetScoreMonthInfo(filter);
        var item = await GetScoreRecordJsonAsync(normalizedRecordId, httpContext.User, ct);
        var context = ParseScoreRecordContext(item, monthInfo.PeriodKey)
            ?? throw new InvalidOperationException("No fue posible interpretar el registro seleccionado.");
        var scenario = await GetScenarioByBusinessIdAsync(context.Record.BusinessId, httpContext.User, ct);
        return BuildScoreVerificationDetail(context, scenario);
    }

    public Task<ScoreVerificationComputedResultDto> RecalculateScoreRecordAsync(ScoreVerificationRequest request, CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var computation = BuildScoreComputationContext(request, requireProductLookup: false);
        return Task.FromResult(computation.Result);
    }

    public async Task<ScoreVerificationSaveResultDto> VerifyScoreRecordAsync(ScoreVerificationRequest request, CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var normalizedRecordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var existingItem = await GetScoreRecordJsonAsync(normalizedRecordId, httpContext.User, ct);
        var existingContext = ParseScoreRecordContext(existingItem, activePeriodKey: null)
            ?? throw new InvalidOperationException("No se encontro el registro seleccionado.");
        var currentUser = await GetCurrentUserAsync(ct) ?? new Models.CurrentUserInfo();
        var normalizedRequest = NormalizeVerificationRequest(request, existingContext.Record.ContractStartDateValue);
        var verifiedFieldKind = DetectPrimitiveFieldKind(existingItem, _scoresVerifiedField);
        var firstContractFieldKind = DetectPrimitiveFieldKind(existingItem, _scoresFirstContractField);

        var computation = BuildScoreComputationContext(normalizedRequest, requireProductLookup: true);
        var additional = BuildAdditionalSnapshot(normalizedRequest, computation, existingContext.Additional, currentUser);
        var additionalJson = SerializeAdditionalForDataverse(additional);

        var updateUrl = $"/api/data/v9.2/{_scoresTableSetName}({normalizedRecordId})";
        Exception? lastError = null;
        foreach (var payload in BuildVerificationPayloadCandidates(normalizedRequest, computation.Result, additionalJson, verifiedFieldKind, firstContractFieldKind))
        {
            try
            {
                await CallDataverseSendAsync(updateUrl, "PATCH", payload, httpContext.User, ct);
                var message = "El registro se verifico correctamente y el puntaje fue recalculado.";
                if (normalizedRequest.AutoBillOptionValue == 0)
                {
                    var billingNotificationStatus = await TryNotifyBillingAsync(existingContext.Record, normalizedRequest, computation.Result, currentUser, ct);
                    if (!string.IsNullOrWhiteSpace(billingNotificationStatus))
                        message = $"{message} Advertencia: {billingNotificationStatus}";
                    else
                        message = $"{message} Se notifico a facturacion para gestionar la facturacion manual.";
                }

                return new ScoreVerificationSaveResultDto
                {
                    Ok = true,
                    Message = message,
                    Result = computation.Result
                };
            }
            catch (InvalidOperationException ex)
            {
                lastError = new InvalidOperationException(BuildVerificationPayloadFailureDetail(payload, ex), ex);
            }
        }

        throw new InvalidOperationException("No se pudo guardar la verificacion en Dataverse.", lastError);
    }

    public async Task<ScoreRecordDeleteResultDto> DeleteScoreRecordAsync(string recordId, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var existingItem = await GetScoreRecordJsonAsync(normalizedRecordId, httpContext.User, ct);
        var existingContext = ParseScoreRecordContext(existingItem, activePeriodKey: null)
            ?? throw new InvalidOperationException("No se encontro el registro seleccionado.");

        if (existingContext.Record.IsVerified)
            throw new InvalidOperationException("Solo puedes eliminar registros pendientes de verificacion.");

        if (existingContext.Additional.MonthlyClosures.Any())
            throw new InvalidOperationException("No se puede eliminar un registro que ya tiene cierres mensuales asociados.");

        var deleteUrl = $"/api/data/v9.2/{_scoresTableSetName}({normalizedRecordId})";
        await CallDataverseDeleteAsync(deleteUrl, httpContext.User, ct);

        return new ScoreRecordDeleteResultDto
        {
            Ok = true,
            RecordId = existingContext.Record.RecordId,
            Message = "El registro pendiente fue eliminado correctamente."
        };
    }

    public async Task<ScoreMoveToRenewalResultDto> MoveScoreBusinessToRenewalAsync(ScoreMoveToRenewalRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var recordIds = (request.RecordIds ?? new List<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizeGuid(value, nameof(request.RecordIds)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (recordIds.Count == 0)
            throw new InvalidOperationException("Debes indicar al menos un registro valido para mover.");

        var updatedRecordIds = new List<string>();
        foreach (var recordId in recordIds)
        {
            var existingItem = await GetScoreRecordJsonAsync(recordId, httpContext.User, ct);
            var existingContext = ParseScoreRecordContext(existingItem, activePeriodKey: null)
                ?? throw new InvalidOperationException("No se encontro uno de los registros seleccionados.");

            if (existingContext.Additional.MonthlyClosures.Any())
                throw new InvalidOperationException($"El negocio de {existingContext.Record.ClientName} ya tiene cierres mensuales asociados y no se puede mover desde esta vista.");

            if (IsRenewalContractKind(existingContext.Record.ContractKindOptionValue))
                continue;

            existingContext.Additional.ContractKindOptionValue = ScoreContractKindRenewalValue;
            var payload = new Dictionary<string, object?>
            {
                [_scoresContractKindField] = ScoreContractKindRenewalValue,
                [_scoresAdditionalField] = SerializeAdditionalForDataverse(existingContext.Additional)
            };

            await CallDataverseSendAsync($"/api/data/v9.2/{_scoresTableSetName}({recordId})", "PATCH", payload, httpContext.User, ct);
            updatedRecordIds.Add(recordId);
        }

        return new ScoreMoveToRenewalResultDto
        {
            Ok = true,
            UpdatedCount = updatedRecordIds.Count,
            RecordIds = updatedRecordIds,
            Message = updatedRecordIds.Count == 0
                ? "El negocio ya estaba marcado como renovacion."
                : $"Se movieron {updatedRecordIds.Count} registro(s) a renovacion."
        };
    }

    public async Task<ScoreMonthCloseResultDto> CloseScoreMonthAsync(ScorePeriodFilter filter, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var monthInfo = GetScoreMonthInfo(filter);
        if (!monthInfo.SupportsClose || string.IsNullOrWhiteSpace(monthInfo.PeriodKey))
            throw new InvalidOperationException("El cierre de mes solo esta disponible en vistas mensuales.");

        var filterParts = new List<string>
        {
            $"{_scoresContractStartDateField} ne null",
            BuildScorePeriodFilter(filter)
        };

        var relativeUrl = $"/api/data/v9.2/{_scoresTableSetName}?$filter={Uri.EscapeDataString(string.Join(" and ", filterParts.Where(part => !string.IsNullOrWhiteSpace(part))))}&$orderby={_scoresContractStartDateField} asc";
        var rawRecords = await GetDataverseEntitiesAsync(relativeUrl, httpContext.User, ct, AddFormattedValueHeaders);
        var contexts = rawRecords
            .Select(item => ParseScoreRecordContext(item, monthInfo.PeriodKey))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.Record.ContractStartDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Record.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (contexts.Count == 0)
            throw new InvalidOperationException("No hay registros para cerrar en el periodo seleccionado.");

        var unverifiedCount = contexts.Count(item => !item.Record.IsVerified);
        if (unverifiedCount > 0)
            throw new InvalidOperationException($"No puedes cerrar el mes hasta verificar las {unverifiedCount} lineas pendientes.");

        var currentUser = await GetCurrentUserAsync(ct) ?? new Models.CurrentUserInfo();
        var scenarioCache = new Dictionary<string, ScenarioStoredDto?>(StringComparer.OrdinalIgnoreCase);
        var salesPerformanceCache = new Dictionary<string, List<SalesPerformanceCompactRecord>>(StringComparer.OrdinalIgnoreCase);
        var logs = new List<ScoreMonthCloseLogEntryDto>();
        var createdCount = 0;
        var updatedCount = 0;
        var skippedCount = 0;
        var errorCount = 0;
        var processedRecordsCount = 0;

        foreach (var context in contexts)
        {
            if (context.Record.IsClosedForActivePeriod)
            {
                skippedCount++;
                logs.Add(BuildMonthCloseLog("info", context.Record.RecordId, context.Record.ClientName, "", $"Registro ya consolidado para {monthInfo.PeriodLabel}."));
                continue;
            }

            if (IsRenewalContractKind(context.Record.ContractKindOptionValue))
            {
                skippedCount++;
                logs.Add(BuildMonthCloseLog("info", context.Record.RecordId, context.Record.ClientName, "", "Registro marcado como renovacion; no se envia a sales performance/productos cloud."));
                continue;
            }

            ScenarioStoredDto? scenario = null;
            if (!string.IsNullOrWhiteSpace(context.Record.BusinessId))
            {
                if (!scenarioCache.TryGetValue(context.Record.BusinessId, out scenario))
                {
                    scenario = await GetScenarioByBusinessIdAsync(context.Record.BusinessId, httpContext.User, ct);
                    scenarioCache[context.Record.BusinessId] = scenario;
                }
            }

            var detail = BuildScoreVerificationDetail(context, scenario);
            if (detail.Lines.Count == 0)
            {
                errorCount++;
                logs.Add(BuildMonthCloseLog("error", context.Record.RecordId, context.Record.ClientName, "", "El registro no tiene lineas para consolidar."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(context.Record.ClientId))
            {
                errorCount++;
                logs.Add(BuildMonthCloseLog("error", context.Record.RecordId, context.Record.ClientName, "", "El registro no tiene cliente lookup valido."));
                continue;
            }

            processedRecordsCount++;
            var recordSucceeded = true;
            foreach (var line in detail.Lines)
            {
                if (string.IsNullOrWhiteSpace(line.ProductId))
                {
                    recordSucceeded = false;
                    errorCount++;
                    logs.Add(BuildMonthCloseLog("error", context.Record.RecordId, context.Record.ClientName, line.ProductName, "Debes seleccionar un producto valido desde el buscador antes de cerrar el mes."));
                    continue;
                }

                if (!salesPerformanceCache.TryGetValue(context.Record.ClientId, out var clientRecords))
                {
                    clientRecords = await GetSalesPerformanceRecordsByClientAsync(context.Record.ClientId, httpContext.User, ct);
                    salesPerformanceCache[context.Record.ClientId] = clientRecords;
                }

                var existingMatch = clientRecords
                    .FirstOrDefault(item => string.Equals(item.ProductId, line.ProductId, StringComparison.OrdinalIgnoreCase));

                try
                {
                    if (existingMatch is not null && !string.IsNullOrWhiteSpace(existingMatch.RecordId))
                    {
                        var newQuantity = existingMatch.Quantity + Math.Max(line.Quantity, 0);
                        await UpdateSalesPerformanceQuantityAsync(existingMatch.RecordId, newQuantity, httpContext.User, ct);
                        existingMatch.Quantity = newQuantity;
                        updatedCount++;
                        logs.Add(BuildMonthCloseLog("success", context.Record.RecordId, context.Record.ClientName, line.ProductName, $"Se incremento la cantidad del producto a {newQuantity}."));
                        continue;
                    }

                    await CreateSalesPerformanceRecordAsync(context.Record, detail, line, httpContext.User, ct);
                    createdCount++;
                    logs.Add(BuildMonthCloseLog("success", context.Record.RecordId, context.Record.ClientName, line.ProductName, "Se creo una nueva linea en cr07a_salesperformancerecord."));
                    salesPerformanceCache[context.Record.ClientId] = await GetSalesPerformanceRecordsByClientAsync(context.Record.ClientId, httpContext.User, ct);
                }
                catch (InvalidOperationException ex)
                {
                    recordSucceeded = false;
                    errorCount++;
                    var errorDetail = BuildMonthCloseErrorDetail(ex);
                    logs.Add(BuildMonthCloseLog(
                        "error",
                        context.Record.RecordId,
                        context.Record.ClientName,
                        line.ProductName,
                        CompactMonthCloseError(errorDetail),
                        errorDetail));
                }
            }

            if (!recordSucceeded)
                continue;

            MarkRecordAsClosed(context.Additional, monthInfo.PeriodKey, currentUser);
            await UpdateScoreAdditionalDataAsync(context.Record.RecordId, context.Additional, httpContext.User, ct);
        }

        var hasErrors = errorCount > 0;
        var message = hasErrors
            ? $"Cierre ejecutado con novedades para {monthInfo.PeriodLabel}. Creados: {createdCount}. Actualizados: {updatedCount}. Omitidos: {skippedCount}. Errores: {errorCount}."
            : $"Mes cerrado correctamente para {monthInfo.PeriodLabel}. Creados: {createdCount}. Actualizados: {updatedCount}. Omitidos: {skippedCount}.";

        return new ScoreMonthCloseResultDto
        {
            HasErrors = hasErrors,
            Message = message,
            PeriodKey = monthInfo.PeriodKey,
            PeriodLabel = monthInfo.PeriodLabel,
            ProcessedRecordsCount = processedRecordsCount,
            CreatedCount = createdCount,
            UpdatedCount = updatedCount,
            SkippedCount = skippedCount,
            ErrorCount = errorCount,
            Logs = logs
        };
    }

    public async Task<ScoreMonthClosePreviewResultDto> PreviewScoreMonthCloseAsync(ScorePeriodFilter filter, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var plan = await BuildScoreMonthClosePlanAsync(filter, httpContext.User, ct);
        return BuildMonthClosePreviewResult(plan);
    }

    public async Task<ScoreMonthCloseResultDto> CloseScoreMonthAsync(ScoreMonthCloseRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Confirmed)
            throw new InvalidOperationException("Debes confirmar el cierre desde la ventana de revision antes de enviarlo.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var filter = ScorePeriodFilterExtensions.ParseOrDefault(request.Filter);
        var plan = await BuildScoreMonthClosePlanAsync(filter, httpContext.User, ct);
        var currentUser = await GetCurrentUserAsync(ct) ?? new Models.CurrentUserInfo();
        var decisions = (request.Decisions ?? new List<ScoreMonthCloseLineDecisionDto>())
            .Where(item => !string.IsNullOrWhiteSpace(item.LineKey))
            .GroupBy(item => item.LineKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Include, StringComparer.OrdinalIgnoreCase);

        var logs = new List<ScoreMonthCloseLogEntryDto>();
        var batchId = Guid.NewGuid().ToString("N");
        var createdCount = 0;
        var updatedCount = 0;
        var skippedCount = 0;
        var selectedCount = 0;
        var excludedCount = 0;
        var warningCount = 0;
        var errorCount = 0;

        foreach (var recordPlan in plan.Records)
        {
            if (recordPlan.Lines.Count == 0)
            {
                errorCount++;
                logs.Add(BuildMonthCloseLog(
                    "error",
                    "error",
                    "",
                    recordPlan.Record.RecordId,
                    recordPlan.Record.ClientName,
                    "",
                    "El registro no tiene lineas para consolidar.",
                    ""));
                continue;
            }

            var appliedSnapshots = new List<ScoreMonthlyClosureLineSnapshot>();

            foreach (var linePlan in recordPlan.Lines)
            {
                if (linePlan.IsAlreadyClosed)
                {
                    skippedCount++;
                    logs.Add(BuildMonthCloseLog(
                        "info",
                        "already-closed",
                        linePlan.LineKey,
                        linePlan.Record.RecordId,
                        linePlan.Record.ClientName,
                        linePlan.Line.ProductName,
                        $"La linea ya estaba consolidada en {plan.MonthInfo.PeriodLabel}.",
                        BuildPreviewLineState(linePlan)));
                    continue;
                }

                var include = linePlan.SelectedByDefault;
                if (linePlan.CanChangeSelection && decisions.TryGetValue(linePlan.LineKey, out var overrideInclude))
                    include = overrideInclude;

                if (!include)
                {
                    excludedCount++;
                    appliedSnapshots.Add(BuildClosureLineSnapshot(
                        linePlan,
                        status: "excluded",
                        salesPerformanceRecordId: "",
                        previousQuantity: 0,
                        appliedQuantity: 0,
                        finalQuantity: 0,
                        warnings: Array.Empty<string>()));
                    logs.Add(BuildMonthCloseLog(
                        "info",
                        "excluded",
                        linePlan.LineKey,
                        linePlan.Record.RecordId,
                        linePlan.Record.ClientName,
                        linePlan.Line.ProductName,
                        string.IsNullOrWhiteSpace(linePlan.Reason)
                            ? "La linea se excluyo del cierre por decision del usuario."
                            : linePlan.Reason,
                        BuildPreviewLineState(linePlan)));
                    continue;
                }

                selectedCount++;

                try
                {
                    if (string.IsNullOrWhiteSpace(linePlan.Record.ClientId))
                        throw new InvalidOperationException("El registro no tiene cliente lookup valido.");

                    if (string.IsNullOrWhiteSpace(linePlan.Line.ProductId))
                        throw new InvalidOperationException("Debes seleccionar un producto valido desde el buscador antes de cerrar el mes.");

                    if (!plan.SalesPerformanceCache.TryGetValue(linePlan.Record.ClientId, out var clientRecords))
                    {
                        clientRecords = await GetSalesPerformanceRecordsByClientAsync(linePlan.Record.ClientId, httpContext.User, ct);
                        plan.SalesPerformanceCache[linePlan.Record.ClientId] = clientRecords;
                    }

                    var currentMatch = clientRecords
                        .FirstOrDefault(item => string.Equals(item.ProductId, linePlan.Line.ProductId, StringComparison.OrdinalIgnoreCase));

                    if (currentMatch is not null && !string.IsNullOrWhiteSpace(currentMatch.RecordId))
                    {
                        var previousQuantity = Math.Max(currentMatch.Quantity, 0);
                        var appliedQuantity = Math.Max(linePlan.Line.Quantity, 0);
                        var newQuantity = previousQuantity + appliedQuantity;

                        await UpdateSalesPerformanceQuantityAsync(currentMatch.RecordId, newQuantity, httpContext.User, ct);
                        currentMatch.Quantity = newQuantity;
                        updatedCount++;

                        logs.Add(BuildMonthCloseLog(
                            "success",
                            "increment",
                            linePlan.LineKey,
                            linePlan.Record.RecordId,
                            linePlan.Record.ClientName,
                            linePlan.Line.ProductName,
                            $"Cantidad incrementada: {previousQuantity} + {appliedQuantity} = {newQuantity}.",
                            BuildUpdatedLineState(linePlan, previousQuantity, appliedQuantity, newQuantity)));
                        appliedSnapshots.Add(BuildClosureLineSnapshot(
                            linePlan,
                            status: "updated",
                            salesPerformanceRecordId: currentMatch.RecordId,
                            previousQuantity: previousQuantity,
                            appliedQuantity: appliedQuantity,
                            finalQuantity: newQuantity,
                            warnings: Array.Empty<string>()));
                        continue;
                    }

                    var createResult = await CreateSalesPerformanceRecordAsync(linePlan.Record, linePlan.Detail, linePlan.Line, httpContext.User, ct);
                    var refreshedClientRecords = await GetSalesPerformanceRecordsByClientAsync(linePlan.Record.ClientId, httpContext.User, ct);
                    plan.SalesPerformanceCache[linePlan.Record.ClientId] = refreshedClientRecords;
                    var createdRecordId = refreshedClientRecords
                        .FirstOrDefault(item => string.Equals(item.ProductId, linePlan.Line.ProductId, StringComparison.OrdinalIgnoreCase))
                        ?.RecordId
                        ?? "";
                    createdCount++;

                    if (createResult.Warnings.Count > 0)
                        warningCount++;

                    logs.Add(BuildMonthCloseLog(
                        createResult.Warnings.Count > 0 ? "warning" : "success",
                        "create",
                        linePlan.LineKey,
                        linePlan.Record.RecordId,
                        linePlan.Record.ClientName,
                        linePlan.Line.ProductName,
                        createResult.Warnings.Count > 0
                            ? $"Se creo la linea, pero queda revision manual pendiente: {string.Join(" ", createResult.Warnings)}"
                            : "Se creo una nueva linea en cr07a_salesperformancerecord.",
                        createResult.FinalState));
                    appliedSnapshots.Add(BuildClosureLineSnapshot(
                        linePlan,
                        status: "created",
                        salesPerformanceRecordId: createdRecordId,
                        previousQuantity: 0,
                        appliedQuantity: Math.Max(linePlan.Line.Quantity, 0),
                        finalQuantity: Math.Max(linePlan.Line.Quantity, 0),
                        warnings: createResult.Warnings));
                }
                catch (InvalidOperationException ex)
                {
                    errorCount++;
                    var errorDetail = BuildMonthCloseErrorDetail(ex);
                    logs.Add(BuildMonthCloseLog(
                        "error",
                        "error",
                        linePlan.LineKey,
                        linePlan.Record.RecordId,
                        linePlan.Record.ClientName,
                        linePlan.Line.ProductName,
                        CompactMonthCloseError(errorDetail),
                        "",
                        errorDetail));
                }
            }

            if (appliedSnapshots.Count == 0)
                continue;

            AppendMonthlyClosure(recordPlan.Context.Additional, plan.MonthInfo.PeriodKey, batchId, currentUser, appliedSnapshots);
            await UpdateScoreAdditionalDataAsync(recordPlan.Record.RecordId, recordPlan.Context.Additional, httpContext.User, ct);
        }

        var hasErrors = errorCount > 0;
        var hasWarnings = warningCount > 0;
        var message = hasErrors
            ? $"Cierre ejecutado con novedades para {plan.MonthInfo.PeriodLabel}. Nuevas: {createdCount}. Incrementos: {updatedCount}. Excluidas: {excludedCount}. Ya cerradas: {skippedCount}. Errores: {errorCount}."
            : hasWarnings
                ? $"Cierre ejecutado para {plan.MonthInfo.PeriodLabel} con revision manual pendiente. Nuevas: {createdCount}. Incrementos: {updatedCount}. Excluidas: {excludedCount}. Ya cerradas: {skippedCount}."
                : $"Cierre ejecutado correctamente para {plan.MonthInfo.PeriodLabel}. Nuevas: {createdCount}. Incrementos: {updatedCount}. Excluidas: {excludedCount}. Ya cerradas: {skippedCount}.";

        return new ScoreMonthCloseResultDto
        {
            HasErrors = hasErrors,
            HasWarnings = hasWarnings,
            Message = message,
            PeriodKey = plan.MonthInfo.PeriodKey,
            PeriodLabel = plan.MonthInfo.PeriodLabel,
            BatchId = batchId,
            CanUndo = createdCount > 0 || updatedCount > 0 || excludedCount > 0,
            ProcessedRecordsCount = plan.Records.Count,
            SelectedCount = selectedCount,
            ExcludedCount = excludedCount,
            CreatedCount = createdCount,
            UpdatedCount = updatedCount,
            SkippedCount = skippedCount,
            WarningCount = warningCount,
            ErrorCount = errorCount,
            Logs = logs
        };
    }

    public async Task<ScoreMonthUndoResultDto> UndoScoreMonthCloseAsync(ScorePeriodFilter filter, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var plan = await BuildScoreMonthClosePlanAsync(filter, httpContext.User, ct);
        var latestBatch = ResolveLatestMonthCloseBatch(plan.Records.Select(item => item.Context), plan.MonthInfo.PeriodKey)
            ?? throw new InvalidOperationException($"No hay un cierre reciente de {plan.MonthInfo.PeriodLabel} para deshacer.");

        var revertedCreatedCount = 0;
        var revertedUpdatedCount = 0;
        var restoredExcludedCount = 0;
        var errorCount = 0;
        var logs = new List<ScoreMonthCloseLogEntryDto>();

        foreach (var recordPlan in plan.Records)
        {
            var batchSnapshot = recordPlan.Context.Additional.MonthlyClosures
                .FirstOrDefault(item =>
                    string.Equals(item.PeriodKey, plan.MonthInfo.PeriodKey, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.BatchId, latestBatch.BatchId, StringComparison.OrdinalIgnoreCase));
            if (batchSnapshot is null || batchSnapshot.Lines.Count == 0)
                continue;

            var revertedLineKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var lineSnapshot in batchSnapshot.Lines)
            {
                try
                {
                    switch (lineSnapshot.Status)
                    {
                        case "created":
                            if (!string.IsNullOrWhiteSpace(lineSnapshot.SalesPerformanceRecordId))
                                await DeleteSalesPerformanceRecordAsync(lineSnapshot.SalesPerformanceRecordId, httpContext.User, ct);

                            revertedCreatedCount++;
                            revertedLineKeys.Add(lineSnapshot.LineKey);
                            logs.Add(BuildMonthCloseLog(
                                "success",
                                "undo-create",
                                lineSnapshot.LineKey,
                                recordPlan.Record.RecordId,
                                recordPlan.Record.ClientName,
                                lineSnapshot.ProductName,
                                "Se elimino la linea creada por el ultimo cierre.",
                                ""));
                            break;

                        case "updated":
                            if (string.IsNullOrWhiteSpace(lineSnapshot.SalesPerformanceRecordId))
                                throw new InvalidOperationException("No se encontro el identificador del registro incrementado para revertirlo.");

                            await UpdateSalesPerformanceQuantityAsync(
                                lineSnapshot.SalesPerformanceRecordId,
                                Math.Max(lineSnapshot.PreviousQuantity, 0),
                                httpContext.User,
                                ct);

                            revertedUpdatedCount++;
                            revertedLineKeys.Add(lineSnapshot.LineKey);
                            logs.Add(BuildMonthCloseLog(
                                "success",
                                "undo-increment",
                                lineSnapshot.LineKey,
                                recordPlan.Record.RecordId,
                                recordPlan.Record.ClientName,
                                lineSnapshot.ProductName,
                                $"Se restauro la cantidad anterior a {Math.Max(lineSnapshot.PreviousQuantity, 0)}.",
                                ""));
                            break;

                        case "excluded":
                            restoredExcludedCount++;
                            revertedLineKeys.Add(lineSnapshot.LineKey);
                            logs.Add(BuildMonthCloseLog(
                                "info",
                                "undo-excluded",
                                lineSnapshot.LineKey,
                                recordPlan.Record.RecordId,
                                recordPlan.Record.ClientName,
                                lineSnapshot.ProductName,
                                "La linea vuelve a quedar disponible para un nuevo cierre.",
                                ""));
                            break;
                    }
                }
                catch (InvalidOperationException ex)
                {
                    errorCount++;
                    var errorDetail = BuildMonthCloseErrorDetail(ex);
                    logs.Add(BuildMonthCloseLog(
                        "error",
                        "undo-error",
                        lineSnapshot.LineKey,
                        recordPlan.Record.RecordId,
                        recordPlan.Record.ClientName,
                        lineSnapshot.ProductName,
                        CompactMonthCloseError(errorDetail),
                        "",
                        errorDetail));
                }
            }

            if (revertedLineKeys.Count == 0)
                continue;

            RemoveMonthlyClosureLines(recordPlan.Context.Additional, plan.MonthInfo.PeriodKey, latestBatch.BatchId, revertedLineKeys);
            await UpdateScoreAdditionalDataAsync(recordPlan.Record.RecordId, recordPlan.Context.Additional, httpContext.User, ct);
        }

        var hasErrors = errorCount > 0;
        var message = hasErrors
            ? $"Se revirtio parcialmente el ultimo cierre de {plan.MonthInfo.PeriodLabel}. Eliminadas: {revertedCreatedCount}. Cantidades restauradas: {revertedUpdatedCount}. Exclusiones liberadas: {restoredExcludedCount}. Errores: {errorCount}."
            : $"Se deshizo el ultimo cierre de {plan.MonthInfo.PeriodLabel}. Eliminadas: {revertedCreatedCount}. Cantidades restauradas: {revertedUpdatedCount}. Exclusiones liberadas: {restoredExcludedCount}.";

        return new ScoreMonthUndoResultDto
        {
            HasErrors = hasErrors,
            Message = message,
            PeriodKey = plan.MonthInfo.PeriodKey,
            PeriodLabel = plan.MonthInfo.PeriodLabel,
            BatchId = latestBatch.BatchId,
            RevertedCreatedCount = revertedCreatedCount,
            RevertedUpdatedCount = revertedUpdatedCount,
            RestoredExcludedCount = restoredExcludedCount,
            ErrorCount = errorCount,
            Logs = logs
        };
    }

    private async Task<ScoreMonthClosePlan> BuildScoreMonthClosePlanAsync(
        ScorePeriodFilter filter,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        var monthInfo = GetScoreMonthInfo(filter);
        if (!monthInfo.SupportsClose || string.IsNullOrWhiteSpace(monthInfo.PeriodKey))
            throw new InvalidOperationException("El cierre de mes solo esta disponible en vistas mensuales.");

        var filterParts = new List<string>
        {
            $"{_scoresContractStartDateField} ne null",
            BuildScorePeriodFilter(filter)
        };

        var relativeUrl = $"/api/data/v9.2/{_scoresTableSetName}?$filter={Uri.EscapeDataString(string.Join(" and ", filterParts.Where(part => !string.IsNullOrWhiteSpace(part))))}&$orderby={_scoresContractStartDateField} asc";
        var rawRecords = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        var contexts = rawRecords
            .Select(item => ParseScoreRecordContext(item, monthInfo.PeriodKey))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.Record.ContractStartDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Record.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (contexts.Count == 0)
            throw new InvalidOperationException("No hay registros para cerrar en el periodo seleccionado.");

        var unverifiedCount = contexts.Count(item => !item.Record.IsVerified);
        if (unverifiedCount > 0)
            throw new InvalidOperationException($"No puedes cerrar el mes hasta verificar las {unverifiedCount} lineas pendientes.");

        var scenarioCache = new Dictionary<string, ScenarioStoredDto?>(StringComparer.OrdinalIgnoreCase);
        var salesPerformanceCache = new Dictionary<string, List<SalesPerformanceCompactRecord>>(StringComparer.OrdinalIgnoreCase);
        var recordPlans = new List<ScoreMonthCloseRecordPlan>();

        foreach (var context in contexts)
        {
            ScenarioStoredDto? scenario = null;
            if (!string.IsNullOrWhiteSpace(context.Record.BusinessId))
            {
                if (!scenarioCache.TryGetValue(context.Record.BusinessId, out scenario))
                {
                    scenario = await GetScenarioByBusinessIdAsync(context.Record.BusinessId, user, ct);
                    scenarioCache[context.Record.BusinessId] = scenario;
                }
            }

            var detail = BuildScoreVerificationDetail(context, scenario);
            var linePlans = new List<ScoreMonthCloseLinePlan>();

            foreach (var (line, index) in detail.Lines.Select((value, index) => (value, index)))
            {
                var lineKey = BuildMonthCloseLineKey(context.Record.RecordId, line.LineId, index + 1);
                var existingSnapshot = ResolveMonthlyClosureLine(context.Additional, monthInfo.PeriodKey, lineKey);
                var isAlreadyClosed = existingSnapshot is not null;
                var isRenewalContract = IsRenewalContractKind(context.Record.ContractKindOptionValue);
                var selectedByDefault = !isAlreadyClosed && !isRenewalContract && detail.AutoBillOptionValue == 1;

                List<SalesPerformanceCompactRecord> clientRecords = new();
                if (!string.IsNullOrWhiteSpace(context.Record.ClientId))
                {
                    if (!salesPerformanceCache.TryGetValue(context.Record.ClientId, out var cachedClientRecords))
                    {
                        clientRecords = await GetSalesPerformanceRecordsByClientAsync(context.Record.ClientId, user, ct);
                        salesPerformanceCache[context.Record.ClientId] = clientRecords;
                    }
                    else
                    {
                        clientRecords = cachedClientRecords;
                    }
                }

                var existingMatch = string.IsNullOrWhiteSpace(line.ProductId)
                    ? null
                    : clientRecords.FirstOrDefault(item => string.Equals(item.ProductId, line.ProductId, StringComparison.OrdinalIgnoreCase));

                linePlans.Add(new ScoreMonthCloseLinePlan
                {
                    Context = context,
                    Record = context.Record,
                    Detail = detail,
                    Line = line,
                    LineKey = lineKey,
                    ExistingMatch = existingMatch,
                    ExistingClosure = existingSnapshot,
                    IsAlreadyClosed = isAlreadyClosed,
                    IsRenewalContract = isRenewalContract,
                    SelectedByDefault = selectedByDefault,
                    CanChangeSelection = !isAlreadyClosed && !isRenewalContract,
                    Reason = ResolveMonthCloseLineReason(detail.AutoBillOptionValue, isAlreadyClosed, isRenewalContract),
                    PredictedAction = isRenewalContract ? "skip-renewal" : existingMatch is not null ? "increment" : "create",
                    Warnings = isRenewalContract ? new List<string>() : BuildMonthCloseWarnings(context.Record, detail, line)
                });
            }

            recordPlans.Add(new ScoreMonthCloseRecordPlan
            {
                Context = context,
                Record = context.Record,
                Detail = detail,
                Lines = linePlans
            });
        }

        return new ScoreMonthClosePlan
        {
            MonthInfo = monthInfo,
            Records = recordPlans,
            SalesPerformanceCache = salesPerformanceCache
        };
    }

    private ScoreMonthClosePreviewResultDto BuildMonthClosePreviewResult(ScoreMonthClosePlan plan)
    {
        var lines = plan.Records
            .SelectMany(record => record.Lines)
            .Select(linePlan => new ScoreMonthClosePreviewLineDto
            {
                LineKey = linePlan.LineKey,
                RecordId = linePlan.Record.RecordId,
                LineId = linePlan.Line.LineId,
                ClientName = linePlan.Record.ClientName,
                ProductName = linePlan.Line.ProductName,
                ProductId = linePlan.Line.ProductId,
                Quantity = Math.Max(linePlan.Line.Quantity, 0),
                UnitSaleUsd = linePlan.Line.SaleUnit,
                AutoBillOptionValue = linePlan.Detail.AutoBillOptionValue,
                BillingDay = ResolveSalesPerformanceBillingDay(linePlan.Detail),
                ProductLineOptionValue = ResolveSalesPerformanceProductLineOptionValue(linePlan.Detail, linePlan.Line),
                ContractTypeOptionValue = linePlan.Detail.ContractTypeOptionValue,
                ContractKindOptionValue = linePlan.Record.ContractKindOptionValue,
                ContractKindLabel = linePlan.Record.ContractKindLabel,
                HasVatOptionValue = ResolveSalesPerformanceHasVatOptionValue(linePlan.Line),
                RenewalDateValue = ResolveSalesPerformanceRenewalDate(linePlan.Detail),
                RenewalDateDisplay = FormatDateDisplay(ResolveSalesPerformanceRenewalDate(linePlan.Detail)),
                SelectedByDefault = linePlan.SelectedByDefault,
                CanChangeSelection = linePlan.CanChangeSelection,
                Reason = linePlan.Reason,
                PredictedAction = linePlan.PredictedAction,
                ExistingQuantity = Math.Max(linePlan.ExistingMatch?.Quantity ?? 0, 0),
                FinalQuantity = linePlan.ExistingMatch is null
                    ? Math.Max(linePlan.Line.Quantity, 0)
                    : Math.Max(linePlan.ExistingMatch.Quantity, 0) + Math.Max(linePlan.Line.Quantity, 0),
                RequiresManualReview = linePlan.Warnings.Count > 0,
                Warnings = linePlan.Warnings
            })
            .ToList();
        var selectedCount = lines.Count(item => item.SelectedByDefault);
        var warningCount = lines.Count(item => item.RequiresManualReview);
        var latestBatch = ResolveLatestMonthCloseBatch(plan.Records.Select(item => item.Context), plan.MonthInfo.PeriodKey);

        return new ScoreMonthClosePreviewResultDto
        {
            Message = selectedCount > 0
                ? $"{plan.MonthInfo.PeriodLabel}: revisa las lineas antes de enviarlas a sales performance."
                : $"{plan.MonthInfo.PeriodLabel}: no hay lineas seleccionadas por defecto. Puedes ajustar la seleccion antes de confirmar.",
            PeriodKey = plan.MonthInfo.PeriodKey,
            PeriodLabel = plan.MonthInfo.PeriodLabel,
            TotalLines = lines.Count,
            SelectedCount = selectedCount,
            ExcludedCount = lines.Count - selectedCount,
            WarningCount = warningCount,
            CanUndo = latestBatch is not null,
            Lines = lines
        };
    }

    public async Task<ScoreOfferDownloadResult?> DownloadScoreOfferAsync(string recordId, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var metadataUrl = $"/api/data/v9.2/{_scoresTableSetName}({normalizedRecordId})?$select={_scoresOfferField}";
        var metadataJson = await CallDataverseGetJsonAsync(metadataUrl, httpContext.User, ct, AddFormattedValueHeaders);

        using var metadataDocument = JsonDocument.Parse(metadataJson);
        var metadata = metadataDocument.RootElement;
        var offerValue = ReadString(metadata, _scoresOfferField).Trim();
        var offerDisplay = ReadString(metadata, $"{_scoresOfferField}{FormattedValueAnnotationSuffix}").Trim();
        var fileName = string.IsNullOrWhiteSpace(offerDisplay) ? offerValue : offerDisplay;

        if (string.IsNullOrWhiteSpace(fileName) && string.IsNullOrWhiteSpace(offerValue))
            return null;

        if (Uri.TryCreate(offerValue, UriKind.Absolute, out var absoluteOfferUrl)
            && (string.Equals(absoluteOfferUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || string.Equals(absoluteOfferUrl.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
        {
            return new ScoreOfferDownloadResult
            {
                RedirectUrl = absoluteOfferUrl.ToString(),
                FileName = string.IsNullOrWhiteSpace(fileName)
                    ? Path.GetFileName(absoluteOfferUrl.LocalPath)
                    : fileName
            };
        }

        var relativeFileUrl = $"/api/data/v9.2/{_scoresTableSetName}({normalizedRecordId})/{_scoresOfferField}/$value";
        var result = await _downstreamApi.CallApiForUserAsync(
            serviceName: "Dataverse",
            options =>
            {
                options.RelativePath = relativeFileUrl;
                options.HttpMethod = "GET";
            },
            user: httpContext.User,
            cancellationToken: ct);

        if (result is not HttpResponseMessage response)
            throw new InvalidOperationException($"Unexpected downstream response type: {result?.GetType().FullName ?? "null"}");

        await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        using var memoryStream = new MemoryStream();
        await responseStream.CopyToAsync(memoryStream, ct);
        var bodyBytes = memoryStream.ToArray();

        if (!response.IsSuccessStatusCode)
        {
            var bodyText = bodyBytes.Length > 0
                ? Encoding.UTF8.GetString(bodyBytes)
                : "";
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {bodyText}");
        }

        return new ScoreOfferDownloadResult
        {
            Content = bodyBytes,
            ContentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
            FileName = ResolveOfferDownloadFileName(response, fileName, normalizedRecordId)
        };
    }

    private string BuildScorePeriodFilter(ScorePeriodFilter filter)
    {
        var today = GetBogotaToday();
        if (filter == ScorePeriodFilter.ThisYear)
        {
            var yearStart = new DateOnly(today.Year, 1, 1);
            var nextYearStart = yearStart.AddYears(1);
            return $"{_scoresContractStartDateField} ge {yearStart:yyyy-MM-dd} and {_scoresContractStartDateField} lt {nextYearStart:yyyy-MM-dd}";
        }

        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var targetMonthStart = filter switch
        {
            ScorePeriodFilter.PreviousMonth => monthStart.AddMonths(-1),
            ScorePeriodFilter.NextMonth => monthStart.AddMonths(1),
            _ => monthStart
        };

        var nextMonthStart = targetMonthStart.AddMonths(1);
        return $"{_scoresContractStartDateField} ge {targetMonthStart:yyyy-MM-dd} and {_scoresContractStartDateField} lt {nextMonthStart:yyyy-MM-dd}";
    }

    private ScoreMonthInfo GetScoreMonthInfo(ScorePeriodFilter filter)
    {
        if (filter == ScorePeriodFilter.ThisYear)
            return new ScoreMonthInfo(false, "", "");

        var today = GetBogotaToday();
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var targetMonthStart = filter switch
        {
            ScorePeriodFilter.PreviousMonth => monthStart.AddMonths(-1),
            ScorePeriodFilter.NextMonth => monthStart.AddMonths(1),
            _ => monthStart
        };

        var culture = CultureInfo.GetCultureInfo("es-CO");
        var periodLabel = culture.TextInfo.ToTitleCase(targetMonthStart.ToString("MMMM yyyy", culture));
        return new ScoreMonthInfo(true, $"{targetMonthStart:yyyy-MM}", periodLabel);
    }

    private async Task<JsonElement> GetScoreRecordJsonAsync(string recordId, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct)
    {
        var relativeUrl = $"/api/data/v9.2/{_scoresTableSetName}({recordId})";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private async Task<ScenarioStoredDto?> GetScenarioByBusinessIdAsync(string businessId, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(businessId))
            return null;

        var select = string.Join(",", new[]
        {
            "cr07a_scenarioid",
            "cr07a_scenarioname",
            "cr07a_dealtype",
            "cr07a_requiresproration",
            "cr07a_startdate",
            "cr07a_enddate",
            "cr07a_linesjson",
            "cr07a_lastresultjson"
        });
        var filter = $"cr07a_scenarioid eq '{EscapeOdataLiteral(businessId.Trim())}'";
        var relativeUrl = $"/api/data/v9.2/{_scenariosTableSetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=1";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);

        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("value");
        if (arr.GetArrayLength() == 0)
            return null;

        var item = arr[0];
        var linesJson = item.TryGetProperty("cr07a_linesjson", out var linesProp)
            ? linesProp.GetString()
            : null;
        var resultJson = item.TryGetProperty("cr07a_lastresultjson", out var resultProp)
            ? resultProp.GetString()
            : null;

        return new ScenarioStoredDto
        {
            ScenarioId = item.TryGetProperty("cr07a_scenarioid", out var idProp) ? (idProp.GetString() ?? "") : "",
            ScenarioName = item.TryGetProperty("cr07a_scenarioname", out var nameProp) ? (nameProp.GetString() ?? "") : "",
            DealType = ReadInt(item, "cr07a_dealtype"),
            RequiresProration = ReadBool(item, "cr07a_requiresproration"),
            StartDate = item.TryGetProperty("cr07a_startdate", out var startProp) ? startProp.GetString() : null,
            EndDate = item.TryGetProperty("cr07a_enddate", out var endProp) ? endProp.GetString() : null,
            Lines = DeserializeJsonOrDefault<List<ScenarioLineInput>>(linesJson) ?? new List<ScenarioLineInput>(),
            LastResult = DeserializeJsonOrDefault<ScenarioResultSnapshot>(resultJson)
        };
    }

    private ScoreRecordContext? ParseScoreRecordContext(JsonElement item, string? activePeriodKey)
    {
        var recordId = ReadString(item, _scoresIdField);
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var contractStartDate = ReadDateOnly(item, _scoresContractStartDateField);
        if (!contractStartDate.HasValue)
            return null;

        var rawDescription = FirstNonEmpty(
            ReadString(item, _scoresDescriptionField),
            ReadString(item, _scoresLegacyDescriptionField));
        var parsedDescription = ParseScoreDescription(rawDescription);
        var rawAdditional = ReadString(item, _scoresAdditionalField);
        var additional = DeserializeJsonOrDefault<ScoreAdditionalDataSnapshot>(rawAdditional) ?? new ScoreAdditionalDataSnapshot();
        if (!string.IsNullOrWhiteSpace(rawAdditional))
            additional.Version = Math.Max(additional.Version, 1);
        NormalizeAdditionalSnapshot(additional);

        var productLines = ResolveScoreProductLines(additional, parsedDescription)
            .OrderBy(line => line.ProductName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(line => line.LineId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var clientId = ReadDataverseLookupId(item, _scoresClientField, "cliente");
        var clientName = ReadDataverseDisplayValue(item, _scoresClientField, "cliente");
        clientName = string.IsNullOrWhiteSpace(clientName)
            ? parsedDescription.ClientName
            : clientName.Trim();
        clientName = string.IsNullOrWhiteSpace(clientName)
            ? "Cliente sin asignar"
            : clientName;

        var score = RoundCurrency(ReadDecimal(item, _scoresScoreField) ?? additional.LastResult?.Points ?? parsedDescription.Score ?? 0m);
        var commission = RoundCurrency(ReadDecimal(item, _scoresCommissionField) ?? additional.LastResult?.Commission ?? parsedDescription.Commission ?? 0m);
        var salesPerson = ReadDataverseDisplayValue(item, _scoresSalesPersonField, "vendedor");
        var ownerId = ReadDataverseLookupId(item, "ownerid", "owner", "propietario");
        var ownerName = FirstNonEmpty(ReadDataverseDisplayValue(item, "ownerid", "owner", "propietario"), "Sin propietario");
        var offer = ReadDataverseDisplayValue(item, _scoresOfferField, "oferta");
        var isVerified = ReadYesNoOptionFlexible(item, _scoresVerifiedField);
        var monthlyValue = RoundCurrency(additional.LastResult?.TotalMonthlySale ?? parsedDescription.TotalMonthlyValue ?? productLines.Sum(line => line.MonthlyValue));
        var totalValue = RoundCurrency(additional.LastResult?.TotalSale ?? parsedDescription.TotalValue ?? productLines.Sum(line => line.TotalValue));
        var renewalDate = ParseAdditionalDateOnly(additional.RenewalDateValue);
        var alignmentDate = ParseAdditionalDateOnly(additional.AlignmentDateValue);
        var lastClosure = ResolveLastClosure(additional);
        var dealTypeValue = ResolveStoredDealTypeValue(additional, parsedDescription, isVerified);
        var contractKindOptionValue = ResolveScoreContractKindOptionValue(
            ReadOptionValue(item, _scoresContractKindField),
            additional.ContractKindOptionValue,
            dealTypeValue);

        return new ScoreRecordContext
        {
            Record = new ScoreRecordDto
            {
                RecordId = recordId,
                ClientId = clientId,
                ClientName = clientName,
                OwnerId = ownerId,
                OwnerName = ownerName,
                ContractStartDateValue = contractStartDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ContractStartDateDisplay = contractStartDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                Score = score,
                Commission = commission,
                SalesPerson = string.IsNullOrWhiteSpace(salesPerson) ? "Sin vendedor" : salesPerson.Trim(),
                Offer = string.IsNullOrWhiteSpace(offer) ? "Sin oferta" : offer.Trim(),
                OfferFileName = offer.Trim(),
                HasOffer = !string.IsNullOrWhiteSpace(offer),
                IsVerified = isVerified,
                FirstContractOptionValue = ReadBinaryOptionValue(item, _scoresFirstContractField),
                LineOptionValue = ReadOptionValue(item, _scoresLineField),
                VerticalOptionValue = ReadOptionValue(item, _scoresVerticalField),
                DescriptionClientName = parsedDescription.ClientName,
                ProvisioningDateValue = parsedDescription.ProvisioningDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                ProvisioningDateDisplay = parsedDescription.ProvisioningDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
                ContractType = parsedDescription.ContractType,
                BusinessId = string.IsNullOrWhiteSpace(additional.BusinessId) ? parsedDescription.BusinessId : additional.BusinessId.Trim(),
                ProrationText = string.IsNullOrWhiteSpace(additional.LastResult?.ProrationText) ? parsedDescription.ProrationText : additional.LastResult!.ProrationText,
                ProrationDays = additional.LastResult?.ProrationDays ?? parsedDescription.ProrationDays,
                ProrationFactor = additional.LastResult?.ProrationFactor ?? parsedDescription.ProrationFactor,
                RawDescription = rawDescription,
                ProductLinesCount = productLines.Count,
                MonthlyValue = monthlyValue,
                TotalValue = totalValue,
                AnnualValue = totalValue,
                BillingDay = additional.BillingDay,
                RenewalDateValue = renewalDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                RenewalDateDisplay = renewalDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
                AlignmentDateValue = alignmentDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                AlignmentDateDisplay = alignmentDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
                HasVatOptionValue = additional.HasVatOptionValue > 0 ? additional.HasVatOptionValue : (productLines.FirstOrDefault()?.HasVatOptionValue ?? 0),
                AutoBillOptionValue = additional.AutoBillOptionValue,
                ProductLineOptionValue = additional.ProductLineOptionValue > 0 ? additional.ProductLineOptionValue : (productLines.FirstOrDefault()?.LineOptionValue ?? 0),
                ContractTypeOptionValue = additional.ContractTypeOptionValue,
                ContractKindOptionValue = contractKindOptionValue,
                ContractKindLabel = ResolveScoreContractKindLabel(contractKindOptionValue),
                DealTypeValue = dealTypeValue,
                RequiresProration = ResolveStoredRequiresProration(additional, parsedDescription),
                ScenarioStartDateValue = FirstNonEmpty(additional.ScenarioStartDateValue, parsedDescription.ScenarioStartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ScenarioEndDateValue = FirstNonEmpty(additional.ScenarioEndDateValue, parsedDescription.ScenarioEndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                IsClosedForActivePeriod = HasMonthlyClosure(
                    additional,
                    activePeriodKey,
                    productLines.Select((line, index) => BuildMonthCloseLineKey(recordId, line.LineId, index + 1))),
                ActivePeriodKey = activePeriodKey ?? "",
                LastVerifiedAtDisplay = FormatDateTimeDisplay(additional.VerifiedAt),
                LastVerifiedBy = additional.VerifiedBy ?? "",
                LastClosedAtDisplay = FormatDateTimeDisplay(lastClosure?.ClosedAt),
                LastClosedBy = lastClosure?.ClosedBy ?? additional.LastClosedBy ?? "",
                ProductLines = productLines
            },
            Additional = additional,
            Description = parsedDescription
        };
    }

    private ScoreRecordDto? ParseScoreRecord(JsonElement item) =>
        ParseScoreRecordContext(item, activePeriodKey: null)?.Record;

    private ScoreVerificationDetailDto BuildScoreVerificationDetail(ScoreRecordContext context, ScenarioStoredDto? scenario)
    {
        var record = context.Record;
        var additional = context.Additional;
        var lines = ResolveVerificationLines(record, additional, scenario);
        var detail = new ScoreVerificationDetailDto
        {
            RecordId = record.RecordId,
            BusinessId = !string.IsNullOrWhiteSpace(record.BusinessId) ? record.BusinessId : scenario?.ScenarioId ?? "",
            DealTypeValue = ResolveDealTypeValue(record, additional, scenario),
            RequiresProration = ResolveRequiresProration(record, additional, scenario),
            ScenarioStartDateValue = ResolveScenarioStartDateValue(record, additional, scenario),
            ScenarioEndDateValue = ResolveScenarioEndDateValue(record, additional, scenario),
            FirstContractOptionValue = record.FirstContractOptionValue > 0 ? record.FirstContractOptionValue : DeriveFirstContractOptionValue(ResolveDealTypeValue(record, additional, scenario)),
            LineOptionValue = record.LineOptionValue,
            VerticalOptionValue = record.VerticalOptionValue,
            BillingDay = record.BillingDay,
            RenewalDateValue = ResolveRenewalDateValue(record, additional, scenario),
            AlignmentDateValue = "",
            HasVatOptionValue = record.HasVatOptionValue,
            AutoBillOptionValue = record.IsVerified ? record.AutoBillOptionValue : (record.AutoBillOptionValue > 0 ? record.AutoBillOptionValue : -1),
            ProductLineOptionValue = record.ProductLineOptionValue,
            ContractTypeOptionValue = record.IsVerified ? record.ContractTypeOptionValue : (record.ContractTypeOptionValue > 0 ? record.ContractTypeOptionValue : -1),
            ContractKindOptionValue = ResolveScoreContractKindOptionValue(record.ContractKindOptionValue, additional.ContractKindOptionValue, ResolveDealTypeValue(record, additional, scenario)),
            Lines = lines,
            ClientId = record.ClientId,
            ClientName = record.ClientName,
            SalesPerson = record.SalesPerson,
            OwnerId = record.OwnerId,
            OwnerName = record.OwnerName,
            Offer = record.Offer,
            OfferFileName = record.OfferFileName,
            HasOffer = record.HasOffer,
            IsVerified = record.IsVerified,
            ContractStartDateValue = record.ContractStartDateValue,
            ContractStartDateDisplay = record.ContractStartDateDisplay,
            ProvisioningDateValue = record.ProvisioningDateValue,
            ProvisioningDateDisplay = record.ProvisioningDateDisplay,
            ContractTypeLabel = record.ContractType,
            ContractKindLabel = ResolveScoreContractKindLabel(ResolveScoreContractKindOptionValue(record.ContractKindOptionValue, additional.ContractKindOptionValue, ResolveDealTypeValue(record, additional, scenario))),
            ProrationSummary = record.ProrationText,
            IsClosedForActivePeriod = record.IsClosedForActivePeriod,
            ActivePeriodKey = record.ActivePeriodKey,
            LastVerifiedAtDisplay = record.LastVerifiedAtDisplay,
            LastVerifiedBy = record.LastVerifiedBy,
            LastClosedAtDisplay = record.LastClosedAtDisplay,
            LastClosedBy = record.LastClosedBy,
            Result = BuildStoredVerificationResult(record)
        };

        detail.BillingDay = ResolveBillingDayForRequest(detail.BillingDay, detail.AutoBillOptionValue, detail.RenewalDateValue, detail.ScenarioEndDateValue, detail.ContractStartDateValue);
        detail.RenewalMode = string.Equals(additional.RenewalMode, "ONETIME", StringComparison.OrdinalIgnoreCase)
            ? "ONETIME"
            : (string.IsNullOrWhiteSpace(detail.RenewalDateValue) && detail.Lines.FirstOrDefault()?.ContractMonths != 12 && !detail.RequiresProration
            ? "ONETIME"
            : "");
        detail.RenewalHint = detail.RenewalMode == "ONETIME"
            ? "ONETIME"
            : (!string.IsNullOrWhiteSpace(detail.RenewalDateValue) ? "" : "Se calculara segun el negocio.");

        try
        {
            var computation = BuildScoreComputationContext(detail, requireProductLookup: false);
            detail.Lines = computation.Lines;
            detail.Result = computation.Result;
            detail.DealTypeValue = computation.DealTypeValue;
            detail.RequiresProration = computation.RequiresProration;
            detail.ScenarioStartDateValue = computation.StartDateValue;
            detail.ScenarioEndDateValue = computation.EndDateValue;
            detail.RenewalDateValue = string.IsNullOrWhiteSpace(detail.RenewalDateValue)
                ? ResolveDefaultRenewalDateValue(detail.RequiresProration, detail.ScenarioEndDateValue, detail.ContractStartDateValue, detail.Lines)
                : detail.RenewalDateValue;
            detail.RenewalMode = string.IsNullOrWhiteSpace(detail.RenewalDateValue) && detail.Lines.FirstOrDefault()?.ContractMonths != 12 && !detail.RequiresProration
                ? "ONETIME"
                : "";
            detail.RenewalHint = detail.RenewalMode == "ONETIME" ? "ONETIME" : "";
            detail.BillingDay = ResolveBillingDayForRequest(detail.BillingDay, detail.AutoBillOptionValue, detail.RenewalDateValue, detail.ScenarioEndDateValue, detail.ContractStartDateValue);
        }
        catch (InvalidOperationException ex)
        {
            detail.WarningMessage = $"El registro tiene datos pendientes antes de recalcular. {ex.Message}";
        }

        return detail;
    }

    private static ScoreVerificationComputedResultDto BuildStoredVerificationResult(ScoreRecordDto record) =>
        new()
        {
            Points = record.Score,
            Commission = record.Commission,
            ProrationDays = record.ProrationDays,
            ProrationFactor = record.ProrationFactor == 0m ? 1m : record.ProrationFactor,
            ProrationText = string.IsNullOrWhiteSpace(record.ProrationText) ? "No" : record.ProrationText,
            TotalMonthlySale = record.MonthlyValue,
            TotalSale = record.TotalValue
        };

    private List<ScoreProductLineDto> ResolveScoreProductLines(ScoreAdditionalDataSnapshot additional, ScoreDescriptionParseResult parsedDescription)
    {
        if (additional.Lines.Count > 0)
        {
            return additional.Lines
                .Select((line, index) => ToScoreProductLine(line, index + 1))
                .ToList();
        }

        return parsedDescription.ProductLines;
    }

    private List<ScoreVerificationLineInput> ResolveVerificationLines(ScoreRecordDto record, ScoreAdditionalDataSnapshot additional, ScenarioStoredDto? scenario)
    {
        if (additional.Lines.Count > 0)
        {
            return additional.Lines
                .Select((line, index) => NormalizeVerificationLine(line, index + 1))
                .ToList();
        }

        if (scenario?.Lines is { Count: > 0 })
        {
            return scenario.Lines
                .Select((line, index) =>
                {
                    var businessType = Enum.IsDefined(typeof(BusinessType), line.BusinessType)
                        ? (BusinessType)line.BusinessType
                        : BusinessType.Otro;

                    return NormalizeVerificationLine(new ScoreVerificationLineInput
                    {
                        LineId = string.IsNullOrWhiteSpace(line.ProductId) ? $"line-{index + 1}" : line.ProductId,
                        ProductId = line.ProductId,
                        ProductName = line.ProductDescription,
                        LineOptionValue = ResolveLineOptionValueFromBusinessType(businessType),
                        LineType = businessType.ToString(),
                        HasVat = line.HasVat,
                        HasVatOptionValue = ResolveHasVatOptionValue(line.HasVat),
                        CostUnit = line.CostUnit,
                        MarginPercent = line.MarginPercent,
                        ContractMonths = line.ContractMonths,
                        Quantity = line.Quantity,
                        SuggestedRetailPrice = line.SuggestedRetailPrice,
                        Acelerador = line.Acelerador
                    }, index + 1);
                })
                .ToList();
        }

        return record.ProductLines
            .Select((line, index) => NormalizeVerificationLine(new ScoreVerificationLineInput
            {
                LineId = string.IsNullOrWhiteSpace(line.LineId) ? $"line-{index + 1}" : line.LineId,
                ProductId = line.ProductId,
                ProductName = line.ProductName,
                LineType = line.LineType,
                LineOptionValue = line.LineOptionValue,
                HasVat = line.HasVat,
                HasVatOptionValue = line.HasVatOptionValue,
                CostUnit = line.CostUnit,
                MarginPercent = line.MarginPercent,
                ContractMonths = line.ContractMonths,
                Quantity = line.Quantity,
                SaleUnit = line.MonthlyUnitValue,
                MonthlyValue = line.MonthlyValue,
                TotalValue = line.TotalValue
            }, index + 1))
            .ToList();
    }

    private ScoreComputationContext BuildScoreComputationContext(ScoreVerificationRequest request, bool requireProductLookup)
    {
        ValidateVerificationRequest(request, requireProductLookup);

        var normalizedLines = request.Lines
            .Select((line, index) => NormalizeVerificationLine(line, index + 1))
            .ToList();
        var startDate = ParseRequiredScenarioDate(
            request.ScenarioStartDateValue,
            request.RequiresProration,
            "La fecha inicial del negocio es obligatoria para recalcular el prorrateo.");
        var endDate = ParseRequiredScenarioDate(
            request.ScenarioEndDateValue,
            request.RequiresProration,
            "Debes indicar la fecha final para recalcular un negocio prorrateado.");

        var dealTypeValue = request.RequiresProration ? (int)DealType.CrossSale : request.DealTypeValue;
        if (!Enum.IsDefined(typeof(DealType), dealTypeValue))
            throw new InvalidOperationException("El tipo de negocio no es valido para recalcular el puntaje.");

        var quoteInput = new QuoteScenarioInput
        {
            ScenarioName = string.IsNullOrWhiteSpace(request.BusinessId) ? "Verificacion" : request.BusinessId,
            DealType = (DealType)dealTypeValue,
            RequiresProration = request.RequiresProration,
            StartDate = startDate?.ToDateTime(TimeOnly.MinValue),
            EndDate = endDate?.ToDateTime(TimeOnly.MinValue),
            Lines = normalizedLines.Select(line => new QuoteLineInput
            {
                BusinessType = ResolveBusinessTypeForLine(line),
                ProductId = line.ProductId,
                ProductDescription = line.ProductName,
                CostUnit = line.CostUnit,
                MarginPercent = line.MarginPercent,
                ContractMonths = line.ContractMonths,
                Quantity = line.Quantity,
                SuggestedRetailPrice = line.SuggestedRetailPrice,
                Acelerador = line.Acelerador,
                HasVat = line.HasVat
            }).ToList()
        };

        var result = _calculator.Calculate(quoteInput);
        return new ScoreComputationContext
        {
            DealTypeValue = dealTypeValue,
            RequiresProration = request.RequiresProration,
            StartDateValue = startDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            EndDateValue = endDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            Lines = normalizedLines,
            Result = new ScoreVerificationComputedResultDto
            {
                Points = RoundCurrency(result.Points),
                Commission = RoundCurrency(result.Commission),
                ProrationDays = result.ProrationDays,
                ProrationFactor = result.ProrationFactor,
                ProrationText = BuildProrationText(result.ProrationDays, result.ProrationFactor),
                TotalMonthlySale = RoundCurrency(result.TotalMonthlySale),
                TotalSale = RoundCurrency(result.TotalSale)
            }
        };
    }

    private void ValidateVerificationRequest(ScoreVerificationRequest request, bool requireProductLookup)
    {
        _ = NormalizeGuid(request.RecordId, nameof(request.RecordId));

        if (request.BillingDay is < 0 or > 31)
            throw new InvalidOperationException("El dia de facturacion debe estar entre 1 y 31.");

        if (request.Lines is null || request.Lines.Count == 0)
            throw new InvalidOperationException("Debes incluir al menos una linea de negocio.");

        if (requireProductLookup)
        {
            if (!AllowedFirstContractOptionValues.Contains(request.FirstContractOptionValue))
                throw new InvalidOperationException("Debes seleccionar si es el primer contrato con el cliente.");

            if (!AllowedVerticalOptionValues.Contains(request.VerticalOptionValue))
                throw new InvalidOperationException("Debes seleccionar una vertical.");

            if (!AllowedBinaryOptionValues.Contains(request.AutoBillOptionValue))
                throw new InvalidOperationException("Debes indicar si el negocio es facturable automatico.");

            if (!AllowedContractTypeOptionValues.Contains(request.ContractTypeOptionValue))
                throw new InvalidOperationException("Debes seleccionar el tipo de contrato.");
        }

        foreach (var (line, index) in request.Lines.Select((value, index) => (value, index)))
        {
            if (string.IsNullOrWhiteSpace((line.ProductName ?? "").Trim()))
                throw new InvalidOperationException($"La linea {index + 1} no tiene nombre de producto.");

            if (requireProductLookup && string.IsNullOrWhiteSpace(line.ProductId))
                throw new InvalidOperationException($"La linea {index + 1} debe seleccionar un producto desde el buscador.");

            if (line.Quantity <= 0)
                throw new InvalidOperationException($"La linea {index + 1} tiene cantidad invalida.");

            if (line.ContractMonths <= 0)
                throw new InvalidOperationException($"La linea {index + 1} tiene duracion invalida.");

            if (line.CostUnit < 0m)
                throw new InvalidOperationException($"La linea {index + 1} tiene costo invalido.");
        }
    }

    private ScoreVerificationLineInput NormalizeVerificationLine(ScoreVerificationLineInput line, int index)
    {
        var costUnit = RoundCurrency(Math.Max(line.CostUnit, 0m));
        var marginPercent = RoundCurrency(line.MarginPercent);
        var productName = string.IsNullOrWhiteSpace(line.ProductName) ? $"Producto {index}" : line.ProductName.Trim();
        var contractMonths = NormalizeContractMonths(line.ContractMonths, productName);
        var quantity = line.Quantity > 0 ? line.Quantity : 1;
        var saleUnit = RoundCurrency(costUnit * (1m + (marginPercent / 100m)));
        var monthlyValue = RoundCurrency(saleUnit * quantity);
        var totalValue = RoundCurrency(monthlyValue * contractMonths);

        return new ScoreVerificationLineInput
        {
            LineId = string.IsNullOrWhiteSpace(line.LineId) ? $"line-{index}" : line.LineId.Trim(),
            ProductId = line.ProductId?.Trim() ?? "",
            ProductName = productName,
            LineType = ResolveLineTypeLabel(
                AllowedLineOptionValues.Contains(line.LineOptionValue) ? line.LineOptionValue : ResolveLineOptionValue(line.LineType),
                line.LineType),
            LineOptionValue = AllowedLineOptionValues.Contains(line.LineOptionValue)
                ? line.LineOptionValue
                : ResolveLineOptionValue(line.LineType),
            HasVat = line.HasVatOptionValue > 0 ? line.HasVatOptionValue == 1 : line.HasVat,
            HasVatOptionValue = line.HasVatOptionValue > 0 ? line.HasVatOptionValue : ResolveHasVatOptionValue(line.HasVat),
            CostUnit = costUnit,
            MarginPercent = marginPercent,
            ContractMonths = contractMonths,
            Quantity = quantity,
            SuggestedRetailPrice = RoundCurrency(line.SuggestedRetailPrice),
            Acelerador = RoundCurrency(line.Acelerador),
            SaleUnit = saleUnit,
            MonthlyValue = monthlyValue,
            TotalValue = totalValue
        };
    }

    private ScoreAdditionalDataSnapshot BuildAdditionalSnapshot(
        ScoreVerificationRequest request,
        ScoreComputationContext computation,
        ScoreAdditionalDataSnapshot existing,
        Models.CurrentUserInfo currentUser)
    {
        existing.Version = 1;
        existing.BusinessId = request.BusinessId?.Trim() ?? existing.BusinessId ?? "";
        existing.DealTypeValue = computation.DealTypeValue;
        existing.RequiresProration = computation.RequiresProration;
        existing.ScenarioStartDateValue = computation.StartDateValue;
        existing.ScenarioEndDateValue = computation.EndDateValue;
        existing.BillingDay = request.BillingDay;
        existing.RenewalDateValue = request.RenewalDateValue?.Trim() ?? "";
        existing.RenewalMode = string.IsNullOrWhiteSpace(existing.RenewalDateValue) && !request.RequiresProration && computation.Lines.FirstOrDefault()?.ContractMonths != 12
            ? "ONETIME"
            : "";
        existing.AlignmentDateValue = "";
        existing.HasVatOptionValue = ResolvePrimaryHasVatOptionValue(computation.Lines, existing.HasVatOptionValue);
        existing.AutoBillOptionValue = request.AutoBillOptionValue;
        existing.ProductLineOptionValue = ResolvePrimaryLineOptionValue(computation.Lines, existing.ProductLineOptionValue);
        existing.ContractTypeOptionValue = request.ContractTypeOptionValue;
        existing.ContractKindOptionValue = ResolveScoreContractKindOptionValue(request.ContractKindOptionValue, existing.ContractKindOptionValue, computation.DealTypeValue);
        existing.Lines = computation.Lines;
        existing.LastResult = computation.Result;
        existing.VerifiedAt = DateTimeOffset.UtcNow;
        existing.VerifiedBy = ResolveUserDisplayName(currentUser);
        return existing;
    }

    private IEnumerable<Dictionary<string, object?>> BuildVerificationPayloadCandidates(
        ScoreVerificationRequest request,
        ScoreVerificationComputedResultDto result,
        string additionalJson,
        PrimitiveFieldKind verifiedFieldKind,
        PrimitiveFieldKind firstContractFieldKind)
    {
        var verifiedValue = ResolvePrimitivePayloadValue(
            verifiedFieldKind,
            preferredBooleanValue: true,
            preferredIntegerValue: 1,
            preferBooleanWhenUnknown: true);
        var firstContractValue = ResolvePrimitivePayloadValue(
            firstContractFieldKind,
            preferredBooleanValue: request.FirstContractOptionValue == 1,
            preferredIntegerValue: request.FirstContractOptionValue,
            preferBooleanWhenUnknown: false);

        yield return new Dictionary<string, object?>
        {
            [_scoresFirstContractField] = firstContractValue,
            [_scoresVerticalField] = request.VerticalOptionValue,
            [_scoresContractKindField] = ResolveScoreContractKindOptionValue(request.ContractKindOptionValue, 0, request.DealTypeValue),
            [_scoresScoreField] = result.Points,
            [_scoresCommissionField] = result.Commission,
            [_scoresAdditionalField] = additionalJson,
            [_scoresVerifiedField] = verifiedValue
        };
    }

    private static object ResolvePrimitivePayloadValue(PrimitiveFieldKind kind, bool preferredBooleanValue, int preferredIntegerValue, bool preferBooleanWhenUnknown) =>
        kind switch
        {
            PrimitiveFieldKind.Boolean => preferredBooleanValue,
            PrimitiveFieldKind.Integer => preferredIntegerValue,
            _ => preferBooleanWhenUnknown ? preferredBooleanValue : preferredIntegerValue
        };

    private string BuildVerificationPayloadFailureDetail(Dictionary<string, object?> payload, Exception ex)
    {
        var payloadSummary = string.Join("; ", payload.Select(entry => DescribePayloadEntry(entry.Key, entry.Value)));
        var hint = TryBuildPayloadTypeMismatchHint(payload, ex);
        return string.IsNullOrWhiteSpace(hint)
            ? $"Payload enviado en verificacion: {payloadSummary}"
            : $"Payload enviado en verificacion: {payloadSummary}. {hint}";
    }

    private static string DescribePayloadEntry(string fieldName, object? value)
    {
        if (value is null)
            return $"{fieldName}=null";

        return value switch
        {
            string text when text.Length > 160 => $"{fieldName}=\"{text[..160]}...\" (String, {text.Length} chars)",
            string text => $"{fieldName}=\"{text}\" (String)",
            bool boolean => $"{fieldName}={boolean} (Boolean)",
            int integer => $"{fieldName}={integer} (Int32)",
            decimal number => $"{fieldName}={number.ToString(CultureInfo.InvariantCulture)} (Decimal)",
            _ => $"{fieldName}={value} ({value.GetType().Name})"
        };
    }

    private static string TryBuildPayloadTypeMismatchHint(Dictionary<string, object?> payload, Exception ex)
    {
        var match = Regex.Match(
            ex.ToString(),
            "Cannot convert the literal '(?<literal>[^']+)' to the expected type '(?<target>Edm\\.[^']+)'",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success)
            return "";

        var literal = match.Groups["literal"].Value.Trim();
        var target = match.Groups["target"].Value.Trim();
        var candidates = payload
            .Where(entry => string.Equals(DescribeLiteralValue(entry.Value), literal, StringComparison.OrdinalIgnoreCase))
            .Select(entry => $"{entry.Key}={DescribePayloadScalar(entry.Value)}")
            .ToList();

        if (candidates.Count == 0)
            return $"Dataverse esperaba {target} pero recibio el literal '{literal}'.";

        return $"Dataverse esperaba {target} pero recibio el literal '{literal}'. Campo(s) candidato(s): {string.Join(", ", candidates)}.";
    }

    private static string DescribeLiteralValue(object? value) =>
        value switch
        {
            null => "null",
            bool boolean => boolean ? "True" : "False",
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? value.ToString() ?? "",
            _ => value.ToString() ?? ""
        };

    private static string DescribePayloadScalar(object? value) =>
        value switch
        {
            null => "null",
            bool boolean => $"{boolean} (Boolean)",
            int integer => $"{integer} (Int32)",
            decimal number => $"{number.ToString(CultureInfo.InvariantCulture)} (Decimal)",
            _ => $"{value} ({value?.GetType().Name ?? "null"})"
        };

    private async Task<string> TryNotifyBillingAsync(
        ScoreRecordDto record,
        ScoreVerificationRequest request,
        ScoreVerificationComputedResultDto result,
        Models.CurrentUserInfo currentUser,
        CancellationToken ct)
    {
        if (request.AutoBillOptionValue == 1)
            return "";

        if (string.IsNullOrWhiteSpace(_scoresBillingNotificationFlowUrl))
            return "No se envio el correo a facturacion porque falta configurar Scores:BillingNotificationFlowUrl.";

        if (string.IsNullOrWhiteSpace(_scoresBillingNotificationRecipientEmail))
            return "No se envio el correo a facturacion porque falta configurar Scores:BillingNotificationRecipientEmail.";

        var payload = new
        {
            source = "puntajes",
            recipientEmail = _scoresBillingNotificationRecipientEmail.Trim(),
            requester = new
            {
                displayName = ResolveUserDisplayName(currentUser),
                email = currentUser.Email?.Trim() ?? ""
            },
            business = new
            {
                recordId = record.RecordId,
                businessId = request.BusinessId?.Trim() ?? "",
                clientId = record.ClientId,
                clientName = record.ClientName,
                salesPerson = record.SalesPerson,
                offer = record.Offer,
                contractStartDate = record.ContractStartDateValue,
                renewalDate = request.RenewalDateValue?.Trim() ?? "",
                billingDay = request.BillingDay,
                firstContractOptionValue = request.FirstContractOptionValue,
                verticalOptionValue = request.VerticalOptionValue,
                autoBillOptionValue = request.AutoBillOptionValue,
                contractTypeOptionValue = request.ContractTypeOptionValue,
                dealTypeValue = request.DealTypeValue,
                requiresProration = request.RequiresProration,
                scenarioStartDateValue = request.ScenarioStartDateValue?.Trim() ?? "",
                scenarioEndDateValue = request.ScenarioEndDateValue?.Trim() ?? ""
            },
            result = new
            {
                points = result.Points,
                commission = result.Commission,
                prorationDays = result.ProrationDays,
                prorationFactor = result.ProrationFactor,
                prorationText = result.ProrationText,
                totalMonthlySale = result.TotalMonthlySale,
                totalSale = result.TotalSale
            },
            lineItems = (request.Lines ?? new List<ScoreVerificationLineInput>()).Select(line => new
            {
                lineId = line.LineId?.Trim() ?? "",
                productId = line.ProductId?.Trim() ?? "",
                productName = line.ProductName?.Trim() ?? "",
                quantity = line.Quantity,
                costUnit = line.CostUnit,
                marginPercent = line.MarginPercent,
                contractMonths = line.ContractMonths,
                lineType = line.LineType?.Trim() ?? "",
                lineOptionValue = line.LineOptionValue,
                hasVat = line.HasVat,
                hasVatOptionValue = line.HasVatOptionValue,
                saleUnit = line.SaleUnit,
                monthlyValue = line.MonthlyValue,
                totalValue = line.TotalValue
            })
        };

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var response = await client.PostAsJsonAsync(_scoresBillingNotificationFlowUrl, payload, cancellationToken: ct);
            if (response.IsSuccessStatusCode)
                return "";

            var body = await response.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(body)
                ? $"No se pudo enviar el correo a facturacion. El flujo respondio HTTP {(int)response.StatusCode}."
                : $"No se pudo enviar el correo a facturacion. {body.Trim()}";
        }
        catch (Exception ex)
        {
            return $"No se pudo enviar el correo a facturacion. {SummarizeExceptionMessages(ex)}";
        }
    }

    private async Task<List<SalesPerformanceCompactRecord>> GetSalesPerformanceRecordsByClientAsync(
        string clientId,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedClientId = NormalizeGuid(clientId, nameof(clientId));
        var clientGuid = Guid.Parse(normalizedClientId);
        var candidateLookupFields = new[]
        {
            _salesPerformanceClientLookupFilterField,
            DefaultSalesPerformanceClientLookupFilterField,
            "_cr07a_clientelookup_value"
        }
        .Where(field => !string.IsNullOrWhiteSpace(field))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        Exception? lastError = null;
        foreach (var lookupField in candidateLookupFields)
        {
            try
            {
                var filter = $"{lookupField} eq {clientGuid:D}";
                // Do not force raw lookup properties in $select. Dataverse rejects unknown
                // lookup backing-field names before fallback detection can run, and we already
                // parse the lookup property dynamically from the returned payload.
                var relativeUrl = $"/api/data/v9.2/{_salesPerformanceTableSetName}?$filter={Uri.EscapeDataString(filter)}";
                var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
                return items
                    .Select(ParseSalesPerformanceCompactRecord)
                    .Where(item => item is not null && string.Equals(item.ClientId, normalizedClientId, StringComparison.OrdinalIgnoreCase))
                    .Select(item => item!)
                    .ToList();
            }
            catch (InvalidOperationException ex)
            {
                lastError = ex;
            }
        }

        var scanItems = await GetDataverseEntitiesAsync($"/api/data/v9.2/{_salesPerformanceTableSetName}", user, ct, AddFormattedValueHeaders);
        var scannedResults = scanItems
            .Select(ParseSalesPerformanceCompactRecord)
            .Where(item => item is not null && string.Equals(item.ClientId, normalizedClientId, StringComparison.OrdinalIgnoreCase))
            .Select(item => item!)
            .ToList();

        if (scannedResults.Count > 0)
            return scannedResults;

        if (lastError is not null)
            throw new InvalidOperationException("No fue posible consultar los registros existentes de sales performance.", lastError);

        return new List<SalesPerformanceCompactRecord>();
    }

    private SalesPerformanceCompactRecord? ParseSalesPerformanceCompactRecord(JsonElement item)
    {
        var recordId = ReadString(item, _salesPerformanceIdField);
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var clientLookupProperty = DetectLookupValueProperty(item, SalesPerformanceClientLookupFieldCandidates, "cliente");
        var productLookupProperty = DetectLookupValueProperty(item, SalesPerformanceProductLookupFieldCandidates, "producto");
        return new SalesPerformanceCompactRecord
        {
            RecordId = recordId,
            ClientId = ReadString(item, clientLookupProperty).Trim(),
            ProductId = ReadString(item, productLookupProperty).Trim(),
            Quantity = ReadIntFlexible(item, DefaultSalesPerformanceQuantityField),
            UnitSaleUsd = ReadDecimal(item, DefaultSalesPerformanceUnitSaleUsdField) ?? 0m
        };
    }

    private async Task UpdateSalesPerformanceQuantityAsync(
        string recordId,
        int quantity,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var payload = new Dictionary<string, object?>
        {
            [DefaultSalesPerformanceQuantityField] = quantity
        };
        var updateUrl = $"/api/data/v9.2/{_salesPerformanceTableSetName}({normalizedRecordId})";
        await CallDataverseSendAsync(updateUrl, "PATCH", payload, user, ct);
    }

    private async Task DeleteSalesPerformanceRecordAsync(
        string recordId,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        await CallDataverseDeleteAsync($"/api/data/v9.2/{_salesPerformanceTableSetName}({normalizedRecordId})", user, ct);
    }

    private async Task<SalesPerformanceCreateResult> CreateSalesPerformanceRecordAsync(
        ScoreRecordDto record,
        ScoreVerificationDetailDto detail,
        ScoreVerificationLineInput line,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        var clientId = NormalizeGuid(record.ClientId, nameof(record.ClientId));
        var productId = NormalizeGuid(line.ProductId, nameof(line.ProductId));
        var createName = BuildSalesPerformanceName(record, line);
        var primaryNameField = await ResolveSalesPerformancePrimaryNameFieldAsync(user, ct);
        var renewalDateValue = ResolveSalesPerformanceRenewalDate(detail);
        var billingDay = ResolveSalesPerformanceBillingDay(detail);
        var hasVatOptionValue = ResolveSalesPerformanceHasVatOptionValue(line);
        var productLineOptionValue = ResolveSalesPerformanceProductLineOptionValue(detail, line);
        var warnings = BuildMonthCloseWarnings(record, detail, line);
        var optionalFieldWarnings = BuildSalesPerformanceCreateOptionalFieldWarnings();

        var basePayload = new Dictionary<string, object?>
        {
            [primaryNameField] = createName,
            [DefaultSalesPerformanceQuantityField] = line.Quantity,
            [DefaultSalesPerformanceUnitSaleUsdField] = line.SaleUnit,
            [_salesPerformanceHasVatField] = hasVatOptionValue == 1,
            [_salesPerformanceAutoBillField] = detail.AutoBillOptionValue == 1
        };

        if (HasSalesPerformanceProductLineValue(detail, line))
            basePayload[_salesPerformanceProductLineField] = productLineOptionValue;

        if (AllowedContractTypeOptionValues.Contains(detail.ContractTypeOptionValue))
            basePayload[_salesPerformanceContractTypeField] = detail.ContractTypeOptionValue;

        if (detail.AutoBillOptionValue == 1 && billingDay > 0)
            basePayload[_salesPerformanceBillingDayField] = billingDay;

        if (!string.IsNullOrWhiteSpace(renewalDateValue))
            basePayload[_salesPerformanceRenewalDateField] = renewalDateValue;

        var clientLookupCandidates = await ResolveSalesPerformanceNavigationPropertyCandidatesAsync(
            _salesPerformanceClientLookupLogicalName,
            BuildLookupLogicalNameCandidates(
                _salesPerformanceClientLookupLogicalName,
                DeriveLookupLogicalName(_salesPerformanceClientLookupFilterField),
                DefaultSalesPerformanceClientCreateLookupLogicalName,
                DefaultSalesPerformanceClientLookupLogicalName,
                "cr07a_clientelookup"),
            user,
            ct);
        var productLookupCandidates = await ResolveSalesPerformanceNavigationPropertyCandidatesAsync(
            _salesPerformanceProductLookupLogicalName,
            BuildLookupLogicalNameCandidates(
                _salesPerformanceProductLookupLogicalName,
                DefaultSalesPerformanceProductLookupLogicalName,
                "cr07a_producto"),
            user,
            ct);

        Exception? lastError = null;
        var attemptDiagnostics = new List<string>();
        foreach (var clientLookupLogicalName in clientLookupCandidates)
        {
            foreach (var productLookupLogicalName in productLookupCandidates)
            {
                var payload = new Dictionary<string, object?>(basePayload)
                {
                    [$"{clientLookupLogicalName}@odata.bind"] = $"/{ClientsEntitySetName}({clientId})",
                    [$"{productLookupLogicalName}@odata.bind"] = $"/{ProductsEntitySetName}({productId})"
                };
                var attemptWarnings = new List<string>(warnings);
                var removedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                while (true)
                {
                    try
                    {
                        await CallDataverseSendAsync($"/api/data/v9.2/{_salesPerformanceTableSetName}", "POST", payload, user, ct);
                        return new SalesPerformanceCreateResult
                        {
                            FinalState = BuildCreatedLineState(
                                record,
                                detail,
                                line,
                                includeContractType: payload.ContainsKey(_salesPerformanceContractTypeField),
                                includeAutoBill: payload.ContainsKey(_salesPerformanceAutoBillField),
                                includeHasVat: payload.ContainsKey(_salesPerformanceHasVatField),
                                includeProductLine: payload.ContainsKey(_salesPerformanceProductLineField),
                                includeBillingDay: payload.ContainsKey(_salesPerformanceBillingDayField),
                                includeRenewalDate: payload.ContainsKey(_salesPerformanceRenewalDateField)),
                            Warnings = attemptWarnings
                                .Where(value => !string.IsNullOrWhiteSpace(value))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList()
                        };
                    }
                    catch (InvalidOperationException ex)
                    {
                        lastError = ex;
                        var errorDetail = BuildMonthCloseErrorDetail(ex);
                        attemptDiagnostics.Add(
                            $"Lookup cliente={clientLookupLogicalName}, producto={productLookupLogicalName}, payload={BuildSalesPerformancePayloadSummary(payload)} -> {ExtractActionableDataverseError(errorDetail)}");

                        var removableField = ResolveRetryableCreateField(errorDetail, payload.Keys, optionalFieldWarnings.Keys, removedFields);
                        if (string.IsNullOrWhiteSpace(removableField))
                            break;

                        payload.Remove(removableField);
                        removedFields.Add(removableField);

                        if (optionalFieldWarnings.TryGetValue(removableField, out var warning)
                            && !attemptWarnings.Contains(warning, StringComparer.OrdinalIgnoreCase))
                        {
                            attemptWarnings.Add(warning);
                        }
                    }
                }
            }
        }

        var diagnosticDetail = attemptDiagnostics.Count == 0
            ? ""
            : $" Intentos: {string.Join(" | ", attemptDiagnostics)}";
        throw new InvalidOperationException($"No se pudo crear la linea en cr07a_salesperformancerecord.{diagnosticDetail}", lastError);
    }


    private string SerializeAdditionalForDataverse(ScoreAdditionalDataSnapshot additional)
    {
        const int maxLength = 4000;
        var json = JsonSerializer.Serialize(additional);
        if (json.Length <= maxLength)
            return json;

        var compact = CloneAdditionalForStorage(additional);
        compact.LastResult = null;
        foreach (var line in compact.Lines)
        {
            line.ProductName = TruncateForAdditional(line.ProductName, 80);
            line.LineType = TruncateForAdditional(line.LineType, 30);
            line.ProductId = TruncateForAdditional(line.ProductId, 60);
            line.LineId = TruncateForAdditional(line.LineId, 40);
        }

        json = JsonSerializer.Serialize(compact);
        if (json.Length <= maxLength)
            return json;

        compact.MonthlyClosures = compact.MonthlyClosures
            .OrderByDescending(item => item.ClosedAt)
            .Take(2)
            .ToList();
        foreach (var closure in compact.MonthlyClosures)
        {
            closure.ClosedBy = TruncateForAdditional(closure.ClosedBy, 60);
            closure.Lines = closure.Lines.Take(15).ToList();
            foreach (var closureLine in closure.Lines)
            {
                closureLine.ProductName = TruncateForAdditional(closureLine.ProductName, 80);
                closureLine.ProductId = TruncateForAdditional(closureLine.ProductId, 60);
                closureLine.Warnings = new List<string>();
            }
        }

        json = JsonSerializer.Serialize(compact);
        if (json.Length <= maxLength)
            return json;

        compact.Lines = compact.Lines.Take(25).ToList();
        json = JsonSerializer.Serialize(compact);
        if (json.Length <= maxLength)
            return json;

        compact.MonthlyClosures = new List<ScoreMonthlyClosureSnapshot>();
        json = JsonSerializer.Serialize(compact);
        if (json.Length <= maxLength)
            return json;

        compact.Lines = compact.Lines.Take(10).ToList();
        compact.VerifiedBy = TruncateForAdditional(compact.VerifiedBy, 40);
        compact.LastClosedBy = TruncateForAdditional(compact.LastClosedBy, 40);
        json = JsonSerializer.Serialize(compact);

        if (json.Length <= maxLength)
            return json;

        compact.Lines = new List<ScoreVerificationLineInput>();
        compact.VerifiedBy = TruncateForAdditional(compact.VerifiedBy, 20);
        compact.LastClosedBy = TruncateForAdditional(compact.LastClosedBy, 20);
        compact.BusinessId = TruncateForAdditional(compact.BusinessId, 50);
        compact.LastResult = null;
        compact.MonthlyClosures = new List<ScoreMonthlyClosureSnapshot>();

        return JsonSerializer.Serialize(compact);
    }

    private static ScoreAdditionalDataSnapshot CloneAdditionalForStorage(ScoreAdditionalDataSnapshot source) =>
        DeserializeJsonOrDefault<ScoreAdditionalDataSnapshot>(JsonSerializer.Serialize(source)) ?? new ScoreAdditionalDataSnapshot();

    private static string TruncateForAdditional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized.Substring(0, maxLength);
    }

    private async Task UpdateScoreAdditionalDataAsync(
        string recordId,
        ScoreAdditionalDataSnapshot additional,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var payload = new Dictionary<string, object?>
        {
            [_scoresAdditionalField] = SerializeAdditionalForDataverse(additional)
        };
        var updateUrl = $"/api/data/v9.2/{_scoresTableSetName}({normalizedRecordId})";
        await CallDataverseSendAsync(updateUrl, "PATCH", payload, user, ct);
    }

    private static ScoreMonthCloseLogEntryDto BuildMonthCloseLog(
        string level,
        string action,
        string lineKey,
        string recordId,
        string clientName,
        string productName,
        string message,
        string finalState,
        string detail = "") =>
        new()
        {
            Level = level,
            Action = action,
            LineKey = lineKey,
            RecordId = recordId,
            ClientName = clientName,
            ProductName = productName,
            Message = message,
            FinalState = finalState,
            Detail = detail
        };

    private static ScoreMonthCloseLogEntryDto BuildMonthCloseLog(
        string level,
        string recordId,
        string clientName,
        string productName,
        string message,
        string detail = "") =>
        BuildMonthCloseLog(level, "", "", recordId, clientName, productName, message, "", detail);

    private static string CompactMonthCloseError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Se produjo un error sin detalle adicional.";

        var compact = string.Join(" ", raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length > 220 ? $"{compact[..217]}..." : compact;
    }

    private static string BuildSalesPerformanceName(ScoreRecordDto record, ScoreVerificationLineInput line)
    {
        var client = string.IsNullOrWhiteSpace(record.ClientName) ? "Cliente" : record.ClientName.Trim();
        var product = string.IsNullOrWhiteSpace(line.ProductName) ? "Producto" : line.ProductName.Trim();
        return $"{client} - {product}";
    }

    private static string ResolveSalesPerformanceRenewalDate(ScoreVerificationDetailDto detail)
    {
        if (string.Equals(detail.RenewalMode, "ONETIME", StringComparison.OrdinalIgnoreCase))
            return "";

        if (detail.RequiresProration)
            return FirstNonEmpty(detail.ScenarioEndDateValue, detail.RenewalDateValue);

        return FirstNonEmpty(detail.RenewalDateValue, detail.ScenarioEndDateValue);
    }

    private static void MarkRecordAsClosed(ScoreAdditionalDataSnapshot additional, string periodKey, Models.CurrentUserInfo currentUser)
    {
        if (string.IsNullOrWhiteSpace(periodKey))
            return;

        additional.MonthlyClosures ??= new List<ScoreMonthlyClosureSnapshot>();
        if (additional.MonthlyClosures.Any(item => string.Equals(item.PeriodKey, periodKey, StringComparison.OrdinalIgnoreCase)))
            return;

        var closure = new ScoreMonthlyClosureSnapshot
        {
            PeriodKey = periodKey,
            ClosedAt = DateTimeOffset.UtcNow,
            ClosedBy = ResolveUserDisplayName(currentUser)
        };
        additional.MonthlyClosures.Add(closure);
        additional.LastClosedAt = closure.ClosedAt;
        additional.LastClosedBy = closure.ClosedBy;
    }

    private static bool HasMonthlyClosure(ScoreAdditionalDataSnapshot additional, string? activePeriodKey, IEnumerable<string>? expectedLineKeys = null)
    {
        if (string.IsNullOrWhiteSpace(activePeriodKey) || additional.MonthlyClosures.Count == 0)
            return false;

        var periodClosures = additional.MonthlyClosures
            .Where(item => string.Equals(item.PeriodKey, activePeriodKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (periodClosures.Count == 0)
            return false;

        if (!periodClosures.Any(item => item.Lines.Count > 0))
            return true;

        var normalizedLineKeys = (expectedLineKeys ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalizedLineKeys.Count == 0)
            return true;

        return normalizedLineKeys.All(lineKey => ResolveMonthlyClosureLine(additional, activePeriodKey, lineKey) is not null);
    }

    private static ScoreMonthlyClosureLineSnapshot? ResolveMonthlyClosureLine(ScoreAdditionalDataSnapshot additional, string? periodKey, string? lineKey)
    {
        if (string.IsNullOrWhiteSpace(periodKey) || string.IsNullOrWhiteSpace(lineKey))
            return null;

        return additional.MonthlyClosures
            .Where(item => string.Equals(item.PeriodKey, periodKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.ClosedAt)
            .SelectMany(item => item.Lines.Select(line => new { item.ClosedAt, Line = line }))
            .Where(item => string.Equals(item.Line.LineKey, lineKey.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.ClosedAt)
            .Select(item => item.Line)
            .FirstOrDefault();
    }

    private static ScoreMonthlyClosureSnapshot? ResolveLatestMonthCloseBatch(IEnumerable<ScoreRecordContext> contexts, string? periodKey)
    {
        if (string.IsNullOrWhiteSpace(periodKey))
            return null;

        return contexts
            .SelectMany(item => item.Additional.MonthlyClosures)
            .Where(item =>
                string.Equals(item.PeriodKey, periodKey, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(item.BatchId)
                && item.Lines.Count > 0)
            .OrderByDescending(item => item.ClosedAt)
            .FirstOrDefault();
    }

    private static void AppendMonthlyClosure(
        ScoreAdditionalDataSnapshot additional,
        string periodKey,
        string batchId,
        Models.CurrentUserInfo currentUser,
        IReadOnlyList<ScoreMonthlyClosureLineSnapshot> lines)
    {
        if (string.IsNullOrWhiteSpace(periodKey) || string.IsNullOrWhiteSpace(batchId) || lines.Count == 0)
            return;

        additional.MonthlyClosures ??= new List<ScoreMonthlyClosureSnapshot>();
        var closure = additional.MonthlyClosures
            .FirstOrDefault(item =>
                string.Equals(item.PeriodKey, periodKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.BatchId, batchId, StringComparison.OrdinalIgnoreCase));
        if (closure is null)
        {
            closure = new ScoreMonthlyClosureSnapshot
            {
                PeriodKey = periodKey,
                BatchId = batchId,
                ClosedAt = DateTimeOffset.UtcNow,
                ClosedBy = ResolveUserDisplayName(currentUser)
            };
            additional.MonthlyClosures.Add(closure);
        }

        foreach (var line in lines.Where(item => !string.IsNullOrWhiteSpace(item.LineKey)))
        {
            closure.Lines.RemoveAll(item => string.Equals(item.LineKey, line.LineKey, StringComparison.OrdinalIgnoreCase));
            closure.Lines.Add(line);
        }

        closure.ClosedAt = DateTimeOffset.UtcNow;
        closure.ClosedBy = ResolveUserDisplayName(currentUser);
        RefreshMonthlyClosureMetadata(additional);
    }

    private static void RemoveMonthlyClosureLines(
        ScoreAdditionalDataSnapshot additional,
        string periodKey,
        string batchId,
        ISet<string> lineKeys)
    {
        if (string.IsNullOrWhiteSpace(periodKey) || string.IsNullOrWhiteSpace(batchId) || lineKeys.Count == 0)
            return;

        foreach (var closure in additional.MonthlyClosures
                     .Where(item =>
                         string.Equals(item.PeriodKey, periodKey, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(item.BatchId, batchId, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            closure.Lines.RemoveAll(line => lineKeys.Contains(line.LineKey));
            if (closure.Lines.Count == 0)
                additional.MonthlyClosures.Remove(closure);
        }

        RefreshMonthlyClosureMetadata(additional);
    }

    private static void RefreshMonthlyClosureMetadata(ScoreAdditionalDataSnapshot additional)
    {
        var lastClosure = ResolveLastClosure(additional);
        additional.LastClosedAt = lastClosure?.ClosedAt;
        additional.LastClosedBy = lastClosure?.ClosedBy ?? "";
    }

    private static ScoreMonthlyClosureSnapshot? ResolveLastClosure(ScoreAdditionalDataSnapshot additional)
    {
        if (additional.MonthlyClosures.Count == 0)
            return null;

        return additional.MonthlyClosures
            .OrderByDescending(item => item.ClosedAt)
            .FirstOrDefault();
    }

    private static string ResolveMonthCloseLineReason(int autoBillOptionValue, bool isAlreadyClosed, bool isRenewalContract)
    {
        if (isAlreadyClosed)
            return "La linea ya fue consolidada en un cierre anterior.";

        if (isRenewalContract)
            return "Se excluye porque el tipo de contrato del puntaje es Renovacion.";

        return autoBillOptionValue == 1
            ? "La linea quedara incluida porque tiene facturacion automatica habilitada."
            : "Se excluye por defecto porque AutoBillOptionValue es distinto de 1.";
    }

    private static int ResolveSalesPerformanceBillingDay(ScoreVerificationDetailDto detail) =>
        detail.BillingDay > 0
            ? detail.BillingDay
            : DeriveBillingDay(detail.RenewalDateValue, detail.ScenarioEndDateValue, detail.ContractStartDateValue);

    private static int ResolveSalesPerformanceHasVatOptionValue(ScoreVerificationLineInput line) =>
        line.HasVatOptionValue is 0 or 1 ? line.HasVatOptionValue : ResolveHasVatOptionValue(line.HasVat);

    private static int ResolveSalesPerformanceProductLineOptionValue(ScoreVerificationDetailDto detail, ScoreVerificationLineInput line)
    {
        if (AllowedLineOptionValues.Contains(line.LineOptionValue))
            return line.LineOptionValue;

        if (AllowedLineOptionValues.Contains(detail.ProductLineOptionValue))
            return detail.ProductLineOptionValue;

        return AllowedProductLineOptionValues.Contains(detail.ProductLineOptionValue)
            ? detail.ProductLineOptionValue
            : 0;
    }

    private static bool HasSalesPerformanceProductLineValue(ScoreVerificationDetailDto detail, ScoreVerificationLineInput line) =>
        AllowedLineOptionValues.Contains(line.LineOptionValue)
        || AllowedLineOptionValues.Contains(detail.ProductLineOptionValue)
        || AllowedProductLineOptionValues.Contains(detail.ProductLineOptionValue);

    private static List<string> BuildMonthCloseWarnings(ScoreRecordDto record, ScoreVerificationDetailDto detail, ScoreVerificationLineInput line)
    {
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(record.ClientId))
            warnings.Add("Falta el lookup del cliente.");

        if (string.IsNullOrWhiteSpace(line.ProductId))
            warnings.Add("Falta el lookup del producto.");

        if (detail.AutoBillOptionValue == 1 && ResolveSalesPerformanceBillingDay(detail) <= 0)
            warnings.Add("No se pudo determinar el dia de facturacion; debes completarlo manualmente.");

        if (!HasSalesPerformanceProductLineValue(detail, line))
            warnings.Add("No se pudo determinar la linea de producto; debes completarla manualmente.");

        if (!AllowedContractTypeOptionValues.Contains(detail.ContractTypeOptionValue))
            warnings.Add("No se pudo determinar el tipo de contrato; debes completarlo manualmente.");

        if (string.IsNullOrWhiteSpace(ResolveSalesPerformanceRenewalDate(detail)))
            warnings.Add("La fecha de renovacion quedo vacia; revisala manualmente despues del cierre.");

        return warnings;
    }

    private static string BuildCreatedLineState(
        ScoreRecordDto record,
        ScoreVerificationDetailDto detail,
        ScoreVerificationLineInput line,
        bool includeContractType,
        bool includeAutoBill,
        bool includeHasVat,
        bool includeProductLine,
        bool includeBillingDay,
        bool includeRenewalDate)
    {
        var renewalDateValue = ResolveSalesPerformanceRenewalDate(detail);
        var billingDay = ResolveSalesPerformanceBillingDay(detail);
        var productLineOptionValue = ResolveSalesPerformanceProductLineOptionValue(detail, line);
        var hasVatOptionValue = ResolveSalesPerformanceHasVatOptionValue(line);

        return $"Cliente: {record.ClientName}. Producto: {line.ProductName}. Cantidad: {Math.Max(line.Quantity, 0)}. " +
               $"Venta UND USD: {line.SaleUnit:0.##}. Contrato: {(includeContractType ? ResolveOptionLabel(PuntajesOptionCatalog.ContractTypeOptions, detail.ContractTypeOptionValue) : "Pendiente manual")}. " +
               $"Facturable automatico: {(includeAutoBill ? ResolveOptionLabel(PuntajesOptionCatalog.AutoBillOptions, detail.AutoBillOptionValue) : "Pendiente manual")}. " +
               $"IVA: {(includeHasVat ? ResolveOptionLabel(PuntajesOptionCatalog.HasVatOptions, hasVatOptionValue) : "Pendiente manual")}. " +
               $"Linea: {(includeProductLine ? ResolveProductLineLabel(productLineOptionValue) : "Pendiente manual")}. " +
               $"Dia facturacion: {(includeBillingDay && billingDay > 0 ? billingDay.ToString(CultureInfo.InvariantCulture) : "Pendiente manual")}. " +
               $"Renovacion: {(includeRenewalDate && !string.IsNullOrWhiteSpace(renewalDateValue) ? renewalDateValue : "Pendiente manual")}.";
    }

    private static string BuildUpdatedLineState(ScoreMonthCloseLinePlan linePlan, int previousQuantity, int appliedQuantity, int newQuantity) =>
        $"Cliente: {linePlan.Record.ClientName}. Producto: {linePlan.Line.ProductName}. Cantidad final: {newQuantity} (antes {previousQuantity}, cierre {appliedQuantity}).";

    private static string BuildPreviewLineState(ScoreMonthCloseLinePlan linePlan)
    {
        if (linePlan.IsRenewalContract)
            return "Accion prevista: no se envia por estar marcado como renovacion.";

        var finalQuantity = linePlan.ExistingMatch is null
            ? Math.Max(linePlan.Line.Quantity, 0)
            : Math.Max(linePlan.ExistingMatch.Quantity, 0) + Math.Max(linePlan.Line.Quantity, 0);

        return $"Accion prevista: {(linePlan.ExistingMatch is null ? "Nueva linea" : "Incremento")}. Cantidad final estimada: {finalQuantity}.";
    }

    private static ScoreMonthlyClosureLineSnapshot BuildClosureLineSnapshot(
        ScoreMonthCloseLinePlan linePlan,
        string status,
        string salesPerformanceRecordId,
        int previousQuantity,
        int appliedQuantity,
        int finalQuantity,
        IReadOnlyList<string> warnings) =>
        new()
        {
            LineKey = linePlan.LineKey,
            LineId = linePlan.Line.LineId,
            ProductId = linePlan.Line.ProductId,
            ProductName = linePlan.Line.ProductName,
            Status = status,
            SalesPerformanceRecordId = salesPerformanceRecordId,
            PreviousQuantity = previousQuantity,
            AppliedQuantity = appliedQuantity,
            FinalQuantity = finalQuantity,
            Warnings = warnings.ToList()
        };

    private static string BuildMonthCloseLineKey(string recordId, string? lineId, int index)
    {
        var normalizedLineId = string.IsNullOrWhiteSpace(lineId) ? $"line-{index}" : lineId.Trim();
        return $"{recordId.Trim()}::{normalizedLineId}";
    }

    private static string ResolveOptionLabel(IEnumerable<ScoreOptionItem> items, int value) =>
        items.FirstOrDefault(item => item.Value == value)?.Label ?? value.ToString(CultureInfo.InvariantCulture);

    private static string ResolveProductLineLabel(int value)
    {
        if (AllowedLineOptionValues.Contains(value))
            return ResolveOptionLabel(PuntajesOptionCatalog.LineOptions, value);

        if (AllowedProductLineOptionValues.Contains(value))
            return ResolveOptionLabel(PuntajesOptionCatalog.ProductLineOptions, value);

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatDateDisplay(string? value)
    {
        if (!TryParseDateOnly(value, out var date))
            return value?.Trim() ?? "";

        return date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    private static void NormalizeAdditionalSnapshot(ScoreAdditionalDataSnapshot additional)
    {
        additional.BusinessId ??= "";
        additional.ScenarioStartDateValue ??= "";
        additional.ScenarioEndDateValue ??= "";
        additional.RenewalDateValue ??= "";
        additional.RenewalMode ??= "";
        additional.AlignmentDateValue ??= "";
        additional.VerifiedBy ??= "";
        additional.LastClosedBy ??= "";
        additional.Lines ??= new List<ScoreVerificationLineInput>();
        additional.MonthlyClosures ??= new List<ScoreMonthlyClosureSnapshot>();
        foreach (var closure in additional.MonthlyClosures)
        {
            closure.BatchId ??= "";
            closure.ClosedBy ??= "";
            closure.Lines ??= new List<ScoreMonthlyClosureLineSnapshot>();
            foreach (var line in closure.Lines)
            {
                line.LineKey ??= "";
                line.LineId ??= "";
                line.ProductId ??= "";
                line.ProductName ??= "";
                line.Status ??= "";
                line.SalesPerformanceRecordId ??= "";
                line.Warnings ??= new List<string>();
            }
        }
    }

    private static string NormalizeLookupToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value
            .Trim()
            .Normalize(NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .ToArray();

        return new string(normalized)
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
    }

    private static bool TryResolveDealTypeValue(string? raw, out int value)
    {
        switch (NormalizeLookupToken(raw))
        {
            case "clientenuevo":
            case "negocionuevo":
                value = (int)DealType.ClienteNuevo;
                return true;
            case "crosssale":
            case "crossale":
                value = (int)DealType.CrossSale;
                return true;
            case "renovacion1vez":
            case "renovacion1":
                value = (int)DealType.Renovacion1;
                return true;
            case "renovacion2veces":
            case "renovacion2vez":
            case "renovacion2":
                value = (int)DealType.Renovacion2;
                return true;
            case "renovacion3vecesomas":
            case "renovacion3vezomas":
            case "renovacion3omas":
            case "renovacion3":
                value = (int)DealType.Renovacion3Plus;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryResolveYesNoValue(string? raw, out bool value)
    {
        switch (NormalizeLookupToken(raw))
        {
            case "si":
            case "yes":
            case "true":
            case "1":
                value = true;
                return true;
            case "no":
            case "false":
            case "0":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    private static int ResolveLineOptionValue(string? raw)
    {
        switch (NormalizeLookupToken(raw))
        {
            case "modernwork":
                return 645250000;
            case "acronis":
                return 645250001;
            case "azure":
                return 645250002;
            case "copiers":
                return 645250003;
            case "security":
                return 645250005;
            case "serviciosprofesionales":
            case "servicios":
                return 645250006;
            case "perpetuo":
            case "perpetual":
                return 645250007;
            case "hardware":
                return 645250004;
            default:
                return 645250004;
        }
    }

    private static string ResolveLineTypeLabel(int lineOptionValue, string? fallback = null)
    {
        if (string.Equals(NormalizeLookupToken(fallback), "hardware", StringComparison.OrdinalIgnoreCase))
            return "Hardware";

        var label = PuntajesOptionCatalog.LineOptions
            .FirstOrDefault(option => option.Value == lineOptionValue)
            ?.Label;

        return !string.IsNullOrWhiteSpace(label)
            ? label
            : (string.IsNullOrWhiteSpace(fallback) ? "Otro" : fallback.Trim());
    }

    private static bool ContainsPrepaidOrYear(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
            return false;

        return productName.Contains("prepaid", StringComparison.OrdinalIgnoreCase)
            || productName.Contains("1 year", StringComparison.OrdinalIgnoreCase);
    }

    private static int NormalizeContractMonths(int contractMonths, string? productName)
    {
        var normalizedMonths = contractMonths > 0 ? contractMonths : 12;
        return ContainsPrepaidOrYear(productName) ? 12 : normalizedMonths;
    }

    private static int ResolveHasVatOptionValue(bool hasVat) => hasVat ? 1 : 0;

    private static int ResolvePrimaryLineOptionValue(IEnumerable<ScoreVerificationLineInput>? lines, int fallback = 0)
    {
        var value = lines?
            .Select(line => line.LineOptionValue)
            .FirstOrDefault(option => AllowedLineOptionValues.Contains(option))
            ?? 0;

        return value != 0 ? value : fallback;
    }

    private static int ResolvePrimaryHasVatOptionValue(IEnumerable<ScoreVerificationLineInput>? lines, int fallback = 0)
    {
        var first = lines?.FirstOrDefault();
        return first is null ? fallback : ResolveHasVatOptionValue(first.HasVat);
    }

    private static int DeriveFirstContractOptionValue(int dealTypeValue) =>
        dealTypeValue == (int)DealType.ClienteNuevo ? 1 : 2;

    private static int ResolveScoreContractKindOptionValue(int directValue, int fallbackValue, int dealTypeValue)
    {
        if (AllowedContractKindOptionValues.Contains(directValue))
            return directValue;

        if (AllowedContractKindOptionValues.Contains(fallbackValue))
            return fallbackValue;

        return IsRenewalDealTypeValue(dealTypeValue)
            ? ScoreContractKindRenewalValue
            : ScoreContractKindNewBusinessValue;
    }

    private static bool IsRenewalContractKind(int contractKindOptionValue) =>
        contractKindOptionValue == ScoreContractKindRenewalValue;

    private static bool IsRenewalDealTypeValue(int dealTypeValue) =>
        dealTypeValue is (int)DealType.Renovacion1 or (int)DealType.Renovacion2 or (int)DealType.Renovacion3Plus;

    private static string ResolveScoreContractKindLabel(int contractKindOptionValue) =>
        ResolveOptionLabel(PuntajesOptionCatalog.ContractKindOptions, contractKindOptionValue);

    private static BusinessType ResolveBusinessTypeForLine(ScoreVerificationLineInput line)
    {
        if (string.Equals(NormalizeLookupToken(line.LineType), "hardware", StringComparison.OrdinalIgnoreCase))
            return BusinessType.Hardware;

        var lineOptionValue = AllowedLineOptionValues.Contains(line.LineOptionValue)
            ? line.LineOptionValue
            : ResolveLineOptionValue(line.LineType);

        return lineOptionValue switch
        {
            645250000 => BusinessType.ModernWork,
            645250001 => BusinessType.Acronis,
            645250002 => BusinessType.Azure,
            645250003 => BusinessType.Copiers,
            645250007 => BusinessType.Perpetuo,
            _ => BusinessType.Otro
        };
    }

    private static int ResolveLineOptionValueFromBusinessType(BusinessType businessType) =>
        businessType switch
        {
            BusinessType.ModernWork => 645250000,
            BusinessType.Acronis => 645250001,
            BusinessType.Azure => 645250002,
            BusinessType.Copiers => 645250003,
            BusinessType.Perpetuo => 645250007,
            BusinessType.Hardware => 645250004,
            _ => 645250004
        };

    private static int ResolveStoredDealTypeValue(ScoreAdditionalDataSnapshot additional, ScoreDescriptionParseResult parsedDescription, bool isVerified)
    {
        if (!isVerified && TryResolveDealTypeValue(parsedDescription.DealTypeText, out var pendingParsedDealTypeValue))
            return pendingParsedDealTypeValue;

        if (additional.Version > 0 && additional.DealTypeValue.HasValue && Enum.IsDefined(typeof(DealType), additional.DealTypeValue.Value))
            return additional.DealTypeValue.Value;

        if (TryResolveDealTypeValue(parsedDescription.DealTypeText, out var parsedDealTypeValue))
            return parsedDealTypeValue;

        return parsedDescription.ProrationDays > 0 ? (int)DealType.CrossSale : (int)DealType.ClienteNuevo;
    }

    private static bool ResolveStoredRequiresProration(ScoreAdditionalDataSnapshot additional, ScoreDescriptionParseResult parsedDescription)
    {
        if (additional.Version > 0 && additional.RequiresProration)
            return true;

        if (parsedDescription.RequiresProration.HasValue)
            return parsedDescription.RequiresProration.Value;

        return parsedDescription.ProrationDays > 0 && parsedDescription.ProrationFactor is > 0m and < 1m;
    }

    private static int ResolveDealTypeValue(ScoreRecordDto record, ScoreAdditionalDataSnapshot additional, ScenarioStoredDto? scenario)
    {
        if (additional.Version > 0 && additional.DealTypeValue.HasValue && Enum.IsDefined(typeof(DealType), additional.DealTypeValue.Value))
            return additional.DealTypeValue.Value;

        if (record.DealTypeValue >= 0 && Enum.IsDefined(typeof(DealType), record.DealTypeValue))
            return record.DealTypeValue;

        if (TryResolveDealTypeValue(record.ContractType, out var parsedFromRecord))
            return parsedFromRecord;

        if (scenario is not null && Enum.IsDefined(typeof(DealType), scenario.DealType))
            return scenario.DealType;

        return record.ProrationDays > 0 ? (int)DealType.CrossSale : (int)DealType.ClienteNuevo;
    }

    private static bool ResolveRequiresProration(ScoreRecordDto record, ScoreAdditionalDataSnapshot additional, ScenarioStoredDto? scenario)
    {
        if (additional.Version > 0 && additional.RequiresProration)
            return true;

        if (record.RequiresProration)
            return true;

        if (scenario?.RequiresProration == true)
            return true;

        return record.ProrationDays > 0 && record.ProrationFactor is > 0m and < 1m;
    }

    private static string ResolveScenarioStartDateValue(ScoreRecordDto record, ScoreAdditionalDataSnapshot additional, ScenarioStoredDto? scenario) =>
        FirstNonEmpty(additional.ScenarioStartDateValue, scenario?.StartDate, record.ScenarioStartDateValue, record.ContractStartDateValue, record.ProvisioningDateValue);

    private static string ResolveScenarioEndDateValue(ScoreRecordDto record, ScoreAdditionalDataSnapshot additional, ScenarioStoredDto? scenario) =>
        FirstNonEmpty(additional.ScenarioEndDateValue, scenario?.EndDate, record.ScenarioEndDateValue, record.RenewalDateValue);

    private static string BuildProrationText(int days, decimal factor) =>
        (days > 0 && factor is > 0m and < 1m)
            ? $"{days} dias ({factor:0.0000})"
            : "No";

    private static ScoreVerificationRequest NormalizeVerificationRequest(ScoreVerificationRequest request, string? contractStartDateValue)
    {
        var scenarioEndDateValue = request.ScenarioEndDateValue?.Trim() ?? "";
        var renewalDateValue = string.IsNullOrWhiteSpace(request.RenewalDateValue)
            ? ResolveDefaultRenewalDateValue(request.RequiresProration, scenarioEndDateValue, contractStartDateValue, request.Lines)
            : request.RenewalDateValue.Trim();

        return new ScoreVerificationRequest
        {
            RecordId = request.RecordId?.Trim() ?? "",
            BusinessId = request.BusinessId?.Trim() ?? "",
            DealTypeValue = request.DealTypeValue,
            RequiresProration = request.RequiresProration,
            ScenarioStartDateValue = request.ScenarioStartDateValue?.Trim() ?? "",
            ScenarioEndDateValue = scenarioEndDateValue,
            FirstContractOptionValue = request.FirstContractOptionValue > 0 ? request.FirstContractOptionValue : DeriveFirstContractOptionValue(request.DealTypeValue),
            LineOptionValue = request.LineOptionValue,
            VerticalOptionValue = request.VerticalOptionValue,
            BillingDay = ResolveBillingDayForRequest(request.BillingDay, request.AutoBillOptionValue, renewalDateValue, scenarioEndDateValue, contractStartDateValue),
            RenewalDateValue = renewalDateValue,
            AlignmentDateValue = "",
            HasVatOptionValue = request.HasVatOptionValue,
            AutoBillOptionValue = request.AutoBillOptionValue,
            ProductLineOptionValue = request.ProductLineOptionValue,
            ContractTypeOptionValue = request.ContractTypeOptionValue,
            ContractKindOptionValue = ResolveScoreContractKindOptionValue(request.ContractKindOptionValue, 0, request.DealTypeValue),
            Lines = (request.Lines ?? new List<ScoreVerificationLineInput>())
                .Select(line => new ScoreVerificationLineInput
                {
                    LineId = line.LineId,
                    ProductId = line.ProductId,
                    ProductName = line.ProductName,
                    LineType = line.LineType,
                    LineOptionValue = line.LineOptionValue,
                    HasVat = line.HasVat,
                    HasVatOptionValue = line.HasVatOptionValue,
                    CostUnit = line.CostUnit,
                    MarginPercent = line.MarginPercent,
                    ContractMonths = line.ContractMonths,
                    Quantity = line.Quantity,
                    SuggestedRetailPrice = line.SuggestedRetailPrice,
                    Acelerador = line.Acelerador,
                    SaleUnit = line.SaleUnit,
                    MonthlyValue = line.MonthlyValue,
                    TotalValue = line.TotalValue
                })
                .ToList()
        };
    }

    private static string ResolveRenewalDateValue(ScoreRecordDto record, ScoreAdditionalDataSnapshot additional, ScenarioStoredDto? scenario)
    {
        if (string.Equals(additional.RenewalMode, "ONETIME", StringComparison.OrdinalIgnoreCase))
            return "";

        var explicitValue = FirstNonEmpty(record.RenewalDateValue, additional.RenewalDateValue);
        if (!string.IsNullOrWhiteSpace(explicitValue))
            return explicitValue;

        return ResolveDefaultRenewalDateValue(record.RequiresProration, ResolveScenarioEndDateValue(record, additional, scenario), record.ContractStartDateValue, record.ProductLines.Select(line => new ScoreVerificationLineInput
        {
            ContractMonths = line.ContractMonths
        }));
    }

    private static string BuildRenewalDateOneYearAfter(string? baseDateValue)
    {
        if (!TryParseDateOnly(baseDateValue, out var parsedDate))
            return "";

        return parsedDate.AddYears(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string ResolveDefaultRenewalDateValue(bool requiresProration, string? scenarioEndDateValue, string? contractStartDateValue, IEnumerable<ScoreVerificationLineInput>? lines)
    {
        if (requiresProration)
            return FirstNonEmpty(scenarioEndDateValue);

        var firstLineMonths = lines?.FirstOrDefault()?.ContractMonths ?? 0;
        return firstLineMonths == 12 ? BuildRenewalDateOneYearAfter(contractStartDateValue) : "";
    }

    private static int ResolveBillingDayForRequest(int billingDay, int autoBillOptionValue, params string?[] candidates)
    {
        if (autoBillOptionValue != 1)
            return 0;

        return billingDay is >= 1 and <= 31
            ? billingDay
            : DeriveBillingDay(candidates);
    }

    private static DateOnly? ParseRequiredScenarioDate(string? raw, bool required, string errorMessage)
    {
        if (TryParseDateOnly(raw, out var parsed))
            return parsed;

        if (required)
            throw new InvalidOperationException(errorMessage);

        return null;
    }

    private static DateOnly? ParseAdditionalDateOnly(string? raw) =>
        TryParseDateOnly(raw, out var parsed) ? parsed : null;

    private static int DeriveBillingDay(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!TryParseDateOnly(candidate, out var date))
                continue;

            return date.Day;
        }

        return 0;
    }

    private static string ResolveUserDisplayName(Models.CurrentUserInfo currentUser)
    {
        if (!string.IsNullOrWhiteSpace(currentUser.DisplayName))
            return currentUser.DisplayName.Trim();

        if (!string.IsNullOrWhiteSpace(currentUser.Email))
            return currentUser.Email.Trim();

        return "Usuario";
    }

    private static string SummarizeExceptionMessages(Exception ex)
    {
        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (string.IsNullOrWhiteSpace(current.Message))
                continue;

            var trimmedMessage = current.Message.Trim();
            if (!messages.Contains(trimmedMessage, StringComparer.OrdinalIgnoreCase))
                messages.Add(trimmedMessage);
        }

        return messages.Count == 0 ? "Error desconocido." : string.Join(" | ", messages);
    }

    private Dictionary<string, string> BuildSalesPerformanceCreateOptionalFieldWarnings() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [_salesPerformanceHasVatField] = "No se pudo guardar el indicador de IVA; debes completarlo manualmente.",
            [_salesPerformanceAutoBillField] = "No se pudo guardar el indicador de facturacion automatica; debes revisarlo manualmente.",
            [_salesPerformanceProductLineField] = "No se pudo guardar la linea de producto; debes completarla manualmente.",
            [_salesPerformanceContractTypeField] = "No se pudo guardar el tipo de contrato; debes completarlo manualmente.",
            [_salesPerformanceBillingDayField] = "No se pudo guardar el dia de facturacion; debes completarlo manualmente.",
            [_salesPerformanceRenewalDateField] = "No se pudo guardar la fecha de renovacion; debes revisarla manualmente."
        };

    private static string? ResolveRetryableCreateField(
        string errorDetail,
        IEnumerable<string> payloadFields,
        IEnumerable<string> optionalFields,
        ISet<string> removedFields)
    {
        var payloadFieldSet = new HashSet<string>(
            payloadFields.Where(value => !string.IsNullOrWhiteSpace(value)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var optionalField in optionalFields.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (removedFields.Contains(optionalField))
                continue;

            if (!payloadFieldSet.Contains(optionalField))
                continue;

            if (errorDetail.Contains(optionalField, StringComparison.OrdinalIgnoreCase))
                return optionalField;
        }

        return null;
    }

    private static string BuildSalesPerformancePayloadSummary(IReadOnlyDictionary<string, object?> payload)
    {
        return string.Join(
            ", ",
            payload
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => $"{item.Key}={FormatPayloadValue(item.Value)}"));
    }

    private static string FormatPayloadValue(object? value)
    {
        if (value is null)
            return "null";

        return value switch
        {
            string text when text.Length > 80 => $"{text[..77]}...",
            string text => text,
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
    }

    private static string BuildMonthCloseErrorDetail(Exception ex)
    {
        var summary = SummarizeExceptionMessages(ex);
        return string.Join(" ", summary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ExtractActionableDataverseError(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "Error sin detalle.";

        var normalized = string.Join(" ", detail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var innerMarker = "InnerException :";
        var innerIndex = normalized.IndexOf(innerMarker, StringComparison.OrdinalIgnoreCase);
        if (innerIndex >= 0)
        {
            normalized = normalized[(innerIndex + innerMarker.Length)..].Trim();
        }

        var stackIndex = normalized.IndexOf(" at Microsoft.", StringComparison.OrdinalIgnoreCase);
        if (stackIndex > 0)
        {
            normalized = normalized[..stackIndex].Trim();
        }

        var odataIndex = normalized.LastIndexOf("Microsoft.OData.ODataException:", StringComparison.OrdinalIgnoreCase);
        if (odataIndex >= 0)
        {
            normalized = normalized[(odataIndex + "Microsoft.OData.ODataException:".Length)..].Trim();
        }

        return normalized.Length > 500 ? $"{normalized[..497]}..." : normalized;
    }

    private static string FormatDateTimeDisplay(DateTimeOffset? value)
    {
        if (!value.HasValue)
            return "";

        var local = value.Value;
        foreach (var timeZoneId in new[] { "SA Pacific Standard Time", "America/Bogota" })
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                local = TimeZoneInfo.ConvertTime(value.Value, zone);
                break;
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return local.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-CO"));
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string GetScoreClientGroupKey(ScoreRecordDto item)
    {
        if (!string.IsNullOrWhiteSpace(item.ClientId))
            return $"id:{item.ClientId}";

        return $"name:{item.ClientName}";
    }

    private static ScoreDescriptionParseResult ParseScoreDescription(string? rawDescription)
    {
        var result = new ScoreDescriptionParseResult();
        var raw = (rawDescription ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        var metadata = new StringBuilder(raw.Length);
        var cursor = 0;
        while (cursor < raw.Length)
        {
            var linesIndex = FindNextLinesLabel(raw, cursor);
            if (linesIndex < 0)
            {
                metadata.Append(raw, cursor, raw.Length - cursor);
                break;
            }

            metadata.Append(raw, cursor, linesIndex - cursor);
            var labelLength = raw.AsSpan(linesIndex).StartsWith("L\u00EDneas:", StringComparison.OrdinalIgnoreCase)
                ? "L\u00EDneas:".Length
                : "Lineas:".Length;

            var arrayStart = SkipWhitespace(raw, linesIndex + labelLength);
            if (arrayStart < raw.Length && raw[arrayStart] == '[')
            {
                var (jsonArray, nextIndex) = ExtractJsonArray(raw, arrayStart);
                if (!string.IsNullOrWhiteSpace(jsonArray))
                {
                    var parsedLines = DeserializeJsonOrDefault<List<RawScoreProductLine>>(jsonArray)
                        ?? new List<RawScoreProductLine>();
                    AppendParsedScoreProductLines(result, parsedLines);

                    cursor = nextIndex;
                    continue;
                }
            }

            if (TryExtractMarkdownProvisioningTable(raw, arrayStart, out var tableLines, out var tableNextIndex))
            {
                var parsedLines = ParseMarkdownProvisioningLines(tableLines);
                AppendParsedScoreProductLines(result, parsedLines);

                cursor = tableNextIndex;
                continue;
            }

            cursor = linesIndex + labelLength;
        }

        foreach (Match match in ExtendedScoreDescriptionFieldRegex.Matches(metadata.ToString()))
        {
            if (!match.Success)
                continue;

            var key = match.Groups["key"].Value.Trim();
            var value = match.Groups["value"].Value.Trim();
            if (string.IsNullOrWhiteSpace(value))
                continue;

            switch (NormalizeDescriptionKey(key))
            {
                case "cliente":
                    if (string.IsNullOrWhiteSpace(result.ClientName))
                        result.ClientName = value;
                    break;
                case "fechaaprovisionamiento":
                    if (!result.ProvisioningDate.HasValue && TryParseDateOnly(value, out var provisioningDate))
                        result.ProvisioningDate = provisioningDate;
                    break;
                case "tipocontrato":
                    if (string.IsNullOrWhiteSpace(result.ContractType))
                        result.ContractType = value;
                    if (string.IsNullOrWhiteSpace(result.DealTypeText) && TryResolveDealTypeValue(value, out _))
                        result.DealTypeText = value;
                    break;
                case "tiponegocio":
                    result.DealTypeText = value;
                    break;
                case "requiereprorrateo":
                case "requiereprorateo":
                    if (!result.RequiresProration.HasValue && TryResolveYesNoValue(value, out var requiresProration))
                        result.RequiresProration = requiresProration;
                    break;
                case "inicio":
                    if (!result.ScenarioStartDate.HasValue && TryParseDateOnly(value, out var scenarioStartDate))
                        result.ScenarioStartDate = scenarioStartDate;
                    break;
                case "final":
                    if (!result.ScenarioEndDate.HasValue && TryParseDateOnly(value, out var scenarioEndDate))
                        result.ScenarioEndDate = scenarioEndDate;
                    break;
                case "puntaje":
                    result.Score ??= ParseLooseDecimal(value);
                    break;
                case "comision":
                    result.Commission ??= ParseLooseDecimal(value);
                    break;
                case "businessid":
                    if (string.IsNullOrWhiteSpace(result.BusinessId))
                        result.BusinessId = value;
                    break;
                case "prorrateo":
                case "prorateo":
                    ApplyProrationMetadata(result, value);
                    break;
                case "ventamensualtotal":
                    result.TotalMonthlyValue ??= ParseLooseDecimal(value);
                    break;
                case "ventatotal":
                case "ventatotalanual":
                    result.TotalValue ??= ParseLooseDecimal(value);
                    break;
            }
        }

        result.ClientName = result.ClientName.Trim();
        result.ContractType = result.ContractType.Trim();
        result.DealTypeText = result.DealTypeText.Trim();
        result.BusinessId = result.BusinessId.Trim();
        result.ProrationText = result.ProrationText.Trim();
        return result;
    }

    private static ScoreProductLineDto ToScoreProductLine(RawScoreProductLine rawLine, int index)
    {
        var quantity = Math.Max(rawLine.Quantity, 0);
        var costUnit = RoundCurrency(rawLine.CostUnit ?? 0m);
        var marginPercent = RoundCurrency(rawLine.MarginPercent ?? 0m);
        var contractMonths = rawLine.ContractMonths.HasValue && rawLine.ContractMonths.Value > 0 ? rawLine.ContractMonths.Value : 12;
        var unitMonthlyValueRaw = rawLine.SaleUnit
            ?? rawLine.Number
            ?? (quantity > 0 ? rawLine.MonthlyValue / quantity : null)
            ?? 0m;
        var monthlyValueRaw = rawLine.MonthlyValue ?? (quantity * unitMonthlyValueRaw);
        var totalValueRaw = rawLine.TotalValue ?? (monthlyValueRaw * contractMonths);
        var productName = string.IsNullOrWhiteSpace(rawLine.ProductName) ? $"Producto {index}" : rawLine.ProductName.Trim();

        return new ScoreProductLineDto
        {
            LineId = rawLine.LineId?.Trim() ?? "",
            ProductId = rawLine.ProductId?.Trim() ?? "",
            ProductName = productName,
            LineType = ResolveLineTypeLabel(ResolveLineOptionValue(rawLine.LineType), rawLine.LineType),
            LineOptionValue = ResolveLineOptionValue(rawLine.LineType),
            HasVat = rawLine.HasVat == true,
            HasVatOptionValue = ResolveHasVatOptionValue(rawLine.HasVat == true),
            Quantity = quantity,
            CostUnit = costUnit,
            MarginPercent = marginPercent,
            ContractMonths = contractMonths,
            MonthlyUnitValue = RoundCurrency(unitMonthlyValueRaw),
            MonthlyValue = RoundCurrency(monthlyValueRaw),
            TotalValue = RoundCurrency(totalValueRaw),
            AnnualValue = RoundCurrency(totalValueRaw)
        };
    }

    private static void AppendParsedScoreProductLines(
        ScoreDescriptionParseResult result,
        IEnumerable<RawScoreProductLine> parsedLines)
    {
        foreach (var line in parsedLines)
        {
            if (!result.RequiresProration.HasValue && line.RequiresProration.HasValue)
                result.RequiresProration = line.RequiresProration.Value;

            if (!result.ScenarioStartDate.HasValue && TryParseDateOnly(line.StartDate, out var lineStartDate))
                result.ScenarioStartDate = lineStartDate;

            if (!result.ScenarioEndDate.HasValue && TryParseDateOnly(line.EndDate, out var lineEndDate))
                result.ScenarioEndDate = lineEndDate;

            result.ProductLines.Add(ToScoreProductLine(line, result.ProductLines.Count + 1));
        }
    }

    private static bool TryExtractMarkdownProvisioningTable(
        string raw,
        int startIndex,
        out List<string> tableLines,
        out int nextIndex)
    {
        tableLines = new List<string>();
        nextIndex = startIndex;
        var index = startIndex;

        while (index < raw.Length)
        {
            var lineStart = index;
            var lineEnd = raw.IndexOf('\n', index);
            if (lineEnd < 0)
                lineEnd = raw.Length;

            var line = raw[lineStart..lineEnd].TrimEnd('\r');
            var nextLineIndex = lineEnd < raw.Length ? lineEnd + 1 : lineEnd;
            if (string.IsNullOrWhiteSpace(line) && tableLines.Count == 0)
            {
                index = nextLineIndex;
                continue;
            }

            if (!line.TrimStart().StartsWith("|", StringComparison.Ordinal))
                break;

            tableLines.Add(line.Trim());
            index = nextLineIndex;
        }

        nextIndex = index;
        return tableLines.Count >= 3
            && SplitMarkdownProvisioningRow(tableLines[0]).Count > 0
            && IsMarkdownProvisioningSeparatorRow(tableLines[1]);
    }

    private static IReadOnlyList<RawScoreProductLine> ParseMarkdownProvisioningLines(IReadOnlyList<string> tableLines)
    {
        if (tableLines.Count < 3)
            return Array.Empty<RawScoreProductLine>();

        var headers = SplitMarkdownProvisioningRow(tableLines[0])
            .Select(NormalizeMarkdownProvisioningHeader)
            .ToList();
        var lines = new List<RawScoreProductLine>();

        foreach (var rowLine in tableLines.Skip(2))
        {
            if (IsMarkdownProvisioningSeparatorRow(rowLine))
                continue;

            var cells = SplitMarkdownProvisioningRow(rowLine);
            if (cells.Count == 0)
                continue;

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Count && index < cells.Count; index++)
            {
                var header = headers[index];
                if (string.IsNullOrWhiteSpace(header))
                    continue;

                row[header] = NormalizeMarkdownProvisioningCell(cells[index]);
            }

            var productName = GetMarkdownProvisioningValue(row, "producto", "productonombre");
            if (string.IsNullOrWhiteSpace(productName))
                continue;

            var quantity = ParseLooseDecimal(GetMarkdownProvisioningValue(row, "cant", "cantidad")) ?? 0m;
            var months = ParseLooseDecimal(GetMarkdownProvisioningValue(row, "meses", "duracionmeses", "duracion")) ?? 12m;
            var rawLine = new RawScoreProductLine
            {
                LineId = GetMarkdownProvisioningValue(row, "lineaid", "lineid"),
                ProductId = GetMarkdownProvisioningValue(row, "productoid", "idproducto"),
                ProductName = productName,
                LineType = GetMarkdownProvisioningValue(row, "tipo"),
                Quantity = (int)Math.Round(Math.Max(quantity, 0m), 0, MidpointRounding.AwayFromZero),
                CostUnit = ParseLooseDecimal(GetMarkdownProvisioningValue(row, "costound", "costo")),
                SaleUnit = ParseLooseDecimal(GetMarkdownProvisioningValue(row, "ventaund", "ventaunitaria")),
                MarginPercent = ParseLooseDecimal(GetMarkdownProvisioningValue(row, "margen", "margenporcentaje")),
                ContractMonths = (int)Math.Round(Math.Max(months, 0m), 0, MidpointRounding.AwayFromZero),
                MonthlyValue = ParseLooseDecimal(GetMarkdownProvisioningValue(row, "ventamensual", "mensual")),
                TotalValue = ParseLooseDecimal(GetMarkdownProvisioningValue(row, "ventatotal", "total")),
                StartDate = GetMarkdownProvisioningValue(row, "inicio"),
                EndDate = GetMarkdownProvisioningValue(row, "final")
            };

            var ivaText = GetMarkdownProvisioningValue(row, "iva", "tieneiva");
            if (TryResolveYesNoValue(ivaText, out var hasVat))
                rawLine.HasVat = hasVat;

            lines.Add(rawLine);
        }

        return lines;
    }

    private static List<string> SplitMarkdownProvisioningRow(string row)
    {
        var trimmed = row.Trim();
        if (trimmed.StartsWith("|", StringComparison.Ordinal))
            trimmed = trimmed[1..];
        if (trimmed.EndsWith("|", StringComparison.Ordinal))
            trimmed = trimmed[..^1];

        return trimmed
            .Split('|')
            .Select(static cell => cell.Trim())
            .ToList();
    }

    private static bool IsMarkdownProvisioningSeparatorRow(string row)
    {
        var cells = SplitMarkdownProvisioningRow(row);
        return cells.Count > 0
            && cells.All(static cell =>
            {
                var normalized = cell.Replace(":", "", StringComparison.Ordinal).Trim();
                return normalized.Length > 0 && normalized.All(static ch => ch == '-');
            });
    }

    private static string NormalizeMarkdownProvisioningHeader(string value)
    {
        var normalized = NormalizeDescriptionKey(value)
            .Replace(".", "", StringComparison.Ordinal)
            .Replace("%", "", StringComparison.Ordinal)
            .Replace("#", "numero", StringComparison.Ordinal);

        return normalized switch
        {
            "cant" => "cantidad",
            "costound" => "costound",
            "ventaund" => "ventaund",
            "margen" => "margen",
            "meses" => "meses",
            "lineaid" => "lineaid",
            "productoid" => "productoid",
            "idproducto" => "idproducto",
            _ => normalized
        };
    }

    private static string NormalizeMarkdownProvisioningCell(string value)
    {
        var normalized = value.Trim();
        return normalized is "-" or "\u2014" ? "" : normalized;
    }

    private static string GetMarkdownProvisioningValue(
        IReadOnlyDictionary<string, string> row,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "";
    }

    private static ScoreProductLineDto ToScoreProductLine(ScoreVerificationLineInput line, int index)
    {
        var normalized = NormalizeVerificationLineStatic(line, index);
        return new ScoreProductLineDto
        {
            LineId = normalized.LineId,
            ProductId = normalized.ProductId,
            ProductName = normalized.ProductName,
            LineType = normalized.LineType,
            LineOptionValue = normalized.LineOptionValue,
            HasVat = normalized.HasVat,
            HasVatOptionValue = normalized.HasVatOptionValue,
            Quantity = normalized.Quantity,
            CostUnit = normalized.CostUnit,
            MarginPercent = normalized.MarginPercent,
            ContractMonths = normalized.ContractMonths,
            MonthlyUnitValue = normalized.SaleUnit,
            MonthlyValue = normalized.MonthlyValue,
            TotalValue = normalized.TotalValue,
            AnnualValue = normalized.TotalValue
        };
    }

    private static ScoreVerificationLineInput NormalizeVerificationLineStatic(ScoreVerificationLineInput line, int index)
    {
        var costUnit = RoundCurrency(Math.Max(line.CostUnit, 0m));
        var marginPercent = RoundCurrency(line.MarginPercent);
        var productName = string.IsNullOrWhiteSpace(line.ProductName) ? $"Producto {index}" : line.ProductName.Trim();
        var contractMonths = NormalizeContractMonths(line.ContractMonths, productName);
        var quantity = line.Quantity > 0 ? line.Quantity : 1;
        var saleUnit = RoundCurrency(costUnit * (1m + (marginPercent / 100m)));
        var monthlyValue = RoundCurrency(saleUnit * quantity);
        var totalValue = RoundCurrency(monthlyValue * contractMonths);

        return new ScoreVerificationLineInput
        {
            LineId = string.IsNullOrWhiteSpace(line.LineId) ? $"line-{index}" : line.LineId.Trim(),
            ProductId = line.ProductId?.Trim() ?? "",
            ProductName = productName,
            LineType = ResolveLineTypeLabel(
                AllowedLineOptionValues.Contains(line.LineOptionValue) ? line.LineOptionValue : ResolveLineOptionValue(line.LineType),
                line.LineType),
            LineOptionValue = AllowedLineOptionValues.Contains(line.LineOptionValue)
                ? line.LineOptionValue
                : ResolveLineOptionValue(line.LineType),
            HasVat = line.HasVatOptionValue > 0 ? line.HasVatOptionValue == 1 : line.HasVat,
            HasVatOptionValue = line.HasVatOptionValue > 0 ? line.HasVatOptionValue : ResolveHasVatOptionValue(line.HasVat),
            CostUnit = costUnit,
            MarginPercent = marginPercent,
            ContractMonths = contractMonths,
            Quantity = quantity,
            SuggestedRetailPrice = RoundCurrency(line.SuggestedRetailPrice),
            Acelerador = RoundCurrency(line.Acelerador),
            SaleUnit = saleUnit,
            MonthlyValue = monthlyValue,
            TotalValue = totalValue
        };
    }

    private static void ApplyProrationMetadata(ScoreDescriptionParseResult result, string value)
    {
        var normalizedValue = value.Trim();
        if (string.IsNullOrWhiteSpace(result.ProrationText))
            result.ProrationText = normalizedValue;

        if (string.Equals(normalizedValue, "no", StringComparison.OrdinalIgnoreCase))
        {
            result.ProrationDays = 0;
            result.ProrationFactor = 1m;
            return;
        }

        var match = ProrationTextRegex.Match(normalizedValue);
        if (!match.Success)
            return;

        if (int.TryParse(match.Groups["days"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var prorationDays))
            result.ProrationDays = prorationDays;

        result.ProrationFactor = ParseLooseDecimal(match.Groups["factor"].Value) ?? result.ProrationFactor;
    }

    private static string NormalizeDescriptionKey(string key)
    {
        var normalized = key
            .Normalize(NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .ToArray();

        return new string(normalized)
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
    }

    private static int FindNextLinesLabel(string raw, int startIndex)
    {
        var accented = raw.IndexOf("L\u00EDneas:", startIndex, StringComparison.OrdinalIgnoreCase);
        var plain = raw.IndexOf("Lineas:", startIndex, StringComparison.OrdinalIgnoreCase);

        if (accented < 0)
            return plain;

        if (plain < 0)
            return accented;

        return Math.Min(accented, plain);
    }

    private static int SkipWhitespace(string raw, int startIndex)
    {
        var index = startIndex;
        while (index < raw.Length && char.IsWhiteSpace(raw[index]))
            index++;

        return index;
    }

    private static (string JsonArray, int NextIndex) ExtractJsonArray(string raw, int startIndex)
    {
        if (startIndex < 0 || startIndex >= raw.Length || raw[startIndex] != '[')
            return ("", startIndex);

        var depth = 0;
        var insideString = false;
        var escapeNext = false;

        for (var index = startIndex; index < raw.Length; index++)
        {
            var current = raw[index];
            if (insideString)
            {
                if (escapeNext)
                {
                    escapeNext = false;
                    continue;
                }

                if (current == '\\')
                {
                    escapeNext = true;
                    continue;
                }

                if (current == '"')
                    insideString = false;

                continue;
            }

            if (current == '"')
            {
                insideString = true;
                continue;
            }

            if (current == '[')
            {
                depth++;
                continue;
            }

            if (current != ']')
                continue;

            depth--;
            if (depth == 0)
                return (raw[startIndex..(index + 1)], index + 1);
        }

        return ("", startIndex);
    }

    private static decimal? ParseLooseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw
            .Trim()
            .Replace("$", "", StringComparison.Ordinal)
            .Replace("%", "", StringComparison.Ordinal)
            .Replace("COP", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariantValue))
            return invariantValue;

        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.GetCultureInfo("es-CO"), out var colombianValue))
            return colombianValue;

        var normalized = trimmed.Replace(" ", "");
        if (normalized.Contains(',') && normalized.Contains('.'))
        {
            var lastComma = normalized.LastIndexOf(',');
            var lastDot = normalized.LastIndexOf('.');
            normalized = lastComma > lastDot
                ? normalized.Replace(".", "").Replace(',', '.')
                : normalized.Replace(",", "");
        }
        else if (normalized.Contains(','))
        {
            normalized = normalized.Replace(',', '.');
        }

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var normalizedValue)
            ? normalizedValue
            : null;
    }

    private static PrimitiveFieldKind DetectPrimitiveFieldKind(JsonElement item, string logicalName)
    {
        if (!item.TryGetProperty(logicalName, out var property))
            return PrimitiveFieldKind.Unknown;

        return property.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False => PrimitiveFieldKind.Boolean,
            JsonValueKind.Number => PrimitiveFieldKind.Integer,
            JsonValueKind.String when bool.TryParse(property.GetString(), out _) => PrimitiveFieldKind.Boolean,
            JsonValueKind.String when int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _) => PrimitiveFieldKind.Integer,
            _ => PrimitiveFieldKind.Unknown
        };
    }

    private static bool ReadYesNoOption(JsonElement item, string logicalName)
    {
        var formatted = ReadString(item, $"{logicalName}{FormattedValueAnnotationSuffix}");
        if (string.Equals(formatted, "si", StringComparison.OrdinalIgnoreCase)
            || string.Equals(formatted, "sÃ­", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (item.TryGetProperty(logicalName, out var property))
        {
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numericValue))
                return numericValue == 1;

            if (property.ValueKind == JsonValueKind.String
                && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
            {
                return parsedValue == 1;
            }
        }

        return false;
    }

    private static int ReadOptionValue(JsonElement item, string logicalName)
    {
        if (!item.TryGetProperty(logicalName, out var property))
            return 0;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numericValue))
            return numericValue;

        if (property.ValueKind == JsonValueKind.String
            && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
        {
            return parsedValue;
        }

        return 0;
    }

    private static bool ReadYesNoOptionFlexible(JsonElement item, string logicalName)
    {
        var formatted = ReadString(item, $"{logicalName}{FormattedValueAnnotationSuffix}");
        if (string.Equals(formatted, "si", StringComparison.OrdinalIgnoreCase)
            || string.Equals(formatted, "sÃ­", StringComparison.OrdinalIgnoreCase)
            || string.Equals(formatted, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(formatted, "no", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!item.TryGetProperty(logicalName, out var property))
            return false;

        if (property.ValueKind == JsonValueKind.True)
            return true;

        if (property.ValueKind == JsonValueKind.False)
            return false;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numericValue))
            return numericValue == 1;

        if (property.ValueKind == JsonValueKind.String)
        {
            var rawValue = property.GetString();
            if (bool.TryParse(rawValue, out var parsedBool))
                return parsedBool;

            if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
                return parsedValue == 1;
        }

        return false;
    }

    private static int ReadBinaryOptionValue(JsonElement item, string logicalName, int trueValue = 1, int falseValue = 2)
    {
        var formatted = ReadString(item, $"{logicalName}{FormattedValueAnnotationSuffix}");
        if (string.Equals(formatted, "si", StringComparison.OrdinalIgnoreCase)
            || string.Equals(formatted, "sÃ­", StringComparison.OrdinalIgnoreCase)
            || string.Equals(formatted, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return trueValue;
        }

        if (string.Equals(formatted, "no", StringComparison.OrdinalIgnoreCase))
            return falseValue;

        if (!item.TryGetProperty(logicalName, out var property))
            return 0;

        if (property.ValueKind == JsonValueKind.True)
            return trueValue;

        if (property.ValueKind == JsonValueKind.False)
            return falseValue;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numericValue))
            return numericValue == 0 ? falseValue : numericValue;

        if (property.ValueKind == JsonValueKind.String)
        {
            var rawValue = property.GetString();
            if (bool.TryParse(rawValue, out var parsedBool))
                return parsedBool ? trueValue : falseValue;

            if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
                return parsedValue == 0 ? falseValue : parsedValue;
        }

        return 0;
    }

    private string ResolveOfferDownloadFileName(HttpResponseMessage response, string fallbackFileName, string recordId)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        if (!string.IsNullOrWhiteSpace(disposition?.FileNameStar))
            return disposition.FileNameStar.Trim('"');

        if (!string.IsNullOrWhiteSpace(disposition?.FileName))
            return disposition.FileName.Trim('"');

        if (response.Headers.TryGetValues("x-ms-file-name", out var fileNameValues))
        {
            var headerValue = fileNameValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerValue))
                return headerValue.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallbackFileName))
            return fallbackFileName;

        return $"oferta-{recordId}.bin";
    }

    private static string ReadDataverseDisplayValue(JsonElement item, string logicalName, params string[] fallbackTokens)
    {
        var formattedDirect = ReadString(item, $"{logicalName}{FormattedValueAnnotationSuffix}");
        if (!string.IsNullOrWhiteSpace(formattedDirect))
            return formattedDirect.Trim();

        var direct = ReadString(item, logicalName);
        if (!string.IsNullOrWhiteSpace(direct))
            return direct.Trim();

        foreach (var lookupProperty in GetLookupCandidateProperties(item, logicalName, fallbackTokens))
        {
            var formattedLookupValue = ReadLookupFormattedValue(item, lookupProperty);
            if (!string.IsNullOrWhiteSpace(formattedLookupValue))
                return formattedLookupValue.Trim();

            var rawLookupValue = ReadString(item, lookupProperty);
            if (!string.IsNullOrWhiteSpace(rawLookupValue))
                return rawLookupValue.Trim();
        }

        return "";
    }

    private static string ReadDataverseLookupId(JsonElement item, string logicalName, params string[] fallbackTokens)
    {
        foreach (var lookupProperty in GetLookupCandidateProperties(item, logicalName, fallbackTokens))
        {
            var rawLookupValue = ReadString(item, lookupProperty);
            if (!string.IsNullOrWhiteSpace(rawLookupValue))
                return rawLookupValue.Trim();
        }

        return "";
    }

    private static IEnumerable<string> GetLookupCandidateProperties(JsonElement item, string logicalName, params string[] fallbackTokens)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var trimmed = value.Trim();
            if (seen.Add(trimmed))
                results.Add(trimmed);
        }

        AddCandidate($"_{logicalName}_value");

        foreach (var property in item.EnumerateObject())
        {
            if (!property.Name.EndsWith("_value", StringComparison.OrdinalIgnoreCase))
                continue;

            if (property.Name.Contains(logicalName, StringComparison.OrdinalIgnoreCase)
                || fallbackTokens.Any(token => property.Name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                AddCandidate(property.Name);
            }
        }

        return results;
    }

    private sealed class ScoreRecordContext
    {
        public ScoreRecordDto Record { get; set; } = new();
        public ScoreDescriptionParseResult Description { get; set; } = new();
        public ScoreAdditionalDataSnapshot Additional { get; set; } = new();
    }

    private sealed class ScoreDescriptionParseResult
    {
        public string ClientName { get; set; } = "";
        public DateOnly? ProvisioningDate { get; set; }
        public string ContractType { get; set; } = "";
        public string DealTypeText { get; set; } = "";
        public bool? RequiresProration { get; set; }
        public DateOnly? ScenarioStartDate { get; set; }
        public DateOnly? ScenarioEndDate { get; set; }
        public decimal? Score { get; set; }
        public decimal? Commission { get; set; }
        public string BusinessId { get; set; } = "";
        public string ProrationText { get; set; } = "";
        public int ProrationDays { get; set; }
        public decimal ProrationFactor { get; set; }
        public decimal? TotalMonthlyValue { get; set; }
        public decimal? TotalValue { get; set; }
        public List<ScoreProductLineDto> ProductLines { get; } = new();
    }

    private sealed class ScoreAdditionalDataSnapshot
    {
        public int Version { get; set; }
        public string? BusinessId { get; set; } = "";
        public int? DealTypeValue { get; set; }
        public bool RequiresProration { get; set; }
        public string? ScenarioStartDateValue { get; set; } = "";
        public string? ScenarioEndDateValue { get; set; } = "";
        public int BillingDay { get; set; }
        public string? RenewalDateValue { get; set; } = "";
        public string? RenewalMode { get; set; } = "";
        public string? AlignmentDateValue { get; set; } = "";
        public int HasVatOptionValue { get; set; }
        public int AutoBillOptionValue { get; set; }
        public int ProductLineOptionValue { get; set; }
        public int ContractTypeOptionValue { get; set; }
        public int ContractKindOptionValue { get; set; }
        public List<ScoreVerificationLineInput> Lines { get; set; } = new();
        public ScoreVerificationComputedResultDto? LastResult { get; set; }
        public DateTimeOffset? VerifiedAt { get; set; }
        public string? VerifiedBy { get; set; } = "";
        public List<ScoreMonthlyClosureSnapshot> MonthlyClosures { get; set; } = new();
        public DateTimeOffset? LastClosedAt { get; set; }
        public string? LastClosedBy { get; set; } = "";
    }

    private sealed class ScoreMonthlyClosureSnapshot
    {
        public string PeriodKey { get; set; } = "";
        public string BatchId { get; set; } = "";
        public DateTimeOffset ClosedAt { get; set; }
        public string ClosedBy { get; set; } = "";
        public List<ScoreMonthlyClosureLineSnapshot> Lines { get; set; } = new();
    }

    private sealed class ScoreMonthlyClosureLineSnapshot
    {
        public string LineKey { get; set; } = "";
        public string LineId { get; set; } = "";
        public string ProductId { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string Status { get; set; } = "";
        public string SalesPerformanceRecordId { get; set; } = "";
        public int PreviousQuantity { get; set; }
        public int AppliedQuantity { get; set; }
        public int FinalQuantity { get; set; }
        public List<string> Warnings { get; set; } = new();
    }

    private sealed class ScoreComputationContext
    {
        public int DealTypeValue { get; set; }
        public bool RequiresProration { get; set; }
        public string StartDateValue { get; set; } = "";
        public string EndDateValue { get; set; } = "";
        public List<ScoreVerificationLineInput> Lines { get; set; } = new();
        public ScoreVerificationComputedResultDto Result { get; set; } = new();
    }

    private enum PrimitiveFieldKind
    {
        Unknown = 0,
        Boolean = 1,
        Integer = 2
    }

    private sealed class SalesPerformanceCompactRecord
    {
        public string RecordId { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string ProductId { get; set; } = "";
        public int Quantity { get; set; }
        public decimal UnitSaleUsd { get; set; }
    }

    private sealed class SalesPerformanceCreateResult
    {
        public string RecordId { get; set; } = "";
        public string FinalState { get; set; } = "";
        public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    }

    private sealed class ScoreMonthClosePlan
    {
        public ScoreMonthInfo MonthInfo { get; set; } = new(false, "", "");
        public List<ScoreMonthCloseRecordPlan> Records { get; set; } = new();
        public Dictionary<string, List<SalesPerformanceCompactRecord>> SalesPerformanceCache { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ScoreMonthCloseRecordPlan
    {
        public ScoreRecordContext Context { get; set; } = new();
        public ScoreRecordDto Record { get; set; } = new();
        public ScoreVerificationDetailDto Detail { get; set; } = new();
        public List<ScoreMonthCloseLinePlan> Lines { get; set; } = new();
    }

    private sealed class ScoreMonthCloseLinePlan
    {
        public ScoreRecordContext Context { get; set; } = new();
        public ScoreRecordDto Record { get; set; } = new();
        public ScoreVerificationDetailDto Detail { get; set; } = new();
        public ScoreVerificationLineInput Line { get; set; } = new();
        public string LineKey { get; set; } = "";
        public SalesPerformanceCompactRecord? ExistingMatch { get; set; }
        public ScoreMonthlyClosureLineSnapshot? ExistingClosure { get; set; }
        public bool IsAlreadyClosed { get; set; }
        public bool IsRenewalContract { get; set; }
        public bool SelectedByDefault { get; set; }
        public bool CanChangeSelection { get; set; }
        public string Reason { get; set; } = "";
        public string PredictedAction { get; set; } = "";
        public List<string> Warnings { get; set; } = new();
    }

    private sealed class RawScoreProductLine
    {
        [JsonPropertyName("lineId")]
        public string? LineId { get; set; }

        [JsonPropertyName("productoId")]
        public string? ProductId { get; set; }

        [JsonPropertyName("productoNombre")]
        public string? ProductName { get; set; }

        [JsonPropertyName("cantidad")]
        public int Quantity { get; set; }

        [JsonPropertyName("number")]
        public decimal? Number { get; set; }

        [JsonPropertyName("costoUnd")]
        public decimal? CostUnit { get; set; }

        [JsonPropertyName("ventaUnd")]
        public decimal? SaleUnit { get; set; }

        [JsonPropertyName("margenPorcentaje")]
        public decimal? MarginPercent { get; set; }

        [JsonPropertyName("duracionMeses")]
        public int? ContractMonths { get; set; }

        [JsonPropertyName("ventaMensual")]
        public decimal? MonthlyValue { get; set; }

        [JsonPropertyName("ventaTotal")]
        public decimal? TotalValue { get; set; }

        [JsonPropertyName("tieneIva")]
        public bool? HasVat { get; set; }

        [JsonPropertyName("tipo")]
        public string? LineType { get; set; }

        [JsonPropertyName("requiereProrrateo")]
        public bool? RequiresProration { get; set; }

        [JsonPropertyName("inicio")]
        public string? StartDate { get; set; }

        [JsonPropertyName("final")]
        public string? EndDate { get; set; }
    }

    private sealed record ScoreMonthInfo(bool SupportsClose, string PeriodKey, string PeriodLabel);
}
