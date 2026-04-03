using System.Globalization;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Nomina;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ServiceFilter(typeof(NominaAccessFilter))]
public sealed class LiquidacionNominasController : Controller
{
    private readonly IDataverseService _dataverse;
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";

    public LiquidacionNominasController(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var currentUser = await GetCurrentUserAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var firstDay = new DateOnly(today.Year, today.Month, 1);
        var lastDay = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        var model = new NominaPageViewModel
        {
            CurrentUser = currentUser,
            InitialPeriodKey = firstDay.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            SuggestedPaymentDateValue = lastDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        return View(model);
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Preview([FromBody] NominaPreviewRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Debes indicar el mes y la fecha de pago a revisar.");

        try
        {
            var result = await _dataverse.PreviewNominaAsync(request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible preparar la liquidacion de nomina.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Confirm([FromBody] NominaConfirmRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Debes indicar el mes y la fecha de pago a procesar.");

        try
        {
            var result = await _dataverse.ConfirmNominaAsync(request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible confirmar la liquidacion de nomina.", ex));
        }
    }

    private async Task<CurrentUserInfo> GetCurrentUserAsync(CancellationToken ct)
    {
        if (HttpContext.Items.TryGetValue(NominaAccessFilter.CurrentUserItemKey, out var cachedUser)
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
            traceId = HttpContext.TraceIdentifier,
            logs = Array.Empty<NominaProcessLogEntryDto>()
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
