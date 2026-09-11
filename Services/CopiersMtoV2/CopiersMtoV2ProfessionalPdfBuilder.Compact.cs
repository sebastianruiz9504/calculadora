using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CotizadorInterno.Web.Models.CopiersMtoV2;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

public sealed partial class CopiersMtoV2ProfessionalPdfBuilder
{
    private const double CompactWidth = 612;
    private const double CompactHeight = 792;
    private const double CompactLeft = 42;
    private const double CompactContentWidth = CompactWidth - CompactLeft * 2;
    private const double CompactBottom = 671;
    private const double CompactFontSize = 9.3;
    private const double CompactLeading = 11.5;
    private static readonly PdfColor CompactBlack = PdfColor.FromHex("#111827");
    private static readonly Lazy<IReadOnlyList<PdfEmbeddedImage>> Letterhead = new(() => [
        ReadLetterhead("Header", "A48948C455A6D2D7C6E95770839B46159CB6AF81647C49A212CC764BC1D325AF"),
        ReadLetterhead("Footer", "A8C7092557A659B76DA72C567ACD2D3385FEDA567E047585A4C5A8EEA33A43CF")
    ]);

    private static CopiersMaintenanceV2RenderedPdf BuildCompact(CopiersMaintenanceV2PdfModel model)
    {
        var number = BuildReportNumber(model);
        var signature = PdfJpeg.TryCreate(model.SignatureContent, model.SignatureContentType)
            ?? throw new CopiersMaintenanceV2ValidationException("signature_format_not_renderable",
                "La firma debe recibirse como imagen JPEG para incluirla en el PDF.");
        var extras = CopiersMultiEquipment.Read(model.Answers);
        if (extras.Count == 0)
            return new() { FileName = $"{number}-Reporte-Servicio-Firmado.pdf", Content = new CompactLayout(model, signature, number).Build() };
        var pages = new List<PdfCanvas> { new CompactLayout(model, signature, number, 1, extras.Count + 1).BuildPage() };
        foreach (var (item, index) in extras.Select((item, index) => (item, index)))
        {
            var replacements = new Dictionary<string, string> {
                ["equipment_reference"] = item.Reference, ["counter_recorded_at"] = item.CounterDate,
                ["copies_before"] = item.CopiesBefore?.ToString(CultureInfo.InvariantCulture) ?? "", ["copies_after"] = item.CopiesAfter?.ToString(CultureInfo.InvariantCulture) ?? "",
                ["scans_before"] = item.ScansBefore?.ToString(CultureInfo.InvariantCulture) ?? "", ["scans_after"] = item.ScansAfter?.ToString(CultureInfo.InvariantCulture) ?? ""
            };
            var answers = model.Answers.Where(x => !replacements.ContainsKey(x.Key)).ToList();
            answers.AddRange(replacements.Select(x => new CopiersMaintenanceV2FormAnswerSnapshot { Key = x.Key, Value = x.Value }));
            var perEquipment = new CopiersMaintenanceV2PdfModel {
                ServiceReference = model.ServiceReference, RecordId = model.RecordId, ClientName = model.ClientName,
                CustomerContactName = model.CustomerContactName, EquipmentSerial = item.Serial, Title = model.Title,
                ServiceDate = model.ServiceDate, TechnicianName = model.TechnicianName, FormVersion = model.FormVersion,
                Answers = answers, WorkPerformed = item.WorkPerformed, CustomerObservations = model.CustomerObservations,
                SignerName = model.SignerName, SignerRole = model.SignerRole, DeviceSignedAtUtc = model.DeviceSignedAtUtc,
                ServerFinalizedAtUtc = model.ServerFinalizedAtUtc, SignatureContent = model.SignatureContent,
                SignatureContentType = model.SignatureContentType, Attachments = model.Attachments
            };
            pages.Add(new CompactLayout(perEquipment, signature, number, index + 2, extras.Count + 1).BuildPage());
        }
        return new() { FileName = $"{number}-Reporte-Servicio-Firmado.pdf", Content = PdfBinaryWriter.Build(pages, signature, number, CompactWidth, CompactHeight, Letterhead.Value) };
    }

    private sealed class CompactLayout(CopiersMaintenanceV2PdfModel model, PdfJpeg signature, string number, int pageNumber = 1, int equipmentCount = 1)
    {
        private readonly PdfCanvas _page = new();
        private double _top = 121;

        public byte[] Build() => PdfBinaryWriter.Build([BuildPage()], signature, number, CompactWidth, CompactHeight, Letterhead.Value);

