using CotizadorInterno.Web.Models.CopiersMtoV2;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

/// <summary>
/// Dataverse persistence boundary for the V2 workflow. A concrete adapter must
/// enforce an alternate key for SubmissionKey and optimistic concurrency for Version.
/// </summary>
public interface ICopiersMaintenanceV2DataverseRepository
{
    Task<CopiersMaintenanceV2DraftRecord> CreateOrGetDraftAsync(
        CopiersMaintenanceV2CreateDraftCommand command,
        CancellationToken ct = default);

    Task<CopiersMaintenanceV2DraftRecord> SaveDraftAsync(
        CopiersMaintenanceV2SaveDraftCommand command,
        CancellationToken ct = default);

    Task<CopiersMaintenanceV2BeginFinalizationResult> TryBeginFinalizationAsync(
        CopiersMaintenanceV2BeginFinalizationCommand command,
        CancellationToken ct = default);

    /// <summary>
    /// Stages signature/evidence/report binaries idempotently, verifies every public,
    /// internal and delivery snapshot while the row remains Finalizing/NotReady, and
    /// only then publishes Finalizing -> ReadyToSend and NotReady -> Pending with ETag.
    /// It must return an independent Dataverse read-back.
    /// </summary>
    Task<CopiersMaintenanceV2DraftRecord> CompleteFinalizationAsync(
        CopiersMaintenanceV2CompleteFinalizationCommand command,
        CancellationToken ct = default);

    Task<CopiersMaintenanceV2DraftRecord> MarkFinalizationFailedAsync(
        CopiersMaintenanceV2FinalizationFailedCommand command,
        CancellationToken ct = default);

}

public interface ICopiersMaintenanceV2Service
{
    Task<CopiersMaintenanceV2DraftResultDto> CreateOrGetDraftAsync(
        CopiersMaintenanceV2DraftRequestDto request,
        CopiersMaintenanceV2ActorContext actor,
        CancellationToken ct = default);

    Task<CopiersMaintenanceV2DraftResultDto> SaveDraftAsync(
        CopiersMaintenanceV2DraftUpdateRequestDto request,
        CopiersMaintenanceV2ActorContext actor,
        CancellationToken ct = default);

    Task<CopiersMaintenanceV2FinalizeResultDto> FinalizeMultipartAsync(
        CopiersMaintenanceV2FinalizeMultipartRequestDto request,
        CopiersMaintenanceV2ActorContext actor,
        CancellationToken ct = default);
}

public interface ICopiersMtoV2PdfBuilder
{
    Task<CopiersMaintenanceV2RenderedPdf> BuildAsync(
        CopiersMaintenanceV2PdfModel model,
        CancellationToken ct = default);
}

/// <summary>
/// App-only Dataverse transport used exclusively by the isolated V2 repository.
/// End users must not receive direct write privileges on the V2 tables.
/// </summary>
public interface ICopiersMtoV2ApplicationDataverseClient
{
    Task<HttpResponseMessage> SendAsync(
        string relativeUrl,
        HttpMethod method,
        HttpContent? content,
        Action<HttpRequestMessage>? customizeRequest,
        CancellationToken ct = default);
}

