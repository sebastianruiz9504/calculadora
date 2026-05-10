using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Reportes;

namespace CotizadorInterno.Web.Services;

public static class ReportesHtmlRenderer
{
    private const string CompanyName = "Digital Tech Copiers S.A.S.";
    private const string ContactEmail = "contacto@digitaltechcolombia.com";
    private const string ContactWeb = "www.digitaltechcolombia.com";

    public static string Render(ReporteConsolidadoPayload payload, ReporteAnalysisPayload analysis)
    {
        payload ??= new ReporteConsolidadoPayload();
        analysis = NormalizeAnalysis(payload, analysis);

        var accent = NormalizeCssColor(payload.Cliente?.ColorCorporativo, "#103975");
        var clientName = FirstNonEmpty(payload.Cliente?.Nombre, "Cliente");
        var tenant = FirstNonEmpty(payload.SeguridadMicrosoft365?.TenantId, "No disponible");
        var periodLabel = BuildPeriodLabel(payload.Periodo);
        var secureScoreLabel = payload.SeguridadMicrosoft365?.TieneSnapshot == true
            ? $"{FormatDecimal(payload.SeguridadMicrosoft365.SecureScorePorcentaje)}%"
            : "Sin snapshot";

        var html = new StringBuilder(96000);
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"es\">");
        html.AppendLine("<head>");
        html.AppendLine("<meta charset=\"utf-8\">");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        html.AppendLine("<title>Informe Mensual - " + H(clientName) + "</title>");
        html.AppendLine(BuildStyles(accent));
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine(RenderSidebar());
        html.AppendLine("<main class=\"main\">");
        html.AppendLine(RenderHero(payload, analysis, clientName, periodLabel, tenant));
        html.AppendLine(RenderMarcoIso(analysis));
        html.AppendLine(RenderWhyDigitalTech());
        html.AppendLine(RenderResumen(payload, analysis, secureScoreLabel));
        html.AppendLine(RenderSoportes(payload));
        html.AppendLine(RenderCumplimientoIso(analysis));
        html.AppendLine(RenderImplementacion(analysis));
        html.AppendLine(RenderSeguridad(payload, analysis));
        html.AppendLine(RenderHallazgos(analysis));
        html.AppendLine(RenderConclusiones(analysis));
        html.AppendLine(RenderRecomendaciones(analysis));
        html.AppendLine(RenderContacto());
        html.AppendLine("<footer class=\"footer\"><strong>DIGITAL TECH</strong> · Informe mensual generado para " + H(clientName) + " · " + H(periodLabel) + "</footer>");
        html.AppendLine("</main>");
        html.AppendLine("<div class=\"watermark\">INFORME MENSUAL</div>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");

