using System.Globalization;
using CotizadorInterno.Web.Models.Conciliacion;

namespace CotizadorInterno.Web.Services;

public static class DirectCashFlowReportBuilder
{
    public static readonly IReadOnlyDictionary<int, int[]> Totals = new Dictionary<int,int[]>
    {
        [10]=[11,12,13,14,15], [16]=[17,18,19,20,21], [9]=[10,16],
        [24]=[25,26,27,28], [29]=[30,31,32,33], [34]=[35,36,37], [23]=[24,29,34],
        [39]=[40,41,42,43,44,45,46,47], [48]=[49,50,51,52], [53]=[54,55], [56]=[57], [58]=[59,60,61,62], [63]=[64,65,66,67],
        [38]=[39,48,53,56,58,63], [22]=[23,38], [70]=[71,72,73], [74]=[75,76,77,78,79]
    };
    private static readonly Dictionary<int,(string Code,string Label,string Kind)> Headings = new()
    {
        [7]=("","SALDO INICIAL BANCOS","balance"),[9]=("","INGRESOS","section"),[10]=("1","SERVICIOS","subtotal"),[16]=("2","OTROS","subtotal"),
        [22]=("","EGRESOS","section"),[23]=("","NÓMINA Y SOCIOS","group"),[24]=("5","PAGO DE SOCIOS","subtotal"),[29]=("6","NÓMINA Y SEGURIDAD SOCIAL","subtotal"),[34]=("7","OTROS GASTOS DE PERSONAL","subtotal"),
        [38]=("","PAGOS PROVEEDORES Y OTROS","group"),[39]=("8","PROVEEDORES","subtotal"),[48]=("9","GASTOS DE VIAJE Y ANTICIPO A TRABAJADORES","subtotal"),[53]=("12","GASTOS DE ARRENDAMIENTO","subtotal"),[56]=("13","SEGUROS","subtotal"),[58]=("14","SERVICIOS PÚBLICOS","subtotal"),[63]=("15","IMPUESTOS","subtotal"),
        [68]=("","FLUJO OPERACIONAL","section"),[70]=("20","TOTAL INGRESOS FINANCIACIÓN","subtotal"),[74]=("21","TOTAL EGRESOS FINANCIACIÓN","subtotal"),[81]=("","NETO DE FINANCIACIÓN","section"),[83]=("","TOTAL FLUJO CAJA","total"),
        [85]=("","SALDO CUENTA CLOUD","input"),[86]=("","SALDO CUENTA COPIERS","input"),[87]=("","SALDO BOLSILLOS","input"),[88]=("","SALDO FIDURENTA","input"),[90]=("","CHECK","check")
    };

