using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using CotizadorInterno.Web.Models.Licenciamiento;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string LicensingConsumptionLogicalName = "cr07a_consumointcomex";
    private const string LicensingConsumptionFallbackEntitySetName = "cr07a_consumointcomexes";
    private const string LicensingConsumptionFallbackIdField = "cr07a_consumointcomexid";
    private const string LicensingConsumptionFallbackPrimaryNameField = "cr07a_name";
    private const string LicensingAccountLookupField = "cr07a_accountid";
    private const string LicensingAccountLookupTargetLogicalName = "cr07a_accountidicp";
    private const string LicensingAccountLookupTargetFallbackEntitySetName = "cr07a_accountidicps";
    private const string LicensingAccountLookupTargetFallbackIdField = "cr07a_accountidicpid";
    private const string LicensingAccountLookupTargetFallbackPrimaryNameField = "cr07a_name";
    private const string LicensingCustomerNameField = "cr07a_nombrecliente";
    private const string LicensingVendorField = "cr07a_vendor";
    private const string LicensingProductLookupField = "cr07a_producto";
    private const string LicensingDaysField = "cr07a_dias";
    private const string LicensingBillingIntervalField = "cr07a_mesconsumo";
    private const string LicensingInvoiceDateField = "cr07a_factura";
    private const string LicensingTotalUsdField = "cr07a_valortotalusd";
    private const string LicensingUnitUsdField = "cr07a_unidadusd";
    private const string LicensingQuantityField = "cr07a_cantidad";
    private const string LicensingTrmField = "cr07a_trm";
    private const string LicensingTotalCopField = "cr07a_pesostotal";
    private const string LicensingContractTypeField = "cr07a_tipocontrato";
    private const int LicensingContractMonthly = 645250000;
    private const int LicensingContractOnetime = 645250001;
    private const int LicensingContractPrepaid = 645250002;
    private const string LicensingProductLookupTargetLogicalName = "cr07a_precioscloud";
    private const string LicensingProductLookupTargetFallbackEntitySetName = "cr07a_preciosclouds";
    private const string LicensingProductLookupTargetFallbackIdField = "cr07a_precioscloudid";
    private const string LicensingProductLookupTargetFallbackPrimaryNameField = "cr07a_priceableitemdescription";
    private const string LicensingProductDescriptionLookupField = "cr07a_priceableitemdescription";
    private const string LicensingModifiedOnField = "modifiedon";
    private const string LicensingManualBreakdownProductName = "Acronis Cyber Cloud Commitment (SPLA) Manual Provisioning - One Time Setup Fee";

    private static readonly CultureInfo LicensingCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly string[] LicensingAccountSearchFieldCandidates =
    {
        "cr07a_accountid",
        "cr07a_accountidicp",
        "cr07a_companyaccountid",
        "cr07a_idcuenta",
        "cr07a_name"
    };
    private static readonly string[] LicensingProductSearchFieldCandidates =
    {
        LicensingProductDescriptionLookupField,
        "cr07a_name"
    };
    private static readonly IReadOnlyList<LicenciamientoContractTypeOptionDto> LicensingContractTypeOptions = new[]
    {
        new LicenciamientoContractTypeOptionDto { Value = LicensingContractMonthly, Label = "Monthly" },
        new LicenciamientoContractTypeOptionDto { Value = LicensingContractOnetime, Label = "Onetime" },
        new LicenciamientoContractTypeOptionDto { Value = LicensingContractPrepaid, Label = "Prepaid" }
    };

    private readonly ConcurrentDictionary<string, LicensingMetadata> _licensingMetadataCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<LicenciamientoBoardDto> GetLicenciamientoBoardAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveLicensingMetadataAsync(httpContext.User, ct);
        var select = BuildLicensingSelectClause(metadata);
        var orderBy = Uri.EscapeDataString($"{LicensingInvoiceDateField} desc,{LicensingModifiedOnField} desc");
        var relativeUrl = $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}?$select={select}&$orderby={orderBy}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, httpContext.User, ct, AddFormattedValueHeaders);
        var records = items
            .Select(item => BuildLicensingRecordDto(metadata, item))
            .Where(static item => item is not null)
            .Select(static item => item!)
            .OrderByDescending(item => item.FacturaValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.NombreCliente, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ProductDisplay, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var facturaOptions = records
            .Where(item => !string.IsNullOrWhiteSpace(item.FacturaValue))
            .GroupBy(item => item.FacturaValue, StringComparer.OrdinalIgnoreCase)
            .Select(group => new LicenciamientoFacturaOptionDto
            {
                Value = group.Key,
                Label = FirstNonEmpty(group.First().FacturaDisplay, group.Key),
                Count = group.Count()
            })
            .OrderByDescending(item => item.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new LicenciamientoBoardDto
        {
            Records = records,
            FacturaOptions = facturaOptions,
            ContractTypeOptions = GetLicensingContractTypeOptions(),
            TotalCount = records.Count,
            TotalUsd = RoundCurrency(records.Sum(item => item.ValorTotalUsd)),
            TotalCop = RoundCurrency(records.Sum(item => item.PesosTotal)),
            Message = records.Count == 0
                ? "No hay consumos cargados."
                : $"Se cargaron {records.Count} consumo(s)."
        };
    }

    public async Task<LicenciamientoPreviewResultDto> PreviewLicenciamientoUploadAsync(
        string fileName,
        byte[] content,
        CancellationToken ct = default)
    {
        if (content.Length == 0)
            throw new InvalidOperationException("El archivo seleccionado esta vacio.");

        var extension = Path.GetExtension(fileName ?? "");
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El archivo debe estar en formato .xlsx o .xlsm.");
        }

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveLicensingMetadataAsync(httpContext.User, ct);
        var rows = ParseLicensingWorkbook(fileName ?? "consumo.xlsx", content);
        await ResolvePreviewLookupsAsync(metadata, rows, httpContext.User, ct);
        ApplyPreviewLookupWarnings(metadata, rows);

        var totalRows = rows.Count;
        var validRows = rows.Count(static row => row.IsValid && !row.RequiresBreakdown);
        return new LicenciamientoPreviewResultDto
        {
            FileName = Path.GetFileName(fileName ?? "consumo.xlsx"),
            Rows = rows,
            ContractTypeOptions = GetLicensingContractTypeOptions(),
            TotalRows = totalRows,
            ValidRows = validRows,
            WarningRows = rows.Count(static row => row.Warnings.Count > 0),
            TotalUsd = RoundCurrency(rows.Where(static row => row.IsValid).Sum(static row => row.ValorTotalUsd)),
            Message = totalRows == 0
                ? "No se encontraron filas para importar."
                : $"Vista previa lista: {validRows} de {totalRows} fila(s) validas."
        };
    }

    public async Task<IReadOnlyList<LicenciamientoLookupItemDto>> SearchLicenciamientoAccountsAsync(
        string query,
        int top = 12,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        query = (query ?? "").Trim();
        if (query.Length < 2)
            return Array.Empty<LicenciamientoLookupItemDto>();

        var metadata = await ResolveLicensingMetadataAsync(httpContext.User, ct);
        return await SearchLicensingLookupOptionsAsync(
            metadata.AccountMetadata,
            metadata.AccountSearchFields,
            metadata.AccountAttributeTypes,
            query,
            Math.Clamp(top, 1, 25),
            httpContext.User,
            ct);
    }

    public async Task<IReadOnlyList<LicenciamientoLookupItemDto>> SearchLicenciamientoProductsAsync(
        string query,
        int top = 12,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        query = (query ?? "").Trim();
        if (query.Length < 2)
            return Array.Empty<LicenciamientoLookupItemDto>();

        var metadata = await ResolveLicensingMetadataAsync(httpContext.User, ct);
        return await SearchLicensingLookupOptionsAsync(
            metadata.ProductMetadata,
            metadata.ProductSearchFields,
            metadata.ProductAttributeTypes,
            query,
            Math.Clamp(top, 1, 25),
            httpContext.User,
            ct);
    }

    public async Task<LicenciamientoImportResultDto> ImportLicenciamientoRowsAsync(
        LicenciamientoImportRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var rows = request.Rows ?? new List<LicenciamientoPreviewRowDto>();
        if (rows.Count == 0)
            throw new InvalidOperationException("No hay filas para procesar.");

        var normalizedRows = rows
            .Select(NormalizeLicensingImportRow)
            .ToList();
        var pendingBreakdownRows = normalizedRows
            .Count(static row => row.RequiresBreakdown && !row.BreakdownGenerated);
        if (pendingBreakdownRows > 0)
            throw new InvalidOperationException(pendingBreakdownRows == 1
                ? "Hay 1 fila de cargo manual pendiente por desglosar."
                : $"Hay {pendingBreakdownRows} filas de cargo manual pendientes por desglosar.");

        var invalidRows = normalizedRows
            .Where(static row => !row.IsValid || row.Errors.Count > 0)
            .ToList();
        if (invalidRows.Count > 0)
            throw new InvalidOperationException($"La vista previa tiene {invalidRows.Count} fila(s) con errores. Corrige el Excel y vuelve a cargarlo.");

        var metadata = await ResolveLicensingMetadataAsync(httpContext.User, ct);
        var rowsToCreate = normalizedRows
            .Where(row => !ShouldSkipLicensingImportRow(metadata, row))
            .ToList();
        if (rowsToCreate.Count == 0)
            throw new InvalidOperationException("No hay filas con lookup de producto para procesar. Selecciona al menos un producto valido en la vista previa.");

        var created = 0;
        foreach (var row in rowsToCreate)
        {
            var payload = BuildLicensingCreatePayload(metadata, row);
            await CallDataverseSendAsync(
                $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}",
                "POST",
                payload,
                httpContext.User,
                ct);
            created++;
        }

        return new LicenciamientoImportResultDto
        {
            CreatedCount = created,
            SkippedCount = rows.Count - created,
            Message = BuildLicensingImportMessage(created, rows.Count - created)
        };
    }

    public async Task<LicenciamientoAdjustTrmResultDto> AdjustLicenciamientoTrmAsync(
        LicenciamientoAdjustTrmRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (!TryParseDateOnly(request.FacturaValue, out var invoiceDate))
            throw new InvalidOperationException("Debes seleccionar una factura valida.");

        if (request.Trm <= 0)
            throw new InvalidOperationException("La TRM debe ser mayor a cero.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveLicensingMetadataAsync(httpContext.User, ct);
        var select = string.Join(",", new[] { metadata.BaseMetadata.PrimaryIdField, LicensingTotalUsdField }.Distinct(StringComparer.OrdinalIgnoreCase));
        var filter = $"{LicensingInvoiceDateField} eq {invoiceDate:yyyy-MM-dd}";
        var relativeUrl = $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, httpContext.User, ct);

        var updated = 0;
        var totalUsd = 0m;
        var totalCop = 0m;
        foreach (var item in items)
        {
            var recordId = ReadString(item, metadata.BaseMetadata.PrimaryIdField);
            if (string.IsNullOrWhiteSpace(recordId))
                continue;

            var usd = RoundCurrency(ReadDecimal(item, LicensingTotalUsdField) ?? 0m);
            var cop = RoundCurrency(usd * request.Trm);
            var payload = new Dictionary<string, object?>
            {
                [LicensingTrmField] = ConvertLicensingPayloadValue(metadata, LicensingTrmField, request.Trm),
                [LicensingTotalCopField] = ConvertLicensingPayloadValue(metadata, LicensingTotalCopField, cop)
            };

            await CallDataverseSendAsync(
                $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({NormalizeGuid(recordId, nameof(recordId))})",
                "PATCH",
                payload,
                httpContext.User,
                ct);

            updated++;
            totalUsd += usd;
            totalCop += cop;
        }

        return new LicenciamientoAdjustTrmResultDto
        {
            UpdatedCount = updated,
            Trm = request.Trm,
            TotalUsd = RoundCurrency(totalUsd),
            TotalCop = RoundCurrency(totalCop),
            Message = updated == 1
                ? "Se actualizo 1 fila con la TRM indicada."
                : $"Se actualizaron {updated} filas con la TRM indicada."
        };
    }

    public async Task<LicenciamientoUpdateContractTypeResultDto> UpdateLicenciamientoContractTypeAsync(
        LicenciamientoUpdateContractTypeRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var option = ResolveLicensingContractTypeOption(request.ContractTypeValue)
            ?? throw new InvalidOperationException("El tipo de contrato seleccionado no es valido.");

        var recordIds = (request.RecordIds ?? new List<string>())
            .Select(static value => NormalizeOptionalGuid(value))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (recordIds.Count == 0)
            throw new InvalidOperationException("Selecciona al menos una fila para actualizar.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveLicensingMetadataAsync(httpContext.User, ct);
        var payload = new Dictionary<string, object?>
        {
            [LicensingContractTypeField] = ConvertLicensingPayloadValue(metadata, LicensingContractTypeField, option.Value)
        };

        foreach (var recordId in recordIds)
        {
            await CallDataverseSendAsync(
                $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({recordId})",
                "PATCH",
                payload,
                httpContext.User,
                ct);
        }

        return new LicenciamientoUpdateContractTypeResultDto
        {
            UpdatedCount = recordIds.Count,
            ContractTypeValue = option.Value,
            ContractTypeLabel = option.Label,
            Message = recordIds.Count == 1
                ? $"Se actualizo 1 fila a {option.Label}."
                : $"Se actualizaron {recordIds.Count} filas a {option.Label}."
        };
    }

    private async Task<LicensingMetadata> ResolveLicensingMetadataAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        const string cacheKey = LicensingConsumptionLogicalName;
        if (_licensingMetadataCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var baseMetadata = await ResolveRhEntityMetadataAsync(
            LicensingConsumptionLogicalName,
            LicensingConsumptionFallbackEntitySetName,
            LicensingConsumptionFallbackIdField,
            LicensingConsumptionFallbackPrimaryNameField,
            user,
            ct);
        var accountMetadata = await ResolveRhEntityMetadataAsync(
            LicensingAccountLookupTargetLogicalName,
            LicensingAccountLookupTargetFallbackEntitySetName,
            LicensingAccountLookupTargetFallbackIdField,
            LicensingAccountLookupTargetFallbackPrimaryNameField,
            user,
            ct);
        var productMetadata = await ResolveRhEntityMetadataAsync(
            LicensingProductLookupTargetLogicalName,
            LicensingProductLookupTargetFallbackEntitySetName,
            LicensingProductLookupTargetFallbackIdField,
            LicensingProductLookupTargetFallbackPrimaryNameField,
            user,
            ct);

        var consumptionAttributes = await LoadLicensingAttributeTypesAsync(LicensingConsumptionLogicalName, user, ct);
        var accountAttributes = await LoadLicensingAttributeTypesAsync(LicensingAccountLookupTargetLogicalName, user, ct);
        var productAttributes = await LoadLicensingAttributeTypesAsync(LicensingProductLookupTargetLogicalName, user, ct);

        var accountFieldIsLookup = ResolveLicensingLookupMode(consumptionAttributes, LicensingAccountLookupField, fallback: true);
        var productFieldIsLookup = ResolveLicensingLookupMode(consumptionAttributes, LicensingProductLookupField, fallback: true);
        var accountNavigationProperty = accountFieldIsLookup
            ? await ResolveRhLookupNavigationPropertyAsync(
                LicensingConsumptionLogicalName,
                LicensingAccountLookupField,
                LicensingAccountLookupField,
                user,
                ct)
            : "";
        var productNavigationProperty = productFieldIsLookup
            ? await ResolveRhLookupNavigationPropertyAsync(
                LicensingConsumptionLogicalName,
                LicensingProductLookupField,
                LicensingProductLookupField,
                user,
                ct)
            : "";

        var metadata = new LicensingMetadata
        {
            BaseMetadata = baseMetadata,
            AccountMetadata = accountMetadata,
            ProductMetadata = productMetadata,
            ConsumptionAttributeTypes = consumptionAttributes,
            AccountAttributeTypes = accountAttributes,
            ProductAttributeTypes = productAttributes,
            AccountFieldIsLookup = accountFieldIsLookup,
            ProductFieldIsLookup = productFieldIsLookup,
            AccountNavigationProperty = accountNavigationProperty,
            ProductNavigationProperty = productNavigationProperty,
            AccountSearchFields = ResolveLicensingSearchFields(
                accountAttributes,
                LicensingAccountSearchFieldCandidates,
                accountMetadata.PrimaryNameField),
            ProductSearchFields = ResolveLicensingSearchFields(
                productAttributes,
                LicensingProductDescriptionLookupField,
                LicensingProductSearchFieldCandidates)
        };

        _licensingMetadataCache[cacheKey] = metadata;
        return metadata;
    }

    private async Task<Dictionary<string, string>> LoadLicensingAttributeTypesAsync(
        string logicalName,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(logicalName)}')" +
                "?$select=LogicalName" +
                "&$expand=Attributes($select=LogicalName,AttributeType)";
            var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Attributes", out var attributes)
                || attributes.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var attribute in attributes.EnumerateArray())
            {
                var attributeName = ReadString(attribute, "LogicalName").Trim();
                if (string.IsNullOrWhiteSpace(attributeName))
                    continue;

                result[attributeName] = ReadString(attribute, "AttributeType").Trim();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(ex, "No fue posible resolver los atributos de {LogicalName} para licenciamiento.", logicalName);
        }

        return result;
    }

    private static bool ResolveLicensingLookupMode(
        IReadOnlyDictionary<string, string> attributeTypes,
        string fieldName,
        bool fallback)
    {
        if (attributeTypes.TryGetValue(fieldName, out var attributeType)
            && !string.IsNullOrWhiteSpace(attributeType))
        {
            return string.Equals(attributeType, "Lookup", StringComparison.OrdinalIgnoreCase)
                || string.Equals(attributeType, "Customer", StringComparison.OrdinalIgnoreCase)
                || string.Equals(attributeType, "Owner", StringComparison.OrdinalIgnoreCase);
        }

        return fallback;
    }

    private static IReadOnlyList<string> ResolveLicensingSearchFields(
        IReadOnlyDictionary<string, string> attributeTypes,
        string preferredField,
        IEnumerable<string> candidates)
    {
        return ResolveLicensingSearchFields(
            attributeTypes,
            new[] { preferredField }.Concat(candidates ?? Array.Empty<string>()));
    }

    private static IReadOnlyList<string> ResolveLicensingSearchFields(
        IReadOnlyDictionary<string, string> attributeTypes,
        params object?[] candidateGroups)
    {
        var orderedCandidates = ResolveExplicitLicensingSearchFields(candidateGroups);

        if (attributeTypes.Count == 0)
            return orderedCandidates;

        var existingCandidates = orderedCandidates
            .Where(attributeTypes.ContainsKey)
            .ToList();

        return existingCandidates.Count > 0
            ? existingCandidates
            : orderedCandidates;
    }

    private static IReadOnlyList<string> ResolveExplicitLicensingSearchFields(params object?[] candidateGroups)
    {
        var fields = new List<string>();
        foreach (var group in candidateGroups)
        {
            switch (group)
            {
                case string value when !string.IsNullOrWhiteSpace(value):
                    fields.Add(value.Trim());
                    break;
                case IEnumerable<string> values:
                    fields.AddRange(values.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value.Trim()));
                    break;
            }
        }

        return fields
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<LicenciamientoContractTypeOptionDto> GetLicensingContractTypeOptions() =>
        LicensingContractTypeOptions
            .Select(static option => new LicenciamientoContractTypeOptionDto
            {
                Value = option.Value,
                Label = option.Label
            })
            .ToList();

    private static LicenciamientoContractTypeOptionDto? ResolveLicensingContractTypeOption(int value) =>
        LicensingContractTypeOptions.FirstOrDefault(option => option.Value == value);

    private static string ResolveLicensingContractTypeLabel(int value) =>
        ResolveLicensingContractTypeOption(value)?.Label ?? "";

    private static string BuildLicensingSelectClause(LicensingMetadata metadata)
    {
        var fields = new List<string>
        {
            metadata.BaseMetadata.PrimaryIdField,
            LicensingCustomerNameField,
            LicensingVendorField,
            LicensingDaysField,
            LicensingBillingIntervalField,
            LicensingInvoiceDateField,
            LicensingTotalUsdField,
            LicensingUnitUsdField,
            LicensingQuantityField,
            LicensingTrmField,
            LicensingTotalCopField,
            LicensingContractTypeField,
            LicensingModifiedOnField
        };

        if (metadata.AccountFieldIsLookup)
        {
            fields.Add(BuildLookupValueProperty(LicensingAccountLookupField));
        }
        else
        {
            fields.Add(LicensingAccountLookupField);
        }

        if (metadata.ProductFieldIsLookup)
        {
            fields.Add(BuildLookupValueProperty(LicensingProductLookupField));
        }
        else
        {
            fields.Add(LicensingProductLookupField);
        }

        if (!string.Equals(metadata.BaseMetadata.PrimaryNameField, LicensingAccountLookupField, StringComparison.OrdinalIgnoreCase)
            || !metadata.AccountFieldIsLookup)
        {
            fields.Add(metadata.BaseMetadata.PrimaryNameField);
        }

        return string.Join(",",
            fields
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private LicenciamientoRecordDto? BuildLicensingRecordDto(LicensingMetadata metadata, JsonElement item)
    {
        var recordId = ReadString(item, metadata.BaseMetadata.PrimaryIdField);
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var accountLookupProperty = metadata.AccountFieldIsLookup ? BuildLookupValueProperty(LicensingAccountLookupField) : LicensingAccountLookupField;
        var productLookupProperty = metadata.ProductFieldIsLookup ? BuildLookupValueProperty(LicensingProductLookupField) : LicensingProductLookupField;
        var invoiceDate = ReadDateOnly(item, LicensingInvoiceDateField);
        var contractType = ReadIntFlexible(item, LicensingContractTypeField);

        return new LicenciamientoRecordDto
        {
            RecordId = recordId,
            CompanyAccountId = ReadString(item, accountLookupProperty).Trim(),
            CompanyAccountDisplay = metadata.AccountFieldIsLookup
                ? FirstNonEmpty(ReadLookupFormattedValue(item, accountLookupProperty), ReadString(item, metadata.BaseMetadata.PrimaryNameField), "Sin cuenta")
                : ReadString(item, LicensingAccountLookupField).Trim(),
            NombreCliente = ReadString(item, LicensingCustomerNameField).Trim(),
            Vendor = ReadString(item, LicensingVendorField).Trim(),
            ProductId = ReadString(item, productLookupProperty).Trim(),
            ProductDisplay = metadata.ProductFieldIsLookup
                ? FirstNonEmpty(ReadLookupFormattedValue(item, productLookupProperty), "Sin producto")
                : ReadString(item, LicensingProductLookupField).Trim(),
            Days = ReadIntFlexible(item, LicensingDaysField),
            BillingInterval = ReadString(item, LicensingBillingIntervalField).Trim(),
            FacturaValue = invoiceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? ReadString(item, LicensingInvoiceDateField).Trim(),
            FacturaDisplay = invoiceDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? ReadString(item, LicensingInvoiceDateField).Trim(),
            ValorTotalUsd = RoundCurrency(ReadDecimal(item, LicensingTotalUsdField) ?? 0m),
            UnidadUsd = RoundCurrency(ReadDecimal(item, LicensingUnitUsdField) ?? 0m),
            Cantidad = RoundCurrency(ReadDecimal(item, LicensingQuantityField) ?? 0m),
            Trm = RoundCurrency(ReadDecimal(item, LicensingTrmField) ?? 0m),
            PesosTotal = RoundCurrency(ReadDecimal(item, LicensingTotalCopField) ?? 0m),
            ContractTypeValue = contractType,
            ContractTypeLabel = FirstNonEmpty(
                ReadString(item, $"{LicensingContractTypeField}{FormattedValueAnnotationSuffix}"),
                ResolveLicensingContractTypeLabel(contractType),
                "Sin tipo"),
            HasAccountLookup = metadata.AccountFieldIsLookup && !string.IsNullOrWhiteSpace(ReadString(item, accountLookupProperty)),
            HasProductLookup = metadata.ProductFieldIsLookup && !string.IsNullOrWhiteSpace(ReadString(item, productLookupProperty))
        };
    }

    private static List<LicenciamientoPreviewRowDto> ParseLicensingWorkbook(string fileName, byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("El archivo no contiene hojas.");

        var firstRow = worksheet.FirstRowUsed()
            ?? throw new InvalidOperationException("La hoja del Excel esta vacia.");
        var lastRow = worksheet.LastRowUsed()
            ?? throw new InvalidOperationException("La hoja del Excel esta vacia.");

        var headerMap = BuildLicensingHeaderMap(firstRow);
        var columns = ResolveLicensingExcelColumns(headerMap);
        var rows = new List<LicenciamientoPreviewRowDto>();
        for (var rowNumber = firstRow.RowNumber() + 1; rowNumber <= lastRow.RowNumber(); rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            if (IsLicensingExcelRowEmpty(row))
                continue;

            var previewRow = ParseLicensingExcelRow(row, rowNumber, columns);
            rows.Add(previewRow);
        }

        return rows;
    }

    private static Dictionary<string, int> BuildLicensingHeaderMap(IXLRow headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var key = NormalizeLicensingHeader(ReadLicensingExcelText(cell));
            if (string.IsNullOrWhiteSpace(key) || map.ContainsKey(key))
                continue;

            map[key] = cell.Address.ColumnNumber;
        }

        return map;
    }

    private static LicensingExcelColumns ResolveLicensingExcelColumns(IReadOnlyDictionary<string, int> headerMap)
    {
        var missing = new List<string>();
        int Required(string header)
        {
            var key = NormalizeLicensingHeader(header);
            if (headerMap.TryGetValue(key, out var columnNumber))
                return columnNumber;

            missing.Add(header);
            return 0;
        }

        var columns = new LicensingExcelColumns
        {
            CompanyAccountId = Required("CompanyAccountid"),
            NombreCliente = Required("End Customer Company"),
            Vendor = Required("Vendor"),
            ProductDescription = Required("PriceableItem description"),
            Days = Required("Days Billed"),
            BillingInterval = Required("Billing Interval"),
            TotalUsd = Required("Costs"),
            UnitUsd = Required("Costs of Unit"),
            Quantity = Required("UDRC Value")
        };

        if (missing.Count > 0)
            throw new InvalidOperationException($"El Excel no contiene las columnas requeridas: {string.Join(", ", missing)}.");

        return columns;
    }

    private static LicenciamientoPreviewRowDto ParseLicensingExcelRow(
        IXLRow row,
        int rowNumber,
        LicensingExcelColumns columns)
    {
        var result = new LicenciamientoPreviewRowDto
        {
            SourceRowNumber = rowNumber,
            CompanyAccountId = ReadLicensingExcelText(row.Cell(columns.CompanyAccountId)),
            NombreCliente = ReadLicensingExcelText(row.Cell(columns.NombreCliente)),
            Vendor = ReadLicensingExcelText(row.Cell(columns.Vendor)),
            ProductDescription = ReadLicensingExcelText(row.Cell(columns.ProductDescription)),
            BillingInterval = ReadLicensingExcelText(row.Cell(columns.BillingInterval)),
            ContractTypeValue = LicensingContractMonthly,
            ContractTypeLabel = "Monthly"
        };

        if (!TryReadLicensingExcelInt(row.Cell(columns.Days), out var days))
            result.Errors.Add("Days Billed no es un numero valido.");
        result.Days = days;

        if (!TryReadLicensingExcelDecimal(row.Cell(columns.TotalUsd), out var totalUsd))
            result.Errors.Add("Costs no es un numero valido.");
        result.ValorTotalUsd = RoundCurrency(totalUsd);

        if (!TryReadLicensingExcelDecimal(row.Cell(columns.UnitUsd), out var unitUsd))
            result.Errors.Add("Costs of Unit no es un numero valido.");
        result.UnidadUsd = RoundCurrency(unitUsd);

        if (!TryReadLicensingExcelDecimal(row.Cell(columns.Quantity), out var quantity))
            result.Errors.Add("UDRC Value no es un numero valido.");
        result.Cantidad = RoundCurrency(quantity);
        result.RequiresBreakdown = IsLicensingBreakdownProduct(result.ProductDescription);

        if (string.IsNullOrWhiteSpace(result.BillingInterval)
            || !TryResolveLicensingInvoiceDate(result.BillingInterval, out var invoiceDate))
        {
            result.Errors.Add("Billing Interval no tiene un formato valido.");
        }
        else
        {
            result.FacturaValue = invoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            result.FacturaDisplay = invoiceDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        if (string.IsNullOrWhiteSpace(result.NombreCliente))
            result.Errors.Add("End Customer Company esta vacio.");
        if (string.IsNullOrWhiteSpace(result.Vendor))
            result.Errors.Add("Vendor esta vacio.");

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    private async Task ResolvePreviewLookupsAsync(
        LicensingMetadata metadata,
        IReadOnlyList<LicenciamientoPreviewRowDto> rows,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        foreach (var row in rows)
        {
            row.CompanyAccountLookupRequired = metadata.AccountFieldIsLookup;
            row.ProductLookupRequired = metadata.ProductFieldIsLookup;
        }

        var accountLookupResult = metadata.AccountFieldIsLookup
            ? await ResolveLicensingLookupMapAsync(
                rows.Select(static row => row.CompanyAccountId),
                metadata.AccountMetadata,
                metadata.AccountSearchFields,
                metadata.AccountAttributeTypes,
                user,
                ct)
            : LicensingLookupMapResult.Empty;
        var productLookupResult = metadata.ProductFieldIsLookup
            ? await ResolveLicensingLookupMapAsync(
                rows.Select(static row => row.ProductDescription),
                metadata.ProductMetadata,
                metadata.ProductSearchFields,
                metadata.ProductAttributeTypes,
                user,
                ct)
            : LicensingLookupMapResult.Empty;

        foreach (var row in rows)
        {
            var accountKey = NormalizeLicensingLookupKey(row.CompanyAccountId);
            if (metadata.AccountFieldIsLookup
                && accountLookupResult.Items.TryGetValue(accountKey, out var account))
            {
                row.CompanyAccountLookupId = account.Id;
                row.CompanyAccountLookupLabel = account.Label;
                row.CompanyAccountLookupFound = true;
            }
            else if (metadata.AccountFieldIsLookup
                && !string.IsNullOrWhiteSpace(accountKey)
                && accountLookupResult.FailureReasons.TryGetValue(accountKey, out var accountFailureReason))
            {
                row.CompanyAccountLookupFailureReason = accountFailureReason;
            }

            var productKey = NormalizeLicensingLookupKey(row.ProductDescription);
            if (metadata.ProductFieldIsLookup
                && productLookupResult.Items.TryGetValue(productKey, out var product))
            {
                row.ProductLookupId = product.Id;
                row.ProductLookupLabel = product.Label;
                row.ProductLookupFound = true;
            }
            else if (metadata.ProductFieldIsLookup
                && !string.IsNullOrWhiteSpace(productKey)
                && productLookupResult.FailureReasons.TryGetValue(productKey, out var productFailureReason))
            {
                row.ProductLookupFailureReason = productFailureReason;
            }
        }
    }

    private static void ApplyPreviewLookupWarnings(
        LicensingMetadata metadata,
        IEnumerable<LicenciamientoPreviewRowDto> rows)
    {
        foreach (var row in rows)
        {
            if (metadata.AccountFieldIsLookup
                && !string.IsNullOrWhiteSpace(row.CompanyAccountId)
                && !row.CompanyAccountLookupFound)
            {
                row.Warnings.Add(FirstNonEmpty(
                    row.CompanyAccountLookupFailureReason,
                    $"CompanyAccountid no se encontro en {LicensingAccountLookupTargetLogicalName}.{LicensingAccountLookupTargetFallbackPrimaryNameField}."));
            }

            if (metadata.ProductFieldIsLookup
                && !string.IsNullOrWhiteSpace(row.ProductDescription)
                && !row.ProductLookupFound)
            {
                var reason = FirstNonEmpty(
                    row.ProductLookupFailureReason,
                    "PriceableItem description no se encontro en el lookup.");
                row.Warnings.Add($"{reason} Selecciona un producto en la vista previa o esta fila se omitira al procesar.");
            }

            if (row.RequiresBreakdown && !row.BreakdownGenerated)
            {
                row.Warnings.Add("Este producto requiere desglose antes de procesar.");
            }
        }
    }

    private async Task<LicensingLookupMapResult> ResolveLicensingLookupMapAsync(
        IEnumerable<string> rawValues,
        RhEntityMetadata targetMetadata,
        IReadOnlyList<string> searchFields,
        IReadOnlyDictionary<string, string> attributeTypes,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var result = new Dictionary<string, LicensingLookupResolution>(StringComparer.OrdinalIgnoreCase);
        var failureReasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var values = rawValues
            .Select(NormalizeLicensingLookupKey)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var value in values)
        {
            var lookupResult = await FindLicensingLookupAsync(targetMetadata, searchFields, value, attributeTypes, user, ct);
            if (lookupResult.Lookup is not null)
            {
                result[value] = lookupResult.Lookup;
            }
            else
            {
                failureReasons[value] = lookupResult.FailureReason;
            }
        }

        return new LicensingLookupMapResult
        {
            Items = result,
            FailureReasons = failureReasons
        };
    }

    private async Task<LicensingLookupSearchResult> FindLicensingLookupAsync(
        RhEntityMetadata targetMetadata,
        IReadOnlyList<string> searchFields,
        string value,
        IReadOnlyDictionary<string, string> attributeTypes,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var attemptedFields = new List<string>();
        var errors = new List<string>();
        foreach (var searchField in searchFields.Where(static field => !string.IsNullOrWhiteSpace(field)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            attemptedFields.Add(searchField);
            try
            {
                var lookup = await TryFindLicensingLookupAsync(
                    targetMetadata,
                    searchField,
                    value,
                    attributeTypes,
                    user,
                    ct);

                if (lookup is not null)
                {
                    return new LicensingLookupSearchResult
                    {
                        Lookup = lookup
                    };
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException)
            {
                errors.Add($"{searchField}: {ex.Message}");
                _logger.LogWarning(
                    ex,
                    "No fue posible resolver lookup de licenciamiento en {EntitySetName}.{SearchField} para {Value}.",
                    targetMetadata.EntitySetName,
                    searchField,
                    value);
            }
        }

        return new LicensingLookupSearchResult
        {
            FailureReason = BuildLicensingLookupFailureReason(targetMetadata, attemptedFields, value, attributeTypes, errors)
        };
    }

    private async Task<LicensingLookupResolution?> TryFindLicensingLookupAsync(
        RhEntityMetadata targetMetadata,
        string searchField,
        string value,
        IReadOnlyDictionary<string, string> attributeTypes,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        attributeTypes.TryGetValue(searchField, out var searchFieldType);
        var select = string.Join(",",
            new[] { targetMetadata.PrimaryIdField, targetMetadata.PrimaryNameField, searchField }
                .Where(static field => !string.IsNullOrWhiteSpace(field))
                .Distinct(StringComparer.OrdinalIgnoreCase));

        foreach (var filter in BuildLicensingLookupFilters(searchField, value, searchFieldType))
        {
            var relativeUrl = $"/api/data/v9.2/{targetMetadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter.Expression)}&$top={filter.Top}";
            var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
            var item = ChooseLicensingLookupItem(items, searchField, value);
            if (item is null)
                continue;

            var id = ReadString(item.Value, targetMetadata.PrimaryIdField);
            if (string.IsNullOrWhiteSpace(id))
                continue;

            return new LicensingLookupResolution
            {
                Id = id,
                Label = FirstNonEmpty(
                    ReadString(item.Value, targetMetadata.PrimaryNameField),
                    ReadString(item.Value, searchField),
                    value)
            };
        }

        return null;
    }

    private async Task<IReadOnlyList<LicenciamientoLookupItemDto>> SearchLicensingLookupOptionsAsync(
        RhEntityMetadata targetMetadata,
        IReadOnlyList<string> searchFields,
        IReadOnlyDictionary<string, string> attributeTypes,
        string query,
        int top,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var results = new List<LicenciamientoLookupItemDto>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var searchField in searchFields.Where(static field => !string.IsNullOrWhiteSpace(field)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (results.Count >= top)
                break;

            attributeTypes.TryGetValue(searchField, out var searchFieldType);
            var select = string.Join(",",
                new[] { targetMetadata.PrimaryIdField, targetMetadata.PrimaryNameField, searchField }
                    .Where(static field => !string.IsNullOrWhiteSpace(field))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            var filter = BuildLicensingLookupSearchExpression(searchField, query, searchFieldType);
            if (string.IsNullOrWhiteSpace(filter))
                continue;

            var remaining = Math.Max(top - results.Count, 1);
            var relativeUrl = $"/api/data/v9.2/{targetMetadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top={remaining}";
            IReadOnlyList<JsonElement> items;
            try
            {
                items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException)
            {
                _logger.LogWarning(
                    ex,
                    "No fue posible buscar opciones de licenciamiento en {EntitySetName}.{SearchField} para {Query}.",
                    targetMetadata.EntitySetName,
                    searchField,
                    query);
                continue;
            }

            foreach (var item in items)
            {
                var id = ReadString(item, targetMetadata.PrimaryIdField).Trim();
                if (string.IsNullOrWhiteSpace(id) || !seenIds.Add(id))
                    continue;

                var matchedValue = ReadString(item, searchField).Trim();
                results.Add(new LicenciamientoLookupItemDto
                {
                    Id = id,
                    Label = FirstNonEmpty(
                        ReadString(item, targetMetadata.PrimaryNameField).Trim(),
                        matchedValue,
                        query),
                    SearchField = searchField,
                    MatchedValue = matchedValue
                });

                if (results.Count >= top)
                    break;
            }
        }

        return results;
    }

    private static Dictionary<string, object?> BuildLicensingCreatePayload(
        LicensingMetadata metadata,
        LicenciamientoPreviewRowDto row)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [LicensingCustomerNameField] = ConvertLicensingPayloadValue(metadata, LicensingCustomerNameField, row.NombreCliente),
            [LicensingVendorField] = ConvertLicensingPayloadValue(metadata, LicensingVendorField, row.Vendor),
            [LicensingDaysField] = ConvertLicensingPayloadValue(metadata, LicensingDaysField, row.Days),
            [LicensingBillingIntervalField] = ConvertLicensingPayloadValue(metadata, LicensingBillingIntervalField, row.BillingInterval),
            [LicensingInvoiceDateField] = ConvertLicensingPayloadValue(metadata, LicensingInvoiceDateField, row.FacturaValue),
            [LicensingTotalUsdField] = ConvertLicensingPayloadValue(metadata, LicensingTotalUsdField, row.ValorTotalUsd),
            [LicensingUnitUsdField] = ConvertLicensingPayloadValue(metadata, LicensingUnitUsdField, row.UnidadUsd),
            [LicensingQuantityField] = ConvertLicensingPayloadValue(metadata, LicensingQuantityField, row.Cantidad),
            [LicensingContractTypeField] = ConvertLicensingPayloadValue(metadata, LicensingContractTypeField, NormalizeLicensingContractTypeValue(row.ContractTypeValue))
        };

        if (metadata.AccountFieldIsLookup)
        {
            if (!string.IsNullOrWhiteSpace(row.CompanyAccountLookupId))
            {
                payload[$"{metadata.AccountNavigationProperty}@odata.bind"] =
                    $"/{metadata.AccountMetadata.EntitySetName}({NormalizeGuid(row.CompanyAccountLookupId, nameof(row.CompanyAccountLookupId))})";
            }
        }
        else
        {
            payload[LicensingAccountLookupField] = ConvertLicensingPayloadValue(metadata, LicensingAccountLookupField, row.CompanyAccountId);
        }

        if (metadata.ProductFieldIsLookup)
        {
            if (!string.IsNullOrWhiteSpace(row.ProductLookupId))
            {
                payload[$"{metadata.ProductNavigationProperty}@odata.bind"] =
                    $"/{metadata.ProductMetadata.EntitySetName}({NormalizeGuid(row.ProductLookupId, nameof(row.ProductLookupId))})";
            }
        }
        else
        {
            payload[LicensingProductLookupField] = ConvertLicensingPayloadValue(metadata, LicensingProductLookupField, row.ProductDescription);
        }

        if ((!string.Equals(metadata.BaseMetadata.PrimaryNameField, LicensingAccountLookupField, StringComparison.OrdinalIgnoreCase)
                || !metadata.AccountFieldIsLookup)
            && !payload.ContainsKey(metadata.BaseMetadata.PrimaryNameField))
        {
            payload[metadata.BaseMetadata.PrimaryNameField] = ConvertLicensingPayloadValue(
                metadata,
                metadata.BaseMetadata.PrimaryNameField,
                BuildLicensingPrimaryName(row));
        }

        return payload;
    }

    private static bool ShouldSkipLicensingImportRow(LicensingMetadata metadata, LicenciamientoPreviewRowDto row) =>
        metadata.ProductFieldIsLookup && string.IsNullOrWhiteSpace(row.ProductLookupId);

    private static string BuildLicensingImportMessage(int created, int skipped)
    {
        var createdMessage = created == 1
            ? "Se cargo 1 fila en Dataverse."
            : $"Se cargaron {created} filas en Dataverse.";

        if (skipped <= 0)
            return createdMessage;

        var skippedMessage = skipped == 1
            ? "Se omitio 1 fila sin lookup de producto."
            : $"Se omitieron {skipped} filas sin lookup de producto.";

        return $"{createdMessage} {skippedMessage}";
    }

    private static LicenciamientoPreviewRowDto NormalizeLicensingImportRow(LicenciamientoPreviewRowDto row)
    {
        row.Warnings ??= new List<string>();
        row.Errors ??= new List<string>();
        row.CompanyAccountId = (row.CompanyAccountId ?? "").Trim();
        row.CompanyAccountLookupId = NormalizeOptionalGuid(row.CompanyAccountLookupId);
        row.CompanyAccountLookupLabel = (row.CompanyAccountLookupLabel ?? "").Trim();
        row.CompanyAccountLookupFailureReason = (row.CompanyAccountLookupFailureReason ?? "").Trim();
        row.NombreCliente = (row.NombreCliente ?? "").Trim();
        row.Vendor = (row.Vendor ?? "").Trim();
        row.ProductDescription = (row.ProductDescription ?? "").Trim();
        row.ProductLookupId = NormalizeOptionalGuid(row.ProductLookupId);
        row.ProductLookupLabel = (row.ProductLookupLabel ?? "").Trim();
        row.ProductLookupFailureReason = (row.ProductLookupFailureReason ?? "").Trim();
        row.BillingInterval = (row.BillingInterval ?? "").Trim();
        row.FacturaValue = (row.FacturaValue ?? "").Trim();
        row.FacturaDisplay = (row.FacturaDisplay ?? "").Trim();
        row.ContractTypeValue = NormalizeLicensingContractTypeValue(row.ContractTypeValue);
        row.ContractTypeLabel = ResolveLicensingContractTypeLabel(row.ContractTypeValue);
        row.RequiresBreakdown = row.RequiresBreakdown && !row.BreakdownGenerated;

        if (!TryParseDateOnly(row.FacturaValue, out _))
            throw new InvalidOperationException($"La fila {row.SourceRowNumber} no tiene una fecha de factura valida.");

        return row;
    }

    private static object? ConvertLicensingPayloadValue(
        LicensingMetadata metadata,
        string fieldName,
        object? value)
    {
        if (value is null)
            return null;

        metadata.ConsumptionAttributeTypes.TryGetValue(fieldName, out var attributeType);
        if (string.IsNullOrWhiteSpace(attributeType))
            return value;

        if (IsLicensingTextAttribute(attributeType))
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

        if (IsLicensingIntegerAttribute(attributeType))
        {
            if (value is int intValue)
                return intValue;

            if (value is long longValue)
                return checked((int)longValue);

            if (value is decimal decimalValue)
                return (int)Math.Round(decimalValue, MidpointRounding.AwayFromZero);

            if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                return parsedInt;

            if (TryParseLicensingDecimal(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsedDecimal))
                return (int)Math.Round(parsedDecimal, MidpointRounding.AwayFromZero);

            return value;
        }

        if (string.Equals(attributeType, "BigInt", StringComparison.OrdinalIgnoreCase))
        {
            if (value is long longValue)
                return longValue;

            if (value is int intValue)
                return (long)intValue;

            if (long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong))
                return parsedLong;

            return value;
        }

        if (IsLicensingDecimalAttribute(attributeType))
        {
            if (value is decimal decimalValue)
                return decimalValue;

            if (value is int intValue)
                return (decimal)intValue;

            if (value is long longValue)
                return (decimal)longValue;

            if (TryParseLicensingDecimal(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsedDecimal))
                return parsedDecimal;

            return value;
        }

        if (string.Equals(attributeType, "DateTime", StringComparison.OrdinalIgnoreCase))
        {
            if (value is DateOnly dateOnly)
                return dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var raw = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            if (TryParseDateOnly(raw, out var parsedDate))
                return parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            return raw;
        }

        if (string.Equals(attributeType, "Boolean", StringComparison.OrdinalIgnoreCase))
        {
            if (value is bool boolValue)
                return boolValue;

            var raw = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (bool.TryParse(raw, out var parsedBool))
                return parsedBool;

            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                return parsedInt != 0;
        }

        return value;
    }

    private static int NormalizeLicensingContractTypeValue(int value)
    {
        var option = ResolveLicensingContractTypeOption(value);
        if (option is null)
            return LicensingContractMonthly;

        return option.Value;
    }

    private static bool IsLicensingBreakdownProduct(string? productDescription) =>
        string.Equals(
            (productDescription ?? "").Trim(),
            LicensingManualBreakdownProductName,
            StringComparison.OrdinalIgnoreCase);

    private static string BuildLicensingPrimaryName(LicenciamientoPreviewRowDto row)
    {
        var parts = new[]
        {
            FirstNonEmpty(row.CompanyAccountId, row.NombreCliente, "Consumo"),
            row.BillingInterval,
            row.Vendor
        }
        .Where(static value => !string.IsNullOrWhiteSpace(value));

        return string.Join(" - ", parts);
    }

    private static string BuildLookupValueProperty(string logicalName) => $"_{logicalName}_value";

    private static IReadOnlyList<LicensingLookupFilter> BuildLicensingLookupFilters(
        string searchField,
        string value,
        string? attributeType)
    {
        var filters = new List<LicensingLookupFilter>();
        if (IsLicensingIntegerAttribute(attributeType)
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue))
        {
            filters.Add(new LicensingLookupFilter($"{searchField} eq {integerValue}", 1));
            return filters;
        }

        if (IsLicensingDecimalAttribute(attributeType)
            && TryParseLicensingDecimal(value, out var decimalValue))
        {
            filters.Add(new LicensingLookupFilter($"{searchField} eq {decimalValue.ToString(CultureInfo.InvariantCulture)}", 1));
            return filters;
        }

        filters.Add(new LicensingLookupFilter($"{searchField} eq '{EscapeOdataLiteral(value)}'", 1));

        if (IsLicensingTextAttribute(attributeType))
        {
            foreach (var token in BuildLicensingLookupContainsTokens(value))
            {
                filters.Add(new LicensingLookupFilter($"contains({searchField},'{EscapeOdataLiteral(token)}')", 10));
            }
        }

        return filters
            .DistinctBy(static item => item.Expression, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildLicensingLookupSearchExpression(
        string searchField,
        string value,
        string? attributeType)
    {
        if (IsLicensingIntegerAttribute(attributeType)
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue))
        {
            return $"{searchField} eq {integerValue}";
        }

        if (IsLicensingDecimalAttribute(attributeType)
            && TryParseLicensingDecimal(value, out var decimalValue))
        {
            return $"{searchField} eq {decimalValue.ToString(CultureInfo.InvariantCulture)}";
        }

        if (!IsLicensingTextAttribute(attributeType))
            return "";

        return $"contains({searchField},'{EscapeOdataLiteral(value)}')";
    }

    private static string BuildLicensingLookupFailureReason(
        RhEntityMetadata targetMetadata,
        IReadOnlyList<string> attemptedFields,
        string value,
        IReadOnlyDictionary<string, string> attributeTypes,
        IReadOnlyList<string> errors)
    {
        var tableName = FirstNonEmpty(targetMetadata.LogicalName, targetMetadata.EntitySetName, "la tabla de lookup");
        if (attemptedFields.Count == 0)
            return $"No hay campos configurados para buscar \"{value}\" en {tableName}.";

        var fieldList = string.Join(", ", attemptedFields.Select(field =>
        {
            attributeTypes.TryGetValue(field, out var attributeType);
            return string.IsNullOrWhiteSpace(attributeType) ? field : $"{field} ({attributeType})";
        }));
        var reason = $"No se encontraron registros en {tableName} para \"{value}\". Se busco en: {fieldList}.";

        if (errors.Count == 0)
            return reason;

        return $"{reason} Errores de consulta: {string.Join(" | ", errors.Take(3))}.";
    }

    private static JsonElement? ChooseLicensingLookupItem(
        IReadOnlyList<JsonElement> items,
        string searchField,
        string requestedValue)
    {
        if (items.Count == 0)
            return null;

        var requestedKey = NormalizeLicensingComparable(requestedValue);
        foreach (var item in items)
        {
            if (string.Equals(
                    NormalizeLicensingComparable(ReadString(item, searchField)),
                    requestedKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return items[0];
    }

    private static IEnumerable<string> BuildLicensingLookupContainsTokens(string value)
    {
        var normalized = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            yield break;

        yield return normalized;

        foreach (var maxLength in new[] { 120, 80, 50 })
        {
            if (normalized.Length <= maxLength)
                continue;

            var token = normalized[..maxLength].Trim();
            if (!string.IsNullOrWhiteSpace(token))
                yield return token;
        }
    }

    private static string NormalizeLicensingComparable(string? value)
    {
        var text = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var builder = new StringBuilder(text.Length);
        var previousWasSpace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasSpace)
                    builder.Append(' ');

                previousWasSpace = true;
                continue;
            }

            builder.Append(char.ToUpperInvariant(ch));
            previousWasSpace = false;
        }

        return builder.ToString();
    }

    private static bool IsLicensingIntegerAttribute(string? attributeType) =>
        string.Equals(attributeType, "Integer", StringComparison.OrdinalIgnoreCase)
        || string.Equals(attributeType, "Picklist", StringComparison.OrdinalIgnoreCase)
        || string.Equals(attributeType, "State", StringComparison.OrdinalIgnoreCase)
        || string.Equals(attributeType, "Status", StringComparison.OrdinalIgnoreCase);

    private static bool IsLicensingDecimalAttribute(string? attributeType) =>
        string.Equals(attributeType, "Decimal", StringComparison.OrdinalIgnoreCase)
        || string.Equals(attributeType, "Double", StringComparison.OrdinalIgnoreCase)
        || string.Equals(attributeType, "Money", StringComparison.OrdinalIgnoreCase);

    private static bool IsLicensingTextAttribute(string? attributeType) =>
        string.IsNullOrWhiteSpace(attributeType)
        || string.Equals(attributeType, "String", StringComparison.OrdinalIgnoreCase)
        || string.Equals(attributeType, "Memo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(attributeType, "EntityName", StringComparison.OrdinalIgnoreCase);

    private static bool TryResolveLicensingInvoiceDate(string billingInterval, out DateOnly invoiceDate)
    {
        invoiceDate = default;
        var normalized = (billingInterval ?? "").Trim()
            .Replace('.', '/')
            .Replace('-', '/');
        var parts = normalized.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return false;

        var first = parts[0];
        var second = parts[1];
        int month;
        int year;
        if (first.Length == 4)
        {
            if (!int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out year)
                || !int.TryParse(second, NumberStyles.Integer, CultureInfo.InvariantCulture, out month))
                return false;
        }
        else
        {
            if (!int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out month)
                || !int.TryParse(second, NumberStyles.Integer, CultureInfo.InvariantCulture, out year))
                return false;
        }

        if (month < 1 || month > 12 || year < 1900 || year > 2100)
            return false;

        var nextMonth = new DateOnly(year, month, 1).AddMonths(1);
        invoiceDate = new DateOnly(nextMonth.Year, nextMonth.Month, 5);
        return true;
    }

    private static bool IsLicensingExcelRowEmpty(IXLRow row) =>
        !row.CellsUsed().Any(cell => !string.IsNullOrWhiteSpace(ReadLicensingExcelText(cell)));

    private static string ReadLicensingExcelText(IXLCell cell)
    {
        if (cell.IsEmpty())
            return "";

        var raw = cell.GetString();
        return (raw ?? "").Trim();
    }

    private static bool TryReadLicensingExcelInt(IXLCell cell, out int value)
    {
        if (cell.TryGetValue<int>(out value))
            return true;

        if (TryReadLicensingExcelDecimal(cell, out var decimalValue))
        {
            value = (int)Math.Round(decimalValue, MidpointRounding.AwayFromZero);
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryReadLicensingExcelDecimal(IXLCell cell, out decimal value)
    {
        if (cell.TryGetValue<decimal>(out value))
            return true;

        return TryParseLicensingDecimal(ReadLicensingExcelText(cell), out value);
    }

    private static bool TryParseLicensingDecimal(string? rawValue, out decimal value)
    {
        value = 0m;
        var raw = (rawValue ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        raw = raw
            .Replace("USD", "", StringComparison.OrdinalIgnoreCase)
            .Replace("$", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            return true;

        if (decimal.TryParse(raw, NumberStyles.Number, LicensingCulture, out value))
            return true;

        var normalized = NormalizeLicensingDecimalText(raw);
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static string NormalizeLicensingDecimalText(string value)
    {
        var raw = value.Trim();
        var lastDot = raw.LastIndexOf('.');
        var lastComma = raw.LastIndexOf(',');
        if (lastDot >= 0 && lastComma >= 0)
        {
            if (lastComma > lastDot)
                return raw.Replace(".", "").Replace(',', '.');

            return raw.Replace(",", "");
        }

        if (lastComma >= 0 && lastDot < 0)
            return raw.Replace(',', '.');

        return raw;
    }

    private static string NormalizeLicensingHeader(string value) =>
        new((value ?? "")
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    private static string NormalizeLicensingLookupKey(string? value) =>
        (value ?? "").Trim();

    private sealed class LicensingMetadata
    {
        public RhEntityMetadata BaseMetadata { get; init; } = new();
        public RhEntityMetadata AccountMetadata { get; init; } = new();
        public RhEntityMetadata ProductMetadata { get; init; } = new();
        public Dictionary<string, string> ConsumptionAttributeTypes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> AccountAttributeTypes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> ProductAttributeTypes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public bool AccountFieldIsLookup { get; init; }
        public bool ProductFieldIsLookup { get; init; }
        public string AccountNavigationProperty { get; init; } = "";
        public string ProductNavigationProperty { get; init; } = "";
        public IReadOnlyList<string> AccountSearchFields { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> ProductSearchFields { get; init; } = Array.Empty<string>();
    }

    private sealed class LicensingLookupResolution
    {
        public string Id { get; init; } = "";
        public string Label { get; init; } = "";
    }

    private sealed class LicensingLookupMapResult
    {
        public static LicensingLookupMapResult Empty { get; } = new();

        public Dictionary<string, LicensingLookupResolution> Items { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> FailureReasons { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class LicensingLookupSearchResult
    {
        public LicensingLookupResolution? Lookup { get; init; }
        public string FailureReason { get; init; } = "";
    }

    private sealed record LicensingLookupFilter(string Expression, int Top);

    private sealed class LicensingExcelColumns
    {
        public int CompanyAccountId { get; init; }
        public int NombreCliente { get; init; }
        public int Vendor { get; init; }
        public int ProductDescription { get; init; }
        public int Days { get; init; }
        public int BillingInterval { get; init; }
        public int TotalUsd { get; init; }
        public int UnitUsd { get; init; }
        public int Quantity { get; init; }
    }
}
