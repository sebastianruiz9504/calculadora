using System.Reflection;
using System.Text.Json;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Licenciamiento;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using Editor = CotizadorInterno.Web.Services.LicenciamientoDashboardContractEditor;

namespace CotizadorInterno.Web.Tests;

public sealed class LicenciamientoDashboardContractEditorTests
{
    private const string CostId = "00000000-0000-0000-0000-000000000001";
    private const string BillId = "00000000-0000-0000-0000-000000000002";
    private static LicenciamientoDashboardContractChangeRequest Request() => new()
    {
        Year = 2026, Month = 8, ClientKey = "group:client-a", SourceContractKey = "monthly", TargetContractKey = "onetime",
        Lines = new()
        {
            new() { Source = "cost", RecordId = CostId, ExpectedContractTypeValue = 645250000 },
            new() { Source = "billing", RecordId = BillId, ExpectedContractTypeValue = 645250000 }
        }
    };
    private static LicenciamientoDashboardDto Dashboard() => new()
    {
        Year = 2026, Month = 8,
        MonthlyCostCard = new()
        {
            Key = "monthly", Breakdown = new[]
            {
                new LicenciamientoDashboardClientCostDto
                {
                    ClientKey = "group:client-a", Lines = new[]
                    {
                        new LicenciamientoDashboardLineDto { Source = "cost", RecordId = CostId, ContractTypeValue = 645250000 },
                        new LicenciamientoDashboardLineDto { Source = "billing", RecordId = BillId, ContractTypeValue = 645250000 }
                    }
                }
            }
        }
    };

    [Theory]
    [InlineData("cost", "onetime", 645250002)]
    [InlineData("billing", "onetime", 645250001)]
    [InlineData("cost", "monthly", 645250000)]
    [InlineData("billing", "monthly", 645250000)]
    public void SourceSpecificValuesLandInTheRequestedExistingDashboardBucket(string source, string target, int expected)
    {
        var value = Editor.TargetValue(source, target);
        Assert.Equal(expected, value);
        var normalize = typeof(DataverseService).GetMethod("ResolveLicenciamientoCruceContractKey", BindingFlags.NonPublic | BindingFlags.Static,
            null, new[] { typeof(int), typeof(string), typeof(bool) }, null)!;
        Assert.Equal(target, normalize.Invoke(null, new object[] { value, "", source == "billing" }));
    }

    [Fact]
    public void DetailKeepsCostAndBillingSeparateAndDeduplicatesOnlyWithinEachSource()
    {
        var cost = new LicenciamientoCruceTraceItemDto { RecordId = CostId, Producto = "Microsoft 365", Cliente = "Cliente", Valor = 100, TipoContratoValue = 645250000 };
        var bill = new LicenciamientoCruceTraceItemDto { RecordId = CostId, Referencia = "FV-1", Valor = 150, Fecha = "2026-09-01", TipoContratoValue = 645250000 };
        var rows = new[] { new LicenciamientoCruceRowDto { Trace = new() { CostItems = new[] { cost, cost }, BillingItems = new[] { bill } } } };
        var lines = Editor.BuildLines(rows);
        Assert.Equal(2, lines.Count);
        Assert.Equal(100m, Assert.Single(lines, line => line.Source == "cost").Amount);
        Assert.Equal(150m, Assert.Single(lines, line => line.Source == "billing").Amount);
        Assert.Equal("Microsoft 365", lines[0].Description);
        Assert.Equal("2026-09-01", lines[1].Date);
    }

