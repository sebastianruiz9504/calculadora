using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Hardware;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Hardware)]
public sealed class HardwareController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private const long MaxUploadBytes = 52_428_800;
    private readonly IDataverseService _dataverse;
    private readonly IProvisioningRequestStore _provisioningRequestStore;

    public HardwareController(IDataverseService dataverse, IProvisioningRequestStore provisioningRequestStore)
    {
        _dataverse = dataverse;
        _provisioningRequestStore = provisioningRequestStore;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        return View(new HardwarePageViewModel
        {
            CurrentUser = await GetCurrentUserAsync(ct),
            TableLogicalName = "cr07a_hardware",
            TableDisplayName = "Hardware"
        });
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> Preview(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
            return BadRequest(CreateErrorPayload("Debes seleccionar un archivo CSV valido."));

        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);

            return Ok(await _dataverse.PreviewHardwareCsvAsync(file.FileName, buffer.ToArray(), ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible preparar la vista previa del CSV.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> Provision(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
            return BadRequest(CreateErrorPayload("Debes seleccionar un archivo CSV valido."));

        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);

            return Ok(await _dataverse.ProvisionHardwareCsvAsync(file.FileName, buffer.ToArray(), ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible crear la tabla Hardware en Dataverse.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Board([FromQuery] int? stateValue, CancellationToken ct)
    {
        try
        {
            var syncMessages = new List<string>();
            var syncedRequestsCount = 0;
            var syncedImportedCount = 0;
            var pendingRequests = await _provisioningRequestStore.GetApprovedPendingHardwareSyncAsync(ct);

            foreach (var request in pendingRequests)
            {
                try
                {
                    var syncResult = await _dataverse.SyncProvisioningHardwareAsync(request, ct);
                    await _provisioningRequestStore.MarkHardwareSyncResultAsync(
                        request.RequestId,
                        syncResult.Status,
                        syncResult.ImportedCount,
                        syncResult.Message,
                        ct);
                    syncedRequestsCount++;
                    syncedImportedCount += Math.Max(0, syncResult.ImportedCount);

                    if (!string.IsNullOrWhiteSpace(syncResult.Message))
                        syncMessages.Add($"{request.RequestId}: {syncResult.Message}");
                }
                catch (Exception ex)
                {
                    var detail = BuildExceptionDetail(ex);
                    await _provisioningRequestStore.MarkHardwareSyncResultAsync(
                        request.RequestId,
                        ProvisioningHardwareSyncStatus.Failed,
                        0,
                        detail,
                        ct);
                    syncMessages.Add($"{request.RequestId}: {detail}");
                }
            }

            var board = await _dataverse.GetHardwareBoardAsync(stateValue, ct);
            board.SyncedRequestsCount = syncedRequestsCount;
            board.SyncedImportedCount = syncedImportedCount;
            board.SyncMessages = syncMessages;
            return Json(board);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar la tabla de Hardware.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> InvoiceSearch([FromQuery] string q, CancellationToken ct)
    {
        try
        {
            return Ok(await _dataverse.SearchHardwareInvoicesAsync(q, 12, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible buscar facturas para Hardware.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SaveStage([FromBody] HardwareStageSaveRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar el hardware y la etapa que quieres guardar."));

        try
        {
            return Ok(await _dataverse.SaveHardwareStageAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar la etapa de Hardware.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> UploadFile(string recordId, string fieldName, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
            return BadRequest(CreateErrorPayload("Debes seleccionar un archivo valido."));

        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);

            return Ok(await _dataverse.UploadHardwareFileAsync(
                recordId,
                fieldName,
                file.FileName,
                file.ContentType,
                buffer.ToArray(),
                ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar el adjunto de Hardware.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> DownloadFile(string recordId, string fieldName, CancellationToken ct)
    {
        try
        {
            var file = await _dataverse.DownloadHardwareFileAsync(recordId, fieldName, ct);
            if (file is null || file.Content.Length == 0)
                return NotFound();

            return File(file.Content, file.ContentType, file.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible descargar el adjunto de Hardware.", ex));
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
