using System.Globalization;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services.MonthClose;

// Pure calculation: no live I/O, no mutation of invoices or historical payments.
public static partial class MonthCloseEngine
{
    public const int FirstYear = 2025;
    public static readonly string[] ReportKeys = ["receivables", "payables", "billing", "forecast", "inventory", "assets", "vacations", "banks"];
    public static DateOnly BogotaToday(DateTimeOffset utc) => DateOnly.FromDateTime(utc.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    public static DateOnly ParsePeriod(string period)
    {
        if (!DateOnly.TryParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || start.Year < FirstYear || start.Year > 2100)
            throw new ArgumentException("El período debe tener formato AAAA-MM, desde enero de 2025.");
        return start;
    }

    public static MonthCloseSnapshot Build(string period, int revision, MonthCloseInput input)
    {
        var start = ParsePeriod(period);
        var end = start.AddMonths(1);
        var today = BogotaToday(input.RetrievedAt);
        if (end > today) throw new ArgumentException("El mes está abierto. No se puede guardar como cierre.");
        // Mutable operational fields cannot be projected backwards. Even on the first
        // day they are labeled with their observation time, not asserted to be midnight.
        var captureOperational = today == end && input.RetrievedAt.ToOffset(TimeSpan.FromHours(-5)).Hour == 0;
        var result = new MonthCloseSnapshot
        {
            Summary = new() { Period = period, Revision = revision, CapturedAt = input.RetrievedAt,
                Cutoff = end.AddDays(-1).ToString("yyyy-MM-dd"), Mode = captureOperational ? "captured" : "reconstructed" },
            SourceCounts = input.Tables.Concat(input.Siigo).ToDictionary(x => x.Key, x => x.Value.Count)
        };
        var dvInvoices = input.Source("billing").Where(x => !IsDemoInvoice(x)).ToList();
        var mapping = dvInvoices.GroupBy(x => S(x, "cr07a_name") + "|" + S(x, "cr07a_nitempresa"))
            .ToDictionary(g => g.Key, g => g.First());
        var invoices = input.Source("invoices");
        var representedLegacyKeys = LinkLegacyInvoices(dvInvoices, invoices, mapping);
        var invoiceById = invoices.Where(x => S(x, "id") != "").GroupBy(x => S(x, "id")).ToDictionary(g => g.Key, g => g.First());
        var clients = input.Source("clients").GroupBy(x => S(x, "cr07a_nit")).ToDictionary(g => g.Key, g => S(g.First(), "cr07a_nombre"));
        var notes = input.Source("credit-notes").GroupBy(x => S(P(x, "invoice"), "id")).ToDictionary(g => g.Key, g => g.ToList());
        var applications = Applications(input);
        var ar = FinancialReport("receivables", "Cuentas por cobrar");
        ar.Message = "Saldo identificable al corte. Documentos y aplicaciones de Siigo; los casos sin trazabilidad se muestran sin saldo y no se suman.";
        ar.Needed = "Auxiliar de cartera inicial y recibos históricos con factura, tercero, fecha efectiva y valor aplicado para resolver las filas sin saldo.";
        var age = new decimal[4];
        foreach (var inv in invoices.Where(x => D(x, "date") is { } d && d < end))
        {
            var issued = D(inv, "date")!.Value;
            var nit = S(P(inv, "customer"), "identification");
            var name = S(inv, "name");
            var values = Amounts(inv);
            var app = applications.GetValueOrDefault(nit + "|" + name) ?? [];
            var credit = notes.GetValueOrDefault(S(inv, "id")) ?? [];
            var paymentsBefore = app.Where(x => x.Date >= issued && x.Date < end).Sum(x => x.Value);
            var notesBefore = credit.Where(x => D(x, "date") is { } d && d < end).Sum(x => N(x, "total"));
            var allApplied = app.Where(x => x.Date >= issued && x.Date <= today).Sum(x => x.Value)
                + credit.Where(x => D(x, "date") is { } d && d <= today).Sum(x => N(x, "total"));
            var residual = N(inv, "total") - allApplied;
            var unknown = !Has(inv, "balance") || Math.Abs(residual - N(inv, "balance")) > 1m;
            decimal? balance = unknown ? null : Math.Max(0, N(inv, "total") - paymentsBefore - notesBefore);
            if (balance <= 0.01m) continue;
            var due = A(inv, "payments").Select(x => D(x, "due_date")).Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(issued).Min();
            var days = Math.Max(0, end.AddDays(-1).DayNumber - due.DayNumber);
            if (balance.HasValue) age[days == 0 ? 0 : days <= 30 ? 1 : days <= 60 ? 2 : 3] += balance.Value;
            var dv = mapping.GetValueOrDefault(name + "|" + nit);
            ar.Rows.Add(Row(("document",name),("party",clients.GetValueOrDefault(nit,nit)),("taxId",nit),("date",issued.ToString("yyyy-MM-dd")),
                ("due",due.ToString("yyyy-MM-dd")),("base",values.Base),("tax",values.Tax),("retentions",values.Retentions),
                ("total",N(inv,"total")),("applications",paymentsBefore),("notes",notesBefore),("balance",balance),
                ("line",Line(dv)),("quality",unknown ? "Faltan aplicaciones históricas; saldo no determinado" : "Reconstruido por fecha efectiva"),
                ("source",S(inv,"id")),("support",string.Join("; ",app.Where(x=>x.Date>=issued&&x.Date<end).Select(x=>x.Reference).Distinct()))));
            if (unknown) ar.Unresolved++;
        }
        // Keep legacy rows visible instead of silently treating missing Siigo history as zero.
        var siigoKeys = invoices.Select(x => S(x,"name")+"|"+S(P(x,"customer"),"identification")).ToHashSet();
        foreach (var inv in dvInvoices.Where(x => D(x,"cr07a_fechadeemision") is { } d && d<end
            && !siigoKeys.Contains(S(x,"cr07a_name")+"|"+S(x,"cr07a_nitempresa"))
            && !representedLegacyKeys.Contains(S(x,"cr07a_name")+"|"+S(x,"cr07a_nitempresa"))
            && (D(x,"cr07a_fechadepago") is not { } paid || paid >= end || N(x,"cr07a_valorpago") < N(x,"cr07a_totalfactura")-1)))
        {
            ar.Unresolved++;
            ar.Rows.Add(Row(("document",S(inv,"cr07a_name")),("party",clients.GetValueOrDefault(S(inv,"cr07a_nitempresa"),S(inv,"cr07a_cliente"))),("taxId",S(inv,"cr07a_nitempresa")),
                ("date",D(inv,"cr07a_fechadeemision")?.ToString("yyyy-MM-dd")),("total",N(inv,"cr07a_totalfactura")),("balance",null),
                ("quality","Registro legado sin auxiliar histórico verificable"),("source",S(inv,"cr07a_facturacionid"))));
        }
        ar.Total = ar.Unresolved > 0 && ar.Rows.All(r => r["balance"] is null) ? null : Sum(ar,"balance");
        result.Reports.Add(ar);
        result.Summary.Aging = ar.Total.HasValue ? new[] {"Al día","1–30 días","31–60 días","Más de 60 días"}.Select((x,i)=>new MonthCloseSlice(x,Round(age[i]))).ToList() : [];

        var ap = BuildPayables(input, end, today);
        result.Reports.Add(ap);

        var billing=FinancialReport("billing","Facturación por línea de negocio");
        billing.State="reconstructed";
        billing.Message="Base emitida menos notas crédito expedidas en el mes. La clasificación por vertical se conserva como estaba al reconstruir.";
        billing.Needed="Completar las facturas sin vertical; para sublíneas Cloud se necesita la relación entre cada ítem facturado y su línea comercial.";
        foreach(var doc in invoices.Where(x=>D(x,"date") is {} d && d>=start && d<end)) AddBilling(doc,1,null);
        foreach(var doc in input.Source("credit-notes").Where(x=>D(x,"date") is {} d && d>=start && d<end)) AddBilling(doc,-1,invoiceById.GetValueOrDefault(S(P(doc,"invoice"),"id")));
        AddLegacyBilling(billing, dvInvoices, siigoKeys, representedLegacyKeys, clients, start, end);
        void AddBilling(JsonElement doc,int sign,JsonElement? original)
        {
            var invoice=original ?? doc;
            var nit=S(P(invoice,"customer"),"identification");
            var dv=mapping.GetValueOrDefault(S(invoice,"name")+"|"+nit);
            var amounts=Amounts(doc); var line=Line(dv);
            if(line=="Sin asignar")billing.Unresolved++;
            billing.Rows.Add(Row(("document",S(doc,"name")),("party",clients.GetValueOrDefault(nit,nit)),("taxId",nit),("date",S(doc,"date")),
                ("line",line),("base",sign*amounts.Base),("tax",sign*amounts.Tax),("retentions",sign*amounts.Retentions),
                ("total",sign*N(doc,"total")),("quality",sign<0?"Nota crédito del período":"Factura emitida"),("source",S(doc,"id"))));
        }
        billing.Total=billing.Unresolved > 0 && billing.Rows.All(r => r["base"] is null) ? null : Sum(billing,"base"); result.Reports.Add(billing);
        if (billing.Unresolved > 0) billing.State = "partial";
        result.Summary.BillingLines=billing.Rows.Where(x=>x["base"] is not null).GroupBy(x=>Convert.ToString(x["line"])??"Sin asignar").Select(g=>new MonthCloseSlice(g.Key,g.Sum(x=>Convert.ToDecimal(x["base"])))).ToList();
        AddOperational(result,input,captureOperational);
        AddBanks(result,input,period);
        result.Summary.Warnings.Add("Los cierres reconstruidos no equivalen a una captura tomada en el pasado. Los saldos pendientes de soporte se identifican y no se presentan como cero.");
        if(ar.Unresolved>0)result.Summary.Warnings.Add($"Cartera: {ar.Unresolved} documentos sin saldo histórico determinable, excluidos del total identificable.");
        result.Summary.Warnings.Add($"Proveedores: saldo parcial identificable de compras Siigo. {ap.Unresolved} documentos sin saldo verificable quedan excluidos del total; faltan auxiliares de documentos soporte y obligaciones fuera de compras.");
        if (start.Year == 2025) result.Summary.Warnings.Add("Cartera 2025: falta conciliar el auxiliar inicial al 31/12/2024 y aplicaciones de documentos legados. Una fecha única de pago no reconstruye abonos parciales.");
        var demos = input.Source("billing").Count(x => IsDemoInvoice(x) && D(x,"cr07a_fechadeemision") is {} d && d>=start && d<end);
        if (demos > 0) result.Summary.Warnings.Add($"Facturación: {demos} registros identificados como DEMO excluidos del mes.");
        result.Summary.Reports=result.Reports.Select(r=>new MonthCloseMetric(r.Key,r.Title,r.State,r.Message,r.Needed,r.Unit,r.Total,r.Rows.Count,r.Unresolved)).ToList();
        return result;
    }

    private sealed record Application(DateOnly Date,decimal Value,string Reference);
    private static Dictionary<string,List<Application>> Applications(MonthCloseInput input)
    {
        var result=new Dictionary<string,List<Application>>();
        foreach(var doc in input.Source("journals"))
        {
            if(D(doc,"date") is not {} date)continue;
            foreach(var item in A(doc,"items"))
            {
                if(!S(P(item,"account"),"code").StartsWith("1305",StringComparison.Ordinal))continue;
                var due=P(item,"due"); if(S(due,"prefix")=="")continue;
                var key=S(P(item,"customer"),"identification")+"|"+S(due,"prefix")+"-"+S(due,"consecutive");
                var sign=S(P(item,"account"),"movement")=="Credit"?1:-1;
                if(!result.TryGetValue(key,out var list))result[key]=list=[];
                list.Add(new(date,sign*N(item,"value"),S(doc,"name")));
            }
        }
        foreach(var doc in input.Source("vouchers"))
        {
            if(D(doc,"date") is not {} date)continue;
            foreach(var item in A(doc,"items"))
            {
                var due=P(item,"due"); if(S(due,"prefix")=="")continue;
                var key=S(P(doc,"customer"),"identification")+"|"+S(due,"prefix")+"-"+S(due,"consecutive");
                if(!result.TryGetValue(key,out var list))result[key]=list=[];
                list.Add(new(date,N(item,"value"),S(doc,"name")));
            }
        }
        return result;
    }

    private static void AddOperational(MonthCloseSnapshot snapshot,MonthCloseInput input,bool observed)
    {
        MonthCloseReport New(string key,string title,string needed,string unit="COP")
        {
            var r=new MonthCloseReport{Key=key,Title=title,State=observed?"partial":"unavailable",Unit=unit,Needed=needed,
                Message=observed?"Estado operativo observado durante la captura; revisar las limitaciones antes de usarlo como cierre contable.":"No hay una captura histórica ni movimientos suficientes para recuperar este mes con fiabilidad."};
            snapshot.Reports.Add(r);return r;
        }
        var forecast=New("forecast","Facturación del mes siguiente","Historial de contratos vigentes al corte, cantidades, moneda, TRM, día de facturación y consumos variables.","USD");
        forecast.Columns=[new("party","Cliente"),new("product","Producto"),new("amount","Base mensual USD","money"),new("quality","Alcance")];
        var inventory=New("inventory","Inventarios","Saldo inicial por referencia y bodega; entradas, salidas, fecha efectiva, costo unitario y método de valoración.","unidades");
        inventory.Columns=[new("product","Referencia"),new("quantity","Cantidad observada","number"),new("quality","Valoración")];
        var assets=New("assets","Activos fijos","Maestro de activos propios: costo de adquisición, fecha de alta/baja, vida útil, residual y depreciación acumulada.","equipos");
        assets.Columns=[new("product","Equipo"),new("serial","Serial"),new("date","Fecha de compra","date"),new("quality","Base contable")];
        var vacations=New("vacations","Vacaciones","Saldo inicial conciliado por empleado, vacaciones tomadas por fecha y salario/base de liquidación vigente al cierre.");
        vacations.Columns=[new("party","Empleado"),new("date","Ingreso","date"),new("taken","Días tomados","number"),new("pending","Días pendientes","number"),new("amount","Estimación con sueldo/30","money"),new("quality","Observación")];
        if(!observed)return;
        foreach(var x in input.Source("cloud").Where(Active))forecast.Rows.Add(Row(("party",S(x,"cr07a_clientname")),("product",S(x,"cr07a_productname")),("amount",N(x,"cr07a_valorventatotalmensual")),("quality","Recurrente Cloud orientativo; validar vigencia. No incluye variables ni Copiers.")));
        forecast.Total=Sum(forecast,"amount");
        foreach(var x in input.Source("inventory").Where(Active))inventory.Rows.Add(Row(("product",S(x,"cr07a_nombredelsuministro")),("quantity",N(x,"cr07a_cantidad")),("quality","Cantidad operativa; sin valoración contable")));
        inventory.Total=Sum(inventory,"quantity");
        foreach(var x in input.Source("assets").Where(Active))assets.Rows.Add(Row(("product",S(x,"cr07a_nombredelequipo")),("serial",S(x,"cr07a_serial")),("date",D(x,"cr07a_fechadecompra")?.ToString("yyyy-MM-dd")),("quality","Inventario físico; propiedad y depreciación pendientes")));
        assets.Total=assets.Rows.Count;
        foreach(var x in input.Source("employees").Where(x=>Active(x)&&N(x,"cr07a_tipocontrato")==645250000))
        {
            decimal? days=Has(x,"cr07a_diasdisponiblescalculados")?N(x,"cr07a_diasdisponiblescalculados"):null;
            decimal? value=days.HasValue&&Has(x,"cr07a_sueldomensual")?Round(days.Value*N(x,"cr07a_sueldomensual")/30):null;
            vacations.Rows.Add(Row(("party",S(x,"cr07a_nombrecompleto")),("date",D(x,"cr07a_fechadeingreso")?.ToString("yyyy-MM-dd")),
                ("taken",Has(x,"cr07a_diastomadoscalculados")?N(x,"cr07a_diastomadoscalculados"):null),("pending",days),("amount",value),
                ("quality","Estimación informativa: requiere saldo conciliado y base salarial validada")));
            if(!value.HasValue)vacations.Unresolved++;
        }
        vacations.Total=Sum(vacations,"amount");
    }

    private static MonthCloseReport FinancialReport(string key,string title)=>new(){Key=key,Title=title,Columns=[
        new("document","Documento"),new("party","Tercero"),new("taxId","NIT"),new("line","Línea"),new("date","Emisión","date"),new("due","Vence","date"),
        new("base","Servicio / base","money"),new("tax","Impuestos","money"),new("retentions","Retenciones de factura","money"),new("total","Total neto documento","money"),
        new("applications","Aplicaciones al corte","money"),new("notes","Notas crédito al corte","money"),new("balance","Saldo al corte","money"),new("quality","Calidad / alcance"),new("support","Comprobantes"),new("source","ID fuente")]};
    internal static (decimal Base,decimal Tax,decimal Retentions) Amounts(JsonElement doc)
    {
        var taxes=A(doc,"items").SelectMany(x=>A(x,"taxes")).Concat(A(doc,"retentions")).ToList();
        bool Ret(JsonElement x)=>S(x,"type").StartsWith("Rete",StringComparison.OrdinalIgnoreCase);
        var tax=taxes.Where(x=>!Ret(x)).Sum(x=>N(x,"value")); var ret=taxes.Where(Ret).Sum(x=>N(x,"value"));
        return(Round(N(doc,"total")-tax+ret),Round(tax),Round(ret));
    }
    private static bool Active(JsonElement x)=>!Has(x,"statecode")||N(x,"statecode")==0;
    private static string Line(JsonElement dv)=>N(dv,"cr07a_vertical") switch{645250000=>"Cloud",645250001=>"Copiers",_=>"Sin asignar"};
    public static JsonElement P(JsonElement x,string key)=>x.ValueKind==JsonValueKind.Object&&x.TryGetProperty(key,out var v)?v:default;
    public static bool Has(JsonElement x,string key)=>P(x,key).ValueKind is not(JsonValueKind.Null or JsonValueKind.Undefined);
    public static string S(JsonElement x,string key)=>P(x,key).ValueKind is JsonValueKind.Null or JsonValueKind.Undefined?"":P(x,key).ToString();
    public static IEnumerable<JsonElement> A(JsonElement x,string key)=>P(x,key).ValueKind==JsonValueKind.Array?P(x,key).EnumerateArray():[];
    public static decimal N(JsonElement x,string key)
    {
        var p=P(x,key);if(p.ValueKind==JsonValueKind.Number&&p.TryGetDecimal(out var d))return d;
        var s=S(x,key).Trim().Replace("$","");
        if(s.Contains(',')&&s.Contains('.'))s=s.LastIndexOf(',')>s.LastIndexOf('.')?s.Replace(".","").Replace(',', '.'):s.Replace(",","");
        else if(s.Contains(','))s=s.Replace(',','.');
        return decimal.TryParse(s,NumberStyles.Number,CultureInfo.InvariantCulture,out var n)?n:0;
    }
    public static DateOnly? D(JsonElement x,string key)=>S(x,key) is {Length:>=10} s&&DateOnly.TryParseExact(s[..10],"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)?d:null;
    private static decimal Round(decimal x)=>Math.Round(x,2,MidpointRounding.AwayFromZero);
    private static decimal Sum(MonthCloseReport r,string key)=>Round(r.Rows.Sum(x=>x.TryGetValue(key,out var v)&&v is not null?Convert.ToDecimal(v,CultureInfo.InvariantCulture):0));
    private static Dictionary<string,object?> Row(params (string Key,object? Value)[] values)=>values.ToDictionary(x=>x.Key,x=>x.Value);
}
