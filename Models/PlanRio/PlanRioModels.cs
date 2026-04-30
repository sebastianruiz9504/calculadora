namespace CotizadorInterno.Web.Models.PlanRio;

public sealed class PlanRioWorkoutDto
{
    public int Id { get; set; }
    public string RecordId { get; set; } = "";
    public DateOnly? Date { get; set; }
    public string Day { get; set; } = "";
    public string WeekKey { get; set; } = "";
    public string WeekLabel { get; set; } = "";
    public string WeekRaw { get; set; } = "";
    public string Workout { get; set; } = "";
    public string Detail { get; set; } = "";
    public int GoalMin { get; set; }
    public decimal Hours { get; set; }
    public string Phase { get; set; } = "";
    public string Discipline { get; set; } = "";
    public string VolumeObjective { get; set; } = "";
    public string IntensityZone { get; set; } = "";
    public string Nutrition { get; set; } = "";
    public string Objective { get; set; } = "";
    public string Status { get; set; } = "";
    public int ActualMin { get; set; }
    public decimal? ActualDistance { get; set; }
    public int AverageHeartRate { get; set; }
    public int? AveragePower { get; set; }
    public string Notes { get; set; } = "";
    public string SourceSheet { get; set; } = "";
    public int SourceRow { get; set; }
}

public sealed class PlanRioWorkoutSaveRequestDto
{
    public string RecordId { get; set; } = "";
    public int DurationMinutes { get; set; }
    public decimal Distance { get; set; }
    public int AverageHeartRate { get; set; }
    public int? AveragePower { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class PlanRioWorkoutSaveResultDto
{
    public string Message { get; set; } = "";
    public PlanRioWorkoutDto? Record { get; set; }
}

public sealed class PlanRioWeekDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int WorkoutCount { get; set; }
    public bool IsSelected { get; set; }
}

public sealed class PlanRioPageViewModel
{
    public string WeekLabel { get; set; } = "Semana no disponible";
    public IReadOnlyList<PlanRioWorkoutDto> Workouts { get; set; } = Array.Empty<PlanRioWorkoutDto>();
    public IReadOnlyList<PlanRioWeekDto> Weeks { get; set; } = Array.Empty<PlanRioWeekDto>();
    public string SourceSheet { get; set; } = "plan corregido";
    public string SourcePath { get; set; } = "";
    public string SourceStatus { get; set; } = "";
    public string DetailColumnName { get; set; } = "";
    public string WorkoutColumnName { get; set; } = "";
    public string WeekColumnName { get; set; } = "";
}
