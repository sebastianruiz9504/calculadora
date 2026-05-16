using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CotizadorInterno.Web.Filters;

public sealed class ModuleAccessFilter : IAsyncActionFilter
{
    private readonly IDataverseService _dataverse;
    private readonly AppModule _module;

    public ModuleAccessFilter(IDataverseService dataverse, AppModule module)
    {
        _dataverse = dataverse;
        _module = module;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.Filters.OfType<IAllowAnonymousFilter>().Any())
        {
            await next();
            return;
        }

        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Result = new ChallengeResult();
            return;
        }

        var definition = AppModuleCatalog.Find(_module);
        var currentUser = await _dataverse.GetCurrentUserAsync(context.HttpContext.RequestAborted);
        if (definition is null || !AppModuleAccessPolicy.CanAccess(definition, currentUser))
        {
            context.Result = new ForbidResult();
            return;
        }

        await next();
    }
}
