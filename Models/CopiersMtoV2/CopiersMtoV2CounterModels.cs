namespace CotizadorInterno.Web.Models.CopiersMtoV2;

public sealed class CopiersMtoV2CounterReadingDto
{
    public string RecordId { get; init; } = "";
    public string EquipmentId { get; init; } = "";
    public string DateValue { get; init; } = "";
    public string DateDisplay { get; init; } = "";
    public DateTimeOffset? RecordedAtUtc { get; init; }
    public long? CopiesCounter { get; init; }
    public long? ScansCounter { get; init; }
}

/// <summary>Server-owned maintenance identity plus the readings accepted in the signed snapshot.</summary>
public sealed class CopiersMtoV2CounterSaveCommand
{
    public string MaintenanceRecordId { get; init; } = "";
    public string SubmissionKey { get; init; } = "";
    public string FinalizationFingerprint { get; init; } = "";
    public string ClientId { get; init; } = "";
    public string EquipmentId { get; init; } = "";
    public string EquipmentSerial { get; init; } = "";
    public DateTimeOffset ReadingAtUtc { get; init; }
    public long? CopiesCounter { get; init; }
    public long? ScansCounter { get; init; }
    public string PreviousCounterRecordId { get; init; } = "";
    public long? PreviousCopiesCounter { get; init; }
    public long? PreviousScansCounter { get; init; }
    public string PreviousDateValue { get; init; } = "";
}

public sealed record CopiersMtoV2CounterSaveResult(string RecordId, bool ReusedExisting);
