using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.Renovaciones;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Renovaciones)]
public sealed class RenovacionesController : Controller
{
    private readonly IDataverseService _dataverse;
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";

    public RenovacionesController(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var currentUser = await GetCurrentUserAsync(ct);

        var model = new RenovacionesPageViewModel
        {
            CurrentUser = currentUser,
            InitialFilter = RenewalPeriodFilter.ThisMonth
        };

        return View(model);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Records([FromQuery] string? filter, CancellationToken ct)
    {
        var parsedFilter = RenewalPeriodFilterExtensions.ParseOrDefault(filter);
        var board = await _dataverse.GetRenewalBoardAsync(parsedFilter, ct);
        return Json(board);
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Update([FromBody] RenewalBatchUpdateRequest? request, CancellationToken ct)
    {
        if (request is null || request.Items.Count == 0)
            return BadRequest("Debes seleccionar al menos una linea para actualizar.");

        var updatedCount = await _dataverse.UpdateRenewalRecordsAsync(request.Items, ct);
        var scenario = await _dataverse.CreateRenewalScenarioAsync(request.Items, ct);
        var redirectUrl = Url.Action("Index", "Calculator", new { scenarioId = scenario.ScenarioId }) ?? $"/Calculator?scenarioId={Uri.EscapeDataString(scenario.ScenarioId)}";
        return Ok(new
        {
            ok = true,
            updated = updatedCount,
            scenarioId = scenario.ScenarioId,
            scenarioName = scenario.ScenarioName,
            redirectUrl,
            warnings = scenario.Warnings
        });
    }

    private async Task<CurrentUserInfo> GetCurrentUserAsync(CancellationToken ct)
    {
        return await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
    }
}
