using System.Globalization;
using System.Net;
using System.Text;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Hardware;

namespace CotizadorInterno.Web.Services;

public static class HardwarePurchaseOrderDocumentBuilder
{
    private const int MaxLinesPerPage = 24;
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-CO");

    public static HardwarePurchaseOrderDocument Build(
        HardwarePurchaseOrderRequest request,
        CurrentUserInfo requester,
        HardwareOptions options,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(options);

        var providerName = RequireText(request.ProviderName, "Nombre de proveedor", 180);
        var sourceLines = (request.Lines ?? new List<HardwarePurchaseOrderLineRequest>())
            .Where(static line => line is not null)
            .ToList();
        if (sourceLines.Count == 0)
            throw new InvalidOperationException("Agrega al menos una linea a la orden de compra.");

        if (sourceLines.Count > MaxLinesPerPage)
            throw new InvalidOperationException($"La ODC de una pagina permite maximo {MaxLinesPerPage} lineas.");

        var lines = new List<HardwarePurchaseOrderDocumentLine>(sourceLines.Count);
        for (var index = 0; index < sourceLines.Count; index++)
        {
            var source = sourceLines[index];
            var product = RequireText(source.Product, $"Producto de la linea {index + 1}", 260);
            var quantity = source.Quantity ?? 0;
            if (quantity <= 0)
                throw new InvalidOperationException($"Cantidad de la linea {index + 1} debe ser mayor a cero.");

            var unitValue = RoundCurrency(source.UnitValueBeforeVat ?? 0m);
            if (unitValue <= 0)
                throw new InvalidOperationException($"Valor unitario de la linea {index + 1} debe ser mayor a cero.");

            var vatPercent = Math.Round(source.VatPercent ?? 0m, 2, MidpointRounding.AwayFromZero);
            if (vatPercent < 0 || vatPercent > 100)
                throw new InvalidOperationException($"IVA de la linea {index + 1} debe estar entre 0 y 100.");

            var totalBeforeVat = RoundCurrency(quantity * unitValue);
            var vatValue = RoundCurrency(totalBeforeVat * vatPercent / 100m);
            var totalWithVat = RoundCurrency(totalBeforeVat + vatValue);
            lines.Add(new HardwarePurchaseOrderDocumentLine
            {
                Product = product,
                Quantity = quantity,
                UnitValueBeforeVat = unitValue,
                TotalBeforeVat = totalBeforeVat,
                VatPercent = vatPercent,
                VatValue = vatValue,
                TotalWithVat = totalWithVat
            });
        }

        var subtotal = RoundCurrency(lines.Sum(static line => line.TotalBeforeVat));
        var vatTotal = RoundCurrency(lines.Sum(static line => line.VatValue));
        var grandTotal = RoundCurrency(lines.Sum(static line => line.TotalWithVat));
        var orderNumber = $"ODC-HW-{createdAt:yyyyMMdd-HHmmss}";
        var requesterName = FirstNonEmpty(requester.DisplayName, requester.Email, "Solicitante");
        var requesterEmail = FirstNonEmpty(requester.Email, requester.EmployeeUserEmail);

        var document = new HardwarePurchaseOrderDocument
        {
            RequestId = Guid.NewGuid().ToString("N"),
            OrderNumber = orderNumber,
            OrderDate = createdAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            OrderDateDisplay = createdAt.ToString("dd/MM/yyyy", Culture),
            ProviderName = providerName,
            RequesterName = requesterName,
            RequesterEmail = requesterEmail,
            CompanyName = FirstNonEmpty(options.CompanyName, "Digital Tech Copiers SAS"),
            CompanyNit = FirstNonEmpty(options.CompanyNit, "900.399.875"),
            CompanyAddress = options.CompanyAddress?.Trim() ?? "",
            CompanyCity = FirstNonEmpty(options.CompanyCity, "Bogota D.C."),
            Lines = lines,
            SubtotalBeforeVat = subtotal,
            VatTotal = vatTotal,
            GrandTotal = grandTotal
        };

        document.PdfFileName = $"{SanitizeFileName(orderNumber)}.pdf";
        document.EmailHtml = BuildEmailHtml(document);
        document.ApprovalSummary = BuildApprovalSummary(document);
        document.PdfContent = BuildPdf(document);
        return document;
    }

