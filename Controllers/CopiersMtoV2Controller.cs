using System.Globalization;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Copiers)]
public sealed class CopiersMtoV2Controller : Controller
{
    private const string DataverseScopeConfigurationKey = "Dataverse:DelegatedScope";

    private readonly IDataverseService _dataverse;
    private readonly ICopiersMaintenanceV2Service _service;
    private readonly CopiersMaintenanceV2Options _options;
    private readonly CopiersMaintenanceV2DataverseOptions _dataverseOptions;
    private readonly ILogger<CopiersMtoV2Controller> _logger;
    private readonly ICopiersMtoV2CounterService? _counters;
    private readonly CopiersActivityV2Runtime? _activities;

    public CopiersMtoV2Controller(
        IDataverseService dataverse,
        ICopiersMaintenanceV2Service service,
        IOptions<CopiersMaintenanceV2Options> options,
        IOptions<CopiersMaintenanceV2DataverseOptions> dataverseOptions,
        ILogger<CopiersMtoV2Controller> logger,
        ICopiersMtoV2CounterService? counters = null,
        CopiersActivityV2Runtime? activities = null)
    {
        _dataverse = dataverse;
        _service = service;
        _options = options.Value;
        _dataverseOptions = dataverseOptions.Value;
        _logger = logger;
        _counters = counters;
        _activities = activities;
    }

    [HttpGet]
    [AuthorizeForScopes(ScopeKeySection = DataverseScopeConfigurationKey)]
    public IActionResult Index() => View();

