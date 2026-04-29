using ClosedXML.Excel;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.PlanRio;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.PlanRio)]
public class PlanRioController : Controller
{
    private const string RequiredSheetName = "plan corregido";
    private static readonly string[] FallbackSheetNames = { "Plan Rio ajustado", "Plan diario" };
    private static readonly CultureInfo ColombianCulture = CultureInfo.GetCultureInfo("es-CO");

    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public PlanRioController(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public IActionResult Index()
    {
        var model = LoadFromWorkbook();
        return View(model);
    }

    private PlanRioPageViewModel LoadFromWorkbook()
    {
        var pathSetting = _configuration["PlanRio:WorkbookPath"] ?? "App_Data/plan-rio.xlsx";
        var fullPath = Path.IsPathRooted(pathSetting)
            ? pathSetting
            : Path.Combine(_environment.ContentRootPath, pathSetting);

        if (!System.IO.File.Exists(fullPath))
        {
            return new PlanRioPageViewModel
            {
                SourcePath = pathSetting,
                SourceSheet = RequiredSheetName,
                SourceStatus = "No tengo acceso al Excel de origen en la ruta configurada.",
                WeekLabel = "Semana no disponible"
            };
        }

        using var workbook = new XLWorkbook(fullPath);
        var worksheet = FindWorksheet(workbook, RequiredSheetName);
        var sourceStatus = $"Excel cargado desde la hoja '{worksheet?.Name ?? RequiredSheetName}'.";
        if (worksheet is null)
        {
            worksheet = FallbackSheetNames
                .Select(name => FindWorksheet(workbook, name))
                .FirstOrDefault(sheet => sheet is not null);

            if (worksheet is not null)
                sourceStatus = $"El Excel no contiene la hoja '{RequiredSheetName}'. Cargué '{worksheet.Name}' como respaldo.";
        }

        if (worksheet is null)
        {
            var availableSheets = string.Join(", ", workbook.Worksheets.Select(sheet => sheet.Name));
            return new PlanRioPageViewModel
            {
                SourcePath = pathSetting,
                SourceSheet = RequiredSheetName,
                SourceStatus = $"El Excel existe, pero no contiene la hoja '{RequiredSheetName}'. Hojas disponibles: {availableSheets}.",
                WeekLabel = "Semana no disponible"
            };
        }

        var usedRange = worksheet.RangeUsed();
        if (usedRange is null)
        {
            return new PlanRioPageViewModel
            {
                SourcePath = pathSetting,
                SourceSheet = worksheet.Name,
                SourceStatus = "La hoja existe, pero no contiene datos.",
                WeekLabel = "Semana no disponible"
            };
        }

        var rows = usedRange.RowsUsed().ToList();
        var headerRowIndex = FindHeaderRowIndex(rows);
        if (headerRowIndex < 0 || headerRowIndex >= rows.Count - 1)
        {
            return new PlanRioPageViewModel
            {
                SourcePath = pathSetting,
                SourceSheet = worksheet.Name,
                SourceStatus = "No pude identificar los encabezados del plan en la hoja.",
                WeekLabel = "Semana no disponible"
            };
        }

        var headerRow = rows[headerRowIndex];
        var headers = headerRow.CellsUsed()
            .Select(cell => new PlanRioColumnHeader(cell.Address.ColumnNumber, cell.GetString().Trim()))
            .Where(header => !string.IsNullOrWhiteSpace(header.Name))
            .ToList();
        var dataRows = rows.Skip(headerRowIndex + 1).ToList();

        var dateCol = FindColumn(headers, "fecha", "date");
        var dayCol = FindColumn(headers, "dia", "día", "day");
        var weekCol = FindColumn(headers, "semana", "week", "microciclo");
        var goalCol = FindColumn(headers, "meta", "min", "duracion", "duración", "tiempo", "duration");
        var workoutCol = FindWorkoutColumn(headers, dataRows, dateCol, dayCol, weekCol, goalCol);
        var detailCol = FindDetailColumn(headers, dataRows, dateCol, dayCol, weekCol, goalCol, workoutCol);

        var workouts = new List<PlanRioWorkoutDto>();
        foreach (var row in dataRows)
        {
            var workout = ReadCell(row, workoutCol);
            var detail = ReadCell(row, detailCol);
            var date = ReadDate(row, dateCol);
            var weekRaw = ReadCell(row, weekCol);

            if (string.IsNullOrWhiteSpace(workout) && !string.IsNullOrWhiteSpace(detail))
                workout = BuildWorkoutTitle(detail);

            if (string.IsNullOrWhiteSpace(workout) && !date.HasValue)
                continue;

            workouts.Add(new PlanRioWorkoutDto
            {
                Id = row.RowNumber(),
                Date = date,
                Day = FirstNonEmpty(ReadCell(row, dayCol), FormatDay(date)),
                Workout = workout,
                Detail = detail,
                GoalMin = ReadDurationMinutes(row, goalCol),
                WeekRaw = weekRaw
            });
        }

        ApplyWeekLabels(workouts);

        var weeks = workouts
            .Where(workout => !string.IsNullOrWhiteSpace(workout.WeekKey))
            .GroupBy(workout => new { workout.WeekKey, workout.WeekLabel })
            .Select(group => new PlanRioWeekDto
            {
                Key = group.Key.WeekKey,
                Label = group.Key.WeekLabel,
                WorkoutCount = group.Count()
            })
            .ToList();

        var selectedWeekKey = ResolveSelectedWeekKey(workouts, weeks);
        foreach (var week in weeks)
            week.IsSelected = string.Equals(week.Key, selectedWeekKey, StringComparison.OrdinalIgnoreCase);

        var selectedWeekLabel = weeks.FirstOrDefault(week => week.IsSelected)?.Label
            ?? workouts.FirstOrDefault()?.WeekLabel
            ?? "Semana no disponible";

        return new PlanRioPageViewModel
        {
            SourcePath = pathSetting,
            SourceSheet = worksheet.Name,
            SourceStatus = sourceStatus,
            WeekLabel = selectedWeekLabel,
            DetailColumnName = ResolveColumnName(headers, detailCol),
            WorkoutColumnName = ResolveColumnName(headers, workoutCol),
            WeekColumnName = ResolveColumnName(headers, weekCol),
            Workouts = workouts,
            Weeks = weeks
        };
    }

    private static int FindHeaderRowIndex(IReadOnlyList<IXLRangeRow> rows)
    {
        var bestIndex = -1;
        var bestScore = 0;

        for (var i = 0; i < Math.Min(rows.Count, 15); i++)
        {
            var names = rows[i].CellsUsed()
                .Select(cell => Normalize(cell.GetString()))
                .Where(static text => text.Length > 0)
                .ToList();

            var score = 0;
            if (names.Any(name => name.Contains("fecha"))) score += 2;
            if (names.Any(name => name.Contains("dia") || name.Contains("day"))) score += 1;
            if (names.Any(name => name.Contains("semana") || name.Contains("week"))) score += 2;
            if (names.Any(name => name.Contains("entreno") || name.Contains("entrenamiento") || name.Contains("actividad") || name.Contains("sesion"))) score += 2;
            if (names.Any(name => name.Contains("detalle") || name.Contains("descripcion") || name.Contains("contenido") || name.Contains("composicion"))) score += 2;

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return bestScore >= 2 ? bestIndex : -1;
    }

    private static IXLWorksheet? FindWorksheet(XLWorkbook workbook, string sheetName) =>
        workbook.Worksheets.FirstOrDefault(sheet =>
            string.Equals(sheet.Name.Trim(), sheetName, StringComparison.OrdinalIgnoreCase));

    private static int FindColumn(IReadOnlyList<PlanRioColumnHeader> headers, params string[] terms)
    {
        var normalizedTerms = terms.Select(Normalize).ToArray();
        return headers.FirstOrDefault(header =>
            normalizedTerms.Any(term => Normalize(header.Name).Contains(term, StringComparison.OrdinalIgnoreCase)))?.Index ?? -1;
    }

    private static int FindWorkoutColumn(
        IReadOnlyList<PlanRioColumnHeader> headers,
        IReadOnlyList<IXLRangeRow> dataRows,
        params int[] excludedColumns)
    {
        var direct = FindColumn(headers, "sesion", "sesión", "entreno", "entrenamiento", "actividad");
        if (direct > 0)
            return direct;

        var discipline = FindColumn(headers, "deporte", "disciplina", "tipo");
        if (discipline > 0)
            return discipline;

        return headers
            .Where(header => !excludedColumns.Contains(header.Index))
            .Select(header => new
            {
                header.Index,
                Score = dataRows
                    .Select(row => ReadCell(row, header.Index))
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => Math.Min(value.Length, 80))
                    .DefaultIfEmpty(0)
                    .Average()
            })
            .OrderByDescending(item => item.Score)
            .FirstOrDefault()?.Index ?? -1;
    }

    private static int FindDetailColumn(
        IReadOnlyList<PlanRioColumnHeader> headers,
        IReadOnlyList<IXLRangeRow> dataRows,
        params int[] lowerPriorityColumns)
    {
        var priorityColumns = lowerPriorityColumns.Where(column => column > 0).ToHashSet();

        return headers
            .Select(header =>
            {
                var normalizedName = Normalize(header.Name);
                var samples = dataRows
                    .Select(row => ReadCell(row, header.Index))
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .ToList();
                var averageLength = samples.Count == 0 ? 0 : samples.Average(static value => value.Length);
                var maxLength = samples.Count == 0 ? 0 : samples.Max(static value => value.Length);
                var multilineCount = samples.Count(static value => value.Contains('\n') || value.Contains('\r'));
                var headerScore = 0;

                if (normalizedName.Contains("detalle")) headerScore += 160;
                if (normalizedName.Contains("descripcion")) headerScore += 120;
                if (normalizedName.Contains("contenido")) headerScore += 100;
                if (normalizedName.Contains("composicion")) headerScore += 100;
                if (normalizedName.Contains("estructura")) headerScore += 80;
                if (normalizedName.Contains("rutina")) headerScore += 80;
                if (normalizedName.Contains("indicacion")) headerScore += 80;
                if (normalizedName.Contains("observacion")) headerScore += 40;

                var priorityPenalty = priorityColumns.Contains(header.Index) ? 80 : 0;
                return new
                {
                    header.Index,
                    Score = headerScore + averageLength + (maxLength * 0.15) + (multilineCount * 12) - priorityPenalty
                };
            })
            .OrderByDescending(item => item.Score)
            .FirstOrDefault()?.Index ?? -1;
    }

    private static void ApplyWeekLabels(IReadOnlyList<PlanRioWorkoutDto> workouts)
    {
        foreach (var workout in workouts)
        {
            workout.WeekKey = ResolveWeekKey(workout);
            workout.WeekLabel = string.IsNullOrWhiteSpace(workout.WeekRaw)
                ? BuildDateWeekLabel(workout.Date)
                : EnsureWeekPrefix(workout.WeekRaw);
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
                ? first.ToString("dd/MM/yyyy", ColombianCulture)
                : $"{first:dd/MM/yyyy} - {last:dd/MM/yyyy}";
            var labelBase = group.FirstOrDefault(static workout => !string.IsNullOrWhiteSpace(workout.WeekRaw))?.WeekRaw;
            var label = string.IsNullOrWhiteSpace(labelBase)
                ? $"Semana del {range}"
                : $"{EnsureWeekPrefix(labelBase)} ({range})";

            foreach (var workout in group)
                workout.WeekLabel = label;
        }
    }

    private static string ResolveSelectedWeekKey(IReadOnlyList<PlanRioWorkoutDto> workouts, IReadOnlyList<PlanRioWeekDto> weeks)
    {
        if (weeks.Count == 0)
            return "";

        var today = DateOnly.FromDateTime(DateTime.Today);
        var currentWorkout = workouts.FirstOrDefault(workout => workout.Date == today);
        if (currentWorkout is not null)
            return currentWorkout.WeekKey;

        var currentWeekStart = StartOfWeek(today);
        var currentWeekEnd = currentWeekStart.AddDays(6);
        var currentWeekWorkout = workouts.FirstOrDefault(workout =>
            workout.Date.HasValue && workout.Date.Value >= currentWeekStart && workout.Date.Value <= currentWeekEnd);
        if (currentWeekWorkout is not null)
            return currentWeekWorkout.WeekKey;

        return weeks[0].Key;
    }

    private static string ResolveWeekKey(PlanRioWorkoutDto workout)
    {
        if (!string.IsNullOrWhiteSpace(workout.WeekRaw))
            return $"week:{Normalize(workout.WeekRaw)}";

        if (workout.Date.HasValue)
            return $"date:{StartOfWeek(workout.Date.Value):yyyyMMdd}";

        return "week:sin-semana";
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var delta = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-delta);
    }

