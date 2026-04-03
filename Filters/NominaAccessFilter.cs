using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Nomina;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CotizadorInterno.Web.Filters;

public sealed class NominaAccessFilter : IAsyncActionFilter
{
    public const string CurrentUserItemKey = "Nomina.CurrentUser";

    private readonly IDataverseService _dataverse;

    public NominaAccessFilter(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Result = new ChallengeResult();
            return;
        }

        CurrentUserInfo? currentUser = null;
        if (!NominaAccessPolicy.HasAccess(user))
        {
            currentUser = await _dataverse.GetCurrentUserAsync(context.HttpContext.RequestAborted);
            if (!NominaAccessPolicy.HasAccess(currentUser?.Email))
            {
                context.Result = new ForbidResult();
                return;
            }
        }

        currentUser ??= await _dataverse.GetCurrentUserAsync(context.HttpContext.RequestAborted);
        if (currentUser is not null)
            context.HttpContext.Items[CurrentUserItemKey] = currentUser;

        await next();
    }
}
