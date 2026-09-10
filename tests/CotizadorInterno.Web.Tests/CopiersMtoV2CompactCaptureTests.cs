using CotizadorInterno.Web.Models.CopiersMtoV2;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CopiersMtoV2CompactCaptureTests
{
    [Fact]
    public void CanonicalizesVisibleTimesToBogotaAndPreservesZeroAndMissingHistory()
    {
        var (request, record, answers) = Capture();
        var result = CopiersMtoV2CompactCapture.Canonicalize(request, record, answers);
        Assert.Equal("09/09/2026 23:30", Value(result, "service_started_at"));
        Assert.Equal("10/09/2026 00:15", Value(result, "service_ended_at"));
        Assert.Equal("0", Value(result, "copies_after"));
        Assert.Equal("0", Value(result, "scans_after"));
        Assert.Contains("Sin registro / 0", Value(result, "counters"));
        var command = CopiersMtoV2CompactCapture.CounterCommand(record, result, request.ServiceEndedAtUtc!.Value, new string('a', 64));
        Assert.Equal(record.RecordId, command.MaintenanceRecordId);
        Assert.Equal(record.SubmissionKey, command.SubmissionKey);
        Assert.Equal(request.ServiceEndedAtUtc, command.ReadingAtUtc);
        Assert.Null(command.PreviousCopiesCounter);
        Assert.Null(command.PreviousScansCounter);
        Assert.Equal(0L, command.CopiesCounter);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.2")]
    [InlineData("2147483648")]
    [InlineData("")]
    public void RejectsInvalidCurrentCounter(string counter)
    {
        var (request, record, answers) = Capture();
        answers.Single(a => a.Key == "copies_after").Value = counter;
        AssertCode("counter_value_invalid", () => CopiersMtoV2CompactCapture.Canonicalize(request, record, answers));
    }

    [Fact]
    public void RejectsSignedTimeSnapshotMismatch()
    {
        var (request, record, answers) = Capture();
        answers.Single(a => a.Key == "service_ended_at_utc").Value = "2026-09-10T05:16:00Z";
        AssertCode("visit_time_snapshot_mismatch", () => CopiersMtoV2CompactCapture.Canonicalize(request, record, answers));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RequiresBothVisitTimes(bool missingStart)
    {
        var (request, record, answers) = Capture();
        if (missingStart) request.ServiceStartedAtUtc = null;
        else request.ServiceEndedAtUtc = null;
        AssertCode(missingStart ? "visit_started_required" : "visit_ended_required",
            () => CopiersMtoV2CompactCapture.Canonicalize(request, record, answers));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsInvertedVisitOrSigningOrder(bool invertedVisit)
    {
        var (request, record, answers) = Capture();
        if (invertedVisit) request.ServiceStartedAtUtc = request.ServiceEndedAtUtc!.Value.AddMinutes(1);
        else request.DeviceSignedAtUtc = request.ServiceEndedAtUtc!.Value.AddMinutes(-1);
        AssertCode("visit_time_order_invalid", () => CopiersMtoV2CompactCapture.Canonicalize(request, record, answers));
    }

    [Fact]
    public void UsesLocalServiceDateNotUtcDate()
    {
        var (request, record, answers) = Capture();
        record.ServiceDate = new DateOnly(2026, 9, 10);
        AssertCode("visit_date_mismatch", () => CopiersMtoV2CompactCapture.Canonicalize(request, record, answers));
    }

    [Theory]
    [InlineData("work")]
    [InlineData("customer")]
    [InlineData("recommendations")]
    public void RejectsOverlongCompactNarrativeWithoutTruncation(string field)
    {
        var (request, record, answers) = Capture();
        if (field == "work") request.WorkPerformed = new string('a', 1001);
        else if (field == "customer") request.CustomerObservations = new string('a', 251);
        else answers.Add(new() { Key = "recommendations", Value = new string('a', 251) });
        Assert.Throws<CopiersMaintenanceV2ValidationException>(() => CopiersMtoV2CompactCapture.Canonicalize(request, record, answers));
    }

    [Fact]
    public void CounterCommandPreservesExactPreviousRecordAndValues()
    {
        var (request, record, answers) = Capture();
        answers.AddRange(new[]
        {
            Answer("counter_record_id", "ef9e050e-3a1f-4eb6-afc9-dc649f91d139"),
            Answer("counter_recorded_at", "2026-09-08"),
            Answer("copies_before", "0"), Answer("scans_before", "0")
        });
        var command = CopiersMtoV2CompactCapture.CounterCommand(record, answers, request.ServiceEndedAtUtc!.Value, new string('a', 64));
        Assert.Equal("ef9e050e-3a1f-4eb6-afc9-dc649f91d139", command.PreviousCounterRecordId);
        Assert.Equal("2026-09-08", command.PreviousDateValue);
        Assert.Equal(0L, command.PreviousCopiesCounter);
        Assert.Equal(0L, command.PreviousScansCounter);
    }

    private static (CopiersMaintenanceV2FinalizeMultipartRequestDto, CopiersMaintenanceV2DraftRecord,
        List<CopiersMaintenanceV2FormAnswerSnapshot>) Capture()
    {
        var request = new CopiersMaintenanceV2FinalizeMultipartRequestDto
        {
            WorkPerformed = "Limpieza y verificación de impresión.",
            ServiceStartedAtUtc = DateTimeOffset.Parse("2026-09-10T04:30:00Z"),
            ServiceEndedAtUtc = DateTimeOffset.Parse("2026-09-10T05:15:00Z"),
            DeviceSignedAtUtc = DateTimeOffset.Parse("2026-09-10T05:16:00Z")
        };
        var record = new CopiersMaintenanceV2DraftRecord
        {
            RecordId = "017ab738-76ab-4fc7-b476-8e89d6c932b8", SubmissionKey = "compact-capture-test-001",
            ClientId = "1f4e7fd4-73e2-4177-ac61-016cbe17cad4", EquipmentId = "4578233e-6307-4231-b09e-1524a19fbe98",
            EquipmentSerial = "TEST-001", ServiceDate = new DateOnly(2026, 9, 9)
        };
        return (request, record, new()
        {
            Answer("service_started_at", "untrusted display"), Answer("service_ended_at", "untrusted display"),
            Answer("service_started_at_utc", "2026-09-10T04:30:00Z"), Answer("service_ended_at_utc", "2026-09-10T05:15:00Z"),
            Answer("copies_after", "0"), Answer("scans_after", "0"), Answer("counters", "untrusted summary")
        });
    }

    private static CopiersMaintenanceV2FormAnswerSnapshot Answer(string key, string value) => new() { Key = key, Value = value };
    private static string Value(IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> answers, string key) => answers.Single(a => a.Key == key).Value;
    private static void AssertCode(string code, Action action) => Assert.Equal(code, Assert.Throws<CopiersMaintenanceV2ValidationException>(action).Code);
}
