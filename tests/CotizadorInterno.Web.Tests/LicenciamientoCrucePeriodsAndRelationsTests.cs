using System.Collections;
using System.Reflection;
using CotizadorInterno.Web.Services;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class LicenciamientoCrucePeriodsAndRelationsTests
{
    [Theory]
    [InlineData("09/2026", "2026-10-05", "2026-09-01")]
    [InlineData("08/2026", "2026-09-05", "2026-08-01")]
    [InlineData("12/2025", "2026-01-05", "2025-12-01")]
    [InlineData("2026-09", "2026-10-05", "2026-09-01")]
    [InlineData(" 9.2026 ", "2026-10-05", "2026-09-01")]
    [InlineData("", "2026-10-05", "2026-09-01")]
    [InlineData("invalid", "2026-01-05", "2025-12-01")]
    [InlineData("07/2026", "2026-10-05", "2026-07-01")]
    public void ConsumptionIsAuthoritativeAndInvoiceFallbackSubtractsOneMonth(string interval, string invoice, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), LicensingConsumptionPeriod.ConsumptionMonth(interval, DateOnly.Parse(invoice)));

    [Fact]
    public void InvalidPeriodWithoutInvoiceDoesNotInventAMonth() =>
        Assert.Null(LicensingConsumptionPeriod.ConsumptionMonth("13/2026", null));

    [Fact]
    public void EveryHistoricalMonthRetainsItsConsumptionAndMatchesNextMonthBilling()
    {
        for (var month = new DateOnly(2025, 1, 1); month <= new DateOnly(2026, 9, 1); month = month.AddMonths(1))
        {
            var actual = LicensingConsumptionPeriod.ConsumptionMonth(month.ToString("MM/yyyy"), month.AddMonths(1).AddDays(4));
            Assert.Equal(month, actual);
            var filter = LicensingConsumptionPeriod.Filter(month, month.AddMonths(1));
            Assert.Contains($"contains(cr07a_mesconsumo,'{month.Year}')", filter);
            Assert.Contains($"cr07a_factura ge {month.AddMonths(1):yyyy-MM-dd}", filter);
            Assert.Contains($"cr07a_factura lt {month.AddMonths(2):yyyy-MM-dd}", filter);
        }
    }

    [Fact]
    public void QuarterQueryIncludesExplicitMovedConsumptionAndBothYearBoundaries()
    {
        var filter = LicensingConsumptionPeriod.Filter(new(2025, 10, 1), new(2026, 1, 1));
        Assert.Contains("contains(cr07a_mesconsumo,'2025')", filter);
        Assert.Contains("cr07a_factura lt 2026-02-01", filter);
        Assert.DoesNotContain("contains(cr07a_mesconsumo,'2026')", filter);
    }

    private static Type Nested(string name) => typeof(DataverseService).GetNestedType(name, BindingFlags.NonPublic)!;
    private static object Object(string type, params (string Name, object Value)[] values)
    {
        var instance = Activator.CreateInstance(Nested(type), true)!;
        foreach (var (name, value) in values) instance.GetType().GetProperty(name)!.SetValue(instance, value);
        return instance;
    }
    private static object? Mapping(object cost, params object[] maps)
    {
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Nested("LicenciamientoCruceAccountMapping")))!;
        foreach (var map in maps) list.Add(map);
        return typeof(DataverseService).GetMethod("FindLicenciamientoCruceMappingForCostRow", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new[] { cost, list });
    }

    [Fact]
    public void OneConsumptionExceptionWinsWithoutMovingOtherRecordsOfTheAccount()
    {
        var account = Guid.NewGuid().ToString(); var one = Guid.NewGuid().ToString(); var two = Guid.NewGuid().ToString();
        var permanent = Object("LicenciamientoCruceAccountMapping", ("SourceAccountId", account), ("TargetClientId", Guid.NewGuid().ToString()));
        var exception = Object("LicenciamientoCruceAccountMapping", ("SourceAccountName", DataverseService.LicensingRecordRelationKey(one)), ("TargetClientId", Guid.NewGuid().ToString()));
        Assert.Same(exception, Mapping(Object("LicenciamientoCruceCostRow", ("RecordId", one), ("OriginalAccountId", account)), permanent, exception));
        Assert.Same(permanent, Mapping(Object("LicenciamientoCruceCostRow", ("RecordId", two), ("OriginalAccountId", account)), exception, permanent));
    }

    [Fact]
    public void AccountRuleDoesNotLeakToAnotherAccountWithTheSameDisplayName()
    {
        var account = Guid.NewGuid().ToString();
        var mapping = Object("LicenciamientoCruceAccountMapping", ("SourceAccountId", account), ("SourceAccountName", "account:" + account));
        Assert.Null(Mapping(Object("LicenciamientoCruceCostRow", ("RecordId", Guid.NewGuid().ToString()), ("OriginalAccountId", Guid.NewGuid().ToString()), ("ClientName", "Same name")), mapping));
    }

    [Fact]
    public void DeterministicRuleIdentityMakesRetriedCreatesConflictInsteadOfDuplicating()
    {
        var account = Guid.NewGuid().ToString();
        Assert.Equal(DataverseService.LicensingRelationId(account), DataverseService.LicensingRelationId(account.ToUpperInvariant()));
        Assert.NotEqual(DataverseService.LicensingRelationId(account), DataverseService.LicensingRelationId(DataverseService.LicensingRecordRelationKey(account)));
    }

    [Fact]
    public void SimilarNamesCannotOverrideDifferentKnownClients()
    {
        var cost = Object("LicenciamientoCruceCostGroup", ("ClientId", Guid.NewGuid().ToString()), ("ClientName", "Cliente SAS"));
        var billing = Object("LicenciamientoCruceBillingGroup", ("ClientId", Guid.NewGuid().ToString()), ("ClientName", "Cliente SAS"));
        var result = typeof(DataverseService).GetMethod("CanUseLicenciamientoCruceNameFallback", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new[] { cost, billing });
        Assert.Equal(false, result);
    }
}
