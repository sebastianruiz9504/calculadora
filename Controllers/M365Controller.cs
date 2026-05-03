using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.M365;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ApiController]
[Route("api/m365")]
[ModuleAuthorize(AppModule.SoporteCloud)]
public sealed class M365Controller : ControllerBase
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private readonly IM365TenantConnectionService _m365;
    private readonly IM365SecuritySnapshotService _securitySnapshots;
    private readonly ILogger<M365Controller> _logger;

    public M365Controller(
        IM365TenantConnectionService m365,
        IM365SecuritySnapshotService securitySnapshots,
        ILogger<M365Controller> logger)
    {
        _m365 = m365;
        _securitySnapshots = securitySnapshots;
        _logger = logger;
    }

    [HttpPost("connect-url")]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public IActionResult ConnectUrl([FromBody] M365ConnectUrlRequest? request)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar el cliente para generar el consentimiento."));

        try
        {
            return Ok(_m365.BuildConnectUrl(request));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "No fue posible generar la URL de consentimiento M365.");
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado generando URL de consentimiento M365.");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible generar la URL de consentimiento de Microsoft.", ex));
        }
    }

    [HttpPost("test-connection")]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> TestConnection([FromBody] M365TestConnectionRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar el cliente o tenant para probar la conexion."));

        try
        {
            return Ok(await _m365.TestConnectionAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "No fue posible probar la conexion M365.");
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado probando conexion M365.");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible probar la conexion Microsoft 365.", ex));
        }
    }

    [HttpPost("security-snapshot")]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CollectSecuritySnapshot([FromBody] M365SecuritySnapshotRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar el cliente para recolectar el snapshot mensual."));

        try
        {
            return Ok(await _securitySnapshots.CollectMonthlySnapshotAsync(request, ct));
        }
        catch (M365PersistenceConfigurationException ex)
        {
            _logger.LogWarning(ex, "Persistencia M365 no configurada para snapshot de seguridad.");
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "No fue posible recolectar el snapshot mensual M365.");
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado recolectando snapshot mensual M365.");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible recolectar el snapshot mensual Microsoft 365.", ex));
        }
    }

    private object CreateErrorPayload(string message, Exception? ex = null)
    {
        var detail = ex is null ? "" : BuildExceptionDetail(ex);
        return new
        {
            message,
            detail = string.Equals(detail, message, StringComparison.Ordinal) ? "" : detail,
            traceId = HttpContext.TraceIdentifier
        };
    }

    private static string BuildExceptionDetail(Exception ex)
    {
        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (string.IsNullOrWhiteSpace(current.Message))
                continue;

            var trimmed = current.Message.Trim();
            if (!messages.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                messages.Add(trimmed);
        }

        return string.Join(" | ", messages);
    }
}
