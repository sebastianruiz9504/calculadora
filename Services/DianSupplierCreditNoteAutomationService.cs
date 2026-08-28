using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Automation;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Models.Reconciliation;

namespace CotizadorInterno.Web.Services;

public sealed class DianSupplierCreditNoteAutomationService : IDianSupplierCreditNoteAutomationService
{
    private const string AccountsPayableCode = "22050501";
    private const string DeductibleVatCode = "240803";
    private static readonly SemaphoreSlim ProcessLock = new(1, 1);
    private readonly IDataverseService _dataverse;
    private readonly ISiigoService _siigo;
    private readonly ILogger<DianSupplierCreditNoteAutomationService> _logger;

    public DianSupplierCreditNoteAutomationService(
        IDataverseService dataverse,
        ISiigoService siigo,
        ILogger<DianSupplierCreditNoteAutomationService> logger)
    {
        _dataverse = dataverse;
        _siigo = siigo;
        _logger = logger;
    }

    public async Task<DianSupplierCreditNoteAutomationResultDto> ProcessPeriodAsync(
        DateOnly periodStart,
        bool dryRun = false,
        IReadOnlySet<string>? externalKeys = null,
        CancellationToken ct = default)
    {
        if (periodStart.Day != 1)
            throw new InvalidOperationException("La automatizacion de notas DIAN requiere el primer dia del mes.");

        await ProcessLock.WaitAsync(ct);
        try
        {
            return await ProcessCoreAsync(periodStart, dryRun, externalKeys, ct);
        }
        finally
        {
            ProcessLock.Release();
        }
    }

