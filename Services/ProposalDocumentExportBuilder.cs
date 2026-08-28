using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.ProposalChat;

namespace CotizadorInterno.Web.Services;

public static class ProposalDocumentExportBuilder
{
    private static readonly Encoding PdfEncoding = Encoding.Latin1;

    public static byte[] BuildWordDocument(ProposalExportRequestDto request)
    {
        var html = NormalizeHtmlDocument(request);
        return Encoding.UTF8.GetBytes(html);
    }

    public static byte[] BuildPdfDocument(ProposalExportRequestDto request)
    {
        var text = ResolveDocumentText(request);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("No hay contenido de propuesta para exportar.");

        var pages = BuildPdfPages(text);
        return BuildPdf(pages);
    }

    public static string BuildSafeFileName(string? rawTitle, string extension)
    {
        var title = string.IsNullOrWhiteSpace(rawTitle) ? "propuesta-comercial" : rawTitle.Trim();
        var invalidChars = Path.GetInvalidFileNameChars();
        var safe = new string(title
            .Select(ch => invalidChars.Contains(ch) ? '-' : ch)
            .ToArray());

        safe = Regex.Replace(safe, @"\s+", "-").Trim('-', '.', ' ');
        if (string.IsNullOrWhiteSpace(safe))
            safe = "propuesta-comercial";

        return $"{safe.ToLowerInvariant()}.{extension.TrimStart('.')}";
    }

    private static string NormalizeHtmlDocument(ProposalExportRequestDto request)
    {
        var html = (request.DocumentHtml ?? "").Trim();
        var content = !string.IsNullOrWhiteSpace(html)
            ? StripHtmlShell(html)
            : $"<section class=\"proposal-page\">{WebUtility.HtmlEncode(request.DocumentText ?? "").Replace("\n", "<br>", StringComparison.Ordinal)}</section>";
        var extraStyles = ExtractStyleBlocks(html);
        return $$"""
<!DOCTYPE html>
<html lang="es">
<head>
  <meta charset="utf-8">
  <title>{{WebUtility.HtmlEncode(FirstNonEmpty(request.DocumentTitle, "Propuesta comercial"))}}</title>
  <style>
    body { margin: 0; font-family: Arial, sans-serif; color: #102033; line-height: 1.45; background: #e9eef5; }
    .proposal-static-page, .proposal-page { box-sizing: border-box; width: 794px; min-height: 1123px; margin: 0 auto 24px; background: #fff; page-break-after: always; overflow: hidden; }
    .proposal-static-page img { display: block; width: 100%; height: auto; }
    .proposal-page { padding: 48px; }
    h1, h2, h3 { color: #061943; }
    table { width: 100%; border-collapse: collapse; }
    th, td { border: 1px solid #d9e2ee; padding: 8px; text-align: left; }
    th { background: #061943; color: #fff; }
    {{extraStyles}}
  </style>
</head>
<body>
  {{BuildFixedIntroHtml(inlineImages: true)}}
  {{content}}
</body>
</html>
""";
    }

    private static string StripHtmlShell(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        var bodyMatch = Regex.Match(html, @"<body[^>]*>([\s\S]*?)</body>", RegexOptions.IgnoreCase);
        return bodyMatch.Success ? bodyMatch.Groups[1].Value : html;
    }

    private static string ExtractStyleBlocks(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        var blocks = Regex
            .Matches(html, @"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase)
            .Select(static match => match.Groups[1].Value.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value));

        return string.Join(Environment.NewLine, blocks);
    }

    private static string BuildFixedIntroHtml(bool inlineImages)
    {
        var cover = ResolveImageSource("proposal-cover.png", inlineImages);
        var about = ResolveImageSource("proposal-about.png", inlineImages);
        return $$"""
  <section class="proposal-static-page"><img src="{{cover}}" alt="Portada propuesta comercial"></section>
  <section class="proposal-static-page"><img src="{{about}}" alt="Sobre Digital Tech"></section>
""";
    }

