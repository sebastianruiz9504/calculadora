using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.Reconciliation;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Conciliacion)]
public sealed class ConciliacionController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private readonly IDataverseService _dataverse;
    private readonly IFinancialReconciliationService _financialReconciliation;
    private readonly ISiigoService _siigo;

    public ConciliacionController(
        IDataverseService dataverse,
        IFinancialReconciliationService financialReconciliation,
        ISiigoService siigo)
    {
        _dataverse = dataverse;
        _financialReconciliation = financialReconciliation;
        _siigo = siigo;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        var (resolvedYear, resolvedMonth) = ResolvePeriod(year, month);
        var model = new ConciliacionPageViewModel
        {
            CurrentUser = await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo(),
            Board = await _dataverse.GetConciliacionBoardAsync(resolvedYear, resolvedMonth, ct)
        };

        return View(model);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SyncHealth([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        try
        {
            var (resolvedYear, resolvedMonth) = ResolvePeriod(year, month);
            var snapshot = await _financialReconciliation.BuildSnapshotAsync(resolvedYear, resolvedMonth, ct);
            return Ok(BuildSyncHealth(snapshot));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible consultar la salud de sincronizacion.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UpdateClientPaymentStatus(
        [FromBody] ConciliacionClientPaymentStatusRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar el cruce a actualizar."));

        try
        {
            return Ok(await _dataverse.UpdateConciliacionClientPaymentStatusAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible actualizar el cruce.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ValidateClientPaymentPreflight(
        [FromBody] ConciliacionClientPaymentStatusRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el cruce a validar."));

        try
        {
            return Ok(await _dataverse.ValidateConciliacionClientPaymentPreflightAsync(request.RecordId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible validar el borrador pre-Siigo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SearchDataverseInvoices(
        [FromBody] ConciliacionInvoiceSearchRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Indica el texto o valor para buscar facturas."));

        try
        {
            return Ok(await _dataverse.SearchConciliacionDataverseInvoicesAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible buscar facturas en Dataverse.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> AssignClientPaymentInvoice(
        [FromBody] ConciliacionAssignInvoiceRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId) || string.IsNullOrWhiteSpace(request.InvoiceRecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el cruce y la factura a asignar."));

        try
        {
            return Ok(await _dataverse.AssignConciliacionClientPaymentInvoiceAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible asignar la factura.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> UpdateDianSupplierDocumentClassification(
        [FromBody] ConciliacionDianClassificationRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el documento DIAN a actualizar."));

        try
        {
            return Ok(await _dataverse.UpdateConciliacionDianSupplierDocumentClassificationAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar la clasificacion DIAN.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CreateDianSupplierInSiigo(
        [FromBody] ConciliacionDianSupplierDocumentRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el documento DIAN."));

        try
        {
            var row = await _dataverse.GetConciliacionDianSupplierDocumentAsync(request.RecordId, ct);
            var supplier = await EnsureDianSupplierInSiigoAsync(row, allowCreate: true, ct);
            var supplierLabel = FirstNonEmpty(supplier.Customer.DisplayName, supplier.Customer.Name, supplier.Customer.Identification);
            var message = supplier.Created
                ? $"Proveedor creado en Siigo: {supplierLabel}."
                : $"Proveedor encontrado en Siigo y asociado: {supplierLabel}.";

            return Ok(await _dataverse.MarkConciliacionDianSupplierAsync(
                request.RecordId,
                supplier.Customer.Id,
                supplierLabel,
                message,
                ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible crear/asociar el proveedor en Siigo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SimulateDianSupplierPurchaseSiigoSend(
        [FromBody] ConciliacionDianSupplierDocumentRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el documento DIAN a simular."));

        try
        {
            var prepared = await PrepareDianSupplierPurchaseForSiigoAsync(request.RecordId, createMissingSupplier: false, ct);
            return Ok(new ConciliacionDianActionResultDto
            {
                Message = prepared.CanSend
                    ? "Simulacion correcta. El payload de factura esta completo y no se envio nada a Siigo."
                    : "Simulacion con pendientes. Corrige los puntos indicados antes del envio real.",
                IsReadyForSiigo = prepared.CanSend,
                TargetEndpoint = $"DRY-RUN {prepared.TargetEndpoint}",
                PayloadJson = prepared.PayloadJson,
                Issues = prepared.Issues,
                Row = prepared.Row
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible simular la factura de compra Siigo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SendDianSupplierPurchaseToSiigo(
        [FromBody] ConciliacionDianSupplierDocumentRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el documento DIAN a enviar."));

        PreparedDianSupplierPurchase prepared;
        try
        {
            prepared = await PrepareDianSupplierPurchaseForSiigoAsync(request.RecordId, createMissingSupplier: true, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible preparar la factura de compra Siigo.", ex));
        }

        if (!prepared.CanSend || prepared.Payload is null)
        {
            return Ok(new ConciliacionDianActionResultDto
            {
                Message = "Envio real bloqueado. Corrige los pendientes visibles antes de enviar.",
                IsReadyForSiigo = false,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                Issues = prepared.Issues,
                Row = prepared.Row
            });
        }

        try
        {
            var siigoResult = await _siigo.CreatePurchaseAsync(
                prepared.Payload,
                BuildSiigoIdempotencyKey(request.RecordId),
                ct);
            var documentLabel = FirstNonEmpty(siigoResult.Name, siigoResult.Id);
            var message = string.IsNullOrWhiteSpace(documentLabel)
                ? "Factura de compra enviada a Siigo."
                : $"Factura de compra enviada a Siigo: {documentLabel}.";
            var dataverseResult = await _dataverse.MarkConciliacionDianSupplierDocumentSiigoResultAsync(
                request.RecordId,
                success: true,
                message: message,
                siigoId: siigoResult.Id,
                siigoName: siigoResult.Name,
                responseJson: siigoResult.RawJson,
                ct);

            dataverseResult.TargetEndpoint = prepared.TargetEndpoint;
            dataverseResult.PayloadJson = prepared.PayloadJson;
            dataverseResult.ResponseJson = siigoResult.RawJson;
            return Ok(dataverseResult);
        }
        catch (InvalidOperationException ex)
        {
            var message = BuildExceptionDetail(ex);
            var dataverseResult = await _dataverse.MarkConciliacionDianSupplierDocumentSiigoResultAsync(
                request.RecordId,
                success: false,
                message: "Siigo rechazo la factura de compra.",
                responseJson: message,
                ct: ct);
            dataverseResult.TargetEndpoint = prepared.TargetEndpoint;
            dataverseResult.PayloadJson = prepared.PayloadJson;
            dataverseResult.Issues = new[] { message };
            return Ok(dataverseResult);
        }
        catch (Exception ex)
        {
            var message = BuildExceptionDetail(ex);
            var dataverseResult = await _dataverse.MarkConciliacionDianSupplierDocumentSiigoResultAsync(
                request.RecordId,
                success: false,
                message: "No fue posible completar el envio real a Siigo.",
                responseJson: message,
                ct: ct);
            dataverseResult.TargetEndpoint = prepared.TargetEndpoint;
            dataverseResult.PayloadJson = prepared.PayloadJson;
            dataverseResult.Issues = new[] { message };
            return StatusCode(StatusCodes.Status500InternalServerError, dataverseResult);
        }
    }

    private async Task<PreparedDianSupplierPurchase> PrepareDianSupplierPurchaseForSiigoAsync(
        string recordId,
        bool createMissingSupplier,
        CancellationToken ct)
    {
        var row = await _dataverse.GetConciliacionDianSupplierDocumentAsync(recordId, ct);
        var issues = ValidateDianSupplierPurchaseBase(row).ToList();
        var targetEndpoint = ResolveDianSupplierDocumentEndpoint(row);
        object? supplierPayload = null;

        SiigoCustomerLookupItemDto? supplier = null;
        if (issues.Count == 0 || issues.All(static issue => !issue.Contains("proveedor", StringComparison.OrdinalIgnoreCase)))
        {
            var supplierResult = await EnsureDianSupplierInSiigoAsync(row, createMissingSupplier, ct);
            supplier = supplierResult.Customer;
            if (supplierResult.Created || supplierResult.WouldCreate)
                supplierPayload = supplierResult.Payload;
            if (!supplierResult.ExistsInSiigo && !createMissingSupplier)
                issues.Add("El proveedor no existe aun en Siigo; el envio real lo creara antes de crear la factura.");

            if (createMissingSupplier && supplierResult.Created)
            {
                var supplierLabel = FirstNonEmpty(supplier.DisplayName, supplier.Name, supplier.Identification);
                await _dataverse.MarkConciliacionDianSupplierAsync(
                    row.RecordId,
                    supplier.Id,
                    supplierLabel,
                    $"Proveedor creado automaticamente antes de crear la factura: {supplierLabel}.",
                    ct);
                row = await _dataverse.GetConciliacionDianSupplierDocumentAsync(recordId, ct);
            }
        }

        var documentTypes = await _siigo.GetDocumentTypesAsync("FC", ct);
        var paymentTypes = await _siigo.GetPaymentTypesAsync("FC", ct);
        var taxes = await _siigo.GetTaxesAsync(ct);
        var purchaseDocument = ResolvePurchaseDocumentType(documentTypes);
        var paymentType = ResolveSupplierPurchasePaymentType(paymentTypes);
        var payloadIssues = new List<string>();
        var purchasePayload = BuildDianSupplierPurchasePayload(row, purchaseDocument, paymentType, taxes, payloadIssues);
        issues.AddRange(payloadIssues);

        var wrapperPayload = supplierPayload is null
            ? purchasePayload
            : new { supplier = supplierPayload, purchase = purchasePayload };
        var payloadJson = JsonSerializer.Serialize(wrapperPayload, new JsonSerializerOptions { WriteIndented = true });

        return new PreparedDianSupplierPurchase(
            Row: row,
            CanSend: issues.Count == 0,
            TargetEndpoint: targetEndpoint,
            Payload: issues.Count == 0 ? purchasePayload : null,
            PayloadJson: payloadJson,
            Issues: issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private async Task<SiigoSupplierEnsureResult> EnsureDianSupplierInSiigoAsync(
        ConciliacionDianSupplierInvoiceRowDto row,
        bool allowCreate,
        CancellationToken ct)
    {
        var identification = ExtractDigits(row.SupplierNit);
        if (identification.Length < 5)
            throw new InvalidOperationException("El documento DIAN no tiene un NIT/identificacion de proveedor valido.");

        var existing = await _siigo.SearchCustomersAsync(identification, top: 10, ct);
        var exact = existing.FirstOrDefault(customer =>
            customer.Active
            && string.Equals(ExtractDigits(customer.Identification), identification, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return new SiigoSupplierEnsureResult(exact, ExistsInSiigo: true, Created: false, WouldCreate: false, Payload: null);
        }

        var payload = BuildSiigoSupplierPayload(row);
        if (!allowCreate)
        {
            return new SiigoSupplierEnsureResult(new SiigoCustomerLookupItemDto
            {
                Id = "",
                DisplayName = $"{row.SupplierName} - {identification}",
                Name = row.SupplierName,
                CommercialName = row.SupplierName,
                Identification = identification,
                Type = "Supplier",
                BranchOffice = 0,
                Active = true
            }, ExistsInSiigo: false, Created: false, WouldCreate: true, Payload: payload);
        }

        var created = await _siigo.CreateCustomerAsync(
            payload,
            BuildSiigoIdempotencyKey($"supplier-{identification}"),
            ct);
        return new SiigoSupplierEnsureResult(created, ExistsInSiigo: true, Created: true, WouldCreate: false, Payload: payload);
    }

    private static IReadOnlyList<string> ValidateDianSupplierPurchaseBase(ConciliacionDianSupplierInvoiceRowDto row)
    {
        var issues = new List<string>();
        if (!IsDianSupplierInvoice(row))
            issues.Add("El envio automatico inicial esta habilitado solo para facturas electronicas de proveedor. Los documentos soporte se conectan en el siguiente paso.");
        if (!string.IsNullOrWhiteSpace(row.SiigoDocumentId) || !string.IsNullOrWhiteSpace(row.SiigoDocumentName))
            issues.Add("Este documento ya tiene documento Siigo asociado.");
        if (string.IsNullOrWhiteSpace(row.SupplierNit) || string.IsNullOrWhiteSpace(row.SupplierName))
            issues.Add("Falta NIT o nombre del proveedor.");
        if (string.IsNullOrWhiteSpace(row.InvoiceNumber) || string.IsNullOrWhiteSpace(row.Folio))
            issues.Add("Falta numero de factura del proveedor.");
        if (string.IsNullOrWhiteSpace(row.EmissionDateValue))
            issues.Add("Falta fecha de emision.");
        if (row.TotalValue <= 0m)
            issues.Add("El total de la factura debe ser mayor a cero.");
        if (string.IsNullOrWhiteSpace(row.AccountCode))
            issues.Add("Falta cuenta gasto.");
        if (string.IsNullOrWhiteSpace(row.CategoryLabel) || row.CategoryLabel.Equals("Sin categoria", StringComparison.OrdinalIgnoreCase))
            issues.Add("Falta categoria.");
        if (row.BaseAmount <= 0m && row.TotalValue <= row.VatValue)
            issues.Add("No hay base valida para crear la linea de compra.");

        return issues;
    }

    private static object BuildDianSupplierPurchasePayload(
        ConciliacionDianSupplierInvoiceRowDto row,
        SiigoDocumentTypeLookupDto purchaseDocument,
        SiigoPaymentTypeLookupDto paymentType,
        IReadOnlyList<SiigoTaxLookupDto> taxes,
        List<string> issues)
    {
        var identification = ExtractDigits(row.SupplierNit);
        var providerInvoiceNumber = ExtractDigits(FirstNonEmpty(row.Folio, row.InvoiceNumber));
        var prefix = (row.Prefix ?? "").Trim();
        if (prefix.Length > 6)
            issues.Add("El prefijo de la factura supera 6 caracteres; Siigo puede rechazarlo.");
        if (string.IsNullOrWhiteSpace(providerInvoiceNumber))
            issues.Add("El consecutivo de la factura del proveedor debe tener numeros.");

        var emissionDate = DateOnly.TryParseExact(row.EmissionDateValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
            ? parsedDate
            : DateOnly.FromDateTime(DateTime.UtcNow);
        var baseAmount = row.BaseAmount > 0m
            ? row.BaseAmount
            : Math.Max(0m, row.TotalValue - row.VatValue);
        var item = new Dictionary<string, object?>
        {
            ["type"] = "Account",
            ["code"] = row.AccountCode.Trim(),
            ["description"] = TruncateControllerText($"{row.InvoiceNumber} {row.SupplierName}", 100),
            ["quantity"] = 1,
            ["price"] = RoundCurrency(baseAmount)
        };

        var itemTaxes = BuildDianSupplierPurchaseItemTaxes(row, baseAmount, taxes, issues);
        if (itemTaxes.Count > 0)
            item["taxes"] = itemTaxes;

        var payment = new Dictionary<string, object?>
        {
            ["id"] = paymentType.Id,
            ["value"] = row.TotalValue
        };
        if (paymentType.DueDate)
            payment["due_date"] = emissionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new Dictionary<string, object?>
        {
            ["document"] = new { id = purchaseDocument.Id },
            ["date"] = emissionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["supplier"] = new
            {
                identification,
                branch_office = 0
            },
            ["provider_invoice"] = new
            {
                prefix = prefix.Length > 6 ? prefix[..6] : prefix,
                number = providerInvoiceNumber
            },
            ["items"] = new[] { item },
            ["payments"] = new[] { payment },
            ["observations"] = TruncateControllerText(
                $"Importado desde DIAN. CUFE/CUDE: {row.Cufe}. Categoria: {row.CategoryLabel}. Cuenta: {row.AccountCode} {row.AccountName}.",
                500)
        };
    }

    private static IReadOnlyList<object> BuildDianSupplierPurchaseItemTaxes(
        ConciliacionDianSupplierInvoiceRowDto row,
        decimal baseAmount,
        IReadOnlyList<SiigoTaxLookupDto> taxes,
        List<string> issues)
    {
        if (row.VatValue <= 0m || baseAmount <= 0m)
            return Array.Empty<object>();

        var percent = Math.Round(row.VatValue / baseAmount * 100m, 2, MidpointRounding.AwayFromZero);
        var tax = taxes
            .Where(static item => item.Active && item.Type.Contains("IVA", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => Math.Abs(item.Percentage - percent))
            .FirstOrDefault();
        if (tax is null || Math.Abs(tax.Percentage - percent) > 0.5m)
        {
            issues.Add($"No encontre en Siigo un IVA activo cercano a {percent:N2}%.");
            return Array.Empty<object>();
        }

        return new object[] { new { id = tax.Id } };
    }

    private static object BuildSiigoSupplierPayload(ConciliacionDianSupplierInvoiceRowDto row)
    {
        var identification = ExtractDigits(row.SupplierNit);
        var isCompany = LooksLikeCompany(row.SupplierName);
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "Supplier",
            ["person_type"] = isCompany ? "Company" : "Person",
            ["id_type"] = isCompany ? "31" : "13",
            ["identification"] = identification,
            ["name"] = BuildSiigoSupplierName(row.SupplierName, isCompany),
            ["commercial_name"] = TruncateControllerText(row.SupplierName, 100),
            ["active"] = true,
            ["vat_responsible"] = isCompany,
            ["fiscal_responsibilities"] = new[] { new { code = "R-99-PN" } },
            ["address"] = new
            {
                address = "Sin direccion",
                city = new
                {
                    country_code = "Co",
                    state_code = "11",
                    city_code = "11001"
                }
            },
            ["phones"] = Array.Empty<object>(),
            ["contacts"] = Array.Empty<object>()
        };
        if (isCompany)
            payload["check_digit"] = CalculateColombianCheckDigit(identification).ToString(CultureInfo.InvariantCulture);

        return payload;
    }

    private static IReadOnlyList<string> BuildSiigoSupplierName(string supplierName, bool isCompany)
    {
        var cleanName = TruncateControllerText(FirstNonEmpty(supplierName, "Proveedor DIAN"), 100);
        if (isCompany)
            return new[] { cleanName };

        var parts = cleanName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length <= 1)
            return new[] { cleanName, "Proveedor" };

        var lastName = string.Join(" ", parts.Skip(Math.Max(1, parts.Length - 2)));
        var firstName = string.Join(" ", parts.Take(Math.Max(1, parts.Length - 2)));
        return new[] { firstName, lastName };
    }

    private static SiigoDocumentTypeLookupDto ResolvePurchaseDocumentType(IReadOnlyList<SiigoDocumentTypeLookupDto> documentTypes)
    {
        var active = documentTypes.Where(static item => item.Active).ToArray();
        return active.FirstOrDefault(static item =>
                item.Type.Equals("FC", StringComparison.OrdinalIgnoreCase)
                && item.Code.Equals("1", StringComparison.OrdinalIgnoreCase)
                && NormalizeSiigoDocumentTypeText($"{item.Name} {item.Description}").Contains("COMPRA", StringComparison.OrdinalIgnoreCase))
            ?? active.FirstOrDefault(static item => item.Type.Equals("FC", StringComparison.OrdinalIgnoreCase) && item.Code.Equals("1", StringComparison.OrdinalIgnoreCase))
            ?? active.FirstOrDefault(static item => item.Type.Equals("FC", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No encontre en Siigo un tipo de documento FC activo para crear compras.");
    }

    private static SiigoPaymentTypeLookupDto ResolveSupplierPurchasePaymentType(IReadOnlyList<SiigoPaymentTypeLookupDto> paymentTypes)
    {
        var active = paymentTypes.Where(static item => item.Active).ToArray();
        return active.FirstOrDefault(static item =>
                item.Name.Contains("Credito proveedores", StringComparison.OrdinalIgnoreCase)
                || item.Name.Contains("Credito proveedor", StringComparison.OrdinalIgnoreCase))
            ?? active.FirstOrDefault(static item => item.Id == 1726)
            ?? active.FirstOrDefault()
            ?? new SiigoPaymentTypeLookupDto
            {
                Id = 1726,
                Name = "Credito proveedores",
                Type = "Proveedor",
                Active = true,
                DueDate = true
            };
    }

    private static bool IsDianSupplierInvoice(ConciliacionDianSupplierInvoiceRowDto row)
    {
        var type = NormalizeSiigoDocumentTypeText(row.DocumentType);
        return type.Contains("FACTURA", StringComparison.OrdinalIgnoreCase)
            && !type.Contains("SOPORTE", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDianSupplierDocumentEndpoint(ConciliacionDianSupplierInvoiceRowDto row) =>
        IsDianSupplierInvoice(row) ? "/v1/purchases" : "/v1/purchase-support-documents";

    private static bool LooksLikeCompany(string name)
    {
        var normalized = NormalizeSiigoDocumentTypeText(name);
        return normalized.Contains(" S A S", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("SAS", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(" S A", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(" LTDA", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("LIMITADA", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("SUCURSAL", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("SOCIEDAD", StringComparison.OrdinalIgnoreCase);
    }

    private static int CalculateColombianCheckDigit(string identification)
    {
        var digits = ExtractDigits(identification);
        var weights = new[] { 71, 67, 59, 53, 47, 43, 41, 37, 29, 23, 19, 17, 13, 7, 3 };
        var offset = Math.Max(0, weights.Length - digits.Length);
        var sum = 0;
        for (var i = 0; i < digits.Length && i + offset < weights.Length; i++)
            sum += (digits[i] - '0') * weights[i + offset];

        var remainder = sum % 11;
        return remainder > 1 ? 11 - remainder : remainder;
    }

    private static string ExtractDigits(string value) =>
        Regex.Replace(value ?? "", @"\D+", "", RegexOptions.CultureInvariant);

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string TruncateControllerText(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value ?? "";

        return value[..maxLength];
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private sealed record SiigoSupplierEnsureResult(
        SiigoCustomerLookupItemDto Customer,
        bool ExistsInSiigo,
        bool Created,
        bool WouldCreate,
        object? Payload);

    private sealed record PreparedDianSupplierPurchase(
        ConciliacionDianSupplierInvoiceRowDto Row,
        bool CanSend,
        string TargetEndpoint,
        object? Payload,
        string PayloadJson,
        IReadOnlyList<string> Issues);

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SimulateClientPaymentSiigoSend(
        [FromBody] ConciliacionClientPaymentStatusRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el cruce a simular."));

        try
        {
            var prepared = await PrepareClientPaymentForSiigoSendAsync(request.RecordId, ct);
            var totals = CalculatePreparedJournalTotals(prepared.PayloadJson);

            return Ok(new ConciliacionSiigoDryRunResultDto
            {
                Message = prepared.CanSend
                    ? "Simulacion correcta. El payload real esta completo y aun no se envio nada a Siigo."
                    : prepared.Message,
                IsReadyForSiigo = prepared.CanSend,
                TargetEndpoint = string.IsNullOrWhiteSpace(prepared.TargetEndpoint)
                    ? "DRY-RUN /v1/journals"
                    : $"DRY-RUN {prepared.TargetEndpoint}",
                PayloadJson = prepared.PayloadJson,
                LineCount = totals.LineCount,
                DebitTotal = totals.Debit,
                CreditTotal = totals.Credit,
                Issues = prepared.Issues,
                Row = prepared.Row
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible simular el envio a Siigo.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SendClientPaymentToSiigo(
        [FromBody] ConciliacionClientPaymentStatusRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            return BadRequest(CreateErrorPayload("Debes indicar el cruce a enviar."));

        ConciliacionSiigoSendPreparedDto prepared;
        try
        {
            prepared = await PrepareClientPaymentForSiigoSendAsync(request.RecordId, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible preparar el envio real a Siigo.", ex));
        }

        if (!prepared.CanSend || prepared.Payload is null)
        {
            return Ok(new ConciliacionSiigoSendResultDto
            {
                Message = prepared.Message,
                IsSuccess = false,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                Issues = prepared.Issues,
                Row = prepared.Row
            });
        }

        try
        {
            var siigoResult = await _siigo.CreateJournalAsync(
                prepared.Payload,
                BuildSiigoIdempotencyKey(request.RecordId),
                ct);
            var documentLabel = string.IsNullOrWhiteSpace(siigoResult.Name)
                ? siigoResult.Id
                : siigoResult.Name;
            var message = string.IsNullOrWhiteSpace(documentLabel)
                ? "Comprobante de ingreso enviado a Siigo."
                : $"Comprobante de ingreso enviado a Siigo: {documentLabel}.";
            var dataverseResult = await _dataverse.MarkConciliacionClientPaymentSiigoSendResultAsync(
                request.RecordId,
                success: true,
                message: message,
                siigoId: siigoResult.Id,
                siigoName: siigoResult.Name,
                responseJson: siigoResult.RawJson,
                ct);

            return Ok(new ConciliacionSiigoSendResultDto
            {
                Message = dataverseResult.Message,
                IsSuccess = true,
                SiigoId = siigoResult.Id,
                SiigoName = siigoResult.Name,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                ResponseJson = siigoResult.RawJson,
                Row = dataverseResult.Row
            });
        }
        catch (InvalidOperationException ex)
        {
            var message = BuildExceptionDetail(ex);
            var dataverseResult = await _dataverse.MarkConciliacionClientPaymentSiigoSendResultAsync(
                request.RecordId,
                success: false,
                message: "Siigo rechazo el envio real.",
                responseJson: message,
                ct: ct);

            return Ok(new ConciliacionSiigoSendResultDto
            {
                Message = "Siigo rechazo el envio real. Revisa el detalle visible en la fila.",
                IsSuccess = false,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                Issues = new[] { message },
                Row = dataverseResult.Row
            });
        }
        catch (Exception ex)
        {
            var message = BuildExceptionDetail(ex);
            var dataverseResult = await _dataverse.MarkConciliacionClientPaymentSiigoSendResultAsync(
                request.RecordId,
                success: false,
                message: "No fue posible completar el envio real a Siigo.",
                responseJson: message,
                ct: ct);

            return StatusCode(StatusCodes.Status500InternalServerError, new ConciliacionSiigoSendResultDto
            {
                Message = "No fue posible completar el envio real a Siigo.",
                IsSuccess = false,
                TargetEndpoint = prepared.TargetEndpoint,
                PayloadJson = prepared.PayloadJson,
                Issues = new[] { message },
                Row = dataverseResult.Row
            });
        }
    }

    private static (int Year, int Month) ResolvePeriod(int? year, int? month)
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, MonthlyFinancialReconciliationHostedService.ResolveTimeZone("SA Pacific Standard Time"));
        var resolvedYear = year.GetValueOrDefault(now.Year);
        var resolvedMonth = month.GetValueOrDefault(now.Month);
        if (resolvedMonth is < 1 or > 12)
            resolvedMonth = now.Month;
        if (resolvedYear < 2020)
            resolvedYear = now.Year;

        return (resolvedYear, resolvedMonth);
    }

    private static ConciliacionSyncHealthDto BuildSyncHealth(FinancialReconciliationSnapshotResult snapshot)
    {
        var summary = snapshot.Summary;
        var items = new[]
        {
            BuildSyncHealthItem(
                key: "facturacion",
                label: "Facturacion",
                description: "Facturas de venta netas: facturas menos notas credito.",
                dataverseTotal: summary.DataverseBillingNet,
                siigoTotal: summary.SiigoBillingNet,
                differenceTotal: summary.BillingDifference,
                dataverseVat: summary.DataverseVatNet,
                siigoVat: summary.SiigoVatNet,
                vatDifference: summary.BillingVatDifference,
                dataverseCount: summary.DataverseBillingInvoiceCount,
                siigoCount: summary.SiigoBillingInvoiceCount,
                differenceRows: summary.BillingDifferenceCount,
                notes: $"NC Dataverse: {summary.DataverseBillingCreditNoteCount:N0}. NC Siigo: {summary.SiigoBillingCreditNoteCount:N0}."),
            BuildSyncHealthItem(
                key: "gastos",
                label: "Gastos",
                description: "Compras y gastos del periodo comparados por documento/proveedor/fecha/valor.",
                dataverseTotal: summary.PowerAppsExpenses,
                siigoTotal: summary.SiigoExpenses,
                differenceTotal: summary.PowerAppsExpenses - summary.SiigoExpenses,
                dataverseVat: summary.PowerAppsExpenseVat,
                siigoVat: summary.SiigoExpenseVat,
                vatDifference: summary.PowerAppsExpenseVat - summary.SiigoExpenseVat,
                dataverseCount: summary.PowerAppsExpenseCount,
                siigoCount: summary.SiigoExpenseCount,
                differenceRows: summary.ExpenseDifferenceCount,
                notes: "Dataverse corresponde a Power Apps/tabla de gastos.")
        };
        var totalDifferenceRows = items.Sum(static item => item.DifferenceRows);

        return new ConciliacionSyncHealthDto
        {
            Year = snapshot.Year,
            Month = snapshot.Month,
            PeriodLabel = snapshot.PeriodLabel,
            GeneratedAtDisplay = FormatSyncHealthDateTime(snapshot.GeneratedAt),
            StatusLabel = totalDifferenceRows == 0 ? "Sincronizado" : "Con diferencias",
            StatusTone = totalDifferenceRows == 0 ? "success" : "warning",
            TotalDifferenceRows = totalDifferenceRows,
            Items = items
        };
    }

    private static ConciliacionSyncHealthItemDto BuildSyncHealthItem(
        string key,
        string label,
        string description,
        decimal dataverseTotal,
        decimal siigoTotal,
        decimal differenceTotal,
        decimal dataverseVat,
        decimal siigoVat,
        decimal vatDifference,
        int dataverseCount,
        int siigoCount,
        int differenceRows,
        string notes)
    {
        var countDifference = dataverseCount - siigoCount;
        return new ConciliacionSyncHealthItemDto
        {
            Key = key,
            Label = label,
            Description = description,
            DataverseTotal = dataverseTotal,
            SiigoTotal = siigoTotal,
            DifferenceTotal = differenceTotal,
            DataverseVat = dataverseVat,
            SiigoVat = siigoVat,
            VatDifference = vatDifference,
            DataverseCount = dataverseCount,
            SiigoCount = siigoCount,
            CountDifference = countDifference,
            DifferenceRows = differenceRows,
            StatusLabel = differenceRows == 0 ? "Conciliado" : "Revisar",
            StatusTone = differenceRows == 0 ? "success" : "warning",
            Notes = notes
        };
    }

    private static string FormatSyncHealthDateTime(DateTimeOffset value)
    {
        var timeZone = MonthlyFinancialReconciliationHostedService.ResolveTimeZone("SA Pacific Standard Time");
        var local = TimeZoneInfo.ConvertTime(value, timeZone);
        return local.ToString("yyyy-MM-dd HH:mm");
    }

    private object CreateErrorPayload(string message, Exception? ex = null)
    {
        var detail = BuildExceptionDetail(ex);
        return new
        {
            message,
            detail = string.Equals(detail, message, StringComparison.Ordinal) ? "" : detail,
            traceId = HttpContext.TraceIdentifier
        };
    }

    private static string BuildExceptionDetail(Exception? ex)
    {
        if (ex is null)
            return "";

        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (string.IsNullOrWhiteSpace(current.Message))
                continue;

            var trimmedMessage = current.Message.Trim();
            if (!messages.Contains(trimmedMessage, StringComparer.OrdinalIgnoreCase))
                messages.Add(trimmedMessage);
        }

        return string.Join(" | ", messages);
    }

    private static string BuildSiigoIdempotencyKey(string recordId)
    {
        var compact = new string((recordId ?? "")
            .Where(char.IsLetterOrDigit)
            .ToArray());
        if (compact.Length == 0)
            compact = Guid.NewGuid().ToString("N");

        var key = $"CNCJ{compact}";
        return key[..Math.Min(30, key.Length)];
    }

    private async Task<ConciliacionSiigoSendPreparedDto> PrepareClientPaymentForSiigoSendAsync(
        string recordId,
        CancellationToken ct)
    {
        var taxes = await _siigo.GetTaxesAsync(ct);
        var documentTypes = await _siigo.GetDocumentTypesAsync("CC", ct);
        var incomeJournalDocument = ResolveIncomeJournalDocumentType(documentTypes);
        var prepared = await _dataverse.PrepareConciliacionClientPaymentSiigoSendAsync(
            recordId,
            ct,
            taxes,
            incomeJournalDocument);

        return await RefreshPreparedClientPaymentWithSiigoBalancesAsync(
            recordId,
            prepared,
            taxes,
            incomeJournalDocument,
            ct);
    }

    private async Task<ConciliacionSiigoSendPreparedDto> RefreshPreparedClientPaymentWithSiigoBalancesAsync(
        string recordId,
        ConciliacionSiigoSendPreparedDto prepared,
        IReadOnlyList<SiigoTaxLookupDto> taxes,
        SiigoDocumentTypeLookupDto incomeJournalDocument,
        CancellationToken ct)
    {
        if (!prepared.CanSend
            || prepared.Row is null
            || string.IsNullOrWhiteSpace(prepared.CustomerIdentification)
            || prepared.InvoiceNumbers.Count == 0)
        {
            return prepared;
        }

        var movementDate = DateOnly.FromDateTime(DateTime.UtcNow);
        if (DateOnly.TryParseExact(
            prepared.Row.MovementDateValue,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedMovementDate))
        {
            movementDate = parsedMovementDate;
        }

        var startDate = movementDate.AddMonths(-18);
        var endDate = movementDate.AddDays(1);
        var siigoInvoices = await _siigo.GetInvoicesAsync(
            customerId: null,
            customerQuery: prepared.CustomerIdentification,
            startDate,
            endDate,
            ct);

        return await _dataverse.PrepareConciliacionClientPaymentSiigoSendAsync(
            recordId,
            ct,
            taxes,
            incomeJournalDocument,
            siigoInvoices.Invoices);
    }

    private static (int LineCount, decimal Debit, decimal Credit) CalculatePreparedJournalTotals(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return (0, 0m, 0m);

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (!document.RootElement.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return (0, 0m, 0m);
            }

            var lineCount = 0;
            var debit = 0m;
            var credit = 0m;
            foreach (var item in items.EnumerateArray())
            {
                var value = ReadDecimal(item, "value");
                if (value == 0m)
                    continue;

                lineCount++;
                var movement = "";
                if (item.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object)
                    movement = ReadString(account, "movement");

                if (string.Equals(movement, "Debit", StringComparison.OrdinalIgnoreCase))
                    debit += value;
                else if (string.Equals(movement, "Credit", StringComparison.OrdinalIgnoreCase))
                    credit += value;
            }

            return (lineCount, Math.Round(debit, 2, MidpointRounding.AwayFromZero), Math.Round(credit, 2, MidpointRounding.AwayFromZero));
        }
        catch (JsonException)
        {
            return (0, 0m, 0m);
        }
    }

    private static decimal ReadDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return 0m;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var number) => number,
            _ => 0m
        };
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return "";

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : value.ToString();
    }

    private static SiigoDocumentTypeLookupDto ResolveIncomeJournalDocumentType(
        IReadOnlyList<SiigoDocumentTypeLookupDto> documentTypes)
    {
        var activeDocuments = documentTypes
            .Where(static documentType => documentType.Active)
            .ToArray();

        var byName = activeDocuments.FirstOrDefault(static documentType =>
            NormalizeSiigoDocumentTypeText($"{documentType.Name} {documentType.Description}")
                .Contains("COMPROBANTE DE INGRESO", StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
            return byName;

        var byCode = activeDocuments.FirstOrDefault(static documentType =>
            string.Equals(documentType.Type, "CC", StringComparison.OrdinalIgnoreCase)
            && string.Equals(documentType.Code, "17", StringComparison.OrdinalIgnoreCase));
        if (byCode is not null)
            return byCode;

        throw new InvalidOperationException("No encontre en Siigo un tipo CC activo llamado Comprobante de ingreso.");
    }

    private static string NormalizeSiigoDocumentTypeText(string value)
    {
        var text = (value ?? "").Trim().ToUpperInvariant();
        return text
            .Replace("Á", "A", StringComparison.Ordinal)
            .Replace("É", "E", StringComparison.Ordinal)
            .Replace("Í", "I", StringComparison.Ordinal)
            .Replace("Ó", "O", StringComparison.Ordinal)
            .Replace("Ú", "U", StringComparison.Ordinal)
            .Replace("Ü", "U", StringComparison.Ordinal)
            .Replace("Ñ", "N", StringComparison.Ordinal);
    }
}
