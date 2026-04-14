using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const int DashboardVerticalCloudOption = 645250000;
    private const int DashboardVerticalCopiersOption = 645250001;
    private const int DashboardContractTypeMonthlyOption = 645250000;
    private const int DashboardContractTypeOneTimeOption = 645250001;
    private static readonly CultureInfo DashboardCulture = CultureInfo.GetCultureInfo("es-CO");

    public async Task<PortfolioDashboardDto> GetPortfolioDashboardAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var portfolioCandidates = await GetBillingRecordsAsync(
            metadata,
            new DateOnly(2000, 1, 1),
            new DateOnly(2101, 1, 1),
            _dashboardBillingEmissionDateField,
            _dashboardBillingEmissionDateFieldKind,
            httpContext.User,
            ct);

        var unpaidInvoices = portfolioCandidates
            .Where(static record => !record.HasPayment)
            .ToList();

        var overdueInvoices = unpaidInvoices
            .Where(record => record.IsOverdue(today))
            .ToList();

        return new PortfolioDashboardDto
        {
            AsOfDateLabel = today.ToString("dd MMM yyyy", DashboardCulture),
            FocusLabel = "Cartera total y vencida",
            HasData = unpaidInvoices.Count > 0,
            RecordsCount = overdueInvoices.Count,
            EmptyStateTitle = "No encontramos facturas pendientes de pago.",
            EmptyStateMessage = "Cuando existan facturas sin pago o vencidas las veras aqui.",
            Kpis = BuildPortfolioKpis(unpaidInvoices, overdueInvoices),
            OverdueInvoices = BuildUnpaidInvoices(overdueInvoices, today)
        };
    }

    public async Task<BillingDashboardDto> GetBillingDashboardAsync(
        int year,
        BillingPeriodKind periodKind,
        int? periodValue = null,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        var period = BuildBillingPeriodDefinition(year, periodKind, periodValue, today);
        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var emissionRecords = await GetBillingRecordsAsync(
            metadata,
            period.CompareStartInclusive,
            period.CurrentEndExclusive,
            _dashboardBillingEmissionDateField,
            _dashboardBillingEmissionDateFieldKind,
            httpContext.User,
            ct);

        var paymentRecords = await GetBillingRecordsAsync(
            metadata,
            period.CompareStartInclusive,
            period.CurrentEndExclusive,
            _dashboardBillingPaymentDateField,
            _dashboardBillingPaymentDateFieldKind,
            httpContext.User,
            ct);

        var currentEmission = emissionRecords
            .Where(record => record.EmissionDate is not null
                && record.EmissionDate.Value >= period.CurrentStartInclusive
                && record.EmissionDate.Value < period.CurrentEndExclusive)
            .ToList();

        var compareEmission = emissionRecords
            .Where(record => record.EmissionDate is not null
                && record.EmissionDate.Value >= period.CompareStartInclusive
                && record.EmissionDate.Value < period.CompareEndExclusive)
            .ToList();

        var currentPayments = paymentRecords
            .Where(record => record.PaymentDate is not null
                && record.PaymentDate.Value >= period.CurrentStartInclusive
                && record.PaymentDate.Value < period.CurrentEndExclusive)
            .ToList();

        var comparePayments = paymentRecords
            .Where(record => record.PaymentDate is not null
                && record.PaymentDate.Value >= period.CompareStartInclusive
                && record.PaymentDate.Value < period.CompareEndExclusive)
            .ToList();

        var totalBilling = SumCurrency(currentEmission, static record => record.TotalInvoice);
        var previousTotalBilling = SumCurrency(compareEmission, static record => record.TotalInvoice);
        var totalCollections = SumCurrency(currentPayments, static record => record.PaymentValue);
        var previousTotalCollections = SumCurrency(comparePayments, static record => record.PaymentValue);
        var totalVat = SumCurrency(currentEmission, static record => record.VatValue);
        var previousTotalVat = SumCurrency(compareEmission, static record => record.VatValue);
        var totalRetentions = SumCurrency(currentPayments, static record => record.RetentionsTotal);
        var previousTotalRetentions = SumCurrency(comparePayments, static record => record.RetentionsTotal);
        var unpaidInvoices = BuildUnpaidInvoices(currentEmission, today);
        var previousUnpaidAmount = SumCurrency(compareEmission.Where(static record => !record.HasPayment), static record => record.TotalInvoice);
        var differenceInvoices = BuildDifferenceInvoices(currentEmission);
        var previousDifferenceAmount = SumCurrency(
            compareEmission.Where(static record => record.HasPayment),
            static record => Math.Abs(record.DifferenceValue));

        var hasData = currentEmission.Count > 0
            || compareEmission.Count > 0
            || currentPayments.Count > 0
            || comparePayments.Count > 0;

        return new BillingDashboardDto
        {
            Year = period.Year,
            CompareYear = period.CompareYear,
            PeriodKind = period.PeriodKind.ToKey(),
            PeriodKindLabel = period.PeriodKind.ToLabel(),
            PeriodValue = period.PeriodValue,
            PeriodLabel = period.PeriodLabel,
            DateRangeLabel = period.DateRangeLabel,
            CompareLabel = period.CompareLabel,
            GranularityLabel = period.GranularityLabel,
            EmptyStateTitle = "No encontramos facturacion para este periodo.",
            EmptyStateMessage = "Cambia el rango y seguimos comparando contra el mismo periodo del año anterior.",
            HasData = hasData,
            RecordsCount = currentEmission.Count,
            CompareRecordsCount = compareEmission.Count,
            Kpis = BuildBillingKpis(
                currentEmission,
                compareEmission,
                currentPayments,
                comparePayments,
                totalBilling,
                previousTotalBilling,
                totalCollections,
                previousTotalCollections,
                totalVat,
                previousTotalVat,
                totalRetentions,
                previousTotalRetentions,
                unpaidInvoices,
                previousUnpaidAmount,
                differenceInvoices,
                previousDifferenceAmount),
            Trend = BuildBillingTrend(period, currentEmission, compareEmission, currentPayments, comparePayments),
            Verticals = BuildVerticalSummaries(currentEmission, compareEmission),
            TopClients = BuildClientSummaries(currentEmission, compareEmission),
            Retentions = BuildRetentionSummaries(currentPayments, comparePayments),
            UnpaidInvoices = unpaidInvoices,
            DifferenceInvoices = differenceInvoices
        };
    }

    private async Task<List<BillingRecordRow>> GetBillingRecordsAsync(
        RhEntityMetadata metadata,
        DateOnly startInclusive,
        DateOnly endExclusive,
        string filterField,
        string filterFieldKind,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var select = string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            _dashboardBillingInvoiceNumberField,
            _dashboardBillingCompanyTaxIdField,
            _dashboardBillingClientField,
            BuildDashboardLookupValuePropertyName(_dashboardBillingClientField),
            _dashboardBillingVerticalField,
            _dashboardBillingContractTypeField,
            _dashboardBillingDueDateField,
            _dashboardBillingEmissionDateField,
            _dashboardBillingTotalField,
            _dashboardBillingVatField,
            _dashboardBillingPaymentDateField,
            _dashboardBillingPaymentValueField,
            _dashboardBillingReteIcaField,
            _dashboardBillingRteIvaField,
            _dashboardBillingRteFteField,
            _dashboardBillingDifferenceField
        }
        .Where(static field => !string.IsNullOrWhiteSpace(field))
        .Distinct(StringComparer.OrdinalIgnoreCase));

        var filter = BuildBillingDateFilter(filterField, filterFieldKind, startInclusive, endExclusive);
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={filterField} asc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => ParseBillingRecord(item, metadata.PrimaryIdField, metadata.PrimaryNameField))
            .Where(static item => item is not null)
            .Cast<BillingRecordRow>()
            .GroupBy(item => item.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private BillingRecordRow? ParseBillingRecord(JsonElement item, string primaryIdField, string primaryNameField)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, primaryIdField),
            ReadString(item, _dashboardBillingIdField),
            ReadString(item, _dashboardBillingInvoiceNumberField));

        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var verticalOption = ReadInt(item, _dashboardBillingVerticalField);
        var contractTypeOption = ReadInt(item, _dashboardBillingContractTypeField);

        return new BillingRecordRow
        {
            RecordId = recordId.Trim(),
            InvoiceNumber = FirstNonEmpty(
                ReadString(item, $"{_dashboardBillingInvoiceNumberField}{FormattedValueAnnotationSuffix}"),
                ReadString(item, _dashboardBillingInvoiceNumberField),
                ReadString(item, primaryNameField),
                recordId),
            CompanyTaxId = ReadString(item, _dashboardBillingCompanyTaxIdField).Trim(),
            ClientName = ReadDashboardClientName(item),
            VerticalOptionValue = verticalOption,
            VerticalLabel = FirstNonEmpty(
                ReadString(item, $"{_dashboardBillingVerticalField}{FormattedValueAnnotationSuffix}"),
                ResolveDashboardVerticalLabel(verticalOption),
                "Sin vertical"),
            ContractTypeOptionValue = contractTypeOption,
            ContractTypeLabel = FirstNonEmpty(
                ReadString(item, $"{_dashboardBillingContractTypeField}{FormattedValueAnnotationSuffix}"),
                ResolveDashboardContractTypeLabel(contractTypeOption),
                "Sin contrato"),
            DueDate = ReadDateOnly(item, _dashboardBillingDueDateField),
            EmissionDate = ReadDateOnly(item, _dashboardBillingEmissionDateField),
            PaymentDate = ReadDateOnly(item, _dashboardBillingPaymentDateField),
            TotalInvoice = RoundCurrency(ReadDecimal(item, _dashboardBillingTotalField) ?? 0m),
            VatValue = RoundCurrency(ReadDecimal(item, _dashboardBillingVatField) ?? 0m),
            PaymentValue = RoundCurrency(ReadDecimal(item, _dashboardBillingPaymentValueField) ?? 0m),
            ReteIcaValue = RoundCurrency(ReadDecimal(item, _dashboardBillingReteIcaField) ?? 0m),
            RteIvaValue = RoundCurrency(ReadDecimal(item, _dashboardBillingRteIvaField) ?? 0m),
            RteFteValue = RoundCurrency(ReadDecimal(item, _dashboardBillingRteFteField) ?? 0m),
            DifferenceValue = RoundCurrency(ReadDecimal(item, _dashboardBillingDifferenceField) ?? 0m)
        };
    }

    private IReadOnlyList<BillingKpiDto> BuildBillingKpis(
        IReadOnlyList<BillingRecordRow> currentEmission,
        IReadOnlyList<BillingRecordRow> compareEmission,
        IReadOnlyList<BillingRecordRow> currentPayments,
        IReadOnlyList<BillingRecordRow> comparePayments,
        decimal totalBilling,
        decimal previousTotalBilling,
        decimal totalCollections,
        decimal previousTotalCollections,
        decimal totalVat,
        decimal previousTotalVat,
        decimal totalRetentions,
        decimal previousTotalRetentions,
        IReadOnlyList<BillingUnpaidInvoiceDto> unpaidInvoices,
        decimal previousUnpaidAmount,
        IReadOnlyList<BillingDifferenceInvoiceDto> differenceInvoices,
        decimal previousDifferenceAmount)
    {
        var currentCloudBilling = SumCurrency(
            currentEmission.Where(static record => record.VerticalOptionValue == DashboardVerticalCloudOption),
            static record => record.TotalInvoice);
        var previousCloudBilling = SumCurrency(
            compareEmission.Where(static record => record.VerticalOptionValue == DashboardVerticalCloudOption),
            static record => record.TotalInvoice);
        var currentCopiersBilling = SumCurrency(
            currentEmission.Where(static record => record.VerticalOptionValue == DashboardVerticalCopiersOption),
            static record => record.TotalInvoice);
        var previousCopiersBilling = SumCurrency(
            compareEmission.Where(static record => record.VerticalOptionValue == DashboardVerticalCopiersOption),
            static record => record.TotalInvoice);
        var cloudRows = currentEmission
            .Where(static record => record.VerticalOptionValue == DashboardVerticalCloudOption)
            .ToList();
        var copiersRows = currentEmission
            .Where(static record => record.VerticalOptionValue == DashboardVerticalCopiersOption)
            .ToList();

        return new[]
        {
            BuildBillingKpi("total-billing", "Facturacion total", "Emitida con fecha de emision dentro del periodo.", totalBilling, previousTotalBilling, "currency", "Facturas", currentEmission.Count.ToString("N0", DashboardCulture)),
            BuildBillingKpi(
                "cloud-billing",
                "Facturacion Vertical Cloud",
                "Facturacion emitida en Cloud.",
                currentCloudBilling,
                previousCloudBilling,
                "currency",
                "Participacion periodo",
                FormatPercentValue(totalBilling == 0m ? 0m : (currentCloudBilling / totalBilling) * 100m),
                breakdowns: BuildVerticalContractBreakdowns(cloudRows)),
            BuildBillingKpi(
                "copiers-billing",
                "Facturacion Vertical Copiers",
                "Facturacion emitida en Copiers.",
                currentCopiersBilling,
                previousCopiersBilling,
                "currency",
                "Participacion periodo",
                FormatPercentValue(totalBilling == 0m ? 0m : (currentCopiersBilling / totalBilling) * 100m),
                breakdowns: BuildVerticalContractBreakdowns(copiersRows))
        };
    }

    private IReadOnlyList<PortfolioKpiDto> BuildPortfolioKpis(
        IReadOnlyList<BillingRecordRow> unpaidInvoices,
        IReadOnlyList<BillingRecordRow> overdueInvoices)
    {
        var unpaidCloudRows = unpaidInvoices
            .Where(static record => record.VerticalOptionValue == DashboardVerticalCloudOption)
            .ToList();
        var unpaidCopiersRows = unpaidInvoices
            .Where(static record => record.VerticalOptionValue == DashboardVerticalCopiersOption)
            .ToList();
        var overdueCloudRows = overdueInvoices
            .Where(static record => record.VerticalOptionValue == DashboardVerticalCloudOption)
            .ToList();
        var overdueCopiersRows = overdueInvoices
            .Where(static record => record.VerticalOptionValue == DashboardVerticalCopiersOption)
            .ToList();

        return new[]
        {
            BuildPortfolioKpi(
                "cloud-portfolio",
                "Cartera Cloud",
                "Total de facturas Cloud sin pago, incluyendo el monto ya vencido.",
                SumCurrency(unpaidCloudRows, static record => record.TotalInvoice),
                SumCurrency(overdueCloudRows, static record => record.TotalInvoice)),
            BuildPortfolioKpi(
                "copiers-portfolio",
                "Cartera Copiers",
                "Total de facturas Copiers sin pago, incluyendo el monto ya vencido.",
                SumCurrency(unpaidCopiersRows, static record => record.TotalInvoice),
                SumCurrency(overdueCopiersRows, static record => record.TotalInvoice))
        };
    }

    private PortfolioKpiDto BuildPortfolioKpi(
        string key,
        string label,
        string hint,
        decimal value,
        decimal overdueValue)
    {
        return new PortfolioKpiDto
        {
            Key = key,
            Label = label,
            Hint = hint,
            Value = RoundCurrency(value),
            ValueFormat = "currency",
            SecondaryLabel = "Vencidas sin pago",
            SecondaryValue = FormatCurrencyValue(overdueValue)
        };
    }

    private BillingKpiDto BuildBillingKpi(
        string key,
        string label,
        string hint,
        decimal value,
        decimal previousValue,
        string valueFormat,
        string secondaryLabel,
        string secondaryValue,
        IReadOnlyList<BillingKpiBreakdownDto>? breakdowns = null,
        bool lowerIsBetter = false)
    {
        return new BillingKpiDto
        {
            Key = key,
            Label = label,
            Hint = hint,
            Value = RoundCurrency(value),
            PreviousValue = RoundCurrency(previousValue),
            GrowthPercent = CalculateGrowthPercent(value, previousValue),
            ValueFormat = valueFormat,
            Tone = ResolveTrendTone(value, previousValue, lowerIsBetter),
            SecondaryLabel = secondaryLabel,
            SecondaryValue = secondaryValue,
            Breakdowns = breakdowns ?? Array.Empty<BillingKpiBreakdownDto>()
        };
    }

    private IReadOnlyList<BillingKpiBreakdownDto> BuildVerticalContractBreakdowns(IReadOnlyList<BillingRecordRow> rows)
    {
        var total = SumCurrency(rows, static row => row.TotalInvoice);
        var mensual = SumCurrency(
            rows.Where(static row => row.ContractTypeOptionValue == DashboardContractTypeMonthlyOption),
            static row => row.TotalInvoice);
        var oneTime = SumCurrency(
            rows.Where(static row => row.ContractTypeOptionValue == DashboardContractTypeOneTimeOption),
            static row => row.TotalInvoice);

        return new[]
        {
            new BillingKpiBreakdownDto
            {
                Key = "mensual",
                Label = "Mensual",
                Value = mensual,
                SharePercent = total == 0m ? 0m : RoundCurrency((mensual / total) * 100m)
            },
            new BillingKpiBreakdownDto
            {
                Key = "onetime",
                Label = "OneTime",
                Value = oneTime,
                SharePercent = total == 0m ? 0m : RoundCurrency((oneTime / total) * 100m)
            }
        };
    }

    private IReadOnlyList<BillingTrendPointDto> BuildBillingTrend(
        BillingPeriodDefinition period,
        IReadOnlyList<BillingRecordRow> currentEmission,
        IReadOnlyList<BillingRecordRow> compareEmission,
        IReadOnlyList<BillingRecordRow> currentPayments,
        IReadOnlyList<BillingRecordRow> comparePayments)
    {
        return period.Categories
            .Select(category => new BillingTrendPointDto
            {
                Key = category.Key,
                Label = category.Label,
                BillingCurrent = SumCurrency(
                    currentEmission.Where(record => record.EmissionDate is not null
                        && string.Equals(GetBillingCategoryKey(record.EmissionDate.Value, period.CurrentStartInclusive, period.TrendGranularity), category.Key, StringComparison.OrdinalIgnoreCase)),
                    static record => record.TotalInvoice),
                BillingPrevious = SumCurrency(
                    compareEmission.Where(record => record.EmissionDate is not null
                        && string.Equals(GetBillingCategoryKey(record.EmissionDate.Value, period.CompareStartInclusive, period.TrendGranularity), category.Key, StringComparison.OrdinalIgnoreCase)),
                    static record => record.TotalInvoice),
                CollectionsCurrent = SumCurrency(
                    currentPayments.Where(record => record.PaymentDate is not null
                        && string.Equals(GetBillingCategoryKey(record.PaymentDate.Value, period.CurrentStartInclusive, period.TrendGranularity), category.Key, StringComparison.OrdinalIgnoreCase)),
                    static record => record.PaymentValue),
                CollectionsPrevious = SumCurrency(
                    comparePayments.Where(record => record.PaymentDate is not null
                        && string.Equals(GetBillingCategoryKey(record.PaymentDate.Value, period.CompareStartInclusive, period.TrendGranularity), category.Key, StringComparison.OrdinalIgnoreCase)),
                    static record => record.PaymentValue),
                RetentionsCurrent = SumCurrency(
                    currentPayments.Where(record => record.PaymentDate is not null
                        && string.Equals(GetBillingCategoryKey(record.PaymentDate.Value, period.CurrentStartInclusive, period.TrendGranularity), category.Key, StringComparison.OrdinalIgnoreCase)),
                    static record => record.RetentionsTotal),
                RetentionsPrevious = SumCurrency(
                    comparePayments.Where(record => record.PaymentDate is not null
                        && string.Equals(GetBillingCategoryKey(record.PaymentDate.Value, period.CompareStartInclusive, period.TrendGranularity), category.Key, StringComparison.OrdinalIgnoreCase)),
                    static record => record.RetentionsTotal)
            })
            .ToList();
    }

    private IReadOnlyList<BillingVerticalSummaryDto> BuildVerticalSummaries(
        IReadOnlyList<BillingRecordRow> currentEmission,
        IReadOnlyList<BillingRecordRow> compareEmission)
    {
        var currentGroups = currentEmission
            .GroupBy(static record => NormalizeBillingGroupKey(record.VerticalLabel), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var compareGroups = compareEmission
            .GroupBy(static record => NormalizeBillingGroupKey(record.VerticalLabel), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        return currentGroups.Keys
            .Concat(compareGroups.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                currentGroups.TryGetValue(key, out var currentRows);
                compareGroups.TryGetValue(key, out var compareRows);
                currentRows ??= new List<BillingRecordRow>();
                compareRows ??= new List<BillingRecordRow>();

                var currentTotal = SumCurrency(currentRows, static row => row.TotalInvoice);
                var compareTotal = SumCurrency(compareRows, static row => row.TotalInvoice);
                var currentVat = SumCurrency(currentRows, static row => row.VatValue);
                var compareVat = SumCurrency(compareRows, static row => row.VatValue);

                return new BillingVerticalSummaryDto
                {
                    Key = key,
                    Label = currentRows.FirstOrDefault()?.VerticalLabel
                        ?? compareRows.FirstOrDefault()?.VerticalLabel
                        ?? "Sin vertical",
                    InvoicesCount = currentRows.Count,
                    UnpaidInvoicesCount = currentRows.Count(static row => !row.HasPayment),
                    TotalBilling = currentTotal,
                    PreviousTotalBilling = compareTotal,
                    GrowthPercent = CalculateGrowthPercent(currentTotal, compareTotal),
                    TotalVat = currentVat,
                    PreviousTotalVat = compareVat,
                    VatGrowthPercent = CalculateGrowthPercent(currentVat, compareVat),
                    UnpaidAmount = SumCurrency(currentRows.Where(static row => !row.HasPayment), static row => row.TotalInvoice),
                    ContractTypes = BuildContractTypeSummaries(currentRows, compareRows)
                };
            })
            .OrderByDescending(static item => item.TotalBilling)
            .ThenBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<BillingContractTypeSummaryDto> BuildContractTypeSummaries(
        IReadOnlyList<BillingRecordRow> currentRows,
        IReadOnlyList<BillingRecordRow> compareRows)
    {
        var currentGroups = currentRows
            .GroupBy(static row => NormalizeBillingGroupKey(row.ContractTypeLabel), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var compareGroups = compareRows
            .GroupBy(static row => NormalizeBillingGroupKey(row.ContractTypeLabel), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var verticalTotal = SumCurrency(currentRows, static row => row.TotalInvoice);

        return currentGroups.Keys
            .Concat(compareGroups.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                currentGroups.TryGetValue(key, out var currentItems);
                compareGroups.TryGetValue(key, out var compareItems);
                currentItems ??= new List<BillingRecordRow>();
                compareItems ??= new List<BillingRecordRow>();

                var currentTotal = SumCurrency(currentItems, static row => row.TotalInvoice);
                var compareTotal = SumCurrency(compareItems, static row => row.TotalInvoice);

                return new BillingContractTypeSummaryDto
                {
                    Key = key,
                    Label = currentItems.FirstOrDefault()?.ContractTypeLabel
                        ?? compareItems.FirstOrDefault()?.ContractTypeLabel
                        ?? "Sin contrato",
                    TotalBilling = currentTotal,
                    PreviousTotalBilling = compareTotal,
                    GrowthPercent = CalculateGrowthPercent(currentTotal, compareTotal),
                    SharePercent = verticalTotal == 0m ? 0m : RoundCurrency((currentTotal / verticalTotal) * 100m)
                };
            })
            .OrderByDescending(static item => item.TotalBilling)
            .ThenBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<BillingClientSummaryDto> BuildClientSummaries(
        IReadOnlyList<BillingRecordRow> currentEmission,
        IReadOnlyList<BillingRecordRow> compareEmission)
    {
        var currentGroups = currentEmission
            .GroupBy(static record => NormalizeBillingGroupKey(record.ClientName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var compareGroups = compareEmission
            .GroupBy(static record => NormalizeBillingGroupKey(record.ClientName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var totalCurrent = SumCurrency(currentEmission, static record => record.TotalInvoice);

        return currentGroups.Keys
            .Concat(compareGroups.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                currentGroups.TryGetValue(key, out var currentRows);
                compareGroups.TryGetValue(key, out var compareRows);
                currentRows ??= new List<BillingRecordRow>();
                compareRows ??= new List<BillingRecordRow>();

                var currentTotal = SumCurrency(currentRows, static row => row.TotalInvoice);
                var compareTotal = SumCurrency(compareRows, static row => row.TotalInvoice);

                return new BillingClientSummaryDto
                {
                    Key = key,
                    ClientName = currentRows.FirstOrDefault()?.ClientName
                        ?? compareRows.FirstOrDefault()?.ClientName
                        ?? "Cliente sin nombre",
                    InvoicesCount = currentRows.Count,
                    TotalBilling = currentTotal,
                    PreviousTotalBilling = compareTotal,
                    GrowthPercent = CalculateGrowthPercent(currentTotal, compareTotal),
                    SharePercent = totalCurrent == 0m ? 0m : RoundCurrency((currentTotal / totalCurrent) * 100m)
                };
            })
            .OrderByDescending(static item => item.TotalBilling)
            .ThenBy(static item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private IReadOnlyList<BillingRetentionItemDto> BuildRetentionSummaries(
        IReadOnlyList<BillingRecordRow> currentPayments,
        IReadOnlyList<BillingRecordRow> comparePayments)
    {
        return new[]
        {
            BuildRetentionSummary("reteica", "ReteICA", SumCurrency(currentPayments, static row => row.ReteIcaValue), SumCurrency(comparePayments, static row => row.ReteIcaValue)),
            BuildRetentionSummary("rteiva", "ReteIVA", SumCurrency(currentPayments, static row => row.RteIvaValue), SumCurrency(comparePayments, static row => row.RteIvaValue)),
            BuildRetentionSummary("rtefte", "ReteFuente", SumCurrency(currentPayments, static row => row.RteFteValue), SumCurrency(comparePayments, static row => row.RteFteValue))
        };
    }

    private BillingRetentionItemDto BuildRetentionSummary(string key, string label, decimal current, decimal previous)
    {
        return new BillingRetentionItemDto
        {
            Key = key,
            Label = label,
            Total = current,
            PreviousTotal = previous,
            GrowthPercent = CalculateGrowthPercent(current, previous)
        };
    }

    private IReadOnlyList<BillingUnpaidInvoiceDto> BuildUnpaidInvoices(
        IReadOnlyList<BillingRecordRow> currentEmission,
        DateOnly today)
    {
        return currentEmission
            .Where(record => record.IsOverdue(today))
            .Select(record => new BillingUnpaidInvoiceDto
            {
                InvoiceNumber = record.InvoiceNumber,
                ClientName = record.ClientName,
                VerticalLabel = record.VerticalLabel,
                ContractTypeLabel = record.ContractTypeLabel,
                DueDateDisplay = record.DueDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
                TotalInvoice = record.TotalInvoice,
                AgeDays = record.GetOverdueDays(today)
            })
            .OrderByDescending(static record => record.AgeDays)
            .ThenByDescending(static record => record.TotalInvoice)
            .ToList();
    }

    private IReadOnlyList<BillingDifferenceInvoiceDto> BuildDifferenceInvoices(IReadOnlyList<BillingRecordRow> currentEmission)
    {
        return currentEmission
            .Where(static record => record.HasPayment && Math.Abs(record.DifferenceValue) >= 0.01m)
            .Select(record => new BillingDifferenceInvoiceDto
            {
                InvoiceNumber = record.InvoiceNumber,
                ClientName = record.ClientName,
                VerticalLabel = record.VerticalLabel,
                PaymentDateDisplay = record.PaymentDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
                TotalInvoice = record.TotalInvoice,
                PaymentValue = record.PaymentValue,
                RetentionsTotal = record.RetentionsTotal,
                Difference = record.DifferenceValue,
                IsBalanced = Math.Abs(record.DifferenceValue) < 0.01m
            })
            .OrderByDescending(item => Math.Abs(item.Difference))
            .ThenBy(static item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static BillingPeriodDefinition BuildBillingPeriodDefinition(
        int year,
        BillingPeriodKind periodKind,
        int? periodValue,
        DateOnly today)
    {
        var resolvedYear = year is < 2000 or > 2100 ? today.Year : year;
        var compareYear = resolvedYear - 1;

        return periodKind switch
        {
            BillingPeriodKind.Quarter => BuildQuarterPeriod(resolvedYear, compareYear, periodValue ?? (resolvedYear == today.Year ? ((today.Month - 1) / 3) + 1 : 1)),
            BillingPeriodKind.Semester => BuildSemesterPeriod(resolvedYear, compareYear, periodValue ?? (resolvedYear == today.Year ? (today.Month <= 6 ? 1 : 2) : 1)),
            BillingPeriodKind.Year => BuildYearPeriod(resolvedYear, compareYear),
            _ => BuildMonthPeriod(resolvedYear, compareYear, periodValue ?? (resolvedYear == today.Year ? today.Month : 1))
        };
    }

    private static BillingPeriodDefinition BuildMonthPeriod(int year, int compareYear, int month)
    {
        var resolvedMonth = Math.Clamp(month, 1, 12);
        var currentStart = new DateOnly(year, resolvedMonth, 1);
        var currentEnd = currentStart.AddMonths(1);
        var compareStart = new DateOnly(compareYear, resolvedMonth, 1);
        var compareEnd = compareStart.AddMonths(1);
        var totalDays = Math.Max(DateTime.DaysInMonth(year, resolvedMonth), DateTime.DaysInMonth(compareYear, resolvedMonth));
        var categories = Enumerable.Range(1, totalDays)
            .Select(day => new BillingCategory(day.ToString("00", CultureInfo.InvariantCulture), day.ToString("00", CultureInfo.InvariantCulture)))
            .ToList();

        return new BillingPeriodDefinition
        {
            Year = year,
            CompareYear = compareYear,
            PeriodKind = BillingPeriodKind.Month,
            PeriodValue = resolvedMonth,
            PeriodLabel = ToTitleCase(currentStart.ToString("MMMM", DashboardCulture)),
            DateRangeLabel = BuildDateRangeLabel(currentStart, currentEnd),
            CompareLabel = $"Vs {ToTitleCase(compareStart.ToString("MMMM", DashboardCulture))} {compareYear}",
            CurrentStartInclusive = currentStart,
            CurrentEndExclusive = currentEnd,
            CompareStartInclusive = compareStart,
            CompareEndExclusive = compareEnd,
            TrendGranularity = BillingTrendGranularity.Day,
            GranularityLabel = "Diaria",
            Categories = categories
        };
    }

    private static BillingPeriodDefinition BuildQuarterPeriod(int year, int compareYear, int quarter)
    {
        var resolvedQuarter = Math.Clamp(quarter, 1, 4);
        var startMonth = ((resolvedQuarter - 1) * 3) + 1;
        var currentStart = new DateOnly(year, startMonth, 1);
        var currentEnd = currentStart.AddMonths(3);
        var compareStart = new DateOnly(compareYear, startMonth, 1);
        var compareEnd = compareStart.AddMonths(3);

        return new BillingPeriodDefinition
        {
            Year = year,
            CompareYear = compareYear,
            PeriodKind = BillingPeriodKind.Quarter,
            PeriodValue = resolvedQuarter,
            PeriodLabel = $"T{resolvedQuarter}",
            DateRangeLabel = BuildDateRangeLabel(currentStart, currentEnd),
            CompareLabel = $"Vs T{resolvedQuarter} {compareYear}",
            CurrentStartInclusive = currentStart,
            CurrentEndExclusive = currentEnd,
            CompareStartInclusive = compareStart,
            CompareEndExclusive = compareEnd,
            TrendGranularity = BillingTrendGranularity.Month,
            GranularityLabel = "Mensual",
            Categories = BuildMonthCategories(currentStart, 3)
        };
    }

    private static BillingPeriodDefinition BuildSemesterPeriod(int year, int compareYear, int semester)
    {
        var resolvedSemester = Math.Clamp(semester, 1, 2);
        var startMonth = resolvedSemester == 1 ? 1 : 7;
        var currentStart = new DateOnly(year, startMonth, 1);
        var currentEnd = currentStart.AddMonths(6);
        var compareStart = new DateOnly(compareYear, startMonth, 1);
        var compareEnd = compareStart.AddMonths(6);

        return new BillingPeriodDefinition
        {
            Year = year,
            CompareYear = compareYear,
            PeriodKind = BillingPeriodKind.Semester,
            PeriodValue = resolvedSemester,
            PeriodLabel = $"S{resolvedSemester}",
            DateRangeLabel = BuildDateRangeLabel(currentStart, currentEnd),
            CompareLabel = $"Vs S{resolvedSemester} {compareYear}",
            CurrentStartInclusive = currentStart,
            CurrentEndExclusive = currentEnd,
            CompareStartInclusive = compareStart,
            CompareEndExclusive = compareEnd,
            TrendGranularity = BillingTrendGranularity.Month,
            GranularityLabel = "Mensual",
            Categories = BuildMonthCategories(currentStart, 6)
        };
    }

    private static BillingPeriodDefinition BuildYearPeriod(int year, int compareYear)
    {
        var currentStart = new DateOnly(year, 1, 1);
        var currentEnd = currentStart.AddYears(1);
        var compareStart = new DateOnly(compareYear, 1, 1);
        var compareEnd = compareStart.AddYears(1);

        return new BillingPeriodDefinition
        {
            Year = year,
            CompareYear = compareYear,
            PeriodKind = BillingPeriodKind.Year,
            PeriodValue = 1,
            PeriodLabel = year.ToString(CultureInfo.InvariantCulture),
            DateRangeLabel = BuildDateRangeLabel(currentStart, currentEnd),
            CompareLabel = $"Vs {compareYear}",
            CurrentStartInclusive = currentStart,
            CurrentEndExclusive = currentEnd,
            CompareStartInclusive = compareStart,
            CompareEndExclusive = compareEnd,
            TrendGranularity = BillingTrendGranularity.Month,
            GranularityLabel = "Mensual",
            Categories = BuildMonthCategories(currentStart, 12)
        };
    }

    private static IReadOnlyList<BillingCategory> BuildMonthCategories(DateOnly startInclusive, int monthCount)
    {
        return Enumerable.Range(0, monthCount)
            .Select(offset =>
            {
                var date = startInclusive.AddMonths(offset);
                return new BillingCategory(
                    (offset + 1).ToString(CultureInfo.InvariantCulture),
                    ToTitleCase(date.ToString("MMM", DashboardCulture)));
            })
            .ToList();
    }

    private static string BuildDateRangeLabel(DateOnly startInclusive, DateOnly endExclusive)
    {
        var endInclusive = endExclusive.AddDays(-1);
        return $"{startInclusive.ToString("dd MMM yyyy", DashboardCulture)} - {endInclusive.ToString("dd MMM yyyy", DashboardCulture)}";
    }

    private static string ToTitleCase(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : DashboardCulture.TextInfo.ToTitleCase(value.Trim().ToLower(DashboardCulture));
    }

    private static string BuildBillingDateFilter(string fieldName, string fieldKind, DateOnly startInclusive, DateOnly endExclusive)
    {
        if (string.Equals(fieldKind, "date-time", StringComparison.OrdinalIgnoreCase))
        {
            var startDateTime = new DateTimeOffset(startInclusive.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var endDateTime = new DateTimeOffset(endExclusive.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            return $"{fieldName} ge {startDateTime:yyyy-MM-ddTHH:mm:ssZ} and {fieldName} lt {endDateTime:yyyy-MM-ddTHH:mm:ssZ}";
        }

        return $"{fieldName} ge {startInclusive:yyyy-MM-dd} and {fieldName} lt {endExclusive:yyyy-MM-dd}";
    }

    private static string ResolveDashboardVerticalLabel(int optionValue)
    {
        return optionValue switch
        {
            DashboardVerticalCloudOption => "Cloud",
            DashboardVerticalCopiersOption => "Copiers",
            _ => "Sin vertical"
        };
    }

    private static string ResolveDashboardContractTypeLabel(int optionValue)
    {
        return optionValue switch
        {
            DashboardContractTypeMonthlyOption => "Mensual",
            DashboardContractTypeOneTimeOption => "OneTime",
            _ => "Sin contrato"
        };
    }

    private static string NormalizeBillingGroupKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "empty";

        return value.Trim().ToLowerInvariant();
    }

    private string ReadDashboardClientName(JsonElement item)
    {
        var configuredLookupProperty = BuildDashboardLookupValuePropertyName(_dashboardBillingClientField);
        var lookupProperty = DetectLookupValueProperty(
            item,
            new[]
            {
                configuredLookupProperty,
                "_cr07a_clientenit_value",
                "_cr07a_clientenitid_value",
                "_cr07a_cliente_value",
                "_cr07a_clienteid_value",
                "_cr07a_clientelookup_value"
            },
            "cliente");

        var scannedClientValue = item.EnumerateObject()
            .Where(property =>
                property.Value.ValueKind == JsonValueKind.String
                && (property.Name.Contains("cliente", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Contains("client", StringComparison.OrdinalIgnoreCase))
                && !property.Name.EndsWith("_value", StringComparison.OrdinalIgnoreCase)
                && !property.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase))
            .Select(property => property.Value.GetString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return FirstNonEmpty(
            ReadLookupFormattedValue(item, lookupProperty),
            ReadLookupFormattedValue(item, configuredLookupProperty),
            ReadString(item, $"{_dashboardBillingClientField}{FormattedValueAnnotationSuffix}"),
            ReadString(item, $"{_dashboardBillingClientField}_name"),
            ReadString(item, _dashboardBillingClientField),
            scannedClientValue,
            "Cliente sin nombre");
    }

    private static string BuildDashboardLookupValuePropertyName(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            return "";

        var trimmed = fieldName.Trim();
        return trimmed.StartsWith("_", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith("_value", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"_{trimmed}_value";
    }

    private static decimal SumCurrency(IEnumerable<BillingRecordRow> rows, Func<BillingRecordRow, decimal> selector) =>
        RoundCurrency(rows.Sum(selector));

    private static decimal? CalculateGrowthPercent(decimal current, decimal previous)
    {
        if (previous == 0m)
            return current == 0m ? 0m : null;

        return RoundCurrency(((current - previous) / Math.Abs(previous)) * 100m);
    }

    private static string ResolveTrendTone(decimal current, decimal previous, bool lowerIsBetter)
    {
        if (current == previous)
            return "neutral";

        var improved = lowerIsBetter
            ? current < previous
            : current > previous;

        return improved ? "positive" : "negative";
    }

    private static decimal CalculateAverageDaysToPay(IEnumerable<BillingRecordRow> rows)
    {
        var paidRows = rows
            .Where(static row => row.EmissionDate is not null && row.PaymentDate is not null && row.PaymentDate.Value >= row.EmissionDate.Value)
            .ToList();

        if (paidRows.Count == 0)
            return 0m;

        var totalDays = paidRows.Sum(row => row.PaymentDate!.Value.DayNumber - row.EmissionDate!.Value.DayNumber);
        return RoundCurrency(totalDays / (decimal)paidRows.Count);
    }

    private static decimal CalculatePaymentCoverage(IReadOnlyList<BillingRecordRow> rows)
    {
        if (rows.Count == 0)
            return 0m;

        return RoundCurrency((rows.Count(static row => row.HasPayment) / (decimal)rows.Count) * 100m);
    }

    private static string FormatPercentValue(decimal value) =>
        $"{RoundCurrency(value).ToString("N2", DashboardCulture)}%";

    private static string FormatCurrencyValue(decimal value) =>
        RoundCurrency(value).ToString("C0", DashboardCulture);

    private static string GetBillingCategoryKey(DateOnly date, DateOnly periodStart, BillingTrendGranularity granularity)
    {
        return granularity switch
        {
            BillingTrendGranularity.Month => (((date.Year - periodStart.Year) * 12) + (date.Month - periodStart.Month) + 1).ToString(CultureInfo.InvariantCulture),
            _ => date.Day.ToString("00", CultureInfo.InvariantCulture)
        };
    }

    private enum BillingTrendGranularity
    {
        Day = 0,
        Month = 1
    }

    private sealed class BillingRecordRow
    {
        public string RecordId { get; set; } = "";
        public string InvoiceNumber { get; set; } = "";
        public string CompanyTaxId { get; set; } = "";
        public string ClientName { get; set; } = "";
        public string VerticalLabel { get; set; } = "";
        public string ContractTypeLabel { get; set; } = "";
        public int VerticalOptionValue { get; set; }
        public int ContractTypeOptionValue { get; set; }
        public DateOnly? DueDate { get; set; }
        public DateOnly? EmissionDate { get; set; }
        public DateOnly? PaymentDate { get; set; }
        public decimal TotalInvoice { get; set; }
        public decimal VatValue { get; set; }
        public decimal PaymentValue { get; set; }
        public decimal ReteIcaValue { get; set; }
        public decimal RteIvaValue { get; set; }
        public decimal RteFteValue { get; set; }
        public decimal DifferenceValue { get; set; }
        public decimal RetentionsTotal => RoundCurrency(ReteIcaValue + RteIvaValue + RteFteValue);
        public bool HasPayment => PaymentDate.HasValue || PaymentValue > 0m;
        public bool IsOverdue(DateOnly today) => !HasPayment && DueDate is not null && DueDate.Value < today;
        public int GetOverdueDays(DateOnly today) => !IsOverdue(today) ? 0 : today.DayNumber - DueDate!.Value.DayNumber;
    }

    private sealed record BillingCategory(string Key, string Label);

    private sealed class BillingPeriodDefinition
    {
        public int Year { get; init; }
        public int CompareYear { get; init; }
        public BillingPeriodKind PeriodKind { get; init; }
        public int PeriodValue { get; init; }
        public string PeriodLabel { get; init; } = "";
        public string DateRangeLabel { get; init; } = "";
        public string CompareLabel { get; init; } = "";
        public DateOnly CurrentStartInclusive { get; init; }
        public DateOnly CurrentEndExclusive { get; init; }
        public DateOnly CompareStartInclusive { get; init; }
        public DateOnly CompareEndExclusive { get; init; }
        public BillingTrendGranularity TrendGranularity { get; init; }
        public string GranularityLabel { get; init; } = "";
        public IReadOnlyList<BillingCategory> Categories { get; init; } = Array.Empty<BillingCategory>();
    }
}
