using System.Text.Json;

namespace CotizadorInterno.Web.Models.M365;

public sealed class M365SecuritySnapshotRequest
{
    public string ClienteId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string Periodo { get; set; } = "";
}

public sealed class M365SecuritySnapshotResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
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
    public string FechaConsulta { get; set; } = "";
    public string EstadoConsulta { get; set; } = "";
    public string ErrorConsulta { get; set; } = "";
}

public sealed class M365SecuritySnapshotRecord
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

public sealed class M365SecurityGraphData
{
    public M365SecureScoreSnapshot SecureScore { get; set; } = new();
    public IReadOnlyList<M365SecurityRecommendation> TopRecommendations { get; set; } = Array.Empty<M365SecurityRecommendation>();
    public IReadOnlyList<M365SecurityAlertSummary> Alerts { get; set; } = Array.Empty<M365SecurityAlertSummary>();
    public IReadOnlyList<M365SecurityIncidentSummary> Incidents { get; set; } = Array.Empty<M365SecurityIncidentSummary>();
    public IReadOnlyList<JsonElement> RawAlerts { get; set; } = Array.Empty<JsonElement>();
    public IReadOnlyList<JsonElement> RawIncidents { get; set; } = Array.Empty<JsonElement>();
}

public sealed class M365SecureScoreSnapshot
{
    public string Id { get; set; } = "";
    public string CreatedDateTime { get; set; } = "";
    public decimal CurrentScore { get; set; }
    public decimal MaxScore { get; set; }
    public IReadOnlyList<M365SecureScoreControlScore> ControlScores { get; set; } = Array.Empty<M365SecureScoreControlScore>();
}

public sealed class M365SecureScoreControlScore
{
    public string ControlName { get; set; } = "";
    public decimal Score { get; set; }
    public decimal MaxScore { get; set; }
}

public sealed class M365SecurityRecommendation
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string ActionType { get; set; } = "";
    public string Remediation { get; set; } = "";
    public string UserImpact { get; set; } = "";
    public string ImplementationCost { get; set; } = "";
    public decimal CurrentScore { get; set; }
    public decimal MaxScore { get; set; }
    public decimal ScoreGap { get; set; }
    public int Rank { get; set; }
}

public sealed class M365SecurityAlertSummary
{
    public string Id { get; set; } = "";
    public string CreatedDateTime { get; set; } = "";
    public string Title { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Status { get; set; } = "";
    public string ServiceSource { get; set; } = "";
}

public sealed class M365SecurityIncidentSummary
{
    public string Id { get; set; } = "";
    public string CreatedDateTime { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Status { get; set; } = "";
}
