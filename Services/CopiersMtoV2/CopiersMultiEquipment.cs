using System.Globalization;
using System.Text.Json;
using CotizadorInterno.Web.Models.CopiersMtoV2;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

// Additional equipment is part of the immutable signed answers in the existing
// MTO row. Its own counter identity includes both maintenance and equipment IDs.
internal static class CopiersMultiEquipment
{
    internal const string Prefix = "equipment_item_";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal sealed class Item
    {
        public string EquipmentId { get; set; } = "";
        public string Serial { get; set; } = "";
        public string Reference { get; set; } = "";
        public string WorkPerformed { get; set; } = "";
        public string CounterRecordId { get; set; } = "";
        public string CounterDate { get; set; } = "";
        public long? CopiesBefore { get; set; }
        public long? CopiesAfter { get; set; }
        public long? ScansBefore { get; set; }
        public long? ScansAfter { get; set; }
    }
    internal static List<Item> Parse(CopiersMaintenanceV2FinalizeMultipartRequestDto request)
    {
        var text = request.AdditionalEquipmentJson ?? "[]";
        if (text.Length > 40000) throw Invalid("El detalle de equipos supera el tamaño permitido.");
        List<Item> items;
        try { items = JsonSerializer.Deserialize<List<Item>>(string.IsNullOrWhiteSpace(text) ? "[]" : text, Json) ?? throw new JsonException(); }
        catch (JsonException) { throw Invalid("El detalle por equipo no es válido."); }
        if (items.Count > 9 || items.Any(x => x is null)) throw Invalid("Puedes incluir hasta diez equipos por mantenimiento.");
        if (items.Count > 0 && (request.ActivityKind != "maintenance" || request.FormVersion != CopiersMtoV2CompactCapture.FormVersion))
            throw Invalid("La selección de varios equipos solo corresponde a mantenimientos.");
        foreach (var item in items)
        {
            item.EquipmentId = CopiersMaintenanceV2Validation.RequiredGuid(item.EquipmentId, "equipment_invalid", "El equipo adicional");
            item.Serial = CopiersMaintenanceV2Validation.Required(item.Serial, "serial_required", "el serial", 200);
            item.WorkPerformed = CopiersMaintenanceV2Validation.Required(item.WorkPerformed, "work_performed_required", $"el trabajo del serial {item.Serial}", CopiersMtoV2CompactCapture.WorkMaxLength);
            item.Reference = CopiersMaintenanceV2Validation.Optional(item.Reference, "reference_long", "La referencia", 200);
            item.CounterRecordId = CopiersMaintenanceV2Validation.OptionalGuid(item.CounterRecordId, "counter_invalid", "El contador anterior");
            item.CounterDate = CopiersMaintenanceV2Validation.Optional(item.CounterDate, "counter_date_invalid", "La fecha del contador", 10);
            if (item.CopiesAfter is null or < 0 or > int.MaxValue || item.ScansAfter is null or < 0 or > int.MaxValue
                || item.CopiesBefore is < 0 or > int.MaxValue || item.ScansBefore is < 0 or > int.MaxValue
                || item.CopiesAfter < item.CopiesBefore || item.ScansAfter < item.ScansBefore)
                throw Invalid($"Revisa los contadores actuales del serial {item.Serial}.");
            if (item.CounterRecordId.Length == 0 && (item.CopiesBefore.HasValue || item.ScansBefore.HasValue || item.CounterDate.Length > 0)
                || item.CounterRecordId.Length > 0 && !DateOnly.TryParseExact(item.CounterDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw Invalid($"Revisa la lectura anterior del serial {item.Serial}.");
        }
        if (items.Select(x => x.EquipmentId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count
            || items.Select(x => x.Serial).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count)
            throw Invalid("No puedes incluir el mismo equipo dos veces.");
        return items;
    }
    internal static void Authorize(CopiersMaintenanceV2FinalizeMultipartRequestDto request, CopiersMaintenanceV2DraftRequestDto draft,
        IEnumerable<(string Id, string ClientId, string Serial, string Reference, bool InStock)> equipment)
    {
        var items = Parse(request);
        foreach (var item in items)
        {
            var row = equipment.FirstOrDefault(x => string.Equals(x.Id, item.EquipmentId, StringComparison.OrdinalIgnoreCase));
            if (row.Id is null || row.InStock || !string.Equals(row.ClientId, draft.ClientId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row.Serial, item.Serial, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.EquipmentId, draft.EquipmentId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Serial, draft.EquipmentSerial, StringComparison.OrdinalIgnoreCase))
                throw Invalid("Un equipo adicional no pertenece al cliente, cambió o está repetido. Revisa los seriales.");
            if (!string.Equals(row.Reference ?? "", item.Reference, StringComparison.Ordinal))
                throw Invalid($"La referencia del serial {item.Serial} cambió. Actualiza el equipo antes de firmar.");
        }
    }
    internal static IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> Append(CopiersMaintenanceV2FinalizeMultipartRequestDto request,
        CopiersMaintenanceV2DraftRecord record, IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> answers)
    {
        var result = answers.ToList();
        foreach (var (item, index) in Parse(request).Select((x, i) => (x, i)))
        {
            if (item.EquipmentId.Equals(record.EquipmentId, StringComparison.OrdinalIgnoreCase) || item.Serial.Equals(record.EquipmentSerial, StringComparison.OrdinalIgnoreCase))
                throw Invalid("No puedes repetir el equipo principal.");
            result.Add(new() { Key = Prefix + (index + 2), Label = "Detalle estructurado por equipo", Value = JsonSerializer.Serialize(item, Json), SortOrder = 40 + index * 2 });
            result.Add(new() { Key = "equipment_detail_" + (index + 2), Label = $"Equipo {index + 2} · {item.Serial}",
                Value = $"{item.Reference}\nTrabajo: {item.WorkPerformed}\nContador anterior ({(item.CounterDate.Length > 0 ? item.CounterDate : "sin registro")}): impresión {item.CopiesBefore?.ToString() ?? "—"}; escaneo {item.ScansBefore?.ToString() ?? "—"}.\nActual: impresión {item.CopiesAfter}; escaneo {item.ScansAfter}.", SortOrder = 41 + index * 2 });
        }
        return result;
    }
    internal static List<Item> Read(IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> answers) => answers
        .Where(x => x.Key.StartsWith(Prefix, StringComparison.Ordinal)).OrderBy(x => x.SortOrder)
        .Select(x => JsonSerializer.Deserialize<Item>(x.Value, Json) ?? throw Invalid("Detalle por equipo inválido.")).ToList();
    internal static CopiersMtoV2CounterSaveCommand Counter(Item item, CopiersMaintenanceV2DraftRecord record, DateTimeOffset ended, string fingerprint) => new()
    {
        MaintenanceRecordId = record.RecordId, AdditionalEquipmentScope = item.EquipmentId, SubmissionKey = record.SubmissionKey,
        ClientId = record.ClientId, EquipmentId = item.EquipmentId, EquipmentSerial = item.Serial, FinalizationFingerprint = fingerprint,
        ReadingAtUtc = ended, CopiesCounter = item.CopiesAfter, ScansCounter = item.ScansAfter,
        PreviousCounterRecordId = item.CounterRecordId, PreviousDateValue = item.CounterDate,
        PreviousCopiesCounter = item.CopiesBefore, PreviousScansCounter = item.ScansBefore
    };
    private static CopiersMaintenanceV2ValidationException Invalid(string message) => new("multi_equipment_invalid", message);
}
