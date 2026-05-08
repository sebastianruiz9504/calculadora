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
    private const long MaxUploadBytes = 128 * 1024 * 1024;
    private readonly IDataverseService _dataverse;

    public HardwareController(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var currentUser = await GetCurrentUserAsync(ct);
        var today = ResolveBogotaToday();
        return View(new HardwareWorkspaceViewModel
        {
            RootId = "hardwareApp",
            Mode = "commercial",
            CurrentUserLabel = !string.IsNullOrWhiteSpace(currentUser.DisplayName)
                ? currentUser.DisplayName
                : currentUser.Email,
            CurrentUserId = currentUser.SystemUserId,
            CurrentUserEmail = currentUser.Email,
            CanImpersonate = HardwareAccessPolicy.IsImpersonationUser(currentUser),
            AllowCreate = !HardwareAccessPolicy.IsSupplierPaymentUser(currentUser),
            AllowCommercialDraftEdit = !HardwareAccessPolicy.IsSupplierPaymentUser(currentUser),
            PreviewUrl = Url.Action(nameof(Preview), "Hardware") ?? "",
            ProvisionUrl = Url.Action(nameof(Provision), "Hardware") ?? "",
            BoardUrl = Url.Action(nameof(CommercialBoard), "Hardware") ?? "",
            CreateUrl = Url.Action(nameof(CreateOrder), "Hardware") ?? "",
            SaveUrl = Url.Action(nameof(CommercialSaveStage), "Hardware") ?? "",
            EditUrl = Url.Action(nameof(CommercialEditRecord), "Hardware") ?? "",
            UploadUrl = Url.Action(nameof(CommercialUploadFile), "Hardware") ?? "",
            DownloadUrl = Url.Action(nameof(CommercialDownloadFile), "Hardware") ?? "",
            InvoiceSearchUrl = Url.Action(nameof(InvoiceSearch), "Hardware") ?? "",
            ClientSearchUrl = Url.Action(nameof(ClientSearch), "Hardware") ?? "",
            OwnerSearchUrl = "",
            ImpersonationUsersUrl = Url.Action(nameof(ImpersonationUsers), "Hardware") ?? "",
            InitialStartDate = new DateOnly(today.Year, today.Month, 1).ToString("yyyy-MM-dd"),
            InitialEndDate = today.ToString("yyyy-MM-dd")
        });
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CommercialBoard(
        [FromQuery] int? stateValue,
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        [FromQuery] string? impersonatedOwnerId,
        CancellationToken ct)
    {
        try
        {
            var effectiveUser = await ResolveEffectiveHardwareUserAsync(impersonatedOwnerId, ct);
            var board = HardwareAccessPolicy.IsSupplierPaymentUser(effectiveUser)
                ? await _dataverse.GetHardwareBoardAsync(
                    HardwareAccessPolicy.OkForSupplierPaymentStateValue,
                    startDate,
                    endDate,
                    ct)
                : await _dataverse.GetHardwareBoardAsync(
                    stateValue,
                    startDate,
                    endDate,
                    ct,
                    currentOwnerOnly: true,
                    ownerOverride: effectiveUser);

            ApplyCommercialBoardAccess(board, effectiveUser);
            return Json(board);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar la tabla comercial de Hardware.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CreateOrder(
        [FromBody] HardwareOrderCreateRequest? request,
        [FromQuery] string? impersonatedOwnerId,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar los datos de la orden de Hardware."));

        try
        {
            var effectiveUser = await ResolveEffectiveHardwareUserAsync(impersonatedOwnerId, ct);
            EnsureCommercialDraftAllowed(effectiveUser);
            return Ok(await _dataverse.CreateHardwareOrderDraftAsync(request, ct, effectiveUser));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible crear la orden de Hardware.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CommercialEditRecord(
        [FromBody] HardwareOrderLineEditRequest? request,
        [FromQuery] string? impersonatedOwnerId,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar la línea de Hardware que quieres editar."));

        try
        {
            var effectiveUser = await ResolveEffectiveHardwareUserAsync(impersonatedOwnerId, ct);
            EnsureCommercialDraftAllowed(effectiveUser);
            return Ok(await _dataverse.UpdateHardwareCommercialDraftAsync(request, ct, effectiveUser));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible editar la línea comercial de Hardware.", ex));
        }
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
    public async Task<IActionResult> Board(
        [FromQuery] int? stateValue,
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        CancellationToken ct)
    {
        try
        {
            return Json(await _dataverse.GetHardwareBoardAsync(stateValue, startDate, endDate, ct));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar la tabla de Hardware.", ex));
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
    public async Task<IActionResult> CommercialSaveStage(
        [FromBody] HardwareStageSaveRequest? request,
        [FromQuery] string? impersonatedOwnerId,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar el hardware y la etapa que quieres guardar."));

        try
        {
            var effectiveUser = await ResolveEffectiveHardwareUserAsync(impersonatedOwnerId, ct);
            var isSupplierPaymentUser = HardwareAccessPolicy.IsSupplierPaymentUser(effectiveUser);
            if (IsSupplierPaymentAction(request.ActionKey))
            {
                if (!isSupplierPaymentUser)
                    return BadRequest(CreateErrorPayload("Solo cartera puede registrar el pago a proveedor en Hardware."));

                return Ok(await _dataverse.SaveHardwareStageAsync(request, ct));
            }

            if (isSupplierPaymentUser)
                return BadRequest(CreateErrorPayload("Cartera solo puede registrar pagos a proveedor en Hardware."));

            return Ok(await _dataverse.SaveHardwareStageAsync(
                request,
                ct,
                requireCurrentOwner: true,
                ownerOverride: effectiveUser));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar la etapa comercial de Hardware.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> EditRecords([FromBody] HardwareBulkEditRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar las filas y los campos que quieres editar."));

        try
        {
            return Ok(await _dataverse.SaveHardwareRecordsAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible editar los registros de Hardware.", ex));
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

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> CommercialUploadFile(
        string recordId,
        string fieldName,
        IFormFile? file,
        [FromQuery] string? impersonatedOwnerId,
        CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
            return BadRequest(CreateErrorPayload("Debes seleccionar un archivo valido."));

        try
        {
            var effectiveUser = await ResolveEffectiveHardwareUserAsync(impersonatedOwnerId, ct);
            var isSupplierPaymentUser = HardwareAccessPolicy.IsSupplierPaymentUser(effectiveUser);
            if (isSupplierPaymentUser && !IsSupplierPaymentFile(fieldName))
                return BadRequest(CreateErrorPayload("Cartera solo puede cargar el soporte de pago a proveedor."));

            if (!isSupplierPaymentUser && IsSupplierPaymentFile(fieldName))
                return BadRequest(CreateErrorPayload("Solo cartera puede cargar el soporte de pago a proveedor."));

            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);

            return Ok(await _dataverse.UploadHardwareFileAsync(
                recordId,
                fieldName,
                file.FileName,
                file.ContentType,
                buffer.ToArray(),
                ct,
                requireCurrentOwner: !isSupplierPaymentUser,
                ownerOverride: isSupplierPaymentUser ? null : effectiveUser,
                requiredStateValue: isSupplierPaymentUser ? HardwareAccessPolicy.OkForSupplierPaymentStateValue : null));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar el adjunto comercial de Hardware.", ex));
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

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CommercialDownloadFile(
        string recordId,
        string fieldName,
        [FromQuery] string? impersonatedOwnerId,
        CancellationToken ct)
    {
        try
        {
            var effectiveUser = await ResolveEffectiveHardwareUserAsync(impersonatedOwnerId, ct);
            var isSupplierPaymentUser = HardwareAccessPolicy.IsSupplierPaymentUser(effectiveUser);

            var file = await _dataverse.DownloadHardwareFileAsync(
                recordId,
                fieldName,
                ct,
                requireCurrentOwner: !isSupplierPaymentUser,
                ownerOverride: isSupplierPaymentUser ? null : effectiveUser,
                requiredStateValue: isSupplierPaymentUser ? HardwareAccessPolicy.OkForSupplierPaymentStateValue : null);
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
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible descargar el adjunto comercial de Hardware.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> InvoiceSearch([FromQuery(Name = "q")] string query, CancellationToken ct)
    {
        try
        {
            return Ok(await _dataverse.SearchHardwareInvoicesAsync(query, 12, ct));
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

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ClientSearch([FromQuery(Name = "q")] string query, CancellationToken ct)
    {
        try
        {
            return Ok(await _dataverse.SearchClientsAsync(query, 12, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible buscar clientes para Hardware.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> OwnerSearch([FromQuery(Name = "q")] string query, CancellationToken ct)
    {
        try
        {
            return Ok(await _dataverse.SearchSystemUsersAsync(query, 12, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible buscar usuarios para Hardware.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ImpersonationUsers(CancellationToken ct)
    {
        try
        {
            var currentUser = await GetCurrentUserAsync(ct);
            if (!HardwareAccessPolicy.IsImpersonationUser(currentUser))
                return Forbid();

            return Ok(await _dataverse.SearchSystemUsersAsync("", 500, ct, includeAllWhenEmpty: true));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar usuarios para personificación de Hardware.", ex));
        }
    }

    private async Task<CurrentUserInfo> GetCurrentUserAsync(CancellationToken ct) =>
        await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo();

    private async Task<CurrentUserInfo> ResolveEffectiveHardwareUserAsync(string? impersonatedOwnerId, CancellationToken ct)
    {
        var currentUser = await GetCurrentUserAsync(ct);
        var normalizedOwnerId = (impersonatedOwnerId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalizedOwnerId))
            return currentUser;

        if (!HardwareAccessPolicy.IsImpersonationUser(currentUser))
            throw new InvalidOperationException("No tienes permisos para personificar usuarios en Hardware.");

        var selectedUser = await _dataverse.GetSystemUserAsync(normalizedOwnerId, ct)
            ?? throw new InvalidOperationException("No fue posible encontrar el usuario seleccionado para personificar.");

        return new CurrentUserInfo
        {
            SystemUserId = selectedUser.Id,
            DisplayName = ResolveSystemUserDisplayName(selectedUser),
            Email = selectedUser.Email
        };
    }

    private static void ApplyCommercialBoardAccess(HardwareBoardDto board, CurrentUserInfo effectiveUser)
    {
        if (HardwareAccessPolicy.IsSupplierPaymentUser(effectiveUser))
        {
            board.StateOptions = board.StateOptions
                .Where(option => option.Value == HardwareAccessPolicy.OkForSupplierPaymentStateValue)
                .ToList();
            return;
        }

        foreach (var row in board.Rows.Where(row => row.StateValue == HardwareAccessPolicy.OkForSupplierPaymentStateValue))
        {
            row.ActionKey = "";
            row.ActionLabel = "";
            row.HasAction = false;
        }
    }

    private static void EnsureCommercialDraftAllowed(CurrentUserInfo effectiveUser)
    {
        if (HardwareAccessPolicy.IsSupplierPaymentUser(effectiveUser))
            throw new InvalidOperationException("Cartera solo puede gestionar pagos a proveedor en Hardware.");
    }

    private static bool IsSupplierPaymentAction(string? actionKey) =>
        string.Equals(
            (actionKey ?? "").Trim(),
            HardwareAccessPolicy.SupplierPaymentActionKey,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsSupplierPaymentFile(string? fieldName) =>
        string.Equals(
            (fieldName ?? "").Trim(),
            HardwareAccessPolicy.SupplierPaymentFileField,
            StringComparison.OrdinalIgnoreCase);

    private static string ResolveSystemUserDisplayName(SystemUserLookupItem selectedUser)
    {
        if (string.IsNullOrWhiteSpace(selectedUser.Name))
            return selectedUser.Email;

        var suffix = string.IsNullOrWhiteSpace(selectedUser.Email) ? "" : $" ({selectedUser.Email})";
        return selectedUser.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? selectedUser.Name[..^suffix.Length]
            : selectedUser.Name;
    }

    private static DateOnly ResolveBogotaToday()
    {
        var utcNow = DateTimeOffset.UtcNow;
        foreach (var timeZoneId in new[] { "SA Pacific Standard Time", "America/Bogota" })
        {
            try
            {
                var timezone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, timezone).DateTime);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return DateOnly.FromDateTime(utcNow.UtcDateTime);
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
