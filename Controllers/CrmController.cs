using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.Crm;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services.Crm;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Crm)]
public sealed class CrmController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private readonly ICrmRepository _repository;
    private readonly ICrmAccessScopeResolver _accessScopeResolver;
    private readonly ILogger<CrmController> _logger;

    public CrmController(
        ICrmRepository repository,
        ICrmAccessScopeResolver accessScopeResolver,
        ILogger<CrmController> logger)
    {
        _repository = repository;
        _accessScopeResolver = accessScopeResolver;
        _logger = logger;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index([FromQuery] CrmWorkspaceQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblemResponse();

        try
        {
            var scope = await _accessScopeResolver.ResolveAsync(query.ViewAsOwnerId, ct);
            return View(await _repository.GetWorkspaceAsync(query, scope, ct));
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
            return HandleException(ex, "No fue posible cargar el CRM.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Workspace([FromQuery] CrmWorkspaceQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblemResponse();

        try
        {
            var scope = await _accessScopeResolver.ResolveAsync(query.ViewAsOwnerId, ct);
            return Ok(await _repository.GetWorkspaceAsync(query, scope, ct));
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
            return HandleException(ex, "No fue posible actualizar la información del CRM.");
        }
    }

    [HttpGet("/Crm/Companies/{id:guid}", Name = "CrmCompanyDetail")]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Company(
        Guid id,
        [FromQuery] CrmDetailQuery query,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblemResponse();

        try
        {
            var scope = await _accessScopeResolver.ResolveAsync(query.ViewAsOwnerId, ct);
            var model = await _repository.GetCompanyDetailAsync(id.ToString("D"), query, scope, ct);
            return View(model);
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
            return HandleException(ex, "No fue posible cargar la hoja de vida de la empresa.");
        }
    }

    [HttpGet("/Crm/Contacts/{id:guid}", Name = "CrmContactDetail")]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Contact(
        Guid id,
        [FromQuery] CrmDetailQuery query,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblemResponse();

        try
        {
            var scope = await _accessScopeResolver.ResolveAsync(query.ViewAsOwnerId, ct);
            var model = await _repository.GetContactDetailAsync(id.ToString("D"), query, scope, ct);
            return View(model);
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
            return HandleException(ex, "No fue posible cargar la hoja de vida del contacto.");
        }
    }

    [HttpGet("/Crm/Deals/{id:guid}", Name = "CrmDealDetail")]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Deal(
        Guid id,
        [FromQuery] CrmDetailQuery query,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblemResponse();

        try
        {
            var scope = await _accessScopeResolver.ResolveAsync(query.ViewAsOwnerId, ct);
            var model = await _repository.GetDealDetailAsync(id.ToString("D"), query, scope, ct);
            return View(model);
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
            return HandleException(ex, "No fue posible cargar la hoja de vida del negocio.");
        }
    }

    [HttpGet("/Crm/Activities/{id:guid}", Name = "CrmActivityDetail")]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Activity(
        Guid id,
        [FromQuery] CrmDetailQuery query,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblemResponse();

        try
        {
            var scope = await _accessScopeResolver.ResolveAsync(query.ViewAsOwnerId, ct);
            var model = await _repository.GetActivityDetailAsync(id.ToString("D"), query, scope, ct);
            return View(model);
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
            return HandleException(ex, "No fue posible cargar la hoja de vida de la actividad.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SearchCompanies(
        [FromQuery] string q,
        [FromQuery] int top = 12,
        [FromQuery] string? viewAsOwnerId = null,
        CancellationToken ct = default)
    {
        try
        {
            var scope = await _accessScopeResolver.ResolveAsync(viewAsOwnerId, ct);
            return Ok(await _repository.SearchCompaniesAsync(q, top, scope, ct));
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
            return HandleException(ex, "No fue posible buscar empresas en el CRM.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCompany(
        [FromBody] CrmCompanyCreateRequest? request,
        [FromQuery] string? viewAsOwnerId,
        CancellationToken ct)
    {
        if (request is null)
            ModelState.AddModelError("", "Envía los datos de la empresa.");
        if (!ModelState.IsValid)
            return ValidationProblemResponse();

        try
        {
            var scope = await _accessScopeResolver.ResolveAsync(viewAsOwnerId, ct);
            var saved = await _repository.CreateCompanyAsync(request!, scope, ct);
            return StatusCode(
                StatusCodes.Status201Created,
                Success("Empresa lead creada correctamente.", saved));
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
            return HandleException(ex, "No fue posible crear la empresa.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateContact(
        [FromBody] CrmContactCreateRequest? request,
        [FromQuery] string? viewAsOwnerId,
        CancellationToken ct)
    {
        if (request is null)
            ModelState.AddModelError("", "Envía los datos del contacto.");
        if (!ModelState.IsValid)
            return ValidationProblemResponse();

        try
        {
            var scope = await _accessScopeResolver.ResolveAsync(viewAsOwnerId, ct);
            var saved = await _repository.CreateContactAsync(request!, scope, ct);
            return StatusCode(
                StatusCodes.Status201Created,
                Success("Contacto creado correctamente.", saved));
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
            return HandleException(ex, "No fue posible crear el contacto.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDeal(
        [FromBody] CrmManualDealCreateRequest? request,
        [FromQuery] string? viewAsOwnerId,
        CancellationToken ct)
    {
        if (request is null)
            ModelState.AddModelError("", "Envía los datos de la oportunidad.");
        if (!ModelState.IsValid)
            return ValidationProblemResponse();

        try
        {
            var scope = await _accessScopeResolver.ResolveAsync(viewAsOwnerId, ct);
            var saved = await _repository.CreateEstimatedDealAsync(request!, scope, ct);
            return StatusCode(
                StatusCodes.Status201Created,
                Success("Oportunidad creada correctamente.", saved));
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
            return HandleException(ex, "No fue posible crear la oportunidad.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateActivity(
        [FromForm] CrmActivityCreateRequest? request,
        [FromQuery] string? viewAsOwnerId,
        CancellationToken ct)
    {
        if (request is null)
            ModelState.AddModelError("", "Envía los datos de la actividad.");
        if (!ModelState.IsValid)
            return ValidationProblemResponse();

        try
        {
            var scope = await _accessScopeResolver.ResolveAsync(viewAsOwnerId, ct);
            var saved = await _repository.CreateActivityAsync(request!, scope, ct);
            return StatusCode(
                StatusCodes.Status201Created,
                Success("Actividad registrada correctamente.", saved));
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
            return HandleException(ex, "No fue posible registrar la actividad.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDealStage(
        [FromBody] CrmDealStageChangeRequest? request,
        [FromQuery] string? viewAsOwnerId,
        CancellationToken ct)
    {
        if (request is null)
            ModelState.AddModelError("", "Envía el negocio y la nueva etapa.");
        if (!ModelState.IsValid)
            return ValidationProblemResponse();

        try
        {
            var scope = await _accessScopeResolver.ResolveAsync(viewAsOwnerId, ct);
            var saved = await _repository.ChangeDealStageAsync(request!, scope, ct);
            return Ok(Success("Etapa actualizada con su historial.", saved));
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
            return HandleException(ex, "No fue posible cambiar la etapa del negocio.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateOwner(
        [FromBody] CrmOwnerChangeRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            ModelState.AddModelError("", "Envía el registro y el nuevo propietario.");
        if (!ModelState.IsValid)
            return ValidationProblemResponse();

        try
        {
            var scope = await _accessScopeResolver.ResolveAsync(request!.ViewAsOwnerId, ct);
            var saved = await _repository.UpdateOwnerAsync(request, scope, ct);
            return Ok(Success("Propietario actualizado correctamente.", saved));
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
            return HandleException(ex, "No fue posible cambiar el propietario.");
        }
    }

    private CrmMutationResult<T> Success<T>(string message, T record) => new()
    {
        Message = message,
        Record = record,
        TraceId = HttpContext.TraceIdentifier
    };

    private IActionResult ValidationProblemResponse()
    {
        var problem = new ValidationProblemDetails(ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "La solicitud del CRM no es válida.",
            Detail = "Revisa los campos indicados e intenta nuevamente.",
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return BadRequest(problem);
    }

    private IActionResult HandleException(Exception exception, string fallbackMessage)
    {
        var (status, title, detail, logLevel) = exception switch
        {
            CrmValidationException validation =>
                (StatusCodes.Status400BadRequest, "La solicitud del CRM no es válida.", validation.Message, LogLevel.Information),
            CrmNotFoundException notFound =>
                (StatusCodes.Status404NotFound, "Registro no encontrado.", notFound.Message, LogLevel.Information),
            CrmConflictException conflict =>
                (StatusCodes.Status409Conflict, "La operación no se puede completar.", conflict.Message, LogLevel.Information),
            CrmAccessDeniedException denied =>
                (StatusCodes.Status403Forbidden, "Acceso denegado.", denied.Message, LogLevel.Warning),
            CrmDataverseException dataverse when dataverse.StatusCode == StatusCodes.Status403Forbidden =>
                (StatusCodes.Status403Forbidden, "Acceso denegado.", "No tienes permisos para consultar este registro.", LogLevel.Warning),
            CrmDataverseException =>
                (StatusCodes.Status502BadGateway, "Dataverse no completó la operación.", fallbackMessage, LogLevel.Error),
            _ =>
                (StatusCodes.Status500InternalServerError, "Error inesperado en el CRM.", fallbackMessage, LogLevel.Error)
        };

        _logger.Log(
            logLevel,
            exception,
            "{Message} TraceId: {TraceId}",
            fallbackMessage,
            HttpContext.TraceIdentifier);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return StatusCode(status, problem);
    }
}
