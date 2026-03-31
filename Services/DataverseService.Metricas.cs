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

    private static readonly MetricsVerticalGoalDefinition[] MetricsVerticalGoals =
    {
        new("cloud", "Cloud", 180m, "#145AF2"),
        new("copiers", "Copiers", 70m, "#F97316")
    };

    public async Task<MetricsDashboardDto> GetMetricsDashboardAsync(MetricsRangeFilter filter, string? sellerKey = null, CancellationToken ct = default)
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

        var sellers = records
            .GroupBy(record => NormalizeMetricsKey(record.SalesPerson), StringComparer.OrdinalIgnoreCase)
            .Select(group => new MetricsSellerOptionDto
            {
                Key = group.Key,
                Name = NormalizeMetricsName(group.First().SalesPerson, "Sin vendedor")
            })
            .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var requestedSellerKey = NormalizeMetricsFilterKey(sellerKey);
        var appliedSeller = sellers.FirstOrDefault(option =>
            string.Equals(option.Key, requestedSellerKey, StringComparison.OrdinalIgnoreCase));

        if (appliedSeller is not null)
        {
            records = records
                .Where(record => string.Equals(NormalizeMetricsKey(record.SalesPerson), appliedSeller.Key, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var goalRange = BuildMetricsGoalRange(filter);
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
                accumulate: false)
        };

        charts.AddRange(MetricsVerticalGoals.Select(goal =>
            BuildVerticalGoalChart(goal, records, goalRange, accumulate: false, filter)));
        charts.AddRange(MetricsVerticalGoals.Select(goal =>
            BuildVerticalGoalChart(goal, records, goalRange, accumulate: true, filter)));

        return new MetricsDashboardDto
        {
            Filter = filter.ToKey(),
            FilterLabel = filter.ToLabel(),
            GranularityLabel = range.Granularity == MetricsGranularity.Day ? "Diaria" : "Mensual",
            AppliedSellerKey = appliedSeller?.Key ?? "",
            AppliedSellerName = appliedSeller?.Name ?? "Todos los vendedores",
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
            Sellers = sellers,
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
            var annualValues = range.Categories
                .Select(category => RoundCurrency(group.Records
                    .Where(record => string.Equals(GetMetricsBucketKey(record.ContractStartDateValue, range.Granularity), category.Key, StringComparison.OrdinalIgnoreCase))
                    .Sum(record => record.AnnualValue)))
                .ToList();

            if (accumulate)
            {
                for (var valueIndex = 1; valueIndex < values.Count; valueIndex++)
                {
                    values[valueIndex] = RoundCurrency(values[valueIndex - 1] + values[valueIndex]);
                    annualValues[valueIndex] = RoundCurrency(annualValues[valueIndex - 1] + annualValues[valueIndex]);
                }
            }

            series.Add(new MetricsSeriesDto
            {
                Key = group.Key,
                Name = group.Name,
                Color = MetricsColorPalette[seriesIndex % MetricsColorPalette.Length],
                TotalScore = group.TotalScore,
                TotalAnnualValue = group.TotalAnnualValue,
                Values = values,
                AnnualValues = annualValues
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

    private MetricsChartDto BuildVerticalGoalChart(
        MetricsVerticalGoalDefinition goal,
        IReadOnlyList<ScoreRecordDto> records,
        MetricsRangeDefinition range,
        bool accumulate,
        MetricsRangeFilter filter)
    {
        var verticalRecords = records
            .Where(record => string.Equals(
                NormalizeMetricsKey(ResolveVerticalLabel(record.VerticalOptionValue)),
                goal.Key,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        var categories = range.Categories
            .Select(category => category.DisplayLabel)
            .ToList();

        var actualValues = range.Categories
            .Select(category => RoundCurrency(verticalRecords
                .Where(record => string.Equals(GetMetricsBucketKey(record.ContractStartDateValue, range.Granularity), category.Key, StringComparison.OrdinalIgnoreCase))
                .Sum(record => record.Score)))
            .ToList();

        var actualAnnualValues = range.Categories
            .Select(category => RoundCurrency(verticalRecords
                .Where(record => string.Equals(GetMetricsBucketKey(record.ContractStartDateValue, range.Granularity), category.Key, StringComparison.OrdinalIgnoreCase))
                .Sum(record => record.AnnualValue)))
            .ToList();

        var goalValues = range.Categories
            .Select(_ => RoundCurrency(goal.MonthlyGoal))
            .ToList();

        if (accumulate)
        {
            for (var index = 1; index < actualValues.Count; index++)
            {
                actualValues[index] = RoundCurrency(actualValues[index - 1] + actualValues[index]);
                actualAnnualValues[index] = RoundCurrency(actualAnnualValues[index - 1] + actualAnnualValues[index]);
                goalValues[index] = RoundCurrency(goalValues[index - 1] + goalValues[index]);
            }
        }

        var currentMonth = new DateOnly(GetBogotaToday().Year, GetBogotaToday().Month, 1);
        var goalStatuses = new List<MetricsGoalStatusDto>(categories.Count);
        for (var index = 0; index < categories.Count; index++)
        {
            var status = ResolveGoalStatus(range.Categories[index].Key, actualValues[index], goalValues[index], currentMonth);
            goalStatuses.Add(new MetricsGoalStatusDto
            {
                Category = categories[index],
                ActualValue = actualValues[index],
                TargetValue = goalValues[index],
                IsMet = status.IsMet,
                StatusTone = status.Tone,
                StatusLabel = status.Label
            });
        }

        var goalFinalValue = goalValues.LastOrDefault();
        var verticalTotalScore = RoundCurrency(verticalRecords.Sum(record => record.Score));
        var verticalTotalAnnualValue = RoundCurrency(verticalRecords.Sum(record => record.AnnualValue));
        var comparisonLabel = accumulate ? "Meta acumulada" : "Meta mensual";

        return new MetricsChartDto
        {
            Key = $"{goal.Key}-{(accumulate ? "accumulated" : "monthly")}-goal",
            Title = accumulate
                ? $"{goal.Label}: puntaje acumulado vs meta"
                : $"{goal.Label}: puntaje mensual vs meta",
            Subtitle = accumulate
                ? $"Avance acumulado en {filter.ToLabel().ToLowerInvariant()} frente a la meta del a\u00f1o"
                : $"Seguimiento por mes en {filter.ToLabel().ToLowerInvariant()} frente a la meta mensual",
            GoalLabel = accumulate
                ? $"Meta acumulada ({goal.MonthlyGoal:0.##} por mes)"
                : $"Meta mensual {goal.MonthlyGoal:0.##}",
            TotalScore = verticalTotalScore,
            TotalAnnualValue = verticalTotalAnnualValue,
            Categories = categories,
            GoalStatuses = goalStatuses,
            Series = new[]
            {
                new MetricsSeriesDto
                {
                    Key = $"{goal.Key}-actual",
                    Name = accumulate ? $"{goal.Label} acumulado" : $"{goal.Label} real",
                    Color = goal.Color,
                    TotalScore = verticalTotalScore,
                    TotalAnnualValue = verticalTotalAnnualValue,
                    Values = actualValues,
                    AnnualValues = actualAnnualValues
                },
                new MetricsSeriesDto
                {
                    Key = $"{goal.Key}-goal",
                    Name = comparisonLabel,
                    Color = "#94A3B8",
                    IsReference = true,
                    StrokeDasharray = "8 6",
                    LegendNote = $"{comparisonLabel} {goalFinalValue:0.##}",
                    TotalScore = goalFinalValue,
                    TotalAnnualValue = 0m,
                    Values = goalValues,
                    AnnualValues = Array.Empty<decimal>()
                }
            }
        };
    }

    private static (bool IsMet, string Tone, string Label) ResolveGoalStatus(
        string categoryKey,
        decimal actualValue,
        decimal targetValue,
        DateOnly currentMonth)
    {
        if (!DateOnly.TryParseExact(categoryKey, "yyyy-MM-01", CultureInfo.InvariantCulture, DateTimeStyles.None, out var categoryMonth))
        {
            return actualValue >= targetValue
                ? (true, "met", "Cumplido")
                : (false, "missed", "No cumplido");
        }

        if (categoryMonth > currentMonth)
        {
            return (false, "upcoming", "Pendiente");
        }

        if (categoryMonth == currentMonth)
        {
            return (actualValue >= targetValue, "in-progress", "En curso");
        }

        return actualValue >= targetValue
            ? (true, "met", "Cumplido")
            : (false, "missed", "No cumplido");
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

    private MetricsRangeDefinition BuildMetricsGoalRange(MetricsRangeFilter filter)
    {
        var today = GetBogotaToday();
        return filter switch
        {
            MetricsRangeFilter.ThisMonth => BuildMonthlyMetricsRange(
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

    private static string? NormalizeMetricsFilterKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var normalized = raw.Trim().ToLowerInvariant();
        return normalized is "all" or "*" ? null : normalized;
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

    private sealed record MetricsVerticalGoalDefinition(
        string Key,
        string Label,
        decimal MonthlyGoal,
        string Color);
}
