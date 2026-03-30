namespace CotizadorInterno.Web.Services;

public sealed class SupplierPortalOptions
{
    public string CertificateRequestFlowUrl { get; set; } = "";
    public string ExpensesTableSetName { get; set; } = "cr07a_gastodelaempresas";
    public string ExpensesTableName { get; set; } = "cr07a_gastodelaempresa";
    public string ExpensesIdField { get; set; } = "cr07a_gastodelaempresaid";
    public string ExpensesDateField { get; set; } = "createdon";
    public string ExpensesDateFieldKind { get; set; } = "date-time";
    public string CompanyName { get; set; } = "Digital Tech Copiers SAS";
    public string CompanyNit { get; set; } = "900.399.875";
    public string CompanyAddress { get; set; } = "";
    public string CompanyCity { get; set; } = "";
}
