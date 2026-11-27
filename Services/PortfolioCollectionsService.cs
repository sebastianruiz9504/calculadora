using System.Security.Claims;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Reportes;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public sealed class PortfolioCollectionsService(IPortfolioCollectionsRepository repository, IReportesEmailSender mail, IOptions<CollectionOptions> options, ILogger<PortfolioCollectionsService> logger)
{
    public async Task<PortfolioPage> PageAsync(string? q, int page, int size, string? sort, bool refresh, CancellationToken ct)
    {
        var snapshot = await repository.GetCollectionSnapshotAsync(refresh, ct: ct);
        var key = PortfolioCollectionRules.SearchKey(q);
        var rows = snapshot.Rows.Where(r => r.IsOverdue && (key.Length == 0 || PortfolioCollectionRules.SearchKey($"{r.InvoiceNumber} {r.ClientName} {r.CompanyTaxId} {r.VerticalLabel} {r.ContractTypeLabel}").Contains(key)));
        rows = sort switch { "client" => rows.OrderBy(r => r.ClientName).ThenBy(r => r.DueDateValue), "amount" => rows.OrderByDescending(r => r.NetTotalInvoice), "newest" => rows.OrderByDescending(r => r.DueDateValue), _ => rows.OrderBy(r => r.DueDateValue) };
        var filtered = rows.ThenStable().ToArray();
        size = size is 25 or 50 or 100 ? size : 50;
        var pages = Math.Max(1, (int)Math.Ceiling(filtered.Length / (double)size));
        page = Math.Clamp(page, 1, pages);
        var summary = new PortfolioDashboardDto { AsOfDateLabel = PortfolioCollectionRules.Today().ToString("dd/MM/yyyy"), FocusLabel = "Cartera total y vencida", HasData = snapshot.Rows.Count > 0, RecordsCount = snapshot.Rows.Count(r => r.IsOverdue),
            Kpis = new[] { (645250000, "Cloud"), (645250001, "Copiers") }.Select(v => new PortfolioKpiDto { Key = v.Item2.ToLowerInvariant() + "-portfolio", Label = "Cartera " + v.Item2, Hint = "Facturas desde la 7504 sin pago.", Value = snapshot.Rows.Where(r => r.VerticalOptionValue == v.Item1).Sum(r => r.NetTotalInvoice), SecondaryLabel = "Vencidas sin pago", SecondaryValue = snapshot.Rows.Where(r => r.IsOverdue && r.VerticalOptionValue == v.Item1).Sum(r => r.NetTotalInvoice).ToString("C0", System.Globalization.CultureInfo.GetCultureInfo("es-CO")) }).ToArray() };
        return new(filtered.Skip((page - 1) * size).Take(size).ToArray(), page, size, filtered.Length, pages, snapshot.RefreshedAt, summary);
    }
    public async Task<CollectionDetail> DetailAsync(string invoiceId, CancellationToken ct)
    {
        var id = Guid.Parse(invoiceId).ToString("D");
        var snapshot = await repository.GetCollectionSnapshotAsync(ct: ct);
        var invoice = snapshot.Rows.FirstOrDefault(r => r.RecordId.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("La factura ya no está pendiente. Actualiza la tabla.");
        var contact = string.IsNullOrWhiteSpace(invoice.ClientId)
            ? new CollectionContact("", invoice.ClientName, "", "", "", Array.Empty<string>())
            : await repository.GetCollectionContactAsync(invoice.ClientId, invoice.ClientName, ct);
        var rows = snapshot.Rows.Where(r => r.IsOverdue && (string.IsNullOrWhiteSpace(invoice.ClientId) ? r.RecordId == id : r.ClientId == invoice.ClientId)).ToArray();
        return new(invoice, contact, rows, await repository.GetCollectionHistoryAsync(id, 1, ct),
            new[] { "first", "second", "warning", "suspension" }.ToDictionary(t => t, t => PortfolioCollectionRules.Preview(t, invoice.ClientName, rows)));
    }
    public async Task<BillingInvoiceRowDto> InvoiceAsync(string invoiceId, CancellationToken ct)
    {
        var id = Guid.Parse(invoiceId).ToString("D");
        var snapshot = await repository.GetCollectionSnapshotAsync(ct: ct);
        return snapshot.Rows.FirstOrDefault(r => r.RecordId == id) ?? throw new InvalidOperationException("La factura ya no está pendiente. Actualiza la tabla.");
    }
    public async Task AddNoteAsync(CollectionNoteRequest request, string author, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 8000) throw new InvalidOperationException("Escribe una observación de 1 a 8.000 caracteres.");
        var invoice = await InvoiceAsync(request.InvoiceId, ct);
        await repository.TryCreateCollectionEventAsync(request.RequestId, invoice.RecordId, invoice.ClientId, "note", "recorded", author, "", "", request.Message.Trim(), new[] { invoice.RecordId }, ct);
    }
    public async Task<CollectionSendResult> SendAsync(CollectionEmailRequest request, string author, bool automatic, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty) throw new InvalidOperationException("Falta el identificador del envío.");
        var invoice = await InvoiceAsync(request.InvoiceId, ct);
        var prior = await repository.GetCollectionEventStatusAsync(request.RequestId, ct);
        if (prior is not null) return ExistingResult(prior);
        if (string.IsNullOrWhiteSpace(invoice.ClientId)) throw new InvalidOperationException("Vincula el cliente antes de enviar el correo.");
        var contact = await repository.GetCollectionContactAsync(invoice.ClientId, invoice.ClientName, ct);
        // Always read current balances directly before collecting; cached UI balances are not used to send.
        var current = await repository.GetCollectionSnapshotAsync(true, invoice.ClientId, ct);
        var rows = current.Rows.Where(r => r.IsOverdue).OrderBy(r => r.DueDateValue).ThenBy(r => r.RecordId).ToArray();
        if (!rows.Any(r => r.RecordId == invoice.RecordId)) throw new InvalidOperationException("La factura ya no está vencida sin pago. Actualiza la tabla.");
        var preview = PortfolioCollectionRules.Preview(request.Template, invoice.ClientName, rows);
        if (!automatic && preview.StatementVersion != request.StatementVersion) throw new InvalidOperationException("El estado de cuenta cambió. Cierra y abre la factura para revisar los saldos antes de enviar.");
        var subject = automatic ? preview.Subject : request.Subject.Trim();
        var message = automatic ? preview.Message : request.Message.Trim();
        if (subject.Length is < 1 or > 250 || message.Length is < 1 or > 12000) throw new InvalidOperationException("Revisa el asunto (hasta 250 caracteres) y el mensaje (hasta 12.000).");
        var recipients = automatic ? contact.Recipients.ToArray() : PortfolioCollectionRules.Emails(request.Recipients + ";" + request.AdditionalRecipients);
        if (recipients.Length == 0) throw new InvalidOperationException("El cliente no tiene un correo válido. Agrega un destinatario.");
        if (request.SaveAdditionalRecipients && !string.IsNullOrWhiteSpace(request.AdditionalRecipients)) await repository.SaveCollectionContactAsync(invoice.ClientId, request.AdditionalRecipients, ct);
        var statement = new AccountStatementDto { ClientId = invoice.ClientId, ClientName = invoice.ClientName, AsOfDateLabel = PortfolioCollectionRules.Today().ToString("dd/MM/yyyy"), HasData = true, TotalAmount = rows.Sum(r => r.NetTotalInvoice), RecordsCount = rows.Length,
            Invoices = rows.Select(r => new AccountStatementInvoiceDto { RecordId = r.RecordId, InvoiceNumber = r.InvoiceNumber, TotalInvoice = r.TotalInvoice, CreditNoteTotal = r.CreditNoteTotal, NetTotalInvoice = r.NetTotalInvoice, EmissionDateDisplay = r.EmissionDateDisplay, DueDateDisplay = r.DueDateDisplay, DaysDisplay = $"{r.AgeDays} días", DaysValue = r.AgeDays, StateLabel = "Vencida", StateKey = "overdue", PublicUrl = r.PublicUrl }).ToArray() };
        var pdf = DashboardController.BuildAccountStatementPdf(statement);
        var body = PortfolioCollectionRules.EmailHtml(message, preview.StatementHtml);
        var claimed = await repository.TryCreateCollectionEventAsync(request.RequestId, invoice.RecordId, invoice.ClientId, automatic ? "automatic" : request.Template, "sending", author,
            string.Join("; ", recipients), subject, message + "\n\nEstado de cuenta adjunto (COP), corte " + PortfolioCollectionRules.Today().ToString("dd/MM/yyyy") + ":\n" + string.Join("\n", rows.Select(r => $"{r.InvoiceNumber} · Emisión {r.EmissionDateDisplay} · Vence {r.DueDateDisplay} · {r.AgeDays} días · Total {r.TotalInvoice:N2} · NC {r.CreditNoteTotal:N2} · Saldo {r.NetTotalInvoice:N2}")), rows.Select(r => r.RecordId), ct);
        if (!claimed) return ExistingResult(await repository.GetCollectionEventStatusAsync(request.RequestId, ct) ?? "sending");
        // After a durable claim, disconnecting the browser must not interrupt delivery bookkeeping.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            await mail.SendAsync(new ReportesEmailMessage { SenderMailbox = options.Value.Sender, To = recipients, Subject = subject, HtmlBody = body,
                Attachments = new[] { new ReporteEmailAttachment { FileName = $"estado-de-cuenta-{PortfolioCollectionRules.Today():yyyyMMdd}.pdf", ContentType = "application/pdf", Content = pdf } } }, new ClaimsPrincipal(), timeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo confirmar el envío de cobranza {EventId}", request.RequestId);
            await repository.CompleteCollectionEventAsync(request.RequestId, "uncertain", CancellationToken.None);
            return new("uncertain", "No se pudo confirmar el envío. Revisa el buzón de cartera antes de reenviar; quedó registrado en el historial.");
        }
        // If this write fails, 'sending' remains durable and blocks duplicate automatic sends.
        await repository.CompleteCollectionEventAsync(request.RequestId, "sent", CancellationToken.None);
        return new("sent", "Correo aceptado por Microsoft 365. Estado de cuenta PDF adjunto y gestión registrada.");
    }
    private static CollectionSendResult ExistingResult(string status) => new(status, status == "sent" ? "Este correo ya fue enviado; no se duplicó." : "Este envío ya está registrado. Revisa su estado en el historial antes de reenviar.");
    public async Task RunAutomaticAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled || !DateOnly.TryParse(options.Value.ActivationDate, out var activation)) return;
        var snapshot = await repository.GetCollectionSnapshotAsync(true, ct: ct);
        var candidates = snapshot.Rows.Where(r => PortfolioCollectionRules.IsNewlyOverdue(r, activation, PortfolioCollectionRules.Today())).ToArray();
        var claimed = await repository.GetAutomaticCollectionClaimsAsync(candidates.Select(r => r.RecordId), ct);
        foreach (var row in candidates.Where(r => !claimed.Contains(r.RecordId)))
        {
            ct.ThrowIfCancellationRequested();
            try { await SendAsync(new CollectionEmailRequest { RequestId = PortfolioCollectionRules.AutomaticId(row.RecordId), InvoiceId = row.RecordId, Template = "first" }, "Automatización de cartera", true, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Aviso de mora pendiente para factura {InvoiceId}", row.RecordId);
                // A single durable issue is visible in the popup; the next run retries prerequisites.
                try { await repository.TryCreateCollectionEventAsync(PortfolioCollectionRules.AutomaticId(PortfolioCollectionRules.AutomaticId(row.RecordId).ToString()), row.RecordId, row.ClientId, "issue", "blocked", "Automatización de cartera", "", "Aviso automático pendiente", "No se pudo preparar el primer aviso. Revisa el cliente vinculado, sus correos y el saldo. El sistema volverá a intentarlo.", new[] { row.RecordId }, ct); }
                catch (Exception historyError) { logger.LogError(historyError, "No se pudo registrar el bloqueo de cobranza"); }
            }
        }
    }
}

internal static class CollectionOrdering
{
    internal static IEnumerable<BillingInvoiceRowDto> ThenStable(this IEnumerable<BillingInvoiceRowDto> rows) => rows is IOrderedEnumerable<BillingInvoiceRowDto> ordered ? ordered.ThenBy(r => r.RecordId, StringComparer.Ordinal) : rows;
}

public sealed class PortfolioCollectionsWorker(IServiceScopeFactory scopes, IOptions<CollectionOptions> options, ILogger<PortfolioCollectionsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PortfolioCollectionsService>().RunAutomaticAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogError(ex, "Error en la revisión automática de cartera"); }
            await Task.Delay(TimeSpan.FromMinutes(Math.Clamp(options.Value.IntervalMinutes, 5, 60)), stoppingToken);
        }
    }
}
