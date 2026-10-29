using System.Globalization;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services.MonthClose;

public static partial class MonthCloseEngine
{
    public const string ReceivablePolicyKey = "cartera-criterio-practico-2025-v1";

    // Explicit, administrative reconstruction only. Never used by automatic captures.
    public static MonthCloseSnapshot ReviseReceivables(MonthCloseSnapshot original, MonthCloseInput input,
        string sourceHash, DateTimeOffset revisedAt)
    {
        var start = ParsePeriod(original.Summary.Period);
        if (start.Year != 2025) throw new ArgumentException("El criterio aprobado solo aplica a los cierres de 2025.");
        if (sourceHash.Length != 64 || !sourceHash.All(Uri.IsHexDigit)) throw new ArgumentException("Hash de fuentes inválido.");
        if (original.SourceHashes.ContainsKey(ReceivablePolicyKey)) throw new InvalidOperationException("El criterio ya está aplicado.");
        var snapshot = JsonSerializer.Deserialize<MonthCloseSnapshot>(JsonSerializer.Serialize(original, MonthCloseCodec.Json), MonthCloseCodec.Json)!;
        var report = snapshot.Reports.Single(r => r.Key == "receivables");
        var legacy = input.Source("billing").Where(x => S(x, "cr07a_facturacionid") != "")
            .ToDictionary(x => S(x, "cr07a_facturacionid"));
        var invoices = input.Source("invoices").Where(x => S(x, "id") != "").ToDictionary(x => S(x, "id"));
        var applications = Applications(input);
        var end = start.AddMonths(1);
        var observed = BogotaToday(input.RetrievedAt);
        var remove = new List<Dictionary<string, object?>>();
        var dateWarnings = new List<string>();
        var invalidRetentions = 0;
        var legacySettled = 0;
        var siigoSettled = 0;
        var siigoOutstanding = 0;

        foreach (var row in report.Rows.Where(r => RowNumber(r, "balance") is null))
        {
            var id = RowText(row, "source");
            if (legacy.TryGetValue(id, out var invoice) && RowText(row, "quality").StartsWith("Registro legado", StringComparison.Ordinal))
            {
                if (S(invoice, "cr07a_name") != RowText(row, "document") || S(invoice, "cr07a_nitempresa") != RowText(row, "taxId"))
                    throw new InvalidDataException("La fuente no corresponde al documento guardado.");
                var total = N(invoice, "cr07a_totalfactura");
                if (RowNumber(row, "total") is not { } storedTotal || Math.Abs(storedTotal - total) > 1m)
                    throw new InvalidDataException("El importe fuente cambió frente a la versión guardada.");
                var paid = N(invoice, "cr07a_valorpago");
                var retentions = new[] { "cr07a_rteftevalor", "cr07a_reteicavalor", "cr07a_rteivavalor" }.Select(k => N(invoice, k)).ToArray();
                if (retentions.Any(r => r < 0) || retentions.Sum() > total) invalidRetentions++;
                // COP 1 absorbs rounding at the approved ~11% boundary. Retention fields
                // cannot establish settlement: imported amounts may exceed the invoice.
                if (total <= 0 || paid <= 0 || Math.Abs(total - paid) > total * .11m + 1m
                    || D(invoice, "cr07a_fechadepago") is not { } date || date >= end || date > observed) continue;
                var reason = "Pago asumido por criterio autorizado: diferencia máxima aproximada del 11% (tolerancia COP 1); sin conciliación bancaria individual.";
                Settle(row, date, total, paid, reason, "Fecha y valor de pago de Dataverse");
                legacySettled++;
            }
            else if (invoices.TryGetValue(id, out invoice) && RowText(row, "quality").StartsWith("Faltan aplicaciones", StringComparison.Ordinal))
            {
                var name = S(invoice, "name");
                var nit = S(P(invoice, "customer"), "identification");
                if (name != RowText(row, "document") || nit != RowText(row, "taxId")) throw new InvalidDataException("Factura Siigo distinta de la versión guardada.");
                var total = N(invoice, "total");
                var app = applications.GetValueOrDefault(nit + "|" + name) ?? [];
                // The approved cases have zero current balance and exactly one linked
                // full closing entry. Do not generalize to ambiguous or partial entries.
                if (total <= 0 || !Has(invoice, "balance") || Math.Abs(N(invoice, "balance")) > 1m
                    || app.Count != 1 || app[0].Value <= 0 || Math.Abs(app[0].Value - total) > 1m || app[0].Date > observed) continue;
                var payment = app[0];
                if (payment.Date < end)
                {
                    Settle(row, payment.Date, total, payment.Value,
                        "Pago asumido por comprobante vinculado que cancela el total y saldo actual cero. No se vuelve a descontar una nota crédito sobre el pago de cierre.", payment.Reference);
                    siigoSettled++;
                }
                else
                {
                    var notes = input.Source("credit-notes").Where(n => S(P(n, "invoice"), "id") == id
                        && D(n, "date") is { } d && d < end).Select(n => N(n, "total")).ToList();
                    if (notes.Any(n => n < 0) || notes.Sum() > total) continue;
                    row["balance"] = Round(total - notes.Sum());
                    row["applications"] = 0m;
                    row["notes"] = notes.Sum();
                    row["quality"] = "Criterio autorizado: pago posterior al corte; saldo del documento menos notas crédito identificadas al corte.";
                    row["support"] = $"{payment.Reference} · pago {payment.Date:yyyy-MM-dd} posterior al corte";
                    siigoOutstanding++;
                }
            }
        }

        void Settle(Dictionary<string, object?> row, DateOnly date, decimal total, decimal paid, string reason, string reference)
        {
            if (DateOnly.TryParse(RowText(row, "date"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var issued) && date < issued)
            {
                dateWarnings.Add(RowText(row, "document"));
                reason += " Fecha de pago anterior a la emisión: posible anticipo o dato por revisar.";
            }
            report.SettlementAssumptions.Add(new(RowText(row, "source"), RowText(row, "document"), RowText(row, "party"),
                date.ToString("yyyy-MM-dd"), total, paid, reason, reference));
            remove.Add(row);
        }

        foreach (var row in remove) report.Rows.Remove(row);
        report.Unresolved = report.Rows.Count(r => RowNumber(r, "balance") is null);
        report.Total = report.Unresolved > 0 && report.Rows.All(r => RowNumber(r, "balance") is null) ? null
            : Round(report.Rows.Sum(r => RowNumber(r, "balance") ?? 0));
        report.Message += $" Criterio práctico autorizado para 2025: {legacySettled + siigoSettled} documentos tratados como pagados al corte. Son supuestos de reconstrucción, no conciliaciones bancarias. El Excel incluye el detalle en Pagos asumidos.";
        report.Needed = "Revisar diferencias mayores al criterio, fechas ausentes y retenciones inconsistentes; verificar con soportes los pagos asumidos y los anticipos señalados.";
        report.State = "partial";
        var age = new decimal[4];
        foreach (var row in report.Rows)
        {
            if (RowNumber(row, "balance") is not { } balance || balance <= 0) continue;
            if (!DateOnly.TryParse(RowText(row, "due"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var due))
                throw new InvalidDataException("Saldo positivo sin fecha de vencimiento; no se puede reclasificar la cartera.");
            var days = Math.Max(0, end.AddDays(-1).DayNumber - due.DayNumber);
            age[days == 0 ? 0 : days <= 30 ? 1 : days <= 60 ? 2 : 3] += balance;
        }
        var summary = snapshot.Summary;
        summary.Aging = report.Total.HasValue ? new[] { "Al día", "1–30 días", "31–60 días", "Más de 60 días" }
            .Select((name, i) => new MonthCloseSlice(name, Round(age[i]))).ToList() : [];
        summary.Reports = summary.Reports.Select(m => m.Key == report.Key ? m with
            { Count = report.Rows.Count, Unresolved = report.Unresolved, Total = report.Total, Message = report.Message, Needed = report.Needed, State = report.State } : m).ToList();
        summary.Warnings.RemoveAll(w => w.StartsWith("Cartera: ", StringComparison.Ordinal));
        summary.Warnings.Add($"Cartera: {report.Unresolved} documentos sin saldo determinado, excluidos del subtotal. Criterio autorizado: {legacySettled} pagos de Dataverse y {siigoSettled} pagos de Siigo asumidos; {siigoOutstanding} documentos con pago posterior conservados como cartera al corte.");
        if (invalidRetentions > 0) summary.Warnings.Add($"Calidad: {invalidRetentions} documentos legados tienen retenciones negativas o superiores a la factura. No se usaron esos valores para justificar los pagos asumidos.");
        if (dateWarnings.Count > 0) summary.Warnings.Add("Posibles anticipos o fechas por revisar: " + string.Join(", ", dateWarnings) + ". Se conserva la fecha original; solo se consideran pagados en cortes posteriores a esa fecha.");
        summary.Warnings.Add($"Revisión del criterio de cartera sobre v{original.Summary.Revision}, SHA-256 {original.Summary.Hash}. Los demás reportes y las exclusiones temporales previas se conservan.");
        snapshot.SourceHashes[ReceivablePolicyKey] = sourceHash;
        summary.Revision++;
        summary.CapturedAt = revisedAt;
        summary.Id = MonthCloseCodec.IdFor(summary.Period, summary.Revision);
        summary.Hash = "";
        return snapshot;
    }

    private static string RowText(Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "";
    private static decimal? RowNumber(Dictionary<string, object?> row, string key) =>
        decimal.TryParse(RowText(row, key), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
}