    private static string BuildDateWeekLabel(DateOnly? date)
    {
        if (!date.HasValue)
            return "Semana no disponible";

        var start = StartOfWeek(date.Value);
        var end = start.AddDays(6);
        return $"Semana del {start:dd/MM/yyyy} al {end:dd/MM/yyyy}";
    }

    private static string EnsureWeekPrefix(string value)
    {
        var cleaned = value.Trim();
        return Normalize(cleaned).Contains("semana")
            ? cleaned
            : $"Semana {cleaned}";
    }

    private static string ReadCell(IXLRangeRow row, int columnIndex)
    {
        if (columnIndex <= 0)
            return "";

        var value = row.Cell(columnIndex).GetFormattedString().Trim();
        return string.Join("\n", value.Split('\n').Select(static line => line.Trim()).Where(static line => line.Length > 0));
    }

    private static DateOnly? ReadDate(IXLRangeRow row, int columnIndex)
    {
        if (columnIndex <= 0)
            return null;

        var cell = row.Cell(columnIndex);
        if (cell.TryGetValue<DateTime>(out var dateTime))
            return DateOnly.FromDateTime(dateTime);

        var raw = cell.GetFormattedString().Trim();
        if (DateOnly.TryParse(raw, ColombianCulture, DateTimeStyles.None, out var date))
            return date;
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return date;

        return null;
    }

