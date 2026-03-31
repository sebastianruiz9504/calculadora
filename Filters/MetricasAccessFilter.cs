using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Metricas;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CotizadorInterno.Web.Filters;

public sealed class MetricasAccessFilter : IAsyncActionFilter
{
    public const string CurrentUserItemKey = "Metricas.CurrentUser";

    private readonly IDataverseService _dataverse;

    public MetricasAccessFilter(IDataverseService dataverse)
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
        if (!MetricasAccessPolicy.HasAccess(user))
        {
            currentUser = await _dataverse.GetCurrentUserAsync(context.HttpContext.RequestAborted);
            if (!MetricasAccessPolicy.HasAccess(currentUser?.Email))
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
