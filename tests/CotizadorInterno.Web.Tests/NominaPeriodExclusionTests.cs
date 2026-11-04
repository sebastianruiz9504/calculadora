using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CotizadorInterno.Web.Models.Nomina;
using CotizadorInterno.Web.Services;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class NominaPeriodExclusionTests
{
    [Fact]
    public void ExcludedEmployeeNeverEntersSubmissionEvenWithMoneyAndIncompleteDays()
    {
        var included = new NominaRowDto { EmployeeId = "included", NetPayroll = 2_550_000m };
        var excluded = new NominaRowDto
        {
            EmployeeId = "excluded", ExcludedFromPeriod = true, NetPayroll = 1_000_000m,
            Warnings = ["Hace falta liquidar 30 dias del mes.", "Hay novedades con dias pendientes de motivo."]
        };

        var rows = DataverseService.GetNominaRowsForSubmission([included, excluded]);
        Assert.Same(included, Assert.Single(rows));
        Assert.Equal(2_550_000m, rows.Sum(row => row.NetPayroll));
    }

    [Fact]
    public void IncludedEmployeeStillMustHaveCompleteDayCoverage()
    {
        var employee = new NominaRowDto { Warnings = ["Hace falta liquidar 30 dias del mes."] };
        Assert.Throws<InvalidOperationException>(() => DataverseService.GetNominaRowsForSubmission([employee]));
    }

    [Fact]
    public void EmptySubmissionIsRejected()
    {
        var employee = new NominaRowDto { ExcludedFromPeriod = true };
        Assert.Throws<InvalidOperationException>(() => DataverseService.GetNominaRowsForSubmission([employee]));
    }

    [Fact]
    public void ExclusionCannotSilentlyLeaveAnAlreadySentPayrollRecord()
    {
        var employee = new NominaRowDto { ExcludedFromPeriod = true, ExistingPayrollRecordId = "existing" };
        Assert.Throws<InvalidOperationException>(() => DataverseService.GetNominaRowsForSubmission([new(), employee]));
    }

    [Fact]
    public void ExclusionSurvivesRequestDeserializationAndAdjustmentNormalization()
    {
        var id = Guid.NewGuid().ToString();
        var request = JsonSerializer.Deserialize<NominaPreviewRequest>(
            $$"""{"periodKey":"2026-10","adjustments":[{"employeeId":"{{id}}","excludedFromPeriod":true}]}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var method = typeof(DataverseService).GetMethod("NormalizeNominaAdjustments", BindingFlags.NonPublic | BindingFlags.Static)!;
        var normalized = (Dictionary<string, NominaAdjustmentInput>)method.Invoke(null, [request.Adjustments])!;
        Assert.True(normalized[id].ExcludedFromPeriod);
        Assert.False(new NominaAdjustmentInput().ExcludedFromPeriod);
    }

    [Fact]
    public void SharedDraftKeepsTheExclusionAndOriginalAmountsWithoutChangingAnotherMonth()
    {
        var draft = new NominaDraftDto
        {
            Version = 4, PeriodKey = "2026-10", PaymentDateValue = "2026-11-05",
            Rows = JsonNode.Parse("""[{"employeeId":"excluded","excludedFromPeriod":true,"netPayroll":1000000}]""")
        };
        var method = typeof(NominaDraftStore).GetMethod("NormalizeDraft", BindingFlags.NonPublic | BindingFlags.Static)!;
        var saved = (NominaDraftDto)method.Invoke(null, [draft])!;
        Assert.Equal("2026-10", saved.PeriodKey);
        Assert.True(saved.Rows![0]!["excludedFromPeriod"]!.GetValue<bool>());
        Assert.Equal(1_000_000m, saved.Rows[0]!["netPayroll"]!.GetValue<decimal>());
        Assert.NotSame(draft.Rows, saved.Rows);
    }
}
