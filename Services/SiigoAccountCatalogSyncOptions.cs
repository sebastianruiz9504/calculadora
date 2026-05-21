namespace CotizadorInterno.Web.Services;

public sealed class SiigoAccountCatalogSyncOptions
{
    public bool Enabled { get; set; } = true;
    public bool RunOnStartup { get; set; }
    public int RunDayOfMonth { get; set; } = 1;
    public string RunTime { get; set; } = "06:00";
    public string TimeZoneId { get; set; } = "SA Pacific Standard Time";
    public int LookbackMonths { get; set; } = 6;
}
