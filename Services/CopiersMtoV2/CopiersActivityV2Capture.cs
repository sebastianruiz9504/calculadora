using System.Globalization;
using CotizadorInterno.Web.Models.CopiersMtoV2;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

internal static class CopiersActivityV2Capture
{
    internal static string Value(IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> answers, string key) =>
        answers.FirstOrDefault(x => x.Key == key)?.Value.Trim() ?? "";

    public static IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> Canonicalize(
        CopiersMaintenanceV2FinalizeMultipartRequestDto request, CopiersMaintenanceV2DraftRecord row,
        IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> answers)
    {
        var kind = row.MaintenanceTypeValue switch { CopiersActivityV2Bindings.MovementType => "movement",
            CopiersActivityV2Bindings.TonerType => "toner", _ => throw Invalid("Tipo de atención no válido.") };
        Match(Value(answers, "activity_kind"), kind);
        Match(request.ActivityKind, kind);
        var started = request.ServiceStartedAtUtc ?? throw Invalid("Falta la hora de entrada.");
        var ended = request.ServiceEndedAtUtc ?? throw Invalid("Falta la hora de salida.");
        if (started > ended || request.DeviceSignedAtUtc is null || ended > request.DeviceSignedAtUtc
            || DateOnly.FromDateTime(started.ToOffset(TimeSpan.FromHours(-5)).DateTime) != row.ServiceDate)
            throw Invalid("Revisa la entrada y salida de la visita antes de firmar.");
        foreach (var (key, value) in new[] { ("service_started_at_utc", started), ("service_ended_at_utc", ended) })
            if (!DateTimeOffset.TryParse(Value(answers, key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var snapshot)
                || snapshot.ToUniversalTime() != value.ToUniversalTime()) throw Invalid("Los horarios cambiaron después de firmar.");
        if (kind == "movement")
        {
            var reason = CopiersMaintenanceV2Validation.Required(request.MovementReason, "movement_reason_required", "el motivo del movimiento", 100);
            Match(Value(answers, "movement_reason"), reason);
            Match(Value(answers, "origin_client_id"), request.OriginClientId, ignoreCase:true);
            if (!string.IsNullOrEmpty(request.OriginClientId)) _ = CopiersMaintenanceV2Validation.RequiredGuid(request.OriginClientId, "origin_invalid", "El origen");
            _ = CopiersMaintenanceV2Validation.Required(Value(answers, "origin_client_name"), "origin_required", "el origen", 850);
            request.WorkPerformed = reason;
        }
        else
        {
            _ = CopiersMaintenanceV2Validation.RequiredGuid(request.SupplyId, "supply_invalid", "El suministro");
            Match(Value(answers, "supply_id"), request.SupplyId, ignoreCase:true);
            Match(Value(answers, "supply_quantity"), request.SupplyQuantity.ToString(CultureInfo.InvariantCulture));
            var name = CopiersMaintenanceV2Validation.Required(Value(answers, "supply_name"), "supply_name_required", "el suministro", 850);
            if (request.SupplyQuantity <= 0) throw Invalid("La cantidad de tóner debe ser un entero mayor que cero.");
            if (!int.TryParse(Value(answers, "supply_stock_before"), NumberStyles.None, CultureInfo.InvariantCulture, out var stock)
                || stock < request.SupplyQuantity) throw Invalid("La cantidad supera el saldo del suministro.");
            request.WorkPerformed = $"Entrega de {request.SupplyQuantity.ToString(CultureInfo.InvariantCulture)} unidad(es) de {name}.";
        }
        _ = CopiersMaintenanceV2Validation.Optional(request.CustomerObservations, "observations_too_long", "Las observaciones", 250);
        var canonical = answers.Select(x => new CopiersMaintenanceV2FormAnswerSnapshot {
            Key=x.Key, Label=x.Label, SortOrder=x.SortOrder, Value=x.Key switch {
                "service_started_at" => started.ToOffset(TimeSpan.FromHours(-5)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                "service_ended_at" => ended.ToOffset(TimeSpan.FromHours(-5)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                "service_started_at_utc" => started.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                "service_ended_at_utc" => ended.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                "destination_client_name" => row.ClientName,
                _ => x.Value }
        }).ToArray();
        var operation = CopiersEquipmentOperation.Read(request.MovementDetailsJson);
        if (operation is null) return canonical;
        if (kind != "movement" || operation.Internal || operation.ClientId != row.ClientId || operation.Equipment.Id != row.EquipmentId || operation.Reason != request.MovementReason)
            throw Invalid("La operación no corresponde al certificado firmado.");
        return canonical.Concat(OperationAnswers(operation)).ToArray();
    }

    internal static IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> OperationAnswers(CopiersEquipmentOperation operation)
    {
        var values = new Dictionary<string,string> {
            ["equipment_operation"]=System.Text.Json.JsonSerializer.Serialize(operation), ["operation_kind"]=operation.Kind,
            ["operation_title"]=operation.Title, ["equipment_condition"]=operation.Equipment.Condition,
            ["equipment_accessories"]=operation.Equipment.Accessories,
            ["operation_link"]=operation.PendingKey,
            ["replacement_serial"]=operation.Replacement?.Serial ?? "", ["replacement_reference"]=operation.Replacement?.Reference ?? "",
            ["replacement_condition"]=operation.Replacement?.Condition ?? "", ["replacement_accessories"]=operation.Replacement?.Accessories ?? ""
        };
        return values.Select((x,i)=>new CopiersMaintenanceV2FormAnswerSnapshot {Key=x.Key,Label=x.Key switch {
            "operation_kind"=>"Clase de operación", "operation_title"=>"Certificado", "equipment_condition"=>"Estado del equipo", "equipment_accessories"=>"Accesorios del equipo",
            "replacement_serial"=>"Serial del reemplazo", "replacement_reference"=>"Referencia del reemplazo", "replacement_condition"=>"Estado del reemplazo", "replacement_accessories"=>"Accesorios del reemplazo", _=>x.Key
        },Value=x.Value,SortOrder=60+i}).ToArray();
    }

    public static CopiersActivityV2BusinessCommand Command(CopiersMaintenanceV2DraftRecord row,
        IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> answers, DateTimeOffset ended, string fingerprint) => new()
    {
        ReportRecordId=row.RecordId, SubmissionKey=row.SubmissionKey, FinalizationFingerprint=fingerprint,
        ActivityKind=Value(answers,"activity_kind"), ClientId=row.ClientId, EquipmentId=row.EquipmentId,
        EquipmentSerial=row.EquipmentSerial, TechnicianSystemUserId=row.TechnicianSystemUserId,
        ServiceReference=row.ServiceReference, ServiceDate=row.ServiceDate, OccurredAtUtc=ended.ToUniversalTime(),
        OriginClientId=Value(answers,"origin_client_id"), OriginClientName=Value(answers,"origin_client_name"),
        MovementReason=Value(answers,"movement_reason"), SupplyId=Value(answers,"supply_id"), SupplyName=Value(answers,"supply_name"),
        Operation=CopiersEquipmentOperation.Read(Value(answers,"equipment_operation")),
        SupplyQuantity=int.TryParse(Value(answers,"supply_quantity"), NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) ? quantity : 0,
        SupplyStockBefore=int.TryParse(Value(answers,"supply_stock_before"), NumberStyles.None, CultureInfo.InvariantCulture, out var stock) ? stock : null
    };

    private static void Match(string actual, string? expected, bool ignoreCase=false)
    {
        if (!string.Equals(actual, expected?.Trim() ?? "", ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw Invalid("Los datos de la atención cambiaron. Revisa el formulario y firma nuevamente.");
    }
    private static CopiersMaintenanceV2ValidationException Invalid(string message) => new("activity_snapshot_invalid", message);
}
