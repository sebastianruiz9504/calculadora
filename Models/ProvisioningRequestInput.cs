namespace CotizadorInterno.Web.Models;

public sealed class ProvisioningRequestInput
{
    public string? Source { get; set; }
    public string? BusinessId { get; set; }
    public ProvisioningRequester? Requester { get; set; }
    public ProvisioningClient? Cliente { get; set; }
    public ProvisioningAprovisionamiento? Aprovisionamiento { get; set; }
    public ProvisioningResultado? Resultado { get; set; }
    public List<ProvisioningLineItem> LineItems { get; set; } = new();
    public ProvisioningAttachment? Attachment { get; set; }
}

public sealed class ProvisioningRequester
{
    public string? SystemUserId { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
}

public sealed class ProvisioningClient
{
    public string? ClienteId { get; set; }
    public string? Nombre { get; set; }
}

public sealed class ProvisioningAprovisionamiento
{
    public string? Fecha { get; set; }
    public string? TipoContratoCode { get; set; }
    public string? TipoContratoLabel { get; set; }
}

public sealed class ProvisioningResultado
{
    public decimal Puntaje { get; set; }
    public decimal Comision { get; set; }
}

public sealed class ProvisioningLineItem
{
    public string? LineId { get; set; }
    public string? ProductoId { get; set; }
    public string? ProductoNombre { get; set; }
    public decimal Cantidad { get; set; }
    public decimal Number { get; set; }
}

public sealed class ProvisioningAttachment
{
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public string? Base64 { get; set; }
}