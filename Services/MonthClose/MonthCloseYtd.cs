using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services.MonthClose;

public static class MonthCloseYtd
{
    public static YtdDashboardDto Compose(string period,IReadOnlyList<MonthCloseSnapshot> snapshots)
    {
        var start=MonthCloseEngine.ParsePeriod(period);
        var parts=snapshots.Where(s=>s.Summary.Period.StartsWith($"{start.Year}-",StringComparison.Ordinal)&&string.CompareOrdinal(s.Summary.Period,period)<=0&&s.Ytd is not null).OrderBy(s=>s.Summary.Period).ToList();
        var points=parts.SelectMany(s=>s.Ytd!.Chart.Points.Where(p=>p.Key==s.Summary.Period)).ToList();
        var chart=new YtdChartDto {Key="total",Title="Ingresos, gastos y utilidad",Subtitle="Cada mes conserva su captura; el mes abierto se actualiza automáticamente.",Points=points,HasData=points.Any(p=>p.RevenueSegments.Count+p.ExpenseSegments.Count>0),TotalSales=points.Sum(p=>p.Sales),TotalExpenses=points.Sum(p=>p.Expenses),TotalUtility=points.Sum(p=>p.Utility)};
        YtdFilterSetDto Filters(bool expenses)
        {
            var filters=parts.Select(s=>expenses?s.Ytd!.ExpenseFilters:s.Ytd!.RevenueFilters).ToList();
            IReadOnlyList<YtdFilterOptionDto> Options(Func<YtdFilterSetDto,IReadOnlyList<YtdFilterOptionDto>> select)=>filters.SelectMany(select).GroupBy(o=>o.Key).Select(g=>new YtdFilterOptionDto{Key=g.Key,Label=g.First().Label,Total=g.Sum(o=>o.Total),RecordsCount=g.Sum(o=>o.RecordsCount)}).OrderBy(x=>x.Label).ToList();
            return new(){Clients=Options(f=>f.Clients),Categories=Options(f=>f.Categories),Verticals=Options(f=>f.Verticals),ContractTypes=Options(f=>f.ContractTypes),BreakdownModes=filters.FirstOrDefault()?.BreakdownModes??[]};
        }
        var months=parts.SelectMany(s=>s.Ytd!.LicensingReconciliation.Months).ToList();
        var invoice=months.Sum(m=>m.InvoiceValue);var licensing=months.Sum(m=>m.LicensingValue);
        var missing=Enumerable.Range(1,start.Month).Where(m=>!points.Any(p=>p.Month==m)).ToArray();
        return new(){Year=start.Year,MonthCutoff=start.Month,PeriodLabel=$"YTD {start.Year}",DateRangeLabel=$"Enero – {start:MM/yyyy}",FocusLabel="Ingresos, gastos y utilidad por mes",HasData=chart.HasData,Chart=chart,Charts=[chart],RevenueFilters=Filters(false),ExpenseFilters=Filters(true),
            SourceWarning=string.Join(" ",parts.Select(p=>p.Ytd!.SourceWarning).Where(s=>s!="").Distinct())+(missing.Length>0?" Meses sin captura YTD: "+string.Join(", ",missing):""),
            LicensingReconciliation=new(){InvoiceTotal=invoice,LicensingTotal=licensing,Difference=invoice-licensing,DifferencePercent=invoice==0?0:(invoice-licensing)/invoice*100,Months=months,Disclaimer="El gráfico conserva la distribución del gasto XCB del YTD. La tabla por cliente usa el costo de licenciamiento cargado, sin redistribuir diferencias."}};
    }
}
