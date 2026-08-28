using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Crm;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Abstractions;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CrmContractsTests
{
    private const string DealScenarioField = "cr07a_escenarioorigen";
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string SolutionRoot =
        Path.Combine(RepositoryRoot, "solutions", "CotizadorInternoCRM");

    [Fact]
    public void EnumsKeepTheExactDataverseOptionValues()
    {
        Assert.Equal(
            [645250000, 645250001, 645250002, 645250003, 645250004, 645250005, 645250006],
            Enum.GetValues<CrmDealStage>().Select(value => (int)value));
        Assert.Equal(
            [645250000, 645250001, 645250002, 645250003, 645250004, 645250005],
            Enum.GetValues<CrmActivityType>().Select(value => (int)value));
        Assert.Equal(
            [645250000, 645250001, 645250002],
            Enum.GetValues<CrmActivityStatus>().Select(value => (int)value));
        Assert.Equal(
            [645250000, 645250001],
            Enum.GetValues<CrmMeetingType>().Select(value => (int)value));
        Assert.Equal(
            [645250000, 645250001, 645250002, 645250003, 645250004],
            Enum.GetValues<CrmContactLifecycle>().Select(value => (int)value));
        Assert.Equal(
            [645250000, 645250001],
            Enum.GetValues<CrmDealKind>().Select(value => (int)value));
        Assert.Equal(
            [645250000, 645250001, 645250002],
            Enum.GetValues<CrmCompanyLifecycle>().Select(value => (int)value));
        Assert.Equal(
            [(645250000, "Oportunidad estimada"), (645250001, "Negocio cotizado")],
            CrmCatalog.DealKinds.Select(item => (item.Value, item.Label)));
        Assert.Equal(
            [(645250000, "Lead"), (645250001, "Cliente activo"), (645250002, "Cliente inactivo")],
            CrmCatalog.CompanyLifecycles.Select(item => (item.Value, item.Label)));
        Assert.Equal(
            [(645250000, "Portafolio"), (645250001, "Seguimiento")],
            CrmCatalog.MeetingTypes.Select(item => (item.Value, item.Label)));
    }

    [Fact]
    public void DealSummaryUsesTheCorrectPipelineMetricAndWonGate()
    {
        var estimatedOpportunity = new CrmDealSummary
        {
            KindValue = (int)CrmDealKind.EstimatedOpportunity,
            EstimatedValue = 18_500_000m,
            ContractValue = 90_000_000m,
            ProvisioningRequested = true
        };
        var quotedWithoutProvisioning = new CrmDealSummary
        {
            KindValue = (int)CrmDealKind.QuotedBusiness,
            EstimatedValue = 18_500_000m,
            ContractValue = 90_000_000m
        };
        var quotedWithProvisioning = new CrmDealSummary
        {
            KindValue = (int)CrmDealKind.QuotedBusiness,
            Score = 91m,
            ContractValue = 90_000_000m,
            ProvisioningRequested = true,
            ProvisioningRequestedAtUtc = DateTimeOffset.Parse("2026-07-24T12:00:00Z"),
            ProvisioningRequestId = "provisioning-request-1"
        };

        Assert.Equal(18_500_000m, estimatedOpportunity.PipelineValue);
        Assert.False(estimatedOpportunity.CanMarkWon);
        Assert.Equal(90_000_000m, quotedWithoutProvisioning.PipelineValue);
        Assert.False(quotedWithoutProvisioning.CanMarkWon);
        Assert.True(quotedWithProvisioning.CanMarkWon);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void WonGateRequiresBothAuthoritativeDealMeters(
        bool hasScore,
        bool hasContractValue)
    {
        var deal = new CrmDealSummary
        {
            KindValue = (int)CrmDealKind.QuotedBusiness,
            Score = hasScore ? 85m : null,
            ContractValue = hasContractValue ? 36_000_000m : null,
            ProvisioningRequested = true,
            ProvisioningRequestedAtUtc = DateTimeOffset.Parse("2026-07-24T12:00:00Z"),
            ProvisioningRequestId = "provisioning-request-1"
        };

        Assert.False(deal.CanMarkWon);
    }

    [Fact]
    public void CompanySummaryDifferentiatesLeadsFromLinkedActiveCustomers()
    {
        var lead = new CrmCompanySummary();
        var activeCustomer = new CrmCompanySummary
        {
            OperationalClientId = Guid.NewGuid().ToString(),
            LifecycleValue = (int)CrmCompanyLifecycle.ActiveCustomer,
            LifecycleLabel = "Cliente activo"
        };

        Assert.Equal((int)CrmCompanyLifecycle.Lead, lead.LifecycleValue);
        Assert.Equal("Lead", lead.LifecycleLabel);
        Assert.False(lead.IsActiveCustomer);
        Assert.True(activeCustomer.IsActiveCustomer);
    }

    [Fact]
    public void CalculatorBrowserInputCannotSupplyAuthoritativeDealMetrics()
    {
        Assert.Null(typeof(CrmDealFromCalculatorRequest).GetProperty(nameof(CrmCalculatorDealUpsertCommand.Score)));
        Assert.Null(typeof(CrmDealFromCalculatorRequest).GetProperty(nameof(CrmCalculatorDealUpsertCommand.ContractValue)));

        var opportunity = ValidCalculatorCommand(CrmDealKind.EstimatedOpportunity);
        Assert.Empty(Validate(opportunity));

        var quoted = ValidCalculatorCommand(CrmDealKind.QuotedBusiness);
        var errors = Validate(quoted);
        Assert.Contains(errors, result => AppliesTo(result, nameof(CrmCalculatorDealUpsertCommand.Score)));
        Assert.Contains(errors, result => AppliesTo(result, nameof(CrmCalculatorDealUpsertCommand.ContractValue)));

        quoted.Score = 82.5m;
        quoted.ContractValue = 48_000_000m;
        Assert.Empty(Validate(quoted));
    }

    [Fact]
    public void ManualDealCreationUsesADedicatedEstimatedOpportunityContract()
    {
        var action = Assert.Single(
            CrmActions(),
            method => method.Name == nameof(CrmController.CreateDeal));
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.Contains(
            action.GetParameters(),
            parameter => parameter.ParameterType == typeof(CrmManualDealCreateRequest)
                && parameter.GetCustomAttribute<FromBodyAttribute>() is not null);

        var repositoryMethod = Assert.Single(
            typeof(ICrmRepository).GetMethods(),
            method => method.Name == nameof(ICrmRepository.CreateEstimatedDealAsync));
        Assert.Equal(typeof(Task<CrmDealSummary>), repositoryMethod.ReturnType);
        Assert.Equal(
            [
                typeof(CrmManualDealCreateRequest),
                typeof(CrmAccessScope),
                typeof(CancellationToken)
            ],
            repositoryMethod.GetParameters().Select(parameter => parameter.ParameterType));

        var request = new CrmManualDealCreateRequest
        {
            CompanyId = Guid.NewGuid().ToString("D"),
            PrimaryContactId = Guid.NewGuid().ToString("D"),
            Name = "Renovación Modern Work",
            EstimatedContractValue = 48_000_000m,
            EstimatedScore = 82.5m,
            Category = "Modern Work",
            BriefDescription = "Estimado comercial antes de construir la cotización."
        };
        Assert.Empty(Validate(request));
    }

    [Fact]
    public void CatalogsMatchTheExactExportedChoices()
    {
        Assert.Equal(
            ReadChoices("cr07a_crmnegocio", "cr07a_etapa"),
            CrmCatalog.DealStages.Select(item => (item.Value, item.Label)));
        Assert.Equal(
            ReadChoices("cr07a_crmactividad", "cr07a_tipo"),
            CrmCatalog.ActivityTypes.Select(item => (item.Value, item.Label)));
        Assert.Equal(
            ReadChoices("cr07a_crmactividad", "cr07a_estado"),
            CrmCatalog.ActivityStatuses.Select(item => (item.Value, item.Label)));
        Assert.Equal(
            ReadChoices("cr07a_crmactividad", "cr07a_tiporeunion"),
            CrmCatalog.MeetingTypes.Select(item => (item.Value, item.Label)));
        Assert.Equal(
            ReadChoices("cr07a_crmcontacto", "cr07a_etapaciclovida"),
            CrmCatalog.ContactLifecycles.Select(item => (item.Value, item.Label)));
        Assert.Equal(
            ReadChoices("cr07a_crmnegocio", "cr07a_etapa"),
            ReadChoices("cr07a_crmhistorialetapa", "cr07a_etapaanterior"));
        Assert.Equal(
            ReadChoices("cr07a_crmnegocio", "cr07a_etapa"),
            ReadChoices("cr07a_crmhistorialetapa", "cr07a_etapanueva"));

        Assert.Equal(
            [false, false, false, false, false, true, true],
            CrmCatalog.DealStages.Select(item => item.IsClosed));
    }

    [Fact]
    public void DefaultEntitySetNamesMatchTheExportedSolution()
    {
        var options = new CrmDataverseOptions();
        var expected = new[]
        {
            ("cr07a_crmcontacto", options.ContactTableSetName),
            ("cr07a_crmnegocio", options.DealTableSetName),
            ("cr07a_crmactividad", options.ActivityTableSetName),
            ("cr07a_crmhistorialetapa", options.StageHistoryTableSetName)
        };

        foreach (var (entity, configuredSetName) in expected)
            Assert.Equal(ReadEntitySetName(entity), configuredSetName);

        Assert.Equal("cr07a_crmempresas", options.CompanyTableSetName);
        Assert.Equal("cr07a_clientes", options.OperationalClientTableSetName);
        Assert.Equal("cr07a_crmempresaid", options.CompanyIdField);
        Assert.Equal("cr07a_nombre", options.CompanyNameField);
        Assert.Equal("cr07a_tiporelacion", options.CompanyLifecycleField);
        Assert.Equal("cr07a_nit", options.CompanyTaxIdField);
        Assert.Equal("cr07a_correo", options.CompanyEmailField);
        Assert.Equal("cr07a_telefono", options.CompanyPhoneField);
        Assert.Equal("cr07a_ciudad", options.CompanyCityField);
        Assert.Equal("cr07a_clienteid", options.OperationalClientIdField);
        Assert.Equal("cr07a_tiporeunion", options.ActivityMeetingTypeField);
    }

    [Fact]
    public void DefaultDealLifecycleFieldsMatchTheCalculatorContract()
    {
        var options = new CrmDataverseOptions();

        Assert.Equal("cr07a_tiporegistro", options.DealKindField);
        Assert.Equal(DealScenarioField, options.DealScenarioIdField);
        Assert.Equal("cr07a_puntaje", options.DealScoreField);
        Assert.Equal("cr07a_valorcontrato", options.DealContractValueField);
        Assert.Equal("cr07a_aprovisionamientosolicitado", options.DealProvisioningRequestedField);
        Assert.Equal(
            "cr07a_fechaaprovisionamientosolicitado",
            options.DealProvisioningRequestedAtField);
        Assert.Equal(
            "cr07a_solicitudaprovisionamiento",
            options.DealProvisioningRequestIdField);
    }

    [Fact]
    public void DefaultLookupAndNavigationNamesMatchTheExportedSolution()
    {
        var options = new CrmDataverseOptions();
        var expected = new[]
        {
            new LookupContract(
                "cr07a_crmnegocio",
                "cr07a_crmcontacto",
                options.DealPrimaryContactLookupLogicalName,
                options.DealPrimaryContactNavigationProperty),
            new LookupContract(
                "cr07a_crmactividad",
                "cr07a_crmcontacto",
                options.ActivityContactLookupLogicalName,
                options.ActivityContactNavigationProperty),
            new LookupContract(
                "cr07a_crmactividad",
                "cr07a_crmnegocio",
                options.ActivityDealLookupLogicalName,
                options.ActivityDealNavigationProperty),
            new LookupContract(
                "cr07a_crmhistorialetapa",
                "cr07a_crmnegocio",
                options.StageHistoryDealLookupLogicalName,
                options.StageHistoryDealNavigationProperty)
        };

        foreach (var contract in expected)
        {
            var exported = ReadLookup(contract.ReferencingEntity, contract.ReferencedEntity, contract.Attribute);

            Assert.Equal(contract.Attribute, exported.Attribute);
            Assert.Equal(contract.NavigationProperty, exported.NavigationProperty);
        }

        Assert.Equal("cr07a_clienteoperativo", options.CompanyOperationalClientLookupLogicalName);
        Assert.Equal("cr07a_ClienteOperativo", options.CompanyOperationalClientNavigationProperty);
        Assert.Equal("cr07a_empresacrm", options.ContactCompanyLookupLogicalName);
        Assert.Equal("cr07a_EmpresaCrm", options.ContactCompanyNavigationProperty);
        Assert.Equal("cr07a_empresacrm", options.DealCompanyLookupLogicalName);
        Assert.Equal("cr07a_EmpresaCrm", options.DealCompanyNavigationProperty);
        Assert.Equal("cr07a_empresacrm", options.ActivityCompanyLookupLogicalName);
        Assert.Equal("cr07a_EmpresaCrm", options.ActivityCompanyNavigationProperty);
    }

    [Fact]
    public void ControllerRequiresTheCrmModuleAndDoesNotAllowAnonymousAccess()
    {
        var authorization = Assert.Single(
            typeof(CrmController).GetCustomAttributes<ModuleAuthorizeAttribute>());
        Assert.Equal(
            AppModule.Crm,
            Assert.IsType<AppModule>(Assert.Single(authorization.Arguments!)));
        Assert.Empty(typeof(CrmController).GetCustomAttributes<AllowAnonymousAttribute>());

        foreach (var action in CrmActions())
            Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Fact]
    public void EveryCrmPostActionRequiresAntiforgeryValidation()
    {
        var postActions = CrmActions()
            .Where(action => action.GetCustomAttribute<HttpPostAttribute>() is not null)
            .ToArray();

        Assert.NotEmpty(postActions);
        Assert.Contains(postActions, action => action.Name == nameof(CrmController.CreateDeal));
        Assert.Contains(postActions, action => action.Name == nameof(CrmController.UpdateOwner));
        Assert.All(
            postActions,
            action => Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>()));
    }

    [Fact]
    public void ActivityCreationDrawersExposeAConditionalMeetingTypeSelector()
    {
        var index = ReadProjectFile("Views", "Crm", "Index.cshtml");
        var detailDrawers = ReadProjectFile("Views", "Crm", "_CrmDetailDrawers.cshtml");
        var workspaceScript = ReadProjectFile("wwwroot", "js", "crm.js");
        var detailScript = ReadProjectFile("wwwroot", "js", "crm-detail.js");

        Assert.Contains("data-meeting-activity-type", index, StringComparison.Ordinal);
        Assert.Contains("data-activity-type-select", index, StringComparison.Ordinal);
        Assert.Contains("data-activity-meeting-type-field", index, StringComparison.Ordinal);
        Assert.Contains("data-activity-meeting-type-select", index, StringComparison.Ordinal);
        Assert.Contains("name=\"MeetingType\"", index, StringComparison.Ordinal);
        Assert.Contains("MeetingTypes", index, StringComparison.Ordinal);

        Assert.Contains("data-detail-activity-type-select", detailDrawers, StringComparison.Ordinal);
        Assert.Contains("data-detail-activity-meeting-type-field", detailDrawers, StringComparison.Ordinal);
        Assert.Contains("data-detail-activity-meeting-type-select", detailDrawers, StringComparison.Ordinal);
        Assert.Contains("name=\"MeetingType\"", detailDrawers, StringComparison.Ordinal);
        Assert.Contains("MeetingTypes", detailDrawers, StringComparison.Ordinal);

        Assert.Contains("[data-activity-type-select]", workspaceScript, StringComparison.Ordinal);
        Assert.Contains("[data-activity-meeting-type-field]", workspaceScript, StringComparison.Ordinal);
        Assert.Contains("[data-activity-meeting-type-select]", workspaceScript, StringComparison.Ordinal);
        Assert.Contains("meetingActivityType", workspaceScript, StringComparison.Ordinal);
        Assert.Contains("activityMeetingTypeSelect.required", workspaceScript, StringComparison.Ordinal);
        Assert.Contains("activityMeetingTypeSelect.disabled", workspaceScript, StringComparison.Ordinal);

        Assert.Contains("[data-detail-activity-type-select]", detailScript, StringComparison.Ordinal);
        Assert.Contains("[data-detail-activity-meeting-type-field]", detailScript, StringComparison.Ordinal);
        Assert.Contains("[data-detail-activity-meeting-type-select]", detailScript, StringComparison.Ordinal);
        Assert.Contains("meetingActivityType", detailScript, StringComparison.Ordinal);
        Assert.Contains("activityMeetingTypeSelect.required", detailScript, StringComparison.Ordinal);
        Assert.Contains("activityMeetingTypeSelect.disabled", detailScript, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sruiz@digitaltechcolombia.com")]
    [InlineData("SRUIZ@DIGITALTECHCOLOMBIA.COM")]
    [InlineData("otra.persona@digitaltechcolombia.com")]
    [InlineData("")]
    public void EmailAloneNeverBypassesTheCrmModulePermission(string email)
    {
        var user = new CurrentUserInfo
        {
            Email = email,
            EmployeeUserEmail = email
        };

        Assert.False(AppModuleAccessPolicy.HasSpecificUserAccess(AppModule.Crm, user));
        Assert.False(AppModuleAccessPolicy.CanAccess(AppModule.Crm, user));
    }

    [Fact]
    public void LostStageRequiresAReasonWhileOtherStagesDoNot()
    {
        foreach (var reason in new[] { "", " ", "\r\n" })
        {
            var invalid = Validate(new CrmDealStageChangeRequest
            {
                DealId = Guid.NewGuid().ToString(),
                NewStage = CrmDealStage.Lost,
                Reason = reason
            });

            Assert.Contains(invalid, result => AppliesTo(result, nameof(CrmDealStageChangeRequest.Reason)));
        }

        Assert.Empty(Validate(new CrmDealStageChangeRequest
        {
            DealId = Guid.NewGuid().ToString(),
            NewStage = CrmDealStage.Lost,
            Reason = "El cliente eligió otra propuesta."
        }));
        Assert.Empty(Validate(new CrmDealStageChangeRequest
        {
            DealId = Guid.NewGuid().ToString(),
            NewStage = CrmDealStage.Won
        }));
    }

    [Fact]
    public void DealValueAndProbabilityAcceptOnlyTheirDocumentedBoundaries()
    {
        foreach (var value in new[] { 0m, 100_000_000_000m })
        {
            var results = Validate(SetDealValue(ValidCalculatorCommand(CrmDealKind.EstimatedOpportunity), value));
            Assert.DoesNotContain(
                results,
                result => AppliesTo(result, nameof(CrmCalculatorDealUpsertCommand.EstimatedValue)));
        }

        foreach (var value in new[] { -0.01m, 100_000_000_000.01m })
        {
            var results = Validate(SetDealValue(ValidCalculatorCommand(CrmDealKind.EstimatedOpportunity), value));
            Assert.Contains(
                results,
                result => AppliesTo(result, nameof(CrmCalculatorDealUpsertCommand.EstimatedValue)));
        }

        foreach (var probability in new[] { 0m, 100m })
        {
            var results = Validate(SetDealProbability(ValidCalculatorCommand(CrmDealKind.EstimatedOpportunity), probability));
            Assert.DoesNotContain(
                results,
                result => AppliesTo(result, nameof(CrmCalculatorDealUpsertCommand.Probability)));
        }

        foreach (var probability in new[] { -0.01m, 100.01m })
        {
            var results = Validate(SetDealProbability(ValidCalculatorCommand(CrmDealKind.EstimatedOpportunity), probability));
            Assert.Contains(
                results,
                result => AppliesTo(result, nameof(CrmCalculatorDealUpsertCommand.Probability)));
        }
    }

    [Fact]
    public void ActivityDurationAcceptsOneThroughOneDayInMinutes()
    {
        foreach (var duration in new[] { 1, 1440 })
        {
            var results = Validate(SetActivityDuration(ValidPlannedActivity(), duration));
            Assert.DoesNotContain(results, result => AppliesTo(result, nameof(CrmActivityCreateRequest.DurationMinutes)));
        }

        foreach (var duration in new[] { 0, 1441 })
        {
            var results = Validate(SetActivityDuration(ValidPlannedActivity(), duration));
            Assert.Contains(results, result => AppliesTo(result, nameof(CrmActivityCreateRequest.DurationMinutes)));
        }
    }

    [Fact]
    public void MeetingTypeIsRequiredOnlyForMeetingActivities()
    {
        var call = ValidPlannedActivity();
        Assert.DoesNotContain(
            Validate(call),
            result => AppliesTo(result, nameof(CrmActivityCreateRequest.MeetingType)));

        var meetingWithoutType = ValidPlannedActivity();
        meetingWithoutType.Type = CrmActivityType.Meeting;
        Assert.Contains(
            Validate(meetingWithoutType),
            result => AppliesTo(result, nameof(CrmActivityCreateRequest.MeetingType)));

        foreach (var meetingType in new[] { CrmMeetingType.Portfolio, CrmMeetingType.FollowUp })
        {
            var meeting = ValidPlannedActivity();
            meeting.Type = CrmActivityType.Meeting;
            meeting.MeetingType = meetingType;
            Assert.DoesNotContain(
                Validate(meeting),
                result => AppliesTo(result, nameof(CrmActivityCreateRequest.MeetingType)));
        }

        var invalidMeetingType = ValidPlannedActivity();
        invalidMeetingType.Type = CrmActivityType.Meeting;
        invalidMeetingType.MeetingType = (CrmMeetingType)645259999;
        Assert.Contains(
            Validate(invalidMeetingType),
            result => AppliesTo(result, nameof(CrmActivityCreateRequest.MeetingType)));

        var callWithMeetingType = ValidPlannedActivity();
        callWithMeetingType.MeetingType = CrmMeetingType.Portfolio;
        Assert.Contains(
            Validate(callWithMeetingType),
            result => AppliesTo(result, nameof(CrmActivityCreateRequest.MeetingType)));
    }

    [Fact]
    public void ContactAndActivityDtosEnforceTheirMinimumConsistentState()
    {
        var validContact = new CrmContactCreateRequest
        {
            CompanyId = Guid.NewGuid().ToString(),
            FirstName = "Laura",
            Email = "laura@example.com",
            Lifecycle = CrmContactLifecycle.Lead
        };
        Assert.Empty(Validate(validContact));

        var validPhoneOnlyContact = new CrmContactCreateRequest
        {
            CompanyId = Guid.NewGuid().ToString(),
            FirstName = "Laura",
            Email = null!,
            Phone = "3001234567",
            Lifecycle = CrmContactLifecycle.Lead
        };
        Assert.Empty(Validate(validPhoneOnlyContact));

        validContact.Email = null!;
        validContact.Phone = null;
        Assert.Contains(
            Validate(validContact),
            result => AppliesTo(result, nameof(CrmContactCreateRequest.Email))
                && AppliesTo(result, nameof(CrmContactCreateRequest.Phone)));

        var missingRelation = ValidPlannedActivity();
        missingRelation.CompanyId = "";
        Assert.Contains(
            Validate(missingRelation),
            result => AppliesTo(result, nameof(CrmActivityCreateRequest.CompanyId))
                && AppliesTo(result, nameof(CrmActivityCreateRequest.ContactId))
                && AppliesTo(result, nameof(CrmActivityCreateRequest.DealId)));

        var completedWithoutResult = ValidPlannedActivity();
        completedWithoutResult.Status = CrmActivityStatus.Completed;
        completedWithoutResult.PlannedAtUtc = null;
        completedWithoutResult.Result = " ";
        Assert.Contains(
            Validate(completedWithoutResult),
            result => AppliesTo(result, nameof(CrmActivityCreateRequest.Result)));
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public async Task ActivityRejectsCrossCompanyRelationsBeforeAnyWrite(
        bool includeCompany,
        bool includeContact,
        bool includeDeal)
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var dealId = Guid.NewGuid();
        var dealCompanyId = includeCompany ? companyB : companyA;
        var (api, recorder) = CreateRecordingApi(request =>
        {
            if (request.Method == HttpMethod.Get.Method
                && request.RelativePath.Contains(
                    $"/cr07a_crmcontactos({contactId:D})",
                    StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(
                    HttpStatusCode.OK,
                    $$"""{"_cr07a_empresacrm_value":"{{companyB:D}}"}""");
            }

            if (request.Method == HttpMethod.Get.Method
                && request.RelativePath.Contains(
                    $"/cr07a_crmnegocios({dealId:D})",
                    StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(
                    HttpStatusCode.OK,
                    DealJson(
                        dealId,
                        dealCompanyId,
                        CrmDealKind.EstimatedOpportunity,
                        "scenario-cross-company"));
            }

            return JsonResponse(HttpStatusCode.OK, """{"value":[]}""");
        });
        var repository = CreateRepository(api);
        var request = ValidPlannedActivity();
        request.CompanyId = includeCompany ? companyA.ToString() : "";
        request.ContactId = includeContact ? contactId.ToString() : "";
        request.DealId = includeDeal ? dealId.ToString() : "";

        await Assert.ThrowsAsync<CrmValidationException>(
            () => repository.CreateActivityAsync(request));

        Assert.DoesNotContain(
            recorder.Requests,
            item => item.Method == HttpMethod.Post.Method);
    }

    [Fact]
    public async Task DealDetailRejectsAPrimaryContactFromAnotherCompany()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var dealId = Guid.NewGuid();
        var deal = DealJson(
            dealId,
            companyA,
            CrmDealKind.EstimatedOpportunity,
            "scenario-detail-cross-company",
            primaryContactId: contactId);
        var (api, _) = CreateRecordingApi(request =>
        {
            if (request.Method == HttpMethod.Get.Method
                && request.RelativePath.Contains(
                    $"/cr07a_crmnegocios({dealId:D})",
                    StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(HttpStatusCode.OK, deal);
            }

            if (request.Method == HttpMethod.Get.Method
                && request.RelativePath.Contains(
                    $"/cr07a_crmcontactos({contactId:D})",
                    StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(
                    HttpStatusCode.OK,
                    $$"""
                    {
                      "cr07a_crmcontactoid": "{{contactId:D}}",
                      "cr07a_nombre": "Contacto cruzado",
                      "_cr07a_empresacrm_value": "{{companyB:D}}"
                    }
                    """);
            }

            return JsonResponse(HttpStatusCode.OK, """{"@odata.count":0,"value":[]}""");
        });
        var repository = CreateRepository(api);

        var error = await Assert.ThrowsAsync<CrmConflictException>(
            () => repository.GetDealDetailAsync(
                dealId.ToString(),
                new CrmDetailQuery()));

        Assert.Contains("contacto principal", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ActivityDetailRejectsRelationsFromDifferentCompanies()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var activityId = Guid.NewGuid();
        var (api, _) = CreateRecordingApi(request =>
        {
            if (request.Method == HttpMethod.Get.Method
                && request.RelativePath.Contains(
                    $"/cr07a_crmactividads({activityId:D})",
                    StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(
                    HttpStatusCode.OK,
                    $$"""
                    {
                      "cr07a_crmactividadid": "{{activityId:D}}",
                      "cr07a_asunto": "Actividad cruzada",
                      "cr07a_tipo": {{(int)CrmActivityType.Call}},
                      "cr07a_estado": {{(int)CrmActivityStatus.Planned}},
                      "_cr07a_empresacrm_value": "{{companyA:D}}",
                      "_cr07a_contacto_value": "{{contactId:D}}"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get.Method
                && request.RelativePath.Contains(
                    $"/cr07a_crmcontactos({contactId:D})",
                    StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(
                    HttpStatusCode.OK,
                    $$"""
                    {
                      "cr07a_crmcontactoid": "{{contactId:D}}",
                      "cr07a_nombre": "Contacto cruzado",
                      "_cr07a_empresacrm_value": "{{companyB:D}}"
                    }
                    """);
            }

            return JsonResponse(HttpStatusCode.OK, """{"@odata.count":0,"value":[]}""");
        });
        var repository = CreateRepository(api);

        var error = await Assert.ThrowsAsync<CrmConflictException>(
            () => repository.GetActivityDetailAsync(
                activityId.ToString(),
                new CrmDetailQuery()));

        Assert.Contains("no son coherentes", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContactDetailRejectsAnAssociatedDealFromAnotherCompany()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var dealId = Guid.NewGuid();
        var (api, _) = CreateRecordingApi(request =>
        {
            if (request.Method == HttpMethod.Get.Method
                && request.RelativePath.Contains(
                    $"/cr07a_crmcontactos({contactId:D})",
                    StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(
                    HttpStatusCode.OK,
                    $$"""
                    {
                      "cr07a_crmcontactoid": "{{contactId:D}}",
                      "cr07a_nombre": "Contacto raíz",
                      "_cr07a_empresacrm_value": "{{companyA:D}}"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get.Method
                && request.RelativePath.Contains("/cr07a_crmnegocios?", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(
                    HttpStatusCode.OK,
                    ODataCollection(DealJson(
                        dealId,
                        companyB,
                        CrmDealKind.EstimatedOpportunity,
                        "scenario-contact-detail-cross-company",
                        primaryContactId: contactId)));
            }

            return JsonResponse(HttpStatusCode.OK, """{"@odata.count":0,"value":[]}""");
        });
        var repository = CreateRepository(api);

        var error = await Assert.ThrowsAsync<CrmConflictException>(
            () => repository.GetContactDetailAsync(
                contactId.ToString(),
                new CrmDetailQuery()));

        Assert.Contains("lista de negocios", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DealDetailRejectsAnAssociatedActivityFromAnotherCompany()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var dealId = Guid.NewGuid();
        var activityId = Guid.NewGuid();
        var rootDeal = DealJson(
            dealId,
            companyA,
            CrmDealKind.QuotedBusiness,
            "scenario-deal-detail-root",
            score: 88m,
            contractValue: 42_000_000m);
        var (api, _) = CreateRecordingApi(request =>
        {
            if (request.Method == HttpMethod.Get.Method
                && request.RelativePath.Contains(
                    $"/cr07a_crmnegocios({dealId:D})",
                    StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(HttpStatusCode.OK, rootDeal);
            }

            if (request.Method == HttpMethod.Get.Method
                && request.RelativePath.Contains("/cr07a_crmactividads?", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(
                    HttpStatusCode.OK,
                    $$"""
                    {
                      "@odata.count": 1,
                      "value": [
                        {
                          "cr07a_crmactividadid": "{{activityId:D}}",
                          "cr07a_asunto": "Actividad de otra empresa",
                          "cr07a_tipo": {{(int)CrmActivityType.Call}},
                          "cr07a_estado": {{(int)CrmActivityStatus.Planned}},
                          "_cr07a_empresacrm_value": "{{companyB:D}}",
                          "_cr07a_negocio_value": "{{dealId:D}}"
                        }
                      ]
                    }
                    """);
            }

            return JsonResponse(HttpStatusCode.OK, """{"@odata.count":0,"value":[]}""");
        });
        var repository = CreateRepository(api);

        var error = await Assert.ThrowsAsync<CrmConflictException>(
            () => repository.GetDealDetailAsync(
                dealId.ToString(),
                new CrmDetailQuery()));

        Assert.Contains("lista de actividades", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("contactos")]
    [InlineData("negocios")]
    [InlineData("actividades")]
    public async Task CompanyDetailRejectsAnyAssociatedListItemFromAnotherCompany(
        string associatedList)
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var dealId = Guid.NewGuid();
        var activityId = Guid.NewGuid();
        var (api, _) = CreateRecordingApi(request =>
        {
            if (request.Method == HttpMethod.Get.Method
                && request.RelativePath.Contains("/cr07a_crmcontactos?", StringComparison.OrdinalIgnoreCase))
            {
                var body = associatedList == "contactos"
                    ? $$"""
                      {
                        "@odata.count": 1,
                        "value": [
                          {
                            "cr07a_crmcontactoid": "{{contactId:D}}",
                            "cr07a_nombre": "Contacto cruzado",
                            "_cr07a_empresacrm_value": "{{companyB:D}}"
                          }
                        ]
                      }
                      """
                    : """{"@odata.count":0,"value":[]}""";
                return JsonResponse(HttpStatusCode.OK, body);
            }

            if (request.Method == HttpMethod.Get.Method
                && request.RelativePath.Contains("/cr07a_crmnegocios?", StringComparison.OrdinalIgnoreCase))
            {
                var body = associatedList == "negocios"
                    ? $$"""{"@odata.count":1,"value":[{{DealJson(dealId, companyB, CrmDealKind.EstimatedOpportunity, "scenario-company-cross")}}]}"""
                    : """{"@odata.count":0,"value":[]}""";
                return JsonResponse(HttpStatusCode.OK, body);
            }

            if (request.Method == HttpMethod.Get.Method
                && request.RelativePath.Contains("/cr07a_crmactividads?", StringComparison.OrdinalIgnoreCase))
            {
                var body = associatedList == "actividades"
                    ? $$"""
                      {
                        "@odata.count": 1,
                        "value": [
                          {
                            "cr07a_crmactividadid": "{{activityId:D}}",
                            "cr07a_asunto": "Actividad cruzada",
                            "cr07a_tipo": {{(int)CrmActivityType.Call}},
                            "cr07a_estado": {{(int)CrmActivityStatus.Planned}},
                            "_cr07a_empresacrm_value": "{{companyB:D}}"
                          }
                        ]
                      }
                      """
                    : """{"@odata.count":0,"value":[]}""";
                return JsonResponse(HttpStatusCode.OK, body);
            }

            throw new InvalidOperationException($"Solicitud inesperada: {request.RelativePath}");
        });
        var repository = CreateRepository(api);

        var error = await Assert.ThrowsAsync<CrmConflictException>(
            () => repository.GetCompanyDetailAsync(
                companyA.ToString(),
                new CrmDetailQuery()));

        Assert.Contains($"lista de {associatedList}", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetailPaginationKeepsNextPageWhenDataverseReturnsANextLink()
    {
        var companyId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var (api, _) = CreateRecordingApi(request =>
        {
            if (request.Method == HttpMethod.Get.Method
                && request.RelativePath.Contains("/cr07a_crmcontactos?", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(
                    HttpStatusCode.OK,
                    $$"""
                    {
                      "@odata.count": 1,
                      "@odata.nextLink": "https://org.crm.dynamics.com/api/data/v9.2/cr07a_crmcontactos?$skiptoken=next",
                      "value": [
                        {
                          "cr07a_crmcontactoid": "{{contactId:D}}",
                          "cr07a_nombre": "Contacto paginado",
                          "_cr07a_empresacrm_value": "{{companyId:D}}"
                        }
                      ]
                    }
                    """);
            }

            return JsonResponse(HttpStatusCode.OK, """{"@odata.count":0,"value":[]}""");
        });
        var repository = CreateRepository(api);

        var detail = await repository.GetCompanyDetailAsync(
            companyId.ToString(),
            new CrmDetailQuery());

        Assert.Equal(1, detail.Contacts.TotalCount);
        Assert.True(detail.Contacts.HasMore);
        Assert.True(detail.Contacts.HasNext);
    }

    [Fact]
    public async Task ContactPayloadUsesTheVerifiedDefaultsWithoutCallingDataverse()
    {
        var companyId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var (api, recorder) = CreateRecordingApi(request =>
        {
            if (request.Method == HttpMethod.Post.Method)
            {
                return JsonResponse(
                    HttpStatusCode.Created,
                    $$"""{"cr07a_crmcontactoid":"{{contactId:D}}"}""");
            }

            return JsonResponse(
                HttpStatusCode.OK,
                $$"""
                {
                  "cr07a_crmcontactoid": "{{contactId:D}}",
                  "cr07a_nombre": "Laura",
                  "cr07a_correo": "laura@example.com",
                  "cr07a_etapaciclovida": 645250003,
                  "cr07a_esprincipal": false,
                  "cr07a_noenviarcorreo": false,
                  "cr07a_nollamar": false,
                  "_cr07a_empresacrm_value": "{{companyId:D}}"
                }
                """);
        });
        var repository = new DataverseCrmRepository(
            api,
            AuthenticatedHttpContextAccessor(),
            Options.Create(new CrmDataverseOptions()),
            NullLogger<DataverseCrmRepository>.Instance);
        var request = new CrmContactCreateRequest
        {
            CompanyId = companyId.ToString(),
            FirstName = "  Laura  ",
            Email = "  Laura@Example.com  "
        };

        var saved = await repository.CreateContactAsync(request);

        Assert.Equal(contactId.ToString(), saved.Id);
        var post = Assert.Single(recorder.Requests, item => item.Method == HttpMethod.Post.Method);
        Assert.Equal("/api/data/v9.2/cr07a_crmcontactos", post.RelativePath);
        using var payload = JsonDocument.Parse(post.Body);
        var root = payload.RootElement;
        Assert.Equal("Laura", root.GetProperty("cr07a_nombre").GetString());
        Assert.Equal("laura@example.com", root.GetProperty("cr07a_correo").GetString());
        Assert.Equal(645250003, root.GetProperty("cr07a_etapaciclovida").GetInt32());
        Assert.False(root.GetProperty("cr07a_esprincipal").GetBoolean());
        Assert.False(root.GetProperty("cr07a_noenviarcorreo").GetBoolean());
        Assert.False(root.GetProperty("cr07a_nollamar").GetBoolean());
        Assert.Equal(
            $"/cr07a_crmempresas({companyId:D})",
            root.GetProperty("cr07a_EmpresaCrm@odata.bind").GetString());
        Assert.False(root.TryGetProperty("cr07a_telefono", out _));
        Assert.False(root.TryGetProperty("cr07a_apellidos", out _));
        Assert.False(root.TryGetProperty("cr07a_cargo", out _));
    }

    [Fact]
    public async Task CompanyCreationAlwaysCreatesALeadOutsideTheOperationalClientTable()
    {
        var companyId = Guid.NewGuid();
        var (api, recorder) = CreateRecordingApi(
            request =>
            {
                if (request.Method == HttpMethod.Post.Method)
                {
                    return JsonResponse(
                        HttpStatusCode.Created,
                        $$"""{"cr07a_crmempresaid":"{{companyId:D}}"}""");
                }

                return JsonResponse(
                    HttpStatusCode.OK,
                    CompanyJson(companyId, CrmCompanyLifecycle.Lead, name: "Prospecto Andino"));
            },
            autoResolveCompanies: false);
        var repository = CreateRepository(api);

        var saved = await repository.CreateCompanyAsync(new CrmCompanyCreateRequest
        {
            Name = "  Prospecto Andino  ",
            Email = "  VENTAS@PROSPECTO.COM  ",
            City = "  Bogotá  "
        });

        Assert.Equal((int)CrmCompanyLifecycle.Lead, saved.LifecycleValue);
        Assert.False(saved.IsActiveCustomer);
        Assert.Null(typeof(CrmCompanyCreateRequest).GetProperty("Lifecycle"));
        var post = Assert.Single(recorder.Requests, item => item.Method == HttpMethod.Post.Method);
        Assert.Equal("/api/data/v9.2/cr07a_crmempresas", post.RelativePath);
        Assert.DoesNotContain(
            recorder.Requests,
            item => item.Method == HttpMethod.Post.Method
                && item.RelativePath.Contains("/cr07a_clientes", StringComparison.OrdinalIgnoreCase));
        using var payload = JsonDocument.Parse(post.Body);
        var root = payload.RootElement;
        Assert.Equal("Prospecto Andino", root.GetProperty("cr07a_nombre").GetString());
        Assert.Equal("ventas@prospecto.com", root.GetProperty("cr07a_correo").GetString());
        Assert.Equal((int)CrmCompanyLifecycle.Lead, root.GetProperty("cr07a_tiporelacion").GetInt32());
        Assert.False(root.TryGetProperty("cr07a_ClienteOperativo@odata.bind", out _));
    }

    [Theory]
    [InlineData(
        CrmCompanyLifecycle.Lead,
        CrmContactLifecycle.Customer,
        CrmContactLifecycle.Lead,
        false)]
    [InlineData(
        CrmCompanyLifecycle.ActiveCustomer,
        CrmContactLifecycle.Lead,
        CrmContactLifecycle.Customer,
        true)]
    public async Task ContactLifecycleIsDerivedFromItsCompany(
        CrmCompanyLifecycle companyLifecycle,
        CrmContactLifecycle requestedLifecycle,
        CrmContactLifecycle expectedLifecycle,
        bool hasOperationalClient)
    {
        var companyId = Guid.NewGuid();
        var operationalClientId = hasOperationalClient ? Guid.NewGuid() : (Guid?)null;
        var contactId = Guid.NewGuid();
        var (api, recorder) = CreateRecordingApi(
            request =>
            {
                if (request.Method == HttpMethod.Get.Method
                    && request.RelativePath.Contains("/cr07a_crmempresas(", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse(
                        HttpStatusCode.OK,
                        CompanyJson(companyId, companyLifecycle, operationalClientId));
                }

                if (request.Method == HttpMethod.Post.Method)
                {
                    return JsonResponse(
                        HttpStatusCode.Created,
                        $$"""{"cr07a_crmcontactoid":"{{contactId:D}}"}""");
                }

                return JsonResponse(
                    HttpStatusCode.OK,
                    $$"""
                    {
                      "cr07a_crmcontactoid": "{{contactId:D}}",
                      "cr07a_nombre": "Laura",
                      "cr07a_correo": "laura@example.com",
                      "cr07a_etapaciclovida": {{(int)expectedLifecycle}},
                      "_cr07a_empresacrm_value": "{{companyId:D}}"
                    }
                    """);
            },
            autoResolveCompanies: false);
        var repository = CreateRepository(api);

        var saved = await repository.CreateContactAsync(new CrmContactCreateRequest
        {
            CompanyId = companyId.ToString(),
            FirstName = "Laura",
            Email = "laura@example.com",
            Lifecycle = requestedLifecycle
        });

        Assert.Equal((int)expectedLifecycle, saved.LifecycleValue);
        var post = Assert.Single(recorder.Requests, item => item.Method == HttpMethod.Post.Method);
        using var payload = JsonDocument.Parse(post.Body);
        Assert.Equal(
            (int)expectedLifecycle,
            payload.RootElement.GetProperty("cr07a_etapaciclovida").GetInt32());
    }

    [Fact]
    public async Task CalculatorOperationalClientIdResolvesToTheUnifiedCrmCompany()
    {
        var operationalClientId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var dealId = Guid.NewGuid();
        const string scenarioId = "scenario-operational-client";
        var (api, recorder) = CreateRecordingApi(
            request =>
            {
                if (request.Method == HttpMethod.Get.Method
                    && request.RelativePath.Contains(
                        $"/cr07a_crmempresas({operationalClientId:D})",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                if (request.Method == HttpMethod.Get.Method
                    && request.RelativePath.Contains("/cr07a_crmempresas?", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse(
                        HttpStatusCode.OK,
                        ODataCollection(CompanyJson(
                            companyId,
                            CrmCompanyLifecycle.ActiveCustomer,
                            operationalClientId)));
                }

                if (request.Method == HttpMethod.Get.Method
                    && request.RelativePath.Contains("/cr07a_crmnegocios?", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse(HttpStatusCode.OK, """{"value":[]}""");
                }

                if (request.Method == HttpMethod.Post.Method)
                {
                    return JsonResponse(
                        HttpStatusCode.Created,
                        $$"""{"cr07a_crmnegocioid":"{{dealId:D}}"}""");
                }

                return JsonResponse(
                    HttpStatusCode.OK,
                    DealJson(
                        dealId,
                        companyId,
                        CrmDealKind.EstimatedOpportunity,
                        scenarioId));
            },
            autoResolveCompanies: false);
        var repository = CreateRepository(api);
        var command = ValidCalculatorCommand(CrmDealKind.EstimatedOpportunity);
        command.CompanyId = operationalClientId.ToString();
        command.ScenarioId = scenarioId;

        var saved = await repository.UpsertDealFromCalculatorAsync(command);

        Assert.Equal(companyId.ToString(), saved.CompanyId);
        var post = Assert.Single(recorder.Requests, item => item.Method == HttpMethod.Post.Method);
        using var payload = JsonDocument.Parse(post.Body);
        Assert.Equal(
            $"/cr07a_crmempresas({companyId:D})",
            payload.RootElement.GetProperty("cr07a_EmpresaCrm@odata.bind").GetString());
        Assert.DoesNotContain(
            recorder.Requests,
            item => item.Method == HttpMethod.Post.Method
                && item.RelativePath.Contains("/cr07a_clientes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ActivityDerivesAndWritesTheUnifiedCompanyFromItsContact()
    {
        var companyId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var activityId = Guid.NewGuid();
        var (api, recorder) = CreateRecordingApi(
            request =>
            {
                if (request.Method == HttpMethod.Get.Method
                    && request.RelativePath.Contains(
                        $"/cr07a_crmcontactos({contactId:D})",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse(
                        HttpStatusCode.OK,
                        $$"""{"_cr07a_empresacrm_value":"{{companyId:D}}"}""");
                }

                if (request.Method == HttpMethod.Post.Method)
                {
                    return JsonResponse(
                        HttpStatusCode.Created,
                        $$"""{"cr07a_crmactividadid":"{{activityId:D}}"}""");
                }

                return JsonResponse(
                    HttpStatusCode.OK,
                    $$"""
                    {
                      "cr07a_crmactividadid": "{{activityId:D}}",
                      "cr07a_asunto": "Seguimiento",
                      "cr07a_tipo": {{(int)CrmActivityType.Call}},
                      "cr07a_estado": {{(int)CrmActivityStatus.Planned}},
                      "_cr07a_empresacrm_value": "{{companyId:D}}",
                      "_cr07a_contacto_value": "{{contactId:D}}"
                    }
                    """);
            },
            autoResolveCompanies: false);
        var repository = CreateRepository(api);

        var saved = await repository.CreateActivityAsync(new CrmActivityCreateRequest
        {
            Subject = "Seguimiento",
            Type = CrmActivityType.Call,
            Status = CrmActivityStatus.Planned,
            PlannedAtUtc = DateTimeOffset.UtcNow.AddDays(1),
            ContactId = contactId.ToString()
        });

        Assert.Equal(companyId.ToString(), saved.CompanyId);
        var post = Assert.Single(recorder.Requests, item => item.Method == HttpMethod.Post.Method);
        using var payload = JsonDocument.Parse(post.Body);
        Assert.Equal(
            $"/cr07a_crmempresas({companyId:D})",
            payload.RootElement.GetProperty("cr07a_EmpresaCrm@odata.bind").GetString());
        Assert.Equal(
            $"/cr07a_crmcontactos({contactId:D})",
            payload.RootElement.GetProperty("cr07a_contacto@odata.bind").GetString());
    }

    [Fact]
    public async Task MeetingTypeIsWrittenSelectedAndMappedFromDataverse()
    {
        var companyId = Guid.NewGuid();
        var activityId = Guid.NewGuid();
        var (api, recorder) = CreateRecordingApi(request =>
        {
            if (request.Method == HttpMethod.Post.Method)
            {
                return JsonResponse(
                    HttpStatusCode.Created,
                    $$"""{"cr07a_crmactividadid":"{{activityId:D}}"}""");
            }

            if (request.Method == HttpMethod.Get.Method
                && request.RelativePath.Contains(
                    $"/cr07a_crmactividads({activityId:D})",
                    StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(
                    HttpStatusCode.OK,
                    $$"""
                    {
                      "cr07a_crmactividadid": "{{activityId:D}}",
                      "cr07a_asunto": "Revisión trimestral",
                      "cr07a_tipo": {{(int)CrmActivityType.Meeting}},
                      "cr07a_tiporeunion": {{(int)CrmMeetingType.FollowUp}},
                      "cr07a_estado": {{(int)CrmActivityStatus.Planned}},
                      "_cr07a_empresacrm_value": "{{companyId:D}}"
                    }
                    """);
            }

            return JsonResponse(HttpStatusCode.OK, """{"value":[]}""");
        });
        var repository = CreateRepository(api);

        var request = ValidPlannedActivity();
        request.Subject = "Revisión trimestral";
        request.Type = CrmActivityType.Meeting;
        request.MeetingType = CrmMeetingType.FollowUp;
        request.CompanyId = companyId.ToString();

        var saved = await repository.CreateActivityAsync(request);

        var post = Assert.Single(recorder.Requests, item => item.Method == HttpMethod.Post.Method);
        using var payload = JsonDocument.Parse(post.Body);
        Assert.Equal(
            (int)CrmMeetingType.FollowUp,
            payload.RootElement.GetProperty("cr07a_tiporeunion").GetInt32());

        var readBack = Assert.Single(
            recorder.Requests,
            item => item.Method == HttpMethod.Get.Method
                && item.RelativePath.Contains(
                    $"/cr07a_crmactividads({activityId:D})",
                    StringComparison.OrdinalIgnoreCase));
        Assert.Contains("cr07a_tiporeunion", readBack.RelativePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((int)CrmMeetingType.FollowUp, saved.MeetingTypeValue);
        Assert.Equal("Seguimiento", saved.MeetingTypeLabel);
        Assert.Equal("Reunión · Seguimiento", saved.TypeDisplayLabel);
    }

    [Fact]
    public async Task CalculatorCreatesOneQuotedBusinessWithAuthoritativeMetrics()
    {
        var companyId = Guid.NewGuid();
        var dealId = Guid.NewGuid();
        const string scenarioId = "scenario-quoted-1";
        var (api, recorder) = CreateRecordingApi(request =>
        {
            if (request.Method == HttpMethod.Get.Method && request.RelativePath.Contains("$top=2"))
                return JsonResponse(HttpStatusCode.OK, """{"value":[]}""");
            if (request.Method == HttpMethod.Post.Method)
            {
                return JsonResponse(
                    HttpStatusCode.Created,
                    $$"""{"cr07a_crmnegocioid":"{{dealId:D}}"}""");
            }

            return JsonResponse(
                HttpStatusCode.OK,
                DealJson(
                    dealId,
                    companyId,
                    CrmDealKind.QuotedBusiness,
                    scenarioId,
                    score: 91.25m,
                    contractValue: 74_500_000m));
        });
        var repository = CreateRepository(api);
        var command = ValidCalculatorCommand(CrmDealKind.QuotedBusiness);
        command.CompanyId = companyId.ToString();
        command.ScenarioId = scenarioId;
        command.Score = 91.25m;
        command.ContractValue = 74_500_000m;
        command.ApplyCommercialFields = true;

        var saved = await repository.UpsertDealFromCalculatorAsync(command);

        Assert.Equal(dealId.ToString(), saved.Id);
        Assert.Equal(91.25m, saved.Score);
        Assert.Equal(74_500_000m, saved.PipelineValue);
        Assert.False(saved.CanMarkWon);
        var post = Assert.Single(recorder.Requests, item => item.Method == HttpMethod.Post.Method);
        using var payload = JsonDocument.Parse(post.Body);
        var root = payload.RootElement;
        Assert.Equal((int)CrmDealKind.QuotedBusiness, root.GetProperty("cr07a_tiporegistro").GetInt32());
        Assert.Equal(scenarioId, root.GetProperty(DealScenarioField).GetString());
        Assert.Equal(91.25m, root.GetProperty("cr07a_puntaje").GetDecimal());
        Assert.Equal(74_500_000m, root.GetProperty("cr07a_valorcontrato").GetDecimal());
        Assert.False(root.GetProperty("cr07a_aprovisionamientosolicitado").GetBoolean());
    }

    [Fact]
    public async Task InteractiveUpsertConvertsOpportunityWithoutDuplicatingAndUpdatesCommercialFields()
    {
        var companyId = Guid.NewGuid();
        var dealId = Guid.NewGuid();
        const string scenarioId = "scenario-convert-1";
        var current = DealJson(
            dealId,
            companyId,
            CrmDealKind.EstimatedOpportunity,
            scenarioId,
            name: "Estimado inicial");
        var updated = DealJson(
            dealId,
            companyId,
            CrmDealKind.QuotedBusiness,
            scenarioId,
            score: 77m,
            contractValue: 50_000_000m,
            name: "Cotización aprobada");
        var (api, recorder) = CreateRecordingApi(request =>
        {
            if (request.Method == HttpMethod.Get.Method && request.RelativePath.Contains("$top=2"))
                return JsonResponse(HttpStatusCode.OK, ODataCollection(current));
            if (request.Method == HttpMethod.Patch.Method)
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            return JsonResponse(HttpStatusCode.OK, updated);
        });
        var repository = CreateRepository(api);
        var command = ValidCalculatorCommand(CrmDealKind.QuotedBusiness);
        command.CompanyId = companyId.ToString();
        command.ScenarioId = scenarioId;
        command.Name = "  Cotización aprobada  ";
        command.EstimatedValue = 12_000_000m;
        command.Probability = 65m;
        command.ExpectedCloseDate = new DateOnly(2026, 9, 30);
        command.NextAction = "  Confirmar orden  ";
        command.NextActionAtUtc = new DateTimeOffset(2026, 8, 1, 14, 30, 0, TimeSpan.Zero);
        command.BusinessLine = "  Cloud  ";
        command.Score = 77m;
        command.ContractValue = 50_000_000m;
        command.ApplyCommercialFields = true;

        var saved = await repository.UpsertDealFromCalculatorAsync(command);

        Assert.Equal(dealId.ToString(), saved.Id);
        Assert.DoesNotContain(
            recorder.Requests,
            item => item.Method == HttpMethod.Post.Method);
        var patch = Assert.Single(recorder.Requests, item => item.Method == HttpMethod.Patch.Method);
        using var payload = JsonDocument.Parse(patch.Body);
        var root = payload.RootElement;
        Assert.Equal("Cotización aprobada", root.GetProperty("cr07a_nombre").GetString());
        Assert.Equal(12_000_000m, root.GetProperty("cr07a_valorestimado").GetDecimal());
        Assert.Equal(65m, root.GetProperty("cr07a_probabilidad").GetDecimal());
        Assert.Equal("2026-09-30T00:00:00Z", root.GetProperty("cr07a_fechacierreestimada").GetString());
        Assert.Equal("Confirmar orden", root.GetProperty("cr07a_proximaaccion").GetString());
        Assert.Equal("Cloud", root.GetProperty("cr07a_lineadenegocio").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("cr07a_contactoprincipal@odata.bind").ValueKind);
        Assert.False(root.TryGetProperty("cr07a_etapa", out _));
    }

    [Fact]
    public async Task ProvisioningUpsertPreservesCommercialFieldsAndInvalidatesChangedMetrics()
    {
        var companyId = Guid.NewGuid();
        var dealId = Guid.NewGuid();
        const string scenarioId = "scenario-reprice-1";
        var current = DealJson(
            dealId,
            companyId,
            CrmDealKind.QuotedBusiness,
            scenarioId,
            score: 70m,
            contractValue: 40_000_000m,
            provisioningRequested: true,
            provisioningRequestId: "request-old",
            name: "Nombre comercial");
        var updated = DealJson(
            dealId,
            companyId,
            CrmDealKind.QuotedBusiness,
            scenarioId,
            score: 82m,
            contractValue: 44_000_000m,
            name: "Nombre comercial");
        var (api, recorder) = CreateRecordingApi(request =>
        {
            if (request.Method == HttpMethod.Get.Method && request.RelativePath.Contains("$top=2"))
                return JsonResponse(HttpStatusCode.OK, ODataCollection(current));
            if (request.Method == HttpMethod.Patch.Method)
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            return JsonResponse(HttpStatusCode.OK, updated);
        });
        var repository = CreateRepository(api);
        var command = ValidCalculatorCommand(CrmDealKind.QuotedBusiness);
        command.CompanyId = companyId.ToString();
        command.ScenarioId = scenarioId;
        command.Name = "Nombre técnico que no debe reemplazar";
        command.EstimatedValue = 0m;
        command.Probability = 0m;
        command.Score = 82m;
        command.ContractValue = 44_000_000m;
        command.ApplyCommercialFields = false;

        var saved = await repository.UpsertDealFromCalculatorAsync(command);

        Assert.False(saved.CanMarkWon);
        var patch = Assert.Single(recorder.Requests, item => item.Method == HttpMethod.Patch.Method);
        using var payload = JsonDocument.Parse(patch.Body);
        var root = payload.RootElement;
        Assert.Equal(82m, root.GetProperty("cr07a_puntaje").GetDecimal());
        Assert.Equal(44_000_000m, root.GetProperty("cr07a_valorcontrato").GetDecimal());
        Assert.False(root.GetProperty("cr07a_aprovisionamientosolicitado").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("cr07a_fechaaprovisionamientosolicitado").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("cr07a_solicitudaprovisionamiento").ValueKind);
        Assert.False(root.TryGetProperty("cr07a_nombre", out _));
        Assert.False(root.TryGetProperty("cr07a_valorestimado", out _));
        Assert.False(root.TryGetProperty("cr07a_probabilidad", out _));
        Assert.False(root.TryGetProperty("cr07a_fechacierreestimada", out _));
        Assert.False(root.TryGetProperty("cr07a_contactoprincipal@odata.bind", out _));
    }

    [Fact]
    public async Task RepricingAWonDealAtomicallyReopensItAndRecordsHistory()
    {
        var companyId = Guid.NewGuid();
        var dealId = Guid.NewGuid();
        const string scenarioId = "scenario-won-reprice";
        var current = DealJson(
            dealId,
            companyId,
            CrmDealKind.QuotedBusiness,
            scenarioId,
            score: 70m,
            contractValue: 40_000_000m,
            provisioningRequested: true,
            provisioningRequestId: "request-old",
            name: "Contrato ganado",
            stage: CrmDealStage.Won);
        var reopened = DealJson(
            dealId,
            companyId,
            CrmDealKind.QuotedBusiness,
            scenarioId,
            score: 82m,
            contractValue: 44_000_000m,
            name: "Contrato ganado",
            stage: CrmDealStage.Negotiation);
        var (api, recorder) = CreateRecordingApi(request =>
        {
            if (request.Method == HttpMethod.Get.Method
                && request.RelativePath.Contains("/cr07a_crmnegocios?", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(HttpStatusCode.OK, ODataCollection(current));
            }

            if (request.Method == HttpMethod.Get.Method
                && request.RelativePath.Contains("/cr07a_crmhistorialetapas?", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(HttpStatusCode.OK, """{"value":[]}""");
            }

            if (request.Method == HttpMethod.Post.Method
                && request.RelativePath.EndsWith("/$batch", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("", Encoding.UTF8, "multipart/mixed")
                };
            }

            return JsonResponse(HttpStatusCode.OK, reopened);
        });
        var repository = CreateRepository(api);
        var command = ValidCalculatorCommand(CrmDealKind.QuotedBusiness);
        command.CompanyId = companyId.ToString();
        command.ScenarioId = scenarioId;
        command.Score = 82m;
        command.ContractValue = 44_000_000m;
        command.ApplyCommercialFields = false;

        var saved = await repository.UpsertDealFromCalculatorAsync(command);

        Assert.Equal((int)CrmDealStage.Negotiation, saved.StageValue);
        Assert.False(saved.CanMarkWon);
        Assert.DoesNotContain(recorder.Requests, request => request.Method == HttpMethod.Patch.Method);
        var batch = Assert.Single(
            recorder.Requests,
            request => request.Method == HttpMethod.Post.Method
                && request.RelativePath.EndsWith("/$batch", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            $"\"cr07a_etapa\":{(int)CrmDealStage.Negotiation}",
            batch.Body,
            StringComparison.Ordinal);
        Assert.Contains(
            $"\"cr07a_etapaanterior\":{(int)CrmDealStage.Won}",
            batch.Body,
            StringComparison.Ordinal);
        Assert.Contains(
            $"\"cr07a_etapanueva\":{(int)CrmDealStage.Negotiation}",
            batch.Body,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"cr07a_aprovisionamientosolicitado\":false",
            batch.Body,
            StringComparison.Ordinal);
        Assert.Contains("\"cr07a_fechacierreal\":null", batch.Body, StringComparison.Ordinal);
        Assert.Contains("\"cr07a_motivoperdida\":null", batch.Body, StringComparison.Ordinal);
        Assert.Contains("Reapertura autom", batch.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisioningUpsertPreservesEvidenceWhenAuthoritativeMetricsAreUnchanged()
    {
        var companyId = Guid.NewGuid();
        var dealId = Guid.NewGuid();
        const string scenarioId = "scenario-stable-1";
        var current = DealJson(
            dealId,
            companyId,
            CrmDealKind.QuotedBusiness,
            scenarioId,
            score: 88m,
            contractValue: 52_000_000m,
            provisioningRequested: true,
            provisioningRequestId: "request-stable");
        var (api, recorder) = CreateRecordingApi(request =>
        {
            if (request.Method == HttpMethod.Get.Method && request.RelativePath.Contains("$top=2"))
                return JsonResponse(HttpStatusCode.OK, ODataCollection(current));
            if (request.Method == HttpMethod.Patch.Method)
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            return JsonResponse(HttpStatusCode.OK, current);
        });
        var repository = CreateRepository(api);
        var command = ValidCalculatorCommand(CrmDealKind.QuotedBusiness);
        command.CompanyId = companyId.ToString();
        command.ScenarioId = scenarioId;
        command.Score = 88m;
        command.ContractValue = 52_000_000m;

        var saved = await repository.UpsertDealFromCalculatorAsync(command);

        Assert.True(saved.CanMarkWon);
        var patch = Assert.Single(recorder.Requests, item => item.Method == HttpMethod.Patch.Method);
        using var payload = JsonDocument.Parse(patch.Body);
        var root = payload.RootElement;
        Assert.False(root.TryGetProperty("cr07a_aprovisionamientosolicitado", out _));
        Assert.False(root.TryGetProperty("cr07a_fechaaprovisionamientosolicitado", out _));
        Assert.False(root.TryGetProperty("cr07a_solicitudaprovisionamiento", out _));
    }

    [Fact]
    public async Task WonStageRejectsEstimatedOrUnprovisionedDealsBeforeAnyWrite()
    {
        foreach (var (kind, provisioningRequested) in new[]
                 {
                     (CrmDealKind.EstimatedOpportunity, true),
                     (CrmDealKind.QuotedBusiness, false)
                 })
        {
            var companyId = Guid.NewGuid();
            var dealId = Guid.NewGuid();
            var (api, recorder) = CreateRecordingApi(_ =>
                JsonResponse(
                    HttpStatusCode.OK,
                    DealJson(
                        dealId,
                        companyId,
                        kind,
                        "scenario-gate",
                        score: kind == CrmDealKind.QuotedBusiness ? 80m : null,
                        contractValue: kind == CrmDealKind.QuotedBusiness ? 25_000_000m : null,
                        provisioningRequested: provisioningRequested)));
            var repository = CreateRepository(api);

            await Assert.ThrowsAsync<CrmConflictException>(() =>
                repository.ChangeDealStageAsync(new CrmDealStageChangeRequest
                {
                    DealId = dealId.ToString(),
                    NewStage = CrmDealStage.Won
                }));

            Assert.Single(recorder.Requests);
            Assert.DoesNotContain(
                recorder.Requests,
                request => request.Method is "POST" or "PATCH");
        }
    }

    [Fact]
    public async Task ProvisioningEvidenceIsStoredOnlyOnTheQuotedScenarioDeal()
    {
        var companyId = Guid.NewGuid();
        var dealId = Guid.NewGuid();
        const string scenarioId = "scenario-provisioning-1";
        const string requestId = "request-provisioning-1";
        var requestedAt = new DateTimeOffset(2026, 7, 24, 16, 30, 0, TimeSpan.Zero);
        var current = DealJson(
            dealId,
            companyId,
            CrmDealKind.QuotedBusiness,
            scenarioId,
            score: 93m,
            contractValue: 61_000_000m);
        var updated = DealJson(
            dealId,
            companyId,
            CrmDealKind.QuotedBusiness,
            scenarioId,
            score: 93m,
            contractValue: 61_000_000m,
            provisioningRequested: true,
            provisioningRequestId: requestId,
            provisioningRequestedAt: requestedAt);
        var (api, recorder) = CreateRecordingApi(request =>
        {
            if (request.Method == HttpMethod.Get.Method && request.RelativePath.Contains("$top=2"))
                return JsonResponse(HttpStatusCode.OK, ODataCollection(current));
            if (request.Method == HttpMethod.Patch.Method)
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            return JsonResponse(HttpStatusCode.OK, updated);
        });
        var repository = CreateRepository(api);

        var saved = await repository.MarkProvisioningRequestedAsync(
            scenarioId,
            requestId,
            requestedAt);

        Assert.NotNull(saved);
        Assert.True(saved.CanMarkWon);
        var patch = Assert.Single(recorder.Requests, item => item.Method == HttpMethod.Patch.Method);
        using var payload = JsonDocument.Parse(patch.Body);
        var root = payload.RootElement;
        Assert.True(root.GetProperty("cr07a_aprovisionamientosolicitado").GetBoolean());
        Assert.Equal(requestId, root.GetProperty("cr07a_solicitudaprovisionamiento").GetString());
        Assert.Equal(
            requestedAt,
            root.GetProperty("cr07a_fechaaprovisionamientosolicitado").GetDateTimeOffset());
    }

    private static IEnumerable<MethodInfo> CrmActions() =>
        typeof(CrmController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

    private static CrmCalculatorDealUpsertCommand ValidCalculatorCommand(CrmDealKind kind) => new()
    {
        ScenarioId = $"scenario-{Guid.NewGuid():N}",
        CompanyId = Guid.NewGuid().ToString(),
        Name = "Renovación desde calculadora",
        Kind = kind,
        EstimatedValue = kind == CrmDealKind.EstimatedOpportunity ? 15_000_000m : 0m,
        Probability = 40m,
        ApplyCommercialFields = true
    };

    private static DataverseCrmRepository CreateRepository(IDownstreamApi api) =>
        new(
            api,
            AuthenticatedHttpContextAccessor(),
            Options.Create(new CrmDataverseOptions()),
            NullLogger<DataverseCrmRepository>.Instance);

    private static string ODataCollection(string rowJson) => $$"""{"value":[{{rowJson}}]}""";

    private static string CompanyJson(
        Guid companyId,
        CrmCompanyLifecycle lifecycle,
        Guid? operationalClientId = null,
        string name = "Empresa CRM")
    {
        var row = new Dictionary<string, object?>
        {
            ["cr07a_crmempresaid"] = companyId.ToString("D"),
            ["cr07a_nombre"] = name,
            ["cr07a_tiporelacion"] = (int)lifecycle
        };
        if (operationalClientId.HasValue)
        {
            row["_cr07a_clienteoperativo_value"] =
                operationalClientId.Value.ToString("D");
        }

        return JsonSerializer.Serialize(row);
    }

    private static string DealJson(
        Guid dealId,
        Guid companyId,
        CrmDealKind? kind,
        string scenarioId,
        decimal? score = null,
        decimal? contractValue = null,
        bool provisioningRequested = false,
        string provisioningRequestId = "",
        DateTimeOffset? provisioningRequestedAt = null,
        string name = "Negocio CRM",
        CrmDealStage stage = CrmDealStage.Prospecting,
        Guid? primaryContactId = null)
    {
        var row = new Dictionary<string, object?>
        {
            ["@odata.etag"] = "W/\"1\"",
            ["cr07a_crmnegocioid"] = dealId.ToString("D"),
            ["cr07a_nombre"] = name,
            [DealScenarioField] = scenarioId,
            ["cr07a_etapa"] = (int)stage,
            ["cr07a_valorestimado"] = 15_000_000m,
            ["cr07a_probabilidad"] = 40m,
            ["cr07a_aprovisionamientosolicitado"] = provisioningRequested,
            ["_cr07a_empresacrm_value"] = companyId.ToString("D")
        };
        if (kind.HasValue)
            row["cr07a_tiporegistro"] = (int)kind.Value;
        if (score.HasValue)
            row["cr07a_puntaje"] = score.Value;
        if (contractValue.HasValue)
            row["cr07a_valorcontrato"] = contractValue.Value;
        if (primaryContactId.HasValue)
            row["_cr07a_contactoprincipal_value"] = primaryContactId.Value.ToString("D");
        if (!string.IsNullOrWhiteSpace(provisioningRequestId))
            row["cr07a_solicitudaprovisionamiento"] = provisioningRequestId;
        if (provisioningRequestedAt.HasValue)
            row["cr07a_fechaaprovisionamientosolicitado"] = provisioningRequestedAt.Value;
        else if (provisioningRequested)
            row["cr07a_fechaaprovisionamientosolicitado"] =
                new DateTimeOffset(2026, 7, 24, 15, 0, 0, TimeSpan.Zero);

        return JsonSerializer.Serialize(row);
    }

    private static CrmCalculatorDealUpsertCommand SetDealValue(
        CrmCalculatorDealUpsertCommand request,
        decimal estimatedValue)
    {
        request.EstimatedValue = estimatedValue;
        return request;
    }

    private static CrmCalculatorDealUpsertCommand SetDealProbability(
        CrmCalculatorDealUpsertCommand request,
        decimal probability)
    {
        request.Probability = probability;
        return request;
    }

    private static CrmActivityCreateRequest ValidPlannedActivity() => new()
    {
        Subject = "Llamada de seguimiento",
        Type = CrmActivityType.Call,
        Status = CrmActivityStatus.Planned,
        PlannedAtUtc = DateTimeOffset.UtcNow.AddDays(1),
        CompanyId = Guid.NewGuid().ToString(),
        DurationMinutes = 30
    };

    private static CrmActivityCreateRequest SetActivityDuration(
        CrmActivityCreateRequest request,
        int durationMinutes)
    {
        request.DurationMinutes = durationMinutes;
        return request;
    }

    private static IReadOnlyList<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
        return results;
    }

    private static bool AppliesTo(ValidationResult result, string propertyName) =>
        result.MemberNames.Contains(propertyName, StringComparer.Ordinal);

    private static (IDownstreamApi Api, RecordingDownstreamApi Recorder) CreateRecordingApi(
        Func<RecordedRequest, HttpResponseMessage> responder,
        bool autoResolveCompanies = true)
    {
        var api = DispatchProxy.Create<IDownstreamApi, RecordingDownstreamApi>();
        var recorder = (RecordingDownstreamApi)api;
        recorder.Responder = request =>
        {
            const string companyMarker = "/cr07a_crmempresas(";
            if (autoResolveCompanies
                && request.Method == HttpMethod.Get.Method)
            {
                var markerIndex = request.RelativePath.IndexOf(
                    companyMarker,
                    StringComparison.OrdinalIgnoreCase);
                var idStart = markerIndex < 0
                    ? -1
                    : markerIndex + companyMarker.Length;
                var idEnd = idStart < 0
                    ? -1
                    : request.RelativePath.IndexOf(')', idStart);
                if (idStart >= 0
                    && idEnd > idStart
                    && Guid.TryParse(
                        request.RelativePath[idStart..idEnd],
                        out var companyId))
                {
                    return JsonResponse(
                        HttpStatusCode.OK,
                        CompanyJson(
                            companyId,
                            CrmCompanyLifecycle.ActiveCustomer,
                            companyId));
                }
            }

            return responder(request);
        };
        return (api, recorder);
    }

    private static IHttpContextAccessor AuthenticatedHttpContextAccessor()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
            authenticationType: "UnitTest");
        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string ReadEntitySetName(string entityName)
    {
        var document = ReadEntity(entityName);
        return document.Descendants("EntitySetName").Single().Value;
    }

    private static IReadOnlyList<(int Value, string Label)> ReadChoices(
        string entityName,
        string attributeName)
    {
        var attribute = ReadEntity(entityName)
            .Descendants("attribute")
            .Single(element => string.Equals(
                (string?)element.Attribute("PhysicalName"),
                attributeName,
                StringComparison.OrdinalIgnoreCase));

        return attribute
            .Descendants("option")
            .Select(option =>
            {
                var value = int.Parse((string)option.Attribute("value")!);
                var label = option
                    .Descendants("label")
                    .Single(element => (string?)element.Attribute("languagecode") == "3082");
                return (value, (string)label.Attribute("description")!);
            })
            .ToArray();
    }

    private static ExportedLookup ReadLookup(
        string referencingEntity,
        string referencedEntity,
        string attributeName)
    {
        var relationship = Directory
            .EnumerateFiles(Path.Combine(SolutionRoot, "Other", "Relationships"), "*.xml")
            .Select(XDocument.Load)
            .SelectMany(document => document.Descendants("EntityRelationship"))
            .Single(element =>
                EqualsElement(element, "ReferencingEntityName", referencingEntity)
                && EqualsElement(element, "ReferencedEntityName", referencedEntity)
                && EqualsElement(element, "ReferencingAttributeName", attributeName));
        var referencingRole = relationship
            .Descendants("EntityRelationshipRole")
            .Single(role => (string?)role.Element("RelationshipRoleType") == "1");

        return new ExportedLookup(
            relationship.Element("ReferencingAttributeName")!.Value,
            referencingRole.Element("NavigationPropertyName")!.Value);
    }

    private static bool EqualsElement(XElement parent, string elementName, string expected) =>
        string.Equals(
            parent.Element(elementName)?.Value,
            expected,
            StringComparison.OrdinalIgnoreCase);

    private static string ReadProjectFile(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. segments]));

    private static XDocument ReadEntity(string entityName) =>
        XDocument.Load(Path.Combine(SolutionRoot, "Entities", entityName, "Entity.xml"));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CotizadorInterno.Web.csproj"))
                && Directory.Exists(Path.Combine(
                    directory.FullName,
                    "solutions",
                    "CotizadorInternoCRM")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "No se encontró la raíz del proyecto ni el export solutions/CotizadorInternoCRM.");
    }

    private sealed record LookupContract(
        string ReferencingEntity,
        string ReferencedEntity,
        string Attribute,
        string NavigationProperty);

    private sealed record ExportedLookup(string Attribute, string NavigationProperty);

    private sealed record RecordedRequest(string RelativePath, string Method, string Body);

    private class RecordingDownstreamApi : DispatchProxy
    {
        public List<RecordedRequest> Requests { get; } = [];
        public Func<RecordedRequest, HttpResponseMessage> Responder { get; set; } =
            _ => throw new InvalidOperationException("La prueba no configuró una respuesta.");

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IDownstreamApi.CallApiForUserAsync)
                || targetMethod.IsGenericMethod
                || args is null
                || args.Length != 5)
            {
                throw new NotSupportedException(
                    $"La prueba no implementa {targetMethod?.Name ?? "un método desconocido"}.");
            }

            var configure = Assert.IsType<Action<DownstreamApiOptions>>(args[1]);
            var options = new DownstreamApiOptions();
            configure(options);
            using var request = new HttpRequestMessage(
                new HttpMethod(options.HttpMethod ?? HttpMethod.Get.Method),
                options.RelativePath ?? "/");
            options.CustomizeHttpRequestMessage?.Invoke(request);

            var body = args[3] is HttpContent content
                ? content.ReadAsStringAsync().GetAwaiter().GetResult()
                : "";
            var recorded = new RecordedRequest(
                options.RelativePath ?? "",
                options.HttpMethod ?? "",
                body);
            Requests.Add(recorded);

            return Task.FromResult(Responder(recorded));
        }
    }
}
