namespace CotizadorInterno.Web.Models.Reportes;

public sealed class ReporteAnalysisPayload
{
    public string HeroSubtitle { get; set; } = "";
    public IReadOnlyList<string> ResumenEjecutivo { get; set; } = Array.Empty<string>();
    public string AlcanceMarco { get; set; } = "";
    public IReadOnlyList<ReporteIsoAnalysisItem> Iso { get; set; } = Array.Empty<ReporteIsoAnalysisItem>();
    public IReadOnlyList<string> Implementacion { get; set; } = Array.Empty<string>();
    public string SeguridadNarrativa { get; set; } = "";
    public IReadOnlyList<ReporteFindingAnalysisItem> Hallazgos { get; set; } = Array.Empty<ReporteFindingAnalysisItem>();
    public IReadOnlyList<string> Conclusiones { get; set; } = Array.Empty<string>();
    public IReadOnlyList<ReporteRecommendationAnalysisItem> Recomendaciones { get; set; } = Array.Empty<ReporteRecommendationAnalysisItem>();
}

public sealed class ReporteIsoAnalysisItem
{
    public string Dominio { get; set; } = "";
    public string Evidencia { get; set; } = "";
    public string Riesgo { get; set; } = "";
    public string Madurez { get; set; } = "";
    public string Recomendacion { get; set; } = "";
}

public sealed class ReporteFindingAnalysisItem
{
    public string Titulo { get; set; } = "";
    public string Evidencia { get; set; } = "";
    public string Impacto { get; set; } = "";
    public string Accion { get; set; } = "";
    public string Severidad { get; set; } = "";
}

public sealed class ReporteRecommendationAnalysisItem
{
    public string Prioridad { get; set; } = "";
    public string Recomendacion { get; set; } = "";
    public string Evidencia { get; set; } = "";
    public string Responsable { get; set; } = "";
    public string Plazo { get; set; } = "";
}
