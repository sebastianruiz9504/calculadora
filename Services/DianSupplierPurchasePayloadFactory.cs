using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.Conciliacion;

namespace CotizadorInterno.Web.Services;

public interface IDianSupplierPurchasePayloadFactory
{
    DianSupplierInvoiceIdentity ResolveIdentity(ConciliacionDianSupplierInvoiceRowDto row);

    DianSupplierPurchasePayloadBuildResult Build(
        ConciliacionDianSupplierInvoiceRowDto row,
        string siigoSupplierIdentification,
        IReadOnlyList<SiigoDocumentTypeLookupDto> documentTypes,
        IReadOnlyList<SiigoPaymentTypeLookupDto> paymentTypes,
        IReadOnlyList<SiigoTaxLookupDto> taxes);
}

public sealed record DianSupplierInvoiceIdentity(string Prefix, string Number);

public sealed record DianSupplierPurchasePayloadBuildResult(
    bool CanSend,
    object? Payload,
    string PayloadJson,
    DianSupplierInvoiceIdentity Identity,
    decimal Total,
    IReadOnlyList<string> Issues);

public sealed class DianSupplierPurchasePayloadFactory : IDianSupplierPurchasePayloadFactory
{
    private const string VatAccountCode = "240803";
    private const string VatDescription = "Iva Descontable";

    public DianSupplierInvoiceIdentity ResolveIdentity(ConciliacionDianSupplierInvoiceRowDto row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var rawNumber = FirstNonEmpty(row.Folio, row.InvoiceNumber).Trim();
        var prefix = NormalizePrefix(row.Prefix);
        var number = ExtractDigits(rawNumber);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            var embeddedIdentity = Regex.Match(
                rawNumber,
                @"^\s*(.+?)[\s\-]+(\d+)\s*$",
                RegexOptions.CultureInvariant);
            if (embeddedIdentity.Success)
            {
                prefix = NormalizePrefix(embeddedIdentity.Groups[1].Value);
                number = ExtractDigits(embeddedIdentity.Groups[2].Value);
            }
        }
        if (string.IsNullOrWhiteSpace(prefix))
            prefix = "DIAN";

