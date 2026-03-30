using System.Globalization;
using System.Net.Http.Json;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.PortalProveedores;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ServiceFilter(typeof(PortalProveedoresAccessFilter))]
public sealed class PortalProveedoresController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";

    private readonly IDataverseService _dataverse;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SupplierPortalOptions _options;

    public PortalProveedoresController(
        IDataverseService dataverse,
        IHttpClientFactory httpClientFactory,
        IOptions<SupplierPortalOptions> options)
    {
        _dataverse = dataverse;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var currentUser = await GetCurrentUserAsync(ct);
        var model = new SupplierPortalPageViewModel
        {
            CurrentUser = currentUser,
            CompanyName = _options.CompanyName,
            CompanyNit = _options.CompanyNit,
            CompanyAddress = _options.CompanyAddress,
            CompanyCity = _options.CompanyCity,
            IsRequestFlowConfigured = !string.IsNullOrWhiteSpace(_options.CertificateRequestFlowUrl),
            RequestFlowConfigPath = "SupplierPortal:CertificateRequestFlowUrl"
        };

        return View(model);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Providers([FromQuery] string startDate, [FromQuery] string endDate, [FromQuery] string? q, CancellationToken ct)
    {
        try
        {
            var (start, end) = ParseDateRange(startDate, endDate);
            var items = await _dataverse.GetSupplierCertificateProvidersAsync(start, end, q, ct);
            return Json(items);
        }
        catch (Exception ex)
        {
            return BadRequest(GetInnermostMessage(ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Summary(
        [FromQuery] string startDate,
        [FromQuery] string endDate,
        [FromQuery] string supplierNit,
        [FromQuery] string? supplierName,
        [FromQuery] string[] certificateTypes,
        CancellationToken ct)
    {
        try
        {
            var query = BuildCertificateQuery(startDate, endDate, supplierNit, supplierName, certificateTypes);
            var summary = await _dataverse.GetSupplierCertificateSummaryAsync(query, ct);
            return Json(summary);
        }
        catch (Exception ex)
        {
            return BadRequest(GetInnermostMessage(ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> RequestCertificates([FromBody] SupplierCertificateRequestInput? input, CancellationToken ct)
    {
        if (input is null)
            return BadRequest("Debes enviar la información de la solicitud.");

        if (string.IsNullOrWhiteSpace(input.Subject))
            return BadRequest("El asunto es obligatorio.");

        if (string.IsNullOrWhiteSpace(input.Body))
            return BadRequest("El cuerpo es obligatorio.");

        if (string.IsNullOrWhiteSpace(_options.CertificateRequestFlowUrl))
        {
            return BadRequest("Configura la URL del flujo en SupplierPortal:CertificateRequestFlowUrl antes de enviar la solicitud.");
        }

        var currentUser = await GetCurrentUserAsync(ct);
        var payload = new
        {
            subject = input.Subject.Trim(),
            body = input.Body.Trim(),
            requestedAtUtc = DateTimeOffset.UtcNow,
            requestedBy = new
            {
                currentUser.SystemUserId,
                currentUser.DisplayName,
                currentUser.Email
            }
        };

        var client = _httpClientFactory.CreateClient();
        using var response = await client.PostAsJsonAsync(_options.CertificateRequestFlowUrl, payload, cancellationToken: ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return BadRequest(string.IsNullOrWhiteSpace(body)
                ? $"El flujo respondió con error HTTP {(int)response.StatusCode}."
                : body);
        }

        return Ok(new { ok = true });
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Certificate(
        [FromQuery] string startDate,
        [FromQuery] string endDate,
        [FromQuery] string supplierNit,
        [FromQuery] string? supplierName,
        [FromQuery] string[] certificateTypes,
        [FromQuery] int autoprint,
        CancellationToken ct)
    {
        try
        {
            var query = BuildCertificateQuery(startDate, endDate, supplierNit, supplierName, certificateTypes);
            var summary = await _dataverse.GetSupplierCertificateSummaryAsync(query, ct);
            if (summary.RecordsCount == 0)
                return BadRequest("No se encontraron registros para emitir el certificado.");

            var currentUser = await GetCurrentUserAsync(ct);
            var issueDate = ResolveColombiaNow().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            var model = new SupplierCertificateDocumentViewModel
            {
                CurrentUser = currentUser,
                CompanyName = _options.CompanyName,
                CompanyNit = _options.CompanyNit,
                CompanyAddress = _options.CompanyAddress,
                CompanyCity = _options.CompanyCity,
                IssueDateDisplay = issueDate,
                AutoPrint = autoprint == 1,
                Certificate = summary
            };

            return View(model);
        }
        catch (Exception ex)
        {
            return BadRequest(GetInnermostMessage(ex));
        }
    }

    private SupplierCertificateQuery BuildCertificateQuery(
        string startDate,
        string endDate,
        string supplierNit,
        string? supplierName,
        string[] certificateTypes)
    {
        var (start, end) = ParseDateRange(startDate, endDate);
        if (string.IsNullOrWhiteSpace(supplierNit))
            throw new InvalidOperationException("Debes seleccionar un proveedor válido.");

        var parsedCertificateTypes = SupplierCertificateTypeExtensions.ParseMany(certificateTypes);
        if (parsedCertificateTypes.Count == 0)
            throw new InvalidOperationException("Debes seleccionar al menos un tipo de certificado.");

        return new SupplierCertificateQuery
        {
            StartDate = start,
            EndDate = end,
            SupplierNit = supplierNit.Trim(),
            SupplierName = supplierName?.Trim() ?? "",
            CertificateTypes = parsedCertificateTypes
        };
    }

    private static (DateOnly StartDate, DateOnly EndDate) ParseDateRange(string startDate, string endDate)
    {
        if (!DateOnly.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            throw new InvalidOperationException("La fecha inicial no es válida.");

        if (!DateOnly.TryParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            throw new InvalidOperationException("La fecha final no es válida.");

        if (end < start)
            throw new InvalidOperationException("La fecha final no puede ser menor que la inicial.");

        return (start, end);
    }

    private async Task<CurrentUserInfo> GetCurrentUserAsync(CancellationToken ct)
    {
        if (HttpContext.Items.TryGetValue(PortalProveedoresAccessFilter.CurrentUserItemKey, out var cachedUser)
            && cachedUser is CurrentUserInfo currentUser)
        {
            return currentUser;
        }

        return await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
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

    private static string GetInnermostMessage(Exception ex)
    {
        var current = ex;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }
}
