using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Renovaciones;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CotizadorInterno.Web.Filters;

public sealed class RenovacionesAccessFilter : IAsyncActionFilter
{
    public const string CurrentUserItemKey = "Renovaciones.CurrentUser";

    private readonly IDataverseService _dataverse;

    public RenovacionesAccessFilter(IDataverseService dataverse)
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
        if (!RenovacionesAccessPolicy.HasAccess(user))
        {
            currentUser = await _dataverse.GetCurrentUserAsync(context.HttpContext.RequestAborted);
            if (!RenovacionesAccessPolicy.HasAccess(currentUser?.Email))
            {
                context.Result = new ForbidResult();
                return;
            }
        }

        currentUser ??= await _dataverse.GetCurrentUserAsync(context.HttpContext.RequestAborted);
        if (currentUser is not null)
        {
            context.HttpContext.Items[CurrentUserItemKey] = currentUser;
        }

        await next();
    }
}
