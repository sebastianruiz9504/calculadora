using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Puntajes;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ServiceFilter(typeof(PuntajesAccessFilter))]
public sealed class PuntajesController : Controller
{
    private readonly IDataverseService _dataverse;
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";

    public PuntajesController(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var currentUser = await GetCurrentUserAsync(ct);

        var model = new PuntajesPageViewModel
        {
            CurrentUser = currentUser,
            InitialFilter = ScorePeriodFilter.ThisMonth,
            FirstContractOptions = PuntajesOptionCatalog.FirstContractOptions,
            LineOptions = PuntajesOptionCatalog.LineOptions,
            VerticalOptions = PuntajesOptionCatalog.VerticalOptions,
            HasVatOptions = PuntajesOptionCatalog.HasVatOptions,
            AutoBillOptions = PuntajesOptionCatalog.AutoBillOptions,
            ProductLineOptions = PuntajesOptionCatalog.ProductLineOptions,
            ContractTypeOptions = PuntajesOptionCatalog.ContractTypeOptions
        };

        return View(model);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Records([FromQuery] string? filter, CancellationToken ct)
    {
        var parsedFilter = ScorePeriodFilterExtensions.ParseOrDefault(filter);
        var board = await _dataverse.GetScoreBoardAsync(parsedFilter, ct);
        return Json(board);
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Verify([FromBody] ScoreVerificationRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Debes enviar los datos de verificacion.");

        try
        {
            var result = await _dataverse.VerifyScoreRecordAsync(request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar la verificacion.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Detail([FromQuery] string recordId, [FromQuery] string? filter, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recordId))
            return BadRequest("Debes indicar el registro a consultar.");

        var parsedFilter = ScorePeriodFilterExtensions.ParseOrDefault(filter);

        try
        {
            var detail = await _dataverse.GetScoreVerificationDetailAsync(recordId, parsedFilter, ct);
            return Json(detail);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar el detalle de verificacion.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Recalculate([FromBody] ScoreVerificationRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Debes enviar los datos del negocio a recalcular.");

        try
        {
            var result = await _dataverse.RecalculateScoreRecordAsync(request, ct);
            return Json(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible recalcular el puntaje.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CloseMonth([FromBody] ScoreMonthCloseRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Debes indicar el periodo a cerrar.");

        try
        {
            var parsedFilter = ScorePeriodFilterExtensions.ParseOrDefault(request.Filter);
            var result = await _dataverse.CloseScoreMonthAsync(parsedFilter, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cerrar el mes.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Offer([FromQuery] string recordId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recordId))
            return BadRequest("Debes indicar el registro de la oferta.");

        try
        {
            var file = await _dataverse.DownloadScoreOfferAsync(recordId, ct);
            if (file is null)
                return NotFound("El registro no tiene una oferta disponible para descargar.");

            if (!string.IsNullOrWhiteSpace(file.RedirectUrl))
                return Redirect(file.RedirectUrl);

            return File(file.Content, file.ContentType, file.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible descargar la oferta.", ex));
        }
    }

    private async Task<CurrentUserInfo> GetCurrentUserAsync(CancellationToken ct)
    {
        if (HttpContext.Items.TryGetValue(PuntajesAccessFilter.CurrentUserItemKey, out var cachedUser)
            && cachedUser is CurrentUserInfo currentUser)
        {
            return currentUser;
        }

        return await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
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
