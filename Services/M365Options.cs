namespace CotizadorInterno.Web.Services;

public sealed class M365Options
{
    public string AuthorityHost { get; set; } = "https://login.microsoftonline.com";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string GraphBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";
    public string[] Scopes { get; set; } = { "https://graph.microsoft.com/.default" };
    public string[] RequestedPermissions { get; set; } =
    {
        "Organization.Read.All",
        "Directory.Read.All",
        "Reports.Read.All",
        "User.Read.All",
        "Group.Read.All"
    };
    public int StateLifetimeMinutes { get; set; } = 60;
    public string CertificateThumbprint { get; set; } = "";
    public string CertificateStoreName { get; set; } = "My";
    public string CertificateStoreLocation { get; set; } = "CurrentUser";
    public string CertificatePath { get; set; } = "";
    public string CertificatePassword { get; set; } = "";
    public M365DataverseOptions Dataverse { get; set; } = new();
}

public sealed class M365DataverseOptions
{
    public string ConnectionTableLogicalName { get; set; } = "cr07a_m365tenantconnection";
    public string ConnectionTableSetName { get; set; } = "cr07a_m365tenantconnections";
    public string ConnectionIdField { get; set; } = "cr07a_m365tenantconnectionid";
    public string PrimaryNameField { get; set; } = "cr07a_name";
    public string ClientLookupField { get; set; } = "cr07a_cliente";
    public string ClientNavigationProperty { get; set; } = "cr07a_cliente";
    public string InternalClientIdField { get; set; } = "cr07a_clienteidinterno";
    public string TenantIdField { get; set; } = "cr07a_tenantid";
    public string TenantHintField { get; set; } = "cr07a_tenanthint";
    public string EstadoConexionField { get; set; } = "cr07a_estadoconexion";
    public string FechaConexionField { get; set; } = "cr07a_fechaconexion";
    public string PermisosSolicitadosField { get; set; } = "cr07a_permisossolicitados";
    public string ResultadoConsentimientoField { get; set; } = "cr07a_resultadoconsentimiento";
    public string AdminConsentField { get; set; } = "cr07a_adminconsent";
    public string ScopeConsentidoField { get; set; } = "cr07a_scopeconsentido";
    public string ErrorField { get; set; } = "cr07a_error";
    public string ErrorDescriptionField { get; set; } = "cr07a_errordescripcion";
    public string LastTestDateField { get; set; } = "cr07a_fechaultimaprueba";
    public string LastTestSuccessField { get; set; } = "cr07a_ultimapruebaexitosa";
    public string LastTestResultField { get; set; } = "cr07a_resultadoultimaprueba";
    public M365SecuritySnapshotDataverseOptions SecuritySnapshot { get; set; } = new();
}

public sealed class M365SecuritySnapshotDataverseOptions
{
    public string TableLogicalName { get; set; } = "cr07a_m365securitysnapshot";
    public string TableSetName { get; set; } = "cr07a_m365securitysnapshots";
    public string IdField { get; set; } = "cr07a_m365securitysnapshotid";
    public string PrimaryNameField { get; set; } = "cr07a_name";
    public string ClientLookupField { get; set; } = "cr07a_cliente";
    public string ClientNavigationProperty { get; set; } = "cr07a_cliente";
    public string InternalClientIdField { get; set; } = "cr07a_clienteidinterno";
    public string TenantIdField { get; set; } = "cr07a_tenantid";
    public string PeriodoField { get; set; } = "cr07a_periodo";
    public string SecureScoreActualField { get; set; } = "cr07a_securescoreactual";
    public string SecureScoreMaximoField { get; set; } = "cr07a_securescoremaximo";
    public string AlertasHighField { get; set; } = "cr07a_alertashigh";
    public string AlertasMediumField { get; set; } = "cr07a_alertasmedium";
    public string AlertasLowField { get; set; } = "cr07a_alertaslow";
    public string IncidentesActivosField { get; set; } = "cr07a_incidentesactivos";
    public string IncidentesResueltosField { get; set; } = "cr07a_incidentesresueltos";
    public string RecomendacionesTopJsonField { get; set; } = "cr07a_recomendacionestopjson";
    public string AlertasJsonField { get; set; } = "cr07a_alertasjson";
    public string IncidentesJsonField { get; set; } = "cr07a_incidentesjson";
    public string FechaConsultaField { get; set; } = "cr07a_fechaconsulta";
    public string EstadoConsultaField { get; set; } = "cr07a_estadoconsulta";
    public string ErrorConsultaField { get; set; } = "cr07a_errorconsulta";
}
