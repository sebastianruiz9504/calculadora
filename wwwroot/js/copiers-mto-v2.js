(function () {
    "use strict";

    const root = document.getElementById("copiersMtoV2App");
    const form = document.getElementById("mtoV2Form");
    if (!root || !(form instanceof HTMLFormElement)) {
        return;
    }

    const totalSteps = 4;
    const maxFiles = Number(root.dataset.maxFiles || 8);
    const maxFileBytes = Number(root.dataset.maxFileBytes || 8 * 1024 * 1024);
    const maxTotalBytes = Number(root.dataset.maxTotalBytes || 20 * 1024 * 1024);
    const allowedExtensions = new Set(["jpg", "jpeg", "png"]);
    const submissionStorageKey = "copiers-mto-v2:submission-id";
    const signatureBoundFieldIds = new Set([
        "mtoV2ClientName",
        "mtoV2EquipmentSerial",
        "mtoV2ServiceStartedAtLocal",
        "mtoV2OnsiteContactName",
        "mtoV2OnsiteContactEmail",
        "mtoV2MaintenanceType",
        "mtoV2ServiceResult",
        "mtoV2WorkPerformed",
        "mtoV2CopiesBefore",
        "mtoV2CopiesAfter",
        "mtoV2ScansBefore",
        "mtoV2ScansAfter",
        "mtoV2Recommendations",
        "mtoV2CustomerObservations",
        "mtoV2SignerName",
        "mtoV2SignerRole",
        "mtoV2MovementReason", "mtoV2SupplyId", "mtoV2SupplyQuantity"
    ]);

    const elements = {
        panels: Array.from(root.querySelectorAll("[data-step-panel]")),
        indicators: Array.from(root.querySelectorAll("[data-step-indicator]")),
        stepTargets: Array.from(root.querySelectorAll("[data-step-target]")),
        nextButtons: Array.from(root.querySelectorAll("[data-next-step]")),
        previousButtons: Array.from(root.querySelectorAll("[data-previous-step]")),
        status: document.getElementById("mtoV2Status"),
        submitStatus: document.getElementById("mtoV2SubmitStatus"),
        previousAttempt: document.getElementById("mtoV2PreviousAttempt"),
        technicianName: document.getElementById("mtoV2TechnicianName"),
        retryBootstrap: document.getElementById("mtoV2RetryBootstrap"),
        submissionKey: document.getElementById("mtoV2SubmissionKey"),
        recordId: document.getElementById("mtoV2RecordId"),
        expectedVersion: document.getElementById("mtoV2ExpectedVersion"),
        formVersion: document.getElementById("mtoV2FormVersion"),
        activityKind: document.getElementById("mtoV2ActivityKind"),
        activityPanels: Array.from(root.querySelectorAll("[data-activity-panel]")),
        clientLabel: document.getElementById("mtoV2ClientLabel"),
        originClientId: document.getElementById("mtoV2OriginClientId"),
        originClientName: document.getElementById("mtoV2OriginClientName"),
        movementReason: document.getElementById("mtoV2MovementReason"),
        retryEquipment: document.getElementById("mtoV2RetryEquipment"),
        supplyId: document.getElementById("mtoV2SupplyId"),
        supplyQuantity: document.getElementById("mtoV2SupplyQuantity"),
        supplyStock: document.getElementById("mtoV2SupplyStock"),
        supplyFeedback: document.getElementById("mtoV2SupplyFeedback"),
        retrySupplies: document.getElementById("mtoV2RetrySupplies"),
        createAnother: document.getElementById("mtoV2CreateAnother"),
        checkEmailStatus: document.getElementById("mtoV2CheckEmailStatus"),
        answersJson: document.getElementById("mtoV2AnswersJson"),
        title: document.getElementById("mtoV2Title"),
        serviceDate: document.getElementById("mtoV2ServiceDate"),
        startedAtUtc: document.getElementById("mtoV2StartedAtUtc"),
        signedAtUtc: document.getElementById("mtoV2SignedAtUtc"),
        serviceStartedAtUtc: document.getElementById("mtoV2ServiceStartedAtUtc"),
        serviceEndedAtUtc: document.getElementById("mtoV2ServiceEndedAtUtc"),
        signatureStartedAt: document.getElementById("mtoV2SignatureStartedAt"),
        signatureEndedAt: document.getElementById("mtoV2SignatureEndedAt"),
        submittedAtUtc: document.getElementById("mtoV2SubmittedAtUtc"),
        serviceStartedAtLocal: document.getElementById("mtoV2ServiceStartedAtLocal"),
        latitude: document.getElementById("mtoV2Latitude"),
        longitude: document.getElementById("mtoV2Longitude"),
        accuracy: document.getElementById("mtoV2Accuracy"),
        geoCapturedAtUtc: document.getElementById("mtoV2GeoCapturedAtUtc"),
        geoStatus: document.getElementById("mtoV2GeoStatus"),
        clientName: document.getElementById("mtoV2ClientName"),
        clientId: document.getElementById("mtoV2ClientId"),
        clientOptions: document.getElementById("mtoV2ClientOptions"),
        catalogFeedback: document.getElementById("mtoV2CatalogFeedback"),
        equipmentSerial: document.getElementById("mtoV2EquipmentSerial"),
        equipmentId: document.getElementById("mtoV2EquipmentId"),
        equipmentOptions: document.getElementById("mtoV2EquipmentOptions"),
        equipmentFeedback: document.getElementById("mtoV2EquipmentFeedback"),
        serviceReference: document.getElementById("mtoV2ServiceReference"),
        onsiteContactName: document.getElementById("mtoV2OnsiteContactName"),
        onsiteContactEmail: document.getElementById("mtoV2OnsiteContactEmail"),
        maintenanceType: document.getElementById("mtoV2MaintenanceType"),
        contactFeedback: document.getElementById("mtoV2ContactFeedback"),
        customerEmailFeedback: document.getElementById("mtoV2CustomerEmailFeedback"),
        editClientEmail: document.getElementById("mtoV2EditClientEmail"),
        clientEmailDialog: document.getElementById("mtoV2ClientEmailDialog"),
        clientEmailForm: document.getElementById("mtoV2ClientEmailForm"),
        emailClientName: document.getElementById("mtoV2EmailClientName"),
        clientEmailEditor: document.getElementById("mtoV2ClientEmailEditor"),
        emailSaveStatus: document.getElementById("mtoV2EmailSaveStatus"),
        saveClientEmail: document.getElementById("mtoV2SaveClientEmail"),
        cancelClientEmail: document.getElementById("mtoV2CancelClientEmail"),
        evidenceInput: document.getElementById("mtoV2EvidenceInput"),
        cameraInput: document.getElementById("mtoV2CameraInput"),
        fileSummary: document.getElementById("mtoV2FileSummary"),
        fileList: document.getElementById("mtoV2FileList"),
        signatureCanvas: document.getElementById("mtoV2SignatureCanvas"),
        signaturePointCount: document.getElementById("mtoV2SignaturePointCount"),
        signatureFeedback: document.getElementById("mtoV2SignatureFeedback"),
        clearSignature: document.getElementById("mtoV2ClearSignature"),
        signerName: document.getElementById("mtoV2SignerName"),
        customerAccepted: document.getElementById("mtoV2CustomerAcceptance"),
        review: document.getElementById("mtoV2Review"),
        finalReviewConfirmed: document.getElementById("mtoV2FinalReviewConfirmed"),
        submitButton: document.getElementById("mtoV2SubmitButton"),
        submitLabel: root.querySelector("[data-submit-label]"),
        copiesBefore: document.getElementById("mtoV2CopiesBefore"),
        copiesAfter: document.getElementById("mtoV2CopiesAfter"),
        scansBefore: document.getElementById("mtoV2ScansBefore"),
        scansAfter: document.getElementById("mtoV2ScansAfter"),
        copiesBeforeDate: document.getElementById("mtoV2CopiesBeforeDate"),
        scansBeforeDate: document.getElementById("mtoV2ScansBeforeDate"),
        counterFeedback: document.getElementById("mtoV2CounterFeedback"),
        retryCounters: document.getElementById("mtoV2RetryCounters")
    };

    const state = {
        currentStep: 1,
        maxUnlockedStep: 1,
        files: [],
        previewUrls: [],
        submitting: false,
        submissionAttempted: false,
        submissionScope: "",
        locationAttempted: false,
        savingClientEmail: false,
        editingClientId: "",
        clientPicker: null,
        equipmentPicker: null,
        activityType: "",
        equipmentCatalog: { scope: "", requestId: 0, loaded: false, loading: false, allowExternalEquipment: false },
        supplies: { requestId: 0, loaded: false, loading: false, items: [], selected: null },
        emailStatus: { requestId: 0, attempts: 0, loading: false, timer: null },
        counters: { scope: "", requestId: 0, loading: false, loaded: false, error: "", recordId: "", dateValue: "", dateDisplay: "" },
        catalog: {
            loaded: false,
            loading: false,
            schemaReady: false,
            activitiesEnabled: false,
            clients: [],
            equipment: [],
            maintenanceTypes: [],
            selectedClient: null,
            selectedEquipment: null
        },
        signature: {
            context: null,
            strokes: [],
            activeStroke: null,
            activePointerId: null,
            resizeObserver: null
        }
    };

    initialize();

    function initialize() {
        initializeSubmission();
        initializeDefaults();
        updateNarrativeCounts();
        initializeCatalogPickers();
        wireEvents();
        void loadBootstrap();
        initializeSignaturePad();
        renderFiles();
        showStep(1, { scroll: false });
    }

    function initializeCatalogPickers() {
        state.clientPicker = window.CopiersMtoV2Picker.create(elements.clientName, elements.clientOptions, {
            onSelect(item) {
                elements.clientName.value = item.label;
                syncClientSelection(item.id);
                invalidateSignatureForChange();
                elements.clientName.classList.remove("is-invalid");
            }
        });
        state.equipmentPicker = window.CopiersMtoV2Picker.create(elements.equipmentSerial, elements.equipmentOptions, {
            onSelect(item) {
                elements.equipmentSerial.value = item.label;
                syncEquipmentSelection(item.id);
                invalidateSignatureForChange();
                elements.equipmentSerial.classList.remove("is-invalid");
            }
        });
    }

    // Older mobile engines may not provide Element.replaceChildren.
    function replaceContents(parent, ...children) {
        if (!parent) return;
        if (typeof parent.replaceChildren === "function") parent.replaceChildren(...children);
        else {
            while (parent.firstChild) parent.removeChild(parent.firstChild);
            children.forEach(child => parent.appendChild(child));
        }
    }

    function initializeSubmission() {
        // The form is not restored on reload, so its previous key must not be
        // restored on its own. Keep it only as a reminder of an unconfirmed send.
        if (elements.previousAttempt) {
            elements.previousAttempt.hidden = !readStoredSubmissionId();
        }
        elements.submissionKey.value = createSubmissionId();
        elements.startedAtUtc.value = new Date().toISOString();
    }

    function prepareSubmissionIdentity() {
        const scope = `${activityKind()}|${elements.clientId.value.trim().toLowerCase()}|${elements.equipmentId.value.trim().toLowerCase() || catalogKey(elements.equipmentSerial?.value)}`;
        if (state.submissionAttempted && state.submissionScope !== scope) {
            // A different activity/customer/equipment is a new capture, never a retry
            // that rewrites the previously signed row. Same-form retries keep key.
            elements.submissionKey.value = createSubmissionId();
            elements.recordId.value = "";
            elements.expectedVersion.value = "";
            elements.serviceReference.value = "";
            elements.submittedAtUtc.value = "";
            state.locationAttempted = false;
            [elements.latitude, elements.longitude, elements.accuracy, elements.geoCapturedAtUtc].forEach(field => { field.value = ""; });
            elements.geoStatus.value = "pending";
        }
        state.submissionScope = scope;
        state.submissionAttempted = true;
        storeSubmissionId(elements.submissionKey.value);
    }

    function initializeDefaults() {
        if (elements.serviceStartedAtLocal && !elements.serviceStartedAtLocal.value) {
            elements.serviceStartedAtLocal.value = toLocalDateTimeValue(new Date());
        }
    }

    async function loadBootstrap() {
        if (state.catalog.loading || state.catalog.loaded) {
            return;
        }

        state.catalog.loading = true;
        elements.retryBootstrap.hidden = true;
        elements.retryBootstrap.disabled = true;
        setCatalogFeedback(elements.catalogFeedback, "Cargando clientes de Copiers…", "");
        setCatalogFeedback(elements.equipmentFeedback, "Selecciona primero un cliente.", "");
        state.clientPicker.setStatus("Cargando clientes de Copiers…");
        state.equipmentPicker.setStatus("Selecciona primero un cliente.");

        try {
            const result = await fetchCatalog();

            const clients = Array.isArray(result?.clients) ? result.clients : Array.isArray(result?.Clients) ? result.Clients : [];
            const equipment = Array.isArray(result?.equipment) ? result.equipment : Array.isArray(result?.Equipment) ? result.Equipment : [];
            const maintenanceTypes = Array.isArray(result?.maintenanceTypes) ? result.maintenanceTypes : Array.isArray(result?.MaintenanceTypes) ? result.MaintenanceTypes : [];
            state.catalog.schemaReady = (result?.schemaReady ?? result?.SchemaReady) === true;
            state.catalog.activitiesEnabled = (result?.activitiesEnabled ?? result?.ActivitiesEnabled) === true;
            state.catalog.clients = clients.map(normalizeClient).filter(Boolean);
            state.catalog.equipment = equipment.map(normalizeEquipment).filter(Boolean);
            state.catalog.maintenanceTypes = maintenanceTypes.map(normalizeMaintenanceType).filter(Boolean);
            if (!state.catalog.clients.length) {
                throw new Error("El catálogo no devolvió clientes disponibles.");
            }

            state.catalog.loaded = true;
            const technicianName = textProperty(result, "technicianName", "TechnicianName");
            if (technicianName && elements.technicianName) {
                elements.technicianName.textContent = technicianName;
            }
            renderClientOptions();
            renderMaintenanceTypeOptions();
            changeActivityType();
            syncClientSelection();
            if (!state.catalog.schemaReady) {
                setCatalogFeedback(
                    elements.catalogFeedback,
                    "El esquema de MTO Firmado V2 aún no está aprovisionado. Reintenta cuando termine la configuración.",
                    "error");
                elements.retryBootstrap.hidden = false;
            }
        } catch (error) {
            state.catalog.loaded = false;
            state.catalog.schemaReady = false;
            const message = error instanceof Error ? error.message : "No fue posible cargar los clientes.";
            state.clientPicker.setStatus(message);
            state.equipmentPicker.setStatus("Catálogo no disponible. Reintenta la carga.");
            setCatalogFeedback(
                elements.catalogFeedback,
                error instanceof Error ? error.message : "No fue posible cargar los clientes.",
                "error");
            setCatalogFeedback(elements.equipmentFeedback, "Catálogo de equipos no disponible.", "error");
            elements.retryBootstrap.hidden = false;
        } finally {
            state.catalog.loading = false;
            elements.retryBootstrap.disabled = false;
        }
    }

    async function fetchCatalog() {
        const controller = typeof AbortController === "function" ? new AbortController() : null;
        let timer;
        const deadline = new Promise((_, reject) => {
            timer = window.setTimeout(() => {
                reject(new Error("La consulta tardó demasiado. Revisa tu conexión y pulsa Reintentar carga del catálogo."));
                controller?.abort();
            }, 25000);
        });
        try {
            return await Promise.race([readCatalogResponse(controller?.signal), deadline]);
        } finally {
            window.clearTimeout(timer);
        }
    }

    async function readCatalogResponse(signal) {
        let response;
        try {
            response = await fetch(root.dataset.bootstrapUrl || "/CopiersMtoV2/Bootstrap", {
                method: "GET", credentials: "same-origin", cache: "no-store",
                headers: { Accept: "application/json" }, signal
            });
        } catch (error) {
            if (error?.name === "AbortError") throw error;
            throw new Error("No se pudo conectar con Copiers. Revisa internet y pulsa Reintentar carga del catálogo.");
        }
        if (response.status === 401 || response.status === 403 || response.redirected
            || (response.ok && !(response.headers.get("content-type") || "").includes("application/json"))) {
            throw new Error("Tu sesión expiró o no tiene acceso a Copiers. Actualiza la página e inicia sesión nuevamente.");
        }
        const result = await readResponse(response);
        if (!response.ok) throw new Error(result?.message || result?.Message || `No fue posible cargar el catálogo (${response.status}). Pulsa Reintentar carga del catálogo.`);
        return result;
    }

    function normalizeClient(item) {
        const id = textProperty(item, "id", "Id");
        const name = textProperty(item, "name", "Name");
        if (!id || !name) {
            return null;
        }
        return {
            id,
            name,
            contactName: textProperty(item, "contactName", "ContactName"),
            email: textProperty(item, "email", "Email")
        };
    }

    function normalizeEquipment(item) {
        const id = textProperty(item, "id", "Id");
        const serial = textProperty(item, "serial", "Serial");
        const clientId = textProperty(item, "clientId", "ClientId");
        if (!id || !serial) {
            return null;
        }
        return {
            id,
            serial,
            clientId,
            clientName: textProperty(item, "clientName", "ClientName"),
            reference: textProperty(item, "reference", "Reference")
        };
    }

    function normalizeMaintenanceType(item) {
        const rawValue = item?.value ?? item?.Value;
        const label = textProperty(item, "label", "Label");
        const value = Number(rawValue);
        return Number.isInteger(value) && value > 0 && label ? { value, label } : null;
    }

    function renderMaintenanceTypeOptions() {
        const placeholder = document.createElement("option");
        placeholder.value = "";
        placeholder.textContent = "Selecciona una opción";
        const options = state.catalog.maintenanceTypes.map(item => {
            const option = document.createElement("option");
            option.value = String(item.value);
            option.textContent = item.label;
            return option;
        });
        if (state.catalog.activitiesEnabled) {
            for (const [value, label] of [["movement", "Movimiento de equipo"], ["toner", "Entrega de tóner"]]) {
                const option = document.createElement("option");
                option.value = value;
                option.textContent = label;
                options.push(option);
            }
        }
        replaceContents(elements.maintenanceType, placeholder, ...options);
    }

    function activityKind() {
        const kind = elements.activityKind?.value;
        return kind === "movement" || kind === "toner" ? kind : "maintenance";
    }

    function activityLabel() {
        return activityKind() === "movement" ? "Movimiento de equipo" : activityKind() === "toner" ? "Entrega de tóner" : "Mantenimiento";
    }

    function changeActivityType() {
        const value = elements.maintenanceType.value;
        if ((value === "movement" || value === "toner") && !state.catalog.activitiesEnabled) {
            elements.maintenanceType.value = "";
            return;
        }
        elements.activityKind.value = value === "movement" || value === "toner" ? value : "maintenance";
        elements.formVersion.value = activityKind() === "maintenance" ? "copiers-mto-v2-2026-09-10" : "copiers-activity-v2-2026-09-10";
        elements.clientLabel.textContent = activityKind() === "movement" ? "Cliente destino" : "Cliente";
        elements.activityPanels.forEach(panel => {
            const active = panel.dataset.activityPanel === activityKind();
            panel.hidden = !active;
            panel.querySelectorAll("input, select, textarea").forEach(field => { field.disabled = !active; });
        });
        if (state.activityType !== value) {
            state.activityType = value;
            state.maxUnlockedStep = 1;
            invalidateSignatureForChange();
            elements.finalReviewConfirmed.checked = false;
            updateProgressAvailability();
            syncEquipmentCatalog();
            if (activityKind() === "toner") void loadSupplies();
            else { state.supplies.requestId += 1; state.supplies.loading = false; }
        }
    }

    function isExternalEquipment() {
        return activityKind() === "maintenance" && state.equipmentCatalog.loaded
            && state.equipmentCatalog.allowExternalEquipment === true && state.catalog.equipment.length === 0;
    }

    function syncEquipmentCatalog(force) {
        const clientId = state.catalog.selectedClient?.id || "";
        const scope = clientId ? `${activityKind()}|${clientId.toLowerCase()}` : "";
        if (!force && scope === state.equipmentCatalog.scope) return;
        state.equipmentCatalog.scope = scope;
        state.equipmentCatalog.requestId += 1;
        state.equipmentCatalog.loaded = false;
        state.equipmentCatalog.loading = false;
        state.equipmentCatalog.allowExternalEquipment = false;
        state.catalog.equipment = [];
        state.catalog.selectedEquipment = null;
        elements.equipmentSerial.value = "";
        elements.equipmentId.value = "";
        elements.originClientId.value = "";
        elements.originClientName.value = "";
        elements.retryEquipment.hidden = true;
        renderEquipmentOptions();
        syncCounterSelection();
        if (clientId) void loadEquipmentCatalog();
    }

    async function loadEquipmentCatalog() {
        const client = state.catalog.selectedClient;
        if (!client || state.equipmentCatalog.loading) return;
        const requestId = ++state.equipmentCatalog.requestId;
        const scope = state.equipmentCatalog.scope;
        const kind = activityKind();
        state.equipmentCatalog.loading = true;
        state.equipmentCatalog.loaded = false;
        state.equipmentCatalog.allowExternalEquipment = false;
        elements.retryEquipment.hidden = true;
        elements.retryEquipment.disabled = true;
        state.equipmentPicker.setStatus("Cargando equipos disponibles…");
        setCatalogFeedback(elements.equipmentFeedback, "Cargando equipos disponibles…", "");
        try {
            const url = `${root.dataset.equipmentUrl || "/CopiersMtoV2/Equipment"}?clientId=${encodeURIComponent(client.id)}&activityKind=${encodeURIComponent(kind)}`;
            const result = await fetchActivityJson(url, "los equipos");
            if (requestId !== state.equipmentCatalog.requestId || scope !== state.equipmentCatalog.scope) return;
            const rawItems = result?.items ?? result?.Items;
            if (!Array.isArray(rawItems)) throw new Error("La respuesta de equipos no es válida. Reintenta la consulta.");
            state.catalog.equipment = rawItems.map(normalizeEquipment).filter(Boolean);
            if (state.catalog.equipment.length !== rawItems.length) throw new Error("El catálogo contiene equipos incompletos. Reintenta la consulta.");
            state.equipmentCatalog.allowExternalEquipment = kind === "maintenance" && rawItems.length === 0
                && (result?.allowExternalEquipment ?? result?.AllowExternalEquipment) === true;
            state.equipmentCatalog.loaded = true;
            renderEquipmentOptions();
            syncEquipmentSelection();
        } catch (error) {
            if (requestId !== state.equipmentCatalog.requestId || scope !== state.equipmentCatalog.scope) return;
            state.equipmentCatalog.loaded = false;
            state.equipmentCatalog.allowExternalEquipment = false;
            state.catalog.equipment = [];
            const message = error instanceof Error ? error.message : "No fue posible consultar los equipos. Reintenta.";
            state.equipmentPicker.setStatus(message);
            setCatalogFeedback(elements.equipmentFeedback, message, "error");
            elements.retryEquipment.hidden = false;
        } finally {
            if (requestId === state.equipmentCatalog.requestId && scope === state.equipmentCatalog.scope) {
                state.equipmentCatalog.loading = false;
                elements.retryEquipment.disabled = false;
            }
        }
    }

    async function fetchActivityJson(url, label) {
        const controller = typeof AbortController === "function" ? new AbortController() : null;
        let timer;
        const deadline = new Promise((_, reject) => {
            timer = window.setTimeout(() => {
                reject(new Error(`La consulta de ${label} tardó demasiado. Revisa internet y reintenta.`));
                controller?.abort();
            }, 20000);
        });
        const request = async () => {
            const response = await fetch(url, { method: "GET", credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" }, signal: controller?.signal });
            if (response.status === 401 || response.status === 403 || response.redirected
                || (response.ok && !(response.headers.get("content-type") || "").includes("application/json"))) {
                throw new Error("Tu sesión expiró o no tiene acceso. Actualiza la página e inicia sesión nuevamente.");
            }
            const result = await readResponse(response);
            if (!response.ok) throw new Error(result?.message || result?.Message || `No fue posible consultar ${label}. Reintenta.`);
            return result;
        };
        try { return await Promise.race([request(), deadline]); }
        catch (error) {
            if (error instanceof TypeError) throw new Error(`No fue posible conectar para consultar ${label}. Revisa internet y reintenta.`);
            throw error;
        }
        finally { window.clearTimeout(timer); }
    }

    function renderClientOptions() {
        state.clientPicker.setItems(state.catalog.clients.map(client => ({
            id: client.id, label: client.name, description: client.contactName || ""
        })));
        setCatalogFeedback(
            elements.catalogFeedback,
            `${state.catalog.clients.length} clientes disponibles. Selecciona una coincidencia de la lista.`,
            "success");
    }

    async function loadSupplies() {
        if (activityKind() !== "toner" || state.supplies.loading) return;
        const requestId = ++state.supplies.requestId;
        state.supplies.loading = true;
        state.supplies.loaded = false;
        elements.retrySupplies.hidden = true;
        elements.retrySupplies.disabled = true;
        setCatalogFeedback(elements.supplyFeedback, "Consultando suministros y existencias…", "");
        try {
            const result = await fetchActivityJson(root.dataset.suppliesUrl || "/CopiersMtoV2/Supplies", "suministros");
            if (requestId !== state.supplies.requestId || activityKind() !== "toner") return;
            const rawItems = result?.items ?? result?.Items;
            if (!Array.isArray(rawItems)) throw new Error("No se recibió un catálogo válido de suministros. Reintenta.");
            state.supplies.items = rawItems.map(item => {
                const id = textProperty(item, "id", "Id"), name = textProperty(item, "name", "Name");
                const rawQuantity = item?.quantity ?? item?.Quantity;
                const quantity = Number(rawQuantity);
                if (!id || !name || rawQuantity === null || rawQuantity === undefined || rawQuantity === ""
                    || !Number.isSafeInteger(quantity) || quantity < 0) throw new Error("El catálogo contiene existencias no válidas. Reintenta.");
                return { id, name, quantity };
            });
            const previousId = elements.supplyId.value;
            const placeholder = document.createElement("option");
            placeholder.value = "";
            placeholder.textContent = "Selecciona el tóner a entregar";
            replaceContents(elements.supplyId, placeholder, ...state.supplies.items.map(item => {
                const option = document.createElement("option");
                option.value = item.id;
                option.textContent = `${item.name} · ${item.quantity} disponibles`;
                option.disabled = item.quantity === 0;
                return option;
            }));
            elements.supplyId.value = state.supplies.items.some(item => sameCatalogId(item.id, previousId) && item.quantity > 0) ? previousId : "";
            state.supplies.loaded = true;
            syncSupplySelection();
            setCatalogFeedback(elements.supplyFeedback, state.supplies.items.some(item => item.quantity > 0)
                ? "Selecciona el suministro y la cantidad a entregar." : "No hay suministros con existencias disponibles.", "");
        } catch (error) {
            if (requestId !== state.supplies.requestId || activityKind() !== "toner") return;
            state.supplies.items = [];
            state.supplies.loaded = false;
            elements.supplyId.value = "";
            syncSupplySelection();
            setCatalogFeedback(elements.supplyFeedback, error instanceof Error ? error.message : "No fue posible consultar suministros. Reintenta.", "error");
            elements.retrySupplies.hidden = false;
        } finally {
            if (requestId === state.supplies.requestId) {
                state.supplies.loading = false;
                elements.retrySupplies.disabled = false;
            }
        }
    }

    function syncSupplySelection() {
        const previous = state.supplies.selected;
        const selected = state.supplies.loaded
            ? state.supplies.items.find(item => sameCatalogId(item.id, elements.supplyId.value)) || null : null;
        state.supplies.selected = selected;
        elements.supplyStock.textContent = selected ? `Existencias disponibles: ${selected.quantity}` : "Selecciona un suministro para consultar sus existencias.";
        elements.supplyQuantity.max = selected ? String(selected.quantity) : "";
        elements.supplyId.setCustomValidity("");
        elements.supplyQuantity.setCustomValidity("");
        if (previous?.id !== selected?.id || previous?.name !== selected?.name || previous?.quantity !== selected?.quantity) invalidateSignatureForChange();
    }

    function prepareSupplyValidity() {
        const supply = state.supplies.selected;
        elements.supplyId.setCustomValidity(!state.supplies.loaded
            ? "Espera a que se carguen los suministros o reintenta su consulta."
            : !supply || supply.quantity <= 0 ? "Selecciona un suministro con existencias disponibles." : "");
        const quantity = Number(elements.supplyQuantity.value);
        elements.supplyQuantity.setCustomValidity(!Number.isSafeInteger(quantity) || quantity <= 0 || quantity > 2147483647
            ? "Ingresa una cantidad entera mayor que cero."
            : supply && quantity > supply.quantity ? "La cantidad no puede superar las existencias disponibles." : "");
    }

    function syncClientSelection(preferredId) {
        const previousClient = state.catalog.selectedClient;
        const value = catalogKey(elements.clientName?.value);
        const matches = state.catalog.clients.filter(client => catalogKey(client.name) === value);
        const selected = matches.find(client => sameCatalogId(client.id, preferredId))
            || matches.find(client => sameCatalogId(client.id, previousClient?.id))
            || (matches.length === 1 ? matches[0] : null);

        state.catalog.selectedClient = selected;
        elements.clientId.value = selected?.id || "";
        elements.editClientEmail.disabled = !selected || state.submitting;
        elements.clientName?.setCustomValidity("");

        if (selected) {
            prefillClientContact(selected, previousClient);
            setCatalogFeedback(elements.catalogFeedback, `Cliente seleccionado: ${selected.name}.`, "success");
        } else if (previousClient) {
            clearClientPrefill(previousClient);
            setCatalogFeedback(
                elements.catalogFeedback,
                value
                    ? "Selecciona un cliente válido de la lista."
                    : `${state.catalog.clients.length} clientes disponibles. Selecciona una coincidencia de la lista.`,
                value ? "error" : "");
        } else if (state.catalog.loaded && value) {
            setCatalogFeedback(elements.catalogFeedback, "Selecciona un cliente válido de la lista.", "error");
        } else if (state.catalog.loaded) {
            setCatalogFeedback(
                elements.catalogFeedback,
                `${state.catalog.clients.length} clientes disponibles. Selecciona una coincidencia de la lista.`,
                "");
        }

        if (state.catalog.loaded && !state.catalog.schemaReady) {
            setCatalogFeedback(
                elements.catalogFeedback,
                "El esquema de MTO Firmado V2 aún no está aprovisionado. Reintenta cuando termine la configuración.",
                "error");
        }

        if (activityKind() !== "movement" && state.catalog.selectedEquipment && !sameCatalogId(state.catalog.selectedEquipment.clientId, selected?.id)) {
            const previousEquipment = state.catalog.selectedEquipment;
            if (catalogKey(elements.equipmentSerial?.value) === catalogKey(previousEquipment.serial)) {
                elements.equipmentSerial.value = "";
            }
            state.catalog.selectedEquipment = null;
            elements.equipmentId.value = "";
        }

        syncEquipmentCatalog();
        if (state.equipmentCatalog.loaded) {
            renderEquipmentOptions();
            syncEquipmentSelection();
        }
    }

    function prefillClientContact(client, previousClient) {
        if (client.contactName) {
            elements.onsiteContactName.value = client.contactName;
            elements.onsiteContactName.readOnly = true;
            setCatalogFeedback(elements.contactFeedback, "Contacto registrado en Copiers.", "success");
        } else {
            if (previousClient && !sameCatalogId(previousClient.id, client.id)) {
                elements.onsiteContactName.value = "";
            }
            elements.onsiteContactName.readOnly = false;
            setCatalogFeedback(elements.contactFeedback, "Copiers no tiene contacto; escríbelo para este reporte.", "");
        }

        elements.onsiteContactEmail.value = client.email || "";
        elements.onsiteContactEmail.readOnly = true;
        setCatalogFeedback(
            elements.customerEmailFeedback,
            client.email
                ? "Correo del encargado de Copiers. Puedes editarlo con el botón + junto al cliente."
                : "Agrega el correo del encargado de Copiers con el botón + junto al cliente para continuar.",
            client.email ? "success" : "error");

        replaceCatalogPrefill(elements.signerName, previousClient?.contactName, client.contactName);
    }

    function clearClientPrefill(previousClient) {
        if (catalogKey(elements.onsiteContactName?.value) === catalogKey(previousClient.contactName)) {
            elements.onsiteContactName.value = "";
        }
        if (catalogKey(elements.signerName?.value) === catalogKey(previousClient.contactName)) {
            elements.signerName.value = "";
        }
        elements.onsiteContactName.readOnly = false;
        elements.onsiteContactEmail.value = "";
        setCatalogFeedback(elements.contactFeedback, "Selecciona un cliente para completar el contacto.", "");
        setCatalogFeedback(elements.customerEmailFeedback, "Selecciona un cliente para consultar el correo autorizado.", "");
    }

    function replaceCatalogPrefill(input, previousValue, nextValue) {
        if (!input || !nextValue) {
            return;
        }
        const currentValue = String(input.value || "").trim();
        if (!currentValue || (previousValue && catalogKey(currentValue) === catalogKey(previousValue))) {
            input.value = nextValue;
            input.classList.remove("is-invalid");
            input.setCustomValidity("");
        }
    }

    function renderEquipmentOptions() {
        const clientId = state.catalog.selectedClient?.id || "";
        const filtered = clientId
            ? state.catalog.equipment.filter(item => activityKind() === "movement" || sameCatalogId(item.clientId, clientId))
            : [];
        state.equipmentPicker.setItems(filtered.map(item => ({
            id: item.id, label: item.serial, description: activityKind() === "movement"
                ? [item.clientName || "Stock", item.reference].filter(Boolean).join(" · ") : item.reference || ""
        })));
        if (!clientId) state.equipmentPicker.setStatus("Selecciona primero un cliente.");

        if (!state.catalog.loaded) {
            return;
        }
        if (!clientId) {
            setCatalogFeedback(elements.equipmentFeedback, "Selecciona primero un cliente.", "");
        } else if (!state.equipmentCatalog.loaded) {
            setCatalogFeedback(elements.equipmentFeedback, "Consultando los equipos disponibles…", "");
        } else if (filtered.length) {
            setCatalogFeedback(
                elements.equipmentFeedback,
                `${filtered.length} equipos disponibles. Selecciona un serial de la lista.`,
                "success");
        } else if (isExternalEquipment()) {
            state.equipmentPicker.setStatus("Sin equipos registrados: escribe el serial del equipo atendido.");
            setCatalogFeedback(elements.equipmentFeedback, "Este cliente no tiene equipos registrados. Escribe el serial del equipo atendido.", "");
        } else {
            setCatalogFeedback(elements.equipmentFeedback, "Este cliente no tiene equipos asociados en Dataverse.", "error");
        }
    }

    function syncEquipmentSelection(preferredId) {
        const clientId = state.catalog.selectedClient?.id || "";
        const value = catalogKey(elements.equipmentSerial?.value);
        const matches = state.catalog.equipment.filter(item =>
            (activityKind() === "movement" || sameCatalogId(item.clientId, clientId)) && catalogKey(item.serial) === value);
        const selected = matches.find(item => sameCatalogId(item.id, preferredId))
            || matches.find(item => sameCatalogId(item.id, state.catalog.selectedEquipment?.id))
            || (matches.length === 1 ? matches[0] : null);

        state.catalog.selectedEquipment = selected;
        elements.equipmentId.value = selected?.id || "";
        elements.originClientId.value = activityKind() === "movement" ? selected?.clientId || "" : "";
        elements.originClientName.value = activityKind() === "movement" && selected ? selected.clientName || "Stock" : "";
        elements.equipmentSerial?.setCustomValidity("");

        if (selected) {
            setCatalogFeedback(elements.equipmentFeedback, `Equipo seleccionado: ${selected.serial}.`, "success");
        } else if (isExternalEquipment()) {
            setCatalogFeedback(elements.equipmentFeedback, value ? `Serial del equipo atendido: ${elements.equipmentSerial.value.trim()}.` : "Escribe el serial del equipo atendido.", value ? "success" : "");
        } else if (state.equipmentCatalog.loaded && clientId && value) {
            setCatalogFeedback(elements.equipmentFeedback, "Selecciona un equipo de la lista de este cliente.", "error");
        }
        syncCounterSelection();
    }

    function updateNarrativeCounts() {
        root.querySelectorAll("[data-character-count-for]").forEach(label => {
            const field = document.getElementById(label.dataset.characterCountFor);
            if (!field) return;
            label.textContent = `${field.value.length} / ${field.maxLength} caracteres · Resume el servicio para el reporte de una página.`;
        });
    }

    function syncCounterSelection() {
        const equipment = activityKind() === "maintenance" ? state.catalog.selectedEquipment : null;
        const external = isExternalEquipment();
        const scope = equipment ? `${equipment.clientId}|${equipment.id}`.toLowerCase()
            : external ? `external|${state.catalog.selectedClient?.id}|${catalogKey(elements.equipmentSerial.value)}` : "";
        if (scope === state.counters.scope) return;
        state.counters.scope = scope;
        state.counters.requestId += 1;
        state.counters.loading = false;
        state.counters.loaded = false;
        state.counters.error = "";
        state.counters.recordId = "";
        state.counters.dateValue = "";
        state.counters.dateDisplay = "";
        [elements.copiesBefore, elements.scansBefore, elements.copiesAfter, elements.scansAfter].forEach(field => { field.value = ""; });
        [elements.copiesBeforeDate, elements.scansBeforeDate].forEach(label => { label.textContent = "Sin registro previo"; });
        elements.retryCounters.hidden = true;
        invalidateSignatureForChange();
        if (external) {
            state.counters.loaded = true;
            setCatalogFeedback(elements.counterFeedback, "Equipo sin historial registrado. Ingresa sus lecturas actuales.", "");
        } else if (equipment) void loadCounterLatest();
        else setCatalogFeedback(elements.counterFeedback, "Selecciona un equipo para consultar su último contador.", "");
    }

    async function loadCounterLatest() {
        const equipment = state.catalog.selectedEquipment;
        if (!equipment || activityKind() !== "maintenance" || isExternalEquipment() || state.counters.loading) return;
        const requestId = ++state.counters.requestId;
        const scope = state.counters.scope;
        state.counters.loading = true;
        state.counters.loaded = false;
        state.counters.error = "";
        elements.retryCounters.hidden = true;
        elements.retryCounters.disabled = true;
        setCatalogFeedback(elements.counterFeedback, "Consultando el último contador del equipo…", "");
        try {
            const result = await fetchCounterLatest(equipment.clientId, equipment.id);
            if (requestId !== state.counters.requestId || scope !== state.counters.scope) return;
            const returnedEquipment = textProperty(result, "equipmentId", "EquipmentId");
            if (!sameCatalogId(returnedEquipment, equipment.id)) throw new Error("El contador recibido no corresponde al equipo seleccionado. Reintenta la consulta.");
            const copies = normalizeCounterValue(result?.copiesCounter ?? result?.CopiesCounter);
            const scans = normalizeCounterValue(result?.scansCounter ?? result?.ScansCounter);
            state.counters.recordId = textProperty(result, "recordId", "RecordId");
            state.counters.dateValue = textProperty(result, "dateValue", "DateValue");
            state.counters.dateDisplay = textProperty(result, "dateDisplay", "DateDisplay");
            elements.copiesBefore.value = copies;
            elements.scansBefore.value = scans;
            const dateLabel = state.counters.dateDisplay ? `Último registro: ${state.counters.dateDisplay}` : "Sin fecha registrada";
            elements.copiesBeforeDate.textContent = copies !== "" ? dateLabel : "Sin registro previo";
            elements.scansBeforeDate.textContent = scans !== "" ? dateLabel : "Sin registro previo";
            state.counters.loaded = true;
            setCatalogFeedback(elements.counterFeedback, state.counters.recordId
                ? "Lecturas anteriores cargadas. Registra únicamente las lecturas actuales."
                : "Este equipo no tiene contadores anteriores. Registra sus lecturas actuales.", "success");
        } catch (error) {
            if (requestId !== state.counters.requestId || scope !== state.counters.scope) return;
            state.counters.error = error instanceof Error ? error.message : "No fue posible consultar el contador anterior. Reintenta.";
            setCatalogFeedback(elements.counterFeedback, state.counters.error, "error");
            elements.retryCounters.hidden = false;
        } finally {
            if (requestId === state.counters.requestId && scope === state.counters.scope) {
                state.counters.loading = false;
                elements.retryCounters.disabled = false;
            }
        }
    }

    function normalizeCounterValue(value) {
        if (value === null || value === undefined || value === "") return "";
        const number = Number(value);
        if (!Number.isSafeInteger(number) || number < 0) throw new Error("El contador anterior no es una lectura válida. Revisa el registro del equipo antes de continuar.");
        return String(number);
    }

    async function fetchCounterLatest(clientId, equipmentId) {
        const controller = typeof AbortController === "function" ? new AbortController() : null;
        let timer;
        const deadline = new Promise((_, reject) => {
            timer = window.setTimeout(() => {
                reject(new Error("La consulta del contador tardó demasiado. Revisa la conexión y pulsa Reintentar contadores."));
                controller?.abort();
            }, 20000);
        });
        const request = async () => {
            const url = `${root.dataset.counterLatestUrl || "/CopiersMtoV2/CounterLatest"}?clientId=${encodeURIComponent(clientId)}&equipmentId=${encodeURIComponent(equipmentId)}`;
            const response = await fetch(url, {
                method: "GET", credentials: "same-origin", cache: "no-store",
                headers: { Accept: "application/json" }, signal: controller?.signal
            });
            if (response.status === 401 || response.status === 403 || response.redirected
                || (response.ok && !(response.headers.get("content-type") || "").includes("application/json"))) {
                throw new Error("Tu sesión expiró o no tiene acceso a contadores. Actualiza la página e inicia sesión nuevamente.");
            }
            const result = await readResponse(response);
            if (!response.ok) throw new Error(result?.message || result?.Message || "No fue posible consultar el contador anterior. Pulsa Reintentar contadores.");
            return result;
        };
        try { return await Promise.race([request(), deadline]); }
        catch (error) {
            if (error instanceof TypeError) throw new Error("No se pudo conectar con contadores. Revisa internet y pulsa Reintentar contadores.");
            throw error;
        }
        finally { window.clearTimeout(timer); }
    }

    function setCatalogFeedback(element, message, tone) {
        if (!element) {
            return;
        }
        element.textContent = message;
        element.classList.remove("is-error", "is-success");
        if (tone) {
            element.classList.add(`is-${tone}`);
        }
    }

    function openClientEmailEditor() {
        const client = state.catalog.selectedClient;
        if (!client || state.submitting || state.savingClientEmail) return;
        state.editingClientId = client.id;
        elements.emailClientName.textContent = client.name;
        elements.clientEmailEditor.value = client.email || "";
        elements.clientEmailEditor.setCustomValidity("");
        clearStatus(elements.emailSaveStatus);
        elements.clientEmailDialog.showModal();
        elements.clientEmailEditor.focus();
    }

    async function saveClientEmail(event) {
        event.preventDefault();
        if (state.savingClientEmail || state.submitting || !state.editingClientId) return;
        const email = elements.clientEmailEditor.value.trim();
        elements.clientEmailEditor.value = email;
        if (!elements.clientEmailEditor.reportValidity()) return;
        const clientId = state.editingClientId;
        state.savingClientEmail = true;
        elements.saveClientEmail.disabled = true;
        elements.cancelClientEmail.disabled = true;
        elements.clientEmailEditor.readOnly = true;
        elements.saveClientEmail.textContent = "Guardando…";
        clearStatus(elements.emailSaveStatus);
        try {
            const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
            const response = await fetch(root.dataset.saveClientEmailUrl || "/CopiersMtoV2/SaveClientEmail", {
                method: "POST",
                credentials: "same-origin",
                headers: {
                    Accept: "application/json",
                    "Content-Type": "application/json",
                    RequestVerificationToken: token
                },
                body: JSON.stringify({ clientId, email })
            });
            const result = await readResponse(response);
            if (!response.ok) {
                throw new Error(result?.message || result?.Message || result?.detail || result?.Detail || "No fue posible guardar el correo del cliente.");
            }
            const savedClientId = textProperty(result, "clientId", "ClientId");
            const savedEmail = textProperty(result, "email", "Email");
            if (!sameCatalogId(savedClientId, clientId) || !savedEmail) {
                throw new Error("No se confirmó el correo guardado. Vuelve a intentar.");
            }
            const client = state.catalog.clients.find(item => sameCatalogId(item.id, clientId));
            if (client) client.email = savedEmail;
            if (sameCatalogId(state.catalog.selectedClient?.id, clientId)) {
                const changed = elements.onsiteContactEmail.value !== savedEmail;
                state.catalog.selectedClient.email = savedEmail;
                elements.onsiteContactEmail.value = savedEmail;
                elements.clientName.setCustomValidity("");
                elements.clientName.classList.remove("is-invalid");
                elements.onsiteContactEmail.classList.remove("is-invalid");
                setCatalogFeedback(elements.customerEmailFeedback, "Correo del encargado de Copiers guardado en el cliente. Usa + para editarlo.", "success");
                if (changed) invalidateSignatureForChange();
            }
            elements.clientEmailDialog.close();
        } catch (error) {
            setStatus(elements.emailSaveStatus, "error", error instanceof Error ? error.message : "No fue posible guardar el correo. Inténtalo de nuevo.");
        } finally {
            state.savingClientEmail = false;
            elements.saveClientEmail.disabled = false;
            elements.cancelClientEmail.disabled = false;
            elements.clientEmailEditor.readOnly = false;
            elements.saveClientEmail.textContent = "Guardar correo";
        }
    }

    function wireEvents() {
        elements.nextButtons.forEach(button => {
            button.addEventListener("click", () => {
                const target = Number(button.dataset.nextStep || 0);
                if (!target || target > totalSteps || !validateStep(state.currentStep)) {
                    return;
                }

                if (state.currentStep === 3) {
                    elements.signedAtUtc.value ||= new Date().toISOString();
                }

                state.maxUnlockedStep = Math.max(state.maxUnlockedStep, target);
                showStep(target);
            });
        });

        elements.previousButtons.forEach(button => {
            button.addEventListener("click", () => {
                const target = Number(button.dataset.previousStep || 0);
                if (target >= 1 && target <= totalSteps) {
                    showStep(target);
                }
            });
        });

        elements.stepTargets.forEach(button => {
            button.addEventListener("click", () => {
                const target = Number(button.dataset.stepTarget || 0);
                if (target >= 1 && target <= state.maxUnlockedStep) {
                    showStep(target);
                }
            });
        });

        elements.editClientEmail.addEventListener("click", openClientEmailEditor);
        elements.clientEmailForm.addEventListener("submit", saveClientEmail);
        elements.cancelClientEmail.addEventListener("click", () => {
            if (!state.savingClientEmail) elements.clientEmailDialog.close();
        });
        elements.clientEmailDialog.addEventListener("cancel", event => {
            if (state.savingClientEmail) event.preventDefault();
        });
        elements.clientEmailEditor.addEventListener("input", () => {
            elements.clientEmailEditor.setCustomValidity("");
            clearStatus(elements.emailSaveStatus);
        });
        elements.retryBootstrap?.addEventListener("click", () => {
            state.catalog.loaded = false;
            void loadBootstrap();
        });
        elements.retryCounters?.addEventListener("click", () => { void loadCounterLatest(); });
        elements.retryEquipment?.addEventListener("click", () => syncEquipmentCatalog(true));
        elements.maintenanceType?.addEventListener("change", changeActivityType);
        elements.supplyId?.addEventListener("change", syncSupplySelection);
        elements.retrySupplies?.addEventListener("click", () => { void loadSupplies(); });
        elements.checkEmailStatus?.addEventListener("click", () => startEmailStatusPolling());
        elements.clientName?.addEventListener("input", () => syncClientSelection());
        elements.equipmentSerial?.addEventListener("input", () => syncEquipmentSelection());
        elements.evidenceInput?.addEventListener("change", handleEvidenceSelection);
        elements.cameraInput?.addEventListener("change", handleEvidenceSelection);
        elements.fileList?.addEventListener("click", handleFileListClick);
        elements.clearSignature?.addEventListener("click", clearSignature);
        form.addEventListener("submit", submitForm);
        window.addEventListener("pagehide", () => {
            stopEmailStatusPolling();
            clearFilePreviews();
        });

        form.querySelectorAll("input, select, textarea").forEach(control => {
            const eventName = control instanceof HTMLSelectElement || control.type === "checkbox" ? "change" : "input";
            control.addEventListener(eventName, () => {
                control.classList.remove("is-invalid");
                control.setCustomValidity("");
                clearStatus(elements.status);
                updateNarrativeCounts();
                if (signatureBoundFieldIds.has(control.id)) {
                    invalidateSignatureForChange();
                }
                if (state.currentStep === 4) {
                    renderReview();
                }
            });
        });
    }

    function showStep(step, options) {
        const normalizedStep = Math.min(totalSteps, Math.max(1, Number(step || 1)));
        state.currentStep = normalizedStep;

        elements.panels.forEach(panel => {
            const panelStep = Number(panel.dataset.stepPanel || 0);
            const active = panelStep === normalizedStep;
            panel.hidden = !active;
            panel.classList.toggle("is-active", active);
        });

        elements.indicators.forEach(indicator => {
            const indicatorStep = Number(indicator.dataset.stepIndicator || 0);
            indicator.classList.toggle("is-active", indicatorStep === normalizedStep);
            indicator.classList.toggle("is-complete", indicatorStep < normalizedStep && indicatorStep <= state.maxUnlockedStep);
            const button = indicator.querySelector("button");
            if (button) {
                button.disabled = indicatorStep > state.maxUnlockedStep || state.submitting;
                if (indicatorStep === normalizedStep) {
                    button.setAttribute("aria-current", "step");
                } else {
                    button.removeAttribute("aria-current");
                }
            }
        });

        if (normalizedStep === 4) {
            renderReview();
        } else if (normalizedStep === 3) {
            updateVisitTimes(true);
            resizeSignatureCanvas();
        }

        clearStatus(elements.status);
        if (options?.scroll !== false) {
            const panel = getPanel(normalizedStep);
            window.setTimeout(() => panel?.scrollIntoView({ behavior: "smooth", block: "start" }), 30);
        }
    }

    function validateStep(step) {
        clearCounterValidity();
        if (step === 2 && activityKind() === "maintenance" && !validateCounters()) {
            return false;
        }
        if (step === 2 && activityKind() === "toner") prepareSupplyValidity();

        const panel = getPanel(step);
        if (!panel) {
            return false;
        }

        if (step === 1) {
            prepareCatalogValidity();
            const start = new Date(elements.serviceStartedAtLocal.value);
            elements.serviceStartedAtLocal.setCustomValidity(start.getTime() > Date.now()
                ? "La hora de entrada no puede estar en el futuro." : "");
        }

        const controls = Array.from(panel.querySelectorAll("input, select, textarea"))
            .filter(control => !control.disabled && control.type !== "hidden" && !control.closest("[hidden]"));
        const firstInvalid = controls.find(control => !control.checkValidity());
        if (firstInvalid) {
            firstInvalid.classList.add("is-invalid");
            firstInvalid.reportValidity();
            firstInvalid.focus({ preventScroll: false });
            setStatus(elements.status, "error", "Revisa los campos obligatorios antes de continuar.");
            return false;
        }

        if (step === 3 && !hasSignature()) {
            elements.signatureFeedback.textContent = "La firma del cliente es obligatoria.";
            elements.signatureFeedback.classList.remove("is-success");
            elements.signatureFeedback.classList.add("is-error");
            setStatus(elements.status, "error", "Solicita la firma del cliente antes de revisar el reporte.");
            elements.signatureCanvas?.focus?.();
            return false;
        }

        return true;
    }

    function prepareCatalogValidity() {
        if (!state.catalog.loaded) {
            elements.clientName?.setCustomValidity(
                state.catalog.loading
                    ? "Espera a que termine de cargar el catálogo de clientes."
                    : "No fue posible validar el cliente porque el catálogo no está disponible.");
        } else if (!state.catalog.schemaReady) {
            elements.clientName?.setCustomValidity("El esquema de MTO Firmado V2 aún no está aprovisionado.");
        } else if (!elements.clientId?.value) {
            elements.clientName?.setCustomValidity("Selecciona un cliente válido de la lista.");
        } else if (!state.catalog.selectedClient?.email) {
            elements.clientName?.setCustomValidity("Agrega el correo del encargado de Copiers con el botón + junto al cliente.");
        } else {
            elements.clientName?.setCustomValidity("");
        }

        const serial = catalogKey(elements.equipmentSerial?.value);
        const catalogMatches = state.catalog.equipment.filter(item => catalogKey(item.serial) === serial);
        const belongsToSelectedClient = catalogMatches.some(item => sameCatalogId(item.clientId, elements.clientId?.value));
        if (!state.equipmentCatalog.loaded) {
            elements.equipmentSerial?.setCustomValidity("Espera a que termine la consulta de equipos o pulsa Reintentar equipos.");
        } else if (isExternalEquipment()) {
            elements.equipmentSerial?.setCustomValidity(serial ? "" : "Escribe el serial del equipo atendido.");
        } else if (activityKind() !== "movement" && catalogMatches.length && !belongsToSelectedClient) {
            elements.equipmentSerial?.setCustomValidity("Ese serial está asociado a otro cliente.");
        } else if (!state.catalog.selectedEquipment?.id) {
            elements.equipmentSerial?.setCustomValidity("Selecciona un equipo registrado de la lista de este cliente.");
        } else {
            elements.equipmentSerial?.setCustomValidity("");
        }
    }

    function validateAllSteps() {
        for (let step = 1; step <= 3; step += 1) {
            showStep(step, { scroll: false });
            if (!validateStep(step)) {
                getPanel(step)?.scrollIntoView({ behavior: "smooth", block: "start" });
                return false;
            }
        }

        showStep(4, { scroll: false });

        if (!elements.finalReviewConfirmed?.checked) {
            elements.finalReviewConfirmed.classList.add("is-invalid");
            elements.finalReviewConfirmed.reportValidity();
            setStatus(elements.submitStatus, "error", "Confirma la revisión final antes de enviar.");
            return false;
        }

        return true;
    }

    function validateCounters() {
        if (!state.counters.loaded) {
            setStatus(elements.status, "error", state.counters.loading
                ? "Espera a que termine la consulta del contador anterior."
                : "Consulta el contador anterior con Reintentar contadores antes de continuar.");
            return false;
        }
        const pairs = [
            [elements.copiesBefore, elements.copiesAfter, "El contador de impresiones actual no puede ser menor al anterior."],
            [elements.scansBefore, elements.scansAfter, "El contador de escaneos final no puede ser menor al inicial."]
        ];

        for (const [before, after, message] of pairs) {
            if (!before?.value || !after?.value) {
                continue;
            }
            if (Number(after.value) < Number(before.value)) {
                after.setCustomValidity(message);
                after.classList.add("is-invalid");
                after.reportValidity();
                after.focus();
                setStatus(elements.status, "error", message);
                return false;
            }
        }

        return true;
    }

    function clearCounterValidity() {
        [elements.copiesBefore, elements.copiesAfter, elements.scansBefore, elements.scansAfter].forEach(input => {
            input?.setCustomValidity("");
            input?.classList.remove("is-invalid");
        });
    }

    function captureGeolocation() {
        elements.latitude.value = "";
        elements.longitude.value = "";
        elements.accuracy.value = "";
        elements.geoCapturedAtUtc.value = "";
        if (!window.isSecureContext || !navigator.geolocation) {
            elements.geoStatus.value = "unsupported";
            return Promise.resolve();
        }
        elements.geoStatus.value = "pending";
        return new Promise(resolve => {
            let settled = false;
            const finish = status => {
                if (settled) return;
                settled = true;
                window.clearTimeout(deadline);
                elements.geoStatus.value = status;
                resolve();
            };
            // Some devices never settle the browser permission prompt. Submission remains bounded.
            const deadline = window.setTimeout(() => finish("timeout"), 10000);
            try {
                navigator.geolocation.getCurrentPosition(position => {
                    if (settled) return;
                    const latitude = Number(position.coords.latitude);
                    const longitude = Number(position.coords.longitude);
                    const accuracy = Number(position.coords.accuracy);
                    if (!Number.isFinite(latitude) || Math.abs(latitude) > 90
                        || !Number.isFinite(longitude) || Math.abs(longitude) > 180
                        || !Number.isFinite(accuracy) || accuracy < 0) {
                        finish("invalid");
                        return;
                    }
                    elements.latitude.value = latitude.toFixed(7);
                    elements.longitude.value = longitude.toFixed(7);
                    elements.accuracy.value = accuracy.toFixed(1);
                    elements.geoCapturedAtUtc.value = new Date().toISOString();
                    finish("captured");
                }, error => {
                    const statuses = { 1: "denied", 2: "unavailable", 3: "timeout" };
                    finish(statuses[error.code] || "error");
                }, { enableHighAccuracy: true, timeout: 8000, maximumAge: 0 });
            } catch {
                finish("unavailable");
            }
        });
    }

    function handleEvidenceSelection(event) {
        const input = event?.currentTarget || elements.evidenceInput;
        const selected = Array.from(input?.files || []);
        const errors = [];
        const initialFileCount = state.files.length;
        let runningTotal = state.files.reduce((sum, file) => sum + file.size, 0);

        for (const [selectedIndex, file] of selected.entries()) {
            const displayName = `Archivo ${initialFileCount + selectedIndex + 1}`;
            const extension = getFileExtension(file.name);
            if (!allowedExtensions.has(extension)) {
                errors.push(`${displayName}: formato no permitido.`);
                continue;
            }
            if (file.size <= 0) {
                errors.push(`${displayName}: el archivo está vacío.`);
                continue;
            }
            if (file.size > maxFileBytes) {
                errors.push(`${displayName}: supera ${formatBytes(maxFileBytes)}.`);
                continue;
            }
            if (state.files.some(current => fileKey(current) === fileKey(file))) {
                continue;
            }
            if (state.files.length >= maxFiles) {
                errors.push(`Solo puedes adjuntar ${maxFiles} archivos.`);
                break;
            }
            if (runningTotal + file.size > maxTotalBytes) {
                errors.push(`Los archivos superan el límite total de ${formatBytes(maxTotalBytes)}.`);
                continue;
            }

            state.files.push(file);
            runningTotal += file.size;
        }

        if (input) {
            input.value = "";
        }
        renderFiles();
        const signatureInvalidated = state.files.length !== initialFileCount && invalidateSignatureForChange();

        if (errors.length) {
            setStatus(
                elements.status,
                "error",
                `${errors.join(" ")}${signatureInvalidated ? " La firma anterior se invalidó porque cambiaron los adjuntos." : ""}`);
        } else if (signatureInvalidated) {
            // El aviso de nueva firma reemplaza el estado neutro de carga.
        } else {
            clearStatus(elements.status);
        }
    }

    function handleFileListClick(event) {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }
        const button = target.closest("[data-remove-file]");
        if (!(button instanceof HTMLButtonElement)) {
            return;
        }

        const index = Number(button.dataset.removeFile);
        if (Number.isInteger(index) && index >= 0 && index < state.files.length) {
            state.files.splice(index, 1);
            renderFiles();
            invalidateSignatureForChange();
        }
    }

    function renderFiles() {
        clearFilePreviews();
        const totalBytes = state.files.reduce((sum, file) => sum + file.size, 0);
        elements.fileSummary.textContent = `${state.files.length} de ${maxFiles} archivos · ${formatBytes(totalBytes)} de ${formatBytes(maxTotalBytes)}`;
        replaceContents(elements.fileList, ...state.files.map((file, index) => {
            const item = document.createElement("li");
            const copy = document.createElement("div");
            const name = document.createElement("strong");
            const size = document.createElement("small");
            const remove = document.createElement("button");

            const customerFileName = customerAttachmentName(file, index);
            name.textContent = customerFileName;
            if (typeof URL.createObjectURL === "function") {
                const preview = document.createElement("img");
                preview.className = "mto-v2-file-preview";
                preview.alt = `Vista previa de ${customerFileName}`;
                preview.src = URL.createObjectURL(file);
                state.previewUrls.push(preview.src);
                item.append(preview);
            }
            size.textContent = formatBytes(file.size);
            copy.append(name, size);
            remove.type = "button";
            remove.className = "mto-v2-file-remove";
            remove.dataset.removeFile = String(index);
            remove.setAttribute("aria-label", `Quitar ${customerFileName}`);
            remove.textContent = "×";
            item.append(copy, remove);
            return item;
        }));
    }

    function clearFilePreviews() {
        state.previewUrls.forEach(url => URL.revokeObjectURL(url));
        state.previewUrls = [];
    }

    function initializeSignaturePad() {
        const canvas = elements.signatureCanvas;
        if (!(canvas instanceof HTMLCanvasElement)) {
            return;
        }

        state.signature.context = canvas.getContext("2d", { alpha: true });
        resizeSignatureCanvas();

        canvas.addEventListener("pointerdown", beginSignatureStroke);
        canvas.addEventListener("pointermove", continueSignatureStroke);
        canvas.addEventListener("pointerup", endSignatureStroke);
        canvas.addEventListener("pointercancel", endSignatureStroke);
        canvas.addEventListener("lostpointercapture", endSignatureStroke);

        if (window.ResizeObserver) {
            state.signature.resizeObserver = new ResizeObserver(resizeSignatureCanvas);
            state.signature.resizeObserver.observe(canvas);
        } else {
            window.addEventListener("resize", resizeSignatureCanvas);
        }
    }

    function beginSignatureStroke(event) {
        if (state.submitting || (event.pointerType === "mouse" && event.button !== 0)) {
            return;
        }
        event.preventDefault();
        updateVisitTimes(true);

        const point = signaturePointFromEvent(event);
        const stroke = [point];
        state.signature.strokes.push(stroke);
        state.signature.activeStroke = stroke;
        state.signature.activePointerId = event.pointerId;
        elements.signatureCanvas.setPointerCapture?.(event.pointerId);
        drawSignatureDot(point);
        updateSignatureState();
    }

    function continueSignatureStroke(event) {
        if (state.signature.activePointerId !== event.pointerId || !state.signature.activeStroke) {
            return;
        }
        event.preventDefault();

        const point = signaturePointFromEvent(event);
        const previous = state.signature.activeStroke[state.signature.activeStroke.length - 1];
        state.signature.activeStroke.push(point);
        drawSignatureSegment(previous, point);
        updateSignatureState();
    }

    function endSignatureStroke(event) {
        if (state.signature.activePointerId !== event.pointerId) {
            return;
        }
        event.preventDefault?.();
        if (elements.signatureCanvas.hasPointerCapture?.(event.pointerId)) {
            elements.signatureCanvas.releasePointerCapture(event.pointerId);
        }
        state.signature.activeStroke = null;
        state.signature.activePointerId = null;
        updateSignatureState();
    }

    function signaturePointFromEvent(event) {
        const rect = elements.signatureCanvas.getBoundingClientRect();
        return {
            x: clamp((event.clientX - rect.left) / Math.max(1, rect.width), 0, 1),
            y: clamp((event.clientY - rect.top) / Math.max(1, rect.height), 0, 1),
            pressure: event.pointerType === "mouse" ? .5 : clamp(event.pressure || .5, .15, 1)
        };
    }

    function resizeSignatureCanvas() {
        const canvas = elements.signatureCanvas;
        const context = state.signature.context;
        if (!(canvas instanceof HTMLCanvasElement) || !context) {
            return;
        }

        const rect = canvas.getBoundingClientRect();
        if (!rect.width || !rect.height) {
            return;
        }
        const ratio = Math.min(3, Math.max(1, window.devicePixelRatio || 1));
        const width = Math.round(rect.width * ratio);
        const height = Math.round(rect.height * ratio);
        if (canvas.width === width && canvas.height === height) {
            return;
        }

        canvas.width = width;
        canvas.height = height;
        context.setTransform(ratio, 0, 0, ratio, 0, 0);
        context.lineCap = "round";
        context.lineJoin = "round";
        redrawSignature();
    }

    function redrawSignature() {
        const canvas = elements.signatureCanvas;
        const context = state.signature.context;
        if (!context || !(canvas instanceof HTMLCanvasElement)) {
            return;
        }

        const rect = canvas.getBoundingClientRect();
        context.clearRect(0, 0, rect.width, rect.height);
        state.signature.strokes.forEach(stroke => {
            if (stroke.length === 1) {
                drawSignatureDot(stroke[0]);
                return;
            }
            for (let index = 1; index < stroke.length; index += 1) {
                drawSignatureSegment(stroke[index - 1], stroke[index]);
            }
        });
    }

    function drawSignatureSegment(from, to) {
        const context = state.signature.context;
        const rect = elements.signatureCanvas.getBoundingClientRect();
        if (!context || !rect.width || !rect.height) {
            return;
        }

        context.beginPath();
        context.strokeStyle = "#15243a";
        context.lineWidth = 1.5 + ((from.pressure + to.pressure) / 2) * 2.7;
        context.moveTo(from.x * rect.width, from.y * rect.height);
        context.lineTo(to.x * rect.width, to.y * rect.height);
        context.stroke();
    }

    function drawSignatureDot(point) {
        const context = state.signature.context;
        const rect = elements.signatureCanvas.getBoundingClientRect();
        if (!context || !rect.width || !rect.height) {
            return;
        }

        context.beginPath();
        context.fillStyle = "#15243a";
        context.arc(point.x * rect.width, point.y * rect.height, 1.2 + point.pressure, 0, Math.PI * 2);
        context.fill();
    }

    function clearSignature() {
        if (state.submitting) {
            return;
        }
        state.signature.strokes = [];
        state.signature.activeStroke = null;
        state.signature.activePointerId = null;
        elements.signedAtUtc.value = "";
        elements.serviceEndedAtUtc.value = "";
        updateVisitTimes(state.currentStep === 3);
        if (elements.customerAccepted) {
            elements.customerAccepted.checked = false;
        }
        if (elements.finalReviewConfirmed) {
            elements.finalReviewConfirmed.checked = false;
        }
        state.maxUnlockedStep = Math.min(state.maxUnlockedStep, 3);
        updateProgressAvailability();
        redrawSignature();
        updateSignatureState();
    }

    function invalidateSignatureForChange() {
        if (!hasSignature()) {
            elements.serviceEndedAtUtc.value = "";
            updateVisitTimes(state.currentStep === 3);
            return false;
        }

        clearSignature();
        elements.signatureFeedback.textContent = "El reporte cambió; solicita la firma nuevamente.";
        elements.signatureFeedback.classList.remove("is-success");
        elements.signatureFeedback.classList.add("is-error");
        setStatus(elements.status, "info", "Cambió información incluida en el reporte. La firma anterior se invalidó para proteger la aceptación del cliente.");
        return true;
    }

    function updateVisitTimes(fixEnd) {
        const start = new Date(elements.serviceStartedAtLocal.value);
        elements.serviceStartedAtUtc.value = Number.isNaN(start.getTime()) ? "" : start.toISOString();
        if (fixEnd && !elements.serviceEndedAtUtc.value) elements.serviceEndedAtUtc.value = new Date().toISOString();
        elements.signatureStartedAt.textContent = formatLocalDateTime(elements.serviceStartedAtUtc.value) || "Pendiente";
        elements.signatureEndedAt.textContent = formatLocalDateTime(elements.serviceEndedAtUtc.value) || "Se registra al solicitar la firma";
    }

    function updateSignatureState() {
        const pointCount = getSignaturePointCount();
        elements.signaturePointCount.value = String(pointCount);
        if (pointCount > 1) {
            elements.signedAtUtc.value ||= new Date().toISOString();
            elements.signatureFeedback.textContent = "Firma capturada";
            elements.signatureFeedback.classList.remove("is-error");
            elements.signatureFeedback.classList.add("is-success");
        } else {
            elements.signatureFeedback.textContent = "Firma pendiente";
            elements.signatureFeedback.classList.remove("is-error", "is-success");
        }
    }

    function hasSignature() {
        return getSignaturePointCount() > 1;
    }

    function getSignaturePointCount() {
        return state.signature.strokes.reduce((total, stroke) => total + stroke.length, 0);
    }

    function createWhiteSignatureCanvas() {
        const source = elements.signatureCanvas;
        const output = document.createElement("canvas");
        output.width = source.width;
        output.height = source.height;
        const context = output.getContext("2d", { alpha: false });
        context.fillStyle = "#ffffff";
        context.fillRect(0, 0, output.width, output.height);
        context.drawImage(source, 0, 0);
        return output;
    }

    function signatureToJpegBlob() {
        return new Promise((resolve, reject) => {
            const output = createWhiteSignatureCanvas();
            output.toBlob(blob => {
                if (!blob) {
                    reject(new Error("No fue posible preparar la firma del cliente."));
                    return;
                }
                resolve(blob);
            }, "image/jpeg", .92);
        });
    }

    function renderReview() {
        if (!elements.review) {
            return;
        }

        replaceContents(elements.review,
            buildReviewSection("Servicio", [
                reviewItem("Cliente", valueOf("mtoV2ClientName")),
                reviewItem("Equipo", valueOf("mtoV2EquipmentSerial")),
                reviewItem("Referencia del equipo", state.catalog.selectedEquipment?.reference || ""),
                reviewItem("Orden o referencia", valueOf("mtoV2ServiceReference") || "Se asigna al enviar"),
                reviewItem("Hora de entrada", formatLocalDateTime(valueOf("mtoV2ServiceStartedAtUtc"))),
                reviewItem("Hora de salida", formatLocalDateTime(valueOf("mtoV2ServiceEndedAtUtc"))),
                reviewItem("Persona que atiende", valueOf("mtoV2OnsiteContactName")),
                reviewItem("Correo de contacto", valueOf("mtoV2OnsiteContactEmail"))
            ]),
            buildActivityReviewSection(),
            buildEvidenceReviewSection(),
            buildSignatureReviewSection());

    }

    function buildActivityReviewSection() {
        if (activityKind() === "movement") return buildReviewSection("Movimiento de equipo", [
            reviewItem("Origen", elements.originClientName.value),
            reviewItem("Cliente destino", valueOf("mtoV2ClientName")),
            reviewItem("Motivo del movimiento", valueOf("mtoV2MovementReason"), true),
            reviewItem("Observaciones del cliente", valueOf("mtoV2CustomerObservations"), true)
        ]);
        if (activityKind() === "toner") return buildReviewSection("Entrega de tóner", [
            reviewItem("Suministro", state.supplies.selected?.name || ""),
            reviewItem("Cantidad entregada", valueOf("mtoV2SupplyQuantity")),
            reviewItem("Existencias antes de la entrega", String(state.supplies.selected?.quantity ?? "")),
            reviewItem("Observaciones del cliente", valueOf("mtoV2CustomerObservations"), true)
        ]);
        return buildReviewSection("Trabajo técnico", [
                reviewItem("Tipo", selectedText("mtoV2MaintenanceType")),
                reviewItem("Resultado", selectedText("mtoV2ServiceResult")),
                reviewItem("Trabajo realizado", valueOf("mtoV2WorkPerformed"), true),
                reviewItem("Contadores", buildCountersSummary(), true),
                reviewItem("Recomendaciones", valueOf("mtoV2Recommendations"), true),
                reviewItem("Observaciones del cliente", valueOf("mtoV2CustomerObservations"), true)
            ]);
    }

    function buildReviewSection(title, items) {
        const section = document.createElement("section");
        const heading = document.createElement("h2");
        const grid = document.createElement("dl");
        section.className = "mto-v2-review-section";
        heading.textContent = title;
        grid.className = "mto-v2-review-section__grid";
        items.forEach(item => grid.append(item));
        section.append(heading, grid);
        return section;
    }

    function reviewItem(label, value, wide) {
        const wrapper = document.createElement("div");
        const term = document.createElement("dt");
        const detail = document.createElement("dd");
        wrapper.className = `mto-v2-review-item${wide ? " is-wide" : ""}`;
        term.textContent = label;
        detail.textContent = value || "No registrado";
        wrapper.append(term, detail);
        return wrapper;
    }

    function buildEvidenceReviewSection() {
        const section = document.createElement("section");
        const heading = document.createElement("h2");
        const list = document.createElement("ul");
        section.className = "mto-v2-review-section";
        heading.textContent = "Evidencias adjuntas";
        list.className = "mto-v2-review-files";

        if (!state.files.length) {
            const item = document.createElement("li");
            item.textContent = "Sin archivos adicionales";
            list.append(item);
        } else {
            state.files.forEach((file, index) => {
                const item = document.createElement("li");
                const customerFileName = customerAttachmentName(file, index);
                item.textContent = `${customerFileName} · ${formatBytes(file.size)}`;
                list.append(item);
            });
        }

        section.append(heading, list);
        return section;
    }

    function buildSignatureReviewSection() {
        const section = document.createElement("section");
        const heading = document.createElement("h2");
        const signer = document.createElement("p");
        const image = document.createElement("img");
        section.className = "mto-v2-review-section";
        heading.textContent = "Conformidad del cliente";
        signer.textContent = [
            valueOf("mtoV2SignerName") || "Sin nombre",
            valueOf("mtoV2SignerRole") || "Sin cargo"
        ].join(" · ");
        image.className = "mto-v2-review-signature";
        image.alt = "Firma capturada del cliente";
        image.src = hasSignature()
            ? createWhiteSignatureCanvas().toDataURL("image/jpeg", .9)
            : "data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs=";
        section.append(heading, signer, image);
        return section;
    }

    function prepareContractFields() {
        const localStart = elements.serviceStartedAtLocal?.value || "";
        elements.serviceDate.value = localStart.includes("T") ? localStart.split("T", 1)[0] : localStart;

        const reference = valueOf("mtoV2ServiceReference");
        const equipment = valueOf("mtoV2EquipmentSerial");
        const client = valueOf("mtoV2ClientName");
        elements.title.value = (reference
            ? `${activityLabel()} ${reference} · ${client}`
            : `${activityLabel()} ${equipment} · ${client}`).slice(0, 250);
        elements.answersJson.value = JSON.stringify(buildStructuredAnswers());
    }

    function buildStructuredAnswers() {
        const definitions = [
            ["activity_kind", "Tipo de atención", activityKind()],
            ["equipment_reference", "Referencia del equipo", state.catalog.selectedEquipment?.reference || ""],
            ["service_started_at", "Hora de entrada", formatLocalDateTime(valueOf("mtoV2ServiceStartedAtUtc"))],
            ["service_ended_at", "Hora de salida", formatLocalDateTime(valueOf("mtoV2ServiceEndedAtUtc"))],
            ["service_started_at_utc", "Entrada UTC", valueOf("mtoV2ServiceStartedAtUtc")],
            ["service_ended_at_utc", "Salida UTC", valueOf("mtoV2ServiceEndedAtUtc")],
            ["onsite_contact", "Persona que atendió", valueOf("mtoV2OnsiteContactName")],
            ["onsite_email", "Correo de contacto", valueOf("mtoV2OnsiteContactEmail")]
        ];
        if (activityKind() === "movement") definitions.push(
            ["movement_reason", "Motivo del movimiento", valueOf("mtoV2MovementReason")],
            ["origin_client_id", "Cliente de origen", elements.originClientId.value],
            ["origin_client_name", "Origen", elements.originClientName.value]
        );
        else if (activityKind() === "toner") definitions.push(
            ["supply_id", "Suministro", state.supplies.selected?.id || ""],
            ["supply_name", "Nombre del suministro", state.supplies.selected?.name || ""],
            ["supply_quantity", "Cantidad entregada", valueOf("mtoV2SupplyQuantity")],
            ["supply_stock_before", "Existencias antes de la entrega", String(state.supplies.selected?.quantity ?? "")]
        );
        else definitions.push(
            ["maintenance_type", "Tipo de mantenimiento", selectedText("mtoV2MaintenanceType")],
            ["service_result", "Resultado del servicio", valueOf("mtoV2ServiceResult")],
            ["counters", "Contadores", buildCountersSummary()],
            ["counter_record_id", "Registro de contador anterior", state.counters.recordId],
            ["counter_recorded_at", "Fecha del contador anterior", state.counters.dateValue],
            ["copies_before", "Impresiones anteriores", valueOf("mtoV2CopiesBefore")],
            ["copies_after", "Impresiones actuales", valueOf("mtoV2CopiesAfter")],
            ["scans_before", "Escaneos anteriores", valueOf("mtoV2ScansBefore")],
            ["scans_after", "Escaneos actuales", valueOf("mtoV2ScansAfter")],
            ["recommendations", "Recomendaciones", valueOf("mtoV2Recommendations")]
        );

        return definitions
            .map(([key, label, value], index) => ({
                key,
                label,
                value: String(value || "").trim(),
                sortOrder: index + 1
            }))
            .filter(answer => answer.value && answer.value !== "No aplica");
    }

    async function submitForm(event) {
        event.preventDefault();
        if (state.submitting || !validateAllSteps()) {
            return;
        }

        prepareSubmissionIdentity();
        state.submitting = true;
        elements.signedAtUtc.value ||= new Date().toISOString();
        elements.submittedAtUtc.value ||= new Date().toISOString();
        prepareContractFields();
        setSubmitState("pending");
        setStatus(elements.submitStatus, "info", "Guardando el registro firmado y preparando el correo…");

        try {
            if (!state.locationAttempted) {
                state.locationAttempted = true;
                await captureGeolocation();
            }
            const signatureBlob = await signatureToJpegBlob();
            const payload = new FormData(form);
            payload.delete("Attachments");
            state.files.forEach(file => payload.append("Attachments", file, file.name));
            payload.append("Signature", signatureBlob, "firma-cliente.jpg");
            payload.set("SignaturePointCount", String(getSignaturePointCount()));

            const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
            const response = await fetch(root.dataset.finalizeUrl || form.action || "/CopiersMtoV2/Finalize", {
                method: "POST",
                body: payload,
                credentials: "same-origin",
                headers: {
                    Accept: "application/json",
                    "Idempotency-Key": elements.submissionKey.value,
                    RequestVerificationToken: token
                }
            });
            const result = await readResponse(response);
            if (!response.ok) {
                throw new Error(result?.message || result?.Message || result?.detail || result?.Detail || `No fue posible finalizar el reporte (${response.status}).`);
            }

            const workflowState = result?.state ?? result?.State;
            const resultMessage = result?.message || result?.Message || "";
            if (matchesState(workflowState, 1, "Finalizing")) {
                state.submitting = false;
                setSubmitState("idle");
                setStatus(
                    elements.submitStatus,
                    "info",
                    `${resultMessage || "La finalización sigue en proceso."} La clave se conservó; espera unos segundos y vuelve a intentar para confirmar el resultado.`);
                updateProgressAvailability();
                return;
            }
            if (!matchesState(workflowState, 2, "ReadyToSend")) {
                state.submitting = false;
                setSubmitState("idle");
                setStatus(
                    elements.submitStatus,
                    "error",
                    resultMessage || "El servidor respondió, pero no confirmó que el reporte quedara listo. Vuelve a intentar sin cambiar la clave.");
                updateProgressAvailability();
                return;
            }

            removeStoredSubmissionId();
            if (elements.previousAttempt) elements.previousAttempt.hidden = true;
            elements.recordId.value = textProperty(result, "recordId", "RecordId");
            const serviceReference = textProperty(result, "serviceReference", "ServiceReference");
            if (serviceReference) {
                elements.serviceReference.value = serviceReference;
                renderReview();
            }
            const emailState = result?.emailState ?? result?.EmailState;
            const emailSent = matchesState(emailState, 3, "Sent");
            const emailProcessing = matchesState(emailState, 2, "Processing");
            const emailFailed = matchesState(emailState, 4, "Failed");
            setSubmitState(emailSent ? "sent" : "created");
            setStatus(
                elements.submitStatus,
                emailFailed ? "error" : emailSent ? "success" : "info",
                emailFailed
                    ? `${resultMessage || "El registro y el reporte quedaron creados."} El correo no fue enviado y requiere revisión interna; no se reintentará automáticamente.`
                    : emailSent
                        ? resultMessage || "Reporte firmado recibido. El registro quedó creado y el correo fue enviado al cliente."
                        : emailProcessing
                            ? resultMessage || "El registro y el reporte quedaron creados. El correo está siendo procesado."
                            : resultMessage || "El registro y el reporte quedaron creados. El correo quedó pendiente de procesamiento." );
            root.classList.add("is-submitted");
            elements.createAnother.hidden = !emailSent;
            elements.checkEmailStatus.hidden = emailSent || !elements.recordId.value;
            if (!emailSent && !emailFailed) startEmailStatusPolling();
        } catch (error) {
            state.submitting = false;
            setSubmitState("idle");
            setStatus(
                elements.submitStatus,
                "error",
                error instanceof Error ? error.message : "No fue posible finalizar el reporte. Puedes volver a intentar sin duplicarlo.");
            updateProgressAvailability();
        }
    }

    function setSubmitState(mode) {
        const pending = mode === "pending";
        const completed = mode === "sent" || mode === "created";
        root.setAttribute("aria-busy", pending ? "true" : "false");
        elements.submitButton.disabled = pending || completed;
        elements.submitButton.classList.toggle("is-pending", pending);
        elements.submitButton.classList.toggle("is-success", completed);
        elements.submitLabel.textContent = pending
            ? "Enviando…"
            : mode === "sent"
                ? "✓ Enviado"
                : mode === "created"
                    ? "✓ Registro creado"
                    : "Crear registro y enviar";
        elements.nextButtons.concat(elements.previousButtons).forEach(button => {
            button.disabled = pending || completed;
        });
        elements.editClientEmail.disabled = pending || completed || !state.catalog.selectedClient;
        elements.panels.forEach(panel => { panel.inert = pending || (completed && Number(panel.dataset.stepPanel) !== 4); });
        elements.finalReviewConfirmed.disabled = pending || completed;
        elements.evidenceInput.disabled = pending || completed;
        elements.cameraInput.disabled = pending || completed;
        elements.clearSignature.disabled = pending || completed;
        updateProgressAvailability();
    }

    function stopEmailStatusPolling() {
        window.clearTimeout(state.emailStatus.timer);
        state.emailStatus.timer = null;
        state.emailStatus.requestId += 1;
        state.emailStatus.loading = false;
    }

    function startEmailStatusPolling() {
        if (!elements.recordId.value || state.emailStatus.loading || !elements.createAnother.hidden) return;
        stopEmailStatusPolling();
        state.emailStatus.attempts = 0;
        state.emailStatus.startedAt = Date.now();
        void checkEmailStatus();
    }

    async function checkEmailStatus() {
        const recordId = elements.recordId.value;
        if (!recordId || state.emailStatus.loading || !elements.createAnother.hidden) return;
        if (state.emailStatus.attempts >= 8 || Date.now() - state.emailStatus.startedAt >= 90000) return;
        const kind = activityKind();
        const requestId = ++state.emailStatus.requestId;
        state.emailStatus.loading = true;
        state.emailStatus.attempts += 1;
        elements.checkEmailStatus.disabled = true;
        let keepPolling = false;
        try {
            const url = `${root.dataset.statusUrl || "/CopiersMtoV2/Status"}?recordId=${encodeURIComponent(recordId)}&activityKind=${encodeURIComponent(kind)}`;
            const result = await fetchActivityJson(url, "el estado del correo");
            if (requestId !== state.emailStatus.requestId || recordId !== elements.recordId.value || kind !== activityKind()) return;
            const workflowState = result?.state ?? result?.State;
            const emailState = result?.emailState ?? result?.EmailState;
            const ready = matchesState(workflowState, 2, "ReadyToSend");
            const sent = ready && matchesState(emailState, 3, "Sent");
            const failed = matchesState(emailState, 4, "Failed");
            elements.createAnother.hidden = !sent;
            elements.checkEmailStatus.hidden = sent;
            if (sent) {
                const reference = textProperty(result, "serviceReference", "ServiceReference");
                if (reference) { elements.serviceReference.value = reference; renderReview(); }
                setSubmitState("sent");
                setStatus(elements.submitStatus, "success", "El registro firmado quedó creado y el correo fue enviado al cliente.");
            } else if (failed) {
                setStatus(elements.submitStatus, "error", "El registro quedó creado, pero el correo no fue enviado y requiere revisión interna. Esta consulta no reenvía correos.");
            } else {
                keepPolling = true;
                setStatus(elements.submitStatus, "info", "El registro quedó creado. Aún no se ha confirmado el envío del correo.");
            }
        } catch (error) {
            if (requestId !== state.emailStatus.requestId || recordId !== elements.recordId.value || kind !== activityKind()) return;
            elements.createAnother.hidden = true;
            elements.checkEmailStatus.hidden = false;
            setStatus(elements.submitStatus, "info", "El registro quedó creado. No fue posible consultar el envío del correo; puedes verificarlo de nuevo sin duplicar el registro.");
        } finally {
            if (requestId === state.emailStatus.requestId) {
                state.emailStatus.loading = false;
                elements.checkEmailStatus.disabled = false;
                if (keepPolling && state.emailStatus.attempts < 8 && Date.now() - state.emailStatus.startedAt < 90000) {
                    state.emailStatus.timer = window.setTimeout(() => { void checkEmailStatus(); }, 5000);
                }
            }
        }
    }

    function updateProgressAvailability() {
        elements.stepTargets.forEach(button => {
            const target = Number(button.dataset.stepTarget || 0);
            button.disabled = state.submitting || target > state.maxUnlockedStep;
        });
    }

    async function readResponse(response) {
        const contentType = response.headers.get("content-type") || "";
        if (contentType.includes("json")) {
            return await response.json();
        }
        const text = (await response.text()).trim();
        return {
            message: contentType.includes("text/plain") && text.length <= 500 ? text : ""
        };
    }

    function matchesState(value, numericValue, textValue) {
        if (typeof value === "number") {
            return value === numericValue;
        }
        const normalized = String(value ?? "")
            .replace(/[^a-z0-9]/gi, "")
            .toLowerCase();
        return normalized === String(numericValue) || normalized === textValue.toLowerCase();
    }

    function buildCountersSummary() {
        const parts = [];
        if (elements.copiesBefore?.value || elements.copiesAfter?.value) {
            parts.push(`Impresiones: ${elements.copiesBefore.value || "Sin registro"} → ${elements.copiesAfter.value || "-"}`);
        }
        if (elements.scansBefore?.value || elements.scansAfter?.value) {
            parts.push(`Escaneos: ${elements.scansBefore.value || "Sin registro"} → ${elements.scansAfter.value || "-"}`);
        }
        if (parts.length && state.counters.dateDisplay) parts.push(`Lectura anterior: ${state.counters.dateDisplay}`);
        return parts.join(" · ") || "No aplica";
    }

    function getPanel(step) {
        return elements.panels.find(panel => Number(panel.dataset.stepPanel || 0) === step) || null;
    }

    function valueOf(id) {
        const element = document.getElementById(id);
        return (element?.value || "").trim();
    }

    function selectedText(id) {
        const select = document.getElementById(id);
        if (!(select instanceof HTMLSelectElement) || !select.value) {
            return "";
        }
        return select.options[select.selectedIndex]?.text || "";
    }

    function formatLocalDateTime(value) {
        if (!value) {
            return "";
        }
        const parsed = new Date(value);
        if (Number.isNaN(parsed.getTime())) {
            return value;
        }
        return new Intl.DateTimeFormat("es-CO", {
            timeZone: "America/Bogota",
            dateStyle: "medium",
            timeStyle: "short"
        }).format(parsed);
    }

    function toLocalDateTimeValue(date) {
        const offset = date.getTimezoneOffset();
        return new Date(date.getTime() - offset * 60000).toISOString().slice(0, 16);
    }

    function getFileExtension(fileName) {
        const index = String(fileName || "").lastIndexOf(".");
        return index >= 0 ? fileName.slice(index + 1).toLowerCase() : "";
    }

    function customerAttachmentName(file, index) {
        const extension = getFileExtension(file?.name) === "png" ? ".png" : ".jpg";
        return `adjunto-${String(index + 1).padStart(3, "0")}${extension}`;
    }

    function fileKey(file) {
        return `${file.name}:${file.size}:${file.lastModified}`;
    }

    function textProperty(source, ...names) {
        if (!source || typeof source !== "object") {
            return "";
        }
        for (const name of names) {
            const value = source[name];
            if (value !== null && value !== undefined) {
                return String(value).trim();
            }
        }
        return "";
    }

    function catalogKey(value) {
        return String(value || "")
            .trim()
            .replace(/\s+/g, " ")
            .toLocaleLowerCase("es-CO");
    }

    function sameCatalogId(left, right) {
        return Boolean(left && right) && catalogKey(left) === catalogKey(right);
    }

    function formatBytes(bytes) {
        const value = Number(bytes || 0);
        if (value < 1024 * 1024) {
            return `${Math.max(0, value / 1024).toFixed(value ? 1 : 0)} KB`;
        }
        return `${(value / 1024 / 1024).toFixed(1)} MB`;
    }

    function setStatus(element, tone, message) {
        if (!element) {
            return;
        }
        element.classList.remove("is-info", "is-success", "is-error");
        if (tone) {
            element.classList.add(`is-${tone}`);
        }
        element.textContent = message || "";
    }

    function clearStatus(element) {
        setStatus(element, "", "");
    }

    function createSubmissionId() {
        if (window.crypto?.randomUUID) {
            return window.crypto.randomUUID();
        }
        const random = new Uint8Array(16);
        window.crypto?.getRandomValues?.(random);
        if (!random.some(value => value !== 0)) {
            for (let index = 0; index < random.length; index += 1) {
                random[index] = Math.floor(Math.random() * 256);
            }
        }
        random[6] = (random[6] & 0x0f) | 0x40;
        random[8] = (random[8] & 0x3f) | 0x80;
        const hex = Array.from(random, value => value.toString(16).padStart(2, "0")).join("");
        return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
    }

    function readStoredSubmissionId() {
        try {
            return window.sessionStorage.getItem(submissionStorageKey) || "";
        } catch {
            return "";
        }
    }

    function storeSubmissionId(value) {
        try {
            window.sessionStorage.setItem(submissionStorageKey, value);
        } catch {
            // El campo oculto sigue conservando la clave durante esta carga.
        }
    }

    function removeStoredSubmissionId() {
        try {
            window.sessionStorage.removeItem(submissionStorageKey);
        } catch {
            // Sin acción: el envío ya fue aceptado por el servidor.
        }
    }

    function clamp(value, minimum, maximum) {
        return Math.min(maximum, Math.max(minimum, value));
    }
})();

