using CotizadorInterno.Web.Models.Contracts;

namespace CotizadorInterno.Web.Services;

public interface IContractsAiService
{
    Task<ContractRutExtractionDto> AnalyzeRutAsync(string fileName, string contentType, byte[] content, CancellationToken ct = default);
    Task<ContractOfferExtractionDto> AnalyzeOfferAsync(string fileName, string contentType, byte[] content, CancellationToken ct = default);
}
