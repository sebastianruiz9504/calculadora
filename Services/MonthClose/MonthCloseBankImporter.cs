using System.Text.Json;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services.MonthClose;

// Offline evidence supplied to the maintenance command. Never loaded by a dashboard request.
public static class MonthCloseBankImporter
{
    public static async Task<MonthCloseBankEvidence?> ReadAsync(string? path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (new FileInfo(path).Length > 20_000_000) throw new InvalidDataException("El soporte bancario supera 20 MB.");
        var input = JsonSerializer.Deserialize<MonthCloseBankEvidence>(await File.ReadAllTextAsync(path, ct), MonthCloseCodec.Json)
            ?? throw new InvalidDataException("Soporte bancario vacío.");
        Validate(input);
        return input;
    }

    public static void Validate(MonthCloseBankEvidence input)
    {
        static void Require(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
        static bool Hash(string value) => Regex.IsMatch(value ?? "", "^[0-9a-fA-F]{64}$");
        Require(Hash(input.ArchiveSha256), "Falta SHA-256 del archivo bancario original.");
        Require(input.Statements.Count > 0, "No hay extractos bancarios.");
        Require(input.Statements.Select(x => (x.Account, x.Period)).Distinct().Count() == input.Statements.Count, "Cuenta/período bancario duplicado.");
        Require(input.Statements.Select(x => x.File).Distinct().Count() == input.Statements.Count, "Extracto bancario duplicado.");
        Require(input.Transactions.Select(x => x.Id).Distinct().Count() == input.Transactions.Count, "Movimiento bancario duplicado.");
        var byFile = input.Transactions.ToLookup(x => x.Source);
        Require(byFile.All(g => input.Statements.Any(s => s.File == g.Key)), "Movimiento sin extracto de origen.");
        foreach (var s in input.Statements)
        {
            var start = MonthCloseEngine.ParsePeriod(s.Period.Length == 6 ? s.Period.Insert(4, "-") : s.Period);
            Require(Regex.IsMatch(s.Account, "^[0-9]{4}$") && Hash(s.DocumentSha256), "Cuenta o hash de PDF inválido.");
            Require(!string.IsNullOrWhiteSpace(s.File) && Path.GetFileName(s.File) == s.File, "Referencia de PDF inválida.");
            var rows = byFile[s.File].OrderBy(x => x.Sequence).ToList();
            Require(rows.Count == s.Rows, $"Faltan movimientos en {s.File}.");
            // Banks can report prior-month value dates (fees/interest adjustments)
            // in the next statement. Membership follows the statement, not value date.
            decimal previous = s.Opening; var previousDate = start.AddMonths(-1);
            for (var n = 0; n < rows.Count; n++)
            {
                var r = rows[n];
                Require(r.Account == s.Account && r.Sequence == n + 1 && r.Page > 0 && !string.IsNullOrWhiteSpace(r.Id), "Identidad/secuencia bancaria inválida.");
                Require(r.Date >= previousDate && r.Date < start.AddMonths(1), $"Fecha fuera del extracto {s.File}.");
                Require(Math.Abs(previous + r.Value - r.Balance) <= .02m, $"Saldo no conciliado en {s.File}, movimiento {r.Sequence}.");
                previous = r.Balance; previousDate = r.Date;
            }
            Require(s.Credits >= 0 && s.Debits >= 0 && Math.Abs(rows.Where(x => x.Value > 0).Sum(x => x.Value) - s.Credits) <= .02m
                && Math.Abs(-rows.Where(x => x.Value < 0).Sum(x => x.Value) - s.Debits) <= .02m
                && Math.Abs(previous - s.Closing) <= .02m && Math.Abs(s.Opening + s.Credits - s.Debits - s.Closing) <= .02m,
                $"Los totales del extracto {s.File} no concilian.");
        }
        foreach (var account in input.Statements.GroupBy(x => x.Account))
        {
            var ordered = account.OrderBy(x => x.Period.Replace("-", "")).ToArray();
            for (var i = 1; i < ordered.Length; i++)
            {
                var previous = ordered[i - 1]; var next = ordered[i];
                var p = previous.Period.Replace("-", "").Insert(4, "-");
                if (MonthCloseEngine.ParsePeriod(p).AddMonths(1).ToString("yyyyMM") == next.Period.Replace("-", ""))
                    Require(Math.Abs(previous.Closing - next.Opening) <= .02m, "Los saldos no continúan entre meses.");
            }
        }
    }
}
