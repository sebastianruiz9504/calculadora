using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CotizadorInterno.Web.Models.SoporteCloud;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string SoporteCloudTrainingLogicalName = "cr07a_capacitacion";
    private const string SoporteCloudTrainingFallbackEntitySetName = "cr07a_capacitacions";
    private const string SoporteCloudTrainingFallbackIdField = "cr07a_capacitacionid";
    private const string SoporteCloudTrainingFallbackPrimaryNameField = "cr07a_name";
    private const string SoporteCloudTrainingDateField = "cr07a_fecha";
    private const string SoporteCloudTrainingDurationMinutesField = "cr07a_duracionhoras";
    private const string SoporteCloudTrainingClientField = "cr07a_cliente";
    private const string SoporteCloudTrainingAttendeesField = "cr07a_cantidadasistentes";
    private const string SoporteCloudTrainingTopicField = "cr07a_tema";
    private const string SoporteCloudTrainingOwnerField = "ownerid";
    private const string SoporteCloudTrainingModifiedOnField = "modifiedon";

    private static readonly IReadOnlyList<SoporteCloudOptionDto> SoporteCloudTrainingTopicFallbackOptions =
        new[]
        {
            new SoporteCloudOptionDto { Value = 645250000, Label = "Sharepoint o Onedrive" },
            new SoporteCloudOptionDto { Value = 645250001, Label = "Correo electronico" },
            new SoporteCloudOptionDto { Value = 645250002, Label = "Teams" },
            new SoporteCloudOptionDto { Value = 645250003, Label = "Forms" },
            new SoporteCloudOptionDto { Value = 645250004, Label = "Seguridad" },
            new SoporteCloudOptionDto { Value = 645250005, Label = "Bookings" },
            new SoporteCloudOptionDto { Value = 645250006, Label = "Excel" },
            new SoporteCloudOptionDto { Value = 645250007, Label = "Power Bi" },
            new SoporteCloudOptionDto { Value = 645250009, Label = "Copilot" },
            new SoporteCloudOptionDto { Value = 645250008, Label = "Otros" }
        };

    private readonly ConcurrentDictionary<string, SoporteCloudTrainingMetadata> _soporteCloudTrainingMetadataCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<SoporteCloudTrainingsBoardDto> GetSoporteCloudTrainingsBoardAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveSoporteCloudTrainingMetadataAsync(httpContext.User, ct);
        var allRows = await LoadSoporteCloudTrainingRowsAsync(metadata, httpContext.User, ct);
        var (resolvedStartDate, resolvedEndDate) = ResolveSoporteCloudDateRange(startDate, endDate);
        var filteredRows = allRows
            .Where(row => IsSoporteCloudTrainingRowInRange(row, resolvedStartDate, resolvedEndDate))
            .ToList();

        var ownerSummaries = filteredRows
            .GroupBy(
                row => BuildDashboardGroupKey(row.OwnerId, row.OwnerName),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new SoporteCloudTrainingOwnerSummaryDto
                {
                    OwnerId = first.OwnerId,
                    OwnerName = FirstNonEmpty(first.OwnerName, "Sin owner"),
                    TotalTrainings = group.Count(),
                    TotalHours = RoundTrainingHours(group.Sum(item => item.DurationMinutes)),
                    TotalAttendees = group.Sum(item => item.Attendees)
                };
            })
            .OrderByDescending(item => item.TotalTrainings)
            .ThenByDescending(item => item.TotalHours)
            .ThenBy(item => item.OwnerName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalClients = filteredRows
            .Select(row => BuildDashboardGroupKey(row.ClientId, row.ClientName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(key => !string.Equals(key, "label:empty", StringComparison.OrdinalIgnoreCase));

        return new SoporteCloudTrainingsBoardDto
        {
            StartDateValue = resolvedStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EndDateValue = resolvedEndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateRangeLabel = $"{resolvedStartDate:dd/MM/yyyy} - {resolvedEndDate:dd/MM/yyyy}",
            TotalTrainings = filteredRows.Count,
            TotalHoursDelivered = RoundTrainingHours(filteredRows.Sum(item => item.DurationMinutes)),
            TotalClients = totalClients,
            TotalAttendees = filteredRows.Sum(item => item.Attendees),
            Message = filteredRows.Count == 0
                ? "No encontramos capacitaciones en el rango seleccionado."
                : $"Se cargaron {filteredRows.Count} capacitacion(es).",
            Records = filteredRows,
            OwnerSummaries = ownerSummaries,
            TopicBreakdowns = BuildSoporteCloudTrainingBreakdowns(
                filteredRows,
                row => row.TopicValue?.ToString(CultureInfo.InvariantCulture),
                row => row.TopicLabel,
                "Sin tema"),
            ClientBreakdowns = BuildSoporteCloudTrainingBreakdowns(
                filteredRows,
                row => row.ClientId,
                row => row.ClientName,
                "Sin cliente"),
            TimeSeries = BuildSoporteCloudTrainingTimeSeries(filteredRows, resolvedStartDate, resolvedEndDate),
            TopicOptions = metadata.TopicOptions
        };
    }

    private async Task<SoporteCloudTrainingMetadata> ResolveSoporteCloudTrainingMetadataAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        const string cacheKey = SoporteCloudTrainingLogicalName;
        if (_soporteCloudTrainingMetadataCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var baseMetadata = await ResolveRhEntityMetadataAsync(
            SoporteCloudTrainingLogicalName,
            SoporteCloudTrainingFallbackEntitySetName,
            SoporteCloudTrainingFallbackIdField,
            SoporteCloudTrainingFallbackPrimaryNameField,
            user,
            ct);

        var topicOptions = await LoadSoporteCloudTrainingOptionsFromMetadataAsync(SoporteCloudTrainingTopicField, user, ct);
        if (topicOptions.Count == 0)
            topicOptions = SoporteCloudTrainingTopicFallbackOptions;

        var resolved = new SoporteCloudTrainingMetadata
        {
            BaseMetadata = baseMetadata,
            TopicOptions = topicOptions
        };

        _soporteCloudTrainingMetadataCache[cacheKey] = resolved;
        return resolved;
    }

    private async Task<IReadOnlyList<SoporteCloudTrainingRowDto>> LoadSoporteCloudTrainingRowsAsync(
        SoporteCloudTrainingMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var relativeUrl =
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}?$select={BuildSoporteCloudTrainingSelectClause(metadata)}" +
            $"&$orderby={SoporteCloudTrainingDateField} desc,{SoporteCloudTrainingModifiedOnField} desc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => BuildSoporteCloudTrainingRowDto(metadata, item))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderByDescending(item => item.DateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildSoporteCloudTrainingSelectClause(SoporteCloudTrainingMetadata metadata)
    {
        return string.Join(",",
            new[]
            {
                metadata.BaseMetadata.PrimaryIdField,
                metadata.BaseMetadata.PrimaryNameField,
                SoporteCloudTrainingDateField,
                SoporteCloudTrainingDurationMinutesField,
                BuildDashboardLookupValuePropertyName(SoporteCloudTrainingClientField),
                SoporteCloudTrainingAttendeesField,
                SoporteCloudTrainingTopicField,
                BuildDashboardLookupValuePropertyName(SoporteCloudTrainingOwnerField),
                SoporteCloudTrainingModifiedOnField
            }
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private SoporteCloudTrainingRowDto? BuildSoporteCloudTrainingRowDto(SoporteCloudTrainingMetadata metadata, JsonElement item)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, metadata.BaseMetadata.PrimaryIdField),
            ReadString(item, SoporteCloudTrainingFallbackIdField));
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var date = ReadDateOnly(item, SoporteCloudTrainingDateField);
        var durationMinutes = Math.Max(ReadDecimal(item, SoporteCloudTrainingDurationMinutesField) ?? 0m, 0m);
        var attendees = Math.Max(ReadIntFlexible(item, SoporteCloudTrainingAttendeesField), 0);
        var topicValue = ReadIntFlexible(item, SoporteCloudTrainingTopicField);
        var clientLookupProperty = DetectLookupValueProperty(
            item,
            new[]
            {
                BuildDashboardLookupValuePropertyName(SoporteCloudTrainingClientField),
                $"_{SoporteCloudTrainingClientField}id_value"
            },
            "cliente");
        var ownerLookupProperty = DetectLookupValueProperty(
            item,
            new[]
            {
                BuildDashboardLookupValuePropertyName(SoporteCloudTrainingOwnerField)
            },
            "owner");

        return new SoporteCloudTrainingRowDto
        {
            RecordId = recordId.Trim(),
            DateValue = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            DateDisplay = date?.ToString("dd/MM/yyyy", SoporteCloudCulture) ?? "Sin fecha",
            DurationMinutes = RoundCurrency(durationMinutes),
            DurationHours = RoundTrainingHours(durationMinutes),
            DurationDisplay = BuildSoporteCloudTrainingDurationDisplay(durationMinutes),
            ClientId = ReadString(item, clientLookupProperty).Trim(),
            ClientName = FirstNonEmpty(
                ReadLookupFormattedValue(item, clientLookupProperty),
                ReadString(item, $"{SoporteCloudTrainingClientField}{FormattedValueAnnotationSuffix}").Trim(),
                "Sin cliente"),
            Attendees = attendees,
            TopicValue = topicValue > 0 ? topicValue : null,
            TopicLabel = ResolveDashboardOptionLabel(
                item,
                SoporteCloudTrainingTopicField,
                topicValue,
                metadata.TopicLabels,
                "Sin tema"),
            OwnerId = ReadString(item, ownerLookupProperty).Trim(),
            OwnerName = FirstNonEmpty(
                ReadLookupFormattedValue(item, ownerLookupProperty),
                ReadString(item, $"{SoporteCloudTrainingOwnerField}{FormattedValueAnnotationSuffix}").Trim(),
                "Sin owner")
        };
    }

    private async Task<IReadOnlyList<SoporteCloudOptionDto>> LoadSoporteCloudTrainingOptionsFromMetadataAsync(
        string fieldName,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var queries = new[]
        {
            BuildSoporteCloudTrainingAttributeMetadataUrl(fieldName, "PicklistAttributeMetadata"),
            BuildSoporteCloudTrainingAttributeMetadataUrl(fieldName, "StatusAttributeMetadata"),
            BuildSoporteCloudTrainingAttributeMetadataUrl(fieldName, "StateAttributeMetadata"),
            BuildSoporteCloudTrainingAttributeMetadataUrl(fieldName, "MultiSelectPicklistAttributeMetadata")
        };

        foreach (var relativeUrl in queries)
        {
            try
            {
                var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);
                var options = ParseSoporteCloudMetadataOptions(json);
                if (options.Count > 0)
                    return options;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogDebug(ex, "No fue posible leer metadata de opciones para {FieldName} usando {RelativeUrl}", fieldName, relativeUrl);
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "La metadata de opciones para {FieldName} no se pudo interpretar.", fieldName);
            }
        }

        _logger.LogWarning("No fue posible resolver las opciones de metadata para {FieldName} en {EntityLogicalName}.", fieldName, SoporteCloudTrainingLogicalName);
        return Array.Empty<SoporteCloudOptionDto>();
    }

    private static string BuildSoporteCloudTrainingAttributeMetadataUrl(string fieldName, string attributeMetadataType)
    {
        return
            $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(SoporteCloudTrainingLogicalName)}')" +
            $"/Attributes(LogicalName='{EscapeOdataLiteral(fieldName)}')/Microsoft.Dynamics.CRM.{attributeMetadataType}" +
            "?$select=LogicalName&$expand=OptionSet($select=Options),GlobalOptionSet($select=Options)";
    }

    private static IReadOnlyList<SoporteCloudTrainingBreakdownDto> BuildSoporteCloudTrainingBreakdowns(
        IReadOnlyList<SoporteCloudTrainingRowDto> rows,
        Func<SoporteCloudTrainingRowDto, string?> keySelector,
        Func<SoporteCloudTrainingRowDto, string> labelSelector,
        string fallbackLabel)
    {
        var totalTrainings = rows.Count;
        return rows
            .GroupBy(
                row => BuildDashboardGroupKey(keySelector(row), labelSelector(row)),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new SoporteCloudTrainingBreakdownDto
                {
                    Key = group.Key,
                    Label = FirstNonEmpty(labelSelector(first), fallbackLabel),
                    TotalTrainings = group.Count(),
                    TotalHours = RoundTrainingHours(group.Sum(item => item.DurationMinutes)),
                    TotalAttendees = group.Sum(item => item.Attendees),
                    SharePercent = totalTrainings == 0
                        ? 0m
                        : Math.Round((group.Count() * 100m) / totalTrainings, 2, MidpointRounding.AwayFromZero)
                };
            })
            .OrderByDescending(item => item.TotalTrainings)
            .ThenByDescending(item => item.TotalHours)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<SoporteCloudTrainingTimePointDto> BuildSoporteCloudTrainingTimeSeries(
        IReadOnlyList<SoporteCloudTrainingRowDto> rows,
        DateOnly startDate,
        DateOnly endDate)
    {
        var datedRows = rows
            .Select(row => new
            {
                Row = row,
                HasDate = TryParseDateOnly(row.DateValue, out var parsedDate),
                Date = parsedDate
            })
            .Where(item => item.HasDate)
            .ToList();
        if (datedRows.Count == 0)
            return Array.Empty<SoporteCloudTrainingTimePointDto>();

        var spanDays = Math.Max(0, endDate.DayNumber - startDate.DayNumber);
        if (spanDays <= 92)
        {
            var buckets = datedRows
                .GroupBy(item => item.Date)
                .ToDictionary(group => group.Key, group => group.Select(item => item.Row).ToList());
            var points = new List<SoporteCloudTrainingTimePointDto>();
            for (var day = startDate; day <= endDate; day = day.AddDays(1))
            {
                buckets.TryGetValue(day, out var bucketRows);
                IReadOnlyList<SoporteCloudTrainingRowDto> pointRows = bucketRows is null
                    ? Array.Empty<SoporteCloudTrainingRowDto>()
                    : bucketRows;
                points.Add(BuildSoporteCloudTrainingTimePoint(
                    day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    day.ToString("dd/MM", SoporteCloudCulture),
                    pointRows));
            }

            return points;
        }

        var monthBuckets = datedRows
            .GroupBy(item => new DateOnly(item.Date.Year, item.Date.Month, 1))
            .ToDictionary(group => group.Key, group => group.Select(item => item.Row).ToList());
        var monthPoints = new List<SoporteCloudTrainingTimePointDto>();
        var currentMonth = new DateOnly(startDate.Year, startDate.Month, 1);
        var endMonth = new DateOnly(endDate.Year, endDate.Month, 1);
        for (; currentMonth <= endMonth; currentMonth = currentMonth.AddMonths(1))
        {
            monthBuckets.TryGetValue(currentMonth, out var bucketRows);
            IReadOnlyList<SoporteCloudTrainingRowDto> pointRows = bucketRows is null
                ? Array.Empty<SoporteCloudTrainingRowDto>()
                : bucketRows;
            monthPoints.Add(BuildSoporteCloudTrainingTimePoint(
                currentMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                currentMonth.ToString("MMM yyyy", SoporteCloudCulture),
                pointRows));
        }

        return monthPoints;
    }

    private static SoporteCloudTrainingTimePointDto BuildSoporteCloudTrainingTimePoint(
        string key,
        string label,
        IReadOnlyList<SoporteCloudTrainingRowDto> rows)
    {
        return new SoporteCloudTrainingTimePointDto
        {
            Key = key,
            Label = label,
            TotalTrainings = rows.Count,
            TotalHours = RoundTrainingHours(rows.Sum(item => item.DurationMinutes)),
            TotalAttendees = rows.Sum(item => item.Attendees)
        };
    }

    private static bool IsSoporteCloudTrainingRowInRange(SoporteCloudTrainingRowDto row, DateOnly startDate, DateOnly endDate)
    {
        if (!TryParseDateOnly(row.DateValue, out var rowDate))
            return true;

        return rowDate >= startDate && rowDate <= endDate;
    }

    private static decimal RoundTrainingHours(decimal durationMinutes) =>
        RoundCurrency(durationMinutes / 60m);

    private static string BuildSoporteCloudTrainingDurationDisplay(decimal durationMinutes)
    {
        var roundedMinutes = RoundCurrency(durationMinutes);
        var roundedHours = RoundTrainingHours(durationMinutes);
        return $"{FormatSoporteCloudTrainingNumber(roundedMinutes)} min / {FormatSoporteCloudTrainingNumber(roundedHours)} h";
    }

    private static string FormatSoporteCloudTrainingNumber(decimal value)
    {
        return decimal.Truncate(value) == value
            ? value.ToString("N0", SoporteCloudCulture)
            : value.ToString("N2", SoporteCloudCulture);
    }

    private sealed class SoporteCloudTrainingMetadata
    {
        public RhEntityMetadata BaseMetadata { get; init; } = new();
        public IReadOnlyList<SoporteCloudOptionDto> TopicOptions { get; init; } = Array.Empty<SoporteCloudOptionDto>();

        public IReadOnlyDictionary<int, string> TopicLabels =>
            TopicOptions.ToDictionary(item => item.Value, item => item.Label);
    }
}
