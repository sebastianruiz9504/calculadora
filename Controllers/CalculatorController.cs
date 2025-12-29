using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Calculator;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.Calculator;

namespace CotizadorInterno.Web.Controllers;

public sealed class CalculatorController : Controller
{
    private readonly IDataverseService _dataverse;
    private readonly IQuoteCalculator _calculator;
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";

    public CalculatorController(IDataverseService dataverse, IQuoteCalculator calculator)
    {
        _dataverse = dataverse;
        _calculator = calculator;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var segment = await _dataverse.GetCurrentUserSegmentAsync(ct);
        ViewData["Segment"] = segment;
        return View();
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ProductSearch([FromQuery] string q, CancellationToken ct)
    {
        var items = await _dataverse.SearchProductsAsync(q, top: 12, ct: ct);
        return Json(items);
    }

    [HttpPost]
    public async Task<IActionResult> Calculate([FromBody] QuoteScenarioInput input, CancellationToken ct)
    {
        // Segmento desde Dataverse (puedes cachearlo luego)
        var segment = await _dataverse.GetCurrentUserSegmentAsync(ct);

        // Calcula (utility oculto, devuelve puntos+comisión)
        var result = _calculator.Calculate(input, segment);

        return Json(new
        {
            points = result.Points,
            commission = result.Commission,
            prorationDays = result.ProrationDays,
            prorationFactor = result.ProrationFactor,
            totalMonthlySale = result.TotalMonthlySale,
            totalSale = result.TotalSale,
            segment = segment.ToString()
        });
    }
}
