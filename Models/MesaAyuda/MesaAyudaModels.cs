using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace CotizadorInterno.Web.Models.MesaAyuda;

public sealed class MesaAyudaPageViewModel
{
    public string CurrentUserName { get; init; } = "";
    public bool AiConfigured { get; init; }
    public bool SchemaProvisioned { get; init; }
}

public sealed class MesaAyudaWorkspaceDto
{
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public bool SchemaProvisioned { get; init; }
    public string DataStatus { get; init; } = "";
    public IReadOnlyList<MesaAyudaQueueDto> Queues { get; init; } = Array.Empty<MesaAyudaQueueDto>();
    public IReadOnlyList<MesaAyudaTicketDto> Tickets { get; init; } = Array.Empty<MesaAyudaTicketDto>();
}

public sealed class MesaAyudaQueueDto
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public int Count { get; init; }
}

public sealed class MesaAyudaTicketDto
{
    public string RecordId { get; init; } = "";
    public string Reference { get; init; } = "";
    public bool ReferenceIsProvisional { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string ClientId { get; init; } = "";
    public string ClientName { get; init; } = "";
    public string Status { get; init; } = "";
    public string StatusKey { get; init; } = "";
    public string StatusTone { get; init; } = "";
    public string Priority { get; init; } = "Sin priorizar";
    public string Channel { get; init; } = "Registro actual";
    public string Category { get; init; } = "";
    public string Workload { get; init; } = "";
    public string CreatedAtDisplay { get; init; } = "";
    public string LastActivityDisplay { get; init; } = "";
    public string AssignedAgent { get; init; } = "";
    public string TenantStatus { get; init; } = "Sin confirmar";
    public string TenantId { get; init; } = "";
    public string ExistingResolution { get; init; } = "";
    public bool HasAttachment { get; init; }
    public string AttachmentFileName { get; init; } = "";
    public IReadOnlyList<MesaAyudaTimelineEventDto> Timeline { get; init; } =
        Array.Empty<MesaAyudaTimelineEventDto>();
}

public sealed class MesaAyudaTimelineEventDto
{
    public string Kind { get; init; } = "";
    public string Tone { get; init; } = "";
    public string Label { get; init; } = "";
    public string Actor { get; init; } = "";
    public string Timestamp { get; init; } = "";
    public string Body { get; init; } = "";
    public string Detail { get; init; } = "";
    public MesaAyudaInvestigationResultDto? Investigation { get; init; }
}

public sealed class MesaAyudaAnalyzeRequestDto
{
    [Required]
    [StringLength(64)]
    public string TicketId { get; set; } = "";

    [StringLength(4000)]
    public string Instruction { get; set; } = "";

    [StringLength(128)]
    public string IdempotencyKey { get; set; } = "";
}

public sealed class MesaAyudaMessageRequestDto
{
    [Required]
    [StringLength(64)]
    public string TicketId { get; set; } = "";

    [Required]
    [StringLength(4000, MinimumLength = 1)]
    public string Content { get; set; } = "";

    [StringLength(128)]
    public string IdempotencyKey { get; set; } = "";
}

public sealed class MesaAyudaMessageResponseDto
{
    public string Message { get; init; } = "";
    public string IdempotencyKey { get; init; } = "";
    public MesaAyudaTimelineEventDto Interaction { get; init; } = new();
}

public sealed class MesaAyudaAiRequest
{
    public required MesaAyudaTicketDto Ticket { get; init; }
    public string Instruction { get; init; } = "";
    public string HashedUserIdentifier { get; init; } = "";
}

public sealed class MesaAyudaInvestigationResultDto
{
    public string ResponseId { get; init; } = "";

    [JsonPropertyName("classification")]
    public string Classification { get; init; } = "doubtful";

    [JsonPropertyName("confidence")]
    public decimal Confidence { get; init; }

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = "";

    [JsonPropertyName("impact")]
    public string Impact { get; init; } = "";

    [JsonPropertyName("workload")]
    public string Workload { get; init; } = "";

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "unconfirmed";

    [JsonPropertyName("confirmed_facts")]
    public IReadOnlyList<string> ConfirmedFacts { get; init; } = Array.Empty<string>();

    [JsonPropertyName("hypotheses")]
    public IReadOnlyList<string> Hypotheses { get; init; } = Array.Empty<string>();

    [JsonPropertyName("missing_information")]
    public IReadOnlyList<string> MissingInformation { get; init; } = Array.Empty<string>();

    [JsonPropertyName("recommended_checks")]
    public IReadOnlyList<string> RecommendedChecks { get; init; } = Array.Empty<string>();

    [JsonPropertyName("risk_flags")]
    public IReadOnlyList<string> RiskFlags { get; init; } = Array.Empty<string>();

    [JsonPropertyName("next_action")]
    public string NextAction { get; init; } = "";

