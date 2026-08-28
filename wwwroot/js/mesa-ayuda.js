(() => {
    "use strict";

    const app = document.getElementById("helpDeskApp");
    if (!app) return;

    const $ = (selector, root = app) => root.querySelector(selector);
    const $$ = (selector, root = app) => Array.from(root.querySelectorAll(selector));
    const token = $('input[name="__RequestVerificationToken"]')?.value || "";
    const workspaceElement = $("[data-workspace]");
    const queueNav = $("[data-queue-nav]");
    const ticketList = $("[data-ticket-list]");
    const globalStatus = $("[data-global-status]");
    const emptyCase = $("[data-empty-case]");
    const caseView = $("[data-case]");
    const timeline = $("[data-timeline]");
    const auditForm = $("[data-audit-form]");
    const auditInstruction = $("[data-audit-instruction]");
    const auditSubmit = $("[data-audit-submit]");
    const messageSubmit = $("[data-message-submit]");
    const auditStatus = $("[data-audit-status]");
    const governanceDialog = $("[data-governance-dialog]");
    const aiConfigured = app.dataset.aiConfigured === "true";
    const schemaProvisioned = app.dataset.schemaProvisioned === "true";

    const state = {
        workspace: null,
        queue: "all",
        search: "",
        selectedId: "",
        pendingOperationKeys: new Map(),
        activeRuns: new Map(),
        operationStatuses: new Map(),
        drafts: new Map()
    };

    const queueIcons = {
        all: '<svg viewBox="0 0 24 24"><path d="M5 5h14v14H5zM8 9h8M8 13h8M8 17h5"/></svg>',
        new: '<svg viewBox="0 0 24 24"><path d="M4 7h16v12H4zM4 11h5l2 3h2l2-3h5M8 4h8"/></svg>',
        active: '<svg viewBox="0 0 24 24"><path d="M12 3a9 9 0 1 0 9 9M12 7v5l3 2M16 3h5v5"/></svg>',
        waiting: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>',
        closed: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="m8 12 2.5 2.5L16 9"/></svg>'
    };

    const timelineIcons = {
        "case-created": '<svg viewBox="0 0 24 24"><path d="M5 5h14v14H5zM8 9h8M8 13h8M8 17h5"/></svg>',
        attachment: '<svg viewBox="0 0 24 24"><path d="m8 12 5-5a3 3 0 0 1 4 4l-7 7a4 4 0 0 1-6-6l7-7"/></svg>',
        resolution: '<svg viewBox="0 0 24 24"><path d="m5 12 4 4L19 6"/></svg>',
        audit: '<svg viewBox="0 0 24 24"><path d="M4 5h16v14H4zM8 9h8M8 13h5M17 16l2 2"/></svg>',
        default: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="8"/></svg>'
    };

    function createElement(tag, className, text) {
        const element = document.createElement(tag);
        if (className) element.className = className;
        if (text !== undefined && text !== null) element.textContent = String(text);
        return element;
    }

    function setText(selector, value, fallback = "Sin confirmar") {
        const element = $(selector);
        if (element) element.textContent = value || fallback;
    }

    function showGlobalStatus(message, type = "") {
        if (!globalStatus) return;
        globalStatus.textContent = message || "";
        globalStatus.classList.toggle("is-error", type === "error");
        globalStatus.hidden = !message;
    }

    function showAuditStatus(message, type = "") {
        if (!auditStatus) return;
        auditStatus.textContent = message || "";
        auditStatus.classList.toggle("is-error", type === "error");
        auditStatus.classList.toggle("is-success", type === "success");
    }

    function setTicketStatus(ticketId, message, type = "", runId = "") {
        if (!ticketId) return;
        state.operationStatuses.set(ticketId, { message, type, runId });
        if (state.selectedId === ticketId) {
            showAuditStatus(message, type);
        }
    }

    async function parseResponse(response) {
        const contentType = response.headers.get("content-type") || "";
        let body;
        if (contentType.includes("json")) {
            body = await response.json();
        } else {
            body = { message: await response.text() };
        }

        if (!response.ok) {
            const validationErrors = body.errors
                ? Object.values(body.errors).flat().join(" ")
                : "";
            const message = [body.message, body.title, body.detail, validationErrors]
                .filter(Boolean)
                .join(" ");
            throw new Error(message || `La solicitud falló (${response.status}).`);
        }

        return body;
    }

    function requestHeaders(json = false) {
        const headers = {};
        if (token) headers.RequestVerificationToken = token;
        if (json) headers["Content-Type"] = "application/json";
        return headers;
    }

    function createClientId() {
        return globalThis.crypto?.randomUUID?.()
            || `${Date.now()}-${Math.random().toString(16).slice(2)}`;
    }

    function stableFingerprint(value) {
        let first = 2166136261;
        let second = 2246822519;
        for (let index = 0; index < value.length; index += 1) {
            const code = value.charCodeAt(index);
            first = Math.imul(first ^ code, 16777619);
            second = Math.imul(second ^ code, 3266489917);
        }
        return `${(first >>> 0).toString(16)}-${(second >>> 0).toString(16)}-${value.length}`;
    }

    function readPendingOperation(key) {
        try {
            return globalThis.sessionStorage?.getItem(key) || "";
        } catch {
            return "";
        }
    }

    function writePendingOperation(key, value) {
        try {
            globalThis.sessionStorage?.setItem(key, value);
        } catch {
            // The in-memory map still protects retries while this page remains open.
        }
    }

    function removePendingOperation(key) {
        state.pendingOperationKeys.delete(key);
        try {
            globalThis.sessionStorage?.removeItem(key);
        } catch {
            // Nothing else is required when browser storage is unavailable.
        }
    }

    function operationKey(kind, ticketId, content) {
        const fingerprint = stableFingerprint(`${kind}\u0000${ticketId}\u0000${content}`);
        const key = `mesa-ayuda:pending:${kind}:${ticketId}:${fingerprint}`;
        let value = state.pendingOperationKeys.get(key) || readPendingOperation(key);
        if (!value) {
            value = createClientId();
        }
        state.pendingOperationKeys.set(key, value);
        writePendingOperation(key, value);
        return { cacheKey: key, value };
    }

    function startTicketRun(kind, ticketId) {
        if (!ticketId || state.activeRuns.has(ticketId)) return null;
        const run = { id: createClientId(), kind, ticketId };
        state.activeRuns.set(ticketId, run);
        return run;
    }

    function finishTicketRun(run) {
        if (!run) return;
        if (state.activeRuns.get(run.ticketId)?.id === run.id) {
            state.activeRuns.delete(run.ticketId);
        }
    }

    function selectedTicket() {
        return state.workspace?.tickets?.find(ticket => ticket.recordId === state.selectedId) || null;
    }

    function filteredTickets() {
        const tickets = state.workspace?.tickets || [];
        const query = state.search.trim().toLocaleLowerCase("es");
        return tickets.filter(ticket => {
            const queueMatches = state.queue === "all" || ticket.statusKey === state.queue;
            if (!queueMatches) return false;
            if (!query) return true;
            return [
                ticket.reference,
                ticket.title,
                ticket.clientName,
                ticket.description,
                ticket.category,
                ticket.workload
            ].some(value => String(value || "").toLocaleLowerCase("es").includes(query));
        });
    }

    function renderQueues() {
        if (!queueNav) return;
        queueNav.replaceChildren();
        const queues = state.workspace?.queues || [];
        queues.forEach(queue => {
            const button = createElement("button", "help-desk__queue-button");
            button.type = "button";
            button.dataset.queue = queue.key;
            button.setAttribute("aria-current", queue.key === state.queue ? "true" : "false");
            button.title = `${queue.label}: ${queue.count}`;

            const icon = createElement("span", "help-desk__queue-icon");
            icon.setAttribute("aria-hidden", "true");
            icon.innerHTML = queueIcons[queue.key] || queueIcons.all;
            button.append(
                icon,
                createElement("span", "help-desk__queue-label", queue.label),
                createElement("span", "help-desk__queue-count", queue.count)
            );

            button.addEventListener("click", () => {
                state.queue = queue.key;
                const visible = filteredTickets();
                if (!visible.some(ticket => ticket.recordId === state.selectedId)) {
                    state.selectedId = visible[0]?.recordId || "";
                }
                renderQueues();
                renderTickets();
                renderCase();
            });
            queueNav.append(button);
        });

        const total = queues.find(queue => queue.key === "all")?.count || 0;
        setText("[data-queue-total]", total, "0");
    }

    function renderTickets() {
        if (!ticketList) return;
        ticketList.replaceChildren();
        const tickets = filteredTickets();
        const queue = (state.workspace?.queues || []).find(item => item.key === state.queue);
        setText("[data-list-title]", queue?.label || "Casos");
        setText("[data-list-count]", tickets.length, "0");

        if (!tickets.length) {
            const empty = createElement("div", "help-desk__empty-list");
            empty.append(
                createElement("strong", "", "No hay casos en esta vista"),
                createElement("span", "", state.search
                    ? "Prueba con otra búsqueda."
                    : "La cola se actualizará cuando existan registros.")
            );
            ticketList.append(empty);
            return;
        }

        tickets.forEach(ticket => {
            const button = createElement("button", "help-desk__ticket");
            button.type = "button";
            button.dataset.ticketId = ticket.recordId;
            button.setAttribute("aria-current", ticket.recordId === state.selectedId ? "true" : "false");

            const top = createElement("div", "help-desk__ticket-top");
            top.append(
                createElement("span", "help-desk__ticket-reference", ticket.reference),
                createStatus(ticket.status, ticket.statusTone, "help-desk__ticket-status")
            );

            const title = createElement("h3", "", ticket.title);
            const meta = createElement("div", "help-desk__ticket-meta");
            meta.append(
                createElement("span", "", ticket.clientName || "Cliente sin confirmar"),
                createElement("span", "", ticket.priority || "Sin priorizar")
            );
            const foot = createElement("div", "help-desk__ticket-foot");
            foot.append(
                createElement("span", "", ticket.category || ticket.workload || "Sin categoría"),
                createElement("span", "", ticket.lastActivityDisplay || ticket.createdAtDisplay || "")
            );

            button.append(top, title, meta, foot);
            button.addEventListener("click", () => {
                state.selectedId = ticket.recordId;
                renderTickets();
                renderCase();
                workspaceElement?.classList.add("is-showing-case");
                if (window.matchMedia("(max-width: 899.98px)").matches) {
                    $("[data-case-title]")?.focus?.({ preventScroll: true });
                }
            });
            ticketList.append(button);
        });
    }

    function createStatus(label, tone, className) {
        const status = createElement("span", className, label || "Sin estado");
        status.dataset.tone = tone || "active";
        return status;
    }

    function renderCase() {
        const ticket = selectedTicket();
        if (!ticket) {
            if (emptyCase) emptyCase.hidden = false;
            if (caseView) caseView.hidden = true;
            return;
        }

        if (emptyCase) emptyCase.hidden = true;
        if (caseView) caseView.hidden = false;
        setText("[data-case-reference]", ticket.reference);
        setText("[data-case-title]", ticket.title, "Caso sin título");
        setText("[data-case-client]", ticket.clientName, "Cliente sin confirmar");

        const status = $("[data-case-status]");
        if (status) {
            status.textContent = ticket.status || "Sin estado";
            status.dataset.tone = ticket.statusTone || "active";
        }

        const provisional = $("[data-provisional-reference]");
        if (provisional) provisional.hidden = !ticket.referenceIsProvisional;

        renderTenantGuard(ticket);
        renderFacts(ticket);
        renderTimeline(ticket);
        renderReadiness();
        renderFlow(ticket);
        configureComposer(ticket);
    }

    function renderTenantGuard(ticket) {
        const guard = $("[data-tenant-guard]");
        const confirmed = Boolean(ticket.tenantId);
        guard?.classList.toggle("is-confirmed", confirmed);
        setText(
            "[data-tenant-summary]",
            confirmed ? `${ticket.clientName} · ${ticket.tenantId}` : `${ticket.clientName} · Tenant ID pendiente`
        );
        setText(
            "[data-tenant-state]",
            confirmed ? "Identidad confirmada" : "Bloqueado para cambios"
        );
    }

    function renderFacts(ticket) {
        setText("[data-fact-client]", ticket.clientName);
        setText("[data-fact-agent]", ticket.assignedAgent, "Sin asignar");
        setText("[data-fact-category]", ticket.category, "Sin categoría");
        setText("[data-fact-workload]", ticket.workload, "No confirmada");
        setText("[data-fact-activity]", ticket.lastActivityDisplay || ticket.createdAtDisplay);
    }

    function renderTimeline(ticket) {
        if (!timeline) return;
        timeline.replaceChildren();
        (ticket.timeline || []).forEach(event => timeline.append(createTimelineEvent(event)));
    }

    function createTimelineEvent(event) {
        const item = createElement("li", "help-desk__timeline-event");
        item.dataset.tone = event.tone || "neutral";

        const node = createElement("span", "help-desk__timeline-node");
        node.setAttribute("aria-hidden", "true");
        node.innerHTML = timelineIcons[event.kind] || timelineIcons.default;

        const content = createElement("div", "help-desk__event-content");
        const head = createElement("div", "help-desk__event-head");
        const actorLabel = [event.label, event.actor].filter(Boolean).join(" · ");
        head.append(
            createElement("strong", "", actorLabel || "Actividad"),
            createElement("span", "", event.timestamp || "")
        );
        content.append(head);
        if (event.investigation) {
            content.append(createAuditCard(event.investigation));
        } else if (event.body) {
            content.append(createElement("p", "help-desk__event-body", event.body));
        }
        if (event.detail) content.append(createElement("p", "help-desk__event-detail", event.detail));
        item.append(node, content);
        return item;
    }

    function createAuditCard(result) {
        const card = createElement("article", "help-desk__audit-card");
        const summary = createElement("div", "help-desk__audit-summary");
        const top = createElement("div", "help-desk__audit-summary-top");
        top.append(
            createAuditBadge(classificationLabel(result.classification), classificationTone(result.classification)),
            createAuditBadge(severityLabel(result.severity), severityTone(result.severity)),
            createAuditBadge(`${Math.round(Number(result.confidence || 0) * 100)}% confianza`, "info"),
            createAuditBadge(result.workload || "Carga no confirmada", "info")
        );
        summary.append(top, createElement("p", "", result.summary || "Sin resumen."));
        card.append(summary);

        const grid = createElement("div", "help-desk__audit-grid");
        appendAuditSection(grid, "Hechos confirmados", result.confirmedFacts);
        appendAuditSection(grid, "Hipótesis", result.hypotheses);
        appendAuditSection(grid, "Información faltante", result.missingInformation);
        appendAuditSection(grid, "Comprobaciones recomendadas", result.recommendedChecks);
        appendAuditSection(grid, "Riesgos", result.riskFlags);
        appendAuditSection(grid, "Impacto", result.impact ? [result.impact] : []);
        card.append(grid);

        const next = createElement("div", "help-desk__audit-next");
        next.append(
            createElement("strong", "", "SIGUIENTE PASO"),
            createElement("span", "", result.nextAction || "Validar el análisis con el agente.")
        );
        card.append(next);
        return card;
    }

    function appendAuditSection(parent, title, values) {
        const section = createElement("section", "help-desk__audit-section");
        section.append(createElement("h4", "", title));
        const normalized = Array.isArray(values) ? values.filter(Boolean) : [];
        if (!normalized.length) {
            section.append(createElement("p", "", "Sin elementos confirmados."));
        } else {
            const list = createElement("ul");
            normalized.forEach(value => list.append(createElement("li", "", value)));
            section.append(list);
        }
        parent.append(section);
    }

    function createAuditBadge(label, tone) {
        const badge = createElement("span", "help-desk__audit-badge", label);
        badge.dataset.tone = tone || "info";
        return badge;
    }

    function classificationLabel(value) {
        return {
            support: "Caso de soporte",
            no_support: "No es soporte",
            doubtful: "Clasificación dudosa"
        }[value] || "Clasificación dudosa";
    }

    function classificationTone(value) {
        return value === "support" ? "success" : value === "no_support" ? "info" : "warning";
    }

    function severityLabel(value) {
        return {
            critical: "Severidad crítica",
            high: "Severidad alta",
            medium: "Severidad media",
            low: "Severidad baja",
            unconfirmed: "Severidad sin confirmar"
        }[value] || "Severidad sin confirmar";
    }

    function severityTone(value) {
        return value === "critical" || value === "high"
            ? "warning"
            : value === "low"
                ? "success"
                : "info";
    }

    function renderReadiness() {
        setText(
            "[data-readiness-copy]",
            schemaProvisioned
                ? "Esquema de Mesa de ayuda activo en Dataverse. El chat y la auditoría están disponibles; la ejecución y remediación aún no están habilitadas."
                : "Cola conectada a cr07a_ticket. El expediente durable y la auditoría esperan el aprovisionamiento confirmado de Dataverse."
        );
    }

    function renderFlow(ticket) {
        const hasAudit = (ticket.timeline || []).some(event =>
            event.kind === "audit" || Boolean(event.investigation));
        $$("[data-flow-step]").forEach(step => {
            step.classList.remove("is-current", "is-complete");
            const key = step.dataset.flowStep;
            if (!hasAudit && key === "audit") step.classList.add("is-current");
            if (hasAudit && key === "audit") step.classList.add("is-complete");
        });
    }

    function configureComposer(ticket) {
        if (!auditInstruction || !auditSubmit || !messageSubmit) return;

        const previousTicketId = auditInstruction.dataset.ticketId;
        if (previousTicketId && previousTicketId !== ticket.recordId) {
            state.drafts.set(previousTicketId, auditInstruction.value);
        }
        if (previousTicketId !== ticket.recordId) {
            auditInstruction.value = state.drafts.get(ticket.recordId) || "";
        }
        auditInstruction.dataset.ticketId = ticket.recordId;

        const activeRun = state.activeRuns.get(ticket.recordId);
        const isBusy = Boolean(activeRun);
        auditInstruction.disabled = !schemaProvisioned || isBusy;
        auditSubmit.disabled = !schemaProvisioned || !aiConfigured || isBusy;
        messageSubmit.disabled = !schemaProvisioned || isBusy;
        auditSubmit.textContent = activeRun?.kind === "audit" ? "Auditando…" : "Auditar caso";
        messageSubmit.textContent = activeRun?.kind === "message" ? "Guardando…" : "Guardar nota";

        if (isBusy) {
            const savedStatus = state.operationStatuses.get(ticket.recordId);
            setText(
                "[data-composer-state]",
                activeRun.kind === "audit" ? "Auditoría en curso" : "Guardando nota"
            );
            showAuditStatus(
                savedStatus?.message
                    || (activeRun.kind === "audit"
                        ? "El auditor está analizando hechos, faltantes y riesgos…"
                        : "Registrando la nota interna en Dataverse…"),
                savedStatus?.type || ""
            );
        } else if (!schemaProvisioned) {
            setText("[data-composer-state]", "Esquema durable pendiente");
            showAuditStatus("El expediente está en vista previa hasta terminar el aprovisionamiento.", "error");
        } else if (!aiConfigured) {
            setText("[data-composer-state]", "Configuración del modelo pendiente");
            showAuditStatus("Puedes registrar notas; falta completar la configuración segura del auditor IA.", "error");
        } else {
            setText("[data-composer-state]", "Responses API · salida estructurada");
            const savedStatus = state.operationStatuses.get(ticket.recordId);
            showAuditStatus(savedStatus?.message || "", savedStatus?.type || "");
        }
    }

    async function loadWorkspace(announce = false) {
        workspaceElement?.setAttribute("aria-busy", "true");
        if (announce) showGlobalStatus("Actualizando casos…");
        try {
            const response = await fetch(app.dataset.workspaceUrl, {
                headers: { Accept: "application/json" }
            });
            state.workspace = await parseResponse(response);
            const visible = filteredTickets();
            if (!visible.some(ticket => ticket.recordId === state.selectedId)) {
                state.selectedId = visible[0]?.recordId || "";
            }
            renderQueues();
            renderTickets();
            renderCase();
            showGlobalStatus(announce ? state.workspace.dataStatus : "");
        } catch (error) {
            showGlobalStatus(error.message || "No fue posible cargar los casos.", "error");
            renderLoadFailure(error.message);
        } finally {
            workspaceElement?.setAttribute("aria-busy", "false");
        }
    }

    function renderLoadFailure(message) {
        if (!ticketList) return;
        ticketList.replaceChildren();
        const empty = createElement("div", "help-desk__empty-list");
        empty.append(
            createElement("strong", "", "No pudimos abrir la bandeja"),
            createElement("span", "", message || "Revisa la conexión e intenta nuevamente.")
        );
        ticketList.append(empty);
    }

    async function submitAudit(event) {
        event.preventDefault();
        const ticket = selectedTicket();
        if (!ticket || !aiConfigured || !schemaProvisioned) return;
        const instruction = auditInstruction?.value?.trim() || "";
        const run = startTicketRun("audit", ticket.recordId);
        if (!run) {
            setTicketStatus(
                ticket.recordId,
                "Ya hay una operación activa para este caso. Espera a que termine.",
                "error"
            );
            return;
        }
        const operation = operationKey("audit", ticket.recordId, instruction);
        setTicketStatus(
            ticket.recordId,
            "El auditor está analizando hechos, faltantes y riesgos…",
            "",
            run.id
        );
        configureComposer(ticket);

        try {
            const response = await fetch(app.dataset.analyzeUrl, {
                method: "POST",
                headers: requestHeaders(true),
                body: JSON.stringify({
                    ticketId: ticket.recordId,
                    instruction,
                    idempotencyKey: operation.value
                })
            });
            const body = await parseResponse(response);
            removePendingOperation(operation.cacheKey);
            state.drafts.delete(ticket.recordId);
            if (auditInstruction?.dataset.ticketId === ticket.recordId) {
                auditInstruction.value = "";
            }
            await loadWorkspace(false);
            setTicketStatus(
                ticket.recordId,
                body.message || "Auditoría completada.",
                "success",
                run.id
            );
            if (state.selectedId === ticket.recordId) {
                timeline?.lastElementChild?.scrollIntoView({ behavior: "smooth", block: "start" });
            }
        } catch (error) {
            setTicketStatus(
                ticket.recordId,
                error.message || "No fue posible completar la auditoría.",
                "error",
                run.id
            );
        } finally {
            finishTicketRun(run);
            const currentTicket = selectedTicket();
            if (currentTicket) configureComposer(currentTicket);
            if (auditInstruction && state.selectedId === ticket.recordId) {
                auditInstruction.focus({ preventScroll: true });
            }
        }
    }

    async function submitMessage() {
        const ticket = selectedTicket();
        const content = auditInstruction?.value?.trim() || "";
        if (!ticket || !schemaProvisioned) return;
        if (!content) {
            showAuditStatus("Escribe una nota antes de guardarla.", "error");
            auditInstruction?.focus();
            return;
        }

        const run = startTicketRun("message", ticket.recordId);
        if (!run) {
            setTicketStatus(
                ticket.recordId,
                "Ya hay una operación activa para este caso. Espera a que termine.",
                "error"
            );
            return;
        }
        const operation = operationKey("message", ticket.recordId, content);
        setTicketStatus(
            ticket.recordId,
            "Registrando la nota interna en Dataverse…",
            "",
            run.id
        );
        configureComposer(ticket);

        try {
            const response = await fetch(app.dataset.messageUrl, {
                method: "POST",
                headers: requestHeaders(true),
                body: JSON.stringify({
                    ticketId: ticket.recordId,
                    content,
                    idempotencyKey: operation.value
                })
            });
            const body = await parseResponse(response);
            removePendingOperation(operation.cacheKey);
            state.drafts.delete(ticket.recordId);
            if (auditInstruction?.dataset.ticketId === ticket.recordId) {
                auditInstruction.value = "";
            }
            await loadWorkspace(false);
            setTicketStatus(
                ticket.recordId,
                body.message || "Nota registrada.",
                "success",
                run.id
            );
            if (state.selectedId === ticket.recordId) {
                timeline?.lastElementChild?.scrollIntoView({ behavior: "smooth", block: "start" });
            }
        } catch (error) {
            setTicketStatus(
                ticket.recordId,
                error.message || "No fue posible registrar la nota.",
                "error",
                run.id
            );
        } finally {
            finishTicketRun(run);
            const currentTicket = selectedTicket();
            if (currentTicket) configureComposer(currentTicket);
            if (auditInstruction && state.selectedId === ticket.recordId) {
                auditInstruction.focus({ preventScroll: true });
            }
        }
    }

    let searchTimer;
    $("[data-ticket-search]")?.addEventListener("input", event => {
        window.clearTimeout(searchTimer);
        searchTimer = window.setTimeout(() => {
            state.search = event.target.value || "";
            const visible = filteredTickets();
            if (!visible.some(ticket => ticket.recordId === state.selectedId)) {
                state.selectedId = visible[0]?.recordId || "";
            }
            renderTickets();
            renderCase();
        }, 180);
    });

    $("[data-reload]")?.addEventListener("click", () => loadWorkspace(true));
    $("[data-mobile-back]")?.addEventListener("click", () => {
        workspaceElement?.classList.remove("is-showing-case");
        ticketList?.querySelector(`[data-ticket-id="${CSS.escape(state.selectedId)}"]`)?.focus();
    });
    $("[data-copy-reference]")?.addEventListener("click", async event => {
        const ticket = selectedTicket();
        if (!ticket) return;
        try {
            await navigator.clipboard.writeText(ticket.reference);
            const button = event.currentTarget;
            const label = button.textContent;
            button.textContent = "Copiado";
            window.setTimeout(() => { button.textContent = label; }, 1400);
        } catch {
            showGlobalStatus(`Referencia: ${ticket.reference}`);
        }
    });
    $("[data-open-governance]")?.addEventListener("click", () => {
        if (typeof governanceDialog?.showModal === "function") {
            governanceDialog.showModal();
        } else if (governanceDialog) {
            governanceDialog.setAttribute("open", "");
        }
    });
    governanceDialog?.addEventListener("click", event => {
        if (event.target === governanceDialog) governanceDialog.close();
    });
    auditForm?.addEventListener("submit", submitAudit);
    messageSubmit?.addEventListener("click", submitMessage);
    auditInstruction?.addEventListener("input", () => {
        const ticketId = auditInstruction.dataset.ticketId;
        if (!ticketId) return;
        state.drafts.set(ticketId, auditInstruction.value);
        if (!state.activeRuns.has(ticketId) && state.operationStatuses.has(ticketId)) {
            state.operationStatuses.delete(ticketId);
            if (state.selectedId === ticketId) showAuditStatus("");
        }
    });

    loadWorkspace(false);
})();
