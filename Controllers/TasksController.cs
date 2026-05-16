using CotizadorInterno.Web.Models.Tasks;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

public sealed class TasksController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private readonly IDataverseService _dataverse;

    public TasksController(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UserSearch([FromQuery(Name = "q")] string query, CancellationToken ct)
    {
        try
        {
            return Ok(await _dataverse.SearchSystemUsersAsync(query, 12, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "No fue posible buscar usuarios.", detail = ex.Message });
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CreateManual([FromBody] ManualTaskCreateRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { message = "Debes diligenciar la tarea." });

        try
        {
            return Ok(await _dataverse.CreateManualTaskAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "No fue posible crear la tarea.", detail = ex.Message });
        }
    }

    [HttpPost]
    [RequestSizeLimit(25 * 1024 * 1024)]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CloseManual([FromForm] string taskId, [FromForm] string comments, [FromForm] IFormFile? attachment, CancellationToken ct)
    {
        try
        {
            byte[]? content = null;
            if (attachment is not null && attachment.Length > 0)
            {
                await using var stream = attachment.OpenReadStream();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory, ct);
                content = memory.ToArray();
            }

            return Ok(await _dataverse.CloseManualTaskAsync(
                new ManualTaskCloseRequest
                {
                    TaskId = taskId,
                    Comments = comments
                },
                attachment?.FileName,
                attachment?.ContentType,
                content,
                ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "No fue posible cerrar la tarea.", detail = ex.Message });
        }
    }
}
