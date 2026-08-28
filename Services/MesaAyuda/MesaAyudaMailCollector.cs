using CotizadorInterno.Web.Models.MesaAyuda;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services.MesaAyuda;

public sealed class MesaAyudaMailCollector : IMesaAyudaMailCollector
{
    private readonly IMesaAyudaGraphMailClient _graph;
    private readonly IMesaAyudaMailDeltaStore _deltaStore;
    private readonly IMesaAyudaIncomingMailSink _sink;
    private readonly MesaAyudaMailCollectionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MesaAyudaMailCollector> _logger;

    public MesaAyudaMailCollector(
        IMesaAyudaGraphMailClient graph,
        IMesaAyudaMailDeltaStore deltaStore,
        IMesaAyudaIncomingMailSink sink,
        IOptions<MesaAyudaMailCollectionOptions> options,
        TimeProvider timeProvider,
        ILogger<MesaAyudaMailCollector> logger)
    {
        _graph = graph;
        _deltaStore = deltaStore;
        _sink = sink;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<MesaAyudaMailCollectionResult> CollectOnceAsync(
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
            return new MesaAyudaMailCollectionResult(false, []);

        var results = new List<MesaAyudaMailboxCollectionResult>();
        foreach (var mailbox in _options.GetNormalizedMailboxes())
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await CollectMailboxSafeAsync(mailbox, ct));
        }

        return new MesaAyudaMailCollectionResult(true, results);
    }

    private async Task<MesaAyudaMailboxCollectionResult> CollectMailboxSafeAsync(
        string mailbox,
        CancellationToken ct)
    {
        try
        {
            return await CollectMailboxAsync(mailbox, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Falló la colección de correo para {Mailbox}; el cursor delta no se avanzó.",
                mailbox);
            return new MesaAyudaMailboxCollectionResult(
                mailbox,
                false,
                0,
                false,
                ex.GetType().Name);
        }
    }

    internal async Task<MesaAyudaMailboxCollectionResult> CollectMailboxAsync(
        string mailbox,
        CancellationToken ct)
    {
        var checkpoint = await _deltaStore.GetAsync(mailbox, ct);
        if (checkpoint is not null
            && (!string.Equals(
                    checkpoint.Mailbox,
                    mailbox,
                    StringComparison.OrdinalIgnoreCase)
                || checkpoint.Version < 0
                || string.IsNullOrWhiteSpace(checkpoint.DeltaLink)))
        {
            throw new InvalidOperationException(
                "El checkpoint durable del buzón no es válido.");
        }

        var now = _timeProvider.GetUtcNow();
        var receivedAfter = now.AddDays(
            -MesaAyudaMailCollectionOptions.InitialLookbackDays);
        string? continuationLink = checkpoint?.DeltaLink;
        string? finalDeltaLink = null;
        var seenLinks = new HashSet<string>(StringComparer.Ordinal);
        var processed = 0;
        DateTimeOffset? lastMessageAt = null;

        for (var pageNumber = 1;
             pageNumber <= _options.MaxPagesPerMailbox;
             pageNumber++)
        {
            if (!string.IsNullOrWhiteSpace(continuationLink)
                && !seenLinks.Add(continuationLink))
            {
                throw new InvalidOperationException(
                    "Microsoft Graph devolvió un ciclo de paginación delta.");
            }

            var page = await _graph.GetDeltaPageAsync(
                new MesaAyudaMailDeltaRequest(
                    mailbox,
                    continuationLink,
                    receivedAfter),
                ct);

            foreach (var change in page.Changes)
            {
                if (change.IsRemoved || change.Message is null)
                    continue;

                await _sink.ProcessAsync(change.Message, ct);
                processed++;
                if (change.Message.ReceivedAtUtc is { } receivedAt
                    && (!lastMessageAt.HasValue || receivedAt > lastMessageAt))
                {
                    lastMessageAt = receivedAt;
                }
            }

            if (!string.IsNullOrWhiteSpace(page.NextLink))
            {
                continuationLink = page.NextLink;
                continue;
            }

            finalDeltaLink = page.DeltaLink;
            break;
        }

        if (string.IsNullOrWhiteSpace(finalDeltaLink))
        {
            throw new InvalidOperationException(
                "La colección delta no terminó dentro del límite de páginas " +
                "o Microsoft Graph no entregó un deltaLink final.");
        }

        var advanced = await _deltaStore.TryAdvanceAsync(
            new MesaAyudaMailDeltaAdvance(
                mailbox,
                checkpoint?.Version,
                finalDeltaLink,
                _timeProvider.GetUtcNow(),
                lastMessageAt),
            ct);

        return new MesaAyudaMailboxCollectionResult(
            mailbox,
            advanced,
            processed,
            advanced,
            advanced ? "completed" : "checkpoint_conflict");
    }
}
