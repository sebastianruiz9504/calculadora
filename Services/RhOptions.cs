namespace CotizadorInterno.Web.Services;

public sealed class RhOptions
{
    public string VacationApprovalFlowUrl { get; set; } = "";
    public string VacationRequestNotesField { get; set; } = "";
    public string VacationRequestFormatField { get; set; } = "cr07a_formato";
    public string VacationRequestFormatFileNameField { get; set; } = "cr07a_formato_name";
    public string CompanyName { get; set; } = "Digital Tech Copiers SAS";
    public string CompanyNit { get; set; } = "900.399.875";
    public string CompanyAddress { get; set; } = "";
    public string CompanyCity { get; set; } = "";
}
