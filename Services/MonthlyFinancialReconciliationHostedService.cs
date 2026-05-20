using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public sealed class MonthlyFinancialReconciliationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FinancialReconciliationOptions _options;
    private readonly ILogger<MonthlyFinancialReconciliationHostedService> _logger;

    public MonthlyFinancialReconciliationHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<FinancialReconciliationOptions> options,
        ILogger<MonthlyFinancialReconciliationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Conciliacion financiera mensual desactivada por configuracion.");
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
                "Proxima conciliacion financiera mensual programada para {NextRunUtc:u}.",
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
            var service = scope.ServiceProvider.GetRequiredService<IFinancialReconciliationService>();
            await service.RunConfiguredPeriodAsync(ct: stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo la conciliacion financiera mensual.");
        }
    }

    public static DateTimeOffset CalculateNextRunUtc(
        DateTimeOffset nowUtc,
        FinancialReconciliationOptions options)
    {
        var timeZone = ResolveTimeZone(options.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        var (hour, minute) = ParseRunTime(options.RunTime);
        var candidate = BuildLocalRunDate(localNow.Year, localNow.Month, options.RunDayOfMonth, hour, minute);
        if (candidate <= localNow.DateTime)
        {
            var nextMonth = new DateOnly(localNow.Year, localNow.Month, 1).AddMonths(1);
            candidate = BuildLocalRunDate(nextMonth.Year, nextMonth.Month, options.RunDayOfMonth, hour, minute);
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), timeZone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        foreach (var candidate in new[]
        {
            timeZoneId,
            "SA Pacific Standard Time",
            "America/Bogota",
            "UTC"
        })
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate.Trim());
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }

    private static (int Hour, int Minute) ParseRunTime(string? value)
    {
        if (TimeOnly.TryParse(value, out var parsed))
            return (parsed.Hour, parsed.Minute);

        return (7, 0);
    }

    private static DateTime BuildLocalRunDate(int year, int month, int configuredDay, int hour, int minute)
    {
        var day = Math.Clamp(configuredDay, 1, DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, day, hour, minute, 0);
    }
}