    public static void ValidatePeriod(int year,int first,int last)
    {
        if(year is < 2020 or > 2100 || first is < 1 or > 12 || last < first || last>12)
            throw new InvalidOperationException("Selecciona un periodo válido dentro del mismo año.");
    }
    public static DirectCashFlowReport Build(int year,int first,int last,DirectCashFlowData data)
    {
        ValidatePeriod(year,first,last);
        var months=new List<DirectCashFlowMonth>();
        var cells=new List<Dictionary<int,decimal?>>();
        decimal? previousClosing=null;
        for(var month=first;month<=last;month++)
        {
            var prefix=$"{year:D4}-{month:D2}";
            var all=data.Movements.Where(x=>x.Date.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
            var rows=all.Where(x=>!x.IsInternalTransfer).ToArray();
            var balances=data.Balances.FirstOrDefault(x=>x.Year==year&&x.Month==month) ?? new DirectCashFlowBalances{Year=year,Month=month};
            var hasActivity=all.Length>0 || balances.Opening.HasValue;
            var opening=balances.Opening ?? previousClosing;
            var openingSource=balances.Opening.HasValue?"Ingresado":previousClosing.HasValue?"Cierre del mes anterior":"Pendiente";
            if(!opening.HasValue && data.BankOpenings.TryGetValue(month,out var bankOpening)) {opening=bankOpening;openingSource="Saldos de conciliación";}
            var values=new Dictionary<int,decimal?>();
            foreach(var c in DirectCashFlowCatalog.Concepts)
                values[c.Row]=hasActivity?rows.Where(x=>x.ConceptKey==c.Key).Sum(x=>x.Amount):null;
            foreach(var total in Totals) values[total.Key]=hasActivity?total.Value.Sum(r=>values.GetValueOrDefault(r)??0):null;
            values[7]=opening;
            values[68]=values[9]-values[22];
            values[81]=values[70]-values[74];
            values[83]=opening+values[68]+values[81];
            values[85]=balances.Cloud;values[86]=balances.Copiers;values[87]=balances.Pockets;values[88]=balances.Fiduciary;
            values[90]=values[83]-balances.Cloud-balances.Copiers-balances.Pockets-balances.Fiduciary;
            var unknown=rows.Where(x=>DirectCashFlowCatalog.Find(x.ConceptKey) is null).ToArray();
            var transferNet=all.Where(x=>x.IsInternalTransfer).Sum(x=>x.Entry-x.Exit);
            var complete=unknown.Length==0 && transferNet==0;
            previousClosing=complete?values[83]:null;
            months.Add(new DirectCashFlowMonth
            {
                Month=month,Label=new DateTime(year,month,1).ToString("MMMM",CultureInfo.GetCultureInfo("es-CO")),
                Entries=rows.Sum(x=>x.Entry),Exits=rows.Sum(x=>x.Exit),
                UnclassifiedCount=unknown.Length,UnclassifiedEntries=unknown.Sum(x=>x.Entry),UnclassifiedExits=unknown.Sum(x=>x.Exit),
                SuggestedCount=rows.Count(x=>x.Status=="Sugerido"),ConfirmedCount=rows.Count(x=>x.Status=="Confirmado"),ManualCount=rows.Count(x=>x.IsManual),
                InternalTransferCount=all.Length-rows.Length,InternalTransferNet=transferNet,
                OpeningBalance=opening,ClosingBalance=values[83],BankDifference=values[90],OpeningSource=openingSource,HasActivity=hasActivity,Balances=balances
            });
            cells.Add(values);
        }
        var reportRows=new List<DirectCashFlowReportRow>();
        for(var r=7;r<=90;r++)
        {
            var c=DirectCashFlowCatalog.Concepts.FirstOrDefault(x=>x.Row==r);
            if(c!=null) reportRows.Add(new(r,c.Code,c.Label,"detail",c.Key,cells.Select(v=>v.GetValueOrDefault(r)).ToArray()));
            else if(Headings.TryGetValue(r,out var h)) reportRows.Add(new(r,h.Code,h.Label,h.Kind,null,cells.Select(v=>v.GetValueOrDefault(r)).ToArray()));
        }
        return new(){Year=year,StartMonth=first,EndMonth=last,Months=months,Rows=reportRows,CapturedAt=data.CapturedAt};
    }
    public static void MarkInternalTransfers(IReadOnlyList<DirectCashFlowMovement> movements, IReadOnlySet<string> omittedIds)
    {
        foreach(var row in movements.Where(x=>omittedIds.Contains(x.RecordId)))
        {
            var counterparts=movements.Where(other=>!other.IsManual && other.Date[..7]==row.Date[..7] && other.RecordId!=row.RecordId && other.Bank!=row.Bank && other.Amount==row.Amount && other.Direction!=row.Direction
                && Math.Abs((DateOnly.Parse(other.Date).ToDateTime(TimeOnly.MinValue)-DateOnly.Parse(row.Date).ToDateTime(TimeOnly.MinValue)).TotalDays)<=4).ToArray();
            if(counterparts.Length==1)
            {
                row.IsInternalTransfer=true;counterparts[0].IsInternalTransfer=true;
            }
        }
        foreach(var row in movements.Where(x=>x.IsInternalTransfer))
        { row.ConceptKey=""; row.ConceptLabel="Traslado entre cuentas propias"; row.Status="Traslado"; row.Reason="Se excluye del consolidado. El saldo neto de traslados debe ser cero."; }
    }
}
