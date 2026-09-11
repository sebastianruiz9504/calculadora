using System.Text.Json;

namespace CotizadorInterno.Web.Models.CopiersMtoV2;

// Server-resolved snapshot. Never accepted as authoritative from a browser.
public sealed record CopiersEquipmentOperation
{
    public string Kind { get; init; } = "";
    public string ClientId { get; init; } = "";
    public string DestinationId { get; init; } = "";
    public string DestinationName { get; init; } = "";
    public bool Internal { get; init; }
    public string PendingKey { get; init; } = "";
    public string Reason { get; init; } = "";
    public CopiersOperationEquipment Equipment { get; init; } = new();
    public CopiersOperationEquipment? Replacement { get; init; }
    public string Title => Kind switch {
        "delivery" => "Certificado de entrega", "replacement" => "Certificado de cambio",
        "withdrawal" => "Certificado de retiro", "receipt" => Internal ? "Recepción interna" : "Certificado de entrega",
        "internal" => "Salida interna", _ => throw new InvalidOperationException("Operación no válida.") };
    public bool StartsTransit => Kind is "withdrawal" or "replacement" or "internal";
    public static CopiersEquipmentOperation? Read(string? json) => string.IsNullOrWhiteSpace(json) ? null
        : JsonSerializer.Deserialize<CopiersEquipmentOperation>(json) ?? throw new InvalidOperationException("Operación no válida.");
}

public sealed record CopiersOperationEquipment
{
    public string Id { get; init; } = "";
    public string Serial { get; init; } = "";
    public string Reference { get; init; } = "";
    public string OriginId { get; init; } = "";
    public string OriginName { get; init; } = "";
    public string Condition { get; init; } = "";
    public string Accessories { get; init; } = "";
}

public sealed record CopiersEquipmentTransit
{
    public string OperationKey { get; init; } = "";
    public string OriginId { get; init; } = "";
    public string OriginName { get; init; } = "";
    public string DestinationId { get; init; } = "";
    public string DestinationName { get; init; } = "";
    public string CustodianId { get; init; } = "";
    public string DepartedAtUtc { get; init; } = "";
}

public sealed class CopiersEquipmentOperationsOptions
{
    public bool Enabled { get; set; }
    public string[] InternalClientIds { get; set; } = [];
    public bool IsInternal(string? id) => InternalClientIds.Contains(id ?? "", StringComparer.OrdinalIgnoreCase);
}
