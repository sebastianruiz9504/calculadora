using System.Globalization;
using System.Text.Json;
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

    private static readonly MetricsIndividualGoalDefinition DefaultIndividualGoal =
        new(125m, 90m, 35m);

    public async Task<MetricsDashboardDto> GetMetricsDashboardAsync(
        MetricsRangeFilter filter,
        MetricsViewMode view,
        MetricsPeriodGranularity period,
        string? sellerKey = null,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var effectivePeriod = filter == MetricsRangeFilter.ThisMonth
            ? MetricsPeriodGranularity.Month
            : period;
        var requestedRange = BuildMetricsRange(filter, effectivePeriod, Array.Empty<ScoreRecordDto>());
        var relativeUrl = filter == MetricsRangeFilter.All
            ? $"/api/data/v9.2/{_scoresTableSetName}?$orderby={_scoresContractStartDateField} asc"
            : BuildMetricsFetchUrl(requestedRange.StartInclusive.AddYears(-1), requestedRange.EndExclusive);
        var rawRecords = await GetDataverseEntitiesAsync(relativeUrl, httpContext.User, ct, AddFormattedValueHeaders);

        var allRecords = rawRecords
            .Select(ParseMetricsScoreRecord)
            .Where(item => item is not null)
            .Cast<ScoreRecordDto>()
            .OrderBy(item => item.ContractStartDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allNewBusinessRecords = allRecords
            .Where(IsNewBusinessMetricsRecord)
            .ToList();

        var displayRange = BuildMetricsRange(filter, effectivePeriod, allNewBusinessRecords);
        var displayRecords = FilterMetricsRecordsByRange(allNewBusinessRecords, displayRange.StartInclusive, displayRange.EndExclusive);

        var sellers = displayRecords
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
            ? BuildIndividualDashboard(filter, effectivePeriod, displayRange, displayRecords, allNewBusinessRecords, sellers, appliedSeller)
            : BuildGlobalDashboard(filter, effectivePeriod, displayRange, displayRecords, allNewBusinessRecords, sellers);
    }

    private string BuildMetricsFetchUrl(DateOnly startInclusive, DateOnly endExclusive)
    {
        var filterExpression = $"{_scoresContractStartDateField} ge {startInclusive:yyyy-MM-dd} and {_scoresContractStartDateField} lt {endExclusive:yyyy-MM-dd}";
        return $"/api/data/v9.2/{_scoresTableSetName}?$filter={Uri.EscapeDataString(filterExpression)}&$orderby={_scoresContractStartDateField} asc";
    }

    private MetricsDashboardDto BuildGlobalDashboard(
        MetricsRangeFilter filter,
        MetricsPeriodGranularity period,
        MetricsRangeDefinition range,
        IReadOnlyList<ScoreRecordDto> records,
        IReadOnlyList<ScoreRecordDto> comparisonRecords,
        IReadOnlyList<MetricsSellerOptionDto> sellers)
    {
        var periodLabel = period.ToLabel().ToLowerInvariant();
        var charts = new List<MetricsChartDto>
        {
            BuildSingleTrendChart(
                key: "global-total-score",
                title: "Puntaje total",
                subtitle: $"Puntaje total en {filter.ToLabel().ToLowerInvariant()}",
                records: records,
                comparisonRecords: comparisonRecords,
                range: range,
                seriesName: "Total",
                color: MetricsColorPalette[0])
        };

        charts.AddRange(MetricsVerticalGoals.Select(goal =>
            BuildGoalComparisonChart(
                key: $"{goal.Key}-monthly-goal",
                title: $"{goal.Label}: puntaje {periodLabel} vs meta",
                subtitle: $"Seguimiento {periodLabel} en {filter.ToLabel().ToLowerInvariant()} frente a la meta del periodo",
                records: FilterRecordsByVertical(records, goal.Key),
                comparisonRecords: FilterRecordsByVertical(comparisonRecords, goal.Key),
                range: range,
                actualSeriesName: $"{goal.Label} real",
                color: goal.Color,
                goalValue: goal.MonthlyGoal,
                accumulate: false)));

        charts.AddRange(MetricsVerticalGoals.Select(goal =>
            BuildGoalComparisonChart(
                key: $"{goal.Key}-accumulated-goal",
                title: $"{goal.Label}: puntaje acumulado vs meta",
                subtitle: $"Avance acumulado en {filter.ToLabel().ToLowerInvariant()} frente a la meta del a\u00f1o",
                records: FilterRecordsByVertical(records, goal.Key),
                comparisonRecords: FilterRecordsByVertical(comparisonRecords, goal.Key),
                range: range,
                actualSeriesName: $"{goal.Label} acumulado",
                color: goal.Color,
                goalValue: goal.MonthlyGoal,
                accumulate: true)));

        return CreateDashboard(
            filter: filter,
            view: MetricsViewMode.Global,
            period: period,
            sellers: sellers,
            appliedSeller: null,
            records: records,
            charts: charts,
            granularityLabel: period.ToLabel(),
            requiresSellerSelection: false,
            emptyStateTitle: "No hay metricas globales disponibles.",
            emptyStateMessage: "Prueba con otro rango para revisar el comportamiento agregado del equipo.");
    }

    private ScoreRecordDto? ParseMetricsScoreRecord(JsonElement item)
    {
        var record = ParseScoreRecord(item);
        if (record is null)
            return null;

        var storedScore = ReadDecimal(item, _scoresScoreField);
        if (storedScore.HasValue)
            record.Score = RoundCurrency(storedScore.Value);

        return record;
    }

    private MetricsDashboardDto BuildIndividualDashboard(
        MetricsRangeFilter filter,
        MetricsPeriodGranularity period,
        MetricsRangeDefinition range,
        IReadOnlyList<ScoreRecordDto> displayRecords,
        IReadOnlyList<ScoreRecordDto> comparisonRecords,
        IReadOnlyList<MetricsSellerOptionDto> sellers,
        MetricsSellerOptionDto? appliedSeller)
    {
        if (appliedSeller is null)
        {
            return CreateDashboard(
                filter: filter,
                view: MetricsViewMode.Individual,
                period: period,
                sellers: sellers,
                appliedSeller: null,
                records: Array.Empty<ScoreRecordDto>(),
                charts: Array.Empty<MetricsChartDto>(),
                granularityLabel: "Pendiente",
                requiresSellerSelection: true,
                emptyStateTitle: "Selecciona un vendedor",
                emptyStateMessage: "La vista Individuales solo se habilita cuando eliges un vendedor. Despu\u00e9s te mostramos las metas individuales estandarizadas.");
        }

        var sellerRecords = displayRecords
            .Where(record => string.Equals(NormalizeMetricsKey(record.SalesPerson), appliedSeller.Key, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var sellerComparisonRecords = comparisonRecords
            .Where(record => string.Equals(NormalizeMetricsKey(record.SalesPerson), appliedSeller.Key, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var individualGoal = DefaultIndividualGoal;
        var periodLabel = period.ToLabel().ToLowerInvariant();
        var charts = new List<MetricsChartDto>
        {
            BuildGoalComparisonChart(
                key: $"{appliedSeller.Key}-seller-monthly-goal",
                title: "Puntaje por vendedor en el tiempo",
                subtitle: $"Meta {periodLabel} de {appliedSeller.Name} en {filter.ToLabel().ToLowerInvariant()}",
                records: sellerRecords,
                comparisonRecords: sellerComparisonRecords,
                range: range,
                actualSeriesName: appliedSeller.Name,
                color: MetricsColorPalette[1],
                goalValue: individualGoal.TotalMonthlyGoal,
                accumulate: false),
            BuildGoalComparisonChart(
                key: $"{appliedSeller.Key}-seller-accumulated-goal",
                title: "Puntaje acumulado por vendedor",
                subtitle: $"Meta acumulada de {appliedSeller.Name} en {filter.ToLabel().ToLowerInvariant()}",
                records: sellerRecords,
                comparisonRecords: sellerComparisonRecords,
                range: range,
                actualSeriesName: $"{appliedSeller.Name} acumulado",
                color: MetricsColorPalette[1],
                goalValue: individualGoal.TotalMonthlyGoal,
                accumulate: true),
            BuildGoalComparisonChart(
                key: $"{appliedSeller.Key}-cloud-monthly-goal",
                title: "Puntaje Cloud",
                subtitle: $"Meta {periodLabel} Cloud de {appliedSeller.Name}",
                records: FilterRecordsByVertical(sellerRecords, "cloud"),
                comparisonRecords: FilterRecordsByVertical(sellerComparisonRecords, "cloud"),
                range: range,
                actualSeriesName: "Cloud real",
                color: "#145AF2",
                goalValue: individualGoal.CloudMonthlyGoal,
                accumulate: false),
            BuildGoalComparisonChart(
                key: $"{appliedSeller.Key}-copiers-monthly-goal",
                title: "Puntaje Copiers",
                subtitle: $"Meta {periodLabel} Copiers de {appliedSeller.Name}",
                records: FilterRecordsByVertical(sellerRecords, "copiers"),
                comparisonRecords: FilterRecordsByVertical(sellerComparisonRecords, "copiers"),
                range: range,
                actualSeriesName: "Copiers real",
                color: "#F97316",
                goalValue: individualGoal.CopiersMonthlyGoal,
                accumulate: false),
            BuildGoalComparisonChart(
                key: $"{appliedSeller.Key}-cloud-accumulated-goal",
                title: "Puntaje Cloud acumulado",
                subtitle: $"Meta acumulada Cloud de {appliedSeller.Name}",
                records: FilterRecordsByVertical(sellerRecords, "cloud"),
                comparisonRecords: FilterRecordsByVertical(sellerComparisonRecords, "cloud"),
                range: range,
                actualSeriesName: "Cloud acumulado",
                color: "#145AF2",
                goalValue: individualGoal.CloudMonthlyGoal,
                accumulate: true),
            BuildGoalComparisonChart(
                key: $"{appliedSeller.Key}-copiers-accumulated-goal",
                title: "Puntaje Copiers acumulado",
                subtitle: $"Meta acumulada Copiers de {appliedSeller.Name}",
                records: FilterRecordsByVertical(sellerRecords, "copiers"),
                comparisonRecords: FilterRecordsByVertical(sellerComparisonRecords, "copiers"),
                range: range,
                actualSeriesName: "Copiers acumulado",
                color: "#F97316",
                goalValue: individualGoal.CopiersMonthlyGoal,
                accumulate: true)
        };

        return CreateDashboard(
            filter: filter,
            view: MetricsViewMode.Individual,
            period: period,
            sellers: sellers,
            appliedSeller: appliedSeller,
            records: sellerRecords,
            charts: charts,
            granularityLabel: period.ToLabel(),
            requiresSellerSelection: false,
            emptyStateTitle: "No hay metricas individuales disponibles.",
            emptyStateMessage: "No encontramos registros del vendedor seleccionado para este rango.");
    }

    private MetricsDashboardDto CreateDashboard(
        MetricsRangeFilter filter,
        MetricsViewMode view,
        MetricsPeriodGranularity period,
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
            Period = period.ToKey(),
            PeriodLabel = period.ToLabel(),
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
        IReadOnlyList<ScoreRecordDto> comparisonRecords,
        MetricsRangeDefinition range,
        string seriesName,
        string color)
    {
        var values = AggregateValues(records, range, record => record.Score);
        var annualValues = AggregateValues(records, range, record => record.AnnualValue);
        var previousYearValues = AggregatePreviousYearValues(comparisonRecords, range, record => record.Score);
        var detailGroups = BuildPeriodDetailGroups(records, range, accumulate: false);

        return new MetricsChartDto
        {
            Key = key,
            Title = title,
            Subtitle = subtitle,
            TotalScore = RoundCurrency(records.Sum(record => record.Score)),
            TotalAnnualValue = RoundCurrency(records.Sum(record => record.AnnualValue)),
            Categories = range.Categories.Select(category => category.DisplayLabel).ToList(),
            GoalStatuses = BuildGoalStatuses(range, values, null, previousYearValues, detailGroups),
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
        IReadOnlyList<ScoreRecordDto> comparisonRecords,
        MetricsRangeDefinition range,
        string actualSeriesName,
        string color,
        decimal goalValue,
        bool accumulate)
    {
        var actualValues = AggregateValues(records, range, record => record.Score);
        var actualAnnualValues = AggregateValues(records, range, record => record.AnnualValue);
        var previousYearValues = AggregatePreviousYearValues(comparisonRecords, range, record => record.Score);
        var goalValues = range.Categories
            .Select(category => RoundCurrency(goalValue * GetCategoryMonthCount(category)))
            .ToList();
        var detailGroups = BuildPeriodDetailGroups(records, range, accumulate);

        if (accumulate)
        {
            AccumulateValuesByYear(actualValues, range.Categories);
            AccumulateValuesByYear(actualAnnualValues, range.Categories);
            AccumulateValuesByYear(previousYearValues, range.Categories);
            AccumulateValuesByYear(goalValues, range.Categories);
        }

        var goalStatuses = BuildGoalStatuses(range, actualValues, goalValues, previousYearValues, detailGroups);
        var finalGoalValue = goalValues.LastOrDefault();
        var comparisonLabel = accumulate ? "Meta acumulada" : $"Meta {range.Granularity.ToLabel().ToLowerInvariant()}";
        var goalLabel = accumulate
            ? $"Meta acumulada ({goalValue:0.##} por mes)"
            : $"Meta {range.Granularity.ToLabel().ToLowerInvariant()} ({goalValue:0.##} por mes)";

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

    private List<decimal> AggregateValues(
        IReadOnlyList<ScoreRecordDto> records,
        MetricsRangeDefinition range,
        Func<ScoreRecordDto, decimal> selector)
    {
        return range.Categories
            .Select(category => RoundCurrency(FilterMetricsRecordsByRange(records, category.StartInclusive, category.EndExclusive).Sum(selector)))
            .ToList();
    }

    private List<decimal> AggregatePreviousYearValues(
        IReadOnlyList<ScoreRecordDto> records,
        MetricsRangeDefinition range,
        Func<ScoreRecordDto, decimal> selector)
    {
        return range.Categories
            .Select(category => RoundCurrency(FilterMetricsRecordsByRange(
                records,
                category.StartInclusive.AddYears(-1),
                category.EndExclusive.AddYears(-1)).Sum(selector)))
            .ToList();
    }

    private static IReadOnlyList<ScoreRecordDto> FilterMetricsRecordsByRange(
        IReadOnlyList<ScoreRecordDto> records,
        DateOnly startInclusive,
        DateOnly endExclusive)
    {
        return records
            .Where(record => TryParseDateOnly(record.ContractStartDateValue, out var date)
                && date >= startInclusive
                && date < endExclusive)
            .ToList();
    }

    private List<IReadOnlyList<MetricsBusinessDetailDto>> BuildPeriodDetailGroups(
        IReadOnlyList<ScoreRecordDto> records,
        MetricsRangeDefinition range,
        bool accumulate)
    {
        var groups = new List<IReadOnlyList<MetricsBusinessDetailDto>>(range.Categories.Count);
        var accumulatedRecords = new List<ScoreRecordDto>();
        int? accumulatedYear = null;

        foreach (var category in range.Categories)
        {
            var periodRecords = FilterMetricsRecordsByRange(records, category.StartInclusive, category.EndExclusive);
            var detailRecords = periodRecords;
            if (accumulate)
            {
                if (accumulatedYear != category.StartInclusive.Year)
                {
                    accumulatedRecords.Clear();
                    accumulatedYear = category.StartInclusive.Year;
                }

                accumulatedRecords.AddRange(periodRecords);
                detailRecords = accumulatedRecords;
            }

            groups.Add(detailRecords
                .OrderBy(record => record.ContractStartDateValue, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.ClientName, StringComparer.OrdinalIgnoreCase)
                .Select(BuildMetricsBusinessDetail)
                .ToList());
        }

        return groups;
    }

    private MetricsBusinessDetailDto BuildMetricsBusinessDetail(ScoreRecordDto record)
    {
        var detailParts = new List<string>();
        var productNames = record.ProductLines
            .Select(line => NormalizeMetricsName(line.ProductName, ""))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (productNames.Count > 0)
            detailParts.Add(string.Join(", ", productNames));

        var vertical = ResolveVerticalLabel(record.VerticalOptionValue);
        if (!string.Equals(vertical, "Sin vertical", StringComparison.OrdinalIgnoreCase))
            detailParts.Add(vertical);

        if (!string.IsNullOrWhiteSpace(record.ContractKindLabel))
            detailParts.Add(record.ContractKindLabel.Trim());

        if (!string.IsNullOrWhiteSpace(record.Offer)
            && !string.Equals(record.Offer, "Sin oferta", StringComparison.OrdinalIgnoreCase)
            && !Guid.TryParse(record.Offer, out _))
        {
            detailParts.Add(record.Offer.Trim());
        }

        return new MetricsBusinessDetailDto
        {
            RecordId = record.RecordId,
            ClientName = NormalizeMetricsName(record.ClientName, "Cliente sin asignar"),
            Score = RoundCurrency(record.Score),
            ContractValue = RoundCurrency(record.AnnualValue),
            ContractStartDateValue = record.ContractStartDateValue,
            ContractStartDateDisplay = string.IsNullOrWhiteSpace(record.ContractStartDateDisplay)
                ? record.ContractStartDateValue
                : record.ContractStartDateDisplay,
            Detail = detailParts.Count > 0
                ? string.Join(" | ", detailParts.Distinct(StringComparer.OrdinalIgnoreCase))
                : "Sin detalle registrado"
        };
    }

    private static void AccumulateValuesByYear(
        IList<decimal> values,
        IReadOnlyList<MetricsCategory> categories)
    {
        for (var index = 1; index < values.Count; index++)
        {
            if (index < categories.Count
                && categories[index].StartInclusive.Year == categories[index - 1].StartInclusive.Year)
            {
                values[index] = values[index - 1] + values[index];
            }
        }
    }

    private List<MetricsGoalStatusDto> BuildGoalStatuses(
        MetricsRangeDefinition range,
        IReadOnlyList<decimal> actualValues,
        IReadOnlyList<decimal>? goalValues,
        IReadOnlyList<decimal> previousYearValues,
        IReadOnlyList<IReadOnlyList<MetricsBusinessDetailDto>> detailGroups)
    {
        var statuses = new List<MetricsGoalStatusDto>(range.Categories.Count);

        for (var index = 0; index < range.Categories.Count; index++)
        {
            var actualValue = index < actualValues.Count ? actualValues[index] : 0m;
            var previousYearValue = index < previousYearValues.Count ? previousYearValues[index] : 0m;
            var hasTarget = goalValues is not null;
            var goalValue = hasTarget && index < goalValues!.Count ? goalValues[index] : 0m;
            var details = index < detailGroups.Count
                ? detailGroups[index]
                : Array.Empty<MetricsBusinessDetailDto>();
            var status = hasTarget
                ? ResolveGoalStatus(range.Categories[index], actualValue, goalValue)
                : range.Categories[index].StartInclusive > GetBogotaToday()
                    ? (IsMet: false, Tone: "upcoming", Label: "Pendiente")
                    : (IsMet: false, Tone: "neutral", Label: details.Count == 1 ? "1 negocio" : $"{details.Count} negocios");

            statuses.Add(new MetricsGoalStatusDto
            {
                CategoryKey = range.Categories[index].Key,
                Category = range.Categories[index].DisplayLabel,
                ActualValue = actualValue,
                TargetValue = goalValue,
                PreviousYearValue = previousYearValue,
                GrowthPercent = CalculateMetricsGrowthPercent(actualValue, previousYearValue),
                HasTarget = hasTarget,
                IsMet = status.IsMet,
                StatusTone = status.Tone,
                StatusLabel = status.Label,
                Details = details
            });
        }

        return statuses;
    }

    internal static decimal? CalculateMetricsGrowthPercent(decimal actualValue, decimal previousYearValue)
    {
        if (previousYearValue == 0m)
            return null;

        return Math.Round(
            ((actualValue - previousYearValue) / Math.Abs(previousYearValue)) * 100m,
            2,
            MidpointRounding.AwayFromZero);
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

    private static bool IsNewBusinessMetricsRecord(ScoreRecordDto record) =>
        !IsRenewalContractKind(record.ContractOptionValue);

    private static (bool IsMet, string Tone, string Label) ResolveGoalStatus(
        MetricsCategory category,
        decimal actualValue,
        decimal targetValue)
    {
        var today = GetBogotaToday();
        if (category.StartInclusive > today)
            return (false, "upcoming", "Pendiente");

        if (today < category.EndExclusive)
            return (actualValue >= targetValue, "in-progress", "En curso");

        return actualValue >= targetValue
            ? (true, "met", "Cumplido")
            : (false, "missed", "No cumplido");
    }

    private MetricsRangeDefinition BuildMetricsRange(
        MetricsRangeFilter filter,
        MetricsPeriodGranularity granularity,
        IReadOnlyList<ScoreRecordDto> records)
    {
        var today = GetBogotaToday();
        if (filter == MetricsRangeFilter.ThisMonth)
        {
            var monthStart = new DateOnly(today.Year, today.Month, 1);
            return BuildMetricsPeriodRange(monthStart, monthStart.AddMonths(1), MetricsPeriodGranularity.Month, includeYearInLabels: false);
        }

        if (filter == MetricsRangeFilter.All)
        {
            var recordDates = records
                .Select(record => TryParseDateOnly(record.ContractStartDateValue, out var date) ? date : (DateOnly?)null)
                .Where(date => date.HasValue)
                .Select(date => date!.Value)
                .ToList();
            var firstYear = recordDates.Count > 0 ? recordDates.Min().Year : today.Year;
            var lastYear = recordDates.Count > 0 ? recordDates.Max().Year : today.Year;
            return BuildMetricsPeriodRange(
                new DateOnly(firstYear, 1, 1),
                new DateOnly(lastYear + 1, 1, 1),
                granularity,
                includeYearInLabels: true);
        }

        var year = filter == MetricsRangeFilter.PreviousYear ? today.Year - 1 : today.Year;
        return BuildMetricsPeriodRange(
            new DateOnly(year, 1, 1),
            new DateOnly(year + 1, 1, 1),
            granularity,
            includeYearInLabels: false);
    }

    private static MetricsRangeDefinition BuildMetricsPeriodRange(
        DateOnly startInclusive,
        DateOnly endExclusive,
        MetricsPeriodGranularity granularity,
        bool includeYearInLabels)
    {
        var categories = new List<MetricsCategory>();
        var monthsPerPeriod = granularity.MonthsPerPeriod();
        for (var date = startInclusive; date < endExclusive; date = date.AddMonths(monthsPerPeriod))
        {
            var categoryEnd = date.AddMonths(monthsPerPeriod);
            if (categoryEnd > endExclusive)
                categoryEnd = endExclusive;

            categories.Add(new MetricsCategory(
                Key: date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DisplayLabel: BuildMetricsCategoryLabel(date, granularity, includeYearInLabels),
                StartInclusive: date,
                EndExclusive: categoryEnd));
        }

        return new MetricsRangeDefinition(startInclusive, endExclusive, granularity, categories);
    }

    private static string BuildMetricsCategoryLabel(
        DateOnly start,
        MetricsPeriodGranularity granularity,
        bool includeYear)
    {
        var label = granularity switch
        {
            MetricsPeriodGranularity.Quarter => $"{((start.Month - 1) / 3) + 1}.\u00ba trimestre",
            MetricsPeriodGranularity.Semester => $"{((start.Month - 1) / 6) + 1}.\u00ba semestre",
            MetricsPeriodGranularity.Year => start.Year.ToString(CultureInfo.InvariantCulture),
            _ => start.ToString("MMM", CultureInfo.GetCultureInfo("es-CO"))
        };

        return includeYear && granularity != MetricsPeriodGranularity.Year
            ? $"{label} {start.Year}"
            : label;
    }

    private static int GetCategoryMonthCount(MetricsCategory category)
    {
        return ((category.EndExclusive.Year - category.StartInclusive.Year) * 12)
            + category.EndExclusive.Month
            - category.StartInclusive.Month;
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

    private sealed record MetricsCategory(
        string Key,
        string DisplayLabel,
        DateOnly StartInclusive,
        DateOnly EndExclusive);

    private sealed record MetricsRangeDefinition(
        DateOnly StartInclusive,
        DateOnly EndExclusive,
        MetricsPeriodGranularity Granularity,
        IReadOnlyList<MetricsCategory> Categories);

    private sealed record MetricsVerticalGoalDefinition(
        string Key,
        string Label,
        decimal MonthlyGoal,
        string Color);

    private sealed record MetricsIndividualGoalDefinition(
        decimal TotalMonthlyGoal,
        decimal CloudMonthlyGoal,
        decimal CopiersMonthlyGoal);
}
