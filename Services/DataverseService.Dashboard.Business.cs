using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Licenciamiento;
using CotizadorInterno.Web.Models.Nomina;
using CotizadorInterno.Web.Models.Puntajes;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private static readonly string[] BusinessProjectionExcludedPayrollNames =
    {
        "german ruiz",
        "jeison romero",
        "luis carlos rivera",
        "yolanda rosero"
    };

    public async Task<BusinessDashboardDto> GetBusinessDashboardAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        var rows = await GetBusinessRecordsAsync(httpContext.User, ct);
        var clientGroups = BuildBusinessContractSummaries(rows);
        var totalAnnualValue = RoundCurrency(clientGroups.Sum(static group => group.AnnualValueUsd));
        var monthlyBilling = RoundCurrency(rows.Sum(static row => row.MonthlyBillingUsd));
        var clientsCount = clientGroups.Count;
        var productsCount = rows
            .Select(ResolveBusinessProductKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var projection = await BuildBusinessProjectionAsync(rows, today, httpContext.User, ct);

        return new BusinessDashboardDto
        {
            AsOfDateLabel = today.ToString("dd MMM yyyy", DashboardCulture),
            FocusLabel = "Productos Cloud agrupados por cliente",
            HasData = rows.Count > 0,
            RecordsCount = rows.Count,
            ClientsCount = clientsCount,
            ProductsCount = productsCount,
            TotalAnnualValueUsd = totalAnnualValue,
            MonthlyBillingUsd = monthlyBilling,
            AverageContractValueUsd = clientsCount == 0 ? 0m : RoundCurrency(totalAnnualValue / clientsCount),
            EmptyStateTitle = "No encontramos negocios cerrados.",
            EmptyStateMessage = "Cuando existan filas en cr07a_salesperformancerecords las veras aqui.",
            Kpis = BuildBusinessKpis(rows, clientGroups, totalAnnualValue, monthlyBilling, productsCount),
            Projection = projection,
            TopContracts = clientGroups.Take(10).ToList(),
            LineSummaries = BuildBusinessLineSummaries(rows, totalAnnualValue),
            TopProducts = BuildBusinessProductSummaries(rows, totalAnnualValue),
            ContractTypes = BuildBusinessContractTypeSummaries(rows, totalAnnualValue)
        };
    }

    private async Task<List<BusinessRecordRow>> GetBusinessRecordsAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var relativeUrl = $"/api/data/v9.2/{_salesPerformanceTableSetName}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(ParseBusinessRecord)
            .Where(static item => item is not null)
            .Cast<BusinessRecordRow>()
            .GroupBy(static item => item.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderByDescending(static item => item.AnnualValueUsd)
            .ThenBy(static item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private BusinessRecordRow? ParseBusinessRecord(JsonElement item)
    {
        var recordId = ReadString(item, _salesPerformanceIdField);
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var clientLookupProperty = DetectLookupValueProperty(item, SalesPerformanceClientLookupFieldCandidates, "cliente");
        var productLookupProperty = DetectLookupValueProperty(item, SalesPerformanceProductLookupFieldCandidates, "producto");
        var quantity = ReadIntFlexible(item, DefaultSalesPerformanceQuantityField);
        var unitSaleUsd = RoundCurrency(ReadDecimal(item, DefaultSalesPerformanceUnitSaleUsdField) ?? 0m);
        var billingDay = ReadIntFlexible(item, _salesPerformanceBillingDayField);
        var contractTypeValue = ReadOptionValue(item, _salesPerformanceContractTypeField);
        var clientName = FirstNonEmpty(
            ReadLookupFormattedValue(item, clientLookupProperty),
            ReadString(item, $"{_salesPerformanceClientLookupLogicalName}{FormattedValueAnnotationSuffix}"),
            ReadString(item, _salesPerformanceClientLookupLogicalName),
            "Cliente sin asignar");
        var productName = FirstNonEmpty(
            ReadLookupFormattedValue(item, productLookupProperty),
            ReadString(item, $"{_salesPerformanceProductLookupLogicalName}{FormattedValueAnnotationSuffix}"),
            ReadString(item, _salesPerformanceProductLookupLogicalName),
            ReadString(item, "cr07a_productname"),
            ReadString(item, _salesPerformancePrimaryNameField),
            "Producto sin asignar");
        var productLineValue = ResolveBusinessProductLineValue(
            ReadOptionValue(item, _salesPerformanceProductLineField),
            productName);

        return new BusinessRecordRow
        {
            RecordId = recordId.Trim(),
            ClientId = ReadString(item, clientLookupProperty).Trim(),
            ClientName = clientName.Trim(),
            ProductId = ReadString(item, productLookupProperty).Trim(),
            ProductName = productName.Trim(),
            ProductLineValue = productLineValue,
            ProductLineLabel = ResolveBusinessProductLineLabel(item, productLineValue, productName),
            ContractTypeValue = contractTypeValue,
            ContractTypeLabel = ResolveBusinessContractTypeLabel(item, contractTypeValue),
            Quantity = quantity,
            UnitSaleUsd = unitSaleUsd,
            BillingDay = billingDay,
            RenewalDate = ReadDateOnly(item, _salesPerformanceRenewalDateField),
            MonthlyBillingUsd = RoundCurrency(quantity * unitSaleUsd),
            AnnualValueUsd = RoundCurrency(quantity * unitSaleUsd * 12m)
        };
    }

    private static IReadOnlyList<BusinessKpiDto> BuildBusinessKpis(
        IReadOnlyList<BusinessRecordRow> rows,
        IReadOnlyList<BusinessContractSummaryDto> clientGroups,
        decimal totalAnnualValue,
        decimal monthlyBilling,
        int productsCount)
    {
        var clientsCount = clientGroups.Count;
        var recordsCount = rows.Count;
        var averageContract = clientsCount == 0 ? 0m : RoundCurrency(totalAnnualValue / clientsCount);
        var quantity = rows.Sum(static row => row.Quantity);

        return new[]
        {
            new BusinessKpiDto
            {
                Key = "total-annual",
                Label = "Valor total USD",
                Hint = "Suma anual por cliente desde Productos Cloud.",
                Value = totalAnnualValue,
                ValueFormat = "usd",
                SecondaryLabel = "Promedio por contrato",
                SecondaryValue = FormatUsdValue(averageContract)
            },
            new BusinessKpiDto
            {
                Key = "monthly-billing",
                Label = "Facturacion mensual",
                Hint = "Cantidad por valor unidad USD en todas las filas.",
                Value = monthlyBilling,
                ValueFormat = "usd",
                SecondaryLabel = "Base anual",
                SecondaryValue = "x12"
            },
            new BusinessKpiDto
            {
                Key = "clients",
                Label = "Contratos cliente",
                Hint = "Clientes distintos agrupados como negocio cerrado.",
                Value = clientsCount,
                ValueFormat = "number",
                SecondaryLabel = "Filas de producto",
                SecondaryValue = recordsCount.ToString("N0", DashboardCulture)
            },
            new BusinessKpiDto
            {
                Key = "products",
                Label = "Productos vendidos",
                Hint = "Productos unicos y unidades activas.",
                Value = productsCount,
                ValueFormat = "number",
                SecondaryLabel = "Unidades",
                SecondaryValue = quantity.ToString("N0", DashboardCulture)
            }
        };
    }

    private async Task<BusinessProjectionDto> BuildBusinessProjectionAsync(
        IReadOnlyList<BusinessRecordRow> rows,
        DateOnly today,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1);
        var periodLabel = ToTitleCase(monthStart.ToString("MMMM yyyy", DashboardCulture));
        var recurringRows = rows
            .Where(static row => row.BillingDay > 0)
            .ToList();
        var recurringBillingUsd = RoundCurrency(recurringRows.Sum(static row => row.MonthlyBillingUsd));
        var recurringBillingCop = RoundCurrency(recurringBillingUsd * UtilityStandardTrm);
        var costs = await GetBusinessProjectionMonthlyCostsAsync(monthStart, ct);
        var payroll = await GetBusinessProjectionCurrentPayrollAsync(monthStart, ct);
        var projectedUtility = RoundCurrency(recurringBillingCop - costs.TotalCost);
        var projectedAfterPayroll = RoundCurrency(projectedUtility - payroll.TotalPayroll);
        var monthlyRows = await BuildBusinessProjectionMonthlyRowsAsync(today, user, ct);

        var projection = new BusinessProjectionDto
        {
            PeriodLabel = periodLabel,
            DateRangeLabel = BuildDateRangeLabel(monthStart, monthEnd),
            HistoryPeriodLabel = BuildBusinessProjectionHistoryPeriodLabel(monthlyRows),
            StandardTrm = UtilityStandardTrm,
            RecurringBillingUsd = recurringBillingUsd,
            RecurringBillingCop = recurringBillingCop,
            RecurringRecordsCount = recurringRows.Count,
            CurrentCostsCop = costs.TotalCost,
            CostRecordsCount = costs.RecordsCount,
            ProjectedMonthlyUtilityCop = projectedUtility,
            ProjectedMonthlyUtilityPercent = CalculateBusinessProjectionPercent(projectedUtility, recurringBillingCop),
            CurrentPayrollCop = payroll.TotalPayroll,
            PayrollRecordsCount = payroll.RecordsCount,
            ProjectedMonthlyUtilityAfterPayrollCop = projectedAfterPayroll,
            ProjectedMonthlyUtilityAfterPayrollPercent = CalculateBusinessProjectionPercent(projectedAfterPayroll, recurringBillingCop),
            MonthlyRows = monthlyRows
        };
        projection.Kpis = BuildBusinessProjectionKpis(projection);

        return projection;
    }

    private async Task<IReadOnlyList<BusinessProjectionMonthRowDto>> BuildBusinessProjectionMonthlyRowsAsync(
        DateOnly today,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var startDate = new DateOnly(2026, 1, 1);
        if (today < startDate)
            return Array.Empty<BusinessProjectionMonthRowDto>();

        var months = BuildUtilityMonthSequence(startDate, today);
        var monthMap = months.ToDictionary(
            static month => month,
            static month => new BusinessProjectionMonthAccumulator { Month = month });

        await ApplyBusinessProjectionBillingHistoryAsync(monthMap, startDate, today, user, ct);
        await ApplyBusinessProjectionCostHistoryAsync(monthMap, user, ct);
        await ApplyBusinessProjectionPayrollHistoryAsync(monthMap, ct);

        return months
            .OrderByDescending(static month => month)
            .Select(month =>
            {
                var item = monthMap[month];
                var utility = RoundCurrency(item.RealMonthlyBillingCop - item.CurrentCostsCop);
                var netUtility = RoundCurrency(utility - item.PayrollCop);

                return new BusinessProjectionMonthRowDto
                {
                    Key = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    MonthYearLabel = ToTitleCase(month.ToString("MMMM yyyy", DashboardCulture)),
                    RealMonthlyBillingCop = item.RealMonthlyBillingCop,
                    BillingRecordsCount = item.BillingRecordsCount,
                    CurrentCostsCop = item.CurrentCostsCop,
                    CostRecordsCount = item.CostRecordsCount,
                    ProjectedMonthlyUtilityCop = utility,
                    ProjectedMonthlyUtilityPercent = CalculateBusinessProjectionPercent(utility, item.RealMonthlyBillingCop),
                    PayrollCop = item.PayrollCop,
                    PayrollRecordsCount = item.PayrollRecordsCount,
                    ProjectedNetUtilityCop = netUtility,
                    ProjectedNetUtilityPercent = CalculateBusinessProjectionPercent(netUtility, item.RealMonthlyBillingCop)
                };
            })
            .ToList();
    }

    private async Task ApplyBusinessProjectionBillingHistoryAsync(
        Dictionary<DateOnly, BusinessProjectionMonthAccumulator> monthMap,
        DateOnly startDate,
        DateOnly today,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        try
        {
            var billingMetadata = await ResolveRhEntityMetadataAsync(
                _dashboardBillingTableLogicalName,
                _dashboardBillingTableSetName,
                _dashboardBillingIdField,
                _dashboardBillingPrimaryNameField,
                user,
                ct);
            var endExclusive = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
            var billingRows = await GetSiigoRevenueLedgerRowsAsync(
                billingMetadata,
                startDate,
                endExclusive,
                user,
                ct);

            foreach (var row in billingRows)
            {
                if (!row.EmissionDate.HasValue || !IsBusinessProjectionRealMonthlyBilling(row))
                    continue;

                var month = new DateOnly(row.EmissionDate.Value.Year, row.EmissionDate.Value.Month, 1);
                if (!monthMap.TryGetValue(month, out var accumulator))
                    continue;

                accumulator.RealMonthlyBillingCop = RoundCurrency(accumulator.RealMonthlyBillingCop + row.NetBeforeVatValue);
                if (!row.IsCreditNoteLedgerEntry)
                    accumulator.BillingRecordsCount++;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "No fue posible calcular facturacion real mensual Cloud Monthly para la proyeccion de negocios.");
        }
    }

    private async Task ApplyBusinessProjectionCostHistoryAsync(
        Dictionary<DateOnly, BusinessProjectionMonthAccumulator> monthMap,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        try
        {
            var consumptionRows = await GetUtilityConsumptionRowsAsync(user, ct);
            foreach (var row in consumptionRows)
            {
                if (!TryResolveUtilityConsumptionMonth(row, out var month)
                    || !monthMap.TryGetValue(month, out var accumulator)
                    || ClassifyUtilityContract(row.ContractTypeValue, row.ContractTypeLabel, "consumption") != UtilityBucket.Monthly)
                {
                    continue;
                }

                accumulator.CurrentCostsCop = RoundCurrency(accumulator.CurrentCostsCop + ResolveUtilityConsumptionCost(row));
                accumulator.CostRecordsCount++;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "No fue posible calcular costos historicos Monthly de consumo Intcomex para la proyeccion de negocios.");
        }
    }

    private async Task ApplyBusinessProjectionPayrollHistoryAsync(
        Dictionary<DateOnly, BusinessProjectionMonthAccumulator> monthMap,
        CancellationToken ct)
    {
        try
        {
            var years = monthMap.Keys
                .Select(static month => month.Year)
                .Distinct()
                .OrderBy(static year => year)
                .ToList();

            foreach (var year in years)
            {
                var history = await GetNominaPaymentHistoryAsync(year, ct);
                foreach (var row in history.Records ?? Array.Empty<NominaPaymentRecordDto>())
                {
                    if (IsBusinessProjectionExcludedPayrollRecord(row)
                        || !TryParseBusinessProjectionPayrollDate(row.PaymentDateValue, out var paymentDate))
                    {
                        continue;
                    }

                    var month = new DateOnly(paymentDate.Year, paymentDate.Month, 1);
                    if (!monthMap.TryGetValue(month, out var accumulator))
                        continue;

                    accumulator.PayrollCop = RoundCurrency(accumulator.PayrollCop + row.TotalPaid);
                    accumulator.PayrollRecordsCount++;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "No fue posible calcular nomina historica para la proyeccion de negocios.");
        }
    }

    private async Task<(decimal TotalCost, int RecordsCount)> GetBusinessProjectionMonthlyCostsAsync(
        DateOnly monthStart,
        CancellationToken ct)
    {
        try
        {
            var monthKey = monthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var cruce = await GetLicenciamientoCruceDashboardAsync(monthStart.Year, monthStart.Month, "month", ct);
            var rows = (cruce.Rows ?? Array.Empty<LicenciamientoCruceRowDto>())
                .Where(row => string.Equals(row.TipoContratoKey, LicenciamientoCruceMonthlyKey, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(row.MesCierre, monthKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return (RoundCurrency(rows.Sum(static row => row.CostoLicenciamiento)), rows.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "No fue posible calcular costos actuales de negocios desde consumo Intcomex.");
            return (0m, 0);
        }
    }

    private async Task<(decimal TotalPayroll, int RecordsCount)> GetBusinessProjectionCurrentPayrollAsync(
        DateOnly monthStart,
        CancellationToken ct)
    {
        try
        {
            var history = await GetNominaPaymentHistoryAsync(monthStart.Year, ct);
            var records = (history.Records ?? Array.Empty<NominaPaymentRecordDto>())
                .Where(row => IsBusinessProjectionPayrollRecordInMonth(row.PaymentDateValue, monthStart)
                    && !IsBusinessProjectionExcludedPayrollRecord(row))
                .ToList();

            return (RoundCurrency(records.Sum(static row => row.TotalPaid)), records.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "No fue posible calcular la ultima nomina del mes para negocios.");
            return (0m, 0);
        }
    }

    private static bool IsBusinessProjectionPayrollRecordInMonth(string? paymentDateValue, DateOnly monthStart)
    {
        if (!TryParseBusinessProjectionPayrollDate(paymentDateValue, out var paymentDate))
        {
            return false;
        }

        return paymentDate.Year == monthStart.Year && paymentDate.Month == monthStart.Month;
    }

    private static bool TryParseBusinessProjectionPayrollDate(string? paymentDateValue, out DateOnly paymentDate) =>
        DateOnly.TryParseExact(
            paymentDateValue ?? "",
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out paymentDate);

    private static bool IsBusinessProjectionExcludedPayrollRecord(NominaPaymentRecordDto row)
    {
        var employeeName = NormalizeUtilityText(row.EmployeeName);
        var recordName = NormalizeUtilityText(row.RecordName);

        return BusinessProjectionExcludedPayrollNames.Any(excluded =>
            (!string.IsNullOrWhiteSpace(employeeName)
                && employeeName.Contains(excluded, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(recordName)
                && recordName.Contains(excluded, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsBusinessProjectionRealMonthlyBilling(BillingRecordRow row)
    {
        var isCloud = row.VerticalOptionValue == DashboardVerticalCloudOption || IsUtilityCloudLabel(row.VerticalLabel);
        var isMonthly = ClassifyUtilityContract(row.ContractTypeOptionValue, row.ContractTypeLabel, "billing") == UtilityBucket.Monthly;
        return isCloud && isMonthly;
    }

    private static string BuildBusinessProjectionHistoryPeriodLabel(IReadOnlyList<BusinessProjectionMonthRowDto> monthlyRows)
    {
        if (monthlyRows.Count == 0)
            return "Desde 2026";

        var ordered = monthlyRows
            .OrderBy(static row => row.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return $"{ordered.First().MonthYearLabel} - {ordered.Last().MonthYearLabel}";
    }

    private static IReadOnlyList<BusinessKpiDto> BuildBusinessProjectionKpis(BusinessProjectionDto projection)
    {
        return new[]
        {
            new BusinessKpiDto
            {
                Key = "recurring-monthly-billing",
                Label = "Facturacion mensual recurrente",
                Hint = $"Filas con dia de facturacion. TRM {FormatBusinessNumber(projection.StandardTrm)} para COP.",
                Value = projection.RecurringBillingUsd,
                ValueFormat = "usd",
                SecondaryLabel = "Estimado",
                SecondaryValue = FormatCopValue(projection.RecurringBillingCop)
            },
            new BusinessKpiDto
            {
                Key = "current-costs",
                Label = "Costos actuales",
                Hint = $"Consumo Intcomex Monthly de {projection.PeriodLabel}.",
                Value = projection.CurrentCostsCop,
                ValueFormat = "currency",
                SecondaryLabel = "Cruces",
                SecondaryValue = projection.CostRecordsCount.ToString("N0", DashboardCulture)
            },
            new BusinessKpiDto
            {
                Key = "projected-monthly-utility",
                Label = "Utilidad mensual proyectada",
                Hint = "Facturacion recurrente estimada menos costos actuales.",
                Value = projection.ProjectedMonthlyUtilityCop,
                ValueFormat = "currency",
                SecondaryLabel = "Margen",
                SecondaryValue = FormatBusinessPercent(projection.ProjectedMonthlyUtilityPercent)
            },
            new BusinessKpiDto
            {
                Key = "current-payroll",
                Label = "Ultima nomina",
                Hint = $"Pagos de nomina de {projection.PeriodLabel}.",
                Value = projection.CurrentPayrollCop,
                ValueFormat = "currency",
                SecondaryLabel = "Registros",
                SecondaryValue = projection.PayrollRecordsCount.ToString("N0", DashboardCulture)
            },
            new BusinessKpiDto
            {
                Key = "projected-net-utility",
                Label = "Utilidad mensual neta proyectada",
                Hint = "Utilidad mensual proyectada menos ultima nomina.",
                Value = projection.ProjectedMonthlyUtilityAfterPayrollCop,
                ValueFormat = "currency",
                SecondaryLabel = "Margen neto",
                SecondaryValue = FormatBusinessPercent(projection.ProjectedMonthlyUtilityAfterPayrollPercent)
            }
        };
    }

    private static IReadOnlyList<BusinessContractSummaryDto> BuildBusinessContractSummaries(IReadOnlyList<BusinessRecordRow> rows)
    {
        var groups = rows
            .GroupBy(ResolveBusinessClientKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToList();
                var first = items.First();
                var annualValue = RoundCurrency(items.Sum(static row => row.AnnualValueUsd));
                var monthlyBilling = RoundCurrency(items.Sum(static row => row.MonthlyBillingUsd));
                var topProduct = items
                    .GroupBy(ResolveBusinessProductKey, StringComparer.OrdinalIgnoreCase)
                    .Select(productGroup => new
                    {
                        Name = productGroup.First().ProductName,
                        Value = productGroup.Sum(static row => row.AnnualValueUsd)
                    })
                    .OrderByDescending(product => product.Value)
                    .ThenBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                return new BusinessContractSummaryDto
                {
                    Key = group.Key,
                    ClientId = first.ClientId,
                    ClientName = first.ClientName,
                    AnnualValueUsd = annualValue,
                    MonthlyBillingUsd = monthlyBilling,
                    RecordsCount = items.Count,
                    ProductsCount = items
                        .Select(ResolveBusinessProductKey)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),
                    TopProductName = topProduct?.Name ?? "",
                    SharePercent = 0m
                };
            })
            .OrderByDescending(static item => item.AnnualValueUsd)
            .ThenBy(static item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalAnnualValue = RoundCurrency(groups.Sum(static group => group.AnnualValueUsd));
        foreach (var group in groups)
        {
            group.SharePercent = CalculateBusinessShare(group.AnnualValueUsd, totalAnnualValue);
        }

        return groups;
    }

    private static IReadOnlyList<BusinessLineSummaryDto> BuildBusinessLineSummaries(
        IReadOnlyList<BusinessRecordRow> rows,
        decimal totalAnnualValue)
    {
        return rows
            .GroupBy(ResolveBusinessLineKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToList();
                var first = items.First();
                var annualValue = RoundCurrency(items.Sum(static row => row.AnnualValueUsd));

                return new BusinessLineSummaryDto
                {
                    Key = group.Key,
                    Label = first.ProductLineLabel,
                    AnnualValueUsd = annualValue,
                    MonthlyBillingUsd = RoundCurrency(items.Sum(static row => row.MonthlyBillingUsd)),
                    RecordsCount = items.Count,
                    ClientsCount = items
                        .Select(ResolveBusinessClientKey)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),
                    Quantity = items.Sum(static row => row.Quantity),
                    SharePercent = CalculateBusinessShare(annualValue, totalAnnualValue)
                };
            })
            .OrderByDescending(static item => item.AnnualValueUsd)
            .ThenBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<BusinessProductSummaryDto> BuildBusinessProductSummaries(
        IReadOnlyList<BusinessRecordRow> rows,
        decimal totalAnnualValue)
    {
        return rows
            .GroupBy(ResolveBusinessProductKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToList();
                var first = items.First();
                var annualValue = RoundCurrency(items.Sum(static row => row.AnnualValueUsd));

                return new BusinessProductSummaryDto
                {
                    Key = group.Key,
                    ProductId = first.ProductId,
                    ProductName = first.ProductName,
                    AnnualValueUsd = annualValue,
                    MonthlyBillingUsd = RoundCurrency(items.Sum(static row => row.MonthlyBillingUsd)),
                    Quantity = items.Sum(static row => row.Quantity),
                    ClientsCount = items
                        .Select(ResolveBusinessClientKey)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),
                    RecordsCount = items.Count,
                    SharePercent = CalculateBusinessShare(annualValue, totalAnnualValue)
                };
            })
            .OrderByDescending(static item => item.Quantity)
            .ThenByDescending(static item => item.AnnualValueUsd)
            .ThenBy(static item => item.ProductName, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    private static IReadOnlyList<BusinessContractTypeSummaryDto> BuildBusinessContractTypeSummaries(
        IReadOnlyList<BusinessRecordRow> rows,
        decimal totalAnnualValue)
    {
        return rows
            .GroupBy(ResolveBusinessContractTypeKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToList();
                var first = items.First();
                var annualValue = RoundCurrency(items.Sum(static row => row.AnnualValueUsd));

                return new BusinessContractTypeSummaryDto
                {
                    Key = group.Key,
                    Label = first.ContractTypeLabel,
                    AnnualValueUsd = annualValue,
                    MonthlyBillingUsd = RoundCurrency(items.Sum(static row => row.MonthlyBillingUsd)),
                    RecordsCount = items.Count,
                    SharePercent = CalculateBusinessShare(annualValue, totalAnnualValue)
                };
            })
            .OrderByDescending(static item => item.AnnualValueUsd)
            .ThenBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int ResolveBusinessProductLineValue(int optionValue, string productName) =>
        IsAcronisBusinessProduct(productName) ? 3 : optionValue;

    private string ResolveBusinessProductLineLabel(JsonElement item, int optionValue) =>
        ResolveBusinessProductLineLabel(item, optionValue, "");

    private string ResolveBusinessProductLineLabel(JsonElement item, int optionValue, string productName)
    {
        if (IsAcronisBusinessProduct(productName))
            return "Acronis";

        var formatted = ReadString(item, $"{_salesPerformanceProductLineField}{FormattedValueAnnotationSuffix}").Trim();
        if (!string.IsNullOrWhiteSpace(formatted))
            return formatted;

        if (!item.TryGetProperty(_salesPerformanceProductLineField, out _))
            return "Sin linea";

        return ResolveProductLineLabel(optionValue);
    }

    private static bool IsAcronisBusinessProduct(string? productName) =>
        !string.IsNullOrWhiteSpace(productName)
        && productName.Contains("Acronis", StringComparison.OrdinalIgnoreCase);

    private string ResolveBusinessContractTypeLabel(JsonElement item, int optionValue)
    {
        var formatted = ReadString(item, $"{_salesPerformanceContractTypeField}{FormattedValueAnnotationSuffix}").Trim();
        if (!string.IsNullOrWhiteSpace(formatted))
            return formatted;

        if (!item.TryGetProperty(_salesPerformanceContractTypeField, out _))
            return "Sin contrato";

        return PuntajesOptionCatalog.ContractTypeOptions
            .FirstOrDefault(option => option.Value == optionValue)?.Label
            ?? optionValue.ToString(CultureInfo.InvariantCulture);
    }

    private static string ResolveBusinessClientKey(BusinessRecordRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.ClientId))
            return $"id:{row.ClientId.Trim()}";

        return $"name:{NormalizeBillingGroupKey(row.ClientName)}";
    }

    private static string ResolveBusinessProductKey(BusinessRecordRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.ProductId))
            return $"id:{row.ProductId.Trim()}";

        return $"name:{NormalizeBillingGroupKey(row.ProductName)}";
    }

    private static string ResolveBusinessLineKey(BusinessRecordRow row) =>
        row.ProductLineValue != 0
            ? $"option:{row.ProductLineValue.ToString(CultureInfo.InvariantCulture)}"
            : $"label:{NormalizeBillingGroupKey(row.ProductLineLabel)}";

    private static string ResolveBusinessContractTypeKey(BusinessRecordRow row) =>
        row.ContractTypeValue != 0
            ? $"option:{row.ContractTypeValue.ToString(CultureInfo.InvariantCulture)}"
            : $"label:{NormalizeBillingGroupKey(row.ContractTypeLabel)}";

    private static decimal CalculateBusinessShare(decimal value, decimal total) =>
        Math.Abs(total) < 0.01m ? 0m : RoundCurrency((value / total) * 100m);

    private static string FormatUsdValue(decimal value) =>
        $"USD {RoundCurrency(value).ToString("N0", DashboardCulture)}";

    private static string FormatCopValue(decimal value) =>
        $"COP {RoundCurrency(value).ToString("N0", DashboardCulture)}";

    private static string FormatBusinessNumber(decimal value) =>
        RoundCurrency(value).ToString("N0", DashboardCulture);

    private static string FormatBusinessPercent(decimal? value) =>
        value.HasValue ? $"{RoundCurrency(value.Value).ToString("N2", DashboardCulture)}%" : "Sin margen";

    private static decimal? CalculateBusinessProjectionPercent(decimal utility, decimal sales)
    {
        if (Math.Abs(sales) < 0.01m)
            return null;

        return RoundCurrency((utility / sales) * 100m);
    }

    private sealed class BusinessRecordRow
    {
        public string RecordId { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string ClientName { get; set; } = "";
        public string ProductId { get; set; } = "";
        public string ProductName { get; set; } = "";
        public int ProductLineValue { get; set; }
        public string ProductLineLabel { get; set; } = "";
        public int ContractTypeValue { get; set; }
        public string ContractTypeLabel { get; set; } = "";
        public int Quantity { get; set; }
        public decimal UnitSaleUsd { get; set; }
        public int BillingDay { get; set; }
        public DateOnly? RenewalDate { get; set; }
        public decimal MonthlyBillingUsd { get; set; }
        public decimal AnnualValueUsd { get; set; }
    }

    private sealed class BusinessProjectionMonthAccumulator
    {
        public DateOnly Month { get; set; }
        public decimal RealMonthlyBillingCop { get; set; }
        public int BillingRecordsCount { get; set; }
        public decimal CurrentCostsCop { get; set; }
        public int CostRecordsCount { get; set; }
        public decimal PayrollCop { get; set; }
        public int PayrollRecordsCount { get; set; }
    }
}
