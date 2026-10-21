using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Licenciamiento;

namespace CotizadorInterno.Web.Services;

internal static class LicensingClientMonthRelation
{
    // Use the original identity, before any manual relation changes the displayed client.
    internal static string GroupKey(string clientId, string accountId, string name, string recordId) =>
        Guid.TryParse(clientId, out var client) ? $"client:{client:D}" :
        Guid.TryParse(accountId, out var account) ? $"account:{account:D}" :
        !string.IsNullOrWhiteSpace(name) ? "name:" + name.Trim().ToUpperInvariant() : $"record:{recordId}";

    internal static IReadOnlyList<LicenciamientoCruceTraceItemDto> SelectLines(
        string anchorId, IReadOnlyList<LicenciamientoCruceRelationLineDto> expected,
        IReadOnlyList<LicenciamientoCruceTraceItemDto> available)
    {
        var anchor = available.SingleOrDefault(line => line.RecordId == anchorId)
            ?? throw new InvalidOperationException("El consumo seleccionado ya no esta disponible. Actualiza el cruce.");
        var lines = available.Where(line => line.RelationGroupKey == anchor.RelationGroupKey && line.Mes == anchor.Mes).ToArray();
        if (string.IsNullOrWhiteSpace(anchor.RelationGroupKey) || lines.Length is 0 or > 1000
            || expected.Count != lines.Length || expected.Select(line => line.RecordId).Distinct().Count() != lines.Length)
            throw new InvalidOperationException("Los consumos del cliente cambiaron. Actualiza para revisar todas las lineas del mes.");
        foreach (var line in lines)
        {
            var snapshot = expected.SingleOrDefault(item => item.RecordId == line.RecordId);
            if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.ExpectedCostVersion)
                || snapshot.ExpectedCostVersion != line.SourceVersion
                || snapshot.ExpectedMappingId != line.RecordRelationId
                || snapshot.ExpectedMappingVersion != line.RecordRelationVersion)
                throw new InvalidOperationException("Un consumo o su relacion cambio. Actualiza el cruce antes de guardar el conjunto.");
        }
        return lines;
    }

    internal sealed record Change(string Path, string Version, Dictionary<string, object?> Payload);

    internal static string BuildBatch(IReadOnlyList<Change> changes, string batch, string changeSet)
    {
        var body = new StringBuilder($"--{batch}\r\nContent-Type: multipart/mixed; boundary={changeSet}\r\n\r\n");
        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];
            body.Append($"--{changeSet}\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\nContent-ID: {index + 1}\r\n\r\n")
                .Append($"PATCH {change.Path} HTTP/1.1\r\n")
                .Append(string.IsNullOrEmpty(change.Version) ? "If-None-Match: *" : $"If-Match: {change.Version}")
                .Append("\r\nContent-Type: application/json; type=entry\r\n\r\n")
                .Append(JsonSerializer.Serialize(change.Payload)).Append("\r\n");
        }
        return body.Append($"--{changeSet}--\r\n--{batch}--\r\n").ToString();
    }
}
