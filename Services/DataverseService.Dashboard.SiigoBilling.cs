using System.Globalization;
using System.Security.Claims;
using CotizadorInterno.Web.Models.Reconciliation;
using Microsoft.Extensions.Caching.Memory;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string SiigoBillingCacheKeyPrefix = "DataverseService.Dashboard.SiigoBilling";

    private async Task<List<BillingRecordRow>> GetSiigoRevenueLedgerRowsAsync(
        RhEntityMetadata metadata,
        DateOnly startInclusive,
        DateOnly endExclusive,
        ClaimsPrincipal? user,
        CancellationToken ct,
        IReadOnlyList<BillingRecordRow>? knownDimensionRows = null)
    {
        if (startInclusive >= endExclusive)
            return new List<BillingRecordRow>();

        var movements = new List<BillingRevenueMovement>();
        var lastIncludedDate = endExclusive.AddDays(-1);
        for (var year = startInclusive.Year; year <= lastIncludedDate.Year; year++)
        {
            var documents = await GetCachedSiigoBillingDocumentsAsync(year, ct);
            var yearStart = new DateOnly(year, 1, 1);
            var yearEnd = yearStart.AddYears(1);
            var periodStart = startInclusive > yearStart ? startInclusive : yearStart;
            var periodEnd = endExclusive < yearEnd ? endExclusive : yearEnd;
            if (periodStart < periodEnd)
                movements.AddRange(BillingRevenueLedger.Build(documents, periodStart, periodEnd));
        }

        var dimensionRows = knownDimensionRows is null
            ? await GetBillingRecordsAsync(
                metadata,
                startInclusive,
                endExclusive,
                _dashboardBillingEmissionDateField,
                _dashboardBillingEmissionDateFieldKind,
                user,
                ct,
                applyCreditNotes: false)
            : knownDimensionRows.ToList();

        var dimensionIndex = BuildBillingDimensionIndex(dimensionRows);
        var hasUnmatchedHistoricalReferences = movements
            .Where(static movement => movement.IsCreditNote)
            .Any(movement => FindBillingDimension(movement, dimensionIndex) is null);

        if (hasUnmatchedHistoricalReferences && startInclusive > new DateOnly(2000, 1, 1))
        {
            var historyStart = startInclusive.Year <= 2003
                ? new DateOnly(2000, 1, 1)
                : startInclusive.AddYears(-3);
            var historicalRows = await GetBillingRecordsAsync(
                metadata,
                historyStart,
                startInclusive,
                _dashboardBillingEmissionDateField,
                _dashboardBillingEmissionDateFieldKind,
                user,
                ct,
                applyCreditNotes: false);

            foreach (var historicalRow in historicalRows)
                AddBillingDimension(dimensionIndex, historicalRow);
        }

        var ledgerRows = movements
            .Select(movement => MapSiigoMovementToBillingRow(
                movement,
                FindBillingDimension(movement, dimensionIndex)))
            .ToList();

        var unmatched = ledgerRows.Count(static row => !row.HasDataverseDimensionMatch);
        if (unmatched > 0)
        {
            _logger.LogWarning(
                "El ledger de facturacion Siigo incluyo {UnmatchedCount} de {MovementCount} movimientos sin dimension de Dataverse entre {StartDate} y {EndDate}. Los valores permanecen en el total global y se clasifican como Sin asignar.",
                unmatched,
                ledgerRows.Count,
                startInclusive,
                endExclusive);
        }

        return ledgerRows;
    }

    private async Task<SiigoFinancialReconciliationData> GetCachedSiigoBillingDocumentsAsync(
        int year,
        CancellationToken ct)
    {
        var today = GetBogotaToday();
        if (year > today.Year)
            return new SiigoFinancialReconciliationData();

        var cacheKey = $"{SiigoBillingCacheKeyPrefix}:{year}";
        if (_memoryCache.TryGetValue(cacheKey, out SiigoFinancialReconciliationData? cached)
            && cached is not null)
        {
            return cached;
        }

        var startInclusive = new DateOnly(year, 1, 1);
        var endExclusive = year == today.Year
            ? today.AddDays(1)
            : startInclusive.AddYears(1);
        var documents = await _siigoService.GetBillingDocumentsAsync(startInclusive, endExclusive, ct);
        var lifetime = year == today.Year
            ? TimeSpan.FromMinutes(2)
            : TimeSpan.FromMinutes(30);

        _memoryCache.Set(
            cacheKey,
            documents,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = lifetime,
                Size = null
            });

        return documents;
    }

    private static BillingDimensionIndex BuildBillingDimensionIndex(IEnumerable<BillingRecordRow> rows)
    {
        var index = new BillingDimensionIndex();
        foreach (var row in rows)
            AddBillingDimension(index, row);

        return index;
    }

    private static void AddBillingDimension(BillingDimensionIndex index, BillingRecordRow row)
    {
        AddPreferredBillingDimension(index.BySiigoId, row.SiigoInvoiceId, row);
        AddPreferredBillingDimension(index.ByDocumentName, NormalizeDocumentKey(row.InvoiceNumber), row);
        AddPreferredBillingDimension(index.ByDocumentName, NormalizeDocumentKey(row.SiigoInvoiceName), row);
        AddPreferredBillingDimension(
            index.ByPrefixAndNumber,
            BuildPrefixNumberKey(row.InvoicePrefix, row.InvoiceCode),
            row);
    }

    private static void AddPreferredBillingDimension(
        IDictionary<string, BillingRecordRow> index,
        string? key,
        BillingRecordRow candidate)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        var normalizedKey = key.Trim();
        if (!index.TryGetValue(normalizedKey, out var current)
            || ScoreBillingDimension(candidate) > ScoreBillingDimension(current))
        {
            index[normalizedKey] = candidate;
        }
    }

    private static int ScoreBillingDimension(BillingRecordRow row)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(row.ClientId)) score += 4;
        if (!string.IsNullOrWhiteSpace(row.ClientName)) score += 2;
        if (row.VerticalOptionValue != 0) score += 2;
        if (row.ContractTypeOptionValue != 0) score += 1;
        if (!string.IsNullOrWhiteSpace(row.BusinessGroupId)) score += 1;
        return score;
    }

    private static BillingRecordRow? FindBillingDimension(
        BillingRevenueMovement movement,
        BillingDimensionIndex index)
    {
        if (!string.IsNullOrWhiteSpace(movement.InvoiceId)
            && index.BySiigoId.TryGetValue(movement.InvoiceId.Trim(), out var bySiigoId))
        {
            return bySiigoId;
        }

        var documentKey = NormalizeDocumentKey(movement.InvoiceName);
        if (!string.IsNullOrWhiteSpace(documentKey)
            && index.ByDocumentName.TryGetValue(documentKey, out var byDocumentName))
        {
            return byDocumentName;
        }

        var prefixAndNumber = BuildPrefixNumberKey(
            movement.InvoicePrefix,
            movement.InvoiceNumber?.ToString(CultureInfo.InvariantCulture));
        return !string.IsNullOrWhiteSpace(prefixAndNumber)
               && index.ByPrefixAndNumber.TryGetValue(prefixAndNumber, out var byPrefixAndNumber)
            ? byPrefixAndNumber
            : null;
    }

    private static BillingRecordRow MapSiigoMovementToBillingRow(
        BillingRevenueMovement movement,
        BillingRecordRow? dimension)
    {
        var documentFallback = $"{movement.DocumentDate:yyyyMMdd}:{movement.DocumentName}";
        return new BillingRecordRow
        {
            RecordId = $"siigo:{(movement.IsCreditNote ? "nc" : "fv")}:{FirstNonEmpty(movement.DocumentId, documentFallback)}",
            InvoiceNumber = FirstNonEmpty(movement.DocumentName, movement.InvoiceName, documentFallback),
            SiigoInvoiceId = movement.InvoiceId,
            SiigoInvoiceName = movement.InvoiceName,
            InvoicePrefix = movement.InvoicePrefix,
            InvoiceCode = movement.InvoiceNumber?.ToString(CultureInfo.InvariantCulture) ?? "",
            ClientId = dimension?.ClientId ?? "",
            CompanyTaxId = FirstNonEmpty(dimension?.CompanyTaxId, movement.CustomerIdentification),
            ClientName = FirstNonEmpty(dimension?.ClientName, movement.CustomerIdentification, "Sin cliente"),
            BusinessGroupId = dimension?.BusinessGroupId ?? "",
            BusinessGroupName = FirstNonEmpty(dimension?.BusinessGroupName, "Sin grupo"),
            VerticalLabel = FirstNonEmpty(dimension?.VerticalLabel, "Sin vertical"),
            ContractTypeLabel = FirstNonEmpty(dimension?.ContractTypeLabel, "Sin contrato"),
            VerticalOptionValue = dimension?.VerticalOptionValue ?? 0,
            ContractTypeOptionValue = dimension?.ContractTypeOptionValue ?? 0,
            DueDate = movement.IsCreditNote ? null : dimension?.DueDate,
            EmissionDate = movement.DocumentDate,
            TotalInvoice = movement.GrossTotal,
            VatPercent = dimension?.VatPercent ?? 0m,
            VatValue = movement.Vat,
            PublicUrl = dimension?.PublicUrl ?? "",
            UsesSiigoRevenueLedger = true,
            IsCreditNoteLedgerEntry = movement.IsCreditNote,
            SuggestedWithholdingTotal = movement.SuggestedWithholdingTotal,
            HasDataverseDimensionMatch = dimension is not null
        };
    }

    private sealed class BillingDimensionIndex
    {
        public Dictionary<string, BillingRecordRow> BySiigoId { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, BillingRecordRow> ByDocumentName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, BillingRecordRow> ByPrefixAndNumber { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
