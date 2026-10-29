using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services.MonthClose;

public interface IMonthCloseRepository
{
    Task<IReadOnlyList<MonthCloseSummary>> ListAsync(int year,CancellationToken ct);
    Task<MonthCloseSnapshot?> ReadAsync(Guid id,CancellationToken ct);
    Task<bool> CreateAsync(MonthCloseSnapshot snapshot,CancellationToken ct);
    Task<Dictionary<string,List<JsonElement>>> ReadSourcesAsync(CancellationToken ct);
}
public interface IMonthCloseSiigoSource
{
    Task<Dictionary<string,List<JsonElement>>> ReadMonthCloseDocumentsAsync(CancellationToken ct);
}
