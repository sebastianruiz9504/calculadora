using Microsoft.AspNetCore.Http;

namespace CotizadorInterno.Web.Models.CopiersMtoV2;

public enum CopiersMaintenanceV2WorkflowState
{
    Draft = 0,
    Finalizing = 1,
    ReadyToSend = 2,
    Failed = 3
}

public enum CopiersMaintenanceV2EmailState
{
    NotReady = 0,
    Pending = 1,
    Processing = 2,
    Sent = 3,
    Failed = 4
}

public sealed class CopiersMaintenanceV2ActorContext
{
    public string SystemUserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
}

public sealed class CopiersMaintenanceV2DraftRequestDto
{
    public string SubmissionKey { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string CustomerContactName { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    public string EquipmentSerial { get; set; } = "";
    public string Title { get; set; } = "";
    public DateOnly ServiceDate { get; set; }
    public int? MaintenanceTypeValue { get; set; }
}

public sealed class CopiersMaintenanceV2DraftUpdateRequestDto
{
    public string RecordId { get; set; } = "";
    public string SubmissionKey { get; set; } = "";
    public string ExpectedVersion { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string CustomerContactName { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    public string EquipmentSerial { get; set; } = "";
    public string Title { get; set; } = "";
    public DateOnly ServiceDate { get; set; }
    public int? MaintenanceTypeValue { get; set; }
}

/// <summary>
/// Transport contract for a future multipart/form-data finalization endpoint.
/// Geolocation is accepted here only so the backend can persist it internally.
/// It is deliberately absent from <see cref="CopiersMaintenanceV2PdfModel"/>.
/// </summary>
public sealed class CopiersMaintenanceV2FinalizeMultipartRequestDto
{
    public string RecordId { get; set; } = "";
    public string SubmissionKey { get; set; } = "";
    public string ExpectedVersion { get; set; } = "";
    public string FormVersion { get; set; } = "";
    public string AnswersJson { get; set; } = "[]";
    public string WorkPerformed { get; set; } = "";
    public string CustomerObservations { get; set; } = "";
    public string CustomerContactName { get; set; } = "";
    public string ServiceAddress { get; set; } = "";
    public string InternalNotes { get; set; } = "";
    public string SignerName { get; set; } = "";
    public string SignerRole { get; set; } = "";
    public bool CustomerAccepted { get; set; }
    public DateTimeOffset? DeviceSignedAtUtc { get; set; }

    // Internal-only capture. These values must never be copied into a PDF/email model.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
    public DateTimeOffset? LocationCapturedAtUtc { get; set; }
    public string LocationSource { get; set; } = "navigator.geolocation";
    public int SignaturePointCount { get; set; }

    public IFormFile? Signature { get; set; }
    public List<IFormFile> Attachments { get; set; } = new();
}

public sealed class CopiersMaintenanceV2FormAnswerInputDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public int SortOrder { get; set; }
}

public sealed class CopiersMaintenanceV2FormAnswerSnapshot
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public int SortOrder { get; set; }
}

public sealed class CopiersMaintenanceV2DraftResultDto
{
    public string RecordId { get; set; } = "";
    public string SubmissionKey { get; set; } = "";
    public string Version { get; set; } = "";
    public CopiersMaintenanceV2WorkflowState State { get; set; }
    public CopiersMaintenanceV2EmailState EmailState { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public bool ReusedExisting { get; set; }
}

public sealed class CopiersMaintenanceV2FinalizeResultDto
{
    public string RecordId { get; set; } = "";
    public string SubmissionKey { get; set; } = "";
    public string Version { get; set; } = "";
    public CopiersMaintenanceV2WorkflowState State { get; set; }
    public CopiersMaintenanceV2EmailState EmailState { get; set; }
    public string ReportFileName { get; set; } = "";
    public string ReportSha256 { get; set; } = "";
    public int AttachmentCount { get; set; }
    public DateTimeOffset ServerFinalizedAtUtc { get; set; }
    public bool IdempotentReplay { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>
/// Immutable input for the signed customer report. Location is intentionally not
/// represented here, preventing a renderer from including it accidentally.
/// </summary>
public sealed class CopiersMaintenanceV2PdfModel
{
    public string RecordId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string CustomerContactName { get; set; } = "";
    public string EquipmentSerial { get; set; } = "";
    public string Title { get; set; } = "";
    public DateOnly ServiceDate { get; set; }
    public string TechnicianName { get; set; } = "";
    public string FormVersion { get; set; } = "";
    public IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> Answers { get; set; } =
        Array.Empty<CopiersMaintenanceV2FormAnswerSnapshot>();
    public string WorkPerformed { get; set; } = "";
    public string CustomerObservations { get; set; } = "";
    public string SignerName { get; set; } = "";
    public string SignerRole { get; set; } = "";
    public DateTimeOffset DeviceSignedAtUtc { get; set; }
    public DateTimeOffset ServerFinalizedAtUtc { get; set; }
    public byte[] SignatureContent { get; set; } = Array.Empty<byte>();
    public string SignatureContentType { get; set; } = "image/png";
    public IReadOnlyList<CopiersMaintenanceV2PdfAttachmentManifestItem> Attachments { get; set; } =
        Array.Empty<CopiersMaintenanceV2PdfAttachmentManifestItem>();
}

public sealed class CopiersMaintenanceV2PdfAttachmentManifestItem
{
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}

