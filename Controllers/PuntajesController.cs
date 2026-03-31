using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Puntajes;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ServiceFilter(typeof(PuntajesAccessFilter))]
public sealed class PuntajesController : Controller
{
    private readonly IDataverseService _dataverse;
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";

    public PuntajesController(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var currentUser = await GetCurrentUserAsync(ct);

        var model = new PuntajesPageViewModel
        {
            CurrentUser = currentUser,
            InitialFilter = ScorePeriodFilter.ThisMonth,
            FirstContractOptions = PuntajesOptionCatalog.FirstContractOptions,
            LineOptions = PuntajesOptionCatalog.LineOptions,
            VerticalOptions = PuntajesOptionCatalog.VerticalOptions
        };

        return View(model);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Records([FromQuery] string? filter, CancellationToken ct)
    {
        var parsedFilter = ScorePeriodFilterExtensions.ParseOrDefault(filter);
        var board = await _dataverse.GetScoreBoardAsync(parsedFilter, ct);
        return Json(board);
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Verify([FromBody] ScoreVerificationRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Debes enviar los datos de verificacion.");

        await _dataverse.VerifyScoreRecordAsync(request, ct);
        return Ok(new { ok = true, message = "El registro se verifico correctamente." });
    }

    private async Task<CurrentUserInfo> GetCurrentUserAsync(CancellationToken ct)
    {
        if (HttpContext.Items.TryGetValue(PuntajesAccessFilter.CurrentUserItemKey, out var cachedUser)
            && cachedUser is CurrentUserInfo currentUser)
        {
            return currentUser;
        }

        return await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
    }
}
