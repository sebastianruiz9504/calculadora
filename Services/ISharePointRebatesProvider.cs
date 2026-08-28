namespace CotizadorInterno.Web.Services;

public interface ISharePointRebatesProvider
{
    Task<SharePointRebatesSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}

public sealed record SharePointRebateRecord(
    string RecordId,
    DateOnly Date,
    decimal Value,
    int SourceRow);

public sealed record SharePointRebatesSnapshot(
    IReadOnlyList<SharePointRebateRecord> Records,
    string ETag,
    DateTimeOffset? LastModifiedUtc,
    bool IsStale,
    string Warning);
