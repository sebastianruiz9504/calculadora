using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public sealed class WeeklyCashFlowMatchingHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CashFlowMatchingOptions _options;
    private readonly ILogger<WeeklyCashFlowMatchingHostedService> _logger;

    public WeeklyCashFlowMatchingHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<CashFlowMatchingOptions> options,
        ILogger<WeeklyCashFlowMatchingHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Cruce semanal de flujo de caja desactivado por configuracion.");
            return;
        }

        if (_options.RunOnStartup)
            await RunOnceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRunUtc = WeeklyExpenseAccountingRulesHostedService.CalculateNextRunUtc(
                DateTimeOffset.UtcNow,
                new ExpenseAccountingRulesOptions
                {
                    RunDayOfWeek = _options.RunDayOfWeek,
                    RunTime = _options.RunTime,
                    TimeZoneId = _options.TimeZoneId
                });
            var delay = nextRunUtc - DateTimeOffset.UtcNow;
            if (delay < TimeSpan.Zero)
                delay = TimeSpan.Zero;

            _logger.LogInformation(
                "Proximo cruce semanal de flujo de caja programado para {NextRunUtc:u}.",
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
            var service = scope.ServiceProvider.GetRequiredService<ICashFlowMatchingService>();
            await service.MatchClientPaymentsAsync(dryRun: _options.DryRun, ct: stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo el cruce semanal de pagos de clientes desde flujo de caja.");
        }
    }
}
