using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public sealed class MonthlySiigoAccountCatalogSyncHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SiigoAccountCatalogSyncOptions _options;
    private readonly ILogger<MonthlySiigoAccountCatalogSyncHostedService> _logger;

    public MonthlySiigoAccountCatalogSyncHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<SiigoAccountCatalogSyncOptions> options,
        ILogger<MonthlySiigoAccountCatalogSyncHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Sincronizacion mensual de catalogo contable Siigo desactivada por configuracion.");
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
                "Proxima sincronizacion mensual de catalogo contable Siigo programada para {NextRunUtc:u}.",
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
            var service = scope.ServiceProvider.GetRequiredService<ISiigoAccountCatalogSyncService>();
            await service.SyncAsync(ct: stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo la sincronizacion mensual de catalogo contable Siigo.");
        }
    }

    public static DateTimeOffset CalculateNextRunUtc(
        DateTimeOffset nowUtc,
        SiigoAccountCatalogSyncOptions options)
    {
        var timeZone = MonthlyFinancialReconciliationHostedService.ResolveTimeZone(options.TimeZoneId);
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

    private static (int Hour, int Minute) ParseRunTime(string? value)
    {
        if (TimeOnly.TryParse(value, out var parsed))
            return (parsed.Hour, parsed.Minute);

        return (6, 0);
    }

    private static DateTime BuildLocalRunDate(int year, int month, int configuredDay, int hour, int minute)
    {
        var day = Math.Clamp(configuredDay, 1, DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, day, hour, minute, 0);
    }
}
