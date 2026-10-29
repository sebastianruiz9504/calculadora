using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services.MonthClose;

public static partial class MonthCloseEngine
{
    private static void AddBanks(MonthCloseSnapshot snapshot, MonthCloseInput input, string period)
    {
        if (input.BankEvidence is not { } evidence) return;
        MonthCloseBankImporter.Validate(evidence);
        var statements = evidence.Statements.Where(s => s.Period.Replace("-", "") == period.Replace("-", "")).OrderBy(s => s.Account).ToList();
        if (statements.Count == 0) return;
        var files = statements.Select(s => s.File).ToHashSet();
        var report = new MonthCloseReport
        {
            Key = "banks", Title = "Bancos · saldos y movimientos", State = "verified", Unit = "COP",
            Message = "Saldo de cierre de las cuentas incluidas en los extractos. Los movimientos pertenecen al mes del extracto y pueden incluir ajustes con fecha anterior. Incluyen transferencias internas y no equivalen a ventas, recaudos de facturas o gastos.",
            Needed = "Saldos, movimientos y totales conciliados con los extractos entregados. La aplicación a facturas se revisa por separado.",
            Total = statements.Sum(s => s.Closing),
            BankAccounts = statements.Select(s => new MonthCloseBankAccount(s.Account, s.Opening, s.Credits, s.Debits, s.Closing)).ToList(),
            Columns = [new("date", "Fecha", "date"), new("account", "Cuenta"), new("description", "Descripción bancaria"),
                new("credit", "Abono", "money"), new("debit", "Cargo", "money"), new("balance", "Saldo cuenta", "money"),
                new("source", "Extracto"), new("page", "Página", "number")]
        };
        foreach (var t in evidence.Transactions.Where(t => files.Contains(t.Source)).OrderBy(t => t.Date).ThenBy(t => t.Account).ThenBy(t => t.Sequence))
            report.Rows.Add(Row(("date", t.Date.ToString("yyyy-MM-dd")), ("account", "•••• " + t.Account), ("description", t.Description),
                ("credit", t.Value > 0 ? t.Value : 0m), ("debit", t.Value < 0 ? -t.Value : 0m), ("balance", t.Balance), ("source", t.Source), ("page", t.Page)));
        snapshot.Reports.Add(report);
        snapshot.SourceHashes["bank-archive"] = evidence.ArchiveSha256;
        foreach (var s in statements) snapshot.SourceHashes[s.File] = s.DocumentSha256;
        snapshot.SourceCounts["bank-statements"] = statements.Count;
        snapshot.SourceCounts["bank-transactions"] = report.Rows.Count;
    }
}
