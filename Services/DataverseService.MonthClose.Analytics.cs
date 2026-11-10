using System.Globalization;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Licenciamiento;
using CotizadorInterno.Web.Services.MonthClose;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    Task IMonthCloseRepository.EnrichAsync(MonthCloseSnapshot snapshot,MonthCloseInput input,CancellationToken ct)
    {
        var period=MonthCloseEngine.ParsePeriod(snapshot.Summary.Period);
        var end=period.AddMonths(1);
        if(snapshot.Summary.Mode=="live")end=MonthCloseEngine.BogotaToday(input.RetrievedAt).AddDays(1);
        var revenue=new List<YtdContribution>();
        var invoices=input.Source("billing").GroupBy(x=>MonthCloseEngine.S(x,"cr07a_name")+"|"+MonthCloseEngine.S(x,"cr07a_nitempresa")).ToDictionary(g=>g.Key,g=>g.First());
        var clients=input.Source("clients").GroupBy(x=>MonthCloseEngine.S(x,"cr07a_nit")).ToDictionary(g=>g.Key,g=>g.ToList());
        var linkedInvoices=(snapshot.Reports.FirstOrDefault(r=>r.Key=="profit-detail")?.Rows??[])
            .Where(r=>MonthCloseEngine.Text(r,"kind")=="Facturación")
            .GroupBy(r=>MonthCloseEngine.Text(r,"source")).ToDictionary(g=>g.Key,g=>g.First());
        foreach(var row in snapshot.Reports.Single(r=>r.Key=="billing").Rows)
        {
            var amount=MonthCloseEngine.Value(row,"base");
            if(amount==0)continue;
            var label=MonthCloseEngine.Text(row,"party");var nit=MonthCloseEngine.Text(row,"taxId");
            var matched=clients.GetValueOrDefault(nit)??[];var client=matched.Count==1?"client:"+MonthCloseEngine.S(matched[0],"cr07a_clienteid"):"nit:"+nit;
            var clientLabel=label;
            if(linkedInvoices.TryGetValue(MonthCloseEngine.Text(row,"source"),out var linked))
            {
                client=MonthCloseEngine.Text(linked,"key");clientLabel=MonthCloseEngine.Text(linked,"party");
            }
            var line=MonthCloseEngine.Text(row,"line");var vertical=line is "Cloud" or "Copiers"?line.ToLowerInvariant():YtdUnassignedVerticalKey;
            var dv=invoices.GetValueOrDefault(MonthCloseEngine.Text(row,"document")+"|"+nit);
            var parsed=dv.ValueKind==JsonValueKind.Object?ParseBillingRecord(dv,_dashboardBillingIdField,_dashboardBillingPrimaryNameField):null;
            var contract=parsed is null?new YtdDimension("","Sin contrato"):ResolveYtdBillingContractType(parsed);
            var record=new YtdBreakdownRecordDto {SourceType="month-close",SourceLabel="Facturación del cierre",RecordId=MonthCloseEngine.Text(row,"source"),DocumentNumber=MonthCloseEngine.Text(row,"document"),DateDisplay=MonthCloseEngine.Text(row,"date"),Counterparty=label,ClientKey=client,ClientLabel=clientLabel,
                CategoryKey="billing",CategoryLabel="Facturación",VerticalKey=vertical,VerticalLabel=line,ContractTypeKey=contract.Key,ContractTypeLabel=contract.Label,Value=amount,TotalBeforeVatValue=amount,VatValue=MonthCloseEngine.Value(row,"tax"),TotalInvoice=MonthCloseEngine.Value(row,"total"),Description=MonthCloseEngine.Text(row,"quality")};
            revenue.Add(new(period.Month,amount,client,clientLabel,"billing","Facturación",vertical,line,contract.Key,contract.Label,1,[record]));
        }
        var rebates=input.Source("rebates").Select(x=>x.Deserialize<SharePointRebateRecord>(MonthCloseCodec.Json)!).Where(x=>x.Date>=period&&x.Date<end).ToList();
        revenue.AddRange(BuildYtdRevenueContributions([],rebates));
        var expenses=input.Source("expenses").Where(x=>MonthCloseEngine.D(x,"cr07a_fechaemision") is {} d&&d>=period&&d<end)
            .Select(x=>ParsePnlExpenseRow(x,"cr07a_fechaemision")).Where(x=>x is not null).Cast<PnlExpenseRow>().ToList();
        var licensing=(snapshot.Reports.FirstOrDefault(r=>r.Key=="profit-detail")?.Rows??[])
            .Where(r=>MonthCloseEngine.Text(r,"kind")=="Licenciamiento")
            .Select(r=>new LicenciamientoCruceRowDto {RowKey=MonthCloseEngine.Text(r,"source"),MatrixClientKey=MonthCloseEngine.Text(r,"key"),Cliente=MonthCloseEngine.Text(r,"party"),MesCosto=period.ToString("MM/yyyy"),
                CostoLicenciamiento=MonthCloseEngine.Value(r,"cost"),TipoContratoKey=MonthCloseEngine.Value(r,"contractType")==645250000?"monthly":"onetime",TipoContrato=MonthCloseEngine.Value(r,"contractType")==645250000?"Monthly":"Prepaid",
                Trace=new(){CostItems=[new(){RecordId=MonthCloseEngine.Text(r,"source")}]} }).ToList();
        var expenseBuild=BuildYtdExpenseContributions(expenses,licensing,period.Year,period.Month);
        var chart=BuildYtdChart(period.Year,period.Month,revenue,expenseBuild.Contributions);
        // Each immutable monthly payload owns its own month, avoiding repeated yearly detail.
        chart.Points=chart.Points.Where(p=>p.Month==period.Month).ToList();
        foreach(var record in chart.Points.SelectMany(p=>p.RevenueSegments.Concat(p.ExpenseSegments)).SelectMany(s=>s.Records))
            record.CanEditAllocation=record.CanEditCategory=record.CanEditContractType=record.CanEditVertical=false;
        snapshot.Ytd=new(){Year=period.Year,MonthCutoff=period.Month,PeriodLabel=$"YTD {period.Year}",FocusLabel="Ingresos, gastos y utilidad por mes",HasData=chart.HasData,Chart=chart,Charts=[chart],RevenueFilters=BuildYtdRevenueFilters(revenue),ExpenseFilters=BuildYtdExpenseFilters(revenue,expenseBuild.Contributions),
            LicensingReconciliation=expenseBuild.Reconciliation,SourceWarning=string.Join(" ",input.Source("rebates-warning").Select(x=>MonthCloseEngine.S(x,"message")))};
        snapshot.Summary.Reports=snapshot.Reports.Select(r=>new MonthCloseMetric(r.Key,r.Title,r.State,r.Message,r.Needed,r.Unit,r.Total,r.Rows.Count,r.Unresolved)).ToList();
        return Task.CompletedTask;
    }
}