    private static string BuildEmailHtml(HardwarePurchaseOrderDocument document)
    {
        var rows = string.Join("", document.Lines.Select(line => $"""
            <tr>
              <td>{Html(line.Product)}</td>
              <td style="text-align:right">{line.Quantity}</td>
              <td style="text-align:right">{Html(FormatCurrency(line.UnitValueBeforeVat))}</td>
              <td style="text-align:right">{Html(FormatCurrency(line.TotalBeforeVat))}</td>
              <td style="text-align:right">{Html(FormatPercent(line.VatPercent))}</td>
              <td style="text-align:right">{Html(FormatCurrency(line.TotalWithVat))}</td>
            </tr>
            """));

        return $$"""
            <div style="font-family:Segoe UI,Arial,sans-serif;color:#1b2733;max-width:760px">
              <div style="border-left:6px solid #1768ac;padding:10px 0 10px 16px;margin-bottom:18px">
                <div style="font-size:12px;color:#617181;text-transform:uppercase;letter-spacing:.08em">Orden de compra aprobada</div>
                <h1 style="font-size:22px;line-height:1.2;margin:3px 0;color:#1768ac">{{Html(document.OrderNumber)}}</h1>
                <div style="font-size:13px;color:#617181">{{Html(document.CompanyName)}} · NIT {{Html(document.CompanyNit)}}</div>
              </div>
              <p>Hola {{Html(document.RequesterName)}},</p>
              <p>La orden de compra de Hardware fue aprobada. Adjunto encuentras el PDF diligenciado.</p>
              <table style="border-collapse:collapse;width:100%;margin:14px 0;font-size:13px">
                <tr>
                  <td style="padding:8px;border:1px solid #d8e1ea;background:#f7fafc"><strong>Proveedor</strong></td>
                  <td style="padding:8px;border:1px solid #d8e1ea">{{Html(document.ProviderName)}}</td>
                  <td style="padding:8px;border:1px solid #d8e1ea;background:#f7fafc"><strong>Fecha</strong></td>
                  <td style="padding:8px;border:1px solid #d8e1ea">{{Html(document.OrderDateDisplay)}}</td>
                </tr>
              </table>
              <table style="border-collapse:collapse;width:100%;font-size:12px">
                <thead>
                  <tr style="background:#1768ac;color:#fff">
                    <th style="padding:8px;border:1px solid #1768ac;text-align:left">Producto</th>
                    <th style="padding:8px;border:1px solid #1768ac;text-align:right">Cantidad</th>
                    <th style="padding:8px;border:1px solid #1768ac;text-align:right">Valor unitario</th>
                    <th style="padding:8px;border:1px solid #1768ac;text-align:right">Total antes IVA</th>
                    <th style="padding:8px;border:1px solid #1768ac;text-align:right">IVA</th>
                    <th style="padding:8px;border:1px solid #1768ac;text-align:right">Total con IVA</th>
                  </tr>
                </thead>
                <tbody>{{rows}}</tbody>
              </table>
              <table style="border-collapse:collapse;margin-left:auto;margin-top:14px;font-size:13px;min-width:280px">
                <tr><td style="padding:6px 10px;border:1px solid #d8e1ea">Subtotal</td><td style="padding:6px 10px;border:1px solid #d8e1ea;text-align:right">{{Html(FormatCurrency(document.SubtotalBeforeVat))}}</td></tr>
                <tr><td style="padding:6px 10px;border:1px solid #d8e1ea">IVA</td><td style="padding:6px 10px;border:1px solid #d8e1ea;text-align:right">{{Html(FormatCurrency(document.VatTotal))}}</td></tr>
                <tr><td style="padding:8px 10px;border:1px solid #1768ac;background:#1768ac;color:#fff"><strong>Total</strong></td><td style="padding:8px 10px;border:1px solid #1768ac;background:#1768ac;color:#fff;text-align:right"><strong>{{Html(FormatCurrency(document.GrandTotal))}}</strong></td></tr>
              </table>
            </div>
            """;
    }