        public PdfCanvas BuildPage()
        {
            var activityKind = model.FormVersion == CopiersActivityV2Bindings.FormVersion ? Answer("activity_kind").ToLowerInvariant() : "maintenance";
            if (model.FormVersion == CopiersActivityV2Bindings.FormVersion && activityKind is not ("movement" or "toner"))
                throw new CopiersMaintenanceV2ValidationException("activity_kind_invalid", "El tipo de actividad firmada no es válido.");
            var title = activityKind switch
            {
                "maintenance" => "Reporte de mantenimiento",
                "movement" => Answer("operation_kind") switch { "delivery" or "receipt" => "Certificado de entrega", "replacement" => "Certificado de cambio", "withdrawal" => "Certificado de retiro", _ => "Movimiento de equipo" },
                "toner" => "Entrega de tóner",
                _ => throw new CopiersMaintenanceV2ValidationException("activity_kind_invalid", "El tipo de actividad firmada no es válido.")
            };
            if (EstimateWidth(number, 11, true) + EstimateWidth(title, 15, true) + 18 > CompactContentWidth)
                throw Overflow();
            foreach (var image in Letterhead.Value)
            {
                var height = CompactWidth * image.Height / image.Width;
                _page.DrawImage(image.Name, 0, image.Name == "Header" ? CompactHeight - height : 0, CompactWidth, height);
            }
            Text(title, CompactLeft, _top, 15, true);
            _page.DrawTextRight(number, CompactWidth - CompactLeft, CompactHeight - _top - 15, 11, true, CompactBlack);
            _top += 24;

            FullField("Cliente", model.ClientName);
            PairFields(Answer("operation_kind") is "withdrawal" or "replacement" ? "Retirado" : equipmentCount > 1 ? $"Serial {pageNumber}/{equipmentCount}" : "Serial", model.EquipmentSerial, "Referencia", Answer("equipment_reference"));
            PairFields("Técnico", model.TechnicianName, "Atendió", model.CustomerContactName);
            FullField("Correo", Answer("onsite_email"));
            if (activityKind == "maintenance")
                PairFields("Tipo", Answer("maintenance_type"), "Resultado", Answer("service_result"));
            PairFields("Entrada", VisitInstant("service_started_at_utc", "service_started_at"),
                "Salida", VisitInstant("service_ended_at_utc", "service_ended_at"));
            _top += 7;

            if (activityKind == "maintenance") DrawCounters();
            else if (activityKind == "movement")
            {
                if (Answer("operation_kind").Length > 0)
                {
                    FullField("Estado", Answer("equipment_condition"));
                    FullField("Accesorios", Answer("equipment_accessories"));
                    if (Answer("operation_kind") == "replacement")
                    {
                        PairFields("Entregado", Answer("replacement_serial"), "Referencia", Answer("replacement_reference"));
                        FullField("Estado", Answer("replacement_condition"));
                        FullField("Accesorios", Answer("replacement_accessories"));
                    }
                }
                else
                {
                    FullField("Origen", Answer("origin_client_name"));
                    FullField("Destino", Answer("destination_client_name"));
                }
                _top += 7;
            }
            else
            {
                FullField("Suministro", Answer("supply_name"));
                FullField("Cantidad", Answer("supply_quantity"));
                _top += 7;
            }
            Paragraph(activityKind == "maintenance" ? "Trabajo realizado" : activityKind == "movement" ? "Motivo del movimiento" : "Detalle de la entrega",
                model.WorkPerformed, required: true);
            Paragraph("Recomendaciones", Answer("recommendations"));
            Paragraph("Observaciones del cliente", model.CustomerObservations);
            DrawSignature(activityKind);
            DrawAttachments();
            EnsureSpace(0);
            return _page;
        }

        private string Answer(string key) => model.Answers.FirstOrDefault(x => x.Key == key)?.Value?.Trim() ?? "";

        private string VisitInstant(string canonicalKey, string displayKey)
        {
            var text = Answer(canonicalKey);
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
                ? FormatBogota(value) : Answer(displayKey);
        }

        private void FullField(string label, string? value)
        {
            var height = Field(label, value, CompactLeft, CompactContentWidth, draw: false);
            EnsureSpace(height);
            Field(label, value, CompactLeft, CompactContentWidth, draw: true);
            _top += height;
        }

        private void PairFields(string leftLabel, string? left, string rightLabel, string? right)
        {
            const double gap = 16;
            var width = (CompactContentWidth - gap) / 2;
            var height = Math.Max(Field(leftLabel, left, CompactLeft, width, false),
                Field(rightLabel, right, CompactLeft + width + gap, width, false));
            EnsureSpace(height);
            Field(leftLabel, left, CompactLeft, width, true);
            Field(rightLabel, right, CompactLeft + width + gap, width, true);
            _top += height;
        }

