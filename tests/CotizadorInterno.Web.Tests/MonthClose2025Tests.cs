using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Services.MonthClose;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class MonthClose2025Tests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static MonthCloseInput Input() => new() { RetrievedAt = DateTimeOffset.Parse("2026-10-29T15:00:00Z") };
    private static MonthCloseBankEvidence Evidence() => new()
    {
        ArchiveSha256 = new string('a',64),
        Statements = [new("enero.pdf","8100","202501",100,10,0,110,1,new string('b',64))],
        Transactions = [new("b1",new DateOnly(2025,1,15),"8100","TRANSFERENCIA",10,110,"enero.pdf",1,1)]
    };
    private static MonthCloseReport Billing(MonthCloseInput input) => MonthCloseEngine.Build("2025-01",1,input).Reports.Single(r=>r.Key=="billing");

    [Fact] public void CanBuildAllTwelve2025MonthsWithoutInventingOperationalHistory()
    {
        foreach(var month in Enumerable.Range(1,12))
        {
            var snapshot=MonthCloseEngine.Build($"2025-{month:00}",1,Input());
            Assert.Equal("reconstructed",snapshot.Summary.Mode);
            Assert.All(snapshot.Reports.Where(r=>new[]{"forecast","inventory","assets","vacations"}.Contains(r.Key)),r=>{Assert.Equal("unavailable",r.State);Assert.Null(r.Total);});
        }
    }
    [Fact] public void LegacyBaseAndVatAreIncludedButImpossibleRetentionsRemainUnknown()
    {
        var input=Input();input.Tables["billing"]=[J("""{"cr07a_name":"DT100","cr07a_nitempresa":"123","cr07a_fechadeemision":"2025-01-05","cr07a_totalfactura":119,"cr07a_facturaantesdeiva":100,"cr07a_ivavalor":19,"cr07a_rteftevalor":0,"cr07a_rteivavalor":0,"cr07a_reteicavalor":400,"cr07a_vertical":645250001}""")];
        var report=Billing(input);var row=Assert.Single(report.Rows);
        Assert.Equal(100m,report.Total);Assert.Equal(19m,row["tax"]);Assert.Null(row["retentions"]);Assert.Null(row["total"]);Assert.Equal("partial",report.State);
    }
    [Fact] public void InvalidLegacyBaseIsNotInventedFromGrossOrPayment()
    {
        var input=Input();input.Tables["billing"]=[J("""{"cr07a_name":"DT100","cr07a_fechadeemision":"2025-01-05","cr07a_totalfactura":119,"cr07a_facturaantesdeiva":1000,"cr07a_ivavalor":19,"cr07a_valorpago":100}""")];
        var report=Billing(input);Assert.Null(Assert.Single(report.Rows)["base"]);Assert.Null(report.Total);
    }
    [Theory] [InlineData("123",100,1)] [InlineData("456",100,2)] [InlineData("123",150,2)]
    public void MigratedDocumentIsDeduplicatedOnlyWithMatchingCustomerAndAmount(string customer,int total,int expected)
    {
        var input=Input();input.Tables["billing"]=[J($$"""{"cr07a_name":"DT100","cr07a_nitempresa":"{{customer}}","cr07a_fechadeemision":"2025-01-06","cr07a_totalfactura":{{total}},"cr07a_facturaantesdeiva":{{total}},"cr07a_ivavalor":0,"cr07a_vertical":645250001}""")];
        input.Siigo["invoices"]=[J("""{"id":"i1","name":"FV-2-100","date":"2025-01-05","customer":{"identification":"123"},"total":100,"balance":100,"items":[]}""")];
        var snapshot=MonthCloseEngine.Build("2025-01",1,input);
        Assert.Equal(expected,snapshot.Reports.Single(r=>r.Key=="billing").Rows.Count);
        if(expected==1){Assert.Equal("Copiers",Assert.Single(snapshot.Reports.Single(r=>r.Key=="billing").Rows)["line"]);Assert.Single(snapshot.Reports.Single(r=>r.Key=="receivables").Rows);}
    }
    [Fact] public void DemoRowsAreExcludedWithAnExplicitWarning()
    {
        var input=Input();input.Tables["billing"]=[J("""{"cr07a_name":"FV-3-0000","cr07a_cliente":"DEMO","cr07a_fechadeemision":"2025-01-10","cr07a_totalfactura":100}""")];
        var snapshot=MonthCloseEngine.Build("2025-01",1,input);Assert.Empty(snapshot.Reports.Single(r=>r.Key=="billing").Rows);Assert.Contains(snapshot.Summary.Warnings,w=>w.Contains("DEMO"));
    }
    [Fact] public void VerifiedBankEvidenceIsSavedWithoutTreatingDepositAsInvoicePayment()
    {
        var input=Input();input.BankEvidence=Evidence();input.Siigo["invoices"]=[J("""{"id":"i1","name":"FV-2-100","date":"2025-01-05","customer":{"identification":"123"},"total":10,"balance":0,"items":[]}""")];
        var snapshot=MonthCloseEngine.Build("2025-01",1,input);var bank=snapshot.Reports.Single(r=>r.Key=="banks");
        Assert.Equal(110m,bank.Total);Assert.Equal("verified",bank.State);Assert.Single(bank.Rows);Assert.Equal(100m,Assert.Single(bank.BankAccounts).Opening);
        Assert.Null(snapshot.Reports.Single(r=>r.Key=="receivables").Total);Assert.Equal(2,snapshot.SourceHashes.Count);
        var (body,hash)=MonthCloseCodec.Encode(snapshot);var decoded=MonthCloseCodec.Decode(body,hash);
        Assert.Equal(snapshot.SourceHashes,decoded.SourceHashes);Assert.Equal(110m,decoded.Reports.Single(r=>r.Key=="banks").BankAccounts[0].Closing);
        Assert.DoesNotContain(MonthCloseEngine.Build("2025-02",1,input).Reports,r=>r.Key=="banks");
    }
    [Fact] public void BankWrongBalanceWrongMonthAndDuplicateRowsAreRejected()
    {
        var wrong=Evidence();wrong.Transactions[0]=wrong.Transactions[0] with{Balance=111};Assert.Throws<InvalidDataException>(()=>MonthCloseBankImporter.Validate(wrong));
        wrong=Evidence();wrong.Transactions[0]=wrong.Transactions[0] with{Date=new DateOnly(2025,2,15)};Assert.Throws<InvalidDataException>(()=>MonthCloseBankImporter.Validate(wrong));
        wrong=Evidence();wrong.Transactions.Add(wrong.Transactions[0]);Assert.Throws<InvalidDataException>(()=>MonthCloseBankImporter.Validate(wrong));
    }
    [Fact] public void BankTotalAndOpeningContinuityAreVerifiedIndependently()
    {
        var wrong=Evidence();wrong.Statements[0]=wrong.Statements[0] with{Credits=20};Assert.Throws<InvalidDataException>(()=>MonthCloseBankImporter.Validate(wrong));
        wrong=Evidence();wrong.Statements.Add(new("febrero.pdf","8100","202502",999,0,0,999,0,new string('c',64)));
        Assert.Throws<InvalidDataException>(()=>MonthCloseBankImporter.Validate(wrong));
    }
    [Fact] public void PriorMonthBankValueDateStaysInItsStatementMonth()
    {
        var input=Input();input.BankEvidence=Evidence();input.BankEvidence.Transactions[0]=input.BankEvidence.Transactions[0] with{Date=new DateOnly(2024,12,31)};
        var report=MonthCloseEngine.Build("2025-01",1,input).Reports.Single(r=>r.Key=="banks");
        Assert.Equal(110m,report.Total);Assert.Equal("2024-12-31",Assert.Single(report.Rows)["date"]);
    }
}
