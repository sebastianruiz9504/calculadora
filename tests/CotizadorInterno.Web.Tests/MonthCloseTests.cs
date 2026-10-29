using System.Reflection;
using System.Text.Json;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.MonthClose;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class MonthCloseTests
{
    private static JsonElement J(string value)=>JsonDocument.Parse(value).RootElement.Clone();
    private static MonthCloseInput Input()=>new(){RetrievedAt=DateTimeOffset.Parse("2026-10-29T15:00:00Z"),Siigo=new(){
        ["invoices"]=[J("""{"id":"inv1","name":"FV-2-1","date":"2026-01-15","total":100,"balance":0,"customer":{"identification":"123"},"items":[],"payments":[{"due_date":"2026-01-31"}]}""")],
        ["journals"]=[],["credit-notes"]=[],["vouchers"]=[]}};
    private static JsonElement Payment(string date,decimal value,string name="FV-2",string customer="123")=>J($$"""{"name":"CC-17-1","date":"{{date}}","items":[{"account":{"code":"13050501","movement":"Credit"},"customer":{"identification":"{{customer}}"},"due":{"prefix":"{{name}}","consecutive":1,"quote":1},"value":{{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}]}""");
    private static MonthCloseReport Report(MonthCloseSnapshot s,string key="receivables")=>s.Reports.Single(r=>r.Key==key);

    [Fact] public void LaterPaymentDoesNotChangeEarlierMonth()
    {
        var input=Input();input.Siigo["journals"]=[Payment("2026-02-10",100)];
        Assert.Equal(100,Report(MonthCloseEngine.Build("2026-01",1,input)).Total);
        Assert.Empty(Report(MonthCloseEngine.Build("2026-02",1,input)).Rows);
    }
    [Fact] public void PartialPaymentsAreAppliedByTheirOwnEffectiveDates()
    {
        var input=Input();input.Siigo["journals"]=[Payment("2026-01-31",40),Payment("2026-02-01",60)];
        Assert.Equal(60,Report(MonthCloseEngine.Build("2026-01",1,input)).Total);
        Assert.Equal(0,Report(MonthCloseEngine.Build("2026-02",1,input)).Total);
    }
    [Fact] public void CreditNoteAffectsItsMonthNotInvoiceMonth()
    {
        var input=Input();input.Siigo["credit-notes"]=[J("""{"id":"nc1","name":"NC-2-1","date":"2026-02-01","invoice":{"id":"inv1"},"total":100,"items":[]}""")];
        var jan=MonthCloseEngine.Build("2026-01",1,input);var feb=MonthCloseEngine.Build("2026-02",1,input);
        Assert.Equal(100,Report(jan).Total);Assert.Equal(100,Report(jan,"billing").Total);
        Assert.Equal(0,Report(feb).Total);Assert.Equal(-100,Report(feb,"billing").Total);
    }
    [Fact] public void MissingApplicationsAreUnknownNotZeroOrInventedDebt()
    {
        var report=Report(MonthCloseEngine.Build("2026-01",1,Input()));
        Assert.Equal(1,report.Unresolved);Assert.Null(Assert.Single(report.Rows)["balance"]);Assert.Null(report.Total);
    }
    [Theory] [InlineData("FV-1","123")] [InlineData("FV-2","456")]
    public void PaymentRequiresBothInvoicePrefixAndCustomer(string prefix,string customer)
    {
        var input=Input();input.Siigo["journals"]=[Payment("2026-01-20",100,prefix,customer)];
        Assert.Equal(1,Report(MonthCloseEngine.Build("2026-01",1,input)).Unresolved);
    }
    [Fact] public void UnknownVerticalIsIncludedInBillingTotals()
    {
        var report=Report(MonthCloseEngine.Build("2026-01",1,Input()),"billing");
        Assert.Equal("Sin asignar",Assert.Single(report.Rows)["line"]);Assert.Equal(100,report.Total);
    }
    [Fact] public void GlobalAndItemRetentionsStaySeparateFromTaxes()
    {
        var doc=J("""{"total":112,"retentions":[{"type":"ReteICA","value":2}],"items":[{"taxes":[{"type":"IVA","value":19},{"type":"Retefuente","value":5}]}]}""");
        var values=MonthCloseEngine.Amounts(doc);Assert.Equal((100m,19m,7m),values);
    }
    [Theory] [InlineData("2026-10")] [InlineData("2026-11")] [InlineData("2026-13")] [InlineData("2024-01")] [InlineData("2026-1")]
    public void OpenFutureOrMalformedMonthsCannotBeClosed(string period)=>Assert.ThrowsAny<ArgumentException>(()=>MonthCloseEngine.Build(period,1,Input()));
    [Fact] public void HistoricOperationalValuesAreNeverInventedFromCurrentState()
    {
        var input=Input();input.Tables["inventory"]=[J("""{"cr07a_nombredelsuministro":"Tóner","cr07a_cantidad":50}""")];
        var report=Report(MonthCloseEngine.Build("2026-09",1,input),"inventory");
        Assert.Equal("unavailable",report.State);Assert.Null(report.Total);Assert.Empty(report.Rows);
    }
    [Fact] public void FirstDayObservationIsLabeledAndDoesNotCreatePastInventory()
    {
        var input=Input();input.RetrievedAt=DateTimeOffset.Parse("2026-10-01T05:10:00Z");
        input.Tables["inventory"]=[J("""{"cr07a_nombredelsuministro":"Tóner","cr07a_cantidad":50}""")];
        var current=MonthCloseEngine.Build("2026-09",1,input);Assert.Equal("captured",current.Summary.Mode);Assert.Equal(50,Report(current,"inventory").Total);
        Assert.Null(Report(MonthCloseEngine.Build("2026-08",1,input),"inventory").Total);
    }
    [Fact] public void BogotaBoundaryDoesNotUseServerCalendar()=>Assert.Equal(new DateOnly(2026,9,30),MonthCloseEngine.BogotaToday(DateTimeOffset.Parse("2026-10-01T04:59:59Z")));
    [Fact] public void CodecVerifiesIntegrityAndSurvivesRoundTrip()
    {
        var s=MonthCloseEngine.Build("2026-01",1,Input());var (body,hash)=MonthCloseCodec.Encode(s);var decoded=MonthCloseCodec.Decode(body,hash);
        Assert.Equal(7,decoded.Reports.Count);Assert.Equal(s.Summary.Period,decoded.Summary.Period);
        Assert.Equal(hash,decoded.Summary.Hash);Assert.Throws<InvalidDataException>(()=>MonthCloseCodec.Decode(body,new string('0',64)));
    }
    [Fact] public void VersionIdsAreIdempotentAndDifferentAcrossRevisions()
    {
        Assert.Equal(MonthCloseCodec.IdFor("2026-01",1),MonthCloseCodec.IdFor("2026-01",1));
        Assert.NotEqual(MonthCloseCodec.IdFor("2026-01",1),MonthCloseCodec.IdFor("2026-01",2));
    }
    [Fact] public void ColombianPaymentStringsAreParsedWithoutThousandFactorError()
    {
        Assert.Equal(174800,MonthCloseEngine.N(J("""{"v":"174.800,00"}"""),"v"));
        Assert.Equal(174800,MonthCloseEngine.N(J("""{"v":"174,800.00"}"""),"v"));
    }
    [Fact] public void SupplierPaymentUsesEffectiveDateAndMatchesSupplierInvoice()
    {
        var input=Input();
        input.Siigo["purchases"]=[J("""{"id":"p1","name":"FC-1-5","date":"2026-01-15","supplier":{"identification":"123"},"provider_invoice":{"prefix":"ABC","number":"00010"},"total":100,"balance":0}""")];
        input.Siigo["payment-receipts"]=[J("""{"name":"RP-1-1","date":"2026-02-02","supplier":{"identification":"123"},"items":[{"account":{"code":"22050501","movement":"Debit"},"due":{"prefix":"ABC","consecutive":10},"value":100}]}""")];
        Assert.Equal(100,Report(MonthCloseEngine.Build("2026-01",1,input),"payables").Total);
        Assert.Empty(Report(MonthCloseEngine.Build("2026-02",1,input),"payables").Rows);
    }
    [Fact] public void SupplierMissingApplicationsNeverInventOldUnpaidDebt()
    {
        var input=Input();input.Siigo["purchases"]=[J("""{"id":"p1","name":"FC-1-5","date":"2026-01-15","supplier":{"identification":"123"},"total":100,"balance":0}""")];
        var report=Report(MonthCloseEngine.Build("2026-01",1,input),"payables");
        Assert.Equal(1,report.Unresolved);Assert.Null(Assert.Single(report.Rows)["balance"]);
    }
    [Fact] public void LegacyPartiallyPaidInvoiceIsStillVisibleAsUnresolved()
    {
        var input=Input();input.Tables["billing"]=[J("""{"cr07a_name":"OLD-1","cr07a_nitempresa":"123","cr07a_fechadeemision":"2024-01-01","cr07a_fechadepago":"2025-01-01","cr07a_valorpago":40,"cr07a_totalfactura":100}""")];
        Assert.Contains(Report(MonthCloseEngine.Build("2026-01",1,input)).Rows,r=>Equals(r["document"],"OLD-1")&&r["balance"] is null);
    }
    [Theory] [InlineData("vacations")] [InlineData("banks")]
    public async Task SensitiveDetailsAndExportsDenyDashboardOnlyUserBeforeReadingStoredData(string key)
    {
        var repo=new FakeRepository();using var cache=new MemoryCache(new MemoryCacheOptions());var service=new MonthCloseService(repo,new FakeSiigo(),cache,NullLogger<MonthCloseService>.Instance);
        var dv=DispatchProxy.Create<IDataverseService,UserProxy>();((UserProxy)(object)dv).User=new CurrentUserInfo{ModuleOptionValues=[AppModuleCatalog.Dashboard.OptionValue]};
        var controller=new MonthCloseController(service,dv,NullLogger<MonthCloseController>.Instance);
        Assert.IsType<ForbidResult>(await controller.Report(Guid.NewGuid(),key));
        Assert.IsType<ForbidResult>(await controller.Export(Guid.NewGuid(),key));Assert.Equal(0,repo.ReadCount);
    }
    [Fact] public async Task ReadingPanelUsesStoredDataWithoutCallingOperationalSources()
    {
        var repo=new FakeRepository();var siigo=new FakeSiigo();using var cache=new MemoryCache(new MemoryCacheOptions());var service=new MonthCloseService(repo,siigo,cache,NullLogger<MonthCloseService>.Instance);
        await service.ListAsync(2026,default);await service.ListAsync(2026,default);Assert.Equal(1,repo.ListCount);Assert.Equal(0,siigo.ReadCount);
    }
    [Fact] public async Task BankSummaryRestrictionDoesNotMutateCachedFinancialValues()
    {
        var summary=new MonthCloseSummary{Period="2025-01",Reports=[new("banks","Bancos","verified","Comprobado","","COP",123456,5,0)]};
        var repo=new FakeRepository{Summaries=[summary]};using var cache=new MemoryCache(new MemoryCacheOptions());
        var service=new MonthCloseService(repo,new FakeSiigo(),cache,NullLogger<MonthCloseService>.Instance);
        var dv=DispatchProxy.Create<IDataverseService,UserProxy>();var proxy=(UserProxy)(object)dv;proxy.User=new CurrentUserInfo{ModuleOptionValues=[AppModuleCatalog.Dashboard.OptionValue]};
        var controller=new MonthCloseController(service,dv,NullLogger<MonthCloseController>.Instance);
        var denied=Assert.IsType<JsonResult>(await controller.Index(2025));
        var json=JsonSerializer.Serialize(denied.Value,MonthCloseCodec.Json);Assert.DoesNotContain("123456",json);Assert.Contains("restricted",json);Assert.Equal(123456,summary.Reports[0].Total);
        proxy.User=new CurrentUserInfo{ModuleOptionValues=[AppModuleCatalog.Dashboard.OptionValue,AppModuleCatalog.Conciliacion.OptionValue]};
        var allowed=Assert.IsType<JsonResult>(await controller.Index(2025));Assert.Contains("123456",JsonSerializer.Serialize(allowed.Value,MonthCloseCodec.Json));Assert.Equal(1,repo.ListCount);
    }
    public class UserProxy:DispatchProxy
    {
        public CurrentUserInfo User=new();
        protected override object? Invoke(MethodInfo? targetMethod,object?[]? args)=>targetMethod!.Name=="GetCurrentUserAsync"?Task.FromResult<CurrentUserInfo?>(User):throw new NotSupportedException();
    }
    private sealed class FakeRepository:IMonthCloseRepository
    {
        public int ReadCount,ListCount;
        public List<MonthCloseSummary> Summaries=[];
        public Task<IReadOnlyList<MonthCloseSummary>> ListAsync(int y,CancellationToken ct){ListCount++;return Task.FromResult<IReadOnlyList<MonthCloseSummary>>(Summaries);}
        public Task<MonthCloseSnapshot?> ReadAsync(Guid id,CancellationToken ct){ReadCount++;return Task.FromResult<MonthCloseSnapshot?>(null);}
        public Task<bool>CreateAsync(MonthCloseSnapshot s,CancellationToken ct)=>throw new NotSupportedException();
        public Task<Dictionary<string,List<JsonElement>>>ReadSourcesAsync(CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class FakeSiigo:IMonthCloseSiigoSource
    {
        public int ReadCount;
        public Task<Dictionary<string,List<JsonElement>>>ReadMonthCloseDocumentsAsync(CancellationToken ct){ReadCount++;throw new NotSupportedException();}
    }
}
