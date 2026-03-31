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

    private static readonly MetricsIndividualGoalDefinition[] MetricsIndividualGoals =
    {
        new("cindy garzon", "Cindy Garzon", 125m, 90m, 35m),
        new("jhonatan saldarriaga", "Jhonatan Saldarriaga", 125m, 90m, 35m)
    };

    public async Task<MetricsDashboardDto> GetMetricsDashboardAsync(
        MetricsRangeFilter filter,
        MetricsViewMode view,
        string? sellerKey = null,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var fetchRange = BuildMetricsRange(filter);
        var filterExpression = $"{_scoresContractStartDateField} ge {fetchRange.StartInclusive:yyyy-MM-dd} and {_scoresContractStartDateField} lt {fetchRange.EndExclusive:yyyy-MM-dd}";
        var relativeUrl = $"/api/data/v9.2/{_scoresTableSetName}?$filter={Uri.EscapeDataString(filterExpression)}&$orderby={_scoresContractStartDateField} asc";
        var rawRecords = await GetDataverseEntitiesAsync(relativeUrl, httpContext.User, ct, AddFormattedValueHeaders);

        var allRecords = rawRecords
            .Select(ParseScoreRecord)
            .Where(item => item is not null)
            .Cast<ScoreRecordDto>()
            .OrderBy(item => item.ContractStartDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sellers = allRecords
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

        return view == MetricsViewMode.Individual
            ? BuildIndividualDashboard(filter, allRecords, sellers, appliedSeller)
            : BuildGlobalDashboard(filter, allRecords, sellers);
    }

    private MetricsDashboardDto BuildGlobalDashboard(
        MetricsRangeFilter filter,
        IReadOnlyList<ScoreRecordDto> records,
        IReadOnlyList<MetricsSellerOptionDto> sellers)
    {
        var range = BuildMetricsRange(filter);
        var goalRange = BuildMetricsGoalRange(filter);

        var charts = new List<MetricsChartDto>
        {
            BuildSingleTrendChart(
                key: "global-total-score",
                title: "Puntaje total",
                subtitle: $"Puntaje total en {filter.ToLabel().ToLowerInvariant()}",
                records: records,
                range: range,
                seriesName: "Total",
                color: MetricsColorPalette[0])
        };

        charts.AddRange(MetricsVerticalGoals.Select(goal =>
            BuildGoalComparisonChart(
                key: $"{goal.Key}-monthly-goal",
                title: $"{goal.Label}: puntaje mensual vs meta",
                subtitle: $"Seguimiento por mes en {filter.ToLabel().ToLowerInvariant()} frente a la meta mensual",
                records: FilterRecordsByVertical(records, goal.Key),
                range: goalRange,
                actualSeriesName: $"{goal.Label} real",
                color: goal.Color,
                goalValue: goal.MonthlyGoal,
                goalLabel: $"Meta mensual {goal.MonthlyGoal:0.##}",
                accumulate: false)));

        charts.AddRange(MetricsVerticalGoals.Select(goal =>
            BuildGoalComparisonChart(
                key: $"{goal.Key}-accumulated-goal",
                title: $"{goal.Label}: puntaje acumulado vs meta",
                subtitle: $"Avance acumulado en {filter.ToLabel().ToLowerInvariant()} frente a la meta del a\u00f1o",
                records: FilterRecordsByVertical(records, goal.Key),
                range: goalRange,
                actualSeriesName: $"{goal.Label} acumulado",
                color: goal.Color,
                goalValue: goal.MonthlyGoal,
                goalLabel: $"Meta acumulada ({goal.MonthlyGoal:0.##} por mes)",
                accumulate: true)));

        return CreateDashboard(
            filter: filter,
            view: MetricsViewMode.Global,
            sellers: sellers,
            appliedSeller: null,
            records: records,
            charts: charts,
            granularityLabel: range.Granularity == MetricsGranularity.Month ? "Mensual" : "Mixta",
            requiresSellerSelection: false,
            emptyStateTitle: "No hay metricas globales disponibles.",
            emptyStateMessage: "Prueba con otro rango para revisar el comportamiento agregado del equipo.");
    }

    private MetricsDashboardDto BuildIndividualDashboard(
        MetricsRangeFilter filter,
        IReadOnlyList<ScoreRecordDto> allRecords,
        IReadOnlyList<MetricsSellerOptionDto> sellers,
        MetricsSellerOptionDto? appliedSeller)
    {
        if (appliedSeller is null)
        {
            return CreateDashboard(
                filter: filter,
                view: MetricsViewMode.Individual,
                sellers: sellers,
                appliedSeller: null,
                records: Array.Empty<ScoreRecordDto>(),
                charts: Array.Empty<MetricsChartDto>(),
                granularityLabel: "Pendiente",
                requiresSellerSelection: true,
                emptyStateTitle: "Selecciona un vendedor",
                emptyStateMessage: "La vista Individuales solo se habilita cuando eliges un vendedor. Despu\u00e9s te mostramos metas o graficas operativas seg\u00fan el perfil.");
        }

        var sellerRecords = allRecords
            .Where(record => string.Equals(NormalizeMetricsKey(record.SalesPerson), appliedSeller.Key, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var privilegedGoal = MetricsIndividualGoals.FirstOrDefault(goal =>
            string.Equals(goal.SellerKey, appliedSeller.Key, StringComparison.OrdinalIgnoreCase));

        if (privilegedGoal is not null)
        {
            var goalRange = BuildMetricsGoalRange(filter);
            var charts = new List<MetricsChartDto>
            {
                BuildGoalComparisonChart(
                    key: $"{appliedSeller.Key}-seller-monthly-goal",
                    title: "Puntaje por vendedor en el tiempo",
                    subtitle: $"Meta mensual de {appliedSeller.Name} en {filter.ToLabel().ToLowerInvariant()}",
                    records: sellerRecords,
                    range: goalRange,
                    actualSeriesName: appliedSeller.Name,
                    color: MetricsColorPalette[1],
                    goalValue: privilegedGoal.TotalMonthlyGoal,
                    goalLabel: $"Meta mensual {privilegedGoal.TotalMonthlyGoal:0.##}",
                    accumulate: false),
                BuildGoalComparisonChart(
                    key: $"{appliedSeller.Key}-seller-accumulated-goal",
                    title: "Puntaje acumulado por vendedor",
                    subtitle: $"Meta acumulada de {appliedSeller.Name} en {filter.ToLabel().ToLowerInvariant()}",
                    records: sellerRecords,
                    range: goalRange,
                    actualSeriesName: $"{appliedSeller.Name} acumulado",
                    color: MetricsColorPalette[1],
                    goalValue: privilegedGoal.TotalMonthlyGoal,
                    goalLabel: $"Meta acumulada ({privilegedGoal.TotalMonthlyGoal:0.##} por mes)",
                    accumulate: true),
                BuildGoalComparisonChart(
                    key: $"{appliedSeller.Key}-cloud-monthly-goal",
                    title: "Puntaje Cloud",
                    subtitle: $"Meta mensual Cloud de {appliedSeller.Name}",
                    records: FilterRecordsByVertical(sellerRecords, "cloud"),
                    range: goalRange,
                    actualSeriesName: "Cloud real",
                    color: "#145AF2",
                    goalValue: privilegedGoal.CloudMonthlyGoal,
                    goalLabel: $"Meta mensual {privilegedGoal.CloudMonthlyGoal:0.##}",
                    accumulate: false),
                BuildGoalComparisonChart(
                    key: $"{appliedSeller.Key}-copiers-monthly-goal",
                    title: "Puntaje Copiers",
                    subtitle: $"Meta mensual Copiers de {appliedSeller.Name}",
                    records: FilterRecordsByVertical(sellerRecords, "copiers"),
                    range: goalRange,
                    actualSeriesName: "Copiers real",
                    color: "#F97316",
                    goalValue: privilegedGoal.CopiersMonthlyGoal,
                    goalLabel: $"Meta mensual {privilegedGoal.CopiersMonthlyGoal:0.##}",
                    accumulate: false),
                BuildGoalComparisonChart(
                    key: $"{appliedSeller.Key}-cloud-accumulated-goal",
                    title: "Puntaje Cloud acumulado",
                    subtitle: $"Meta acumulada Cloud de {appliedSeller.Name}",
                    records: FilterRecordsByVertical(sellerRecords, "cloud"),
                    range: goalRange,
                    actualSeriesName: "Cloud acumulado",
                    color: "#145AF2",
                    goalValue: privilegedGoal.CloudMonthlyGoal,
                    goalLabel: $"Meta acumulada ({privilegedGoal.CloudMonthlyGoal:0.##} por mes)",
                    accumulate: true),
                BuildGoalComparisonChart(
                    key: $"{appliedSeller.Key}-copiers-accumulated-goal",
                    title: "Puntaje Copiers acumulado",
                    subtitle: $"Meta acumulada Copiers de {appliedSeller.Name}",
                    records: FilterRecordsByVertical(sellerRecords, "copiers"),
                    range: goalRange,
                    actualSeriesName: "Copiers acumulado",
                    color: "#F97316",
                    goalValue: privilegedGoal.CopiersMonthlyGoal,
                    goalLabel: $"Meta acumulada ({privilegedGoal.CopiersMonthlyGoal:0.##} por mes)",
                    accumulate: true)
            };

            return CreateDashboard(
                filter: filter,
                view: MetricsViewMode.Individual,
                sellers: sellers,
                appliedSeller: appliedSeller,
                records: sellerRecords,
                charts: charts,
                granularityLabel: "Mensual",
                requiresSellerSelection: false,
                emptyStateTitle: "No hay metricas individuales disponibles.",
                emptyStateMessage: "No encontramos registros del vendedor seleccionado para este rango.");
        }

        var trendRange = BuildMetricsRange(filter);
        var quarterRange = BuildQuarterlyMetricsRange(filter);
        var standardCharts = new List<MetricsChartDto>
        {
            BuildSingleTrendChart(
                key: $"{appliedSeller.Key}-total-score",
                title: "Puntaje total",
                subtitle: $"Resultado individual de {appliedSeller.Name} en {filter.ToLabel().ToLowerInvariant()}",
                records: sellerRecords,
                range: trendRange,
                seriesName: "Total",
                color: MetricsColorPalette[0]),
            BuildSingleTrendChart(
                key: $"{appliedSeller.Key}-cloud-score",
                title: "Puntaje Cloud",
                subtitle: $"Resultado Cloud de {appliedSeller.Name}",
                records: FilterRecordsByVertical(sellerRecords, "cloud"),
                range: trendRange,
                seriesName: "Cloud",
                color: "#145AF2"),
            BuildSingleTrendChart(
                key: $"{appliedSeller.Key}-copiers-score",
                title: "Puntaje Copiers",
                subtitle: $"Resultado Copiers de {appliedSeller.Name}",
                records: FilterRecordsByVertical(sellerRecords, "copiers"),
                range: trendRange,
                seriesName: "Copiers",
                color: "#F97316"),
            BuildCorporatePlaceholderChart(appliedSeller, filter, quarterRange)
        };

        return CreateDashboard(
            filter: filter,
            view: MetricsViewMode.Individual,
            sellers: sellers,
            appliedSeller: appliedSeller,
            records: sellerRecords,
            charts: standardCharts,
            granularityLabel: "Mixta",
            requiresSellerSelection: false,
            emptyStateTitle: "No hay metricas individuales disponibles.",
            emptyStateMessage: "No encontramos registros del vendedor seleccionado para este rango.");
    }

    private MetricsDashboardDto CreateDashboard(
        MetricsRangeFilter filter,
        MetricsViewMode view,
        IReadOnlyList<MetricsSellerOptionDto> sellers,
        MetricsSellerOptionDto? appliedSeller,
        IReadOnlyList<ScoreRecordDto> records,
        IReadOnlyList<MetricsChartDto> charts,
        string granularityLabel,
        bool requiresSellerSelection,
        string emptyStateTitle,
        string emptyStateMessage)
    {
        return new MetricsDashboardDto
        {
            Filter = filter.ToKey(),
            FilterLabel = filter.ToLabel(),
            View = view.ToKey(),
            ViewLabel = view.ToLabel(),
            GranularityLabel = granularityLabel,
            AppliedSellerKey = appliedSeller?.Key ?? "",
            AppliedSellerName = view == MetricsViewMode.Individual
                ? appliedSeller?.Name ?? "Selecciona un vendedor"
                : "Todos los vendedores",
            RequiresSellerSelection = requiresSellerSelection,
            EmptyStateTitle = emptyStateTitle,
            EmptyStateMessage = emptyStateMessage,
            RecordsCount = requiresSellerSelection ? 0 : records.Count,
            SellersCount = requiresSellerSelection
                ? 0
                : records.Select(record => NormalizeMetricsName(record.SalesPerson, "Sin vendedor"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
            VerticalsCount = requiresSellerSelection
                ? 0
                : records.Select(record => ResolveVerticalLabel(record.VerticalOptionValue))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
            TotalScore = requiresSellerSelection ? 0m : RoundCurrency(records.Sum(record => record.Score)),
            TotalAnnualValue = requiresSellerSelection ? 0m : RoundCurrency(records.Sum(record => record.AnnualValue)),
            Sellers = sellers,
            Charts = charts
        };
    }

    private MetricsChartDto BuildSingleTrendChart(
        string key,
        string title,
        string subtitle,
        IReadOnlyList<ScoreRecordDto> records,
        MetricsRangeDefinition range,
        string seriesName,
        string color)
    {
        var values = AggregateValues(records, range, record => record.Score);
        var annualValues = AggregateValues(records, range, record => record.AnnualValue);

        return new MetricsChartDto
        {
            Key = key,
            Title = title,
            Subtitle = subtitle,
            TotalScore = RoundCurrency(records.Sum(record => record.Score)),
            TotalAnnualValue = RoundCurrency(records.Sum(record => record.AnnualValue)),
            Categories = range.Categories.Select(category => category.DisplayLabel).ToList(),
            Series = new[]
            {
                new MetricsSeriesDto
                {
                    Key = key,
                    Name = seriesName,
                    Color = color,
                    TotalScore = RoundCurrency(records.Sum(record => record.Score)),
                    TotalAnnualValue = RoundCurrency(records.Sum(record => record.AnnualValue)),
                    Values = values,
                    AnnualValues = annualValues
                }
            }
        };
    }

    private MetricsChartDto BuildGoalComparisonChart(
        string key,
        string title,
        string subtitle,
        IReadOnlyList<ScoreRecordDto> records,
        MetricsRangeDefinition range,
        string actualSeriesName,
        string color,
        decimal goalValue,
        string goalLabel,
        bool accumulate)
    {
        var actualValues = AggregateValues(records, range, record => record.Score);
        var actualAnnualValues = AggregateValues(records, range, record => record.AnnualValue);
        var goalValues = range.Categories.Select(_ => RoundCurrency(goalValue)).ToList();

        if (accumulate)
        {
            AccumulateValues(actualValues);
            AccumulateValues(actualAnnualValues);
            AccumulateValues(goalValues);
        }

        var goalStatuses = BuildGoalStatuses(range, actualValues, goalValues);
        var finalGoalValue = goalValues.LastOrDefault();
        var comparisonLabel = accumulate ? "Meta acumulada" : "Meta mensual";

        return new MetricsChartDto
        {
            Key = key,
            Title = title,
            Subtitle = subtitle,
            GoalLabel = goalLabel,
            TotalScore = RoundCurrency(records.Sum(record => record.Score)),
            TotalAnnualValue = RoundCurrency(records.Sum(record => record.AnnualValue)),
            Categories = range.Categories.Select(category => category.DisplayLabel).ToList(),
            GoalStatuses = goalStatuses,
            Series = new[]
            {
                new MetricsSeriesDto
                {
                    Key = $"{key}-actual",
                    Name = actualSeriesName,
                    Color = color,
                    TotalScore = RoundCurrency(records.Sum(record => record.Score)),
                    TotalAnnualValue = RoundCurrency(records.Sum(record => record.AnnualValue)),
                    Values = actualValues,
                    AnnualValues = actualAnnualValues
                },
                new MetricsSeriesDto
                {
                    Key = $"{key}-goal",
                    Name = comparisonLabel,
                    Color = "#94A3B8",
                    IsReference = true,
                    StrokeDasharray = "8 6",
                    LegendNote = $"{comparisonLabel} {finalGoalValue:0.##}",
                    TotalScore = finalGoalValue,
                    TotalAnnualValue = 0m,
                    Values = goalValues,
                    AnnualValues = Array.Empty<decimal>()
                }
            }
        };
    }

    private MetricsChartDto BuildCorporatePlaceholderChart(
        MetricsSellerOptionDto seller,
        MetricsRangeFilter filter,
        MetricsRangeDefinition quarterRange)
    {
        var actualValues = quarterRange.Categories.Select(_ => 0m).ToList();
        var goalValues = quarterRange.Categories.Select(_ => 2m).ToList();

        return new MetricsChartDto
        {
            Key = $"{seller.Key}-corporate-placeholder",
            Title = "Negocios Corporate",
            Subtitle = $"Meta anual 8, distribuida en 2 por quarter para {seller.Name}",
            GoalLabel = "Meta anual 8, 2 x quarter",
            TotalScore = 0m,
            TotalAnnualValue = 0m,
            Categories = quarterRange.Categories.Select(category => category.DisplayLabel).ToList(),
            GoalStatuses = BuildGoalStatuses(quarterRange, actualValues, goalValues),
            Series = new[]
            {
                new MetricsSeriesDto
                {
                    Key = $"{seller.Key}-corporate-actual",
                    Name = "Corporate real",
                    Color = "#7C3AED",
                    TotalScore = 0m,
                    TotalAnnualValue = 0m,
                    Values = actualValues,
                    AnnualValues = Array.Empty<decimal>()
                },
                new MetricsSeriesDto
                {
                    Key = $"{seller.Key}-corporate-goal",
                    Name = "Meta quarter",
                    Color = "#94A3B8",
                    IsReference = true,
                    StrokeDasharray = "8 6",
                    LegendNote = "Meta quarter 2",
                    TotalScore = 8m,
                    TotalAnnualValue = 0m,
                    Values = goalValues,
                    AnnualValues = Array.Empty<decimal>()
                }
            }
        };
    }

    private List<decimal> AggregateValues(
        IReadOnlyList<ScoreRecordDto> records,
        MetricsRangeDefinition range,
        Func<ScoreRecordDto, decimal> selector)
    {
        return range.Categories
            .Select(category => RoundCurrency(records
                .Where(record => string.Equals(GetMetricsBucketKey(record.ContractStartDateValue, range.Granularity), category.Key, StringComparison.OrdinalIgnoreCase))
                .Sum(selector)))
            .ToList();
    }

    private static void AccumulateValues(IList<decimal> values)
    {
        for (var index = 1; index < values.Count; index++)
        {
            values[index] = values[index - 1] + values[index];
        }
    }

    private List<MetricsGoalStatusDto> BuildGoalStatuses(
        MetricsRangeDefinition range,
        IReadOnlyList<decimal> actualValues,
        IReadOnlyList<decimal> goalValues)
    {
        var currentMonth = new DateOnly(GetBogotaToday().Year, GetBogotaToday().Month, 1);
        var statuses = new List<MetricsGoalStatusDto>(range.Categories.Count);

        for (var index = 0; index < range.Categories.Count; index++)
        {
            var actualValue = index < actualValues.Count ? actualValues[index] : 0m;
            var goalValue = index < goalValues.Count ? goalValues[index] : 0m;
            var status = ResolveGoalStatus(range.Categories[index].Key, actualValue, goalValue, currentMonth);

            statuses.Add(new MetricsGoalStatusDto
            {
                Category = range.Categories[index].DisplayLabel,
                ActualValue = actualValue,
                TargetValue = goalValue,
                IsMet = status.IsMet,
                StatusTone = status.Tone,
                StatusLabel = status.Label
            });
        }

        return statuses;
    }

    private static IReadOnlyList<ScoreRecordDto> FilterRecordsByVertical(IReadOnlyList<ScoreRecordDto> records, string verticalKey)
    {
        return records
            .Where(record => string.Equals(
                NormalizeMetricsKey(ResolveVerticalLabel(record.VerticalOptionValue)),
                verticalKey,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static (bool IsMet, string Tone, string Label) ResolveGoalStatus(
        string categoryKey,
        decimal actualValue,
        decimal targetValue,
        DateOnly currentMonth)
    {
        if (DateOnly.TryParseExact(categoryKey, "yyyy-MM-01", CultureInfo.InvariantCulture, DateTimeStyles.None, out var categoryMonth))
        {
            if (categoryMonth > currentMonth)
                return (false, "upcoming", "Pendiente");

            if (categoryMonth == currentMonth)
                return (actualValue >= targetValue, "in-progress", "En curso");

            return actualValue >= targetValue
                ? (true, "met", "Cumplido")
                : (false, "missed", "No cumplido");
        }

        if (TryParseQuarterCategory(categoryKey, out var categoryYear, out var categoryQuarter))
        {
            var currentQuarter = ((currentMonth.Month - 1) / 3) + 1;
            if (categoryYear > currentMonth.Year || (categoryYear == currentMonth.Year && categoryQuarter > currentQuarter))
                return (false, "upcoming", "Pendiente");

            if (categoryYear == currentMonth.Year && categoryQuarter == currentQuarter)
                return (actualValue >= targetValue, "in-progress", "En curso");

            return actualValue >= targetValue
                ? (true, "met", "Cumplido")
                : (false, "missed", "No cumplido");
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

    private MetricsRangeDefinition BuildQuarterlyMetricsRange(MetricsRangeFilter filter)
    {
        var today = GetBogotaToday();
        var year = filter == MetricsRangeFilter.PreviousYear ? today.Year - 1 : today.Year;
        var categories = Enumerable.Range(1, 4)
            .Select(quarter => new MetricsCategory(
                Key: $"{year}-Q{quarter}",
                DisplayLabel: $"Q{quarter}"))
            .ToList();

        return new MetricsRangeDefinition(
            new DateOnly(year, 1, 1),
            new DateOnly(year + 1, 1, 1),
            MetricsGranularity.Quarter,
            categories);
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

        return granularity switch
        {
            MetricsGranularity.Day => contractDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            MetricsGranularity.Quarter => $"{contractDate.Year}-Q{((contractDate.Month - 1) / 3) + 1}",
            _ => new DateOnly(contractDate.Year, contractDate.Month, 1).ToString("yyyy-MM-01", CultureInfo.InvariantCulture)
        };
    }

    private static bool TryParseQuarterCategory(string value, out int year, out int quarter)
    {
        year = 0;
        quarter = 0;

        var parts = value.Split("-Q", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;

        return int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out year)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out quarter)
            && quarter is >= 1 and <= 4;
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
        Month = 1,
        Quarter = 2
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

    private sealed record MetricsIndividualGoalDefinition(
        string SellerKey,
        string SellerName,
        decimal TotalMonthlyGoal,
        decimal CloudMonthlyGoal,
        decimal CopiersMonthlyGoal);
}
