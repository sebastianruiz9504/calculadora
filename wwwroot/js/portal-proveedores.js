(function () {
    const app = document.getElementById("portalProveedoresApp");
    if (!app) {
        return;
    }

    const flowConfigured = app.dataset.flowConfigured === "true";
    const requestSubject = document.getElementById("requestSubject");
    const requestBody = document.getElementById("requestBody");
    const sendRequestBtn = document.getElementById("sendRequestBtn");
    const requestStatus = document.getElementById("requestStatus");

    const startDateInput = document.getElementById("startDate");
    const endDateInput = document.getElementById("endDate");
    const supplierSearchInput = document.getElementById("supplierSearchInput");
    const supplierNitInput = document.getElementById("supplierNit");
    const supplierResults = document.getElementById("supplierResults");
    const supplierHelper = document.getElementById("supplierHelper");
    const reloadProvidersBtn = document.getElementById("reloadProvidersBtn");
    const selectedSupplierChipWrap = document.getElementById("selectedSupplierChipWrap");
    const selectedSupplierChip = document.getElementById("selectedSupplierChip");
    const certificateTypeButton = document.getElementById("certificateTypeButton");
    const certificateTypeInputs = Array.from(document.querySelectorAll(".certificate-type-input"));
    const searchSummaryBtn = document.getElementById("searchSummaryBtn");
    const clearFiltersBtn = document.getElementById("clearFiltersBtn");
    const searchStatus = document.getElementById("searchStatus");
    const summarySection = document.getElementById("summarySection");
    const summarySupplier = document.getElementById("summarySupplier");
    const summarySupplierNit = document.getElementById("summarySupplierNit");
    const summaryPeriod = document.getElementById("summaryPeriod");
    const summaryTypes = document.getElementById("summaryTypes");
    const summaryInvoices = document.getElementById("summaryInvoices");
    const summaryBase = document.getElementById("summaryBase");
    const summaryReteFuente = document.getElementById("summaryReteFuente");
    const summaryReteIca = document.getElementById("summaryReteIca");
    const summaryRecords = document.getElementById("summaryRecords");
    const reteFuenteCard = document.getElementById("reteFuenteCard");
    const reteIcaCard = document.getElementById("reteIcaCard");
    const tableSection = document.getElementById("tableSection");
    const resultsTableHead = document.getElementById("resultsTableHead");
    const resultsTableBody = document.getElementById("resultsTableBody");
    const resultsTableFoot = document.getElementById("resultsTableFoot");
    const emitCertificateBtn = document.getElementById("emitCertificateBtn");

    const moneyFormatter = new Intl.NumberFormat("es-CO", {
        style: "currency",
        currency: "COP",
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    });

    const state = {
        providers: [],
        providerRangeKey: "",
        selectedProvider: null,
        summary: null,
        loadingProviders: false,
        loadingSummary: false,
        sendingRequest: false
    };

    function escapeHtml(value) {
        return (value ?? "").toString()
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function formatMoney(value) {
        return moneyFormatter.format(Number(value || 0));
    }

    function setStatus(target, type, message) {
        if (!target) {
            return;
        }

        if (!message) {
            target.className = "portal-status";
            target.textContent = "";
            return;
        }

        target.className = `portal-status show ${type}`;
        target.textContent = message;
    }

    async function getErrorMessage(response, fallbackMessage) {
        const text = await response.text();
        return text || fallbackMessage;
    }

    function getDateRangeKey() {
        return `${startDateInput.value}|${endDateInput.value}`;
    }

    function hasValidDateRange() {
        return Boolean(startDateInput.value && endDateInput.value && endDateInput.value >= startDateInput.value);
    }

    function clearSelectedProvider(preserveInput) {
        state.selectedProvider = null;
        supplierNitInput.value = "";
        selectedSupplierChipWrap.hidden = true;
        selectedSupplierChip.textContent = "";

        if (!preserveInput) {
            supplierSearchInput.value = "";
        }
    }

    function resetResults() {
        state.summary = null;
        summarySection.hidden = true;
        tableSection.hidden = true;
        emitCertificateBtn.disabled = true;
        resultsTableHead.innerHTML = "";
        resultsTableBody.innerHTML = "";
        resultsTableFoot.innerHTML = "";
    }

    function updateCertificateTypeButton() {
        const selectedTypes = getSelectedCertificateTypes();
        if (selectedTypes.length === 0) {
            certificateTypeButton.textContent = "Selecciona al menos uno";
            return;
        }

        const labels = selectedTypes.map(value => value === "retefuente" ? "Rete fuente" : "Rete ICA");
        certificateTypeButton.textContent = labels.join(" + ");
    }

    function getSelectedCertificateTypes() {
        return certificateTypeInputs
            .filter(input => input.checked)
            .map(input => input.value);
    }

    function hideProviderMenu() {
        supplierResults.classList.remove("show");
        supplierResults.innerHTML = "";
    }

    function selectProvider(item) {
        state.selectedProvider = item;
        supplierSearchInput.value = item.name || item.nit || "";
        supplierNitInput.value = item.nit || "";
        selectedSupplierChip.textContent = `${item.name || "Proveedor"} · ${item.nit || "Sin NIT"}`;
        selectedSupplierChipWrap.hidden = false;
        hideProviderMenu();
    }

    function renderProviderMenu(filterText) {
        if (!Array.isArray(state.providers) || state.providers.length === 0) {
            supplierResults.innerHTML = [
                "<div class=\"portal-lookup-option\">",
                "<div class=\"portal-lookup-option__title\">Sin proveedores</div>",
                "<div class=\"portal-lookup-option__sub\">No se encontraron resultados para el rango seleccionado.</div>",
                "</div>"
            ].join("");
            supplierResults.classList.add("show");
            return;
        }

        const normalizedFilter = (filterText || "").trim().toLowerCase();
        const items = state.providers
            .filter(item => {
                if (!normalizedFilter) {
                    return true;
                }

                return (item.name || "").toLowerCase().includes(normalizedFilter)
                    || (item.nit || "").toLowerCase().includes(normalizedFilter);
            })
            .slice(0, 80);

        if (items.length === 0) {
            supplierResults.innerHTML = [
                "<div class=\"portal-lookup-option\">",
                "<div class=\"portal-lookup-option__title\">Sin coincidencias</div>",
                "<div class=\"portal-lookup-option__sub\">Ajusta el nombre o el NIT.</div>",
                "</div>"
            ].join("");
            supplierResults.classList.add("show");
            return;
        }

        supplierResults.innerHTML = items.map(item => [
            `<div class="portal-lookup-option" data-nit="${escapeHtml(item.nit || "")}" data-name="${escapeHtml(item.name || "")}">`,
            `<div class="portal-lookup-option__title">${escapeHtml(item.name || "Proveedor sin nombre")}</div>`,
            `<div class="portal-lookup-option__sub">NIT: ${escapeHtml(item.nit || "Sin NIT")}</div>`,
            "</div>"
        ].join("")).join("");
        supplierResults.classList.add("show");

        supplierResults.querySelectorAll(".portal-lookup-option[data-nit]").forEach(option => {
            option.addEventListener("mousedown", event => {
                event.preventDefault();
                selectProvider({
                    nit: option.dataset.nit || "",
                    name: option.dataset.name || ""
                });
            });
        });
    }

    async function loadProviders(force) {
        if (!hasValidDateRange()) {
            throw new Error("Selecciona un rango de fechas válido antes de cargar proveedores.");
        }

        const rangeKey = getDateRangeKey();
        if (!force && state.providerRangeKey === rangeKey && state.providers.length > 0) {
            return;
        }

        state.loadingProviders = true;
        reloadProvidersBtn.disabled = true;
        supplierHelper.textContent = "Consultando proveedores en Dataverse...";

        try {
            const url = `/PortalProveedores/Providers?startDate=${encodeURIComponent(startDateInput.value)}&endDate=${encodeURIComponent(endDateInput.value)}`;
            const response = await fetch(url, { headers: { "Accept": "application/json" } });
            if (!response.ok) {
                throw new Error(await getErrorMessage(response, "No fue posible cargar proveedores."));
            }

            state.providers = await response.json();
            state.providerRangeKey = rangeKey;
            supplierHelper.textContent = `${state.providers.length} proveedores únicos cargados para el rango seleccionado.`;
        } finally {
            state.loadingProviders = false;
            reloadProvidersBtn.disabled = false;
        }
    }

    function renderSummary(data) {
        const selectedTypes = getSelectedCertificateTypes();
        const showReteFuente = selectedTypes.includes("retefuente");
        const showReteIca = selectedTypes.includes("reteica");
        const records = Array.isArray(data.records) ? data.records : [];

        summarySection.hidden = false;
        summarySupplier.textContent = data.supplierName || "Proveedor sin nombre";
        summarySupplierNit.textContent = `NIT: ${data.supplierNit || "Sin NIT"}`;
        summaryPeriod.textContent = data.periodLabel || "-";
        summaryTypes.textContent = data.certificateTypesLabel || "-";
        summaryInvoices.textContent = formatMoney(data.totalInvoices);
        summaryBase.textContent = formatMoney(data.totalBase);
        summaryReteFuente.textContent = formatMoney(data.totalReteFuente);
        summaryReteIca.textContent = formatMoney(data.totalReteIca);
        summaryRecords.textContent = `${data.recordsCount || 0} filas`;
        reteFuenteCard.hidden = !showReteFuente;
        reteIcaCard.hidden = !showReteIca;

        if (!records.length) {
            tableSection.hidden = false;
            resultsTableHead.innerHTML = "";
            resultsTableBody.innerHTML = "<tr><td colspan=\"7\" class=\"portal-empty\">No se encontraron filas para el proveedor y rango consultados.</td></tr>";
            resultsTableFoot.innerHTML = "";
            emitCertificateBtn.disabled = true;
            return;
        }

        const headerCells = [
            "<th>Fecha</th>",
            "<th>Proveedor</th>",
            "<th>NIT</th>",
            "<th class=\"text-end\">Total facturas</th>",
            "<th class=\"text-end\">Total base</th>"
        ];

        if (showReteFuente) {
            headerCells.push("<th class=\"text-end\">Total rete fuente</th>");
        }

        if (showReteIca) {
            headerCells.push("<th class=\"text-end\">Total rete ICA</th>");
        }

        resultsTableHead.innerHTML = headerCells.join("");
        resultsTableBody.innerHTML = records.map(record => {
            const cells = [
                `<td>${escapeHtml(record.expenseDateDisplay || record.expenseDateValue || "")}</td>`,
                `<td><div class="fw-semibold">${escapeHtml(record.supplierName || "")}</div></td>`,
                `<td>${escapeHtml(record.supplierNit || "")}</td>`,
                `<td class="text-end">${formatMoney(record.totalInvoices)}</td>`,
                `<td class="text-end">${formatMoney(record.totalBase)}</td>`
            ];

            if (showReteFuente) {
                cells.push(`<td class="text-end">${formatMoney(record.totalReteFuente)}</td>`);
            }

            if (showReteIca) {
                cells.push(`<td class="text-end">${formatMoney(record.totalReteIca)}</td>`);
            }

            return `<tr>${cells.join("")}</tr>`;
        }).join("");

        const totalCells = [
            "<th colspan=\"3\" class=\"text-end\">Totales</th>",
            `<th class="text-end">${formatMoney(data.totalInvoices)}</th>`,
            `<th class="text-end">${formatMoney(data.totalBase)}</th>`
        ];

        if (showReteFuente) {
            totalCells.push(`<th class="text-end">${formatMoney(data.totalReteFuente)}</th>`);
        }

        if (showReteIca) {
            totalCells.push(`<th class="text-end">${formatMoney(data.totalReteIca)}</th>`);
        }

        resultsTableFoot.innerHTML = `<tr>${totalCells.join("")}</tr>`;
        tableSection.hidden = false;
        emitCertificateBtn.disabled = false;
    }

    async function submitCertificateRequest() {
        if (state.sendingRequest) {
            return;
        }

        const subject = requestSubject.value.trim();
        const body = requestBody.value.trim();

        if (!subject) {
            setStatus(requestStatus, "error", "Debes diligenciar el asunto.");
            requestSubject.focus();
            return;
        }

        if (!body) {
            setStatus(requestStatus, "error", "Debes diligenciar el cuerpo.");
            requestBody.focus();
            return;
        }

        if (!flowConfigured) {
            setStatus(requestStatus, "warning", "Configura primero la URL del flujo en appsettings.json.");
            return;
        }

        state.sendingRequest = true;
        sendRequestBtn.disabled = true;
        setStatus(requestStatus, "info", "Enviando solicitud al flujo de Power Automate...");

        try {
            const response = await fetch("/PortalProveedores/RequestCertificates", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json"
                },
                body: JSON.stringify({ subject, body })
            });

            if (!response.ok) {
                throw new Error(await getErrorMessage(response, "No fue posible enviar la solicitud."));
            }

            setStatus(requestStatus, "success", "La solicitud fue enviada correctamente al flujo.");
        } catch (error) {
            setStatus(requestStatus, "error", error && error.message ? error.message : "No fue posible enviar la solicitud.");
        } finally {
            state.sendingRequest = false;
            sendRequestBtn.disabled = !flowConfigured;
        }
    }

    async function searchSummary() {
        if (state.loadingSummary) {
            return;
        }

        if (!hasValidDateRange()) {
            setStatus(searchStatus, "error", "Selecciona un rango de fechas válido.");
            return;
        }

        if (!state.selectedProvider || !state.selectedProvider.nit) {
            setStatus(searchStatus, "error", "Debes seleccionar un proveedor del dropdown.");
            supplierSearchInput.focus();
            return;
        }

        const selectedTypes = getSelectedCertificateTypes();
        if (selectedTypes.length === 0) {
            setStatus(searchStatus, "error", "Selecciona al menos un tipo de certificado.");
            return;
        }

        state.loadingSummary = true;
        searchSummaryBtn.disabled = true;
        emitCertificateBtn.disabled = true;
        setStatus(searchStatus, "info", "Consultando movimientos y calculando totales...");

        try {
            const params = new URLSearchParams();
            params.set("startDate", startDateInput.value);
            params.set("endDate", endDateInput.value);
            params.set("supplierNit", state.selectedProvider.nit);
            params.set("supplierName", state.selectedProvider.name || "");
            selectedTypes.forEach(type => params.append("certificateTypes", type));

            const response = await fetch(`/PortalProveedores/Summary?${params.toString()}`, {
                headers: { "Accept": "application/json" }
            });

            if (!response.ok) {
                throw new Error(await getErrorMessage(response, "No fue posible consultar la información."));
            }

            state.summary = await response.json();
            renderSummary(state.summary);
            setStatus(searchStatus, "success", "Información consultada correctamente.");
        } catch (error) {
            resetResults();
            setStatus(searchStatus, "error", error && error.message ? error.message : "No fue posible consultar la información.");
        } finally {
            state.loadingSummary = false;
            searchSummaryBtn.disabled = false;
        }
    }

    function clearAll() {
        startDateInput.value = "";
        endDateInput.value = "";
        supplierSearchInput.value = "";
        supplierNitInput.value = "";
        state.providers = [];
        state.providerRangeKey = "";
        clearSelectedProvider(false);
        resetResults();
        certificateTypeInputs.forEach(input => {
            input.checked = false;
        });
        updateCertificateTypeButton();
        hideProviderMenu();
        setStatus(searchStatus, "", "");
        supplierHelper.textContent = "Primero selecciona el rango de fechas para cargar proveedores.";
    }

    function handleRangeChange() {
        state.providers = [];
        state.providerRangeKey = "";
        clearSelectedProvider(false);
        resetResults();
        hideProviderMenu();
        setStatus(searchStatus, "", "");

        if (!startDateInput.value || !endDateInput.value) {
            supplierHelper.textContent = "Primero selecciona el rango de fechas para cargar proveedores.";
            return;
        }

        if (endDateInput.value < startDateInput.value) {
            supplierHelper.textContent = "La fecha final no puede ser menor que la inicial.";
            return;
        }

        supplierHelper.textContent = "Enfoca el campo proveedor o pulsa Actualizar lista para cargar los resultados del rango.";
    }

    function buildCertificateUrl() {
        if (!state.selectedProvider || !state.selectedProvider.nit) {
            return "";
        }

        const params = new URLSearchParams();
        params.set("startDate", startDateInput.value);
        params.set("endDate", endDateInput.value);
        params.set("supplierNit", state.selectedProvider.nit);
        params.set("supplierName", state.selectedProvider.name || "");
        getSelectedCertificateTypes().forEach(type => params.append("certificateTypes", type));
        params.set("autoprint", "1");
        return `/PortalProveedores/Certificate?${params.toString()}`;
    }

    sendRequestBtn.addEventListener("click", submitCertificateRequest);
    startDateInput.addEventListener("change", handleRangeChange);
    endDateInput.addEventListener("change", handleRangeChange);

    reloadProvidersBtn.addEventListener("click", async () => {
        try {
            await loadProviders(true);
            renderProviderMenu(supplierSearchInput.value.trim());
        } catch (error) {
            setStatus(searchStatus, "error", error && error.message ? error.message : "No fue posible cargar proveedores.");
        }
    });

    supplierSearchInput.addEventListener("focus", async () => {
        if (!hasValidDateRange()) {
            return;
        }

        try {
            await loadProviders(false);
            renderProviderMenu(supplierSearchInput.value.trim());
        } catch (error) {
            setStatus(searchStatus, "error", error && error.message ? error.message : "No fue posible cargar proveedores.");
        }
    });

    supplierSearchInput.addEventListener("input", async () => {
        if (state.selectedProvider && supplierSearchInput.value.trim() !== (state.selectedProvider.name || "").trim()) {
            clearSelectedProvider(true);
        }

        if (!hasValidDateRange()) {
            return;
        }

        try {
            await loadProviders(false);
            renderProviderMenu(supplierSearchInput.value.trim());
        } catch (error) {
            setStatus(searchStatus, "error", error && error.message ? error.message : "No fue posible cargar proveedores.");
        }
    });

    supplierSearchInput.addEventListener("blur", () => {
        window.setTimeout(hideProviderMenu, 150);
    });

    certificateTypeInputs.forEach(input => {
        input.addEventListener("change", () => {
            updateCertificateTypeButton();
            if (state.summary) {
                renderSummary(state.summary);
            }
        });
    });

    clearFiltersBtn.addEventListener("click", clearAll);
    searchSummaryBtn.addEventListener("click", searchSummary);

    emitCertificateBtn.addEventListener("click", () => {
        const url = buildCertificateUrl();
        if (!url) {
            setStatus(searchStatus, "error", "Debes consultar la información antes de emitir el certificado.");
            return;
        }

        window.open(url, "_blank", "noopener");
    });

    document.addEventListener("click", event => {
        if (!event.target.closest(".portal-lookup")) {
            hideProviderMenu();
        }
    });

    updateCertificateTypeButton();
})();