    private static int ReadDurationMinutes(IXLRangeRow row, int columnIndex)
    {
        if (columnIndex <= 0)
            return 0;

        var cell = row.Cell(columnIndex);
        if (cell.TryGetValue<double>(out var numericValue))
        {
            if (numericValue > 0 && numericValue < 1)
                return (int)Math.Round(numericValue * 24 * 60);
            if (numericValue > 0 && numericValue <= 8)
                return (int)Math.Round(numericValue * 60);
            return (int)Math.Round(numericValue);
        }

        var raw = cell.GetFormattedString().Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        var timeMatch = Regex.Match(raw, @"(?<hours>\d{1,2})\s*:\s*(?<minutes>\d{1,2})");
        if (timeMatch.Success)
            return (int.Parse(timeMatch.Groups["hours"].Value, CultureInfo.InvariantCulture) * 60)
                + int.Parse(timeMatch.Groups["minutes"].Value, CultureInfo.InvariantCulture);

        var hourMinuteMatch = Regex.Match(raw, @"(?:(?<hours>\d+(?:[.,]\d+)?)\s*h(?:oras?)?)?\s*(?:(?<minutes>\d+)\s*m(?:in(?:utos?)?)?)?");
        if (hourMinuteMatch.Success && (hourMinuteMatch.Groups["hours"].Success || hourMinuteMatch.Groups["minutes"].Success))
        {
            var hours = hourMinuteMatch.Groups["hours"].Success
                ? double.Parse(hourMinuteMatch.Groups["hours"].Value.Replace(',', '.'), CultureInfo.InvariantCulture)
                : 0;
            var minutes = hourMinuteMatch.Groups["minutes"].Success
                ? int.Parse(hourMinuteMatch.Groups["minutes"].Value, CultureInfo.InvariantCulture)
                : 0;
            return (int)Math.Round(hours * 60) + minutes;
        }

        var numberMatch = Regex.Match(raw, @"\d+");
        return numberMatch.Success ? int.Parse(numberMatch.Value, CultureInfo.InvariantCulture) : 0;
    }

    private static string FormatDay(DateOnly? date)
    {
        if (!date.HasValue)
            return "";

        var dayName = ColombianCulture.DateTimeFormat.GetDayName(date.Value.DayOfWeek);
        return ColombianCulture.TextInfo.ToTitleCase(dayName);
    }

    private static string BuildWorkoutTitle(string detail)
    {
        var firstLine = detail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? detail;
        return firstLine.Length <= 70 ? firstLine : $"{firstLine[..67]}...";
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string ResolveColumnName(IReadOnlyList<PlanRioColumnHeader> headers, int columnIndex) =>
        headers.FirstOrDefault(header => header.Index == columnIndex)?.Name ?? "";

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var chars = normalized
            .Where(static c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(static c => char.ToLowerInvariant(c))
            .ToArray();
        return new string(chars).Normalize(NormalizationForm.FormC);
    }

    private sealed record PlanRioColumnHeader(int Index, string Name);
}
