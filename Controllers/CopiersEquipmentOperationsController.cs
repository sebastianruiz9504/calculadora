using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Copiers)]
[AuthorizeForScopes(ScopeKeySection="Dataverse:DelegatedScope")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CopiersEquipmentOperationsController(CopiersEquipmentOperationsService service, CopiersSubmissionStore submissions,
    ILogger<CopiersEquipmentOperationsController> logger) : Controller
{
    [HttpGet] public async Task<IActionResult> Index(CancellationToken ct)
    { await service.AuthorizeAsync(ct); return View(); }
    [HttpGet] public async Task<IActionResult> Bootstrap(CancellationToken ct)=>Ok(await service.BootstrapAsync(ct));
    [HttpGet] public async Task<IActionResult> SubmissionStatus(string submissionKey,CancellationToken ct)
    {
        await service.AuthorizeAsync(ct);
        if(string.IsNullOrWhiteSpace(submissionKey)||submissionKey.Length>128)return BadRequest();
        var receipt=await submissions.FindAsync(CopiersSubmissionStore.Owner(User),submissionKey,ct);
        return receipt is null?NotFound():Ok(CopiersSubmissionStore.Status(receipt));
    }
    [HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(25*1024*1024)]
    [RequestFormLimits(MultipartBodyLengthLimit=25*1024*1024)]
    public async Task<IActionResult> Submit([FromForm] CopiersMaintenanceV2FinalizeMultipartRequestDto request,CancellationToken ct)
    {
        try
        {
            var actor=await service.AuthorizeAsync(ct);
            if(!Guid.TryParse(request.SubmissionKey,out _)||Request.Headers["Idempotency-Key"].ToString()!=request.SubmissionKey) return BadRequest(new {message="La clave del envío no coincide."});
            var owner=CopiersSubmissionStore.Owner(User);
            // Never acknowledge changed bytes without comparing the stored fingerprint.
            var draft=await service.PrepareAsync(request,Request.Form,actor,ct);
            var receipt=await submissions.ReceiveAsync(owner,draft,request,actor,ct);
            return Accepted(CopiersSubmissionStore.Status(receipt));
        }
        catch(CopiersMaintenanceV2ValidationException ex){return BadRequest(new {message=ex.Message});}
        catch(CopiersMaintenanceV2ConcurrencyException ex){return Conflict(new {message=ex.Message});}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(MicrosoftIdentityWebChallengeUserException){throw;}
        catch(Exception ex){logger.LogError(ex,"No se confirmó la recepción de la operación {SubmissionKey}.",request.SubmissionKey);return StatusCode(503,new {message="No se pudo confirmar la recepción. Conserva el borrador y reintenta con la misma clave."});}
    }
}
