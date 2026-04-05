using System.Diagnostics;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

public class HomeController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private readonly IDataverseService _dataverse;

    public HomeController(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var currentUser = await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
        var firstModule = AppModuleCatalog.NavigationModules
            .FirstOrDefault(module => currentUser.HasModule(module.OptionValue));

        if (firstModule is not null)
            return RedirectToAction(firstModule.Action, firstModule.Controller);

        return View(currentUser);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
