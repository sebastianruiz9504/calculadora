using System.Reflection;
using System.Text.Json;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.MonthClose;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class MonthCloseProfitabilityTests
{
    private static JsonElement J(string json)=>JsonDocument.Parse(json).RootElement.Clone();
    private static JsonElement Client(string id="c1",string nit="123",string city="Bogotá")=>JsonSerializer.SerializeToElement(new Dictionary<string,object?>{["cr07a_clienteid"]=id,["cr07a_nombre"]="Cliente "+id,["cr07a_nit"]=nit,["cr07a_ciudad"]=city});
    private static JsonElement Account(string id="a1",string client="c1",string group="",string label="622")=>JsonSerializer.SerializeToElement(new Dictionary<string,object?>{["cr07a_accountidicpid"]=id,["cr07a_name"]=label,["_cr07a_cliente_value"]=client,["cr07a_grupoempresarialid"]=group,["cr07a_grupoempresarialname"]=group==""?"":"Grupo empresarial"});
    private static JsonElement License(string id="l1",string account="a1",decimal cost=80,string invoice="2026-11-02")=>JsonSerializer.SerializeToElement(new Dictionary<string,object?>{["cr07a_consumointcomexid"]=id,["_cr07a_accountid_value"]=account,["cr07a_pesostotal"]=cost,["cr07a_factura"]=invoice,["cr07a_mesconsumo"]="10/2026",["cr07a_nombrecliente"]="Cliente proveedor"});
    private static MonthCloseInput Input()=>new(){RetrievedAt=DateTimeOffset.Parse("2026-11-10T16:00:00Z"),Tables=new(){["accounts"]=[Account()],["clients"]=[Client()]}};
    private static MonthCloseSnapshot Snapshot(params (string Nit,string Line,decimal Base)[] invoices)
    {
        var billing=new MonthCloseReport {Key="billing"};
        foreach(var (nit,line,amount) in invoices)billing.Rows.Add(new(){["document"]="FV"+nit,["party"]="Cliente "+nit,["taxId"]=nit,["line"]=line,["base"]=amount,["tax"]=amount*.19m,["date"]="2026-11-05",["source"]="inv"+nit});
        return new(){Summary=new(){Period="2026-11",Mode="live",CapturedAt=DateTimeOffset.Parse("2026-11-10T16:00:00Z")},Reports=[billing]};
    }
    private static MonthCloseSnapshot Calculate(MonthCloseInput input,params (string Nit,string Line,decimal Base)[] invoices)
    {
        var snapshot=Snapshot(invoices);MonthCloseEngine.AddProfitability(snapshot,input,new(2026,11,1),new(2026,11,11),true);return snapshot;
    }
    private static MonthCloseReport Report(MonthCloseSnapshot s,string key="profit-cloud")=>s.Reports.Single(r=>r.Key==key);
    private static JsonElement Visit(string id="m1",int state=827270002,int legacyStatus=0,string legacy="")=>JsonSerializer.SerializeToElement(new Dictionary<string,object?>{
        ["dtc_copiersmtov2id"]=id,["_dtc_client_value"]="c1",["dtc_servicedate"]="2026-11-05",["dtc_workflowstate"]=state,["dtc_businessstatus"]=legacyStatus,["dtc_formversion"]="copiers-legacy-v1",["dtc_legacysourcekey"]=legacy});

    [Fact] public void InvoiceMonthMatchesTheSameBillingMonthWithoutConsumptionOffset()
    {
        var input=Input();input.Tables["licensing"]=[License(),License("previous",cost:999,invoice:"2026-10-02"),License("future",cost:999,invoice:"2026-12-02")];
        var s=Calculate(input,("123","Cloud",100));var row=Assert.Single(Report(s).Rows);
        Assert.Equal(80m,row["licensing"]);Assert.Equal(20m,row["profit"]);Assert.Equal(20m,row["margin"]);
        Assert.Equal(19m,Report(s,"profit-detail").Rows.Single(r=>Equals(r["kind"],"Facturación"))["tax"]);
    }
    [Fact] public void CostOnlyClientAppearsWithNegativeProfitDuringTheOpenMonth()
    {
        var input=Input();input.Tables["licensing"]=[License()];var row=Assert.Single(Report(Calculate(input)).Rows);
        Assert.Equal(0m,row["billing"]);Assert.Equal(-80m,row["profit"]);Assert.Null(row["margin"]);
    }
    [Fact] public void MissingCostUploadDoesNotPretendToBeFullProfit()
    {
        var s=Calculate(Input(),("123","Cloud",100));Assert.Null(Assert.Single(Report(s).Rows)["profit"]);Assert.Equal(1,Report(s).Unresolved);
    }
    [Fact] public void ExplicitGroupConsolidatesBothCustomersOnce()
    {
        var input=Input();input.Tables["clients"]=[Client(),Client("c2","456")];input.Tables["accounts"]=[Account(group:"g"),Account("a2","c2","g","623")];input.Tables["licensing"]=[License(),License("l2","a2",30)];
        var row=Assert.Single(Report(Calculate(input,("123","Cloud",100),("456","Cloud",50))).Rows);
        Assert.Equal("group:g",row["key"]);Assert.Equal(150m,row["billing"]);Assert.Equal(110m,row["cost"]);Assert.Equal(40m,row["profit"]);
    }
    [Fact] public void DuplicateAccountNumberWithConflictingClientsIsVisibleButNotConfirmed()
    {
        var input=Input();input.Tables["clients"]=[Client(),Client("c2","456")];input.Tables["accounts"]=[Account(),Account("a2","c2")];input.Tables["licensing"]=[License()];
        var s=Calculate(input,("123","Cloud",100));Assert.Null(Assert.Single(Report(s).Rows)["profit"]);Assert.NotEmpty(Report(s,"account-audit").Rows);
    }
    [Fact] public void UnlinkedCostsRemainVisibleAndAreNeverMatchedByName()
    {
        var input=Input();input.Tables["licensing"]=[License(account:"")];var s=Calculate(input,("123","Cloud",100));
        Assert.Equal(2,Report(s).Rows.Count);Assert.All(Report(s).Rows,r=>Assert.Null(r["profit"]));Assert.Single(Report(s,"account-audit").Rows);
    }
    [Fact] public void RecordSpecificMappingOverridesAccountMapping()
    {
        var input=Input();input.Tables["licensing"]=[License(account:"")];input.Tables["account-maps"]=[J("""{"cr07a_active":true,"cr07a_sourceaccountname":"consumption:l1","cr07a_targetclientid":"c1"}""")];
        Assert.Equal(20m,Assert.Single(Report(Calculate(input,("123","Cloud",100))).Rows)["profit"]);
    }
    [Theory]
    [InlineData("Bogotá",40000)] [InlineData("Bogotá D.C.",40000)] [InlineData("Chía",100000)] [InlineData("Facatativá",100000)]
    [InlineData("CHIA CUNDINAMARCA",100000)] [InlineData("Bogotá D.C., Colombia",40000)] [InlineData("Chía, Cundinamarca, Colombia",100000)]
    [InlineData("Cartagena de Indias",100000)] [InlineData("Santa Marta",100000)] [InlineData("Barranquilla",100000)] [InlineData("Cali",500000)]
    public void FreightUsesApprovedMunicipalities(string city,decimal expected)=>Assert.Equal(expected,MonthCloseEngine.Freight(city,new()));
    [Fact] public void UnknownCityIsPendingInsteadOfAnInventedFreight()=>Assert.Null(MonthCloseEngine.Freight("",new()));
    [Fact] public void OnlyCompletedVisitsAreCostedOnce()
    {
        var input=Input();input.Tables["maintenance"]=[Visit(),Visit("draft",827270000),Visit("failed",827270003),Visit("historical1",827270004,645250000,"old1"),Visit("historical2",827270004,645250000,"old1"),Visit("pending",827270004,645250001,"old2")];
        var row=Assert.Single(Report(Calculate(input,("123","Copiers",500000)),"profit-copiers").Rows);
        Assert.Equal(2,row["maintenanceCount"]);Assert.Equal(200000m,row["maintenance"]);Assert.Equal(80000m,row["freight"]);Assert.Equal(220000m,row["profit"]);
    }
    [Fact] public void MissingCityMakesClientProfitPendingButPreservesKnownMaintenanceCost()
    {
        var input=Input();input.Tables["clients"]=[Client(city:"")];input.Tables["maintenance"]=[Visit()];var row=Assert.Single(Report(Calculate(input,("123","Copiers",500000)),"profit-copiers").Rows);
        Assert.Null(row["profit"]);Assert.Equal(100000m,row["knownCost"]);
    }
    [Fact] public void DeliveryUsesTheLastApprovedCostAvailableOnDeliveryDay()
    {
        var input=Input();input.Tables["deliveries"]=[J("""{"cr07a_entregaid":"d1","_cr07a_iddecliente_value":"c1","_cr07a_iddesuministro_value":"toner","cr07a_fechadeentrega":"2026-11-05","cr07a_cantidadentregada":3,"cr07a_estadodeentrega":645250000}""")];
        input.Tables["supply-purchases"]=[J("""{"_cr07a_suministro_value":"toner","cr07a_aprobadoeingresado":true,"cr07a_valorunitarioantesdeiva":70,"createdon":"2026-11-04"}"""),J("""{"_cr07a_suministro_value":"toner","cr07a_aprobadoeingresado":true,"cr07a_valorunitarioantesdeiva":100,"createdon":"2026-11-06"}"""),J("""{"_cr07a_suministro_value":"toner","cr07a_aprobadoeingresado":false,"cr07a_valorunitarioantesdeiva":999,"createdon":"2026-11-05"}""")];
        var row=Assert.Single(Report(Calculate(input,("123","Copiers",300)),"profit-copiers").Rows);Assert.Equal(210m,row["supplies"]);Assert.Equal(90m,row["profit"]);
    }
    [Fact] public void ActiveBusinessMetricUsesSameAnnualQuantityTimesUnitFormula()
    {
        var input=Input();input.Tables["cloud"]=[J("""{"cr07a_quantity":5,"cr07a_valorventaunidadusd":12.34,"cr07a_valortotalanual":9999,"statecode":0}"""),J("""{"cr07a_quantity":5,"cr07a_valorventaunidadusd":500,"statecode":1}""")];
        Assert.Equal(740.4m,Report(Calculate(input),"business").Total);
    }
    [Fact] public void LiveCalculationExcludesTomorrowAndFutureMonthDocuments()
    {
        var input=Input();input.Siigo["invoices"]=[J("""{"id":"1","name":"one","date":"2026-11-10","total":100,"balance":100,"items":[]}"""),J("""{"id":"2","name":"future","date":"2026-11-11","total":900,"balance":900,"items":[]}""")];
        var live=MonthCloseEngine.BuildLive(input);Assert.Equal("live",live.Summary.Mode);Assert.Equal("2026-11-10",live.Summary.Cutoff);Assert.Equal(100,Report(live,"billing").Total);
    }
    [Fact] public void HistoricalBusinessValueUsesOnlyTheFrozenMonthlyBase()
    {
        var input=Input();input.Tables["cloud"]=[J("""{"cr07a_quantity":500,"cr07a_valorventaunidadusd":999}""")];
        var snapshot=Snapshot();snapshot.Reports.Add(new(){Key="forecast",State="captured",Rows=[new(){["party"]="Cliente al cierre",["product"]="Licencia",["amount"]=25m}]});
        MonthCloseEngine.AddProfitability(snapshot,input,new(2026,10,1),new(2026,11,1),false);
        Assert.Equal(300m,Report(snapshot,"business").Total);Assert.Equal("Cliente al cierre",Assert.Single(Report(snapshot,"business").Rows)["party"]);
    }
    [Fact] public void YtdCompositionPreservesFilterUnionAndRestrictsTheSelectedYear()
    {
        MonthCloseSnapshot Month(string period,decimal amount)=>new(){Summary=new(){Period=period},Ytd=new(){Chart=new(){Points=[new(){Key=period,Month=int.Parse(period[5..]),Sales=amount}]},
            ExpenseFilters=new(){Clients=[new(){Key="group:g",Label="Grupo",Total=amount}],BreakdownModes=[new(){Key="client",Label="Clientes"}]}}};
        var ytd=MonthCloseYtd.Compose("2026-02",[Month("2025-12",999),Month("2026-01",100),Month("2026-02",200),Month("2026-03",999)]);
        Assert.Equal(2,ytd.Chart.Points.Count);Assert.Equal(300m,ytd.Chart.TotalSales);Assert.Equal(300m,Assert.Single(ytd.ExpenseFilters.Clients).Total);
        Assert.Equal("client",Assert.Single(ytd.ExpenseFilters.BreakdownModes).Key);
    }
    [Fact] public async Task YtdRevenueUsesTheSameGroupIdentityAsProfitabilityAndIsReadOnly()
    {
        var input=Input();input.Tables["accounts"]=[Account(group:"g")];input.Tables["licensing"]=[License()];
        var snapshot=Calculate(input,("123","Cloud",100));
        // This method is a pure projection; no initialized HTTP client or credentials are needed.
        var repository=(IMonthCloseRepository)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(DataverseService));
        await repository.EnrichAsync(snapshot,input,CancellationToken.None);
        Assert.Equal("group:g",Assert.Single(snapshot.Ytd!.RevenueFilters.Clients).Key);
        Assert.Equal("group:g",Assert.Single(snapshot.Ytd.ExpenseFilters.Clients).Key);
        var records=snapshot.Ytd.Chart.Points.SelectMany(p=>p.RevenueSegments).SelectMany(s=>s.Records).ToList();
        Assert.NotEmpty(records);Assert.All(records,r=>{Assert.Equal("group:g",r.ClientKey);Assert.False(r.CanEditVertical||r.CanEditContractType||r.CanEditAllocation||r.CanEditCategory);});
    }
    [Fact] public void FreezingKeepsTheLastObservationAndNeverMutatesTheLivePayload()
    {
        var input=Input();input.Tables["licensing"]=[License()];var live=MonthCloseEngine.BuildLive(input);
        var before=JsonSerializer.Serialize(live,MonthCloseCodec.Json);var frozen=MonthCloseService.Freeze(live,1,new(2026,12,1));
        Assert.Equal(before,JsonSerializer.Serialize(live,MonthCloseCodec.Json));Assert.Equal(live.Summary.CapturedAt,frozen.Summary.CapturedAt);
        Assert.Equal("captured",frozen.Summary.Mode);Assert.Equal("2026-11-30",frozen.Summary.Cutoff);
        input.Tables["licensing"]=[];Assert.Equal(-80m,Report(frozen).Total);
        var (body,hash)=MonthCloseCodec.Encode(frozen);Assert.Equal(-80m,Report(MonthCloseCodec.Decode(body,hash)).Total);
        Assert.Throws<InvalidOperationException>(()=>MonthCloseService.Freeze(live,1,new(2026,11,30)));
    }
    [Fact] public async Task AgingPopupIsFilteredAndItsTotalEqualsTheSelectedSlice()
    {
        var snapshot=new MonthCloseSnapshot{Summary=new(){Cutoff="2026-11-10"},Reports=[new(){Key="receivables",Columns=[new("balance","Saldo","money")],Rows=[
            new(){["due"]="2026-11-10",["balance"]=10},new(){["due"]="2026-11-09",["balance"]=20},new(){["due"]="2026-10-11",["balance"]=30},new(){["due"]="2026-10-10",["balance"]=40},new(){["due"]="2026-11-01",["balance"]=null}]}]};
        using var cache=new MemoryCache(new MemoryCacheOptions());var repo=new Repository(snapshot);var service=new MonthCloseService(repo,new NoSiigo(),cache,NullLogger<MonthCloseService>.Instance);
        var dv=DispatchProxy.Create<IDataverseService,NoCalls>();var controller=new MonthCloseController(service,dv,NullLogger<MonthCloseController>.Instance);
        var result=Assert.IsType<JsonResult>(await controller.Report(Guid.NewGuid(),"receivables",aging:1));var json=JsonSerializer.SerializeToElement(result.Value,MonthCloseCodec.Json);
        Assert.Equal(2,json.GetProperty("count").GetInt32());Assert.Equal(50,json.GetProperty("totals").GetProperty("balance").GetDecimal());
        Assert.Equal(5,snapshot.Reports[0].Rows.Count);
    }
    private sealed class Repository(MonthCloseSnapshot snapshot):IMonthCloseRepository
    {
        public Task<IReadOnlyList<MonthCloseSummary>> ListAsync(int y,CancellationToken ct)=>Task.FromResult<IReadOnlyList<MonthCloseSummary>>([]);
        public Task<MonthCloseSnapshot?> ReadAsync(Guid id,CancellationToken ct)=>Task.FromResult<MonthCloseSnapshot?>(snapshot);
        public Task<bool> CreateAsync(MonthCloseSnapshot s,CancellationToken ct)=>throw new NotSupportedException();
        public Task<Dictionary<string,List<JsonElement>>> ReadSourcesAsync(CancellationToken ct)=>throw new NotSupportedException();
    }
    public class NoCalls:DispatchProxy { protected override object? Invoke(MethodInfo? target,object?[]? args)=>throw new NotSupportedException(); }
    private sealed class NoSiigo:IMonthCloseSiigoSource {public Task<Dictionary<string,List<JsonElement>>>ReadMonthCloseDocumentsAsync(CancellationToken ct)=>throw new NotSupportedException();}
}
