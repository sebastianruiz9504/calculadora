using System.Security.Claims;
using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.PublicDataExport;

public static class PublicDataExportAuthorization
{
    public const string AdminEmail = "sruiz@digitaltechcolombia.com";

    public static bool IsAdmin(CurrentUserInfo? currentUser, ClaimsPrincipal? principal = null)
    {
        return EnumerateCandidateEmails(currentUser, principal)
            .Any(email => EmailMatches(email, AdminEmail));
    }

    private static IEnumerable<string?> EnumerateCandidateEmails(CurrentUserInfo? currentUser, ClaimsPrincipal? principal)
    {
        if (currentUser is not null)
        {
            yield return currentUser.Email;
            yield return currentUser.EmployeeUserEmail;
        }

        if (principal is null)
            yield break;

        yield return principal.FindFirstValue("preferred_username");
        yield return principal.FindFirstValue(ClaimTypes.Upn);
        yield return principal.FindFirstValue(ClaimTypes.Email);
        yield return principal.Identity?.Name;
    }

    private static bool EmailMatches(string? actualEmail, string expectedEmail)
    {
        return string.Equals(
            NormalizeEmail(actualEmail),
            NormalizeEmail(expectedEmail),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEmail(string? email) =>
        (email ?? "").Trim().ToLowerInvariant();
}

public static class PublicDataExportDatasetKeys
{
    public const string Expenses = "expenses";
    public const string Billing = "billing";
}

public sealed class PublicDataExportColumnDefinition
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string ValueField { get; init; } = "";
    public IReadOnlyList<string> SelectFields { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FallbackFields { get; init; } = Array.Empty<string>();
    public string ValueType { get; init; } = "text";
    public bool PreferFormattedValue { get; init; }
    public bool DefaultSelected { get; init; } = true;
}

public sealed class PublicDataExportDatasetDefinition
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string Description { get; init; } = "";
    public string EntitySetName { get; init; } = "";
    public string EntityLogicalName { get; init; } = "";
    public string PrimaryIdField { get; init; } = "";
    public string PrimaryNameField { get; init; } = "";
    public string OrderBy { get; init; } = "";
    public IReadOnlyList<PublicDataExportColumnDefinition> Columns { get; init; } = Array.Empty<PublicDataExportColumnDefinition>();

    public PublicDataExportColumnDefinition? FindColumn(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        return Columns.FirstOrDefault(column =>
            string.Equals(column.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class PublicDataExportCatalogDto
{
    public IReadOnlyList<PublicDataExportDatasetDefinition> Datasets { get; init; } = Array.Empty<PublicDataExportDatasetDefinition>();

    public PublicDataExportDatasetDefinition? FindDataset(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        return Datasets.FirstOrDefault(dataset =>
            string.Equals(dataset.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class PublicDataExportSettings
{
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public int PasswordIterations { get; set; }
    public DateTimeOffset? PasswordUpdatedUtc { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset? UpdatedUtc { get; set; }
    public Dictionary<string, List<string>> ApprovedColumnsByDataset { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasPassword =>
        !string.IsNullOrWhiteSpace(PasswordHash)
        && !string.IsNullOrWhiteSpace(PasswordSalt)
        && PasswordIterations > 0;

    public IReadOnlyList<string> GetApprovedColumns(string datasetKey)
    {
        if (string.IsNullOrWhiteSpace(datasetKey))
            return Array.Empty<string>();

        if (ApprovedColumnsByDataset.TryGetValue(datasetKey.Trim(), out var columns))
            return columns;

        return ApprovedColumnsByDataset
            .FirstOrDefault(item => string.Equals(item.Key, datasetKey.Trim(), StringComparison.OrdinalIgnoreCase))
            .Value
            ?? new List<string>();
    }
}

public sealed class PublicDataExportAdminViewModel
{
    public CurrentUserInfo CurrentUser { get; init; } = new();
    public PublicDataExportCatalogDto Catalog { get; init; } = new();
    public PublicDataExportSettings Settings { get; init; } = new();
    public string StatusMessage { get; init; } = "";
    public string ErrorMessage { get; init; } = "";

    public IReadOnlyList<string> GetSelectedColumnKeys(string datasetKey)
    {
        var selected = Settings.GetApprovedColumns(datasetKey);
        if (selected.Count > 0)
            return selected;

        var dataset = Catalog.FindDataset(datasetKey);
        if (dataset is null)
            return Array.Empty<string>();

        return dataset.Columns
            .Where(static column => column.DefaultSelected)
            .Select(static column => column.Key)
            .ToList();
    }
}

public sealed class PublicDataExportAdminSaveRequest
{
    public string NewPassword { get; set; } = "";
    public string ConfirmPassword { get; set; } = "";
    public List<string> BillingColumns { get; set; } = new();
    public List<string> ExpensesColumns { get; set; } = new();
}

public sealed class PublicDataExportPublicViewModel
{
    public PublicDataExportCatalogDto Catalog { get; init; } = new();
    public PublicDataExportSettings Settings { get; init; } = new();
    public bool IsConfigured { get; init; }
    public bool IsAuthorized { get; init; }
    public bool IsCurrentUserAdmin { get; init; }
    public string LoginError { get; init; } = "";
    public string Message { get; init; } = "";

    public IReadOnlyList<PublicDataExportDatasetDefinition> AvailableDatasets =>
        Catalog.Datasets
            .Where(dataset => Settings.GetApprovedColumns(dataset.Key).Count > 0)
            .ToList();
}

public sealed class PublicDataExportCellDto
{
    public string DisplayValue { get; set; } = "";
    public string RawValue { get; set; } = "";
    public string ValueType { get; set; } = "text";
    public decimal? NumberValue { get; set; }
    public string DateValue { get; set; } = "";
}

public sealed class PublicDataExportRowDto
{
    public Dictionary<string, PublicDataExportCellDto> Cells { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PublicDataExportTableDto
{
    public string DatasetKey { get; set; } = "";
    public string DatasetLabel { get; set; } = "";
    public IReadOnlyList<PublicDataExportColumnDefinition> Columns { get; set; } = Array.Empty<PublicDataExportColumnDefinition>();
    public IReadOnlyList<PublicDataExportRowDto> Rows { get; set; } = Array.Empty<PublicDataExportRowDto>();
    public int RecordsCount { get; set; }
    public bool IsPreview { get; set; }
    public int? PreviewLimit { get; set; }
    public string Message { get; set; } = "";
}
