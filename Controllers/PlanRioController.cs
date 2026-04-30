using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.PlanRio)]
public class PlanRioController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";

    private readonly IDataverseService _dataverse;

    public PlanRioController(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var model = await _dataverse.GetPlanRioPageAsync(ct);
        return View(model);
    }
}
