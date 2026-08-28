(() => {
    "use strict";
    const app = document.getElementById("contractsApp");
    if (!app) return;
    const $ = (selector, root = app) => root.querySelector(selector);
    const $$ = (selector, root = app) => Array.from(root.querySelectorAll(selector));
    const token = $('input[name="__RequestVerificationToken"]')?.value || "";
    const today = new Date().toLocaleDateString("en-CA", { timeZone: "America/Bogota" });
    const urls = {
        clientSearch: app.dataset.clientSearchUrl,
        analyzeRut: app.dataset.analyzeRutUrl,
        analyzeOffer: app.dataset.analyzeOfferUrl,
        create: app.dataset.createUrl,
        createOrder: app.dataset.createOrderUrl,
        uploadSigned: app.dataset.uploadSignedUrl,
        uploadOrderSigned: app.dataset.uploadOrderSignedUrl,
        generateAct: app.dataset.generateActUrl
    };
    const contractModal = $("[data-contract-modal]");
    const orderModal = $("[data-order-modal]");
    const contractForm = $("[data-contract-form]");
    const orderForm = $("[data-order-form]");
    const modalStatus = $("[data-modal-status]");
    const orderStatus = $("[data-order-status]");
    const globalStatus = $("[data-global-status]");
    const clientInput = $("[data-client-name]");
    const clientIdInput = $("[data-client-id]");
    const clientOptions = $("[data-client-options]");
    const rutFileInput = $("[data-rut-file]");
    const offerFileInput = $("[data-offer-file]");
    const equipmentRows = $("[data-equipment-rows]");
    const valueRows = $("[data-value-rows]");
    const orderEquipmentRows = $("[data-order-equipment-rows]");
    let currentStep = 1;
    let clientLookup = [];
    let signedTargetId = "";
    let orderSignedTargetId = "";
    let rutAnalyzed = false;
    let offerAnalyzed = false;
    let rutMetadata = {};
    let offerMetadata = {};

    function showStatus(element, message, type = "") {
        if (!element) return;
        element.textContent = message || "";
        element.classList.toggle("is-error", type === "error");
        element.classList.toggle("is-success", type === "success");
        element.hidden = !message;
    }
    async function parseResponse(response) {
        const type = response.headers.get("content-type") || "";
        const body = type.includes("json") ? await response.json() : { message: await response.text() };
        if (!response.ok) {
            const message = [body.message, body.detail].filter(Boolean).join(" ");
            throw new Error(message || "La solicitud falló (" + response.status + ").");
        }
        return body;
    }
    function requestHeaders(json = false) {
        const headers = {};
        if (token) headers.RequestVerificationToken = token;
        if (json) headers["Content-Type"] = "application/json";
        return headers;
    }
    function setBusy(button, busy, label = "Procesando…") {
        if (!button) return;
        if (busy) {
            button.dataset.originalLabel = button.textContent;
            button.textContent = label;
            button.disabled = true;
        } else {
            button.textContent = button.dataset.originalLabel || button.textContent;
            button.disabled = false;
        }
    }
    function openModal(modal) {
        modal.hidden = false;
        document.body.classList.add("contracts-modal-open");
    }
    function closeModal(modal) {
        modal.hidden = true;
        if (contractModal.hidden && orderModal.hidden) document.body.classList.remove("contracts-modal-open");
    }
    function updateStep(step) {
        currentStep = Math.max(1, Math.min(4, step));
        $$("[data-step]", contractModal).forEach(section => section.hidden = Number(section.dataset.step) !== currentStep);
        $$("[data-step-tab]", contractModal).forEach(tab => {
            const number = Number(tab.dataset.stepTab);
            tab.classList.toggle("is-current", number === currentStep);
            tab.classList.toggle("is-complete", number < currentStep);
        });
        $("[data-prev-step]", contractModal).hidden = currentStep === 1;
        $("[data-next-step]", contractModal).hidden = currentStep === 4;
        $("[data-create-contract]", contractModal).hidden = currentStep !== 4;
        showStatus(modalStatus, "");
        if (currentStep === 4) renderReview();
    }
    function updateDocumentState(kind, message, mode) {
        const card = $("[data-upload-card=\"" + kind + "\"]");
        const state = $("[data-" + kind + "-state]");
        if (state) state.textContent = message;
        card?.classList.toggle("is-ready", mode === "ready");
        card?.classList.toggle("is-working", mode === "working");
    }
    function resetContractForm() {
        contractForm.reset();
        $('[name="contractType"][value="645260000"]', contractForm).checked = true;
        $("[data-field='contractDate']", contractForm).value = today;
        $("[data-field='signatureCity']", contractForm).value = "Bogotá D.C.";
        clientIdInput.value = "";
        clientLookup = [];
        clientOptions.innerHTML = "";
        rutAnalyzed = offerAnalyzed = false;
        rutMetadata = {};
        offerMetadata = {};
        equipmentRows.innerHTML = "";
        valueRows.innerHTML = "";
        addEquipmentRow();
        updateDocumentState("rut", "Sin analizar", "");
        updateDocumentState("offer", "Sin analizar", "");
        updateStep(1);
    }
    function debounce(fn, wait = 280) {
        let timer;
        return (...args) => {
            clearTimeout(timer);
            timer = setTimeout(() => fn(...args), wait);
        };
    }
    async function searchClients() {
        const query = clientInput.value.trim();
        clientIdInput.value = "";
        if (query.length < 2) {
            clientOptions.innerHTML = "";
            return;
        }
        try {
            const response = await fetch(urls.clientSearch + "?q=" + encodeURIComponent(query), { headers: requestHeaders() });
            clientLookup = await parseResponse(response);
            clientOptions.innerHTML = "";
            clientLookup.forEach(client => {
                const option = document.createElement("option");
                option.value = client.name;
                clientOptions.appendChild(option);
            });
            syncSelectedClient();
        } catch (error) {
            showStatus(modalStatus, error.message, "error");
        }
    }
    function syncSelectedClient() {
        const value = clientInput.value.trim();
        const match = clientLookup.find(item => item.name.localeCompare(value, undefined, { sensitivity: "accent" }) === 0);
        clientIdInput.value = match?.id || "";
    }
    function addTemplateRow(templateId, target, data = {}) {
        const fragment = document.getElementById(templateId).content.cloneNode(true);
        const row = fragment.querySelector("tr");
        $$("[data-col]", row).forEach(input => {
            const value = data[input.dataset.col];
            if (value !== undefined && value !== null) input.value = value;
        });
        target.appendChild(fragment);
    }
    function addEquipmentRow(data = {}, target = equipmentRows, templateId = "equipmentRowTemplate") {
        addTemplateRow(templateId, target, data);
    }
    function addValueRow(data = {}) {
        addTemplateRow("valueRowTemplate", valueRows, data);
    }
    function numeric(value, fallback = 0) {
        const parsed = Number(value);
        return Number.isFinite(parsed) ? parsed : fallback;
    }
    function collectRows(target, kind = "equipment") {
        return $$("tr", target).map(row => {
            const values = {};
            $$("[data-col]", row).forEach(input => values[input.dataset.col] = input.value.trim());
            if (kind === "value") return values;
            return {
                equipmentOrService: values.equipmentOrService || "",
                quantity: Math.max(1, numeric(values.quantity, 1)),
                brand: values.brand || "",
                model: values.model || "",
                colorMode: values.colorMode || "",
                includedPrints: Math.max(0, numeric(values.includedPrints)),
                includedScans: Math.max(0, numeric(values.includedScans)),
                monthlyFee: Math.max(0, numeric(values.monthlyFee)),
                additionalClickPrice: Math.max(0, numeric(values.additionalClickPrice)),
                vatPercent: 19,
                vatIncluded: false,
                notes: ""
            };
        }).filter(row => kind === "value" ? Object.values(row).some(Boolean) : row.equipmentOrService || row.model);
    }
    function writeMappedFields(selector, data) {
        $$(selector, contractForm).forEach(input => {
            const key = input.dataset.rut || input.dataset.offer;
            const value = data[key];
            if (value !== undefined && value !== null && typeof value !== "object") input.value = value;
        });
    }
    function readMappedFields(selector) {
        const result = {};
        $$(selector, contractForm).forEach(input => {
            const key = input.dataset.rut || input.dataset.offer;
            result[key] = input.type === "number" ? numeric(input.value) : input.value.trim();
        });
        return result;
    }
    async function analyzeDocument(kind) {
        const isRut = kind === "rut";
        const fileInput = isRut ? rutFileInput : offerFileInput;
        const button = isRut ? $("[data-analyze-rut]") : $("[data-analyze-offer]");
        const file = fileInput.files[0];
        if (!file) {
            showStatus(modalStatus, "Selecciona " + (isRut ? "el RUT" : "la oferta aprobada") + " antes de analizar.", "error");
            return;
        }
        const formData = new FormData();
        formData.append("file", file);
        setBusy(button, true, "Analizando con IA…");
        updateDocumentState(kind, "Azure OpenAI está leyendo el documento…", "working");
        showStatus(modalStatus, "");
        try {
            const response = await fetch(isRut ? urls.analyzeRut : urls.analyzeOffer, { method: "POST", headers: requestHeaders(), body: formData });
            const data = await parseResponse(response);
            const confidence = Math.round(numeric(data.confidence) * 100);
            if (isRut) {
                rutAnalyzed = true;
                rutMetadata = data;
                writeMappedFields("[data-rut]", data);
                if (!$("[data-offer='executionAddress']").value) $("[data-offer='executionAddress']").value = data.mainAddress || data.notificationAddress || "";
                updateDocumentState(kind, "Analizado · confianza " + confidence + "%", "ready");
            } else {
                offerAnalyzed = true;
                offerMetadata = data;
                writeMappedFields("[data-offer]", data);
                equipmentRows.innerHTML = "";
                (data.equipmentLines || []).forEach(line => addEquipmentRow(line));
                if (!equipmentRows.children.length) addEquipmentRow();
                valueRows.innerHTML = "";
                (data.valueAddedServices || []).forEach(addValueRow);
                $("[data-special-conditions]").value = (data.specialConditions || []).join("\n");
                updateDocumentState(kind, (data.equipmentLines || []).length + " líneas detectadas · confianza " + confidence + "%", "ready");
            }
        } catch (error) {
            if (isRut) rutAnalyzed = false; else offerAnalyzed = false;
            updateDocumentState(kind, "No se pudo analizar", "");
            showStatus(modalStatus, error.message, "error");
        } finally {
            setBusy(button, false);
        }
    }
    function getContractPayload() {
        const rut = { ...rutMetadata, ...readMappedFields("[data-rut]") };
        rut.taxResponsibilities = rutMetadata.taxResponsibilities || [];
        rut.economicActivities = rutMetadata.economicActivities || [];
        rut.sourceNotes = rutMetadata.sourceNotes || [];
        rut.confidence = numeric(rutMetadata.confidence);
        const offer = { ...offerMetadata, ...readMappedFields("[data-offer]") };
        offer.contractType = "Copiers";
        offer.currency = offerMetadata.currency || "COP";
        offer.startCondition = offerMetadata.startCondition || "Fecha efectiva del acta de entrega e instalación";
        offer.recommendedTitle = offerMetadata.recommendedTitle || "Contrato marco de arrendamiento de equipos de impresión";
        offer.equipmentLines = collectRows(equipmentRows);
        offer.valueAddedServices = collectRows(valueRows, "value");
        offer.specialConditions = $("[data-special-conditions]").value.split(/\r?\n/).map(value => value.trim()).filter(Boolean);
        offer.warnings = offerMetadata.warnings || [];
        offer.confidence = numeric(offerMetadata.confidence);
        return {
            clientId: clientIdInput.value,
            clientName: clientInput.value.trim(),
            contractTypeValue: 645260000,
            contractDate: $("[data-field='contractDate']").value,
            signatureCity: $("[data-field='signatureCity']").value.trim(),
            initialActNumber: 0,
            rut,
            offer
        };
    }
    function validateStep(step) {
        syncSelectedClient();
        if (step === 1) {
            if (!clientIdInput.value) return "Selecciona un cliente válido de la lista de Dataverse.";
            if (!$("[data-field='contractDate']").value) return "Indica la fecha del contrato.";
        }
        if (step === 2) {
            if (!rutFileInput.files[0] || !rutAnalyzed) return "Selecciona y analiza el RUT.";
            if (!offerFileInput.files[0] || !offerAnalyzed) return "Selecciona y analiza la oferta aprobada.";
        }
        if (step === 3) {
            const payload = getContractPayload();
            const required = [
                [payload.rut.legalName, "razón social"], [payload.rut.nit, "NIT"],
                [payload.rut.legalRepresentativeName, "representante legal"], [payload.rut.legalRepresentativeId, "identificación del representante"],
                [payload.rut.mainAddress, "dirección principal"], [payload.rut.city, "ciudad"],
                [payload.offer.executionAddress, "lugar de ejecución"]
            ];
            const missing = required.find(item => !String(item[0] || "").trim());
            if (missing) return "Completa el campo " + missing[1] + ".";
            if (!payload.offer.equipmentLines.length) return "Agrega al menos una línea de equipo o servicio.";
        }
        return "";
    }
    function escapeHtml(value) {
        return String(value ?? "").replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]);
    }
    function reviewCell(label, value) {
        return "<div><span>" + escapeHtml(label) + "</span><strong>" + escapeHtml(value) + "</strong></div>";
    }
    function renderReview() {
        const payload = getContractPayload();
        const monthly = payload.offer.equipmentLines.reduce((sum, line) => sum + numeric(line.monthlyFee) * numeric(line.quantity, 1), 0);
        const money = monthly.toLocaleString("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
        const nit = payload.rut.nit + (payload.rut.verificationDigit ? "-" + payload.rut.verificationDigit : "");
        $("[data-contract-review]").innerHTML =
            reviewCell("Cliente", payload.rut.legalName) + reviewCell("NIT", nit) +
            reviewCell("Representante legal", payload.rut.legalRepresentativeName) +
            reviewCell("Equipos / servicios", payload.offer.equipmentLines.length + " líneas") +
            reviewCell("Canon mensual estimado", money) +
            reviewCell("Valor agregado", payload.offer.valueAddedServices.length + " servicios") +
            reviewCell("Duración", payload.offer.durationMonths + " meses") +
            reviewCell("Lugar de ejecución", payload.offer.executionAddress) +
            reviewCell("Revisiones de IA", (payload.offer.warnings || []).length ? payload.offer.warnings.length + " observaciones" : "Sin alertas");
    }
    async function submitContract(event) {
        event.preventDefault();
        const validation = validateStep(3);
        if (validation) return showStatus(modalStatus, validation, "error");
        const button = $("[data-create-contract]");
        const formData = new FormData();
        formData.append("payloadJson", JSON.stringify(getContractPayload()));
        formData.append("rutFile", rutFileInput.files[0]);
        formData.append("offerFile", offerFileInput.files[0]);
        setBusy(button, true, "Generando documentos…");
        showStatus(modalStatus, "Reservando consecutivo y creando registros en Dataverse…");
        try {
            const result = await parseResponse(await fetch(urls.create, { method: "POST", headers: requestHeaders(), body: formData }));
            showStatus(modalStatus, result.message, "success");
            setTimeout(() => window.location.reload(), 1100);
        } catch (error) {
            showStatus(modalStatus, error.message, "error");
            setBusy(button, false);
        }
    }
    function openOrder(button) {
        orderForm.reset();
        $("[data-order-contract-id]").value = button.dataset.id;
        $("[data-order-contract-label]").textContent = "Contrato " + button.dataset.code;
        $("[data-order='creationDate']").value = today;
        $("[data-order='durationMonths']").value = 12;
        $("[data-order='executionAddress']").value = button.dataset.address || "";
        $("[data-order='object']").value = "Adición de equipos y servicios al contrato marco.";
        orderEquipmentRows.innerHTML = "";
        addEquipmentRow({}, orderEquipmentRows, "orderEquipmentRowTemplate");
        showStatus(orderStatus, "");
        openModal(orderModal);
    }
    async function submitOrder(event) {
        event.preventDefault();
        const fields = {};
        $$("[data-order]", orderForm).forEach(input => fields[input.dataset.order] = input.type === "number" || input.tagName === "SELECT" ? numeric(input.value) : input.value.trim());
        const payload = {
            contractId: $("[data-order-contract-id]").value,
            orderTypeValue: fields.orderTypeValue,
            creationDate: fields.creationDate,
            startDate: null,
            durationMonths: fields.durationMonths,
            executionAddress: fields.executionAddress,
            object: fields.object,
            equipmentLines: collectRows(orderEquipmentRows),
            valueAddedServices: [],
            specialConditions: $("[data-order-conditions]").value.split(/\r?\n/).map(value => value.trim()).filter(Boolean)
        };
        if (!payload.object || !payload.executionAddress || !payload.equipmentLines.length) return showStatus(orderStatus, "Completa el objeto, lugar de ejecución y al menos una línea.", "error");
        const button = orderForm.querySelector('[type="submit"]');
        setBusy(button, true, "Generando orden…");
        try {
            const result = await parseResponse(await fetch(urls.createOrder, { method: "POST", headers: requestHeaders(true), body: JSON.stringify(payload) }));
            showStatus(orderStatus, result.message, "success");
            setTimeout(() => window.location.reload(), 900);
        } catch (error) {
            showStatus(orderStatus, error.message, "error");
            setBusy(button, false);
        }
    }
    async function uploadSigned(kind, id, file) {
        const isOrder = kind === "order";
        const data = new FormData();
        data.append(isOrder ? "orderId" : "contractId", id);
        data.append("file", file);
        showStatus(globalStatus, "Cargando documento firmado…");
        try {
            const result = await parseResponse(await fetch(isOrder ? urls.uploadOrderSigned : urls.uploadSigned, { method: "POST", headers: requestHeaders(), body: data }));
            showStatus(globalStatus, result.message, "success");
            setTimeout(() => window.location.reload(), 800);
        } catch (error) {
            showStatus(globalStatus, error.message, "error");
        }
    }
    async function generateAct(button) {
        setBusy(button, true, "Generando…");
        showStatus(globalStatus, "Generando el acta con los datos del contrato y la orden…");
        try {
            const body = JSON.stringify({ contractId: button.dataset.contractId, orderId: button.dataset.orderId });
            const result = await parseResponse(await fetch(urls.generateAct, { method: "POST", headers: requestHeaders(true), body }));
            showStatus(globalStatus, result.message, "success");
            setTimeout(() => window.location.reload(), 850);
        } catch (error) {
            showStatus(globalStatus, error.message, "error");
            setBusy(button, false);
        }
    }

    $$("[data-open-contract]").forEach(button => button.addEventListener("click", () => { resetContractForm(); openModal(contractModal); }));
    $$("[data-close-modal]").forEach(button => button.addEventListener("click", () => closeModal(contractModal)));
    $$("[data-close-order]").forEach(button => button.addEventListener("click", () => closeModal(orderModal)));
    $("[data-next-step]")?.addEventListener("click", () => {
        const error = validateStep(currentStep);
        if (error) return showStatus(modalStatus, error, "error");
        updateStep(currentStep + 1);
    });
    $("[data-prev-step]")?.addEventListener("click", () => updateStep(currentStep - 1));
    $$("[data-step-tab]").forEach(tab => tab.addEventListener("click", () => {
        const target = Number(tab.dataset.stepTab);
        if (target < currentStep) updateStep(target);
    }));
    clientInput?.addEventListener("input", debounce(searchClients));
    clientInput?.addEventListener("change", syncSelectedClient);
    $("[data-analyze-rut]")?.addEventListener("click", () => analyzeDocument("rut"));
    $("[data-analyze-offer]")?.addEventListener("click", () => analyzeDocument("offer"));
    $("[data-add-equipment]")?.addEventListener("click", () => addEquipmentRow());
    $("[data-add-value]")?.addEventListener("click", () => addValueRow());
    $("[data-order-add-equipment]")?.addEventListener("click", () => addEquipmentRow({}, orderEquipmentRows, "orderEquipmentRowTemplate"));
    app.addEventListener("click", event => {
        const remove = event.target.closest("[data-remove-row]");
        if (remove) {
            const row = remove.closest("tr");
            const body = row.parentElement;
            row.remove();
            if (!body.children.length && body === equipmentRows) addEquipmentRow();
            if (!body.children.length && body === orderEquipmentRows) addEquipmentRow({}, orderEquipmentRows, "orderEquipmentRowTemplate");
            return;
        }
        const expand = event.target.closest("[data-expand-contract]");
        if (expand) {
            const detail = $("[data-contract-detail]", expand.closest("[data-contract-card]"));
            const open = expand.getAttribute("aria-expanded") !== "true";
            expand.setAttribute("aria-expanded", String(open));
            detail.hidden = !open;
            return;
        }
        const orderButton = event.target.closest("[data-open-order]");
        if (orderButton) return openOrder(orderButton);
        const signed = event.target.closest("[data-upload-signed]");
        if (signed) {
            signedTargetId = signed.dataset.id;
            $("[data-signed-file]").click();
            return;
        }
        const orderSigned = event.target.closest("[data-upload-order-signed]");
        if (orderSigned) {
            orderSignedTargetId = orderSigned.dataset.id;
            $("[data-order-signed-file]").click();
            return;
        }
        const act = event.target.closest("[data-generate-act]");
        if (act) generateAct(act);
    });
    $("[data-signed-file]")?.addEventListener("change", event => {
        const file = event.target.files[0];
        if (file && signedTargetId) uploadSigned("contract", signedTargetId, file);
        event.target.value = "";
    });
    $("[data-order-signed-file]")?.addEventListener("change", event => {
        const file = event.target.files[0];
        if (file && orderSignedTargetId) uploadSigned("order", orderSignedTargetId, file);
        event.target.value = "";
    });
    $("[data-contract-search]")?.addEventListener("input", event => {
        const query = event.target.value.trim().toLowerCase();
        let visible = 0;
        $$("[data-contract-card]").forEach(card => {
            const show = !query || (card.dataset.searchText || "").includes(query);
            card.hidden = !show;
            if (show) visible++;
        });
        const empty = $("[data-search-empty]");
        if (empty) empty.hidden = visible > 0;
    });
    contractForm?.addEventListener("submit", submitContract);
    orderForm?.addEventListener("submit", submitOrder);
})();
