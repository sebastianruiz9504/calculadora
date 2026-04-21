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
        equipmentAssignment: app.dataset.equipmentAssignmentUrl || "",
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
    const equipmentDetailSerial = document.getElementById("copiersEquipmentDetailSerial");
    const equipmentDetailCurrentClient = document.getElementById("copiersEquipmentDetailCurrentClient");
    const equipmentDetailCategory = document.getElementById("copiersEquipmentDetailCategory");
    const equipmentDetailReference = document.getElementById("copiersEquipmentDetailReference");
    const equipmentDetailObservations = document.getElementById("copiersEquipmentDetailObservations");
    const equipmentMaintenanceBody = document.getElementById("copiersEquipmentMaintenanceBody");

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
        equipmentSerialSearch: "",
        equipmentDetail: null,
        equipmentClientSuggestions: [],
        equipmentAssignmentSaving: false,
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

    maintenanceForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await saveMaintenance();
    });

    equipmentAssignmentForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await saveEquipmentAssignment();
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

        [confirmIngresoModal, ingresoModal, equipmentDetailModal, maintenanceModal, deliveryModal].forEach((modal) => {
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

    maintenanceClientNameInput?.addEventListener("change", () => {
        syncClientSelection(maintenanceClientNameInput, maintenanceClientIdInput, state.maintenanceClientSuggestions);
        updateMaintenanceEquipmentOptions();
    });

    equipmentClientNameInput?.addEventListener("input", debounce(async () => {
        equipmentClientIdInput.value = "";
        await updateClientSuggestions(equipmentClientNameInput.value, equipmentClientOptions, "equipment");
    }, 250));

    equipmentClientNameInput?.addEventListener("change", () => {
        syncClientSelection(equipmentClientNameInput, equipmentClientIdInput, state.equipmentClientSuggestions);
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

        equipmentBody.innerHTML = equipmentRows.length ? equipmentRows.map((row) => `
            <tr class="is-selectable" data-equipment-id="${escapeHtml(row.recordId || "")}" tabindex="0">
                <td>${escapeHtml(row.serial)}</td>
                <td>${row.inStock ? '<span class="copiers-badge is-warning">Stock</span>' : escapeHtml(row.clientName || "Sin cliente")}</td>
                <td>${escapeHtml(row.categoryLabel || "")}</td>
                <td>${escapeHtml(row.reference || "")}</td>
                <td>${escapeHtml(row.observations || "")}</td>
                <td class="text-end">${numberFormatter.format(Number(row.maintenanceCount || 0))}</td>
                <td>${escapeHtml(row.lastMaintenanceDateDisplay || "")}</td>
            </tr>`).join("") : `<tr><td colspan="7" class="text-center copiers-muted">No hay equipos para mostrar.</td></tr>`;
    }

    function renderEquipmentDetailLoading(row) {
        resetEquipmentDetail();
        showModal(equipmentDetailModal);
        equipmentDetailTitle.textContent = row?.serial ? `Equipo ${row.serial}` : "Detalle del equipo";
        equipmentDetailSubtitle.textContent = "Cargando informacion del equipo y sus mantenimientos...";
        equipmentMaintenanceBody.innerHTML = `<tr><td colspan="8" class="text-center copiers-muted">Cargando historial del equipo...</td></tr>`;
        showStatus(equipmentDetailStatus, "info", "Consultando detalle del equipo...");
    }

    async function loadEquipmentDetail(recordId) {
        if (!recordId) {
            return;
        }

        const row = findById(state.equipment?.equipmentRows, recordId);
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

        equipmentDetailTitle.textContent = equipment.serial ? `Equipo ${equipment.serial}` : "Detalle del equipo";
        equipmentDetailSubtitle.textContent = equipment.inStock
            ? "Este equipo esta en stock. Asignale un cliente para dejarlo operativo."
            : "Revisa la informacion del equipo y actualiza solo el cliente asignado.";
        equipmentDetailSerial.textContent = equipment.serial || "Sin serial";
        equipmentDetailCurrentClient.textContent = equipment.inStock ? "Stock" : (equipment.clientName || "Sin cliente");
        equipmentDetailCategory.textContent = equipment.categoryLabel || "Sin categoria";
        equipmentDetailReference.textContent = equipment.reference || "Sin referencia";
        equipmentDetailObservations.textContent = equipment.observations || "Sin observaciones";
        renderEquipmentMaintenanceTable(detail?.maintenanceRows);
    }

    function resetEquipmentDetail() {
        state.equipmentDetail = null;
        equipmentRecordIdInput.value = "";
        equipmentClientIdInput.value = "";
        equipmentClientNameInput.value = "";
        equipmentDetailTitle.textContent = "Detalle del equipo";
        equipmentDetailSubtitle.textContent = "Consulta el equipo, reasigna el cliente y revisa sus mantenimientos.";
        equipmentDetailSerial.textContent = "-";
        equipmentDetailCurrentClient.textContent = "-";
        equipmentDetailCategory.textContent = "-";
        equipmentDetailReference.textContent = "-";
        equipmentDetailObservations.textContent = "-";
        clearStatus(equipmentDetailStatus);
        equipmentMaintenanceBody.innerHTML = `<tr><td colspan="8" class="text-center copiers-muted">Selecciona un equipo para ver sus mantenimientos.</td></tr>`;
    }

    function renderEquipmentMaintenanceTable(rows) {
        const items = Array.isArray(rows) ? rows : [];
        equipmentMaintenanceBody.innerHTML = items.length ? items.map((row) => {
            const attachment = row.hasAttachment
                ? `<a class="copiers-link" href="${buildDownloadUrl(urls.downloadMaintenance, "maintenanceId", row.recordId)}" target="_blank" rel="noopener">${escapeHtml(row.attachmentFileName || "Descargar")}</a>`
                : `<span class="copiers-muted">Sin adjunto</span>`;

            return `
                <tr>
                    <td>${escapeHtml(row.dateDisplay || "")}</td>
                    <td>${escapeHtml(row.title || "")}</td>
                    <td>${escapeHtml(row.maintenanceTypeLabel || "")}</td>
                    <td>${escapeHtml(row.clientName || "Sin cliente")}</td>
                    <td>${escapeHtml(row.technicianName || "")}</td>
                    <td>${escapeHtml(row.description || "")}</td>
                    <td>${escapeHtml(row.internalId || "")}</td>
                    <td>${attachment}</td>
                </tr>`;
        }).join("") : `<tr><td colspan="8" class="text-center copiers-muted">Este equipo no tiene mantenimientos registrados.</td></tr>`;
    }

    async function saveEquipmentAssignment() {
        if (state.equipmentAssignmentSaving) {
            return;
        }

        try {
            syncClientSelection(equipmentClientNameInput, equipmentClientIdInput, state.equipmentClientSuggestions);
            const clientName = (equipmentClientNameInput.value || "").trim();
            if (!clientName) {
                throw new Error("Debes seleccionar el cliente al que quedara asignado el equipo.");
            }

            state.equipmentAssignmentSaving = true;
            equipmentSaveBtn.disabled = true;
            showStatus(equipmentDetailStatus, "info", "Guardando reasignacion...");
            const payload = {
                recordId: equipmentRecordIdInput.value,
                clientId: equipmentClientIdInput.value,
                clientName,
                moveToStock: false
            };

            const result = await fetchJson(urls.equipmentAssignment, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });

            await loadEquipment();
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

        maintenanceRecordIdInput.value = row?.recordId || "";
        maintenanceTitleInput.value = row?.title || "";
        maintenanceClientIdInput.value = row?.clientId || "";
        maintenanceClientNameInput.value = row?.clientName || "";
        populateMaintenanceOptions(row?.equipmentId || "");
        maintenanceDateInput.value = row?.dateValue || todayValue();
        maintenanceTypeSelect.value = row?.maintenanceTypeValue || "";
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
            if (target === "delivery") {
                state.deliveryClientSuggestions = [];
            } else if (target === "equipment") {
                state.equipmentClientSuggestions = [];
            } else {
                state.maintenanceClientSuggestions = [];
            }
            return;
        }

        try {
            const suggestions = await fetchJson(`${urls.clientSearch}?q=${encodeURIComponent(query)}`);
            if (target === "delivery") {
                state.deliveryClientSuggestions = Array.isArray(suggestions) ? suggestions : [];
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
