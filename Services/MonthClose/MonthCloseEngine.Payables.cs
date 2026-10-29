using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services.MonthClose;

public static partial class MonthCloseEngine
{
    private static string PurchaseKey(string nit,string prefix,string number)=>nit+"|"+prefix.Trim().ToUpperInvariant()+"-"+number.Trim().TrimStart('0');
    private static string[] PurchaseAliases(JsonElement purchase)
    {
        var nit=S(P(purchase,"supplier"),"identification");
        var supplierInvoice=P(purchase,"provider_invoice");
        var name=S(purchase,"name");var separator=name.LastIndexOf('-');
        var keys=new List<string>();
        if(separator>0)keys.Add(PurchaseKey(nit,name[..separator],name[(separator+1)..]));
        if(S(supplierInvoice,"number")!="")keys.Add(PurchaseKey(nit,S(supplierInvoice,"prefix"),S(supplierInvoice,"number")));
        return keys.Distinct().ToArray();
    }

    private static MonthCloseReport BuildPayables(MonthCloseInput input,DateOnly end,DateOnly today)
    {
        var report=FinancialReport("payables","Cuentas por pagar");
        report.Message="Saldo parcial de compras Siigo reconstruido por fecha de aplicación. Se valida contra el saldo actual de cada compra. Los documentos que no concilian quedan sin saldo y fuera del total. No incluye obligaciones de documentos soporte ni otros pasivos sin auxiliar.";
        report.Needed="Auxiliar de proveedores y documentos soporte al corte, saldo inicial, abonos y notas con NIT, factura, fecha efectiva y valor aplicado. Los gastos de Dataverse por sí solos no demuestran una deuda pendiente.";
        var applications=new Dictionary<string,List<Application>>();
        foreach(var doc in input.Source("journals").Concat(input.Source("payment-receipts")))
        {
            if(D(doc,"date") is not {} date)continue;
            foreach(var item in A(doc,"items"))
            {
                var account=P(item,"account");var due=P(item,"due");
                if(!S(account,"code").StartsWith("2205",StringComparison.Ordinal)||!Has(due,"consecutive"))continue;
                var nit=S(P(item,"customer"),"identification");
                if(nit=="")nit=S(P(doc,"supplier"),"identification");
                if(nit=="")continue;
                var key=PurchaseKey(nit,S(due,"prefix"),S(due,"consecutive"));
                if(!applications.TryGetValue(key,out var list))applications[key]=list=[];
                list.Add(new(date,(S(account,"movement")=="Debit"?1:-1)*N(item,"value"),S(doc,"name")));
            }
        }
        var purchases=input.Source("purchases");
        var aliasCount=purchases.SelectMany(PurchaseAliases).GroupBy(x=>x).ToDictionary(g=>g.Key,g=>g.Count());
        var suppliers=input.Source("expenses").Where(x=>S(x,"cr07a_nombreemisor")!="").GroupBy(x=>S(x,"cr07a_nitemisor"))
            .ToDictionary(g=>g.Key,g=>S(g.First(),"cr07a_nombreemisor"));
        foreach(var purchase in purchases.Where(x=>D(x,"date") is {} d&&d<end))
        {
            var issued=D(purchase,"date")!.Value;
            var nit=S(P(purchase,"supplier"),"identification");
            var keys=PurchaseAliases(purchase);
            var apps=keys.SelectMany(k=>applications.GetValueOrDefault(k)??[]).Where(x=>x.Date>=issued&&x.Date<=today).ToList();
            var before=apps.Where(x=>x.Date<end).Sum(x=>x.Value);
            var gap=N(purchase,"total")-apps.Sum(x=>x.Value)-N(purchase,"balance");
            var unknown=!Has(purchase,"balance")||keys.Any(k=>aliasCount[k]>1)||Math.Abs(gap)>1m||N(purchase,"total")<0;
            decimal? balance=unknown?null:Math.Max(0,N(purchase,"total")-before);
            if(balance<=0.01m)continue;
            if(unknown)report.Unresolved++;
            var amounts=Amounts(purchase);var supplierInvoice=P(purchase,"provider_invoice");
            report.Rows.Add(Row(("document",S(purchase,"name")+" · "+S(supplierInvoice,"prefix")+S(supplierInvoice,"number")),
                ("party",suppliers.GetValueOrDefault(nit,nit)),("taxId",nit),("date",issued.ToString("yyyy-MM-dd")),
                ("base",amounts.Base),("tax",amounts.Tax),("retentions",amounts.Retentions),("total",N(purchase,"total")),
                ("applications",before),("balance",balance),("quality",unknown?"Auxiliar incompleto o ambiguo; saldo no determinado":"Reconstruido; aplicaciones concilian con el saldo actual"),
                ("support",string.Join("; ",apps.Where(x=>x.Date<end).Select(x=>x.Reference).Distinct())),("source",S(purchase,"id"))));
        }
        report.Total=report.Unresolved>0&&report.Rows.All(r=>r["balance"] is null)?null:Sum(report,"balance");
        return report;
    }
}
