using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CotizadorInterno.Web.Models.Nomina;

namespace CotizadorInterno.Web.Services;

public interface INominaDraftStore
{
    Task<NominaDraftDto?> LoadLatestAsync(CancellationToken ct = default);
    Task<NominaDraftDto?> LoadAsync(string periodKey, CancellationToken ct = default);
    Task<NominaDraftDto> SaveAsync(NominaDraftDto draft, CancellationToken ct = default);
    Task DeleteAsync(string periodKey, CancellationToken ct = default);
}

public sealed class NominaDraftStore : INominaDraftStore
{
    private const string DraftsFileName = "nomina-preliquidacion-drafts.json";
    private const int MaxDraftsToKeep = 24;

    private readonly string _filePath;
    private readonly string _legacyFilePath;
    private readonly ILogger<NominaDraftStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public NominaDraftStore(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<NominaDraftStore> logger)
    {
        _legacyFilePath = Path.Combine(environment.ContentRootPath, "App_Data", DraftsFileName);
        _filePath = ResolveDraftsFilePath(configuration, environment.ContentRootPath, _legacyFilePath);
        _logger = logger;
    }

    public async Task<NominaDraftDto?> LoadLatestAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var file = await ReadDraftsFileAsync(ResolveExistingDraftsPath(), ct);
            return file.Drafts
                .Where(IsValidStoredDraft)
                .OrderByDescending(GetSavedAt)
                .FirstOrDefault();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NominaDraftDto?> LoadAsync(string periodKey, CancellationToken ct = default)
    {
        var normalizedPeriodKey = NormalizePeriodKey(periodKey);
        if (string.IsNullOrWhiteSpace(normalizedPeriodKey))
            return null;

        await _gate.WaitAsync(ct);
        try
        {
            var file = await ReadDraftsFileAsync(ResolveExistingDraftsPath(), ct);
            return file.Drafts
                .Where(IsValidStoredDraft)
                .Where(draft => string.Equals(draft.PeriodKey, normalizedPeriodKey, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(GetSavedAt)
                .FirstOrDefault();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NominaDraftDto> SaveAsync(NominaDraftDto draft, CancellationToken ct = default)
    {
        var normalizedDraft = NormalizeDraft(draft);

        await _gate.WaitAsync(ct);
        try
        {
            var existingPath = ResolveExistingDraftsPath();
            var file = await ReadDraftsFileAsync(existingPath, ct);
            file.Drafts = file.Drafts
                .Where(IsValidStoredDraft)
                .Where(item => !string.Equals(item.PeriodKey, normalizedDraft.PeriodKey, StringComparison.OrdinalIgnoreCase))
                .Append(normalizedDraft)
                .OrderByDescending(GetSavedAt)
                .Take(MaxDraftsToKeep)
                .ToList();

            await WriteDraftsFileAsync(_filePath, file, ct);
            if (existingPath is not null && !PathsEqual(existingPath, _filePath))
            {
                _logger.LogInformation(
                    "Borradores de nomina migrados desde {LegacyPath} hacia {DraftsPath}.",
                    existingPath,
                    _filePath);
            }

            return normalizedDraft;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string periodKey, CancellationToken ct = default)
    {
        var normalizedPeriodKey = NormalizePeriodKey(periodKey);
        if (string.IsNullOrWhiteSpace(normalizedPeriodKey))
            return;

        await _gate.WaitAsync(ct);
        try
        {
            var existingPath = ResolveExistingDraftsPath();
            var file = await ReadDraftsFileAsync(existingPath, ct);
            var originalCount = file.Drafts.Count;
            file.Drafts = file.Drafts
                .Where(IsValidStoredDraft)
                .Where(item => !string.Equals(item.PeriodKey, normalizedPeriodKey, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(GetSavedAt)
                .Take(MaxDraftsToKeep)
                .ToList();

            if (file.Drafts.Count != originalCount || existingPath is not null)
                await WriteDraftsFileAsync(_filePath, file, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string? ResolveExistingDraftsPath()
    {
        if (File.Exists(_filePath))
            return _filePath;

        if (!PathsEqual(_filePath, _legacyFilePath) && File.Exists(_legacyFilePath))
            return _legacyFilePath;

        return null;
    }

    private static async Task<NominaDraftStoreFile> ReadDraftsFileAsync(string? filePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new NominaDraftStoreFile();

        var json = await File.ReadAllTextAsync(filePath, ct);
        return JsonSerializer.Deserialize<NominaDraftStoreFile>(json, JsonOptions)
            ?? new NominaDraftStoreFile();
    }

    private static async Task WriteDraftsFileAsync(string filePath, NominaDraftStoreFile file, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(filePath) ?? ".";
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(file, JsonOptions);
        var tempPath = Path.Combine(directory, $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(tempPath, json, ct);
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    private static NominaDraftDto NormalizeDraft(NominaDraftDto draft)
    {
        if (draft is null)
            throw new ArgumentNullException(nameof(draft));

        var periodKey = NormalizePeriodKey(draft.PeriodKey);
        if (string.IsNullOrWhiteSpace(periodKey))
            throw new InvalidOperationException("El borrador no tiene periodo de nomina.");

        if (draft.Rows is not JsonArray rows || rows.Count == 0)
            throw new InvalidOperationException("El borrador no tiene filas de preliquidacion.");

        var savedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        return new NominaDraftDto
        {
            Version = draft.Version > 0 ? draft.Version : 1,
            SavedAt = savedAt,
            SavedByEmail = (draft.SavedByEmail ?? "").Trim(),
            SavedByName = (draft.SavedByName ?? "").Trim(),
            PeriodKey = periodKey,
            PaymentDateValue = (draft.PaymentDateValue ?? "").Trim(),
            PeriodLabel = (draft.PeriodLabel ?? "").Trim(),
            PaymentDateDisplay = (draft.PaymentDateDisplay ?? "").Trim(),
            Rows = rows.DeepClone(),
            Logs = draft.Logs is JsonArray logs ? logs.DeepClone() : new JsonArray()
        };
    }

    private static bool IsValidStoredDraft(NominaDraftDto? draft)
    {
        return draft is not null
            && !string.IsNullOrWhiteSpace(draft.PeriodKey)
            && draft.Rows is JsonArray rows
            && rows.Count > 0;
    }

    private static DateTimeOffset GetSavedAt(NominaDraftDto draft)
    {
        return DateTimeOffset.TryParse(
            draft.SavedAt,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var savedAt)
            ? savedAt
            : DateTimeOffset.MinValue;
    }

    private static string NormalizePeriodKey(string? periodKey)
    {
        return (periodKey ?? "").Trim();
    }

    private static string ResolveDraftsFilePath(
        IConfiguration configuration,
        string contentRootPath,
        string legacyFilePath)
    {
        var configuredPath = FirstNonEmpty(
            configuration["Nomina:DraftsFilePath"],
            Environment.GetEnvironmentVariable("NOMINA_DRAFTS_PATH"));
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return ResolveFullPath(configuredPath, contentRootPath);

        var azureHome = Environment.GetEnvironmentVariable("HOME");
        if (IsAzureAppService() && !string.IsNullOrWhiteSpace(azureHome))
        {
            return Path.Combine(
                azureHome.Trim(),
                "data",
                "CotizadorInterno",
                DraftsFileName);
        }

        return legacyFilePath;
    }

    private static string ResolveFullPath(string configuredPath, string contentRootPath)
    {
        var expandedPath = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
        return Path.IsPathRooted(expandedPath)
            ? Path.GetFullPath(expandedPath)
            : Path.GetFullPath(Path.Combine(contentRootPath, expandedPath));
    }

    private static bool IsAzureAppService()
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values
            .Select(static value => value?.Trim() ?? "")
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
            ?? "";
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch
        {
        }
    }

    private sealed class NominaDraftStoreFile
    {
        public List<NominaDraftDto> Drafts { get; set; } = new();
    }
}
