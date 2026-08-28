using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CotizadorInterno.Web.Models.MesaAyuda;

public sealed class MesaAyudaChangePlanDraft
{
    public string TicketId { get; init; } = "";
    public int Version { get; init; }
    public string TenantId { get; init; } = "";
    public string EnvironmentId { get; init; } = "";
    public string ResourceId { get; init; } = "";
    public string ToolName { get; init; } = "";
    public string ToolVersion { get; init; } = "";
    public string CanonicalArgumentsJson { get; init; } = "{}";
    public string BeforeStateJson { get; init; } = "{}";
    public string ProposedStateJson { get; init; } = "{}";
    public string StateFingerprint { get; init; } = "";
    public string Impact { get; init; } = "";
    public string Risk { get; init; } = "";
    public string VerificationStrategy { get; init; } = "";
    public string RollbackStrategy { get; init; } = "";
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public string IdempotencyKey { get; init; } = "";
}

public sealed class MesaAyudaFrozenChangePlan
{
    public required MesaAyudaChangePlanDraft Plan { get; init; }
    public string CanonicalPlanJson { get; init; } = "";
    public string Sha256 { get; init; } = "";
}

public sealed class MesaAyudaApprovalReceipt
{
    public string PlanSha256 { get; init; } = "";
    public string ApprovedByOid { get; init; } = "";
    public string ApprovedByEmail { get; init; } = "";
    public DateTimeOffset ApprovedAtUtc { get; init; }
}

public sealed class MesaAyudaApprovalValidation
{
    public bool IsValid { get; init; }
    public string Reason { get; init; } = "";
}

