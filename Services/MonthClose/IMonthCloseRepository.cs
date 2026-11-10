using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services.MonthClose;

public interface IMonthCloseRepository
{
    Task<IReadOnlyList<MonthCloseSummary>> ListAsync(int year,CancellationToken ct);
    Task<MonthCloseSnapshot?> ReadAsync(Guid id,CancellationToken ct);
    Task<bool> CreateAsync(MonthCloseSnapshot snapshot,CancellationToken ct);
    Task<Dictionary<string,List<JsonElement>>> ReadSourcesAsync(CancellationToken ct);
    Task<MonthCloseSnapshot?> ReadLiveAsync(string period,CancellationToken ct) => Task.FromResult<MonthCloseSnapshot?>(null);
    Task<MonthCloseSummary?> ReadLiveSummaryAsync(string period,CancellationToken ct) => Task.FromResult<MonthCloseSummary?>(null);
    Task SaveLiveAsync(MonthCloseSnapshot snapshot,CancellationToken ct) => throw new NotSupportedException();
    Task EnrichAsync(MonthCloseSnapshot snapshot,MonthCloseInput input,CancellationToken ct) => Task.CompletedTask;
}
public interface IMonthCloseSiigoSource
{
    Task<Dictionary<string,List<JsonElement>>> ReadMonthCloseDocumentsAsync(CancellationToken ct);
}
