using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class DashboardPerformanceContractTests
{
    private static readonly string ProjectRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void BillingInvoicesUsesMonthAndServerPagination()
    {
        var view = Read("Views", "Dashboard", "Index.cshtml");
        var script = Read("wwwroot", "js", "dashboard.js");
        var controller = Read("Controllers", "DashboardController.cs");
        var service = Read("Services", "DataverseService.Dashboard.cs");

        Assert.Contains("billingInvoicesMonth", view, StringComparison.Ordinal);
        Assert.Contains("billingInvoicesPreviousPageBtn", view, StringComparison.Ordinal);
        Assert.Contains("billingInvoicesNextPageBtn", view, StringComparison.Ordinal);
        Assert.Contains("pageSize", script, StringComparison.Ordinal);
        Assert.Contains("duplicatesOnly", script, StringComparison.Ordinal);
        Assert.Contains("GetBillingInvoicesPageAsync", controller, StringComparison.Ordinal);
        Assert.Contains("GetBillingRecordsAsync(", service, StringComparison.Ordinal);
        Assert.Contains("start.AddMonths(1)", service, StringComparison.Ordinal);
    }

    [Fact]
    public void HiddenHardwareAndSupportCloudAreLoadedOnlyWhenTheirTabsBecomeVisible()
    {
        var hardware = Read("wwwroot", "js", "hardware.js");
        var support = Read("wwwroot", "js", "support-cloud.js");

        Assert.Contains("initializeWhenVisible", hardware, StringComparison.Ordinal);
        Assert.Contains("MutationObserver", hardware, StringComparison.Ordinal);
        Assert.Contains("data-dashboard-group-target=\"support-cloud\"", support, StringComparison.Ordinal);
        Assert.Contains("data-dashboard-panel=\"support-cloud\"", support, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportCloudDateRangeIsAppliedInDataverseQuery()
    {
        var tickets = Read("Services", "DataverseService.SoporteCloud.cs");
        var trainings = Read("Services", "DataverseService.SoporteCloud.Capacitaciones.cs");

        Assert.Contains("BuildBillingDateFilter(", tickets, StringComparison.Ordinal);
        Assert.Contains("$filter=", tickets, StringComparison.Ordinal);
        Assert.Contains("BuildBillingDateFilter(SoporteCloudTrainingDateField", trainings, StringComparison.Ordinal);
        Assert.Contains("$filter=", trainings, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardResponsesAreCompressedAndPortfolioDoesNotForceReload()
    {
        var program = Read("Program.cs");
        var dashboard = Read("wwwroot", "js", "dashboard.js");

        Assert.Contains("AddResponseCompression", program, StringComparison.Ordinal);
        Assert.Contains("UseResponseCompression", program, StringComparison.Ordinal);
        Assert.DoesNotContain("setPortfolioSubtab(state.portfolioSubtab, { refresh: true })", dashboard, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine(new[] { ProjectRoot }.Concat(segments).ToArray()));
}
