using CotizadorInterno.Web.Models.Metricas;
using CotizadorInterno.Web.Services;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class MetricasPeriodDetailTests
{
    private static readonly string ProjectRoot = FindProjectRoot();

    [Fact]
    public void RangeFilterAcceptsAllWithoutChangingTheDefault()
    {
        Assert.Equal(MetricsRangeFilter.All, MetricsRangeFilterExtensions.ParseOrDefault("todo"));
        Assert.Equal("all", MetricsRangeFilter.All.ToKey());
        Assert.Equal("Todo", MetricsRangeFilter.All.ToLabel());
        Assert.Equal(MetricsRangeFilter.ThisYear, MetricsRangeFilterExtensions.ParseOrDefault("valor-desconocido"));
    }

    [Theory]
    [InlineData("month", MetricsPeriodGranularity.Month, 1)]
    [InlineData("trimestre", MetricsPeriodGranularity.Quarter, 3)]
    [InlineData("semester", MetricsPeriodGranularity.Semester, 6)]
    [InlineData("año", MetricsPeriodGranularity.Year, 12)]
    public void PeriodGranularityUsesTheExpectedNumberOfMonths(
        string input,
        MetricsPeriodGranularity expected,
        int expectedMonths)
    {
        var parsed = MetricsPeriodGranularityExtensions.ParseOrDefault(input);

        Assert.Equal(expected, parsed);
        Assert.Equal(expectedMonths, parsed.MonthsPerPeriod());
    }

    [Theory]
    [InlineData(120, 100, 20)]
    [InlineData(80, 100, -20)]
    [InlineData(100, 100, 0)]
    public void GrowthUsesTheSamePeriodFromThePreviousYear(
        decimal actual,
        decimal previousYear,
        decimal expected)
    {
        Assert.Equal(expected, DataverseService.CalculateMetricsGrowthPercent(actual, previousYear));
    }

    [Fact]
    public void GrowthDoesNotInventAPercentageWithoutAPreviousYearBase()
    {
        Assert.Null(DataverseService.CalculateMetricsGrowthPercent(25m, 0m));
    }

    [Fact]
    public void MetricsViewExposesAllPeriodsAndTheResponsiveBusinessDialog()
    {
        var view = ReadProjectFile("Views", "Metricas", "Index.cshtml");

        Assert.Contains("data-filter=\"all\"", view, StringComparison.Ordinal);
        Assert.Contains("data-period=\"month\"", view, StringComparison.Ordinal);
        Assert.Contains("data-period=\"quarter\"", view, StringComparison.Ordinal);
        Assert.Contains("data-period=\"semester\"", view, StringComparison.Ordinal);
        Assert.Contains("data-period=\"year\"", view, StringComparison.Ordinal);
        Assert.Contains("id=\"metricsDetailModal\"", view, StringComparison.Ordinal);
        Assert.Contains("Nombre de cliente", view, StringComparison.Ordinal);
        Assert.Contains("Puntaje", view, StringComparison.Ordinal);
        Assert.Contains("Valor de contrato", view, StringComparison.Ordinal);
        Assert.Contains("Fecha de inicio", view, StringComparison.Ordinal);
        Assert.Contains("Detalle", view, StringComparison.Ordinal);
    }

    [Fact]
    public void PeriodCardsRenderGrowthAndOpenTheMatchingBusinessDetails()
    {
        var script = ReadProjectFile("wwwroot", "js", "metricas.js");
        var styles = ReadProjectFile("wwwroot", "css", "metricas.css");

        Assert.Contains("period: state.period", script, StringComparison.Ordinal);
        Assert.Contains("formatStatusGrowthLabel(status)", script, StringComparison.Ordinal);
        Assert.Contains("data-metrics-detail", script, StringComparison.Ordinal);
        Assert.Contains("openDetailModal", script, StringComparison.Ordinal);
        Assert.Contains("detail.contractValue", script, StringComparison.Ordinal);
        Assert.Contains("table-layout: fixed", styles, StringComparison.Ordinal);
        Assert.Contains("width: min(1280px, calc(100vw - 32px))", styles, StringComparison.Ordinal);
        Assert.Contains("content: attr(data-label)", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendAlignsGoalsGrowthAndDetailsToTheSelectedPeriod()
    {
        var service = ReadProjectFile("Services", "DataverseService.Metricas.cs");

        Assert.Contains("requestedRange.StartInclusive.AddYears(-1)", service, StringComparison.Ordinal);
        Assert.Contains("goalValue * GetCategoryMonthCount(category)", service, StringComparison.Ordinal);
        Assert.Contains("AggregatePreviousYearValues", service, StringComparison.Ordinal);
        Assert.Contains("BuildPeriodDetailGroups(records, range, accumulate)", service, StringComparison.Ordinal);
        Assert.Contains("accumulatedRecords.AddRange(periodRecords)", service, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([ProjectRoot, .. parts]));

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CotizadorInterno.Web.csproj")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro la raiz del proyecto CotizadorInterno.Web.");
    }
}
