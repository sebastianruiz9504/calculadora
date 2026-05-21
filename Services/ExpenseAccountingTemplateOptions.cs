namespace CotizadorInterno.Web.Services;

public sealed class ExpenseAccountingTemplateOptions
{
    public bool Enabled { get; set; } = true;
    public bool RunOnStartup { get; set; }
    public string RunDayOfWeek { get; set; } = "Monday";
    public string RunTime { get; set; } = "08:20";
    public string TimeZoneId { get; set; } = "SA Pacific Standard Time";
    public int LookbackDays { get; set; } = 45;
    public string MovementType { get; set; } = "Compra";
    public bool Overwrite { get; set; }
    public bool DryRun { get; set; }
}
