using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Calculator;
using CotizadorInterno.Web.Models.Crm;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.Calculator;
using CotizadorInterno.Web.Services.Crm;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Calculator)]
public sealed class CalculatorController : Controller
{
    private readonly IDataverseService _dataverse;
    private readonly IQuoteCalculator _calculator;
    private readonly ICrmRepository _crmRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CalculatorOptions _calculatorOptions;
    private readonly bool _crmCalculatorSyncEnabled;
    private readonly ILogger<CalculatorController> _logger;
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private const int ProvisioningDescriptionMaxLength = 4000;
    private const int ProvisioningLongDescriptionMaxLength = 1048576;
    private const string ProvisioningDescriptionField = "cr07a_aprovisionamientodetallelargo";
    private const string ProvisioningLegacyDescriptionField = "cr07a_description";
    private const int ProvisioningContractKindNewBusinessValue = 645250000;
    private const int ProvisioningContractKindRenewalValue = 645250001;
    private static readonly CultureInfo ColombianCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly JsonSerializerOptions ProvisioningDescriptionJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public CalculatorController(
        IDataverseService dataverse,
        IQuoteCalculator calculator,
        ICrmRepository crmRepository,
        IHttpClientFactory httpClientFactory,
        IOptions<CalculatorOptions> calculatorOptions,
        ILogger<CalculatorController> logger,
        IConfiguration? configuration = null)
    {
        _dataverse = dataverse;
        _calculator = calculator;
        _crmRepository = crmRepository;
        _httpClientFactory = httpClientFactory;
        _calculatorOptions = calculatorOptions.Value;
        _crmCalculatorSyncEnabled =
            configuration?.GetValue<bool>("Crm:CalculatorSyncEnabled") ?? false;
        _logger = logger;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(
        [FromQuery] string? scenarioId,
        [FromQuery] string? crmDealId,
        CancellationToken ct,
        [FromQuery] bool embedded = false)
    {
        var currentUser = await _dataverse.GetCurrentUserAsync(ct);
        if (embedded
            && !Request.Query.ContainsKey("newCrmOpportunity")
            && !string.IsNullOrWhiteSpace(scenarioId))
        {
            try
            {
                ScenarioStoredDto? embeddedScenario;
                if (_crmCalculatorSyncEnabled
                    && !string.IsNullOrWhiteSpace(crmDealId)
                    && AppModuleAccessPolicy.CanAccess(AppModule.Crm, currentUser))
                {
                    embeddedScenario = await FindAuthorizedScenarioAsync(
                        scenarioId,
                        crmDealId,
                        currentUser,
                        ct);
                }
                else
                {
                    embeddedScenario = await _dataverse.GetScenarioByIdAsync(scenarioId.Trim(), ct);
                    if (embeddedScenario is not null
                        && (currentUser is null
                            || string.IsNullOrWhiteSpace(currentUser.SystemUserId)
                            || !string.Equals(
                                embeddedScenario.OwnerSystemUserId,
                                currentUser.SystemUserId,
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new CrmAccessDeniedException(
                            "El escenario no pertenece al usuario actual.");
                    }
                }

                if (embeddedScenario is null)
                {
                    return NotFound(new
                    {
                        message = "El escenario solicitado no existe o no está disponible.",
                        scenarioId,
                        traceId = HttpContext.TraceIdentifier
                    });
                }

                ViewData["EmbeddedCalculator"] = true;
                ViewData["CurrentUser"] = currentUser;
                ViewData["StoredScenarios"] = new List<ScenarioStoredDto> { embeddedScenario };
                ViewData["ScenarioGroups"] = Array.Empty<ScenarioGroupStoredDto>();
                ViewData["ProposalHistoryByGroup"] =
                    new Dictionary<string, IReadOnlyList<ProposalExportHistoryItemDto>>(
                        StringComparer.OrdinalIgnoreCase);
                ViewData["CrmCalculatorSyncEnabled"] = _crmCalculatorSyncEnabled;
                return View("Index");
            }
            catch (Exception ex) when (IsScenarioAccessException(ex))
            {
                return BuildScenarioAccessError(ex);
            }
        }

        var storedScenarios = (await _dataverse.GetScenariosForUserAsync(ct)).ToList();
        ScenarioStoredDto? contextualScenario = null;

        if (_crmCalculatorSyncEnabled
            && !string.IsNullOrWhiteSpace(scenarioId)
            && !string.IsNullOrWhiteSpace(crmDealId)
            && AppModuleAccessPolicy.CanAccess(AppModule.Crm, currentUser))
        {
            try
            {
                var requestedScenario = await FindAuthorizedScenarioAsync(
                    scenarioId,
                    crmDealId,
                    currentUser,
                    ct);
                if (requestedScenario is not null)
                {
                    contextualScenario = requestedScenario;
                    var existingIndex = storedScenarios.FindIndex(item =>
                        string.Equals(
                            item.ScenarioId?.Trim(),
                            requestedScenario.ScenarioId?.Trim(),
                            StringComparison.OrdinalIgnoreCase));
                    if (existingIndex >= 0)
                        storedScenarios[existingIndex] = requestedScenario;
                    else
                        storedScenarios.Add(requestedScenario);
                }
            }
            catch (Exception ex) when (IsScenarioAccessException(ex))
            {
                return BuildScenarioAccessError(ex);
            }
        }

        var proposalHistory = (await _dataverse.GetProposalHistoryForUserAsync(ct))
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
        if (contextualScenario is not null)
        {
            try
            {
                var contextualGroupId = FirstNonEmpty(
                    contextualScenario.GroupId,
                    contextualScenario.ScenarioId);
                var groupScenarios = await _dataverse.GetScenariosByGroupIdAsync(contextualGroupId, ct);
                if (groupScenarios.Any(item => !string.Equals(
                        item.OwnerSystemUserId,
                        contextualScenario.OwnerSystemUserId,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ScenarioPersistenceConflictException(
                        "Los escenarios del negocio no comparten el mismo propietario.");
                }
                proposalHistory[contextualGroupId] =
                    await _dataverse.GetProposalHistoryAsync(contextualGroupId, ct);
            }
            catch (Exception ex) when (IsScenarioAccessException(ex))
            {
                return BuildScenarioAccessError(ex);
            }
        }
        var groups = BuildScenarioGroups(storedScenarios, proposalHistory);

        if (embedded)
        {
            ViewData["EmbeddedCalculator"] = true;
            if (!Request.Query.ContainsKey("newCrmOpportunity"))
            {
                storedScenarios = string.IsNullOrWhiteSpace(scenarioId)
                    ? []
                    : storedScenarios
                        .Where(item => string.Equals(item.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                        .ToList();
            }
        }

        ViewData["CurrentUser"] = currentUser;
        ViewData["StoredScenarios"] = storedScenarios;
        ViewData["ScenarioGroups"] = groups;
        ViewData["ProposalHistoryByGroup"] = proposalHistory;
        ViewData["CrmCalculatorSyncEnabled"] = _crmCalculatorSyncEnabled;
        return embedded ? View("Index") : View("Workspace");
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Proposal(
        [FromQuery] string? scenarioId,
        [FromQuery] string? crmDealId,
        CancellationToken ct,
        [FromQuery] string? groupId = null)
    {
        if (string.IsNullOrWhiteSpace(groupId) && string.IsNullOrWhiteSpace(scenarioId))
        {
            return BadRequest(new
            {
                message = "Selecciona, calcula y guarda un escenario antes de generar la propuesta.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        var currentUser = await _dataverse.GetCurrentUserAsync(ct);
        ScenarioStoredDto? accessScenario;
        try
        {
            accessScenario = !string.IsNullOrWhiteSpace(scenarioId)
                ? (_crmCalculatorSyncEnabled
                    ? await FindAuthorizedScenarioAsync(scenarioId, crmDealId, currentUser, ct)
                    : await FindCurrentUserScenarioAsync(scenarioId, ct))
                : null;
        }
        catch (Exception ex) when (IsScenarioAccessException(ex))
        {
            return BuildScenarioAccessError(ex);
        }

        if (!string.IsNullOrWhiteSpace(scenarioId) && accessScenario is null)
        {
            return NotFound(new
            {
                message = "El escenario solicitado no existe o no está disponible para tu usuario.",
                scenarioId,
                traceId = HttpContext.TraceIdentifier
            });
        }

        var normalizedGroupId = FirstNonEmpty(groupId, accessScenario?.GroupId, accessScenario?.ScenarioId).Trim();
        if (accessScenario is not null
            && !string.Equals(
                FirstNonEmpty(accessScenario.GroupId, accessScenario.ScenarioId),
                normalizedGroupId,
                StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new
            {
                message = "El escenario seleccionado no pertenece al negocio solicitado.",
                traceId = HttpContext.TraceIdentifier
            });
        }
        IReadOnlyList<ScenarioStoredDto> authorizedScenarios;
        try
        {
            // Autoriza el grupo completo antes de excluir posibilidades de la propuesta.
            // Así una fila ajena no puede quedar oculta por IncludeInProposal y abrir acceso
            // al historial o a la configuración compartida del grupo.
            authorizedScenarios = await _dataverse.GetScenariosByGroupIdAsync(normalizedGroupId, ct);
            if (authorizedScenarios.Count == 0)
                throw new ScenarioPersistenceNotFoundException("El escenario no existe o no está disponible.");
            var authorizedOwnerId = accessScenario?.OwnerSystemUserId
                ?? currentUser?.SystemUserId
                ?? "";
            if (string.IsNullOrWhiteSpace(authorizedOwnerId))
                throw new CrmAccessDeniedException("No fue posible validar el usuario actual.");
            if (authorizedScenarios.Any(item => !string.Equals(
                    item.OwnerSystemUserId,
                    authorizedOwnerId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                if (accessScenario is null)
                    throw new CrmAccessDeniedException("El negocio no pertenece al usuario actual.");
                throw new ScenarioPersistenceConflictException(
                    "Los escenarios del negocio no comparten el mismo propietario.");
            }
        }
        catch (Exception ex) when (IsScenarioAccessException(ex))
        {
            return BuildScenarioAccessError(ex);
        }

        var scenarios = authorizedScenarios
            .Where(item => item.IncludeInProposal)
            .OrderBy(item => item.PossibilityOrder)
            .ToList();
        if (scenarios.Count == 0)
        {
            return NotFound(new
            {
                message = "El negocio no contiene escenarios disponibles para la propuesta.",
                groupId = normalizedGroupId,
                traceId = HttpContext.TraceIdentifier
            });
        }
        if (scenarios.Count > 3)
            return Conflict(new { message = "El negocio supera el máximo de tres escenarios.", traceId = HttpContext.TraceIdentifier });

        if (accessScenario is null)
        {
            accessScenario = scenarios[0];
        }

        try
        {
            var possibilityModels = new List<CalculatorProposalPossibilityViewModel>(scenarios.Count);
            foreach (var scenario in scenarios)
            {
                if (scenario.LastResult is null)
                    throw new InvalidOperationException($"Calcula el escenario '{scenario.PossibilityName}' antes de generar la propuesta.");
                var currentHash = ScenarioInputHasher.Compute(scenario);
                if (!string.IsNullOrWhiteSpace(scenario.LastResult.InputHash)
                    && !string.Equals(scenario.LastResult.InputHash, currentHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"El escenario '{scenario.PossibilityName}' cambió después del último cálculo.");
                }
                _ = RecalculateStoredScenario(scenario);
                var possibilityLines = scenario.Lines.Select(BuildProposalLine).ToList();
                possibilityModels.Add(new CalculatorProposalPossibilityViewModel
                {
                    ScenarioId = scenario.ScenarioId,
                    Title = FirstNonEmpty(scenario.PossibilityName, scenario.ScenarioName, $"Escenario {scenario.PossibilityOrder}"),
                    Order = scenario.PossibilityOrder,
                    IsRecommended = scenario.IsRecommended,
                    TotalMonthlySale = Round2(possibilityLines.Sum(line => line.MonthlySale)),
                    TotalContractSale = Round2(possibilityLines.Sum(line => line.ContractSale)),
                    TotalMonthlyVat = Round2(possibilityLines.Sum(line => line.MonthlyVat)),
                    TotalContractVat = Round2(possibilityLines.Sum(line => line.ContractVat)),
                    Lines = possibilityLines
                });
            }

            var primary = possibilityModels.FirstOrDefault(item => item.IsRecommended) ?? possibilityModels[0];
            var history = await _dataverse.GetProposalHistoryAsync(normalizedGroupId, ct);
            var latestConfiguration = await _dataverse.GetLatestProposalConfigurationAsync(normalizedGroupId, ct);
            var model = new CalculatorProposalViewModel
            {
                GroupId = normalizedGroupId,
                GroupName = FirstNonEmpty(scenarios[0].GroupName, scenarios[0].ScenarioName, "Negocio"),
                EconomicHash = BuildGroupEconomicHash(scenarios),
                LatestConfigurationJson = latestConfiguration?.ConfigurationJson ?? "",
                ScenarioId = accessScenario.ScenarioId,
                CrmDealId = accessScenario.CrmDealId,
                ScenarioName = FirstNonEmpty(scenarios[0].GroupName, scenarios[0].ScenarioName, "Negocio"),
                PreparedByName = currentUser?.DisplayName?.Trim() ?? "",
                PreparedByEmail = currentUser?.Email?.Trim() ?? "",
                TotalMonthlySale = primary.TotalMonthlySale,
                TotalContractSale = primary.TotalContractSale,
                TotalMonthlyVat = primary.TotalMonthlyVat,
                TotalContractVat = primary.TotalContractVat,
                Lines = primary.Lines,
                Possibilities = possibilityModels,
                ExportHistory = history
            };
            return View("Proposal", model);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message,
                groupId = normalizedGroupId,
                traceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ProductSearch([FromQuery] string q, CancellationToken ct)
    {
        var items = await _dataverse.SearchProductsAsync(q, top: 12, ct: ct);
        return Json(items);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ClientSearch([FromQuery] string q, CancellationToken ct)
    {
        var items = await _dataverse.SearchClientsAsync(q, top: 12, ct: ct);
        return Json(items);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ClientRenewalDates([FromQuery] string clientId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return BadRequest(new
            {
                message = "Debes seleccionar un cliente valido.",
                detail = "El parametro clientId llego vacio.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        try
        {
            var items = await _dataverse.SearchRenewalDatesByClientAsync(clientId, top: 250, ct: ct);
            return Json(items);
        }
        catch (InvalidOperationException ex)
        {
            var traceId = HttpContext.TraceIdentifier;

            _logger.LogError(
                ex,
                "Error consultando fechas de renovacion para cliente {ClientId}. TraceId: {TraceId}.",
                clientId,
                traceId);

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "No se pudieron consultar las fechas de renovacion.",
                detail = BuildDiagnosticMessage(ex),
                traceId
            });
        }
        catch (Exception ex)
        {
            var traceId = HttpContext.TraceIdentifier;
            var detail = CompactDiagnosticMessage(ex.Message);

            _logger.LogError(
                ex,
                "Error inesperado consultando fechas de renovacion para cliente {ClientId}. TraceId: {TraceId}.",
                clientId,
                traceId);

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Ocurrio un error inesperado consultando las fechas de renovacion.",
                detail = string.IsNullOrWhiteSpace(detail)
                    ? "No se recibio detalle adicional del servidor."
                    : detail,
                traceId
            });
        }
    }

    [HttpPost]
    public IActionResult Calculate([FromBody] QuoteScenarioInput input)
    {
        if (input is null)
            return BadRequest("Payload invÃ¡lido.");

        NormalizeProrationRules(input);

        var licenseValidation = ValidateLicenseCaps(input);
        if (!string.IsNullOrWhiteSpace(licenseValidation))
            return BadRequest(licenseValidation);

        var result = _calculator.Calculate(input);

        return Json(new
        {
            inputHash = ScenarioInputHasher.Compute(input),
            points = result.Points,
            commission = result.Commission,
            prorationDays = result.ProrationDays,
            prorationFactor = result.ProrationFactor,
            totalMonthlySale = result.TotalMonthlySale,
            totalSale = result.TotalSale
        });
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveScenario([FromBody] ScenarioSaveRequest input, CancellationToken ct)
    {
        if (input is null)
            return BadRequest("Payload invÃ¡lido.");

        NormalizeProrationRules(input);

        if (string.IsNullOrWhiteSpace(input.ScenarioId))
            return BadRequest("ScenarioId requerido.");

        if (input.LastResult is not null)
        {
            try
            {
                input.LastResult = BuildAuthoritativeScenarioSnapshot(input);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new
                {
                    message = ex.Message,
                    traceId = HttpContext.TraceIdentifier
                });
            }
        }

        try
        {
            var currentUser = await _dataverse.GetCurrentUserAsync(ct);
            if (_crmCalculatorSyncEnabled
                && !string.IsNullOrWhiteSpace(input.CrmDealId)
                && AppModuleAccessPolicy.CanAccess(AppModule.Crm, currentUser))
            {
                var scenario = await FindAuthorizedScenarioAsync(
                    input.ScenarioId,
                    input.CrmDealId,
                    currentUser,
                    ct);
                if (scenario is null)
                {
                    return NotFound(new
                    {
                        message = "El escenario asociado al negocio ya no existe.",
                        scenarioId = input.ScenarioId,
                        traceId = HttpContext.TraceIdentifier
                    });
                }

                if (scenario.IsCrmSharedAccess)
                {
                    // El acceso compartido por CRM conserva el contrato de actualización
                    // existente: la jerarquía solo puede administrarla su propietario.
                    var sharedUpdate = await _dataverse.UpdateScenarioByIdAuthorizedAsync(
                        input,
                        scenario.OwnerSystemUserId,
                        ct);
                    return Ok(sharedUpdate);
                }

                var updated = await _dataverse.SaveScenarioV2Async(input, updateOnly: true, ct);
                return Ok(updated);
            }

            var saved = await _dataverse.SaveScenarioV2Async(input, updateOnly: false, ct);
            return Ok(saved);
        }
        catch (Exception ex) when (IsScenarioAccessException(ex))
        {
            return BuildScenarioAccessError(ex);
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePossibility(
        [FromBody] ScenarioPossibilityCreateRequest? request,
        CancellationToken ct)
    {
        request ??= new ScenarioPossibilityCreateRequest();
        try
        {
            var currentUser = await _dataverse.GetCurrentUserAsync(ct);
            if (currentUser is null || string.IsNullOrWhiteSpace(currentUser.SystemUserId))
                return Forbid();

            var groupId = request.GroupId?.Trim() ?? "";
            var isNewGroup = string.IsNullOrWhiteSpace(groupId);
            var existing = isNewGroup
                ? new List<ScenarioStoredDto>()
                : (await _dataverse.GetScenariosByGroupIdAsync(groupId, ct))
                    .OrderBy(item => item.PossibilityOrder)
                    .ToList();

            if (existing.Any(item => !string.Equals(
                    item.OwnerSystemUserId,
                    currentUser.SystemUserId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return Forbid();
            }
            if (existing.Count >= 3)
                return Conflict(new { message = "Un negocio admite máximo tres escenarios.", traceId = HttpContext.TraceIdentifier });

            groupId = isNewGroup ? Guid.NewGuid().ToString("D") : groupId;
            var occupied = existing.Select(item => item.PossibilityOrder).ToHashSet();
            var order = Enumerable.Range(1, 3).First(value => !occupied.Contains(value));
            var source = existing.FirstOrDefault(item => string.Equals(
                    item.ScenarioId,
                    request.SourceScenarioId?.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                ?? (request.DuplicateSource ? existing.FirstOrDefault() : null);

            if (!string.IsNullOrWhiteSpace(request.SourceScenarioId) && source is null)
            {
                return NotFound(new
                {
                    message = "El escenario que deseas duplicar no pertenece a este negocio.",
                    traceId = HttpContext.TraceIdentifier
                });
            }

            var groupName = isNewGroup
                ? FirstNonEmpty(request.Name, $"Negocio {DateTime.Now:dd/MM/yyyy HH:mm}")
                : FirstNonEmpty(existing[0].GroupName, existing[0].ScenarioName, "Negocio");
            var possibilityName = isNewGroup
                ? "Escenario 1"
                : FirstNonEmpty(request.Name, $"Escenario {order}");
            var save = new ScenarioSaveRequest
            {
                ScenarioId = Guid.NewGuid().ToString("D"),
                GroupId = groupId,
                GroupName = groupName,
                PossibilityName = possibilityName,
                ScenarioName = possibilityName,
                PossibilityOrder = order,
                IncludeInProposal = true,
                IsRecommended = existing.Count == 0,
                DealType = source?.DealType ?? (int)DealType.ClienteNuevo,
                RequiresProration = source?.RequiresProration ?? false,
                StartDate = ParseScenarioDate(source?.StartDate),
                EndDate = ParseScenarioDate(source?.EndDate),
                Lines = source is null || !request.DuplicateSource
                    ? []
                    : source.Lines.Select((line, index) => new ScenarioLineInput
                    {
                        LineId = Guid.NewGuid().ToString("D"),
                        LineOrder = index + 1,
                        BusinessType = line.BusinessType,
                        ProductId = line.ProductId,
                        ProductDescription = line.ProductDescription,
                        CostUnit = line.CostUnit,
                        MarginPercent = line.MarginPercent,
                        ContractMonths = line.ContractMonths,
                        Quantity = line.Quantity,
                        SuggestedRetailPrice = line.SuggestedRetailPrice,
                        Acelerador = line.Acelerador,
                        HasVat = line.HasVat
                    }).ToList(),
                // El duplicado conserva los insumos, pero nace pendiente de cálculo. Reutilizar
                // el snapshot persistido del origen mezcla el resultado con una nueva jerarquía
                // de líneas y puede hacer que Dataverse rechace el cambio atómico.
                LastResult = null
            };

            var saved = await _dataverse.SaveScenarioV2Async(save, updateOnly: false, ct)
                ?? throw new InvalidOperationException("Dataverse no devolvió el escenario creado.");
            return Ok(saved);
        }
        catch (Exception ex) when (IsScenarioAccessException(ex))
        {
            return BuildScenarioAccessError(ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "No fue posible crear el escenario {SourceScenarioId} en el negocio {GroupId}. TraceId: {TraceId}",
                request.SourceScenarioId,
                request.GroupId,
                HttpContext.TraceIdentifier);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    message = "No fue posible duplicar el escenario. Intenta nuevamente.",
                    traceId = HttpContext.TraceIdentifier
                });
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameScenarioGroup(
        [FromBody] ScenarioGroupRenameRequest? request,
        CancellationToken ct)
    {
        var groupId = request?.GroupId?.Trim() ?? "";
        var groupName = request?.GroupName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(groupId))
            return BadRequest(new { message = "Selecciona un negocio válido.", traceId = HttpContext.TraceIdentifier });
        if (groupId.Length > 100)
            return BadRequest(new { message = "El identificador del negocio admite máximo 100 caracteres.", traceId = HttpContext.TraceIdentifier });
        if (string.IsNullOrWhiteSpace(groupName))
            return BadRequest(new { message = "El nombre del negocio es obligatorio.", traceId = HttpContext.TraceIdentifier });
        if (groupName.Length > 200)
            return BadRequest(new { message = "El nombre del negocio admite máximo 200 caracteres.", traceId = HttpContext.TraceIdentifier });

        try
        {
            var changed = await _dataverse.RenameScenarioGroupAsync(groupId, groupName, ct);
            if (!changed)
                return NotFound(new { message = "El negocio no existe o no está disponible para tu usuario.", traceId = HttpContext.TraceIdentifier });
            return Ok(new { ok = true, groupId, groupName });
        }
        catch (Exception ex) when (IsScenarioAccessException(ex))
        {
            return BuildScenarioAccessError(ex);
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecommendPossibility(
        [FromBody] ScenarioPossibilityRecommendationRequest? request,
        CancellationToken ct)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.GroupId)
            || string.IsNullOrWhiteSpace(request.ScenarioId))
        {
            return BadRequest(new { message = "Selecciona un escenario válido.", traceId = HttpContext.TraceIdentifier });
        }

        try
        {
            var currentUser = await _dataverse.GetCurrentUserAsync(ct);
            var scenarios = (await _dataverse.GetScenariosByGroupIdAsync(request.GroupId.Trim(), ct))
                .OrderBy(item => item.PossibilityOrder)
                .ToList();
            if (scenarios.Count == 0)
                return NotFound(new { message = "El negocio no existe.", traceId = HttpContext.TraceIdentifier });
            if (currentUser is null || scenarios.Any(item => !string.Equals(
                    item.OwnerSystemUserId,
                    currentUser.SystemUserId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return Forbid();
            }
            if (!scenarios.Any(item => string.Equals(item.ScenarioId, request.ScenarioId.Trim(), StringComparison.OrdinalIgnoreCase)))
                return NotFound(new { message = "El escenario no pertenece al negocio.", traceId = HttpContext.TraceIdentifier });

            var changed = await _dataverse.RecommendScenarioPossibilityAsync(
                request.GroupId.Trim(),
                request.ScenarioId.Trim(),
                ct);
            if (!changed)
                return NotFound(new { message = "El escenario no pertenece al negocio.", traceId = HttpContext.TraceIdentifier });
            return Ok(new { ok = true });
        }
        catch (Exception ex) when (IsScenarioAccessException(ex))
        {
            return BuildScenarioAccessError(ex);
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [ModuleAuthorize(AppModule.Crm)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendToCrm(
        [FromBody] CrmDealFromCalculatorRequest? request,
        CancellationToken ct)
    {
        if (!_crmCalculatorSyncEnabled)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Sincronización con CRM temporalmente no disponible",
                Detail = "Disponible cuando el CRM se publique para todos.",
                Instance = HttpContext.Request.Path
            };
            problem.Extensions["traceId"] = HttpContext.TraceIdentifier;

            var result = new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
            result.ContentTypes.Add("application/problem+json");
            return result;
        }

        if (request is null)
            ModelState.AddModelError("", "Envía los datos del registro comercial.");
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var currentUser = await _dataverse.GetCurrentUserAsync(ct);
        ScenarioStoredDto? scenario;
        try
        {
            scenario = await FindAuthorizedScenarioAsync(
                request!.ScenarioId,
                request.DealId,
                currentUser,
                ct);
        }
        catch (Exception ex) when (IsScenarioAccessException(ex))
        {
            return BuildScenarioAccessError(ex);
        }

        if (scenario is null)
        {
            return NotFound(new
            {
                message = "El escenario solicitado no existe o no está disponible para tu usuario.",
                scenarioId = request.ScenarioId,
                traceId = HttpContext.TraceIdentifier
            });
        }

        QuoteScenarioResult? authoritativeResult = null;
        if (request.Kind == CrmDealKind.QuotedBusiness)
        {
            if (scenario.LastResult is null)
            {
                return BadRequest(new
                {
                    message = "Calcula y guarda el escenario antes de crear un negocio cotizado.",
                    scenarioId = scenario.ScenarioId,
                    traceId = HttpContext.TraceIdentifier
                });
            }

            try
            {
                authoritativeResult = RecalculateStoredScenario(scenario);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new
                {
                    message = ex.Message,
                    scenarioId = scenario.ScenarioId,
                    traceId = HttpContext.TraceIdentifier
                });
            }
        }

        var command = new CrmCalculatorDealUpsertCommand
        {
            DealId = request.DealId?.Trim() ?? "",
            ScenarioId = scenario.ScenarioId,
            CompanyId = request.CompanyId?.Trim() ?? "",
            PrimaryContactId = request.PrimaryContactId?.Trim() ?? "",
            Name = request.Name?.Trim() ?? "",
            Kind = request.Kind,
            EstimatedValue = request.Kind == CrmDealKind.EstimatedOpportunity
                ? request.EstimatedValue
                : 0m,
            Probability = request.Probability,
            ExpectedCloseDate = request.ExpectedCloseDate,
            NextAction = request.NextAction?.Trim() ?? "",
            NextActionAtUtc = request.NextActionAtUtc,
            BusinessLine = request.BusinessLine?.Trim() ?? "",
            ApplyCommercialFields = true,
            Score = authoritativeResult?.Points,
            ContractValue = authoritativeResult?.TotalSale
        };

        try
        {
            var accessScope = BuildCalculatorCrmAccessScope(currentUser);
            var saved = await _crmRepository.UpsertDealFromCalculatorAsync(
                command,
                accessScope,
                ct);
            return Ok(new
            {
                ok = true,
                message = request.Kind == CrmDealKind.QuotedBusiness
                    ? "Negocio cotizado sincronizado con el CRM."
                    : "Oportunidad estimada sincronizada con el CRM.",
                deal = saved,
                canMarkWon = saved.CanMarkWon,
                traceId = HttpContext.TraceIdentifier
            });
        }
        catch (CrmValidationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message,
                traceId = HttpContext.TraceIdentifier
            });
        }
        catch (CrmNotFoundException ex)
        {
            return NotFound(new
            {
                message = ex.Message,
                traceId = HttpContext.TraceIdentifier
            });
        }
        catch (CrmConflictException ex)
        {
            return Conflict(new
            {
                message = ex.Message,
                traceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "No fue posible sincronizar el escenario {ScenarioId} con CRM. TraceId: {TraceId}.",
                scenario.ScenarioId,
                HttpContext.TraceIdentifier);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "No fue posible sincronizar el escenario con el CRM.",
                traceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpDelete]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteScenario([FromQuery] string scenarioId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
            return BadRequest("ScenarioId requerido.");

        var normalizedScenarioId = scenarioId.Trim();
        if (_crmCalculatorSyncEnabled)
        {
            CrmDealSummary? linkedDeal;
            try
            {
                linkedDeal = await _crmRepository.GetDealByScenarioIdAsync(
                    normalizedScenarioId,
                    ct);
            }
            catch (CrmConflictException ex)
            {
                return BuildScenarioAccessError(ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "No fue posible verificar el vínculo CRM antes de eliminar el escenario {ScenarioId}. TraceId: {TraceId}.",
                    normalizedScenarioId,
                    HttpContext.TraceIdentifier);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "No fue posible verificar si el escenario está vinculado al CRM. No se eliminó ningún registro.",
                    scenarioId = normalizedScenarioId,
                    traceId = HttpContext.TraceIdentifier
                });
            }

            if (linkedDeal is not null)
            {
                return Conflict(new
                {
                    message = "El escenario está vinculado a un negocio CRM y no se puede eliminar.",
                    scenarioId = normalizedScenarioId,
                    crmDealId = linkedDeal.Id,
                    traceId = HttpContext.TraceIdentifier
                });
            }
        }

        try
        {
            var deleted = await _dataverse.DeleteScenarioAsync(normalizedScenarioId, ct);
            if (!deleted)
            {
                return NotFound(new
                {
                    message = "El escenario no existe o no pertenece a tu usuario.",
                    scenarioId = normalizedScenarioId,
                    traceId = HttpContext.TraceIdentifier
                });
            }

            return Ok(new { ok = true });
        }
        catch (ScenarioPersistenceConflictException ex)
        {
            return BuildScenarioAccessError(ex);
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ProposalHistory([FromQuery] string groupId, CancellationToken ct)
    {
        try
        {
            _ = await GetOwnedScenarioGroupAsync(groupId, ct);
            var history = await _dataverse.GetProposalHistoryAsync(groupId.Trim(), ct);
            return Json(history);
        }
        catch (Exception ex) when (IsScenarioAccessException(ex))
        {
            return BuildScenarioAccessError(ex);
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ProposalExport(
        [FromQuery] string groupId,
        [FromQuery] string exportId,
        CancellationToken ct,
        [FromQuery] string? scenarioId = null,
        [FromQuery] string? crmDealId = null)
    {
        if (string.IsNullOrWhiteSpace(exportId))
            return BadRequest(new { message = "ExportId requerido.", traceId = HttpContext.TraceIdentifier });
        try
        {
            _ = await GetAuthorizedScenarioGroupAsync(groupId, scenarioId, crmDealId, ct);
            var history = await _dataverse.GetProposalHistoryAsync(groupId.Trim(), ct);
            if (!history.Any(item => string.Equals(item.ExportId, exportId.Trim(), StringComparison.OrdinalIgnoreCase)))
                return NotFound(new { message = "La exportación no pertenece al escenario.", traceId = HttpContext.TraceIdentifier });
            var download = await _dataverse.DownloadProposalExportAsync(exportId.Trim(), ct);
            if (download is null)
                return NotFound(new { message = "El PDF exportado no está disponible.", traceId = HttpContext.TraceIdentifier });
            return File(download.Content, download.ContentType, download.FileName);
        }
        catch (Exception ex) when (IsScenarioAccessException(ex))
        {
            return BuildScenarioAccessError(ex);
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> ProposalExports(
        [FromForm] string groupId,
        [FromForm] string economicHash,
        [FromForm] string idempotencyKey,
        [FromForm] string configurationJson,
        [FromForm] string fileName,
        [FromForm] IFormFile? pdf,
        CancellationToken ct,
        [FromForm] string? scenarioId = null,
        [FromForm] string? crmDealId = null)
    {
        if (pdf is null || pdf.Length is <= 0 or > 10 * 1024 * 1024)
            return BadRequest(new { message = "El PDF debe tener un tamaño entre 1 byte y 10 MB.", traceId = HttpContext.TraceIdentifier });
        if (!string.Equals(pdf.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "El archivo exportado debe ser un PDF.", traceId = HttpContext.TraceIdentifier });
        if (!Guid.TryParse(idempotencyKey, out _))
            return BadRequest(new { message = "La llave de exportación no es válida.", traceId = HttpContext.TraceIdentifier });
        if (Encoding.UTF8.GetByteCount(configurationJson ?? "") > 128 * 1024)
            return BadRequest(new { message = "La configuración de propuesta supera 128 KB.", traceId = HttpContext.TraceIdentifier });

        try
        {
            var scenarios = (await GetAuthorizedScenarioGroupAsync(groupId, scenarioId, crmDealId, ct))
                .Where(item => item.IncludeInProposal)
                .OrderBy(item => item.PossibilityOrder)
                .ToList();
            if (scenarios.Count is < 1 or > 3)
                return Conflict(new { message = "La propuesta debe incluir entre uno y tres escenarios.", traceId = HttpContext.TraceIdentifier });

            foreach (var scenario in scenarios)
            {
                if (scenario.LastResult is null)
                    return Conflict(new { message = $"Calcula el escenario '{scenario.PossibilityName}' antes de exportar.", traceId = HttpContext.TraceIdentifier });
                var inputHash = ScenarioInputHasher.Compute(scenario);
                if (!string.IsNullOrWhiteSpace(scenario.LastResult.InputHash)
                    && !string.Equals(scenario.LastResult.InputHash, inputHash, StringComparison.OrdinalIgnoreCase))
                    return Conflict(new { message = $"El escenario '{scenario.PossibilityName}' cambió después de calcularse.", traceId = HttpContext.TraceIdentifier });
                _ = RecalculateStoredScenario(scenario);
            }

            var authoritativeHash = BuildGroupEconomicHash(scenarios);
            if (!string.Equals(authoritativeHash, economicHash?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(new
                {
                    message = "Los valores del escenario cambiaron. Recarga la propuesta antes de exportar.",
                    traceId = HttpContext.TraceIdentifier
                });
            }

            using var configurationDocument = JsonDocument.Parse(string.IsNullOrWhiteSpace(configurationJson) ? "{}" : configurationJson);
            var publicPossibilities = scenarios.Select(scenario =>
            {
                var lines = scenario.Lines.Select(BuildProposalLine).ToList();
                return new
                {
                    scenarioId = scenario.ScenarioId,
                    title = FirstNonEmpty(scenario.PossibilityName, scenario.ScenarioName, $"Escenario {scenario.PossibilityOrder}"),
                    order = scenario.PossibilityOrder,
                    isRecommended = scenario.IsRecommended,
                    totalMonthlySale = Round2(lines.Sum(line => line.MonthlySale)),
                    totalContractSale = Round2(lines.Sum(line => line.ContractSale)),
                    totalMonthlyVat = Round2(lines.Sum(line => line.MonthlyVat)),
                    totalContractVat = Round2(lines.Sum(line => line.ContractVat)),
                    lines
                };
            }).ToList();
            var snapshotJson = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                groupId = groupId.Trim(),
                economicHash = authoritativeHash,
                configuration = configurationDocument.RootElement,
                possibilities = publicPossibilities
            });
            if (Encoding.UTF8.GetByteCount(snapshotJson) > 512 * 1024)
            {
                return BadRequest(new
                {
                    message = "La propuesta completa supera el límite de 512 KB de configuración. Reduce los textos descriptivos antes de exportar.",
                    traceId = HttpContext.TraceIdentifier
                });
            }

            await using var stream = new MemoryStream((int)pdf.Length);
            await pdf.CopyToAsync(stream, ct);
            var bytes = stream.ToArray();
            if (bytes.Length < 5 || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-")
                return BadRequest(new { message = "El archivo generado no tiene una cabecera PDF válida.", traceId = HttpContext.TraceIdentifier });

            var result = await _dataverse.SaveProposalExportAsync(new ProposalExportSaveRequest
            {
                GroupId = groupId.Trim(),
                OwnerSystemUserId = scenarios[0].OwnerSystemUserId,
                IdempotencyKey = idempotencyKey.Trim(),
                EconomicHash = authoritativeHash,
                ConfigurationJson = snapshotJson,
                FileName = fileName,
                PdfContent = bytes,
                PossibilityCount = scenarios.Count
            }, ct);
            return Ok(new
            {
                ok = true,
                exportId = result.Export.ExportId,
                version = result.Export.Version,
                fileName = result.Export.FileName,
                exportedAtUtc = result.Export.ExportedAtUtc,
                alreadyExisted = result.AlreadyExisted,
                downloadUrl = Url.Action(nameof(ProposalExport), new
                {
                    groupId = groupId.Trim(),
                    exportId = result.Export.ExportId,
                    scenarioId = string.IsNullOrWhiteSpace(scenarioId)
                        ? scenarios[0].ScenarioId
                        : scenarioId.Trim(),
                    crmDealId = string.IsNullOrWhiteSpace(crmDealId) ? null : crmDealId.Trim()
                })
            });
        }
        catch (JsonException)
        {
            return BadRequest(new { message = "La configuración de propuesta no contiene JSON válido.", traceId = HttpContext.TraceIdentifier });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message, traceId = HttpContext.TraceIdentifier });
        }
        catch (Exception ex) when (IsScenarioAccessException(ex))
        {
            return BuildScenarioAccessError(ex);
        }
    }

    [HttpPost]
    public IActionResult Export([FromBody] QuoteScenarioInput input)
    {
        if (input is null)
            return BadRequest("Payload invÃ¡lido.");

        NormalizeProrationRules(input);

        if (input.Lines is null || input.Lines.Count == 0)
            return BadRequest("No hay lÃ­neas para exportar.");

        var licenseValidation = ValidateLicenseCaps(input);
        if (!string.IsNullOrWhiteSpace(licenseValidation))
            return BadRequest(licenseValidation);

        var productValidation = ValidateSelectedProducts(input.Lines, "exportar el Excel");
        if (!string.IsNullOrWhiteSpace(productValidation))
            return BadRequest(productValidation);

        var fileName = BuildFileName(input.ScenarioName);
        using var workbook = BuildWorkbook(input);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    [HttpPost]
    public IActionResult ValidateProvisioning([FromBody] ProvisioningRequestInput? input)
    {
        if (input is null)
            return BadRequest("Payload invÃƒÂ¡lido.");

        var validationError = ValidateProvisioningPayload(input);
        if (!string.IsNullOrWhiteSpace(validationError))
            return BadRequest(validationError);

        return Ok(new { ok = true });
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SubmitProvisioning([FromBody] ProvisioningRequestInput? input, CancellationToken ct)
    {
        if (input is null)
            return BadRequest("Payload invÃƒÂ¡lido.");

        var validationError = ValidateProvisioningPayload(input);
        if (!string.IsNullOrWhiteSpace(validationError))
            return BadRequest(validationError);

        if (string.IsNullOrWhiteSpace(_calculatorOptions.ProvisioningRequestFlowUrl))
        {
            return BadRequest("Configura la URL del flujo en Calculator:ProvisioningRequestFlowUrl antes de enviar la solicitud.");
        }

        var currentUser = await _dataverse.GetCurrentUserAsync(ct);
        ScenarioStoredDto? scenario;
        try
        {
            scenario = _crmCalculatorSyncEnabled
                ? await FindAuthorizedScenarioAsync(
                    input.BusinessId,
                    input.CrmDealId,
                    currentUser,
                    ct)
                : await FindCurrentUserScenarioAsync(input.BusinessId, ct);
        }
        catch (Exception ex) when (IsScenarioAccessException(ex))
        {
            return BuildScenarioAccessError(ex);
        }

        if (scenario is null)
        {
            return NotFound(new
            {
                message = "El escenario de la solicitud no existe o no está disponible para tu usuario.",
                scenarioId = input.BusinessId?.Trim() ?? "",
                traceId = HttpContext.TraceIdentifier
            });
        }

        if (scenario.LastResult is null)
        {
            return BadRequest(new
            {
                message = "Calcula y guarda el escenario antes de solicitar el aprovisionamiento.",
                scenarioId = scenario.ScenarioId,
                traceId = HttpContext.TraceIdentifier
            });
        }

        QuoteScenarioResult authoritativeResult;
        try
        {
            authoritativeResult = RecalculateStoredScenario(scenario);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message,
                scenarioId = scenario.ScenarioId,
                traceId = HttpContext.TraceIdentifier
            });
        }

        input.BusinessId = scenario.ScenarioId;
        input.Scenario = BuildProvisioningScenarioContext(scenario);
        input.Resultado = BuildProvisioningResult(authoritativeResult);

        try
        {
            await EnsureHardwareProductsForProvisioningAsync(input, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(BuildDiagnosticMessage(ex));
        }

        var requestId = Guid.NewGuid().ToString("N");
        var payload = BuildProvisioningFlowPayload(input, requestId);
        var client = _httpClientFactory.CreateClient();
        try
        {
            using var response = await client.PostAsJsonAsync(_calculatorOptions.ProvisioningRequestFlowUrl, payload, cancellationToken: ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var message = string.IsNullOrWhiteSpace(body)
                    ? $"El flujo respondiÃ³ con error HTTP {(int)response.StatusCode}."
                    : body;
                return BadRequest(message);
            }
        }
        catch (Exception ex)
        {
            var message = BuildDiagnosticMessage(ex);
            return BadRequest(message);
        }

        if (!_crmCalculatorSyncEnabled
            || !AppModuleAccessPolicy.CanAccess(AppModule.Crm, currentUser))
        {
            return Ok(new
            {
                ok = true,
                requestId,
                crmDealId = "",
                crmSynchronized = false,
                canMarkWon = false,
                deal = (CrmDealSummary?)null,
                message = "Solicitud enviada a aprobación."
            });
        }

        CrmDealSummary? markedDeal;
        try
        {
            var accessScope = BuildCalculatorCrmAccessScope(currentUser);
            _ = await _crmRepository.UpsertDealFromCalculatorAsync(
                new CrmCalculatorDealUpsertCommand
                {
                    DealId = input.CrmDealId?.Trim() ?? "",
                    ScenarioId = scenario.ScenarioId,
                    CompanyId = input.Cliente?.ClienteId?.Trim() ?? "",
                    PrimaryContactId = "",
                    Name = BuildProvisioningDealName(scenario, input.Cliente),
                    Kind = CrmDealKind.QuotedBusiness,
                    EstimatedValue = 0m,
                    Probability = 0m,
                    ExpectedCloseDate = ParseDateOnlyValue(input.Aprovisionamiento?.Fecha),
                    NextAction = "",
                    NextActionAtUtc = null,
                    BusinessLine = "",
                    ApplyCommercialFields = false,
                    Score = authoritativeResult.Points,
                    ContractValue = authoritativeResult.TotalSale
                },
                accessScope,
                ct);
            markedDeal = await _crmRepository.MarkProvisioningRequestedAsync(
                scenario.ScenarioId,
                requestId,
                DateTimeOffset.UtcNow,
                accessScope,
                ct);
            if (markedDeal is null)
            {
                throw new CrmDataverseException(
                    "El negocio quedó sincronizado, pero no fue posible registrar la evidencia de aprovisionamiento.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "El flujo aceptó la solicitud {RequestId}, pero falló el enlace CRM del escenario {ScenarioId}. TraceId: {TraceId}.",
                requestId,
                scenario.ScenarioId,
                HttpContext.TraceIdentifier);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                ok = false,
                flowAccepted = true,
                requestId,
                canMarkWon = false,
                message = "Aprovisionamiento recibió la solicitud, pero el CRM no pudo registrar el vínculo. No la envíes nuevamente; usa el RequestId para reconciliarla.",
                detail = CompactDiagnosticMessage(ex.Message),
                traceId = HttpContext.TraceIdentifier
            });
        }

        return Ok(new
        {
            ok = true,
            requestId,
            crmDealId = markedDeal.Id,
            crmSynchronized = true,
            canMarkWon = markedDeal.CanMarkWon,
            deal = markedDeal,
            message = "Solicitud enviada a aprobación y negocio actualizado en el CRM."
        });
    }

    private static IReadOnlyList<ScenarioGroupStoredDto> BuildScenarioGroups(
        IReadOnlyCollection<ScenarioStoredDto> scenarios,
        IReadOnlyDictionary<string, IReadOnlyList<ProposalExportHistoryItemDto>> historyByGroup)
    {
        return scenarios
            .Where(item => !string.IsNullOrWhiteSpace(item.ScenarioId))
            .GroupBy(
                item => FirstNonEmpty(item.GroupId, item.ScenarioId),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var possibilities = group
                    .OrderBy(item => item.PossibilityOrder)
                    .ThenBy(item => item.ScenarioName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                var primary = possibilities.FirstOrDefault(item => item.IsRecommended) ?? possibilities[0];
                historyByGroup.TryGetValue(group.Key, out var history);
                return new ScenarioGroupStoredDto
                {
                    GroupId = group.Key,
                    GroupName = FirstNonEmpty(primary.GroupName, primary.ScenarioName, "Negocio"),
                    PrimaryScenarioId = primary.ScenarioId,
                    Possibilities = possibilities,
                    ProposalHistory = history ?? []
                };
            })
            .OrderBy(group => group.GroupName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private string BuildGroupEconomicHash(IEnumerable<ScenarioStoredDto> source)
    {
        var possibilities = source
            .Where(item => item.IncludeInProposal)
            .OrderBy(item => item.PossibilityOrder)
            .ThenBy(item => item.ScenarioId, StringComparer.Ordinal)
            .Select(item =>
            {
                var lines = item.Lines.Select(BuildProposalLine).ToList();
                return new
                {
                    scenarioId = item.ScenarioId.Trim(),
                    inputHash = ScenarioInputHasher.Compute(item),
                    title = FirstNonEmpty(item.PossibilityName, item.ScenarioName),
                    order = item.PossibilityOrder,
                    recommended = item.IsRecommended,
                    lines = lines.Select(line => new
                    {
                        line.Front,
                        line.Description,
                        line.Quantity,
                        line.ContractMonths,
                        line.UnitSale,
                        line.MonthlySale,
                        line.ContractSale,
                        line.HasVat,
                        line.MonthlyVat,
                        line.ContractVat
                    })
                };
            });
        var canonical = JsonSerializer.Serialize(possibilities);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private async Task<IReadOnlyList<ScenarioStoredDto>> GetOwnedScenarioGroupAsync(
        string? groupId,
        CancellationToken ct)
    {
        var normalized = groupId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ScenarioPersistenceNotFoundException("GroupId requerido.");
        var currentUser = await _dataverse.GetCurrentUserAsync(ct)
            ?? throw new CrmAccessDeniedException("No fue posible validar el usuario actual.");
        if (string.IsNullOrWhiteSpace(currentUser.SystemUserId))
            throw new CrmAccessDeniedException("No fue posible validar el usuario actual.");
        var scenarios = await _dataverse.GetScenariosByGroupIdAsync(normalized, ct);
        if (scenarios.Count == 0)
            throw new ScenarioPersistenceNotFoundException("El escenario no existe o no está disponible.");
        if (scenarios.Any(item => !string.Equals(
                item.OwnerSystemUserId,
                currentUser.SystemUserId,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new CrmAccessDeniedException("El escenario no pertenece al usuario actual.");
        }
        return scenarios;
    }

    private async Task<IReadOnlyList<ScenarioStoredDto>> GetAuthorizedScenarioGroupAsync(
        string? groupId,
        string? scenarioId,
        string? crmDealId,
        CancellationToken ct)
    {
        var normalizedGroupId = groupId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalizedGroupId))
            throw new ScenarioPersistenceNotFoundException("GroupId requerido.");

        if (string.IsNullOrWhiteSpace(scenarioId))
            return await GetOwnedScenarioGroupAsync(normalizedGroupId, ct);

        var currentUser = await _dataverse.GetCurrentUserAsync(ct)
            ?? throw new CrmAccessDeniedException("No fue posible validar el usuario actual.");
        if (string.IsNullOrWhiteSpace(currentUser.SystemUserId))
            throw new CrmAccessDeniedException("No fue posible validar el usuario actual.");

        var accessScenario = _crmCalculatorSyncEnabled
            ? await FindAuthorizedScenarioAsync(scenarioId, crmDealId, currentUser, ct)
            : await FindCurrentUserScenarioAsync(scenarioId, ct);
        if (accessScenario is null)
            throw new ScenarioPersistenceNotFoundException("El escenario no existe o no está disponible.");

        var authorizedGroupId = FirstNonEmpty(accessScenario.GroupId, accessScenario.ScenarioId).Trim();
        if (!string.Equals(authorizedGroupId, normalizedGroupId, StringComparison.OrdinalIgnoreCase))
            throw new CrmAccessDeniedException("El escenario no pertenece al negocio solicitado.");

        var scenarios = await _dataverse.GetScenariosByGroupIdAsync(normalizedGroupId, ct);
        if (scenarios.Count == 0)
            throw new ScenarioPersistenceNotFoundException("El escenario no existe o no está disponible.");
        if (scenarios.Any(item => !string.Equals(
                item.OwnerSystemUserId,
                accessScenario.OwnerSystemUserId,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new ScenarioPersistenceConflictException(
                "Los escenarios del negocio no comparten el mismo propietario.");
        }
        return scenarios;
    }

    private static ScenarioSaveRequest BuildScenarioSaveRequest(ScenarioStoredDto scenario) => new()
    {
        ScenarioId = scenario.ScenarioId,
        GroupId = scenario.GroupId,
        GroupName = scenario.GroupName,
        PossibilityName = scenario.PossibilityName,
        PossibilityOrder = scenario.PossibilityOrder,
        IncludeInProposal = scenario.IncludeInProposal,
        IsRecommended = scenario.IsRecommended,
        ExpectedRowVersion = scenario.RowVersion,
        CrmDealId = scenario.CrmDealId,
        ScenarioName = scenario.ScenarioName,
        DealType = scenario.DealType,
        RequiresProration = scenario.RequiresProration,
        StartDate = ParseScenarioDate(scenario.StartDate),
        EndDate = ParseScenarioDate(scenario.EndDate),
        Lines = scenario.Lines,
        LastResult = scenario.LastResult
    };

    private async Task<ScenarioStoredDto?> FindCurrentUserScenarioAsync(
        string? scenarioId,
        CancellationToken ct)
    {
        var normalizedScenarioId = scenarioId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalizedScenarioId))
            return null;

        var scenarios = await _dataverse.GetScenariosForUserAsync(ct);
        return scenarios.FirstOrDefault(item =>
            string.Equals(
                item.ScenarioId?.Trim(),
                normalizedScenarioId,
                StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ScenarioStoredDto?> FindAuthorizedScenarioAsync(
        string? scenarioId,
        string? crmDealId,
        CurrentUserInfo? currentUser,
        CancellationToken ct)
    {
        var normalizedScenarioId = scenarioId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalizedScenarioId))
            return null;

        var normalizedDealId = crmDealId?.Trim() ?? "";
        if (!AppModuleAccessPolicy.CanAccess(AppModule.Crm, currentUser)
            || string.IsNullOrWhiteSpace(normalizedDealId))
        {
            return await FindCurrentUserScenarioAsync(normalizedScenarioId, ct);
        }

        if (!Guid.TryParse(normalizedDealId, out var dealGuid))
            throw new CrmValidationException("El negocio CRM seleccionado no es válido.");

        var accessScope = BuildCalculatorCrmAccessScope(currentUser);
        var detail = await _crmRepository.GetDealDetailAsync(
            dealGuid.ToString("D"),
            new CrmDetailQuery { PageSize = 5 },
            accessScope,
            ct);
        if (!string.Equals(
            detail.Deal.ScenarioId?.Trim(),
            normalizedScenarioId,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new CrmConflictException(
                "El negocio CRM no está asociado al escenario solicitado.");
        }

        var scenario = await _dataverse.GetScenarioByIdAsync(normalizedScenarioId, ct)
            ?? throw new CrmNotFoundException(
                "El escenario asociado al negocio ya no existe.");
        scenario.CrmDealId = detail.Deal.Id;
        scenario.IsCrmSharedAccess = !string.Equals(
            scenario.OwnerSystemUserId?.Trim(),
            currentUser?.SystemUserId?.Trim(),
            StringComparison.OrdinalIgnoreCase);
        return scenario;
    }

    private static CrmAccessScope BuildCalculatorCrmAccessScope(CurrentUserInfo? currentUser)
    {
        if (!Guid.TryParse(currentUser?.SystemUserId, out var actorSystemUserId)
            || !CrmAccessPolicy.CanAccess(currentUser))
        {
            throw new CrmAccessDeniedException(
                "No fue posible validar tu identidad de propietario en el CRM.");
        }

        var actorId = actorSystemUserId.ToString("D");
        var isCrmAdministrator = CrmAccessPolicy.IsAdministrator(currentUser);
        return new CrmAccessScope
        {
            ActorSystemUserId = actorId,
            ActorName = currentUser?.DisplayName?.Trim() ?? "",
            Role = isCrmAdministrator ? CrmRole.Administrator : CrmRole.User,
            OwnerFilterSystemUserId = isCrmAdministrator ? "" : actorId
        };
    }

    private static bool IsScenarioAccessException(Exception ex) =>
        ex is CrmValidationException
            or CrmNotFoundException
            or CrmConflictException
            or CrmAccessDeniedException
            or ScenarioPersistenceConflictException
            or ScenarioPersistenceNotFoundException
            or ScenarioPersistenceConcurrencyException;

    private IActionResult BuildScenarioAccessError(Exception ex)
    {
        var payload = new
        {
            message = ex.Message,
            traceId = HttpContext.TraceIdentifier
        };
        return ex switch
        {
            CrmValidationException => BadRequest(payload),
            CrmAccessDeniedException => StatusCode(StatusCodes.Status403Forbidden, payload),
            CrmNotFoundException or ScenarioPersistenceNotFoundException => NotFound(payload),
            _ => Conflict(payload)
        };
    }

    private QuoteScenarioResult RecalculateStoredScenario(ScenarioStoredDto scenario)
    {
        if (scenario.Lines is null || scenario.Lines.Count == 0)
            throw new InvalidOperationException("El negocio cotizado requiere al menos una línea guardada.");

        var dealTypeValue = scenario.RequiresProration
            ? (int)DealType.CrossSale
            : scenario.DealType;
        if (!Enum.IsDefined(typeof(DealType), dealTypeValue))
            throw new InvalidOperationException("El escenario guardado tiene un tipo de negocio inválido.");

        var startDate = ParseScenarioDate(scenario.StartDate);
        var endDate = ParseScenarioDate(scenario.EndDate);
        if (scenario.RequiresProration
            && (!startDate.HasValue || !endDate.HasValue || endDate.Value.Date < startDate.Value.Date))
        {
            throw new InvalidOperationException(
                "El escenario guardado requiere fechas válidas para calcular el prorrateo.");
        }

        var lines = new List<QuoteLineInput>(scenario.Lines.Count);
        for (var index = 0; index < scenario.Lines.Count; index++)
        {
            var line = scenario.Lines[index];
            if (!Enum.IsDefined(typeof(BusinessType), line.BusinessType))
            {
                throw new InvalidOperationException(
                    $"La línea {index + 1} tiene un tipo de negocio inválido.");
            }

            if (string.IsNullOrWhiteSpace(line.ProductDescription))
                throw new InvalidOperationException($"La línea {index + 1} no tiene producto.");
            if (line.Quantity <= 0)
                throw new InvalidOperationException($"La línea {index + 1} tiene una cantidad inválida.");
            if (line.ContractMonths <= 0)
                throw new InvalidOperationException($"La línea {index + 1} tiene una duración inválida.");

            lines.Add(new QuoteLineInput
            {
                BusinessType = (BusinessType)line.BusinessType,
                ProductId = line.ProductId?.Trim() ?? "",
                ProductDescription = line.ProductDescription.Trim(),
                CostUnit = line.CostUnit,
                MarginPercent = line.MarginPercent,
                ContractMonths = line.ContractMonths,
                Quantity = line.Quantity,
                SuggestedRetailPrice = line.SuggestedRetailPrice,
                Acelerador = line.Acelerador,
                HasVat = line.HasVat
            });
        }

        var quote = new QuoteScenarioInput
        {
            ScenarioName = scenario.ScenarioName,
            DealType = (DealType)dealTypeValue,
            RequiresProration = scenario.RequiresProration,
            StartDate = startDate,
            EndDate = endDate,
            Lines = lines
        };
        NormalizeProrationRules(quote);

        var licenseValidation = ValidateLicenseCaps(quote);
        if (!string.IsNullOrWhiteSpace(licenseValidation))
            throw new InvalidOperationException(licenseValidation);

        return _calculator.Calculate(quote);
    }

    private ScenarioResultSnapshot BuildAuthoritativeScenarioSnapshot(ScenarioSaveRequest request)
    {
        var scenario = new ScenarioStoredDto
        {
            ScenarioId = request.ScenarioId,
            ScenarioName = request.ScenarioName,
            DealType = request.DealType,
            RequiresProration = request.RequiresProration,
            StartDate = request.StartDate?.ToString("O", CultureInfo.InvariantCulture),
            EndDate = request.EndDate?.ToString("O", CultureInfo.InvariantCulture),
            Lines = request.Lines ?? []
        };
        var result = RecalculateStoredScenario(scenario);
        return new ScenarioResultSnapshot
        {
            InputHash = ScenarioInputHasher.Compute(request),
            Points = result.Points,
            Commission = result.Commission,
            ProrationDays = result.ProrationDays,
            ProrationFactor = result.ProrationFactor,
            ProrationText = result.ProrationDays > 0
                ? $"{result.ProrationDays} días ({result.ProrationFactor:0.0000})"
                : "No",
            TotalMonthlySale = result.TotalMonthlySale,
            TotalSale = result.TotalSale
        };
    }

    private static ProvisioningScenarioContext BuildProvisioningScenarioContext(
        ScenarioStoredDto scenario)
    {
        var dealTypeValue = scenario.RequiresProration
            ? (int)DealType.CrossSale
            : scenario.DealType;
        return new ProvisioningScenarioContext
        {
            DealTypeValue = dealTypeValue,
            DealTypeLabel = Enum.IsDefined(typeof(DealType), dealTypeValue)
                ? ((DealType)dealTypeValue).ToString()
                : "",
            RequiresProration = scenario.RequiresProration,
            StartDate = scenario.StartDate,
            EndDate = scenario.EndDate
        };
    }

    private static ProvisioningResultado BuildProvisioningResult(QuoteScenarioResult result) =>
        new()
        {
            Puntaje = result.Points,
            Comision = result.Commission,
            ProrrateoDias = result.ProrationDays,
            ProrrateoFactor = result.ProrationFactor,
            ProrrateoTexto = result.ProrationDays > 0
                ? $"{result.ProrationDays} días ({result.ProrationFactor:0.0000})"
                : "No",
            VentaMensualTotal = result.TotalMonthlySale,
            VentaTotal = result.TotalSale,
            VentaTotalAnual = result.TotalSale
        };

    private static string BuildProvisioningDealName(
        ScenarioStoredDto scenario,
        ProvisioningClient? client)
    {
        var scenarioName = scenario.ScenarioName?.Trim() ?? "";
        var clientName = client?.Nombre?.Trim() ?? "";
        var value = scenarioName.Length >= 3
            ? scenarioName
            : $"Negocio {clientName}".Trim();
        if (value.Length < 3)
            value = "Negocio cotizado";

        return value.Length <= 200 ? value : value[..200];
    }

    private static DateTime? ParseScenarioDate(string? value)
    {
        var normalized = value?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return DateTime.TryParse(
            normalized,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed.Date
            : null;
    }

    private static DateOnly? ParseDateOnlyValue(string? value)
    {
        var normalized = value?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return DateOnly.TryParse(
            normalized,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var parsed)
            ? parsed
            : null;
    }

    private static XLWorkbook BuildWorkbook(QuoteScenarioInput input)
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("CotizaciÃ³n");

        var row = 1;
        sheet.Cell(row, 1).Value = "Escenario";
        sheet.Cell(row, 2).Value = input.ScenarioName;
        row++;

        sheet.Cell(row, 1).Value = "Tipo de negocio";
        sheet.Cell(row, 2).Value = input.DealType.ToString();
        row++;

        if (input.RequiresProration)
        {
            sheet.Cell(row, 1).Value = "Prorrateo";
            sheet.Cell(row, 2).Value = input.StartDate.HasValue && input.EndDate.HasValue
                ? $"{input.StartDate:yyyy-MM-dd} al {input.EndDate:yyyy-MM-dd}"
                : "Pendiente fechas de prorrateo";
            row++;
        }

        row++;

        var headers = new List<string>
        {
            "Tipo",
            "Producto",
            "Margen %",
            "DuraciÃ³n (meses)",
            "Venta UND",
            "Cantidad",
            "Venta Mensual",
            "Venta Total",
            "Precio Sugerido"
        };

        var headerRow = row;
        for (var i = 0; i < headers.Count; i++)
            sheet.Cell(headerRow, i + 1).Value = headers[i];

        sheet.Range(headerRow, 1, headerRow, headers.Count).Style.Font.Bold = true;
        row++;

        var idxSaleUnit = headers.IndexOf("Venta UND") + 1;
        var idxMonthly = headers.IndexOf("Venta Mensual") + 1;
        var idxTotal = headers.IndexOf("Venta Total") + 1;
        var idxSuggested = headers.IndexOf("Precio Sugerido") + 1;

        decimal tSaleUnit = 0m, tMonthly = 0m, tTotal = 0m, tSuggested = 0m;

        foreach (var line in input.Lines)
        {
            var computed = ComputeLine(line);

            sheet.Cell(row, 1).Value = line.BusinessType.ToString();
            sheet.Cell(row, 2).Value = line.ProductDescription;
            sheet.Cell(row, 3).Value = Round2(line.MarginPercent);
            sheet.Cell(row, 4).Value = line.ContractMonths;
            sheet.Cell(row, idxSaleUnit).Value = computed.SaleUnit;
            sheet.Cell(row, 6).Value = line.Quantity;
            sheet.Cell(row, idxMonthly).Value = computed.Monthly;
            sheet.Cell(row, idxTotal).Value = computed.Total;
            sheet.Cell(row, idxSuggested).Value = Round2(line.SuggestedRetailPrice);

            tSaleUnit += computed.SaleUnit * line.Quantity;
            tMonthly += computed.Monthly;
            tTotal += computed.Total;
            tSuggested += line.SuggestedRetailPrice * line.Quantity;

            row++;
        }

        sheet.Cell(row, 1).Value = "Totales";
        sheet.Cell(row, 3).Value = "â€”";
        sheet.Cell(row, 4).Value = "â€”";
        sheet.Cell(row, idxSaleUnit).Value = Round2(tSaleUnit);
        sheet.Cell(row, 6).Value = "â€”";
        sheet.Cell(row, idxMonthly).Value = Round2(tMonthly);
        sheet.Cell(row, idxTotal).Value = Round2(tTotal);
        sheet.Cell(row, idxSuggested).Value = Round2(tSuggested);

        sheet.Range(headerRow + 1, 1, row, headers.Count).Style.NumberFormat.Format = "#,##0.00";
        sheet.Column(6).Style.NumberFormat.Format = "0";
        sheet.Column(4).Style.NumberFormat.Format = "0";
        sheet.Column(3).Style.NumberFormat.Format = "#,##0.00";
        sheet.Columns().AdjustToContents();

        return workbook;
    }

    private static string? ValidateProvisioningPayload(ProvisioningRequestInput input)
    {
        if (string.IsNullOrWhiteSpace(input.BusinessId))
            return "No se recibió el identificador del escenario.";

        if (input.Cliente is null || !Guid.TryParse(input.Cliente.ClienteId, out _))
            return "Selecciona un cliente válido para solicitar el aprovisionamiento.";

        if (input.LineItems is null || input.LineItems.Count == 0)
            return "No hay lÃ­neas para enviar.";

        var productValidation = ValidateSelectedProducts(input.LineItems, "enviar la solicitud de aprovisionamiento");
        if (!string.IsNullOrWhiteSpace(productValidation))
            return productValidation;

        if (input.Scenario is null)
            return "No se recibio el tipo de negocio del escenario.";

        if (!Enum.IsDefined(typeof(DealType), input.Scenario.DealTypeValue))
            return "El tipo de negocio del escenario no es valido.";

        var contractKindCode = ResolveProvisioningContractKindCode(input.Aprovisionamiento);
        if (contractKindCode is not ProvisioningContractKindNewBusinessValue and not ProvisioningContractKindRenewalValue)
            return "Selecciona si el contrato es negocio nuevo o renovacion.";

        var attachment = input.Attachment;
        if (attachment is null)
            return "Debes adjuntar la oferta autorizada o correo de aprobaciÃ³n.";

        if (string.IsNullOrWhiteSpace(attachment.FileName) || string.IsNullOrWhiteSpace(attachment.Base64))
            return "Debes adjuntar la oferta autorizada o correo de aprobaciÃ³n.";

        var extension = Path.GetExtension(attachment.FileName).ToLowerInvariant().TrimStart('.');
        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pdf", "jpg", "jpeg", "doc", "docx"
        };
        if (!allowedExtensions.Contains(extension))
            return "El adjunto debe ser PDF, JPG/JPEG o DOC/DOCX.";

        var allowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf",
            "image/jpeg",
            "image/jpg",
            "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        };

        if (string.IsNullOrWhiteSpace(attachment.ContentType) || !allowedContentTypes.Contains(attachment.ContentType))
            return "El adjunto debe ser PDF, JPG/JPEG o DOC/DOCX.";

        try
        {
            _ = Convert.FromBase64String(attachment.Base64);
        }
        catch (FormatException)
        {
            return "El adjunto no es vÃ¡lido.";
        }

        return null;
    }

    private async Task EnsureHardwareProductsForProvisioningAsync(ProvisioningRequestInput input, CancellationToken ct)
    {
        if (input.LineItems is null || input.LineItems.Count == 0)
            return;

        var cache = new Dictionary<string, ProductLookupItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var (line, index) in input.LineItems.Select((value, index) => (value, index)))
        {
            if (!IsHardwareLine(line.Tipo) || !string.IsNullOrWhiteSpace(line.ProductoId))
                continue;

            var productName = (line.ProductoNombre ?? "").Trim();
            if (string.IsNullOrWhiteSpace(productName))
                throw new InvalidOperationException($"La linea {index + 1} de Hardware no tiene producto.");

            var suggestedRetailPrice = line.VentaUnd > 0m
                ? line.VentaUnd
                : line.CostoUnd * (1m + (line.MargenPorcentaje / 100m));
            var cacheKey = BuildHardwareProductCacheKey(productName, line.CostoUnd, suggestedRetailPrice);

            if (!cache.TryGetValue(cacheKey, out var product))
            {
                product = await _dataverse.EnsureCalculatorProductAsync(new ProductCreateInput
                {
                    Description = productName,
                    PurchasePrice = line.CostoUnd,
                    SuggestedRetailPrice = suggestedRetailPrice,
                    Acelerador = 0m
                }, ct);
                cache[cacheKey] = product;
            }

            line.ProductoId = product.Id;
            if (string.IsNullOrWhiteSpace(line.LineId) || line.LineId.StartsWith("line-", StringComparison.OrdinalIgnoreCase))
                line.LineId = product.Id;
        }
    }

    private static string BuildHardwareProductCacheKey(string productName, decimal costUnit, decimal suggestedRetailPrice) =>
        string.Join(
            "|",
            productName.Trim().ToUpperInvariant(),
            Round2(costUnit).ToString("0.##", CultureInfo.InvariantCulture),
            Round2(suggestedRetailPrice).ToString("0.##", CultureInfo.InvariantCulture));

    private static object BuildProvisioningFlowPayload(
        ProvisioningRequestInput input,
        string requestId)
    {
        var requester = input.Requester;
        var cliente = input.Cliente;
        var aprovisionamiento = input.Aprovisionamiento;
        var scenario = input.Scenario;
        var resultado = input.Resultado;
        var attachment = input.Attachment;
        var dealTypeValue = ResolveDealTypeValue(scenario);
        var dealTypeLabel = ResolveDealTypeLabel(scenario);
        var contractKindCode = ResolveProvisioningContractKindCode(aprovisionamiento);
        var contractKindLabel = ResolveProvisioningContractKindLabel(contractKindCode, aprovisionamiento);
        var isNewBusinessContract = contractKindCode == ProvisioningContractKindNewBusinessValue;
        var contractValue = RoundWholeNumber(resultado is { VentaTotalAnual: > 0m }
            ? resultado.VentaTotalAnual
            : resultado?.VentaTotal ?? 0m);
        var normalizedScenarioStartDate = NormalizeDateLikeValue(scenario?.StartDate);
        var normalizedScenarioEndDate = NormalizeDateLikeValue(scenario?.EndDate);
        var lineItems = input.LineItems.Select(item => new ProvisioningFlowLinePayload
        {
            LineId = item.LineId?.Trim() ?? "",
            ProductoId = item.ProductoId?.Trim() ?? "",
            ProductoNombre = item.ProductoNombre?.Trim() ?? "",
            Cantidad = Round2(item.Cantidad),
            Number = Round2(item.Number),
            CostoUnd = Round2(item.CostoUnd),
            VentaUnd = Round2(item.VentaUnd),
            MargenPorcentaje = Round2(item.MargenPorcentaje),
            DuracionMeses = item.DuracionMeses,
            SuggestedRetailPrice = Round2(item.SuggestedRetailPrice),
            Acelerador = Round2(item.Acelerador),
            VentaMensual = Round2(item.VentaMensual),
            VentaTotal = Round2(item.VentaTotal),
            TieneIva = item.TieneIva,
            Tipo = item.Tipo?.Trim() ?? "",
            RequiereProrrateo = scenario?.RequiresProration ?? false,
            Inicio = normalizedScenarioStartDate,
            Final = normalizedScenarioEndDate
        }).ToList();
        var descriptionText = BuildFullProvisioningDescription(cliente, aprovisionamiento, scenario, resultado, lineItems, dealTypeLabel);
        var legacyDescriptionText = BuildLimitedProvisioningDescription(cliente, aprovisionamiento, scenario, resultado, lineItems, dealTypeLabel);
        var lineItemsJson = SerializeDetailedProvisioningLines(lineItems);
        var lineItemsTableText = BuildProvisioningLineItemsMarkdownTable(lineItems, maxProductNameLength: 120, includeCommercialFields: true, includeTechnicalFields: true);
        var lineItemsTableMarkdown = BuildProvisioningLineItemsMarkdownTable(lineItems, maxProductNameLength: 120, includeCommercialFields: true, includeTechnicalFields: false);
        var lineItemsTableHtml = BuildProvisioningLineItemsHtmlTable(lineItems);
        var notificationSummaryText = BuildProvisioningNotificationSummaryText(requester, cliente, aprovisionamiento, scenario, resultado, dealTypeLabel, contractKindLabel, requestId);
        var teamsMessageMarkdown = BuildProvisioningTeamsMessageMarkdown(notificationSummaryText, lineItemsTableMarkdown);
        var emailHtml = BuildProvisioningEmailHtml(notificationSummaryText, lineItemsTableHtml);

        return new
        {
            requestId,
            cr07a_contractvalue = contractValue,
            source = input.Source?.Trim() ?? "",
            businessId = input.BusinessId?.Trim() ?? "",
            requester = requester is null ? null : new
            {
                systemUserId = requester.SystemUserId?.Trim() ?? "",
                displayName = requester.DisplayName?.Trim() ?? "",
                email = requester.Email?.Trim() ?? ""
            },
            cliente = cliente is null ? null : new
            {
                clienteId = cliente.ClienteId?.Trim() ?? "",
                nombre = cliente.Nombre?.Trim() ?? ""
            },
            aprovisionamiento = aprovisionamiento is null ? null : new
            {
                fecha = aprovisionamiento.Fecha?.Trim() ?? "",
                tipoContratoCode = aprovisionamiento.TipoContratoCode?.Trim() ?? "",
                tipoContratoLabel = aprovisionamiento.TipoContratoLabel?.Trim() ?? "",
                tipoContratoPuntajeCode = contractKindCode,
                tipoContratoPuntajeLabel = contractKindLabel,
                cr07a_contrato = contractKindCode,
                cr07a_tipodecontrato = contractKindCode,
                esNegocioNuevo = isNewBusinessContract,
                esRenovacion = contractKindCode == ProvisioningContractKindRenewalValue
            },
            scenario = scenario is null ? null : new
            {
                dealTypeValue,
                dealTypeLabel,
                contractKindCode,
                contractKindLabel,
                shouldProvisionCloudProduct = isNewBusinessContract,
                requiresProration = scenario.RequiresProration,
                startDate = normalizedScenarioStartDate,
                endDate = normalizedScenarioEndDate
            },
            resultado = resultado is null ? null : new
            {
                puntaje = RoundWholeNumber(resultado.Puntaje),
                comision = RoundWholeNumber(resultado.Comision),
                prorrateoDias = resultado.ProrrateoDias,
                prorrateoFactor = RoundWholeNumber(resultado.ProrrateoFactor),
                prorrateoTexto = resultado.ProrrateoTexto?.Trim() ?? "",
                ventaMensualTotal = RoundWholeNumber(resultado.VentaMensualTotal),
                ventaTotal = RoundWholeNumber(resultado.VentaTotal),
                ventaTotalAnual = RoundWholeNumber(resultado.VentaTotalAnual),
                cr07a_contractvalue = contractValue
            },
            descriptionText,
            legacyDescriptionText,
            lineItemsJson,
            lineItemsTableText,
            lineItemsTableMarkdown,
            lineItemsTableHtml,
            notificationSummaryText,
            teamsMessageMarkdown,
            emailHtml,
            descriptionTextLength = descriptionText.Length,
            legacyDescriptionTextLength = legacyDescriptionText.Length,
            dataverseFields = new
            {
                description = ProvisioningDescriptionField,
                legacyDescription = ProvisioningLegacyDescriptionField
            },
            notification = new
            {
                summaryText = notificationSummaryText,
                teamsMarkdown = teamsMessageMarkdown,
                emailHtml,
                lineItemsTableText,
                lineItemsTableMarkdown,
                lineItemsTableHtml
            },
            lineItems = lineItems.Select(item => new
            {
                lineId = item.LineId,
                productoId = item.ProductoId,
                productoNombre = item.ProductoNombre,
                // The Power Automate trigger schema expects integers for these fields.
                cantidad = RoundWholeNumber(item.Cantidad),
                number = RoundWholeNumber(item.Number),
                costoUnd = RoundWholeNumber(item.CostoUnd),
                ventaUnd = RoundWholeNumber(item.VentaUnd),
                margenPorcentaje = RoundWholeNumber(item.MargenPorcentaje),
                duracionMeses = item.DuracionMeses,
                suggestedRetailPrice = RoundWholeNumber(item.SuggestedRetailPrice),
                acelerador = item.Acelerador,
                ventaMensual = RoundWholeNumber(item.VentaMensual),
                ventaTotal = RoundWholeNumber(item.VentaTotal),
                tieneIva = item.TieneIva,
                tipo = item.Tipo,
                requiereProrrateo = item.RequiereProrrateo,
                inicio = item.Inicio,
                final = item.Final
            }),
            attachment = attachment is null ? null : new
            {
                fileName = attachment.FileName?.Trim() ?? "",
                contentType = attachment.ContentType?.Trim() ?? "",
                base64 = attachment.Base64 ?? ""
            }
        };
    }

    private static string BuildFullProvisioningDescription(
        ProvisioningClient? cliente,
        ProvisioningAprovisionamiento? aprovisionamiento,
        ProvisioningScenarioContext? scenario,
        ProvisioningResultado? resultado,
        IReadOnlyList<ProvisioningFlowLinePayload> lineItems,
        string dealTypeLabel)
    {
        var description = BuildProvisioningDescriptionText(
            BuildProvisioningDescriptionHeader(cliente, aprovisionamiento, scenario, resultado, dealTypeLabel),
            BuildProvisioningLineItemsMarkdownTable(lineItems, maxProductNameLength: 160, includeCommercialFields: true, includeTechnicalFields: true));
        return TruncateTextForDescription(description, ProvisioningLongDescriptionMaxLength);
    }

    private static string BuildLimitedProvisioningDescription(
        ProvisioningClient? cliente,
        ProvisioningAprovisionamiento? aprovisionamiento,
        ProvisioningScenarioContext? scenario,
        ProvisioningResultado? resultado,
        IReadOnlyList<ProvisioningFlowLinePayload> lineItems,
        string dealTypeLabel)
    {
        var headerText = BuildProvisioningDescriptionHeader(cliente, aprovisionamiento, scenario, resultado, dealTypeLabel);
        var detailedDescription = BuildProvisioningDescriptionText(
            headerText,
            BuildProvisioningLineItemsMarkdownTable(lineItems, maxProductNameLength: 120, includeCommercialFields: true, includeTechnicalFields: true));
        if (FitsProvisioningDescriptionLimit(detailedDescription))
            return detailedDescription;

        var compactDescription = BuildProvisioningDescriptionText(
            headerText,
            BuildProvisioningLineItemsMarkdownTable(lineItems, maxProductNameLength: 90, includeCommercialFields: true, includeTechnicalFields: false));
        if (FitsProvisioningDescriptionLimit(compactDescription))
            return compactDescription;

        foreach (var maxProductNameLength in new[] { 120, 80, 50, 30 })
        {
            compactDescription = BuildProvisioningDescriptionText(
                headerText,
                BuildProvisioningLineItemsMarkdownTable(lineItems, maxProductNameLength, includeCommercialFields: true, includeTechnicalFields: false));
            if (FitsProvisioningDescriptionLimit(compactDescription))
                return compactDescription;
        }

        compactDescription = BuildProvisioningDescriptionText(
            headerText,
            BuildProvisioningLineItemsMarkdownTable(lineItems, maxProductNameLength: 30, includeCommercialFields: false, includeTechnicalFields: false));
        if (FitsProvisioningDescriptionLimit(compactDescription))
            return compactDescription;

        return BuildProvisioningDescriptionWithLineBudget(headerText, lineItems);
    }

    private static string BuildProvisioningDescriptionHeader(
        ProvisioningClient? cliente,
        ProvisioningAprovisionamiento? aprovisionamiento,
        ProvisioningScenarioContext? scenario,
        ProvisioningResultado? resultado,
        string dealTypeLabel)
    {
        var builder = new StringBuilder();
        var normalizedProvisioningDate = NormalizeDateLikeValue(aprovisionamiento?.Fecha);
        var normalizedScenarioStartDate = NormalizeDateLikeValue(scenario?.StartDate);
        var normalizedScenarioEndDate = NormalizeDateLikeValue(scenario?.EndDate);
        var requiresProration = scenario?.RequiresProration == true;
        var contractKindCode = ResolveProvisioningContractKindCode(aprovisionamiento);
        var contractKindLabel = ResolveProvisioningContractKindLabel(contractKindCode, aprovisionamiento);

        builder.AppendLine($"Cliente: {cliente?.Nombre?.Trim() ?? ""}");
        builder.AppendLine($"Fecha aprovisionamiento: {normalizedProvisioningDate}");
        if (!string.IsNullOrWhiteSpace(contractKindLabel))
            builder.AppendLine($"Tipo contrato: {contractKindLabel}");
        builder.AppendLine($"Tipo negocio: {dealTypeLabel}");
        builder.AppendLine($"Requiere prorrateo: {(requiresProration ? "Si" : "No")}");
        if (!string.IsNullOrWhiteSpace(normalizedScenarioStartDate))
            builder.AppendLine($"Inicio: {normalizedScenarioStartDate}");
        if (!string.IsNullOrWhiteSpace(normalizedScenarioEndDate))
            builder.AppendLine($"Final: {normalizedScenarioEndDate}");
        builder.AppendLine($"Puntaje: {FormatDecimalText(resultado?.Puntaje ?? 0m)}");
        builder.AppendLine($"Comisión: {FormatDecimalText(resultado?.Comision ?? 0m)}");
        builder.AppendLine($"Prorrateo: {(resultado?.ProrrateoTexto?.Trim() ?? (requiresProration ? "Si" : "No"))}");
        builder.AppendLine($"Venta mensual total: {FormatDecimalText(resultado?.VentaMensualTotal ?? 0m)}");
        builder.AppendLine($"Venta total anual: {FormatDecimalText(resultado?.VentaTotalAnual ?? resultado?.VentaTotal ?? 0m)}");
        return builder.ToString();
    }

    private static string BuildProvisioningDescriptionText(string headerText, string linesContent, string? extraMetadataLine = null)
    {
        var builder = new StringBuilder(headerText.Length + linesContent.Length + 24);
        builder.Append(headerText);
        if (!string.IsNullOrWhiteSpace(extraMetadataLine))
            extraMetadataLine = extraMetadataLine.Trim();
        builder.AppendLine();
        builder.AppendLine("Líneas:");
        builder.Append(linesContent);
        if (!string.IsNullOrWhiteSpace(extraMetadataLine))
        {
            builder.AppendLine();
            builder.Append(extraMetadataLine);
        }
        return builder.ToString();
    }

    private static string BuildProvisioningNotificationSummaryText(
        ProvisioningRequester? requester,
        ProvisioningClient? cliente,
        ProvisioningAprovisionamiento? aprovisionamiento,
        ProvisioningScenarioContext? scenario,
        ProvisioningResultado? resultado,
        string dealTypeLabel,
        string contractKindLabel,
        string requestId)
    {
        var rows = new List<(string Label, string Value)>
        {
            ("Solicitud", requestId),
            ("Cliente", cliente?.Nombre?.Trim() ?? ""),
            ("Solicitante", FirstNonEmpty(requester?.DisplayName, requester?.Email)),
            ("Correo solicitante", requester?.Email?.Trim() ?? ""),
            ("Fecha aprovisionamiento", NormalizeDateLikeValue(aprovisionamiento?.Fecha)),
            ("Tipo contrato", contractKindLabel),
            ("Tipo negocio", dealTypeLabel),
            ("Requiere prorrateo", scenario?.RequiresProration == true ? "Si" : "No")
        };

        var normalizedScenarioStartDate = NormalizeDateLikeValue(scenario?.StartDate);
        var normalizedScenarioEndDate = NormalizeDateLikeValue(scenario?.EndDate);
        if (!string.IsNullOrWhiteSpace(normalizedScenarioStartDate))
            rows.Add(("Inicio", normalizedScenarioStartDate));
        if (!string.IsNullOrWhiteSpace(normalizedScenarioEndDate))
            rows.Add(("Final", normalizedScenarioEndDate));

        rows.Add(("Prorrateo", resultado?.ProrrateoTexto?.Trim() ?? ""));
        rows.Add(("Puntaje", FormatDecimalForNotification(resultado?.Puntaje ?? 0m)));
        rows.Add(("Comision", FormatMoneyForNotification(resultado?.Comision ?? 0m)));
        rows.Add(("Venta mensual total", FormatMoneyForNotification(resultado?.VentaMensualTotal ?? 0m)));
        rows.Add(("Venta total anual", FormatMoneyForNotification(resultado?.VentaTotalAnual ?? resultado?.VentaTotal ?? 0m)));

        var labelWidth = rows.Max(static row => row.Label.Length);
        var builder = new StringBuilder();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Value))
                continue;

            builder.Append(row.Label.PadRight(labelWidth));
            builder.Append(": ");
            builder.AppendLine(row.Value.Trim());
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildProvisioningTeamsMessageMarkdown(string summaryText, string lineItemsTableMarkdown)
    {
        var builder = new StringBuilder(summaryText.Length + lineItemsTableMarkdown.Length + 80);
        builder.AppendLine("**Solicitud de aprovisionamiento**");
        builder.AppendLine();
        builder.AppendLine("```");
        builder.AppendLine(summaryText);
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("**Lineas solicitadas**");
        builder.Append(lineItemsTableMarkdown);
        return builder.ToString();
    }

    private static string BuildProvisioningEmailHtml(string summaryText, string lineItemsTableHtml)
    {
        var builder = new StringBuilder(summaryText.Length + lineItemsTableHtml.Length + 512);
        builder.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;color:#172033;font-size:14px;line-height:1.45;\">");
        builder.Append("<h2 style=\"margin:0 0 14px;color:#102a43;font-size:20px;\">Solicitud de aprovisionamiento</h2>");
        builder.Append("<pre style=\"margin:0 0 18px;padding:14px;background:#f6f8fb;border:1px solid #d9e2ec;border-radius:6px;white-space:pre-wrap;font-family:Consolas,Segoe UI,Arial,sans-serif;font-size:13px;\">");
        builder.Append(WebUtility.HtmlEncode(summaryText));
        builder.Append("</pre>");
        builder.Append("<h3 style=\"margin:0 0 10px;color:#102a43;font-size:16px;\">Lineas solicitadas</h3>");
        builder.Append(lineItemsTableHtml);
        builder.Append("</div>");
        return builder.ToString();
    }

    private static string BuildProvisioningLineItemsMarkdownTable(
        IReadOnlyList<ProvisioningFlowLinePayload> lineItems,
        int? maxProductNameLength,
        bool includeCommercialFields,
        bool includeTechnicalFields)
    {
        if (lineItems.Count == 0)
            return "_Sin lineas._";

        var builder = new StringBuilder(lineItems.Count * 180);
        if (includeCommercialFields)
        {
            builder.Append("| # | Tipo | Producto | Cant. | Costo und. | Venta und. | Margen % | Precio sugerido | Acelerador | Meses | Venta mensual | Venta total | IVA | Inicio | Final |");
            if (includeTechnicalFields)
                builder.Append(" Producto Id |");
            builder.AppendLine();
            builder.Append("|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|---|");
            if (includeTechnicalFields)
                builder.Append(" --- |");
            builder.AppendLine();
        }
        else
        {
            builder.AppendLine("| # | Tipo | Producto | Cant. | Venta mensual | Venta total | IVA |");
            builder.AppendLine("|---:|---|---|---:|---:|---:|---|");
        }

        for (var index = 0; index < lineItems.Count; index++)
        {
            var item = lineItems[index];
            if (includeCommercialFields)
            {
                builder.Append("| ");
                builder.Append(index + 1);
                builder.Append(" | ");
                builder.Append(FormatMarkdownCell(item.Tipo));
                builder.Append(" | ");
                builder.Append(FormatMarkdownCell(item.ProductoNombre, maxProductNameLength));
                builder.Append(" | ");
                builder.Append(FormatQuantityForNotification(item.Cantidad));
                builder.Append(" | ");
                builder.Append(FormatMoneyForNotification(item.CostoUnd));
                builder.Append(" | ");
                builder.Append(FormatMoneyForNotification(item.VentaUnd));
                builder.Append(" | ");
                builder.Append(FormatPercentForNotification(item.MargenPorcentaje));
                builder.Append(" | ");
                builder.Append(FormatMoneyForNotification(item.SuggestedRetailPrice));
                builder.Append(" | ");
                builder.Append(FormatDecimalForNotification(item.Acelerador));
                builder.Append(" | ");
                builder.Append(item.DuracionMeses.ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(FormatMoneyForNotification(item.VentaMensual));
                builder.Append(" | ");
                builder.Append(FormatMoneyForNotification(item.VentaTotal));
                builder.Append(" | ");
                builder.Append(item.TieneIva ? "Si" : "No");
                builder.Append(" | ");
                builder.Append(FormatMarkdownCell(item.Inicio));
                builder.Append(" | ");
                builder.Append(FormatMarkdownCell(item.Final));
                builder.Append(" |");
                if (includeTechnicalFields)
                {
                    builder.Append(' ');
                    builder.Append(FormatMarkdownCell(item.ProductoId));
                    builder.Append(" |");
                }
                builder.AppendLine();
                continue;
            }

            builder.Append("| ");
            builder.Append(index + 1);
            builder.Append(" | ");
            builder.Append(FormatMarkdownCell(item.Tipo));
            builder.Append(" | ");
            builder.Append(FormatMarkdownCell(item.ProductoNombre, maxProductNameLength));
            builder.Append(" | ");
            builder.Append(FormatQuantityForNotification(item.Cantidad));
            builder.Append(" | ");
            builder.Append(FormatMoneyForNotification(item.VentaMensual));
            builder.Append(" | ");
            builder.Append(FormatMoneyForNotification(item.VentaTotal));
            builder.Append(" | ");
            builder.Append(item.TieneIva ? "Si" : "No");
            builder.AppendLine(" |");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildProvisioningLineItemsHtmlTable(IReadOnlyList<ProvisioningFlowLinePayload> lineItems)
    {
        if (lineItems.Count == 0)
            return "<p style=\"margin:0;color:#52616b;\">Sin lineas.</p>";

        var builder = new StringBuilder(lineItems.Count * 220);
        builder.Append("<table style=\"border-collapse:collapse;width:100%;font-size:13px;\">");
        builder.Append("<thead><tr style=\"background:#102a43;color:#fff;\">");
        foreach (var header in new[] { "#", "Tipo", "Producto", "Cant.", "Venta und.", "Meses", "Venta mensual", "Venta total", "IVA" })
        {
            builder.Append("<th style=\"padding:9px 10px;border:1px solid #bcccdc;text-align:left;\">");
            builder.Append(WebUtility.HtmlEncode(header));
            builder.Append("</th>");
        }
        builder.Append("</tr></thead><tbody>");

        for (var index = 0; index < lineItems.Count; index++)
        {
            var item = lineItems[index];
            builder.Append("<tr>");
            AppendHtmlCell(builder, (index + 1).ToString(CultureInfo.InvariantCulture), alignRight: true);
            AppendHtmlCell(builder, item.Tipo);
            AppendHtmlCell(builder, item.ProductoNombre);
            AppendHtmlCell(builder, FormatQuantityForNotification(item.Cantidad), alignRight: true);
            AppendHtmlCell(builder, FormatMoneyForNotification(item.VentaUnd), alignRight: true);
            AppendHtmlCell(builder, item.DuracionMeses.ToString(CultureInfo.InvariantCulture), alignRight: true);
            AppendHtmlCell(builder, FormatMoneyForNotification(item.VentaMensual), alignRight: true);
            AppendHtmlCell(builder, FormatMoneyForNotification(item.VentaTotal), alignRight: true);
            AppendHtmlCell(builder, item.TieneIva ? "Si" : "No");
            builder.Append("</tr>");
        }

        builder.Append("</tbody></table>");
        return builder.ToString();
    }

    private static void AppendHtmlCell(StringBuilder builder, string? value, bool alignRight = false)
    {
        builder.Append("<td style=\"padding:8px 10px;border:1px solid #d9e2ec;vertical-align:top;");
        if (alignRight)
            builder.Append("text-align:right;white-space:nowrap;");
        builder.Append("\">");
        builder.Append(WebUtility.HtmlEncode(FirstNonEmpty(value, "-")));
        builder.Append("</td>");
    }

    private static string FormatMarkdownCell(string? value, int? maxLength = null)
    {
        var compact = CompactWhitespace(value);
        if (maxLength.HasValue)
            compact = TrimTextForDescription(compact, maxLength.Value);

        return string.IsNullOrWhiteSpace(compact)
            ? "-"
            : compact.Replace("|", "/", StringComparison.Ordinal);
    }

    private static string CompactWhitespace(string? value) =>
        string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string FormatQuantityForNotification(decimal value) =>
        Round2(value).ToString("#,0.##", ColombianCulture);

    private static string FormatMoneyForNotification(decimal value) =>
        "$" + Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("N0", ColombianCulture);

    private static string FormatPercentForNotification(decimal value) =>
        Round2(value).ToString("#,0.##", ColombianCulture) + "%";

    private static string FormatDecimalForNotification(decimal value) =>
        Round2(value).ToString("#,0.##", ColombianCulture);

    private static string SerializeDetailedProvisioningLines(IReadOnlyList<ProvisioningFlowLinePayload> lineItems) =>
        JsonSerializer.Serialize(lineItems.Select(item => new
        {
            lineId = item.LineId,
            productoId = item.ProductoId,
            productoNombre = item.ProductoNombre,
            cantidad = item.Cantidad,
            number = item.Number,
            costoUnd = item.CostoUnd,
            ventaUnd = item.VentaUnd,
            margenPorcentaje = item.MargenPorcentaje,
            duracionMeses = item.DuracionMeses,
            suggestedRetailPrice = item.SuggestedRetailPrice,
            acelerador = item.Acelerador,
            ventaMensual = item.VentaMensual,
            ventaTotal = item.VentaTotal,
            tieneIva = item.TieneIva,
            tipo = item.Tipo,
            requiereProrrateo = item.RequiereProrrateo,
            inicio = item.Inicio,
            final = item.Final
        }), ProvisioningDescriptionJsonOptions);

    private static string BuildProvisioningDescriptionWithLineBudget(
        string headerText,
        IReadOnlyList<ProvisioningFlowLinePayload> lineItems)
    {
        var includedLines = new List<ProvisioningFlowLinePayload>();
        var lastAcceptedDescription = BuildProvisioningDescriptionText(
            headerText,
            BuildProvisioningLineItemsMarkdownTable(includedLines, maxProductNameLength: 30, includeCommercialFields: false, includeTechnicalFields: false),
            $"Lineas incluidas en descripcion: 0/{lineItems.Count}");

        if (!FitsProvisioningDescriptionLimit(lastAcceptedDescription))
            return TruncateProvisioningDescription(lastAcceptedDescription);

        foreach (var item in lineItems)
        {
            includedLines.Add(item);

            var candidate = BuildProvisioningDescriptionText(
                headerText,
                BuildProvisioningLineItemsMarkdownTable(includedLines, maxProductNameLength: 30, includeCommercialFields: false, includeTechnicalFields: false),
                $"Lineas incluidas en descripcion: {includedLines.Count}/{lineItems.Count}");
            if (FitsProvisioningDescriptionLimit(candidate))
            {
                lastAcceptedDescription = candidate;
                continue;
            }

            includedLines.RemoveAt(includedLines.Count - 1);
            break;
        }

        return FitsProvisioningDescriptionLimit(lastAcceptedDescription)
            ? lastAcceptedDescription
            : TruncateProvisioningDescription(lastAcceptedDescription);
    }

    private static bool FitsProvisioningDescriptionLimit(string value) =>
        value.Length <= ProvisioningDescriptionMaxLength
        && JsonSerializer.Serialize(value).Length <= ProvisioningDescriptionMaxLength;

    private static string TruncateProvisioningDescription(string value)
    {
        var truncated = TruncateTextForDescription(value, ProvisioningDescriptionMaxLength);
        if (FitsProvisioningDescriptionLimit(truncated))
            return truncated;

        var low = 0;
        var high = Math.Min(value.Length, ProvisioningDescriptionMaxLength);
        var best = "";
        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var candidate = TruncateTextForDescription(value, mid);
            if (FitsProvisioningDescriptionLimit(candidate))
            {
                best = candidate;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return best;
    }

    private static string TrimTextForDescription(string value, int? maxLength) =>
        maxLength.HasValue
            ? TruncateTextForDescription(value, maxLength.Value)
            : value;

    private static string TruncateTextForDescription(string value, int maxLength)
    {
        if (maxLength <= 0)
            return "";

        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        if (maxLength <= 3)
            return value[..maxLength];

        return value[..(maxLength - 3)].TrimEnd() + "...";
    }

    private static string ResolveDealTypeLabel(ProvisioningScenarioContext? scenario)
    {
        return ResolveDealTypeValue(scenario) switch
        {
            0 => "ClienteNuevo",
            1 => "CrossSale",
            2 => "Renovacion 1 vez",
            3 => "Renovacion 2 veces",
            4 => "Renovacion 3 veces o mas",
            _ => "ClienteNuevo"
        };
    }

    private static int ResolveDealTypeValue(ProvisioningScenarioContext? scenario)
    {
        if (scenario?.RequiresProration == true)
            return (int)DealType.CrossSale;

        if (scenario is not null && Enum.IsDefined(typeof(DealType), scenario.DealTypeValue))
            return scenario.DealTypeValue;

        return (int)DealType.ClienteNuevo;
    }

    private static int ResolveProvisioningContractKindCode(ProvisioningAprovisionamiento? aprovisionamiento)
    {
        var rawCode = aprovisionamiento?.TipoContratoCode?.Trim();
        if (int.TryParse(rawCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCode)
            && parsedCode is ProvisioningContractKindNewBusinessValue or ProvisioningContractKindRenewalValue)
        {
            return parsedCode;
        }

        var normalizedLabel = NormalizeContractKindToken(aprovisionamiento?.TipoContratoLabel);
        return normalizedLabel switch
        {
            "negocionuevo" or "nuevo" => ProvisioningContractKindNewBusinessValue,
            "renovacion" or "renovación" or "contratoexistente" => ProvisioningContractKindRenewalValue,
            _ => 0
        };
    }

    private static string ResolveProvisioningContractKindLabel(int contractKindCode, ProvisioningAprovisionamiento? aprovisionamiento)
    {
        if (!string.IsNullOrWhiteSpace(aprovisionamiento?.TipoContratoLabel))
            return aprovisionamiento.TipoContratoLabel.Trim();

        return contractKindCode switch
        {
            ProvisioningContractKindNewBusinessValue => "Negocio nuevo",
            ProvisioningContractKindRenewalValue => "Renovacion",
            _ => ""
        };
    }

    private static string NormalizeContractKindToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value
            .Trim()
            .Normalize(NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .ToArray();

        return new string(normalized)
            .ToLowerInvariant()
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("_", "");
    }

    private static string NormalizeDateLikeValue(string? raw, bool preferIsoWhenPossible = false)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var trimmed = raw.Trim();
        if (!DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            && !DateTimeOffset.TryParse(trimmed, CultureInfo.GetCultureInfo("es-CO"), DateTimeStyles.AssumeUniversal, out parsed))
        {
            return trimmed;
        }

        return preferIsoWhenPossible
            ? parsed.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
            : parsed.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string FormatDecimalText(decimal value) =>
        Round2(value).ToString("0.##", CultureInfo.InvariantCulture);

    private static string? ValidateLicenseCaps(QuoteScenarioInput input)
    {
        return null;
    }

    private static string? ValidateSelectedProducts(IReadOnlyList<QuoteLineInput> lines, string actionLabel)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var productDescription = line.ProductDescription?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(productDescription))
                return $"La linea {index + 1} no tiene producto.";

            if (string.IsNullOrWhiteSpace(line.ProductId) && line.BusinessType != BusinessType.Hardware)
                return $"La linea {index + 1} debe seleccionar un producto valido de la lista antes de {actionLabel}.";
        }

        return null;
    }

    private static string? ValidateSelectedProducts(IReadOnlyList<ProvisioningLineItem> lines, string actionLabel)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var productName = line.ProductoNombre?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(productName))
                return $"La linea {index + 1} no tiene producto.";

            if (string.IsNullOrWhiteSpace(line.ProductoId) && !IsHardwareLine(line.Tipo))
                return $"La linea {index + 1} debe seleccionar un producto valido de la lista antes de {actionLabel}.";
        }

        return null;
    }

    private static bool IsHardwareLine(string? tipo) =>
        string.Equals((tipo ?? "").Trim(), BusinessType.Hardware.ToString(), StringComparison.OrdinalIgnoreCase);

    private static void NormalizeProrationRules(QuoteScenarioInput input)
    {
        if (input.RequiresProration)
            input.DealType = DealType.CrossSale;
    }

    private static void NormalizeProrationRules(ScenarioSaveRequest input)
    {
        if (input.RequiresProration)
            input.DealType = (int)DealType.CrossSale;
    }

    private static ExportLine ComputeLine(QuoteLineInput line)
    {
        var saleUnit = Round2(line.CostUnit * (1m + (line.MarginPercent / 100m)));
        var monthly = Round2(saleUnit * line.Quantity);
        var total = Round2(monthly * line.ContractMonths);

        return new ExportLine(saleUnit, monthly, total);
    }

    private static CalculatorProposalLineViewModel BuildProposalLine(ScenarioLineInput line)
    {
        var saleUnit = Round2(line.CostUnit * (1m + (line.MarginPercent / 100m)));
        var monthly = Round2(saleUnit * line.Quantity);
        var contract = Round2(monthly * line.ContractMonths);
        var monthlyVat = line.HasVat ? Round2(monthly * 0.19m) : 0m;
        var contractVat = line.HasVat ? Round2(contract * 0.19m) : 0m;

        return new CalculatorProposalLineViewModel
        {
            Front = GetProposalFront(line.BusinessType),
            Description = line.ProductDescription?.Trim() ?? "",
            Quantity = line.Quantity,
            ContractMonths = line.ContractMonths,
            UnitSale = saleUnit,
            MonthlySale = monthly,
            ContractSale = contract,
            HasVat = line.HasVat,
            MonthlyVat = monthlyVat,
            ContractVat = contractVat,
            MonthlyTotalWithVat = Round2(monthly + monthlyVat),
            ContractTotalWithVat = Round2(contract + contractVat)
        };
    }

    private static string GetProposalFront(int businessType) =>
        businessType switch
        {
            (int)BusinessType.ModernWork => "Licenciamiento",
            (int)BusinessType.Azure => "Azure",
            (int)BusinessType.Acronis => "Backup y seguridad",
            (int)BusinessType.Perpetuo => "Licenciamiento perpetuo",
            (int)BusinessType.Copiers => "Copiers",
            (int)BusinessType.Hardware => "Hardware",
            _ => "Servicios profesionales"
        };

    private static decimal Round2(decimal v) =>
        Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static int RoundWholeNumber(decimal value) =>
        (int)Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static string BuildFileName(string? scenarioName)
    {
        var safe = string.Join("_", (scenarioName ?? "Cotizacion").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safe))
            safe = "Cotizacion";
        return $"{safe}.xlsx";
    }

    private sealed class ProvisioningFlowLinePayload
    {
        public string LineId { get; set; } = "";
        public string ProductoId { get; set; } = "";
        public string ProductoNombre { get; set; } = "";
        public decimal Cantidad { get; set; }
        public decimal Number { get; set; }
        public decimal CostoUnd { get; set; }
        public decimal VentaUnd { get; set; }
        public decimal MargenPorcentaje { get; set; }
        public int DuracionMeses { get; set; }
        public decimal SuggestedRetailPrice { get; set; }
        public decimal Acelerador { get; set; }
        public decimal VentaMensual { get; set; }
        public decimal VentaTotal { get; set; }
        public bool TieneIva { get; set; }
        public string Tipo { get; set; } = "";
        public bool RequiereProrrateo { get; set; }
        public string Inicio { get; set; } = "";
        public string Final { get; set; } = "";
    }

    private static string BuildDiagnosticMessage(Exception ex)
    {
        var messages = new List<string>();

        for (var current = ex; current is not null && messages.Count < 3; current = current.InnerException)
        {
            var message = CompactDiagnosticMessage(current.Message);
            if (string.IsNullOrWhiteSpace(message))
                continue;

            if (messages.Contains(message, StringComparer.OrdinalIgnoreCase))
                continue;

            messages.Add(message);
        }

        return messages.Count == 0
            ? "No se recibio detalle adicional del backend."
            : string.Join(" | ", messages);
    }

    private static string CompactDiagnosticMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var compact = string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return compact.Length > 500
            ? $"{compact[..497]}..."
            : compact;
    }

    private sealed record ExportLine(decimal SaleUnit, decimal Monthly, decimal Total);
}
