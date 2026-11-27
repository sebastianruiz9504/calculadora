using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services;

public static partial class PortfolioCollectionRules
{
    public const int MinimumInvoice = 7504;
    public static bool IsVisible(string? invoice) => int.TryParse(InvoiceSuffix().Match(invoice?.Trim() ?? "").Value, out var n) && n >= MinimumInvoice;
    [GeneratedRegex(@"\d+$", RegexOptions.CultureInvariant)] private static partial Regex InvoiceSuffix();
    public static string SearchKey(string? value) => string.Concat((value ?? "").Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToUpperInvariant();
    public static string[] Emails(string? value, bool strict = true)
    {
        var result = new List<string>();
        foreach (var part in (value ?? "").Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!MailAddress.TryCreate(part, out var address) || !string.Equals(address.Address, part, StringComparison.OrdinalIgnoreCase) || !part.Contains('.'))
            {
                if (strict) throw new InvalidOperationException($"Correo no válido: {part}");
                continue;
            }
            result.Add(address.Address);
        }
        var emails = result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (emails.Length > 20) throw new InvalidOperationException("Máximo 20 destinatarios por correo.");
        if (string.Join(";", emails).Length > 3900) throw new InvalidOperationException("La lista de destinatarios es demasiado larga.");
        return emails;
    }
    public static Guid AutomaticId(string invoiceId) => new(SHA256.HashData(Encoding.UTF8.GetBytes("cartera-primer-aviso-v1:" + Guid.Parse(invoiceId).ToString("D")))[..16]);
    public static DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "America/Bogota").DateTime);
    public static bool IsNewlyOverdue(BillingInvoiceRowDto row, DateOnly activation, DateOnly today) =>
        IsVisible(row.InvoiceNumber) && row.IsPortfolioPending && DateOnly.TryParse(row.DueDateValue, out var due) && due < today && due.AddDays(1) >= activation;
    public static string StatementVersion(IReadOnlyList<BillingInvoiceRowDto> rows) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", rows.OrderBy(r => r.RecordId).Select(r => $"{r.RecordId}:{r.NetTotalInvoice.ToString(CultureInfo.InvariantCulture)}:{r.DueDateValue}")))));
    public static CollectionEmailPreview Preview(string template, string client, IReadOnlyList<BillingInvoiceRowDto> rows)
    {
        var (subject, text) = template switch
        {
            "first" => ("Factura en mora · Digital Tech", "Le informamos que su factura ha entrado en mora. Le invitamos a realizar el pago de las facturas relacionadas para evitar interrupciones del servicio."),
            "second" => ("Segundo recordatorio de pago · Digital Tech", "Le recordamos que las facturas relacionadas continúan pendientes de pago. Agradecemos realizar el pago o informarnos la fecha de su compromiso de pago para mantener la continuidad del servicio."),
            "warning" => ("Aviso de posible suspensión por mora · Digital Tech", "A la fecha registramos las facturas vencidas relacionadas. Le invitamos a regularizar el pago a la mayor brevedad para evitar la suspensión del servicio, de acuerdo con las condiciones de su contrato. Por favor comparta su comprobante o contacte a nuestro equipo de cartera."),
            "suspension" => ("Notificación de suspensión por mora · Digital Tech", "Le notificamos la suspensión del servicio por las obligaciones vencidas relacionadas, conforme a las condiciones de su contrato. Para coordinar la reactivación, agradecemos realizar el pago y compartir el comprobante con nuestro equipo de cartera."),
            _ => throw new InvalidOperationException("Selecciona una plantilla válida.")
        };
        return new(subject, $"Estimado cliente {client},\n\n{text}\n\nSi ya realizó el pago, por favor envíenos el comprobante para verificar y actualizar su estado de cuenta.\n\nCordialmente,\nEquipo de Cartera\nDigital Tech", StatementHtml(client, rows), StatementVersion(rows));
    }
    public static string StatementHtml(string client, IReadOnlyList<BillingInvoiceRowDto> rows)
    {
        static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
        static string Money(decimal n) => n.ToString("C0", CultureInfo.GetCultureInfo("es-CO"));
        var body = new StringBuilder($"<h2>Estado de cuenta — facturas vencidas</h2><p>{H(client)} · Corte {Today():dd/MM/yyyy} · Valores en COP</p><table style='border-collapse:collapse;width:100%' border='1' cellpadding='8'><thead><tr><th>Factura</th><th>Emisión</th><th>Vencimiento</th><th>Días en mora</th><th>Valor factura</th><th>Notas crédito</th><th>Saldo pendiente</th></tr></thead><tbody>");
        foreach (var r in rows)
        {
            var label = H(r.InvoiceNumber);
            if (Uri.TryCreate(r.PublicUrl, UriKind.Absolute, out var link) && link.Scheme == "https") label = $"<a href='{H(link.AbsoluteUri)}'>{label}</a>";
            body.Append($"<tr><td>{label}</td><td>{H(r.EmissionDateDisplay)}</td><td>{H(r.DueDateDisplay)}</td><td>{r.AgeDays}</td><td>{Money(r.TotalInvoice)}</td><td>{Money(r.CreditNoteTotal)}</td><td>{Money(r.NetTotalInvoice)}</td></tr>");
        }
        return body.Append($"</tbody></table><p><strong>Total vencido: {Money(rows.Sum(r => r.NetTotalInvoice))}</strong></p>").ToString();
    }
    public static string EmailHtml(string message, string statement) => $"<html><body style='font-family:Arial,sans-serif;color:#203047'><div style='white-space:pre-wrap'>{WebUtility.HtmlEncode(message)}</div>{statement}</body></html>";
}
