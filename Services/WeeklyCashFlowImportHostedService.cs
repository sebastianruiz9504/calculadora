using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public sealed class WeeklyCashFlowImportHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CashFlowImportOptions _options;
    private readonly ILogger<WeeklyCashFlowImportHostedService> _logger;

    public WeeklyCashFlowImportHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<CashFlowImportOptions> options,
        ILogger<WeeklyCashFlowImportHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Importacion semanal de flujo de caja desactivada por configuracion.");
            return;
        }

        if (_options.RunOnStartup)
            await RunOnceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRunUtc = CashFlowSchedule.CalculateNextDailyRunUtc(
                DateTimeOffset.UtcNow,
                _options.RunTime,
                _options.TimeZoneId);
            var delay = nextRunUtc - DateTimeOffset.UtcNow;
            if (delay < TimeSpan.Zero)
                delay = TimeSpan.Zero;

            _logger.LogInformation(
                "Proxima importacion diaria de flujo de caja programada para {NextRunUtc:u}.",
                nextRunUtc);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ICashFlowImportService>();
            await service.ImportAsync(_options.DryRun, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo la importacion diaria de flujo de caja.");
        }
    }
}
