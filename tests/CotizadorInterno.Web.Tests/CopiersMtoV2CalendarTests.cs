using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CopiersMtoV2CalendarTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EquipmentReceiptsShowCorrectCalendarTypeAndInternalLinkWithoutRawJson(bool internalOnly)
    {
        var f=new Fixture(activities:true);f.AddActivity(CopiersActivityV2Bindings.MovementType);
        var operation=new CotizadorInterno.Web.Models.CopiersMtoV2.CopiersEquipmentOperation {
            Kind="receipt",Internal=internalOnly,PendingKey=EvidenceId,DestinationName="Destino previsto",
            Equipment=new(){OriginName="Origen interno"}
        };
        f.ActivityRows[0][f.Options.AnswersJsonField]=JsonSerializer.Serialize(CopiersActivityV2Capture.OperationAnswers(operation),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if(internalOnly)
        {
            f.ActivityRows[0][f.Options.EmailStateField]=f.Options.EmailNotReadyStateValue;
            f.ActivityRows[0][f.Options.SignedReportEvidenceKeyField]="";
            f.ActivityRows[0][f.Options.SignatureEvidenceKeyField]="";
        }
        var week=await f.Service.WeekAsync("all","2026-09-08");
        var item=Assert.Single(week.Events,x=>x.ActivityKind=="movement");
        Assert.Equal(internalOnly?"Recepción interna":"Recepción de equipo",item.MaintenanceType);
        Assert.Equal(internalOnly?"NotRequired":"Pending",item.EmailState);
        var detail=await f.Service.DetailAsync("activity:"+Ticket);
        Assert.Equal(internalOnly,detail.InternalOperation);
        Assert.Equal("activity:"+EvidenceId,detail.RelatedActivityId);
        Assert.Contains(detail.MovementDetails,x=>x.Label=="Origen interno"&&x.Value=="Origen interno");
        Assert.DoesNotContain(detail.Answers,x=>x.Key=="equipment_operation");
        if(internalOnly)Assert.Empty(detail.ReportUrl);
    }
    private const string Technician = "7b5d74cb-7da1-473c-bd1a-66635c10d42a";
    private const string Ticket = "bd456109-5a50-4aa8-b11e-45f3efb11be1";
    private const string EvidenceId = "6b00cafd-17b5-418a-b3d5-98dba18989a4";
    private static readonly string EvidenceKey = new('a', 64);

    [Fact]
    public void ControllerRequiresDashboardAndNeverCaches()
    {
        var module = Assert.Single(typeof(CopiersMtoV2CalendarController).GetCustomAttributes<ModuleAuthorizeAttribute>());
        Assert.Equal(AppModule.Dashboard, Assert.Single(module.Arguments!));
        var cache = Assert.Single(typeof(CopiersMtoV2CalendarController).GetCustomAttributes<ResponseCacheAttribute>());
        Assert.True(cache.NoStore);
        Assert.Equal(ResponseCacheLocation.None, cache.Location);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task EveryEndpointRequiresAuthenticatedDashboardAndCopiersBeforeAppOnlyRead(bool authenticated, bool dashboard, bool copiers)
    {
        var f = new Fixture();
        if (!authenticated) f.Context.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity());
        if (!dashboard) f.Users.Current.ModuleOptionValues.Remove(AppModuleCatalog.Dashboard.OptionValue);
        if (!copiers) f.Users.Current.ModuleOptionValues.Remove(AppModuleCatalog.Copiers.OptionValue);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.BootstrapAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.WeekAsync(Technician, "2026-09-08"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.WeekAsync("all", "2026-09-08"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.DetailAsync(Ticket));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.EvidenceAsync(Ticket, EvidenceKey));
        Assert.Empty(f.Transport.Requests);
    }

    [Fact]
    public async Task AllTechniciansIncludesSentVisitByAnotherTechnicianAndKeepsFailedSignedHistory()
    {
        var f = new Fixture();
        f.Main[f.Options.WorkflowStateField] = f.Options.FailedStateValue;
        var sent = (JsonObject)f.Main.DeepClone();
        sent[f.Options.MainIdField] = "bbbbbbbb-1111-2222-3333-444444444444";
        sent[f.Options.TechnicianUserIdField] = "cccccccc-1111-2222-3333-444444444444";
        sent[f.Options.TechnicianNameField] = "Otro técnico";
        sent[f.Options.ServiceDateField] = "2026-09-10T00:00:00Z";
        sent[f.Options.WorkflowStateField] = f.Options.ReadyToSendStateValue;
        sent[f.Options.EmailStateField] = f.Options.EmailSentStateValue;
        sent[f.Options.DeviceSignedAtUtcField] = "2026-09-10T13:11:13Z";
        sent[f.Options.ServerFinalizedAtUtcField] = "2026-09-10T13:11:43Z";
        sent[f.Options.AnswersJsonField] = "[]";
        f.AdditionalMainRows.Add(sent);

        var all = await f.Service.WeekAsync("all", "2026-09-10");
        Assert.Equal(2, all.Events.Count);
        Assert.Contains(all.Events, x => x.WorkflowState == "Failed");
        Assert.Contains(all.Events, x => x.TechnicianId == "cccccccc-1111-2222-3333-444444444444" && x.EmailState == "Sent");
        Assert.DoesNotContain("dtc_technicianuserkey eq", Uri.UnescapeDataString(Assert.Single(f.Transport.Requests)));
        Assert.Equal(Technician, Assert.Single((await f.Service.WeekAsync(Technician, "2026-09-10")).Events).TechnicianId);
    }

    [Fact]
    public async Task AllTechniciansStillExcludesDraftAndOutOfWeekRows()
    {
        var f = new Fixture();
        var draft = (JsonObject)f.Main.DeepClone();
        draft[f.Options.WorkflowStateField] = f.Options.DraftStateValue;
        f.AdditionalMainRows.Add(draft);
        var outside = (JsonObject)f.Main.DeepClone();
        outside[f.Options.ServiceDateField] = "2026-09-14T00:00:00Z";
        f.AdditionalMainRows.Add(outside);
        Assert.Single((await f.Service.WeekAsync("ALL", "2026-09-08")).Events);
    }

    [Fact]
    public async Task CanonicalEntryAndExitAreUsedInsteadOfLocalizedDisplayOrLaterSignature()
    {
        var f = new Fixture();
        f.Main[f.Options.AnswersJsonField] = JsonSerializer.Serialize(new[] {
            new { key = "service_started_at", label = "Hora de entrada", value = "8 sept 2026, 8:00 a. m." },
            new { key = "service_started_at_utc", label = "Hora de entrada UTC", value = "2026-09-08T09:15:00-05:00" },
            new { key = "service_ended_at_utc", label = "Hora de salida UTC", value = "2026-09-08T14:50:00Z" }
        });
        var entry = Assert.Single((await f.Service.WeekAsync(Technician, "2026-09-08")).Events);
        Assert.Equal(DateTimeOffset.Parse("2026-09-08T14:15:00Z"), entry.StartAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-09-08T14:50:00Z"), entry.EndAtUtc);
        Assert.False(entry.DurationEstimated);
        Assert.Contains("entrada hasta la salida", entry.TimingNote);
        var detail = await f.Service.DetailAsync(Ticket);
        Assert.Equal(entry.EndAtUtc, detail.EndAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-09-08T15:00:00Z"), detail.DeviceSignedAtUtc);
    }

    [Theory]
    [InlineData("2026-09-08T13:00:00")]
    [InlineData("not-a-date")]
    [InlineData("1999-09-08T13:00:00Z")]
    public async Task InvalidCanonicalInstantFallsBackToHistoricalBogotaVisitAndSignature(string value)
    {
        var f = new Fixture();
        f.Main[f.Options.AnswersJsonField] = JsonSerializer.Serialize(new[] {
            new { key = "service_started_at", label = "Inicio de visita", value = "8 sept 2026, 9:15 a. m." },
            new { key = "service_started_at_utc", label = "Inicio UTC", value },
            new { key = "service_ended_at_utc", label = "Salida UTC", value }
        });
        var entry = Assert.Single((await f.Service.WeekAsync(Technician, "2026-09-08")).Events);
        Assert.Equal(DateTimeOffset.Parse("2026-09-08T14:15:00Z"), entry.StartAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-09-08T15:00:00Z"), entry.EndAtUtc);
        Assert.False(entry.DurationEstimated);
    }

    [Fact]
    public async Task WeekUsesMondayDateOnlyRangeAndRecordedBogotaVisit()
    {
        var f = new Fixture();
        var result = await f.Service.WeekAsync(Technician.ToUpperInvariant(), "2026-09-08");
        Assert.Equal("2026-09-07", result.WeekStart);
        Assert.Equal("America/Bogota", result.TimeZone);
        var entry = Assert.Single(result.Events);
        Assert.Equal("Preventivo", entry.MaintenanceType);
        Assert.Equal("Pending", entry.EmailState);
        Assert.Equal(DateTimeOffset.Parse("2026-09-08T14:15:00Z"), entry.StartAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-09-08T15:00:00Z"), entry.EndAtUtc);
        Assert.False(entry.DurationEstimated);
        var request = Uri.UnescapeDataString(Assert.Single(f.Transport.Requests));
        Assert.Contains("dtc_servicedate ge 2026-09-07T00:00:00Z", request);
        Assert.Contains("dtc_servicedate lt 2026-09-14T00:00:00Z", request);
        Assert.Contains($"dtc_technicianuserkey eq '{Technician}'", request);
        Assert.DoesNotContain("latitude", request);
        Assert.DoesNotContain("internalnotes", request);
    }

    [Theory]
    [InlineData("8 sept 2026, 9:15 a. m.", "2026-09-08T14:15:00Z")]
    [InlineData("8 sept 2026, 12:15 a. m.", "2026-09-08T05:15:00Z")]
    [InlineData("8 sept 2026, 2:15 p. m.", "2026-09-08T19:15:00Z")]
    public async Task VisitStartParsesActualSpanishBrowserDisplay(string start, string expected)
    {
        var f = new Fixture();
        f.Main[f.Options.AnswersJsonField] = JsonSerializer.Serialize(new[] { new { key = "service_started_at", label = "Inicio de visita", value = start } });
        f.Main[f.Options.DeviceSignedAtUtcField] = "2026-09-08T20:00:00Z";
        var result = Assert.Single((await f.Service.WeekAsync(Technician, "2026-09-08")).Events);
        Assert.False(result.DurationEstimated);
        Assert.Equal(DateTimeOffset.Parse(expected), result.StartAtUtc);
    }

    [Fact]
    public async Task MissingVisitStartUsesExplicitVisualDurationWithoutInventingActualDuration()
    {
        var f = new Fixture();
        f.Main[f.Options.AnswersJsonField] = "[]";
        var entry = Assert.Single((await f.Service.WeekAsync(Technician, "2026-09-08")).Events);
        Assert.True(entry.DurationEstimated);
        Assert.Equal(TimeSpan.FromMinutes(30), entry.EndAtUtc - entry.StartAtUtc);
        Assert.Contains("duración real no disponible", entry.TimingNote);
    }

    [Fact]
    public async Task WeekDefensivelyRejectsAnotherTechnicianAndOutOfWeekEvenWhenUpstreamIgnoresFilter()
    {
        var f = new Fixture();
        f.Main[f.Options.TechnicianUserIdField] = Guid.NewGuid().ToString("D");
        Assert.Empty((await f.Service.WeekAsync(Technician, "2026-09-08")).Events);
        f.Main[f.Options.TechnicianUserIdField] = Technician;
        f.Main[f.Options.ServiceDateField] = "2026-09-14T00:00:00Z";
        Assert.Empty((await f.Service.WeekAsync(Technician, "2026-09-08")).Events);
    }

    [Theory]
    [InlineData("bad-id", "2026-09-08")]
    [InlineData(Technician, "2026-22-99")]
    [InlineData(Technician, "1999-01-01")]
    public async Task InvalidInputNeverReachesAppOnlyTransport(string technician, string week)
    {
        var f = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.WeekAsync(technician, week));
        Assert.Empty(f.Transport.Requests);
    }

    [Fact]
    public async Task DetailIncludesInternalLocationAndCompleteSavedAnswersWithProtectedFileUrls()
    {
        var f = new Fixture();
        var detail = await f.Service.DetailAsync(Ticket);
        Assert.Equal("MTO-001234", detail.ServiceReference);
        Assert.Equal("Revisión y limpieza", detail.WorkPerformed);
        Assert.Equal("Seguimiento interno", detail.InternalNotes);
        Assert.Equal("Persona que firma", detail.SignerName);
        Assert.True(detail.CustomerAccepted);
        Assert.Equal(4.711, detail.Location!.Latitude);
        Assert.Equal(-74.072, detail.Location.Longitude);
        Assert.Equal(13.5, detail.Location.AccuracyMeters);
        Assert.Equal("service_started_at", Assert.Single(detail.Answers).Key);
        Assert.StartsWith($"/CopiersMtoV2Calendar/Evidence?id={Ticket}&evidenceKey=", detail.ReportUrl);
        Assert.Equal("SignedReport", Assert.Single(detail.Evidences).Purpose);
    }

    [Theory]
    [InlineData(100, -74)]
    [InlineData(4, -181)]
    public async Task InvalidCoordinatesAreNotTurnedIntoMapPoints(double latitude, double longitude)
    {
        var f = new Fixture();
        f.Main[f.Options.LatitudeField] = latitude;
        f.Main[f.Options.LongitudeField] = longitude;
        Assert.Null((await f.Service.DetailAsync(Ticket)).Location);
    }

    [Fact]
    public async Task FailedSignedAttemptIsVisibleButNeverReportedAsCompletedOrSent()
    {
        var f = new Fixture();
        f.Main[f.Options.WorkflowStateField] = f.Options.FailedStateValue;
        f.Main[f.Options.EmailStateField] = f.Options.EmailNotReadyStateValue;
        var detail = await f.Service.DetailAsync(Ticket);
        Assert.Equal("Failed", detail.WorkflowState);
        Assert.Equal("NotReady", detail.EmailState);
        Assert.NotEmpty(detail.ReportUrl);
        Assert.Equal("Failed", Assert.Single((await f.Service.WeekAsync(Technician, "2026-09-08")).Events).WorkflowState);
        f.Main[f.Options.SignedReportEvidenceKeyField] = null;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.DetailAsync(Ticket));
    }

    [Fact]
    public async Task DraftCannotBeReadEvenByDashboardAuthorizedUser()
    {
        var f = new Fixture();
        f.Main[f.Options.WorkflowStateField] = f.Options.DraftStateValue;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.DetailAsync(Ticket));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.EvidenceAsync(Ticket, EvidenceKey));
        Assert.DoesNotContain(f.Transport.Requests, x => x.Contains("/$value"));
    }

    [Fact]
    public async Task EvidenceVerifiesParentHashMimeSizeAndStableEtag()
    {
        var f = new Fixture();
        var file = await f.Service.EvidenceAsync(Ticket, EvidenceKey);
        Assert.Equal(f.Bytes, file.Content);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("MTO-001234.pdf", file.FileName);
        Assert.Equal(1, f.Transport.Requests.Count(x => x.EndsWith("/$value")));
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("hash")]
    [InlineData("mime")]
    [InlineData("size")]
    [InlineData("etag")]
    [InlineData("bytes")]
    public async Task EvidenceFailsClosedWhenOwnershipOrIntegrityChanges(string mutation)
    {
        var f = new Fixture();
        switch (mutation)
        {
            case "parent": f.Evidence[$"_{f.Options.EvidenceParentLookupLogicalName}_value"] = Guid.NewGuid().ToString("D"); break;
            case "hash": f.Evidence[f.Options.EvidenceSha256Field] = new string('b', 64); break;
            case "mime": f.Evidence[f.Options.EvidenceContentTypeField] = "text/html"; break;
            case "size": f.Evidence[f.Options.EvidenceSizeField] = 20; break;
            case "etag": f.Transport.MutateOnFileRead = () => f.Evidence["@odata.etag"] = "W/\"3\""; break;
            case "bytes": f.Transport.MutateOnFileRead = () => f.Bytes[5] ^= 1; break;
        }
        await Assert.ThrowsAnyAsync<Exception>(() => f.Service.EvidenceAsync(Ticket, EvidenceKey));
    }

    [Fact]
    public async Task ForeignContinuationNeverReceivesAppOnlyCredentials()
    {
        var f = new Fixture();
        f.Transport.NextLink = "https://attacker.example/api/data/v9.2/dtc_copiersmtov2s?$skiptoken=x";
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.WeekAsync(Technician, "2026-09-08"));
        Assert.Single(f.Transport.Requests);
    }

    [Fact]
    public async Task RepeatedContinuationFailsBoundedly()
    {
        var f = new Fixture();
        f.Transport.NextLink = "https://orgc79ca19c.crm2.dynamics.com/api/data/v9.2/dtc_copiersmtov2s?$skiptoken=x";
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.WeekAsync(Technician, "2026-09-08"));
        Assert.Equal(2, f.Transport.Requests.Count);
    }

    [Fact]
    public async Task BootstrapUsesCopiersEmployeesAndHistoricalTechniciansWithoutLeakingPermissions()
    {
        var f = new Fixture();
        f.Users.Employees = [
            new() { IsActive = true, SystemUserId = Technician, EmployeeName = "Técnico registrado", ModuleOptionValues = [AppModuleCatalog.Copiers.OptionValue] },
            new() { IsActive = true, SystemUserId = Guid.NewGuid().ToString("D"), EmployeeName = "No autorizado", ModuleOptionValues = [] }
        ];
        var result = await f.Service.BootstrapAsync();
        Assert.Equal("all", result.DefaultTechnicianId);
        Assert.Equal("Técnico registrado", Assert.Single(result.Technicians).Name);
        Assert.Contains("groupby", Uri.UnescapeDataString(Assert.Single(f.Transport.Requests)));
    }

    [Theory]
    [InlineData(827270010, "movement", "Movimiento")]
    [InlineData(827270011, "toner", "Entrega de tóner")]
    public async Task CalendarAggregatesRealActivityHeaderWithoutChangingMaintenanceIdentity(int type, string kind, string label)
    {
        var f = new Fixture(activities: true);
        f.AddActivity(type);
        var week = await f.Service.WeekAsync("all", "2026-09-08");
        Assert.Equal(2, week.Events.Count);
        Assert.Equal("maintenance", week.Events.Single(x => x.Id == Ticket).ActivityKind);
        var activity = week.Events.Single(x => x.Id == "activity:" + Ticket);
        Assert.Equal(kind, activity.ActivityKind);
        Assert.Equal(label, activity.MaintenanceType);
        Assert.Equal("ACT-001234", activity.ServiceReference);
        Assert.Equal(2, f.Transport.Requests.Count);
        Assert.Contains(f.Transport.Requests, x => x.Contains(f.ActivityOptions.MainEntitySetName));
    }

    [Fact]
    public async Task ActivityDetailAndEvidenceUseOnlyActivityTableLookupAndPrefixedUrls()
    {
        var f = new Fixture(activities: true);
        f.AddActivity(827270010);
        var detail = await f.Service.DetailAsync("activity:" + Ticket);
        Assert.Equal("activity:" + Ticket, detail.Id);
        Assert.Equal("movement", detail.ActivityKind);
        Assert.NotNull(detail.Location);
        Assert.Contains("id=activity%3A" + Ticket, detail.ReportUrl);
        Assert.Equal(f.Bytes, (await f.Service.EvidenceAsync("activity:" + Ticket, EvidenceKey)).Content);
        Assert.DoesNotContain(f.Transport.Requests, x => x.Contains(f.Options.MainEntitySetName) || x.Contains(f.Options.EvidenceEntitySetName));
        Assert.Contains(f.Transport.Requests, x => Uri.UnescapeDataString(x).Contains("_dtc_signedactivity_value eq " + Ticket));
    }

    [Fact]
    public async Task ActivityEvidenceCannotAdoptTheOldMaintenanceParentLookup()
    {
        var f = new Fixture(activities: true);
        f.AddActivity(827270011);
        f.ActivityEvidence!.Remove("_dtc_signedactivity_value");
        f.ActivityEvidence["_dtc_signedmto_value"] = Ticket;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.EvidenceAsync("activity:" + Ticket, EvidenceKey));
        Assert.DoesNotContain(f.Transport.Requests, x => x.EndsWith("/$value"));
    }

    [Fact]
    public async Task DisabledActivityFeatureNeverReadsItsUnprovisionedTable()
    {
        var f = new Fixture();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.DetailAsync("activity:" + Ticket));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.EvidenceAsync("activity:" + Ticket, EvidenceKey));
        Assert.Empty(f.Transport.Requests);
        await f.Service.WeekAsync("all", "2026-09-08");
        Assert.Single(f.Transport.Requests);
    }

    [Fact]
    public async Task EnabledActivitiesStillRequireDashboardAndCopiersBeforeAnyApplicationRead()
    {
        var f = new Fixture(activities: true);
        f.Users.Current.ModuleOptionValues.Remove(AppModuleCatalog.Dashboard.OptionValue);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.WeekAsync("all", "2026-09-08"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.DetailAsync("activity:" + Ticket));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.EvidenceAsync("activity:" + Ticket, EvidenceKey));
        Assert.Empty(f.Transport.Requests);
    }

    [Theory]
    [InlineData("activity:not-a-guid")]
    [InlineData("activity:activity:bd456109-5a50-4aa8-b11e-45f3efb11be1")]
    [InlineData("other:bd456109-5a50-4aa8-b11e-45f3efb11be1")]
    [InlineData("activity:{bd456109-5a50-4aa8-b11e-45f3efb11be1}")]
    public async Task MalformedActivityReferencesNeverReachDataverse(string id)
    {
        var f = new Fixture(activities: true);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.DetailAsync(id));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.EvidenceAsync(id, EvidenceKey));
        Assert.Empty(f.Transport.Requests);
    }

    [Fact]
    public async Task ActivityHistoricalTechnicianAppearsButUnknownActivityTypesDoNotMasqueradeAsToner()
    {
        var f = new Fixture(activities: true);
        f.AddActivity(827270099);
        var other = Guid.NewGuid().ToString("D");
        f.ActivityRows[0][f.Options.TechnicianUserIdField] = other;
        f.ActivityRows[0][f.Options.TechnicianNameField] = "Técnico histórico de entregas";
        var bootstrap = await f.Service.BootstrapAsync();
        Assert.True(bootstrap.ActivitiesEnabled);
        Assert.Contains(bootstrap.Technicians, x => x.Id == other);
        Assert.Single((await f.Service.WeekAsync("all", "2026-09-08")).Events);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.DetailAsync("activity:" + Ticket));
    }

    private sealed class Fixture
    {
        public Fixture(bool activities = false, [CallerFilePath] string path = "")
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", ".."));
            var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(root, "appsettings.json"))
                .AddInMemoryCollection(new Dictionary<string, string?> { ["CopiersMtoV2:ActivitiesEnabled"] = activities.ToString() }).Build();
            Options = configuration.GetSection(CopiersMaintenanceV2DataverseOptions.SectionName).Get<CopiersMaintenanceV2DataverseOptions>()!;
            var service = DispatchProxy.Create<IDataverseService, UserProxy>();
            Users = (UserProxy)service;
            Context.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "test@example.com")], "test")) };
            Main = new()
            {
                [Options.MainIdField] = Ticket, [Options.ServiceReferenceField] = "MTO-001234", [Options.TechnicianUserIdField] = Technician,
                [Options.TechnicianNameField] = "Técnico", [Options.TechnicianEmailField] = "test@example.com", [Options.ClientNameField] = "Cliente de prueba",
                [Options.ServiceDateField] = "2026-09-08T00:00:00Z", [Options.WorkflowStateField] = Options.ReadyToSendStateValue,
                [Options.EmailStateField] = Options.EmailPendingStateValue, [Options.MaintenanceTypeField] = Options.MaintenanceTypePreventiveValue,
                [Options.AnswersJsonField] = "[{\"key\":\"service_started_at\",\"label\":\"Inicio de visita\",\"value\":\"8 sept 2026, 9:15 a. m.\"}]",
                [Options.DeviceSignedAtUtcField] = "2026-09-08T15:00:00Z", [Options.ServerFinalizedAtUtcField] = "2026-09-08T15:01:00Z",
                [Options.SignedReportEvidenceKeyField] = EvidenceKey, [Options.SignedReportSha256Field] = Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant(),
                [Options.WorkPerformedField] = "Revisión y limpieza", [Options.InternalNotesField] = "Seguimiento interno",
                [Options.SignerNameField] = "Persona que firma", [Options.CustomerAcceptedField] = true,
                [Options.LatitudeField] = 4.711, [Options.LongitudeField] = -74.072, [Options.AccuracyMetersField] = 13.5,
                [Options.LocationSourceField] = "navigator.geolocation", [Options.LocationCapturedAtUtcField] = "2026-09-08T15:00:00Z"
            };
            Evidence = new()
            {
                ["@odata.etag"] = "W/\"2\"", [Options.EvidenceIdField] = EvidenceId, [Options.EvidenceKeyField] = EvidenceKey,
                [$"_{Options.EvidenceParentLookupLogicalName}_value"] = Ticket, [Options.EvidencePurposeField] = Options.EvidenceSignedReportPurposeValue,
                [Options.EvidenceContentTypeField] = "application/pdf", [Options.EvidenceSizeField] = Bytes.Length,
                [Options.EvidenceSha256Field] = Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant(),
                [Options.EvidenceOriginalFileNameField] = "MTO-001234.pdf", [Options.EvidenceSequenceField] = 0
            };
            Transport = new(this);
            Service = new(Transport, service, Context, Microsoft.Extensions.Options.Options.Create(Options), configuration);
        }
        public CopiersMaintenanceV2DataverseOptions Options { get; }
        public CopiersMaintenanceV2DataverseOptions ActivityOptions => CopiersActivityV2Bindings.Create(Options);
        public UserProxy Users { get; }
        public HttpContextAccessor Context { get; } = new();
        public JsonObject Main { get; }
        public List<JsonObject> AdditionalMainRows { get; } = [];
        public List<JsonObject> ActivityRows { get; } = [];
        public JsonObject? ActivityEvidence { get; private set; }
        public JsonObject Evidence { get; }
        public byte[] Bytes { get; } = Encoding.ASCII.GetBytes("%PDF-1.4\nTest fixture PDF bytes\n%%EOF");
        public Transport Transport { get; }
        public CopiersMtoV2CalendarService Service { get; }
        public void AddActivity(int type)
        {
            var activity = (JsonObject)Main.DeepClone();
            activity.Remove(Options.MainIdField);
            activity[ActivityOptions.MainIdField] = Ticket;
            activity[Options.ServiceReferenceField] = "ACT-001234";
            activity[Options.MaintenanceTypeField] = type;
            ActivityRows.Add(activity);
            ActivityEvidence = (JsonObject)Evidence.DeepClone();
            ActivityEvidence.Remove(Options.EvidenceIdField);
            ActivityEvidence.Remove("_" + Options.EvidenceParentLookupLogicalName + "_value");
            ActivityEvidence[ActivityOptions.EvidenceIdField] = EvidenceId;
            ActivityEvidence["_" + ActivityOptions.EvidenceParentLookupLogicalName + "_value"] = Ticket;
        }
    }

    public class UserProxy : DispatchProxy
    {
        public CurrentUserInfo Current { get; } = new() { SystemUserId = Technician, DisplayName = "Técnico", Email = "test@example.com",
            ModuleOptionValues = [AppModuleCatalog.Dashboard.OptionValue, AppModuleCatalog.Copiers.OptionValue] };
        public IReadOnlyList<EmployeeModulePermissionRowDto> Employees { get; set; } = [];
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IDataverseService.GetCurrentUserAsync) => Task.FromResult<CurrentUserInfo?>(Current),
            nameof(IDataverseService.GetEmployeeModulePermissionsAsync) => Task.FromResult(Employees),
            _ => throw new NotSupportedException(targetMethod?.Name)
        };
    }

    private sealed class Transport(Fixture fixture) : ICopiersMtoV2ApplicationDataverseClient
    {
        public List<string> Requests { get; } = [];
        public string? NextLink { get; set; }
        public Action? MutateOnFileRead { get; set; }
        public Task<HttpResponseMessage> SendAsync(string relativeUrl, HttpMethod method, HttpContent? content,
            Action<HttpRequestMessage>? customizeRequest, CancellationToken ct = default)
        {
            Assert.Equal(HttpMethod.Get, method);
            Assert.Null(content);
            Requests.Add(relativeUrl);
            if (relativeUrl.EndsWith("/$value"))
            {
                MutateOnFileRead?.Invoke();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture.Bytes) });
            }
            JsonObject result;
            if (relativeUrl.Contains(fixture.ActivityOptions.EvidenceEntitySetName))
                result = relativeUrl.Contains($"{fixture.ActivityOptions.EvidenceEntitySetName}(")
                    ? fixture.ActivityEvidence! : new() { ["value"] = fixture.ActivityEvidence is null ? new JsonArray() : new JsonArray(fixture.ActivityEvidence.DeepClone()) };
            else if (relativeUrl.Contains(fixture.ActivityOptions.MainEntitySetName))
                result = relativeUrl.Contains($"{fixture.ActivityOptions.MainEntitySetName}(")
                    ? fixture.ActivityRows.Single() : new() { ["value"] = new JsonArray(fixture.ActivityRows.Select(x => x.DeepClone()).ToArray()) };
            else if (relativeUrl.Contains($"{fixture.Options.EvidenceEntitySetName}(")) result = fixture.Evidence;
            else if (relativeUrl.Contains($"{fixture.Options.MainEntitySetName}(")) result = fixture.Main;
            else
            {
                var row = relativeUrl.Contains(fixture.Options.EvidenceEntitySetName) ? fixture.Evidence : fixture.Main;
                var rows = new JsonArray(row.DeepClone());
                if (!relativeUrl.Contains(fixture.Options.EvidenceEntitySetName))
                    foreach (var additional in fixture.AdditionalMainRows) rows.Add(additional.DeepClone());
                result = new() { ["value"] = rows };
                if (NextLink is not null) result["@odata.nextLink"] = NextLink;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(result.ToJsonString(), Encoding.UTF8, "application/json") });
        }
    }
}
