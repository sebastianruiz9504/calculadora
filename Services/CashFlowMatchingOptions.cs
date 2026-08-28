namespace CotizadorInterno.Web.Services;

public sealed class CashFlowMatchingOptions
{
    public bool Enabled { get; set; } = true;
    public bool RunOnStartup { get; set; }
    public string RunDayOfWeek { get; set; } = "Monday";
    public string RunTime { get; set; } = "07:45";
    public string TimeZoneId { get; set; } = "SA Pacific Standard Time";
    public bool DryRun { get; set; }
    public int LookbackDays { get; set; } = 180;
    public decimal DifferenceTolerance { get; set; } = 2000m;
}
