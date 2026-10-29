using System.Globalization;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Services.MonthClose;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class MonthCloseReceivablePolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-29T22:00:00Z");
    private static readonly string Hash = new('a', 64);
    private static JsonElement J(string text) => JsonDocument.Parse(text).RootElement.Clone();
    private static MonthCloseInput Legacy(decimal paid = 960, string date = "2025-01-20", decimal retention = 40) => new()
    {
        RetrievedAt = Now, Tables = new() { ["billing"] = [J($$"""{"cr07a_facturacionid":"old1","cr07a_name":"DT1","cr07a_nitempresa":"123","cr07a_fechadeemision":"2025-01-15","cr07a_fechadepago":"{{date}}","cr07a_totalfactura":1000,"cr07a_valorpago":{{paid.ToString(CultureInfo.InvariantCulture)}},"cr07a_rteftevalor":{{retention.ToString(CultureInfo.InvariantCulture)}}}""")] }
    };
    private static MonthCloseInput Siigo(string paymentDate = "2025-02-10", decimal payment = 100, string nit = "123", string prefix = "FV-2", decimal currentBalance = 0) => new()
    {
        RetrievedAt = Now, Siigo = new()
        {
            ["invoices"] = [J($$"""{"id":"i1","name":"FV-2-1","date":"2025-01-15","total":100,"balance":{{currentBalance.ToString(CultureInfo.InvariantCulture)}},"customer":{"identification":"123"},"payments":[{"due_date":"2025-01-31"}]}""")],
            ["journals"] = [J($$"""{"name":"CC-1-1","date":"{{paymentDate}}","items":[{"account":{"code":"13050501","movement":"Credit"},"customer":{"identification":"{{nit}}"},"due":{"prefix":"{{prefix}}","consecutive":1},"value":{{payment.ToString(CultureInfo.InvariantCulture)}}}]}""")],
            ["credit-notes"] = [J("""{"id":"n1","name":"NC-1-1","date":"2025-01-20","total":3,"invoice":{"id":"i1"}}""")]
        }
    };
    private static MonthCloseSnapshot Original(MonthCloseInput input, string period = "2025-01")
    {
        var s = MonthCloseEngine.Build(period, 2, input);
        var (_, hash) = MonthCloseCodec.Encode(s); s.Summary.Hash = hash;
        return s;
    }
    private static MonthCloseSnapshot Revise(MonthCloseInput input, string period = "2025-01") =>
        MonthCloseEngine.ReviseReceivables(Original(input, period), input, Hash, Now);
    private static MonthCloseReport Ar(MonthCloseSnapshot s) => s.Reports.Single(r => r.Key == "receivables");

    [Fact] public void ApprovedPaymentHasAuditTrailWithoutEditingSourceOrOriginal()
    {
        var input = Legacy(); var original = Original(input);
        var prior = JsonSerializer.Serialize(original, MonthCloseCodec.Json);
        var sources = JsonSerializer.Serialize(input, MonthCloseCodec.Json);
        var revised = MonthCloseEngine.ReviseReceivables(original, input, Hash, Now);
        Assert.Equal(prior, JsonSerializer.Serialize(original, MonthCloseCodec.Json));
        Assert.Equal(sources, JsonSerializer.Serialize(input, MonthCloseCodec.Json));
        Assert.Empty(Ar(revised).Rows); Assert.Equal(0, Ar(revised).Unresolved); Assert.Equal(0m, Ar(revised).Total);
        var assumed = Assert.Single(Ar(revised).SettlementAssumptions);
        Assert.Equal("2025-01-20", assumed.PaymentDate); Assert.Equal(960m, assumed.PaymentAmount);
        Assert.Equal(3, revised.Summary.Revision); Assert.Equal(Hash, revised.SourceHashes[MonthCloseEngine.ReceivablePolicyKey]);
        var (body, hash) = MonthCloseCodec.Encode(revised);
        Assert.Equal(assumed, Assert.Single(Ar(MonthCloseCodec.Decode(body, hash)).SettlementAssumptions));
    }
    [Theory] [InlineData(890)] [InlineData(889.99)]
    public void ApproximatelyElevenPercentAcceptsCopRounding(decimal paid) => Assert.Single(Ar(Revise(Legacy(paid))).SettlementAssumptions);
    [Theory] [InlineData(720)] [InlineData(0)] [InlineData(-1)] [InlineData(1120)]
    public void ImplausibleOrZeroPaymentStaysUnresolved(decimal paid)
    {
        // Overpayments are present in some stored revisions even if the strict builder would omit them.
        var input = Legacy(paid); var original = Original(Legacy(960));
        var result = MonthCloseEngine.ReviseReceivables(original, input, Hash, Now);
        Assert.Equal(1, Ar(result).Unresolved); Assert.Empty(Ar(result).SettlementAssumptions);
    }
    [Fact] public void AcceptsPlausibleOverpaymentInStoredUnresolvedRow()
    {
        var input = Legacy(1050); var original = Original(Legacy(960));
        Assert.Single(Ar(MonthCloseEngine.ReviseReceivables(original, input, Hash, Now)).SettlementAssumptions);
    }
    [Theory] [InlineData("")] [InlineData("2025-02-01")] [InlineData("2027-01-01")]
    public void MissingOrLaterDateCannotEraseEarlierReceivable(string date)
    {
        var report = Ar(Revise(Legacy(date: date)));
        Assert.Equal(1, report.Unresolved); Assert.Empty(report.SettlementAssumptions);
    }
    [Fact] public void InvalidRetentionsAreWarnedNotUsedToInferPayment()
    {
        var result = Revise(Legacy(retention: 100000));
        Assert.Single(Ar(result).SettlementAssumptions);
        Assert.Contains(result.Summary.Warnings, w => w.Contains("retenciones negativas o superiores"));
        Assert.Equal(1, Ar(Revise(Legacy(720, retention: 100000))).Unresolved);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void PriorIssueDateIsPreservedAsAnExplicitAdvanceWarning(bool legacy)
    {
        var result = Revise(legacy ? Legacy(date: "2025-01-10") : Siigo(paymentDate: "2025-01-10"));
        Assert.Equal("2025-01-10", Assert.Single(Ar(result).SettlementAssumptions).PaymentDate);
        Assert.Contains(result.Summary.Warnings, w => w.StartsWith("Posibles anticipos"));
    }
    [Fact] public void ClosingEntryDoesNotSubtractCreditNoteTwiceOrAlterBilling()
    {
        var input = Siigo(paymentDate: "2025-01-25"); var original = Original(input);
        var expected = JsonSerializer.Serialize(original.Reports.Where(r => r.Key != "receivables"), MonthCloseCodec.Json);
        var revised = MonthCloseEngine.ReviseReceivables(original, input, Hash, Now);
        Assert.Empty(Ar(revised).Rows); Assert.Single(Ar(revised).SettlementAssumptions);
        Assert.Equal(expected, JsonSerializer.Serialize(revised.Reports.Where(r => r.Key != "receivables"), MonthCloseCodec.Json));
    }
    [Fact] public void LaterSiigoPaymentKeepsDebtNetOfDatedNotesAndUpdatesAging()
    {
        var result = Revise(Siigo());
        Assert.Equal(97m, Ar(result).Total); Assert.Equal(0, Ar(result).Unresolved);
        Assert.Equal(97m, Assert.Single(result.Summary.Aging, x => x.Value != 0).Value);
        Assert.Equal(97m, result.Summary.Reports.Single(r => r.Key == "receivables").Total);
        Assert.Empty(Ar(Revise(Siigo(), "2025-02")).Rows);
    }
    [Theory] [InlineData("456", "FV-2", 100, 0)] [InlineData("123", "FV-1", 100, 0)] [InlineData("123", "FV-2", 60, 0)] [InlineData("123", "FV-2", 100, 10)]
    public void AmbiguousOrIncompleteSiigoMatchIsNeverAssumed(string nit, string prefix, decimal payment, decimal balance)
    {
        var report = Ar(Revise(Siigo(nit: nit, prefix: prefix, payment: payment, currentBalance: balance)));
        Assert.Equal(1, report.Unresolved); Assert.Empty(report.SettlementAssumptions);
    }
    [Fact] public void MultipleSiigoEntriesRequireSeparateReconciliation()
    {
        var input = Siigo();input.Siigo["journals"].Add(input.Siigo["journals"][0]);
        Assert.Equal(1, Ar(Revise(input)).Unresolved);
    }
    [Fact] public void PolicyDoesNotApplyTo2026OrToAnAlreadyRevisedSnapshot()
    {
        var input = Legacy();
        Assert.Throws<ArgumentException>(() => MonthCloseEngine.ReviseReceivables(Original(input, "2026-01"), input, Hash, Now));
        Assert.Throws<InvalidOperationException>(() => MonthCloseEngine.ReviseReceivables(Revise(input), input, Hash, Now));
    }
    [Fact] public void SourceMismatchStopsTheRevision()
    {
        var original = Original(Legacy());Ar(original).Rows[0]["total"] = 99999m;
        Assert.Throws<InvalidDataException>(() => MonthCloseEngine.ReviseReceivables(original, Legacy(), Hash, Now));
    }
}
