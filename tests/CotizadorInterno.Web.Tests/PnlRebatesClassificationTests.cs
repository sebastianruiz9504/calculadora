using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Reconciliation;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Abstractions;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class PnlRebatesClassificationTests
{
    private const int Year = 2024;

    [Theory]
    [InlineData("all")]
    [InlineData("cloud")]
    [InlineData("copiers")]
    public async Task RebatesAreIncludedOnceInRevenueAndExcludedFromCosts(string vertical)
    {
        using var fixture = new PnlFixture();

        var dashboard = await fixture.Service.GetPnlDashboardAsync(Year, 2, vertical);

        Assert.Equal(2, dashboard.MonthCutoff);
        Assert.Equal(new decimal[] { 100m, 40m }, Row(dashboard, "income-rebates").Values);
        Assert.Equal(140m, Row(dashboard, "income-rebates").Total);
        Assert.DoesNotContain(dashboard.Rows, row => row.Key == "cogs-rebates");
        Assert.Single(dashboard.Rows, row => row.Label == "Rebates");
        var keys = dashboard.Rows.Select(row => row.Key).ToList();
        Assert.True(keys.IndexOf("income-cloud") < keys.IndexOf("income-rebates"));
        Assert.True(keys.IndexOf("income-rebates") < keys.IndexOf("income-total"));
        Assert.True(keys.IndexOf("income-total") < keys.IndexOf("section-cogs"));

        var copiersOnly = vertical == "copiers";
        var income = copiersOnly ? new decimal[] { 100m, 40m } : [1_100m, 540m];
        var costs = copiersOnly ? new decimal[] { 0m, 0m } : [400m, 200m];
        var profit = copiersOnly ? new decimal[] { 100m, 40m } : [700m, 340m];
        var netIncome = copiersOnly ? new decimal[] { 125m, 45m } : [725m, 345m];
        Assert.Equal(income, Row(dashboard, "income-total").Values);
        Assert.Equal(costs, Row(dashboard, "cogs-total").Values);
        Assert.Equal(profit, Row(dashboard, "gross-profit").Values);
        Assert.Equal(profit, Row(dashboard, "ebitda").Values);
        Assert.Equal(new decimal[] { 25m, 5m }, Row(dashboard, "other-financial-income").Values);
        Assert.Equal(netIncome, Row(dashboard, "income-before-taxes").Values);
        Assert.Equal(netIncome, Row(dashboard, "net-income").Values);
        Assert.Equal(income.Sum(), Assert.Single(dashboard.Kpis, kpi => kpi.Key == "operating-revenue").Value);
        Assert.Equal(profit.Sum(), Assert.Single(dashboard.Kpis, kpi => kpi.Key == "gross-profit").Value);
        Assert.Equal(profit.Sum(), Assert.Single(dashboard.Kpis, kpi => kpi.Key == "ebitda").Value);
        Assert.Equal(new decimal[] { 100m, 100m }, Row(dashboard, "income-total").Percentages);
        Assert.Equal(copiersOnly ? 100m : 8.54m, Row(dashboard, "income-rebates").TotalPercentage);
        Assert.Equal(copiersOnly ? new decimal[] { 100m, 100m } : [9.09m, 7.41m],
            Row(dashboard, "income-rebates").Percentages);
    }

    [Theory]
    [InlineData("income-rebates")]
    [InlineData("income-total")]
    [InlineData("cogs-total")]
    [InlineData("gross-profit")]
    [InlineData("ebitda")]
    [InlineData("other-financial-income")]
    [InlineData("income-before-taxes")]
    [InlineData("net-income")]
    public async Task MonthlyAndAccumulatedDetailsReconcileIncludingRebateReversals(string rowKey)
    {
        using var fixture = new PnlFixture();
        var dashboard = await fixture.Service.GetPnlDashboardAsync(Year, 2, "all");
        var expectedMonths = ExpectedMonthlyValues(rowKey);
        var includesRebates = rowKey is not ("cogs-total" or "other-financial-income");

        foreach (var month in new int?[] { null, 1, 2 })
        {
            var detail = await fixture.Service.GetPnlCellDetailAsync(Year, 2, "all", rowKey, month);
            var expected = month.HasValue ? expectedMonths[month.Value - 1] : expectedMonths.Sum();

            Assert.Equal(rowKey, detail.RowKey);
            Assert.Equal(month, detail.CellMonth);
            Assert.Equal(expected, detail.Total);
            Assert.Equal(expected, detail.Records.Sum(record => record.CellValue));
            Assert.Equal(month.HasValue ? Row(dashboard, rowKey).Values[month.Value - 1] : Row(dashboard, rowKey).Total,
                detail.Total);
            Assert.DoesNotContain(detail.Records, record => record.RecordId.StartsWith("manual-rebate", StringComparison.Ordinal));
            Assert.DoesNotContain(detail.Records, record => record.RecordId.StartsWith("rebate-outside", StringComparison.Ordinal));

            var rebateRecords = detail.Records
                .Where(record => record.RecordId.StartsWith("rebate-", StringComparison.Ordinal))
                .ToList();
            if (!includesRebates)
            {
                Assert.Empty(rebateRecords);
                continue;
            }

            Assert.Equal(month == 1 ? 1 : month == 2 ? 2 : 3, rebateRecords.Count);
            Assert.Equal(month == 1 ? 100m : month == 2 ? 40m : 140m,
                rebateRecords.Sum(record => record.CellValue));
            if (month != 1)
            {
                Assert.Equal(-10m, Assert.Single(rebateRecords, record => record.RecordId == "rebate-reversal").CellValue);
            }
        }
    }

    [Fact]
    public async Task RebatesChangeRevenueAndProfitByTheirAmountWithoutChangingCostsOrFinancialIncome()
    {
        using var withRebates = new PnlFixture();
        using var withoutRebates = new PnlFixture(includeSharePointRebates: false);
        var current = await withRebates.Service.GetPnlDashboardAsync(Year, 2, "all");
        var baseline = await withoutRebates.Service.GetPnlDashboardAsync(Year, 2, "all");

        foreach (var rowKey in new[] { "income-total", "gross-profit", "ebitda", "income-before-taxes", "net-income" })
        {
            Assert.Equal(100m, Row(current, rowKey).Values[0] - Row(baseline, rowKey).Values[0]);
            Assert.Equal(40m, Row(current, rowKey).Values[1] - Row(baseline, rowKey).Values[1]);
            Assert.Equal(140m, Row(current, rowKey).Total - Row(baseline, rowKey).Total);
        }

        Assert.Equal(Row(baseline, "cogs-total").Values, Row(current, "cogs-total").Values);
        Assert.Equal(Row(baseline, "other-financial-income").Values, Row(current, "other-financial-income").Values);
        Assert.Equal(0m, Row(baseline, "income-rebates").Total);
        var emptyDetail = await withoutRebates.Service.GetPnlCellDetailAsync(Year, 2, "all", "income-rebates");
        Assert.Empty(emptyDetail.Records);
        Assert.Equal(0m, emptyDetail.Total);
        Assert.Contains("No encontramos rebates", emptyDetail.EmptyMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PreviousRebateRowKeyResolvesToTheSameIncomeDetail(int? month)
    {
        using var fixture = new PnlFixture();
        var legacy = await fixture.Service.GetPnlCellDetailAsync(Year, 2, "all", "cogs-rebates", month);
        var current = await fixture.Service.GetPnlCellDetailAsync(Year, 2, "all", "income-rebates", month);

        Assert.Equal("income-rebates", legacy.RowKey);
        Assert.Equal(current.Total, legacy.Total);
        Assert.Equal(current.Records.Select(record => (record.RecordId, record.CellValue)),
            legacy.Records.Select(record => (record.RecordId, record.CellValue)));
    }

    private static PnlRowDto Row(PnlDashboardDto dashboard, string key) =>
        Assert.Single(dashboard.Rows, row => row.Key == key);

    private static decimal[] ExpectedMonthlyValues(string rowKey) => rowKey switch
    {
        "income-rebates" => [100m, 40m],
        "income-total" => [1_100m, 540m],
        "cogs-total" => [400m, 200m],
        "gross-profit" or "ebitda" => [700m, 340m],
        "other-financial-income" => [25m, 5m],
        "income-before-taxes" or "net-income" => [725m, 345m],
        _ => throw new ArgumentOutOfRangeException(nameof(rowKey))
    };

    private sealed class PnlFixture : IDisposable
    {
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        public DataverseService Service { get; }

        public PnlFixture(bool includeSharePointRebates = true)
        {
            var downstream = DispatchProxy.Create<IDownstreamApi, FixtureDownstreamApi>();
            var siigo = DispatchProxy.Create<ISiigoService, FixtureSiigoService>();
            Service = new DataverseService(
                downstream,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
                null!,
                null!,
                siigo,
                _cache,
                new FixtureRebatesProvider(includeSharePointRebates),
                new ConfigurationBuilder().Build(),
                Options.Create(new RhOptions()),
                NullLogger<DataverseService>.Instance);
        }

        public void Dispose() => _cache.Dispose();
    }

    public class FixtureDownstreamApi : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IDownstreamApi.CallApiForUserAsync)
                || targetMethod.IsGenericMethod || args is null)
            {
                throw new NotSupportedException($"Unexpected downstream method: {targetMethod?.Name}");
            }

            var configure = Assert.IsType<Action<DownstreamApiOptions>>(args[1]);
            var options = new DownstreamApiOptions();
            configure(options);
            Assert.Equal("GET", options.HttpMethod);
            var path = options.RelativePath ?? "";
            var body = path.Contains("EntityDefinitions(", StringComparison.Ordinal)
                ? "{}"
                : JsonSerializer.Serialize(new { value = CollectionRows(path) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }

        private static object[] CollectionRows(string path)
        {
            if (path.StartsWith("/api/data/v9.2/cr07a_facturacions?", StringComparison.Ordinal))
            {
                return [BillingDimension(1, 1_190m), BillingDimension(2, 595m)];
            }

            if (path.StartsWith("/api/data/v9.2/cr07a_gastodelaempresas?", StringComparison.Ordinal))
            {
                return [LicensingExpense(1, 400m), LicensingExpense(2, 200m)];
            }

            if (path.StartsWith("/api/data/v9.2/cr07a_pnlmanualitems?", StringComparison.Ordinal))
            {
                // Legacy manual rebates deliberately duplicate the SharePoint amounts.
                return
                [
                    ManualItem("manual-rebate-1", 1, 645250000, 100m),
                    ManualItem("manual-rebate-2", 2, 645250000, 40m),
                    ManualItem("manual-financial-1", 1, 645250001, 25m),
                    ManualItem("manual-financial-2", 2, 645250001, 5m)
                ];
            }

            throw new NotSupportedException($"Unexpected Dataverse request: {path}");
        }

        private static object BillingDimension(int month, decimal gross) => new
        {
            cr07a_facturacionid = $"billing-{month}",
            cr07a_name = $"FV-{month}",
            cr07a_fechadeemision = $"{Year}-{month:00}-05",
            cr07a_vertical = 645250000,
            cr07a_tipocontrato = 645250000,
            cr07a_totalfactura = gross,
            cr07a_ivavalor = gross - (month == 1 ? 1_000m : 500m)
        };

        private static object LicensingExpense(int month, decimal net) => new
        {
            cr07a_gastodelaempresaid = $"licensing-{month}",
            cr07a_fechaemision = $"{Year}-{month:00}-06",
            cr07a_categoria = 645250010,
            cr07a_total = net * 1.19m,
            cr07a_iva = net * 0.19m,
            cr07a_totalantesdeiva = net,
            cr07a_cloud = net,
            cr07a_copiers = 0m
        };

        private static object ManualItem(string id, int month, int type, decimal value) => new
        {
            cr07a_pnlmanualitemid = id,
            cr07a_name = id,
            cr07a_fecha = $"{Year}-{month:00}-10",
            cr07a_tipo = type,
            cr07a_valor = value
        };
    }

    public class FixtureSiigoService : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(ISiigoService.GetBillingDocumentsAsync))
                throw new NotSupportedException($"Unexpected Siigo method: {targetMethod?.Name}");

            return Task.FromResult(new SiigoFinancialReconciliationData
            {
                Invoices =
                [
                    new SiigoReconciliationInvoice
                    {
                        Id = "siigo-1", Name = "FV-1", Date = new DateOnly(Year, 1, 5),
                        Total = 1_190m, GrossTotal = 1_190m, Vat = 190m, StampStatus = "Accepted"
                    },
                    new SiigoReconciliationInvoice
                    {
                        Id = "siigo-2", Name = "FV-2", Date = new DateOnly(Year, 2, 5),
                        Total = 595m, GrossTotal = 595m, Vat = 95m, StampStatus = "Accepted"
                    }
                ]
            });
        }
    }

    private sealed class FixtureRebatesProvider(bool includeRecords) : ISharePointRebatesProvider
    {
        public Task<SharePointRebatesSnapshot> GetSnapshotAsync(CancellationToken ct = default) =>
            Task.FromResult(new SharePointRebatesSnapshot(
                includeRecords
                    ? new SharePointRebateRecord[]
                    {
                        new("rebate-january", new DateOnly(Year, 1, 10), 100m, 2),
                        new("rebate-february", new DateOnly(Year, 2, 10), 50m, 3),
                        new("rebate-reversal", new DateOnly(Year, 2, 11), -10m, 4),
                        new("rebate-outside-cutoff", new DateOnly(Year, 3, 10), 900m, 5),
                        new("rebate-outside-year", new DateOnly(Year - 1, 12, 10), 700m, 6)
                    }
                    : Array.Empty<SharePointRebateRecord>(),
                "fixture", null, false, ""));
    }
}
