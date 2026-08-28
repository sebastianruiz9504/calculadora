using System.Text.RegularExpressions;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CrmNavigationContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void SidebarAndWorkspaceExposeTheSameFiveSubmodules()
    {
        var view = ReadProjectFile("Views", "Crm", "Index.cshtml");
        var expected = new[]
        {
            "actividades",
            "contactos",
            "empresas",
            "negocios",
            "resumen"
        };
        var navigation = Captures(view, @"\bdata-crm-nav=""([^""]+)""");
        var panels = Captures(view, @"\bdata-crm-view=""([^""]+)""");

        Assert.Equal(expected, navigation.Order(StringComparer.Ordinal));
        Assert.Equal(expected, panels.Order(StringComparer.Ordinal));
        Assert.Equal(navigation.Count, navigation.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(panels.Count, panels.Distinct(StringComparer.Ordinal).Count());

        foreach (var name in expected)
        {
            Assert.Contains($"aria-controls=\"crm-{name}\"", view, StringComparison.Ordinal);
            Assert.Contains($"id=\"crm-{name}\"", view, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NavigationScriptShowsOnlyOneViewAndPreservesItAcrossNavigation()
    {
        var script = ReadProjectFile("wwwroot", "js", "crm.js");

        Assert.Contains("view.hidden = !active", script, StringComparison.Ordinal);
        Assert.Contains("link.setAttribute(\"aria-current\", \"page\")", script, StringComparison.Ordinal);
        Assert.Contains("window.history.pushState", script, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"popstate\"", script, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"hashchange\"", script, StringComparison.Ordinal);
        Assert.Contains("preserveCrmViewInNavigation", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SidebarExpandsForPointerAndKeyboardAndHasANonHoverLayout()
    {
        var styles = ReadProjectFile("wwwroot", "css", "crm.css");

        Assert.Contains(".crm-sidebar:hover", styles, StringComparison.Ordinal);
        Assert.Contains(".crm-sidebar:has(:focus-visible)", styles, StringComparison.Ordinal);
        Assert.Contains("(hover: none)", styles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 900px)", styles, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void CompanyDirectoryUsesTheFullWorkspaceAndEveryRowOpensItsProfile()
    {
        var view = ReadProjectFile("Views", "Crm", "Index.cshtml");
        var styles = ReadProjectFile("wwwroot", "css", "crm.css");
        var script = ReadProjectFile("wwwroot", "js", "crm.js");

        Assert.Contains("class=\"crm-directory crm-directory--single\"", view, StringComparison.Ordinal);
        Assert.Contains(".crm-directory.crm-directory--single", styles, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 1fr);", styles, StringComparison.Ordinal);
        Assert.Contains(".crm-table__row--companies[data-company-row] .crm-record-link::after", styles, StringComparison.Ordinal);
        Assert.Contains("inset: 0;", styles, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"Company\"", view, StringComparison.Ordinal);
        Assert.Contains("nameLink.href = detailUrl(urls.companyDetail, company.id)", script, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> Captures(string input, string pattern) =>
        Regex.Matches(input, pattern, RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value)
            .ToArray();

    private static string ReadProjectFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. parts]));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CotizadorInterno.Web.csproj")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró la raíz de CotizadorInterno.Web.");
    }
}
