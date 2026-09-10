using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CopiersActivityV2LifecycleTests
{
    private const string RecordId = "63e2a827-72d7-4ac3-a7dd-775668a03587";
    private const string ClientId = "81522aa1-96b9-45e8-937a-b5f0d920dc46";
    private const string OriginId = "ab34c33a-cf9f-4d80-9563-0dc8c0be9ef8";
    private const string EquipmentId = "58e07029-6d84-425c-aee0-17b5d8f14a32";
    private const string SupplyId = "f3ac6e22-c614-4693-89ec-7b1dab4f3141";
    private const string TechnicianId = "0336949f-67d4-42ca-80fd-ded5ae55fdcf";
    private const string SubmissionKey = "activity-lifecycle-test-20260910";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-10T15:00:00Z");

    [Fact]
    public void ActivityBindingsCloneWithoutMutatingMaintenanceOptions()
    {
        var original = Bindings();
        var before = JsonSerializer.Serialize(original);
        var activity = CopiersActivityV2Bindings.Create(original);
        Assert.Equal("dtc_copiersactivityv2s", activity.MainEntitySetName);
        Assert.Equal("dtc_copiersactivityv2id", activity.MainIdField);
        Assert.Equal("dtc_copiersactivityevidencev2s", activity.EvidenceEntitySetName);
        Assert.Equal("dtc_signedactivity", activity.EvidenceParentLookupLogicalName);
        Assert.Equal("dtc_SignedActivity", activity.EvidenceParentNavigationProperty);
        Assert.Equal(CopiersActivityV2Bindings.MovementType, activity.MaintenanceTypeCorrectiveValue);
        Assert.Equal(CopiersActivityV2Bindings.TonerType, activity.MaintenanceTypePreventiveValue);
        activity.MainEntitySetName = "changed";
        Assert.Equal(before, JsonSerializer.Serialize(original));
    }

    [Theory]
    [InlineData("movement")]
    [InlineData("toner")]
    public async Task ActivityValidatesThenBuildsThenCommitsRealBusinessBeforePublishingReadyPending(string kind)
    {
        var f = new Lifecycle(kind);
        var result = await f.Service().FinalizeMultipartAsync(Request(kind), Actor());
        Assert.Equal(new[] { "begin", "business.validate", "pdf", "business.commit", "complete" }, f.Operations);
        Assert.Equal(1, f.Business.Created);
        Assert.Equal(0, f.Repository.CreateCalls); // A business operation is not another MTO draft.
        Assert.Equal(CopiersMaintenanceV2WorkflowState.ReadyToSend, result.State);
        Assert.Equal(CopiersMaintenanceV2EmailState.Pending, result.EmailState);
        Assert.DoesNotContain("enviado al cliente", result.Message);
        var command = Assert.Single(f.Business.Commits);
        Assert.Equal(kind, command.ActivityKind);
        Assert.Equal(RecordId, command.ReportRecordId);
        Assert.Equal(TechnicianId, command.TechnicianSystemUserId);
        Assert.Equal(Now.AddMinutes(-3), command.OccurredAtUtc);
        var completed = Assert.Single(f.Repository.Completed);
        Assert.Equal(completed.FinalizationFingerprint, command.FinalizationFingerprint);
        Assert.Equal(completed.SignedReport.Sha256, Assert.Single(f.Business.PdfHashes));
        Assert.Equal("cliente@example.test", Assert.Single(completed.EmailOutbox.To));
        Assert.Equal("Contacto catálogo", completed.Answers.Single(x => x.Key == "onsite_contact").Value);
        Assert.Equal("cliente@example.test", completed.Answers.Single(x => x.Key == "onsite_email").Value);
        Assert.Equal("Cliente destino", completed.Answers.Single(x => x.Key == "destination_client_name").Value);
        Assert.Equal(kind == "movement" ? "Reubicación de oficina" : "Entrega de 2 unidad(es) de Tóner de muestra.", completed.WorkPerformed);
    }

    [Theory]
    [InlineData("validate")]
    [InlineData("pdf")]
    [InlineData("commit")]
    public async Task AnyPrepublicationFailureCannotQueueEmailOrFinishHeader(string phase)
    {
        var f = new Lifecycle("toner");
        f.Business.FailValidation = phase == "validate";
        f.Business.FailCommit = phase == "commit";
        f.Pdf.Fail = phase == "pdf";
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service().FinalizeMultipartAsync(Request("toner"), Actor()));
        Assert.Empty(f.Repository.Completed);
        Assert.Equal(CopiersMaintenanceV2WorkflowState.Failed, f.Repository.Record.State);
        Assert.Equal(CopiersMaintenanceV2EmailState.NotReady, f.Repository.Record.EmailState);
        Assert.Equal(0, f.Business.Created);
        Assert.Equal(phase == "validate" ? 0 : 1, f.Pdf.Models.Count);
        Assert.Equal(phase == "commit" ? 1 : 0, f.Business.Commits.Count);
    }

    [Theory]
    [InlineData("movement")]
    [InlineData("toner")]
    public async Task PersistedBusinessThenUnstagedHeaderFailureRetriesSamePdfHashAfterCaptureExpires(string kind)
    {
        var f = new Lifecycle(kind);
        f.Repository.FailBeforeStaging = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service().FinalizeMultipartAsync(Request(kind), Actor()));
        Assert.Equal(1, f.Business.Created);
        Assert.Empty(f.Repository.Record.FinalizationFingerprint);
        Assert.Equal(CopiersMaintenanceV2EmailState.NotReady, f.Repository.Record.EmailState);
        var result = await f.Service(Now.AddDays(2)).FinalizeMultipartAsync(Request(kind), Actor());
        Assert.Equal(CopiersMaintenanceV2EmailState.Pending, result.EmailState);
        Assert.Equal(1, f.Business.Created);
        Assert.Equal(2, f.Business.Commits.Count);
        Assert.Equal(f.Business.Commits[0], f.Business.Commits[1]);
        Assert.Equal(f.Business.PdfHashes[0], f.Business.PdfHashes[1]);
        Assert.All(f.Pdf.Models, model => Assert.Equal(Now.AddMinutes(-2), model.ServerFinalizedAtUtc));
        Assert.Equal(Now.AddDays(2), f.Repository.Completed[1].ServerFinalizedAtUtc);
        Assert.Equal(1, f.Repository.PublishCalls);
        Assert.Equal(0, f.Repository.CreateCalls);
    }

    [Theory]
    [InlineData("movement")]
    [InlineData("toner")]
    public async Task ReadyReplaySkipsPdfBusinessAndPublicationButRejectsChangedSignedFacts(string kind)
    {
        var f = new Lifecycle(kind);
        await f.Service().FinalizeMultipartAsync(Request(kind), Actor());
        var replay = await f.Service(Now.AddDays(2)).FinalizeMultipartAsync(Request(kind), Actor());
        Assert.True(replay.IdempotentReplay);
        Assert.Single(f.Business.Commits);
        Assert.Single(f.Pdf.Models);
        Assert.Equal(1, f.Repository.PublishCalls);
        var changed = Request(kind);
        changed.CustomerObservations = "Texto cambiado";
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => f.Service().FinalizeMultipartAsync(changed, Actor()));
        Assert.Single(f.Business.Commits);
        Assert.Single(f.Pdf.Models);
        Assert.Equal(0, f.Repository.FailedCalls);
    }

    [Fact]
    public async Task ChangedRetryCannotAdoptAnAlreadyCommittedBusinessFingerprint()
    {
        var f = new Lifecycle("toner");
        f.Repository.FailBeforeStaging = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service().FinalizeMultipartAsync(Request("toner"), Actor()));
        var changed = Request("toner");
        changed.CustomerObservations = "Nuevo contenido firmado";
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => f.Service().FinalizeMultipartAsync(changed, Actor()));
        Assert.Single(f.Pdf.Models);
        Assert.Single(f.Business.Commits);
        Assert.Equal(1, f.Business.Created);
        Assert.Equal(0, f.Repository.PublishCalls);
    }

    [Theory]
    [InlineData(CopiersMaintenanceV2WorkflowState.Draft)]
    [InlineData(CopiersMaintenanceV2WorkflowState.Failed)]
    public async Task ActivitySubmissionKeyCannotSwitchBusinessKindEvenBeforeHeaderFingerprintWasStaged(CopiersMaintenanceV2WorkflowState state)
    {
        var f = new Lifecycle("movement");
        f.Repository.Record.State = state;
        var request = DraftRequest(f.Repository.Record);
        request.MaintenanceTypeValue = CopiersActivityV2Bindings.TonerType;
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => f.Service().CreateOrGetDraftAsync(request, Actor()));
        Assert.Equal(CopiersActivityV2Bindings.MovementType, f.Repository.Record.MaintenanceTypeValue);
        Assert.Empty(f.Business.Commits);
        Assert.Empty(f.Repository.Completed);
    }

    [Fact]
    public async Task LegacyDraftMayStillChangePreventiveCorrectiveWithoutSwitchingBusinessDataset()
    {
        var f = new Lifecycle("movement");
        f.Repository.Record.MaintenanceTypeValue = Bindings().MaintenanceTypeCorrectiveValue;
        var request = DraftRequest(f.Repository.Record);
        request.MaintenanceTypeValue = Bindings().MaintenanceTypePreventiveValue;
        var result = await f.Service(activityBindings:false).CreateOrGetDraftAsync(request, Actor());
        Assert.True(result.ReusedExisting);
        Assert.Empty(f.Business.Commits);
    }

    [Fact]
    public async Task ExpiredNewActivityWithoutDurableBusinessProofCannotPublish()
    {
        var f = new Lifecycle("movement");
        var ex = await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => f.Service(Now.AddDays(2)).FinalizeMultipartAsync(Request("movement"), Actor()));
        Assert.Equal("signed_at_stale", ex.Code);
        Assert.Empty(f.Pdf.Models);
        Assert.Empty(f.Business.Commits);
        Assert.Empty(f.Repository.Completed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongFormForRepositoryIsRejectedBeforeAnyLeaseOrFailedMutation(bool activityRepository)
    {
        var f = new Lifecycle("movement");
        var request = Request("movement");
        request.FormVersion = activityRepository ? CopiersMtoV2CompactCapture.FormVersion : CopiersActivityV2Bindings.FormVersion;
        var service = f.Service(activityBindings: activityRepository);
        var ex = await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => service.FinalizeMultipartAsync(request, Actor()));
        Assert.Equal("activity_form_mismatch", ex.Code);
        Assert.Equal(0, f.Repository.BeginCalls);
        Assert.Equal(0, f.Repository.FailedCalls);
        Assert.Empty(f.Operations);
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("reason")]
    [InlineData("origin")]
    [InlineData("quantity")]
    [InlineData("stock")]
    [InlineData("supply")]
    [InlineData("time")]
    public async Task MismatchedPostedBusinessFactsNeverReachBusinessValidationOrPdf(string fact)
    {
        var kind = fact is "kind" or "reason" or "origin" ? "movement" : "toner";
        var f = new Lifecycle(kind);
        var request = Request(kind);
        switch (fact)
        {
            case "kind": request.ActivityKind = "toner"; break;
            case "reason": request.MovementReason = "Otro motivo"; break;
            case "origin": request.OriginClientId = ClientId; break;
            case "quantity": request.SupplyQuantity = 3; break;
            case "stock": ChangeAnswer(request, "supply_stock_before", "1"); break;
            case "supply": request.SupplyId = ClientId; break;
            case "time": request.ServiceEndedAtUtc = request.ServiceEndedAtUtc!.Value.AddMinutes(1); break;
        }
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => f.Service().FinalizeMultipartAsync(request, Actor()));
        Assert.Equal(0, f.Business.ValidateCalls);
        Assert.Empty(f.Pdf.Models);
        Assert.Empty(f.Repository.Completed);
    }

    [Fact]
    public async Task StockMovementAcceptsEmptyOriginConvertedToNullByMultipartModelBinding()
    {
        var f = new Lifecycle("movement");
        var request = Request("movement");
        request.OriginClientId = null!;
        ChangeAnswer(request, "origin_client_id", "");
        ChangeAnswer(request, "origin_client_name", "Stock");
        var result = await f.Service().FinalizeMultipartAsync(request, Actor());
        Assert.Equal(CopiersMaintenanceV2WorkflowState.ReadyToSend, result.State);
        Assert.Equal("", Assert.Single(f.Business.Commits).OriginClientId);
        Assert.Equal("Stock", f.Business.Commits[0].OriginClientName);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task ExternalEquipmentRequiresOrganizationWideZeroNotAnEmptyDelegatedCatalog(int count, bool allowed)
    {
        var f = new ControllerFixture();
        f.App.Response = count == 0 ? "{\"value\":[]}" : "{\"value\":[{\"cr07a_equipoid\":\"hidden-equipment\"}]}";
        var lookup = Assert.IsType<OkObjectResult>(await f.Controller.Equipment(ClientId, "maintenance", default));
        Assert.Equal(allowed, JsonSerializer.SerializeToElement(lookup.Value).GetProperty("allowExternalEquipment").GetBoolean());
        var query = Assert.Single(f.App.Reads);
        Assert.Contains("cr07a_equipos?$select=cr07a_equipoid", query);
        Assert.Contains("_cr07a_cliente_value eq " + ClientId, query);
        Assert.Contains("$top=1", query);
        f.SetForm("EquipmentId", ""); f.SetForm("EquipmentSerial", "EXTERNO-001");
        var result = await f.Controller.Finalize(new() { SubmissionKey=SubmissionKey }, default);
        if (allowed)
        {
            Assert.IsType<OkObjectResult>(result);
            Assert.Equal("", f.Maintenance.Draft!.EquipmentId);
            Assert.Equal("EXTERNO-001", f.Maintenance.Draft.EquipmentSerial);
        }
        else { Assert.IsType<BadRequestObjectResult>(result); Assert.Null(f.Maintenance.Draft); }
    }

    [Fact]
    public async Task FailedOrganizationProbeNeverEnablesFreeEntryOrWritesMaintenance()
    {
        var f = new ControllerFixture(); f.App.Status = HttpStatusCode.Forbidden;
        var result = Assert.IsType<ObjectResult>(await f.Controller.Equipment(ClientId, "maintenance", default));
        Assert.Equal(502, result.StatusCode);
        f.SetForm("EquipmentId", ""); f.SetForm("EquipmentSerial", "EXTERNO-001");
        Assert.IsNotType<OkObjectResult>(await f.Controller.Finalize(new() { SubmissionKey=SubmissionKey }, default));
        Assert.Null(f.Maintenance.Draft);
    }

    [Theory]
    [InlineData("movement", "copiers-mto-v2-2026-09-10")]
    [InlineData("maintenance", CopiersActivityV2Bindings.FormVersion)]
    [InlineData("other", CopiersActivityV2Bindings.FormVersion)]
    public async Task ControllerRejectsInvalidActivityFormBeforeAnyPersistenceOrApplicationRead(string kind, string version)
    {
        var f = new ControllerFixture();
        var result = await f.Controller.Finalize(new() { SubmissionKey=SubmissionKey, ActivityKind=kind, FormVersion=version }, default);
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(f.Maintenance.Draft);
        Assert.Empty(f.App.Reads);
    }

    [Fact]
    public async Task MovementPostCannotBypassSourceClientAllowlistAppliedByLookup()
    {
        var f = new ControllerFixture();
        f.Options.AllowedClientIds = [ClientId];
        f.Dataverse.Dashboard.EquipmentRows = [new() { RecordId=EquipmentId, Serial="SERIAL-001", ClientId=OriginId, InStock=false }];
        var result = await f.Controller.Finalize(new() { SubmissionKey=SubmissionKey, ActivityKind="movement", FormVersion=CopiersActivityV2Bindings.FormVersion }, default);
        Assert.True(result is ForbidResult or BadRequestObjectResult);
        Assert.Empty(f.App.Reads);
        Assert.Null(f.Maintenance.Draft);
    }

    [Theory]
    [InlineData("maintenance", 827270000, 0)]
    [InlineData("movement", 827270001, 1)]
    [InlineData("toner", 827270002, 2)]
    [InlineData("movement", 827270003, 3)]
    [InlineData("toner", 827270004, 4)]
    public async Task StatusDistinguishesNotReadyPendingProcessingSentFailedAndUsesCorrectDataset(string kind, int email, int expected)
    {
        var f = new ControllerFixture();
        f.App.Response = JsonSerializer.Serialize(new Dictionary<string,object> { [f.Bindings.TechnicianUserIdField]=TechnicianId,
            [f.Bindings.WorkflowStateField]=f.Bindings.ReadyToSendStateValue, [f.Bindings.EmailStateField]=email, [f.Bindings.ServiceReferenceField]="ACT-000123" });
        var result = Assert.IsType<OkObjectResult>(await f.Controller.Status(RecordId, kind, default));
        var json = JsonSerializer.SerializeToElement(result.Value);
        Assert.Equal(2, json.GetProperty("state").GetInt32());
        Assert.Equal(expected, json.GetProperty("emailState").GetInt32());
        Assert.Contains((kind == "maintenance" ? f.Bindings.MainEntitySetName : CopiersActivityV2Bindings.MainEntitySet) + "(" + RecordId + ")", Assert.Single(f.App.Reads));
    }

    [Fact]
    public async Task StatusNeverRevealsAnotherTechniciansSubmission()
    {
        var f = new ControllerFixture();
        f.App.Response = JsonSerializer.Serialize(new Dictionary<string,object> { [f.Bindings.TechnicianUserIdField]=OriginId });
        Assert.IsType<ForbidResult>(await f.Controller.Status(RecordId, "movement", default));
        Assert.Single(f.App.Reads);
    }

    [Theory]
    [InlineData(true, "movement", RecordId)]
    [InlineData(false, "other", RecordId)]
    [InlineData(false, "toner", "bad-id")]
    public async Task StatusRejectsDisabledInvalidKindsAndIdsBeforeApplicationReads(bool disabled, string kind, string id)
    {
        var f = new ControllerFixture(); f.Options.ActivitiesEnabled = !disabled;
        Assert.IsType<BadRequestObjectResult>(await f.Controller.Status(id, kind, default));
        Assert.Empty(f.App.Reads);
    }

    [Fact]
    public void DisabledRuntimeCannotCreateActivityRepository()
    {
        var f = new ControllerFixture(); f.Options.ActivitiesEnabled = false;
        Assert.Equal("activities_disabled", Assert.Throws<CopiersMaintenanceV2ValidationException>(() => f.Runtime.CreateService()).Code);
        Assert.Empty(f.App.Reads);
    }

    private static CopiersMaintenanceV2DataverseOptions Bindings() => new() {
        MainEntitySetName="dtc_copiersmtov2s", MainIdField="dtc_copiersmtov2id", TechnicianUserIdField="dtc_technicianuserid",
        WorkflowStateField="dtc_workflowstate", EmailStateField="dtc_emailstate", ServiceReferenceField="dtc_reference",
        MaintenanceTypeCorrectiveValue=827270000, MaintenanceTypePreventiveValue=827270001,
        ReadyToSendStateValue=827270002, FinalizingStateValue=827270001, FailedStateValue=827270003,
        EmailNotReadyStateValue=827270000, EmailPendingStateValue=827270001, EmailProcessingStateValue=827270002,
        EmailSentStateValue=827270003, EmailFailedStateValue=827270004 };
    private static CopiersMaintenanceV2ActorContext Actor() => new() { SystemUserId=TechnicianId, DisplayName="Técnico prueba", Email="tecnico@example.test" };
    private static CopiersMaintenanceV2DraftRequestDto DraftRequest(CopiersMaintenanceV2DraftRecord row) => new() {
        SubmissionKey=row.SubmissionKey, ClientId=row.ClientId, ClientName=row.ClientName, CustomerContactName=row.CustomerContactName,
        CustomerEmail=row.CustomerEmail, EquipmentId=row.EquipmentId, EquipmentSerial=row.EquipmentSerial, Title=row.Title,
        ServiceDate=row.ServiceDate, MaintenanceTypeValue=row.MaintenanceTypeValue };
    private static CopiersMaintenanceV2DraftRecord Record(string kind) => new() { RecordId=RecordId, SubmissionKey=SubmissionKey,
        ServiceReference="ACT-000123", Version="W/\"1\"", TechnicianSystemUserId=TechnicianId, TechnicianName="Técnico prueba",
        TechnicianEmail="tecnico@example.test", ClientId=ClientId, ClientName="Cliente destino", CustomerContactName="Contacto catálogo",
        CustomerEmail="cliente@example.test", EquipmentId=EquipmentId, EquipmentSerial="SERIAL-001", Title="Actividad",
        ServiceDate=new(2026,9,10), MaintenanceTypeValue=kind=="movement" ? CopiersActivityV2Bindings.MovementType : CopiersActivityV2Bindings.TonerType };
    private static CopiersMaintenanceV2FinalizeMultipartRequestDto Request(string kind)
    {
        var bytes = CopiersMtoV2CompactPdfTests.Model().SignatureContent;
        var request = new CopiersMaintenanceV2FinalizeMultipartRequestDto {
            RecordId=RecordId, SubmissionKey=SubmissionKey, ExpectedVersion="W/\"1\"", ActivityKind=kind, FormVersion=CopiersActivityV2Bindings.FormVersion,
            OriginClientId=OriginId, MovementReason="Reubicación de oficina", SupplyId=SupplyId, SupplyQuantity=2,
            WorkPerformed="Untrusted work", CustomerObservations="Recibido", SignerName="Cliente muestra", SignerRole="Supervisor", CustomerAccepted=true,
            ServiceStartedAtUtc=Now.AddHours(-1), ServiceEndedAtUtc=Now.AddMinutes(-3), DeviceSignedAtUtc=Now.AddMinutes(-2),
            Latitude=4.71, Longitude=-74.07, AccuracyMeters=10, LocationCapturedAtUtc=Now.AddMinutes(-1), LocationSource="navigator.geolocation",
            SignaturePointCount=5, Signature=new FormFile(new MemoryStream(bytes),0,bytes.Length,"Signature","firma.jpg") { Headers=new HeaderDictionary(),ContentType="image/jpeg" } };
        request.AnswersJson = JsonSerializer.Serialize(new Dictionary<string,string> {
            ["activity_kind"]=kind, ["onsite_contact"]="untrusted contact", ["onsite_email"]="untrusted@example.test",
            ["service_started_at"]="display", ["service_ended_at"]="display", ["service_started_at_utc"]=request.ServiceStartedAtUtc.Value.ToString("O"),
            ["service_ended_at_utc"]=request.ServiceEndedAtUtc.Value.ToString("O"), ["origin_client_id"]=OriginId, ["origin_client_name"]="Cliente origen",
            ["movement_reason"]=request.MovementReason, ["destination_client_name"]="untrusted destination", ["supply_id"]=SupplyId,
            ["supply_name"]="Tóner de muestra", ["supply_quantity"]="2", ["supply_stock_before"]="5"
        }.Select(x => new CopiersMaintenanceV2FormAnswerInputDto { Key=x.Key,Value=x.Value }));
        return request;
    }
    private static void ChangeAnswer(CopiersMaintenanceV2FinalizeMultipartRequestDto request, string key, string value)
    { var answers=JsonSerializer.Deserialize<List<CopiersMaintenanceV2FormAnswerInputDto>>(request.AnswersJson)!; answers.Single(x=>x.Key==key).Value=value; request.AnswersJson=JsonSerializer.Serialize(answers); }

    private sealed class Lifecycle
    {
        public readonly List<string> Operations=[];
        public readonly Repository Repository;
        public readonly Business Business;
        public readonly PdfSpy Pdf;
        public Lifecycle(string kind) { Repository=new(Record(kind),Operations); Business=new(Operations); Pdf=new(Operations); }
        public CopiersMaintenanceV2Service Service(DateTimeOffset? now=null, bool activityBindings=true) => new(Repository,Pdf,
            Options.Create(new CopiersMaintenanceV2Options { ActivitiesEnabled=true }),
            Options.Create(activityBindings ? CopiersActivityV2Bindings.Create(Bindings()) : Bindings()),
            new Clock(now ?? Now), NullLogger<CopiersMaintenanceV2Service>.Instance, business:Business);
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class PdfSpy(List<string> operations) : ICopiersMtoV2PdfBuilder
    {
        public bool Fail; public List<CopiersMaintenanceV2PdfModel> Models { get; }=[];
        public Task<CopiersMaintenanceV2RenderedPdf> BuildAsync(CopiersMaintenanceV2PdfModel model,CancellationToken ct=default)
        {
            operations.Add("pdf"); Models.Add(model); if(Fail) throw new InvalidOperationException("PDF failure");
            // Byte-sensitive test double, not a customer deliverable. A changing server timestamp changes this hash.
            return Task.FromResult(new CopiersMaintenanceV2RenderedPdf { FileName="report.pdf", Content=Encoding.UTF8.GetBytes("%PDF-1.4\n%"+JsonSerializer.Serialize(model)+"\n%%EOF") });
        }
    }
    private sealed class Business(List<string> operations) : ICopiersActivityV2BusinessService
    {
        public bool FailValidation, FailCommit; public int Created, ValidateCalls;
        public List<CopiersActivityV2BusinessCommand> Commits { get; }=[];
        public List<string> PdfHashes { get; }=[];
        private CopiersActivityV2BusinessCommand? _persisted;
        public Task<bool> ValidateAsync(CopiersActivityV2BusinessCommand command,CancellationToken ct=default)
        { operations.Add("business.validate"); ValidateCalls++; if(FailValidation) throw new InvalidOperationException("validation failure"); RequireSame(command); return Task.FromResult(_persisted is not null); }
        public Task<CopiersActivityV2BusinessResult> CommitAsync(CopiersActivityV2BusinessCommand command,CopiersMaintenanceV2StoredFile pdf,CancellationToken ct=default)
        { operations.Add("business.commit"); Commits.Add(command); PdfHashes.Add(pdf.Sha256); if(FailCommit) throw new InvalidOperationException("commit failure"); RequireSame(command); var reused=_persisted is not null; if(!reused) { Created++; _persisted=command; } return Task.FromResult(new CopiersActivityV2BusinessResult(RecordId,command.ServiceReference,reused)); }
        private void RequireSame(CopiersActivityV2BusinessCommand command) { if(_persisted is not null && _persisted!=command) throw new CopiersMaintenanceV2ConcurrencyException("Durable business snapshot differs"); }
    }
    private sealed class Repository(CopiersMaintenanceV2DraftRecord record,List<string> operations) : ICopiersMaintenanceV2DataverseRepository
    {
        public CopiersMaintenanceV2DraftRecord Record=record; public bool FailBeforeStaging; public int CreateCalls,BeginCalls,FailedCalls,PublishCalls;
        public List<CopiersMaintenanceV2CompleteFinalizationCommand> Completed { get; }=[];
        public Task<CopiersMaintenanceV2DraftRecord> CreateOrGetDraftAsync(CopiersMaintenanceV2CreateDraftCommand command,CancellationToken ct=default) { CreateCalls++; return Task.FromResult(Record); }
        public Task<CopiersMaintenanceV2DraftRecord> SaveDraftAsync(CopiersMaintenanceV2SaveDraftCommand command,CancellationToken ct=default) => throw new NotSupportedException();
        public Task<CopiersMaintenanceV2BeginFinalizationResult> TryBeginFinalizationAsync(CopiersMaintenanceV2BeginFinalizationCommand command,CancellationToken ct=default)
        { operations.Add("begin"); BeginCalls++; var ready=Record.State==CopiersMaintenanceV2WorkflowState.ReadyToSend; if(!ready) Record.State=CopiersMaintenanceV2WorkflowState.Finalizing; return Task.FromResult(new CopiersMaintenanceV2BeginFinalizationResult { Record=Record,FinalizationLeaseId=command.FinalizationLeaseId,Disposition=ready ? CopiersMaintenanceV2BeginDisposition.AlreadyReady : CopiersMaintenanceV2BeginDisposition.Acquired }); }
        public Task<CopiersMaintenanceV2DraftRecord> CompleteFinalizationAsync(CopiersMaintenanceV2CompleteFinalizationCommand command,CancellationToken ct=default)
        { operations.Add("complete"); Completed.Add(command); if(FailBeforeStaging) { FailBeforeStaging=false; throw new InvalidOperationException("Header staging failed before fingerprint"); }
            Record.FinalizationFingerprint=command.FinalizationFingerprint; Record.ReportSha256=command.SignedReport.Sha256; Record.ReportFileName=command.SignedReport.FileName;
            Record.State=CopiersMaintenanceV2WorkflowState.ReadyToSend; Record.EmailState=CopiersMaintenanceV2EmailState.Pending; Record.ServerFinalizedAtUtc=command.ServerFinalizedAtUtc; PublishCalls++; return Task.FromResult(Record); }
        public Task<CopiersMaintenanceV2DraftRecord> MarkFinalizationFailedAsync(CopiersMaintenanceV2FinalizationFailedCommand command,CancellationToken ct=default)
        { FailedCalls++; Record.State=CopiersMaintenanceV2WorkflowState.Failed; Record.EmailState=CopiersMaintenanceV2EmailState.NotReady; return Task.FromResult(Record); }
    }
    private sealed class AppReadSpy : ICopiersMtoV2ApplicationDataverseClient
    {
        public string Response="{\"value\":[]}"; public HttpStatusCode Status=HttpStatusCode.OK; public List<string> Reads { get; }=[];
        public Task<HttpResponseMessage> SendAsync(string url,HttpMethod method,HttpContent? content,Action<HttpRequestMessage>? customize,CancellationToken ct=default)
        { Assert.Equal(HttpMethod.Get,method); Assert.Null(content); Reads.Add(url); return Task.FromResult(new HttpResponseMessage(Status) { Content=new StringContent(Response) }); }
    }
    private sealed class ControllerFixture
    {
        public readonly AppReadSpy App=new(); public readonly CopiersMaintenanceV2Options Options=new() { PilotEnabled=true,ActivitiesEnabled=true };
        public readonly CopiersMaintenanceV2DataverseOptions Bindings=CopiersActivityV2LifecycleTests.Bindings();
        public readonly CopiersMtoV2ControllerTests.CopiersDataverseProxy Dataverse; public readonly MaintenanceSpy Maintenance=new();
        public readonly CopiersActivityV2Runtime Runtime; public readonly CopiersMtoV2Controller Controller;
        private readonly Dictionary<string,StringValues> _form=new() { ["ClientId"]=ClientId,["EquipmentId"]=EquipmentId,["ServiceDate"]="2026-09-10",["MaintenanceTypeValue"]="827270001",["CustomerContactName"]="Visitante" };
        public ControllerFixture()
        {
            var service=DispatchProxy.Create<IDataverseService,CopiersMtoV2ControllerTests.CopiersDataverseProxy>(); Dataverse=(CopiersMtoV2ControllerTests.CopiersDataverseProxy)service;
            Dataverse.Clients=[new() { Id=ClientId,Name="Cliente",Email="cliente@example.test" }];
            Runtime=new(App,new HttpContextAccessor(),Microsoft.Extensions.Options.Options.Create(Options),Microsoft.Extensions.Options.Options.Create(Bindings),new PdfSpy([]),new Business([]),new Clock(Now),NullLoggerFactory.Instance);
            Controller=new(service,Maintenance,Microsoft.Extensions.Options.Options.Create(Options),Microsoft.Extensions.Options.Options.Create(Bindings),NullLogger<CopiersMtoV2Controller>.Instance,activities:Runtime)
            { ControllerContext=new() { HttpContext=new DefaultHttpContext { User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name,"tecnico@example.com")],"test")) } } };
            Controller.Request.Headers["Idempotency-Key"]=SubmissionKey; Controller.Request.Form=new FormCollection(_form);
        }
        public void SetForm(string key,string value) { _form[key]=value; Controller.Request.Form=new FormCollection(_form); }
    }
    private sealed class MaintenanceSpy : ICopiersMaintenanceV2Service
    {
        public CopiersMaintenanceV2DraftRequestDto? Draft;
        public Task<CopiersMaintenanceV2DraftResultDto> CreateOrGetDraftAsync(CopiersMaintenanceV2DraftRequestDto request,CopiersMaintenanceV2ActorContext actor,CancellationToken ct=default)
        { Draft=request; return Task.FromResult(new CopiersMaintenanceV2DraftResultDto { RecordId=RecordId,SubmissionKey=SubmissionKey,Version="1" }); }
        public Task<CopiersMaintenanceV2DraftResultDto> SaveDraftAsync(CopiersMaintenanceV2DraftUpdateRequestDto request,CopiersMaintenanceV2ActorContext actor,CancellationToken ct=default) => throw new NotSupportedException();
        public Task<CopiersMaintenanceV2FinalizeResultDto> FinalizeMultipartAsync(CopiersMaintenanceV2FinalizeMultipartRequestDto request,CopiersMaintenanceV2ActorContext actor,CancellationToken ct=default)
            => Task.FromResult(new CopiersMaintenanceV2FinalizeResultDto { RecordId=RecordId,SubmissionKey=SubmissionKey,State=CopiersMaintenanceV2WorkflowState.ReadyToSend,EmailState=CopiersMaintenanceV2EmailState.Pending });
    }
}
