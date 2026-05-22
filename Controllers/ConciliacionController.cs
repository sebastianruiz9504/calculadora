using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Conciliacion)]
public sealed class ConciliacionController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private readonly IDataverseService _dataverse;

    public ConciliacionController(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        var (resolvedYear, resolvedMonth) = ResolvePeriod(year, month);
        var model = new ConciliacionPageViewModel
        {
            CurrentUser = await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo(),
            Board = await _dataverse.GetConciliacionBoardAsync(resolvedYear, resolvedMonth, ct)
        };

        return View(model);
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UpdateClientPaymentStatus(
        [FromBody] ConciliacionClientPaymentStatusRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar el cruce a actualizar."));

        try
        {
            return Ok(await _dataverse.UpdateConciliacionClientPaymentStatusAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible actualizar el cruce.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ValidateClientPaymentPreflight(
        [FromBody] ConciliacionClientPaymentStatusRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el cruce a validar."));

        try
        {
            return Ok(await _dataverse.ValidateConciliacionClientPaymentPreflightAsync(request.RecordId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible validar el borrador pre-Siigo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SimulateClientPaymentSiigoSend(
        [FromBody] ConciliacionClientPaymentStatusRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el cruce a simular."));

        try
        {
            return Ok(await _dataverse.SimulateConciliacionClientPaymentSiigoSendAsync(request.RecordId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible simular el envio a Siigo.", ex));
        }
    }

    private static (int Year, int Month) ResolvePeriod(int? year, int? month)
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, MonthlyFinancialReconciliationHostedService.ResolveTimeZone("SA Pacific Standard Time"));
        var resolvedYear = year.GetValueOrDefault(now.Year);
        var resolvedMonth = month.GetValueOrDefault(now.Month);
        if (resolvedMonth is < 1 or > 12)
            resolvedMonth = now.Month;
        if (resolvedYear < 2020)
            resolvedYear = now.Year;

        return (resolvedYear, resolvedMonth);
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
