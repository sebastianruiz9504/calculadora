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
        var checks=data.Statements.Where(s=>string.CompareOrdinal(s.Period,$"{year:D4}-{first:D2}")>=0 && string.CompareOrdinal(s.Period,$"{year:D4}-{last:D2}")<=0)
            .Select(s=>CheckStatement(s,data.Movements)).OrderBy(x=>x.Period).ThenBy(x=>x.Bank).ToArray();
        return new(){Year=year,StartMonth=first,EndMonth=last,Months=months,Rows=reportRows,CapturedAt=data.CapturedAt,BankChecks=checks};
    }
    public static DirectCashFlowBankCheck CheckStatement(DirectCashFlowStatement statement,IReadOnlyList<DirectCashFlowMovement> movements)
    {
        var current=movements.Where(x=>x.Bank==statement.Bank && x.Date.StartsWith(statement.Period+"-",StringComparison.Ordinal)).ToArray();
        var byId=current.ToDictionary(x=>(x.SourceKind,x.RecordId));
        var expected=statement.Rows.Select(x=>(x.SourceKind,x.RecordId)).ToHashSet();
        var missing=0;var changed=0;var prior=statement.Opening;
        var valid=statement.Rows.Count>0 && expected.Count==statement.Rows.Count;
        foreach(var line in statement.Rows)
        {
            valid &= line.Date.StartsWith(statement.Period+"-",StringComparison.Ordinal) && line.Amount!=0 && prior+line.Amount==line.Balance;
            prior=line.Balance;
            if(!byId.TryGetValue((line.SourceKind,line.RecordId),out var actual))missing++;
            else if(actual.Date!=line.Date || actual.Entry-actual.Exit!=line.Amount)changed++;
        }
        valid &= prior==statement.Closing && statement.Opening+statement.Credits-statement.Debits==statement.Closing
            && statement.Rows.Sum(x=>Math.Max(0,x.Amount))==statement.Credits && statement.Rows.Sum(x=>Math.Max(0,-x.Amount))==statement.Debits;
        return new()
        {
            Period=statement.Period,Bank=statement.Bank,Account=statement.Account,Opening=statement.Opening,
            StatementCredits=statement.Credits,StatementDebits=statement.Debits,StatementClosing=statement.Closing,
            RegisteredCredits=current.Sum(x=>x.Entry),RegisteredDebits=current.Sum(x=>x.Exit),StatementCount=statement.Rows.Count,RegisteredCount=current.Length,
            MissingCount=missing,ChangedCount=changed,UnexpectedCount=current.Count(x=>!expected.Contains((x.SourceKind,x.RecordId))),
            PendingCount=current.Count(x=>!x.IsInternalTransfer&&DirectCashFlowCatalog.Find(x.ConceptKey)is null),ImportedCount=current.Count(x=>x.IsStatement),StatementValid=valid
        };
    }
    public static void MarkInternalTransfers(IReadOnlyList<DirectCashFlowMovement> movements, IReadOnlySet<string> omittedIds)
    {
        // A pair present in both statements, on the same day, with the bank's
        // own-transfer description is independent of manual cash adjustments.
        var statementTransfers=movements.Where(x=>x.IsStatement && x.Description.Equals("TRANSFERENCIA CTA SUC VIRTUAL",StringComparison.OrdinalIgnoreCase))
            .GroupBy(x=>(x.Date,x.Amount));
        foreach(var group in statementTransfers)
        {
            var pair=group.ToArray();
            if(pair.Length==2 && pair[0].Bank!=pair[1].Bank && pair[0].Entry==pair[1].Exit && pair[0].Exit==pair[1].Entry)
                foreach(var row in pair)row.IsInternalTransfer=true;
        }
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
