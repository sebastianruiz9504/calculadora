namespace CotizadorInterno.Web.Services;

public sealed class CashFlowImportOptions
{
    public bool Enabled { get; set; } = true;
    public bool RunOnStartup { get; set; }
    public string RunDayOfWeek { get; set; } = "Monday";
    public string RunTime { get; set; } = "07:30";
    public string TimeZoneId { get; set; } = "SA Pacific Standard Time";
    public bool DryRun { get; set; }
    public bool IncludeFutureRows { get; set; }
    public bool SendSummaryEmail { get; set; } = true;
    public bool SendSummaryEmailOnDryRun { get; set; }
    public string SummaryRecipientEmail { get; set; } = "sruiz@digitaltechcolombia.com";
    public string DriveId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string FileName { get; set; } = "Pagos de facturas copiers y cloud.xlsx";
    public string LocalFilePath { get; set; } = "";
    public string CloudTableName { get; set; } = "Flujodecajacloud";
    public string CopiersTableName { get; set; } = "Flujodecajacopiers";
    public string CloudBankAccountCode { get; set; } = "11100504";
    public string CloudBankAccountName { get; set; } = "Bancolombia Cloud 8100";
    public string CopiersBankAccountCode { get; set; } = "11100505";
    public string CopiersBankAccountName { get; set; } = "Bancolombia Copiers 7316";
}
