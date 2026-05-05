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
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public PublicDataExportSettingsStore(IWebHostEnvironment environment)
    {
        _filePath = Path.Combine(environment.ContentRootPath, "App_Data", "public-data-export-settings.json");
    }

    public async Task<PublicDataExportSettings> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(_filePath))
                return new PublicDataExportSettings();

            var json = await File.ReadAllTextAsync(_filePath, ct);
            var settings = JsonSerializer.Deserialize<PublicDataExportSettings>(json, JsonOptions)
                ?? new PublicDataExportSettings();

            settings.ApprovedColumnsByDataset = NormalizeColumns(settings.ApprovedColumnsByDataset);
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
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? ".");
            settings.ApprovedColumnsByDataset = NormalizeColumns(settings.ApprovedColumnsByDataset);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        finally
        {
            _gate.Release();
        }
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
