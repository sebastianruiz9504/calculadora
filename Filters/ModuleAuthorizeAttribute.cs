using CotizadorInterno.Web.Models.Permissions;
using Microsoft.AspNetCore.Mvc;

namespace CotizadorInterno.Web.Filters;

public sealed class ModuleAuthorizeAttribute : TypeFilterAttribute
{
    public ModuleAuthorizeAttribute(AppModule module)
        : base(typeof(ModuleAccessFilter))
    {
        Arguments = new object[] { module };
    }
}
