using System.Text.Json;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CopiersMultiEquipmentTests
{
    private const string Primary = "11111111-1111-4111-8111-111111111111";
    private const string Extra = "22222222-2222-4222-8222-222222222222";
    private const string Customer = "33333333-3333-4333-8333-333333333333";
    private static CopiersMultiEquipment.Item Item() => new() { EquipmentId = Extra, Serial = "SECOND-SERIAL", Reference = "Ricoh", WorkPerformed = "Trabajo exclusivamente segundo equipo", CopiesAfter = 250, ScansAfter = 30 };
    private static CopiersMaintenanceV2FinalizeMultipartRequestDto Request(params CopiersMultiEquipment.Item[] items) => new() {
        FormVersion = CopiersMtoV2CompactCapture.FormVersion, AdditionalEquipmentJson = JsonSerializer.Serialize(items)
    };
    private static CopiersMaintenanceV2DraftRequestDto Draft() => new() { ClientId = Customer, EquipmentId = Primary, EquipmentSerial = "PRIMARY" };
    [Fact] public void AuthorizationRejectsOtherCustomersAndRepeatedPrimary()
    {
        var request = Request(Item());
        Assert.Throws<CopiersMaintenanceV2ValidationException>(() => CopiersMultiEquipment.Authorize(request, Draft(), [(Extra, Primary, "SECOND-SERIAL", "Ricoh", false)]));
        var item = Item(); item.EquipmentId = Primary;
        Assert.Throws<CopiersMaintenanceV2ValidationException>(() => CopiersMultiEquipment.Authorize(Request(item), Draft(), [(Primary, Customer, "SECOND-SERIAL", "Ricoh", false)]));
        CopiersMultiEquipment.Authorize(request, Draft(), [(Extra, Customer, "SECOND-SERIAL", "Ricoh", false)]);
    }
    [Fact] public void StockAndChangedSerialReferenceAreRejectedBeforeReceipt()
    {
        foreach (var row in new[] { (Extra, Customer, "SECOND-SERIAL", "Ricoh", true), (Extra, Customer, "CHANGED", "Ricoh", false), (Extra, Customer, "SECOND-SERIAL", "CHANGED", false) })
            Assert.Throws<CopiersMaintenanceV2ValidationException>(() => CopiersMultiEquipment.Authorize(Request(Item()), Draft(), [row]));
    }
    [Fact] public void RequiredPerSerialDetailsAndCounterRangesAreValidated()
    {
        var missing = Item(); missing.WorkPerformed = " ";
        Assert.Throws<CopiersMaintenanceV2ValidationException>(() => CopiersMultiEquipment.Parse(Request(missing)));
        var negative = Item(); negative.CopiesAfter = -1;
        Assert.Throws<CopiersMaintenanceV2ValidationException>(() => CopiersMultiEquipment.Parse(Request(negative)));
        var history = Item(); history.CopiesBefore = 20;
        Assert.Throws<CopiersMaintenanceV2ValidationException>(() => CopiersMultiEquipment.Parse(Request(history)));
        Assert.Throws<CopiersMaintenanceV2ValidationException>(() => CopiersMultiEquipment.Parse(Request(Item(), Item())));
        Assert.Throws<CopiersMaintenanceV2ValidationException>(() => CopiersMultiEquipment.Parse(Request(Enumerable.Range(0, 10).Select(_ => Item()).ToArray())));
        var movement = Request(Item()); movement.ActivityKind = "movement";
        Assert.Throws<CopiersMaintenanceV2ValidationException>(() => CopiersMultiEquipment.Parse(movement));
    }
    [Fact] public void CounterIdentityIsStablePerTicketAndEquipmentWithoutChangingLegacy()
    {
        var old = DataverseService.BuildCopiersMtoV2CounterRecordId(Primary);
        var extra = DataverseService.BuildCopiersMtoV2CounterRecordId(Primary, Extra);
        Assert.Equal(old, DataverseService.BuildCopiersMtoV2CounterRecordId(Primary, ""));
        Assert.NotEqual(old, extra);
        Assert.Equal(extra, DataverseService.BuildCopiersMtoV2CounterRecordId(Primary.ToUpperInvariant(), Extra.ToUpperInvariant()));
        Assert.NotEqual(extra, DataverseService.BuildCopiersMtoV2CounterRecordId(Primary, Customer));
    }
    [Fact] public void CanonicalSnapshotRoundTripsEachSerialAndItsOwnCounters()
    {
        var record = new CopiersMaintenanceV2DraftRecord { EquipmentId = Primary, EquipmentSerial = "PRIMARY", RecordId = Primary, ClientId = Customer };
        var answers = CopiersMultiEquipment.Append(Request(Item()), record, []);
        var item = Assert.Single(CopiersMultiEquipment.Read(answers));
        Assert.Equal("SECOND-SERIAL", item.Serial);
        var command = CopiersMultiEquipment.Counter(item, record, DateTimeOffset.UtcNow, new string('a', 64));
        Assert.Equal(Extra, command.AdditionalEquipmentScope); Assert.Equal(250, command.CopiesCounter);
        Assert.Contains(answers, x => x.Key == "equipment_detail_2" && x.Value.Contains(item.WorkPerformed));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultiReportHasOneLetterheadPagePerEquipmentAndOriginalSignatureOnEach(bool dense)
    {
        var model = CopiersMtoV2CompactPdfTests.Model();
        var item = Item();
        if (dense) {
            static string Fill(string value, int length) => string.Concat(Enumerable.Repeat(value, length / value.Length + 1))[..length];
            model.WorkPerformed = Fill(model.WorkPerformed + " ", 1000);
            item.WorkPerformed = Fill(item.WorkPerformed + " ", 1000);
            model.CustomerObservations = Fill(model.CustomerObservations + " ", 250);
            model.Answers = model.Answers.Select(x => x.Key == "recommendations" ? new CopiersMaintenanceV2FormAnswerSnapshot { Key = x.Key, Value = Fill(x.Value + " ", 250) } : x).ToArray();
        }
        model.Answers = CopiersMultiEquipment.Append(Request(item), new() { EquipmentId = Primary, EquipmentSerial = model.EquipmentSerial }, model.Answers);
        var rendered = await new CopiersMtoV2ProfessionalPdfBuilder().BuildAsync(model);
        using var pdf = PdfDocument.Open(rendered.Content);
        Assert.Equal(2, pdf.NumberOfPages);
        var first = ContentOrderTextExtractor.GetText(pdf.GetPage(1));
        var second = ContentOrderTextExtractor.GetText(pdf.GetPage(2));
        Assert.Contains(model.EquipmentSerial, first); Assert.DoesNotContain("SECOND-SERIAL", first);
        Assert.Contains("SECOND-SERIAL", second); Assert.Contains("Trabajo exclusivamente segundo equipo", second);
        Assert.DoesNotContain("Limpieza interna", second);
        foreach (var page in pdf.GetPages()) {
            Assert.Equal(3, page.GetImages().Count()); Assert.Contains("Ana Cliente", ContentOrderTextExtractor.GetText(page));
            Assert.Contains("visita completa", ContentOrderTextExtractor.GetText(page));
            Assert.All(page.Letters, x => { Assert.InRange(x.BoundingBox.Left, 41.8, 570.2); Assert.InRange(x.BoundingBox.Right, 41.8, 570.2); Assert.InRange(x.BoundingBox.Bottom, 121, 676); });
        }
        var sample = Environment.GetEnvironmentVariable("COPIERS_MULTI_PDF_SAMPLE");
        if (!string.IsNullOrWhiteSpace(sample)) await File.WriteAllBytesAsync(dense ? sample.Replace(".pdf", "-dense.pdf") : sample, rendered.Content);
    }
}
