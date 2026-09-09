using CotizadorInterno.Web.Services.SoporteCloud;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class SharedLiveSurveyStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CotizadorInterno.LiveSurveyStore.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task IndependentWorkersAndRestartRestoreTheSameQuestionAndParticipantState()
    {
        var administrator = CreateStore();
        var participant = CreateStore();
        await administrator.MutateAsync<SurveyState, bool>("code-ABC123", _ => new(new SurveyState
        {
            Question = 2,
            Answers = new() { ["participant-1"] = 3 }
        }, true));

        var observed = await participant.ReadAsync<SurveyState>("code-abc123");
        Assert.Equal(2, observed!.Question);
        Assert.Equal(3, observed.Answers["participant-1"]);

        await participant.MutateAsync<SurveyState, bool>("code-ABC123", current =>
        {
            current!.Answers["participant-2"] = 1;
            return new(current, true);
        });

        // A newly constructed store has no process-local state to restore from.
        var restarted = await CreateStore().ReadAsync<SurveyState>("code-ABC123");
        Assert.Equal(2, restarted!.Question);
        Assert.Equal(2, restarted.Answers.Count);
    }

    [Fact]
    public async Task ParallelWorkersDoNotLoseParticipantUpdates()
    {
        var workers = Enumerable.Range(0, 8).Select(_ => CreateStore()).ToArray();
        await Task.WhenAll(workers.Select(async (worker, index) =>
        {
            for (var answer = 0; answer < 12; answer++)
            {
                var participantKey = $"worker-{index}-answer-{answer}";
                await worker.MutateAsync<SurveyState, int>("code-parallel", current =>
                {
                    current ??= new SurveyState();
                    current.Answers.Add(participantKey, answer);
                    current.Question++;
                    return new(current, current.Question);
                });
            }
        }));

        var state = await CreateStore().ReadAsync<SurveyState>("code-parallel");
        Assert.Equal(96, state!.Question);
        Assert.Equal(96, state.Answers.Count);
    }

    [Fact]
    public async Task CachedReadsAreDetachedAndRefreshAcrossWorkersWithinTheCacheBound()
    {
        var writer = CreateStore();
        var reader = new SharedLiveSurveyStore(_directory, readCacheDuration: TimeSpan.FromMilliseconds(150));
        await writer.MutateAsync<SurveyState, bool>("code-cache", _ => new(new SurveyState { Question = 1 }, true));
        var first = await reader.ReadAsync<SurveyState>("code-cache");
        first!.Question = 99;
        first.Answers.Add("not-persisted", 99);
        var second = await reader.ReadAsync<SurveyState>("code-cache");
        Assert.Equal(1, second!.Question);
        Assert.Empty(second.Answers);

        await writer.MutateAsync<SurveyState, bool>("code-cache", current =>
        {
            current!.Question = 2;
            return new(current, true);
        });
        await Task.Delay(210);
        Assert.Equal(2, (await reader.ReadAsync<SurveyState>("code-cache"))!.Question);
    }

    [Fact]
    public async Task MutationBypassesAnOlderReadCache()
    {
        var first = new SharedLiveSurveyStore(_directory);
        var second = new SharedLiveSurveyStore(_directory);
        await first.MutateAsync<SurveyState, bool>("code-fresh", _ => new(new SurveyState { Question = 1 }, true));
        await first.ReadAsync<SurveyState>("code-fresh");
        await second.MutateAsync<SurveyState, bool>("code-fresh", current =>
        {
            current!.Question++;
            return new(current, true);
        });
        var result = await first.MutateAsync<SurveyState, int>("code-fresh", current =>
        {
            current!.Question++;
            return new(current, current.Question);
        });
        Assert.Equal(3, result);
    }

    [Fact]
    public async Task LockedDocumentCanBeCancelledAndDoesNotBlockOtherSessions()
    {
        Directory.CreateDirectory(_directory);
        await using var heldLock = new FileStream(Path.Combine(_directory, "code-held.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        var worker = CreateStore();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            worker.MutateAsync<SurveyState, bool>("code-held", _ => new(new SurveyState(), true), cancellation.Token));

        Assert.True(await worker.MutateAsync<SurveyState, bool>("code-free", _ => new(new SurveyState(), true)));
        Assert.False(File.Exists(Path.Combine(_directory, "code-held.json")));
    }

    [Fact]
    public async Task LockWaitHasABoundAndReturnsASafeAvailabilityError()
    {
        Directory.CreateDirectory(_directory);
        await using var heldLock = new FileStream(Path.Combine(_directory, "code-held.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        var worker = new SharedLiveSurveyStore(_directory, lockTimeout: TimeSpan.FromMilliseconds(60));
        var error = await Assert.ThrowsAsync<SharedLiveSurveyStoreUnavailableException>(() =>
            worker.MutateAsync<SurveyState, bool>("code-held", _ => new(new SurveyState(), true)));

        Assert.IsType<TimeoutException>(error.InnerException);
        Assert.DoesNotContain(_directory, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_directory, "code-held.json")));
    }

    [Fact]
    public async Task ReadWaitsForAnInProgressCommitInsteadOfReportingMissingState()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "code-pending.json");
        var heldLock = new FileStream(Path.Combine(_directory, "code-pending.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        var reading = CreateStore().ReadAsync<SurveyState>("code-pending");
        try
        {
            await Task.Delay(50);
            Assert.False(reading.IsCompleted);
            await File.WriteAllTextAsync(path, "{\"question\":2,\"answers\":{}}");
        }
        finally { await heldLock.DisposeAsync(); }

        Assert.Equal(2, (await reading)!.Question);
    }

    [Fact]
    public async Task CorruptionDoesNotBecomeAMissingSessionOrGetOverwritten()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "code-corrupt.json");
        await File.WriteAllTextAsync(path, "{partial");
        var worker = CreateStore();
        await Assert.ThrowsAsync<SharedLiveSurveyStoreUnavailableException>(() => worker.ReadAsync<SurveyState>("code-corrupt"));
        var callbackCalled = false;
        await Assert.ThrowsAsync<SharedLiveSurveyStoreUnavailableException>(() =>
            worker.MutateAsync<SurveyState, bool>("code-corrupt", _ =>
            {
                callbackCalled = true;
                return new(new SurveyState(), true);
            }));
        Assert.False(callbackCalled);
        Assert.Equal("{partial", await File.ReadAllTextAsync(path));
        Assert.Null(await worker.ReadAsync<SurveyState>("code-missing"));
    }

    [Fact]
    public async Task FailedSerializationPreservesLastCommittedStateAndReleasesLock()
    {
        var worker = CreateStore();
        await worker.MutateAsync<GraphState, bool>("code-atomic", _ => new(new GraphState { Label = "committed" }, true));
        await Assert.ThrowsAsync<SharedLiveSurveyStoreUnavailableException>(() =>
            worker.MutateAsync<GraphState, bool>("code-atomic", current =>
            {
                current!.Label = "uncommitted";
                current.Child = current;
                return new(current, true);
            }));

        var restarted = CreateStore();
        Assert.Equal("committed", (await restarted.ReadAsync<GraphState>("code-atomic"))!.Label);
        Assert.True(await restarted.MutateAsync<GraphState, bool>("code-atomic", current => new(current, true)));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentReadersOnlyObserveCompleteCommittedDocuments()
    {
        var writer = CreateStore();
        var reader = CreateStore();
        await writer.MutateAsync<SurveyState, bool>("code-atomic-read", _ => new(new SurveyState(), true));
        var writing = Task.Run(async () =>
        {
            for (var i = 1; i <= 30; i++)
            {
                await writer.MutateAsync<SurveyState, bool>("code-atomic-read", current =>
                {
                    current!.Question = i;
                    current.Answers["version"] = i;
                    return new(current, true);
                });
            }
        });

        try
        {
            for (var read = 0; read < 60; read++)
            {
                var observed = await reader.ReadAsync<SurveyState>("code-atomic-read");
                Assert.NotNull(observed);
                Assert.Equal(observed.Question, observed.Answers.GetValueOrDefault("version"));
            }
        }
        finally { await writing; }
        Assert.Equal(30, (await reader.ReadAsync<SurveyState>("code-atomic-read"))!.Question);
    }

    [Fact]
    public async Task NonPersistingMutationDoesNotCreateOrModifyADocument()
    {
        var worker = CreateStore();
        Assert.Equal("missing", await worker.MutateAsync<SurveyState, string>("code-nochange", _ => new(null, "missing", Persist: false)));
        Assert.Null(await worker.ReadAsync<SurveyState>("code-nochange"));
        await worker.MutateAsync<SurveyState, bool>("code-nochange", _ => new(new SurveyState { Question = 1 }, true));
        await worker.MutateAsync<SurveyState, bool>("code-nochange", current =>
        {
            current!.Question = 99;
            return new(current, false, Persist: false);
        });
        Assert.Equal(1, (await worker.ReadAsync<SurveyState>("code-nochange"))!.Question);
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("..\\secret")]
    [InlineData("C:\\secret")]
    [InlineData("/secret")]
    [InlineData("code.with.dots")]
    [InlineData("")]
    public async Task TraversalAndNonDocumentKeysAreRejected(string key)
    {
        var worker = CreateStore();
        await Assert.ThrowsAsync<ArgumentException>(() => worker.ReadAsync<SurveyState>(key));
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void AppServiceUsesBuiltInSharedHomeAndLocalRunsStayOutsideWebroot()
    {
        var variables = new Dictionary<string, string?>
        {
            ["WEBSITE_INSTANCE_ID"] = "worker-1",
            ["HOME"] = _directory
        };
        Assert.Equal(Path.Combine(_directory, "data", "CotizadorInterno", "soporte-cloud-live", "v1"),
            SharedLiveSurveyStore.ResolveDirectory(_directory, name => variables.GetValueOrDefault(name)));
        Assert.Equal(Path.Combine(_directory, "App_Data", "soporte-cloud-live"),
            SharedLiveSurveyStore.ResolveDirectory(_directory, _ => null));
    }

    [Theory]
    [InlineData("WEBSITE_LOCAL_CACHE_OPTION", "Always")]
    [InlineData("WEBSITE_LOCALCACHE_READY", "true")]
    [InlineData("WEBSITE_LOCALCACHE_READY", "1")]
    public void AppServiceLocalCacheIsRejectedInsteadOfCreatingPrivateWorkerState(string setting, string value)
    {
        var variables = new Dictionary<string, string?>
        {
            ["WEBSITE_SITE_NAME"] = "app-service",
            ["HOME"] = _directory,
            [setting] = value
        };
        Assert.Throws<InvalidOperationException>(() =>
            SharedLiveSurveyStore.ResolveDirectory(_directory, name => variables.GetValueOrDefault(name)));
    }

    [Fact]
    public void MissingAppServiceHomeCannotFallBackToLocalState()
    {
        Assert.Throws<InvalidOperationException>(() => SharedLiveSurveyStore.ResolveDirectory(_directory,
            name => name == "WEBSITE_INSTANCE_ID" ? "worker-1" : null));
    }

    private SharedLiveSurveyStore CreateStore() => new(_directory, readCacheDuration: TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    public sealed class SurveyState
    {
        public int Question { get; set; }
        public Dictionary<string, int> Answers { get; set; } = new();
    }

    public sealed class GraphState
    {
        public string Label { get; set; } = "";
        public GraphState? Child { get; set; }
    }
}
