using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CopiersMtoV2ControllerTests
{
    private const string ClientId = "81522aa1-96b9-45e8-937a-b5f0d920dc46";
    private const string OtherClientId = "ab34c33a-cf9f-4d80-9563-0dc8c0be9ef8";
    private const string EquipmentId = "58e07029-6d84-425c-aee0-17b5d8f14a32";
    private const string SubmissionKey = "mto-controller-test-20260908";

    [Fact]
    public void ControllerRetainsCopiersModuleAuthorization()
    {
        var authorization = Assert.Single(typeof(CopiersMtoV2Controller)
            .GetCustomAttributes<ModuleAuthorizeAttribute>());
        Assert.Equal(AppModule.Copiers, Assert.IsType<AppModule>(Assert.Single(authorization.Arguments!)));
    }

    [Fact]
    public async Task BootstrapReturnsDedicatedClientCatalogWhenPilotIsDisabledAndAllowlistIsEmpty()
    {
        var fixture = new Fixture(pilotEnabled: false);
        fixture.Dataverse.Clients =
        [
            new() { Id = OtherClientId, Name = "Cliente sin equipos", Email = "" },
            Client()
        ];
        fixture.Dataverse.Dashboard.ClientSummaries =
        [
            new() { ClientId = Guid.NewGuid().ToString("D"), ClientName = "Resumen ajeno", Email = "viejo@example.com" }
        ];

        var result = Assert.IsType<OkObjectResult>(await fixture.Controller.Bootstrap(default));
        var bootstrap = Assert.IsType<CopiersMtoV2BootstrapDto>(result.Value);

        Assert.Equal(2, bootstrap.Clients.Count);
        Assert.Contains(bootstrap.Clients, item => item.Id == OtherClientId && item.Email == "");
        Assert.Contains(bootstrap.Clients, item => item.Id == ClientId && item.Email == "copiers@example.com");
        Assert.DoesNotContain(bootstrap.Clients, item => item.Name == "Resumen ajeno");
        Assert.Equal(1, fixture.Dataverse.ClientCatalogReadCount);
        Assert.Equal(EquipmentId, Assert.Single(bootstrap.Equipment).Id);
        Assert.Equal("Técnico autorizado", bootstrap.TechnicianName);
        Assert.Equal(0, fixture.Service.CreateCount);
    }

    [Fact]
    public async Task BootstrapStillRejectsTechnicianOutsideAnExplicitAllowlist()
    {
        var fixture = new Fixture(pilotEnabled: false);
        fixture.Options.AllowedTechnicianEmails = ["otro.tecnico@example.com"];

        Assert.IsType<ForbidResult>(await fixture.Controller.Bootstrap(default));
    }

    [Fact]
    public async Task BootstrapAppliesClientAllowlistAndExcludesUnassignedEquipment()
    {
        var fixture = new Fixture(pilotEnabled: false);
        fixture.Options.AllowedClientIds = [ClientId.ToUpperInvariant()];
        fixture.Dataverse.Clients = [Client(), new() { Id = OtherClientId, Name = "Otro cliente" }];
        fixture.Dataverse.Dashboard.EquipmentRows =
        [
            Equipment(),
            new() { RecordId = Guid.NewGuid().ToString("D"), ClientId = ClientId, Serial = "STOCK", InStock = true },
            new() { RecordId = Guid.NewGuid().ToString("D"), ClientId = OtherClientId, Serial = "OTRO" }
        ];

        var result = Assert.IsType<OkObjectResult>(await fixture.Controller.Bootstrap(default));
        var bootstrap = Assert.IsType<CopiersMtoV2BootstrapDto>(result.Value);

        Assert.Equal(ClientId, Assert.Single(bootstrap.Clients).Id);
        Assert.Equal(EquipmentId, Assert.Single(bootstrap.Equipment).Id);
    }

    [Theory]
    [InlineData("", "customer_email_required")]
    [InlineData("   ", "customer_email_required")]
    [InlineData("no-es-un-correo", "customer_email_invalid")]
    [InlineData("uno@example.com;dos@example.com", "customer_email_invalid")]
    public async Task SaveClientEmailRejectsEmptyOrInvalidEmailBeforeWriting(string email, string expectedCode)
    {
        var fixture = new Fixture(pilotEnabled: false);

        var result = Assert.IsType<BadRequestObjectResult>(await fixture.Controller.SaveClientEmail(
            new() { ClientId = ClientId, Email = email }, default));

        Assert.Equal(expectedCode, Property(result.Value, "code"));
        Assert.Equal(0, fixture.Dataverse.EmailSaveCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-es-un-id")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task SaveClientEmailRejectsInvalidClientBeforeWriting(string clientId)
    {
        var fixture = new Fixture(pilotEnabled: false);

        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.SaveClientEmail(
            new() { ClientId = clientId, Email = "copiers@example.com" }, default));
        Assert.Equal(0, fixture.Dataverse.EmailSaveCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("anterior@example.com")]
    public async Task SaveClientEmailSupportsAddingOrEditingAndReturnsPersistedValue(string previousEmail)
    {
        var fixture = new Fixture(pilotEnabled: false);
        fixture.Dataverse.Clients = [Client(previousEmail)];
        fixture.Dataverse.SavedClient = Client("normalizado@example.com");

        var result = Assert.IsType<OkObjectResult>(await fixture.Controller.SaveClientEmail(
            new() { ClientId = ClientId, Email = "NORMALIZADO@example.com" }, default));

        Assert.Equal(1, fixture.Dataverse.EmailSaveCount);
        Assert.Equal(ClientId, fixture.Dataverse.LastSavedClientId);
        Assert.Equal("NORMALIZADO@example.com", fixture.Dataverse.LastSavedEmail);
        Assert.Equal(ClientId, Property(result.Value, "clientId"));
        Assert.Equal("normalizado@example.com", Property(result.Value, "email"));
    }

    [Fact]
    public async Task SaveClientEmailRejectsClientOutsideExplicitAllowlistBeforeWriting()
    {
        var fixture = new Fixture(pilotEnabled: false);
        fixture.Options.AllowedClientIds = [OtherClientId];

        Assert.IsType<ForbidResult>(await fixture.Controller.SaveClientEmail(
            new() { ClientId = ClientId, Email = "copiers@example.com" }, default));
        Assert.Equal(0, fixture.Dataverse.EmailSaveCount);
    }

    [Fact]
    public async Task SaveClientEmailReportsConcurrentChangeAsConflict()
    {
        var fixture = new Fixture(pilotEnabled: false);
        fixture.Dataverse.SaveException = new CopiersMaintenanceV2ConcurrencyException("El cliente cambió.");

        Assert.IsType<ConflictObjectResult>(await fixture.Controller.SaveClientEmail(
            new() { ClientId = ClientId, Email = "copiers@example.com" }, default));
    }

    [Fact]
    public async Task FinalizeRejectsEquipmentOfAnotherClientBeforeCreatingDraft()
    {
        var fixture = new Fixture();
        fixture.Dataverse.Dashboard.EquipmentRows = [Equipment(OtherClientId)];

        var result = Assert.IsType<BadRequestObjectResult>(await fixture.Controller.Finalize(Request(), default));

        Assert.Equal("equipment_client_mismatch", Property(result.Value, "code"));
        AssertNoPersistence(fixture);
    }

    [Theory]
    [InlineData("")]
    [InlineData("serial-manual")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task FinalizeRequiresSelectedEquipmentAndDoesNotAcceptManualSerial(string submittedEquipmentId)
    {
        var fixture = new Fixture();
        fixture.SetForm("EquipmentId", submittedEquipmentId);
        fixture.SetForm("EquipmentSerial", "SERIAL-MANUAL");

        var result = Assert.IsType<BadRequestObjectResult>(await fixture.Controller.Finalize(Request(), default));

        Assert.Equal("equipment_required", Property(result.Value, "code"));
        AssertNoPersistence(fixture);
    }

    [Fact]
    public async Task FinalizeRejectsEquipmentThatIsNoLongerAssignedBeforeCreatingDraft()
    {
        var fixture = new Fixture();
        var equipment = Equipment();
        equipment.InStock = true;
        fixture.Dataverse.Dashboard.EquipmentRows = [equipment];

        var result = Assert.IsType<BadRequestObjectResult>(await fixture.Controller.Finalize(Request(), default));

        Assert.Equal("equipment_not_serviceable", Property(result.Value, "code"));
        AssertNoPersistence(fixture);
    }

    [Fact]
    public async Task FinalizeRequiresSavedClientContactEmailAndIgnoresSubmittedEmail()
    {
        var fixture = new Fixture();
        fixture.Dataverse.Clients = [Client("")];
        fixture.SetForm("CustomerEmail", "forged@example.com");

        var result = Assert.IsType<BadRequestObjectResult>(await fixture.Controller.Finalize(Request(), default));

        Assert.Equal("client_email_missing", Property(result.Value, "code"));
        AssertNoPersistence(fixture);
    }

    [Fact]
    public async Task FinalizeUsesAuthoritativeClientEmailAndEquipmentAndGeneratesTitleServerSide()
    {
        var fixture = new Fixture();
        fixture.SetForm("ClientName", "NOMBRE ALTERADO");
        fixture.SetForm("CustomerEmail", "forged@example.com");
        fixture.SetForm("EquipmentSerial", "SERIAL-ALTERADO");
        fixture.SetForm("Title", "ORDEN-MANUAL-99999");
        var request = Request();
        request.RecordId = Guid.NewGuid().ToString("D");
        request.ExpectedVersion = "version-alterada";

        Assert.IsType<OkObjectResult>(await fixture.Controller.Finalize(request, default));

        var draft = Assert.IsType<CopiersMaintenanceV2DraftRequestDto>(fixture.Service.LastDraft);
        Assert.Equal(ClientId, draft.ClientId);
        Assert.Equal("Cliente de catálogo", draft.ClientName);
        Assert.Equal("copiers@example.com", draft.CustomerEmail);
        Assert.Equal(EquipmentId, draft.EquipmentId);
        Assert.Equal("SERIAL-CATALOGO-001", draft.EquipmentSerial);
        Assert.Equal("Persona en la visita", draft.CustomerContactName);
        Assert.Equal(new DateOnly(2026, 9, 8), draft.ServiceDate);
        Assert.Equal("Mantenimiento 2026-09-08 · SERIAL-CATALOGO-001", draft.Title);
        Assert.Equal(fixture.Service.DraftResult.RecordId, fixture.Service.LastFinalization!.RecordId);
        Assert.Equal(fixture.Service.DraftResult.Version, fixture.Service.LastFinalization.ExpectedVersion);
        Assert.Equal(1, fixture.Dataverse.ClientCatalogReadCount);
        Assert.Equal(1, fixture.Service.CreateCount);
        Assert.Equal(1, fixture.Service.FinalizeCount);
    }

    [Fact]
    public async Task FinalizeRefreshesReusedDraftWithCurrentAuthoritativeClientEmail()
    {
        var fixture = new Fixture();
        fixture.Service.DraftResult.ReusedExisting = true;
        fixture.Service.DraftResult.State = CopiersMaintenanceV2WorkflowState.Draft;
        fixture.Dataverse.Clients = [Client("actualizado@example.com")];

        Assert.IsType<OkObjectResult>(await fixture.Controller.Finalize(Request(), default));

        Assert.Equal(1, fixture.Service.SaveCount);
        Assert.Equal("actualizado@example.com", fixture.Service.LastUpdate!.CustomerEmail);
        Assert.Equal("Mantenimiento 2026-09-08 · SERIAL-CATALOGO-001", fixture.Service.LastUpdate.Title);
    }

    [Fact]
    public async Task FinalizeRejectsInvalidIdempotencyHeaderBeforeCatalogOrPersistence()
    {
        var fixture = new Fixture();
        fixture.Controller.Request.Headers["Idempotency-Key"] = "otra-clave";

        var result = Assert.IsType<BadRequestObjectResult>(await fixture.Controller.Finalize(Request(), default));

        Assert.Equal("idempotency_header_invalid", Property(result.Value, "code"));
        Assert.Equal(0, fixture.Dataverse.ClientCatalogReadCount);
        AssertNoPersistence(fixture);
    }

    private static void AssertNoPersistence(Fixture fixture)
    {
        Assert.Equal(0, fixture.Service.CreateCount);
        Assert.Equal(0, fixture.Service.SaveCount);
        Assert.Equal(0, fixture.Service.FinalizeCount);
    }

    private static string? Property(object? value, string name) =>
        JsonSerializer.SerializeToElement(value).GetProperty(name).GetString();

    private static CopiersMaintenanceV2FinalizeMultipartRequestDto Request() => new() { SubmissionKey = SubmissionKey };

    private static CopiersMtoV2ClientOptionDto Client(string email = "copiers@example.com") => new()
    {
        Id = ClientId, Name = "Cliente de catálogo", ContactName = "Contacto de catálogo", Email = email
    };

    private static CopiersEquipmentRowDto Equipment(string clientId = ClientId) => new()
    {
        RecordId = EquipmentId, ClientId = clientId, Serial = "SERIAL-CATALOGO-001",
        ClientName = "Cliente de catálogo", Reference = "Referencia del equipo", InStock = false
    };

    private sealed class Fixture
    {
        private readonly Dictionary<string, StringValues> _form = new()
        {
            ["ClientId"] = ClientId,
            ["EquipmentId"] = EquipmentId,
            ["ServiceDate"] = "2026-09-08",
            ["MaintenanceTypeValue"] = "827270001",
            ["CustomerContactName"] = "Persona en la visita"
        };

        public Fixture(bool pilotEnabled = true)
        {
            var dataverseService = DispatchProxy.Create<IDataverseService, CopiersDataverseProxy>();
            Dataverse = (CopiersDataverseProxy)dataverseService;
            Dataverse.Clients = [Client()];
            Dataverse.Dashboard.EquipmentRows = [Equipment()];
            Options = new CopiersMaintenanceV2Options { PilotEnabled = pilotEnabled };
            Controller = new CopiersMtoV2Controller(dataverseService, Service,
                Microsoft.Extensions.Options.Options.Create(Options),
                Microsoft.Extensions.Options.Options.Create(new CopiersMaintenanceV2DataverseOptions
                {
                    MaintenanceTypeCorrectiveValue = 827270000,
                    MaintenanceTypePreventiveValue = 827270001
                }), NullLogger<CopiersMtoV2Controller>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.Name, "tecnico@example.com")], "test"))
                    }
                }
            };
            Controller.Request.Headers["Idempotency-Key"] = SubmissionKey;
            Controller.Request.Form = new FormCollection(_form);
        }

        public CopiersDataverseProxy Dataverse { get; }
        public RecordingMaintenanceService Service { get; } = new();
        public CopiersMaintenanceV2Options Options { get; }
        public CopiersMtoV2Controller Controller { get; }

        public void SetForm(string name, string value)
        {
            _form[name] = value;
            Controller.Request.Form = new FormCollection(_form);
        }
    }

    public class CopiersDataverseProxy : DispatchProxy
    {
        public CurrentUserInfo CurrentUser { get; set; } = new()
        {
            SystemUserId = "0336949f-67d4-42ca-80fd-ded5ae55fdcf",
            EmployeeName = "Técnico autorizado", Email = "tecnico@example.com",
            ModuleOptionValues = [AppModuleCatalog.Find(AppModule.Copiers)!.OptionValue]
        };
        public IReadOnlyList<CopiersMtoV2ClientOptionDto> Clients { get; set; } = [];
        public CopiersEquipmentDashboardDto Dashboard { get; set; } = new();
        public CopiersMtoV2ClientOptionDto SavedClient { get; set; } = new();
        public Exception? SaveException { get; set; }
        public int ClientCatalogReadCount { get; private set; }
        public int EmailSaveCount { get; private set; }
        public string? LastSavedClientId { get; private set; }
        public string? LastSavedEmail { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IDataverseService.GetCurrentUserAsync):
                    return Task.FromResult<CurrentUserInfo?>(CurrentUser);
                case nameof(IDataverseService.GetCopiersEquipmentDashboardAsync):
                    return Task.FromResult(Dashboard);
                case nameof(IDataverseService.GetCopiersMtoV2ClientsAsync):
                    ClientCatalogReadCount++;
                    return Task.FromResult(Clients);
                case nameof(IDataverseService.SaveCopiersMtoV2ClientEmailAsync):
                    EmailSaveCount++;
                    LastSavedClientId = (string?)args![0];
                    LastSavedEmail = (string?)args[1];
                    return SaveException is null
                        ? Task.FromResult(SavedClient)
                        : Task.FromException<CopiersMtoV2ClientOptionDto>(SaveException);
                default:
                    throw new NotSupportedException($"La prueba no implementa {targetMethod?.Name}.");
            }
        }
    }

    private sealed class RecordingMaintenanceService : ICopiersMaintenanceV2Service
    {
        public CopiersMaintenanceV2DraftResultDto DraftResult { get; } = new()
        {
            RecordId = "f7b31f75-093c-4ce6-bd41-a4ed245d4c38", SubmissionKey = SubmissionKey,
            Version = "W/\"41\"", State = CopiersMaintenanceV2WorkflowState.Draft
        };
        public CopiersMaintenanceV2DraftRequestDto? LastDraft { get; private set; }
        public CopiersMaintenanceV2DraftUpdateRequestDto? LastUpdate { get; private set; }
        public CopiersMaintenanceV2FinalizeMultipartRequestDto? LastFinalization { get; private set; }
        public int CreateCount { get; private set; }
        public int SaveCount { get; private set; }
        public int FinalizeCount { get; private set; }

        public Task<CopiersMaintenanceV2DraftResultDto> CreateOrGetDraftAsync(
            CopiersMaintenanceV2DraftRequestDto request, CopiersMaintenanceV2ActorContext actor, CancellationToken ct = default)
        {
            CreateCount++;
            LastDraft = request;
            return Task.FromResult(DraftResult);
        }

        public Task<CopiersMaintenanceV2DraftResultDto> SaveDraftAsync(
            CopiersMaintenanceV2DraftUpdateRequestDto request, CopiersMaintenanceV2ActorContext actor, CancellationToken ct = default)
        {
            SaveCount++;
            LastUpdate = request;
            return Task.FromResult(DraftResult);
        }

        public Task<CopiersMaintenanceV2FinalizeResultDto> FinalizeMultipartAsync(
            CopiersMaintenanceV2FinalizeMultipartRequestDto request, CopiersMaintenanceV2ActorContext actor, CancellationToken ct = default)
        {
            FinalizeCount++;
            LastFinalization = request;
            return Task.FromResult(new CopiersMaintenanceV2FinalizeResultDto
            {
                RecordId = request.RecordId, SubmissionKey = request.SubmissionKey,
                State = CopiersMaintenanceV2WorkflowState.ReadyToSend,
                EmailState = CopiersMaintenanceV2EmailState.Pending
            });
        }
    }
}
