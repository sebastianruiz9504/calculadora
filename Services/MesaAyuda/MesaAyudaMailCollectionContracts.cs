using CotizadorInterno.Web.Models.MesaAyuda;

namespace CotizadorInterno.Web.Services.MesaAyuda;

public interface IMesaAyudaGraphMailClient
{
    Task<MesaAyudaMailDeltaPage> GetDeltaPageAsync(
        MesaAyudaMailDeltaRequest request,
        CancellationToken ct = default);
}

public interface IMesaAyudaIncomingMailSink
{
    // Implementations must be idempotent by MesaAyudaCollectedMail.IdempotencyKey.
    // Collection and support classification intentionally remain separate stages.
    Task ProcessAsync(
        MesaAyudaCollectedMail message,
        CancellationToken ct = default);
}

public interface IMesaAyudaMailDeltaStore
{
    Task<MesaAyudaMailDeltaCheckpoint?> GetAsync(
        string mailbox,
        CancellationToken ct = default);

    // This must be a durable compare-and-swap. Returning false prevents an older
    // worker from overwriting a checkpoint advanced by another app instance.
    Task<bool> TryAdvanceAsync(
        MesaAyudaMailDeltaAdvance advance,
        CancellationToken ct = default);
}

public interface IMesaAyudaMailCollector
{
    Task<MesaAyudaMailCollectionResult> CollectOnceAsync(
        CancellationToken ct = default);
}
