using CotizadorInterno.Web.Models.Calculator;

namespace CotizadorInterno.Web.Services;

public interface IAzureOpenAIQuoteProposalService
{
    Task<string> GenerateProposalHtmlAsync(
        QuoteProposalGenerationInput input,
        CancellationToken ct = default);
}
