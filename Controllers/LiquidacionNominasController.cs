using System.Globalization;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.Nomina;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Nomina)]
public sealed class LiquidacionNominasController : Controller
{
    private readonly IDataverseService _dataverse;
    private readonly INominaDraftStore _draftStore;
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private const long MaxPaymentProofUploadBytes = 128 * 1024 * 1024;

    public LiquidacionNominasController(IDataverseService dataverse, INominaDraftStore draftStore)
    {
        _dataverse = dataverse;
        _draftStore = draftStore;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var currentUser = await GetCurrentUserAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var firstDay = new DateOnly(today.Year, today.Month, 1);
        var lastDay = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        var model = new NominaPageViewModel
        {
            CurrentUser = currentUser,
            InitialPeriodKey = firstDay.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            SuggestedPaymentDateValue = lastDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        return View(model);
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Preview([FromBody] NominaPreviewRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Debes indicar el mes y la fecha de pago a revisar.");

        try
        {
            var result = await _dataverse.PreviewNominaAsync(request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible preparar la liquidacion de nomina.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Confirm([FromBody] NominaConfirmRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Debes indicar el mes y la fecha de pago a procesar.");

        try
        {
            var result = await _dataverse.ConfirmNominaAsync(request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible confirmar la liquidacion de nomina.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ExistingPeriod([FromQuery] string? periodKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(periodKey))
            return BadRequest(CreateErrorPayload("Debes seleccionar el mes a liquidar."));

        try
        {
            return Ok(await _dataverse.GetNominaClosedPeriodAsync(periodKey, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible revisar la nomina existente en Dataverse.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SaveClosedVerticals([FromBody] NominaClosedVerticalsSaveRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes enviar al menos una distribucion por vertical para guardar."));

        try
        {
            return Ok(await _dataverse.SaveNominaClosedVerticalsAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar la distribucion por vertical.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [RequestSizeLimit(MaxPaymentProofUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxPaymentProofUploadBytes)]
    public async Task<IActionResult> UploadPaymentProof(string recordId, string? paymentType, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
            return BadRequest(CreateErrorPayload("Debes seleccionar un comprobante de pago valido."));

        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);

            var result = await _dataverse.UploadNominaPaymentProofAsync(
                recordId,
                file.FileName,
                file.ContentType,
                buffer.ToArray(),
                paymentType: paymentType ?? "",
                ct);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar el comprobante de pago.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> DownloadPaymentProof(string recordId, string? paymentType, CancellationToken ct)
    {
        try
        {
            var file = await _dataverse.DownloadNominaPaymentProofAsync(recordId, paymentType ?? "", ct);
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
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible descargar el comprobante de pago.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Draft([FromQuery] string? periodKey, CancellationToken ct)
    {
        try
        {
            var draft = string.IsNullOrWhiteSpace(periodKey)
                ? await _draftStore.LoadLatestAsync(ct)
                : await _draftStore.LoadAsync(periodKey, ct);

            return Ok(new NominaDraftLoadResultDto
            {
                Draft = draft
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar el borrador compartido de nomina.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Draft([FromBody] NominaDraftDto? draft, CancellationToken ct)
    {
        if (draft is null)
            return BadRequest("Debes enviar un borrador de preliquidacion para guardar.");

        try
        {
            var currentUser = await GetCurrentUserAsync(ct);
            draft.SavedByEmail = currentUser.Email;
            draft.SavedByName = FirstNonEmpty(currentUser.DisplayName, currentUser.EmployeeName, currentUser.Email);

            var saved = await _draftStore.SaveAsync(draft, ct);
            return Ok(new NominaDraftLoadResultDto
            {
                Draft = saved
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar el borrador compartido de nomina.", ex));
        }
    }

    [HttpDelete]
    [ActionName("Draft")]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> DeleteDraft([FromQuery] string? periodKey, CancellationToken ct)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(periodKey))
                await _draftStore.DeleteAsync(periodKey, ct);

            return Ok(new { deleted = !string.IsNullOrWhiteSpace(periodKey) });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible borrar el borrador compartido de nomina.", ex));
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
            traceId = HttpContext.TraceIdentifier,
            logs = Array.Empty<NominaProcessLogEntryDto>()
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

    private static string FirstNonEmpty(params string?[] values)
    {
        return values
            .Select(static value => value?.Trim() ?? "")
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
            ?? "";
    }
}
