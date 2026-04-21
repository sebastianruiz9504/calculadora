(function () {
    const app = document.getElementById("copiersApp");
    if (!app) {
        return;
    }

    const urls = {
        maintenance: app.dataset.maintenanceUrl || "",
        saveMaintenance: app.dataset.saveMaintenanceUrl || "",
        uploadMaintenance: app.dataset.uploadMaintenanceUrl || "",
        downloadMaintenance: app.dataset.downloadMaintenanceUrl || "",
        equipment: app.dataset.equipmentUrl || "",
        supplies: app.dataset.suppliesUrl || "",
        pendingInvoices: app.dataset.pendingInvoicesUrl || "",
        approveInvoice: app.dataset.approveInvoiceUrl || "",
        deliveries: app.dataset.deliveriesUrl || "",
        saveDelivery: app.dataset.saveDeliveryUrl || "",
        uploadDelivery: app.dataset.uploadDeliveryUrl || "",
        downloadDelivery: app.dataset.downloadDeliveryUrl || "",
        clientSearch: app.dataset.clientSearchUrl || ""
    };

    const statusBanner = document.getElementById("copiersStatus");
    const tabButtons = Array.from(document.querySelectorAll("[data-copiers-tab]"));
    const panels = Array.from(document.querySelectorAll("[data-copiers-panel]"));

    const maintenanceBody = document.getElementById("copiersMaintenanceBody");
    const maintenanceCount = document.getElementById("copiersMaintenanceCount");
    const maintenanceEmpty = document.getElementById("copiersMaintenanceEmpty");
    const maintenanceRefreshBtn = document.getElementById("copiersMaintenanceRefreshBtn");
    const newMaintenanceBtn = document.getElementById("copiersNewMaintenanceBtn");
    const maintenanceModal = document.getElementById("copiersMaintenanceModal");
    const maintenanceForm = document.getElementById("copiersMaintenanceForm");
    const maintenanceModalStatus = document.getElementById("copiersMaintenanceModalStatus");
    const maintenanceSaveBtn = document.getElementById("copiersMaintenanceSaveBtn");
    const maintenanceRecordIdInput = document.getElementById("copiersMaintenanceRecordId");
    const maintenanceTitleInput = document.getElementById("copiersMaintenanceTitle");
    const maintenanceInternalIdInput = document.getElementById("copiersMaintenanceInternalId");
    const maintenanceEquipmentSelect = document.getElementById("copiersMaintenanceEquipment");
    const maintenanceClientIdInput = document.getElementById("copiersMaintenanceClientId");
    const maintenanceClientNameInput = document.getElementById("copiersMaintenanceClientName");
    const clientOptions = document.getElementById("copiersClientOptions");
    const maintenanceDateInput = document.getElementById("copiersMaintenanceDate");
    const maintenanceTypeSelect = document.getElementById("copiersMaintenanceType");
    const maintenanceDescriptionInput = document.getElementById("copiersMaintenanceDescription");
    const maintenanceFileInput = document.getElementById("copiersMaintenanceFile");

    const equipmentRefreshBtn = document.getElementById("copiersEquipmentRefreshBtn");
    const equipmentCount = document.getElementById("copiersEquipmentCount");
    const equipmentKpis = document.getElementById("copiersEquipmentKpis");
    const equipmentClientsBody = document.getElementById("copiersEquipmentClientsBody");
    const equipmentStockBody = document.getElementById("copiersEquipmentStockBody");
    const equipmentBody = document.getElementById("copiersEquipmentBody");

    const suppliesRefreshBtn = document.getElementById("copiersSuppliesRefreshBtn");
    const suppliesBody = document.getElementById("copiersSuppliesBody");
    const suppliesCount = document.getElementById("copiersSuppliesCount");
    const suppliesEmpty = document.getElementById("copiersSuppliesEmpty");
    const newIngresoBtn = document.getElementById("copiersNewIngresoBtn");
    const ingresoModal = document.getElementById("copiersIngresoModal");
    const ingresoStatus = document.getElementById("copiersIngresoStatus");
    const pendingInvoicesBody = document.getElementById("copiersPendingInvoicesBody");
    const verifyIngresoBtn = document.getElementById("copiersVerifyIngresoBtn");
    const confirmIngresoModal = document.getElementById("copiersConfirmIngresoModal");
    const confirmIngresoText = document.getElementById("copiersConfirmIngresoText");
    const confirmIngresoBtn = document.getElementById("copiersConfirmIngresoBtn");

    const deliveriesRefreshBtn = document.getElementById("copiersDeliveriesRefreshBtn");
    const newDeliveryBtn = document.getElementById("copiersNewDeliveryBtn");
    const deliveriesBody = document.getElementById("copiersDeliveriesBody");
    const deliveriesCount = document.getElementById("copiersDeliveriesCount");
    const deliveriesEmpty = document.getElementById("copiersDeliveriesEmpty");
    const deliveryModal = document.getElementById("copiersDeliveryModal");
    const deliveryForm = document.getElementById("copiersDeliveryForm");
    const deliveryModalStatus = document.getElementById("copiersDeliveryModalStatus");
    const deliverySaveBtn = document.getElementById("copiersDeliverySaveBtn");
    const deliveryClientIdInput = document.getElementById("copiersDeliveryClientId");
    const deliveryClientNameInput = document.getElementById("copiersDeliveryClientName");
    const deliveryClientOptions = document.getElementById("copiersDeliveryClientOptions");
    const deliverySupplySelect = document.getElementById("copiersDeliverySupply");
    const deliveryDateInput = document.getElementById("copiersDeliveryDate");
    const deliveryQuantityInput = document.getElementById("copiersDeliveryQuantity");
    const deliveryStatusSelect = document.getElementById("copiersDeliveryStatus");
    const deliveryFileInput = document.getElementById("copiersDeliveryFile");

    const numberFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });

    const moneyFormatter = new Intl.NumberFormat("es-CO", {
        style: "currency",
        currency: "COP",
        maximumFractionDigits: 0
    });

    const state = {
        activeTab: "maintenance",
        busy: false,
        maintenance: null,
        equipment: null,
        supplies: null,
        pendingInvoices: [],
        deliveries: null,
        selectedPendingInvoiceId: "",
        maintenanceClientSuggestions: [],
        deliveryClientSuggestions: []
    };

    tabButtons.forEach((button) => {
        button.addEventListener("click", async () => {
            const tab = button.dataset.copiersTab || "maintenance";
            setActiveTab(tab);
            await ensureTabData(tab);
        });
    });

    maintenanceRefreshBtn?.addEventListener("click", () => loadMaintenance());
    equipmentRefreshBtn?.addEventListener("click", () => loadEquipment());
    suppliesRefreshBtn?.addEventListener("click", () => loadSupplies());
    deliveriesRefreshBtn?.addEventListener("click", () => loadDeliveries());

    newMaintenanceBtn?.addEventListener("click", async () => {
        await openMaintenanceModal();
    });

    maintenanceBody?.addEventListener("click", async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        if (target.closest("a")) {
            return;
        }

        const rowElement = target.closest("[data-record-id]");
        if (!(rowElement instanceof HTMLElement)) {
            return;
        }

        const row = findById(state.maintenance?.records, rowElement.dataset.recordId);
        if (row) {
            await openMaintenanceModal(row);
        }
    });

    newIngresoBtn?.addEventListener("click", openIngresoModal);
    verifyIngresoBtn?.addEventListener("click", openConfirmIngresoModal);
    confirmIngresoBtn?.addEventListener("click", approveSelectedIngreso);

    pendingInvoicesBody?.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const rowElement = target.closest("[data-invoice-id]");
        if (!(rowElement instanceof HTMLElement)) {
            return;
        }

        state.selectedPendingInvoiceId = rowElement.dataset.invoiceId || "";
        renderPendingInvoices();
    });

    newDeliveryBtn?.addEventListener("click", openDeliveryModal);

    maintenanceForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await saveMaintenance();
    });

    deliveryForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await saveDelivery();
    });

    document.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const closeTarget = target.getAttribute("data-copiers-close");
        if (closeTarget === "maintenance") {
            closeModal(maintenanceModal);
        } else if (closeTarget === "ingreso") {
            closeModal(ingresoModal);
        } else if (closeTarget === "confirmIngreso") {
            closeModal(confirmIngresoModal);
        } else if (closeTarget === "delivery") {
            closeModal(deliveryModal);
        }
    });

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape" || state.busy) {
            return;
        }

        [confirmIngresoModal, ingresoModal, maintenanceModal, deliveryModal].forEach((modal) => {
            if (modal && !modal.hidden) {
                closeModal(modal);
            }
        });
    });

    maintenanceClientNameInput?.addEventListener("input", debounce(async () => {
        maintenanceClientIdInput.value = "";
        await updateClientSuggestions(maintenanceClientNameInput.value, clientOptions, "maintenance");
    }, 250));

    maintenanceClientNameInput?.addEventListener("change", () => {
        syncClientSelection(maintenanceClientNameInput, maintenanceClientIdInput, state.maintenanceClientSuggestions);
    });

    deliveryClientNameInput?.addEventListener("input", debounce(async () => {
        deliveryClientIdInput.value = "";
        await updateClientSuggestions(deliveryClientNameInput.value, deliveryClientOptions, "delivery");
    }, 250));

    deliveryClientNameInput?.addEventListener("change", () => {
        syncClientSelection(deliveryClientNameInput, deliveryClientIdInput, state.deliveryClientSuggestions);
    });

    loadMaintenance();

    async function ensureTabData(tab) {
        if (tab === "maintenance" && !state.maintenance) {
            await loadMaintenance();
        } else if (tab === "equipment" && !state.equipment) {
            await loadEquipment();
        } else if (tab === "supplies" && !state.supplies) {
            await loadSupplies();
        } else if (tab === "deliveries" && !state.deliveries) {
            await loadDeliveries();
        }
    }

    function setActiveTab(tab) {
        state.activeTab = tab;
        tabButtons.forEach((button) => {
            button.classList.toggle("is-active", button.dataset.copiersTab === tab);
        });
        panels.forEach((panel) => {
            const active = panel.dataset.copiersPanel === tab;
            panel.classList.toggle("is-active", active);
            panel.hidden = !active;
        });
    }

    async function loadMaintenance() {
        try {
            setBusy(true);
            showStatus(statusBanner, "info", "Cargando mantenimientos...");
            state.maintenance = await fetchJson(urls.maintenance);
            renderMaintenance();
            clearStatus(statusBanner);
        } catch (error) {
            showStatus(statusBanner, "error", getErrorMessage(error));
        } finally {
            setBusy(false);
        }
    }

    function renderMaintenance() {
        const records = Array.isArray(state.maintenance?.records) ? state.maintenance.records : [];
        maintenanceCount.textContent = `${records.length} registro${records.length === 1 ? "" : "s"}`;
        maintenanceEmpty.hidden = records.length > 0;
        maintenanceBody.innerHTML = records.map((row) => {
            const attachment = row.hasAttachment
                ? `<a class="copiers-link" href="${buildDownloadUrl(urls.downloadMaintenance, "maintenanceId", row.recordId)}" target="_blank" rel="noopener">${escapeHtml(row.attachmentFileName || "Descargar")}</a>`
                : `<span class="copiers-muted">Sin adjunto</span>`;

            return `
                <tr class="is-selectable" data-record-id="${escapeHtml(row.recordId)}" tabindex="0">
                    <td>${escapeHtml(row.equipmentSerial || "Sin equipo")}</td>
                    <td>${escapeHtml(row.clientName || "Sin cliente")}</td>
                    <td>${escapeHtml(row.dateDisplay || "")}</td>
                    <td><span class="copiers-badge">${escapeHtml(row.maintenanceTypeLabel || "Sin tipo")}</span></td>
                    <td>${escapeHtml(row.technicianName || "")}</td>
                    <td>${attachment}</td>
                    <td>${escapeHtml(row.description || "")}</td>
                </tr>`;
        }).join("");
    }

    async function loadEquipment() {
        try {
            setBusy(true);
            showStatus(statusBanner, "info", "Cargando equipos...");
            state.equipment = await fetchJson(urls.equipment);
            renderEquipment();
            clearStatus(statusBanner);
        } catch (error) {
            showStatus(statusBanner, "error", getErrorMessage(error));
        } finally {
            setBusy(false);
        }
    }

    function renderEquipment() {
        const dashboard = state.equipment || {};
        const kpis = Array.isArray(dashboard.kpis) ? dashboard.kpis : [];
        const clientRows = Array.isArray(dashboard.clientSummaries) ? dashboard.clientSummaries : [];
        const stockRows = Array.isArray(dashboard.stockRows) ? dashboard.stockRows : [];
        const equipmentRows = Array.isArray(dashboard.equipmentRows) ? dashboard.equipmentRows : [];

        equipmentCount.textContent = `${equipmentRows.length} equipo${equipmentRows.length === 1 ? "" : "s"}`;
        equipmentKpis.innerHTML = kpis.map((kpi) => `
            <article class="copiers-kpi">
                <span>${escapeHtml(kpi.label)}</span>
                <strong>${formatMetric(kpi.value, kpi.valueFormat)}</strong>
                <small>${escapeHtml(kpi.secondaryLabel || "")}: ${escapeHtml(kpi.secondaryValue || "")}</small>
            </article>`).join("");

        equipmentClientsBody.innerHTML = clientRows.map((row) => `
            <tr>
                <td>${escapeHtml(row.clientName)}</td>
                <td class="text-end">${numberFormatter.format(Number(row.equipmentCount || 0))}</td>
                <td>${escapeHtml(row.categoryBreakdown || "")}</td>
            </tr>`).join("");

        equipmentStockBody.innerHTML = stockRows.map((row) => `
            <tr>
                <td>${escapeHtml(row.serial)}</td>
                <td>${escapeHtml(row.categoryLabel || "")}</td>
                <td>${escapeHtml(row.reference || "")}</td>
            </tr>`).join("");

        equipmentBody.innerHTML = equipmentRows.map((row) => `
            <tr>
                <td>${escapeHtml(row.serial)}</td>
                <td>${escapeHtml(row.clientName)}</td>
                <td>${escapeHtml(row.categoryLabel || "")}</td>
                <td>${escapeHtml(row.reference || "")}</td>
                <td class="text-end">${numberFormatter.format(Number(row.maintenanceCount || 0))}</td>
                <td>${escapeHtml(row.lastMaintenanceDateDisplay || "")}</td>
            </tr>`).join("");
    }

    async function loadSupplies() {
        try {
            setBusy(true);
            showStatus(statusBanner, "info", "Cargando suministros...");
            state.supplies = await fetchJson(urls.supplies);
            renderSupplies();
            clearStatus(statusBanner);
        } catch (error) {
            showStatus(statusBanner, "error", getErrorMessage(error));
        } finally {
            setBusy(false);
        }
    }

    function renderSupplies() {
        const records = Array.isArray(state.supplies?.records) ? state.supplies.records : [];
        suppliesCount.textContent = `${records.length} suministro${records.length === 1 ? "" : "s"}`;
        suppliesEmpty.hidden = records.length > 0;
        suppliesBody.innerHTML = records.map((row) => {
            const exhausted = Number(row.quantity || 0) <= 0 || Number(row.statusValue || 0) === 645250001;
            return `
                <tr>
                    <td>${escapeHtml(row.name)}</td>
                    <td class="text-end">${numberFormatter.format(Number(row.quantity || 0))}</td>
                    <td>${escapeHtml(row.lastPurchaseDateDisplay || "")}</td>
                    <td><span class="copiers-badge ${exhausted ? "is-danger" : "is-good"}">${escapeHtml(row.statusLabel || "")}</span></td>
                </tr>`;
        }).join("");
    }

    async function openIngresoModal() {
        try {
            setBusy(true);
            state.selectedPendingInvoiceId = "";
            showModal(ingresoModal);
            showStatus(ingresoStatus, "info", "Cargando ingresos pendientes...");
            const payload = await fetchJson(urls.pendingInvoices);
            state.pendingInvoices = Array.isArray(payload.records) ? payload.records : [];
            renderPendingInvoices();
            clearStatus(ingresoStatus);
        } catch (error) {
            showStatus(ingresoStatus, "error", getErrorMessage(error));
        } finally {
            setBusy(false);
        }
    }

    function renderPendingInvoices() {
        verifyIngresoBtn.disabled = !state.selectedPendingInvoiceId;
        pendingInvoicesBody.innerHTML = state.pendingInvoices.map((row) => {
            const selected = row.recordId === state.selectedPendingInvoiceId;
            return `
                <tr class="is-selectable ${selected ? "is-selected" : ""}" data-invoice-id="${escapeHtml(row.recordId)}">
                    <td>${escapeHtml(row.invoiceNumber)}</td>
                    <td>${escapeHtml(row.supplyName)}</td>
                    <td class="text-end">${numberFormatter.format(Number(row.quantity || 0))}</td>
                    <td><span class="copiers-badge is-warning">${escapeHtml(row.approvedLabel || "No")}</span></td>
                </tr>`;
        }).join("");

        if (state.pendingInvoices.length === 0) {
            pendingInvoicesBody.innerHTML = `<tr><td colspan="4" class="text-center copiers-muted">No hay ingresos pendientes por verificar.</td></tr>`;
        }
    }

    function openConfirmIngresoModal() {
        const invoice = findById(state.pendingInvoices, state.selectedPendingInvoiceId);
        if (!invoice) {
            showStatus(ingresoStatus, "warning", "Selecciona una fila para verificar.");
            return;
        }

        confirmIngresoText.textContent = `${invoice.invoiceNumber} - ${invoice.supplyName} - ${numberFormatter.format(Number(invoice.quantity || 0))} unidades`;
        showModal(confirmIngresoModal);
    }

    async function approveSelectedIngreso() {
        if (!state.selectedPendingInvoiceId) {
            return;
        }

        try {
            setBusy(true);
            confirmIngresoBtn.disabled = true;
            const result = await fetchJson(urls.approveInvoice, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ invoiceId: state.selectedPendingInvoiceId })
            });
            closeModal(confirmIngresoModal);
            closeModal(ingresoModal);
            await loadSupplies();
            showStatus(statusBanner, "success", result.message || "Ingreso aprobado.");
        } catch (error) {
            showStatus(ingresoStatus, "error", getErrorMessage(error));
        } finally {
            confirmIngresoBtn.disabled = false;
            setBusy(false);
        }
    }

    async function loadDeliveries() {
        try {
            setBusy(true);
            showStatus(statusBanner, "info", "Cargando entregas...");
            state.deliveries = await fetchJson(urls.deliveries);
            renderDeliveries();
            clearStatus(statusBanner);
        } catch (error) {
            showStatus(statusBanner, "error", getErrorMessage(error));
        } finally {
            setBusy(false);
        }
    }

    function renderDeliveries() {
        const records = Array.isArray(state.deliveries?.records) ? state.deliveries.records : [];
        deliveriesCount.textContent = `${records.length} entrega${records.length === 1 ? "" : "s"}`;
        deliveriesEmpty.hidden = records.length > 0;
        deliveriesBody.innerHTML = records.map((row) => {
            const attachment = row.hasAttachment
                ? `<a class="copiers-link" href="${buildDownloadUrl(urls.downloadDelivery, "deliveryId", row.recordId)}" target="_blank" rel="noopener">${escapeHtml(row.attachmentFileName || "Descargar")}</a>`
                : `<span class="copiers-muted">Sin adjunto</span>`;
            const completed = Number(row.statusValue || 0) === 645250000;

            return `
                <tr>
                    <td>${escapeHtml(row.clientName)}</td>
                    <td>${escapeHtml(row.supplyName)}</td>
                    <td>${escapeHtml(row.deliveryDateDisplay || "")}</td>
                    <td class="text-end">${numberFormatter.format(Number(row.quantityDelivered || 0))}</td>
                    <td><span class="copiers-badge ${completed ? "is-good" : "is-warning"}">${escapeHtml(row.statusLabel || "")}</span></td>
                    <td>${attachment}</td>
                    <td>${escapeHtml(row.ownerName || "")}</td>
                </tr>`;
        }).join("");
    }

    async function openMaintenanceModal(row) {
        if (!state.maintenance) {
            await loadMaintenance();
        }

        if (!state.equipment) {
            await loadEquipment();
        }

        populateMaintenanceOptions();
        maintenanceRecordIdInput.value = row?.recordId || "";
        maintenanceTitleInput.value = row?.title || "";
        maintenanceInternalIdInput.value = row?.internalId || "";
        maintenanceEquipmentSelect.value = row?.equipmentId || "";
        maintenanceClientIdInput.value = row?.clientId || "";
        maintenanceClientNameInput.value = row?.clientName || "";
        maintenanceDateInput.value = row?.dateValue || todayValue();
        maintenanceTypeSelect.value = row?.maintenanceTypeValue || "";
        maintenanceDescriptionInput.value = row?.description || "";
        maintenanceFileInput.value = "";
        clearStatus(maintenanceModalStatus);
        showModal(maintenanceModal);
    }

    function populateMaintenanceOptions() {
        const equipmentRows = Array.isArray(state.equipment?.equipmentRows) ? state.equipment.equipmentRows : [];
        maintenanceEquipmentSelect.innerHTML = `<option value="">Selecciona un equipo</option>` + equipmentRows.map((row) => {
            const label = `${row.serial || "Equipo"}${row.clientName ? " - " + row.clientName : ""}`;
            return `<option value="${escapeHtml(row.recordId)}">${escapeHtml(label)}</option>`;
        }).join("");

        const typeOptions = Array.isArray(state.maintenance?.typeOptions) ? state.maintenance.typeOptions : [];
        maintenanceTypeSelect.innerHTML = `<option value="">Sin tipo</option>` + typeOptions.map((option) => (
            `<option value="${option.value}">${escapeHtml(option.label)}</option>`
        )).join("");
    }

    async function saveMaintenance() {
        try {
            setBusy(true);
            maintenanceSaveBtn.disabled = true;
            showStatus(maintenanceModalStatus, "info", "Guardando mantenimiento...");
            syncClientSelection(maintenanceClientNameInput, maintenanceClientIdInput, state.maintenanceClientSuggestions);
            const payload = {
                recordId: maintenanceRecordIdInput.value,
                title: maintenanceTitleInput.value,
                internalId: maintenanceInternalIdInput.value,
                equipmentId: maintenanceEquipmentSelect.value,
                clientId: maintenanceClientIdInput.value,
                clientName: maintenanceClientNameInput.value,
                dateValue: maintenanceDateInput.value,
                description: maintenanceDescriptionInput.value,
                maintenanceTypeValue: maintenanceTypeSelect.value ? Number(maintenanceTypeSelect.value) : null
            };
            let result = await fetchJson(urls.saveMaintenance, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });

            const file = maintenanceFileInput.files?.[0];
            if (file && result.record?.recordId) {
                showStatus(maintenanceModalStatus, "info", "Adjuntando reporte...");
                result = await uploadFile(urls.uploadMaintenance, "maintenanceId", result.record.recordId, file);
            }

            closeModal(maintenanceModal);
            await loadMaintenance();
            showStatus(statusBanner, "success", result.message || "Mantenimiento guardado.");
        } catch (error) {
            showStatus(maintenanceModalStatus, "error", getErrorMessage(error));
        } finally {
            maintenanceSaveBtn.disabled = false;
            setBusy(false);
        }
    }

    async function openDeliveryModal() {
        if (!state.supplies) {
            await loadSupplies();
        }

        if (!state.deliveries) {
            await loadDeliveries();
        }

        populateDeliveryOptions();
        deliveryClientIdInput.value = "";
        deliveryClientNameInput.value = "";
        deliverySupplySelect.value = "";
        deliveryDateInput.value = todayValue();
        deliveryQuantityInput.value = "";
        deliveryStatusSelect.value = "645250000";
        deliveryFileInput.value = "";
        clearStatus(deliveryModalStatus);
        showModal(deliveryModal);
    }

    function populateDeliveryOptions() {
        const supplies = Array.isArray(state.supplies?.records) ? state.supplies.records : [];
        deliverySupplySelect.innerHTML = `<option value="">Selecciona un suministro</option>` + supplies.map((row) => (
            `<option value="${escapeHtml(row.recordId)}">${escapeHtml(row.name)} (${numberFormatter.format(Number(row.quantity || 0))})</option>`
        )).join("");

        const statusOptions = Array.isArray(state.deliveries?.statusOptions) ? state.deliveries.statusOptions : [];
        deliveryStatusSelect.innerHTML = statusOptions.map((option) => (
            `<option value="${option.value}">${escapeHtml(option.label)}</option>`
        )).join("");
    }

    async function saveDelivery() {
        try {
            setBusy(true);
            deliverySaveBtn.disabled = true;
            showStatus(deliveryModalStatus, "info", "Guardando entrega...");
            syncClientSelection(deliveryClientNameInput, deliveryClientIdInput, state.deliveryClientSuggestions);

            let result = await fetchJson(urls.saveDelivery, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    clientId: deliveryClientIdInput.value,
                    clientName: deliveryClientNameInput.value,
                    supplyId: deliverySupplySelect.value,
                    deliveryDateValue: deliveryDateInput.value,
                    quantityDelivered: Number(deliveryQuantityInput.value || 0),
                    statusValue: deliveryStatusSelect.value ? Number(deliveryStatusSelect.value) : null
                })
            });

            const file = deliveryFileInput.files?.[0];
            if (file && result.record?.recordId) {
                showStatus(deliveryModalStatus, "info", "Adjuntando comprobante...");
                result = await uploadFile(urls.uploadDelivery, "deliveryId", result.record.recordId, file);
            }

            closeModal(deliveryModal);
            await Promise.all([loadDeliveries(), loadSupplies()]);
            showStatus(statusBanner, "success", result.message || "Entrega guardada.");
        } catch (error) {
            showStatus(deliveryModalStatus, "error", getErrorMessage(error));
        } finally {
            deliverySaveBtn.disabled = false;
            setBusy(false);
        }
    }

    async function updateClientSuggestions(term, datalist, target) {
        const query = (term || "").trim();
        if (query.length < 2) {
            datalist.innerHTML = "";
            return;
        }

        try {
            const suggestions = await fetchJson(`${urls.clientSearch}?q=${encodeURIComponent(query)}`);
            if (target === "delivery") {
                state.deliveryClientSuggestions = Array.isArray(suggestions) ? suggestions : [];
            } else {
                state.maintenanceClientSuggestions = Array.isArray(suggestions) ? suggestions : [];
            }

            datalist.innerHTML = (Array.isArray(suggestions) ? suggestions : []).map((item) => (
                `<option value="${escapeHtml(item.name || "")}"></option>`
            )).join("");
        } catch {
            datalist.innerHTML = "";
        }
    }

    function syncClientSelection(input, hiddenInput, suggestions) {
        const value = normalizeText(input.value);
        const match = (suggestions || []).find((item) => normalizeText(item.name) === value);
        hiddenInput.value = match?.id || hiddenInput.value || "";
    }

    async function uploadFile(baseUrl, idParamName, id, file) {
        const form = new FormData();
        form.append("file", file);
        return await fetchJson(`${baseUrl}?${encodeURIComponent(idParamName)}=${encodeURIComponent(id)}`, {
            method: "POST",
            body: form
        });
    }

    async function fetchJson(url, options) {
        const response = await fetch(url, {
            ...(options || {}),
            headers: {
                Accept: "application/json",
                ...(options?.headers || {})
            }
        });
        const payload = await readPayload(response);
        if (!response.ok) {
            throw createResponseError(payload);
        }
        return payload;
    }

    async function readPayload(response) {
        const contentType = response.headers.get("content-type") || "";
        if (contentType.includes("application/json")) {
            return await response.json();
        }
        return { message: await response.text() };
    }

    function createResponseError(payload) {
        const error = new Error(payload?.detail || payload?.message || "Ocurrio un error inesperado.");
        error.payload = payload;
        return error;
    }

    function getErrorMessage(error) {
        return error instanceof Error ? error.message : "Ocurrio un error inesperado.";
    }

    function setBusy(value) {
        state.busy = value;
    }

    function showStatus(element, tone, message) {
        if (!element) {
            return;
        }

        element.textContent = message || "";
        element.className = `copiers-status is-visible ${tone ? "is-" + tone : ""}`;
    }

    function clearStatus(element) {
        if (!element) {
            return;
        }

        element.textContent = "";
        element.className = "copiers-status";
    }

    function showModal(modal) {
        if (modal) {
            modal.hidden = false;
        }
    }

    function closeModal(modal) {
        if (modal) {
            modal.hidden = true;
        }
    }

    function findById(rows, id) {
        return (rows || []).find((row) => row.recordId === id);
    }

    function buildDownloadUrl(baseUrl, key, value) {
        return `${baseUrl}?${encodeURIComponent(key)}=${encodeURIComponent(value || "")}`;
    }

    function formatMetric(value, format) {
        const numeric = Number(value || 0);
        if (format === "currency") {
            return moneyFormatter.format(numeric);
        }
        return numberFormatter.format(numeric);
    }

    function escapeHtml(value) {
        return (value ?? "").toString()
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function normalizeText(value) {
        return (value ?? "")
            .toString()
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .toLowerCase()
            .trim();
    }

    function todayValue() {
        const now = new Date();
        const offset = now.getTimezoneOffset();
        return new Date(now.getTime() - offset * 60000).toISOString().slice(0, 10);
    }

    function debounce(callback, delay) {
        let handle = 0;
        return (...args) => {
            window.clearTimeout(handle);
            handle = window.setTimeout(() => callback(...args), delay);
        };
    }
})();
