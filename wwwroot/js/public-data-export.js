(function () {
    const publicApp = document.getElementById("publicDataExportApp");
    if (publicApp) {
        initPublicPortal(publicApp);
    }

    const adminApp = document.getElementById("publicDataExportAdmin");
    if (adminApp) {
        initAdmin(adminApp);
    }

    function initPublicPortal(root) {
        const datasetSelect = root.querySelector("[data-pde-dataset]");
        const previewButton = root.querySelector("[data-pde-preview]");
        const downloadLink = root.querySelector("[data-pde-download]");
        const status = root.querySelector("[data-pde-status]");
        const table = root.querySelector("[data-pde-table]");

        if (!datasetSelect || !previewButton || !downloadLink || !table) {
            return;
        }

        function buildUrl(baseUrl) {
            const url = new URL(baseUrl, window.location.origin);
            url.searchParams.set("dataset", datasetSelect.value || root.dataset.defaultDataset || "");
            return url.toString();
        }

        function updateDownloadLink() {
            downloadLink.href = buildUrl(root.dataset.downloadUrl || "");
        }

        async function loadPreview() {
            const url = buildUrl(root.dataset.previewUrl || "");
            setStatus("Cargando vista previa...", false);
            previewButton.disabled = true;
            try {
                const response = await fetch(url, {
                    headers: {
                        "Accept": "application/json"
                    }
                });

                if (!response.ok) {
                    throw new Error(await readError(response));
                }

                const payload = await response.json();
                renderTable(table, payload);
                setStatus(payload.message || "Vista previa cargada.", false);
            } catch (error) {
                renderTable(table, { columns: [], rows: [] });
                setStatus(error.message || "No fue posible cargar la vista previa.", true);
            } finally {
                previewButton.disabled = false;
            }
        }

        datasetSelect.addEventListener("change", function () {
            updateDownloadLink();
            loadPreview();
        });

        previewButton.addEventListener("click", loadPreview);
        downloadLink.addEventListener("click", function (event) {
            updateDownloadLink();
            if (!downloadLink.href || downloadLink.getAttribute("href") === "#") {
                event.preventDefault();
            }
        });

        updateDownloadLink();
        loadPreview();
    }

    function initAdmin(root) {
        root.querySelectorAll("[data-pde-admin-dropdown]").forEach(function (container) {
            const counter = container.querySelector("[data-pde-selected-count]");
            const menu = container.querySelector(".pde-column-menu");
            const inputs = Array.from(container.querySelectorAll("input[type='checkbox']"));

            function updateCounter() {
                if (counter) {
                    counter.textContent = String(inputs.filter(function (input) { return input.checked; }).length);
                }
            }

            if (menu) {
                menu.addEventListener("click", function (event) {
                    event.stopPropagation();
                });
            }

            inputs.forEach(function (input) {
                input.addEventListener("change", updateCounter);
            });

            updateCounter();
        });
    }

    function renderTable(table, payload) {
        const thead = table.querySelector("thead");
        const tbody = table.querySelector("tbody");
        const columns = payload && Array.isArray(payload.columns) ? payload.columns : [];
        const rows = payload && Array.isArray(payload.rows) ? payload.rows : [];

        if (!columns.length) {
            thead.innerHTML = "";
            tbody.innerHTML = "<tr><td>Sin columnas aprobadas.</td></tr>";
            return;
        }

        thead.innerHTML = "<tr>" + columns.map(function (column) {
            return "<th>" + escapeHtml(column.label || "") + "</th>";
        }).join("") + "</tr>";

        if (!rows.length) {
            tbody.innerHTML = "<tr><td colspan=\"" + columns.length + "\">Sin registros para mostrar.</td></tr>";
            return;
        }

        tbody.innerHTML = rows.map(function (row) {
            const cells = row.cells || {};
            return "<tr>" + columns.map(function (column) {
                const cell = cells[column.key] || {};
                return "<td>" + escapeHtml(cell.displayValue || "") + "</td>";
            }).join("") + "</tr>";
        }).join("");
    }

    async function readError(response) {
        const contentType = response.headers.get("content-type") || "";
        if (contentType.includes("application/json")) {
            const payload = await response.json().catch(function () { return null; });
            return (payload && (payload.message || payload.detail)) || "La solicitud no fue aceptada.";
        }

        return await response.text() || "La solicitud no fue aceptada.";
    }

    function setStatus(message, isError) {
        const status = document.querySelector("[data-pde-status]");
        if (!status) {
            return;
        }

        status.textContent = message || "";
        status.classList.toggle("is-visible", Boolean(message));
        status.classList.toggle("is-error", Boolean(isError));
    }

    function escapeHtml(value) {
        return String(value)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }
})();
