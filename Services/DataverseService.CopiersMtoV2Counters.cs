using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using CotizadorInterno.Web.Services.CopiersMtoV2;

namespace CotizadorInterno.Web.Services;

/// <summary>
/// V2 writes into the existing counters table with the signed-in technician's
/// delegated permissions. No app-only role, schema change or legacy route changes.
/// </summary>
public sealed partial class DataverseService : ICopiersMtoV2CounterService
{
    public async Task<CopiersMtoV2CounterReadingDto> GetLatestAsync(
        string clientId, string equipmentId, CancellationToken ct = default)
    {
        var user = RequireCopiersCounterUser();
        var scope = await ValidateCopiersCounterEquipmentAsync(clientId, equipmentId, null, user, ct);
        var metadata = await ResolveCopiersMtoCounterMetadataAsync(user, ct);
        var filter = $"{BuildDashboardLookupValuePropertyName(CopiersLegacyCountersEquipmentField)} eq {scope.Equipment.RecordId}";
        var order = $"{CopiersLegacyCountersDateField} desc,createdon desc,{metadata.PrimaryIdField} desc";
        var path = $"/api/data/v9.2/{metadata.EntitySetName}?$select={CopiersMtoCounterSelect(metadata)}"
            + $"&$filter={Uri.EscapeDataString(filter)}&$orderby={Uri.EscapeDataString(order)}&$top=1";
        using var document = JsonDocument.Parse(await CallDataverseGetJsonAsync(path, user, ct));
        var items = document.RootElement.GetProperty("value");
        return items.GetArrayLength() == 0
            ? new CopiersMtoV2CounterReadingDto { EquipmentId = scope.Equipment.RecordId }
            : MapCopiersMtoCounter(items[0], metadata.PrimaryIdField);
    }

    public async Task<bool> ValidateForMaintenanceAsync(CopiersMtoV2CounterSaveCommand command, CancellationToken ct = default)
    {
        var normalized = ValidateCopiersMtoCounterCommand(command);
        var user = RequireCopiersCounterUser();
        await ValidateCopiersCounterEquipmentAsync(command.ClientId, normalized.EquipmentId, command.EquipmentSerial, user, ct);
        var metadata = await ResolveCopiersMtoCounterMetadataAsync(user, ct);
        await ValidateCopiersMtoCounterHistoryAsync(command, normalized, metadata, user, ct);
        // A previous attempt may already have written the counter before an
        // uncertain finalization response. Changed content must never overwrite it.
        var existing = await ReadCopiersMtoCounterByIdAsync(normalized.CounterId, metadata, user, ct);
        if (existing.HasValue)
            EnsureCopiersMtoCounterMatches(existing.Value, command, normalized, metadata);
        return existing.HasValue;
    }

    public async Task<CopiersMtoV2CounterSaveResult> SaveForMaintenanceAsync(
        CopiersMtoV2CounterSaveCommand command, CancellationToken ct = default)
    {
        var normalized = ValidateCopiersMtoCounterCommand(command);
        var user = RequireCopiersCounterUser();
        var scope = await ValidateCopiersCounterEquipmentAsync(command.ClientId, normalized.EquipmentId, command.EquipmentSerial, user, ct);
        var metadata = await ResolveCopiersMtoCounterMetadataAsync(user, ct);

        // Read the stable ID first: transport retries never issue another create
        // for the same maintenance. This is not a best-effort random-ID insert.
        var existing = await ReadCopiersMtoCounterByIdAsync(normalized.CounterId, metadata, user, ct);
        if (existing.HasValue)
        {
            EnsureCopiersMtoCounterMatches(existing.Value, command, normalized, metadata);
            await ValidateCopiersMtoCounterHistoryAsync(command, normalized, metadata, user, ct);
            return new(normalized.CounterId, true);
        }
        await ValidateCopiersMtoCounterHistoryAsync(command, normalized, metadata, user, ct);
        var navigation = await ResolveRhLookupNavigationPropertyAsync(
            metadata.LogicalName, CopiersLegacyCountersEquipmentField, "cr07a_Maquina", user, ct);
        var payload = new Dictionary<string, object?>
        {
            [metadata.PrimaryNameField] = normalized.Name,
            [CopiersLegacyCountersDateField] = normalized.ReadingAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            [CopiersLegacyCountersCopiesField] = command.CopiesCounter!.Value,
            [CopiersLegacyCountersScansField] = command.ScansCounter!.Value,
            [$"{navigation}@odata.bind"] = $"/{scope.Metadata.EntitySetName}({normalized.EquipmentId})"
        };
        using var content = JsonContent.Create(payload);
        using var response = await _downstreamApi.CallApiForUserAsync("Dataverse", options =>
        {
            options.RelativePath = $"/api/data/v9.2/{metadata.EntitySetName}({normalized.CounterId})";
            options.HttpMethod = "PATCH";
            options.CustomizeHttpRequestMessage = request => request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        }, content: content, user: user, cancellationToken: ct);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.PreconditionFailed)
            throw new CopiersMaintenanceV2PersistenceException(
                $"No fue posible guardar los contadores del mantenimiento (HTTP {(int)response.StatusCode}). Puedes reintentar el mismo envío.");

