using CotizadorInterno.Web.Models.Dashboard;
using Microsoft.Extensions.Caching.Memory;

namespace CotizadorInterno.Web.Services.MonthClose;

public sealed class MonthCloseService(IMonthCloseRepository repository,IMonthCloseSiigoSource siigo,IMemoryCache cache,ILogger<MonthCloseService> logger)
{
    private static readonly SemaphoreSlim CaptureGate=new(1,1);
    private static readonly SemaphoreSlim ReadGate=new(1,1);
    public async Task<IReadOnlyList<MonthCloseSummary>> ListAsync(int year,CancellationToken ct)
    {
        if(year is <2026 or >2100)throw new ArgumentOutOfRangeException(nameof(year));
        var key=$"month-close:index:{year}";
        if(cache.TryGetValue<IReadOnlyList<MonthCloseSummary>>(key,out var hit))return hit!;
        await ReadGate.WaitAsync(ct);
        try
        {
            if(cache.TryGetValue<IReadOnlyList<MonthCloseSummary>>(key,out hit))return hit!;
            var data=await repository.ListAsync(year,ct);
            return cache.Set(key,data,TimeSpan.FromMinutes(5));
        }
        finally{ReadGate.Release();}
    }
    public async Task<MonthCloseSnapshot?> ReadAsync(Guid id,CancellationToken ct)
    {
        var key=$"month-close:snapshot:{id}";
        if(cache.TryGetValue<MonthCloseSnapshot>(key,out var hit))return hit;
        await ReadGate.WaitAsync(ct);
        try
        {
            if(cache.TryGetValue<MonthCloseSnapshot>(key,out hit))return hit;
            var data=await repository.ReadAsync(id,ct);
            if(data is not null)cache.Set(key,data,TimeSpan.FromMinutes(30));
            return data;
        }
        finally{ReadGate.Release();}
    }
    public async Task<int> CaptureMissingAsync(int year,CancellationToken ct,string? revisePeriod=null)
    {
        await CaptureGate.WaitAsync(ct);
        try
        {
            var today=MonthCloseEngine.BogotaToday(DateTimeOffset.UtcNow);
            var existing=await repository.ListAsync(year,ct);
            var periods=Enumerable.Range(1,12).Select(m=>new DateOnly(year,m,1)).Where(m=>m.AddMonths(1)<=today)
                .Where(m=>revisePeriod is null?!existing.Any(s=>s.Period==m.ToString("yyyy-MM")):m.ToString("yyyy-MM")==revisePeriod).ToList();
            if(periods.Count==0)return 0;
            logger.LogInformation("Preparando {Count} cierres mensuales de {Year}.",periods.Count,year);
            var input=new MonthCloseInput{Tables=await repository.ReadSourcesAsync(ct),Siigo=await siigo.ReadMonthCloseDocumentsAsync(ct),RetrievedAt=DateTimeOffset.UtcNow};
            var saved=0;
            foreach(var p in periods)
            {
                var period=p.ToString("yyyy-MM");
                var revision=existing.Where(x=>x.Period==period).Select(x=>x.Revision).DefaultIfEmpty(0).Max()+1;
                var snapshot=MonthCloseEngine.Build(period,revision,input);
                if(await repository.CreateAsync(snapshot,ct))
                {
                    var verified=await repository.ReadAsync(snapshot.Summary.Id,ct);
                    if(verified?.Summary.Hash!=snapshot.Summary.Hash)throw new InvalidOperationException("No se pudo verificar el cierre persistido.");
                    saved++;logger.LogInformation("Cierre {Period} v{Revision} guardado y verificado.",period,revision);
                }
            }
            cache.Remove($"month-close:index:{year}");return saved;
        }
        finally{CaptureGate.Release();}
    }
}
