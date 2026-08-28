using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public sealed class MonthlyFinancialReconciliationHostedService : BackgroundService
{
    private const int FailedRetryDelayMinutes = 30;
    private static readonly JsonSerializerOptions StateJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FinancialReconciliationOptions _options;
    private readonly ILogger<MonthlyFinancialReconciliationHostedService> _logger;
    private string _lastSuccessfulRunKey = "";

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
            await RunConfiguredPeriodOnceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var dueRun = await ResolveDueRunAsync(DateTimeOffset.UtcNow, stoppingToken);
            if (dueRun is not null)
            {
                var succeeded = await RunScheduledOnceAsync(dueRun, stoppingToken);
                if (!succeeded)
                {
                    await DelaySafeAsync(TimeSpan.FromMinutes(FailedRetryDelayMinutes), stoppingToken);
                }

                continue;
            }

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
        }
    }

    private async Task RunConfiguredPeriodOnceAsync(CancellationToken stoppingToken)
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

    private async Task<bool> RunScheduledOnceAsync(
        ScheduledReconciliationRun dueRun,
        CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IFinancialReconciliationService>();
            var result = await service.RunAndSendAsync(dueRun.TargetYear, dueRun.TargetMonth, stoppingToken);
            _lastSuccessfulRunKey = dueRun.RunKey;
            await SaveStateAsync(new FinancialReconciliationScheduleState
            {
                LastSuccessfulRunKey = dueRun.RunKey,
                LastSuccessfulAtUtc = DateTimeOffset.UtcNow,
                LastScheduledLocal = dueRun.ScheduledLocal.ToString("yyyy-MM-dd HH:mm:ss"),
                LastScheduledUtc = dueRun.ScheduledUtc,
                LastTargetPeriod = $"{dueRun.TargetYear:D4}-{dueRun.TargetMonth:D2}",
                LastEmailSent = result.EmailSent,
                LastEmailStatus = result.EmailStatus,
                LastReteFuenteEmailSent = result.ReteFuenteEmailSent,
                LastReteFuenteEmailStatus = result.ReteFuenteEmailStatus
            }, stoppingToken);

            _logger.LogInformation(
                "Conciliacion financiera mensual programada {RunKey} ejecutada para periodo {Year}-{Month:D2}. Correo conciliacion: {EmailStatus}. Correo retefuente: {ReteFuenteEmailStatus}.",
                dueRun.RunKey,
                dueRun.TargetYear,
                dueRun.TargetMonth,
                result.EmailStatus,
                result.ReteFuenteEmailStatus);

            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Fallo la conciliacion financiera mensual programada {RunKey} para periodo {Year}-{Month:D2}. Se reintentara en {RetryMinutes} minutos.",
                dueRun.RunKey,
                dueRun.TargetYear,
                dueRun.TargetMonth,
                FailedRetryDelayMinutes);
            return false;
        }
    }

    private async Task<ScheduledReconciliationRun?> ResolveDueRunAsync(
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var timeZone = ResolveTimeZone(_options.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        var (hour, minute) = ParseRunTime(_options.RunTime);
        var scheduledLocal = BuildLocalRunDate(localNow.Year, localNow.Month, _options.RunDayOfMonth, hour, minute);
        if (scheduledLocal > localNow.DateTime)
        {
            var previousMonth = new DateOnly(localNow.Year, localNow.Month, 1).AddMonths(-1);
            scheduledLocal = BuildLocalRunDate(previousMonth.Year, previousMonth.Month, _options.RunDayOfMonth, hour, minute);
        }

        var runKey = $"{scheduledLocal.Year:D4}-{scheduledLocal.Month:D2}";
        if (string.Equals(_lastSuccessfulRunKey, runKey, StringComparison.OrdinalIgnoreCase))
            return null;

        var state = await ReadStateAsync(ct);
        if (string.Equals(state.LastSuccessfulRunKey, runKey, StringComparison.OrdinalIgnoreCase))
        {
            _lastSuccessfulRunKey = runKey;
            return null;
        }

        var offset = Math.Clamp(_options.PeriodOffsetMonths, 1, 24);
        var targetPeriod = new DateOnly(scheduledLocal.Year, scheduledLocal.Month, 1).AddMonths(-offset);
        var scheduledUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(scheduledLocal, DateTimeKind.Unspecified),
            timeZone);

        return new ScheduledReconciliationRun(
            runKey,
            scheduledLocal,
            new DateTimeOffset(scheduledUtc, TimeSpan.Zero),
            targetPeriod.Year,
            targetPeriod.Month);
    }

    private async Task<FinancialReconciliationScheduleState> ReadStateAsync(CancellationToken ct)
    {
        var path = ResolveStatePath();
        if (!File.Exists(path))
            return new FinancialReconciliationScheduleState();

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<FinancialReconciliationScheduleState>(json, StateJsonOptions)
                ?? new FinancialReconciliationScheduleState();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "No fue posible leer el estado de agenda de conciliacion financiera en {StatePath}. Se evaluara como no ejecutado.", path);
            return new FinancialReconciliationScheduleState();
        }
    }

    private async Task SaveStateAsync(FinancialReconciliationScheduleState state, CancellationToken ct)
    {
        var path = ResolveStatePath();
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(state, StateJsonOptions);
            await File.WriteAllTextAsync(path, json, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "No fue posible guardar el estado de agenda de conciliacion financiera en {StatePath}.", path);
        }
    }

    private static string ResolveStatePath()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
            return Path.Combine(home, "data", "CotizadorInterno", "financial-reconciliation-schedule-state.json");

        return Path.Combine(AppContext.BaseDirectory, "App_Data", "financial-reconciliation-schedule-state.json");
    }

    private static async Task DelaySafeAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
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

    private sealed record ScheduledReconciliationRun(
        string RunKey,
        DateTime ScheduledLocal,
        DateTimeOffset ScheduledUtc,
        int TargetYear,
        int TargetMonth);

    private sealed class FinancialReconciliationScheduleState
    {
        public string LastSuccessfulRunKey { get; set; } = "";
        public DateTimeOffset? LastSuccessfulAtUtc { get; set; }
        public string LastScheduledLocal { get; set; } = "";
        public DateTimeOffset? LastScheduledUtc { get; set; }
        public string LastTargetPeriod { get; set; } = "";
        public bool LastEmailSent { get; set; }
        public string LastEmailStatus { get; set; } = "";
        public bool LastReteFuenteEmailSent { get; set; }
        public string LastReteFuenteEmailStatus { get; set; } = "";
    }
}
