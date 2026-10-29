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
        var rows=await GetDataverseAppEntitiesAsync($"/api/data/v9.2/{MonthCloseSet}?$select=cr07a_resumen&$filter=cr07a_anio eq {year}&$orderby=cr07a_periodo asc,cr07a_revision desc",ct);
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
            ("billing","cr07a_facturacions","cr07a_facturacionid,cr07a_name,cr07a_cliente,cr07a_nitempresa,cr07a_vertical,cr07a_fechadeemision,cr07a_fechadepago,cr07a_valorpago,cr07a_totalfactura,cr07a_facturaantesdeiva,cr07a_ivavalor,cr07a_rteftevalor,cr07a_rteivavalor,cr07a_reteicavalor"),
            ("clients","cr07a_clientes","cr07a_nit,cr07a_nombre"),
            ("expenses","cr07a_gastodelaempresas","cr07a_gastodelaempresaid,cr07a_folio,cr07a_prefijo,cr07a_nombreemisor,cr07a_nitemisor,cr07a_fechaemision,cr07a_fechadepago,cr07a_valorpago,cr07a_total,cr07a_totalantesdeiva,cr07a_iva,cr07a_retefuente,cr07a_reteica,cr07a_reteiva,cr07a_divisa,cr07a_tipodocumento"),
            ("cloud","cr07a_salesperformancerecords","cr07a_clientname,cr07a_productname,cr07a_valorventatotalmensual,cr07a_fecharenovacion,statecode"),
            ("inventory","cr07a_suministros","cr07a_nombredelsuministro,cr07a_cantidad,statecode"),
            ("assets","cr07a_equipos","cr07a_nombredelequipo,cr07a_serial,cr07a_fechadecompra,statecode"),
            ("employees","cr07a_empleados","cr07a_nombrecompleto,cr07a_tipocontrato,cr07a_fechadeingreso,cr07a_fechadesalida,cr07a_diastomadoscalculados,cr07a_diasdisponiblescalculados,cr07a_sueldomensual,statecode")};
        var result=new Dictionary<string,List<JsonElement>>();
        foreach(var source in sources)
            result[source.Key]=await GetDataverseAppEntitiesAsync($"/api/data/v9.2/{source.Table}?$select={source.Fields}",ct);
        return result;
    }
}
