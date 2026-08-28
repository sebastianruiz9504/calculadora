using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services;

public interface IAzureOpenAIDashboardAgentService
{
    Task<DashboardAgentChatResponseDto> AskAsync(
        DashboardAgentChatRequestDto request,
        CancellationToken ct = default);
}