        return html.ToString();
    }

    private static string RenderSidebar()
    {
        return """
<aside class="sidebar">
  <div class="sidebar-logo">
    <div class="sidebar-logo-title">DIGITAL TECH</div>
    <small>Cloud · Seguridad · Gestion TI</small>
  </div>
  <nav>
    <a href="#portada"><span>▣</span> Portada</a>
    <a href="#marco-iso"><span>▣</span> Marco Normativo</a>
    <a href="#porqueDT"><span>▣</span> Digital Tech</a>
    <a href="#resumen"><span>▣</span> Resumen</a>
    <a href="#soportes"><span>▣</span> Soportes</a>
    <a href="#cumplimiento-iso"><span>▣</span> ISO 27001</a>
    <a href="#implementacion"><span>▣</span> Implementacion</a>
    <a href="#seguridad"><span>▣</span> Seguridad</a>
    <a href="#hallazgos"><span>▣</span> Hallazgos</a>
    <a href="#conclusiones"><span>▣</span> Conclusiones</a>
    <a href="#recomendaciones"><span>▣</span> Recomendaciones</a>
    <a href="#contacto"><span>▣</span> Contacto</a>
  </nav>
  <div class="sidebar-footer"><span>Informe ejecutivo mensual</span></div>
</aside>
""";
    }

    private static string RenderHero(
        ReporteConsolidadoPayload payload,
        ReporteAnalysisPayload analysis,
        string clientName,
        string periodLabel,
        string tenant)
    {
        var logo = payload.Cliente?.Logo ?? "";
        var logoHtml = string.IsNullOrWhiteSpace(logo)
            ? "<div class=\"hero-brand-text\">" + H(clientName) + "</div>"
            : "<img class=\"hero-logo\" src=\"" + A(logo) + "\" alt=\"" + A(clientName) + "\">";

        return $$"""
<section class="hero" id="portada">
  <div class="hero-badge">Informe Mensual</div>
  {{logoHtml}}
  <h1>Informe Mensual de Soporte Cloud y Seguridad Microsoft 365</h1>
  <p class="hero-sub">{{H(analysis.HeroSubtitle)}}</p>
  <div class="hero-meta">
    <div class="hero-meta-item"><span class="label">Entregado a</span><strong class="value">{{H(clientName)}}</strong></div>
    <div class="hero-meta-item"><span class="label">Periodo</span><strong class="value">{{H(periodLabel)}}</strong></div>
    <div class="hero-meta-item"><span class="label">Tenant</span><strong class="value">{{H(tenant)}}</strong></div>
    <div class="hero-meta-item"><span class="label">Generado por</span><strong class="value">{{CompanyName}}</strong></div>
  </div>
</section>
""";
    }

    private static string RenderMarcoIso(ReporteAnalysisPayload analysis)
    {
        var rows = new StringBuilder();
        foreach (var item in analysis.Iso)
        {
            rows.AppendLine("<tr>");
            rows.AppendLine("<td>" + H(item.Dominio) + "</td>");
            rows.AppendLine("<td>" + H(item.Evidencia) + "</td>");
            rows.AppendLine("<td>" + H(item.Riesgo) + "</td>");
            rows.AppendLine("<td><span class=\"badge badge-neutral\">" + H(item.Madurez) + "</span></td>");
            rows.AppendLine("<td>" + H(item.Recomendacion) + "</td>");
            rows.AppendLine("</tr>");
        }

        return $$"""
<section class="section" id="marco-iso">
  <h2 class="section-title"><span>01</span> Alcance y Marco Normativo</h2>
  <p class="section-subtitle">Gestion operativa, continuidad de servicio y postura de seguridad Microsoft 365.</p>
  <div class="divider"></div>
  <p class="section-text">{{H(analysis.AlcanceMarco)}}</p>
  <div class="table-wrapper">
    <table>
      <thead><tr><th>Dominio</th><th>Evidencia</th><th>Riesgo</th><th>Madurez</th><th>Recomendacion</th></tr></thead>
      <tbody>{{rows}}</tbody>
    </table>
  </div>
</section>
""";
    }

    private static string RenderWhyDigitalTech()
    {
        return """
<section class="section section-alt" id="porqueDT">
  <h2 class="section-title"><span>DT</span> ¿Por que Digital Tech?</h2>
  <p class="section-subtitle">Acompanamiento especializado para operaciones Microsoft 365 y seguridad empresarial.</p>
  <div class="divider"></div>
  <div class="partners-grid">
    <div class="partner-card"><strong>Operacion gestionada</strong><p>Seguimiento mensual con evidencias, prioridades y acciones.</p></div>
    <div class="partner-card"><strong>Seguridad Microsoft 365</strong><p>Lectura ejecutiva de Secure Score, alertas e incidentes.</p></div>
    <div class="partner-card"><strong>Gestion por tickets</strong><p>Trazabilidad sobre actividades, horas, responsables y resultados.</p></div>
    <div class="partner-card"><strong>Mejora continua</strong><p>Recomendaciones accionables orientadas a madurez operativa.</p></div>
  </div>
</section>
""";
    }

    private static string RenderResumen(
        ReporteConsolidadoPayload payload,
        ReporteAnalysisPayload analysis,
        string secureScoreLabel)
    {
        var paragraphs = string.Join(Environment.NewLine, analysis.ResumenEjecutivo.Select(text => "<p class=\"section-text\">" + H(text) + "</p>"));
        var security = payload.SeguridadMicrosoft365 ?? new ReporteSecurityPromptPayload();
        var summary = payload.ResumenTickets ?? new ReporteTicketSummaryPayload();

        return $$"""
<section class="section" id="resumen">
  <h2 class="section-title"><span>02</span> 1. Resumen Ejecutivo</h2>
  <p class="section-subtitle">Lectura ejecutiva del periodo con indicadores operativos y de seguridad.</p>
  <div class="divider"></div>
  <div class="stats-grid">
    {{RenderStatCard(FormatInteger(summary.TotalTickets), "Tickets registrados", "Casos del periodo")}}
    {{RenderStatCard(FormatDecimal(summary.TotalHoras), "Horas reportadas", "Esfuerzo operativo")}}
    {{RenderStatCard(FormatDecimal(summary.PromedioHoras), "Promedio por ticket", "Horas por caso")}}
    {{RenderStatCard(secureScoreLabel, "Secure Score", security.TieneSnapshot ? "Snapshot Microsoft 365" : "Limitacion operativa")}}
    {{RenderStatCard(FormatInteger(security.AlertasHigh), "Alertas altas", "Microsoft 365 Defender")}}
    {{RenderStatCard(FormatInteger(security.IncidentesActivos), "Incidentes activos", "Seguimiento requerido")}}
  </div>
  {{paragraphs}}
</section>
""";
    }

    private static string RenderSoportes(ReporteConsolidadoPayload payload)
    {
        var rows = new StringBuilder();
        var tickets = payload.TicketsRelevantes ?? Array.Empty<ReporteTicketPromptItem>();
        if (tickets.Count == 0)
        {
            rows.AppendLine("<tr><td colspan=\"9\" class=\"empty-cell\">No se registraron tickets de soporte cloud para el periodo.</td></tr>");
        }
        else
        {
            foreach (var ticket in tickets)
            {
                rows.AppendLine("<tr>");
                rows.AppendLine("<td>" + H(ticket.Fecha) + "</td>");
                rows.AppendLine("<td><strong>" + H(ticket.Titulo) + "</strong><small>" + H(FirstNonEmpty(ticket.Descripcion, "Sin descripcion registrada.")) + "</small></td>");
                rows.AppendLine("<td><span class=\"badge badge-consultoria\">" + H(ticket.Tipo) + "</span></td>");
                rows.AppendLine("<td><span class=\"badge badge-security\">" + H(ticket.Categoria) + "</span></td>");
                rows.AppendLine("<td>" + H(ticket.Metodo) + "</td>");
                rows.AppendLine("<td>" + H(ticket.Creador) + "</td>");
                rows.AppendLine("<td>" + H(FormatDecimal(ticket.Horas)) + "</td>");
                rows.AppendLine("<td><span class=\"" + A(ResolveStateBadge(ticket.Estado)) + "\">" + H(ticket.Estado) + "</span></td>");
                rows.AppendLine("<td>" + H(FirstNonEmpty(ticket.Solucion, "Sin solucion documentada.")) + "</td>");
                rows.AppendLine("</tr>");
            }
        }

        return $$"""
<section class="section section-alt" id="soportes">
  <h2 class="section-title"><span>03</span> 1.1 Soportes Tecnicos Realizados</h2>
  <p class="section-subtitle">Detalle de tickets usados como evidencia operacional del informe.</p>
  <div class="divider"></div>
  <div class="table-wrapper support-table">
    <table>
      <thead><tr><th>Fecha</th><th>Ticket</th><th>Tipo</th><th>Categoria</th><th>Metodo</th><th>Creador</th><th>Horas</th><th>Estado</th><th>Resultado</th></tr></thead>
      <tbody>{{rows}}</tbody>
    </table>
  </div>
</section>
""";
    }

    private static string RenderCumplimientoIso(ReporteAnalysisPayload analysis)
    {
        var cards = string.Join(Environment.NewLine, analysis.Iso.Take(4).Select(item =>
            "<div class=\"stat-card\"><div class=\"stat-number small\">" + H(item.Madurez) + "</div><div class=\"stat-label\">" + H(item.Dominio) + "</div><div class=\"stat-sub\">" + H(item.Riesgo) + "</div></div>"));

        return $$"""
<section class="section" id="cumplimiento-iso">
  <h2 class="section-title"><span>04</span> Cumplimiento ISO 27001:2022</h2>
  <p class="section-subtitle">Alineacion operativa observada, no equivalente a certificacion ni auditoria formal.</p>
  <div class="divider"></div>
  <div class="stats-grid">{{cards}}</div>
</section>
""";
    }

    private static string RenderImplementacion(ReporteAnalysisPayload analysis)
    {
        var items = string.Join(Environment.NewLine, analysis.Implementacion.Select(text => "<li><span>✓</span><p>" + H(text) + "</p></li>"));
        return $$"""
<section class="section section-alt" id="implementacion">
  <h2 class="section-title"><span>05</span> 2. Implementacion</h2>
  <p class="section-subtitle">Actividades ejecutadas, cambios y gestion operativa evidenciada.</p>
  <div class="divider"></div>
  <ul class="impl-list">{{items}}</ul>
</section>
""";
    }

    private static string RenderSeguridad(ReporteConsolidadoPayload payload, ReporteAnalysisPayload analysis)
    {
        var security = payload.SeguridadMicrosoft365 ?? new ReporteSecurityPromptPayload();
        var score = Math.Clamp((double)security.SecureScorePorcentaje, 0, 100);
        var dashOffset = 565.48d - (565.48d * score / 100d);
        var recommendations = RenderJsonList(security.Recomendaciones, "No se recibieron recomendaciones de Secure Score para este periodo.");
        var alerts = RenderJsonList(security.Alertas, "No se recibieron alertas detalladas para este periodo.");
        var incidents = RenderJsonList(security.Incidentes, "No se recibieron incidentes detallados para este periodo.");
        var limitation = security.TieneSnapshot
            ? ""
            : "<div class=\"notice\"><strong>Limitacion operativa:</strong> " + H(FirstNonEmpty(security.ErrorConsulta, "No se encontro snapshot mensual de seguridad para este periodo.")) + "</div>";

        return $$"""
<section class="section" id="seguridad">
  <h2 class="section-title"><span>06</span> 3. Reporte de Seguridad</h2>
  <p class="section-subtitle">Secure Score, alertas, incidentes y recomendaciones Microsoft 365.</p>
  <div class="divider"></div>
  {{limitation}}
  <div class="gauge-container">
    <div class="gauge">
      <svg viewBox="0 0 220 220" aria-hidden="true">
        <circle class="gauge-bg" cx="110" cy="110" r="90"></circle>
        <circle class="gauge-fill" cx="110" cy="110" r="90" style="stroke-dashoffset:{{dashOffset.ToString("0.##", CultureInfo.InvariantCulture)}}"></circle>
      </svg>
      <div class="gauge-text"><strong>{{H(FormatDecimal(security.SecureScorePorcentaje))}}%</strong><span>Secure Score</span></div>
    </div>
    <div class="gauge-info">
      <h3>Postura de seguridad del tenant</h3>
      <p>{{H(analysis.SeguridadNarrativa)}}</p>
      <div class="comparison-item"><span>Actual</span><div class="bar-bg"><i class="bar-fill" style="width:{{score.ToString("0.##", CultureInfo.InvariantCulture)}}%"></i></div><strong>{{H(FormatDecimal(security.SecureScoreActual))}}</strong></div>
      <div class="comparison-item"><span>Maximo</span><div class="bar-bg"><i class="bar-fill bar-gray" style="width:100%"></i></div><strong>{{H(FormatDecimal(security.SecureScoreMaximo))}}</strong></div>
    </div>
  </div>
  <div class="stats-grid">
    {{RenderStatCard(FormatInteger(security.AlertasHigh), "Alertas altas", "Prioridad critica")}}
    {{RenderStatCard(FormatInteger(security.AlertasMedium), "Alertas medias", "Seguimiento")}}
    {{RenderStatCard(FormatInteger(security.AlertasLow), "Alertas bajas", "Monitoreo")}}
    {{RenderStatCard(FormatInteger(security.IncidentesActivos), "Incidentes activos", "Atencion requerida")}}
    {{RenderStatCard(FormatInteger(security.IncidentesResueltos), "Incidentes resueltos", "Cierre registrado")}}
  </div>
  <div class="three-columns">
    <div><h3>Recomendaciones top</h3>{{recommendations}}</div>
    <div><h3>Alertas relevantes</h3>{{alerts}}</div>
    <div><h3>Incidentes</h3>{{incidents}}</div>
  </div>
</section>
""";
    }

    private static string RenderHallazgos(ReporteAnalysisPayload analysis)
    {
        var items = string.Join(Environment.NewLine, analysis.Hallazgos.Select(item => $$"""
<article class="finding-card">
  <span class="badge badge-neutral">{{H(FirstNonEmpty(item.Severidad, "Observacion"))}}</span>
  <h3>{{H(item.Titulo)}}</h3>
  <p><strong>Evidencia:</strong> {{H(item.Evidencia)}}</p>
  <p><strong>Impacto:</strong> {{H(item.Impacto)}}</p>
  <p><strong>Accion:</strong> {{H(item.Accion)}}</p>
</article>
"""));

        return $$"""
<section class="section section-alt" id="hallazgos">
  <h2 class="section-title"><span>07</span> 4. Hallazgos de Auditoria</h2>
  <p class="section-subtitle">Observaciones derivadas de tickets, metricas y snapshot Microsoft 365.</p>
  <div class="divider"></div>
  <div class="finding-list">{{items}}</div>
</section>
""";
    }

    private static string RenderConclusiones(ReporteAnalysisPayload analysis)
    {
        var items = string.Join(Environment.NewLine, analysis.Conclusiones.Select(text => "<li><span>✓</span><p>" + H(text) + "</p></li>"));
        return $$"""
<section class="section" id="conclusiones">
  <h2 class="section-title"><span>08</span> Conclusiones</h2>
  <p class="section-subtitle">Cierre ejecutivo del periodo evaluado.</p>
  <div class="divider"></div>
  <ul class="impl-list">{{items}}</ul>
</section>
""";
    }

    private static string RenderRecomendaciones(ReporteAnalysisPayload analysis)
    {
        var rows = new StringBuilder();
        foreach (var item in analysis.Recomendaciones)
        {
            rows.AppendLine("<tr>");
            rows.AppendLine("<td><span class=\"badge badge-abierto\">" + H(item.Prioridad) + "</span></td>");
            rows.AppendLine("<td>" + H(item.Recomendacion) + "</td>");
            rows.AppendLine("<td>" + H(item.Evidencia) + "</td>");
            rows.AppendLine("<td>" + H(item.Responsable) + "</td>");
            rows.AppendLine("<td>" + H(item.Plazo) + "</td>");
            rows.AppendLine("</tr>");
        }

        return $$"""
<section class="section section-alt" id="recomendaciones">
  <h2 class="section-title"><span>09</span> Recomendaciones</h2>
  <p class="section-subtitle">Plan de accion sugerido con priorizacion ejecutiva.</p>
  <div class="divider"></div>
  <div class="table-wrapper">
    <table>
      <thead><tr><th>Prioridad</th><th>Recomendacion</th><th>Evidencia</th><th>Responsable sugerido</th><th>Plazo</th></tr></thead>
      <tbody>{{rows}}</tbody>
    </table>
  </div>
</section>
""";
    }

    private static string RenderContacto()
    {
        return $$"""
<section class="section" id="contacto">
  <h2 class="section-title"><span>10</span> Contacto</h2>
  <p class="section-subtitle">Equipo responsable del acompanamiento y mejora continua.</p>
  <div class="divider"></div>
  <div class="contact-card">
    <h3>{{CompanyName}}</h3>
    <div class="role">Equipo de Servicios Cloud y Seguridad</div>
    <div class="contact-item">{{ContactEmail}}</div>
    <div class="contact-item">{{ContactWeb}}</div>
  </div>
</section>
""";
    }

    private static string RenderStatCard(string number, string label, string sub)
    {
        return "<div class=\"stat-card\"><div class=\"stat-number\">" + H(number) + "</div><div class=\"stat-label\">" + H(label) + "</div><div class=\"stat-sub\">" + H(sub) + "</div></div>";
    }

    private static string RenderJsonList(IReadOnlyList<JsonElement> items, string emptyText)
    {
        if (items.Count == 0)
            return "<p class=\"muted\">" + H(emptyText) + "</p>";

        var builder = new StringBuilder();
        builder.AppendLine("<ul class=\"compact-list\">");
        foreach (var item in items.Take(8))
            builder.AppendLine("<li>" + H(SummarizeJsonElement(item)) + "</li>");
        builder.AppendLine("</ul>");
        return builder.ToString();
    }

    private static ReporteAnalysisPayload NormalizeAnalysis(ReporteConsolidadoPayload payload, ReporteAnalysisPayload? analysis)
    {
        analysis ??= new ReporteAnalysisPayload();
        var clientName = FirstNonEmpty(payload.Cliente?.Nombre, "el cliente");
        var summary = payload.ResumenTickets ?? new ReporteTicketSummaryPayload();
        var security = payload.SeguridadMicrosoft365 ?? new ReporteSecurityPromptPayload();

        if (string.IsNullOrWhiteSpace(analysis.HeroSubtitle))
            analysis.HeroSubtitle = $"Reporte ejecutivo del periodo {payload.Periodo?.Valor} para {clientName}, basado en tickets de soporte cloud y evidencia Microsoft 365 disponible.";

        if (analysis.ResumenEjecutivo.Count == 0)
        {
            analysis.ResumenEjecutivo = new[]
            {
                summary.Resumen,
                security.TieneSnapshot
                    ? $"El snapshot Microsoft 365 registra un Secure Score de {FormatDecimal(security.SecureScorePorcentaje)}%, con {security.AlertasHigh} alerta(s) alta(s) y {security.IncidentesActivos} incidente(s) activo(s)."
                    : "No se encontro snapshot mensual de seguridad Microsoft 365 para el periodo, lo cual limita la lectura completa de postura de seguridad."
            };
        }

        if (string.IsNullOrWhiteSpace(analysis.AlcanceMarco))
            analysis.AlcanceMarco = "El informe consolida evidencias de soporte cloud, continuidad operativa, gestion de tickets y postura de seguridad Microsoft 365, usando ISO/IEC 27001:2022 como marco de referencia operativo.";

        if (analysis.Iso.Count == 0)
        {
            analysis.Iso = new[]
            {
                new ReporteIsoAnalysisItem { Dominio = "Gestion de incidentes", Evidencia = $"{summary.TotalTickets} ticket(s) registrados en el periodo.", Riesgo = "Riesgo operativo dependiente del cierre y trazabilidad de casos.", Madurez = summary.TotalTickets > 0 ? "Medio" : "Sin evidencia", Recomendacion = "Mantener cierre documentado y clasificacion consistente." },
                new ReporteIsoAnalysisItem { Dominio = "Monitoreo de seguridad", Evidencia = security.TieneSnapshot ? $"Secure Score {FormatDecimal(security.SecureScorePorcentaje)}%." : "Sin snapshot mensual.", Riesgo = security.TieneSnapshot ? "Riesgo sujeto a alertas e incidentes activos." : "Visibilidad limitada de postura de seguridad.", Madurez = security.TieneSnapshot ? "Medio" : "Sin evidencia", Recomendacion = "Recolectar y revisar snapshot mensualmente." },
                new ReporteIsoAnalysisItem { Dominio = "Mejora continua", Evidencia = "Metricas por estado, categoria, metodo y creador.", Riesgo = "Priorizacion incompleta si no se revisan tendencias.", Madurez = "Medio", Recomendacion = "Convertir hallazgos en plan de accion mensual." }
            };
        }

        if (analysis.Implementacion.Count == 0)
            analysis.Implementacion = new[] { "Se evidencian actividades operativas y de soporte asociadas a la administracion cloud del periodo, de acuerdo con los tickets registrados." };

        if (string.IsNullOrWhiteSpace(analysis.SeguridadNarrativa))
            analysis.SeguridadNarrativa = security.TieneSnapshot
                ? "La postura de seguridad se interpreta a partir del snapshot mensual Microsoft 365, considerando Secure Score, alertas, incidentes y recomendaciones disponibles."
                : "La postura de seguridad no pudo evaluarse completamente por ausencia de snapshot mensual Microsoft 365.";

        if (analysis.Hallazgos.Count == 0)
        {
            analysis.Hallazgos = new[]
            {
                new ReporteFindingAnalysisItem { Titulo = "Seguimiento operacional del periodo", Evidencia = summary.Resumen, Impacto = "Permite visualizar carga operativa y esfuerzo reportado.", Accion = "Revisar tendencias y priorizar casos abiertos o de mayor consumo.", Severidad = "Media" },
                new ReporteFindingAnalysisItem { Titulo = security.TieneSnapshot ? "Postura Microsoft 365 observada" : "Limitacion de evidencia de seguridad", Evidencia = security.TieneSnapshot ? $"Secure Score {FormatDecimal(security.SecureScorePorcentaje)}%." : "No se encontro snapshot mensual.", Impacto = security.TieneSnapshot ? "Permite priorizar alertas, incidentes y recomendaciones." : "Reduce visibilidad de riesgos de seguridad.", Accion = "Formalizar revision mensual de seguridad.", Severidad = security.TieneSnapshot ? "Media" : "Alta" }
            };
        }

        if (analysis.Conclusiones.Count == 0)
            analysis.Conclusiones = new[] { "El periodo cuenta con evidencia operativa suficiente para revisar la gestion de soporte cloud.", "La postura de seguridad debe mantenerse bajo seguimiento mensual con acciones priorizadas." };

        if (analysis.Recomendaciones.Count == 0)
        {
            analysis.Recomendaciones = new[]
            {
                new ReporteRecommendationAnalysisItem { Prioridad = "Alta", Recomendacion = "Revisar y cerrar tickets pendientes o de mayor esfuerzo.", Evidencia = summary.Resumen, Responsable = "Equipo Cloud", Plazo = "Corto plazo" },
                new ReporteRecommendationAnalysisItem { Prioridad = security.TieneSnapshot ? "Media" : "Alta", Recomendacion = security.TieneSnapshot ? "Priorizar recomendaciones de Secure Score e incidentes activos." : "Recolectar snapshot mensual de seguridad Microsoft 365.", Evidencia = security.TieneSnapshot ? $"Alertas altas: {security.AlertasHigh}; incidentes activos: {security.IncidentesActivos}." : "Sin snapshot disponible.", Responsable = "Equipo Seguridad", Plazo = "Corto plazo" }
            };
        }

        return analysis;
    }

    private static string BuildPeriodLabel(ReportePeriodoPayload? period)
    {
        if (period is null)
            return "Periodo no disponible";

        return string.IsNullOrWhiteSpace(period.FechaInicio) || string.IsNullOrWhiteSpace(period.FechaFin)
            ? FirstNonEmpty(period.Valor, "Periodo no disponible")
            : $"{period.FechaInicio} a {period.FechaFin}";
    }

    private static string SummarizeJsonElement(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
            return item.GetString() ?? "";

        if (item.ValueKind != JsonValueKind.Object)
            return item.ToString();

        var title = FirstJsonString(item, "title", "displayName", "name", "incidentName", "alertName", "controlName");
        var category = FirstJsonString(item, "category", "severity", "status", "vendorInformation", "implementationStatus");
        var detail = FirstJsonString(item, "description", "summary", "recommendedAction", "remediation", "userPrincipalName");
        return string.Join(" · ", new[] { title, category, detail }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
    }

    private static string FirstJsonString(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!item.TryGetProperty(propertyName, out var property))
                continue;

            if (property.ValueKind == JsonValueKind.String)
                return property.GetString() ?? "";

            if (property.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                return property.ToString();
        }

        return "";
    }

    private static string ResolveStateBadge(string? state)
    {
        var value = (state ?? "").ToLowerInvariant();
        if (value.Contains("cerr", StringComparison.Ordinal) || value.Contains("resuelto", StringComparison.Ordinal))
            return "badge badge-resuelto";

        if (value.Contains("abierto", StringComparison.Ordinal) || value.Contains("pend", StringComparison.Ordinal) || value.Contains("proceso", StringComparison.Ordinal))
            return "badge badge-abierto";

        return "badge badge-neutral";
    }

    private static string BuildStyles(string accent)
    {
        return """
<style>
:root{--dark1:#1a1a2e;--dark2:#16213e;--dark3:#0f3460;--accent:__ACCENT__;--accent-light:#1b5faa;--accent-dark:#0b2a5c;--white:#fff;--gray-light:#f4f6f9;--gray:#e0e0e0;--gray-dark:#6c757d;--sidebar-w:270px;--transition:all .3s ease}*{box-sizing:border-box;margin:0;padding:0;font-weight:400!important}html{scroll-behavior:smooth}body{font-family:'Segoe UI',Arial,sans-serif;background:var(--gray-light);color:#333;line-height:1.7;overflow-x:hidden}.sidebar{position:fixed;inset:0 auto 0 0;width:var(--sidebar-w);background:linear-gradient(to top,#000 0%,#0F5094 100%);z-index:10;display:flex;flex-direction:column;box-shadow:4px 0 20px rgba(0,0,0,.3)}.sidebar-logo{padding:28px 20px 20px;text-align:center;border-bottom:1px solid rgba(255,255,255,.25)}.sidebar-logo-title{color:#fff;font-weight:900;font-size:15px;letter-spacing:2px}.sidebar-logo small,.sidebar-footer span{color:rgba(255,255,255,.8);font-size:10px;letter-spacing:1px}.sidebar nav{flex:1;padding:16px 0;overflow-y:auto}.sidebar nav a{display:flex;gap:12px;align-items:center;padding:11px 24px;color:#fff;text-decoration:none;font-size:13px;font-weight:700;border-left:3px solid transparent}.sidebar nav a:hover{background:rgba(255,255,255,.15);border-left-color:#fff}.sidebar-footer{padding:16px 20px;border-top:1px solid rgba(255,255,255,.25);text-align:center}.main{margin-left:var(--sidebar-w);min-height:100vh}.hero{background:linear-gradient(to right,#000 0%,#0F5094 100%);color:#fff;padding:88px 60px 72px;overflow:hidden}.hero-badge{display:inline-block;border:1px solid rgba(122,255,255,.55);color:#7AFFFF;padding:6px 18px;border-radius:50px;font-size:12px;font-weight:800;letter-spacing:2px;text-transform:uppercase;margin-bottom:24px}.hero-logo{max-width:220px;max-height:82px;object-fit:contain;margin-bottom:20px;background:rgba(255,255,255,.92);border-radius:14px;padding:12px}.hero-brand-text{font-weight:900;letter-spacing:2px;text-transform:uppercase;margin-bottom:18px;color:#fff}.hero h1{font-size:42px;font-weight:900;line-height:1.15;max-width:900px;margin-bottom:16px}.hero-sub{font-size:17px;color:rgba(255,255,255,.78);max-width:820px;margin-bottom:32px}.hero-meta{display:flex;flex-wrap:wrap;gap:18px}.hero-meta-item{display:flex;flex-direction:column;gap:3px;background:rgba(255,255,255,.08);padding:12px 18px;border-radius:12px;backdrop-filter:blur(10px);max-width:260px}.label{font-size:10px;color:rgba(255,255,255,.6);text-transform:uppercase;letter-spacing:1px}.value{font-size:14px;font-weight:800;overflow-wrap:anywhere}.section{padding:68px 60px;background:#fff}.section-alt{background:#efefef}.section-title{font-size:28px;font-weight:900;color:var(--dark1);display:flex;align-items:center;gap:14px;margin-bottom:8px}.section-title span{background:linear-gradient(to top,#000 0%,#0F5094 100%);color:#fff;min-width:48px;height:48px;border-radius:14px;display:inline-flex;align-items:center;justify-content:center;font-size:15px}.section-subtitle{color:var(--gray-dark);font-size:15px;margin-bottom:24px;padding-left:62px}.divider{height:4px;background:linear-gradient(90deg,var(--accent),transparent);border-radius:4px;margin:10px 0 30px;max-width:120px}.section-text{font-size:15px;color:#555;line-height:1.85;max-width:980px;margin-bottom:18px}.stats-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:18px;margin:28px 0}.stat-card{background:#fff;border-radius:16px;padding:24px 20px;text-align:center;border:1px solid #eee;box-shadow:0 4px 20px rgba(0,0,0,.05);break-inside:avoid}.section-alt .stat-card{background:#fff}.stat-number{font-size:34px;font-weight:900;color:var(--dark1);line-height:1;overflow-wrap:anywhere}.stat-number.small{font-size:22px}.stat-label{font-size:13px;color:var(--gray-dark);margin-top:8px;font-weight:800}.stat-sub{font-size:11px;color:#8a96a6;margin-top:4px}.table-wrapper{overflow-x:auto;border-radius:14px;box-shadow:0 4px 20px rgba(0,0,0,.06);margin:20px 0;background:#fff}table{width:100%;border-collapse:collapse;font-size:13px;background:#fff}thead{background:linear-gradient(135deg,var(--dark1),var(--dark3));color:#fff}th{padding:14px 16px;text-align:left;font-size:11px;letter-spacing:.5px;text-transform:uppercase;white-space:nowrap}td{padding:12px 16px;vertical-align:top;border-bottom:1px solid #f0f0f0}td small{display:block;color:#697789;margin-top:4px;max-width:520px}.empty-cell{text-align:center;color:#697789;padding:28px}.badge{display:inline-block;padding:4px 11px;border-radius:50px;font-size:11px;font-weight:800;white-space:nowrap}.badge-resuelto{background:rgba(16,57,117,.12);color:#0b2a5c}.badge-abierto{background:rgba(255,152,0,.14);color:#9a4d00}.badge-consultoria{background:rgba(156,39,176,.1);color:#7B1FA2}.badge-security{background:#E3F2FD;color:#1565C0}.badge-neutral{background:#eef1f5;color:#536173}.partners-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:16px}.partner-card{background:#fff;border:1px solid var(--gray);border-radius:14px;padding:22px;box-shadow:0 3px 15px rgba(0,0,0,.04)}.partner-card strong{display:block;color:var(--dark2);margin-bottom:8px}.partner-card p{color:#5f6d7e;font-size:13px}.impl-list{list-style:none;margin:20px 0}.impl-list li{display:flex;gap:14px;align-items:flex-start;background:#fff;border-left:4px solid var(--accent);border-radius:12px;padding:16px 20px;margin-bottom:12px;box-shadow:0 2px 12px rgba(0,0,0,.04);break-inside:avoid}.impl-list span{color:var(--accent);font-weight:900}.gauge-container{display:flex;flex-wrap:wrap;align-items:center;justify-content:center;gap:42px;margin:36px 0}.gauge{position:relative;width:220px;height:220px}.gauge svg{width:220px;height:220px;transform:rotate(-90deg)}.gauge-bg{fill:none;stroke:#e8e8e8;stroke-width:14}.gauge-fill{fill:none;stroke:var(--accent);stroke-width:14;stroke-linecap:round;stroke-dasharray:565.48;transition:stroke-dashoffset 1s ease}.gauge-text{position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center}.gauge-text strong{font-size:34px;color:var(--dark1)}.gauge-text span{font-size:11px;text-transform:uppercase;color:var(--gray-dark);font-weight:800}.gauge-info{max-width:450px}.gauge-info h3,.three-columns h3{font-size:19px;color:var(--dark1);margin-bottom:10px}.comparison-item{display:flex;align-items:center;gap:12px;margin-top:12px;font-size:13px;font-weight:700}.bar-bg{flex:1;height:10px;border-radius:10px;background:#e8e8e8;overflow:hidden}.bar-fill{display:block;height:100%;background:linear-gradient(90deg,var(--accent),var(--accent-light));border-radius:10px}.bar-gray{background:linear-gradient(90deg,#aaa,#ccc)}.three-columns{display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:18px;margin-top:24px}.compact-list{list-style:none}.compact-list li{background:#fff;border:1px solid #edf0f4;border-radius:10px;padding:10px 12px;margin-bottom:8px;color:#536173;font-size:13px}.notice{background:#fff6e5;border-left:4px solid #f5a623;padding:14px 16px;border-radius:10px;margin-bottom:20px}.muted{color:#748094;font-size:13px}.finding-list{display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:18px}.finding-card{background:#fff;border:1px solid #e9edf3;border-radius:14px;padding:20px;box-shadow:0 3px 14px rgba(0,0,0,.04);break-inside:avoid}.finding-card h3{margin:12px 0;color:var(--dark1)}.finding-card p{font-size:13.5px;color:#586879;margin-top:8px}.contact-card{background:linear-gradient(135deg,var(--dark1),var(--dark3));border-radius:20px;padding:42px 36px;max-width:520px;color:#fff;text-align:center;margin:0 auto;box-shadow:0 10px 40px rgba(0,0,0,.15)}.contact-card h3{font-size:22px;margin-bottom:6px}.role{font-size:12px;font-weight:900;letter-spacing:2px;text-transform:uppercase;margin-bottom:22px}.contact-item{color:rgba(255,255,255,.84);margin:8px 0}.footer{background:linear-gradient(to left,#000 0%,#0F5094 100%);color:rgba(255,255,255,.65);text-align:center;padding:28px 40px;font-size:12px}.footer strong{color:#fff}.watermark{position:fixed;bottom:10px;left:calc(var(--sidebar-w) + 20px);font-size:10px;font-weight:900;color:rgba(0,0,0,.04);letter-spacing:4px;pointer-events:none}@media(max-width:900px){.sidebar{display:none}.main{margin-left:0}.hero{padding:72px 24px 56px}.hero h1{font-size:30px}.section{padding:42px 20px}.section-subtitle{padding-left:0}.watermark{left:20px}.support-table table{min-width:900px}}@media print{*{print-color-adjust:exact;-webkit-print-color-adjust:exact}.sidebar,.watermark{display:none}.main{margin-left:0}.hero,.footer{background:#0F5094!important}.section{padding:34px 24px;break-inside:auto}.stat-card,.finding-card,.impl-list li,.table-wrapper{break-inside:avoid;box-shadow:none}a{text-decoration:none;color:inherit}}
</style>
""".Replace("__ACCENT__", accent, StringComparison.Ordinal);
    }

    private static string NormalizeCssColor(string? raw, string fallback)
    {
        var value = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return value.StartsWith('#') && (value.Length == 4 || value.Length == 7) ? value : fallback;
    }

    private static string FormatInteger(int value) => value.ToString("N0", CultureInfo.GetCultureInfo("es-CO"));

    private static string FormatDecimal(decimal value) => value.ToString("N2", CultureInfo.GetCultureInfo("es-CO"));

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");

    private static string A(string? value) => H(value).Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}
