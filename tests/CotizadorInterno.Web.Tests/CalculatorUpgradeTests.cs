using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Calculator;
using CotizadorInterno.Web.Models.Puntajes;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.Calculator;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CalculatorUpgradeTests
{
    private static readonly ProductUpgrade Upgrade = new()
    {
        TargetProductId = "standard", TargetCostUnit = 12m, TargetSuggestedRetailPrice = 15m,
        BaseProductId = "basic", BaseProductDescription = "Basic", BaseCostUnit = 6m, BaseSuggestedRetailPrice = 7m
    };

    private static QuoteLineInput QuoteLine(bool upgrade = true) => new()
    {
        BusinessType = BusinessType.ModernWork, ProductId = "standard", ProductDescription = "Standard",
        CostUnit = upgrade ? 6m : 12m, MarginPercent = 20m, Acelerador = 4m,
        Quantity = upgrade ? 10 : 5, ContractMonths = 12, SuggestedRetailPrice = upgrade ? 8m : 15m,
        Upgrade = upgrade ? Upgrade : null
    };

    private static ScenarioLineInput StoredLine(bool upgrade = true) => new()
    {
        LineId = "line-1", LineOrder = 1, BusinessType = 0, ProductId = "standard", ProductDescription = "Standard",
        CostUnit = upgrade ? 6m : 12m, MarginPercent = 20m, Acelerador = 4m,
        Quantity = upgrade ? 10 : 5, ContractMonths = 12, SuggestedRetailPrice = upgrade ? 8m : 15m,
        Upgrade = upgrade ? Upgrade : null
    };

    [Fact]
    public void MixedFullAndUpgradeLinesUseIncrementalCostAndDestinationAccelerator()
    {
        var result = new QuoteCalculator().Calculate(new() { DealType = DealType.CrossSale, Lines = [QuoteLine(), QuoteLine(false)] });
        Assert.Equal(144m, result.TotalMonthlySale);
        Assert.Equal(1728m, result.TotalSale);
        Assert.Equal(345.6m, result.UtilityRaw);
        Assert.Equal(11.52m, result.Points);
        Assert.Equal(414720m, result.Commission);
    }

    [Fact]
    public void ProrationIsAppliedOnceAfterIncrementalUtility()
    {
        var result = new QuoteCalculator().Calculate(new()
        {
            DealType = DealType.CrossSale, Lines = [QuoteLine(), QuoteLine(false)], RequiresProration = true,
            StartDate = new(2026, 1, 1), EndDate = new(2026, 6, 30)
        });
        Assert.Equal(181, result.ProrationDays);
        Assert.Equal(181m / 365m, result.ProrationFactor);
        Assert.Equal(345.6m * (181m / 365m), result.UtilityAdjusted);
        Assert.Equal(5.71m, result.Points);
        Assert.Equal(205560m, result.Commission);
        Assert.Equal(1728m, result.TotalSale); // Existing display semantics remain unchanged.
    }

    [Theory]
    [InlineData(DealType.ClienteNuevo, 1.08)]
    [InlineData(DealType.Renovacion1, 0.50)]
    [InlineData(DealType.Renovacion2, 0.37)]
    [InlineData(DealType.Renovacion3Plus, 0.20)]
    public void UpgradeKeepsExistingDealMultipliers(DealType type, decimal multiplier)
    {
        var result = new QuoteCalculator().Calculate(new() { DealType = type, Lines = [QuoteLine()] });
        Assert.Equal(172.8m * multiplier, result.UtilityAdjusted);
    }

    [Fact]
    public void InvalidOrStaleUpgradeIsRejectedBeforeCalculating()
    {
        var line = QuoteLine();
        line.CostUnit = 12m;
        Assert.Throws<InvalidOperationException>(() => new QuoteCalculator().Calculate(new() { Lines = [line] }));
        Assert.Throws<InvalidOperationException>(() => UpgradePricing.Validate("basic", "Basic", 6m, Upgrade));
        Assert.Throws<InvalidOperationException>(() => UpgradePricing.Validate("standard", "Standard", 0m, Upgrade with { BaseCostUnit = 12m }));
        Assert.Throws<InvalidOperationException>(() => UpgradePricing.Validate("standard", "Standard", 6m, Upgrade with { TargetProductId = null! }));
    }

    [Theory]
    [InlineData("Standard 1 Year Prepaid", "Basic 1 Year Prepaid", true)]
    [InlineData("Standard 1 Year Monthly", "Basic 1 Year Prepaid", false)]
    [InlineData("Standard 1 Year", "Basic 3 Year", false)]
    [InlineData("Standard Monthly", "Basic Annual", false)]
    public void BillingPeriodsMustMatch(string target, string source, bool valid)
    {
        var error = Record.Exception(() => UpgradePricing.Validate("standard", target, 6m, Upgrade with { BaseProductDescription = source }));
        Assert.Equal(valid, error is null);
    }

    [Fact]
    public void SnapshotSurvivesStructuredDataverseRoundTripWithoutAnotherRecord()
    {
        var service = (DataverseService)RuntimeHelpers.GetUninitializedObject(typeof(DataverseService));
        var build = typeof(DataverseService).GetMethod("BuildCalculatorLinePayload", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var payload = Assert.IsType<Dictionary<string, object?>>(build.Invoke(service,
            [StoredLine(), "group", "possibility", "record", new CurrentUserInfo(), false]));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var parse = typeof(DataverseService).GetMethod("ParseCalculatorLine", BindingFlags.Static | BindingFlags.NonPublic)!;
        var parsed = parse.Invoke(null, [json.RootElement])!;
        var restored = Assert.IsType<ScenarioLineInput>(parsed.GetType().GetProperty("Line")!.GetValue(parsed));
        Assert.Equal(Upgrade, restored.Upgrade);
        Assert.Equal(6m, restored.CostUnit);
        Assert.Equal(ScenarioInputHasher.ComputeLines([StoredLine()]), ScenarioInputHasher.ComputeLines([restored]));

        var clear = Assert.IsType<Dictionary<string, object?>>(build.Invoke(service,
            [StoredLine(false), "group", "possibility", "record", new CurrentUserInfo(), false]));
        Assert.Null(clear["cr07a_linesjson"]);
    }

    [Fact]
    public void HashIncludesTheBaseButKeepsLegacyFullPriceHashes()
    {
        var quote = new QuoteScenarioInput { DealType = DealType.CrossSale, Lines = [QuoteLine()] };
        var save = new ScenarioSaveRequest { DealType = 1, Lines = [StoredLine()] };
        Assert.Equal(ScenarioInputHasher.Compute(quote), ScenarioInputHasher.Compute(save));
        var oldHash = ScenarioInputHasher.Compute(save);
        save.Lines[0].Upgrade = Upgrade with { BaseProductId = "another-base" };
        Assert.NotEqual(oldHash, ScenarioInputHasher.Compute(save));

        var legacy = "1|0||\n1|0|standard|Standard|12|20|12|5|15|4|0";
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacy))),
            ScenarioInputHasher.Compute(new ScenarioSaveRequest { DealType = 1, Lines = [StoredLine(false)] }));
    }

    [Fact]
    public void ProposalUsesUpgradeLabelAndIncrementalPriceIncludingVat()
    {
        var line = StoredLine();
        line.HasVat = true;
        var build = typeof(CalculatorController).GetMethod("BuildProposalLine", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = Assert.IsType<CalculatorProposalLineViewModel>(build.Invoke(null, [line]));
        Assert.Equal("Upgrade: Basic → Standard", result.Description);
        Assert.Equal(7.2m, result.UnitSale);
        Assert.Equal(864m, result.ContractSale);
        Assert.Equal(164.16m, result.ContractVat);
    }

    [Fact]
    public void SubmittedScoreRetainsUpgradeAndFourDecimalCostsDuringVerification()
    {
        var precise = Upgrade with { TargetCostUnit = 12.1234m };
        var result = DataverseService.ResolveVerificationLinesFromSources([], [new ScoreProductLineDto
        {
            LineId = "line", ProductId = "standard", ProductName = "Standard", CostUnit = 6.1234m,
            Upgrade = precise, Quantity = 10, ContractMonths = 12, MarginPercent = 20, Acelerador = 4,
            LineOptionValue = 645250000
        }], []);
        Assert.Equal(precise, result.Single().Upgrade);
        Assert.Equal(6.1234m, result.Single().CostUnit);
        UpgradePricing.Validate(result[0].ProductId, result[0].ProductName, result[0].CostUnit, result[0].Upgrade);
    }

    [Fact]
    public void ExcelContainsTheUpgradeLabelAndIncrementalAmounts()
    {
        var method = typeof(CalculatorController).GetMethod("BuildWorkbook", BindingFlags.Static | BindingFlags.NonPublic)!;
        using var workbook = (ClosedXML.Excel.XLWorkbook)method.Invoke(null, [new QuoteScenarioInput { Lines = [QuoteLine()] }])!;
        var product = workbook.Worksheet(1).CellsUsed().Single(x => x.GetString() == "Upgrade: Basic → Standard");
        var amounts = product.WorksheetRow().CellsUsed().Where(x => x.DataType == ClosedXML.Excel.XLDataType.Number).Select(x => x.GetValue<decimal>()).ToArray();
        Assert.Contains(7.2m, amounts);
        Assert.Contains(864m, amounts);
    }

    [Fact]
    public void ProvisioningDescriptionRoundTripsIntoScoreWithoutLosingUpgradeOrPrecision()
    {
        var precise = Upgrade with { TargetCostUnit = 12.1234m };
        var request = new ProvisioningRequestInput
        {
            Scenario = new() { DealTypeValue = 1, RequiresProration = true, StartDate = "2026-01-01", EndDate = "2026-06-30" },
            LineItems = [new()
            {
                LineId = "line", ProductoId = "standard", ProductoNombre = "Standard", Tipo = "ModernWork",
                CostoUnd = 6.1234m, Upgrade = precise, MargenPorcentaje = 20m, Acelerador = 4m,
                Cantidad = 10, DuracionMeses = 12, VentaUnd = 7.35m, VentaMensual = 73.5m, VentaTotal = 882m
            }]
        };
        var build = typeof(CalculatorController).GetMethod("BuildProvisioningFlowPayload", BindingFlags.Static | BindingFlags.NonPublic)!;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(build.Invoke(null, [request, "upgrade-test"])));
        using var lines = JsonDocument.Parse(json.RootElement.GetProperty("lineItemsJson").GetString()!);
        Assert.Equal(6.1234m, lines.RootElement[0].GetProperty("costoUnd").GetDecimal());
        Assert.True(lines.RootElement[0].GetProperty("esUpgrade").GetBoolean());
        Assert.Contains("Upgrade:", json.RootElement.GetProperty("lineItemsTableMarkdown").GetString());
        var parse = typeof(DataverseService).GetMethod("ParseScoreDescription", BindingFlags.Static | BindingFlags.NonPublic)!;
        var parsed = parse.Invoke(null, [json.RootElement.GetProperty("descriptionText").GetString()])!;
        var products = (List<ScoreProductLineDto>)parsed.GetType().GetProperty("ProductLines")!.GetValue(parsed)!;
        Assert.Equal(precise, products.Single().Upgrade);
        Assert.Equal(6.1234m, products.Single().CostUnit);
    }

    [Fact]
    public void LargeMixedScoreSnapshotFitsExistingStorageWithoutLosingLines()
    {
        var lines = Enumerable.Range(0, 35).Select(index => new ScoreVerificationLineInput
        {
            LineId = $"line-{index}", ProductId = "standard", ProductName = "Microsoft 365 Standard 1 Year Monthly",
            CostUnit = index % 2 == 0 ? 6m : 12m, MarginPercent = 20m, Quantity = index + 1,
            Upgrade = index % 2 == 0 ? Upgrade : null
        }).ToList();
        var json = JsonSerializer.Serialize(new { Version = 1, RequiresProration = true, Lines = lines });
        Assert.True(json.Length > 4000);
        var packed = ScoreSnapshotStorage.Pack(json, 1);
        Assert.True(packed.Length <= 4000);
        Assert.Equal(json, ScoreSnapshotStorage.Expand(packed));
        using var restored = JsonDocument.Parse(ScoreSnapshotStorage.Expand(packed)!);
        Assert.Equal(1, DataverseService.ResolveScoreAdditionalSnapshotVersion(packed, restored.RootElement.GetProperty("Version").GetInt32()));
        Assert.Equal("{\"Version\":0}", ScoreSnapshotStorage.Expand("{\"Version\":0}"));
    }

    [Fact]
    public void PayrollCanReadCommissionFallbackFromPackedUpgradeSnapshot()
    {
        var packed = ScoreSnapshotStorage.Pack(JsonSerializer.Serialize(new { Version = 1, LastResult = new { Commission = 205560m } }), 1);
        using var row = JsonDocument.Parse(JsonSerializer.Serialize(new { additional = packed }));
        var service = (DataverseService)RuntimeHelpers.GetUninitializedObject(typeof(DataverseService));
        foreach (var field in new[] { ("_scoresAdditionalField", "additional"), ("_scoresCommissionField", "commission"), ("_scoresDescriptionField", "description"), ("_scoresLegacyDescriptionField", "legacy") })
            typeof(DataverseService).GetField(field.Item1, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(service, field.Item2);
        var read = typeof(DataverseService).GetMethod("ResolveNominaCommissionValue", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Equal(205560m, read.Invoke(service, [row.RootElement]));
    }
}
