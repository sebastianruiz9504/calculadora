using CotizadorInterno.Web.Models.ProposalChat;

namespace CotizadorInterno.Web.Services;

public interface IAzureOpenAIProposalChatService
{
    Task<ProposalChatResponseDto> AskAsync(
        ProposalChatRequestDto request,
        CancellationToken ct = default);
}
