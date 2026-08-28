using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class ConciliacionTransferUiContractTests
{
    private static readonly string ProjectRoot = FindProjectRoot();

    [Fact]
    public void OnlyFinalConciliatedOrNoSiigoRowsRenderAsClosed()
    {
        var view = File.ReadAllText(
            Path.Combine(ProjectRoot, "Views", "Conciliacion", "_Conciliacion2.cshtml"));
        var styles = File.ReadAllText(
            Path.Combine(ProjectRoot, "wwwroot", "css", "conciliacion.css"));

        Assert.Contains("CashFlowFinalConciliated", view, StringComparison.Ordinal);
        Assert.Contains("CashFlowClosedWithoutSiigo", view, StringComparison.Ordinal);
        Assert.Contains("var pending = !omitted && !conciliated;", view, StringComparison.Ordinal);
        Assert.Contains("pending ? \"is-review-pending\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("CashFlowReportedForClose", view, StringComparison.Ordinal);
        Assert.DoesNotContain("is-siigo-pending", view, StringComparison.Ordinal);
        Assert.DoesNotContain("cnc-v2-state--info", view, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label=\"Siigo pendiente\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain(".cnc-click-row.is-siigo-pending td", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectingInternalTransferOpensTheRealSiigoAccountingVoucherFlow()
    {
        var script = File.ReadAllText(
            Path.Combine(ProjectRoot, "wwwroot", "js", "conciliacion.js"));

        Assert.Contains("const isPendingSiigoCashFlowCategory", script, StringComparison.Ordinal);
        Assert.Contains("const markCashFlowRowPendingSiigo", script, StringComparison.Ordinal);
        Assert.Contains("updateConciliacion2CheckState(targetRow, false);", script, StringComparison.Ordinal);
        Assert.Contains("value === \"no-incluida-conciliacion\"", script, StringComparison.Ordinal);
        Assert.Contains("case \"traslado-interno\":", script, StringComparison.Ordinal);
        Assert.Contains("renderCashFlowWizardAccountingVoucher(row);", script, StringComparison.Ordinal);
        Assert.Contains("Cuenta bancaria contraparte", script, StringComparison.Ordinal);
        Assert.Contains(
            "<select class=\"form-select\" data-cnc-wizard-voucher-account>${accountHtml}</select>",
            script,
            StringComparison.Ordinal);
        Assert.Contains("Selecciona la cuenta bancaria contraparte.", script, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "data-cnc-wizard-voucher-account${isInternalTransfer ? \" disabled\" : \"\"}",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "El envio a Siigo queda pendiente para la siguiente fase.",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "value === \"traslado-interno\" || value === \"no-incluida-conciliacion\"",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ConciliatedRowsShowTheCreatedSiigoDocumentInDescription()
    {
        var view = File.ReadAllText(
            Path.Combine(ProjectRoot, "Views", "Conciliacion", "_Conciliacion2.cshtml"));
        var script = File.ReadAllText(
            Path.Combine(ProjectRoot, "wwwroot", "js", "conciliacion.js"));

        Assert.Contains("string ConciliatedDescription", view, StringComparison.Ordinal);
        Assert.Contains("$\"{description} - {siigoDocumentName}\"", view, StringComparison.Ordinal);
        Assert.Contains("var userDescription = ConciliatedDescription(row, conciliated);", view, StringComparison.Ordinal);
        Assert.Contains("const appendSiigoDocumentToConciliacion2Description", script, StringComparison.Ordinal);
        Assert.Contains("appendSiigoDocumentToConciliacion2Description(targetRow, payloadRow);", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ClientPaymentCanBeLeftPendingWithReasonAndOrangeState()
    {
        var index = File.ReadAllText(
            Path.Combine(ProjectRoot, "Views", "Conciliacion", "Index.cshtml"));
        var view = File.ReadAllText(
            Path.Combine(ProjectRoot, "Views", "Conciliacion", "_Conciliacion2.cshtml"));
        var script = File.ReadAllText(
            Path.Combine(ProjectRoot, "wwwroot", "js", "conciliacion.js"));
        var styles = File.ReadAllText(
            Path.Combine(ProjectRoot, "wwwroot", "css", "conciliacion.css"));

        Assert.Contains("data-cashflow-pending-url", index, StringComparison.Ordinal);
        Assert.Contains("CashFlowPendingReview", view, StringComparison.Ordinal);
        Assert.Contains("is-review-pending", view, StringComparison.Ordinal);
        Assert.Contains("data-cnc-wizard-client-leave-pending>Dejar pendiente", script, StringComparison.Ordinal);
        Assert.Contains("modal.id = \"cncCashFlowPendingModal\"", script, StringComparison.Ordinal);
        Assert.Contains("data-cnc-cashflow-pending-reason", script, StringComparison.Ordinal);
        Assert.Contains("const markCashFlowRowPendingReview", script, StringComparison.Ordinal);
        Assert.Contains(".cnc-click-row.is-review-pending td", styles, StringComparison.Ordinal);
        Assert.Contains(".cnc-v2-state--review", styles, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CotizadorInterno.Web.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "No se encontro la raiz del proyecto CotizadorInterno.Web.");
    }
}
