using System.Net;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Services.MonthClose;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService : IMonthCloseRepository
{
    private const string MonthCloseSet="cr07a_cierremensuals";
    async Task<IReadOnlyList<MonthCloseSummary>> IMonthCloseRepository.ListAsync(int year,CancellationToken ct)
    {
        if(year is <MonthCloseEngine.FirstYear or >2100)throw new ArgumentOutOfRangeException(nameof(year));
        var rows=await GetDataverseAppEntitiesAsync($"/api/data/v9.2/{MonthCloseSet}?$select=cr07a_resumen&$filter=cr07a_anio eq {year} and cr07a_metodo ne 'live'&$orderby=cr07a_periodo asc,cr07a_revision desc",ct);
        return rows.Select(x=>JsonSerializer.Deserialize<MonthCloseSummary>(MonthCloseEngine.S(x,"cr07a_resumen"),MonthCloseCodec.Json)!).ToList();
    }
    async Task<MonthCloseSnapshot?> IMonthCloseRepository.ReadAsync(Guid id,CancellationToken ct)
    {
        using var response=await CallDataverseAppResponseAsync($"/api/data/v9.2/{MonthCloseSet}({id:D})?$select=cr07a_contenido,cr07a_hash","GET",ct);
        if(response.StatusCode==HttpStatusCode.NotFound)return null;
        response.EnsureSuccessStatusCode();
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return MonthCloseCodec.Decode(MonthCloseEngine.S(json.RootElement,"cr07a_contenido"),MonthCloseEngine.S(json.RootElement,"cr07a_hash"));
    }
    async Task<bool> IMonthCloseRepository.CreateAsync(MonthCloseSnapshot snapshot,CancellationToken ct)
    {
        var s=snapshot.Summary;s.Id=MonthCloseCodec.IdFor(s.Period,s.Revision);
        var (body,hash)=MonthCloseCodec.Encode(snapshot);s.Hash=hash;
        var payload=new Dictionary<string,object?>{
            ["cr07a_name"]=$"Cierre {s.Period} · v{s.Revision}",["cr07a_periodo"]=s.Period,["cr07a_anio"]=MonthCloseEngine.ParsePeriod(s.Period).Year,
            ["cr07a_revision"]=s.Revision,["cr07a_capturado"]=s.CapturedAt,["cr07a_metodo"]=s.Mode,["cr07a_hash"]=hash,
            ["cr07a_resumen"]=JsonSerializer.Serialize(s,MonthCloseCodec.Json),["cr07a_contenido"]=body};
        using var content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json");
        // Atomic create-only upsert. The deterministic ID makes retries and concurrent
        // workers idempotent. Existing versions can never be overwritten by this path.
        using var response=await CallDataverseAppResponseAsync($"/api/data/v9.2/{MonthCloseSet}({s.Id:D})","PATCH",ct,content,
            request=>request.Headers.TryAddWithoutValidation("If-None-Match","*"));
        if(response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)return false;
        response.EnsureSuccessStatusCode();return true;
    }
    async Task<Dictionary<string,List<JsonElement>>> IMonthCloseRepository.ReadSourcesAsync(CancellationToken ct)
    {
        var sources=new (string Key,string Table,string Fields)[]{
            ("billing","cr07a_facturacions","cr07a_facturacionid,cr07a_name,cr07a_cliente,cr07a_nitempresa,cr07a_vertical,cr07a_tipocontrato,cr07a_fechadeemision,cr07a_fechadepago,cr07a_valorpago,cr07a_totalfactura,cr07a_facturaantesdeiva,cr07a_ivavalor,cr07a_rteftevalor,cr07a_rteivavalor,cr07a_reteicavalor"),
            ("clients","cr07a_clientes","cr07a_clienteid,cr07a_nit,cr07a_nombre,cr07a_ciudad"),
            ("expenses","cr07a_gastodelaempresas","cr07a_gastodelaempresaid,cr07a_folio,cr07a_prefijo,cr07a_nombreemisor,cr07a_nitemisor,cr07a_fechaemision,cr07a_fechadepago,cr07a_valorpago,cr07a_total,cr07a_totalantesdeiva,cr07a_iva,cr07a_retefuente,cr07a_reteica,cr07a_reteiva,cr07a_divisa,cr07a_tipodocumento,cr07a_categoria,cr07a_cloud,cr07a_copiers,cr07a_nombrereceptor"),
            ("cloud","cr07a_salesperformancerecords","cr07a_salesperformancerecordid,_cr07a_clientelookup_value,cr07a_clientname,cr07a_productname,cr07a_quantity,cr07a_valorventaunidadusd,cr07a_valorventatotalmensual,cr07a_fecharenovacion,statecode"),
            ("accounts","cr07a_accountidicps","cr07a_accountidicpid,cr07a_name,_cr07a_cliente_value,cr07a_grupoempresarialid,cr07a_grupoempresarialname,statecode"),
            ("account-maps","cr07a_licenciamientoaccountmaps","cr07a_licenciamientoaccountmapid,cr07a_active,cr07a_sourceaccountid,cr07a_sourceaccountname,cr07a_targetaccountid,cr07a_targetclientid,cr07a_targetclientname"),
            ("licensing","cr07a_consumointcomexes","cr07a_consumointcomexid,cr07a_name,cr07a_nombrecliente,_cr07a_accountid_value,cr07a_factura,cr07a_mesconsumo,cr07a_pesostotal,cr07a_valortotalusd,cr07a_trm,cr07a_tipocontrato,cr07a_productocorto"),
            ("supply-purchases","cr07a_facturasproveedorescopierses","cr07a_facturasproveedorescopiersid,cr07a_name,_cr07a_suministro_value,cr07a_cantidad,cr07a_valorunitarioantesdeiva,cr07a_aprobadoeingresado,createdon"),
            ("deliveries","cr07a_entregas","cr07a_entregaid,cr07a_entrega1,_cr07a_iddecliente_value,_cr07a_iddesuministro_value,cr07a_fechadeentrega,cr07a_cantidadentregada,cr07a_estadodeentrega"),
            ("maintenance","dtc_copiersmtov2s","dtc_copiersmtov2id,dtc_reference,_dtc_client_value,dtc_clientnamesnapshot,dtc_servicedate,dtc_workflowstate,dtc_businessstatus,dtc_formversion,dtc_legacysourcekey,dtc_serviceaddressinternal"),
            ("inventory","cr07a_suministros","cr07a_nombredelsuministro,cr07a_cantidad,statecode"),
            ("assets","cr07a_equipos","cr07a_nombredelequipo,cr07a_serial,cr07a_fechadecompra,statecode"),
            ("employees","cr07a_empleados","cr07a_nombrecompleto,cr07a_tipocontrato,cr07a_fechadeingreso,cr07a_fechadesalida,cr07a_diastomadoscalculados,cr07a_diasdisponiblescalculados,cr07a_sueldomensual,statecode")};
        var result=new Dictionary<string,List<JsonElement>>();
        // Bounded parallel reads; every source must finish before publishing a new observation.
        foreach(var batch in sources.Chunk(3))
        {
            var rows=await Task.WhenAll(batch.Select(async source=>(source.Key,Rows:await GetDataverseAppEntitiesAsync($"/api/data/v9.2/{source.Table}?$select={source.Fields}",ct,AddFormattedValueHeaders))));
            foreach(var row in rows)result[row.Key]=row.Rows;
        }
        var rebates=await _sharePointRebatesProvider.GetSnapshotAsync(ct);
        result["rebates"]=rebates.Records.Select(r=>JsonSerializer.SerializeToElement(r,MonthCloseCodec.Json)).ToList();
        if(!string.IsNullOrWhiteSpace(rebates.Warning))result["rebates-warning"]=[JsonSerializer.SerializeToElement(new{message=rebates.Warning})];
        return result;
    }

    Task<MonthCloseSnapshot?> IMonthCloseRepository.ReadLiveAsync(string period,CancellationToken ct)
        => ((IMonthCloseRepository)this).ReadAsync(MonthCloseCodec.IdFor(period,0),ct);

    async Task<MonthCloseSummary?> IMonthCloseRepository.ReadLiveSummaryAsync(string period,CancellationToken ct)
    {
        var id=MonthCloseCodec.IdFor(period,0);
        using var response=await CallDataverseAppResponseAsync($"/api/data/v9.2/{MonthCloseSet}({id:D})?$select=cr07a_resumen","GET",ct);
        if(response.StatusCode==HttpStatusCode.NotFound)return null;
        response.EnsureSuccessStatusCode();
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return JsonSerializer.Deserialize<MonthCloseSummary>(MonthCloseEngine.S(json.RootElement,"cr07a_resumen"),MonthCloseCodec.Json);
    }

    async Task IMonthCloseRepository.SaveLiveAsync(MonthCloseSnapshot snapshot,CancellationToken ct)
    {
        var s=snapshot.Summary;
        if(s.Mode!="live"||s.Period!=MonthCloseEngine.BogotaToday(DateTimeOffset.UtcNow).ToString("yyyy-MM"))
            throw new InvalidOperationException("Solo se actualiza la observación del mes abierto.");
        s.Id=MonthCloseCodec.IdFor(s.Period,0);
        // The live row has its own deterministic ID. Closed versions use revision IDs > 0.
        using var existing=await CallDataverseAppResponseAsync($"/api/data/v9.2/{MonthCloseSet}({s.Id:D})?$select=cr07a_metodo,cr07a_capturado","GET",ct);
        string? etag=null;
        if(existing.StatusCode!=HttpStatusCode.NotFound)
        {
            existing.EnsureSuccessStatusCode();using var doc=JsonDocument.Parse(await existing.Content.ReadAsStringAsync(ct));
            if(MonthCloseEngine.S(doc.RootElement,"cr07a_metodo")!="live")throw new InvalidOperationException("Un cierre guardado no se puede actualizar.");
            if(DateTimeOffset.TryParse(MonthCloseEngine.S(doc.RootElement,"cr07a_capturado"),out var captured)&&captured>=s.CapturedAt)return;
            etag=MonthCloseEngine.S(doc.RootElement,"@odata.etag");
            if(string.IsNullOrWhiteSpace(etag))throw new InvalidOperationException("Falta versión para actualizar la observación.");
        }
        var (body,hash)=MonthCloseCodec.Encode(snapshot);s.Hash=hash;
        var payload=new Dictionary<string,object?>{["cr07a_name"]=$"Mes en curso {s.Period}",["cr07a_periodo"]=s.Period,["cr07a_anio"]=MonthCloseEngine.ParsePeriod(s.Period).Year,["cr07a_revision"]=1,
            ["cr07a_capturado"]=s.CapturedAt,["cr07a_metodo"]="live",["cr07a_hash"]=hash,["cr07a_resumen"]=JsonSerializer.Serialize(s,MonthCloseCodec.Json),["cr07a_contenido"]=body};
        using var content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json");
        if(s.Period!=MonthCloseEngine.BogotaToday(DateTimeOffset.UtcNow).ToString("yyyy-MM"))
            throw new InvalidOperationException("El mes terminó durante la lectura; se conserva la última captura completa.");
        using var response=await CallDataverseAppResponseAsync($"/api/data/v9.2/{MonthCloseSet}({s.Id:D})","PATCH",ct,content,
            r=>r.Headers.TryAddWithoutValidation(etag is null?"If-None-Match":"If-Match",etag??"*"));
        if(response.StatusCode==HttpStatusCode.PreconditionFailed)return;
        response.EnsureSuccessStatusCode();
    }
}
