using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class ConciliacionVerticalAndMultiCustomerUiContractTests
{
    private static readonly string ProjectRoot = FindProjectRoot();

    [Fact]
    public void VerticalFiltersAreExactAndTheInitialTableIsCloudEntriesOnly()
    {
        var script = File.ReadAllText(Path.Combine(ProjectRoot, "wwwroot", "js", "conciliacion.js"));
        var view = File.ReadAllText(Path.Combine(ProjectRoot, "Views", "Conciliacion", "_Conciliacion2.cshtml"));

        Assert.Contains("return normalizedFlow === normalizeText(activeVertical);", script, StringComparison.Ordinal);
        Assert.Contains("return false;", script, StringComparison.Ordinal);
        Assert.Contains("row.SourceFlow?.Trim(), \"Cloud\"", view, StringComparison.Ordinal);
        Assert.Contains("row.SourceFlow?.Trim(), \"Copiers\"", view, StringComparison.Ordinal);
        Assert.Contains("verticalFilter == \"cloud\" && directionFilter == \"entrada\"", view, StringComparison.Ordinal);
        Assert.Contains("@Number(defaultVisibleRows) filas", view, StringComparison.Ordinal);
    }

    [Fact]
    public void ExitCategoriesAndClientPaymentsExposeTheApprovedOptions()
    {
        var script = File.ReadAllText(Path.Combine(ProjectRoot, "wwwroot", "js", "conciliacion.js"));

        Assert.Contains("{ value: \"traslado-interno\", label: \"Traslado interno\" }", script, StringComparison.Ordinal);
        Assert.Contains("data-cnc-wizard-client-add-company", script, StringComparison.Ordinal);
        Assert.Contains("customerIdentification: invoice.customerIdentification", script, StringComparison.Ordinal);
        Assert.Contains("customerBranchOffice: Number(invoice.customerBranchOffice", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplierPaymentAllocatesThePaidValueWhileDianBaseOnlyCalculatesRetentions()
    {
        var script = File.ReadAllText(Path.Combine(ProjectRoot, "wwwroot", "js", "conciliacion.js"));
        var controller = File.ReadAllText(Path.Combine(ProjectRoot, "Controllers", "ConciliacionController.cs"));
        var persistence = File.ReadAllText(Path.Combine(ProjectRoot, "Services", "DataverseService.ConciliacionDianActions.cs"));
        var models = File.ReadAllText(Path.Combine(ProjectRoot, "Models", "Conciliacion", "ConciliacionModels.cs"));

        Assert.Contains("Math.abs(distributed - paymentValue) > 1", script, StringComparison.Ordinal);
        Assert.Contains("Distribuye el valor pagado de ${money(paymentValue)} entre Cloud y Copiers.", script, StringComparison.Ordinal);
        Assert.Contains("La base DIAN se usa solamente para calcular las retenciones.", script, StringComparison.Ordinal);
        Assert.Contains("paymentValue * cloudValue / storedAllocation", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Distribuye la base DIAN de ${money(base)}", script, StringComparison.Ordinal);

        Assert.Contains("PaymentValue = allocation.AppliedValue", controller, StringComparison.Ordinal);
        Assert.Contains("Cloud y Copiers deben sumar el valor pagado de {paymentValue:N2}", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("Cloud y Copiers deben sumar la base DIAN de {purchase.DataverseBaseAmount:N2}", controller, StringComparison.Ordinal);

        Assert.Contains("public decimal PaymentValue { get; set; }", models, StringComparison.Ordinal);
        Assert.Contains("var paymentValue = RoundCurrency(request.PaymentValue);", persistence, StringComparison.Ordinal);
        Assert.Contains("Cloud y Copiers deben sumar el valor pagado de {paymentValue:N2}.", persistence, StringComparison.Ordinal);
        Assert.DoesNotContain("Cloud y Copiers deben sumar la base DIAN de {allocationBase:N2}.", persistence, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CotizadorInterno.Web.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro la raiz del proyecto CotizadorInterno.Web.");
    }
}
