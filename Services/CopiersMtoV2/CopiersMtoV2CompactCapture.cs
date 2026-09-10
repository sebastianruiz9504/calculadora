using System.Globalization;
using CotizadorInterno.Web.Models.CopiersMtoV2;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

/// <summary>Versioned signed facts for the compact form; legacy retries keep their original contract.</summary>
internal static class CopiersMtoV2CompactCapture
{
    public const string FormVersion = "copiers-mto-v2-2026-09-10";
    public const int WorkMaxLength = 1000;
    public const int NotesMaxLength = 250;
    private static readonly TimeSpan BogotaOffset = TimeSpan.FromHours(-5);

    public static IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> Canonicalize(
        CopiersMaintenanceV2FinalizeMultipartRequestDto request,
        CopiersMaintenanceV2DraftRecord record,
        IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> answers)
    {
        var started = request.ServiceStartedAtUtc?.ToUniversalTime()
            ?? throw Invalid("visit_started_required", "Falta la hora de entrada de la visita.");
        var ended = request.ServiceEndedAtUtc?.ToUniversalTime()
            ?? throw Invalid("visit_ended_required", "Falta la hora de salida que debe mostrarse antes de firmar.");
        if (started > ended || request.DeviceSignedAtUtc is null || ended > request.DeviceSignedAtUtc.Value)
            throw Invalid("visit_time_order_invalid", "La entrada debe ser anterior a la salida y la salida no puede ser posterior a la firma.");
        if (DateOnly.FromDateTime(started.ToOffset(BogotaOffset).DateTime) != record.ServiceDate)
            throw Invalid("visit_date_mismatch", "La fecha del mantenimiento no coincide con su hora de entrada.");
        RequireMatchingInstant(answers, "service_started_at_utc", started);
        RequireMatchingInstant(answers, "service_ended_at_utc", ended);
        _ = CopiersMaintenanceV2Validation.Required(request.WorkPerformed, "work_performed_required", "el trabajo realizado", WorkMaxLength);
        _ = CopiersMaintenanceV2Validation.Optional(request.CustomerObservations, "customer_observations_too_long", "Las observaciones del cliente", NotesMaxLength);
        _ = CopiersMaintenanceV2Validation.Optional(Value(answers, "recommendations"), "recommendations_too_long", "Las recomendaciones", NotesMaxLength);
        var copiesBefore = Counter(answers, "copies_before", required: false);
        var copiesAfter = Counter(answers, "copies_after", required: true);
        var scansBefore = Counter(answers, "scans_before", required: false);
        var scansAfter = Counter(answers, "scans_after", required: true);

        return answers.Select(answer => new CopiersMaintenanceV2FormAnswerSnapshot
        {
            Key = answer.Key, Label = answer.Label, SortOrder = answer.SortOrder,
            Value = answer.Key switch
            {
                "service_started_at" => FormatVisit(started),
                "service_ended_at" => FormatVisit(ended),
                "service_started_at_utc" => started.ToString("O", CultureInfo.InvariantCulture),
                "service_ended_at_utc" => ended.ToString("O", CultureInfo.InvariantCulture),
                "copies_before" => Number(copiesBefore),
                "copies_after" => Number(copiesAfter),
                "scans_before" => Number(scansBefore),
                "scans_after" => Number(scansAfter),
                "counters" => $"Impresión: {Display(copiesBefore)} / {Display(copiesAfter)}. Escaneo: {Display(scansBefore)} / {Display(scansAfter)}.",
                _ => answer.Value
            }
        }).ToList();
    }

    public static CopiersMtoV2CounterSaveCommand CounterCommand(
        CopiersMaintenanceV2DraftRecord record,
        IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> answers,
        DateTimeOffset endedAtUtc,
        string finalizationFingerprint) => new()
    {
        MaintenanceRecordId = record.RecordId, SubmissionKey = record.SubmissionKey,
        FinalizationFingerprint = finalizationFingerprint,
        ClientId = record.ClientId, EquipmentId = record.EquipmentId, EquipmentSerial = record.EquipmentSerial,
        ReadingAtUtc = endedAtUtc.ToUniversalTime(),
        CopiesCounter = Counter(answers, "copies_after", true), ScansCounter = Counter(answers, "scans_after", true),
        PreviousCounterRecordId = Value(answers, "counter_record_id"),
        PreviousDateValue = Value(answers, "counter_recorded_at"),
        PreviousCopiesCounter = Counter(answers, "copies_before", false),
        PreviousScansCounter = Counter(answers, "scans_before", false)
    };

    private static void RequireMatchingInstant(IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> answers, string key, DateTimeOffset expected)
    {
        if (!DateTimeOffset.TryParse(Value(answers, key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var actual)
            || actual.ToUniversalTime() != expected)
            throw Invalid("visit_time_snapshot_mismatch", "Los horarios cambiaron después de preparar la firma. Revisa el servicio y firma nuevamente.");
    }

    private static long? Counter(IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> answers, string key, bool required)
    {
        var value = Value(answers, key);
        if (string.IsNullOrWhiteSpace(value) && !required) return null;
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0 || parsed > int.MaxValue)
            throw Invalid("counter_value_invalid", "Registra los contadores actuales como números enteros entre 0 y 2.147.483.647.");
        return parsed;
    }

    private static string Value(IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> answers, string key) =>
        answers.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal))?.Value?.Trim() ?? "";
    private static string Number(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
    private static string Display(long? value) => value?.ToString("N0", CultureInfo.GetCultureInfo("es-CO")) ?? "Sin registro";
    private static string FormatVisit(DateTimeOffset value) => value.ToOffset(BogotaOffset).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    private static CopiersMaintenanceV2ValidationException Invalid(string code, string message) => new(code, message);
}
