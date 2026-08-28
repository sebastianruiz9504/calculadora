using System.Reflection;
using System.Security.Claims;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Models.MesaAyuda;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.MesaAyuda;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class MesaAyudaPersistenceTests
{
    [Fact]
    public void IdempotencyKeysAreStablePerClientOperationAndPurpose()
    {
        const string ticketId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var first = MesaAyudaIdempotencyPolicy.CreateOperationKey(
            ticketId,
            "browser-retry-1");
        var second = MesaAyudaIdempotencyPolicy.CreateOperationKey(
            ticketId,
            "browser-retry-1");

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Equal(
            MesaAyudaIdempotencyPolicy.Derive(first, "ai-result"),
            MesaAyudaIdempotencyPolicy.Derive(second, "ai-result"));
        Assert.NotEqual(
            MesaAyudaIdempotencyPolicy.Derive(first, "ai-result"),
            MesaAyudaIdempotencyPolicy.Derive(first, "agent-instruction"));
    }

    [Fact]
    public void ExternalEmailCaseKeyUsesStableVersionedCanonicalForm()
    {
        var key = MesaAyudaExternalCaseKeyPolicy.CreateEmail(
            "sruiz@digitaltechcolombia.com",
            "AAQkAGQ1Example==");

        Assert.Equal(
            "212e8409013dac57e102de19570e56c31bede71fc53c847a1b83c4f1749f2a0d",
            key);
        Assert.Matches("^[0-9a-f]{64}$", key);
        Assert.Equal(
            key,
            MesaAyudaExternalCaseKeyPolicy.CreateEmail(
                "  SRUIZ@DIGITALTECHCOLOMBIA.COM ",
                " AAQkAGQ1Example== "));
    }

    [Fact]
    public void ExternalEmailCaseKeyPreservesOpaqueConversationIdentifier()
    {
        var original = MesaAyudaExternalCaseKeyPolicy.CreateEmail(
            "sruiz@digitaltechcolombia.com",
            "AAQkAGQ1Example==");

        Assert.NotEqual(
            original,
            MesaAyudaExternalCaseKeyPolicy.CreateEmail(
                "sruiz@digitaltechcolombia.com",
                "aaqkagq1example=="));
        Assert.NotEqual(
            original,
            MesaAyudaExternalCaseKeyPolicy.CreateEmail(
                "abarriga@digitaltechcolombia.com",
                "AAQkAGQ1Example=="));
    }

    [Theory]
    [InlineData("", "conversation")]
    [InlineData("   ", "conversation")]
    [InlineData("mailbox@example.com", "")]
    [InlineData("mailbox@example.com", "   ")]
    public void ExternalEmailCaseKeyRejectsIncompleteIdentity(
        string mailbox,
        string conversationId)
    {
        Assert.Throws<InvalidOperationException>(() =>
            MesaAyudaExternalCaseKeyPolicy.CreateEmail(
                mailbox,
                conversationId));
    }

    [Fact]
    public async Task DurableWorkspaceUsesCaseNumberOwnerTenantAndPersistedTimeline()
    {
        var api = DispatchProxy.Create<IDataverseService, MesaDataverseProxy>();
        var fake = (MesaDataverseProxy)api;
        fake.Tickets =
        [
            new MesaAyudaDataverseTicketDto
            {
                RecordId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                CaseNumber = "SOP-2026-000123",
                Title = "No recibe correo",
                Description = "El usuario reporta el incidente.",
                ClientName = "Contoso",
                Status = "Nuevo",
                CreatedAtValue = "2026-07-23T15:00:00Z",
                CreatedAtDisplay = "23/07/2026 10:00",
                LastActivityAtValue = "2026-07-23T16:00:00Z",
                LastActivityAtDisplay = "23/07/2026 11:00",
                CreatedByName = "Importador",
                OwnerId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                OwnerName = "Sebastian Ruiz",
                SourceChannel = "email",
                TenantRecordId = "cccccccc-cccc-cccc-cccc-cccccccccccc",
                TenantName = "Contoso Tenant",
                TenantId = "dddddddd-dddd-dddd-dddd-dddddddddddd"
            }
        ];
        fake.Interactions =
        [
            new MesaAyudaInteractionDto
            {
                RecordId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
                TicketId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                EventAtUtc = DateTimeOffset.Parse("2026-07-23T16:00:00Z"),
                ActorName = "Auditor IA",
                Subject = "Resultado de auditoria IA",
                Content = "La causa aun no esta confirmada.",
                ModelResponseId = "resp_123",
                Classification = "support",
                Confidence = 0.92m
            }
        ];
        var service = new MesaAyudaWorkspaceService(
            api,
            Options.Create(new MesaAyudaOptions
            {
                SchemaProvisioned = true,
                MonitoredMailboxes = ["sruiz@digitaltechcolombia.com"]
            }));

        var workspace = await service.GetWorkspaceAsync();
        var ticket = Assert.Single(workspace.Tickets);

        Assert.Equal("SOP-2026-000123", ticket.Reference);
        Assert.False(ticket.ReferenceIsProvisional);
        Assert.Equal("Sebastian Ruiz", ticket.AssignedAgent);
        Assert.Equal(
            "dddddddd-dddd-dddd-dddd-dddddddddddd",
            ticket.TenantId);
        Assert.Contains(
            ticket.Timeline,
            item => item.Kind == "audit"
                && item.Body.Contains("causa", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnalyzePersistsInstructionAndResultWithDifferentDerivedKeys()
    {
        var dataverse = DispatchProxy.Create<IDataverseService, MesaDataverseProxy>();
        var workspace = new RecordingWorkspace();
        var controller = CreateController(
            dataverse,
            workspace,
            new StubAiService());
        var request = new MesaAyudaAnalyzeRequestDto
        {
            TicketId = RecordingWorkspace.TicketId,
            Instruction = "Valida Exchange sin ejecutar cambios.",
            IdempotencyKey = "retry-123"
        };

        var result = await controller.Analyze(request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<MesaAyudaAnalyzeResponseDto>(ok.Value);

        Assert.NotNull(workspace.Instruction);
        Assert.NotNull(workspace.Investigation);
        Assert.NotEqual(
            workspace.Instruction!.IdempotencyKey,
            workspace.Investigation!.IdempotencyKey);
        Assert.Equal(2, response.Interactions.Count);
        Assert.Equal(64, response.IdempotencyKey.Length);
    }

    [Fact]
    public async Task MessageUsesAuthenticatedActorAndDurableWorkspace()
    {
        var dataverse = DispatchProxy.Create<IDataverseService, MesaDataverseProxy>();
        var workspace = new RecordingWorkspace();
        var controller = CreateController(
            dataverse,
            workspace,
            new StubAiService());

        var result = await controller.Message(
            new MesaAyudaMessageRequestDto
            {
                TicketId = RecordingWorkspace.TicketId,
                Content = "Confirma el dominio antes de continuar.",
                IdempotencyKey = "message-retry-1"
            },
            CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<MesaAyudaMessageResponseDto>(ok.Value);

        Assert.NotNull(workspace.Instruction);
        Assert.Equal(
            "sruiz@digitaltechcolombia.com",
            workspace.Instruction!.ActorAddress);
        Assert.Equal(
            "11111111-1111-1111-1111-111111111111",
            workspace.Instruction.ActorObjectId);
        Assert.Equal(64, response.IdempotencyKey.Length);
    }

    private static MesaAyudaController CreateController(
        IDataverseService dataverse,
        IMesaAyudaWorkspaceService workspace,
        IMesaAyudaAiService ai)
    {
        var controller = new MesaAyudaController(
            dataverse,
            workspace,
            ai,
            Options.Create(new MesaAyudaOptions
            {
                SchemaProvisioned = true
            }),
            NullLogger<MesaAyudaController>.Instance);
        var identity = new ClaimsIdentity(
            [
                new Claim(
                    "oid",
                    "11111111-1111-1111-1111-111111111111"),
                new Claim("name", "Sebastian Ruiz"),
                new Claim(
                    "preferred_username",
                    "sruiz@digitaltechcolombia.com")
            ],
            "UnitTest");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
        return controller;
    }

    private sealed class RecordingWorkspace : IMesaAyudaWorkspaceService
    {
        public const string TicketId =
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

        public MesaAyudaInternalMessageCreate? Instruction { get; private set; }
        public MesaAyudaInvestigationCreate? Investigation { get; private set; }

        public Task<MesaAyudaWorkspaceDto> GetWorkspaceAsync(
            CancellationToken ct = default) =>
            Task.FromResult(new MesaAyudaWorkspaceDto());

        public Task<MesaAyudaTicketDto?> GetTicketAsync(
            string ticketId,
            CancellationToken ct = default) =>
            Task.FromResult<MesaAyudaTicketDto?>(
                string.Equals(ticketId, TicketId, StringComparison.OrdinalIgnoreCase)
                    ? new MesaAyudaTicketDto
                    {
                        RecordId = TicketId,
                        Reference = "SOP-2026-000123",
                        Title = "Caso de prueba"
                    }
                    : null);

        public Task<MesaAyudaTimelineEventDto> CreateInternalMessageAsync(
            MesaAyudaInternalMessageCreate request,
            CancellationToken ct = default)
        {
            Instruction = request;
            return Task.FromResult(new MesaAyudaTimelineEventDto
            {
                Kind = "message",
                Actor = request.ActorName,
                Body = request.Content
            });
        }

        public Task<MesaAyudaInvestigationResultDto?>
            GetPersistedInvestigationAsync(
                string idempotencyKey,
                CancellationToken ct = default) =>
            Task.FromResult<MesaAyudaInvestigationResultDto?>(null);

        public Task<MesaAyudaTimelineEventDto> SaveInvestigationAsync(
            MesaAyudaInvestigationCreate request,
            CancellationToken ct = default)
        {
            Investigation = request;
            return Task.FromResult(new MesaAyudaTimelineEventDto
            {
                Kind = "audit",
                Actor = "Auditor IA",
                Body = request.Investigation.Summary
            });
        }
    }

    private sealed class StubAiService : IMesaAyudaAiService
    {
        public bool IsConfigured => true;

        public Task<MesaAyudaInvestigationResultDto> AnalyzeAsync(
            MesaAyudaAiRequest request,
            CancellationToken ct = default) =>
            Task.FromResult(new MesaAyudaInvestigationResultDto
            {
                ResponseId = "resp_test",
                Classification = "support",
                Confidence = 0.9m,
                Summary = "Auditoria completada.",
                Severity = "medium"
            });
    }

    public class MesaDataverseProxy : DispatchProxy
    {
        public IReadOnlyList<MesaAyudaDataverseTicketDto> Tickets { get; set; } =
            Array.Empty<MesaAyudaDataverseTicketDto>();
        public IReadOnlyList<MesaAyudaInteractionDto> Interactions { get; set; } =
            Array.Empty<MesaAyudaInteractionDto>();

        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args) =>
            targetMethod?.Name switch
            {
                nameof(IDataverseService.GetMesaAyudaTicketsAsync) =>
                    Task.FromResult(Tickets),
                nameof(IDataverseService.GetMesaAyudaInteractionsAsync) =>
                    Task.FromResult(Interactions),
                _ => throw new NotSupportedException(
                    $"La prueba no implementa {targetMethod?.Name}.")
            };
    }
}
