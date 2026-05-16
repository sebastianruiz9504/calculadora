(function () {
    const roots = Array.from(document.querySelectorAll("[data-hw-root]"));
    roots.forEach(initHardwareWorkspace);

    function initHardwareWorkspace(root) {
        const config = {
            mode: root.dataset.hwMode || "dashboard",
            canImpersonate: root.dataset.hwCanImpersonate === "true",
            currentUserId: root.dataset.hwCurrentUserId || "",
            currentUserEmail: root.dataset.hwCurrentUserEmail || "",
            allowCreate: root.dataset.hwAllowCreate !== "false",
            allowCommercialDraftEdit: root.dataset.hwAllowCommercialDraftEdit !== "false",
            supplierPaymentEmail: root.dataset.hwSupplierPaymentEmail || "",
            previewUrl: root.dataset.previewUrl || "",
            provisionUrl: root.dataset.provisionUrl || "",
            boardUrl: root.dataset.boardUrl || "",
            createUrl: root.dataset.createUrl || "",
            purchaseOrderUrl: root.dataset.purchaseOrderUrl || "",
            saveUrl: root.dataset.saveUrl || "",
            editUrl: root.dataset.editUrl || "",
            uploadUrl: root.dataset.uploadUrl || "",
            downloadUrl: root.dataset.downloadUrl || "",
            invoiceSearchUrl: root.dataset.invoiceSearchUrl || "",
            clientSearchUrl: root.dataset.clientSearchUrl || "",
            ownerSearchUrl: root.dataset.ownerSearchUrl || "",
            impersonationUsersUrl: root.dataset.impersonationUsersUrl || "",
            initialStartDate: root.dataset.initialStartDate || "",
            initialEndDate: root.dataset.initialEndDate || ""
        };
        const isCommercialMode = normalizeText(config.mode) === "commercial";
        const hardwareStateOkForSupplierPayment = 645250001;

        const elements = {
            status: root.querySelector("[data-hw-status]"),
            activeUserLabel: root.querySelector("[data-hw-active-user-label]"),
            impersonationSelect: root.querySelector("[data-hw-impersonation-select]"),
            impersonationReset: root.querySelector("[data-hw-impersonation-reset]"),
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
            createModal: root.querySelector("[data-hw-create-modal]"),
            createStatus: root.querySelector("[data-hw-create-status]"),
            createLines: root.querySelector("[data-hw-create-lines]"),
            openCreateModalBtn: root.querySelector("[data-hw-open-create-modal]"),
            openPurchaseOrderModalBtn: root.querySelector("[data-hw-open-purchase-order-modal]"),
            addCreateLineBtn: root.querySelector("[data-hw-add-create-line]"),
            saveCreateBtn: root.querySelector("[data-hw-save-create]"),
            createModalKicker: root.querySelector("[data-hw-create-modal-kicker]"),
            createModalTitle: root.querySelector("[data-hw-create-modal-title]"),
            createModalSubtitle: root.querySelector("[data-hw-create-modal-subtitle]"),
            createModalMeta: root.querySelector("[data-hw-create-modal-meta]"),
            closeCreateModalButtons: Array.from(root.querySelectorAll("[data-hw-close-create-modal]")),
            createFileInputs: Array.from(root.querySelectorAll("[data-hw-create-file-input]")),
            createFileNames: Array.from(root.querySelectorAll("[data-hw-create-file-name]")),
            purchaseOrderModal: root.querySelector("[data-hw-purchase-order-modal]"),
            purchaseOrderForm: root.querySelector("[data-hw-purchase-order-form]"),
            purchaseOrderStatus: root.querySelector("[data-hw-purchase-order-status]"),
            purchaseOrderLines: root.querySelector("[data-hw-purchase-order-lines]"),
            addPurchaseOrderLineBtn: root.querySelector("[data-hw-add-purchase-order-line]"),
            submitPurchaseOrderBtn: root.querySelector("[data-hw-submit-purchase-order]"),
            closePurchaseOrderModalButtons: Array.from(root.querySelectorAll("[data-hw-close-purchase-order-modal]")),
            purchaseOrderFields: {
                providerName: root.querySelector('[data-hw-purchase-order-field="providerName"]')
            },
            purchaseOrderTotals: {
                subtotal: root.querySelector('[data-hw-purchase-order-total="subtotal"]'),
                vat: root.querySelector('[data-hw-purchase-order-total="vat"]'),
                grand: root.querySelector('[data-hw-purchase-order-total="grand"]')
            },
            createClientOptions: root.querySelector("[data-hw-create-client-options]"),
            createClientHint: root.querySelector("[data-hw-create-client-hint]"),
            createFields: {
                purchaseOrderNumber: root.querySelector('[data-hw-create-field="purchaseOrderNumber"]'),
                odcDate: root.querySelector('[data-hw-create-field="odcDate"]'),
                clientName: root.querySelector('[data-hw-create-field="clientName"]'),
                supplierDocumentType: root.querySelector('[data-hw-create-field="supplierDocumentType"]')
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
                supplierDocumentType: root.querySelector('[data-hw-field="supplierDocumentType"]'),
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
            createEditingRecord: null,
            purchaseOrderLineSequence: 0,
            invoiceSuggestions: [],
            invoiceLookupTimer: 0,
            invoiceLookupSequence: 0,
            impersonationUsers: [],
            impersonatedOwnerId: "",
            impersonatedOwnerEmail: "",
            defaultUserLabel: (root.querySelector("[data-hw-active-user-label]")?.textContent || "").trim()
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
                subtitle: "Completa la documentación inicial y define si el proveedor va por proforma u ODC al proveedor.",
                buttonLabel: "Registrar documentación",
                meta: "Con proforma pasa a Ok para pago; con ODC al proveedor salta ese paso",
                requiredFiles: ["cr07a_ordendecompra"]
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

        elements.impersonationSelect?.addEventListener("change", handleImpersonationChange);
        elements.impersonationReset?.addEventListener("click", resetImpersonation);
        elements.csvFile?.addEventListener("change", handleCsvFileChange);
        elements.analyzeCsvBtn?.addEventListener("click", previewCsv);
        elements.provisionCsvBtn?.addEventListener("click", provisionCsv);
        elements.refreshBtn.addEventListener("click", () => loadBoard());
        elements.selectedActionBtn?.addEventListener("click", openSelectedRows);
        elements.editSelectedBtn?.addEventListener("click", openBulkEditForSelectedRows);
        elements.openCreateModalBtn?.addEventListener("click", openCreateModal);
        elements.openPurchaseOrderModalBtn?.addEventListener("click", openPurchaseOrderModal);
        elements.addCreateLineBtn?.addEventListener("click", () => addCreateLine());
        elements.addPurchaseOrderLineBtn?.addEventListener("click", () => addPurchaseOrderLine());
        elements.createForm?.addEventListener("submit", async event => {
            event.preventDefault();
            await saveCommercialCreateForm();
        });
        elements.purchaseOrderForm?.addEventListener("submit", async event => {
            event.preventDefault();
            await submitPurchaseOrder();
        });
        elements.purchaseOrderForm?.addEventListener("input", event => {
            const target = event.target;
            if (target instanceof HTMLElement && target.closest("[data-hw-purchase-order-line]")) {
                recalculatePurchaseOrderTotals();
            }
        });
        elements.createForm?.addEventListener("change", event => {
            const target = event.target;
            if (!(target instanceof HTMLInputElement)) {
                return;
            }

            const fileInput = target.closest("[data-hw-create-file-input]");
            if (fileInput instanceof HTMLInputElement) {
                renderCreateFileNames();
            }
        });
        elements.createFields.clientName?.addEventListener("input", handleCreateClientLookupInput);
        elements.createFields.supplierDocumentType?.addEventListener("change", () => {
            syncCreateSupplierDocumentCards();
            renderCreateFileNames();
            renderCreateFormMode();
        });
        elements.fields.supplierDocumentType?.addEventListener("change", () => {
            syncDocumentationDocumentCards();
            renderFileCards();
            updateDocumentationModalMeta();
        });
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

            const downloadGroupButton = target.closest("[data-hw-download-group]");
            if (downloadGroupButton instanceof HTMLButtonElement) {
                const group = findDisplayGroup(downloadGroupButton.dataset.hwDownloadGroup || "");
                if (group) {
                    downloadSupplierPaymentDocuments(group.rows);
                }
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
                return;
            }

            if (target.closest("a, button, input, select, textarea, label")) {
                return;
            }

            const openRecordRow = target.closest("[data-hw-open-record]");
            if (isCommercialMode && openRecordRow instanceof HTMLElement) {
                const record = findRow(openRecordRow.dataset.hwOpenRecord || "");
                if (record) {
                    openModalForRecords([record]);
                }
                return;
            }

            const openGroupRow = target.closest("[data-hw-open-group]");
            if (isCommercialMode && openGroupRow instanceof HTMLElement) {
                const group = findDisplayGroup(openGroupRow.dataset.hwOpenGroup || "");
                if (group) {
                    openModalForRecords(group.rows);
                }
                return;
            }

            const editableGroupRow = target.closest("[data-hw-edit-group]");
            if (isCommercialMode && editableGroupRow instanceof HTMLElement) {
                const group = findDisplayGroup(editableGroupRow.dataset.hwEditGroup || "");
                const editableRecords = Array.isArray(group?.rows)
                    ? group.rows.filter(isCommercialLineEditable)
                    : [];
                if (editableRecords.length === 1) {
                    openCreateModalForEdit(editableRecords[0]);
                } else if (editableRecords.length > 1) {
                    setStatus(elements.boardStatus, "info", "Selecciona la línea específica que quieres editar.");
                }
                return;
            }

            const editableRow = target.closest("[data-hw-edit-record]");
            if (isCommercialMode && editableRow instanceof HTMLElement) {
                const record = findRow(editableRow.dataset.hwEditRecord || "");
                if (record) {
                    openCreateModalForEdit(record);
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

        elements.closeCreateModalButtons.forEach(button => {
            button.addEventListener("click", closeCreateModal);
        });

        elements.closePurchaseOrderModalButtons.forEach(button => {
            button.addEventListener("click", closePurchaseOrderModal);
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

        elements.createModal?.addEventListener("click", event => {
            const target = event.target;
            if (target instanceof HTMLElement && target.hasAttribute("data-hw-close-create-modal")) {
                closeCreateModal();
            }
        });

        elements.purchaseOrderModal?.addEventListener("click", event => {
            const target = event.target;
            if (target instanceof HTMLElement && target.hasAttribute("data-hw-close-purchase-order-modal")) {
                closePurchaseOrderModal();
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

        elements.purchaseOrderForm?.addEventListener("click", event => {
            const target = event.target;
            if (!(target instanceof HTMLElement)) {
                return;
            }

            const removeLineButton = target.closest("[data-hw-remove-purchase-order-line]");
            if (removeLineButton instanceof HTMLButtonElement) {
                removePurchaseOrderLine(removeLineButton.dataset.hwRemovePurchaseOrderLine || "");
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

            if (elements.purchaseOrderModal && !elements.purchaseOrderModal.hidden) {
                closePurchaseOrderModal();
            } else if (elements.createModal && !elements.createModal.hidden) {
                closeCreateModal();
            } else if (elements.editModal && !elements.editModal.hidden) {
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
        if (isCommercialMode && elements.purchaseOrderLines && !elements.purchaseOrderLines.children.length) {
            addPurchaseOrderLine();
        }

        if (config.canImpersonate && elements.impersonationSelect && config.impersonationUsersUrl) {
            loadImpersonationUsers();
        }

        syncAccessControls();
        loadBoard();

        async function loadImpersonationUsers() {
            try {
                const result = await fetchJson(config.impersonationUsersUrl, { method: "GET" });
                state.impersonationUsers = Array.isArray(result) ? result : [];
                renderImpersonationOptions();
            } catch (error) {
                setStatus(elements.status, "warning", getErrorMessage(error));
            }
        }

        function renderImpersonationOptions() {
            if (!elements.impersonationSelect) {
                return;
            }

            const currentId = normalizeGuid(config.currentUserId);
            const selectedId = normalizeGuid(state.impersonatedOwnerId);
            const options = state.impersonationUsers
                .filter(user => normalizeGuid(user?.id || "") !== currentId)
                .map(user => {
                    const id = user?.id || "";
                    return `
                        <option value="${escapeHtml(id)}" data-email="${escapeHtml(user?.email || "")}" ${normalizeGuid(id) === selectedId ? "selected" : ""}>
                            ${escapeHtml(buildSystemUserLabel(user))}
                        </option>
                    `;
                })
                .join("");

            elements.impersonationSelect.innerHTML = `
                <option value="">Mi usuario</option>
                ${options}
            `;
            elements.impersonationSelect.value = state.impersonatedOwnerId || "";
        }

        function handleImpersonationChange() {
            const selectedOption = elements.impersonationSelect?.selectedOptions?.[0] || null;
            state.impersonatedOwnerId = elements.impersonationSelect?.value || "";
            state.impersonatedOwnerEmail = selectedOption?.dataset?.email || "";
            state.selectedRecordIds.clear();
            updateActiveUserLabel(selectedOption);
            syncAccessControls();
            closeModal(true);
            closeCreateModal(true);
            closeEditModal(true);
            loadBoard();
        }

        function resetImpersonation() {
            if (!elements.impersonationSelect) {
                return;
            }

            elements.impersonationSelect.value = "";
            handleImpersonationChange();
        }

        function updateActiveUserLabel(selectedOption) {
            if (!elements.activeUserLabel) {
                return;
            }

            elements.activeUserLabel.textContent = state.impersonatedOwnerId
                ? selectedOption?.textContent?.trim() || "Usuario seleccionado"
                : state.defaultUserLabel || "Mi usuario";
        }

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
            syncAccessControls();

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
            if (!isSupplierPaymentEffectiveUser()) {
                renderCommercialStateSections(rows);
                return;
            }

            renderSupplierPaymentRows(rows);
        }

        function renderSupplierPaymentRows(rows) {
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
                    <table class="table align-middle hardware-table hardware-supplier-payment-table">
                        <thead>
                            <tr>
                                <th>ODC</th>
                                <th>Cliente</th>
                                <th>Proveedor</th>
                                <th class="text-end">Valor total a pagar</th>
                                <th>Descargar</th>
                                <th>Registrar pago</th>
                            </tr>
                        </thead>
                        <tbody>
                            ${groups.map(renderSupplierPaymentGroupRow).join("")}
                        </tbody>
                    </table>
                </div>
            `;
        }

        function renderCommercialStateSections(rows) {
            const sections = buildCommercialStateSections(rows);
            state.displayItems = sections.flatMap(section => section.groups);

            if (!rows.length) {
                elements.rows.innerHTML = `
                    <div class="hardware-empty">No hay registros de Hardware para tu usuario.</div>
                `;
                syncSelectAllState();
                return;
            }

            elements.rows.innerHTML = `
                <div class="hardware-state-sections">
                    ${sections.map(renderCommercialStateSection).join("")}
                </div>
            `;
        }

        function buildCommercialStateSections(rows) {
            const rowsByState = new Map();
            rows.forEach(row => {
                const stateValue = Number(row?.stateValue || 0);
                if (!rowsByState.has(stateValue)) {
                    rowsByState.set(stateValue, []);
                }

                rowsByState.get(stateValue).push(row);
            });

            const selectedValue = Number(state.board?.selectedStateValue || 0);
            const stateOptions = Array.isArray(state.board?.stateOptions) ? state.board.stateOptions : [];
            const baseOptions = stateOptions.length
                ? stateOptions
                : Array.from(rowsByState.keys()).map(value => {
                    const first = rowsByState.get(value)?.[0] || {};
                    return {
                        value,
                        label: first.stateLabel || `Estado ${value}`,
                        tone: first.stateTone || ""
                    };
                });
            const visibleOptions = selectedValue > 0
                ? baseOptions.filter(option => Number(option?.value || 0) === selectedValue)
                : baseOptions;
            const knownValues = new Set(visibleOptions.map(option => Number(option?.value || 0)));
            const sections = visibleOptions.map(option => {
                const stateValue = Number(option?.value || 0);
                const stateRows = rowsByState.get(stateValue) || [];
                return buildCommercialStateSection(option, stateRows, stateValue);
            });

            rowsByState.forEach((stateRows, stateValue) => {
                if (knownValues.has(stateValue) || (selectedValue > 0 && stateValue !== selectedValue)) {
                    return;
                }

                const first = stateRows[0] || {};
                sections.push(buildCommercialStateSection({
                    value: stateValue,
                    label: first.stateLabel || `Estado ${stateValue}`,
                    tone: first.stateTone || ""
                }, stateRows, stateValue));
            });

            return sections;
        }

        function buildCommercialStateSection(option, rows, stateValue) {
            const groups = buildCommercialGroups(rows, `state-${stateValue}`);
            return {
                option,
                rows,
                groups,
                count: rows.length,
                quantity: rows.reduce((total, row) => total + Number(row?.quantity || 0), 0),
                totalSale: rows.reduce((total, row) => total + Number(row?.totalSale || 0), 0)
            };
        }

        function renderCommercialStateSection(section) {
            const option = section.option || {};
            const label = option.label || "Sin estado";
            const tone = option.tone || "";
            return `
                <section class="hardware-state-section ${toneClass(tone)}">
                    <div class="hardware-state-section__header">
                        <div class="hardware-state-section__title">
                            ${renderStatePill(label, tone)}
                            <div>
                                <h3>${escapeHtml(label)}</h3>
                                <p>${formatNumber(section.count)} línea(s) · ${formatNumber(section.quantity)} und · ${formatCurrency(section.totalSale)}</p>
                            </div>
                        </div>
                    </div>
                    ${section.rows.length
                        ? `<div class="hardware-table-wrap">
                            <table class="table align-middle hardware-table hardware-commercial-table hardware-commercial-state-table">
                                <thead>
                                    <tr>
                                        <th>Orden / Cliente</th>
                                        <th>Producto / referencia</th>
                                        <th class="text-end">Cant.</th>
                                        <th class="text-end">Costo proveedor</th>
                                        <th class="text-end">Venta unidad</th>
                                        <th>Proveedor</th>
                                        <th>Acción</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    ${section.groups.map(group => renderCommercialStateGroup(group)).join("")}
                                </tbody>
                            </table>
                        </div>`
                        : `<div class="hardware-empty hardware-empty--state">Sin registros en este estado.</div>`}
                </section>
            `;
        }

        function renderCommercialStateGroup(group) {
            const first = group.rows[0] || {};
            const validAction = validateActionRecords(group.rows);
            const editableRows = group.rows.filter(isCommercialLineEditable);
            const groupCanOpenSingleEdit = editableRows.length === 1;
            const totalQuantity = group.rows.reduce((total, row) => total + Number(row?.quantity || 0), 0);
            const totalSale = group.rows.reduce((total, row) => total + Number(row?.totalSale || 0), 0);
            const clientLabel = getCommonValue(group.rows, "clientName") || "Varios clientes";
            const odcDate = getCommonValue(group.rows, "odcDateDisplay") || "Varias fechas";
            const groupEditAttributes = groupCanOpenSingleEdit
                ? ` data-hw-edit-group="${escapeHtml(group.key)}" title="Editar línea"`
                : "";
            return `
                <tr class="hardware-table__row hardware-commercial-table__group ${groupCanOpenSingleEdit ? "is-editable" : ""} ${toneClass(first.stateTone)}"${groupEditAttributes}>
                    <td colspan="7">
                        <div class="hardware-commercial-order">
                            <div>
                                <strong>${escapeHtml(group.orderNumber)}</strong>
                                <span>${escapeHtml(clientLabel)} · ${escapeHtml(odcDate)} · ${formatNumber(group.rows.length)} fila(s) · ${formatNumber(totalQuantity)} und · ${formatCurrency(totalSale)}</span>
                                <div class="hardware-commercial-order__files">
                                    ${renderCommercialGroupFileLink(group.rows, "cr07a_ordendecompra")}
                                    ${renderCommercialGroupSupplierDocumentLink(group.rows)}
                                    ${renderCommercialGroupFileLink(group.rows, "cr07a_pagoaproveedor")}
                                    ${renderCommercialGroupFileLink(group.rows, "cr07a_actadeentrega")}
                                    ${renderCommercialGroupInvoiceChip(group.rows)}
                                </div>
                            </div>
                            <div class="hardware-commercial-order__status">
                                ${editableRows.length > 0
                                    ? `<span class="hardware-table__submeta">${escapeHtml(groupCanOpenSingleEdit ? "Click para editar" : "Click en una línea para editarla")}</span>`
                                    : validAction.ok
                                    ? `<button type="button" class="btn btn-sm btn-primary" data-hw-action-group="${escapeHtml(group.key)}">${escapeHtml(group.rows[0].actionLabel || "Gestionar")}</button>`
                                    : `<span class="hardware-table__submeta">${escapeHtml(validAction.message || "Sin botón")}</span>`}
                            </div>
                        </div>
                    </td>
                </tr>
                ${group.rows.map(row => renderCommercialStateRecordRow(row)).join("")}
            `;
        }

        function renderCommercialStateRecordRow(row) {
            const editable = isCommercialLineEditable(row);
            const editAttributes = editable
                ? ` data-hw-edit-record="${escapeHtml(row?.recordId || "")}" title="Editar línea"`
                : "";
            const actionLabel = resolveCommercialLineStatusLabel(row, editable);
            return `
                <tr class="hardware-table__row hardware-table__row--child ${editable ? "is-editable" : ""} ${toneClass(row?.stateTone)}"${editAttributes}>
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
                    <td>
                        <div class="hardware-action-cell">
                            <span class="hardware-table__submeta">${escapeHtml(actionLabel)}</span>
                        </div>
                    </td>
                </tr>
            `;
        }

        function buildCommercialGroups(rows, keyPrefix = "") {
            const groups = new Map();
            rows.forEach((row, index) => {
                const orderNumber = String(row?.purchaseOrderNumber || "").trim() || "Sin orden";
                const baseKey = normalizeText(orderNumber) || `sin-orden-${index}`;
                const key = keyPrefix ? `${keyPrefix}|${baseKey}` : baseKey;
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

        function renderHardwareFlowTableRow(row, columnCount, isChild = false, extraClass = "") {
            const classes = [
                "hardware-flow-row",
                isChild ? "hardware-flow-row--child" : "",
                toneClass(row?.stateTone),
                extraClass
            ].filter(Boolean).join(" ");

            return `
                <tr class="${classes}">
                    <td colspan="${Number(columnCount) || 1}">
                        ${renderHardwareFlowMini(row)}
                    </td>
                </tr>
            `;
        }

        function renderHardwareFlowMini(row) {
            const steps = [
                { value: 645250000, label: "Documentación", tone: "documentation" },
                { value: 645250001, label: "Ok pago proveedor", tone: "supplier-ready" },
                { value: 645250002, label: "Pagada proveedor", tone: "supplier-paid" },
                { value: 645250003, label: "Tránsito", tone: "in-transit" },
                { value: 645250004, label: "Entrega", tone: "awaiting-billing" },
                { value: 645250005, label: "Factura", tone: "awaiting-payment" },
                { value: 645250006, label: "Cierre", tone: "closed" }
            ];
            const currentValue = Number(row?.stateValue || 645250000);
            const skipsSupplierPayment = normalizeSupplierDocumentType(row?.supplierDocumentType || "") === "odc-proveedor";
            const nextActiveStep = steps.find(step =>
                step.value > currentValue
                && !(skipsSupplierPayment && step.value === 645250001));

            const renderedSteps = steps.map(step => {
                const skipped = skipsSupplierPayment && step.value === 645250001 && currentValue >= 645250002;
                const current = currentValue === step.value;
                const done = !current && !skipped && currentValue > step.value;
                const next = !current && !done && !skipped && nextActiveStep?.value === step.value;
                const stateClass = skipped
                    ? "is-skipped"
                    : current
                        ? "is-current"
                        : done
                            ? "is-done"
                            : next
                                ? "is-next"
                                : "is-pending";
                const title = skipped
                    ? `${step.label}: omitido por ODC al proveedor`
                    : current
                        ? `${step.label}: paso actual`
                        : done
                            ? `${step.label}: completado`
                            : next
                                ? `${step.label}: siguiente paso`
                                : `${step.label}: pendiente`;

                return `
                    <span class="hardware-flow-step ${stateClass} ${toneClass(step.tone)}" title="${escapeHtml(title)}" role="listitem">
                        <span class="hardware-flow-step__dot" aria-hidden="true"></span>
                        <span class="hardware-flow-step__label">${escapeHtml(step.label)}</span>
                    </span>
                `;
            }).join("");

            return `
                <div class="hardware-flow" role="list" aria-label="Flujo de estados de ${escapeHtml(row?.name || "hardware")}">
                    ${renderedSteps}
                </div>
            `;
        }

        function renderCommercialGroup(group, showLineProforma = false) {
            const first = group.rows[0] || {};
            const validAction = validateActionRecords(group.rows);
            const totalQuantity = group.rows.reduce((total, row) => total + Number(row?.quantity || 0), 0);
            const clientLabel = getCommonValue(group.rows, "clientName") || "Varios clientes";
            const odcDate = getCommonValue(group.rows, "odcDateDisplay") || "Varias fechas";
            const groupStateLabel = getCommonValue(group.rows, "stateLabel") || "Varios estados";
            const groupStateTone = getCommonValue(group.rows, "stateTone") || first.stateTone || "";
            const columnCount = showLineProforma ? 9 : 8;
            return `
                <tr class="hardware-table__row hardware-commercial-table__group ${toneClass(first.stateTone)}">
                    <td colspan="${columnCount}">
                        <div class="hardware-commercial-order">
                            <div>
                                <strong>${escapeHtml(group.orderNumber)}</strong>
                                <span>${escapeHtml(clientLabel)} · ${escapeHtml(odcDate)} · ${formatNumber(group.rows.length)} fila(s) · ${formatNumber(totalQuantity)} und</span>
                                <div class="hardware-commercial-order__files">
                                    ${renderCommercialGroupFileLink(group.rows, "cr07a_ordendecompra")}
                                    ${renderCommercialGroupSupplierDocumentLink(group.rows)}
                                    ${renderCommercialGroupFileLink(group.rows, "cr07a_pagoaproveedor")}
                                    ${renderCommercialGroupFileLink(group.rows, "cr07a_actadeentrega")}
                                    ${renderCommercialGroupInvoiceChip(group.rows)}
                                </div>
                            </div>
                            <div class="hardware-commercial-order__status">
                                ${renderStatePill(groupStateLabel, groupStateTone)}
                                ${validAction.ok
                                    ? `<button type="button" class="btn btn-sm btn-primary" data-hw-action-group="${escapeHtml(group.key)}">${escapeHtml(group.rows[0].actionLabel || "Gestionar")}</button>`
                                    : `<span class="hardware-table__submeta">${escapeHtml(validAction.message || "Sin botón")}</span>`}
                            </div>
                        </div>
                    </td>
                </tr>
                ${group.rows.map(row => renderCommercialRecordRow(row, group.rows, showLineProforma, columnCount)).join("")}
            `;
        }

        function renderSupplierPaymentGroupRow(group) {
            const first = group.rows[0] || {};
            const validation = validateActionRecords(group.rows);
            const clientLabel = getCommonValue(group.rows, "clientName") || "Varios clientes";
            const providerLabel = getCommonValue(group.rows, "provider") || "Varios proveedores";
            const paymentTotal = calculateSupplierPaymentTotal(group.rows);
            const downloads = resolveSupplierPaymentDocumentDownloads(group.rows);
            return `
                <tr class="hardware-table__row hardware-supplier-payment-row ${toneClass(first.stateTone)}" data-hw-open-group="${escapeHtml(group.key)}" title="Abrir gestión de pago a proveedor">
                    <td class="hardware-table__order-cell">
                        <span class="hardware-order-number">${escapeHtml(group.orderNumber || "Sin ODC")}</span>
                    </td>
                    <td class="hardware-table__client-cell">
                        <strong>${escapeHtml(clientLabel)}</strong>
                    </td>
                    <td>${escapeHtml(providerLabel || "-")}</td>
                    <td class="text-end"><strong>${formatCurrency(paymentTotal)}</strong></td>
                    <td>
                        <div class="hardware-action-cell">
                            <button type="button" class="btn btn-sm btn-outline-primary" data-hw-download-group="${escapeHtml(group.key)}" ${downloads.length ? "" : "disabled"}>
                                Descargar
                            </button>
                        </div>
                    </td>
                    <td>
                        <div class="hardware-action-cell">
                            ${validation.ok
                                ? `<button type="button" class="btn btn-sm btn-primary" data-hw-action-group="${escapeHtml(group.key)}">Registrar pago</button>`
                                : `<span class="hardware-table__submeta">${escapeHtml(validation.message || "Sin acción")}</span>`}
                        </div>
                    </td>
                </tr>
            `;
        }

        function calculateSupplierPaymentTotal(rows) {
            return (Array.isArray(rows) ? rows : []).reduce((total, row) => {
                const supplierTotal = Number(row?.supplierTotal || 0);
                if (supplierTotal > 0) {
                    return total + supplierTotal;
                }

                return total + (Number(row?.quantity || 0) * Number(row?.supplierUnitCost || 0));
            }, 0);
        }

        function resolveSupplierPaymentDocumentDownloads(rows) {
            const items = Array.isArray(rows) ? rows : [];
            const downloads = [];
            addDownloadIfAvailable(downloads, items, "cr07a_ordendecompra", "ODC");

            const supplierDocumentSource = items.find(item => resolveSupplierDocumentField(item) === "cr07a_odcproveedor")
                || items[0]
                || null;
            addDownloadIfAvailable(
                downloads,
                items,
                resolveSupplierDocumentField(supplierDocumentSource),
                resolveSupplierDocumentField(supplierDocumentSource) === "cr07a_odcproveedor" ? "ODC proveedor" : "Proforma");

            return downloads;
        }

        function addDownloadIfAvailable(downloads, rows, fieldName, label) {
            const record = resolveOrderFileRecord(rows, fieldName);
            if (!record?.recordId || !hasExistingFile(record, fieldName)) {
                return;
            }

            const key = `${record.recordId}|${fieldName}`;
            if (downloads.some(item => item.key === key)) {
                return;
            }

            downloads.push({
                key,
                label,
                url: buildDownloadUrl(record.recordId, fieldName)
            });
        }

        function downloadSupplierPaymentDocuments(rows) {
            const downloads = resolveSupplierPaymentDocumentDownloads(rows);
            if (!downloads.length) {
                setStatus(elements.boardStatus, "warning", "No hay ODC o proforma para descargar en esta orden.");
                return;
            }

            downloads.forEach(item => {
                window.open(item.url, "_blank", "noopener");
            });
        }

        function renderCommercialRecordRow(row, orderRows = [], showLineProforma = false, columnCount = 8) {
            const editable = isCommercialLineEditable(row);
            const openable = isSupplierPaymentEffectiveUser() && Boolean(row?.recordId) && Boolean(row?.hasAction) && Boolean(row?.actionKey);
            const actionLabel = resolveCommercialLineStatusLabel(row, editable, openable);
            const rowAttributes = [
                editable ? `data-hw-edit-record="${escapeHtml(row?.recordId || "")}" title="Editar línea"` : "",
                openable ? `data-hw-open-record="${escapeHtml(row?.recordId || "")}" title="Abrir gestión de pago a proveedor"` : ""
            ].filter(Boolean).join(" ");
            return `
                <tr class="hardware-table__row hardware-table__row--child ${editable ? "is-editable" : ""} ${openable ? "is-actionable" : ""} ${toneClass(row?.stateTone)}"${rowAttributes ? ` ${rowAttributes}` : ""}>
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
                    ${showLineProforma ? `<td class="text-center">${renderCommercialLineProformaLink(row, orderRows)}</td>` : ""}
                    <td class="hardware-table__state-cell">${renderStatePill(row?.stateLabel || "Sin estado", row?.stateTone || "")}</td>
                    <td>
                        <div class="hardware-action-cell">
                            <span class="hardware-table__submeta">${escapeHtml(actionLabel)}</span>
                        </div>
                    </td>
                </tr>
                ${renderHardwareFlowTableRow(row, columnCount, true)}
            `;
        }

        function renderCommercialLineProformaLink(row, orderRows) {
            const fieldName = resolveSupplierDocumentField(row);
            const label = fieldName === "cr07a_odcproveedor" ? "ODC proveedor" : "Proforma";
            const recordWithDocument = resolveOrderFileRecord(orderRows, fieldName);
            const hasDocument = hasExistingFile(recordWithDocument, fieldName);
            if (!hasDocument || !recordWithDocument?.recordId) {
                return `
                    <span class="hardware-icon-link is-disabled" title="${escapeHtml(label)} pendiente" aria-label="${escapeHtml(label)} pendiente">
                        ${downloadIconSvg()}
                        <span class="visually-hidden">${escapeHtml(label)} pendiente</span>
                    </span>
                `;
            }

            const fileName = resolveExistingFileName(recordWithDocument, fieldName);
            const title = fileName ? `Descargar ${label}: ${fileName}` : `Descargar ${label}`;
            return `
                <a class="hardware-icon-link"
                   href="${escapeHtml(buildDownloadUrl(recordWithDocument.recordId, fieldName))}"
                   target="_blank"
                   rel="noopener"
                   title="${escapeHtml(title)}"
                   aria-label="${escapeHtml(title)}">
                    ${downloadIconSvg()}
                    <span class="visually-hidden">Descargar ${escapeHtml(label)} de ${escapeHtml(row?.name || "esta línea")}</span>
                </a>
            `;
        }

        function downloadIconSvg() {
            return `
                <svg aria-hidden="true" viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                    <path d="M7 10l5 5 5-5"></path>
                    <path d="M12 15V3"></path>
                </svg>
            `;
        }

        function renderCommercialGroupFileLink(rows, fieldName) {
            const row = resolveOrderFileRecord(rows, fieldName);
            const label = resolveFileChipLabel(fieldName);
            const hasFile = hasExistingFile(row, fieldName);
            if (!hasFile) {
                return `<span class="hardware-file-chip is-missing">${escapeHtml(label)} pendiente</span>`;
            }

            const fileName = resolveExistingFileName(row, fieldName);
            return `
                <a class="hardware-file-chip" href="${escapeHtml(buildDownloadUrl(row.recordId, fieldName))}" target="_blank" rel="noopener">
                    ${escapeHtml(label)}${fileName ? ` · ${escapeHtml(fileName)}` : ""}
                </a>
            `;
        }

        function renderCommercialGroupSupplierDocumentLink(rows) {
            const items = Array.isArray(rows) ? rows : [];
            const row = items.find(item => resolveSupplierDocumentField(item) === "cr07a_odcproveedor")
                || items[0]
                || null;
            return renderCommercialGroupFileLink(items, resolveSupplierDocumentField(row));
        }

        function renderCommercialGroupInvoiceChip(rows) {
            const invoiceNumbers = Array.from(new Set((Array.isArray(rows) ? rows : [])
                .map(row => String(row?.invoiceNumber || "").trim())
                .filter(Boolean)));

            if (!invoiceNumbers.length) {
                return `<span class="hardware-file-chip is-missing">Factura pendiente</span>`;
            }

            const label = invoiceNumbers.length === 1
                ? `Factura · ${invoiceNumbers[0]}`
                : `Facturas · ${formatNumber(invoiceNumbers.length)}`;
            return `<span class="hardware-file-chip">${escapeHtml(label)}</span>`;
        }

        function resolveFileChipLabel(fieldName) {
            switch (fieldName) {
                case "cr07a_ordendecompra":
                    return "ODC cliente";
                case "cr07a_adjuntarproforma":
                    return "Proforma proveedor";
                case "cr07a_odcproveedor":
                    return "ODC proveedor";
                case "cr07a_pagoaproveedor":
                    return "Pago proveedor";
                case "cr07a_actadeentrega":
                    return "Acta entrega";
                default:
                    return "Archivo";
            }
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
                ${expanded ? group.rows.map(row => renderRecordRow(row, true)).join("") : renderHardwareFlowTableRow(first, 9, false, "hardware-flow-row--group")}
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
                ${renderHardwareFlowTableRow(row, 9, isChild)}
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

        function isCommercialLineEditable(row) {
            return isCommercialMode
                && config.allowCommercialDraftEdit
                && !isSupplierPaymentEffectiveUser()
                && Boolean(row?.recordId)
                && Number(row?.stateValue || 0) === hardwareStateOkForSupplierPayment;
        }

        function hasCommercialLinePassedEditableState(row) {
            return Number(row?.stateValue || 0) > hardwareStateOkForSupplierPayment;
        }

        function resolveCommercialLineStatusLabel(row, editable, openable = false) {
            if (openable) {
                return "Abrir";
            }

            if (editable) {
                return "Editar";
            }

            return hasCommercialLinePassedEditableState(row)
                ? "Bloqueada"
                : "Gestionar por orden";
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
            setFieldValue(
                elements.fields.supplierDocumentType,
                normalizeSupplierDocumentType(getCommonValue(state.modalRecords, "supplierDocumentType") || "proforma"));
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
                syncDocumentationDocumentCards();
                updateDocumentationModalMeta();
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
                </tr>
            `).join("");
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
            if ((!elements.createModal || elements.createModal.hidden)
                && (!elements.purchaseOrderModal || elements.purchaseOrderModal.hidden)
                && (!elements.editModal || elements.editModal.hidden)) {
                document.body.classList.remove("hardware-modal-open");
            }
        }

        function renderFileCards() {
            syncDocumentationDocumentCards();
            const fileFields = [
                "cr07a_ordendecompra",
                "cr07a_adjuntarproforma",
                "cr07a_odcproveedor",
                "cr07a_pagoaproveedor",
                "cr07a_actadeentrega"
            ];

            fileFields.forEach(fieldName => {
                const fileNameTargets = elements.fileNames.filter(item => item.dataset.hwFileName === fieldName);
                const fileHintTargets = elements.fileHints.filter(item => item.dataset.hwFileHint === fieldName);
                const downloadLinks = elements.downloadLinks.filter(item => item.dataset.hwDownloadLink === fieldName);
                const pendingFile = state.pendingFiles[fieldName];
                const actionKey = elements.actionKey?.value || "";
                const isDocumentationOrderFile = actionKey === "register-documentation" && isOrderDocumentationFile(fieldName);
                const orderFileRecord = resolveOrderFileRecord(state.modalRecords, fieldName);
                const orderHasFile = hasExistingFile(orderFileRecord, fieldName);

                if (isDocumentationOrderFile) {
                    const orderHasFile = hasExistingFile(orderFileRecord, fieldName);
                    fileNameTargets.forEach(fileNameTarget => {
                        fileNameTarget.textContent = pendingFile instanceof File
                            ? pendingFile.name
                            : resolveExistingFileName(orderFileRecord, fieldName) || "Sin archivo";
                    });

                    fileHintTargets.forEach(fileHintTarget => {
                        fileHintTarget.textContent = pendingFile instanceof File
                            ? "El archivo se cargará solo en la primera fila de la orden."
                            : orderHasFile
                                ? "Adjunto registrado en una fila de la orden."
                                : (fileHintTarget.dataset.defaultHint || "");
                    });

                    downloadLinks.forEach(downloadLink => {
                        downloadLink.href = orderHasFile && orderFileRecord
                            ? buildDownloadUrl(orderFileRecord.recordId, fieldName)
                            : "#";
                        downloadLink.classList.toggle("is-disabled", !orderHasFile);
                    });

                    return;
                }

                fileNameTargets.forEach(fileNameTarget => {
                    fileNameTarget.textContent = pendingFile instanceof File
                        ? pendingFile.name
                        : resolveExistingFileName(orderFileRecord, fieldName) || "Sin archivo";
                });

                fileHintTargets.forEach(fileHintTarget => {
                    fileHintTarget.textContent = pendingFile instanceof File
                        ? "El archivo se cargará solo en la primera fila de la orden."
                        : orderHasFile
                            ? "Adjunto registrado en una fila de la orden."
                            : (fileHintTarget.dataset.defaultHint || "");
                });

                downloadLinks.forEach(downloadLink => {
                    const hideSupplierPaymentDownload = isSupplierPaymentEffectiveUser()
                        && actionKey === "register-supplier-payment"
                        && fieldName === "cr07a_pagoaproveedor";
                    downloadLink.hidden = hideSupplierPaymentDownload;
                    if (hideSupplierPaymentDownload) {
                        return;
                    }

                    downloadLink.href = orderHasFile && orderFileRecord
                        ? buildDownloadUrl(orderFileRecord.recordId, fieldName)
                        : "#";
                    downloadLink.classList.toggle("is-disabled", !(orderHasFile && orderFileRecord));
                });
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
                const result = await fetchJson(buildImpersonatedUrl(config.saveUrl), {
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

        function openPurchaseOrderModal() {
            if (!elements.purchaseOrderModal) {
                return;
            }
            if (!canCreateCommercialRecords()) {
                setStatus(elements.boardStatus, "warning", "La vista de cartera no puede generar ODC comerciales.");
                return;
            }

            resetPurchaseOrderForm();
            clearStatus(elements.purchaseOrderStatus);
            elements.purchaseOrderModal.hidden = false;
            document.body.classList.add("hardware-modal-open");
            elements.purchaseOrderFields.providerName?.focus();
        }

        function closePurchaseOrderModal(force = false) {
            if (state.saving && !force) {
                return;
            }

            clearStatus(elements.purchaseOrderStatus);
            if (elements.purchaseOrderModal) {
                elements.purchaseOrderModal.hidden = true;
            }

            if ((!elements.modal || elements.modal.hidden)
                && (!elements.createModal || elements.createModal.hidden)
                && (!elements.editModal || elements.editModal.hidden)) {
                document.body.classList.remove("hardware-modal-open");
            }
        }

        function addPurchaseOrderLine(values = {}) {
            if (!elements.purchaseOrderLines) {
                return;
            }

            const rowKey = values.rowKey || `purchase-line-${++state.purchaseOrderLineSequence}`;
            elements.purchaseOrderLines.insertAdjacentHTML("beforeend", `
                <tr data-hw-purchase-order-line="${escapeHtml(rowKey)}">
                    <td>
                        <input type="text" class="form-control form-control-sm" data-hw-purchase-order-line-field="product" value="${escapeHtml(values.product || "")}" />
                    </td>
                    <td>
                        <input type="number" min="1" step="1" class="form-control form-control-sm text-end" data-hw-purchase-order-line-field="quantity" value="${escapeHtml(values.quantity || "")}" />
                    </td>
                    <td>
                        <input type="number" min="0" step="0.01" class="form-control form-control-sm text-end" data-hw-purchase-order-line-field="unitValueBeforeVat" value="${escapeHtml(values.unitValueBeforeVat || "")}" />
                    </td>
                    <td class="text-end hardware-purchase-order-computed" data-hw-purchase-order-line-total="beforeVat">COP 0</td>
                    <td>
                        <input type="number" min="0" max="100" step="0.01" class="form-control form-control-sm text-end" data-hw-purchase-order-line-field="vatPercent" value="${escapeHtml(values.vatPercent ?? "19")}" />
                    </td>
                    <td class="text-end hardware-purchase-order-computed" data-hw-purchase-order-line-total="withVat">COP 0</td>
                    <td>
                        <button type="button" class="btn btn-sm btn-outline-secondary" data-hw-remove-purchase-order-line="${escapeHtml(rowKey)}">Quitar</button>
                    </td>
                </tr>
            `);
            syncPurchaseOrderLineButtons();
            recalculatePurchaseOrderTotals();
        }

        function removePurchaseOrderLine(rowKey) {
            if (!elements.purchaseOrderLines) {
                return;
            }

            const row = elements.purchaseOrderLines.querySelector(`[data-hw-purchase-order-line="${cssEscape(rowKey)}"]`);
            row?.remove();
            if (!elements.purchaseOrderLines.children.length) {
                addPurchaseOrderLine();
            }
            syncPurchaseOrderLineButtons();
            recalculatePurchaseOrderTotals();
        }

        function syncPurchaseOrderLineButtons() {
            if (!elements.purchaseOrderLines) {
                return;
            }

            const rows = Array.from(elements.purchaseOrderLines.querySelectorAll("[data-hw-purchase-order-line]"));
            rows.forEach(row => {
                const button = row.querySelector("[data-hw-remove-purchase-order-line]");
                if (button instanceof HTMLButtonElement) {
                    button.disabled = rows.length <= 1 || state.busy;
                }
            });
        }

        function syncDocumentationDocumentCards() {
            const actionKey = elements.actionKey?.value || "";
            const supplierDocumentType = normalizeSupplierDocumentType(elements.fields.supplierDocumentType?.value || "proforma");
            root.querySelectorAll("[data-hw-file-card]").forEach(card => {
                const fieldName = card.dataset.hwFileCard || "";
                if (actionKey !== "register-documentation") {
                    if (fieldName === "cr07a_odcproveedor") {
                        card.hidden = true;
                    } else if (fieldName === "cr07a_ordendecompra" || fieldName === "cr07a_adjuntarproforma") {
                        card.hidden = false;
                    }
                    return;
                }

                if (fieldName === "cr07a_adjuntarproforma") {
                    card.hidden = supplierDocumentType !== "proforma";
                } else if (fieldName === "cr07a_odcproveedor") {
                    card.hidden = supplierDocumentType !== "odc-proveedor";
                } else if (fieldName === "cr07a_ordendecompra") {
                    card.hidden = false;
                }
            });
        }

        function updateDocumentationModalMeta() {
            if ((elements.actionKey?.value || "") !== "register-documentation") {
                return;
            }

            const supplierDocumentType = normalizeSupplierDocumentType(elements.fields.supplierDocumentType?.value || "proforma");
            setText(
                elements.modalMeta,
                supplierDocumentType === "odc-proveedor"
                    ? "Próximo estado: Pagada a proveedor. Se omite Ok para pago a proveedor."
                    : "Próximo estado: Ok para pago a proveedor.");
        }

        function resetPurchaseOrderForm() {
            elements.purchaseOrderForm?.reset();
            if (elements.purchaseOrderLines) {
                elements.purchaseOrderLines.innerHTML = "";
                addPurchaseOrderLine();
            }
            recalculatePurchaseOrderTotals();
        }

        async function submitPurchaseOrder() {
            if (state.saving || !elements.purchaseOrderForm || !config.purchaseOrderUrl) {
                return;
            }

            let draft;
            try {
                draft = buildPurchaseOrderDraft();
            } catch (error) {
                setStatus(elements.purchaseOrderStatus, "error", getErrorMessage(error));
                return;
            }

            try {
                state.saving = true;
                setBusy(true);
                setStatus(elements.purchaseOrderStatus, "info", "Enviando ODC para aprobación...");
                const result = await fetchJson(config.purchaseOrderUrl, {
                    method: "POST",
                    body: JSON.stringify(draft)
                });

                resetPurchaseOrderForm();
                closePurchaseOrderModal(true);
                setStatus(elements.status, "success", result?.message || "ODC enviada para aprobación.");
            } catch (error) {
                setStatus(elements.purchaseOrderStatus, "error", getErrorMessage(error));
            } finally {
                state.saving = false;
                if (state.busy) {
                    setBusy(false);
                }
            }
        }

        function buildPurchaseOrderDraft() {
            const providerName = (elements.purchaseOrderFields.providerName?.value || "").trim();
            if (!providerName) {
                throw new Error("Debes diligenciar el nombre de proveedor.");
            }

            const rows = Array.from(elements.purchaseOrderLines?.querySelectorAll("[data-hw-purchase-order-line]") || []);
            if (!rows.length) {
                throw new Error("Agrega al menos una línea.");
            }

            return {
                providerName,
                lines: rows.map((row, index) => {
                    const product = getPurchaseOrderLineValue(row, "product");
                    const quantity = parseIntegerStrict(getPurchaseOrderLineValue(row, "quantity"));
                    const unitValueBeforeVat = parseDecimalStrict(getPurchaseOrderLineValue(row, "unitValueBeforeVat"));
                    const vatPercent = parseDecimalStrict(getPurchaseOrderLineValue(row, "vatPercent"));

                    if (!product) {
                        throw new Error(`Debes diligenciar Producto en la línea ${index + 1}.`);
                    }
                    if (!Number.isInteger(quantity) || quantity <= 0) {
                        throw new Error(`Debes diligenciar una cantidad válida en la línea ${index + 1}.`);
                    }
                    if (!(unitValueBeforeVat > 0)) {
                        throw new Error(`Debes diligenciar un valor unitario válido en la línea ${index + 1}.`);
                    }
                    if (!Number.isFinite(vatPercent) || vatPercent < 0 || vatPercent > 100) {
                        throw new Error(`El IVA de la línea ${index + 1} debe estar entre 0 y 100.`);
                    }

                    return {
                        product,
                        quantity,
                        unitValueBeforeVat,
                        vatPercent
                    };
                })
            };
        }

        function recalculatePurchaseOrderTotals() {
            let subtotal = 0;
            let vatTotal = 0;
            let grandTotal = 0;
            const rows = Array.from(elements.purchaseOrderLines?.querySelectorAll("[data-hw-purchase-order-line]") || []);
            rows.forEach(row => {
                const quantity = parseIntegerStrict(getPurchaseOrderLineValue(row, "quantity"));
                const unitValue = parseDecimalStrict(getPurchaseOrderLineValue(row, "unitValueBeforeVat"));
                const vatPercent = parseDecimalStrict(getPurchaseOrderLineValue(row, "vatPercent"));
                const hasValidInputs = Number.isInteger(quantity)
                    && quantity > 0
                    && unitValue > 0
                    && Number.isFinite(vatPercent)
                    && vatPercent >= 0;
                const beforeVat = hasValidInputs ? quantity * unitValue : 0;
                const vatValue = beforeVat * vatPercent / 100;
                const withVat = beforeVat + vatValue;
                subtotal += beforeVat;
                vatTotal += vatValue;
                grandTotal += withVat;
                setPurchaseOrderComputedValue(row, "beforeVat", formatCurrency(beforeVat));
                setPurchaseOrderComputedValue(row, "withVat", formatCurrency(withVat));
            });

            setText(elements.purchaseOrderTotals.subtotal, formatCurrency(subtotal));
            setText(elements.purchaseOrderTotals.vat, formatCurrency(vatTotal));
            setText(elements.purchaseOrderTotals.grand, formatCurrency(grandTotal));
        }

        function getPurchaseOrderLineValue(row, fieldName) {
            return (row.querySelector(`[data-hw-purchase-order-line-field="${cssEscape(fieldName)}"]`)?.value || "").trim();
        }

        function setPurchaseOrderComputedValue(row, fieldName, value) {
            const target = row.querySelector(`[data-hw-purchase-order-line-total="${cssEscape(fieldName)}"]`);
            if (target) {
                target.textContent = value;
            }
        }

        function openCreateModal() {
            if (!elements.createModal) {
                return;
            }
            if (!canCreateCommercialRecords()) {
                setStatus(elements.boardStatus, "warning", "La vista de cartera solo permite registrar pagos a proveedor.");
                return;
            }

            state.createEditingRecord = null;
            resetCreateForm();
            renderCreateFormMode();
            syncCreateSupplierDocumentCards();
            renderCreateFileNames();
            clearStatus(elements.createStatus);
            elements.createModal.hidden = false;
            document.body.classList.add("hardware-modal-open");
            elements.createFields.purchaseOrderNumber?.focus();
        }

        function openCreateModalForEdit(record) {
            if (!elements.createModal) {
                return;
            }

            if (!isCommercialLineEditable(record)) {
                setStatus(elements.boardStatus, "warning", "Solo puedes editar líneas comerciales en estado Ok para pago a proveedor.");
                return;
            }

            state.createEditingRecord = { ...record };
            resetCreateForm({ preserveEditingRecord: true, addBlankLine: false });
            renderCreateFormMode();

            setFieldValue(elements.createFields.purchaseOrderNumber, record.purchaseOrderNumber || "");
            setFieldValue(elements.createFields.odcDate, record.odcDateValue || "");
            setFieldValue(elements.createFields.clientName, record.clientName || "");
            setFieldValue(elements.createFields.supplierDocumentType, "proforma");
            state.createClientSelection = record.clientId && record.clientName
                ? { id: record.clientId, name: record.clientName }
                : null;
            if (elements.createClientHint) {
                elements.createClientHint.textContent = state.createClientSelection
                    ? "Cliente seleccionado."
                    : "Busca y selecciona un cliente.";
            }

            if (elements.createLines) {
                elements.createLines.innerHTML = "";
                addCreateLine({
                    rowKey: record.recordId || `line-${++state.createLineSequence}`,
                    name: record.name || "",
                    quantity: record.quantity || "",
                    supplierUnitCost: formatInputNumber(record.supplierUnitCost || 0),
                    saleUnit: formatInputNumber(record.saleUnit || 0),
                    provider: record.provider || ""
                });
            }

            syncCreateSupplierDocumentCards();
            renderCreateFileNames();
            clearStatus(elements.createStatus);
            elements.createModal.hidden = false;
            document.body.classList.add("hardware-modal-open");
            const firstLineInput = elements.createLines?.querySelector('[data-hw-create-line-field="name"]');
            if (firstLineInput instanceof HTMLElement) {
                firstLineInput.focus();
            }
        }

        function closeCreateModal(force = false) {
            if (state.saving && !force) {
                return;
            }

            clearStatus(elements.createStatus);
            state.createEditingRecord = null;
            if (elements.createModal) {
                elements.createModal.hidden = true;
            }

            if ((!elements.modal || elements.modal.hidden)
                && (!elements.purchaseOrderModal || elements.purchaseOrderModal.hidden)
                && (!elements.editModal || elements.editModal.hidden)) {
                document.body.classList.remove("hardware-modal-open");
            }
        }

        function renderCreateFormMode() {
            const editing = Boolean(state.createEditingRecord?.recordId);
            const supplierDocumentType = normalizeSupplierDocumentType(elements.createFields.supplierDocumentType?.value || "proforma");
            setText(elements.createModalKicker, editing ? "Edición comercial" : "Area comercial");
            setText(elements.createModalTitle, editing ? "Editar línea de hardware" : "Nuevo registro de hardware");
            setText(
                elements.createModalSubtitle,
                editing
                    ? "Actualiza la línea seleccionada antes de registrar el pago al proveedor."
                    : "Registra la orden, adjunta la ODC cliente y define el documento para proveedor.");
            setText(
                elements.createModalMeta,
                editing
                    ? "La edición solo está disponible antes de registrar el pago al proveedor."
                    : supplierDocumentType === "odc-proveedor"
                        ? "Con ODC al proveedor la orden avanza directo a Pagada a proveedor."
                        : "Con proforma la orden pasa a Ok para pago a proveedor.");
            setText(elements.saveCreateBtn, editing ? "Guardar cambios" : "Guardar orden");
            const supplierTypeField = elements.createFields.supplierDocumentType?.closest(".hardware-field");
            if (supplierTypeField instanceof HTMLElement) {
                supplierTypeField.hidden = editing;
            }
            if (elements.addCreateLineBtn) {
                elements.addCreateLineBtn.hidden = editing;
                elements.addCreateLineBtn.disabled = editing || state.busy;
            }
            syncCreateSupplierDocumentCards();
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
                    const editing = Boolean(state.createEditingRecord?.recordId);
                    button.hidden = editing;
                    button.disabled = editing || rows.length <= 1 || state.busy;
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

        async function saveCommercialCreateForm() {
            if (state.createEditingRecord?.recordId) {
                await updateCommercialLine();
                return;
            }

            await createCommercialOrder();
        }

        async function updateCommercialLine() {
            if (state.saving || !elements.createForm || !config.editUrl || !state.createEditingRecord?.recordId) {
                return;
            }

            let draft;
            try {
                draft = buildEditOrderDraft();
            } catch (error) {
                setStatus(elements.createStatus, "error", getErrorMessage(error));
                return;
            }

            try {
                state.saving = true;
                setBusy(true);
                setStatus(elements.createStatus, "info", "Guardando cambios de Hardware...");
                const result = await fetchJson(buildImpersonatedUrl(config.editUrl), {
                    method: "POST",
                    body: JSON.stringify(draft.payload)
                });

                const pendingUploads = [
                    ["cr07a_ordendecompra", draft.orderFile],
                    ["cr07a_adjuntarproforma", draft.proformaFile]
                ].filter(([, file]) => file instanceof File);

                if (pendingUploads.length) {
                    setStatus(elements.createStatus, "info", "Cargando adjuntos actualizados...");
                    for (const [fieldName, file] of pendingUploads) {
                        await uploadFile(draft.fileRecordIds[fieldName] || draft.payload.recordId, fieldName, file);
                    }
                }

                resetCreateForm();
                closeCreateModal(true);
                await loadBoard();
                setStatus(elements.status, "success", result?.message || "Línea de Hardware actualizada.");
            } catch (error) {
                setStatus(elements.createStatus, "error", getErrorMessage(error));
            } finally {
                state.saving = false;
                if (state.busy) {
                    setBusy(false);
                }
            }
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
                const createResult = await fetchJson(buildImpersonatedUrl(config.createUrl), {
                    method: "POST",
                    body: JSON.stringify(draft.payload)
                });

                const records = Array.isArray(createResult?.records) ? createResult.records : [];
                if (records.length !== draft.lines.length) {
                    throw new Error("La respuesta de creación no coincide con las filas enviadas.");
                }

                setStatus(elements.createStatus, "info", "Cargando adjuntos de la orden...");
                const firstRecordId = records[0]?.recordId || "";
                if (!firstRecordId) {
                    throw new Error("No se recibió el id de la primera fila.");
                }

                await uploadFile(firstRecordId, "cr07a_ordendecompra", draft.orderFile);
                await uploadFile(firstRecordId, draft.supplierDocumentFieldName, draft.supplierDocumentFile);

                setStatus(elements.createStatus, "info", "Aplicando documentación de la orden...");
                const savePayload = {
                    recordId: records[0].recordId,
                    recordIds: records.map(record => record.recordId).filter(Boolean),
                    actionKey: "register-documentation",
                    purchaseOrderNumber: draft.payload.purchaseOrderNumber,
                    supplierDocumentType: draft.supplierDocumentType,
                    freightValue: 0,
                    documentationRows: records.map((record, index) => ({
                        recordId: record.recordId,
                        odcDateValue: draft.payload.odcDateValue,
                        supplierUnitCost: draft.lines[index].supplierUnitCost,
                        provider: draft.lines[index].provider
                    }))
                };

                const saveResult = await fetchJson(buildImpersonatedUrl(config.saveUrl), {
                    method: "POST",
                    body: JSON.stringify(savePayload)
                });

                resetCreateForm();
                closeCreateModal(true);
                await loadBoard();
                setStatus(elements.status, "success", saveResult?.message || createResult?.message || "Orden de Hardware guardada.");
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

            const orderFile = getCreateOrderFile("cr07a_ordendecompra");
            const supplierDocumentType = normalizeSupplierDocumentType(elements.createFields.supplierDocumentType?.value || "proforma");
            const supplierDocumentFieldName = supplierDocumentType === "odc-proveedor"
                ? "cr07a_odcproveedor"
                : "cr07a_adjuntarproforma";
            const supplierDocumentFile = getCreateOrderFile(supplierDocumentFieldName);
            if (!(orderFile instanceof File)) {
                throw new Error("Debes adjuntar la ODC cliente.");
            }
            if (!(supplierDocumentFile instanceof File)) {
                throw new Error(supplierDocumentType === "odc-proveedor"
                    ? "Debes adjuntar la ODC al proveedor."
                    : "Debes adjuntar la Proforma proveedor.");
            }

            const lines = readCreateLineDrafts();
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
                lines,
                orderFile,
                supplierDocumentType,
                supplierDocumentFieldName,
                supplierDocumentFile
            };
        }

        function buildEditOrderDraft() {
            const record = state.createEditingRecord;
            if (!record?.recordId) {
                throw new Error("No hay una línea de Hardware activa para editar.");
            }

            if (!isCommercialLineEditable(record)) {
                throw new Error("Solo puedes editar líneas en estado Ok para pago a proveedor.");
            }

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

            const orderFile = getCreateOrderFile("cr07a_ordendecompra");
            const proformaFile = getCreateOrderFile("cr07a_adjuntarproforma");
            if (!(orderFile instanceof File) && !hasExistingCreateFile("cr07a_ordendecompra")) {
                throw new Error("Debes adjuntar la ODC cliente.");
            }
            if (!(proformaFile instanceof File) && !hasExistingCreateFile("cr07a_adjuntarproforma")) {
                throw new Error("Debes adjuntar la Proforma proveedor.");
            }

            const lines = readCreateLineDrafts();
            if (lines.length !== 1) {
                throw new Error("La edición permite una sola línea de Hardware.");
            }

            const line = lines[0];
            return {
                payload: {
                    recordId: record.recordId,
                    purchaseOrderNumber,
                    odcDateValue,
                    clientId: state.createClientSelection.id,
                    clientName: state.createClientSelection.name,
                    name: line.name,
                    quantity: line.quantity,
                    supplierUnitCost: line.supplierUnitCost,
                    saleUnit: line.saleUnit,
                    provider: line.provider
                },
                orderFile,
                proformaFile,
                fileRecordIds: {
                    cr07a_ordendecompra: resolveCreateFileRecordId("cr07a_ordendecompra"),
                    cr07a_adjuntarproforma: resolveCreateFileRecordId("cr07a_adjuntarproforma")
                }
            };
        }

        function readCreateLineDrafts() {
            const rows = Array.from(elements.createLines?.querySelectorAll("[data-hw-create-line]") || []);
            if (!rows.length) {
                throw new Error("Agrega al menos una fila.");
            }

            return rows.map((row, index) => {
                const name = getCreateLineValue(row, "name");
                const quantity = parseIntegerStrict(getCreateLineValue(row, "quantity"));
                const supplierUnitCost = parseDecimal(getCreateLineValue(row, "supplierUnitCost"));
                const saleUnit = parseDecimal(getCreateLineValue(row, "saleUnit"));
                const provider = getCreateLineValue(row, "provider");

                if (!name) {
                    throw new Error(`Debes diligenciar Producto / referencia en la fila ${index + 1}.`);
                }
                if (!Number.isInteger(quantity) || quantity <= 0) {
                    throw new Error(`Debes diligenciar una cantidad válida en la fila ${index + 1}.`);
                }
                if (!(supplierUnitCost > 0)) {
                    throw new Error(`Debes diligenciar un costo unitario de proveedor válido en la fila ${index + 1}.`);
                }
                if (!(saleUnit > 0)) {
                    throw new Error(`Debes diligenciar una venta unidad válida en la fila ${index + 1}.`);
                }
                if (!provider) {
                    throw new Error(`Debes diligenciar proveedor en la fila ${index + 1}.`);
                }

                return {
                    rowKey: row.dataset.hwCreateLine || `line-${index + 1}`,
                    name,
                    quantity,
                    supplierUnitCost,
                    saleUnit,
                    provider
                };
            });
        }

        function resetCreateForm(options = {}) {
            const preserveEditingRecord = Boolean(options.preserveEditingRecord);
            const addBlankLine = options.addBlankLine !== false;
            elements.createForm?.reset();
            if (!preserveEditingRecord) {
                state.createEditingRecord = null;
            }
            state.createClientSelection = null;
            state.createClientSuggestions = [];
            closeCreateClientLookupMenu();
            if (elements.createLines) {
                elements.createLines.innerHTML = "";
                if (addBlankLine) {
                    addCreateLine();
                }
            }
            if (elements.createClientHint) {
                elements.createClientHint.textContent = "Busca y selecciona un cliente.";
            }
            renderCreateFormMode();
            syncCreateSupplierDocumentCards();
            renderCreateFileNames();
        }

        function getCreateLineValue(row, fieldName) {
            return (row.querySelector(`[data-hw-create-line-field="${cssEscape(fieldName)}"]`)?.value || "").trim();
        }

        function getCreateOrderFile(fieldName) {
            const input = elements.createFileInputs.find(item => item.dataset.hwCreateFileInput === fieldName);
            return input instanceof HTMLInputElement && input.files && input.files.length > 0
                ? input.files[0]
                : null;
        }

        function renderCreateFileNames() {
            syncCreateSupplierDocumentCards();
            elements.createFileNames.forEach(target => {
                const fieldName = target.dataset.hwCreateFileName || "";
                const file = getCreateOrderFile(fieldName);
                target.textContent = file instanceof File
                    ? file.name
                    : resolveExistingCreateFileName(fieldName) || "Sin archivo";
            });
        }

        function syncCreateSupplierDocumentCards() {
            const editing = Boolean(state.createEditingRecord?.recordId);
            const supplierDocumentType = editing
                ? "proforma"
                : normalizeSupplierDocumentType(elements.createFields.supplierDocumentType?.value || "proforma");

            root.querySelectorAll("[data-hw-create-file-card]").forEach(card => {
                const fieldName = card.dataset.hwCreateFileCard || "";
                if (fieldName === "cr07a_ordendecompra") {
                    card.hidden = false;
                } else if (fieldName === "cr07a_adjuntarproforma") {
                    card.hidden = supplierDocumentType !== "proforma";
                } else if (fieldName === "cr07a_odcproveedor") {
                    card.hidden = editing || supplierDocumentType !== "odc-proveedor";
                }
            });
        }

        function resolveExistingCreateFileName(fieldName) {
            return state.createEditingRecord?.recordId
                ? resolveExistingFileName(resolveCreateOrderFileRecord(fieldName), fieldName)
                : "";
        }

        function hasExistingCreateFile(fieldName) {
            return state.createEditingRecord?.recordId
                ? hasExistingFile(resolveCreateOrderFileRecord(fieldName), fieldName)
                : false;
        }

        function resolveCreateFileRecordId(fieldName) {
            const record = resolveCreateOrderFileRecord(fieldName) || state.createEditingRecord;
            return record?.recordId || "";
        }

        function resolveCreateOrderFileRecord(fieldName) {
            if (!state.createEditingRecord?.recordId) {
                return null;
            }

            const orderRows = findCommercialOrderRows(state.createEditingRecord);
            return resolveOrderFileRecord(orderRows, fieldName) || state.createEditingRecord;
        }

        function findCommercialOrderRows(record) {
            const orderNumber = normalizeText(record?.purchaseOrderNumber || "");
            if (!orderNumber) {
                return [record];
            }

            const rows = state.rows.filter(row =>
                normalizeText(row?.purchaseOrderNumber || "") === orderNumber);
            return rows.length ? rows : [record];
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
                const result = await fetchJson(buildImpersonatedUrl(config.editUrl), {
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

            if ((!elements.modal || elements.modal.hidden)
                && (!elements.createModal || elements.createModal.hidden)
                && (!elements.purchaseOrderModal || elements.purchaseOrderModal.hidden)) {
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

                const firstRecord = state.modalRecords[0];
                if (firstRecord?.recordId) {
                    await uploadFile(firstRecord.recordId, key, file);
                }
            }
        }

        async function uploadFile(recordId, fieldName, file) {
            const formData = new FormData();
            formData.append("recordId", recordId || "");
            formData.append("fieldName", fieldName || "");
            formData.append("file", file);

            await fetchJson(buildImpersonatedUrl(config.uploadUrl), {
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
            const supplierDocumentType = normalizeSupplierDocumentType(elements.fields.supplierDocumentType?.value || "proforma");
            const supplierDocumentFieldName = supplierDocumentType === "odc-proveedor"
                ? "cr07a_odcproveedor"
                : "cr07a_adjuntarproforma";
            if (!purchaseOrderNumber) {
                throw new Error("Debes diligenciar cr07a_noorden.");
            }
            if (freightValueRaw && freightValue < 0) {
                throw new Error("Debes diligenciar un cr07a_valorflete válido.");
            }
            if (isCommercialMode && !commonOdcDate) {
                throw new Error("Debes diligenciar cr07a_fechaodc.");
            }
            if (!hasOrderDocumentationFileOrPending("cr07a_ordendecompra")) {
                throw new Error("Debes cargar la ODC cliente.");
            }
            if (!hasOrderDocumentationFileOrPending(supplierDocumentFieldName)) {
                throw new Error(supplierDocumentType === "odc-proveedor"
                    ? "Debes cargar la ODC al proveedor."
                    : "Debes cargar la Proforma proveedor.");
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
                supplierDocumentType,
                freightValue: freightValueRaw ? freightValue : 0,
                documentationRows
            };
        }

        function hasOrderDocumentationFileOrPending(fieldName) {
            return state.pendingFiles[fieldName] instanceof File
                || state.modalRecords.some(record => hasExistingFile(record, fieldName));
        }

        function hasGlobalFileOrPending(fieldName) {
            if (state.pendingFiles[fieldName] instanceof File) {
                return true;
            }

            const orderFileRecord = resolveOrderFileRecord(state.modalRecords, fieldName);
            return hasExistingFile(orderFileRecord, fieldName);
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
                case "cr07a_odcproveedor":
                    return Boolean(record.hasSupplierPurchaseOrder);
                case "cr07a_pagoaproveedor":
                    return Boolean(record.hasSupplierPaymentProof);
                case "cr07a_actadeentrega":
                    return Boolean(record.hasDeliveryRecord);
                default:
                    return false;
            }
        }

        function resolveOrderFileRecord(records, fieldName) {
            const items = Array.isArray(records) ? records : [];
            const directRecord = items.find(record => hasExistingFile(record, fieldName));
            if (directRecord || !isOrderDocumentationFile(fieldName)) {
                return directRecord || items[0] || null;
            }

            const orderNumbers = new Set(items
                .map(record => normalizeText(record?.purchaseOrderNumber || ""))
                .filter(Boolean));
            if (orderNumbers.size > 0) {
                const orderRecord = state.rows.find(record =>
                    orderNumbers.has(normalizeText(record?.purchaseOrderNumber || ""))
                    && hasExistingFile(record, fieldName));
                if (orderRecord) {
                    return orderRecord;
                }
            }

            return items[0] || null;
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
                case "cr07a_odcproveedor":
                    return record.supplierPurchaseOrderFileName || "";
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
                    return "Adjuntar ODC cliente";
                case "cr07a_adjuntarproforma":
                    return "Adjuntar Proforma proveedor";
                case "cr07a_odcproveedor":
                    return "Adjuntar ODC al proveedor";
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
            appendImpersonationParam(url);

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
            appendImpersonationParam(url);
            return `${url.pathname}${url.search}`;
        }

        function buildImpersonatedUrl(rawUrl) {
            if (!rawUrl) {
                return rawUrl;
            }

            const url = new URL(rawUrl, window.location.origin);
            appendImpersonationParam(url);
            return `${url.pathname}${url.search}`;
        }

        function appendImpersonationParam(url) {
            if (state.impersonatedOwnerId) {
                url.searchParams.set("impersonatedOwnerId", state.impersonatedOwnerId);
            } else {
                url.searchParams.delete("impersonatedOwnerId");
            }
        }

        function buildGroupKey(row) {
            return `${normalizeText(row?.ownerId || row?.ownerName || "sin-owner")}|${Number(row?.stateValue || 0)}|${normalizeText(row?.purchaseOrderNumber || "")}`;
        }

        function normalizeSupplierDocumentType(value) {
            const normalized = normalizeText(value || "").replace(/_/g, "-");
            if (!normalized || normalized === "proforma") {
                return "proforma";
            }

            if (normalized === "odc-proveedor"
                || normalized === "supplier-odc"
                || normalized === "odc-al-proveedor"
                || normalized === "orden-proveedor"
                || normalized === "orden-de-compra-proveedor") {
                return "odc-proveedor";
            }

            return "proforma";
        }

        function resolveSupplierDocumentField(row) {
            const supplierDocumentType = normalizeSupplierDocumentType(row?.supplierDocumentType || "");
            if (supplierDocumentType === "odc-proveedor"
                || (!row?.hasProforma && row?.hasSupplierPurchaseOrder)) {
                return "cr07a_odcproveedor";
            }

            return "cr07a_adjuntarproforma";
        }

        function isOrderDocumentationFile(fieldName) {
            return fieldName === "cr07a_ordendecompra"
                || fieldName === "cr07a_adjuntarproforma"
                || fieldName === "cr07a_odcproveedor";
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

        function buildSystemUserLabel(user) {
            return user?.name || user?.email || user?.id || "Usuario";
        }

        function isSupplierPaymentEffectiveUser() {
            const email = state.impersonatedOwnerId
                ? state.impersonatedOwnerEmail
                : config.currentUserEmail;
            return Boolean(config.supplierPaymentEmail)
                && normalizeText(email) === normalizeText(config.supplierPaymentEmail);
        }

        function canCreateCommercialRecords() {
            return isCommercialMode
                && config.allowCreate
                && !isSupplierPaymentEffectiveUser();
        }

        function syncAccessControls() {
            if (elements.openCreateModalBtn) {
                const canCreate = canCreateCommercialRecords();
                elements.openCreateModalBtn.hidden = !canCreate;
                elements.openCreateModalBtn.disabled = state.busy || !canCreate;
            }

            if (elements.openPurchaseOrderModalBtn) {
                const canCreate = canCreateCommercialRecords();
                elements.openPurchaseOrderModalBtn.hidden = !canCreate;
                elements.openPurchaseOrderModalBtn.disabled = state.busy || !canCreate;
            }

            if (elements.impersonationReset) {
                elements.impersonationReset.disabled = state.busy || !state.impersonatedOwnerId;
            }
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
                elements.impersonationSelect,
                elements.impersonationReset,
                elements.selectAll,
                elements.selectedActionBtn,
                elements.editSelectedBtn,
                elements.openCreateModalBtn,
                elements.openPurchaseOrderModalBtn,
                elements.addCreateLineBtn,
                elements.saveCreateBtn,
                elements.addPurchaseOrderLineBtn,
                elements.submitPurchaseOrderBtn
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

            elements.closeCreateModalButtons.forEach(button => {
                button.disabled = isBusy;
            });

            elements.closePurchaseOrderModalButtons.forEach(button => {
                button.disabled = isBusy;
            });

            elements.editForm?.querySelectorAll("input, select, textarea, button").forEach(element => {
                element.disabled = isBusy;
            });

            elements.createForm?.querySelectorAll("input, select, textarea, button").forEach(element => {
                element.disabled = isBusy;
            });

            elements.purchaseOrderForm?.querySelectorAll("input, select, textarea, button").forEach(element => {
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
            renderCreateFormMode();
            syncCreateLineButtons();
            syncPurchaseOrderLineButtons();
            syncAccessControls();
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

        function parseDecimalStrict(value) {
            let normalized = String(value || "").trim().replace(/\s/g, "");
            if (!normalized) {
                return Number.NaN;
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
            return Number.isFinite(parsed) ? parsed : Number.NaN;
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

        function normalizeGuid(value) {
            return String(value || "")
                .replaceAll("{", "")
                .replaceAll("}", "")
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
