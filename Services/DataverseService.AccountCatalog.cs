using System.Globalization;
using System.Text.Json;
using CotizadorInterno.Web.Models.Automation;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string AccountCatalogLogicalName = "cr07a_cuentacontablesiigo";
    private const string AccountCatalogSetName = "cr07a_cuentacontablesiigos";
    private const string AccountCatalogIdField = "cr07a_cuentacontablesiigoid";
    private const string AccountCatalogPrimaryNameField = "cr07a_name";
    private const string AccountCatalogCodeField = "cr07a_codigo";
    private const string AccountCatalogNameField = "cr07a_nombre";
    private const string AccountCatalogTypeField = "cr07a_tipo";
    private const string AccountCatalogActiveField = "cr07a_activo";
    private const string AccountCatalogOriginField = "cr07a_origen";
    private const string AccountCatalogLastUpdateField = "cr07a_ultimaactualizacion";

    public async Task<AccountCatalogSyncResultDto> UpsertSiigoAccountCatalogAsync(
        DateOnly startDate,
        DateOnly endDate,
        IReadOnlyList<SiigoObservedAccountDto> accounts,
        CancellationToken ct = default)
    {
        if (startDate > endDate)
            throw new InvalidOperationException("El periodo de sincronizacion de cuentas contables no es valido.");

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            AccountCatalogLogicalName,
            AccountCatalogSetName,
            AccountCatalogIdField,
            AccountCatalogPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildAccountCatalogAttributeSet(metadata, attributes);

        var existingRows = await GetAccountCatalogRowsAsync(metadata, attributes, ct);
        var existingByCode = existingRows
            .Where(static row => !string.IsNullOrWhiteSpace(row.Code))
            .GroupBy(static row => row.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        var uniqueAccounts = (accounts ?? Array.Empty<SiigoObservedAccountDto>())
            .Where(static account => !string.IsNullOrWhiteSpace(account.Code))
            .GroupBy(static account => account.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static account => account.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var syncDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var created = 0;
        var updated = 0;
        var unchanged = 0;

        foreach (var account in uniqueAccounts)
        {
            ct.ThrowIfCancellationRequested();

            var code = account.Code.Trim();
            existingByCode.TryGetValue(code, out var current);
            var payload = BuildAccountCatalogPayload(metadata, attributes, account, current, syncDate);
            if (current is null)
            {
                await CallDataverseAppSendAsync(
                    $"/api/data/v9.2/{metadata.EntitySetName}",
                    "POST",
                    payload,
                    ct);
                created++;
                continue;
            }

            if (payload.Count == 0)
            {
                unchanged++;
                continue;
            }

            await CallDataverseAppSendAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}({current.RecordId})",
                "PATCH",
                payload,
                ct);
            updated++;
        }

        return new AccountCatalogSyncResultDto
        {
            StartDate = startDate,
            EndDate = endDate,
            ObservedAccounts = uniqueAccounts.Count,
            Created = created,
            Updated = updated,
            Unchanged = unchanged
        };
    }

    private async Task<IReadOnlyList<AccountCatalogRow>> GetAccountCatalogRowsAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        CancellationToken ct)
    {
        var optionalSelect = BuildOptionalSelect(
            attributes,
            metadata.PrimaryNameField,
            AccountCatalogCodeField,
            AccountCatalogNameField,
            AccountCatalogTypeField,
            AccountCatalogActiveField,
            AccountCatalogOriginField,
            AccountCatalogLastUpdateField);
        var select = string.Join(",", new[] { metadata.PrimaryIdField }
            .Concat(optionalSelect.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$top=5000";
        var rows = await GetDataverseAppEntitiesAsync(url, ct);
        return rows
            .Select(row => ParseAccountCatalogRow(row, metadata))
            .Where(static row => row is not null)
            .Cast<AccountCatalogRow>()
            .ToList();
    }

    private static AccountCatalogRow? ParseAccountCatalogRow(JsonElement item, RhEntityMetadata metadata)
    {
        var recordId = ReadString(item, metadata.PrimaryIdField).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        return new AccountCatalogRow
        {
            RecordId = recordId,
            PrimaryName = ReadString(item, metadata.PrimaryNameField).Trim(),
            Code = ReadString(item, AccountCatalogCodeField).Trim(),
            Name = ReadString(item, AccountCatalogNameField).Trim(),
            Type = ReadString(item, AccountCatalogTypeField).Trim(),
            Active = ReadBool(item, AccountCatalogActiveField),
            Origin = ReadString(item, AccountCatalogOriginField).Trim(),
            LastUpdate = ReadDateOnly(item, AccountCatalogLastUpdateField)
        };
    }

    private static Dictionary<string, object?> BuildAccountCatalogPayload(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        SiigoObservedAccountDto account,
        AccountCatalogRow? current,
        DateOnly syncDate)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var shouldCreate = current is null;
        var code = account.Code.Trim();
        var name = TruncateAccountCatalogText(FirstNonEmpty(account.Name, code), 100);
        var primaryName = TruncateAccountCatalogText($"{code} - {name}", 100);
        var type = TruncateAccountCatalogText(FirstNonEmpty(account.Type, "Otro"), 100);
        var preserveManualValues = current is not null && IsManualAccountCatalogOrigin(current.Origin);
        var origin = preserveManualValues
            ? current!.Origin
            : MergeAccountCatalogOrigin(current?.Origin, "Siigo automatico");

        SetAccountCatalogValue(payload, attributes, metadata.PrimaryNameField, current?.PrimaryName, primaryName, shouldCreate || !preserveManualValues);
        SetAccountCatalogValue(payload, attributes, AccountCatalogCodeField, current?.Code, code, shouldCreate);
        if (!preserveManualValues)
        {
            SetAccountCatalogValue(payload, attributes, AccountCatalogNameField, current?.Name, name, shouldCreate);
            SetAccountCatalogValue(payload, attributes, AccountCatalogTypeField, current?.Type, type, shouldCreate);
        }

        SetAccountCatalogValue(payload, attributes, AccountCatalogOriginField, current?.Origin, origin, shouldCreate || !preserveManualValues);
        SetAccountCatalogValue(payload, attributes, AccountCatalogActiveField, current?.Active, true, shouldCreate || current?.Active != true);
        SetAccountCatalogValue(
            payload,
            attributes,
            AccountCatalogLastUpdateField,
            current?.LastUpdate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            syncDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            true);

        return payload;
    }

    private static HashSet<string> BuildAccountCatalogAttributeSet(RhEntityMetadata metadata, ISet<string> attributes)
    {
        var values = attributes.Count > 0
            ? attributes
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                metadata.PrimaryIdField,
                metadata.PrimaryNameField,
                AccountCatalogCodeField,
                AccountCatalogNameField,
                AccountCatalogTypeField,
                AccountCatalogActiveField,
                AccountCatalogOriginField,
                AccountCatalogLastUpdateField
            };

        values.Add(metadata.PrimaryIdField);
        if (!string.IsNullOrWhiteSpace(metadata.PrimaryNameField))
            values.Add(metadata.PrimaryNameField);

        return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
    }

    private static void SetAccountCatalogValue<T>(
        IDictionary<string, object?> payload,
        ISet<string> attributes,
        string field,
        T? current,
        T? value,
        bool force = false)
    {
        if (string.IsNullOrWhiteSpace(field) || !attributes.Contains(field))
            return;

        if (!force && ValuesEqual(current, value))
            return;

        if (force || !ValuesEqual(current, value))
            payload[field] = value;
    }

    private static bool IsManualAccountCatalogOrigin(string origin) =>
        origin.Contains("manual", StringComparison.OrdinalIgnoreCase)
        || origin.Contains("validado", StringComparison.OrdinalIgnoreCase);

    private static string MergeAccountCatalogOrigin(string? current, string source)
    {
        var values = (current ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(new[] { source })
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return TruncateAccountCatalogText(string.Join("; ", values), 100);
    }

    private static string TruncateAccountCatalogText(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }

    private sealed class AccountCatalogRow
    {
        public string RecordId { get; init; } = "";
        public string PrimaryName { get; init; } = "";
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public string Type { get; init; } = "";
        public bool Active { get; init; }
        public string Origin { get; init; } = "";
        public DateOnly? LastUpdate { get; init; }
    }
}
