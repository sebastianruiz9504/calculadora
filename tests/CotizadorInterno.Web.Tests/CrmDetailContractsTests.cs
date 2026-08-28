using System.ComponentModel.DataAnnotations;
using System.Reflection;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.Crm;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CrmDetailContractsTests
{
    [Theory]
    [InlineData("Company", typeof(CrmCompanyDetailViewModel), "/Crm/Companies/{id:guid}")]
    [InlineData("Contact", typeof(CrmContactDetailViewModel), "/Crm/Contacts/{id:guid}")]
    [InlineData("Deal", typeof(CrmDealDetailViewModel), "/Crm/Deals/{id:guid}")]
    [InlineData("Activity", typeof(CrmActivityDetailViewModel), "/Crm/Activities/{id:guid}")]
    public void DetailControllerActionsAreProtectedGetEndpoints(
        string actionName,
        Type expectedModelType,
        string expectedRoute)
    {
        var action = Assert.Single(
            CrmActions(),
            method => method.Name == actionName);

        var httpGet = action.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(httpGet);
        Assert.Equal(expectedRoute, httpGet!.Template);
        Assert.Null(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<AuthorizeForScopesAttribute>());
        Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Equal(typeof(Task<IActionResult>), action.ReturnType);

        Assert.Contains(
            action.GetParameters(),
            parameter => parameter.ParameterType == typeof(Guid)
                && string.Equals(parameter.Name, "id", StringComparison.Ordinal));
        Assert.Contains(
            action.GetParameters(),
            parameter => parameter.ParameterType == typeof(CrmDetailQuery));

        var repositoryMethod = Assert.Single(
            typeof(ICrmRepository).GetMethods(),
            method => method.Name == $"Get{actionName}DetailAsync"
                && method.GetParameters().Any(
                    parameter => parameter.ParameterType == typeof(CrmAccessScope)));
        Assert.Equal(typeof(Task<>).MakeGenericType(expectedModelType), repositoryMethod.ReturnType);
        Assert.Equal(
            [
                typeof(string),
                typeof(CrmDetailQuery),
                typeof(CrmAccessScope),
                typeof(CancellationToken)
            ],
            repositoryMethod.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void DetailControllerKeepsTheCrmModuleBoundary()
    {
        var authorization = Assert.Single(
            typeof(CrmController).GetCustomAttributes<ModuleAuthorizeAttribute>());

        Assert.Equal(
            AppModule.Crm,
            Assert.IsType<AppModule>(Assert.Single(authorization.Arguments!)));
    }

    [Fact]
    public void DetailQueryHasIndependentBoundedPaginationForEveryAssociation()
    {
        var defaults = new CrmDetailQuery();

        Assert.Equal(1, defaults.ContactPage);
        Assert.Equal(1, defaults.DealPage);
        Assert.Equal(1, defaults.ActivityPage);
        Assert.Equal(1, defaults.HistoryPage);
        Assert.Equal(12, defaults.PageSize);
        Assert.Empty(Validate(defaults));

        var invalid = new CrmDetailQuery
        {
            ContactPage = 0,
            DealPage = 501,
            ActivityPage = 0,
            HistoryPage = 501,
            PageSize = 4
        };
        var errors = Validate(invalid);

        Assert.Contains(errors, result => AppliesTo(result, nameof(CrmDetailQuery.ContactPage)));
        Assert.Contains(errors, result => AppliesTo(result, nameof(CrmDetailQuery.DealPage)));
        Assert.Contains(errors, result => AppliesTo(result, nameof(CrmDetailQuery.ActivityPage)));
        Assert.Contains(errors, result => AppliesTo(result, nameof(CrmDetailQuery.HistoryPage)));
        Assert.Contains(errors, result => AppliesTo(result, nameof(CrmDetailQuery.PageSize)));
    }

    [Fact]
    public void EveryCrmRecordSummaryCarriesTheSameAuditContract()
    {
        var summaries = new object[]
        {
            new CrmCompanySummary(),
            new CrmContactSummary(),
            new CrmDealSummary(),
            new CrmActivitySummary()
        };

        foreach (var summary in summaries)
        {
            var auditProperty = summary.GetType().GetProperty("Audit");
            Assert.NotNull(auditProperty);
            Assert.Equal(typeof(CrmRecordAuditInfo), auditProperty!.PropertyType);
            Assert.NotNull(auditProperty.GetValue(summary));
        }

        var expectedProperties = new Dictionary<string, Type>
        {
            [nameof(CrmRecordAuditInfo.OwnerId)] = typeof(string),
            [nameof(CrmRecordAuditInfo.OwnerName)] = typeof(string),
            [nameof(CrmRecordAuditInfo.CreatedById)] = typeof(string),
            [nameof(CrmRecordAuditInfo.CreatedByName)] = typeof(string),
            [nameof(CrmRecordAuditInfo.ModifiedById)] = typeof(string),
            [nameof(CrmRecordAuditInfo.ModifiedByName)] = typeof(string),
            [nameof(CrmRecordAuditInfo.CreatedAtUtc)] = typeof(DateTimeOffset?),
            [nameof(CrmRecordAuditInfo.ModifiedAtUtc)] = typeof(DateTimeOffset?)
        };

        foreach (var (propertyName, propertyType) in expectedProperties)
        {
            var property = typeof(CrmRecordAuditInfo).GetProperty(propertyName);
            Assert.NotNull(property);
            Assert.Equal(propertyType, property!.PropertyType);
        }
    }

    [Fact]
    public void OwnerAndAuditFieldsCannotBeSuppliedByBrowserMutationDtos()
    {
        var mutationTypes = new[]
        {
            typeof(CrmCompanyCreateRequest),
            typeof(CrmContactCreateRequest),
            typeof(CrmActivityCreateRequest),
            typeof(CrmManualDealCreateRequest),
            typeof(CrmDealFromCalculatorRequest),
            typeof(CrmDealStageChangeRequest)
        };
        var protectedNames = new[]
        {
            "OwnerId",
            "OwnerName",
            "CreatedById",
            "CreatedByName",
            "ModifiedById",
            "ModifiedByName",
            "CreatedAtUtc",
            "ModifiedAtUtc"
        };

        foreach (var mutationType in mutationTypes)
        {
            foreach (var propertyName in protectedNames)
                Assert.Null(mutationType.GetProperty(propertyName));
        }
    }

    [Fact]
    public void DetailModelsExposeOnlyTheAssociationsThatBelongToTheirRecordType()
    {
        var company = new CrmCompanyDetailViewModel();
        Assert.NotNull(company.Company);
        Assert.Equal(12, company.Contacts.PageSize);
        Assert.Equal(12, company.Deals.PageSize);
        Assert.Equal(12, company.Activities.PageSize);

        var contact = new CrmContactDetailViewModel();
        Assert.NotNull(contact.Contact);
        Assert.Equal(12, contact.Deals.PageSize);
        Assert.Equal(12, contact.Activities.PageSize);

        var deal = new CrmDealDetailViewModel();
        Assert.NotNull(deal.Deal);
        Assert.Equal(12, deal.Activities.PageSize);
        Assert.Equal(12, deal.StageHistory.PageSize);

        var activity = new CrmActivityDetailViewModel();
        Assert.NotNull(activity.Activity);
        Assert.Equal(12, activity.RelatedActivities.PageSize);
    }

    [Fact]
    public void StageHistoryPreservesBothSidesOfTheTransitionAndItsEvidence()
    {
        var changedAt = DateTimeOffset.Parse("2026-07-24T16:30:00Z");
        var item = new CrmStageHistorySummary
        {
            Id = Guid.NewGuid().ToString(),
            DealId = Guid.NewGuid().ToString(),
            PreviousStageValue = (int)CrmDealStage.Proposal,
            PreviousStageLabel = CrmCatalog.DealStageLabel((int)CrmDealStage.Proposal),
            NewStageValue = (int)CrmDealStage.Negotiation,
            NewStageLabel = CrmCatalog.DealStageLabel((int)CrmDealStage.Negotiation),
            ChangedAtUtc = changedAt,
            DurationDays = 3.5m,
            Reason = "Cliente solicitó ajustes."
        };

        Assert.Equal((int)CrmDealStage.Proposal, item.PreviousStageValue);
        Assert.Equal("Propuesta", item.PreviousStageLabel);
        Assert.Equal((int)CrmDealStage.Negotiation, item.NewStageValue);
        Assert.Equal("Negociación", item.NewStageLabel);
        Assert.Equal(changedAt, item.ChangedAtUtc);
        Assert.Equal(3.5m, item.DurationDays);
        Assert.False(string.IsNullOrWhiteSpace(item.Reason));
    }

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

    private static IEnumerable<MethodInfo> CrmActions() =>
        typeof(CrmController).GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
}
