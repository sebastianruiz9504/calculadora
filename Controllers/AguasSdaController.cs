using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.AguasSda;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

public sealed class AguasSdaController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private readonly IDataverseService _dataverse;
    private readonly ILogger<AguasSdaController> _logger;

    public AguasSdaController(IDataverseService dataverse, ILogger<AguasSdaController> logger)
    {
        _dataverse = dataverse;
        _logger = logger;
    }

    [HttpGet]
    [ModuleAuthorize(AppModule.AguasSdaBitacoras)]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public IActionResult Index()
    {
        return RedirectToAction(nameof(Bitacoras));
    }

    [HttpGet]
    [ModuleAuthorize(AppModule.AguasSdaBitacoras)]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Bitacoras(CancellationToken ct)
    {
        try
        {
            return View(await _dataverse.GetAguasSdaBitacoraBoardAsync(ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No fue posible cargar bitacoras Aguas SDA.");
            return View(new AguasSdaBitacoraBoardViewModel { LoadWarning = BuildExceptionDetail(ex) });
        }
    }

    [HttpPost]
    [ModuleAuthorize(AppModule.AguasSdaBitacoras)]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [RequestSizeLimit(90_000_000)]
    public async Task<IActionResult> GuardarBitacora([FromForm] AguasSdaBitacoraSaveRequest request, CancellationToken ct)
    {
        try
        {
            var photos = new Dictionary<string, (string FileName, string ContentType, byte[] Content)>(StringComparer.OrdinalIgnoreCase);
            await AddPhotoAsync(photos, "antes", Request.Form.Files["fotoAntes"], ct);
            await AddPhotoAsync(photos, "durante", Request.Form.Files["fotoDurante"], ct);
            await AddPhotoAsync(photos, "despues", Request.Form.Files["fotoDespues"], ct);
            return Ok(await _dataverse.SaveAguasSdaBitacoraAsync(request, photos, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No fue posible guardar bitacora Aguas SDA.");
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar la bitacora.", ex));
        }
    }

    [HttpGet]
    [ModuleAuthorize(AppModule.AguasSdaAprobacion)]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Aprobacion(CancellationToken ct)
    {
        try
        {
            return View(await _dataverse.GetAguasSdaApprovalBoardAsync(ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No fue posible cargar aprobacion Aguas SDA.");
            return View(new AguasSdaApprovalBoardViewModel { LoadWarning = BuildExceptionDetail(ex) });
        }
    }

    [HttpPost]
    [ModuleAuthorize(AppModule.AguasSdaAprobacion)]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Aprobar([FromBody] AguasSdaApprovalRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes enviar la bitacora a aprobar."));

        try
        {
            return Ok(await _dataverse.ApproveAguasSdaBitacoraAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible aprobar la bitacora.", ex));
        }
    }

    [HttpPost]
    [ModuleAuthorize(AppModule.AguasSdaAprobacion)]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Rechazar([FromBody] AguasSdaApprovalRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes enviar la bitacora a devolver."));

        try
        {
            return Ok(await _dataverse.RejectAguasSdaBitacoraAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible devolver la bitacora.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Archivo([FromQuery] string recordId, [FromQuery] string kind, CancellationToken ct)
    {
        try
        {
            var file = await _dataverse.DownloadAguasSdaBitacoraAssetAsync(recordId, kind, ct);
            if (file is null)
                return NotFound();

            return File(file.Content, file.ContentType, file.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
    }

    [HttpGet]
    [ModuleAuthorize(AppModule.AguasSdaTablaBase)]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> TablaBase(CancellationToken ct)
    {
        try
        {
            return View("Tabla", await _dataverse.GetAguasSdaTablaBaseAsync(ct));
        }
        catch (Exception ex)
        {
            return View("Tabla", new AguasSdaGenericTableViewModel
            {
                Title = "Tabla base",
                LoadWarning = BuildExceptionDetail(ex)
            });
        }
    }

    [HttpGet]
    [ModuleAuthorize(AppModule.AguasSdaMatrizInterna)]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> MatrizInterna(CancellationToken ct)
    {
        try
        {
            return View("Tabla", await _dataverse.GetAguasSdaMatrizInternaAsync(ct));
        }
        catch (Exception ex)
        {
            return View("Tabla", new AguasSdaGenericTableViewModel
            {
                Title = "Matriz interna",
                LoadWarning = BuildExceptionDetail(ex)
            });
        }
    }

    [HttpGet]
    [ModuleAuthorize(AppModule.AguasSdaPermisos)]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Permisos(CancellationToken ct)
    {
        try
        {
            return View(await _dataverse.GetAguasSdaPermissionPageAsync(ct));
        }
        catch (Exception ex)
        {
            return View(new AguasSdaPermissionPageViewModel { LoadWarning = BuildExceptionDetail(ex) });
        }
    }

    [HttpGet]
    [ModuleAuthorize(AppModule.AguasSdaPermisos)]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> BuscarUsuarios([FromQuery] string q, CancellationToken ct)
    {
        try
        {
            return Ok(await _dataverse.SearchAguasSdaSystemUsersAsync(q, 20, ct));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible buscar usuarios en AGUAS DE BOGOTA.", ex));
        }
    }

    [HttpPost]
    [ModuleAuthorize(AppModule.AguasSdaPermisos)]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> GuardarUsuario([FromBody] AguasSdaAppUserSaveRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes enviar el usuario a guardar."));

        try
        {
            return Ok(await _dataverse.SaveAguasSdaAppUserAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar el usuario SDA.", ex));
        }
    }

    [HttpPost]
    [ModuleAuthorize(AppModule.AguasSdaPermisos)]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> EliminarUsuario([FromBody] AguasSdaAppUserSaveRequest? request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el usuario a eliminar."));

        try
        {
            return Ok(await _dataverse.DeleteAguasSdaAppUserAsync(request.RecordId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible eliminar el usuario SDA.", ex));
        }
    }

    private static async Task AddPhotoAsync(
        Dictionary<string, (string FileName, string ContentType, byte[] Content)> photos,
        string kind,
        IFormFile? file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return;

        if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Las fotos deben ser archivos de imagen.");

        if (file.Length > 25 * 1024 * 1024)
            throw new InvalidOperationException("Cada foto debe pesar maximo 25 MB.");

        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);
        photos[kind] = (Path.GetFileName(file.FileName), file.ContentType, memory.ToArray());
    }

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

            var trimmed = current.Message.Trim();
            if (!messages.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                messages.Add(trimmed);
        }

        return string.Join(" | ", messages);
    }
}