    [JsonPropertyName("requires_tenant_confirmation")]
    public bool RequiresTenantConfirmation { get; init; } = true;
}

public sealed class MesaAyudaAnalyzeResponseDto
{
    public string Message { get; init; } = "";
    public string IdempotencyKey { get; init; } = "";
    public MesaAyudaInvestigationResultDto Investigation { get; init; } = new();
    public IReadOnlyList<MesaAyudaTimelineEventDto> Interactions { get; init; } =
        Array.Empty<MesaAyudaTimelineEventDto>();
}

public sealed class MesaAyudaDataverseTicketDto
{
    public string RecordId { get; init; } = "";
    public string CaseNumber { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string ClientId { get; init; } = "";
    public string ClientName { get; init; } = "";
    public string Status { get; init; } = "";
    public string Category { get; init; } = "";
    public string Workload { get; init; } = "";
    public string CreatedAtValue { get; init; } = "";
    public string CreatedAtDisplay { get; init; } = "";
    public string LastActivityAtValue { get; init; } = "";
    public string LastActivityAtDisplay { get; init; } = "";
    public string CreatedByName { get; init; } = "";
    public string OwnerId { get; init; } = "";
    public string OwnerName { get; init; } = "";
    public string SourceChannel { get; init; } = "";
    internal string ReceiveMailbox { get; init; } = "";
    internal string ExternalConversation { get; init; } = "";
    internal string ExternalCaseKey { get; init; } = "";
    public string AiClassification { get; init; } = "";
    public decimal? AiConfidence { get; init; }
    public string AiSeverity { get; init; } = "";
    public string AiSummary { get; init; } = "";
    public string AutomationStatus { get; init; } = "";
    public string TenantRecordId { get; init; } = "";
    public string TenantName { get; init; } = "";
    public string TenantId { get; init; } = "";
    public string ExistingResolution { get; init; } = "";
    public bool HasAttachment { get; init; }
    public string AttachmentFileName { get; init; } = "";
}

public sealed class MesaAyudaInteractionDto
{
    public string RecordId { get; init; } = "";
    public string TicketId { get; init; } = "";
    public string InteractionKey { get; init; } = "";
    public string IdempotencyKey { get; init; } = "";
    public DateTimeOffset? EventAtUtc { get; init; }
    public int Sequence { get; init; }
    public string InteractionType { get; init; } = "";
    public string Direction { get; init; } = "";
    public string ActorType { get; init; } = "";
    public string ActorName { get; init; } = "";
    public string ActorAddress { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Content { get; init; } = "";
    public string StructuredJson { get; init; } = "";
    public string ModelResponseId { get; init; } = "";
    public string Classification { get; init; } = "";
    public decimal? Confidence { get; init; }
    public bool VisibleToCustomer { get; init; }
}

public sealed class MesaAyudaInternalMessageCreate
{
    public required string TicketId { get; init; }
    public required string Content { get; init; }
    public required string IdempotencyKey { get; init; }
    public string Subject { get; init; } = "Mensaje interno";
    public string ActorName { get; init; } = "";
    public string ActorAddress { get; init; } = "";
    public string ActorObjectId { get; init; } = "";
    public DateTimeOffset EventAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class MesaAyudaInvestigationCreate
{
    public required string TicketId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required MesaAyudaInvestigationResultDto Investigation { get; init; }
    public DateTimeOffset EventAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public static class MesaAyudaIdempotencyPolicy
{
    public static string CreateOperationKey(string ticketId, string? clientKey)
    {
        var normalizedTicket = Guid.TryParse(ticketId, out var parsedTicket)
            ? parsedTicket.ToString("D")
            : throw new InvalidOperationException("El ticket no tiene un identificador valido.");
        var normalizedClientKey = string.IsNullOrWhiteSpace(clientKey)
            ? Guid.NewGuid().ToString("N")
            : clientKey.Trim();
        return Hash($"{normalizedTicket}|operation|{normalizedClientKey}");
    }

    public static string Derive(string operationKey, string purpose)
    {
        if (string.IsNullOrWhiteSpace(operationKey))
            throw new InvalidOperationException("La operacion no tiene clave de idempotencia.");
        if (string.IsNullOrWhiteSpace(purpose))
            throw new InvalidOperationException("La operacion no tiene un proposito valido.");

        return Hash($"{operationKey.Trim().ToLowerInvariant()}|{purpose.Trim().ToLowerInvariant()}");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}

public static class MesaAyudaExternalCaseKeyPolicy
{
    public static string CreateEmail(string mailbox, string conversationId)
    {
        if (string.IsNullOrWhiteSpace(mailbox))
            throw new InvalidOperationException(
                "El buzon receptor es obligatorio para identificar el caso.");
        if (string.IsNullOrWhiteSpace(conversationId))
            throw new InvalidOperationException(
                "La conversacion externa es obligatoria para identificar el caso.");

        const string channel = "email";
        var normalizedMailbox = mailbox.Trim().ToLowerInvariant();
        var normalizedConversation = conversationId.Trim();
        var canonical =
            $"mesa-ayuda:external-case:v1|{channel.Length}:{channel}|" +
            $"{normalizedMailbox.Length}:{normalizedMailbox}|" +
            $"{normalizedConversation.Length}:{normalizedConversation}";

        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}
