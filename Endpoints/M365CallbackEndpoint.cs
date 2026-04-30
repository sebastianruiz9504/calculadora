using System.Net;
using CotizadorInterno.Web.Models.M365;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CotizadorInterno.Web.Endpoints;

public static class M365CallbackEndpoint
{
    public static RouteHandlerBuilder MapM365CallbackEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints
            .MapGet("/api/m365/callback", HandleCallbackAsync)
            .AllowAnonymous();
    }

    private static async Task<IResult> HandleCallbackAsync(
        [FromQuery] string tenant,
        [FromQuery(Name = "admin_consent")] string adminConsent,
        [FromQuery] string state,
        [FromQuery] string? scope,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        IM365TenantConnectionService m365,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("M365CallbackEndpoint");

        try
        {
            var result = await m365.HandleConsentCallbackAsync(new M365ConsentCallbackRequest
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
            logger.LogWarning(ex, "Callback M365 invalido.");
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
            logger.LogError(ex, "Error inesperado procesando callback M365.");
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

    private static IResult HtmlCallbackResult(int statusCode, string message, M365ConsentCallbackResult result)
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

        return Results.Text(html, "text/html; charset=utf-8", statusCode: statusCode);
    }
}
