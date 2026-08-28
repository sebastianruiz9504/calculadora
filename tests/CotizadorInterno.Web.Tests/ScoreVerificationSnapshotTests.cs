using CotizadorInterno.Web.Models.Calculator;
using CotizadorInterno.Web.Models.Puntajes;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.Calculator;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class ScoreVerificationSnapshotTests
{
    private const int ModernWorkOptionValue = 645250000;

    [Fact]
    public void PendingSubmissionSnapshotWinsOverReusedScenario()
    {
        var submission = new[] { CreateSubmissionLine(cost: 100m, margin: 20m, quantity: 1) };
        var currentScenario = new[] { CreateScenarioLine(cost: 200m, margin: 25m, quantity: 2) };

        var resolved = DataverseService.ResolveVerificationLinesFromSources(
            verifiedLines: Array.Empty<ScoreVerificationLineInput>(),
            submissionLines: submission,
            scenarioLines: currentScenario);

        Assert.Single(resolved);
        Assert.Equal(100m, resolved[0].CostUnit);
        Assert.Equal(20m, resolved[0].MarginPercent);
        Assert.Equal(1, resolved[0].Quantity);

        var calculated = Calculate(resolved);
        Assert.Equal(8.64m, calculated.Points);
        Assert.Equal(311_040m, calculated.Commission);
        Assert.Equal(120m, calculated.TotalMonthlySale);
        Assert.Equal(1_440m, calculated.TotalSale);
    }

    [Fact]
    public void ExistingVerifiedSnapshotRemainsAuthoritative()
    {
        var verified = new[] { CreateVerifiedLine(cost: 150m, margin: 30m, quantity: 3) };
        var submission = new[] { CreateSubmissionLine(cost: 100m, margin: 20m, quantity: 1) };
        var currentScenario = new[] { CreateScenarioLine(cost: 200m, margin: 25m, quantity: 2) };

        var resolved = DataverseService.ResolveVerificationLinesFromSources(
            verifiedLines: verified,
            submissionLines: submission,
            scenarioLines: currentScenario);

        Assert.Single(resolved);
        Assert.Equal("verified-line", resolved[0].LineId);
        Assert.Equal("Producto verificado", resolved[0].ProductName);
        Assert.Equal(150m, resolved[0].CostUnit);
        Assert.Equal(30m, resolved[0].MarginPercent);
        Assert.Equal(3, resolved[0].Quantity);
    }

    [Fact]
    public void HeaderOnlySubmissionDoesNotLoadMutableScenario()
    {
        Assert.False(DataverseService.ShouldLoadMutableScoreScenario(
            isVerified: false,
            hasVerifiedLines: false,
            hasSubmissionSnapshot: true));

        Assert.False(DataverseService.ShouldLoadMutableScoreScenario(
            isVerified: false,
            hasVerifiedLines: false,
            hasSubmissionSnapshot: false));

        Assert.False(DataverseService.ShouldLoadMutableScoreScenario(
            isVerified: true,
            hasVerifiedLines: false,
            hasSubmissionSnapshot: false));
    }

    [Fact]
    public void ExplicitPendingSnapshotVersionIsNotPromotedToVerified()
    {
        Assert.Equal(0, DataverseService.ResolveScoreAdditionalSnapshotVersion("{\"Version\":0}", 0));
        Assert.Equal(0, DataverseService.ResolveScoreAdditionalSnapshotVersion("not-json", 0));
        Assert.Equal(0, DataverseService.ResolveScoreAdditionalSnapshotVersion("{\"ContractKindOptionValue\":645250001}", 0));
        Assert.Equal(1, DataverseService.ResolveScoreAdditionalSnapshotVersion("{\"Lines\":[{\"LineId\":\"1\"}]}", 0));
    }

    [Fact]
    public void PendingSubmissionProrationWinsOverMetadataOnlyAdditionalJson()
    {
        Assert.True(DataverseService.ResolveScoreStoredRequiresProration(
            isVerified: false,
            additionalVersion: 1,
            additionalRequiresProration: false,
            submissionRequiresProration: true,
            submissionProrationDays: 31,
            submissionProrationFactor: 31m / 365m));

        Assert.False(DataverseService.ResolveScoreStoredRequiresProration(
            isVerified: true,
            additionalVersion: 1,
            additionalRequiresProration: false,
            submissionRequiresProration: true,
            submissionProrationDays: 31,
            submissionProrationFactor: 31m / 365m));
    }

    [Fact]
    public void ScalarValuesFollowTheSameStateAwarePrecedence()
    {
        Assert.Equal(30m, DataverseService.ResolveScoreSnapshotValue(
            isVerified: true,
            verifiedValue: 30m,
            submissionValue: 20m,
            storedValue: 10m));
        Assert.Equal(20m, DataverseService.ResolveScoreSnapshotValue(
            isVerified: false,
            verifiedValue: 30m,
            submissionValue: 20m,
            storedValue: 10m));
        Assert.Equal(10m, DataverseService.ResolveScoreSnapshotValue(
            isVerified: true,
            verifiedValue: null,
            submissionValue: 20m,
            storedValue: 10m));
    }

    [Fact]
    public void ImmutableHeaderTotalsWinWhenCompactedSnapshotHasNoLastResult()
    {
        var verifiedAggregate = DataverseService.ResolveVerifiedScoreLineAggregate(
            verifiedResultValue: null,
            submissionValue: 120m,
            hasVerifiedLines: true,
            verifiedLineAggregate: 540m);

        var resolved = DataverseService.ResolveScoreSnapshotValue(
            isVerified: true,
            verifiedValue: verifiedAggregate,
            submissionValue: 120m,
            storedValue: null);

        Assert.Equal(120m, resolved);

        Assert.Equal(540m, DataverseService.ResolveVerifiedScoreLineAggregate(
            verifiedResultValue: null,
            submissionValue: null,
            hasVerifiedLines: true,
            verifiedLineAggregate: 540m));
    }

    [Fact]
    public void ExactCalculatorHeaderIsPreservedOnlyForAnUnchangedPendingSubmission()
    {
        Assert.True(DataverseService.ShouldPreserveSubmittedScoreResult(
            isVerified: false,
            inputsUnchanged: true,
            score: 9.83m,
            commission: 353_880m,
            monthlyValue: 885m,
            totalValue: 885m));

        Assert.True(DataverseService.ShouldPreserveSubmittedScoreResult(
            isVerified: false,
            inputsUnchanged: true,
            score: -15m,
            commission: -446_700m,
            monthlyValue: 654m,
            totalValue: 7_842m));

        Assert.False(DataverseService.ShouldPreserveSubmittedScoreResult(
            isVerified: false,
            inputsUnchanged: false,
            score: 9.83m,
            commission: 353_880m,
            monthlyValue: 885m,
            totalValue: 885m));

        Assert.False(DataverseService.ShouldPreserveSubmittedScoreResult(
            isVerified: false,
            inputsUnchanged: true,
            score: 9.83m,
            commission: null,
            monthlyValue: 885m,
            totalValue: 885m));
    }

    [Fact]
    public void MutableScenarioIsNeverUsedWhenRecordHasNoSnapshotLines()
    {
        var currentScenario = new[] { CreateScenarioLine(cost: 200m, margin: 25m, quantity: 2) };

        var resolved = DataverseService.ResolveVerificationLinesFromSources(
            verifiedLines: Array.Empty<ScoreVerificationLineInput>(),
            submissionLines: Array.Empty<ScoreProductLineDto>(),
            scenarioLines: currentScenario);

        Assert.Empty(resolved);
    }

    [Fact]
    public void TwoSubmissionsWithSameBusinessIdKeepDifferentCalculatorResults()
    {
        var currentScenario = new[] { CreateScenarioLine(cost: 200m, margin: 25m, quantity: 2) };
        var first = DataverseService.ResolveVerificationLinesFromSources(
            Array.Empty<ScoreVerificationLineInput>(),
            new[] { CreateSubmissionLine(cost: 100m, margin: 20m, quantity: 1) },
            currentScenario);
        var second = DataverseService.ResolveVerificationLinesFromSources(
            Array.Empty<ScoreVerificationLineInput>(),
            new[] { CreateSubmissionLine(cost: 200m, margin: 25m, quantity: 2) },
            currentScenario);

        var firstResult = Calculate(first);
        var secondResult = Calculate(second);

        Assert.Equal(8.64m, firstResult.Points);
        Assert.Equal(311_040m, firstResult.Commission);
        Assert.Equal(43.20m, secondResult.Points);
        Assert.Equal(1_555_200m, secondResult.Commission);
        Assert.NotEqual(firstResult.Points, secondResult.Points);
        Assert.NotEqual(firstResult.Commission, secondResult.Commission);
    }

    private static QuoteScenarioResult Calculate(IReadOnlyList<ScoreVerificationLineInput> lines) =>
        new QuoteCalculator().Calculate(new QuoteScenarioInput
        {
            DealType = DealType.ClienteNuevo,
            Lines = lines.Select(line => new QuoteLineInput
            {
                BusinessType = BusinessType.ModernWork,
                ProductId = line.ProductId,
                ProductDescription = line.ProductName,
                CostUnit = line.CostUnit,
                MarginPercent = line.MarginPercent,
                ContractMonths = line.ContractMonths,
                Quantity = line.Quantity,
                SuggestedRetailPrice = line.SuggestedRetailPrice,
                Acelerador = line.Acelerador,
                HasVat = line.HasVat
            }).ToList()
        });

    private static ScoreProductLineDto CreateSubmissionLine(decimal cost, decimal margin, int quantity) =>
        new()
        {
            LineId = "submission-line",
            ProductId = "11111111-1111-1111-1111-111111111111",
            ProductName = "Producto historico",
            LineType = "ModernWork",
            LineOptionValue = ModernWorkOptionValue,
            CostUnit = cost,
            MarginPercent = margin,
            ContractMonths = 12,
            Quantity = quantity
        };

    private static ScoreVerificationLineInput CreateVerifiedLine(decimal cost, decimal margin, int quantity) =>
        new()
        {
            LineId = "verified-line",
            ProductId = "11111111-1111-1111-1111-111111111111",
            ProductName = "Producto verificado",
            LineType = "ModernWork",
            LineOptionValue = ModernWorkOptionValue,
            CostUnit = cost,
            MarginPercent = margin,
            ContractMonths = 12,
            Quantity = quantity
        };

    private static ScenarioLineInput CreateScenarioLine(decimal cost, decimal margin, int quantity) =>
        new()
        {
            BusinessType = (int)BusinessType.ModernWork,
            ProductId = "11111111-1111-1111-1111-111111111111",
            ProductDescription = "Producto actual del escenario",
            CostUnit = cost,
            MarginPercent = margin,
            ContractMonths = 12,
            Quantity = quantity
        };
}