    [Theory]
    [InlineData("client")]
    [InlineData("period")]
    [InlineData("record")]
    [InlineData("stale")]
    public async Task InvalidScopeOrStaleSelectionNeverPreparesOrWrites(string scenario)
    {
        var request = Request();
        if (scenario == "client") request.ClientKey = "another-client";
        if (scenario == "period") request.Month = 7;
        if (scenario == "record") request.Lines[0].RecordId = Guid.NewGuid().ToString();
        if (scenario == "stale") request.Lines[0].ExpectedContractTypeValue = 645240000;
        var prepared = false;
        await Assert.ThrowsAsync<LicenciamientoDashboardConflictException>(() => Editor.ApplyAsync(request, Dashboard(),
            (line, ct) => { prepared = true; throw new Exception(); },
            (changes, ct) => throw new Exception(), (change, ct) => throw new Exception(), default));
        Assert.False(prepared);
    }

    [Fact]
    public void DuplicateInvalidAndOversizedSelectionsAreRejected()
    {
        var duplicate = Request(); duplicate.Lines.Add(duplicate.Lines[0]);
        Assert.Throws<InvalidOperationException>(() => Editor.ValidateRequest(duplicate));
        var invalid = Request(); invalid.Lines[0].RecordId = "not-a-guid";
        Assert.Throws<InvalidOperationException>(() => Editor.ValidateRequest(invalid));
        var empty = Request(); empty.Lines.Clear();
        Assert.Throws<InvalidOperationException>(() => Editor.ValidateRequest(empty));
        var oversized = Request(); oversized.Lines = Enumerable.Range(0, Editor.MaxLines + 1).Select(_ => new LicenciamientoDashboardLineSelection()).ToList();
        Assert.Throws<InvalidOperationException>(() => Editor.ValidateRequest(oversized));
    }

    [Fact]
    public async Task MixedSourcesAreSavedOnceAndReadBackWhileUnselectedRowsAreUntouched()
    {
        var records = new Dictionary<string, int> { [CostId] = 645250000, [BillId] = 645250000, ["unselected"] = 645250000 };
        var batches = 0; var readbacks = 0;
        var result = await Editor.ApplyAsync(Request(), Dashboard(),
            (line, ct) => Task.FromResult(new Editor.Change(line.RecordId, "type", Editor.TargetValue(line.Source, "onetime"), "W/\"1\"", Editor.TargetValue(line.Source, "onetime"), false)),
            (changes, ct) =>
            {
                batches++; Assert.Equal(2, changes.Count);
                foreach (var change in changes) records[change.Path] = change.TargetValue;
                return Task.CompletedTask;
            },
            (change, ct) => { Interlocked.Increment(ref readbacks); return Task.FromResult(records[change.Path] == change.TargetValue); }, default);
        Assert.Equal(1, batches); Assert.Equal(2, readbacks); Assert.Equal(2, result.UpdatedCount);
        Assert.Equal(645250002, records[CostId]); Assert.Equal(645250001, records[BillId]); Assert.Equal(645250000, records["unselected"]);
    }

    [Fact]
    public async Task FailurePreparingOneLinePreventsTheWholeBatch()
    {
        var persisted = false;
        await Assert.ThrowsAsync<LicenciamientoDashboardConflictException>(() => Editor.ApplyAsync(Request(), Dashboard(),
            (line, ct) => line.Source == "billing" ? throw Editor.Conflict()
                : Task.FromResult(new Editor.Change(line.RecordId, "type", 645250002, "W/\"1\"", 645250002, false)),
            (changes, ct) => { persisted = true; return Task.CompletedTask; }, (change, ct) => Task.FromResult(true), default));
        Assert.False(persisted);
    }

    [Fact]
    public async Task LostResponseRetryRecognizesAlreadyMovedRowsWithoutAnotherWrite()
    {
        var dashboard = Dashboard();
        var client = dashboard.MonthlyCostCard.Breakdown[0];
        foreach (var line in client.Lines) line.ContractTypeValue = Editor.TargetValue(line.Source, "onetime");
        dashboard.MonthlyCostCard.Breakdown = Array.Empty<LicenciamientoDashboardClientCostDto>();
        dashboard.PrepaidCostCard = new() { Key = "onetime", Breakdown = new[] { client } };
        var result = await Editor.ApplyAsync(Request(), dashboard,
            (line, ct) => Task.FromResult(new Editor.Change(line.RecordId, "type", Editor.TargetValue(line.Source, "onetime"), "W/\"2\"", Editor.TargetValue(line.Source, "onetime"), true)),
            (changes, ct) => throw new Exception("Must not write again"), (change, ct) => Task.FromResult(true), default);
        Assert.Equal(0, result.UpdatedCount); Assert.Equal(2, result.AlreadyUpdatedCount);
    }

