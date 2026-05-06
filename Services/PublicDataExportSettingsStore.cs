using System.Security.Cryptography;
using System.Text.Json;
using CotizadorInterno.Web.Models.PublicDataExport;

namespace CotizadorInterno.Web.Services;

public interface IPublicDataExportSettingsStore
{
    Task<PublicDataExportSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(PublicDataExportSettings settings, CancellationToken ct = default);
}

public sealed class PublicDataExportSettingsStore : IPublicDataExportSettingsStore
{
    private const string SettingsFileName = "public-data-export-settings.json";

    private readonly string _filePath;
    private readonly string _legacyFilePath;
    private readonly ILogger<PublicDataExportSettingsStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public PublicDataExportSettingsStore(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<PublicDataExportSettingsStore> logger)
    {
        _legacyFilePath = Path.Combine(environment.ContentRootPath, "App_Data", SettingsFileName);
        _filePath = ResolveSettingsFilePath(configuration, environment.ContentRootPath, _legacyFilePath);
        _logger = logger;
    }

    public async Task<PublicDataExportSettings> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var existingPath = ResolveExistingSettingsPath();
            if (existingPath is null)
                return new PublicDataExportSettings();

            var settings = await ReadSettingsAsync(existingPath, ct);
            if (!PathsEqual(existingPath, _filePath))
            {
                await WriteSettingsAsync(_filePath, settings, ct);
                _logger.LogInformation(
                    "Configuracion de descargas publicas migrada desde {LegacyPath} hacia {SettingsPath}.",
                    existingPath,
                    _filePath);
            }

            return settings;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(PublicDataExportSettings settings, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            settings.ApprovedColumnsByDataset = NormalizeColumns(settings.ApprovedColumnsByDataset);
            await WriteSettingsAsync(_filePath, settings, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string? ResolveExistingSettingsPath()
    {
        if (File.Exists(_filePath))
            return _filePath;

        if (!PathsEqual(_filePath, _legacyFilePath) && File.Exists(_legacyFilePath))
            return _legacyFilePath;

        return null;
    }

    private static async Task<PublicDataExportSettings> ReadSettingsAsync(string filePath, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(filePath, ct);
        var settings = JsonSerializer.Deserialize<PublicDataExportSettings>(json, JsonOptions)
            ?? new PublicDataExportSettings();

        settings.ApprovedColumnsByDataset = NormalizeColumns(settings.ApprovedColumnsByDataset);
        return settings;
    }

    private static async Task WriteSettingsAsync(
        string filePath,
        PublicDataExportSettings settings,
        CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(filePath) ?? ".";
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
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

    private static string ResolveSettingsFilePath(
        IConfiguration configuration,
        string contentRootPath,
        string legacyFilePath)
    {
        var configuredPath = FirstNonEmpty(
            configuration["PublicDataExport:SettingsFilePath"],
            Environment.GetEnvironmentVariable("PUBLIC_DATA_EXPORT_SETTINGS_PATH"));
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return ResolveFullPath(configuredPath, contentRootPath);

        var azureHome = Environment.GetEnvironmentVariable("HOME");
        if (IsAzureAppService() && !string.IsNullOrWhiteSpace(azureHome))
        {
            return Path.Combine(
                azureHome.Trim(),
                "data",
                "CotizadorInterno",
                SettingsFileName);
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

    private static Dictionary<string, List<string>> NormalizeColumns(Dictionary<string, List<string>>? source)
    {
        var normalized = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
            return normalized;

        foreach (var item in source)
        {
            if (string.IsNullOrWhiteSpace(item.Key))
                continue;

            normalized[item.Key.Trim()] = (item.Value ?? new List<string>())
                .Where(static column => !string.IsNullOrWhiteSpace(column))
                .Select(static column => column.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return normalized;
    }
}

public static class PublicDataExportPasswordHasher
{
    private const int DefaultIterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static void SetPassword(PublicDataExportSettings settings, string password)
    {
        if (settings is null)
            throw new ArgumentNullException(nameof(settings));

        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Debes escribir una contrasena para el portal publico.");

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password.Trim(),
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            HashSize);

        settings.PasswordSalt = Convert.ToBase64String(salt);
        settings.PasswordHash = Convert.ToBase64String(hash);
        settings.PasswordIterations = DefaultIterations;
        settings.PasswordUpdatedUtc = DateTimeOffset.UtcNow;
    }

    public static bool Verify(PublicDataExportSettings settings, string password)
    {
        if (settings is null || !settings.HasPassword || string.IsNullOrWhiteSpace(password))
            return false;

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(settings.PasswordSalt);
            expectedHash = Convert.FromBase64String(settings.PasswordHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password.Trim(),
            salt,
            settings.PasswordIterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
