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
    private static readonly IReadOnlyDictionary<string, string> ValidatedAccountCatalogNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["1105050102"] = "Caja manejo para pagos",
        ["11051001"] = "Cajas menores",
        ["11100502"] = "Pagos en linea",
        ["11100504"] = "Bancolombia Cloud 8100",
        ["11100505"] = "Bancolombia Copiers 7316",
        ["13050501"] = "Clientes nacionales",
        ["13551501"] = "Retefuente 2.5%",
        ["13551503"] = "Retefuente 4%",
        ["13551513"] = "Retefuente 3.5%",
        ["13551701"] = "ReteIVA 15%",
        ["13551801"] = "ReteICA 11.04",
        ["13551805"] = "ReteICA 9.66",
        ["22050501"] = "Proveedores nacionales",
        ["42958101"] = "Ajuste al peso",
        ["510521"] = "Viaticos",
        ["511030"] = "Asesoria contable",
        ["511036"] = "Asesoria comercial",
        ["51201001"] = "Arrendamientos - construcciones y edificaciones",
        ["51303001"] = "Seguros - Terremoto",
        ["51352501"] = "Servicios publicos - acueducto y alcantarillado",
        ["51353001"] = "Servicios publicos - energia electrica",
        ["51353501"] = "Servicios publicos - telefono",
        ["51952501"] = "Elementos de aseo y cafeteria",
        ["52054801"] = "Bonificaciones",
        ["529501"] = "Gastos de transporte y fletes",
        ["53050501"] = "Gastos bancarios",
        ["53050502"] = "Gravamen 4 x 1000",
        ["53054001"] = "IVA bancario",
        ["613510"] = "Mercancias no fabricadas por la empresa",
        ["61355401"] = "Servicios de nube IaaS / Cloud",
        ["61355402"] = "Servicios de nube",
        ["613599"] = "Costo de ventas suministros"
    };

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
            PrimaryName = RepairSpanishMojibakeText(ReadString(item, metadata.PrimaryNameField)).Trim(),
            Code = ReadString(item, AccountCatalogCodeField).Trim(),
            Name = RepairSpanishMojibakeText(ReadString(item, AccountCatalogNameField)).Trim(),
            Type = RepairSpanishMojibakeText(ReadString(item, AccountCatalogTypeField)).Trim(),
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
        var name = ResolveAccountCatalogName(code, account.Name);
        var primaryName = TruncateAccountCatalogText($"{code} - {name}", 100);
        var type = TruncateAccountCatalogText(RepairSpanishMojibakeText(FirstNonEmpty(account.Type, "Otro")), 100);
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
        if (!preserveManualValues)
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

    private static string ResolveAccountCatalogName(string code, string? name)
    {
        var normalizedCode = (code ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(normalizedCode)
            && ValidatedAccountCatalogNames.TryGetValue(normalizedCode, out var validatedName))
        {
            return validatedName;
        }

        var normalizedName = TruncateAccountCatalogText(RepairSpanishMojibakeText(FirstNonEmpty(name, normalizedCode)), 100).Trim();
        if (IsLikelyAccountCatalogLineDescription(normalizedName))
            return normalizedCode;

        return normalizedName;
    }

    private static bool IsLikelyAccountCatalogLineDescription(string value)
    {
        var normalized = NormalizeConciliacionLookupText(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (normalized.Contains(" base:", StringComparison.OrdinalIgnoreCase))
            return true;

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length < 3)
            return false;

        return normalized.StartsWith("nomina ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("pago nomina ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("pago banco ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("proveedor ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("cuenta de cobro ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("clientes nacionales ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("ajuste al peso ", StringComparison.OrdinalIgnoreCase);
    }

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

    private static string RepairSpanishMojibakeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value ?? "";

        var text = value;
        foreach (var (source, target) in new[]
        {
            ("\u00c3\u00a1", "\u00e1"),
            ("\u00c3\u00a9", "\u00e9"),
            ("\u00c3\u00ad", "\u00ed"),
            ("\u00c3\u00b3", "\u00f3"),
            ("\u00c3\u00ba", "\u00fa"),
            ("\u00c3\u00bc", "\u00fc"),
            ("\u00c3\u00b1", "\u00f1"),
            ("\u00c3\u0081", "\u00c1"),
            ("\u00c3\u0089", "\u00c9"),
            ("\u00c3\u008d", "\u00cd"),
            ("\u00c3\u0093", "\u00d3"),
            ("\u00c3\u009a", "\u00da"),
            ("\u00c3\u009c", "\u00dc"),
            ("\u00c3\u0091", "\u00d1"),
            ("\u00c2", "")
        })
        {
            text = text.Replace(source, target, StringComparison.Ordinal);
        }

        foreach (var (source, target) in new[]
        {
            ("CI\ufffdN", "CI\u00d3N"),
            ("ci\ufffdn", "ci\u00f3n"),
            ("SI\ufffdN", "SI\u00d3N"),
            ("si\ufffdn", "si\u00f3n"),
            ("P\ufffdBLIC", "P\u00daBLIC"),
            ("p\ufffdblic", "p\u00fablic"),
            ("N\ufffdMINA", "N\u00d3MINA"),
            ("n\ufffdmina", "n\u00f3mina"),
            ("P\ufffdLIZA", "P\u00d3LIZA"),
            ("p\ufffdliza", "p\u00f3liza"),
            ("INTER\ufffdS", "INTER\u00c9S"),
            ("inter\ufffds", "inter\u00e9s"),
            ("D\ufffdBITO", "D\u00c9BITO"),
            ("d\ufffdbito", "d\u00e9bito"),
            ("CR\ufffdDITO", "CR\u00c9DITO"),
            ("cr\ufffddito", "cr\u00e9dito"),
            ("TEL\ufffdFONO", "TEL\u00c9FONO"),
            ("tel\ufffdfono", "tel\u00e9fono"),
            ("T\ufffdCNIC", "T\u00c9CNIC"),
            ("t\ufffdcnic", "t\u00e9cnic"),
            ("ASESOR\ufffdA", "ASESOR\u00cdA"),
            ("asesor\ufffda", "asesor\u00eda"),
            ("VEH\ufffdCUL", "VEH\u00cdCUL"),
            ("veh\ufffdcul", "veh\u00edcul"),
            ("COMPA\ufffd\ufffdA", "COMPA\u00d1\u00cdA"),
            ("compa\ufffd\ufffda", "compa\u00f1\u00eda"),
            ("COMPA\ufffdIA", "COMPA\u00d1IA"),
            ("compa\ufffdia", "compa\u00f1ia"),
            ("EL\ufffdCTR", "EL\u00c9CTR"),
            ("el\ufffdctr", "el\u00e9ctr"),
            ("M\ufffdDIC", "M\u00c9DIC"),
            ("m\ufffddic", "m\u00e9dic"),
            ("P\ufffdGINA", "P\u00c1GINA"),
            ("p\ufffdgina", "p\u00e1gina"),
            ("PAPELER\ufffdA", "PAPELER\u00cdA"),
            ("papeler\ufffda", "papeler\u00eda"),
            ("TECNOLOG\ufffdA", "TECNOLOG\u00cdA"),
            ("tecnolog\ufffda", "tecnolog\u00eda"),
            ("GARANT\ufffdA", "GARANT\u00cdA"),
            ("garant\ufffda", "garant\u00eda"),
            ("A\ufffdO", "A\u00d1O"),
            ("a\ufffdo", "a\u00f1o")
        })
        {
            text = text.Replace(source, target, StringComparison.Ordinal);
        }

        return text;
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
