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
        equipmentDetail: app.dataset.equipmentDetailUrl || "",
        equipmentInventory: app.dataset.equipmentInventoryUrl || "",
        equipmentAssignment: app.dataset.equipmentAssignmentUrl || "",
        saveEquipment: app.dataset.saveEquipmentUrl || "",
        saveEquipmentClient: app.dataset.saveEquipmentClientUrl || "",
        supplies: app.dataset.suppliesUrl || "",
        saveSupplyQuantity: app.dataset.saveSupplyQuantityUrl || "",
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
    const maintenanceModalTitle = document.getElementById("copiersMaintenanceModalTitle");
    const maintenanceModalStatus = document.getElementById("copiersMaintenanceModalStatus");
    const maintenanceSaveBtn = document.getElementById("copiersMaintenanceSaveBtn");
    const maintenanceRecordIdInput = document.getElementById("copiersMaintenanceRecordId");
    const maintenanceTitleInput = document.getElementById("copiersMaintenanceTitle");
    const maintenanceEquipmentSelect = document.getElementById("copiersMaintenanceEquipment");
    const maintenanceClientIdInput = document.getElementById("copiersMaintenanceClientId");
    const maintenanceClientNameInput = document.getElementById("copiersMaintenanceClientName");
    const clientOptions = document.getElementById("copiersClientOptions");
    const maintenanceDateInput = document.getElementById("copiersMaintenanceDate");
    const maintenanceTypeSelect = document.getElementById("copiersMaintenanceType");
    const maintenanceStatusSelect = document.getElementById("copiersMaintenanceStatus");
    const maintenanceDescriptionInput = document.getElementById("copiersMaintenanceDescription");
    const maintenanceFileInput = document.getElementById("copiersMaintenanceFile");

    const equipmentRefreshBtn = document.getElementById("copiersEquipmentRefreshBtn");
    const equipmentCount = document.getElementById("copiersEquipmentCount");
    const equipmentKpis = document.getElementById("copiersEquipmentKpis");
    const equipmentClientsBody = document.getElementById("copiersEquipmentClientsBody");
    const equipmentStockBody = document.getElementById("copiersEquipmentStockBody");
    const equipmentBody = document.getElementById("copiersEquipmentBody");
    const equipmentSerialSearch = document.getElementById("copiersEquipmentSerialSearch");
    const equipmentDetailModal = document.getElementById("copiersEquipmentDetailModal");
    const equipmentDetailStatus = document.getElementById("copiersEquipmentDetailStatus");
    const equipmentDetailTitle = document.getElementById("copiersEquipmentDetailTitle");
    const equipmentDetailSubtitle = document.getElementById("copiersEquipmentDetailSubtitle");
    const equipmentAssignmentForm = document.getElementById("copiersEquipmentAssignmentForm");
    const equipmentRecordIdInput = document.getElementById("copiersEquipmentRecordId");
    const equipmentClientIdInput = document.getElementById("copiersEquipmentClientId");
    const equipmentClientNameInput = document.getElementById("copiersEquipmentClientName");
    const equipmentClientOptions = document.getElementById("copiersEquipmentClientOptions");
    const equipmentSaveBtn = document.getElementById("copiersEquipmentSaveBtn");
    const equipmentSerialInput = document.getElementById("copiersEquipmentSerial");
    const equipmentCategorySelect = document.getElementById("copiersEquipmentCategory");
    const equipmentAreaInput = document.getElementById("copiersEquipmentArea");
    const equipmentSiteInput = document.getElementById("copiersEquipmentSite");
    const equipmentReferenceInput = document.getElementById("copiersEquipmentReference");
    const equipmentMaintenanceCountInput = document.getElementById("copiersEquipmentMaintenanceCount");
    const equipmentLastMaintenanceInput = document.getElementById("copiersEquipmentLastMaintenance");
    const equipmentObservationsInput = document.getElementById("copiersEquipmentObservations");
    const clientDetailModal = document.getElementById("copiersClientDetailModal");
    const clientDetailForm = document.getElementById("copiersClientDetailForm");
    const clientDetailStatus = document.getElementById("copiersClientDetailStatus");
    const clientDetailIdInput = document.getElementById("copiersClientDetailId");
    const clientDetailNameInput = document.getElementById("copiersClientDetailName");
    const clientDetailContactInput = document.getElementById("copiersClientDetailContact");
    const clientDetailEmailInput = document.getElementById("copiersClientDetailEmail");
    const clientDetailPhoneInput = document.getElementById("copiersClientDetailPhone");
    const clientDetailAddressInput = document.getElementById("copiersClientDetailAddress");
    const clientDetailSaveBtn = document.getElementById("copiersClientDetailSaveBtn");

    const inventoryClientNameInput = document.getElementById("copiersInventoryClientName");
    const inventoryClientIdInput = document.getElementById("copiersInventoryClientId");
    const inventoryClientOptions = document.getElementById("copiersInventoryClientOptions");
    const inventoryLoadBtn = document.getElementById("copiersInventoryLoadBtn");
    const inventoryClearBtn = document.getElementById("copiersInventoryClearBtn");
    const inventoryCount = document.getElementById("copiersInventoryCount");
    const inventoryMissing = document.getElementById("copiersInventoryMissing");
    const inventoryKpis = document.getElementById("copiersInventoryKpis");
    const inventoryLocations = document.getElementById("copiersInventoryLocations");
    const inventoryBody = document.getElementById("copiersInventoryBody");
    const inventoryEmpty = document.getElementById("copiersInventoryEmpty");

    const suppliesRefreshBtn = document.getElementById("copiersSuppliesRefreshBtn");
    const suppliesBody = document.getElementById("copiersSuppliesBody");
    const suppliesCount = document.getElementById("copiersSuppliesCount");
    const suppliesEmpty = document.getElementById("copiersSuppliesEmpty");
    const supplyModal = document.getElementById("copiersSupplyModal");
    const supplyForm = document.getElementById("copiersSupplyForm");
    const supplyModalStatus = document.getElementById("copiersSupplyModalStatus");
    const supplyRecordIdInput = document.getElementById("copiersSupplyRecordId");
    const supplyNameInput = document.getElementById("copiersSupplyName");
    const supplyQuantityInput = document.getElementById("copiersSupplyQuantity");
    const supplyLastPurchaseInput = document.getElementById("copiersSupplyLastPurchase");
    const supplyStatusLabelInput = document.getElementById("copiersSupplyStatusLabel");
    const supplySaveBtn = document.getElementById("copiersSupplySaveBtn");
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
    const deliveryModalTitle = document.getElementById("copiersDeliveryModalTitle");
    const deliveryModalStatus = document.getElementById("copiersDeliveryModalStatus");
    const deliverySaveBtn = document.getElementById("copiersDeliverySaveBtn");
    const deliveryRecordIdInput = document.getElementById("copiersDeliveryRecordId");
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
        equipmentSerialSearch: "",
        equipmentDetail: null,
        equipmentClientSuggestions: [],
        equipmentAssignmentSaving: false,
        clientDetail: null,
        clientSaving: false,
        equipmentInventory: null,
        equipmentInventoryClientSuggestions: [],
        supplies: null,
        pendingInvoices: [],
        deliveries: null,
        selectedPendingInvoiceId: "",
        maintenanceClientSuggestions: [],
        deliveryClientSuggestions: []
    };

    const maintenanceStatusPending = 645250001;
    const maintenanceStatusCompleted = 645250000;
    const fallbackMaintenanceStatusOptions = [
        { value: maintenanceStatusCompleted, label: "Completado" },
        { value: maintenanceStatusPending, label: "Pendiente" }
    ];

    tabButtons.forEach((button) => {
        button.addEventListener("click", async () => {
            const tab = button.dataset.copiersTab || "maintenance";
            setActiveTab(tab);
            await ensureTabData(tab);
        });
    });

    maintenanceRefreshBtn?.addEventListener("click", () => loadMaintenance());
    equipmentRefreshBtn?.addEventListener("click", () => loadEquipment());
    inventoryLoadBtn?.addEventListener("click", () => loadEquipmentInventory());
    inventoryClearBtn?.addEventListener("click", clearEquipmentInventory);
    suppliesRefreshBtn?.addEventListener("click", () => loadSupplies());
    deliveriesRefreshBtn?.addEventListener("click", () => loadDeliveries());

    equipmentSerialSearch?.addEventListener("input", () => {
        state.equipmentSerialSearch = equipmentSerialSearch.value || "";
        renderEquipment();
    });

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

    suppliesBody?.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const rowElement = target.closest("[data-supply-id]");
        if (!(rowElement instanceof HTMLElement)) {
            return;
        }

        const row = findById(state.supplies?.records, rowElement.dataset.supplyId);
        if (row) {
            openSupplyModal(row);
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

    newDeliveryBtn?.addEventListener("click", () => openDeliveryModal());

    deliveriesBody?.addEventListener("click", async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement) || target.closest("a")) {
            return;
        }

        const rowElement = target.closest("[data-delivery-id]");
        if (!(rowElement instanceof HTMLElement)) {
            return;
        }

        const row = findById(state.deliveries?.records, rowElement.dataset.deliveryId);
        if (row) {
            await openDeliveryModal(row);
        }
    });

    equipmentBody?.addEventListener("click", async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement) || target.closest("a")) {
            return;
        }

        const rowElement = target.closest("[data-equipment-id]");
        if (!(rowElement instanceof HTMLElement)) {
            return;
        }

        await loadEquipmentDetail(rowElement.dataset.equipmentId || "");
    });

    equipmentClientsBody?.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement) || target.closest("a")) {
            return;
        }

        const rowElement = target.closest("[data-client-id]");
        if (!(rowElement instanceof HTMLElement)) {
            return;
        }

        const row = findClientSummary(rowElement.dataset.clientId || "");
        if (row) {
            openClientDetail(row);
        }
    });

    inventoryBody?.addEventListener("click", async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement) || target.closest("a")) {
            return;
        }

        const rowElement = target.closest("[data-equipment-id]");
        if (!(rowElement instanceof HTMLElement)) {
            return;
        }

        await loadEquipmentDetail(rowElement.dataset.equipmentId || "");
    });

    maintenanceForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await saveMaintenance();
    });

    equipmentAssignmentForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await saveEquipmentAssignment();
    });

    clientDetailForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await saveEquipmentClient();
    });

    supplyForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await saveSupplyQuantity();
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
        } else if (closeTarget === "equipmentDetail") {
            closeModal(equipmentDetailModal);
        } else if (closeTarget === "clientDetail") {
            closeModal(clientDetailModal);
        } else if (closeTarget === "supply") {
            closeModal(supplyModal);
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

        [confirmIngresoModal, ingresoModal, supplyModal, clientDetailModal, equipmentDetailModal, maintenanceModal, deliveryModal].forEach((modal) => {
            if (modal && !modal.hidden) {
                closeModal(modal);
            }
        });
    });

    maintenanceClientNameInput?.addEventListener("input", debounce(async () => {
        maintenanceClientIdInput.value = "";
        await updateClientSuggestions(maintenanceClientNameInput.value, clientOptions, "maintenance");
        updateMaintenanceEquipmentOptions();
    }, 250));

    maintenanceClientNameInput?.addEventListener("focus", () => {
        updateClientSuggestions(maintenanceClientNameInput.value, clientOptions, "maintenance");
    });

    maintenanceClientNameInput?.addEventListener("change", () => {
        syncClientSelection(maintenanceClientNameInput, maintenanceClientIdInput, state.maintenanceClientSuggestions);
        updateMaintenanceEquipmentOptions();
    });

    equipmentClientNameInput?.addEventListener("input", debounce(async () => {
        equipmentClientIdInput.value = "";
        await updateClientSuggestions(equipmentClientNameInput.value, equipmentClientOptions, "equipment");
    }, 250));

    equipmentClientNameInput?.addEventListener("focus", () => {
        updateClientSuggestions(equipmentClientNameInput.value, equipmentClientOptions, "equipment");
    });

    equipmentClientNameInput?.addEventListener("change", () => {
        syncClientSelection(equipmentClientNameInput, equipmentClientIdInput, state.equipmentClientSuggestions);
    });

    inventoryClientNameInput?.addEventListener("input", debounce(async () => {
        inventoryClientIdInput.value = "";
        await updateClientSuggestions(inventoryClientNameInput.value, inventoryClientOptions, "equipmentInventory");
    }, 250));

    inventoryClientNameInput?.addEventListener("focus", () => {
        updateClientSuggestions(inventoryClientNameInput.value, inventoryClientOptions, "equipmentInventory");
    });

    inventoryClientNameInput?.addEventListener("change", () => {
        syncClientSelection(inventoryClientNameInput, inventoryClientIdInput, state.equipmentInventoryClientSuggestions);
    });

    inventoryClientNameInput?.addEventListener("keydown", async (event) => {
        if (event.key === "Enter") {
            event.preventDefault();
            await loadEquipmentInventory();
        }
    });

    deliveryClientNameInput?.addEventListener("input", debounce(async () => {
        deliveryClientIdInput.value = "";
        await updateClientSuggestions(deliveryClientNameInput.value, deliveryClientOptions, "delivery");
    }, 250));

    deliveryClientNameInput?.addEventListener("focus", () => {
        updateClientSuggestions(deliveryClientNameInput.value, deliveryClientOptions, "delivery");
    });

    deliveryClientNameInput?.addEventListener("change", () => {
        syncClientSelection(deliveryClientNameInput, deliveryClientIdInput, state.deliveryClientSuggestions);
    });

    loadMaintenance();

    async function ensureTabData(tab) {
        if (tab === "maintenance" && !state.maintenance) {
            await loadMaintenance();
        } else if (tab === "equipment" && !state.equipment) {
            await loadEquipment();
        } else if (tab === "equipmentInventory" && !state.equipmentInventory) {
            renderEquipmentInventory();
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
            const statusValue = Number(row.maintenanceStatusValue || maintenanceStatusPending);
            const completed = statusValue === maintenanceStatusCompleted;

            return `
                <tr class="is-selectable" data-record-id="${escapeHtml(row.recordId)}" tabindex="0">
                    <td data-label="Equipo">${escapeHtml(row.equipmentSerial || "Sin equipo")}</td>
                    <td data-label="Cliente">${escapeHtml(row.clientName || "Sin cliente")}</td>
                    <td data-label="Fecha">${escapeHtml(row.dateDisplay || "")}</td>
                    <td data-label="Tipo"><span class="copiers-badge">${escapeHtml(row.maintenanceTypeLabel || "Sin tipo")}</span></td>
                    <td data-label="Estado"><span class="copiers-badge ${completed ? "is-good" : "is-warning"}">${escapeHtml(row.maintenanceStatusLabel || "Pendiente")}</span></td>
                    <td data-label="Tecnico">${escapeHtml(row.technicianName || "")}</td>
                    <td data-label="Reporte">${attachment}</td>
                    <td data-label="Descripcion">${escapeHtml(row.description || "")}</td>
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
        const allEquipmentRows = Array.isArray(dashboard.equipmentRows) ? dashboard.equipmentRows : [];
        const serialFilter = normalizeText(state.equipmentSerialSearch);
        const equipmentRows = serialFilter
            ? allEquipmentRows.filter((row) => normalizeText(row.serial).includes(serialFilter))
            : allEquipmentRows;

        equipmentCount.textContent = serialFilter
            ? `${equipmentRows.length} de ${allEquipmentRows.length} equipos`
            : `${allEquipmentRows.length} equipo${allEquipmentRows.length === 1 ? "" : "s"}`;
        equipmentKpis.innerHTML = kpis.map((kpi) => `
            <article class="copiers-kpi">
                <span>${escapeHtml(kpi.label)}</span>
                <strong>${formatMetric(kpi.value, kpi.valueFormat)}</strong>
                <small>${escapeHtml(kpi.secondaryLabel || "")}: ${escapeHtml(kpi.secondaryValue || "")}</small>
            </article>`).join("");

        equipmentClientsBody.innerHTML = clientRows.map((row) => `
            <tr class="is-selectable" data-client-id="${escapeHtml(row.clientId || "")}" tabindex="0">
                <td data-label="Cliente">${escapeHtml(row.clientName)}</td>
                <td data-label="Persona a cargo">${escapeHtml(row.contactName || "")}</td>
                <td data-label="Correo">${escapeHtml(row.email || "")}</td>
                <td data-label="Telefono">${escapeHtml(row.phone || "")}</td>
                <td data-label="Direccion">${escapeHtml(row.address || "")}</td>
                <td data-label="Equipos" class="text-end">${numberFormatter.format(Number(row.equipmentCount || 0))}</td>
                <td data-label="Categorias">${escapeHtml(row.categoryBreakdown || "")}</td>
            </tr>`).join("");

        equipmentStockBody.innerHTML = stockRows.map((row) => `
            <tr>
                <td data-label="Serial">${escapeHtml(row.serial)}</td>
                <td data-label="Categoria">${escapeHtml(row.categoryLabel || "")}</td>
                <td data-label="Referencia">${escapeHtml(row.reference || "")}</td>
            </tr>`).join("");

        equipmentBody.innerHTML = equipmentRows.length ? equipmentRows.map((row) => `
            <tr class="is-selectable" data-equipment-id="${escapeHtml(row.recordId || "")}" tabindex="0">
                <td data-label="Serial">${escapeHtml(row.serial)}</td>
                <td data-label="Cliente">${row.inStock ? '<span class="copiers-badge is-warning">Stock</span>' : escapeHtml(row.clientName || "Sin cliente")}</td>
                <td data-label="Categoria">${escapeHtml(row.categoryLabel || "")}</td>
                <td data-label="Area">${escapeHtml(row.area || "")}</td>
                <td data-label="Sede">${escapeHtml(row.site || "")}</td>
                <td data-label="Referencia">${escapeHtml(row.reference || "")}</td>
                <td data-label="Observaciones">${escapeHtml(row.observations || "")}</td>
            </tr>`).join("") : `<tr><td colspan="7" class="text-center copiers-muted">No hay equipos para mostrar.</td></tr>`;
    }

    async function loadEquipmentInventory() {
        try {
            syncClientSelection(inventoryClientNameInput, inventoryClientIdInput, state.equipmentInventoryClientSuggestions);
            const clientId = inventoryClientIdInput.value || "";
            const clientName = (inventoryClientNameInput.value || "").trim();
            if (!clientId && !clientName) {
                showStatus(statusBanner, "warning", "Selecciona un cliente para consultar el inventario de equipos.");
                renderEquipmentInventory();
                return;
            }

            setBusy(true);
            inventoryLoadBtn.disabled = true;
            showStatus(statusBanner, "info", "Cargando inventario de equipos...");
            const params = new URLSearchParams();
            if (clientId) {
                params.set("clientId", clientId);
            }
            if (clientName) {
                params.set("clientName", clientName);
            }

            state.equipmentInventory = await fetchJson(`${urls.equipmentInventory}?${params.toString()}`);
            renderEquipmentInventory();

            clearStatus(statusBanner);
        } catch (error) {
            showStatus(statusBanner, "error", getErrorMessage(error));
        } finally {
            inventoryLoadBtn.disabled = false;
            setBusy(false);
        }
    }

    function renderEquipmentInventory() {
        const inventory = state.equipmentInventory || null;
        const records = Array.isArray(inventory?.records) ? inventory.records : [];
        const kpis = Array.isArray(inventory?.kpis) ? inventory.kpis : [];

        inventoryCount.textContent = `${records.length} equipo${records.length === 1 ? "" : "s"}`;
        inventoryEmpty.hidden = Boolean(inventory) && records.length > 0;
        inventoryEmpty.textContent = inventory
            ? "No hay equipos registrados para este cliente."
            : "Selecciona un cliente para consultar sus equipos.";

        clearStatus(inventoryMissing);

        inventoryKpis.innerHTML = kpis.map((kpi) => `
            <article class="copiers-kpi">
                <span>${escapeHtml(kpi.label)}</span>
                <strong>${numberFormatter.format(Number(kpi.value || 0))}</strong>
                <small>${escapeHtml(kpi.secondaryLabel || "")}: ${escapeHtml(kpi.secondaryValue || "")}</small>
            </article>`).join("");

        inventoryLocations.innerHTML = inventory ? `
            <article class="copiers-location-card copiers-location-card--client">
                <div>
                    <span>Cliente</span>
                    <strong>${escapeHtml(inventory.clientName || "Sin cliente")}</strong>
                    <p>${escapeHtml(inventory.clientContactName || "Sin persona a cargo")}</p>
                    <small>${escapeHtml(inventory.clientEmail || "Sin correo")}</small>
                    <small>${escapeHtml(inventory.clientPhone || "Sin telefono")}</small>
                    <small>${escapeHtml(inventory.clientAddress || "Sin direccion")}</small>
                </div>
                ${renderAddressMapFrame(inventory.clientAddress)}
            </article>` : "";

        inventoryBody.innerHTML = records.length ? records.map((row) => `
            <tr class="is-selectable" data-equipment-id="${escapeHtml(row.recordId || "")}" tabindex="0">
                <td data-label="No.">${numberFormatter.format(Number(row.lineNumber || 0))}</td>
                <td data-label="Serial de maquina">${escapeHtml(row.serial || "")}</td>
                <td data-label="Empresa">${escapeHtml(row.company || "")}</td>
                <td data-label="Area">${escapeHtml(row.area || "")}</td>
                <td data-label="Sede">${escapeHtml(row.site || "")}</td>
                <td data-label="Observaciones">${escapeHtml(row.observations || "")}</td>
            </tr>`).join("") : `<tr><td colspan="6" class="text-center copiers-muted">No hay equipos para mostrar.</td></tr>`;
    }

    function clearEquipmentInventory() {
        state.equipmentInventory = null;
        state.equipmentInventoryClientSuggestions = [];
        inventoryClientNameInput.value = "";
        inventoryClientIdInput.value = "";
        inventoryClientOptions.innerHTML = "";
        renderEquipmentInventory();
        clearStatus(inventoryMissing);
        clearStatus(statusBanner);
    }

    function renderEquipmentDetailLoading(row) {
        resetEquipmentDetail();
        showModal(equipmentDetailModal);
        equipmentDetailTitle.textContent = row?.serial ? `Equipo ${row.serial}` : "Detalle del equipo";
        equipmentDetailSubtitle.textContent = "Cargando informacion del equipo...";
        showStatus(equipmentDetailStatus, "info", "Consultando detalle del equipo...");
    }

    async function loadEquipmentDetail(recordId) {
        if (!recordId) {
            return;
        }

        const row = findById(state.equipment?.equipmentRows, recordId)
            || findById(state.equipment?.stockRows, recordId)
            || findById(state.equipmentInventory?.records, recordId);
        renderEquipmentDetailLoading(row);

        try {
            const detail = await fetchJson(`${urls.equipmentDetail}?equipmentId=${encodeURIComponent(recordId)}`);
            fillEquipmentDetail(detail);
            clearStatus(equipmentDetailStatus);
        } catch (error) {
            showStatus(equipmentDetailStatus, "error", getErrorMessage(error));
        }
    }

    function fillEquipmentDetail(detail) {
        const equipment = detail?.equipment || {};
        state.equipmentDetail = detail || null;
        equipmentRecordIdInput.value = equipment.recordId || "";
        equipmentClientIdInput.value = equipment.clientId || "";
        equipmentClientNameInput.value = equipment.inStock ? "" : (equipment.clientName || "");
        equipmentSerialInput.value = equipment.serial || "";
        equipmentAreaInput.value = equipment.area || "";
        equipmentSiteInput.value = equipment.site || "";
        equipmentReferenceInput.value = equipment.reference || "";
        equipmentObservationsInput.value = equipment.observations || "";
        equipmentMaintenanceCountInput.value = numberFormatter.format(Number(equipment.maintenanceCount || 0));
        equipmentLastMaintenanceInput.value = equipment.lastMaintenanceDateDisplay || "";
        populateEquipmentCategoryOptions(equipment.categoryValue, detail?.categoryOptions || state.equipment?.categoryOptions);

        equipmentDetailTitle.textContent = equipment.serial ? `Equipo ${equipment.serial}` : "Detalle del equipo";
        equipmentDetailSubtitle.textContent = equipment.inStock
            ? "Este equipo esta en stock. Puedes asignarle un cliente y completar sus datos."
            : "Edita los datos visibles de la tabla y los campos de seguimiento.";
    }

    function resetEquipmentDetail() {
        state.equipmentDetail = null;
        equipmentRecordIdInput.value = "";
        equipmentClientIdInput.value = "";
        equipmentClientNameInput.value = "";
        equipmentSerialInput.value = "";
        equipmentAreaInput.value = "";
        equipmentSiteInput.value = "";
        equipmentReferenceInput.value = "";
        equipmentObservationsInput.value = "";
        equipmentMaintenanceCountInput.value = "";
        equipmentLastMaintenanceInput.value = "";
        equipmentCategorySelect.innerHTML = "";
        equipmentDetailTitle.textContent = "Detalle del equipo";
        equipmentDetailSubtitle.textContent = "Edita la informacion operativa del equipo seleccionado.";
        clearStatus(equipmentDetailStatus);
    }

    function populateEquipmentCategoryOptions(selectedValue, options) {
        const items = Array.isArray(options) ? options : [];
        const selected = selectedValue === null || selectedValue === undefined ? "" : String(selectedValue);
        equipmentCategorySelect.innerHTML = `<option value="">Sin tipo</option>` + items.map((option) => {
            const value = String(option.value ?? "");
            return `<option value="${escapeHtml(value)}" ${value === selected ? "selected" : ""}>${escapeHtml(option.label || value)}</option>`;
        }).join("");
    }

    async function saveEquipmentAssignment() {
        if (state.equipmentAssignmentSaving) {
            return;
        }

        try {
            syncClientSelection(equipmentClientNameInput, equipmentClientIdInput, state.equipmentClientSuggestions);
            const clientName = (equipmentClientNameInput.value || "").trim();
            const serial = (equipmentSerialInput.value || "").trim();
            if (!serial) {
                throw new Error("Debes indicar el serial del equipo.");
            }

            state.equipmentAssignmentSaving = true;
            equipmentSaveBtn.disabled = true;
            showStatus(equipmentDetailStatus, "info", "Guardando equipo...");
            const payload = {
                recordId: equipmentRecordIdInput.value,
                serial,
                clientId: equipmentClientIdInput.value,
                clientName,
                categoryValue: parseNullableInt(equipmentCategorySelect.value),
                reference: equipmentReferenceInput.value || "",
                area: equipmentAreaInput.value || "",
                site: equipmentSiteInput.value || "",
                observations: equipmentObservationsInput.value || ""
            };

            const result = await fetchJson(urls.saveEquipment || urls.equipmentAssignment, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });

            await loadEquipment();
            if (state.equipmentInventory) {
                await loadEquipmentInventory();
            }
            const detail = await fetchJson(`${urls.equipmentDetail}?equipmentId=${encodeURIComponent(result.recordId || payload.recordId)}`);
            fillEquipmentDetail(detail);
            showStatus(equipmentDetailStatus, "success", result.message || "Equipo actualizado correctamente.");
        } catch (error) {
            showStatus(equipmentDetailStatus, "error", getErrorMessage(error));
        } finally {
            state.equipmentAssignmentSaving = false;
            equipmentSaveBtn.disabled = false;
        }
    }

    function findClientSummary(clientId) {
        const normalizedId = (clientId || "").toLowerCase();
        return (state.equipment?.clientSummaries || []).find((row) => (row.clientId || "").toLowerCase() === normalizedId);
    }

    function openClientDetail(row) {
        state.clientDetail = row || null;
        clientDetailIdInput.value = row?.clientId || "";
        clientDetailNameInput.value = row?.clientName || "";
        clientDetailContactInput.value = row?.contactName || "";
        clientDetailEmailInput.value = row?.email || "";
        clientDetailPhoneInput.value = row?.phone || "";
        clientDetailAddressInput.value = row?.address || "";
        clearStatus(clientDetailStatus);
        showModal(clientDetailModal);
    }

    async function saveEquipmentClient() {
        if (state.clientSaving) {
            return;
        }

        const clientId = clientDetailIdInput.value || "";
        const clientName = (clientDetailNameInput.value || "").trim();
        if (!clientId || !clientName) {
            showStatus(clientDetailStatus, "error", "Debes indicar el nombre del cliente.");
            return;
        }

        try {
            state.clientSaving = true;
            clientDetailSaveBtn.disabled = true;
            showStatus(clientDetailStatus, "info", "Guardando cliente...");
            const payload = {
                clientId,
                clientName,
                contactName: clientDetailContactInput.value || "",
                email: clientDetailEmailInput.value || "",
                phone: clientDetailPhoneInput.value || "",
                address: clientDetailAddressInput.value || ""
            };

            const result = await fetchJson(urls.saveEquipmentClient, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });

            if ((inventoryClientIdInput.value || "").toLowerCase() === clientId.toLowerCase()) {
                inventoryClientNameInput.value = clientName;
            }

            await loadEquipment();
            if (state.equipmentInventory) {
                await loadEquipmentInventory();
            }

            showStatus(clientDetailStatus, "success", result.message || "Cliente actualizado correctamente.");
        } catch (error) {
            showStatus(clientDetailStatus, "error", getErrorMessage(error));
        } finally {
            state.clientSaving = false;
            clientDetailSaveBtn.disabled = false;
        }
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
                <tr class="is-selectable" data-supply-id="${escapeHtml(row.recordId)}" tabindex="0">
                    <td data-label="Referencia">${escapeHtml(row.name)}</td>
                    <td data-label="Cantidad" class="text-end">${numberFormatter.format(Number(row.quantity || 0))}</td>
                    <td data-label="Ultima compra">${escapeHtml(row.lastPurchaseDateDisplay || "")}</td>
                    <td data-label="Estado"><span class="copiers-badge ${exhausted ? "is-danger" : "is-good"}">${escapeHtml(row.statusLabel || "")}</span></td>
                </tr>`;
        }).join("");
    }

    function openSupplyModal(row) {
        supplyRecordIdInput.value = row?.recordId || "";
        supplyNameInput.value = row?.name || "";
        supplyQuantityInput.value = formatInputNumber(row?.quantity);
        supplyLastPurchaseInput.value = row?.lastPurchaseDateDisplay || "";
        supplyStatusLabelInput.value = row?.statusLabel || "";
        clearStatus(supplyModalStatus);
        showModal(supplyModal);
    }

    async function saveSupplyQuantity() {
        try {
            setBusy(true);
            supplySaveBtn.disabled = true;
            showStatus(supplyModalStatus, "info", "Guardando cantidad...");

            const result = await fetchJson(urls.saveSupplyQuantity, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    recordId: supplyRecordIdInput.value,
                    quantity: Number(supplyQuantityInput.value || 0)
                })
            });

            closeModal(supplyModal);
            await loadSupplies();
            showStatus(statusBanner, "success", result.message || "Cantidad actualizada.");
        } catch (error) {
            showStatus(supplyModalStatus, "error", getErrorMessage(error));
        } finally {
            supplySaveBtn.disabled = false;
            setBusy(false);
        }
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
                    <td data-label="Factura">${escapeHtml(row.invoiceNumber)}</td>
                    <td data-label="Suministro">${escapeHtml(row.supplyName)}</td>
                    <td data-label="Cantidad" class="text-end">${numberFormatter.format(Number(row.quantity || 0))}</td>
                    <td data-label="Estado"><span class="copiers-badge is-warning">${escapeHtml(row.approvedLabel || "No")}</span></td>
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
                <tr class="is-selectable" data-delivery-id="${escapeHtml(row.recordId)}" tabindex="0">
                    <td data-label="Cliente">${escapeHtml(row.clientName)}</td>
                    <td data-label="Suministro">${escapeHtml(row.supplyName)}</td>
                    <td data-label="Fecha">${escapeHtml(row.deliveryDateDisplay || "")}</td>
                    <td data-label="Cantidad" class="text-end">${numberFormatter.format(Number(row.quantityDelivered || 0))}</td>
                    <td data-label="Estado"><span class="copiers-badge ${completed ? "is-good" : "is-warning"}">${escapeHtml(row.statusLabel || "")}</span></td>
                    <td data-label="Comprobante">${attachment}</td>
                    <td data-label="Owner">${escapeHtml(row.ownerName || "")}</td>
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

        maintenanceModalTitle.textContent = row?.recordId ? "Editar mantenimiento" : "Nuevo mantenimiento";
        maintenanceRecordIdInput.value = row?.recordId || "";
        maintenanceTitleInput.value = row?.title || "";
        maintenanceClientIdInput.value = row?.clientId || "";
        maintenanceClientNameInput.value = row?.clientName || "";
        populateMaintenanceOptions(row?.equipmentId || "");
        maintenanceDateInput.value = row?.dateValue || todayValue();
        maintenanceTypeSelect.value = row?.maintenanceTypeValue || "";
        maintenanceStatusSelect.value = row?.maintenanceStatusValue ? String(row.maintenanceStatusValue) : String(maintenanceStatusPending);
        maintenanceDescriptionInput.value = row?.description || "";
        maintenanceFileInput.value = "";
        clearStatus(maintenanceModalStatus);
        showModal(maintenanceModal);
    }

    function populateMaintenanceOptions(selectedEquipmentId = "") {
        const typeOptions = Array.isArray(state.maintenance?.typeOptions) ? state.maintenance.typeOptions : [];
        maintenanceTypeSelect.innerHTML = `<option value="">Sin tipo</option>` + typeOptions.map((option) => (
            `<option value="${option.value}">${escapeHtml(option.label)}</option>`
        )).join("");

        const statusOptions = Array.isArray(state.maintenance?.statusOptions) && state.maintenance.statusOptions.length
            ? state.maintenance.statusOptions
            : fallbackMaintenanceStatusOptions;
        maintenanceStatusSelect.innerHTML = statusOptions.map((option) => (
            `<option value="${option.value}">${escapeHtml(option.label)}</option>`
        )).join("");

        updateMaintenanceEquipmentOptions(selectedEquipmentId);
    }

    function updateMaintenanceEquipmentOptions(selectedEquipmentId = "") {
        if (!maintenanceEquipmentSelect) {
            return;
        }

        const allEquipmentRows = Array.isArray(state.equipment?.equipmentRows) ? state.equipment.equipmentRows : [];
        const clientId = maintenanceClientIdInput.value || "";
        const clientName = normalizeText(maintenanceClientNameInput.value);
        let rows = allEquipmentRows.filter((row) => {
            if (clientId) {
                return (row.clientId || "") === clientId;
            }

            if (clientName) {
                return normalizeText(row.clientName) === clientName;
            }

            return false;
        });

        if (selectedEquipmentId && !rows.some((row) => row.recordId === selectedEquipmentId)) {
            const selected = findById(allEquipmentRows, selectedEquipmentId);
            if (selected) {
                rows = [selected, ...rows];
            }
        }

        const placeholder = clientId || clientName
            ? "Selecciona un equipo"
            : "Selecciona primero un cliente";
        maintenanceEquipmentSelect.disabled = !(clientId || clientName);
        maintenanceEquipmentSelect.innerHTML = `<option value="">${placeholder}</option>` + rows.map((row) => {
            const label = `${row.serial || "Equipo"}${row.reference ? " - " + row.reference : ""}`;
            return `<option value="${escapeHtml(row.recordId)}">${escapeHtml(label)}</option>`;
        }).join("");

        maintenanceEquipmentSelect.value = selectedEquipmentId || "";
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
                internalId: "",
                equipmentId: maintenanceEquipmentSelect.value,
                clientId: maintenanceClientIdInput.value,
                clientName: maintenanceClientNameInput.value,
                dateValue: maintenanceDateInput.value,
                description: maintenanceDescriptionInput.value,
                maintenanceTypeValue: maintenanceTypeSelect.value ? Number(maintenanceTypeSelect.value) : null,
                maintenanceStatusValue: maintenanceStatusSelect.value ? Number(maintenanceStatusSelect.value) : maintenanceStatusPending
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

    async function openDeliveryModal(row) {
        if (!state.supplies) {
            await loadSupplies();
        }

        if (!state.deliveries) {
            await loadDeliveries();
        }

        populateDeliveryOptions(row?.supplyId || "");
        deliveryModalTitle.textContent = row?.recordId ? "Editar entrega" : "Nueva entrega";
        deliveryRecordIdInput.value = row?.recordId || "";
        deliveryClientIdInput.value = row?.clientId || "";
        deliveryClientNameInput.value = row?.clientName || "";
        deliverySupplySelect.value = row?.supplyId || "";
        deliveryDateInput.value = row?.deliveryDateValue || todayValue();
        deliveryQuantityInput.value = row?.recordId ? formatInputNumber(row.quantityDelivered) : "";
        deliveryStatusSelect.value = row?.statusValue ? String(row.statusValue) : "645250000";
        deliveryFileInput.value = "";
        clearStatus(deliveryModalStatus);
        showModal(deliveryModal);
    }

    function populateDeliveryOptions(selectedSupplyId = "") {
        const supplies = Array.isArray(state.supplies?.records) ? state.supplies.records : [];
        const deliveryRecords = Array.isArray(state.deliveries?.records) ? state.deliveries.records : [];
        const selectedDeliverySupply = selectedSupplyId && !supplies.some((row) => row.recordId === selectedSupplyId)
            ? deliveryRecords.find((row) => row.supplyId === selectedSupplyId)
            : null;
        const rows = selectedDeliverySupply
            ? [{ recordId: selectedDeliverySupply.supplyId, name: selectedDeliverySupply.supplyName, quantity: 0 }, ...supplies]
            : supplies;

        deliverySupplySelect.innerHTML = `<option value="">Selecciona un suministro</option>` + rows.map((row) => (
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
                    recordId: deliveryRecordIdInput.value,
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

        try {
            const suggestions = await fetchJson(`${urls.clientSearch}?q=${encodeURIComponent(query)}&top=5000`);
            if (target === "delivery") {
                state.deliveryClientSuggestions = Array.isArray(suggestions) ? suggestions : [];
            } else if (target === "equipmentInventory") {
                state.equipmentInventoryClientSuggestions = Array.isArray(suggestions) ? suggestions : [];
            } else if (target === "equipment") {
                state.equipmentClientSuggestions = Array.isArray(suggestions) ? suggestions : [];
            } else {
                state.maintenanceClientSuggestions = Array.isArray(suggestions) ? suggestions : [];
            }

            datalist.innerHTML = (Array.isArray(suggestions) ? suggestions : []).map((item) => (
                `<option value="${escapeHtml(item.name || "")}"></option>`
            )).join("");
        } catch {
            datalist.innerHTML = "";
            if (target === "delivery") {
                state.deliveryClientSuggestions = [];
            } else if (target === "equipmentInventory") {
                state.equipmentInventoryClientSuggestions = [];
            } else if (target === "equipment") {
                state.equipmentClientSuggestions = [];
            } else {
                state.maintenanceClientSuggestions = [];
            }
        }
    }

    function syncClientSelection(input, hiddenInput, suggestions) {
        const value = normalizeText(input.value);
        const match = (suggestions || []).find((item) => normalizeText(item.name) === value);
        hiddenInput.value = match?.id || hiddenInput.value || "";
    }

    function buildMissingColumnsText(missingColumns) {
        const columns = (missingColumns || [])
            .map((column) => `${column.label || "Columna"} (${column.logicalName || ""})`)
            .join(", ");
        return columns
            ? `Faltan columnas en Dataverse para completar el inventario: ${columns}.`
            : "";
    }

    function renderAddressMapFrame(address) {
        const value = (address || "").trim();
        if (!value) {
            return "";
        }

        const url = `https://www.google.com/maps?q=${encodeURIComponent(value)}&output=embed`;
        return `<iframe class="copiers-map-frame" src="${escapeHtml(url)}" loading="lazy" referrerpolicy="no-referrer-when-downgrade"></iframe>`;
    }

    function renderMapLink(url, label) {
        if (!isSafeMapUrl(url)) {
            return `<span class="copiers-muted">Sin mapa</span>`;
        }

        return `<a class="copiers-link" href="${escapeHtml(url)}" target="_blank" rel="noopener">${escapeHtml(label || "Abrir mapa")}</a>`;
    }

    function renderMapFrame(url) {
        if (!isTrustedMapEmbedUrl(url)) {
            return "";
        }

        return `<iframe class="copiers-map-frame" src="${escapeHtml(url)}" loading="lazy" referrerpolicy="no-referrer-when-downgrade"></iframe>`;
    }

    function isTrustedMapEmbedUrl(url) {
        if (!isSafeMapUrl(url)) {
            return false;
        }

        try {
            const parsed = new URL(url);
            return parsed.hostname.toLowerCase().endsWith("google.com")
                && parsed.pathname.toLowerCase().startsWith("/maps/embed");
        } catch {
            return false;
        }
    }

    function isSafeMapUrl(url) {
        if (!url) {
            return false;
        }

        try {
            const parsed = new URL(url);
            const host = parsed.hostname.toLowerCase();
            const isGoogleHost = host === "maps.app.goo.gl"
                || host === "google.com"
                || host.endsWith(".google.com")
                || host === "google.com.co"
                || host.endsWith(".google.com.co");
            return (parsed.protocol === "https:" || parsed.protocol === "http:") && isGoogleHost;
        } catch {
            return false;
        }
    }

    async function uploadFile(baseUrl, idParamName, id, file) {
        const form = new FormData();
        form.append("file", file, toAsciiFileName(file?.name || "archivo"));
        return await fetchJson(`${baseUrl}?${encodeURIComponent(idParamName)}=${encodeURIComponent(id)}`, {
            method: "POST",
            body: form
        });
    }

    function toAsciiFileName(fileName) {
        const normalized = String(fileName || "archivo")
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "");
        const ascii = normalized
            .replace(/[^\x20-\x7E]/g, "")
            .replace(/["\\/:*?<>|]+/g, "-")
            .trim();

        return ascii || "archivo";
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

    function formatInputNumber(value) {
        if (value === null || value === undefined || value === "") {
            return "";
        }

        const numeric = Number(value);
        return Number.isFinite(numeric) ? String(numeric) : "";
    }

    function parseNullableInt(value) {
        if (value === null || value === undefined || value === "") {
            return null;
        }

        const numeric = Number(value);
        return Number.isInteger(numeric) ? numeric : null;
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