        private double Field(string label, string? value, double x, double width, bool draw)
        {
            const double labelWidth = 50;
            var lines = Wrap(value, width - labelWidth, CompactFontSize);
            var height = lines.Count * CompactLeading + 3;
            if (draw)
            {
                Text(label, x, _top, 8.5, true);
                DrawLines(lines, x + labelWidth, _top, CompactFontSize, CompactLeading);
            }
            return height;
        }

        private void DrawCounters()
        {
            var date = Answer("counter_recorded_at");
            var priorLabel = DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var prior)
                ? prior.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "Sin lectura previa";
            EnsureSpace(68);
            Text("Contadores", CompactLeft, _top, 10, true);
            _page.DrawTextRight($"Lectura anterior: {priorLabel}", CompactWidth - CompactLeft,
                CompactHeight - _top - 8.2, 8.2, false, Muted);
            _top += 16;
            var widths = new[] { 234d, 147d, 147d };
            var readingDate = DateTimeOffset.TryParse(Answer("service_ended_at_utc"), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var ended)
                ? DateOnly.FromDateTime(ended.ToOffset(TimeSpan.FromHours(-5)).DateTime) : model.ServiceDate;
            var headings = new[] { "Lectura acumulada", "Anterior", $"Actual {readingDate:dd/MM/yyyy}" };
            CounterRow(headings, widths, heading: true);
            CounterRow(["Impresiones", Counter("copies_before"), Counter("copies_after")], widths, heading: false);
            CounterRow(["Escaneos", Counter("scans_before"), Counter("scans_after")], widths, heading: false);
            _top += 8;
        }

