namespace CotizadorInterno.Web.Models.PlanRio;

public sealed class PlanRioWorkoutDto
{
    public int Id { get; set; }
    public DateOnly? Date { get; set; }
    public string Day { get; set; } = "";
    public string WeekKey { get; set; } = "";
    public string WeekLabel { get; set; } = "";
    public string WeekRaw { get; set; } = "";
    public string Workout { get; set; } = "";
    public string Detail { get; set; } = "";
    public int GoalMin { get; set; }
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
