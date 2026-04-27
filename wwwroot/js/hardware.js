(function () {
    const roots = Array.from(document.querySelectorAll("[data-hw-root]"));
    roots.forEach(initHardwareWorkspace);

    function initHardwareWorkspace(root) {
        const config = {
            mode: root.dataset.hwMode || "dashboard",
            previewUrl: root.dataset.previewUrl || "",
            provisionUrl: root.dataset.provisionUrl || "",
            boardUrl: root.dataset.boardUrl || "",
            createUrl: root.dataset.createUrl || "",
            saveUrl: root.dataset.saveUrl || "",
            editUrl: root.dataset.editUrl || "",
            uploadUrl: root.dataset.uploadUrl || "",
            downloadUrl: root.dataset.downloadUrl || "",
            invoiceSearchUrl: root.dataset.invoiceSearchUrl || "",
            clientSearchUrl: root.dataset.clientSearchUrl || "",
            ownerSearchUrl: root.dataset.ownerSearchUrl || "",
            initialStartDate: root.dataset.initialStartDate || "",
            initialEndDate: root.dataset.initialEndDate || ""
        };
        const isCommercialMode = normalizeText(config.mode) === "commercial";

        const elements = {
            status: root.querySelector("[data-hw-status]"),
            csvFile: root.querySelector("[data-hw-csv-file]"),
            analyzeCsvBtn: root.querySelector("[data-hw-analyze-csv]"),
            provisionCsvBtn: root.querySelector("[data-hw-provision-csv]"),
            importStatus: root.querySelector("[data-hw-import-status]"),
            summaryWrap: root.querySelector("[data-hw-summary]"),
            summaryList: root.querySelector("[data-hw-summary-list]"),
            columnsWrap: root.querySelector("[data-hw-columns-wrap]"),
            columnsBody: root.querySelector("[data-hw-columns-body]"),
            systemColumnsNote: root.querySelector("[data-hw-system-columns-note]"),
            provisionWrap: root.querySelector("[data-hw-provision-wrap]"),
            provisionList: root.querySelector("[data-hw-provision-list]"),
            boardStatus: root.querySelector("[data-hw-board-status]"),
            stateFilter: root.querySelector("[data-hw-state-filter]"),
            startDate: root.querySelector("[data-hw-start-date]"),
            endDate: root.querySelector("[data-hw-end-date]"),
            filterLabel: root.querySelector("[data-hw-filter-label]"),
            refreshBtn: root.querySelector("[data-hw-refresh]"),
            selectedActionBtn: root.querySelector("[data-hw-selected-action]"),
            editSelectedBtn: root.querySelector("[data-hw-edit-selected]"),
            selectionSummary: root.querySelector("[data-hw-selection-summary]"),
            warnings: root.querySelector("[data-hw-warnings]"),
            stateSummary: root.querySelector("[data-hw-state-summary]"),
            rows: root.querySelector("[data-hw-rows]"),
            createForm: root.querySelector("[data-hw-create-form]"),
            createStatus: root.querySelector("[data-hw-create-status]"),
            createLines: root.querySelector("[data-hw-create-lines]"),
            addCreateLineBtn: root.querySelector("[data-hw-add-create-line]"),
            saveCreateBtn: root.querySelector("[data-hw-save-create]"),
            createClientOptions: root.querySelector("[data-hw-create-client-options]"),
            createClientHint: root.querySelector("[data-hw-create-client-hint]"),
            createFields: {
                purchaseOrderNumber: root.querySelector('[data-hw-create-field="purchaseOrderNumber"]'),
                odcDate: root.querySelector('[data-hw-create-field="odcDate"]'),
                clientName: root.querySelector('[data-hw-create-field="clientName"]')
            },
            selectAll: root.querySelector("[data-hw-select-all]"),
            totalRecords: root.querySelector("[data-hw-total-records]"),
            totalSales: root.querySelector("[data-hw-total-sales]"),
            pendingActions: root.querySelector("[data-hw-pending-actions]"),
            closedCount: root.querySelector("[data-hw-closed-count]"),
            modal: root.querySelector("[data-hw-modal]"),
            modalTitle: root.querySelector("[data-hw-modal-title]"),
            modalSubtitle: root.querySelector("[data-hw-modal-subtitle]"),
            modalStatus: root.querySelector("[data-hw-modal-status]"),
            modalMeta: root.querySelector("[data-hw-modal-meta]"),
            form: root.querySelector("[data-hw-form]"),
            saveStageBtn: root.querySelector("[data-hw-save-stage]"),
            recordId: root.querySelector("[data-hw-record-id]"),
            actionKey: root.querySelector("[data-hw-action-key]"),
            recordName: root.querySelector("[data-hw-record-name]"),
            recordMeta: root.querySelector("[data-hw-record-meta]"),
            recordState: root.querySelector("[data-hw-record-state]"),
            closeModalButtons: Array.from(root.querySelectorAll("[data-hw-close-modal]")),
            stagePanels: Array.from(root.querySelectorAll("[data-hw-stage-panel]")),
            documentationRows: root.querySelector("[data-hw-documentation-rows]"),
            invoiceOptions: root.querySelector("[data-hw-invoice-options]"),
            fields: {
                purchaseOrderNumber: root.querySelector('[data-hw-field="purchaseOrderNumber"]'),
                freightValue: root.querySelector('[data-hw-field="freightValue"]'),
                odcDate: root.querySelector('[data-hw-field="odcDate"]'),
                supplierUnitCost: root.querySelector('[data-hw-field="supplierUnitCost"]'),
                provider: root.querySelector('[data-hw-field="provider"]'),
                supplierPaymentDate: root.querySelector('[data-hw-field="supplierPaymentDate"]'),
                deliveryRecordDate: root.querySelector('[data-hw-field="deliveryRecordDate"]'),
                invoiceNumber: root.querySelector('[data-hw-field="invoiceNumber"]')
            },
            fileInputs: Array.from(root.querySelectorAll("[data-hw-file-input]")),
            fileNames: Array.from(root.querySelectorAll("[data-hw-file-name]")),
            fileHints: Array.from(root.querySelectorAll("[data-hw-file-hint]")),
            downloadLinks: Array.from(root.querySelectorAll("[data-hw-download-link]"))
        };

        elements.editModal = root.querySelector("[data-hw-edit-modal]");
        elements.editTitle = root.querySelector("[data-hw-edit-title]");
        elements.editSubtitle = root.querySelector("[data-hw-edit-subtitle]");
        elements.editStatus = root.querySelector("[data-hw-edit-status]");
        elements.editForm = root.querySelector("[data-hw-edit-form]");
        elements.editCount = root.querySelector("[data-hw-edit-count]");
        elements.editMeta = root.querySelector("[data-hw-edit-meta]");
        elements.saveEditBtn = root.querySelector("[data-hw-save-edit]");
        elements.closeEditModalButtons = Array.from(root.querySelectorAll("[data-hw-close-edit-modal]"));
        elements.clientOptions = root.querySelector("[data-hw-client-options]");
        elements.clientHint = root.querySelector("[data-hw-client-hint]");
        elements.ownerOptions = root.querySelector("[data-hw-owner-options]");
        elements.ownerHint = root.querySelector("[data-hw-owner-hint]");
        elements.editFields = {
            ownerName: root.querySelector('[data-hw-edit-field="ownerName"]'),
            clientName: root.querySelector('[data-hw-edit-field="clientName"]'),
            quantity: root.querySelector('[data-hw-edit-field="quantity"]'),
            saleUnit: root.querySelector('[data-hw-edit-field="saleUnit"]'),
            totalSale: root.querySelector('[data-hw-edit-field="totalSale"]'),
            stateValue: root.querySelector('[data-hw-edit-field="stateValue"]'),
            purchaseOrderNumber: root.querySelector('[data-hw-edit-field="purchaseOrderNumber"]'),
            odcDateValue: root.querySelector('[data-hw-edit-field="odcDateValue"]'),
            supplierUnitCost: root.querySelector('[data-hw-edit-field="supplierUnitCost"]'),
            supplierTotal: root.querySelector('[data-hw-edit-field="supplierTotal"]'),
            freightValue: root.querySelector('[data-hw-edit-field="freightValue"]'),
            utility: root.querySelector('[data-hw-edit-field="utility"]'),
            marginValue: root.querySelector('[data-hw-edit-field="marginValue"]'),
            provider: root.querySelector('[data-hw-edit-field="provider"]'),
            supplierPaymentDateValue: root.querySelector('[data-hw-edit-field="supplierPaymentDateValue"]'),
            deliveryRecordDateValue: root.querySelector('[data-hw-edit-field="deliveryRecordDateValue"]'),
            invoiceNumber: root.querySelector('[data-hw-edit-field="invoiceNumber"]')
        };

        if (!config.boardUrl
            || !config.saveUrl
            || !config.uploadUrl
            || !elements.rows
            || !elements.modal
            || !elements.form
            || !elements.stateFilter
            || !elements.refreshBtn
            || (!isCommercialMode && !elements.selectedActionBtn)) {
            return;
        }

        const state = {
            preview: null,
            board: null,
            rows: [],
            displayItems: [],
            selectedRecordIds: new Set(),
            expandedGroups: new Set(),
            modalRecords: [],
            pendingFiles: {},
            busy: false,
            boardLoading: false,
            saving: false,
            editRecords: [],
            editDirtyFields: new Set(),
            editClientSelection: null,
            editOwnerSelection: null,
            clientLookupTimer: 0,
            clientLookupSequence: 0,
            clientSuggestions: [],
            ownerLookupTimer: 0,
            ownerLookupSequence: 0,
            ownerSuggestions: [],
            createClientSelection: null,
            createClientLookupTimer: 0,
            createClientLookupSequence: 0,
            createClientSuggestions: [],
            createLineSequence: 0,
            invoiceSuggestions: [],
            invoiceLookupTimer: 0,
            invoiceLookupSequence: 0
        };

        [elements.status, elements.importStatus, elements.boardStatus, elements.modalStatus, elements.editStatus, elements.createStatus]
            .filter(Boolean)
            .forEach(element => {
                element.dataset.baseClass = element.className;
            });

        elements.fileHints.forEach(item => {
            item.dataset.defaultHint = item.textContent || "";
        });

        const stageConfig = {
            "register-documentation": {
                title: "Registrar documentación",
                subtitle: "Completa la documentación inicial y deja las filas listas para pago a proveedor.",
                buttonLabel: "Registrar documentación",
                meta: "Próximo estado: Ok para pago a proveedor",
                requiredFiles: ["cr07a_ordendecompra", "cr07a_adjuntarproforma"]
            },
            "register-supplier-payment": {
                title: "Registrar pago a proveedor",
                subtitle: "Adjunta el soporte de pago y registra la fecha correspondiente.",
                buttonLabel: "Registrar pago a proveedor",
                meta: "Próximo estado: Pagada a proveedor",
                requiredFiles: ["cr07a_pagoaproveedor"]
            },
            "register-received": {
                title: "Registrar recibido",
                subtitle: "Confirma el recibido por comercial para mover las filas a tránsito.",
                buttonLabel: "Aprobar recibido por comercial",
                meta: "Próximo estado: En tránsito a oficina o cliente",
                requiredFiles: []
            },
            "register-client-received": {
                title: "Registrar recibido cliente",
                subtitle: "Carga el acta de entrega y registra su fecha para habilitar la facturación.",
                buttonLabel: "Registrar recibido cliente",
                meta: "Próximo estado: Entregado en espera de facturación",
                requiredFiles: ["cr07a_actadeentrega"]
            },
            "register-invoice": {
                title: "Registrar factura",
                subtitle: "Selecciona una factura exacta desde la tabla Facturación.",
                buttonLabel: "Registrar factura",
                meta: "Próximo estado: Facturado en espera de pago",
                requiredFiles: []
            },
            "register-client-payment": {
                title: "Registrar pago cliente",
                subtitle: "Se consultará la factura en Facturación para cerrar automáticamente si ya tiene pago.",
                buttonLabel: "Validar pago cliente",
                meta: "Próximo estado: Cerrado si la factura ya tiene pago",
                requiredFiles: []
            }
        };

        elements.csvFile?.addEventListener("change", handleCsvFileChange);
        elements.analyzeCsvBtn?.addEventListener("click", previewCsv);
        elements.provisionCsvBtn?.addEventListener("click", provisionCsv);
        elements.refreshBtn.addEventListener("click", () => loadBoard());
        elements.selectedActionBtn?.addEventListener("click", openSelectedRows);
        elements.editSelectedBtn?.addEventListener("click", openBulkEditForSelectedRows);
        elements.addCreateLineBtn?.addEventListener("click", () => addCreateLine());
        elements.createForm?.addEventListener("submit", async event => {
            event.preventDefault();
            await createCommercialOrder();
        });
        elements.createFields.clientName?.addEventListener("input", handleCreateClientLookupInput);
        if (elements.startDate) {
            elements.startDate.value = config.initialStartDate || "";
            elements.startDate.addEventListener("change", handleDateFilterChange);
        }
        if (elements.endDate) {
            elements.endDate.value = config.initialEndDate || "";
            elements.endDate.addEventListener("change", handleDateFilterChange);
        }
        elements.stateFilter.addEventListener("change", () => {
            elements.filterLabel.textContent = elements.stateFilter.options[elements.stateFilter.selectedIndex]?.text || "Todos los estados";
            state.selectedRecordIds.clear();
            loadBoard();
        });

        function handleDateFilterChange() {
            state.selectedRecordIds.clear();
            loadBoard();
        }

        elements.selectAll?.addEventListener("change", () => {
            const checked = Boolean(elements.selectAll?.checked);
            state.selectedRecordIds.clear();
            if (checked) {
                state.rows.forEach(row => state.selectedRecordIds.add(row.recordId));
            }

            renderRows(state.board);
            renderSelectionState();
        });

        elements.rows.addEventListener("click", event => {
            const target = event.target;
            if (!(target instanceof HTMLElement)) {
                return;
            }

            const toggleButton = target.closest("[data-hw-toggle-group]");
            if (toggleButton instanceof HTMLElement) {
                const groupKey = toggleButton.dataset.hwToggleGroup || "";
                if (state.expandedGroups.has(groupKey)) {
                    state.expandedGroups.delete(groupKey);
                } else {
                    state.expandedGroups.add(groupKey);
                }

                renderRows(state.board);
                return;
            }

            const groupAction = target.closest("[data-hw-action-group]");
            if (groupAction instanceof HTMLButtonElement) {
                const group = findDisplayGroup(groupAction.dataset.hwActionGroup || "");
                if (group) {
                    openModalForRecords(group.rows);
                }
                return;
            }

            const actionButton = target.closest("[data-hw-action-record]");
            if (actionButton instanceof HTMLButtonElement) {
                const record = findRow(actionButton.dataset.hwActionRecord || "");
                if (record) {
                    openModalForRecords([record]);
                }
            }
        });

        elements.rows.addEventListener("change", event => {
            const target = event.target;
            if (!(target instanceof HTMLInputElement)) {
                return;
            }

            if (target.matches("[data-hw-select-record]")) {
                const recordId = target.dataset.hwSelectRecord || "";
                if (target.checked) {
                    state.selectedRecordIds.add(recordId);
                } else {
                    state.selectedRecordIds.delete(recordId);
                }

                renderRows(state.board);
                renderSelectionState();
                return;
            }

            if (target.matches("[data-hw-select-group]")) {
                const group = findDisplayGroup(target.dataset.hwSelectGroup || "");
                if (!group) {
                    return;
                }

                group.rows.forEach(row => {
                    if (target.checked) {
                        state.selectedRecordIds.add(row.recordId);
                    } else {
                        state.selectedRecordIds.delete(row.recordId);
                    }
                });

                renderRows(state.board);
                renderSelectionState();
            }
        });

        elements.form.addEventListener("submit", async event => {
            event.preventDefault();
            await saveStage();
        });

        elements.editForm?.addEventListener("submit", async event => {
            event.preventDefault();
            await saveBulkEdit();
        });

        elements.editForm?.addEventListener("input", handleBulkEditInput);
        elements.editForm?.addEventListener("change", handleBulkEditChange);

        elements.form.addEventListener("change", event => {
            const target = event.target;
            if (!(target instanceof HTMLInputElement)) {
                return;
            }

            const documentationInput = target.closest("[data-hw-doc-file-input]");
            if (documentationInput instanceof HTMLInputElement) {
                const recordId = documentationInput.dataset.hwRecordId || "";
                const fieldName = documentationInput.dataset.hwDocFileInput || "";
                state.pendingFiles[buildDocumentationFileKey(recordId, fieldName)] =
                    documentationInput.files && documentationInput.files.length > 0 ? documentationInput.files[0] : null;
                renderDocumentationFileNames();
                return;
            }

            const globalFileInput = target.closest("[data-hw-file-input]");
            if (globalFileInput instanceof HTMLInputElement) {
                const fieldName = globalFileInput.dataset.hwFileInput || "";
                state.pendingFiles[fieldName] =
                    globalFileInput.files && globalFileInput.files.length > 0 ? globalFileInput.files[0] : null;
                renderFileCards();
            }
        });

        elements.closeModalButtons.forEach(button => {
            button.addEventListener("click", closeModal);
        });

        elements.closeEditModalButtons.forEach(button => {
            button.addEventListener("click", closeEditModal);
        });

        elements.modal.addEventListener("click", event => {
            const target = event.target;
            if (target instanceof HTMLElement && target.hasAttribute("data-hw-close-modal")) {
                closeModal();
            }
        });

        elements.editModal?.addEventListener("click", event => {
            const target = event.target;
            if (!(target instanceof HTMLElement)) {
                return;
            }

            if (target.hasAttribute("data-hw-close-edit-modal")) {
                closeEditModal();
                return;
            }

            const option = target.closest("[data-hw-client-option]");
            if (option instanceof HTMLElement) {
                selectClientOption(option);
                return;
            }

            const ownerOption = target.closest("[data-hw-owner-option]");
            if (ownerOption instanceof HTMLElement) {
                selectOwnerOption(ownerOption);
            }
        });

        elements.createForm?.addEventListener("click", event => {
            const target = event.target;
            if (!(target instanceof HTMLElement)) {
                return;
            }

            const clientOption = target.closest("[data-hw-create-client-option]");
            if (clientOption instanceof HTMLElement) {
                selectCreateClientOption(clientOption);
                return;
            }

            const removeLineButton = target.closest("[data-hw-remove-create-line]");
            if (removeLineButton instanceof HTMLButtonElement) {
                removeCreateLine(removeLineButton.dataset.hwRemoveCreateLine || "");
            }
        });

        document.addEventListener("click", event => {
            const target = event.target;
            if (!(target instanceof HTMLElement)) {
                return;
            }

            if (!target.closest(".hardware-lookup")) {
                closeLookupMenus();
                closeCreateClientLookupMenu();
            }
        });

        document.addEventListener("keydown", event => {
            if (event.key !== "Escape") {
                return;
            }

            if (elements.editModal && !elements.editModal.hidden) {
                closeEditModal();
            } else if (!elements.modal.hidden) {
                closeModal();
            }
        });

        elements.fields.invoiceNumber?.addEventListener("input", handleInvoiceLookupInput);
        elements.fields.invoiceNumber?.addEventListener("change", syncInvoiceSelection);
        elements.fields.invoiceNumber?.addEventListener("blur", syncInvoiceSelection);

        if (isCommercialMode && elements.createLines && !elements.createLines.children.length) {
            addCreateLine();
        }

        loadBoard();

        function handleCsvFileChange() {
            state.preview = null;
            if (elements.provisionCsvBtn) {
                elements.provisionCsvBtn.disabled = true;
            }
            hidePreview();
            hideProvisionResult();
            clearStatus(elements.importStatus);
        }

        async function previewCsv() {
            const file = elements.csvFile?.files && elements.csvFile.files.length > 0
                ? elements.csvFile.files[0]
                : null;
            if (!file) {
                setStatus(elements.importStatus, "warning", "Selecciona un archivo CSV antes de analizar.");
                return;
            }

            try {
                setBusy(true);
                hideProvisionResult();
                setStatus(elements.importStatus, "info", "Analizando estructura del CSV...");
                const formData = new FormData();
                formData.append("file", file);
                const result = await fetchJson(config.previewUrl, {
                    method: "POST",
                    body: formData
                });

                state.preview = result;
                renderPreview(result);
                setStatus(elements.importStatus, "success", result?.message || "Vista previa lista.");
            } catch (error) {
                state.preview = null;
                hidePreview();
                hideProvisionResult();
                setStatus(elements.importStatus, "error", getErrorMessage(error));
            } finally {
                setBusy(false);
            }
        }

        async function provisionCsv() {
            const file = elements.csvFile?.files && elements.csvFile.files.length > 0
                ? elements.csvFile.files[0]
                : null;
            if (!file) {
                setStatus(elements.importStatus, "warning", "Selecciona un archivo CSV antes de importar.");
                return;
            }

            if (!state.preview) {
                setStatus(elements.importStatus, "warning", "Analiza primero el archivo para confirmar el esquema.");
                return;
            }

            try {
                setBusy(true);
                setStatus(elements.importStatus, "info", "Creando tabla y columnas de Hardware en Dataverse...");
                const formData = new FormData();
                formData.append("file", file);
                const result = await fetchJson(config.provisionUrl, {
                    method: "POST",
                    body: formData
                });

                renderProvisionResult(result);
                setStatus(elements.importStatus, "success", result?.message || "Carga completada.");
                await loadBoard();
            } catch (error) {
                hideProvisionResult();
                setStatus(elements.importStatus, "error", getErrorMessage(error));
            } finally {
                setBusy(false);
            }
        }

        function renderPreview(result) {
            if (!elements.summaryWrap || !elements.summaryList || !elements.columnsWrap || !elements.columnsBody) {
                return;
            }

            const columns = Array.isArray(result?.columns) ? result.columns : [];
            const systemColumns = Array.isArray(result?.systemColumns) ? result.systemColumns : [];

            elements.summaryList.innerHTML = [
                `<li><strong>Archivo:</strong> ${escapeHtml(result?.fileName || "")}</li>`,
                `<li><strong>Tabla objetivo:</strong> <code>${escapeHtml(result?.tableLogicalName || "")}</code></li>`,
                `<li><strong>Separador detectado:</strong> ${escapeHtml(result?.detectedDelimiterLabel || "-")}</li>`,
                `<li><strong>Filas detectadas:</strong> ${formatNumber(result?.totalRows || 0)}</li>`,
                `<li><strong>Columnas del CSV:</strong> ${formatNumber(result?.totalColumns || columns.length)}</li>`
            ].join("");
            elements.summaryWrap.hidden = false;

            if (elements.systemColumnsNote) {
                elements.systemColumnsNote.textContent = systemColumns.length > 0
                    ? `Campos técnicos adicionales: ${systemColumns.join(", ")}`
                    : "";
            }

            elements.columnsBody.innerHTML = columns.map(column => `
                <tr>
                    <td>${escapeHtml(column.sourceHeader || column.displayLabel || "")}</td>
                    <td><code>${escapeHtml(column.logicalName || "")}</code></td>
                    <td>${escapeHtml(column.dataverseType || "")}</td>
                    <td>${escapeHtml(column.exampleValue || "-")}</td>
                </tr>
            `).join("");

            elements.columnsWrap.hidden = columns.length === 0;
            if (elements.provisionCsvBtn) {
                elements.provisionCsvBtn.disabled = columns.length === 0 || Number(result?.totalRows || 0) === 0;
            }
        }

        function hidePreview() {
            if (elements.summaryWrap) {
                elements.summaryWrap.hidden = true;
            }
            if (elements.columnsWrap) {
                elements.columnsWrap.hidden = true;
            }
            if (elements.summaryList) {
                elements.summaryList.innerHTML = "";
            }
            if (elements.columnsBody) {
                elements.columnsBody.innerHTML = "";
            }
            if (elements.systemColumnsNote) {
                elements.systemColumnsNote.textContent = "";
            }
        }

        function renderProvisionResult(result) {
            if (!elements.provisionWrap || !elements.provisionList) {
                return;
            }

            elements.provisionList.innerHTML = [
                `<li><strong>Tabla:</strong> <code>${escapeHtml(result?.tableLogicalName || "")}</code></li>`,
                `<li><strong>Entity set:</strong> <code>${escapeHtml(result?.entitySetName || "")}</code></li>`,
                `<li><strong>Tabla creada:</strong> ${result?.tableCreated ? "Sí" : "No"}</li>`,
                `<li><strong>Columnas nuevas:</strong> ${formatNumber(result?.createdColumnsCount || 0)}</li>`,
                `<li><strong>Columnas reutilizadas:</strong> ${formatNumber(result?.existingColumnsCount || 0)}</li>`,
                `<li><strong>Filas importadas:</strong> ${formatNumber(result?.importedCount || 0)}</li>`,
                `<li><strong>Filas duplicadas omitidas:</strong> ${formatNumber(result?.skippedDuplicatesCount || 0)}</li>`
            ].join("");
            elements.provisionWrap.hidden = false;
        }

        function hideProvisionResult() {
            if (elements.provisionWrap) {
                elements.provisionWrap.hidden = true;
            }
            if (elements.provisionList) {
                elements.provisionList.innerHTML = "";
            }
        }

        async function loadBoard() {
            if (state.boardLoading) {
                return;
            }

            try {
                state.boardLoading = true;
                elements.refreshBtn.disabled = true;
                setStatus(elements.boardStatus, "info", "Cargando tabla de Hardware...");

                const result = await fetchJson(buildBoardUrl(), { method: "GET" });
                state.board = result;
                state.rows = Array.isArray(result?.rows) ? result.rows : [];
                trimSelectionToVisibleRows();
                renderBoard(result);
            } catch (error) {
                elements.rows.innerHTML = `
                    <div class="hardware-empty">${escapeHtml(getErrorMessage(error))}</div>
                `;
                setStatus(elements.boardStatus, "error", getErrorMessage(error));
            } finally {
                state.boardLoading = false;
                elements.refreshBtn.disabled = false;
                renderSelectionState();
            }
        }

        function renderBoard(board) {
            renderFilterOptions(board);
            renderWarnings(board);
            renderStateSummary(board);
            renderSummaryCards(board);
            renderRows(board);

            const warnings = Array.isArray(board?.warnings) ? board.warnings.filter(Boolean) : [];
            const summaryParts = [];
            if (board?.message) {
                summaryParts.push(String(board.message));
            }

            const kind = warnings.length > 0
                ? "warning"
                : state.rows.length > 0 ? "success" : "info";
            setStatus(elements.boardStatus, kind, summaryParts.join(" ").trim() || "Tabla cargada.");
        }

        function renderFilterOptions(board) {
            const options = Array.isArray(board?.stateOptions) ? board.stateOptions : [];
            const selectedValue = board?.selectedStateValue ? String(board.selectedStateValue) : "";

            elements.stateFilter.innerHTML = `
                <option value="">Todos los estados</option>
                ${options.map(option => `
                    <option value="${escapeHtml(option.value)}" ${String(option.value) === selectedValue ? "selected" : ""}>
                        ${escapeHtml(option.label || "")}
                    </option>
                `).join("")}
            `;

            elements.filterLabel.textContent = buildActiveFilterLabel(board);
        }

        function renderWarnings(board) {
            if (!elements.warnings) {
                return;
            }

            const warnings = Array.isArray(board?.warnings) ? board.warnings.filter(Boolean) : [];
            if (!warnings.length) {
                elements.warnings.hidden = true;
                elements.warnings.innerHTML = "";
                return;
            }

            elements.warnings.hidden = false;
            elements.warnings.innerHTML = warnings
                .map(message => `<div class="hardware-warning-list__item">${escapeHtml(message)}</div>`)
                .join("");
        }

        function renderStateSummary(board) {
            if (!elements.stateSummary) {
                return;
            }

            const items = Array.isArray(board?.stateSummaries) ? board.stateSummaries : [];
            elements.stateSummary.innerHTML = items.map(item => `
                <article class="hardware-state-card">
                    <span class="hardware-state-card__label">${escapeHtml(item.label || "")}</span>
                    <strong class="hardware-state-card__value">${formatNumber(item.count || 0)}</strong>
                    <span class="hardware-pill ${toneClass(item.tone)}">${escapeHtml(item.label || "")}</span>
                </article>
            `).join("");
        }

        function renderSummaryCards(board) {
            const rows = Array.isArray(board?.rows) ? board.rows : [];
            const totalVisibleSales = rows.reduce((total, row) => total + Number(row?.totalSale || 0), 0);
            const pendingActions = rows.filter(row => Boolean(row?.hasAction)).length;
            const closedCount = rows.filter(row => Number(row?.stateValue || 0) === 645250006).length;

            setText(elements.totalRecords, formatNumber(rows.length));
            setText(elements.totalSales, formatCurrency(totalVisibleSales));
            setText(elements.pendingActions, formatNumber(pendingActions));
            setText(elements.closedCount, formatNumber(closedCount));
        }

        function renderRows(board) {
            const rows = Array.isArray(board?.rows) ? board.rows : [];
            if (isCommercialMode) {
                renderCommercialRows(rows);
                return;
            }

            const ownerTables = buildOwnerTables(rows);
            state.displayItems = ownerTables.flatMap(owner => owner.items);

            if (!rows.length) {
                elements.rows.innerHTML = `
                    <div class="hardware-empty">No hay registros de Hardware para mostrar.</div>
                `;
                syncSelectAllState();
                return;
            }

            elements.rows.innerHTML = ownerTables.map(renderOwnerTable).join("");

            syncGroupCheckboxStates();
            syncSelectAllState();
        }

        function renderCommercialRows(rows) {
            const groups = buildCommercialGroups(rows);
            state.displayItems = groups;

            if (!rows.length) {
                elements.rows.innerHTML = `
                    <div class="hardware-empty">No hay registros de Hardware para tu usuario.</div>
                `;
                syncSelectAllState();
                return;
            }

            elements.rows.innerHTML = `
                <div class="hardware-table-wrap">
                    <table class="table align-middle hardware-table hardware-commercial-table">
                        <thead>
                            <tr>
                                <th>Orden / Cliente</th>
                                <th>cr07a_name</th>
                                <th class="text-end">cr07a_cant</th>
                                <th class="text-end">cr07a_costountproveedor</th>
                                <th class="text-end">cr07a_ventaunidad</th>
                                <th>cr07a_proveedor</th>
                                <th>cr07a_ordendecompra</th>
                                <th>cr07a_adjuntarproforma</th>
                                <th>Estado</th>
                                <th>Botón</th>
                            </tr>
                        </thead>
                        <tbody>
                            ${groups.map(renderCommercialGroup).join("")}
                        </tbody>
                    </table>
                </div>
            `;
        }

        function buildCommercialGroups(rows) {
            const groups = new Map();
            rows.forEach((row, index) => {
                const orderNumber = String(row?.purchaseOrderNumber || "").trim() || "Sin orden";
                const key = normalizeText(orderNumber) || `sin-orden-${index}`;
                if (!groups.has(key)) {
                    groups.set(key, {
                        type: "group",
                        key,
                        orderNumber,
                        rows: [],
                        index
                    });
                }

                groups.get(key).rows.push(row);
            });

            return Array.from(groups.values())
                .sort((left, right) => left.index - right.index);
        }

        function renderCommercialGroup(group) {
            const first = group.rows[0] || {};
            const validAction = validateActionRecords(group.rows);
            const totalQuantity = group.rows.reduce((total, row) => total + Number(row?.quantity || 0), 0);
            const clientLabel = getCommonValue(group.rows, "clientName") || "Varios clientes";
            const odcDate = getCommonValue(group.rows, "odcDateDisplay") || "Varias fechas";
            return `
                <tr class="hardware-table__row hardware-commercial-table__group ${toneClass(first.stateTone)}">
                    <td colspan="10">
                        <div class="hardware-commercial-order">
                            <div>
                                <strong>${escapeHtml(group.orderNumber)}</strong>
                                <span>${escapeHtml(clientLabel)} · ${escapeHtml(odcDate)} · ${formatNumber(group.rows.length)} fila(s) · ${formatNumber(totalQuantity)} und</span>
                            </div>
                            ${validAction.ok
                                ? `<button type="button" class="btn btn-sm btn-primary" data-hw-action-group="${escapeHtml(group.key)}">${escapeHtml(group.rows[0].actionLabel || "Gestionar")}</button>`
                                : `<span class="hardware-table__submeta">${escapeHtml(validAction.message || "Sin botón")}</span>`}
                        </div>
                    </td>
                </tr>
                ${group.rows.map(row => renderCommercialRecordRow(row)).join("")}
            `;
        }

        function renderCommercialRecordRow(row) {
            return `
                <tr class="hardware-table__row hardware-table__row--child ${toneClass(row?.stateTone)}">
                    <td class="hardware-table__client-cell">
                        <div class="hardware-table__submeta">${escapeHtml(row?.purchaseOrderNumber || "Sin orden")}</div>
                        <strong>${escapeHtml(row?.clientName || "Sin cliente")}</strong>
                        <div class="hardware-table__submeta">${escapeHtml(row?.odcDateDisplay || "Sin fecha")}</div>
                    </td>
                    <td>
                        <div class="hardware-table__title">
                            <strong>${escapeHtml(row?.name || "Hardware")}</strong>
                        </div>
                    </td>
                    <td class="text-end">${formatNumber(row?.quantity || 0)}</td>
                    <td class="text-end">${formatCurrency(row?.supplierUnitCost || 0)}</td>
                    <td class="text-end">${formatCurrency(row?.saleUnit || 0)}</td>
                    <td>${escapeHtml(row?.provider || "-")}</td>
                    <td>${renderCommercialFileLink(row, "cr07a_ordendecompra")}</td>
                    <td>${renderCommercialFileLink(row, "cr07a_adjuntarproforma")}</td>
                    <td class="hardware-table__state-cell">${renderStatePill(row?.stateLabel || "Sin estado", row?.stateTone || "")}</td>
                    <td>
                        <div class="hardware-action-cell">
                            ${row?.hasAction
                                ? `<button type="button" class="btn btn-sm btn-primary" data-hw-action-record="${escapeHtml(row?.recordId || "")}">${escapeHtml(row?.actionLabel || "Gestionar")}</button>`
                                : `<span class="hardware-table__submeta">Sin botón</span>`}
                        </div>
                    </td>
                </tr>
            `;
        }

        function renderCommercialFileLink(row, fieldName) {
            const hasFile = hasExistingFile(row, fieldName);
            const fileName = resolveExistingFileName(row, fieldName);
            if (!hasFile) {
                return `<span class="hardware-table__submeta">Sin archivo</span>`;
            }

            return `
                <a class="hardware-file-card__link" href="${escapeHtml(buildDownloadUrl(row.recordId, fieldName))}" target="_blank" rel="noopener">
                    ${escapeHtml(fileName || "Descargar")}
                </a>
            `;
        }

        function buildOwnerTables(rows) {
            const owners = new Map();
            rows.forEach((row, index) => {
                const ownerKey = normalizeText(row?.ownerId || row?.ownerName || "sin-owner") || "sin-owner";
                if (!owners.has(ownerKey)) {
                    owners.set(ownerKey, {
                        key: ownerKey,
                        ownerId: row?.ownerId || "",
                        ownerName: row?.ownerName || "Sin propietario",
                        rows: [],
                        index
                    });
                }

                owners.get(ownerKey).rows.push(row);
            });

            return Array.from(owners.values())
                .sort((left, right) => left.index - right.index)
                .map(owner => ({
                    ...owner,
                    items: buildDisplayItems(owner.rows)
                }));
        }

        function renderOwnerTable(owner) {
            const totalSale = owner.rows.reduce((total, row) => total + Number(row?.totalSale || 0), 0);
            const totalQuantity = owner.rows.reduce((total, row) => total + Number(row?.quantity || 0), 0);
            return `
                <section class="hardware-owner-table" data-hw-owner-table="${escapeHtml(owner.key)}">
                    <div class="hardware-owner-table__header">
                        <div>
                            <h3>${escapeHtml(owner.ownerName || "Sin propietario")}</h3>
                            <p>${formatNumber(owner.rows.length)} registro(s) · ${formatNumber(totalQuantity)} und · ${formatCurrency(totalSale)}</p>
                        </div>
                    </div>
                    <div class="hardware-table-wrap">
                        <table class="table align-middle hardware-table">
                            <colgroup>
                                <col class="hardware-table__col-select" />
                                <col class="hardware-table__col-client" />
                                <col class="hardware-table__col-order" />
                                <col class="hardware-table__col-date" />
                                <col class="hardware-table__col-quantity" />
                                <col class="hardware-table__col-sale-unit" />
                                <col class="hardware-table__col-total" />
                                <col class="hardware-table__col-state" />
                                <col class="hardware-table__col-action" />
                            </colgroup>
                            <thead>
                                <tr>
                                    <th class="hardware-table__select-col"></th>
                                    <th>Cliente</th>
                                    <th>No orden</th>
                                    <th>Fecha ODC</th>
                                    <th class="text-end">Cantidad</th>
                                    <th class="text-end">Venta unidad</th>
                                    <th class="text-end">Total línea</th>
                                    <th class="hardware-table__state-col">Estado</th>
                                    <th class="hardware-table__action-col">Botón</th>
                                </tr>
                            </thead>
                            <tbody>
                                ${owner.items.map(item => item.type === "group"
                                    ? renderGroupRows(item)
                                    : renderRecordRow(item.row, false)).join("")}
                            </tbody>
                        </table>
                    </div>
                </section>
            `;
        }

        function renderGroupRows(group) {
            const expanded = state.expandedGroups.has(group.key);
            const totalQuantity = group.rows.reduce((total, row) => total + Number(row?.quantity || 0), 0);
            const totalSale = group.rows.reduce((total, row) => total + Number(row?.totalSale || 0), 0);
            const allSelected = group.rows.every(row => state.selectedRecordIds.has(row.recordId));
            const first = group.rows[0] || {};
            const clientLabel = getCommonValue(group.rows, "clientName") || "Varios clientes";

            return `
                <tr class="hardware-table__row hardware-table__group ${toneClass(first.stateTone)}">
                    <td>
                        <input type="checkbox" class="form-check-input" data-hw-select-group="${escapeHtml(group.key)}" ${allSelected ? "checked" : ""} aria-label="Seleccionar grupo ${escapeHtml(group.orderNumber)}" />
                    </td>
                    <td class="hardware-table__client-cell">
                        <div class="hardware-table__title">
                            <button type="button" class="hardware-group-toggle" data-hw-toggle-group="${escapeHtml(group.key)}" aria-expanded="${expanded ? "true" : "false"}">
                                ${expanded ? "-" : "+"}
                            </button>
                            <strong>${escapeHtml(clientLabel)}</strong>
                            <div class="hardware-table__submeta">${formatNumber(group.rows.length)} filas agrupadas</div>
                        </div>
                    </td>
                    <td class="hardware-table__order-cell"><span class="hardware-order-number">${escapeHtml(group.orderNumber)}</span></td>
                    <td>${escapeHtml(getCommonValue(group.rows, "odcDateDisplay") || "Varias fechas")}</td>
                    <td class="text-end">${formatNumber(totalQuantity)}</td>
                    <td class="text-end">-</td>
                    <td class="text-end">${formatCurrency(totalSale)}</td>
                    <td class="hardware-table__state-cell">${renderStatePill(first.stateLabel || "Sin estado", first.stateTone || "")}</td>
                    <td>
                        <div class="hardware-action-cell">
                            ${first.hasAction
                                ? `<button type="button" class="btn btn-sm btn-primary" data-hw-action-group="${escapeHtml(group.key)}">${escapeHtml(first.actionLabel || "Gestionar")}</button>`
                                : `<span class="hardware-table__submeta">Sin botón</span>`}
                        </div>
                    </td>
                </tr>
                ${expanded ? group.rows.map(row => renderRecordRow(row, true)).join("") : ""}
            `;
        }

        function renderRecordRow(row, isChild) {
            const selected = state.selectedRecordIds.has(row?.recordId || "");
            return `
                <tr class="hardware-table__row ${isChild ? "hardware-table__row--child" : ""} ${toneClass(row?.stateTone)}">
                    <td>
                        <input type="checkbox" class="form-check-input" data-hw-select-record="${escapeHtml(row?.recordId || "")}" ${selected ? "checked" : ""} aria-label="Seleccionar ${escapeHtml(row?.name || "hardware")}" />
                    </td>
                    <td class="hardware-table__client-cell">
                        <div class="hardware-table__title">
                            <strong>${escapeHtml(row?.clientName || "-")}</strong>
                        </div>
                    </td>
                    <td class="hardware-table__order-cell">${row?.purchaseOrderNumber ? `<span class="hardware-order-number">${escapeHtml(row.purchaseOrderNumber)}</span>` : `<span class="hardware-table__submeta">Sin orden</span>`}</td>
                    <td>${row?.odcDateDisplay ? escapeHtml(row.odcDateDisplay) : `<span class="hardware-table__submeta">Sin fecha</span>`}</td>
                    <td class="text-end">${formatNumber(row?.quantity || 0)}</td>
                    <td class="text-end">${formatCurrency(row?.saleUnit || 0)}</td>
                    <td class="text-end">${formatCurrency(row?.totalSale || 0)}</td>
                    <td class="hardware-table__state-cell">${renderStatePill(row?.stateLabel || "Sin estado", row?.stateTone || "")}</td>
                    <td>
                        <div class="hardware-action-cell">
                            ${row?.hasAction
                                ? `<button type="button" class="btn btn-sm btn-primary" data-hw-action-record="${escapeHtml(row?.recordId || "")}">${escapeHtml(row?.actionLabel || "Gestionar")}</button>`
                                : `<span class="hardware-table__submeta">Sin botón</span>`}
                        </div>
                    </td>
                </tr>
            `;
        }

        function buildDisplayItems(rows) {
            const groups = new Map();
            const singles = [];

            rows.forEach((row, index) => {
                const orderNumber = String(row?.purchaseOrderNumber || "").trim();
                if (!orderNumber) {
                    singles.push({ type: "record", row, index });
                    return;
                }

                const key = buildGroupKey(row);
                if (!groups.has(key)) {
                    groups.set(key, {
                        type: "group",
                        key,
                        orderNumber,
                        stateValue: row.stateValue,
                        rows: [],
                        index
                    });
                }

                groups.get(key).rows.push(row);
            });

            groups.forEach(group => {
                if (group.rows.length === 1) {
                    singles.push({ type: "record", row: group.rows[0], index: group.index });
                } else {
                    singles.push(group);
                }
            });

            return singles.sort((left, right) => left.index - right.index);
        }

        function renderSelectionState() {
            if (!elements.selectedActionBtn) {
                return;
            }

            const selectedRecords = getSelectedRows();
            const validation = validateActionRecords(selectedRecords);
            const count = selectedRecords.length;

            if (count === 0) {
                elements.selectedActionBtn.disabled = true;
                elements.selectedActionBtn.textContent = "Gestionar selección";
                if (elements.editSelectedBtn) {
                    elements.editSelectedBtn.disabled = true;
                }
                setText(elements.selectionSummary, "Selecciona una o varias filas del mismo estado.");
                syncSelectAllState();
                return;
            }

            elements.selectedActionBtn.disabled = state.busy || !validation.ok;
            if (elements.editSelectedBtn) {
                elements.editSelectedBtn.disabled = state.busy;
            }
            elements.selectedActionBtn.textContent = validation.ok
                ? (selectedRecords[0].actionLabel || "Gestionar selección")
                : "Gestionar selección";
            setText(elements.selectionSummary, validation.ok
                ? `${formatNumber(count)} fila(s) seleccionada(s) · ${selectedRecords[0].stateLabel || "Sin estado"}`
                : `${validation.message} · Puedes editar la selección.`);
            syncSelectAllState();
        }

        function openSelectedRows() {
            const selectedRecords = getSelectedRows();
            const validation = validateActionRecords(selectedRecords);
            if (!validation.ok) {
                setStatus(elements.boardStatus, "warning", validation.message);
                return;
            }

            openModalForRecords(selectedRecords);
        }

        function openModalForRecords(records) {
            const validation = validateActionRecords(records);
            if (!validation.ok) {
                setStatus(elements.boardStatus, "warning", validation.message);
                return;
            }

            state.modalRecords = records.map(record => ({ ...record }));
            state.pendingFiles = {};
            resetFileInputs();
            renderModal();
            elements.modal.hidden = false;
            document.body.classList.add("hardware-modal-open");
        }

        function validateActionRecords(records) {
            if (!records.length) {
                return { ok: false, message: "Selecciona al menos una fila de Hardware." };
            }

            const states = new Set(records.map(row => Number(row?.stateValue || 0)));
            if (states.size !== 1) {
                return { ok: false, message: "Todas las filas seleccionadas deben estar en el mismo estado." };
            }

            const actionKeys = new Set(records.map(row => row?.actionKey || ""));
            if (actionKeys.size !== 1 || !records[0]?.hasAction || !records[0]?.actionKey) {
                return { ok: false, message: "El estado seleccionado no tiene una acción disponible." };
            }

            return { ok: true, message: "" };
        }

        function renderModal() {
            if (!state.modalRecords.length) {
                return;
            }

            const first = state.modalRecords[0];
            const actionKey = first.actionKey || "";
            const configItem = stageConfig[actionKey] || {
                title: "Gestionar hardware",
                subtitle: "Completa la etapa seleccionada.",
                buttonLabel: "Guardar etapa",
                meta: "Gestión manual",
                requiredFiles: []
            };

            elements.recordId.value = first.recordId || "";
            elements.actionKey.value = actionKey;
            elements.modalTitle.textContent = configItem.title;
            elements.modalSubtitle.textContent = configItem.subtitle;
            elements.modalMeta.textContent = configItem.meta;
            elements.saveStageBtn.textContent = configItem.buttonLabel;
            elements.recordName.textContent = state.modalRecords.length === 1
                ? first.name || "Hardware"
                : `${formatNumber(state.modalRecords.length)} filas seleccionadas`;
            elements.recordMeta.textContent = buildModalMeta(state.modalRecords);
            elements.recordState.className = `hardware-pill ${toneClass(first.stateTone)}`;
            elements.recordState.textContent = first.stateLabel || "Sin estado";

            setFieldValue(elements.fields.purchaseOrderNumber, getCommonValue(state.modalRecords, "purchaseOrderNumber"));
            setFieldValue(elements.fields.freightValue, formatInputNumber(sumValues(state.modalRecords, "freightValue")));
            setFieldValue(elements.fields.odcDate, getCommonValue(state.modalRecords, "odcDateValue"));
            setFieldValue(elements.fields.supplierPaymentDate, getCommonValue(state.modalRecords, "supplierPaymentDateValue"));
            setFieldValue(elements.fields.deliveryRecordDate, getCommonValue(state.modalRecords, "deliveryRecordDateValue"));
            setFieldValue(elements.fields.invoiceNumber, getCommonValue(state.modalRecords, "invoiceNumber"));
            elements.invoiceOptions.innerHTML = "";
            state.invoiceSuggestions = [];

            elements.stagePanels.forEach(panel => {
                panel.hidden = panel.dataset.hwStagePanel !== actionKey;
            });

            if (actionKey === "register-documentation") {
                renderDocumentationRows();
            } else if (elements.documentationRows) {
                elements.documentationRows.innerHTML = "";
            }

            renderFileCards();
            clearStatus(elements.modalStatus);
        }

        function buildModalMeta(records) {
            const quantity = records.reduce((total, row) => total + Number(row?.quantity || 0), 0);
            const totalSale = records.reduce((total, row) => total + Number(row?.totalSale || 0), 0);
            const clientName = getCommonValue(records, "clientName") || "Varios clientes";
            const orderNumber = getCommonValue(records, "purchaseOrderNumber");
            const parts = [
                clientName,
                `${formatNumber(quantity)} und`,
                formatCurrency(totalSale)
            ];
            if (orderNumber) {
                parts.push(`Orden ${orderNumber}`);
            }

            return parts.join(" · ");
        }

        function renderDocumentationRows() {
            if (!elements.documentationRows) {
                return;
            }

            elements.documentationRows.innerHTML = state.modalRecords.map(row => `
                <tr data-hw-documentation-row="${escapeHtml(row.recordId)}">
                    <td>
                        <div class="hardware-table__title">
                            <strong>${escapeHtml(row.name || "Hardware")}</strong>
                            <div class="hardware-table__submeta">${escapeHtml(row.clientName || "Sin cliente")}</div>
                        </div>
                    </td>
                    <td class="text-end">${formatNumber(row.quantity || 0)}</td>
                    <td class="text-end">${formatCurrency(row.saleUnit || 0)}</td>
                    ${isCommercialMode ? "" : `
                        <td>
                            <input type="date" class="form-control form-control-sm" data-hw-doc-field="odcDate" value="${escapeHtml(row.odcDateValue || "")}" />
                        </td>
                    `}
                    <td>
                        <input type="number" min="0" step="0.01" class="form-control form-control-sm" data-hw-doc-field="supplierUnitCost" value="${escapeHtml(formatInputNumber(row.supplierUnitCost || 0))}" />
                    </td>
                    <td>
                        <input type="text" class="form-control form-control-sm" data-hw-doc-field="provider" value="${escapeHtml(row.provider || "")}" />
                    </td>
                    <td>${renderDocumentationFileCell(row, "cr07a_ordendecompra")}</td>
                    <td>${renderDocumentationFileCell(row, "cr07a_adjuntarproforma")}</td>
                </tr>
            `).join("");

            renderDocumentationFileNames();
        }

        function renderDocumentationFileCell(row, fieldName) {
            const hasExisting = hasExistingFile(row, fieldName);
            const link = hasExisting
                ? `<a class="hardware-file-card__link" href="${escapeHtml(buildDownloadUrl(row.recordId, fieldName))}" target="_blank" rel="noopener">Descargar</a>`
                : `<span class="hardware-table__submeta">Sin archivo</span>`;

            return `
                <div class="hardware-doc-file">
                    ${link}
                    <span class="hardware-file-card__name" data-hw-doc-file-name="${escapeHtml(fieldName)}" data-hw-record-id="${escapeHtml(row.recordId)}">${escapeHtml(resolveExistingFileName(row, fieldName) || "Sin archivo")}</span>
                    <input type="file" class="form-control form-control-sm" data-hw-doc-file-input="${escapeHtml(fieldName)}" data-hw-record-id="${escapeHtml(row.recordId)}" />
                </div>
            `;
        }

        function renderDocumentationFileNames() {
            elements.form.querySelectorAll("[data-hw-doc-file-name]").forEach(target => {
                const recordId = target.dataset.hwRecordId || "";
                const fieldName = target.dataset.hwDocFileName || "";
                const record = state.modalRecords.find(row => row.recordId === recordId);
                const pending = state.pendingFiles[buildDocumentationFileKey(recordId, fieldName)];
                target.textContent = pending instanceof File
                    ? pending.name
                    : resolveExistingFileName(record, fieldName) || "Sin archivo";
            });
        }

        function closeModal(force = false) {
            if (state.saving && !force) {
                return;
            }

            state.modalRecords = [];
            state.pendingFiles = {};
            resetFileInputs();
            clearStatus(elements.modalStatus);
            elements.modal.hidden = true;
            document.body.classList.remove("hardware-modal-open");
        }

        function renderFileCards() {
            const fileFields = [
                "cr07a_ordendecompra",
                "cr07a_adjuntarproforma",
                "cr07a_pagoaproveedor",
                "cr07a_actadeentrega"
            ];

            fileFields.forEach(fieldName => {
                const fileNameTarget = elements.fileNames.find(item => item.dataset.hwFileName === fieldName);
                const fileHintTarget = elements.fileHints.find(item => item.dataset.hwFileHint === fieldName);
                const downloadLink = elements.downloadLinks.find(item => item.dataset.hwDownloadLink === fieldName);
                const pendingFile = state.pendingFiles[fieldName];
                const existingCount = state.modalRecords.filter(record => hasExistingFile(record, fieldName)).length;
                const total = state.modalRecords.length;

                if (fileNameTarget) {
                    if (pendingFile instanceof File) {
                        fileNameTarget.textContent = pendingFile.name;
                    } else if (total === 1) {
                        fileNameTarget.textContent = resolveExistingFileName(state.modalRecords[0], fieldName) || "Sin archivo";
                    } else {
                        fileNameTarget.textContent = existingCount > 0
                            ? `${formatNumber(existingCount)} de ${formatNumber(total)} con archivo`
                            : "Sin archivo";
                    }
                }

                if (fileHintTarget) {
                    fileHintTarget.textContent = pendingFile instanceof File
                        ? "El archivo se cargará en todas las filas seleccionadas antes de guardar."
                        : existingCount === total && total > 0
                            ? "Todas las filas seleccionadas ya tienen archivo registrado."
                            : (fileHintTarget.dataset.defaultHint || "");
                }

                if (downloadLink) {
                    const enabled = total === 1 && hasExistingFile(state.modalRecords[0], fieldName);
                    downloadLink.href = enabled
                        ? buildDownloadUrl(state.modalRecords[0].recordId, fieldName)
                        : "#";
                    downloadLink.classList.toggle("is-disabled", !enabled);
                }
            });
        }

        async function saveStage() {
            if (state.saving || !state.modalRecords.length) {
                return;
            }

            let payload;
            try {
                payload = buildStagePayload();
            } catch (error) {
                setStatus(elements.modalStatus, "error", getErrorMessage(error));
                return;
            }

            try {
                state.saving = true;
                setBusy(true);
                setStatus(elements.modalStatus, "info", "Cargando adjuntos de Hardware...");
                await uploadPendingFiles();

                setStatus(elements.modalStatus, "info", "Guardando etapa de Hardware...");
                const result = await fetchJson(config.saveUrl, {
                    method: "POST",
                    body: JSON.stringify(payload)
                });

                closeModal(true);
                setBusy(false);
                state.selectedRecordIds.clear();
                await loadBoard();
                setStatus(elements.status, "success", result?.message || "Etapa guardada correctamente.");
            } catch (error) {
                setStatus(elements.modalStatus, "error", getErrorMessage(error));
            } finally {
                state.saving = false;
                if (state.busy) {
                    setBusy(false);
                }
            }
        }

        function openBulkEditForSelectedRows() {
            if (!config.editUrl) {
                setStatus(elements.boardStatus, "warning", "No está configurada la edición de registros de Hardware.");
                return;
            }

            const selectedRecords = getSelectedRows();
            if (!selectedRecords.length) {
                setStatus(elements.boardStatus, "warning", "Selecciona al menos una fila de Hardware para editar.");
                return;
            }

            state.editRecords = selectedRecords.map(record => ({ ...record }));
            state.editDirtyFields = new Set();
            state.editClientSelection = null;
            state.editOwnerSelection = null;
            state.clientSuggestions = [];
            state.ownerSuggestions = [];
            closeLookupMenus();
            renderBulkEditModal();

            elements.editModal.hidden = false;
            document.body.classList.add("hardware-modal-open");
        }

        function renderBulkEditModal() {
            if (!elements.editForm || !state.editRecords.length) {
                return;
            }

            const records = state.editRecords;
            const ownerName = getCommonValue(records, "ownerName");
            const ownerId = getCommonValue(records, "ownerId");
            const clientName = getCommonValue(records, "clientName");
            const clientId = getCommonValue(records, "clientId");
            state.editOwnerSelection = ownerName && ownerId
                ? { id: ownerId, name: ownerName }
                : null;
            state.editClientSelection = clientName && clientId
                ? { id: clientId, name: clientName }
                : null;

            setText(elements.editCount, `${formatNumber(records.length)} fila(s) seleccionada(s)`);
            setEditFieldValue("ownerName", ownerName);
            setEditFieldValue("clientName", clientName);
            setEditFieldValue("quantity", getCommonNumberValue(records, "quantity"));
            setEditFieldValue("saleUnit", getCommonNumberValue(records, "saleUnit"));
            setEditFieldValue("totalSale", getCommonNumberValue(records, "totalSale"));
            renderBulkEditStateOptions(getCommonValue(records, "stateValue"));
            setEditFieldValue("purchaseOrderNumber", getCommonValue(records, "purchaseOrderNumber"));
            setEditFieldValue("odcDateValue", getCommonValue(records, "odcDateValue"));
            setEditFieldValue("supplierUnitCost", getCommonNumberValue(records, "supplierUnitCost"));
            setEditFieldValue("supplierTotal", getCommonNumberValue(records, "supplierTotal"));
            setEditFieldValue("freightValue", getCommonNumberValue(records, "freightValue"));
            setEditFieldValue("utility", getCommonNumberValue(records, "utility"));
            setEditFieldValue("marginValue", getCommonNumberValue(records, "marginValue"));
            setEditFieldValue("provider", getCommonValue(records, "provider"));
            setEditFieldValue("supplierPaymentDateValue", getCommonValue(records, "supplierPaymentDateValue"));
            setEditFieldValue("deliveryRecordDateValue", getCommonValue(records, "deliveryRecordDateValue"));
            setEditFieldValue("invoiceNumber", getCommonValue(records, "invoiceNumber"));

            if (elements.clientHint) {
                elements.clientHint.textContent = clientName
                    ? "El cliente actual se mantiene si no haces una nueva selección."
                    : "Busca y selecciona un cliente para cambiarlo.";
            }
            if (elements.ownerHint) {
                elements.ownerHint.textContent = ownerName
                    ? "El propietario actual se mantiene si no haces una nueva selección."
                    : "Busca y selecciona un usuario para cambiar el propietario.";
            }

            clearStatus(elements.editStatus);
            updateEditDirtyMeta();
        }

        function renderBulkEditStateOptions(selectedValue) {
            const target = elements.editFields.stateValue;
            if (!target) {
                return;
            }

            const options = Array.isArray(state.board?.stateOptions) ? state.board.stateOptions : [];
            target.innerHTML = `
                <option value="">Sin cambio</option>
                ${options.map(option => `
                    <option value="${escapeHtml(option.value)}">${escapeHtml(option.label || "")}</option>
                `).join("")}
            `;
            target.value = selectedValue ? String(selectedValue) : "";
        }

        function handleBulkEditInput(event) {
            const fieldName = getEditFieldName(event.target);
            if (!fieldName) {
                return;
            }

            markEditFieldDirty(fieldName);
            if (fieldName === "clientName") {
                state.editClientSelection = null;
                queueClientLookup((elements.editFields.clientName?.value || "").trim());
            } else if (fieldName === "ownerName") {
                state.editOwnerSelection = null;
                queueOwnerLookup((elements.editFields.ownerName?.value || "").trim());
            }
        }

        function handleBulkEditChange(event) {
            const fieldName = getEditFieldName(event.target);
            if (!fieldName) {
                return;
            }

            markEditFieldDirty(fieldName);
        }

        function markEditFieldDirty(fieldName) {
            state.editDirtyFields.add(fieldName);
            updateEditDirtyMeta();
        }

        function updateEditDirtyMeta() {
            const changedCount = state.editDirtyFields.size;
            setText(elements.editMeta, changedCount === 0
                ? "Sin cambios"
                : `${formatNumber(changedCount)} campo(s) modificado(s)`);

            if (elements.saveEditBtn) {
                elements.saveEditBtn.disabled = state.busy || changedCount === 0;
            }
        }

        function queueClientLookup(query) {
            window.clearTimeout(state.clientLookupTimer);

            if (!elements.clientOptions || !config.clientSearchUrl) {
                return;
            }

            if (query.length < 2) {
                state.clientSuggestions = [];
                closeClientLookupMenu();
                if (elements.clientHint) {
                    elements.clientHint.textContent = "Escribe al menos 2 caracteres para buscar el cliente.";
                }
                return;
            }

            if (elements.clientHint) {
                elements.clientHint.textContent = "Buscando cliente...";
            }

            const sequence = ++state.clientLookupSequence;
            state.clientLookupTimer = window.setTimeout(async () => {
                try {
                    const result = await fetchJson(buildClientSearchUrl(query), { method: "GET" });
                    if (sequence !== state.clientLookupSequence) {
                        return;
                    }

                    state.clientSuggestions = Array.isArray(result) ? result : [];
                    renderClientLookupOptions(state.clientSuggestions);
                    if (elements.clientHint) {
                        elements.clientHint.textContent = state.clientSuggestions.length > 0
                            ? "Selecciona una coincidencia para guardar el lookup."
                            : "No se encontraron clientes con esa búsqueda.";
                    }
                } catch (error) {
                    if (sequence !== state.clientLookupSequence) {
                        return;
                    }

                    state.clientSuggestions = [];
                    closeClientLookupMenu();
                    if (elements.clientHint) {
                        elements.clientHint.textContent = getErrorMessage(error);
                    }
                }
            }, 220);
        }

        function queueOwnerLookup(query) {
            window.clearTimeout(state.ownerLookupTimer);

            if (!elements.ownerOptions || !config.ownerSearchUrl) {
                return;
            }

            if (query.length < 2) {
                state.ownerSuggestions = [];
                closeOwnerLookupMenu();
                if (elements.ownerHint) {
                    elements.ownerHint.textContent = "Escribe al menos 2 caracteres para buscar el usuario.";
                }
                return;
            }

            if (elements.ownerHint) {
                elements.ownerHint.textContent = "Buscando usuario...";
            }

            const sequence = ++state.ownerLookupSequence;
            state.ownerLookupTimer = window.setTimeout(async () => {
                try {
                    const result = await fetchJson(buildOwnerSearchUrl(query), { method: "GET" });
                    if (sequence !== state.ownerLookupSequence) {
                        return;
                    }

                    state.ownerSuggestions = Array.isArray(result) ? result : [];
                    renderOwnerLookupOptions(state.ownerSuggestions);
                    if (elements.ownerHint) {
                        elements.ownerHint.textContent = state.ownerSuggestions.length > 0
                            ? "Selecciona una coincidencia para guardar el propietario."
                            : "No se encontraron usuarios con esa búsqueda.";
                    }
                } catch (error) {
                    if (sequence !== state.ownerLookupSequence) {
                        return;
                    }

                    state.ownerSuggestions = [];
                    closeOwnerLookupMenu();
                    if (elements.ownerHint) {
                        elements.ownerHint.textContent = getErrorMessage(error);
                    }
                }
            }, 220);
        }

        function renderClientLookupOptions(items) {
            if (!elements.clientOptions) {
                return;
            }

            if (!items.length) {
                elements.clientOptions.innerHTML = `<div class="hardware-lookup__empty">Sin coincidencias</div>`;
                elements.clientOptions.classList.add("is-open");
                return;
            }

            elements.clientOptions.innerHTML = items.map(item => `
                <button type="button"
                        class="hardware-lookup__option"
                        data-hw-client-option
                        data-client-id="${escapeHtml(item?.id || "")}"
                        data-client-name="${escapeHtml(item?.name || "")}">
                    <span>${escapeHtml(item?.name || "Cliente sin nombre")}</span>
                    <small>${escapeHtml(item?.id || "")}</small>
                </button>
            `).join("");
            elements.clientOptions.classList.add("is-open");
        }

        function renderOwnerLookupOptions(items) {
            if (!elements.ownerOptions) {
                return;
            }

            if (!items.length) {
                elements.ownerOptions.innerHTML = `<div class="hardware-lookup__empty">Sin coincidencias</div>`;
                elements.ownerOptions.classList.add("is-open");
                return;
            }

            elements.ownerOptions.innerHTML = items.map(item => `
                <button type="button"
                        class="hardware-lookup__option"
                        data-hw-owner-option
                        data-owner-id="${escapeHtml(item?.id || "")}"
                        data-owner-name="${escapeHtml(item?.name || "")}">
                    <span>${escapeHtml(item?.name || "Usuario sin nombre")}</span>
                    <small>${escapeHtml(item?.email || item?.id || "")}</small>
                </button>
            `).join("");
            elements.ownerOptions.classList.add("is-open");
        }

        function selectClientOption(option) {
            const clientId = option.dataset.clientId || "";
            const clientName = option.dataset.clientName || "";
            if (!clientId || !clientName) {
                return;
            }

            state.editClientSelection = { id: clientId, name: clientName };
            setEditFieldValue("clientName", clientName);
            markEditFieldDirty("clientName");
            closeClientLookupMenu();
            if (elements.clientHint) {
                elements.clientHint.textContent = "Cliente seleccionado para guardar.";
            }
        }

        function selectOwnerOption(option) {
            const ownerId = option.dataset.ownerId || "";
            const ownerName = option.dataset.ownerName || "";
            if (!ownerId || !ownerName) {
                return;
            }

            state.editOwnerSelection = { id: ownerId, name: ownerName };
            setEditFieldValue("ownerName", ownerName);
            markEditFieldDirty("ownerName");
            closeOwnerLookupMenu();
            if (elements.ownerHint) {
                elements.ownerHint.textContent = "Propietario seleccionado para guardar.";
            }
        }

        function closeClientLookupMenu() {
            if (!elements.clientOptions) {
                return;
            }

            elements.clientOptions.innerHTML = "";
            elements.clientOptions.classList.remove("is-open");
        }

        function closeOwnerLookupMenu() {
            if (!elements.ownerOptions) {
                return;
            }

            elements.ownerOptions.innerHTML = "";
            elements.ownerOptions.classList.remove("is-open");
        }

        function closeLookupMenus() {
            closeClientLookupMenu();
            closeOwnerLookupMenu();
        }

        function addCreateLine(values = {}) {
            if (!elements.createLines) {
                return;
            }

            const rowKey = values.rowKey || `line-${++state.createLineSequence}`;
            elements.createLines.insertAdjacentHTML("beforeend", `
                <tr data-hw-create-line="${escapeHtml(rowKey)}">
                    <td>
                        <input type="text" class="form-control form-control-sm" data-hw-create-line-field="name" value="${escapeHtml(values.name || "")}" />
                    </td>
                    <td>
                        <input type="number" min="1" step="1" class="form-control form-control-sm text-end" data-hw-create-line-field="quantity" value="${escapeHtml(values.quantity || "")}" />
                    </td>
                    <td>
                        <input type="number" min="0" step="0.01" class="form-control form-control-sm text-end" data-hw-create-line-field="supplierUnitCost" value="${escapeHtml(values.supplierUnitCost || "")}" />
                    </td>
                    <td>
                        <input type="number" min="0" step="0.01" class="form-control form-control-sm text-end" data-hw-create-line-field="saleUnit" value="${escapeHtml(values.saleUnit || "")}" />
                    </td>
                    <td>
                        <input type="text" class="form-control form-control-sm" data-hw-create-line-field="provider" value="${escapeHtml(values.provider || "")}" />
                    </td>
                    <td>
                        <input type="file" class="form-control form-control-sm" data-hw-create-line-file="cr07a_ordendecompra" />
                    </td>
                    <td>
                        <input type="file" class="form-control form-control-sm" data-hw-create-line-file="cr07a_adjuntarproforma" />
                    </td>
                    <td>
                        <button type="button" class="btn btn-sm btn-outline-secondary" data-hw-remove-create-line="${escapeHtml(rowKey)}">Quitar</button>
                    </td>
                </tr>
            `);
            syncCreateLineButtons();
        }

        function removeCreateLine(rowKey) {
            if (!elements.createLines) {
                return;
            }

            const row = elements.createLines.querySelector(`[data-hw-create-line="${cssEscape(rowKey)}"]`);
            row?.remove();
            if (!elements.createLines.children.length) {
                addCreateLine();
            }
            syncCreateLineButtons();
        }

        function syncCreateLineButtons() {
            if (!elements.createLines) {
                return;
            }

            const rows = Array.from(elements.createLines.querySelectorAll("[data-hw-create-line]"));
            rows.forEach(row => {
                const button = row.querySelector("[data-hw-remove-create-line]");
                if (button instanceof HTMLButtonElement) {
                    button.disabled = rows.length <= 1 || state.busy;
                }
            });
        }

        function handleCreateClientLookupInput() {
            const query = (elements.createFields.clientName?.value || "").trim();
            state.createClientSelection = null;
            queueCreateClientLookup(query);
        }

        function queueCreateClientLookup(query) {
            window.clearTimeout(state.createClientLookupTimer);

            if (!elements.createClientOptions || !config.clientSearchUrl) {
                return;
            }

            if (query.length < 2) {
                state.createClientSuggestions = [];
                closeCreateClientLookupMenu();
                if (elements.createClientHint) {
                    elements.createClientHint.textContent = "Escribe al menos 2 caracteres para buscar el cliente.";
                }
                return;
            }

            if (elements.createClientHint) {
                elements.createClientHint.textContent = "Buscando cliente...";
            }

            const sequence = ++state.createClientLookupSequence;
            state.createClientLookupTimer = window.setTimeout(async () => {
                try {
                    const result = await fetchJson(buildClientSearchUrl(query), { method: "GET" });
                    if (sequence !== state.createClientLookupSequence) {
                        return;
                    }

                    state.createClientSuggestions = Array.isArray(result) ? result : [];
                    renderCreateClientLookupOptions(state.createClientSuggestions);
                    if (elements.createClientHint) {
                        elements.createClientHint.textContent = state.createClientSuggestions.length > 0
                            ? "Selecciona una coincidencia para guardar el lookup."
                            : "No se encontraron clientes con esa búsqueda.";
                    }
                } catch (error) {
                    if (sequence !== state.createClientLookupSequence) {
                        return;
                    }

                    state.createClientSuggestions = [];
                    closeCreateClientLookupMenu();
                    if (elements.createClientHint) {
                        elements.createClientHint.textContent = getErrorMessage(error);
                    }
                }
            }, 220);
        }

        function renderCreateClientLookupOptions(items) {
            if (!elements.createClientOptions) {
                return;
            }

            if (!items.length) {
                elements.createClientOptions.innerHTML = `<div class="hardware-lookup__empty">Sin coincidencias</div>`;
                elements.createClientOptions.classList.add("is-open");
                return;
            }

            elements.createClientOptions.innerHTML = items.map(item => `
                <button type="button"
                        class="hardware-lookup__option"
                        data-hw-create-client-option
                        data-client-id="${escapeHtml(item?.id || "")}"
                        data-client-name="${escapeHtml(item?.name || "")}">
                    <span>${escapeHtml(item?.name || "Cliente sin nombre")}</span>
                    <small>${escapeHtml(item?.id || "")}</small>
                </button>
            `).join("");
            elements.createClientOptions.classList.add("is-open");
        }

        function selectCreateClientOption(option) {
            const clientId = option.dataset.clientId || "";
            const clientName = option.dataset.clientName || "";
            if (!clientId || !clientName) {
                return;
            }

            state.createClientSelection = { id: clientId, name: clientName };
            setFieldValue(elements.createFields.clientName, clientName);
            closeCreateClientLookupMenu();
            if (elements.createClientHint) {
                elements.createClientHint.textContent = "Cliente seleccionado.";
            }
        }

        function closeCreateClientLookupMenu() {
            if (!elements.createClientOptions) {
                return;
            }

            elements.createClientOptions.innerHTML = "";
            elements.createClientOptions.classList.remove("is-open");
        }

        async function createCommercialOrder() {
            if (state.saving || !elements.createForm || !config.createUrl) {
                return;
            }

            let draft;
            try {
                draft = buildCreateOrderDraft();
            } catch (error) {
                setStatus(elements.createStatus, "error", getErrorMessage(error));
                return;
            }

            try {
                state.saving = true;
                setBusy(true);
                setStatus(elements.createStatus, "info", "Creando registros de Hardware...");
                const createResult = await fetchJson(config.createUrl, {
                    method: "POST",
                    body: JSON.stringify(draft.payload)
                });

                const records = Array.isArray(createResult?.records) ? createResult.records : [];
                if (records.length !== draft.lines.length) {
                    throw new Error("La respuesta de creación no coincide con las filas enviadas.");
                }

                setStatus(elements.createStatus, "info", "Cargando adjuntos de la orden...");
                for (let index = 0; index < draft.lines.length; index++) {
                    const recordId = records[index]?.recordId || "";
                    if (!recordId) {
                        throw new Error(`No se recibió el id de la fila ${index + 1}.`);
                    }

                    await uploadFile(recordId, "cr07a_ordendecompra", draft.lines[index].orderFile);
                    await uploadFile(recordId, "cr07a_adjuntarproforma", draft.lines[index].proformaFile);
                }

                setStatus(elements.createStatus, "info", "Aplicando documentación de la orden...");
                const savePayload = {
                    recordId: records[0].recordId,
                    recordIds: records.map(record => record.recordId).filter(Boolean),
                    actionKey: "register-documentation",
                    purchaseOrderNumber: draft.payload.purchaseOrderNumber,
                    freightValue: 0,
                    documentationRows: records.map((record, index) => ({
                        recordId: record.recordId,
                        odcDateValue: draft.payload.odcDateValue,
                        supplierUnitCost: draft.lines[index].supplierUnitCost,
                        provider: draft.lines[index].provider
                    }))
                };

                const saveResult = await fetchJson(config.saveUrl, {
                    method: "POST",
                    body: JSON.stringify(savePayload)
                });

                resetCreateForm();
                await loadBoard();
                setStatus(elements.createStatus, "success", saveResult?.message || createResult?.message || "Orden de Hardware guardada.");
            } catch (error) {
                setStatus(elements.createStatus, "error", getErrorMessage(error));
            } finally {
                state.saving = false;
                if (state.busy) {
                    setBusy(false);
                }
            }
        }

        function buildCreateOrderDraft() {
            const purchaseOrderNumber = (elements.createFields.purchaseOrderNumber?.value || "").trim();
            const odcDateValue = (elements.createFields.odcDate?.value || "").trim();
            const typedClient = (elements.createFields.clientName?.value || "").trim();

            if (!purchaseOrderNumber) {
                throw new Error("Debes diligenciar cr07a_noorden.");
            }
            if (!odcDateValue) {
                throw new Error("Debes diligenciar cr07a_fechaodc.");
            }
            if (!state.createClientSelection?.id
                || normalizeText(state.createClientSelection.name) !== normalizeText(typedClient)) {
                throw new Error("Selecciona un cliente válido desde el buscador.");
            }

            const rows = Array.from(elements.createLines?.querySelectorAll("[data-hw-create-line]") || []);
            if (!rows.length) {
                throw new Error("Agrega al menos una fila.");
            }

            const lines = rows.map((row, index) => {
                const name = getCreateLineValue(row, "name");
                const quantity = parseIntegerStrict(getCreateLineValue(row, "quantity"));
                const supplierUnitCost = parseDecimal(getCreateLineValue(row, "supplierUnitCost"));
                const saleUnit = parseDecimal(getCreateLineValue(row, "saleUnit"));
                const provider = getCreateLineValue(row, "provider");
                const orderFile = getCreateLineFile(row, "cr07a_ordendecompra");
                const proformaFile = getCreateLineFile(row, "cr07a_adjuntarproforma");

                if (!name) {
                    throw new Error(`Debes diligenciar cr07a_name en la fila ${index + 1}.`);
                }
                if (!Number.isInteger(quantity) || quantity <= 0) {
                    throw new Error(`Debes diligenciar cr07a_cant válido en la fila ${index + 1}.`);
                }
                if (!(supplierUnitCost > 0)) {
                    throw new Error(`Debes diligenciar cr07a_costountproveedor válido en la fila ${index + 1}.`);
                }
                if (!(saleUnit > 0)) {
                    throw new Error(`Debes diligenciar cr07a_ventaunidad válido en la fila ${index + 1}.`);
                }
                if (!provider) {
                    throw new Error(`Debes diligenciar cr07a_proveedor en la fila ${index + 1}.`);
                }
                if (!(orderFile instanceof File)) {
                    throw new Error(`Debes adjuntar cr07a_ordendecompra en la fila ${index + 1}.`);
                }
                if (!(proformaFile instanceof File)) {
                    throw new Error(`Debes adjuntar cr07a_adjuntarproforma en la fila ${index + 1}.`);
                }

                return {
                    rowKey: row.dataset.hwCreateLine || `line-${index + 1}`,
                    name,
                    quantity,
                    supplierUnitCost,
                    saleUnit,
                    provider,
                    orderFile,
                    proformaFile
                };
            });

            return {
                payload: {
                    purchaseOrderNumber,
                    odcDateValue,
                    clientId: state.createClientSelection.id,
                    clientName: state.createClientSelection.name,
                    lines: lines.map(line => ({
                        rowKey: line.rowKey,
                        name: line.name,
                        quantity: line.quantity,
                        supplierUnitCost: line.supplierUnitCost,
                        saleUnit: line.saleUnit,
                        provider: line.provider
                    }))
                },
                lines
            };
        }

        function resetCreateForm() {
            elements.createForm?.reset();
            state.createClientSelection = null;
            state.createClientSuggestions = [];
            closeCreateClientLookupMenu();
            if (elements.createLines) {
                elements.createLines.innerHTML = "";
                addCreateLine();
            }
            if (elements.createClientHint) {
                elements.createClientHint.textContent = "Busca y selecciona un cliente.";
            }
        }

        function getCreateLineValue(row, fieldName) {
            return (row.querySelector(`[data-hw-create-line-field="${cssEscape(fieldName)}"]`)?.value || "").trim();
        }

        function getCreateLineFile(row, fieldName) {
            const input = row.querySelector(`[data-hw-create-line-file="${cssEscape(fieldName)}"]`);
            return input instanceof HTMLInputElement && input.files && input.files.length > 0
                ? input.files[0]
                : null;
        }

        async function saveBulkEdit() {
            if (state.saving || !state.editRecords.length) {
                return;
            }

            let payload;
            try {
                payload = buildBulkEditPayload();
            } catch (error) {
                setStatus(elements.editStatus, "error", getErrorMessage(error));
                return;
            }

            try {
                state.saving = true;
                setBusy(true);
                setStatus(elements.editStatus, "info", "Guardando cambios de Hardware...");
                const result = await fetchJson(config.editUrl, {
                    method: "POST",
                    body: JSON.stringify(payload)
                });

                closeEditModal(true);
                state.selectedRecordIds.clear();
                await loadBoard();
                setStatus(elements.status, "success", result?.message || "Registros de Hardware actualizados.");
            } catch (error) {
                setStatus(elements.editStatus, "error", getErrorMessage(error));
            } finally {
                state.saving = false;
                if (state.busy) {
                    setBusy(false);
                }
            }
        }

        function buildBulkEditPayload() {
            const dirty = state.editDirtyFields;
            if (!dirty.size) {
                throw new Error("Modifica al menos un campo antes de guardar.");
            }

            const payload = {
                recordIds: state.editRecords.map(record => record.recordId).filter(Boolean)
            };

            if (!payload.recordIds.length) {
                throw new Error("Selecciona al menos una fila de Hardware para editar.");
            }

            if (dirty.has("ownerName")) {
                const typedOwner = (elements.editFields.ownerName?.value || "").trim();
                if (!state.editOwnerSelection?.id
                    || normalizeText(state.editOwnerSelection.name) !== normalizeText(typedOwner)) {
                    throw new Error("Selecciona un propietario válido desde la lista de usuarios.");
                }

                payload.ownerChanged = true;
                payload.ownerId = state.editOwnerSelection.id;
                payload.ownerName = state.editOwnerSelection.name;
            }

            if (dirty.has("clientName")) {
                const typedClient = (elements.editFields.clientName?.value || "").trim();
                if (!state.editClientSelection?.id
                    || normalizeText(state.editClientSelection.name) !== normalizeText(typedClient)) {
                    throw new Error("Selecciona un cliente válido desde la lista de resultados.");
                }

                payload.clientChanged = true;
                payload.clientId = state.editClientSelection.id;
                payload.clientName = state.editClientSelection.name;
            }

            if (dirty.has("quantity")) {
                payload.quantityChanged = true;
                payload.quantity = parseOptionalIntegerInput("quantity", "Cantidad");
            }
            if (dirty.has("saleUnit")) {
                payload.saleUnitChanged = true;
                payload.saleUnit = parseOptionalDecimalInput("saleUnit", "Venta unidad");
            }
            if (dirty.has("totalSale")) {
                payload.totalSaleChanged = true;
                payload.totalSale = parseOptionalDecimalInput("totalSale", "Total línea");
            }
            if (dirty.has("stateValue")) {
                const rawState = getEditFieldValue("stateValue");
                const stateValue = rawState ? Number.parseInt(rawState, 10) : Number.NaN;
                if (!Number.isInteger(stateValue)) {
                    throw new Error("Selecciona un estado válido.");
                }
                payload.stateChanged = true;
                payload.stateValue = stateValue;
            }
            if (dirty.has("purchaseOrderNumber")) {
                payload.purchaseOrderNumberChanged = true;
                payload.purchaseOrderNumber = getEditFieldValue("purchaseOrderNumber");
            }
            if (dirty.has("odcDateValue")) {
                payload.odcDateChanged = true;
                payload.odcDateValue = getEditFieldValue("odcDateValue");
            }
            if (dirty.has("supplierUnitCost")) {
                payload.supplierUnitCostChanged = true;
                payload.supplierUnitCost = parseOptionalDecimalInput("supplierUnitCost", "Costo unt proveedor");
            }
            if (dirty.has("supplierTotal")) {
                payload.supplierTotalChanged = true;
                payload.supplierTotal = parseOptionalDecimalInput("supplierTotal", "Total proveedor");
            }
            if (dirty.has("freightValue")) {
                payload.freightValueChanged = true;
                payload.freightValue = parseOptionalDecimalInput("freightValue", "Valor flete");
            }
            if (dirty.has("utility")) {
                payload.utilityChanged = true;
                payload.utility = parseOptionalDecimalInput("utility", "Utilidad");
            }
            if (dirty.has("marginValue")) {
                payload.marginValueChanged = true;
                payload.marginValue = parseOptionalDecimalInput("marginValue", "Valor margen");
            }
            if (dirty.has("provider")) {
                payload.providerChanged = true;
                payload.provider = getEditFieldValue("provider");
            }
            if (dirty.has("supplierPaymentDateValue")) {
                payload.supplierPaymentDateChanged = true;
                payload.supplierPaymentDateValue = getEditFieldValue("supplierPaymentDateValue");
            }
            if (dirty.has("deliveryRecordDateValue")) {
                payload.deliveryRecordDateChanged = true;
                payload.deliveryRecordDateValue = getEditFieldValue("deliveryRecordDateValue");
            }
            if (dirty.has("invoiceNumber")) {
                payload.invoiceNumberChanged = true;
                payload.invoiceNumber = getEditFieldValue("invoiceNumber");
            }

            return payload;
        }

        function closeEditModal(force = false) {
            if (state.saving && !force) {
                return;
            }

            window.clearTimeout(state.clientLookupTimer);
            window.clearTimeout(state.ownerLookupTimer);
            state.editRecords = [];
            state.editDirtyFields = new Set();
            state.editClientSelection = null;
            state.editOwnerSelection = null;
            state.clientSuggestions = [];
            state.ownerSuggestions = [];
            closeLookupMenus();
            clearStatus(elements.editStatus);

            if (elements.editModal) {
                elements.editModal.hidden = true;
            }

            if (!elements.modal || elements.modal.hidden) {
                document.body.classList.remove("hardware-modal-open");
            }
        }

        async function uploadPendingFiles() {
            const entries = Object.entries(state.pendingFiles)
                .filter(([, file]) => file instanceof File);

            for (const [key, file] of entries) {
                if (key.includes("|")) {
                    const [recordId, fieldName] = key.split("|");
                    await uploadFile(recordId, fieldName, file);
                    continue;
                }

                for (const record of state.modalRecords) {
                    await uploadFile(record.recordId, key, file);
                }
            }
        }

        async function uploadFile(recordId, fieldName, file) {
            const formData = new FormData();
            formData.append("recordId", recordId || "");
            formData.append("fieldName", fieldName || "");
            formData.append("file", file);

            await fetchJson(config.uploadUrl, {
                method: "POST",
                body: formData
            });
        }

        function buildStagePayload() {
            if (!state.modalRecords.length) {
                throw new Error("No hay registros de Hardware activos.");
            }

            const actionKey = elements.actionKey.value || "";
            const recordIds = state.modalRecords.map(record => record.recordId);
            const supplierPaymentDate = (elements.fields.supplierPaymentDate?.value || "").trim();
            const deliveryRecordDate = (elements.fields.deliveryRecordDate?.value || "").trim();
            const invoiceNumber = (elements.fields.invoiceNumber?.value || "").trim();

            const requiredFiles = stageConfig[actionKey]?.requiredFiles || [];
            if (actionKey !== "register-documentation") {
                requiredFiles.forEach(fieldName => {
                    if (!hasGlobalFileOrPending(fieldName)) {
                        throw new Error(`Debes cargar el archivo requerido para ${resolveFileLabel(fieldName)} antes de guardar.`);
                    }
                });
            }

            switch (actionKey) {
                case "register-documentation":
                    return buildDocumentationPayload(recordIds, actionKey);

                case "register-supplier-payment":
                    if (!supplierPaymentDate) {
                        throw new Error("Debes diligenciar la Fecha de pago a proveedor.");
                    }
                    break;

                case "register-client-received":
                    if (!deliveryRecordDate) {
                        throw new Error("Debes diligenciar la Fecha acta de entrega.");
                    }
                    break;

                case "register-invoice":
                    if (!invoiceNumber) {
                        throw new Error("Debes seleccionar un número de factura.");
                    }
                    break;

                default:
                    break;
            }

            return {
                recordId: recordIds[0],
                recordIds,
                actionKey,
                supplierPaymentDateValue: supplierPaymentDate,
                deliveryRecordDateValue: deliveryRecordDate,
                invoiceNumber
            };
        }

        function buildDocumentationPayload(recordIds, actionKey) {
            const purchaseOrderNumber = (elements.fields.purchaseOrderNumber?.value || "").trim();
            const freightValueRaw = (elements.fields.freightValue?.value || "").trim();
            const freightValue = parseDecimal(freightValueRaw);
            const commonOdcDate = (elements.fields.odcDate?.value || "").trim();
            if (!purchaseOrderNumber) {
                throw new Error("Debes diligenciar cr07a_noorden.");
            }
            if (freightValueRaw && freightValue < 0) {
                throw new Error("Debes diligenciar un cr07a_valorflete válido.");
            }
            if (isCommercialMode && !commonOdcDate) {
                throw new Error("Debes diligenciar cr07a_fechaodc.");
            }

            const documentationRows = Array.from(elements.documentationRows?.querySelectorAll("[data-hw-documentation-row]") || [])
                .map(rowElement => {
                    const recordId = rowElement.dataset.hwDocumentationRow || "";
                    const record = state.modalRecords.find(item => item.recordId === recordId);
                    const odcDate = isCommercialMode
                        ? commonOdcDate
                        : (rowElement.querySelector('[data-hw-doc-field="odcDate"]')?.value || "").trim();
                    const supplierUnitCost = parseDecimal(rowElement.querySelector('[data-hw-doc-field="supplierUnitCost"]')?.value || "");
                    const provider = (rowElement.querySelector('[data-hw-doc-field="provider"]')?.value || "").trim();

                    if (!odcDate) {
                        throw new Error(`Debes diligenciar Fecha ODC para ${record?.name || "la fila seleccionada"}.`);
                    }
                    if (!(supplierUnitCost > 0)) {
                        throw new Error(`Debes diligenciar un Costo Unt Proveedor antes de IVA válido para ${record?.name || "la fila seleccionada"}.`);
                    }
                    if (!provider) {
                        throw new Error(`Debes diligenciar Proveedor para ${record?.name || "la fila seleccionada"}.`);
                    }
                    if (!hasDocumentationFileOrPending(record, "cr07a_ordendecompra")) {
                        throw new Error(`Debes cargar Adjuntar ODC para ${record?.name || "la fila seleccionada"}.`);
                    }
                    if (!hasDocumentationFileOrPending(record, "cr07a_adjuntarproforma")) {
                        throw new Error(`Debes cargar Adjuntar Proforma para ${record?.name || "la fila seleccionada"}.`);
                    }

                    return {
                        recordId,
                        odcDateValue: odcDate,
                        supplierUnitCost,
                        provider
                    };
                });

            return {
                recordId: recordIds[0],
                recordIds,
                actionKey,
                purchaseOrderNumber,
                freightValue: freightValueRaw ? freightValue : 0,
                documentationRows
            };
        }

        function hasGlobalFileOrPending(fieldName) {
            if (state.pendingFiles[fieldName] instanceof File) {
                return true;
            }

            return state.modalRecords.length > 0
                && state.modalRecords.every(record => hasExistingFile(record, fieldName));
        }

        function hasDocumentationFileOrPending(record, fieldName) {
            if (!record) {
                return false;
            }

            return state.pendingFiles[buildDocumentationFileKey(record.recordId, fieldName)] instanceof File
                || hasExistingFile(record, fieldName);
        }

        function hasExistingFile(record, fieldName) {
            if (!record) {
                return false;
            }

            switch (fieldName) {
                case "cr07a_ordendecompra":
                    return Boolean(record.hasOrderPurchase);
                case "cr07a_adjuntarproforma":
                    return Boolean(record.hasProforma);
                case "cr07a_pagoaproveedor":
                    return Boolean(record.hasSupplierPaymentProof);
                case "cr07a_actadeentrega":
                    return Boolean(record.hasDeliveryRecord);
                default:
                    return false;
            }
        }

        function resolveExistingFileName(record, fieldName) {
            if (!record) {
                return "";
            }

            switch (fieldName) {
                case "cr07a_ordendecompra":
                    return record.orderPurchaseFileName || "";
                case "cr07a_adjuntarproforma":
                    return record.proformaFileName || "";
                case "cr07a_pagoaproveedor":
                    return record.supplierPaymentProofFileName || "";
                case "cr07a_actadeentrega":
                    return record.deliveryRecordFileName || "";
                default:
                    return "";
            }
        }

        function resolveFileLabel(fieldName) {
            switch (fieldName) {
                case "cr07a_ordendecompra":
                    return "Adjuntar ODC";
                case "cr07a_adjuntarproforma":
                    return "Adjuntar Proforma";
                case "cr07a_pagoaproveedor":
                    return "Adjuntar pago a proveedor";
                case "cr07a_actadeentrega":
                    return "Adjuntar acta de entrega";
                default:
                    return "archivo";
            }
        }

        function handleInvoiceLookupInput() {
            const query = (elements.fields.invoiceNumber?.value || "").trim();
            window.clearTimeout(state.invoiceLookupTimer);

            if (query.length < 2) {
                state.invoiceSuggestions = [];
                elements.invoiceOptions.innerHTML = "";
                return;
            }

            const sequence = ++state.invoiceLookupSequence;
            state.invoiceLookupTimer = window.setTimeout(async () => {
                try {
                    const result = await fetchJson(buildInvoiceSearchUrl(query), { method: "GET" });
                    if (sequence !== state.invoiceLookupSequence) {
                        return;
                    }

                    state.invoiceSuggestions = Array.isArray(result) ? result : [];
                    elements.invoiceOptions.innerHTML = state.invoiceSuggestions.map(item => `
                        <option value="${escapeHtml(item.number || "")}" label="${escapeHtml(buildInvoiceOptionLabel(item))}"></option>
                    `).join("");
                } catch {
                    if (sequence !== state.invoiceLookupSequence) {
                        return;
                    }

                    state.invoiceSuggestions = [];
                    elements.invoiceOptions.innerHTML = "";
                }
            }, 220);
        }

        function syncInvoiceSelection() {
            const inputValue = normalizeText(elements.fields.invoiceNumber?.value || "");
            const exactMatch = state.invoiceSuggestions.find(item => normalizeText(item.number || "") === inputValue);
            if (exactMatch && elements.fields.invoiceNumber) {
                elements.fields.invoiceNumber.value = exactMatch.number || "";
            }
        }

        function buildInvoiceOptionLabel(item) {
            const parts = [];
            if (item?.clientName) {
                parts.push(item.clientName);
            }
            if (Number(item?.paymentValue || 0) > 0) {
                parts.push(`Pago ${formatCurrency(item.paymentValue || 0)}`);
            }
            return parts.join(" · ");
        }

        function buildBoardUrl() {
            const url = new URL(config.boardUrl, window.location.origin);
            const stateValue = elements.stateFilter.value || "";
            const startDate = (elements.startDate?.value || "").trim();
            const endDate = (elements.endDate?.value || "").trim();
            if (stateValue) {
                url.searchParams.set("stateValue", stateValue);
            }
            if (startDate) {
                url.searchParams.set("startDate", startDate);
            }
            if (endDate) {
                url.searchParams.set("endDate", endDate);
            }

            return `${url.pathname}${url.search}`;
        }

        function buildClientSearchUrl(query) {
            const url = new URL(config.clientSearchUrl, window.location.origin);
            url.searchParams.set("q", query);
            return `${url.pathname}${url.search}`;
        }

        function buildOwnerSearchUrl(query) {
            const url = new URL(config.ownerSearchUrl, window.location.origin);
            url.searchParams.set("q", query);
            return `${url.pathname}${url.search}`;
        }

        function buildInvoiceSearchUrl(query) {
            const url = new URL(config.invoiceSearchUrl, window.location.origin);
            url.searchParams.set("q", query);
            return `${url.pathname}${url.search}`;
        }

        function buildDownloadUrl(recordId, fieldName) {
            const url = new URL(config.downloadUrl, window.location.origin);
            url.searchParams.set("recordId", recordId);
            url.searchParams.set("fieldName", fieldName);
            return `${url.pathname}${url.search}`;
        }

        function buildGroupKey(row) {
            return `${normalizeText(row?.ownerId || row?.ownerName || "sin-owner")}|${Number(row?.stateValue || 0)}|${normalizeText(row?.purchaseOrderNumber || "")}`;
        }

        function buildDocumentationFileKey(recordId, fieldName) {
            return `${recordId}|${fieldName}`;
        }

        function findDisplayGroup(key) {
            return state.displayItems.find(item => item.type === "group" && item.key === key) || null;
        }

        function findRow(recordId) {
            return state.rows.find(row => row.recordId === recordId) || null;
        }

        function getSelectedRows() {
            return state.rows.filter(row => state.selectedRecordIds.has(row.recordId));
        }

        function trimSelectionToVisibleRows() {
            const visibleIds = new Set(state.rows.map(row => row.recordId));
            Array.from(state.selectedRecordIds).forEach(recordId => {
                if (!visibleIds.has(recordId)) {
                    state.selectedRecordIds.delete(recordId);
                }
            });
        }

        function syncGroupCheckboxStates() {
            elements.rows.querySelectorAll("[data-hw-select-group]").forEach(input => {
                if (!(input instanceof HTMLInputElement)) {
                    return;
                }

                const group = findDisplayGroup(input.dataset.hwSelectGroup || "");
                if (!group) {
                    return;
                }

                const selectedCount = group.rows.filter(row => state.selectedRecordIds.has(row.recordId)).length;
                input.indeterminate = selectedCount > 0 && selectedCount < group.rows.length;
                input.checked = selectedCount === group.rows.length;
            });
        }

        function syncSelectAllState() {
            if (!elements.selectAll) {
                return;
            }

            const selectedVisible = state.rows.filter(row => state.selectedRecordIds.has(row.recordId)).length;
            elements.selectAll.indeterminate = selectedVisible > 0 && selectedVisible < state.rows.length;
            elements.selectAll.checked = state.rows.length > 0 && selectedVisible === state.rows.length;
        }

        function getCommonValue(rows, property) {
            const values = rows
                .map(row => String(row?.[property] || "").trim())
                .filter(Boolean);
            if (!values.length) {
                return "";
            }

            const first = values[0];
            return values.every(value => value === first) ? first : "";
        }

        function getCommonNumberValue(rows, property) {
            const value = getCommonValue(rows, property);
            if (value === "") {
                return "";
            }

            const numeric = Number(value);
            return Number.isFinite(numeric) ? String(numeric) : value;
        }

        function sumValues(rows, property) {
            return rows.reduce((total, row) => total + Number(row?.[property] || 0), 0);
        }

        function setBusy(isBusy) {
            state.busy = isBusy;
            [
                elements.csvFile,
                elements.analyzeCsvBtn,
                elements.provisionCsvBtn,
                elements.stateFilter,
                elements.startDate,
                elements.endDate,
                elements.refreshBtn,
                elements.selectAll,
                elements.selectedActionBtn,
                elements.editSelectedBtn,
                elements.addCreateLineBtn,
                elements.saveCreateBtn
            ].forEach(element => {
                if (element) {
                    element.disabled = isBusy;
                }
            });

            elements.form.querySelectorAll("input, select, textarea, button").forEach(element => {
                element.disabled = isBusy;
            });

            elements.closeModalButtons.forEach(button => {
                button.disabled = isBusy;
            });

            elements.editForm?.querySelectorAll("input, select, textarea, button").forEach(element => {
                element.disabled = isBusy;
            });

            elements.createForm?.querySelectorAll("input, select, textarea, button").forEach(element => {
                element.disabled = isBusy;
            });

            elements.closeEditModalButtons.forEach(button => {
                button.disabled = isBusy;
            });

            if (elements.provisionCsvBtn && !isBusy) {
                const previewColumns = Array.isArray(state.preview?.columns) ? state.preview.columns : [];
                elements.provisionCsvBtn.disabled = previewColumns.length === 0 || Number(state.preview?.totalRows || 0) === 0;
            }

            renderSelectionState();
            updateEditDirtyMeta();
            syncCreateLineButtons();
        }

        async function fetchJson(url, options = {}) {
            const isFormData = options.body instanceof FormData;
            const headers = {
                Accept: "application/json",
                ...(options.headers || {})
            };

            if (!isFormData && options.body && !headers["Content-Type"]) {
                headers["Content-Type"] = "application/json";
            }

            const response = await fetch(url, {
                method: options.method || "GET",
                headers: isFormData ? { Accept: headers.Accept } : headers,
                body: options.body
            });

            const contentType = response.headers.get("content-type") || "";
            if (!response.ok) {
                const rawBody = await response.text();
                let message = rawBody;

                if (contentType.includes("application/json")) {
                    try {
                        const payload = rawBody ? JSON.parse(rawBody) : null;
                        message = typeof payload === "string"
                            ? payload
                            : payload?.message || payload?.detail || payload?.title || rawBody;
                    } catch {
                        message = rawBody;
                    }
                }

                throw new Error(message || "No fue posible completar la solicitud.");
            }

            if (!contentType.includes("application/json")) {
                const message = await response.text();
                throw new Error(message || "La respuesta del servidor no fue válida.");
            }

            return response.json();
        }

        function setStatus(target, type, message) {
            if (!target) {
                return;
            }

            if (!message) {
                clearStatus(target);
                return;
            }

            const baseClass = target.dataset.baseClass || target.className || "hardware-status";
            target.className = `${baseClass} is-visible is-${type}`;
            target.textContent = message;
        }

        function clearStatus(target) {
            if (!target) {
                return;
            }

            target.className = target.dataset.baseClass || "hardware-status";
            target.textContent = "";
        }

        function getErrorMessage(error) {
            return error instanceof Error ? error.message : "Ocurrió un error inesperado.";
        }

        function setText(element, value) {
            if (element) {
                element.textContent = value ?? "";
            }
        }

        function setFieldValue(element, value) {
            if (element) {
                element.value = value ?? "";
            }
        }

        function setEditFieldValue(fieldName, value) {
            setFieldValue(elements.editFields?.[fieldName], value);
        }

        function getEditFieldValue(fieldName) {
            return (elements.editFields?.[fieldName]?.value || "").trim();
        }

        function getEditFieldName(target) {
            if (!(target instanceof HTMLElement)) {
                return "";
            }

            const field = target.closest("[data-hw-edit-field]");
            return field instanceof HTMLElement ? field.dataset.hwEditField || "" : "";
        }

        function parseOptionalIntegerInput(fieldName, label) {
            const raw = getEditFieldValue(fieldName);
            if (!raw) {
                return null;
            }

            const parsed = Number.parseInt(raw, 10);
            if (!Number.isInteger(parsed) || String(parsed) !== String(Number(raw))) {
                throw new Error(`${label} debe ser un número entero válido.`);
            }

            return parsed;
        }

        function parseOptionalDecimalInput(fieldName, label) {
            const raw = getEditFieldValue(fieldName);
            if (!raw) {
                return null;
            }

            const parsed = parseDecimal(raw);
            if (!Number.isFinite(parsed)) {
                throw new Error(`${label} debe ser un número válido.`);
            }

            return parsed;
        }

        function buildActiveFilterLabel(board) {
            const stateLabel = elements.stateFilter.options[elements.stateFilter.selectedIndex]?.text || "Todos los estados";
            const dateLabel = board?.dateFilterLabel || "";
            return dateLabel ? `${stateLabel} · ${dateLabel}` : stateLabel;
        }

        function resetFileInputs() {
            elements.form.querySelectorAll('input[type="file"]').forEach(input => {
                input.value = "";
            });
        }

        function toneClass(tone) {
            return tone ? `is-${escapeHtml(tone)}` : "";
        }

        function renderPill(label, tone) {
            return `<span class="hardware-pill ${toneClass(tone)}">${escapeHtml(label || "-")}</span>`;
        }

        function renderStatePill(label, tone) {
            const lines = getStateLabelLines(label);
            return `<span class="hardware-pill hardware-pill--state ${toneClass(tone)}">${lines.map(line => `<span>${escapeHtml(line)}</span>`).join("")}</span>`;
        }

        function getStateLabelLines(label) {
            const text = String(label || "-").trim();
            switch (normalizeText(text)) {
                case "en espera de documentacion":
                    return ["En espera de", "documentación"];
                case "ok para pago a proveedor":
                    return ["Ok para pago", "a proveedor"];
                case "pagada a proveedor":
                    return ["Pagada", "a proveedor"];
                case "en transito a oficina o cliente":
                    return ["En tránsito", "a oficina o cliente"];
                case "entregado en espera de facturacion":
                    return ["Entregado", "en espera de facturación"];
                case "facturado en espera de pago":
                    return ["Facturado", "en espera de pago"];
                default:
                    return [text || "-"];
            }
        }

        function formatNumber(value) {
            return new Intl.NumberFormat("es-CO").format(Number(value || 0));
        }

        function formatCurrency(value) {
            return new Intl.NumberFormat("es-CO", {
                style: "currency",
                currency: "COP",
                minimumFractionDigits: 0,
                maximumFractionDigits: 0
            }).format(Number(value || 0));
        }

        function parseDecimal(value) {
            let normalized = String(value || "").trim().replace(/\s/g, "");
            if (!normalized) {
                return 0;
            }

            const hasComma = normalized.includes(",");
            const hasDot = normalized.includes(".");
            if (hasComma && hasDot) {
                normalized = normalized.replaceAll(".", "").replace(",", ".");
            } else if (hasComma) {
                normalized = normalized.replace(",", ".");
            } else if (/^\d{1,3}(\.\d{3})+$/.test(normalized)) {
                normalized = normalized.replaceAll(".", "");
            }

            const parsed = Number.parseFloat(normalized);
            return Number.isFinite(parsed) ? parsed : 0;
        }

        function parseIntegerStrict(value) {
            const normalized = String(value || "").trim();
            if (!/^\d+$/.test(normalized)) {
                return Number.NaN;
            }

            return Number.parseInt(normalized, 10);
        }

        function formatInputNumber(value) {
            return Number(value || 0) > 0 ? Number(value).toFixed(2) : "";
        }

        function normalizeText(value) {
            return String(value || "")
                .normalize("NFD")
                .replace(/[\u0300-\u036f]/g, "")
                .toLowerCase()
                .trim();
        }

        function escapeHtml(value) {
            return String(value ?? "")
                .replaceAll("&", "&amp;")
                .replaceAll("<", "&lt;")
                .replaceAll(">", "&gt;")
                .replaceAll('"', "&quot;")
                .replaceAll("'", "&#39;");
        }

        function cssEscape(value) {
            if (window.CSS && typeof window.CSS.escape === "function") {
                return window.CSS.escape(String(value || ""));
            }

            return String(value || "").replaceAll('"', '\\"').replaceAll("\\", "\\\\");
        }
    }
})();
