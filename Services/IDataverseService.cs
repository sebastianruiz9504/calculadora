using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Services;

public interface IDataverseService
{
    Task<UserSegment> GetCurrentUserSegmentAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ProductLookupItem>> SearchProductsAsync(string query, int top = 12, CancellationToken ct = default);
}
