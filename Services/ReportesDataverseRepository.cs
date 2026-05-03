using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CotizadorInterno.Web.Models.Reportes;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace CotizadorInterno.Web.Services;

public sealed class ReportesDataverseRepository : IReportesDataverseRepository
{
    private const string FormattedValueAnnotationSuffix = "@OData.Community.Display.V1.FormattedValue";
    private readonly ReportesOptions _options;
    private readonly M365Options _m365Options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ReportesDataverseRepository> _logger;
    private readonly string _dataverseBaseUrl;
    private readonly string _azureAuthorityInstance;
    private readonly string _dataverseTenantId;
    private readonly string _dataverseClientId;
    private readonly string _dataverseClientSecret;
    private readonly string _dataverseCredentialSource;
    private readonly ConcurrentDictionary<string, bool> _attributeExistsCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly CultureInfo ReportCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public ReportesDataverseRepository(
        IOptions<ReportesOptions> options,
        IOptions<M365Options> m365Options,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ReportesDataverseRepository> logger)
    {
        _options = options.Value;
        _m365Options = m365Options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _dataverseBaseUrl = (configuration["Dataverse:BaseUrl"] ?? "").TrimEnd('/');
        _azureAuthorityInstance = configuration["AzureAd:Instance"] ?? "https://login.microsoftonline.com/";
        _dataverseTenantId = FirstNonEmpty(configuration["Dataverse:TenantId"], configuration["AzureAd:TenantId"]);

        var credential = ResolveDataverseAppCredential(configuration);
        _dataverseClientId = credential.ClientId;
        _dataverseClientSecret = credential.ClientSecret;
        _dataverseCredentialSource = credential.Source;
    }

    public async Task<ReporteMonthlyInput> LoadMonthlyInputAsync(
        string clienteId,
        string periodo,
        DateOnly startDate,
        DateOnly endExclusiveDate,
        CancellationToken ct = default)
    {
        var normalizedClienteId = NormalizeGuid(clienteId, nameof(clienteId));
        var normalizedPeriodo = NormalizeRequiredText(periodo, nameof(periodo));

        var clientTask = LoadClientAsync(normalizedClienteId, ct);
        var ticketsTask = LoadTicketsAsync(normalizedClienteId, startDate, endExclusiveDate, ct);
        var securityTask = FindSecuritySnapshotAsync(normalizedClienteId, normalizedPeriodo, ct);

        await Task.WhenAll(clientTask, ticketsTask, securityTask);

        return new ReporteMonthlyInput
        {
            Cliente = clientTask.Result,
            Periodo = normalizedPeriodo,
            FechaInicio = startDate,
            FechaFinExclusiva = endExclusiveDate,
            Tickets = ticketsTask.Result,
            SecuritySnapshot = securityTask.Result
        };
    }

    public async Task<ReporteHtmlGeneradoRecord> UpsertGeneratedReportAsync(
        ReporteHtmlGeneradoRecord report,
        CancellationToken ct = default)
    {
        if (report is null)
            throw new ArgumentNullException(nameof(report));

        var clienteId = NormalizeGuid(report.ClienteId, nameof(report.ClienteId));
        var periodo = NormalizeRequiredText(report.Periodo, nameof(report.Periodo));
        var existing = await FindGeneratedReportAsync(clienteId, periodo, ct);
        var table = _options.GeneratedReport;
        var payload = BuildGeneratedReportPayload(report, clienteId, periodo);

        var navigationProperty = await ResolveLookupNavigationPropertyAsync(
            table.TableLogicalName,
            table.ClientLookupField,
            table.ClientNavigationProperty,
            ct);
        if (!string.IsNullOrWhiteSpace(navigationProperty))
            payload[$"{navigationProperty}@odata.bind"] = $"/{_options.Client.TableSetName}({clienteId})";

        var relativeUrl = string.IsNullOrWhiteSpace(existing?.RecordId)
            ? $"/api/data/v9.2/{table.TableSetName}"
            : $"/api/data/v9.2/{table.TableSetName}({existing.RecordId})";
        var method = string.IsNullOrWhiteSpace(existing?.RecordId) ? "POST" : "PATCH";
        var body = await CallDataverseAppSendAsync(relativeUrl, method, payload, ct, AddReturnRepresentationHeaders);
        if (!string.IsNullOrWhiteSpace(body))
        {
            using var doc = JsonDocument.Parse(body);
            var saved = BuildGeneratedReportRecord(doc.RootElement);
            if (!string.IsNullOrWhiteSpace(saved.RecordId))
                return saved;
        }

        return await FindGeneratedReportAsync(clienteId, periodo, ct)
            ?? new ReporteHtmlGeneradoRecord
            {
                RecordId = existing?.RecordId ?? "",
                ClienteId = clienteId,
                ClienteNombre = existing?.ClienteNombre ?? "",
                Periodo = periodo,
                HtmlGenerado = report.HtmlGenerado,
                Estado = report.Estado,
                FechaGeneracion = report.FechaGeneracion,
                PromptVersion = report.PromptVersion,
                Errores = report.Errores
            };
    }

