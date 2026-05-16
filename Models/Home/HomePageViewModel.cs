using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.Tasks;

namespace CotizadorInterno.Web.Models.Home;

public sealed class HomePageViewModel
{
    public CurrentUserInfo CurrentUser { get; init; } = new();
    public string UserDisplayName { get; init; } = "Usuario";
    public IReadOnlyList<TaskBoardItemDto> PendingTasks { get; init; } = Array.Empty<TaskBoardItemDto>();
    public IReadOnlyList<AppModuleDefinition> AvailableModules { get; init; } = Array.Empty<AppModuleDefinition>();
    public bool CanManagePublicDataExport { get; init; }
}
