(() => {
    'use strict';
    const root = document.getElementById('collectionWorkspace');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const base = root.dataset.baseUrl;
    const dialog = $('collectionDialog');
    const money = new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 });
    const date = value => value ? new Date(value).toLocaleString('es-CO', { timeZone: 'America/Bogota', dateStyle: 'medium', timeStyle: 'short' }) : '';
    const escape = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    let page = 1, pages = 1, timer, abort, seq = 0, selected = '', detail, historyPage = 1, dialogSeq = 0, previewSeq = 0;
    let lastKey = '', lastTime = 0, sending = false, noteId, sendId, statementVersion = '';
    function status(id, message, error = false) { $(id).textContent = message || ''; $(id).classList.toggle('collection-error', error); }
    async function api(path, { data, signal } = {}) {
        const response = await fetch(`${base}/${path}`, { method: data ? 'POST' : 'GET', signal, cache: 'no-store', headers: data ? { 'Content-Type': 'application/json', RequestVerificationToken: root.querySelector('[name="__RequestVerificationToken"]').value } : {}, body: data ? JSON.stringify(data) : undefined });
        if (response.redirected || response.headers.get('content-type')?.includes('text/html')) throw new Error('La sesión venció. Recarga la página para iniciar sesión.');
        const result = await response.json();
        if (!response.ok) throw new Error(result.message || 'No fue posible completar la operación.');
        return result;
    }
    async function load(refresh = false) {
        const q = $('portfolioClientSearch').value.trim(), size = $('collectionPageSize').value, sort = $('collectionSort').value;
        const key = JSON.stringify([q, page, size, sort]);
        if (!refresh && key === lastKey && Date.now() - lastTime < 60000) {
            status('collectionStatus', ''); $('portfolioOverdueTable').setAttribute('aria-busy', 'false');
            $('collectionPrevious').disabled = page <= 1; $('collectionNext').disabled = page >= pages;
            return;
        }
        abort?.abort(); abort = new AbortController(); const version = ++seq;
        $('portfolioOverdueTable').setAttribute('aria-busy', 'true');
        $('collectionPrevious').disabled = $('collectionNext').disabled = true;
        status('collectionStatus', 'Cargando facturas…');
        try {
            const data = await api(`Page?${new URLSearchParams({ q, page, pageSize: size, sort, refresh })}`, { signal: abort.signal });
            if (version !== seq) return;
            page = data.page; pages = data.pages; lastKey = JSON.stringify([q, page, size, sort]); lastTime = Date.now();
            $('portfolioUnpaidBody').innerHTML = data.items.length ? data.items.map(r => `<tr data-invoice="${escape(r.recordId)}"><td><button type="button" class="collection-invoice">${escape(r.invoiceNumber)}</button></td><td>${escape(r.clientName)}<small class="collection-muted d-block">${escape(r.companyTaxId)}</small></td><td>${escape(r.verticalLabel)}</td><td>${escape(r.contractTypeLabel)}</td><td>${escape(r.dueDateDisplay)}</td><td class="text-center"><span class="collection-days">${r.ageDays} días</span></td><td class="text-end">${escape(money.format(r.netTotalInvoice))}</td></tr>`).join('') : '<tr><td colspan="7">No hay facturas vencidas que coincidan con la búsqueda.</td></tr>';
            const start = data.total ? (page - 1) * data.pageSize + 1 : 0, end = Math.min(page * data.pageSize, data.total);
            $('portfolioResultsCount').textContent = `${start}–${end} de ${data.total} facturas`;
            $('collectionPageLabel').textContent = `Página ${page} de ${pages}`;
            $('collectionUpdated').textContent = `Actualizado ${date(data.refreshedAt)}`;
            document.dispatchEvent(new CustomEvent('portfolio:loaded', { detail: data.summary }));
            $('collectionPrevious').disabled = page <= 1; $('collectionNext').disabled = page >= pages;
            status('collectionStatus', '');
        } catch (error) { if (version === seq && error.name !== 'AbortError') status('collectionStatus', error.message, true); }
        finally { if (version === seq) $('portfolioOverdueTable').setAttribute('aria-busy', 'false'); }
    }
    window.PortfolioCollections = { load };
    $('portfolioClientSearch').addEventListener('input', () => { clearTimeout(timer); abort?.abort(); ++seq; page = 1; timer = setTimeout(() => load(), 300); });
    ['collectionSort', 'collectionPageSize'].forEach(id => $(id).addEventListener('change', () => { page = 1; load(); }));
    $('portfolioOverdueClearFiltersBtn').addEventListener('click', () => { clearTimeout(timer); $('portfolioClientSearch').value = ''; $('collectionSort').value = 'oldest'; page = 1; load(true); });
    $('collectionPrevious').addEventListener('click', () => { page--; load(); });
    $('collectionNext').addEventListener('click', () => { page++; load(); });
    $('portfolioUnpaidBody').addEventListener('click', e => { const row = e.target.closest('[data-invoice]'); if (row) open(row.dataset.invoice); });
    function historyMarkup(rows) {
        const labels = { automatic: 'Correo automático', first: 'Primer recordatorio', second: 'Segundo correo', warning: 'Aviso de suspensión', suspension: 'Suspensión', note: 'Observación', issue: 'Aviso automático pendiente' };
        return [...rows].reverse().map(r => {
            const state = r.status === 'sent' ? `enviado ${date(r.sentAt)}` : r.status === 'recorded' || r.status === 'blocked' ? date(r.createdAt) : r.status === 'sending' ? 'envío en curso o pendiente de verificación' : 'envío no confirmado; revisar el buzón';
            return `<article class="collection-event ${r.kind === 'note' ? 'collection-event--note' : ''}"><strong>${escape(labels[r.kind] || r.kind)} ${escape(state)}</strong><small>${escape(r.author)} · ${escape(date(r.createdAt))}</small>${r.recipients ? `<small>Para: ${escape(r.recipients)}</small>` : ''}${r.subject ? `<p><strong>${escape(r.subject)}</strong></p>` : ''}<p>${escape(r.message)}</p></article>`;
        }).join('');
    }
    function renderHistory(data, prepend = false) {
        if (prepend) $('collectionHistory').insertAdjacentHTML('afterbegin', historyMarkup(data.items));
        else $('collectionHistory').innerHTML = data.items.length ? historyMarkup(data.items) : '<p class="collection-muted">Aún no hay gestiones registradas.</p>';
        $('collectionOlder').hidden = !data.hasMore; historyPage = data.page;
        if (!prepend) $('collectionHistory').scrollTop = $('collectionHistory').scrollHeight;
    }
    async function refreshHistory() {
        const id = selected, version = dialogSeq;
        const data = await api(`History?${new URLSearchParams({ invoiceId: id, page: 1 })}`);
        if (version === dialogSeq && id === selected) renderHistory(data);
    }
    async function template() {
        const version = ++previewSeq, id = selected;
        $('collectionSend').disabled = true;
        $('collectionSuspensionLabel').hidden = $('collectionTemplate').value !== 'suspension';
        $('collectionSuspensionConfirm').checked = false;
        try {
            const data = detail.templates[$('collectionTemplate').value];
            if (version !== previewSeq || id !== selected) return;
            $('collectionSubject').value = data.subject; $('collectionMessage').value = data.message;
            $('collectionStatement').innerHTML = data.statementHtml; statementVersion = data.statementVersion;
            $('collectionSend').disabled = !detail?.contact.clientId;
        } catch (error) { if (version === previewSeq) status('collectionDialogStatus', error.message, true); }
    }
    async function open(id) {
        selected = id; const version = ++dialogSeq; ++previewSeq; detail = null; sendId = crypto.randomUUID(); noteId = crypto.randomUUID(); sending = false;
        $('collectionDialogContent').hidden = true; $('collectionTitle').textContent = 'Cargando factura…'; $('collectionClient').textContent = '';
        $('collectionNote').value = ''; $('collectionAdditional').value = ''; $('collectionSaveRecipients').checked = false; $('collectionTemplate').value = 'second';
        status('collectionDialogStatus', 'Cargando estado de cuenta e historial…');
        if (!dialog.open) dialog.showModal();
        try {
            const data = await api(`Detail?${new URLSearchParams({ invoiceId: id })}`);
            if (version !== dialogSeq) return;
            detail = data; $('collectionTitle').textContent = data.invoice.invoiceNumber; $('collectionClient').textContent = `${data.invoice.clientName} · ${money.format(data.invoice.netTotalInvoice)} · ${data.invoice.ageDays} días en mora`;
            $('collectionRecipients').value = data.contact.recipients.join('; ');
            $('collectionContactSource').textContent = `Correo electrónico: ${data.contact.email || 'sin registrar'} · Facturación: ${data.contact.billingEmail || 'sin registrar'} · Cobranza: ${data.contact.collectionEmail || 'sin registrar'}`;
            renderHistory(data.history); $('collectionDialogContent').hidden = false;
            status('collectionDialogStatus', data.contact.clientId ? '' : 'La factura no tiene un cliente vinculado. Puedes registrar observaciones; vincula el cliente para enviar correos.', !data.contact.clientId);
            await template();
        } catch (error) { if (version === dialogSeq) status('collectionDialogStatus', error.message, true); }
    }
    $('collectionClose').addEventListener('click', () => { if (!sending) dialog.close(); });
    dialog.addEventListener('cancel', e => { if (sending) e.preventDefault(); });
    dialog.addEventListener('close', () => { ++dialogSeq; ++previewSeq; selected = ''; });
    $('collectionTemplate').addEventListener('change', template);
    $('collectionSaveContact').addEventListener('click', async () => {
        $('collectionSaveContact').disabled = true;
        try {
            const contact = await api('Recipients', { data: { invoiceId: selected, additionalRecipients: $('collectionAdditional').value } });
            detail.contact = contact;
            $('collectionContactSource').textContent = `Correo electrónico: ${contact.email || 'sin registrar'} · Facturación: ${contact.billingEmail || 'sin registrar'} · Cobranza: ${contact.collectionEmail || 'sin registrar'}`;
            status('collectionDialogStatus', 'Correo cobranza guardado en el cliente.');
        } catch (error) { status('collectionDialogStatus', error.message, true); }
        finally { $('collectionSaveContact').disabled = false; }
    });
    $('collectionOlder').addEventListener('click', async () => {
        const version = dialogSeq; $('collectionOlder').disabled = true;
        try { const data = await api(`History?${new URLSearchParams({ invoiceId: selected, page: historyPage + 1 })}`); if (version === dialogSeq) renderHistory(data, true); }
        catch (error) { status('collectionDialogStatus', error.message, true); }
        finally { $('collectionOlder').disabled = false; }
    });
    $('collectionNoteForm').addEventListener('submit', async e => {
        e.preventDefault(); const version = dialogSeq; $('collectionNoteSave').disabled = true;
        try { await api('Note', { data: { requestId: noteId, invoiceId: selected, message: $('collectionNote').value } }); if (version !== dialogSeq) return; noteId = crypto.randomUUID(); $('collectionNote').value = ''; await refreshHistory(); status('collectionDialogStatus', 'Observación guardada.'); }
        catch (error) { if (version === dialogSeq) status('collectionDialogStatus', error.message, true); }
        finally { $('collectionNoteSave').disabled = false; }
    });
    $('collectionEmailForm').addEventListener('submit', async e => {
        e.preventDefault(); if (sending) return;
        if ($('collectionTemplate').value === 'suspension' && !$('collectionSuspensionConfirm').checked) { status('collectionDialogStatus', 'Confirma que corresponde notificar la suspensión.', true); return; }
        sending = true; $('collectionSend').disabled = $('collectionClose').disabled = true; status('collectionDialogStatus', 'Verificando saldos y enviando correo…');
        try {
            const result = await api('Send', { data: { requestId: sendId, invoiceId: selected, template: $('collectionTemplate').value, recipients: $('collectionRecipients').value, additionalRecipients: $('collectionAdditional').value, saveAdditionalRecipients: $('collectionSaveRecipients').checked, subject: $('collectionSubject').value, message: $('collectionMessage').value, statementVersion } });
            status('collectionDialogStatus', result.message, result.status !== 'sent'); await refreshHistory();
            // Keep the same operation ID: another click or network retry cannot duplicate this email.
            $('collectionSend').textContent = result.status === 'sent' ? 'Correo enviado' : 'Envío registrado · revisar historial';
        } catch (error) { status('collectionDialogStatus', error.message, true); $('collectionSend').disabled = false; }
        finally { sending = false; $('collectionClose').disabled = false; }
    });
    dialog.addEventListener('close', () => { $('collectionSend').textContent = 'Enviar correo y registrar gestión'; });
})();
