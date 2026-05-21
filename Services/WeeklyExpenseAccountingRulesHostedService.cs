using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public sealed class WeeklyExpenseAccountingRulesHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ExpenseAccountingRulesOptions _options;
    private readonly ILogger<WeeklyExpenseAccountingRulesHostedService> _logger;

    public WeeklyExpenseAccountingRulesHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<ExpenseAccountingRulesOptions> options,
        ILogger<WeeklyExpenseAccountingRulesHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Aplicacion semanal de reglas contables de gastos desactivada por configuracion.");
            return;
        }

        if (_options.RunOnStartup)
            await RunOnceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRunUtc = CalculateNextRunUtc(DateTimeOffset.UtcNow, _options);
            var delay = nextRunUtc - DateTimeOffset.UtcNow;
            if (delay < TimeSpan.Zero)
                delay = TimeSpan.Zero;

            _logger.LogInformation(
                "Proxima aplicacion semanal de reglas contables de gastos programada para {NextRunUtc:u}.",
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
            var service = scope.ServiceProvider.GetRequiredService<IExpenseAccountingRuleService>();
            await service.ApplyAsync(
                movementType: _options.MovementType,
                overwrite: _options.Overwrite,
                ct: stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo la aplicacion semanal de reglas contables de gastos.");
        }
    }

    public static DateTimeOffset CalculateNextRunUtc(DateTimeOffset nowUtc, ExpenseAccountingRulesOptions options)
    {
        var timeZone = MonthlyFinancialReconciliationHostedService.ResolveTimeZone(options.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        var targetDay = ParseDayOfWeek(options.RunDayOfWeek);
        var (hour, minute) = ParseRunTime(options.RunTime);
        var daysUntilTarget = ((int)targetDay - (int)localNow.DayOfWeek + 7) % 7;
        var candidateDate = localNow.Date.AddDays(daysUntilTarget);
        var candidate = new DateTime(candidateDate.Year, candidateDate.Month, candidateDate.Day, hour, minute, 0);
        if (candidate <= localNow.DateTime)
            candidate = candidate.AddDays(7);

        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), timeZone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static DayOfWeek ParseDayOfWeek(string? value)
    {
        if (Enum.TryParse<DayOfWeek>(value, ignoreCase: true, out var parsed))
            return parsed;

        return DayOfWeek.Monday;
    }

    private static (int Hour, int Minute) ParseRunTime(string? value)
    {
        if (TimeOnly.TryParse(value, out var parsed))
            return (parsed.Hour, parsed.Minute);

        return (8, 0);
    }
}
