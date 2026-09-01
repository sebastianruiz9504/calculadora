using CotizadorInterno.Web.Models.Calculator;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.Calculator;
using System.Reflection;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CalculatorPersistenceContractTests
{
    [Fact]
    public void InputHashIsStableAcrossStoredAndSaveModels()
    {
        var line = Line("line-1", 1, "  Microsoft   365  ");
        var save = new ScenarioSaveRequest
        {
            DealType = 1,
            RequiresProration = false,
            Lines = [line]
        };
        var stored = new ScenarioStoredDto
        {
            DealType = save.DealType,
            RequiresProration = save.RequiresProration,
            Lines = [Line("line-1", 1, "Microsoft 365")]
        };

        Assert.Equal(ScenarioInputHasher.Compute(save), ScenarioInputHasher.Compute(stored));
        Assert.Equal(64, ScenarioInputHasher.Compute(save).Length);
    }

    [Fact]
    public void StructuredLinesHashDetectsEconomicChangesButIgnoresWhitespaceNoise()
    {
        var original = Line("line-1", 1, "Microsoft   365");
        var whitespaceOnly = Line("line-1", 1, " Microsoft 365 ");
        var changed = Line("line-1", 1, "Microsoft 365");
        changed.Quantity = 2;

        Assert.Equal(
            ScenarioInputHasher.ComputeLines([original]),
            ScenarioInputHasher.ComputeLines([whitespaceOnly]));
        Assert.NotEqual(
            ScenarioInputHasher.ComputeLines([original]),
            ScenarioInputHasher.ComputeLines([changed]));
    }

    [Fact]
    public void DataverseExportFieldNamesMatchTheProvisionedSchema()
    {
        var service = ReadProjectFile("Services", "DataverseService.CalculatorProposalExports.cs");
        var schema = ReadProjectFile("scripts", "provision_calculator_possibilities_schema.py");
        var required = new[]
        {
            "cr07a_exportidempotency",
            "cr07a_exporteconomichash",
            "cr07a_exportconfigurationhash",
            "cr07a_exportpdfhash",
            "cr07a_exportleasetoken",
            "cr07a_exportconfigurationfile",
            "cr07a_exportpdffile",
            "cr07a_lineshash"
        };

        foreach (var logicalName in required)
        {
            Assert.Contains(logicalName, service + ReadProjectFile(
                "Services",
                "DataverseService.CalculatorPossibilities.cs"), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(logicalName, schema, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("\"cr07a_idempotencykey\"", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"cr07a_configurationfile\"", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"cr07a_pdffile\"", service, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProposalCurrencyDefaultsToUsdAndCopOverridesRemainProposalOnly()
    {
        var view = ReadProjectFile("Views", "Calculator", "Proposal.cshtml");
        var script = ReadProjectFile("wwwroot", "js", "calculator-proposal.js");

        Assert.Contains("<option selected>USD</option>", view, StringComparison.Ordinal);
        Assert.Contains("<option>COP</option>", view, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"currency\" disabled", view, StringComparison.Ordinal);
        Assert.Contains("economicOverrides", script, StringComparison.Ordinal);
        Assert.Contains("currentEconomicHash()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("saveScenarioToDataverse", script, StringComparison.Ordinal);
    }

    [Fact]
    public void PossibilityLinesAndRecommendationUseAtomicDataverseChangeSets()
    {
        var service = ReadProjectFile("Services", "DataverseService.CalculatorPossibilities.cs");

        Assert.Contains("CommitCalculatorPossibilityAsync", service, StringComparison.Ordinal);
        Assert.Contains("ExecuteCalculatorChangeSetAsync", service, StringComparison.Ordinal);
        Assert.Contains("/api/data/v9.2/$batch", service, StringComparison.Ordinal);
        Assert.Contains("request.IsRecommended = record.Scenario.IsRecommended", service, StringComparison.Ordinal);
        Assert.Contains("CalculatorStructuredLinesHashField] = ScenarioInputHasher.ComputeLines", service, StringComparison.Ordinal);
        Assert.Contains("CalculatorLegacyMemoCompatibilityMaxLength", service, StringComparison.Ordinal);
        Assert.Contains("CalculatorMaxChangeSetOperations", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ReplaceCalculatorLinesAsync", service, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistedProposalFilesAreVerifiedAgainstTheirHashes()
    {
        var service = ReadProjectFile("Services", "DataverseService.CalculatorProposalExports.cs");

        Assert.Contains("record.ConfigurationHash", service, StringComparison.Ordinal);
        Assert.Contains("record.PdfHash", service, StringComparison.Ordinal);
        Assert.Contains("SHA256.HashData", service, StringComparison.Ordinal);
        Assert.Contains("GetDataverseEntitiesAsync(url", service, StringComparison.Ordinal);
        Assert.Contains("ParseCalculatorTimestamp(item, \"modifiedon\")", service, StringComparison.Ordinal);
        Assert.Contains("NormalizationForm.FormD", service, StringComparison.Ordinal);
        Assert.Contains("RetireCalculatorProposalExportAttemptAsync", service, StringComparison.Ordinal);
        Assert.Contains("retired-export", service, StringComparison.Ordinal);
        Assert.Contains("overwrite: false", service, StringComparison.Ordinal);
    }

    [Fact]
    public void LongProposalFileNamesKeepThePdfExtension()
    {
        var sanitizer = typeof(DataverseService).GetMethod(
            "SanitizeCalculatorExportFileName",
            BindingFlags.NonPublic | BindingFlags.Static);

        var result = Assert.IsType<string>(sanitizer?.Invoke(null, [new string('a', 240)]));

        Assert.True(result.Length <= 180);
        Assert.EndsWith(".pdf", result);
    }

    private static ScenarioLineInput Line(string id, int order, string description) => new()
    {
        LineId = id,
        LineOrder = order,
        BusinessType = 1,
        ProductId = "product-1",
        ProductDescription = description,
        CostUnit = 100m,
        MarginPercent = 20m,
        ContractMonths = 12,
        Quantity = 1,
        SuggestedRetailPrice = 150m,
        Acelerador = 1.5m,
        HasVat = true
    };

    private static string ReadProjectFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, Path.Combine(segments));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            current = current.Parent;
        }
        throw new FileNotFoundException($"No se encontró {Path.Combine(segments)}.");
    }
}