        private string Counter(string key) => long.TryParse(Answer(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value.ToString("N0", CultureInfo.GetCultureInfo("es-CO")) : "Sin registro";

        private void CounterRow(IReadOnlyList<string> values, IReadOnlyList<double> widths, bool heading)
        {
            const double height = 17;
            EnsureSpace(height);
            var x = CompactLeft;
            for (var i = 0; i < values.Count; i++)
            {
                _page.FillRectangle(x, CompactHeight - _top - height, widths[i], height, heading ? Pale : White);
                _page.StrokeRectangle(x, CompactHeight - _top - height, widths[i], height, Line, .45);
                Text(values[i], x + 7, _top + 3, heading ? 8.2 : CompactFontSize, heading);
                x += widths[i];
            }
            _top += height;
        }

        private void Paragraph(string label, string? value, bool required = false)
        {
            if (!required && string.IsNullOrWhiteSpace(value)) return;
            var lines = Wrap(value, CompactContentWidth, CompactFontSize);
            EnsureSpace(15 + lines.Count * CompactLeading + 7);
            Text(label, CompactLeft, _top, 10, true);
            _top += 15;
            DrawLines(lines, CompactLeft, _top, CompactFontSize, CompactLeading);
            _top += lines.Count * CompactLeading + 7;
        }

        private void DrawSignature(string activityKind)
        {
            const double textWidth = 343;
            const double imageMaxWidth = 164;
            const double imageMaxHeight = 46;
            var consent = activityKind switch
            {
                "movement" => "Revisé los datos del movimiento del equipo, su origen y destino. Mi firma deja constancia de la atención y de la información consignada.",
                "toner" => "Revisé el suministro y la cantidad relacionados en esta entrega. Mi firma deja constancia de la recepción y de la información consignada.",
                _ => "Revisé este reporte y recibí explicación del trabajo realizado. Mi firma deja constancia de la atención y de la información consignada."
            };
            if (activityKind == "movement") consent = Answer("operation_kind") switch
            {
                "delivery" or "receipt" => "Recibí el equipo y los accesorios relacionados, en el estado indicado.",
                "withdrawal" => "Entregué al técnico el equipo y los accesorios relacionados para su retiro, en el estado indicado.",
                "replacement" => "Se retiró el equipo anterior y recibí el equipo de reemplazo con los accesorios y estados relacionados.",
                _ => consent
            };
            var consentLines = Wrap(consent, textWidth, 8.2);
            if (equipmentCount > 1) consentLines = Wrap($"Revisé los {equipmentCount} equipos relacionados. Esta firma corresponde a la visita completa y deja constancia de la atención y del trabajo explicado.", textWidth, 8.2);
            var nameLines = Wrap($"{model.SignerName} - {model.SignerRole}".Trim(' ', '-'), textWidth, 9);
            var height = 19 + consentLines.Count * 10 + nameLines.Count * 11 + 15;
            height = Math.Max(78, height);
            EnsureSpace(height + 7);
            Text("Conformidad del cliente", CompactLeft, _top, 10, true);
            DrawLines(consentLines, CompactLeft, _top + 17, 8.2, 10);
            var nameTop = _top + 17 + consentLines.Count * 10 + 3;
            DrawLines(nameLines, CompactLeft, nameTop, 9, 11);
            Text($"Firma: {FormatBogota(model.DeviceSignedAtUtc)}", CompactLeft, nameTop + nameLines.Count * 11 + 3, 8.2, false);
            var scale = Math.Min(imageMaxWidth / signature.Width, imageMaxHeight / signature.Height);
            var width = signature.Width * scale;
            var imageHeight = signature.Height * scale;
            var imageX = CompactWidth - CompactLeft - imageMaxWidth;
            _page.DrawImage("Sig", imageX, CompactHeight - _top - 17 - imageHeight, width, imageHeight);
            _page.DrawLine(imageX, CompactHeight - _top - 17 - imageMaxHeight - 3,
                imageX + imageMaxWidth, CompactHeight - _top - 17 - imageMaxHeight - 3, Line, .6);
            _top += height + 7;
        }

        private void DrawAttachments()
        {
            var notice = model.Attachments.Count == 0 ? "Anexos: sin archivos adicionales."
                : $"Anexos ({model.Attachments.Count}): " + string.Join("; ", model.Attachments.Select(x => $"{x.FileName} ({FormatBytes(x.Size)})"))
                    + ". Se entregan con el correo de este reporte.";
            var lines = Wrap(notice, CompactContentWidth, 8.2);
            EnsureSpace(lines.Count * 10);
            DrawLines(lines, CompactLeft, _top, 8.2, 10);
            _top += lines.Count * 10;
        }

        private void EnsureSpace(double height)
        {
            if (_top + height > CompactBottom)
                throw Overflow();
        }

        private CopiersMaintenanceV2ValidationException Overflow() => new("compact_pdf_content_overflow",
            model.FormVersion == CopiersActivityV2Bindings.FormVersion
                ? "La información de la actividad supera el espacio de una página. Resume el detalle o las observaciones sin omitir datos importantes y vuelve a firmar."
                : "La información del mantenimiento supera el espacio de una página. Resume el trabajo o las observaciones sin omitir datos importantes y vuelve a firmar.");

        private void DrawLines(IReadOnlyList<string> lines, double x, double top, double size, double leading)
        {
            for (var index = 0; index < lines.Count; index++) Text(lines[index], x, top + index * leading, size, false);
        }

        private void Text(string value, double x, double top, double size, bool bold) =>
            _page.DrawText(value, x, CompactHeight - top - size, size, bold, CompactBlack);
    }

    private sealed record PdfEmbeddedImage(string Name, int Width, int Height, byte[] Content);

    private static PdfEmbeddedImage ReadLetterhead(string name, string sha256)
    {
        using var resource = typeof(CopiersMtoV2ProfessionalPdfBuilder).Assembly
            .GetManifestResourceStream($"CopiersMtoV2.Letterhead.{name}.png")
            ?? throw new InvalidOperationException("No se encontró el membrete institucional del reporte.");
        using var output = new MemoryStream();
        resource.CopyTo(output);
        var png = output.ToArray();
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(png)), sha256, StringComparison.Ordinal))
            throw new InvalidOperationException("La integridad del membrete institucional no coincide.");
        // The retained artwork is fixed RGB, 8-bit, non-interlaced PNG. Embed its
        // original compressed scanlines directly; no JPEG conversion or pixel loss.
        var width = 0;
        var height = 0;
        using var compressed = new MemoryStream();
        for (var offset = 8; offset + 12 <= png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            if (length < 0 || (long)offset + length + 12 > png.Length) throw new InvalidOperationException("Membrete PNG inválido.");
            var kind = Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png.AsSpan(offset + 8, length);
            if (kind == "IHDR")
            {
                if (length != 13 || data[8] != 8 || data[9] != 2 || data[10] != 0 || data[11] != 0 || data[12] != 0)
                    throw new InvalidOperationException("El formato del membrete PNG no es compatible.");
                width = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
                height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4));
            }
            if (kind == "IDAT") compressed.Write(data);
            offset += length + 12;
        }
        if (width != 2637 || height != 502 || compressed.Length == 0) throw new InvalidOperationException("Las dimensiones del membrete institucional no coinciden.");
        return new(name, width, height, compressed.ToArray());
    }
}
