using System.Globalization;
using System.Text.Json;
using CotizadorInterno.Web.Models.Conciliacion;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string ConciliacionCreatedOnField = "createdon";
    private const string ConciliacionModifiedOnField = "modifiedon";
    private static readonly CultureInfo ConciliacionCulture = CultureInfo.GetCultureInfo("es-CO");

    public async Task<ConciliacionBoardDto> GetConciliacionBoardAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        if (year < 2020 || month is < 1 or > 12)
            throw new InvalidOperationException("El periodo de conciliacion no es valido.");

        var start = new DateOnly(year, month, 1);
        var endExclusive = start.AddMonths(1);
        var cashFlowSummaryTask = GetConciliacionCashFlowSummaryAsync(start, endExclusive, ct);
        var clientPaymentsTask = GetConciliacionClientPaymentsAsync(start, endExclusive, ct);
        await Task.WhenAll(cashFlowSummaryTask, clientPaymentsTask);

        var cashFlow = cashFlowSummaryTask.Result;
        var clientPayments = BuildConciliacionClientPaymentSummary(clientPaymentsTask.Result);
        var phases = BuildConciliacionPhases(cashFlow, clientPayments);
        var pending = clientPayments.PendingReview;
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
            ClientPayments = clientPayments
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

    private async Task<ConciliacionCashFlowSummary> GetConciliacionCashFlowSummaryAsync(
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
        var movementSelect = string.Join(",", new[]
        {
            movementMetadata.PrimaryIdField,
            CashFlowDateField,
            CashFlowEntryField,
            CashFlowExitField,
            CashFlowSourceFlowField,
            ConciliacionModifiedOnField
        }.Where(static field => !string.IsNullOrWhiteSpace(field)).Distinct(StringComparer.OrdinalIgnoreCase));
        var movementFilter = BuildBillingDateFilter(CashFlowDateField, "date-only", startInclusive, endExclusive);
        var movementUrl = $"/api/data/v9.2/{movementMetadata.EntitySetName}?$select={movementSelect}&$filter={Uri.EscapeDataString(movementFilter)}";
        var movementRows = await GetDataverseAppEntitiesAsync(movementUrl, ct);

        var transferMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowTransferLogicalName,
            CashFlowTransferSetName,
            CashFlowTransferIdField,
            CashFlowTransferPrimaryNameField,
            ct);
        var transferSelect = string.Join(",", new[]
        {
            transferMetadata.PrimaryIdField,
            CashFlowTransferDateField,
            CashFlowTransferValueField,
            CashFlowTransferSourceFlowField,
            ConciliacionModifiedOnField
        }.Where(static field => !string.IsNullOrWhiteSpace(field)).Distinct(StringComparer.OrdinalIgnoreCase));
        var transferFilter = BuildBillingDateFilter(CashFlowTransferDateField, "date-only", startInclusive, endExclusive);
        var transferUrl = $"/api/data/v9.2/{transferMetadata.EntitySetName}?$select={transferSelect}&$filter={Uri.EscapeDataString(transferFilter)}";
        var transferRows = await GetDataverseAppEntitiesAsync(transferUrl, ct);

        var movementModified = movementRows
            .Select(static item => ParseConciliacionDateTimeOffset(ReadString(item, ConciliacionModifiedOnField)))
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value);
        var transferModified = transferRows
            .Select(static item => ParseConciliacionDateTimeOffset(ReadString(item, ConciliacionModifiedOnField)))
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value);

        return new ConciliacionCashFlowSummary
        {
            Movements = movementRows.Count,
            Transfers = transferRows.Count,
            Entries = RoundCurrency(movementRows.Sum(static item => ReadDecimal(item, CashFlowEntryField) ?? 0m)),
            Exits = RoundCurrency(movementRows.Sum(static item => ReadDecimal(item, CashFlowExitField) ?? 0m)),
            TransferValue = RoundCurrency(transferRows.Sum(static item => ReadDecimal(item, CashFlowTransferValueField) ?? 0m)),
            LastRun = movementModified.Concat(transferModified).DefaultIfEmpty().Max()
        };
    }

    private static ConciliacionClientPaymentSummaryDto BuildConciliacionClientPaymentSummary(
        IReadOnlyList<ConciliacionClientPaymentRowDto> rows)
    {
        var pendingRows = rows.Where(static row => IsConciliacionPendingReviewStatus(row.Status)).ToArray();
        var suggestedRows = rows.Where(static row => string.Equals(row.Status, "Sugerido", StringComparison.OrdinalIgnoreCase)).ToArray();
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
            Rejected = rows.Count(static row => string.Equals(row.Status, "Rechazado", StringComparison.OrdinalIgnoreCase)),
            PendingReview = pendingRows.Length,
            DifferenceOutOfTolerance = rows.Count(static row => string.Equals(row.Status, "DiferenciaFueraRango", StringComparison.OrdinalIgnoreCase)),
            NoInvoiceToken = rows.Count(static row => string.Equals(row.Status, "SinFacturaDescripcion", StringComparison.OrdinalIgnoreCase)),
            NoInvoiceMatch = rows.Count(static row => string.Equals(row.Status, "FacturaNoEncontrada", StringComparison.OrdinalIgnoreCase)),
            AmbiguousInvoice = rows.Count(static row => string.Equals(row.Status, "FacturaAmbigua", StringComparison.OrdinalIgnoreCase)),
            TotalEntries = RoundCurrency(rows.Sum(static row => row.EntryValue)),
            SuggestedEntries = RoundCurrency(suggestedRows.Sum(static row => row.EntryValue)),
            PendingReviewEntries = RoundCurrency(pendingRows.Sum(static row => row.EntryValue)),
            LastRunLabel = FormatConciliacionDateTimeDisplay(lastRun),
            Rows = rows
        };
    }

    private static IReadOnlyList<ConciliacionPhaseDto> BuildConciliacionPhases(
        ConciliacionCashFlowSummary cashFlow,
        ConciliacionClientPaymentSummaryDto clientPayments)
    {
        return new[]
        {
            BuildStaticConciliacionPhase(
                "dian",
                "1. Importador DIAN robusto",
                "Pendiente",
                "warning",
                "Semanal",
                "Sin log robusto aun",
                "Crear staging con deduplicacion CUFE/CUDE antes de subir definitivo.",
                new[]
                {
                    Step("Exportable DIAN", "Actual", "info", "El Excel actual sigue siendo fuente."),
                    Step("Staging validado", "Falta", "warning", "Pendiente CUFE/CUDE y errores por fila."),
                    Step("Carga final", "Manual", "neutral", "Hoy lo hace el flujo existente.")
                }),
            BuildStaticConciliacionPhase(
                "autoclasificacion",
                "2. Autoclasificacion de gastos",
                "Parcial",
                "info",
                "Semanal",
                "Reglas y campos creados",
                "Completar reglas reales y convertir correcciones en nuevas reglas.",
                new[]
                {
                    Step("Campos Dataverse", "Listo", "success", "Cuenta, confianza, estado y motivo."),
                    Step("Reglas cuenta", "Activo", "success", "Motor semanal disponible."),
                    Step("Cloud/Copiers", "Falta", "warning", "Distribucion automatica por regla/mix.")
                }),
            BuildStaticConciliacionPhase(
                "cuentas-cobro",
                "3. Cuentas de cobro a Siigo",
                "Pendiente",
                "warning",
                "Bajo evento",
                "Sin ejecucion automatica",
                "Crear documento soporte en Siigo desde el modulo de cuentas de cobro.",
                new[]
                {
                    Step("Cuenta creada", "Actual", "info", "Nace en app/Dataverse."),
                    Step("Documento soporte", "Falta", "warning", "Pendiente API Siigo."),
                    Step("Confirmacion", "Falta", "warning", "Guardar id/respuesta Siigo.")
                }),
            BuildStaticConciliacionPhase(
                "flujo-caja",
                "4. Flujo de caja a Dataverse",
                cashFlow.Movements > 0 ? "Activo" : "Sin datos del periodo",
                cashFlow.Movements > 0 ? "success" : "neutral",
                "Semanal",
                FormatConciliacionDateTimeDisplay(cashFlow.LastRun),
                "Usar salidas importadas para cruzar pagos a proveedores y comprobantes.",
                new[]
                {
                    Step("Excel SharePoint", "Listo", "success", "Cloud y Copiers."),
                    Step("Movimientos", "Listo", "success", $"{cashFlow.Movements:N0} movimientos."),
                    Step("Traslados", "Separado", "success", $"{cashFlow.Transfers:N0} traslados internos."),
                    Step("Cruces salidas", "Siguiente", "warning", "Pendiente proveedores/comprobantes.")
                },
                $"Entradas {cashFlow.Entries:N0}; salidas {cashFlow.Exits:N0}; traslados {cashFlow.TransferValue:N0}."),
            BuildStaticConciliacionPhase(
                "pagos-clientes",
                "5. Pagos de clientes",
                clientPayments.PendingReview > 0 ? "Con pendientes" : clientPayments.Suggested > 0 ? "Listo para aprobar" : "Sin pendientes",
                clientPayments.PendingReview > 0 ? "warning" : clientPayments.Suggested > 0 ? "info" : "success",
                "Semanal",
                clientPayments.LastRunLabel,
                "Aprobar sugeridos y revisar diferencias antes de crear comprobantes Siigo.",
                new[]
                {
                    Step("Entradas", "Importadas", "success", $"{clientPayments.TotalRows:N0} cruces."),
                    Step("Cruce factura", "Ejecutado", "success", $"{clientPayments.Suggested:N0} sugeridos."),
                    Step("Revision", clientPayments.PendingReview > 0 ? "Pendiente" : "Lista", clientPayments.PendingReview > 0 ? "warning" : "success", $"{clientPayments.PendingReview:N0} por revisar."),
                    Step("Comprobante", "Futuro", "neutral", "Aun no envia a Siigo.")
                },
                $"Valor revisado {clientPayments.TotalEntries:N0}; sugerido {clientPayments.SuggestedEntries:N0}."),
            BuildStaticConciliacionPhase(
                "pagos-proveedores",
                "6. Pagos a proveedores",
                "Pendiente",
                "warning",
                "Semanal",
                "Sin ejecucion automatica",
                "Cruzar salidas bancarias contra gastos/documentos soporte.",
                new[]
                {
                    Step("Salidas banco", "Disponible", "info", "Ya estan en flujo de caja."),
                    Step("Match gasto", "Falta", "warning", "Proveedor/factura/valor."),
                    Step("Egreso Siigo", "Futuro", "neutral", "Borrador primero.")
                }),
            BuildStaticConciliacionPhase(
                "comprobantes",
                "7. Comprobantes contables manuales",
                "Parcial",
                "info",
                "Semanal",
                "Plantillas piloto creadas",
                "Ampliar plantillas para gastos bancarios, comisiones, intereses y ajustes.",
                new[]
                {
                    Step("Plantillas", "Piloto", "info", "ENEL, Acueducto, Colombia Movil."),
                    Step("Banco", "Falta", "warning", "Debe venir de flujo de caja."),
                    Step("Borrador Siigo", "Futuro", "neutral", "Despues de aprobacion.")
                }),
            BuildStaticConciliacionPhase(
                "supervision",
                "8. Bandeja de supervision",
                "En construccion",
                "info",
                "Continuo",
                "Modulo Conciliacion activo",
                "Conectar acciones de cada fase y bitacora de cambios.",
                new[]
                {
                    Step("Modulo", "Listo", "success", "Contenedor creado."),
                    Step("Pagos clientes", "Activo", "success", "Primera pestaña funcional."),
                    Step("Demas fases", "Siguiente", "warning", "Conectar excepciones.")
                }),
            BuildStaticConciliacionPhase(
                "reportes",
                "9. Conciliacion y reportes",
                "Parcial",
                "info",
                "Mensual",
                "Reporte financiero existente",
                "Integrar resultados aprobados, pendientes y acciones antes/despues al correo mensual.",
                new[]
                {
                    Step("Facturacion/NC", "Listo", "success", "Conciliacion mensual existente."),
                    Step("Gastos", "Parcial", "info", "Cruce mensual existe, falta integrar aprobaciones."),
                    Step("Reporte cierre", "Siguiente", "warning", "Unir con esta bandeja.")
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
        string runSummary = "") =>
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
            ConciliacionCreatedOnField,
            ConciliacionModifiedOnField
        }
        .Where(field => !string.IsNullOrWhiteSpace(field) && attributes.Contains(field))
        .Distinct(StringComparer.OrdinalIgnoreCase));
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
            _ => false
        };
    }

    private static string ResolveConciliacionStatusLabel(string status)
    {
        return status switch
        {
            "Sugerido" => "Sugerido",
            "Aprobado" => "Aprobado",
            "Rechazado" => "Rechazado",
            "RevisionManual" => "Revision manual",
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
            "DiferenciaFueraRango" => "warning",
            "SinFacturaDescripcion" => "neutral",
            "FacturaNoEncontrada" => "danger",
            "FacturaAmbigua" => "warning",
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

    private sealed class ConciliacionCashFlowSummary
    {
        public int Movements { get; set; }
        public int Transfers { get; set; }
        public decimal Entries { get; set; }
        public decimal Exits { get; set; }
        public decimal TransferValue { get; set; }
        public DateTimeOffset LastRun { get; set; }
    }
}
