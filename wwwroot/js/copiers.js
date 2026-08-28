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
        preventiveMaintenance: app.dataset.preventiveMaintenanceUrl || "",
        preventiveMaintenanceFrequency: app.dataset.preventiveMaintenanceFrequencyUrl || "",
        schedulePreventiveMaintenance: app.dataset.schedulePreventiveMaintenanceUrl || "",
        calendarConsent: app.dataset.calendarConsentUrl || "",
        saveCounter: app.dataset.saveCounterUrl || "",
        uploadCounter: app.dataset.uploadCounterUrl || "",
        equipment: app.dataset.equipmentUrl || "",
        equipmentDetail: app.dataset.equipmentDetailUrl || "",
        equipmentInventory: app.dataset.equipmentInventoryUrl || "",
        equipmentBackupAssignment: app.dataset.equipmentBackupAssignmentUrl || "",
        equipmentAssignment: app.dataset.equipmentAssignmentUrl || "",
        saveEquipment: app.dataset.saveEquipmentUrl || "",
        registerEquipmentMovement: app.dataset.registerEquipmentMovementUrl || "",
        uploadEquipmentMovement: app.dataset.uploadEquipmentMovementUrl || "",
        downloadEquipmentMovement: app.dataset.downloadEquipmentMovementUrl || "",
        billingDays: app.dataset.billingDaysUrl || "",
        equipmentMovements: app.dataset.equipmentMovementsUrl || "",
        lineEquipmentAssignment: app.dataset.lineEquipmentAssignmentUrl || "",
        saveLineEquipmentAssignment: app.dataset.lineEquipmentAssignmentSaveUrl || "",
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
    const externalEquipmentValue = "__external__";

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

    const preventiveRefreshBtn = document.getElementById("copiersPreventiveRefreshBtn");
    const preventiveBody = document.getElementById("copiersPreventiveBody");
    const preventiveCount = document.getElementById("copiersPreventiveCount");
    const preventiveEmpty = document.getElementById("copiersPreventiveEmpty");
    const preventiveScheduleModal = document.getElementById("copiersPreventiveScheduleModal");
    const preventiveScheduleForm = document.getElementById("copiersPreventiveScheduleForm");
    const preventiveScheduleSubtitle = document.getElementById("copiersPreventiveScheduleSubtitle");
    const preventiveScheduleStatus = document.getElementById("copiersPreventiveScheduleStatus");
    const preventiveScheduleClientIdInput = document.getElementById("copiersPreventiveScheduleClientId");
    const preventiveScheduleClientNameInput = document.getElementById("copiersPreventiveScheduleClientName");
    const preventiveScheduleClientDisplayInput = document.getElementById("copiersPreventiveScheduleClientDisplay");
    const preventiveScheduleDateInput = document.getElementById("copiersPreventiveScheduleDate");
    const preventiveScheduleTimeInput = document.getElementById("copiersPreventiveScheduleTime");
    const preventiveScheduleDurationInput = document.getElementById("copiersPreventiveScheduleDuration");
    const preventiveScheduleSaveBtn = document.getElementById("copiersPreventiveScheduleSaveBtn");
    const preventivePeriodButtons = Array.from(document.querySelectorAll("[data-copiers-preventive-period]"));
    const counterModal = document.getElementById("copiersCounterModal");
    const counterForm = document.getElementById("copiersCounterForm");
    const counterStatus = document.getElementById("copiersCounterStatus");
    const counterEquipmentIdInput = document.getElementById("copiersCounterEquipmentId");
    const counterEquipmentNameInput = document.getElementById("copiersCounterEquipmentName");
    const counterCopiesInput = document.getElementById("copiersCounterCopies");
    const counterScansInput = document.getElementById("copiersCounterScans");
    const counterDateInput = document.getElementById("copiersCounterDate");
    const counterFileInput = document.getElementById("copiersCounterFile");
    const counterSaveBtn = document.getElementById("copiersCounterSaveBtn");

    const billingDaysRefreshBtn = document.getElementById("copiersBillingDaysRefreshBtn");
    const billingDaysCount = document.getElementById("copiersBillingDaysCount");
    const billingDaysBody = document.getElementById("copiersBillingDaysBody");
    const billingDaysEmpty = document.getElementById("copiersBillingDaysEmpty");
    const movementsRefreshBtn = document.getElementById("copiersMovementsRefreshBtn");
    const movementsCount = document.getElementById("copiersMovementsCount");
    const movementsSearchInput = document.getElementById("copiersMovementsSearch");
    const movementsBody = document.getElementById("copiersMovementsBody");
    const movementsEmpty = document.getElementById("copiersMovementsEmpty");
    const movementSortButtons = Array.from(document.querySelectorAll("[data-copiers-movement-sort]"));
    const lineEquipmentModal = document.getElementById("copiersLineEquipmentModal");
    const lineEquipmentStatus = document.getElementById("copiersLineEquipmentStatus");
    const lineEquipmentTitle = document.getElementById("copiersLineEquipmentTitle");
    const lineEquipmentSubtitle = document.getElementById("copiersLineEquipmentSubtitle");
    const lineEquipmentSummary = document.getElementById("copiersLineEquipmentSummary");
    const lineEquipmentAssignedCount = document.getElementById("copiersLineEquipmentAssignedCount");
    const lineEquipmentAvailableCount = document.getElementById("copiersLineEquipmentAvailableCount");
    const lineEquipmentAssignedBody = document.getElementById("copiersLineEquipmentAssignedBody");
    const lineEquipmentAvailableBody = document.getElementById("copiersLineEquipmentAvailableBody");
    const lineEquipmentSaveBtn = document.getElementById("copiersLineEquipmentSaveBtn");

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
    const equipmentMaintenanceBody = document.getElementById("copiersEquipmentMaintenanceBody");
    const equipmentMovementsBody = document.getElementById("copiersEquipmentMovementsBody");
    const registerMovementBtn = document.getElementById("copiersRegisterMovementBtn");
    const equipmentMovementModal = document.getElementById("copiersEquipmentMovementModal");
    const equipmentMovementForm = document.getElementById("copiersEquipmentMovementForm");
    const equipmentMovementTitle = document.getElementById("copiersEquipmentMovementTitle");
    const equipmentMovementSubtitle = document.getElementById("copiersEquipmentMovementSubtitle");
    const equipmentMovementStatus = document.getElementById("copiersEquipmentMovementStatus");
    const movementEquipmentIdInput = document.getElementById("copiersMovementEquipmentId");
    const movementClientIdInput = document.getElementById("copiersMovementClientId");
    const movementClientNameInput = document.getElementById("copiersMovementClientName");
    const movementClientOptions = document.getElementById("copiersMovementClientOptions");
    const movementDateInput = document.getElementById("copiersMovementDate");
    const movementReasonInput = document.getElementById("copiersMovementReason");
    const movementFileInput = document.getElementById("copiersMovementFile");
    const movementSaveBtn = document.getElementById("copiersMovementSaveBtn");
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
    const inventoryContractLines = document.getElementById("copiersInventoryContractLines");
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
        preventiveMaintenance: null,
        preventivePeriod: "this-month",
        preventiveExpandedClients: new Set(),
        preventiveFrequencySavingKeys: new Set(),
        canEditPreventiveFrequency: app.dataset.canEditPreventiveFrequency === "true",
        counterSaving: false,
        scheduleSaving: false,
        billingDays: null,
        billingDaysExpandedGroups: new Set(),
        movements: null,
        movementsSearchTerm: "",
        movementsSortKey: "dateValue",
        movementsSortDirection: "desc",
        lineEquipmentDetail: null,
        lineEquipmentDraftIds: new Set(),
        lineEquipmentSaving: false,
        equipment: null,
        equipmentSerialSearch: "",
        equipmentDetail: null,
        equipmentClientSuggestions: [],
        equipmentAssignmentSaving: false,
        movementClientSuggestions: [],
        movementSaving: false,
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
    const maintenanceTypePreventive = 645250001;
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
    preventiveRefreshBtn?.addEventListener("click", () => loadPreventiveMaintenance());
    preventivePeriodButtons.forEach((button) => {
        button.addEventListener("click", () => {
            const period = button.dataset.copiersPreventivePeriod === "previous-month" ? "previous-month" : "this-month";
            if (state.preventivePeriod === period) {
                return;
            }

            state.preventivePeriod = period;
            state.preventiveExpandedClients.clear();
            updatePreventivePeriodButtons();
            loadPreventiveMaintenance();
        });
    });
    billingDaysRefreshBtn?.addEventListener("click", () => loadBillingDays());
    movementsRefreshBtn?.addEventListener("click", () => loadMovements());
    equipmentRefreshBtn?.addEventListener("click", () => loadEquipment());
    inventoryLoadBtn?.addEventListener("click", () => loadEquipmentInventory());
    inventoryClearBtn?.addEventListener("click", clearEquipmentInventory);
    suppliesRefreshBtn?.addEventListener("click", () => loadSupplies());
    deliveriesRefreshBtn?.addEventListener("click", () => loadDeliveries());

    equipmentSerialSearch?.addEventListener("input", () => {
        state.equipmentSerialSearch = equipmentSerialSearch.value || "";
        renderEquipment();
    });

    movementsSearchInput?.addEventListener("input", () => {
        state.movementsSearchTerm = movementsSearchInput.value || "";
        renderMovements();
    });

    movementSortButtons.forEach((button) => {
        button.addEventListener("click", () => {
            const key = button.dataset.copiersMovementSort || "dateValue";
            if (state.movementsSortKey === key) {
                state.movementsSortDirection = state.movementsSortDirection === "desc" ? "asc" : "desc";
            } else {
                state.movementsSortKey = key;
                state.movementsSortDirection = key === "dateValue" ? "desc" : "asc";
            }

            renderMovements();
        });
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

    preventiveBody?.addEventListener("click", async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const frequencyButton = target.closest("[data-preventive-frequency]");
        if (frequencyButton instanceof HTMLElement) {
            await togglePreventiveFrequency(frequencyButton.dataset.preventiveFrequency || "");
            return;
        }

        const toggleButton = target.closest("[data-preventive-toggle]");
        if (toggleButton instanceof HTMLElement) {
            const clientKey = toggleButton.dataset.preventiveToggle || "";
            if (state.preventiveExpandedClients.has(clientKey)) {
                state.preventiveExpandedClients.delete(clientKey);
            } else {
                state.preventiveExpandedClients.add(clientKey);
            }

            renderPreventiveMaintenance();
            return;
        }

        const scheduleButton = target.closest("[data-preventive-schedule]");
        if (scheduleButton instanceof HTMLElement) {
            const client = findPreventiveClient(scheduleButton.dataset.preventiveSchedule || "");
            if (client) {
                openPreventiveScheduleModal(client);
            }
            return;
        }

        const maintenanceButton = target.closest("[data-preventive-maintenance-equipment]");
        if (maintenanceButton instanceof HTMLElement) {
            const client = findPreventiveClient(maintenanceButton.dataset.preventiveClient || "");
            const equipment = findPreventiveEquipment(client, maintenanceButton.dataset.preventiveMaintenanceEquipment || "");
            if (client && equipment) {
                await openPreventiveMaintenanceModal(client, equipment);
            }
            return;
        }

        const counterButton = target.closest("[data-preventive-counter-equipment]");
        if (counterButton instanceof HTMLElement) {
            const client = findPreventiveClient(counterButton.dataset.preventiveClient || "");
            const equipment = findPreventiveEquipment(client, counterButton.dataset.preventiveCounterEquipment || "");
            if (client && equipment) {
                openCounterModal(client, equipment);
            }
        }
    });

    billingDaysBody?.addEventListener("click", async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const assignmentButton = target.closest("[data-billing-line-assignment]");
        if (assignmentButton instanceof HTMLElement) {
            const row = findBillingDayLine(assignmentButton.dataset.billingLineAssignment || "");
            if (row) {
                await loadLineEquipmentAssignment(row);
            }
            return;
        }

        const toggleButton = target.closest("[data-billing-group-toggle]");
        if (toggleButton instanceof HTMLElement) {
            const groupId = toggleButton.dataset.billingGroupToggle || "";
            if (state.billingDaysExpandedGroups.has(groupId)) {
                state.billingDaysExpandedGroups.delete(groupId);
            } else {
                state.billingDaysExpandedGroups.add(groupId);
            }

            renderBillingDays();
        }
    });

    lineEquipmentSaveBtn?.addEventListener("click", saveLineEquipmentAssignment);

    lineEquipmentAssignedBody?.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const button = target.closest("[data-line-equipment-remove]");
        if (button instanceof HTMLElement) {
            removeLineEquipment(button.dataset.lineEquipmentRemove || "");
        }
    });

    lineEquipmentAvailableBody?.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const button = target.closest("[data-line-equipment-assign]");
        if (button instanceof HTMLElement) {
            assignLineEquipment(button.dataset.lineEquipmentAssign || "");
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
    registerMovementBtn?.addEventListener("click", openEquipmentMovementModal);

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

        const backupButton = target.closest("[data-inventory-backup-toggle]");
        if (backupButton instanceof HTMLElement) {
            event.preventDefault();
            event.stopPropagation();
            await saveEquipmentBackupAssignment(
                backupButton.dataset.equipmentId || "",
                backupButton.dataset.isBackup !== "true");
            return;
        }

        const rowElement = target.closest("[data-equipment-id]");
        if (!(rowElement instanceof HTMLElement)) {
            return;
        }

        await loadEquipmentDetail(rowElement.dataset.equipmentId || "");
    });

    inventoryLocations?.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const cardElement = target.closest("[data-inventory-client-id]");
        if (!(cardElement instanceof HTMLElement)) {
            return;
        }

        const inventory = state.equipmentInventory || {};
        openClientDetail({
            clientId: inventory.clientId || cardElement.dataset.inventoryClientId || "",
            clientName: inventory.clientName || inventoryClientNameInput.value || "",
            contactName: inventory.clientContactName || "",
            email: inventory.clientEmail || "",
            phone: inventory.clientPhone || "",
            address: inventory.clientAddress || ""
        });
    });

    maintenanceForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await saveMaintenance();
    });

    preventiveScheduleForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await schedulePreventiveMaintenance();
    });

    counterForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await saveCounter();
    });

    equipmentAssignmentForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await saveEquipmentAssignment();
    });

    equipmentMovementForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await saveEquipmentMovement();
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
        } else if (closeTarget === "preventiveSchedule") {
            closeModal(preventiveScheduleModal);
        } else if (closeTarget === "counter") {
            closeModal(counterModal);
        } else if (closeTarget === "lineEquipment") {
            closeLineEquipmentModal();
        } else if (closeTarget === "equipmentDetail") {
            closeModal(equipmentDetailModal);
        } else if (closeTarget === "equipmentMovement") {
            closeModal(equipmentMovementModal);
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

        [confirmIngresoModal, ingresoModal, supplyModal, counterModal, lineEquipmentModal, preventiveScheduleModal, equipmentMovementModal, clientDetailModal, equipmentDetailModal, maintenanceModal, deliveryModal].forEach((modal) => {
            if (modal && !modal.hidden) {
                if (modal === lineEquipmentModal) {
                    closeLineEquipmentModal();
                } else {
                    closeModal(modal);
                }
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

    movementClientNameInput?.addEventListener("input", debounce(async () => {
        movementClientIdInput.value = "";
        await updateClientSuggestions(movementClientNameInput.value, movementClientOptions, "movement");
    }, 250));

    movementClientNameInput?.addEventListener("focus", () => {
        updateClientSuggestions(movementClientNameInput.value, movementClientOptions, "movement");
    });

    movementClientNameInput?.addEventListener("change", () => {
        syncClientSelection(movementClientNameInput, movementClientIdInput, state.movementClientSuggestions);
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
        } else if (tab === "preventiveMaintenance" && !state.preventiveMaintenance) {
            await loadPreventiveMaintenance();
        } else if (tab === "billingDays" && !state.billingDays) {
            await loadBillingDays();
        } else if (tab === "movements" && !state.movements) {
            await loadMovements();
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
                    <td data-label="Equipo">${escapeHtml(row.equipmentSerial || "Equipo externo")}</td>
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

    async function loadPreventiveMaintenance() {
        try {
            setBusy(true);
            showStatus(statusBanner, "info", "Cargando mantenimientos preventivos...");
            const url = new URL(urls.preventiveMaintenance, window.location.origin);
            url.searchParams.set("period", state.preventivePeriod || "this-month");
            state.preventiveMaintenance = await fetchJson(url.toString());
            state.preventivePeriod = state.preventiveMaintenance?.periodFilter || state.preventivePeriod;
            state.canEditPreventiveFrequency = Boolean(state.preventiveMaintenance?.canEditMaintenanceFrequency);
            renderPreventiveMaintenance();
            clearStatus(statusBanner);
        } catch (error) {
            showStatus(statusBanner, "error", getErrorMessage(error));
        } finally {
            setBusy(false);
        }
    }

    function renderPreventiveMaintenance() {
        const clients = Array.isArray(state.preventiveMaintenance?.clients) ? state.preventiveMaintenance.clients : [];
        updatePreventivePeriodButtons();
        if (preventiveCount) {
            preventiveCount.textContent = `${clients.length} cliente${clients.length === 1 ? "" : "s"}`;
        }

        if (preventiveEmpty) {
            preventiveEmpty.hidden = clients.length > 0;
        }

        if (!preventiveBody) {
            return;
        }

        preventiveBody.innerHTML = clients.length ? clients.map((client) => {
            const clientKey = client.clientKey || client.clientId || client.clientName || "";
            const expanded = state.preventiveExpandedClients.has(clientKey);
            const statusLabel = client.monthlyStatusLabel || "Pendiente";
            const scheduleLabel = client.scheduleButtonLabel || "Programar mantenimiento";
            const scheduleDisabled = Boolean(client.scheduleButtonDisabled);
            const scheduledHint = client.scheduledDateDisplay ? ` - ${client.scheduledDateDisplay}` : "";
            const frequencyLabel = client.maintenanceFrequencyLabel || "Mensual";
            const frequencyClass = client.isBimonthlyMaintenance ? "is-bimonthly" : "is-monthly";
            const canEditFrequency = Boolean(state.canEditPreventiveFrequency && client.clientId);
            const frequencySaving = state.preventiveFrequencySavingKeys.has(clientKey);
            const frequencyTitle = canEditFrequency
                ? `Cambiar periodicidad. Actual: ${frequencyLabel}`
                : `Periodicidad ${frequencyLabel}. Solo lectura.`;
            const clientCity = (client.clientCity || "").trim();
            return `
                <tr class="copiers-preventive-client-row ${expanded ? "is-expanded" : ""}">
                    <td data-label="Cliente">
                        <div class="copiers-preventive-client-main">
                            <button type="button" class="copiers-preventive-toggle" data-preventive-toggle="${escapeHtml(clientKey)}" aria-expanded="${expanded ? "true" : "false"}">
                                <span class="copiers-preventive-toggle__icon">${expanded ? "-" : "+"}</span>
                                <strong>${escapeHtml(client.clientName || "Sin cliente")}</strong>
                            </button>
                            <div class="copiers-preventive-client-meta">
                                <button type="button" class="copiers-frequency-switch ${frequencyClass} ${canEditFrequency ? "is-editable" : "is-readonly"} ${frequencySaving ? "is-saving" : ""}" data-preventive-frequency="${escapeHtml(clientKey)}" role="switch" aria-checked="${client.isBimonthlyMaintenance ? "true" : "false"}" aria-label="Periodicidad ${escapeHtml(frequencyLabel)}" title="${escapeHtml(frequencyTitle)}" ${canEditFrequency && !frequencySaving ? "" : "disabled"}>
                                    <span>Mensual</span>
                                    <span>Bimensual</span>
                                </button>
                                ${clientCity ? `<span class="copiers-client-city" title="Ciudad">${escapeHtml(clientCity)}</span>` : ""}
                            </div>
                        </div>
                    </td>
                    <td data-label="Acciones" class="text-end">
                        <button type="button" class="btn btn-sm copiers-preventive-action ${getPreventiveActionClass(client.scheduleButtonTone || "primary")}" data-preventive-schedule="${escapeHtml(clientKey)}" title="${escapeHtml(statusLabel + scheduledHint)}" ${scheduleDisabled ? "disabled" : ""}>${escapeHtml(scheduleLabel)}</button>
                    </td>
                </tr>
                ${expanded ? renderPreventiveClientDetail(client, clientKey) : ""}
            `;
        }).join("") : `<tr><td colspan="2" class="text-center copiers-muted">No hay clientes con productos copiers para mostrar.</td></tr>`;
    }

    function renderPreventiveClientDetail(client, clientKey) {
        const equipment = Array.isArray(client?.equipment) ? client.equipment : [];
        const periodLabel = state.preventiveMaintenance?.counterPeriodLabel || "Mes vigente";

        return `
            <tr class="copiers-preventive-detail-row">
                <td colspan="2">
                    <div class="copiers-preventive-detail">
                        <div class="copiers-preventive-detail__summary">
                            <span>${numberFormatter.format(Number(equipment.length || 0))} equipo${equipment.length === 1 ? "" : "s"}</span>
                            <span>${numberFormatter.format(Number(client?.maintenanceRegisteredCount || 0))} con mantenimiento</span>
                            <span>${numberFormatter.format(Number(client?.countersRegisteredCount || 0))} con contador</span>
                            <span>${escapeHtml(periodLabel)}</span>
                        </div>
                        <div class="copiers-table-wrap copiers-table-wrap--compact">
                            <table class="table align-middle copiers-table copiers-table--preventive-equipment">
                                <thead>
                                    <tr>
                                        <th>Equipo</th>
                                        <th>Tipo</th>
                                        <th>Referencia</th>
                                        <th>Sede</th>
                                        <th>Area</th>
                                        <th>Ultimo contador</th>
                                        <th class="text-end">Contador impresora</th>
                                        <th class="text-end">Contador escaner</th>
                                        <th class="text-end">Acciones</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    ${equipment.length ? equipment.map((row) => renderPreventiveEquipmentRow(clientKey, row)).join("") : `<tr><td colspan="9" class="text-center copiers-muted">Este cliente no tiene equipos asignados.</td></tr>`}
                                </tbody>
                            </table>
                        </div>
                    </div>
                </td>
            </tr>
        `;
    }

    async function togglePreventiveFrequency(clientKey) {
        const client = findPreventiveClient(clientKey);
        if (!client) {
            return;
        }

        if (!state.canEditPreventiveFrequency) {
            showStatus(statusBanner, "error", "Solo adaza@digitaltechcolombia.com o sruiz@digitaltechcolombia.com pueden cambiar esta periodicidad.");
            return;
        }

        if (!client.clientId) {
            showStatus(statusBanner, "error", "Este cliente no tiene ID de Dataverse para guardar la periodicidad.");
            return;
        }

        if (!urls.preventiveMaintenanceFrequency || state.preventiveFrequencySavingKeys.has(clientKey)) {
            return;
        }

        const previous = {
            key: client.maintenanceFrequencyKey || "monthly",
            label: client.maintenanceFrequencyLabel || "Mensual",
            isBimonthly: Boolean(client.isBimonthlyMaintenance)
        };
        const nextKey = previous.isBimonthly ? "monthly" : "bimonthly";
        applyPreventiveFrequency(client, nextKey);
        state.preventiveFrequencySavingKeys.add(clientKey);
        renderPreventiveMaintenance();

        try {
            const result = await fetchJson(urls.preventiveMaintenanceFrequency, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    clientId: client.clientId || "",
                    clientName: client.clientName || "",
                    frequencyKey: nextKey
                })
            });
            applyPreventiveFrequency(client, result?.maintenanceFrequencyKey || nextKey, result);
            showStatus(statusBanner, "success", result?.message || "Periodicidad actualizada.");
        } catch (error) {
            client.maintenanceFrequencyKey = previous.key;
            client.maintenanceFrequencyLabel = previous.label;
            client.isBimonthlyMaintenance = previous.isBimonthly;
            showStatus(statusBanner, "error", getErrorMessage(error));
        } finally {
            state.preventiveFrequencySavingKeys.delete(clientKey);
            renderPreventiveMaintenance();
        }
    }

    function applyPreventiveFrequency(client, frequencyKey, result) {
        const normalized = frequencyKey === "bimonthly" ? "bimonthly" : "monthly";
        client.maintenanceFrequencyKey = normalized;
        client.maintenanceFrequencyLabel = result?.maintenanceFrequencyLabel || (normalized === "bimonthly" ? "Bimensual" : "Mensual");
        client.isBimonthlyMaintenance = result?.isBimonthlyMaintenance ?? normalized === "bimonthly";
    }

    function renderPreventiveEquipmentRow(clientKey, row) {
        const hasCounter = Boolean(row.hasMonthlyCounter);
        const statusTone = hasCounter ? "is-good" : "is-warning";
        const statusLabel = row.counterDateDisplay || (hasCounter ? "Registrado" : "Pendiente");
        const maintenanceButtonLabel = row.maintenanceButtonLabel || (row.hasMonthlyMaintenance ? "Mantenimiento registrado" : "Registrar mantenimiento");
        const counterButtonLabel = row.counterButtonLabel || (row.hasMonthlyCounter ? "Contador registrado" : "Registrar contador");
        return `
            <tr>
                <td data-label="Equipo"><strong>${escapeHtml(row.serial || "Equipo sin serial")}</strong></td>
                <td data-label="Tipo">${escapeHtml(row.categoryLabel || "")}</td>
                <td data-label="Referencia">${escapeHtml(row.reference || "")}</td>
                <td data-label="Sede">${escapeHtml(row.site || "")}</td>
                <td data-label="Area">${escapeHtml(row.area || "")}</td>
                <td data-label="Ultimo contador"><span class="copiers-badge ${statusTone}">${escapeHtml(statusLabel)}</span></td>
                <td data-label="Contador impresora" class="text-end">${escapeHtml(formatNullableNumber(row.counterCopies))}</td>
                <td data-label="Contador escaner" class="text-end">${escapeHtml(formatNullableNumber(row.counterScans))}</td>
                <td data-label="Acciones" class="text-end">
                    <div class="copiers-inline-actions">
                        <button type="button" class="btn btn-sm copiers-preventive-equipment-action ${getPreventiveActionClass(row.maintenanceButtonTone || "outline-primary")}" data-preventive-maintenance-equipment="${escapeHtml(row.recordId || "")}" data-preventive-client="${escapeHtml(clientKey)}">${escapeHtml(maintenanceButtonLabel)}</button>
                        <button type="button" class="btn btn-sm copiers-preventive-equipment-action ${getPreventiveActionClass(row.counterButtonTone || "outline-secondary")}" data-preventive-counter-equipment="${escapeHtml(row.recordId || "")}" data-preventive-client="${escapeHtml(clientKey)}">${escapeHtml(counterButtonLabel)}</button>
                    </div>
                </td>
            </tr>
        `;
    }

    function openPreventiveScheduleModal(client) {
        if (!client) {
            return;
        }

        preventiveScheduleClientIdInput.value = client.clientId || "";
        preventiveScheduleClientNameInput.value = client.clientName || "";
        preventiveScheduleClientDisplayInput.value = client.clientName || "Sin cliente";
        preventiveScheduleDateInput.value = selectedPreventiveDateValue();
        preventiveScheduleTimeInput.value = defaultTimeValue();
        preventiveScheduleDurationInput.value = "60";
        preventiveScheduleSubtitle.textContent = `Reserva un espacio para ${client.clientName || "este cliente"}.`;
        clearStatus(preventiveScheduleStatus);
        showModal(preventiveScheduleModal);
    }

    async function schedulePreventiveMaintenance() {
        try {
            state.scheduleSaving = true;
            setBusy(true);
            preventiveScheduleSaveBtn.disabled = true;
            showStatus(preventiveScheduleStatus, "info", "Reservando espacio en tu calendario...");
            const result = await fetchJson(urls.schedulePreventiveMaintenance, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    clientId: preventiveScheduleClientIdInput.value,
                    clientName: preventiveScheduleClientNameInput.value,
                    dateValue: preventiveScheduleDateInput.value,
                    timeValue: preventiveScheduleTimeInput.value,
                    durationMinutes: Number(preventiveScheduleDurationInput.value || 60)
                })
            });

            closeModal(preventiveScheduleModal);
            await loadPreventiveMaintenance();
            showStatus(statusBanner, "success", result.message || "Mantenimiento preventivo programado.");
        } catch (error) {
            showScheduleError(error);
        } finally {
            preventiveScheduleSaveBtn.disabled = false;
            state.scheduleSaving = false;
            setBusy(false);
        }
    }

    async function openPreventiveMaintenanceModal(client, equipment) {
        await openMaintenanceModal({
            recordId: "",
            title: `Mantenimiento preventivo - ${equipment.serial || client.clientName || "Cliente"}`,
            equipmentId: equipment.recordId || "",
            clientId: client.clientId || equipment.clientId || "",
            clientName: client.clientName || equipment.clientName || "",
            dateValue: selectedPreventiveDateValue(),
            description: "",
            maintenanceTypeValue: maintenanceTypePreventive,
            maintenanceStatusValue: maintenanceStatusPending
        });
    }

    function openCounterModal(client, equipment) {
        counterEquipmentIdInput.value = equipment.recordId || "";
        counterEquipmentNameInput.value = `${equipment.serial || "Equipo"} - ${client.clientName || equipment.clientName || "Sin cliente"}`;
        counterCopiesInput.value = "";
        counterScansInput.value = "";
        counterDateInput.value = selectedPreventiveDateValue();
        counterFileInput.value = "";
        clearStatus(counterStatus);
        showModal(counterModal);
    }

    async function saveCounter() {
        try {
            state.counterSaving = true;
            setBusy(true);
            counterSaveBtn.disabled = true;
            showStatus(counterStatus, "info", "Registrando contador...");
            let result = await fetchJson(urls.saveCounter, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    equipmentId: counterEquipmentIdInput.value,
                    copiesCounter: parseNullableInt(counterCopiesInput.value),
                    scansCounter: parseNullableInt(counterScansInput.value),
                    dateValue: counterDateInput.value
                })
            });

            const file = counterFileInput.files?.[0];
            if (file && result.recordId) {
                showStatus(counterStatus, "info", "Adjuntando pagina de estado...");
                result = await uploadFile(urls.uploadCounter, "counterId", result.recordId, file);
            }

            closeModal(counterModal);
            await loadPreventiveMaintenance();
            showStatus(statusBanner, "success", result.message || "Contador registrado.");
        } catch (error) {
            showStatus(counterStatus, "error", getErrorMessage(error));
        } finally {
            counterSaveBtn.disabled = false;
            state.counterSaving = false;
            setBusy(false);
        }
    }

    async function loadBillingDays() {
        try {
            setBusy(true);
            billingDaysRefreshBtn && (billingDaysRefreshBtn.disabled = true);
            showStatus(statusBanner, "info", "Cargando dias de facturacion...");
            state.billingDays = await fetchJson(urls.billingDays);
            renderBillingDays();
            clearStatus(statusBanner);
        } catch (error) {
            showStatus(statusBanner, "error", getErrorMessage(error));
        } finally {
            billingDaysRefreshBtn && (billingDaysRefreshBtn.disabled = false);
            setBusy(false);
        }
    }

    function buildFallbackBillingDayGroups(rows) {
        const groups = new Map();
        (rows || []).forEach((row) => {
            const billingDay = Number(row.billingDay || 0);
            const clientKey = row.clientId || normalizeText(row.clientName || "sin-cliente");
            const groupId = `${clientKey}|day:${billingDay}`;
            if (!groups.has(groupId)) {
                groups.set(groupId, {
                    groupId,
                    clientId: row.clientId || "",
                    clientName: row.clientName || "Sin cliente",
                    billingDay,
                    billingDayDisplay: row.billingDayDisplay || (billingDay > 0 ? `Dia ${billingDay}` : "Sin dia"),
                    productLinesCount: 0,
                    equipmentCount: 0,
                    quantity: 0,
                    includedOperations: 0,
                    additionalOperation: 0,
                    equipmentAssignmentSummary: "Sin asignacion",
                    lines: []
                });
            }

            const group = groups.get(groupId);
            group.lines.push(row);
            group.productLinesCount += 1;
            group.quantity += Number(row.quantity || 0);
            group.includedOperations += Number(row.includedOperations || 0);
            group.additionalOperation += Number(row.additionalOperation || 0);
        });

        return Array.from(groups.values());
    }

    function getBillingDayGroups() {
        const dashboard = state.billingDays || {};
        const groups = Array.isArray(dashboard.groups) && dashboard.groups.length
            ? [...dashboard.groups]
            : buildFallbackBillingDayGroups(Array.isArray(dashboard.rows) ? dashboard.rows : []);

        return groups.sort((left, right) => {
            const leftDay = Number(left.billingDay || 0) > 0 ? Number(left.billingDay || 0) : Number.MAX_SAFE_INTEGER;
            const rightDay = Number(right.billingDay || 0) > 0 ? Number(right.billingDay || 0) : Number.MAX_SAFE_INTEGER;
            if (leftDay !== rightDay) {
                return leftDay - rightDay;
            }

            return normalizeText(left.clientName).localeCompare(normalizeText(right.clientName), "es");
        });
    }

    function renderBillingDays() {
        const groups = getBillingDayGroups();
        const dashboardRows = Array.isArray(state.billingDays?.rows) ? state.billingDays.rows : [];
        const rowCount = dashboardRows.length || groups.reduce((sum, group) => sum + Number(group.productLinesCount || 0), 0);

        if (billingDaysCount) {
            billingDaysCount.textContent = `${numberFormatter.format(groups.length)} grupo${groups.length === 1 ? "" : "s"} / ${numberFormatter.format(rowCount)} linea${rowCount === 1 ? "" : "s"}`;
        }

        if (billingDaysEmpty) {
            billingDaysEmpty.hidden = groups.length > 0;
        }

        if (!billingDaysBody) {
            return;
        }

        billingDaysBody.innerHTML = groups.length
            ? groups.map((group) => renderBillingDayGroupRows(group)).join("")
            : `<tr><td colspan="6" class="text-center copiers-muted">${escapeHtml(state.billingDays?.emptyStateMessage || "No hay registros de facturacion copiers disponibles.")}</td></tr>`;
    }

    function renderBillingDayGroupRows(group) {
        const groupId = group.groupId || "";
        const expanded = state.billingDaysExpandedGroups.has(groupId);
        const lineCount = Number(group.productLinesCount || (Array.isArray(group.lines) ? group.lines.length : 0));
        const equipmentCount = Number(group.equipmentCount || 0);

        return `
            <tr class="copiers-billing-group-row ${expanded ? "is-expanded" : ""}">
                <td data-label="Dia fact.">
                    <button type="button" class="copiers-group-toggle" data-billing-group-toggle="${escapeHtml(groupId)}" aria-expanded="${expanded ? "true" : "false"}">
                        <span>${expanded ? "-" : "+"}</span>
                        ${escapeHtml(group.billingDayDisplay || "Sin dia")}
                    </button>
                </td>
                <td data-label="Cliente">${escapeHtml(group.clientName || "Sin cliente")}</td>
                <td data-label="Lineas">${escapeHtml(numberFormatter.format(lineCount))} linea${lineCount === 1 ? "" : "s"}</td>
                <td data-label="Cantidad" class="text-end">${escapeHtml(numberFormatter.format(Number(group.quantity || 0)))}</td>
                <td data-label="Equipos">${renderBillingAssignmentSummary(equipmentCount, group.equipmentAssignmentSummary || "Sin asignacion")}</td>
                <td data-label="Detalle" class="text-end">
                    <button type="button" class="btn btn-sm btn-outline-secondary" data-billing-group-toggle="${escapeHtml(groupId)}">
                        ${expanded ? "Ocultar" : "Desglosar"}
                    </button>
                </td>
            </tr>
            ${expanded ? renderBillingDayGroupDetail(group) : ""}
        `;
    }

    function renderBillingAssignmentSummary(count, summary) {
        return `
            <span class="copiers-assignment-inline">
                <strong>${escapeHtml(numberFormatter.format(Number(count || 0)))}</strong>
                <small>${escapeHtml(summary || "Sin asignacion")}</small>
            </span>
        `;
    }

    function renderBillingDayGroupDetail(group) {
        const lines = Array.isArray(group?.lines) ? group.lines : [];

        return `
            <tr class="copiers-billing-detail-row">
                <td colspan="6">
                    <div class="copiers-billing-detail">
                        <div class="copiers-billing-detail__header">
                            <strong>Lineas de productos Copiers</strong>
                            <span>${escapeHtml(group?.equipmentAssignmentSummary || `${numberFormatter.format(lines.length)} linea(s)`)}</span>
                        </div>
                        ${renderBillingDayProductLines(lines)}
                    </div>
                </td>
            </tr>
        `;
    }

    function renderBillingDayProductLines(lines) {
        const items = Array.isArray(lines) ? lines : [];
        if (!items.length) {
            return `<div class="copiers-empty copiers-empty--inline">No hay lineas de productos para este grupo.</div>`;
        }

        return `
            <div class="copiers-billing-lines">
                <div class="copiers-billing-line copiers-billing-line--header">
                    <span>Producto</span>
                    <span>Cant.</span>
                    <span>Equipos</span>
                    <span>Oper. incl.</span>
                    <span>Oper. adic.</span>
                </div>
                ${items.map((row) => `
                    <div class="copiers-billing-line">
                        <span>${escapeHtml(row.productName || "Producto sin nombre")}</span>
                        <span class="text-end">${escapeHtml(numberFormatter.format(Number(row.quantity || 0)))}</span>
                        <button type="button"
                                class="copiers-assignment-btn ${row.hasAssignmentOverflow ? "is-warning" : ""}"
                                data-billing-line-assignment="${escapeHtml(row.recordId || "")}"
                                title="Asignar equipos a esta linea">
                            <strong>${escapeHtml(`${numberFormatter.format(Number(row.assignedEquipmentCount || 0))}/${numberFormatter.format(Number(row.equipmentAssignmentCapacity || 0))}`)}</strong>
                            <small>${escapeHtml(row.equipmentAssignmentSummary || "Sin asignacion")}</small>
                        </button>
                        <span class="text-end">${escapeHtml(numberFormatter.format(Number(row.includedOperations || 0)))}</span>
                        <span class="text-end">${escapeHtml(numberFormatter.format(Number(row.additionalOperation || 0)))}</span>
                    </div>
                `).join("")}
            </div>
        `;
    }

    function findBillingDayLine(recordId) {
        const normalizedId = (recordId || "").toLowerCase();
        if (!normalizedId) {
            return null;
        }

        const direct = (state.billingDays?.rows || []).find((row) => (row.recordId || "").toLowerCase() === normalizedId);
        if (direct) {
            return direct;
        }

        for (const group of getBillingDayGroups()) {
            const row = (group.lines || []).find((item) => (item.recordId || "").toLowerCase() === normalizedId);
            if (row) {
                return row;
            }
        }

        return null;
    }

    async function loadMovements() {
        try {
            setBusy(true);
            movementsRefreshBtn && (movementsRefreshBtn.disabled = true);
            showStatus(statusBanner, "info", "Cargando movimientos de equipos...");
            state.movements = await fetchJson(urls.equipmentMovements);
            renderMovements();
            clearStatus(statusBanner);
        } catch (error) {
            showStatus(statusBanner, "error", getErrorMessage(error));
        } finally {
            movementsRefreshBtn && (movementsRefreshBtn.disabled = false);
            setBusy(false);
        }
    }

    function renderMovements() {
        const allRecords = Array.isArray(state.movements?.records) ? state.movements.records : [];
        const records = getFilteredMovementRows(allRecords);

        if (movementsCount) {
            movementsCount.textContent = state.movementsSearchTerm
                ? `${records.length} de ${allRecords.length} registros`
                : `${records.length} registro${records.length === 1 ? "" : "s"}`;
        }

        if (movementsEmpty) {
            movementsEmpty.hidden = allRecords.length > 0;
        }

        updateMovementSortButtons();

        if (!movementsBody) {
            return;
        }

        movementsBody.innerHTML = records.length ? records.map((row) => `
            <tr>
                <td data-label="Fecha movimiento">${escapeHtml(row.dateDisplay || "-")}</td>
                <td data-label="Equipo">${escapeHtml(row.equipmentSerial || "Sin equipo")}</td>
                <td data-label="Cliente nuevo">${escapeHtml(row.clientName || "Sin cliente")}</td>
                <td data-label="Motivo movimiento">${escapeHtml(row.reason || "Sin motivo")}</td>
                <td data-label="Acta de entrega">${renderMovementAttachmentLink(row)}</td>
            </tr>
        `).join("") : `<tr><td colspan="5" class="text-center copiers-muted">${escapeHtml(allRecords.length ? "No hay movimientos para el filtro aplicado." : (state.movements?.emptyStateMessage || "No hay movimientos de equipos registrados."))}</td></tr>`;
    }

    function getFilteredMovementRows(rows) {
        const query = normalizeText(state.movementsSearchTerm);
        const sortKey = state.movementsSortKey || "dateValue";
        const direction = state.movementsSortDirection === "asc" ? 1 : -1;

        return [...rows]
            .filter((row) => {
                if (!query) {
                    return true;
                }

                return normalizeText(getMovementSearchText(row)).includes(query);
            })
            .sort((left, right) => {
                const comparison = compareMovementValues(
                    getMovementSortValue(left, sortKey),
                    getMovementSortValue(right, sortKey));

                if (comparison !== 0) {
                    return comparison * direction;
                }

                return compareMovementValues(left.equipmentSerial || "", right.equipmentSerial || "");
            });
    }

    function getMovementSearchText(row) {
        return [
            row?.dateDisplay || "",
            row?.dateValue || "",
            row?.equipmentSerial || "",
            row?.clientName || "",
            row?.reason || "",
            row?.hasAttachment ? "Con acta" : "Sin acta",
            row?.attachmentFileName || ""
        ].join(" ");
    }

    function getMovementSortValue(row, sortKey) {
        if (sortKey === "attachment") {
            return row?.hasAttachment ? 1 : 0;
        }

        if (sortKey === "dateValue") {
            return `${row?.dateValue || row?.dateDisplay || ""}|${row?.createdOnValue || ""}`;
        }

        return row?.[sortKey] || "";
    }

    function compareMovementValues(left, right) {
        const leftIsEmpty = left === null || left === undefined || left === "";
        const rightIsEmpty = right === null || right === undefined || right === "";
        if (leftIsEmpty && rightIsEmpty) {
            return 0;
        }

        if (leftIsEmpty) {
            return 1;
        }

        if (rightIsEmpty) {
            return -1;
        }

        if (typeof left === "number" || typeof right === "number") {
            return Number(left || 0) - Number(right || 0);
        }

        return left.toString().localeCompare(right.toString(), "es", { numeric: true, sensitivity: "base" });
    }

    function updateMovementSortButtons() {
        movementSortButtons.forEach((button) => {
            const key = button.dataset.copiersMovementSort || "";
            const isActive = key === state.movementsSortKey;
            button.classList.toggle("is-active", isActive);
            button.dataset.sortDirection = isActive ? state.movementsSortDirection : "";
            button.setAttribute("aria-pressed", isActive ? "true" : "false");
        });
    }

    function resetLineEquipmentModal() {
        state.lineEquipmentDetail = null;
        state.lineEquipmentDraftIds = new Set();
        state.lineEquipmentSaving = false;
        lineEquipmentTitle && (lineEquipmentTitle.textContent = "Equipos de la linea");
        lineEquipmentSubtitle && (lineEquipmentSubtitle.textContent = "Asigna equipos del cliente a esta linea de producto Copiers.");
        lineEquipmentSummary && (lineEquipmentSummary.innerHTML = "");
        lineEquipmentAssignedCount && (lineEquipmentAssignedCount.textContent = "0 equipos");
        lineEquipmentAvailableCount && (lineEquipmentAvailableCount.textContent = "0 equipos");
        lineEquipmentSaveBtn && (lineEquipmentSaveBtn.disabled = false);
        lineEquipmentAssignedBody && (lineEquipmentAssignedBody.innerHTML = `<tr><td colspan="3" class="text-center copiers-muted">No hay equipos asignados a esta linea.</td></tr>`);
        lineEquipmentAvailableBody && (lineEquipmentAvailableBody.innerHTML = `<tr><td colspan="3" class="text-center copiers-muted">No hay equipos disponibles para asignar.</td></tr>`);
        clearStatus(lineEquipmentStatus);
    }

    function closeLineEquipmentModal() {
        closeModal(lineEquipmentModal);
        resetLineEquipmentModal();
    }

    function renderLineEquipmentLoading(row) {
        resetLineEquipmentModal();
        lineEquipmentTitle && (lineEquipmentTitle.textContent = row?.productName || "Equipos de la linea");
        lineEquipmentSubtitle && (lineEquipmentSubtitle.textContent = "Cargando equipos asignados y disponibles del cliente...");
        lineEquipmentSaveBtn && (lineEquipmentSaveBtn.disabled = true);
        showStatus(lineEquipmentStatus, "info", "Consultando asignacion de equipos...");
        showModal(lineEquipmentModal);
    }

    function buildLineEquipmentAssignmentUrl(lineId, clientId) {
        const params = new URLSearchParams({
            lineId: lineId || "",
            clientId: clientId || ""
        });
        return `${urls.lineEquipmentAssignment}?${params.toString()}`;
    }

    async function loadLineEquipmentAssignment(row) {
        if (!row || !urls.lineEquipmentAssignment) {
            showStatus(statusBanner, "error", "No hay una URL configurada para consultar asignaciones.");
            return;
        }

        renderLineEquipmentLoading(row);
        try {
            const detail = await fetchJson(buildLineEquipmentAssignmentUrl(row.recordId || "", row.clientId || ""));
            renderLineEquipmentDetail(detail);
            clearStatus(lineEquipmentStatus);
        } catch (error) {
            lineEquipmentSubtitle && (lineEquipmentSubtitle.textContent = "No fue posible cargar la asignacion de equipos de esta linea.");
            showStatus(lineEquipmentStatus, "error", getErrorMessage(error));
        }
    }

    function getLineEquipmentPool(detail) {
        const byId = new Map();
        [...(detail?.assignedEquipment || []), ...(detail?.availableEquipment || [])].forEach((item) => {
            const id = item?.equipmentId || "";
            if (id && !byId.has(id)) {
                byId.set(id, item);
            }
        });

        return Array.from(byId.values()).sort((left, right) =>
            String(left?.serial || "").localeCompare(String(right?.serial || ""), "es", { numeric: true, sensitivity: "base" }));
    }

    function buildLineEquipmentDetailText(item) {
        return [item?.categoryLabel, item?.reference, item?.site, item?.area]
            .filter((value) => value && String(value).trim())
            .join(" · ") || "Sin detalle";
    }

    function renderLineEquipmentRow(item, action) {
        const isAssign = action === "assign";
        return `
            <tr>
                <td data-label="Equipo"><strong>${escapeHtml(item?.serial || "Equipo sin serial")}</strong></td>
                <td data-label="Detalle">${escapeHtml(buildLineEquipmentDetailText(item))}</td>
                <td data-label="Accion" class="text-end">
                    <button type="button"
                            class="btn btn-sm ${isAssign ? "btn-outline-primary" : "btn-outline-secondary"}"
                            data-line-equipment-${isAssign ? "assign" : "remove"}="${escapeHtml(item?.equipmentId || "")}">
                        ${isAssign ? "Asignar" : "Quitar"}
                    </button>
                </td>
            </tr>
        `;
    }

    function renderLineEquipmentDetail(detail) {
        state.lineEquipmentDetail = detail || null;
        state.lineEquipmentDraftIds = new Set(
            (detail?.assignedEquipment || [])
                .map((item) => item?.equipmentId || "")
                .filter(Boolean)
        );
        renderLineEquipmentDraft();
    }

    function renderLineEquipmentSummary(detail, assignedCount, availableCount) {
        if (!lineEquipmentSummary) {
            return;
        }

        const capacity = Number(detail?.assignmentCapacity || 0);
        const overflow = assignedCount > capacity;
        lineEquipmentSummary.innerHTML = `
            <article class="copiers-detail-card ${overflow ? "is-warning" : ""}">
                <span>Cupos de la linea</span>
                <strong>${escapeHtml(numberFormatter.format(capacity))}</strong>
            </article>
            <article class="copiers-detail-card">
                <span>Asignados</span>
                <strong>${escapeHtml(numberFormatter.format(assignedCount))}</strong>
            </article>
            <article class="copiers-detail-card">
                <span>Disponibles</span>
                <strong>${escapeHtml(numberFormatter.format(availableCount))}</strong>
            </article>
            <article class="copiers-detail-card">
                <span>Oper. incluidas</span>
                <strong>${escapeHtml(numberFormatter.format(Number(detail?.includedOperations || 0)))}</strong>
            </article>
        `;
    }

    function renderLineEquipmentDraft() {
        const detail = state.lineEquipmentDetail;
        const pool = getLineEquipmentPool(detail);
        const assignedIds = state.lineEquipmentDraftIds || new Set();
        const assigned = pool.filter((item) => assignedIds.has(item?.equipmentId || ""));
        const available = pool.filter((item) => !assignedIds.has(item?.equipmentId || ""));
        const capacity = Number(detail?.assignmentCapacity || 0);

        lineEquipmentTitle && (lineEquipmentTitle.textContent = detail?.productName || "Equipos de la linea");
        lineEquipmentSubtitle && (lineEquipmentSubtitle.textContent = [
            detail?.clientName || "",
            `${numberFormatter.format(assigned.length)}/${numberFormatter.format(capacity)} asignados`
        ].filter(Boolean).join(" · "));
        lineEquipmentAssignedCount && (lineEquipmentAssignedCount.textContent = `${numberFormatter.format(assigned.length)} equipo${assigned.length === 1 ? "" : "s"}`);
        lineEquipmentAvailableCount && (lineEquipmentAvailableCount.textContent = `${numberFormatter.format(available.length)} equipo${available.length === 1 ? "" : "s"}`);
        renderLineEquipmentSummary(detail, assigned.length, available.length);

        if (lineEquipmentAssignedBody) {
            lineEquipmentAssignedBody.innerHTML = assigned.length
                ? assigned.map((item) => renderLineEquipmentRow(item, "remove")).join("")
                : `<tr><td colspan="3" class="text-center copiers-muted">No hay equipos asignados a esta linea.</td></tr>`;
        }

        if (lineEquipmentAvailableBody) {
            lineEquipmentAvailableBody.innerHTML = available.length
                ? available.map((item) => renderLineEquipmentRow(item, "assign")).join("")
                : `<tr><td colspan="3" class="text-center copiers-muted">No hay equipos disponibles para asignar.</td></tr>`;
        }

        if (lineEquipmentSaveBtn) {
            lineEquipmentSaveBtn.disabled = state.lineEquipmentSaving || assigned.length > capacity;
        }

        if (assigned.length > capacity) {
            showStatus(lineEquipmentStatus, "error", `Esta linea permite maximo ${numberFormatter.format(capacity)} equipo(s).`);
        } else if (!state.lineEquipmentSaving && !lineEquipmentStatus?.classList.contains("is-success")) {
            clearStatus(lineEquipmentStatus);
        }
    }

    function assignLineEquipment(equipmentId) {
        const capacity = Number(state.lineEquipmentDetail?.assignmentCapacity || 0);
        const normalizedId = equipmentId || "";
        if (!normalizedId) {
            return;
        }

        if ((state.lineEquipmentDraftIds?.size || 0) >= capacity) {
            showStatus(lineEquipmentStatus, "error", `Esta linea permite maximo ${numberFormatter.format(capacity)} equipo(s).`);
            return;
        }

        state.lineEquipmentDraftIds.add(normalizedId);
        clearStatus(lineEquipmentStatus);
        renderLineEquipmentDraft();
    }

    function removeLineEquipment(equipmentId) {
        const normalizedId = equipmentId || "";
        if (!normalizedId) {
            return;
        }

        state.lineEquipmentDraftIds.delete(normalizedId);
        clearStatus(lineEquipmentStatus);
        renderLineEquipmentDraft();
    }

    async function saveLineEquipmentAssignment() {
        const detail = state.lineEquipmentDetail;
        if (!detail || state.lineEquipmentSaving) {
            return;
        }

        if (!urls.saveLineEquipmentAssignment) {
            showStatus(lineEquipmentStatus, "error", "No hay una URL configurada para guardar la asignacion.");
            return;
        }

        try {
            state.lineEquipmentSaving = true;
            lineEquipmentSaveBtn && (lineEquipmentSaveBtn.disabled = true);
            showStatus(lineEquipmentStatus, "info", "Guardando asignacion...");
            const result = await fetchJson(urls.saveLineEquipmentAssignment, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    lineId: detail.lineId || "",
                    clientId: detail.clientId || "",
                    equipmentIds: Array.from(state.lineEquipmentDraftIds || [])
                })
            });

            renderLineEquipmentDetail(result?.detail || detail);
            showStatus(lineEquipmentStatus, "success", result?.message || "Asignacion actualizada correctamente.");
            await loadBillingDays();
        } catch (error) {
            showStatus(lineEquipmentStatus, "error", getErrorMessage(error));
        } finally {
            state.lineEquipmentSaving = false;
            renderLineEquipmentDraft();
        }
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
        const contractLines = Array.isArray(inventory?.contractLines) ? inventory.contractLines : [];
        const issues = Array.isArray(inventory?.issues) ? inventory.issues : [];

        inventoryCount.textContent = `${records.length} equipo${records.length === 1 ? "" : "s"}`;
        inventoryEmpty.hidden = Boolean(inventory) && records.length > 0;
        inventoryEmpty.textContent = inventory
            ? "No hay equipos registrados para este cliente."
            : "Selecciona un cliente para consultar sus equipos.";

        if (issues.length) {
            showStatus(inventoryMissing, "warning", issues.map(issue => issue.message).join(" "));
        } else {
            clearStatus(inventoryMissing);
        }

        inventoryKpis.innerHTML = kpis.map((kpi) => `
            <article class="copiers-kpi">
                <span>${escapeHtml(kpi.label)}</span>
                <strong>${numberFormatter.format(Number(kpi.value || 0))}</strong>
                <small>${escapeHtml(kpi.secondaryLabel || "")}: ${escapeHtml(kpi.secondaryValue || "")}</small>
            </article>`).join("");

        if (inventoryContractLines) {
            inventoryContractLines.innerHTML = contractLines.length ? contractLines.map(line => `
                <article class="copiers-contract-line">
                    <div>
                        <span>${escapeHtml(line.billingDayDisplay || "Sin dia")}</span>
                        <strong>${escapeHtml(line.productName || "Producto Copiers")}</strong>
                        <small>${escapeHtml(line.assignmentSummary || "")}</small>
                    </div>
                    <dl>
                        <div>
                            <dt>Cantidad</dt>
                            <dd>${numberFormatter.format(Number(line.quantity || 0))}</dd>
                        </div>
                        <div>
                            <dt>Operaciones incl.</dt>
                            <dd>${numberFormatter.format(Number(line.includedOperations || 0))}</dd>
                        </div>
                    </dl>
                </article>
            `).join("") : (inventory ? `
                <div class="copiers-empty copiers-empty--inline">No hay lineas contratadas en Productos Copiers para este cliente.</div>
            ` : "");
        }

        inventoryLocations.innerHTML = inventory ? `
            <article class="copiers-location-card copiers-location-card--client is-selectable" data-inventory-client-id="${escapeHtml(inventory.clientId || "")}" tabindex="0">
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
                <td data-label="Clasificacion">${renderInventoryAssignmentBadge(row)}</td>
                <td data-label="Producto contratado">${escapeHtml(row.contractLineName || "")}${row.billingDayDisplay ? `<small class="copiers-table-note">${escapeHtml(row.billingDayDisplay)}</small>` : ""}</td>
                <td data-label="Operaciones incl." class="text-end">${row.includedOperations ? numberFormatter.format(Number(row.includedOperations || 0)) : ""}</td>
                <td data-label="Area">${escapeHtml(row.area || "")}</td>
                <td data-label="Sede">${escapeHtml(row.site || "")}</td>
                <td data-label="Observaciones">${escapeHtml(row.observations || "")}</td>
                <td data-label="Backup" class="text-end">
                    <button type="button"
                            class="btn btn-sm ${row.isBackup ? "btn-outline-secondary" : "btn-outline-primary"} copiers-backup-btn"
                            data-inventory-backup-toggle
                            data-equipment-id="${escapeHtml(row.recordId || "")}"
                            data-is-backup="${row.isBackup ? "true" : "false"}">
                        ${row.isBackup ? "Quitar" : "Marcar"}
                    </button>
                </td>
            </tr>`).join("") : `<tr><td colspan="10" class="text-center copiers-muted">No hay equipos para mostrar.</td></tr>`;
    }

    function renderInventoryAssignmentBadge(row) {
        const status = row?.assignmentStatus || "Sin clasificar";
        const tone = row?.isBackup
            ? "is-info"
            : (status.toLowerCase().includes("sin") ? "is-warning" : "is-success");
        return `<span class="copiers-badge ${tone}">${escapeHtml(status)}</span>`;
    }

    async function saveEquipmentBackupAssignment(equipmentId, isBackup) {
        if (!equipmentId || !urls.equipmentBackupAssignment) {
            showStatus(statusBanner, "error", "No hay una URL configurada para actualizar backups.");
            return;
        }

        const inventory = state.equipmentInventory || {};
        const clientId = inventory.clientId || inventoryClientIdInput.value || "";
        const clientName = inventory.clientName || inventoryClientNameInput.value || "";
        try {
            setBusy(true);
            showStatus(statusBanner, "info", isBackup ? "Marcando equipo como backup..." : "Quitando equipo de backups...");
            const result = await fetchJson(urls.equipmentBackupAssignment, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    clientId,
                    clientName,
                    equipmentId,
                    isBackup
                })
            });
            state.equipmentInventory = result.inventory || state.equipmentInventory;
            renderEquipmentInventory();
            showStatus(statusBanner, "success", result.message || "Clasificacion de backup actualizada.");
        } catch (error) {
            showStatus(statusBanner, "error", getErrorMessage(error));
        } finally {
            setBusy(false);
        }
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
        equipmentDetailSubtitle.textContent = "Cargando informacion e historiales del equipo...";
        if (equipmentMaintenanceBody) {
            equipmentMaintenanceBody.innerHTML = `<tr><td colspan="9" class="text-center copiers-muted">Cargando historial del equipo...</td></tr>`;
        }
        if (equipmentMovementsBody) {
            equipmentMovementsBody.innerHTML = `<tr><td colspan="4" class="text-center copiers-muted">Cargando movimientos del equipo...</td></tr>`;
        }
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
        equipmentClientNameInput.value = equipment.inStock ? "Stock" : (equipment.clientName || "");
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
            ? "Este equipo esta en stock. Usa Registrar movimiento para asignarlo a un cliente."
            : "Edita los datos visibles de la tabla y consulta sus movimientos.";
        renderEquipmentMaintenanceTable(detail?.maintenanceRows);
        renderEquipmentMovementsTable(detail?.movementRows);
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
        if (equipmentMaintenanceBody) {
            equipmentMaintenanceBody.innerHTML = `<tr><td colspan="9" class="text-center copiers-muted">Selecciona un equipo para ver sus mantenimientos.</td></tr>`;
        }
        if (equipmentMovementsBody) {
            equipmentMovementsBody.innerHTML = `<tr><td colspan="4" class="text-center copiers-muted">Selecciona un equipo para ver sus movimientos.</td></tr>`;
        }
    }

    function renderEquipmentMaintenanceTable(rows) {
        if (!equipmentMaintenanceBody) {
            return;
        }

        const items = Array.isArray(rows) ? rows : [];
        equipmentMaintenanceBody.innerHTML = items.length ? items.map((row) => {
            const attachment = row.hasAttachment
                ? `<a class="copiers-link" href="${buildDownloadUrl(urls.downloadMaintenance, "maintenanceId", row.recordId)}" target="_blank" rel="noopener">${escapeHtml(row.attachmentFileName || "Descargar")}</a>`
                : `<span class="copiers-muted">Sin adjunto</span>`;
            const statusValue = Number(row.maintenanceStatusValue || maintenanceStatusPending);
            const completed = statusValue === maintenanceStatusCompleted;

            return `
                <tr>
                    <td data-label="Fecha">${escapeHtml(row.dateDisplay || "")}</td>
                    <td data-label="Titulo">${escapeHtml(row.title || "")}</td>
                    <td data-label="Tipo">${escapeHtml(row.maintenanceTypeLabel || "")}</td>
                    <td data-label="Estado"><span class="copiers-badge ${completed ? "is-good" : "is-warning"}">${escapeHtml(row.maintenanceStatusLabel || "Pendiente")}</span></td>
                    <td data-label="Cliente">${escapeHtml(row.clientName || "Sin cliente")}</td>
                    <td data-label="Tecnico">${escapeHtml(row.technicianName || "")}</td>
                    <td data-label="Descripcion">${escapeHtml(row.description || "")}</td>
                    <td data-label="ID">${escapeHtml(row.internalId || "")}</td>
                    <td data-label="Adjunto">${attachment}</td>
                </tr>`;
        }).join("") : `<tr><td colspan="9" class="text-center copiers-muted">Este equipo no tiene mantenimientos registrados.</td></tr>`;
    }

    function renderEquipmentMovementsTable(rows) {
        if (!equipmentMovementsBody) {
            return;
        }

        const items = Array.isArray(rows) ? rows : [];
        equipmentMovementsBody.innerHTML = items.length ? items.map((row) => `
            <tr>
                <td data-label="Fecha">${escapeHtml(row.dateDisplay || "")}</td>
                <td data-label="Cliente nuevo">${escapeHtml(row.clientName || "Sin cliente")}</td>
                <td data-label="Motivo">${escapeHtml(row.reason || "")}</td>
                <td data-label="Acta">${renderMovementAttachmentLink(row)}</td>
            </tr>`).join("") : `<tr><td colspan="4" class="text-center copiers-muted">Este equipo no tiene movimientos registrados.</td></tr>`;
    }

    function renderMovementAttachmentLink(row) {
        if (!row?.hasAttachment) {
            return `<span class="copiers-muted">Sin acta</span>`;
        }

        return `<a class="copiers-link" href="${buildDownloadUrl(urls.downloadEquipmentMovement, "movementId", row.recordId)}" target="_blank" rel="noopener">${escapeHtml(row.attachmentFileName || "Descargar")}</a>`;
    }

    function populateEquipmentCategoryOptions(selectedValue, options) {
        const items = Array.isArray(options) ? options : [];
        const selected = selectedValue === null || selectedValue === undefined ? "" : String(selectedValue);
        equipmentCategorySelect.innerHTML = `<option value="">Sin tipo</option>` + items.map((option) => {
            const value = String(option.value ?? "");
            return `<option value="${escapeHtml(value)}" ${value === selected ? "selected" : ""}>${escapeHtml(option.label || value)}</option>`;
        }).join("");
    }

    function openEquipmentMovementModal() {
        const equipment = state.equipmentDetail?.equipment || {};
        const equipmentId = equipment.recordId || equipmentRecordIdInput.value || "";
        if (!equipmentId) {
            showStatus(equipmentDetailStatus, "error", "Selecciona un equipo antes de registrar un movimiento.");
            return;
        }

        movementEquipmentIdInput.value = equipmentId;
        movementClientIdInput.value = "";
        movementClientNameInput.value = "";
        movementClientOptions.innerHTML = "";
        movementDateInput.value = todayValue();
        movementReasonInput.value = "";
        movementFileInput && (movementFileInput.value = "");
        state.movementClientSuggestions = [];
        equipmentMovementTitle.textContent = "Registrar movimiento";
        equipmentMovementSubtitle.textContent = equipment.serial
            ? `Registra el cambio de cliente para el equipo ${equipment.serial}.`
            : "Registra el cambio de cliente del equipo seleccionado.";
        clearStatus(equipmentMovementStatus);
        showModal(equipmentMovementModal);
        movementClientNameInput.focus();
    }

    async function saveEquipmentMovement() {
        if (state.movementSaving) {
            return;
        }

        try {
            syncClientSelection(movementClientNameInput, movementClientIdInput, state.movementClientSuggestions);
            const equipmentId = movementEquipmentIdInput.value || equipmentRecordIdInput.value || "";
            const clientName = (movementClientNameInput.value || "").trim();
            const reason = (movementReasonInput.value || "").trim();
            if (!equipmentId) {
                throw new Error("Selecciona un equipo para registrar el movimiento.");
            }
            if (!clientName || !movementClientIdInput.value) {
                throw new Error("Selecciona un cliente nuevo valido de la lista.");
            }
            if (!movementDateInput.value) {
                throw new Error("Debes indicar la fecha de movimiento.");
            }
            if (!reason) {
                throw new Error("Debes indicar el motivo del movimiento.");
            }

            state.movementSaving = true;
            movementSaveBtn.disabled = true;
            showStatus(equipmentMovementStatus, "info", "Registrando movimiento...");
            const payload = {
                equipmentId,
                clientId: movementClientIdInput.value,
                clientName,
                dateValue: movementDateInput.value,
                reason
            };

            let result = await fetchJson(urls.registerEquipmentMovement, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });

            const file = movementFileInput?.files?.[0];
            if (file && !result.recordId) {
                throw new Error("El movimiento se registro, pero no recibimos el ID para adjuntar el acta. Actualiza e intenta adjuntar nuevamente.");
            }

            if (file) {
                showStatus(equipmentMovementStatus, "info", "Adjuntando acta de entrega...");
                result = await uploadFile(urls.uploadEquipmentMovement, "movementId", result.recordId, file);
            }

            await loadEquipment();
            if (state.movements) {
                await loadMovements();
            }
            if (state.equipmentInventory) {
                await loadEquipmentInventory();
            }

            const detail = await fetchJson(`${urls.equipmentDetail}?equipmentId=${encodeURIComponent(equipmentId)}`);
            fillEquipmentDetail(detail);
            closeModal(equipmentMovementModal);
            showStatus(equipmentDetailStatus, "success", result.message || "Movimiento registrado correctamente.");
        } catch (error) {
            showStatus(equipmentMovementStatus, "error", getErrorMessage(error));
        } finally {
            state.movementSaving = false;
            movementSaveBtn.disabled = false;
        }
    }

    async function saveEquipmentAssignment() {
        if (state.equipmentAssignmentSaving) {
            return;
        }

        try {
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
        const selectedEquipmentId = row?.recordId && !row?.equipmentId
            ? externalEquipmentValue
            : (row?.equipmentId || "");
        populateMaintenanceOptions(selectedEquipmentId);
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

        const hasClient = Boolean(clientId || clientName);
        const placeholder = hasClient
            ? "Selecciona un equipo"
            : "Selecciona primero un cliente";
        const externalOption = hasClient
            ? `<option value="${externalEquipmentValue}">Equipo externo</option>`
            : "";
        maintenanceEquipmentSelect.disabled = !hasClient;
        maintenanceEquipmentSelect.innerHTML = `<option value="">${placeholder}</option>` + externalOption + rows.map((row) => {
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
            const selectedEquipmentId = maintenanceEquipmentSelect.value;
            if (!selectedEquipmentId) {
                throw new Error("Selecciona un equipo o Equipo externo.");
            }
            if (selectedEquipmentId === externalEquipmentValue && !maintenanceClientIdInput.value && !maintenanceClientNameInput.value.trim()) {
                throw new Error("Selecciona el cliente del equipo externo.");
            }
            const payload = {
                recordId: maintenanceRecordIdInput.value,
                title: maintenanceTitleInput.value,
                internalId: "",
                equipmentId: selectedEquipmentId,
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
            if (state.preventiveMaintenance) {
                await loadPreventiveMaintenance();
            }
            const message = result.message || "Mantenimiento guardado.";
            setActiveTab("maintenance");
            showSaveConfirmation(message);
            returnToCopiersTable("maintenance");
            showStatus(statusBanner, "success", message);
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
            const message = result.message || "Entrega guardada.";
            setActiveTab("deliveries");
            showSaveConfirmation(message);
            returnToCopiersTable("deliveries");
            showStatus(statusBanner, "success", message);
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
            } else if (target === "movement") {
                state.movementClientSuggestions = Array.isArray(suggestions) ? suggestions : [];
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
            } else if (target === "movement") {
                state.movementClientSuggestions = [];
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

    function showScheduleError(error) {
        const payload = error?.payload || {};
        if (payload.action !== "calendarConsentRequired") {
            showStatus(preventiveScheduleStatus, "error", getErrorMessage(error));
            return;
        }

        showStatus(preventiveScheduleStatus, "error", payload.message || getErrorMessage(error));
        const consentUrl = payload.consentUrl || urls.calendarConsent;
        if (!consentUrl || !preventiveScheduleStatus) {
            return;
        }

        const link = document.createElement("a");
        link.className = "copiers-link";
        link.href = consentUrl;
        link.textContent = "Autorizar calendario";
        preventiveScheduleStatus.appendChild(document.createTextNode(" "));
        preventiveScheduleStatus.appendChild(link);
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

    function returnToCopiersTable(tab) {
        const target = tab === "deliveries"
            ? deliveriesBody?.closest(".copiers-panel")
            : maintenanceBody?.closest(".copiers-panel");

        if (target && typeof target.scrollIntoView === "function") {
            window.setTimeout(() => {
                target.scrollIntoView({ behavior: "smooth", block: "start" });
            }, 50);
        }
    }

    function showSaveConfirmation(message) {
        if (message && typeof window.alert === "function") {
            window.alert(message);
        }
    }

    function findById(rows, id) {
        return (rows || []).find((row) => row.recordId === id);
    }

    function findPreventiveClient(clientKey) {
        const clients = Array.isArray(state.preventiveMaintenance?.clients) ? state.preventiveMaintenance.clients : [];
        return clients.find((client) => (client.clientKey || client.clientId || client.clientName || "") === clientKey) || null;
    }

    function findPreventiveEquipment(client, equipmentId) {
        const equipment = Array.isArray(client?.equipment) ? client.equipment : [];
        return equipment.find((row) => (row.recordId || "") === (equipmentId || "")) || null;
    }

    function updatePreventivePeriodButtons() {
        preventivePeriodButtons.forEach((button) => {
            const period = button.dataset.copiersPreventivePeriod === "previous-month" ? "previous-month" : "this-month";
            const isActive = period === (state.preventivePeriod || "this-month");
            button.classList.toggle("is-active", isActive);
            button.setAttribute("aria-pressed", isActive ? "true" : "false");
        });
    }

    function selectedPreventiveDateValue() {
        if ((state.preventivePeriod || "this-month") === "previous-month") {
            return state.preventiveMaintenance?.periodEndValue || todayValue();
        }

        return todayValue();
    }

    function getPreventiveActionClass(tone) {
        const normalized = (tone || "").trim().replace(/^outline-/, "");
        if (normalized === "success" || normalized === "good") {
            return "copiers-preventive-action--success";
        }
        if (normalized === "warning" || normalized === "pending") {
            return "copiers-preventive-action--warning";
        }
        if (normalized === "danger") {
            return "copiers-preventive-action--danger";
        }
        if (normalized === "secondary") {
            return "copiers-preventive-action--secondary";
        }
        return "copiers-preventive-action--primary";
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

    function formatNullableNumber(value) {
        if (value === null || value === undefined || value === "") {
            return "-";
        }

        const numeric = Number(value);
        return Number.isFinite(numeric) ? numberFormatter.format(numeric) : "-";
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

    function defaultTimeValue() {
        const now = new Date();
        now.setMinutes(0, 0, 0);
        now.setHours(now.getHours() + 1);
        return `${String(now.getHours()).padStart(2, "0")}:00`;
    }

    function debounce(callback, delay) {
        let handle = 0;
        return (...args) => {
            window.clearTimeout(handle);
            handle = window.setTimeout(() => callback(...args), delay);
        };
    }
})();
