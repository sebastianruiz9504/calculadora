using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Home;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

public class HomeController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private readonly IDataverseService _dataverse;
    private readonly ILogger<HomeController> _logger;

    public HomeController(IDataverseService dataverse, ILogger<HomeController> logger)
    {
        _dataverse = dataverse;
        _logger = logger;
    }

    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var currentUser = await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
        var availableModules = AppModuleCatalog.NavigationModules
            .Where(module => currentUser.HasModule(module.OptionValue))
            .ToList();

        var model = new HomePageViewModel
        {
            CurrentUser = currentUser,
            UserDisplayName = ResolveUserDisplayName(currentUser),
            AvailableModules = availableModules
        };

        return View(model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        if (feature?.Error is not null)
        {
            _logger.LogError(feature.Error, "Unhandled exception while processing {Path}", feature.Path);
        }

        var showDiagnostics = IsLocalRequest(HttpContext);
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            Path = feature?.Path,
            ErrorMessage = showDiagnostics ? feature?.Error.Message : null,
            ErrorDetails = showDiagnostics ? feature?.Error.ToString() : null
        });
    }

    private string ResolveUserDisplayName(CurrentUserInfo currentUser)
    {
        if (!string.IsNullOrWhiteSpace(currentUser.DisplayName))
            return currentUser.DisplayName.Trim();

        if (!string.IsNullOrWhiteSpace(currentUser.EmployeeUserDisplayName))
            return currentUser.EmployeeUserDisplayName.Trim();

        if (!string.IsNullOrWhiteSpace(currentUser.EmployeeName))
            return currentUser.EmployeeName.Trim();

        var principal = HttpContext?.User;
        if (principal is null)
            return NormalizeUserDisplayName(currentUser.Email);

        var givenName = principal.FindFirstValue(ClaimTypes.GivenName);
        var surname = principal.FindFirstValue(ClaimTypes.Surname);
        var fullName = string.Join(" ", new[] { givenName, surname }.Where(static part => !string.IsNullOrWhiteSpace(part)));

        return NormalizeUserDisplayName(
            principal.FindFirstValue("name")
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? (string.IsNullOrWhiteSpace(fullName) ? null : fullName)
            ?? principal.GetDisplayName()
            ?? principal.Identity?.Name
            ?? principal.FindFirstValue("preferred_username")
            ?? principal.FindFirstValue(ClaimTypes.Upn)
            ?? principal.FindFirstValue(ClaimTypes.Email)
            ?? currentUser.Email);
    }

    private static string NormalizeUserDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Usuario";

        var trimmed = value.Trim();
        if (!trimmed.Contains('@'))
            return trimmed;

        var localPart = trimmed.Split('@', 2)[0]
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ');

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(localPart.ToLowerInvariant());
    }

    private static bool IsLocalRequest(HttpContext httpContext)
    {
        var host = httpContext.Request.Host.Host;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        return IPAddress.TryParse(host, out var ipAddress) && IPAddress.IsLoopback(ipAddress);
    }
}
