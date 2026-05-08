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
    private const string LicenciamientoCruceOtherKey = "otros";
    private const string LicenciamientoCruceOtherLabel = "Sin tipo";
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

        var totals = new LicenciamientoCruceTotalsDto
        {
            TotalCostosLicenciamiento = totalCostCross,
            TotalFacturacionRelacionada = totalBillingCross,
            MargenBrutoTotal = totalMargin,
            MargenBrutoPct = totalMarginPct,
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
            ClientName = clientName,
            CompanyAccountDisplay = record.CompanyAccountDisplay,
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
            LicenciamientoCruceOtherKey
        };
        var segments = new List<LicenciamientoCruceMatrixSegmentDto>();

        foreach (var key in orderedKeys)
        {
            var segmentRows = rows
                .Where(row => string.Equals(row.TipoContratoKey, key, StringComparison.OrdinalIgnoreCase))
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
                Label = ResolveLicenciamientoCruceContractLabel(key),
                RecordsCount = clientRows.Count,
                NegativeMarginCount = clientRows.Count(static row => row.HasNegativeMargin),
                OrphanCount = segmentRows.Count(static row =>
                    string.Equals(row.EstadoCruce, LicenciamientoCruceStatusCostOnly, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(row.EstadoCruce, LicenciamientoCruceStatusBillingOnly, StringComparison.OrdinalIgnoreCase)),
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
                return new LicenciamientoCruceMatrixCellDto
                {
                    Mes = month.Key,
                    CostoLicenciamiento = cost,
                    FacturacionSinIva = billing,
                    UtilidadValor = utility,
                    UtilidadPct = CalculateLicenciamientoCruceMarginPercent(utility, billing),
                    HasNegativeMargin = utility < 0m,
                    HasOrphans = monthRows.Any(static row =>
                        string.Equals(row.EstadoCruce, LicenciamientoCruceStatusCostOnly, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(row.EstadoCruce, LicenciamientoCruceStatusBillingOnly, StringComparison.OrdinalIgnoreCase))
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
        foreach (var row in rows)
        {
            if (string.Equals(row.EstadoCruce, LicenciamientoCruceStatusCostOnly, StringComparison.OrdinalIgnoreCase))
            {
                orphans.AddRange((row.Trace?.CostItems ?? Array.Empty<LicenciamientoCruceTraceItemDto>())
                    .Select(item => BuildLicenciamientoCruceOrphanRecord(item, "cost", row.EstadoCruce, "No hay factura emitida en el mismo mes, con el mismo cliente padre y tipo de contrato.")));
            }
            else if (string.Equals(row.EstadoCruce, LicenciamientoCruceStatusBillingOnly, StringComparison.OrdinalIgnoreCase))
            {
                orphans.AddRange((row.Trace?.BillingItems ?? Array.Empty<LicenciamientoCruceTraceItemDto>())
                    .Select(item => BuildLicenciamientoCruceOrphanRecord(item, "billing", row.EstadoCruce, "No hay costo con mes factura igual, mismo cliente padre y tipo de contrato.")));
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
        var costClientId = NormalizeOptionalGuid(cost.ClientId);
        var billingClientId = NormalizeOptionalGuid(billing.ClientId);
        return string.IsNullOrWhiteSpace(costClientId) || string.IsNullOrWhiteSpace(billingClientId);
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
        public string ClientId { get; set; } = "";
        public string ClientName { get; set; } = "";
        public string CompanyAccountDisplay { get; set; } = "";
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
