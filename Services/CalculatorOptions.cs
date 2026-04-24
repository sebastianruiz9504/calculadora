namespace CotizadorInterno.Web.Services;

public sealed class CalculatorOptions
{
    public string ProvisioningRequestFlowUrl { get; set; } = "";
    public string ProvisioningApprovalCallbackUrl { get; set; } = "";
    public string ProvisioningApprovalCallbackSecret { get; set; } = "";
    public string ProvisioningRequestStorePath { get; set; } = "";
}
