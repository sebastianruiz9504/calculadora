using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class ConciliacionBankBalanceContractTests
{
    private static readonly string ProjectRoot = FindProjectRoot();

    [Fact]
    public void BankBalanceDtoKeepsTheCanonicalBankAndCalculationFields()
    {
        var dtoType = FindProductionType(
            "CotizadorInterno.Web.Models.Conciliacion.ConciliacionBankBalanceDto");

        AssertProperties(
            dtoType,
            "BankKey",
            "BankLabel",
            "SourceFlow",
            "BankAccountCode",
            "BankAccountName",
            "OpeningBalance",
            "HasOpeningBalance",
            "TotalEntries",
            "TotalExits",
            "CurrentBalance");
    }

    [Fact]
    public void OpeningBalanceRequestIsScopedToPeriodAndCanonicalBank()
    {
        var requestType = FindProductionType(
            "CotizadorInterno.Web.Models.Conciliacion.ConciliacionBankOpeningBalanceRequest");

        AssertProperties(requestType, "Year", "Month", "BankKey", "OpeningBalance");
    }

    [Fact]
    public void BankBalanceEndpointsSeparateReadFromProtectedWrite()
    {
        var actions = typeof(Controllers.ConciliacionController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public);
        var getAction = actions.Single(method => string.Equals(
            method.Name,
            "BankBalances",
            StringComparison.Ordinal));
        var postAction = actions.Single(method => string.Equals(
            method.Name,
            "SetBankOpeningBalance",
            StringComparison.Ordinal));

        Assert.NotNull(getAction.GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(getAction.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(postAction.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(postAction.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.Contains(
            postAction.GetParameters(),
            parameter => string.Equals(
                parameter.ParameterType.Name,
                "ConciliacionBankOpeningBalanceRequest",
                StringComparison.Ordinal));
    }

    [Fact]
    public void OpeningBalanceWriteActionCarriesTheAntiforgeryAttributeAtRuntime()
    {
        var action = typeof(Controllers.ConciliacionController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => string.Equals(
                method.Name,
                "SetBankOpeningBalance",
                StringComparison.Ordinal));

        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void Conciliacion2RendersAccessibleBalanceCardAndOpeningBalanceDialog()
    {
        var view = ReadProjectFile("Views", "Conciliacion", "_Conciliacion2.cshtml");

        Assert.Contains("data-cnc-bank-balance-card", view, StringComparison.Ordinal);
        Assert.Contains("data-cnc-bank-balance-select", view, StringComparison.Ordinal);
        Assert.Contains("data-cnc-bank-balance-current", view, StringComparison.Ordinal);
        Assert.Contains("data-cnc-bank-balance-opening", view, StringComparison.Ordinal);
        Assert.Contains("data-cnc-bank-balance-entries", view, StringComparison.Ordinal);
        Assert.Contains("data-cnc-bank-balance-exits", view, StringComparison.Ordinal);
        Assert.Contains("data-cnc-bank-balance-open-opening", view, StringComparison.Ordinal);
        Assert.Contains("data-cnc-bank-balance-save", view, StringComparison.Ordinal);
        Assert.Contains("data-cnc-bank-balance-modal", view, StringComparison.Ordinal);
        Assert.Contains("Poner saldo inicial", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BalanceCardIsRefreshedAfterBankImportAndKeepsAResponsiveSymmetricLayout()
    {
        var script = ReadProjectFile("wwwroot", "js", "conciliacion.js");
        var styles = ReadProjectFile("wwwroot", "css", "conciliacion.css");

        Assert.Contains("data-cnc-bank-balance-select", script, StringComparison.Ordinal);
        Assert.Contains("data-cnc-bank-balance-current", script, StringComparison.Ordinal);
        Assert.Contains("data-cnc-bank-balance-save", script, StringComparison.Ordinal);
        Assert.Contains("data-cnc-bank-balance-modal", script, StringComparison.Ordinal);
        Assert.Contains("bankBalances", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bankImport", script, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("[data-cnc-bank-balance-card]", styles, StringComparison.Ordinal);
        Assert.Contains("align-items: stretch", styles, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("height: 100%", styles, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@media", styles, StringComparison.OrdinalIgnoreCase);
    }

    private static Type FindProductionType(string fullName) =>
        typeof(Controllers.ConciliacionController).Assembly.GetType(fullName)
        ?? throw new Xunit.Sdk.XunitException($"No se encontro el contrato {fullName}.");

    private static void AssertProperties(Type type, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            Assert.NotNull(type.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public));
        }
    }

    private static string ReadProjectFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([ProjectRoot, .. parts]));

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CotizadorInterno.Web.csproj")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "No se encontro la raiz del proyecto CotizadorInterno.Web.");
    }
}
