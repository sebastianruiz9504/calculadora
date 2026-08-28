using System.Globalization;
using System.Text;
using CotizadorInterno.Web.Models.CopiersMtoV2;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

/// <summary>
/// Dependency-free A4 renderer for the signed customer report. Its input type
/// intentionally cannot carry internal location data.
/// </summary>
public sealed class CopiersMtoV2ProfessionalPdfBuilder : ICopiersMtoV2PdfBuilder
{
    private const double PageWidth = 595.28;
    private const double PageHeight = 841.89;
    private const double Left = 48;
    private const double Right = 48;
    private const double ContentWidth = PageWidth - Left - Right;
    private const double HeaderBottom = 112;
    private const double FooterTop = 806;

    private static readonly PdfColor Navy = PdfColor.FromHex("#0B2347");
    private static readonly PdfColor Green = PdfColor.FromHex("#43B978");
    private static readonly PdfColor Ink = PdfColor.FromHex("#162235");
    private static readonly PdfColor Muted = PdfColor.FromHex("#64748B");
    private static readonly PdfColor Line = PdfColor.FromHex("#DCE5EE");
    private static readonly PdfColor Pale = PdfColor.FromHex("#F4F7FA");
    private static readonly PdfColor SoftGreen = PdfColor.FromHex("#EAF8F0");
    private static readonly PdfColor White = PdfColor.FromHex("#FFFFFF");

    public Task<CopiersMaintenanceV2RenderedPdf> BuildAsync(
        CopiersMaintenanceV2PdfModel model,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ct.ThrowIfCancellationRequested();
        var reportNumber = BuildReportNumber(model);
        var signature = PdfJpeg.TryCreate(model.SignatureContent, model.SignatureContentType)
            ?? throw new CopiersMaintenanceV2ValidationException(
                "signature_format_not_renderable",
                "La firma debe recibirse como imagen JPEG para incluirla en el PDF.");

        var document = new PdfLayoutDocument(reportNumber, signature);
        RenderSummary(document, model);
        RenderTechnicalDetail(document, model);
        RenderEvidenceAndSignature(document, model);
        var content = document.Build();

        return Task.FromResult(new CopiersMaintenanceV2RenderedPdf
        {
            FileName = $"{reportNumber}-Reporte-Servicio-Firmado.pdf",
            Content = content
        });
    }

