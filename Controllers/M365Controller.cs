using System.Net;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.M365;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Authorization;
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
    private readonly ILogger<M365Controller> _logger;

    public M365Controller(IM365TenantConnectionService m365, ILogger<M365Controller> logger)
    {
        _m365 = m365;
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

    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string tenant,
        [FromQuery(Name = "admin_consent")] string adminConsent,
        [FromQuery] string state,
        [FromQuery] string? scope,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        CancellationToken ct)
    {
        try
        {
            var result = await _m365.HandleConsentCallbackAsync(new M365ConsentCallbackRequest
            {
                Tenant = tenant,
                AdminConsent = adminConsent,
                State = state,
                Scope = scope ?? "",
                Error = error ?? "",
                ErrorDescription = errorDescription ?? ""
            }, ct);

            return HtmlCallbackResult(result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest, result.Message, result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Callback M365 invalido.");
            return HtmlCallbackResult(
                StatusCodes.Status400BadRequest,
                ex.Message,
                new M365ConsentCallbackResult
                {
                    Success = false,
                    Message = ex.Message,
                    EstadoConexion = "Callback invalido"
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado procesando callback M365.");
            return HtmlCallbackResult(
                StatusCodes.Status500InternalServerError,
                "No fue posible guardar el consentimiento de Microsoft 365.",
                new M365ConsentCallbackResult
                {
                    Success = false,
                    Message = "No fue posible guardar el consentimiento de Microsoft 365.",
                    EstadoConexion = "Error callback"
                });
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

    private IActionResult HtmlCallbackResult(int statusCode, string message, M365ConsentCallbackResult result)
    {
        var title = result.Success ? "Consentimiento guardado" : "Consentimiento no completado";
        var html = $$"""
            <!doctype html>
            <html lang="es">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>{{WebUtility.HtmlEncode(title)}}</title>
                <style>
                    body { margin: 0; font-family: Arial, sans-serif; background: #f6f9fc; color: #17263c; }
                    main { width: min(720px, calc(100% - 32px)); margin: 48px auto; padding: 24px; border: 1px solid #d9e3ee; border-radius: 8px; background: #fff; }
                    h1 { margin: 0 0 12px; font-size: 26px; }
                    p { line-height: 1.55; }
                    dl { display: grid; grid-template-columns: 160px 1fr; gap: 8px 16px; }
                    dt { color: #5f7088; font-weight: 700; }
                    dd { margin: 0; overflow-wrap: anywhere; }
                </style>
            </head>
            <body>
                <main>
                    <h1>{{WebUtility.HtmlEncode(title)}}</h1>
                    <p>{{WebUtility.HtmlEncode(message)}}</p>
                    <dl>
                        <dt>Cliente</dt>
                        <dd>{{WebUtility.HtmlEncode(result.ClienteId)}}</dd>
                        <dt>Tenant</dt>
                        <dd>{{WebUtility.HtmlEncode(result.TenantId)}}</dd>
                        <dt>Estado</dt>
                        <dd>{{WebUtility.HtmlEncode(result.EstadoConexion)}}</dd>
                    </dl>
                </main>
            </body>
            </html>
            """;

        return new ContentResult
        {
            Content = html,
            ContentType = "text/html; charset=utf-8",
            StatusCode = statusCode
        };
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
