using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Dashboard)]
[AuthorizeForScopes(Scopes = new[] { "https://orgc79ca19c.crm2.dynamics.com/user_impersonation" })]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PortfolioCollectionsController(PortfolioCollectionsService service, IPortfolioCollectionsRepository repository, ILogger<PortfolioCollectionsController> logger) : Controller
{
    private string Author => User.FindFirst("name")?.Value ?? User.Identity?.Name ?? "Usuario de cartera";
    [HttpGet] public Task<IActionResult> Page(string? q, int page = 1, int pageSize = 50, string? sort = null, bool refresh = false, CancellationToken ct = default) => Run(async () => await service.PageAsync(q?.Length > 200 ? q[..200] : q, page, pageSize, sort, refresh, ct));
    [HttpGet] public Task<IActionResult> Detail(string invoiceId, CancellationToken ct) => Run(async () => await service.DetailAsync(invoiceId, ct));
    [HttpGet] public Task<IActionResult> History(string invoiceId, int page, CancellationToken ct) => Run(async () => { await service.InvoiceAsync(invoiceId, ct); return await repository.GetCollectionHistoryAsync(invoiceId, page, ct); });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Note([FromBody] CollectionNoteRequest request, CancellationToken ct) => Run(async () => { await service.AddNoteAsync(request, Author, ct); return new { message = "Observación guardada." }; });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Send([FromBody] CollectionEmailRequest request, CancellationToken ct) => Run(async () => await service.SendAsync(request, Author, false, ct));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Recipients([FromBody] CollectionRecipientRequest request, CancellationToken ct) => Run(async () => {
        var invoice = await service.InvoiceAsync(request.InvoiceId, ct);
        if (string.IsNullOrWhiteSpace(invoice.ClientId) || PortfolioCollectionRules.Emails(request.AdditionalRecipients).Length == 0) throw new InvalidOperationException("Selecciona un cliente vinculado y escribe al menos un correo válido.");
        await repository.SaveCollectionContactAsync(invoice.ClientId, request.AdditionalRecipients, ct);
        return await repository.GetCollectionContactAsync(invoice.ClientId, invoice.ClientName, ct);
    });
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return Json(await action()); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (FormatException) { return BadRequest(new { message = "Selecciona una factura válida." }); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception ex) { logger.LogError(ex, "Error de gestión de cartera"); return StatusCode(500, new { message = "No fue posible completar la gestión. Actualiza el historial antes de reintentar." }); }
    }
}