    private static string BuildApprovalSummary(HardwarePurchaseOrderDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Orden: {document.OrderNumber}");
        builder.AppendLine($"Proveedor: {document.ProviderName}");
        builder.AppendLine($"Solicitante: {document.RequesterName} <{document.RequesterEmail}>");
        builder.AppendLine($"Fecha: {document.OrderDateDisplay}");
        builder.AppendLine($"Subtotal: {FormatCurrency(document.SubtotalBeforeVat)}");
        builder.AppendLine($"IVA: {FormatCurrency(document.VatTotal)}");
        builder.AppendLine($"Total: {FormatCurrency(document.GrandTotal)}");
        builder.AppendLine();
        builder.AppendLine("Lineas:");
        foreach (var line in document.Lines)
        {
            builder.AppendLine($"- {line.Product} | Cant. {line.Quantity} | Unit. {FormatCurrency(line.UnitValueBeforeVat)} | IVA {FormatPercent(line.VatPercent)} | Total {FormatCurrency(line.TotalWithVat)}");
        }

        return builder.ToString();
    }

    private static byte[] BuildPdf(HardwarePurchaseOrderDocument document)
    {
        var canvas = new PdfCanvas();
        canvas.FillRectangle(0, 736, 612, 56, "#1768ac");
        canvas.DrawText("Digital Tech Copiers SAS", 42, 766, 16, true, "#ffffff");
        canvas.DrawTextRight("Orden de Compra", 570, 766, 18, true, "#ffffff");
        canvas.DrawText("NIT " + document.CompanyNit, 42, 748, 9, false, "#ffffff");
        canvas.DrawTextRight(document.OrderNumber, 570, 748, 10, true, "#ffffff");

        canvas.DrawText("Proveedor", 42, 706, 9, true, "#617181");
        canvas.DrawText(document.ProviderName, 42, 690, 12, true, "#1b2733");
        canvas.DrawText("Fecha", 360, 706, 9, true, "#617181");
        canvas.DrawText(document.OrderDateDisplay, 360, 690, 12, false, "#1b2733");
        canvas.DrawText("Solicitante", 42, 668, 9, true, "#617181");
        canvas.DrawText($"{document.RequesterName} - {document.RequesterEmail}", 42, 652, 10, false, "#1b2733");

        const double tableX = 42;
        const double tableWidth = 528;
        var y = 612d;
        var rowHeight = Math.Clamp(330d / Math.Max(document.Lines.Count, 1), 15d, 25d);
        var columns = new[]
        {
            new PdfColumn("Producto", 185d, false),
            new PdfColumn("Cant.", 42d, true),
            new PdfColumn("Unitario", 74d, true),
            new PdfColumn("Antes IVA", 78d, true),
            new PdfColumn("IVA", 48d, true),
            new PdfColumn("Total", 101d, true)
        };

        canvas.FillRectangle(tableX, y, tableWidth, 22, "#e7f2fb");
        canvas.StrokeRectangle(tableX, y, tableWidth, 22, "#c8d5e1");
        var x = tableX;
        foreach (var column in columns)
        {
            canvas.DrawText(column.Title, x + 5, y + 7, 8, true, "#0e4f84");
            x += column.Width;
        }

        y -= rowHeight;
        foreach (var line in document.Lines)
        {
            canvas.StrokeRectangle(tableX, y, tableWidth, rowHeight, "#d8e1ea");
            x = tableX;
            DrawPdfCell(canvas, line.Product, x, y, columns[0], rowHeight, 7.5, "#1b2733");
            x += columns[0].Width;
            DrawPdfCell(canvas, line.Quantity.ToString(CultureInfo.InvariantCulture), x, y, columns[1], rowHeight, 7.5, "#1b2733");
            x += columns[1].Width;
            DrawPdfCell(canvas, FormatCurrency(line.UnitValueBeforeVat), x, y, columns[2], rowHeight, 7.5, "#1b2733");
            x += columns[2].Width;
            DrawPdfCell(canvas, FormatCurrency(line.TotalBeforeVat), x, y, columns[3], rowHeight, 7.5, "#1b2733");
            x += columns[3].Width;
            DrawPdfCell(canvas, FormatPercent(line.VatPercent), x, y, columns[4], rowHeight, 7.5, "#1b2733");
            x += columns[4].Width;
            DrawPdfCell(canvas, FormatCurrency(line.TotalWithVat), x, y, columns[5], rowHeight, 7.5, "#1b2733");
            y -= rowHeight;
        }

        var totalsY = Math.Max(112d, y - 78d);
        canvas.StrokeRectangle(348, totalsY + 44, 222, 22, "#d8e1ea");
        canvas.DrawText("Subtotal", 358, totalsY + 51, 8, false, "#617181");
        canvas.DrawTextRight(FormatCurrency(document.SubtotalBeforeVat), 560, totalsY + 51, 8, false, "#1b2733");
        canvas.StrokeRectangle(348, totalsY + 22, 222, 22, "#d8e1ea");
        canvas.DrawText("IVA", 358, totalsY + 29, 8, false, "#617181");
        canvas.DrawTextRight(FormatCurrency(document.VatTotal), 560, totalsY + 29, 8, false, "#1b2733");
        canvas.FillRectangle(348, totalsY, 222, 22, "#1768ac");
        canvas.DrawText("Total", 358, totalsY + 7, 9, true, "#ffffff");
        canvas.DrawTextRight(FormatCurrency(document.GrandTotal), 560, totalsY + 7, 9, true, "#ffffff");

        canvas.DrawText("Documento generado automaticamente desde el modulo Hardware.", 42, 70, 8, false, "#617181");
        canvas.DrawText("Aprobador: adaza@digitaltechcolombia.com", 42, 56, 8, false, "#617181");
        return canvas.ToPdf();
    }

