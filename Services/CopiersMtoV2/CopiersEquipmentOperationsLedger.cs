using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

// One change set per physical event. Departure does not assign an asset to its
// intended destination; only an independently recorded receipt can do so.
public sealed class CopiersEquipmentOperationsLedger(ICopiersMtoV2ApplicationDataverseClient client,
    IOptions<CopiersEquipmentOperationsOptions> settings, IConfiguration configuration)
{
    public const string TransitField = "dtc_transitjson";
    public const string OperationField = "dtc_operationjson";
    public const string Movements = "cr07a_movimientosequiposes";
    internal sealed record Change(string Path, string Version, bool Insert, Dictionary<string, object?> Payload);
    internal static string Id(string report, int index) => new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"copiers-equipment-operation/{report}/{index}"))[..16]).ToString("D");
    internal static string Text(JsonElement row, string field) => row.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    internal static bool Same(string? a, string? b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
    internal static CopiersMaintenanceV2ValidationException Invalid(string message) => new("equipment_operation_invalid", message);
    private static CopiersMaintenanceV2ConcurrencyException Conflict(string message) => new(message);

    public void EnsureEnabled()
    {
        if (!settings.Value.Enabled || settings.Value.InternalClientIds.Length < 2
            || settings.Value.InternalClientIds.Any(x => !Guid.TryParse(x, out var id) || id == Guid.Empty))
            throw Invalid("La gestión de equipos aún no está habilitada.");
    }
    public async Task<JsonElement?> ReadAsync(string path, CancellationToken ct)
    {
        using var response = await client.SendAsync("/api/data/v9.2/" + path, HttpMethod.Get, null, null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new CopiersMaintenanceV2PersistenceException($"No fue posible consultar la operación (HTTP {(int)response.StatusCode}).");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.Clone();
    }
    public async Task<IReadOnlyList<JsonElement>> QueryAsync(string path, CancellationToken ct)
    {
        var rows = new List<JsonElement>();
        var pages=0;
        while (!string.IsNullOrEmpty(path))
        {
            if(++pages>40 || rows.Count>20000) throw new CopiersMaintenanceV2PersistenceException("El catálogo supera el límite de consulta; requiere revisión.");
            var page = await ReadAsync(path, ct) ?? throw new CopiersMaintenanceV2PersistenceException("Catálogo no disponible.");
            rows.AddRange(page.GetProperty("value").EnumerateArray().Select(x => x.Clone()));
            var next = Text(page, "@odata.nextLink");
            if (next.Length == 0) break;
            var expected = new Uri(configuration["CopiersMtoV2:DataverseApp:BaseUrl"]!);
            if (!Uri.TryCreate(next, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != expected.Host || uri.Port!=expected.Port || uri.UserInfo.Length>0 || uri.Fragment.Length>0 || !uri.AbsolutePath.StartsWith("/api/data/v9.2/", StringComparison.Ordinal))
                throw new CopiersMaintenanceV2PersistenceException("Página de catálogo no válida.");
            path = uri.PathAndQuery["/api/data/v9.2/".Length..];
        }
        return rows;
    }
    public Task<JsonElement?> EquipmentAsync(string id, CancellationToken ct) => ReadAsync(
        $"cr07a_equipos({CopiersMaintenanceV2Validation.RequiredGuid(id,"equipment_invalid","El equipo")})?$select=cr07a_equipoid,cr07a_nombredelequipo,cr07a_referencia,_cr07a_cliente_value,{TransitField}", ct);

    public async Task<bool> ValidateAsync(CopiersActivityV2BusinessCommand command, CancellationToken ct)
    {
        EnsureEnabled();
        if (await ExistingAsync(command, ct)) return true;
        _ = await PlanAsync(command, ct);
        return false;
    }
    public async Task<CopiersActivityV2BusinessResult> CommitAsync(CopiersActivityV2BusinessCommand command,
        CopiersMaintenanceV2StoredFile? pdf, CancellationToken ct)
    {
        EnsureEnabled();
        var op = command.Operation ?? throw Invalid("Falta la operación.");
        if (!op.Internal && pdf is null) throw Invalid("Falta el certificado firmado.");
        var reused = await ExistingAsync(command, ct);
        if (!reused)
        {
            var plan = await PlanAsync(command, ct);
            try { await BatchAsync(plan, ct); }
            catch (Exception ex) when (ex is HttpRequestException or CopiersMaintenanceV2ConcurrencyException or CopiersMaintenanceV2PersistenceException)
            {
                if (!await ExistingAsync(command, ct)) throw;
                reused = true;
            }
            if (!await ExistingAsync(command, ct)) throw new CopiersMaintenanceV2PersistenceException("No se confirmó el movimiento. Conserva la misma clave.");
        }
        if (pdf is not null)
            for (var i = 0; i < (op.Replacement is null ? 1 : 2); i++) await StorePdfAsync(Id(command.ReportRecordId, i), pdf, ct);
        return new(Id(command.ReportRecordId, 0), command.ServiceReference, reused);
    }
    private async Task<bool> ExistingAsync(CopiersActivityV2BusinessCommand command, CancellationToken ct)
    {
        var count = command.Operation?.Replacement is null ? 1 : 2;
        var found = 0;
        for (var i = 0; i < count; i++)
        {
            var row = await ReadAsync($"{Movements}({Id(command.ReportRecordId,i)})?$select=dtc_signedreportkey,dtc_signedfingerprint,{OperationField}", ct);
            if (row is null) continue;
            if (Text(row.Value,"dtc_signedreportkey") != command.ReportRecordId
                || !Same(Text(row.Value,"dtc_signedfingerprint"), command.FinalizationFingerprint)
                || Text(row.Value,OperationField) != JsonSerializer.Serialize(command.Operation))
                throw Conflict("La operación ya existe con otros datos. No se sobrescribió.");
            found++;
        }
        if (found != 0 && found != count) throw Conflict("La operación tiene movimientos incompletos y requiere revisión interna.");
        return found == count;
    }
    internal async Task<List<Change>> PlanAsync(CopiersActivityV2BusinessCommand command, CancellationToken ct)
    {
        var op = command.Operation ?? throw Invalid("Falta la operación.");
        if (op.Kind is not ("delivery" or "withdrawal" or "replacement" or "receipt" or "internal")) throw Invalid("Operación no válida.");
        if (!Same(command.EquipmentId, op.Equipment.Id) || !Same(command.ClientId, op.ClientId)) throw Invalid("La operación no corresponde al certificado.");
        if ((op.Kind == "replacement") != (op.Replacement is not null)) throw Invalid("Selecciona los dos equipos del cambio.");
        if (op.Replacement is not null && Same(op.Equipment.Id, op.Replacement.Id)) throw Invalid("El equipo retirado y el entregado deben ser diferentes.");
        var destination = await ReadAsync($"cr07a_clientes({CopiersMaintenanceV2Validation.RequiredGuid(op.DestinationId,"destination_invalid","El destino")})?$select=cr07a_nombre,statecode",ct)
            ?? throw Invalid("El destino no existe.");
        if (destination.GetProperty("statecode").GetInt32() != 0 || Text(destination,"cr07a_nombre") != op.DestinationName) throw Conflict("El destino cambió. Revisa la operación.");
        var changes = new List<Change>();
        var equipment = await EquipmentAsync(op.Equipment.Id, ct) ?? throw Invalid("El equipo no existe.");
        CheckEquipment(equipment, op.Equipment);
        var transitText = Text(equipment, TransitField);
        Dictionary<string, object?> patch;
        if (op.Kind == "receipt")
        {
            var transit = string.IsNullOrEmpty(transitText) ? null : JsonSerializer.Deserialize<CopiersEquipmentTransit>(transitText);
            if (transit is null || !Same(transit.OperationKey, op.PendingKey) || !Same(transit.DestinationId, op.DestinationId)
                || !Same(op.ClientId, op.DestinationId) || op.Internal != settings.Value.IsInternal(op.DestinationId))
                throw Conflict("La recepción no corresponde a un traslado pendiente de este destino.");
            patch = new() { ["cr07a_Cliente@odata.bind"] = $"/cr07a_clientes({op.DestinationId})", [TransitField] = null };
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(transitText)) throw Conflict("El equipo está en tránsito. Confirma su recepción antes de iniciar otro movimiento.");
            if (!Same(Text(equipment,"_cr07a_cliente_value"), op.Equipment.OriginId)) throw Conflict("La asignación del equipo cambió.");
            if (Same(op.Equipment.OriginId, op.DestinationId)) throw Invalid("El origen y destino deben ser diferentes.");
            var originInternal = settings.Value.IsInternal(op.Equipment.OriginId) || string.IsNullOrEmpty(op.Equipment.OriginId);
            if (op.Kind == "delivery" && (!originInternal || op.Internal || settings.Value.IsInternal(op.ClientId) || !Same(op.ClientId,op.DestinationId))) throw Invalid("La entrega debe salir de una ubicación interna hacia el cliente.");
            if (op.Kind is "withdrawal" or "replacement" && (originInternal || op.Internal || !Same(op.ClientId, op.Equipment.OriginId))) throw Invalid("Selecciona un equipo del cliente que firma el retiro.");
            if (op.Kind == "replacement" && !settings.Value.IsInternal(op.DestinationId)) throw Invalid("Selecciona el destino interno del equipo reemplazado.");
            if (op.Kind == "internal" && (!op.Internal || !originInternal || !settings.Value.IsInternal(op.DestinationId))) throw Invalid("El traslado interno solo admite depósito y oficina.");
            patch = op.StartsTransit ? new() {
                ["cr07a_Cliente@odata.bind"] = null,
                [TransitField] = JsonSerializer.Serialize(new CopiersEquipmentTransit {
                    OperationKey=command.ReportRecordId, OriginId=op.Equipment.OriginId, OriginName=op.Equipment.OriginName,
                    DestinationId=op.DestinationId, DestinationName=op.DestinationName, CustodianId=command.TechnicianSystemUserId,
                    DepartedAtUtc=command.OccurredAtUtc.ToUniversalTime().ToString("O") })
            } : new() { ["cr07a_Cliente@odata.bind"]=$"/cr07a_clientes({op.DestinationId})", [TransitField]=null };
        }
        AddEquipment(op.Equipment, equipment, patch, 0, op.DestinationId);
        if (op.Replacement is { } replacement)
        {
            var row = await EquipmentAsync(replacement.Id,ct) ?? throw Invalid("El reemplazo no existe.");
            CheckEquipment(row,replacement);
            if (!Same(Text(row,"_cr07a_cliente_value"),replacement.OriginId) || Text(row,TransitField).Length != 0
                || !(settings.Value.IsInternal(replacement.OriginId) || replacement.OriginId.Length == 0))
                throw Conflict("El reemplazo no está disponible en una ubicación interna.");
            AddEquipment(replacement,row,new() { ["cr07a_Cliente@odata.bind"]=$"/cr07a_clientes({op.ClientId})",[TransitField]=null },1,op.ClientId);
        }
        return changes;

        void AddEquipment(CopiersOperationEquipment asset, JsonElement row, Dictionary<string,object?> assetPatch, int index, string target)
        {
            var phase = op.Kind == "replacement" ? index == 0 ? "Retiro" : "Entrega" : op.Kind == "receipt" ? "Recepción" : op.Kind == "delivery" ? "Entrega" : "Salida";
            var record = new Dictionary<string,object?> {
                ["cr07a_name"]=$"{command.ServiceReference} - {phase} - {asset.Serial}", ["cr07a_fecha"]=command.OccurredAtUtc.ToUniversalTime().ToString("O"),
                ["cr07a_Cliente@odata.bind"]=$"/cr07a_clientes({target})",["cr07a_Equipo@odata.bind"]=$"/cr07a_equipos({asset.Id})",
                ["ownerid@odata.bind"]=$"/systemusers({command.TechnicianSystemUserId})",["cr07a_motivo"]=op.Reason,
                ["dtc_signedreportkey"]=command.ReportRecordId,["dtc_signedfingerprint"]=command.FinalizationFingerprint,["dtc_reference"]=command.ServiceReference,
                ["dtc_originclientkey"]=asset.OriginId.Length==0?null:asset.OriginId,["dtc_originclientname"]=asset.OriginName,[OperationField]=JsonSerializer.Serialize(op)
            };
            changes.Add(new($"{Movements}({Id(command.ReportRecordId,index)})","*",true,record));
            var version=Text(row,"@odata.etag");
            if (!Regex.IsMatch(version,"^W/\"[0-9]+\"$")) throw Conflict("No se pudo verificar la versión del equipo.");
            changes.Add(new($"cr07a_equipos({asset.Id})",version,false,assetPatch));
        }
    }
    private static void CheckEquipment(JsonElement row, CopiersOperationEquipment snapshot)
    {
        if (Text(row,"cr07a_nombredelequipo").Trim()!=snapshot.Serial || Text(row,"cr07a_referencia").Trim()!=snapshot.Reference)
            throw Conflict("La identificación del equipo cambió. Revisa los datos antes de firmar.");
    }
    internal async Task BatchAsync(IReadOnlyList<Change> changes, CancellationToken ct)
    {
        var baseUri = new Uri(configuration["CopiersMtoV2:DataverseApp:BaseUrl"]!);
        if (baseUri.Scheme!="https" || baseUri.AbsolutePath!="/" || baseUri.Query.Length!=0) throw new InvalidOperationException("Entorno no válido.");
        var batch="batch_"+Guid.NewGuid().ToString("N"); var set="changeset_"+Guid.NewGuid().ToString("N");
        var body=new StringBuilder($"--{batch}\r\nContent-Type: multipart/mixed; boundary={set}\r\n\r\n");
        var i=0;
        foreach(var change in changes) body.Append($"--{set}\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\nContent-ID: {++i}\r\n\r\nPATCH {baseUri.GetLeftPart(UriPartial.Authority)}/api/data/v9.2/{change.Path} HTTP/1.1\r\nContent-Type: application/json\r\n{(change.Insert?"If-None-Match":"If-Match")}: {change.Version}\r\n\r\n{JsonSerializer.Serialize(change.Payload)}\r\n");
        body.Append($"--{set}--\r\n--{batch}--\r\n");
        using var content=new StringContent(body.ToString(),Encoding.UTF8);
        content.Headers.ContentType=MediaTypeHeaderValue.Parse($"multipart/mixed; boundary={batch}");
        using var response=await client.SendAsync("/api/data/v9.2/$batch",HttpMethod.Post,content,null,ct);
        var text=await response.Content.ReadAsStringAsync(ct);
        var statuses=Regex.Matches(text,@"^HTTP/1\.[01] (?<status>\d{3})",RegexOptions.Multiline).Select(x=>int.Parse(x.Groups["status"].Value,CultureInfo.InvariantCulture)).ToArray();
        if(statuses.Any(x=>x is 409 or 412)) throw Conflict("El equipo cambió durante la operación. No se aplicó un movimiento parcial.");
        if(!response.IsSuccessStatusCode || statuses.Length!=changes.Count || statuses.Any(x=>x<200||x>=300)) throw new CopiersMaintenanceV2PersistenceException("No se confirmó la operación completa en Dataverse. Reintenta con la misma clave.");
    }
    private async Task StorePdfAsync(string id, CopiersMaintenanceV2StoredFile pdf, CancellationToken ct)
    {
        var path=$"/api/data/v9.2/{Movements}({id})/cr07a_actadeentrega";
        if(await Verify(true)) return;
        using var content=new ByteArrayContent(pdf.Content); content.Headers.ContentType=new("application/octet-stream");
        using var response=await client.SendAsync(path,HttpMethod.Patch,content,r=> {r.Headers.TryAddWithoutValidation("If-Match","*");r.Headers.TryAddWithoutValidation("x-ms-file-name",Uri.EscapeDataString(pdf.FileName));},ct);
        if(!response.IsSuccessStatusCode) throw new CopiersMaintenanceV2PersistenceException("Movimiento guardado; falta guardar el certificado. Se reintentará sin repetir el movimiento.");
        _=await Verify(false);
        async Task<bool> Verify(bool missingAllowed)
        {
            using var response=await client.SendAsync(path+"/$value",HttpMethod.Get,null,null,ct);
            if(missingAllowed && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent) return false;
            if(!response.IsSuccessStatusCode || response.Content.Headers.ContentLength>12*1024*1024) throw new CopiersMaintenanceV2PersistenceException("No se pudo verificar el certificado.");
            var bytes=await response.Content.ReadAsByteArrayAsync(ct);
            if(missingAllowed&&bytes.Length==0)return false;
            if(bytes.Length!=pdf.Size || !Same(Convert.ToHexString(SHA256.HashData(bytes)),pdf.Sha256)) throw Conflict("El certificado existente no coincide. No se sobrescribió.");
            return true;
        }
    }
}
