(function () {
    const app = document.getElementById("hardwareImportApp");
    if (!app) {
        return;
    }

    const previewUrl = app.dataset.previewUrl || "";
    const provisionUrl = app.dataset.provisionUrl || "";

    const fileInput = document.getElementById("hardwareCsvFile");
    const analyzeBtn = document.getElementById("hardwareAnalyzeBtn");
    const provisionBtn = document.getElementById("hardwareProvisionBtn");
    const status = document.getElementById("hardwareStatus");
    const summary = document.getElementById("hardwareSummary");
    const summaryList = document.getElementById("hardwareSummaryList");
    const columnsWrap = document.getElementById("hardwareColumnsWrap");
    const columnsBody = document.getElementById("hardwareColumnsBody");
    const systemColumnsNote = document.getElementById("hardwareSystemColumnsNote");
    const provisionWrap = document.getElementById("hardwareProvisionWrap");
    const provisionList = document.getElementById("hardwareProvisionList");

    if (!fileInput || !analyzeBtn || !provisionBtn || !status || !summary || !summaryList || !columnsWrap || !columnsBody || !systemColumnsNote || !provisionWrap || !provisionList) {
        return;
    }

    const state = {
        busy: false,
        preview: null
    };

    analyzeBtn.addEventListener("click", previewCsv);
    provisionBtn.addEventListener("click", provisionCsv);
    fileInput.addEventListener("change", handleFileChange);

    function handleFileChange() {
        state.preview = null;
        provisionBtn.disabled = true;
        hideProvisionResult();
        hidePreview();
        clearStatus();
    }

    async function previewCsv() {
        const file = fileInput.files && fileInput.files.length > 0 ? fileInput.files[0] : null;
        if (!file) {
            showStatus("warning", "Selecciona un archivo CSV antes de analizar.");
            return;
        }

        try {
            setBusy(true);
            hideProvisionResult();
            showStatus("info", "Analizando estructura del CSV...");

            const formData = new FormData();
            formData.append("file", file);
            const result = await fetchJson(previewUrl, {
                method: "POST",
                body: formData
            });

            state.preview = result;
            renderPreview(result);
            showStatus("success", result.message || "Vista previa lista.");
        } catch (error) {
            state.preview = null;
            hidePreview();
            hideProvisionResult();
            showStatus("danger", getErrorMessage(error));
        } finally {
            setBusy(false);
        }
    }

    async function provisionCsv() {
        const file = fileInput.files && fileInput.files.length > 0 ? fileInput.files[0] : null;
        if (!file) {
            showStatus("warning", "Selecciona un archivo CSV antes de importar.");
            return;
        }

        if (!state.preview) {
            showStatus("warning", "Analiza primero el archivo para confirmar el esquema.");
            return;
        }

        try {
            setBusy(true);
            showStatus("info", "Creando tabla y columnas en Dataverse. Luego se importaran las filas nuevas...");

            const formData = new FormData();
            formData.append("file", file);
            const result = await fetchJson(provisionUrl, {
                method: "POST",
                body: formData
            });

            renderProvisionResult(result);
            showStatus("success", result.message || "Carga completada.");
        } catch (error) {
            hideProvisionResult();
            showStatus("danger", getErrorMessage(error));
        } finally {
            setBusy(false);
        }
    }

    function renderPreview(result) {
        const columns = Array.isArray(result?.columns) ? result.columns : [];
        const systemColumns = Array.isArray(result?.systemColumns) ? result.systemColumns : [];

        summaryList.innerHTML = [
            `<li><strong>Archivo:</strong> ${escapeHtml(result?.fileName || "")}</li>`,
            `<li><strong>Tabla objetivo:</strong> <code>${escapeHtml(result?.tableLogicalName || "")}</code></li>`,
            `<li><strong>Separador detectado:</strong> ${escapeHtml(result?.detectedDelimiterLabel || "-")}</li>`,
            `<li><strong>Filas detectadas:</strong> ${formatNumber(result?.totalRows || 0)}</li>`,
            `<li><strong>Columnas del CSV:</strong> ${formatNumber(result?.totalColumns || columns.length)}</li>`
        ].join("");
        summary.hidden = false;

        systemColumnsNote.textContent = systemColumns.length > 0
            ? `Campos tecnicos adicionales: ${systemColumns.join(", ")}`
            : "";

        columnsBody.innerHTML = columns.map((column) => `
            <tr>
                <td>${escapeHtml(column.sourceHeader || column.displayLabel || "")}</td>
                <td><code>${escapeHtml(column.logicalName || "")}</code></td>
                <td>${escapeHtml(column.dataverseType || "")}</td>
                <td>${escapeHtml(column.exampleValue || "-")}</td>
            </tr>
        `).join("");

        columnsWrap.hidden = columns.length === 0;
        provisionBtn.disabled = columns.length === 0 || Number(result?.totalRows || 0) === 0;
    }

    function hidePreview() {
        summary.hidden = true;
        columnsWrap.hidden = true;
        summaryList.innerHTML = "";
        columnsBody.innerHTML = "";
        systemColumnsNote.textContent = "";
    }

    function renderProvisionResult(result) {
        provisionList.innerHTML = [
            `<li><strong>Tabla:</strong> <code>${escapeHtml(result?.tableLogicalName || "")}</code></li>`,
            `<li><strong>Entity set:</strong> <code>${escapeHtml(result?.entitySetName || "")}</code></li>`,
            `<li><strong>Tabla creada:</strong> ${result?.tableCreated ? "Si" : "No"}</li>`,
            `<li><strong>Columnas nuevas:</strong> ${formatNumber(result?.createdColumnsCount || 0)}</li>`,
            `<li><strong>Columnas reutilizadas:</strong> ${formatNumber(result?.existingColumnsCount || 0)}</li>`,
            `<li><strong>Filas importadas:</strong> ${formatNumber(result?.importedCount || 0)}</li>`,
            `<li><strong>Filas duplicadas omitidas:</strong> ${formatNumber(result?.skippedDuplicatesCount || 0)}</li>`
        ].join("");

        provisionWrap.hidden = false;
    }

    function hideProvisionResult() {
        provisionWrap.hidden = true;
        provisionList.innerHTML = "";
    }

    function setBusy(value) {
        state.busy = value;
        fileInput.disabled = value;
        analyzeBtn.disabled = value;
        provisionBtn.disabled = value || !state.preview || Number(state.preview?.totalRows || 0) === 0;
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

    function showStatus(kind, message) {
        status.innerHTML = `<div class="alert alert-${kind} mb-0">${escapeHtml(message || "")}</div>`;
    }

    function clearStatus() {
        status.innerHTML = "";
    }

    function formatNumber(value) {
        return new Intl.NumberFormat("es-CO").format(Number(value || 0));
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#39;");
    }
})();
