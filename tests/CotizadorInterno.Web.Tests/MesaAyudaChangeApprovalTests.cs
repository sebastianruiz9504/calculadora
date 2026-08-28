using CotizadorInterno.Web.Models.MesaAyuda;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class MesaAyudaChangeApprovalTests
{
    [Fact]
    public void JsonPropertyOrderDoesNotChangeTheFrozenPlanHash()
    {
        var expires = DateTimeOffset.Parse("2026-07-23T20:00:00Z");
        var first = MesaAyudaChangeApprovalPolicy.Freeze(
            CreateDraft(expires, """{"user":"u1","enabled":true}"""));
        var second = MesaAyudaChangeApprovalPolicy.Freeze(
            CreateDraft(expires, """{"enabled":true,"user":"u1"}"""));

        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(first.CanonicalPlanJson, second.CanonicalPlanJson);
    }

    [Fact]
    public void ChangingTheTargetTenantInvalidatesThePlanHash()
    {
        var expires = DateTimeOffset.Parse("2026-07-23T20:00:00Z");
        var original = MesaAyudaChangeApprovalPolicy.Freeze(CreateDraft(expires));
        var changed = MesaAyudaChangeApprovalPolicy.Freeze(
            CopyDraft(
                original.Plan,
                tenantId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        Assert.NotEqual(original.Sha256, changed.Sha256);
    }

    [Fact]
    public void MatchingApprovalAndObservedStateAreValidBeforeExpiry()
    {
        var now = DateTimeOffset.Parse("2026-07-23T19:00:00Z");
        var frozen = MesaAyudaChangeApprovalPolicy.Freeze(
            CreateDraft(now.AddMinutes(15)));
        var approval = new MesaAyudaApprovalReceipt
        {
            PlanSha256 = frozen.Sha256,
            ApprovedByOid = "11111111-1111-1111-1111-111111111111",
            ApprovedByEmail = "sruiz@digitaltechcolombia.com",
            ApprovedAtUtc = now
        };

        var validation = MesaAyudaChangeApprovalPolicy.ValidateForExecution(
            frozen,
            approval,
            approval.ApprovedByOid,
            frozen.Plan.StateFingerprint,
            now.AddMinutes(1));

        Assert.True(validation.IsValid);
    }

    [Fact]
    public void ExpiredApprovalOrChangedResourceStateIsRejected()
    {
        var now = DateTimeOffset.Parse("2026-07-23T19:00:00Z");
        var frozen = MesaAyudaChangeApprovalPolicy.Freeze(
            CreateDraft(now.AddMinutes(5)));
        var approval = new MesaAyudaApprovalReceipt
        {
            PlanSha256 = frozen.Sha256,
            ApprovedByOid = "11111111-1111-1111-1111-111111111111",
            ApprovedAtUtc = now
        };

        var changedState = MesaAyudaChangeApprovalPolicy.ValidateForExecution(
            frozen,
            approval,
            approval.ApprovedByOid,
            "W/\"different\"",
            now.AddMinutes(1));
        var expired = MesaAyudaChangeApprovalPolicy.ValidateForExecution(
            frozen,
            approval,
            approval.ApprovedByOid,
            frozen.Plan.StateFingerprint,
            now.AddMinutes(6));

        Assert.False(changedState.IsValid);
        Assert.Contains("cambio", changedState.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(expired.IsValid);
        Assert.Contains("expiro", expired.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApprovalFromAnotherAuthenticatedUserIsRejected()
    {
        var now = DateTimeOffset.Parse("2026-07-23T19:00:00Z");
        var frozen = MesaAyudaChangeApprovalPolicy.Freeze(
            CreateDraft(now.AddMinutes(5)));
        var approval = new MesaAyudaApprovalReceipt
        {
            PlanSha256 = frozen.Sha256,
            ApprovedByOid = "11111111-1111-1111-1111-111111111111",
            ApprovedAtUtc = now
        };

        var validation = MesaAyudaChangeApprovalPolicy.ValidateForExecution(
            frozen,
            approval,
            "22222222-2222-2222-2222-222222222222",
            frozen.Plan.StateFingerprint,
            now.AddMinutes(1));

        Assert.False(validation.IsValid);
        Assert.Contains("usuario autenticado", validation.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static MesaAyudaChangePlanDraft CreateDraft(
        DateTimeOffset expires,
        string arguments = """{"enabled":true,"user":"u1"}""") =>
        new()
        {
            TicketId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            Version = 1,
            TenantId = "11111111-2222-3333-4444-555555555555",
            EnvironmentId = "Default-11111111-2222-3333-4444-555555555555",
            ResourceId = "user/u1/mailbox",
            ToolName = "UpdateMailboxSetting",
            ToolVersion = "1",
            CanonicalArgumentsJson = arguments,
            BeforeStateJson = """{"enabled":false}""",
            ProposedStateJson = """{"enabled":true}""",
            StateFingerprint = "W/\"7812\"",
            Impact = "Habilita la opcion exacta para un usuario.",
            Risk = "medium",
            VerificationStrategy = "Releer el ajuste y comparar el valor.",
            RollbackStrategy = "Restaurar enabled=false si el valor sigue siendo el aplicado.",
            ExpiresAtUtc = expires,
            IdempotencyKey = "ticket-a-v1-action-1"
        };

    private static MesaAyudaChangePlanDraft CopyDraft(
        MesaAyudaChangePlanDraft source,
        string tenantId) =>
        new()
        {
            TicketId = source.TicketId,
            Version = source.Version,
            TenantId = tenantId,
            EnvironmentId = source.EnvironmentId,
            ResourceId = source.ResourceId,
            ToolName = source.ToolName,
            ToolVersion = source.ToolVersion,
            CanonicalArgumentsJson = source.CanonicalArgumentsJson,
            BeforeStateJson = source.BeforeStateJson,
            ProposedStateJson = source.ProposedStateJson,
            StateFingerprint = source.StateFingerprint,
            Impact = source.Impact,
            Risk = source.Risk,
            VerificationStrategy = source.VerificationStrategy,
            RollbackStrategy = source.RollbackStrategy,
            ExpiresAtUtc = source.ExpiresAtUtc,
            IdempotencyKey = source.IdempotencyKey
        };
}
