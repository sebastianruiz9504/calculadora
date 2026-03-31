using System.Globalization;
using CotizadorInterno.Web.Models.Metricas;
using CotizadorInterno.Web.Models.Puntajes;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private static readonly string[] MetricsColorPalette =
    {
        "#145AF2",
        "#10B981",
        "#F97316",
        "#7C3AED",
        "#EF4444",
        "#0891B2",
        "#CA8A04",
        "#DC2626"
    };

    public async Task<MetricsDashboardDto> GetMetricsDashboardAsync(MetricsRangeFilter filter, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var range = BuildMetricsRange(filter);
        var filterExpression = $"{_scoresContractStartDateField} ge {range.StartInclusive:yyyy-MM-dd} and {_scoresContractStartDateField} lt {range.EndExclusive:yyyy-MM-dd}";
        var relativeUrl = $"/api/data/v9.2/{_scoresTableSetName}?$filter={Uri.EscapeDataString(filterExpression)}&$orderby={_scoresContractStartDateField} asc";
        var rawRecords = await GetDataverseEntitiesAsync(relativeUrl, httpContext.User, ct, AddFormattedValueHeaders);

        var records = rawRecords
            .Select(ParseScoreRecord)
            .Where(item => item is not null)
            .Cast<ScoreRecordDto>()
            .OrderBy(item => item.ContractStartDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var charts = new List<MetricsChartDto>
        {
            BuildMetricsChart(
                key: "score-by-seller",
                title: "Puntaje por vendedor en el tiempo",
                subtitle: $"Filtro actual: {filter.ToLabel()}",
                records: records,
                range: range,
                seriesKeySelector: record => NormalizeMetricsKey(record.SalesPerson),
                seriesNameSelector: record => NormalizeMetricsName(record.SalesPerson, "Sin vendedor"),
                accumulate: false),
            BuildMetricsChart(
                key: "accumulated-score-by-seller",
                title: "Puntaje acumulado por vendedor",
                subtitle: $"Acumulado en {filter.ToLabel().ToLowerInvariant()}",
                records: records,
                range: range,
                seriesKeySelector: record => NormalizeMetricsKey(record.SalesPerson),
                seriesNameSelector: record => NormalizeMetricsName(record.SalesPerson, "Sin vendedor"),
                accumulate: true),
            BuildMetricsChart(
                key: "total-score",
                title: "Puntaje total",
                subtitle: $"Puntaje total en {filter.ToLabel().ToLowerInvariant()}",
                records: records,
                range: range,
                seriesKeySelector: _ => "total",
                seriesNameSelector: _ => "Total",
                accumulate: false),
            BuildMetricsChart(
                key: "score-by-vertical",
                title: "Puntaje total por vertical",
                subtitle: $"Verticales en {filter.ToLabel().ToLowerInvariant()}",
                records: records,
                range: range,
                seriesKeySelector: record => NormalizeMetricsKey(ResolveVerticalLabel(record.VerticalOptionValue)),
                seriesNameSelector: record => ResolveVerticalLabel(record.VerticalOptionValue),
                accumulate: false)
        };

        return new MetricsDashboardDto
        {
            Filter = filter.ToKey(),
            FilterLabel = filter.ToLabel(),
            RecordsCount = records.Count,
            SellersCount = records
                .Select(record => NormalizeMetricsName(record.SalesPerson, "Sin vendedor"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            VerticalsCount = records
                .Select(record => ResolveVerticalLabel(record.VerticalOptionValue))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            TotalScore = RoundCurrency(records.Sum(record => record.Score)),
            TotalAnnualValue = RoundCurrency(records.Sum(record => record.AnnualValue)),
            Charts = charts
        };
    }

    private MetricsChartDto BuildMetricsChart(
        string key,
        string title,
        string subtitle,
        IReadOnlyList<ScoreRecordDto> records,
        MetricsRangeDefinition range,
        Func<ScoreRecordDto, string> seriesKeySelector,
        Func<ScoreRecordDto, string> seriesNameSelector,
        bool accumulate)
    {
        var categories = range.Categories
            .Select(category => category.DisplayLabel)
            .ToList();

        var groupedRecords = records
            .GroupBy(seriesKeySelector, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var representative = group.First();
                return new
                {
                    Key = group.Key,
                    Name = seriesNameSelector(representative),
                    Records = group.ToList(),
                    TotalScore = RoundCurrency(group.Sum(record => record.Score)),
                    TotalAnnualValue = RoundCurrency(group.Sum(record => record.AnnualValue))
                };
            })
            .OrderByDescending(group => group.TotalScore)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var series = new List<MetricsSeriesDto>(groupedRecords.Count);
        for (var seriesIndex = 0; seriesIndex < groupedRecords.Count; seriesIndex++)
        {
            var group = groupedRecords[seriesIndex];
            var values = range.Categories
                .Select(category => RoundCurrency(group.Records
                    .Where(record => string.Equals(GetMetricsBucketKey(record.ContractStartDateValue, range.Granularity), category.Key, StringComparison.OrdinalIgnoreCase))
                    .Sum(record => record.Score)))
                .ToList();

            if (accumulate)
            {
                for (var valueIndex = 1; valueIndex < values.Count; valueIndex++)
                {
                    values[valueIndex] = RoundCurrency(values[valueIndex - 1] + values[valueIndex]);
                }
            }

            series.Add(new MetricsSeriesDto
            {
                Key = group.Key,
                Name = group.Name,
                Color = MetricsColorPalette[seriesIndex % MetricsColorPalette.Length],
                TotalScore = group.TotalScore,
                TotalAnnualValue = group.TotalAnnualValue,
                Values = values
            });
        }

        return new MetricsChartDto
        {
            Key = key,
            Title = title,
            Subtitle = subtitle,
            TotalScore = RoundCurrency(records.Sum(record => record.Score)),
            TotalAnnualValue = RoundCurrency(records.Sum(record => record.AnnualValue)),
            Categories = categories,
            Series = series
        };
    }

    private MetricsRangeDefinition BuildMetricsRange(MetricsRangeFilter filter)
    {
        var today = GetBogotaToday();
        return filter switch
        {
            MetricsRangeFilter.ThisMonth => BuildDailyMetricsRange(
                new DateOnly(today.Year, today.Month, 1),
                new DateOnly(today.Year, today.Month, 1).AddMonths(1)),
            MetricsRangeFilter.PreviousYear => BuildMonthlyMetricsRange(
                new DateOnly(today.Year - 1, 1, 1),
                new DateOnly(today.Year, 1, 1)),
            _ => BuildMonthlyMetricsRange(
                new DateOnly(today.Year, 1, 1),
                new DateOnly(today.Year + 1, 1, 1))
        };
    }

    private static MetricsRangeDefinition BuildDailyMetricsRange(DateOnly startInclusive, DateOnly endExclusive)
    {
        var categories = new List<MetricsCategory>();
        for (var date = startInclusive; date < endExclusive; date = date.AddDays(1))
        {
            categories.Add(new MetricsCategory(
                Key: date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DisplayLabel: date.ToString("dd MMM", CultureInfo.GetCultureInfo("es-CO"))));
        }

        return new MetricsRangeDefinition(startInclusive, endExclusive, MetricsGranularity.Day, categories);
    }

    private static MetricsRangeDefinition BuildMonthlyMetricsRange(DateOnly startInclusive, DateOnly endExclusive)
    {
        var categories = new List<MetricsCategory>();
        for (var date = startInclusive; date < endExclusive; date = date.AddMonths(1))
        {
            categories.Add(new MetricsCategory(
                Key: new DateOnly(date.Year, date.Month, 1).ToString("yyyy-MM-01", CultureInfo.InvariantCulture),
                DisplayLabel: date.ToString("MMM", CultureInfo.GetCultureInfo("es-CO"))));
        }

        return new MetricsRangeDefinition(startInclusive, endExclusive, MetricsGranularity.Month, categories);
    }

    private static string GetMetricsBucketKey(string contractStartDateValue, MetricsGranularity granularity)
    {
        if (!TryParseDateOnly(contractStartDateValue, out var contractDate))
            return "";

        return granularity == MetricsGranularity.Day
            ? contractDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : new DateOnly(contractDate.Year, contractDate.Month, 1).ToString("yyyy-MM-01", CultureInfo.InvariantCulture);
    }

    private static string NormalizeMetricsKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "empty";

        return raw.Trim().ToLowerInvariant();
    }

    private static string NormalizeMetricsName(string? raw, string fallback)
    {
        return string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
    }

    private static string ResolveVerticalLabel(int optionValue)
    {
        return PuntajesOptionCatalog.VerticalOptions
            .FirstOrDefault(option => option.Value == optionValue)?.Label
            ?? "Sin vertical";
    }

    private enum MetricsGranularity
    {
        Day = 0,
        Month = 1
    }

    private sealed record MetricsCategory(string Key, string DisplayLabel);

    private sealed record MetricsRangeDefinition(
        DateOnly StartInclusive,
        DateOnly EndExclusive,
        MetricsGranularity Granularity,
        IReadOnlyList<MetricsCategory> Categories);
}
