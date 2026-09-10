using System.Drawing;
using System.Drawing.Imaging;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CopiersMtoV2CompactPdfTests
{
    [Fact]
    public async Task CompactReportIsOneLetterPageWithOriginalLetterheadSignedFactsAndAttachments()
    {
        var model = Model();
        var rendered = await new CopiersMtoV2ProfessionalPdfBuilder().BuildAsync(model);
        using var pdf = PdfDocument.Open(rendered.Content);
        var page = Assert.Single(pdf.GetPages());
        Assert.Equal(612, page.Width, 2);
        Assert.Equal(792, page.Height, 2);
        var text = ContentOrderTextExtractor.GetText(page);
        foreach (var expected in new[] { "Reporte de mantenimiento", "MTO-DEMO-001", "Cliente de muestra SAS", "SERIAL-DEMO-001",
            "Ricoh MP 501", "Técnico de muestra", "Ana Cliente", "ana@example.test", "Preventivo", "Operativo",
            "10/09/2026 08:00 COT", "10/09/2026 09:00 COT", "31/08/2026", "148.220", "148.245", "4.100", "4.105",
            "Limpieza interna", "Próximo mantenimiento", "Funcionamiento verificado", "Coordinadora administrativa",
            "Conformidad del cliente", "evidencia-rodillos.jpg", "prueba-impresion.png", "Anexos (2)" })
            Assert.Contains(expected, text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, page.GetImages().Count());
        Assert.Equal(2, page.GetImages().Count(x => x.WidthInSamples == 2637 && x.HeightInSamples == 502));
        Assert.DoesNotContain("identificación", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GPS", text);
        Assert.DoesNotContain("UBICACION-INTERNA-DEC0Y", text);
        Assert.DoesNotContain("UTC AUXILIAR", text);
        Assert.DoesNotContain("REGISTRO-INTERNO-DEC0Y", text);
        Assert.True(page.Letters.All(x => x.FontSize >= 8.19));
        AssertGlyphBounds(page);
        var sample = Environment.GetEnvironmentVariable("COPIERS_V2_COMPACT_PDF_SAMPLE_PATH");
        if (!string.IsNullOrWhiteSpace(sample)) await File.WriteAllBytesAsync(sample, rendered.Content);
    }

    [Fact]
    public async Task MaximumNormalNarrativeFitsOnePageWithoutDroppingTheLastWords()
    {
        var model = Model();
        model.WorkPerformed = Fit("Se realizó limpieza, ajuste de alimentación y pruebas de impresión. ", 1000 - " FIN-TRABAJO".Length) + " FIN-TRABAJO";
        model.CustomerObservations = Fit("El cliente revisó el equipo y comprobó la calidad. ", 250 - " FIN-CLIENTE".Length) + " FIN-CLIENTE";
        model.Answers = model.Answers.Select(x => x.Key == "recommendations"
            ? Answer(x.Key, Fit("Mantener el equipo limpio y verificar el papel. ", 250 - " FIN-RECOM".Length) + " FIN-RECOM") : x).ToArray();
        Assert.Equal(1000, model.WorkPerformed.Length);
        Assert.Equal(250, model.CustomerObservations.Length);
        Assert.Equal(250, model.Answers.Single(x => x.Key == "recommendations").Value.Length);
        var rendered = await new CopiersMtoV2ProfessionalPdfBuilder().BuildAsync(model);
        using var pdf = PdfDocument.Open(rendered.Content);
        var page = Assert.Single(pdf.GetPages());
        var text = ContentOrderTextExtractor.GetText(page);
        foreach (var marker in new[] { "FIN-TRABAJO", "FIN-CLIENTE", "FIN-RECOM", "Ana Cliente", "Anexos (2)" }) Assert.Contains(marker, text);
        AssertGlyphBounds(page);
        var sample = Environment.GetEnvironmentVariable("COPIERS_V2_COMPACT_PDF_DENSE_SAMPLE_PATH");
        if (!string.IsNullOrWhiteSpace(sample)) await File.WriteAllBytesAsync(sample, rendered.Content);
    }

    [Fact]
    public async Task UnfittableContentFailsClearlyInsteadOfShrinkingTruncatingOrAddingPages()
    {
        var model = Model();
        model.WorkPerformed = string.Join("\n", Enumerable.Range(0, 60).Select(x => $"Detalle obligatorio {x}"));
        var error = await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => new CopiersMtoV2ProfessionalPdfBuilder().BuildAsync(model));
        Assert.Equal("compact_pdf_content_overflow", error.Code);
        Assert.Contains("una página", error.Message);
    }

    [Fact]
    public async Task OverlongReferenceFailsInsteadOfOverlappingTheReportTitle()
    {
        var model = Model();
        model.ServiceReference = new string('W', 80);
        var error = await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => new CopiersMtoV2ProfessionalPdfBuilder().BuildAsync(model));
        Assert.Equal("compact_pdf_content_overflow", error.Code);
    }

    [Fact]
    public async Task CurrentCounterDateUsesTheExitInBogotaWhenTheVisitCrossesMidnight()
    {
        var model = Model();
        model.Answers = model.Answers.Select(x => x.Key == "service_ended_at_utc"
            ? Answer(x.Key, "2026-09-11T05:15:00Z") : x).ToArray();
        model.DeviceSignedAtUtc = DateTimeOffset.Parse("2026-09-11T05:16:00Z");
        var rendered = await new CopiersMtoV2ProfessionalPdfBuilder().BuildAsync(model);
        using var pdf = PdfDocument.Open(rendered.Content);
        var text = ContentOrderTextExtractor.GetText(Assert.Single(pdf.GetPages()));
        Assert.Contains("Actual 11/09/2026", text);
        Assert.Contains("11/09/2026 00:15 COT", text);
        Assert.Contains("10/09/2026 08:00 COT", text);
    }

    [Fact]
    public async Task MissingPriorCounterIsNotPrintedAsZeroAndEmptyOptionalNotesDoNotWasteSpace()
    {
        var model = Model();
        model.CustomerObservations = "";
        model.Answers = model.Answers.Where(x => x.Key is not ("copies_before" or "scans_before" or "counter_recorded_at" or "recommendations")).ToArray();
        model.Attachments = [];
        var rendered = await new CopiersMtoV2ProfessionalPdfBuilder().BuildAsync(model);
        using var pdf = PdfDocument.Open(rendered.Content);
        var text = ContentOrderTextExtractor.GetText(Assert.Single(pdf.GetPages()));
        Assert.Contains("Sin lectura previa", text);
        Assert.Contains("Sin registro", text);
        Assert.Contains("sin archivos adicionales", text);
        Assert.DoesNotContain("Recomendaciones", text);
        Assert.DoesNotContain("Observaciones del cliente", text);
    }

    [Fact]
    public async Task OlderFormVersionsKeepTheExistingA4ReportAndSingleSignatureImage()
    {
        var model = Model();
        model.FormVersion = "copiers-mto-v2-2026-08-27";
        var rendered = await new CopiersMtoV2ProfessionalPdfBuilder().BuildAsync(model);
        using var pdf = PdfDocument.Open(rendered.Content);
        Assert.Equal(595.28, pdf.GetPage(1).Width, 2);
        Assert.Equal(841.89, pdf.GetPage(1).Height, 2);
        Assert.Single(pdf.GetPages().SelectMany(page => page.GetImages()));
        Assert.Contains("REPORTE DE SERVICIO", ContentOrderTextExtractor.GetText(pdf.GetPage(1)));
    }

    private static void AssertGlyphBounds(UglyToad.PdfPig.Content.Page page)
    {
        foreach (var letter in page.Letters)
        {
            Assert.InRange(letter.BoundingBox.Left, 41.8, 570.2);
            Assert.InRange(letter.BoundingBox.Right, 41.8, 570.2);
            Assert.InRange(letter.BoundingBox.Bottom, 121, 676);
            Assert.InRange(letter.BoundingBox.Top, 121, 676);
        }
    }

    private static string Fit(string text, int length) => string.Concat(Enumerable.Repeat(text, length / text.Length + 1))[..length];
    private static CopiersMaintenanceV2FormAnswerSnapshot Answer(string key, string value) => new() { Key = key, Label = key, Value = value };
    private static CopiersMaintenanceV2PdfModel Model() => new()
    {
        ServiceReference = "MTO-DEMO-001", RecordId = "11111111-2222-3333-4444-555555555555", ClientName = "Cliente de muestra SAS",
        CustomerContactName = "Ana Cliente", EquipmentSerial = "SERIAL-DEMO-001", Title = "Mantenimiento de equipo",
        ServiceDate = new(2026, 9, 10), TechnicianName = "Técnico de muestra", FormVersion = CopiersMtoV2CompactCapture.FormVersion,
        WorkPerformed = "Limpieza interna del equipo y del alimentador automático. Revisión de rodillos, ajuste de bandejas y limpieza del cristal de escaneo. Se realizaron pruebas de impresión y digitalización; la calidad y la alimentación quedaron verificadas con el cliente.",
        CustomerObservations = "Funcionamiento verificado. El equipo se recibe operativo.", SignerName = "Ana Cliente", SignerRole = "Coordinadora administrativa",
        DeviceSignedAtUtc = DateTimeOffset.Parse("2026-09-10T14:02:00Z"), ServerFinalizedAtUtc = DateTimeOffset.Parse("2026-09-10T14:02:30Z"),
        SignatureContent = SampleSignature(), SignatureContentType = "image/jpeg",
        Answers = [ Answer("equipment_reference", "Ricoh MP 501"), Answer("onsite_email", "ana@example.test"), Answer("maintenance_type", "Preventivo"),
            Answer("service_result", "Operativo"), Answer("service_started_at_utc", "2026-09-10T13:00:00Z"), Answer("service_ended_at_utc", "2026-09-10T14:00:00Z"),
            Answer("service_started_at", "UTC AUXILIAR"), Answer("counter_recorded_at", "2026-08-31"), Answer("copies_before", "148220"), Answer("copies_after", "148245"),
            Answer("scans_before", "4100"), Answer("scans_after", "4105"), Answer("recommendations", "Próximo mantenimiento en el periodo programado. Mantener el papel seco y limpiar el cristal con un paño suave."),
            Answer("counter_record_id", "REGISTRO-INTERNO-DEC0Y"), Answer("internal_location", "UBICACION-INTERNA-DEC0Y") ],
        Attachments = [ new() { FileName = "evidencia-rodillos.jpg", Size = 210 * 1024, Sha256 = new string('A', 64) },
            new() { FileName = "prueba-impresion.png", Size = 190 * 1024, Sha256 = new string('B', 64) } ]
    };

#pragma warning disable CA1416
    private static byte[] SampleSignature()
    {
        using var bitmap = new Bitmap(480, 120);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        using var font = new Font("Segoe UI", 27, FontStyle.Italic);
        graphics.DrawString("Firma de muestra", font, Brushes.MidnightBlue, new PointF(15, 25));
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Jpeg);
        return output.ToArray();
    }
#pragma warning restore CA1416
}
