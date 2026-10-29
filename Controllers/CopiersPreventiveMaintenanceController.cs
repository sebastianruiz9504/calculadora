using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.Copiers;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Dashboard)]
[ModuleAuthorize(AppModule.Copiers)]
[AuthorizeForScopes(ScopeKeySection = "Dataverse:DelegatedScope")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class CopiersPreventiveMaintenanceController(
    IDataverseService dataverse, ILogger<CopiersPreventiveMaintenanceController> logger) : Controller
{
    [HttpGet]
    public Task<IActionResult> Board(string? period, CancellationToken ct) => ExecuteAsync(
        async () => Ok(await dataverse.GetCopiersPreventiveMaintenanceBoardAsync(period, ct)));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Frequency([FromBody] CopiersPreventiveMaintenanceFrequencyUpdateRequestDto? request, CancellationToken ct) => ExecuteAsync(
        async () => request is null ? BadRequest(new { message = "Selecciona un cliente y su periodicidad." })
            : Ok(await dataverse.UpdateCopiersPreventiveMaintenanceFrequencyAsync(request, ct)));

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException) { return StatusCode(403, new { message = "No tienes permisos para esta operación." }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception ex)
        {
            logger.LogError(ex, "No fue posible consultar o actualizar el panel preventivo.");
            return StatusCode(500, new { message = "No fue posible cargar el estado de los mantenimientos. Intenta actualizar el panel." });
        }
    }
}
