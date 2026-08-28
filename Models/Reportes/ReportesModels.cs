using System.Text.Json;

namespace CotizadorInterno.Web.Models.Reportes;

public sealed class ReporteGenerarRequest
{
    public string ClienteId { get; set; } = "";
    public string Periodo { get; set; } = "";
    public string RecomendacionMensual { get; set; } = "";
}

public sealed class ReporteGenerarResult
{
    public string IdReporte { get; set; } = "";
    public string Html { get; set; } = "";
    public string Estado { get; set; } = "";
    public string Error { get; set; } = "";
}

public sealed class ReporteMonthlyInput
{
    public ReporteClienteData Cliente { get; set; } = new();
    public string Periodo { get; set; } = "";
    public DateOnly FechaInicio { get; set; }
    public DateOnly FechaFinExclusiva { get; set; }
    public IReadOnlyList<ReporteTicketData> Tickets { get; set; } = Array.Empty<ReporteTicketData>();
    public ReporteSecuritySnapshotData? SecuritySnapshot { get; set; }
}

public sealed class ReporteClienteData
{
    public string ClienteId { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string PersonaACargo { get; set; } = "";
    public string Correo { get; set; } = "";
    public string Logo { get; set; } = "";
    public string ColorCorporativo { get; set; } = "";
}

public sealed class ReporteTicketData
{
    public string RecordId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string CreationDateValue { get; set; } = "";
    public string CreationDateDisplay { get; set; } = "";
    public string StateLabel { get; set; } = "";
    public string TypeLabel { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string CategoryLabel { get; set; } = "";
    public string CreatorName { get; set; } = "";
    public decimal HoursTaken { get; set; }
    public string MethodLabel { get; set; } = "";
    public string Solution { get; set; } = "";
    public string ModifiedOnDisplay { get; set; } = "";
}

public sealed class ReporteSecuritySnapshotData
{
    public string RecordId { get; set; } = "";
    public string ClienteId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string Periodo { get; set; } = "";
    public decimal SecureScoreActual { get; set; }
    public decimal SecureScoreMaximo { get; set; }
    public int AlertasHigh { get; set; }
    public int AlertasMedium { get; set; }
    public int AlertasLow { get; set; }
    public int IncidentesActivos { get; set; }
    public int IncidentesResueltos { get; set; }
    public string RecomendacionesTopJson { get; set; } = "";
    public string AlertasJson { get; set; } = "";
    public string IncidentesJson { get; set; } = "";
    public string FechaConsulta { get; set; } = "";
    public string EstadoConsulta { get; set; } = "";
    public string ErrorConsulta { get; set; } = "";
}

public sealed class ReporteHtmlGeneradoRecord
{
    public string RecordId { get; set; } = "";
    public string ClienteId { get; set; } = "";
    public string ClienteNombre { get; set; } = "";
    public string Periodo { get; set; } = "";
    public string HtmlGenerado { get; set; } = "";
    public string Estado { get; set; } = "";
    public string FechaGeneracion { get; set; } = "";
    public string PromptVersion { get; set; } = "";
    public string Errores { get; set; } = "";
}

public sealed class ReporteEmailAttachment
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

public class ReporteSendEmailRequest
{
    public string SubjectTemplate { get; set; } = "";
    public string BodyTemplate { get; set; } = "";
}

public sealed class ReporteTestEmailRequest : ReporteSendEmailRequest
{
    public string TestEmail { get; set; } = "";
}

public sealed class ReporteConsolidadoPayload
{
    public ReporteClienteData Cliente { get; set; } = new();
    public ReportePeriodoPayload Periodo { get; set; } = new();
    public string RecomendacionMensual { get; set; } = "";
    public ReporteTicketSummaryPayload ResumenTickets { get; set; } = new();
    public ReporteTicketMetricsPayload MetricasTickets { get; set; } = new();
    public IReadOnlyList<ReporteTicketPromptItem> TicketsRelevantes { get; set; } = Array.Empty<ReporteTicketPromptItem>();
    public ReporteSecurityPromptPayload SeguridadMicrosoft365 { get; set; } = new();
}

public sealed class ReportePeriodoPayload
{
    public string Valor { get; set; } = "";
    public string FechaInicio { get; set; } = "";
    public string FechaFin { get; set; } = "";
}

public sealed class ReporteTicketSummaryPayload
{
    public int TotalTickets { get; set; }
    public decimal TotalHoras { get; set; }
    public decimal PromedioHoras { get; set; }
    public string Resumen { get; set; } = "";
}

public sealed class ReporteTicketMetricsPayload
{
    public IReadOnlyList<ReporteBreakdownItem> PorEstado { get; set; } = Array.Empty<ReporteBreakdownItem>();
    public IReadOnlyList<ReporteBreakdownItem> PorTipo { get; set; } = Array.Empty<ReporteBreakdownItem>();
    public IReadOnlyList<ReporteBreakdownItem> PorCategoria { get; set; } = Array.Empty<ReporteBreakdownItem>();
    public IReadOnlyList<ReporteBreakdownItem> PorMetodo { get; set; } = Array.Empty<ReporteBreakdownItem>();
    public IReadOnlyList<ReporteBreakdownItem> PorCreador { get; set; } = Array.Empty<ReporteBreakdownItem>();
}

public sealed class ReporteBreakdownItem
{
    public string Label { get; set; } = "";
    public int Total { get; set; }
    public decimal Horas { get; set; }
    public decimal Porcentaje { get; set; }
}

public sealed class ReporteTicketPromptItem
{
    public string RecordId { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Fecha { get; set; } = "";
    public string Estado { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Metodo { get; set; } = "";
    public string Creador { get; set; } = "";
    public decimal Horas { get; set; }
    public string Descripcion { get; set; } = "";
    public string Solucion { get; set; } = "";
}

public sealed class ReporteSecurityPromptPayload
{
    public bool TieneSnapshot { get; set; }
    public string EstadoConsulta { get; set; } = "";
    public string ErrorConsulta { get; set; } = "";
    public string TenantId { get; set; } = "";
    public decimal SecureScoreActual { get; set; }
    public decimal SecureScoreMaximo { get; set; }
    public decimal SecureScorePorcentaje { get; set; }
    public int AlertasHigh { get; set; }
    public int AlertasMedium { get; set; }
    public int AlertasLow { get; set; }
    public int IncidentesActivos { get; set; }
    public int IncidentesResueltos { get; set; }
    public IReadOnlyList<JsonElement> Alertas { get; set; } = Array.Empty<JsonElement>();
    public IReadOnlyList<JsonElement> Incidentes { get; set; } = Array.Empty<JsonElement>();
}
