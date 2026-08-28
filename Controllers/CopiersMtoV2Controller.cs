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

    public CopiersMtoV2Controller(
        IDataverseService dataverse,
        ICopiersMaintenanceV2Service service,
        IOptions<CopiersMaintenanceV2Options> options,
        IOptions<CopiersMaintenanceV2DataverseOptions> dataverseOptions,
        ILogger<CopiersMtoV2Controller> logger)
    {
        _dataverse = dataverse;
        _service = service;
        _options = options.Value;
        _dataverseOptions = dataverseOptions.Value;
        _logger = logger;
    }

    [HttpGet]
    [AuthorizeForScopes(ScopeKeySection = DataverseScopeConfigurationKey)]
    public IActionResult Index() => View();

    [HttpGet]
    [AuthorizeForScopes(ScopeKeySection = DataverseScopeConfigurationKey)]
    public async Task<IActionResult> Bootstrap(CancellationToken ct)
    {
        if (!_options.PilotEnabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, Error("El piloto de MTO Firmado V2 aún no está habilitado.", code: "pilot_disabled"));
        try
        {
            var currentUserTask = _dataverse.GetCurrentUserAsync(ct);
            var equipmentTask = _dataverse.GetCopiersEquipmentDashboardAsync(ct);
            await Task.WhenAll(currentUserTask, equipmentTask);
            var currentUser = await currentUserTask ?? new CurrentUserInfo();
            var dashboard = await equipmentTask;
            EnsureTechnicianPilotAccess(currentUser);

            var clients = dashboard.ClientSummaries
                .Where(item => Guid.TryParse(item.ClientId, out _) && !string.IsNullOrWhiteSpace(item.ClientName))
                .Where(item => IsClientAllowed(item.ClientId))
                .GroupBy(item => NormalizeGuidForComparison(item.ClientId), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.ClientName, StringComparer.OrdinalIgnoreCase)
                .Select(item => new CopiersMtoV2ClientOptionDto
                {
                    Id = item.ClientId,
                    Name = item.ClientName,
                    ContactName = item.ContactName,
                    Email = item.Email
                })
                .ToList();
            var equipment = dashboard.EquipmentRows
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
            ValidateIdempotencyHeader(request.SubmissionKey);
            var currentUser = await _dataverse.GetCurrentUserAsync(ct)
                ?? throw new InvalidOperationException("No fue posible identificar al técnico autenticado.");
            EnsureTechnicianPilotAccess(currentUser);
            var dashboard = await _dataverse.GetCopiersEquipmentDashboardAsync(ct);
            var draftInput = BuildAuthoritativeDraftRequest(request, dashboard);
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

            var draft = await _service.CreateOrGetDraftAsync(draftInput, actor, ct);
            if (draft.ReusedExisting
                && draft.State is CopiersMaintenanceV2WorkflowState.Draft or CopiersMaintenanceV2WorkflowState.Failed)
            {
                draft = await _service.SaveDraftAsync(new CopiersMaintenanceV2DraftUpdateRequestDto
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
            var result = await _service.FinalizeMultipartAsync(request, actor, ct);
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
        CopiersEquipmentDashboardDto dashboard)
    {
        var clientId = RequiredFormGuid("ClientId", "El cliente");
        var client = dashboard.ClientSummaries.FirstOrDefault(item => SameGuid(item.ClientId, clientId))
            ?? throw new CopiersMaintenanceV2ValidationException("client_not_found", "El cliente ya no está disponible en Copiers.");
        if (!IsClientAllowed(client.ClientId))
            throw new UnauthorizedAccessException("El cliente no pertenece al alcance autorizado del piloto MTO V2.");
        if (string.IsNullOrWhiteSpace(client.Email))
            throw new CopiersMaintenanceV2ValidationException("client_email_missing", "El cliente no tiene correo en Copiers. Actualízalo antes de enviar el reporte.");

        var submittedEquipmentId = FormValue("EquipmentId");
        var submittedSerial = FormValue("EquipmentSerial");
        string equipmentId;
        string equipmentSerial;
        if (Guid.TryParse(submittedEquipmentId, out var parsedEquipmentId) && parsedEquipmentId != Guid.Empty)
        {
            var equipment = dashboard.EquipmentRows.FirstOrDefault(item => SameGuid(item.RecordId, parsedEquipmentId.ToString("D")))
                ?? throw new CopiersMaintenanceV2ValidationException("equipment_not_found", "El equipo ya no está disponible en Copiers.");
            if (equipment.InStock || string.IsNullOrWhiteSpace(equipment.Serial))
                throw new CopiersMaintenanceV2ValidationException("equipment_not_serviceable", "El equipo seleccionado no está asignado o no tiene serial válido.");
            if (!SameGuid(equipment.ClientId, clientId))
                throw new CopiersMaintenanceV2ValidationException("equipment_client_mismatch", "El equipo seleccionado no pertenece al cliente.");
            equipmentId = equipment.RecordId;
            equipmentSerial = equipment.Serial;
        }
        else
        {
            var externalOrTypedSerial = RequiredFormText("EquipmentSerial", "el serial del equipo", 200);
            var knownMatches = dashboard.EquipmentRows
                .Where(item => string.Equals(item.Serial?.Trim(), externalOrTypedSerial, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (knownMatches.Count > 1)
                throw new CopiersMaintenanceV2ValidationException("equipment_serial_ambiguous", "El serial coincide con más de un equipo; selecciónalo desde el catálogo.");
            if (knownMatches.Count == 1)
            {
                var knownEquipment = knownMatches[0];
                if (knownEquipment.InStock || string.IsNullOrWhiteSpace(knownEquipment.Serial))
                    throw new CopiersMaintenanceV2ValidationException("equipment_not_serviceable", "El serial corresponde a un equipo en inventario y no puede tratarse como externo.");
                if (!SameGuid(knownEquipment.ClientId, clientId))
                    throw new CopiersMaintenanceV2ValidationException("equipment_client_mismatch", "El equipo indicado pertenece a otro cliente.");
                equipmentId = knownEquipment.RecordId;
                equipmentSerial = knownEquipment.Serial;
            }
            else
            {
                equipmentId = "";
                equipmentSerial = externalOrTypedSerial;
            }
        }

        var serviceDateRaw = RequiredFormText("ServiceDate", "la fecha del servicio", 10);
        if (!DateOnly.TryParseExact(serviceDateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var serviceDate))
            throw new CopiersMaintenanceV2ValidationException("service_date_invalid", "La fecha del servicio no es válida.");
        var maintenanceType = int.TryParse(FormValue("MaintenanceTypeValue"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedType)
            ? parsedType
            : (int?)null;

        return new CopiersMaintenanceV2DraftRequestDto
        {
            SubmissionKey = request.SubmissionKey,
            ClientId = client.ClientId,
            ClientName = client.ClientName,
            CustomerContactName = RequiredFormText("CustomerContactName", "la persona que atiende", 160),
            CustomerEmail = client.Email.Trim(),
            EquipmentId = equipmentId,
            EquipmentSerial = equipmentSerial,
            Title = RequiredFormText("Title", "el título del reporte", 250),
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

