using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Licenciamiento;

namespace CotizadorInterno.Web.Services;

public sealed class LicenciamientoDashboardConflictException(string message) : InvalidOperationException(message);

internal static class LicenciamientoDashboardContractEditor
{
    internal const int MaxLines = 200;
    internal sealed record Change(string Path, string Field, object Value, string Version, int TargetValue, bool AlreadyUpdated);

    internal static IReadOnlyList<LicenciamientoDashboardLineDto> BuildLines(IEnumerable<LicenciamientoCruceRowDto> rows) =>
        rows.SelectMany(row => row.Trace.CostItems.Select(item => ToLine(item, "cost"))
                .Concat(row.Trace.BillingItems.Select(item => ToLine(item, "billing"))))
            .GroupBy(line => Key(line.Source, line.RecordId), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(line => line.Source == "cost" ? 0 : 1)
            .ThenBy(line => line.Description, StringComparer.OrdinalIgnoreCase)
            .ThenBy(line => line.Reference, StringComparer.OrdinalIgnoreCase).ToList();

    private static LicenciamientoDashboardLineDto ToLine(LicenciamientoCruceTraceItemDto item, string source) => new()
    {
        Source = source,
        RecordId = item.RecordId,
        Description = source == "cost" ? (string.IsNullOrWhiteSpace(item.Producto) ? "Licencia" : item.Producto) : "Factura",
        ClientName = item.Cliente,
        Reference = item.Referencia,
        Date = item.Fecha,
        Amount = item.Valor,
        ContractTypeValue = item.TipoContratoValue,
        ContractTypeLabel = item.TipoContrato
    };

    internal static string Key(string source, string id) => $"{source}:{id}";
    internal static int TargetValue(string source, string contractKey) => (source, contractKey) switch
    {
        ("cost" or "billing", "monthly") => 645250000,
        ("cost", "onetime") => 645250002,
        ("billing", "onetime") => 645250001,
        _ => throw new InvalidOperationException("El tipo de contrato o la fuente no es válido.")
    };

    internal static void ValidateRequest(LicenciamientoDashboardContractChangeRequest request)
    {
        if (request.Year is < 2000 or > 2100 || request.Month is < 1 or > 12
            || string.IsNullOrWhiteSpace(request.ClientKey) || request.ClientKey.Length > 1024
            || request.SourceContractKey is not ("monthly" or "onetime")
            || request.TargetContractKey is not ("monthly" or "onetime")
            || request.SourceContractKey == request.TargetContractKey)
            throw new InvalidOperationException("El cliente, periodo o tipo de contrato no es válido.");
        if (request.Lines is null || request.Lines.Count is < 1 or > MaxLines)
            throw new InvalidOperationException($"Selecciona entre 1 y {MaxLines} filas para cambiar el contrato.");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in request.Lines)
        {
            if (line is null || line.Source is not ("cost" or "billing")
                || !Guid.TryParseExact(line.RecordId, "D", out var id) || id == Guid.Empty
                || !line.ExpectedContractTypeValue.HasValue || !keys.Add(Key(line.Source, line.RecordId)))
                throw new InvalidOperationException("La selección contiene filas inválidas o repetidas. Recarga el detalle.");
        }
    }

