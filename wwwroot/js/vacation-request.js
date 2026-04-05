(function () {
    const app = document.getElementById("vacationRequestApp");
    if (!app) {
        return;
    }

    const contextUrl = app.dataset.contextUrl || "";
    const submitUrl = app.dataset.submitUrl || "";
    const documentUrl = app.dataset.documentUrl || "";
    const downloadUrl = app.dataset.downloadUrl || "";
    const tableKey = app.dataset.tableKey || "vacaciones";
    const formatField = app.dataset.formatField || "cr07a_formato";

    const statusBanner = document.getElementById("vacationStatusBanner");
    const refreshBtn = document.getElementById("vacationRefreshBtn");
    const resetBtn = document.getElementById("vacationResetBtn");
    const submitBtn = document.getElementById("vacationSubmitBtn");
    const startDateInput = document.getElementById("vacationStartDate");
    const endDateInput = document.getElementById("vacationEndDate");
    const notesInput = document.getElementById("vacationNotes");
    const employeeNameInput = document.getElementById("vacationEmployeeName");
    const employeePositionInput = document.getElementById("vacationEmployeePosition");
    const employeeEmailInput = document.getElementById("vacationEmployeeEmail");
    const accruedDaysLabel = document.getElementById("vacationAccruedDays");
    const registeredDaysLabel = document.getElementById("vacationRegisteredDays");
    const availableDaysLabel = document.getElementById("vacationAvailableDays");
    const requestedDaysLabel = document.getElementById("vacationRequestedDays");
    const remainingDaysLabel = document.getElementById("vacationRemainingDays");
    const availabilityText = document.getElementById("vacationAvailabilityText");
    const previewHint = document.getElementById("vacationPreviewHint");
    const previewCard = document.getElementById("vacationPreviewCard");
    const requestsBody = document.getElementById("vacationRequestsBody");
    const requestsCount = document.getElementById("vacationRequestsCount");
    const emptyState = document.getElementById("vacationEmptyState");

    const numberFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });

    const state = {
        busy: false,
        context: null
    };

    refreshBtn.addEventListener("click", async () => {
        await loadContext();
    });

    resetBtn.addEventListener("click", () => {
        startDateInput.value = "";
        endDateInput.value = "";
        notesInput.value = "";
        recalculatePreview();
    });

    submitBtn.addEventListener("click", async () => {
        await submitRequest();
    });

    startDateInput.addEventListener("change", recalculatePreview);
    endDateInput.addEventListener("change", recalculatePreview);

    loadContext();

    async function loadContext() {
        try {
            setBusy(true);
            renderStatus("info", "Cargando saldo y solicitudes de vacaciones...");

            const response = await fetch(contextUrl, {
                headers: {
                    Accept: "application/json"
                }
            });

            const payload = await readPayload(response);
            if (!response.ok) {
                throw createResponseError(payload);
            }

            state.context = payload;
            renderContext();
            recalculatePreview();
            renderStatus("success", "Informacion de vacaciones cargada.");
        } catch (error) {
            renderStatus("error", buildErrorMessage(error));
        } finally {
            setBusy(false);
        }
    }

    async function submitRequest() {
        try {
            if (!state.context) {
                return;
            }

            const preview = getPreviewState();
            if (!preview.isReady) {
                renderStatus("warning", preview.message);
                return;
            }

            if (!preview.hasEnoughDays) {
                renderStatus("warning", "No puedes enviar la solicitud porque el saldo disponible no alcanza.");
                return;
            }

            setBusy(true);
            renderStatus("info", "Registrando solicitud de vacaciones...");

            const response = await fetch(submitUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    startDate: startDateInput.value,
                    endDate: endDateInput.value,
                    notes: notesInput.value
                })
            });

            const payload = await readPayload(response);
            if (!response.ok) {
                throw createResponseError(payload);
            }

            renderStatus(payload.status === "warning" ? "warning" : "success", [payload.message, payload.flowMessage].filter(Boolean).join(" "));
            startDateInput.value = "";
            endDateInput.value = "";
            notesInput.value = "";
            await loadContext();
        } catch (error) {
            renderStatus("error", buildErrorMessage(error));
        } finally {
            setBusy(false);
        }
    }

    function renderContext() {
        const context = state.context;
        const employee = context?.employee || {};
        employeeNameInput.value = employee.fullName || "";
        employeePositionInput.value = employee.position || "";
        employeeEmailInput.value = employee.email || "";

        accruedDaysLabel.textContent = formatDays(context?.accruedDays || 0);
        registeredDaysLabel.textContent = formatDays(context?.registeredDays || 0);
        availableDaysLabel.textContent = formatDays(context?.availableDays || 0);

        const requests = Array.isArray(context?.requests) ? context.requests : [];
        requestsCount.textContent = `${requests.length} ${requests.length === 1 ? "registro" : "registros"}`;
        emptyState.hidden = requests.length > 0;
        requestsBody.innerHTML = requests.map((item) => {
            const notes = item.notes ? escapeHtml(item.notes) : "<span class=\"vacation-muted\">Sin notas</span>";
            const actions = [
                `<a class="vacation-link" href="${escapeHtml(buildDocumentLink(item.recordId))}" target="_blank" rel="noopener">Ver formato</a>`
            ];

            if (item.hasDocument) {
                actions.push(`<a class="vacation-link" href="${escapeHtml(buildDownloadLink(item.recordId))}" target="_blank" rel="noopener">Descargar PDF</a>`);
            }

            return `
                <tr>
                    <td>${escapeHtml(buildPeriodLabel(item.startDateDisplay, item.endDateDisplay))}</td>
                    <td>${escapeHtml(formatDays(item.requestedDays || 0))}</td>
                    <td class="vacation-history-table__notes">${notes}</td>
                    <td>${escapeHtml(item.createdOnDisplay || "-")}</td>
                    <td class="vacation-history-table__actions">${actions.join(" ")}</td>
                </tr>
            `;
        }).join("");
    }

    function recalculatePreview() {
        const preview = getPreviewState();
        requestedDaysLabel.textContent = formatDays(preview.requestedDays);
        remainingDaysLabel.textContent = formatDays(preview.remainingDays);
        availabilityText.textContent = preview.message;
        previewHint.textContent = preview.hint;
        previewCard.classList.toggle("is-valid", preview.hasEnoughDays && preview.isReady);
        previewCard.classList.toggle("is-invalid", !preview.hasEnoughDays && preview.isReady);
    }

    function getPreviewState() {
        const availableDays = Number(state.context?.availableDays || 0);
        const startDate = parseDateOnly(startDateInput.value);
        const endDate = parseDateOnly(endDateInput.value);

        if (!startDate || !endDate) {
            return {
                requestedDays: 0,
                remainingDays: availableDays,
                hasEnoughDays: false,
                isReady: false,
                message: "Selecciona una fecha inicial y una fecha final para calcular.",
                hint: "El calculo excluye sabados, domingos y festivos nacionales de Colombia."
            };
        }

        if (endDate < startDate) {
            return {
                requestedDays: 0,
                remainingDays: availableDays,
                hasEnoughDays: false,
                isReady: false,
                message: "La fecha final no puede ser menor que la inicial.",
                hint: "Ajusta el rango para continuar."
            };
        }

        const details = calculateBusinessDayDetails(startDate, endDate);
        const remainingDays = roundDays(availableDays - details.businessDays);
        const hasEnoughDays = remainingDays >= 0;

        return {
            requestedDays: details.businessDays,
            remainingDays,
            hasEnoughDays,
            isReady: details.businessDays > 0,
            message: details.businessDays <= 0
                ? "El rango seleccionado no tiene dias habiles para tomar vacaciones."
                : hasEnoughDays
                    ? "El saldo disponible alcanza para esta solicitud."
                    : "El saldo disponible no alcanza para este rango.",
            hint: `Se excluyen ${details.excludedDays} dia(s) entre fines de semana y festivos nacionales.`
        };
    }

    function calculateBusinessDayDetails(startDate, endDate) {
        const holidayKeys = getColombiaHolidayKeys(startDate.getUTCFullYear(), endDate.getUTCFullYear());
        let businessDays = 0;
        let excludedDays = 0;

        for (let current = cloneDate(startDate); current <= endDate; current = addDays(current, 1)) {
            const dayOfWeek = current.getUTCDay();
            const dateKey = toDateKey(current);
            if (dayOfWeek === 0 || dayOfWeek === 6 || holidayKeys.has(dateKey)) {
                excludedDays += 1;
                continue;
            }

            businessDays += 1;
        }

        return {
            businessDays,
            excludedDays
        };
    }

    function getColombiaHolidayKeys(startYear, endYear) {
        const holidays = new Set();
        for (let year = startYear; year <= endYear; year += 1) {
            const easterSunday = getEasterSunday(year);
            addHoliday(holidays, createUtcDate(year, 1, 1));
            addHoliday(holidays, moveHolidayToMonday(createUtcDate(year, 1, 6)));
            addHoliday(holidays, moveHolidayToMonday(createUtcDate(year, 3, 19)));
            addHoliday(holidays, addDays(easterSunday, -3));
            addHoliday(holidays, addDays(easterSunday, -2));
            addHoliday(holidays, createUtcDate(year, 5, 1));
            addHoliday(holidays, moveHolidayToMonday(addDays(easterSunday, 39)));
            addHoliday(holidays, moveHolidayToMonday(addDays(easterSunday, 60)));
            addHoliday(holidays, moveHolidayToMonday(addDays(easterSunday, 68)));
            addHoliday(holidays, moveHolidayToMonday(createUtcDate(year, 6, 29)));
            addHoliday(holidays, createUtcDate(year, 7, 20));
            addHoliday(holidays, createUtcDate(year, 8, 7));
            addHoliday(holidays, moveHolidayToMonday(createUtcDate(year, 8, 15)));
            addHoliday(holidays, moveHolidayToMonday(createUtcDate(year, 10, 12)));
            addHoliday(holidays, moveHolidayToMonday(createUtcDate(year, 11, 1)));
            addHoliday(holidays, moveHolidayToMonday(createUtcDate(year, 11, 11)));
            addHoliday(holidays, createUtcDate(year, 12, 8));
            addHoliday(holidays, createUtcDate(year, 12, 25));
        }

        return holidays;
    }

    function getEasterSunday(year) {
        const a = year % 19;
        const b = Math.floor(year / 100);
        const c = year % 100;
        const d = Math.floor(b / 4);
        const e = b % 4;
        const f = Math.floor((b + 8) / 25);
        const g = Math.floor((b - f + 1) / 3);
        const h = (19 * a + b - d - g + 15) % 30;
        const i = Math.floor(c / 4);
        const k = c % 4;
        const l = (32 + 2 * e + 2 * i - h - k) % 7;
        const m = Math.floor((a + 11 * h + 22 * l) / 451);
        const month = Math.floor((h + l - 7 * m + 114) / 31);
        const day = ((h + l - 7 * m + 114) % 31) + 1;
        return createUtcDate(year, month, day);
    }

    function moveHolidayToMonday(date) {
        const dayOfWeek = date.getUTCDay();
        const offset = (8 - dayOfWeek) % 7;
        return offset === 0 ? date : addDays(date, offset);
    }

    function addHoliday(set, date) {
        set.add(toDateKey(date));
    }

    function parseDateOnly(value) {
        if (!value) {
            return null;
        }

        const parts = value.split("-").map(Number);
        if (parts.length !== 3 || parts.some((part) => Number.isNaN(part))) {
            return null;
        }

        return createUtcDate(parts[0], parts[1], parts[2]);
    }

    function createUtcDate(year, month, day) {
        return new Date(Date.UTC(year, month - 1, day));
    }

    function addDays(date, days) {
        const copy = cloneDate(date);
        copy.setUTCDate(copy.getUTCDate() + days);
        return copy;
    }

    function cloneDate(date) {
        return new Date(date.getTime());
    }

    function toDateKey(date) {
        return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`;
    }

    function pad(value) {
        return String(value).padStart(2, "0");
    }

    function buildPeriodLabel(startDate, endDate) {
        if (!startDate && !endDate) {
            return "-";
        }

        return startDate === endDate ? startDate : `${startDate || "-"} al ${endDate || "-"}`;
    }

    function buildDocumentLink(recordId) {
        return `${documentUrl}?recordId=${encodeURIComponent(recordId)}`;
    }

    function buildDownloadLink(recordId) {
        const params = new URLSearchParams({
            tableKey,
            recordId,
            fieldName: formatField
        });
        return `${downloadUrl}?${params.toString()}`;
    }

    function roundDays(value) {
        return Math.round((Number(value) + Number.EPSILON) * 100) / 100;
    }

    function formatDays(value) {
        return numberFormatter.format(roundDays(value));
    }

    function setBusy(isBusy) {
        state.busy = isBusy;
        refreshBtn.disabled = isBusy;
        resetBtn.disabled = isBusy;
        submitBtn.disabled = isBusy;
        startDateInput.disabled = isBusy;
        endDateInput.disabled = isBusy;
        notesInput.disabled = isBusy;
    }

    function renderStatus(level, message) {
        statusBanner.className = `rh-status rh-status--${level} is-visible`;
        statusBanner.textContent = message;
    }

    function buildErrorMessage(error) {
        if (!error) {
            return "Ocurrio un error inesperado.";
        }

        const parts = [];
        if (error.message) {
            parts.push(error.message);
        }

        if (error.detail) {
            parts.push(error.detail);
        }

        if (error.traceId) {
            parts.push(`TraceId: ${error.traceId}`);
        }

        return parts.join(" | ");
    }

    function createResponseError(payload) {
        return {
            message: payload?.message || "La operacion no se pudo completar.",
            detail: payload?.detail || "",
            traceId: payload?.traceId || ""
        };
    }

    async function readPayload(response) {
        const contentType = response.headers.get("content-type") || "";
        if (contentType.includes("application/json")) {
            return await response.json();
        }

        return {
            message: await response.text()
        };
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll("\"", "&quot;")
            .replaceAll("'", "&#39;");
    }
})();
