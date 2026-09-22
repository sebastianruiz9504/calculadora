using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Copiers;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.RH;
using CotizadorInterno.Web.Services.CopiersMtoV2;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private ICopiersMtoV2ApplicationDataverseClient MtoHistoryClient => _httpContextAccessor.HttpContext?.RequestServices
        .GetRequiredService<ICopiersMtoV2ApplicationDataverseClient>() ?? throw new UnauthorizedAccessException();

    private async Task<CurrentUserInfo> RequireMtoHistoryAccessAsync(CancellationToken ct)
    {
        if (_httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated != true) throw new UnauthorizedAccessException();
        var user=await GetCurrentUserAsync(ct);
        if(user is null || !AppModuleAccessPolicy.CanAccess(AppModule.Copiers,user)) throw new UnauthorizedAccessException();
        return user;
    }

    private async Task<List<JsonElement>> ReadMtoHistoryAsync(string path,CancellationToken ct)
    {
        var result=new List<JsonElement>(); var seen=new HashSet<string>();
        for(int page=0;page<100;page++)
        {
            if(!seen.Add(path)) throw new InvalidOperationException("La consulta de mantenimientos repitió una página.");
            using var response=await MtoHistoryClient.SendAsync(path,HttpMethod.Get,null,r=>r.Headers.TryAddWithoutValidation("Prefer","odata.maxpagesize=500"),ct);
            if(!response.IsSuccessStatusCode) throw new InvalidOperationException("No fue posible consultar los mantenimientos V2.");
            using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if(!doc.RootElement.TryGetProperty("value",out var rows)) throw new InvalidOperationException("Respuesta de mantenimientos incompleta.");
            result.AddRange(rows.EnumerateArray().Select(x=>x.Clone()));
            var next=CopiersMaintenanceHistory.Text(doc.RootElement,"@odata.nextLink");
            if(string.IsNullOrEmpty(next)) return result;
            if(!Uri.TryCreate(next,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.Host!="orgc79ca19c.crm2.dynamics.com" || uri.Port!=443 || uri.UserInfo.Length>0 || !uri.AbsolutePath.StartsWith("/api/data/v9.2/",StringComparison.Ordinal))
                throw new InvalidOperationException("La paginación no pertenece al entorno autorizado.");
            path=uri.PathAndQuery;
        }
        throw new InvalidOperationException("La consulta de mantenimientos supera el límite de páginas.");
    }

    private async Task<List<CopiersMaintenanceRecordRow>> GetUnifiedMaintenanceRowsAsync(bool personal,CancellationToken ct,string? equipmentId=null)
    {
        var actor=await RequireMtoHistoryAccessAsync(ct);
        // Personal endpoints always enforce the authenticated system user, including administrators.
        if(!personal && !AppModuleAccessPolicy.CanAccess(AppModule.Dashboard,actor)) personal=true;
        var fields="dtc_copiersmtov2id,dtc_reference,dtc_title,dtc_formversion,dtc_legacysourcekey,dtc_businessstatus,dtc_workflowstate,dtc_maintenancetype,dtc_servicedate,dtc_technicianuserkey,dtc_techniciannamesnapshot,_dtc_client_value,dtc_clientnamesnapshot,_dtc_equipment_value,dtc_equipmentserialsnapshot,dtc_workperformed,dtc_answersjson,dtc_reportevidencekey,dtc_reportfilename";
        var filter="statecode eq 0 and (dtc_workflowstate eq 827270002 or dtc_workflowstate eq 827270003 or dtc_workflowstate eq 827270004)";
        if(personal) filter+=$" and dtc_technicianuserkey eq '{NormalizeGuid(actor.SystemUserId,nameof(actor.SystemUserId))}'";
        var rows=await ReadMtoHistoryAsync($"/api/data/v9.2/dtc_copiersmtov2s?$select={fields}&$filter={Uri.EscapeDataString(filter)}&$orderby=dtc_servicedate desc",ct);
        return rows.SelectMany(CopiersMaintenanceHistory.Project)
            .Where(r=>(!personal || r.TechnicianId.Equals(actor.SystemUserId,StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrWhiteSpace(equipmentId) || r.EquipmentId.Equals(equipmentId,StringComparison.OrdinalIgnoreCase)))
            .Select(r=>new CopiersMaintenanceRecordRow {
                RecordId=r.RecordId,Title=r.Title,InternalId=r.InternalId,EquipmentId=r.EquipmentId,EquipmentSerial=r.EquipmentSerial,
                MaintenanceDate=DateOnly.ParseExact(r.DateValue,"yyyy-MM-dd",CultureInfo.InvariantCulture),DateValue=r.DateValue,DateDisplay=r.DateDisplay,
                Description=r.Description,ClientId=r.ClientId,ClientName=r.ClientName,HasAttachment=r.HasAttachment,AttachmentFileName=r.AttachmentFileName,
                MaintenanceTypeValue=r.MaintenanceTypeValue,MaintenanceTypeLabel=r.MaintenanceTypeLabel,MaintenanceStatusValue=r.MaintenanceStatusValue,
                MaintenanceStatusLabel=r.MaintenanceStatusLabel,TechnicianId=r.TechnicianId,TechnicianName=r.TechnicianName,IsHistorical=r.IsHistorical,SourceLabel=r.SourceLabel
            }).ToList();
    }

    private async Task<CopiersMaintenanceSaveResultDto> UpdateHistoricalMaintenanceStatusAsync(CopiersMaintenanceSaveRequestDto request,CancellationToken ct)
    {
        var actor=await RequireMtoHistoryAccessAsync(ct);
        if(!Guid.TryParse(request.RecordId,out var id)) throw new InvalidOperationException("Registra los nuevos mantenimientos desde MTO firmado V2.");
        var rows=await ReadMtoHistoryAsync($"/api/data/v9.2/dtc_copiersmtov2s?$filter=dtc_copiersmtov2id eq {id:D} and statecode eq 0&$top=1",ct);
        if(rows.Count!=1 || !CopiersMaintenanceHistory.IsHistorical(rows[0]) || !string.Equals(CopiersMaintenanceHistory.Text(rows[0],"dtc_technicianuserkey"),actor.SystemUserId,StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Solo puedes actualizar el estado de tus mantenimientos históricos.");
        if(request.MaintenanceStatusValue is not (645250000 or 645250001))throw new ArgumentException("Estado de mantenimiento inválido.");
        var etag=CopiersMaintenanceHistory.Text(rows[0],"@odata.etag");
        if(string.IsNullOrWhiteSpace(etag))throw new InvalidOperationException("No se pudo comprobar la versión del histórico.");
        using var content=new StringContent(JsonSerializer.Serialize(new {dtc_businessstatus=request.MaintenanceStatusValue}),Encoding.UTF8,"application/json");
        using var response=await MtoHistoryClient.SendAsync($"/api/data/v9.2/dtc_copiersmtov2s({id:D})",HttpMethod.Patch,content,r=>r.Headers.TryAddWithoutValidation("If-Match",etag),ct);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException("El histórico cambió o no pudo actualizarse. Recarga antes de intentar nuevamente.");
        var after=BuildMaintenanceRows(await GetUnifiedMaintenanceRowsAsync(true,ct)).Single(r=>r.RecordId==id.ToString("D"));
        if(after.MaintenanceStatusValue!=request.MaintenanceStatusValue)throw new InvalidOperationException("El estado guardado no pudo verificarse.");
        return new() {Record=after,Message="Estado del histórico actualizado. Su acta y datos originales se conservaron."};
    }

    private async Task<RhFileDownloadResult?> DownloadUnifiedMaintenanceAttachmentAsync(string maintenanceId,CancellationToken ct)
    {
        var actor=await RequireMtoHistoryAccessAsync(ct);var id=NormalizeGuid(maintenanceId,nameof(maintenanceId));
        var rows=await ReadMtoHistoryAsync($"/api/data/v9.2/dtc_copiersmtov2s?$filter=dtc_copiersmtov2id eq {id} and statecode eq 0&$top=1",ct);
        if(rows.Count!=1 || !CopiersMaintenanceHistory.IsVisible(rows[0]))return null;
        var parent=rows[0];
        if(!AppModuleAccessPolicy.CanAccess(AppModule.Dashboard,actor) && !string.Equals(CopiersMaintenanceHistory.Text(parent,"dtc_technicianuserkey"),actor.SystemUserId,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException();
        var key=CopiersMaintenanceHistory.Text(parent,"dtc_reportevidencekey");
        if(key.Length!=64 || !key.All(Uri.IsHexDigit))return null;
        var evidence=await ReadMtoHistoryAsync($"/api/data/v9.2/dtc_copiersmtoevidenciav2s?$filter=_dtc_signedmto_value eq {id} and dtc_evidencekey eq '{key}'&$top=2",ct);
        if(evidence.Count!=1)return null;
        var file=evidence[0];var history=CopiersMaintenanceHistory.IsHistorical(parent);
        var purpose=CopiersMaintenanceHistory.Number(file,"dtc_purpose");
        if(purpose!=(history?CopiersMaintenanceHistory.HistoricalFilePurpose:827270001))throw new InvalidOperationException("El archivo no corresponde al reporte.");
        var expected=CopiersMaintenanceHistory.Text(file,"dtc_sha256");
        if(expected.Length!=64 || !expected.All(Uri.IsHexDigit) || expected!=CopiersMaintenanceHistory.Text(parent,"dtc_reportsha256"))throw new InvalidOperationException("La referencia del archivo no coincide.");
        var fileId=NormalizeGuid(CopiersMaintenanceHistory.Text(file,"dtc_copiersmtoevidenciav2id"),"fileId");
        var max=history?32*1024*1024:12*1024*1024;
        using var response=await MtoHistoryClient.SendAsync($"/api/data/v9.2/dtc_copiersmtoevidenciav2s({fileId})/dtc_filecontent/$value",HttpMethod.Get,null,null,ct);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException("No fue posible descargar el archivo.");
        await using var input=await response.Content.ReadAsStreamAsync(ct);using var output=new MemoryStream();var buffer=new byte[65536];int read;
        while((read=await input.ReadAsync(buffer,ct))>0) {if(output.Length+read>max)throw new InvalidOperationException("El archivo supera el límite autorizado.");output.Write(buffer,0,read);}
        var bytes=output.ToArray();
        if(bytes.Length!=file.GetProperty("dtc_bytelength").GetInt64() || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes),Convert.FromHexString(expected)))throw new InvalidOperationException("El archivo no pasó la verificación de integridad.");
        var mime=CopiersMaintenanceHistory.Text(file,"dtc_contenttype");
        if(mime is not ("application/pdf" or "image/jpeg" or "image/png"))throw new InvalidOperationException("Tipo de archivo no autorizado.");
        var signatureOk = mime == "application/pdf" ? bytes.AsSpan().StartsWith("%PDF-"u8) : mime == "image/jpeg" ? bytes.AsSpan().StartsWith(new byte[]{255,216,255}) : bytes.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10});
        if(!signatureOk)throw new InvalidOperationException("El contenido no corresponde al tipo de archivo.");
        var stable = await ReadMtoHistoryAsync($"/api/data/v9.2/dtc_copiersmtoevidenciav2s?$filter=dtc_copiersmtoevidenciav2id eq {fileId}&$top=1",ct);
        if(stable.Count!=1 || string.IsNullOrEmpty(CopiersMaintenanceHistory.Text(file,"@odata.etag")) || CopiersMaintenanceHistory.Text(stable[0],"@odata.etag")!=CopiersMaintenanceHistory.Text(file,"@odata.etag"))throw new InvalidOperationException("El archivo cambió durante la descarga.");
        return new() {Content=bytes,ContentType=mime,FileName=Path.GetFileName(CopiersMaintenanceHistory.Text(file,"dtc_originalfilename"))};
    }
}