    internal static IReadOnlyList<LicenciamientoDashboardLineSelection> SelectLines(
        LicenciamientoDashboardContractChangeRequest request, LicenciamientoDashboardDto dashboard)
    {
        ValidateRequest(request);
        if (dashboard.Year != request.Year || dashboard.Month != request.Month)
            throw Conflict();
        var available = new[] { dashboard.MonthlyCostCard, dashboard.PrepaidCostCard }
            .SelectMany(card => card.Breakdown.Where(client => string.Equals(client.ClientKey, request.ClientKey, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(client => client.Lines)
            .GroupBy(line => Key(line.Source, line.RecordId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var line in request.Lines)
        {
            if (!available.TryGetValue(Key(line.Source, line.RecordId), out var current)
                || (current.ContractTypeValue != line.ExpectedContractTypeValue
                    && current.ContractTypeValue != TargetValue(line.Source, request.TargetContractKey)))
                throw Conflict();
        }
        return request.Lines;
    }

    internal static LicenciamientoDashboardConflictException Conflict() => new(
        "Una fila cambió o ya no pertenece a este cliente y periodo. No se aplicó el cambio. Recarga el detalle.");

    internal static Change Prepare(string path, string field, object payloadValue, int targetValue,
        int? expectedValue, JsonElement record)
    {
        var current = record.TryGetProperty(field, out var value)
            && int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? (int?)number : null;
        if (current != expectedValue && current != targetValue)
            throw Conflict();
        var version = record.TryGetProperty("@odata.etag", out var etag) ? etag.GetString() ?? "" : "";
        if (!Regex.IsMatch(version, "^W/\"[0-9]+\"$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("No se pudo comprobar la versión de una fila. Recarga el detalle.");
        return new(path, field, payloadValue, version, targetValue, current == targetValue);
    }

    internal static async Task<LicenciamientoDashboardContractChangeResult> ApplyAsync(
        LicenciamientoDashboardContractChangeRequest request, LicenciamientoDashboardDto dashboard,
        Func<LicenciamientoDashboardLineSelection, CancellationToken, Task<Change>> prepare,
        Func<IReadOnlyList<Change>, CancellationToken, Task> persist,
        Func<Change, CancellationToken, Task<bool>> verify, CancellationToken ct)
    {
        var lines = SelectLines(request, dashboard);
        var changes = new Change[lines.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, lines.Count), new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = ct },
            async (index, token) => changes[index] = await prepare(lines[index], token));
        var pending = changes.Where(change => !change.AlreadyUpdated).ToArray();
        if (pending.Length > 0)
            await persist(pending, ct);
        await Parallel.ForEachAsync(changes, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = ct },
            async (change, token) =>
            {
                if (!await verify(change, token))
                    throw new InvalidOperationException("El envío terminó, pero no se pudo confirmar el tipo de todas las filas. Recarga para revisar su estado.");
            });
        var label = request.TargetContractKey == "monthly" ? "Monthly" : "Prepaid";
        return new()
        {
            UpdatedCount = pending.Length,
            AlreadyUpdatedCount = changes.Length - pending.Length,
            Message = pending.Length == 0 ? $"Las {changes.Length} filas ya están en {label}."
                : pending.Length == 1 ? $"Se cambió 1 fila a {label}." : $"Se cambiaron {pending.Length} filas a {label}."
        };
    }

    internal static string BuildBatch(IReadOnlyList<Change> changes, string batch, string changeSet)
    {
        const string crlf = "\r\n";
        var body = new StringBuilder().Append("--").Append(batch).Append(crlf)
            .Append("Content-Type: multipart/mixed; boundary=").Append(changeSet).Append(crlf).Append(crlf);
        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];
            body.Append("--").Append(changeSet).Append(crlf)
                .Append("Content-Type: application/http\r\nContent-Transfer-Encoding: binary\r\nContent-ID: ")
                .Append(index + 1).Append(crlf).Append(crlf)
                .Append("PATCH ").Append(change.Path).Append(" HTTP/1.1\r\nIf-Match: ").Append(change.Version)
                .Append("\r\nContent-Type: application/json; type=entry\r\n\r\n")
                .Append(JsonSerializer.Serialize(new Dictionary<string, object?> { [change.Field] = change.Value })).Append(crlf);
        }
        return body.Append("--").Append(changeSet).Append("--\r\n--").Append(batch).Append("--\r\n").ToString();
    }

    internal static void ValidateBatchResponse(bool success, string body, int expectedCount)
    {
        var statuses = Regex.Matches(body, @"HTTP/1\.[01]\s+(\d{3})")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
        if (statuses.Any(status => status is 409 or 412))
            throw Conflict();
        if (!success || statuses.Length != expectedCount || statuses.Any(status => status is < 200 or >= 300))
            throw new InvalidOperationException("No se pudo confirmar la actualización conjunta. Recarga el detalle antes de volver a guardar.");
    }
}