public static class MesaAyudaChangeApprovalPolicy
{
    public static MesaAyudaFrozenChangePlan Freeze(MesaAyudaChangePlanDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ValidateDraft(draft);

        var canonicalJson = BuildCanonicalPlanJson(draft);
        var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)))
            .ToLowerInvariant();
        return new MesaAyudaFrozenChangePlan
        {
            Plan = draft,
            CanonicalPlanJson = canonicalJson,
            Sha256 = hash
        };
    }

    public static MesaAyudaApprovalValidation ValidateForExecution(
        MesaAyudaFrozenChangePlan frozen,
        MesaAyudaApprovalReceipt approval,
        string executingActorOid,
        string observedStateFingerprint,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(frozen);
        ArgumentNullException.ThrowIfNull(approval);

        var recalculated = Freeze(frozen.Plan);
        if (!FixedEquals(recalculated.Sha256, frozen.Sha256)
            || !FixedEquals(recalculated.CanonicalPlanJson, frozen.CanonicalPlanJson))
        {
            return Invalid("El plan fue modificado despues de congelarse.");
        }

        if (!FixedEquals(approval.PlanSha256, frozen.Sha256))
        {
            return Invalid("La aprobacion corresponde a otra version del plan.");
        }

        if (string.IsNullOrWhiteSpace(approval.ApprovedByOid))
        {
            return Invalid("La aprobacion no contiene el oid del usuario autenticado.");
        }

        if (!Guid.TryParse(approval.ApprovedByOid, out var approverOid)
            || !Guid.TryParse(executingActorOid, out var actorOid)
            || approverOid != actorOid)
        {
            return Invalid("La aprobacion no pertenece al usuario autenticado que solicita la ejecucion.");
        }

        if (approval.ApprovedAtUtc == default
            || approval.ApprovedAtUtc > nowUtc.AddMinutes(1))
        {
            return Invalid("La fecha de aprobacion no es valida.");
        }

        if (frozen.Plan.ExpiresAtUtc <= nowUtc)
        {
            return Invalid("La aprobacion expiro; se requiere una nueva revision.");
        }

        if (!FixedEquals(
                observedStateFingerprint?.Trim() ?? "",
                frozen.Plan.StateFingerprint.Trim()))
        {
            return Invalid("El recurso cambio desde la auditoria; el plan debe recalcularse.");
        }

        return new MesaAyudaApprovalValidation
        {
            IsValid = true,
            Reason = "Aprobacion valida para el plan y estado observados."
        };
    }

    private static string BuildCanonicalPlanJson(MesaAyudaChangePlanDraft draft)
    {
        var fields = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["arguments"] = ParseAndCanonicalizeJson(draft.CanonicalArgumentsJson, nameof(draft.CanonicalArgumentsJson)),
            ["before"] = ParseAndCanonicalizeJson(draft.BeforeStateJson, nameof(draft.BeforeStateJson)),
            ["environmentId"] = draft.EnvironmentId.Trim(),
            ["expiresAtUtc"] = draft.ExpiresAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["idempotencyKey"] = draft.IdempotencyKey.Trim(),
            ["impact"] = draft.Impact.Trim(),
            ["proposed"] = ParseAndCanonicalizeJson(draft.ProposedStateJson, nameof(draft.ProposedStateJson)),
            ["resourceId"] = draft.ResourceId.Trim(),
            ["risk"] = draft.Risk.Trim().ToLowerInvariant(),
            ["rollbackStrategy"] = draft.RollbackStrategy.Trim(),
            ["stateFingerprint"] = draft.StateFingerprint.Trim(),
            ["tenantId"] = draft.TenantId.Trim().ToLowerInvariant(),
            ["ticketId"] = draft.TicketId.Trim().ToLowerInvariant(),
            ["toolName"] = draft.ToolName.Trim(),
            ["toolVersion"] = draft.ToolVersion.Trim(),
            ["verificationStrategy"] = draft.VerificationStrategy.Trim(),
            ["version"] = draft.Version
        };

        return JsonSerializer.Serialize(fields);
    }

    private static JsonElement ParseAndCanonicalizeJson(string value, string fieldName)
    {
        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(value) ? "{}" : value);
            var canonical = CanonicalizeElement(document.RootElement);
            using var normalized = JsonDocument.Parse(canonical);
            return normalized.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"{fieldName} debe ser un JSON valido.",
                ex);
        }
    }

    private static string CanonicalizeElement(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonicalElement(writer, element);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonicalElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalElement(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalElement(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException("El plan contiene un valor JSON no soportado.");
        }
    }

    private static void ValidateDraft(MesaAyudaChangePlanDraft draft)
    {
        RequireGuid(draft.TicketId, nameof(draft.TicketId));
        RequireGuid(draft.TenantId, nameof(draft.TenantId));
        if (draft.Version <= 0)
        {
            throw new InvalidOperationException("Version debe ser mayor que cero.");
        }

        Require(draft.EnvironmentId, nameof(draft.EnvironmentId));
        Require(draft.ResourceId, nameof(draft.ResourceId));
        Require(draft.ToolName, nameof(draft.ToolName));
        Require(draft.ToolVersion, nameof(draft.ToolVersion));
        Require(draft.StateFingerprint, nameof(draft.StateFingerprint));
        Require(draft.Impact, nameof(draft.Impact));
        Require(draft.Risk, nameof(draft.Risk));
        Require(draft.VerificationStrategy, nameof(draft.VerificationStrategy));
        Require(draft.RollbackStrategy, nameof(draft.RollbackStrategy));
        Require(draft.IdempotencyKey, nameof(draft.IdempotencyKey));
        if (draft.ExpiresAtUtc == default)
        {
            throw new InvalidOperationException("ExpiresAtUtc es obligatorio.");
        }
    }

    private static void Require(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{fieldName} es obligatorio.");
        }
    }

    private static void RequireGuid(string value, string fieldName)
    {
        if (!Guid.TryParse(value, out _))
        {
            throw new InvalidOperationException($"{fieldName} debe ser un GUID valido.");
        }
    }

    private static bool FixedEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left ?? "");
        var rightBytes = Encoding.UTF8.GetBytes(right ?? "");
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static MesaAyudaApprovalValidation Invalid(string reason) =>
        new() { IsValid = false, Reason = reason };
}
