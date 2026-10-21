using System.Globalization;

namespace CotizadorInterno.Web.Services;

internal static class LicensingConsumptionPeriod
{
    internal static DateOnly? ConsumptionMonth(string? interval, DateOnly? invoiceDate)
    {
        var text = (interval ?? "").Trim().Replace('.', '/').Replace('-', '/').Replace('\\', '/');
        var parts = text.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            var yearText = parts[0].Length == 4 ? parts[0] : parts[1];
            var monthText = parts[0].Length == 4 ? parts[1] : parts[0];
            if (int.TryParse(yearText, out var year) && int.TryParse(monthText, out var month)
                && year is >= 1900 and <= 2100 && month is >= 1 and <= 12)
                return new DateOnly(year, month, 1);
        }
        return invoiceDate is { Year: > 1 } date ? new DateOnly(date.Year, date.Month, 1).AddMonths(-1) : null;
    }

    // Include explicit consumption periods even when a prepaid cost was moved independently
    // of its supplier invoice. The caller applies ConsumptionMonth again after retrieval.
    internal static string Filter(DateOnly start, DateOnly end)
    {
        var clauses = new List<string>
        {
            $"(cr07a_factura ge {start.AddMonths(1):yyyy-MM-dd} and cr07a_factura lt {end.AddMonths(1):yyyy-MM-dd})"
        };
        // Year candidates keep the URL bounded and include whitespace/format variations.
        // The exact month interval is enforced after parsing each returned row.
        for (var year = start.Year; year <= end.AddDays(-1).Year; year++)
            clauses.Add($"contains(cr07a_mesconsumo,'{year.ToString(CultureInfo.InvariantCulture)}')");
        return string.Join(" or ", clauses.Distinct());
    }
}
