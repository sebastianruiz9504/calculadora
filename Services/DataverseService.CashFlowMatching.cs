using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.Automation;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string ClientPaymentMatchLogicalName = "cr07a_cruceflujocaja";
    private const string ClientPaymentMatchSetName = "cr07a_cruceflujocajas";
    private const string ClientPaymentMatchIdField = "cr07a_cruceflujocajaid";
    private const string ClientPaymentMatchPrimaryNameField = "cr07a_name";
    private const string ClientPaymentMatchTypeField = "cr07a_tipo";
    private const string ClientPaymentMatchStatusField = "cr07a_estado";
    private const string ClientPaymentMatchConfidenceField = "cr07a_confianza";
    private const string ClientPaymentMatchReasonField = "cr07a_motivo";
    private const string ClientPaymentMatchDifferenceField = "cr07a_diferencia";
    private const string ClientPaymentMatchMovementIdField = "cr07a_movimientobancarioid";
    private const string ClientPaymentMatchMovementExternalKeyField = "cr07a_movimientoclaveexterna";
    private const string ClientPaymentMatchMovementDateField = "cr07a_fechamovimiento";
    private const string ClientPaymentMatchSourceFlowField = "cr07a_origenflujo";
    private const string ClientPaymentMatchBankCodeField = "cr07a_bancocuentacodigo";
    private const string ClientPaymentMatchBankNameField = "cr07a_bancocuentanombre";
    private const string ClientPaymentMatchDescriptionField = "cr07a_descripcionmovimiento";
    private const string ClientPaymentMatchEntryField = "cr07a_valorentrada";
    private const string ClientPaymentMatchInvoiceIdsField = "cr07a_facturacionid";
    private const string ClientPaymentMatchInvoiceNumbersField = "cr07a_facturanumero";
    private const string ClientPaymentMatchClientField = "cr07a_cliente";
    private const string ClientPaymentMatchInvoiceTotalField = "cr07a_valorfactura";
    private const string ClientPaymentMatchPaymentValueField = "cr07a_valorpago";
    private const string ClientPaymentMatchReteFteField = "cr07a_reteftevalor";
    private const string ClientPaymentMatchReteIcaField = "cr07a_reteicavalor";
    private const string ClientPaymentMatchRteIvaField = "cr07a_rteivavalor";
    private const string ClientPaymentMatchDraftJsonField = "cr07a_jsonborradorsiigo";
    private const string ClientPaymentMatchExternalKeyField = "cr07a_claveexterna";
    private const string ClientPaymentMatchSourceHashField = "cr07a_hashorigen";
    private static readonly Regex CashFlowInvoiceTokenRegex = new(
        @"\b(?<prefix>FEDT|FEKT|FVE|FEV|FEM|FV|FE)[-\s]*(?:(?<series>\d+)[-\s]+)?(?<number>\d+)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public async Task<CashFlowClientPaymentMatchResultDto> MatchCashFlowClientPaymentsAsync(
        DateOnly startDate,
        DateOnly endDate,
        bool dryRun = false,
        decimal differenceTolerance = 2000m,
        CancellationToken ct = default)
    {
        if (startDate > endDate)
            throw new InvalidOperationException("El periodo de cruce de flujo de caja no es valido.");

        var movementMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var movementAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(movementMetadata.LogicalName, ct);
        movementAttributes = BuildCashFlowClientPaymentMovementAttributeSet(movementMetadata, movementAttributes);

        var matchMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var matchAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(matchMetadata.LogicalName, ct);
        matchAttributes = BuildCashFlowClientPaymentMatchAttributeSet(matchMetadata, matchAttributes);

        var billingMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            ct);

        var movements = await GetCashFlowClientPaymentMovementsAsync(
            movementMetadata,
            movementAttributes,
            startDate,
            endDate,
            ct);
        var invoices = await GetCashFlowClientPaymentBillingRowsAsync(billingMetadata, ct);
        var invoiceIndex = BuildCashFlowClientPaymentInvoiceIndex(invoices);
        var rows = movements
            .Select(movement => BuildCashFlowClientPaymentMatchRow(movement, invoiceIndex, differenceTolerance))
            .ToArray();
        var existingIndex = await GetCashFlowClientPaymentMatchExistingIndexAsync(matchMetadata, matchAttributes, ct);

        using var throttler = new SemaphoreSlim(CashFlowUpsertMaxConcurrency);
        var tasks = rows.Select(async row =>
        {
            ct.ThrowIfCancellationRequested();
            await throttler.WaitAsync(ct);
            try
            {
                return await UpsertCashFlowClientPaymentMatchAsync(
                    matchMetadata,
                    matchAttributes,
                    existingIndex,
                    row,
                    dryRun,
                    ct);
            }
            finally
            {
                throttler.Release();
            }
        }).ToArray();
        var outcomes = await Task.WhenAll(tasks);

        var result = new CashFlowClientPaymentMatchResultDto
        {
            DryRun = dryRun,
            StartDate = startDate,
            EndDate = endDate,
            ReviewedMovements = movements.Count,
            CandidateMovements = movements.Count,
            Rows = rows
                .OrderByDescending(static row => row.MovementDate)
                .ThenBy(static row => row.SourceFlow, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.Description, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            TotalEntries = RoundCurrency(movements.Sum(static row => row.EntryValue)),
            SuggestedEntries = RoundCurrency(rows.Where(static row => IsCashFlowClientPaymentSuggested(row.Status)).Sum(static row => row.EntryValue)),
            PendingReviewEntries = RoundCurrency(rows.Where(static row => !IsCashFlowClientPaymentSuggested(row.Status)).Sum(static row => row.EntryValue)),
            Suggested = rows.Count(static row => IsCashFlowClientPaymentSuggested(row.Status)),
            PendingReview = rows.Count(static row => !IsCashFlowClientPaymentSuggested(row.Status)),
            NoInvoiceToken = rows.Count(static row => string.Equals(row.Status, "SinFacturaDescripcion", StringComparison.OrdinalIgnoreCase)),
            NoInvoiceMatch = rows.Count(static row => string.Equals(row.Status, "FacturaNoEncontrada", StringComparison.OrdinalIgnoreCase)),
            AmbiguousInvoice = rows.Count(static row => string.Equals(row.Status, "FacturaAmbigua", StringComparison.OrdinalIgnoreCase)),
            DifferenceOutOfTolerance = rows.Count(static row => string.Equals(row.Status, "DiferenciaFueraRango", StringComparison.OrdinalIgnoreCase))
        };

        foreach (var outcome in outcomes)
        {
            switch (outcome)
            {
                case CashFlowClientPaymentMatchOutcome.Created:
                    result.Created++;
                    break;
                case CashFlowClientPaymentMatchOutcome.Updated:
                    result.Updated++;
                    break;
                case CashFlowClientPaymentMatchOutcome.Unchanged:
                    result.Unchanged++;
                    break;
                case CashFlowClientPaymentMatchOutcome.Skipped:
                    result.Skipped++;
                    break;
            }
        }

        return result;
    }

    private async Task<IReadOnlyList<CashFlowClientPaymentMovementRow>> GetCashFlowClientPaymentMovementsAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var select = BuildCashFlowClientPaymentMovementSelect(metadata, attributes);
        var endExclusive = endDate.AddDays(1);
        var filters = new List<string>
        {
            BuildBillingDateFilter(CashFlowDateField, "date-only", startDate, endExclusive),
            $"{CashFlowEntryField} gt 0"
        }
        .Where(static filter => !string.IsNullOrWhiteSpace(filter));
        var filter = string.Join(" and ", filters);
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={CashFlowDateField} asc";
        var items = await GetDataverseAppEntitiesAsync(url, ct, AddFormattedValueHeaders);

        return items
            .Select(item => ParseCashFlowClientPaymentMovement(item, metadata))
            .Where(static row => row is not null && row.EntryValue > 0m && !IsCashFlowClientPaymentMovementExcluded(row))
            .Cast<CashFlowClientPaymentMovementRow>()
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
    }

    private async Task<IReadOnlyList<BillingRecordRow>> GetCashFlowClientPaymentBillingRowsAsync(
        RhEntityMetadata metadata,
        CancellationToken ct)
    {
        var select = BuildBillingSelectClause(metadata);
        var orderBy = Uri.EscapeDataString($"{_dashboardBillingEmissionDateField} desc");
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$orderby={orderBy}";
        var items = await GetDataverseAppEntitiesAsync(url, ct, AddFormattedValueHeaders);

        return items
            .Select(item => ParseBillingRecord(item, metadata.PrimaryIdField, metadata.PrimaryNameField))
            .Where(static row => row is not null && !string.IsNullOrWhiteSpace(row.InvoiceNumber))
            .Cast<BillingRecordRow>()
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
    }

    private CashFlowClientPaymentMatchRowDto BuildCashFlowClientPaymentMatchRow(
        CashFlowClientPaymentMovementRow movement,
        IReadOnlyDictionary<string, List<BillingRecordRow>> invoiceIndex,
        decimal differenceTolerance)
    {
        var tokens = ExtractCashFlowClientPaymentInvoiceTokens(movement.Description);
        var row = new CashFlowClientPaymentMatchRowDto
        {
            MovementId = movement.RecordId,
            MovementExternalKey = movement.ExternalKey,
            MovementDate = movement.Date,
            SourceFlow = movement.SourceFlow,
            BankAccountCode = movement.BankAccountCode,
            BankAccountName = movement.BankAccountName,
            Description = movement.Description,
            EntryValue = movement.EntryValue,
            InvoiceTokens = tokens,
            Status = "SinFacturaDescripcion",
            Reason = "La descripcion del movimiento no trae una factura reconocible.",
            Confidence = 20
        };

        if (tokens.Count == 0)
            return FinalizeCashFlowClientPaymentMatchRow(row, Array.Empty<BillingRecordRow>());

        var matched = new List<BillingRecordRow>();
        var missing = new List<string>();
        var ambiguous = new List<string>();
        foreach (var token in tokens)
        {
            var key = NormalizeDocumentKey(token);
            if (string.IsNullOrWhiteSpace(key) || !invoiceIndex.TryGetValue(key, out var candidates) || candidates.Count == 0)
            {
                missing.Add(token);
                continue;
            }

            var distinctCandidates = candidates
                .GroupBy(static candidate => candidate.RecordId, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .ToList();
            if (distinctCandidates.Count > 1)
            {
                ambiguous.Add(token);
                continue;
            }

            matched.Add(distinctCandidates[0]);
        }

        matched = matched
            .GroupBy(static invoice => invoice.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();

        if (ambiguous.Count > 0)
        {
            row.Status = "FacturaAmbigua";
            row.Reason = $"La descripcion trae facturas con mas de un posible registro en Dataverse: {string.Join(", ", ambiguous)}.";
            row.Confidence = 45;
            return FinalizeCashFlowClientPaymentMatchRow(row, matched);
        }

        if (missing.Count > 0)
        {
            row.Status = "FacturaNoEncontrada";
            row.Reason = $"No encontre estas facturas en Dataverse: {string.Join(", ", missing)}.";
            row.Confidence = matched.Count > 0 ? 55 : 35;
            return FinalizeCashFlowClientPaymentMatchRow(row, matched);
        }

        var invoiceTotal = RoundCurrency(matched.Sum(static invoice => invoice.NetTotalInvoice));
        var reteFteValue = RoundCurrency(matched.Sum(ResolveCashFlowClientPaymentReteFteValue));
        var reteIcaValue = RoundCurrency(matched.Sum(ResolveCashFlowClientPaymentReteIcaValue));
        var rteIvaValue = RoundCurrency(matched.Sum(ResolveCashFlowClientPaymentRteIvaValue));
        var retentions = RoundCurrency(reteFteValue + reteIcaValue + rteIvaValue);
        var difference = RoundCurrency(invoiceTotal - movement.EntryValue - retentions);
        row.InvoiceTotal = invoiceTotal;
        row.ReteFteValue = reteFteValue;
        row.ReteIcaValue = reteIcaValue;
        row.RteIvaValue = rteIvaValue;
        row.RetentionsTotal = retentions;
        row.DifferenceValue = difference;

        if (Math.Abs(difference) <= differenceTolerance)
        {
            row.Status = "Sugerido";
            row.Reason = matched.Count == 1
                ? "Factura encontrada en la descripcion y diferencia dentro del rango."
                : "Facturas encontradas en la descripcion y diferencia agregada dentro del rango.";
            row.Confidence = matched.Count == 1 ? 95 : 90;
            return FinalizeCashFlowClientPaymentMatchRow(row, matched);
        }

        row.Status = "DiferenciaFueraRango";
        row.Reason = matched.Any(static invoice => invoice.RteFteValue > 0m || invoice.ReteIcaValue > 0m || invoice.RteIvaValue > 0m)
            ? $"Factura encontrada, pero la diferencia supera {differenceTolerance:0.##}."
            : $"Factura encontrada, pero la diferencia supera {differenceTolerance:0.##}. Puede faltar registrar retencion en facturacion.";
        row.Confidence = 75;
        return FinalizeCashFlowClientPaymentMatchRow(row, matched);
    }

    private CashFlowClientPaymentMatchRowDto FinalizeCashFlowClientPaymentMatchRow(
        CashFlowClientPaymentMatchRowDto row,
        IReadOnlyList<BillingRecordRow> invoices)
    {
        row.InvoiceRecordIds = JoinDistinctCashFlowClientPaymentValues(invoices.Select(static invoice => invoice.RecordId));
        row.InvoiceNumbers = JoinDistinctCashFlowClientPaymentValues(invoices.Select(static invoice => invoice.InvoiceNumber));
        row.ClientNames = JoinDistinctCashFlowClientPaymentValues(invoices.Select(static invoice => invoice.ClientName));
        row.ExternalKey = BuildCashFlowClientPaymentExternalKey(row);
        row.SiigoDraftJson = BuildCashFlowClientPaymentSiigoDraftJson(row, invoices);
        row.SourceHash = BuildCashFlowClientPaymentSourceHash(row);
        return row;
    }

    private async Task<CashFlowClientPaymentMatchOutcome> UpsertCashFlowClientPaymentMatchAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        IReadOnlyDictionary<string, CashFlowClientPaymentMatchExistingRecord> existingIndex,
        CashFlowClientPaymentMatchRowDto row,
        bool dryRun,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(row.ExternalKey))
            return CashFlowClientPaymentMatchOutcome.Skipped;

        var payload = BuildCashFlowClientPaymentMatchPayload(metadata, attributes, row);
        if (payload.Count == 0)
            return CashFlowClientPaymentMatchOutcome.Unchanged;

        if (existingIndex.TryGetValue(row.ExternalKey, out var existing))
        {
            if (IsCashFlowClientPaymentProtectedMatchStatus(existing.Status))
                return CashFlowClientPaymentMatchOutcome.Unchanged;

            if (attributes.Contains(ClientPaymentMatchSourceHashField)
                && !string.IsNullOrWhiteSpace(existing.SourceHash)
                && string.Equals(existing.SourceHash, row.SourceHash, StringComparison.OrdinalIgnoreCase))
            {
                return CashFlowClientPaymentMatchOutcome.Unchanged;
            }

            if (!dryRun)
            {
                await CallDataverseAppSendAsync(
                    $"/api/data/v9.2/{metadata.EntitySetName}({existing.Id})",
                    "PATCH",
                    payload,
                    ct);
            }

            return CashFlowClientPaymentMatchOutcome.Updated;
        }

        if (!dryRun)
        {
            await CallDataverseAppSendAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}",
                "POST",
                payload,
                ct);
        }

        return CashFlowClientPaymentMatchOutcome.Created;
    }

    private async Task<IReadOnlyDictionary<string, CashFlowClientPaymentMatchExistingRecord>> GetCashFlowClientPaymentMatchExistingIndexAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        CancellationToken ct)
    {
        if (!attributes.Contains(ClientPaymentMatchExternalKeyField))
            return new Dictionary<string, CashFlowClientPaymentMatchExistingRecord>(StringComparer.OrdinalIgnoreCase);

        var select = string.Join(",", new[]
            {
                metadata.PrimaryIdField,
                ClientPaymentMatchExternalKeyField,
                ClientPaymentMatchSourceHashField,
                ClientPaymentMatchStatusField
            }
            .Where(field => attributes.Contains(field) || string.Equals(field, metadata.PrimaryIdField, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$top=5000";
        var rows = await GetDataverseAppEntitiesAsync(url, ct);

        return rows
            .Select(row => new
            {
                Id = ReadString(row, metadata.PrimaryIdField).Trim(),
                Key = ReadString(row, ClientPaymentMatchExternalKeyField).Trim(),
                Hash = attributes.Contains(ClientPaymentMatchSourceHashField)
                    ? ReadString(row, ClientPaymentMatchSourceHashField).Trim()
                    : "",
                Status = attributes.Contains(ClientPaymentMatchStatusField)
                    ? ReadString(row, ClientPaymentMatchStatusField).Trim()
                    : ""
            })
            .Where(static row => !string.IsNullOrWhiteSpace(row.Id) && !string.IsNullOrWhiteSpace(row.Key))
            .GroupBy(static row => row.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group =>
                {
                    var first = group.First();
                    return new CashFlowClientPaymentMatchExistingRecord(first.Id, first.Hash, first.Status);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, object?> BuildCashFlowClientPaymentMatchPayload(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        CashFlowClientPaymentMatchRowDto row)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, metadata.PrimaryNameField, null, BuildCashFlowClientPaymentMatchPrimaryName(row), force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchTypeField, null, "PagoCliente", force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchStatusField, null, row.Status, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchConfidenceField, (int?)null, row.Confidence, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReasonField, null, row.Reason, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchDifferenceField, (decimal?)null, row.DifferenceValue, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchMovementIdField, null, row.MovementId, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchMovementExternalKeyField, null, row.MovementExternalKey, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchMovementDateField, null, row.MovementDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchSourceFlowField, null, row.SourceFlow, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchBankCodeField, null, row.BankAccountCode, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchBankNameField, null, row.BankAccountName, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchDescriptionField, null, row.Description, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchEntryField, (decimal?)null, row.EntryValue, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchInvoiceIdsField, null, row.InvoiceRecordIds, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchInvoiceNumbersField, null, row.InvoiceNumbers, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchClientField, null, row.ClientNames, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchInvoiceTotalField, (decimal?)null, row.InvoiceTotal, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPaymentValueField, (decimal?)null, row.EntryValue, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReteFteField, (decimal?)null, row.ReteFteValue, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReteIcaField, (decimal?)null, row.ReteIcaValue, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchRteIvaField, (decimal?)null, row.RteIvaValue, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchDraftJsonField, null, row.SiigoDraftJson, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchExternalKeyField, null, row.ExternalKey, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchSourceHashField, null, row.SourceHash, force: true);
        return payload;
    }

    private static CashFlowClientPaymentMovementRow? ParseCashFlowClientPaymentMovement(
        JsonElement item,
        RhEntityMetadata metadata)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, metadata.PrimaryIdField),
            ReadString(item, CashFlowMovementIdField)).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        return new CashFlowClientPaymentMovementRow
        {
            RecordId = recordId,
            ExternalKey = FirstNonEmpty(
                ReadString(item, CashFlowExternalKeyField),
                ReadString(item, CashFlowReferenceField),
                recordId),
            Date = ReadDateOnly(item, CashFlowDateField),
            SourceFlow = ReadString(item, CashFlowSourceFlowField).Trim(),
            BankAccountCode = ReadString(item, CashFlowBankAccountCodeField).Trim(),
            BankAccountName = FirstNonEmpty(
                ReadString(item, CashFlowBankAccountNameField),
                ReadString(item, CashFlowBankField)).Trim(),
            Description = ReadString(item, CashFlowDescriptionField).Trim(),
            EntryValue = RoundCurrency(ReadDecimal(item, CashFlowEntryField) ?? 0m),
            DataverseStatus = ReadString(item, CashFlowStatusField).Trim(),
            SiigoStatus = ReadString(item, CashFlowSiigoStatusField).Trim(),
            SiigoDocumentId = ReadString(item, CashFlowSiigoDocumentIdField).Trim()
        };
    }

    private static IReadOnlyDictionary<string, List<BillingRecordRow>> BuildCashFlowClientPaymentInvoiceIndex(
        IEnumerable<BillingRecordRow> invoices)
    {
        var result = new Dictionary<string, List<BillingRecordRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var invoice in invoices)
        {
            AddCashFlowClientPaymentInvoiceIndex(result, invoice.InvoiceNumber, invoice);
        }

        return result;
    }

    private static void AddCashFlowClientPaymentInvoiceIndex(
        IDictionary<string, List<BillingRecordRow>> index,
        string value,
        BillingRecordRow invoice)
    {
        var key = NormalizeDocumentKey(value);
        if (string.IsNullOrWhiteSpace(key))
            return;

        if (!index.TryGetValue(key, out var rows))
        {
            rows = new List<BillingRecordRow>();
            index[key] = rows;
        }

        if (!rows.Any(row => string.Equals(row.RecordId, invoice.RecordId, StringComparison.OrdinalIgnoreCase)))
            rows.Add(invoice);
    }

    private static IReadOnlyList<string> ExtractCashFlowClientPaymentInvoiceTokens(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return Array.Empty<string>();

        var tokens = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = CashFlowInvoiceTokenRegex.Matches(description).Cast<Match>().ToArray();

        for (var index = 0; index < matches.Length; index++)
        {
            var match = matches[index];
            var prefix = match.Groups["prefix"].Value.Trim().ToUpperInvariant();
            var series = match.Groups["series"].Value.Trim();
            var number = match.Groups["number"].Value.Trim();
            AddCashFlowClientPaymentInvoiceToken(tokens, seen, BuildCashFlowClientPaymentInvoiceToken(prefix, series, number));

            var nextMatchStart = index + 1 < matches.Length ? matches[index + 1].Index : description.Length;
            AddCashFlowClientPaymentInvoiceContinuationTokens(
                description,
                match.Index + match.Length,
                nextMatchStart,
                prefix,
                series,
                tokens,
                seen);
        }

        return tokens.ToArray();
    }

    private static void AddCashFlowClientPaymentInvoiceContinuationTokens(
        string description,
        int startIndex,
        int endIndex,
        string prefix,
        string series,
        ICollection<string> tokens,
        ISet<string> seen)
    {
        var index = startIndex;
        while (index < endIndex)
        {
            var remaining = description[index..endIndex];
            var separator = Regex.Match(
                remaining,
                @"^[\s,;/+&]+(?:(?:y|e)\s+)?",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(100));
            if (!separator.Success)
                break;

            index += separator.Length;
            if (index >= endIndex)
                break;

            remaining = description[index..endIndex];
            var numberMatch = Regex.Match(
                remaining,
                @"^(?<number>\d{3,})(?!\d)(?!\s*[-/]\s*\d)",
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
            if (!numberMatch.Success)
                break;

            AddCashFlowClientPaymentInvoiceToken(
                tokens,
                seen,
                BuildCashFlowClientPaymentInvoiceToken(prefix, series, numberMatch.Groups["number"].Value));
            index += numberMatch.Length;
        }
    }

    private static void AddCashFlowClientPaymentInvoiceToken(
        ICollection<string> tokens,
        ISet<string> seen,
        string token)
    {
        token = NormalizeDocumentToken(token);
        if (!string.IsNullOrWhiteSpace(token) && seen.Add(token))
            tokens.Add(token);
    }

    private static string BuildCashFlowClientPaymentInvoiceToken(string prefix, string series, string number)
    {
        return string.IsNullOrWhiteSpace(series)
            ? $"{prefix}-{number}"
            : $"{prefix}-{series}-{number}";
    }

    private static string NormalizeDocumentToken(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        normalized = Regex.Replace(normalized, @"\s+", "-", RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"-+", "-", RegexOptions.CultureInvariant);
        return normalized.Trim('-');
    }

    private static decimal ResolveCashFlowClientPaymentReteFteValue(BillingRecordRow invoice)
    {
        if (invoice.RteFteValue <= 0m)
            return 0m;

        return invoice.RteFteValue <= 1m
            ? CalculateRegistroPagoReteFteValue(invoice.NetTotalInvoice, invoice.NetVatValue, invoice.RteFteValue)
            : RoundCurrency(invoice.RteFteValue);
    }

    private static decimal ResolveCashFlowClientPaymentReteIcaValue(BillingRecordRow invoice)
    {
        if (invoice.ReteIcaValue <= 0m)
            return 0m;

        return invoice.ReteIcaValue <= 50m
            ? CalculateRegistroPagoReteIcaValue(invoice.NetTotalInvoice, invoice.NetVatValue, invoice.ReteIcaValue)
            : RoundCurrency(invoice.ReteIcaValue);
    }

    private static decimal ResolveCashFlowClientPaymentRteIvaValue(BillingRecordRow invoice)
    {
        if (invoice.RteIvaValue <= 0m)
            return 0m;

        return invoice.RteIvaValue <= 1m
            ? CalculateRegistroPagoRteIvaValue(invoice.NetTotalInvoice, invoice.NetVatValue, invoice.RteIvaValue)
            : RoundCurrency(invoice.RteIvaValue);
    }

    private static string BuildCashFlowClientPaymentSiigoDraftJson(
        CashFlowClientPaymentMatchRowDto row,
        IReadOnlyList<BillingRecordRow> invoices)
    {
        var lines = new List<object>();
        if (row.EntryValue > 0m)
        {
            lines.Add(new
            {
                accountCode = row.BankAccountCode,
                accountName = row.BankAccountName,
                description = row.BankAccountName,
                debit = row.EntryValue,
                credit = 0m
            });
        }

        if (row.ReteFteValue > 0m)
        {
            lines.Add(new
            {
                accountCode = "13551513",
                accountName = "Retencion en la fuente",
                description = "Retefuente",
                debit = row.ReteFteValue,
                credit = 0m
            });
        }

        if (row.ReteIcaValue > 0m)
        {
            lines.Add(new
            {
                accountCode = "13551805",
                accountName = "Rete ICA",
                description = "ReteICA",
                debit = row.ReteIcaValue,
                credit = 0m
            });
        }

        if (row.RteIvaValue > 0m)
        {
            lines.Add(new
            {
                accountCode = "13551701",
                accountName = "Rete IVA",
                description = "RteIVA",
                debit = row.RteIvaValue,
                credit = 0m
            });
        }

        if (row.InvoiceTotal > 0m)
        {
            lines.Add(new
            {
                accountCode = "13050501",
                accountName = "Clientes nacionales",
                thirdParty = row.ClientNames,
                detail = row.InvoiceNumbers,
                description = "Clientes nacionales",
                debit = 0m,
                credit = row.InvoiceTotal
            });
        }

        if (row.DifferenceValue != 0m && Math.Abs(row.DifferenceValue) <= RegistroPagosClientesBalancedTolerance)
        {
            lines.Add(new
            {
                accountCode = "42958101",
                accountName = "Ajuste al peso",
                description = "Ajuste al peso",
                debit = row.DifferenceValue > 0m ? row.DifferenceValue : 0m,
                credit = row.DifferenceValue < 0m ? Math.Abs(row.DifferenceValue) : 0m
            });
        }

        var draft = new
        {
            type = "ComprobanteIngresoSiigoBorrador",
            source = "cash-flow-client-payment",
            status = row.Status,
            confidence = row.Confidence,
            requiresReview = !IsCashFlowClientPaymentSuggested(row.Status)
                || row.RteIvaValue > 0m
                || invoices.Count == 0,
            movement = new
            {
                id = row.MovementId,
                externalKey = row.MovementExternalKey,
                date = row.MovementDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                sourceFlow = row.SourceFlow,
                description = row.Description,
                entry = row.EntryValue,
                bankAccountCode = row.BankAccountCode,
                bankAccountName = row.BankAccountName
            },
            invoices = invoices.Select(invoice => new
            {
                recordId = invoice.RecordId,
                number = invoice.InvoiceNumber,
                client = invoice.ClientName,
                total = invoice.NetTotalInvoice,
                vat = invoice.NetVatValue,
                reteFtePercent = invoice.RteFteValue,
                reteIcaRate = invoice.ReteIcaValue,
                rteIvaPercent = invoice.RteIvaValue
            }).ToArray(),
            totals = new
            {
                invoiceTotal = row.InvoiceTotal,
                payment = row.EntryValue,
                reteFte = row.ReteFteValue,
                reteIca = row.ReteIcaValue,
                rteIva = row.RteIvaValue,
                retentions = row.RetentionsTotal,
                difference = row.DifferenceValue
            },
            lines
        };

        return JsonSerializer.Serialize(draft, JsonOptions);
    }

    private static string BuildCashFlowClientPaymentExternalKey(CashFlowClientPaymentMatchRowDto row)
    {
        var tokenPart = row.InvoiceTokens.Count == 0
            ? "no-invoice-token"
            : string.Join("-", row.InvoiceTokens.Select(NormalizeDocumentKey).Where(static token => !string.IsNullOrWhiteSpace(token)));
        if (string.IsNullOrWhiteSpace(tokenPart))
            tokenPart = "invoice-token-empty";

        return $"cashflow-client-payment:{NormalizeCashFlowClientPaymentKey(row.MovementExternalKey)}:{NormalizeCashFlowClientPaymentKey(tokenPart)}";
    }

    private static string BuildCashFlowClientPaymentSourceHash(CashFlowClientPaymentMatchRowDto row)
    {
        var raw = string.Join("|", new[]
        {
            row.MovementId,
            row.MovementExternalKey,
            row.MovementDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            row.SourceFlow,
            row.BankAccountCode,
            row.BankAccountName,
            row.Description,
            row.EntryValue.ToString("0.##", CultureInfo.InvariantCulture),
            string.Join(",", row.InvoiceTokens),
            row.InvoiceRecordIds,
            row.InvoiceNumbers,
            row.ClientNames,
            row.InvoiceTotal.ToString("0.##", CultureInfo.InvariantCulture),
            row.ReteFteValue.ToString("0.##", CultureInfo.InvariantCulture),
            row.ReteIcaValue.ToString("0.##", CultureInfo.InvariantCulture),
            row.RteIvaValue.ToString("0.##", CultureInfo.InvariantCulture),
            row.DifferenceValue.ToString("0.##", CultureInfo.InvariantCulture),
            row.Status,
            row.Reason,
            row.Confidence.ToString(CultureInfo.InvariantCulture),
            row.SiigoDraftJson
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static string BuildCashFlowClientPaymentMatchPrimaryName(CashFlowClientPaymentMatchRowDto row)
    {
        var date = row.MovementDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Sin fecha";
        var invoice = string.IsNullOrWhiteSpace(row.InvoiceNumbers)
            ? string.Join(", ", row.InvoiceTokens)
            : row.InvoiceNumbers;
        if (string.IsNullOrWhiteSpace(invoice))
            invoice = "Sin factura";

        return TruncateAccountCatalogText($"{date} {row.SourceFlow} {invoice} {row.EntryValue:0.##}".Trim(), 100);
    }

    private static string JoinDistinctCashFlowClientPaymentValues(IEnumerable<string> values)
    {
        return TruncateAccountCatalogText(
            string.Join(" | ", values
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)),
            1000);
    }

    private static string NormalizeCashFlowClientPaymentKey(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
            else if (ch is ':' or '-' or '_' or '.')
                builder.Append('-');
        }

        var key = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(key) ? "na" : key;
    }

    private static string BuildCashFlowClientPaymentMovementSelect(
        RhEntityMetadata metadata,
        ISet<string> attributes)
    {
        return string.Join(",", new[]
            {
                metadata.PrimaryIdField,
                metadata.PrimaryNameField,
                CashFlowDateField,
                CashFlowDescriptionField,
                CashFlowEntryField,
                CashFlowExternalKeyField,
                CashFlowReferenceField,
                CashFlowSourceFlowField,
                CashFlowBankAccountCodeField,
                CashFlowBankAccountNameField,
                CashFlowBankField,
                CashFlowStatusField,
                CashFlowSiigoStatusField,
                CashFlowSiigoDocumentIdField
            }
            .Where(field => !string.IsNullOrWhiteSpace(field)
                && (attributes.Contains(field)
                    || string.Equals(field, metadata.PrimaryIdField, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(field, metadata.PrimaryNameField, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static HashSet<string> BuildCashFlowClientPaymentMovementAttributeSet(
        RhEntityMetadata metadata,
        ISet<string> attributes)
    {
        if (attributes.Count > 0)
            return new HashSet<string>(attributes.Concat(new[] { metadata.PrimaryIdField, metadata.PrimaryNameField }), StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            CashFlowDateField,
            CashFlowDescriptionField,
            CashFlowEntryField,
            CashFlowExternalKeyField,
            CashFlowReferenceField,
            CashFlowSourceFlowField,
            CashFlowBankAccountCodeField,
            CashFlowBankAccountNameField,
            CashFlowBankField,
            CashFlowStatusField,
            CashFlowSiigoStatusField,
            CashFlowSiigoDocumentIdField
        }, StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> BuildCashFlowClientPaymentMatchAttributeSet(
        RhEntityMetadata metadata,
        ISet<string> attributes)
    {
        if (attributes.Count > 0)
            return new HashSet<string>(attributes.Concat(new[] { metadata.PrimaryIdField, metadata.PrimaryNameField }), StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            ClientPaymentMatchTypeField,
            ClientPaymentMatchStatusField,
            ClientPaymentMatchConfidenceField,
            ClientPaymentMatchReasonField,
            ClientPaymentMatchDifferenceField,
            ClientPaymentMatchMovementIdField,
            ClientPaymentMatchMovementExternalKeyField,
            ClientPaymentMatchMovementDateField,
            ClientPaymentMatchSourceFlowField,
            ClientPaymentMatchBankCodeField,
            ClientPaymentMatchBankNameField,
            ClientPaymentMatchDescriptionField,
            ClientPaymentMatchEntryField,
            ClientPaymentMatchInvoiceIdsField,
            ClientPaymentMatchInvoiceNumbersField,
            ClientPaymentMatchClientField,
            ClientPaymentMatchInvoiceTotalField,
            ClientPaymentMatchPaymentValueField,
            ClientPaymentMatchReteFteField,
            ClientPaymentMatchReteIcaField,
            ClientPaymentMatchRteIvaField,
            ClientPaymentMatchDraftJsonField,
            ClientPaymentMatchExternalKeyField,
            ClientPaymentMatchSourceHashField
        }, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsCashFlowClientPaymentSuggested(string status) =>
        string.Equals(status, "Sugerido", StringComparison.OrdinalIgnoreCase);

    private static bool IsCashFlowClientPaymentMovementExcluded(CashFlowClientPaymentMovementRow row) =>
        !string.IsNullOrWhiteSpace(row.SiigoDocumentId)
        || string.Equals(row.DataverseStatus, ConciliacionCashFlowPendingReviewStatus, StringComparison.OrdinalIgnoreCase)
        || string.Equals(row.DataverseStatus, ConciliacionCashFlowOmittedStatus, StringComparison.OrdinalIgnoreCase)
        || string.Equals(row.DataverseStatus, "Conciliado", StringComparison.OrdinalIgnoreCase)
        || string.Equals(row.DataverseStatus, "EnviadoSiigo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(row.SiigoStatus, "Conciliado", StringComparison.OrdinalIgnoreCase)
        || string.Equals(row.SiigoStatus, "EnviadoSiigo", StringComparison.OrdinalIgnoreCase);

    private static bool IsCashFlowClientPaymentProtectedMatchStatus(string? status) =>
        string.Equals(status, "Aprobado", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "ListoSiigo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "AplicadoDataverse", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "EnviadoSiigo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Conciliado", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "ErrorSiigo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "ReasignadoCategoria", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Omitido", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Rechazado", StringComparison.OrdinalIgnoreCase);

    private sealed class CashFlowClientPaymentMovementRow
    {
        public string RecordId { get; set; } = "";
        public string ExternalKey { get; set; } = "";
        public DateOnly? Date { get; set; }
        public string SourceFlow { get; set; } = "";
        public string BankAccountCode { get; set; } = "";
        public string BankAccountName { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal EntryValue { get; set; }
        public string DataverseStatus { get; set; } = "";
        public string SiigoStatus { get; set; } = "";
        public string SiigoDocumentId { get; set; } = "";
    }

    private enum CashFlowClientPaymentMatchOutcome
    {
        Created,
        Updated,
        Unchanged,
        Skipped
    }

    private sealed record CashFlowClientPaymentMatchExistingRecord(string Id, string SourceHash, string Status);
}
