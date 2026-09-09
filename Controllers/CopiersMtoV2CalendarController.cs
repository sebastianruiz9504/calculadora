using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Dashboard)]
[AuthorizeForScopes(ScopeKeySection = "Dataverse:DelegatedScope")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class CopiersMtoV2CalendarController(
    ICopiersMtoV2CalendarService service,
    ILogger<CopiersMtoV2CalendarController> logger) : Controller
{
    [HttpGet]
    public Task<IActionResult> Bootstrap(CancellationToken ct) => ReadAsync(
        async () => Ok(await service.BootstrapAsync(ct)));

    [HttpGet]
    public Task<IActionResult> Week(string technicianId, string weekStart, CancellationToken ct) => ReadAsync(
        async () => Ok(await service.WeekAsync(technicianId, weekStart, ct)));

    [HttpGet]
    public Task<IActionResult> Detail(string id, CancellationToken ct) => ReadAsync(
        async () => Ok(await service.DetailAsync(id, ct)));

    [HttpGet]
    public Task<IActionResult> Evidence(string id, string evidenceKey, CancellationToken ct) => ReadAsync(async () =>
    {
        var evidence = await service.EvidenceAsync(id, evidenceKey, ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
        // Inline only the validated PDF/image bytes, never a Dataverse URL or HTML attachment.
        var disposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("inline")
        {
            FileNameStar = evidence.FileName
        };
        Response.Headers.ContentDisposition = disposition.ToString();
        return File(evidence.Content, evidence.ContentType);
    });

    private async Task<IActionResult> ReadAsync(Func<Task<IActionResult>> action)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Pragma = "no-cache";
        try { return await action(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(new { message = "No encontramos ese mantenimiento o adjunto finalizado." }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "No fue posible consultar el calendario interno de MTO V2.");
            return StatusCode(502, new { message = "No fue posible cargar el mantenimiento firmado. Intenta nuevamente." });
        }
    }
}
