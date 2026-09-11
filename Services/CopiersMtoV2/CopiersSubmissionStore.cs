using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using Microsoft.AspNetCore.DataProtection;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

// Durable receipt on App Service's shared HOME volume (never wwwroot/local temp).
// Atomic replacement + cross-process file leases also cover multiple workers.
public sealed class CopiersSubmissionStore
{
    private readonly string _directory;
    private readonly IDataProtector _protector;
    public CopiersSubmissionStore(IWebHostEnvironment environment, IDataProtectionProvider protection)
        : this(ResolveDirectory(environment.ContentRootPath, Environment.GetEnvironmentVariable), protection) { }
    internal CopiersSubmissionStore(string directory, IDataProtectionProvider protection)
    {
        _directory = directory;
        _protector = protection.CreateProtector("CopiersMtoV2.DurableSubmission.v1");
    }
    internal static string ResolveDirectory(string root, Func<string, string?> env)
    {
        var appService = !string.IsNullOrEmpty(env("WEBSITE_INSTANCE_ID")) || !string.IsNullOrEmpty(env("WEBSITE_SITE_NAME"));
        if (!appService) return Path.Combine(root, "App_Data", "copiers-submissions-v1");
        if (string.Equals(env("WEBSITE_LOCAL_CACHE_OPTION"), "Always", StringComparison.OrdinalIgnoreCase)
            || env("WEBSITE_LOCALCACHE_READY") is "true" or "1" || !Path.IsPathFullyQualified(env("HOME") ?? ""))
            throw new InvalidOperationException("La recepción de Copiers requiere almacenamiento compartido persistente.");
        return Path.Combine(env("HOME")!, "data", "CotizadorInterno", "copiers-submissions-v1");
    }
    public static string Owner(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) throw new UnauthorizedAccessException();
        var oid = user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value ?? user.FindFirst("oid")?.Value;
        var tid = user.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value ?? user.FindFirst("tid")?.Value;
        if (!Guid.TryParse(oid, out var o) || !Guid.TryParse(tid, out var t)) throw new UnauthorizedAccessException();
        return $"{t:D}/{o:D}";
    }
    internal static string Key(string owner, string submissionKey)
    {
        if (string.IsNullOrWhiteSpace(submissionKey) || submissionKey.Length > 128) throw new ArgumentException("Clave de envío inválida.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner + "/" + submissionKey))).ToLowerInvariant();
    }
    public async Task<CopiersSubmission> ReceiveAsync(string owner, CopiersMaintenanceV2DraftRequestDto draft,
        CopiersMaintenanceV2FinalizeMultipartRequestDto request, CopiersMaintenanceV2ActorContext actor, CancellationToken ct)
    {
        var id = Key(owner, request.SubmissionKey);
        // Serialize scalar fields only; IFormFile is a disposed request resource.
        var node = JsonSerializer.SerializeToNode(request)!.AsObject();
        node.Remove(nameof(request.Signature)); node.Remove(nameof(request.Attachments));
        node[nameof(request.RecordId)] = ""; node[nameof(request.ExpectedVersion)] = "";
        var files = new List<CopiersSubmissionFile>();
        foreach (var file in (request.Signature is null ? Enumerable.Empty<IFormFile>() : new[] { request.Signature }).Concat(request.Attachments))
        {
            if (file.Length <= 0 || file.Length > 8 * 1024 * 1024 || files.Sum(x => x.Content.LongLength) + file.Length > 22 * 1024 * 1024)
                throw new CopiersMaintenanceV2ValidationException("file_size_invalid", "Los adjuntos exceden el tamaño permitido.");
            using var stream = new MemoryStream(); await file.CopyToAsync(stream, ct);
            files.Add(new() { Field = ReferenceEquals(file, request.Signature) ? "Signature" : "Attachments", Name = Path.GetFileName(file.FileName), Type = file.ContentType, Content = stream.ToArray() });
        }
        if (files.Count > 9 || !files.Any(x => x.Field == "Signature"))
            throw new CopiersMaintenanceV2ValidationException("signature_required", "Se requiere la firma y máximo ocho evidencias.");
        var payload = new CopiersSubmissionPayload { RequestJson = node.ToJsonString(), Draft = draft, Actor = actor, Files = files };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        Directory.CreateDirectory(_directory);
        await using var ownerLock = await LockAsync(Key(owner, "owner"), ct);
        await using var lease = await LockAsync(id, ct);
        var old = await ReadAsync(id, ct);
        if (old is not null)
        {
            if (old.Fingerprint != hash) throw new CopiersMaintenanceV2ConcurrencyException("Esta clave ya recibió otro contenido firmado. Recupera el envío original.");
            return old;
        }
        // Bound admitted pending work per owner without dropping any existing receipt.
        var pending = 0;
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            var entry = await ReadAsync(Path.GetFileNameWithoutExtension(path), ct);
            if (entry?.Owner == owner && entry.Result is null) pending++;
        }
        if (pending >= 10) throw new CopiersMaintenanceV2ValidationException("pending_limit", "Hay diez envíos pendientes. Revisa su estado antes de crear otro.");
        var item = new CopiersSubmission { Id = id, Owner = owner, SubmissionKey = request.SubmissionKey,
            Fingerprint = hash, ReceivedAtUtc = DateTimeOffset.UtcNow, Payload = payload };
        await WriteAsync(item, ct);
        return item;
    }
    public async Task<CopiersSubmission?> FindAsync(string owner, string key, CancellationToken ct)
    {
        var item = await ReadAsync(Key(owner, key), ct);
        return item?.Owner == owner ? item : null;
    }
    internal IEnumerable<string> PendingIds() => Directory.Exists(_directory)
        ? Directory.EnumerateFiles(_directory, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>() : [];
    internal async Task<FileStream> LockAsync(string id, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(_directory, id + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 20) { await Task.Delay(100, ct); }
        }
    }
    internal async Task<CopiersSubmission?> ReadAsync(string id, CancellationToken ct)
    {
        var path = Path.Combine(_directory, id + ".json");
        try
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var bytes = new MemoryStream(); await input.CopyToAsync(bytes, ct);
            return JsonSerializer.Deserialize<CopiersSubmission>(_protector.Unprotect(bytes.ToArray()));
        }
        catch (FileNotFoundException) { return null; }
        // A fresh deployment has no receipt directory until its first accepted
        // upload. The status preflight must report "not received" in that case.
        // Do not swallow other IO/protection failures: they must block replay.
        catch (DirectoryNotFoundException) { return null; }
    }
    internal async Task WriteAsync(CopiersSubmission item, CancellationToken ct)
    {
        var path = Path.Combine(_directory, item.Id + ".json");
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(item));
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            { await output.WriteAsync(bytes, ct); output.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static object Status(CopiersSubmission item) => new { received = true, submissionKey = item.SubmissionKey,
        status = item.Result is not null ? "completed" : item.NeedsReview ? "needs_review" : "received",
        receivedAtUtc = item.ReceivedAtUtc, result = item.Result,
        message = item.Result is not null ? "Registro creado; consultando el correo." : item.NeedsReview ? item.Error : "Recibido de forma segura. El servidor está preparando el registro y el PDF." };
}
public sealed class CopiersSubmission
{
    public string Id { get; set; } = "";
    public string Owner { get; set; } = "";
    public string SubmissionKey { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public DateTimeOffset NextAttemptUtc { get; set; }
    public int Attempts { get; set; }
    public bool NeedsReview { get; set; }
    public string Error { get; set; } = "";
    public CopiersSubmissionPayload? Payload { get; set; }
    public CopiersMaintenanceV2FinalizeResultDto? Result { get; set; }
}
public sealed class CopiersSubmissionPayload
{
    public string RequestJson { get; set; } = "";
    public CopiersMaintenanceV2DraftRequestDto Draft { get; set; } = new();
    public CopiersMaintenanceV2ActorContext Actor { get; set; } = new();
    public List<CopiersSubmissionFile> Files { get; set; } = [];
}
public sealed class CopiersSubmissionFile
{
    public string Field { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public byte[] Content { get; set; } = [];
}
