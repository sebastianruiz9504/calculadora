using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.SoporteCloud;
using CotizadorInterno.Web.Services;
using System.Runtime.CompilerServices;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class DashboardTodaySummaryTests
{
    private static readonly string ProjectRoot = FindProjectRoot();

    [Fact]
    public void Build_UsesSameDayCutoffAndReusesEveryRequestedBreakdown()
    {
        var today = new DateOnly(2026, 8, 17);
        var portfolio = new PortfolioDashboardDto
        {
            Invoices = new[]
            {
                Invoice("current-cloud", "2026-08-05", "Cloud", 645250000, 100m, pending: true, overdue: true),
                Invoice("current-copiers", "2026-08-17", "Copiers", 645250001, 200m),
                Invoice("future-current", "2026-08-18", "Cloud", 645250000, 900m),
                Invoice("previous-cloud", "2026-07-01", "Cloud", 645250000, 80m),
                Invoice("previous-copiers", "2026-07-17", "Copiers", 645250001, 120m, pending: true),
                Invoice("late-previous", "2026-07-18", "Cloud", 645250000, 700m)
            }
        };
        var ytd = Ytd(
            2026,
            ExpensePoint("2026-07",
                Expense("2026-07-17", "cloud", "Cloud", 200m),
                Expense("2026-07-18", "copiers", "Copiers", 100m)),
            ExpensePoint("2026-08",
                Expense("2026-08-10", "cloud", "Cloud", 300m),
                Expense("2026-08-18", "copiers", "Copiers", 50m)));
        var currentSupport = Support(("ana", "Ana", 2), ("luis", "Luis", 1));
        var previousSupport = Support(("ana", "Ana", 1));
        var equipment = new CopiersEquipmentDashboardDto
        {
            MaintenanceRows = new[]
            {
                Maintenance("2026-08-03", "ana", "Ana"),
                Maintenance("2026-08-17", "ana", "Ana"),
                Maintenance("2026-07-31", "luis", "Luis")
            }
        };

        var result = DashboardTodaySummaryBuilder.Build(
            today,
            portfolio,
            ytd,
            null,
            currentSupport,
            previousSupport,
            equipment);

        Assert.Equal("1-17 de agosto 2026", result.CurrentPeriodLabel);
        Assert.Equal("1-17 de julio 2026", result.ComparisonPeriodLabel);
        Assert.Equal(7, result.Cards.Count);

        var billing = Card(result, "billing");
        Assert.Equal(300m, billing.Value);
        Assert.Equal(200m, billing.PreviousValue);
        Assert.Equal(50m, billing.GrowthPercent);
        Assert.Equal(100m, Item(billing, "cloud").Value);
        Assert.Equal(80m, Item(billing, "cloud").PreviousValue);

        var invoices = Card(result, "invoice-count");
        Assert.Equal(2m, invoices.Value);
        Assert.Equal(2m, invoices.PreviousValue);
        Assert.Equal(0m, invoices.GrowthPercent);

        var expenses = Card(result, "expenses");
        Assert.Equal(300m, expenses.Value);
        Assert.Equal(200m, expenses.PreviousValue);
        Assert.Equal(50m, expenses.GrowthPercent);
        Assert.DoesNotContain(expenses.Items, item => item.Key == "copiers");

        var support = Card(result, "support-cloud");
        Assert.Equal(3m, support.Value);
        Assert.Equal(1m, support.PreviousValue);
        Assert.Equal(200m, support.GrowthPercent);
        Assert.Null(Item(support, "id:luis").GrowthPercent);

        var maintenance = Card(result, "copiers-maintenance");
        Assert.Equal(2m, maintenance.Value);
        Assert.Equal(2m, Item(maintenance, "id:ana").Value);
        Assert.False(maintenance.ShowsGrowth);

        var currentPortfolio = Card(result, "portfolio");
        Assert.Equal(220m, currentPortfolio.Value);
        Assert.Equal(100m, Card(result, "overdue-portfolio").Value);
        Assert.Equal("detail", currentPortfolio.DestinationSubtab);
    }

    [Fact]
    public void Build_InJanuaryReadsDecemberFromPreviousYearAndCapsShortMonths()
    {
        var january = DashboardTodaySummaryBuilder.Build(
            new DateOnly(2027, 1, 31),
            new PortfolioDashboardDto(),
            Ytd(2027, ExpensePoint("2027-01", Expense("2027-01-31", "cloud", "Cloud", 150m))),
            Ytd(2026, ExpensePoint("2026-12", Expense("2026-12-31", "cloud", "Cloud", 100m))),
            Support(),
            Support(),
            new CopiersEquipmentDashboardDto());

        Assert.Equal(50m, Card(january, "expenses").GrowthPercent);
        Assert.Equal("1-31 de diciembre 2026", january.ComparisonPeriodLabel);

        var march = DashboardTodaySummaryBuilder.Build(
            new DateOnly(2027, 3, 31),
            new PortfolioDashboardDto(),
            Ytd(2027),
            null,
            Support(),
            Support(),
            new CopiersEquipmentDashboardDto());

        Assert.Equal("1-28 de febrero 2027", march.ComparisonPeriodLabel);
    }

    [Fact]
    public void TodayIsTheFirstTabAndCardsNavigateToTheirExistingDetails()
    {
        var view = File.ReadAllText(Path.Combine(ProjectRoot, "Views", "Dashboard", "Index.cshtml"));
        var script = File.ReadAllText(Path.Combine(ProjectRoot, "wwwroot", "js", "dashboard.js"));
        var styles = File.ReadAllText(Path.Combine(ProjectRoot, "wwwroot", "css", "dashboard.css"));

        var todayPosition = view.IndexOf("data-dashboard-tab=\"today\"", StringComparison.Ordinal);
        var agentPosition = view.IndexOf("data-dashboard-tab=\"agent\"", StringComparison.Ordinal);
        Assert.True(todayPosition >= 0 && todayPosition < agentPosition);
        Assert.Contains("data-today-url", view, StringComparison.Ordinal);
        Assert.Contains("data-dashboard-panel=\"today\"", view, StringComparison.Ordinal);
        Assert.Contains("ensureTodayDashboardMarkup", script, StringComparison.Ordinal);
        Assert.Contains("loadTodayFromExistingEndpoints", script, StringComparison.Ordinal);
        Assert.Contains("data-today-card", script, StringComparison.Ordinal);
        Assert.Contains("setBillingSubtab(subtab || \"overview\")", script, StringComparison.Ordinal);
        Assert.Contains("setPortfolioSubtab(subtab || \"detail\")", script, StringComparison.Ordinal);
        Assert.Contains("setCopiersSubtab(subtab || \"maintenance\")", script, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(3, minmax(0, 1fr))", styles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 680px)", styles, StringComparison.Ordinal);
    }

    private static TodayDashboardCardDto Card(TodayDashboardDto dashboard, string key) =>
        Assert.Single(dashboard.Cards, card => card.Key == key);

    private static TodayDashboardItemDto Item(TodayDashboardCardDto card, string key) =>
        Assert.Single(card.Items, item => item.Key == key);

    private static BillingInvoiceRowDto Invoice(
        string id,
        string date,
        string vertical,
        int verticalValue,
        decimal value,
        bool pending = false,
        bool overdue = false) =>
        new()
        {
            RecordId = id,
            EmissionDateValue = date,
            VerticalLabel = vertical,
            VerticalOptionValue = verticalValue,
            NetTotalInvoice = value,
            IsPortfolioPending = pending,
            IsOverdue = overdue
        };

    private static YtdBreakdownRecordDto Expense(string date, string verticalKey, string verticalLabel, decimal value) =>
        new()
        {
            DateDisplay = date,
            VerticalKey = verticalKey,
            VerticalLabel = verticalLabel,
            Value = value
        };

    private static YtdChartPointDto ExpensePoint(string key, params YtdBreakdownRecordDto[] records) =>
        new()
        {
            Key = key,
            ExpenseSegments = new[]
            {
                new YtdBreakdownSegmentDto { Records = records }
            }
        };

    private static YtdDashboardDto Ytd(int year, params YtdChartPointDto[] points) =>
        new()
        {
            Year = year,
            Chart = new YtdChartDto { Points = points }
        };

    private static SoporteCloudBoardDto Support(params (string Id, string Name, int Tickets)[] owners) =>
        new()
        {
            TotalTickets = owners.Sum(static owner => owner.Tickets),
            CreatorSummaries = owners.Select(static owner => new SoporteCloudCreatorSummaryDto
            {
                CreatorId = owner.Id,
                CreatorName = owner.Name,
                TotalTickets = owner.Tickets
            }).ToList()
        };

    private static CopiersMaintenanceRowDto Maintenance(string date, string ownerId, string ownerName) =>
        new()
        {
            DateValue = date,
            TechnicianId = ownerId,
            TechnicianName = ownerName
        };

    private static string FindProjectRoot([CallerFilePath] string sourcePath = "")
    {
        var directory = new FileInfo(sourcePath).Directory;
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CotizadorInterno.Web.csproj")))
                return directory.FullName;

            directory = directory.Parent;
        }

        directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CotizadorInterno.Web.csproj")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro la raiz de CotizadorInterno.Web.");
    }
}
