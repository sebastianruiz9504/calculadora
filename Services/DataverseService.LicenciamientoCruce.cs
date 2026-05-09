using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Licenciamiento;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string LicenciamientoCruceStatusExact = "Match exacto";
    private const string LicenciamientoCruceStatusProbable = "Match probable";
    private const string LicenciamientoCruceStatusCostOnly = "Costo sin facturacion";
    private const string LicenciamientoCruceStatusBillingOnly = "Facturacion sin costo";
    private const string LicenciamientoCruceMonthlyKey = "monthly";
    private const string LicenciamientoCruceMonthlyLabel = "Monthly";
    private const string LicenciamientoCruceOneTimeKey = "onetime";
    private const string LicenciamientoCruceOneTimeLabel = "Prepaid";
    private const string LicenciamientoCruceAllKey = "all";
    private const string LicenciamientoCruceAllLabel = "Todo";
    private const string LicenciamientoCruceOtherKey = "otros";
    private const string LicenciamientoCruceOtherLabel = "Sin tipo";
    private const string LicenciamientoCruceAccountMapLogicalName = "cr07a_licenciamientoaccountmap";
    private const string LicenciamientoCruceAccountMapFallbackEntitySetName = "cr07a_licenciamientoaccountmaps";
    private const string LicenciamientoCruceAccountMapFallbackIdField = "cr07a_licenciamientoaccountmapid";
    private const string LicenciamientoCruceAccountMapPrimaryNameField = "cr07a_name";
    private const string LicenciamientoCruceAccountMapSourceAccountIdField = "cr07a_sourceaccountid";
    private const string LicenciamientoCruceAccountMapSourceAccountNameField = "cr07a_sourceaccountname";
    private const string LicenciamientoCruceAccountMapSourceClientNameField = "cr07a_sourceclientname";
    private const string LicenciamientoCruceAccountMapTargetAccountIdField = "cr07a_targetaccountid";
    private const string LicenciamientoCruceAccountMapTargetAccountNameField = "cr07a_targetaccountname";
    private const string LicenciamientoCruceAccountMapTargetClientIdField = "cr07a_targetclientid";
    private const string LicenciamientoCruceAccountMapTargetClientNameField = "cr07a_targetclientname";
    private const string LicenciamientoCruceAccountMapActiveField = "cr07a_active";
    private const string LicenciamientoCruceAccountMapNotesField = "cr07a_notes";
    private const int LicenciamientoCruceBillingMonthlyOption = 645250000;
    private const int LicenciamientoCruceBillingOneTimeOption = 645250001;
    private const int LicenciamientoCruceCostMonthlyOption = 645250000;
    private const int LicenciamientoCruceCostMonthlyLegacyOption = 645240000;
    private const int LicenciamientoCruceCostOneTimeOption = 645250001;
    private const int LicenciamientoCruceCostPrepaidOption = 645250002;
    private const decimal LicenciamientoCruceProbableThreshold = 0.76m;

    private static readonly string[] LicenciamientoCruceLegalTokens =
    {
        "SAS",
        "SA",
        "S A S",
        "S A",
        "LTDA",
        "LIMITADA",
        "INC",
        "CORP",
        "CORPORACION",
        "FUNDACION",
        "EMPRESA",
        "UNION TEMPORAL"
    };

    public async Task<LicenciamientoCruceDashboardDto> GetLicenciamientoCruceDashboardAsync(
        int year,
        int month,
        string periodMode = "month",
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var licensingMetadata = await ResolveLicensingMetadataAsync(httpContext.User, ct);
        var billingMetadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var latestDataMonth = await ResolveLicenciamientoCruceLatestDataMonthAsync(
            licensingMetadata,
            billingMetadata,
            httpContext.User,
            ct);
        var period = ResolveLicenciamientoCrucePeriod(year, month, periodMode, latestDataMonth);

        var costRows = await GetLicenciamientoCruceCostRowsAsync(
            licensingMetadata,
            period.Start,
            period.End,
            period.SelectedMonth,
            0,
            httpContext.User,
            ct);
        var allBillingRows = await GetBillingRecordsAsync(
            billingMetadata,
            period.Start,
            period.End,
            _dashboardBillingEmissionDateField,
            _dashboardBillingEmissionDateFieldKind,
            httpContext.User,
            ct);
        var excludedCopiersBillingRows = allBillingRows
            .Where(static row => IsLicenciamientoCruceCopiersVertical(row.VerticalLabel))
            .ToList();
        var billingRows = allBillingRows
            .Where(static row => !IsLicenciamientoCruceCopiersVertical(row.VerticalLabel))
            .ToList();

        var months = BuildLicenciamientoCruceMatrixMonths(costRows, billingRows, period.Start, period.End, period.SelectedMonth);
        var rows = months
            .SelectMany(monthInfo =>
            {
                var monthDate = new DateOnly(monthInfo.Year, monthInfo.Month, 1);
                var monthCostRows = costRows
                    .Where(row => GetLicenciamientoCruceCostMonth(row) == monthDate)
                    .ToList();
                var monthBillingRows = billingRows
                    .Where(row => GetLicenciamientoCruceBillingMonth(row) == monthDate)
                    .ToList();
                var costGroups = BuildLicenciamientoCruceCostGroups(monthCostRows, monthDate);
                var billingGroups = BuildLicenciamientoCruceBillingGroups(monthBillingRows);
                return BuildLicenciamientoCruceRows(costGroups, billingGroups, monthDate, monthDate);
            })
            .OrderBy(row => ResolveLicenciamientoCruceContractOrder(row.TipoContratoKey))
            .ThenBy(row => row.MesCierre, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => ResolveLicenciamientoCruceStateOrder(row.EstadoCruce))
            .ThenBy(row => row.Cliente, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var row in rows)
        {
            row.MatrixClientKey = ResolveLicenciamientoCruceMatrixClientKey(row);
        }

        var totalCostSource = RoundCurrency(costRows.Sum(static row => row.CostCop));
        var totalCostCross = RoundCurrency(rows.Sum(static row => row.CostoLicenciamiento));
        var totalBillingSource = RoundCurrency(billingRows.Sum(CalculateLicenciamientoCruceBillingWithoutVat));
        var totalBillingCross = RoundCurrency(rows.Sum(static row => row.FacturacionSinIva));
        var totalMargin = RoundCurrency(totalBillingCross - totalCostCross);
        var totalMarginPct = CalculateLicenciamientoCruceMarginPercent(totalMargin, totalBillingCross);
        var totalPositiveMargin = RoundCurrency(rows.Where(static row => row.MargenBruto > 0m).Sum(static row => row.MargenBruto));
        var totalNegativeMargin = RoundCurrency(rows.Where(static row => row.MargenBruto < 0m).Sum(static row => row.MargenBruto));

        var totals = new LicenciamientoCruceTotalsDto
        {
            TotalCostosLicenciamiento = totalCostCross,
            TotalFacturacionRelacionada = totalBillingCross,
            MargenBrutoTotal = totalMargin,
            MargenBrutoPct = totalMarginPct,
            TotalUtilidadPositiva = totalPositiveMargin,
            TotalUtilidadNegativa = totalNegativeMargin,
            TotalCostosFuente = totalCostSource,
            TotalCostosCruce = totalCostCross,
            TotalFacturacionFuenteSinIva = totalBillingSource
        };
        var matrixSegments = BuildLicenciamientoCruceMatrixSegments(rows, months);
        var orphanRecords = BuildLicenciamientoCruceOrphanRecords(rows);

        return new LicenciamientoCruceDashboardDto
        {
            MesCierre = FormatLicenciamientoCruceMonth(period.SelectedMonth),
            MesCosto = FormatLicenciamientoCruceMonth(period.SelectedMonth),
            MesFacturacion = FormatLicenciamientoCruceMonth(period.SelectedMonth),
            BillingOffsetMonths = 0,
            SelectedYear = period.SelectedMonth.Year,
            SelectedMonth = period.SelectedMonth.Month,
            PeriodMode = period.Mode,
            PeriodLabel = period.Label,
            LatestDataMonth = FormatLicenciamientoCruceMonth(latestDataMonth),
            HasData = rows.Count > 0,
            RecordsCount = rows.Count,
            Totals = totals,
            StatusCounts = BuildLicenciamientoCruceStatusCounts(rows),
            Rows = rows,
            ContractSegments = BuildLicenciamientoCruceContractSegments(rows),
            MatrixSegments = matrixSegments,
            MatrixMonths = months,
            Orphans = orphanRecords,
            CostContractTypeOptions = BuildLicenciamientoCruceCostContractTypeOptions(),
            BillingContractTypeOptions = BuildLicenciamientoCruceBillingContractTypeOptions(),
            BillingVerticalOptions = BuildLicenciamientoCruceBillingVerticalOptions(),
            MonthSummaries = BuildLicenciamientoCruceMonthSummaries(rows),
            Alerts = BuildLicenciamientoCruceAlerts(rows),
            Validations = BuildLicenciamientoCruceValidations(
                costRows,
                billingRows,
                rows,
                totalCostSource,
                totalCostCross,
                period.SelectedMonth,
                excludedCopiersBillingRows.Count),
            Message = rows.Count == 0
                ? "No hay costos ni facturacion para el periodo seleccionado."
                : $"Cruce listo para {period.Label}: costos por mes factura contra facturas emitidas en el mismo mes."
        };
    }

    public async Task<LicenciamientoCruceUpdateBillingVerticalResultDto> UpdateLicenciamientoCruceBillingVerticalAsync(
        LicenciamientoCruceUpdateBillingVerticalRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var recordIds = (request.RecordIds ?? Array.Empty<string>())
            .Select(static value => NormalizeOptionalGuid(value))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (recordIds.Count == 0)
            throw new InvalidOperationException("Selecciona al menos una factura para cambiar la vertical.");

        var option = BuildLicenciamientoCruceBillingVerticalOptions()
            .FirstOrDefault(item => item.Value == request.VerticalOptionValue);
        if (option is null)
            throw new InvalidOperationException("La vertical seleccionada no es valida.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var payload = new Dictionary<string, object?>
        {
            [_dashboardBillingVerticalField] = option.Value
        };

        foreach (var recordId in recordIds)
        {
            await CallDataverseSendAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}({recordId})",
                "PATCH",
                payload,
                httpContext.User,
                ct);
        }

        return new LicenciamientoCruceUpdateBillingVerticalResultDto
        {
            UpdatedCount = recordIds.Count,
            VerticalOptionValue = option.Value,
            VerticalLabel = option.Label,
            Message = recordIds.Count == 1
                ? $"Vertical actualizada a {option.Label}."
                : $"{recordIds.Count:N0} facturas actualizadas a {option.Label}."
        };
    }

    public async Task<LicenciamientoCruceUpdateCostAccountResultDto> UpdateLicenciamientoCruceCostAccountAsync(
        LicenciamientoCruceUpdateCostAccountRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var recordId = NormalizeOptionalGuid(request.RecordId);
        if (string.IsNullOrWhiteSpace(recordId))
            throw new InvalidOperationException("Selecciona un registro de consumo valido.");

        var accountInput = (request.AccountId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(accountInput))
            throw new InvalidOperationException("Indica el Account ID que debe quedar en el consumo.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveLicensingMetadataAsync(httpContext.User, ct);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var accountLookupId = NormalizeOptionalGuid(accountInput);
        var accountLabel = accountInput;

        if (metadata.AccountFieldIsLookup)
        {
            if (string.IsNullOrWhiteSpace(accountLookupId))
            {
                var lookupResult = await FindLicensingLookupAsync(
                    metadata.AccountMetadata,
                    metadata.AccountSearchFields,
                    accountInput,
                    metadata.AccountAttributeTypes,
                    httpContext.User,
                    ct);
                if (lookupResult.Lookup is null)
                    throw new InvalidOperationException(lookupResult.FailureReason);

                accountLookupId = NormalizeGuid(lookupResult.Lookup.Id, nameof(lookupResult.Lookup.Id));
                accountLabel = FirstNonEmpty(lookupResult.Lookup.Label, accountInput);
            }

            payload[$"{metadata.AccountNavigationProperty}@odata.bind"] =
                $"/{metadata.AccountMetadata.EntitySetName}({accountLookupId})";
        }
        else
        {
            payload[LicensingAccountLookupField] = ConvertLicensingPayloadValue(metadata, LicensingAccountLookupField, accountInput);
        }

        await CallDataverseSendAsync(
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({recordId})",
            "PATCH",
            payload,
            httpContext.User,
            ct);

        var clientId = "";
        var clientName = "";
        if (metadata.AccountFieldIsLookup && !string.IsNullOrWhiteSpace(accountLookupId))
        {
            try
            {
                var resolution = await ResolveLicensingClientFromAccountAsync(metadata, accountLookupId, httpContext.User, ct);
                clientId = NormalizeOptionalGuid(resolution.ClientId);
                clientName = resolution.ClientName;
            }
            catch (InvalidOperationException)
            {
                // The row was updated; if the Account ID still has no client, the next refresh will keep it as orphan.
            }
        }

        return new LicenciamientoCruceUpdateCostAccountResultDto
        {
            RecordId = recordId,
            AccountId = accountLookupId,
            AccountLabel = accountLabel,
            ClientId = clientId,
            ClientName = clientName,
            Message = string.IsNullOrWhiteSpace(clientName)
                ? $"Account ID actualizado a {accountLabel}."
                : $"Account ID actualizado a {accountLabel}; cliente resuelto: {clientName}."
        };
    }

    public async Task<IReadOnlyList<LicenciamientoCruceAccountLookupDto>> SearchLicenciamientoCruceAccountsAsync(
        string query,
        int top = 12,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        query = (query ?? "").Trim();
        if (query.Length < 2)
            return Array.Empty<LicenciamientoCruceAccountLookupDto>();

        var metadata = await ResolveLicensingMetadataAsync(httpContext.User, ct);
        return await SearchLicenciamientoCruceAccountOptionsAsync(
            metadata,
            query,
            Math.Clamp(top, 1, 25),
            httpContext.User,
            ct);
    }

    public async Task<LicenciamientoCruceSaveAccountMappingResultDto> SaveLicenciamientoCruceAccountMappingAsync(
        LicenciamientoCruceSaveAccountMappingRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var sourceAccountId = NormalizeOptionalGuid(request.SourceAccountId);
        var sourceAccountName = (request.SourceAccountName ?? "").Trim();
        var sourceClientName = (request.SourceClientName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(sourceAccountId) && string.IsNullOrWhiteSpace(sourceAccountName))
            throw new InvalidOperationException("No se encontro la cuenta origen para guardar el mapeo.");

        var targetAccountId = NormalizeOptionalGuid(request.TargetAccountId);
        if (string.IsNullOrWhiteSpace(targetAccountId))
            throw new InvalidOperationException("Selecciona un Account ID destino valido.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var licensingMetadata = await ResolveLicensingMetadataAsync(httpContext.User, ct);
        var mappingMetadata = await EnsureLicenciamientoCruceAccountMapTableAsync(httpContext.User, ct);
        var targetAccount = await ResolveLicenciamientoCruceAccountLookupAsync(
            licensingMetadata,
            targetAccountId,
            httpContext.User,
            ct);
        var targetClient = await ResolveLicensingClientFromAccountAsync(
            licensingMetadata,
            targetAccountId,
            httpContext.User,
            ct);

        var mappingName = BuildLicenciamientoCruceMappingName(
            FirstNonEmpty(sourceAccountName, sourceAccountId),
            targetAccount.AccountName);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [LicenciamientoCruceAccountMapPrimaryNameField] = mappingName,
            [LicenciamientoCruceAccountMapSourceAccountIdField] = sourceAccountId,
            [LicenciamientoCruceAccountMapSourceAccountNameField] = sourceAccountName,
            [LicenciamientoCruceAccountMapSourceClientNameField] = sourceClientName,
            [LicenciamientoCruceAccountMapTargetAccountIdField] = targetAccountId,
            [LicenciamientoCruceAccountMapTargetAccountNameField] = targetAccount.AccountName,
            [LicenciamientoCruceAccountMapTargetClientIdField] = NormalizeOptionalGuid(targetClient.ClientId),
            [LicenciamientoCruceAccountMapTargetClientNameField] = targetClient.ClientName,
            [LicenciamientoCruceAccountMapActiveField] = true,
            [LicenciamientoCruceAccountMapNotesField] = (request.Notes ?? "").Trim()
        };

        var existingMapping = await FindLicenciamientoCruceAccountMappingAsync(
            mappingMetadata,
            sourceAccountId,
            sourceAccountName,
            httpContext.User,
            ct);
        string mappingId;
        if (existingMapping is null)
        {
            var body = await CallDataverseSendAsync(
                $"/api/data/v9.2/{mappingMetadata.EntitySetName}",
                "POST",
                payload,
                httpContext.User,
                ct);
            mappingId = await ResolveCreatedLicenciamientoCruceMappingIdAsync(
                mappingMetadata,
                body,
                sourceAccountId,
                sourceAccountName,
                httpContext.User,
                ct);
        }
        else
        {
            mappingId = existingMapping.MappingId;
            await CallDataverseSendAsync(
                $"/api/data/v9.2/{mappingMetadata.EntitySetName}({mappingId})",
                "PATCH",
                payload,
                httpContext.User,
                ct);
        }

        return new LicenciamientoCruceSaveAccountMappingResultDto
        {
            Message = $"Mapeo guardado: {FirstNonEmpty(sourceAccountName, sourceAccountId)} -> {targetAccount.AccountName}.",
            MappingId = mappingId,
            SourceAccountId = sourceAccountId,
            SourceAccountName = sourceAccountName,
            TargetAccountId = targetAccountId,
            TargetAccountName = targetAccount.AccountName,
            TargetClientId = NormalizeOptionalGuid(targetClient.ClientId),
            TargetClientName = targetClient.ClientName
        };
    }

    public async Task<LicenciamientoCruceUpdateCostInvoiceDateResultDto> UpdateLicenciamientoCruceCostInvoiceDateAsync(
        LicenciamientoCruceUpdateCostInvoiceDateRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var recordId = NormalizeOptionalGuid(request.RecordId);
        if (string.IsNullOrWhiteSpace(recordId))
            throw new InvalidOperationException("Selecciona un costo valido.");

        if (!TryParseDateOnly((request.InvoiceDate ?? "").Trim(), out var invoiceDate))
            throw new InvalidOperationException("La fecha factura seleccionada no es valida.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var metadata = await ResolveLicensingMetadataAsync(httpContext.User, ct);
        var payload = new Dictionary<string, object?>
        {
            [LicensingInvoiceDateField] = ConvertLicensingPayloadValue(metadata, LicensingInvoiceDateField, invoiceDate)
        };

        await CallDataverseSendAsync(
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({recordId})",
            "PATCH",
            payload,
            httpContext.User,
            ct);

        return new LicenciamientoCruceUpdateCostInvoiceDateResultDto
        {
            Message = $"Costo movido a {invoiceDate:yyyy-MM}.",
            RecordId = recordId,
            InvoiceDate = invoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Month = invoiceDate.ToString("yyyy-MM", CultureInfo.InvariantCulture)
        };
    }

    private async Task<IReadOnlyList<LicenciamientoCruceAccountLookupDto>> SearchLicenciamientoCruceAccountOptionsAsync(
        LicensingMetadata metadata,
        string query,
        int top,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var results = new List<LicenciamientoCruceAccountLookupDto>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clientLookupProperty = BuildLookupValueProperty(LicensingAccountClientLookupField);

        if (Guid.TryParse(query, out var queryGuid))
        {
            var exact = await TryGetLicenciamientoCruceAccountLookupByIdAsync(metadata, queryGuid.ToString("D"), user, ct);
            if (exact is not null)
            {
                results.Add(exact);
                seenIds.Add(exact.AccountId);
            }
        }

        foreach (var searchField in metadata.AccountSearchFields.Where(static field => !string.IsNullOrWhiteSpace(field)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (results.Count >= top)
                break;

            metadata.AccountAttributeTypes.TryGetValue(searchField, out var searchFieldType);
            var filter = BuildLicensingLookupSearchExpression(searchField, query, searchFieldType);
            if (string.IsNullOrWhiteSpace(filter))
                continue;

            var select = string.Join(",",
                new[] { metadata.AccountMetadata.PrimaryIdField, metadata.AccountMetadata.PrimaryNameField, searchField, clientLookupProperty }
                    .Where(static field => !string.IsNullOrWhiteSpace(field))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            var remaining = Math.Max(top - results.Count, 1);
            var relativeUrl = $"/api/data/v9.2/{metadata.AccountMetadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top={remaining}";
            IReadOnlyList<JsonElement> items;
            try
            {
                items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException)
            {
                _logger.LogWarning(
                    ex,
                    "No fue posible buscar Account IDs para cruce de licenciamiento en {SearchField}.",
                    searchField);
                continue;
            }

            foreach (var item in items)
            {
                var account = BuildLicenciamientoCruceAccountLookup(metadata, item, searchField, query);
                if (account is null || !seenIds.Add(account.AccountId))
                    continue;

                results.Add(account);
                if (results.Count >= top)
                    break;
            }
        }

        return results;
    }

    private async Task<LicenciamientoCruceAccountLookupDto> ResolveLicenciamientoCruceAccountLookupAsync(
        LicensingMetadata metadata,
        string accountId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedAccountId = NormalizeGuid(accountId, nameof(accountId));
        var account = await TryGetLicenciamientoCruceAccountLookupByIdAsync(metadata, normalizedAccountId, user, ct);
        if (account is null)
            throw new InvalidOperationException("No se encontro el Account ID destino seleccionado.");

        return account;
    }

    private async Task<LicenciamientoCruceAccountLookupDto?> TryGetLicenciamientoCruceAccountLookupByIdAsync(
        LicensingMetadata metadata,
        string accountId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var clientLookupProperty = BuildLookupValueProperty(LicensingAccountClientLookupField);
        var select = string.Join(",",
            new[] { metadata.AccountMetadata.PrimaryIdField, metadata.AccountMetadata.PrimaryNameField, clientLookupProperty }
                .Where(static field => !string.IsNullOrWhiteSpace(field))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        var relativeUrl = $"/api/data/v9.2/{metadata.AccountMetadata.EntitySetName}({accountId})?$select={select}";

        try
        {
            var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
            using var doc = JsonDocument.Parse(json);
            return BuildLicenciamientoCruceAccountLookup(metadata, doc.RootElement, metadata.AccountMetadata.PrimaryNameField, accountId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(ex, "No fue posible resolver el Account ID destino {AccountId}.", accountId);
            return null;
        }
    }

    private static LicenciamientoCruceAccountLookupDto? BuildLicenciamientoCruceAccountLookup(
        LicensingMetadata metadata,
        JsonElement item,
        string searchField,
        string query)
    {
        var clientLookupProperty = BuildLookupValueProperty(LicensingAccountClientLookupField);
        var id = NormalizeOptionalGuid(ReadString(item, metadata.AccountMetadata.PrimaryIdField));
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var matchedValue = ReadString(item, searchField).Trim();
        return new LicenciamientoCruceAccountLookupDto
        {
            AccountId = id,
            AccountName = FirstNonEmpty(
                ReadString(item, metadata.AccountMetadata.PrimaryNameField).Trim(),
                matchedValue,
                query),
            ClientId = NormalizeOptionalGuid(ReadString(item, clientLookupProperty)),
            ClientName = FirstNonEmpty(ReadLookupFormattedValue(item, clientLookupProperty), ""),
            SearchField = searchField,
            MatchedValue = matchedValue
        };
    }

    private async Task<RhEntityMetadata> EnsureLicenciamientoCruceAccountMapTableAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var metadata = await TryResolveLicenciamientoCruceAccountMapMetadataAsync(user, ct);
        var createdAny = false;
        if (metadata is null)
        {
            await CreateLicenciamientoCruceAccountMapEntityAsync(user, ct);
            createdAny = true;
            metadata = await WaitForLicenciamientoCruceAccountMapMetadataAsync(user, ct);
        }

        var existingAttributes = await LoadLicenciamientoCruceAccountMapAttributesAsync(user, ct);
        foreach (var definition in BuildLicenciamientoCruceAccountMapAttributeDefinitions())
        {
            if (existingAttributes.Contains(definition.LogicalName))
                continue;

            await CallDataverseSendAsync(
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(LicenciamientoCruceAccountMapLogicalName)}')/Attributes",
                "POST",
                BuildLicenciamientoCruceMapAttributePayload(definition),
                user,
                ct);
            createdAny = true;
        }

        if (createdAny)
        {
            await PublishLicenciamientoCruceAccountMapEntityAsync(user, ct);
            await WaitForLicenciamientoCruceAccountMapAttributesAsync(user, ct);
        }

        return metadata;
    }

    private async Task<RhEntityMetadata?> TryResolveLicenciamientoCruceAccountMapMetadataAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(LicenciamientoCruceAccountMapLogicalName)}')" +
                "?$select=LogicalName,EntitySetName,PrimaryIdAttribute,PrimaryNameAttribute";
            var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);
            using var doc = JsonDocument.Parse(json);
            return new RhEntityMetadata
            {
                LogicalName = LicenciamientoCruceAccountMapLogicalName,
                EntitySetName = FirstNonEmpty(ReadString(doc.RootElement, "EntitySetName"), LicenciamientoCruceAccountMapFallbackEntitySetName),
                PrimaryIdField = FirstNonEmpty(ReadString(doc.RootElement, "PrimaryIdAttribute"), LicenciamientoCruceAccountMapFallbackIdField),
                PrimaryNameField = FirstNonEmpty(ReadString(doc.RootElement, "PrimaryNameAttribute"), LicenciamientoCruceAccountMapPrimaryNameField)
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogInformation(ex, "La tabla de mapeo de Account ID de licenciamiento aun no existe o no es visible.");
            return null;
        }
    }

    private async Task<RhEntityMetadata> WaitForLicenciamientoCruceAccountMapMetadataAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            var metadata = await TryResolveLicenciamientoCruceAccountMapMetadataAsync(user, ct);
            if (metadata is not null)
                return metadata;

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        throw new InvalidOperationException("Dataverse creo la tabla de mapeo, pero aun no la expone. Intenta nuevamente en unos segundos.");
    }

    private async Task<HashSet<string>> LoadLicenciamientoCruceAccountMapAttributesAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            LicenciamientoCruceAccountMapPrimaryNameField
        };

        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(LicenciamientoCruceAccountMapLogicalName)}')" +
                "?$select=LogicalName&$expand=Attributes($select=LogicalName)";
            var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Attributes", out var attributes)
                || attributes.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var attribute in attributes.EnumerateArray())
            {
                var logicalName = ReadString(attribute, "LogicalName").Trim();
                if (!string.IsNullOrWhiteSpace(logicalName))
                    result.Add(logicalName);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(ex, "No fue posible listar las columnas de la tabla de mapeo de licenciamiento.");
        }

        return result;
    }

    private async Task WaitForLicenciamientoCruceAccountMapAttributesAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var required = BuildLicenciamientoCruceAccountMapAttributeDefinitions()
            .Select(static definition => definition.LogicalName)
            .Append(LicenciamientoCruceAccountMapPrimaryNameField)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var attempt = 1; attempt <= 8; attempt++)
        {
            var existing = await LoadLicenciamientoCruceAccountMapAttributesAsync(user, ct);
            if (required.All(existing.Contains))
                return;

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        throw new InvalidOperationException("Dataverse aun no expone todas las columnas del mapeo de Account ID. Intenta nuevamente en unos segundos.");
    }

    private async Task CreateLicenciamientoCruceAccountMapEntityAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.EntityMetadata",
            ["Attributes"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["@odata.type"] = "Microsoft.Dynamics.CRM.StringAttributeMetadata",
                    ["AttributeType"] = "String",
                    ["AttributeTypeName"] = CreateHardwareValuePayload("StringType"),
                    ["Description"] = CreateHardwareLabelPayload("Nombre descriptivo del mapeo de Account ID de licenciamiento."),
                    ["DisplayName"] = CreateHardwareLabelPayload("Nombre"),
                    ["IsPrimaryName"] = true,
                    ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
                    ["SchemaName"] = "cr07a_Name",
                    ["FormatName"] = CreateHardwareValuePayload("Text"),
                    ["MaxLength"] = 300
                }
            },
            ["Description"] = CreateHardwareLabelPayload("Equivalencias persistentes para agrupar Account IDs de consumo Intcomex hacia un Account ID canonico."),
            ["DisplayCollectionName"] = CreateHardwareLabelPayload("Mapeos Account ID Licenciamiento"),
            ["DisplayName"] = CreateHardwareLabelPayload("Mapeo Account ID Licenciamiento"),
            ["HasActivities"] = false,
            ["HasNotes"] = false,
            ["IsActivity"] = false,
            ["OwnershipType"] = "UserOwned",
            ["SchemaName"] = "cr07a_LicenciamientoAccountMap"
        };

        await CallDataverseSendAsync("/api/data/v9.2/EntityDefinitions", "POST", payload, user, ct);
    }

    private static IReadOnlyList<LicenciamientoCruceMapAttributeDefinition> BuildLicenciamientoCruceAccountMapAttributeDefinitions() =>
        new[]
        {
            new LicenciamientoCruceMapAttributeDefinition(LicenciamientoCruceAccountMapSourceAccountIdField, "cr07a_SourceAccountId", "Account ID origen", "string", 200),
            new LicenciamientoCruceMapAttributeDefinition(LicenciamientoCruceAccountMapSourceAccountNameField, "cr07a_SourceAccountName", "Nombre cuenta origen", "string", 300),
            new LicenciamientoCruceMapAttributeDefinition(LicenciamientoCruceAccountMapSourceClientNameField, "cr07a_SourceClientName", "Cliente origen", "string", 300),
            new LicenciamientoCruceMapAttributeDefinition(LicenciamientoCruceAccountMapTargetAccountIdField, "cr07a_TargetAccountId", "Account ID destino", "string", 200),
            new LicenciamientoCruceMapAttributeDefinition(LicenciamientoCruceAccountMapTargetAccountNameField, "cr07a_TargetAccountName", "Nombre cuenta destino", "string", 300),
            new LicenciamientoCruceMapAttributeDefinition(LicenciamientoCruceAccountMapTargetClientIdField, "cr07a_TargetClientId", "Cliente destino ID", "string", 200),
            new LicenciamientoCruceMapAttributeDefinition(LicenciamientoCruceAccountMapTargetClientNameField, "cr07a_TargetClientName", "Cliente destino", "string", 300),
            new LicenciamientoCruceMapAttributeDefinition(LicenciamientoCruceAccountMapActiveField, "cr07a_Active", "Activo", "boolean", 0),
            new LicenciamientoCruceMapAttributeDefinition(LicenciamientoCruceAccountMapNotesField, "cr07a_Notes", "Observacion", "memo", 2000)
        };

    private static object BuildLicenciamientoCruceMapAttributePayload(
        LicenciamientoCruceMapAttributeDefinition definition)
    {
        if (string.Equals(definition.Kind, "boolean", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, object?>
            {
                ["@odata.type"] = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata",
                ["AttributeType"] = "Boolean",
                ["AttributeTypeName"] = CreateHardwareValuePayload("BooleanType"),
                ["DefaultValue"] = true,
                ["OptionSet"] = new Dictionary<string, object?>
                {
                    ["TrueOption"] = new Dictionary<string, object?>
                    {
                        ["Value"] = 1,
                        ["Label"] = CreateHardwareLabelPayload("Si")
                    },
                    ["FalseOption"] = new Dictionary<string, object?>
                    {
                        ["Value"] = 0,
                        ["Label"] = CreateHardwareLabelPayload("No")
                    }
                },
                ["Description"] = CreateHardwareLabelPayload(definition.DisplayName),
                ["DisplayName"] = CreateHardwareLabelPayload(definition.DisplayName),
                ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
                ["SchemaName"] = definition.SchemaName
            };
        }

        if (string.Equals(definition.Kind, "memo", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, object?>
            {
                ["@odata.type"] = "Microsoft.Dynamics.CRM.MemoAttributeMetadata",
                ["AttributeType"] = "Memo",
                ["AttributeTypeName"] = CreateHardwareValuePayload("MemoType"),
                ["Format"] = "TextArea",
                ["ImeMode"] = "Disabled",
                ["MaxLength"] = definition.MaxLength,
                ["IsLocalizable"] = false,
                ["Description"] = CreateHardwareLabelPayload(definition.DisplayName),
                ["DisplayName"] = CreateHardwareLabelPayload(definition.DisplayName),
                ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
                ["SchemaName"] = definition.SchemaName
            };
        }

        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.StringAttributeMetadata",
            ["AttributeType"] = "String",
            ["AttributeTypeName"] = CreateHardwareValuePayload("StringType"),
            ["Description"] = CreateHardwareLabelPayload(definition.DisplayName),
            ["DisplayName"] = CreateHardwareLabelPayload(definition.DisplayName),
            ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
            ["SchemaName"] = definition.SchemaName,
            ["FormatName"] = CreateHardwareValuePayload("Text"),
            ["MaxLength"] = definition.MaxLength
        };
    }

    private async Task PublishLicenciamientoCruceAccountMapEntityAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var publishXml =
            $"<importexportxml><entities><entity>{LicenciamientoCruceAccountMapLogicalName}</entity></entities></importexportxml>";
        await CallDataverseSendAsync(
            "/api/data/v9.2/PublishXml",
            "POST",
            new Dictionary<string, object?> { ["ParameterXml"] = publishXml },
            user,
            ct);
    }

    private async Task<LicenciamientoCruceAccountMapping?> FindLicenciamientoCruceAccountMappingAsync(
        RhEntityMetadata metadata,
        string sourceAccountId,
        string sourceAccountName,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(sourceAccountId))
            filters.Add($"{LicenciamientoCruceAccountMapSourceAccountIdField} eq '{EscapeOdataLiteral(sourceAccountId)}'");

        if (!string.IsNullOrWhiteSpace(sourceAccountName))
            filters.Add($"{LicenciamientoCruceAccountMapSourceAccountNameField} eq '{EscapeOdataLiteral(sourceAccountName)}'");

        if (filters.Count == 0)
            return null;

        var select = BuildLicenciamientoCruceMappingSelect(metadata);
        var filter = string.Join(" or ", filters);
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=1";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return items
            .Select(item => ParseLicenciamientoCruceAccountMapping(metadata, item))
            .FirstOrDefault(item => item is not null);
    }

    private async Task<string> ResolveCreatedLicenciamientoCruceMappingIdAsync(
        RhEntityMetadata metadata,
        string body,
        string sourceAccountId,
        string sourceAccountName,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var inlineId = NormalizeOptionalGuid(ReadString(doc.RootElement, metadata.PrimaryIdField));
                if (!string.IsNullOrWhiteSpace(inlineId))
                    return inlineId;
            }
            catch (JsonException)
            {
                // Dataverse often returns 204 on create; fall through to lookup.
            }
        }

        var mapping = await FindLicenciamientoCruceAccountMappingAsync(
            metadata,
            sourceAccountId,
            sourceAccountName,
            user,
            ct);
        if (mapping is null || string.IsNullOrWhiteSpace(mapping.MappingId))
            throw new InvalidOperationException("El mapeo fue guardado, pero no fue posible recuperar su ID.");

        return mapping.MappingId;
    }

    private static string BuildLicenciamientoCruceMappingSelect(RhEntityMetadata metadata) =>
        string.Join(",",
            new[]
            {
                metadata.PrimaryIdField,
                metadata.PrimaryNameField,
                LicenciamientoCruceAccountMapSourceAccountIdField,
                LicenciamientoCruceAccountMapSourceAccountNameField,
                LicenciamientoCruceAccountMapSourceClientNameField,
                LicenciamientoCruceAccountMapTargetAccountIdField,
                LicenciamientoCruceAccountMapTargetAccountNameField,
                LicenciamientoCruceAccountMapTargetClientIdField,
                LicenciamientoCruceAccountMapTargetClientNameField,
                LicenciamientoCruceAccountMapActiveField,
                LicenciamientoCruceAccountMapNotesField
            }
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));

    private static LicenciamientoCruceAccountMapping? ParseLicenciamientoCruceAccountMapping(
        RhEntityMetadata metadata,
        JsonElement item)
    {
        var mappingId = NormalizeOptionalGuid(ReadString(item, metadata.PrimaryIdField));
        if (string.IsNullOrWhiteSpace(mappingId))
            return null;

        var targetAccountId = NormalizeOptionalGuid(ReadString(item, LicenciamientoCruceAccountMapTargetAccountIdField));
        if (string.IsNullOrWhiteSpace(targetAccountId))
            return null;

        var active = !item.TryGetProperty(LicenciamientoCruceAccountMapActiveField, out _)
            || ReadBool(item, LicenciamientoCruceAccountMapActiveField);

        return new LicenciamientoCruceAccountMapping
        {
            MappingId = mappingId,
            SourceAccountId = NormalizeOptionalGuid(ReadString(item, LicenciamientoCruceAccountMapSourceAccountIdField)),
            SourceAccountName = ReadString(item, LicenciamientoCruceAccountMapSourceAccountNameField).Trim(),
            SourceClientName = ReadString(item, LicenciamientoCruceAccountMapSourceClientNameField).Trim(),
            TargetAccountId = targetAccountId,
            TargetAccountName = ReadString(item, LicenciamientoCruceAccountMapTargetAccountNameField).Trim(),
            TargetClientId = NormalizeOptionalGuid(ReadString(item, LicenciamientoCruceAccountMapTargetClientIdField)),
            TargetClientName = ReadString(item, LicenciamientoCruceAccountMapTargetClientNameField).Trim(),
            Active = active,
            Notes = ReadString(item, LicenciamientoCruceAccountMapNotesField).Trim()
        };
    }

    private static string BuildLicenciamientoCruceMappingName(string source, string target)
    {
        var normalizedSource = FirstNonEmpty(source, "Origen");
        var normalizedTarget = FirstNonEmpty(target, "Destino");
        var name = $"{normalizedSource} -> {normalizedTarget}";
        return name.Length <= 300 ? name : name[..300];
    }

    private async Task<DateOnly> ResolveLicenciamientoCruceLatestDataMonthAsync(
        LicensingMetadata licensingMetadata,
        RhEntityMetadata billingMetadata,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var dates = new List<DateOnly>();

        try
        {
            var filter = Uri.EscapeDataString($"{LicensingInvoiceDateField} ne null");
            var orderBy = Uri.EscapeDataString($"{LicensingInvoiceDateField} desc");
            var relativeUrl = $"/api/data/v9.2/{licensingMetadata.BaseMetadata.EntitySetName}?$select={LicensingInvoiceDateField}&$filter={filter}&$orderby={orderBy}&$top=1";
            var rows = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
            var costDate = rows.Count > 0 ? ReadDateOnly(rows[0], LicensingInvoiceDateField) : null;
            if (costDate.HasValue)
                dates.Add(new DateOnly(costDate.Value.Year, costDate.Value.Month, 1));
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(ex, "No fue posible resolver el ultimo mes con costos de licenciamiento.");
        }

        try
        {
            var filter = Uri.EscapeDataString($"{_dashboardBillingEmissionDateField} ne null");
            var orderBy = Uri.EscapeDataString($"{_dashboardBillingEmissionDateField} desc");
            var relativeUrl = $"/api/data/v9.2/{billingMetadata.EntitySetName}?$select={_dashboardBillingEmissionDateField}&$filter={filter}&$orderby={orderBy}&$top=1";
            var rows = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
            var billingDate = rows.Count > 0 ? ReadDateOnly(rows[0], _dashboardBillingEmissionDateField) : null;
            if (billingDate.HasValue)
                dates.Add(new DateOnly(billingDate.Value.Year, billingDate.Value.Month, 1));
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(ex, "No fue posible resolver el ultimo mes con facturacion.");
        }

        if (dates.Count > 0)
            return dates.Max();

        var today = GetBogotaToday();
        return new DateOnly(today.Year, today.Month, 1);
    }

    private static LicenciamientoCrucePeriod ResolveLicenciamientoCrucePeriod(
        int year,
        int month,
        string? periodMode,
        DateOnly latestDataMonth)
    {
        var selectedMonth = year is >= 2000 and <= 2100 && month is >= 1 and <= 12
            ? new DateOnly(year, month, 1)
            : latestDataMonth;
        var mode = (periodMode ?? "month").Trim().ToLowerInvariant() switch
        {
            "quarter" => "quarter",
            "ytd" => "ytd",
            _ => "month"
        };

        var start = mode switch
        {
            "quarter" => new DateOnly(selectedMonth.Year, (((selectedMonth.Month - 1) / 3) * 3) + 1, 1),
            "ytd" => new DateOnly(selectedMonth.Year, 1, 1),
            _ => selectedMonth
        };
        var end = mode switch
        {
            "quarter" => start.AddMonths(3),
            "ytd" => selectedMonth.AddMonths(1),
            _ => selectedMonth.AddMonths(1)
        };
        var label = mode switch
        {
            "quarter" => $"{selectedMonth.Year} Q{((selectedMonth.Month - 1) / 3) + 1}",
            "ytd" => $"{selectedMonth.Year} acumulado a {FormatLicenciamientoCruceMonth(selectedMonth)}",
            _ => FormatLicenciamientoCruceMonth(selectedMonth)
        };

        return new LicenciamientoCrucePeriod
        {
            SelectedMonth = selectedMonth,
            Start = start,
            End = end,
            Mode = mode,
            Label = label
        };
    }

    private static IReadOnlyList<LicenciamientoCruceMatrixMonthDto> BuildLicenciamientoCruceMatrixMonths(
        IReadOnlyList<LicenciamientoCruceCostRow> costRows,
        IReadOnlyList<BillingRecordRow> billingRows,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly selectedMonth)
    {
        var months = costRows
            .Select(GetLicenciamientoCruceCostMonth)
            .Concat(billingRows.Select(GetLicenciamientoCruceBillingMonth))
            .Where(month => month >= periodStart && month < periodEnd)
            .Distinct()
            .OrderBy(static month => month)
            .ToList();

        if (months.Count == 0)
            months.Add(selectedMonth);

        return months
            .Select(month => new LicenciamientoCruceMatrixMonthDto
            {
                Key = FormatLicenciamientoCruceMonth(month),
                Label = month.ToString("MMM yyyy", CultureInfo.GetCultureInfo("es-CO")),
                Year = month.Year,
                Month = month.Month
            })
            .ToList();
    }

    private static DateOnly GetLicenciamientoCruceCostMonth(LicenciamientoCruceCostRow row)
    {
        if (row.InvoiceDate.HasValue)
            return new DateOnly(row.InvoiceDate.Value.Year, row.InvoiceDate.Value.Month, 1);

        return new DateOnly(row.CostMonth.Year, row.CostMonth.Month, 1);
    }

    private static DateOnly GetLicenciamientoCruceBillingMonth(BillingRecordRow row)
    {
        if (row.EmissionDate.HasValue)
            return new DateOnly(row.EmissionDate.Value.Year, row.EmissionDate.Value.Month, 1);

        return DateOnly.MinValue;
    }

    private async Task<List<LicenciamientoCruceCostRow>> GetLicenciamientoCruceCostRowsAsync(
        LicensingMetadata metadata,
        DateOnly invoiceMonthStart,
        DateOnly invoiceMonthEnd,
        DateOnly fallbackCostMonth,
        int billingOffsetMonths,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var select = BuildLicensingSelectClause(metadata);
        var filter = BuildBillingDateFilter(
            LicensingInvoiceDateField,
            "date-only",
            invoiceMonthStart,
            invoiceMonthEnd);
        var orderBy = Uri.EscapeDataString($"{LicensingInvoiceDateField} asc,{LicensingCustomerNameField} asc");
        var relativeUrl = $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={orderBy}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        var rows = items
            .Select(item => ParseLicenciamientoCruceCostRow(metadata, item, fallbackCostMonth, billingOffsetMonths))
            .Where(static row => row is not null)
            .Cast<LicenciamientoCruceCostRow>()
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();

        await ApplyLicenciamientoCruceAccountMappingsAsync(metadata, rows, user, ct);
        await ResolveLicenciamientoCruceCostClientsFromAccountsAsync(metadata, rows, user, ct);
        return rows;
    }

    private LicenciamientoCruceCostRow? ParseLicenciamientoCruceCostRow(
        LicensingMetadata metadata,
        JsonElement item,
        DateOnly fallbackCostMonth,
        int billingOffsetMonths)
    {
        var record = BuildLicensingRecordDto(metadata, item);
        if (record is null)
            return null;

        var invoiceDate = ReadDateOnly(item, LicensingInvoiceDateField);
        var costMonth = invoiceDate
            ?? TryParseLicenciamientoCruceMonth(record.BillingInterval)
            ?? fallbackCostMonth;
        costMonth = new DateOnly(costMonth.Year, costMonth.Month, 1);

        var costCop = record.PesosTotal;
        if (Math.Abs(costCop) < 0.01m && Math.Abs(record.ValorTotalUsd) >= 0.01m && Math.Abs(record.Trm) >= 0.01m)
            costCop = RoundCurrency(record.ValorTotalUsd * record.Trm);

        var clientName = FirstNonEmpty(
            record.NombreCliente,
            record.CompanyAccountDisplay,
            "Cliente sin nombre");

        return new LicenciamientoCruceCostRow
        {
            RecordId = record.RecordId,
            AccountId = NormalizeOptionalGuid(record.CompanyAccountId),
            OriginalAccountId = NormalizeOptionalGuid(record.CompanyAccountId),
            ClientName = clientName,
            CompanyAccountDisplay = record.CompanyAccountDisplay,
            OriginalAccountDisplay = record.CompanyAccountDisplay,
            Vendor = record.Vendor,
            ProductId = NormalizeOptionalGuid(record.ProductId),
            ProductDisplay = record.ProductDisplay,
            ContractTypeValue = record.ContractTypeValue,
            ContractTypeKey = ResolveLicenciamientoCruceContractKey(record.ContractTypeValue, record.ContractTypeLabel, isBillingSource: false),
            ContractTypeLabel = ResolveLicenciamientoCruceContractLabel(ResolveLicenciamientoCruceContractKey(record.ContractTypeValue, record.ContractTypeLabel, isBillingSource: false)),
            InvoiceDate = invoiceDate,
            CostMonth = costMonth,
            CostCop = RoundCurrency(costCop)
        };
    }

    private async Task ApplyLicenciamientoCruceAccountMappingsAsync(
        LicensingMetadata metadata,
        IReadOnlyList<LicenciamientoCruceCostRow> rows,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return;

        var mappings = await LoadLicenciamientoCruceAccountMappingsAsync(user, ct);
        if (mappings.Count == 0)
            return;

        foreach (var row in rows)
        {
            var mapping = FindLicenciamientoCruceMappingForCostRow(row, mappings);
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.TargetAccountId))
                continue;

            row.OriginalAccountId = FirstNonEmpty(row.OriginalAccountId, row.AccountId);
            row.OriginalAccountDisplay = FirstNonEmpty(row.OriginalAccountDisplay, row.CompanyAccountDisplay);
            row.AccountId = mapping.TargetAccountId;
            row.CompanyAccountDisplay = FirstNonEmpty(mapping.TargetAccountName, row.CompanyAccountDisplay);
            row.AccountMappingId = mapping.MappingId;
            row.AccountMappingApplied = true;
            row.ClientId = NormalizeOptionalGuid(mapping.TargetClientId);
            row.ClientName = FirstNonEmpty(mapping.TargetClientName, row.ClientName);
        }
    }

    private async Task<IReadOnlyList<LicenciamientoCruceAccountMapping>> LoadLicenciamientoCruceAccountMappingsAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        RhEntityMetadata metadata;
        try
        {
            metadata = await EnsureLicenciamientoCruceAccountMapTableAsync(user, ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(ex, "No fue posible asegurar la tabla de mapeo de Account ID para cruce de licenciamiento.");
            return Array.Empty<LicenciamientoCruceAccountMapping>();
        }

        var select = BuildLicenciamientoCruceMappingSelect(metadata);
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$top=5000";
        IReadOnlyList<JsonElement> items;
        try
        {
            items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(ex, "No fue posible consultar los mapeos de Account ID de licenciamiento.");
            return Array.Empty<LicenciamientoCruceAccountMapping>();
        }

        return items
            .Select(item => ParseLicenciamientoCruceAccountMapping(metadata, item))
            .Where(static item => item is not null)
            .Select(static item => item!)
            .Where(static item => item.Active)
            .ToList();
    }

    private static LicenciamientoCruceAccountMapping? FindLicenciamientoCruceMappingForCostRow(
        LicenciamientoCruceCostRow row,
        IReadOnlyList<LicenciamientoCruceAccountMapping> mappings)
    {
        var sourceAccountId = NormalizeOptionalGuid(FirstNonEmpty(row.OriginalAccountId, row.AccountId));
        if (!string.IsNullOrWhiteSpace(sourceAccountId))
        {
            var byId = mappings.FirstOrDefault(mapping =>
                string.Equals(NormalizeOptionalGuid(mapping.SourceAccountId), sourceAccountId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
                return byId;
        }

        var sourceAccountName = NormalizeLicenciamientoCruceMapKey(FirstNonEmpty(row.OriginalAccountDisplay, row.CompanyAccountDisplay));
        if (!string.IsNullOrWhiteSpace(sourceAccountName))
        {
            var byAccountName = mappings.FirstOrDefault(mapping =>
                string.Equals(NormalizeLicenciamientoCruceMapKey(mapping.SourceAccountName), sourceAccountName, StringComparison.OrdinalIgnoreCase));
            if (byAccountName is not null)
                return byAccountName;
        }

        var sourceClientName = NormalizeLicenciamientoCruceClientKey(row.ClientName);
        if (!string.IsNullOrWhiteSpace(sourceClientName))
        {
            return mappings.FirstOrDefault(mapping =>
                string.Equals(NormalizeLicenciamientoCruceClientKey(mapping.SourceClientName), sourceClientName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private async Task ResolveLicenciamientoCruceCostClientsFromAccountsAsync(
        LicensingMetadata metadata,
        IReadOnlyList<LicenciamientoCruceCostRow> rows,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var accountIds = rows
            .Select(static row => NormalizeOptionalGuid(row.AccountId))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (accountIds.Count == 0)
            return;

        var resolutions = new Dictionary<string, LicensingAccountClientResolution>(StringComparer.OrdinalIgnoreCase);
        foreach (var accountId in accountIds)
        {
            try
            {
                resolutions[accountId] = await ResolveLicensingClientFromAccountAsync(metadata, accountId, user, ct);
            }
            catch (InvalidOperationException)
            {
                // If an Account ID is not tied to a client, keep the row visible as unmatched.
            }
        }

        foreach (var row in rows)
        {
            var accountId = NormalizeOptionalGuid(row.AccountId);
            if (string.IsNullOrWhiteSpace(accountId) || !resolutions.TryGetValue(accountId, out var resolution))
                continue;

            row.ClientId = NormalizeOptionalGuid(resolution.ClientId);
            row.ClientName = FirstNonEmpty(resolution.ClientName, row.ClientName, "Cliente sin nombre");
        }
    }

    private static IReadOnlyList<LicenciamientoCruceCostGroup> BuildLicenciamientoCruceCostGroups(
        IReadOnlyList<LicenciamientoCruceCostRow> rows,
        DateOnly fallbackCostMonth)
    {
        return rows
            .GroupBy(row => $"{row.ContractTypeKey}|{BuildLicenciamientoCruceGroupingKey(row.ClientId, row.ClientName, row.AccountId)}", StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToList();
                var clientId = ResolveLicenciamientoCruceMostCommonText(items.Select(static row => row.ClientId), "");
                var clientName = ResolveLicenciamientoCruceMostCommonText(items.Select(static row => row.ClientName), "Cliente sin nombre");
                var contractTypeKey = ResolveLicenciamientoCruceMostCommonText(items.Select(static row => row.ContractTypeKey), LicenciamientoCruceOtherKey);
                var contractTypeLabel = ResolveLicenciamientoCruceContractLabel(contractTypeKey);
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in items)
                {
                    AddLicenciamientoCruceMatchKeys(keys, item.ClientId, item.ClientName);
                }

                return new LicenciamientoCruceCostGroup
                {
                    GroupKey = group.Key,
                    ClientId = clientId,
                    ContractTypeKey = contractTypeKey,
                    ContractTypeLabel = contractTypeLabel,
                    ClientName = clientName,
                    Vertical = ResolveLicenciamientoCruceMostCommonText(items.Select(static row => row.Vendor), "Licenciamiento"),
                    CostMonth = ResolveLicenciamientoCruceMostCommonMonth(items.Select(static row => row.CostMonth)) ?? fallbackCostMonth,
                    CostCop = RoundCurrency(items.Sum(static row => row.CostCop)),
                    RecordIds = items.Select(static row => row.RecordId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    MatchKeys = keys,
                    CostItems = items.Select(BuildLicenciamientoCruceCostTraceItem).ToList()
                };
            })
            .OrderBy(static group => group.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<LicenciamientoCruceBillingGroup> BuildLicenciamientoCruceBillingGroups(
        IReadOnlyList<BillingRecordRow> rows)
    {
        return rows
            .GroupBy(row => $"{ResolveLicenciamientoCruceContractKey(row.ContractTypeOptionValue, row.ContractTypeLabel, isBillingSource: true)}|{BuildLicenciamientoCruceGroupingKey(row.ClientId, row.ClientName)}", StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToList();
                var clientId = ResolveLicenciamientoCruceMostCommonText(items.Select(static row => NormalizeOptionalGuid(row.ClientId)), "");
                var clientName = ResolveLicenciamientoCruceMostCommonText(items.Select(static row => row.ClientName), "Cliente sin nombre");
                var nit = ResolveLicenciamientoCruceMostCommonText(items.Select(static row => row.CompanyTaxId), "");
                var contractTypeKey = ResolveLicenciamientoCruceMostCommonText(items.Select(static row => ResolveLicenciamientoCruceContractKey(row.ContractTypeOptionValue, row.ContractTypeLabel, isBillingSource: true)), LicenciamientoCruceOtherKey);
                var contractTypeLabel = ResolveLicenciamientoCruceContractLabel(contractTypeKey);
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in items)
                {
                    AddLicenciamientoCruceMatchKeys(keys, item.ClientId, item.ClientName);
                }

                return new LicenciamientoCruceBillingGroup
                {
                    GroupKey = group.Key,
                    ClientId = clientId,
                    ContractTypeKey = contractTypeKey,
                    ContractTypeLabel = contractTypeLabel,
                    ClientName = clientName,
                    Nit = nit,
                    Vertical = ResolveLicenciamientoCruceMostCommonText(items.Select(static row => row.VerticalLabel), "Sin vertical"),
                    BillingWithoutVat = RoundCurrency(items.Sum(CalculateLicenciamientoCruceBillingWithoutVat)),
                    BillingRecordIds = items.Select(static row => row.RecordId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    MatchKeys = keys,
                    BillingItems = items.Select(BuildLicenciamientoCruceBillingTraceItem).ToList(),
                    HasInvalidVat = items.Any(static row => row.TotalInvoice < row.VatValue),
                    HasMissingVatValue = items.Any(static row => row.TotalInvoice > 0m && row.VatPercent > 0m && Math.Abs(row.VatValue) < 0.01m)
                };
            })
            .OrderBy(static group => group.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<LicenciamientoCruceRowDto> BuildLicenciamientoCruceRows(
        IReadOnlyList<LicenciamientoCruceCostGroup> costGroups,
        IReadOnlyList<LicenciamientoCruceBillingGroup> billingGroups,
        DateOnly closeMonth,
        DateOnly billingMonth)
    {
        var rows = new List<LicenciamientoCruceRowDto>();
        var unmatchedCosts = new List<LicenciamientoCruceCostGroup>();
        var usedBillingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cost in costGroups)
        {
            var exactBilling = billingGroups.FirstOrDefault(billing =>
                !usedBillingKeys.Contains(billing.GroupKey)
                && string.Equals(cost.ContractTypeKey, billing.ContractTypeKey, StringComparison.OrdinalIgnoreCase)
                && HasLicenciamientoCruceExactClientMatch(cost, billing));
            if (exactBilling is null)
            {
                unmatchedCosts.Add(cost);
                continue;
            }

            usedBillingKeys.Add(exactBilling.GroupKey);
            rows.Add(BuildLicenciamientoCruceRow(
                cost,
                exactBilling,
                closeMonth,
                billingMonth,
                LicenciamientoCruceStatusExact,
                100m));
        }

        foreach (var cost in unmatchedCosts)
        {
            var probableBilling = billingGroups
                .Where(billing => !usedBillingKeys.Contains(billing.GroupKey))
                .Where(billing => string.Equals(cost.ContractTypeKey, billing.ContractTypeKey, StringComparison.OrdinalIgnoreCase))
                .Where(billing => CanUseLicenciamientoCruceNameFallback(cost, billing))
                .Select(billing => new
                {
                    Billing = billing,
                    Score = CalculateLicenciamientoCruceClientSimilarity(cost.ClientName, billing.ClientName)
                })
                .Where(item => item.Score >= LicenciamientoCruceProbableThreshold)
                .OrderByDescending(static item => item.Score)
                .ThenByDescending(static item => item.Billing.BillingWithoutVat)
                .FirstOrDefault();

            if (probableBilling is null)
            {
                rows.Add(BuildLicenciamientoCruceCostOnlyRow(cost, closeMonth, billingMonth));
                continue;
            }

            usedBillingKeys.Add(probableBilling.Billing.GroupKey);
            rows.Add(BuildLicenciamientoCruceRow(
                cost,
                probableBilling.Billing,
                closeMonth,
                billingMonth,
                LicenciamientoCruceStatusProbable,
                RoundCurrency(probableBilling.Score * 100m)));
        }

        foreach (var billing in billingGroups.Where(billing => !usedBillingKeys.Contains(billing.GroupKey)))
        {
            rows.Add(BuildLicenciamientoCruceBillingOnlyRow(billing, closeMonth, billingMonth));
        }

        return rows;
    }

    private static LicenciamientoCruceRowDto BuildLicenciamientoCruceRow(
        LicenciamientoCruceCostGroup cost,
        LicenciamientoCruceBillingGroup billing,
        DateOnly closeMonth,
        DateOnly billingMonth,
        string status,
        decimal matchScore)
    {
        var margin = RoundCurrency(billing.BillingWithoutVat - cost.CostCop);
        var marginPct = CalculateLicenciamientoCruceMarginPercent(margin, billing.BillingWithoutVat);

        return new LicenciamientoCruceRowDto
        {
            RowKey = $"match:{cost.GroupKey}:{billing.GroupKey}",
            MesCierre = FormatLicenciamientoCruceMonth(closeMonth),
            MesCosto = FormatLicenciamientoCruceMonth(cost.CostMonth),
            MesFacturacion = FormatLicenciamientoCruceMonth(billingMonth),
            TipoContrato = ResolveLicenciamientoCruceContractLabel(cost.ContractTypeKey),
            TipoContratoKey = cost.ContractTypeKey,
            Cliente = FirstNonEmpty(billing.ClientName, cost.ClientName, "Cliente sin nombre"),
            NitCliente = billing.Nit,
            Vertical = FirstNonEmpty(billing.Vertical, cost.Vertical, "Licenciamiento"),
            CostoLicenciamiento = cost.CostCop,
            FacturacionSinIva = billing.BillingWithoutVat,
            MargenBruto = margin,
            MargenBrutoPct = marginPct,
            EstadoCruce = status,
            FuenteCosto = "cr07a_consumointcomex",
            FuenteFacturacion = "cr07a_facturacion",
            CostRecordCount = cost.RecordIds.Count,
            BillingRecordCount = billing.BillingRecordIds.Count,
            MatchScore = matchScore,
            IsMarginAlert = margin < 0m,
            CanInspect = !string.Equals(status, LicenciamientoCruceStatusExact, StringComparison.OrdinalIgnoreCase),
            Trace = BuildLicenciamientoCruceTrace(cost, billing, status, matchScore)
        };
    }

    private static LicenciamientoCruceRowDto BuildLicenciamientoCruceCostOnlyRow(
        LicenciamientoCruceCostGroup cost,
        DateOnly closeMonth,
        DateOnly billingMonth)
    {
        var margin = RoundCurrency(0m - cost.CostCop);

        return new LicenciamientoCruceRowDto
        {
            RowKey = $"cost:{cost.GroupKey}",
            MesCierre = FormatLicenciamientoCruceMonth(closeMonth),
            MesCosto = FormatLicenciamientoCruceMonth(cost.CostMonth),
            MesFacturacion = FormatLicenciamientoCruceMonth(billingMonth),
            TipoContrato = ResolveLicenciamientoCruceContractLabel(cost.ContractTypeKey),
            TipoContratoKey = cost.ContractTypeKey,
            Cliente = cost.ClientName,
            Vertical = FirstNonEmpty(cost.Vertical, "Licenciamiento"),
            CostoLicenciamiento = cost.CostCop,
            FacturacionSinIva = 0m,
            MargenBruto = margin,
            MargenBrutoPct = null,
            EstadoCruce = LicenciamientoCruceStatusCostOnly,
            FuenteCosto = "cr07a_consumointcomex",
            FuenteFacturacion = "",
            CostRecordCount = cost.RecordIds.Count,
            BillingRecordCount = 0,
            MatchScore = 0m,
            IsMarginAlert = true,
            CanInspect = true,
            Trace = BuildLicenciamientoCruceTrace(cost, null, LicenciamientoCruceStatusCostOnly, 0m)
        };
    }

    private static LicenciamientoCruceRowDto BuildLicenciamientoCruceBillingOnlyRow(
        LicenciamientoCruceBillingGroup billing,
        DateOnly closeMonth,
        DateOnly billingMonth)
    {
        var margin = RoundCurrency(billing.BillingWithoutVat);
        var marginPct = CalculateLicenciamientoCruceMarginPercent(margin, billing.BillingWithoutVat);

        return new LicenciamientoCruceRowDto
        {
            RowKey = $"billing:{billing.GroupKey}",
            MesCierre = FormatLicenciamientoCruceMonth(closeMonth),
            MesCosto = FormatLicenciamientoCruceMonth(closeMonth),
            MesFacturacion = FormatLicenciamientoCruceMonth(billingMonth),
            TipoContrato = ResolveLicenciamientoCruceContractLabel(billing.ContractTypeKey),
            TipoContratoKey = billing.ContractTypeKey,
            Cliente = billing.ClientName,
            NitCliente = billing.Nit,
            Vertical = FirstNonEmpty(billing.Vertical, "Sin vertical"),
            CostoLicenciamiento = 0m,
            FacturacionSinIva = billing.BillingWithoutVat,
            MargenBruto = margin,
            MargenBrutoPct = marginPct,
            EstadoCruce = LicenciamientoCruceStatusBillingOnly,
            FuenteCosto = "",
            FuenteFacturacion = "cr07a_facturacion",
            CostRecordCount = 0,
            BillingRecordCount = billing.BillingRecordIds.Count,
            MatchScore = 0m,
            IsMarginAlert = false,
            CanInspect = true,
            Trace = BuildLicenciamientoCruceTrace(null, billing, LicenciamientoCruceStatusBillingOnly, 0m)
        };
    }

    private static LicenciamientoCruceStatusCountsDto BuildLicenciamientoCruceStatusCounts(
        IReadOnlyList<LicenciamientoCruceRowDto> rows)
    {
        return new LicenciamientoCruceStatusCountsDto
        {
            MatchExacto = rows.Count(static row => string.Equals(row.EstadoCruce, LicenciamientoCruceStatusExact, StringComparison.OrdinalIgnoreCase)),
            MatchProbable = rows.Count(static row => string.Equals(row.EstadoCruce, LicenciamientoCruceStatusProbable, StringComparison.OrdinalIgnoreCase)),
            CostoSinFacturacion = rows.Count(static row => string.Equals(row.EstadoCruce, LicenciamientoCruceStatusCostOnly, StringComparison.OrdinalIgnoreCase)),
            FacturacionSinCosto = rows.Count(static row => string.Equals(row.EstadoCruce, LicenciamientoCruceStatusBillingOnly, StringComparison.OrdinalIgnoreCase))
        };
    }

    private static IReadOnlyList<LicenciamientoCruceMonthSummaryDto> BuildLicenciamientoCruceMonthSummaries(
        IReadOnlyList<LicenciamientoCruceRowDto> rows)
    {
        return rows
            .GroupBy(static row => row.MesCierre, StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
            {
                var cost = RoundCurrency(group.Sum(static row => row.CostoLicenciamiento));
                var billing = RoundCurrency(group.Sum(static row => row.FacturacionSinIva));
                var margin = RoundCurrency(billing - cost);
                return new LicenciamientoCruceMonthSummaryDto
                {
                    MesCierre = group.Key,
                    CostosLicenciamiento = cost,
                    FacturacionRelacionada = billing,
                    MargenBruto = margin,
                    MargenBrutoPct = CalculateLicenciamientoCruceMarginPercent(margin, billing)
                };
            })
            .OrderBy(static row => row.MesCierre, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<LicenciamientoCruceContractSegmentDto> BuildLicenciamientoCruceContractSegments(
        IReadOnlyList<LicenciamientoCruceRowDto> rows)
    {
        var orderedKeys = new[]
        {
            LicenciamientoCruceMonthlyKey,
            LicenciamientoCruceOneTimeKey,
            LicenciamientoCruceOtherKey
        };
        var segments = new List<LicenciamientoCruceContractSegmentDto>();

        foreach (var key in orderedKeys)
        {
            var segmentRows = rows
                .Where(row => string.Equals(row.TipoContratoKey, key, StringComparison.OrdinalIgnoreCase))
                .OrderBy(row => ResolveLicenciamientoCruceStateOrder(row.EstadoCruce))
                .ThenBy(row => row.Cliente, StringComparer.OrdinalIgnoreCase)
                .ToList();

            segments.Add(new LicenciamientoCruceContractSegmentDto
            {
                Key = key,
                Label = ResolveLicenciamientoCruceContractLabel(key),
                RecordsCount = segmentRows.Count,
                NegativeMarginCount = segmentRows.Count(static row => row.MargenBruto < 0m),
                Totals = BuildLicenciamientoCruceTotals(segmentRows),
                StatusCounts = BuildLicenciamientoCruceStatusCounts(segmentRows),
                Rows = segmentRows
            });
        }

        return segments;
    }

    private static IReadOnlyList<LicenciamientoCruceMatrixSegmentDto> BuildLicenciamientoCruceMatrixSegments(
        IReadOnlyList<LicenciamientoCruceRowDto> rows,
        IReadOnlyList<LicenciamientoCruceMatrixMonthDto> months)
    {
        var orderedKeys = new[]
        {
            LicenciamientoCruceMonthlyKey,
            LicenciamientoCruceOneTimeKey,
            LicenciamientoCruceAllKey,
            LicenciamientoCruceOtherKey
        };
        var segments = new List<LicenciamientoCruceMatrixSegmentDto>();

        foreach (var key in orderedKeys)
        {
            var segmentRows = rows
                .Where(row => string.Equals(key, LicenciamientoCruceAllKey, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(row.TipoContratoKey, key, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var clientRows = segmentRows
                .GroupBy(ResolveLicenciamientoCruceMatrixClientKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => BuildLicenciamientoCruceMatrixClientRow(group.ToList(), months))
                .OrderBy(static row => row.HasNegativeMargin ? 0 : 1)
                .ThenBy(static row => row.Cliente, StringComparer.OrdinalIgnoreCase)
                .ToList();

            segments.Add(new LicenciamientoCruceMatrixSegmentDto
            {
                Key = key,
                Label = string.Equals(key, LicenciamientoCruceAllKey, StringComparison.OrdinalIgnoreCase)
                    ? LicenciamientoCruceAllLabel
                    : ResolveLicenciamientoCruceContractLabel(key),
                RecordsCount = clientRows.Count,
                NegativeMarginCount = clientRows.Count(static row => row.HasNegativeMargin),
                OrphanCount = clientRows.SelectMany(static row => row.Cells).Count(static cell => cell.HasOrphans),
                Totals = BuildLicenciamientoCruceTotals(segmentRows),
                StatusCounts = BuildLicenciamientoCruceStatusCounts(segmentRows),
                Rows = clientRows
            });
        }

        return segments;
    }

    private static LicenciamientoCruceMatrixClientRowDto BuildLicenciamientoCruceMatrixClientRow(
        IReadOnlyList<LicenciamientoCruceRowDto> rows,
        IReadOnlyList<LicenciamientoCruceMatrixMonthDto> months)
    {
        var first = rows.FirstOrDefault() ?? new LicenciamientoCruceRowDto();
        var cells = months
            .Select(month =>
            {
                var monthRows = rows
                    .Where(row => string.Equals(row.MesCierre, month.Key, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var cost = RoundCurrency(monthRows.Sum(static row => row.CostoLicenciamiento));
                var billing = RoundCurrency(monthRows.Sum(static row => row.FacturacionSinIva));
                var utility = RoundCurrency(billing - cost);
                var hasCost = Math.Abs(cost) >= 0.01m;
                var hasBilling = Math.Abs(billing) >= 0.01m;
                return new LicenciamientoCruceMatrixCellDto
                {
                    Mes = month.Key,
                    CostoLicenciamiento = cost,
                    FacturacionSinIva = billing,
                    UtilidadValor = utility,
                    UtilidadPct = CalculateLicenciamientoCruceMarginPercent(utility, billing),
                    HasNegativeMargin = utility < 0m,
                    HasOrphans = hasCost != hasBilling
                };
            })
            .ToList();
        var totalCost = RoundCurrency(rows.Sum(static row => row.CostoLicenciamiento));
        var totalBilling = RoundCurrency(rows.Sum(static row => row.FacturacionSinIva));
        var totalUtility = RoundCurrency(totalBilling - totalCost);
        var clientId = ResolveLicenciamientoCruceMatrixClientId(rows);

        return new LicenciamientoCruceMatrixClientRowDto
        {
            RowKey = ResolveLicenciamientoCruceMatrixClientKey(first),
            ClienteId = clientId,
            Cliente = ResolveLicenciamientoCruceMostCommonText(rows.Select(static row => row.Cliente), "Cliente sin nombre"),
            NitCliente = ResolveLicenciamientoCruceMostCommonText(rows.Select(static row => row.NitCliente), ""),
            TotalCostoLicenciamiento = totalCost,
            TotalFacturacionSinIva = totalBilling,
            TotalUtilidad = totalUtility,
            TotalUtilidadPct = CalculateLicenciamientoCruceMarginPercent(totalUtility, totalBilling),
            HasNegativeMargin = cells.Any(static cell => cell.HasNegativeMargin),
            HasOrphans = cells.Any(static cell => cell.HasOrphans),
            Cells = cells
        };
    }

    private static string ResolveLicenciamientoCruceMatrixClientId(IEnumerable<LicenciamientoCruceRowDto> rows)
    {
        foreach (var row in rows)
        {
            var billingId = NormalizeOptionalGuid(row.Trace?.BillingClientId);
            if (!string.IsNullOrWhiteSpace(billingId))
                return billingId;

            var costId = NormalizeOptionalGuid(row.Trace?.CostClientId);
            if (!string.IsNullOrWhiteSpace(costId))
                return costId;
        }

        return "";
    }

    private static string ResolveLicenciamientoCruceMatrixClientKey(LicenciamientoCruceRowDto row)
    {
        var clientId = NormalizeOptionalGuid(row.Trace?.BillingClientId);
        if (string.IsNullOrWhiteSpace(clientId))
            clientId = NormalizeOptionalGuid(row.Trace?.CostClientId);
        if (!string.IsNullOrWhiteSpace(clientId))
            return $"client:{clientId}";

        var clientKey = NormalizeLicenciamientoCruceClientKey(row.Cliente);
        return !string.IsNullOrWhiteSpace(clientKey)
            ? $"name:{clientKey}"
            : $"row:{row.RowKey}";
    }

    private static IReadOnlyList<LicenciamientoCruceOrphanRecordDto> BuildLicenciamientoCruceOrphanRecords(
        IReadOnlyList<LicenciamientoCruceRowDto> rows)
    {
        var orphans = new List<LicenciamientoCruceOrphanRecordDto>();
        foreach (var group in rows.GroupBy(row => $"{row.TipoContratoKey}|{row.MesCierre}|{FirstNonEmpty(row.MatrixClientKey, ResolveLicenciamientoCruceMatrixClientKey(row))}", StringComparer.OrdinalIgnoreCase))
        {
            var groupRows = group.ToList();
            var costTotal = RoundCurrency(groupRows.Sum(static row => row.CostoLicenciamiento));
            var billingTotal = RoundCurrency(groupRows.Sum(static row => row.FacturacionSinIva));
            var hasCost = Math.Abs(costTotal) >= 0.01m;
            var hasBilling = Math.Abs(billingTotal) >= 0.01m;
            if (hasCost && !hasBilling)
            {
                orphans.AddRange(groupRows
                    .SelectMany(static row => row.Trace?.CostItems ?? Array.Empty<LicenciamientoCruceTraceItemDto>())
                    .DistinctBy(static item => item.RecordId, StringComparer.OrdinalIgnoreCase)
                    .Select(item => BuildLicenciamientoCruceOrphanRecord(item, "cost", LicenciamientoCruceStatusCostOnly, "No hay factura emitida en el mismo mes, con el mismo cliente y tipo de contrato.")));
            }
            else if (hasBilling && !hasCost)
            {
                orphans.AddRange(groupRows
                    .SelectMany(static row => row.Trace?.BillingItems ?? Array.Empty<LicenciamientoCruceTraceItemDto>())
                    .DistinctBy(static item => item.RecordId, StringComparer.OrdinalIgnoreCase)
                    .Select(item => BuildLicenciamientoCruceOrphanRecord(item, "billing", LicenciamientoCruceStatusBillingOnly, "No hay costo con mes factura igual, mismo cliente y tipo de contrato.")));
            }
        }

        return orphans
            .OrderBy(static row => row.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Mes, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Cliente, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static LicenciamientoCruceOrphanRecordDto BuildLicenciamientoCruceOrphanRecord(
        LicenciamientoCruceTraceItemDto item,
        string source,
        string status,
        string reason) =>
        new()
        {
            Source = source,
            Status = status,
            RecordId = item.RecordId,
            Referencia = item.Referencia,
            Mes = item.Mes,
            Cliente = item.Cliente,
            ClienteId = item.ClienteId,
            AccountId = item.AccountId,
            Account = item.Account,
            Producto = item.Producto,
            ProductoId = item.ProductoId,
            TipoContrato = item.TipoContrato,
            TipoContratoValue = item.TipoContratoValue,
            Vertical = item.Vertical,
            VerticalValue = item.VerticalValue,
            Fecha = item.Fecha,
            Valor = item.Valor,
            Reason = reason
        };

    private static IReadOnlyList<LicenciamientoCruceOptionDto> BuildLicenciamientoCruceCostContractTypeOptions() =>
        new[]
        {
            new LicenciamientoCruceOptionDto { Value = LicenciamientoCruceCostMonthlyOption, Label = "Monthly" },
            new LicenciamientoCruceOptionDto { Value = LicenciamientoCruceCostOneTimeOption, Label = "Onetime" },
            new LicenciamientoCruceOptionDto { Value = LicenciamientoCruceCostPrepaidOption, Label = "Prepaid" }
        };

    private static IReadOnlyList<LicenciamientoCruceOptionDto> BuildLicenciamientoCruceBillingContractTypeOptions() =>
        new[]
        {
            new LicenciamientoCruceOptionDto { Value = LicenciamientoCruceBillingMonthlyOption, Label = "Mensual" },
            new LicenciamientoCruceOptionDto { Value = LicenciamientoCruceBillingOneTimeOption, Label = "OneTime" }
        };

    private static IReadOnlyList<LicenciamientoCruceOptionDto> BuildLicenciamientoCruceBillingVerticalOptions() =>
        new[]
        {
            new LicenciamientoCruceOptionDto { Value = DashboardVerticalCloudOption, Label = "Cloud" },
            new LicenciamientoCruceOptionDto { Value = DashboardVerticalCopiersOption, Label = "Copiers" }
        };

    private static LicenciamientoCruceTotalsDto BuildLicenciamientoCruceTotals(
        IReadOnlyList<LicenciamientoCruceRowDto> rows)
    {
        var cost = RoundCurrency(rows.Sum(static row => row.CostoLicenciamiento));
        var billing = RoundCurrency(rows.Sum(static row => row.FacturacionSinIva));
        var margin = RoundCurrency(billing - cost);

        return new LicenciamientoCruceTotalsDto
        {
            TotalCostosLicenciamiento = cost,
            TotalFacturacionRelacionada = billing,
            MargenBrutoTotal = margin,
            MargenBrutoPct = CalculateLicenciamientoCruceMarginPercent(margin, billing),
            TotalUtilidadPositiva = RoundCurrency(rows.Where(static row => row.MargenBruto > 0m).Sum(static row => row.MargenBruto)),
            TotalUtilidadNegativa = RoundCurrency(rows.Where(static row => row.MargenBruto < 0m).Sum(static row => row.MargenBruto)),
            TotalCostosFuente = cost,
            TotalCostosCruce = cost,
            TotalFacturacionFuenteSinIva = billing
        };
    }

    private static IReadOnlyList<LicenciamientoCruceAlertDto> BuildLicenciamientoCruceAlerts(
        IReadOnlyList<LicenciamientoCruceRowDto> rows)
    {
        var negativeRows = rows.Where(static row => row.MargenBruto < 0m).ToList();

        return new[]
        {
            new LicenciamientoCruceAlertDto
            {
                Key = "negative-margin",
                Label = "Margen negativo",
                Severity = negativeRows.Count > 0 ? "danger" : "ok",
                Count = negativeRows.Count,
                Value = RoundCurrency(negativeRows.Sum(static row => row.MargenBruto))
            }
        };
    }

    private IReadOnlyList<LicenciamientoCruceValidationDto> BuildLicenciamientoCruceValidations(
        IReadOnlyList<LicenciamientoCruceCostRow> costRows,
        IReadOnlyList<BillingRecordRow> billingRows,
        IReadOnlyList<LicenciamientoCruceRowDto> rows,
        decimal totalCostSource,
        decimal totalCostCross,
        DateOnly billingMonth,
        int excludedCopiersBillingCount)
    {
        var invalidVatRows = billingRows.Count(static row => row.TotalInvoice < row.VatValue);
        var missingVatValueRows = billingRows.Count(static row => row.TotalInvoice > 0m && row.VatPercent > 0m && Math.Abs(row.VatValue) < 0.01m);
        var duplicateCostCount = costRows.Count - costRows.Select(static row => row.RecordId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var duplicateBillingCount = billingRows.Count - billingRows.Select(static row => row.RecordId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var unmatchedCount = rows.Count(static row =>
            string.Equals(row.EstadoCruce, LicenciamientoCruceStatusCostOnly, StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.EstadoCruce, LicenciamientoCruceStatusBillingOnly, StringComparison.OrdinalIgnoreCase));
        var costDelta = Math.Abs(totalCostSource - totalCostCross);

        return new[]
        {
            new LicenciamientoCruceValidationDto
            {
                Key = "cost-date",
                Label = "Fecha costo",
                Status = "ok",
                Detail = $"Los costos se filtran por {LicensingInvoiceDateField} en {FormatLicenciamientoCruceMonth(billingMonth)}; no se usa modifiedon."
            },
            new LicenciamientoCruceValidationDto
            {
                Key = "billing-date",
                Label = "Fecha facturacion",
                Status = "ok",
                Detail = $"La facturacion se filtra por {_dashboardBillingEmissionDateField}; no se usa fecha de modificacion."
            },
            new LicenciamientoCruceValidationDto
            {
                Key = "billing-without-vat",
                Label = "Facturacion sin IVA",
                Status = invalidVatRows > 0 ? "error" : missingVatValueRows > 0 ? "warning" : "ok",
                Detail = invalidVatRows > 0
                    ? $"{invalidVatRows:N0} factura(s) tienen IVA mayor al total."
                    : missingVatValueRows > 0
                        ? $"{missingVatValueRows:N0} factura(s) tienen porcentaje de IVA pero valor IVA en cero; se uso total_factura - iva."
                        : "La base se calcula como total_factura - iva."
            },
            new LicenciamientoCruceValidationDto
            {
                Key = "billing-copiers-excluded",
                Label = "Facturas Copiers",
                Status = "ok",
                Detail = $"{excludedCopiersBillingCount:N0} factura(s) con vertical Copiers fueron excluidas del cruce."
            },
            new LicenciamientoCruceValidationDto
            {
                Key = "duplicates",
                Label = "Duplicados",
                Status = duplicateCostCount > 0 || duplicateBillingCount > 0 ? "warning" : "ok",
                Detail = $"Costos duplicados por ID: {duplicateCostCount:N0}. Facturas duplicadas por ID: {duplicateBillingCount:N0}."
            },
            new LicenciamientoCruceValidationDto
            {
                Key = "unmatched-visible",
                Label = "Sin match visibles",
                Status = "ok",
                Detail = $"{unmatchedCount:N0} registro(s) sin match quedan visibles en el detalle."
            },
            new LicenciamientoCruceValidationDto
            {
                Key = "cost-total",
                Label = "Total costos",
                Status = costDelta < 0.01m ? "ok" : "error",
                Detail = $"Fuente: {totalCostSource:N2}. Cruce: {totalCostCross:N2}."
            }
        };
    }

    private static decimal CalculateLicenciamientoCruceBillingWithoutVat(BillingRecordRow row)
    {
        var value = row.TotalInvoice - row.VatValue;
        return RoundCurrency(value < 0m ? 0m : value);
    }

    private static decimal? CalculateLicenciamientoCruceMarginPercent(decimal margin, decimal billingWithoutVat)
    {
        if (Math.Abs(billingWithoutVat) < 0.01m)
            return null;

        return RoundCurrency((margin / billingWithoutVat) * 100m);
    }

    private static LicenciamientoCruceTraceDto BuildLicenciamientoCruceTrace(
        LicenciamientoCruceCostGroup? cost,
        LicenciamientoCruceBillingGroup? billing,
        string status,
        decimal matchScore)
    {
        var costClientId = NormalizeOptionalGuid(cost?.ClientId);
        var billingClientId = NormalizeOptionalGuid(billing?.ClientId);
        var hasBothClientIds = !string.IsNullOrWhiteSpace(costClientId) && !string.IsNullOrWhiteSpace(billingClientId);
        var matchMode = status switch
        {
            LicenciamientoCruceStatusExact when hasBothClientIds => "Cliente por lookup + tipo de contrato",
            LicenciamientoCruceStatusExact => "Cliente por nombre + tipo de contrato",
            LicenciamientoCruceStatusProbable => $"Nombre probable ({matchScore:N2}%) + tipo de contrato",
            LicenciamientoCruceStatusCostOnly => "No se encontro facturacion compatible",
            LicenciamientoCruceStatusBillingOnly => "No se encontro costo compatible",
            _ => status
        };

        return new LicenciamientoCruceTraceDto
        {
            MatchMode = matchMode,
            Rule = "Costos: Account ID -> cliente padre; Facturacion: cliente lookup. Luego se compara cliente padre y tipo de contrato normalizado.",
            CostClientId = costClientId,
            BillingClientId = billingClientId,
            CostGroupKey = cost?.GroupKey ?? "",
            BillingGroupKey = billing?.GroupKey ?? "",
            CostItems = cost?.CostItems ?? Array.Empty<LicenciamientoCruceTraceItemDto>(),
            BillingItems = billing?.BillingItems ?? Array.Empty<LicenciamientoCruceTraceItemDto>()
        };
    }

    private static LicenciamientoCruceTraceItemDto BuildLicenciamientoCruceCostTraceItem(
        LicenciamientoCruceCostRow row) =>
        new()
        {
            Fuente = "cr07a_consumointcomex",
            RecordId = row.RecordId,
            Referencia = FirstNonEmpty(row.CompanyAccountDisplay, row.AccountId, row.RecordId),
            Cliente = row.ClientName,
            ClienteId = row.ClientId,
            AccountId = row.AccountId,
            Account = row.CompanyAccountDisplay,
            AccountIdOriginal = FirstNonEmpty(row.OriginalAccountId, row.AccountId),
            AccountOriginal = FirstNonEmpty(row.OriginalAccountDisplay, row.CompanyAccountDisplay),
            AccountMappingId = row.AccountMappingId,
            AccountMappingApplied = row.AccountMappingApplied,
            Producto = row.ProductDisplay,
            ProductoId = row.ProductId,
            TipoContrato = row.ContractTypeLabel,
            TipoContratoValue = row.ContractTypeValue,
            Vertical = FirstNonEmpty(row.Vendor, "Licenciamiento"),
            Fecha = row.InvoiceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            Mes = FormatLicenciamientoCruceMonth(GetLicenciamientoCruceCostMonth(row)),
            Valor = row.CostCop,
            ValorTotal = row.CostCop
        };

    private static LicenciamientoCruceTraceItemDto BuildLicenciamientoCruceBillingTraceItem(
        BillingRecordRow row)
    {
        var withoutVat = CalculateLicenciamientoCruceBillingWithoutVat(row);
        return new LicenciamientoCruceTraceItemDto
        {
            Fuente = "cr07a_facturacion",
            RecordId = row.RecordId,
            Referencia = FirstNonEmpty(row.InvoiceNumber, row.RecordId),
            Cliente = row.ClientName,
            ClienteId = NormalizeOptionalGuid(row.ClientId),
            TipoContrato = row.ContractTypeLabel,
            TipoContratoValue = row.ContractTypeOptionValue,
            Vertical = row.VerticalLabel,
            VerticalValue = row.VerticalOptionValue,
            Fecha = row.EmissionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            Mes = row.EmissionDate.HasValue ? FormatLicenciamientoCruceMonth(new DateOnly(row.EmissionDate.Value.Year, row.EmissionDate.Value.Month, 1)) : "",
            Valor = withoutVat,
            ValorTotal = row.TotalInvoice,
            Iva = row.VatValue
        };
    }

    private static void AddLicenciamientoCruceMatchKeys(HashSet<string> keys, params string?[] values)
    {
        foreach (var value in values)
        {
            var clientId = NormalizeOptionalGuid(value);
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                keys.Add($"client:{clientId}");
                continue;
            }

            var clientKey = NormalizeLicenciamientoCruceClientKey(value);
            if (!string.IsNullOrWhiteSpace(clientKey))
                keys.Add($"name:{clientKey}");
        }
    }

    private static string BuildLicenciamientoCruceGroupingKey(params string?[] values)
    {
        foreach (var value in values)
        {
            var clientId = NormalizeOptionalGuid(value);
            if (!string.IsNullOrWhiteSpace(clientId))
                return $"client:{clientId}";
        }

        foreach (var value in values)
        {
            var clientKey = NormalizeLicenciamientoCruceClientKey(value);
            if (!string.IsNullOrWhiteSpace(clientKey))
                return $"name:{clientKey}";
        }

        return Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
    }

    private static bool HasLicenciamientoCruceExactClientMatch(
        LicenciamientoCruceCostGroup cost,
        LicenciamientoCruceBillingGroup billing)
    {
        var costClientId = NormalizeOptionalGuid(cost.ClientId);
        var billingClientId = NormalizeOptionalGuid(billing.ClientId);
        if (!string.IsNullOrWhiteSpace(costClientId) && !string.IsNullOrWhiteSpace(billingClientId))
            return string.Equals(costClientId, billingClientId, StringComparison.OrdinalIgnoreCase);

        return cost.MatchKeys.Overlaps(billing.MatchKeys);
    }

    private static bool CanUseLicenciamientoCruceNameFallback(
        LicenciamientoCruceCostGroup cost,
        LicenciamientoCruceBillingGroup billing)
    {
        return !string.IsNullOrWhiteSpace(NormalizeLicenciamientoCruceClientKey(cost.ClientName))
            && !string.IsNullOrWhiteSpace(NormalizeLicenciamientoCruceClientKey(billing.ClientName));
    }

    private static decimal CalculateLicenciamientoCruceClientSimilarity(string? left, string? right)
    {
        var leftKey = NormalizeLicenciamientoCruceClientKey(left);
        var rightKey = NormalizeLicenciamientoCruceClientKey(right);
        if (string.IsNullOrWhiteSpace(leftKey) || string.IsNullOrWhiteSpace(rightKey))
            return 0m;

        if (string.Equals(leftKey, rightKey, StringComparison.OrdinalIgnoreCase))
            return 1m;

        if (leftKey.Contains(rightKey, StringComparison.OrdinalIgnoreCase)
            || rightKey.Contains(leftKey, StringComparison.OrdinalIgnoreCase))
        {
            var minLength = Math.Min(leftKey.Length, rightKey.Length);
            var maxLength = Math.Max(leftKey.Length, rightKey.Length);
            return maxLength == 0 ? 0m : minLength / (decimal)maxLength;
        }

        var leftTokens = leftKey.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rightTokens = rightKey.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
            return 0m;

        var intersection = leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        var union = leftTokens.Union(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0m : intersection / (decimal)union;
    }

    private static string NormalizeLicenciamientoCruceClientKey(string? value)
    {
        var text = RemoveLicenciamientoCruceDiacritics(value ?? "").ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var builder = new StringBuilder(text.Length);
        var previousWasSpace = false;
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                previousWasSpace = false;
                continue;
            }

            if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        var tokens = builder
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !LicenciamientoCruceLegalTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return string.Join(" ", tokens);
    }

    private static string NormalizeLicenciamientoCruceMapKey(string? value)
    {
        var text = RemoveLicenciamientoCruceDiacritics(value ?? "").ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(ch);
        }

        return builder.ToString();
    }

    private static bool IsLicenciamientoCruceCopiersVertical(string? value)
    {
        var normalized = NormalizeLicenciamientoCruceClientKey(value);
        return string.Equals(normalized, "COPIERS", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("COPIERS", StringComparison.OrdinalIgnoreCase);
    }

    private static string RemoveLicenciamientoCruceDiacritics(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static DateOnly? TryParseLicenciamientoCruceMonth(string? rawValue)
    {
        var raw = (rawValue ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (TryParseDateOnly(raw, out var parsedDate))
            return new DateOnly(parsedDate.Year, parsedDate.Month, 1);

        var normalized = raw
            .Replace('.', '/')
            .Replace('-', '/')
            .Replace('\\', '/');
        var parts = normalized.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        var first = parts[0];
        var second = parts[1];
        int year;
        int month;
        if (first.Length == 4)
        {
            if (!int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out year)
                || !int.TryParse(second, NumberStyles.Integer, CultureInfo.InvariantCulture, out month))
                return null;
        }
        else
        {
            if (!int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out month)
                || !int.TryParse(second, NumberStyles.Integer, CultureInfo.InvariantCulture, out year))
                return null;
        }

        if (month is < 1 or > 12 || year is < 1900 or > 2100)
            return null;

        return new DateOnly(year, month, 1);
    }

    private static string ResolveLicenciamientoCruceMostCommonText(IEnumerable<string?> values, string fallback)
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

    private static DateOnly? ResolveLicenciamientoCruceMostCommonMonth(IEnumerable<DateOnly> values)
    {
        return values
            .Select(static value => new DateOnly(value.Year, value.Month, 1))
            .GroupBy(static value => value)
            .OrderByDescending(static group => group.Count())
            .ThenByDescending(static group => group.Key)
            .Select(static group => (DateOnly?)group.Key)
            .FirstOrDefault();
    }

    private static string FormatLicenciamientoCruceMonth(DateOnly value) =>
        value.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static string ResolveLicenciamientoCruceContractKey(string? value)
    {
        var normalized = NormalizeLicenciamientoCruceClientKey(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return LicenciamientoCruceOtherKey;

        if (normalized.Contains("ONETIME", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("ONE TIME", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("PREPAID", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("PRE PAID", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("PREPAGO", StringComparison.OrdinalIgnoreCase))
        {
            return LicenciamientoCruceOneTimeKey;
        }

        if (normalized.Contains("MONTHLY", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("MONTLHY", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("MENSUAL", StringComparison.OrdinalIgnoreCase))
        {
            return LicenciamientoCruceMonthlyKey;
        }

        return LicenciamientoCruceOtherKey;
    }

    private static string ResolveLicenciamientoCruceContractKey(int optionValue, string? label, bool isBillingSource)
    {
        if (isBillingSource)
        {
            return optionValue switch
            {
                LicenciamientoCruceBillingMonthlyOption => LicenciamientoCruceMonthlyKey,
                LicenciamientoCruceBillingOneTimeOption => LicenciamientoCruceOneTimeKey,
                _ => ResolveLicenciamientoCruceContractKey(label)
            };
        }

        return optionValue switch
        {
            LicenciamientoCruceCostMonthlyOption or LicenciamientoCruceCostMonthlyLegacyOption => LicenciamientoCruceMonthlyKey,
            LicenciamientoCruceCostOneTimeOption or LicenciamientoCruceCostPrepaidOption => LicenciamientoCruceOneTimeKey,
            _ => ResolveLicenciamientoCruceContractKey(label)
        };
    }

    private static string ResolveLicenciamientoCruceContractLabel(string? keyOrLabel)
    {
        var key = string.Equals(keyOrLabel, LicenciamientoCruceMonthlyKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(keyOrLabel, LicenciamientoCruceOneTimeKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(keyOrLabel, LicenciamientoCruceOtherKey, StringComparison.OrdinalIgnoreCase)
                ? keyOrLabel
                : ResolveLicenciamientoCruceContractKey(keyOrLabel);

        return key switch
        {
            LicenciamientoCruceMonthlyKey => LicenciamientoCruceMonthlyLabel,
            LicenciamientoCruceOneTimeKey => LicenciamientoCruceOneTimeLabel,
            _ => LicenciamientoCruceOtherLabel
        };
    }

    private static int ResolveLicenciamientoCruceContractOrder(string? key)
    {
        return key switch
        {
            LicenciamientoCruceMonthlyKey => 0,
            LicenciamientoCruceOneTimeKey => 1,
            _ => 2
        };
    }

    private static int ResolveLicenciamientoCruceStateOrder(string status)
    {
        return status switch
        {
            LicenciamientoCruceStatusCostOnly => 0,
            LicenciamientoCruceStatusBillingOnly => 1,
            LicenciamientoCruceStatusProbable => 2,
            LicenciamientoCruceStatusExact => 3,
            _ => 4
        };
    }

    private sealed class LicenciamientoCruceCostRow
    {
        public string RecordId { get; set; } = "";
        public string AccountId { get; set; } = "";
        public string OriginalAccountId { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string ClientName { get; set; } = "";
        public string CompanyAccountDisplay { get; set; } = "";
        public string OriginalAccountDisplay { get; set; } = "";
        public string AccountMappingId { get; set; } = "";
        public bool AccountMappingApplied { get; set; }
        public string Vendor { get; set; } = "";
        public string ProductId { get; set; } = "";
        public string ProductDisplay { get; set; } = "";
        public int ContractTypeValue { get; set; }
        public string ContractTypeKey { get; set; } = LicenciamientoCruceOtherKey;
        public string ContractTypeLabel { get; set; } = LicenciamientoCruceOtherLabel;
        public DateOnly? InvoiceDate { get; set; }
        public DateOnly CostMonth { get; set; }
        public decimal CostCop { get; set; }
    }

    private sealed record LicenciamientoCruceMapAttributeDefinition(
        string LogicalName,
        string SchemaName,
        string DisplayName,
        string Kind,
        int MaxLength);

    private sealed class LicenciamientoCruceAccountMapping
    {
        public string MappingId { get; init; } = "";
        public string SourceAccountId { get; init; } = "";
        public string SourceAccountName { get; init; } = "";
        public string SourceClientName { get; init; } = "";
        public string TargetAccountId { get; init; } = "";
        public string TargetAccountName { get; init; } = "";
        public string TargetClientId { get; init; } = "";
        public string TargetClientName { get; init; } = "";
        public bool Active { get; init; } = true;
        public string Notes { get; init; } = "";
    }

    private sealed class LicenciamientoCruceCostGroup
    {
        public string GroupKey { get; init; } = "";
        public string ClientId { get; init; } = "";
        public string ContractTypeKey { get; init; } = LicenciamientoCruceOtherKey;
        public string ContractTypeLabel { get; init; } = LicenciamientoCruceOtherLabel;
        public string ClientName { get; init; } = "";
        public string Vertical { get; init; } = "";
        public DateOnly CostMonth { get; init; }
        public decimal CostCop { get; init; }
        public IReadOnlyList<string> RecordIds { get; init; } = Array.Empty<string>();
        public HashSet<string> MatchKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<LicenciamientoCruceTraceItemDto> CostItems { get; init; } = Array.Empty<LicenciamientoCruceTraceItemDto>();
    }

    private sealed class LicenciamientoCruceBillingGroup
    {
        public string GroupKey { get; init; } = "";
        public string ClientId { get; init; } = "";
        public string ContractTypeKey { get; init; } = LicenciamientoCruceOtherKey;
        public string ContractTypeLabel { get; init; } = LicenciamientoCruceOtherLabel;
        public string ClientName { get; init; } = "";
        public string Nit { get; init; } = "";
        public string Vertical { get; init; } = "";
        public decimal BillingWithoutVat { get; init; }
        public IReadOnlyList<string> BillingRecordIds { get; init; } = Array.Empty<string>();
        public HashSet<string> MatchKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<LicenciamientoCruceTraceItemDto> BillingItems { get; init; } = Array.Empty<LicenciamientoCruceTraceItemDto>();
        public bool HasInvalidVat { get; init; }
        public bool HasMissingVatValue { get; init; }
    }

    private sealed class LicenciamientoCrucePeriod
    {
        public DateOnly SelectedMonth { get; init; }
        public DateOnly Start { get; init; }
        public DateOnly End { get; init; }
        public string Mode { get; init; } = "month";
        public string Label { get; init; } = "";
    }
}
