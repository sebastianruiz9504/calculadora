using CotizadorInterno.Web.Models.Licenciamiento;
using CotizadorInterno.Web.Services;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class LicensingClientMonthRelationTests
{
    private static LicenciamientoCruceTraceItemDto Line(int number, string month = "2026-08", string key = "name:FORMULABS") => new()
    {
        RecordId = number.ToString(), Mes = month, RelationGroupKey = key, SourceVersion = $"W/\"{number}\"",
        RecordRelationId = number == 1 ? "existing-map" : "", RecordRelationVersion = number == 1 ? "W/\"20\"" : "",
        TipoContrato = number == 5 ? "Prepaid" : "Monthly", Valor = 100
    };
    private static LicenciamientoCruceRelationLineDto Snapshot(LicenciamientoCruceTraceItemDto line) => new()
    {
        RecordId = line.RecordId, ExpectedCostVersion = line.SourceVersion,
        ExpectedMappingId = line.RecordRelationId, ExpectedMappingVersion = line.RecordRelationVersion
    };

    [Fact]
    public void SelectingAnyLineIncludesAllFiveIncludingAlreadyRelatedAndOtherContractTypeButExcludesOtherMonthAndClient()
    {
        var group = Enumerable.Range(1, 5).Select(n => Line(n)).ToArray();
        var available = group.Concat(new[] { Line(6, "2026-09"), Line(7, key: "name:OTHER") }).ToArray();
        foreach (var anchor in group)
        {
            var selected = LicensingClientMonthRelation.SelectLines(anchor.RecordId, group.Select(Snapshot).ToArray(), available);
            Assert.Equal(5, selected.Count);
            Assert.Equal(500m, selected.Sum(line => line.Valor));
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("injected")]
    [InlineData("stale-cost")]
    [InlineData("stale-map")]
    public void IncompleteOrChangedGroupsAreRejectedBeforeAnyWrite(string fault)
    {
        var group = Enumerable.Range(1, 5).Select(n => Line(n)).ToArray();
        var expected = group.Select(Snapshot).ToList();
        if (fault == "missing") expected.RemoveAt(4);
        if (fault == "duplicate") expected[4] = Snapshot(group[0]);
        if (fault == "injected") expected[4].RecordId = "another-client";
        if (fault == "stale-cost") expected[4].ExpectedCostVersion = "old";
        if (fault == "stale-map") expected[0].ExpectedMappingVersion = "old";
        Assert.Throws<InvalidOperationException>(() => LicensingClientMonthRelation.SelectLines("1", expected, group));
    }

    [Fact]
    public void GroupIdentityUsesOriginalClientAcrossAccountsAndNeverFuzzyNameMatching()
    {
        var client = Guid.NewGuid().ToString();
        var account = Guid.NewGuid().ToString();
        Assert.Equal(LicensingClientMonthRelation.GroupKey(client, account, "Old name", "1"),
            LicensingClientMonthRelation.GroupKey(client, Guid.NewGuid().ToString(), "New name", "2"));
        Assert.NotEqual(LicensingClientMonthRelation.GroupKey("", account, "Same name", "1"),
            LicensingClientMonthRelation.GroupKey("", Guid.NewGuid().ToString(), "Same name", "2"));
        Assert.NotEqual(LicensingClientMonthRelation.GroupKey("", "", "FORMULABS", "1"),
            LicensingClientMonthRelation.GroupKey("", "", "FORMULABS OTHER SAS", "2"));
        Assert.NotEqual(LicensingClientMonthRelation.GroupKey("", "", "", "1"), LicensingClientMonthRelation.GroupKey("", "", "", "2"));
    }

    [Fact]
    public void FiveMappingsAreOneAtomicChangesetWithExistingAndNewConcurrencyConditions()
    {
        var changes = Enumerable.Range(1, 5).Select(n => new LicensingClientMonthRelation.Change($"/api/data/v9.2/maps({n})",
            n == 1 ? "W/\"20\"" : "", new() { ["targetclientid"] = "client", ["sourceaccountname"] = $"consumption:{n}" })).ToArray();
        var body = LicensingClientMonthRelation.BuildBatch(changes, "batch", "group");
        Assert.Equal(1, body.Split("Content-Type: multipart/mixed").Length - 1);
        Assert.Equal(5, body.Split("PATCH ").Length - 1);
        Assert.Equal(4, body.Split("If-None-Match: *").Length - 1);
        Assert.Contains("If-Match: W/\"20\"", body);
        Assert.EndsWith("--group--\r\n--batch--\r\n", body);
        Assert.Throws<LicenciamientoDashboardConflictException>(() => LicenciamientoDashboardContractEditor.ValidateBatchResponse(true, "HTTP/1.1 412 Precondition Failed", 5));
    }
}
