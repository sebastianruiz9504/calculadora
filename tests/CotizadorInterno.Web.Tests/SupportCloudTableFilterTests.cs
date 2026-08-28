using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class SupportCloudTableFilterTests
{
    private static readonly string ProjectRoot = FindProjectRoot();

    [Theory]
    [InlineData("Views", "Dashboard", "Index.cshtml")]
    [InlineData("Views", "SoporteCloud", "Index.cshtml")]
    public void TicketTablesExposeAFilterForEveryVisibleColumn(
        string directory,
        string feature,
        string fileName)
    {
        var view = File.ReadAllText(Path.Combine(ProjectRoot, directory, feature, fileName));
        var filterKeys = new[]
        {
            "creationDate",
            "ticket",
            "client",
            "state",
            "type",
            "creator",
            "hours",
            "attachment",
            "actions"
        };

        foreach (var filterKey in filterKeys)
        {
            Assert.Contains(
                $"data-sc-filter=\"{filterKey}\"",
                view,
                StringComparison.Ordinal);
        }

        Assert.Contains("data-sc-clear-filters", view, StringComparison.Ordinal);
    }

    [Fact]
    public void ClientCombinesColumnFiltersAndSupportsNumericHoursExpressions()
    {
        var script = File.ReadAllText(
            Path.Combine(ProjectRoot, "wwwroot", "js", "support-cloud.js"));

        Assert.Contains("function getFilteredRecords()", script, StringComparison.Ordinal);
        Assert.Contains("terms.every(term => haystack.includes(term))", script, StringComparison.Ordinal);
        Assert.Contains("function matchesHoursFilter(", script, StringComparison.Ordinal);
        Assert.Contains("function matchesAttachmentFilter(", script, StringComparison.Ordinal);
        Assert.Contains("Ningún ticket coincide con los filtros aplicados.", script, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CotizadorInterno.Web.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "No se encontró la raíz del proyecto CotizadorInterno.Web.");
    }
}
