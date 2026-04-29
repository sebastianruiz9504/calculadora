(function () {
    const roots = Array.from(document.querySelectorAll("#soporteCloudApp, #dashboardSupportCloudApp"));
    if (!roots.length) {
        return;
    }

    const currencyFormatter = new Intl.NumberFormat("es-CO", {
        style: "currency",
        currency: "COP",
        minimumFractionDigits: 0,
        maximumFractionDigits: 0
    });

    const numberFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });

    roots.forEach(initializeSupportCloudWorkspace);

    function initializeSupportCloudWorkspace(root) {
        const config = {
            showCharts: String(root.dataset.showCharts || "").toLowerCase() === "true",
            loadUrl: root.dataset.loadUrl || "",
            clientSearchUrl: root.dataset.clientSearchUrl || "",
            saveUrl: root.dataset.saveUrl || "",
            uploadUrl: root.dataset.uploadUrl || "",
            downloadUrl: root.dataset.downloadUrl || "",
            trainingsUrl: root.dataset.trainingsUrl || "",
            currentUserName: root.dataset.currentUserName || "Usuario actual"
        };

        const elements = {
            subtabButtons: Array.from(root.querySelectorAll("[data-sc-subtab]")),
            subpanels: Array.from(root.querySelectorAll("[data-sc-subpanel]")),
            status: root.querySelector("[data-sc-status]"),
            startDate: root.querySelector("[data-sc-start-date]"),
            endDate: root.querySelector("[data-sc-end-date]"),
            rangeLabel: root.querySelector("[data-sc-range-label]"),
            refresh: root.querySelector("[data-sc-refresh]"),
            newTicket: root.querySelector("[data-sc-new-ticket]"),
            ticketActions: Array.from(root.querySelectorAll("[data-sc-ticket-action]")),
            totalTickets: root.querySelector("[data-sc-total-tickets]"),
            totalHours: root.querySelector("[data-sc-total-hours]"),
            totalCreators: root.querySelector("[data-sc-total-creators]"),
            totalClients: root.querySelector("[data-sc-total-clients]"),
            creators: root.querySelector("[data-sc-creators]"),
            recordsCount: root.querySelector("[data-sc-records-count]"),
            rows: root.querySelector("[data-sc-rows]"),
            empty: root.querySelector("[data-sc-empty]"),
            chartsSection: root.querySelector("[data-sc-charts-section]"),
            typeChart: root.querySelector('[data-sc-chart="type"]'),
            methodChart: root.querySelector('[data-sc-chart="method"]'),
            categoryChart: root.querySelector('[data-sc-chart="category"]'),
            trainingsStatus: root.querySelector("[data-sct-status]"),
            trainingsTotal: root.querySelector("[data-sct-total-trainings]"),
            trainingsTotalHours: root.querySelector("[data-sct-total-hours]"),
            trainingsTotalClients: root.querySelector("[data-sct-total-clients]"),
            trainingsTotalAttendees: root.querySelector("[data-sct-total-attendees]"),
            trainingsOwners: root.querySelector("[data-sct-owners]"),
            trainingsRecordsCount: root.querySelector("[data-sct-records-count]"),
            trainingsRows: root.querySelector("[data-sct-rows]"),
            trainingsEmpty: root.querySelector("[data-sct-empty]"),
            trainingsTopicChart: root.querySelector('[data-sct-chart="topic"]'),
            trainingsClientChart: root.querySelector('[data-sct-chart="clients"]'),
            trainingsTimeChart: root.querySelector('[data-sct-chart="time"]'),
            modal: root.querySelector("[data-sc-modal]"),
            modalStatus: root.querySelector("[data-sc-modal-status]"),
            modalTitle: root.querySelector("[data-sc-modal-title]"),
            modalSubtitle: root.querySelector("[data-sc-modal-subtitle]"),
            modalMeta: root.querySelector("[data-sc-modal-meta]"),
            form: root.querySelector("[data-sc-form]"),
            closeModalButtons: Array.from(root.querySelectorAll("[data-sc-close-modal]")),
            recordId: root.querySelector("[data-sc-record-id]"),
            clientId: root.querySelector("[data-sc-client-id]"),
            clientOptions: root.querySelector("[data-sc-client-options]"),
            attachmentInput: root.querySelector("[data-sc-attachment-input]"),
            attachmentName: root.querySelector("[data-sc-attachment-name]"),
            attachmentHint: root.querySelector("[data-sc-attachment-hint]"),
            downloadLink: root.querySelector("[data-sc-download-link]"),
            fields: {
                title: root.querySelector('[data-sc-field="title"]'),
                description: root.querySelector('[data-sc-field="description"]'),
                creationDate: root.querySelector('[data-sc-field="creationDate"]'),
                state: root.querySelector('[data-sc-field="state"]'),
                type: root.querySelector('[data-sc-field="type"]'),
                clientName: root.querySelector('[data-sc-field="clientName"]'),
                category: root.querySelector('[data-sc-field="category"]'),
                creatorName: root.querySelector('[data-sc-field="creatorName"]'),
                hoursTaken: root.querySelector('[data-sc-field="hoursTaken"]'),
                method: root.querySelector('[data-sc-field="method"]'),
                solution: root.querySelector('[data-sc-field="solution"]')
            }
        };

        const state = {
            activeSubtab: "tickets",
            board: null,
            records: [],
            trainingsBoard: null,
            trainingsRecords: [],
            clientSuggestions: [],
            busy: false,
            trainingsBusy: false,
            saving: false,
            loaded: false,
            trainingsLoaded: false,
            lookupTimer: 0,
            lookupSequence: 0,
            draft: null,
            pendingFile: null
        };

        elements.startDate && (elements.startDate.value = root.dataset.initialStartDate || "");
        elements.endDate && (elements.endDate.value = root.dataset.initialEndDate || "");
        if (elements.rangeLabel) {
            elements.rangeLabel.textContent = buildRangeLabel(
                elements.startDate?.value || "",
                elements.endDate?.value || "");
        }

        if (elements.chartsSection) {
            elements.chartsSection.hidden = !config.showCharts;
        }

        syncSupportSubtabVisibility();

        elements.subtabButtons.forEach(button => {
            button.addEventListener("click", () => {
                const subtabKey = button.dataset.scSubtab || "tickets";
                if (subtabKey !== state.activeSubtab) {
                    setActiveSupportSubtab(subtabKey);
                }
            });
        });

        elements.refresh?.addEventListener("click", () => {
            loadActiveSupportSubtab({ force: true });
        });

        elements.newTicket?.addEventListener("click", () => {
            openModal();
        });

        [elements.startDate, elements.endDate].forEach(input => {
            input?.addEventListener("change", () => {
                if (elements.rangeLabel) {
                    elements.rangeLabel.textContent = buildRangeLabel(
                        elements.startDate?.value || "",
                        elements.endDate?.value || "");
                }
                state.loaded = false;
                state.trainingsLoaded = false;
                loadActiveSupportSubtab({ force: true });
            });
        });

        elements.rows?.addEventListener("click", event => {
            const target = event.target;
            if (!(target instanceof HTMLElement)) {
                return;
            }

            if (target.closest("a")) {
                return;
            }

            const row = resolveRowFromEvent(target);
            if (!row) {
                return;
            }

            openModal(row.recordId);
        });

        elements.rows?.addEventListener("keydown", event => {
            const target = event.target;
            if (!(target instanceof HTMLElement)) {
                return;
            }

            if (event.key !== "Enter" && event.key !== " ") {
                return;
            }

            const row = resolveRowFromEvent(target);
            if (!row) {
                return;
            }

            event.preventDefault();
            openModal(row.recordId);
        });

        elements.closeModalButtons.forEach(button => {
            button.addEventListener("click", () => {
                closeModal();
            });
        });

        elements.modal?.addEventListener("click", event => {
            const target = event.target;
            if (target instanceof HTMLElement && target.hasAttribute("data-sc-close-modal")) {
                closeModal();
            }
        });

        document.addEventListener("keydown", event => {
            if (event.key === "Escape" && elements.modal && !elements.modal.hidden) {
                closeModal();
            }
        });

        elements.attachmentInput?.addEventListener("change", () => {
            state.pendingFile = elements.attachmentInput?.files?.[0] || null;
            renderAttachmentState();
        });

        elements.fields.clientName?.addEventListener("input", () => {
            elements.clientId && (elements.clientId.value = "");
            const query = (elements.fields.clientName.value || "").trim();
            window.clearTimeout(state.lookupTimer);

            if (query.length < 2) {
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
            await saveTicket();
        });

        const dashboardTabButton = document.querySelector('[data-dashboard-tab="support-cloud"]');
        if (root.id === "dashboardSupportCloudApp") {
            dashboardTabButton?.addEventListener("click", () => {
                loadActiveSupportSubtab();
            });

            if (dashboardTabButton?.classList.contains("is-active")) {
                loadActiveSupportSubtab();
            }
        } else {
            loadBoard();
        }

        function resolveRowFromEvent(target) {
            const rowElement = target.closest("tr[data-sc-record-id]");
            if (!(rowElement instanceof HTMLTableRowElement)) {
                return null;
            }

            const recordId = rowElement.dataset.scRecordId || "";
            return state.records.find(item => item.recordId === recordId) || null;
        }

        function buildClientSearchUrl(query) {
            const url = new URL(config.clientSearchUrl, window.location.origin);
            url.searchParams.set("q", query);
            return `${url.pathname}${url.search}`;
        }

        function buildLoadUrl() {
            const url = new URL(config.loadUrl, window.location.origin);
            if (elements.startDate?.value) {
                url.searchParams.set("startDate", elements.startDate.value);
            }
            if (elements.endDate?.value) {
                url.searchParams.set("endDate", elements.endDate.value);
            }
            return `${url.pathname}${url.search}`;
        }

        function buildTrainingsUrl() {
            const url = new URL(config.trainingsUrl, window.location.origin);
            if (elements.startDate?.value) {
                url.searchParams.set("startDate", elements.startDate.value);
            }
            if (elements.endDate?.value) {
                url.searchParams.set("endDate", elements.endDate.value);
            }
            return `${url.pathname}${url.search}`;
        }

        function buildDownloadUrl(recordId) {
            const url = new URL(config.downloadUrl, window.location.origin);
            url.searchParams.set("recordId", recordId);
            return `${url.pathname}${url.search}`;
        }

        function loadActiveSupportSubtab(options = {}) {
            if (state.activeSubtab === "trainings") {
                loadTrainings(options);
                return;
            }

            if (!state.loaded || options.force) {
                loadBoard(options);
            }
        }

        function setActiveSupportSubtab(subtabKey) {
            state.activeSubtab = subtabKey === "trainings" && config.trainingsUrl
                ? "trainings"
                : "tickets";
            syncSupportSubtabVisibility();
            loadActiveSupportSubtab();
        }

        function syncSupportSubtabVisibility() {
            if (!elements.subtabButtons.length && !elements.subpanels.length) {
                return;
            }

            elements.subtabButtons.forEach(button => {
                const isActive = button.dataset.scSubtab === state.activeSubtab;
                button.classList.toggle("is-active", isActive);
                button.setAttribute("aria-selected", isActive ? "true" : "false");
            });

            elements.subpanels.forEach(panel => {
                const isActive = panel.dataset.scSubpanel === state.activeSubtab;
                panel.classList.toggle("is-active", isActive);
                panel.hidden = !isActive;
            });

            elements.ticketActions.forEach(action => {
                action.hidden = state.activeSubtab !== "tickets";
            });
        }

        async function loadBoard(options = {}) {
            const force = Boolean(options.force);
            if ((state.busy && !force) || !config.loadUrl) {
                return;
            }

            setBusy(true);
            setStatus(elements.status, "info", "Cargando tickets de soporte cloud...");

            try {
                const board = await fetchJson(buildLoadUrl());
                state.board = board;
                state.records = Array.isArray(board?.records) ? board.records.map(hydrateRecord) : [];
                state.loaded = true;

                syncRangeInputs(board);
                renderSummary();
                renderCreators();
                renderCharts();
                renderTable();
                setStatus(elements.status, state.records.length ? "success" : "info", board?.message || "");
            } catch (error) {
                setStatus(elements.status, "error", buildErrorMessage(error));
            } finally {
                setBusy(false);
            }
        }

        async function loadTrainings(options = {}) {
            const force = Boolean(options.force);
            if ((state.trainingsBusy && !force) || !config.trainingsUrl) {
                return;
            }

            if (state.trainingsLoaded && !force) {
                renderTrainingsDashboard();
                return;
            }

            setTrainingsBusy(true);
            setStatus(elements.trainingsStatus, "info", "Cargando capacitaciones de soporte cloud...");

            try {
                const board = await fetchJson(buildTrainingsUrl());
                state.trainingsBoard = board;
                state.trainingsRecords = Array.isArray(board?.records) ? board.records.map(hydrateTrainingRecord) : [];
                state.trainingsLoaded = true;

                syncRangeInputs(board);
                renderTrainingsDashboard();
                setStatus(elements.trainingsStatus, state.trainingsRecords.length ? "success" : "info", board?.message || "");
            } catch (error) {
                setStatus(elements.trainingsStatus, "error", buildErrorMessage(error));
            } finally {
                setTrainingsBusy(false);
            }
        }

        function syncRangeInputs(board) {
            if (elements.startDate && board?.startDateValue) {
                elements.startDate.value = board.startDateValue;
            }
            if (elements.endDate && board?.endDateValue) {
                elements.endDate.value = board.endDateValue;
            }
            if (elements.rangeLabel) {
                elements.rangeLabel.textContent = board?.dateRangeLabel
                    || buildRangeLabel(elements.startDate?.value || "", elements.endDate?.value || "");
            }
        }

        function renderSummary() {
            if (elements.totalTickets) {
                elements.totalTickets.textContent = numberFormatter.format(Number(state.board?.totalTickets || 0));
            }
            if (elements.totalHours) {
                elements.totalHours.textContent = numberFormatter.format(Number(state.board?.totalHours || 0));
            }
            if (elements.totalCreators) {
                elements.totalCreators.textContent = numberFormatter.format(Number(state.board?.totalCreators || 0));
            }
            if (elements.totalClients) {
                elements.totalClients.textContent = numberFormatter.format(Number(state.board?.totalClients || 0));
            }
        }

        function renderCreators() {
            if (!elements.creators) {
                return;
            }

            const items = Array.isArray(state.board?.creatorSummaries) ? state.board.creatorSummaries : [];
            if (!items.length) {
                elements.creators.innerHTML = '<div class="support-cloud-placeholder">No hay creadores para el rango seleccionado.</div>';
                return;
            }

            elements.creators.innerHTML = items.map(item => `
                <article class="support-cloud-creator-card">
                    <span class="support-cloud-creator-card__label">${escapeHtml(item.creatorName || "Sin creador")}</span>
                    <strong class="support-cloud-creator-card__value">${escapeHtml(numberFormatter.format(Number(item.totalTickets || 0)))}</strong>
                    <span class="support-cloud-creator-card__meta">${escapeHtml(numberFormatter.format(Number(item.totalHours || 0)))} horas</span>
                </article>
            `).join("");
        }

        function renderCharts() {
            if (!config.showCharts) {
                return;
            }

            renderBreakdown(elements.typeChart, state.board?.typeBreakdowns, "No hay datos por tipo.");
            renderBreakdown(elements.methodChart, state.board?.methodBreakdowns, "No hay datos por metodo.");
            renderBreakdown(elements.categoryChart, state.board?.categoryBreakdowns, "No hay datos por categoria.");
        }

        function renderBreakdown(container, items, emptyMessage) {
            if (!container) {
                return;
            }

            const rows = Array.isArray(items) ? items : [];
            if (!rows.length) {
                container.innerHTML = `<div class="support-cloud-placeholder">${escapeHtml(emptyMessage)}</div>`;
                return;
            }

            const maxTickets = Math.max(1, ...rows.map(item => Number(item.totalTickets || 0)));
            container.innerHTML = rows.map(item => {
                const width = Math.max(6, Math.round((Number(item.totalTickets || 0) / maxTickets) * 100));
                return `
                    <div class="support-cloud-breakdown__row">
                        <div class="support-cloud-breakdown__head">
                            <span class="support-cloud-breakdown__label">${escapeHtml(item.label || "Sin dato")}</span>
                            <span class="support-cloud-breakdown__value">${escapeHtml(numberFormatter.format(Number(item.totalTickets || 0)))} ticket(s) · ${escapeHtml(numberFormatter.format(Number(item.totalHours || 0)))} h</span>
                        </div>
                        <div class="support-cloud-breakdown__track">
                            <span class="support-cloud-breakdown__fill" style="width:${width}%"></span>
                        </div>
                    </div>
                `;
            }).join("");
        }

        function renderTrainingsDashboard() {
            renderTrainingSummary();
            renderTrainingOwnerBars();
            renderTrainingCharts();
            renderTrainingTable();
        }

        function renderTrainingSummary() {
            const board = state.trainingsBoard || {};
            if (elements.trainingsTotal) {
                elements.trainingsTotal.textContent = numberFormatter.format(Number(board.totalTrainings || 0));
            }
            if (elements.trainingsTotalHours) {
                elements.trainingsTotalHours.textContent = numberFormatter.format(Number(board.totalHoursDelivered || 0));
            }
            if (elements.trainingsTotalClients) {
                elements.trainingsTotalClients.textContent = numberFormatter.format(Number(board.totalClients || 0));
            }
            if (elements.trainingsTotalAttendees) {
                elements.trainingsTotalAttendees.textContent = numberFormatter.format(Number(board.totalAttendees || 0));
            }
        }

        function renderTrainingOwnerBars() {
            if (!elements.trainingsOwners) {
                return;
            }

            const items = Array.isArray(state.trainingsBoard?.ownerSummaries) ? state.trainingsBoard.ownerSummaries : [];
            if (!items.length) {
                elements.trainingsOwners.innerHTML = '<div class="support-cloud-placeholder support-cloud-placeholder--compact">Sin owners en el rango.</div>';
                return;
            }

            const maxTrainings = Math.max(1, ...items.map(item => Number(item.totalTrainings || 0)));
            elements.trainingsOwners.innerHTML = items.map(item => {
                const width = Math.max(6, Math.round((Number(item.totalTrainings || 0) / maxTrainings) * 100));
                return `
                    <div class="support-cloud-owner-bar">
                        <div class="support-cloud-owner-bar__head">
                            <span>${escapeHtml(item.ownerName || "Sin owner")}</span>
                            <strong>${escapeHtml(numberFormatter.format(Number(item.totalTrainings || 0)))}</strong>
                        </div>
                        <div class="support-cloud-breakdown__track">
                            <span class="support-cloud-breakdown__fill" style="width:${width}%"></span>
                        </div>
                    </div>
                `;
            }).join("");
        }

        function renderTrainingCharts() {
            renderTrainingBreakdown(elements.trainingsTopicChart, state.trainingsBoard?.topicBreakdowns, "No hay capacitaciones por tema.");
            renderTrainingBreakdown(elements.trainingsClientChart, state.trainingsBoard?.clientBreakdowns, "No hay clientes con capacitaciones.");
            renderTrainingTimeChart();
        }

        function renderTrainingBreakdown(container, items, emptyMessage) {
            if (!container) {
                return;
            }

            const rows = Array.isArray(items) ? items : [];
            if (!rows.length) {
                container.innerHTML = `<div class="support-cloud-placeholder">${escapeHtml(emptyMessage)}</div>`;
                return;
            }

            const maxTrainings = Math.max(1, ...rows.map(item => Number(item.totalTrainings || 0)));
            container.innerHTML = rows.map(item => {
                const width = Math.max(6, Math.round((Number(item.totalTrainings || 0) / maxTrainings) * 100));
                return `
                    <div class="support-cloud-breakdown__row">
                        <div class="support-cloud-breakdown__head">
                            <span class="support-cloud-breakdown__label">${escapeHtml(item.label || "Sin dato")}</span>
                            <span class="support-cloud-breakdown__value">${escapeHtml(numberFormatter.format(Number(item.totalTrainings || 0)))} cap. · ${escapeHtml(numberFormatter.format(Number(item.totalHours || 0)))} h · ${escapeHtml(numberFormatter.format(Number(item.totalAttendees || 0)))} asistentes</span>
                        </div>
                        <div class="support-cloud-breakdown__track">
                            <span class="support-cloud-breakdown__fill" style="width:${width}%"></span>
                        </div>
                    </div>
                `;
            }).join("");
        }

        function renderTrainingTimeChart() {
            if (!elements.trainingsTimeChart) {
                return;
            }

            const points = Array.isArray(state.trainingsBoard?.timeSeries) ? state.trainingsBoard.timeSeries : [];
            const maxTrainings = Math.max(0, ...points.map(point => Number(point.totalTrainings || 0)));
            if (!points.length || maxTrainings <= 0) {
                elements.trainingsTimeChart.innerHTML = '<div class="support-cloud-placeholder">No hay datos suficientes para la serie de tiempo.</div>';
                return;
            }

            const width = 760;
            const height = 260;
            const padding = { top: 24, right: 24, bottom: 46, left: 42 };
            const plotWidth = width - padding.left - padding.right;
            const plotHeight = height - padding.top - padding.bottom;
            const baselineY = padding.top + plotHeight;
            const yForValue = value => baselineY - ((Number(value || 0) / maxTrainings) * plotHeight);
            const xForIndex = index => points.length === 1
                ? padding.left + (plotWidth / 2)
                : padding.left + ((index / (points.length - 1)) * plotWidth);
            const coords = points.map((point, index) => ({
                x: xForIndex(index),
                y: yForValue(point.totalTrainings),
                point
            }));
            const linePath = coords
                .map((coord, index) => `${index === 0 ? "M" : "L"} ${coord.x.toFixed(1)} ${coord.y.toFixed(1)}`)
                .join(" ");
            const labelEvery = Math.max(1, Math.ceil(points.length / 8));
            const barSlot = plotWidth / Math.max(points.length, 1);
            const barWidth = Math.max(5, Math.min(28, barSlot * 0.46));
            const bars = coords.map(coord => {
                const barHeight = Math.max(2, baselineY - coord.y);
                return `<rect class="support-cloud-time-chart__bar" x="${(coord.x - (barWidth / 2)).toFixed(1)}" y="${coord.y.toFixed(1)}" width="${barWidth.toFixed(1)}" height="${barHeight.toFixed(1)}"><title>${escapeHtml(coord.point.label || "")}: ${escapeHtml(numberFormatter.format(Number(coord.point.totalTrainings || 0)))} capacitaciones</title></rect>`;
            }).join("");
            const dots = coords.map(coord => `
                <circle class="support-cloud-time-chart__dot" cx="${coord.x.toFixed(1)}" cy="${coord.y.toFixed(1)}" r="4">
                    <title>${escapeHtml(coord.point.label || "")}: ${escapeHtml(numberFormatter.format(Number(coord.point.totalTrainings || 0)))} capacitaciones</title>
                </circle>
            `).join("");
            const xLabels = coords
                .filter((coord, index) => index === 0 || index === coords.length - 1 || index % labelEvery === 0)
                .map(coord => `<text class="support-cloud-time-chart__axis" x="${coord.x.toFixed(1)}" y="${height - 12}" text-anchor="middle">${escapeHtml(coord.point.label || "")}</text>`)
                .join("");
            const yLabels = [0, Math.ceil(maxTrainings / 2), maxTrainings]
                .filter((value, index, list) => list.indexOf(value) === index)
                .map(value => {
                    const y = yForValue(value);
                    return `
                        <line class="support-cloud-time-chart__grid" x1="${padding.left}" x2="${width - padding.right}" y1="${y.toFixed(1)}" y2="${y.toFixed(1)}"></line>
                        <text class="support-cloud-time-chart__axis" x="${padding.left - 10}" y="${(y + 4).toFixed(1)}" text-anchor="end">${escapeHtml(numberFormatter.format(value))}</text>
                    `;
                })
                .join("");

            elements.trainingsTimeChart.innerHTML = `
                <svg class="support-cloud-time-chart__svg" viewBox="0 0 ${width} ${height}" role="img" aria-label="Capacitaciones en el tiempo">
                    ${yLabels}
                    <line class="support-cloud-time-chart__baseline" x1="${padding.left}" x2="${width - padding.right}" y1="${baselineY}" y2="${baselineY}"></line>
                    ${bars}
                    <path class="support-cloud-time-chart__line" d="${linePath}"></path>
                    ${dots}
                    ${xLabels}
                </svg>
            `;
        }

        function renderTrainingTable() {
            if (!elements.trainingsRows) {
                return;
            }

            const rows = state.trainingsRecords;
            if (elements.trainingsRecordsCount) {
                elements.trainingsRecordsCount.textContent = `${numberFormatter.format(rows.length)} capacitacion(es)`;
            }

            if (elements.trainingsEmpty) {
                elements.trainingsEmpty.hidden = rows.length > 0;
            }

            if (!rows.length) {
                elements.trainingsRows.innerHTML = `
                    <tr>
                        <td colspan="7" class="support-cloud-table__empty">No encontramos capacitaciones para este rango.</td>
                    </tr>
                `;
                return;
            }

            elements.trainingsRows.innerHTML = rows.map(row => `
                <tr class="support-cloud-table__row support-cloud-table__row--static">
                    <td data-label="Fecha">${escapeHtml(row.dateDisplay || "-")}</td>
                    <td data-label="Duracion">
                        <div class="support-cloud-table__ticket">
                            <div class="support-cloud-table__ticket-title">${escapeHtml(row.durationDisplay || "-")}</div>
                            <div class="support-cloud-table__ticket-description">${escapeHtml(numberFormatter.format(Number(row.durationHours || 0)))} horas entregadas</div>
                        </div>
                    </td>
                    <td data-label="Cliente">${escapeHtml(row.clientName || "-")}</td>
                    <td data-label="Asistentes" class="text-end support-cloud-table__hours">${escapeHtml(numberFormatter.format(Number(row.attendees || 0)))}</td>
                    <td data-label="Tema">${renderPill(row.topicLabel || "Sin tema")}</td>
                    <td data-label="Propietario">${escapeHtml(row.ownerName || "-")}</td>
                    <td data-label="ID"><span class="support-cloud-table__muted">${escapeHtml(truncateText(row.recordId || "-", 12))}</span></td>
                </tr>
            `).join("");
        }

        function renderTable() {
            if (!elements.rows) {
                return;
            }

            const rows = state.records;
            if (elements.recordsCount) {
                elements.recordsCount.textContent = `${numberFormatter.format(rows.length)} ticket(s)`;
            }

            if (elements.empty) {
                elements.empty.hidden = rows.length > 0;
            }

            if (!rows.length) {
                elements.rows.innerHTML = `
                    <tr>
                        <td colspan="8" class="support-cloud-table__empty">No encontramos tickets para este rango.</td>
                    </tr>
                `;
                return;
            }

            elements.rows.innerHTML = rows.map(row => `
                <tr tabindex="0" class="support-cloud-table__row" data-sc-record-id="${escapeHtml(row.recordId || "")}">
                    <td data-label="Fecha">${escapeHtml(row.creationDateDisplay || "-")}</td>
                    <td data-label="Ticket">
                        <div class="support-cloud-table__ticket">
                            <div class="support-cloud-table__ticket-title">${escapeHtml(row.title || "-")}</div>
                            <div class="support-cloud-table__ticket-description">${escapeHtml(buildTicketExcerpt(row))}</div>
                            ${renderTicketMeta(row)}
                        </div>
                    </td>
                    <td data-label="Cliente">${escapeHtml(row.clientName || "-")}</td>
                    <td data-label="Estado">${renderPill(row.stateLabel || "Sin estado")}</td>
                    <td data-label="Tipo">${escapeHtml(row.typeLabel || "-")}</td>
                    <td data-label="Creador">${escapeHtml(row.creatorName || "-")}</td>
                    <td data-label="Horas" class="text-end support-cloud-table__hours">${escapeHtml(numberFormatter.format(Number(row.hoursTaken || 0)))}</td>
                    <td data-label="Adjunto">
                        ${row.hasAttachment
                            ? `<a class="support-cloud-table__link" href="${escapeHtml(buildDownloadUrl(row.recordId || ""))}" target="_blank" rel="noopener" title="${escapeHtml(row.attachmentFileName || "Adjunto")}">Descargar</a>`
                            : '<span class="support-cloud-table__muted">Sin adjunto</span>'}
                    </td>
                </tr>
            `).join("");
        }

        function openModal(recordId) {
            const source = recordId
                ? state.records.find(item => item.recordId === recordId)
                : null;

            state.draft = source ? { ...source } : createEmptyDraft();
            state.pendingFile = null;
            resetAttachmentInput();
            renderModal();

            if (elements.modal) {
                elements.modal.hidden = false;
            }

            document.body.classList.add("support-cloud-modal-open");
        }

        function closeModal() {
            if (state.saving) {
                return;
            }

            state.draft = null;
            state.pendingFile = null;
            resetAttachmentInput();
            clearStatus(elements.modalStatus);

            if (elements.modal) {
                elements.modal.hidden = true;
            }

            document.body.classList.remove("support-cloud-modal-open");
        }

        function createEmptyDraft() {
            return {
                recordId: "",
                title: "",
                description: "",
                creationDateValue: elements.endDate?.value || new Date().toISOString().slice(0, 10),
                creationDateDisplay: "",
                stateValue: "",
                stateLabel: "",
                typeValue: "",
                typeLabel: "",
                clientId: "",
                clientName: "",
                categoryValue: "",
                categoryLabel: "",
                creatorId: "",
                creatorName: config.currentUserName,
                hoursTaken: 0,
                methodValue: "",
                methodLabel: "",
                solution: "",
                hasAttachment: false,
                attachmentFileName: "",
                modifiedOnDisplay: ""
            };
        }

        function renderModal() {
            if (!state.draft) {
                return;
            }

            const isNew = !state.draft.recordId;
            if (elements.modalTitle) {
                elements.modalTitle.textContent = isNew ? "Nuevo ticket de soporte cloud" : "Editar ticket de soporte cloud";
            }
            if (elements.modalSubtitle) {
                elements.modalSubtitle.textContent = isNew
                    ? "Completa los campos y guarda el ticket."
                    : "Actualiza el formulario completo del ticket seleccionado.";
            }
            if (elements.modalMeta) {
                elements.modalMeta.textContent = isNew
                    ? `Rango activo: ${elements.rangeLabel?.textContent || "-"}`
                    : `Actualizado: ${state.draft.modifiedOnDisplay || "Sin fecha"}`;
            }

            if (elements.recordId) {
                elements.recordId.value = state.draft.recordId || "";
            }
            if (elements.clientId) {
                elements.clientId.value = state.draft.clientId || "";
            }

            setFieldValue(elements.fields.title, state.draft.title);
            setFieldValue(elements.fields.description, state.draft.description);
            setFieldValue(elements.fields.creationDate, state.draft.creationDateValue);
            setFieldValue(elements.fields.clientName, state.draft.clientName);
            setFieldValue(elements.fields.creatorName, state.draft.creatorName || config.currentUserName);
            setFieldValue(elements.fields.hoursTaken, formatInputNumber(state.draft.hoursTaken));
            setFieldValue(elements.fields.solution, state.draft.solution);

            populateSelect(elements.fields.state, state.board?.stateOptions, state.draft.stateValue);
            populateSelect(elements.fields.type, state.board?.typeOptions, state.draft.typeValue);
            populateSelect(elements.fields.category, state.board?.categoryOptions, state.draft.categoryValue);
            populateSelect(elements.fields.method, state.board?.methodOptions, state.draft.methodValue);

            renderAttachmentState();
            clearStatus(elements.modalStatus);
        }

        function populateSelect(select, options, selectedValue) {
            if (!(select instanceof HTMLSelectElement)) {
                return;
            }

            const items = Array.isArray(options) ? options : [];
            const selected = selectedValue === null || selectedValue === undefined ? "" : String(selectedValue);
            select.innerHTML = `
                <option value="">Selecciona una opcion...</option>
                ${items.map(item => `
                    <option value="${escapeHtml(item.value)}" ${String(item.value) === selected ? "selected" : ""}>${escapeHtml(item.label || "")}</option>
                `).join("")}
            `;
        }

        function renderAttachmentState() {
            if (!elements.attachmentName || !elements.attachmentHint || !elements.downloadLink || !state.draft) {
                return;
            }

            const pendingFile = state.pendingFile;
            const hasAttachment = Boolean(state.draft.recordId && state.draft.hasAttachment);

            elements.attachmentName.textContent = pendingFile
                ? pendingFile.name
                : hasAttachment
                    ? state.draft.attachmentFileName || "Adjunto cargado"
                    : "Sin adjunto cargado";

            elements.attachmentHint.textContent = pendingFile
                ? "El archivo se subira junto con el guardado del ticket."
                : hasAttachment
                    ? "Ya existe un adjunto asociado a este ticket."
                    : "Guarda el ticket y adjunta el soporte en PDF, imagen o Word.";

            elements.downloadLink.href = hasAttachment ? buildDownloadUrl(state.draft.recordId || "") : "#";
            elements.downloadLink.classList.toggle("is-disabled", !hasAttachment);
        }

        async function saveTicket() {
            if (state.saving || !state.draft) {
                return;
            }

            let payload;
            let savedRecord = null;
            const hadPendingFile = Boolean(state.pendingFile);
            try {
                payload = buildPayload();
            } catch (error) {
                setStatus(elements.modalStatus, "error", error instanceof Error ? error.message : "Revisa los datos del ticket.");
                return;
            }

            state.saving = true;
            setBusy(true);
            setStatus(elements.modalStatus, "info", payload.recordId ? "Guardando ticket..." : "Creando ticket...");

            try {
                const result = await fetchJson(config.saveUrl, {
                    method: "POST",
                    body: JSON.stringify(payload)
                });

                savedRecord = hydrateRecord(result?.record);
                state.draft = { ...savedRecord };
                if (state.pendingFile && savedRecord.recordId) {
                    setStatus(elements.modalStatus, "info", "Ticket guardado. Subiendo adjunto...");
                    savedRecord = await uploadPendingAttachment(savedRecord.recordId);
                    state.draft = { ...savedRecord };
                }

                closeModal();
                setBusy(false);
                await loadBoard({ force: true });
                setStatus(
                    elements.status,
                    "success",
                    hadPendingFile
                        ? "Ticket y adjunto guardados correctamente."
                        : (result?.message || "Ticket guardado correctamente."));
            } catch (error) {
                if (savedRecord?.recordId) {
                    renderModal();
                }
                setStatus(elements.modalStatus, "error", buildErrorMessage(error));
            } finally {
                state.saving = false;
                if (state.busy) {
                    setBusy(false);
                }
            }
        }

        async function uploadPendingAttachment(recordId) {
            const formData = new FormData();
            formData.append("recordId", recordId);
            formData.append("file", state.pendingFile);

            const result = await fetchJson(config.uploadUrl, {
                method: "POST",
                body: formData
            });

            return hydrateRecord(result?.record);
        }

        function buildPayload() {
            const title = (elements.fields.title?.value || "").trim();
            const description = (elements.fields.description?.value || "").trim();
            const creationDateValue = elements.fields.creationDate?.value || "";
            const stateValue = elements.fields.state?.value || "";
            const typeValue = elements.fields.type?.value || "";
            const clientId = elements.clientId?.value || "";
            const clientName = (elements.fields.clientName?.value || "").trim();
            const categoryValue = elements.fields.category?.value || "";
            const hoursTaken = parseDecimal(elements.fields.hoursTaken?.value || "0");
            const methodValue = elements.fields.method?.value || "";
            const solution = (elements.fields.solution?.value || "").trim();

            if (!title) {
                throw new Error("Debes diligenciar el titulo del ticket.");
            }
            if (!description) {
                throw new Error("Debes diligenciar la descripcion del ticket.");
            }
            if (!creationDateValue) {
                throw new Error("Debes diligenciar la fecha de creacion.");
            }
            if (!stateValue) {
                throw new Error("Debes seleccionar un estado.");
            }
            if (!typeValue) {
                throw new Error("Debes seleccionar un tipo.");
            }
            if (!clientId && !clientName) {
                throw new Error("Debes seleccionar un cliente.");
            }
            if (!categoryValue) {
                throw new Error("Debes seleccionar una categoria.");
            }
            if (!methodValue) {
                throw new Error("Debes seleccionar un metodo.");
            }
            if (hoursTaken < 0) {
                throw new Error("Las horas tomadas no pueden ser negativas.");
            }

            return {
                recordId: elements.recordId?.value || "",
                title,
                description,
                creationDateValue,
                stateValue: Number(stateValue),
                typeValue: Number(typeValue),
                clientId,
                clientName,
                categoryValue: Number(categoryValue),
                hoursTaken,
                methodValue: Number(methodValue),
                solution
            };
        }

        function hydrateRecord(record) {
            return {
                recordId: record?.recordId || "",
                title: record?.title || "",
                description: record?.description || "",
                creationDateValue: record?.creationDateValue || "",
                creationDateDisplay: record?.creationDateDisplay || "",
                stateValue: record?.stateValue ?? "",
                stateLabel: record?.stateLabel || "",
                typeValue: record?.typeValue ?? "",
                typeLabel: record?.typeLabel || "",
                clientId: record?.clientId || "",
                clientName: record?.clientName || "",
                categoryValue: record?.categoryValue ?? "",
                categoryLabel: record?.categoryLabel || "",
                creatorId: record?.creatorId || "",
                creatorName: record?.creatorName || config.currentUserName,
                hoursTaken: Number(record?.hoursTaken || 0),
                methodValue: record?.methodValue ?? "",
                methodLabel: record?.methodLabel || "",
                solution: record?.solution || "",
                hasAttachment: Boolean(record?.hasAttachment),
                attachmentFileName: record?.attachmentFileName || "",
                modifiedOnDisplay: record?.modifiedOnDisplay || ""
            };
        }

        function hydrateTrainingRecord(record) {
            return {
                recordId: record?.recordId || "",
                dateValue: record?.dateValue || "",
                dateDisplay: record?.dateDisplay || "",
                durationMinutes: Number(record?.durationMinutes || 0),
                durationHours: Number(record?.durationHours || 0),
                durationDisplay: record?.durationDisplay || "",
                clientId: record?.clientId || "",
                clientName: record?.clientName || "",
                attendees: Number(record?.attendees || 0),
                topicValue: record?.topicValue ?? "",
                topicLabel: record?.topicLabel || "",
                ownerId: record?.ownerId || "",
                ownerName: record?.ownerName || ""
            };
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
                elements.startDate,
                elements.endDate,
                elements.refresh,
                elements.newTicket
            ].forEach(element => {
                if (element) {
                    element.disabled = isBusy;
                }
            });

            [
                elements.fields.title,
                elements.fields.description,
                elements.fields.creationDate,
                elements.fields.state,
                elements.fields.type,
                elements.fields.clientName,
                elements.fields.category,
                elements.fields.hoursTaken,
                elements.fields.method,
                elements.fields.solution,
                elements.attachmentInput
            ].forEach(element => {
                if (element) {
                    element.disabled = isBusy;
                }
            });

            elements.closeModalButtons.forEach(button => {
                button.disabled = isBusy;
            });
        }

        function setTrainingsBusy(isBusy) {
            state.trainingsBusy = isBusy;

            [
                elements.startDate,
                elements.endDate,
                elements.refresh
            ].forEach(element => {
                if (element) {
                    element.disabled = isBusy;
                }
            });

            elements.subtabButtons.forEach(button => {
                button.disabled = isBusy;
            });
        }
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

        target.className = `support-cloud-status is-visible is-${type}`;
        target.textContent = message;
    }

    function clearStatus(target) {
        if (!target) {
            return;
        }

        target.className = "support-cloud-status";
        target.textContent = "";
    }

    function buildErrorMessage(error) {
        return error instanceof Error
            ? error.message
            : "Ocurrio un error inesperado.";
    }

    function setFieldValue(element, value) {
        if (!element) {
            return;
        }

        element.value = value ?? "";
    }

    function formatInputNumber(value) {
        return Number(value || 0).toFixed(2);
    }

    function parseDecimal(value) {
        const parsed = Number.parseFloat(String(value || "").replace(",", "."));
        return Number.isFinite(parsed) ? parsed : 0;
    }

    function buildTicketExcerpt(row) {
        const text = (row?.description || row?.solution || "").trim();
        if (!text) {
            return "Abre el ticket para ver el detalle completo.";
        }

        return truncateText(text, 180);
    }

    function renderTicketMeta(row) {
        const tags = [
            row?.categoryLabel,
            row?.methodLabel
        ]
            .filter(value => String(value || "").trim().length > 0)
            .map(value => `<span class="support-cloud-table__tag">${escapeHtml(value)}</span>`);

        if (!tags.length) {
            return "";
        }

        return `<div class="support-cloud-table__ticket-meta">${tags.join("")}</div>`;
    }

    function buildRangeLabel(startDate, endDate) {
        if (!startDate && !endDate) {
            return "Sin rango";
        }

        return [startDate || "-", endDate || "-"].join(" - ");
    }

    function renderPill(text) {
        return `<span class="support-cloud-pill">${escapeHtml(text || "-")}</span>`;
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
        const text = String(value ?? "")
            .replace(/\s+/g, " ")
            .trim();

        if (!text || text.length <= maxLength) {
            return text;
        }

        return `${text.slice(0, Math.max(0, maxLength - 1)).trimEnd()}…`;
    }

    function resetAttachmentInput() {
        roots.forEach(root => {
            root.querySelectorAll("[data-sc-attachment-input]").forEach(input => {
                input.value = "";
            });
        });
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
