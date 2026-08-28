using System.Text.RegularExpressions;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CrmDetailUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void WorkspaceRecordsUseSemanticLinksToTheirCanonicalDetailActions()
    {
        var view = ReadProjectFile("Views", "Crm", "Index.cshtml");
        var expectedLinks = new[]
        {
            ("Company", "@company.Id"),
            ("Contact", "@contact.Id"),
            ("Deal", "@deal.Id"),
            ("Activity", "@activity.Id")
        };

        foreach (var (action, routeValue) in expectedLinks)
        {
            Assert.Contains(
                OpeningTagAttributes(view, "a"),
                attributes =>
                    HasAttribute(attributes, "asp-action", action)
                    && HasAttribute(attributes, "asp-route-id", routeValue));
        }

        foreach (var entity in new[] { "company", "contact", "deal", "activity" })
        {
            Assert.Contains(
                $"data-{entity}-detail-url-template",
                view,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AjaxCreatedRecordsUseTheSameCanonicalDetailLinkTemplates()
    {
        var script = ReadProjectFile("wwwroot", "js", "crm.js");

        foreach (var entity in new[] { "company", "contact", "deal", "activity" })
        {
            Assert.Contains(
                $"{entity}DetailUrlTemplate",
                script,
                StringComparison.Ordinal);
        }

        Assert.Contains("crm-record-link", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailUrlTemplatesUseAValidGuidSentinelBeforeClientSideReplacement()
    {
        foreach (var viewName in new[] { "Index", "Company", "Contact", "Deal", "Activity" })
        {
            var view = ReadProjectFile("Views", "Crm", $"{viewName}.cshtml");

            Assert.Contains("id = Guid.Empty", view, StringComparison.Ordinal);
            Assert.Contains(
                ".Replace(sentinel, \"__id__\", StringComparison.Ordinal)",
                view,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "new { id = \"__id__\" }",
                view,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WorkspaceCreationNavigatesToEveryCreatedRecordAndPreservesActivityId()
    {
        var script = ReadProjectFile("wwwroot", "js", "crm.js");

        Assert.Equal(
            5,
            Regex.Matches(
                script,
                @"window\.location\.assign\(target\)",
                RegexOptions.CultureInvariant).Count);
        Assert.Contains(
            "const target = detailUrl(urls.companyDetail, id);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const target = detailUrl(urls.contactDetail, id);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const target = detailUrl(urls.activityDetail, id);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const target = detailUrl(urls.dealDetail, id);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const target = calculatorDealUrl();",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "id: record.id || record.Id || \"\"",
            script,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Company", "CrmCompanyDetailViewModel")]
    [InlineData("Contact", "CrmContactDetailViewModel")]
    [InlineData("Deal", "CrmDealDetailViewModel")]
    [InlineData("Activity", "CrmActivityDetailViewModel")]
    public void EveryDetailViewUsesTheTypedSharedRecordShell(
        string viewName,
        string modelName)
    {
        var view = ReadProjectFile("Views", "Crm", $"{viewName}.cshtml");

        Assert.Matches(
            $@"@model\s+(?:[\w.]+\.)?{Regex.Escape(modelName)}\b",
            view);
        Assert.Contains("id=\"crmDetailApp\"", view, StringComparison.Ordinal);
        Assert.Contains("crm-record-header", view, StringComparison.Ordinal);
        Assert.Contains("crm-record-layout", view, StringComparison.Ordinal);
        Assert.Contains("crm-record-card", view, StringComparison.Ordinal);
        Assert.Contains("_CrmSidebar", view, StringComparison.Ordinal);
        Assert.Contains("_CrmDetailDrawers", view, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Company")]
    [InlineData("Contact")]
    [InlineData("Deal")]
    [InlineData("Activity")]
    public void EveryDetailViewConvertsDisplayedHoursExplicitlyToBogota(string viewName)
    {
        var view = ReadProjectFile("Views", "Crm", $"{viewName}.cshtml");

        Assert.Contains("\"America/Bogota\"", view, StringComparison.Ordinal);
        Assert.Contains("\"SA Pacific Standard Time\"", view, StringComparison.Ordinal);
        Assert.Contains(
            "TimeZoneInfo.ConvertTime(value, bogotaTimeZone)",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain(".ToLocalTime()", view, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Company", 3)]
    [InlineData("Contact", 2)]
    [InlineData("Deal", 2)]
    [InlineData("Activity", 1)]
    public void DetailPaginationOnlyRendersLinksForAvailableDestinations(
        string viewName,
        int paginatorCount)
    {
        var view = ReadProjectFile("Views", "Crm", $"{viewName}.cshtml");
        const RegexOptions options =
            RegexOptions.CultureInvariant | RegexOptions.Singleline;

        Assert.Equal(
            paginatorCount,
            Regex.Matches(
                view,
                @"@if\s*\(Model\.\w+\.HasPrevious\)\s*\{\s*<a\b",
                options).Count);
        Assert.Equal(
            paginatorCount,
            Regex.Matches(
                view,
                @"@if\s*\(Model\.\w+\.HasNext\)\s*\{\s*<a\b",
                options).Count);
        Assert.Equal(
            paginatorCount,
            Regex.Matches(
                view,
                @"<span\s+aria-disabled=""true"">Anterior</span>",
                options).Count);
        Assert.Equal(
            paginatorCount,
            Regex.Matches(
                view,
                @"<span\s+aria-disabled=""true"">Siguiente</span>",
                options).Count);
        Assert.DoesNotContain("aria-disabled=\"@(!Model.", view, StringComparison.Ordinal);
        Assert.DoesNotContain("tabindex=\"@(Model.", view, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Company", "Contact", "Deal", "Activity")]
    [InlineData("Contact", "Company", "Deal", "Activity")]
    [InlineData("Deal", "Company", "Contact", "Activity")]
    [InlineData("Activity", "Company", "Contact", "Deal")]
    public void AssociatedRecordsAreNavigableFromEveryDetailView(
        string viewName,
        string firstAssociation,
        string secondAssociation,
        string thirdAssociation)
    {
        var view = ReadProjectFile("Views", "Crm", $"{viewName}.cshtml");

        foreach (var association in new[]
                 {
                     firstAssociation,
                     secondAssociation,
                     thirdAssociation
                 })
        {
            Assert.Contains(
                OpeningTagAttributes(view, "a"),
                attributes => HasAttribute(attributes, "asp-action", association));
        }
    }

    [Fact]
    public void CompanyDetailCreatesContactsActivitiesAndDealsInItsLockedContext()
    {
        var view = ReadProjectFile("Views", "Crm", "Company.cshtml");

        Assert.Contains("data-create-contact-url", view, StringComparison.Ordinal);
        Assert.Contains("data-create-activity-url", view, StringComparison.Ordinal);
        Assert.Contains("data-create-deal-url", view, StringComparison.Ordinal);
        Assert.Contains("data-calculator-url", view, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"CrmCompanyId\"] = Model.Company.Id", view, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"CrmCompanyName\"] = Model.Company.Name", view, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"CrmDealCompanyId\"] = Model.Company.Id", view, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"CrmDealCompanyName\"] = Model.Company.Name", view, StringComparison.Ordinal);
        Assert.Contains("data-detail-open-contact", view, StringComparison.Ordinal);
        Assert.Contains("data-detail-open-deal", view, StringComparison.Ordinal);
        Assert.Contains("data-detail-open-activity", view, StringComparison.Ordinal);
        Assert.Contains("<partial name=\"_CrmDealDrawer\" />", view, StringComparison.Ordinal);
    }

    [Fact]
    public void ContactDetailCreatesActivitiesAndDealsInItsCompanyContext()
    {
        var view = ReadProjectFile("Views", "Crm", "Contact.cshtml");

        Assert.Contains("data-create-activity-url", view, StringComparison.Ordinal);
        Assert.Contains("data-create-deal-url", view, StringComparison.Ordinal);
        Assert.Contains("data-calculator-url", view, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"CrmCompanyId\"] = companyId", view, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"CrmContactId\"] = Model.Contact.Id", view, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"CrmContactName\"] = Model.Contact.FullName", view, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"CrmDealCompanyId\"] = companyId", view, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"CrmDealContactId\"] = Model.Contact.Id", view, StringComparison.Ordinal);
        Assert.Contains("data-detail-open-deal", view, StringComparison.Ordinal);
        Assert.Contains("data-detail-open-activity", view, StringComparison.Ordinal);
        Assert.Contains("<partial name=\"_CrmDealDrawer\" />", view, StringComparison.Ordinal);
    }

    [Fact]
    public void DealDetailCreatesActivitiesAndEditsOnlyItsCalculatorScenario()
    {
        var view = ReadProjectFile("Views", "Crm", "Deal.cshtml");

        Assert.Contains("data-create-activity-url", view, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"CrmCompanyId\"]", view, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"CrmContactId\"]", view, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"CrmDealId\"] = Model.Deal.Id", view, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"CrmDealName\"] = Model.Deal.Name", view, StringComparison.Ordinal);
        Assert.Contains("data-detail-open-activity", view, StringComparison.Ordinal);
        foreach (var parameter in new[]
                 {
                     "scenarioId = Model.Deal.ScenarioId",
                     "crmDealId = Model.Deal.Id",
                     "crmCompanyId = companyId",
                     "crmCompanyName = companyName",
                     "crmContactId = contactId",
                     "crmContactName = contactName",
                     "crmDealKind = Model.Deal.KindValue",
                     "crmDealName = Model.Deal.Name",
                     "crmProbability = Model.Deal.Probability",
                     "crmEstimatedValue = Model.Deal.EstimatedValue"
                 })
        {
            Assert.Contains(parameter, view, StringComparison.Ordinal);
        }
        Assert.Contains("href=\"@calculatorUrl\"", view, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityDetailCanCreateAFollowUpWithTheSameAssociations()
    {
        var view = ReadProjectFile("Views", "Crm", "Activity.cshtml");

        Assert.Contains("data-create-activity-url", view, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"CrmCompanyId\"]", view, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"CrmContactId\"]", view, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"CrmDealId\"]", view, StringComparison.Ordinal);
        Assert.Contains("data-detail-open-activity", view, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailDrawersPostOnlySupportedContextualRecordTypes()
    {
        var partial = ReadProjectFile("Views", "Crm", "_CrmDetailDrawers.cshtml");

        Assert.Contains("data-detail-contact-form", partial, StringComparison.Ordinal);
        Assert.Contains("data-detail-activity-form", partial, StringComparison.Ordinal);
        Assert.Contains("name=\"CompanyId\"", partial, StringComparison.Ordinal);
        Assert.Contains("name=\"ContactId\"", partial, StringComparison.Ordinal);
        Assert.Contains("name=\"DealId\"", partial, StringComparison.Ordinal);
        Assert.DoesNotContain("data-deal-form", partial, StringComparison.Ordinal);
        Assert.DoesNotContain("data-create-deal", partial, StringComparison.Ordinal);

        foreach (var viewName in new[] { "Company", "Contact", "Deal", "Activity" })
        {
            var view = ReadProjectFile("Views", "Crm", $"{viewName}.cshtml");
            Assert.Contains("@Html.AntiForgeryToken()", view, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ManualOrCalculatorDealCreationIsAvailableInTheRequestedContexts()
    {
        foreach (var viewName in new[] { "Index", "Company", "Contact" })
        {
            var view = ReadProjectFile("Views", "Crm", $"{viewName}.cshtml");
            Assert.Contains("data-create-deal-url", view, StringComparison.Ordinal);
            Assert.Contains("data-calculator-url", view, StringComparison.Ordinal);
            Assert.Contains("<partial name=\"_CrmDealDrawer\" />", view, StringComparison.Ordinal);
        }

        var drawer = ReadProjectFile("Views", "Crm", "_CrmDealDrawer.cshtml");
        Assert.Contains("data-deal-form", drawer, StringComparison.Ordinal);
        Assert.Contains("value=\"manual\"", drawer, StringComparison.Ordinal);
        Assert.Contains("checked", drawer, StringComparison.Ordinal);
        Assert.Contains("Sin calculadora", drawer, StringComparison.Ordinal);
        Assert.Contains("value=\"calculator\"", drawer, StringComparison.Ordinal);
        Assert.Contains("Con calculadora", drawer, StringComparison.Ordinal);

        foreach (var scriptName in new[] { "crm.js", "crm-detail.js" })
        {
            var script = ReadProjectFile("wwwroot", "js", scriptName);
            Assert.Contains("fetch(urls.createDeal", script, StringComparison.Ordinal);
            Assert.Contains("if (dealMode() === \"calculator\")", script, StringComparison.Ordinal);
            Assert.Contains("target.searchParams.set(\"newCrmOpportunity\", \"1\")", script, StringComparison.Ordinal);
        }
    }

    private static IReadOnlyList<string> OpeningTagAttributes(
        string input,
        string tagName) =>
        Regex.Matches(
                input,
                $@"<{Regex.Escape(tagName)}\b(?<attributes>[^>]*)>",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
            .Select(match => match.Groups["attributes"].Value)
            .ToArray();

    private static bool HasAttribute(
        string attributes,
        string attributeName,
        string value) =>
        Regex.IsMatch(
            attributes,
            $@"\b{Regex.Escape(attributeName)}\s*=\s*[""']{Regex.Escape(value)}[""']",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

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
