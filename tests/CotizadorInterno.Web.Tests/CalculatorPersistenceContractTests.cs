using CotizadorInterno.Web.Models.Calculator;
using CotizadorInterno.Web.Services.Calculator;
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
    public void ProposalCurrencyIsLockedToCalculatorCurrency()
    {
        var view = ReadProjectFile("Views", "Calculator", "Proposal.cshtml");

        Assert.Contains("id=\"currency\" disabled", view, StringComparison.Ordinal);
        Assert.DoesNotContain("<option>USD</option>", view, StringComparison.OrdinalIgnoreCase);
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
