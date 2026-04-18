using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.CuentasCobro;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.CuentasCobro)]
public sealed class CuentasCobroController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private readonly IDataverseService _dataverse;

    public CuentasCobroController(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var now = ResolveColombiaNow();
        var model = new CuentasCobroPageViewModel
        {
            CurrentUser = await GetCurrentUserAsync(ct),
            InitialYear = now.Year,
            InitialMonth = now.Month
        };

        return View(model);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Data([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        try
        {
            var result = await _dataverse.GetCuentasCobroBoardAsync(year, month, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar las cuentas de cobro.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Save([FromBody] CuentaCobroSaveRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar la fila a guardar."));

        try
        {
            var result = await _dataverse.SaveCuentaCobroAsync(request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar la cuenta de cobro.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [RequestSizeLimit(134217728)]
    [RequestFormLimits(MultipartBodyLengthLimit = 134217728)]
    public async Task<IActionResult> UploadFile(string recordId, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
            return BadRequest(CreateErrorPayload("Debes seleccionar un archivo valido."));

        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);

            var result = await _dataverse.UploadCuentaCobroAttachmentAsync(
                recordId,
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
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar el adjunto de la cuenta de cobro.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> DownloadFile(string recordId, CancellationToken ct)
    {
        try
        {
            var file = await _dataverse.DownloadCuentaCobroAttachmentAsync(recordId, ct);
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
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible descargar el adjunto de la cuenta de cobro.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> MarkPrinted([FromBody] string? recordId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recordId))
            return BadRequest(CreateErrorPayload("Debes indicar la cuenta de cobro a imprimir."));

        try
        {
            var result = await _dataverse.MarkCuentaCobroAsPrintedAsync(recordId, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible marcar la cuenta de cobro como impresa.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Print(string recordId, int autoprint = 0, CancellationToken ct = default)
    {
        try
        {
            var model = new CuentaCobroPrintViewModel
            {
                CurrentUser = await GetCurrentUserAsync(ct),
                Record = await _dataverse.GetCuentaCobroByIdAsync(recordId, ct),
                AutoPrint = autoprint == 1,
                PrintedAtDisplay = ResolveColombiaNow().ToString("dd/MM/yyyy HH:mm")
            };

            return View(model);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible generar la vista de impresion de la cuenta de cobro.", ex));
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

    private static DateTimeOffset ResolveColombiaNow()
    {
        var utcNow = DateTimeOffset.UtcNow;
        foreach (var timeZoneId in new[] { "SA Pacific Standard Time", "America/Bogota" })
        {
            try
            {
                var timezone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return TimeZoneInfo.ConvertTime(utcNow, timezone);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return utcNow;
    }
}
