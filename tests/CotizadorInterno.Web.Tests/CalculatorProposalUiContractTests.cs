using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CalculatorProposalUiContractTests
{
    private static readonly string ProjectRoot = FindProjectRoot();

    [Fact]
    public void CalculatorOpensTheSavedScenarioInTheDedicatedConfigurator()
    {
        var calculator = Read("Views", "Calculator", "Index.cshtml");

        Assert.Contains("id=\"btnGenerateProposal\"", calculator, StringComparison.Ordinal);
        Assert.Contains("openProposalConfigurator", calculator, StringComparison.Ordinal);
        Assert.Contains("await saveScenarioToDataverse(s, lines)", calculator, StringComparison.Ordinal);
        Assert.Contains("scenarioId: s.id", calculator, StringComparison.Ordinal);
        Assert.Contains("window.location.assign", calculator, StringComparison.Ordinal);
    }

    [Fact]
    public void EconomicOfferIsLockedAndContainsMonthlyAndContractTotals()
    {
        var view = Read("Views", "Calculator", "Proposal.cshtml");
        var script = Read("wwwroot", "js", "calculator-proposal.js");

        Assert.Contains("data-economic-locked=\"true\"", view, StringComparison.Ordinal);
        Assert.Contains("Información recalculada en el servidor", view, StringComparison.Ordinal);
        Assert.Contains("Venta mensual", view, StringComparison.Ordinal);
        Assert.Contains("Total contrato", view, StringComparison.Ordinal);
        Assert.DoesNotContain("addEconomic", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("removeEconomic", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contenteditable", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProposalConfigurationKeepsLockedEconomicsAndUsesV20VisualPdfWithoutAi()
    {
        var view = Read("Views", "Calculator", "Proposal.cshtml");
        var script = Read("wwwroot", "js", "calculator-proposal.js");
        var pdf = Read("wwwroot", "js", "proposal-pdf-v17.js");

        Assert.Contains("Configuración de la solución", view, StringComparison.Ordinal);
        Assert.Contains("Valores agregados", view, StringComparison.Ordinal);
        Assert.Contains("Generar oferta PDF", view, StringComparison.Ordinal);
        Assert.Contains("Microsoft Defender", script, StringComparison.Ordinal);
        Assert.Contains("Revisión de costos (FinOps)", script, StringComparison.Ordinal);
        Assert.Contains("window.generateProposalPdf", pdf, StringComparison.Ordinal);
        Assert.Contains("%PDF-1.4", pdf, StringComparison.Ordinal);
        Assert.Contains("Valor del contrato", pdf, StringComparison.Ordinal);
        Assert.Contains("Microsoft Solutions Partner - Modern Work.", pdf, StringComparison.Ordinal);
        Assert.Contains("\"cert_partner_sec\"", pdf, StringComparison.Ordinal);
        Assert.Contains("\"cert_partner_mw\"", pdf, StringComparison.Ordinal);
        Assert.Contains("\"cert_mct2\"", pdf, StringComparison.Ordinal);
        Assert.Contains("\"badge_cyber\"", pdf, StringComparison.Ordinal);
        Assert.Contains("\"badge_ea\"", pdf, StringComparison.Ordinal);
        Assert.Contains("\"badge_azure\"", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("totalAnual", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("T. ANUAL", pdf, StringComparison.Ordinal);
        Assert.Contains("'Notas aclaratorias'", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("p._textAbs(p.mL+16,p.y-22,'Notas'", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("AzureOpenAI", view + script + pdf, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/GenerateProposal", view + script + pdf, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AzureOpenAIQuoteProposal", view + script + pdf, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nvalencia@digitaltechcolombia.com", pdf, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("321) 256 5005", pdf, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublicProposalModelDoesNotExposeInternalCalculationInputs()
    {
        var model = Read("Models", "Calculator", "ProposalConfigurationModels.cs");
        var view = Read("Views", "Calculator", "Proposal.cshtml");

        Assert.DoesNotContain("CostUnit", model, StringComparison.Ordinal);
        Assert.DoesNotContain("MarginPercent", model, StringComparison.Ordinal);
        Assert.DoesNotContain("Acelerador", model, StringComparison.Ordinal);
        Assert.DoesNotContain("Points", model, StringComparison.Ordinal);
        Assert.DoesNotContain("Commission", model, StringComparison.Ordinal);
        Assert.Contains("JsonSerializer.Serialize(Model", view, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([ProjectRoot, .. parts]));

    private static string FindProjectRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CotizadorInterno.Web.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró la raíz de CotizadorInterno.Web.");
    }
}
