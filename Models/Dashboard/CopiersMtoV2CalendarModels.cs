namespace CotizadorInterno.Web.Models.Dashboard;

public sealed record CopiersMtoV2CalendarTechnicianDto(string Id, string Name, string Email);
public sealed record CopiersMtoV2CalendarBootstrapDto(
    IReadOnlyList<CopiersMtoV2CalendarTechnicianDto> Technicians,
    string DefaultTechnicianId,
    string TimeZone = "America/Bogota",
    bool ActivitiesEnabled = false);
public sealed record CopiersMtoV2CalendarWeekDto(
    string WeekStart,
    IReadOnlyList<CopiersMtoV2CalendarEventDto> Events,
    string TimeZone = "America/Bogota");

public class CopiersMtoV2CalendarEventDto
{
    public string Id { get; set; } = "";
    public string ServiceReference { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string MaintenanceType { get; set; } = "";
    public string ActivityKind { get; set; } = "maintenance";
    public string TechnicianId { get; set; } = "";
    public string TechnicianName { get; set; } = "";
    public DateTimeOffset StartAtUtc { get; set; }
    public DateTimeOffset EndAtUtc { get; set; }
    public bool DurationEstimated { get; set; }
    public string TimingNote { get; set; } = "";
    public string WorkflowState { get; set; } = "ReadyToSend";
    public string EmailState { get; set; } = "";
}

public sealed class CopiersMtoV2CalendarDetailDto : CopiersMtoV2CalendarEventDto
{
    public string ClientContactName { get; set; } = "";
    public string ClientEmail { get; set; } = "";
    public string EquipmentSerial { get; set; } = "";
    public string Title { get; set; } = "";
    public string TechnicianEmail { get; set; } = "";
    public string ServiceDate { get; set; } = "";
    public string FormVersion { get; set; } = "";
    public DateTimeOffset? DeviceSignedAtUtc { get; set; }
    public DateTimeOffset? ServerFinalizedAtUtc { get; set; }
    public string WorkPerformed { get; set; } = "";
    public string CustomerObservations { get; set; } = "";
    public string ServiceAddress { get; set; } = "";
    public string InternalNotes { get; set; } = "";
    public string SignerName { get; set; } = "";
    public string SignerRole { get; set; } = "";
    public bool CustomerAccepted { get; set; }
    public IReadOnlyList<CopiersMtoV2CalendarAnswerDto> Answers { get; set; } = [];
    public IReadOnlyList<CopiersMtoV2CalendarEvidenceDto> Evidences { get; set; } = [];
    public string ReportUrl { get; set; } = "";
    public string SignatureUrl { get; set; } = "";
    public CopiersMtoV2CalendarLocationDto? Location { get; set; }
    public bool InternalOperation { get; set; }
    public IReadOnlyList<CopiersMtoV2CalendarAnswerDto> MovementDetails { get; set; } = [];
    public string RelatedActivityId { get; set; } = "";
}

public sealed record CopiersMtoV2CalendarAnswerDto(string Key, string Label, string Value);
public sealed record CopiersMtoV2CalendarLocationDto(
    double Latitude, double Longitude, double? AccuracyMeters, DateTimeOffset? CapturedAtUtc, string Source);
public sealed record CopiersMtoV2CalendarEvidenceDto(
    string EvidenceKey, string FileName, string ContentType, long SizeBytes, string Purpose, string Url);
public sealed record CopiersMtoV2CalendarFile(byte[] Content, string ContentType, string FileName);
