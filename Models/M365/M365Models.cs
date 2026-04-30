namespace CotizadorInterno.Web.Models.M365;

public sealed class M365ConnectUrlRequest
{
    public string ClienteId { get; set; } = "";
    public string TenantIdOrDomain { get; set; } = "";
}

public sealed class M365ConnectUrlResult
{
    public string Url { get; set; } = "";
    public string ClienteId { get; set; } = "";
    public string TenantHint { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public IReadOnlyList<string> Scopes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> RequestedPermissions { get; set; } = Array.Empty<string>();
}

public sealed class M365ConsentCallbackRequest
{
    public string Tenant { get; set; } = "";
    public string AdminConsent { get; set; } = "";
    public string State { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Error { get; set; } = "";
    public string ErrorDescription { get; set; } = "";
}

public sealed class M365ConsentCallbackResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string ClienteId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string EstadoConexion { get; set; } = "";
    public string RecordId { get; set; } = "";
}

public sealed class M365TestConnectionRequest
{
    public string ClienteId { get; set; } = "";
    public string TenantId { get; set; } = "";
}

public sealed class M365TestConnectionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string ClienteId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string TenantDisplayName { get; set; } = "";
    public string GraphEndpoint { get; set; } = "";
    public string EstadoConexion { get; set; } = "";
    public string TestedAt { get; set; } = "";
}

public sealed class M365TenantConnectionRecord
{
    public string RecordId { get; set; } = "";
    public string ClienteId { get; set; } = "";
    public string ClienteNombre { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string TenantHint { get; set; } = "";
    public string EstadoConexion { get; set; } = "";
    public string FechaConexion { get; set; } = "";
    public bool AdminConsent { get; set; }
    public string PermisosSolicitados { get; set; } = "";
    public string ResultadoConsentimiento { get; set; } = "";
}
