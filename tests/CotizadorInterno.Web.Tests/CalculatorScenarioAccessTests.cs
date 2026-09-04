using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Calculator;
using CotizadorInterno.Web.Models.Crm;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.Calculator;
using CotizadorInterno.Web.Services.Crm;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CalculatorScenarioAccessTests
{
    [Fact]
    public async Task ProvisioningWhenCrmSyncIsDisabledContinuesWithoutReadingOrWritingCrm()
    {
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("commercial-owner", hasCrm: true);
        data.UserScenarios = [Scenario("scenario-1", "commercial-owner")];
        var (crm, crmRecorder) = CreateCrm();
        var controller = CreateController(dataverse, crm);

        var action = await controller.SubmitProvisioning(
            ProvisioningRequest("scenario-1"),
            CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(action);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        Assert.False(json.RootElement.GetProperty("crmSynchronized").GetBoolean());
        Assert.Equal("", json.RootElement.GetProperty("crmDealId").GetString());
        Assert.Equal(0, crmRecorder.DetailCalls);
        Assert.Equal(0, crmRecorder.UpsertCalls);
        Assert.Equal(0, crmRecorder.MarkProvisioningCalls);
    }

    [Fact]
    public async Task CrmUserCanOpenASharedScenarioOnlyThroughAnOwnerScopedDeal()
    {
        var dealId = Guid.NewGuid().ToString("D");
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("crm-viewer", hasCrm: true);
        data.UserScenarios = [];
        data.GlobalScenario = Scenario("scenario-shared", "original-owner");
        var (crm, crmRecorder) = CreateCrm();
        crmRecorder.Detail = DealDetail(dealId, "scenario-shared");
        var controller = CreateController(
            dataverse,
            crm,
            crmCalculatorSyncEnabled: true);

        var action = await controller.Index(
            "scenario-shared",
            dealId,
            CancellationToken.None);

        var view = Assert.IsType<ViewResult>(action);
        var scenarios = Assert.IsAssignableFrom<IReadOnlyList<ScenarioStoredDto>>(
            view.ViewData["StoredScenarios"]);
        var scenario = Assert.Single(scenarios);
        Assert.True(scenario.IsCrmSharedAccess);
        Assert.Equal(dealId, scenario.CrmDealId);
        Assert.Equal(1, data.GlobalReads);
        Assert.Equal(1, crmRecorder.DetailCalls);
        Assert.NotNull(crmRecorder.LastDetailScope);
        Assert.Equal(CrmRole.User, crmRecorder.LastDetailScope!.Role);
        Assert.Equal(
            data.CurrentUser!.SystemUserId,
            crmRecorder.LastDetailScope.OwnerFilterSystemUserId);
    }

    [Fact]
    public async Task SharedCrmSavePatchesTheOriginalScenarioWithoutOwnerScopedUpsert()
    {
        var dealId = Guid.NewGuid().ToString("D");
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("crm-viewer", hasCrm: true);
        data.GlobalScenario = Scenario("scenario-shared", "original-owner");
        var (crm, crmRecorder) = CreateCrm();
        crmRecorder.Detail = DealDetail(dealId, "scenario-shared");
        var controller = CreateController(
            dataverse,
            crm,
            crmCalculatorSyncEnabled: true);
        var request = ScenarioSave("scenario-shared", dealId);

        var action = await controller.SaveScenario(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(action);
        Assert.Equal(1, data.GlobalUpdates);
        Assert.Equal(0, data.OwnerUpserts);
        Assert.Same(request, data.LastGlobalUpdate);
    }

    [Fact]
    public async Task SaveScenarioReplacesBrowserScoresWithAuthoritativeCalculation()
    {
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("commercial-owner", hasCrm: false);
        var (crm, _) = CreateCrm();
        var controller = CreateController(dataverse, crm);
        var request = ScenarioSave("scenario-authoritative", "");
        request.LastResult = new ScenarioResultSnapshot
        {
            InputHash = "",
            Points = 999_999m,
            Commission = 999_999m,
            TotalMonthlySale = 1m,
            TotalSale = 1m
        };

        var action = await controller.SaveScenario(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(action);
        var persisted = Assert.IsType<ScenarioSaveRequest>(data.LastOwnerUpsert);
        Assert.NotNull(persisted.LastResult);
        Assert.NotEqual(999_999m, persisted.LastResult.Points);
        Assert.NotEqual(999_999m, persisted.LastResult.Commission);
        Assert.Equal(64, persisted.LastResult.InputHash.Length);
        Assert.True(persisted.LastResult.TotalMonthlySale > 1m);
        Assert.True(persisted.LastResult.TotalSale > 1m);
    }

    [Fact]
    public async Task SendToCrmUsesTheExactDealToResolveASharedScenario()
    {
        var dealId = Guid.NewGuid().ToString("D");
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("crm-viewer", hasCrm: true);
        data.UserScenarios = [];
        data.GlobalScenario = Scenario("scenario-shared", "original-owner");
        var (crm, crmRecorder) = CreateCrm();
        crmRecorder.Detail = DealDetail(dealId, "scenario-shared");
        var controller = CreateController(
            dataverse,
            crm,
            crmCalculatorSyncEnabled: true);

        var action = await controller.SendToCrm(
            new CrmDealFromCalculatorRequest
            {
                DealId = dealId,
                ScenarioId = "scenario-shared",
                CompanyId = Guid.NewGuid().ToString("D"),
                Name = "Oportunidad compartida",
                Kind = CrmDealKind.EstimatedOpportunity,
                EstimatedValue = 15_000_000m,
                Probability = 30m
            },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(action);
        Assert.Equal(1, data.GlobalReads);
        Assert.Equal(1, crmRecorder.DetailCalls);
        Assert.Equal(1, crmRecorder.UpsertCalls);
        Assert.Equal(dealId, crmRecorder.LastUpsertCommand?.DealId);
        Assert.Equal("scenario-shared", crmRecorder.LastUpsertCommand?.ScenarioId);
    }

    [Fact]
    public async Task SendToCrmReturnsProblem503BeforeValidatingTheRequestWhenSyncIsDisabled()
    {
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("crm-viewer", hasCrm: true);
        var (crm, crmRecorder) = CreateCrm();
        var controller = CreateController(dataverse, crm);

        var action = await controller.SendToCrm(
            request: null,
            CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Contains("application/problem+json", result.ContentTypes);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.Status);
        Assert.Equal(
            "Disponible cuando el CRM se publique para todos.",
            problem.Detail);
        Assert.Equal(0, data.GlobalReads);
        Assert.Equal(0, crmRecorder.DetailCalls);
        Assert.Equal(0, crmRecorder.UpsertCalls);
    }

    [Fact]
    public async Task SharedProvisioningSynchronizesAndMarksCrmOnlyAfterFlowAcceptance()
    {
        var events = new List<string>();
        var dealId = Guid.NewGuid().ToString("D");
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("crm-viewer", hasCrm: true);
        data.UserScenarios = [];
        data.GlobalScenario = Scenario("scenario-shared", "original-owner");
        var (crm, crmRecorder) = CreateCrm();
        crmRecorder.Detail = DealDetail(dealId, "scenario-shared");
        crmRecorder.Events = events;
        var controller = CreateController(
            dataverse,
            crm,
            events,
            crmCalculatorSyncEnabled: true);
        var request = ProvisioningRequest("scenario-shared");
        request.CrmDealId = dealId;

        var action = await controller.SubmitProvisioning(
            request,
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(action);
        Assert.Equal(1, data.GlobalReads);
        Assert.Equal(1, crmRecorder.DetailCalls);
        Assert.Equal(1, crmRecorder.UpsertCalls);
        Assert.Equal(1, crmRecorder.MarkProvisioningCalls);
        Assert.Equal(
            ["crm-detail", "flow", "crm-upsert", "crm-mark"],
            events);
        Assert.Equal(dealId, crmRecorder.LastUpsertCommand?.DealId);
        Assert.Equal("scenario-shared", crmRecorder.LastMarkedScenarioId);
    }

    [Fact]
    public async Task NonCrmUserCannotTurnAForeignScenarioIntoAnOwnerScopedDuplicate()
    {
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("commercial-viewer", hasCrm: false);
        data.OwnerUpsertException = new ScenarioPersistenceConflictException(
            "El escenario ya pertenece a otro usuario.");
        var (crm, crmRecorder) = CreateCrm();
        var controller = CreateController(dataverse, crm);

        var action = await controller.SaveScenario(
            ScenarioSave("scenario-foreign", Guid.NewGuid().ToString("D")),
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(action);
        Assert.Equal(1, data.OwnerUpserts);
        Assert.Equal(0, data.GlobalReads);
        Assert.Equal(0, crmRecorder.DetailCalls);
    }

    [Fact]
    public async Task CrmDealMustReferenceTheExactScenarioBeforeGlobalSave()
    {
        var dealId = Guid.NewGuid().ToString("D");
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("crm-viewer", hasCrm: true);
        var (crm, crmRecorder) = CreateCrm();
        crmRecorder.Detail = DealDetail(dealId, "different-scenario");
        var controller = CreateController(
            dataverse,
            crm,
            crmCalculatorSyncEnabled: true);

        var action = await controller.SaveScenario(
            ScenarioSave("scenario-requested", dealId),
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(action);
        Assert.Equal(0, data.GlobalReads);
        Assert.Equal(0, data.GlobalUpdates);
        Assert.Equal(0, data.OwnerUpserts);
    }

    [Fact]
    public async Task DuplicateGlobalScenarioIdReturnsConflict()
    {
        var dealId = Guid.NewGuid().ToString("D");
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("crm-viewer", hasCrm: true);
        data.GlobalReadException = new ScenarioPersistenceConflictException(
            "Existen varios escenarios con el mismo identificador.");
        var (crm, crmRecorder) = CreateCrm();
        crmRecorder.Detail = DealDetail(dealId, "scenario-duplicate");
        var controller = CreateController(
            dataverse,
            crm,
            crmCalculatorSyncEnabled: true);

        var action = await controller.Index(
            "scenario-duplicate",
            dealId,
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(action);
        Assert.Equal(1, data.GlobalReads);
        Assert.Equal(0, data.GlobalUpdates);
    }

    [Fact]
    public async Task ForeignOrMissingScenarioDeleteDoesNotReportSuccess()
    {
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("crm-viewer", hasCrm: true);
        data.DeleteResult = false;
        var (crm, _) = CreateCrm();
        var controller = CreateController(dataverse, crm);

        var action = await controller.DeleteScenario(
            "scenario-foreign",
            CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(action);
    }

    [Fact]
    public async Task OwnerCanDeleteWithoutReadingCrmWhenSyncIsDisabled()
    {
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("scenario-owner", hasCrm: true);
        data.DeleteResult = true;
        var (crm, crmRecorder) = CreateCrm();
        crmRecorder.ScenarioLookupException =
            new InvalidOperationException("CRM no disponible.");
        var controller = CreateController(dataverse, crm);

        var action = await controller.DeleteScenario(
            "scenario-owner-record",
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(action);
        Assert.Equal(0, crmRecorder.ScenarioLookupCalls);
        Assert.Equal(1, data.DeleteCalls);
    }

    [Fact]
    public async Task OwnerCannotDeleteACrmLinkedScenarioAfterReload()
    {
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("scenario-owner", hasCrm: true);
        data.DeleteResult = true;
        var (crm, crmRecorder) = CreateCrm();
        crmRecorder.ScenarioLookup = new CrmDealSummary
        {
            Id = Guid.NewGuid().ToString("D"),
            ScenarioId = "scenario-linked"
        };
        var controller = CreateController(
            dataverse,
            crm,
            crmCalculatorSyncEnabled: true);

        var action = await controller.DeleteScenario(
            "scenario-linked",
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(action);
        Assert.Equal(1, crmRecorder.ScenarioLookupCalls);
        Assert.Equal(0, data.DeleteCalls);
    }

    [Fact]
    public async Task ScenarioDeleteFailsClosedWhenCrmLinkCannotBeVerified()
    {
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("scenario-owner", hasCrm: false);
        data.DeleteResult = true;
        var (crm, crmRecorder) = CreateCrm();
        crmRecorder.ScenarioLookupException =
            new InvalidOperationException("Dataverse no disponible.");
        var controller = CreateController(
            dataverse,
            crm,
            crmCalculatorSyncEnabled: true);

        var action = await controller.DeleteScenario(
            "scenario-owner-record",
            CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal(1, crmRecorder.ScenarioLookupCalls);
        Assert.Equal(0, data.DeleteCalls);
    }

    [Fact]
    public void GlobalScenarioPersistenceUsesTwoRowDuplicateDetectionAndOwnerFreePatch()
    {
        var service = ReadProjectFile("Services", "DataverseService.cs");
        var helperStart = service.IndexOf(
            "private async Task<ScenarioRecord?> FindUniqueScenarioRecordAsync",
            StringComparison.Ordinal);
        var payloadStart = service.IndexOf(
            "private static Dictionary<string, object?> BuildScenarioPayload",
            helperStart,
            StringComparison.Ordinal);
        var payloadEnd = service.IndexOf(
            "private static ScenarioStoredDto ParseScenarioStoredDto",
            payloadStart,
            StringComparison.Ordinal);

        Assert.True(helperStart >= 0);
        Assert.True(payloadStart > helperStart);
        Assert.True(payloadEnd > payloadStart);
        Assert.Contains("$top=2", service[helperStart..payloadStart], StringComparison.Ordinal);
        var payload = service[payloadStart..payloadEnd];
        Assert.DoesNotContain("cr07a_systemuserid", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("cr07a_displayname", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("cr07a_email", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("ownerid", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CalculatorUiOnlyBlocksCrmLinkedDeletionWhenSyncIsEnabled()
    {
        var view = ReadProjectFile("Views", "Calculator", "Index.cshtml");

        Assert.Contains(
            "|| (crmCalculatorSyncEnabled && Boolean(s.crmDealId))",
            view,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (crmCalculatorSyncEnabled && activeScenario?.crmDealId)",
            view,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (activeScenario?.isCrmSharedAccess)",
            view,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OwnerCanOpenProposalWithOnlyCustomerFacingEconomicFields()
    {
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("scenario-owner", hasCrm: false);
        var scenario = Scenario("scenario-proposal", data.CurrentUser.SystemUserId);
        scenario.Lines[0].HasVat = true;
        data.UserScenarios = [scenario];
        var (crm, crmRecorder) = CreateCrm();
        var controller = CreateController(dataverse, crm);

        var action = await controller.Proposal(
            "scenario-proposal",
            null,
            CancellationToken.None);

        var view = Assert.IsType<ViewResult>(action);
        Assert.Equal("Proposal", view.ViewName);
        var model = Assert.IsType<CalculatorProposalViewModel>(view.Model);
        var line = Assert.Single(model.Lines);
        Assert.Equal("scenario-proposal", model.ScenarioId);
        Assert.Equal("scenario-owner", model.PreparedByName);
        Assert.Equal("Microsoft 365", line.Description);
        Assert.Equal(120m, line.UnitSale);
        Assert.Equal(120m, line.MonthlySale);
        Assert.Equal(1_440m, line.ContractSale);
        Assert.Equal(22.80m, line.MonthlyVat);
        Assert.Equal(273.60m, line.ContractVat);
        Assert.Equal(142.80m, line.MonthlyTotalWithVat);
        Assert.Equal(1_713.60m, line.ContractTotalWithVat);
        Assert.Equal(0, crmRecorder.DetailCalls);

        var publicProperties = typeof(CalculatorProposalLineViewModel)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(nameof(ScenarioLineInput.CostUnit), publicProperties);
        Assert.DoesNotContain(nameof(ScenarioLineInput.MarginPercent), publicProperties);
        Assert.DoesNotContain(nameof(ScenarioLineInput.Acelerador), publicProperties);
        Assert.DoesNotContain(nameof(ScenarioResultSnapshot.Points), publicProperties);
        Assert.DoesNotContain(nameof(ScenarioResultSnapshot.Commission), publicProperties);
    }

    [Fact]
    public async Task SharedCrmProposalUsesTheExactAuthorizedDeal()
    {
        var dealId = Guid.NewGuid().ToString("D");
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("crm-viewer", hasCrm: true);
        data.UserScenarios = [];
        data.GlobalScenario = Scenario("scenario-shared-proposal", "original-owner");
        var (crm, crmRecorder) = CreateCrm();
        crmRecorder.Detail = DealDetail(dealId, "scenario-shared-proposal");
        var controller = CreateController(dataverse, crm, crmCalculatorSyncEnabled: true);

        var action = await controller.Proposal(
            "scenario-shared-proposal",
            dealId,
            CancellationToken.None);

        var view = Assert.IsType<ViewResult>(action);
        var model = Assert.IsType<CalculatorProposalViewModel>(view.Model);
        Assert.Equal(dealId, model.CrmDealId);
        Assert.Equal(1, data.GlobalReads);
        Assert.Equal(1, crmRecorder.DetailCalls);
        Assert.Equal(data.CurrentUser.SystemUserId, crmRecorder.LastDetailScope?.OwnerFilterSystemUserId);
    }

    [Fact]
    public async Task GroupOnlyProposalRejectsMixedOwners()
    {
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("scenario-owner", hasCrm: false);
        var first = Scenario("possibility-1", data.CurrentUser.SystemUserId);
        first.GroupId = "group-1";
        first.PossibilityOrder = 1;
        var foreign = Scenario("possibility-2", "another-owner");
        foreign.GroupId = "group-1";
        foreign.PossibilityOrder = 2;
        data.UserScenarios = [first, foreign];
        var (crm, _) = CreateCrm();
        var controller = CreateController(dataverse, crm);

        var action = await controller.Proposal(
            scenarioId: null,
            crmDealId: null,
            ct: CancellationToken.None,
            groupId: "group-1");

        var result = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task ScenarioProposalRejectsExcludedPossibilityOwnedByAnotherUser()
    {
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("scenario-owner", hasCrm: false);
        var first = Scenario("possibility-1", data.CurrentUser.SystemUserId);
        first.GroupId = "group-mixed-hidden";
        first.PossibilityOrder = 1;
        var hiddenForeign = Scenario("possibility-2", "another-owner");
        hiddenForeign.GroupId = "group-mixed-hidden";
        hiddenForeign.PossibilityOrder = 2;
        hiddenForeign.IncludeInProposal = false;
        data.UserScenarios = [first, hiddenForeign];
        var (crm, _) = CreateCrm();
        var controller = CreateController(dataverse, crm);

        var action = await controller.Proposal(
            "possibility-1",
            null,
            CancellationToken.None,
            "group-mixed-hidden");

        Assert.IsType<ConflictObjectResult>(action);
    }

    [Fact]
    public async Task RecommendPossibilityUsesTheDedicatedPersistenceOperation()
    {
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("scenario-owner", hasCrm: false);
        var first = Scenario("possibility-1", data.CurrentUser.SystemUserId);
        first.GroupId = "group-recommend";
        first.PossibilityOrder = 1;
        var second = Scenario("possibility-2", data.CurrentUser.SystemUserId);
        second.GroupId = "group-recommend";
        second.PossibilityOrder = 2;
        data.UserScenarios = [first, second];
        var (crm, _) = CreateCrm();
        var controller = CreateController(dataverse, crm);

        var action = await controller.RecommendPossibility(
            new ScenarioPossibilityRecommendationRequest
            {
                GroupId = "group-recommend",
                ScenarioId = "possibility-2"
            },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(action);
        Assert.Equal(1, data.RecommendCalls);
        Assert.Equal("possibility-2", data.LastRecommendedScenarioId);
    }

    [Fact]
    public async Task RenameScenarioGroupAuthorizesOwnerAndUsesDedicatedPersistenceOperation()
    {
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("scenario-owner", hasCrm: false);
        var scenario = Scenario("scenario-1", data.CurrentUser.SystemUserId);
        scenario.GroupId = "group-rename";
        data.UserScenarios = [scenario];
        data.RenameResult = true;
        var (crm, _) = CreateCrm();
        var controller = CreateController(dataverse, crm);

        var action = await controller.RenameScenarioGroup(
            new ScenarioGroupRenameRequest
            {
                GroupId = "group-rename",
                GroupName = "Renovación 2027"
            },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(action);
        Assert.Equal(1, data.RenameCalls);
        Assert.Equal("group-rename", data.LastRenamedGroupId);
        Assert.Equal("Renovación 2027", data.LastRenamedGroupName);
    }

    [Fact]
    public async Task DuplicatePossibilityCopiesInputsWithNewLineIdsAndClearsTheStoredResult()
    {
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("scenario-owner", hasCrm: false);
        var source = Scenario("scenario-source", data.CurrentUser.SystemUserId);
        source.GroupId = "group-duplicate";
        source.GroupName = "Negocio duplicado";
        source.PossibilityOrder = 1;
        source.Lines[0].LineId = "source-line";
        source.LastResult!.InputHash = new string('A', 64);
        data.UserScenarios = [source];
        var (crm, _) = CreateCrm();
        var controller = CreateController(dataverse, crm);

        var action = await controller.CreatePossibility(
            new ScenarioPossibilityCreateRequest
            {
                GroupId = source.GroupId,
                SourceScenarioId = source.ScenarioId,
                DuplicateSource = true,
                Name = "Escenario 2"
            },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(action);
        var saved = Assert.IsType<ScenarioSaveRequest>(data.LastOwnerUpsert);
        Assert.Equal(source.GroupId, saved.GroupId);
        Assert.Equal(2, saved.PossibilityOrder);
        Assert.Null(saved.LastResult);
        var duplicatedLine = Assert.Single(saved.Lines);
        Assert.NotEqual(source.Lines[0].LineId, duplicatedLine.LineId);
        Assert.Equal(source.Lines[0].ProductId, duplicatedLine.ProductId);
        Assert.Equal(source.Lines[0].Quantity, duplicatedLine.Quantity);
        Assert.Equal(source.Lines[0].SuggestedRetailPrice, duplicatedLine.SuggestedRetailPrice);
    }

    [Fact]
    public async Task DuplicatePossibilityReturnsJsonInsteadOfAnHtmlErrorPage()
    {
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("scenario-owner", hasCrm: false);
        var source = Scenario("scenario-source", data.CurrentUser.SystemUserId);
        source.GroupId = "group-error";
        data.UserScenarios = [source];
        data.OwnerUpsertException = new InvalidOperationException("Dataverse batch failure");
        var (crm, _) = CreateCrm();
        var controller = CreateController(dataverse, crm);

        var action = await controller.CreatePossibility(
            new ScenarioPossibilityCreateRequest
            {
                GroupId = source.GroupId,
                SourceScenarioId = source.ScenarioId,
                DuplicateSource = true
            },
            CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        Assert.Contains("duplicar el escenario", json.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task RenameScenarioGroupDoesNotRevealAnUnavailableBusiness()
    {
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("scenario-viewer", hasCrm: false);
        var foreign = Scenario("scenario-foreign", Guid.NewGuid().ToString("D"));
        foreign.GroupId = "group-foreign";
        data.UserScenarios = [foreign];
        data.RenameResult = false;
        var (crm, _) = CreateCrm();
        var controller = CreateController(dataverse, crm);

        var action = await controller.RenameScenarioGroup(
            new ScenarioGroupRenameRequest
            {
                GroupId = "group-foreign",
                GroupName = "Nombre no autorizado"
            },
            CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(action);
        Assert.Equal(1, data.RenameCalls);
    }

    [Fact]
    public async Task MissingProposalScenarioReturnsNotFound()
    {
        var (dataverse, data) = CreateDataverse();
        data.CurrentUser = User("scenario-owner", hasCrm: false);
        data.UserScenarios = [];
        var (crm, _) = CreateCrm();
        var controller = CreateController(dataverse, crm);

        var action = await controller.Proposal(
            "missing-scenario",
            null,
            CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(action);
    }

    private static CalculatorController CreateController(
        IDataverseService dataverse,
        ICrmRepository crm,
        IList<string>? events = null,
        bool crmCalculatorSyncEnabled = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Crm:CalculatorSyncEnabled"] =
                    crmCalculatorSyncEnabled ? "true" : "false"
            })
            .Build();
        var controller = new CalculatorController(
            dataverse,
            new QuoteCalculator(),
            crm,
            new StaticHttpClientFactory(
                new HttpClient(new SuccessHandler(events))
                {
                    BaseAddress = new Uri("https://flow.test")
                }),
            Options.Create(new CalculatorOptions
            {
                ProvisioningRequestFlowUrl = "https://flow.test/submit"
            }),
            NullLogger<CalculatorController>.Instance,
            configuration);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    private static CurrentUserInfo User(string displayName, bool hasCrm) => new()
    {
        SystemUserId = Guid.NewGuid().ToString("D"),
        DisplayName = displayName,
        Email = $"{displayName}@example.test",
        ModuleOptionValues = hasCrm
            ? [AppModuleCatalog.Crm.OptionValue]
            : []
    };

    private static ScenarioStoredDto Scenario(string scenarioId, string ownerSystemUserId) =>
        new()
        {
            ScenarioId = scenarioId,
            OwnerSystemUserId = ownerSystemUserId,
            ScenarioName = "Escenario",
            DealType = (int)DealType.ClienteNuevo,
            Lines =
            [
                new ScenarioLineInput
                {
                    BusinessType = (int)BusinessType.ModernWork,
                    ProductId = Guid.NewGuid().ToString("D"),
                    ProductDescription = "Microsoft 365",
                    CostUnit = 100m,
                    MarginPercent = 20m,
                    ContractMonths = 12,
                    Quantity = 1,
                    HasVat = false
                }
            ],
            LastResult = new ScenarioResultSnapshot
            {
                Points = 10m,
                TotalSale = 1_440m
            }
        };

    private static ScenarioSaveRequest ScenarioSave(string scenarioId, string crmDealId) =>
        new()
        {
            ScenarioId = scenarioId,
            CrmDealId = crmDealId,
            ScenarioName = "Escenario actualizado",
            DealType = (int)DealType.ClienteNuevo,
            Lines = Scenario(scenarioId, "owner").Lines
        };

    private static CrmDealDetailViewModel DealDetail(string dealId, string scenarioId) =>
        new()
        {
            Deal = new CrmDealSummary
            {
                Id = dealId,
                ScenarioId = scenarioId,
                Name = "Negocio CRM"
            }
        };

    private static ProvisioningRequestInput ProvisioningRequest(string scenarioId) =>
        new()
        {
            BusinessId = scenarioId,
            CrmDealId = Guid.NewGuid().ToString("D"),
            Requester = new ProvisioningRequester
            {
                SystemUserId = "commercial-owner",
                DisplayName = "Commercial Owner",
                Email = "commercial-owner@example.test"
            },
            Cliente = new ProvisioningClient
            {
                ClienteId = Guid.NewGuid().ToString("D"),
                Nombre = "Cliente"
            },
            Aprovisionamiento = new ProvisioningAprovisionamiento
            {
                Fecha = "2026-07-24",
                TipoContratoCode = "645250000",
                TipoContratoLabel = "Negocio nuevo"
            },
            Scenario = new ProvisioningScenarioContext
            {
                DealTypeValue = (int)DealType.ClienteNuevo
            },
            LineItems =
            [
                new ProvisioningLineItem
                {
                    LineId = "line-1",
                    ProductoId = Guid.NewGuid().ToString("D"),
                    ProductoNombre = "Microsoft 365",
                    Cantidad = 1,
                    Number = 1,
                    CostoUnd = 100m,
                    VentaUnd = 120m,
                    MargenPorcentaje = 20m,
                    DuracionMeses = 12,
                    VentaMensual = 120m,
                    VentaTotal = 1_440m,
                    Tipo = BusinessType.ModernWork.ToString()
                }
            ],
            Attachment = new ProvisioningAttachment
            {
                FileName = "oferta.pdf",
                ContentType = "application/pdf",
                Base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("test"))
            }
        };

    private static (IDataverseService Service, CalculatorDataverseProxy Recorder) CreateDataverse()
    {
        var service = DispatchProxy.Create<IDataverseService, CalculatorDataverseProxy>();
        return (service, (CalculatorDataverseProxy)service);
    }

    private static (ICrmRepository Service, CalculatorCrmProxy Recorder) CreateCrm()
    {
        var service = DispatchProxy.Create<ICrmRepository, CalculatorCrmProxy>();
        return (service, (CalculatorCrmProxy)service);
    }

    private static string ReadProjectFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                Path.Combine(segments));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"No se encontró {Path.Combine(segments)} desde el directorio de pruebas.");
    }

    public class CalculatorDataverseProxy : DispatchProxy
    {
        public CurrentUserInfo? CurrentUser { get; set; }
        public IReadOnlyList<ScenarioStoredDto> UserScenarios { get; set; } = [];
        public ScenarioStoredDto? GlobalScenario { get; set; }
        public Exception? GlobalReadException { get; set; }
        public Exception? OwnerUpsertException { get; set; }
        public int GlobalReads { get; private set; }
        public int GlobalUpdates { get; private set; }
        public int OwnerUpserts { get; private set; }
        public int DeleteCalls { get; private set; }
        public int RecommendCalls { get; private set; }
        public int RenameCalls { get; private set; }
        public bool DeleteResult { get; set; }
        public bool RenameResult { get; set; }
        public string LastRecommendedScenarioId { get; private set; } = "";
        public string LastRenamedGroupId { get; private set; } = "";
        public string LastRenamedGroupName { get; private set; } = "";
        public ScenarioSaveRequest? LastGlobalUpdate { get; private set; }
        public ScenarioSaveRequest? LastOwnerUpsert { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IDataverseService.GetCurrentUserAsync):
                    return Task.FromResult(CurrentUser);
                case nameof(IDataverseService.GetScenariosForUserAsync):
                    return Task.FromResult(UserScenarios);
                case nameof(IDataverseService.GetScenariosByGroupIdAsync):
                    var groupId = Assert.IsType<string>(args![0]);
                    var groupCandidates = UserScenarios
                        .Concat(GlobalScenario is null ? [] : [GlobalScenario])
                        .GroupBy(item => item.ScenarioId, StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.First());
                    return Task.FromResult<IReadOnlyList<ScenarioStoredDto>>(groupCandidates
                        .Where(item => string.Equals(
                            string.IsNullOrWhiteSpace(item.GroupId) ? item.ScenarioId : item.GroupId,
                            groupId,
                            StringComparison.OrdinalIgnoreCase))
                        .OrderBy(item => item.PossibilityOrder)
                        .ToList());
                case nameof(IDataverseService.GetProposalHistoryForUserAsync):
                    return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<ProposalExportHistoryItemDto>>>(
                        new Dictionary<string, IReadOnlyList<ProposalExportHistoryItemDto>>(StringComparer.OrdinalIgnoreCase));
                case nameof(IDataverseService.GetProposalHistoryAsync):
                    return Task.FromResult<IReadOnlyList<ProposalExportHistoryItemDto>>([]);
                case nameof(IDataverseService.GetLatestProposalConfigurationAsync):
                    return Task.FromResult<ProposalConfigurationSnapshotDto?>(null);
                case nameof(IDataverseService.GetScenarioByIdAsync):
                    GlobalReads++;
                    return GlobalReadException is null
                        ? Task.FromResult(GlobalScenario)
                        : Task.FromException<ScenarioStoredDto?>(GlobalReadException);
                case nameof(IDataverseService.UpdateScenarioByIdAsync):
                    GlobalUpdates++;
                    LastGlobalUpdate = Assert.IsType<ScenarioSaveRequest>(args![0]);
                    return Task.CompletedTask;
                case nameof(IDataverseService.UpdateScenarioByIdAuthorizedAsync):
                    GlobalUpdates++;
                    LastGlobalUpdate = Assert.IsType<ScenarioSaveRequest>(args![0]);
                    return Task.FromResult<ScenarioStoredDto?>(GlobalScenario);
                case nameof(IDataverseService.UpsertScenarioAsync):
                    OwnerUpserts++;
                    return OwnerUpsertException is null
                        ? Task.CompletedTask
                        : Task.FromException(OwnerUpsertException);
                case nameof(IDataverseService.SaveScenarioV2Async):
                    var saveRequest = Assert.IsType<ScenarioSaveRequest>(args![0]);
                    var updateOnly = Assert.IsType<bool>(args[1]);
                    if (updateOnly)
                    {
                        GlobalUpdates++;
                        LastGlobalUpdate = saveRequest;
                    }
                    else
                    {
                        OwnerUpserts++;
                        LastOwnerUpsert = saveRequest;
                    }
                    if (!updateOnly && OwnerUpsertException is not null)
                        return Task.FromException<ScenarioStoredDto?>(OwnerUpsertException);
                    return Task.FromResult<ScenarioStoredDto?>(GlobalScenario
                        ?? UserScenarios.FirstOrDefault(item => string.Equals(item.ScenarioId, saveRequest.ScenarioId, StringComparison.OrdinalIgnoreCase))
                        ?? new ScenarioStoredDto { ScenarioId = saveRequest.ScenarioId });
                case nameof(IDataverseService.DeleteScenarioAsync):
                    DeleteCalls++;
                    return Task.FromResult(DeleteResult);
                case nameof(IDataverseService.RecommendScenarioPossibilityAsync):
                    RecommendCalls++;
                    LastRecommendedScenarioId = Assert.IsType<string>(args![1]);
                    return Task.FromResult(true);
                case nameof(IDataverseService.RenameScenarioGroupAsync):
                    RenameCalls++;
                    LastRenamedGroupId = Assert.IsType<string>(args![0]);
                    LastRenamedGroupName = Assert.IsType<string>(args[1]);
                    return Task.FromResult(RenameResult);
                default:
                    throw new NotSupportedException(
                        $"La prueba no implementa {targetMethod?.Name ?? "un método desconocido"}.");
            }
        }
    }

    public class CalculatorCrmProxy : DispatchProxy
    {
        public CrmDealDetailViewModel Detail { get; set; } = new();
        public int DetailCalls { get; private set; }
        public int UpsertCalls { get; private set; }
        public int MarkProvisioningCalls { get; private set; }
        public int ScenarioLookupCalls { get; private set; }
        public CrmAccessScope? LastDetailScope { get; private set; }
        public IList<string>? Events { get; set; }
        public CrmCalculatorDealUpsertCommand? LastUpsertCommand { get; private set; }
        public string LastMarkedScenarioId { get; private set; } = "";
        public CrmDealSummary? ScenarioLookup { get; set; }
        public Exception? ScenarioLookupException { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(ICrmRepository.GetDealDetailAsync):
                    DetailCalls++;
                    Events?.Add("crm-detail");
                    LastDetailScope = args?
                        .OfType<CrmAccessScope>()
                        .SingleOrDefault();
                    return Task.FromResult(Detail);
                case nameof(ICrmRepository.GetDealByScenarioIdAsync):
                    ScenarioLookupCalls++;
                    return ScenarioLookupException is null
                        ? Task.FromResult(ScenarioLookup)
                        : Task.FromException<CrmDealSummary?>(ScenarioLookupException);
                case nameof(ICrmRepository.UpsertDealFromCalculatorAsync):
                    UpsertCalls++;
                    Events?.Add("crm-upsert");
                    LastUpsertCommand = Assert.IsType<CrmCalculatorDealUpsertCommand>(args![0]);
                    return Task.FromResult(new CrmDealSummary());
                case nameof(ICrmRepository.MarkProvisioningRequestedAsync):
                    MarkProvisioningCalls++;
                    Events?.Add("crm-mark");
                    LastMarkedScenarioId = Assert.IsType<string>(args![0]);
                    return Task.FromResult<CrmDealSummary?>(new CrmDealSummary());
                default:
                    throw new NotSupportedException(
                        $"La prueba no implementa {targetMethod?.Name ?? "un método desconocido"}.");
            }
        }
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class SuccessHandler(IList<string>? events) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            events?.Add("flow");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
