(() => {
    const root = document.querySelector("[data-aguas-sda-mode]");
    if (!root) return;

    const mode = root.dataset.aguasSdaMode || "";
    const escapeHtml = value => String(value || "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#039;");

    const fetchJson = async (url, options = {}) => {
        const response = await fetch(url, options);
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) {
            throw new Error(payload.detail || payload.message || "No fue posible completar la solicitud.");
        }
        return payload;
    };

    const setStatus = (el, tone, message) => {
        if (!el) return;
        el.textContent = message || "";
        el.className = "ags-status";
        if (message) {
            el.classList.add("is-visible", `is-${tone || "info"}`);
        }
    };

    initTables();
    if (mode === "bitacoras") initBitacoras();
    if (mode === "aprobacion") initAprobacion();
    if (mode === "permisos") initPermisos();

    function initTables() {
        document.querySelectorAll(".ags-table-filter").forEach(input => {
            const table = document.querySelector(input.dataset.tableFilter || "");
            if (!table) return;
            input.addEventListener("input", () => {
                const query = input.value.trim().toLowerCase();
                table.querySelectorAll("tbody tr").forEach(row => {
                    if (row.classList.contains("ags-empty-row")) return;
                    row.hidden = query && !row.textContent.toLowerCase().includes(query);
                });
            });
        });

        document.querySelectorAll(".ags-table th[data-sort]").forEach((th, index) => {
            th.addEventListener("click", () => {
                const table = th.closest("table");
                const tbody = table?.querySelector("tbody");
                if (!tbody) return;
                const asc = th.dataset.sortDir !== "asc";
                table.querySelectorAll("th[data-sort]").forEach(item => item.dataset.sortDir = "");
                th.dataset.sortDir = asc ? "asc" : "desc";
                const rows = Array.from(tbody.querySelectorAll("tr:not(.ags-empty-row)"));
                rows.sort((a, b) => {
                    const av = (a.children[index]?.textContent || "").trim().toLowerCase();
                    const bv = (b.children[index]?.textContent || "").trim().toLowerCase();
                    return asc ? av.localeCompare(bv, "es") : bv.localeCompare(av, "es");
                });
                rows.forEach(row => tbody.appendChild(row));
            });
        });
    }

    function initBitacoras() {
        const records = window.aguasSdaBitacoras || [];
        const profile = window.aguasSdaProfile || {};
        const modalEl = document.getElementById("agsBitacoraModal");
        const modal = modalEl ? new bootstrap.Modal(modalEl) : null;
        const form = document.getElementById("agsBitacoraForm");
        const status = document.getElementById("agsFormStatus");
        const saveUrl = root.dataset.saveUrl || "";
        const fileUrl = root.dataset.fileUrl || "";

        const fields = {
            recordId: document.getElementById("agsRecordId"),
            enviar: document.getElementById("agsEnviar"),
            fecha: document.getElementById("agsFecha"),
            periodo: document.getElementById("agsPeriodo"),
            usuario: document.getElementById("agsUsuario"),
            correo: document.getElementById("agsCorreo"),
            cargo: document.getElementById("agsCargo"),
            dependencia: document.getElementById("agsDependencia"),
            area: document.getElementById("agsArea"),
            frente: document.getElementById("agsFrente"),
            ubicacion: document.getElementById("agsUbicacion"),
            horaInicio: document.getElementById("agsHoraInicio"),
            horaFin: document.getElementById("agsHoraFin"),
            actividad: document.getElementById("agsActividad"),
            descripcion: document.getElementById("agsDescripcion"),
            recursos: document.getElementById("agsRecursos"),
            novedades: document.getElementById("agsNovedades"),
            riesgos: document.getElementById("agsRiesgos"),
            observaciones: document.getElementById("agsObservaciones"),
            fotoAntes: document.getElementById("agsFotoAntes"),
            fotoDurante: document.getElementById("agsFotoDurante"),
            fotoDespues: document.getElementById("agsFotoDespues")
        };

        const buildPeriod = dateValue => {
            if (!dateValue) return "";
            const [year, month] = dateValue.split("-").map(Number);
            if (!year || !month) return "";
            const number = ((year - 2025) * 12) + month - 11 + 1;
            return number > 0 ? `Periodo ${number}` : "";
        };

        const fillPhotoState = record => {
            const states = {
                antes: record?.fotoAntesBlob,
                durante: record?.fotoDuranteBlob,
                despues: record?.fotoDespuesBlob
            };
            Object.keys(states).forEach(kind => {
                const el = form.querySelector(`[data-photo-state="${kind}"]`);
                if (!el) return;
                el.innerHTML = states[kind]
                    ? `<a href="${fileUrl}?recordId=${encodeURIComponent(record.recordId)}&kind=${encodeURIComponent(kind)}" target="_blank" rel="noopener">Cargada</a>`
                    : "";
            });
        };

        const setReadonly = readonly => {
            form.querySelectorAll("input, textarea").forEach(input => {
                if (input.type === "hidden") return;
                if (input.readOnly) return;
                input.disabled = readonly;
            });
            document.getElementById("agsSavePartial").disabled = readonly;
            document.getElementById("agsSubmitBitacora").disabled = readonly;
        };

        const openForm = record => {
            form.reset();
            setStatus(status, "", "");
            fields.recordId.value = record?.recordId || "";
            fields.fecha.value = record?.fecha || new Date().toISOString().slice(0, 10);
            fields.periodo.value = record?.periodoLabel || buildPeriod(fields.fecha.value);
            fields.usuario.value = record?.nombreUsuario || profile.systemUserName || "";
            fields.correo.value = record?.correoUsuario || profile.systemUserEmail || "";
            fields.cargo.value = record?.cargo || profile.cargo || "";
            fields.dependencia.value = record?.dependencia || profile.dependencia || "";
            fields.area.value = record?.areaIntervencionName || profile.areaIntervencionName || "";
            fields.frente.value = record?.frenteTrabajo || profile.frenteTrabajo || "";
            fields.ubicacion.value = record?.ubicacion || "";
            fields.horaInicio.value = record?.horaInicio || "";
            fields.horaFin.value = record?.horaFin || "";
            fields.actividad.value = record?.actividad || "";
            fields.descripcion.value = record?.descripcion || "";
            fields.recursos.value = record?.recursos || "";
            fields.novedades.value = record?.novedades || "";
            fields.riesgos.value = record?.riesgos || "";
            fields.observaciones.value = record?.observaciones || "";
            fillPhotoState(record);
            setReadonly(record && !record.puedeEditar);
            modal?.show();
        };

        fields.fecha?.addEventListener("change", () => {
            fields.periodo.value = buildPeriod(fields.fecha.value);
        });

        document.getElementById("agsNewBitacora")?.addEventListener("click", () => openForm(null));
        document.querySelectorAll(".ags-open-bitacora").forEach(button => {
            button.addEventListener("click", () => {
                const record = records.find(item => item.recordId === button.dataset.recordId);
                openForm(record);
            });
        });

        const submitForm = async enviar => {
            if (!saveUrl) return;
            if (!form.reportValidity()) return;
            fields.enviar.value = enviar ? "true" : "false";
            const data = new FormData(form);
            data.set("enviar", enviar ? "true" : "false");

            setStatus(status, "info", enviar ? "Enviando bitacora..." : "Guardando...");
            document.getElementById("agsSavePartial").disabled = true;
            document.getElementById("agsSubmitBitacora").disabled = true;

            try {
                const payload = await fetchJson(saveUrl, { method: "POST", body: data });
                setStatus(status, "success", payload.message || "Guardado.");
                window.location.reload();
            } catch (error) {
                setStatus(status, "error", error.message || "No fue posible guardar.");
                document.getElementById("agsSavePartial").disabled = false;
                document.getElementById("agsSubmitBitacora").disabled = false;
            }
        };

        document.getElementById("agsSavePartial")?.addEventListener("click", () => submitForm(false));
        document.getElementById("agsSubmitBitacora")?.addEventListener("click", () => submitForm(true));
    }

    function initAprobacion() {
        const records = window.aguasSdaApprovalRows || [];
        const modalEl = document.getElementById("agsApprovalModal");
        const modal = modalEl ? new bootstrap.Modal(modalEl) : null;
        const fileUrl = root.dataset.fileUrl || "";
        const approveUrl = root.dataset.approveUrl || "";
        const rejectUrl = root.dataset.rejectUrl || "";
        const status = document.getElementById("agsApprovalStatus");
        let selectedRecord = null;

        const assetUrl = (record, kind) => `${fileUrl}?recordId=${encodeURIComponent(record.recordId)}&kind=${encodeURIComponent(kind)}&v=${Date.now()}`;

        const openApproval = record => {
            selectedRecord = record;
            document.getElementById("agsApprovalTitle").textContent = `${record.fechaLabel || ""} · ${record.areaIntervencionName || ""}`;
            document.getElementById("agsApprovalDetails").innerHTML = [
                ["Periodo", record.periodoLabel],
                ["Usuario", record.nombreUsuario],
                ["Actividad", record.actividad],
                ["Ubicacion", record.ubicacion],
                ["Horario", `${record.horaInicio || ""} - ${record.horaFin || ""}`],
                ["Descripcion", record.descripcion]
            ].map(([label, value]) => `<div><dt>${escapeHtml(label)}</dt><dd>${escapeHtml(value || "-")}</dd></div>`).join("");
            modalEl.querySelectorAll("[data-approval-photo]").forEach(img => {
                img.src = assetUrl(record, img.dataset.approvalPhoto);
            });
            document.getElementById("agsApprovalPdf").href = assetUrl(record, "pdf");
            document.getElementById("agsApprovalComment").value = "";
            setStatus(status, "", "");
            modal?.show();
        };

        document.querySelectorAll(".ags-approval-row").forEach(row => {
            row.addEventListener("click", () => {
                const record = records.find(item => item.recordId === row.dataset.recordId);
                if (record) openApproval(record);
            });
        });

        const sendDecision = async (url, message) => {
            if (!selectedRecord || !url) return;
            setStatus(status, "info", message);
            const body = JSON.stringify({
                recordId: selectedRecord.recordId,
                comentario: document.getElementById("agsApprovalComment").value.trim()
            });
            try {
                const payload = await fetchJson(url, {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body
                });
                setStatus(status, "success", payload.message || "Actualizado.");
                window.location.reload();
            } catch (error) {
                setStatus(status, "error", error.message || "No fue posible actualizar.");
            }
        };

        document.getElementById("agsApproveBitacora")?.addEventListener("click", () => sendDecision(approveUrl, "Aprobando..."));
        document.getElementById("agsRejectBitacora")?.addEventListener("click", () => sendDecision(rejectUrl, "Devolviendo..."));
    }

    function initPermisos() {
        let users = window.aguasSdaUsers || [];
        const form = document.getElementById("agsUserForm");
        const status = document.getElementById("agsUserStatus");
        const searchUrl = root.dataset.searchUsersUrl || "";
        const saveUrl = root.dataset.saveUserUrl || "";
        const deleteUrl = root.dataset.deleteUserUrl || "";
        const results = document.getElementById("agsSystemUserResults");
        let searchTimer = null;

        const ids = {
            recordId: document.getElementById("agsUserRecordId"),
            systemUserId: document.getElementById("agsSystemUserId"),
            systemUserName: document.getElementById("agsSystemUserName"),
            systemUserEmail: document.getElementById("agsSystemUserEmail"),
            search: document.getElementById("agsSystemUserSearch"),
            area: document.getElementById("agsUserArea"),
            cargo: document.getElementById("agsUserCargo"),
            dependencia: document.getElementById("agsUserDependencia"),
            telefono: document.getElementById("agsUserTelefono"),
            contrato: document.getElementById("agsUserContrato"),
            frente: document.getElementById("agsUserFrente"),
            activo: document.getElementById("agsUserActivo"),
            mode: document.getElementById("agsUserFormMode")
        };

        const setUserForm = user => {
            ids.recordId.value = user?.recordId || "";
            ids.systemUserId.value = user?.systemUserId || "";
            ids.systemUserName.value = user?.systemUserName || "";
            ids.systemUserEmail.value = user?.systemUserEmail || "";
            ids.search.value = user ? `${user.systemUserName || ""} (${user.systemUserEmail || ""})` : "";
            ids.area.value = user?.areaIntervencionId || "";
            ids.cargo.value = user?.cargo || "";
            ids.dependencia.value = user?.dependencia || "";
            ids.telefono.value = user?.telefono || "";
            ids.contrato.value = user?.contratoConvenio || "";
            ids.frente.value = user?.frenteTrabajo || "";
            ids.activo.checked = user?.activo !== false;
            ids.mode.textContent = user ? "Editando registro" : "Nuevo registro";
            document.querySelectorAll("[data-ags-role]").forEach(input => {
                input.checked = (user?.roleValues || []).includes(Number(input.value));
            });
            setStatus(status, "", "");
        };

        const buildPayload = () => ({
            recordId: ids.recordId.value,
            systemUserId: ids.systemUserId.value,
            systemUserName: ids.systemUserName.value,
            systemUserEmail: ids.systemUserEmail.value,
            areaIntervencionId: ids.area.value,
            roleValues: Array.from(document.querySelectorAll("[data-ags-role]:checked")).map(input => Number(input.value)),
            cargo: ids.cargo.value.trim(),
            dependencia: ids.dependencia.value.trim(),
            telefono: ids.telefono.value.trim(),
            contratoConvenio: ids.contrato.value.trim(),
            frenteTrabajo: ids.frente.value.trim(),
            activo: ids.activo.checked
        });

        document.getElementById("agsNewUser")?.addEventListener("click", () => setUserForm(null));
        document.querySelectorAll(".ags-user-row").forEach(row => {
            row.addEventListener("click", () => {
                const user = users.find(item => item.recordId === row.dataset.recordId);
                setUserForm(user);
            });
        });

        ids.search?.addEventListener("input", () => {
            clearTimeout(searchTimer);
            const query = ids.search.value.trim();
            ids.systemUserId.value = "";
            ids.systemUserName.value = "";
            ids.systemUserEmail.value = "";
            if (query.length < 2 || !searchUrl) {
                results.hidden = true;
                return;
            }

            searchTimer = setTimeout(async () => {
                try {
                    const items = await fetchJson(`${searchUrl}?q=${encodeURIComponent(query)}`);
                    results.innerHTML = items.map(item => `<button type="button" data-id="${escapeHtml(item.id)}" data-name="${escapeHtml(item.name)}" data-email="${escapeHtml(item.email)}">${escapeHtml(item.name)}<br><small>${escapeHtml(item.email)}</small></button>`).join("");
                    results.hidden = items.length === 0;
                } catch (error) {
                    results.innerHTML = `<button type="button">${escapeHtml(error.message)}</button>`;
                    results.hidden = false;
                }
            }, 250);
        });

        results?.addEventListener("click", event => {
            const button = event.target.closest("button[data-id]");
            if (!button) return;
            ids.systemUserId.value = button.dataset.id || "";
            ids.systemUserName.value = button.dataset.name || "";
            ids.systemUserEmail.value = button.dataset.email || "";
            ids.search.value = `${button.dataset.name || ""} (${button.dataset.email || ""})`;
            results.hidden = true;
        });

        form?.addEventListener("submit", async event => {
            event.preventDefault();
            setStatus(status, "info", "Guardando usuario...");
            try {
                const payload = await fetchJson(saveUrl, {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify(buildPayload())
                });
                setStatus(status, "success", payload.message || "Usuario guardado.");
                window.location.reload();
            } catch (error) {
                setStatus(status, "error", error.message || "No fue posible guardar.");
            }
        });

        document.getElementById("agsDeleteUser")?.addEventListener("click", async () => {
            if (!ids.recordId.value) return;
            setStatus(status, "info", "Eliminando usuario...");
            try {
                const payload = await fetchJson(deleteUrl, {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ recordId: ids.recordId.value })
                });
                setStatus(status, "success", payload.message || "Usuario eliminado.");
                window.location.reload();
            } catch (error) {
                setStatus(status, "error", error.message || "No fue posible eliminar.");
            }
        });

        setUserForm(null);
    }
})();
