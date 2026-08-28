using System.Globalization;
using System.Text.Json;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Services;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class ConciliacionClientRetentionCatalogTests
{
    public static IEnumerable<object[]> ApprovedRetentions()
    {
        yield return ["RteFte", 4027, "2.5", "2.5", "13551501", "300000"];
        yield return ["RteFte", 4038, "3.5", "3.5", "13551513", "420000"];
        yield return ["RteFte", 4026, "4", "4", "13551503", "480000"];
        yield return ["RteFte", 4024, "10", "10", "13551507", "1200000"];
        yield return ["RteFte", 4023, "11", "11", "13551509", "1320000"];
        yield return ["ReteIca", 4034, "4.14", "4.14", "13551813", "49680"];
        yield return ["ReteIca", 4033, "6.9", "6.9", "13551811", "82800"];
        yield return ["ReteIca", 4031, "8", "8.66", "13551807", "103920"];
        yield return ["ReteIca", 4030, "9.66", "9.66", "13551805", "115920"];
        yield return ["ReteIca", 4028, "11.04", "11.04", "13551801", "132480"];
    }

    [Theory]
    [MemberData(nameof(ApprovedRetentions))]
    public void ApprovedCatalogUsesExactSiigoIdsEffectiveRatesAndAccounts(
        string kind,
        int taxId,
        string catalogRateText,
        string effectiveRateText,
        string accountCode,
        string expectedValueText)
    {
        var catalogRate = ParseDecimal(catalogRateText);
        var effectiveRate = ParseDecimal(effectiveRateText);
        var expectedValue = ParseDecimal(expectedValueText);
        var tax = BuildTax(kind, taxId, catalogRate, active: true);

        var definition = ConciliacionRetentionMapping.ResolveClientPaymentDefinition(kind, tax);
        var options = ConciliacionController.BuildClientPaymentRetentionOptions([tax], kind);

        Assert.NotNull(definition);
        Assert.Equal(catalogRate, definition!.CatalogRate);
        Assert.Equal(effectiveRate, definition.EffectiveRate);
        Assert.Equal(accountCode, definition.AccountCode);
        var option = Assert.Single(options);
        Assert.Equal(taxId, option.TaxId);
        Assert.Equal(effectiveRate, option.Rate);
        Assert.Equal(accountCode, ConciliacionRetentionMapping.ResolveAccountCode(kind, tax, effectiveRate));

        var divisor = string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase)
            ? 1000m
            : 100m;
        Assert.Equal(expectedValue, Math.Round(12_000_000m * option.Rate / divisor, 2, MidpointRounding.AwayFromZero));

        var invoice = new ConciliacionSiigoOpenInvoiceDto
        {
            Id = $"invoice-{taxId}",
            DataverseRecordId = Guid.NewGuid().ToString("D"),
            Name = $"FV-TEST-{taxId}",
            Total = 12_000_000m,
            Balance = 12_000_000m,
            TaxBase = 12_000_000m
        };
        var search = new ConciliacionSiigoOpenInvoiceSearchResultDto
        {
            Invoices = [invoice],
            ReteFuenteOptions = string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase) ? [] : options,
            ReteIcaOptions = string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase) ? options : []
        };
        var issues = new List<string>();
        var allocation = ConciliacionController.BuildClientPaymentAllocatedInvoice(
            new ConciliacionPaymentAllocationRequest
            {
                DocumentId = invoice.Id,
                DataverseRecordId = invoice.DataverseRecordId,
                AppliedValue = 12_000_000m - expectedValue,
                ReteFuenteTaxId = string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase) ? 0 : taxId,
                ReteIcaTaxId = string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase) ? taxId : 0
            },
            search,
            issues);

        Assert.Empty(issues);
        Assert.NotNull(allocation);
        var allocatedRetention = Assert.Single(allocation!.Retentions);
        Assert.Equal(taxId, allocatedRetention.TaxId);
        Assert.Equal(effectiveRate, allocatedRetention.Rate);
        Assert.Equal(accountCode, allocatedRetention.AccountCode);
        Assert.Equal(expectedValue, allocatedRetention.Value);
    }

    [Fact]
    public void ClientDropdownsPublishOnlyActiveHistoricallyApprovedTaxesInRateOrder()
    {
        var taxes = ApprovedRetentions()
            .Select(row => BuildTax(
                (string)row[0],
                (int)row[1],
                ParseDecimal((string)row[2]),
                active: true))
            .Concat(new[]
            {
                BuildTax("RteFte", 4041, 1m, active: true),
                BuildTax("RteFte", 4040, 2m, active: true),
                BuildTax("RteFte", 4025, 6m, active: true),
                BuildTax("RteFte", 4039, 7m, active: true),
                BuildTax("ReteIca", 4032, 7m, active: true),
                BuildTax("ReteIca", 4029, 13.8m, active: true),
                BuildTax("RteFte", 9999, 4m, active: true),
                BuildTax("RteFte", 4027, 2.5m, active: false)
            })
            .ToArray();

        var reteFuente = ConciliacionController.BuildClientPaymentRetentionOptions(taxes, "RteFte");
        var reteIca = ConciliacionController.BuildClientPaymentRetentionOptions(taxes, "ReteIca");

        Assert.Equal([4027, 4038, 4026, 4024, 4023], reteFuente.Select(static option => option.TaxId));
        Assert.Equal(["2,5%", "3,5%", "4%", "10%", "11%"], reteFuente.Select(static option => option.RateLabel));
        Assert.Equal([4034, 4033, 4031, 4030, 4028], reteIca.Select(static option => option.TaxId));
        Assert.Equal(
            ["4,14 x mil", "6,9 x mil", "8,66 x mil", "9,66 x mil", "11,04 x mil"],
            reteIca.Select(static option => option.RateLabel));
        Assert.Equal("ReteICA 8,66 x mil", reteIca.Single(static option => option.TaxId == 4031).Name);
    }

    [Fact]
    public void HistoricalEffectiveReteIcaRateFindsSiigoTax4031WithoutRateFallback()
    {
        var taxes = new[]
        {
            BuildTax("ReteIca", 9999, 8.66m, active: true),
            BuildTax("ReteIca", 4031, 8m, active: true)
        };

        var tax = ConciliacionRetentionMapping.FindClientPaymentTax(taxes, "ReteIca", 8.66m);

        Assert.NotNull(tax);
        Assert.Equal(4031, tax!.Id);
        Assert.Equal("", ConciliacionRetentionMapping.ResolveAccountCode("ReteIca", taxes[0], 8.66m));
    }

    [Fact]
    public void GenericLegacyTaxLookupRemainsAvailableOutsideClientPayments()
    {
        var tax = BuildTax("RteFte", 4039, 7m, active: true);

        Assert.Equal(4039, ConciliacionRetentionMapping.FindTax([tax], "ReteFuente", 7m)?.Id);
        Assert.Null(ConciliacionRetentionMapping.FindClientPaymentTax([tax], "ReteFuente", 7m));

        var issues = new List<string>();
        var retention = Assert.Single(ConciliacionController.ValidateAndResolveCuentaCobroPaymentRetentions(
            new ConciliacionCuentaCobroRowDto
            {
                ValorTotal = 1_000_000m,
                ValorPago = 930_000m,
                BankAccountCode = "11100504",
                Retentions =
                [
                    new ConciliacionCuentaCobroRetentionDto
                    {
                        Kind = "ReteFuente",
                        Label = "ReteFuente 7%",
                        TaxId = 0,
                        BaseValue = 1_000_000m,
                        Rate = 7m,
                        Value = 70_000m
                    }
                ]
            },
            [tax],
            ["22050501", "11100504", "23651503"],
            issues));

        Assert.Empty(issues);
        Assert.Equal(4039, retention.TaxId);
        Assert.Equal("23651503", retention.AccountCode);
    }

    [Fact]
    public void InactiveOrAlteredApprovedTaxesAreNotPublishedForClientPayments()
    {
        var inactive = BuildTax("RteFte", 4024, 10m, active: false);
        var alteredRate = BuildTax("RteFte", 4026, 5m, active: true);
        var alteredKind = new SiigoTaxLookupDto
        {
            Id = 4030,
            Type = "RteFte",
            Name = "ReteFuente 9.66%",
            Percentage = 9.66m,
            Active = true
        };

        Assert.Empty(ConciliacionController.BuildClientPaymentRetentionOptions([inactive, alteredRate], "RteFte"));
        Assert.Empty(ConciliacionController.BuildClientPaymentRetentionOptions([alteredKind], "ReteIca"));
    }

    [Fact]
    public void ClientRteIvaOptionsKeepUsingTheActiveSiigoCatalog()
    {
        var taxes = new[]
        {
            new SiigoTaxLookupDto
            {
                Id = 4050,
                Type = "ReteIVA",
                Name = "Retencion IVA 15%",
                Percentage = 15m,
                Active = true
            },
            new SiigoTaxLookupDto
            {
                Id = 4051,
                Type = "ReteIVA",
                Name = "Retencion IVA 10%",
                Percentage = 10m,
                Active = false
            }
        };

        var option = Assert.Single(
            ConciliacionController.BuildClientPaymentRetentionOptions(taxes, "RteIva"));

        Assert.Equal(4050, option.TaxId);
        Assert.Equal(15m, option.Rate);
        Assert.Equal("15%", option.RateLabel);
        Assert.Equal("13551701", ConciliacionRetentionMapping.ResolveAccountCode("RteIva", taxes[0], 15m));
    }

    [Fact]
    public void ReteIca866UsesTax4031ApprovedAccountAndExistingCc17Due()
    {
        const decimal grossValue = 12_000_000m;
        const decimal retentionValue = 103_920m;
        const decimal bankValue = grossValue - retentionValue;
        var tax = BuildTax("ReteIca", 4031, 8m, active: true);
        var options = ConciliacionController.BuildClientPaymentRetentionOptions([tax], "ReteIca");
        var customer = new ConciliacionSiigoSupplierLookupDto
        {
            Id = "customer-1",
            Identification = "900399875",
            Name = "DIGITAL TECH COPIERS S A S",
            DisplayName = "DIGITAL TECH COPIERS S A S",
            BranchOffice = 0
        };
        var invoice = new ConciliacionSiigoOpenInvoiceDto
        {
            Id = "invoice-4031",
            DataverseRecordId = Guid.NewGuid().ToString("D"),
            Name = "FV-2-7899",
            Total = grossValue,
            Balance = grossValue,
            TaxBase = grossValue,
            Vat = 0m,
            HasExactDueReference = true,
            DuePrefix = "FV-2",
            DueConsecutive = 7899,
            DueQuote = 1,
            DueDateValue = "2026-09-06"
        };
        var search = new ConciliacionSiigoOpenInvoiceSearchResultDto
        {
            Customer = customer,
            Invoices = [invoice],
            ReteIcaOptions = options
        };
        var issues = new List<string>();
        var allocation = ConciliacionController.BuildClientPaymentAllocatedInvoice(
            new ConciliacionPaymentAllocationRequest
            {
                DocumentId = invoice.Id,
                DataverseRecordId = invoice.DataverseRecordId,
                AppliedValue = bankValue,
                ReteIcaTaxId = 4031
            },
            search,
            issues,
            customer);

        Assert.NotNull(allocation);
        Assert.Empty(issues);
        var retention = Assert.Single(allocation!.Retentions);
        Assert.Equal(8.66m, retention.Rate);
        Assert.Equal(retentionValue, retention.Value);
        Assert.Equal("13551807", retention.AccountCode);
        Assert.Equal(4031, retention.TaxId);
        Assert.Equal(grossValue, allocation.GrossValue);

        var payload = ConciliacionController.BuildClientInvoicePaymentJournalPayload(
            new ConciliacionCashFlowRowDto
            {
                RecordId = Guid.NewGuid().ToString("D"),
                MovementDateValue = "2026-08-27",
                EntryValue = bankValue,
                BankAccountCode = "11100504",
                BankAccountName = "Bancolombia Cloud 8100",
                Description = "Pago cliente"
            },
            customer,
            [allocation],
            new SiigoDocumentTypeLookupDto { Id = 17, Type = "CC", Code = "17", Active = true },
            issues);

        Assert.Empty(issues);
        using var json = JsonSerializer.SerializeToDocument(payload);
        var root = json.RootElement;
        Assert.Equal(17, root.GetProperty("document").GetProperty("id").GetInt32());
        var items = root.GetProperty("items").EnumerateArray().ToArray();
        var bank = FindItem(items, "11100504");
        var receivable = FindItem(items, "13050501");
        var reteIca = FindItem(items, "13551807");
        Assert.Equal("Debit", bank.GetProperty("account").GetProperty("movement").GetString());
        Assert.Equal(bankValue, bank.GetProperty("value").GetDecimal());
        Assert.Equal("Credit", receivable.GetProperty("account").GetProperty("movement").GetString());
        Assert.Equal(grossValue, receivable.GetProperty("value").GetDecimal());
        Assert.Equal("FV-2", receivable.GetProperty("due").GetProperty("prefix").GetString());
        Assert.Equal(7899, receivable.GetProperty("due").GetProperty("consecutive").GetInt32());
        Assert.Equal(1, receivable.GetProperty("due").GetProperty("quote").GetInt32());
        Assert.Equal("Debit", reteIca.GetProperty("account").GetProperty("movement").GetString());
        Assert.Equal(retentionValue, reteIca.GetProperty("value").GetDecimal());
        Assert.Equal(4031, reteIca.GetProperty("tax").GetProperty("id").GetInt32());
        Assert.Equal(grossValue, SumByMovement(items, "Debit"));
        Assert.Equal(grossValue, SumByMovement(items, "Credit"));
    }

    private static SiigoTaxLookupDto BuildTax(string kind, int id, decimal percentage, bool active)
    {
        var isIca = string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase);
        return new SiigoTaxLookupDto
        {
            Id = id,
            Type = isIca ? "ReteIca" : "RteFte",
            Name = isIca ? $"ReteICA {percentage}" : $"ReteFuente {percentage}%",
            Percentage = percentage,
            Active = active
        };
    }

    private static JsonElement FindItem(IEnumerable<JsonElement> items, string accountCode) =>
        items.Single(item => string.Equals(
            item.GetProperty("account").GetProperty("code").GetString(),
            accountCode,
            StringComparison.OrdinalIgnoreCase));

    private static decimal SumByMovement(IEnumerable<JsonElement> items, string movement) =>
        items
            .Where(item => string.Equals(
                item.GetProperty("account").GetProperty("movement").GetString(),
                movement,
                StringComparison.OrdinalIgnoreCase))
            .Sum(static item => item.GetProperty("value").GetDecimal());

    private static decimal ParseDecimal(string value) =>
        decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
}
