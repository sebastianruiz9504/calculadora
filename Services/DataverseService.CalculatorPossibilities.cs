using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Calculator;
using CotizadorInterno.Web.Services.Calculator;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const int CalculatorRecordTypeGroup = 645250000;
    private const int CalculatorRecordTypePossibility = 645250001;
    private const int CalculatorRecordTypeLine = 645250002;
    private const int CalculatorRecordTypeProposalExport = 645250003;
    private const int CalculatorExportStatusUploading = 645250000;
    private const int CalculatorExportStatusCompleted = 645250001;
    private const int CalculatorExportStatusFailed = 645250002;
    private const int CalculatorMaxPossibilities = 3;
    private const int CalculatorMaxLinesPerPossibility = 900;
    private const int CalculatorMaxChangeSetOperations = 1_000;
    private const int CalculatorLegacyMemoCompatibilityMaxLength = 1_000_000;

    private const string CalculatorRecordTypeField = "cr07a_recordtype";
    private const string CalculatorRecordKeyField = "cr07a_recordkey";
    private const string CalculatorGroupIdField = "cr07a_groupid";
    private const string CalculatorGroupNameField = "cr07a_groupname";
    private const string CalculatorPossibilityNameField = "cr07a_possibilityname";
    private const string CalculatorPossibilityOrderField = "cr07a_possibilityorder";
    private const string CalculatorIncludeInProposalField = "cr07a_includeinproposal";
    private const string CalculatorIsRecommendedField = "cr07a_isrecommended";
    private const string CalculatorInputHashField = "cr07a_inputhash";
    private const string CalculatorStructuredLinesHashField = "cr07a_lineshash";
    private const string CalculatorParentLookupNavigation = "cr07a_ParentRecord";

    private const string CalculatorLineIdField = "cr07a_lineid";
    private const string CalculatorLineOrderField = "cr07a_lineorder";
    private const string CalculatorLinePossibilityIdField = "cr07a_possibilityid";
    private const string CalculatorLineBusinessTypeField = "cr07a_linebusinesstype";
    private const string CalculatorLineProductIdField = "cr07a_lineproductid";
    private const string CalculatorLineDescriptionField = "cr07a_lineproductdescription";
    private const string CalculatorLineCostUnitField = "cr07a_linecostunit";
    private const string CalculatorLineMarginField = "cr07a_linemarginpercent";
    private const string CalculatorLineContractMonthsField = "cr07a_linecontractmonths";
    private const string CalculatorLineQuantityField = "cr07a_linequantity";
    private const string CalculatorLineSuggestedPriceField = "cr07a_linesuggestedprice";
    private const string CalculatorLineAcceleratorField = "cr07a_lineaccelerator";
    private const string CalculatorLineHasVatField = "cr07a_linehasvat";

    private const string CalculatorResultPointsField = "cr07a_resultpoints";
    private const string CalculatorResultCommissionField = "cr07a_resultcommission";
    private const string CalculatorResultProrationDaysField = "cr07a_resultprorationdays";
    private const string CalculatorResultProrationFactorField = "cr07a_resultprorationfactor";
    private const string CalculatorResultProrationTextField = "cr07a_resultprorationtext";
    private const string CalculatorResultMonthlyField = "cr07a_resultmonthlysale";
    private const string CalculatorResultTotalField = "cr07a_resulttotalsale";

    public async Task<IReadOnlyList<ScenarioStoredDto>> GetScenariosByGroupIdAsync(
        string groupId,
        CancellationToken ct = default)
    {
        var normalized = groupId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            return [];
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var typeFilter = $"({CalculatorRecordTypeField} eq null or ({CalculatorRecordTypeField} eq {CalculatorRecordTypePossibility} and {CalculatorStructuredLinesHashField} ne null))";
        var filter = $"{CalculatorGroupIdField} eq '{EscapeOdataLiteral(normalized)}' and {typeFilter}";
        var url = $"/api/data/v9.2/{_scenariosTableSetName}?$select={BuildCalculatorPossibilitySelect()}&$filter={Uri.EscapeDataString(filter)}&$orderby={CalculatorPossibilityOrderField} asc&$top={CalculatorMaxPossibilities + 1}";
        var json = await CallDataverseGetJsonAsync(url, httpContext.User, ct);
        using var doc = JsonDocument.Parse(json);
        var scenarios = doc.RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(ParseCalculatorPossibility)
            .Where(item => !string.IsNullOrWhiteSpace(item.ScenarioId))
            .ToList();
        if (scenarios.Count == 0)
        {
            var legacy = await GetCalculatorPossibilityByIdAsync(normalized, ct);
            if (legacy is not null)
                scenarios.Add(legacy);
        }
        if (scenarios.Count > CalculatorMaxPossibilities)
            throw new ScenarioPersistenceConflictException("El negocio contiene más de tres escenarios.");
        if (scenarios.Count > 0)
            await HydrateCalculatorLinesAsync(scenarios, scenarios[0].OwnerSystemUserId, httpContext.User, ct);
        return scenarios;
    }

    private async Task<IReadOnlyList<ScenarioStoredDto>> GetCalculatorPossibilitiesForCurrentUserAsync(
        CancellationToken ct)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var currentUser = await GetCurrentUserAsync(ct);
        if (currentUser is null || string.IsNullOrWhiteSpace(currentUser.SystemUserId))
            return [];

        var select = BuildCalculatorPossibilitySelect();
        var ownerFilter = $"cr07a_systemuserid eq '{EscapeOdataLiteral(currentUser.SystemUserId)}'";
        var typeFilter = $"({CalculatorRecordTypeField} eq null or ({CalculatorRecordTypeField} eq {CalculatorRecordTypePossibility} and {CalculatorStructuredLinesHashField} ne null))";
        var filter = $"{ownerFilter} and {typeFilter}";
        var relativeUrl =
            $"/api/data/v9.2/{_scenariosTableSetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={CalculatorGroupNameField} asc,{CalculatorPossibilityOrderField} asc,createdon asc";
        var rawItems = await GetDataverseEntitiesAsync(relativeUrl, httpContext.User, ct);
        var items = rawItems
            .Select(ParseCalculatorPossibility)
            .Where(item => !string.IsNullOrWhiteSpace(item.ScenarioId))
            .ToList();

        await HydrateCalculatorLinesAsync(items, currentUser.SystemUserId, httpContext.User, ct);
        return items;
    }

    private async Task<ScenarioStoredDto?> GetCalculatorPossibilityByIdAsync(
        string scenarioId,
        CancellationToken ct)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var record = await FindCalculatorPossibilityRecordAsync(scenarioId, httpContext.User, ct);
        if (record is null)
            return null;

        await HydrateCalculatorLinesAsync(
            [record.Scenario],
            record.Scenario.OwnerSystemUserId,
            httpContext.User,
            ct);
        return record.Scenario;
    }

    private async Task<ScenarioStoredDto?> SaveCalculatorPossibilityAsync(
        ScenarioSaveRequest request,
        bool updateOnly,
        CancellationToken ct,
        string? authorizedOwnerSystemUserId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scenarioId = request.ScenarioId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(scenarioId))
            throw new ArgumentException("ScenarioId requerido.", nameof(request));

        NormalizeCalculatorPossibilityRequest(request);
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var currentUser = await GetCurrentUserAsync(ct)
            ?? throw new InvalidOperationException("Usuario actual no disponible.");
        if (string.IsNullOrWhiteSpace(currentUser.SystemUserId))
            throw new InvalidOperationException("Usuario actual no disponible.");

        var record = await FindCalculatorPossibilityRecordAsync(scenarioId, httpContext.User, ct);
        var ownerMatchesCurrentUser = record is not null && string.Equals(
            record.Scenario.OwnerSystemUserId,
            currentUser.SystemUserId,
            StringComparison.OrdinalIgnoreCase);
        var ownerMatchesAuthorizedContext = record is not null
            && !string.IsNullOrWhiteSpace(authorizedOwnerSystemUserId)
            && string.Equals(
                record.Scenario.OwnerSystemUserId,
                authorizedOwnerSystemUserId.Trim(),
                StringComparison.OrdinalIgnoreCase);
        if (record is not null && !ownerMatchesCurrentUser && !ownerMatchesAuthorizedContext)
        {
            throw new ScenarioPersistenceConflictException(
                "El escenario pertenece a otro usuario y solo puede editarse desde su negocio CRM asociado.");
        }
        if (record is null && updateOnly)
            throw new ScenarioPersistenceNotFoundException("El escenario asociado al negocio ya no existe.");

        if (record is not null
            && !string.IsNullOrWhiteSpace(request.ExpectedRowVersion)
            && !string.Equals(request.ExpectedRowVersion.Trim(), record.ETag, StringComparison.Ordinal))
        {
            throw new ScenarioPersistenceConcurrencyException(
                "Este escenario cambió en otra pestaña. Recarga el negocio antes de continuar.");
        }

        var persistenceOwner = ownerMatchesAuthorizedContext && !ownerMatchesCurrentUser
            ? new CurrentUserInfo
            {
                SystemUserId = record!.Scenario.OwnerSystemUserId,
                DisplayName = FirstNonEmpty(record.Scenario.OwnerDisplayName, record.Scenario.OwnerSystemUserId),
                Email = record.Scenario.OwnerEmail
            }
            : currentUser;

        if (record is not null
            && !string.Equals(
                FirstNonEmpty(record.Scenario.GroupId, record.Scenario.ScenarioId),
                request.GroupId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ScenarioPersistenceConflictException(
                "Un escenario existente no se puede mover a otro negocio.");
        }

        var possibilities = await QueryCalculatorPossibilityRecordsByGroupAsync(
            request.GroupId,
            persistenceOwner.SystemUserId,
            httpContext.User,
            ct,
            includeIncomplete: true);
        if (record is null)
        {
            if (possibilities.Count >= CalculatorMaxPossibilities)
                throw new ScenarioPersistenceConflictException("Un negocio admite máximo tres escenarios.");
            if (possibilities.Any(item => item.Scenario.PossibilityOrder == request.PossibilityOrder))
                throw new ScenarioPersistenceConflictException("Ya existe un escenario en esa posición.");
            request.IsRecommended = possibilities.Count == 0;
        }
        else
        {
            if (possibilities.Any(item =>
                    !string.Equals(item.RecordId, record.RecordId, StringComparison.OrdinalIgnoreCase)
                    && item.Scenario.PossibilityOrder == request.PossibilityOrder))
            {
                throw new ScenarioPersistenceConflictException("Ya existe un escenario en esa posición.");
            }

            // La recomendación solo cambia mediante el change set especializado; un
            // autoguardado o una petición manipulada no puede crear dos recomendadas.
            request.IsRecommended = record.Scenario.IsRecommended;
        }

        var groupRecordId = await EnsureCalculatorGroupRecordAsync(
            request,
            persistenceOwner,
            httpContext.User,
            ct);

        var payload = BuildCalculatorPossibilityPayload(request, persistenceOwner, groupRecordId, record is null);
        var isCreate = record is null;
        if (isCreate)
        {
            // La fila queda fuera de las consultas normales hasta que el change set
            // publique, en una sola operación, sus hijos tipados y la huella final.
            payload[CalculatorStructuredLinesHashField] = null;
            await SendCalculatorJsonAsync(
                $"/api/data/v9.2/{_scenariosTableSetName}",
                "POST",
                payload,
                httpContext.User,
                ct,
                requestMessage => requestMessage.Headers.TryAddWithoutValidation("Prefer", "return=representation"));
            record = await FindCalculatorPossibilityRecordAsync(scenarioId, httpContext.User, ct)
                ?? throw new InvalidOperationException("Dataverse creó el escenario pero no fue posible leerlo nuevamente.");
        }
        var committedRecord = record
            ?? throw new InvalidOperationException("No fue posible resolver el escenario que se va a guardar.");

        payload[CalculatorStructuredLinesHashField] = ScenarioInputHasher.ComputeLines(request.Lines);
        var commitEtag = isCreate || string.IsNullOrWhiteSpace(request.ExpectedRowVersion)
            ? committedRecord.ETag
            : request.ExpectedRowVersion.Trim();
        try
        {
            await CommitCalculatorPossibilityAsync(
                committedRecord.RecordId,
                request,
                payload,
                commitEtag,
                persistenceOwner,
                httpContext.User,
                ct);
        }
        catch
        {
            if (isCreate)
            {
                try
                {
                    await DeleteCalculatorRecordAsync(
                        committedRecord.RecordId,
                        httpContext.User,
                        ct,
                        committedRecord.ETag);
                }
                catch
                {
                    // La excepción original conserva la causa. El escenario incompleto
                    // queda con LinesHash nulo y las consultas normales no la publican.
                }
            }
            throw;
        }

        return await GetCalculatorPossibilityByIdAsync(scenarioId, ct);
    }

    private async Task<bool> DeleteCalculatorPossibilityAsync(string scenarioId, CancellationToken ct)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var currentUser = await GetCurrentUserAsync(ct)
            ?? throw new InvalidOperationException("Usuario actual no disponible.");
        var record = await FindCalculatorPossibilityRecordAsync(scenarioId, httpContext.User, ct);
        if (record is null
            || !string.Equals(record.Scenario.OwnerSystemUserId, currentUser.SystemUserId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var siblings = await QueryCalculatorPossibilityRecordsByGroupAsync(
            record.Scenario.GroupId,
            currentUser.SystemUserId,
            httpContext.User,
            ct);
        if (siblings.Count <= 1)
            throw new ScenarioPersistenceConflictException("Debes mantener al menos un escenario en el negocio.");

        var lines = await QueryCalculatorLineRecordsAsync(scenarioId, currentUser.SystemUserId, httpContext.User, ct);
        var remaining = siblings
            .Where(item => !string.Equals(item.RecordId, record.RecordId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Scenario.PossibilityOrder)
            .ToList();
        var recommendedScenarioId = record.Scenario.IsRecommended
            ? remaining[0].Scenario.ScenarioId
            : remaining.FirstOrDefault(item => item.Scenario.IsRecommended)?.Scenario.ScenarioId
                ?? remaining[0].Scenario.ScenarioId;
        var operations = lines
            .Select(line => new CalculatorChangeSetOperation(
                "DELETE",
                $"/api/data/v9.2/{_scenariosTableSetName}({line.RecordId})",
                null,
                line.ETag))
            .Concat(remaining
            .Select(item => new CalculatorChangeSetOperation(
                "PATCH",
                $"/api/data/v9.2/{_scenariosTableSetName}({item.RecordId})",
                JsonSerializer.Serialize(
                    new Dictionary<string, object?>
                    {
                        [CalculatorIsRecommendedField] = string.Equals(
                            item.Scenario.ScenarioId,
                            recommendedScenarioId,
                            StringComparison.OrdinalIgnoreCase)
                    },
                    JsonOptions),
                item.ETag)))
            .Append(new CalculatorChangeSetOperation(
                "DELETE",
                $"/api/data/v9.2/{_scenariosTableSetName}({record.RecordId})",
                null,
                record.ETag))
            .ToList();
        if (operations.Count > CalculatorMaxChangeSetOperations)
            throw new ScenarioPersistenceConflictException("El escenario supera el límite atómico de eliminación.");
        await ExecuteCalculatorChangeSetAsync(operations, httpContext.User, ct);
        return true;
    }

    private async Task<bool> RecommendCalculatorPossibilityAsync(
        string groupId,
        string scenarioId,
        CancellationToken ct)
    {
        var normalizedGroupId = groupId?.Trim() ?? "";
        var normalizedScenarioId = scenarioId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalizedGroupId)
            || string.IsNullOrWhiteSpace(normalizedScenarioId))
        {
            return false;
        }

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var currentUser = await GetCurrentUserAsync(ct)
            ?? throw new InvalidOperationException("Usuario actual no disponible.");
        if (string.IsNullOrWhiteSpace(currentUser.SystemUserId))
            throw new InvalidOperationException("Usuario actual no disponible.");

        var siblings = await QueryCalculatorPossibilityRecordsByGroupAsync(
            normalizedGroupId,
            currentUser.SystemUserId,
            httpContext.User,
            ct);
        if (!siblings.Any(item => string.Equals(
                item.Scenario.ScenarioId,
                normalizedScenarioId,
                StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var operations = siblings
            .Select(item => new CalculatorChangeSetOperation(
                "PATCH",
                $"/api/data/v9.2/{_scenariosTableSetName}({item.RecordId})",
                JsonSerializer.Serialize(
                    new Dictionary<string, object?>
                    {
                        [CalculatorIsRecommendedField] = string.Equals(
                            item.Scenario.ScenarioId,
                            normalizedScenarioId,
                            StringComparison.OrdinalIgnoreCase)
                    },
                    JsonOptions),
                item.ETag))
            .ToList();
        await ExecuteCalculatorChangeSetAsync(operations, httpContext.User, ct);
        return true;
    }

    private async Task<bool> RenameCalculatorScenarioGroupAsync(
        string groupId,
        string groupName,
        CancellationToken ct)
    {
        var normalizedGroupId = groupId?.Trim() ?? "";
        var normalizedGroupName = groupName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalizedGroupId))
            return false;
        if (normalizedGroupId.Length > 100)
            throw new ArgumentException("El identificador del negocio admite máximo 100 caracteres.", nameof(groupId));
        if (string.IsNullOrWhiteSpace(normalizedGroupName))
            throw new ArgumentException("El nombre del negocio es obligatorio.", nameof(groupName));
        if (normalizedGroupName.Length > 200)
            throw new ArgumentException("El nombre del negocio admite máximo 200 caracteres.", nameof(groupName));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var currentUser = await GetCurrentUserAsync(ct)
            ?? throw new InvalidOperationException("Usuario actual no disponible.");
        if (string.IsNullOrWhiteSpace(currentUser.SystemUserId))
            throw new InvalidOperationException("Usuario actual no disponible.");

        var siblings = await QueryCalculatorPossibilityRecordsByGroupAsync(
            normalizedGroupId,
            currentUser.SystemUserId,
            httpContext.User,
            ct,
            includeIncomplete: true);
        if (siblings.Count == 0)
        {
            var legacy = await FindCalculatorPossibilityRecordAsync(
                normalizedGroupId,
                httpContext.User,
                ct);
            if (legacy is not null
                && string.Equals(
                    legacy.Scenario.OwnerSystemUserId,
                    currentUser.SystemUserId,
                    StringComparison.OrdinalIgnoreCase))
            {
                siblings = [legacy];
            }
        }
        if (siblings.Count == 0)
            return false;
        if (siblings.Count > CalculatorMaxPossibilities)
            throw new ScenarioPersistenceConflictException("El negocio contiene más de tres escenarios.");

        var groupFilter =
            $"{CalculatorRecordTypeField} eq {CalculatorRecordTypeGroup} and {CalculatorGroupIdField} eq '{EscapeOdataLiteral(normalizedGroupId)}'";
        var groupSelect = $"{_scenariosTableName}id,cr07a_systemuserid,{CalculatorGroupNameField}";
        var groupUrl =
            $"/api/data/v9.2/{_scenariosTableSetName}?$select={groupSelect}&$filter={Uri.EscapeDataString(groupFilter)}&$top=2";

        async Task<(string RecordId, string ETag)> ReadGroupRecordAsync()
        {
            var raw = await CallDataverseGetJsonAsync(groupUrl, httpContext.User, ct);
            using var document = JsonDocument.Parse(raw);
            var values = document.RootElement.GetProperty("value");
            if (values.GetArrayLength() > 1)
                throw new ScenarioPersistenceConflictException("El negocio está duplicado en Dataverse.");
            if (values.GetArrayLength() == 0)
                return ("", "");

            var item = values[0];
            var ownerId = CalculatorReadString(item, "cr07a_systemuserid");
            if (!string.Equals(ownerId, currentUser.SystemUserId, StringComparison.OrdinalIgnoreCase))
                throw new ScenarioPersistenceConflictException("El negocio pertenece a otro usuario.");
            return (
                CalculatorReadString(item, $"{_scenariosTableName}id"),
                CalculatorReadString(item, "@odata.etag"));
        }

        var groupRecord = await ReadGroupRecordAsync();
        var groupPayload = new Dictionary<string, object?>
        {
            ["cr07a_name"] = CalculatorTrim(normalizedGroupName, 100),
            [CalculatorGroupNameField] = normalizedGroupName
        };
        var operations = new List<CalculatorChangeSetOperation>(siblings.Count + 1);
        if (string.IsNullOrWhiteSpace(groupRecord.RecordId))
        {
            groupPayload[CalculatorRecordTypeField] = CalculatorRecordTypeGroup;
            groupPayload[CalculatorRecordKeyField] = BuildCalculatorRecordKey("group", normalizedGroupId);
            groupPayload[CalculatorGroupIdField] = normalizedGroupId;
            groupPayload["cr07a_systemuserid"] = currentUser.SystemUserId;
            groupPayload["cr07a_displayname"] = currentUser.DisplayName;
            groupPayload["cr07a_email"] = currentUser.Email;
            operations.Add(new CalculatorChangeSetOperation(
                "POST",
                $"/api/data/v9.2/{_scenariosTableSetName}",
                JsonSerializer.Serialize(groupPayload, JsonOptions),
                null));
        }
        else
        {
            operations.Add(new CalculatorChangeSetOperation(
                "PATCH",
                $"/api/data/v9.2/{_scenariosTableSetName}({groupRecord.RecordId})",
                JsonSerializer.Serialize(groupPayload, JsonOptions),
                groupRecord.ETag));
        }
        operations.AddRange(siblings.Select(item => new CalculatorChangeSetOperation(
            "PATCH",
            $"/api/data/v9.2/{_scenariosTableSetName}({item.RecordId})",
            JsonSerializer.Serialize(
                new Dictionary<string, object?>
                {
                    [CalculatorGroupIdField] = normalizedGroupId,
                    [CalculatorGroupNameField] = normalizedGroupName
                },
                JsonOptions),
            item.ETag)));

        await ExecuteCalculatorChangeSetAsync(operations, httpContext.User, ct);
        return true;
    }

    private async Task<string> EnsureCalculatorGroupRecordAsync(
        ScenarioSaveRequest request,
        CurrentUserInfo currentUser,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var groupId = request.GroupId;
        var filter = $"{CalculatorRecordTypeField} eq {CalculatorRecordTypeGroup} and {CalculatorGroupIdField} eq '{EscapeOdataLiteral(groupId)}'";
        var select = $"{_scenariosTableName}id,cr07a_systemuserid";
        var url = $"/api/data/v9.2/{_scenariosTableSetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=2";
        var json = await CallDataverseGetJsonAsync(url, user, ct);
        using var doc = JsonDocument.Parse(json);
        var values = doc.RootElement.GetProperty("value");
        if (values.GetArrayLength() > 1)
            throw new ScenarioPersistenceConflictException("El negocio contenedor está duplicado en Dataverse.");
        if (values.GetArrayLength() == 1)
        {
            var item = values[0];
            var ownerId = CalculatorReadString(item, "cr07a_systemuserid");
            if (!string.Equals(ownerId, currentUser.SystemUserId, StringComparison.OrdinalIgnoreCase))
                throw new ScenarioPersistenceConflictException("El negocio contenedor pertenece a otro usuario.");
            return CalculatorReadString(item, $"{_scenariosTableName}id");
        }

        var payload = new Dictionary<string, object?>
        {
            ["cr07a_name"] = CalculatorTrim(request.GroupName, 100),
            [CalculatorRecordTypeField] = CalculatorRecordTypeGroup,
            [CalculatorRecordKeyField] = BuildCalculatorRecordKey("group", groupId),
            [CalculatorGroupIdField] = groupId,
            [CalculatorGroupNameField] = CalculatorTrim(request.GroupName, 200),
            ["cr07a_systemuserid"] = currentUser.SystemUserId,
            ["cr07a_displayname"] = currentUser.DisplayName,
            ["cr07a_email"] = currentUser.Email
        };
        await SendCalculatorJsonAsync(
            $"/api/data/v9.2/{_scenariosTableSetName}",
            "POST",
            payload,
            user,
            ct,
            message => message.Headers.TryAddWithoutValidation("Prefer", "return=representation"));

        json = await CallDataverseGetJsonAsync(url, user, ct);
        using var reread = JsonDocument.Parse(json);
        var rereadValues = reread.RootElement.GetProperty("value");
        if (rereadValues.GetArrayLength() != 1)
            throw new InvalidOperationException("Dataverse creó el negocio contenedor pero no fue posible leerlo nuevamente.");
        return CalculatorReadString(rereadValues[0], $"{_scenariosTableName}id");
    }

    private Dictionary<string, object?> BuildCalculatorPossibilityPayload(
        ScenarioSaveRequest request,
        CurrentUserInfo currentUser,
        string groupRecordId,
        bool isCreate)
    {
        var result = request.LastResult;
        var compatibilityLinesJson = JsonSerializer.Serialize(request.Lines, JsonOptions);
        if (compatibilityLinesJson.Length > CalculatorLegacyMemoCompatibilityMaxLength)
            compatibilityLinesJson = "";
        var payload = new Dictionary<string, object?>
        {
            ["cr07a_name"] = CalculatorTrim(request.PossibilityName, 100),
            ["cr07a_scenarioid"] = request.ScenarioId,
            ["cr07a_scenarioname"] = CalculatorTrim(request.PossibilityName, 200),
            ["cr07a_dealtype"] = request.DealType,
            ["cr07a_requiresproration"] = request.RequiresProration,
            ["cr07a_startdate"] = request.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["cr07a_enddate"] = request.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            // Los hijos tipados son la fuente de verdad. Este snapshot acotado se
            // confirma en el mismo change set y permite un rollback puente de solo
            // lectura sin volver a depender del límite del Memo para persistir.
            ["cr07a_linesjson"] = string.IsNullOrEmpty(compatibilityLinesJson)
                ? null
                : compatibilityLinesJson,
            ["cr07a_lastresultjson"] = result is null ? null : JsonSerializer.Serialize(result, JsonOptions),
            [CalculatorRecordTypeField] = CalculatorRecordTypePossibility,
            [CalculatorRecordKeyField] = BuildCalculatorRecordKey("possibility", request.ScenarioId),
            [CalculatorGroupIdField] = request.GroupId,
            [CalculatorGroupNameField] = CalculatorTrim(request.GroupName, 200),
            [CalculatorPossibilityNameField] = CalculatorTrim(request.PossibilityName, 200),
            [CalculatorPossibilityOrderField] = request.PossibilityOrder,
            [CalculatorIncludeInProposalField] = request.IncludeInProposal,
            [CalculatorIsRecommendedField] = request.IsRecommended,
            [CalculatorInputHashField] = result?.InputHash,
            [CalculatorResultPointsField] = result?.Points,
            [CalculatorResultCommissionField] = result?.Commission,
            [CalculatorResultProrationDaysField] = result?.ProrationDays,
            [CalculatorResultProrationFactorField] = result is null
                ? null
                : Math.Round(result.ProrationFactor, 10, MidpointRounding.AwayFromZero),
            [CalculatorResultProrationTextField] = CalculatorTrim(result?.ProrationText, 300),
            [CalculatorResultMonthlyField] = result?.TotalMonthlySale,
            [CalculatorResultTotalField] = result?.TotalSale,
            [$"{CalculatorParentLookupNavigation}@odata.bind"] = $"/{_scenariosTableSetName}({groupRecordId})"
        };
        if (isCreate)
        {
            payload["cr07a_systemuserid"] = currentUser.SystemUserId;
            payload["cr07a_displayname"] = currentUser.DisplayName;
            payload["cr07a_email"] = currentUser.Email;
        }
        return payload;
    }

    private async Task CommitCalculatorPossibilityAsync(
        string possibilityRecordId,
        ScenarioSaveRequest request,
        Dictionary<string, object?> possibilityPayload,
        string possibilityEtag,
        CurrentUserInfo currentUser,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var existing = await QueryCalculatorLineRecordsAsync(
            request.ScenarioId,
            currentUser.SystemUserId,
            user,
            ct);
        var byLineId = existing
            .Where(item => !string.IsNullOrWhiteSpace(item.Line.LineId))
            .ToDictionary(item => item.Line.LineId, StringComparer.OrdinalIgnoreCase);
        var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var operations = new List<CalculatorChangeSetOperation>(request.Lines.Count + existing.Count + 1);

        foreach (var line in request.Lines.OrderBy(item => item.LineOrder))
        {
            retained.Add(line.LineId);
            var payload = BuildCalculatorLinePayload(
                line,
                request.GroupId,
                request.ScenarioId,
                possibilityRecordId,
                currentUser,
                isCreate: !byLineId.ContainsKey(line.LineId));
            if (byLineId.TryGetValue(line.LineId, out var record))
            {
                operations.Add(new CalculatorChangeSetOperation(
                    "PATCH",
                    $"/api/data/v9.2/{_scenariosTableSetName}({record.RecordId})",
                    JsonSerializer.Serialize(payload, JsonOptions),
                    record.ETag));
            }
            else
            {
                operations.Add(new CalculatorChangeSetOperation(
                    "POST",
                    $"/api/data/v9.2/{_scenariosTableSetName}",
                    JsonSerializer.Serialize(payload, JsonOptions),
                    null));
            }
        }

        foreach (var stale in existing.Where(item => !retained.Contains(item.Line.LineId)))
        {
            operations.Add(new CalculatorChangeSetOperation(
                "DELETE",
                $"/api/data/v9.2/{_scenariosTableSetName}({stale.RecordId})",
                null,
                stale.ETag));
        }

        operations.Add(new CalculatorChangeSetOperation(
            "PATCH",
            $"/api/data/v9.2/{_scenariosTableSetName}({possibilityRecordId})",
            JsonSerializer.Serialize(possibilityPayload, JsonOptions),
            possibilityEtag));
        if (operations.Count > 1000)
            throw new InvalidOperationException("La actualización supera el límite atómico de Dataverse.");
        await ExecuteCalculatorChangeSetAsync(operations, user, ct);
    }

    private Dictionary<string, object?> BuildCalculatorLinePayload(
        ScenarioLineInput line,
        string groupId,
        string possibilityId,
        string possibilityRecordId,
        CurrentUserInfo currentUser,
        bool isCreate)
    {
        var payload = new Dictionary<string, object?>
        {
            ["cr07a_name"] = CalculatorTrim(string.IsNullOrWhiteSpace(line.ProductDescription) ? $"Línea {line.LineOrder}" : line.ProductDescription, 100),
            [CalculatorRecordTypeField] = CalculatorRecordTypeLine,
            [CalculatorRecordKeyField] = BuildCalculatorRecordKey("line", line.LineId),
            [CalculatorGroupIdField] = groupId,
            [CalculatorLinePossibilityIdField] = possibilityId,
            [CalculatorLineIdField] = line.LineId,
            [CalculatorLineOrderField] = line.LineOrder,
            [CalculatorLineBusinessTypeField] = line.BusinessType,
            [CalculatorLineProductIdField] = CalculatorTrim(line.ProductId, 100),
            [CalculatorLineDescriptionField] = CalculatorTrim(line.ProductDescription, 500),
            [CalculatorLineCostUnitField] = line.CostUnit,
            [CalculatorLineMarginField] = line.MarginPercent,
            [CalculatorLineContractMonthsField] = line.ContractMonths,
            [CalculatorLineQuantityField] = line.Quantity,
            [CalculatorLineSuggestedPriceField] = line.SuggestedRetailPrice,
            [CalculatorLineAcceleratorField] = line.Acelerador,
            [CalculatorLineHasVatField] = line.HasVat,
            [$"{CalculatorParentLookupNavigation}@odata.bind"] = $"/{_scenariosTableSetName}({possibilityRecordId})"
        };
        if (isCreate)
        {
            payload["cr07a_systemuserid"] = currentUser.SystemUserId;
            payload["cr07a_displayname"] = currentUser.DisplayName;
            payload["cr07a_email"] = currentUser.Email;
        }
        return payload;
    }

    private async Task HydrateCalculatorLinesAsync(
        IReadOnlyCollection<ScenarioStoredDto> scenarios,
        string ownerSystemUserId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (scenarios.Count == 0 || string.IsNullOrWhiteSpace(ownerSystemUserId))
            return;
        var scenarioIds = scenarios.Select(item => item.ScenarioId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filter = $"cr07a_systemuserid eq '{EscapeOdataLiteral(ownerSystemUserId)}' and {CalculatorRecordTypeField} eq {CalculatorRecordTypeLine}";
        var select = BuildCalculatorLineSelect();
        var url = $"/api/data/v9.2/{_scenariosTableSetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={CalculatorLinePossibilityIdField} asc,{CalculatorLineOrderField} asc";
        var rawLines = await GetDataverseEntitiesAsync(url, user, ct);
        var grouped = rawLines
            .Select(ParseCalculatorLine)
            .Where(item => scenarioIds.Contains(item.PossibilityId))
            .GroupBy(item => item.PossibilityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(item => item.Line.LineOrder).Select(item => item.Line).ToList(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var scenario in scenarios)
        {
            var lines = grouped.TryGetValue(scenario.ScenarioId, out var structuredLines)
                ? structuredLines
                : [];
            if (!string.IsNullOrWhiteSpace(scenario.StructuredLinesHash))
            {
                if (!string.Equals(
                        scenario.StructuredLinesHash,
                        ScenarioInputHasher.ComputeLines(lines),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new ScenarioPersistenceConflictException(
                        $"Las líneas tipadas del escenario '{scenario.PossibilityName}' no superaron la validación de integridad.");
                }
                scenario.Lines = lines;
            }
        }
    }

    private async Task<List<CalculatorLineRecord>> QueryCalculatorLineRecordsAsync(
        string possibilityId,
        string ownerSystemUserId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var filter =
            $"cr07a_systemuserid eq '{EscapeOdataLiteral(ownerSystemUserId)}' and {CalculatorRecordTypeField} eq {CalculatorRecordTypeLine} and {CalculatorLinePossibilityIdField} eq '{EscapeOdataLiteral(possibilityId)}'";
        var url = $"/api/data/v9.2/{_scenariosTableSetName}?$select={BuildCalculatorLineSelect()}&$filter={Uri.EscapeDataString(filter)}&$orderby={CalculatorLineOrderField} asc";
        var json = await CallDataverseGetJsonAsync(url, user, ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(item =>
            {
                var parsed = ParseCalculatorLine(item);
                return new CalculatorLineRecord(
                    CalculatorReadString(item, $"{_scenariosTableName}id"),
                    CalculatorReadString(item, "@odata.etag"),
                    parsed.Line);
            })
            .ToList();
    }

    private async Task<List<CalculatorPossibilityRecord>> QueryCalculatorPossibilityRecordsByGroupAsync(
        string groupId,
        string ownerSystemUserId,
        ClaimsPrincipal user,
        CancellationToken ct,
        bool includeIncomplete = false)
    {
        var typeFilter = includeIncomplete
            ? $"({CalculatorRecordTypeField} eq {CalculatorRecordTypePossibility} or {CalculatorRecordTypeField} eq null)"
            : $"({CalculatorRecordTypeField} eq null or ({CalculatorRecordTypeField} eq {CalculatorRecordTypePossibility} and {CalculatorStructuredLinesHashField} ne null))";
        var filter =
            $"cr07a_systemuserid eq '{EscapeOdataLiteral(ownerSystemUserId)}' and {typeFilter} and {CalculatorGroupIdField} eq '{EscapeOdataLiteral(groupId)}'";
        var url = $"/api/data/v9.2/{_scenariosTableSetName}?$select={BuildCalculatorPossibilitySelect()}&$filter={Uri.EscapeDataString(filter)}&$orderby={CalculatorPossibilityOrderField} asc";
        var json = await CallDataverseGetJsonAsync(url, user, ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(item => new CalculatorPossibilityRecord(
                CalculatorReadString(item, $"{_scenariosTableName}id"),
                CalculatorReadString(item, "@odata.etag"),
                ParseCalculatorPossibility(item)))
            .ToList();
    }

    private async Task<CalculatorPossibilityRecord?> FindCalculatorPossibilityRecordAsync(
        string scenarioId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalized = scenarioId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        var typeFilter = $"({CalculatorRecordTypeField} eq {CalculatorRecordTypePossibility} or {CalculatorRecordTypeField} eq null)";
        var filter = $"cr07a_scenarioid eq '{EscapeOdataLiteral(normalized)}' and {typeFilter}";
        var url = $"/api/data/v9.2/{_scenariosTableSetName}?$select={BuildCalculatorPossibilitySelect()}&$filter={Uri.EscapeDataString(filter)}&$top=2";
        var json = await CallDataverseGetJsonAsync(url, user, ct);
        using var doc = JsonDocument.Parse(json);
        var values = doc.RootElement.GetProperty("value");
        if (values.GetArrayLength() == 0)
            return null;
        if (values.GetArrayLength() > 1)
            throw new ScenarioPersistenceConflictException($"Existen varios escenarios con el identificador '{normalized}'.");
        var item = values[0];
        return new CalculatorPossibilityRecord(
            CalculatorReadString(item, $"{_scenariosTableName}id"),
            CalculatorReadString(item, "@odata.etag"),
            ParseCalculatorPossibility(item));
    }

    private ScenarioStoredDto ParseCalculatorPossibility(JsonElement item)
    {
        var scenarioId = CalculatorReadString(item, "cr07a_scenarioid");
        var scenarioName = CalculatorReadString(item, "cr07a_scenarioname");
        var groupId = CalculatorReadString(item, CalculatorGroupIdField);
        var groupName = CalculatorReadString(item, CalculatorGroupNameField);
        var possibilityName = CalculatorReadString(item, CalculatorPossibilityNameField);
        var order = CalculatorReadInt(item, CalculatorPossibilityOrderField);
        var linesJson = CalculatorReadString(item, "cr07a_linesjson");
        var resultJson = CalculatorReadString(item, "cr07a_lastresultjson");
        var result = DeserializeJsonOrDefault<ScenarioResultSnapshot>(resultJson);
        var inputHash = CalculatorReadString(item, CalculatorInputHashField);
        if (result is null && !string.IsNullOrWhiteSpace(inputHash))
        {
            result = new ScenarioResultSnapshot
            {
                InputHash = inputHash,
                Points = CalculatorReadDecimal(item, CalculatorResultPointsField),
                Commission = CalculatorReadDecimal(item, CalculatorResultCommissionField),
                ProrationDays = CalculatorReadInt(item, CalculatorResultProrationDaysField),
                ProrationFactor = CalculatorReadDecimal(item, CalculatorResultProrationFactorField),
                ProrationText = CalculatorReadString(item, CalculatorResultProrationTextField),
                TotalMonthlySale = CalculatorReadDecimal(item, CalculatorResultMonthlyField),
                TotalSale = CalculatorReadDecimal(item, CalculatorResultTotalField)
            };
        }
        else if (result is not null && string.IsNullOrWhiteSpace(result.InputHash))
        {
            result.InputHash = inputHash;
        }

        var resolvedScenarioName = FirstNonEmpty(possibilityName, scenarioName, "Escenario");
        return new ScenarioStoredDto
        {
            ScenarioId = scenarioId,
            GroupId = FirstNonEmpty(groupId, scenarioId),
            GroupName = FirstNonEmpty(groupName, scenarioName, "Negocio"),
            PossibilityName = resolvedScenarioName,
            PossibilityOrder = order > 0 ? order : 1,
            IncludeInProposal = CalculatorReadNullableBool(item, CalculatorIncludeInProposalField) ?? true,
            IsRecommended = CalculatorReadNullableBool(item, CalculatorIsRecommendedField) ?? order <= 1,
            RowVersion = CalculatorReadString(item, "@odata.etag"),
            OwnerSystemUserId = CalculatorReadString(item, "cr07a_systemuserid"),
            OwnerDisplayName = CalculatorReadString(item, "cr07a_displayname"),
            OwnerEmail = CalculatorReadString(item, "cr07a_email"),
            StructuredLinesHash = CalculatorReadString(item, CalculatorStructuredLinesHashField),
            ScenarioName = resolvedScenarioName,
            DealType = CalculatorReadInt(item, "cr07a_dealtype"),
            RequiresProration = CalculatorReadBool(item, "cr07a_requiresproration"),
            StartDate = CalculatorReadString(item, "cr07a_startdate"),
            EndDate = CalculatorReadString(item, "cr07a_enddate"),
            Lines = DeserializeJsonOrDefault<List<ScenarioLineInput>>(linesJson) ?? [],
            LastResult = result
        };
    }

    private static CalculatorParsedLine ParseCalculatorLine(JsonElement item) => new()
    {
        PossibilityId = CalculatorReadString(item, CalculatorLinePossibilityIdField),
        Line = new ScenarioLineInput
        {
            LineId = CalculatorReadString(item, CalculatorLineIdField),
            LineOrder = CalculatorReadInt(item, CalculatorLineOrderField),
            BusinessType = CalculatorReadInt(item, CalculatorLineBusinessTypeField),
            ProductId = CalculatorReadString(item, CalculatorLineProductIdField),
            ProductDescription = CalculatorReadString(item, CalculatorLineDescriptionField),
            CostUnit = CalculatorReadDecimal(item, CalculatorLineCostUnitField),
            MarginPercent = CalculatorReadDecimal(item, CalculatorLineMarginField),
            ContractMonths = CalculatorReadInt(item, CalculatorLineContractMonthsField),
            Quantity = CalculatorReadInt(item, CalculatorLineQuantityField),
            SuggestedRetailPrice = CalculatorReadDecimal(item, CalculatorLineSuggestedPriceField),
            Acelerador = CalculatorReadDecimal(item, CalculatorLineAcceleratorField),
            HasVat = CalculatorReadBool(item, CalculatorLineHasVatField)
        }
    };

    private static void NormalizeCalculatorPossibilityRequest(ScenarioSaveRequest request)
    {
        request.Lines ??= [];
        request.ScenarioId = request.ScenarioId.Trim();
        request.GroupId = FirstNonEmpty(request.GroupId, request.ScenarioId).Trim();
        request.GroupName = FirstNonEmpty(request.GroupName, request.ScenarioName, "Negocio").Trim();
        request.PossibilityName = FirstNonEmpty(request.PossibilityName, request.ScenarioName, "Escenario").Trim();
        request.ScenarioName = request.PossibilityName;
        if (request.ScenarioId.Length > 100 || request.GroupId.Length > 100)
            throw new InvalidOperationException("Los identificadores de escenario no pueden superar 100 caracteres.");
        if (request.GroupName.Length > 200 || request.PossibilityName.Length > 200)
            throw new InvalidOperationException("Los nombres de negocio y escenario no pueden superar 200 caracteres.");
        if (request.PossibilityOrder is < 1 or > CalculatorMaxPossibilities)
            throw new ScenarioPersistenceConflictException("La posición del escenario debe estar entre 1 y 3.");
        if (!Enum.IsDefined(typeof(DealType), request.DealType))
            throw new InvalidOperationException("El tipo de negocio del escenario no es válido.");
        if (request.RequiresProration
            && (!request.StartDate.HasValue
                || !request.EndDate.HasValue
                || request.EndDate.Value.Date < request.StartDate.Value.Date))
        {
            throw new InvalidOperationException("El escenario requiere fechas válidas para el prorrateo.");
        }
        if (request.Lines.Count > CalculatorMaxLinesPerPossibility)
            throw new InvalidOperationException($"Un escenario no puede superar {CalculatorMaxLinesPerPossibility} líneas.");

        var lineIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            line.LineId = string.IsNullOrWhiteSpace(line.LineId)
                ? Guid.NewGuid().ToString("D")
                : line.LineId.Trim();
            line.LineOrder = index + 1;
            line.ProductId = line.ProductId?.Trim() ?? "";
            line.ProductDescription = line.ProductDescription?.Trim() ?? "";
            if (line.LineId.Length > 100)
                throw new InvalidOperationException($"El identificador de la línea {index + 1} supera 100 caracteres.");
            if (!lineIds.Add(line.LineId))
                throw new InvalidOperationException($"El identificador de la línea {index + 1} está duplicado.");
            if (!Enum.IsDefined(typeof(BusinessType), line.BusinessType))
                throw new InvalidOperationException($"La línea {index + 1} tiene un tipo de negocio inválido.");
            if (line.ProductId.Length > 100 || line.ProductDescription.Length > 500)
                throw new InvalidOperationException($"El producto de la línea {index + 1} supera el tamaño permitido.");
            if (line.ContractMonths is < 1 or > 1200)
                throw new InvalidOperationException($"La duración de la línea {index + 1} debe estar entre 1 y 1.200 meses.");
            if (line.Quantity is < 1 or > 1_000_000)
                throw new InvalidOperationException($"La cantidad de la línea {index + 1} debe estar entre 1 y 1.000.000.");
            ValidateCalculatorDecimal(line.CostUnit, 4, $"costo de la línea {index + 1}", minimum: 0m);
            ValidateCalculatorDecimal(line.MarginPercent, 6, $"margen de la línea {index + 1}", minimum: -100m);
            ValidateCalculatorDecimal(line.SuggestedRetailPrice, 4, $"precio sugerido de la línea {index + 1}", minimum: 0m);
            ValidateCalculatorDecimal(line.Acelerador, 6, $"acelerador de la línea {index + 1}", minimum: 0m);
        }

        if (request.LastResult is not null)
        {
            var inputHash = ScenarioInputHasher.Compute(request);
            if (!string.IsNullOrWhiteSpace(request.LastResult.InputHash)
                && !string.Equals(request.LastResult.InputHash, inputHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new ScenarioPersistenceConcurrencyException(
                    "Los insumos cambiaron después del cálculo. Calcula nuevamente el escenario.");
            }
            request.LastResult.InputHash = inputHash;
            ValidateCalculatorDecimal(request.LastResult.Points, 6, "puntaje calculado");
            ValidateCalculatorDecimal(request.LastResult.Commission, 4, "comisión calculada");
            ValidateCalculatorDecimal(request.LastResult.ProrationFactor, 28, "factor de prorrateo");
            ValidateCalculatorDecimal(request.LastResult.TotalMonthlySale, 4, "venta mensual calculada");
            ValidateCalculatorDecimal(request.LastResult.TotalSale, 4, "venta contractual calculada");
        }
    }

    private static void ValidateCalculatorDecimal(
        decimal value,
        int maxScale,
        string label,
        decimal minimum = -100_000_000_000m,
        decimal maximum = 100_000_000_000m)
    {
        if (value < minimum || value > maximum)
            throw new InvalidOperationException($"El {label} está fuera del rango permitido.");
        var scale = (decimal.GetBits(value)[3] >> 16) & 0x7F;
        if (scale > maxScale)
            throw new InvalidOperationException($"El {label} supera {maxScale} decimales.");
    }

    private string BuildCalculatorPossibilitySelect() => string.Join(",", new[]
    {
        $"{_scenariosTableName}id", "cr07a_scenarioid", "cr07a_systemuserid", "cr07a_displayname", "cr07a_email", "cr07a_scenarioname",
        "cr07a_dealtype", "cr07a_requiresproration", "cr07a_startdate", "cr07a_enddate",
        "cr07a_linesjson", "cr07a_lastresultjson", CalculatorRecordTypeField, CalculatorRecordKeyField,
        CalculatorGroupIdField, CalculatorGroupNameField, CalculatorPossibilityNameField,
        CalculatorPossibilityOrderField, CalculatorIncludeInProposalField, CalculatorIsRecommendedField,
        CalculatorInputHashField, CalculatorStructuredLinesHashField,
        CalculatorResultPointsField, CalculatorResultCommissionField,
        CalculatorResultProrationDaysField, CalculatorResultProrationFactorField,
        CalculatorResultProrationTextField, CalculatorResultMonthlyField, CalculatorResultTotalField,
        "versionnumber"
    });

    private string BuildCalculatorLineSelect() => string.Join(",", new[]
    {
        $"{_scenariosTableName}id", CalculatorLinePossibilityIdField, CalculatorLineIdField,
        CalculatorLineOrderField, CalculatorLineBusinessTypeField, CalculatorLineProductIdField,
        CalculatorLineDescriptionField, CalculatorLineCostUnitField, CalculatorLineMarginField,
        CalculatorLineContractMonthsField, CalculatorLineQuantityField, CalculatorLineSuggestedPriceField,
        CalculatorLineAcceleratorField, CalculatorLineHasVatField, "versionnumber"
    });

    private async Task SendCalculatorJsonAsync(
        string relativeUrl,
        string method,
        object payload,
        ClaimsPrincipal user,
        CancellationToken ct,
        Action<HttpRequestMessage>? customizeRequest = null)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await CallRhDataverseResponseAsync(relativeUrl, method, user, ct, content, customizeRequest);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            throw new ScenarioPersistenceConcurrencyException("El escenario cambió en otra pestaña. Recarga antes de guardar.");
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new ScenarioPersistenceConflictException("Dataverse rechazó el registro porque su llave ya existe.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
    }

    private async Task ExecuteCalculatorChangeSetAsync(
        IReadOnlyCollection<CalculatorChangeSetOperation> operations,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (operations.Count == 0)
            return;

        var batchBoundary = $"batch_{Guid.NewGuid():N}";
        var changeSetBoundary = $"changeset_{Guid.NewGuid():N}";
        const string crlf = "\r\n";
        var body = new StringBuilder()
            .Append("--").Append(batchBoundary).Append(crlf)
            .Append("Content-Type: multipart/mixed; boundary=").Append(changeSetBoundary).Append(crlf)
            .Append(crlf);
        var contentId = 0;
        foreach (var operation in operations)
        {
            contentId++;
            body.Append("--").Append(changeSetBoundary).Append(crlf)
                .Append("Content-Type: application/http").Append(crlf)
                .Append("Content-Transfer-Encoding: binary").Append(crlf)
                .Append("Content-ID: ").Append(contentId.ToString(CultureInfo.InvariantCulture)).Append(crlf)
                .Append(crlf)
                .Append(operation.Method).Append(' ').Append(operation.RelativeUrl).Append(" HTTP/1.1").Append(crlf);
            if (!string.IsNullOrWhiteSpace(operation.IfMatch))
                body.Append("If-Match: ").Append(operation.IfMatch).Append(crlf);
            if (operation.JsonBody is not null)
            {
                body.Append("Content-Type: application/json; type=entry").Append(crlf)
                    .Append(crlf)
                    .Append(operation.JsonBody).Append(crlf);
            }
            else
            {
                body.Append(crlf);
            }
        }
        body.Append("--").Append(changeSetBoundary).Append("--").Append(crlf)
            .Append("--").Append(batchBoundary).Append("--").Append(crlf);

        using var content = new StringContent(body.ToString(), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("multipart/mixed");
        content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", batchBoundary));
        using var response = await CallRhDataverseResponseAsync(
            "/api/data/v9.2/$batch",
            "POST",
            user,
            ct,
            content);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse rechazó el cambio atómico: {(int)response.StatusCode} {responseBody}");
        if (responseBody.Contains("HTTP/1.1 412", StringComparison.OrdinalIgnoreCase))
        {
            throw new ScenarioPersistenceConcurrencyException(
                "El escenario cambió en otra pestaña. Recarga antes de continuar.");
        }
        if (System.Text.RegularExpressions.Regex.IsMatch(
                responseBody,
                @"HTTP/1\.1\s+[45]\d\d",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            throw new InvalidOperationException("Dataverse rechazó una operación y no aplicó ningún cambio al escenario.");
        }
    }

    private async Task DeleteCalculatorRecordAsync(
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct,
        string? etag = null)
    {
        using var response = await CallRhDataverseResponseAsync(
            $"/api/data/v9.2/{_scenariosTableSetName}({recordId})",
            "DELETE",
            user,
            ct,
            customizeRequest: request => request.Headers.TryAddWithoutValidation(
                "If-Match",
                FirstNonEmpty(etag, "*")));
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            return;
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode}: {body}");
    }

    private static string BuildCalculatorRecordKey(string kind, string value)
    {
        var normalized = new string((value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or ':')
            .ToArray());
        var key = $"{kind}:{normalized}";
        return key.Length <= 180 ? key : key[..180];
    }

    private static string CalculatorTrim(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? "";
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string CalculatorReadString(JsonElement item, string field) =>
        item.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString()
            : "";

    private static int CalculatorReadInt(JsonElement item, string field)
    {
        if (!item.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
            return 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number
                : 0;
    }

    private static decimal CalculatorReadDecimal(JsonElement item, string field)
    {
        if (!item.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
            return 0m;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : decimal.TryParse(value.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number)
                ? number
                : 0m;
    }

    private static bool CalculatorReadBool(JsonElement item, string field) =>
        CalculatorReadNullableBool(item, field) ?? false;

    private static bool? CalculatorReadNullableBool(JsonElement item, string field)
    {
        if (!item.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt32(out var number) => number != 0,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private sealed record CalculatorPossibilityRecord(string RecordId, string ETag, ScenarioStoredDto Scenario);
    private sealed record CalculatorLineRecord(string RecordId, string ETag, ScenarioLineInput Line);
    private sealed record CalculatorChangeSetOperation(
        string Method,
        string RelativeUrl,
        string? JsonBody,
        string? IfMatch);
    private sealed class CalculatorParsedLine
    {
        public string PossibilityId { get; init; } = "";
        public ScenarioLineInput Line { get; init; } = new();
    }
}
