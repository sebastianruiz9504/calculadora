(function () {
    const roots = Array.from(document.querySelectorAll("#enviosApp, #enviosTransportadorApp"));
    if (!roots.length) {
        return;
    }

    const STATUS_OPEN = 645250000;
    const STATUS_SCHEDULED = 645250001;
    const STATUS_PICKUP_APPROVED = 645250002;
    const STATUS_DELIVERED = 645250003;
    const STATUS_CLOSED = 645250004;

    const currencyFormatter = new Intl.NumberFormat("es-CO", {
        style: "currency",
        currency: "COP",
        minimumFractionDigits: 0,
        maximumFractionDigits: 0
    });

    const numberFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 0
    });

    roots.forEach(initializeEnvios);

    function initializeEnvios(root) {
        const mode = root.dataset.mode || "usuario";
        const config = {
            mode,
            loadUrl: root.dataset.loadUrl || "",
            clientSearchUrl: root.dataset.clientSearchUrl || "",
            createUrl: root.dataset.createUrl || "",
            approvePickupUrl: root.dataset.approvePickupUrl || "",
            approveDeliveryUrl: root.dataset.approveDeliveryUrl || "",
            downloadActUrl: root.dataset.downloadActUrl || "",
            scheduleUrl: root.dataset.scheduleUrl || "",
            confirmDeliveryUrl: root.dataset.confirmDeliveryUrl || ""
        };

        const elements = {
            status: root.querySelector("[data-env-status]"),
            month: root.querySelector("[data-env-month]"),
            refresh: root.querySelector("[data-env-refresh]"),
            newButton: root.querySelector("[data-env-new]"),
            monthLabel: root.querySelector("[data-env-month-label]"),
            calendar: root.querySelector("[data-env-calendar]"),
            rows: root.querySelector("[data-env-rows]"),
            empty: root.querySelector("[data-env-empty]"),
            count: root.querySelector("[data-env-count]"),
            summaries: {
                open: root.querySelector('[data-env-summary="open"]'),
                scheduled: root.querySelector('[data-env-summary="scheduled"]'),
                pickup: root.querySelector('[data-env-summary="pickup"]'),
                delivered: root.querySelector('[data-env-summary="delivered"]'),
                freight: root.querySelector('[data-env-summary="freight"]')
            },
            modal: root.querySelector("[data-env-modal]"),
            modalStatus: root.querySelector("[data-env-modal-status]"),
            form: root.querySelector("[data-env-form]"),
            closeModalButtons: Array.from(root.querySelectorAll("[data-env-close-modal]")),
            clientId: root.querySelector("[data-env-client-id]"),
            clientOptions: root.querySelector("[data-env-client-options]"),
            fields: {
                origin: root.querySelector('[data-env-field="origin"]'),
                destination: root.querySelector('[data-env-field="destination"]'),
                clientName: root.querySelector('[data-env-field="clientName"]'),
                whatIsSent: root.querySelector('[data-env-field="whatIsSent"]'),
                observations: root.querySelector('[data-env-field="observations"]'),
                recipientName: root.querySelector('[data-env-field="recipientName"]'),
                recipientPhone: root.querySelector('[data-env-field="recipientPhone"]')
            }
        };

        const state = {
            board: null,
            records: [],
            clientSuggestions: [],
            lookupTimer: 0,
            lookupSequence: 0,
            busy: false
        };

        if (elements.month) {
            elements.month.value = root.dataset.initialMonth || new Date().toISOString().slice(0, 7);
        }

        elements.refresh?.addEventListener("click", () => loadBoard({ force: true }));
        elements.month?.addEventListener("change", () => loadBoard({ force: true }));
        elements.newButton?.addEventListener("click", openModal);

        elements.rows?.addEventListener("click", async event => {
            const target = event.target;
            if (!(target instanceof HTMLElement)) {
                return;
            }

            const action = target.closest("[data-env-action]");
            if (!(action instanceof HTMLElement)) {
                return;
            }

            event.preventDefault();
            const rowElement = action.closest("[data-env-record-id]");
            const recordId = rowElement?.getAttribute("data-env-record-id") || "";
            if (!recordId) {
                return;
            }

            if (action.dataset.envAction === "approve-pickup") {
                await postRecordAction(config.approvePickupUrl, recordId, "Aprobando recogida...");
            } else if (action.dataset.envAction === "approve-delivery") {
                await approveDelivery(rowElement, recordId);
            } else if (action.dataset.envAction === "schedule") {
                await scheduleShipment(rowElement, recordId);
            } else if (action.dataset.envAction === "confirm-delivery") {
                await postRecordAction(config.confirmDeliveryUrl, recordId, "Confirmando entrega...");
            }
        });

        elements.closeModalButtons.forEach(button => {
            button.addEventListener("click", () => closeModal());
        });

        elements.modal?.addEventListener("click", event => {
            const target = event.target;
            if (target instanceof HTMLElement && target.hasAttribute("data-env-close-modal")) {
                closeModal();
            }
        });

        document.addEventListener("keydown", event => {
            if (event.key === "Escape" && elements.modal && !elements.modal.hidden) {
                closeModal();
            }
        });

        elements.fields.clientName?.addEventListener("input", () => {
            if (elements.clientId) {
                elements.clientId.value = "";
            }

            const query = (elements.fields.clientName.value || "").trim();
            window.clearTimeout(state.lookupTimer);

            if (query.length < 2 || !config.clientSearchUrl) {
                state.clientSuggestions = [];
                renderClientSuggestions();
                return;
            }

            const currentSequence = ++state.lookupSequence;
            state.lookupTimer = window.setTimeout(async () => {
                try {
                    const items = await fetchJson(buildClientSearchUrl(query));
                    if (currentSequence !== state.lookupSequence) {
                        return;
                    }

                    state.clientSuggestions = Array.isArray(items) ? items : [];
                    renderClientSuggestions();
                    syncClientSelection();
                } catch {
                    if (currentSequence !== state.lookupSequence) {
                        return;
                    }

                    state.clientSuggestions = [];
                    renderClientSuggestions();
                }
            }, 220);
        });

        elements.fields.clientName?.addEventListener("change", syncClientSelection);
        elements.fields.clientName?.addEventListener("blur", syncClientSelection);

        elements.form?.addEventListener("submit", async event => {
            event.preventDefault();
            await createShipment();
        });

        loadBoard();

        function buildLoadUrl() {
            const url = new URL(config.loadUrl, window.location.origin);
            const monthValue = elements.month?.value || "";
            if (monthValue.includes("-")) {
                const parts = monthValue.split("-");
                url.searchParams.set("year", parts[0]);
                url.searchParams.set("month", parts[1]);
            }

            return `${url.pathname}${url.search}`;
        }

        function buildClientSearchUrl(query) {
            const url = new URL(config.clientSearchUrl, window.location.origin);
            url.searchParams.set("q", query);
            return `${url.pathname}${url.search}`;
        }

        function buildDownloadActUrl(recordId) {
            const url = new URL(config.downloadActUrl, window.location.origin);
            url.searchParams.set("recordId", recordId);
            return `${url.pathname}${url.search}`;
        }

        async function loadBoard(options = {}) {
            const force = Boolean(options.force);
            if ((state.busy && !force) || !config.loadUrl) {
                return;
            }

            setBusy(true);
            setStatus(elements.status, "info", "Cargando envios...");

            try {
                const board = await fetchJson(buildLoadUrl());
                state.board = board || {};
                state.records = Array.isArray(board?.records) ? board.records.map(hydrateRecord) : [];
                if (elements.month && board?.selectedMonthValue) {
                    elements.month.value = board.selectedMonthValue;
                }

                renderSummary();
                renderCalendar();
                renderRows();
                setStatus(elements.status, state.records.length ? "success" : "info", board?.message || "");
            } catch (error) {
                setStatus(elements.status, "error", buildErrorMessage(error));
            } finally {
                setBusy(false);
            }
        }

        function renderSummary() {
            setText(elements.summaries.open, numberFormatter.format(Number(state.board?.openCount || 0)));
            setText(elements.summaries.scheduled, numberFormatter.format(Number(state.board?.scheduledCount || 0)));
            setText(elements.summaries.pickup, numberFormatter.format(Number(state.board?.pickupApprovedCount || 0)));
            setText(elements.summaries.delivered, numberFormatter.format(Number(state.board?.deliveredCount || 0)));
            setText(elements.summaries.freight, currencyFormatter.format(Number(state.board?.totalFreightValue || 0)));
            setText(elements.monthLabel, state.board?.selectedMonthLabel || "Mes seleccionado");
            setText(elements.count, `${numberFormatter.format(Number(state.board?.totalRecords || 0))} solicitudes`);
        }

        function renderCalendar() {
            if (!elements.calendar) {
                return;
            }

            const days = Array.isArray(state.board?.calendarDays) ? state.board.calendarDays : [];
            if (!days.length) {
                elements.calendar.innerHTML = '<div class="envios-placeholder">No hay calendario para este mes.</div>';
                return;
            }

            const firstDate = new Date(`${days[0].dateValue}T12:00:00`);
            const startOffset = (firstDate.getDay() + 6) % 7;
            const blanks = Array.from({ length: startOffset }, () => '<div class="envios-calendar__cell is-empty"></div>');
            const labels = ["Lun", "Mar", "Mie", "Jue", "Vie", "Sab", "Dom"]
                .map(day => `<div class="envios-calendar__weekday">${day}</div>`);
            const cells = days.map(day => {
                const count = Number(day.scheduledCount || 0);
                return `
                    <div class="envios-calendar__cell ${count > 0 ? "has-count" : ""}">
                        <span class="envios-calendar__day">${escapeHtml(day.dayNumber)}</span>
                        <strong>${escapeHtml(count)}</strong>
                        <small>${count === 1 ? "envio" : "envios"}</small>
                    </div>
                `;
            });

            elements.calendar.innerHTML = `${labels.join("")}${blanks.join("")}${cells.join("")}`;
        }

        function renderRows() {
            if (!elements.rows) {
                return;
            }

            if (!state.records.length) {
                elements.rows.innerHTML = "";
                if (elements.empty) {
                    elements.empty.hidden = false;
                }
                return;
            }

            if (elements.empty) {
                elements.empty.hidden = true;
            }

            elements.rows.innerHTML = config.mode === "transportador"
                ? state.records.map(renderTransporterRow).join("")
                : state.records.map(renderUserRow).join("");
        }

        function renderUserRow(row) {
            return `
                <tr data-env-record-id="${escapeHtml(row.recordId)}">
                    <td data-label="Estado">${renderStatus(row)}</td>
                    <td data-label="Fecha">
                        <div class="envios-table__main">${escapeHtml(row.scheduledAtDisplay || "Sin agenda")}</div>
                        <div class="envios-table__muted">Creado: ${escapeHtml(row.requestDateDisplay || "-")}</div>
                    </td>
                    <td data-label="Ruta">
                        <div class="envios-route">
                            <strong>${escapeHtml(row.origin || "-")}</strong>
                            <span>${escapeHtml(row.destination || "-")}</span>
                        </div>
                        <div class="envios-table__muted">${escapeHtml(truncateText(row.whatIsSent, 90))}</div>
                    </td>
                    <td data-label="Cliente">
                        <div class="envios-table__main">${escapeHtml(row.clientName || "-")}</div>
                        <div class="envios-table__muted">${escapeHtml(row.createdByName || "")}</div>
                    </td>
                    <td data-label="Recibe">
                        <div class="envios-table__main">${escapeHtml(row.recipientName || "-")}</div>
                        <div class="envios-table__muted">${escapeHtml(row.recipientPhone || "")}</div>
                    </td>
                    <td data-label="Flete">
                        <div class="envios-table__main">${currencyFormatter.format(Number(row.freightValue || 0))}</div>
                        <div class="envios-table__muted">${escapeHtml(row.transporterName || "")}</div>
                    </td>
                    <td data-label="Accion">${renderUserAction(row)}</td>
                </tr>
            `;
        }

        function renderTransporterRow(row) {
            return `
                <tr data-env-record-id="${escapeHtml(row.recordId)}">
                    <td data-label="Estado">${renderStatus(row)}</td>
                    <td data-label="Solicitud">
                        <div class="envios-table__main">${escapeHtml(row.clientName || "-")}</div>
                        <div class="envios-table__muted">${escapeHtml(truncateText(row.whatIsSent, 120))}</div>
                    </td>
                    <td data-label="Ruta">
                        <div class="envios-route">
                            <strong>${escapeHtml(row.origin || "-")}</strong>
                            <span>${escapeHtml(row.destination || "-")}</span>
                        </div>
                    </td>
                    <td data-label="Recibe">
                        <div class="envios-table__main">${escapeHtml(row.recipientName || "-")}</div>
                        <div class="envios-table__muted">${escapeHtml(row.recipientPhone || "")}</div>
                    </td>
                    <td data-label="Fecha y flete">${renderScheduleInputs(row)}</td>
                    <td data-label="Accion">${renderTransporterAction(row)}</td>
                </tr>
            `;
        }

        function renderStatus(row) {
            return `<span class="envios-status-pill ${resolveStatusClass(row.statusValue)}">${escapeHtml(row.statusLabel || "Sin estado")}</span>`;
        }

        function renderUserAction(row) {
            if (row.statusValue === STATUS_SCHEDULED) {
                return `<button type="button" class="btn btn-sm btn-primary" data-env-action="approve-pickup">Aprobar recogida</button>`;
            }

            if (row.statusValue === STATUS_DELIVERED) {
                return `
                    <div class="envios-action-stack">
                        <input type="file" class="form-control form-control-sm" accept=".pdf,.jpg,.jpeg,.png,.doc,.docx" data-env-final-file />
                        <button type="button" class="btn btn-sm btn-primary" data-env-action="approve-delivery">Recibido a satisfaccion</button>
                    </div>
                `;
            }

            if (row.hasDeliveryAct && config.downloadActUrl) {
                return `<a class="envios-link" href="${escapeHtml(buildDownloadActUrl(row.recordId))}" target="_blank" rel="noopener">Acta</a>`;
            }

            return `<span class="envios-table__muted">${escapeHtml(resolveWaitingLabel(row))}</span>`;
        }

        function renderScheduleInputs(row) {
            if (row.statusValue !== STATUS_OPEN && row.statusValue !== STATUS_SCHEDULED) {
                return `
                    <div class="envios-table__main">${escapeHtml(row.scheduledAtDisplay || "Sin agenda")}</div>
                    <div class="envios-table__muted">${currencyFormatter.format(Number(row.freightValue || 0))}</div>
                `;
            }

            return `
                <div class="envios-schedule-fields">
                    <input type="datetime-local" class="form-control form-control-sm" value="${escapeHtml(row.scheduledAtValue || "")}" data-env-schedule-at />
                    <input type="number" min="0" step="100" class="form-control form-control-sm" value="${escapeHtml(row.freightValue || "")}" data-env-freight />
                </div>
            `;
        }

        function renderTransporterAction(row) {
            if (row.statusValue === STATUS_OPEN || row.statusValue === STATUS_SCHEDULED) {
                return `<button type="button" class="btn btn-sm btn-primary" data-env-action="schedule">Agendar</button>`;
            }

            if (row.statusValue === STATUS_PICKUP_APPROVED) {
                return `<button type="button" class="btn btn-sm btn-primary" data-env-action="confirm-delivery">Confirmar entrega</button>`;
            }

            return `<span class="envios-table__muted">${escapeHtml(resolveWaitingLabel(row))}</span>`;
        }

        async function createShipment() {
            if (state.busy || !config.createUrl) {
                return;
            }

            let payload;
            try {
                payload = buildCreatePayload();
            } catch (error) {
                setStatus(elements.modalStatus, "error", buildErrorMessage(error));
                return;
            }

            setBusy(true);
            setStatus(elements.modalStatus, "info", "Creando solicitud...");

            try {
                const result = await fetchJson(config.createUrl, {
                    method: "POST",
                    body: JSON.stringify(payload)
                });
                closeModal();
                await loadBoard({ force: true });
                setStatus(elements.status, "success", result?.message || "Solicitud creada correctamente.");
            } catch (error) {
                setStatus(elements.modalStatus, "error", buildErrorMessage(error));
            } finally {
                setBusy(false);
            }
        }

        function buildCreatePayload() {
            const payload = {
                origin: readValue(elements.fields.origin),
                destination: readValue(elements.fields.destination),
                clientId: elements.clientId?.value || "",
                clientName: readValue(elements.fields.clientName),
                whatIsSent: readValue(elements.fields.whatIsSent),
                observations: readValue(elements.fields.observations),
                recipientName: readValue(elements.fields.recipientName),
                recipientPhone: readValue(elements.fields.recipientPhone)
            };

            if (!payload.origin) {
                throw new Error("El origen es obligatorio.");
            }
            if (!payload.destination) {
                throw new Error("El destino es obligatorio.");
            }
            if (!payload.clientId && !payload.clientName) {
                throw new Error("Debes seleccionar un cliente.");
            }
            if (!payload.whatIsSent) {
                throw new Error("Debes indicar que se envia.");
            }
            if (!payload.recipientName) {
                throw new Error("Debes indicar quien recibe.");
            }
            if (!payload.recipientPhone) {
                throw new Error("Debes indicar el telefono de quien recibe.");
            }

            return payload;
        }

        async function postRecordAction(url, recordId, loadingMessage) {
            if (state.busy || !url) {
                return;
            }

            setBusy(true);
            setStatus(elements.status, "info", loadingMessage || "Guardando...");

            try {
                const result = await fetchJson(url, {
                    method: "POST",
                    body: JSON.stringify({ recordId })
                });
                await loadBoard({ force: true });
                setStatus(elements.status, "success", result?.message || "Cambio guardado correctamente.");
            } catch (error) {
                setStatus(elements.status, "error", buildErrorMessage(error));
            } finally {
                setBusy(false);
            }
        }

        async function scheduleShipment(rowElement, recordId) {
            if (state.busy || !config.scheduleUrl) {
                return;
            }

            const scheduledAtValue = rowElement?.querySelector("[data-env-schedule-at]")?.value || "";
            const freightValue = parseDecimal(rowElement?.querySelector("[data-env-freight]")?.value || "0");
            if (!scheduledAtValue) {
                setStatus(elements.status, "error", "Debes indicar fecha y hora.");
                return;
            }
            if (freightValue <= 0) {
                setStatus(elements.status, "error", "El valor del flete debe ser mayor a cero.");
                return;
            }

            setBusy(true);
            setStatus(elements.status, "info", "Agendando envio...");

            try {
                const result = await fetchJson(config.scheduleUrl, {
                    method: "POST",
                    body: JSON.stringify({ recordId, scheduledAtValue, freightValue })
                });
                await loadBoard({ force: true });
                setStatus(elements.status, "success", result?.message || "Envio agendado correctamente.");
            } catch (error) {
                setStatus(elements.status, "error", buildErrorMessage(error));
            } finally {
                setBusy(false);
            }
        }

        async function approveDelivery(rowElement, recordId) {
            if (state.busy || !config.approveDeliveryUrl) {
                return;
            }

            const file = rowElement?.querySelector("[data-env-final-file]")?.files?.[0] || null;
            if (!file) {
                setStatus(elements.status, "error", "Debes adjuntar el acta de entrega.");
                return;
            }

            const formData = new FormData();
            formData.append("recordId", recordId);
            formData.append("file", file);

            setBusy(true);
            setStatus(elements.status, "info", "Cargando acta y cerrando envio...");

            try {
                const result = await fetchJson(config.approveDeliveryUrl, {
                    method: "POST",
                    body: formData
                });
                await loadBoard({ force: true });
                setStatus(elements.status, "success", result?.message || "Envio cerrado correctamente.");
            } catch (error) {
                setStatus(elements.status, "error", buildErrorMessage(error));
            } finally {
                setBusy(false);
            }
        }

        function openModal() {
            clearForm();
            clearStatus(elements.modalStatus);
            if (elements.modal) {
                elements.modal.hidden = false;
            }
            document.body.classList.add("envios-modal-open");
        }

        function closeModal(force = false) {
            if (state.busy && !force) {
                return;
            }

            if (elements.modal) {
                elements.modal.hidden = true;
            }
            document.body.classList.remove("envios-modal-open");
        }

        function clearForm() {
            Object.values(elements.fields).forEach(field => {
                if (field) {
                    field.value = "";
                }
            });
            if (elements.clientId) {
                elements.clientId.value = "";
            }
            state.clientSuggestions = [];
            renderClientSuggestions();
        }

        function renderClientSuggestions() {
            if (!elements.clientOptions) {
                return;
            }

            elements.clientOptions.innerHTML = state.clientSuggestions.map(item => `
                <option value="${escapeHtml(item.name || "")}" data-id="${escapeHtml(item.id || "")}"></option>
            `).join("");
        }

        function syncClientSelection() {
            const inputValue = normalizeText(elements.fields.clientName?.value || "");
            const selectedItem = state.clientSuggestions.find(item => normalizeText(item.name || "") === inputValue);
            if (elements.clientId) {
                elements.clientId.value = selectedItem?.id || "";
            }
        }

        function setBusy(isBusy) {
            state.busy = isBusy;

            [
                elements.month,
                elements.refresh,
                elements.newButton,
                elements.fields.origin,
                elements.fields.destination,
                elements.fields.clientName,
                elements.fields.whatIsSent,
                elements.fields.observations,
                elements.fields.recipientName,
                elements.fields.recipientPhone
            ].forEach(element => {
                if (element) {
                    element.disabled = isBusy;
                }
            });

            root.querySelectorAll("[data-env-action], [data-env-schedule-at], [data-env-freight], [data-env-final-file]").forEach(element => {
                element.disabled = isBusy;
            });
        }
    }

    function hydrateRecord(record) {
        return {
            recordId: record?.recordId || "",
            name: record?.name || "",
            origin: record?.origin || "",
            destination: record?.destination || "",
            clientId: record?.clientId || "",
            clientName: record?.clientName || "",
            whatIsSent: record?.whatIsSent || "",
            observations: record?.observations || "",
            recipientName: record?.recipientName || "",
            recipientPhone: record?.recipientPhone || "",
            statusValue: Number(record?.statusValue || 0),
            statusLabel: record?.statusLabel || "",
            requestDateValue: record?.requestDateValue || "",
            requestDateDisplay: record?.requestDateDisplay || "",
            scheduledAtValue: record?.scheduledAtValue || "",
            scheduledAtDisplay: record?.scheduledAtDisplay || "",
            transporterId: record?.transporterId || "",
            transporterName: record?.transporterName || "",
            freightValue: Number(record?.freightValue || 0),
            pickupApproved: Boolean(record?.pickupApproved),
            pickupApprovedAtDisplay: record?.pickupApprovedAtDisplay || "",
            pickupApprovedByName: record?.pickupApprovedByName || "",
            deliveryConfirmedAtDisplay: record?.deliveryConfirmedAtDisplay || "",
            deliveredByName: record?.deliveredByName || "",
            receivedSatisfied: Boolean(record?.receivedSatisfied),
            receivedSatisfiedAtDisplay: record?.receivedSatisfiedAtDisplay || "",
            receivedSatisfiedByName: record?.receivedSatisfiedByName || "",
            hasDeliveryAct: Boolean(record?.hasDeliveryAct),
            deliveryActFileName: record?.deliveryActFileName || "",
            createdById: record?.createdById || "",
            createdByName: record?.createdByName || "",
            modifiedOnDisplay: record?.modifiedOnDisplay || ""
        };
    }

    async function fetchJson(url, options = {}) {
        const isFormData = options.body instanceof FormData;
        const headers = {
            Accept: "application/json",
            ...(options.headers || {})
        };

        if (!isFormData && options.body && !headers["Content-Type"]) {
            headers["Content-Type"] = "application/json";
        }

        const response = await fetch(url, {
            method: options.method || "GET",
            headers: isFormData ? { Accept: headers.Accept } : headers,
            body: options.body
        });

        const contentType = response.headers.get("content-type") || "";
        if (!response.ok) {
            const rawBody = await response.text();
            let message = rawBody;

            if (contentType.includes("application/json")) {
                try {
                    const payload = rawBody ? JSON.parse(rawBody) : null;
                    message = typeof payload === "string"
                        ? payload
                        : payload?.message || payload?.detail || payload?.title || rawBody;
                } catch {
                    message = rawBody;
                }
            }

            throw new Error(message || "No fue posible completar la solicitud.");
        }

        if (!contentType.includes("application/json")) {
            const message = await response.text();
            throw new Error(message || "La respuesta del servidor no fue valida.");
        }

        return response.json();
    }

    function setStatus(target, type, message) {
        if (!target) {
            return;
        }

        if (!message) {
            clearStatus(target);
            return;
        }

        target.className = `envios-status is-visible is-${type}`;
        target.textContent = message;
    }

    function clearStatus(target) {
        if (!target) {
            return;
        }

        target.className = "envios-status";
        target.textContent = "";
    }

    function setText(target, value) {
        if (target) {
            target.textContent = value;
        }
    }

    function readValue(element) {
        return (element?.value || "").trim();
    }

    function parseDecimal(value) {
        const parsed = Number.parseFloat(String(value || "").replace(",", "."));
        return Number.isFinite(parsed) ? parsed : 0;
    }

    function buildErrorMessage(error) {
        return error instanceof Error
            ? error.message
            : "Ocurrio un error inesperado.";
    }

    function resolveWaitingLabel(row) {
        if (row.statusValue === STATUS_OPEN) {
            return "Pendiente de transportador";
        }
        if (row.statusValue === STATUS_SCHEDULED) {
            return "Pendiente de recogida";
        }
        if (row.statusValue === STATUS_PICKUP_APPROVED) {
            return "Pendiente de entrega";
        }
        if (row.statusValue === STATUS_DELIVERED) {
            return "Pendiente de acta";
        }
        if (row.statusValue === STATUS_CLOSED) {
            return "Cerrado";
        }
        return "";
    }

    function resolveStatusClass(statusValue) {
        if (statusValue === STATUS_OPEN) {
            return "is-open";
        }
        if (statusValue === STATUS_SCHEDULED) {
            return "is-scheduled";
        }
        if (statusValue === STATUS_PICKUP_APPROVED) {
            return "is-pickup";
        }
        if (statusValue === STATUS_DELIVERED) {
            return "is-delivered";
        }
        if (statusValue === STATUS_CLOSED) {
            return "is-closed";
        }
        return "";
    }

    function normalizeText(value) {
        return (value ?? "")
            .toString()
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .toLowerCase()
            .trim();
    }

    function truncateText(value, maxLength) {
        const text = String(value ?? "").replace(/\s+/g, " ").trim();
        if (!text || text.length <= maxLength) {
            return text;
        }

        return `${text.slice(0, Math.max(0, maxLength - 1)).trimEnd()}...`;
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#39;");
    }
})();