        return new DianSupplierInvoiceIdentity(prefix, number);
    }

    public DianSupplierPurchasePayloadBuildResult Build(
        ConciliacionDianSupplierInvoiceRowDto row,
        string siigoSupplierIdentification,
        IReadOnlyList<SiigoDocumentTypeLookupDto> documentTypes,
        IReadOnlyList<SiigoPaymentTypeLookupDto> paymentTypes,
        IReadOnlyList<SiigoTaxLookupDto> taxes)
    {
        ArgumentNullException.ThrowIfNull(row);
        documentTypes ??= Array.Empty<SiigoDocumentTypeLookupDto>();
        paymentTypes ??= Array.Empty<SiigoPaymentTypeLookupDto>();
        taxes ??= Array.Empty<SiigoTaxLookupDto>();

        var issues = new List<string>();
        var identity = ResolveIdentity(row);
        var supplierIdentification = ExtractDigits(siigoSupplierIdentification);

        if (supplierIdentification.Length < 5)
            issues.Add("El proveedor Siigo no tiene una identificacion valida.");
        if (string.IsNullOrWhiteSpace(row.AccountCode))
            issues.Add("Falta cuenta gasto.");
        if (identity.Prefix.Length > 6)
            issues.Add("El prefijo de la factura supera 6 caracteres; no se enviara truncado a Siigo.");
        if (string.IsNullOrWhiteSpace(identity.Number))
            issues.Add("El consecutivo de la factura del proveedor debe contener numeros.");
        if (!DateOnly.TryParseExact(
                row.EmissionDateValue,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var emissionDate))
        {
            issues.Add("La fecha de emision no tiene formato valido para Siigo (yyyy-MM-dd).");
        }
        if (row.TotalValue <= 0m)
            issues.Add("El total de la factura debe ser mayor a cero.");
        if (!string.Equals((row.Currency ?? "").Trim(), "COP", StringComparison.OrdinalIgnoreCase))
            issues.Add($"Solo se crean automaticamente compras en COP. Divisa DIAN recibida: {FirstNonEmpty(row.Currency, "sin informar")}.");

        SiigoDocumentTypeLookupDto? purchaseDocument = null;
        SiigoPaymentTypeLookupDto? paymentType = null;
        try
        {
            purchaseDocument = ResolvePurchaseDocumentType(documentTypes);
        }
        catch (InvalidOperationException ex)
        {
            issues.Add(ex.Message);
        }

        try
        {
            paymentType = ResolveSupplierPurchasePaymentType(paymentTypes);
        }
        catch (InvalidOperationException ex)
        {
            issues.Add(ex.Message);
        }

        var items = BuildItems(row, taxes, issues);
        var total = CalculateTotal(items);
        if (Math.Abs(total - row.TotalValue) > 1m)
        {
            issues.Add($"El total calculado para Siigo ({total:N2}) no coincide con el total DIAN ({row.TotalValue:N2}).");
        }

        object? payload = null;
        if (purchaseDocument is not null && paymentType is not null)
        {
            var payment = new Dictionary<string, object?>
            {
                ["id"] = paymentType.Id,
                ["value"] = total
            };
            if (paymentType.DueDate && emissionDate != default)
                payment["due_date"] = emissionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            payload = new Dictionary<string, object?>
            {
                ["document"] = new { id = purchaseDocument.Id },
                ["date"] = emissionDate == default
                    ? row.EmissionDateValue
                    : emissionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["supplier"] = new
                {
                    identification = supplierIdentification,
                    branch_office = 0
                },
                ["provider_invoice"] = new
                {
                    prefix = identity.Prefix,
                    number = identity.Number
                },
                ["items"] = items.Select(static item => item.Payload).ToArray(),
                ["payments"] = new[] { payment },
                ["observations"] = Truncate(
                    $"Importado desde DIAN. CUFE/CUDE: {row.Cufe}. Cuenta: {row.AccountCode} {row.AccountName}.",
                    500)
            };
        }

        var payloadJson = payload is null
            ? ""
            : JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        var distinctIssues = issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new DianSupplierPurchasePayloadBuildResult(
            CanSend: payload is not null && distinctIssues.Length == 0,
            Payload: payload is not null && distinctIssues.Length == 0 ? payload : null,
            PayloadJson: payloadJson,
            Identity: identity,
            Total: total,
            Issues: distinctIssues);
    }

    private static IReadOnlyList<PurchaseItemDraft> BuildItems(
        ConciliacionDianSupplierInvoiceRowDto row,
        IReadOnlyList<SiigoTaxLookupDto> taxes,
        List<string> issues)
    {
        var baseAmount = row.BaseAmount > 0m
            ? row.BaseAmount
            : Math.Max(0m, row.TotalValue - row.VatValue);
        if (baseAmount <= 0m)
            baseAmount = row.TotalValue;

        var description = Truncate(FirstNonEmpty(row.AccountName, row.AccountCode, "Cuenta contable"), 100);
        if (row.VatValue <= 0m)
        {
            return new[]
            {
                BuildItem(row.AccountCode, description, RoundUnitPrice(baseAmount))
            };
        }

        var taxMatch = ResolveVatTax(row, baseAmount, taxes, issues);
        if (taxMatch is null)
        {
            return new[]
            {
                BuildItem(row.AccountCode, description, RoundUnitPrice(baseAmount)),
                BuildItem(VatAccountCode, VatDescription, RoundUnitPrice(row.VatValue))
            };
        }

        var drafts = new List<PurchaseItemDraft>
        {
            BuildItem(row.AccountCode, description, RoundUnitPrice(taxMatch.TaxableBase))
        };
        if (taxMatch.NonTaxedBase > 0.01m)
        {
            drafts.Add(BuildItem(
                row.AccountCode,
                Truncate($"{description} sin IVA", 100),
                RoundUnitPrice(taxMatch.NonTaxedBase)));
        }

        drafts.Add(BuildItem(VatAccountCode, VatDescription, RoundUnitPrice(row.VatValue)));
        var difference = RoundCurrency(row.TotalValue - CalculateTotal(drafts));
        if (Math.Abs(difference) <= 1m && difference != 0m)
        {
            var last = drafts[^1];
            var adjusted = RoundUnitPrice(last.Price + difference);
            drafts[^1] = BuildItem(last.AccountCode, last.Description, adjusted);
        }

        return drafts;
    }

    private static VatMatch? ResolveVatTax(
        ConciliacionDianSupplierInvoiceRowDto row,
        decimal baseAmount,
        IReadOnlyList<SiigoTaxLookupDto> taxes,
        List<string> issues)
    {
        var selected = taxes
            .Where(static tax => tax.Active
                && tax.Id > 0
                && tax.Percentage > 0m
                && tax.Type.Equals("IVA", StringComparison.OrdinalIgnoreCase))
            .Select(tax =>
            {
                var taxableBase = row.VatValue / (tax.Percentage / 100m);
                return new VatMatch(taxableBase, baseAmount - taxableBase, tax.Percentage);
            })
            .Where(static match => match.TaxableBase > 0m && match.NonTaxedBase >= -1m)
            .OrderBy(static match => match.NonTaxedBase < 0m ? 0m : match.NonTaxedBase)
            .ThenByDescending(static match => match.Percentage)
            .FirstOrDefault();

        if (selected is null)
        {
            var effectivePercent = baseAmount > 0m
                ? Math.Round(row.VatValue / baseAmount * 100m, 2, MidpointRounding.AwayFromZero)
                : 0m;
            issues.Add($"No encontre en Siigo un IVA activo que permita cuadrar IVA {row.VatValue:N2} sobre base {baseAmount:N2}. Tasa efectiva {effectivePercent:N2}%.");
            return null;
        }

        if (selected.NonTaxedBase < 0m && Math.Abs(selected.NonTaxedBase) <= 1m)
            return selected with { TaxableBase = baseAmount, NonTaxedBase = 0m };

        return selected;
    }

    private static PurchaseItemDraft BuildItem(string accountCode, string description, decimal price)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "Account",
            ["code"] = (accountCode ?? "").Trim(),
            ["description"] = description,
            ["quantity"] = 1,
            ["price"] = price
        };
        return new PurchaseItemDraft((accountCode ?? "").Trim(), description, price, payload);
    }

    private static decimal CalculateTotal(IEnumerable<PurchaseItemDraft> items) =>
        RoundCurrency(items.Sum(static item => item.Price));

    private static SiigoDocumentTypeLookupDto ResolvePurchaseDocumentType(
        IReadOnlyList<SiigoDocumentTypeLookupDto> documentTypes)
    {
        var active = documentTypes.Where(static item => item.Active).ToArray();
        return active.FirstOrDefault(static item =>
                item.Type.Equals("FC", StringComparison.OrdinalIgnoreCase)
                && item.Code.Equals("1", StringComparison.OrdinalIgnoreCase)
                && NormalizeText($"{item.Name} {item.Description}").Contains("COMPRA", StringComparison.OrdinalIgnoreCase))
            ?? active.FirstOrDefault(static item => item.Type.Equals("FC", StringComparison.OrdinalIgnoreCase) && item.Code.Equals("1", StringComparison.OrdinalIgnoreCase))
            ?? active.FirstOrDefault(static item => item.Type.Equals("FC", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No encontre en Siigo un tipo de documento FC activo para crear compras.");
    }

    private static SiigoPaymentTypeLookupDto ResolveSupplierPurchasePaymentType(
        IReadOnlyList<SiigoPaymentTypeLookupDto> paymentTypes)
    {
        var active = paymentTypes.Where(static item => item.Active).ToArray();
        var selected = active.FirstOrDefault(static item =>
                NormalizeText(item.Name).Equals("CREDITO PROVEEDORES", StringComparison.OrdinalIgnoreCase))
            ?? active.FirstOrDefault(static item => item.Id == 1726)
            ?? throw new InvalidOperationException("No encontre en Siigo la forma de pago FC activa 'Credito proveedores' (ID 1726); no se usara otra forma de pago por defecto.");
        if (!selected.DueDate)
        {
            throw new InvalidOperationException(
                "La forma de pago 'Credito proveedores' no permite fecha de vencimiento en Siigo; se bloqueo para no registrar la compra como pago inmediato.");
        }

        return selected;
    }

    private static string NormalizeText(string value)
    {
        var decomposed = (value ?? "").Normalize(NormalizationForm.FormD);
        var withoutDiacritics = new string(decomposed
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .ToArray())
            .Normalize(NormalizationForm.FormC);
        return Regex.Replace(withoutDiacritics.Trim().ToUpperInvariant(), @"\s+", " ", RegexOptions.CultureInvariant);
    }

    private static string ExtractDigits(string? value) =>
        new((value ?? "").Where(char.IsDigit).ToArray());

    private static string NormalizePrefix(string? value) =>
        new((value ?? "")
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal RoundUnitPrice(decimal value) =>
        Math.Round(value, 6, MidpointRounding.AwayFromZero);

    private static string Truncate(string? value, int maxLength)
    {
        var resolved = value ?? "";
        return resolved.Length <= maxLength ? resolved : resolved[..maxLength];
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private sealed record VatMatch(decimal TaxableBase, decimal NonTaxedBase, decimal Percentage);

    private sealed record PurchaseItemDraft(
        string AccountCode,
        string Description,
        decimal Price,
        Dictionary<string, object?> Payload);
}