    private async Task<DianSupplierCreditNoteAutomationResultDto> ProcessCoreAsync(
        DateOnly periodStart,
        bool dryRun,
        IReadOnlySet<string>? externalKeys,
        CancellationToken ct)
    {
        var periodEnd = periodStart.AddMonths(1);
        var result = new DianSupplierCreditNoteAutomationResultDto
        {
            DryRun = dryRun,
            PeriodStart = periodStart,
            PeriodEndExclusive = periodEnd,
            Status = "Running"
        };
        var rows = await _dataverse.GetConciliacionDianSupplierDocumentsForAutomationAsync(
            periodStart,
            periodEnd,
            ct);
        result.RowsReviewed = rows.Count;
        var notes = rows
            .Where(IsReceivedSupplierCreditNote)
            .Where(row => externalKeys is null
                || externalKeys.Count == 0
                || externalKeys.Contains(row.ExcelKey))
            .OrderBy(static row => row.EmissionDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.SupplierNit, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        result.Eligible = notes.Length;

        if (notes.Length == 0)
        {
            result.Completed = !dryRun;
            result.Status = dryRun ? "DryRunReady" : "Completed";
            result.Message = "No hay notas credito de proveedor recibidas para aplicar en el periodo.";
            return result;
        }

        var invoices = rows.Where(IsReceivedSupplierInvoice).ToArray();
        var receiptSearchStart = periodStart;
        var receiptSearchEnd = DateOnly.FromDateTime(DateTime.UtcNow);
        if (receiptSearchEnd < receiptSearchStart)
            receiptSearchStart = receiptSearchEnd.AddDays(-31);
        SiigoDocumentTypeLookupDto? journalDocument = null;
        if (!dryRun)
        {
            journalDocument = ResolveCreditNoteJournalDocumentType(
                await _siigo.GetDocumentTypesAsync("CC", ct));
        }

        var rowResults = new List<DianSupplierCreditNoteAutomationRowResultDto>(notes.Length);
        foreach (var note in notes)
        {
            ct.ThrowIfCancellationRequested();
            var rowResult = BuildRowResult(note);
            rowResults.Add(rowResult);

            if (!string.IsNullOrWhiteSpace(note.SiigoDocumentId))
            {
                rowResult.Status = "AlreadyApplied";
                rowResult.Message = $"La nota ya esta aplicada mediante {note.SiigoDocumentName}.";
                rowResult.SiigoAdjustmentId = note.SiigoDocumentId;
                rowResult.SiigoAdjustmentName = note.SiigoDocumentName;
                continue;
            }

            if (string.IsNullOrWhiteSpace(note.SiigoSupplierId))
            {
                rowResult.Status = "PendingSupplier";
                rowResult.Message = "El proveedor no existe o no esta asociado en Siigo.";
                continue;
            }

            var source = ResolveSourceInvoice(note, invoices);
            if (source.Status.Equals("Ambiguous", StringComparison.OrdinalIgnoreCase))
            {
                rowResult.Status = "AmbiguousSourcePurchase";
                rowResult.Message = source.Message;
                continue;
            }
            if (source.Invoice is null)
            {
                rowResult.Status = "PendingSourcePurchase";
                rowResult.Message = source.Message;
                continue;
            }

            var invoice = source.Invoice;
            rowResult.SourceInvoiceNumber = invoice.InvoiceNumber;
            rowResult.SourcePurchaseId = invoice.SiigoDocumentId;
            rowResult.SourcePurchaseName = invoice.SiigoDocumentName;
            if (string.IsNullOrWhiteSpace(invoice.SiigoDocumentId)
                || string.IsNullOrWhiteSpace(invoice.SiigoDocumentName))
            {
                rowResult.Status = "PendingSourcePurchase";
                rowResult.Message = $"La factura origen {invoice.InvoiceNumber} aun no tiene compra Siigo.";
                continue;
            }

            if (dryRun)
            {
                rowResult.Status = "DryRunReady";
                rowResult.Message = $"Lista para aplicar a {invoice.SiigoDocumentName} ({invoice.InvoiceNumber}).";
                continue;
            }

            SiigoReconciliationPurchase? purchase;
            try
            {
                purchase = await _siigo.GetPurchaseByIdAsync(invoice.SiigoDocumentId, ct);
            }
            catch (Exception ex)
            {
                rowResult.Status = "Failed";
                rowResult.Message = $"No fue posible verificar la compra origen {invoice.SiigoDocumentName}: {ex.Message}";
                continue;
            }
            if (purchase is null)
            {
                rowResult.Status = "PendingSourcePurchase";
                rowResult.Message = $"Siigo no devolvio la compra origen {invoice.SiigoDocumentName}.";
                continue;
            }

            if (!TryResolvePurchaseDue(purchase, out var purchaseDue, out var dueIssue))
            {
                rowResult.Status = "Failed";
                rowResult.Message = dueIssue;
                continue;
            }

            var observationMarker = BuildObservationMarker(note.Cufe);
            SiigoVoucherCreateResultDto? existingReceipt;
            try
            {
                existingReceipt = await _siigo.FindJournalByObservationAsync(
                    observationMarker,
                    receiptSearchStart,
                    receiptSearchEnd,
                    ct);
            }
            catch (Exception ex)
            {
                rowResult.Status = "Failed";
                rowResult.Message =
                    $"No fue posible comprobar si la nota {note.InvoiceNumber} ya tenia ajuste contable en Siigo: {ex.Message}";
                continue;
            }

            decimal? payableBalance;
            try
            {
                payableBalance = await _siigo.GetAccountsPayableBalanceAsync(
                    note.SupplierNit,
                    purchaseDue.Prefix,
                    purchaseDue.Consecutive,
                    purchaseDue.Quote,
                    ct);
            }
            catch (Exception ex)
            {
                rowResult.Status = "Failed";
                rowResult.Message =
                    $"No fue posible consultar el vencimiento {purchaseDue.Label} en cuentas por pagar: {ex.Message}";
                continue;
            }
            if (!payableBalance.HasValue && existingReceipt is null)
            {
                rowResult.Status = "Failed";
                rowResult.Message =
                    $"Siigo no devolvio el vencimiento {purchaseDue.Label} de la compra {purchase.Name}.";
                continue;
            }
            var authoritativeBalance = RoundCurrency(payableBalance ?? 0m);
            rowResult.PurchaseBalanceBefore = authoritativeBalance;

            if (existingReceipt is not null)
            {
                if (!ValidateExistingJournal(existingReceipt, note, purchase, out var receiptIssue))
                {
                    rowResult.Status = "Failed";
                    rowResult.Message =
                        $"Siigo ya tiene {existingReceipt.Name} para la nota {note.InvoiceNumber}, pero no cruza "
                        + $"correctamente el vencimiento de {purchase.Name}: {receiptIssue}";
                    continue;
                }
                if (authoritativeBalance + note.TotalValue > purchase.Total + 0.01m)
                {
                    rowResult.Status = "Failed";
                    rowResult.Message =
                        $"Siigo ya tiene {existingReceipt.Name}, pero el saldo {authoritativeBalance:N2} de {purchaseDue.Label} "
                        + "no refleja al menos el valor de la nota; se requiere corregir el ajuste antes de confirmarlo.";
                    continue;
                }

                rowResult.PurchaseBalanceAfter = authoritativeBalance;
                rowResult.SiigoAdjustmentId = existingReceipt.Id;
                rowResult.SiigoAdjustmentName = existingReceipt.Name;
                rowResult.Status = "Applied";
                rowResult.Message =
                    $"Nota credito proveedor {note.InvoiceNumber} confirmada en {existingReceipt.Name} y vinculada a "
                    + $"{purchase.Name} (factura proveedor {invoice.InvoiceNumber}). Saldo actual: "
                    + $"{rowResult.PurchaseBalanceAfter:N2}.";
                if (IsAmbiguousWriteHold(note))
                {
                    await _dataverse.ConfirmConciliacionDianSupplierDocumentAmbiguousWriteAsync(
                        note.RecordId,
                        existingReceipt.Id,
                        existingReceipt.Name,
                        rowResult.Message,
                        existingReceipt.RawJson,
                        ct);
                }
                else
                {
                    var existingWorkingNote = await _dataverse.GetConciliacionDianSupplierDocumentAsync(note.RecordId, ct);
                    if (!string.IsNullOrWhiteSpace(existingWorkingNote.SiigoDocumentId))
                    {
                        rowResult.Status = "AlreadyApplied";
                        rowResult.Message = $"La nota ya esta aplicada mediante {existingWorkingNote.SiigoDocumentName}.";
                        rowResult.SiigoAdjustmentId = existingWorkingNote.SiigoDocumentId;
                        rowResult.SiigoAdjustmentName = existingWorkingNote.SiigoDocumentName;
                        continue;
                    }

                    var claimedExisting = await _dataverse.TryClaimConciliacionDianSupplierDocumentForSiigoAsync(
                        note.RecordId,
                        existingWorkingNote.ConcurrencyToken,
                        ct);
                    if (!claimedExisting)
                    {
                        rowResult.Status = "ConcurrentProcessing";
                        rowResult.Message = "La nota cambio o esta siendo procesada mientras se vinculaba su ajuste Siigo.";
                        continue;
                    }
                    await _dataverse.MarkConciliacionDianSupplierDocumentSiigoResultAsync(
                        note.RecordId,
                        success: true,
                        message: rowResult.Message,
                        siigoId: existingReceipt.Id,
                        siigoName: existingReceipt.Name,
                        responseJson: existingReceipt.RawJson,
                        ownsProcessingClaim: true,
                        ct: ct);
                }
                continue;
            }

            if (authoritativeBalance + 0.01m < note.TotalValue)
            {
                rowResult.Status = "Failed";
                rowResult.Message =
                    $"La nota por {note.TotalValue:N2} supera el saldo {authoritativeBalance:N2} "
                    + $"del vencimiento {purchaseDue.Label}; no se aplico.";
                continue;
            }

            var workingNote = await _dataverse.GetConciliacionDianSupplierDocumentAsync(note.RecordId, ct);
            if (!string.IsNullOrWhiteSpace(workingNote.SiigoDocumentId))
            {
                rowResult.Status = "AlreadyApplied";
                rowResult.Message = $"La nota ya esta aplicada mediante {workingNote.SiigoDocumentName}.";
                rowResult.SiigoAdjustmentId = workingNote.SiigoDocumentId;
                rowResult.SiigoAdjustmentName = workingNote.SiigoDocumentName;
                continue;
            }

            var issues = new List<string>();
            var payload = BuildJournalPayload(
                workingNote,
                invoice,
                purchase,
                journalDocument!,
                issues);
            if (issues.Count > 0)
            {
                rowResult.Status = "Failed";
                rowResult.Message = string.Join(" ", issues);
                continue;
            }

            var claimed = await _dataverse.TryClaimConciliacionDianSupplierDocumentForSiigoAsync(
                note.RecordId,
                workingNote.ConcurrencyToken,
                ct);
            if (!claimed)
            {
                rowResult.Status = "ConcurrentProcessing";
                rowResult.Message = "La nota cambio o esta siendo procesada por otra ejecucion.";
                continue;
            }

            var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            try
            {
                var created = await _siigo.CreateJournalAsync(
                    payload,
                    idempotencyKey: BuildIdempotencyKey(note.Cufe),
                    ct: ct);
                var expectedBalance = RoundCurrency(authoritativeBalance - note.TotalValue);
                var verified = await WaitForExpectedPayableBalanceAsync(
                    note.SupplierNit,
                    purchaseDue,
                    expectedBalance,
                    ct);
                if (verified is null)
                {
                    const string marker = "[SIIGO_WRITE_AMBIGUOUS]";
                    rowResult.Status = "Failed";
                    rowResult.Message =
                        $"{marker} Siigo creo {created.Name}, pero no confirmo el saldo esperado de {purchase.Name}.";
                    await _dataverse.MarkConciliacionDianSupplierDocumentSiigoResultAsync(
                        note.RecordId,
                        success: false,
                        message: rowResult.Message,
                        responseJson: created.RawJson,
                        ownsProcessingClaim: true,
                        releaseProcessingClaim: false,
                        ct: ct);
                    continue;
                }

                rowResult.PurchaseBalanceAfter = verified.Value;
                rowResult.SiigoAdjustmentId = created.Id;
                rowResult.SiigoAdjustmentName = created.Name;
                rowResult.Status = "Applied";
                rowResult.Message =
                    $"Nota credito proveedor {note.InvoiceNumber} aplicada a {purchase.Name} "
                    + $"(factura proveedor {invoice.InvoiceNumber}) mediante ajuste {created.Name}. "
                    + $"Saldo {purchaseDue.Label}: {authoritativeBalance:N2} -> {verified.Value:N2}.";
                await _dataverse.MarkConciliacionDianSupplierDocumentSiigoResultAsync(
                    note.RecordId,
                    success: true,
                    message: rowResult.Message,
                    siigoId: created.Id,
                    siigoName: created.Name,
                    responseJson: created.RawJson,
                    ownsProcessingClaim: true,
                    ct: ct);
            }
            catch (Exception ex)
            {
                var recovered = await TryRecoverCreatedReceiptAsync(
                    observationMarker,
                    receiptSearchStart,
                    receiptSearchEnd,
                    purchase,
                    purchaseDue,
                    authoritativeBalance,
                    note,
                    invoice,
                    rowResult,
                    ct);
                if (recovered)
                    continue;

                rowResult.Status = "Failed";
                rowResult.Message = $"No fue posible aplicar la nota a {purchase.Name}: {ex.Message}";
                _logger.LogError(
                    ex,
                    "Fallo la aplicacion de nota DIAN {CreditNote} a compra {Purchase}. Payload={Payload}",
                    note.InvoiceNumber,
                    purchase.Name,
                    payloadJson);
                await _dataverse.MarkConciliacionDianSupplierDocumentSiigoResultAsync(
                    note.RecordId,
                    success: false,
                    message: $"{rowResult.Message} Payload: {payloadJson}",
                    ownsProcessingClaim: true,
                    releaseProcessingClaim: true,
                    ct: ct);
            }
        }

        result.Rows = rowResults;
        CompleteResult(result);
        return result;
    }

    internal static DianSupplierCreditNoteSourceResolution ResolveSourceInvoice(
        ConciliacionDianSupplierInvoiceRowDto note,
        IReadOnlyList<ConciliacionDianSupplierInvoiceRowDto> invoices)
    {
        var noteSupplier = CanonicalTaxId(note.SupplierNit);
        var candidates = invoices
            .Where(invoice => CanonicalTaxId(invoice.SupplierNit).Equals(noteSupplier, StringComparison.OrdinalIgnoreCase))
            .Where(invoice => string.Compare(
                invoice.EmissionDateValue,
                note.EmissionDateValue,
                StringComparison.OrdinalIgnoreCase) <= 0)
            .Where(invoice => invoice.TotalValue + 0.01m >= note.TotalValue)
            .ToArray();

        if (candidates.Length == 0)
        {
            return new DianSupplierCreditNoteSourceResolution(
                "Pending",
                null,
                $"No se encontro una factura recibida anterior del proveedor {note.SupplierNit} con valor suficiente.");
        }

        var exactValue = candidates
            .Where(invoice => Math.Abs(invoice.TotalValue - note.TotalValue) <= 0.01m
                && Math.Abs(invoice.VatValue - note.VatValue) <= 0.01m)
            .ToArray();
        if (exactValue.Length == 1)
            return new DianSupplierCreditNoteSourceResolution("Resolved", exactValue[0], "Coincidencia unica por proveedor y valores.");

        var notePrefix = NormalizePrefix(note.Prefix);
        var samePrefix = candidates
            .Where(invoice => !string.IsNullOrWhiteSpace(notePrefix)
                && NormalizePrefix(invoice.Prefix).Equals(notePrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (samePrefix.Length == 1)
            return new DianSupplierCreditNoteSourceResolution("Resolved", samePrefix[0], "Coincidencia unica por proveedor y prefijo.");

        if (candidates.Length == 1)
            return new DianSupplierCreditNoteSourceResolution("Resolved", candidates[0], "Unica factura candidata del proveedor en el periodo.");

        return new DianSupplierCreditNoteSourceResolution(
            "Ambiguous",
            null,
            $"Hay {candidates.Length:N0} facturas candidatas para la nota {note.InvoiceNumber}; se requiere la referencia XML de la factura origen.");
    }

    internal static object BuildJournalPayload(
        ConciliacionDianSupplierInvoiceRowDto note,
        ConciliacionDianSupplierInvoiceRowDto sourceInvoice,
        SiigoReconciliationPurchase purchase,
        SiigoDocumentTypeLookupDto journalDocument,
        ICollection<string> issues)
    {
        if (!TryResolvePurchaseDue(purchase, out var purchaseDue, out var dueIssue))
            issues.Add(dueIssue);
        if (string.IsNullOrWhiteSpace(sourceInvoice.AccountCode))
            issues.Add($"La factura origen {sourceInvoice.InvoiceNumber} no tiene cuenta contable.");
        if (note.TotalValue <= 0m)
            issues.Add("La nota credito debe tener valor mayor a cero.");

        var baseValue = note.BaseAmount > 0m
            ? note.BaseAmount
            : Math.Max(0m, note.TotalValue - note.VatValue);
        var accountedValue = RoundCurrency(baseValue + note.VatValue);
        if (Math.Abs(accountedValue - note.TotalValue) > 0.01m)
        {
            issues.Add(
                $"La base e IVA de la nota ({accountedValue:N2}) no cuadran con el total ({note.TotalValue:N2}).");
        }

        var noteDate = DateOnly.TryParseExact(
            note.EmissionDateValue,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedDate)
            ? parsedDate
            : default;
        if (noteDate == default)
            issues.Add("La nota credito no tiene fecha de emision valida.");
        var purchaseDueDate = purchase.PaymentDueDate
            ?? purchase.Date
            ?? noteDate;
        if (purchaseDueDate == default)
            issues.Add($"La compra Siigo {purchase.Name} no devolvio la fecha de su vencimiento.");

        var identification = ExtractDigits(note.SupplierNit);
        if (identification.Length < 5)
            issues.Add("La nota credito no tiene NIT de proveedor valido.");

        var supplier = new
        {
            identification,
            branch_office = 0
        };
        var items = new List<Dictionary<string, object?>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["account"] = new { code = AccountsPayableCode, movement = "Debit" },
                ["customer"] = supplier,
                ["description"] = Truncate($"Aplicacion NC proveedor {note.InvoiceNumber} a {sourceInvoice.InvoiceNumber}", 200),
                ["due"] = new
                {
                    prefix = purchaseDue.Prefix,
                    consecutive = purchaseDue.Consecutive,
                    quote = 1,
                    date = purchaseDueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                },
                ["value"] = RoundCurrency(note.TotalValue)
            }
        };
        if (baseValue > 0m)
        {
            items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["account"] = new { code = sourceInvoice.AccountCode.Trim(), movement = "Credit" },
                ["customer"] = supplier,
                ["description"] = Truncate($"Reversion gasto NC {note.InvoiceNumber}", 200),
                ["value"] = RoundCurrency(baseValue)
            });
        }
        if (note.VatValue > 0m)
        {
            items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["account"] = new { code = DeductibleVatCode, movement = "Credit" },
                ["customer"] = supplier,
                ["description"] = Truncate($"Reversion IVA NC {note.InvoiceNumber}", 200),
                ["value"] = RoundCurrency(note.VatValue)
            });
        }

        return new
        {
            document = new { id = journalDocument.Id },
            date = noteDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            items,
            observations = Truncate(
                $"Nota credito recibida de proveedor importada desde DIAN. "
                + $"Nota {note.InvoiceNumber}; CUDE {note.Cufe}; factura origen {sourceInvoice.InvoiceNumber}; "
                + $"compra Siigo {purchase.Name}.",
                500)
        };
    }

    internal static SiigoDocumentTypeLookupDto ResolveCreditNoteJournalDocumentType(
        IReadOnlyList<SiigoDocumentTypeLookupDto> documentTypes)
    {
        var active = documentTypes
            .Where(static item => item.Active
                && item.Type.Equals("CC", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return active.FirstOrDefault(static item =>
                item.Code.Equals("1", StringComparison.OrdinalIgnoreCase)
                && NormalizeText($"{item.Name} {item.Description}")
                    .Contains("AJUSTES CONTABLES", StringComparison.OrdinalIgnoreCase))
            ?? active.FirstOrDefault(static item =>
                NormalizeText($"{item.Name} {item.Description}")
                    .Contains("AJUSTES CONTABLES", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "No encontre en Siigo el comprobante CC activo 'Ajustes contables' para aplicar notas de proveedor.");
    }

    private async Task<bool> TryRecoverCreatedReceiptAsync(
        string observationMarker,
        DateOnly startDate,
        DateOnly endDate,
        SiigoReconciliationPurchase purchase,
        DianSupplierCreditNotePurchaseDue purchaseDue,
        decimal balanceBefore,
        ConciliacionDianSupplierInvoiceRowDto note,
        ConciliacionDianSupplierInvoiceRowDto invoice,
        DianSupplierCreditNoteAutomationRowResultDto rowResult,
        CancellationToken ct)
    {
        try
        {
            var receipt = await _siigo.FindJournalByObservationAsync(
                observationMarker,
                startDate,
                endDate,
                ct);
            if (receipt is null)
                return false;
            if (!ValidateExistingJournal(receipt, note, purchase, out _))
                return false;

            var expectedBalance = RoundCurrency(balanceBefore - note.TotalValue);
            var verified = await WaitForExpectedPayableBalanceAsync(
                note.SupplierNit,
                purchaseDue,
                expectedBalance,
                ct);
            if (verified is null)
                return false;

            rowResult.PurchaseBalanceAfter = verified.Value;
            rowResult.SiigoAdjustmentId = receipt.Id;
            rowResult.SiigoAdjustmentName = receipt.Name;
            rowResult.Status = "Applied";
            rowResult.Message =
                $"Nota credito proveedor {note.InvoiceNumber} aplicada a {purchase.Name} "
                + $"(factura proveedor {invoice.InvoiceNumber}) mediante ajuste {receipt.Name}. "
                + $"La respuesta inicial fue ambigua, pero Siigo confirmo el documento y el saldo "
                + $"{purchaseDue.Label}: {balanceBefore:N2} -> {verified.Value:N2}.";
            await _dataverse.MarkConciliacionDianSupplierDocumentSiigoResultAsync(
                note.RecordId,
                success: true,
                message: rowResult.Message,
                siigoId: receipt.Id,
                siigoName: receipt.Name,
                responseJson: receipt.RawJson,
                ownsProcessingClaim: true,
                ct: ct);
            return true;
        }
        catch (Exception recoveryException)
        {
            _logger.LogWarning(
                recoveryException,
                "No fue posible recuperar por CUDE el ajuste Siigo de la nota DIAN {CreditNote}.",
                note.InvoiceNumber);
            return false;
        }
    }

    private async Task<decimal?> WaitForExpectedPayableBalanceAsync(
        string supplierIdentification,
        DianSupplierCreditNotePurchaseDue purchaseDue,
        decimal expectedBalance,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var balance = await _siigo.GetAccountsPayableBalanceAsync(
                supplierIdentification,
                purchaseDue.Prefix,
                purchaseDue.Consecutive,
                purchaseDue.Quote,
                ct);
            var effectiveBalance = RoundCurrency(balance ?? 0m);
            if (Math.Abs(effectiveBalance - expectedBalance) <= 0.01m)
                return effectiveBalance;
            if (attempt < 5)
                await Task.Delay(TimeSpan.FromMilliseconds(750), ct);
        }
        return null;
    }

    internal static bool ValidateExistingJournal(
        SiigoVoucherCreateResultDto receipt,
        ConciliacionDianSupplierInvoiceRowDto note,
        SiigoReconciliationPurchase purchase,
        out string issue)
    {
        issue = "";
        if (string.IsNullOrWhiteSpace(receipt.RawJson))
        {
            issue = "Siigo no devolvio el detalle del ajuste.";
            return false;
        }
        if (!TryResolvePurchaseDue(purchase, out var purchaseDue, out var purchaseDueIssue))
        {
            issue = purchaseDueIssue;
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(receipt.RawJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                issue = "el ajuste no devolvio items contables.";
                return false;
            }

            var expectedDueDate = purchase.PaymentDueDate ?? purchase.Date;
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("account", out var account)
                    || !account.TryGetProperty("code", out var code)
                    || !account.TryGetProperty("movement", out var movement)
                    || !string.Equals(code.GetString(), AccountsPayableCode, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(movement.GetString(), "Debit", StringComparison.OrdinalIgnoreCase)
                    || !item.TryGetProperty("value", out var value)
                    || value.ValueKind != JsonValueKind.Number
                    || Math.Abs(value.GetDecimal() - note.TotalValue) > 0.01m
                    || !item.TryGetProperty("due", out var due)
                    || !item.TryGetProperty("customer", out var customer)
                    || !customer.TryGetProperty("identification", out var identification)
                    || !CanonicalTaxId(identification.ToString())
                        .Equals(CanonicalTaxId(note.SupplierNit), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var prefixMatches = due.TryGetProperty("prefix", out var prefix)
                    && string.Equals(prefix.GetString(), purchaseDue.Prefix, StringComparison.OrdinalIgnoreCase);
                var consecutiveMatches = due.TryGetProperty("consecutive", out var consecutive)
                    && consecutive.TryGetInt32(out var parsedConsecutive)
                    && parsedConsecutive == purchaseDue.Consecutive;
                var quoteMatches = due.TryGetProperty("quote", out var quote)
                    && quote.TryGetInt32(out var parsedQuote)
                    && parsedQuote == 1;
                var dateMatches = !expectedDueDate.HasValue
                    || due.TryGetProperty("date", out var date)
                    && DateOnly.TryParseExact(
                        date.GetString(),
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var parsedDueDate)
                    && parsedDueDate == expectedDueDate.Value;
                if (prefixMatches && consecutiveMatches && quoteMatches && dateMatches)
                    return true;
            }

            issue =
                $"no contiene un debito {AccountsPayableCode} por {note.TotalValue:N2} al vencimiento "
                + $"{purchaseDue.Prefix}-{purchaseDue.Consecutive}, cuota 1, fecha "
                + $"{expectedDueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "esperada"}.";
            return false;
        }
        catch (JsonException ex)
        {
            issue = $"el detalle JSON del ajuste no es valido: {ex.Message}";
            return false;
        }
    }

    private static bool IsReceivedSupplierCreditNote(ConciliacionDianSupplierInvoiceRowDto row)
    {
        var type = NormalizeText(row.DocumentType);
        var group = NormalizeText(row.DianGroup);
        return (type.Contains("NOTA DE CREDITO", StringComparison.OrdinalIgnoreCase)
                || type.Contains("CREDIT NOTE", StringComparison.OrdinalIgnoreCase))
            && group.Contains("RECIBID", StringComparison.OrdinalIgnoreCase)
            && !group.Contains("EMITID", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReceivedSupplierInvoice(ConciliacionDianSupplierInvoiceRowDto row)
    {
        var type = NormalizeText(row.DocumentType);
        var group = NormalizeText(row.DianGroup);
        return type.Contains("FACTURA ELECTRONICA", StringComparison.OrdinalIgnoreCase)
            && !type.Contains("NOTA", StringComparison.OrdinalIgnoreCase)
            && group.Contains("RECIBID", StringComparison.OrdinalIgnoreCase)
            && !group.Contains("EMITID", StringComparison.OrdinalIgnoreCase);
    }

    private static DianSupplierCreditNoteAutomationRowResultDto BuildRowResult(
        ConciliacionDianSupplierInvoiceRowDto note) =>
        new()
        {
            RecordId = note.RecordId,
            CreditNoteNumber = note.InvoiceNumber,
            Cude = note.Cufe,
            SupplierNit = note.SupplierNit,
            SupplierName = note.SupplierName,
            TotalValue = note.TotalValue
        };

    private static void CompleteResult(DianSupplierCreditNoteAutomationResultDto result)
    {
        result.Applied = result.Rows.Count(static row => row.Status.Equals("Applied", StringComparison.OrdinalIgnoreCase));
        result.AlreadyApplied = result.Rows.Count(static row => row.Status.Equals("AlreadyApplied", StringComparison.OrdinalIgnoreCase));
        result.PendingSupplier = result.Rows.Count(static row => row.Status.Equals("PendingSupplier", StringComparison.OrdinalIgnoreCase));
        result.PendingSourcePurchase = result.Rows.Count(static row => row.Status.Equals("PendingSourcePurchase", StringComparison.OrdinalIgnoreCase));
        result.AmbiguousSourcePurchase = result.Rows.Count(static row => row.Status.Equals("AmbiguousSourcePurchase", StringComparison.OrdinalIgnoreCase));
        result.ConcurrentProcessing = result.Rows.Count(static row => row.Status.Equals("ConcurrentProcessing", StringComparison.OrdinalIgnoreCase));
        result.Failed = result.Rows.Count(static row => row.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase));
        result.AppliedValue = RoundCurrency(result.Rows
            .Where(static row => row.Status.Equals("Applied", StringComparison.OrdinalIgnoreCase))
            .Sum(static row => row.TotalValue));
        var completedRows = result.Applied + result.AlreadyApplied;
        result.Completed = !result.DryRun && completedRows == result.Eligible;
        result.Status = result.DryRun
            ? "DryRunReady"
            : result.Completed
                ? "Completed"
                : result.Failed > 0
                    ? "CompletedWithErrors"
                    : "Pending";
        result.Message = result.DryRun
            ? $"{result.Eligible:N0} nota(s) credito de proveedor lista(s) para validar/aplicar."
            : result.Completed
                ? $"Notas de proveedor completadas: {result.Applied:N0} aplicada(s), {result.AlreadyApplied:N0} ya aplicada(s)."
                : $"Notas de proveedor pendientes: {result.PendingSupplier:N0} sin proveedor, "
                  + $"{result.PendingSourcePurchase:N0} sin compra origen, {result.AmbiguousSourcePurchase:N0} ambiguas, "
                  + $"{result.ConcurrentProcessing:N0} concurrentes y {result.Failed:N0} con error.";
    }

    private static string BuildObservationMarker(string cude) =>
        $"CUDE {(cude ?? "").Trim()}";

    private static string BuildIdempotencyKey(string cude)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes((cude ?? "").Trim().ToLowerInvariant()));
        return $"DIANNC{Convert.ToHexString(bytes)[..24]}";
    }

    private static bool IsAmbiguousWriteHold(ConciliacionDianSupplierInvoiceRowDto row) =>
        row.AutomationState.Equals("VerificacionSiigoPendiente", StringComparison.OrdinalIgnoreCase)
        || row.ReviewReason.Contains("[SIIGO_WRITE_AMBIGUOUS]", StringComparison.OrdinalIgnoreCase);

    internal static bool TryResolvePurchaseDue(
        SiigoReconciliationPurchase purchase,
        out DianSupplierCreditNotePurchaseDue due,
        out string issue)
    {
        var prefix = (purchase.ProviderInvoicePrefix ?? "").Trim().ToUpperInvariant();
        var numberDigits = ExtractDigits(purchase.ProviderInvoiceNumber);
        if (string.IsNullOrWhiteSpace(prefix)
            || !int.TryParse(numberDigits, NumberStyles.None, CultureInfo.InvariantCulture, out var consecutive)
            || consecutive <= 0)
        {
            due = new DianSupplierCreditNotePurchaseDue("", 0, 1);
            issue =
                $"La compra Siigo {purchase.Name} no devolvio un vencimiento de proveedor valido "
                + $"({purchase.ProviderInvoicePrefix}-{purchase.ProviderInvoiceNumber}).";
            return false;
        }

        due = new DianSupplierCreditNotePurchaseDue(prefix, consecutive, 1);
        issue = "";
        return true;
    }

    private static string CanonicalTaxId(string value)
    {
        var digits = ExtractDigits(value).TrimStart('0');
        if (digits.Length == 10)
            digits = digits[..^1];
        return digits;
    }

    private static string NormalizePrefix(string value) =>
        new((value ?? "")
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    private static string NormalizeText(string value)
    {
        var decomposed = (value ?? "").Normalize(NormalizationForm.FormD);
        return new string(decomposed
                .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                .ToArray())
            .Normalize(NormalizationForm.FormC)
            .Trim()
            .ToUpperInvariant();
    }

    private static string ExtractDigits(string? value) =>
        new((value ?? "").Where(char.IsDigit).ToArray());

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed record DianSupplierCreditNoteSourceResolution(
    string Status,
    ConciliacionDianSupplierInvoiceRowDto? Invoice,
    string Message);

public sealed record DianSupplierCreditNotePurchaseDue(
    string Prefix,
    int Consecutive,
    int Quote)
{
    public string Label => $"{Prefix}-{Consecutive}, cuota {Quote}";
}