public sealed class CopiersMaintenanceV2DraftRecord
{
    public string RecordId { get; set; } = "";
    public string SubmissionKey { get; set; } = "";
    public string Version { get; set; } = "";
    public CopiersMaintenanceV2WorkflowState State { get; set; }
    public CopiersMaintenanceV2EmailState EmailState { get; set; }
    public string TechnicianSystemUserId { get; set; } = "";
    public string TechnicianName { get; set; } = "";
    public string TechnicianEmail { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string CustomerContactName { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    public string EquipmentSerial { get; set; } = "";
    public string Title { get; set; } = "";
    public DateOnly ServiceDate { get; set; }
    public int? MaintenanceTypeValue { get; set; }
    public string ReportFileName { get; set; } = "";
    public string ReportSha256 { get; set; } = "";
    public string SignatureEvidenceKey { get; set; } = "";
    public string ReportEvidenceKey { get; set; } = "";
    public string FinalizationFingerprint { get; set; } = "";
    public int AttachmentCount { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? ServerFinalizedAtUtc { get; set; }
    public bool WasCreated { get; set; }
}

public class CopiersMaintenanceV2CreateDraftCommand
{
    public string SubmissionKey { get; set; } = "";
    public string TechnicianSystemUserId { get; set; } = "";
    public string TechnicianName { get; set; } = "";
    public string TechnicianEmail { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string CustomerContactName { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    public string EquipmentSerial { get; set; } = "";
    public string Title { get; set; } = "";
    public DateOnly ServiceDate { get; set; }
    public int? MaintenanceTypeValue { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
}

public sealed class CopiersMaintenanceV2SaveDraftCommand : CopiersMaintenanceV2CreateDraftCommand
{
    public string RecordId { get; set; } = "";
    public string ExpectedVersion { get; set; } = "";
}

public enum CopiersMaintenanceV2BeginDisposition
{
    Acquired = 0,
    AlreadyReady = 1,
    InProgress = 2,
    Conflict = 3
}

public sealed class CopiersMaintenanceV2BeginFinalizationCommand
{
    public string RecordId { get; set; } = "";
    public string SubmissionKey { get; set; } = "";
    public string ExpectedVersion { get; set; } = "";
    public string TechnicianSystemUserId { get; set; } = "";
    public string FinalizationLeaseId { get; set; } = "";
    public DateTimeOffset StartedAtUtc { get; set; }
}

public sealed class CopiersMaintenanceV2BeginFinalizationResult
{
    public CopiersMaintenanceV2BeginDisposition Disposition { get; set; }
    public string FinalizationLeaseId { get; set; } = "";
    public CopiersMaintenanceV2DraftRecord Record { get; set; } = new();
    public string Message { get; set; } = "";
}

/// <summary>Internal audit data. It is not accepted by any PDF/email contract.</summary>
public sealed class CopiersMaintenanceV2InternalLocationData
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double AccuracyMeters { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public string Source { get; set; } = "navigator.geolocation";
}

public sealed class CopiersMaintenanceV2StoredFile
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

public sealed class CopiersMaintenanceV2CompleteFinalizationCommand
{
    public string RecordId { get; set; } = "";
    public string SubmissionKey { get; set; } = "";
    public string FinalizationLeaseId { get; set; } = "";
    public string TechnicianSystemUserId { get; set; } = "";
    public CopiersMaintenanceV2BaseSnapshot BaseSnapshot { get; set; } = new();
    public string FinalizationFingerprint { get; set; } = "";
    public string FormVersion { get; set; } = "";
    public IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> Answers { get; set; } =
        Array.Empty<CopiersMaintenanceV2FormAnswerSnapshot>();
    public string WorkPerformed { get; set; } = "";
    public string CustomerObservations { get; set; } = "";
    public string ServiceAddressInternal { get; set; } = "";
    public string InternalNotes { get; set; } = "";
    public string SignerName { get; set; } = "";
    public string SignerRole { get; set; } = "";
    public bool CustomerAccepted { get; set; }
    public int SignaturePointCount { get; set; }
    public DateTimeOffset DeviceSignedAtUtc { get; set; }
    public DateTimeOffset ServerFinalizedAtUtc { get; set; }
    public CopiersMaintenanceV2InternalLocationData? InternalLocation { get; set; }
    public CopiersMaintenanceV2StoredFile Signature { get; set; } = new();
    public IReadOnlyList<CopiersMaintenanceV2StoredFile> OriginalAttachments { get; set; } =
        Array.Empty<CopiersMaintenanceV2StoredFile>();
    public IReadOnlyList<CopiersMaintenanceV2StoredFile> CustomerAttachments { get; set; } =
        Array.Empty<CopiersMaintenanceV2StoredFile>();
    public CopiersMaintenanceV2StoredFile SignedReport { get; set; } = new();
    public CopiersMaintenanceV2EmailOutboxSnapshot EmailOutbox { get; set; } = new();
}

public sealed class CopiersMaintenanceV2BaseSnapshot
{
    public string TechnicianSystemUserId { get; set; } = "";
    public string TechnicianName { get; set; } = "";
    public string TechnicianEmail { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string CustomerContactName { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    public string EquipmentSerial { get; set; } = "";
    public string Title { get; set; } = "";
    public DateOnly ServiceDate { get; set; }
    public int? MaintenanceTypeValue { get; set; }

    public static CopiersMaintenanceV2BaseSnapshot From(CopiersMaintenanceV2DraftRecord record) => new()
    {
        TechnicianSystemUserId = record.TechnicianSystemUserId,
        TechnicianName = record.TechnicianName,
        TechnicianEmail = record.TechnicianEmail,
        ClientId = record.ClientId,
        ClientName = record.ClientName,
        CustomerContactName = record.CustomerContactName,
        CustomerEmail = record.CustomerEmail,
        EquipmentId = record.EquipmentId,
        EquipmentSerial = record.EquipmentSerial,
        Title = record.Title,
        ServiceDate = record.ServiceDate,
        MaintenanceTypeValue = record.MaintenanceTypeValue
    };
}

public sealed class CopiersMaintenanceV2FinalizationFailedCommand
{
    public string RecordId { get; set; } = "";
    public string SubmissionKey { get; set; } = "";
    public string FinalizationLeaseId { get; set; } = "";
    public string TechnicianSystemUserId { get; set; } = "";
    public string ErrorCode { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public DateTimeOffset FailedAtUtc { get; set; }
}

public sealed class CopiersMaintenanceV2RenderedPdf
{
    public string FileName { get; set; } = "";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

public sealed class CopiersMaintenanceV2EmailOutboxSnapshot
{
    public string OutboxKey { get; set; } = "";
    public IReadOnlyList<string> To { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Cc { get; set; } = Array.Empty<string>();
    public string Subject { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class CopiersMaintenanceV2ValidationException : InvalidOperationException
{
    public CopiersMaintenanceV2ValidationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class CopiersMaintenanceV2ConcurrencyException : InvalidOperationException
{
    public CopiersMaintenanceV2ConcurrencyException(string message) : base(message)
    {
    }
}

public sealed class CopiersMaintenanceV2PersistenceException : InvalidOperationException
{
    public CopiersMaintenanceV2PersistenceException(string message) : base(message)
    {
    }
}