    private static void RenderSummary(PdfLayoutDocument document, CopiersMaintenanceV2PdfModel model)
    {
        document.DrawStatus("REPORTE CERRADO Y FIRMADO", "Resultado consignado por el técnico y aceptado por el cliente");
        document.Spacer(14);

        document.DrawInfoGrid(new[]
        {
            new PdfInfoCell("Cliente", model.ClientName),
            new PdfInfoCell("Equipo", model.EquipmentSerial),
            new PdfInfoCell("Contacto", model.CustomerContactName),
            new PdfInfoCell("Fecha del servicio", FormatDate(model.ServiceDate)),
            new PdfInfoCell("Técnico", model.TechnicianName),
            new PdfInfoCell("Reporte / versión", $"{document.ReportNumber} · {model.FormVersion}")
        });
        document.Spacer(18);

        document.Section("01 · Información del servicio");
        document.DetailBlock("Asunto", model.Title, shaded: false);
        foreach (var answer in model.Answers.OrderBy(item => item.SortOrder).ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(answer.Value))
                continue;
            document.DetailBlock(answer.Label, answer.Value, shaded: answer.SortOrder % 2 == 0);
        }
    }

    private static void RenderTechnicalDetail(PdfLayoutDocument document, CopiersMaintenanceV2PdfModel model)
    {
        document.Spacer(12);
        document.Section("02 · Trabajo y resultado");
        document.DetailBlock("Trabajo realizado", model.WorkPerformed, shaded: false);
        if (!string.IsNullOrWhiteSpace(model.CustomerObservations))
            document.DetailBlock("Observaciones del cliente", model.CustomerObservations, shaded: true);
    }

    private static void RenderEvidenceAndSignature(PdfLayoutDocument document, CopiersMaintenanceV2PdfModel model)
    {
        document.Spacer(12);
        var manifestHeight = 23 + (Math.Max(1, model.Attachments.Count) * 25);
        document.Section("03 · Evidencias y conformidad", manifestHeight);
        document.DrawAttachmentManifest(model.Attachments);
        document.Spacer(14);

        const string consent =
            "Declaro que revisé la información anterior, recibí explicación del trabajo realizado y firmo como constancia de atención. " +
            "La firma manuscrita corresponde a esta versión cerrada del reporte; cualquier cambio posterior exige una nueva firma.";
        document.DrawSignature(
            consent,
            model.SignerName,
            model.SignerRole,
            model.DeviceSignedAtUtc,
            model.ServerFinalizedAtUtc,
            model.FormVersion);
        document.Spacer(12);
        document.SmallNote("Documento de servicio emitido por Digital Tech Copiers SAS para el cliente y el equipo identificados en este reporte.");
    }

    private static string BuildReportNumber(CopiersMaintenanceV2PdfModel model)
    {
        var compactId = Guid.TryParse(model.RecordId, out var parsed)
            ? parsed.ToString("N")[..8].ToUpperInvariant()
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(model.RecordId ?? "")))[..8];
        var date = model.ServiceDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : model.ServiceDate;
        return $"MTO-{date:yyyyMMdd}-{compactId}";
    }

    private static string FormatDate(DateOnly value) =>
        value == default
            ? "No registrada"
            : value.ToDateTime(TimeOnly.MinValue).ToString("d 'de' MMMM 'de' yyyy", CultureInfo.GetCultureInfo("es-CO"));

    private sealed class PdfLayoutDocument
    {
        private readonly List<PdfCanvas> _pages = new();
        private readonly PdfJpeg _signature;
        private PdfCanvas _page = null!;
        private double _top;

        public PdfLayoutDocument(string reportNumber, PdfJpeg signature)
        {
            ReportNumber = reportNumber;
            _signature = signature;
            NewPage();
        }

        public string ReportNumber { get; }

        public void DrawStatus(string leftText, string rightText)
        {
            EnsureSpace(30);
            var y = ToPdfY(_top, 23);
            _page.FillRectangle(Left, y, ContentWidth, 23, SoftGreen);
            _page.StrokeRectangle(Left, y, ContentWidth, 23, PdfColor.FromHex("#B8E5CC"), 0.6);
            _page.DrawText(leftText, Left + 9, y + 8, 8.4, true, PdfColor.FromHex("#167548"));
            _page.DrawTextRight(rightText, PageWidth - Right - 9, y + 8, 8.4, true, PdfColor.FromHex("#167548"));
            _top += 23;
        }

        public void DrawInfoGrid(IReadOnlyList<PdfInfoCell> cells)
        {
            const double gap = 0;
            var width = (ContentWidth - gap) / 2;
            for (var index = 0; index < cells.Count; index += 2)
            {
                var leftCell = cells[index];
                var rightCell = index + 1 < cells.Count ? cells[index + 1] : new PdfInfoCell("", "");
                var leftLines = Wrap(leftCell.Value, width - 18, 9.4);
                var rightLines = Wrap(rightCell.Value, width - 18, 9.4);
                var height = 19 + (Math.Max(leftLines.Count, rightLines.Count) * 12.2) + 7;
                EnsureSpace(height);
                var y = ToPdfY(_top, height);
                DrawInfoCell(Left, y, width, height, leftCell, leftLines);
                DrawInfoCell(Left + width + gap, y, width, height, rightCell, rightLines);
                _top += height;
            }
        }

        public void Section(string title, double minimumFollowingSpace = 0)
        {
            EnsureSpace(25 + minimumFollowingSpace);
            _page.DrawText(title, Left + 6, ToPdfY(_top, 16) + 2, 11.4, true, Navy);
            _top += 23;
        }

        public void DetailBlock(string label, string? text, bool shaded)
        {
            var safeText = string.IsNullOrWhiteSpace(text) ? "No registrado" : text.Trim();
            var lines = Wrap(safeText, ContentWidth - 26, 9.1);
            const double headerHeight = 25;
            const double lineHeight = 12.1;
            const double gap = 6;
            var offset = 0;
            var continuation = false;

            while (offset < lines.Count)
            {
                var available = FooterTop - 14 - _top - gap;
                var maxLines = (int)Math.Floor((available - headerHeight) / lineHeight);
                if (maxLines < Math.Min(2, lines.Count - offset))
                {
                    NewPage();
                    available = FooterTop - 14 - _top - gap;
                    maxLines = (int)Math.Floor((available - headerHeight) / lineHeight);
                }
                if (maxLines < 1)
                    throw new InvalidOperationException("El área imprimible del reporte es insuficiente.");

                var take = Math.Min(maxLines, lines.Count - offset);
                var segment = lines.Skip(offset).Take(take).ToArray();
                var height = headerHeight + (segment.Length * lineHeight);
                var y = ToPdfY(_top, height);
                _page.FillRectangle(Left, y, ContentWidth, height, shaded ? Pale : White);
                _page.StrokeRectangle(Left, y, ContentWidth, height, Line, 0.55);
                _page.FillRectangle(Left, y, 3, height, Green);
                var segmentLabel = continuation ? $"{label} · CONTINUACIÓN" : label;
                _page.DrawText(segmentLabel.ToUpperInvariant(), Left + 10, y + height - 15, 7.3, true, Muted);
                var textY = y + height - 29;
                foreach (var line in segment)
                {
                    _page.DrawText(line, Left + 10, textY, 9.1, false, Ink);
                    textY -= lineHeight;
                }
                _top += height + gap;
                offset += take;
                continuation = true;
                if (offset < lines.Count)
                    NewPage();
            }
        }

        public void DrawAttachmentManifest(IReadOnlyList<CopiersMaintenanceV2PdfAttachmentManifestItem> attachments)
        {
            var rows = attachments.Count == 0
                ? new[] { new CopiersMaintenanceV2PdfAttachmentManifestItem { FileName = "Sin archivos adicionales", Size = 0, Sha256 = "" } }
                : attachments;
            const double headerHeight = 23;
            const double rowHeight = 25;
            var required = headerHeight + (rows.Count * rowHeight);
            EnsureSpace(required);
            var y = ToPdfY(_top, required);
            var firstWidth = 335d;
            _page.FillRectangle(Left, y + required - headerHeight, ContentWidth, headerHeight, Navy);
            _page.DrawText("ARCHIVO", Left + 9, y + required - 15, 7.5, true, White);
            _page.DrawText("INTEGRIDAD", Left + firstWidth + 9, y + required - 15, 7.5, true, White);
            var rowY = y + required - headerHeight - rowHeight;
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                _page.FillRectangle(Left, rowY, ContentWidth, rowHeight, index % 2 == 0 ? White : Pale);
                _page.StrokeRectangle(Left, rowY, ContentWidth, rowHeight, Line, 0.45);
                _page.DrawLine(Left + firstWidth, rowY, Left + firstWidth, rowY + rowHeight, Line, 0.45);
                var fileLabel = row.Size > 0
                    ? $"{index + 1:00}. {row.FileName} · {FormatBytes(row.Size)}"
                    : row.FileName;
                _page.DrawText(TruncateToWidth(fileLabel, firstWidth - 18, 8.6), Left + 9, rowY + 8.5, 8.6, false, Ink);
                var hash = string.IsNullOrWhiteSpace(row.Sha256) ? "-" : $"SHA-256 · {row.Sha256[..Math.Min(16, row.Sha256.Length)].ToLowerInvariant()}...";
                _page.DrawText(hash, Left + firstWidth + 9, rowY + 8.5, 7.6, false, Muted);
                rowY -= rowHeight;
            }
            _top += required;
        }

        public void DrawSignature(
            string consent,
            string signerName,
            string signerRole,
            DateTimeOffset deviceSignedAtUtc,
            DateTimeOffset serverFinalizedAtUtc,
            string formVersion)
        {
            const double height = 154;
            EnsureSpace(height);
            var y = ToPdfY(_top, height);
            var leftWidth = 290d;
            _page.FillRectangle(Left, y, ContentWidth, height, White);
            _page.StrokeRectangle(Left, y, ContentWidth, height, Line, 0.7);
            _page.FillRectangle(Left, y + height - 23, ContentWidth, 23, Pale);
            _page.DrawLine(Left + leftWidth, y, Left + leftWidth, y + height, Line, 0.55);
            _page.DrawText("CONFORMIDAD DEL CLIENTE", Left + 9, y + height - 15, 7.4, true, Muted);
            _page.DrawText("VALIDACIÓN DEL DOCUMENTO", Left + leftWidth + 9, y + height - 15, 7.4, true, Muted);

            var consentLines = Wrap(consent, leftWidth - 18, 8.3);
            var textY = y + height - 38;
            foreach (var line in consentLines.Take(5))
            {
                _page.DrawText(line, Left + 9, textY, 8.3, false, Ink);
                textY -= 11;
            }

            var signatureX = Left + 16;
            var signatureY = y + 12;
            var signatureMaxWidth = leftWidth - 32;
            var signatureMaxHeight = 47d;
            var scale = Math.Min(signatureMaxWidth / _signature.Width, signatureMaxHeight / _signature.Height);
            var signatureWidth = _signature.Width * scale;
            var signatureHeight = _signature.Height * scale;
            _page.DrawImage("Sig", signatureX, signatureY, signatureWidth, signatureHeight);

            var detailX = Left + leftWidth + 9;
            var localSigned = deviceSignedAtUtc == default ? "No registrada" : FormatBogota(deviceSignedAtUtc);
            var localClosed = serverFinalizedAtUtc == default ? "No registrado" : FormatBogota(serverFinalizedAtUtc);
            _page.DrawText(TruncateToWidth(signerName, ContentWidth - leftWidth - 18, 9.2), detailX, y + 72, 9.2, true, Ink);
            _page.DrawText(TruncateToWidth(signerRole, ContentWidth - leftWidth - 18, 8.3), detailX, y + 58, 8.3, false, Muted);
            _page.DrawText($"Firma: {localSigned}", detailX, y + 42, 7.5, false, Muted);
            _page.DrawText($"Cierre: {localClosed}", detailX, y + 30, 7.5, false, Muted);
            _page.DrawText($"Formato: {formVersion}", detailX, y + 18, 7.5, false, Muted);
            _top += height;
        }

        public void SmallNote(string text)
        {
            var lines = Wrap(text, ContentWidth, 7.3);
            EnsureSpace(lines.Count * 10);
            var y = ToPdfY(_top, 8);
            foreach (var line in lines)
            {
                _page.DrawText(line, Left + 6, y, 7.3, false, Muted);
                y -= 10;
                _top += 10;
            }
        }

        public void Spacer(double points)
        {
            EnsureSpace(points);
            _top += points;
        }

        public byte[] Build()
        {
            for (var index = 0; index < _pages.Count; index++)
                DrawFooter(_pages[index], index + 1, _pages.Count);
            return PdfBinaryWriter.Build(_pages, _signature, ReportNumber);
        }

        private void NewPage()
        {
            _page = new PdfCanvas();
            _pages.Add(_page);
            DrawHeader(_page);
            _top = HeaderBottom + 14;
        }

        private void EnsureSpace(double height)
        {
            if (_top + height <= FooterTop - 14)
                return;
            NewPage();
        }

        private void DrawHeader(PdfCanvas canvas)
        {
            canvas.FillRectangle(0, PageHeight - 112, PageWidth, 112, Navy);
            canvas.FillRectangle(0, PageHeight - 115, PageWidth, 3, Green);
            canvas.DrawText("REPORTE DE SERVICIO", Left, PageHeight - 48, 17.2, true, White);
            canvas.DrawText("DIGITAL TECH COPIERS SAS · MTO FIRMADO", Left, PageHeight - 69, 8.5, false, White);
            canvas.DrawTextRight(ReportNumber, PageWidth - Right, PageHeight - 48, 9.3, true, White);
            canvas.DrawTextRight("Documento final · Evidencia íntegra", PageWidth - Right, PageHeight - 69, 7.8, false, White);
        }

        private static void DrawFooter(PdfCanvas canvas, int page, int total)
        {
            canvas.DrawLine(Left, 36, PageWidth - Right, 36, Line, 0.7);
            canvas.DrawText("Digital Tech Copiers SAS · MTO Firmado V2", Left, 22, 7.2, false, Muted);
            canvas.DrawTextRight($"Página {page} de {total}", PageWidth - Right, 22, 7.2, false, Muted);
        }

        private void DrawInfoCell(double x, double y, double width, double height, PdfInfoCell cell, IReadOnlyList<string> lines)
        {
            _page.FillRectangle(x, y, width, height, White);
            _page.StrokeRectangle(x, y, width, height, Line, 0.55);
            _page.DrawText(cell.Label.ToUpperInvariant(), x + 9, y + height - 14, 7.2, true, Muted);
            var textY = y + height - 29;
            foreach (var line in lines)
            {
                _page.DrawText(line, x + 9, textY, 9.4, true, Ink);
                textY -= 12.2;
            }
        }

        private static double ToPdfY(double top, double height) => PageHeight - top - height;
    }

    private sealed class PdfCanvas
    {
        private readonly StringBuilder _content = new();

        public byte[] ToBytes() => Encoding.Latin1.GetBytes(_content.ToString());

        public void FillRectangle(double x, double y, double width, double height, PdfColor color)
        {
            SetColor(color, fill: true);
            _content.AppendLine($"{N(x)} {N(y)} {N(width)} {N(height)} re f");
        }

        public void StrokeRectangle(double x, double y, double width, double height, PdfColor color, double lineWidth)
        {
            SetColor(color, fill: false);
            _content.AppendLine($"{N(lineWidth)} w {N(x)} {N(y)} {N(width)} {N(height)} re S");
        }

        public void DrawLine(double x1, double y1, double x2, double y2, PdfColor color, double lineWidth)
        {
            SetColor(color, fill: false);
            _content.AppendLine($"{N(lineWidth)} w {N(x1)} {N(y1)} m {N(x2)} {N(y2)} l S");
        }

        public void DrawText(string text, double x, double y, double fontSize, bool bold, PdfColor color)
        {
            SetColor(color, fill: true);
            _content.AppendLine($"BT /{(bold ? "F2" : "F1")} {N(fontSize)} Tf 1 0 0 1 {N(x)} {N(y)} Tm ({EscapeText(text)}) Tj ET");
        }

        public void DrawTextRight(string text, double rightX, double y, double fontSize, bool bold, PdfColor color)
        {
            var safe = ToWinAnsi(text);
            DrawText(safe, rightX - EstimateWidth(safe, fontSize, bold), y, fontSize, bold, color);
        }

        public void DrawImage(string name, double x, double y, double width, double height) =>
            _content.AppendLine($"q {N(width)} 0 0 {N(height)} {N(x)} {N(y)} cm /{name} Do Q");

        private void SetColor(PdfColor color, bool fill) =>
            _content.AppendLine($"{N(color.R)} {N(color.G)} {N(color.B)} {(fill ? "rg" : "RG")}");
    }

    private static class PdfBinaryWriter
    {
        public static byte[] Build(IReadOnlyList<PdfCanvas> pages, PdfJpeg signature, string reportNumber)
        {
            const int catalogNumber = 1;
            const int pagesNumber = 2;
            const int regularFontNumber = 3;
            const int boldFontNumber = 4;
            const int imageNumber = 5;
            var firstPageNumber = 6;
            var pageNumbers = Enumerable.Range(0, pages.Count).Select(index => firstPageNumber + (index * 2)).ToArray();
            var objects = new SortedDictionary<int, byte[]>();
            objects[catalogNumber] = Ascii($"{catalogNumber} 0 obj\n<< /Type /Catalog /Pages {pagesNumber} 0 R >>\nendobj\n");
            objects[pagesNumber] = Ascii($"{pagesNumber} 0 obj\n<< /Type /Pages /Kids [{string.Join(" ", pageNumbers.Select(number => $"{number} 0 R"))}] /Count {pages.Count} >>\nendobj\n");
            objects[regularFontNumber] = Ascii($"{regularFontNumber} 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");
            objects[boldFontNumber] = Ascii($"{boldFontNumber} 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");
            objects[imageNumber] = Combine(
                Ascii($"{imageNumber} 0 obj\n<< /Type /XObject /Subtype /Image /Width {signature.Width} /Height {signature.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Interpolate true /Length {signature.Content.Length} >>\nstream\n"),
                signature.Content,
                Ascii("\nendstream\nendobj\n"));

            for (var index = 0; index < pages.Count; index++)
            {
                var pageNumber = pageNumbers[index];
                var contentNumber = pageNumber + 1;
                var stream = pages[index].ToBytes();
                objects[pageNumber] = Ascii(
                    $"{pageNumber} 0 obj\n<< /Type /Page /Parent {pagesNumber} 0 R /MediaBox [0 0 {N(PageWidth)} {N(PageHeight)}] " +
                    $"/Resources << /Font << /F1 {regularFontNumber} 0 R /F2 {boldFontNumber} 0 R >> /XObject << /Sig {imageNumber} 0 R >> >> " +
                    $"/Contents {contentNumber} 0 R >>\nendobj\n");
                objects[contentNumber] = Combine(
                    Ascii($"{contentNumber} 0 obj\n<< /Length {stream.Length} >>\nstream\n"),
                    stream,
                    Ascii("\nendstream\nendobj\n"));
            }

            var infoNumber = objects.Keys.Max() + 1;
            objects[infoNumber] = Encoding.Latin1.GetBytes(
                $"{infoNumber} 0 obj\n<< /Title ({EscapeText($"Reporte de servicio {reportNumber}")}) /Author (Digital Tech Copiers SAS) /Subject (MTO Firmado V2) >>\nendobj\n");

            using var output = new MemoryStream();
            Write(output, Ascii("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n"));
            var offsets = new Dictionary<int, long>();
            foreach (var item in objects)
            {
                offsets[item.Key] = output.Position;
                Write(output, item.Value);
            }
            var xref = output.Position;
            var maxObject = objects.Keys.Max();
            Write(output, Ascii($"xref\n0 {maxObject + 1}\n0000000000 65535 f \n"));
            for (var number = 1; number <= maxObject; number++)
            {
                var offset = offsets.TryGetValue(number, out var value) ? value : 0;
                Write(output, Ascii($"{offset:0000000000} 00000 {(offset == 0 ? 'f' : 'n')} \n"));
            }
            Write(output, Ascii($"trailer\n<< /Size {maxObject + 1} /Root {catalogNumber} 0 R /Info {infoNumber} 0 R >>\nstartxref\n{xref}\n%%EOF"));
            return output.ToArray();
        }

        private static byte[] Ascii(string value) => Encoding.Latin1.GetBytes(value);

        private static byte[] Combine(params byte[][] chunks)
        {
            using var stream = new MemoryStream();
            foreach (var chunk in chunks)
                Write(stream, chunk);
            return stream.ToArray();
        }

        private static void Write(Stream stream, byte[] value) => stream.Write(value, 0, value.Length);
    }

    private sealed class PdfJpeg
    {
        private PdfJpeg(byte[] content, int width, int height)
        {
            Content = content;
            Width = width;
            Height = height;
        }

        public byte[] Content { get; }
        public int Width { get; }
        public int Height { get; }

        public static PdfJpeg? TryCreate(byte[] content, string? contentType)
        {
            if (content.Length < 4
                || content[0] != 0xFF
                || content[1] != 0xD8
                || !string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
                return null;

            var offset = 2;
            while (offset + 8 < content.Length)
            {
                if (content[offset] != 0xFF)
                {
                    offset++;
                    continue;
                }
                var marker = content[offset + 1];
                offset += 2;
                if (marker is 0xD8 or 0xD9)
                    continue;
                if (offset + 2 > content.Length)
                    break;
                var length = (content[offset] << 8) | content[offset + 1];
                if (length < 2 || offset + length > content.Length)
                    break;
                if (marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF)
                {
                    if (length < 7)
                        return null;
                    var height = (content[offset + 3] << 8) | content[offset + 4];
                    var width = (content[offset + 5] << 8) | content[offset + 6];
                    return width > 0 && height > 0 ? new PdfJpeg(content, width, height) : null;
                }
                offset += length;
            }
            return null;
        }
    }

    private readonly record struct PdfInfoCell(string Label, string Value);

    private readonly record struct PdfColor(double R, double G, double B)
    {
        public static PdfColor FromHex(string value)
        {
            var hex = value.Trim().TrimStart('#');
            return new PdfColor(
                Convert.ToInt32(hex[..2], 16) / 255d,
                Convert.ToInt32(hex.Substring(2, 2), 16) / 255d,
                Convert.ToInt32(hex.Substring(4, 2), 16) / 255d);
        }
    }

    private static IReadOnlyList<string> Wrap(string? value, double width, double fontSize)
    {
        var text = ToWinAnsi(value);
        if (string.IsNullOrWhiteSpace(text))
            return new[] { "No registrado" };
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = "";
            foreach (var word in words)
            {
                var candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";
                if (EstimateWidth(candidate, fontSize, bold: false) <= width)
                {
                    current = candidate;
                    continue;
                }
                if (!string.IsNullOrEmpty(current))
                    lines.Add(current);
                current = "";
                var remaining = word;
                while (remaining.Length > 0 && EstimateWidth(remaining, fontSize, false) > width)
                {
                    var take = 1;
                    while (take < remaining.Length
                        && EstimateWidth(remaining[..(take + 1)], fontSize, false) <= width)
                    {
                        take++;
                    }
                    lines.Add(remaining[..take]);
                    remaining = remaining[take..];
                }
                current = remaining;
            }
            if (!string.IsNullOrEmpty(current))
                lines.Add(current);
        }
        return lines.Count == 0 ? new[] { "No registrado" } : lines;
    }

    private static string TruncateToWidth(string? value, double width, double fontSize)
    {
        var safe = ToWinAnsi(value);
        if (EstimateWidth(safe, fontSize, false) <= width)
            return safe;
        const string suffix = "...";
        while (safe.Length > 1 && EstimateWidth(safe + suffix, fontSize, false) > width)
            safe = safe[..^1];
        return safe.TrimEnd() + suffix;
    }

    private static double EstimateWidth(string? value, double fontSize, bool bold) =>
        (value ?? "").Sum(character => HelveticaWidth(character, bold)) * fontSize;

    // Widths are conservative Helvetica/Helvetica-Bold AFM proportions. Accurate
    // metrics keep long unbroken values inside their cells instead of relying on
    // a visual average that can clip wide glyphs near the page boundary.
    private static double HelveticaWidth(char character, bool bold)
    {
        if (character is >= '0' and <= '9')
            return 0.556;

        return character switch
        {
            ' ' => 0.278,
            'i' or 'l' or 'í' or 'ì' or 'î' or 'ï' => bold ? 0.278 : 0.222,
            'I' or 'Í' or 'Ì' or 'Î' or 'Ï' => 0.278,
            'j' => bold ? 0.278 : 0.222,
            'f' or 't' => bold ? 0.333 : 0.278,
            'r' => bold ? 0.389 : 0.333,
            'm' => bold ? 0.889 : 0.833,
            'w' => bold ? 0.778 : 0.722,
            'M' => 0.833,
            'W' => 0.944,
            'G' or 'O' or 'Q' or 'Ó' or 'Ò' or 'Ô' or 'Ö' => 0.778,
            'C' or 'D' or 'H' or 'N' or 'R' or 'U' or 'Ñ' or 'Ú' or 'Ù' or 'Û' or 'Ü' => 0.722,
            'A' or 'B' or 'E' or 'K' or 'P' or 'S' or 'V' or 'X' or 'Y' or 'Á' or 'À' or 'Â' or 'Ä' or 'É' or 'È' or 'Ê' or 'Ë' => bold ? 0.722 : 0.667,
            'F' or 'T' or 'Z' or 'L' => 0.611,
            'a' or 'b' or 'd' or 'e' or 'g' or 'h' or 'n' or 'o' or 'p' or 'q' or 'u'
                or 'á' or 'à' or 'â' or 'ä' or 'é' or 'è' or 'ê' or 'ë'
                or 'ñ' or 'ó' or 'ò' or 'ô' or 'ö' or 'ú' or 'ù' or 'û' or 'ü' => bold ? 0.611 : 0.556,
            'c' or 's' or 'v' or 'x' or 'y' or 'z' => bold ? 0.556 : 0.500,
            '@' => 1.015,
            '.' or ',' or ':' or ';' or '!' or '|' or '·' or '/' => 0.278,
            '-' or '(' or ')' or '[' or ']' => 0.333,
            _ => bold ? 0.622 : 0.600
        };
    }

    private static string EscapeText(string? value) =>
        ToWinAnsi(value)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static string ToWinAnsi(string? value)
    {
        var builder = new StringBuilder(value?.Length ?? 0);
        foreach (var character in (value ?? "").Normalize(NormalizationForm.FormC))
        {
            builder.Append(character switch
            {
                '\u2013' or '\u2014' => '-',
                '\u2018' or '\u2019' => '\'',
                '\u201C' or '\u201D' => '"',
                '\u2022' => '·',
                '\u2192' => 'a',
                '\t' => ' ',
                >= ' ' and <= '\u00FF' => character,
                '\n' or '\r' => character,
                _ => '?'
            });
        }
        return builder.ToString();
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:0.#} KB",
        _ => $"{bytes / 1024d / 1024d:0.#} MB"
    };

    private static string FormatBogota(DateTimeOffset value)
    {
        var zone = ResolveBogotaTimeZone();
        var local = TimeZoneInfo.ConvertTime(value, zone);
        return local.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) + " COT";
    }

    private static TimeZoneInfo ResolveBogotaTimeZone()
    {
        foreach (var id in new[] { "SA Pacific Standard Time", "America/Bogota" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

