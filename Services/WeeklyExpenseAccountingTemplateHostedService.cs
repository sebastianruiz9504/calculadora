using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public sealed class WeeklyExpenseAccountingTemplateHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ExpenseAccountingTemplateOptions _options;
    private readonly ILogger<WeeklyExpenseAccountingTemplateHostedService> _logger;

    public WeeklyExpenseAccountingTemplateHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<ExpenseAccountingTemplateOptions> options,
        ILogger<WeeklyExpenseAccountingTemplateHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Aplicacion semanal de plantillas contables de gastos desactivada por configuracion.");
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
                "Proxima aplicacion semanal de plantillas contables de gastos programada para {NextRunUtc:u}.",
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
            var service = scope.ServiceProvider.GetRequiredService<IExpenseAccountingTemplateService>();
            await service.ApplyAsync(
                movementType: _options.MovementType,
                overwrite: _options.Overwrite,
                dryRun: _options.DryRun,
                ct: stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo la aplicacion semanal de plantillas contables de gastos.");
        }
    }
}
