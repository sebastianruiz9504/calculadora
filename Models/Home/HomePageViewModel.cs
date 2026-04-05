using CotizadorInterno.Web.Models.Permissions;

namespace CotizadorInterno.Web.Models.Home;

public sealed class HomePageViewModel
{
    public CurrentUserInfo CurrentUser { get; init; } = new();
    public string UserDisplayName { get; init; } = "Usuario";
    public IReadOnlyList<AppModuleDefinition> AvailableModules { get; init; } = Array.Empty<AppModuleDefinition>();
}
