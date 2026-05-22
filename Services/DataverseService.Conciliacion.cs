using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.Conciliacion;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string ConciliacionCreatedOnField = "createdon";
    private const string ConciliacionModifiedOnField = "modifiedon";
    private const string ClientPaymentMatchPreflightStatusField = "cr07a_preflightestado";
    private const string ClientPaymentMatchPreflightMessageField = "cr07a_preflightmensaje";
    private const string ClientPaymentMatchPreflightValidatedOnField = "cr07a_preflightfecha";
    private const string ClientPaymentMatchPreflightDebitField = "cr07a_preflightdebito";
    private const string ClientPaymentMatchPreflightCreditField = "cr07a_preflightcredito";
    private static readonly CultureInfo ConciliacionCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly Regex ConciliacionInvoiceTokenRegex = new(
        @"\b(?:FV|FVE|FEV|FEM|FE|FEDT|FEKT)[-\s]*\d+(?:[-\s]*\d+)?\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public async Task<ConciliacionBoardDto> GetConciliacionBoardAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        if (year < 2020 || month is < 1 or > 12)
            throw new InvalidOperationException("El periodo de conciliacion no es valido.");

        var start = new DateOnly(year, month, 1);
        var endExclusive = start.AddMonths(1);
        var cashFlowRowsTask = GetConciliacionCashFlowRowsAsync(start, endExclusive, ct);
        var clientPaymentsTask = GetConciliacionClientPaymentsAsync(start, endExclusive, ct);
        await Task.WhenAll(cashFlowRowsTask, clientPaymentsTask);

        var clientPayments = BuildConciliacionClientPaymentSummary(clientPaymentsTask.Result);
        var cashFlow = BuildConciliacionCashFlowSummary(cashFlowRowsTask.Result, clientPayments.Rows);
        var phases = BuildConciliacionPhases(cashFlow, clientPayments);
        var pending = clientPayments.PendingReview + cashFlow.PendingValidationRows;
        var suggested = clientPayments.Suggested;
        var approved = clientPayments.Approved;

        return new ConciliacionBoardDto
        {
            Year = year,
            Month = month,
            PeriodLabel = start.ToString("MMMM yyyy", ConciliacionCulture),
            StatusLabel = pending > 0 ? "Con pendientes" : suggested > 0 ? "Listo para aprobacion" : "En preparacion",
            StatusTone = pending > 0 ? "warning" : suggested > 0 ? "info" : "neutral",
            TotalPendingReview = pending,
            TotalSuggested = suggested,
            TotalApproved = approved,
            ClientPaymentEntries = clientPayments.TotalEntries,
            Phases = phases,
            CashFlow = cashFlow,
            ClientPayments = clientPayments
        };
    }

    public async Task<ConciliacionPreflightResultDto> ValidateConciliacionClientPaymentPreflightAsync(
        string recordId,
        CancellationToken ct = default)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var row = await GetConciliacionClientPaymentByIdAsync(metadata, normalizedRecordId, ct)
            ?? throw new InvalidOperationException("No encontramos el cruce de pago a validar.");

        var catalog = await GetConciliacionAccountCatalogAsync(ct);
        var preflight = ValidateConciliacionClientPaymentDraft(row, catalog);
        var isTechnicallyReady = preflight.Issues.Count == 0;
        var isApprovedForSiigo = IsConciliacionApprovedForSiigo(row.Status);
        var isReadyForSiigo = isTechnicallyReady && isApprovedForSiigo;
        var preflightStatus = isReadyForSiigo
            ? "ListoSiigo"
            : isTechnicallyReady
                ? "ValidadoPendienteAprobacion"
                : "BloqueadoSiigo";
        var nextStatus = row.Status;
        if (isReadyForSiigo)
        {
            nextStatus = "ListoSiigo";
        }
        else if (!isTechnicallyReady && IsConciliacionSiigoCandidateStatus(row.Status))
        {
            nextStatus = "BloqueadoSiigo";
        }

        var message = isReadyForSiigo
            ? "Prevalidacion correcta. El cruce queda listo para Siigo cuando activemos el envio."
            : isTechnicallyReady
                ? "Prevalidacion contable correcta. Falta aprobar el cruce antes de dejarlo listo para Siigo."
                : "Prevalidacion bloqueada: corrige los puntos indicados antes de enviar a Siigo.";
        var detailMessage = preflight.Issues.Count == 0
            ? message
            : $"{message} {string.Join(" ", preflight.Issues)}";

        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchStatusField, null, nextStatus, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReasonField, null, detailMessage, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightStatusField, null, preflightStatus, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightMessageField, null, detailMessage, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightValidatedOnField, (DateTimeOffset?)null, DateTimeOffset.UtcNow, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightDebitField, (decimal?)null, preflight.DebitTotal, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightCreditField, (decimal?)null, preflight.CreditTotal, force: true);

        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para guardar la prevalidacion.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})",
            "PATCH",
            payload,
            ct);

        var updatedRow = await GetConciliacionClientPaymentByIdAsync(metadata, normalizedRecordId, ct);
        return new ConciliacionPreflightResultDto
        {
            Message = message,
            IsReadyForSiigo = isReadyForSiigo,
            Issues = preflight.Issues,
            Row = updatedRow
        };
    }

    public async Task<ConciliacionSiigoDryRunResultDto> SimulateConciliacionClientPaymentSiigoSendAsync(
        string recordId,
        CancellationToken ct = default)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var row = await GetConciliacionClientPaymentByIdAsync(metadata, normalizedRecordId, ct)
            ?? throw new InvalidOperationException("No encontramos el cruce de pago a simular.");

        var catalog = await GetConciliacionAccountCatalogAsync(ct);
        var preflight = ValidateConciliacionClientPaymentDraft(row, catalog);
        var issues = new List<string>(preflight.Issues);
        if (!string.Equals(row.Status, "ListoSiigo", StringComparison.OrdinalIgnoreCase))
            issues.Add("El cruce debe estar en estado Listo Siigo antes de habilitar el envio real.");

        var payloadJson = "";
        var lineCount = 0;
        try
        {
            var payload = BuildConciliacionClientPaymentSiigoDryRunPayload(row, preflight, out lineCount);
            payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonOptions) { WriteIndented = true });
        }
        catch (InvalidOperationException ex)
        {
            issues.Add(ex.Message);
        }
        catch (JsonException)
        {
            issues.Add("El JSON de borrador Siigo no es valido y no se puede simular.");
        }

        var ready = issues.Count == 0;
        return new ConciliacionSiigoDryRunResultDto
        {
            Message = ready
                ? "Simulacion correcta. El payload esta completo y aun no se envio nada a Siigo."
                : "Simulacion con pendientes. Corrige los puntos indicados antes del envio real.",
            IsReadyForSiigo = ready,
            TargetEndpoint = "DRY-RUN /v1/vouchers",
            PayloadJson = payloadJson,
            LineCount = lineCount,
            DebitTotal = preflight.DebitTotal,
            CreditTotal = preflight.CreditTotal,
            Issues = issues,
            Row = row
        };
    }

    public async Task<ConciliacionActionResultDto> UpdateConciliacionClientPaymentStatusAsync(
        ConciliacionClientPaymentStatusRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var recordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var status = NormalizeConciliacionClientPaymentStatus(request.Status);
        var reason = (request.Reason ?? "").Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = status switch
            {
                "Aprobado" => "Aprobado desde modulo Conciliacion.",
                "Rechazado" => "Rechazado desde modulo Conciliacion.",
                "RevisionManual" => "Marcado para revision manual desde modulo Conciliacion.",
                _ => "Estado actualizado desde modulo Conciliacion."
            };
        }

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchStatusField, null, status, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReasonField, null, reason, force: true);

        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para actualizar el cruce.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({recordId})",
            "PATCH",
            payload,
            ct);

        var row = await GetConciliacionClientPaymentByIdAsync(metadata, recordId, ct);
        return new ConciliacionActionResultDto
        {
            Message = $"Cruce marcado como {ResolveConciliacionStatusLabel(status)}.",
            Row = row
        };
    }

    private async Task<IReadOnlyList<ConciliacionClientPaymentRowDto>> GetConciliacionClientPaymentsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildConciliacionClientPaymentAttributeSet(metadata, attributes);
        var select = BuildConciliacionClientPaymentSelect(metadata, attributes);
        var filter = BuildBillingDateFilter(ClientPaymentMatchMovementDateField, "date-only", startInclusive, endExclusive);
        var orderBy = Uri.EscapeDataString($"{ClientPaymentMatchMovementDateField} desc");
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={orderBy}";
        var rows = await GetDataverseAppEntitiesAsync(url, ct, AddFormattedValueHeaders);

        return rows
            .Select(item => ParseConciliacionClientPaymentRow(item, metadata))
            .Where(static row => row is not null)
            .Cast<ConciliacionClientPaymentRowDto>()
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderByDescending(static row => row.MovementDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.SourceFlow, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.ClientNames, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<ConciliacionClientPaymentRowDto?> GetConciliacionClientPaymentByIdAsync(
        RhEntityMetadata metadata,
        string recordId,
        CancellationToken ct)
    {
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildConciliacionClientPaymentAttributeSet(metadata, attributes);
        var select = BuildConciliacionClientPaymentSelect(metadata, attributes);
        var json = await CallDataverseAppGetJsonAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({recordId})?$select={select}",
            ct,
            AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        return ParseConciliacionClientPaymentRow(doc.RootElement, metadata);
    }

    private async Task<IReadOnlyList<ConciliacionCashFlowRowDto>> GetConciliacionCashFlowRowsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct)
    {
        var movementMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var movementAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(movementMetadata.LogicalName, ct);
        var movementSelect = BuildConciliacionSelectClause(movementMetadata, movementAttributes, new[]
        {
            movementMetadata.PrimaryIdField,
            movementMetadata.PrimaryNameField,
            CashFlowDateField,
            CashFlowBankField,
            CashFlowDescriptionField,
            CashFlowEntryField,
            CashFlowExitField,
            CashFlowSourceFlowField,
            CashFlowBankAccountCodeField,
            CashFlowBankAccountNameField,
            CashFlowRecipientField,
            CashFlowDestinationBankField,
            CashFlowDocumentTypeField,
            CashFlowObservationsField,
            CashFlowMovementTypeField,
            CashFlowStatusField,
            CashFlowSiigoDocumentIdField,
            CashFlowSiigoStatusField,
            CashFlowExternalKeyField,
            CashFlowReviewReasonField,
            ConciliacionModifiedOnField
        });
        var movementFilter = BuildBillingDateFilter(CashFlowDateField, "date-only", startInclusive, endExclusive);
        var movementUrl = $"/api/data/v9.2/{movementMetadata.EntitySetName}?$select={movementSelect}&$filter={Uri.EscapeDataString(movementFilter)}&$orderby={CashFlowDateField} desc";
        var movementRows = await GetDataverseAppEntitiesAsync(movementUrl, ct);

        var transferMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowTransferLogicalName,
            CashFlowTransferSetName,
            CashFlowTransferIdField,
            CashFlowTransferPrimaryNameField,
            ct);
        var transferAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(transferMetadata.LogicalName, ct);
        var transferSelect = BuildConciliacionSelectClause(transferMetadata, transferAttributes, new[]
        {
            transferMetadata.PrimaryIdField,
            transferMetadata.PrimaryNameField,
            CashFlowTransferDateField,
            CashFlowTransferValueField,
            CashFlowTransferSourceFlowField,
            CashFlowTransferFromField,
            CashFlowTransferToField,
            CashFlowTransferEntryField,
            CashFlowTransferExitField,
            CashFlowTransferDescriptionField,
            CashFlowTransferRecipientField,
            CashFlowTransferDestinationBankField,
            CashFlowTransferDocumentTypeField,
            CashFlowTransferObservationsField,
            CashFlowTransferStatusField,
            CashFlowTransferExternalKeyField,
            ConciliacionModifiedOnField
        });
        var transferFilter = BuildBillingDateFilter(CashFlowTransferDateField, "date-only", startInclusive, endExclusive);
        var transferUrl = $"/api/data/v9.2/{transferMetadata.EntitySetName}?$select={transferSelect}&$filter={Uri.EscapeDataString(transferFilter)}&$orderby={CashFlowTransferDateField} desc";
        var transferRows = await GetDataverseAppEntitiesAsync(transferUrl, ct);

        return movementRows
            .Select(item => ParseConciliacionCashFlowMovementRow(item, movementMetadata))
            .Concat(transferRows.Select(item => ParseConciliacionCashFlowTransferRow(item, transferMetadata)))
            .Where(static row => row is not null && !IsConciliacionPocketTransfer(row))
            .Cast<ConciliacionCashFlowRowDto>()
            .OrderByDescending(static row => row.MovementDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.SourceFlow, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Description, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ConciliacionCashFlowSummaryDto BuildConciliacionCashFlowSummary(
        IReadOnlyList<ConciliacionCashFlowRowDto> rows,
        IReadOnlyList<ConciliacionClientPaymentRowDto> clientPayments)
    {
        var matchByExternalKey = clientPayments
            .Where(static row => !string.IsNullOrWhiteSpace(row.MovementExternalKey))
            .GroupBy(static row => row.MovementExternalKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(row.ExternalKey)
                && matchByExternalKey.TryGetValue(row.ExternalKey.Trim(), out var match))
            {
                ApplyConciliacionClientPaymentMatch(row, match);
            }
        }

        var lastRun = rows
            .Select(static row => ParseConciliacionDateTimeOffset(row.ModifiedOnValue))
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .DefaultIfEmpty()
            .Max();

        return new ConciliacionCashFlowSummaryDto
        {
            TotalRows = rows.Count,
            MovementRows = rows.Count(static row => string.Equals(row.SourceKind, "Movimiento", StringComparison.OrdinalIgnoreCase)),
            TransferRows = rows.Count(static row => string.Equals(row.SourceKind, "Traslado", StringComparison.OrdinalIgnoreCase)),
            EntryRows = rows.Count(static row => string.Equals(row.Direction, "Entrada", StringComparison.OrdinalIgnoreCase)),
            ExitRows = rows.Count(static row => string.Equals(row.Direction, "Salida", StringComparison.OrdinalIgnoreCase)),
            OutgoingInvoiceRows = rows.Count(static row => string.Equals(row.DetectedTypeKey, "salida-fe", StringComparison.OrdinalIgnoreCase)),
            IncomingInvoiceRows = rows.Count(static row => string.Equals(row.DetectedTypeKey, "entrada-fe", StringComparison.OrdinalIgnoreCase)),
            CollectionAccountRows = rows.Count(static row => string.Equals(row.DetectedTypeKey, "cuenta-cobro", StringComparison.OrdinalIgnoreCase)),
            AccountingVoucherRows = rows.Count(static row =>
                string.Equals(row.DetectedTypeKey, "comprobante-contable", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.DetectedTypeKey, "entrada-comprobante", StringComparison.OrdinalIgnoreCase)),
            OrphanRows = rows.Count(static row => string.Equals(row.DetectedTypeKey, "huerfano", StringComparison.OrdinalIgnoreCase)),
            PendingValidationRows = rows.Count(static row => string.Equals(row.ValidationStatus, "Pendiente validar", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.ValidationStatus, "Revisar", StringComparison.OrdinalIgnoreCase)),
            PendingSiigoRows = rows.Count(static row => row.RegistrationStatus.Contains("Siigo pendiente", StringComparison.OrdinalIgnoreCase)),
            TotalEntries = RoundCurrency(rows.Sum(static row => row.EntryValue)),
            TotalExits = RoundCurrency(rows.Sum(static row => row.ExitValue)),
            TotalTransfers = RoundCurrency(rows.Where(static row => string.Equals(row.Direction, "Traslado", StringComparison.OrdinalIgnoreCase)).Sum(static row => row.Amount)),
            LastRunLabel = FormatConciliacionDateTimeDisplay(lastRun),
            Rows = rows
        };
    }

    private static string BuildConciliacionSelectClause(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        IEnumerable<string> fields)
    {
        var selected = fields
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Where(field => attributes.Count == 0
                || attributes.Contains(field)
                || string.Equals(field, metadata.PrimaryIdField, StringComparison.OrdinalIgnoreCase)
                || string.Equals(field, metadata.PrimaryNameField, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return selected.Length > 0 ? string.Join(",", selected) : metadata.PrimaryIdField;
    }

    private static ConciliacionCashFlowRowDto? ParseConciliacionCashFlowMovementRow(
        JsonElement item,
        RhEntityMetadata metadata)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, metadata.PrimaryIdField),
            ReadString(item, CashFlowMovementIdField)).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var date = ReadDateOnly(item, CashFlowDateField);
        var entry = RoundCurrency(ReadDecimal(item, CashFlowEntryField) ?? 0m);
        var exit = RoundCurrency(ReadDecimal(item, CashFlowExitField) ?? 0m);
        var row = new ConciliacionCashFlowRowDto
        {
            RecordId = recordId,
            SourceKind = "Movimiento",
            SourceKindLabel = "Movimiento",
            MovementDateValue = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            MovementDateDisplay = date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
            SourceFlow = ReadString(item, CashFlowSourceFlowField).Trim(),
            BankAccountCode = ReadString(item, CashFlowBankAccountCodeField).Trim(),
            BankAccountName = FirstNonEmpty(
                ReadString(item, CashFlowBankAccountNameField),
                ReadString(item, CashFlowBankField)).Trim(),
            EntryValue = entry,
            ExitValue = exit,
            Amount = RoundCurrency(Math.Max(entry, exit)),
            Description = ReadString(item, CashFlowDescriptionField).Trim(),
            Recipient = ReadString(item, CashFlowRecipientField).Trim(),
            DestinationBank = ReadString(item, CashFlowDestinationBankField).Trim(),
            DocumentType = ReadString(item, CashFlowDocumentTypeField).Trim(),
            Observations = ReadString(item, CashFlowObservationsField).Trim(),
            ExcelMovementType = ReadString(item, CashFlowMovementTypeField).Trim(),
            DataverseStatus = FirstNonEmpty(ReadString(item, CashFlowStatusField), "Importado").Trim(),
            SiigoStatus = ReadString(item, CashFlowSiigoStatusField).Trim(),
            ExternalKey = ReadString(item, CashFlowExternalKeyField).Trim(),
            ModifiedOnValue = ReadString(item, ConciliacionModifiedOnField).Trim()
        };

        row.Direction = entry > 0m ? "Entrada" : exit > 0m ? "Salida" : "Sin valor";
        row.DirectionTone = entry > 0m ? "success" : exit > 0m ? "danger" : "neutral";
        CompleteConciliacionCashFlowRow(
            row,
            ReadString(item, CashFlowSiigoDocumentIdField).Trim(),
            row.SiigoStatus);
        return row;
    }

    private static ConciliacionCashFlowRowDto? ParseConciliacionCashFlowTransferRow(
        JsonElement item,
        RhEntityMetadata metadata)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, metadata.PrimaryIdField),
            ReadString(item, CashFlowTransferIdField)).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var date = ReadDateOnly(item, CashFlowTransferDateField);
        var entry = RoundCurrency(ReadDecimal(item, CashFlowTransferEntryField) ?? 0m);
        var exit = RoundCurrency(ReadDecimal(item, CashFlowTransferExitField) ?? 0m);
        var value = RoundCurrency(ReadDecimal(item, CashFlowTransferValueField) ?? Math.Max(entry, exit));
        var transferFrom = ReadString(item, CashFlowTransferFromField).Trim();
        var transferTo = ReadString(item, CashFlowTransferToField).Trim();
        var row = new ConciliacionCashFlowRowDto
        {
            RecordId = recordId,
            SourceKind = "Traslado",
            SourceKindLabel = "Traslado interno",
            MovementDateValue = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            MovementDateDisplay = date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
            SourceFlow = ReadString(item, CashFlowTransferSourceFlowField).Trim(),
            BankAccountName = string.Join(" => ", new[] { transferFrom, transferTo }.Where(static value => !string.IsNullOrWhiteSpace(value))),
            Direction = "Traslado",
            DirectionTone = "neutral",
            EntryValue = entry,
            ExitValue = exit,
            Amount = value,
            Description = ReadString(item, CashFlowTransferDescriptionField).Trim(),
            Recipient = ReadString(item, CashFlowTransferRecipientField).Trim(),
            DestinationBank = ReadString(item, CashFlowTransferDestinationBankField).Trim(),
            DocumentType = ReadString(item, CashFlowTransferDocumentTypeField).Trim(),
            Observations = ReadString(item, CashFlowTransferObservationsField).Trim(),
            ExcelMovementType = "TRASLADO",
            DataverseStatus = FirstNonEmpty(ReadString(item, CashFlowTransferStatusField), "InternoNoSiigo").Trim(),
            ExternalKey = ReadString(item, CashFlowTransferExternalKeyField).Trim(),
            ModifiedOnValue = ReadString(item, ConciliacionModifiedOnField).Trim()
        };

        CompleteConciliacionCashFlowRow(row, "", "");
        return row;
    }

    private static void CompleteConciliacionCashFlowRow(
        ConciliacionCashFlowRowDto row,
        string siigoDocumentId,
        string siigoStatus)
    {
        var detection = ResolveConciliacionCashFlowDetectedType(row);
        row.DetectedTypeKey = detection.Key;
        row.DetectedTypeLabel = detection.Label;
        row.DetectedTypeTone = detection.Tone;
        row.ActionTargetKey = detection.TargetKey;
        row.CanValidate = !string.Equals(detection.Key, "traslado-interno", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(detection.Key, "traslado-interno", StringComparison.OrdinalIgnoreCase))
        {
            row.ValidationStatus = "Interno";
            row.ValidationTone = "neutral";
            row.RegistrationStatus = "Dataverse OK / no aplica Siigo";
            row.RegistrationTone = "neutral";
            row.InvoiceStatus = "No aplica";
            row.InvoiceStatusTone = "neutral";
            row.SiigoDocumentStatus = "No aplica";
            row.SiigoDocumentTone = "neutral";
            row.SiigoPaymentStatus = "No aplica";
            row.SiigoPaymentTone = "neutral";
            row.InvoiceBalanceStatus = "No aplica";
            row.DataversePaymentStatus = "No aplica";
            row.DataversePaymentTone = "neutral";
            return;
        }

        var siigoRegistered = IsConciliacionSiigoRegistered(siigoDocumentId, siigoStatus);
        row.ValidationStatus = "Pendiente validar";
        row.ValidationTone = "warning";
        row.RegistrationStatus = siigoRegistered
            ? "Dataverse OK / Siigo OK"
            : "Dataverse OK / Siigo pendiente";
        row.RegistrationTone = siigoRegistered ? "success" : "warning";
        row.InvoiceStatus = ResolveDefaultInvoiceStatus(row.DetectedTypeKey);
        row.InvoiceStatusTone = row.InvoiceStatus.Contains("OK", StringComparison.OrdinalIgnoreCase) ? "success" : "warning";
        row.SiigoDocumentStatus = siigoRegistered ? "Siigo OK" : "Pendiente Siigo";
        row.SiigoDocumentTone = siigoRegistered ? "success" : "warning";
        row.SiigoPaymentStatus = siigoRegistered ? "Pago/registro Siigo detectado" : "Pendiente envio Siigo";
        row.SiigoPaymentTone = siigoRegistered ? "success" : "warning";
        row.InvoiceBalanceStatus = string.Equals(row.DetectedTypeKey, "salida-fe", StringComparison.OrdinalIgnoreCase)
            ? "Saldo sin calcular"
            : "No aplica";
        row.DataversePaymentStatus = "Flujo Dataverse OK";
        row.DataversePaymentTone = "success";
    }

    private static void ApplyConciliacionClientPaymentMatch(
        ConciliacionCashFlowRowDto row,
        ConciliacionClientPaymentRowDto match)
    {
        row.MatchRecordId = match.RecordId;
        row.MatchStatus = match.Status;
        row.ActionTargetKey = "entradas-fe";
        row.CanValidate = true;

        if (!string.IsNullOrWhiteSpace(match.InvoiceNumbers)
            || string.Equals(row.DetectedTypeKey, "huerfano", StringComparison.OrdinalIgnoreCase))
        {
            row.DetectedTypeKey = "entrada-fe";
            row.DetectedTypeLabel = "Entrada FE - pago cliente";
            row.DetectedTypeTone = "success";
        }

        row.ValidationStatus = match.Status switch
        {
            "Aprobado" or "ListoSiigo" => "Validada",
            "Sugerido" => "Pendiente validar",
            "Rechazado" => "Rechazada",
            _ => "Revisar"
        };
        row.ValidationTone = match.Status switch
        {
            "Aprobado" or "ListoSiigo" => "success",
            "Sugerido" => "info",
            "Rechazado" => "danger",
            _ => "warning"
        };
        row.InvoiceStatus = string.IsNullOrWhiteSpace(match.InvoiceNumbers)
            ? "Factura no encontrada"
            : "Factura Dataverse OK";
        row.InvoiceStatusTone = string.IsNullOrWhiteSpace(match.InvoiceNumbers) ? "danger" : "success";
        row.DataversePaymentStatus = match.RetentionsTotal > 0m
            ? $"Pago Dataverse OK con retenciones {match.RetentionsTotal:N0}"
            : "Pago Dataverse OK sin retenciones";
        row.DataversePaymentTone = "success";
        row.SiigoPaymentStatus = string.Equals(match.Status, "ListoSiigo", StringComparison.OrdinalIgnoreCase)
            ? "Listo para envio Siigo"
            : "Pendiente envio Siigo";
        row.SiigoPaymentTone = string.Equals(match.Status, "ListoSiigo", StringComparison.OrdinalIgnoreCase) ? "info" : "warning";
        row.RegistrationStatus = string.Equals(match.Status, "ListoSiigo", StringComparison.OrdinalIgnoreCase)
            ? "Dataverse OK / listo Siigo"
            : "Dataverse OK / Siigo pendiente";
        row.RegistrationTone = string.Equals(match.Status, "ListoSiigo", StringComparison.OrdinalIgnoreCase) ? "info" : "warning";
    }

    private static (string Key, string Label, string Tone, string TargetKey) ResolveConciliacionCashFlowDetectedType(
        ConciliacionCashFlowRowDto row)
    {
        if (string.Equals(row.SourceKind, "Traslado", StringComparison.OrdinalIgnoreCase))
            return ("traslado-interno", "Traslado interno entre cuentas", "neutral", "flujo-caja");

        var text = BuildConciliacionCashFlowSearchText(row);
        if (string.Equals(row.Direction, "Entrada", StringComparison.OrdinalIgnoreCase))
        {
            if (ConciliacionInvoiceTokenRegex.IsMatch(text))
                return ("entrada-fe", "Entrada FE - pago cliente", "success", "entradas-fe");

            if (ContainsConciliacionAny(text, "ABONO INTERES", "APERTURA INVERSION", "INTERES", "RENDIMIENTO", "CANCELACION INVERSION", "CANCELACION INVERCION"))
                return ("entrada-comprobante", "Entrada - comprobante contable", "info", "comprobantes");

            return ("huerfano", "Entrada sin clasificar", "warning", "huerfanos");
        }

        if (string.Equals(row.Direction, "Salida", StringComparison.OrdinalIgnoreCase))
        {
            if (ContainsConciliacionAny(text, "CUENTA DE COBRO", "CUENTAS DE COBRO", "DOCUMENTO SOPORTE", "DOC SOPORTE", "DS "))
                return ("cuenta-cobro", "Documento soporte / cuenta de cobro", "info", "cuentas-cobro");

            if (ContainsConciliacionAny(text, "FACTURA ELECTRONICA", "FACTURA ELECTR", "FACTURA", "FEV", "FVE", "FE "))
                return ("salida-fe", "Salida FE - factura electronica", "success", "salidas-fe");

            if (ContainsConciliacionAny(
                text,
                "MI PLANILLA",
                "MIPLANILLA",
                "PLANILLA",
                "ETB",
                "ENEL",
                "CANCELACION INVERSION",
                "CANCELACION INVERCION",
                "GRAVAMEN",
                "GMF",
                "4X1000",
                "4 X 1000",
                "COMISION",
                "GASTO BANCARIO",
                "INTERES",
                "DIAN",
                "IMPUESTO"))
            {
                return ("comprobante-contable", "Salida - comprobante contable", "info", "comprobantes");
            }

            return ("huerfano", "Salida sin clasificar", "warning", "huerfanos");
        }

        return ("huerfano", "Sin clasificar", "warning", "huerfanos");
    }

    private static string ResolveDefaultInvoiceStatus(string detectedTypeKey)
    {
        return detectedTypeKey switch
        {
            "salida-fe" => "Pendiente cruce Dataverse",
            "cuenta-cobro" => "Se creara desde flujo",
            "comprobante-contable" => "No requiere factura",
            "entrada-comprobante" => "No requiere factura",
            "entrada-fe" => "Pendiente cruce factura",
            _ => "Pendiente clasificar"
        };
    }

    private static bool IsConciliacionPocketTransfer(ConciliacionCashFlowRowDto? row)
    {
        if (row is null)
            return false;

        return ContainsConciliacionAny(BuildConciliacionCashFlowSearchText(row), "BOLSILLO");
    }

    private static bool IsConciliacionSiigoRegistered(string siigoDocumentId, string siigoStatus)
    {
        if (!string.IsNullOrWhiteSpace(siigoDocumentId))
            return true;

        var status = (siigoStatus ?? "").Trim();
        if (status.Equals("si", StringComparison.OrdinalIgnoreCase)
            || status.Equals("sí", StringComparison.OrdinalIgnoreCase)
            || status.Equals("ok", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalized = status.ToUpperInvariant();
        return normalized.Contains("REGISTR", StringComparison.Ordinal)
            || normalized.Contains("SUBID", StringComparison.Ordinal)
            || normalized.Contains("ENVIAD", StringComparison.Ordinal)
            || normalized.Contains("CREAD", StringComparison.Ordinal);
    }

    private static string BuildConciliacionCashFlowSearchText(ConciliacionCashFlowRowDto row) =>
        string.Join(" ", new[]
        {
            row.Description,
            row.Recipient,
            row.DestinationBank,
            row.DocumentType,
            row.Observations,
            row.ExcelMovementType,
            row.BankAccountName,
            row.SourceFlow
        }).ToUpperInvariant();

    private static bool ContainsConciliacionAny(string text, params string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static ConciliacionClientPaymentSummaryDto BuildConciliacionClientPaymentSummary(
        IReadOnlyList<ConciliacionClientPaymentRowDto> rows)
    {
        var pendingRows = rows.Where(static row => IsConciliacionPendingReviewStatus(row.Status)).ToArray();
        var suggestedRows = rows.Where(static row => string.Equals(row.Status, "Sugerido", StringComparison.OrdinalIgnoreCase)).ToArray();
        var readyRows = rows.Where(static row => string.Equals(row.Status, "ListoSiigo", StringComparison.OrdinalIgnoreCase)).ToArray();
        var lastRun = rows
            .Select(static row => ParseConciliacionDateTimeOffset(row.ModifiedOnDisplay))
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .DefaultIfEmpty()
            .Max();

        return new ConciliacionClientPaymentSummaryDto
        {
            TotalRows = rows.Count,
            Suggested = suggestedRows.Length,
            Approved = rows.Count(static row => string.Equals(row.Status, "Aprobado", StringComparison.OrdinalIgnoreCase)),
            ReadyForSiigo = readyRows.Length,
            PreflightOk = rows.Count(static row => string.Equals(row.PreflightStatus, "ListoSiigo", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.PreflightStatus, "ValidadoPendienteAprobacion", StringComparison.OrdinalIgnoreCase)),
            PreflightBlocked = rows.Count(static row => string.Equals(row.PreflightStatus, "BloqueadoSiigo", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.Status, "BloqueadoSiigo", StringComparison.OrdinalIgnoreCase)),
            Rejected = rows.Count(static row => string.Equals(row.Status, "Rechazado", StringComparison.OrdinalIgnoreCase)),
            PendingReview = pendingRows.Length,
            DifferenceOutOfTolerance = rows.Count(static row => string.Equals(row.Status, "DiferenciaFueraRango", StringComparison.OrdinalIgnoreCase)),
            NoInvoiceToken = rows.Count(static row => string.Equals(row.Status, "SinFacturaDescripcion", StringComparison.OrdinalIgnoreCase)),
            NoInvoiceMatch = rows.Count(static row => string.Equals(row.Status, "FacturaNoEncontrada", StringComparison.OrdinalIgnoreCase)),
            AmbiguousInvoice = rows.Count(static row => string.Equals(row.Status, "FacturaAmbigua", StringComparison.OrdinalIgnoreCase)),
            TotalEntries = RoundCurrency(rows.Sum(static row => row.EntryValue)),
            SuggestedEntries = RoundCurrency(suggestedRows.Sum(static row => row.EntryValue)),
            ReadyForSiigoEntries = RoundCurrency(readyRows.Sum(static row => row.EntryValue)),
            PendingReviewEntries = RoundCurrency(pendingRows.Sum(static row => row.EntryValue)),
            LastRunLabel = FormatConciliacionDateTimeDisplay(lastRun),
            Rows = rows
        };
    }

    private static IReadOnlyList<ConciliacionPhaseDto> BuildConciliacionPhases(
        ConciliacionCashFlowSummaryDto cashFlow,
        ConciliacionClientPaymentSummaryDto clientPayments)
    {
        return new[]
        {
            BuildStaticConciliacionPhase(
                "flujo-caja",
                "Flujo de caja por banco",
                cashFlow.TotalRows > 0 ? "Activo" : "Sin datos",
                cashFlow.TotalRows > 0 ? "success" : "neutral",
                "Semanal y cierre mensual",
                cashFlow.LastRunLabel,
                "Validar cada fila antes de enviarla a Siigo y cruzar el extracto bancario al cierre.",
                new[]
                {
                    Step("Filas importadas", "Listo", "success", $"{cashFlow.TotalRows:N0} filas del periodo."),
                    Step("Tipo detectado", "Parcial", "info", "Clasificacion inicial por entrada/salida y texto."),
                    Step("Validacion", cashFlow.PendingValidationRows > 0 ? "Pendiente" : "Lista", cashFlow.PendingValidationRows > 0 ? "warning" : "success", $"{cashFlow.PendingValidationRows:N0} filas por validar."),
                    Step("Extracto mensual", "Falta", "warning", "Cruce banco vs flujo y tabla de cierre.")
                },
                $"Entradas {cashFlow.TotalEntries:N0}; salidas {cashFlow.TotalExits:N0}; traslados {cashFlow.TotalTransfers:N0}.",
                new[]
                {
                    "Importacion de flujo de caja Cloud/Copiers a Dataverse.",
                    "Separacion de traslados internos y omision de traslados de bolsillos.",
                    "Columna visual de tipo de comprobante detectado."
                },
                new[]
                {
                    "Persistir la categoria reasignada desde el popup.",
                    "Cruzar mensualmente contra extractos bancarios y saldos finales por banco.",
                    "Bloquear envio a Siigo hasta que la fila este validada y completa."
                }),
            BuildStaticConciliacionPhase(
                "salidas-fe",
                "Registro de Salidas FE",
                cashFlow.OutgoingInvoiceRows > 0 ? "Detectado" : "Sin filas",
                cashFlow.OutgoingInvoiceRows > 0 ? "info" : "neutral",
                "Por periodo",
                cashFlow.LastRunLabel,
                "Cruzar salidas con factura electronica contra Dataverse, Siigo y saldo de factura.",
                new[]
                {
                    Step("Filas candidatas", "Detectadas", "info", $"{cashFlow.OutgoingInvoiceRows:N0} salidas FE."),
                    Step("Factura Dataverse", "Falta", "warning", "Cruce DIAN/Dataverse pendiente."),
                    Step("Factura Siigo", "Falta", "warning", "Consulta de compras/egresos pendiente."),
                    Step("Pago Siigo", "Falta", "warning", "Registro de pago pendiente.")
                },
                "",
                new[]
                {
                    "Filtro lateral y tabla de salidas con factura electronica.",
                    "Estado visual para factura Dataverse, factura Siigo, pago Siigo y saldo."
                },
                new[]
                {
                    "Conectar cruce real contra gastos DIAN/Dataverse.",
                    "Consultar saldo de factura y pago en Siigo.",
                    "Crear prevalidacion completa antes del envio a Siigo."
                }),
            BuildStaticConciliacionPhase(
                "entradas-fe",
                "Registro de Entradas FE",
                clientPayments.PendingReview > 0 ? "Con pendientes" : clientPayments.Suggested > 0 ? "Listo para aprobar" : "Sin pendientes",
                clientPayments.PendingReview > 0 ? "warning" : clientPayments.Suggested > 0 ? "info" : "success",
                "Semanal",
                clientPayments.LastRunLabel,
                "Validar pagos de clientes, retenciones y borrador contable antes del envio a Siigo.",
                new[]
                {
                    Step("Entradas", "Importadas", "success", $"{clientPayments.TotalRows:N0} cruces."),
                    Step("Factura Dataverse", "Parcial", "info", $"{clientPayments.Suggested:N0} sugeridos."),
                    Step("Pago Dataverse", "Activo", "success", "Cruce guarda retenciones calculadas."),
                    Step("Subida Siigo", "Falta", "warning", $"{clientPayments.ReadyForSiigo:N0} listos para envio futuro.")
                },
                $"Valor revisado {clientPayments.TotalEntries:N0}; sugerido {clientPayments.SuggestedEntries:N0}; listo Siigo {clientPayments.ReadyForSiigoEntries:N0}.",
                new[]
                {
                    "Cruce de entradas contra facturacion Dataverse.",
                    "Aprobacion, revision, rechazo y prevalidacion pre-Siigo.",
                    "Borrador contable con retenciones y balance debito/credito."
                },
                new[]
                {
                    "Envio real a Siigo de los registros `ListoSiigo`.",
                    "Confirmar marca de pago registrado en Dataverse cuando el comprobante quede creado.",
                    "Reflejar cambios posteriores de Siigo hacia Dataverse."
                }),
            BuildStaticConciliacionPhase(
                "cuentas-cobro",
                "Registro de cuentas de cobro",
                cashFlow.CollectionAccountRows > 0 ? "Detectado" : "Sin filas",
                cashFlow.CollectionAccountRows > 0 ? "info" : "neutral",
                "Por actualizacion de flujo",
                cashFlow.LastRunLabel,
                "Crear automaticamente la cuenta de cobro en el modulo y completar retenciones alli.",
                new[]
                {
                    Step("Filas candidatas", "Detectadas", "info", $"{cashFlow.CollectionAccountRows:N0} cuentas de cobro."),
                    Step("Creacion app", "Falta", "warning", "Crear registro automaticamente desde flujo."),
                    Step("Retenciones", "Actual", "info", "Formulario existente en modulo cuentas de cobro."),
                    Step("Dataverse DIAN", "Falta", "warning", "Se confirma en importacion DIAN siguiente.")
                },
                "",
                new[]
                {
                    "Filtro y deteccion inicial desde flujo de caja.",
                    "Modulo de cuentas de cobro ya permite capturar retenciones."
                },
                new[]
                {
                    "Crear registros automaticamente en el modulo al actualizar flujo.",
                    "Subir a Siigo cuando retenciones esten completas y aprobadas.",
                    "Marcar subida a Dataverse en la siguiente importacion DIAN."
                }),
            BuildStaticConciliacionPhase(
                "comprobantes",
                "Registro de comprobantes contables",
                cashFlow.AccountingVoucherRows > 0 ? "Detectado" : "Sin filas",
                cashFlow.AccountingVoucherRows > 0 ? "info" : "neutral",
                "Semanal",
                cashFlow.LastRunLabel,
                "Validar comprobantes sin factura/documento soporte y preparar asiento completo.",
                new[]
                {
                    Step("Filas candidatas", "Detectadas", "info", $"{cashFlow.AccountingVoucherRows:N0} comprobantes."),
                    Step("Dataverse", "Flujo OK", "success", "Registro bancario ya existe."),
                    Step("Plantillas", "Parcial", "info", "Hay plantillas piloto para algunos casos."),
                    Step("Siigo", "Falta", "warning", "Crear journals/egresos automaticos.")
                },
                "",
                new[]
                {
                    "Deteccion de MI PLANILLA, ENEL, ETB, intereses, inversiones, gravamen y gastos bancarios.",
                    "Catalogo de cuentas Siigo y plantillas multi-linea ya existen como base."
                },
                new[]
                {
                    "Consolidar gravamen mensual por banco en un solo comprobante.",
                    "Partir MI PLANILLA por salud, pension, ARL y caja con cuentas contables separadas.",
                    "Validar que cada asiento tenga todas sus lineas antes de crear Siigo/Dataverse."
                }),
            BuildStaticConciliacionPhase(
                "huerfanos",
                "Registros huerfanos",
                cashFlow.OrphanRows > 0 ? "Con pendientes" : "Sin pendientes",
                cashFlow.OrphanRows > 0 ? "warning" : "success",
                "Continuo",
                cashFlow.LastRunLabel,
                "Reasignar categoria con popup y convertir correcciones frecuentes en reglas.",
                new[]
                {
                    Step("Filas sin tipo", "Pendiente", cashFlow.OrphanRows > 0 ? "warning" : "success", $"{cashFlow.OrphanRows:N0} registros."),
                    Step("Popup categoria", "Visual", "info", "Opciones restringidas por entrada/salida."),
                    Step("Guardado Dataverse", "Falta", "warning", "Campo/endpoint pendiente.")
                },
                "",
                new[]
                {
                    "Vista dedicada de huerfanos.",
                    "Popup visual para reasignar categoria segun entrada o salida."
                },
                new[]
                {
                    "Guardar reasignacion en Dataverse.",
                    "Crear reglas desde correcciones repetidas.",
                    "Reprocesar las filas despues de reasignarlas."
                })
        };
    }

    private static ConciliacionPhaseDto BuildStaticConciliacionPhase(
        string key,
        string label,
        string status,
        string tone,
        string cadence,
        string lastRun,
        string nextStep,
        IReadOnlyList<ConciliacionFlowStepDto> steps,
        string runSummary = "",
        IReadOnlyList<string>? readyItems = null,
        IReadOnlyList<string>? missingItems = null) =>
        new()
        {
            Key = key,
            Label = label,
            StatusLabel = status,
            StatusTone = tone,
            CadenceLabel = cadence,
            LastRunLabel = string.IsNullOrWhiteSpace(lastRun) ? "Sin log" : lastRun,
            RunSummary = string.IsNullOrWhiteSpace(runSummary) ? "Resumen pendiente de conectar a logs historicos." : runSummary,
            NextStep = nextStep,
            ReadyItems = readyItems ?? Array.Empty<string>(),
            MissingItems = missingItems ?? Array.Empty<string>(),
            Steps = steps
        };

    private static ConciliacionFlowStepDto Step(string label, string status, string tone, string summary) =>
        new()
        {
            Label = label,
            StatusLabel = status,
            StatusTone = tone,
            Summary = summary
        };

    private static ConciliacionClientPaymentRowDto? ParseConciliacionClientPaymentRow(
        JsonElement item,
        RhEntityMetadata metadata)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, metadata.PrimaryIdField),
            ReadString(item, ClientPaymentMatchIdField)).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var status = FirstNonEmpty(ReadString(item, ClientPaymentMatchStatusField), "Sin estado");
        var movementDate = ReadDateOnly(item, ClientPaymentMatchMovementDateField);
        var modifiedOn = ParseConciliacionDateTimeOffset(ReadString(item, ConciliacionModifiedOnField));
        var preflightStatus = ReadString(item, ClientPaymentMatchPreflightStatusField).Trim();
        var preflightValidatedOn = ParseConciliacionDateTimeOffset(ReadString(item, ClientPaymentMatchPreflightValidatedOnField));

        return new ConciliacionClientPaymentRowDto
        {
            RecordId = recordId,
            Status = status,
            StatusLabel = ResolveConciliacionStatusLabel(status),
            StatusTone = ResolveConciliacionStatusTone(status),
            Confidence = ReadInt(item, ClientPaymentMatchConfidenceField),
            Reason = ReadString(item, ClientPaymentMatchReasonField).Trim(),
            MovementId = ReadString(item, ClientPaymentMatchMovementIdField).Trim(),
            MovementExternalKey = ReadString(item, ClientPaymentMatchMovementExternalKeyField).Trim(),
            MovementDateValue = movementDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            MovementDateDisplay = movementDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
            SourceFlow = ReadString(item, ClientPaymentMatchSourceFlowField).Trim(),
            BankAccountCode = ReadString(item, ClientPaymentMatchBankCodeField).Trim(),
            BankAccountName = ReadString(item, ClientPaymentMatchBankNameField).Trim(),
            Description = ReadString(item, ClientPaymentMatchDescriptionField).Trim(),
            InvoiceNumbers = ReadString(item, ClientPaymentMatchInvoiceNumbersField).Trim(),
            ClientNames = ReadString(item, ClientPaymentMatchClientField).Trim(),
            EntryValue = RoundCurrency(ReadDecimal(item, ClientPaymentMatchEntryField) ?? 0m),
            InvoiceTotal = RoundCurrency(ReadDecimal(item, ClientPaymentMatchInvoiceTotalField) ?? 0m),
            RetentionsTotal = RoundCurrency((ReadDecimal(item, ClientPaymentMatchReteFteField) ?? 0m)
                + (ReadDecimal(item, ClientPaymentMatchReteIcaField) ?? 0m)
                + (ReadDecimal(item, ClientPaymentMatchRteIvaField) ?? 0m)),
            DifferenceValue = RoundCurrency(ReadDecimal(item, ClientPaymentMatchDifferenceField) ?? 0m),
            DraftJson = ReadString(item, ClientPaymentMatchDraftJsonField).Trim(),
            PreflightStatus = preflightStatus,
            PreflightStatusLabel = ResolveConciliacionPreflightStatusLabel(preflightStatus),
            PreflightStatusTone = ResolveConciliacionPreflightStatusTone(preflightStatus),
            PreflightMessage = ReadString(item, ClientPaymentMatchPreflightMessageField).Trim(),
            PreflightDebitTotal = RoundCurrency(ReadDecimal(item, ClientPaymentMatchPreflightDebitField) ?? 0m),
            PreflightCreditTotal = RoundCurrency(ReadDecimal(item, ClientPaymentMatchPreflightCreditField) ?? 0m),
            PreflightValidatedOnDisplay = FormatConciliacionDateTimeDisplay(preflightValidatedOn),
            ModifiedOnDisplay = modifiedOn?.ToString("O", CultureInfo.InvariantCulture) ?? ""
        };
    }

    private static HashSet<string> BuildConciliacionClientPaymentAttributeSet(
        RhEntityMetadata metadata,
        ISet<string> attributes)
    {
        var values = attributes.Count > 0
            ? new HashSet<string>(attributes, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            ClientPaymentMatchIdField,
            ClientPaymentMatchStatusField,
            ClientPaymentMatchConfidenceField,
            ClientPaymentMatchReasonField,
            ClientPaymentMatchMovementIdField,
            ClientPaymentMatchMovementExternalKeyField,
            ClientPaymentMatchMovementDateField,
            ClientPaymentMatchSourceFlowField,
            ClientPaymentMatchBankCodeField,
            ClientPaymentMatchBankNameField,
            ClientPaymentMatchDescriptionField,
            ClientPaymentMatchEntryField,
            ClientPaymentMatchInvoiceNumbersField,
            ClientPaymentMatchClientField,
            ClientPaymentMatchInvoiceTotalField,
            ClientPaymentMatchReteFteField,
            ClientPaymentMatchReteIcaField,
            ClientPaymentMatchRteIvaField,
            ClientPaymentMatchDifferenceField,
            ClientPaymentMatchDraftJsonField,
            ClientPaymentMatchPreflightStatusField,
            ClientPaymentMatchPreflightMessageField,
            ClientPaymentMatchPreflightValidatedOnField,
            ClientPaymentMatchPreflightDebitField,
            ClientPaymentMatchPreflightCreditField,
            ConciliacionCreatedOnField,
            ConciliacionModifiedOnField
        })
        {
            if (!string.IsNullOrWhiteSpace(field))
                values.Add(field);
        }

        return values;
    }

    private static string BuildConciliacionClientPaymentSelect(
        RhEntityMetadata metadata,
        ISet<string> attributes)
    {
        return string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            ClientPaymentMatchIdField,
            ClientPaymentMatchStatusField,
            ClientPaymentMatchConfidenceField,
            ClientPaymentMatchReasonField,
            ClientPaymentMatchMovementIdField,
            ClientPaymentMatchMovementExternalKeyField,
            ClientPaymentMatchMovementDateField,
            ClientPaymentMatchSourceFlowField,
            ClientPaymentMatchBankCodeField,
            ClientPaymentMatchBankNameField,
            ClientPaymentMatchDescriptionField,
            ClientPaymentMatchEntryField,
            ClientPaymentMatchInvoiceNumbersField,
            ClientPaymentMatchClientField,
            ClientPaymentMatchInvoiceTotalField,
            ClientPaymentMatchReteFteField,
            ClientPaymentMatchReteIcaField,
            ClientPaymentMatchRteIvaField,
            ClientPaymentMatchDifferenceField,
            ClientPaymentMatchDraftJsonField,
            ClientPaymentMatchPreflightStatusField,
            ClientPaymentMatchPreflightMessageField,
            ClientPaymentMatchPreflightValidatedOnField,
            ClientPaymentMatchPreflightDebitField,
            ClientPaymentMatchPreflightCreditField,
            ConciliacionCreatedOnField,
            ConciliacionModifiedOnField
        }
        .Where(field => !string.IsNullOrWhiteSpace(field) && attributes.Contains(field))
        .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyDictionary<string, ConciliacionAccountCatalogItem>> GetConciliacionAccountCatalogAsync(
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            AccountCatalogLogicalName,
            AccountCatalogSetName,
            AccountCatalogIdField,
            AccountCatalogPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildAccountCatalogAttributeSet(metadata, attributes);
        var rows = await GetAccountCatalogRowsAsync(metadata, attributes, ct);

        return rows
            .Where(static row => !string.IsNullOrWhiteSpace(row.Code))
            .GroupBy(static row => row.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group =>
                {
                    var row = group.First();
                    return new ConciliacionAccountCatalogItem(row.Code.Trim(), row.Name.Trim(), row.Active);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static ConciliacionPreflightValidation ValidateConciliacionClientPaymentDraft(
        ConciliacionClientPaymentRowDto row,
        IReadOnlyDictionary<string, ConciliacionAccountCatalogItem> accountCatalog)
    {
        var issues = new List<string>();
        var debitTotal = 0m;
        var creditTotal = 0m;

        if (!IsConciliacionSiigoCandidateStatus(row.Status))
            issues.Add("El estado actual debe resolverse antes de preparar envio a Siigo.");
        if (row.EntryValue <= 0m)
            issues.Add("El movimiento no tiene valor de entrada.");
        if (row.InvoiceTotal <= 0m)
            issues.Add("No hay total de factura asociado.");
        if (string.IsNullOrWhiteSpace(row.InvoiceNumbers))
            issues.Add("No hay numero de factura asociado.");
        if (string.IsNullOrWhiteSpace(row.ClientNames))
            issues.Add("No hay cliente asociado.");
        if (string.IsNullOrWhiteSpace(row.BankAccountCode))
            issues.Add("No hay cuenta bancaria contable.");
        if (Math.Abs(row.DifferenceValue) > RegistroPagosClientesBalancedTolerance)
            issues.Add($"La diferencia supera la tolerancia de {RegistroPagosClientesBalancedTolerance:N0}.");

        if (string.IsNullOrWhiteSpace(row.DraftJson))
        {
            issues.Add("No existe JSON de borrador Siigo.");
            return new ConciliacionPreflightValidation(RoundCurrency(debitTotal), RoundCurrency(creditTotal), issues);
        }

        try
        {
            using var doc = JsonDocument.Parse(row.DraftJson);
            var root = doc.RootElement;
            var type = ReadString(root, "type");
            if (!string.Equals(type, "ComprobanteIngresoSiigoBorrador", StringComparison.OrdinalIgnoreCase))
                issues.Add("El borrador no corresponde al tipo ComprobanteIngresoSiigoBorrador.");

            if (!root.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array || lines.GetArrayLength() == 0)
            {
                issues.Add("El borrador no tiene lineas contables.");
                return new ConciliacionPreflightValidation(RoundCurrency(debitTotal), RoundCurrency(creditTotal), issues);
            }

            var lineNumber = 0;
            foreach (var line in lines.EnumerateArray())
            {
                lineNumber++;
                var accountCode = ReadString(line, "accountCode").Trim();
                var accountName = ReadString(line, "accountName").Trim();
                var debit = RoundCurrency(ReadDecimal(line, "debit") ?? 0m);
                var credit = RoundCurrency(ReadDecimal(line, "credit") ?? 0m);
                debitTotal = RoundCurrency(debitTotal + debit);
                creditTotal = RoundCurrency(creditTotal + credit);

                if (ReadBool(line, "requiresAccountMapping"))
                    issues.Add($"Linea {lineNumber}: falta mapear cuenta contable para {FirstNonEmpty(accountName, "la linea")}.");
                if (debit < 0m || credit < 0m)
                    issues.Add($"Linea {lineNumber}: debito/credito no puede ser negativo.");
                if (debit > 0m && credit > 0m)
                    issues.Add($"Linea {lineNumber}: no puede tener debito y credito al mismo tiempo.");
                if (debit == 0m && credit == 0m)
                    continue;
                if (string.IsNullOrWhiteSpace(accountCode))
                {
                    issues.Add($"Linea {lineNumber}: falta codigo de cuenta.");
                    continue;
                }
                if (!accountCatalog.TryGetValue(accountCode, out var account))
                {
                    issues.Add($"Linea {lineNumber}: la cuenta {accountCode} no esta en el catalogo contable Siigo de Dataverse.");
                    continue;
                }
                if (!account.Active)
                    issues.Add($"Linea {lineNumber}: la cuenta {accountCode} esta inactiva.");
            }

            if (Math.Abs(debitTotal - creditTotal) > 1m)
                issues.Add($"El asiento no cuadra: debito {debitTotal:N2} vs credito {creditTotal:N2}.");
        }
        catch (JsonException)
        {
            issues.Add("El JSON de borrador Siigo no es valido.");
        }

        return new ConciliacionPreflightValidation(RoundCurrency(debitTotal), RoundCurrency(creditTotal), issues);
    }

    private static object BuildConciliacionClientPaymentSiigoDryRunPayload(
        ConciliacionClientPaymentRowDto row,
        ConciliacionPreflightValidation preflight,
        out int lineCount)
    {
        if (string.IsNullOrWhiteSpace(row.DraftJson))
            throw new InvalidOperationException("No existe JSON de borrador Siigo para armar la simulacion.");

        using var doc = JsonDocument.Parse(row.DraftJson);
        var root = doc.RootElement;
        var type = ReadString(root, "type");
        if (!string.Equals(type, "ComprobanteIngresoSiigoBorrador", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El borrador no corresponde al tipo ComprobanteIngresoSiigoBorrador.");
        if (!root.TryGetProperty("lines", out var linesElement)
            || linesElement.ValueKind != JsonValueKind.Array
            || linesElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("El borrador no tiene lineas contables para simular.");
        }

        var lines = new List<object>();
        foreach (var line in linesElement.EnumerateArray())
        {
            var debit = RoundCurrency(ReadDecimal(line, "debit") ?? 0m);
            var credit = RoundCurrency(ReadDecimal(line, "credit") ?? 0m);
            if (debit == 0m && credit == 0m)
                continue;

            var accountCode = ReadString(line, "accountCode").Trim();
            lines.Add(new
            {
                account = new
                {
                    code = accountCode,
                    name = ReadString(line, "accountName").Trim()
                },
                description = FirstNonEmpty(
                    ReadString(line, "description"),
                    ReadString(line, "detail"),
                    row.InvoiceNumbers,
                    row.Description).Trim(),
                thirdParty = FirstNonEmpty(ReadString(line, "thirdParty"), row.ClientNames).Trim(),
                detail = FirstNonEmpty(ReadString(line, "detail"), row.InvoiceNumbers).Trim(),
                debit,
                credit
            });
        }

        lineCount = lines.Count;
        if (lineCount == 0)
            throw new InvalidOperationException("El borrador no tiene lineas con debito o credito.");

        var paymentType = ResolveConciliacionClientPaymentType(row.SourceFlow, row.BankAccountCode);
        var movementDate = FirstNonEmpty(row.MovementDateValue, ReadString(root, "movement.date")).Trim();
        var invoices = ReadConciliacionDraftInvoices(root);

        return new
        {
            dryRun = true,
            targetEndpoint = "/v1/vouchers",
            note = "Payload de prueba generado por Conciliacion. No fue enviado a Siigo.",
            document = new
            {
                type = "RC",
                id = 7480,
                code = "1",
                name = "Recibo de caja"
            },
            paymentType,
            date = movementDate,
            customer = new
            {
                name = row.ClientNames,
                invoices = row.InvoiceNumbers
            },
            movement = new
            {
                id = row.MovementId,
                externalKey = row.MovementExternalKey,
                sourceFlow = row.SourceFlow,
                bankAccountCode = row.BankAccountCode,
                bankAccountName = row.BankAccountName,
                description = row.Description,
                entry = row.EntryValue
            },
            totals = new
            {
                invoiceTotal = row.InvoiceTotal,
                payment = row.EntryValue,
                retentions = row.RetentionsTotal,
                difference = row.DifferenceValue,
                debit = preflight.DebitTotal,
                credit = preflight.CreditTotal
            },
            invoices,
            items = lines
        };
    }

    private static object ResolveConciliacionClientPaymentType(string sourceFlow, string bankAccountCode)
    {
        var isCopiers = sourceFlow.Contains("Copiers", StringComparison.OrdinalIgnoreCase)
            || bankAccountCode.Contains("11100505", StringComparison.OrdinalIgnoreCase);

        return new
        {
            documentType = "RC",
            id = isCopiers ? 13568 : 13566,
            name = isCopiers ? "Bancolombia Copiers Ventas" : "Bancolombia Cloud Ventas"
        };
    }

    private static IReadOnlyList<object> ReadConciliacionDraftInvoices(JsonElement root)
    {
        if (!root.TryGetProperty("invoices", out var invoicesElement)
            || invoicesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<object>();
        }

        return invoicesElement
            .EnumerateArray()
            .Select(invoice => new
            {
                recordId = ReadString(invoice, "recordId").Trim(),
                number = ReadString(invoice, "number").Trim(),
                client = ReadString(invoice, "client").Trim(),
                total = RoundCurrency(ReadDecimal(invoice, "total") ?? 0m),
                vat = RoundCurrency(ReadDecimal(invoice, "vat") ?? 0m)
            })
            .Cast<object>()
            .ToArray();
    }

    private static string NormalizeConciliacionClientPaymentStatus(string? rawStatus)
    {
        var status = (rawStatus ?? "").Trim();
        var allowed = new HashSet<string>(new[]
        {
            "Sugerido",
            "Aprobado",
            "Rechazado",
            "RevisionManual"
        }, StringComparer.OrdinalIgnoreCase);

        if (!allowed.Contains(status))
            throw new InvalidOperationException("El estado solicitado no es valido.");

        return allowed.First(value => string.Equals(value, status, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsConciliacionPendingReviewStatus(string status)
    {
        return status switch
        {
            "DiferenciaFueraRango" => true,
            "SinFacturaDescripcion" => true,
            "FacturaNoEncontrada" => true,
            "FacturaAmbigua" => true,
            "RevisionManual" => true,
            "BloqueadoSiigo" => true,
            _ => false
        };
    }

    private static bool IsConciliacionApprovedForSiigo(string status) =>
        string.Equals(status, "Aprobado", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "ListoSiigo", StringComparison.OrdinalIgnoreCase);

    private static bool IsConciliacionSiigoCandidateStatus(string status) =>
        string.Equals(status, "Sugerido", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Aprobado", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "ListoSiigo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "BloqueadoSiigo", StringComparison.OrdinalIgnoreCase);

    private static string ResolveConciliacionStatusLabel(string status)
    {
        return status switch
        {
            "Sugerido" => "Sugerido",
            "Aprobado" => "Aprobado",
            "Rechazado" => "Rechazado",
            "RevisionManual" => "Revision manual",
            "ListoSiigo" => "Listo Siigo",
            "BloqueadoSiigo" => "Bloqueado pre-Siigo",
            "DiferenciaFueraRango" => "Diferencia fuera de rango",
            "SinFacturaDescripcion" => "Sin factura en descripcion",
            "FacturaNoEncontrada" => "Factura no encontrada",
            "FacturaAmbigua" => "Factura ambigua",
            _ => status
        };
    }

    private static string ResolveConciliacionStatusTone(string status)
    {
        return status switch
        {
            "Sugerido" => "info",
            "Aprobado" => "success",
            "Rechazado" => "danger",
            "RevisionManual" => "warning",
            "ListoSiigo" => "success",
            "BloqueadoSiigo" => "danger",
            "DiferenciaFueraRango" => "warning",
            "SinFacturaDescripcion" => "neutral",
            "FacturaNoEncontrada" => "danger",
            "FacturaAmbigua" => "warning",
            _ => "neutral"
        };
    }

    private static string ResolveConciliacionPreflightStatusLabel(string status)
    {
        return status switch
        {
            "ListoSiigo" => "Listo Siigo",
            "ValidadoPendienteAprobacion" => "OK, falta aprobar",
            "BloqueadoSiigo" => "Bloqueado",
            _ => string.IsNullOrWhiteSpace(status) ? "Sin validar" : status
        };
    }

    private static string ResolveConciliacionPreflightStatusTone(string status)
    {
        return status switch
        {
            "ListoSiigo" => "success",
            "ValidadoPendienteAprobacion" => "info",
            "BloqueadoSiigo" => "danger",
            _ => "neutral"
        };
    }

    private static DateTimeOffset? ParseConciliacionDateTimeOffset(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            || DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value))
        {
            return value;
        }

        return null;
    }

    private static string FormatConciliacionDateTimeDisplay(DateTimeOffset? value)
    {
        if (!value.HasValue || value.Value == default)
            return "Sin log";

        var bogota = TimeZoneInfo.ConvertTime(value.Value, MonthlyFinancialReconciliationHostedService.ResolveTimeZone("SA Pacific Standard Time"));
        return bogota.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    private sealed record ConciliacionPreflightValidation(
        decimal DebitTotal,
        decimal CreditTotal,
        IReadOnlyList<string> Issues);

    private sealed record ConciliacionAccountCatalogItem(
        string Code,
        string Name,
        bool Active);
}
