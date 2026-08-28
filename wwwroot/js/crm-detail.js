(() => {
    "use strict";

    const app = document.getElementById("crmDetailApp");
    if (!app) return;

    const $ = (selector, root = app) => root ? root.querySelector(selector) : null;
    const $$ = (selector, root = app) => root ? Array.from(root.querySelectorAll(selector)) : [];
    const token = $('input[name="__RequestVerificationToken"]')?.value || "";
    const workspace = $(".crm-workspace");
    const globalStatus = $("[data-detail-global-status]");
    const urls = {
        createContact: app.dataset.createContactUrl || "",
        createActivity: app.dataset.createActivityUrl || "",
        createDeal: app.dataset.createDealUrl || "",
        updateOwner: app.dataset.updateOwnerUrl || "",
        searchCompanies: app.dataset.searchCompaniesUrl || "",
        calculator: app.dataset.calculatorUrl || "",
        crmIndex: app.dataset.crmIndexUrl || "/Crm",
        contactDetail: app.dataset.contactDetailUrlTemplate || "",
        dealDetail: app.dataset.dealDetailUrlTemplate || "",
        activityDetail: app.dataset.activityDetailUrlTemplate || ""
    };
    const viewAsOwnerId = app.dataset.viewAsOwnerId || "";
    const plannedActivityStatus = app.dataset.plannedActivityStatus || "";
    const completedActivityStatus = app.dataset.completedActivityStatus || "";
    const meetingActivityType = app.dataset.meetingActivityType || "";

    const contactDrawer = $("[data-detail-contact-drawer]");
    const contactPanel = $(".crm-drawer__panel", contactDrawer);
    const contactForm = $("[data-detail-contact-form]");
    const contactStatus = $("[data-detail-contact-status]");
    const activityDrawer = $("[data-detail-activity-drawer]");
    const activityPanel = $(".crm-drawer__panel", activityDrawer);
    const activityForm = $("[data-detail-activity-form]");
    const activityStatus = $("[data-detail-activity-status]");
    const activityTypeSelect = $("[data-detail-activity-type-select]");
    const activityMeetingTypeField = $("[data-detail-activity-meeting-type-field]");
    const activityMeetingTypeSelect = $("[data-detail-activity-meeting-type-select]");
    const activityStatusSelect = $("[data-detail-activity-status-select]");
    const activityPlannedInput = $("[data-detail-activity-planned]");
    const activityCompletedField = $("[data-detail-activity-completed-field]");
    const activityCompletedInput = $("[data-detail-activity-completed]");
    const activityResultField = $("[data-detail-activity-result-field]");
    const activityResultInput = $("[data-detail-activity-result]");
    const dealDrawer = $("[data-deal-drawer]");
    const dealPanel = $(".crm-drawer__panel", dealDrawer);
    const dealForm = $("[data-deal-form]");
    const dealStatus = $("[data-deal-status]");
    const dealManualFields = $("[data-deal-manual-fields]");
    const dealCalculatorPanel = $("[data-deal-calculator-panel]");
    const dealCompanySearch = $("[data-deal-company-search]");
    const dealCompanyResults = $("[data-deal-company-results]");
    const dealContactSelect = $("[data-deal-contact-select]");

    const bogotaInputDateTime = new Intl.DateTimeFormat("en-CA", {
        timeZone: "America/Bogota",
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
        hourCycle: "h23"
    });

    let openDrawerState = null;
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
        const body = contentType.includes("json")
            ? await response.json()
            : { message: await response.text() };

        if (!response.ok) {
            const validationMessages = body?.errors && typeof body.errors === "object"
                ? Object.values(body.errors)
                    .flatMap(value => Array.isArray(value) ? value : [value])
                    .filter(value => typeof value === "string" && value.trim())
                : [];
            const messages = [body?.detail, body?.message, ...validationMessages]
                .filter(value => typeof value === "string" && value.trim());
            throw new Error([...new Set(messages)].join(" ") || `La solicitud falló (${response.status}).`);
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
            ["ContactPage", "DealPage", "ActivityPage", "HistoryPage"].forEach(name => {
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
            control.setAttribute("aria-busy", "true");
        } else {
            control.textContent = control.dataset.originalLabel || control.textContent;
            control.disabled = false;
            control.removeAttribute("aria-busy");
        }
    }

    function ownerReturnUrl(result) {
        const provided = result?.redirectUrl || result?.RedirectUrl;
        if (provided) return withViewAs(provided);
        const target = new URL(urls.crmIndex || "/Crm", window.location.origin);
        const sectionByType = {
            company: "empresas",
            contact: "contactos",
            deal: "negocios",
            activity: "actividades"
        };
        target.hash = `crm-${sectionByType[app.dataset.recordType] || "resumen"}`;
        return withViewAs(`${target.pathname}${target.search}${target.hash}`);
    }

    async function submitOwner(event) {
        event.preventDefault();
        const form = event.currentTarget;
        const select = form.elements.namedItem("NewOwnerSystemUserId");
        const status = $("[data-owner-status]", form);
        const button = $("[data-owner-save]", form);
        if (!(select instanceof HTMLSelectElement) || !select.value) {
            select?.setCustomValidity("Selecciona un propietario.");
            form.reportValidity();
            return;
        }
        if (!urls.updateOwner) {
            showStatus(status, "El cambio de propietario aún no está configurado.", "error");
            return;
        }

        select.setCustomValidity("");
        setBusy(button, true);
        showStatus(status, "");
        try {
            const response = await fetch(urls.updateOwner, {
                method: "POST",
                headers: requestHeaders(true),
                body: JSON.stringify({
                    ObjectType: form.dataset.objectType || "",
                    RecordId: form.dataset.recordId || "",
                    NewOwnerSystemUserId: select.value,
                    ViewAsOwnerId: viewAsOwnerId || null
                })
            });
            const result = await parseResponse(response);
            const selectedText = select.selectedOptions[0]?.textContent?.split(" · ")[0]?.trim();
            const currentName = $("[data-owner-current-name]", form.closest("[data-owner-editor]"));
            if (currentName && selectedText) currentName.textContent = selectedText;
            showStatus(status, result?.message || "Propietario actualizado.", "success");
            window.setTimeout(() => window.location.assign(ownerReturnUrl(result)), 500);
        } catch (error) {
            showStatus(status, error.message, "error");
        } finally {
            setBusy(button, false);
        }
    }

    function focusableElements(container) {
        return $$(
            'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
            container
        ).filter(element => !element.hidden && element.getAttribute("aria-hidden") !== "true");
    }

    function debounce(callback, wait = 280) {
        let timer;
        return (...args) => {
            window.clearTimeout(timer);
            timer = window.setTimeout(() => callback(...args), wait);
        };
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
        if (workspace) workspace.inert = true;
        document.body.classList.add("crm-drawer-open");
        window.requestAnimationFrame(() => $(firstFocusSelector, drawer)?.focus());
    }

    function closeDrawer(drawer, status) {
        if (!drawer) return;
        const trigger = openDrawerState?.drawer === drawer ? openDrawerState.trigger : null;
        drawer.hidden = true;
        showStatus(status, "");
        if (openDrawerState?.drawer === drawer) openDrawerState = null;
        if (!openDrawerState) {
            if (workspace) workspace.inert = false;
            document.body.classList.remove("crm-drawer-open");
        }
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

    function validateContactChannel() {
        if (!contactForm) return;
        const email = contactForm.elements.namedItem("Email");
        const phone = contactForm.elements.namedItem("Phone");
        const valid = Boolean(String(email?.value || "").trim() || String(phone?.value || "").trim());
        if (email instanceof HTMLInputElement) {
            email.setCustomValidity(valid ? "" : "Registra al menos un correo o un teléfono.");
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

    async function submitContact(event) {
        event.preventDefault();
        if (!contactForm || !urls.createContact) {
            showStatus(contactStatus, "La creación de contactos no está configurada.", "error");
            return;
        }

        validateContactChannel();
        if (!contactForm.reportValidity()) return;
        const button = $("[data-detail-save-contact]", contactForm);
        setBusy(button, true, "Creando…");
        showStatus(contactStatus, "");

        try {
            const response = await fetch(urls.createContact, {
                method: "POST",
                headers: requestHeaders(true),
                body: JSON.stringify(contactPayload())
            });
            const result = await parseResponse(response);
            const record = result?.record || result?.Record || {};
            const id = record.id || record.Id || "";
            const target = detailUrl(urls.contactDetail, id);
            if (!target) throw new Error("El contacto fue creado, pero no recibimos su identificador para abrir la ficha.");
            closeDrawer(contactDrawer, contactStatus);
            window.location.assign(target);
        } catch (error) {
            showStatus(contactStatus, error.message, "error");
        } finally {
            setBusy(button, false);
        }
    }

    async function submitActivity(event) {
        event.preventDefault();
        if (!activityForm || !urls.createActivity) {
            showStatus(activityStatus, "El registro de actividades no está configurado.", "error");
            return;
        }
        if (!activityForm.reportValidity()) return;

        const button = $("[data-detail-save-activity]", activityForm);
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
            if (!target) throw new Error("La actividad fue creada, pero no recibimos su identificador para abrir la ficha.");
            closeDrawer(activityDrawer, activityStatus);
            window.location.assign(target);
        } catch (error) {
            showStatus(activityStatus, error.message, "error");
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
            const response = await fetch(target, { headers: { Accept: "application/json" } });
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

    $$("[data-detail-open-contact]").forEach(button => {
        button.addEventListener("click", () => openDrawer(
            contactDrawer,
            contactPanel,
            contactStatus,
            button,
            "[data-detail-contact-first-focus]",
            validateContactChannel));
    });
    $$("[data-detail-close-contact]").forEach(button => {
        button.addEventListener("click", () => closeDrawer(contactDrawer, contactStatus));
    });
    contactDrawer?.addEventListener("keydown", event => {
        trapDrawerFocus(event, contactDrawer, contactPanel, contactStatus);
    });
    contactForm?.addEventListener("submit", submitContact);
    contactForm?.elements.namedItem("Email")?.addEventListener("input", validateContactChannel);
    contactForm?.elements.namedItem("Phone")?.addEventListener("input", validateContactChannel);

    $$("[data-detail-open-activity]").forEach(button => {
        button.addEventListener("click", () => openDrawer(
            activityDrawer,
            activityPanel,
            activityStatus,
            button,
            "[data-detail-activity-first-focus]",
            () => {
                setActivityDefaultDate();
                syncActivityType();
                syncActivityStatus();
            }));
    });
    $$("[data-detail-close-activity]").forEach(button => {
        button.addEventListener("click", () => closeDrawer(activityDrawer, activityStatus));
    });
    activityDrawer?.addEventListener("keydown", event => {
        trapDrawerFocus(event, activityDrawer, activityPanel, activityStatus);
    });
    activityStatusSelect?.addEventListener("change", syncActivityStatus);
    activityTypeSelect?.addEventListener("change", syncActivityType);
    activityForm?.addEventListener("submit", submitActivity);

    $$("[data-detail-open-deal]").forEach(button => {
        button.addEventListener("click", () => openDrawer(
            dealDrawer,
            dealPanel,
            dealStatus,
            button,
            "[data-deal-first-focus]",
            resetDealForm));
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

    $$("[data-owner-form]").forEach(form => {
        form.addEventListener("submit", submitOwner);
    });
    $$("[data-owner-cancel]").forEach(button => {
        button.addEventListener("click", () => {
            button.closest("[data-owner-editor]")?.removeAttribute("open");
        });
    });
    $$("[data-owner-editor]").forEach(editor => {
        editor.addEventListener("toggle", () => {
            if (!editor.open) return;
            $$("[data-owner-editor]").forEach(other => {
                if (other !== editor) other.removeAttribute("open");
            });
        });
    });

    initializeScopeSelector();
    syncActivityType();
    syncActivityStatus();
    syncDealMode();
    showStatus(globalStatus, "");
})();
