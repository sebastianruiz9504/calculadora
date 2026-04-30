using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CotizadorInterno.Web.Models.PlanRio;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string PlanRioLogicalName = "cr07a_planrioentreno";
    private const string PlanRioEntitySetName = "cr07a_planrioentrenos";
    private const string PlanRioPrimaryIdField = "cr07a_planrioentrenoid";
    private const string PlanRioPrimaryNameField = "cr07a_name";
    private const string PlanRioDateField = "cr07a_fecha";
    private const string PlanRioDayField = "cr07a_dia";
    private const string PlanRioWeekField = "cr07a_semanaplan";
    private const string PlanRioWeekStartField = "cr07a_iniciodesemana";
    private const string PlanRioPhaseField = "cr07a_fase";
    private const string PlanRioDisciplineField = "cr07a_disciplina";
    private const string PlanRioSessionField = "cr07a_sesion";
    private const string PlanRioMinutesField = "cr07a_min";
    private const string PlanRioHoursField = "cr07a_horas";
    private const string PlanRioVolumeField = "cr07a_volumenobjetivo";
    private const string PlanRioIntensityField = "cr07a_intensidadzona";
    private const string PlanRioDetailField = "cr07a_detalle";
    private const string PlanRioNutritionField = "cr07a_nutricionhidratacion";
    private const string PlanRioObjectiveField = "cr07a_objetivo";
    private const string PlanRioStatusField = "cr07a_estado";
    private const string PlanRioActualMinutesField = "cr07a_duracionreal";
    private const string PlanRioActualDistanceField = "cr07a_distanciareal";
    private const string PlanRioAverageHeartRateField = "cr07a_fcpromedio";
    private const string PlanRioAveragePowerField = "cr07a_potenciapromedio";
    private const string PlanRioNotesField = "cr07a_notas";
    private const string PlanRioSourceSheetField = "cr07a_origenhoja";
    private const string PlanRioSourceRowField = "cr07a_filaorigen";

    private static readonly CultureInfo PlanRioCulture = CultureInfo.GetCultureInfo("es-CO");

    public async Task<PlanRioPageViewModel> GetPlanRioPageAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveRhEntityMetadataAsync(
            PlanRioLogicalName,
            PlanRioEntitySetName,
            PlanRioPrimaryIdField,
            PlanRioPrimaryNameField,
            httpContext.User,
            ct);

        var rows = await LoadPlanRioRowsAsync(metadata, httpContext.User, ct);
        ApplyPlanRioWeekLabels(rows);

        var weeks = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.WeekKey))
            .GroupBy(row => new { row.WeekKey, row.WeekLabel })
            .Select(group => new PlanRioWeekDto
            {
                Key = group.Key.WeekKey,
                Label = group.Key.WeekLabel,
                WorkoutCount = group.Count()
            })
            .OrderBy(week => ResolvePlanRioWeekOrder(rows, week.Key))
            .ToList();

        var selectedWeekKey = ResolvePlanRioSelectedWeekKey(rows, weeks);
        foreach (var week in weeks)
            week.IsSelected = string.Equals(week.Key, selectedWeekKey, StringComparison.OrdinalIgnoreCase);

        var selectedWeekLabel = weeks.FirstOrDefault(week => week.IsSelected)?.Label
            ?? rows.FirstOrDefault()?.WeekLabel
            ?? "Semana no disponible";
        var sourceSheet = ResolveMostCommonValue(rows.Select(row => row.SourceSheet), "Dataverse");

        return new PlanRioPageViewModel
        {
            WeekLabel = selectedWeekLabel,
            Workouts = rows,
            Weeks = weeks,
            SourceSheet = sourceSheet,
            SourcePath = $"Dataverse: {metadata.EntitySetName}",
            SourceStatus = rows.Count == 0
                ? "La tabla de Dataverse no tiene entrenos cargados."
                : $"Dataverse cargó {rows.Count} entreno(s) desde {metadata.EntitySetName}.",
            DetailColumnName = PlanRioDetailField,
            WorkoutColumnName = PlanRioSessionField,
            WeekColumnName = PlanRioWeekField
        };
    }

    public async Task<PlanRioWorkoutSaveResultDto> SavePlanRioWorkoutAsync(
        PlanRioWorkoutSaveRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveRhEntityMetadataAsync(
            PlanRioLogicalName,
            PlanRioEntitySetName,
            PlanRioPrimaryIdField,
            PlanRioPrimaryNameField,
            httpContext.User,
            ct);

        var recordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var payload = BuildPlanRioSavePayload(request);
        await CallDataverseSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({recordId})",
            "PATCH",
            payload,
            httpContext.User,
            ct);

        var record = await LoadPlanRioWorkoutByIdAsync(metadata, recordId, httpContext.User, ct);
        if (record is not null)
            ApplyPlanRioWeekLabels(new[] { record });

        return new PlanRioWorkoutSaveResultDto
        {
            Message = "Entreno registrado correctamente.",
            Record = record
        };
    }

    private async Task<List<PlanRioWorkoutDto>> LoadPlanRioRowsAsync(
        RhEntityMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var selectFields = BuildPlanRioSelectFields(metadata);

        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={string.Join(",", selectFields)}" +
            $"&$orderby={PlanRioDateField} asc,{PlanRioSourceRowField} asc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select((item, index) => BuildPlanRioWorkout(item, metadata, index + 1))
            .Where(item => item is not null)
            .Cast<PlanRioWorkoutDto>()
            .ToList();
    }

    private async Task<PlanRioWorkoutDto?> LoadPlanRioWorkoutByIdAsync(
        RhEntityMetadata metadata,
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var selectFields = BuildPlanRioSelectFields(metadata);
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}({recordId})?$select={string.Join(",", selectFields)}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        return BuildPlanRioWorkout(doc.RootElement, metadata, 1);
    }

    private static IReadOnlyList<string> BuildPlanRioSelectFields(RhEntityMetadata metadata)
    {
        return new[]
            {
                metadata.PrimaryIdField,
                metadata.PrimaryNameField,
                PlanRioDateField,
                PlanRioDayField,
                PlanRioWeekField,
                PlanRioWeekStartField,
                PlanRioPhaseField,
                PlanRioDisciplineField,
                PlanRioSessionField,
                PlanRioMinutesField,
                PlanRioHoursField,
                PlanRioVolumeField,
                PlanRioIntensityField,
                PlanRioDetailField,
                PlanRioNutritionField,
                PlanRioObjectiveField,
                PlanRioStatusField,
                PlanRioActualMinutesField,
                PlanRioActualDistanceField,
                PlanRioAverageHeartRateField,
                PlanRioAveragePowerField,
                PlanRioNotesField,
                PlanRioSourceSheetField,
                PlanRioSourceRowField
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static PlanRioWorkoutDto? BuildPlanRioWorkout(JsonElement item, RhEntityMetadata metadata, int fallbackId)
    {
        var recordId = FirstNonEmpty(ReadString(item, metadata.PrimaryIdField), ReadString(item, PlanRioPrimaryIdField));
        var date = ReadDateOnly(item, PlanRioDateField);
        var sourceRow = ReadInt(item, PlanRioSourceRowField);
        var session = FirstNonEmpty(ReadString(item, PlanRioSessionField), ReadString(item, metadata.PrimaryNameField));
        var detail = ReadString(item, PlanRioDetailField).Trim();

        if (string.IsNullOrWhiteSpace(session) && string.IsNullOrWhiteSpace(detail) && !date.HasValue)
            return null;

        var weekNumber = ReadInt(item, PlanRioWeekField);
        return new PlanRioWorkoutDto
        {
            Id = sourceRow > 0 ? sourceRow : fallbackId,
            RecordId = recordId,
            Date = date,
            Day = FirstNonEmpty(ReadString(item, PlanRioDayField), FormatPlanRioDay(date)),
            WeekRaw = weekNumber > 0 ? weekNumber.ToString(CultureInfo.InvariantCulture) : "",
            Workout = session,
            Detail = detail,
            GoalMin = ReadInt(item, PlanRioMinutesField),
            Hours = ReadDecimal(item, PlanRioHoursField) ?? 0m,
            Phase = ReadString(item, PlanRioPhaseField).Trim(),
            Discipline = ReadString(item, PlanRioDisciplineField).Trim(),
            VolumeObjective = ReadString(item, PlanRioVolumeField).Trim(),
            IntensityZone = ReadString(item, PlanRioIntensityField).Trim(),
            Nutrition = ReadString(item, PlanRioNutritionField).Trim(),
            Objective = ReadString(item, PlanRioObjectiveField).Trim(),
            Status = ReadString(item, PlanRioStatusField).Trim(),
            ActualMin = ReadInt(item, PlanRioActualMinutesField),
            ActualDistance = ReadDecimal(item, PlanRioActualDistanceField),
            AverageHeartRate = ReadInt(item, PlanRioAverageHeartRateField),
            AveragePower = ReadNullableInt(item, PlanRioAveragePowerField),
            Notes = ReadString(item, PlanRioNotesField).Trim(),
            SourceSheet = ReadString(item, PlanRioSourceSheetField).Trim(),
            SourceRow = sourceRow
        };
    }

    private static Dictionary<string, object?> BuildPlanRioSavePayload(PlanRioWorkoutSaveRequestDto request)
    {
        if (request.DurationMinutes <= 0 || request.DurationMinutes > 3000)
            throw new InvalidOperationException("La duracion debe estar entre 1 y 3000 minutos.");

        if (request.Distance <= 0m || request.Distance > 1000m)
            throw new InvalidOperationException("La distancia debe estar entre 0.01 y 1000.");

        if (request.AverageHeartRate <= 0 || request.AverageHeartRate > 250)
            throw new InvalidOperationException("La FC promedio debe estar entre 1 y 250.");

        if (request.AveragePower is < 0 or > 2000)
            throw new InvalidOperationException("La potencia promedio debe estar entre 0 y 2000.");

        var notes = request.Notes?.Trim() ?? "";
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [PlanRioActualMinutesField] = request.DurationMinutes,
            [PlanRioActualDistanceField] = Math.Round(request.Distance, 2, MidpointRounding.AwayFromZero),
            [PlanRioAverageHeartRateField] = request.AverageHeartRate,
            [PlanRioAveragePowerField] = request.AveragePower,
            [PlanRioNotesField] = string.IsNullOrWhiteSpace(notes) ? null : notes
        };
    }

    private static int? ReadNullableInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        return p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetInt32(out var v) ? v : null,
            JsonValueKind.String => int.TryParse(p.GetString(), out var v) ? v : null,
            _ => null
        };
    }

    private static void ApplyPlanRioWeekLabels(IReadOnlyList<PlanRioWorkoutDto> workouts)
    {
        foreach (var workout in workouts)
        {
            workout.WeekKey = ResolvePlanRioWeekKey(workout);
            workout.WeekLabel = string.IsNullOrWhiteSpace(workout.WeekRaw)
                ? BuildPlanRioDateWeekLabel(workout.Date)
                : $"Semana {workout.WeekRaw}";
        }

        foreach (var group in workouts.GroupBy(static workout => workout.WeekKey))
        {
            var dates = group
                .Where(static workout => workout.Date.HasValue)
                .Select(static workout => workout.Date!.Value)
                .OrderBy(static date => date)
                .ToList();

            if (dates.Count == 0)
                continue;

            var first = dates.First();
            var last = dates.Last();
            var range = first == last
                ? first.ToString("dd/MM/yyyy", PlanRioCulture)
                : $"{first:dd/MM/yyyy} - {last:dd/MM/yyyy}";
            var weekRaw = group.FirstOrDefault(static workout => !string.IsNullOrWhiteSpace(workout.WeekRaw))?.WeekRaw;
            var label = string.IsNullOrWhiteSpace(weekRaw)
                ? $"Semana del {range}"
                : $"Semana {weekRaw} ({range})";

            foreach (var workout in group)
                workout.WeekLabel = label;
        }
    }

    private static string ResolvePlanRioSelectedWeekKey(IReadOnlyList<PlanRioWorkoutDto> workouts, IReadOnlyList<PlanRioWeekDto> weeks)
    {
        if (weeks.Count == 0)
            return "";

        var today = DateOnly.FromDateTime(DateTime.Today);
        var currentWorkout = workouts.FirstOrDefault(workout => workout.Date == today);
        if (currentWorkout is not null)
            return currentWorkout.WeekKey;

        var currentWeekStart = StartOfPlanRioWeek(today);
        var currentWeekEnd = currentWeekStart.AddDays(6);
        var currentWeekWorkout = workouts.FirstOrDefault(workout =>
            workout.Date.HasValue && workout.Date.Value >= currentWeekStart && workout.Date.Value <= currentWeekEnd);
        if (currentWeekWorkout is not null)
            return currentWeekWorkout.WeekKey;

        return weeks[0].Key;
    }

    private static int ResolvePlanRioWeekOrder(IReadOnlyList<PlanRioWorkoutDto> workouts, string weekKey)
    {
        var matching = workouts.Where(workout => string.Equals(workout.WeekKey, weekKey, StringComparison.OrdinalIgnoreCase)).ToList();
        var weekNumber = matching
            .Select(workout => int.TryParse(workout.WeekRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : int.MaxValue)
            .DefaultIfEmpty(int.MaxValue)
            .Min();
        if (weekNumber != int.MaxValue)
            return weekNumber;

        return matching
            .Where(workout => workout.Date.HasValue)
            .Select(workout => workout.Date!.Value.DayNumber)
            .DefaultIfEmpty(int.MaxValue)
            .Min();
    }

    private static string ResolvePlanRioWeekKey(PlanRioWorkoutDto workout)
    {
        if (!string.IsNullOrWhiteSpace(workout.WeekRaw))
            return $"week:{workout.WeekRaw.Trim()}";

        if (workout.Date.HasValue)
            return $"date:{StartOfPlanRioWeek(workout.Date.Value):yyyyMMdd}";

        return "week:sin-semana";
    }

    private static DateOnly StartOfPlanRioWeek(DateOnly date)
    {
        var delta = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-delta);
    }

    private static string BuildPlanRioDateWeekLabel(DateOnly? date)
    {
        if (!date.HasValue)
            return "Semana no disponible";

        var start = StartOfPlanRioWeek(date.Value);
        var end = start.AddDays(6);
        return $"Semana del {start:dd/MM/yyyy} al {end:dd/MM/yyyy}";
    }

    private static string FormatPlanRioDay(DateOnly? date)
    {
        if (!date.HasValue)
            return "";

        var dayName = PlanRioCulture.DateTimeFormat.GetDayName(date.Value.DayOfWeek);
        return PlanRioCulture.TextInfo.ToTitleCase(dayName);
    }
}
