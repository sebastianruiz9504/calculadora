using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Licenciamiento;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.CruceLicenciamiento)]
public sealed class CruceLicenciamientoController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private readonly IDataverseService _dataverse;

    public CruceLicenciamientoController(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        return View(new LicenciamientoCrucePageViewModel
        {
            CurrentUser = await GetCurrentUserAsync(ct),
            DefaultYear = 0,
            DefaultMonth = 0,
            DefaultPeriodMode = "month"
        });
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Data(
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] string periodMode = "month",
        CancellationToken ct = default)
    {
        try
        {
            return Ok(await _dataverse.GetLicenciamientoCruceDashboardAsync(
                year,
                month,
                periodMode,
                ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible construir el cruce de licenciamiento.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UpdateCostContractType([FromBody] LicenciamientoUpdateContractTypeRequestDto? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Solicitud invalida."));

        try
        {
            return Ok(await _dataverse.UpdateLicenciamientoContractTypeAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible actualizar el tipo de contrato del consumo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UpdateBillingContractType([FromBody] BillingInvoicesContractTypeUpdateRequestDto? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Solicitud invalida."));

        try
        {
            return Ok(await _dataverse.UpdateBillingInvoicesContractTypeAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible actualizar el tipo de contrato de la factura.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UpdateBillingVertical([FromBody] LicenciamientoCruceUpdateBillingVerticalRequestDto? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Solicitud invalida."));

        try
        {
            return Ok(await _dataverse.UpdateLicenciamientoCruceBillingVerticalAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible actualizar la vertical de la factura.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UpdateCostAccount([FromBody] LicenciamientoCruceUpdateCostAccountRequestDto? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Solicitud invalida."));

        try
        {
            return Ok(await _dataverse.UpdateLicenciamientoCruceCostAccountAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible actualizar el Account ID del consumo.", ex));
        }
    }

    private async Task<CurrentUserInfo> GetCurrentUserAsync(CancellationToken ct) =>
        await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo();

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
