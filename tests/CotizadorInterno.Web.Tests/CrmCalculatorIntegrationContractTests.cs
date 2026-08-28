using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Crm;
using CotizadorInterno.Web.Models.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CrmCalculatorIntegrationContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Theory]
    [InlineData(CrmDealKind.EstimatedOpportunity, false, false)]
    [InlineData(CrmDealKind.EstimatedOpportunity, true, false)]
    [InlineData(CrmDealKind.QuotedBusiness, false, false)]
    [InlineData(CrmDealKind.QuotedBusiness, true, true)]
    public void OnlyAProvisionedQuotedBusinessCanBeMarkedWon(
        CrmDealKind kind,
        bool provisioningRequested,
        bool expected)
    {
        var deal = new CrmDealSummary
        {
            KindValue = (int)kind,
            Score = 80m,
            ContractValue = 25_000_000m,
            ProvisioningRequested = provisioningRequested,
            ProvisioningRequestedAtUtc = provisioningRequested
                ? DateTimeOffset.Parse("2026-07-24T12:00:00Z")
                : null,
            ProvisioningRequestId = provisioningRequested ? "provisioning-request-1" : ""
        };

        Assert.Equal(expected, deal.CanMarkWon);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void QuotedBusinessRequiresCompleteProvisioningEvidenceToBeMarkedWon(
        bool hasRequestedAt,
        bool hasRequestId)
    {
        var deal = new CrmDealSummary
        {
            KindValue = (int)CrmDealKind.QuotedBusiness,
            Score = 80m,
            ContractValue = 25_000_000m,
            ProvisioningRequested = true,
            ProvisioningRequestedAtUtc = hasRequestedAt
                ? DateTimeOffset.Parse("2026-07-24T12:00:00Z")
                : null,
            ProvisioningRequestId = hasRequestId ? "provisioning-request-1" : ""
        };

        Assert.False(deal.CanMarkWon);
    }

    [Fact]
    public void CalculatorDealRequestRejectsMalformedGuidRelationsAndUndefinedKind()
    {
        var request = ValidCalculatorRequest();
        request.DealId = "negocio-no-guid";
        request.CompanyId = "empresa-no-guid";
        request.PrimaryContactId = "contacto-no-guid";
        request.Kind = (CrmDealKind)int.MaxValue;

        var errors = Validate(request);

        Assert.Contains(errors, result => AppliesTo(result, nameof(request.DealId)));
        Assert.Contains(errors, result => AppliesTo(result, nameof(request.CompanyId)));
        Assert.Contains(errors, result => AppliesTo(result, nameof(request.PrimaryContactId)));
        Assert.Contains(errors, result => AppliesTo(result, nameof(request.Kind)));
    }

    [Fact]
    public void CalculatorDealRequestAcceptsEmptyOptionalGuidRelations()
    {
        var request = ValidCalculatorRequest();

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void QuotedCalculatorCommandRequiresBothAuthoritativeMeters()
    {
        var command = ValidCalculatorCommand(CrmDealKind.QuotedBusiness);

        var missingMeters = Validate(command);
        Assert.Contains(missingMeters, result => AppliesTo(result, nameof(command.Score)));
        Assert.Contains(missingMeters, result => AppliesTo(result, nameof(command.ContractValue)));

        command.Score = 74.25m;
        command.ContractValue = 36_500_000m;

        Assert.Empty(Validate(command));
    }

    [Fact]
    public void EstimatedOpportunityDoesNotRequireQuotedMeters()
    {
        var command = ValidCalculatorCommand(CrmDealKind.EstimatedOpportunity);

        Assert.Empty(Validate(command));
    }

    [Fact]
    public void NewContactDefaultsToCustomerForTheCurrentActiveCompanyBase()
    {
        var contact = new CrmContactCreateRequest();

        Assert.Equal(CrmContactLifecycle.Customer, contact.Lifecycle);
    }

    [Fact]
    public void CalculatorSendToCrmIsAPostProtectedByCrmAndAntiforgery()
    {
        var action = Assert.Single(
            typeof(CalculatorController)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == nameof(CalculatorController.SendToCrm));

        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());

        var authorization = Assert.Single(
            action.GetCustomAttributes<ModuleAuthorizeAttribute>());
        Assert.Equal(
            AppModule.Crm,
            Assert.IsType<AppModule>(Assert.Single(authorization.Arguments!)));

        var bodyParameter = Assert.Single(
            action.GetParameters(),
            parameter => parameter.ParameterType == typeof(CrmDealFromCalculatorRequest));
        Assert.NotNull(bodyParameter.GetCustomAttribute<FromBodyAttribute>());
    }

    [Fact]
    public void CalculatorCrmSyncIsDisabledByDefaultInServerConfiguration()
    {
        var settings = ReadProjectFile("appsettings.json");
        var controller = ReadProjectFile("Controllers", "CalculatorController.cs");

        Assert.Contains(
            "\"CalculatorSyncEnabled\": false",
            settings,
            StringComparison.Ordinal);
        Assert.Contains(
            "Crm:CalculatorSyncEnabled",
            controller,
            StringComparison.Ordinal);
        Assert.Contains(
            "StatusCodes.Status503ServiceUnavailable",
            controller,
            StringComparison.Ordinal);
        Assert.Contains(
            "application/problem+json",
            controller,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CrmCompanySearchIsAGetProtectedByTheCrmModule()
    {
        var action = Assert.Single(
            typeof(CrmController)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == nameof(CrmController.SearchCompanies));

        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
        Assert.NotNull(action.GetCustomAttribute<AuthorizeForScopesAttribute>());
        Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>());

        var authorization = Assert.Single(
            typeof(CrmController).GetCustomAttributes<ModuleAuthorizeAttribute>());
        Assert.Equal(
            AppModule.Crm,
            Assert.IsType<AppModule>(Assert.Single(authorization.Arguments!)));
    }

    [Fact]
    public void CalculatorCrmSelectorUsesTheDedicatedCrmCompanySearchEndpoint()
    {
        var view = ReadProjectFile("Views", "Calculator", "Index.cshtml");
        var searchStart = view.IndexOf(
            "async function crmCompanySearch(q)",
            StringComparison.Ordinal);
        var searchEnd = view.IndexOf(
            "async function clientRenewalDateSearch",
            searchStart,
            StringComparison.Ordinal);

        Assert.Contains(
            "Url.Action(\"SearchCompanies\", \"Crm\")",
            view,
            StringComparison.Ordinal);
        Assert.True(searchStart >= 0);
        Assert.True(searchEnd > searchStart);

        var crmSearchFunction = view[searchStart..searchEnd];
        Assert.Contains("CRM_COMPANY_SEARCH_URL", crmSearchFunction, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "/Calculator/ClientSearch",
            crmSearchFunction,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CalculatorCrmSelectorDisplaysLifecycleAndPreservesOperationalClientIdentity()
    {
        var view = ReadProjectFile("Views", "Calculator", "Index.cshtml");

        Assert.Contains(
            "${escapeHtml(company.lifecycleLabel || \"Lead\")}",
            view,
            StringComparison.Ordinal);
        Assert.Contains(
            "operationalClientId: company.operationalClientId || \"\"",
            view,
            StringComparison.Ordinal);
        Assert.Contains(
            "lifecycleLabel: company.lifecycleLabel || \"\"",
            view,
            StringComparison.Ordinal);
        Assert.Contains(
            "operationalClientId: crmState.company.operationalClientId || \"\"",
            view,
            StringComparison.Ordinal);
        Assert.Contains(
            "lifecycleLabel: crmState.company.lifecycleLabel || \"\"",
            view,
            StringComparison.Ordinal);
        Assert.Contains(
            "companyId: crmState.company.id",
            view,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisioningRequestCarriesTheLinkedCrmDealId()
    {
        var property = typeof(ProvisioningRequestInput)
            .GetProperty(nameof(ProvisioningRequestInput.CrmDealId));

        Assert.NotNull(property);
        Assert.Equal(typeof(string), property!.PropertyType);
        Assert.True(property.CanRead);
        Assert.True(property.CanWrite);
    }

    [Fact]
    public void BrowserRequestCannotSupplyAuthoritativeDealMeters()
    {
        Assert.Null(typeof(CrmDealFromCalculatorRequest).GetProperty(nameof(CrmCalculatorDealUpsertCommand.Score)));
        Assert.Null(typeof(CrmDealFromCalculatorRequest).GetProperty(nameof(CrmCalculatorDealUpsertCommand.ContractValue)));
        Assert.NotNull(typeof(CrmCalculatorDealUpsertCommand).GetProperty(nameof(CrmCalculatorDealUpsertCommand.Score)));
        Assert.NotNull(typeof(CrmCalculatorDealUpsertCommand).GetProperty(nameof(CrmCalculatorDealUpsertCommand.ContractValue)));
    }

    [Fact]
    public void ProvisioningLinksCrmOnlyAfterTheExternalFlowAcceptsTheRequest()
    {
        var controller = ReadProjectFile("Controllers", "CalculatorController.cs");
        var submitStart = controller.IndexOf(
            "public async Task<IActionResult> SubmitProvisioning",
            StringComparison.Ordinal);
        var flowCall = controller.IndexOf("PostAsJsonAsync", submitStart, StringComparison.Ordinal);
        var upsert = controller.IndexOf("UpsertDealFromCalculatorAsync", flowCall, StringComparison.Ordinal);
        var markProvisioning = controller.IndexOf("MarkProvisioningRequestedAsync", upsert, StringComparison.Ordinal);

        Assert.True(submitStart >= 0);
        Assert.True(flowCall > submitStart);
        Assert.True(upsert > flowCall);
        Assert.True(markProvisioning > upsert);
        Assert.Contains("flowAccepted = true", controller, StringComparison.Ordinal);
        Assert.Contains("canMarkWon = false", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRequestedScenarioIsNeverReplacedWithTheFirstScenario()
    {
        var view = ReadProjectFile("Views", "Calculator", "Index.cshtml");

        Assert.Contains("requestedScenarioMissing = true", view, StringComparison.Ordinal);
        Assert.Contains("renderUnavailableRequestedScenario()", view, StringComparison.Ordinal);
        Assert.Contains("activeScenarioId = null", view, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "scenarios.find(s => s.id === requestedScenarioId)?.id",
            view,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CalculatorUiKeepsCrmSyncUnavailableWithoutReenablingTheButton()
    {
        var view = ReadProjectFile("Views", "Calculator", "Index.cshtml");

        Assert.Contains("id=\"btnSendToCrm\"", view, StringComparison.Ordinal);
        Assert.Contains("aria-disabled=\"true\"", view, StringComparison.Ordinal);
        Assert.Contains(
            "Disponible cuando el CRM se publique para todos",
            view,
            StringComparison.Ordinal);
        Assert.Contains("btnSendToCrm.disabled = true", view, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "btnSendToCrm.addEventListener(\"click\", openCrmDealModal)",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "window.setTimeout(openCrmDealModal, 0)",
            view,
            StringComparison.Ordinal);
        Assert.Contains("id=\"crmDealModal\"", view, StringComparison.Ordinal);
        Assert.Contains("id=\"crmDealKind\"", view, StringComparison.Ordinal);
        Assert.Contains("CrmDealKind.EstimatedOpportunity", view, StringComparison.Ordinal);
        Assert.Contains("CrmDealKind.QuotedBusiness", view, StringComparison.Ordinal);
        Assert.Contains("Oportunidad estimada", view, StringComparison.Ordinal);
        Assert.Contains("Negocio cotizado", view, StringComparison.Ordinal);
        Assert.Contains("id=\"crmQuotedScore\"", view, StringComparison.Ordinal);
        Assert.Contains("id=\"crmQuotedContractValue\"", view, StringComparison.Ordinal);
        Assert.Contains("id=\"btnConfirmCrmDeal\"", view, StringComparison.Ordinal);
        Assert.Contains("const CRM_SEND_URL = \"/Calculator/SendToCrm\";", view, StringComparison.Ordinal);
        Assert.Contains("requestedCrmDealId", view, StringComparison.Ordinal);
        Assert.Contains(
            "matchedScenario.crmDealId = isGuid(requestedCrmDealId) ? requestedCrmDealId : \"\"",
            view,
            StringComparison.Ordinal);
        Assert.Contains("dealId: s.crmDealId || \"\"", view, StringComparison.Ordinal);
        Assert.Contains("s.crmDealId = dealId", view, StringComparison.Ordinal);
        Assert.Contains("payload.crmDealId = s.crmDealId", view, StringComparison.Ordinal);
        Assert.Contains("@Html.AntiForgeryToken()", view, StringComparison.Ordinal);
    }

    [Fact]
    public void CalculatorCrmModalCannotOpenWhileCalculatorSyncIsPaused()
    {
        var view = ReadProjectFile("Views", "Calculator", "Index.cshtml");

        foreach (var markup in new[]
                 {
                     "role=\"dialog\"",
                     "aria-modal=\"true\"",
                     "aria-labelledby=\"crmDealModalTitle\"",
                     "aria-describedby=\"crmDealModalDescription\"",
                     "data-crm-deal-dialog"
                 })
        {
            Assert.Contains(markup, view, StringComparison.Ordinal);
        }

        foreach (var behavior in new[]
                 {
                     "function trapCrmDealModalFocus(event)",
                     "event.key === \"Escape\"",
                     "lockCrmDealModalBackground();",
                     "sibling.inert = true",
                     "element.inert = false",
                     "crmDealModalReturnFocus",
                     "returnFocus.focus()",
                     "crmDealModal.addEventListener(\"keydown\", trapCrmDealModalFocus)"
                 })
        {
            Assert.Contains(behavior, view, StringComparison.Ordinal);
        }

        var openStart = view.IndexOf(
            "function openCrmDealModal(trigger)",
            StringComparison.Ordinal);
        var openEnd = view.IndexOf(
            "function closeCrmDealModal()",
            openStart,
            StringComparison.Ordinal);
        Assert.True(openStart >= 0);
        Assert.True(openEnd > openStart);
        Assert.Contains("return;", view[openStart..openEnd], StringComparison.Ordinal);
        Assert.DoesNotContain(
            "btnSendToCrm.addEventListener",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "window.setTimeout(openCrmDealModal",
            view,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CalculatorReadsCompanyAndContactContextFromDedicatedCrmParameters()
    {
        var view = ReadProjectFile("Views", "Calculator", "Index.cshtml");
        var expectedParameters = new[]
        {
            "crmCompanyId",
            "crmCompanyName",
            "crmContactId",
            "crmContactName",
            "crmDealKind",
            "crmDealName",
            "crmProbability",
            "crmEstimatedValue"
        };

        foreach (var parameter in expectedParameters)
        {
            Assert.Contains(
                $"queryParameters.get(\"{parameter}\")",
                view,
                StringComparison.Ordinal);
        }

        Assert.Contains("companyId: crmState.company.id", view, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "queryParameters.get(\"companyId\")",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "queryParameters.get(\"contactId\")",
            view,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CrmNewDealActionsOpenTheManualOrCalculatorFlow()
    {
        var view = ReadProjectFile("Views", "Crm", "Index.cshtml");
        var drawer = ReadProjectFile("Views", "Crm", "_CrmDealDrawer.cshtml");
        var script = ReadProjectFile("wwwroot", "js", "crm.js");
        var calculatorView = ReadProjectFile("Views", "Calculator", "Index.cshtml");
        var actions = Regex.Matches(
            view,
            @"<button\b(?<attributes>[^>]*)>\s*Nuevo negocio\s*</button>",
            RegexOptions.CultureInvariant);

        Assert.Equal(2, actions.Count);
        foreach (Match action in actions)
        {
            var attributes = action.Groups["attributes"].Value;
            Assert.Contains("type=\"button\"", attributes, StringComparison.Ordinal);
            Assert.Contains("data-open-deal", attributes, StringComparison.Ordinal);
        }

        Assert.Contains("data-create-deal-url", view, StringComparison.Ordinal);
        Assert.Contains("data-calculator-url", view, StringComparison.Ordinal);
        Assert.Contains("<partial name=\"_CrmDealDrawer\" />", view, StringComparison.Ordinal);
        Assert.Contains("name=\"DealCreationMode\"", drawer, StringComparison.Ordinal);
        Assert.Contains("value=\"manual\"", drawer, StringComparison.Ordinal);
        Assert.Contains("checked", drawer, StringComparison.Ordinal);
        Assert.Contains("Sin calculadora", drawer, StringComparison.Ordinal);
        Assert.Contains("value=\"calculator\"", drawer, StringComparison.Ordinal);
        Assert.Contains("Con calculadora", drawer, StringComparison.Ordinal);
        Assert.Contains("name=\"EstimatedContractValue\"", drawer, StringComparison.Ordinal);
        Assert.Contains("name=\"EstimatedScore\"", drawer, StringComparison.Ordinal);
        Assert.Contains("name=\"Category\"", drawer, StringComparison.Ordinal);
        Assert.Contains("name=\"BriefDescription\"", drawer, StringComparison.Ordinal);
        Assert.Contains("if (dealMode() === \"calculator\")", script, StringComparison.Ordinal);
        Assert.Contains("fetch(urls.createDeal", script, StringComparison.Ordinal);
        Assert.Contains("target.searchParams.set(\"newCrmOpportunity\", \"1\")", script, StringComparison.Ordinal);

        Assert.Contains("if (requestedNewCrmOpportunity)", calculatorView, StringComparison.Ordinal);
        Assert.Contains("clearNewCrmOpportunityRequest();", calculatorView, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "window.setTimeout(openCrmDealModal",
            calculatorView,
            StringComparison.Ordinal);
        Assert.Contains(
            nameof(CrmController.CreateDeal),
            typeof(CrmController)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Select(method => method.Name));
    }

    [Fact]
    public void CrmDealWithoutScenarioDoesNotBuildACalculatorEditUrl()
    {
        var view = ReadProjectFile("Views", "Crm", "Index.cshtml");

        Assert.Contains(
            "var hasCalculatorScenario = !string.IsNullOrWhiteSpace(deal.ScenarioId);",
            view,
            StringComparison.Ordinal);
        Assert.Contains(
            "var calculatorUrl = hasCalculatorScenario",
            view,
            StringComparison.Ordinal);
        foreach (var parameter in new[]
                 {
                     "scenarioId = deal.ScenarioId",
                     "crmDealId = deal.Id",
                     "crmCompanyId = deal.CompanyId",
                     "crmContactId = deal.PrimaryContactId",
                     "crmDealKind = deal.KindValue",
                     "crmDealName = deal.Name",
                     "crmProbability = deal.Probability",
                     "crmEstimatedValue = deal.EstimatedValue"
                 })
        {
            Assert.Contains(parameter, view, StringComparison.Ordinal);
        }
        Assert.Contains("@if (hasCalculatorScenario)", view, StringComparison.Ordinal);
        Assert.Contains("Sin escenario de calculadora", view, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Url.Action(\"Index\", \"Calculator\", new { crmDealId = deal.Id })",
            view,
            StringComparison.Ordinal);
    }

    private static CrmDealFromCalculatorRequest ValidCalculatorRequest() => new()
    {
        DealId = "",
        ScenarioId = "escenario-editable-01",
        CompanyId = Guid.NewGuid().ToString(),
        PrimaryContactId = "",
        Name = "Oportunidad calculada",
        Kind = CrmDealKind.EstimatedOpportunity,
        EstimatedValue = 10_000_000m,
        Probability = 25m
    };

    private static CrmCalculatorDealUpsertCommand ValidCalculatorCommand(CrmDealKind kind) => new()
    {
        DealId = "",
        ScenarioId = "escenario-editable-01",
        CompanyId = Guid.NewGuid().ToString(),
        PrimaryContactId = "",
        Name = "Registro calculado",
        Kind = kind,
        EstimatedValue = kind == CrmDealKind.EstimatedOpportunity ? 10_000_000m : 0m,
        Probability = 25m
    };

    private static IReadOnlyList<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            value,
            new ValidationContext(value),
            results,
            validateAllProperties: true);
        return results;
    }

    private static bool AppliesTo(ValidationResult result, string propertyName) =>
        result.MemberNames.Contains(propertyName, StringComparer.Ordinal);

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
