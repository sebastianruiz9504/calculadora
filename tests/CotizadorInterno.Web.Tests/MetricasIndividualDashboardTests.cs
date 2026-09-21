using CotizadorInterno.Web.Models.Metricas;
using CotizadorInterno.Web.Models.Puntajes;
using CotizadorInterno.Web.Services;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class MetricasIndividualDashboardTests
{
    private static readonly int Year = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "SA Pacific Standard Time").Year;

    private static ScoreRecordDto Record(string seller, int month, decimal score, int first = 1, int vertical = 645250000, int? year = null) => new()
    {
        RecordId = Guid.NewGuid().ToString(), ClientId = "same-client", ClientName = "Cliente prueba",
        SalesPerson = seller, ContractStartDateValue = $"{year ?? Year}-{month:00}-01", Score = score,
        AnnualValue = score * 100, FirstContractOptionValue = first, VerticalOptionValue = vertical, ContractOptionValue = 645250000
    };

    private static MetricsDashboardDto Dashboard(IReadOnlyList<ScoreRecordDto> records, string? seller = null,
        MetricsRangeFilter filter = MetricsRangeFilter.ThisYear, MetricsPeriodGranularity period = MetricsPeriodGranularity.Month,
        MetricsViewMode view = MetricsViewMode.Individual) =>
        DataverseService.BuildMetricsDashboardFromRecords(filter, view, period, seller, records);

    [Fact]
    public void DefaultShowsAllSixChartsWithDistinctSellerColorsAndOneIndividualGoal()
    {
        var dashboard = Dashboard([Record("Ana", 1, 10), Record("Luis", 2, 30, vertical: 645250001)]);
        Assert.False(dashboard.RequiresSellerSelection);
        Assert.Equal("Todos los vendedores", dashboard.AppliedSellerName);
        Assert.Equal(2, dashboard.RecordsCount);
        Assert.Equal(40m, dashboard.TotalScore);
        Assert.Equal(6, dashboard.Charts.Count);
        foreach (var chart in dashboard.Charts)
        {
            var actual = chart.Series.Where(series => !series.IsReference).ToList();
            Assert.Equal(new[] { "Ana", "Luis" }, actual.Select(series => series.Name));
            Assert.Equal(2, actual.Select(series => series.Color).Distinct().Count());
            Assert.Single(chart.Series, series => series.IsReference);
            Assert.Equal(24, chart.GoalStatuses.Count);
        }
        Assert.Equal(125m, dashboard.Charts[0].Series.Single(series => series.IsReference).Values[0]);
    }

    [Fact]
    public void SelectingSellerOnlyFiltersTheirSeriesTotalsGoalsAndDetailsWithoutChangingColor()
    {
        var records = new[] { Record("Ana", 1, 10), Record("Luis", 2, 30, vertical: 645250001), Record("Luis", 2, 5, year: Year - 1) };
        var all = Dashboard(records);
        var selected = Dashboard(records, " LUIS ");
        Assert.Equal("luis", selected.AppliedSellerKey);
        Assert.Equal(30m, selected.TotalScore);
        Assert.Equal(1, selected.NewClientsCount);
        for (var index = 0; index < 6; index++)
        {
            var expected = all.Charts[index].Series.Single(series => series.Name == "Luis");
            var actual = selected.Charts[index].Series.Single(series => !series.IsReference);
            Assert.Equal(expected.Color, actual.Color);
            Assert.Equal(expected.Values, actual.Values);
            Assert.Equal(expected.AnnualValues, actual.AnnualValues);
            Assert.Equal(12, selected.Charts[index].GoalStatuses.Count);
            Assert.All(selected.Charts[index].GoalStatuses, status => Assert.Equal("Luis", status.SellerName));
        }
        var february = selected.Charts[0].GoalStatuses[1];
        Assert.Equal(5m, february.PreviousYearValue);
        Assert.Equal(500m, february.GrowthPercent);
        Assert.Equal(records[1].RecordId, Assert.Single(february.Details).RecordId);
    }

    [Fact]
    public void NewClientsCountsEveryYesRowIncludingRepeatedClientsButNotNoUnknownOrOutsideRange()
    {
        var records = new[] { Record("Ana", 1, 10), Record("Ana", 2, 20), Record("Ana", 3, 5, first: 2),
            Record("Ana", 4, 5, first: 0), Record("Luis", 1, 30), Record("Ana", 1, 5, year: Year - 1),
            Record("Ana", 1, 5, year: Year + 1) };
        Assert.Equal(3, Dashboard(records).NewClientsCount);
        Assert.Equal(2, Dashboard(records, "ana").NewClientsCount);
        Assert.Equal(1, Dashboard(records, "luis").NewClientsCount);
        Assert.Equal(1, Dashboard(records, filter: MetricsRangeFilter.PreviousYear).NewClientsCount);
        Assert.Equal(5, Dashboard(records, filter: MetricsRangeFilter.All).NewClientsCount);
    }

    [Fact]
    public void NewClientDetailsMatchTheCountAndContainNamesAndDatesNewestFirst()
    {
        var january = Record("Ana", 1, 10);
        january.ClientName = "Cliente repetido";
        var march = Record("Ana", 3, 20);
        march.ClientName = january.ClientName;
        var february = Record("Luis", 2, 30);
        february.ClientName = "Cliente Luis";
        var records = new[] { january, march, february, Record("Ana", 4, 10, first: 2),
            Record("Ana", 5, 10, first: 0), Record("Ana", 6, 10, year: Year - 1) };

        var all = Dashboard(records);
        Assert.Equal(all.NewClientsCount, all.NewClients.Count);
        Assert.Equal(new[] { march.RecordId, february.RecordId, january.RecordId }, all.NewClients.Select(client => client.RecordId));
        Assert.Equal(new[] { "Cliente repetido", "Cliente Luis", "Cliente repetido" }, all.NewClients.Select(client => client.ClientName));
        Assert.Equal($"{Year}-03-01", all.NewClients[0].ContractStartDateValue);
        Assert.Equal($"01/03/{Year}", all.NewClients[0].ContractStartDateDisplay);

        var ana = Dashboard(records, "ana");
        Assert.Equal(ana.NewClientsCount, ana.NewClients.Count);
        Assert.Equal(new[] { march.RecordId, january.RecordId }, ana.NewClients.Select(client => client.RecordId));
        Assert.Equal(records[5].RecordId, Assert.Single(Dashboard(records, "ana", MetricsRangeFilter.PreviousYear).NewClients).RecordId);
    }

    [Fact]
    public void NewClientDetailsAreEmptyWhenTheSelectedSellerHasOnlyNoRecords()
    {
        var dashboard = Dashboard([Record("Ana", 1, 10), Record("Luis", 2, 30, first: 2)], "luis");
        Assert.Equal("luis", dashboard.AppliedSellerKey);
        Assert.Equal(0, dashboard.NewClientsCount);
        Assert.Empty(dashboard.NewClients);
    }

    [Fact]
    public void ExplicitFirstContractFlagIsCountedIndependentlyFromTheExistingScoreRenewalExclusion()
    {
        var renewal = Record("Ana", 1, 999);
        renewal.ContractOptionValue = 645250001;
        var dashboard = Dashboard([renewal, Record("Luis", 1, 20)]);
        Assert.Equal(2, dashboard.NewClientsCount);
        Assert.Equal(20m, dashboard.TotalScore);
        Assert.Equal(1, Dashboard([renewal], "ana").NewClientsCount);
    }

    [Theory]
    [InlineData(MetricsPeriodGranularity.Month, 12, 125)]
    [InlineData(MetricsPeriodGranularity.Quarter, 4, 375)]
    [InlineData(MetricsPeriodGranularity.Semester, 2, 750)]
    [InlineData(MetricsPeriodGranularity.Year, 1, 1500)]
    public void EveryGroupingKeepsCountsAndScalesTheIndividualGoal(MetricsPeriodGranularity period, int categories, decimal goal)
    {
        var dashboard = Dashboard([Record("Ana", 1, 10), Record("Luis", 2, 20)], period: period);
        Assert.Equal(2, dashboard.NewClientsCount);
        Assert.Equal(categories, dashboard.Charts[0].Categories.Count);
        Assert.Equal(goal, dashboard.Charts[0].Series.Single(series => series.IsReference).Values[0]);
        Assert.Equal(30m, dashboard.Charts[0].Series.Where(series => !series.IsReference).Sum(series => series.Values.Sum()));
    }

    [Fact]
    public void ThisMonthCountsOnlyTheCurrentMonthAndForcesMonthlyGrouping()
    {
        var now = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "SA Pacific Standard Time");
        var records = new[] { Record("Ana", now.Month, 10), Record("Ana", now.Month == 1 ? 2 : 1, 20), Record("Luis", now.Month, 30, first: 2) };
        var dashboard = Dashboard(records, filter: MetricsRangeFilter.ThisMonth, period: MetricsPeriodGranularity.Year);
        Assert.Equal(1, dashboard.NewClientsCount);
        Assert.Equal(records[0].RecordId, Assert.Single(dashboard.NewClients).RecordId);
        Assert.Equal("month", dashboard.Period);
        Assert.Single(dashboard.Charts[0].Categories);
        Assert.Equal(40m, dashboard.TotalScore);
    }

    [Fact]
    public void AccumulatedSeriesAndDetailRestartAtEachYearForEachSeller()
    {
        var lastYear = Record("Ana", 12, 10, year: Year - 1);
        var thisYear = Record("Ana", 1, 20);
        var dashboard = Dashboard([lastYear, thisYear, Record("Luis", 1, 30)], filter: MetricsRangeFilter.All);
        var chart = dashboard.Charts[1];
        var ana = chart.Series.Single(series => series.Name == "Ana");
        Assert.Equal(10m, ana.Values[11]);
        Assert.Equal(20m, ana.Values[12]);
        Assert.Equal(125m, chart.Series.Single(series => series.IsReference).Values[12]);
        var january = chart.GoalStatuses.Single(status => status.SellerName == "Ana" && status.CategoryKey == $"{Year}-01-01");
        Assert.Equal(thisYear.RecordId, Assert.Single(january.Details).RecordId);
    }

    [Fact]
    public void EmptyRangeAndAnUnavailableSellerDoNotRequireSelection()
    {
        var empty = Dashboard([]);
        Assert.False(empty.RequiresSellerSelection);
        Assert.Equal(0, empty.NewClientsCount);
        Assert.Empty(empty.Charts);
        var fallback = Dashboard([Record("Ana", 1, 10)], "unavailable");
        Assert.Equal("", fallback.AppliedSellerKey);
        Assert.Equal(6, fallback.Charts.Count);
        Assert.Equal(1, fallback.NewClientsCount);
    }

    [Fact]
    public void GlobalChartsAndGoalsRemainUnfiltered()
    {
        var dashboard = Dashboard([Record("Ana", 1, 10), Record("Luis", 1, 30)], "ana", view: MetricsViewMode.Global);
        Assert.Equal(5, dashboard.Charts.Count);
        Assert.Equal(40m, dashboard.TotalScore);
        Assert.Equal(2, dashboard.SellersCount);
        Assert.Equal(180m, dashboard.Charts[1].Series.Single(series => series.IsReference).Values[0]);
        Assert.All(dashboard.Charts.SelectMany(chart => chart.GoalStatuses), status => Assert.Empty(status.SellerName));
    }
}
