using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using CotizadorInterno.Web.Models.Conciliacion;
using Microsoft.Extensions.Caching.Memory;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string DirectAssignmentSet="cr07a_clasificacionflujodirectos";
    private const string DirectManualSet="cr07a_movimientomanualbancos";
    private const string DirectBalanceSet="cr07a_saldoflujodirectos";
    private static long DirectCashFlowRevision;
    private static readonly SemaphoreSlim DirectCashFlowReadGate=new(1,1);
    private static readonly SemaphoreSlim DirectCashFlowSeedGate=new(1,1);

    private async Task<IReadOnlyList<ConciliacionCashFlowRowDto>> AddDirectCashFlowRowsAsync(
        IReadOnlyList<ConciliacionCashFlowRowDto> source, DateOnly start,DateOnly end,CancellationToken ct)
    {
        var filter=Uri.EscapeDataString($"cr07a_fecha ge {start:yyyy-MM-dd}T00:00:00Z and cr07a_fecha lt {end:yyyy-MM-dd}T00:00:00Z");
        var assignmentsTask=GetDataverseAppEntitiesAsync($"/api/data/v9.2/{DirectAssignmentSet}?$select=cr07a_clasificacionflujodirectoid,cr07a_concepto,cr07a_estado,cr07a_motivo&$filter={filter}",ct);
        var manualTask=GetDataverseAppEntitiesAsync($"/api/data/v9.2/{DirectManualSet}?$select=cr07a_movimientomanualbancoid,cr07a_fecha,cr07a_banco,cr07a_entrada,cr07a_salida,cr07a_descripcion,cr07a_referencia,cr07a_concepto,modifiedon&$filter={filter} and statecode eq 0",ct);
        await Task.WhenAll(assignmentsTask,manualTask);
        var assignments=assignmentsTask.Result.ToDictionary(x=>ReadString(x,"cr07a_clasificacionflujodirectoid"),StringComparer.OrdinalIgnoreCase);
        foreach(var row in source)
        {
            if(assignments.TryGetValue(row.RecordId,out var a))
            {
                row.DirectConceptKey=ReadString(a,"cr07a_concepto");row.DirectStatus=ReadString(a,"cr07a_estado");
                row.DirectReason=ReadString(a,"cr07a_motivo");row.DirectVersion=ReadString(a,"@odata.etag");
                var concept=DirectCashFlowCatalog.Find(row.DirectConceptKey);
                if(concept?.Direction!=row.Direction) {row.DirectConceptKey="";row.DirectStatus="Pendiente";row.DirectReason="La dirección cambió. Revisa el concepto.";}
            }
        }
        return source.Concat(manualTask.Result.Select(ParseDirectManualRow)).OrderByDescending(x=>x.MovementDateValue).ThenBy(x=>x.RecordId).ToArray();
    }
    private static ConciliacionCashFlowRowDto ParseDirectManualRow(JsonElement item)
    {
        var bank=ReadString(item,"cr07a_banco");var entry=ReadDecimal(item,"cr07a_entrada")??0;var exit=ReadDecimal(item,"cr07a_salida")??0;
        var date=ReadDateOnly(item,"cr07a_fecha");
        var account=ResolveConciliacionCashFlowBankAccount(bank);
        return new()
        {
            RecordId=ReadString(item,"cr07a_movimientomanualbancoid"),SourceKind="Manual",SourceKindLabel="Manual · solo banco",
            MovementDateValue=date?.ToString("yyyy-MM-dd")??"",MovementDateDisplay=date?.ToString("dd/MM/yyyy")??"",
            SourceFlow=bank,BankAccountCode=account.Code,BankAccountName=account.Name,
            EntryValue=entry,ExitValue=exit,Amount=Math.Max(entry,exit),Direction=entry>0?"Entrada":"Salida",DirectionTone=entry>0?"success":"danger",
            Description=ReadString(item,"cr07a_descripcion"),Observations=ReadString(item,"cr07a_referencia"),
            DetectedTypeKey="manual-bancario",DetectedTypeLabel="Manual · solo banco",DetectedTypeTone="manual",
            DataverseStatus="SoloBanco",SiigoStatus="NoEnviar",RegistrationStatus="Solo banco · no enviado a Siigo",ValidationStatus="Registro manual",CanValidate=false,
            DirectConceptKey=ReadString(item,"cr07a_concepto"),DirectStatus="Confirmado",DirectVersion=ReadString(item,"@odata.etag"),
            ModifiedOnValue=ReadString(item,"modifiedon")
        };
    }
    private static DirectCashFlowMovement ToDirectMovement(ConciliacionCashFlowRowDto r)
    {
        var c=DirectCashFlowCatalog.Find(r.DirectConceptKey);
        return new()
        {
            RecordId=r.RecordId,SourceKind=r.SourceKind,Date=r.MovementDateValue,Bank=r.SourceFlow,
            Description=r.Description,Reference=r.Observations,Direction=r.Direction,Amount=r.Amount,Entry=r.EntryValue,Exit=r.ExitValue,
            ConceptKey=c?.Key??"",ConceptLabel=c is null?"Sin clasificar":$"{c.Code} · {c.Label}",
            Status=c is null?"Pendiente":r.DirectStatus,Reason=r.DirectReason,Version=r.DirectVersion,
            IsInternalTransfer=r.SourceKind=="Traslado" || r.DetectedTypeKey=="traslado-interno"
        };
    }
    public async Task<DirectCashFlowData> GetDirectCashFlowDataAsync(int year,int first,int last,CancellationToken ct=default)
    {
        DirectCashFlowReportBuilder.ValidatePeriod(year,first,last);
        var key=$"direct-cash:{_dataverseBaseUrl}:{year}:{first}:{last}:{Interlocked.Read(ref DirectCashFlowRevision)}";
        if(_memoryCache.TryGetValue<DirectCashFlowData>(key,out var cached)&&cached!=null)return cached;
        await DirectCashFlowReadGate.WaitAsync(ct);
        try
        {
            if(_memoryCache.TryGetValue<DirectCashFlowData>(key,out cached)&&cached!=null)return cached;
            var start=new DateOnly(year,first,1);var end=new DateOnly(year,last,1).AddMonths(1);
            var sourceTask=GetConciliacionCashFlowRowsAsync(start,end,ct);
            var periodFilter=Uri.EscapeDataString($"cr07a_periodo ge '{year:D4}-{first:D2}' and cr07a_periodo le '{year:D4}-{last:D2}'");
            var balancesTask=GetDataverseAppEntitiesAsync($"/api/data/v9.2/{DirectBalanceSet}?$select=cr07a_periodo,cr07a_inicial,cr07a_cloud,cr07a_copiers,cr07a_bolsillos,cr07a_fidurenta&$filter={periodFilter}",ct);
            var openingFilter=Uri.EscapeDataString($"cr07a_periodokey ge '{year:D4}-{first:D2}' and cr07a_periodokey le '{year:D4}-{last:D2}'");
            var openingsTask=GetDataverseAppEntitiesAsync($"/api/data/v9.2/{ConciliacionBankCloseSetName}?$select=cr07a_periodokey,cr07a_origenflujo,cr07a_saldoinicial&$filter={openingFilter}",ct);
            await Task.WhenAll(sourceTask,balancesTask,openingsTask);
            var movements=sourceTask.Result.Select(ToDirectMovement).ToArray();
            var omitted=sourceTask.Result.Where(x=>x.DataverseStatus=="Omitido" && x.Description.Contains("CONCILIO",StringComparison.OrdinalIgnoreCase)).Select(x=>x.RecordId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            DirectCashFlowReportBuilder.MarkInternalTransfers(movements,omitted);
            var balances=balancesTask.Result.Select(x=>
            {
                var period=ReadString(x,"cr07a_periodo");
                return new DirectCashFlowBalances {Year=year,Month=int.Parse(period[5..],CultureInfo.InvariantCulture),Opening=ReadDecimal(x,"cr07a_inicial"),Cloud=ReadDecimal(x,"cr07a_cloud"),Copiers=ReadDecimal(x,"cr07a_copiers"),Pockets=ReadDecimal(x,"cr07a_bolsillos"),Fiduciary=ReadDecimal(x,"cr07a_fidurenta"),Version=ReadString(x,"@odata.etag")};
            }).ToArray();
            var openings=openingsTask.Result.GroupBy(x=>ReadString(x,"cr07a_periodokey")).Where(g=>g.Count()==2 && g.Select(x=>ReadString(x,"cr07a_origenflujo")).Distinct().Count()==2)
                .ToDictionary(g=>int.Parse(g.Key[5..],CultureInfo.InvariantCulture),g=>g.Sum(x=>ReadDecimal(x,"cr07a_saldoinicial")??0));
            var result=new DirectCashFlowData {Movements=movements,Balances=balances,BankOpenings=openings};
            _memoryCache.Set(key,result,TimeSpan.FromSeconds(20));return result;
        }
        finally{DirectCashFlowReadGate.Release();}
    }
    public async Task<DirectCashFlowMovement> GetDirectCashFlowMovementAsync(string sourceKind,string recordId,CancellationToken ct=default)
    {
        var source=await ReadDirectSourceAsync(sourceKind,recordId,ct);
        var date=DateOnly.Parse(source.MovementDateValue,CultureInfo.InvariantCulture);
        var data=await GetDirectCashFlowDataAsync(date.Year,date.Month,date.Month,ct);
        return data.Movements.FirstOrDefault(x=>x.RecordId==source.RecordId&&x.SourceKind==sourceKind)??throw new InvalidOperationException("El registro ya no está disponible.");
    }
    private async Task<ConciliacionCashFlowRowDto> ReadDirectSourceAsync(string kind,string id,CancellationToken ct)
    {
        if(!Guid.TryParse(id,out var guid))throw new InvalidOperationException("Registro inválido.");
        if(kind=="Manual")
        {
            using var doc=JsonDocument.Parse(await CallDataverseAppGetJsonAsync($"/api/data/v9.2/{DirectManualSet}({guid})",ct));
            if(ReadInt(doc.RootElement,"statecode")!=0)throw new InvalidOperationException("El registro manual está anulado.");
            return ParseDirectManualRow(doc.RootElement);
        }
        if(kind is not ("Movimiento" or "Traslado"))throw new InvalidOperationException("Origen de registro inválido.");
        var transfer=kind=="Traslado";
        var metadata=await ResolveFinancialReconciliationEntityMetadataAppAsync(transfer?CashFlowTransferLogicalName:CashFlowMovementLogicalName,transfer?CashFlowTransferSetName:CashFlowMovementSetName,transfer?CashFlowTransferIdField:CashFlowMovementIdField,transfer?CashFlowTransferPrimaryNameField:CashFlowMovementPrimaryNameField,ct);
        var attrs=await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName,ct);
        return (transfer?await GetConciliacionCashFlowTransferByIdAsync(metadata,attrs,guid.ToString(),ct):await GetConciliacionCashFlowMovementByIdAsync(metadata,attrs,guid.ToString(),ct))??throw new InvalidOperationException("Registro no encontrado.");
    }
    public async Task SaveDirectCashFlowAssignmentAsync(DirectCashFlowAssignmentRequest request,CancellationToken ct=default)
    {
        var row=await ReadDirectSourceAsync(request.SourceKind,request.RecordId,ct);
        DirectCashFlowCatalog.Require(request.ConceptKey,row.Direction);
        var current=await GetDirectCashFlowMovementAsync(request.SourceKind,request.RecordId,ct);
        if(current.IsInternalTransfer)throw new InvalidOperationException("Los traslados entre cuentas propias se excluyen del consolidado.");
        if(request.SourceKind=="Manual")
            await DirectWriteAsync(DirectManualSet,Guid.Parse(request.RecordId),new Dictionary<string,object?>{["cr07a_concepto"]=request.ConceptKey},request.Version,ct,requireExisting:true);
        else
            await DirectWriteAsync(DirectAssignmentSet,Guid.Parse(row.RecordId),new Dictionary<string,object?>
            {
                ["cr07a_name"]=$"{row.SourceKind} {row.RecordId}",["cr07a_origen"]=row.SourceKind,["cr07a_fecha"]=row.MovementDateValue,
                ["cr07a_concepto"]=request.ConceptKey,["cr07a_estado"]="Confirmado",["cr07a_motivo"]="Clasificación revisada por el usuario."
            },request.Version,ct);
        Interlocked.Increment(ref DirectCashFlowRevision);
    }
    public async Task<DirectCashFlowMovement> SaveDirectCashFlowManualAsync(DirectCashFlowManualRequest request,CancellationToken ct=default)
    {
        if(!Guid.TryParse(request.RecordId,out var id))throw new InvalidOperationException("Identificador manual inválido.");
        if(!DateOnly.TryParseExact(request.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)||date.Year is < 2020 or > 2100)throw new InvalidOperationException("Fecha inválida.");
        if(request.Bank is not ("Cloud" or "Copiers"))throw new InvalidOperationException("Selecciona la cuenta Cloud o Copiers.");
        if(request.Amount<=0||request.Amount>100_000_000_000m||decimal.Round(request.Amount,2)!=request.Amount)throw new InvalidOperationException("El importe debe ser positivo y tener máximo dos decimales.");
        if(request.Description.Trim().Length is < 3 or > 500||request.Reference.Length>200)throw new InvalidOperationException("Escribe una descripción de 3 a 500 caracteres y una referencia de máximo 200.");
        DirectCashFlowCatalog.Require(request.ConceptKey,request.Direction);
        var payload=new Dictionary<string,object?>
        {
            ["cr07a_name"]=$"Manual {date:yyyy-MM-dd} {request.Bank}",["cr07a_fecha"]=request.Date,["cr07a_banco"]=request.Bank,
            ["cr07a_entrada"]=request.Direction=="Entrada"?request.Amount:0m,["cr07a_salida"]=request.Direction=="Salida"?request.Amount:0m,
            ["cr07a_descripcion"]=request.Description.Trim(),["cr07a_referencia"]=request.Reference.Trim(),["cr07a_concepto"]=request.ConceptKey,
            ["cr07a_origen"]="Manual",["cr07a_estadosiigo"]="NoEnviar"
        };
        // Same client UUID makes network retries idempotent. Edits use the persisted ETag.
        if(string.IsNullOrEmpty(request.Version))
        {
            var filter=Uri.EscapeDataString($"statecode eq 0 and cr07a_fecha eq {request.Date}T00:00:00Z and cr07a_banco eq '{request.Bank}' and cr07a_entrada eq {Convert.ToDecimal(payload["cr07a_entrada"]).ToString(CultureInfo.InvariantCulture)} and cr07a_salida eq {Convert.ToDecimal(payload["cr07a_salida"]).ToString(CultureInfo.InvariantCulture)}");
            var duplicates=await GetDataverseAppEntitiesAsync($"/api/data/v9.2/{DirectManualSet}?$select=cr07a_movimientomanualbancoid,cr07a_descripcion,cr07a_referencia,cr07a_concepto&$filter={filter}",ct);
            if(duplicates.Any(x=>ReadString(x,"cr07a_movimientomanualbancoid")==id.ToString()&&ReadString(x,"cr07a_descripcion")==request.Description.Trim()&&ReadString(x,"cr07a_referencia")==request.Reference.Trim()&&ReadString(x,"cr07a_concepto")==request.ConceptKey))
            {
                Interlocked.Increment(ref DirectCashFlowRevision);
                return await GetDirectCashFlowMovementAsync("Manual",id.ToString(),ct);
            }
            if(duplicates.Any(x=>ReadString(x,"cr07a_movimientomanualbancoid")!=id.ToString()&&ReadString(x,"cr07a_descripcion")==request.Description.Trim()&&ReadString(x,"cr07a_referencia")==request.Reference.Trim()))
                throw new InvalidOperationException("Ya existe un registro manual con la misma fecha, cuenta, valor, descripción y referencia.");
        }
        await DirectWriteAsync(DirectManualSet,id,payload,request.Version,ct);
        Interlocked.Increment(ref DirectCashFlowRevision);
        return await GetDirectCashFlowMovementAsync("Manual",id.ToString(),ct);
    }
    public async Task VoidDirectCashFlowManualAsync(DirectCashFlowAssignmentRequest request,CancellationToken ct=default)
    {
        await ReadDirectSourceAsync("Manual",request.RecordId,ct);
        await DirectWriteAsync(DirectManualSet,Guid.Parse(request.RecordId),new Dictionary<string,object?>{["statecode"]=1,["statuscode"]=2},request.Version,ct,requireExisting:true);
        Interlocked.Increment(ref DirectCashFlowRevision);
    }
    public async Task SaveDirectCashFlowBalancesAsync(DirectCashFlowBalances request,CancellationToken ct=default)
    {
        DirectCashFlowReportBuilder.ValidatePeriod(request.Year,request.Month,request.Month);
        if(new[]{request.Opening,request.Cloud,request.Copiers,request.Pockets,request.Fiduciary}.Any(x=>x.HasValue&&(Math.Abs(x.Value)>100_000_000_000m||decimal.Round(x.Value,2)!=x.Value)))throw new InvalidOperationException("Revisa los saldos: máximo dos decimales y 100.000 millones.");
        var period=$"{request.Year:D4}-{request.Month:D2}";
        var id=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("direct-cash-balance:"+period)).AsSpan(0,16));
        await DirectWriteAsync(DirectBalanceSet,id,new Dictionary<string,object?>
        {
            ["cr07a_name"]=period,["cr07a_periodo"]=period,["cr07a_inicial"]=request.Opening,
            ["cr07a_cloud"]=request.Cloud,["cr07a_copiers"]=request.Copiers,["cr07a_bolsillos"]=request.Pockets,["cr07a_fidurenta"]=request.Fiduciary
        },request.Version,ct);
        Interlocked.Increment(ref DirectCashFlowRevision);
    }
    private async Task DirectWriteAsync(string set,Guid id,object payload,string version,CancellationToken ct,bool requireExisting=false)
    {
        if(requireExisting && string.IsNullOrWhiteSpace(version))throw new InvalidOperationException("Vuelve a abrir el registro para cargar su versión actual.");
        using var content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json");
        using var response=await CallDataverseAppResponseAsync($"/api/data/v9.2/{set}({id})","PATCH",ct,content,r=>
            r.Headers.TryAddWithoutValidation(string.IsNullOrEmpty(version)?"If-None-Match":"If-Match",string.IsNullOrEmpty(version)?"*":version));
        if(response.StatusCode==HttpStatusCode.PreconditionFailed)throw new InvalidOperationException("El registro ya fue guardado o cambió en otra sesión. Actualiza antes de continuar.");
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException("No fue posible guardar el registro del flujo de caja.");
    }
    public async Task<int> SuggestDirectCashFlowOctoberAsync(CancellationToken ct=default)
    {
        await DirectCashFlowSeedGate.WaitAsync(ct);
        try
        {
            const int year=2026;const int month=10;
            Interlocked.Increment(ref DirectCashFlowRevision);
            var data=await GetDirectCashFlowDataAsync(year,month,month,ct);
            var eligible=data.Movements.Where(x=>!x.IsInternalTransfer&&!x.IsManual&&string.IsNullOrEmpty(x.Version)).Select(x=>x.RecordId).ToHashSet();
            var source=await GetConciliacionCashFlowRowsAsync(new(year,month,1),new(year,11,1),ct);
            var count=0;
            foreach(var row in source.Where(x=>eligible.Contains(x.RecordId)))
            {
                var (concept,reason)=DirectCashFlowCatalog.Suggest(row);if(string.IsNullOrEmpty(concept))continue;
                await DirectWriteAsync(DirectAssignmentSet,Guid.Parse(row.RecordId),new Dictionary<string,object?>
                {
                    ["cr07a_name"]=$"{row.SourceKind} {row.RecordId}",["cr07a_origen"]=row.SourceKind,["cr07a_fecha"]=row.MovementDateValue,
                    ["cr07a_concepto"]=concept,["cr07a_estado"]="Sugerido",["cr07a_motivo"]=reason
                },"",ct);count++;
            }
            return count;
        }
        finally{Interlocked.Increment(ref DirectCashFlowRevision);DirectCashFlowSeedGate.Release();}
    }
}
