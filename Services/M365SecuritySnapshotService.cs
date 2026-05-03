using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CotizadorInterno.Web.Models.M365;

namespace CotizadorInterno.Web.Services;

public sealed class M365SecuritySnapshotService : IM365SecuritySnapshotService
{
    private static readonly TimeSpan BogotaOffset = TimeSpan.FromHours(-5);
    private readonly IM365SecurityGraphClient _graphClient;
    private readonly IM365SecuritySnapshotRepository _repository;
    private readonly ILogger<M365SecuritySnapshotService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public M365SecuritySnapshotService(
        IM365SecurityGraphClient graphClient,
        IM365SecuritySnapshotRepository repository,
        ILogger<M365SecuritySnapshotService> logger)
    {
        _graphClient = graphClient;
        _repository = repository;
        _logger = logger;
    }

    public async Task<M365SecuritySnapshotResult> CollectMonthlySnapshotAsync(
        M365SecuritySnapshotRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var clienteId = NormalizeGuid(request.ClienteId, nameof(request.ClienteId));
        var period = ResolvePeriod(request.Periodo);
        var connection = await _repository.FindConnectionForSnapshotAsync(clienteId, request.TenantId, ct)
            ?? throw new InvalidOperationException("No hay una conexion Microsoft 365 guardada para el cliente o tenant indicado.");

        if (string.IsNullOrWhiteSpace(connection.TenantId))
            throw new InvalidOperationException("La conexion guardada no tiene tenantId.");

        if (!connection.AdminConsent)
            throw new InvalidOperationException("La conexion Microsoft 365 no tiene consentimiento de administrador confirmado.");

        var fechaConsulta = DateTimeOffset.UtcNow;
        M365SecurityGraphData graphData;
        try
        {
            graphData = await _graphClient.CollectSecurityDataAsync(
                connection.TenantId,
                period.StartUtc,
                period.EndExclusiveUtc,
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var error = BuildExceptionDetail(ex);
            _logger.LogWarning(
                ex,
                "Consulta mensual M365 fallida para cliente {ClienteId}, tenant {TenantId}, periodo {Periodo}.",
                clienteId,
                connection.TenantId,
                period.Value);

            var failedSnapshot = await _repository.UpsertSnapshotAsync(new M365SecuritySnapshotRecord
            {
                ClienteId = clienteId,
                TenantId = connection.TenantId,
                Periodo = period.Value,
                FechaConsulta = fechaConsulta.ToString("O", CultureInfo.InvariantCulture),
                EstadoConsulta = "Error",
                ErrorConsulta = Truncate(error, 3900)
            }, ct);

            return ToResult(
                failedSnapshot,
                success: false,
                message: "La consulta a Microsoft Graph fallo. Se guardo el error en el snapshot mensual.");
        }

        var snapshot = BuildCompletedSnapshot(clienteId, connection.TenantId, period.Value, fechaConsulta, graphData);
        var saved = await _repository.UpsertSnapshotAsync(snapshot, ct);

        _logger.LogInformation(
            "Snapshot mensual M365 guardado para cliente {ClienteId}, tenant {TenantId}, periodo {Periodo}.",
            clienteId,
            connection.TenantId,
            period.Value);

        return ToResult(saved, success: true, message: "Snapshot mensual Microsoft 365 guardado correctamente.");
    }

    private static M365SecuritySnapshotRecord BuildCompletedSnapshot(
        string clienteId,
        string tenantId,
        string periodo,
        DateTimeOffset fechaConsulta,
        M365SecurityGraphData graphData)
    {
        return new M365SecuritySnapshotRecord
        {
            ClienteId = clienteId,
            TenantId = tenantId,
            Periodo = periodo,
            SecureScoreActual = graphData.SecureScore.CurrentScore,
            SecureScoreMaximo = graphData.SecureScore.MaxScore,
            AlertasHigh = CountSeverity(graphData.Alerts, "high"),
            AlertasMedium = CountSeverity(graphData.Alerts, "medium"),
            AlertasLow = CountSeverity(graphData.Alerts, "low"),
            IncidentesActivos = CountStatus(graphData.Incidents, "active"),
            IncidentesResueltos = CountStatus(graphData.Incidents, "resolved"),
            RecomendacionesTopJson = JsonSerializer.Serialize(graphData.TopRecommendations, JsonOptions),
            AlertasJson = JsonSerializer.Serialize(graphData.RawAlerts, JsonOptions),
            IncidentesJson = JsonSerializer.Serialize(graphData.RawIncidents, JsonOptions),
            FechaConsulta = fechaConsulta.ToString("O", CultureInfo.InvariantCulture),
            EstadoConsulta = "Completado",
            ErrorConsulta = ""
        };
    }

    private static M365SecuritySnapshotResult ToResult(
        M365SecuritySnapshotRecord snapshot,
        bool success,
        string message)
    {
        return new M365SecuritySnapshotResult
        {
            Success = success,
            Message = message,
            RecordId = snapshot.RecordId,
            ClienteId = snapshot.ClienteId,
            TenantId = snapshot.TenantId,
            Periodo = snapshot.Periodo,
            SecureScoreActual = snapshot.SecureScoreActual,
            SecureScoreMaximo = snapshot.SecureScoreMaximo,
            AlertasHigh = snapshot.AlertasHigh,
            AlertasMedium = snapshot.AlertasMedium,
            AlertasLow = snapshot.AlertasLow,
            IncidentesActivos = snapshot.IncidentesActivos,
            IncidentesResueltos = snapshot.IncidentesResueltos,
            FechaConsulta = snapshot.FechaConsulta,
            EstadoConsulta = snapshot.EstadoConsulta,
            ErrorConsulta = snapshot.ErrorConsulta
        };
    }

    private static int CountSeverity(IReadOnlyList<M365SecurityAlertSummary> alerts, string severity) =>
        alerts.Count(alert => string.Equals(
            NormalizeGraphValue(alert.Severity),
            severity,
            StringComparison.OrdinalIgnoreCase));

    private static int CountStatus(IReadOnlyList<M365SecurityIncidentSummary> incidents, string status) =>
        incidents.Count(incident => string.Equals(
            NormalizeGraphValue(incident.Status),
            status,
            StringComparison.OrdinalIgnoreCase));

    private static M365SnapshotPeriod ResolvePeriod(string? rawPeriod)
    {
        var value = rawPeriod?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(value))
        {
            var nowBogota = DateTimeOffset.UtcNow.ToOffset(BogotaOffset);
            var previousMonth = new DateTimeOffset(nowBogota.Year, nowBogota.Month, 1, 0, 0, 0, BogotaOffset)
                .AddMonths(-1);
            return BuildPeriod(previousMonth.Year, previousMonth.Month);
        }

        if (!DateTime.TryParseExact(
                value,
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            throw new InvalidOperationException("El periodo debe tener formato yyyy-MM.");
        }

        return BuildPeriod(parsed.Year, parsed.Month);
    }

    private static M365SnapshotPeriod BuildPeriod(int year, int month)
    {
        var startLocal = new DateTimeOffset(year, month, 1, 0, 0, 0, BogotaOffset);
        var endLocal = startLocal.AddMonths(1);
        return new M365SnapshotPeriod(
            startLocal.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            startLocal.ToUniversalTime(),
            endLocal.ToUniversalTime());
    }

    private static string NormalizeGraphValue(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();

    private static string NormalizeGuid(string? raw, string paramName)
    {
        if (!Guid.TryParse(raw, out var parsed))
            throw new InvalidOperationException($"El valor de {paramName} no es valido.");

        return parsed.ToString("D");
    }

    private static string BuildExceptionDetail(Exception ex)
    {
        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (string.IsNullOrWhiteSpace(current.Message))
                continue;

            var trimmed = current.Message.Trim();
            if (!messages.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                messages.Add(trimmed);
        }

        return string.Join(" | ", messages);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private sealed record M365SnapshotPeriod(
        string Value,
        DateTimeOffset StartUtc,
        DateTimeOffset EndExclusiveUtc);
}
