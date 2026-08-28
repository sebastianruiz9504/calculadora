using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.MesaAyuda;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.MesaAyuda;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.MesaAyuda)]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class MesaAyudaController : Controller
{
    private const string DataverseScope =
        "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private static readonly ConcurrentDictionary<string, byte> ActiveAudits =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IDataverseService _dataverse;
    private readonly IMesaAyudaWorkspaceService _workspace;
    private readonly IMesaAyudaAiService _ai;
    private readonly MesaAyudaOptions _options;
    private readonly ILogger<MesaAyudaController> _logger;

    public MesaAyudaController(
        IDataverseService dataverse,
        IMesaAyudaWorkspaceService workspace,
        IMesaAyudaAiService ai,
        IOptions<MesaAyudaOptions> options,
        ILogger<MesaAyudaController> logger)
    {
        _dataverse = dataverse;
        _workspace = workspace;
        _ai = ai;
        _options = options.Value;
        _logger = logger;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var currentUser = await _dataverse.GetCurrentUserAsync(ct);
        return View(new MesaAyudaPageViewModel
        {
            CurrentUserName = FirstNonEmpty(
                currentUser?.DisplayName,
                currentUser?.EmployeeName,
                currentUser?.Email,
                "Sebastian Ruiz"),
            AiConfigured = _ai.IsConfigured,
            SchemaProvisioned = _options.SchemaProvisioned
        });
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Workspace(CancellationToken ct)
    {
        try
        {
            return Ok(await _workspace.GetWorkspaceAsync(ct));
        }
        catch (MicrosoftIdentityWebChallengeUserException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No fue posible cargar la Mesa de ayuda.");
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "No fue posible cargar los casos.",
                detail: "Revisa la conexion delegada con Dataverse e intenta nuevamente.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Message(
        [FromBody] MesaAyudaMessageRequestDto? request,
        CancellationToken ct)
    {
        if (request is null)
        {
            ModelState.AddModelError(
                "",
                "Envia el mensaje que deseas registrar.");
        }
        else if (string.IsNullOrWhiteSpace(request.Content))
        {
            ModelState.AddModelError(
                nameof(request.Content),
                "El mensaje interno esta vacio.");
        }

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!_options.SchemaProvisioned)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "El chat durable aun no esta activo.",
                detail: "Provisiona el esquema confirmado de Mesa de ayuda antes de registrar mensajes.");
        }

        try
        {
            var ticket = await _workspace.GetTicketAsync(
                request!.TicketId,
                ct);
            if (ticket is null)
            {
                return NotFound(new
                {
                    message =
                        "El ticket no existe o no esta disponible en la cola autorizada."
                });
            }

            var operationKey = MesaAyudaIdempotencyPolicy.CreateOperationKey(
                ticket.RecordId,
                request.IdempotencyKey);
            var messageKey = MesaAyudaIdempotencyPolicy.Derive(
                operationKey,
                "internal-message");
            var interaction = await _workspace.CreateInternalMessageAsync(
                new MesaAyudaInternalMessageCreate
                {
                    TicketId = ticket.RecordId,
                    Content = request.Content.Trim(),
                    IdempotencyKey = messageKey,
                    ActorName = BuildActorName(User),
                    ActorAddress = BuildActorAddress(User),
                    ActorObjectId = BuildActorObjectId(User)
                },
                ct);

            return Ok(new MesaAyudaMessageResponseDto
            {
                Message = "Mensaje interno registrado en Dataverse.",
                IdempotencyKey = operationKey,
                Interaction = interaction
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
        catch (InvalidOperationException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "No fue posible registrar el mensaje.",
                detail: ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Fallo el registro de un mensaje interno para el ticket {TicketId}.",
                request?.TicketId);
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "No fue posible registrar el mensaje.",
                detail: "El chat no se modifico. Intenta nuevamente.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Analyze(
        [FromBody] MesaAyudaAnalyzeRequestDto? request,
        CancellationToken ct)
    {
        if (request is null)
        {
            ModelState.AddModelError("", "Envia el ticket que deseas auditar.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (!_options.SchemaProvisioned)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "La auditoria durable aun no esta activa.",
                detail: "Provisiona el esquema confirmado de Mesa de ayuda antes de ejecutar el auditor.");
        }

        if (!_ai.IsConfigured)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "El auditor IA aun no esta configurado.",
                detail: "Configura un endpoint y despliegue validos de Azure OpenAI con identidad administrada, o el proveedor alternativo definido para MesaAyudaAI.");
        }

        var auditKey = Guid.TryParse(request!.TicketId, out var ticketId)
            ? ticketId.ToString("D")
            : request.TicketId.Trim();
        if (!ActiveAudits.TryAdd(auditKey, 0))
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Ya existe una auditoria activa para este ticket.",
                detail: "Espera a que termine la ejecucion actual antes de iniciar otra auditoria sobre el mismo caso.");
        }

        try
        {
            var ticket = await _workspace.GetTicketAsync(request.TicketId, ct);
            if (ticket is null)
            {
                return NotFound(new
                {
                    message = "El ticket no existe o no esta disponible en la cola autorizada."
                });
            }

            var operationKey = MesaAyudaIdempotencyPolicy.CreateOperationKey(
                ticket.RecordId,
                request.IdempotencyKey);
            var resultKey = MesaAyudaIdempotencyPolicy.Derive(
                operationKey,
                "ai-result");
            var persisted = await _workspace.GetPersistedInvestigationAsync(
                resultKey,
                ct);
            if (persisted is not null)
            {
                return Ok(new MesaAyudaAnalyzeResponseDto
                {
                    Message =
                        "Auditoria recuperada desde Dataverse; no se ejecuto nuevamente el modelo.",
                    IdempotencyKey = operationKey,
                    Investigation = persisted
                });
            }

            var persistedEvents = new List<MesaAyudaTimelineEventDto>();
            var instruction = request.Instruction?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(instruction))
            {
                persistedEvents.Add(
                    await _workspace.CreateInternalMessageAsync(
                        new MesaAyudaInternalMessageCreate
                        {
                            TicketId = ticket.RecordId,
                            Content = instruction,
                            Subject = "Instruccion para la auditoria",
                            IdempotencyKey =
                                MesaAyudaIdempotencyPolicy.Derive(
                                    operationKey,
                                    "agent-instruction"),
                            ActorName = BuildActorName(User),
                            ActorAddress = BuildActorAddress(User),
                            ActorObjectId = BuildActorObjectId(User)
                        },
                        ct));
            }

            var investigation = await _ai.AnalyzeAsync(
                new MesaAyudaAiRequest
                {
                    Ticket = ticket,
                    Instruction = instruction,
                    HashedUserIdentifier = BuildSafetyIdentifier(User)
                },
                ct);
            persistedEvents.Add(
                await _workspace.SaveInvestigationAsync(
                    new MesaAyudaInvestigationCreate
                    {
                        TicketId = ticket.RecordId,
                        IdempotencyKey = resultKey,
                        Investigation = investigation
                    },
                    ct));

            return Ok(new MesaAyudaAnalyzeResponseDto
            {
                Message = "Auditoria completada. Los hallazgos requieren revision del agente antes de cualquier cambio.",
                IdempotencyKey = operationKey,
                Investigation = investigation,
                Interactions = persistedEvents
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
        catch (InvalidOperationException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "No fue posible iniciar la auditoria.",
                detail: ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Fallo la auditoria IA del ticket {TicketId}.",
                request?.TicketId);
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "El auditor IA no pudo completar el analisis.",
                detail: "No se ejecuto ningun cambio. Intenta nuevamente o revisa la configuracion del modelo.");
        }
        finally
        {
            ActiveAudits.TryRemove(auditKey, out _);
        }
    }

    private static string BuildSafetyIdentifier(ClaimsPrincipal principal)
    {
        var source = FirstNonEmpty(
            principal.FindFirstValue("oid"),
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            principal.FindFirstValue("preferred_username"),
            "mesa-ayuda-user");
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(source.ToLowerInvariant()));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static string BuildActorName(ClaimsPrincipal principal) =>
        FirstNonEmpty(
            principal.FindFirstValue("name"),
            principal.FindFirstValue(ClaimTypes.Name),
            principal.FindFirstValue("preferred_username"),
            "Agente");

    private static string BuildActorAddress(ClaimsPrincipal principal) =>
        FirstNonEmpty(
            principal.FindFirstValue("preferred_username"),
            principal.FindFirstValue(ClaimTypes.Email),
            principal.FindFirstValue("upn"));

    private static string BuildActorObjectId(ClaimsPrincipal principal) =>
        FirstNonEmpty(
            principal.FindFirstValue("oid"),
            principal.FindFirstValue(ClaimTypes.NameIdentifier));

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}
