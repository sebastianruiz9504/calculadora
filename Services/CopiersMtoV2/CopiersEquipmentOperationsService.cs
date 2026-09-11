using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using Microsoft.Extensions.Options;
using static CotizadorInterno.Web.Services.CopiersMtoV2.CopiersEquipmentOperationsLedger;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

public sealed class CopiersEquipmentOperationsService(CopiersEquipmentOperationsLedger ledger, IDataverseService dataverse,
    CopiersActivityV2Runtime runtime, IOptions<CopiersEquipmentOperationsOptions> settings,
    IOptions<CopiersMaintenanceV2Options> options, IOptions<CopiersMaintenanceV2DataverseOptions> bindings, ICopiersMtoV2PdfBuilder pdf)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    public async Task<CopiersMaintenanceV2ActorContext> AuthorizeAsync(CancellationToken ct)
    {
        ledger.EnsureEnabled();
        if (!options.Value.PilotEnabled || !options.Value.ActivitiesEnabled) throw Invalid("Las actividades de Copiers no están habilitadas.");
        var user=await dataverse.GetCurrentUserAsync(ct) ?? throw new UnauthorizedAccessException();
        var actor=CopiersMaintenanceV2Validation.Actor(new() {SystemUserId=user.SystemUserId,
            DisplayName=new[]{user.EmployeeName,user.EmployeeUserDisplayName,user.DisplayName}.FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x))??"Técnico",
            Email=new[]{user.EmployeeUserEmail,user.Email}.FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x))??""});
        if(options.Value.AllowedTechnicianEmails.Length>0&&!options.Value.AllowedTechnicianEmails.Contains(actor.Email,StringComparer.OrdinalIgnoreCase)) throw new UnauthorizedAccessException();
        return actor;
    }
    private bool Allowed(string id)=>options.Value.AllowedClientIds.Length==0||options.Value.AllowedClientIds.Contains(id,StringComparer.OrdinalIgnoreCase);
    private async Task<IReadOnlyList<CopiersMtoV2ClientOptionDto>> ClientsAsync(CancellationToken ct)=>
        (await dataverse.GetCopiersMtoV2ClientsAsync(ct)).Where(x=>Allowed(x.Id)).ToArray();

    public async Task<object> BootstrapAsync(CancellationToken ct)
    {
        var actor=await AuthorizeAsync(ct); var clients=await ClientsAsync(ct);
        // Delegated equipment visibility gates the application-only transit metadata.
        var visible=(await dataverse.GetCopiersMtoV2EquipmentAsync(ct)).Select(x=>x.RecordId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows=await ledger.QueryAsync($"cr07a_equipos?$select=cr07a_equipoid,cr07a_nombredelequipo,cr07a_referencia,_cr07a_cliente_value,{TransitField}&$filter=statecode eq 0",ct);
        var ids=clients.Select(x=>x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var equipment=new List<object>();
        foreach(var row in rows)
        {
            var id=Text(row,"cr07a_equipoid"); var origin=Text(row,"_cr07a_cliente_value");
            if(!visible.Contains(id)|| (origin.Length>0&&!ids.Contains(origin)))continue;
            var pendingText=Text(row,TransitField);
            var pending=pendingText.Length==0?null:JsonSerializer.Deserialize<CopiersEquipmentTransit>(pendingText);
            if(pending is not null && (!ids.Contains(pending.DestinationId) || (pending.OriginId.Length>0&&!ids.Contains(pending.OriginId)))) continue;
            equipment.Add(new {id,serial=Text(row,"cr07a_nombredelequipo"),reference=Text(row,"cr07a_referencia"),clientId=origin,
                clientName=clients.FirstOrDefault(x=>Same(x.Id,origin))?.Name??"Stock",pending});
        }
        return new { technician=actor.DisplayName, clients=clients.Select(x=>new {x.Id,x.Name,x.ContactName,x.Email,isInternal=settings.Value.IsInternal(x.Id)}),equipment };
    }

    public async Task<CopiersMaintenanceV2DraftRequestDto> PrepareAsync(CopiersMaintenanceV2FinalizeMultipartRequestDto request,
        IFormCollection form, CopiersMaintenanceV2ActorContext actor,CancellationToken ct)
    {
        string Value(string name)=>form[name].FirstOrDefault()?.Trim()??"";
        string Required(string name,int length)=>CopiersMaintenanceV2Validation.Required(Value(name),"operation_required",name,length);
        string GuidValue(string name)=>CopiersMaintenanceV2Validation.RequiredGuid(Value(name),"operation_guid",name);
        var kind=Required("OperationKind",20);
        if(kind is not ("delivery" or "withdrawal" or "replacement" or "receipt" or "internal"))throw Invalid("Selecciona una operación válida.");
        var clients=await ClientsAsync(ct);
        CopiersMtoV2ClientOptionDto Client(string id)=>clients.SingleOrDefault(x=>Same(x.Id,id))??throw new UnauthorizedAccessException("Cliente fuera del alcance autorizado.");
        var client=Client(GuidValue("ClientId"));
        var destination=Client(kind is "delivery" or "receipt" ? client.Id : GuidValue("DestinationId"));
        var visible=(await dataverse.GetCopiersMtoV2EquipmentAsync(ct)).Select(x=>x.RecordId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        async Task<CopiersOperationEquipment> Asset(string field,string condition,string accessories)
        {
            var id=GuidValue(field);
            if(!visible.Contains(id))throw new UnauthorizedAccessException("Equipo fuera del alcance autorizado.");
            var row=await ledger.EquipmentAsync(id,ct)??throw Invalid("El equipo no existe.");
            var origin=Text(row,"_cr07a_cliente_value");
            var name=origin.Length==0?"Stock":Client(origin).Name;
            return new() {Id=id,Serial=Text(row,"cr07a_nombredelequipo").Trim(),Reference=Text(row,"cr07a_referencia").Trim(),OriginId=origin,OriginName=name,
                Condition=Required(condition,160),Accessories=CopiersMaintenanceV2Validation.Optional(Value(accessories),"accessories_invalid","Los accesorios",160)};
        }
        var equipment=await Asset("EquipmentId","EquipmentCondition","EquipmentAccessories");
        var replacement=kind=="replacement"?await Asset("ReplacementId","ReplacementCondition","ReplacementAccessories"):null;
        var internalOnly=kind=="internal"||(kind=="receipt"&&settings.Value.IsInternal(client.Id));
        if(!internalOnly&&settings.Value.IsInternal(client.Id))throw Invalid("Selecciona el cliente que firma el certificado.");
        var pendingKey=kind=="receipt"?GuidValue("PendingKey"):"";
        if(kind=="receipt")
        {
            var row=await ledger.EquipmentAsync(equipment.Id,ct)??throw Invalid("Equipo no disponible.");
            var transitText=Text(row,TransitField);
            var transit=transitText.Length==0?null:JsonSerializer.Deserialize<CopiersEquipmentTransit>(transitText);
            if(transit is null || !Same(transit.OperationKey,pendingKey))throw Invalid("Selecciona un traslado pendiente.");
            if(transit.OriginId.Length>0) _=Client(transit.OriginId);
            // The existing destination and operation cannot be redirected by posted fields.
            if(!Same(transit.DestinationId,client.Id))throw new UnauthorizedAccessException();
            equipment=equipment with {OriginId=transit.OriginId,OriginName=transit.OriginName};
        }
        var op=new CopiersEquipmentOperation {Kind=kind,ClientId=client.Id,DestinationId=destination.Id,DestinationName=destination.Name,
            Internal=internalOnly,PendingKey=pendingKey,Reason=Required("MovementReason",100),Equipment=equipment,Replacement=replacement};
        if(internalOnly&&!string.Equals(Value("InternalConfirmed"),"true",StringComparison.OrdinalIgnoreCase))throw Invalid("Confirma la operación interna.");
        var started=request.ServiceStartedAtUtc??throw Invalid("Falta la hora de inicio.");
        var ended=request.ServiceEndedAtUtc??throw Invalid("Falta la hora de cierre.");
        if(started>ended || ended-started>TimeSpan.FromDays(1))throw Invalid("Revisa los horarios de la atención.");
        if(!internalOnly)
        {
            CopiersMaintenanceV2Validation.ValidateCustomerEmail(client.Email);
            _=CopiersMaintenanceV2Validation.DeviceSignedAt(request.DeviceSignedAtUtc,DateTimeOffset.UtcNow,options.Value);
            if(request.DeviceSignedAtUtc<ended||!request.CustomerAccepted)throw Invalid("Revisa los datos y solicita la firma del cliente.");
            _=CopiersMaintenanceV2Validation.Required(request.SignerName,"signer_required","el nombre de quien firma",200);
            _=CopiersMaintenanceV2Validation.Required(request.SignerRole,"role_required","el cargo de quien firma",150);
        }
        else
        {
            if(ended>DateTimeOffset.UtcNow.AddMinutes(3)||ended<DateTimeOffset.UtcNow.AddDays(-1))throw Invalid("Revisa la hora de la operación.");
            request.Signature=null;request.SignerName="";request.SignerRole="";request.CustomerAccepted=false;request.DeviceSignedAtUtc=null;
        }
        _=CopiersMaintenanceV2Validation.Location(request,DateTimeOffset.UtcNow,options.Value,enforceFreshness:false);
        request.ActivityKind="movement";request.FormVersion=CopiersActivityV2Bindings.FormVersion;request.AdditionalEquipmentJson="[]";
        request.InternalNotes=CopiersMaintenanceV2Validation.Optional(request.InternalNotes,"notes_invalid","Las notas internas",4000);
        request.ServiceAddress=CopiersMaintenanceV2Validation.Optional(request.ServiceAddress,"address_invalid","La dirección",300);
        request.CustomerObservations=internalOnly?"":CopiersMaintenanceV2Validation.Optional(request.CustomerObservations,"observations_invalid","Las observaciones",250);
        request.MovementDetailsJson=JsonSerializer.Serialize(op);request.OriginClientId=equipment.OriginId;request.MovementReason=op.Reason;request.WorkPerformed=op.Reason;
        var contact=internalOnly?"":Required("CustomerContactName",160);
        var answers=new Dictionary<string,string> {
            ["activity_kind"]="movement",["movement_reason"]=op.Reason,["origin_client_id"]=equipment.OriginId,["origin_client_name"]=equipment.OriginName,
            ["destination_client_name"]=client.Name,["equipment_reference"]=equipment.Reference,["onsite_contact"]=contact,["onsite_email"]=internalOnly?"":client.Email,
            ["service_started_at_utc"]=started.ToUniversalTime().ToString("O"),["service_ended_at_utc"]=ended.ToUniversalTime().ToString("O"),
            ["service_started_at"]=started.ToOffset(TimeSpan.FromHours(-5)).ToString("dd/MM/yyyy HH:mm"),["service_ended_at"]=ended.ToOffset(TimeSpan.FromHours(-5)).ToString("dd/MM/yyyy HH:mm")
        };
        request.AnswersJson=JsonSerializer.Serialize(answers.Select((x,i)=>new CopiersMaintenanceV2FormAnswerSnapshot {Key=x.Key,Label=x.Key,Value=x.Value,SortOrder=i}),WebJson);
        // Reject unreadable evidence and a certificate that cannot fit before admitting
        // an immutable receipt. Validation errors can still be corrected in the form.
        var attachments=CopiersMaintenanceV2Validation.BuildCustomerSafeAttachments(await CopiersMaintenanceV2Validation.ReadAttachmentsAsync(request.Attachments,options.Value,ct),options.Value);
        if(!internalOnly)
        {
            if(request.SignaturePointCount<options.Value.MinSignaturePointCount)throw Invalid("La firma no contiene suficientes trazos.");
            var signature=await CopiersMaintenanceV2Validation.ReadSignatureAsync(request.Signature,options.Value,ct);
            var publicAnswers=CopiersMaintenanceV2Validation.ParseAnswers(request.AnswersJson,options.Value,CopiersActivityV2Bindings.FormVersion)
                .Concat(CopiersActivityV2Capture.OperationAnswers(op)).Where(x=>x.Key is not ("equipment_operation" or "operation_link" or "origin_client_id" or "origin_client_name" or "destination_client_name")).ToArray();
            _=await pdf.BuildAsync(new() {ServiceReference="ACT-999999999",RecordId=Id(request.SubmissionKey,99),ClientName=client.Name,
                CustomerContactName=contact,EquipmentSerial=equipment.Serial,Title=op.Title,ServiceDate=DateOnly.FromDateTime(started.ToOffset(TimeSpan.FromHours(-5)).DateTime),
                TechnicianName=actor.DisplayName,FormVersion=CopiersActivityV2Bindings.FormVersion,Answers=publicAnswers,WorkPerformed=op.Reason,
                CustomerObservations=request.CustomerObservations,SignerName=request.SignerName,SignerRole=request.SignerRole,DeviceSignedAtUtc=request.DeviceSignedAtUtc!.Value,
                SignatureContent=signature.Content,SignatureContentType=signature.ContentType,Attachments=attachments.Select(x=>new CopiersMaintenanceV2PdfAttachmentManifestItem{FileName=x.FileName,Size=x.Size,Sha256=x.Sha256}).ToArray()},ct);
        }
        await ledger.ValidateAsync(new() {ReportRecordId=Id(request.SubmissionKey,99),ClientId=client.Id,EquipmentId=equipment.Id,EquipmentSerial=equipment.Serial,
            TechnicianSystemUserId=actor.SystemUserId,OccurredAtUtc=ended,Operation=op,FinalizationFingerprint="preflight"},ct);
        return new() {SubmissionKey=request.SubmissionKey,ClientId=client.Id,ClientName=client.Name,CustomerContactName=contact,CustomerEmail=internalOnly?"":client.Email.Trim(),
            EquipmentId=equipment.Id,EquipmentSerial=equipment.Serial,Title=$"{op.Title} · {equipment.Serial}",ServiceDate=DateOnly.FromDateTime(started.ToOffset(TimeSpan.FromHours(-5)).DateTime),MaintenanceTypeValue=CopiersActivityV2Bindings.MovementType};
    }

    public async Task<CopiersMaintenanceV2FinalizeResultDto> CompleteInternalAsync(CopiersMaintenanceV2DraftRequestDto draft,
        CopiersMaintenanceV2FinalizeMultipartRequestDto request,CopiersMaintenanceV2ActorContext actor,CancellationToken ct)
    {
        ledger.EnsureEnabled();
        var op=CopiersEquipmentOperation.Read(request.MovementDetailsJson)??throw Invalid("Falta la operación interna.");
        if(!op.Internal||op.Kind is not ("internal" or "receipt"))throw Invalid("La operación requiere certificado de cliente.");
        var repository=runtime.CreateRepository();
        var row=await repository.CreateOrGetDraftAsync(new() {
            SubmissionKey=draft.SubmissionKey,TechnicianSystemUserId=actor.SystemUserId,TechnicianName=actor.DisplayName,TechnicianEmail=actor.Email,
            ClientId=draft.ClientId,ClientName=draft.ClientName,CustomerEmail="",CustomerContactName="",EquipmentId=draft.EquipmentId,EquipmentSerial=draft.EquipmentSerial,
            Title=draft.Title,ServiceDate=draft.ServiceDate,MaintenanceTypeValue=CopiersActivityV2Bindings.MovementType
        },ct);
        if(!Same(row.TechnicianSystemUserId,actor.SystemUserId)||row.SubmissionKey!=request.SubmissionKey||!Same(row.ClientId,draft.ClientId)||!Same(row.EquipmentId,draft.EquipmentId))throw new UnauthorizedAccessException();
        var files=await CopiersMaintenanceV2Validation.ReadAttachmentsAsync(request.Attachments,options.Value,ct);
        var safe=CopiersMaintenanceV2Validation.BuildCustomerSafeAttachments(files,options.Value);
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {draft,request,files=files.Select(x=>x.Sha256)}))));
        if(row.FinalizationFingerprint.Length>0&&!Same(row.FinalizationFingerprint,fingerprint))throw new CopiersMaintenanceV2ConcurrencyException("La clave ya contiene otra operación.");
        var command=new CopiersActivityV2BusinessCommand {ReportRecordId=row.RecordId,SubmissionKey=request.SubmissionKey,FinalizationFingerprint=fingerprint,
            ActivityKind="movement",ClientId=draft.ClientId,EquipmentId=draft.EquipmentId,EquipmentSerial=draft.EquipmentSerial,TechnicianSystemUserId=actor.SystemUserId,
            ServiceReference=row.ServiceReference,ServiceDate=draft.ServiceDate,OccurredAtUtc=request.ServiceEndedAtUtc!.Value,Operation=op};
        await ledger.ValidateAsync(command,ct);
        var evidenceKeys=await repository.SaveInternalOperationEvidenceAsync(row.RecordId,request.SubmissionKey,actor.SystemUserId,safe,request.ServiceEndedAtUtc.Value,ct);
        await ledger.CommitAsync(command,null,ct);
        var o=CopiersActivityV2Bindings.Create(bindings.Value);
        var current=await ledger.ReadAsync($"{o.MainEntitySetName}({row.RecordId})?$select={o.WorkflowStateField},{o.FinalizationFingerprintField},{o.EmailStateField}",ct)??throw Invalid("No se encontró la operación.");
        var answers=JsonSerializer.Deserialize<List<CopiersMaintenanceV2FormAnswerSnapshot>>(request.AnswersJson,WebJson)??[];
        answers.AddRange(CopiersActivityV2Capture.OperationAnswers(op));
        var location=CopiersMaintenanceV2Validation.Location(request,DateTimeOffset.UtcNow,options.Value,enforceFreshness:false);
        var patch=new Dictionary<string,object?> {
            [o.WorkflowStateField]=o.ReadyToSendStateValue,[o.EmailStateField]=o.EmailNotReadyStateValue,[o.FormVersionField]=CopiersActivityV2Bindings.FormVersion,
            [o.AnswersJsonField]=JsonSerializer.Serialize(answers,WebJson),[o.WorkPerformedField]=op.Reason,[o.InternalNotesField]=request.InternalNotes,
            [o.ServiceAddressInternalField]=request.ServiceAddress,[o.FinalizationFingerprintField]=fingerprint,[o.ServerFinalizedAtUtcField]=request.ServiceEndedAtUtc.Value.ToUniversalTime().ToString("O"),
            [o.AttachmentManifestJsonField]=JsonSerializer.Serialize(safe.Select((file,i)=>new {evidenceKey=evidenceKeys[i],sha256=file.Sha256.ToLowerInvariant(),file.FileName,file.ContentType,file.Size}),WebJson),
            [o.CustomerAcceptedField]=false,[o.AttachmentCountField]=safe.Count,[o.LatitudeField]=location?.Latitude,[o.LongitudeField]=location?.Longitude,
            [o.AccuracyMetersField]=location?.AccuracyMeters,[o.LocationCapturedAtUtcField]=location?.CapturedAtUtc.ToUniversalTime().ToString("O"),[o.LocationSourceField]=location?.Source??"not-captured"
        };
        if(current.GetProperty(o.WorkflowStateField).GetInt32()!=o.ReadyToSendStateValue)
            await ledger.BatchAsync([new($"{o.MainEntitySetName}({row.RecordId})",Text(current,"@odata.etag"),false,patch)],ct);
        var final=await ledger.ReadAsync($"{o.MainEntitySetName}({row.RecordId})?$select={o.WorkflowStateField},{o.FinalizationFingerprintField},{o.EmailStateField}",ct)??throw Invalid("Operación no disponible.");
        if(final.GetProperty(o.WorkflowStateField).GetInt32()!=o.ReadyToSendStateValue||!Same(Text(final,o.FinalizationFingerprintField),fingerprint)||final.GetProperty(o.EmailStateField).GetInt32()!=o.EmailNotReadyStateValue)
            throw new CopiersMaintenanceV2PersistenceException("No se confirmó el cierre interno. Se conservará el envío.");
        return new() {RecordId=row.RecordId,ServiceReference=row.ServiceReference,SubmissionKey=request.SubmissionKey,State=CopiersMaintenanceV2WorkflowState.ReadyToSend,
            EmailRequired=false,EmailState=CopiersMaintenanceV2EmailState.NotReady,AttachmentCount=safe.Count,Message="Operación interna registrada. No requiere correo ni firma de cliente."};
    }
}
