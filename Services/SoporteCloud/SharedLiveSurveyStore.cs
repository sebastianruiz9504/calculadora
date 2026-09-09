using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace CotizadorInterno.Web.Services.SoporteCloud;

public sealed record SharedStateMutation<T, TResult>(T? State, TResult Result, bool Persist = true)
    where T : class;

public sealed class SharedLiveSurveyStoreUnavailableException : InvalidOperationException
{
    internal SharedLiveSurveyStoreUnavailableException(Exception cause)
        : base("No fue posible sincronizar la encuesta. Intenta de nuevo en unos segundos.", cause)
    {
    }
}

/// <summary>
/// Coordinates live survey state between App Service workers through its built-in
/// shared HOME storage. A per-document exclusive handle serializes read/modify/write;
/// same-directory replacement prevents readers from observing partial JSON.
/// </summary>
public sealed class SharedLiveSurveyStore
{
    private const int MaximumCacheEntries = 256;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Lazy<string> _directory;
    private readonly TimeSpan _lockTimeout;
    private readonly TimeSpan _readCacheDuration;
    private readonly ConcurrentDictionary<string, CachedState> _cache = new(StringComparer.Ordinal);
    private bool _directoryInitialized;

    public SharedLiveSurveyStore(IWebHostEnvironment environment)
        : this(() => ResolveDirectory(environment.ContentRootPath, Environment.GetEnvironmentVariable),
            TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(150))
    {
    }

    internal SharedLiveSurveyStore(string directory, TimeSpan? lockTimeout = null, TimeSpan? readCacheDuration = null)
        : this(() => Path.GetFullPath(directory), lockTimeout ?? TimeSpan.FromSeconds(3),
            readCacheDuration ?? TimeSpan.FromMilliseconds(150))
    {
    }

    private SharedLiveSurveyStore(Func<string> directory, TimeSpan lockTimeout, TimeSpan readCacheDuration)
    {
        if (lockTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lockTimeout));
        if (readCacheDuration < TimeSpan.Zero || readCacheDuration > TimeSpan.FromMilliseconds(200))
            throw new ArgumentOutOfRangeException(nameof(readCacheDuration));

