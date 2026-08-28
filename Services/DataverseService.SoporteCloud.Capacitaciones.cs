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
    private const string SoporteCloudTrainingTopicTextField = "cr07a_temacapacitacion";
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
        CancellationToken ct = default,
        bool includeAll = false)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var (resolvedStartDate, resolvedEndDate) = ResolveSoporteCloudDateRange(startDate, endDate);
        var metadata = await ResolveSoporteCloudTrainingMetadataAsync(httpContext.User, ct);
        var allRows = await LoadSoporteCloudTrainingRowsAsync(
            metadata,
            httpContext.User,
            includeAll ? null : resolvedStartDate,
            includeAll ? null : resolvedEndDate,
            ct);
        var filteredRows = includeAll
            ? allRows.ToList()
            : allRows
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
                    TotalMinutes = RoundCurrency(group.Sum(item => item.DurationMinutes)),
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
            StartDateValue = includeAll ? "" : resolvedStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EndDateValue = includeAll ? "" : resolvedEndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateRangeLabel = includeAll ? "Todas las capacitaciones" : $"{resolvedStartDate:dd/MM/yyyy} - {resolvedEndDate:dd/MM/yyyy}",
            TotalTrainings = filteredRows.Count,
            TotalMinutesDelivered = RoundCurrency(filteredRows.Sum(item => item.DurationMinutes)),
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
            TimeSeries = includeAll && filteredRows.Count > 0
                ? BuildSoporteCloudTrainingTimeSeries(filteredRows, ResolveTrainingMinDate(filteredRows), ResolveTrainingMaxDate(filteredRows))
                : BuildSoporteCloudTrainingTimeSeries(filteredRows, resolvedStartDate, resolvedEndDate),
            TopicOptions = metadata.TopicOptions
        };
    }

    public async Task<SoporteCloudTrainingSaveResultDto> SaveSoporteCloudTrainingAsync(
        SoporteCloudTrainingSaveRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveSoporteCloudTrainingMetadataAsync(httpContext.User, ct);
        var normalized = await NormalizeSoporteCloudTrainingSaveRequestAsync(request, metadata, ct);
        await CreateSoporteCloudTrainingAsync(metadata, normalized, httpContext.User, ct);

        return new SoporteCloudTrainingSaveResultDto
        {
            Message = "Capacitacion registrada correctamente.",
            Board = await GetSoporteCloudTrainingsBoardAsync(ct: ct, includeAll: true)
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

        string clientNavigationProperty;
        try
        {
            clientNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
                SoporteCloudTrainingLogicalName,
                SoporteCloudTrainingClientField,
                SoporteCloudTrainingClientField,
                user,
                ct);
        }
        catch (InvalidOperationException)
        {
            clientNavigationProperty = SoporteCloudTrainingClientField;
        }

        var resolved = new SoporteCloudTrainingMetadata
        {
            BaseMetadata = baseMetadata,
            TopicOptions = topicOptions,
            ClientNavigationProperty = clientNavigationProperty
        };

        _soporteCloudTrainingMetadataCache[cacheKey] = resolved;
        return resolved;
    }

    private async Task<IReadOnlyList<SoporteCloudTrainingRowDto>> LoadSoporteCloudTrainingRowsAsync(
        SoporteCloudTrainingMetadata metadata,
        ClaimsPrincipal user,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken ct)
    {
        var filterClause = startDate.HasValue && endDate.HasValue
            ? $"&$filter={Uri.EscapeDataString(BuildBillingDateFilter(SoporteCloudTrainingDateField, "date-only", startDate.Value, endDate.Value.AddDays(1)))}"
            : "";
        var relativeUrl =
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}?$select={BuildSoporteCloudTrainingSelectClause(metadata)}" +
            filterClause +
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

    private async Task<SoporteCloudTrainingRowDto?> FindSoporteCloudTrainingByPrimaryNameAsync(
        SoporteCloudTrainingMetadata metadata,
        string primaryName,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(metadata.BaseMetadata.PrimaryNameField)
            || string.IsNullOrWhiteSpace(primaryName))
        {
            return null;
        }

        var filter = Uri.EscapeDataString($"{metadata.BaseMetadata.PrimaryNameField} eq '{EscapeOdataLiteral(primaryName)}'");
        var relativeUrl =
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}?$select={BuildSoporteCloudTrainingSelectClause(metadata)}&$filter={filter}&$top=1";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return items
            .Select(item => BuildSoporteCloudTrainingRowDto(metadata, item))
            .FirstOrDefault(item => item is not null);
    }

    private async Task CreateSoporteCloudTrainingAsync(
        SoporteCloudTrainingMetadata metadata,
        SoporteCloudTrainingWriteModel model,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [metadata.BaseMetadata.PrimaryNameField] = model.PrimaryName,
            [SoporteCloudTrainingTopicTextField] = model.TopicText,
            [SoporteCloudTrainingDateField] = model.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [SoporteCloudTrainingDurationMinutesField] = model.DurationMinutes,
            [SoporteCloudTrainingAttendeesField] = model.Attendees,
            [SoporteCloudTrainingTopicField] = model.TopicValue
        };

        if (!string.IsNullOrWhiteSpace(model.ClientId))
        {
            payload[$"{metadata.ClientNavigationProperty}@odata.bind"] =
                $"/{ClientsEntitySetName}({NormalizeGuid(model.ClientId, nameof(model.ClientId))})";
        }

        await CallDataverseSendAsync(
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}",
            "POST",
            payload,
            user,
            ct);
    }

    private static string BuildSoporteCloudTrainingSelectClause(SoporteCloudTrainingMetadata metadata)
    {
        return string.Join(",",
            new[]
            {
                metadata.BaseMetadata.PrimaryIdField,
                metadata.BaseMetadata.PrimaryNameField,
                SoporteCloudTrainingTopicTextField,
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
            TopicText = FirstNonEmpty(
                ReadString(item, SoporteCloudTrainingTopicTextField).Trim(),
                ReadString(item, metadata.BaseMetadata.PrimaryNameField).Trim()),
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
                    TotalMinutes = RoundCurrency(group.Sum(item => item.DurationMinutes)),
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
            TotalMinutes = RoundCurrency(rows.Sum(item => item.DurationMinutes)),
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

    private async Task<SoporteCloudTrainingWriteModel> NormalizeSoporteCloudTrainingSaveRequestAsync(
        SoporteCloudTrainingSaveRequest request,
        SoporteCloudTrainingMetadata metadata,
        CancellationToken ct)
    {
        var topicText = (request.TopicText ?? "").Trim();
        if (string.IsNullOrWhiteSpace(topicText))
            throw new InvalidOperationException("Debes indicar el tema de la capacitacion.");

        if (!TryParseDateOnly(request.DateValue, out var date))
            throw new InvalidOperationException("La fecha de la capacitacion debe ser valida.");

        var durationMinutes = RoundCurrency(request.DurationMinutes);
        if (durationMinutes < 0m)
            throw new InvalidOperationException("La duracion no puede ser negativa.");

        var attendees = Math.Max(request.Attendees, 0);
        var topicValue = NormalizeSoporteCloudTrainingTopicValue(request.TopicValue, metadata.TopicLabels);
        var clientId = FirstNonEmpty(
            NormalizeOptionalGuid(request.ClientId),
            string.IsNullOrWhiteSpace(request.ClientName) ? "" : await ResolveSoporteCloudClientIdAsync(request.ClientName, ct));
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("Debes seleccionar un cliente.");

        return new SoporteCloudTrainingWriteModel
        {
            PrimaryName = BuildSoporteCloudTrainingManualName(topicText, date),
            TopicText = topicText,
            Date = date,
            DurationMinutes = durationMinutes,
            ClientId = clientId,
            Attendees = attendees,
            TopicValue = topicValue
        };
    }

    private async Task<SoporteCloudTrainingRowDto?> EnsureSoporteCloudTrainingFromSurveySessionAsync(
        SoporteCloudSurveySessionDto session,
        SoporteCloudSurveySessionDetailDto detail,
        decimal? durationMinutes,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var metadata = await ResolveSoporteCloudTrainingMetadataAsync(user, ct);
        var primaryName = BuildSoporteCloudTrainingSurveyName(session);
        var existing = await FindSoporteCloudTrainingByPrimaryNameAsync(metadata, primaryName, user, ct);
        if (existing is not null)
            return existing;

        var date = TryParseDateOnly(session.DateValue, out var parsedDate)
            ? parsedDate
            : GetBogotaToday();
        var normalizedDuration = RoundCurrency(Math.Max(durationMinutes ?? 0m, 0m));
        var attendees = detail.Session.CompletedCount > 0
            ? detail.Session.CompletedCount
            : detail.Session.RegisteredCount;
        var topicValue = ResolveSoporteCloudTrainingTopicValueByLabel(session.TopicName, metadata.TopicOptions)
            ?? ResolveSoporteCloudTrainingTopicValueByLabel("Otros", metadata.TopicOptions)
            ?? SoporteCloudTrainingTopicFallbackOptions.FirstOrDefault(item => string.Equals(item.Label, "Otros", StringComparison.OrdinalIgnoreCase))?.Value
            ?? metadata.TopicOptions.FirstOrDefault()?.Value
            ?? 0;

        if (topicValue <= 0)
            throw new InvalidOperationException("No fue posible resolver el tema para registrar la capacitacion.");

        var model = new SoporteCloudTrainingWriteModel
        {
            PrimaryName = primaryName,
            TopicText = FirstNonEmpty(session.TopicName, session.Name, "Capacitacion desde encuesta"),
            Date = date,
            DurationMinutes = normalizedDuration,
            ClientId = session.ClientId,
            Attendees = Math.Max(attendees, 0),
            TopicValue = topicValue
        };

        await CreateSoporteCloudTrainingAsync(metadata, model, user, ct);
        return await FindSoporteCloudTrainingByPrimaryNameAsync(metadata, primaryName, user, ct);
    }

    private static int NormalizeSoporteCloudTrainingTopicValue(
        int? value,
        IReadOnlyDictionary<int, string> knownLabels)
    {
        if (!value.HasValue || value.Value <= 0)
            throw new InvalidOperationException("Debes seleccionar un tema valido.");

        if (knownLabels.Count > 0 && !knownLabels.ContainsKey(value.Value))
            throw new InvalidOperationException("El tema seleccionado no es valido.");

        return value.Value;
    }

    private static int? ResolveSoporteCloudTrainingTopicValueByLabel(
        string? label,
        IReadOnlyList<SoporteCloudOptionDto> options)
    {
        var normalizedLabel = NormalizeSoporteCloudText(label);
        if (string.IsNullOrWhiteSpace(normalizedLabel))
            return null;

        var exact = options.FirstOrDefault(item =>
            string.Equals(NormalizeSoporteCloudText(item.Label), normalizedLabel, StringComparison.Ordinal));
        if (exact is not null)
            return exact.Value;

        var contains = options.FirstOrDefault(item =>
            normalizedLabel.Contains(NormalizeSoporteCloudText(item.Label), StringComparison.Ordinal)
            || NormalizeSoporteCloudText(item.Label).Contains(normalizedLabel, StringComparison.Ordinal));
        return contains?.Value;
    }

    private static DateOnly ResolveTrainingMinDate(IReadOnlyList<SoporteCloudTrainingRowDto> rows)
    {
        var dates = rows
            .Select(row => TryParseDateOnly(row.DateValue, out var date) ? date : (DateOnly?)null)
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .ToList();
        return dates.Count == 0 ? GetBogotaToday() : dates.Min();
    }

    private static DateOnly ResolveTrainingMaxDate(IReadOnlyList<SoporteCloudTrainingRowDto> rows)
    {
        var dates = rows
            .Select(row => TryParseDateOnly(row.DateValue, out var date) ? date : (DateOnly?)null)
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .ToList();
        return dates.Count == 0 ? GetBogotaToday() : dates.Max();
    }

    private static string BuildSoporteCloudTrainingManualName(string topicText, DateOnly date)
    {
        var normalizedTopic = topicText.ReplaceLineEndings(" ").Trim();
        if (normalizedTopic.Length > 120)
            normalizedTopic = normalizedTopic[..120].Trim();

        return $"{date:yyyy-MM-dd} - {normalizedTopic}";
    }

    private static string BuildSoporteCloudTrainingSurveyName(SoporteCloudSurveySessionDto session)
    {
        var key = FirstNonEmpty(session.Code, session.SessionId);
        var name = FirstNonEmpty(session.Name, session.TopicName, "Encuesta");
        return $"QR-{key}: {name}";
    }

    private static decimal RoundTrainingHours(decimal durationMinutes) =>
        RoundCurrency(durationMinutes / 60m);

    private static string BuildSoporteCloudTrainingDurationDisplay(decimal durationMinutes)
    {
        var roundedMinutes = RoundCurrency(durationMinutes);
        return $"{FormatSoporteCloudTrainingNumber(roundedMinutes)} min";
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
        public string ClientNavigationProperty { get; init; } = SoporteCloudTrainingClientField;

        public IReadOnlyDictionary<int, string> TopicLabels =>
            TopicOptions.ToDictionary(item => item.Value, item => item.Label);
    }

    private sealed class SoporteCloudTrainingWriteModel
    {
        public string PrimaryName { get; init; } = "";
        public string TopicText { get; init; } = "";
        public DateOnly Date { get; init; }
        public decimal DurationMinutes { get; init; }
        public string ClientId { get; init; } = "";
        public int Attendees { get; init; }
        public int TopicValue { get; init; }
    }
}
