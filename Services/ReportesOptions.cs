namespace CotizadorInterno.Web.Services;

public sealed class AzureOpenAIOptions
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string DeploymentName { get; set; } = "";
    public string ApiVersion { get; set; } = "2024-02-15-preview";
    public int TimeoutSeconds { get; set; } = 180;
    public decimal Temperature { get; set; } = 0.2m;
    public int MaxTokens { get; set; } = 7000;
    public string TokenParameterName { get; set; } = "max_tokens";
    public bool IncludeTemperature { get; set; } = true;
    public string ReasoningEffort { get; set; } = "";
    public string Verbosity { get; set; } = "";
}

public sealed class ReportesOptions
{
    public string PromptVersion { get; set; } = "m365-soporte-cloud-v1";
    public string DefaultCorporateColor { get; set; } = "#0f766e";
    public int MaxTicketsInPrompt { get; set; } = 60;
    public int MaxSecurityItemsInPrompt { get; set; } = 25;
    public ReportesClientDataverseOptions Client { get; set; } = new();
    public ReportesTicketDataverseOptions Ticket { get; set; } = new();
    public ReportesGeneratedReportDataverseOptions GeneratedReport { get; set; } = new();
}

public sealed class ReportesClientDataverseOptions
{
    public string TableLogicalName { get; set; } = "cr07a_cliente";
    public string TableSetName { get; set; } = "cr07a_clientes";
    public string IdField { get; set; } = "cr07a_clienteid";
    public string NameField { get; set; } = "cr07a_nombre";
    public string[] LogoFieldCandidates { get; set; } =
    {
        "cr07a_logocliente",
        "cr07a_logo",
        "cr07a_logourl"
    };
    public string[] ColorFieldCandidates { get; set; } =
    {
        "cr07a_colorcorporativo",
        "cr07a_color",
        "cr07a_brandcolor"
    };
}

public sealed class ReportesTicketDataverseOptions
{
    public string TableLogicalName { get; set; } = "cr07a_ticket";
    public string TableSetName { get; set; } = "cr07a_tickets";
    public string IdField { get; set; } = "cr07a_ticketid";
    public string PrimaryNameField { get; set; } = "cr07a_tituloticket";
    public string TitleField { get; set; } = "cr07a_tituloticket";
    public string DescriptionField { get; set; } = "cr07a_descripcion";
    public string CreationDateField { get; set; } = "cr07a_fechacreacion";
    public string StateField { get; set; } = "cr07a_estado";
    public string TypeField { get; set; } = "cr07a_tipo";
    public string ClientLookupField { get; set; } = "cr07a_cliente";
    public string[] ClientLookupValueFilterFields { get; set; } =
    {
        "_cr07a_cliente_value",
        "_cr07a_clienteid_value"
    };
    public string CategoryField { get; set; } = "cr07a_categoria";
    public string CreatedByField { get; set; } = "createdby";
    public string HoursTakenField { get; set; } = "cr07a_horastomadas";
    public string MethodField { get; set; } = "cr07a_metodo";
    public string SolutionField { get; set; } = "cr07a_solucion";
    public string ModifiedOnField { get; set; } = "modifiedon";
    public string CreatedOnFallbackField { get; set; } = "createdon";
}

public sealed class ReportesGeneratedReportDataverseOptions
{
    public string TableLogicalName { get; set; } = "cr07a_m365generatedreport";
    public string TableSetName { get; set; } = "cr07a_m365generatedreports";
    public string IdField { get; set; } = "cr07a_m365generatedreportid";
    public string PrimaryNameField { get; set; } = "cr07a_name";
    public string ClientLookupField { get; set; } = "cr07a_cliente";
    public string ClientNavigationProperty { get; set; } = "cr07a_cliente";
    public string InternalClientIdField { get; set; } = "cr07a_clienteidinterno";
    public string PeriodoField { get; set; } = "cr07a_periodo";
    public string HtmlGeneradoField { get; set; } = "cr07a_htmlgenerado";
    public string EstadoField { get; set; } = "cr07a_estado";
    public string FechaGeneracionField { get; set; } = "cr07a_fechageneracion";
    public string PromptVersionField { get; set; } = "cr07a_promptversion";
    public string ErroresField { get; set; } = "cr07a_errores";
}
