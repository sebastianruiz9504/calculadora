using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;
using Microsoft.Extensions.Caching.Memory;

namespace CotizadorInterno.Web.Services.MonthClose;

public sealed class MonthCloseService(IMonthCloseRepository repository,IMonthCloseSiigoSource siigo,IMemoryCache cache,ILogger<MonthCloseService> logger,IConfiguration? configuration=null)
{
    private static readonly SemaphoreSlim CaptureGate=new(1,1);
    private static readonly SemaphoreSlim[] ReadGates=Enumerable.Range(0,32).Select(_=>new SemaphoreSlim(1,1)).ToArray();
    private async Task<T?> Cached<T>(string key,TimeSpan lifetime,Func<Task<T?>> read,CancellationToken ct) where T:class
    {
        if(cache.TryGetValue<T>(key,out var hit))return hit;
        var gate=ReadGates[(uint)StringComparer.Ordinal.GetHashCode(key)%ReadGates.Length];await gate.WaitAsync(ct);
        try
        {
            if(cache.TryGetValue<T>(key,out hit))return hit;
            var data=await read();if(data is not null)cache.Set(key,data,lifetime);return data;
        }
        finally{gate.Release();}
    }
    public async Task<IReadOnlyList<MonthCloseSummary>> ListAsync(int year,CancellationToken ct)
    {
        if(year is <MonthCloseEngine.FirstYear or >2100)throw new ArgumentOutOfRangeException(nameof(year));
        return await Cached($"month-close:index:{year}",TimeSpan.FromMinutes(5),async()=>await repository.ListAsync(year,ct),ct)??[];
    }
    public Task<MonthCloseSummary?> CurrentAsync(CancellationToken ct)
    {
        var period=MonthCloseEngine.BogotaToday(DateTimeOffset.UtcNow).ToString("yyyy-MM");
        return Cached($"month-close:live:{period}",TimeSpan.FromSeconds(20),()=>repository.ReadLiveSummaryAsync(period,ct),ct);
    }
    public Task<MonthCloseSnapshot?> ReadAsync(Guid id,CancellationToken ct)
    {
        var live=id==MonthCloseCodec.IdFor(MonthCloseEngine.BogotaToday(DateTimeOffset.UtcNow).ToString("yyyy-MM"),0);
        return Cached($"month-close:snapshot:{id}",live?TimeSpan.FromSeconds(20):TimeSpan.FromMinutes(30),()=>repository.ReadAsync(id,ct),ct);
    }
    private async Task<MonthCloseInput> SourcesAsync(CancellationToken ct)
    {
        var started=DateTimeOffset.UtcNow;
        var tables=repository.ReadSourcesAsync(ct);var documents=siigo.ReadMonthCloseDocumentsAsync(ct);
        await Task.WhenAll(tables,documents);
        var now=DateTimeOffset.UtcNow;
        if(MonthCloseEngine.BogotaToday(started).Month!=MonthCloseEngine.BogotaToday(now).Month)throw new InvalidOperationException("Cambió el mes durante la lectura. Se conservará la última observación completa y se reintentará.");
        return new(){Tables=await tables,Siigo=await documents,RetrievedAt=now,CostPolicy=configuration?.GetSection("MonthClose:Costs").Get<MonthCloseCostPolicy>()??new()};
    }
    public async Task RefreshLiveAsync(CancellationToken ct)
    {
        await CaptureGate.WaitAsync(ct);
        try
        {
            var input=await SourcesAsync(ct);var snapshot=MonthCloseEngine.BuildLive(input);
            await repository.EnrichAsync(snapshot,input,ct);await repository.SaveLiveAsync(snapshot,ct);
            cache.Remove($"month-close:live:{snapshot.Summary.Period}");cache.Remove($"month-close:snapshot:{snapshot.Summary.Id}");
            logger.LogInformation("Panel de cierre actualizado: {Period}, captura {CapturedAt}.",snapshot.Summary.Period,snapshot.Summary.CapturedAt);
        }
        finally{CaptureGate.Release();}
    }
    public async Task<YtdDashboardDto?> YtdAsync(Guid id,CancellationToken ct)
    {
        var selected=await ReadAsync(id,ct);if(selected is null)return null;
        var start=MonthCloseEngine.ParsePeriod(selected.Summary.Period);
        var summaries=(await ListAsync(start.Year,ct)).Where(s=>string.CompareOrdinal(s.Period,selected.Summary.Period)<0)
            .GroupBy(s=>s.Period).Select(g=>g.OrderByDescending(s=>s.Revision).First()).ToList();
        var snapshots=new List<MonthCloseSnapshot>();
        foreach(var batch in summaries.Chunk(3))
        {
            var items=await Task.WhenAll(batch.Select(s=>ReadAsync(s.Id,ct)));snapshots.AddRange(items.Where(s=>s is not null).Cast<MonthCloseSnapshot>());
        }
        snapshots.Add(selected);return MonthCloseYtd.Compose(selected.Summary.Period,snapshots);
    }
    public static MonthCloseSnapshot Freeze(MonthCloseSnapshot live,int revision,DateOnly today)
    {
        var end=MonthCloseEngine.ParsePeriod(live.Summary.Period).AddMonths(1);
        if(live.Summary.Mode!="live"||end>today)throw new InvalidOperationException("Solo se congela una observación de un mes terminado.");
        var frozen=JsonSerializer.Deserialize<MonthCloseSnapshot>(JsonSerializer.Serialize(live,MonthCloseCodec.Json),MonthCloseCodec.Json)!;
        frozen.Summary.Id=MonthCloseCodec.IdFor(frozen.Summary.Period,revision);frozen.Summary.Revision=revision;
        frozen.Summary.Mode="captured";frozen.Summary.Hash="";frozen.Summary.Cutoff=end.AddDays(-1).ToString("yyyy-MM-dd");
        frozen.Summary.Warnings.Add("Cierre inmutable a partir de la última observación completa: "+frozen.Summary.CapturedAt.ToOffset(TimeSpan.FromHours(-5)).ToString("yyyy-MM-dd HH:mm:ss 'Colombia'")+". No se consultan fuentes actuales para modificarlo.");
        return frozen;
    }
    public async Task<int> CaptureMissingAsync(int year,CancellationToken ct,string? revisePeriod=null,MonthCloseBankEvidence? bankEvidence=null)
    {
        if(year is <MonthCloseEngine.FirstYear or >2100)throw new ArgumentOutOfRangeException(nameof(year));
        if(bankEvidence is not null)MonthCloseBankImporter.Validate(bankEvidence);
        await CaptureGate.WaitAsync(ct);
        try
        {
            var today=MonthCloseEngine.BogotaToday(DateTimeOffset.UtcNow);var existing=await repository.ListAsync(year,ct);
            if(revisePeriod is not null&&existing.Any(s=>s.Period==revisePeriod&&s.Reports.Any(r=>r.Key=="profit-cloud")))throw new InvalidOperationException("Este cierre está congelado y no admite actualización ni nuevas revisiones.");
            var periods=Enumerable.Range(1,12).Select(m=>new DateOnly(year,m,1)).Where(m=>m.AddMonths(1)<=today)
                .Where(m=>revisePeriod is null?!existing.Any(s=>s.Period==m.ToString("yyyy-MM")):m.ToString("yyyy-MM")==revisePeriod).ToList();
            MonthCloseInput? input=null;var saved=0;
            foreach(var p in periods)
            {
                var period=p.ToString("yyyy-MM");var revision=existing.Where(x=>x.Period==period).Select(x=>x.Revision).DefaultIfEmpty(0).Max()+1;
                var live=await repository.ReadLiveAsync(period,ct);MonthCloseSnapshot snapshot;
                if(live is not null&&revisePeriod is null)snapshot=Freeze(live,revision,today);
                else
                {
                    input??=await SourcesAsync(ct);input.BankEvidence=bankEvidence;snapshot=MonthCloseEngine.Build(period,revision,input);await repository.EnrichAsync(snapshot,input,ct);
                }
                if(await SaveVerifiedAsync(snapshot,ct))saved++;
            }
            cache.Remove($"month-close:index:{year}");return saved;
        }
        finally{CaptureGate.Release();}
    }
    private async Task<bool> SaveVerifiedAsync(MonthCloseSnapshot snapshot,CancellationToken ct)
    {
        if(!await repository.CreateAsync(snapshot,ct))return false;
        var verified=await repository.ReadAsync(snapshot.Summary.Id,ct);
        if(verified?.Summary.Hash!=snapshot.Summary.Hash)throw new InvalidOperationException("No se pudo verificar el cierre persistido.");return true;
    }
    // One-time deployment migration: append analytical sections, preserving every
    // existing financial report and original version. Subsequent calls do nothing.
    public async Task<int> UpgradeDashboardAsync(bool apply,string output,CancellationToken ct)
    {
        await CaptureGate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(output);
            var replay=configuration?["MonthClose:ReplayPath"];
            if(apply&&!string.IsNullOrEmpty(replay))throw new InvalidOperationException("La publicación requiere fuentes actuales.");
            var input=string.IsNullOrEmpty(replay)?await SourcesAsync(ct):JsonSerializer.Deserialize<MonthCloseInput>(await File.ReadAllTextAsync(replay,ct),MonthCloseCodec.Json)!;
            await File.WriteAllTextAsync(output+"-source.json",JsonSerializer.Serialize(input,MonthCloseCodec.Json),ct);
            var count=0;
            for(var year=MonthCloseEngine.FirstYear;year<=MonthCloseEngine.BogotaToday(input.RetrievedAt).Year;year++)
            {
                var existing=await repository.ListAsync(year,ct);
                foreach(var summary in existing.GroupBy(s=>s.Period).Select(g=>g.OrderByDescending(s=>s.Revision).First()).Where(s=>!s.Reports.Any(r=>r.Key=="profit-cloud")))
                {
                    var original=await repository.ReadAsync(summary.Id,ct)??throw new InvalidDataException("Cierre original no disponible.");
                    var snapshot=JsonSerializer.Deserialize<MonthCloseSnapshot>(JsonSerializer.Serialize(original,MonthCloseCodec.Json),MonthCloseCodec.Json)!;
                    var start=MonthCloseEngine.ParsePeriod(summary.Period);
                    MonthCloseEngine.AddProfitability(snapshot,input,start,start.AddMonths(1),false);await repository.EnrichAsync(snapshot,input,ct);
                    snapshot.Summary.Revision=summary.Revision+1;snapshot.Summary.CapturedAt=input.RetrievedAt;snapshot.Summary.Mode="reconstructed";
                    snapshot.Summary.Warnings.Add("Se añadieron utilidad y YTD con evidencia disponible el "+input.RetrievedAt.ToString("yyyy-MM-dd")+"; los reportes financieros originales y la versión anterior se conservaron.");
                    snapshot.SourceHashes["original-close"]=original.Summary.Hash;snapshot.Summary.Id=MonthCloseCodec.IdFor(summary.Period,snapshot.Summary.Revision);
                    var (_,hash)=MonthCloseCodec.Encode(snapshot);snapshot.Summary.Hash=hash;
                    await File.WriteAllTextAsync(Path.Combine(output,summary.Period+".json"),JsonSerializer.Serialize(snapshot,MonthCloseCodec.Json),ct);
                    if(apply)
                    {
                        var latest=(await repository.ListAsync(year,ct)).Where(s=>s.Period==summary.Period).MaxBy(s=>s.Revision);
                        if(latest?.Id!=summary.Id||latest.Hash!=original.Summary.Hash)throw new InvalidOperationException("Cambió un cierre durante la preparación; se detuvo la publicación.");
                        if(await SaveVerifiedAsync(snapshot,ct))count++;
                    }
                    else count++;
                }
                cache.Remove($"month-close:index:{year}");
            }
            var current=MonthCloseEngine.BuildLive(input);await repository.EnrichAsync(current,input,ct);
            current.Summary.Id=MonthCloseCodec.IdFor(current.Summary.Period,0);
            var (_,currentHash)=MonthCloseCodec.Encode(current);current.Summary.Hash=currentHash;
            await File.WriteAllTextAsync(Path.Combine(output,"live.json"),JsonSerializer.Serialize(current,MonthCloseCodec.Json),ct);
            if(apply)await repository.SaveLiveAsync(current,ct);return count;
        }
        finally{CaptureGate.Release();}
    }
}
