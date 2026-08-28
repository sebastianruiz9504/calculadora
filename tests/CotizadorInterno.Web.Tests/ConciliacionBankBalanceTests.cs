using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Services;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class ConciliacionBankBalanceTests
{
    [Fact]
    public void CurrentBalanceIsOpeningPlusEntriesMinusExitsForTheRequestedPeriod()
    {
        const string bankKey = "Cloud|11100504";
        var balances = DataverseService.BuildConciliacionCashFlowBankBalances(
            new[]
            {
                Movement("2026-07-02", "Cloud", "11100504", "Bancolombia Cloud", entry: 1_250_000m),
                Movement("2026-07-14", "Cloud", "11100504", "Bancolombia Cloud", exit: 325_400m),
                Movement("2026-08-01", "Cloud", "11100504", "Bancolombia Cloud", entry: 9_999_999m)
            },
            2026,
            7,
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                [bankKey] = 2_000_000m
            });

        var balance = Assert.Single(balances, item => item.BankKey == bankKey);
        Assert.Equal(bankKey, balance.BankKey);
        Assert.Equal("Cloud", balance.SourceFlow);
        Assert.Equal("11100504", balance.BankAccountCode);
        Assert.Equal("Bancolombia Cloud", balance.BankAccountName);
        Assert.True(balance.HasOpeningBalance);
        Assert.Equal(2_000_000m, balance.OpeningBalance);
        Assert.Equal(1_250_000m, balance.TotalEntries);
        Assert.Equal(325_400m, balance.TotalExits);
        Assert.Equal(2_924_600m, balance.CurrentBalance);
    }

    [Fact]
    public void InternalTransferDecreasesOriginAndIncreasesDestination()
    {
        const string cloudKey = "Cloud|11100504";
        const string copiersKey = "Copiers|11100505";
        var balances = DataverseService.BuildConciliacionCashFlowBankBalances(
            new[]
            {
                Movement(
                    "2026-07-20",
                    "Cloud",
                    "11100504",
                    "Bancolombia Cloud",
                    exit: 250_000m,
                    sourceKind: "Traslado"),
                Movement(
                    "2026-07-20",
                    "Copiers",
                    "11100505",
                    "Bancolombia Copiers",
                    entry: 250_000m,
                    sourceKind: "Traslado")
            },
            2026,
            7,
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                [cloudKey] = 1_000_000m,
                [copiersKey] = 400_000m
            });

        var cloud = Assert.Single(balances, item => item.BankKey == cloudKey);
        var copiers = Assert.Single(balances, item => item.BankKey == copiersKey);

        Assert.Equal(0m, cloud.TotalEntries);
        Assert.Equal(250_000m, cloud.TotalExits);
        Assert.Equal(750_000m, cloud.CurrentBalance);

        Assert.Equal(250_000m, copiers.TotalEntries);
        Assert.Equal(0m, copiers.TotalExits);
        Assert.Equal(650_000m, copiers.CurrentBalance);
    }

    [Fact]
    public void OpeningBalanceAndMovementsStayIsolatedByCanonicalBankKey()
    {
        const string cloudKey = "Cloud|11100504";
        const string copiersKey = "Copiers|11100505";
        var balances = DataverseService.BuildConciliacionCashFlowBankBalances(
            new[]
            {
                Movement("2026-07-03", "Cloud", "11100504", "Cuenta principal", entry: 100m),
                Movement("2026-07-03", "Copiers", "11100505", "Cuenta principal", exit: 30m)
            },
            2026,
            7,
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                [cloudKey] = 500m
            });

        var cloud = Assert.Single(balances, item => item.BankKey == cloudKey);
        var copiers = Assert.Single(balances, item => item.BankKey == copiersKey);

        Assert.True(cloud.HasOpeningBalance);
        Assert.Equal(600m, cloud.CurrentBalance);

        Assert.False(copiers.HasOpeningBalance);
        Assert.Equal(0m, copiers.OpeningBalance);
        Assert.Equal(-30m, copiers.CurrentBalance);
    }

    private static ConciliacionCashFlowRowDto Movement(
        string date,
        string sourceFlow,
        string bankAccountCode,
        string bankAccountName,
        decimal entry = 0m,
        decimal exit = 0m,
        string sourceKind = "Movimiento") =>
        new()
        {
            MovementDateValue = date,
            SourceFlow = sourceFlow,
            BankAccountCode = bankAccountCode,
            BankAccountName = bankAccountName,
            SourceKind = sourceKind,
            Direction = entry > 0m ? "Entrada" : "Salida",
            EntryValue = entry,
            ExitValue = exit,
            Amount = Math.Max(entry, exit)
        };
}
