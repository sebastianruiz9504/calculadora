using System.Text.Json;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services.MonthClose;

public static partial class MonthCloseEngine
{
    private static bool IsDemoInvoice(JsonElement row) => S(row, "cr07a_cliente").Trim().Equals("DEMO", StringComparison.OrdinalIgnoreCase);

    // The historical migration renamed DT<number> to FV-2-<number>. Require a unique
    // document, the same customer/amount and at most one day of migration date drift.
    private static HashSet<string> LinkLegacyInvoices(List<JsonElement> legacy, List<JsonElement> invoices, Dictionary<string, JsonElement> mapping)
    {
        var byKey = invoices.ToLookup(x => S(x, "name") + "|" + S(P(x, "customer"), "identification"));
        var represented = new HashSet<string>();
        foreach (var row in legacy)
        {
            var name = S(row, "cr07a_name");
            if (!Regex.IsMatch(name, "^DT[0-9]+$") || D(row, "cr07a_fechadeemision") is not { } issued) continue;
            var key = "FV-2-" + name[2..] + "|" + S(row, "cr07a_nitempresa");
            var matches = byKey[key].Where(x => D(x, "date") is { } d && Math.Abs(d.DayNumber - issued.DayNumber) <= 1
                && Has(row,"cr07a_totalfactura") && Math.Abs(N(x,"total") - N(row,"cr07a_totalfactura")) <= 1m).ToList();
            if (matches.Count != 1) continue;
            represented.Add(name + "|" + S(row,"cr07a_nitempresa"));
            mapping.TryAdd(key, row);
        }
        return represented;
    }

    private static void AddLegacyBilling(MonthCloseReport report, List<JsonElement> legacy, HashSet<string> siigoKeys,
        HashSet<string> represented, Dictionary<string,string> clients, DateOnly start, DateOnly end)
    {
        var count = 0;
        foreach (var row in legacy.Where(x => D(x,"cr07a_fechadeemision") is {} d && d>=start && d<end))
        {
            var nit = S(row,"cr07a_nitempresa"); var key = S(row,"cr07a_name") + "|" + nit;
            if (siigoKeys.Contains(key) || represented.Contains(key)) continue;
            count++;
            var gross = Has(row,"cr07a_totalfactura") ? N(row,"cr07a_totalfactura") : (decimal?)null;
            decimal? basis = Has(row,"cr07a_facturaantesdeiva") ? N(row,"cr07a_facturaantesdeiva") : null;
            decimal? tax = Has(row,"cr07a_ivavalor") ? N(row,"cr07a_ivavalor") : null;
            // Legacy total is gross, before withholding. Never derive taxes/retentions
            // from an unvalidated percentage or from the current payment amount.
            if (basis is null || tax is null || gross is null || basis < 0 || tax < 0 || Math.Abs(basis.Value + tax.Value - gross.Value) > 1m)
            { basis = null; tax = null; }
            var retFields = new[]{"cr07a_rteftevalor","cr07a_rteivavalor","cr07a_reteicavalor"};
            decimal? retention = retFields.All(k => Has(row,k) && N(row,k)>=0) ? retFields.Sum(k => N(row,k)) : null;
            if (gross is null || retention > gross) retention = null;
            var line = Line(row);
            // Historical credit notes for legacy invoices are not fully available.
            report.Unresolved++;
            report.Rows.Add(Row(("document", S(row,"cr07a_name")), ("party", clients.GetValueOrDefault(nit,S(row,"cr07a_cliente"))),
                ("taxId",nit), ("date",D(row,"cr07a_fechadeemision")?.ToString("yyyy-MM-dd")), ("line",line),
                ("base",basis), ("tax",tax), ("retentions",retention), ("total",gross.HasValue&&retention.HasValue?gross-retention:null),
                ("quality",basis is null ? "Legado: base/IVA por conciliar; excluido de la base total" : retention is null ? "Base/IVA consistentes; retenciones y notas crédito históricas pendientes" : "Legado: notas crédito históricas pendientes"),
                ("source",S(row,"cr07a_facturacionid"))));
        }
        if (count == 0) return;
        report.State = "partial";
        report.Message = "Facturas Siigo y registros legados de Dataverse sin duplicar documentos migrados, menos notas crédito disponibles. La base es parcial cuando faltan importes verificables o notas históricas.";
        report.Needed = "Validar el auxiliar de facturación anterior a Siigo, notas crédito y retenciones inconsistentes. La línea comercial corresponde a la clasificación disponible al reconstruir.";
    }
}