    private static void DrawPdfCell(PdfCanvas canvas, string value, double x, double y, PdfColumn column, double rowHeight, double fontSize, string color)
    {
        var text = TruncateForPdf(value, column.Width - 10, fontSize);
        if (column.RightAligned)
            canvas.DrawTextRight(text, x + column.Width - 5, y + (rowHeight / 2) - 3, fontSize, false, color);
        else
            canvas.DrawText(text, x + 5, y + (rowHeight / 2) - 3, fontSize, false, color);
    }

    private static string RequireText(string? value, string label, int maxLength)
    {
        var text = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"{label} es obligatorio.");

        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string FormatCurrency(decimal value) =>
        $"COP {value.ToString("N0", Culture)}";

    private static string FormatPercent(decimal value) =>
        $"{value.ToString("0.##", Culture)}%";

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string Html(string? value) =>
        WebUtility.HtmlEncode(value ?? "");

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(invalidChars.Contains(character) ? '-' : character);

        return builder.ToString();
    }

    private static string TruncateForPdf(string value, double width, double fontSize)
    {
        var safe = ToPdfSafeText(value);
        var maxChars = Math.Max(4, (int)Math.Floor(width / (fontSize * 0.52)));
        return safe.Length <= maxChars ? safe : safe[..Math.Max(1, maxChars - 1)] + ".";
    }

    private static string ToPdfSafeText(string value)
    {
        var normalized = (value ?? "").Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(character <= 126 ? character : ' ');
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed record PdfColumn(string Title, double Width, bool RightAligned);

    private sealed class PdfCanvas
    {
        private readonly StringBuilder _content = new();

        public void FillRectangle(double x, double y, double width, double height, string color)
        {
            AppendColor(color, fill: true);
            _content.AppendLine($"{N(x)} {N(y)} {N(width)} {N(height)} re f");
        }

        public void StrokeRectangle(double x, double y, double width, double height, string color)
        {
            AppendColor(color, fill: false);
            _content.AppendLine("0.7 w");
            _content.AppendLine($"{N(x)} {N(y)} {N(width)} {N(height)} re S");
        }

        public void DrawText(string text, double x, double y, double fontSize, bool bold, string color)
        {
            AppendColor(color, fill: true);
            _content.AppendLine($"BT /{(bold ? "F2" : "F1")} {N(fontSize)} Tf 1 0 0 1 {N(x)} {N(y)} Tm ({EscapePdfText(ToPdfSafeText(text))}) Tj ET");
        }

        public void DrawTextRight(string text, double rightX, double y, double fontSize, bool bold, string color)
        {
            var safe = ToPdfSafeText(text);
            var width = safe.Length * fontSize * 0.52;
            DrawText(safe, rightX - width, y, fontSize, bold, color);
        }

        public byte[] ToPdf()
        {
            var contentBytes = Encoding.ASCII.GetBytes(_content.ToString());
            var objects = new List<byte[]>
            {
                Ascii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"),
                Ascii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n"),
                Ascii("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>\nendobj\n"),
                Ascii("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n"),
                Ascii("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>\nendobj\n"),
                Combine(
                    Ascii($"6 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n"),
                    contentBytes,
                    Ascii("\nendstream\nendobj\n"))
            };

            using var stream = new MemoryStream();
            Write(stream, Ascii("%PDF-1.4\n"));
            var offsets = new List<long> { 0 };
            foreach (var item in objects)
            {
                offsets.Add(stream.Position);
                Write(stream, item);
            }

            var xrefOffset = stream.Position;
            Write(stream, Ascii($"xref\n0 {objects.Count + 1}\n"));
            Write(stream, Ascii("0000000000 65535 f \n"));
            foreach (var offset in offsets.Skip(1))
                Write(stream, Ascii($"{offset:0000000000} 00000 n \n"));

            Write(stream, Ascii($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF"));
            return stream.ToArray();
        }

        private void AppendColor(string color, bool fill)
        {
            var (r, g, b) = ParseHexColor(color);
            _content.AppendLine($"{N(r)} {N(g)} {N(b)} {(fill ? "rg" : "RG")}");
        }

        private static (double R, double G, double B) ParseHexColor(string color)
        {
            var hex = (color ?? "#000000").Trim().TrimStart('#');
            if (hex.Length != 6)
                hex = "000000";

            return (
                Convert.ToInt32(hex[..2], 16) / 255d,
                Convert.ToInt32(hex.Substring(2, 2), 16) / 255d,
                Convert.ToInt32(hex.Substring(4, 2), 16) / 255d);
        }

        private static string EscapePdfText(string value) =>
            (value ?? "")
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("(", "\\(", StringComparison.Ordinal)
                .Replace(")", "\\)", StringComparison.Ordinal)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal);

        private static string N(double value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);

        private static byte[] Ascii(string value) =>
            Encoding.ASCII.GetBytes(value);

        private static byte[] Combine(params byte[][] chunks)
        {
            using var stream = new MemoryStream();
            foreach (var chunk in chunks)
                Write(stream, chunk);

            return stream.ToArray();
        }

        private static void Write(Stream stream, byte[] bytes) =>
            stream.Write(bytes, 0, bytes.Length);
    }
}

public sealed class HardwarePurchaseOrderDocument
{
    public string RequestId { get; set; } = "";
    public string OrderNumber { get; set; } = "";
    public string OrderDate { get; set; } = "";
    public string OrderDateDisplay { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public string RequesterName { get; set; } = "";
    public string RequesterEmail { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string CompanyNit { get; set; } = "";
    public string CompanyAddress { get; set; } = "";
    public string CompanyCity { get; set; } = "";
    public IReadOnlyList<HardwarePurchaseOrderDocumentLine> Lines { get; set; } = Array.Empty<HardwarePurchaseOrderDocumentLine>();
    public decimal SubtotalBeforeVat { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public string PdfFileName { get; set; } = "";
    public byte[] PdfContent { get; set; } = Array.Empty<byte>();
    public string EmailHtml { get; set; } = "";
    public string ApprovalSummary { get; set; } = "";
}

public sealed class HardwarePurchaseOrderDocumentLine
{
    public string Product { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitValueBeforeVat { get; set; }
    public decimal TotalBeforeVat { get; set; }
    public decimal VatPercent { get; set; }
    public decimal VatValue { get; set; }
    public decimal TotalWithVat { get; set; }
}