        // Resolve lazily so an unavailable survey store does not stop unrelated modules.
        _directory = new Lazy<string>(directory);
        _lockTimeout = lockTimeout;
        _readCacheDuration = readCacheDuration;
    }

    public async Task<T?> ReadAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedKey = ValidateKey(key);
        var directory = GetDirectory();
        if (_cache.TryGetValue(normalizedKey, out var cached)
            && Stopwatch.GetElapsedTime(cached.ReadStartedAt) < _readCacheDuration)
        {
            // Never expose the cached object: consumers may mutate a returned snapshot.
            return Deserialize<T>(cached.Json);
        }

        // Windows/SMB replacement may briefly hide the destination name from a new
        // opener. Share the writer lease on a cache miss so that such a window can
        // never be mistaken for an absent survey and trigger database rehydration.
        await using var lease = await AcquireLockAsync(
            Path.Combine(directory, normalizedKey + ".lock"), cancellationToken);
        if (_cache.TryGetValue(normalizedKey, out cached)
            && Stopwatch.GetElapsedTime(cached.ReadStartedAt) < _readCacheDuration)
            return Deserialize<T>(cached.Json);

        var startedAt = Stopwatch.GetTimestamp();
        var json = await ReadFileAsync(Path.Combine(directory, normalizedKey + ".json"), cancellationToken);
        if (json is null)
        {
            _cache.TryRemove(normalizedKey, out _);
            return null;
        }

        var state = Deserialize<T>(json);
        Cache(normalizedKey, json, startedAt);
        return state;
    }

    public async Task<TResult> MutateAsync<T, TResult>(
        string key,
        Func<T?, SharedStateMutation<T, TResult>> mutation,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(mutation);
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedKey = ValidateKey(key);
        var directory = GetDirectory();
        var path = Path.Combine(directory, normalizedKey + ".json");

        await using var lease = await AcquireLockAsync(
            Path.Combine(directory, normalizedKey + ".lock"), cancellationToken);

        // The callback is deliberately synchronous: perform remote business writes outside
        // this lease, then commit only the small local transition under the lock.
        var json = await ReadFileAsync(path, cancellationToken);
        var state = json is null ? null : Deserialize<T>(json);
        var change = mutation(state);
        cancellationToken.ThrowIfCancellationRequested();
        _cache.TryRemove(normalizedKey, out _);
        if (!change.Persist) return change.Result;
        if (change.State is null)
            throw new InvalidOperationException("El estado persistido de la encuesta no puede ser nulo.");

        byte[] updatedJson;
        try
        {
            updatedJson = JsonSerializer.SerializeToUtf8Bytes(change.State, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new SharedLiveSurveyStoreUnavailableException(ex);
        }

        await WriteAtomicAsync(path, updatedJson, cancellationToken);
        Cache(normalizedKey, updatedJson, Stopwatch.GetTimestamp());
        return change.Result;
    }

    internal static string ResolveDirectory(string contentRoot, Func<string, string?> getEnvironmentVariable)
    {
        var isAppService = !string.IsNullOrWhiteSpace(getEnvironmentVariable("WEBSITE_INSTANCE_ID"))
            || !string.IsNullOrWhiteSpace(getEnvironmentVariable("WEBSITE_SITE_NAME"));
        if (!isAppService) return Path.GetFullPath(Path.Combine(contentRoot, "App_Data", "soporte-cloud-live"));

        var cacheReady = getEnvironmentVariable("WEBSITE_LOCALCACHE_READY");
        if (string.Equals(getEnvironmentVariable("WEBSITE_LOCAL_CACHE_OPTION"), "Always", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cacheReady, "true", StringComparison.OrdinalIgnoreCase) || cacheReady == "1")
        {
            throw new InvalidOperationException("Live surveys require shared App Service storage; Local Cache is enabled.");
        }

        var azureHome = getEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(azureHome) || !Path.IsPathFullyQualified(azureHome))
            throw new InvalidOperationException("The shared App Service HOME storage is unavailable.");

        return Path.GetFullPath(Path.Combine(azureHome, "data", "CotizadorInterno", "soporte-cloud-live", "v1"));
    }

    private string GetDirectory()
    {
        try
        {
            var directory = _directory.Value;
            if (!Volatile.Read(ref _directoryInitialized))
            {
                Directory.CreateDirectory(directory);
                Volatile.Write(ref _directoryInitialized, true);
            }
            return directory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            throw new SharedLiveSurveyStoreUnavailableException(ex);
        }
    }

    private static string ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || !char.IsAsciiLetterOrDigit(key[0])
            || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
        {
            throw new ArgumentException("La clave de la encuesta no es válida.", nameof(key));
        }

        return key.ToLowerInvariant();
    }

    private async Task<FileStream> AcquireLockAsync(string path, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Never delete lock files: unlinking a held lock can let another worker
                // create a second lock for the same document.
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    1, FileOptions.Asynchronous);
            }
            catch (IOException ex) when (IsLockContention(ex))
            {
                if (Stopwatch.GetElapsedTime(startedAt) >= _lockTimeout)
                    throw new SharedLiveSurveyStoreUnavailableException(new TimeoutException("Shared survey lock timed out.", ex));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SharedLiveSurveyStoreUnavailableException(ex);
            }

            await Task.Delay(Random.Shared.Next(15, 31), cancellationToken);
        }
    }

    private static bool IsLockContention(IOException exception)
    {
        // Win32 sharing/lock violations; EAGAIN is used by .NET file sharing on Unix.
        var code = exception.HResult & 0xffff;
        return code is 32 or 33 or 11;
    }

    private static async Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var bytes = new MemoryStream();
            await stream.CopyToAsync(bytes, cancellationToken);
            return bytes.ToArray();
        }
        catch (FileNotFoundException)
        {
            // Only a genuinely absent document permits recovery from Dataverse.
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SharedLiveSurveyStoreUnavailableException(ex);
        }
    }

    private static T Deserialize<T>(byte[] json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new JsonException("The stored survey state is null.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new SharedLiveSurveyStoreUnavailableException(ex);
        }
    }

    private static async Task WriteAtomicAsync(string path, byte[] json, CancellationToken cancellationToken)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 16 * 1024, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // ReplaceFile preserves a readable name while readers hold the old
                // version open. MoveFileEx(overwrite) can leave a delete-pending name
                // on Windows and fail a following replacement with AccessDenied.
                File.Replace(temporaryPath, path, destinationBackupFileName: null);
            }
            catch (FileNotFoundException)
            {
                // First commit only; the document's exclusive lock excludes another
                // creator, and all later commits use the atomic replacement above.
                File.Move(temporaryPath, path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SharedLiveSurveyStoreUnavailableException(ex);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void Cache(string key, byte[] json, long startedAt)
    {
        if (_readCacheDuration == TimeSpan.Zero) return;
        if (_cache.Count >= MaximumCacheEntries)
        {
            foreach (var entry in _cache.Take(MaximumCacheEntries / 4))
                _cache.TryRemove(entry.Key, out _);
        }
        _cache[key] = new CachedState(json, startedAt);
    }

    private sealed record CachedState(byte[] Json, long ReadStartedAt);
}
