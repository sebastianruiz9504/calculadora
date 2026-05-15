using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Puntajes;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
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
        var productLineValue = ReadOptionValue(item, _salesPerformanceProductLineField);
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
            ReadString(item, _salesPerformancePrimaryNameField),
            "Producto sin asignar");

        return new BusinessRecordRow
        {
            RecordId = recordId.Trim(),
            ClientId = ReadString(item, clientLookupProperty).Trim(),
            ClientName = clientName.Trim(),
            ProductId = ReadString(item, productLookupProperty).Trim(),
            ProductName = productName.Trim(),
            ProductLineValue = productLineValue,
            ProductLineLabel = ResolveBusinessProductLineLabel(item, productLineValue),
            ContractTypeValue = contractTypeValue,
            ContractTypeLabel = ResolveBusinessContractTypeLabel(item, contractTypeValue),
            Quantity = quantity,
            UnitSaleUsd = unitSaleUsd,
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

    private string ResolveBusinessProductLineLabel(JsonElement item, int optionValue)
    {
        var formatted = ReadString(item, $"{_salesPerformanceProductLineField}{FormattedValueAnnotationSuffix}").Trim();
        if (!string.IsNullOrWhiteSpace(formatted))
            return formatted;

        if (!item.TryGetProperty(_salesPerformanceProductLineField, out _))
            return "Sin linea";

        return ResolveProductLineLabel(optionValue);
    }

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
        public DateOnly? RenewalDate { get; set; }
        public decimal MonthlyBillingUsd { get; set; }
        public decimal AnnualValueUsd { get; set; }
    }
}
