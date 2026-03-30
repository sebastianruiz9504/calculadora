using CotizadorInterno.Web.Models.Calculator;

namespace CotizadorInterno.Web.Services.Calculator;

public interface IQuoteCalculator
{
    QuoteScenarioResult Calculate(QuoteScenarioInput input);
}
