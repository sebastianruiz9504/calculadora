(() => {
    "use strict";

    const app = document.getElementById("crmApp");
    if (!app) return;

    const $ = (selector, root = app) => root ? root.querySelector(selector) : null;
    const $$ = (selector, root = app) => root ? Array.from(root.querySelectorAll(selector)) : [];
    const token = $('input[name="__RequestVerificationToken"]')?.value || "";
    const urls = {
        createCompany: app.dataset.createCompanyUrl,
        createContact: app.dataset.createContactUrl,
        createActivity: app.dataset.createActivityUrl,
        createDeal: app.dataset.createDealUrl,
        searchCompanies: app.dataset.searchCompaniesUrl,
        calculator: app.dataset.calculatorUrl,
        updateStage: app.dataset.updateStageUrl,
        companyDetail: app.dataset.companyDetailUrlTemplate,
        contactDetail: app.dataset.contactDetailUrlTemplate,
        dealDetail: app.dataset.dealDetailUrlTemplate,
        activityDetail: app.dataset.activityDetailUrlTemplate
    };
    const viewAsOwnerId = app.dataset.viewAsOwnerId || "";
    const lostStageValue = app.dataset.lostStageValue || "";
    const wonStageValue = app.dataset.wonStageValue || "";
    const quotedDealKind = app.dataset.quotedDealKind || "";
    const companyLifecycleValues = {
        lead: app.dataset.leadCompanyLifecycle || "",
        active: app.dataset.activeCompanyLifecycle || "",
        inactive: app.dataset.inactiveCompanyLifecycle || ""
    };
    const contactLifecycleValues = {
        lead: app.dataset.leadContactLifecycle || "",
        customer: app.dataset.customerContactLifecycle || "",
        inactive: app.dataset.inactiveContactLifecycle || ""
    };
    const plannedActivityStatus = app.dataset.plannedActivityStatus || "";
    const completedActivityStatus = app.dataset.completedActivityStatus || "";
    const meetingActivityType = app.dataset.meetingActivityType || "";
    const globalStatus = $("[data-global-status]");
    const crmNavLinks = $$("[data-crm-nav]");
    const crmViewTargets = $$("[data-crm-view-target]");
    const crmViews = $$("[data-crm-view]");
    const crmViewNames = new Set(crmViews.map(view => view.dataset.crmView));
    const crmSearchForm = $("[data-crm-search-form]");
    const currency = new Intl.NumberFormat("es-CO", {
        style: "currency",
        currency: "COP",
        maximumFractionDigits: 0
    });
    const shortDate = new Intl.DateTimeFormat("es-CO", {
        day: "2-digit",
        month: "short"
    });
    const shortDateTime = new Intl.DateTimeFormat("es-CO", {
        timeZone: "America/Bogota",
        day: "2-digit",
        month: "short",
        hour: "2-digit",
        minute: "2-digit"
    });
    const bogotaInputDateTime = new Intl.DateTimeFormat("en-CA", {
        timeZone: "America/Bogota",
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
        hourCycle: "h23"
    });

    const companyDrawer = $("[data-company-drawer]");
    const companyPanel = $(".crm-drawer__panel", companyDrawer);
    const companyForm = $("[data-company-form]");
    const companyStatus = $("[data-company-status]");
    const contactDrawer = $("[data-contact-drawer]");
    const contactPanel = $(".crm-drawer__panel", contactDrawer);
    const contactForm = $("[data-contact-form]");
    const contactStatus = $("[data-contact-status]");
    const contactCompanySelect = $("[data-contact-company]");
    const contactLifecycleSelect = $("[data-contact-lifecycle]");
    const contactLifecycleHelp = $("[data-contact-lifecycle-help]");
    const activityDrawer = $("[data-activity-drawer]");
    const activityPanel = $(".crm-drawer__panel", activityDrawer);
    const activityForm = $("[data-activity-form]");
    const activityStatus = $("[data-activity-status]");
    const activityTypeSelect = $("[data-activity-type-select]");
    const activityMeetingTypeField = $("[data-activity-meeting-type-field]");
    const activityMeetingTypeSelect = $("[data-activity-meeting-type-select]");
    const activityStatusSelect = $("[data-activity-status-select]");
    const activityPlannedInput = $("[data-activity-planned-input]");
    const activityCompletedField = $("[data-activity-completed-field]");
    const activityCompletedInput = $("[data-activity-completed-input]");
    const activityResultField = $("[data-activity-result-field]");
    const activityResultInput = $("[data-activity-result-input]");
    const dealDrawer = $("[data-deal-drawer]");
    const dealPanel = $(".crm-drawer__panel", dealDrawer);
    const dealForm = $("[data-deal-form]");
    const dealStatus = $("[data-deal-status]");
    const dealManualFields = $("[data-deal-manual-fields]");
    const dealCalculatorPanel = $("[data-deal-calculator-panel]");
    const dealCompanySearch = $("[data-deal-company-search]");
    const dealCompanyResults = $("[data-deal-company-results]");
    const dealContactSelect = $("[data-deal-contact-select]");

    const lossDialog = $("[data-loss-dialog]");
    const lossForm = $("[data-loss-form]", lossDialog);
    const lossReason = $("[data-loss-reason]", lossDialog);
    const lossStatus = $("[data-loss-status]", lossDialog);

    let openDrawerState = null;
    let pendingLossSelect = null;
    let lossSubmitting = false;
    let dealKindFilter = "all";
    let dealCompanySearchRequest = 0;

    function showStatus(element, message, type = "") {
        if (!element) return;
        element.textContent = message || "";
        element.classList.toggle("is-error", type === "error");
        element.classList.toggle("is-success", type === "success");
        element.hidden = !message;
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
            const validationMessages = body?.errors && typeof body.errors === "object"
                ? Object.values(body.errors)
                    .flatMap(value => Array.isArray(value) ? value : [value])
                    .filter(value => typeof value === "string" && value.trim())
                : [];
            const messages = [body?.detail, body?.message, ...validationMessages]
                .filter(value => typeof value === "string" && value.trim());
            const error = new Error(
                [...new Set(messages)].join(" ") || "La solicitud falló (" + response.status + ").");
            error.status = response.status;
            throw error;
        }

        return body;
    }

    function requestHeaders(json = false) {
        const headers = {};
        if (token) headers.RequestVerificationToken = token;
        if (json) headers["Content-Type"] = "application/json";
        return headers;
    }

    function withViewAs(url) {
        if (!url || !viewAsOwnerId) return url || "";
        const target = new URL(url, window.location.origin);
        target.searchParams.set("ViewAsOwnerId", viewAsOwnerId);
        return target.origin === window.location.origin
            ? `${target.pathname}${target.search}${target.hash}`
            : target.toString();
    }

    function detailUrl(template, id) {
        if (!template || !id) return "";
        return withViewAs(template.replace("__id__", encodeURIComponent(id)));
    }

    function initializeScopeSelector() {
        const form = $("[data-crm-scope-form]");
        const select = $("[data-crm-scope-select]", form);
        if (!form || !(select instanceof HTMLSelectElement)) return;

        form.addEventListener("submit", event => {
            event.preventDefault();
            const target = new URL(window.location.href);
            const selectedOwnerId = select.value.trim();
            ["CompanyPage", "ContactPage", "DealPage", "ActivityPage"].forEach(name => {
                target.searchParams.delete(name);
            });
            if (selectedOwnerId) {
                target.searchParams.set("ViewAsOwnerId", selectedOwnerId);
            } else {
                target.searchParams.delete("ViewAsOwnerId");
            }
            window.location.assign(target.toString());
        });
        select.addEventListener("change", () => form.requestSubmit());
    }

    function setBusy(control, busy, busyLabel = "Guardando…") {
        if (!control) return;
        if (busy) {
            control.dataset.originalLabel = control.textContent;
            control.textContent = busyLabel;
            control.disabled = true;
        } else {
            control.textContent = control.dataset.originalLabel || control.textContent;
            control.disabled = false;
        }
    }

    function debounce(callback, wait = 280) {
        let timer;
        return (...args) => {
            window.clearTimeout(timer);
            timer = window.setTimeout(() => callback(...args), wait);
        };
    }

    function crmViewHash(viewName) {
        return "#crm-" + viewName;
    }

    function crmViewFromLocation() {
        let decodedHash = "";
        try {
            decodedHash = decodeURIComponent(window.location.hash || "");
        } catch {
            decodedHash = "";
        }
        const candidate = decodedHash.replace(/^#crm-/i, "")
            .trim()
            .toLocaleLowerCase("es");
        return crmViewNames.has(candidate) ? candidate : "resumen";
    }

    function preserveCrmViewInNavigation(viewName) {
        const hash = crmViewHash(viewName);
        if (crmSearchForm) {
            const action = new URL(
                crmSearchForm.getAttribute("action") || window.location.pathname,
                window.location.href);
            action.hash = hash;
            crmSearchForm.setAttribute("action", action.pathname + action.search + action.hash);
        }

        $$("[data-crm-preserve-view], .crm-pagination a").forEach(link => {
            const target = new URL(link.getAttribute("href") || "", window.location.href);
            target.hash = hash;
            link.setAttribute("href", target.pathname + target.search + target.hash);
        });
    }

    function activateCrmView(viewName, options = {}) {
        const normalized = crmViewNames.has(viewName) ? viewName : "resumen";
        const activePanel = crmViews.find(view => view.dataset.crmView === normalized);
        if (!activePanel) return;

        crmViews.forEach(view => {
            const active = view === activePanel;
            view.hidden = !active;
            view.classList.toggle("is-active", active);
        });
        crmNavLinks.forEach(link => {
            const active = link.dataset.crmNav === normalized;
            link.classList.toggle("is-active", active);
            if (active) {
                link.setAttribute("aria-current", "page");
            } else {
                link.removeAttribute("aria-current");
            }
        });
        app.dataset.activeView = normalized;
        preserveCrmViewInNavigation(normalized);

        const hash = crmViewHash(normalized);
        if (window.location.hash !== hash && options.history === "push") {
            if (window.history?.pushState) {
                window.history.pushState({ crmView: normalized }, "", hash);
            } else {
                window.location.hash = hash;
            }
        } else if (window.location.hash !== hash && options.history === "replace") {
            if (window.history?.replaceState) {
                window.history.replaceState({ crmView: normalized }, "", hash);
            } else {
                window.location.replace(hash);
            }
        }

        if (options.scroll) {
            activePanel.scrollIntoView({
                behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth",
                block: "start"
            });
        }
    }

    function handleCrmNavigation(event) {
        const link = event.currentTarget;
        const viewName = link.dataset.crmNav || link.dataset.crmViewTarget;
        if (!crmViewNames.has(viewName)) return;
        event.preventDefault();
        activateCrmView(viewName, { history: "push", scroll: true });
    }

    function localDateTimeValue(date) {
        const parts = Object.fromEntries(
            bogotaInputDateTime
                .formatToParts(date)
                .filter(part => part.type !== "literal")
                .map(part => [part.type, part.value]));
        return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}`;
    }

    function bogotaLocalToIso(value) {
        if (!value) return null;
        const normalized = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(value)
            ? `${value}:00`
            : value;
        const date = new Date(`${normalized}-05:00`);
        return Number.isNaN(date.getTime()) ? null : date.toISOString();
    }

    function focusableElements(container) {
        return $$(
            'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
            container
        ).filter(element => !element.hidden && element.getAttribute("aria-hidden") !== "true");
    }

    function openDrawer(drawer, panel, status, trigger, firstFocusSelector, beforeOpen) {
        if (!drawer || !panel) return;
        if (openDrawerState?.drawer && openDrawerState.drawer !== drawer) {
            closeDrawer(openDrawerState.drawer);
        }

        openDrawerState = {
            drawer,
            trigger: trigger || document.activeElement
        };
        showStatus(status, "");
        if (typeof beforeOpen === "function") beforeOpen();
        drawer.hidden = false;
        document.body.classList.add("crm-drawer-open");
        window.requestAnimationFrame(() => $(firstFocusSelector, drawer)?.focus());
    }

    function closeDrawer(drawer, status) {
        if (!drawer) return;
        const trigger = openDrawerState?.drawer === drawer ? openDrawerState.trigger : null;
        drawer.hidden = true;
        showStatus(status, "");
        if (openDrawerState?.drawer === drawer) openDrawerState = null;
        if (!openDrawerState) document.body.classList.remove("crm-drawer-open");
        if (trigger instanceof HTMLElement) trigger.focus();
    }

    function trapDrawerFocus(event, drawer, panel, status) {
        if (event.key === "Escape") {
            event.preventDefault();
            closeDrawer(drawer, status);
            return;
        }

        if (event.key !== "Tab") return;
        const focusable = focusableElements(panel);
        if (!focusable.length) return;
        const first = focusable[0];
        const last = focusable[focusable.length - 1];

        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
        }
    }

    function setActivityDefaultDate() {
        if (!activityPlannedInput || activityPlannedInput.value) return;
        const nextHour = new Date();
        nextHour.setMinutes(0, 0, 0);
        nextHour.setHours(nextHour.getHours() + 1);
        activityPlannedInput.value = localDateTimeValue(nextHour);
    }

    function syncActivityStatus() {
        if (!activityStatusSelect) return;
        const isPlanned = activityStatusSelect.value === plannedActivityStatus;
        const isCompleted = activityStatusSelect.value === completedActivityStatus;

        if (activityPlannedInput) activityPlannedInput.required = isPlanned;
        if (activityCompletedField) activityCompletedField.hidden = !isCompleted;
        if (activityCompletedInput) {
            activityCompletedInput.disabled = !isCompleted;
            activityCompletedInput.required = isCompleted;
            if (isCompleted && !activityCompletedInput.value) {
                activityCompletedInput.value = localDateTimeValue(new Date());
            }
        }
        if (activityResultField) activityResultField.hidden = !isCompleted;
        if (activityResultInput) {
            activityResultInput.disabled = !isCompleted;
            activityResultInput.required = isCompleted;
        }
    }

    function syncActivityType() {
        if (!(activityTypeSelect instanceof HTMLSelectElement)
            || !(activityMeetingTypeSelect instanceof HTMLSelectElement)) {
            return;
        }

        const isMeeting = activityTypeSelect.value === meetingActivityType;
        if (activityMeetingTypeField) activityMeetingTypeField.hidden = !isMeeting;
        activityMeetingTypeSelect.disabled = !isMeeting;
        activityMeetingTypeSelect.required = isMeeting;
        if (!isMeeting) activityMeetingTypeSelect.value = "";
    }

    function openActivityDrawer(trigger) {
        openDrawer(
            activityDrawer,
            activityPanel,
            activityStatus,
            trigger,
            "[data-activity-first-focus]",
            () => {
                setActivityDefaultDate();
                syncActivityType();
                syncActivityStatus();
            });
    }

    function openCompanyDrawer(trigger) {
        openDrawer(companyDrawer, companyPanel, companyStatus, trigger, "[data-company-first-focus]");
    }

    function syncContactLifecycle() {
        if (!(contactCompanySelect instanceof HTMLSelectElement)
            || !(contactLifecycleSelect instanceof HTMLSelectElement)) {
            return;
        }

        const companyLifecycle = contactCompanySelect.selectedOptions[0]?.dataset.companyLifecycle || "";
        let targetLifecycle = "";
        let help = "El ciclo se ajusta al tipo de empresa seleccionada.";

        if (companyLifecycle === companyLifecycleValues.lead) {
            targetLifecycle = contactLifecycleValues.lead;
            help = "Los contactos de una empresa Lead se registran como Lead.";
        } else if (companyLifecycle === companyLifecycleValues.active) {
            targetLifecycle = contactLifecycleValues.customer;
            help = "Los contactos de un cliente activo se registran como Cliente.";
        } else if (companyLifecycle === companyLifecycleValues.inactive) {
            targetLifecycle = contactLifecycleValues.inactive;
            help = "Los contactos de una empresa inactiva se registran como Inactivo.";
        }

        if (targetLifecycle) contactLifecycleSelect.value = targetLifecycle;
        contactLifecycleSelect.disabled = Boolean(targetLifecycle);
        if (contactLifecycleHelp) contactLifecycleHelp.textContent = help;
    }

    function openContactDrawer(trigger) {
        openDrawer(
            contactDrawer,
            contactPanel,
            contactStatus,
            trigger,
            "[data-contact-first-focus]",
            syncContactLifecycle);
    }

    function applySimpleFilter(inputSelector, itemSelector, emptySelector) {
        const input = $(inputSelector);
        const query = input?.value.trim().toLocaleLowerCase("es") || "";
        let visible = 0;
        $$(itemSelector).forEach(item => {
            const matches = !query || (item.dataset.searchText || "").includes(query);
            item.hidden = !matches;
            if (matches) visible++;
        });
        const empty = $(emptySelector);
        if (empty) empty.hidden = visible > 0;
    }

    function bindFilter(inputSelector, itemSelector, emptySelector) {
        const input = $(inputSelector);
        if (!input) return;
        input.addEventListener("input", debounce(() => {
            applySimpleFilter(inputSelector, itemSelector, emptySelector);
        }));
    }

    function applyCompanyFilter() {
        const search = $("[data-company-search]")?.value.trim().toLocaleLowerCase("es") || "";
        const lifecycle = String($("[data-company-lifecycle-filter]")?.value || "");
        let visible = 0;

        $$("[data-company-row]").forEach(row => {
            const matchesSearch = !search || (row.dataset.searchText || "").includes(search);
            const matchesLifecycle = !lifecycle || row.dataset.companyLifecycle === lifecycle;
            const matches = matchesSearch && matchesLifecycle;
            row.hidden = !matches;
            if (matches) visible++;
        });

        const total = $$("[data-company-row]").length;
        const table = $("[data-company-table]");
        const empty = $("[data-company-empty]");
        if (table) table.hidden = visible === 0;
        if (empty) {
            empty.hidden = visible > 0;
            const title = $("strong", empty);
            const description = $("span", empty);
            if (title) title.textContent = total === 0 ? "Aún no hay empresas" : "No encontramos empresas";
            if (description) {
                description.textContent = total === 0
                    ? "Crea una empresa lead para iniciar su seguimiento comercial."
                    : "Ajusta la búsqueda o el tipo de empresa.";
            }
        }
    }

    function updateStageSummary(stage) {
        if (!stage) return;
        const visibleCards = $$("[data-deal-card]", stage).filter(card => !card.hidden);
        const count = $("[data-stage-count]", stage);
        const total = $("[data-stage-total]", stage);
        const empty = $("[data-stage-empty]", stage);
        const amount = visibleCards.reduce((sum, card) => sum + (Number(card.dataset.amount) || 0), 0);
        if (count) {
            count.textContent = visibleCards.length
                + (visibleCards.length === 1 ? " negocio" : " negocios");
        }
        if (total) total.textContent = currency.format(amount);
        if (empty) empty.hidden = visibleCards.length > 0;
    }

    function updateOpenPageSummary(sourceStage, targetStage, card) {
        const sourceIsClosed = sourceStage?.dataset.stageClosed === "true";
        const targetIsClosed = targetStage?.dataset.stageClosed === "true";
        if (sourceIsClosed === targetIsClosed) return;

        const direction = targetIsClosed ? -1 : 1;
        const count = $("[data-open-deals-count]");
        const total = $("[data-open-pipeline-total]");
        if (count) {
            count.textContent = String(Math.max(0, (Number(count.textContent) || 0) + direction));
        }
        if (total) {
            const nextValue = Math.max(
                0,
                (Number(total.dataset.value) || 0) + direction * (Number(card?.dataset.amount) || 0));
            total.dataset.value = String(nextValue);
            total.textContent = currency.format(nextValue);
        }
    }

    function applyPipelineFilter() {
        const input = $("[data-pipeline-search]");
        if (!input) return;
        const query = input.value.trim().toLocaleLowerCase("es");
        let visible = 0;

        $$("[data-pipeline-stage]").forEach(stage => {
            $$("[data-deal-card]", stage).forEach(card => {
                const matchesSearch = !query || (card.dataset.searchText || "").includes(query);
                const matchesKind = dealKindFilter === "all"
                    || card.dataset.dealKind === dealKindFilter;
                const matches = matchesSearch && matchesKind;
                card.hidden = !matches;
                if (matches) visible++;
            });
            updateStageSummary(stage);
        });

        const empty = $("[data-pipeline-empty]");
        const board = $("[data-pipeline-board]");
        if (empty) empty.hidden = visible > 0;
        if (board) board.hidden = visible === 0;
    }

    function setDealKindFilter(button) {
        if (!(button instanceof HTMLButtonElement)) return;
        dealKindFilter = button.dataset.dealKindFilter || "all";
        $$("[data-deal-kind-filter]").forEach(filterButton => {
            const active = filterButton === button;
            filterButton.classList.toggle("is-active", active);
            filterButton.setAttribute("aria-pressed", active ? "true" : "false");
        });
        applyPipelineFilter();
    }

    function openLossDialog(select) {
        if (!lossDialog || !lossReason) {
            select.value = select.dataset.currentStage;
            showStatus(globalStatus, "No fue posible abrir el registro del motivo de pérdida.", "error");
            return;
        }

        pendingLossSelect = select;
        const dealName = $("h4", select.closest("[data-deal-card]"))?.textContent?.trim();
        const dealNameElement = $("[data-loss-deal-name]", lossDialog);
        if (dealNameElement) dealNameElement.textContent = dealName || "este negocio";
        lossReason.value = "";
        lossReason.setCustomValidity("");
        showStatus(lossStatus, "");
        document.body.classList.add("crm-modal-open");

        if (typeof lossDialog.showModal === "function") {
            lossDialog.showModal();
        } else {
            lossDialog.setAttribute("open", "");
        }

        window.requestAnimationFrame(() => lossReason.focus());
    }

    function closeLossDialog(restoreStage = true, force = false) {
        if (lossSubmitting && !force) return;
        const select = pendingLossSelect;
        if (restoreStage && select) select.value = select.dataset.currentStage;

        if (lossDialog?.open && typeof lossDialog.close === "function") {
            lossDialog.close();
        } else {
            lossDialog?.removeAttribute("open");
        }

        document.body.classList.remove("crm-modal-open");
        showStatus(lossStatus, "");
        pendingLossSelect = null;
        if (select instanceof HTMLElement) select.focus();
    }

    async function persistDealStage(select, reason = "") {
        const dealId = select.dataset.dealId;
        const previousStageValue = select.dataset.currentStage;
        const targetStageValue = select.value;
        if (!dealId || targetStageValue === previousStageValue) return null;

        const targetStage = $('[data-pipeline-stage][data-stage-value="' + CSS.escape(targetStageValue) + '"]');
        const targetList = $("[data-stage-deals]", targetStage);
        if (!targetList) throw new Error("No se encontró la etapa seleccionada en el tablero.");

        const card = select.closest("[data-deal-card]");
        const sourceStage = select.closest("[data-pipeline-stage]");
        select.disabled = true;
        card?.classList.add("is-updating");

        try {
            const response = await fetch(urls.updateStage, {
                method: "POST",
                headers: requestHeaders(true),
                body: JSON.stringify({
                    dealId,
                    newStage: Number(targetStageValue),
                    reason
                })
            });
            const result = await parseResponse(response);
            targetList.insertBefore(card, $("[data-stage-empty]", targetStage));
            select.dataset.currentStage = targetStageValue;
            updateOpenPageSummary(sourceStage, targetStage, card);
            updateStageSummary(sourceStage);
            updateStageSummary(targetStage);
            return result;
        } finally {
            select.disabled = false;
            card?.classList.remove("is-updating");
        }
    }

    async function updateDealStage(select) {
        const previousStageValue = select.dataset.currentStage;
        if (select.value === previousStageValue) return;
        if (!urls.updateStage) {
            select.value = previousStageValue;
            showStatus(globalStatus, "La actualización de etapas aún no está configurada.", "error");
            return;
        }

        showStatus(globalStatus, "");
        try {
            const result = await persistDealStage(select);
            showStatus(globalStatus, result?.message || "El negocio cambió de etapa.", "success");
        } catch (error) {
            select.value = previousStageValue;
            showStatus(globalStatus, error.message, "error");
        }
    }

    async function submitLostDeal(event) {
        event.preventDefault();
        if (!pendingLossSelect || !lossForm || !lossReason) return;

        const reason = lossReason.value.trim();
        lossReason.setCustomValidity(reason ? "" : "Indica el motivo por el que se perdió el negocio.");
        if (!lossForm.reportValidity()) return;

        if (!urls.updateStage) {
            showStatus(lossStatus, "La actualización de etapas aún no está configurada.", "error");
            return;
        }

        const button = $("[data-confirm-loss]", lossDialog);
        const cancelButtons = $$("[data-cancel-loss]", lossDialog);
        lossSubmitting = true;
        setBusy(button, true, "Guardando…");
        cancelButtons.forEach(cancelButton => { cancelButton.disabled = true; });
        lossReason.disabled = true;
        lossDialog.setAttribute("aria-busy", "true");
        showStatus(lossStatus, "");
        showStatus(globalStatus, "");

        try {
            const result = await persistDealStage(pendingLossSelect, reason);
            closeLossDialog(false, true);
            showStatus(globalStatus, result?.message || "El negocio se cerró como perdido.", "success");
        } catch (error) {
            if (error.status === 409) {
                closeLossDialog(true, true);
                showStatus(globalStatus, error.message, "error");
            } else {
                showStatus(lossStatus, error.message, "error");
            }
        } finally {
            lossSubmitting = false;
            setBusy(button, false);
            cancelButtons.forEach(cancelButton => { cancelButton.disabled = false; });
            lossReason.disabled = false;
            lossDialog.removeAttribute("aria-busy");
        }
    }

    function selectedLabel(form, formData, fieldName, fallback = "") {
        const select = form?.elements.namedItem(fieldName);
        const value = formData.get(fieldName);
        if (!(select instanceof HTMLSelectElement) || !value) return fallback;
        return select.selectedOptions[0]?.textContent?.trim() || fallback;
    }

    function companyLifecycleLabel(company) {
        const value = String(company.lifecycleValue ?? company.lifecycle ?? "");
        if (value === companyLifecycleValues.lead) return "Lead";
        if (value === companyLifecycleValues.active) return "Cliente activo";
        if (value === companyLifecycleValues.inactive) return "Inactivo";
        return company.lifecycleLabel || "Lead";
    }

    function buildCompanyRow(company) {
        const lifecycle = companyLifecycleLabel(company);
        const lifecycleValue = String(
            company.lifecycleValue
            ?? company.lifecycle
            ?? companyLifecycleValues.lead);
        const row = document.createElement("article");
        row.className = "crm-table__row crm-table__row--companies";
        row.setAttribute("role", "row");
        row.dataset.companyRow = "";
        row.dataset.companyId = company.id || "";
        row.dataset.companyLifecycle = lifecycleValue;
        row.dataset.searchText = [
            company.name,
            company.taxId,
            company.city,
            company.email,
            company.phone,
            lifecycle
        ].filter(Boolean).join(" ").toLocaleLowerCase("es");

        const identityCell = document.createElement("div");
        identityCell.setAttribute("role", "cell");
        const identity = document.createElement("div");
        identity.className = "crm-company__identity";
        const name = document.createElement("strong");
        const nameLink = document.createElement("a");
        nameLink.className = "crm-record-link";
        nameLink.href = detailUrl(urls.companyDetail, company.id) || "#";
        nameLink.textContent = company.name || "Empresa";
        name.append(nameLink);
        const badge = document.createElement("span");
        badge.className = "crm-record-badge";
        badge.dataset.companyLifecycleLabel = "";
        badge.textContent = lifecycle;
        identity.append(name, badge);
        const taxId = document.createElement("span");
        taxId.textContent = company.taxId ? "NIT " + company.taxId : "Sin NIT registrado";
        identityCell.append(identity, taxId);

        const contactCell = document.createElement("div");
        contactCell.setAttribute("role", "cell");
        const email = document.createElement("strong");
        email.textContent = company.email || "Sin correo registrado";
        const phone = document.createElement("span");
        phone.textContent = company.phone || "Sin teléfono registrado";
        contactCell.append(email, phone);

        const locationCell = document.createElement("div");
        locationCell.setAttribute("role", "cell");
        const city = document.createElement("strong");
        city.textContent = company.city || "Sin ciudad registrada";
        const locationLabel = document.createElement("span");
        locationLabel.textContent = "Ubicación comercial";
        locationCell.append(city, locationLabel);

        row.append(identityCell, contactCell, locationCell);
        return row;
    }

    function addCompanyToSelects(company) {
        if (!company.id) return;
        const lifecycle = companyLifecycleLabel(company);
        const lifecycleValue = String(
            company.lifecycleValue
            ?? company.lifecycle
            ?? companyLifecycleValues.lead);

        if (contactCompanySelect instanceof HTMLSelectElement) {
            const option = new Option(
                [company.name || "Empresa", lifecycle].filter(Boolean).join(" · "),
                company.id);
            option.dataset.companyLifecycle = lifecycleValue;
            option.dataset.companyLifecycleLabel = lifecycle;
            contactCompanySelect.add(option);
        }

        const activityCompanySelect = activityForm?.elements.namedItem("CompanyId");
        if (activityCompanySelect instanceof HTMLSelectElement) {
            activityCompanySelect.add(new Option(company.name || "Empresa", company.id));
        }

        $$("[data-open-contact]").forEach(button => {
            button.disabled = false;
            button.removeAttribute("title");
        });
    }

    function companyPayload() {
        const elements = companyForm.elements;
        return {
            name: String(elements.namedItem("Name")?.value || "").trim(),
            taxId: String(elements.namedItem("TaxId")?.value || "").trim(),
            email: String(elements.namedItem("Email")?.value || "").trim(),
            phone: String(elements.namedItem("Phone")?.value || "").trim(),
            city: String(elements.namedItem("City")?.value || "").trim()
        };
    }

    async function submitCompany(event) {
        event.preventDefault();
        if (!companyForm || !urls.createCompany) {
            showStatus(companyStatus, "La creación de empresas aún no está configurada.", "error");
            return;
        }
        if (!companyForm.reportValidity()) return;

        const button = $("[data-save-company]", companyForm);
        const payload = companyPayload();
        setBusy(button, true, "Creando…");
        showStatus(companyStatus, "");

        try {
            const response = await fetch(urls.createCompany, {
                method: "POST",
                headers: requestHeaders(true),
                body: JSON.stringify(payload)
            });
            const result = await parseResponse(response);
            const record = result?.record || result?.Record || {};
            const id = record.id || record.Id || "";
            const company = {
                ...payload,
                lifecycleValue: Number(companyLifecycleValues.lead),
                lifecycleLabel: "Lead",
                ...record,
                id
            };
            const target = detailUrl(urls.companyDetail, id);
            if (!target) {
                throw new Error("La empresa fue creada, pero no recibimos su identificador para abrir la ficha.");
            }
            $("[data-company-list]")?.prepend(buildCompanyRow(company));
            addCompanyToSelects(company);
            companyForm.reset();
            closeDrawer(companyDrawer, companyStatus);
            applyCompanyFilter();
            activateCrmView("empresas", { history: "push", scroll: true });
            showStatus(globalStatus, result.message || "Empresa lead creada correctamente.", "success");
            window.location.assign(target);
        } catch (error) {
            showStatus(companyStatus, error.message, "error");
        } finally {
            setBusy(button, false);
        }
    }

    function initials(firstName, lastName) {
        return ((firstName || "").trim().charAt(0) + (lastName || "").trim().charAt(0))
            .toLocaleUpperCase("es") || "—";
    }

    function contactLifecycleLabel(contact) {
        switch (Number(contact.lifecycleValue ?? contact.lifecycle)) {
            case 645250000: return "Lead";
            case 645250001: return "MQL";
            case 645250002: return "SQL";
            case 645250003: return "Cliente";
            case 645250004: return "Inactivo";
            default: return contact.lifecycleLabel || "Sin etapa";
        }
    }

    function buildContactRow(contact) {
        const fullName = contact.fullName
            || [contact.firstName, contact.lastName].filter(Boolean).join(" ")
            || "Contacto";
        const lifecycle = contactLifecycleLabel(contact);
        const row = document.createElement("article");
        row.className = "crm-contact";
        row.dataset.contactRow = "";
        row.dataset.contactId = contact.id || "";
        row.dataset.companyId = contact.companyId || "";
        row.dataset.searchText = [
            fullName,
            contact.companyName,
            contact.companyLifecycleLabel,
            contact.email,
            contact.jobTitle,
            lifecycle
        ].filter(Boolean).join(" ").toLocaleLowerCase("es");

        const avatar = document.createElement("div");
        avatar.className = "crm-contact__avatar";
        avatar.setAttribute("aria-hidden", "true");
        avatar.textContent = initials(contact.firstName, contact.lastName);

        const identity = document.createElement("div");
        identity.className = "crm-contact__identity";
        const nameRow = document.createElement("div");
        nameRow.className = "crm-contact__name";
        const name = document.createElement("strong");
        const nameLink = document.createElement("a");
        nameLink.className = "crm-record-link";
        nameLink.href = detailUrl(urls.contactDetail, contact.id) || "#";
        nameLink.textContent = fullName;
        name.append(nameLink);
        const lifecycleBadge = document.createElement("span");
        lifecycleBadge.className = "crm-record-badge crm-record-badge--lifecycle";
        lifecycleBadge.textContent = lifecycle;
        nameRow.append(name, lifecycleBadge);
        const context = document.createElement("span");
        context.textContent = [
            contact.jobTitle,
            contact.companyName,
            contact.companyLifecycleLabel
        ].filter(Boolean).join(" · ");
        identity.append(nameRow, context);

        const details = document.createElement("div");
        details.className = "crm-contact__details";
        if (contact.email) {
            const email = document.createElement("a");
            email.href = "mailto:" + contact.email;
            email.textContent = contact.email;
            details.append(email);
        }
        if (contact.phone) {
            const phone = document.createElement("a");
            phone.href = "tel:" + contact.phone;
            phone.textContent = contact.phone;
            details.append(phone);
        }

        row.append(avatar, identity, details);
        return row;
    }

    function addContactToSelects(contact) {
        if (!contact.id) return;
        const fullName = contact.fullName
            || [contact.firstName, contact.lastName].filter(Boolean).join(" ")
            || "Contacto";

        const activitySelect = activityForm?.elements.namedItem("ContactId");
        if (activitySelect instanceof HTMLSelectElement) {
            const option = new Option(
                [fullName, contact.companyName].filter(Boolean).join(" · "),
                contact.id);
            option.dataset.companyId = contact.companyId || "";
            activitySelect.add(option);
        }

    }

    function contactPayload() {
        const elements = contactForm.elements;
        return {
            companyId: String(elements.namedItem("CompanyId")?.value || ""),
            firstName: String(elements.namedItem("FirstName")?.value || "").trim(),
            lastName: String(elements.namedItem("LastName")?.value || "").trim(),
            email: String(elements.namedItem("Email")?.value || "").trim(),
            phone: String(elements.namedItem("Phone")?.value || "").trim(),
            jobTitle: String(elements.namedItem("JobTitle")?.value || "").trim(),
            lifecycle: Number(elements.namedItem("Lifecycle")?.value || 0),
            isPrimary: Boolean(elements.namedItem("IsPrimary")?.checked),
            doNotEmail: Boolean(elements.namedItem("DoNotEmail")?.checked),
            doNotCall: Boolean(elements.namedItem("DoNotCall")?.checked)
        };
    }

    function validateContactChannel() {
        if (!contactForm) return false;
        const email = contactForm.elements.namedItem("Email");
        const phone = contactForm.elements.namedItem("Phone");
        const valid = Boolean(email?.value.trim() || phone?.value.trim());
        if (email instanceof HTMLInputElement) {
            email.setCustomValidity(valid ? "" : "Registra al menos un correo o un teléfono.");
        }
        return valid;
    }

    async function submitContact(event) {
        event.preventDefault();
        if (!contactForm || !urls.createContact) {
            showStatus(contactStatus, "La creación de contactos aún no está configurada.", "error");
            return;
        }

        validateContactChannel();
        if (!contactForm.reportValidity()) return;
        const button = $("[data-save-contact]", contactForm);
        const payload = contactPayload();
        const selectedCompanyOption = contactCompanySelect instanceof HTMLSelectElement
            ? contactCompanySelect.selectedOptions[0]
            : null;
        const companyLifecycleLabel = selectedCompanyOption?.dataset.companyLifecycleLabel || "";
        setBusy(button, true, "Creando…");
        showStatus(contactStatus, "");

        try {
            const response = await fetch(urls.createContact, {
                method: "POST",
                headers: requestHeaders(true),
                body: JSON.stringify(payload)
            });
            const result = await parseResponse(response);
            const record = result?.record || result?.Record || {};
            const id = record.id || record.Id || "";
            const contact = {
                ...payload,
                companyLifecycleLabel,
                ...record,
                id
            };
            const target = detailUrl(urls.contactDetail, id);
            if (!target) {
                throw new Error("El contacto fue creado, pero no recibimos su identificador para abrir la ficha.");
            }
            const list = $("[data-contact-list]");
            list?.prepend(buildContactRow(contact));
            addContactToSelects(contact);
            const empty = $("[data-contact-empty]");
            if (empty) {
                empty.hidden = true;
                const title = $("strong", empty);
                const description = $("span", empty);
                if (title) title.textContent = "No encontramos contactos";
                if (description) description.textContent = "Ajusta la búsqueda e inténtalo de nuevo.";
            }
            contactForm.reset();
            syncContactLifecycle();
            closeDrawer(contactDrawer, contactStatus);
            applySimpleFilter("[data-contact-search]", "[data-contact-row]", "[data-contact-empty]");
            activateCrmView("contactos", { history: "push", scroll: true });
            showStatus(globalStatus, result.message || "El contacto quedó creado.", "success");
            window.location.assign(target);
        } catch (error) {
            showStatus(contactStatus, error.message, "error");
        } finally {
            setBusy(button, false);
        }
    }

    function dealMode() {
        return dealForm?.elements.namedItem("DealCreationMode")?.value || "manual";
    }

    function syncDealContacts(companyId) {
        if (!(dealContactSelect instanceof HTMLSelectElement)) return;
        let selectedIsAvailable = !dealContactSelect.value;
        Array.from(dealContactSelect.options).forEach(option => {
            if (!option.value) {
                option.hidden = false;
                option.disabled = false;
                return;
            }
            const belongsToCompany = !companyId || !option.dataset.companyId
                || option.dataset.companyId.toLocaleLowerCase("es") === companyId.toLocaleLowerCase("es");
            option.hidden = !belongsToCompany;
            option.disabled = !belongsToCompany;
            if (option.selected && belongsToCompany) selectedIsAvailable = true;
        });
        if (!selectedIsAvailable) dealContactSelect.value = "";
    }

    function syncDealMode() {
        if (!dealForm) return;
        const calculatorMode = dealMode() === "calculator";
        if (dealManualFields) dealManualFields.hidden = calculatorMode;
        if (dealCalculatorPanel) dealCalculatorPanel.hidden = !calculatorMode;
        $$("input, select, textarea", dealManualFields).forEach(field => {
            field.disabled = calculatorMode;
        });
        const button = $("[data-save-deal]", dealForm);
        if (button) button.textContent = calculatorMode ? "Ir a la calculadora" : "Crear negocio";
    }

    function resetDealForm() {
        if (!dealForm) return;
        dealForm.reset();
        const companyId = String(dealForm.elements.namedItem("CompanyId")?.value || "");
        if (dealCompanyResults) {
            dealCompanyResults.replaceChildren();
            dealCompanyResults.hidden = true;
        }
        syncDealContacts(companyId);
        syncDealMode();
        showStatus(dealStatus, "");
    }

    function openDealDrawer(trigger) {
        openDrawer(
            dealDrawer,
            dealPanel,
            dealStatus,
            trigger,
            "[data-deal-first-focus]",
            resetDealForm);
    }

    function selectDealCompany(company) {
        if (!dealForm || !company?.id) return;
        const idInput = dealForm.elements.namedItem("CompanyId");
        const nameInput = dealForm.elements.namedItem("CompanyName");
        if (idInput) idInput.value = company.id;
        if (nameInput) nameInput.value = company.name || "";
        if (dealCompanySearch instanceof HTMLInputElement) {
            dealCompanySearch.value = company.name || "";
            dealCompanySearch.setCustomValidity("");
        }
        syncDealContacts(company.id);
        if (dealCompanyResults) {
            dealCompanyResults.replaceChildren();
            dealCompanyResults.hidden = true;
        }
    }

    function renderDealCompanyResults(companies) {
        if (!dealCompanyResults) return;
        dealCompanyResults.replaceChildren();
        if (!companies.length) {
            const empty = document.createElement("p");
            empty.className = "crm-company-picker__empty";
            empty.textContent = "No encontramos empresas con ese criterio.";
            dealCompanyResults.append(empty);
            dealCompanyResults.hidden = false;
            return;
        }

        companies.forEach(company => {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "crm-company-picker__option";
            button.setAttribute("role", "option");
            const name = document.createElement("strong");
            name.textContent = company.name || "Empresa";
            const context = document.createElement("span");
            context.textContent = [
                company.taxId ? `NIT ${company.taxId}` : "",
                company.lifecycleLabel || "",
                company.city || ""
            ].filter(Boolean).join(" · ");
            button.append(name, context);
            button.addEventListener("click", () => selectDealCompany(company));
            dealCompanyResults.append(button);
        });
        dealCompanyResults.hidden = false;
    }

    async function searchDealCompanies() {
        if (!(dealCompanySearch instanceof HTMLInputElement) || !urls.searchCompanies) return;
        const query = dealCompanySearch.value.trim();
        const idInput = dealForm?.elements.namedItem("CompanyId");
        const nameInput = dealForm?.elements.namedItem("CompanyName");
        if (idInput) idInput.value = "";
        if (nameInput) nameInput.value = "";
        syncDealContacts("");

        if (query.length < 2) {
            if (dealCompanyResults) dealCompanyResults.hidden = true;
            return;
        }

        const requestId = ++dealCompanySearchRequest;
        try {
            const target = new URL(urls.searchCompanies, window.location.origin);
            target.searchParams.set("q", query);
            target.searchParams.set("top", "12");
            if (viewAsOwnerId) target.searchParams.set("ViewAsOwnerId", viewAsOwnerId);
            const response = await fetch(target, {
                headers: { Accept: "application/json" }
            });
            const result = await parseResponse(response);
            if (requestId !== dealCompanySearchRequest) return;
            const companies = Array.isArray(result)
                ? result
                : (result?.items || result?.records || []);
            renderDealCompanyResults(companies);
        } catch (error) {
            if (requestId !== dealCompanySearchRequest) return;
            showStatus(dealStatus, error.message, "error");
        }
    }

    function validateDealCompany() {
        if (!dealForm) return false;
        const companyId = String(dealForm.elements.namedItem("CompanyId")?.value || "").trim();
        if (dealCompanySearch instanceof HTMLInputElement) {
            dealCompanySearch.setCustomValidity(companyId ? "" : "Selecciona una empresa de los resultados.");
        }
        return Boolean(companyId);
    }

    function calculatorDealUrl() {
        if (!dealForm || !urls.calculator) return "";
        const elements = dealForm.elements;
        const companyId = String(elements.namedItem("CompanyId")?.value || "");
        const companyName = String(elements.namedItem("CompanyName")?.value || "");
        const contactId = String(elements.namedItem("PrimaryContactId")?.value || "");
        const contactSelect = elements.namedItem("PrimaryContactId");
        const contactNameInput = elements.namedItem("PrimaryContactName");
        const contactName = String(contactNameInput?.value
            || (contactSelect instanceof HTMLSelectElement
                ? contactSelect.selectedOptions[0]?.textContent
                : "")
            || "").trim();
        const target = new URL(urls.calculator, window.location.origin);
        target.searchParams.set("newCrmOpportunity", "1");
        target.searchParams.set("crmCompanyId", companyId);
        target.searchParams.set("crmCompanyName", companyName);
        if (contactId) target.searchParams.set("crmContactId", contactId);
        if (contactName) target.searchParams.set("crmContactName", contactName);
        if (viewAsOwnerId) target.searchParams.set("ViewAsOwnerId", viewAsOwnerId);
        return `${target.pathname}${target.search}`;
    }

    function dealPayload() {
        const elements = dealForm.elements;
        return {
            CompanyId: String(elements.namedItem("CompanyId")?.value || ""),
            PrimaryContactId: String(elements.namedItem("PrimaryContactId")?.value || "") || null,
            Name: String(elements.namedItem("Name")?.value || "").trim(),
            EstimatedContractValue: Number(elements.namedItem("EstimatedContractValue")?.value || 0),
            EstimatedScore: Number(elements.namedItem("EstimatedScore")?.value || 0),
            Category: String(elements.namedItem("Category")?.value || "").trim(),
            BriefDescription: String(elements.namedItem("BriefDescription")?.value || "").trim(),
            ViewAsOwnerId: viewAsOwnerId || null
        };
    }

    async function submitDeal(event) {
        event.preventDefault();
        if (!dealForm) return;
        validateDealCompany();
        if (!dealForm.reportValidity()) return;

        if (dealMode() === "calculator") {
            const target = calculatorDealUrl();
            if (!target) {
                showStatus(dealStatus, "No fue posible abrir la calculadora.", "error");
                return;
            }
            window.location.assign(target);
            return;
        }

        if (!urls.createDeal) {
            showStatus(dealStatus, "La creación de negocios aún no está configurada.", "error");
            return;
        }

        const button = $("[data-save-deal]", dealForm);
        setBusy(button, true, "Creando…");
        showStatus(dealStatus, "");
        try {
            const response = await fetch(urls.createDeal, {
                method: "POST",
                headers: requestHeaders(true),
                body: JSON.stringify(dealPayload())
            });
            const result = await parseResponse(response);
            const record = result?.record || result?.Record || result || {};
            const id = record.id || record.Id || "";
            const target = detailUrl(urls.dealDetail, id);
            if (!target) {
                throw new Error("El negocio fue creado, pero no recibimos su identificador para abrir la ficha.");
            }
            closeDrawer(dealDrawer, dealStatus);
            window.location.assign(target);
        } catch (error) {
            showStatus(dealStatus, error.message, "error");
        } finally {
            setBusy(button, false);
            syncDealMode();
        }
    }

    function formatDateOnly(value) {
        if (!value) return "Sin fecha de cierre";
        const parts = String(value).slice(0, 10).split("-").map(Number);
        if (parts.length !== 3 || parts.some(Number.isNaN)) return "Sin fecha de cierre";
        return shortDate.format(new Date(parts[0], parts[1] - 1, parts[2]));
    }

    function formatDateTime(value) {
        if (!value) return "";
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? "" : shortDateTime.format(date);
    }

    function incrementTextNumber(element, increment = 1) {
        if (!element) return;
        element.textContent = String((Number(element.textContent) || 0) + increment);
    }

    function buildActivityRow(activity) {
        const row = document.createElement("article");
        row.className = "crm-activity-row";
        row.dataset.activityRow = "";
        row.dataset.searchText = [
            activity.typeDisplayLabel || activity.typeLabel,
            activity.subject,
            activity.relatedName
        ].filter(Boolean).join(" ").toLocaleLowerCase("es");

        const marker = document.createElement("div");
        marker.className = "crm-activity-row__marker";
        marker.setAttribute("aria-hidden", "true");

        const main = document.createElement("div");
        main.className = "crm-activity-row__main";
        const subject = document.createElement("strong");
        const subjectLink = document.createElement("a");
        subjectLink.className = "crm-record-link";
        subjectLink.href = detailUrl(urls.activityDetail, activity.id) || "#";
        subjectLink.textContent = activity.subject || "Actividad comercial";
        subject.append(subjectLink);
        const type = document.createElement("span");
        type.textContent = activity.typeDisplayLabel || activity.typeLabel || "Actividad";
        main.append(subject, type);

        const relation = document.createElement("div");
        relation.className = "crm-activity-row__owner";
        const relationLabel = document.createElement("span");
        relationLabel.textContent = "Asociado a";
        const relatedName = document.createElement("strong");
        relatedName.textContent = activity.relatedName || "Sin asociación";
        relation.append(relationLabel, relatedName);

        const timing = document.createElement("div");
        timing.className = "crm-activity-row__date";
        const status = document.createElement("span");
        status.textContent = activity.statusLabel || "Planeada";
        const time = document.createElement("time");
        if (activity.occurredAt) time.dateTime = activity.occurredAt;
        time.textContent = formatDateTime(activity.occurredAt) || "Sin fecha";
        timing.append(status, time);
        row.append(marker, main, relation, timing);
        return row;
    }

    function prependActivity(result, formData) {
        const list = $("[data-activity-list]");
        if (!list) return;

        const company = selectedLabel(activityForm, formData, "CompanyId");
        const contact = selectedLabel(activityForm, formData, "ContactId");
        const deal = selectedLabel(activityForm, formData, "DealId");
        const typeLabel = selectedLabel(activityForm, formData, "Type", "Actividad");
        const meetingTypeLabel = selectedLabel(activityForm, formData, "MeetingType");
        const statusLabel = selectedLabel(activityForm, formData, "Status", "Planeada");
        const record = result?.record || result?.Record || {};
        const activity = {
            id: record.id || record.Id || "",
            typeValue: record.typeValue ?? Number(formData.get("Type")),
            typeLabel: record.typeLabel || typeLabel,
            meetingTypeValue: record.meetingTypeValue ?? (Number(formData.get("MeetingType")) || null),
            meetingTypeLabel: record.meetingTypeLabel || meetingTypeLabel,
            typeDisplayLabel: record.typeDisplayLabel
                || [record.typeLabel || typeLabel, record.meetingTypeLabel || meetingTypeLabel]
                    .filter(Boolean)
                    .join(" · "),
            statusValue: record.statusValue ?? Number(formData.get("Status")),
            statusLabel: record.statusLabel || statusLabel,
            subject: record.subject || String(formData.get("Subject") || ""),
            relatedName: record.dealName || record.contactName || record.companyName
                || [deal, contact, company].find(Boolean)
                || "Sin asociación",
            occurredAt: record.completedAtUtc || record.plannedAtUtc
                || String(formData.get("CompletedAtUtc") || formData.get("PlannedAtUtc") || "")
        };

        list.prepend(buildActivityRow(activity));
        const empty = $("[data-activity-empty]");
        if (empty) empty.hidden = true;

        if (String(activity.statusValue) === plannedActivityStatus) {
            incrementTextNumber($("[data-planned-activities-count]"));
        }
        if (String(activity.statusValue) === completedActivityStatus) {
            incrementTextNumber($('[data-performance-type="' + CSS.escape(String(activity.typeValue)) + '"]'));
        }
    }

    function validateActivityRelation() {
        if (!activityForm) return false;
        const company = activityForm.elements.namedItem("CompanyId");
        const contact = activityForm.elements.namedItem("ContactId");
        const deal = activityForm.elements.namedItem("DealId");
        const valid = Boolean(company?.value || contact?.value || deal?.value);
        if (company instanceof HTMLSelectElement) {
            company.setCustomValidity(valid ? "" : "Relaciona la actividad con una empresa, un contacto o un negocio.");
        }
        return valid;
    }

    async function submitActivity(event) {
        event.preventDefault();
        if (!urls.createActivity) {
            showStatus(activityStatus, "El registro de actividades aún no está configurado.", "error");
            return;
        }

        validateActivityRelation();
        if (!activityForm.reportValidity()) return;
        const button = $("[data-save-activity]");
        const formData = new FormData(activityForm);
        const plannedAt = String(formData.get("PlannedAtUtc") || "");
        const completedAt = String(formData.get("CompletedAtUtc") || "");
        if (plannedAt) formData.set("PlannedAtUtc", bogotaLocalToIso(plannedAt));
        if (completedAt) formData.set("CompletedAtUtc", bogotaLocalToIso(completedAt));
        setBusy(button, true);
        showStatus(activityStatus, "");

        try {
            const response = await fetch(urls.createActivity, {
                method: "POST",
                headers: requestHeaders(),
                body: formData
            });
            const result = await parseResponse(response);
            const record = result?.record || result?.Record || {};
            const id = record.id || record.Id || "";
            const target = detailUrl(urls.activityDetail, id);
            if (!target) {
                throw new Error("La actividad fue creada, pero no recibimos su identificador para abrir la ficha.");
            }
            prependActivity(result, formData);
            activityForm.reset();
            syncActivityType();
            syncActivityStatus();
            closeDrawer(activityDrawer, activityStatus);
            activateCrmView("actividades", { history: "push", scroll: true });
            showStatus(globalStatus, result.message || "La actividad quedó registrada.", "success");
            window.location.assign(target);
        } catch (error) {
            showStatus(activityStatus, error.message, "error");
        } finally {
            setBusy(button, false);
        }
    }

    [...crmNavLinks, ...crmViewTargets].forEach(link => {
        link.addEventListener("click", handleCrmNavigation);
    });
    window.addEventListener("popstate", () => {
        activateCrmView(crmViewFromLocation(), { scroll: true });
    });
    window.addEventListener("hashchange", () => {
        activateCrmView(crmViewFromLocation(), { scroll: true });
    });

    $$("[data-open-company]").forEach(button => {
        button.addEventListener("click", () => openCompanyDrawer(button));
    });
    $$("[data-close-company]").forEach(button => {
        button.addEventListener("click", () => closeDrawer(companyDrawer, companyStatus));
    });
    companyDrawer?.addEventListener("keydown", event => {
        trapDrawerFocus(event, companyDrawer, companyPanel, companyStatus);
    });
    companyForm?.addEventListener("submit", submitCompany);

    $$("[data-open-contact]").forEach(button => {
        button.addEventListener("click", () => openContactDrawer(button));
    });
    $$("[data-close-contact]").forEach(button => {
        button.addEventListener("click", () => closeDrawer(contactDrawer, contactStatus));
    });
    contactDrawer?.addEventListener("keydown", event => {
        trapDrawerFocus(event, contactDrawer, contactPanel, contactStatus);
    });
    contactForm?.addEventListener("submit", submitContact);
    contactCompanySelect?.addEventListener("change", syncContactLifecycle);
    $("[data-contact-email]")?.addEventListener("input", validateContactChannel);
    $("[data-contact-phone]")?.addEventListener("input", validateContactChannel);

    $$("[data-open-deal]").forEach(button => {
        button.addEventListener("click", () => openDealDrawer(button));
    });
    $$("[data-close-deal]").forEach(button => {
        button.addEventListener("click", () => closeDrawer(dealDrawer, dealStatus));
    });
    dealDrawer?.addEventListener("keydown", event => {
        trapDrawerFocus(event, dealDrawer, dealPanel, dealStatus);
    });
    dealForm?.addEventListener("submit", submitDeal);
    $$("[data-deal-mode]", dealForm).forEach(input => {
        input.addEventListener("change", syncDealMode);
    });
    dealCompanySearch?.addEventListener("input", debounce(searchDealCompanies, 240));
    dealCompanySearch?.addEventListener("blur", () => {
        window.setTimeout(() => {
            if (dealCompanyResults && !dealCompanyResults.contains(document.activeElement)) {
                dealCompanyResults.hidden = true;
            }
        }, 120);
    });

    $$("[data-open-activity]").forEach(button => {
        button.addEventListener("click", () => openActivityDrawer(button));
    });
    $$("[data-close-activity]").forEach(button => {
        button.addEventListener("click", () => closeDrawer(activityDrawer, activityStatus));
    });
    activityDrawer?.addEventListener("keydown", event => {
        trapDrawerFocus(event, activityDrawer, activityPanel, activityStatus);
    });
    activityForm?.addEventListener("submit", submitActivity);
    activityTypeSelect?.addEventListener("change", syncActivityType);
    activityStatusSelect?.addEventListener("change", syncActivityStatus);
    ["CompanyId", "ContactId", "DealId"].forEach(name => {
        activityForm?.elements.namedItem(name)?.addEventListener("change", validateActivityRelation);
    });

    lossForm?.addEventListener("submit", submitLostDeal);
    lossReason?.addEventListener("input", () => lossReason.setCustomValidity(""));
    $$("[data-cancel-loss]").forEach(button => {
        button.addEventListener("click", () => closeLossDialog());
    });
    lossDialog?.addEventListener("cancel", event => {
        event.preventDefault();
        closeLossDialog();
    });
    lossDialog?.addEventListener("click", event => {
        if (event.target === lossDialog) closeLossDialog();
    });

    app.addEventListener("change", event => {
        const stageSelect = event.target.closest("[data-deal-stage]");
        if (!stageSelect) return;
        if (stageSelect.value === lostStageValue) {
            openLossDialog(stageSelect);
        } else {
            updateDealStage(stageSelect);
        }
    });

    $("[data-pipeline-search]")?.addEventListener("input", debounce(applyPipelineFilter));
    $$("[data-deal-kind-filter]").forEach(button => {
        button.addEventListener("click", () => setDealKindFilter(button));
    });
    $("[data-company-search]")?.addEventListener("input", debounce(applyCompanyFilter));
    $("[data-company-lifecycle-filter]")?.addEventListener("change", applyCompanyFilter);
    bindFilter("[data-contact-search]", "[data-contact-row]", "[data-contact-empty]");
    bindFilter("[data-activity-search]", "[data-activity-row]", "[data-activity-empty]");

    $$("[data-pipeline-stage]").forEach(updateStageSummary);
    const activityEmpty = $("[data-activity-empty]");
    if (activityEmpty) activityEmpty.hidden = $$("[data-activity-row]").length > 0;
    const requestedView = crmViewFromLocation();
    const hasRecognizedHash = !window.location.hash
        || window.location.hash.toLocaleLowerCase("es") === crmViewHash(requestedView);
    activateCrmView(requestedView, {
        history: hasRecognizedHash ? "none" : "replace"
    });
    initializeScopeSelector();
    applyCompanyFilter();
    syncContactLifecycle();
    syncDealMode();
    syncActivityType();
    syncActivityStatus();
})();
