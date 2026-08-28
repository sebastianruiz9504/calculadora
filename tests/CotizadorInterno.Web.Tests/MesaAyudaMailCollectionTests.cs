using System.Net;
using System.Text;
using Azure.Core;
using Azure.Identity;
using CotizadorInterno.Web.Models.MesaAyuda;
using CotizadorInterno.Web.Services.MesaAyuda;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class MesaAyudaMailCollectionTests
{
    private const string Mailbox = "sruiz@digitaltechcolombia.com";

    [Fact]
    public void DefaultsAreDisabledAndProductionCredentialHasNoInteractiveFallback()
    {
        var options = new MesaAyudaMailCollectionOptions();
        var production = MesaAyudaGraphTokenProvider.CreateCredentialChain(
            options,
            isDevelopment: false);
        var development = MesaAyudaGraphTokenProvider.CreateCredentialChain(
            options,
            isDevelopment: true);

        Assert.False(options.Enabled);
        Assert.Equal(7, MesaAyudaMailCollectionOptions.InitialLookbackDays);
        Assert.Single(production);
        Assert.IsType<ManagedIdentityCredential>(production[0]);
        Assert.Equal(2, development.Length);
        Assert.IsType<ManagedIdentityCredential>(development[0]);
        Assert.IsType<AzureCliCredential>(development[1]);
    }

    [Fact]
    public void ServiceCollectionExtensionResolvesGraphClientWhileRuntimeIsDisabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment { EnvironmentName = Environments.Production });
        services.AddMesaAyudaMailCollection(
            new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptions<MesaAyudaMailCollectionOptions>>()
            .Value;

        Assert.False(options.Enabled);
        Assert.NotNull(
            provider.GetRequiredService<IMesaAyudaGraphMailClient>());
        Assert.Null(provider.GetService<IMesaAyudaMailDeltaStore>());
        Assert.Null(provider.GetService<IMesaAyudaIncomingMailSink>());
    }

    [Fact]
    public async Task InitialGraphReadIsGetOnlyWithImmutableIdsAndSevenDayFilter()
    {
        HttpRequestMessage? captured = null;
        var handler = new DelegateHandler(request =>
        {
            captured = CloneRequest(request);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "value": [],
                      "@odata.deltaLink": "https://graph.microsoft.com/v1.0/users/sruiz%40digitaltechcolombia.com/mailFolders/inbox/messages/delta?$deltatoken=done"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var client = CreateGraphClient(handler);
        var initialAfter = new DateTimeOffset(
            2026,
            7,
            16,
            12,
            0,
            0,
            TimeSpan.Zero);

        await client.GetDeltaPageAsync(
            new MesaAyudaMailDeltaRequest(Mailbox, null, initialAfter));

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("Bearer unit-test-token", captured.Headers.Authorization?.ToString());
        Assert.Contains(
            "IdType=\"ImmutableId\"",
            captured.Headers.GetValues("Prefer"));
        var query = Uri.UnescapeDataString(captured.RequestUri!.Query);
        Assert.Contains(
            "$filter=receivedDateTime ge 2026-07-16T12:00:00Z",
            query,
            StringComparison.Ordinal);
        Assert.Contains("changeType=created", query, StringComparison.Ordinal);
        Assert.Contains("/mailFolders/inbox/messages/delta", captured.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task CollectorAdvancesCheckpointOnlyAfterEveryPageWasProcessed()
    {
        var events = new List<string>();
        var graph = new SequencedGraphClient(
            new MesaAyudaMailDeltaPage(
                [Change("graph-1")],
                "https://graph.microsoft.com/v1.0/users/sruiz%40digitaltechcolombia.com/mailFolders/inbox/messages/delta?$skiptoken=next",
                null),
            new MesaAyudaMailDeltaPage(
                [Change("graph-2")],
                null,
                "https://graph.microsoft.com/v1.0/users/sruiz%40digitaltechcolombia.com/mailFolders/inbox/messages/delta?$deltatoken=done"));
        var store = new RecordingDeltaStore(events);
        var sink = new RecordingSink(events);
        var now = new DateTimeOffset(
            2026,
            7,
            23,
            12,
            0,
            0,
            TimeSpan.Zero);
        var collector = CreateCollector(graph, store, sink, now);

        var result = await collector.CollectOnceAsync();

        var mailboxResult = Assert.Single(result.Mailboxes);
        Assert.True(mailboxResult.Succeeded);
        Assert.Equal(2, mailboxResult.ProcessedMessages);
        Assert.Equal(["sink:graph-1", "sink:graph-2", "advance"], events);
        Assert.Equal(2, graph.Requests.Count);
        Assert.Null(graph.Requests[0].ContinuationLink);
        Assert.Equal(
            now.AddDays(-7),
            graph.Requests[0].InitialReceivedAfterUtc);
        Assert.Equal(
            graph.Pages[0].NextLink,
            graph.Requests[1].ContinuationLink);
        Assert.NotNull(store.LastAdvance);
        Assert.Equal(graph.Pages[1].DeltaLink, store.LastAdvance!.DeltaLink);
    }

    [Fact]
    public async Task SinkFailureLeavesDurableCheckpointUntouched()
    {
        var events = new List<string>();
        var graph = new SequencedGraphClient(
            new MesaAyudaMailDeltaPage(
                [Change("graph-1")],
                null,
                "https://graph.microsoft.com/v1.0/users/sruiz%40digitaltechcolombia.com/mailFolders/inbox/messages/delta?$deltatoken=done"));
        var store = new RecordingDeltaStore(events);
        var sink = new RecordingSink(events, shouldFail: true);
        var collector = CreateCollector(
            graph,
            store,
            sink,
            DateTimeOffset.UtcNow);

        var result = await collector.CollectOnceAsync();

        var mailboxResult = Assert.Single(result.Mailboxes);
        Assert.False(mailboxResult.Succeeded);
        Assert.False(mailboxResult.CheckpointAdvanced);
        Assert.Null(store.LastAdvance);
        Assert.Equal(["sink:graph-1"], events);
    }

    [Fact]
    public async Task ContinuationLinkCannotEscapeConfiguredGraphInbox()
    {
        var called = false;
        var handler = new DelegateHandler(_ =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = CreateGraphClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetDeltaPageAsync(
                new MesaAyudaMailDeltaRequest(
                    Mailbox,
                    "https://evil.example/v1.0/users/x/messages/delta?token=secret",
                    DateTimeOffset.UtcNow)));

        Assert.False(called);
    }

    private static GraphMesaAyudaMailClient CreateGraphClient(
        HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            new StaticTokenProvider(),
            Options.Create(new MesaAyudaMailCollectionOptions()),
            NullLogger<GraphMesaAyudaMailClient>.Instance);

    private static MesaAyudaMailCollector CreateCollector(
        IMesaAyudaGraphMailClient graph,
        IMesaAyudaMailDeltaStore store,
        IMesaAyudaIncomingMailSink sink,
        DateTimeOffset now) =>
        new(
            graph,
            store,
            sink,
            Options.Create(
                new MesaAyudaMailCollectionOptions
                {
                    Enabled = true,
                    Mailboxes = [Mailbox]
                }),
            new FixedTimeProvider(now),
            NullLogger<MesaAyudaMailCollector>.Instance);

    private static MesaAyudaMailDeltaChange Change(string id) =>
        new(
            false,
            new MesaAyudaCollectedMail
            {
                IdempotencyKey =
                    MesaAyudaCollectedMail.CreateIdempotencyKey(Mailbox, id),
                Mailbox = Mailbox,
                GraphMessageId = id,
                ReceivedAtUtc = DateTimeOffset.UtcNow
            });

    private static HttpRequestMessage CloneRequest(HttpRequestMessage source)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);
        foreach (var header in source.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return clone;
    }

    private sealed class StaticTokenProvider : IMesaAyudaGraphTokenProvider
    {
        public ValueTask<AccessToken> GetTokenAsync(CancellationToken ct) =>
            ValueTask.FromResult(
                new AccessToken(
                    "unit-test-token",
                    DateTimeOffset.UtcNow.AddMinutes(10)));
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private sealed class SequencedGraphClient(
        params MesaAyudaMailDeltaPage[] pages)
        : IMesaAyudaGraphMailClient
    {
        private int _index;

        public IReadOnlyList<MesaAyudaMailDeltaPage> Pages { get; } = pages;
        public List<MesaAyudaMailDeltaRequest> Requests { get; } = [];

        public Task<MesaAyudaMailDeltaPage> GetDeltaPageAsync(
            MesaAyudaMailDeltaRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(pages[_index++]);
        }
    }

    private sealed class RecordingDeltaStore(List<string> events)
        : IMesaAyudaMailDeltaStore
    {
        public MesaAyudaMailDeltaAdvance? LastAdvance { get; private set; }

        public Task<MesaAyudaMailDeltaCheckpoint?> GetAsync(
            string mailbox,
            CancellationToken ct = default) =>
            Task.FromResult<MesaAyudaMailDeltaCheckpoint?>(null);

        public Task<bool> TryAdvanceAsync(
            MesaAyudaMailDeltaAdvance advance,
            CancellationToken ct = default)
        {
            events.Add("advance");
            LastAdvance = advance;
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingSink(
        List<string> events,
        bool shouldFail = false)
        : IMesaAyudaIncomingMailSink
    {
        public Task ProcessAsync(
            MesaAyudaCollectedMail message,
            CancellationToken ct = default)
        {
            events.Add($"sink:{message.GraphMessageId}");
            return shouldFail
                ? Task.FromException(
                    new InvalidOperationException("simulated sink failure"))
                : Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "";
        public string ApplicationName { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
