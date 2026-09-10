using CotizadorInterno.Web.Models.CopiersMtoV2;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CopiersActivityV2PdfTests
{
    [Theory]
    [InlineData("movement", "Movimiento de equipo")]
    [InlineData("toner", "Entrega de tóner")]
    public async Task ActivityReportIsOneCorporateLetterPageWithTypedFactsAndClientSignature(string kind, string title)
    {
        var model = Model(kind);
        var result = await new CopiersMtoV2ProfessionalPdfBuilder().BuildAsync(model);
        using var document = PdfDocument.Open(result.Content);
        var page = Assert.Single(document.GetPages());
        Assert.Equal(612, page.Width, 2);
        Assert.Equal(792, page.Height, 2);
        var text = ContentOrderTextExtractor.GetText(page);
        foreach (var expected in new[] { title, "ACT-000123", "Cliente destino de muestra SAS", "SERIAL-DEMO-001", "Ana Cliente",
            "ana@example.test", "10/09/2026 08:00 COT", "10/09/2026 09:00 COT", "Conformidad del cliente", "evidencia-rodillos.jpg" })
            Assert.Contains(expected, text);
        Assert.Equal(3, page.GetImages().Count());
        Assert.Equal(2, page.GetImages().Count(x => x.WidthInSamples == 2637 && x.HeightInSamples == 502));
        foreach (var secret in new[] { "Contadores", "Preventivo", "Correctivo", "SECRET-ID", "STOCK-PRIVATE", "UBICACION-INTERNA-DEC0Y", "REGISTRO-INTERNO-DEC0Y" })
            Assert.DoesNotContain(secret, text);
        if (kind == "movement")
        {
            Assert.Contains("Origen", text); Assert.Contains("Destino", text);
            Assert.Contains("Cliente origen de muestra SAS", text);
            Assert.Contains("Reubicación por reorganización de la oficina", text);
            Assert.DoesNotContain("Suministro", text);
        }
        else
        {
            Assert.Contains("Suministro", text); Assert.Contains("Tóner Ricoh MP 501 negro", text);
            Assert.Contains("Cantidad", text); Assert.Contains("2", text);
            Assert.DoesNotContain("Motivo del movimiento", text);
        }
        foreach (var letter in page.Letters)
        {
            Assert.InRange(letter.BoundingBox.Left, 41.8, 570.2);
            Assert.InRange(letter.BoundingBox.Right, 41.8, 570.2);
            Assert.InRange(letter.BoundingBox.Bottom, 121, 676);
            Assert.InRange(letter.BoundingBox.Top, 121, 676);
            Assert.True(letter.FontSize >= 8.19);
        }
        var sampleDirectory = Environment.GetEnvironmentVariable("COPIERS_ACTIVITY_V2_PDF_SAMPLE_DIR");
        if (!string.IsNullOrWhiteSpace(sampleDirectory)) await File.WriteAllBytesAsync(Path.Combine(sampleDirectory, kind + "-sample.pdf"), result.Content);
    }

    [Theory]
    [InlineData("movement")]
    [InlineData("toner")]
    public async Task ActivityPdfIsDeterministicForRetryAndDoesNotPrintServerFinalizationTime(string kind)
    {
        var model = Model(kind);
        var builder = new CopiersMtoV2ProfessionalPdfBuilder();
        var first = await builder.BuildAsync(model);
        model.ServerFinalizedAtUtc = model.ServerFinalizedAtUtc.AddHours(1);
        var retried = await builder.BuildAsync(model);
        Assert.Equal(first.Content, retried.Content);
    }

    [Theory]
    [InlineData("")]
    [InlineData("maintenance")]
    [InlineData("other")]
    public async Task UnknownActivityKindCannotGenerateAMisleadingMaintenanceReport(string kind)
    {
        var exception = await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => new CopiersMtoV2ProfessionalPdfBuilder().BuildAsync(Model(kind)));
        Assert.Equal("activity_kind_invalid", exception.Code);
    }

    private static CopiersMaintenanceV2PdfModel Model(string kind)
    {
        var model = CopiersMtoV2CompactPdfTests.Model();
        model.ServiceReference = "ACT-000123";
        model.FormVersion = CopiersActivityV2Bindings.FormVersion;
        model.ClientName = "Cliente destino de muestra SAS";
        model.WorkPerformed = kind == "movement" ? "Reubicación por reorganización de la oficina. Se entrega el equipo en el destino indicado."
            : "Entrega de dos unidades de tóner negro Ricoh MP 501. El cliente confirma la recepción del suministro.";
        model.CustomerObservations = "Entrega recibida y verificada por el cliente.";
        model.Answers = model.Answers.Where(x => x.Key != "recommendations").Concat(new[] {
            Answer("activity_kind", kind), Answer("origin_client_id", "SECRET-ID"), Answer("origin_client_name", "Cliente origen de muestra SAS"),
            Answer("destination_client_name", model.ClientName), Answer("movement_reason", "Reubicación por reorganización de la oficina"),
            Answer("supply_id", "SECRET-ID"), Answer("supply_name", "Tóner Ricoh MP 501 negro"), Answer("supply_quantity", "2"), Answer("supply_stock_before", "STOCK-PRIVATE")
        }).ToArray();
        return model;
    }
    private static CopiersMaintenanceV2FormAnswerSnapshot Answer(string key, string value) => new() { Key = key, Label = key, Value = value };
}
