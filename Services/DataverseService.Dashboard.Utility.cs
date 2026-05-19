using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Licenciamiento;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const int UtilityStartYear = 2025;
    private const decimal UtilityStandardTrm = 3750m;

    private enum UtilityBucket
    {
        Unknown,
        Monthly,
        Prepaid
    }

    public async Task<UtilityDashboardDto> GetUtilityDashboardAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        var startDate = new DateOnly(UtilityStartYear, 1, 1);
        var endExclusive = today.AddDays(1);
        var months = BuildUtilityMonthSequence(startDate, today);
        var unresolvedRows = new List<UtilityUnresolvedRowDto>();

        var user = httpContext.User;
        var billingMetadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            user,
            ct);

        var productRows = await GetBusinessRecordsAsync(user, ct);
        var priceMap = await LoadUtilityProductPriceMapAsync(user, ct);
        var billingRows = await GetBillingRecordsAsync(
            billingMetadata,
            startDate,
            endExclusive,
            _dashboardBillingEmissionDateField,
            _dashboardBillingEmissionDateFieldKind,
            user,
            ct);
        var consumptionRows = await GetUtilityConsumptionRowsAsync(user, ct);

        var theoreticalLines = BuildUtilityTheoreticalLines(productRows, priceMap, unresolvedRows);
        var theoreticalMonthly = BuildUtilityTheoreticalCard(
            "monthly",
            "Utilidad teorica Monthly",
            theoreticalLines.Where(static line => line.Bucket == UtilityBucket.Monthly));
        var theoreticalPrepaid = BuildUtilityTheoreticalCard(
            "prepaid",
            "Utilidad teorica Prepaid",
            theoreticalLines.Where(static line => line.Bucket == UtilityBucket.Prepaid));

        var realSegments = BuildUtilityRealSegments(months, billingRows, consumptionRows, startDate, today, unresolvedRows);

        var recordsCount = productRows.Count(static row => row.BillingDay > 0)
            + realSegments.Monthly.BillingRecordsCount
            + realSegments.Monthly.CostRecordsCount
            + realSegments.Prepaid.BillingRecordsCount
            + realSegments.Prepaid.CostRecordsCount;

        return new UtilityDashboardDto
        {
            StartYear = UtilityStartYear,
            EndYear = today.Year,
            EndMonth = today.Month,
            PeriodLabel = BuildUtilityPeriodLabel(startDate, today),
            DateRangeLabel = BuildDateRangeLabel(startDate, endExclusive),
            FocusLabel = "Cloud Monthly y Prepaid desde Productos Cloud, Facturacion y Consumo Intcomex",
            HasData = recordsCount > 0,
            RecordsCount = recordsCount,
            StandardTrm = UtilityStandardTrm,
            TheoreticalMonthly = theoreticalMonthly,
            TheoreticalPrepaid = theoreticalPrepaid,
            RealMonthly = realSegments.Monthly,
            RealPrepaid = realSegments.Prepaid,
            UnresolvedRows = unresolvedRows
                .OrderBy(static row => row.SourceLabel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.Reason, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.Reference, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            EmptyStateMessage = "No hay datos de utilidad para Cloud desde enero de 2025 hasta la fecha."
        };
    }

    public async Task<UtilityAssignmentResultDto> AssignUtilityRowAsync(
        UtilityAssignmentRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var sourceType = NormalizeUtilitySourceType(request.SourceType);
        var targetBucket = NormalizeUtilityTargetBucket(request.TargetBucket);
        var recordId = NormalizeOptionalGuid(request.RecordId);
        if (string.IsNullOrWhiteSpace(recordId))
            throw new InvalidOperationException("Selecciona una fila valida para asignar.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var user = httpContext.User;
        switch (sourceType)
        {
            case "billing":
                await AssignUtilityBillingRowAsync(recordId, targetBucket, user, ct);
                break;
            case "consumption":
                await AssignUtilityConsumptionRowAsync(recordId, targetBucket, user, ct);
                break;
            case "sales-performance":
                await AssignUtilitySalesPerformanceRowAsync(recordId, targetBucket, user, ct);
                break;
            default:
                throw new InvalidOperationException("La fuente seleccionada no permite asignacion automatica.");
        }

        return new UtilityAssignmentResultDto
        {
            SourceType = sourceType,
            RecordId = recordId,
            TargetBucket = targetBucket,
            Message = targetBucket == "monthly"
                ? "Fila asignada a Monthly."
                : "Fila asignada a Prepaid."
        };
    }

    private async Task AssignUtilityBillingRowAsync(
        string recordId,
        string targetBucket,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            user,
            ct);

        var payload = new Dictionary<string, object?>
        {
            [_dashboardBillingVerticalField] = DashboardVerticalCloudOption,
            [_dashboardBillingContractTypeField] = targetBucket == "monthly"
                ? DashboardContractTypeMonthlyOption
                : DashboardContractTypeOneTimeOption
        };

        await CallDataverseSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({recordId})",
            "PATCH",
            payload,
            user,
            ct);
    }

    private async Task AssignUtilityConsumptionRowAsync(
        string recordId,
        string targetBucket,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var metadata = await ResolveLicensingMetadataAsync(user, ct);
        var contractType = targetBucket == "monthly"
            ? LicensingContractMonthly
            : LicensingContractPrepaid;
        var payload = new Dictionary<string, object?>
        {
            [LicensingContractTypeField] = ConvertLicensingPayloadValue(metadata, LicensingContractTypeField, contractType)
        };

        await CallDataverseSendAsync(
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({recordId})",
            "PATCH",
            payload,
            user,
            ct);
    }

    private async Task AssignUtilitySalesPerformanceRowAsync(
        string recordId,
        string targetBucket,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            [_salesPerformanceContractTypeField] = targetBucket == "monthly" ? 0 : 1
        };

        await CallDataverseSendAsync(
            $"/api/data/v9.2/{_salesPerformanceTableSetName}({recordId})",
            "PATCH",
            payload,
            user,
            ct);
    }

    private List<UtilityTheoreticalLine> BuildUtilityTheoreticalLines(
        IReadOnlyList<BusinessRecordRow> productRows,
        UtilityProductPriceMap priceMap,
        List<UtilityUnresolvedRowDto> unresolvedRows)
    {
        var lines = new List<UtilityTheoreticalLine>();
        foreach (var row in productRows.Where(static item => item.BillingDay > 0))
        {
            var bucket = ClassifyUtilityContract(row.ContractTypeValue, row.ContractTypeLabel, "sales-performance");
            var sale = RoundCurrency(row.Quantity * row.UnitSaleUsd * UtilityStandardTrm);
            var hasCost = priceMap.TryGetCost(row, out var unitCostUsd);
            var cost = hasCost
                ? RoundCurrency(row.Quantity * unitCostUsd * UtilityStandardTrm)
                : 0m;

            if (bucket == UtilityBucket.Unknown)
            {
                unresolvedRows.Add(new UtilityUnresolvedRowDto
                {
                    SourceType = "sales-performance",
                    SourceLabel = "Productos Cloud",
                    RecordId = row.RecordId,
                    Reference = row.ProductName,
                    ClientName = row.ClientName,
                    ProductName = row.ProductName,
                    CurrentContractType = FirstNonEmpty(row.ContractTypeLabel, "Sin contrato"),
                    Reason = "Producto Cloud con dia de facturacion, pero sin tipo Monthly/Prepaid claro.",
                    Amount = sale,
                    CanAssign = true
                });
                continue;
            }

            if (!hasCost)
            {
                unresolvedRows.Add(new UtilityUnresolvedRowDto
                {
                    SourceType = "price",
                    SourceLabel = "Precios Cloud",
                    RecordId = row.RecordId,
                    Reference = row.ProductName,
                    ClientName = row.ClientName,
                    ProductName = row.ProductName,
                    CurrentContractType = row.ContractTypeLabel,
                    Reason = "Producto vendido sin costo unitario encontrado en Precios Cloud.",
                    SuggestedBucket = bucket == UtilityBucket.Monthly ? "monthly" : "prepaid",
                    Amount = sale,
                    CanAssign = false
                });
            }

            lines.Add(new UtilityTheoreticalLine(row.RecordId, bucket, sale, cost, hasCost));
        }

        return lines;
    }

    private static UtilityTheoreticalCardDto BuildUtilityTheoreticalCard(
        string key,
        string label,
        IEnumerable<UtilityTheoreticalLine> sourceLines)
    {
        var lines = sourceLines.ToList();
        var sales = RoundCurrency(lines.Sum(static line => line.Sales));
        var cost = RoundCurrency(lines.Sum(static line => line.Cost));
        var utility = RoundCurrency(sales - cost);

        return new UtilityTheoreticalCardDto
        {
            Key = key,
            Label = label,
            Sales = sales,
            Cost = cost,
            Utility = utility,
            UtilityPercent = CalculateUtilityPercent(utility, sales),
            RecordsCount = lines.Count,
            MissingCostCount = lines.Count(static line => !line.HasCost)
        };
    }

    private UtilityRealSegments BuildUtilityRealSegments(
        IReadOnlyList<DateOnly> months,
        IReadOnlyList<BillingRecordRow> billingRows,
        IReadOnlyList<LicenciamientoRecordDto> consumptionRows,
        DateOnly startDate,
        DateOnly today,
        List<UtilityUnresolvedRowDto> unresolvedRows)
    {
        var monthly = CreateUtilityMonthAccumulator(months);
        var prepaid = CreateUtilityMonthAccumulator(months);
        var startMonth = new DateOnly(startDate.Year, startDate.Month, 1);
        var endMonth = new DateOnly(today.Year, today.Month, 1);

        foreach (var row in billingRows)
        {
            var bucket = ClassifyUtilityContract(row.ContractTypeOptionValue, row.ContractTypeLabel, "billing");
            var hasCloudVertical = row.VerticalOptionValue == DashboardVerticalCloudOption
                || IsUtilityCloudLabel(row.VerticalLabel);
            var missingVertical = IsUtilityMissingLabel(row.VerticalLabel, "Sin vertical")
                || row.VerticalOptionValue == 0;

            if (!hasCloudVertical)
            {
                if (missingVertical)
                {
                    unresolvedRows.Add(BuildUtilityBillingUnresolvedRow(
                        row,
                        "Factura sin vertical para validar si corresponde a Cloud.",
                        bucket,
                        canAssign: true));
                }

                continue;
            }

            if (!row.EmissionDate.HasValue)
            {
                unresolvedRows.Add(BuildUtilityBillingUnresolvedRow(
                    row,
                    "Factura Cloud sin fecha de emision para ubicar el mes.",
                    bucket,
                    canAssign: false));
                continue;
            }

            if (bucket == UtilityBucket.Unknown)
            {
                unresolvedRows.Add(BuildUtilityBillingUnresolvedRow(
                    row,
                    "Factura Cloud sin tipo de contrato Monthly/Prepaid claro.",
                    bucket,
                    canAssign: true));
                continue;
            }

            var month = new DateOnly(row.EmissionDate.Value.Year, row.EmissionDate.Value.Month, 1);
            if (!monthly.ContainsKey(month))
                continue;

            var target = bucket == UtilityBucket.Monthly ? monthly[month] : prepaid[month];
            target.Sales = RoundCurrency(target.Sales + row.TotalInvoice);
            target.BillingRecordsCount++;
        }

        foreach (var row in consumptionRows)
        {
            if (!TryResolveUtilityConsumptionMonth(row, out var month))
            {
                unresolvedRows.Add(BuildUtilityConsumptionUnresolvedRow(
                    row,
                    "Consumo Intcomex sin mes/factura para ubicarlo en el eje mensual.",
                    UtilityBucket.Unknown,
                    canAssign: false));
                continue;
            }

            if (month < startMonth || month > endMonth)
                continue;

            var bucket = ClassifyUtilityContract(row.ContractTypeValue, row.ContractTypeLabel, "consumption");
            if (bucket == UtilityBucket.Unknown)
            {
                unresolvedRows.Add(BuildUtilityConsumptionUnresolvedRow(
                    row,
                    "Consumo Intcomex sin tipo de contrato Monthly/Prepaid claro.",
                    bucket,
                    canAssign: true));
                continue;
            }

            if (!monthly.ContainsKey(month))
                continue;

            var target = bucket == UtilityBucket.Monthly ? monthly[month] : prepaid[month];
            target.Cost = RoundCurrency(target.Cost + ResolveUtilityConsumptionCost(row));
            target.CostRecordsCount++;
        }

        return new UtilityRealSegments(
            BuildUtilityRealSegment("monthly", "Utilidad real Monthly", months, monthly),
            BuildUtilityRealSegment("prepaid", "Utilidad real Prepaid", months, prepaid));
    }

    private static Dictionary<DateOnly, UtilityMonthAccumulator> CreateUtilityMonthAccumulator(
        IReadOnlyList<DateOnly> months)
    {
        return months.ToDictionary(
            static month => month,
            static month => new UtilityMonthAccumulator { Month = month });
    }

    private static UtilityRealSegmentDto BuildUtilityRealSegment(
        string key,
        string label,
        IReadOnlyList<DateOnly> months,
        IReadOnlyDictionary<DateOnly, UtilityMonthAccumulator> accumulators)
    {
        var points = months
            .Select(month =>
            {
                var item = accumulators[month];
                var utility = RoundCurrency(item.Sales - item.Cost);
                return new UtilityMonthlyPointDto
                {
                    Key = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    Label = FormatUtilityMonthLabel(month),
                    Year = month.Year,
                    Month = month.Month,
                    Sales = item.Sales,
                    Cost = item.Cost,
                    Utility = utility,
                    UtilityPercent = CalculateUtilityPercent(utility, item.Sales),
                    BillingRecordsCount = item.BillingRecordsCount,
                    CostRecordsCount = item.CostRecordsCount
                };
            })
            .ToList();

        var sales = RoundCurrency(points.Sum(static point => point.Sales));
        var cost = RoundCurrency(points.Sum(static point => point.Cost));
        var totalUtility = RoundCurrency(sales - cost);

        return new UtilityRealSegmentDto
        {
            Key = key,
            Label = label,
            Sales = sales,
            Cost = cost,
            Utility = totalUtility,
            UtilityPercent = CalculateUtilityPercent(totalUtility, sales),
            BillingRecordsCount = points.Sum(static point => point.BillingRecordsCount),
            CostRecordsCount = points.Sum(static point => point.CostRecordsCount),
            Months = points
        };
    }

    private async Task<IReadOnlyList<LicenciamientoRecordDto>> GetUtilityConsumptionRowsAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var metadata = await ResolveLicensingMetadataAsync(user, ct);
        var select = BuildLicensingSelectClause(metadata);
        var orderBy = Uri.EscapeDataString($"{LicensingInvoiceDateField} asc,{LicensingModifiedOnField} desc");
        var relativeUrl = $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}?$select={select}&$orderby={orderBy}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => BuildLicensingRecordDto(metadata, item))
            .Where(static item => item is not null)
            .Select(static item => item!)
            .GroupBy(static item => item.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
    }

    private async Task<UtilityProductPriceMap> LoadUtilityProductPriceMapAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var relativeUrl = $"/api/data/v9.2/{ProductsEntitySetName}?$select={ProductsIdField},{ProductsDescriptionField},{ProductsPurchasePriceField}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        var prices = items
            .Select(static item => new UtilityProductPriceRow
            {
                ProductId = NormalizeOptionalGuid(ReadString(item, ProductsIdField)),
                ProductName = ReadString(item, ProductsDescriptionField).Trim(),
                PurchasePriceUsd = RoundCurrency(ReadDecimal(item, ProductsPurchasePriceField) ?? 0m)
            })
            .Where(static item => !string.IsNullOrWhiteSpace(item.ProductId) || !string.IsNullOrWhiteSpace(item.ProductName))
            .ToList();

        return new UtilityProductPriceMap(prices);
    }

    private static UtilityUnresolvedRowDto BuildUtilityBillingUnresolvedRow(
        BillingRecordRow row,
        string reason,
        UtilityBucket bucket,
        bool canAssign)
    {
        return new UtilityUnresolvedRowDto
        {
            SourceType = "billing",
            SourceLabel = "Facturacion",
            RecordId = row.RecordId,
            Reference = row.InvoiceNumber,
            ClientName = row.ClientName,
            DateDisplay = row.EmissionDate?.ToString("dd MMM yyyy", DashboardCulture) ?? "Sin fecha",
            CurrentVertical = FirstNonEmpty(row.VerticalLabel, "Sin vertical"),
            CurrentContractType = FirstNonEmpty(row.ContractTypeLabel, "Sin contrato"),
            Reason = reason,
            SuggestedBucket = bucket == UtilityBucket.Monthly
                ? "monthly"
                : bucket == UtilityBucket.Prepaid ? "prepaid" : "",
            Amount = row.TotalInvoice,
            CanAssign = canAssign
        };
    }

    private static UtilityUnresolvedRowDto BuildUtilityConsumptionUnresolvedRow(
        LicenciamientoRecordDto row,
        string reason,
        UtilityBucket bucket,
        bool canAssign)
    {
        return new UtilityUnresolvedRowDto
        {
            SourceType = "consumption",
            SourceLabel = "Consumo Intcomex",
            RecordId = row.RecordId,
            Reference = FirstNonEmpty(row.FacturaDisplay, row.FacturaValue, row.BillingInterval, row.RecordId),
            ClientName = row.NombreCliente,
            ProductName = row.ProductDisplay,
            DateDisplay = FirstNonEmpty(row.FacturaDisplay, row.BillingInterval, "Sin fecha"),
            CurrentContractType = FirstNonEmpty(row.ContractTypeLabel, "Sin tipo"),
            Reason = reason,
            SuggestedBucket = bucket == UtilityBucket.Monthly
                ? "monthly"
                : bucket == UtilityBucket.Prepaid ? "prepaid" : "",
            Amount = ResolveUtilityConsumptionCost(row),
            CanAssign = canAssign
        };
    }

    private static decimal ResolveUtilityConsumptionCost(LicenciamientoRecordDto row)
    {
        if (Math.Abs(row.PesosTotal) >= 0.01m)
            return RoundCurrency(row.PesosTotal);

        var usd = Math.Abs(row.ValorTotalUsd) >= 0.01m
            ? row.ValorTotalUsd
            : RoundCurrency(row.UnidadUsd * row.Cantidad);
        var trm = row.Trm > 0m ? row.Trm : UtilityStandardTrm;
        return RoundCurrency(usd * trm);
    }

    private static bool TryResolveUtilityConsumptionMonth(LicenciamientoRecordDto row, out DateOnly month)
    {
        if (TryParseUtilityMonth(row.FacturaValue, out month)
            || TryParseUtilityMonth(row.FacturaDisplay, out month)
            || TryParseUtilityMonth(row.BillingInterval, out month))
        {
            month = new DateOnly(month.Year, month.Month, 1);
            return true;
        }

        month = default;
        return false;
    }

    private static bool TryParseUtilityMonth(string? raw, out DateOnly month)
    {
        month = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (TryParseDateOnly(raw, out var date))
        {
            month = new DateOnly(date.Year, date.Month, 1);
            return true;
        }

        if (DateOnly.TryParse(raw, DashboardCulture, DateTimeStyles.AllowWhiteSpaces, out date))
        {
            month = new DateOnly(date.Year, date.Month, 1);
            return true;
        }

        if (DateTime.TryParse(raw, DashboardCulture, DateTimeStyles.AllowWhiteSpaces, out var dashboardDate))
        {
            month = new DateOnly(dashboardDate.Year, dashboardDate.Month, 1);
            return true;
        }

        var trimmed = raw.Trim();
        var yearMonth = Regex.Match(trimmed, @"(?<year>20\d{2})[\s_\-/]+(?<month>0?[1-9]|1[0-2])");
        if (yearMonth.Success
            && int.TryParse(yearMonth.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedYear)
            && int.TryParse(yearMonth.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMonth))
        {
            month = new DateOnly(parsedYear, parsedMonth, 1);
            return true;
        }

        var monthYear = Regex.Match(trimmed, @"(?<month>0?[1-9]|1[0-2])[\s_\-/]+(?<year>20\d{2})");
        if (monthYear.Success
            && int.TryParse(monthYear.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedYear)
            && int.TryParse(monthYear.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedMonth))
        {
            month = new DateOnly(parsedYear, parsedMonth, 1);
            return true;
        }

        var normalized = NormalizeUtilityText(trimmed);
        for (var index = 1; index <= 12; index++)
        {
            var full = NormalizeUtilityText(DashboardCulture.DateTimeFormat.GetMonthName(index));
            var abbreviated = NormalizeUtilityText(DashboardCulture.DateTimeFormat.GetAbbreviatedMonthName(index).TrimEnd('.'));
            if (!normalized.Contains(full, StringComparison.OrdinalIgnoreCase)
                && !normalized.Contains(abbreviated, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var yearMatch = Regex.Match(trimmed, @"20\d{2}");
            if (!yearMatch.Success || !int.TryParse(yearMatch.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedYear))
                continue;

            month = new DateOnly(parsedYear, index, 1);
            return true;
        }

        return false;
    }

    private static IReadOnlyList<DateOnly> BuildUtilityMonthSequence(DateOnly startDate, DateOnly endDate)
    {
        var months = new List<DateOnly>();
        var cursor = new DateOnly(startDate.Year, startDate.Month, 1);
        var endMonth = new DateOnly(endDate.Year, endDate.Month, 1);
        while (cursor <= endMonth)
        {
            months.Add(cursor);
            cursor = cursor.AddMonths(1);
        }

        return months;
    }

    private static string BuildUtilityPeriodLabel(DateOnly startDate, DateOnly endDate) =>
        $"{FormatUtilityMonthLabel(new DateOnly(startDate.Year, startDate.Month, 1), includeYear: true)} - {FormatUtilityMonthLabel(new DateOnly(endDate.Year, endDate.Month, 1), includeYear: true)}";

    private static string FormatUtilityMonthLabel(DateOnly month, bool includeYear = true)
    {
        var label = DashboardCulture.DateTimeFormat.GetAbbreviatedMonthName(month.Month).TrimEnd('.');
        return includeYear
            ? $"{ToTitleCase(label)} {month.Year}"
            : ToTitleCase(label);
    }

    private static decimal? CalculateUtilityPercent(decimal utility, decimal sales) =>
        Math.Abs(sales) < 0.01m ? null : RoundCurrency((utility / sales) * 100m);

    private static UtilityBucket ClassifyUtilityContract(int optionValue, string? label, string source)
    {
        var normalized = NormalizeUtilityText(label);
        if (normalized.Contains("monthly", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("mensual", StringComparison.OrdinalIgnoreCase))
        {
            return UtilityBucket.Monthly;
        }

        if (normalized.Contains("prepaid", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("pre paid", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("onetime", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("one time", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("annual", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("anual", StringComparison.OrdinalIgnoreCase))
        {
            return UtilityBucket.Prepaid;
        }

        if (optionValue == DashboardContractTypeMonthlyOption || optionValue == LicensingContractMonthly)
            return UtilityBucket.Monthly;

        if (optionValue == DashboardContractTypeOneTimeOption
            || optionValue == LicensingContractOnetime
            || optionValue == LicensingContractPrepaid)
        {
            return UtilityBucket.Prepaid;
        }

        if (string.Equals(source, "sales-performance", StringComparison.OrdinalIgnoreCase)
            && optionValue == 1)
        {
            return UtilityBucket.Prepaid;
        }

        return UtilityBucket.Unknown;
    }

    private static bool IsUtilityCloudLabel(string? label) =>
        NormalizeUtilityText(label).Contains("cloud", StringComparison.OrdinalIgnoreCase);

    private static bool IsUtilityMissingLabel(string? label, string missingText)
    {
        var normalized = NormalizeUtilityText(label);
        return string.IsNullOrWhiteSpace(normalized)
            || normalized == NormalizeUtilityText(missingText)
            || normalized.StartsWith("sin ", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeUtilitySourceType(string? sourceType)
    {
        var normalized = NormalizeUtilityText(sourceType);
        return normalized switch
        {
            "billing" or "facturacion" => "billing",
            "consumption" or "consumo" or "consumo intcomex" => "consumption",
            "sales performance" or "sales-performance" or "productos cloud" => "sales-performance",
            _ => normalized
        };
    }

    private static string NormalizeUtilityTargetBucket(string? targetBucket)
    {
        var normalized = NormalizeUtilityText(targetBucket);
        return normalized switch
        {
            "monthly" or "mensual" => "monthly",
            "prepaid" or "pre paid" or "onetime" or "one time" or "annual" or "anual" => "prepaid",
            _ => throw new InvalidOperationException("Selecciona si la fila pertenece a Monthly o Prepaid.")
        };
    }

    private static string NormalizeUtilityText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }

        return Regex.Replace(builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant(), @"\s+", " ").Trim();
    }

    private sealed record UtilityTheoreticalLine(
        string RecordId,
        UtilityBucket Bucket,
        decimal Sales,
        decimal Cost,
        bool HasCost);

    private sealed record UtilityRealSegments(UtilityRealSegmentDto Monthly, UtilityRealSegmentDto Prepaid);

    private sealed class UtilityMonthAccumulator
    {
        public DateOnly Month { get; set; }
        public decimal Sales { get; set; }
        public decimal Cost { get; set; }
        public int BillingRecordsCount { get; set; }
        public int CostRecordsCount { get; set; }
    }

    private sealed class UtilityProductPriceRow
    {
        public string ProductId { get; set; } = "";
        public string ProductName { get; set; } = "";
        public decimal PurchasePriceUsd { get; set; }
    }

    private sealed class UtilityProductPriceMap
    {
        private readonly Dictionary<string, decimal> _byId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, decimal> _byName = new(StringComparer.OrdinalIgnoreCase);

        public UtilityProductPriceMap(IEnumerable<UtilityProductPriceRow> rows)
        {
            foreach (var row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.ProductId) && !_byId.ContainsKey(row.ProductId))
                    _byId[row.ProductId] = row.PurchasePriceUsd;

                var nameKey = NormalizeUtilityText(row.ProductName);
                if (!string.IsNullOrWhiteSpace(nameKey) && !_byName.ContainsKey(nameKey))
                    _byName[nameKey] = row.PurchasePriceUsd;
            }
        }

        public bool TryGetCost(BusinessRecordRow row, out decimal purchasePriceUsd)
        {
            var productId = NormalizeOptionalGuid(row.ProductId);
            if (!string.IsNullOrWhiteSpace(productId) && _byId.TryGetValue(productId, out purchasePriceUsd))
                return true;

            var productName = NormalizeUtilityText(row.ProductName);
            if (!string.IsNullOrWhiteSpace(productName) && _byName.TryGetValue(productName, out purchasePriceUsd))
                return true;

            purchasePriceUsd = 0m;
            return false;
        }
    }
}
