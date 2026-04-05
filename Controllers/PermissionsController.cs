using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Permissions)]
public sealed class PermissionsController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private readonly IDataverseService _dataverse;

    public PermissionsController(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var model = new EmployeeModulePermissionsPageViewModel
        {
            CurrentUser = await GetCurrentUserAsync(ct),
            Modules = AppModuleCatalog.PermissionModules,
            Employees = await _dataverse.GetEmployeeModulePermissionsAsync(ct)
        };

        return View(model);
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Save([FromBody] EmployeeModulePermissionSaveRequest? request, CancellationToken ct)
    {
        if (request is null || request.Employees.Count == 0)
            return BadRequest(new { message = "Debes enviar al menos un empleado para actualizar." });

        try
        {
            var result = await _dataverse.SaveEmployeeModulePermissionsAsync(request, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "No fue posible guardar los permisos.",
                detail = BuildExceptionDetail(ex),
                traceId = HttpContext.TraceIdentifier
            });
        }
    }

    private async Task<CurrentUserInfo> GetCurrentUserAsync(CancellationToken ct) =>
        await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo();

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
