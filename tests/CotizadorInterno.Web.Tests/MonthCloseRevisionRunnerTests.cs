using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Services.MonthClose;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class MonthCloseRevisionRunnerTests
{
    [Fact] public async Task ApplyUsesFrozenPlanAndRetryDoesNotCreateMoreVersions()
    {
        using var files = new Files(); var repo = new Repository();
        await MonthCloseReceivableRevisionRunner.RunAsync(repo, files.Source, files.Root, false, default);
        Assert.Equal(0, repo.Writes);
        await MonthCloseReceivableRevisionRunner.RunAsync(repo, null, files.Root, true, default);
        Assert.Equal(12, repo.Writes); Assert.Equal(24, repo.Stored.Count);
        await MonthCloseReceivableRevisionRunner.RunAsync(repo, null, files.Root, true, default);
        Assert.Equal(12, repo.Writes);
    }
    [Fact] public async Task TamperedLastProposalStopsBeforeAnyWrite()
    {
        using var files = new Files(); var repo = new Repository();
        await MonthCloseReceivableRevisionRunner.RunAsync(repo, files.Source, files.Root, false, default);
        var path = Path.Combine(files.Root, "2025-12.json");
        var s = JsonSerializer.Deserialize<MonthCloseSnapshot>(await File.ReadAllTextAsync(path), MonthCloseCodec.Json)!;
        s.Reports[0].Total = 999;
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(s, MonthCloseCodec.Json));
        await Assert.ThrowsAsync<InvalidDataException>(() => MonthCloseReceivableRevisionRunner.RunAsync(repo, null, files.Root, true, default));
        Assert.Equal(0, repo.Writes);
    }
    [Fact] public async Task ConcurrentRevisionStopsBeforeAnyWrite()
    {
        using var files = new Files(); var repo = new Repository();
        await MonthCloseReceivableRevisionRunner.RunAsync(repo, files.Source, files.Root, false, default);
        var s = Repository.Snapshot("2025-12", 3);
        repo.Stored[s.Summary.Id] = s;
        await Assert.ThrowsAsync<InvalidDataException>(() => MonthCloseReceivableRevisionRunner.RunAsync(repo, null, files.Root, true, default));
        Assert.Equal(0, repo.Writes);
    }
    private sealed class Files : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "month-close-policy-tests-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "source.json");
        public Files()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Source, JsonSerializer.Serialize(new MonthCloseInput { RetrievedAt = DateTimeOffset.Parse("2026-10-29T12:00:00Z") }, MonthCloseCodec.Json));
        }
        public void Dispose()
        {
            foreach (var file in Directory.EnumerateFiles(Root)) File.Delete(file);
            Directory.Delete(Root);
        }
    }
    private sealed class Repository : IMonthCloseRepository
    {
        public readonly Dictionary<Guid, MonthCloseSnapshot> Stored = [];
        public int Writes;
        public Repository()
        {
            foreach (var month in Enumerable.Range(1, 12))
            {
                var s = Snapshot($"2025-{month:00}", 2); Stored[s.Summary.Id] = s;
            }
        }
        public static MonthCloseSnapshot Snapshot(string period, int revision)
        {
            var s = MonthCloseEngine.Build(period, revision, new() { RetrievedAt = DateTimeOffset.Parse("2026-10-29T12:00:00Z") });
            s.Summary.Id = MonthCloseCodec.IdFor(period, revision);
            var (_, hash) = MonthCloseCodec.Encode(s); s.Summary.Hash = hash; return s;
        }
        public Task<IReadOnlyList<MonthCloseSummary>> ListAsync(int year, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MonthCloseSummary>>(Stored.Values.Select(s => s.Summary).ToList());
        public Task<MonthCloseSnapshot?> ReadAsync(Guid id, CancellationToken ct) => Task.FromResult(Stored.GetValueOrDefault(id));
        public Task<bool> CreateAsync(MonthCloseSnapshot s, CancellationToken ct)
        {
            s.Summary.Id = MonthCloseCodec.IdFor(s.Summary.Period, s.Summary.Revision);
            var (_, hash) = MonthCloseCodec.Encode(s); s.Summary.Hash = hash;
            if (Stored.ContainsKey(s.Summary.Id)) return Task.FromResult(false);
            Stored[s.Summary.Id] = s; Writes++; return Task.FromResult(true);
        }
        public Task<Dictionary<string, List<JsonElement>>> ReadSourcesAsync(CancellationToken ct) => throw new NotSupportedException("Revision never reads mutable operational sources.");
    }
}
