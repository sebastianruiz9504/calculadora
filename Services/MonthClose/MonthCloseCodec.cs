using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services.MonthClose;

public static class MonthCloseCodec
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static Guid IdFor(string period,int revision) => new(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"DigitalTech.MonthClose/v1/{period}/{revision}"))[..16]);
    public static (string Body,string Hash) Encode(MonthCloseSnapshot snapshot)
    {
        snapshot.Summary.Hash="";
        var bytes=JsonSerializer.SerializeToUtf8Bytes(snapshot,Json);
        using var stream=new MemoryStream();
        using(var gzip=new GZipStream(stream,CompressionLevel.SmallestSize,true))gzip.Write(bytes);
        var body=Convert.ToBase64String(stream.ToArray());
        if(body.Length>1000000)throw new InvalidOperationException("El cierre supera el tamaño permitido. No se guardó un cierre truncado.");
        return(body,Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
    public static MonthCloseSnapshot Decode(string body,string hash)
    {
        using var stream=new MemoryStream(Convert.FromBase64String(body));
        using var gzip=new GZipStream(stream,CompressionMode.Decompress);
        using var buffer=new MemoryStream();
        var block=new byte[16384];int count;
        while((count=gzip.Read(block))>0){buffer.Write(block,0,count);if(buffer.Length>40_000_000)throw new InvalidDataException("Cierre demasiado grande.");}
        var bytes=buffer.ToArray();
        if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("La integridad del cierre no coincide.");
        var result=JsonSerializer.Deserialize<MonthCloseSnapshot>(bytes,Json)??throw new InvalidDataException("Cierre vacío.");
        if(result.SchemaVersion!=1)throw new InvalidDataException("Versión de cierre no compatible.");
        result.Summary.Hash=hash;return result;
    }
}
