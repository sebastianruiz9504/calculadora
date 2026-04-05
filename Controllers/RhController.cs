using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.RH;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Rh)]
public sealed class RhController : Controller
{
    private readonly IDataverseService _dataverse;
    private readonly RhOptions _options;
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";

    public RhController(IDataverseService dataverse, IOptions<RhOptions> options)
    {
        _dataverse = dataverse;
        _options = options.Value;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var model = new RhHomePageViewModel
        {
            CurrentUser = await GetCurrentUserAsync(ct),
            Modules = RhModuleCatalog.All
        };

        return View(model);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Table(string key, CancellationToken ct)
    {
        var module = RhModuleCatalog.Find(key);
        if (module is null)
            return NotFound();

        if (string.Equals(module.Key, RhModuleKeys.VacationRequests, StringComparison.OrdinalIgnoreCase))
        {
            var vacationModel = new VacationRequestPageViewModel
            {
                CurrentUser = await GetCurrentUserAsync(ct),
                Module = module,
                IsApprovalFlowConfigured = !string.IsNullOrWhiteSpace(_options.VacationApprovalFlowUrl),
                ApprovalFlowConfigPath = "Rh:VacationApprovalFlowUrl",
                FormatFieldName = string.IsNullOrWhiteSpace(_options.VacationRequestFormatField)
                    ? "cr07a_formato"
                    : _options.VacationRequestFormatField
            };

            return View("VacationRequest", vacationModel);
        }

        var model = new RhTablePageViewModel
        {
            CurrentUser = await GetCurrentUserAsync(ct),
            Module = module
        };

        return View(model);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Data(string tableKey, CancellationToken ct)
    {
        try
        {
            var result = await _dataverse.GetRhTableDataAsync(tableKey, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar la tabla de RH.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Save([FromBody] RhSaveRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar la tabla y los valores a guardar."));

        try
        {
            var result = await _dataverse.SaveRhRecordAsync(request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar el registro de RH.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [RequestSizeLimit(134217728)]
    [RequestFormLimits(MultipartBodyLengthLimit = 134217728)]
    public async Task<IActionResult> UploadFile(string tableKey, string recordId, string fieldName, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
            return BadRequest(CreateErrorPayload("Debes seleccionar un archivo valido."));

        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);

            var result = await _dataverse.UploadRhFieldFileAsync(
                tableKey,
                recordId,
                fieldName,
                file.FileName,
                file.ContentType,
                buffer.ToArray(),
                ct);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar el archivo en RH.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> DownloadFile(string tableKey, string recordId, string fieldName, bool inline = false, CancellationToken ct = default)
    {
        try
        {
            var file = await _dataverse.DownloadRhFieldFileAsync(tableKey, recordId, fieldName, ct);
            if (file is null || file.Content.Length == 0)
                return NotFound();

            return inline
                ? File(file.Content, file.ContentType)
                : File(file.Content, file.ContentType, file.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible descargar el archivo de RH.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> VacationRequestContext(CancellationToken ct)
    {
        try
        {
            var result = await _dataverse.GetVacationRequestContextAsync(ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar la solicitud de vacaciones.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SubmitVacationRequest([FromBody] VacationRequestSubmitInput? input, CancellationToken ct)
    {
        if (input is null)
            return BadRequest(CreateErrorPayload("Debes indicar el rango de fechas para la solicitud."));

        try
        {
            var result = await _dataverse.SubmitVacationRequestAsync(input, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible registrar la solicitud de vacaciones.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> VacationDocument(string recordId, int autoprint = 0, CancellationToken ct = default)
    {
        try
        {
            var html = await _dataverse.GetVacationRequestDocumentHtmlAsync(recordId, autoprint == 1, ct);
            return Content(html, "text/html; charset=utf-8");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible generar el formato de vacaciones.", ex));
        }
    }

    private async Task<CurrentUserInfo> GetCurrentUserAsync(CancellationToken ct)
    {
        return await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
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

            var trimmedMessage = current.Message.Trim();
            if (!messages.Contains(trimmedMessage, StringComparer.OrdinalIgnoreCase))
                messages.Add(trimmedMessage);
        }

        return string.Join(" | ", messages);
    }
}