        var persisted = await ReadCopiersMtoCounterByIdAsync(normalized.CounterId, metadata, user, ct)
            ?? throw new CopiersMaintenanceV2PersistenceException("No fue posible verificar el contador guardado. Reintenta el mismo envío.");
        EnsureCopiersMtoCounterMatches(persisted, command, normalized, metadata);
        return new(normalized.CounterId, response.StatusCode == HttpStatusCode.PreconditionFailed);
    }

    private ClaimsPrincipal RequireCopiersCounterUser()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        return user?.Identity?.IsAuthenticated == true ? user : throw new UnauthorizedAccessException("No hay un usuario autenticado.");
    }

    private Task<RhEntityMetadata> ResolveCopiersMtoCounterMetadataAsync(ClaimsPrincipal user, CancellationToken ct) =>
        ResolveRhEntityMetadataAsync(CopiersCountersLogicalName, CopiersLegacyCountersTableSetName,
            CopiersCountersPrimaryIdField, "cr07a_equipo", user, ct);

    private async Task<(RhEntityMetadata Metadata, CopiersEquipmentRecordRow Equipment)> ValidateCopiersCounterEquipmentAsync(
        string clientId, string equipmentId, string? expectedSerial, ClaimsPrincipal user, CancellationToken ct)
    {
        var client = CopiersMaintenanceV2Validation.RequiredGuid(clientId, "client_invalid", "El cliente");
        var equipmentKey = CopiersMaintenanceV2Validation.RequiredGuid(equipmentId, "equipment_invalid", "El equipo");
        var metadata = await ResolveRhEntityMetadataAsync(DashboardEquipmentTableLogicalName, DashboardEquipmentTableSetName,
            DashboardEquipmentIdField, DashboardEquipmentPrimaryNameField, user, ct);
        var equipment = await GetEquipmentRecordByIdAsync(metadata, equipmentKey, user, ct);
        if (equipment is null || equipment.InStock || !string.Equals(equipment.ClientId, client, StringComparison.OrdinalIgnoreCase))
            throw new CopiersMaintenanceV2ValidationException("counter_equipment_client_mismatch", "El equipo ya no está asignado al cliente seleccionado. Recarga el catálogo.");
        if (expectedSerial is not null && !string.Equals(equipment.Serial.Trim(), expectedSerial.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new CopiersMaintenanceV2ValidationException("counter_equipment_serial_mismatch", "El serial del equipo cambió. Recarga el catálogo antes de firmar.");
        return (metadata, equipment);
    }

    private async Task ValidateCopiersMtoCounterHistoryAsync(CopiersMtoV2CounterSaveCommand command,
        CopiersMtoCounterNormalized normalized, RhEntityMetadata metadata, ClaimsPrincipal user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.PreviousCounterRecordId))
        {
            if (command.PreviousCopiesCounter.HasValue || command.PreviousScansCounter.HasValue || !string.IsNullOrWhiteSpace(command.PreviousDateValue))
                throw new CopiersMaintenanceV2ValidationException("counter_history_missing", "Los contadores anteriores no tienen un registro de origen. Vuelve a seleccionar el equipo.");
            // Do not trust a hidden "no history" marker. Check only records
            // uploaded before the signed visit end, excluding our retry-safe row.
            // A later technician's reading must not change this signed snapshot.
            var cutoff = normalized.ReadingAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            var filter = $"{BuildDashboardLookupValuePropertyName(CopiersLegacyCountersEquipmentField)} eq {normalized.EquipmentId}"
                + $" and createdon le {cutoff} and {metadata.PrimaryIdField} ne {normalized.CounterId}";
            var path = $"/api/data/v9.2/{metadata.EntitySetName}?$select={metadata.PrimaryIdField}"
                + $"&$filter={Uri.EscapeDataString(filter)}&$top=1";
            using var history = JsonDocument.Parse(await CallDataverseGetJsonAsync(path, user, ct));
            if (history.RootElement.GetProperty("value").GetArrayLength() > 0)
                throw new CopiersMaintenanceV2ValidationException("counter_history_missing", "Existe una lectura anterior del equipo. Vuelve a seleccionarlo para cargar sus contadores antes de firmar.");
            return;
        }
        var previousId = CopiersMaintenanceV2Validation.RequiredGuid(command.PreviousCounterRecordId, "counter_history_invalid", "El contador anterior");
        if (string.Equals(previousId, normalized.CounterId, StringComparison.OrdinalIgnoreCase))
            throw new CopiersMaintenanceV2ValidationException("counter_history_invalid", "El contador anterior no puede ser el mismo registro del mantenimiento.");
        var raw = await ReadCopiersMtoCounterByIdAsync(previousId, metadata, user, ct)
            ?? throw new CopiersMaintenanceV2ValidationException("counter_history_missing", "El contador anterior ya no está disponible. Vuelve a seleccionar el equipo.");
        var previous = MapCopiersMtoCounter(raw, metadata.PrimaryIdField);
        if (!string.Equals(previous.EquipmentId, normalized.EquipmentId, StringComparison.OrdinalIgnoreCase)
            || previous.CopiesCounter != command.PreviousCopiesCounter || previous.ScansCounter != command.PreviousScansCounter
            || !string.Equals(previous.DateValue, command.PreviousDateValue, StringComparison.Ordinal))
            throw new CopiersMaintenanceV2ValidationException("counter_history_changed", "La lectura anterior cambió o no corresponde al equipo. Vuelve a seleccionar el equipo y revisa los contadores.");
        if (command.CopiesCounter < previous.CopiesCounter || command.ScansCounter < previous.ScansCounter)
            throw new CopiersMaintenanceV2ValidationException("counter_decreased", "Los contadores actuales no pueden ser menores que la lectura anterior.");
    }

    private async Task<JsonElement?> ReadCopiersMtoCounterByIdAsync(string id, RhEntityMetadata metadata, ClaimsPrincipal user, CancellationToken ct)
    {
        using var response = await _downstreamApi.CallApiForUserAsync("Dataverse", options =>
        {
            options.RelativePath = $"/api/data/v9.2/{metadata.EntitySetName}({id})?$select={CopiersMtoCounterSelect(metadata)}";
            options.HttpMethod = "GET";
        }, user: user, cancellationToken: ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new CopiersMaintenanceV2PersistenceException($"No fue posible consultar el contador (HTTP {(int)response.StatusCode}).");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.Clone();
    }

    private static string CopiersMtoCounterSelect(RhEntityMetadata metadata) => string.Join(",", new[]
    {
        metadata.PrimaryIdField, metadata.PrimaryNameField, CopiersLegacyCountersDateField,
        CopiersLegacyCountersCopiesField, CopiersLegacyCountersScansField,
        BuildDashboardLookupValuePropertyName(CopiersLegacyCountersEquipmentField), "createdon"
    }.Distinct(StringComparer.OrdinalIgnoreCase));

    private static CopiersMtoV2CounterReadingDto MapCopiersMtoCounter(JsonElement row, string idField)
    {
        var dateText = ReadString(row, CopiersLegacyCountersDateField);
        DateTimeOffset? recordedAtUtc = DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
        var localDate = recordedAtUtc?.ToOffset(TimeSpan.FromHours(-5));
        return new()
        {
            RecordId = ReadString(row, idField),
            EquipmentId = ReadString(row, BuildDashboardLookupValuePropertyName(CopiersLegacyCountersEquipmentField)),
            DateValue = localDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            DateDisplay = localDate?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? "",
            RecordedAtUtc = recordedAtUtc,
            CopiesCounter = ReadCopiersCounterLong(row, CopiersLegacyCountersCopiesField),
            ScansCounter = ReadCopiersCounterLong(row, CopiersLegacyCountersScansField)
        };
    }

    private static CopiersMtoCounterNormalized ValidateCopiersMtoCounterCommand(CopiersMtoV2CounterSaveCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var maintenanceId = CopiersMaintenanceV2Validation.RequiredGuid(command.MaintenanceRecordId, "record_id_invalid", "El mantenimiento");
        var equipmentId = CopiersMaintenanceV2Validation.RequiredGuid(command.EquipmentId, "equipment_invalid", "El equipo");
        var key = (command.SubmissionKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200)
            throw new CopiersMaintenanceV2ValidationException("submission_key_invalid", "La clave de envío no es válida.");
        var fingerprint = (command.FinalizationFingerprint ?? "").Trim();
        if (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit))
            throw new CopiersMaintenanceV2ValidationException("counter_fingerprint_invalid", "No fue posible verificar la huella del reporte firmado.");
        if (!command.CopiesCounter.HasValue || !command.ScansCounter.HasValue)
            throw new CopiersMaintenanceV2ValidationException("counter_current_required", "Registra los contadores actuales de impresión y escaneo.");
        if (command.CopiesCounter is < 0 or > int.MaxValue || command.ScansCounter is < 0 or > int.MaxValue
            || command.PreviousCopiesCounter is < 0 or > int.MaxValue || command.PreviousScansCounter is < 0 or > int.MaxValue)
            throw new CopiersMaintenanceV2ValidationException("counter_out_of_range", "Los contadores deben ser enteros entre 0 y 2147483647.");
        if (command.ReadingAtUtc.Year is < 2000 or > 2100)
            throw new CopiersMaintenanceV2ValidationException("counter_date_invalid", "La fecha del contador no es válida.");
        var counterId = BuildCopiersMtoV2CounterRecordId(maintenanceId);
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
        // The existing column is UserLocal DateAndTime, not DateOnly. Preserve
        // the real visit timestamp (whole seconds), and derive display in Bogotá.
        var readingAtUtc = DateTimeOffset.FromUnixTimeSeconds(command.ReadingAtUtc.ToUnixTimeSeconds());
        return new(counterId, equipmentId, readingAtUtc,
            // This full SHA-256 binds the reading to every signed fact and file.
            // It permits an exact retry after a failed ticket staging write; a
            // later signature or changed attachment can never reuse this proof.
            $"MTO V2 {Guid.Parse(maintenanceId):N} {keyHash} {fingerprint.ToLowerInvariant()}");
    }

    internal static string BuildCopiersMtoV2CounterRecordId(string maintenanceRecordId)
    {
        var canonicalId = Guid.Parse(maintenanceRecordId).ToString("D").ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("digitaltech/copiers-mto-v2/counter/" + canonicalId));
        return new Guid(bytes.AsSpan(0, 16)).ToString("D");
    }

    private static void EnsureCopiersMtoCounterMatches(JsonElement raw, CopiersMtoV2CounterSaveCommand command,
        CopiersMtoCounterNormalized expected, RhEntityMetadata metadata)
    {
        var actual = MapCopiersMtoCounter(raw, metadata.PrimaryIdField);
        if (!string.Equals(actual.RecordId, expected.CounterId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(actual.EquipmentId, expected.EquipmentId, StringComparison.OrdinalIgnoreCase)
            || actual.RecordedAtUtc != expected.ReadingAtUtc || actual.CopiesCounter != command.CopiesCounter || actual.ScansCounter != command.ScansCounter
            || ReadString(raw, metadata.PrimaryNameField) != expected.Name)
            throw new CopiersMaintenanceV2ConcurrencyException("Este mantenimiento ya registró contadores diferentes. No se sobrescribió la lectura existente.");
    }

    private sealed record CopiersMtoCounterNormalized(string CounterId, string EquipmentId, DateTimeOffset ReadingAtUtc, string Name);
}