    public async Task<IReadOnlyList<ReporteHtmlGeneradoRecord>> ListGeneratedReportsAsync(
        string periodo,
        CancellationToken ct = default)
    {
        var normalizedPeriodo = NormalizeRequiredText(periodo, nameof(periodo));
        var table = _options.GeneratedReport;
        var filter = $"{table.PeriodoField} eq '{EscapeOdataLiteral(normalizedPeriodo)}'";
        var relativeUrl =
            $"/api/data/v9.2/{table.TableSetName}" +
            $"?$select={BuildGeneratedReportListSelectClause()}" +
            $"&$filter={Uri.EscapeDataString(filter)}" +
            "&$orderby=modifiedon desc&$top=5000";

        var items = await GetDataverseAppEntitiesAsync(relativeUrl, ct, AddFormattedValueHeaders);
        return items
            .Select(BuildGeneratedReportRecord)
            .Where(item => !string.IsNullOrWhiteSpace(item.RecordId))
            .OrderBy(item => FirstNonEmpty(item.ClienteNombre, item.ClienteId), StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(item => item.FechaGeneracion, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ReporteHtmlGeneradoRecord?> GetGeneratedReportAsync(
        string reportId,
        CancellationToken ct = default)
    {
        var normalizedReportId = NormalizeGuid(reportId, nameof(reportId));
        var table = _options.GeneratedReport;
        var relativeUrl =
            $"/api/data/v9.2/{table.TableSetName}({normalizedReportId})" +
            $"?$select={BuildGeneratedReportSelectClause()}";

        try
        {
            var json = await CallDataverseAppGetJsonAsync(relativeUrl, ct, AddFormattedValueHeaders);
            using var doc = JsonDocument.Parse(json);
            var record = BuildGeneratedReportRecord(doc.RootElement);
            return string.IsNullOrWhiteSpace(record.RecordId) ? null : record;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("404", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
    }

    private async Task<ReporteClienteData> LoadClientAsync(string clienteId, CancellationToken ct)
    {
        var table = _options.Client;
        var selectFields = new List<string> { table.IdField, table.NameField };
        selectFields.AddRange(await ResolveExistingAttributesAsync(table.TableLogicalName, table.LogoFieldCandidates, ct));
        selectFields.AddRange(await ResolveExistingAttributesAsync(table.TableLogicalName, table.ColorFieldCandidates, ct));

        var relativeUrl =
            $"/api/data/v9.2/{table.TableSetName}({clienteId})" +
            $"?$select={string.Join(",", selectFields.Distinct(StringComparer.OrdinalIgnoreCase))}";
        var json = await CallDataverseAppGetJsonAsync(relativeUrl, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var logo = ReadFirstString(root, table.LogoFieldCandidates);
        var color = NormalizeCssColor(ReadFirstString(root, table.ColorFieldCandidates), _options.DefaultCorporateColor);

        return new ReporteClienteData
        {
            ClienteId = FirstNonEmpty(ReadString(root, table.IdField), clienteId),
            Nombre = FirstNonEmpty(ReadString(root, table.NameField), "Cliente"),
            Logo = logo,
            ColorCorporativo = color
        };
    }

    private async Task<IReadOnlyList<ReporteTicketData>> LoadTicketsAsync(
        string clienteId,
        DateOnly startDate,
        DateOnly endExclusiveDate,
        CancellationToken ct)
    {
        var lookupFields = _options.Ticket.ClientLookupValueFilterFields
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (lookupFields.Length == 0)
            lookupFields = new[] { BuildLookupValuePropertyName(_options.Ticket.ClientLookupField) };

        Exception? lastError = null;
        foreach (var lookupField in lookupFields)
        {
            try
            {
                var filter = $"{lookupField} eq {clienteId} and {BuildTicketDateFilter(_options.Ticket.CreationDateField, startDate, endExclusiveDate)}";
                return await LoadTicketsByFilterAsync(filter, startDate, endExclusiveDate, applyLocalDateFilter: false, ct);
            }
            catch (InvalidOperationException ex)
            {
                lastError = ex;
                _logger.LogDebug(
                    ex,
                    "No fue posible consultar tickets con lookup {LookupField} y filtro mensual. Se probara fallback.",
                    lookupField);
            }
        }

        foreach (var lookupField in lookupFields)
        {
            try
            {
                var filter = $"{lookupField} eq {clienteId}";
                return await LoadTicketsByFilterAsync(filter, startDate, endExclusiveDate, applyLocalDateFilter: true, ct);
            }
            catch (InvalidOperationException ex)
            {
                lastError = ex;
                _logger.LogDebug(
                    ex,
                    "No fue posible consultar tickets con lookup {LookupField}.",
                    lookupField);
            }
        }

        throw lastError ?? new InvalidOperationException("No fue posible consultar tickets del periodo.");
    }

    private async Task<IReadOnlyList<ReporteTicketData>> LoadTicketsByFilterAsync(
        string filter,
        DateOnly startDate,
        DateOnly endExclusiveDate,
        bool applyLocalDateFilter,
        CancellationToken ct)
    {
        var table = _options.Ticket;
        var relativeUrl =
            $"/api/data/v9.2/{table.TableSetName}" +
            $"?$select={BuildTicketSelectClause()}" +
            $"&$filter={Uri.EscapeDataString(filter)}" +
            $"&$orderby={table.CreationDateField} desc,{table.ModifiedOnField} desc&$top=5000";
        var items = await GetDataverseAppEntitiesAsync(relativeUrl, ct, AddFormattedValueHeaders);
        var tickets = items
            .Select(BuildTicketData)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();

        if (applyLocalDateFilter)
        {
            tickets = tickets
                .Where(ticket => IsTicketInPeriod(ticket, startDate, endExclusiveDate))
                .ToList();
        }

        return tickets
            .OrderByDescending(item => item.CreationDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<ReporteSecuritySnapshotData?> FindSecuritySnapshotAsync(
        string clienteId,
        string periodo,
        CancellationToken ct)
    {
        var table = _m365Options.Dataverse.SecuritySnapshot;
        var filter =
            $"{table.InternalClientIdField} eq '{EscapeOdataLiteral(clienteId)}' and " +
            $"{table.PeriodoField} eq '{EscapeOdataLiteral(periodo)}'";
        var relativeUrl =
            $"/api/data/v9.2/{table.TableSetName}" +
            $"?$select={BuildSecuritySnapshotSelectClause()}" +
            $"&$filter={Uri.EscapeDataString(filter)}" +
            "&$orderby=modifiedon desc&$top=1";
        var items = await GetDataverseAppEntitiesAsync(relativeUrl, ct, AddFormattedValueHeaders);
        return items.Select(BuildSecuritySnapshotData).FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.RecordId));
    }

    private async Task<ReporteHtmlGeneradoRecord?> FindGeneratedReportAsync(
        string clienteId,
        string periodo,
        CancellationToken ct)
    {
        var table = _options.GeneratedReport;
        var filter =
            $"{table.InternalClientIdField} eq '{EscapeOdataLiteral(clienteId)}' and " +
            $"{table.PeriodoField} eq '{EscapeOdataLiteral(periodo)}'";
        var relativeUrl =
            $"/api/data/v9.2/{table.TableSetName}" +
            $"?$select={BuildGeneratedReportSelectClause()}" +
            $"&$filter={Uri.EscapeDataString(filter)}" +
            "&$orderby=modifiedon desc&$top=1";
        var items = await GetDataverseAppEntitiesAsync(relativeUrl, ct, AddFormattedValueHeaders);
        return items.Select(BuildGeneratedReportRecord).FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.RecordId));
    }

    private Dictionary<string, object?> BuildGeneratedReportPayload(
        ReporteHtmlGeneradoRecord report,
        string clienteId,
        string periodo)
    {
        var table = _options.GeneratedReport;
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [table.PrimaryNameField] = $"Informe mensual M365 - {clienteId} - {periodo}",
            [table.InternalClientIdField] = clienteId,
            [table.PeriodoField] = periodo,
            [table.HtmlGeneradoField] = report.HtmlGenerado ?? "",
            [table.EstadoField] = report.Estado ?? "",
            [table.FechaGeneracionField] = report.FechaGeneracion ?? "",
            [table.PromptVersionField] = report.PromptVersion ?? "",
            [table.ErroresField] = report.Errores ?? ""
        };
    }

    private ReporteTicketData? BuildTicketData(JsonElement item)
    {
        var table = _options.Ticket;
        var recordId = FirstNonEmpty(ReadString(item, table.IdField), ReadString(item, $"{table.TableLogicalName}id"));
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var creationDate = ReadDateOnly(item, table.CreationDateField)
            ?? ReadDateOnly(item, table.CreatedOnFallbackField);
        var modifiedDate = ReadDateOnly(item, table.ModifiedOnField);
        var clientLookup = BuildLookupValuePropertyName(table.ClientLookupField);
        var createdByLookup = BuildLookupValuePropertyName(table.CreatedByField);

        return new ReporteTicketData
        {
            RecordId = recordId.Trim(),
            Title = FirstNonEmpty(
                ReadString(item, table.TitleField).Trim(),
                ReadString(item, table.PrimaryNameField).Trim(),
                "Ticket sin titulo"),
            Description = ReadString(item, table.DescriptionField).Trim(),
            CreationDateValue = creationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            CreationDateDisplay = creationDate?.ToString("dd/MM/yyyy", ReportCulture) ?? "",
            StateLabel = FirstNonEmpty(ReadFormattedValue(item, table.StateField), ReadString(item, table.StateField), "Sin estado"),
            TypeLabel = FirstNonEmpty(ReadFormattedValue(item, table.TypeField), ReadString(item, table.TypeField), "Sin tipo"),
            ClientId = ReadString(item, clientLookup).Trim(),
            ClientName = FirstNonEmpty(ReadLookupFormattedValue(item, clientLookup), ReadFormattedValue(item, table.ClientLookupField), "Sin cliente"),
            CategoryLabel = FirstNonEmpty(ReadFormattedValue(item, table.CategoryField), ReadString(item, table.CategoryField), "Sin categoria"),
            CreatorName = FirstNonEmpty(ReadLookupFormattedValue(item, createdByLookup), ReadFormattedValue(item, table.CreatedByField), "Sin creador"),
            HoursTaken = RoundDecimal(ReadDecimal(item, table.HoursTakenField) ?? 0m),
            MethodLabel = FirstNonEmpty(ReadFormattedValue(item, table.MethodField), ReadString(item, table.MethodField), "Sin metodo"),
            Solution = ReadString(item, table.SolutionField).Trim(),
            ModifiedOnDisplay = modifiedDate?.ToString("dd/MM/yyyy", ReportCulture) ?? ""
        };
    }

    private ReporteSecuritySnapshotData BuildSecuritySnapshotData(JsonElement item)
    {
        var table = _m365Options.Dataverse.SecuritySnapshot;
        var clientLookup = BuildLookupValuePropertyName(table.ClientLookupField);
        return new ReporteSecuritySnapshotData
        {
            RecordId = ReadString(item, table.IdField).Trim(),
            ClienteId = FirstNonEmpty(ReadString(item, table.InternalClientIdField), ReadString(item, clientLookup)),
            TenantId = ReadString(item, table.TenantIdField).Trim(),
            Periodo = ReadString(item, table.PeriodoField).Trim(),
            SecureScoreActual = ReadDecimal(item, table.SecureScoreActualField) ?? 0m,
            SecureScoreMaximo = ReadDecimal(item, table.SecureScoreMaximoField) ?? 0m,
            AlertasHigh = ReadInt(item, table.AlertasHighField) ?? 0,
            AlertasMedium = ReadInt(item, table.AlertasMediumField) ?? 0,
            AlertasLow = ReadInt(item, table.AlertasLowField) ?? 0,
            IncidentesActivos = ReadInt(item, table.IncidentesActivosField) ?? 0,
            IncidentesResueltos = ReadInt(item, table.IncidentesResueltosField) ?? 0,
            RecomendacionesTopJson = ReadString(item, table.RecomendacionesTopJsonField).Trim(),
            AlertasJson = ReadString(item, table.AlertasJsonField).Trim(),
            IncidentesJson = ReadString(item, table.IncidentesJsonField).Trim(),
            FechaConsulta = ReadString(item, table.FechaConsultaField).Trim(),
            EstadoConsulta = ReadString(item, table.EstadoConsultaField).Trim(),
            ErrorConsulta = ReadString(item, table.ErrorConsultaField).Trim()
        };
    }

    private ReporteHtmlGeneradoRecord BuildGeneratedReportRecord(JsonElement item)
    {
        var table = _options.GeneratedReport;
        var clientLookup = BuildLookupValuePropertyName(table.ClientLookupField);
        return new ReporteHtmlGeneradoRecord
        {
            RecordId = ReadString(item, table.IdField).Trim(),
            ClienteId = FirstNonEmpty(ReadString(item, table.InternalClientIdField), ReadString(item, clientLookup)),
            ClienteNombre = FirstNonEmpty(ReadLookupFormattedValue(item, clientLookup), ReadFormattedValue(item, table.ClientLookupField)),
            Periodo = ReadString(item, table.PeriodoField).Trim(),
            HtmlGenerado = ReadString(item, table.HtmlGeneradoField),
            Estado = ReadString(item, table.EstadoField).Trim(),
            FechaGeneracion = ReadString(item, table.FechaGeneracionField).Trim(),
            PromptVersion = ReadString(item, table.PromptVersionField).Trim(),
            Errores = ReadString(item, table.ErroresField).Trim()
        };
    }

    private string BuildTicketSelectClause()
    {
        var table = _options.Ticket;
        return string.Join(",",
            new[]
            {
                table.IdField,
                table.PrimaryNameField,
                table.TitleField,
                table.DescriptionField,
                table.CreationDateField,
                table.StateField,
                table.TypeField,
                BuildLookupValuePropertyName(table.ClientLookupField),
                table.CategoryField,
                BuildLookupValuePropertyName(table.CreatedByField),
                table.HoursTakenField,
                table.MethodField,
                table.SolutionField,
                table.ModifiedOnField,
                table.CreatedOnFallbackField
            }
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private string BuildSecuritySnapshotSelectClause()
    {
        var table = _m365Options.Dataverse.SecuritySnapshot;
        return string.Join(",",
            new[]
            {
                table.IdField,
                table.PrimaryNameField,
                table.InternalClientIdField,
                BuildLookupValuePropertyName(table.ClientLookupField),
                table.TenantIdField,
                table.PeriodoField,
                table.SecureScoreActualField,
                table.SecureScoreMaximoField,
                table.AlertasHighField,
                table.AlertasMediumField,
                table.AlertasLowField,
                table.IncidentesActivosField,
                table.IncidentesResueltosField,
                table.RecomendacionesTopJsonField,
                table.AlertasJsonField,
                table.IncidentesJsonField,
                table.FechaConsultaField,
                table.EstadoConsultaField,
                table.ErrorConsultaField
            }
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private string BuildGeneratedReportSelectClause()
    {
        var table = _options.GeneratedReport;
        return string.Join(",",
            new[]
            {
                table.IdField,
                table.PrimaryNameField,
                table.InternalClientIdField,
                BuildLookupValuePropertyName(table.ClientLookupField),
                table.PeriodoField,
                table.HtmlGeneradoField,
                table.EstadoField,
                table.FechaGeneracionField,
                table.PromptVersionField,
                table.ErroresField
            }
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private string BuildGeneratedReportListSelectClause()
    {
        var table = _options.GeneratedReport;
        return string.Join(",",
            new[]
            {
                table.IdField,
                table.PrimaryNameField,
                table.InternalClientIdField,
                BuildLookupValuePropertyName(table.ClientLookupField),
                table.PeriodoField,
                table.EstadoField,
                table.FechaGeneracionField,
                table.PromptVersionField,
                table.ErroresField
            }
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<string>> ResolveExistingAttributesAsync(
        string entityLogicalName,
        IEnumerable<string> candidateFields,
        CancellationToken ct)
    {
        var existing = new List<string>();
        foreach (var field in candidateFields.Where(field => !string.IsNullOrWhiteSpace(field)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (await AttributeExistsAsync(entityLogicalName, field.Trim(), ct))
                existing.Add(field.Trim());
        }

        return existing;
    }

    private async Task<bool> AttributeExistsAsync(
        string entityLogicalName,
        string attributeLogicalName,
        CancellationToken ct)
    {
        var cacheKey = $"{entityLogicalName}|{attributeLogicalName}";
        if (_attributeExistsCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var relativeUrl =
            $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(entityLogicalName)}')" +
            $"/Attributes(LogicalName='{EscapeOdataLiteral(attributeLogicalName)}')?$select=LogicalName";
        var json = await CallDataverseAppGetJsonOrNullAsync(relativeUrl, ct);
        var exists = !string.IsNullOrWhiteSpace(json);
        _attributeExistsCache[cacheKey] = exists;
        return exists;
    }

    private async Task<string> ResolveLookupNavigationPropertyAsync(
        string entityLogicalName,
        string lookupField,
        string configuredNavigationProperty,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(configuredNavigationProperty)
            && !string.Equals(configuredNavigationProperty, lookupField, StringComparison.OrdinalIgnoreCase))
        {
            return configuredNavigationProperty.Trim();
        }

        if (string.IsNullOrWhiteSpace(entityLogicalName) || string.IsNullOrWhiteSpace(lookupField))
            return lookupField;

        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(entityLogicalName)}')" +
                "?$select=LogicalName" +
                "&$expand=ManyToOneRelationships($select=ReferencingAttribute,ReferencingEntityNavigationPropertyName)";
            var json = await CallDataverseAppGetJsonAsync(relativeUrl, ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ManyToOneRelationships", out var relationships)
                && relationships.ValueKind == JsonValueKind.Array)
            {
                var navigationProperty = relationships
                    .EnumerateArray()
                    .Where(relationship => string.Equals(
                        ReadString(relationship, "ReferencingAttribute"),
                        lookupField,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(relationship => ReadString(relationship, "ReferencingEntityNavigationPropertyName"))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

                if (!string.IsNullOrWhiteSpace(navigationProperty))
                    return navigationProperty.Trim();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(
                ex,
                "No fue posible resolver la propiedad de navegacion {LookupField} para {EntityLogicalName}. Se usara {Fallback}.",
                lookupField,
                entityLogicalName,
                lookupField);
        }

        return lookupField;
    }

    private async Task<string> GetDataverseAppAccessTokenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_dataverseBaseUrl)
            || string.IsNullOrWhiteSpace(_dataverseTenantId)
            || string.IsNullOrWhiteSpace(_dataverseClientId)
            || string.IsNullOrWhiteSpace(_dataverseClientSecret))
        {
            throw new ReportesConfigurationException(
                "La generacion de reportes requiere credenciales app-only para Dataverse. Configura Dataverse:BaseUrl, Dataverse:TenantId o AzureAd:TenantId, y una credencial valida: Dataverse:ClientSecret con Dataverse:ClientId o AzureAd:ClientId, AzureAd:ClientSecret con AzureAd:ClientId, o M365:ClientSecret con M365:ClientId.");
        }

        var authority = $"{_azureAuthorityInstance.TrimEnd('/')}/{_dataverseTenantId.Trim()}";
        var app = ConfidentialClientApplicationBuilder
            .Create(_dataverseClientId.Trim())
            .WithClientSecret(_dataverseClientSecret)
            .WithAuthority(authority)
            .Build();
        var result = await app
            .AcquireTokenForClient(new[] { $"{_dataverseBaseUrl}/.default" })
            .ExecuteAsync(ct);

        _logger.LogDebug(
            "Token app-only de Dataverse obtenido usando credencial {CredentialSource}.",
            string.IsNullOrWhiteSpace(_dataverseCredentialSource) ? "sin origen" : _dataverseCredentialSource);

        return result.AccessToken;
    }

    private async Task<string> CallDataverseAppGetJsonAsync(
        string relativeUrl,
        CancellationToken ct,
        Action<HttpRequestMessage>? customizeRequest = null)
    {
        using var response = await CallDataverseAppResponseAsync(relativeUrl, "GET", ct, customizeRequest: customizeRequest);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw BuildDataverseAppException(response, body);

        return body;
    }

    private async Task<string?> CallDataverseAppGetJsonOrNullAsync(
        string relativeUrl,
        CancellationToken ct,
        Action<HttpRequestMessage>? customizeRequest = null)
    {
        using var response = await CallDataverseAppResponseAsync(relativeUrl, "GET", ct, customizeRequest: customizeRequest);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
            throw BuildDataverseAppException(response, body);

        return body;
    }

    private async Task<string> CallDataverseAppSendAsync(
        string relativeUrl,
        string method,
        object payload,
        CancellationToken ct,
        Action<HttpRequestMessage>? customizeRequest = null)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await CallDataverseAppResponseAsync(relativeUrl, method, ct, content, customizeRequest);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw BuildDataverseAppException(response, body);

        return body;
    }

    private InvalidOperationException BuildDataverseAppException(HttpResponseMessage response, string body)
    {
        var baseMessage = $"Dataverse app error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}";
        if (response.StatusCode == HttpStatusCode.Forbidden
            && (body.Contains("0x80072560", StringComparison.OrdinalIgnoreCase)
                || body.Contains("not a member of the organization", StringComparison.OrdinalIgnoreCase)))
        {
            return new ReportesConfigurationException(
                "La app configurada para reportes no es miembro del entorno Dataverse. Crea o activa un Application User para la App Registration indicada y asignale un rol con permisos sobre clientes, tickets, snapshots M365 y reportes generados.",
                new InvalidOperationException(baseMessage));
        }

        return new InvalidOperationException(baseMessage);
    }

    private async Task<HttpResponseMessage> CallDataverseAppResponseAsync(
        string relativeUrl,
        string method,
        CancellationToken ct,
        HttpContent? content = null,
        Action<HttpRequestMessage>? customizeRequest = null)
    {
        var token = await GetDataverseAppAccessTokenAsync(ct);
        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), BuildDataverseAppUri(relativeUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        request.Headers.TryAddWithoutValidation("OData-Version", "4.0");
        if (content is not null)
            request.Content = content;

        customizeRequest?.Invoke(request);
        return await client.SendAsync(request, ct);
    }

    private async Task<List<JsonElement>> GetDataverseAppEntitiesAsync(
        string relativeUrl,
        CancellationToken ct,
        Action<HttpRequestMessage>? customizeRequest = null)
    {
        const int maxPages = 50;
        var pageCount = 0;
        var items = new List<JsonElement>();
        string? nextRelativeUrl = relativeUrl;

        while (!string.IsNullOrWhiteSpace(nextRelativeUrl))
        {
            pageCount++;
            if (pageCount > maxPages)
                throw new InvalidOperationException("Se alcanzo el limite de paginas consultando datos de reportes en Dataverse.");

            var json = await CallDataverseAppGetJsonAsync(nextRelativeUrl, ct, customizeRequest);
            using var doc = JsonDocument.Parse(json);
            var value = doc.RootElement.GetProperty("value");
            foreach (var item in value.EnumerateArray())
            {
                items.Add(item.Clone());
            }

            nextRelativeUrl = doc.RootElement.TryGetProperty("@odata.nextLink", out var nextLinkProp)
                ? GetRelativeDataverseUrl(nextLinkProp.GetString())
                : null;
        }

        return items;
    }

    private Uri BuildDataverseAppUri(string relativeUrl)
    {
        if (Uri.TryCreate(relativeUrl, UriKind.Absolute, out var absoluteUri))
            return absoluteUri;

        var normalizedRelativeUrl = relativeUrl.StartsWith("/", StringComparison.Ordinal)
            ? relativeUrl
            : $"/{relativeUrl}";

        return new Uri($"{_dataverseBaseUrl}{normalizedRelativeUrl}", UriKind.Absolute);
    }

    private static string BuildTicketDateFilter(string creationDateField, DateOnly startDate, DateOnly endExclusiveDate)
    {
        var start = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = endExclusiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $"{creationDateField} ge {start} and {creationDateField} lt {end}";
    }

    private static bool IsTicketInPeriod(
        ReporteTicketData ticket,
        DateOnly startDate,
        DateOnly endExclusiveDate)
    {
        if (!DateOnly.TryParseExact(
                ticket.CreationDateValue,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return true;
        }

        return date >= startDate && date < endExclusiveDate;
    }

    private static decimal RoundDecimal(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string NormalizeGuid(string? raw, string paramName)
    {
        if (!Guid.TryParse(raw, out var parsed))
            throw new InvalidOperationException($"El valor de {paramName} no es valido.");

        return parsed.ToString("D");
    }

    private static string NormalizeRequiredText(string? value, string paramName)
    {
        var normalized = value?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException($"El valor de {paramName} es obligatorio.");

        return normalized;
    }

    private static string NormalizeCssColor(string? raw, string fallback)
    {
        var value = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        if (value.StartsWith("#", StringComparison.Ordinal)
            && (value.Length is 4 or 7 or 9)
            && value.Skip(1).All(Uri.IsHexDigit))
        {
            return value;
        }

        if ((value.Length is 3 or 6 or 8) && value.All(Uri.IsHexDigit))
            return $"#{value}";

        if (value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("hsl(", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("hsla(", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return fallback;
    }

    private static string EscapeOdataLiteral(string value) =>
        (value ?? string.Empty).Replace("'", "''");

    private static DataverseAppCredential ResolveDataverseAppCredential(IConfiguration configuration)
    {
        var dataverseClientId = FirstNonEmpty(configuration["Dataverse:ClientId"], configuration["AzureAd:ClientId"]);
        var dataverseClientSecret = FirstNonEmpty(configuration["Dataverse:ClientSecret"]);
        if (!string.IsNullOrWhiteSpace(dataverseClientId)
            && !string.IsNullOrWhiteSpace(dataverseClientSecret))
        {
            return new DataverseAppCredential(dataverseClientId, dataverseClientSecret, "Dataverse");
        }

        var azureClientId = FirstNonEmpty(configuration["AzureAd:ClientId"]);
        var azureClientSecret = FirstNonEmpty(configuration["AzureAd:ClientSecret"]);
        if (!string.IsNullOrWhiteSpace(azureClientId)
            && !string.IsNullOrWhiteSpace(azureClientSecret))
        {
            return new DataverseAppCredential(azureClientId, azureClientSecret, "AzureAd");
        }

        var m365ClientId = FirstNonEmpty(configuration["M365:ClientId"]);
        var m365ClientSecret = FirstNonEmpty(configuration["M365:ClientSecret"]);
        if (!string.IsNullOrWhiteSpace(m365ClientId)
            && !string.IsNullOrWhiteSpace(m365ClientSecret))
        {
            return new DataverseAppCredential(m365ClientId, m365ClientSecret, "M365");
        }

        return new DataverseAppCredential(dataverseClientId, "", "");
    }

    private static string? GetRelativeDataverseUrl(string? nextLink)
    {
        if (string.IsNullOrWhiteSpace(nextLink))
            return null;

        if (Uri.TryCreate(nextLink, UriKind.Absolute, out var absoluteUri))
            return $"{absoluteUri.AbsolutePath}{absoluteUri.Query}";

        return nextLink;
    }

    private static string BuildLookupValuePropertyName(string lookupField) =>
        $"_{lookupField}_value";

    private static string ReadFirstString(JsonElement item, IEnumerable<string> propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = ReadString(item, propertyName).Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return "";
    }

    private static string ReadFormattedValue(JsonElement item, string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return "";

        return ReadString(item, $"{propertyName}{FormattedValueAnnotationSuffix}").Trim();
    }

    private static string ReadLookupFormattedValue(JsonElement item, string? lookupValuePropertyName)
    {
        if (string.IsNullOrWhiteSpace(lookupValuePropertyName))
            return "";

        return ReadString(item, $"{lookupValuePropertyName}{FormattedValueAnnotationSuffix}").Trim();
    }

    private static string ReadString(JsonElement item, string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return "";

        if (!item.TryGetProperty(propertyName, out var property))
            return "";

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? "",
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => ""
        };
    }

    private static decimal? ReadDecimal(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
            return number;

        if (property.ValueKind == JsonValueKind.String
            && decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int? ReadInt(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            return number;

        if (property.ValueKind == JsonValueKind.String
            && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateOnly? ReadDateOnly(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return null;

        var raw = property.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dateOnly))
            return dateOnly;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            return DateOnly.FromDateTime(dto.ToOffset(TimeSpan.FromHours(-5)).DateTime);

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return DateOnly.FromDateTime(dt);

        return null;
    }

    private static void AddReturnRepresentationHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation(
            "Prefer",
            $"return=representation, odata.include-annotations=\"{FormattedValueAnnotationSuffix.TrimStart('@')}\"");
    }

    private static void AddFormattedValueHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("Prefer", $"odata.include-annotations=\"{FormattedValueAnnotationSuffix.TrimStart('@')}\"");
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private sealed record DataverseAppCredential(string ClientId, string ClientSecret, string Source);
}

public sealed class ReportesConfigurationException : InvalidOperationException
{
    public ReportesConfigurationException(string message)
        : base(message)
    {
    }

    public ReportesConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
