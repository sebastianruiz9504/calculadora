namespace CotizadorInterno.Web.Services;

public sealed class AzureOpenAIOptions
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string DeploymentName { get; set; } = "";
    public string ApiVersion { get; set; } = "2025-01-01-preview";
    public int TimeoutSeconds { get; set; } = 600;
    public decimal Temperature { get; set; } = 0.2m;
    public int MaxTokens { get; set; } = 16000;
    public string TokenParameterName { get; set; } = "max_completion_tokens";
    public bool IncludeTemperature { get; set; } = false;
    public string ReasoningEffort { get; set; } = "high";
    public string Verbosity { get; set; } = "high";
}

public sealed class ReportesOptions
{
    public string PromptVersion { get; set; } = "m365-soporte-cloud-v3-analysis-template";
    public string DefaultCorporateColor { get; set; } = "#103975";
    public int MaxTicketsInPrompt { get; set; } = 40;
    public int MaxSecurityItemsInPrompt { get; set; } = 15;
    public ReportesClientDataverseOptions Client { get; set; } = new();
    public ReportesTicketDataverseOptions Ticket { get; set; } = new();
    public ReportesGeneratedReportDataverseOptions GeneratedReport { get; set; } = new();
    public ReportesAttachmentDataverseOptions Attachment { get; set; } = new();
    public ReportesEmailOptions Email { get; set; } = new();
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
    public string[] ContactNameFieldCandidates { get; set; } =
    {
        "cr07a_nombrepersonaacargo",
        "cr07a_personaacargo",
        "cr07a_responsable",
        "cr07a_nombrecontacto",
        "cr07a_contacto"
    };
    public string[] ContactEmailFieldCandidates { get; set; } =
    {
        "cr07a_correoelectronico",
        "cr07a_correo",
        "cr07a_email",
        "cr07a_correocontacto",
        "cr07a_emailcontacto",
        "emailaddress1"
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

public sealed class ReportesAttachmentDataverseOptions
{
    public string TableLogicalName { get; set; } = "cr07a_m365reportattachment";
    public string TableSetName { get; set; } = "cr07a_m365reportattachments";
    public string IdField { get; set; } = "cr07a_m365reportattachmentid";
    public string PrimaryNameField { get; set; } = "cr07a_name";
    public string ReportLookupField { get; set; } = "cr07a_reporte";
    public string ReportNavigationProperty { get; set; } = "cr07a_reporte";
    public string InternalReportIdField { get; set; } = "cr07a_reporteidinterno";
    public string FileNameField { get; set; } = "cr07a_filename";
    public string ContentTypeField { get; set; } = "cr07a_contenttype";
    public string SizeField { get; set; } = "cr07a_size";
    public string UploadDateField { get; set; } = "cr07a_fechacarga";
}

public sealed class ReportesEmailOptions
{
    public bool UseSignedInUserSender { get; set; } = true;
    public string SenderUserPrincipalName { get; set; } = "sruiz@digitaltechcolombia.com";
    public string DefaultSubjectTemplate { get; set; } = "Informe mensual Microsoft 365 - {ClienteNombre} - {Periodo}";
    public string DefaultBodyTemplate { get; set; } =
        "Hola {ContactoNombre},\n\nAdjunto encontrara el informe mensual de Microsoft 365 de {ClienteNombre} correspondiente al periodo {Periodo}, junto con los anexos cargados para el reporte.\n\nQuedamos atentos a cualquier comentario.\n\nSaludos,\nDigital Tech";
    public ReportesEmailRecipientOverrideOptions[] RecipientOverrides { get; set; } = Array.Empty<ReportesEmailRecipientOverrideOptions>();
}

public sealed class ReportesEmailRecipientOverrideOptions
{
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string[] ClientNameContains { get; set; } = Array.Empty<string>();
    public string[] To { get; set; } = Array.Empty<string>();
    public string[] Cc { get; set; } = Array.Empty<string>();
}
