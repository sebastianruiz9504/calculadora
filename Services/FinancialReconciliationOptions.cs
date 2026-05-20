namespace CotizadorInterno.Web.Services;

public sealed class FinancialReconciliationOptions
{
    public bool Enabled { get; set; } = true;
    public bool RunOnStartup { get; set; }
    public int RunDayOfMonth { get; set; } = 2;
    public string RunTime { get; set; } = "07:00";
    public string TimeZoneId { get; set; } = "SA Pacific Standard Time";
    public int PeriodOffsetMonths { get; set; } = 1;
    public string RecipientEmail { get; set; } = "sruiz@digitaltechcolombia.com";
    public string SenderUserPrincipalName { get; set; } = "sruiz@digitaltechcolombia.com";
    public decimal DifferenceTolerance { get; set; } = 1m;
    public bool SendWhenNoDifferences { get; set; } = true;
    public bool ExcludeAnnulledSiigoInvoices { get; set; } = true;
    public string EmailFlowUrl { get; set; } = "";
}
