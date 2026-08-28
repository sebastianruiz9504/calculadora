using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.Automation;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Reconciliation;

namespace CotizadorInterno.Web.Services;

public sealed class DianSupplierInvoiceAutomationService : IDianSupplierInvoiceAutomationService
{
    private const string AmbiguousWriteHoldMarker = "[SIIGO_WRITE_AMBIGUOUS]";
    private static readonly SemaphoreSlim ProcessLock = new(1, 1);
    private static readonly HashSet<string> SuccessfulStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "AlreadyImported",
        "ExistingLinked",
        "Created",
        "RecoveredAfterAmbiguousError"
    };

    private readonly IDataverseService _dataverse;
    private readonly ISiigoService _siigo;
    private readonly IDianSupplierPurchasePayloadFactory _payloadFactory;
    private readonly ILogger<DianSupplierInvoiceAutomationService> _logger;

    public DianSupplierInvoiceAutomationService(
        IDataverseService dataverse,
        ISiigoService siigo,
        IDianSupplierPurchasePayloadFactory payloadFactory,
        ILogger<DianSupplierInvoiceAutomationService> logger)
    {
        _dataverse = dataverse;
        _siigo = siigo;
        _payloadFactory = payloadFactory;
        _logger = logger;
    }

    public async Task<DianSupplierInvoiceAutomationResultDto> ProcessPeriodAsync(
        DateOnly periodStart,
        bool dryRun = false,
        string? supplierKey = null,
        IReadOnlySet<string>? externalKeys = null,
        CancellationToken ct = default)
    {
        if (periodStart.Day != 1)
            throw new InvalidOperationException("La automatizacion DIAN requiere el primer dia del mes como inicio del periodo.");

        var requestedSupplierKey = string.IsNullOrWhiteSpace(supplierKey)
            ? ""
            : ExtractDigits(supplierKey);
        if (!string.IsNullOrWhiteSpace(supplierKey) && requestedSupplierKey.Length < 5)
            throw new InvalidOperationException("La llave del proveedor no contiene un NIT/identificacion valido.");

        await ProcessLock.WaitAsync(ct);
        try
        {
            return await ProcessPeriodCoreAsync(periodStart, dryRun, requestedSupplierKey, externalKeys, ct);
        }
        finally
        {
            ProcessLock.Release();
        }
    }

    private async Task<DianSupplierInvoiceAutomationResultDto> ProcessPeriodCoreAsync(
        DateOnly periodStart,
        bool dryRun,
        string requestedSupplierKey,
        IReadOnlySet<string>? externalKeys,
        CancellationToken ct)
    {
        var periodEndExclusive = periodStart.AddMonths(1);
        var result = new DianSupplierInvoiceAutomationResultDto
        {
            DryRun = dryRun,
            PeriodStart = periodStart,
            PeriodEndExclusive = periodEndExclusive,
            SupplierKey = requestedSupplierKey,
            Status = "Running"
        };
        var rowResults = new List<DianSupplierInvoiceAutomationRowResultDto>();
        var pendingSuppliers = new List<DianSupplierAutomationMissingSupplierDto>();

        var queriedRows = await _dataverse.GetConciliacionDianSupplierDocumentsForAutomationAsync(
            periodStart,
            periodEndExclusive,
            ct);
        result.RowsReviewed = queriedRows.Count;

        var eligibleRows = queriedRows
            .Where(row => IsEligibleReceivedElectronicInvoice(row, periodStart, periodEndExclusive))
            .Where(row => string.IsNullOrWhiteSpace(requestedSupplierKey)
                || SupplierRequestMatches(row, requestedSupplierKey))
            .Where(row => externalKeys is null
                || externalKeys.Count == 0
                || externalKeys.Contains(row.ExcelKey))
            .OrderBy(static row => row.EmissionDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.SupplierNit, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        result.EligibleRows = eligibleRows.Length;
        result.Eligible = eligibleRows.Length;
        result.FilteredOutRows = Math.Max(0, queriedRows.Count - eligibleRows.Length);

        if (eligibleRows.Length == 0)
        {
            result.CanComplete = true;
            result.IsComplete = !dryRun;
            result.Completed = !dryRun;
            result.Status = dryRun ? "DryRunReady" : "Completed";
            result.Message = string.IsNullOrWhiteSpace(requestedSupplierKey)
                ? "No hay facturas electronicas recibidas elegibles en el periodo."
                : "No hay facturas electronicas recibidas elegibles para el proveedor indicado.";
            return result;
        }

        var missingBusinessKeyRows = eligibleRows
            .Where(static row => string.IsNullOrWhiteSpace(row.SiigoBusinessKey))
            .ToArray();
        foreach (var missingBusinessKeyRow in missingBusinessKeyRows)
        {
            const string missingBusinessKeyMessage = "La factura no tiene SiigoBusinessKey durable. Reimporta el Excel DIAN despues de ejecutar el aprovisionamiento; no se publicara en Siigo.";
            if (!dryRun)
                await PersistFailureBestEffortAsync(missingBusinessKeyRow.RecordId, missingBusinessKeyMessage, ct);
            rowResults.Add(BuildRowResult(
                missingBusinessKeyRow,
                "MissingBusinessIdentity",
                missingBusinessKeyMessage));
        }

        var processableRows = eligibleRows
            .Where(static row => !string.IsNullOrWhiteSpace(row.SiigoBusinessKey))
            .ToArray();
        if (processableRows.Length == 0)
        {
            CompleteResult(result, rowResults, pendingSuppliers);
            return result;
        }

        var duplicateBusinessRows = processableRows
            .Select(row => new { Row = row, Key = BuildDianBusinessIdentityKey(row) })
            .Where(static item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group
                .Select(item => item.Row.Cufe)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1)
            .SelectMany(static group => group.Select(static item => item.Row))
            .ToArray();
        var duplicateSiigoLinkRows = processableRows
            .Where(static row => !string.IsNullOrWhiteSpace(row.SiigoDocumentId))
            .GroupBy(static row => row.SiigoDocumentId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group
                .Select(row => row.Cufe)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1)
            .SelectMany(static group => group)
            .ToArray();
        var duplicateIdentityRows = duplicateBusinessRows
            .Concat(duplicateSiigoLinkRows)
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        if (duplicateIdentityRows.Length > 0)
        {
            const string duplicateMessage = "Se detectaron CUFE distintos para la misma identidad de factura o el mismo SiigoDocumentId. El job se bloqueo antes de crear/vincular compras; corrige el duplicado en Dataverse.";
            foreach (var duplicateRow in duplicateIdentityRows)
            {
                if (!dryRun)
                {
                    await PersistFailureBestEffortAsync(
                        duplicateRow.RecordId,
                        duplicateMessage,
                        ct);
                }

                rowResults.Add(BuildRowResult(
                    duplicateRow,
                    "DuplicateInvoiceIdentity",
                    duplicateMessage,
                    CanonicalSupplierKey(duplicateRow.SupplierNit, IsLikelyCompanySupplier(duplicateRow))));
            }

            CompleteResult(result, rowResults, pendingSuppliers);
            return result;
        }

        var rowsPendingSiigo = processableRows
            .Where(static row => string.IsNullOrWhiteSpace(row.SiigoDocumentId))
            .ToArray();

        var (purchaseLookupStart, purchaseLookupEnd) = ResolvePurchaseLookupRange(
            processableRows,
            periodStart,
            periodEndExclusive.AddDays(-1));

        List<SiigoReconciliationPurchase> purchases;
        try
        {
            purchases = (await _siigo.GetPurchasesByDateRangeAsync(
                    purchaseLookupStart,
                    purchaseLookupEnd,
                    ct))
                .ToList();
        }
        catch (Exception ex)
        {
            var detail = BuildExceptionDetail(ex);
            _logger.LogWarning(ex, "No se pudo precargar compras Siigo para automatizacion DIAN {PeriodStart}.", periodStart);
            foreach (var row in processableRows)
            {
                var hasAmbiguousWriteHold = HasAmbiguousWriteHold(row)
                    || row.AutomationState.Equals("ProcesandoSiigo", StringComparison.OrdinalIgnoreCase);
                var message = hasAmbiguousWriteHold
                    ? $"{AmbiguousWriteHoldMarker} Sigue pendiente confirmar el resultado anterior en Siigo; no se reintentara el POST hasta poder verificarlo. {detail}"
                    : $"No se publico porque no fue posible verificar duplicados en Siigo. {detail}";
                if (!dryRun)
                    await PersistFailureBestEffortAsync(row.RecordId, message, ct);
                rowResults.Add(BuildRowResult(
                    row,
                    hasAmbiguousWriteHold ? "AmbiguousWritePending" : "PurchaseLookupFailed",
                    message));
            }

            CompleteResult(result, rowResults, pendingSuppliers);
            return result;
        }

        foreach (var row in processableRows.Where(static item => !string.IsNullOrWhiteSpace(item.SiigoDocumentId)))
        {
            var allowSupplierCheckDigit = IsLikelyCompanySupplier(row)
                || !string.IsNullOrWhiteSpace(row.SiigoSupplierId)
                && TryCanonicalColombianNit(row.SupplierNit, out _);
            var supplierKey = CanonicalSupplierKey(row.SupplierNit, allowSupplierCheckDigit);
            var identity = _payloadFactory.ResolveIdentity(row);
            var businessMatches = FindExistingPurchaseMatches(
                purchases,
                supplierKey,
                identity,
                allowSupplierCheckDigit);
            var existing = businessMatches.FirstOrDefault(purchase => string.Equals(
                purchase.Id?.Trim(), row.SiigoDocumentId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing is null
                || businessMatches.Count != 1
                || !PurchaseValuesMatch(existing, row))
            {
                var message = $"Dataverse referencia la compra Siigo {row.SiigoDocumentId}, pero no coincide de forma unica con NIT, prefijo, folio, fecha y total DIAN. El job queda pendiente y no publicara otra factura.";
                if (!dryRun)
                    await PersistFailureBestEffortAsync(row.RecordId, $"{AmbiguousWriteHoldMarker} {message}", ct);
                rowResults.Add(BuildRowResult(
                    row,
                    "ExistingReferenceConflict",
                    message,
                    supplierKey,
                    row.SiigoDocumentId,
                    row.SiigoDocumentName));
                continue;
            }

            if (!dryRun)
            {
                try
                {
                    await _dataverse.MarkConciliacionDianSupplierDocumentSiigoResultAsync(
                        row.RecordId,
                        success: true,
                        message: "La compra Siigo asociada fue verificada nuevamente por NIT, prefijo, folio, fecha y total.",
                        siigoId: existing.Id,
                        siigoName: existing.Name,
                        ct: ct);
                }
                catch (Exception ex)
                {
                    var message = $"La compra Siigo coincide, pero Dataverse no confirmo el vinculo durable. {BuildExceptionDetail(ex)}";
                    rowResults.Add(BuildRowResult(
                        row,
                        "ExistingVerificationPersistenceFailed",
                        message,
                        supplierKey,
                        existing.Id,
                        existing.Name));
                    continue;
                }
            }

            rowResults.Add(BuildRowResult(
                row,
                "AlreadyImported",
                "La factura tiene una compra Siigo asociada y verificada.",
                supplierKey,
                existing.Id,
                FirstNonEmpty(existing.Name, row.SiigoDocumentName)));
        }

        if (rowsPendingSiigo.Length == 0)
        {
            CompleteResult(result, rowResults, pendingSuppliers);
            return result;
        }

        Task<PurchaseCatalogs>? catalogsTask = null;
        Task<PurchaseCatalogs> GetCatalogsAsync()
        {
            return catalogsTask ??= LoadPurchaseCatalogsAsync(ct);
        }

        var groups = rowsPendingSiigo
            .GroupBy(row => CanonicalSupplierKey(row.SupplierNit, IsLikelyCompanySupplier(row)), StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        result.SupplierGroupsReviewed = groups.Length;

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            var groupRows = group.ToArray();
            var canonicalKey = group.Key;
            var allowSupplierCheckDigit = groupRows.Any(IsLikelyCompanySupplier);
            if (canonicalKey.Length < 5)
            {
                foreach (var row in groupRows)
                {
                    const string message = "La factura no tiene un NIT/identificacion de proveedor valido.";
                    await PersistFailureBestEffortAsync(row.RecordId, message, ct);
                    rowResults.Add(BuildRowResult(row, "InvalidSupplier", message));
                }
                continue;
            }

            SiigoCustomerLookupItemDto? supplier;
            try
            {
                var candidates = await _siigo.SearchCustomersAsync(canonicalKey, top: 50, ct);
                supplier = candidates.FirstOrDefault(candidate =>
                    candidate.Active
                    && string.Equals(
                        CanonicalSupplierKey(candidate.Identification, allowSupplierCheckDigit),
                        canonicalKey,
                        StringComparison.OrdinalIgnoreCase));

                var associatedSupplierIds = groupRows
                    .Select(static row => row.SiigoSupplierId?.Trim() ?? "")
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (supplier is null
                    && associatedSupplierIds.Length == 1
                    && TryCanonicalColombianNit(canonicalKey, out var associatedBaseNit)
                    && !string.Equals(associatedBaseNit, canonicalKey, StringComparison.Ordinal))
                {
                    var associatedCandidates = await _siigo.SearchCustomersAsync(associatedBaseNit, top: 50, ct);
                    supplier = associatedCandidates.FirstOrDefault(candidate =>
                        candidate.Active
                        && string.Equals(candidate.Id, associatedSupplierIds[0], StringComparison.OrdinalIgnoreCase)
                        && string.Equals(ExtractDigits(candidate.Identification), associatedBaseNit, StringComparison.Ordinal));
                }
                else if (supplier is null && associatedSupplierIds.Length > 1)
                {
                    throw new InvalidOperationException(
                        "Dataverse tiene mas de un proveedor Siigo asociado al mismo NIT DIAN; se requiere revision manual.");
                }
            }
            catch (Exception ex)
            {
                result.SupplierLookupFailed++;
                var detail = BuildExceptionDetail(ex);
                _logger.LogWarning(ex, "Fallo consulta de proveedor Siigo {SupplierKey} en automatizacion DIAN.", canonicalKey);
                foreach (var row in groupRows)
                {
                    var message = $"No se publico porque fallo la validacion del proveedor en Siigo. {detail}";
                    await PersistFailureBestEffortAsync(row.RecordId, message, ct);
                    rowResults.Add(BuildRowResult(row, "SupplierLookupFailed", message, canonicalKey));
                }
                continue;
            }

            if (supplier is null)
            {
                result.SupplierGroupsMissing++;
                pendingSuppliers.Add(BuildPendingSupplier(canonicalKey, groupRows));
                foreach (var row in groupRows)
                {
                    rowResults.Add(BuildRowResult(
                        row,
                        "PendingSupplier",
                        "Proveedor no encontrado en Siigo; queda pendiente en la bandeja de creacion.",
                        canonicalKey));
                }
                continue;
            }

            result.SupplierGroupsFound++;
            var supplierLabel = FirstNonEmpty(
                supplier.DisplayName,
                supplier.Name,
                supplier.CommercialName,
                supplier.Identification);
            var purchaseSupplierKey = FirstNonEmpty(
                CanonicalSupplierKey(supplier.Identification, allowSupplierCheckDigit),
                canonicalKey);

            foreach (var row in groupRows)
            {
                ct.ThrowIfCancellationRequested();
                var workingRow = row;

                var holdIdentity = _payloadFactory.ResolveIdentity(workingRow);
                var holdMatches = FindExistingPurchaseMatches(
                    purchases,
                    purchaseSupplierKey,
                    holdIdentity,
                    allowSupplierCheckDigit);
                var wasProcessing = workingRow.AutomationState.Equals("ProcesandoSiigo", StringComparison.OrdinalIgnoreCase);
                var canRetryConfirmedRateLimitRejection = HasAmbiguousWriteHold(workingRow)
                    && IsConfirmedRateLimitRejection(workingRow);
                if ((HasAmbiguousWriteHold(workingRow) || wasProcessing)
                    && holdMatches.Count == 0
                    && !canRetryConfirmedRateLimitRejection)
                {
                    var isFreshConcurrentProcessing = wasProcessing
                        && !HasAmbiguousWriteHold(workingRow)
                        && workingRow.ModifiedAt.HasValue
                        && workingRow.ModifiedAt.Value > DateTimeOffset.UtcNow.AddMinutes(-15);
                    var message = isFreshConcurrentProcessing
                        ? "Otra ejecucion esta procesando esta factura; no se publicara en paralelo."
                        : "Siigo no confirmo una creacion anterior y aun no aparece una compra coincidente. El POST queda bloqueado para evitar duplicados.";
                    if (!isFreshConcurrentProcessing && !dryRun)
                    {
                        await PersistFailureBestEffortAsync(
                            workingRow.RecordId,
                            $"{AmbiguousWriteHoldMarker} {message}",
                            ct);
                    }

                    rowResults.Add(BuildRowResult(
                        workingRow,
                        isFreshConcurrentProcessing ? "ConcurrentProcessing" : "AmbiguousWritePending",
                        message,
                        purchaseSupplierKey));
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(workingRow.SiigoDocumentId))
                {
                    rowResults.Add(BuildRowResult(
                        workingRow,
                        "AlreadyImported",
                        "La factura ya tiene un documento Siigo asociado.",
                        canonicalKey,
                        workingRow.SiigoDocumentId,
                        workingRow.SiigoDocumentName));
                    continue;
                }

                if (!dryRun && !SupplierAssociationMatches(workingRow, supplier, supplierLabel))
                {
                    try
                    {
                        await _dataverse.MarkConciliacionDianSupplierAsync(
                            workingRow.RecordId,
                            supplier.Id,
                            supplierLabel,
                            $"Proveedor Siigo encontrado automaticamente por NIT {workingRow.SupplierNit}: {supplierLabel}.",
                            ct);
                        result.SupplierRowsAssociated++;
                        workingRow = await _dataverse.GetConciliacionDianSupplierDocumentAsync(workingRow.RecordId, ct);
                    }
                    catch (Exception ex)
                    {
                        var detail = BuildExceptionDetail(ex);
                        var message = $"Se encontro el proveedor en Siigo, pero no fue posible asociarlo en Dataverse. {detail}";
                        await PersistFailureBestEffortAsync(workingRow.RecordId, message, ct);
                        rowResults.Add(BuildRowResult(workingRow, "SupplierAssociationFailed", message, canonicalKey));
                        continue;
                    }
                }

                if (string.IsNullOrWhiteSpace(workingRow.AccountCode))
                {
                    const string message = "Factura bloqueada: falta la cuenta contable para construir la compra Siigo.";
                    if (!dryRun)
                        await PersistFailureBestEffortAsync(workingRow.RecordId, message, ct);
                    rowResults.Add(BuildRowResult(workingRow, "PendingClassification", message, canonicalKey));
                    continue;
                }

                var identity = _payloadFactory.ResolveIdentity(workingRow);
                var existingMatches = FindExistingPurchaseMatches(purchases, purchaseSupplierKey, identity, allowSupplierCheckDigit);
                if (existingMatches.Count > 1)
                {
                    var message = $"Se encontraron {existingMatches.Count:N0} compras Siigo para el mismo NIT, prefijo y folio; se requiere revision manual.";
                    if (!dryRun)
                        await PersistFailureBestEffortAsync(workingRow.RecordId, message, ct);
                    rowResults.Add(BuildRowResult(workingRow, "AmbiguousExistingPurchase", message, canonicalKey));
                    continue;
                }

                if (existingMatches.Count == 1 && !PurchaseValuesMatch(existingMatches[0], workingRow))
                {
                    var existing = existingMatches[0];
                    var message = $"Ya existe una compra Siigo con el mismo proveedor, prefijo y folio ({FirstNonEmpty(existing.Name, existing.Id)}), pero su fecha o total no coincide con DIAN. Se bloqueo la creacion para evitar un duplicado.";
                    if (!dryRun)
                        await PersistFailureBestEffortAsync(workingRow.RecordId, message, ct);
                    rowResults.Add(BuildRowResult(workingRow, "ExistingPurchaseConflict", message, canonicalKey, existing.Id, existing.Name));
                    continue;
                }

                if (existingMatches.Count == 1)
                {
                    var existing = existingMatches[0];
                    if (dryRun)
                    {
                        rowResults.Add(BuildRowResult(
                            workingRow,
                            "ExistingPurchaseWouldLink",
                            $"La compra {FirstNonEmpty(existing.Name, existing.Id)} ya existe en Siigo y se asociaria sin crear otra.",
                            canonicalKey,
                            existing.Id,
                            existing.Name));
                    }
                    else
                    {
                        if (!await TryClaimDianInvoiceForSiigoAsync(workingRow, canonicalKey, rowResults, ct))
                            continue;
                        rowResults.Add(await LinkExistingPurchaseAsync(
                            workingRow,
                            existing,
                            canonicalKey,
                            "Compra ya existente en Siigo; se asocio sin crear un duplicado.",
                            "ExistingLinked",
                            ct));
                    }
                    continue;
                }

                PurchaseCatalogs catalogs;
                try
                {
                    catalogs = await GetCatalogsAsync();
                }
                catch (Exception ex)
                {
                    var detail = BuildExceptionDetail(ex);
                    var message = $"No fue posible cargar los catalogos requeridos para crear la compra Siigo. {detail}";
                    if (!dryRun)
                        await PersistFailureBestEffortAsync(workingRow.RecordId, message, ct);
                    rowResults.Add(BuildRowResult(workingRow, "CatalogLookupFailed", message, canonicalKey));
                    continue;
                }

                var prepared = _payloadFactory.Build(
                    workingRow,
                    FirstNonEmpty(supplier.Identification, workingRow.SupplierNit),
                    catalogs.DocumentTypes,
                    catalogs.PaymentTypes,
                    catalogs.Taxes);
                if (!prepared.CanSend || prepared.Payload is null)
                {
                    var message = prepared.Issues.Count == 0
                        ? "No fue posible construir el payload de la compra Siigo."
                        : string.Join(" ", prepared.Issues);
                    if (!dryRun)
                        await PersistFailureBestEffortAsync(workingRow.RecordId, message, ct);
                    rowResults.Add(BuildRowResult(workingRow, "ValidationFailed", message, canonicalKey, issues: prepared.Issues));
                    continue;
                }

                if (dryRun)
                {
                    rowResults.Add(BuildRowResult(
                        workingRow,
                        "ReadyDryRun",
                        "Factura validada; en ejecucion real se crearia la compra Siigo.",
                        canonicalKey));
                    continue;
                }

                if (!await TryClaimDianInvoiceForSiigoAsync(workingRow, canonicalKey, rowResults, ct))
                    continue;

                try
                {
                    var created = await _siigo.CreatePurchaseAsync(prepared.Payload, idempotencyKey: null, ct: ct);
                    AddCreatedPurchaseToPrecheckCache(purchases, workingRow, purchaseSupplierKey, prepared.Identity, created);
                    try
                    {
                        await _dataverse.MarkConciliacionDianSupplierDocumentSiigoResultAsync(
                            workingRow.RecordId,
                            success: true,
                            message: $"Factura de compra creada automaticamente en Siigo: {FirstNonEmpty(created.Name, created.Id)}.",
                            siigoId: created.Id,
                            siigoName: created.Name,
                            responseJson: created.RawJson,
                            ct: ct);
                        rowResults.Add(BuildRowResult(
                            workingRow,
                            "Created",
                            "Factura de compra creada automaticamente en Siigo.",
                            canonicalKey,
                            created.Id,
                            created.Name));
                    }
                    catch (Exception persistException)
                    {
                        var message = $"{AmbiguousWriteHoldMarker} Siigo creo la compra {FirstNonEmpty(created.Name, created.Id)}, pero no fue posible guardar la asociacion en Dataverse. No se debe reenviar; el siguiente proceso la recuperara por NIT, prefijo y folio. {BuildExceptionDetail(persistException)}";
                        _logger.LogError(persistException, "Compra Siigo creada pero no persistida para DIAN {RecordId}.", workingRow.RecordId);
                        await PersistFailureBestEffortAsync(
                            workingRow.RecordId,
                            message,
                            ct,
                            ownsProcessingClaim: true);
                        rowResults.Add(BuildRowResult(
                            workingRow,
                            "AmbiguousWritePending",
                            message,
                            canonicalKey,
                            created.Id,
                            created.Name));
                    }
                }
                catch (Exception ex)
                {
                    if (IsMissingSupplierPurchaseFailure(ex))
                    {
                        var message = $"Siigo confirmo que el proveedor {canonicalKey} no existe. La factura queda pendiente para cargar el RUT y crear el proveedor.";
                        try
                        {
                            await _dataverse.ClearConciliacionDianSupplierAsync(
                                workingRow.RecordId,
                                message,
                                ct);
                            if (!pendingSuppliers.Any(item => string.Equals(
                                    item.SupplierKey,
                                    canonicalKey,
                                    StringComparison.OrdinalIgnoreCase)))
                            {
                                pendingSuppliers.Add(BuildPendingSupplier(canonicalKey, groupRows));
                                result.SupplierGroupsMissing++;
                            }
                            rowResults.Add(BuildRowResult(
                                workingRow,
                                "PendingSupplier",
                                message,
                                canonicalKey));
                        }
                        catch (Exception clearException)
                        {
                            var clearMessage = $"{message} No fue posible retirar la asociacion obsoleta en Dataverse. {BuildExceptionDetail(clearException)}";
                            await PersistFailureBestEffortAsync(
                                workingRow.RecordId,
                                clearMessage,
                                ct,
                                ownsProcessingClaim: true,
                                releaseProcessingClaim: true);
                            rowResults.Add(BuildRowResult(
                                workingRow,
                                "SupplierAssociationClearFailed",
                                clearMessage,
                                canonicalKey));
                        }
                        continue;
                    }

                    var recovered = false;
                    var ambiguousWriteFailure = IsAmbiguousWriteFailure(ex);
                    if (ambiguousWriteFailure)
                    {
                        try
                        {
                            purchases = (await _siigo.GetPurchasesByDateRangeAsync(
                                    purchaseLookupStart,
                                    purchaseLookupEnd,
                                    ct))
                                .ToList();
                            var recoveredMatches = FindExistingPurchaseMatches(purchases, purchaseSupplierKey, prepared.Identity, allowSupplierCheckDigit);
                            if (recoveredMatches.Count == 1)
                            {
                                if (PurchaseValuesMatch(recoveredMatches[0], workingRow))
                                {
                                    rowResults.Add(await LinkExistingPurchaseAsync(
                                        workingRow,
                                        recoveredMatches[0],
                                        canonicalKey,
                                        "La respuesta de creacion fue ambigua, pero la compra se encontro al reconsultar Siigo y quedo asociada.",
                                        "RecoveredAfterAmbiguousError",
                                        ct));
                                }
                                else
                                {
                                    var message = $"{AmbiguousWriteHoldMarker} La respuesta de creacion fue ambigua y la compra encontrada tiene fecha o total diferentes; se requiere revision manual.";
                                    await PersistFailureBestEffortAsync(
                                        workingRow.RecordId,
                                        message,
                                        ct,
                                        ownsProcessingClaim: true);
                                    rowResults.Add(BuildRowResult(workingRow, "AmbiguousPurchaseConflict", message, canonicalKey));
                                }
                                recovered = true;
                            }
                            else if (recoveredMatches.Count > 1)
                            {
                                var message = $"{AmbiguousWriteHoldMarker} La respuesta de creacion fue ambigua y la reconsulta encontro varias compras coincidentes; se requiere revision manual.";
                                await PersistFailureBestEffortAsync(
                                    workingRow.RecordId,
                                    message,
                                    ct,
                                    ownsProcessingClaim: true);
                                rowResults.Add(BuildRowResult(workingRow, "AmbiguousRecoveredPurchases", message, canonicalKey));
                                recovered = true;
                            }
                        }
                        catch (Exception recoveryException)
                        {
                            _logger.LogWarning(
                                recoveryException,
                                "No se pudo reconsultar Siigo despues de error ambiguo creando DIAN {RecordId}.",
                                workingRow.RecordId);
                        }
                    }

                    if (!recovered)
                    {
                        var message = ambiguousWriteFailure
                            ? $"{AmbiguousWriteHoldMarker} Siigo no confirmo la creacion de la factura de compra. No se reintentara otro POST hasta encontrarla en la consulta o realizar una revision manual. {BuildExceptionDetail(ex)}"
                            : $"Siigo rechazo la creacion de la factura de compra. {BuildExceptionDetail(ex)}";
                        await PersistFailureBestEffortAsync(
                            workingRow.RecordId,
                            message,
                            ct,
                            ownsProcessingClaim: true,
                            releaseProcessingClaim: !ambiguousWriteFailure);
                        rowResults.Add(BuildRowResult(
                            workingRow,
                            ambiguousWriteFailure ? "AmbiguousWritePending" : "CreateFailed",
                            message,
                            canonicalKey));
                    }
                }
            }
        }

        CompleteResult(result, rowResults, pendingSuppliers);
        return result;
    }

    private async Task<PurchaseCatalogs> LoadPurchaseCatalogsAsync(CancellationToken ct)
    {
        var documentTypesTask = _siigo.GetDocumentTypesAsync("FC", ct);
        var paymentTypesTask = _siigo.GetPaymentTypesAsync("FC", ct);
        var taxesTask = _siigo.GetTaxesAsync(ct);
        await Task.WhenAll(documentTypesTask, paymentTypesTask, taxesTask);
        return new PurchaseCatalogs(
            await documentTypesTask,
            await paymentTypesTask,
            await taxesTask);
    }

    private async Task<DianSupplierInvoiceAutomationRowResultDto> LinkExistingPurchaseAsync(
        ConciliacionDianSupplierInvoiceRowDto row,
        SiigoReconciliationPurchase purchase,
        string supplierKey,
        string message,
        string successStatus,
        CancellationToken ct)
    {
        try
        {
            await _dataverse.MarkConciliacionDianSupplierDocumentSiigoResultAsync(
                row.RecordId,
                success: true,
                message: message,
                siigoId: purchase.Id,
                siigoName: purchase.Name,
                responseJson: "",
                ct: ct);
            return BuildRowResult(row, successStatus, message, supplierKey, purchase.Id, purchase.Name);
        }
        catch (Exception ex)
        {
            var detail = $"Se encontro la compra {FirstNonEmpty(purchase.Name, purchase.Id)} en Siigo, pero no fue posible guardar la asociacion en Dataverse. {BuildExceptionDetail(ex)}";
            _logger.LogError(ex, "No se pudo asociar compra existente Siigo a DIAN {RecordId}.", row.RecordId);
            await PersistFailureBestEffortAsync(
                row.RecordId,
                detail,
                ct,
                ownsProcessingClaim: true,
                releaseProcessingClaim: true);
            return BuildRowResult(row, "ExistingLinkPersistenceFailed", detail, supplierKey, purchase.Id, purchase.Name);
        }
    }

    private async Task PersistFailureBestEffortAsync(
        string recordId,
        string message,
        CancellationToken ct,
        bool ownsProcessingClaim = false,
        bool releaseProcessingClaim = false)
    {
        try
        {
            await _dataverse.MarkConciliacionDianSupplierDocumentSiigoResultAsync(
                recordId,
                success: false,
                message: message,
                responseJson: message,
                ownsProcessingClaim: ownsProcessingClaim,
                releaseProcessingClaim: releaseProcessingClaim,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo persistir el fallo de automatizacion DIAN {RecordId}.", recordId);
        }
    }

    private async Task<bool> TryClaimDianInvoiceForSiigoAsync(
        ConciliacionDianSupplierInvoiceRowDto row,
        string supplierKey,
        ICollection<DianSupplierInvoiceAutomationRowResultDto> rowResults,
        CancellationToken ct)
    {
        try
        {
            var claimed = await _dataverse.TryClaimConciliacionDianSupplierDocumentForSiigoAsync(
                row.RecordId,
                row.ConcurrencyToken,
                ct);
            if (claimed)
                return true;

            rowResults.Add(BuildRowResult(
                row,
                "ConcurrentProcessing",
                "La factura cambio antes de reservar el envio; se omitio para evitar una publicacion concurrente.",
                supplierKey));
            return false;
        }
        catch (Exception ex)
        {
            var message = $"No fue posible reservar atomicamente la factura antes de publicar en Siigo. {BuildExceptionDetail(ex)}";
            rowResults.Add(BuildRowResult(row, "ClaimFailed", message, supplierKey));
            return false;
        }
    }

    private static IReadOnlyList<SiigoReconciliationPurchase> FindExistingPurchaseMatches(
        IReadOnlyList<SiigoReconciliationPurchase> purchases,
        string supplierKey,
        DianSupplierInvoiceIdentity identity,
        bool allowSupplierCheckDigit)
    {
        var expectedPrefix = NormalizeDocumentPart(identity.Prefix);
        var expectedNumber = NormalizeInvoiceNumber(identity.Number);
        if (string.IsNullOrWhiteSpace(expectedNumber))
            return Array.Empty<SiigoReconciliationPurchase>();
        var expectedFullNumber = NormalizeDocumentPart($"{identity.Prefix}{identity.Number}");

        return purchases
            .Where(purchase => string.Equals(
                CanonicalSupplierKey(purchase.SupplierIdentification, allowSupplierCheckDigit),
                supplierKey,
                StringComparison.OrdinalIgnoreCase))
            .Where(purchase =>
            {
                var splitMatches = string.Equals(
                        NormalizeDocumentPart(purchase.ProviderInvoicePrefix),
                        expectedPrefix,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        NormalizeInvoiceNumber(purchase.ProviderInvoiceNumber),
                        expectedNumber,
                        StringComparison.OrdinalIgnoreCase);
                var purchaseFullNumber = NormalizeDocumentPart(purchase.ProviderInvoiceFullNumber);
                var fullMatches = !string.IsNullOrWhiteSpace(expectedFullNumber)
                    && (string.Equals(purchaseFullNumber, expectedFullNumber, StringComparison.OrdinalIgnoreCase)
                        || !string.IsNullOrWhiteSpace(expectedPrefix)
                        && purchaseFullNumber.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(
                            NormalizeInvoiceNumber(purchaseFullNumber[expectedPrefix.Length..]),
                            expectedNumber,
                            StringComparison.OrdinalIgnoreCase));
                return splitMatches || fullMatches;
            })
            .GroupBy(static purchase => FirstNonEmpty(purchase.Id, purchase.Name), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private static void AddCreatedPurchaseToPrecheckCache(
        ICollection<SiigoReconciliationPurchase> purchases,
        ConciliacionDianSupplierInvoiceRowDto row,
        string supplierKey,
        DianSupplierInvoiceIdentity identity,
        SiigoVoucherCreateResultDto created)
    {
        purchases.Add(new SiigoReconciliationPurchase
        {
            Id = created.Id,
            Name = created.Name,
            Date = DateOnly.TryParseExact(
                row.EmissionDateValue,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date)
                    ? date
                    : null,
            SupplierIdentification = supplierKey,
            ProviderInvoicePrefix = identity.Prefix,
            ProviderInvoiceNumber = identity.Number,
            ProviderInvoiceFullNumber = $"{identity.Prefix}{identity.Number}",
            Total = row.TotalValue,
            Vat = row.VatValue,
            Balance = row.TotalValue
        });
    }

    private static bool PurchaseValuesMatch(
        SiigoReconciliationPurchase purchase,
        ConciliacionDianSupplierInvoiceRowDto row)
    {
        var totalMatches = Math.Abs(purchase.Total - row.TotalValue) <= 1m;
        if (!DateOnly.TryParseExact(
                row.EmissionDateValue,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var expectedDate))
        {
            return false;
        }

        return totalMatches && purchase.Date.HasValue && purchase.Date.Value == expectedDate;
    }

    private static DianSupplierAutomationMissingSupplierDto BuildPendingSupplier(
        string supplierKey,
        IReadOnlyList<ConciliacionDianSupplierInvoiceRowDto> rows)
    {
        var invoices = rows
            .Select(static row => new DianSupplierAutomationPendingInvoiceDto
            {
                RecordId = row.RecordId,
                InvoiceNumber = row.InvoiceNumber,
                CufeCude = row.Cufe,
                EmissionDate = row.EmissionDateValue,
                TotalValue = row.TotalValue
            })
            .ToArray();
        return new DianSupplierAutomationMissingSupplierDto
        {
            SupplierKey = supplierKey,
            SupplierNit = rows.Select(static row => row.SupplierNit).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? supplierKey,
            SupplierName = rows.Select(static row => row.SupplierName).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? "Proveedor sin nombre",
            RepresentativeRecordId = rows.Select(static row => row.RecordId).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? "",
            PendingInvoiceCount = rows.Count,
            TotalValue = Math.Round(rows.Sum(static row => row.TotalValue), 2, MidpointRounding.AwayFromZero),
            Invoices = invoices
        };
    }

    private static DianSupplierInvoiceAutomationRowResultDto BuildRowResult(
        ConciliacionDianSupplierInvoiceRowDto row,
        string status,
        string message,
        string? supplierKey = null,
        string? siigoId = null,
        string? siigoName = null,
        IReadOnlyList<string>? issues = null)
    {
        return new DianSupplierInvoiceAutomationRowResultDto
        {
            RecordId = row.RecordId,
            CufeCude = row.Cufe,
            InvoiceNumber = row.InvoiceNumber,
            SupplierKey = FirstNonEmpty(supplierKey, CanonicalSupplierKey(row.SupplierNit, IsLikelyCompanySupplier(row))),
            SupplierNit = row.SupplierNit,
            SupplierName = row.SupplierName,
            Status = status,
            Message = message,
            SiigoId = siigoId ?? "",
            SiigoName = siigoName ?? "",
            Issues = issues ?? Array.Empty<string>()
        };
    }

    private static void CompleteResult(
        DianSupplierInvoiceAutomationResultDto result,
        IReadOnlyList<DianSupplierInvoiceAutomationRowResultDto> rows,
        IReadOnlyList<DianSupplierAutomationMissingSupplierDto> pendingSuppliers)
    {
        result.Rows = rows;
        result.PendingSuppliers = pendingSuppliers;
        result.AlreadyImported = rows.Count(static row => row.Status.Equals("AlreadyImported", StringComparison.OrdinalIgnoreCase));
        result.AlreadyLinked = result.AlreadyImported;
        result.ExistingLinked = rows.Count(static row =>
            row.Status.Equals("ExistingLinked", StringComparison.OrdinalIgnoreCase)
            || row.Status.Equals("RecoveredAfterAmbiguousError", StringComparison.OrdinalIgnoreCase));
        result.ExistingPurchasesLinked = result.ExistingLinked;
        result.Created = rows.Count(static row => row.Status.Equals("Created", StringComparison.OrdinalIgnoreCase));
        result.PurchasesCreated = result.Created;
        result.PurchasesRecoveredAfterAmbiguousError = rows.Count(static row => row.Status.Equals("RecoveredAfterAmbiguousError", StringComparison.OrdinalIgnoreCase));
        result.PendingSupplierInvoices = rows.Count(static row => row.Status.Equals("PendingSupplier", StringComparison.OrdinalIgnoreCase));
        result.PendingClassification = rows.Count(static row => row.Status.Equals("PendingClassification", StringComparison.OrdinalIgnoreCase));
        result.ConcurrentProcessing = rows.Count(static row => row.Status.Equals("ConcurrentProcessing", StringComparison.OrdinalIgnoreCase));
        result.AmbiguousWritePending = rows.Count(static row => row.Status.Equals("AmbiguousWritePending", StringComparison.OrdinalIgnoreCase));
        result.BlockedMissingAccount = result.PendingClassification;
        result.PurchasesReadyInDryRun = rows.Count(static row => row.Status.Equals("ReadyDryRun", StringComparison.OrdinalIgnoreCase));

        result.Failed = rows.Count(row =>
            !SuccessfulStatuses.Contains(row.Status)
            && !row.Status.Equals("ReadyDryRun", StringComparison.OrdinalIgnoreCase)
            && !row.Status.Equals("ExistingPurchaseWouldLink", StringComparison.OrdinalIgnoreCase)
            && !row.Status.Equals("PendingSupplier", StringComparison.OrdinalIgnoreCase)
            && !row.Status.Equals("PendingClassification", StringComparison.OrdinalIgnoreCase)
            && !row.Status.Equals("ConcurrentProcessing", StringComparison.OrdinalIgnoreCase)
            && !row.Status.Equals("AmbiguousWritePending", StringComparison.OrdinalIgnoreCase));

        result.CanComplete = rows.Count == result.EligibleRows
            && result.PendingSupplierInvoices == 0
            && result.PendingClassification == 0
            && result.AmbiguousWritePending == 0
            && result.Failed == 0
            && rows.All(row => SuccessfulStatuses.Contains(row.Status)
                || result.DryRun && (row.Status.Equals("ReadyDryRun", StringComparison.OrdinalIgnoreCase)
                    || row.Status.Equals("ExistingPurchaseWouldLink", StringComparison.OrdinalIgnoreCase)));
        result.IsComplete = !result.DryRun && result.CanComplete;
        result.Completed = result.IsComplete;
        result.Status = result.IsComplete
            ? "Completed"
            : result.DryRun && result.CanComplete
                ? "DryRunReady"
                : result.Failed > 0
                    ? "CompletedWithErrors"
                    : "Pending";
        result.Message = result.IsComplete
            ? $"Automatizacion DIAN completada: {result.Created:N0} creada(s), {result.ExistingLinked:N0} existente(s) asociada(s), {result.AlreadyImported:N0} ya importada(s)."
            : result.DryRun && result.CanComplete
                ? $"Simulacion lista: {result.PurchasesReadyInDryRun:N0} compra(s) se crearian y {rows.Count(static row => row.Status.Equals("ExistingPurchaseWouldLink", StringComparison.OrdinalIgnoreCase)):N0} se asociarian."
                : $"Automatizacion DIAN pendiente: {result.PendingSupplierInvoices:N0} factura(s) sin proveedor, {result.PendingClassification:N0} sin cuenta, {result.AmbiguousWritePending:N0} esperando confirmacion segura de Siigo, {result.ConcurrentProcessing:N0} en otra ejecucion y {result.Failed:N0} con error.";
    }

    private static (DateOnly Start, DateOnly End) ResolvePurchaseLookupRange(
        IReadOnlyList<ConciliacionDianSupplierInvoiceRowDto> rows,
        DateOnly fallbackStart,
        DateOnly fallbackEnd)
    {
        var emissionDates = rows
            .Select(static row => DateOnly.TryParseExact(
                row.EmissionDateValue,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date)
                    ? date
                    : (DateOnly?)null)
            .Where(static date => date.HasValue)
            .Select(static date => date!.Value)
            .ToArray();
        if (emissionDates.Length == 0)
            return (fallbackStart, fallbackEnd);

        var minimum = emissionDates.Min();
        var maximum = emissionDates.Max();
        return (
            new DateOnly(minimum.Year - 1, 1, 1),
            new DateOnly(maximum.Year + 1, 12, 31));
    }

    private static bool HasAmbiguousWriteHold(ConciliacionDianSupplierInvoiceRowDto row) =>
        row.AutomationState.Equals("VerificacionSiigoPendiente", StringComparison.OrdinalIgnoreCase)
        || row.ReviewReason.Contains(AmbiguousWriteHoldMarker, StringComparison.OrdinalIgnoreCase);

    private static bool IsConfirmedRateLimitRejection(ConciliacionDianSupplierInvoiceRowDto row) =>
        row.ReviewReason.Contains("respondio 429", StringComparison.OrdinalIgnoreCase)
        || row.ReviewReason.Contains("\"Code\": \"requests_limit\"", StringComparison.OrdinalIgnoreCase);

    private static bool IsEligibleReceivedElectronicInvoice(
        ConciliacionDianSupplierInvoiceRowDto row,
        DateOnly periodStart,
        DateOnly periodEndExclusive)
    {
        var type = NormalizeText(row.DocumentType);
        var group = NormalizeText(row.DianGroup);
        var source = NormalizeText(row.AutomationSource);
        if (string.IsNullOrWhiteSpace(row.Cufe)
            || string.IsNullOrWhiteSpace(row.ExcelKey)
            || !row.ExcelKey.StartsWith("dian-cufe:", StringComparison.OrdinalIgnoreCase)
            || !source.Equals("DIAN EXCEL", StringComparison.OrdinalIgnoreCase)
            || !type.Contains("FACTURA ELECTRONICA", StringComparison.OrdinalIgnoreCase)
            || type.Contains("SOPORTE", StringComparison.OrdinalIgnoreCase)
            || type.Contains("NOTA", StringComparison.OrdinalIgnoreCase)
            || type.Contains("APPLICATION RESPONSE", StringComparison.OrdinalIgnoreCase)
            || !group.Contains("RECIBID", StringComparison.OrdinalIgnoreCase)
            || group.Contains("EMITID", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return DateOnly.TryParseExact(
                row.ReceptionDateValue,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var receptionDate)
            && receptionDate >= periodStart
            && receptionDate < periodEndExclusive;
    }

    private string BuildDianBusinessIdentityKey(ConciliacionDianSupplierInvoiceRowDto row)
    {
        var supplierKey = TryCanonicalColombianNit(row.SupplierNit, out var baseNit)
            ? baseNit
            : ExtractDigits(row.SupplierNit);
        var identity = _payloadFactory.ResolveIdentity(row);
        var prefix = NormalizeDocumentPart(identity.Prefix);
        var number = NormalizeInvoiceNumber(identity.Number);
        if (string.IsNullOrWhiteSpace(supplierKey)
            || string.IsNullOrWhiteSpace(prefix)
            || string.IsNullOrWhiteSpace(number))
        {
            return "";
        }

        return string.Join("|", supplierKey, prefix, number);
    }

    private static bool SupplierAssociationMatches(
        ConciliacionDianSupplierInvoiceRowDto row,
        SiigoCustomerLookupItemDto supplier,
        string supplierLabel)
    {
        return !string.IsNullOrWhiteSpace(row.SiigoSupplierId)
            && string.Equals(row.SiigoSupplierId.Trim(), supplier.Id.Trim(), StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(supplierLabel)
                || string.Equals(row.SiigoSupplierName.Trim(), supplierLabel.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string CanonicalSupplierKey(string? value, bool allowCheckDigit)
    {
        var digits = ExtractDigits(value);
        if (allowCheckDigit && digits.Length == 10)
        {
            var baseNit = digits[..^1];
            var checkDigit = digits[^1] - '0';
            if (CalculateColombianCheckDigit(baseNit) == checkDigit)
                return baseNit;
        }

        return digits;
    }

    private static bool TryCanonicalColombianNit(string? value, out string baseNit)
    {
        var digits = ExtractDigits(value);
        baseNit = "";
        if (digits.Length != 10)
            return false;

        var candidate = digits[..^1];
        if (digits[^1] - '0' != CalculateColombianCheckDigit(candidate))
            return false;

        baseNit = candidate;
        return true;
    }

    private static bool SupplierRequestMatches(
        ConciliacionDianSupplierInvoiceRowDto row,
        string requestedSupplierDigits)
    {
        var rowDigits = ExtractDigits(row.SupplierNit);
        if (string.Equals(rowDigits, requestedSupplierDigits, StringComparison.Ordinal))
            return true;
        if (!IsLikelyCompanySupplier(row))
            return false;

        return string.Equals(
            CanonicalSupplierKey(rowDigits, allowCheckDigit: true),
            CanonicalSupplierKey(requestedSupplierDigits, allowCheckDigit: true),
            StringComparison.Ordinal);
    }

    private static bool IsLikelyCompanySupplier(ConciliacionDianSupplierInvoiceRowDto row)
    {
        if ((row.SupplierNit ?? "").Contains('-', StringComparison.Ordinal))
            return true;

        var name = NormalizeText(row.SupplierName);
        return Regex.IsMatch(name, @"\b(S\s*A\s*S|S\s*A|LTDA|LIMITADA|SOCIEDAD|EMPRESA|FUNDACION|CORPORACION)\b", RegexOptions.CultureInvariant);
    }

    private static int CalculateColombianCheckDigit(string identification)
    {
        var digits = ExtractDigits(identification);
        var weights = new[] { 71, 67, 59, 53, 47, 43, 41, 37, 29, 23, 19, 17, 13, 7, 3 };
        var offset = Math.Max(0, weights.Length - digits.Length);
        var sum = 0;
        for (var index = 0; index < digits.Length && index + offset < weights.Length; index++)
            sum += (digits[index] - '0') * weights[index + offset];

        var remainder = sum % 11;
        return remainder > 1 ? 11 - remainder : remainder;
    }

    private static string NormalizeDocumentPart(string? value) =>
        new((value ?? "")
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    private static string NormalizeInvoiceNumber(string? value)
    {
        var digits = ExtractDigits(value);
        if (digits.Length == 0)
            return "";

        var withoutLeadingZeroes = digits.TrimStart('0');
        return withoutLeadingZeroes.Length == 0 ? "0" : withoutLeadingZeroes;
    }

    private static string NormalizeText(string? value)
    {
        var normalized = (value ?? "").Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return Regex.Replace(
            builder.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant().Trim(),
            @"\s+",
            " ",
            RegexOptions.CultureInvariant);
    }

    private static string ExtractDigits(string? value) =>
        new((value ?? "").Where(char.IsDigit).ToArray());

    private static bool IsAmbiguousWriteFailure(Exception exception)
    {
        if (exception is HttpRequestException or TaskCanceledException or TimeoutException)
            return true;

        var detail = BuildExceptionDetail(exception);
        return detail.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("tiempo de espera", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("respuesta vacia", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("creo la factura de compra", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("no fue posible interpretar la respuesta", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("no incluyo id ni nombre", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("currently unavailable", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("document_query_service", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("respondio 408", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("respondio 500", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("respondio 502", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("respondio 503", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("respondio 504", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("conexion", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsMissingSupplierPurchaseFailure(Exception exception)
    {
        var detail = BuildExceptionDetail(exception);
        return detail.Contains("supplier doesn't exist", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("supplier does not exist", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("proveedor no existe", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("proveedor no encontrado", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildExceptionDetail(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (!string.IsNullOrWhiteSpace(current.Message)
                && !messages.Contains(current.Message.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                messages.Add(current.Message.Trim());
            }
        }

        return string.Join(" | ", messages);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private sealed record PurchaseCatalogs(
        IReadOnlyList<SiigoDocumentTypeLookupDto> DocumentTypes,
        IReadOnlyList<SiigoPaymentTypeLookupDto> PaymentTypes,
        IReadOnlyList<SiigoTaxLookupDto> Taxes);
}
