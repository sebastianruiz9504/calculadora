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
    public void EconomicOfferDefaultsToUsdAndOnlyCopEnablesProposalAmountOverrides()
    {
        var view = Read("Views", "Calculator", "Proposal.cshtml");
        var script = Read("wwwroot", "js", "calculator-proposal.js");
        var pdf = Read("wwwroot", "js", "proposal-pdf-v17.js");

        Assert.Contains("<option selected>USD</option>", view, StringComparison.Ordinal);
        Assert.Contains("<option>COP</option>", view, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"currency\" disabled", view, StringComparison.Ordinal);
        Assert.Contains("data-economic-source=\"calculator\"", view, StringComparison.Ordinal);
        Assert.Contains("Valores en USD bloqueados", view, StringComparison.Ordinal);
        Assert.Contains("Conversión manual en COP", script, StringComparison.Ordinal);
        Assert.Contains("dataset.economicMode", script, StringComparison.Ordinal);
        Assert.Contains("economicOverrides", script, StringComparison.Ordinal);
        Assert.Contains("schemaVersion: 2", script, StringComparison.Ordinal);
        Assert.Contains("effectiveLineAmounts", script, StringComparison.Ordinal);
        Assert.Contains("unitSale", script, StringComparison.Ordinal);
        Assert.Contains("monthlyTotalWithVat", script, StringComparison.Ordinal);
        Assert.Contains("contractTotalWithVat", script, StringComparison.Ordinal);
        Assert.Contains("Venta mensual", script, StringComparison.Ordinal);
        Assert.Contains("Venta anual / contrato", script, StringComparison.Ordinal);
        Assert.Contains("isHardwareProposal", script, StringComparison.Ordinal);
        Assert.Contains("economicHeaders.push(\"Duración\")", script, StringComparison.Ordinal);
        Assert.Contains("hideDuration: isHardwareProposal()", script, StringComparison.Ordinal);
        Assert.Contains("if(!hideDuration)", pdf, StringComparison.Ordinal);
        Assert.Contains("indexOf('hardware')", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("addEconomic", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("removeEconomic", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("saveScenarioToDataverse", script, StringComparison.Ordinal);
        Assert.DoesNotContain("contenteditable", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProposalConfigurationKeepsCalculatorEconomicsSeparateAndUsesV20VisualPdfWithoutAi()
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
        Assert.Contains("pendingExportConfigurationJson", script, StringComparison.Ordinal);
        Assert.Contains("pendingExportConfigurationJson !== configurationJson", script, StringComparison.Ordinal);
        Assert.DoesNotContain("nvalencia@digitaltechcolombia.com", pdf, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("321) 256 5005", pdf, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValueAddedAndHistoryRemainBoundedAndPdfPaginatesLongContent()
    {
        var view = Read("Views", "Calculator", "Proposal.cshtml");
        var script = Read("wwwroot", "js", "calculator-proposal.js");
        var pdf = Read("wwwroot", "js", "proposal-pdf-v17.js");
        var workspaceCss = Read("wwwroot", "css", "calculator-workspace.css");

        Assert.Contains("maxRows: 24", script, StringComparison.Ordinal);
        Assert.Contains("front: 80", script, StringComparison.Ordinal);
        Assert.Contains("name: 160", script, StringComparison.Ordinal);
        Assert.Contains("detail: 600", script, StringComparison.Ordinal);
        Assert.Contains("input.maxLength = maxLength", script, StringComparison.Ordinal);
        Assert.Contains("storedValues.slice(0, valueLimits.maxRows)", script, StringComparison.Ordinal);
        Assert.Contains("hasta 24 valores agregados", view, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("DTPDF.prototype._splitToken", pdf, StringComparison.Ordinal);
        Assert.Contains("(d.valoresAgregados||[]).slice(0,24)", pdf, StringComparison.Ordinal);
        Assert.Contains("needSpace(15)", pdf, StringComparison.Ordinal);
        Assert.Contains("needSpace(14)", pdf, StringComparison.Ordinal);
        Assert.Contains("No se agregaron valores adicionales a esta propuesta.", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("var defGen=", pdf, StringComparison.Ordinal);

        Assert.Contains("max-height: 340px", workspaceCss, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto", workspaceCss, StringComparison.Ordinal);
        Assert.Contains("overscroll-behavior: contain", workspaceCss, StringComparison.Ordinal);
    }

    [Fact]
    public void LongCommercialFieldsUseMeasuredWrappingAndSafePageFlow()
    {
        var pdf = Read("wwwroot", "js", "proposal-pdf-v17.js");

        Assert.Contains("fitWrappedText", pdf, StringComparison.Ordinal);
        Assert.Contains("drawWrappedText", pdf, StringComparison.Ordinal);
        Assert.Contains("drawBackContactRow", pdf, StringComparison.Ordinal);
        Assert.Contains("organizerW", pdf, StringComparison.Ordinal);
        Assert.Contains("conditionRows", pdf, StringComparison.Ordinal);
        Assert.Contains("conditionsH", pdf, StringComparison.Ordinal);
        Assert.Contains("proposalNoteOffset", pdf, StringComparison.Ordinal);
        Assert.Contains("needSpace(110+18+conditionsH+8)", pdf, StringComparison.Ordinal);
        Assert.Contains("this._wrap(String(txt).toUpperCase()", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain(".slice(0,2)", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("p._textAbs(ox,by-42,d.comercial", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("p._tw(d.comercial||'Digital Tech'", pdf, StringComparison.Ordinal);
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
