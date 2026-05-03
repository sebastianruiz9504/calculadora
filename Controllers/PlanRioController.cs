using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.PlanRio;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.PlanRio)]
public class PlanRioController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";

    private readonly IDataverseService _dataverse;
    private readonly ILogger<PlanRioController> _logger;

    public PlanRioController(IDataverseService dataverse, ILogger<PlanRioController> logger)
    {
        _dataverse = dataverse;
        _logger = logger;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try
        {
            var model = await _dataverse.GetPlanRioPageAsync(ct);
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No fue posible cargar el modulo Plan Rio.");
            return View(CreateLoadErrorModel(ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Register([FromBody] PlanRioWorkoutSaveRequestDto? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes enviar el entreno a registrar."));

        try
        {
            return Ok(await _dataverse.SavePlanRioWorkoutAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible registrar el entreno.", ex));
        }
    }

    private object CreateErrorPayload(string message, Exception? ex = null)
    {
        var detail = BuildExceptionDetail(ex);
        return new
        {
            message,
            detail = string.Equals(detail, message, StringComparison.Ordinal) ? "" : detail,
            traceId = HttpContext.TraceIdentifier
        };
    }

    private static PlanRioPageViewModel CreateLoadErrorModel(Exception ex)
    {
        var detail = BuildExceptionDetail(ex);
        return new PlanRioPageViewModel
        {
            WeekLabel = "Semana no disponible",
            Workouts = Array.Empty<PlanRioWorkoutDto>(),
            Weeks = Array.Empty<PlanRioWeekDto>(),
            SourceSheet = "",
            SourcePath = "Dataverse: Plan Rio",
            SourceStatus = string.IsNullOrWhiteSpace(detail)
                ? "No fue posible cargar Plan Rio desde Dataverse."
                : $"No fue posible cargar Plan Rio desde Dataverse: {detail}"
        };
    }

    private static string BuildExceptionDetail(Exception? ex)
    {
        if (ex is null)
            return "";

        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (string.IsNullOrWhiteSpace(current.Message))
                continue;

            var trimmedMessage = current.Message.Trim();
            if (!messages.Contains(trimmedMessage, StringComparer.OrdinalIgnoreCase))
                messages.Add(trimmedMessage);
        }

        return string.Join(" | ", messages);
    }
}
