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
        "(?<key>Cliente|Fecha aprovisionamiento|Tipo contrato|Puntaje|Comisi(?:\\u00F3|o)n|BusinessId|Prorrateo|Prorateo|Venta mensual total|Venta total anual|Venta total)\\s*:\\s*(?<value>.*?)(?=(Cliente|Fecha aprovisionamiento|Tipo contrato|Puntaje|Comisi(?:\\u00F3|o)n|BusinessId|Prorrateo|Prorateo|Venta mensual total|Venta total anual|Venta total|L(?:\\u00ED|i)neas)\\s*:|$)",
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

        var records = rawRecords
            .Select(item => ParseScoreRecordContext(item, monthInfo.PeriodKey))
            .Where(item => item is not null)
            .Select(item => item!.Record)
            .OrderBy(item => item.ContractStartDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Offer, StringComparer.OrdinalIgnoreCase)
            .ToList();

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

        var computation = BuildScoreComputationContext(normalizedRequest, requireProductLookup: true);
        var additional = BuildAdditionalSnapshot(normalizedRequest, computation, existingContext.Additional, currentUser);
        var additionalJson = JsonSerializer.Serialize(additional);

        var updateUrl = $"/api/data/v9.2/{_scoresTableSetName}({normalizedRecordId})";
        Exception? lastError = null;
        foreach (var payload in BuildVerificationPayloadCandidates(normalizedRequest, computation.Result, additionalJson))
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
                lastError = ex;
            }
        }

        throw new InvalidOperationException("No se pudo guardar la verificacion en Dataverse.", lastError);
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
                    logs.Add(BuildMonthCloseLog("error", context.Record.RecordId, context.Record.ClientName, line.ProductName, CompactMonthCloseError(ex.Message)));
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

        var rawDescription = ReadString(item, _scoresDescriptionField);
        var parsedDescription = ParseScoreDescription(rawDescription);
        var additional = DeserializeJsonOrDefault<ScoreAdditionalDataSnapshot>(ReadString(item, _scoresAdditionalField)) ?? new ScoreAdditionalDataSnapshot();
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
        var offer = ReadDataverseDisplayValue(item, _scoresOfferField, "oferta");
        var isVerified = ReadYesNoOption(item, _scoresVerifiedField);
        var monthlyValue = RoundCurrency(additional.LastResult?.TotalMonthlySale ?? parsedDescription.TotalMonthlyValue ?? productLines.Sum(line => line.MonthlyValue));
        var totalValue = RoundCurrency(additional.LastResult?.TotalSale ?? parsedDescription.TotalValue ?? productLines.Sum(line => line.TotalValue));
        var renewalDate = ParseAdditionalDateOnly(additional.RenewalDateValue);
        var alignmentDate = ParseAdditionalDateOnly(additional.AlignmentDateValue);
        var lastClosure = ResolveLastClosure(additional);

        return new ScoreRecordContext
        {
            Record = new ScoreRecordDto
            {
                RecordId = recordId,
                ClientId = clientId,
                ClientName = clientName,
                ContractStartDateValue = contractStartDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ContractStartDateDisplay = contractStartDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                Score = score,
                Commission = commission,
                SalesPerson = string.IsNullOrWhiteSpace(salesPerson) ? "Sin vendedor" : salesPerson.Trim(),
                Offer = string.IsNullOrWhiteSpace(offer) ? "Sin oferta" : offer.Trim(),
                OfferFileName = offer.Trim(),
                HasOffer = !string.IsNullOrWhiteSpace(offer),
                IsVerified = isVerified,
                FirstContractOptionValue = ReadOptionValue(item, _scoresFirstContractField),
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
                HasVatOptionValue = additional.HasVatOptionValue,
                AutoBillOptionValue = additional.AutoBillOptionValue,
                ProductLineOptionValue = additional.ProductLineOptionValue,
                ContractTypeOptionValue = additional.ContractTypeOptionValue,
                DealTypeValue = additional.DealTypeValue,
                RequiresProration = additional.RequiresProration,
                ScenarioStartDateValue = additional.ScenarioStartDateValue ?? "",
                ScenarioEndDateValue = additional.ScenarioEndDateValue ?? "",
                IsClosedForActivePeriod = HasMonthlyClosure(additional, activePeriodKey),
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
        var detail = new ScoreVerificationDetailDto
        {
            RecordId = record.RecordId,
            BusinessId = !string.IsNullOrWhiteSpace(record.BusinessId) ? record.BusinessId : scenario?.ScenarioId ?? "",
            DealTypeValue = ResolveDealTypeValue(record, additional, scenario),
            RequiresProration = ResolveRequiresProration(record, additional, scenario),
            ScenarioStartDateValue = ResolveScenarioStartDateValue(record, additional, scenario),
            ScenarioEndDateValue = ResolveScenarioEndDateValue(record, additional, scenario),
            FirstContractOptionValue = record.FirstContractOptionValue,
            LineOptionValue = record.LineOptionValue,
            VerticalOptionValue = record.VerticalOptionValue,
            BillingDay = record.BillingDay,
            RenewalDateValue = ResolveRenewalDateValue(record, additional, scenario),
            AlignmentDateValue = !string.IsNullOrWhiteSpace(record.AlignmentDateValue) ? record.AlignmentDateValue : FirstNonEmpty(additional.AlignmentDateValue, scenario?.EndDate),
            HasVatOptionValue = record.HasVatOptionValue,
            AutoBillOptionValue = record.AutoBillOptionValue,
            ProductLineOptionValue = record.ProductLineOptionValue,
            ContractTypeOptionValue = record.ContractTypeOptionValue,
            Lines = ResolveVerificationLines(record, additional, scenario),
            ClientId = record.ClientId,
            ClientName = record.ClientName,
            SalesPerson = record.SalesPerson,
            Offer = record.Offer,
            OfferFileName = record.OfferFileName,
            HasOffer = record.HasOffer,
            IsVerified = record.IsVerified,
            ContractStartDateValue = record.ContractStartDateValue,
            ContractStartDateDisplay = record.ContractStartDateDisplay,
            ProvisioningDateValue = record.ProvisioningDateValue,
            ProvisioningDateDisplay = record.ProvisioningDateDisplay,
            ContractTypeLabel = record.ContractType,
            ProrationSummary = record.ProrationText,
            IsClosedForActivePeriod = record.IsClosedForActivePeriod,
            ActivePeriodKey = record.ActivePeriodKey,
            LastVerifiedAtDisplay = record.LastVerifiedAtDisplay,
            LastVerifiedBy = record.LastVerifiedBy,
            LastClosedAtDisplay = record.LastClosedAtDisplay,
            LastClosedBy = record.LastClosedBy
        };

        detail.BillingDay = ResolveBillingDayForRequest(detail.BillingDay, detail.AutoBillOptionValue, detail.RenewalDateValue, detail.AlignmentDateValue, detail.ScenarioEndDateValue, detail.ContractStartDateValue);

        try
        {
            var computation = BuildScoreComputationContext(detail, requireProductLookup: false);
            detail.Lines = computation.Lines;
            detail.Result = computation.Result;
            detail.DealTypeValue = computation.DealTypeValue;
            detail.RequiresProration = computation.RequiresProration;
            detail.ScenarioStartDateValue = computation.StartDateValue;
            detail.ScenarioEndDateValue = computation.EndDateValue;
        }
        catch (InvalidOperationException ex)
        {
            detail.WarningMessage = $"El registro tiene datos pendientes antes de recalcular. {ex.Message}";
            detail.Result = null;
        }

        return detail;
    }

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
                .Select((line, index) => NormalizeVerificationLine(new ScoreVerificationLineInput
                {
                    LineId = string.IsNullOrWhiteSpace(line.ProductId) ? $"line-{index + 1}" : line.ProductId,
                    ProductId = line.ProductId,
                    ProductName = line.ProductDescription,
                    CostUnit = line.CostUnit,
                    MarginPercent = line.MarginPercent,
                    ContractMonths = line.ContractMonths,
                    Quantity = line.Quantity,
                    SuggestedRetailPrice = line.SuggestedRetailPrice,
                    Acelerador = line.Acelerador
                }, index + 1))
                .ToList();
        }

        return record.ProductLines
            .Select((line, index) => NormalizeVerificationLine(new ScoreVerificationLineInput
            {
                LineId = string.IsNullOrWhiteSpace(line.LineId) ? $"line-{index + 1}" : line.LineId,
                ProductId = line.ProductId,
                ProductName = line.ProductName,
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
            FirstNonEmpty(request.AlignmentDateValue, request.ScenarioEndDateValue),
            request.RequiresProration,
            "Debes indicar la fecha de alineacion para recalcular un negocio prorrateado.");

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
                BusinessType = BusinessType.Otro,
                ProductId = line.ProductId,
                ProductDescription = line.ProductName,
                CostUnit = line.CostUnit,
                MarginPercent = line.MarginPercent,
                ContractMonths = line.ContractMonths,
                Quantity = line.Quantity,
                SuggestedRetailPrice = line.SuggestedRetailPrice,
                Acelerador = line.Acelerador
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

        if (!AllowedFirstContractOptionValues.Contains(request.FirstContractOptionValue))
            throw new InvalidOperationException("La opcion de primer contrato no es valida.");

        if (!AllowedLineOptionValues.Contains(request.LineOptionValue))
            throw new InvalidOperationException("La linea seleccionada no es valida.");

        if (!AllowedVerticalOptionValues.Contains(request.VerticalOptionValue))
            throw new InvalidOperationException("La vertical seleccionada no es valida.");

        if (!AllowedBinaryOptionValues.Contains(request.HasVatOptionValue))
            throw new InvalidOperationException("La opcion de IVA no es valida.");

        if (!AllowedBinaryOptionValues.Contains(request.AutoBillOptionValue))
            throw new InvalidOperationException("La opcion de facturable automatico no es valida.");

        if (!AllowedProductLineOptionValues.Contains(request.ProductLineOptionValue))
            throw new InvalidOperationException("La linea del resumen mensual no es valida.");

        if (!AllowedContractTypeOptionValues.Contains(request.ContractTypeOptionValue))
            throw new InvalidOperationException("El tipo de contrato del resumen mensual no es valido.");

        if (request.BillingDay is < 0 or > 31)
            throw new InvalidOperationException("El dia de facturacion debe estar entre 1 y 31.");

        if (request.Lines is null || request.Lines.Count == 0)
            throw new InvalidOperationException("Debes incluir al menos una linea de negocio.");

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
        var contractMonths = line.ContractMonths > 0 ? line.ContractMonths : 12;
        var quantity = line.Quantity > 0 ? line.Quantity : 1;
        var saleUnit = RoundCurrency(costUnit * (1m + (marginPercent / 100m)));
        var monthlyValue = RoundCurrency(saleUnit * quantity);
        var totalValue = RoundCurrency(monthlyValue * contractMonths);

        return new ScoreVerificationLineInput
        {
            LineId = string.IsNullOrWhiteSpace(line.LineId) ? $"line-{index}" : line.LineId.Trim(),
            ProductId = line.ProductId?.Trim() ?? "",
            ProductName = string.IsNullOrWhiteSpace(line.ProductName) ? $"Producto {index}" : line.ProductName.Trim(),
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
        existing.AlignmentDateValue = request.AlignmentDateValue?.Trim() ?? "";
        existing.HasVatOptionValue = request.HasVatOptionValue;
        existing.AutoBillOptionValue = request.AutoBillOptionValue;
        existing.ProductLineOptionValue = request.ProductLineOptionValue;
        existing.ContractTypeOptionValue = request.ContractTypeOptionValue;
        existing.Lines = computation.Lines;
        existing.LastResult = computation.Result;
        existing.VerifiedAt = DateTimeOffset.UtcNow;
        existing.VerifiedBy = ResolveUserDisplayName(currentUser);
        return existing;
    }

    private IEnumerable<Dictionary<string, object?>> BuildVerificationPayloadCandidates(
        ScoreVerificationRequest request,
        ScoreVerificationComputedResultDto result,
        string additionalJson)
    {
        Dictionary<string, object?> BuildBasePayload(object verifiedValue) => new()
        {
            [_scoresFirstContractField] = request.FirstContractOptionValue,
            [_scoresLineField] = request.LineOptionValue,
            [_scoresVerticalField] = request.VerticalOptionValue,
            [_scoresScoreField] = result.Points,
            [_scoresCommissionField] = result.Commission,
            [_scoresAdditionalField] = additionalJson,
            [_scoresVerifiedField] = verifiedValue
        };

        yield return BuildBasePayload(1);
    }

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
                alignmentDate = request.AlignmentDateValue?.Trim() ?? "",
                billingDay = request.BillingDay,
                firstContractOptionValue = request.FirstContractOptionValue,
                lineOptionValue = request.LineOptionValue,
                verticalOptionValue = request.VerticalOptionValue,
                hasVatOptionValue = request.HasVatOptionValue,
                autoBillOptionValue = request.AutoBillOptionValue,
                productLineOptionValue = request.ProductLineOptionValue,
                contractTypeOptionValue = request.ContractTypeOptionValue,
                dealTypeValue = request.DealTypeValue,
                requiresProration = request.RequiresProration
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
                var select = string.Join(",", new[]
                {
                    _salesPerformanceIdField,
                    DefaultSalesPerformanceQuantityField,
                    DefaultSalesPerformanceUnitSaleUsdField
                });
                var relativeUrl = $"/api/data/v9.2/{_salesPerformanceTableSetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}";
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
            Quantity = ReadIntFlexible(item, DefaultSalesPerformanceQuantityField)
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

    private async Task CreateSalesPerformanceRecordAsync(
        ScoreRecordDto record,
        ScoreVerificationDetailDto detail,
        ScoreVerificationLineInput line,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        var clientId = NormalizeGuid(record.ClientId, nameof(record.ClientId));
        var productId = NormalizeGuid(line.ProductId, nameof(line.ProductId));
        var createName = BuildSalesPerformanceName(record, line);
        var renewalDateValue = ResolveSalesPerformanceRenewalDate(detail);
        var billingDay = detail.BillingDay > 0 ? detail.BillingDay : DeriveBillingDay(detail.RenewalDateValue, detail.AlignmentDateValue);

        var basePayload = new Dictionary<string, object?>
        {
            [_salesPerformancePrimaryNameField] = createName,
            [DefaultSalesPerformanceQuantityField] = line.Quantity,
            [DefaultSalesPerformanceUnitSaleUsdField] = line.SaleUnit,
            [_salesPerformanceHasVatField] = detail.HasVatOptionValue,
            [_salesPerformanceAutoBillField] = detail.AutoBillOptionValue,
            [_salesPerformanceProductLineField] = detail.ProductLineOptionValue,
            [_salesPerformanceContractTypeField] = detail.ContractTypeOptionValue
        };

        if (detail.AutoBillOptionValue == 1 && billingDay > 0)
            basePayload[_salesPerformanceBillingDayField] = billingDay;

        if (!string.IsNullOrWhiteSpace(renewalDateValue))
            basePayload[_salesPerformanceRenewalDateField] = renewalDateValue;

        var clientLookupCandidates = BuildLookupLogicalNameCandidates(
            _salesPerformanceClientLookupLogicalName,
            DeriveLookupLogicalName(_salesPerformanceClientLookupFilterField),
            DefaultSalesPerformanceClientCreateLookupLogicalName,
            DefaultSalesPerformanceClientLookupLogicalName,
            "cr07a_clienteid",
            "cr07a_clientelookup");
        var productLookupCandidates = BuildLookupLogicalNameCandidates(
            _salesPerformanceProductLookupLogicalName,
            DefaultSalesPerformanceProductLookupLogicalName,
            "cr07a_producto");

        Exception? lastError = null;
        foreach (var clientLookupLogicalName in clientLookupCandidates)
        {
            foreach (var productLookupLogicalName in productLookupCandidates)
            {
                var payload = new Dictionary<string, object?>(basePayload)
                {
                    [$"{clientLookupLogicalName}@odata.bind"] = $"/{ClientsEntitySetName}({clientId})",
                    [$"{productLookupLogicalName}@odata.bind"] = $"/{ProductsEntitySetName}({productId})"
                };

                try
                {
                    await CallDataverseSendAsync($"/api/data/v9.2/{_salesPerformanceTableSetName}", "POST", payload, user, ct);
                    return;
                }
                catch (InvalidOperationException ex)
                {
                    lastError = ex;
                }
            }
        }

        throw new InvalidOperationException("No se pudo crear la linea en cr07a_salesperformancerecord.", lastError);
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
            [_scoresAdditionalField] = JsonSerializer.Serialize(additional)
        };
        var updateUrl = $"/api/data/v9.2/{_scoresTableSetName}({normalizedRecordId})";
        await CallDataverseSendAsync(updateUrl, "PATCH", payload, user, ct);
    }

    private static ScoreMonthCloseLogEntryDto BuildMonthCloseLog(string level, string recordId, string clientName, string productName, string message) =>
        new()
        {
            Level = level,
            RecordId = recordId,
            ClientName = clientName,
            ProductName = productName,
            Message = message
        };

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
        if (detail.RequiresProration)
            return FirstNonEmpty(detail.AlignmentDateValue, detail.ScenarioEndDateValue, detail.RenewalDateValue);

        return FirstNonEmpty(detail.RenewalDateValue, detail.AlignmentDateValue, detail.ScenarioEndDateValue);
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

    private static bool HasMonthlyClosure(ScoreAdditionalDataSnapshot additional, string? activePeriodKey)
    {
        if (string.IsNullOrWhiteSpace(activePeriodKey) || additional.MonthlyClosures.Count == 0)
            return false;

        return additional.MonthlyClosures.Any(item =>
            string.Equals(item.PeriodKey, activePeriodKey, StringComparison.OrdinalIgnoreCase));
    }

    private static ScoreMonthlyClosureSnapshot? ResolveLastClosure(ScoreAdditionalDataSnapshot additional)
    {
        if (additional.MonthlyClosures.Count == 0)
            return null;

        return additional.MonthlyClosures
            .OrderByDescending(item => item.ClosedAt)
            .FirstOrDefault();
    }

    private static void NormalizeAdditionalSnapshot(ScoreAdditionalDataSnapshot additional)
    {
        additional.BusinessId ??= "";
        additional.ScenarioStartDateValue ??= "";
        additional.ScenarioEndDateValue ??= "";
        additional.RenewalDateValue ??= "";
        additional.AlignmentDateValue ??= "";
        additional.VerifiedBy ??= "";
        additional.LastClosedBy ??= "";
        additional.Lines ??= new List<ScoreVerificationLineInput>();
        additional.MonthlyClosures ??= new List<ScoreMonthlyClosureSnapshot>();
    }

    private static int ResolveDealTypeValue(ScoreRecordDto record, ScoreAdditionalDataSnapshot additional, ScenarioStoredDto? scenario)
    {
        if (additional.DealTypeValue >= 0 && Enum.IsDefined(typeof(DealType), additional.DealTypeValue))
            return additional.DealTypeValue;

        if (scenario is not null && Enum.IsDefined(typeof(DealType), scenario.DealType))
            return scenario.DealType;

        return record.ProrationDays > 0 ? (int)DealType.CrossSale : (int)DealType.ClienteNuevo;
    }

    private static bool ResolveRequiresProration(ScoreRecordDto record, ScoreAdditionalDataSnapshot additional, ScenarioStoredDto? scenario)
    {
        if (additional.RequiresProration)
            return true;

        if (scenario?.RequiresProration == true)
            return true;

        return record.ProrationDays > 0 && record.ProrationFactor is > 0m and < 1m;
    }

    private static string ResolveScenarioStartDateValue(ScoreRecordDto record, ScoreAdditionalDataSnapshot additional, ScenarioStoredDto? scenario) =>
        FirstNonEmpty(additional.ScenarioStartDateValue, scenario?.StartDate, record.ContractStartDateValue, record.ProvisioningDateValue);

    private static string ResolveScenarioEndDateValue(ScoreRecordDto record, ScoreAdditionalDataSnapshot additional, ScenarioStoredDto? scenario) =>
        FirstNonEmpty(additional.ScenarioEndDateValue, additional.AlignmentDateValue, scenario?.EndDate, record.AlignmentDateValue, record.RenewalDateValue);

    private static string BuildProrationText(int days, decimal factor) =>
        (days > 0 && factor is > 0m and < 1m)
            ? $"{days} dias ({factor:0.0000})"
            : "No";

    private static ScoreVerificationRequest NormalizeVerificationRequest(ScoreVerificationRequest request, string? contractStartDateValue)
    {
        var renewalDateValue = string.IsNullOrWhiteSpace(request.RenewalDateValue)
            ? BuildRenewalDateOneYearAfter(contractStartDateValue)
            : request.RenewalDateValue.Trim();
        var alignmentDateValue = request.AlignmentDateValue?.Trim() ?? "";
        var scenarioEndDateValue = request.ScenarioEndDateValue?.Trim() ?? "";

        return new ScoreVerificationRequest
        {
            RecordId = request.RecordId?.Trim() ?? "",
            BusinessId = request.BusinessId?.Trim() ?? "",
            DealTypeValue = request.DealTypeValue,
            RequiresProration = request.RequiresProration,
            ScenarioStartDateValue = request.ScenarioStartDateValue?.Trim() ?? "",
            ScenarioEndDateValue = scenarioEndDateValue,
            FirstContractOptionValue = request.FirstContractOptionValue,
            LineOptionValue = request.LineOptionValue,
            VerticalOptionValue = request.VerticalOptionValue,
            BillingDay = ResolveBillingDayForRequest(request.BillingDay, request.AutoBillOptionValue, renewalDateValue, alignmentDateValue, scenarioEndDateValue, contractStartDateValue),
            RenewalDateValue = renewalDateValue,
            AlignmentDateValue = alignmentDateValue,
            HasVatOptionValue = request.HasVatOptionValue,
            AutoBillOptionValue = request.AutoBillOptionValue,
            ProductLineOptionValue = request.ProductLineOptionValue,
            ContractTypeOptionValue = request.ContractTypeOptionValue,
            Lines = (request.Lines ?? new List<ScoreVerificationLineInput>())
                .Select(line => new ScoreVerificationLineInput
                {
                    LineId = line.LineId,
                    ProductId = line.ProductId,
                    ProductName = line.ProductName,
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

    private static string ResolveRenewalDateValue(ScoreRecordDto record, ScoreAdditionalDataSnapshot additional, ScenarioStoredDto? scenario) =>
        FirstNonEmpty(
            record.RenewalDateValue,
            additional.RenewalDateValue,
            BuildRenewalDateOneYearAfter(record.ContractStartDateValue),
            scenario?.EndDate);

    private static string BuildRenewalDateOneYearAfter(string? baseDateValue)
    {
        if (!TryParseDateOnly(baseDateValue, out var parsedDate))
            return "";

        return parsedDate.AddYears(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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
                    foreach (var line in parsedLines)
                    {
                        result.ProductLines.Add(ToScoreProductLine(line, result.ProductLines.Count + 1));
                    }

                    cursor = nextIndex;
                    continue;
                }
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

    private static ScoreProductLineDto ToScoreProductLine(ScoreVerificationLineInput line, int index)
    {
        var normalized = NormalizeVerificationLineStatic(line, index);
        return new ScoreProductLineDto
        {
            LineId = normalized.LineId,
            ProductId = normalized.ProductId,
            ProductName = normalized.ProductName,
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
        var contractMonths = line.ContractMonths > 0 ? line.ContractMonths : 12;
        var quantity = line.Quantity > 0 ? line.Quantity : 1;
        var saleUnit = RoundCurrency(costUnit * (1m + (marginPercent / 100m)));
        var monthlyValue = RoundCurrency(saleUnit * quantity);
        var totalValue = RoundCurrency(monthlyValue * contractMonths);

        return new ScoreVerificationLineInput
        {
            LineId = string.IsNullOrWhiteSpace(line.LineId) ? $"line-{index}" : line.LineId.Trim(),
            ProductId = line.ProductId?.Trim() ?? "",
            ProductName = string.IsNullOrWhiteSpace(line.ProductName) ? $"Producto {index}" : line.ProductName.Trim(),
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

        var trimmed = raw.Trim();
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
        public int Version { get; set; } = 1;
        public string? BusinessId { get; set; } = "";
        public int DealTypeValue { get; set; }
        public bool RequiresProration { get; set; }
        public string? ScenarioStartDateValue { get; set; } = "";
        public string? ScenarioEndDateValue { get; set; } = "";
        public int BillingDay { get; set; }
        public string? RenewalDateValue { get; set; } = "";
        public string? AlignmentDateValue { get; set; } = "";
        public int HasVatOptionValue { get; set; }
        public int AutoBillOptionValue { get; set; }
        public int ProductLineOptionValue { get; set; }
        public int ContractTypeOptionValue { get; set; }
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
        public DateTimeOffset ClosedAt { get; set; }
        public string ClosedBy { get; set; } = "";
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

    private sealed class SalesPerformanceCompactRecord
    {
        public string RecordId { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string ProductId { get; set; } = "";
        public int Quantity { get; set; }
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
    }

    private sealed record ScoreMonthInfo(bool SupportsClose, string PeriodKey, string PeriodLabel);
}
