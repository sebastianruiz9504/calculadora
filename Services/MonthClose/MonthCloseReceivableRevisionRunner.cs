using System.Security.Cryptography;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services.MonthClose;

// Administrative CLI only: prepare a frozen proposal, then create those exact revisions.
public static class MonthCloseReceivableRevisionRunner
{
    private sealed record Entry(string Period, Guid PreviousId, string PreviousHash, Guid Id, string Hash);
    public static async Task RunAsync(IMonthCloseRepository repository, string? sourcePath, string outputPath, bool apply, CancellationToken ct)
    {
        var root = Path.GetFullPath(outputPath);
        var planPath = Path.Combine(root, "plan.json");
        var existing = await repository.ListAsync(2025, ct);
        if (!apply)
        {
            if (File.Exists(planPath)) throw new InvalidOperationException("Ya existe un plan; aplica el plan revisado o elige otra carpeta.");
            if (string.IsNullOrWhiteSpace(sourcePath) || new FileInfo(sourcePath).Length > 40_000_000) throw new ArgumentException("Archivo de fuentes requerido, máximo 40 MB.");
            var bytes = await File.ReadAllBytesAsync(sourcePath, ct);
            var input = JsonSerializer.Deserialize<MonthCloseInput>(bytes, MonthCloseCodec.Json) ?? throw new InvalidDataException("Fuentes vacías.");
            var sourceHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var latest = existing.GroupBy(s => s.Period).Select(g => g.MaxBy(s => s.Revision)!).OrderBy(s => s.Period).ToList();
            if (latest.Count != 12) throw new InvalidOperationException("Se requieren los doce cierres existentes de 2025.");
            Directory.CreateDirectory(root);
            var entries = new List<Entry>();
            foreach (var summary in latest)
            {
                var original = await repository.ReadAsync(summary.Id, ct) ?? throw new InvalidDataException("Falta el cierre original.");
                if (original.Summary.Hash != summary.Hash) throw new InvalidDataException("Cambió el cierre durante la lectura.");
                var revised = MonthCloseEngine.ReviseReceivables(original, input, sourceHash, DateTimeOffset.UtcNow);
                var (_, hash) = MonthCloseCodec.Encode(revised); revised.Summary.Hash = hash;
                await File.WriteAllTextAsync(Path.Combine(root, summary.Period + ".json"), JsonSerializer.Serialize(revised, MonthCloseCodec.Json), ct);
                entries.Add(new(summary.Period, summary.Id, summary.Hash, revised.Summary.Id, hash));
                var ar = revised.Reports.Single(r => r.Key == "receivables");
                Console.WriteLine($"{summary.Period}: v{revised.Summary.Revision}, {ar.Rows.Count} filas, {ar.Unresolved} pendientes, {ar.SettlementAssumptions.Count} pagos asumidos, saldo {ar.Total}");
            }
            await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(entries, MonthCloseCodec.Json), ct);
            Console.WriteLine("Plan preparado; no se guardaron cambios en Dataverse.");
            return;
        }

        var plan = JsonSerializer.Deserialize<List<Entry>>(await File.ReadAllTextAsync(planPath, ct), MonthCloseCodec.Json)!;
        if (plan.Count != 12 || plan.Select(e => e.Period).Distinct().Count() != 12 || plan.Any(e => MonthCloseEngine.ParsePeriod(e.Period).Year != 2025))
            throw new InvalidDataException("Plan de revisión inválido.");
        // Validate all parents and local proposals before the first write.
        var pending = new List<MonthCloseSnapshot>();
        foreach (var entry in plan)
        {
            var original = await repository.ReadAsync(entry.PreviousId, ct);
            if (original?.Summary.Hash != entry.PreviousHash) throw new InvalidDataException("El cierre original cambió.");
            var revised = JsonSerializer.Deserialize<MonthCloseSnapshot>(await File.ReadAllTextAsync(Path.Combine(root, entry.Period + ".json"), ct), MonthCloseCodec.Json)!;
            var (_, hash) = MonthCloseCodec.Encode(revised);
            if (hash != entry.Hash || revised.Summary.Id != entry.Id || revised.Summary.Period != entry.Period
                || revised.Summary.Revision != original.Summary.Revision + 1 || MonthCloseCodec.IdFor(entry.Period, revised.Summary.Revision) != entry.Id)
                throw new InvalidDataException("La propuesta no coincide con el plan revisado.");
            var latest = existing.Where(s => s.Period == entry.Period).MaxBy(s => s.Revision)!;
            if (latest.Id == entry.Id && latest.Hash == entry.Hash) continue;
            if (latest.Id != entry.PreviousId || latest.Hash != entry.PreviousHash) throw new InvalidDataException("Hay una revisión posterior; no se sobrescribe.");
            pending.Add(revised);
        }
        foreach (var revised in pending)
        {
            await repository.CreateAsync(revised, ct);
            var readBack = await repository.ReadAsync(revised.Summary.Id, ct);
            if (readBack?.Summary.Hash != revised.Summary.Hash) throw new InvalidDataException("No coincide la revisión guardada.");
        }
        Console.WriteLine($"Revisiones guardadas y verificadas: {pending.Count}");
    }
}
