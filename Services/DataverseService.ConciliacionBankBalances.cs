using System.Globalization;
using CotizadorInterno.Web.Models.Conciliacion;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string ConciliacionBankCloseLogicalName = "cr07a_cierreflujocajabanco";
    private const string ConciliacionBankCloseSetName = "cr07a_cierreflujocajabancos";
    private const string ConciliacionBankCloseIdField = "cr07a_cierreflujocajabancoid";
    private const string ConciliacionBankClosePrimaryNameField = "cr07a_name";
    private const string ConciliacionBankCloseExternalKeyField = "cr07a_claveexterna";
    private const string ConciliacionBankClosePeriodField = "cr07a_periodokey";
    private const string ConciliacionBankCloseSourceFlowField = "cr07a_origenflujo";
    private const string ConciliacionBankCloseAccountCodeField = "cr07a_bancocuentacodigo";
    private const string ConciliacionBankCloseAccountNameField = "cr07a_bancocuentanombre";
    private const string ConciliacionBankCloseOpeningBalanceField = "cr07a_saldoinicial";
    private const decimal ConciliacionBankOpeningBalanceLimit = 100_000_000_000m;

    public async Task<IReadOnlyList<ConciliacionBankBalanceDto>> GetConciliacionBankBalancesAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        ValidateConciliacionBankBalancePeriod(year, month);
        var start = new DateOnly(year, month, 1);
        var endExclusive = start.AddMonths(1);
        var rowsTask = GetConciliacionCashFlowRowsAsync(start, endExclusive, ct);
        var openingBalancesTask = GetConciliacionBankOpeningBalanceIndexAsync(year, month, ct);
        await Task.WhenAll(rowsTask, openingBalancesTask);

        return BuildConciliacionCashFlowBankBalances(
            rowsTask.Result,
            year,
            month,
            openingBalancesTask.Result);
    }

    public async Task<ConciliacionBankOpeningBalanceResultDto> SetConciliacionBankOpeningBalanceAsync(
        ConciliacionBankOpeningBalanceRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateConciliacionBankBalancePeriod(request.Year, request.Month);

        var openingBalance = RoundCurrency(request.OpeningBalance);
        if (openingBalance < -ConciliacionBankOpeningBalanceLimit
            || openingBalance > ConciliacionBankOpeningBalanceLimit)
        {
            throw new InvalidOperationException(
                $"El saldo inicial debe estar entre {-ConciliacionBankOpeningBalanceLimit:N0} "
                + $"y {ConciliacionBankOpeningBalanceLimit:N0}.");
        }

        var requestedBankKey = (request.BankKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(requestedBankKey))
            throw new InvalidOperationException("Selecciona el banco al que corresponde el saldo inicial.");

        var currentBalances = await GetConciliacionBankBalancesAsync(request.Year, request.Month, ct);
        var selectedBank = currentBalances.FirstOrDefault(balance =>
            string.Equals(balance.BankKey, requestedBankKey, StringComparison.OrdinalIgnoreCase));
        if (selectedBank is null || string.IsNullOrWhiteSpace(selectedBank.BankAccountCode))
        {
            throw new InvalidOperationException(
                "El banco seleccionado no corresponde a una cuenta bancaria configurada para conciliacion.");
        }

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ConciliacionBankCloseLogicalName,
            ConciliacionBankCloseSetName,
            ConciliacionBankCloseIdField,
            ConciliacionBankClosePrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        EnsureConciliacionBankBalanceSchema(metadata, attributes);

        var externalKey = BuildConciliacionBankBalanceExternalKey(
            request.Year,
            request.Month,
            selectedBank.SourceFlow,
            selectedBank.BankAccountCode);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [metadata.PrimaryNameField] =
                $"{selectedBank.PeriodKey} - {selectedBank.SourceFlow} - {selectedBank.BankAccountCode}",
            [ConciliacionBankCloseExternalKeyField] = externalKey,
            [ConciliacionBankClosePeriodField] = selectedBank.PeriodKey,
            [ConciliacionBankCloseSourceFlowField] = selectedBank.SourceFlow,
            [ConciliacionBankCloseAccountCodeField] = selectedBank.BankAccountCode,
            [ConciliacionBankCloseAccountNameField] = selectedBank.BankAccountName,
            [ConciliacionBankCloseOpeningBalanceField] = openingBalance
        };

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}"
            + $"({ConciliacionBankCloseExternalKeyField}='{EscapeOdataLiteral(externalKey)}')",
            "PATCH",
            payload,
            ct);

        var refreshed = await GetConciliacionBankBalancesAsync(request.Year, request.Month, ct);
        var persisted = refreshed.FirstOrDefault(balance =>
            string.Equals(balance.BankKey, selectedBank.BankKey, StringComparison.OrdinalIgnoreCase));
        if (persisted is null
            || !persisted.HasOpeningBalance
            || persisted.OpeningBalance != openingBalance)
        {
            throw new InvalidOperationException(
                "Dataverse no devolvio el saldo inicial guardado; el cambio no se dara por confirmado.");
        }

        return new ConciliacionBankOpeningBalanceResultDto
        {
            Message = $"Saldo inicial de {persisted.BankLabel} guardado para {persisted.PeriodKey}.",
            Balance = persisted
        };
    }

    internal static IReadOnlyList<ConciliacionBankBalanceDto> BuildConciliacionCashFlowBankBalances(
        IReadOnlyList<ConciliacionCashFlowRowDto> rows,
        int year,
        int month,
        IReadOnlyDictionary<string, decimal>? openingBalances = null)
    {
        ValidateConciliacionBankBalancePeriod(year, month);
        rows ??= Array.Empty<ConciliacionCashFlowRowDto>();
        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = periodStart.AddMonths(1);
        var periodKey = $"{year:D4}-{month:D2}";
        var openingIndex = openingBalances is null
            ? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, decimal>(openingBalances, StringComparer.OrdinalIgnoreCase);
        var accounts = new Dictionary<string, ConciliacionBankAccountIdentity>(
            StringComparer.OrdinalIgnoreCase);

        AddConfiguredAccount("Cloud");
        AddConfiguredAccount("Copiers");

        var periodRows = rows
            .Where(row =>
            {
                var date = ParseConciliacionDateOnlyValue(row.MovementDateValue);
                return date.HasValue && date.Value >= periodStart && date.Value < periodEnd;
            })
            .Where(static row => !string.IsNullOrWhiteSpace(row.BankAccountCode))
            .ToArray();

        foreach (var row in periodRows)
        {
            var sourceFlow = FirstNonEmpty(row.SourceFlow, "Sin vertical").Trim();
            var accountCode = row.BankAccountCode.Trim();
            var bankKey = BuildConciliacionBankBalanceKey(sourceFlow, accountCode);
            accounts[bankKey] = new ConciliacionBankAccountIdentity(
                bankKey,
                sourceFlow,
                accountCode,
                FirstNonEmpty(row.BankAccountName, accountCode).Trim());
        }

        return accounts.Values
            .Select(account =>
            {
                var accountRows = periodRows
                    .Where(row => string.Equals(
                        BuildConciliacionBankBalanceKey(row.SourceFlow, row.BankAccountCode),
                        account.BankKey,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var totalEntries = RoundCurrency(accountRows.Sum(static row => row.EntryValue));
                var totalExits = RoundCurrency(accountRows.Sum(static row => row.ExitValue));
                var hasOpeningBalance = openingIndex.TryGetValue(account.BankKey, out var openingBalance);
                openingBalance = RoundCurrency(openingBalance);

                return new ConciliacionBankBalanceDto
                {
                    BankKey = account.BankKey,
                    BankLabel = BuildConciliacionBankBalanceLabel(account),
                    SourceFlow = account.SourceFlow,
                    BankAccountCode = account.BankAccountCode,
                    BankAccountName = account.BankAccountName,
                    Year = year,
                    Month = month,
                    PeriodKey = periodKey,
                    HasOpeningBalance = hasOpeningBalance,
                    OpeningBalance = openingBalance,
                    TotalEntries = totalEntries,
                    TotalExits = totalExits,
                    CurrentBalance = RoundCurrency(openingBalance + totalEntries - totalExits)
                };
            })
            .OrderBy(static balance => balance.SourceFlow, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static balance => balance.BankAccountCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        void AddConfiguredAccount(string sourceFlow)
        {
            var configured = ResolveConciliacionCashFlowBankAccount(sourceFlow);
            if (string.IsNullOrWhiteSpace(configured.Code))
                return;

            var bankKey = BuildConciliacionBankBalanceKey(sourceFlow, configured.Code);
            accounts[bankKey] = new ConciliacionBankAccountIdentity(
                bankKey,
                sourceFlow,
                configured.Code,
                configured.Name);
        }
    }

    internal static string BuildConciliacionBankBalanceExternalKey(
        int year,
        int month,
        string sourceFlow,
        string bankAccountCode)
    {
        ValidateConciliacionBankBalancePeriod(year, month);
        var normalizedFlow = NormalizeConciliacionBankBalanceKeyPart(sourceFlow);
        var normalizedCode = NormalizeConciliacionBankBalanceKeyPart(bankAccountCode);
        if (string.IsNullOrWhiteSpace(normalizedFlow) || string.IsNullOrWhiteSpace(normalizedCode))
            throw new InvalidOperationException("El banco no tiene una identidad estable para guardar su saldo.");

        return $"conciliacion:flujo-caja:banco:{year:D4}-{month:D2}:{normalizedFlow}:{normalizedCode}";
    }

    private async Task<IReadOnlyDictionary<string, decimal>> GetConciliacionBankOpeningBalanceIndexAsync(
        int year,
        int month,
        CancellationToken ct)
    {
        ValidateConciliacionBankBalancePeriod(year, month);
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ConciliacionBankCloseLogicalName,
            ConciliacionBankCloseSetName,
            ConciliacionBankCloseIdField,
            ConciliacionBankClosePrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        EnsureConciliacionBankBalanceSchema(metadata, attributes);

        var select = string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            ConciliacionBankCloseExternalKeyField,
            ConciliacionBankClosePeriodField,
            ConciliacionBankCloseSourceFlowField,
            ConciliacionBankCloseAccountCodeField,
            ConciliacionBankCloseOpeningBalanceField
        });
        var periodKey = $"{year:D4}-{month:D2}";
        var filter = $"{ConciliacionBankClosePeriodField} eq '{EscapeOdataLiteral(periodKey)}'";
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}"
            + $"&$filter={Uri.EscapeDataString(filter)}";
        var rows = await GetDataverseAppEntitiesAsync(url, ct);
        var duplicate = rows
            .GroupBy(
                row => BuildConciliacionBankBalanceKey(
                    ReadString(row, ConciliacionBankCloseSourceFlowField),
                    ReadString(row, ConciliacionBankCloseAccountCodeField)),
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group =>
                !string.IsNullOrWhiteSpace(group.Key)
                && group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Dataverse tiene mas de un saldo inicial para {duplicate.Key} en {periodKey}.");
        }

        return rows
            .Select(row => new
            {
                BankKey = BuildConciliacionBankBalanceKey(
                    ReadString(row, ConciliacionBankCloseSourceFlowField),
                    ReadString(row, ConciliacionBankCloseAccountCodeField)),
                OpeningBalance = RoundCurrency(
                    ReadDecimal(row, ConciliacionBankCloseOpeningBalanceField) ?? 0m)
            })
            .Where(static row => !string.IsNullOrWhiteSpace(row.BankKey))
            .ToDictionary(
                static row => row.BankKey,
                static row => row.OpeningBalance,
                StringComparer.OrdinalIgnoreCase);
    }

    private static void EnsureConciliacionBankBalanceSchema(
        RhEntityMetadata metadata,
        ISet<string> attributes)
    {
        var required = new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            ConciliacionBankCloseExternalKeyField,
            ConciliacionBankClosePeriodField,
            ConciliacionBankCloseSourceFlowField,
            ConciliacionBankCloseAccountCodeField,
            ConciliacionBankCloseAccountNameField,
            ConciliacionBankCloseOpeningBalanceField
        };
        var missing = required
            .Where(field =>
                !string.Equals(field, metadata.PrimaryIdField, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(field, metadata.PrimaryNameField, StringComparison.OrdinalIgnoreCase)
                && !attributes.Contains(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                "La tabla de saldos bancarios no tiene el esquema requerido: "
                + string.Join(", ", missing));
        }
    }

    private static string BuildConciliacionBankBalanceKey(string? sourceFlow, string? bankAccountCode)
    {
        var flow = (sourceFlow ?? "").Trim();
        var code = (bankAccountCode ?? "").Trim();
        return string.IsNullOrWhiteSpace(flow) || string.IsNullOrWhiteSpace(code)
            ? ""
            : $"{flow}|{code}";
    }

    private static string BuildConciliacionBankBalanceLabel(ConciliacionBankAccountIdentity account) =>
        $"{account.SourceFlow} - {account.BankAccountName} ({account.BankAccountCode})";

    private static string NormalizeConciliacionBankBalanceKeyPart(string? value)
    {
        var normalized = NormalizeConciliacionAccountingVoucherText(value ?? "")
            .ToLowerInvariant();
        return System.Text.RegularExpressions.Regex.Replace(
            normalized,
            @"[^a-z0-9]+",
            "-",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            .Trim('-');
    }

    private static void ValidateConciliacionBankBalancePeriod(int year, int month)
    {
        if (year < 2020 || year > 2100 || month is < 1 or > 12)
            throw new InvalidOperationException("El periodo del saldo bancario no es valido.");
    }

    private sealed record ConciliacionBankAccountIdentity(
        string BankKey,
        string SourceFlow,
        string BankAccountCode,
        string BankAccountName);
}
