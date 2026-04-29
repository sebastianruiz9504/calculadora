using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.Permissions;
using Microsoft.AspNetCore.Mvc;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.PlanRio)]
public class PlanRioController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
