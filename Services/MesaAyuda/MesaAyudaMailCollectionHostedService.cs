using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services.MesaAyuda;

public sealed class MesaAyudaMailCollectionHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MesaAyudaMailCollectionOptions _options;
    private readonly ILogger<MesaAyudaMailCollectionHostedService> _logger;

    public MesaAyudaMailCollectionHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<MesaAyudaMailCollectionOptions> options,
        ILogger<MesaAyudaMailCollectionHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Colección de correo de Mesa de Ayuda desactivada por configuración.");
            return;
        }

        var interval = TimeSpan.FromSeconds(_options.PollIntervalSeconds);
        if (!_options.RunOnStartup
            && !await DelaySafeAsync(interval, stoppingToken))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceSafeAsync(stoppingToken);
            if (!await DelaySafeAsync(interval, stoppingToken))
                break;
        }
    }

    private async Task RunOnceSafeAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var collector =
                scope.ServiceProvider.GetRequiredService<IMesaAyudaMailCollector>();
            var result = await collector.CollectOnceAsync(ct);
            var failures = result.Mailboxes.Count(mailbox => !mailbox.Succeeded);
            _logger.LogInformation(
                "Ciclo de correo Mesa de Ayuda terminado: {MailboxCount} buzones, {ProcessedMessages} mensajes y {Failures} fallos.",
                result.Mailboxes.Count,
                result.ProcessedMessages,
                failures);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "No fue posible iniciar el ciclo de correo de Mesa de Ayuda. " +
                "Verifique el store durable y el sink registrados.");
            throw;
        }
    }

    private static async Task<bool> DelaySafeAsync(
        TimeSpan delay,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
    }
}
