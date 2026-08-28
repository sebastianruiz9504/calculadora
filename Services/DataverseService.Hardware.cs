using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Hardware;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const int HardwareStateWaitingDocumentation = 645250000;
    private const int HardwareStateOkForSupplierPayment = 645250001;
    private const int HardwareStatePaidToSupplier = 645250002;
    private const int HardwareStateInTransit = 645250003;
    private const int HardwareStateDeliveredAwaitingBilling = 645250004;
    private const int HardwareStateBilledAwaitingPayment = 645250005;
    private const int HardwareStateClosed = 645250006;
    private const string HardwareTableLogicalName = "cr07a_hardware";
    private const string HardwarePrimaryNameLogicalName = "cr07a_name";
    private const string HardwareImportKeyLogicalName = "cr07a_importkey";
    private const string HardwareSourceFileNameLogicalName = "cr07a_sourcefilename";
    private const string HardwareSourceRowNumberLogicalName = "cr07a_sourcerownumber";
    private const string HardwareQuantityLogicalName = "cr07a_cant";
    private const string HardwareSaleUnitLogicalName = "cr07a_ventaunidad";
    private const string HardwareTotalSaleLogicalName = "cr07a_precioventa";
    private const string HardwareUtilityLogicalName = "cr07a_utilidad";
    private const string HardwareMarginValueLogicalName = "cr07a_valormargen";
    private const string HardwareClientLookupLogicalName = "cr07a_cliente";
    private const string HardwareStateLogicalName = "cr07a_estado";
    private const string HardwareOwnerLogicalName = "ownerid";
    private const string HardwareSupplierUnitCostLogicalName = "cr07a_costountproveedor";
    private const string HardwareSupplierTotalLogicalName = "cr07a_totalesproveedor";
    private const string HardwareFreightValueLogicalName = "cr07a_valorflete";
    private const string HardwarePurchaseOrderNumberLogicalName = "cr07a_noorden";
    private const string HardwareSupplierLogicalName = "cr07a_proveedor";
    private const string HardwareSupplierDocumentGroupKeyLogicalName = "cr07a_grupoproforma";
    private const string HardwareSupplierDocumentGroupLabelLogicalName = "cr07a_nombreproforma";
    private const string HardwareOdcDateLogicalName = "cr07a_fechaodc";
    private const string HardwareSupplierPaymentDateLogicalName = "cr07a_fechapagoaproveedor";
    private const string HardwareDeliveryRecordDateLogicalName = "cr07a_fechaactadeentrega";
    private const string HardwareInvoiceNumberLogicalName = "cr07a_numerodefactura";
    private const string HardwareOrderPurchaseFileLogicalName = "cr07a_ordendecompra";
    private const string HardwareOrderPurchaseFileNameLogicalName = "cr07a_ordendecompra_name";
    private const string HardwareProformaFileLogicalName = "cr07a_adjuntarproforma";
    private const string HardwareProformaFileNameLogicalName = "cr07a_adjuntarproforma_name";
    private const string HardwareSupplierPurchaseOrderFileLogicalName = "cr07a_odcproveedor";
    private const string HardwareSupplierPurchaseOrderFileNameLogicalName = "cr07a_odcproveedor_name";
    private const string HardwareSupplierDocumentTypeLogicalName = "cr07a_tipodocumentoproveedor";
    private const string HardwareSupplierDocumentTypeProforma = "proforma";
    private const string HardwareSupplierDocumentTypePurchaseOrder = "odc-proveedor";
    private const string HardwareSupplierPaymentFileLogicalName = "cr07a_pagoaproveedor";
    private const string HardwareSupplierPaymentFileNameLogicalName = "cr07a_pagoaproveedor_name";
    private const string HardwareDeliveryRecordFileLogicalName = "cr07a_actadeentrega";
    private const string HardwareDeliveryRecordFileNameLogicalName = "cr07a_actadeentrega_name";
    private const string HardwareCreatedOnLogicalName = "createdon";
    private const string HardwareModifiedOnLogicalName = "modifiedon";
    private const string HardwareTableDisplayName = "Hardware";
    private const string HardwarePrimaryNameSchemaName = "cr07a_Name";
    private static readonly IReadOnlyDictionary<string, string> HardwareAllowedFileFields =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [HardwareOrderPurchaseFileLogicalName] = "orden-de-compra",
            [HardwareProformaFileLogicalName] = "proforma",
            [HardwareSupplierPurchaseOrderFileLogicalName] = "odc-proveedor",
            [HardwareSupplierPaymentFileLogicalName] = "pago-proveedor",
            [HardwareDeliveryRecordFileLogicalName] = "acta-entrega"
        };
    private static readonly string[] HardwareClientLookupFieldCandidates =
    {
        "_cr07a_cliente_value",
        "_cr07a_clienteid_value",
        "_cr07a_clientelookup_value"
    };
    private static readonly CultureInfo HardwareCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly string[] HardwareDateFormats =
    {
        "dd/MM/yyyy",
        "d/M/yyyy",
        "dd-MM-yyyy",
        "d-M-yyyy",
        "yyyy-MM-dd",
        "yyyy/MM/dd"
    };
    private static readonly IReadOnlyList<HardwareStateOptionDto> HardwareStates = new[]
    {
        new HardwareStateOptionDto
        {
            Value = HardwareStateWaitingDocumentation,
            Label = "En espera de documentación",
            Tone = "documentation",
            ActionKey = "register-documentation",
            ActionLabel = "Registrar documentación",
            HasAction = true
        },
        new HardwareStateOptionDto
        {
            Value = HardwareStateOkForSupplierPayment,
            Label = "Ok para pago a proveedor",
            Tone = "supplier-ready",
            ActionKey = "register-supplier-payment",
            ActionLabel = "Registrar pago a proveedor",
            HasAction = true
        },
        new HardwareStateOptionDto
        {
            Value = HardwareStatePaidToSupplier,
            Label = "Ok pago proveedor",
            Tone = "supplier-paid",
            ActionKey = "register-client-received",
            ActionLabel = "Registrar acta de entrega",
            HasAction = true
        },
        new HardwareStateOptionDto
        {
            Value = HardwareStateInTransit,
            Label = "En tránsito a oficina o cliente",
            Tone = "in-transit",
            ActionKey = "register-client-received",
            ActionLabel = "Registrar recibido cliente",
            HasAction = true
        },
        new HardwareStateOptionDto
        {
            Value = HardwareStateDeliveredAwaitingBilling,
            Label = "Entregado en espera de facturación",
            Tone = "awaiting-billing",
            ActionKey = "register-invoice",
            ActionLabel = "Registrar factura",
            HasAction = true
        },
        new HardwareStateOptionDto
        {
            Value = HardwareStateBilledAwaitingPayment,
            Label = "Facturado en espera de pago",
            Tone = "awaiting-payment",
            ActionKey = "register-client-payment",
            ActionLabel = "Registrar pago cliente",
            HasAction = true
        },
        new HardwareStateOptionDto
        {
            Value = HardwareStateClosed,
            Label = "Cerrado",
            Tone = "closed",
            ActionKey = "",
            ActionLabel = "",
            HasAction = false
        }
    };

    private static readonly IReadOnlyList<HardwareManagedColumnDefinition> HardwareSystemColumns = new[]
    {
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Import Key",
            SourceHeader = "Import Key",
            LogicalName = HardwareImportKeyLogicalName,
            SchemaName = "cr07a_ImportKey",
            Kind = HardwareAttributeKind.String,
            MaxLength = 128,
            IsSystemColumn = true
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Source File Name",
            SourceHeader = "Source File Name",
            LogicalName = HardwareSourceFileNameLogicalName,
            SchemaName = "cr07a_SourceFileName",
            Kind = HardwareAttributeKind.String,
            MaxLength = 200,
            IsSystemColumn = true
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Source Row Number",
            SourceHeader = "Source Row Number",
            LogicalName = HardwareSourceRowNumberLogicalName,
            SchemaName = "cr07a_SourceRowNumber",
            Kind = HardwareAttributeKind.Integer,
            IsSystemColumn = true
        }
    };
    private static readonly IReadOnlyList<HardwareManagedColumnDefinition> HardwareProvisioningColumns = new[]
    {
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Cantidad",
            SourceHeader = "Cantidad",
            LogicalName = HardwareQuantityLogicalName,
            SchemaName = "cr07a_Cant",
            Kind = HardwareAttributeKind.Integer,
            IsSystemColumn = false
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Venta unidad",
            SourceHeader = "Venta unidad",
            LogicalName = HardwareSaleUnitLogicalName,
            SchemaName = "cr07a_VentaUnidad",
            Kind = HardwareAttributeKind.Money,
            IsSystemColumn = false
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Precio venta",
            SourceHeader = "Precio venta",
            LogicalName = HardwareTotalSaleLogicalName,
            SchemaName = "cr07a_PrecioVenta",
            Kind = HardwareAttributeKind.Money,
            IsSystemColumn = false
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Utilidad",
            SourceHeader = "Utilidad",
            LogicalName = HardwareUtilityLogicalName,
            SchemaName = "cr07a_Utilidad",
            Kind = HardwareAttributeKind.Money,
            IsSystemColumn = false
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Valor margen",
            SourceHeader = "Valor margen",
            LogicalName = HardwareMarginValueLogicalName,
            SchemaName = "cr07a_ValorMargen",
            Kind = HardwareAttributeKind.Decimal,
            Precision = 2,
            IsSystemColumn = false
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Estado",
            SourceHeader = "Estado",
            LogicalName = HardwareStateLogicalName,
            SchemaName = "cr07a_Estado",
            Kind = HardwareAttributeKind.Integer,
            IsSystemColumn = false
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Fecha ODC",
            SourceHeader = "Fecha ODC",
            LogicalName = HardwareOdcDateLogicalName,
            SchemaName = "cr07a_FechaODC",
            Kind = HardwareAttributeKind.Date,
            IsSystemColumn = false
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Costo unt proveedor antes de IVA",
            SourceHeader = "Costo unitario proveedor",
            LogicalName = HardwareSupplierUnitCostLogicalName,
            SchemaName = "cr07a_CostoUntProveedor",
            Kind = HardwareAttributeKind.Money,
            IsSystemColumn = false
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Totales proveedor",
            SourceHeader = "Totales proveedor",
            LogicalName = HardwareSupplierTotalLogicalName,
            SchemaName = "cr07a_TotalesProveedor",
            Kind = HardwareAttributeKind.Money,
            IsSystemColumn = false
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Valor flete",
            SourceHeader = "Valor flete",
            LogicalName = HardwareFreightValueLogicalName,
            SchemaName = "cr07a_ValorFlete",
            Kind = HardwareAttributeKind.Money,
            IsSystemColumn = false
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "No orden",
            SourceHeader = "No orden",
            LogicalName = HardwarePurchaseOrderNumberLogicalName,
            SchemaName = "cr07a_NoOrden",
            Kind = HardwareAttributeKind.String,
            MaxLength = 100,
            IsSystemColumn = false
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Proveedor",
            SourceHeader = "Proveedor",
            LogicalName = HardwareSupplierLogicalName,
            SchemaName = "cr07a_Proveedor",
            Kind = HardwareAttributeKind.String,
            MaxLength = 200,
            IsSystemColumn = false
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Grupo proforma",
            SourceHeader = "Grupo proforma",
            LogicalName = HardwareSupplierDocumentGroupKeyLogicalName,
            SchemaName = "cr07a_GrupoProforma",
            Kind = HardwareAttributeKind.String,
            MaxLength = 120,
            IsSystemColumn = false
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Nombre proforma",
            SourceHeader = "Nombre proforma",
            LogicalName = HardwareSupplierDocumentGroupLabelLogicalName,
            SchemaName = "cr07a_NombreProforma",
            Kind = HardwareAttributeKind.String,
            MaxLength = 200,
            IsSystemColumn = false
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Fecha de pago a proveedor",
            SourceHeader = "Fecha pago proveedor",
            LogicalName = HardwareSupplierPaymentDateLogicalName,
            SchemaName = "cr07a_FechaPagoAProveedor",
            Kind = HardwareAttributeKind.Date,
            IsSystemColumn = false
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Fecha acta de entrega",
            SourceHeader = "Fecha acta de entrega",
            LogicalName = HardwareDeliveryRecordDateLogicalName,
            SchemaName = "cr07a_FechaActaDeEntrega",
            Kind = HardwareAttributeKind.Date,
            IsSystemColumn = false
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Número de factura",
            SourceHeader = "Número de factura",
            LogicalName = HardwareInvoiceNumberLogicalName,
            SchemaName = "cr07a_NumeroDeFactura",
            Kind = HardwareAttributeKind.String,
            MaxLength = 200,
            IsSystemColumn = false
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Tipo documento proveedor",
            SourceHeader = "Tipo documento proveedor",
            LogicalName = HardwareSupplierDocumentTypeLogicalName,
            SchemaName = "cr07a_TipoDocumentoProveedor",
            Kind = HardwareAttributeKind.String,
            MaxLength = 40,
            IsSystemColumn = false
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "ODC proveedor",
            SourceHeader = "ODC proveedor",
            LogicalName = HardwareSupplierPurchaseOrderFileLogicalName,
            SchemaName = "cr07a_ODCProveedor",
            Kind = HardwareAttributeKind.File,
            MaxLength = 131072,
            IsSystemColumn = false
        }
    };

    public Task<HardwareCsvPreviewResultDto> PreviewHardwareCsvAsync(
        string fileName,
        byte[] content,
        CancellationToken ct = default)
    {
        var document = ParseHardwareCsv(fileName, content);
        return Task.FromResult(new HardwareCsvPreviewResultDto
        {
            FileName = document.FileName,
            TableLogicalName = HardwareTableLogicalName,
            TableDisplayName = HardwareTableDisplayName,
            DetectedDelimiterLabel = GetHardwareDelimiterLabel(document.Delimiter),
            TotalRows = document.Rows.Count,
            TotalColumns = document.Columns.Count,
            SystemColumnsCount = HardwareSystemColumns.Count,
            SystemColumns = HardwareSystemColumns.Select(static item => item.LogicalName).ToList(),
            Columns = document.Columns
                .Select(column => new HardwareCsvColumnDto
                {
                    Index = column.Index,
                    SourceHeader = column.SourceHeader,
                    DisplayLabel = column.DisplayLabel,
                    LogicalName = column.LogicalName,
                    SchemaName = column.SchemaName,
                    DataverseType = GetHardwareAttributeKindLabel(column.Kind),
                    ExampleValue = column.ExampleValue
                })
                .ToList(),
            Message = document.Rows.Count == 0
                ? "Se detectaron columnas, pero no hay filas con datos para importar."
                : $"Vista previa lista: {document.Rows.Count} fila(s) y {document.Columns.Count} columna(s)."
        });
    }

    public async Task<HardwareProvisionResultDto> ProvisionHardwareCsvAsync(
        string fileName,
        byte[] content,
        CancellationToken ct = default)
    {
        var document = ParseHardwareCsv(fileName, content);
        if (document.Rows.Count == 0)
            throw new InvalidOperationException("El archivo no tiene filas con datos para importar.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var user = httpContext.User;
        var tableCreated = false;
        if (await TryResolveHardwareEntityMetadataAsync(user, ct) is null)
        {
            await CreateHardwareEntityAsync(user, ct);
            tableCreated = true;
        }

        var existingAttributes = await LoadHardwareAttributesAsync(user, ct);
        var createdColumns = new List<string>();
        var existingColumns = new List<string>();

        foreach (var column in document.Columns.Concat(HardwareSystemColumns))
        {
            var matchedAttribute = FindMatchingHardwareAttribute(existingAttributes, column);
            if (matchedAttribute is not null)
            {
                column.ResolvedLogicalName = matchedAttribute.LogicalName;
                existingColumns.Add(matchedAttribute.LogicalName);
                continue;
            }

            await CreateHardwareAttributeAsync(column, user, ct);
            createdColumns.Add(column.LogicalName);
        }

        if (tableCreated || createdColumns.Count > 0)
        {
            await PublishHardwareEntityAsync(user, ct);
        }

        var metadata = await ResolveHardwareEntityMetadataAsync(user, ct);
        await ResolveHardwareColumnLogicalNamesAsync(document.Columns.Concat(HardwareSystemColumns).ToList(), user, ct);
        var importedCount = 0;
        var skippedDuplicates = 0;

        foreach (var row in document.Rows)
        {
            var importKey = ComputeHardwareImportKey(row);
            if (await HardwareRecordExistsAsync(metadata.EntitySetName, importKey, user, ct))
            {
                skippedDuplicates++;
                continue;
            }

            var payload = BuildHardwareRecordPayload(document, row, metadata.PrimaryNameField, importKey);
            await CallDataverseSendAsync($"/api/data/v9.2/{metadata.EntitySetName}", "POST", payload, user, ct);
            importedCount++;
        }

        return new HardwareProvisionResultDto
        {
            Message = BuildHardwareProvisionMessage(tableCreated, createdColumns.Count, importedCount, skippedDuplicates),
            TableLogicalName = metadata.LogicalName,
            EntitySetName = metadata.EntitySetName,
            TableCreated = tableCreated,
            CreatedColumnsCount = createdColumns.Count,
            ExistingColumnsCount = existingColumns.Count,
            ImportedCount = importedCount,
            SkippedDuplicatesCount = skippedDuplicates,
            CreatedColumns = createdColumns,
            ExistingColumns = existingColumns
        };
    }

    public async Task<HardwareBoardDto> GetHardwareBoardAsync(
        int? stateValue = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken ct = default,
        bool currentOwnerOnly = false,
        CurrentUserInfo? ownerOverride = null,
        bool filterByCreatedOn = false)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var user = httpContext.User;
        var currentUser = currentOwnerOnly ? ownerOverride ?? await GetCurrentUserAsync(ct) : null;
        if (currentOwnerOnly && string.IsNullOrWhiteSpace(currentUser?.SystemUserId))
            throw new InvalidOperationException("No fue posible resolver el owner autenticado para filtrar Hardware.");

        var metadata = await TryResolveHardwareEntityMetadataAsync(user, ct);
        if (metadata is null)
        {
            return new HardwareBoardDto
            {
                Message = "La tabla Hardware aun no existe en Dataverse.",
                StateOptions = HardwareStates.ToList()
            };
        }

        await AutoClosePaidHardwareRecordsAsync(user, ct);

        await EnsureHardwareWorkflowSchemaAsync(user, ct);
        metadata = await ResolveHardwareEntityMetadataAsync(user, ct);
        var attributes = await LoadHardwareAttributesAsync(user, ct);
        if ((startDate.HasValue || endDate.HasValue) && !filterByCreatedOn && !HasHardwareAttribute(attributes, HardwareOdcDateLogicalName))
            throw new InvalidOperationException($"La tabla Hardware no tiene la columna {HardwareOdcDateLogicalName} para filtrar por fecha ODC.");

        var selectFields = BuildHardwareBoardSelectFields(metadata, attributes);
        var filters = BuildHardwareBoardFilters(stateValue, startDate, endDate, attributes, filterByCreatedOn);
        if (currentOwnerOnly)
        {
            filters.Add(
                $"{BuildDashboardLookupValuePropertyName(HardwareOwnerLogicalName)} eq {NormalizeGuid(currentUser!.SystemUserId, nameof(currentUser.SystemUserId))}");
        }
        var filter = filters.Count > 0
            ? $"&$filter={Uri.EscapeDataString(string.Join(" and ", filters))}"
            : "";
        var orderBy = filterByCreatedOn
            ? $"{HardwareCreatedOnLogicalName} desc"
            : HasHardwareAttribute(attributes, HardwareOdcDateLogicalName)
            ? $"{HardwareOdcDateLogicalName} desc,{HardwareModifiedOnLogicalName} desc"
            : $"{HardwareModifiedOnLogicalName} desc";
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={string.Join(",", selectFields.Distinct(StringComparer.OrdinalIgnoreCase))}" +
            filter +
            $"&$orderby={Uri.EscapeDataString(orderBy)}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        var rows = items
            .Select(item => BuildHardwareBoardRowDto(metadata, attributes, item))
            .Where(row => row is not null)
            .Select(row => row!)
            .ToList();

        return new HardwareBoardDto
        {
            Message = rows.Count == 0
                ? "No hay registros de Hardware para mostrar con el filtro actual."
                : $"Se cargaron {rows.Count} registro(s) de Hardware.",
            DateFilterStartValue = FormatHardwareDateValue(startDate),
            DateFilterEndValue = FormatHardwareDateValue(endDate),
            DateFilterLabel = BuildHardwareDateFilterLabel(startDate, endDate, filterByCreatedOn),
            TotalCount = rows.Count,
            SelectedStateValue = stateValue,
            StateOptions = HardwareStates.ToList(),
            StateSummaries = BuildHardwareStateSummaries(rows),
            Warnings = BuildHardwareWarnings(attributes),
            Rows = rows,
            SupplierPaymentHistoryRows = BuildHardwareSupplierPaymentHistoryRows(rows)
        };
    }

    public async Task<HardwareOrderCreateResultDto> CreateHardwareOrderDraftAsync(
        HardwareOrderCreateRequest request,
        CancellationToken ct = default,
        CurrentUserInfo? ownerOverride = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var user = httpContext.User;
        var currentUser = ownerOverride ?? await GetCurrentUserAsync(ct);
        if (string.IsNullOrWhiteSpace(currentUser?.SystemUserId))
            throw new InvalidOperationException("No fue posible resolver el owner autenticado para crear Hardware.");

        await EnsureProvisioningHardwareSchemaAsync(user, ct);

        var metadata = await ResolveHardwareEntityMetadataAsync(user, ct);
        var attributes = await LoadHardwareAttributesAsync(user, ct);
        foreach (var fieldName in new[]
                 {
                     HardwareQuantityLogicalName,
                     HardwareSaleUnitLogicalName,
                     HardwareTotalSaleLogicalName,
                     HardwareUtilityLogicalName,
                     HardwareMarginValueLogicalName,
                     HardwareStateLogicalName,
                     HardwareSupplierUnitCostLogicalName,
                     HardwareSupplierTotalLogicalName,
                     HardwarePurchaseOrderNumberLogicalName,
                     HardwareSupplierLogicalName,
                     HardwareSupplierDocumentGroupKeyLogicalName,
                     HardwareSupplierDocumentGroupLabelLogicalName,
                     HardwareOdcDateLogicalName,
                     HardwareSupplierDocumentTypeLogicalName
                 })
        {
            EnsureHardwareAttributeExists(attributes, fieldName);
        }

        var purchaseOrderNumber = RequireHardwareText(request.PurchaseOrderNumber, "cr07a_noorden");
        var odcDate = ParseHardwareStageDate(request.OdcDateValue, "cr07a_fechaodc");
        var clientId = NormalizeOptionalGuid(request.ClientId);
        if (string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(request.ClientName))
            clientId = await ResolveCopiersClientIdAsync(request.ClientName.Trim(), ct);
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("Selecciona un cliente valido desde el buscador.");

        var lines = (request.Lines ?? new List<HardwareOrderLineCreateRequest>())
            .Where(line => line is not null)
            .ToList();
        if (lines.Count == 0)
            throw new InvalidOperationException("Agrega al menos una fila de hardware.");

        var clientNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            HardwareTableLogicalName,
            HardwareClientLookupLogicalName,
            HardwareClientLookupLogicalName,
            user,
            ct);
        var ownerNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            HardwareTableLogicalName,
            HardwareOwnerLogicalName,
            HardwareOwnerLogicalName,
            user,
            ct);

        var createdRecords = new List<HardwareBoardRowDto>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            var payload = BuildHardwareOrderDraftPayload(
                metadata,
                lines[index],
                index,
                purchaseOrderNumber,
                odcDate,
                clientId,
                clientNavigationProperty,
                currentUser.SystemUserId,
                ownerNavigationProperty);

            using var response = await SendDataversePayloadWithRepresentationAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}",
                "POST",
                payload,
                user,
                ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var createdId = ExtractRhRecordId(response, body, metadata.PrimaryIdField);
            createdRecords.Add(await GetHardwareRecordByIdAsync(metadata, createdId, user, ct));
        }

        return new HardwareOrderCreateResultDto
        {
            Message = createdRecords.Count == 1
                ? "Se creo 1 fila de Hardware."
                : $"Se crearon {createdRecords.Count} filas de Hardware.",
            Records = createdRecords
        };
    }

    public async Task<HardwareBulkEditResultDto> UpdateHardwareCommercialDraftAsync(
        HardwareOrderLineEditRequest request,
        CancellationToken ct = default,
        CurrentUserInfo? ownerOverride = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var user = httpContext.User;
        var currentUser = ownerOverride ?? await GetCurrentUserAsync(ct);
        if (string.IsNullOrWhiteSpace(currentUser?.SystemUserId))
            throw new InvalidOperationException("No fue posible resolver el owner autenticado para editar Hardware.");

        await EnsureProvisioningHardwareSchemaAsync(user, ct);

        var metadata = await ResolveHardwareEntityMetadataAsync(user, ct);
        var attributes = await LoadHardwareAttributesAsync(user, ct);
        foreach (var fieldName in new[]
                 {
                     HardwareQuantityLogicalName,
                     HardwareSaleUnitLogicalName,
                     HardwareTotalSaleLogicalName,
                     HardwareUtilityLogicalName,
                     HardwareMarginValueLogicalName,
                     HardwareStateLogicalName,
                     HardwareSupplierUnitCostLogicalName,
                     HardwareSupplierTotalLogicalName,
                     HardwarePurchaseOrderNumberLogicalName,
                     HardwareSupplierLogicalName,
                     HardwareSupplierDocumentGroupKeyLogicalName,
                     HardwareSupplierDocumentGroupLabelLogicalName,
                     HardwareOdcDateLogicalName,
                     HardwareClientLookupLogicalName,
                     HardwareSupplierDocumentTypeLogicalName
                 })
        {
            EnsureHardwareAttributeExists(attributes, fieldName);
        }

        var normalizedRecordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var currentRecord = await GetHardwareRecordByIdAsync(metadata, normalizedRecordId, user, ct);
        EnsureHardwareRecordsOwnedByCurrentUser(new[] { currentRecord }, currentUser);
        EnsureHardwareActionState(
            NormalizeHardwareStateValue(currentRecord.StateValue),
            HardwareStateWaitingDocumentation,
            currentRecord.StateLabel);

        var primaryNameField = string.IsNullOrWhiteSpace(metadata.PrimaryNameField)
            ? HardwarePrimaryNameLogicalName
            : metadata.PrimaryNameField;
        var name = RequireHardwareText(request.Name, "Producto / referencia");
        if (name.Length > 200)
            name = name[..200];

        var purchaseOrderNumber = RequireHardwareText(request.PurchaseOrderNumber, "cr07a_noorden");
        var odcDate = ParseHardwareStageDate(request.OdcDateValue, "cr07a_fechaodc");
        var quantity = ParseHardwareOrderQuantity(request.Quantity, 0);
        var supplierUnitCost = ParseHardwareStageCurrency(request.SupplierUnitCost, "cr07a_costountproveedor");
        var saleUnit = ParseHardwareStageCurrency(request.SaleUnit, "cr07a_ventaunidad");
        var supplierTotal = RoundCurrency(quantity * supplierUnitCost);
        var priceSale = RoundCurrency(quantity * saleUnit);
        var marginValue = CalculateHardwareMarginValue(priceSale, supplierTotal);
        var utility = CalculateHardwareUtility(priceSale, marginValue);

        var clientId = NormalizeOptionalGuid(request.ClientId);
        if (string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(request.ClientName))
            clientId = await ResolveCopiersClientIdAsync(request.ClientName.Trim(), ct);
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("Selecciona un cliente valido desde el buscador.");

        var clientNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            HardwareTableLogicalName,
            HardwareClientLookupLogicalName,
            HardwareClientLookupLogicalName,
            user,
            ct);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [primaryNameField] = name,
            [HardwareQuantityLogicalName] = quantity,
            [HardwareSaleUnitLogicalName] = saleUnit,
            [HardwareTotalSaleLogicalName] = priceSale,
            [HardwareSupplierUnitCostLogicalName] = supplierUnitCost,
            [HardwareSupplierTotalLogicalName] = supplierTotal,
            [HardwareUtilityLogicalName] = utility,
            [HardwareMarginValueLogicalName] = marginValue,
            [HardwarePurchaseOrderNumberLogicalName] = purchaseOrderNumber,
            [HardwareSupplierLogicalName] = RequireHardwareText(request.Provider, "cr07a_proveedor"),
            [HardwareSupplierDocumentGroupKeyLogicalName] = ResolveHardwareSupplierDocumentGroupKey(
                request.SupplierDocumentGroupKey,
                request.SupplierDocumentGroupLabel,
                purchaseOrderNumber,
                request.Provider,
                0),
            [HardwareSupplierDocumentGroupLabelLogicalName] = ResolveHardwareSupplierDocumentGroupLabel(
                request.SupplierDocumentGroupLabel,
                request.Provider,
                0),
            [HardwareOdcDateLogicalName] = odcDate,
            [$"{clientNavigationProperty}@odata.bind"] = $"/{ClientsEntitySetName}({clientId})"
        };

        await PatchHardwareRecordAsync(metadata.EntitySetName, normalizedRecordId, payload, user, ct);
        var updatedRecord = await GetHardwareRecordByIdAsync(metadata, normalizedRecordId, user, ct);

        return new HardwareBulkEditResultDto
        {
            Records = new[] { updatedRecord },
            Message = "Se actualizó la línea de Hardware."
        };
    }

    public async Task<HardwareBulkEditResultDto> DeleteHardwareCommercialDraftAsync(
        string recordId,
        CancellationToken ct = default,
        CurrentUserInfo? ownerOverride = null)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var user = httpContext.User;
        var currentUser = ownerOverride ?? await GetCurrentUserAsync(ct);
        if (string.IsNullOrWhiteSpace(currentUser?.SystemUserId))
            throw new InvalidOperationException("No fue posible resolver el owner autenticado para eliminar Hardware.");

        await EnsureProvisioningHardwareSchemaAsync(user, ct);

        var metadata = await ResolveHardwareEntityMetadataAsync(user, ct);
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var currentRecord = await GetHardwareRecordByIdAsync(metadata, normalizedRecordId, user, ct);
        EnsureHardwareRecordsOwnedByCurrentUser(new[] { currentRecord }, currentUser);
        EnsureHardwareActionState(
            NormalizeHardwareStateValue(currentRecord.StateValue),
            HardwareStateWaitingDocumentation,
            currentRecord.StateLabel);

        using var response = await CallRhDataverseResponseAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})",
            "DELETE",
            user,
            ct,
            customizeRequest: request => request.Headers.TryAddWithoutValidation("If-Match", "*"));
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        return new HardwareBulkEditResultDto
        {
            Message = "Se eliminó la línea de Hardware."
        };
    }

    public async Task<HardwareSaveResultDto> SaveHardwareStageAsync(
        HardwareStageSaveRequest request,
        CancellationToken ct = default,
        bool requireCurrentOwner = false,
        CurrentUserInfo? ownerOverride = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var user = httpContext.User;
        await EnsureProvisioningHardwareSchemaAsync(user, ct);

        var metadata = await ResolveHardwareEntityMetadataAsync(user, ct);
        var normalizedRecordIds = ResolveHardwareStageRecordIds(request);
        var currentRecords = new List<HardwareBoardRowDto>(normalizedRecordIds.Count);
        foreach (var recordId in normalizedRecordIds)
            currentRecords.Add(await GetHardwareRecordByIdAsync(metadata, recordId, user, ct));

        if (requireCurrentOwner)
            EnsureHardwareRecordsOwnedByCurrentUser(currentRecords, ownerOverride ?? await GetCurrentUserAsync(ct));

        var normalizedActionKey = NormalizeHardwareCell(request.ActionKey).ToLowerInvariant();
        var currentStates = currentRecords
            .Select(record => NormalizeHardwareStateValue(record.StateValue))
            .Distinct()
            .ToList();
        if (currentStates.Count != 1)
            throw new InvalidOperationException("Todas las filas seleccionadas deben estar en el mismo estado para avanzar.");

        var currentState = currentStates[0];
        var message = "";
        int? expectedStateAfterSave = null;
        string? requiredPerRecordFileAfterSave = null;
        var payloadsByRecordId = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);

        switch (normalizedActionKey)
        {
            case "register-documentation":
                EnsureHardwareActionState(currentState, HardwareStateWaitingDocumentation, currentRecords[0].StateLabel);
                var purchaseOrderNumber = RequireHardwareText(request.PurchaseOrderNumber, "No orden");
                var supplierDocumentType = NormalizeHardwareSupplierDocumentType(request.SupplierDocumentType);
                var freightSplits = SplitHardwareFreight(ParseHardwareStageOptionalNonNegativeCurrency(request.FreightValue, "Valor flete"), currentRecords.Count);
                var documentationRows = ResolveHardwareDocumentationRows(request, currentRecords);
                EnsureHardwareOrderFilePresent(currentRecords, HardwareOrderPurchaseFileLogicalName, "Adjuntar ODC cliente");
                if (supplierDocumentType == HardwareSupplierDocumentTypePurchaseOrder)
                {
                    EnsureHardwareOrderFilePresent(currentRecords, HardwareSupplierPurchaseOrderFileLogicalName, "Adjuntar ODC al proveedor");
                }
                else
                {
                    EnsureHardwareSupplierDocumentFilePresentForEachGroup(
                        currentRecords,
                        HardwareProformaFileLogicalName,
                        "Adjuntar proforma");
                }

                var nextDocumentationState = supplierDocumentType == HardwareSupplierDocumentTypePurchaseOrder
                    ? HardwareStatePaidToSupplier
                    : HardwareStateOkForSupplierPayment;
                expectedStateAfterSave = nextDocumentationState;

                for (var index = 0; index < currentRecords.Count; index++)
                {
                    var current = currentRecords[index];
                    var row = documentationRows[NormalizeGuid(current.RecordId, nameof(current.RecordId))];
                    var supplierUnitCost = ParseHardwareStageCurrency(row.SupplierUnitCost, "Costo Unt Proveedor antes de IVA");
                    var supplierTotal = RoundCurrency(Math.Max(current.Quantity, 0) * supplierUnitCost);
                    var priceSale = RoundCurrency(Math.Max(current.Quantity, 0) * current.SaleUnit);
                    var freightValue = freightSplits[index];
                    var marginValue = CalculateHardwareMarginValue(priceSale, supplierTotal);
                    var utility = CalculateHardwareUtility(priceSale, marginValue);

                    payloadsByRecordId[current.RecordId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        [HardwarePurchaseOrderNumberLogicalName] = purchaseOrderNumber,
                        [HardwareFreightValueLogicalName] = freightValue,
                        [HardwareOdcDateLogicalName] = ParseHardwareStageDate(row.OdcDateValue, "Fecha ODC"),
                        [HardwareSupplierUnitCostLogicalName] = supplierUnitCost,
                        [HardwareSupplierTotalLogicalName] = supplierTotal,
                        [HardwareTotalSaleLogicalName] = priceSale,
                        [HardwareUtilityLogicalName] = utility,
                        [HardwareMarginValueLogicalName] = marginValue,
                        [HardwareSupplierLogicalName] = RequireHardwareText(row.Provider, "Proveedor"),
                        [HardwareSupplierDocumentGroupKeyLogicalName] = ResolveHardwareSupplierDocumentGroupKey(
                            row.SupplierDocumentGroupKey,
                            row.SupplierDocumentGroupLabel,
                            purchaseOrderNumber,
                            row.Provider,
                            index),
                        [HardwareSupplierDocumentGroupLabelLogicalName] = ResolveHardwareSupplierDocumentGroupLabel(
                            row.SupplierDocumentGroupLabel,
                            row.Provider,
                            index),
                        [HardwareSupplierDocumentTypeLogicalName] = supplierDocumentType,
                        [HardwareStateLogicalName] = nextDocumentationState
                    };
                }

                if (supplierDocumentType == HardwareSupplierDocumentTypePurchaseOrder)
                {
                    message = currentRecords.Count == 1
                        ? "Documentación registrada con ODC al proveedor. Se omitió Ok para pago a proveedor y el hardware pasó a Ok pago proveedor."
                        : $"Documentación registrada con ODC al proveedor en {currentRecords.Count} fila(s). Se omitió Ok para pago a proveedor y el hardware pasó a Ok pago proveedor.";
                }
                else
                {
                    message = currentRecords.Count == 1
                        ? "Documentación registrada con proforma. El hardware pasó a Ok para pago a proveedor."
                        : $"Documentación registrada con proforma en {currentRecords.Count} fila(s). El hardware pasó a Ok para pago a proveedor.";
                }
                break;

            case "register-supplier-payment":
                EnsureHardwareActionState(currentState, HardwareStateOkForSupplierPayment, currentRecords[0].StateLabel);
                await EnsureHardwareFilePresentOnEachRecordAsync(
                    metadata,
                    currentRecords,
                    HardwareSupplierPaymentFileLogicalName,
                    "Adjuntar pago a proveedor",
                    user,
                    ct);
                foreach (var current in currentRecords)
                {
                    payloadsByRecordId[current.RecordId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        [HardwareSupplierPaymentDateLogicalName] = ParseHardwareStageDate(request.SupplierPaymentDateValue, "Fecha de pago a proveedor"),
                        [HardwareStateLogicalName] = HardwareStatePaidToSupplier
                    };
                }

                expectedStateAfterSave = HardwareStatePaidToSupplier;
                requiredPerRecordFileAfterSave = HardwareSupplierPaymentFileLogicalName;
                message = currentRecords.Count == 1
                    ? "Pago a proveedor registrado. El hardware pasó a Ok pago proveedor."
                    : $"Pago a proveedor registrado en {currentRecords.Count} fila(s). El hardware pasó a Ok pago proveedor.";
                break;

            case "register-received":
                EnsureHardwareActionState(currentState, HardwareStatePaidToSupplier, currentRecords[0].StateLabel);
                foreach (var current in currentRecords)
                {
                    payloadsByRecordId[current.RecordId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        [HardwareStateLogicalName] = HardwareStateInTransit
                    };
                }

                expectedStateAfterSave = HardwareStateInTransit;
                message = currentRecords.Count == 1
                    ? "Recibido aprobado por comercial. El hardware pasó a En tránsito a oficina o cliente."
                    : $"Recibido aprobado por comercial en {currentRecords.Count} fila(s). El hardware pasó a En tránsito a oficina o cliente.";
                break;

            case "register-client-received":
                EnsureHardwareActionStateOneOf(
                    currentState,
                    new[] { HardwareStatePaidToSupplier, HardwareStateInTransit },
                    currentRecords[0].StateLabel);
                await EnsureHardwareFilePresentOnEachRecordAsync(
                    metadata,
                    currentRecords,
                    HardwareDeliveryRecordFileLogicalName,
                    "Adjuntar acta de entrega",
                    user,
                    ct);
                foreach (var current in currentRecords)
                {
                    payloadsByRecordId[current.RecordId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        [HardwareDeliveryRecordDateLogicalName] = ParseHardwareStageDate(request.DeliveryRecordDateValue, "Fecha acta de entrega"),
                        [HardwareStateLogicalName] = HardwareStateDeliveredAwaitingBilling
                    };
                }

                expectedStateAfterSave = HardwareStateDeliveredAwaitingBilling;
                requiredPerRecordFileAfterSave = HardwareDeliveryRecordFileLogicalName;
                message = currentRecords.Count == 1
                    ? "Recibido cliente registrado. El hardware pasó a Entregado en espera de facturación."
                    : $"Recibido cliente registrado en {currentRecords.Count} fila(s). El hardware pasó a Entregado en espera de facturación.";
                break;

            case "register-invoice":
                EnsureHardwareActionState(currentState, HardwareStateDeliveredAwaitingBilling, currentRecords[0].StateLabel);
                var invoiceNumber = await ResolveHardwareInvoiceNumberAsync(request.InvoiceNumber, user, ct);
                foreach (var current in currentRecords)
                {
                    payloadsByRecordId[current.RecordId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        [HardwareInvoiceNumberLogicalName] = invoiceNumber,
                        [HardwareStateLogicalName] = HardwareStateBilledAwaitingPayment
                    };
                }

                expectedStateAfterSave = HardwareStateBilledAwaitingPayment;
                message = currentRecords.Count == 1
                    ? "Factura registrada. El hardware pasó a Facturado en espera de pago."
                    : $"Factura registrada en {currentRecords.Count} fila(s). El hardware pasó a Facturado en espera de pago.";
                break;

            case "register-client-payment":
                EnsureHardwareActionState(currentState, HardwareStateBilledAwaitingPayment, currentRecords[0].StateLabel);
                foreach (var current in currentRecords)
                {
                    var activeInvoiceNumber = FirstNonEmpty(request.InvoiceNumber, current.InvoiceNumber);
                    if (string.IsNullOrWhiteSpace(activeInvoiceNumber))
                        throw new InvalidOperationException("El hardware no tiene número de factura para validar el pago del cliente.");

                    var hasPayment = await HardwareInvoiceHasPaymentAsync(activeInvoiceNumber, user, ct);
                    if (hasPayment)
                    {
                        payloadsByRecordId[current.RecordId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            [HardwareInvoiceNumberLogicalName] = activeInvoiceNumber.Trim(),
                            [HardwareStateLogicalName] = HardwareStateClosed
                        };
                    }
                }

                message = payloadsByRecordId.Count > 0
                    ? currentRecords.Count == 1
                        ? "Se confirmó el pago del cliente en Facturación. El hardware quedó cerrado."
                        : $"Se confirmó el pago del cliente en Facturación para {payloadsByRecordId.Count} fila(s). El hardware quedó cerrado."
                    : "La factura aún no registra valor pago en Facturación. El hardware sigue en espera de pago.";

                break;

            default:
                throw new InvalidOperationException("La acción seleccionada no es válida para Hardware.");
        }

        foreach (var item in payloadsByRecordId)
        {
            if (item.Value.Count > 0)
                await PatchHardwareRecordAsync(metadata.EntitySetName, item.Key, item.Value, user, ct);
        }

        var updatedRecords = await ReloadHardwareRecordsUntilConsistentAsync(
            metadata,
            normalizedRecordIds,
            user,
            ct,
            expectedStateAfterSave,
            requiredPerRecordFileAfterSave);

        return new HardwareSaveResultDto
        {
            Message = message,
            Record = updatedRecords.FirstOrDefault() ?? new HardwareBoardRowDto(),
            Records = updatedRecords
        };
    }

    public async Task<HardwareBulkEditResultDto> SaveHardwareRecordsAsync(
        HardwareBulkEditRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var user = httpContext.User;
        await EnsureProvisioningHardwareSchemaAsync(user, ct);

        var metadata = await ResolveHardwareEntityMetadataAsync(user, ct);
        var attributes = await LoadHardwareAttributesAsync(user, ct);
        var recordIds = ResolveHardwareBulkEditRecordIds(request);
        if (request.StateChanged && request.ApplyStateChangeToOrderScope)
        {
            recordIds = await ExpandHardwareRecordIdsToOrderScopeAsync(
                metadata,
                recordIds,
                user,
                ct);
        }

        var payload = await BuildHardwareBulkEditPayloadAsync(request, attributes, user, ct);
        if (payload.Count == 0)
            throw new InvalidOperationException("Modifica al menos un campo antes de guardar.");

        foreach (var recordId in recordIds)
        {
            await PatchHardwareRecordAsync(metadata.EntitySetName, recordId, payload, user, ct);
        }

        var updatedRecords = new List<HardwareBoardRowDto>(recordIds.Count);
        foreach (var recordId in recordIds)
            updatedRecords.Add(await GetHardwareRecordByIdAsync(metadata, recordId, user, ct));

        return new HardwareBulkEditResultDto
        {
            Records = updatedRecords,
            Message = recordIds.Count == 1
                ? "Se actualizo 1 fila de Hardware."
                : $"Se actualizaron {recordIds.Count} filas de Hardware."
        };
    }

    private async Task PatchHardwareRecordAsync(
        string entitySetName,
        string recordId,
        IReadOnlyDictionary<string, object?> payload,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await CallRhDataverseResponseAsync(
            $"/api/data/v9.2/{entitySetName}({NormalizeGuid(recordId, nameof(recordId))})",
            "PATCH",
            user,
            ct,
            content,
            AddRhReturnRepresentationHeaders);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
    }

    private async Task<List<HardwareBoardRowDto>> ReloadHardwareRecordsUntilConsistentAsync(
        RhEntityMetadata metadata,
        IReadOnlyCollection<string> recordIds,
        ClaimsPrincipal user,
        CancellationToken ct,
        int? expectedState,
        string? requiredPerRecordFileField)
    {
        const int maxAttempts = 4;
        List<HardwareBoardRowDto> records = new(recordIds.Count);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            records = await LoadHardwareRecordsByIdsAsync(metadata, recordIds, user, ct);
            if (HardwareRecordsMatchExpectedSaveState(records, expectedState, requiredPerRecordFileField))
                return records;

            if (attempt < maxAttempts)
                await Task.Delay(TimeSpan.FromMilliseconds(450), ct);
        }

        throw new InvalidOperationException(BuildHardwareSaveConsistencyError(records, expectedState, requiredPerRecordFileField));
    }

    private async Task<List<HardwareBoardRowDto>> LoadHardwareRecordsByIdsAsync(
        RhEntityMetadata metadata,
        IReadOnlyCollection<string> recordIds,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var records = new List<HardwareBoardRowDto>(recordIds.Count);
        foreach (var recordId in recordIds)
            records.Add(await GetHardwareRecordByIdAsync(metadata, recordId, user, ct));

        return records;
    }

    private static bool HardwareRecordsMatchExpectedSaveState(
        IReadOnlyCollection<HardwareBoardRowDto> records,
        int? expectedState,
        string? requiredPerRecordFileField)
    {
        if (expectedState.HasValue
            && records.Any(record => NormalizeHardwareStateValue(record.StateValue) != expectedState.Value))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(requiredPerRecordFileField)
            || records.All(record => HasHardwareFile(record, requiredPerRecordFileField!));
    }

    private static string BuildHardwareSaveConsistencyError(
        IReadOnlyCollection<HardwareBoardRowDto> records,
        int? expectedState,
        string? requiredPerRecordFileField)
    {
        var details = new List<string>();
        if (expectedState.HasValue)
        {
            var pendingStateCount = records.Count(record => NormalizeHardwareStateValue(record.StateValue) != expectedState.Value);
            if (pendingStateCount > 0)
            {
                var expectedLabel = ResolveHardwareStateOption(expectedState.Value).Label;
                details.Add($"{pendingStateCount} fila(s) no quedaron en '{expectedLabel}'.");
            }
        }

        if (!string.IsNullOrWhiteSpace(requiredPerRecordFileField))
        {
            var missingFileCount = records.Count(record => !HasHardwareFile(record, requiredPerRecordFileField!));
            if (missingFileCount > 0)
                details.Add($"{missingFileCount} fila(s) no muestran el adjunto requerido.");
        }

        return details.Count == 0
            ? "Dataverse recibió la solicitud, pero no fue posible confirmar el estado final de la carga."
            : $"Dataverse recibió la solicitud, pero no confirmó el estado final de la carga. {string.Join(" ", details)} Actualiza la página e intenta nuevamente si el registro sigue pendiente.";
    }

    private async Task EnsureHardwareFilePresentOnEachRecordAsync(
        RhEntityMetadata metadata,
        IReadOnlyList<HardwareBoardRowDto> records,
        string fieldName,
        string fieldLabel,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var missingRecords = records
            .Where(record => !HasHardwareFile(record, fieldName))
            .ToList();
        if (missingRecords.Count == 0)
            return;

        var sourceRecord = records.FirstOrDefault(record => HasHardwareFile(record, fieldName));
        if (sourceRecord is null)
            throw new InvalidOperationException($"Debes cargar el archivo '{fieldLabel}' antes de avanzar esta etapa.");

        var sourceFile = await TryDownloadHardwareFileContentAsync(metadata, sourceRecord.RecordId, fieldName, user, ct)
            ?? throw new InvalidOperationException($"No fue posible leer el archivo '{fieldLabel}' ya cargado para completar las filas seleccionadas.");

        foreach (var record in missingRecords)
        {
            await UploadHardwareFileContentAsync(
                metadata,
                record.RecordId,
                fieldName,
                sourceFile.FileName,
                sourceFile.Content,
                user,
                ct);
        }
    }

    private async Task<List<string>> ExpandHardwareRecordIdsToOrderScopeAsync(
        RhEntityMetadata metadata,
        IReadOnlyCollection<string> baseRecordIds,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var expandedRecordIds = new HashSet<string>(baseRecordIds, StringComparer.OrdinalIgnoreCase);
        var orderNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var recordId in baseRecordIds)
        {
            var record = await GetHardwareRecordByIdAsync(metadata, recordId, user, ct);
            var orderNumber = record.PurchaseOrderNumber.Trim();
            if (!string.IsNullOrWhiteSpace(orderNumber))
                orderNumbers.Add(orderNumber);
        }

        foreach (var orderNumber in orderNumbers)
        {
            var filter = $"{HardwarePurchaseOrderNumberLogicalName} eq '{EscapeOdataLiteral(orderNumber)}'";
            var relativeUrl =
                $"/api/data/v9.2/{metadata.EntitySetName}?$select={metadata.PrimaryIdField}" +
                $"&$filter={Uri.EscapeDataString(filter)}&$top=250";
            var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct);
            foreach (var item in items)
            {
                var recordId = ReadString(item, metadata.PrimaryIdField).Trim();
                if (!string.IsNullOrWhiteSpace(recordId))
                    expandedRecordIds.Add(NormalizeGuid(recordId, metadata.PrimaryIdField));
            }
        }

        return expandedRecordIds.ToList();
    }

    private async Task UploadHardwareFileContentAsync(
        RhEntityMetadata metadata,
        string recordId,
        string fieldName,
        string fileName,
        byte[] content,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var normalizedFieldName = ResolveHardwareAllowedFileField(fieldName);
        var safeFileName = SanitizeRhFileName(fileName, HardwareAllowedFileFields[normalizedFieldName]);
        ValidateHardwareAttachmentUpload(safeFileName, content);

        using var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/octet-stream");

        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})/{normalizedFieldName}";
        using var response = await CallRhDataverseResponseAsync(
            relativeUrl,
            "PATCH",
            user,
            ct,
            fileContent,
            request =>
            {
                request.Headers.TryAddWithoutValidation("If-Match", "*");
                request.Headers.TryAddWithoutValidation("x-ms-file-name", safeFileName);
            });

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
    }

    private async Task<HardwareFileDownloadResult?> TryDownloadHardwareFileContentAsync(
        RhEntityMetadata metadata,
        string recordId,
        string fieldName,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var normalizedFieldName = ResolveHardwareAllowedFileField(fieldName);

        using var response = await CallRhDataverseResponseAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})/{normalizedFieldName}/$value",
            "GET",
            user,
            ct);
        if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var bodyBytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var bodyText = bodyBytes.Length == 0 ? "" : Encoding.UTF8.GetString(bodyBytes);
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {bodyText}");
        }

        return new HardwareFileDownloadResult
        {
            FileName = FirstNonEmpty(
                ReadHeaderValue(response, "x-ms-file-name"),
                ReadHeaderValue(response, "filename"),
                $"{normalizedFieldName}-{normalizedRecordId}.bin"),
            ContentType =
                response.Content.Headers.ContentType?.MediaType
                ?? ReadHeaderValue(response, "mimetype")
                ?? "application/octet-stream",
            Content = bodyBytes
        };
    }

    public async Task<HardwareFileUploadResultDto> UploadHardwareFileAsync(
        string recordId,
        string fieldName,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken ct = default,
        bool requireCurrentOwner = false,
        CurrentUserInfo? ownerOverride = null,
        int? requiredStateValue = null,
        IReadOnlyCollection<int>? allowedStateValues = null)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var user = httpContext.User;
        var metadata = await ResolveHardwareEntityMetadataAsync(user, ct);
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var normalizedFieldName = ResolveHardwareAllowedFileField(fieldName);
        var attributes = await LoadHardwareAttributesAsync(user, ct);
        EnsureHardwareFileAttributeExists(attributes, normalizedFieldName);
        HardwareBoardRowDto? currentRecord = null;
        if (requireCurrentOwner || requiredStateValue.HasValue || allowedStateValues?.Count > 0)
        {
            currentRecord = await GetHardwareRecordByIdAsync(metadata, normalizedRecordId, user, ct);
        }

        if (requireCurrentOwner)
        {
            EnsureHardwareRecordsOwnedByCurrentUser(new[] { currentRecord! }, ownerOverride ?? await GetCurrentUserAsync(ct));
        }

        if (requiredStateValue.HasValue)
        {
            EnsureHardwareActionState(
                NormalizeHardwareStateValue(currentRecord!.StateValue),
                requiredStateValue.Value,
                currentRecord.StateLabel);
        }
        else if (allowedStateValues?.Count > 0)
        {
            EnsureHardwareActionStateOneOf(
                NormalizeHardwareStateValue(currentRecord!.StateValue),
                allowedStateValues,
                currentRecord.StateLabel);
        }

        await UploadHardwareFileContentAsync(metadata, normalizedRecordId, normalizedFieldName, fileName, content, user, ct);

        var record = await GetHardwareRecordByIdAsync(metadata, normalizedRecordId, user, ct);
        return new HardwareFileUploadResultDto
        {
            Message = "Archivo cargado correctamente.",
            Record = record
        };
    }

    public async Task<HardwareFileDownloadResult?> DownloadHardwareFileAsync(
        string recordId,
        string fieldName,
        CancellationToken ct = default,
        bool requireCurrentOwner = false,
        CurrentUserInfo? ownerOverride = null,
        int? requiredStateValue = null)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var user = httpContext.User;
        var metadata = await ResolveHardwareEntityMetadataAsync(user, ct);
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var normalizedFieldName = ResolveHardwareAllowedFileField(fieldName);
        var attributes = await LoadHardwareAttributesAsync(user, ct);
        EnsureHardwareFileAttributeExists(attributes, normalizedFieldName);
        HardwareBoardRowDto? currentRecord = null;
        if (requireCurrentOwner || requiredStateValue.HasValue)
        {
            currentRecord = await GetHardwareRecordByIdAsync(metadata, normalizedRecordId, user, ct);
        }

        if (requireCurrentOwner)
        {
            EnsureHardwareRecordsOwnedByCurrentUser(new[] { currentRecord! }, ownerOverride ?? await GetCurrentUserAsync(ct));
        }

        if (requiredStateValue.HasValue)
        {
            EnsureHardwareActionState(
                NormalizeHardwareStateValue(currentRecord!.StateValue),
                requiredStateValue.Value,
                currentRecord.StateLabel);
        }

        return await TryDownloadHardwareFileContentAsync(metadata, normalizedRecordId, normalizedFieldName, user, ct);
    }

    public async Task<IReadOnlyList<HardwareInvoiceLookupItemDto>> SearchHardwareInvoicesAsync(
        string query,
        int top = 12,
        CancellationToken ct = default)
    {
        var normalizedQuery = NormalizeHardwareCell(query);
        if (normalizedQuery.Length < 2)
            return Array.Empty<HardwareInvoiceLookupItemDto>();

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var user = httpContext.User;
        var escapedQuery = EscapeOdataLiteral(normalizedQuery);
        var filter =
            $"contains({_dashboardBillingInvoiceNumberField},'{escapedQuery}') or startswith({_dashboardBillingInvoiceNumberField},'{escapedQuery}')";
        var selectFields = new[]
        {
            _dashboardBillingIdField,
            _dashboardBillingInvoiceNumberField,
            BuildDashboardLookupValuePropertyName(_dashboardBillingClientField),
            _dashboardBillingPaymentValueField
        };
        var relativeUrl =
            $"/api/data/v9.2/{_dashboardBillingTableSetName}?$select={string.Join(",", selectFields)}" +
            $"&$filter={Uri.EscapeDataString(filter)}&$orderby={_dashboardBillingInvoiceNumberField} asc&$top={Math.Clamp(top, 1, 30)}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item =>
            {
                var lookupProperty = DetectLookupValueProperty(
                    item,
                    new[]
                    {
                        BuildDashboardLookupValuePropertyName(_dashboardBillingClientField),
                        "_cr07a_clientenit_value",
                        "_cr07a_cliente_value"
                    },
                    "cliente");
                return new HardwareInvoiceLookupItemDto
                {
                    RecordId = ReadString(item, _dashboardBillingIdField).Trim(),
                    Number = ReadString(item, _dashboardBillingInvoiceNumberField).Trim(),
                    ClientName = FirstNonEmpty(
                        ReadLookupFormattedValue(item, lookupProperty),
                        ReadString(item, $"{_dashboardBillingClientField}{FormattedValueAnnotationSuffix}"),
                        "Sin cliente"),
                    PaymentValue = RoundCurrency(ReadDecimal(item, _dashboardBillingPaymentValueField) ?? 0m)
                };
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Number))
            .GroupBy(item => item.Number, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Number, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static HardwareCsvDocument ParseHardwareCsv(string fileName, byte[] content)
    {
        if (content.Length == 0)
            throw new InvalidOperationException("El archivo seleccionado esta vacio.");

        var extension = Path.GetExtension(fileName ?? "");
        if (!string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El archivo debe estar en formato .csv.");

        var text = DecodeHardwareCsv(content);
        var delimiter = DetectHardwareDelimiter(text);
        var rawRows = ParseHardwareRows(text, delimiter);
        if (rawRows.Count == 0)
            throw new InvalidOperationException("No se encontraron encabezados en el archivo.");

        var headerRow = rawRows[0];
        var dataRows = rawRows
            .Skip(1)
            .Select(static row => row.ToList())
            .Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(NormalizeHardwareCell(cell))))
            .ToList();

        var columnCount = Math.Max(
            headerRow.Count,
            dataRows.Count == 0 ? 0 : dataRows.Max(static row => row.Count));
        if (columnCount == 0)
            throw new InvalidOperationException("No se detectaron columnas validas en el CSV.");

        var columns = BuildHardwareColumnDefinitions(headerRow, dataRows, columnCount);
        if (columns.Count == 0)
            throw new InvalidOperationException("No se pudieron preparar columnas para Dataverse.");

        var rows = dataRows
            .Select((row, index) => new HardwareCsvRow
            {
                SourceRowNumber = index + 2,
                Values = PadHardwareRow(row, columnCount)
            })
            .ToList();

        return new HardwareCsvDocument
        {
            FileName = Path.GetFileName(fileName ?? "hardware.csv"),
            Delimiter = delimiter,
            Columns = columns,
            Rows = rows
        };
    }

    private static List<HardwareManagedColumnDefinition> BuildHardwareColumnDefinitions(
        IReadOnlyList<string> headerRow,
        IReadOnlyList<List<string>> dataRows,
        int columnCount)
    {
        var usedLogicalNames = new HashSet<string>(
            HardwareSystemColumns.Select(static item => item.LogicalName),
            StringComparer.OrdinalIgnoreCase);
        var columns = new List<HardwareManagedColumnDefinition>(columnCount);

        for (var index = 0; index < columnCount; index++)
        {
            var sourceHeader = index < headerRow.Count ? NormalizeHardwareCell(headerRow[index]) : "";
            var displayLabel = SanitizeHardwareHeader(sourceHeader, index + 1);
            var values = dataRows
                .Select(row => index < row.Count ? NormalizeHardwareCell(row[index]) : "")
                .ToList();
            var kind = InferHardwareColumnKind(displayLabel, values);
            var logicalName = CreateUniqueHardwareLogicalName(displayLabel, usedLogicalNames, index + 1);
            var schemaName = CreateHardwareSchemaName(logicalName);

            columns.Add(new HardwareManagedColumnDefinition
            {
                Index = index,
                SourceHeader = string.IsNullOrWhiteSpace(sourceHeader) ? $"Columna {index + 1}" : sourceHeader,
                DisplayLabel = displayLabel,
                LogicalName = logicalName,
                SchemaName = schemaName,
                Kind = kind,
                ExampleValue = values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "",
                MaxLength = DetermineHardwareMaxLength(kind, displayLabel, values)
            });
        }

        return columns;
    }

    private async Task<RhEntityMetadata?> TryResolveHardwareEntityMetadataAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var relativeUrl =
            $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(HardwareTableLogicalName)}')" +
            "?$select=LogicalName,EntitySetName,PrimaryIdAttribute,PrimaryNameAttribute";
        using var response = await CallRhDataverseResponseAsync(relativeUrl, "GET", user, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        using var doc = JsonDocument.Parse(body);
        var metadata = new RhEntityMetadata
        {
            LogicalName = FirstNonEmpty(ReadString(doc.RootElement, "LogicalName"), HardwareTableLogicalName),
            EntitySetName = ReadString(doc.RootElement, "EntitySetName").Trim(),
            PrimaryIdField = ReadString(doc.RootElement, "PrimaryIdAttribute").Trim(),
            PrimaryNameField = FirstNonEmpty(ReadString(doc.RootElement, "PrimaryNameAttribute"), HardwarePrimaryNameLogicalName)
        };

        if (string.IsNullOrWhiteSpace(metadata.EntitySetName) || string.IsNullOrWhiteSpace(metadata.PrimaryIdField))
            throw new InvalidOperationException("No fue posible resolver la metadata base de la tabla Hardware.");

        _rhEntityMetadataCache[HardwareTableLogicalName] = metadata;
        return metadata;
    }

    private async Task<RhEntityMetadata> ResolveHardwareEntityMetadataAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var metadata = await TryResolveHardwareEntityMetadataAsync(user, ct);
        if (metadata is null)
            throw new InvalidOperationException("La tabla Hardware aun no existe en Dataverse.");

        return metadata;
    }

    private async Task<List<HardwareAttributeMetadata>> LoadHardwareAttributesAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var relativeUrl =
            $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(HardwareTableLogicalName)}')" +
            "?$select=LogicalName&$expand=Attributes($select=LogicalName,SchemaName,AttributeType)";
        using var response = await CallRhDataverseResponseAsync(relativeUrl, "GET", user, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new List<HardwareAttributeMetadata>();

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        using var doc = JsonDocument.Parse(body);
        var result = new List<HardwareAttributeMetadata>();
        if (!doc.RootElement.TryGetProperty("Attributes", out var attributes)
            || attributes.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var attribute in attributes.EnumerateArray())
        {
            var logicalName = ReadString(attribute, "LogicalName").Trim();
            if (string.IsNullOrWhiteSpace(logicalName))
                continue;

            result.Add(new HardwareAttributeMetadata
            {
                LogicalName = logicalName,
                SchemaName = ReadString(attribute, "SchemaName").Trim(),
                AttributeType = ReadString(attribute, "AttributeType").Trim()
            });
        }

        return result;
    }

    private async Task ResolveHardwareColumnLogicalNamesAsync(
        IReadOnlyList<HardwareManagedColumnDefinition> columns,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        const int maxAttempts = 8;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var existingAttributes = await LoadHardwareAttributesAsync(user, ct);
            var unresolvedColumns = new List<HardwareManagedColumnDefinition>();

            foreach (var column in columns)
            {
                var matchedAttribute = FindMatchingHardwareAttribute(existingAttributes, column);
                if (matchedAttribute is null)
                {
                    unresolvedColumns.Add(column);
                    continue;
                }

                column.ResolvedLogicalName = matchedAttribute.LogicalName;
            }

            if (unresolvedColumns.Count == 0)
                return;

            if (attempt < maxAttempts)
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        var pendingColumns = columns
            .Where(column => string.IsNullOrWhiteSpace(column.ResolvedLogicalName))
            .Select(column => column.LogicalName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        throw new InvalidOperationException(
            $"Dataverse aun no expone estas columnas para importar: {string.Join(", ", pendingColumns)}. Intenta de nuevo en unos segundos.");
    }

    private static HardwareAttributeMetadata? FindMatchingHardwareAttribute(
        IEnumerable<HardwareAttributeMetadata> attributes,
        HardwareManagedColumnDefinition column)
    {
        var candidates = attributes.ToList();
        var exactLogical = candidates.FirstOrDefault(attribute =>
            string.Equals(attribute.LogicalName, column.LogicalName, StringComparison.OrdinalIgnoreCase));
        if (exactLogical is not null)
            return exactLogical;

        var exactSchema = candidates.FirstOrDefault(attribute =>
            !string.IsNullOrWhiteSpace(attribute.SchemaName)
            && string.Equals(attribute.SchemaName, column.SchemaName, StringComparison.OrdinalIgnoreCase));
        if (exactSchema is not null)
            return exactSchema;

        var normalizedTarget = NormalizeHardwareAttributeAlias(column.LogicalName);
        var normalizedSchema = NormalizeHardwareAttributeAlias(column.SchemaName);
        return candidates.FirstOrDefault(attribute =>
            NormalizeHardwareAttributeAlias(attribute.LogicalName) == normalizedTarget
            || NormalizeHardwareAttributeAlias(attribute.SchemaName) == normalizedTarget
            || NormalizeHardwareAttributeAlias(attribute.LogicalName) == normalizedSchema
            || NormalizeHardwareAttributeAlias(attribute.SchemaName) == normalizedSchema);
    }

    private async Task CreateHardwareEntityAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.EntityMetadata",
            ["Attributes"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["@odata.type"] = "Microsoft.Dynamics.CRM.StringAttributeMetadata",
                    ["AttributeType"] = "String",
                    ["AttributeTypeName"] = CreateHardwareValuePayload("StringType"),
                    ["Description"] = CreateHardwareLabelPayload("Nombre principal del registro de hardware."),
                    ["DisplayName"] = CreateHardwareLabelPayload("Nombre"),
                    ["IsPrimaryName"] = true,
                    ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
                    ["SchemaName"] = HardwarePrimaryNameSchemaName,
                    ["FormatName"] = CreateHardwareValuePayload("Text"),
                    ["MaxLength"] = 200
                }
            },
            ["Description"] = CreateHardwareLabelPayload("Tabla creada desde el modulo Hardware para importar ventas y compras de hardware."),
            ["DisplayCollectionName"] = CreateHardwareLabelPayload(HardwareTableDisplayName),
            ["DisplayName"] = CreateHardwareLabelPayload(HardwareTableDisplayName),
            ["HasActivities"] = false,
            ["HasNotes"] = false,
            ["IsActivity"] = false,
            ["OwnershipType"] = "UserOwned",
            ["SchemaName"] = "cr07a_Hardware"
        };

        await CallDataverseSendAsync("/api/data/v9.2/EntityDefinitions", "POST", payload, user, ct);
    }

    private async Task CreateHardwareAttributeAsync(
        HardwareManagedColumnDefinition column,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        await CallDataverseSendAsync(
            $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(HardwareTableLogicalName)}')/Attributes",
            "POST",
            BuildHardwareAttributePayload(column),
            user,
            ct);
    }

    private async Task PublishHardwareEntityAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var publishXml =
            $"<importexportxml><entities><entity>{HardwareTableLogicalName}</entity></entities></importexportxml>";
        await CallDataverseSendAsync(
            "/api/data/v9.2/PublishXml",
            "POST",
            new Dictionary<string, object?> { ["ParameterXml"] = publishXml },
            user,
            ct);
    }

    private static object BuildHardwareAttributePayload(HardwareManagedColumnDefinition column)
    {
        return column.Kind switch
        {
            HardwareAttributeKind.Date => BuildHardwareDateAttributePayload(column),
            HardwareAttributeKind.Money => BuildHardwareMoneyAttributePayload(column),
            HardwareAttributeKind.Integer => BuildHardwareIntegerAttributePayload(column),
            HardwareAttributeKind.Decimal => BuildHardwareDecimalAttributePayload(column),
            HardwareAttributeKind.Boolean => BuildHardwareBooleanAttributePayload(column),
            HardwareAttributeKind.Memo => BuildHardwareMemoAttributePayload(column),
            HardwareAttributeKind.File => BuildHardwareFileAttributePayload(column),
            _ => BuildHardwareStringAttributePayload(column)
        };
    }

    private static object BuildHardwareStringAttributePayload(HardwareManagedColumnDefinition column)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.StringAttributeMetadata",
            ["AttributeType"] = "String",
            ["AttributeTypeName"] = CreateHardwareValuePayload("StringType"),
            ["Description"] = CreateHardwareLabelPayload(BuildHardwareColumnDescription(column)),
            ["DisplayName"] = CreateHardwareLabelPayload(column.DisplayLabel),
            ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
            ["SchemaName"] = column.SchemaName,
            ["FormatName"] = CreateHardwareValuePayload("Text"),
            ["MaxLength"] = Math.Clamp(column.MaxLength, 50, 4000)
        };
    }

    private static object BuildHardwareMemoAttributePayload(HardwareManagedColumnDefinition column)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.MemoAttributeMetadata",
            ["AttributeType"] = "Memo",
            ["AttributeTypeName"] = CreateHardwareValuePayload("MemoType"),
            ["Format"] = "TextArea",
            ["ImeMode"] = "Disabled",
            ["MaxLength"] = Math.Clamp(column.MaxLength, 200, 4000),
            ["IsLocalizable"] = false,
            ["Description"] = CreateHardwareLabelPayload(BuildHardwareColumnDescription(column)),
            ["DisplayName"] = CreateHardwareLabelPayload(column.DisplayLabel),
            ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
            ["SchemaName"] = column.SchemaName
        };
    }

    private static object BuildHardwareMoneyAttributePayload(HardwareManagedColumnDefinition column)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.MoneyAttributeMetadata",
            ["AttributeType"] = "Money",
            ["AttributeTypeName"] = CreateHardwareValuePayload("MoneyType"),
            ["Description"] = CreateHardwareLabelPayload(BuildHardwareColumnDescription(column)),
            ["DisplayName"] = CreateHardwareLabelPayload(column.DisplayLabel),
            ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
            ["SchemaName"] = column.SchemaName,
            ["PrecisionSource"] = 2
        };
    }

    private static object BuildHardwareDateAttributePayload(HardwareManagedColumnDefinition column)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata",
            ["AttributeType"] = "DateTime",
            ["AttributeTypeName"] = CreateHardwareValuePayload("DateTimeType"),
            ["Description"] = CreateHardwareLabelPayload(BuildHardwareColumnDescription(column)),
            ["DisplayName"] = CreateHardwareLabelPayload(column.DisplayLabel),
            ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
            ["SchemaName"] = column.SchemaName,
            ["Format"] = "DateOnly"
        };
    }

    private static object BuildHardwareIntegerAttributePayload(HardwareManagedColumnDefinition column)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata",
            ["AttributeType"] = "Integer",
            ["AttributeTypeName"] = CreateHardwareValuePayload("IntegerType"),
            ["MaxValue"] = int.MaxValue,
            ["MinValue"] = int.MinValue,
            ["Format"] = "None",
            ["SourceTypeMask"] = 0,
            ["Description"] = CreateHardwareLabelPayload(BuildHardwareColumnDescription(column)),
            ["DisplayName"] = CreateHardwareLabelPayload(column.DisplayLabel),
            ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
            ["SchemaName"] = column.SchemaName
        };
    }

    private static object BuildHardwareDecimalAttributePayload(HardwareManagedColumnDefinition column)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata",
            ["AttributeType"] = "Decimal",
            ["AttributeTypeName"] = CreateHardwareValuePayload("DecimalType"),
            ["MaxValue"] = 1000000000m,
            ["MinValue"] = -1000000000m,
            ["Precision"] = column.Precision,
            ["Description"] = CreateHardwareLabelPayload(BuildHardwareColumnDescription(column)),
            ["DisplayName"] = CreateHardwareLabelPayload(column.DisplayLabel),
            ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
            ["SchemaName"] = column.SchemaName
        };
    }

    private static object BuildHardwareBooleanAttributePayload(HardwareManagedColumnDefinition column)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata",
            ["AttributeType"] = "Boolean",
            ["AttributeTypeName"] = CreateHardwareValuePayload("BooleanType"),
            ["DefaultValue"] = false,
            ["OptionSet"] = new Dictionary<string, object?>
            {
                ["TrueOption"] = new Dictionary<string, object?>
                {
                    ["Value"] = 1,
                    ["Label"] = CreateHardwareLabelPayload("Si")
                },
                ["FalseOption"] = new Dictionary<string, object?>
                {
                    ["Value"] = 0,
                    ["Label"] = CreateHardwareLabelPayload("No")
                },
                ["OptionSetType"] = "Boolean"
            },
            ["Description"] = CreateHardwareLabelPayload(BuildHardwareColumnDescription(column)),
            ["DisplayName"] = CreateHardwareLabelPayload(column.DisplayLabel),
            ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
            ["SchemaName"] = column.SchemaName
        };
    }

    private static object BuildHardwareFileAttributePayload(HardwareManagedColumnDefinition column)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.FileAttributeMetadata",
            ["AttributeTypeName"] = CreateHardwareValuePayload("FileType"),
            ["Description"] = CreateHardwareLabelPayload(BuildHardwareColumnDescription(column)),
            ["DisplayName"] = CreateHardwareLabelPayload(column.DisplayLabel),
            ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
            ["SchemaName"] = column.SchemaName,
            ["MaxSizeInKB"] = column.MaxLength > 0 ? column.MaxLength : 131072
        };
    }

    private static Dictionary<string, object?> CreateHardwareLabelPayload(string text)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.Label",
            ["LocalizedLabels"] = new object[]
            {
                CreateHardwareLocalizedLabel(text, 3082),
                CreateHardwareLocalizedLabel(text, 1033)
            }
        };
    }

    private static Dictionary<string, object?> CreateHardwareLocalizedLabel(string text, int languageCode)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.LocalizedLabel",
            ["Label"] = text,
            ["LanguageCode"] = languageCode
        };
    }

    private static Dictionary<string, object?> CreateRequiredLevelNonePayload()
    {
        return new Dictionary<string, object?>
        {
            ["Value"] = "None",
            ["CanBeChanged"] = true,
            ["ManagedPropertyLogicalName"] = "canmodifyrequirementlevelsettings"
        };
    }

    private static Dictionary<string, object?> CreateHardwareValuePayload(string value)
    {
        return new Dictionary<string, object?>
        {
            ["Value"] = value
        };
    }

    private async Task<bool> HardwareRecordExistsAsync(
        string entitySetName,
        string importKey,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var filter = $"{HardwareImportKeyLogicalName} eq '{EscapeOdataLiteral(importKey)}'";
        var relativeUrl =
            $"/api/data/v9.2/{entitySetName}?$select={HardwareImportKeyLogicalName}&$filter={Uri.EscapeDataString(filter)}&$top=1";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct);
        return items.Count > 0;
    }

    private static Dictionary<string, object?> BuildHardwareRecordPayload(
        HardwareCsvDocument document,
        HardwareCsvRow row,
        string primaryNameField,
        string importKey)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [string.IsNullOrWhiteSpace(primaryNameField) ? HardwarePrimaryNameLogicalName : primaryNameField] =
                BuildHardwareRecordName(document, row),
            [HardwareImportKeyLogicalName] = importKey,
            [HardwareSourceFileNameLogicalName] = document.FileName,
            [HardwareSourceRowNumberLogicalName] = row.SourceRowNumber
        };

        for (var index = 0; index < document.Columns.Count; index++)
        {
            var column = document.Columns[index];
            var rawValue = index < row.Values.Count ? row.Values[index] : "";
            var convertedValue = ConvertHardwareColumnValue(column, rawValue);
            if (convertedValue is null)
                continue;

            payload[FirstNonEmpty(column.ResolvedLogicalName, column.LogicalName)] = convertedValue;
        }

        return payload;
    }

    private static object? ConvertHardwareColumnValue(HardwareManagedColumnDefinition column, string? rawValue)
    {
        var normalized = NormalizeHardwareCell(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return column.Kind switch
        {
            HardwareAttributeKind.Date => ParseHardwareDate(normalized, column.DisplayLabel).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            HardwareAttributeKind.Money => RoundCurrency(ParseHardwareDecimal(normalized, column.DisplayLabel)),
            HardwareAttributeKind.Integer => ParseHardwareInteger(normalized, column.DisplayLabel),
            HardwareAttributeKind.Decimal => Math.Round(ParseHardwareDecimal(normalized, column.DisplayLabel), column.Precision, MidpointRounding.AwayFromZero),
            HardwareAttributeKind.Boolean => ParseHardwareBoolean(normalized, column.DisplayLabel),
            _ => normalized
        };
    }

    private static string BuildHardwareRecordName(HardwareCsvDocument document, HardwareCsvRow row)
    {
        var description = GetHardwareRowValue(document, row, "descripcion", "producto");
        var dateValue = GetHardwareRowValue(document, row, "fecha");
        if (TryParseHardwareDate(dateValue, out var parsedDate))
            dateValue = parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var identifier = GetHardwareRowValue(document, row, "orden", "factura", "remision");
        var parts = new[]
        {
            FirstNonEmpty(description, "Hardware"),
            string.IsNullOrWhiteSpace(dateValue) ? "" : dateValue,
            string.IsNullOrWhiteSpace(identifier) ? $"Fila {row.SourceRowNumber}" : identifier
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToList();

        var value = string.Join(" - ", parts);
        return value.Length <= 200 ? value : value[..200];
    }

    private static string GetHardwareRowValue(HardwareCsvDocument document, HardwareCsvRow row, params string[] tokens)
    {
        foreach (var token in tokens.Where(token => !string.IsNullOrWhiteSpace(token)))
        {
            for (var index = 0; index < document.Columns.Count; index++)
            {
                var column = document.Columns[index];
                if (!column.DisplayLabel.Contains(token, StringComparison.OrdinalIgnoreCase)
                    && !column.SourceHeader.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = index < row.Values.Count ? NormalizeHardwareCell(row.Values[index]) : "";
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        for (var index = 0; index < document.Columns.Count; index++)
        {
            if (document.Columns[index].Kind is HardwareAttributeKind.String or HardwareAttributeKind.Memo)
            {
                var value = index < row.Values.Count ? NormalizeHardwareCell(row.Values[index]) : "";
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return "";
    }

    private static string ComputeHardwareImportKey(HardwareCsvRow row)
    {
        var rawKey = string.Join(
            "|",
            new[] { row.SourceRowNumber.ToString(CultureInfo.InvariantCulture) }
                .Concat(row.Values.Select(value => NormalizeHardwareCell(value).ToLowerInvariant())));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task EnsureProvisioningHardwareSchemaAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        await EnsureHardwareWorkflowSchemaAsync(user, ct);

        var existingAttributes = await LoadHardwareAttributesAsync(user, ct);
        var clientAttribute = existingAttributes.FirstOrDefault(attribute =>
            string.Equals(attribute.LogicalName, HardwareClientLookupLogicalName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(attribute.SchemaName, HardwareClientLookupLogicalName, StringComparison.OrdinalIgnoreCase));

        if (clientAttribute is null)
        {
            throw new InvalidOperationException(
                $"La tabla Hardware no tiene el campo lookup {HardwareClientLookupLogicalName}. Crealo en Dataverse antes de gestionar registros de Hardware.");
        }
    }

    private async Task EnsureHardwareWorkflowSchemaAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var tableCreated = false;
        if (await TryResolveHardwareEntityMetadataAsync(user, ct) is null)
        {
            await CreateHardwareEntityAsync(user, ct);
            tableCreated = true;
        }

        var existingAttributes = await LoadHardwareAttributesAsync(user, ct);
        var createdColumns = new List<string>();
        foreach (var column in HardwareProvisioningColumns)
        {
            var matchedAttribute = FindMatchingHardwareAttribute(existingAttributes, column);
            if (matchedAttribute is not null)
            {
                column.ResolvedLogicalName = matchedAttribute.LogicalName;
                continue;
            }

            await CreateHardwareAttributeAsync(column, user, ct);
            createdColumns.Add(column.LogicalName);
        }

        if (tableCreated || createdColumns.Count > 0)
            await PublishHardwareEntityAsync(user, ct);

        await ResolveHardwareColumnLogicalNamesAsync(HardwareProvisioningColumns.ToList(), user, ct);
    }

    private static Dictionary<string, object?> BuildHardwareOrderDraftPayload(
        RhEntityMetadata metadata,
        HardwareOrderLineCreateRequest line,
        int lineIndex,
        string purchaseOrderNumber,
        string odcDate,
        string clientId,
        string clientNavigationProperty,
        string ownerId,
        string ownerNavigationProperty)
    {
        var name = RequireHardwareText(line.Name, $"cr07a_name de la fila {lineIndex + 1}");
        if (name.Length > 200)
            name = name[..200];

        var quantity = ParseHardwareOrderQuantity(line.Quantity, lineIndex);
        var supplierUnitCost = ParseHardwareStageCurrency(line.SupplierUnitCost, $"cr07a_costountproveedor de la fila {lineIndex + 1}");
        var saleUnit = ParseHardwareStageCurrency(line.SaleUnit, $"cr07a_ventaunidad de la fila {lineIndex + 1}");
        var supplierTotal = RoundCurrency(quantity * supplierUnitCost);
        var priceSale = RoundCurrency(quantity * saleUnit);
        var marginValue = CalculateHardwareMarginValue(priceSale, supplierTotal);
        var utility = CalculateHardwareUtility(priceSale, marginValue);

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [string.IsNullOrWhiteSpace(metadata.PrimaryNameField) ? HardwarePrimaryNameLogicalName : metadata.PrimaryNameField] = name,
            [HardwareQuantityLogicalName] = quantity,
            [HardwareSaleUnitLogicalName] = saleUnit,
            [HardwareTotalSaleLogicalName] = priceSale,
            [HardwareSupplierUnitCostLogicalName] = supplierUnitCost,
            [HardwareSupplierTotalLogicalName] = supplierTotal,
            [HardwareFreightValueLogicalName] = 0m,
            [HardwareUtilityLogicalName] = utility,
            [HardwareMarginValueLogicalName] = marginValue,
            [HardwarePurchaseOrderNumberLogicalName] = purchaseOrderNumber,
            [HardwareSupplierLogicalName] = RequireHardwareText(line.Provider, $"cr07a_proveedor de la fila {lineIndex + 1}"),
            [HardwareSupplierDocumentGroupKeyLogicalName] = ResolveHardwareSupplierDocumentGroupKey(
                line.SupplierDocumentGroupKey,
                line.SupplierDocumentGroupLabel,
                purchaseOrderNumber,
                line.Provider,
                lineIndex),
            [HardwareSupplierDocumentGroupLabelLogicalName] = ResolveHardwareSupplierDocumentGroupLabel(
                line.SupplierDocumentGroupLabel,
                line.Provider,
                lineIndex),
            [HardwareSupplierDocumentTypeLogicalName] = HardwareSupplierDocumentTypeProforma,
            [HardwareOdcDateLogicalName] = odcDate,
            [HardwareStateLogicalName] = HardwareStateWaitingDocumentation,
            [$"{clientNavigationProperty}@odata.bind"] = $"/{ClientsEntitySetName}({NormalizeGuid(clientId, nameof(clientId))})",
            [$"{ownerNavigationProperty}@odata.bind"] = $"/systemusers({NormalizeGuid(ownerId, nameof(ownerId))})"
        };
    }

    private List<string> BuildHardwareBoardSelectFields(
        RhEntityMetadata metadata,
        IReadOnlyList<HardwareAttributeMetadata> attributes)
    {
        var selectFields = new List<string>
        {
            metadata.PrimaryIdField,
            string.IsNullOrWhiteSpace(metadata.PrimaryNameField) ? HardwarePrimaryNameLogicalName : metadata.PrimaryNameField,
            HardwareCreatedOnLogicalName,
            HardwareModifiedOnLogicalName
        };

        foreach (var field in new[]
                 {
                     HardwareQuantityLogicalName,
                     HardwareSaleUnitLogicalName,
                     HardwareTotalSaleLogicalName,
                     HardwareUtilityLogicalName,
                     HardwareMarginValueLogicalName,
                     HardwareStateLogicalName,
                     HardwareSupplierUnitCostLogicalName,
                     HardwareSupplierTotalLogicalName,
                     HardwareFreightValueLogicalName,
                     HardwarePurchaseOrderNumberLogicalName,
                     HardwareSupplierLogicalName,
                     HardwareSupplierDocumentGroupKeyLogicalName,
                     HardwareSupplierDocumentGroupLabelLogicalName,
                     HardwareOdcDateLogicalName,
                     HardwareSupplierPaymentDateLogicalName,
                     HardwareDeliveryRecordDateLogicalName,
                     HardwareInvoiceNumberLogicalName,
                     HardwareSupplierDocumentTypeLogicalName
                 })
        {
            if (HasHardwareAttribute(attributes, field))
                selectFields.Add(field);
        }

        if (HasHardwareAttribute(attributes, HardwareClientLookupLogicalName))
            selectFields.Add(HardwareClientLookupFieldCandidates[0]);

        selectFields.Add(BuildDashboardLookupValuePropertyName(HardwareOwnerLogicalName));

        AddHardwareFileSelectField(selectFields, attributes, HardwareOrderPurchaseFileLogicalName, HardwareOrderPurchaseFileNameLogicalName);
        AddHardwareFileSelectField(selectFields, attributes, HardwareProformaFileLogicalName, HardwareProformaFileNameLogicalName);
        AddHardwareFileSelectField(selectFields, attributes, HardwareSupplierPurchaseOrderFileLogicalName, HardwareSupplierPurchaseOrderFileNameLogicalName);
        AddHardwareFileSelectField(selectFields, attributes, HardwareSupplierPaymentFileLogicalName, HardwareSupplierPaymentFileNameLogicalName);
        AddHardwareFileSelectField(selectFields, attributes, HardwareDeliveryRecordFileLogicalName, HardwareDeliveryRecordFileNameLogicalName);

        return selectFields;
    }

    private static void AddHardwareFileSelectField(
        ICollection<string> selectFields,
        IReadOnlyList<HardwareAttributeMetadata> attributes,
        string fieldLogicalName,
        string fileNameLogicalName)
    {
        if (!HasHardwareAttribute(attributes, fieldLogicalName))
            return;

        if (!selectFields.Contains(fieldLogicalName, StringComparer.OrdinalIgnoreCase))
            selectFields.Add(fieldLogicalName);

        if (!selectFields.Contains(fileNameLogicalName, StringComparer.OrdinalIgnoreCase))
            selectFields.Add(fileNameLogicalName);
    }

    private HardwareBoardRowDto? BuildHardwareBoardRowDto(
        RhEntityMetadata metadata,
        IReadOnlyList<HardwareAttributeMetadata> attributes,
        JsonElement item)
    {
        var recordId = ReadString(item, metadata.PrimaryIdField).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var primaryNameField = string.IsNullOrWhiteSpace(metadata.PrimaryNameField)
            ? HardwarePrimaryNameLogicalName
            : metadata.PrimaryNameField;
        var clientLookupProperty = HasHardwareAttribute(attributes, HardwareClientLookupLogicalName)
            ? DetectLookupValueProperty(item, HardwareClientLookupFieldCandidates, "cliente")
            : null;
        var ownerLookupProperty = BuildDashboardLookupValuePropertyName(HardwareOwnerLogicalName);
        var stateValue = NormalizeHardwareStateValue(ReadIntFlexible(item, HardwareStateLogicalName));
        var state = ResolveHardwareStateOption(stateValue);
        var createdOn = ReadHardwareCreatedOnDate(item);
        var modifiedOn = ReadDateOnly(item, HardwareModifiedOnLogicalName);

        return new HardwareBoardRowDto
        {
            RecordId = recordId,
            Name = FirstNonEmpty(ReadString(item, primaryNameField).Trim(), "Sin nombre"),
            OwnerId = ReadString(item, ownerLookupProperty).Trim(),
            OwnerName = FirstNonEmpty(ReadLookupFormattedValue(item, ownerLookupProperty), "Sin owner"),
            ClientId = ReadString(item, clientLookupProperty).Trim(),
            ClientName = FirstNonEmpty(
                ReadLookupFormattedValue(item, clientLookupProperty),
                ReadString(item, HardwareClientLookupLogicalName).Trim(),
                "Sin cliente"),
            Quantity = ReadIntFlexible(item, HardwareQuantityLogicalName),
            SaleUnit = RoundCurrency(ReadDecimal(item, HardwareSaleUnitLogicalName) ?? 0m),
            TotalSale = RoundCurrency(ReadDecimal(item, HardwareTotalSaleLogicalName) ?? 0m),
            StateValue = state.Value,
            StateLabel = state.Label,
            StateTone = state.Tone,
            ActionKey = state.ActionKey,
            ActionLabel = state.ActionLabel,
            HasAction = state.HasAction,
            Provider = ReadString(item, HardwareSupplierLogicalName).Trim(),
            SupplierDocumentGroupKey = ResolveHardwareSupplierDocumentGroupKey(
                ReadString(item, HardwareSupplierDocumentGroupKeyLogicalName),
                ReadString(item, HardwareSupplierDocumentGroupLabelLogicalName),
                ReadString(item, HardwarePurchaseOrderNumberLogicalName),
                ReadString(item, HardwareSupplierLogicalName),
                0),
            SupplierDocumentGroupLabel = ResolveHardwareSupplierDocumentGroupLabel(
                ReadString(item, HardwareSupplierDocumentGroupLabelLogicalName),
                ReadString(item, HardwareSupplierLogicalName),
                0),
            InvoiceNumber = ReadString(item, HardwareInvoiceNumberLogicalName).Trim(),
            PurchaseOrderNumber = ReadString(item, HardwarePurchaseOrderNumberLogicalName).Trim(),
            SupplierUnitCost = RoundCurrency(ReadDecimal(item, HardwareSupplierUnitCostLogicalName) ?? 0m),
            SupplierTotal = RoundCurrency(ReadDecimal(item, HardwareSupplierTotalLogicalName) ?? 0m),
            FreightValue = RoundCurrency(ReadDecimal(item, HardwareFreightValueLogicalName) ?? 0m),
            Utility = Math.Round(ReadDecimal(item, HardwareUtilityLogicalName) ?? 0m, 4, MidpointRounding.AwayFromZero),
            MarginValue = Math.Round(ReadDecimal(item, HardwareMarginValueLogicalName) ?? 0m, 2, MidpointRounding.AwayFromZero),
            InvoiceHasClientPayment = state.Value == HardwareStateClosed,
            CreatedOnValue = FormatHardwareDateValue(createdOn),
            CreatedOnDisplay = FormatHardwareDateDisplay(createdOn),
            OdcDateValue = FormatHardwareDateValue(ReadDateOnly(item, HardwareOdcDateLogicalName)),
            OdcDateDisplay = FormatHardwareDateDisplay(ReadDateOnly(item, HardwareOdcDateLogicalName)),
            SupplierPaymentDateValue = FormatHardwareDateValue(ReadDateOnly(item, HardwareSupplierPaymentDateLogicalName)),
            SupplierPaymentDateDisplay = FormatHardwareDateDisplay(ReadDateOnly(item, HardwareSupplierPaymentDateLogicalName)),
            DeliveryRecordDateValue = FormatHardwareDateValue(ReadDateOnly(item, HardwareDeliveryRecordDateLogicalName)),
            DeliveryRecordDateDisplay = FormatHardwareDateDisplay(ReadDateOnly(item, HardwareDeliveryRecordDateLogicalName)),
            HasOrderPurchase = HasHardwareFile(item, HardwareOrderPurchaseFileLogicalName, HardwareOrderPurchaseFileNameLogicalName),
            OrderPurchaseFileName = ResolveHardwareFileName(item, HardwareOrderPurchaseFileLogicalName, HardwareOrderPurchaseFileNameLogicalName),
            HasProforma = HasHardwareFile(item, HardwareProformaFileLogicalName, HardwareProformaFileNameLogicalName),
            ProformaFileName = ResolveHardwareFileName(item, HardwareProformaFileLogicalName, HardwareProformaFileNameLogicalName),
            HasSupplierPurchaseOrder = HasHardwareFile(item, HardwareSupplierPurchaseOrderFileLogicalName, HardwareSupplierPurchaseOrderFileNameLogicalName),
            SupplierPurchaseOrderFileName = ResolveHardwareFileName(item, HardwareSupplierPurchaseOrderFileLogicalName, HardwareSupplierPurchaseOrderFileNameLogicalName),
            SupplierDocumentType = NormalizeHardwareSupplierDocumentTypeForDisplay(ReadString(item, HardwareSupplierDocumentTypeLogicalName)),
            HasSupplierPaymentProof = HasHardwareFile(item, HardwareSupplierPaymentFileLogicalName, HardwareSupplierPaymentFileNameLogicalName),
            SupplierPaymentProofFileName = ResolveHardwareFileName(item, HardwareSupplierPaymentFileLogicalName, HardwareSupplierPaymentFileNameLogicalName),
            HasDeliveryRecord = HasHardwareFile(item, HardwareDeliveryRecordFileLogicalName, HardwareDeliveryRecordFileNameLogicalName),
            DeliveryRecordFileName = ResolveHardwareFileName(item, HardwareDeliveryRecordFileLogicalName, HardwareDeliveryRecordFileNameLogicalName),
            ModifiedOnValue = FormatHardwareDateValue(modifiedOn),
            ModifiedOnDisplay = modifiedOn?.ToString("dd/MM/yyyy", HardwareCulture) ?? ""
        };
    }

    private static IReadOnlyList<HardwareBoardRowDto> BuildHardwareSupplierPaymentHistoryRows(IEnumerable<HardwareBoardRowDto> rows) =>
        rows
            .Where(static row => row.HasSupplierPaymentProof)
            .OrderBy(static row => ResolveHardwareSupplierPaymentHistoryDate(row) ?? DateOnly.MaxValue)
            .ThenBy(static row => row.ModifiedOnValue)
            .ThenBy(static row => row.PurchaseOrderNumber)
            .ThenBy(static row => row.SupplierDocumentGroupLabel)
            .ToList();

    private static DateOnly? ResolveHardwareSupplierPaymentHistoryDate(HardwareBoardRowDto row) =>
        TryParseHardwareIsoDate(row.SupplierPaymentDateValue)
        ?? TryParseHardwareIsoDate(row.ModifiedOnValue);

    private static DateOnly? TryParseHardwareIsoDate(string? value)
    {
        var normalized = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return DateOnly.TryParseExact(
            normalized,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    private static DateOnly? ReadHardwareCreatedOnDate(JsonElement item)
    {
        if (!item.TryGetProperty(HardwareCreatedOnLogicalName, out var property) || property.ValueKind != JsonValueKind.String)
            return null;

        var raw = property.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTimeOffset))
            return DateOnly.FromDateTime(dateTimeOffset.ToOffset(TimeSpan.FromHours(-5)).DateTime);

        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dateOnly))
            return dateOnly;

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime))
            return DateOnly.FromDateTime(dateTime);

        return null;
    }

    private async Task<HardwareBoardRowDto> GetHardwareRecordByIdAsync(
        RhEntityMetadata metadata,
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var attributes = await LoadHardwareAttributesAsync(user, ct);
        var selectFields = BuildHardwareBoardSelectFields(metadata, attributes);
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}({NormalizeGuid(recordId, nameof(recordId))})?$select={string.Join(",", selectFields.Distinct(StringComparer.OrdinalIgnoreCase))}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        var row = BuildHardwareBoardRowDto(metadata, attributes, doc.RootElement)
            ?? throw new InvalidOperationException("No fue posible reconstruir el registro de Hardware.");

        return row;
    }

    private async Task AutoClosePaidHardwareRecordsAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var metadata = await TryResolveHardwareEntityMetadataAsync(user, ct);
        if (metadata is null)
            return;

        var attributes = await LoadHardwareAttributesAsync(user, ct);
        if (!HasHardwareAttribute(attributes, HardwareStateLogicalName)
            || !HasHardwareAttribute(attributes, HardwareInvoiceNumberLogicalName))
            return;

        var filter =
            $"{HardwareStateLogicalName} eq {HardwareStateBilledAwaitingPayment}" +
            $" and {HardwareInvoiceNumberLogicalName} ne null";
        var selectFields = string.Join(",",
            new[]
            {
                metadata.PrimaryIdField,
                BuildDashboardLookupValuePropertyName(HardwareOwnerLogicalName),
                HardwareStateLogicalName,
                HardwareInvoiceNumberLogicalName
            }
            .Distinct(StringComparer.OrdinalIgnoreCase));
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={selectFields}&$filter={Uri.EscapeDataString(filter)}&$top=250";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct);

        foreach (var item in items)
        {
            var invoiceNumber = ReadString(item, HardwareInvoiceNumberLogicalName).Trim();
            if (string.IsNullOrWhiteSpace(invoiceNumber))
                continue;

            if (!await HardwareInvoiceHasPaymentAsync(invoiceNumber, user, ct))
                continue;

            var recordId = ReadString(item, metadata.PrimaryIdField).Trim();
            if (string.IsNullOrWhiteSpace(recordId))
                continue;

            await CallDataverseSendAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}({NormalizeGuid(recordId, nameof(recordId))})",
                "PATCH",
                new Dictionary<string, object?> { [HardwareStateLogicalName] = HardwareStateClosed },
                user,
                ct);
        }
    }

    private async Task<string> ResolveHardwareInvoiceNumberAsync(
        string? invoiceNumber,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalized = NormalizeHardwareCell(invoiceNumber);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Debes seleccionar un número de factura válido.");

        var matches = await SearchHardwareInvoicesAsync(normalized, 20, ct);
        var exactMatch = matches.FirstOrDefault(item =>
            string.Equals(NormalizeHardwareLookupText(item.Number), NormalizeHardwareLookupText(normalized), StringComparison.Ordinal));

        if (exactMatch is null)
            throw new InvalidOperationException("Selecciona una coincidencia exacta de la tabla Facturación para el número de factura.");

        return exactMatch.Number;
    }

    private async Task<bool> HardwareInvoiceHasPaymentAsync(
        string invoiceNumber,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedInvoice = NormalizeHardwareCell(invoiceNumber);
        if (string.IsNullOrWhiteSpace(normalizedInvoice))
            return false;

        var filter =
            $"{_dashboardBillingInvoiceNumberField} eq '{EscapeOdataLiteral(normalizedInvoice)}' and {_dashboardBillingPaymentValueField} gt 0";
        var relativeUrl =
            $"/api/data/v9.2/{_dashboardBillingTableSetName}?$select={_dashboardBillingIdField}&$filter={Uri.EscapeDataString(filter)}&$top=1";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct);
        return items.Count > 0;
    }

    private static IReadOnlyList<HardwareStateSummaryDto> BuildHardwareStateSummaries(IReadOnlyList<HardwareBoardRowDto> rows)
    {
        return HardwareStates
            .Select(state => new HardwareStateSummaryDto
            {
                Value = state.Value,
                Label = state.Label,
                Tone = state.Tone,
                Count = rows.Count(row => NormalizeHardwareStateValue(row.StateValue) == state.Value)
            })
            .ToList();
    }

    private static List<string> BuildHardwareBoardFilters(
        int? stateValue,
        DateOnly? startDate,
        DateOnly? endDate,
        IReadOnlyList<HardwareAttributeMetadata> attributes,
        bool filterByCreatedOn)
    {
        var filters = new List<string>();
        if (stateValue.HasValue && stateValue.Value > 0)
            filters.Add($"{HardwareStateLogicalName} eq {NormalizeHardwareStateValue(stateValue.Value)}");

        if (filterByCreatedOn)
        {
            AddHardwareCreatedOnFilters(filters, startDate, endDate);
        }
        else if (HasHardwareAttribute(attributes, HardwareOdcDateLogicalName))
        {
            if (startDate.HasValue)
                filters.Add($"{HardwareOdcDateLogicalName} ge {startDate.Value:yyyy-MM-dd}");

            if (endDate.HasValue)
                filters.Add($"{HardwareOdcDateLogicalName} le {endDate.Value:yyyy-MM-dd}");
        }

        return filters;
    }

    private static void AddHardwareCreatedOnFilters(ICollection<string> filters, DateOnly? startDate, DateOnly? endDate)
    {
        if (startDate.HasValue)
            filters.Add($"{HardwareCreatedOnLogicalName} ge {FormatHardwareCreatedOnBoundary(startDate.Value)}");

        if (endDate.HasValue)
            filters.Add($"{HardwareCreatedOnLogicalName} lt {FormatHardwareCreatedOnBoundary(endDate.Value.AddDays(1))}");
    }

    private static string FormatHardwareCreatedOnBoundary(DateOnly date)
    {
        var localDateTime = date.ToDateTime(TimeOnly.MinValue);
        var utcDateTime = DateTime.SpecifyKind(localDateTime.AddHours(5), DateTimeKind.Utc);
        return utcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static string BuildHardwareDateFilterLabel(DateOnly? startDate, DateOnly? endDate, bool filterByCreatedOn = false)
    {
        var prefix = filterByCreatedOn ? "Creación" : "ODC";

        if (startDate.HasValue && endDate.HasValue)
            return $"{prefix} {startDate.Value:dd/MM/yyyy} - {endDate.Value:dd/MM/yyyy}";

        if (startDate.HasValue)
            return $"{prefix} desde {startDate.Value:dd/MM/yyyy}";

        if (endDate.HasValue)
            return $"{prefix} hasta {endDate.Value:dd/MM/yyyy}";

        return filterByCreatedOn ? "Todas las fechas de creacion" : "Todas las fechas ODC";
    }

    private static IReadOnlyList<string> BuildHardwareWarnings(IReadOnlyList<HardwareAttributeMetadata> attributes)
    {
        var warnings = new List<string>();
        foreach (var pair in HardwareAllowedFileFields)
        {
            if (!HasHardwareAttribute(attributes, pair.Key))
                warnings.Add($"El campo de archivo {pair.Key} aún no existe en Dataverse.");
        }

        if (!HasHardwareAttribute(attributes, HardwareClientLookupLogicalName))
            warnings.Add($"El lookup {HardwareClientLookupLogicalName} debe existir para asociar el cliente.");

        return warnings;
    }

    private static bool IsHardwareRecordOwnedByCurrentUser(HardwareBoardRowDto row, CurrentUserInfo? currentUser)
    {
        if (currentUser is null)
            return false;

        if (!string.IsNullOrWhiteSpace(currentUser.SystemUserId)
            && !string.IsNullOrWhiteSpace(row.OwnerId)
            && string.Equals(NormalizeOptionalGuid(row.OwnerId), NormalizeOptionalGuid(currentUser.SystemUserId), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(currentUser.DisplayName)
            && !string.IsNullOrWhiteSpace(row.OwnerName)
            && string.Equals(row.OwnerName.Trim(), currentUser.DisplayName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(currentUser.Email)
            && !string.IsNullOrWhiteSpace(row.OwnerName)
            && string.Equals(row.OwnerName.Trim(), currentUser.Email.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureHardwareRecordsOwnedByCurrentUser(
        IEnumerable<HardwareBoardRowDto> records,
        CurrentUserInfo? currentUser)
    {
        if (currentUser is null)
            throw new InvalidOperationException("No fue posible resolver el owner autenticado.");

        var unauthorized = records
            .Where(record => !IsHardwareRecordOwnedByCurrentUser(record, currentUser))
            .Select(record => record.Name)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(unauthorized))
            throw new InvalidOperationException($"El registro '{unauthorized}' no pertenece al owner autenticado.");
    }

    private static HardwareStateOptionDto ResolveHardwareStateOption(int stateValue)
    {
        return HardwareStates.FirstOrDefault(item => item.Value == stateValue)
            ?? new HardwareStateOptionDto
            {
                Value = stateValue,
                Label = $"Estado {stateValue}",
                Tone = "neutral",
                ActionKey = "",
                ActionLabel = "",
                HasAction = false
            };
    }

    private static int NormalizeHardwareStateValue(int stateValue) =>
        stateValue <= 0 ? HardwareStateWaitingDocumentation : stateValue;

    private static List<string> ResolveHardwareStageRecordIds(HardwareStageSaveRequest request)
    {
        var rawIds = (request.RecordIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        if (!string.IsNullOrWhiteSpace(request.RecordId))
            rawIds.Insert(0, request.RecordId);

        var normalized = rawIds
            .Select(id => NormalizeGuid(id, nameof(request.RecordIds)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
            throw new InvalidOperationException("Selecciona al menos una fila de Hardware.");

        return normalized;
    }

    private static List<string> ResolveHardwareBulkEditRecordIds(HardwareBulkEditRequest request)
    {
        var normalized = (request.RecordIds ?? new List<string>())
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => NormalizeGuid(id, nameof(request.RecordIds)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
            throw new InvalidOperationException("Selecciona al menos una fila de Hardware para editar.");

        return normalized;
    }

    private async Task<Dictionary<string, object?>> BuildHardwareBulkEditPayloadAsync(
        HardwareBulkEditRequest request,
        IReadOnlyList<HardwareAttributeMetadata> attributes,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (request.OwnerChanged)
        {
            var ownerId = NormalizeOptionalGuid(request.OwnerId);
            if (string.IsNullOrWhiteSpace(ownerId))
                throw new InvalidOperationException("Selecciona un propietario valido de la lista.");

            var ownerNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
                HardwareTableLogicalName,
                HardwareOwnerLogicalName,
                HardwareOwnerLogicalName,
                user,
                ct);
            payload[$"{ownerNavigationProperty}@odata.bind"] = $"/systemusers({ownerId})";
        }

        if (request.ClientChanged)
        {
            EnsureHardwareAttributeExists(attributes, HardwareClientLookupLogicalName);
            var clientId = NormalizeOptionalGuid(request.ClientId);
            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException("Selecciona un cliente valido de la lista.");

            var clientNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
                HardwareTableLogicalName,
                HardwareClientLookupLogicalName,
                HardwareClientLookupLogicalName,
                user,
                ct);
            payload[$"{clientNavigationProperty}@odata.bind"] = $"/{ClientsEntitySetName}({clientId})";
        }

        if (request.QuantityChanged)
            payload[HardwareQuantityLogicalName] = ParseHardwareBulkInteger(request.Quantity, "Cantidad");
        if (request.SaleUnitChanged)
            payload[HardwareSaleUnitLogicalName] = ParseHardwareBulkNonNegativeCurrency(request.SaleUnit, "Venta unidad");
        if (request.TotalSaleChanged)
            payload[HardwareTotalSaleLogicalName] = ParseHardwareBulkNonNegativeCurrency(request.TotalSale, "Total linea");
        if (request.StateChanged)
            payload[HardwareStateLogicalName] = ParseHardwareBulkState(request.StateValue);
        if (request.PurchaseOrderNumberChanged)
            payload[HardwarePurchaseOrderNumberLogicalName] = NormalizeHardwareCell(request.PurchaseOrderNumber);
        if (request.OdcDateChanged)
            payload[HardwareOdcDateLogicalName] = ParseHardwareBulkOptionalDate(request.OdcDateValue, "Fecha ODC");
        if (request.SupplierUnitCostChanged)
            payload[HardwareSupplierUnitCostLogicalName] = ParseHardwareBulkNonNegativeCurrency(request.SupplierUnitCost, "Costo unt proveedor");
        if (request.SupplierTotalChanged)
            payload[HardwareSupplierTotalLogicalName] = ParseHardwareBulkNonNegativeCurrency(request.SupplierTotal, "Total proveedor");
        if (request.FreightValueChanged)
            payload[HardwareFreightValueLogicalName] = ParseHardwareBulkNonNegativeCurrency(request.FreightValue, "Valor flete");
        if (request.UtilityChanged)
            payload[HardwareUtilityLogicalName] = ParseHardwareBulkCurrency(request.Utility, "Utilidad");
        if (request.MarginValueChanged)
            payload[HardwareMarginValueLogicalName] = ParseHardwareBulkCurrency(request.MarginValue, "Valor margen");
        if (request.ProviderChanged)
            payload[HardwareSupplierLogicalName] = NormalizeHardwareCell(request.Provider);
        if (request.SupplierPaymentDateChanged)
            payload[HardwareSupplierPaymentDateLogicalName] = ParseHardwareBulkOptionalDate(request.SupplierPaymentDateValue, "Fecha pago proveedor");
        if (request.DeliveryRecordDateChanged)
            payload[HardwareDeliveryRecordDateLogicalName] = ParseHardwareBulkOptionalDate(request.DeliveryRecordDateValue, "Fecha acta entrega");
        if (request.InvoiceNumberChanged)
            payload[HardwareInvoiceNumberLogicalName] = NormalizeHardwareCell(request.InvoiceNumber);

        foreach (var fieldName in payload.Keys
                     .Where(fieldName => !fieldName.EndsWith("@odata.bind", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            EnsureHardwareAttributeExists(attributes, fieldName);
        }

        return payload;
    }

    private static Dictionary<string, HardwareDocumentationLineSaveRequest> ResolveHardwareDocumentationRows(
        HardwareStageSaveRequest request,
        IReadOnlyList<HardwareBoardRowDto> currentRecords)
    {
        var rows = (request.DocumentationRows ?? new List<HardwareDocumentationLineSaveRequest>())
            .Where(row => !string.IsNullOrWhiteSpace(row.RecordId))
            .GroupBy(row => NormalizeGuid(row.RecordId, nameof(row.RecordId)), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        if (rows.Count == 0 && currentRecords.Count == 1)
        {
            var recordKey = NormalizeGuid(currentRecords[0].RecordId, nameof(currentRecords));
            rows[recordKey] = new HardwareDocumentationLineSaveRequest
            {
                RecordId = currentRecords[0].RecordId,
                OdcDateValue = request.OdcDateValue,
                SupplierUnitCost = request.SupplierUnitCost,
                Provider = request.Provider,
                SupplierDocumentGroupKey = request.SupplierDocumentGroupKey,
                SupplierDocumentGroupLabel = request.SupplierDocumentGroupLabel
            };
        }

        foreach (var record in currentRecords)
        {
            var recordKey = NormalizeGuid(record.RecordId, nameof(record.RecordId));
            if (!rows.ContainsKey(recordKey))
                throw new InvalidOperationException($"Faltan los datos de documentación para la fila {record.Name}.");
        }

        return rows;
    }

    private static List<decimal> SplitHardwareFreight(decimal totalFreight, int count)
    {
        if (count <= 0)
            return new List<decimal>();

        var roundedTotal = RoundCurrency(totalFreight);
        var baseValue = RoundCurrency(roundedTotal / count);
        var result = Enumerable.Repeat(baseValue, count).ToList();
        var assigned = result.Sum();
        result[^1] = RoundCurrency(result[^1] + (roundedTotal - assigned));
        return result;
    }

    private static decimal CalculateHardwareMarginValue(decimal priceSale, decimal supplierTotal) =>
        RoundCurrency(priceSale - supplierTotal);

    private static decimal CalculateHardwareUtility(decimal priceSale, decimal marginValue)
    {
        if (priceSale <= 0m)
            return 0m;

        return Math.Round(marginValue / priceSale, 4, MidpointRounding.AwayFromZero);
    }

    private static bool HasHardwareAttribute(IReadOnlyList<HardwareAttributeMetadata> attributes, string logicalName)
    {
        return attributes.Any(attribute =>
            string.Equals(attribute.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(attribute.SchemaName, logicalName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasHardwareFile(JsonElement item, string logicalName, string fileNameLogicalName)
    {
        return !string.IsNullOrWhiteSpace(ReadString(item, logicalName))
            || !string.IsNullOrWhiteSpace(ReadString(item, fileNameLogicalName));
    }

    private static string ResolveHardwareFileName(JsonElement item, string logicalName, string fileNameLogicalName)
    {
        return FirstNonEmpty(
            ReadString(item, fileNameLogicalName).Trim(),
            ReadString(item, $"{logicalName}{FormattedValueAnnotationSuffix}").Trim(),
            !string.IsNullOrWhiteSpace(ReadString(item, logicalName).Trim()) ? "Archivo cargado" : "");
    }

    private static string FormatHardwareDateValue(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    private static string FormatHardwareDateDisplay(DateOnly? value) =>
        value?.ToString("dd/MM/yyyy", HardwareCulture) ?? "";

    private static void EnsureHardwareActionState(int currentState, int expectedState, string currentLabel)
    {
        if (currentState == expectedState)
            return;

        var expected = ResolveHardwareStateOption(expectedState).Label;
        throw new InvalidOperationException(
            $"Esta acción solo está disponible cuando el hardware está en '{expected}'. Estado actual: '{FirstNonEmpty(currentLabel, ResolveHardwareStateOption(currentState).Label)}'.");
    }

    private static void EnsureHardwareActionStateOneOf(
        int currentState,
        IReadOnlyCollection<int> expectedStates,
        string currentLabel)
    {
        var normalizedExpectedStates = expectedStates
            .Select(NormalizeHardwareStateValue)
            .Distinct()
            .ToList();
        if (normalizedExpectedStates.Contains(currentState))
            return;

        var expected = string.Join(
            "' o '",
            normalizedExpectedStates.Select(state => ResolveHardwareStateOption(state).Label));
        throw new InvalidOperationException(
            $"Esta acción solo está disponible cuando el hardware está en '{expected}'. Estado actual: '{FirstNonEmpty(currentLabel, ResolveHardwareStateOption(currentState).Label)}'.");
    }

    private static void EnsureHardwareOrderFilePresent(
        IEnumerable<HardwareBoardRowDto> records,
        string fieldName,
        string fieldLabel)
    {
        if (!records.Any(record => HasHardwareFile(record, fieldName)))
            throw new InvalidOperationException($"Debes cargar el archivo '{fieldLabel}' de la orden antes de avanzar esta etapa.");
    }

    private static void EnsureHardwareSupplierDocumentFilePresentForEachGroup(
        IReadOnlyList<HardwareBoardRowDto> records,
        string fieldName,
        string fieldLabel)
    {
        var missingGroups = records
            .GroupBy(
                ResolveHardwareSupplierDocumentGroupKey,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => !group.Any(record => HasHardwareFile(record, fieldName)))
            .Select(group => ResolveHardwareSupplierDocumentGroupLabel(group.First()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missingGroups.Count == 0)
            return;

        throw new InvalidOperationException(
            $"Debes cargar el archivo '{fieldLabel}' para cada proforma. Pendiente: {string.Join(", ", missingGroups)}.");
    }

    private static bool HasHardwareFile(HardwareBoardRowDto record, string fieldName) =>
        fieldName switch
        {
            HardwareOrderPurchaseFileLogicalName => record.HasOrderPurchase,
            HardwareProformaFileLogicalName => record.HasProforma,
            HardwareSupplierPurchaseOrderFileLogicalName => record.HasSupplierPurchaseOrder,
            HardwareSupplierPaymentFileLogicalName => record.HasSupplierPaymentProof,
            HardwareDeliveryRecordFileLogicalName => record.HasDeliveryRecord,
            _ => false
        };

    private static string ResolveHardwareSupplierDocumentGroupKey(HardwareBoardRowDto row) =>
        ResolveHardwareSupplierDocumentGroupKey(
            row.SupplierDocumentGroupKey,
            row.SupplierDocumentGroupLabel,
            row.PurchaseOrderNumber,
            row.Provider,
            0);

    private static string ResolveHardwareSupplierDocumentGroupLabel(HardwareBoardRowDto row) =>
        ResolveHardwareSupplierDocumentGroupLabel(row.SupplierDocumentGroupLabel, row.Provider, 0);

    private static string ResolveHardwareSupplierDocumentGroupKey(
        string? key,
        string? label,
        string? purchaseOrderNumber,
        string? provider,
        int index)
    {
        var normalizedKey = NormalizeHardwareGroupToken(key);
        if (!string.IsNullOrWhiteSpace(normalizedKey) && !IsGenericHardwareSupplierDocumentGroupKey(normalizedKey))
            return normalizedKey;

        var normalizedLabel = NormalizeHardwareGroupToken(label);
        if (!string.IsNullOrWhiteSpace(normalizedLabel))
            return normalizedLabel;

        var orderPart = NormalizeHardwareGroupToken(purchaseOrderNumber);
        var providerPart = NormalizeHardwareGroupToken(provider);
        var fallback = string.Join("|", new[] { orderPart, providerPart }.Where(static part => !string.IsNullOrWhiteSpace(part)));
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;

        return $"proforma-{index + 1}";
    }

    private static bool IsGenericHardwareSupplierDocumentGroupKey(string? value)
    {
        var normalized = NormalizeHardwareGroupToken(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (string.Equals(normalized, "proforma", StringComparison.OrdinalIgnoreCase))
            return true;

        return normalized.StartsWith("proforma-", StringComparison.OrdinalIgnoreCase)
            && normalized["proforma-".Length..].All(char.IsDigit);
    }

    private static string ResolveHardwareSupplierDocumentGroupLabel(string? label, string? provider, int index)
    {
        var normalizedLabel = NormalizeHardwareCell(label);
        if (!string.IsNullOrWhiteSpace(normalizedLabel))
            return normalizedLabel.Length <= 200 ? normalizedLabel : normalizedLabel[..200];

        var normalizedProvider = NormalizeHardwareCell(provider);
        if (!string.IsNullOrWhiteSpace(normalizedProvider))
        {
            var value = $"Proforma {normalizedProvider}";
            return value.Length <= 200 ? value : value[..200];
        }

        return $"Proforma {index + 1}";
    }

    private static string NormalizeHardwareGroupToken(string? value)
    {
        var normalized = RemoveHardwareDiacritics(NormalizeHardwareCell(value))
            .ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return "";

        var builder = new StringBuilder(normalized.Length);
        var lastWasDash = false;
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasDash = false;
                continue;
            }

            if (!lastWasDash)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        var result = builder.ToString().Trim('-');
        return result.Length <= 120 ? result : result[..120];
    }

    private static string NormalizeHardwareSupplierDocumentType(string? rawValue)
    {
        var normalized = NormalizeHardwareCell(rawValue)
            .Replace("_", "-", StringComparison.Ordinal)
            .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalized) || normalized == HardwareSupplierDocumentTypeProforma)
            return HardwareSupplierDocumentTypeProforma;

        if (normalized is HardwareSupplierDocumentTypePurchaseOrder
            or "supplier-odc"
            or "odc-proveedor"
            or "odc-al-proveedor"
            or "orden-proveedor"
            or "orden-de-compra-proveedor")
        {
            return HardwareSupplierDocumentTypePurchaseOrder;
        }

        throw new InvalidOperationException("Selecciona si el proveedor se gestionará con proforma o con ODC al proveedor.");
    }

    private static string NormalizeHardwareSupplierDocumentTypeForDisplay(string? rawValue)
    {
        try
        {
            return NormalizeHardwareSupplierDocumentType(rawValue);
        }
        catch
        {
            return HardwareSupplierDocumentTypeProforma;
        }
    }

    private static string ParseHardwareStageDate(string? rawValue, string label)
    {
        if (!TryParseHardwareDate(rawValue, out var date))
            throw new InvalidOperationException($"El campo {label} es obligatorio y debe ser una fecha válida.");

        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static decimal ParseHardwareStageCurrency(decimal? value, string label)
    {
        if (!value.HasValue || value.Value <= 0m)
            throw new InvalidOperationException($"El campo {label} es obligatorio y debe ser mayor a cero.");

        return RoundCurrency(value.Value);
    }

    private static decimal ParseHardwareStageNonNegativeCurrency(decimal? value, string label)
    {
        if (!value.HasValue || value.Value < 0m)
            throw new InvalidOperationException($"El campo {label} es obligatorio y no puede ser negativo.");

        return RoundCurrency(value.Value);
    }

    private static decimal ParseHardwareStageOptionalNonNegativeCurrency(decimal? value, string label)
    {
        if (!value.HasValue)
            return 0m;

        if (value.Value < 0m)
            throw new InvalidOperationException($"El campo {label} no puede ser negativo.");

        return RoundCurrency(value.Value);
    }

    private static int ParseHardwareOrderQuantity(int? value, int lineIndex)
    {
        if (!value.HasValue || value.Value <= 0)
            throw new InvalidOperationException($"El campo cr07a_cant de la fila {lineIndex + 1} debe ser mayor a cero.");

        return value.Value;
    }

    private static int ParseHardwareBulkInteger(int? value, string label)
    {
        if (!value.HasValue || value.Value < 0)
            throw new InvalidOperationException($"El campo {label} debe ser un numero entero no negativo.");

        return value.Value;
    }

    private static decimal ParseHardwareBulkNonNegativeCurrency(decimal? value, string label)
    {
        if (!value.HasValue || value.Value < 0m)
            throw new InvalidOperationException($"El campo {label} no puede ser negativo.");

        return RoundCurrency(value.Value);
    }

    private static decimal ParseHardwareBulkCurrency(decimal? value, string label)
    {
        if (!value.HasValue)
            throw new InvalidOperationException($"El campo {label} debe ser numerico.");

        return RoundCurrency(value.Value);
    }

    private static int ParseHardwareBulkState(int? value)
    {
        if (!value.HasValue)
            throw new InvalidOperationException("Selecciona un estado valido.");

        var normalized = NormalizeHardwareStateValue(value.Value);
        _ = ResolveHardwareStateOption(normalized);
        return normalized;
    }

    private static string? ParseHardwareBulkOptionalDate(string? rawValue, string label)
    {
        var normalized = NormalizeHardwareCell(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        if (!TryParseHardwareDate(normalized, out var date))
            throw new InvalidOperationException($"El campo {label} debe ser una fecha valida.");

        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string RequireHardwareText(string? value, string label)
    {
        var normalized = NormalizeHardwareCell(value);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException($"El campo {label} es obligatorio.");

        return normalized;
    }

    private static void EnsureHardwareAttributeExists(
        IReadOnlyList<HardwareAttributeMetadata> attributes,
        string fieldName)
    {
        if (!HasHardwareAttribute(attributes, fieldName))
            throw new InvalidOperationException($"La tabla Hardware no tiene la columna {fieldName}.");
    }

    private static string ResolveHardwareAllowedFileField(string? fieldName)
    {
        var normalized = NormalizeHardwareCell(fieldName);
        if (string.IsNullOrWhiteSpace(normalized) || !HardwareAllowedFileFields.ContainsKey(normalized))
            throw new InvalidOperationException("El campo de archivo seleccionado no es válido para Hardware.");

        return normalized;
    }

    private static void EnsureHardwareFileAttributeExists(
        IReadOnlyList<HardwareAttributeMetadata> attributes,
        string fieldName)
    {
        var attribute = attributes.FirstOrDefault(item =>
            string.Equals(item.LogicalName, fieldName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.SchemaName, fieldName, StringComparison.OrdinalIgnoreCase));

        if (attribute is null)
        {
            throw new InvalidOperationException(
                $"La tabla Hardware no tiene el campo de archivo {fieldName}. Créalo en Dataverse antes de usar esta etapa.");
        }
    }

    private static void ValidateHardwareAttachmentUpload(string fileName, byte[] content)
    {
        if (content.Length == 0)
            throw new InvalidOperationException("El archivo seleccionado está vacío.");

        if (content.Length > 128 * 1024 * 1024)
            throw new InvalidOperationException("El archivo supera el límite permitido de 128 MB.");

        if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
            throw new InvalidOperationException("El archivo debe conservar una extensión válida.");
    }

    private static string NormalizeHardwareLookupText(string? value)
    {
        return RemoveHardwareDiacritics(value ?? "")
            .Trim()
            .ToLowerInvariant();
    }

    private static string DecodeHardwareCsv(byte[] content)
    {
        try
        {
            return NormalizeHardwareText(new UTF8Encoding(false, true).GetString(content));
        }
        catch (DecoderFallbackException)
        {
            return NormalizeHardwareText(Encoding.Latin1.GetString(content));
        }
    }

    private static string NormalizeHardwareText(string text)
    {
        return (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimStart('\uFEFF');
    }

    private static char DetectHardwareDelimiter(string text)
    {
        var firstLine = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
            ?? "";

        var semicolonCount = CountHardwareDelimiter(firstLine, ';');
        var commaCount = CountHardwareDelimiter(firstLine, ',');
        return semicolonCount > commaCount ? ';' : ',';
    }

    private static int CountHardwareDelimiter(string line, char delimiter)
    {
        var count = 0;
        var inQuotes = false;
        foreach (var character in line ?? string.Empty)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && character == delimiter)
                count++;
        }

        return count;
    }

    private static List<List<string>> ParseHardwareRows(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var currentValue = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';

            if (character == '"')
            {
                if (inQuotes && next == '"')
                {
                    currentValue.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (!inQuotes && character == delimiter)
            {
                currentRow.Add(currentValue.ToString());
                currentValue.Clear();
                continue;
            }

            if (!inQuotes && character == '\n')
            {
                currentRow.Add(currentValue.ToString());
                rows.Add(currentRow);
                currentRow = new List<string>();
                currentValue.Clear();
                continue;
            }

            currentValue.Append(character);
        }

        if (currentValue.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentValue.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }

    private static List<string> PadHardwareRow(IReadOnlyList<string> row, int columnCount)
    {
        var result = new List<string>(columnCount);
        for (var index = 0; index < columnCount; index++)
            result.Add(index < row.Count ? NormalizeHardwareCell(row[index]) : "");

        return result;
    }

    private static string NormalizeHardwareCell(string? value)
    {
        return (value ?? string.Empty)
            .Replace('\u00A0', ' ')
            .Replace('\uFFFD', ' ')
            .Trim();
    }

    private static string SanitizeHardwareHeader(string rawHeader, int position)
    {
        var normalized = NormalizeHardwareCell(rawHeader);
        normalized = string.Join(
            " ",
            normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.IsNullOrWhiteSpace(normalized) ? $"Columna {position}" : normalized;
    }

    private static HardwareAttributeKind InferHardwareColumnKind(string header, IReadOnlyList<string> values)
    {
        var nonEmptyValues = values
            .Select(NormalizeHardwareCell)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        var normalizedHeader = RemoveHardwareDiacritics(header).ToLowerInvariant();

        if (LooksLikeHardwareIdentifierField(normalizedHeader))
            return HardwareAttributeKind.String;

        if (LooksLikeHardwareDateField(normalizedHeader)
            && (nonEmptyValues.Count == 0 || nonEmptyValues.All(TryParseHardwareDate)))
        {
            return HardwareAttributeKind.Date;
        }

        if (LooksLikeHardwareMoneyField(normalizedHeader)
            && (nonEmptyValues.Count == 0 || nonEmptyValues.All(TryParseHardwareDecimalValue)))
        {
            return HardwareAttributeKind.Money;
        }

        if (LooksLikeHardwarePercentField(normalizedHeader)
            && (nonEmptyValues.Count == 0 || nonEmptyValues.All(TryParseHardwareDecimalValue)))
        {
            return HardwareAttributeKind.Decimal;
        }

        if (LooksLikeHardwareLongTextField(normalizedHeader))
            return HardwareAttributeKind.Memo;

        if (nonEmptyValues.Count > 0 && nonEmptyValues.All(TryParseHardwareBoolean))
            return HardwareAttributeKind.Boolean;

        if (nonEmptyValues.Count > 0 && nonEmptyValues.All(TryParseHardwareDate))
            return HardwareAttributeKind.Date;

        if (nonEmptyValues.Count > 0 && nonEmptyValues.All(TryParseHardwareInteger))
            return LooksLikeHardwareQuantityField(normalizedHeader)
                ? HardwareAttributeKind.Integer
                : HardwareAttributeKind.Decimal;

        if (nonEmptyValues.Count > 0 && nonEmptyValues.All(TryParseHardwareDecimalValue))
            return HardwareAttributeKind.Decimal;

        if (nonEmptyValues.Any(static value => value.Length > 120))
            return HardwareAttributeKind.Memo;

        return HardwareAttributeKind.String;
    }

    private static bool LooksLikeHardwareIdentifierField(string normalizedHeader)
    {
        return normalizedHeader.StartsWith("no ", StringComparison.Ordinal)
            || normalizedHeader.StartsWith("no.", StringComparison.Ordinal)
            || normalizedHeader.StartsWith("#", StringComparison.Ordinal)
            || normalizedHeader.StartsWith("numero ", StringComparison.Ordinal)
            || normalizedHeader.StartsWith("nro ", StringComparison.Ordinal)
            || normalizedHeader.StartsWith("num ", StringComparison.Ordinal);
    }

    private static bool LooksLikeHardwareDateField(string normalizedHeader) =>
        normalizedHeader.Contains("fecha", StringComparison.Ordinal);

    private static bool LooksLikeHardwareMoneyField(string normalizedHeader) =>
        normalizedHeader.Contains("valor", StringComparison.Ordinal)
        || normalizedHeader.Contains("costo", StringComparison.Ordinal)
        || normalizedHeader.Contains("precio", StringComparison.Ordinal)
        || normalizedHeader.Contains("total", StringComparison.Ordinal);

    private static bool LooksLikeHardwarePercentField(string normalizedHeader) =>
        normalizedHeader.Contains("%", StringComparison.Ordinal)
        || normalizedHeader.Contains("porcentaje", StringComparison.Ordinal)
        || normalizedHeader.Contains("utilidad", StringComparison.Ordinal);

    private static bool LooksLikeHardwareQuantityField(string normalizedHeader) =>
        normalizedHeader.Contains("cant", StringComparison.Ordinal)
        || normalizedHeader.Contains("cantidad", StringComparison.Ordinal);

    private static bool LooksLikeHardwareLongTextField(string normalizedHeader) =>
        normalizedHeader.Contains("descripcion", StringComparison.Ordinal)
        || normalizedHeader.Contains("detalle", StringComparison.Ordinal)
        || normalizedHeader.Contains("observ", StringComparison.Ordinal)
        || normalizedHeader.Contains("link", StringComparison.Ordinal);

    private static string CreateUniqueHardwareLogicalName(string label, ISet<string> usedNames, int position)
    {
        var baseName = BuildHardwareLogicalBase(label);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = $"columna_{position}";

        var candidate = TruncateHardwareLogicalName($"cr07a_{baseName}");
        if (usedNames.Add(candidate))
            return candidate;

        var suffix = 2;
        while (true)
        {
            var withSuffix = TruncateHardwareLogicalName($"cr07a_{baseName}_{suffix}");
            if (usedNames.Add(withSuffix))
                return withSuffix;

            suffix++;
        }
    }

    private static string BuildHardwareLogicalBase(string label)
    {
        var normalized = RemoveHardwareDiacritics(label)
            .ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        var previousUnderscore = false;

        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (builder.Length == 0 && char.IsDigit(character))
                    builder.Append("col_");

                builder.Append(character);
                previousUnderscore = false;
                continue;
            }

            if (previousUnderscore)
                continue;

            builder.Append('_');
            previousUnderscore = true;
        }

        return builder
            .ToString()
            .Trim('_');
    }

    private static string TruncateHardwareLogicalName(string value)
    {
        const int maxLength = 48;
        if (value.Length <= maxLength)
            return value;

        return value[..maxLength].TrimEnd('_');
    }

    private static string CreateHardwareSchemaName(string logicalName)
    {
        var baseName = logicalName.StartsWith("cr07a_", StringComparison.OrdinalIgnoreCase)
            ? logicalName["cr07a_".Length..]
            : logicalName;
        var pascal = string.Join(
            "_",
            baseName
                .Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => char.ToUpperInvariant(segment[0]) + segment[1..]));
        var schemaName = $"cr07a_{pascal}";
        return schemaName.Length <= 50 ? schemaName : schemaName[..50];
    }

    private static string NormalizeHardwareAttributeAlias(string? value)
    {
        return string.Join(
            "",
            (value ?? string.Empty)
                .Where(char.IsLetterOrDigit))
            .ToLowerInvariant();
    }

    private static int DetermineHardwareMaxLength(
        HardwareAttributeKind kind,
        string header,
        IReadOnlyList<string> values)
    {
        if (kind == HardwareAttributeKind.Memo)
            return 4000;

        if (kind != HardwareAttributeKind.String)
            return 0;

        var observedMax = values.Count == 0 ? 0 : values.Max(static value => value.Length);
        if (header.Contains("link", StringComparison.OrdinalIgnoreCase))
            return Math.Clamp(Math.Max(observedMax + 40, 250), 250, 1000);

        return Math.Clamp(Math.Max(observedMax + 20, 100), 100, 4000);
    }

    private static string BuildHardwareColumnDescription(HardwareManagedColumnDefinition column)
    {
        return column.IsSystemColumn
            ? "Campo tecnico generado por el modulo Hardware."
            : $"Columna importada desde el CSV de Hardware: {column.SourceHeader}.";
    }

    private static string GetHardwareDelimiterLabel(char delimiter) =>
        delimiter == ';' ? "Punto y coma (;)" : "Coma (,)";

    private static string GetHardwareAttributeKindLabel(HardwareAttributeKind kind) =>
        kind switch
        {
            HardwareAttributeKind.Date => "Fecha",
            HardwareAttributeKind.Money => "Moneda",
            HardwareAttributeKind.Integer => "Numero entero",
            HardwareAttributeKind.Decimal => "Decimal",
            HardwareAttributeKind.Boolean => "Si/No",
            HardwareAttributeKind.Memo => "Texto largo",
            HardwareAttributeKind.File => "Archivo",
            _ => "Texto"
        };

    private static bool TryParseHardwareDate(string? rawValue) =>
        TryParseHardwareDate(rawValue, out _);

    private static bool TryParseHardwareDate(string? rawValue, out DateOnly date)
    {
        var normalized = NormalizeHardwareCell(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            date = default;
            return false;
        }

        if (DateOnly.TryParseExact(normalized, HardwareDateFormats, HardwareCulture, DateTimeStyles.AllowWhiteSpaces, out date))
            return true;

        if (DateOnly.TryParse(normalized, HardwareCulture, DateTimeStyles.AllowWhiteSpaces, out date))
            return true;

        return TryParseDateOnly(normalized, out date);
    }

    private static DateOnly ParseHardwareDate(string rawValue, string label)
    {
        if (TryParseHardwareDate(rawValue, out var date))
            return date;

        throw new InvalidOperationException($"El valor de {label} debe ser una fecha valida.");
    }

    private static bool TryParseHardwareDecimalValue(string? rawValue) =>
        TryParseHardwareDecimalValue(rawValue, out _);

    private static bool TryParseHardwareDecimalValue(string? rawValue, out decimal value)
    {
        var normalized = NormalizeHardwareNumericLiteral(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            value = 0m;
            return false;
        }

        if (decimal.TryParse(normalized, NumberStyles.Number, HardwareCulture, out value))
            return true;

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static decimal ParseHardwareDecimal(string rawValue, string label)
    {
        if (TryParseHardwareDecimalValue(rawValue, out var value))
            return value;

        throw new InvalidOperationException($"El valor de {label} debe ser numerico.");
    }

    private static bool TryParseHardwareInteger(string? rawValue)
    {
        var normalized = NormalizeHardwareNumericLiteral(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (int.TryParse(normalized, NumberStyles.Integer, HardwareCulture, out _))
            return true;

        if (!TryParseHardwareDecimalValue(rawValue, out var decimalValue))
            return false;

        return decimal.Truncate(decimalValue) == decimalValue;
    }

    private static int ParseHardwareInteger(string rawValue, string label)
    {
        if (int.TryParse(NormalizeHardwareNumericLiteral(rawValue), NumberStyles.Integer, HardwareCulture, out var value))
            return value;

        if (TryParseHardwareDecimalValue(rawValue, out var decimalValue) && decimal.Truncate(decimalValue) == decimalValue)
            return (int)decimalValue;

        throw new InvalidOperationException($"El valor de {label} debe ser un numero entero.");
    }

    private static bool TryParseHardwareBoolean(string? rawValue)
    {
        var normalized = NormalizeHardwareCell(rawValue).ToLowerInvariant();
        return normalized is "si" or "sí" or "no" or "true" or "false" or "1" or "0";
    }

    private static bool ParseHardwareBoolean(string rawValue, string label)
    {
        var normalized = NormalizeHardwareCell(rawValue).ToLowerInvariant();
        return normalized switch
        {
            "si" => true,
            "sí" => true,
            "true" => true,
            "1" => true,
            "no" => false,
            "false" => false,
            "0" => false,
            _ => throw new InvalidOperationException($"El valor de {label} debe ser Si/No.")
        };
    }

    private static string NormalizeHardwareNumericLiteral(string? rawValue)
    {
        var normalized = NormalizeHardwareCell(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
            return "";

        normalized = normalized
            .Replace("$", "", StringComparison.Ordinal)
            .Replace("%", "", StringComparison.Ordinal)
            .Replace("COP", "", StringComparison.OrdinalIgnoreCase)
            .Replace("USD", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return string.Join("", normalized.Where(character =>
            char.IsDigit(character)
            || character == '.'
            || character == ','
            || character == '-'));
    }

    private static string RemoveHardwareDiacritics(string value)
    {
        var normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string BuildHardwareProvisionMessage(
        bool tableCreated,
        int createdColumnsCount,
        int importedCount,
        int skippedDuplicatesCount)
    {
        var tableMessage = tableCreated ? "Se creo la tabla Hardware" : "Se reutilizo la tabla Hardware";
        var columnsMessage = createdColumnsCount switch
        {
            0 => "sin crear columnas nuevas",
            1 => "creando 1 columna nueva",
            _ => $"creando {createdColumnsCount} columnas nuevas"
        };
        var importMessage = importedCount switch
        {
            0 => "No se importaron filas nuevas",
            1 => "Se importo 1 fila",
            _ => $"Se importaron {importedCount} filas"
        };

        if (skippedDuplicatesCount <= 0)
            return $"{tableMessage}, {columnsMessage}. {importMessage}.";

        var duplicatesMessage = skippedDuplicatesCount == 1
            ? "Se omitio 1 fila duplicada"
            : $"Se omitieron {skippedDuplicatesCount} filas duplicadas";
        return $"{tableMessage}, {columnsMessage}. {importMessage}. {duplicatesMessage}.";
    }

    private sealed class HardwareCsvDocument
    {
        public string FileName { get; init; } = "";
        public char Delimiter { get; init; }
        public IReadOnlyList<HardwareManagedColumnDefinition> Columns { get; init; } = Array.Empty<HardwareManagedColumnDefinition>();
        public IReadOnlyList<HardwareCsvRow> Rows { get; init; } = Array.Empty<HardwareCsvRow>();
    }

    private sealed class HardwareCsvRow
    {
        public int SourceRowNumber { get; init; }
        public IReadOnlyList<string> Values { get; init; } = Array.Empty<string>();
    }

    private sealed class HardwareManagedColumnDefinition
    {
        public int Index { get; init; }
        public string SourceHeader { get; init; } = "";
        public string DisplayLabel { get; init; } = "";
        public string LogicalName { get; init; } = "";
        public string SchemaName { get; init; } = "";
        public string ResolvedLogicalName { get; set; } = "";
        public HardwareAttributeKind Kind { get; init; }
        public string ExampleValue { get; init; } = "";
        public int MaxLength { get; init; }
        public int Precision { get; init; } = 4;
        public bool IsSystemColumn { get; init; }
    }

    private sealed class HardwareAttributeMetadata
    {
        public string LogicalName { get; init; } = "";
        public string SchemaName { get; init; } = "";
        public string AttributeType { get; init; } = "";
    }

    private enum HardwareAttributeKind
    {
        String,
        Memo,
        Date,
        Money,
        Integer,
        Decimal,
        Boolean,
        File
    }
}