    [Fact]
    public async Task ReadbackMismatchNeverReportsSuccess()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Editor.ApplyAsync(Request(), Dashboard(),
            (line, ct) => Task.FromResult(new Editor.Change(line.RecordId, "type", 645250002, "W/\"1\"", 645250002, false)),
            (changes, ct) => Task.CompletedTask, (change, ct) => Task.FromResult(false), default));
    }

    [Fact]
    public void PrepareRequiresVersionAndRejectsAnotherEditorsContractChange()
    {
        using var stale = JsonDocument.Parse("{\"type\":645250001,\"@odata.etag\":\"W/\\\"2\\\"\"}");
        Assert.Throws<LicenciamientoDashboardConflictException>(() => Editor.Prepare("/record", "type", 645250002, 645250002, 645250000, stale.RootElement));
        using var missingVersion = JsonDocument.Parse("{\"type\":645250000}");
        Assert.Throws<InvalidOperationException>(() => Editor.Prepare("/record", "type", 645250002, 645250002, 645250000, missingVersion.RootElement));
    }

    [Fact]
    public void BatchHasOneChangeSetExactVersionGuardsAndOnlyContractFields()
    {
        var operations = new[]
        {
            new Editor.Change($"/api/data/v9.2/costs({CostId})", "cr07a_tipocontrato", 645250002, "W/\"12\"", 645250002, false),
            new Editor.Change($"/api/data/v9.2/bills({BillId})", "contract", 645250001, "W/\"13\"", 645250001, false)
        };
        var body = Editor.BuildBatch(operations, "batch_test", "changeset_test");
        Assert.Equal(1, body.Split("boundary=changeset_test").Length - 1);
        Assert.Contains("If-Match: W/\"12\"\r\n", body);
        Assert.Contains("If-Match: W/\"13\"\r\n", body);
        Assert.Contains("{\"cr07a_tipocontrato\":645250002}", body);
        Assert.Contains("{\"contract\":645250001}", body);
        Assert.EndsWith("--changeset_test--\r\n--batch_test--\r\n", body);
        Assert.DoesNotContain("\n", body.Replace("\r\n", ""));
    }

    [Theory]
    [InlineData("", 2)]
    [InlineData("HTTP/1.1 204 No Content", 2)]
    [InlineData("HTTP/1.1 204 No Content\r\nHTTP/1.1 400 Bad Request", 2)]
    public void MalformedOrFailedBatchCannotBeMistakenForSuccess(string body, int count) =>
        Assert.Throws<InvalidOperationException>(() => Editor.ValidateBatchResponse(true, body, count));

    [Fact]
    public void BatchConflictIsExplicitAndSuccessfulBatchNeedsAllResponses()
    {
        Assert.Throws<LicenciamientoDashboardConflictException>(() => Editor.ValidateBatchResponse(true, "HTTP/1.1 412 Precondition Failed", 2));
        Editor.ValidateBatchResponse(true, "HTTP/1.1 204 No Content\r\nHTTP/1.1 204 No Content", 2);
    }

    [Fact]
    public void NewMutationRequiresPostAndAntiforgeryAndUsesDashboardAuthorization()
    {
        var action = typeof(DashboardController).GetMethod(nameof(DashboardController.LicenciamientoContractType))!;
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.Contains(typeof(DashboardController).GetCustomAttributes(), attribute => attribute.GetType().Name == "ModuleAuthorizeAttribute");
    }
}
