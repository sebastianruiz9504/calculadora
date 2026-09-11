namespace CotizadorInterno.Web.Models.CopiersMtoV2;

/// <summary>Authoritative identity and immutable, signed business facts supplied by the activity lifecycle.</summary>
public sealed record CopiersActivityV2BusinessCommand
{
    public string ReportRecordId { get; init; } = "";
    public string SubmissionKey { get; init; } = "";
    public string FinalizationFingerprint { get; init; } = "";
    public string ActivityKind { get; init; } = "";
    public string ClientId { get; init; } = "";
    public string EquipmentId { get; init; } = "";
    public string EquipmentSerial { get; init; } = "";
    public string TechnicianSystemUserId { get; init; } = "";
    public string ServiceReference { get; init; } = "";
    public DateOnly ServiceDate { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public string OriginClientId { get; init; } = "";
    public string OriginClientName { get; init; } = "";
    public string MovementReason { get; init; } = "";
    public CopiersEquipmentOperation? Operation { get; init; }
    public string SupplyId { get; init; } = "";
    public string SupplyName { get; init; } = "";
    public int SupplyQuantity { get; init; }
    public int? SupplyStockBefore { get; init; }
}

public sealed record CopiersActivityV2BusinessResult(string BusinessRecordId, string ServiceReference, bool ReusedExisting);
