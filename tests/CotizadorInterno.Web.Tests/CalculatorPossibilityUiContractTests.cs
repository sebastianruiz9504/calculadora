using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CalculatorPossibilityUiContractTests
{
    private static readonly string ProjectRoot = FindProjectRoot();

    [Fact]
    public void WorkspaceOwnsPossibilityLifecycleAndLimitsTheGroupToThree()
    {
        var workspace = Read("Views", "Calculator", "Workspace.cshtml");

        Assert.Contains("group.Possibilities.Any(possibility =>", workspace, StringComparison.Ordinal);
        Assert.Contains("possibility.ScenarioId, requestedScenarioId", workspace, StringComparison.Ordinal);
        Assert.Contains("var contextualCrmDealId = Context.Request.Query[\"crmDealId\"]", workspace, StringComparison.Ordinal);
        Assert.Contains("? contextualCrmDealId", workspace, StringComparison.Ordinal);
        Assert.Contains("data-create-url=\"@Url.Action(\"CreatePossibility\", \"Calculator\")\"", workspace, StringComparison.Ordinal);
        Assert.Contains("data-recommend-url=\"@Url.Action(\"RecommendPossibility\", \"Calculator\")\"", workspace, StringComparison.Ordinal);
        Assert.Contains("data-delete-url=\"@Url.Action(\"DeleteScenario\", \"Calculator\")\"", workspace, StringComparison.Ordinal);
        Assert.Contains("activePossibilities.Count >= 3", workspace, StringComparison.Ordinal);
        Assert.Contains("activePossibilities.Count <= 1", workspace, StringComparison.Ordinal);
        Assert.Contains("data-duplicate-possibility", workspace, StringComparison.Ordinal);
        Assert.Contains("data-recommend-possibility", workspace, StringComparison.Ordinal);
    }

    [Fact]
    public void EachPossibilityUsesAnIsolatedEmbeddedCalculatorWithExplicitCalculationSave()
    {
        var workspace = Read("Views", "Calculator", "Workspace.cshtml");
        var calculator = Read("Views", "Calculator", "Index.cshtml");

        Assert.Contains("data-calculator-frame", workspace, StringComparison.Ordinal);
        Assert.Contains("embedded=true", workspace, StringComparison.Ordinal);
        Assert.Contains("tab.dataset.sid = String(s.id || \"\")", calculator, StringComparison.Ordinal);
        Assert.DoesNotContain("data-sid=\"${s.id}\"", calculator, StringComparison.Ordinal);
        Assert.Contains("Math.max(1, Math.ceil(requestedHeight))", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.max(620, Math.ceil(requestedHeight))", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.min(3200", workspace, StringComparison.Ordinal);
        Assert.Contains("calculatorInstance?.scrollHeight", calculator, StringComparison.Ordinal);
        Assert.Contains("resizeObserver.observe(calculatorInstance || document.body)", calculator, StringComparison.Ordinal);
        Assert.DoesNotContain("document.documentElement.scrollHeight", calculator, StringComparison.Ordinal);
        Assert.Contains("_CalculatorEmbeddedLayout", calculator, StringComparison.Ordinal);
        Assert.DoesNotContain("const AUTOSAVE_DELAY_MS", calculator, StringComparison.Ordinal);
        Assert.DoesNotContain("expectedRowVersion: scenario.rowVersion", calculator, StringComparison.Ordinal);
        Assert.Contains("lineId: line.lineId", calculator, StringComparison.Ordinal);
        Assert.Contains("lineOrder: Number(line.lineOrder", calculator, StringComparison.Ordinal);
        Assert.Contains("possibilityName: scenario.possibilityName", calculator, StringComparison.Ordinal);
        Assert.Contains("possibilityOrder: Number(scenario.possibilityOrder", calculator, StringComparison.Ordinal);
        Assert.Contains("source: \"digitaltech-calculator\"", calculator, StringComparison.Ordinal);
        Assert.Contains("if (!embeddedCalculator)", calculator, StringComparison.Ordinal);
        Assert.DoesNotContain("keepalive: Boolean(options.keepalive)", calculator, StringComparison.Ordinal);
        Assert.DoesNotContain("flushPendingScenarioAutosavesForNavigation", calculator, StringComparison.Ordinal);
        Assert.DoesNotContain("window.addEventListener(\"beforeunload\"", calculator, StringComparison.Ordinal);
        Assert.Contains("lineEventsAbortController?.abort()", calculator, StringComparison.Ordinal);
        Assert.Contains("signal: lineEventSignal", calculator, StringComparison.Ordinal);
        Assert.Contains("costInput.addEventListener(\"input\", captureFocusedNumericInput", calculator, StringComparison.Ordinal);
        Assert.Contains("marginInput.addEventListener(\"input\", captureFocusedNumericInput", calculator, StringComparison.Ordinal);
        Assert.Contains("monthsInput.addEventListener(\"input\", captureFocusedNumericInput", calculator, StringComparison.Ordinal);
        Assert.Contains("qtyInput.addEventListener(\"input\", captureFocusedNumericInput", calculator, StringComparison.Ordinal);
        Assert.Contains("const captureFocusedNumericInput = () => syncLineInputs(false)", calculator, StringComparison.Ordinal);
        Assert.Contains("raw.split(thousandsSeparator).join(\"\")", calculator, StringComparison.Ordinal);
        Assert.Contains("Number.isFinite(n)", calculator, StringComparison.Ordinal);
        Assert.Contains("ensurePossibilitiesSaved", workspace, StringComparison.Ordinal);
        Assert.Contains("hasUnsavedPossibilities", workspace, StringComparison.Ordinal);
        Assert.Contains("Calcula y guarda todos los escenarios incluidos", workspace, StringComparison.Ordinal);
    }

    [Fact]
    public void PossibilitiesCanBeCompactedWithoutUnmountingTheirCalculator()
    {
        var workspace = Read("Views", "Calculator", "Workspace.cshtml");
        var styles = Read("wwwroot", "css", "calculator-workspace.css");

        Assert.Contains("data-toggle-possibility", workspace, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"true\"", workspace, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"@possibilityPanelId\"", workspace, StringComparison.Ordinal);
        Assert.Contains("data-possibility-panel", workspace, StringComparison.Ordinal);
        Assert.Contains("setPossibilityExpanded(card", workspace, StringComparison.Ordinal);
        Assert.Contains("panel.hidden = !expanded", workspace, StringComparison.Ordinal);
        Assert.Contains("card.classList.toggle(\"is-collapsed\"", workspace, StringComparison.Ordinal);
        Assert.Contains("calculator:request-resize", workspace, StringComparison.Ordinal);
        Assert.Contains("[data-possibility-panel][hidden]", styles, StringComparison.Ordinal);

        var panelStart = workspace.IndexOf("<div id=\"@possibilityPanelId\" data-possibility-panel>", StringComparison.Ordinal);
        var frameStart = workspace.IndexOf("data-calculator-frame", panelStart, StringComparison.Ordinal);
        Assert.True(panelStart >= 0);
        Assert.True(frameStart > panelStart);
        Assert.DoesNotContain("frame.remove()", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("frame.src =", workspace, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitSavePreservesLineIdentityWhileAProductSelectionIsInFlight()
    {
        var calculator = Read("Views", "Calculator", "Index.cshtml");

        Assert.Contains("Object.assign(currentLine, normalizedLine)", calculator, StringComparison.Ordinal);
        Assert.DoesNotContain("scenario.lines = normalizedLines", calculator, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceUsesASearchableBusinessPickerAndDismissibleInsightsRail()
    {
        var workspace = Read("Views", "Calculator", "Workspace.cshtml");
        var styles = Read("wwwroot", "css", "calculator-workspace.css");

        Assert.Contains("data-business-picker", workspace, StringComparison.Ordinal);
        Assert.Contains("data-business-search", workspace, StringComparison.Ordinal);
        Assert.Contains("data-business-option", workspace, StringComparison.Ordinal);
        Assert.Contains("businessSearch?.addEventListener(\"input\", filterBusinessOptions)", workspace, StringComparison.Ordinal);
        Assert.Contains("businessPickerPopover.hidden = !open", workspace, StringComparison.Ordinal);
        Assert.Contains("data-insights-toggle", workspace, StringComparison.Ordinal);
        Assert.Contains("data-insights-panel", workspace, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\"", workspace, StringComparison.Ordinal);
        Assert.Contains("aria-hidden=\"true\"", workspace, StringComparison.Ordinal);
        Assert.Contains("insightsPanel.hidden = !open", workspace, StringComparison.Ordinal);
        Assert.Contains("insightsPanel.inert = !open", workspace, StringComparison.Ordinal);
        Assert.Contains("insights?.addEventListener(\"pointerleave\"", workspace, StringComparison.Ordinal);
        Assert.Contains("if (insights && !insights.contains(event.target)) setInsightsOpen(false)", workspace, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 48px minmax(0, 1fr);", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("calculator-workspace__groups", workspace, StringComparison.Ordinal);
    }

    [Fact]
    public void BusinessAndScenarioNamesAreEditableAtTheirOwnLevels()
    {
        var workspace = Read("Views", "Calculator", "Workspace.cshtml");
        var calculator = Read("Views", "Calculator", "Index.cshtml");
        var proposal = Read("Views", "Calculator", "Proposal.cshtml");
        var controller = Read("Controllers", "CalculatorController.cs");
        var service = Read("Services", "DataverseService.CalculatorPossibilities.cs");

        Assert.Contains("Negocios guardados", workspace, StringComparison.Ordinal);
        Assert.Contains("Nombre del negocio", workspace, StringComparison.Ordinal);
        Assert.Contains("data-business-name", workspace, StringComparison.Ordinal);
        Assert.Contains("data-save-business-name", workspace, StringComparison.Ordinal);
        Assert.Contains("data-rename-group-url", workspace, StringComparison.Ordinal);
        Assert.Contains("await postJson(workspace.dataset.renameGroupUrl", workspace, StringComparison.Ordinal);
        Assert.Contains("Agregar escenario", workspace, StringComparison.Ordinal);
        Assert.Contains("Nombre del escenario", calculator, StringComparison.Ordinal);
        Assert.Contains("Negocio guardado", proposal, StringComparison.Ordinal);
        Assert.DoesNotContain("Escenario guardado", proposal, StringComparison.Ordinal);
        Assert.DoesNotContain("<small>Alternativas</small>", proposal, StringComparison.Ordinal);
        Assert.DoesNotContain("Por alternativa", proposal, StringComparison.Ordinal);
        Assert.DoesNotContain("Nueva posibilidad", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("Agregar posibilidad", workspace, StringComparison.Ordinal);
        Assert.Contains("Task<IActionResult> RenameScenarioGroup", controller, StringComparison.Ordinal);
        Assert.Contains("RenameCalculatorScenarioGroupAsync", service, StringComparison.Ordinal);

        var renameStart = service.IndexOf("private async Task<bool> RenameCalculatorScenarioGroupAsync", StringComparison.Ordinal);
        var renameEnd = service.IndexOf("private async Task<string> EnsureCalculatorGroupRecordAsync", renameStart, StringComparison.Ordinal);
        Assert.True(renameStart >= 0);
        Assert.True(renameEnd > renameStart);
        var renameMethod = service[renameStart..renameEnd];
        Assert.Contains("operations.AddRange(siblings.Select", renameMethod, StringComparison.Ordinal);
        Assert.Contains("CalculatorGroupNameField", renameMethod, StringComparison.Ordinal);
        Assert.Contains("groupRecord.ETag", renameMethod, StringComparison.Ordinal);
        Assert.Contains("item.ETag", renameMethod, StringComparison.Ordinal);
        Assert.Contains("\"POST\"", renameMethod, StringComparison.Ordinal);
        Assert.Contains("ExecuteCalculatorChangeSetAsync(operations", renameMethod, StringComparison.Ordinal);
        Assert.Equal(1, renameMethod.Split("ExecuteCalculatorChangeSetAsync(", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void ProposalAndExportHistoryAreOwnedByTheGroup()
    {
        var workspace = Read("Views", "Calculator", "Workspace.cshtml");

        Assert.Contains("data-open-proposal", workspace, StringComparison.Ordinal);
        Assert.Contains("data-crm-deal-id=", workspace, StringComparison.Ordinal);
        Assert.Contains("groupId: workspace.dataset.activeGroupId", workspace, StringComparison.Ordinal);
        Assert.Contains("scenarioId: workspace.dataset.primaryScenarioId", workspace, StringComparison.Ordinal);
        Assert.Contains("query.set(\"crmDealId\", workspace.dataset.crmDealId)", workspace, StringComparison.Ordinal);
        Assert.Contains("Historial de propuestas", workspace, StringComparison.Ordinal);
        Assert.Contains("Url.Action(\"ProposalExport\", \"Calculator\"", workspace, StringComparison.Ordinal);
        Assert.Contains("activeHistory.Count > 0 ? \"Editar propuesta\" : \"Generar propuesta\"", workspace, StringComparison.Ordinal);
    }

    [Fact]
    public void CalculationKeepsTheCapturedScenarioAcrossUserEdits()
    {
        var calculator = Read("Views", "Calculator", "Index.cshtml");

        var capture = calculator.IndexOf("const targetScenario = getActiveScenario();", StringComparison.Ordinal);
        var payload = calculator.IndexOf("const payload = buildPayload({ scenario: targetScenario });", capture, StringComparison.Ordinal);
        var revision = calculator.IndexOf("const calculationRevision = Number(targetScenario.changeRevision || 0);", payload, StringComparison.Ordinal);
        var fetch = calculator.IndexOf("fetch(\"/Calculator/Calculate\"", revision, StringComparison.Ordinal);
        var save = calculator.IndexOf("await saveScenarioToDataverse(s, payload.lines);", fetch, StringComparison.Ordinal);

        Assert.True(capture >= 0);
        Assert.True(payload > capture);
        Assert.True(revision > payload);
        Assert.True(fetch > revision);
        Assert.True(save > fetch);
        Assert.Contains("Number(s.changeRevision || 0) !== calculationRevision", calculator, StringComparison.Ordinal);
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
