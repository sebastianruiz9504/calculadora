using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Nomina;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.PrimaLegal)]
public sealed class PrimaLegalController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private readonly IDataverseService _dataverse;

    public PrimaLegalController(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(int? year, int? semester, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var resolvedYear = year.GetValueOrDefault(today.Year);
        var resolvedSemester = semester is 2 ? 2 : 1;
        if (!semester.HasValue)
            resolvedSemester = today.Month <= 6 ? 1 : 2;

        var currentUserTask = GetCurrentUserAsync(ct);
        var boardTask = _dataverse.GetPrimaLegalBoardAsync(resolvedYear, resolvedSemester, ct);

        await Task.WhenAll(currentUserTask, boardTask);

        var model = new PrimaLegalPageViewModel
        {
            CurrentUser = currentUserTask.Result,
            SelectedYear = resolvedYear,
            SelectedSemester = resolvedSemester,
            YearOptions = Enumerable.Range(today.Year - 2, 5).Reverse().ToArray(),
            Board = boardTask.Result
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Liquidar(PrimaLegalLiquidationRequest request, CancellationToken ct)
    {
        var result = await _dataverse.SavePrimaLegalLiquidationAsync(request, ct);
        TempData["PrimaLegalMessage"] = result.Message;
        TempData["PrimaLegalTotal"] = result.TotalPrimaAmount.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return RedirectToAction(nameof(Index), new { year = request.Year, semester = request.Semester });
    }

    private async Task<CurrentUserInfo> GetCurrentUserAsync(CancellationToken ct)
    {
        return await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
    }
}
