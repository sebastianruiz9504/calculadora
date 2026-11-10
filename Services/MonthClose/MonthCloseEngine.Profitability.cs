using System.Globalization;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services.MonthClose;

public static partial class MonthCloseEngine
{
    public static string Text(Dictionary<string, object?> row, string key) => Convert.ToString(row.GetValueOrDefault(key), CultureInfo.InvariantCulture) ?? "";
    public static decimal Value(Dictionary<string, object?> row, string key) => decimal.TryParse(Text(row,key), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : 0;
    private static string CityKey(string value) => string.Concat(value.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))).ToLowerInvariant();
    public static decimal? Freight(string city, MonthCloseCostPolicy policy)
    {
        var key = CityKey(city);
        foreach (var suffix in new[] { "colombia", "cundinamarca" })
            if (key.EndsWith(suffix, StringComparison.Ordinal) && key.Length > suffix.Length) key = key[..^suffix.Length];
        if (key.Length == 0) return null;
        if (key is "bogota" or "bogotadc" or "santafedebogota" or "bogotadistritocapital") return policy.BogotaFreight;
        if (policy.RegionalCities.Any(c => CityKey(c) == key) || key == "cartagenadeindias") return policy.RegionalFreight;
        return policy.OtherFreight;
    }

    public static void AddProfitability(MonthCloseSnapshot snapshot, MonthCloseInput input, DateOnly start, DateOnly end, bool observed)
    {
        snapshot.CostPolicy = input.CostPolicy;
        var clients = input.Source("clients").Where(x=>S(x,"cr07a_clienteid")!="").GroupBy(x=>S(x,"cr07a_clienteid")).ToDictionary(g=>g.Key,g=>g.First());
        var byNit = clients.Values.Where(x=>S(x,"cr07a_nit")!="").GroupBy(x=>S(x,"cr07a_nit")).ToDictionary(g=>g.Key,g=>g.ToList());
        var accounts = input.Source("accounts").Where(Active).GroupBy(x=>S(x,"cr07a_accountidicpid")).ToDictionary(g=>g.Key,g=>g.First());
        var aliases = accounts.Values.GroupBy(x=>S(x,"cr07a_name").Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.ToList(),StringComparer.OrdinalIgnoreCase);
        var maps = input.Source("account-maps").Where(x=>S(x,"cr07a_active").Equals("True",StringComparison.OrdinalIgnoreCase)).ToList();
        string Group(JsonElement a) => S(a,"cr07a_grupoempresarialid") is {Length:>0} g ? "group:"+g.Trim().ToLowerInvariant() : "client:"+S(a,"_cr07a_cliente_value");
        var clientGroups = accounts.Values.Where(x=>S(x,"_cr07a_cliente_value")!="").GroupBy(x=>S(x,"_cr07a_cliente_value")).ToDictionary(g=>g.Key,g=>g.Select(Group).Distinct().ToArray());
        var details = new MonthCloseReport {Key="profit-detail", Title="Movimientos de utilidad por cliente", State="captured", Columns=[
            new("line","Vertical"),new("party","Cliente / grupo"),new("client","Cliente facturado / atendido"),new("taxId","NIT"),new("account","Account ID"),new("kind","Concepto"),new("document","Documento"),new("date","Fecha","date"),new("product","Producto / servicio"),
            new("quantity","Cantidad","number"),new("base","Facturación antes de IVA","money"),new("tax","IVA","money"),new("cost","Costo antes de IVA","money"),new("unitCost","Costo unitario","money"),new("city","Ciudad"),new("quality","Criterio / pendientes"),new("source","ID fuente")],
            Message="Ingresos y costos del mismo mes. Los costos estándar de mantenimiento y flete son estimaciones de gestión; la utilidad no incluye gastos generales.", Needed="Resolver relaciones de Account ID, ciudades y suministros sin costo documentado."};
        var audit = new MonthCloseReport {Key="account-audit", Title="Revisión de Account ID", State="partial", Columns=[new("account","Account ID"),new("party","Cliente registrado"),new("quality","Hallazgo"),new("source","ID registro")], Message="Relaciones exactas. No se cruzan clientes por parecido de nombre."};
        foreach(var a in accounts.Values)
        {
            var id=S(a,"cr07a_accountidicpid");var clientId=S(a,"_cr07a_cliente_value");
            var issue=clientId==""||!clients.ContainsKey(clientId)?"Account ID sin cliente válido":aliases[S(a,"cr07a_name").Trim()].Count>1?"Account ID duplicado; revisar las relaciones de cliente y grupo":clientGroups.GetValueOrDefault(clientId)?.Length>1?"Cliente relacionado con varios grupos empresariales":"";
            if(issue!="") audit.Rows.Add(Row(("account",S(a,"cr07a_name")),("party",S(clients.GetValueOrDefault(clientId),"cr07a_nombre")),("quality",issue),("source",id)));
        }
        (string Key,string Label,bool Resolved) ClientIdentity(string id, bool cloud)
        {
            if(!clients.TryGetValue(id,out var c)) return ("unlinked:"+id,"Cliente sin relación",false);
            if(!cloud) return ("client:"+id,S(c,"cr07a_nombre"),true);
            var groups=clientGroups.GetValueOrDefault(id)??[];
            if(groups.Length>1) return ("ambiguous:"+id,S(c,"cr07a_nombre"),false);
            var group=groups.FirstOrDefault()??"client:"+id;
            var groupName=accounts.Values.Where(a=>Group(a)==group).Select(a=>S(a,"cr07a_grupoempresarialname")).FirstOrDefault(n=>n!="");
            return(group,groupName??S(c,"cr07a_nombre"),true);
        }
        void Add(string line,string id,string fallback,string kind,string source,string document,DateOnly? date,decimal? cost,decimal revenue=0,decimal tax=0,string account="",string quality="",decimal quantity=1,decimal? unitCost=null,string product="",string city="",bool invalid=false,string? forcedKey=null)
        {
            var identity=ClientIdentity(id,line=="Cloud"); var key=forcedKey??identity.Key;
            if(id=="")key=forcedKey??"unlinked:"+source;
            var resolved=identity.Resolved&&!invalid;
            details.Rows.Add(Row(("key",key),("line",line),("party",identity.Resolved?identity.Label:fallback),("client",S(clients.GetValueOrDefault(id),"cr07a_nombre") is {Length:>0} n?n:fallback),("clientId",id),("taxId",S(clients.GetValueOrDefault(id),"cr07a_nit")),
                ("kind",kind),("source",source),("document",document),("date",date?.ToString("yyyy-MM-dd")),("base",revenue),("tax",tax),("cost",cost),("quantity",quantity),("unitCost",unitCost),("account",account),("product",product),("city",city),
                ("quality",quality+(resolved?"":" · Relación de cliente / grupo pendiente")),("resolved",resolved&&cost.HasValue)));
        }
        foreach(var row in snapshot.Reports.Single(r=>r.Key=="billing").Rows.Where(r=>Text(r,"line") is "Cloud" or "Copiers"))
        {
            var matches=byNit.GetValueOrDefault(Text(row,"taxId"))??[];
            var id=matches.Count==1?S(matches[0],"cr07a_clienteid"):"";
            var valid=Text(row,"base")!="";
            Add(Text(row,"line"),id,Text(row,"party"),"Facturación",Text(row,"source"),Text(row,"document"),DateOnly.TryParse(Text(row,"date"),out var date)?date:null,0,Value(row,"base"),Value(row,"tax"),quality:Text(row,"quality"),invalid:!valid);
        }
        foreach(var cost in input.Source("licensing").Where(x=>D(x,"cr07a_factura") is {} d&&d>=start&&d<end))
        {
            var source=S(cost,"cr07a_consumointcomexid");var accountId=S(cost,"_cr07a_accountid_value");var id="";var invalid=false;
            var specific=maps.Where(m=>S(m,"cr07a_sourceaccountname")=="consumption:"+source).ToList();
            var mapping=specific.Count>0?specific:maps.Where(m=>accountId!=""&&S(m,"cr07a_sourceaccountid")==accountId).ToList();
            if(mapping.Count>1)invalid=true;
            if(mapping.Count==1){accountId=S(mapping[0],"cr07a_targetaccountid");id=S(mapping[0],"cr07a_targetclientid");}
            var a=accounts.GetValueOrDefault(accountId);
            if(id=="")id=S(a,"_cr07a_cliente_value");
            var name=S(a,"cr07a_name");
            if(name!=""&&aliases[name.Trim()].Select(Group).Distinct().Count()>1)invalid=true;
            decimal? value=Has(cost,"cr07a_pesostotal")?N(cost,"cr07a_pesostotal"):Has(cost,"cr07a_valortotalusd")&&N(cost,"cr07a_trm")>0?Round(N(cost,"cr07a_valortotalusd")*N(cost,"cr07a_trm")):null;
            Add("Cloud",id,S(cost,"cr07a_nombrecliente"),"Licenciamiento",source,S(cost,"cr07a_name"),D(cost,"cr07a_factura"),value,account:name,quality:invalid?"Account ID ambiguo; revisar":"Fecha de factura de licenciamiento; cruce exacto por Account ID",product:S(cost,"cr07a_productocorto"),invalid:invalid,forcedKey:id==""?"account:"+(accountId==""?source:accountId):null);
            var last=details.Rows[^1]; last["contractType"]=N(cost,"cr07a_tipocontrato");
            if(id==""||invalid) audit.Rows.Add(Row(("account",name),("party",S(cost,"cr07a_nombrecliente")),("quality",invalid?"Relación ambigua":"Costo sin cliente por Account ID"),("source",source)));
        }
        foreach(var visit in input.Source("maintenance").Where(x=>D(x,"dtc_servicedate") is {} d&&d>=start&&d<end)
            .Where(x=>N(x,"dtc_workflowstate")==827270002||N(x,"dtc_workflowstate")==827270004&&S(x,"dtc_formversion")=="copiers-legacy-v1"&&N(x,"dtc_businessstatus")==645250000)
            .GroupBy(x=>S(x,"dtc_legacysourcekey") is {Length:>0} k?k:S(x,"dtc_copiersmtov2id")).Select(g=>g.First()))
        {
            var id=S(visit,"_dtc_client_value");var city=S(clients.GetValueOrDefault(id),"cr07a_ciudad");var source=S(visit,"dtc_copiersmtov2id");var date=D(visit,"dtc_servicedate");
            Add("Copiers",id,S(visit,"dtc_clientnamesnapshot"),"Mantenimiento",source,S(visit,"dtc_reference"),date,input.CostPolicy.Maintenance,quality:"Costo estándar por visita completada (no por equipo)",unitCost:input.CostPolicy.Maintenance,city:city);
            Add("Copiers",id,S(visit,"dtc_clientnamesnapshot"),"Flete",source,S(visit,"dtc_reference"),date,Freight(city,input.CostPolicy),quality:city==""?"Falta ciudad para calcular flete":"Tarifa estándar; ciudad registrada del cliente",unitCost:Freight(city,input.CostPolicy),city:city);
        }
        var purchases=input.Source("supply-purchases").Where(x=>S(x,"cr07a_aprobadoeingresado").Equals("True",StringComparison.OrdinalIgnoreCase)&&Has(x,"cr07a_valorunitarioantesdeiva")).ToList();
        foreach(var delivery in input.Source("deliveries").Where(x=>N(x,"cr07a_estadodeentrega")==645250000&&D(x,"cr07a_fechadeentrega") is {} d&&d>=start&&d<end))
        {
            var id=S(delivery,"_cr07a_iddecliente_value");var date=D(delivery,"cr07a_fechadeentrega");var supply=S(delivery,"_cr07a_iddesuministro_value");
            var purchase=purchases.Where(x=>S(x,"_cr07a_suministro_value")==supply&&D(x,"createdon")<=date).OrderByDescending(x=>S(x,"createdon")).FirstOrDefault();
            decimal? unit=Has(purchase,"cr07a_valorunitarioantesdeiva")?N(purchase,"cr07a_valorunitarioantesdeiva"):null;
            var quantity=N(delivery,"cr07a_cantidadentregada");
            Add("Copiers",id,"Cliente sin relación","Suministro",S(delivery,"cr07a_entregaid"),S(delivery,"cr07a_entrega1"),date,unit*quantity,
                quality:unit.HasValue?"Último costo aprobado registrado antes de la entrega · "+S(purchase,"cr07a_name"):"Sin compra aprobada anterior a la entrega; costo pendiente",quantity:quantity,unitCost:unit,product:S(delivery,"_cr07a_iddesuministro_value@OData.Community.Display.V1.FormattedValue"));
        }
        snapshot.Reports.RemoveAll(r=>r.Key is "profit-cloud" or "profit-copiers" or "profit-detail" or "account-audit" or "business");
        foreach(var vertical in new[]{"Cloud","Copiers"})
        {
            var report=new MonthCloseReport{Key="profit-"+vertical.ToLowerInvariant(),Title="Utilidad por cliente · "+vertical,State="captured",Message=details.Message,Needed=details.Needed,
                Columns=[new("party","Cliente / grupo"),new("accounts","Account ID"),new("billing","Facturación sin IVA","money"),new("licensing","Licenciamiento","money"),new("supplyQuantity","Suministros","number"),new("supplies","Costo suministros","money"),new("maintenanceCount","Visitas","number"),new("maintenance","Mantenimientos","money"),new("freight","Fletes","money"),new("cost","Costo total","money"),new("profit","Utilidad","money"),new("margin","Margen %","number"),new("quality","Estado del cruce")]};
            foreach(var g in details.Rows.Where(r=>Text(r,"line")==vertical).GroupBy(r=>Text(r,"key")))
            {
                decimal Costs(string kind)=>Round(g.Where(r=>Text(r,"kind")==kind).Sum(r=>Value(r,"cost")));
                var billing=Round(g.Sum(r=>Value(r,"base")));var cost=Round(g.Sum(r=>Value(r,"cost")));var unresolved=g.Count(r=>!Equals(r["resolved"],true));
                var hasCost=g.Any(r=>Text(r,"kind")!="Facturación");var hasBilling=g.Any(r=>Text(r,"kind")=="Facturación");
                // A missing source upload cannot be represented as a verified zero cost.
                var incomplete=unresolved>0||vertical=="Cloud"&&!hasCost;
                decimal? profit=incomplete?null:billing-cost;
                report.Rows.Add(Row(("key",g.Key),("party",Text(g.First(),"party")),("accounts",string.Join(", ",g.Select(r=>Text(r,"account")).Where(s=>s!="").Distinct())),("billing",billing),("licensing",Costs("Licenciamiento")),("supplies",Costs("Suministro")),("supplyQuantity",g.Where(r=>Text(r,"kind")=="Suministro").Sum(r=>Value(r,"quantity"))),
                    ("maintenanceCount",g.Count(r=>Text(r,"kind")=="Mantenimiento")),("maintenance",Costs("Mantenimiento")),("freight",Costs("Flete")),("cost",incomplete?null:cost),("knownCost",cost),("profit",profit),("margin",profit.HasValue&&billing!=0?Round(profit.Value/billing*100):null),
                    ("quality",unresolved>0?$"Revisar {unresolved} movimientos":!hasCost?"Facturación sin costo registrado":!hasBilling?"Costo sin facturación del mes":"Cruce completo · costos de gestión")));
                if(incomplete)report.Unresolved++;
            }
            report.Rows=report.Rows.OrderBy(r=>Text(r,"profit")==""?1:0).ThenBy(r=>Value(r,"profit")).ThenBy(r=>Text(r,"party")).ToList();
            report.Total=report.Rows.Count>0&&report.Rows.All(r=>r["profit"] is null)?null:Sum(report,"profit");
            if(report.Unresolved>0)report.State="partial";
            snapshot.Reports.Add(report);
        }
        details.Unresolved=details.Rows.Count(r=>!Equals(r["resolved"],true));audit.Unresolved=audit.Rows.Count;
        snapshot.Reports.Add(details);snapshot.Reports.Add(audit);
        var business=new MonthCloseReport{Key="business",Title="Total de negocios activos",Unit="USD",State=observed?"captured":"unavailable",Message="Valor total USD: suma anual de cantidad × valor unidad USD × 12, igual que Todos los negocios.",Columns=[new("party","Cliente"),new("product","Producto"),new("quantity","Cantidad","number"),new("unit","Valor unidad USD","money"),new("monthly","Mensual USD","money"),new("annual","Valor total USD","money"),new("date","Renovación","date")]};
        if(observed)
        {
            foreach(var b in input.Source("cloud").Where(Active))
            {
                var q=N(b,"cr07a_quantity");var unit=Round(N(b,"cr07a_valorventaunidadusd"));
                business.Rows.Add(Row(("party",S(b,"_cr07a_clientelookup_value@OData.Community.Display.V1.FormattedValue") is {Length:>0} name?name:S(b,"cr07a_clientname")),("product",S(b,"cr07a_productname")),("quantity",q),("unit",unit),("monthly",Round(q*unit)),("annual",Round(q*unit*12)),("date",D(b,"cr07a_fecharenovacion")?.ToString("yyyy-MM-dd"))));
            }
            business.Total=Sum(business,"annual");
        }
        else if(snapshot.Reports.FirstOrDefault(r=>r.Key=="forecast") is {Rows.Count:>0} forecast)
        {
            // Historical values come exclusively from the business base already frozen at close.
            business.State=forecast.State;
            business.Message="Valor anualizado de la base mensual de negocios Cloud guardada en este cierre (× 12). No consulta contratos actuales.";
            business.Columns=[new("party","Cliente"),new("product","Producto"),new("monthly","Mensual USD guardado","money"),new("annual","Valor total USD","money")];
            foreach(var row in forecast.Rows)
                business.Rows.Add(Row(("party",Text(row,"party")),("product",Text(row,"product")),("monthly",Value(row,"amount")),("annual",Round(Value(row,"amount")*12))));
            business.Total=Sum(business,"annual");
        }
        else business.Message="No se capturó el valor de los negocios activos en este cierre histórico.";
        snapshot.Reports.Add(business);
        if(audit.Unresolved>0)snapshot.Summary.Warnings.Add($"Account ID: {audit.Unresolved} relaciones o costos requieren revisión. No se unieron clientes por nombre.");
    }
}
