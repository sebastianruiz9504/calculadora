using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace CotizadorInterno.Web.Services;

/// <summary>Preserves complete upgrade snapshots inside the existing 4,000-character score field.</summary>
internal static class ScoreSnapshotStorage
{
    private const string EncodingName = "gzip-base64-v1";
    private const int MaxExpandedBytes = 4 * 1024 * 1024;

    internal static string Pack(string json, int version)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(json));
        var packed = JsonSerializer.Serialize(new { Version = version, Encoding = EncodingName, Data = Convert.ToBase64String(output.ToArray()) });
        if (packed.Length > 4000)
            throw new InvalidOperationException("El detalle de los upgrades supera la capacidad del registro de Puntajes. Divide el negocio para conservar todas sus líneas.");
        return packed;
    }

    internal static string? Expand(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.Contains(EncodingName, StringComparison.Ordinal)) return json;
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Encoding", out var encoding) || encoding.GetString() != EncodingName) return json;
        using var input = new MemoryStream(Convert.FromBase64String(doc.RootElement.GetProperty("Data").GetString()!));
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = gzip.Read(buffer)) > 0)
        {
            if (output.Length + count > MaxExpandedBytes)
                throw new InvalidOperationException("El detalle guardado de Puntajes excede el tamaño permitido.");
            output.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
