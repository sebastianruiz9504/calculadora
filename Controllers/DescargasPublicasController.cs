using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.PublicDataExport;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

public sealed class DescargasPublicasController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private const string AccessCookieName = "DTech.PublicDataExport";
    private static readonly TimeSpan AccessLifetime = TimeSpan.FromHours(8);

    private readonly IDataverseService _dataverse;
    private readonly IPublicDataExportSettingsStore _settingsStore;
    private readonly IDataProtector _protector;

    public DescargasPublicasController(
        IDataverseService dataverse,
        IPublicDataExportSettingsStore settingsStore,
        IDataProtectionProvider dataProtectionProvider)
    {
        _dataverse = dataverse;
        _settingsStore = settingsStore;
        _protector = dataProtectionProvider.CreateProtector("CotizadorInterno.PublicDataExport.Access.v1");
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var catalog = _dataverse.GetPublicDataExportCatalog();
        var settings = await _settingsStore.LoadAsync(ct);
        var isConfigured = IsConfigured(settings, catalog);
        var model = new PublicDataExportPublicViewModel
        {
            Catalog = catalog,
            Settings = settings,
            IsConfigured = isConfigured,
            IsAuthorized = isConfigured && HasPublicAccess(settings),
            IsCurrentUserAdmin = PublicDataExportAuthorization.IsAdmin(null, User),
            Message = isConfigured
                ? ""
                : "El portal aun no esta configurado. El administrador debe definir la contrasena y las columnas aprobadas."
        };

        return View(model);
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Entrar([FromForm] string password, CancellationToken ct)
    {
        var catalog = _dataverse.GetPublicDataExportCatalog();
        var settings = await _settingsStore.LoadAsync(ct);
        if (!IsConfigured(settings, catalog))
        {
            return View("Index", new PublicDataExportPublicViewModel
            {
                Catalog = catalog,
                Settings = settings,
                IsConfigured = false,
                IsCurrentUserAdmin = PublicDataExportAuthorization.IsAdmin(null, User),
                Message = "El portal aun no esta configurado."
            });
        }

        if (!PublicDataExportPasswordHasher.Verify(settings, password))
        {
            return View("Index", new PublicDataExportPublicViewModel
            {
                Catalog = catalog,
                Settings = settings,
                IsConfigured = true,
                IsAuthorized = false,
                IsCurrentUserAdmin = PublicDataExportAuthorization.IsAdmin(null, User),
                LoginError = "La contrasena no es valida."
            });
        }

        SetPublicAccessCookie(settings);
        return RedirectToAction(nameof(Index));
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Salir()
    {
        Response.Cookies.Delete(AccessCookieName);
        return RedirectToAction(nameof(Index));
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Preview([FromQuery] string dataset, CancellationToken ct)
    {
        try
        {
            var catalog = _dataverse.GetPublicDataExportCatalog();
            var settings = await _settingsStore.LoadAsync(ct);
            if (!HasPublicAccess(settings))
                return Unauthorized(new { message = "Debes ingresar la contrasena del portal." });

            var (definition, approvedColumns) = ResolveApprovedDataset(catalog, settings, dataset);
            var table = await _dataverse.GetPublicDataExportTableAsync(definition.Key, approvedColumns, top: 200, ct);
            return Ok(table);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Descargar([FromQuery] string dataset, CancellationToken ct)
    {
        try
        {
            var catalog = _dataverse.GetPublicDataExportCatalog();
            var settings = await _settingsStore.LoadAsync(ct);
            if (!HasPublicAccess(settings))
                return Unauthorized("Debes ingresar la contrasena del portal.");

            var (definition, approvedColumns) = ResolveApprovedDataset(catalog, settings, dataset);
            var table = await _dataverse.GetPublicDataExportTableAsync(definition.Key, approvedColumns, top: null, ct);
            var content = BuildExcel(table);
            var fileName = $"{BuildSafeFileName(definition.Label)}-{ResolveBogotaToday():yyyyMMdd}.xlsx";
            return File(
                content,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Admin([FromQuery] int? saved, CancellationToken ct)
    {
        var currentUser = await TryGetCurrentUserAsync(ct);
        if (!PublicDataExportAuthorization.IsAdmin(currentUser, User))
            return Forbid();

        var model = await BuildAdminModelAsync(
            currentUser,
            statusMessage: saved == 1 ? "Configuracion guardada correctamente." : "",
            errorMessage: "",
            ct);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Admin([FromForm] PublicDataExportAdminSaveRequest request, CancellationToken ct)
    {
        var currentUser = await TryGetCurrentUserAsync(ct);
        if (!PublicDataExportAuthorization.IsAdmin(currentUser, User))
            return Forbid();

        var catalog = _dataverse.GetPublicDataExportCatalog();
        var settings = await _settingsStore.LoadAsync(ct);
        settings.ApprovedColumnsByDataset = BuildApprovedColumnMap(catalog, request);

        var hasNewPassword = !string.IsNullOrWhiteSpace(request.NewPassword);
        if (hasNewPassword)
        {
            if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
            {
                return View(await BuildAdminModelAsync(
                    currentUser,
                    "",
                    "La confirmacion de contrasena no coincide.",
                    ct,
                    settings));
            }

            if (request.NewPassword.Trim().Length < 8)
            {
                return View(await BuildAdminModelAsync(
                    currentUser,
                    "",
                    "La contrasena debe tener al menos 8 caracteres.",
                    ct,
                    settings));
            }

            PublicDataExportPasswordHasher.SetPassword(settings, request.NewPassword);
        }
        else if (!settings.HasPassword)
        {
            return View(await BuildAdminModelAsync(
                currentUser,
                "",
                "Debes definir una contrasena antes de habilitar el portal publico.",
                ct,
                settings));
        }

        settings.UpdatedBy = FirstNonEmpty(currentUser.Email, currentUser.EmployeeUserEmail, PublicDataExportAuthorization.AdminEmail);
        settings.UpdatedUtc = DateTimeOffset.UtcNow;
        await _settingsStore.SaveAsync(settings, ct);

        return RedirectToAction(nameof(Admin), new { saved = 1 });
    }

    private async Task<PublicDataExportAdminViewModel> BuildAdminModelAsync(
        CurrentUserInfo currentUser,
        string statusMessage,
        string errorMessage,
        CancellationToken ct,
        PublicDataExportSettings? settingsOverride = null)
    {
        return new PublicDataExportAdminViewModel
        {
            CurrentUser = currentUser,
            Catalog = _dataverse.GetPublicDataExportCatalog(),
            Settings = settingsOverride ?? await _settingsStore.LoadAsync(ct),
            StatusMessage = statusMessage,
            ErrorMessage = errorMessage
        };
    }

    private async Task<CurrentUserInfo> TryGetCurrentUserAsync(CancellationToken ct)
    {
        try
        {
            return await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
        }
        catch
        {
            return new CurrentUserInfo();
        }
    }

    private static Dictionary<string, List<string>> BuildApprovedColumnMap(
        PublicDataExportCatalogDto catalog,
        PublicDataExportAdminSaveRequest request)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        AddApprovedColumns(map, catalog, PublicDataExportDatasetKeys.Billing, request.BillingColumns);
        AddApprovedColumns(map, catalog, PublicDataExportDatasetKeys.Expenses, request.ExpensesColumns);
        return map;
    }

    private static void AddApprovedColumns(
        IDictionary<string, List<string>> map,
        PublicDataExportCatalogDto catalog,
        string datasetKey,
        IEnumerable<string>? requestedColumns)
    {
        var dataset = catalog.FindDataset(datasetKey);
        if (dataset is null)
            return;

        var allowed = dataset.Columns
            .Select(static column => column.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        map[dataset.Key] = (requestedColumns ?? Array.Empty<string>())
            .Where(column => !string.IsNullOrWhiteSpace(column) && allowed.Contains(column.Trim()))
            .Select(static column => column.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static (PublicDataExportDatasetDefinition Definition, IReadOnlyList<string> ApprovedColumns) ResolveApprovedDataset(
        PublicDataExportCatalogDto catalog,
        PublicDataExportSettings settings,
        string datasetKey)
    {
        var definition = catalog.FindDataset(datasetKey)
            ?? throw new InvalidOperationException("La tabla solicitada no existe.");
        var approvedColumns = settings.GetApprovedColumns(definition.Key)
            .Where(column => definition.FindColumn(column) is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (approvedColumns.Count == 0)
            throw new InvalidOperationException("La tabla seleccionada no tiene columnas aprobadas para descargar.");

        return (definition, approvedColumns);
    }

    private static bool IsConfigured(PublicDataExportSettings settings, PublicDataExportCatalogDto catalog)
    {
        return settings.HasPassword
            && catalog.Datasets.Any(dataset => settings.GetApprovedColumns(dataset.Key)
                .Any(column => dataset.FindColumn(column) is not null));
    }

    private bool HasPublicAccess(PublicDataExportSettings settings)
    {
        if (!settings.HasPassword)
            return false;

        if (!Request.Cookies.TryGetValue(AccessCookieName, out var protectedValue)
            || string.IsNullOrWhiteSpace(protectedValue))
        {
            return false;
        }

        try
        {
            var json = _protector.Unprotect(protectedValue);
            var ticket = JsonSerializer.Deserialize<PublicAccessTicket>(json);
            if (ticket is null || ticket.ExpiresUtc < DateTimeOffset.UtcNow)
                return false;

            return string.Equals(ticket.PasswordHash, settings.PasswordHash, StringComparison.Ordinal);
        }
        catch
        {
            Response.Cookies.Delete(AccessCookieName);
            return false;
        }
    }

    private void SetPublicAccessCookie(PublicDataExportSettings settings)
    {
        var ticket = new PublicAccessTicket(settings.PasswordHash, DateTimeOffset.UtcNow.Add(AccessLifetime));
        var protectedValue = _protector.Protect(JsonSerializer.Serialize(ticket));
        Response.Cookies.Append(
            AccessCookieName,
            protectedValue,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps,
                Expires = ticket.ExpiresUtc
            });
    }

    private static byte[] BuildExcel(PublicDataExportTableDto table)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(BuildWorksheetName(table.DatasetLabel));

        for (var columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            worksheet.Cell(1, columnIndex + 1).Value = table.Columns[columnIndex].Label;
        }

        var rowIndex = 2;
        foreach (var row in table.Rows)
        {
            for (var columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                var column = table.Columns[columnIndex];
                row.Cells.TryGetValue(column.Key, out var cell);
                WriteExcelCell(worksheet.Cell(rowIndex, columnIndex + 1), cell);
            }

            rowIndex++;
        }

        if (table.Columns.Count > 0)
        {
            worksheet.Range(1, 1, 1, table.Columns.Count).Style.Font.Bold = true;
            worksheet.Range(1, 1, Math.Max(rowIndex - 1, 1), table.Columns.Count).SetAutoFilter();
            worksheet.Columns().AdjustToContents();
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteExcelCell(IXLCell target, PublicDataExportCellDto? cell)
    {
        if (cell is null)
        {
            target.Value = "";
            return;
        }

        if (string.Equals(cell.ValueType, "date", StringComparison.OrdinalIgnoreCase)
            && DateOnly.TryParseExact(cell.DateValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            target.Value = date.ToDateTime(TimeOnly.MinValue);
            target.Style.DateFormat.Format = "dd/mm/yyyy";
            return;
        }

        if ((string.Equals(cell.ValueType, "currency", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cell.ValueType, "number", StringComparison.OrdinalIgnoreCase))
            && cell.NumberValue.HasValue)
        {
            target.Value = cell.NumberValue.Value;
            target.Style.NumberFormat.Format = string.Equals(cell.ValueType, "currency", StringComparison.OrdinalIgnoreCase)
                ? "$ #,##0.00"
                : "#,##0.00";
            return;
        }

        target.Value = cell.DisplayValue ?? "";
        if (string.Equals(cell.ValueType, "url", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(cell.DisplayValue, UriKind.Absolute, out _))
        {
            target.SetHyperlink(new XLHyperlink(cell.DisplayValue));
        }
    }

    private static string BuildWorksheetName(string value)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars().Concat(new[] { '[', ']', ':', '*', '?', '/', '\\' }));
        var cleaned = new string((value ?? "Datos")
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray())
            .Trim();

        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = "Datos";

        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }

    private static string BuildSafeFileName(string value)
    {
        var cleaned = string.Join("-", (value ?? "datos")
            .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        cleaned = cleaned
            .Replace(" ", "-", StringComparison.OrdinalIgnoreCase)
            .Trim('-');

        return string.IsNullOrWhiteSpace(cleaned)
            ? "datos"
            : cleaned.ToLowerInvariant();
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

    private static string FirstNonEmpty(params string?[] values)
    {
        return values
            .Select(static value => value?.Trim() ?? "")
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
            ?? "";
    }

    private sealed record PublicAccessTicket(string PasswordHash, DateTimeOffset ExpiresUtc);
}
