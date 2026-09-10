using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.CopiersMtoV2;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

public interface ICopiersActivityV2BusinessService
{
    Task<bool> ValidateAsync(CopiersActivityV2BusinessCommand command, CancellationToken ct = default);
    Task<CopiersActivityV2BusinessResult> CommitAsync(CopiersActivityV2BusinessCommand command,
        CopiersMaintenanceV2StoredFile signedReport, CancellationToken ct = default);
}

/// <summary>
/// Commits real delivery/movement rows, never maintenance rows. The immutable
/// business row and mutable stock/assignment form one Dataverse change set.
/// Its deterministic identity is also a durable proof for uncertain retries.
/// Caller must authorize the technician and provide the signed server snapshot.
/// </summary>
public sealed class CopiersActivityV2BusinessService : ICopiersActivityV2BusinessService
{
    public const int TonerCategoryValue = 645250000;
    private const int MaxPdfBytes = 12 * 1024 * 1024;
    private const string ReportKeyField = "dtc_signedreportkey";
    private const string FingerprintField = "dtc_signedfingerprint";
    private const string ReferenceField = "dtc_reference";
    private const string OriginKeyField = "dtc_originclientkey";
    private const string OriginNameField = "dtc_originclientname";
    private readonly ICopiersMtoV2ApplicationDataverseClient _client;
    private readonly Lazy<string> _organization;
    private static readonly Regex BatchStatuses = new(@"^HTTP/1\.[01] (?<status>\d{3})(?:\s|$)", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex EntityVersion = new("^(?:W/)?\"[0-9]+\"$", RegexOptions.CultureInvariant);

    public CopiersActivityV2BusinessService(ICopiersMtoV2ApplicationDataverseClient client, IConfiguration configuration)
    {
        _client = client;
        var configured = configuration["CopiersMtoV2:DataverseApp:BaseUrl"];
        // DI also constructs this dependency while serving the unchanged MTO
        // page. An unconfigured/disabled activity must not break that page.
        _organization = new(() =>
        {
            if (!Uri.TryCreate(configured, UriKind.Absolute, out var organization) || organization.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(organization.UserInfo) || !string.IsNullOrEmpty(organization.Query)
                || !string.IsNullOrEmpty(organization.Fragment) || organization.AbsolutePath != "/")
                throw new InvalidOperationException("El entorno Dataverse de actividades firmadas no está configurado correctamente.");
            return organization.GetLeftPart(UriPartial.Authority);
        });
    }

    public async Task<bool> ValidateAsync(CopiersActivityV2BusinessCommand command, CancellationToken ct = default)
    {
        var normalized = Normalize(command);
        var own = await ReadBusinessAsync(normalized, ct);
        if (own.HasValue)
        {
            EnsureBusinessMatches(own.Value, normalized);
            return true;
        }
        _ = await PrepareChangeAsync(normalized, ct);
        return false;
    }

    public async Task<CopiersActivityV2BusinessResult> CommitAsync(CopiersActivityV2BusinessCommand command,
        CopiersMaintenanceV2StoredFile signedReport, CancellationToken ct = default)
    {
        var normalized = Normalize(command);
        ValidatePdf(signedReport);
        var own = await ReadBusinessAsync(normalized, ct);
        var reused = own.HasValue;
        if (own.HasValue) EnsureBusinessMatches(own.Value, normalized);
        else
        {
            var plan = await PrepareChangeAsync(normalized, ct);
            try
            {
                await ExecuteChangeAsync(normalized, plan, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is CopiersMaintenanceV2PersistenceException
                or CopiersMaintenanceV2ConcurrencyException or HttpRequestException)
            {
                // A concurrent identical commit or lost HTTP response is safe to
                // reconcile by the insert-only record, never by subtracting again.
                var reconciled = await ReadBusinessAsync(normalized, ct);
                if (!reconciled.HasValue) throw;
                EnsureBusinessMatches(reconciled.Value, normalized);
                reused = true;
            }
            own = await ReadBusinessAsync(normalized, ct)
                ?? throw Persistence("No fue posible verificar el registro de la actividad. Reintenta el mismo envío.");
            EnsureBusinessMatches(own.Value, normalized);
        }
        // Publication of the email remains the caller's final step, after this
        // actual-table file has also passed independent binary read-back.
        await EnsureSignedPdfAsync(normalized, signedReport, ct);
        return new(BuildBusinessRecordId(normalized.ReportRecordId, normalized.ActivityKind), normalized.ServiceReference, reused);
    }

    private async Task<BusinessChange> PrepareChangeAsync(CopiersActivityV2BusinessCommand command, CancellationToken ct)
    {
        var targetClient = await ReadRequiredAsync($"cr07a_clientes({command.ClientId})?$select=cr07a_clienteid,cr07a_nombre,statecode", "El cliente", ct);
        if (Integer(targetClient, "statecode") != 0) throw Invalid("activity_client_inactive", "El cliente está inactivo.");
        var equipment = await ReadRequiredAsync($"cr07a_equipos({command.EquipmentId})?$select=cr07a_equipoid,cr07a_nombredelequipo,_cr07a_cliente_value", "El equipo", ct);
        if (!string.Equals(Text(equipment, "cr07a_nombredelequipo").Trim(), command.EquipmentSerial, StringComparison.Ordinal))
            throw Invalid("activity_serial_changed", "El serial cambió. Recarga el equipo antes de firmar.");
        var descriptor = Describe(command.ActivityKind);
        var record = new Dictionary<string, object?>
        {
            [ReportKeyField] = command.ReportRecordId,
            [FingerprintField] = command.FinalizationFingerprint,
            [ReferenceField] = command.ServiceReference,
            [descriptor.NameField] = Title(command),
            [descriptor.DateField] = Instant(command.OccurredAtUtc),
            [descriptor.ClientNavigation + "@odata.bind"] = $"/cr07a_clientes({command.ClientId})",
            [descriptor.EquipmentNavigation + "@odata.bind"] = $"/cr07a_equipos({command.EquipmentId})",
            ["ownerid@odata.bind"] = $"/systemusers({command.TechnicianSystemUserId})"
        };
        if (command.ActivityKind == "movement")
        {
            if (!SameOptionalGuid(Text(equipment, "_cr07a_cliente_value"), command.OriginClientId))
                throw Conflict("El cliente de origen del equipo cambió. Recarga y firma el movimiento actualizado.");
            if (!string.IsNullOrEmpty(command.OriginClientId))
            {
                var origin = await ReadRequiredAsync($"cr07a_clientes({command.OriginClientId})?$select=cr07a_clienteid,cr07a_nombre", "El cliente de origen", ct);
                if (!string.Equals(Text(origin, "cr07a_nombre").Trim(), command.OriginClientName, StringComparison.Ordinal))
                    throw Conflict("El nombre del cliente de origen cambió. Revisa el movimiento antes de firmar.");
            }
            else if (command.OriginClientName is not "" and not "Sin cliente" and not "Inventario" and not "Stock")
                throw Invalid("activity_origin_invalid", "El equipo sin cliente no puede tener otro origen declarado.");
            record[OriginKeyField] = string.IsNullOrEmpty(command.OriginClientId) ? null : command.OriginClientId;
            record[OriginNameField] = command.OriginClientName;
            record["cr07a_motivo"] = command.MovementReason;
            return new(record, $"cr07a_equipos({command.EquipmentId})", Version(equipment), new()
            {
                ["cr07a_Cliente@odata.bind"] = $"/cr07a_clientes({command.ClientId})"
            });
        }
        if (!SameOptionalGuid(Text(equipment, "_cr07a_cliente_value"), command.ClientId))
            throw Invalid("activity_equipment_client_mismatch", "El equipo no está asignado al cliente que recibe el tóner.");
        var supply = await ReadRequiredAsync($"cr07a_suministros({command.SupplyId})?$select=cr07a_suministroid,cr07a_nombredelsuministro,cr07a_cantidad,cr07a_categoria", "El suministro", ct);
        if (Integer(supply, "cr07a_categoria") != TonerCategoryValue)
            throw Invalid("activity_supply_not_toner", "Selecciona un suministro clasificado como tóner en el inventario.");
        if (!string.Equals(Text(supply, "cr07a_nombredelsuministro").Trim(), command.SupplyName, StringComparison.Ordinal))
            throw Conflict("El nombre del tóner cambió. Revisa el suministro antes de firmar.");
        var stock = Integer(supply, "cr07a_cantidad");
        if (stock is null or < 0) throw Invalid("activity_stock_invalid", "La existencia del tóner no es válida. Solicita revisión del inventario.");
        if (command.SupplyStockBefore.HasValue && stock != command.SupplyStockBefore)
            throw Conflict("La existencia del tóner cambió. Recarga el suministro y confirma la entrega.");
        if (stock < command.SupplyQuantity)
            throw Invalid("activity_stock_insufficient", $"No hay tóner suficiente. Existencia actual: {stock.Value.ToString(CultureInfo.InvariantCulture)}.");
        var remaining = stock.Value - command.SupplyQuantity;
        record["cr07a_IDdesuministro@odata.bind"] = $"/cr07a_suministros({command.SupplyId})";
        record["cr07a_cantidadentregada"] = command.SupplyQuantity;
        record["cr07a_estadodeentrega"] = 645250000;
        return new(record, $"cr07a_suministros({command.SupplyId})", Version(supply), new()
        {
            ["cr07a_cantidad"] = remaining,
            ["cr07a_estadodelsuministro"] = remaining == 0 ? 645250001 : 645250000
        });
    }

    private async Task ExecuteChangeAsync(CopiersActivityV2BusinessCommand command, BusinessChange plan, CancellationToken ct)
    {
        var batch = "batch_" + Guid.NewGuid().ToString("N");
        var changeset = "changeset_" + Guid.NewGuid().ToString("N");
        var descriptor = Describe(command.ActivityKind);
        var recordPath = $"{descriptor.EntitySet}({BuildBusinessRecordId(command.ReportRecordId, command.ActivityKind)})";
        const string crlf = "\r\n";
        var body = new StringBuilder().Append("--").Append(batch).Append(crlf)
            .Append("Content-Type: multipart/mixed; boundary=").Append(changeset).Append(crlf).Append(crlf);
        AppendOperation(body, changeset, 1, recordPath, "If-None-Match", "*", plan.Record);
        AppendOperation(body, changeset, 2, plan.AssetPath, "If-Match", plan.AssetVersion, plan.AssetPatch);
        body.Append("--").Append(changeset).Append("--").Append(crlf).Append("--").Append(batch).Append("--").Append(crlf);
        using var content = new StringContent(body.ToString(), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("multipart/mixed");
        content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", batch));
        using var response = await _client.SendAsync("/api/data/v9.2/$batch", HttpMethod.Post, content, null, ct);
        var result = await response.Content.ReadAsStringAsync(ct);
        var statuses = BatchStatuses.Matches(result).Select(match => int.Parse(match.Groups["status"].Value, CultureInfo.InvariantCulture)).ToArray();
        if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict || statuses.Any(code => code is 409 or 412))
            throw Conflict("El equipo o el inventario cambió durante el envío. Revisa el registro antes de reintentar.");
        if (!response.IsSuccessStatusCode || statuses.Length != 2 || statuses.Any(code => code < 200 || code > 299))
            throw Persistence("Dataverse no confirmó la actividad y su actualización de inventario en una sola operación. Reintenta el mismo envío.");
    }

    private void AppendOperation(StringBuilder body, string boundary, int contentId, string relativePath,
        string header, string version, Dictionary<string, object?> payload)
    {
        body.Append("--").Append(boundary).Append("\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\nContent-ID: ")
            .Append(contentId.ToString(CultureInfo.InvariantCulture)).Append("\r\n\r\nPATCH ")
            .Append(_organization.Value).Append("/api/data/v9.2/").Append(relativePath).Append(" HTTP/1.1\r\nContent-Type: application/json; type=entry\r\n")
            .Append(header).Append(": ").Append(version).Append("\r\n\r\n").Append(JsonSerializer.Serialize(payload)).Append("\r\n");
    }

    private async Task<JsonElement?> ReadBusinessAsync(CopiersActivityV2BusinessCommand command, CancellationToken ct)
    {
        var descriptor = Describe(command.ActivityKind);
        var select = $"{descriptor.IdField},{descriptor.NameField},{descriptor.DateField},{descriptor.ClientLookup},{descriptor.EquipmentLookup},_ownerid_value,{ReportKeyField},{FingerprintField},{ReferenceField}";
        select += command.ActivityKind == "movement" ? $",cr07a_motivo,{OriginKeyField},{OriginNameField}" : ",_cr07a_iddesuministro_value,cr07a_cantidadentregada,cr07a_estadodeentrega";
        return await ReadOptionalAsync($"{descriptor.EntitySet}({BuildBusinessRecordId(command.ReportRecordId, command.ActivityKind)})?$select={select}", ct);
    }

    private static void EnsureBusinessMatches(JsonElement row, CopiersActivityV2BusinessCommand command)
    {
        var descriptor = Describe(command.ActivityKind);
        var matches = SameOptionalGuid(Text(row, descriptor.IdField), BuildBusinessRecordId(command.ReportRecordId, command.ActivityKind))
            && SameOptionalGuid(Text(row, ReportKeyField), command.ReportRecordId)
            && string.Equals(Text(row, FingerprintField), command.FinalizationFingerprint, StringComparison.OrdinalIgnoreCase)
            && Text(row, ReferenceField) == command.ServiceReference && Text(row, descriptor.NameField) == Title(command)
            && SameOptionalGuid(Text(row, descriptor.ClientLookup), command.ClientId)
            && SameOptionalGuid(Text(row, descriptor.EquipmentLookup), command.EquipmentId)
            && SameOptionalGuid(Text(row, "_ownerid_value"), command.TechnicianSystemUserId)
            && DateTimeOffset.TryParse(Text(row, descriptor.DateField), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var actualTime)
            && actualTime == command.OccurredAtUtc;
        matches &= command.ActivityKind == "movement"
            ? Text(row, "cr07a_motivo") == command.MovementReason && SameOptionalGuid(Text(row, OriginKeyField), command.OriginClientId)
                && Text(row, OriginNameField) == command.OriginClientName
            : SameOptionalGuid(Text(row, "_cr07a_iddesuministro_value"), command.SupplyId)
                && Integer(row, "cr07a_cantidadentregada") == command.SupplyQuantity && Integer(row, "cr07a_estadodeentrega") == 645250000;
        if (!matches) throw Conflict("La actividad ya existe con información diferente. No se cambió el registro ni el inventario.");
    }

    private async Task EnsureSignedPdfAsync(CopiersActivityV2BusinessCommand command, CopiersMaintenanceV2StoredFile file, CancellationToken ct)
    {
        var descriptor = Describe(command.ActivityKind);
        var path = $"/api/data/v9.2/{descriptor.EntitySet}({BuildBusinessRecordId(command.ReportRecordId, command.ActivityKind)})/{descriptor.FileField}";
        if (await VerifyFileAsync(path, file, allowMissing: true, ct)) return;
        using var content = new ByteArrayContent(file.Content);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await _client.SendAsync(path, HttpMethod.Patch, content, request =>
        {
            request.Headers.TryAddWithoutValidation("If-Match", "*");
            request.Headers.TryAddWithoutValidation("x-ms-file-name", Uri.EscapeDataString(file.FileName));
        }, ct);
        if (!response.IsSuccessStatusCode) throw Persistence("La actividad quedó registrada, pero falta adjuntar el PDF firmado. Reintenta el mismo envío; no se repetirá el movimiento de inventario.");
        _ = await VerifyFileAsync(path, file, allowMissing: false, ct);
    }

    private async Task<bool> VerifyFileAsync(string path, CopiersMaintenanceV2StoredFile file, bool allowMissing, CancellationToken ct)
    {
        using var response = await _client.SendAsync(path + "/$value", HttpMethod.Get, null, null, ct);
        if (allowMissing && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent) return false;
        if (!response.IsSuccessStatusCode) throw Persistence("No fue posible verificar el PDF firmado de la actividad.");
        if (response.Content.Headers.ContentLength > MaxPdfBytes) throw Conflict("El comprobante existente supera el tamaño permitido y no se sobrescribió.");
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (allowMissing && bytes.Length == 0) return false;
        if (bytes.LongLength != file.Size || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw Conflict("El PDF firmado existente no coincide con este envío y no se sobrescribió.");
        return true;
    }

    private async Task<JsonElement?> ReadOptionalAsync(string path, CancellationToken ct)
    {
        using var response = await _client.SendAsync("/api/data/v9.2/" + path, HttpMethod.Get, null, null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw Persistence($"No fue posible consultar Dataverse para la actividad (HTTP {(int)response.StatusCode}).");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.Clone();
    }

    private async Task<JsonElement> ReadRequiredAsync(string path, string label, CancellationToken ct) =>
        await ReadOptionalAsync(path, ct) ?? throw Invalid("activity_record_missing", $"{label} ya no está disponible. Recarga el formulario.");

    private static CopiersActivityV2BusinessCommand Normalize(CopiersActivityV2BusinessCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var kind = command.ActivityKind?.Trim().ToLowerInvariant();
        if (kind is not "movement" and not "toner") throw Invalid("activity_kind_invalid", "Selecciona movimiento de equipo o entrega de tóner.");
        var fingerprint = (command.FinalizationFingerprint ?? "").Trim();
        if (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit)) throw Invalid("activity_fingerprint_invalid", "La huella del reporte firmado no es válida.");
        if (command.ServiceDate.Year is < 2000 or > 2100 || command.OccurredAtUtc.Year is < 2000 or > 2100)
            throw Invalid("activity_date_invalid", "La fecha de la actividad no es válida.");
        if (kind == "toner" && (command.SupplyQuantity <= 0 || command.SupplyStockBefore is < 0))
            throw Invalid("activity_quantity_invalid", "La cantidad de tóner debe ser un entero mayor que cero y la existencia no puede ser negativa.");
        return command with
        {
            ActivityKind = kind, ReportRecordId = RequiredGuid(command.ReportRecordId, "El reporte"),
            SubmissionKey = Required(command.SubmissionKey, "la clave de envío", 128), FinalizationFingerprint = fingerprint.ToLowerInvariant(),
            ClientId = RequiredGuid(command.ClientId, "El cliente"), EquipmentId = RequiredGuid(command.EquipmentId, "El equipo"),
            TechnicianSystemUserId = RequiredGuid(command.TechnicianSystemUserId, "El técnico"),
            EquipmentSerial = Required(command.EquipmentSerial, "el serial", 250), ServiceReference = Required(command.ServiceReference, "la referencia", 100),
            OccurredAtUtc = DateTimeOffset.FromUnixTimeSeconds(command.OccurredAtUtc.ToUnixTimeSeconds()),
            OriginClientId = string.IsNullOrWhiteSpace(command.OriginClientId) ? "" : RequiredGuid(command.OriginClientId, "El origen"),
            OriginClientName = CopiersMaintenanceV2Validation.Optional(command.OriginClientName, "activity_origin_name_invalid", "el nombre del cliente de origen", 850),
            MovementReason = kind == "movement" ? Required(command.MovementReason, "el motivo del movimiento", 100) : "",
            SupplyId = kind == "toner" ? RequiredGuid(command.SupplyId, "El tóner") : "",
            SupplyName = kind == "toner" ? Required(command.SupplyName, "el nombre del tóner", 850) : ""
        };
    }

    private static void ValidatePdf(CopiersMaintenanceV2StoredFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Content is null || file.Content.Length is < 5 or > MaxPdfBytes || file.Size != file.Content.LongLength
            || !file.Content.AsSpan(0, 5).SequenceEqual("%PDF-"u8) || file.ContentType != "application/pdf"
            || !string.Equals(Convert.ToHexString(SHA256.HashData(file.Content)), file.Sha256, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(file.FileName) || file.FileName.Length > 200
            || file.FileName.IndexOfAny(['/', '\\', '\r', '\n']) >= 0 || !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw Invalid("activity_pdf_invalid", "El PDF firmado no superó la validación de integridad.");
    }

    internal static string BuildBusinessRecordId(string reportId, string kind)
    {
        var data = $"digitaltech/copiers-activity-v2/{kind.Trim().ToLowerInvariant()}/{Guid.Parse(reportId):D}";
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(data)).AsSpan(0, 16)).ToString("D");
    }

    private static string Title(CopiersActivityV2BusinessCommand command) =>
        $"{command.ServiceReference} - {(command.ActivityKind == "movement" ? "Movimiento de equipo" : "Entrega de tóner")} - {command.EquipmentSerial}";
    private static string Instant(DateTimeOffset instant) => instant.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    private static string RequiredGuid(string value, string label) => CopiersMaintenanceV2Validation.RequiredGuid(value, "activity_id_invalid", label);
    private static string Required(string value, string label, int maximum) => CopiersMaintenanceV2Validation.Required(value, "activity_required", label, maximum);
    private static string Text(JsonElement row, string field) => row.TryGetProperty(field, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
    private static int? Integer(JsonElement row, string field) => row.TryGetProperty(field, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var value) ? value : null;
    private static string Version(JsonElement row)
    {
        var value = Text(row, "@odata.etag");
        return EntityVersion.IsMatch(value) ? value : throw Persistence("Dataverse no devolvió una versión segura del equipo o inventario.");
    }
    private static bool SameOptionalGuid(string left, string right) => string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right)
        || Guid.TryParse(left, out var leftId) && Guid.TryParse(right, out var rightId) && leftId == rightId;
    private static CopiersMaintenanceV2ValidationException Invalid(string code, string message) => new(code, message);
    private static CopiersMaintenanceV2ConcurrencyException Conflict(string message) => new(message);
    private static CopiersMaintenanceV2PersistenceException Persistence(string message) => new(message);
    private sealed record BusinessChange(Dictionary<string, object?> Record, string AssetPath, string AssetVersion, Dictionary<string, object?> AssetPatch);
    private sealed record BusinessDescriptor(string EntitySet, string IdField, string NameField, string DateField, string ClientLookup,
        string EquipmentLookup, string ClientNavigation, string EquipmentNavigation, string FileField);
    private static BusinessDescriptor Describe(string kind) => kind == "movement"
        ? new("cr07a_movimientosequiposes", "cr07a_movimientosequiposid", "cr07a_name", "cr07a_fecha", "_cr07a_cliente_value", "_cr07a_equipo_value", "cr07a_Cliente", "cr07a_Equipo", "cr07a_actadeentrega")
        : new("cr07a_entregas", "cr07a_entregaid", "cr07a_entrega1", "cr07a_fechadeentrega", "_cr07a_iddecliente_value", "_cr07a_iddeequipo_value", "cr07a_IDdecliente", "cr07a_IDdeequipo", "cr07a_comprobantedeentrega");
}
