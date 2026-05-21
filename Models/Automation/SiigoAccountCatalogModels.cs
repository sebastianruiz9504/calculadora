namespace CotizadorInterno.Web.Models.Automation;

public sealed class SiigoObservedAccountDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Source { get; set; } = "";
    public int Uses { get; set; }
    public DateOnly? LastSeenDate { get; set; }
}

public sealed class AccountCatalogSyncResultDto
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int ObservedAccounts { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
}
