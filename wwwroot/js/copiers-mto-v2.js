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
    const maxLocationAccuracyMeters = Number(root.dataset.maxLocationAccuracyMeters || 250);
    const maxLocationAgeMs = Number(root.dataset.maxLocationAgeMs || 15 * 60 * 1000);
    const allowedExtensions = new Set(["jpg", "jpeg", "png"]);
    const submissionStorageKey = "copiers-mto-v2:submission-id";
    const signatureBoundFieldIds = new Set([
        "mtoV2ClientName",
        "mtoV2EquipmentSerial",
        "mtoV2ServiceReference",
        "mtoV2ServiceStartedAtLocal",
        "mtoV2OnsiteContactName",
        "mtoV2OnsiteContactEmail",
        "mtoV2MaintenanceType",
        "mtoV2ServiceResult",
        "mtoV2ReportedIssue",
        "mtoV2TechnicalDiagnosis",
        "mtoV2WorkPerformed",
        "mtoV2PartsUsed",
        "mtoV2CopiesBefore",
        "mtoV2CopiesAfter",
        "mtoV2ScansBefore",
        "mtoV2ScansAfter",
        "mtoV2Recommendations",
        "mtoV2CustomerObservations",
        "mtoV2SignerName",
        "mtoV2SignerRole",
        "mtoV2SignerDocument"
    ]);

    const elements = {
        panels: Array.from(root.querySelectorAll("[data-step-panel]")),
        indicators: Array.from(root.querySelectorAll("[data-step-indicator]")),
        stepTargets: Array.from(root.querySelectorAll("[data-step-target]")),
        nextButtons: Array.from(root.querySelectorAll("[data-next-step]")),
        previousButtons: Array.from(root.querySelectorAll("[data-previous-step]")),
        status: document.getElementById("mtoV2Status"),
        submitStatus: document.getElementById("mtoV2SubmitStatus"),
        technicianName: document.getElementById("mtoV2TechnicianName"),
        retryBootstrap: document.getElementById("mtoV2RetryBootstrap"),
        submissionKey: document.getElementById("mtoV2SubmissionKey"),
        recordId: document.getElementById("mtoV2RecordId"),
        expectedVersion: document.getElementById("mtoV2ExpectedVersion"),
        answersJson: document.getElementById("mtoV2AnswersJson"),
        title: document.getElementById("mtoV2Title"),
        serviceDate: document.getElementById("mtoV2ServiceDate"),
        startedAtUtc: document.getElementById("mtoV2StartedAtUtc"),
        signedAtUtc: document.getElementById("mtoV2SignedAtUtc"),
        submittedAtUtc: document.getElementById("mtoV2SubmittedAtUtc"),
        serviceStartedAtLocal: document.getElementById("mtoV2ServiceStartedAtLocal"),
        latitude: document.getElementById("mtoV2Latitude"),
        longitude: document.getElementById("mtoV2Longitude"),
        accuracy: document.getElementById("mtoV2Accuracy"),
        geoCapturedAtUtc: document.getElementById("mtoV2GeoCapturedAtUtc"),
        geoStatus: document.getElementById("mtoV2GeoStatus"),
        captureLocation: document.getElementById("mtoV2CaptureLocation"),
        geoFeedback: document.getElementById("mtoV2GeoFeedback"),
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
        evidenceInput: document.getElementById("mtoV2EvidenceInput"),
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
        scansAfter: document.getElementById("mtoV2ScansAfter")
    };

    const state = {
        currentStep: 1,
        maxUnlockedStep: 1,
        files: [],
        submitting: false,
        locating: false,
        catalog: {
            loaded: false,
            loading: false,
            schemaReady: false,
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
        initializeSignaturePad();
        wireEvents();
        renderFiles();
        showStep(1, { scroll: false });
        void loadBootstrap();
    }

    function initializeSubmission() {
        let submissionId = readStoredSubmissionId();
        if (!submissionId) {
            submissionId = createSubmissionId();
            storeSubmissionId(submissionId);
        }

        elements.submissionKey.value = submissionId;
        elements.startedAtUtc.value = new Date().toISOString();
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

        try {
            const response = await fetch(root.dataset.bootstrapUrl || "/CopiersMtoV2/Bootstrap", {
                method: "GET",
                credentials: "same-origin",
                headers: { Accept: "application/json" }
            });
            const result = await readResponse(response);
            if (!response.ok) {
                throw new Error(result?.message || result?.Message || result?.detail || result?.Detail || `No fue posible cargar el catálogo (${response.status}).`);
            }

            const clients = Array.isArray(result?.clients) ? result.clients : Array.isArray(result?.Clients) ? result.Clients : [];
            const equipment = Array.isArray(result?.equipment) ? result.equipment : Array.isArray(result?.Equipment) ? result.Equipment : [];
            const maintenanceTypes = Array.isArray(result?.maintenanceTypes) ? result.maintenanceTypes : Array.isArray(result?.MaintenanceTypes) ? result.MaintenanceTypes : [];
            state.catalog.schemaReady = (result?.schemaReady ?? result?.SchemaReady) === true;
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
        if (!serial || !clientId) {
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
        elements.maintenanceType?.replaceChildren(placeholder, ...options);
    }

    function renderClientOptions() {
        const options = state.catalog.clients.map(client => {
            const option = document.createElement("option");
            option.value = client.name;
            return option;
        });
        elements.clientOptions?.replaceChildren(...options);
        setCatalogFeedback(
            elements.catalogFeedback,
            `${state.catalog.clients.length} clientes disponibles. Selecciona una coincidencia de la lista.`,
            "success");
    }

    function syncClientSelection() {
        const previousClient = state.catalog.selectedClient;
        const value = catalogKey(elements.clientName?.value);
        const selected = state.catalog.clients.find(client => catalogKey(client.name) === value) || null;

        state.catalog.selectedClient = selected;
        elements.clientId.value = selected?.id || "";
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

        if (state.catalog.selectedEquipment && !sameCatalogId(state.catalog.selectedEquipment.clientId, selected?.id)) {
            const previousEquipment = state.catalog.selectedEquipment;
            if (catalogKey(elements.equipmentSerial?.value) === catalogKey(previousEquipment.serial)) {
                elements.equipmentSerial.value = "";
            }
            if (previousEquipment.reference && elements.serviceReference?.value === previousEquipment.reference) {
                elements.serviceReference.value = "";
            }
            state.catalog.selectedEquipment = null;
            elements.equipmentId.value = "";
        }

        renderEquipmentOptions();
        syncEquipmentSelection();
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
                ? "Correo autorizado en Copiers para el envío del reporte."
                : "El cliente no tiene correo en Copiers. Debe actualizarse antes de enviar.",
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
            ? state.catalog.equipment.filter(item => sameCatalogId(item.clientId, clientId))
            : [];
        const options = filtered.map(item => {
            const option = document.createElement("option");
            option.value = item.serial;
            option.label = [item.reference, item.clientName].filter(Boolean).join(" · ");
            return option;
        });
        elements.equipmentOptions?.replaceChildren(...options);

        if (!state.catalog.loaded) {
            return;
        }
        if (!clientId) {
            setCatalogFeedback(elements.equipmentFeedback, "Selecciona primero un cliente.", "");
        } else if (filtered.length) {
            setCatalogFeedback(
                elements.equipmentFeedback,
                `${filtered.length} equipos del cliente. También puedes registrar un serial externo.`,
                "success");
        } else {
            setCatalogFeedback(elements.equipmentFeedback, "Sin equipos asociados; puedes registrar un serial externo.", "");
        }
    }

    function syncEquipmentSelection() {
        const previousEquipment = state.catalog.selectedEquipment;
        const clientId = state.catalog.selectedClient?.id || "";
        const value = catalogKey(elements.equipmentSerial?.value);
        const selected = state.catalog.equipment.find(item =>
            sameCatalogId(item.clientId, clientId) && catalogKey(item.serial) === value) || null;

        state.catalog.selectedEquipment = selected;
        elements.equipmentId.value = selected?.id || "";
        elements.equipmentSerial?.setCustomValidity("");

        if (selected) {
            replaceCatalogPrefill(elements.serviceReference, previousEquipment?.reference, selected.reference);
            setCatalogFeedback(elements.equipmentFeedback, `Equipo seleccionado: ${selected.serial}.`, "success");
        } else if (state.catalog.loaded && clientId && value) {
            setCatalogFeedback(elements.equipmentFeedback, "Serial externo: se enviará sin identificador de equipo.", "");
        }
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

        elements.captureLocation?.addEventListener("click", captureGeolocation);
        elements.retryBootstrap?.addEventListener("click", () => {
            state.catalog.loaded = false;
            void loadBootstrap();
        });
        elements.clientName?.addEventListener("input", syncClientSelection);
        elements.equipmentSerial?.addEventListener("input", syncEquipmentSelection);
        elements.evidenceInput?.addEventListener("change", handleEvidenceSelection);
        elements.fileList?.addEventListener("click", handleFileListClick);
        elements.clearSignature?.addEventListener("click", clearSignature);
        form.addEventListener("submit", submitForm);

        form.querySelectorAll("input, select, textarea").forEach(control => {
            const eventName = control instanceof HTMLSelectElement || control.type === "checkbox" ? "change" : "input";
            control.addEventListener(eventName, () => {
                control.classList.remove("is-invalid");
                control.setCustomValidity("");
                clearStatus(elements.status);
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
        }

        clearStatus(elements.status);
        if (options?.scroll !== false) {
            const panel = getPanel(normalizedStep);
            window.setTimeout(() => panel?.scrollIntoView({ behavior: "smooth", block: "start" }), 30);
        }
    }

    function validateStep(step) {
        clearCounterValidity();
        if (step === 2 && !validateCounters()) {
            return false;
        }

        const panel = getPanel(step);
        if (!panel) {
            return false;
        }

        if (step === 1) {
            prepareCatalogValidity();
        }

        const controls = Array.from(panel.querySelectorAll("input, select, textarea"))
            .filter(control => !control.disabled && control.type !== "hidden");
        const firstInvalid = controls.find(control => !control.checkValidity());
        if (firstInvalid) {
            firstInvalid.classList.add("is-invalid");
            firstInvalid.reportValidity();
            firstInvalid.focus({ preventScroll: false });
            setStatus(elements.status, "error", "Revisa los campos obligatorios antes de continuar.");
            return false;
        }

        if (step === 1 && elements.geoStatus.value !== "captured") {
            elements.geoFeedback.classList.add("is-error");
            setStatus(elements.status, "error", "Captura la ubicación de la visita antes de continuar.");
            elements.captureLocation?.focus();
            return false;
        }

        if (step === 1 && !isLocationFresh()) {
            setGeoFailure("La ubicación tiene más de 15 minutos. Captúrala nuevamente antes de enviar.", "stale");
            elements.captureLocation?.focus();
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
            elements.clientName?.setCustomValidity("El cliente no tiene correo registrado en Copiers. Debe actualizarse antes de enviar.");
        } else {
            elements.clientName?.setCustomValidity("");
        }

        const serial = catalogKey(elements.equipmentSerial?.value);
        const catalogMatches = state.catalog.equipment.filter(item => catalogKey(item.serial) === serial);
        const belongsToSelectedClient = catalogMatches.some(item => sameCatalogId(item.clientId, elements.clientId?.value));
        if (catalogMatches.length && !belongsToSelectedClient) {
            elements.equipmentSerial?.setCustomValidity("Ese serial está asociado a otro cliente.");
        } else {
            elements.equipmentSerial?.setCustomValidity("");
        }
    }

    function isLocationFresh() {
        const capturedAt = Date.parse(elements.geoCapturedAtUtc?.value || "");
        return Number.isFinite(capturedAt)
            && Date.now() - capturedAt >= 0
            && Date.now() - capturedAt <= maxLocationAgeMs;
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
        const pairs = [
            [elements.copiesBefore, elements.copiesAfter, "El contador de copias final no puede ser menor al inicial."],
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
        if (state.locating) {
            return;
        }
        if (!window.isSecureContext || !navigator.geolocation) {
            setGeoFailure("Este navegador no permite capturar la ubicación de forma segura.", "unsupported");
            return;
        }

        state.locating = true;
        elements.captureLocation.disabled = true;
        elements.geoFeedback.textContent = "Solicitando permiso y ubicación…";
        elements.geoFeedback.classList.remove("is-error", "is-success");

        navigator.geolocation.getCurrentPosition(
            position => {
                const latitude = Number(position.coords.latitude);
                const longitude = Number(position.coords.longitude);
                const accuracy = Number(position.coords.accuracy);
                if (!Number.isFinite(latitude) || !Number.isFinite(longitude) || !Number.isFinite(accuracy) || accuracy < 0) {
                    setGeoFailure("El dispositivo no devolvió coordenadas válidas.", "invalid");
                    return;
                }
                if (Number.isFinite(accuracy) && accuracy > maxLocationAccuracyMeters) {
                    setGeoFailure(
                        `La precisión obtenida fue de ${Math.round(accuracy)} m y debe ser de ${maxLocationAccuracyMeters} m o menos. Vuelve a intentar.`,
                        "imprecise");
                    return;
                }

                elements.latitude.value = latitude.toFixed(7);
                elements.longitude.value = longitude.toFixed(7);
                elements.accuracy.value = Number.isFinite(accuracy) ? accuracy.toFixed(1) : "";
                elements.geoCapturedAtUtc.value = new Date().toISOString();
                elements.geoStatus.value = "captured";
                elements.geoFeedback.textContent = Number.isFinite(accuracy)
                    ? `Ubicación capturada internamente · precisión aproximada ${Math.round(accuracy)} m`
                    : "Ubicación capturada internamente";
                elements.geoFeedback.classList.remove("is-error");
                elements.geoFeedback.classList.add("is-success");
                clearStatus(elements.status);
                finishGeolocationRequest();
            },
            error => {
                const messages = {
                    1: "El permiso de ubicación fue rechazado. Actívalo en el navegador y vuelve a intentar.",
                    2: "El dispositivo no pudo determinar la ubicación. Revisa GPS o conectividad.",
                    3: "La captura de ubicación agotó el tiempo. Vuelve a intentar."
                };
                const statuses = { 1: "denied", 2: "unavailable", 3: "timeout" };
                setGeoFailure(messages[error.code] || "No fue posible capturar la ubicación.", statuses[error.code] || "error");
            },
            {
                enableHighAccuracy: true,
                timeout: 20000,
                maximumAge: 0
            });
    }

    function setGeoFailure(message, status) {
        elements.latitude.value = "";
        elements.longitude.value = "";
        elements.accuracy.value = "";
        elements.geoCapturedAtUtc.value = "";
        elements.geoStatus.value = status;
        elements.geoFeedback.textContent = message;
        elements.geoFeedback.classList.remove("is-success");
        elements.geoFeedback.classList.add("is-error");
        setStatus(elements.status, "error", message);
        finishGeolocationRequest();
    }

    function finishGeolocationRequest() {
        state.locating = false;
        elements.captureLocation.disabled = false;
    }

    function handleEvidenceSelection() {
        const selected = Array.from(elements.evidenceInput?.files || []);
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

        if (elements.evidenceInput) {
            elements.evidenceInput.value = "";
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
        const totalBytes = state.files.reduce((sum, file) => sum + file.size, 0);
        elements.fileSummary.textContent = `${state.files.length} de ${maxFiles} archivos · ${formatBytes(totalBytes)} de ${formatBytes(maxTotalBytes)}`;
        elements.fileList.replaceChildren(...state.files.map((file, index) => {
            const item = document.createElement("li");
            const copy = document.createElement("div");
            const name = document.createElement("strong");
            const size = document.createElement("small");
            const remove = document.createElement("button");

            const customerFileName = customerAttachmentName(file, index);
            name.textContent = customerFileName;
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
            return false;
        }

        clearSignature();
        elements.signatureFeedback.textContent = "El reporte cambió; solicita la firma nuevamente.";
        elements.signatureFeedback.classList.remove("is-success");
        elements.signatureFeedback.classList.add("is-error");
        setStatus(elements.status, "info", "Cambió información incluida en el reporte. La firma anterior se invalidó para proteger la aceptación del cliente.");
        return true;
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

        elements.review.replaceChildren(
            buildReviewSection("Servicio", [
                reviewItem("Cliente", valueOf("mtoV2ClientName")),
                reviewItem("Equipo", valueOf("mtoV2EquipmentSerial")),
                reviewItem("Referencia del equipo", state.catalog.selectedEquipment?.reference || ""),
                reviewItem("Orden o referencia", valueOf("mtoV2ServiceReference")),
                reviewItem("Inicio de visita", formatLocalDateTime(valueOf("mtoV2ServiceStartedAtLocal"))),
                reviewItem("Persona que atiende", valueOf("mtoV2OnsiteContactName")),
                reviewItem("Correo de contacto", valueOf("mtoV2OnsiteContactEmail"))
            ]),
            buildReviewSection("Trabajo técnico", [
                reviewItem("Tipo", selectedText("mtoV2MaintenanceType")),
                reviewItem("Resultado", selectedText("mtoV2ServiceResult")),
                reviewItem("Solicitud o falla", valueOf("mtoV2ReportedIssue"), true),
                reviewItem("Diagnóstico", valueOf("mtoV2TechnicalDiagnosis"), true),
                reviewItem("Trabajo realizado", valueOf("mtoV2WorkPerformed"), true),
                reviewItem("Repuestos o materiales", valueOf("mtoV2PartsUsed"), true),
                reviewItem("Contadores", buildCountersSummary(), true),
                reviewItem("Recomendaciones", valueOf("mtoV2Recommendations"), true),
                reviewItem("Observaciones del cliente", valueOf("mtoV2CustomerObservations"), true)
            ]),
            buildEvidenceReviewSection(),
            buildSignatureReviewSection());

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
        const signerDocument = valueOf("mtoV2SignerDocument");
        signer.textContent = [
            valueOf("mtoV2SignerName") || "Sin nombre",
            valueOf("mtoV2SignerRole") || "Sin cargo",
            signerDocument ? `Identificación ${signerDocument}` : "Sin identificación"
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
            ? `MTO ${reference} · ${client}`
            : `Mantenimiento ${equipment} · ${client}`).slice(0, 250);
        elements.answersJson.value = JSON.stringify(buildStructuredAnswers());
    }

    function buildStructuredAnswers() {
        const definitions = [
            ["service_reference", "Orden o referencia", valueOf("mtoV2ServiceReference")],
            ["equipment_reference", "Referencia del equipo", state.catalog.selectedEquipment?.reference || ""],
            ["service_started_at", "Inicio de visita", formatLocalDateTime(valueOf("mtoV2ServiceStartedAtLocal"))],
            ["onsite_contact", "Persona que atendió", valueOf("mtoV2OnsiteContactName")],
            ["onsite_email", "Correo de contacto", valueOf("mtoV2OnsiteContactEmail")],
            ["maintenance_type", "Tipo de mantenimiento", selectedText("mtoV2MaintenanceType")],
            ["service_result", "Resultado del servicio", valueOf("mtoV2ServiceResult")],
            ["reported_issue", "Solicitud o falla reportada", valueOf("mtoV2ReportedIssue")],
            ["technical_diagnosis", "Diagnóstico técnico", valueOf("mtoV2TechnicalDiagnosis")],
            ["parts_used", "Repuestos o materiales", valueOf("mtoV2PartsUsed")],
            ["counters", "Contadores", buildCountersSummary()],
            ["recommendations", "Recomendaciones", valueOf("mtoV2Recommendations")],
            ["signer_document", "Identificación de quien firma", valueOf("mtoV2SignerDocument")]
        ];

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

        state.submitting = true;
        elements.signedAtUtc.value ||= new Date().toISOString();
        elements.submittedAtUtc.value = new Date().toISOString();
        prepareContractFields();
        setSubmitState("pending");
        setStatus(elements.submitStatus, "info", "Guardando reporte, creando ticket y preparando el correo…");

        try {
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
            const emailState = result?.emailState ?? result?.EmailState;
            const emailSent = matchesState(emailState, 3, "Sent");
            const emailProcessing = matchesState(emailState, 2, "Processing");
            const emailFailed = matchesState(emailState, 4, "Failed");
            setSubmitState(emailSent ? "sent" : "created");
            setStatus(
                elements.submitStatus,
                emailFailed ? "error" : emailSent ? "success" : "info",
                emailFailed
                    ? `${resultMessage || "El ticket y el reporte quedaron creados."} El correo no fue enviado y requiere revisión interna; no se reintentará automáticamente.`
                    : emailSent
                        ? resultMessage || "Reporte firmado recibido. El ticket quedó creado y el correo fue enviado al cliente."
                        : emailProcessing
                            ? resultMessage || "El ticket y el reporte quedaron creados. El correo está siendo procesado."
                            : resultMessage || "El ticket y el reporte quedaron creados. El correo quedó pendiente de procesamiento." );
            root.classList.add("is-submitted");
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
                    ? "✓ Ticket creado"
                    : "Crear ticket y enviar";
        elements.nextButtons.concat(elements.previousButtons).forEach(button => {
            button.disabled = pending || completed;
        });
        elements.captureLocation.disabled = pending || completed || state.locating;
        elements.evidenceInput.disabled = pending || completed;
        elements.clearSignature.disabled = pending || completed;
        updateProgressAvailability();
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
            parts.push(`Copias: ${elements.copiesBefore.value || "-"} → ${elements.copiesAfter.value || "-"}`);
        }
        if (elements.scansBefore?.value || elements.scansAfter?.value) {
            parts.push(`Escaneos: ${elements.scansBefore.value || "-"} → ${elements.scansAfter.value || "-"}`);
        }
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