    [HttpGet]
    [AuthorizeForScopes(ScopeKeySection = DataverseScopeConfigurationKey)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Equipment(string clientId, string activityKind, CancellationToken ct)
    {
        try
        {
            EnsureTechnicianPilotAccess(await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo());
            if (!Guid.TryParse(clientId, out var id) || id == Guid.Empty || !IsClientAllowed(clientId)) return Forbid();
            activityKind = NormalizeActivityKind(activityKind);
            var clients = await _dataverse.GetCopiersMtoV2ClientsAsync(ct);
            if (!clients.Any(x => SameGuid(x.Id, clientId))) return Forbid();
            var all = await _dataverse.GetCopiersMtoV2EquipmentAsync(ct);
            var items = all.Where(x => activityKind == "movement"
                    ? (x.InStock || IsClientAllowed(x.ClientId))
                    : SameGuid(x.ClientId, clientId) && !x.InStock)
                .Where(x => Guid.TryParse(x.RecordId, out _) && !string.IsNullOrWhiteSpace(x.Serial))
                .Select(x => new CopiersMtoV2EquipmentOptionDto { Id=x.RecordId, Serial=x.Serial, ClientId=x.ClientId,
                    ClientName=x.InStock ? "Inventario" : x.ClientName, Reference=x.Reference }).ToArray();
            var allowExternalEquipment = activityKind == "maintenance" && items.Length == 0 && _activities is not null
                && await _activities.ClientHasNoEquipmentAsync(clientId, ct);
            return Ok(new { items, allowExternalEquipment });
        }
        catch (MicrosoftIdentityWebChallengeUserException) { throw; }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (CopiersMaintenanceV2ValidationException ex) { return BadRequest(Error(ex.Message, code:ex.Code)); }
        catch (Exception ex) { _logger.LogError(ex, "Error cargando equipos de la atención V2."); return StatusCode(502, Error("No fue posible cargar los equipos. Reintenta la búsqueda.")); }
    }

    [HttpGet]
    [AuthorizeForScopes(ScopeKeySection = DataverseScopeConfigurationKey)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Supplies(CancellationToken ct)
    {
        EnsureTechnicianPilotAccess(await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo());
        if (!_options.ActivitiesEnabled) return NotFound();
        // Lookup is read-only; the legacy inventory GET synchronizes stock statuses.
        var rows = await _dataverse.GetCopiersMtoV2TonerSuppliesAsync(ct);
        return Ok(new { items = rows.Where(x => x.Quantity > 0).Select(x => new { id=x.Id, name=x.Label, quantity=x.Quantity }) });
    }

    [HttpGet]
    [AuthorizeForScopes(ScopeKeySection = DataverseScopeConfigurationKey)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Status(string recordId, string activityKind, CancellationToken ct)
    {
        try
        {
            var actor = await _dataverse.GetCurrentUserAsync(ct) ?? throw new UnauthorizedAccessException();
            EnsureTechnicianPilotAccess(actor);
            if (_activities is null) return StatusCode(503);
            return Ok(await _activities.StatusAsync(recordId, NormalizeActivityKind(activityKind), actor.SystemUserId, ct));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (CopiersMaintenanceV2ValidationException ex) { return BadRequest(Error(ex.Message, code:ex.Code)); }
        catch (CopiersMaintenanceV2PersistenceException ex) { return StatusCode(502, Error(ex.Message)); }
    }

    [HttpGet]
    [AuthorizeForScopes(ScopeKeySection = DataverseScopeConfigurationKey)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CounterLatest(string clientId, string equipmentId, CancellationToken ct)
    {
        try
        {
            EnsureTechnicianPilotAccess(await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo());
            if (!IsClientAllowed(clientId)) return Forbid();
            if (_counters is null) throw new InvalidOperationException("El servicio de contadores no está disponible.");
            return Ok(await _counters.GetLatestAsync(clientId, equipmentId, ct));
        }
        catch (MicrosoftIdentityWebChallengeUserException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (CopiersMaintenanceV2ValidationException ex) { return BadRequest(Error(ex.Message, code: ex.Code)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No fue posible consultar la última lectura del equipo en MTO V2.");
            return StatusCode(502, Error("No fue posible consultar los contadores. Reintenta la carga antes de firmar."));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(ScopeKeySection = DataverseScopeConfigurationKey)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Bootstrap(CancellationToken ct)
    {
        try
        {
            var currentUser = await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
            EnsureTechnicianPilotAccess(currentUser);
            var equipmentTask = _dataverse.GetCopiersMtoV2EquipmentAsync(ct);
            var clientsTask = _dataverse.GetCopiersMtoV2ClientsAsync(ct);
            await Task.WhenAll(equipmentTask, clientsTask);

            var clients = (await clientsTask)
                .Where(item => Guid.TryParse(item.Id, out _) && !string.IsNullOrWhiteSpace(item.Name))
                .Where(item => IsClientAllowed(item.Id))
                .GroupBy(item => NormalizeGuidForComparison(item.Id), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var equipment = (await equipmentTask)
                .Where(item => !item.InStock
                    && Guid.TryParse(item.RecordId, out _)
                    && Guid.TryParse(item.ClientId, out _)
                    && !string.IsNullOrWhiteSpace(item.Serial)
                    && IsClientAllowed(item.ClientId))
                .OrderBy(item => item.ClientName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Serial, StringComparer.OrdinalIgnoreCase)
                .Select(item => new CopiersMtoV2EquipmentOptionDto
                {
                    Id = item.RecordId,
                    Serial = item.Serial,
                    ClientId = item.ClientId,
                    ClientName = item.ClientName,
                    Reference = item.Reference
                })
                .ToList();

            return Ok(new CopiersMtoV2BootstrapDto
            {
                SchemaReady = _dataverseOptions.SchemaProvisioned
                    && _dataverseOptions.FindMissingBindings().Count == 0,
                ActivitiesEnabled = _options.ActivitiesEnabled,
                TechnicianName = FirstNonEmpty(
                    currentUser.EmployeeName,
                    currentUser.EmployeeUserDisplayName,
                    currentUser.DisplayName,
                    User.Identity?.Name,
                    "Técnico"),
                TechnicianEmail = FirstNonEmpty(currentUser.EmployeeUserEmail, currentUser.Email),
                MaintenanceTypes = BuildMaintenanceTypeOptions(),
                Clients = clients,
                Equipment = equipment
            });
        }
        catch (MicrosoftIdentityWebChallengeUserException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Acceso al piloto MTO V2 rechazado.");
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "No fue posible cargar el catálogo de MTO Firmado V2.");
            return StatusCode(StatusCodes.Status500InternalServerError, Error("No fue posible cargar clientes y equipos de Copiers."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No fue posible cargar el catálogo de MTO Firmado V2.");
            return StatusCode(StatusCodes.Status500InternalServerError, Error("No fue posible cargar clientes y equipos de Copiers."));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AuthorizeForScopes(ScopeKeySection = DataverseScopeConfigurationKey)]
    public async Task<IActionResult> SaveClientEmail([FromBody] CopiersMtoV2ClientEmailRequestDto? request, CancellationToken ct)
    {
        try
        {
            if (request is null || !Guid.TryParse(request.ClientId, out var id) || id == Guid.Empty)
                return BadRequest(Error("Selecciona un cliente válido."));
            EnsureTechnicianPilotAccess(await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo());
            if (!IsClientAllowed(request.ClientId)) return Forbid();
            CopiersMaintenanceV2Validation.ValidateCustomerEmail(request.Email);
            var saved = await _dataverse.SaveCopiersMtoV2ClientEmailAsync(request.ClientId, request.Email, ct);
            return Ok(new { clientId = saved.Id, email = saved.Email });
        }
        catch (CopiersMaintenanceV2ValidationException ex) { return BadRequest(Error(ex.Message, code: ex.Code)); }
        catch (CopiersMaintenanceV2ConcurrencyException ex) { return Conflict(Error(ex.Message)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No fue posible actualizar el correo del encargado de Copiers.");
            return StatusCode(502, Error("No fue posible guardar el correo en Clientes. Verifica tus permisos e inténtalo nuevamente."));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AuthorizeForScopes(ScopeKeySection = DataverseScopeConfigurationKey)]
    [RequestSizeLimit(26214400)]
    [RequestFormLimits(MultipartBodyLengthLimit = 26214400)]
    public async Task<IActionResult> Finalize(
        [FromForm] CopiersMaintenanceV2FinalizeMultipartRequestDto? request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(Error("Debes enviar los datos del reporte firmado."));
        if (!_options.PilotEnabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, Error("El piloto de MTO Firmado V2 aún no está habilitado.", code: "pilot_disabled"));

        try
        {
            request.ActivityKind = NormalizeActivityKind(request.ActivityKind);
            if (request.ActivityKind != "maintenance" && request.FormVersion != CopiersActivityV2Bindings.FormVersion
                || request.ActivityKind == "maintenance" && request.FormVersion == CopiersActivityV2Bindings.FormVersion)
                throw new CopiersMaintenanceV2ValidationException("activity_form_mismatch", "El formato no corresponde al tipo de atención.");
            ValidateIdempotencyHeader(request.SubmissionKey);
            var currentUser = await _dataverse.GetCurrentUserAsync(ct)
                ?? throw new InvalidOperationException("No fue posible identificar al técnico autenticado.");
            EnsureTechnicianPilotAccess(currentUser);
            var dashboard = await _dataverse.GetCopiersEquipmentDashboardAsync(ct);
            var clients = await _dataverse.GetCopiersMtoV2ClientsAsync(ct);
            var clientId = FormValue("ClientId");
            var allowExternal = request.ActivityKind == "maintenance" && string.IsNullOrWhiteSpace(FormValue("EquipmentId"))
                && _activities is not null && await _activities.ClientHasNoEquipmentAsync(clientId, ct);
            var draftInput = BuildAuthoritativeDraftRequest(request, dashboard, clients, allowExternal);
            var actor = new CopiersMaintenanceV2ActorContext
            {
                SystemUserId = currentUser.SystemUserId,
                DisplayName = FirstNonEmpty(
                    currentUser.EmployeeName,
                    currentUser.EmployeeUserDisplayName,
                    currentUser.DisplayName,
                    User.Identity?.Name,
                    "Técnico"),
                Email = FirstNonEmpty(currentUser.EmployeeUserEmail, currentUser.Email)
            };

            var service = request.ActivityKind == "maintenance" ? _service
                : _activities?.CreateService() ?? throw new InvalidOperationException("El servicio de actas no está disponible.");
            var draft = await service.CreateOrGetDraftAsync(draftInput, actor, ct);
            if (draft.ReusedExisting
                && draft.State is CopiersMaintenanceV2WorkflowState.Draft or CopiersMaintenanceV2WorkflowState.Failed)
            {
                draft = await service.SaveDraftAsync(new CopiersMaintenanceV2DraftUpdateRequestDto
                {
                    RecordId = draft.RecordId,
                    SubmissionKey = draft.SubmissionKey,
                    ExpectedVersion = draft.Version,
                    ClientId = draftInput.ClientId,
                    ClientName = draftInput.ClientName,
                    CustomerContactName = draftInput.CustomerContactName,
                    CustomerEmail = draftInput.CustomerEmail,
                    EquipmentId = draftInput.EquipmentId,
                    EquipmentSerial = draftInput.EquipmentSerial,
                    Title = draftInput.Title,
                    ServiceDate = draftInput.ServiceDate,
                    MaintenanceTypeValue = draftInput.MaintenanceTypeValue
                }, actor, ct);
            }

            request.RecordId = draft.RecordId;
            request.SubmissionKey = draft.SubmissionKey;
            request.ExpectedVersion = draft.Version;
            var result = await service.FinalizeMultipartAsync(request, actor, ct);
            return Ok(result);
        }
        catch (CopiersMaintenanceV2ValidationException ex)
        {
            return BadRequest(Error(ex.Message, ex, ex.Code));
        }
        catch (CopiersMaintenanceV2ConcurrencyException ex)
        {
            return Conflict(Error(ex.Message, ex, "concurrency_conflict"));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Acceso rechazado finalizando MTO V2.");
            return Forbid();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("aún no está aprovisionado", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                Error("El esquema de MTO Firmado V2 aún no está habilitado.", code: "schema_not_ready"));
        }
        catch (CopiersMaintenanceV2PersistenceException ex)
        {
            _logger.LogError(ex, "Dataverse rechazó una operación de MTO Firmado V2 {SubmissionKey}.", request.SubmissionKey);
            return StatusCode(
                StatusCodes.Status502BadGateway,
                Error("No fue posible guardar el reporte en Dataverse. Puedes reintentar con la misma clave.", code: "dataverse_unavailable"));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Falló una operación interna finalizando MTO V2 {SubmissionKey}.", request.SubmissionKey);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                Error("No fue posible finalizar el reporte firmado. Puedes reintentar con la misma clave.", code: "finalization_failed"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No fue posible finalizar el MTO Firmado V2 {SubmissionKey}.", request.SubmissionKey);
            return StatusCode(StatusCodes.Status500InternalServerError, Error("No fue posible finalizar el reporte firmado."));
        }
    }

    private CopiersMaintenanceV2DraftRequestDto BuildAuthoritativeDraftRequest(
        CopiersMaintenanceV2FinalizeMultipartRequestDto request,
        CopiersEquipmentDashboardDto dashboard,
        IReadOnlyList<CopiersMtoV2ClientOptionDto> clients,
        bool allowExternalEquipment = false)
    {
        var clientId = RequiredFormGuid("ClientId", "El cliente");
        var client = clients.FirstOrDefault(item => SameGuid(item.Id, clientId))
            ?? throw new CopiersMaintenanceV2ValidationException("client_not_found", "El cliente ya no está disponible en Copiers.");
        if (!IsClientAllowed(client.Id))
            throw new UnauthorizedAccessException("El cliente no pertenece al alcance autorizado del piloto MTO V2.");
        if (string.IsNullOrWhiteSpace(client.Email))
            throw new CopiersMaintenanceV2ValidationException("client_email_missing", "Agrega el correo de la persona encargada de Copiers con el botón + antes de enviar.");

        var submittedEquipmentId = FormValue("EquipmentId");
        var submittedSerial = FormValue("EquipmentSerial");
        string equipmentId;
        string equipmentSerial;
        if (Guid.TryParse(submittedEquipmentId, out var parsedEquipmentId) && parsedEquipmentId != Guid.Empty)
        {
            var equipment = dashboard.EquipmentRows.FirstOrDefault(item => SameGuid(item.RecordId, parsedEquipmentId.ToString("D")))
                ?? throw new CopiersMaintenanceV2ValidationException("equipment_not_found", "El equipo ya no está disponible en Copiers.");
            if (request.ActivityKind == "movement" && !equipment.InStock && !IsClientAllowed(equipment.ClientId))
                throw new UnauthorizedAccessException("El cliente de origen no pertenece al alcance autorizado de Copiers.");
            if ((equipment.InStock && request.ActivityKind != "movement") || string.IsNullOrWhiteSpace(equipment.Serial))
                throw new CopiersMaintenanceV2ValidationException("equipment_not_serviceable", "El equipo seleccionado no está asignado o no tiene serial válido.");
            if (request.ActivityKind != "movement" && !SameGuid(equipment.ClientId, clientId))
                throw new CopiersMaintenanceV2ValidationException("equipment_client_mismatch", "El equipo seleccionado no pertenece al cliente.");
            equipmentId = equipment.RecordId;
            equipmentSerial = equipment.Serial;
        }
        else if (request.ActivityKind == "maintenance" && allowExternalEquipment)
        {
            equipmentId = "";
            equipmentSerial = RequiredFormText("EquipmentSerial", "el serial del equipo ajeno", 200);
        }
        else
        {
            throw new CopiersMaintenanceV2ValidationException("equipment_required", "Selecciona un serial de la tabla Equipos del cliente.");
        }

        var serviceDateRaw = RequiredFormText("ServiceDate", "la fecha del servicio", 10);
        if (!DateOnly.TryParseExact(serviceDateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var serviceDate))
            throw new CopiersMaintenanceV2ValidationException("service_date_invalid", "La fecha del servicio no es válida.");
        var maintenanceType = int.TryParse(FormValue("MaintenanceTypeValue"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedType)
            ? parsedType
            : (int?)null;
        if (request.ActivityKind != "maintenance") maintenanceType = request.ActivityKind == "movement"
            ? CopiersActivityV2Bindings.MovementType : CopiersActivityV2Bindings.TonerType;
        var kindLabel = request.ActivityKind == "movement" ? "Movimiento de equipo" : request.ActivityKind == "toner" ? "Entrega de tóner" : "Mantenimiento";

        return new CopiersMaintenanceV2DraftRequestDto
        {
            SubmissionKey = request.SubmissionKey,
            ClientId = client.Id,
            ClientName = client.Name,
            CustomerContactName = RequiredFormText("CustomerContactName", "la persona que atiende", 160),
            CustomerEmail = client.Email.Trim(),
            EquipmentId = equipmentId,
            EquipmentSerial = equipmentSerial,
            Title = $"{kindLabel} {serviceDate:yyyy-MM-dd} · {equipmentSerial}",
            ServiceDate = serviceDate,
            MaintenanceTypeValue = maintenanceType
        };
    }

    private void ValidateIdempotencyHeader(string submissionKey)
    {
        var header = Request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(header) || !string.Equals(header, submissionKey?.Trim(), StringComparison.Ordinal))
            throw new CopiersMaintenanceV2ValidationException("idempotency_header_invalid", "La clave idempotente del formulario no coincide.");
    }

    private string NormalizeActivityKind(string? value)
    {
        var kind = string.IsNullOrWhiteSpace(value) ? "maintenance" : value.Trim();
        if (kind is not ("maintenance" or "movement" or "toner"))
            throw new CopiersMaintenanceV2ValidationException("activity_invalid", "Selecciona un tipo de atención válido.");
        if (kind != "maintenance" && !_options.ActivitiesEnabled)
            throw new CopiersMaintenanceV2ValidationException("activities_disabled", "Las nuevas actas todavía no están habilitadas.");
        return kind;
    }

    private string RequiredFormGuid(string key, string label)
    {
        var value = FormValue(key);
        return Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed.ToString("D")
            : throw new CopiersMaintenanceV2ValidationException($"{key.ToLowerInvariant()}_invalid", $"{label} no es válido.");
    }

    private string RequiredFormText(string key, string label, int maxLength)
    {
        var value = FormValue(key);
        if (string.IsNullOrWhiteSpace(value))
            throw new CopiersMaintenanceV2ValidationException($"{key.ToLowerInvariant()}_required", $"Debes indicar {label}.");
        if (value.Length > maxLength)
            throw new CopiersMaintenanceV2ValidationException($"{key.ToLowerInvariant()}_too_long", $"{label} supera {maxLength:N0} caracteres.");
        return value;
    }

    private string FormValue(string key) => Request.Form[key].FirstOrDefault()?.Trim() ?? "";

    private IReadOnlyList<CopiersMtoV2MaintenanceTypeOptionDto> BuildMaintenanceTypeOptions()
    {
        if (_dataverseOptions.MaintenanceTypeCorrectiveValue <= 0
            || _dataverseOptions.MaintenanceTypePreventiveValue <= 0
            || _dataverseOptions.MaintenanceTypeCorrectiveValue == _dataverseOptions.MaintenanceTypePreventiveValue)
        {
            return Array.Empty<CopiersMtoV2MaintenanceTypeOptionDto>();
        }
        return new[]
        {
            new CopiersMtoV2MaintenanceTypeOptionDto { Value = _dataverseOptions.MaintenanceTypePreventiveValue, Label = "Preventivo" },
            new CopiersMtoV2MaintenanceTypeOptionDto { Value = _dataverseOptions.MaintenanceTypeCorrectiveValue, Label = "Correctivo" }
        };
    }

    private void EnsureTechnicianPilotAccess(CurrentUserInfo currentUser)
    {
        // ModuleAuthorize already enforces Copiers access. An empty optional allowlist
        // means all authorized technicians, not a disabled form.
        if (!_options.AllowedTechnicianEmails.Any(value => !string.IsNullOrWhiteSpace(value))) return;
        var email = FirstNonEmpty(currentUser.EmployeeUserEmail, currentUser.Email, User.Identity?.Name);
        var allowed = _options.AllowedTechnicianEmails
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Any(value => string.Equals(value.Trim(), email, StringComparison.OrdinalIgnoreCase));
        if (!allowed)
            throw new UnauthorizedAccessException("El técnico no está incluido en la lista autorizada del piloto MTO V2.");
    }

    private bool IsClientAllowed(string? clientId)
    {
        var configured = _options.AllowedClientIds.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return configured.Length == 0 || configured.Any(value => SameGuid(value, clientId));
    }

    private object Error(string message, Exception? exception = null, string code = "request_failed") => new
    {
        message,
        code,
        detail = exception is null || string.Equals(exception.Message, message, StringComparison.Ordinal) ? "" : exception.Message,
        traceId = HttpContext.TraceIdentifier
    };

    private static bool SameGuid(string? left, string? right) =>
        Guid.TryParse(left, out var leftGuid)
        && Guid.TryParse(right, out var rightGuid)
        && leftGuid == rightGuid;

    private static string NormalizeGuidForComparison(string value) =>
        Guid.TryParse(value, out var parsed) ? parsed.ToString("D") : value.Trim();

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))?.Trim() ?? "";
}

