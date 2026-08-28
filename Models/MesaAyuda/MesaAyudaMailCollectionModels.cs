using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CotizadorInterno.Web.Models.MesaAyuda;

public sealed record MesaAyudaMailParty(string Name, string Address);

public sealed record MesaAyudaCollectedMail
{
    public required string IdempotencyKey { get; init; }
    public required string Mailbox { get; init; }
    public required string GraphMessageId { get; init; }
    public string InternetMessageId { get; init; } = "";
    public string ConversationId { get; init; } = "";
    public string ChangeTag { get; init; } = "";
    public string Subject { get; init; } = "";
    public MesaAyudaMailParty? From { get; init; }
    public MesaAyudaMailParty? Sender { get; init; }
    public IReadOnlyList<MesaAyudaMailParty> ToRecipients { get; init; } = [];
    public IReadOnlyList<MesaAyudaMailParty> CcRecipients { get; init; } = [];
    public DateTimeOffset? ReceivedAtUtc { get; init; }
    public DateTimeOffset? SentAtUtc { get; init; }
    public string BodyContentType { get; init; } = "";
    public string Body { get; init; } = "";
    public string BodyPreview { get; init; } = "";
    public string Importance { get; init; } = "";
    public bool HasAttachments { get; init; }
    public bool IsRead { get; init; }

    internal static string CreateIdempotencyKey(string mailbox, string graphMessageId)
    {
        var canonical =
            $"{mailbox.Trim().ToLowerInvariant()}\n{graphMessageId.Trim()}";
        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}

public sealed record MesaAyudaMailDeltaCheckpoint(
    string Mailbox,
    string DeltaLink,
    long Version,
    DateTimeOffset AdvancedAtUtc);

public sealed record MesaAyudaMailDeltaAdvance(
    string Mailbox,
    long? ExpectedVersion,
    string DeltaLink,
    DateTimeOffset AdvancedAtUtc,
    DateTimeOffset? LastMessageAtUtc);

public sealed record MesaAyudaMailboxCollectionResult(
    string Mailbox,
    bool Succeeded,
    int ProcessedMessages,
    bool CheckpointAdvanced,
    string Status);

public sealed record MesaAyudaMailCollectionResult(
    bool Enabled,
    IReadOnlyList<MesaAyudaMailboxCollectionResult> Mailboxes)
{
    public int ProcessedMessages =>
        Mailboxes.Sum(mailbox => mailbox.ProcessedMessages);
}

public sealed record MesaAyudaMailDeltaRequest(
    string Mailbox,
    string? ContinuationLink,
    DateTimeOffset InitialReceivedAfterUtc);

public sealed record MesaAyudaMailDeltaChange(
    bool IsRemoved,
    MesaAyudaCollectedMail? Message);

public sealed record MesaAyudaMailDeltaPage(
    IReadOnlyList<MesaAyudaMailDeltaChange> Changes,
    string? NextLink,
    string? DeltaLink);

internal sealed class GraphMailDeltaEnvelope
{
    [JsonPropertyName("value")]
    public List<GraphMailMessage> Value { get; init; } = [];

    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; init; }

    [JsonPropertyName("@odata.deltaLink")]
    public string? DeltaLink { get; init; }
}

internal sealed class GraphMailMessage
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("internetMessageId")]
    public string? InternetMessageId { get; init; }

    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; init; }

    [JsonPropertyName("@odata.etag")]
    public string? ChangeTag { get; init; }

    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    [JsonPropertyName("from")]
    public GraphMailRecipient? From { get; init; }

    [JsonPropertyName("sender")]
    public GraphMailRecipient? Sender { get; init; }

    [JsonPropertyName("toRecipients")]
    public List<GraphMailRecipient> ToRecipients { get; init; } = [];

    [JsonPropertyName("ccRecipients")]
    public List<GraphMailRecipient> CcRecipients { get; init; } = [];

    [JsonPropertyName("receivedDateTime")]
    public DateTimeOffset? ReceivedAtUtc { get; init; }

    [JsonPropertyName("sentDateTime")]
    public DateTimeOffset? SentAtUtc { get; init; }

    [JsonPropertyName("body")]
    public GraphMailBody? Body { get; init; }

    [JsonPropertyName("bodyPreview")]
    public string? BodyPreview { get; init; }

    [JsonPropertyName("importance")]
    public string? Importance { get; init; }

    [JsonPropertyName("hasAttachments")]
    public bool HasAttachments { get; init; }

    [JsonPropertyName("isRead")]
    public bool IsRead { get; init; }

    [JsonPropertyName("@removed")]
    public JsonElement? Removed { get; init; }
}

internal sealed class GraphMailRecipient
{
    [JsonPropertyName("emailAddress")]
    public GraphMailAddress? EmailAddress { get; init; }
}

internal sealed class GraphMailAddress
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("address")]
    public string? Address { get; init; }
}

internal sealed class GraphMailBody
{
    [JsonPropertyName("contentType")]
    public string? ContentType { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }
}