    private static string ResolveImageSource(string fileName, bool inlineImages)
    {
        if (!inlineImages)
            return $"/img/proposals/{fileName}";

        var path = ResolveProposalImagePath(fileName);
        var contentType = fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            ? "image/jpeg"
            : "image/png";
        return $"data:{contentType};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
    }

    private static string ResolveProposalImagePath(string fileName) =>
        Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "img", "proposals", fileName);

    private static string ResolveDocumentText(ProposalExportRequestDto request)
    {
        if (!string.IsNullOrWhiteSpace(request.DocumentText))
            return CleanText(request.DocumentText);

        return HtmlToText(request.DocumentHtml);
    }

    private static string HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        var text = html;
        text = Regex.Replace(text, @"<\s*(br|hr)\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</\s*(p|div|section|article|h1|h2|h3|h4|li|tr|table)\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*li[^>]*>", "- ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        return CleanText(text);
    }

    private static string CleanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var lines = value
            .Replace("\r", "", StringComparison.Ordinal)
            .Split('\n')
            .Select(static line => Regex.Replace(line.Trim(), @"\s+", " "))
            .ToList();

        var compact = new List<string>(lines.Count);
        var previousBlank = false;
        foreach (var line in lines)
        {
            var isBlank = string.IsNullOrWhiteSpace(line);
            if (isBlank && previousBlank)
                continue;

            compact.Add(line);
            previousBlank = isBlank;
        }

        return string.Join('\n', compact).Trim();
    }

    private static IReadOnlyList<string> BuildPdfPages(string text)
    {
        var parts = Regex
            .Split(text, @"(?=PAGINA\s+\d+)", RegexOptions.IgnoreCase)
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .Select(static part => part.Trim())
            .ToList();

        if (parts.Count == 0)
            parts.Add(text);

        var pages = new List<string>();
        foreach (var part in parts)
        {
            pages.AddRange(WrapPdfTextIntoPages(part));
        }

        return pages.Count == 0 ? new[] { text } : pages;
    }

    private static IReadOnlyList<string> WrapPdfTextIntoPages(string text)
    {
        const int maxLinesPerPage = 54;
        var allLines = new List<string>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                allLines.Add("");
                continue;
            }

            allLines.AddRange(WrapLine(line, 92));
        }

        var pages = new List<string>();
        for (var index = 0; index < allLines.Count; index += maxLinesPerPage)
        {
            pages.Add(string.Join('\n', allLines.Skip(index).Take(maxLinesPerPage)));
        }

        return pages;
    }

    private static IEnumerable<string> WrapLine(string line, int maxChars)
    {
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();
        foreach (var word in words)
        {
            if (current.Length > 0 && current.Length + word.Length + 1 > maxChars)
            {
                yield return current.ToString();
                current.Clear();
            }

            if (current.Length > 0)
                current.Append(' ');

            current.Append(word);
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    private static byte[] BuildPdf(IReadOnlyList<string> pages)
    {
        const double pageWidth = 595.28;
        const double pageHeight = 841.89;
        const double marginX = 42;
        const double topY = 792;
        const double lineHeight = 13.4;

        var contents = new List<string>(pages.Count);
        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var builder = new StringBuilder();
            AppendPdfRect(builder, 0, pageHeight - 54, pageWidth, 54, "0.02 0.10 0.26", fill: true);
            AppendPdfText(builder, "DIGITAL TECH COLOMBIA", marginX, pageHeight - 32, 10, "F2", "1 1 1");
            AppendPdfText(builder, $"Pagina {pageIndex + 1} de {pages.Count}", pageWidth - marginX - 90, pageHeight - 32, 8, "F1", "1 1 1");

            var y = topY - 34;
            foreach (var line in pages[pageIndex].Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    y -= lineHeight;
                    continue;
                }

                var isHeading = Regex.IsMatch(line, @"^(PAGINA\s+\d+|[0-9]+\.\s+|PROPUESTA|SOBRE\s+DIGITAL)", RegexOptions.IgnoreCase);
                AppendPdfText(builder, line, marginX, y, isHeading ? 10.5 : 8.8, isHeading ? "F2" : "F1", "0.06 0.13 0.22");
                y -= isHeading ? lineHeight + 2 : lineHeight;
            }

            AppendPdfText(builder, "www.digitaltechcolombia.com", marginX, 28, 7.5, "F1", "0.35 0.43 0.54");
            contents.Add(builder.ToString());
        }

        var fixedImages = new[]
        {
            ResolveProposalImagePath("proposal-cover.jpg"),
            ResolveProposalImagePath("proposal-about.jpg")
        };
        return WritePdf(contents, fixedImages, pageWidth, pageHeight);
    }

    private static byte[] WritePdf(IReadOnlyList<string> pageContents, IReadOnlyList<string> fixedImagePaths, double pageWidth, double pageHeight)
    {
        var objects = new List<byte[]> { Array.Empty<byte>() };
        int AddObject(byte[] body)
        {
            objects.Add(body);
            return objects.Count - 1;
        }

        var catalogId = AddObject(Array.Empty<byte>());
        var pagesId = AddObject(Array.Empty<byte>());
        var fontRegularId = AddObject(PdfBody("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        var fontBoldId = AddObject(PdfBody("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));
        var kids = new List<int>();

        foreach (var imagePath in fixedImagePaths.Where(File.Exists))
        {
            var imageBytes = File.ReadAllBytes(imagePath);
            var (width, height) = ReadJpegSize(imageBytes);
            var imageId = AddObject(PdfStreamBody(
                imageBytes,
                $"/Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode"));
            var contentId = AddObject(PdfStreamBody(
                PdfEncoding.GetBytes(FormattableString.Invariant($"q {pageWidth:0.###} 0 0 {pageHeight:0.###} 0 0 cm /Im1 Do Q\n")),
                ""));
            var pageId = AddObject(PdfBody(FormattableString.Invariant(
                $"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {pageWidth:0.##} {pageHeight:0.##}] /Resources << /XObject << /Im1 {imageId} 0 R >> >> /Contents {contentId} 0 R >>")));
            kids.Add(pageId);
        }

        for (var index = 0; index < pageContents.Count; index++)
        {
            var contentId = AddObject(PdfStreamBody(PdfEncoding.GetBytes(pageContents[index]), ""));
            var pageId = AddObject(PdfBody(FormattableString.Invariant(
                $"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {pageWidth:0.##} {pageHeight:0.##}] /Resources << /Font << /F1 {fontRegularId} 0 R /F2 {fontBoldId} 0 R >> >> /Contents {contentId} 0 R >>")));
            kids.Add(pageId);
        }

        objects[catalogId] = PdfBody($"<< /Type /Catalog /Pages {pagesId} 0 R >>");
        objects[pagesId] = PdfBody($"<< /Type /Pages /Kids [{string.Join(" ", kids.Select(static id => $"{id} 0 R"))}] /Count {kids.Count} >>");

        var offsets = new long[objects.Count];
        using var stream = new MemoryStream();
        WritePdfString(stream, "%PDF-1.4\n");
        for (var objectNumber = 1; objectNumber < objects.Count; objectNumber++)
        {
            offsets[objectNumber] = stream.Position;
            WritePdfString(stream, $"{objectNumber} 0 obj\n");
            stream.Write(objects[objectNumber], 0, objects[objectNumber].Length);
            WritePdfString(stream, "\nendobj\n");
        }

        var xrefOffset = stream.Position;
        WritePdfString(stream, $"xref\n0 {objects.Count}\n");
        WritePdfString(stream, "0000000000 65535 f \n");
        for (var index = 1; index < objects.Count; index++)
        {
            WritePdfString(stream, $"{offsets[index]:D10} 00000 n \n");
        }

        WritePdfString(stream, $"trailer\n<< /Size {objects.Count} /Root {catalogId} 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
        return stream.ToArray();
    }

    private static byte[] PdfBody(string value) => PdfEncoding.GetBytes(value);

    private static byte[] PdfStreamBody(byte[] streamBytes, string dictionaryEntries)
    {
        using var stream = new MemoryStream();
        var dict = string.IsNullOrWhiteSpace(dictionaryEntries)
            ? $"<< /Length {streamBytes.Length} >>\nstream\n"
            : $"<< {dictionaryEntries} /Length {streamBytes.Length} >>\nstream\n";
        var header = PdfEncoding.GetBytes(dict);
        stream.Write(header, 0, header.Length);
        stream.Write(streamBytes, 0, streamBytes.Length);
        var footer = PdfEncoding.GetBytes("\nendstream");
        stream.Write(footer, 0, footer.Length);
        return stream.ToArray();
    }

    private static (int Width, int Height) ReadJpegSize(byte[] bytes)
    {
        for (var index = 2; index + 9 < bytes.Length;)
        {
            if (bytes[index] != 0xFF)
            {
                index++;
                continue;
            }

            var marker = bytes[index + 1];
            var length = (bytes[index + 2] << 8) + bytes[index + 3];
            if (marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF)
            {
                var height = (bytes[index + 5] << 8) + bytes[index + 6];
                var width = (bytes[index + 7] << 8) + bytes[index + 8];
                return (width, height);
            }

            index += 2 + Math.Max(length, 2);
        }

        return (1191, 1685);
    }

    private static void AppendPdfRect(StringBuilder content, double x, double y, double width, double height, string color, bool fill)
    {
        content.AppendFormat(
            System.Globalization.CultureInfo.InvariantCulture,
            fill
                ? "{0} rg {1:0.###} {2:0.###} {3:0.###} {4:0.###} re f\n"
                : "{0} RG {1:0.###} {2:0.###} {3:0.###} {4:0.###} re S\n",
            color,
            x,
            y,
            width,
            height);
    }

    private static void AppendPdfText(StringBuilder content, string text, double x, double y, double fontSize, string fontResource, string color)
    {
        content.AppendFormat(
            System.Globalization.CultureInfo.InvariantCulture,
            "BT /{0} {1:0.###} Tf {2} rg 1 0 0 1 {3:0.###} {4:0.###} Tm ({5}) Tj ET\n",
            fontResource,
            fontSize,
            color,
            x,
            y,
            EscapePdfText(ToPdfSafeText(text)));
    }

    private static void WritePdfObject(MemoryStream stream, long[] offsets, int objectNumber, string body)
    {
        offsets[objectNumber] = stream.Position;
        WritePdfString(stream, $"{objectNumber} 0 obj\n{body}\nendobj\n");
    }

    private static void WritePdfStreamObject(MemoryStream stream, long[] offsets, int objectNumber, string content)
    {
        offsets[objectNumber] = stream.Position;
        var contentBytes = PdfEncoding.GetBytes(content);
        WritePdfString(stream, $"{objectNumber} 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        stream.Write(contentBytes, 0, contentBytes.Length);
        WritePdfString(stream, "\nendstream\nendobj\n");
    }

    private static void WritePdfString(MemoryStream stream, string value)
    {
        var bytes = PdfEncoding.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string ToPdfSafeText(string value)
    {
        return (value ?? "")
            .Replace("–", "-", StringComparison.Ordinal)
            .Replace("—", "-", StringComparison.Ordinal)
            .Replace("“", "\"", StringComparison.Ordinal)
            .Replace("”", "\"", StringComparison.Ordinal)
            .Replace("‘", "'", StringComparison.Ordinal)
            .Replace("’", "'", StringComparison.Ordinal)
            .Replace("•", "-", StringComparison.Ordinal);
    }

    private static string EscapePdfText(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}
