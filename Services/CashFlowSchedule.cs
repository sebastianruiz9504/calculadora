namespace CotizadorInterno.Web.Services;

internal static class CashFlowSchedule
{
    public static DateTimeOffset CalculateNextDailyRunUtc(
        DateTimeOffset nowUtc,
        string? runTime,
        string? timeZoneId)
    {
        var timeZone = MonthlyFinancialReconciliationHostedService.ResolveTimeZone(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        var (hour, minute) = ParseRunTime(runTime);
        var candidate = new DateTime(localNow.Year, localNow.Month, localNow.Day, hour, minute, 0);
        if (candidate <= localNow.DateTime)
            candidate = candidate.AddDays(1);

        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), timeZone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static (int Hour, int Minute) ParseRunTime(string? value)
    {
        if (TimeOnly.TryParse(value, out var parsed))
            return (parsed.Hour, parsed.Minute);

        return (7, 30);
    }
}
