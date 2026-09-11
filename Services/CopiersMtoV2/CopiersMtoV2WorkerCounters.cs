using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.CopiersMtoV2;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

// Only the durable worker receives this adapter, after the interactive controller
// has authorized the technician and resolved the customer/equipment snapshot.
public sealed class CopiersMtoV2WorkerCounters(ICopiersMtoV2ApplicationDataverseClient client) : ICopiersMtoV2CounterService
{
    private const string Set = "cr07a_contadoreses";
    private const string Fields = "cr07a_contadoresid,cr07a_equipo,_cr07a_maquina_value,cr07a_fechadetomadecontador,cr07a_contador,cr07a_contadorescaner,createdon";
    public Task<CopiersMtoV2CounterReadingDto> GetLatestAsync(string clientId, string equipmentId, CancellationToken ct = default) => throw new NotSupportedException();

    public async Task<bool> ValidateForMaintenanceAsync(CopiersMtoV2CounterSaveCommand c, CancellationToken ct = default)
    {
        var equipment = Guid.Parse(c.EquipmentId).ToString("D");
        var customer = Guid.Parse(c.ClientId).ToString("D");
        var row = await ReadAsync($"cr07a_equipos({equipment})?$select=cr07a_nombredelequipo,_cr07a_cliente_value", ct)
            ?? throw new CopiersMaintenanceV2ValidationException("equipment_missing", "El equipo ya no está disponible.");
        if (!string.Equals(Text(row, "_cr07a_cliente_value"), customer, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Text(row, "cr07a_nombredelequipo").Trim(), c.EquipmentSerial.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new CopiersMaintenanceV2ValidationException("counter_equipment_client_mismatch", "El equipo cambió de cliente o serial. Requiere revisión interna.");
        if (c.CopiesCounter is null or < 0 or > int.MaxValue || c.ScansCounter is null or < 0 or > int.MaxValue
            || c.PreviousCopiesCounter is < 0 or > int.MaxValue || c.PreviousScansCounter is < 0 or > int.MaxValue
            || c.FinalizationFingerprint.Length != 64 || !c.FinalizationFingerprint.All(Uri.IsHexDigit))
            throw new CopiersMaintenanceV2ValidationException("counter_invalid", "Los contadores del reporte no son válidos.");
        var id = DataverseService.BuildCopiersMtoV2CounterRecordId(c.MaintenanceRecordId, c.AdditionalEquipmentScope);
        if (string.IsNullOrWhiteSpace(c.PreviousCounterRecordId))
        {
            if (c.PreviousCopiesCounter.HasValue || c.PreviousScansCounter.HasValue || !string.IsNullOrWhiteSpace(c.PreviousDateValue))
                throw new CopiersMaintenanceV2ValidationException("counter_history_missing", "La lectura anterior no tiene registro de origen.");
            var cutoff = DateTimeOffset.FromUnixTimeSeconds(c.ReadingAtUtc.ToUnixTimeSeconds()).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            var filter = Uri.EscapeDataString($"_cr07a_maquina_value eq {equipment} and createdon le {cutoff} and cr07a_contadoresid ne {id}");
            var previous = await ReadAsync($"{Set}?$select=cr07a_contadoresid&$filter={filter}&$top=1", ct);
            if (previous!.Value.GetProperty("value").GetArrayLength() != 0)
                throw new CopiersMaintenanceV2ValidationException("counter_history_required", "El equipo tenía una lectura anterior no incluida en la firma.");
        }
        else
        {
            var previousId = Guid.Parse(c.PreviousCounterRecordId).ToString("D");
            var previous = await ReadAsync($"{Set}({previousId})?$select={Fields}", ct);
            if (previous is null || previousId == id || Text(previous, "_cr07a_maquina_value") != equipment
                || Number(previous, "cr07a_contador") != c.PreviousCopiesCounter || Number(previous, "cr07a_contadorescaner") != c.PreviousScansCounter
                || DateTimeOffset.Parse(Text(previous, "cr07a_fechadetomadecontador"), CultureInfo.InvariantCulture).ToOffset(TimeSpan.FromHours(-5)).ToString("yyyy-MM-dd") != c.PreviousDateValue)
                throw new CopiersMaintenanceV2ValidationException("counter_history_changed", "La lectura anterior cambió. Requiere revisión interna.");
            if (c.CopiesCounter < c.PreviousCopiesCounter || c.ScansCounter < c.PreviousScansCounter)
                throw new CopiersMaintenanceV2ValidationException("counter_decreased", "Los contadores actuales son menores que la lectura anterior.");
        }
        var existing = await ReadAsync($"{Set}({id})?$select={Fields}", ct);
        if (existing.HasValue) EnsureMatches(existing.Value, c);
        return existing.HasValue;
    }

    public async Task<CopiersMtoV2CounterSaveResult> SaveForMaintenanceAsync(CopiersMtoV2CounterSaveCommand c, CancellationToken ct = default)
    {
        var id = DataverseService.BuildCopiersMtoV2CounterRecordId(c.MaintenanceRecordId, c.AdditionalEquipmentScope);
        if (await ValidateForMaintenanceAsync(c, ct)) return new(id, true);
        using var body = JsonContent.Create(new Dictionary<string, object?> {
            ["cr07a_contadoresid"] = id,
            ["cr07a_equipo"] = Name(c), ["cr07a_contador"] = c.CopiesCounter, ["cr07a_contadorescaner"] = c.ScansCounter,
            ["cr07a_fechadetomadecontador"] = DateTimeOffset.FromUnixTimeSeconds(c.ReadingAtUtc.ToUnixTimeSeconds()).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["cr07a_Maquina@odata.bind"] = $"/cr07a_equipos({Guid.Parse(c.EquipmentId):D})" });
        // POST with a deterministic primary key needs Create, not Write.
        using var response = await client.SendAsync($"/api/data/v9.2/{Set}", HttpMethod.Post, body, null, ct);
        var persisted = await ReadAsync($"{Set}({id})?$select={Fields}", ct);
        if (persisted is null)
            throw new CopiersMaintenanceV2PersistenceException($"No fue posible verificar el contador guardado (HTTP {(int)response.StatusCode}).");
        EnsureMatches(persisted.Value, c);
        return new(id, !response.IsSuccessStatusCode);
    }
    private static string Name(CopiersMtoV2CounterSaveCommand c) => $"MTO V2 {Guid.Parse(c.MaintenanceRecordId):N} {Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(c.SubmissionKey.Trim())))[..16].ToLowerInvariant()} {c.FinalizationFingerprint.ToLowerInvariant()}";
    private static void EnsureMatches(JsonElement row, CopiersMtoV2CounterSaveCommand c)
    {
        if (Text(row, "cr07a_equipo") != Name(c) || Text(row, "_cr07a_maquina_value") != Guid.Parse(c.EquipmentId).ToString("D")
            || Number(row, "cr07a_contador") != c.CopiesCounter || Number(row, "cr07a_contadorescaner") != c.ScansCounter
            || DateTimeOffset.Parse(Text(row, "cr07a_fechadetomadecontador"), CultureInfo.InvariantCulture).ToUnixTimeSeconds() != c.ReadingAtUtc.ToUnixTimeSeconds())
            throw new CopiersMaintenanceV2ConcurrencyException("El mantenimiento ya tiene contadores diferentes. No se sobrescribieron.");
    }
    private async Task<JsonElement?> ReadAsync(string path, CancellationToken ct)
    {
        using var response = await client.SendAsync("/api/data/v9.2/" + path, HttpMethod.Get, null, null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new CopiersMaintenanceV2PersistenceException($"No fue posible consultar contadores (HTTP {(int)response.StatusCode}).");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.Clone();
    }
    private static string Text(JsonElement? row, string name) => row!.Value.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
    private static long? Number(JsonElement? row, string name) => row!.Value.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;
}
