using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.ProposalChat;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.ProposalChat)]
public sealed class ProposalChatController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private readonly IAzureOpenAIProposalChatService _proposalChat;
    private readonly ILogger<ProposalChatController> _logger;

    public ProposalChatController(
        IAzureOpenAIProposalChatService proposalChat,
        ILogger<ProposalChatController> logger)
    {
        _proposalChat = proposalChat;
        _logger = logger;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Chat([FromBody] ProposalChatRequestDto request, CancellationToken ct)
    {
        try
        {
            return Json(await _proposalChat.AskAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No fue posible responder con el agente de propuestas.");
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible responder con el agente de propuestas.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public IActionResult ExportPdf([FromBody] ProposalExportRequestDto request)
    {
        try
        {
            var content = ProposalDocumentExportBuilder.BuildPdfDocument(request);
            var fileName = ProposalDocumentExportBuilder.BuildSafeFileName(request.DocumentTitle, "pdf");
            return File(content, "application/pdf", fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No fue posible exportar la propuesta en PDF.");
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible exportar la propuesta en PDF.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public IActionResult ExportWord([FromBody] ProposalExportRequestDto request)
    {
        try
        {
            var content = ProposalDocumentExportBuilder.BuildWordDocument(request);
            var fileName = ProposalDocumentExportBuilder.BuildSafeFileName(request.DocumentTitle, "doc");
            return File(content, "application/msword", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No fue posible exportar la propuesta en Word.");
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible exportar la propuesta en Word.");
        }
    }
}
