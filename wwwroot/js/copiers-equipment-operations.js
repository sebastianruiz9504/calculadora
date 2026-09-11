(function () {
    "use strict";
    const root = document.getElementById("equipmentOperations");
    if (!root) return;
    const $ = id => document.getElementById("eo" + id), form = $("Form"), fields = $("Fields");
    const input = name => form.elements.namedItem(name), drafts = window.CopiersMtoV2Drafts;
    const owner = root.dataset.owner + "/equipment-operations", canvas = $("Signature"), ctx = canvas.getContext("2d");
    const values = ["OperationKind", "EquipmentCondition", "EquipmentAccessories", "ReplacementCondition", "ReplacementAccessories", "MovementReason", "VisitStart", "ServiceAddress", "CustomerContactName", "CustomerObservations", "InternalNotes", "SignerName", "SignerRole", "CustomerAccepted", "InternalConfirmed"];
    let catalog = { clients: [], equipment: [] }, state, ready = false, busy = false, stroke = null, poll, saveChain = Promise.resolve();
    const titles = { delivery: "Certificado de entrega", replacement: "Certificado de cambio", withdrawal: "Certificado de retiro", internal: "Salida interna", receipt: "Confirmación de recepción" };
    const consents = { delivery: "Recibí el equipo y los accesorios relacionados, en el estado indicado.", receipt: "Recibí el equipo y los accesorios relacionados, en el estado indicado.", withdrawal: "Entregué al técnico el equipo y los accesorios relacionados para su retiro, en el estado indicado.", replacement: "Se retiró el equipo anterior y recibí el equipo de reemplazo con los accesorios y estados relacionados." };
    const nowLocal = () => { const d = new Date(); return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16); };
    const fresh = () => ({ key: crypto.randomUUID(), client: "", destination: "", equipment: "", replacement: "", pendingKey: "", strokes: [], photos: [], page: 1, ended: "", signed: "", frozen: null, result: null, received: false });
    const client = () => catalog.clients.find(x => x.id === state.client);
    const asset = () => catalog.equipment.find(x => x.id === state.equipment);
    const kind = () => input("OperationKind").value;
    const internal = () => state.frozen ? state.internalOnly === true : kind() === "internal" || (kind() === "receipt" && client()?.isInternal === true);
    const localAsset = x => !x.pending && (!x.clientId || catalog.clients.some(c => c.id === x.clientId && c.isInternal));
    function message(text, error = false) { $("Status").textContent = text; $("Status").className = "alert " + (error ? "alert-warning" : "alert-info"); }
    function capture() {
        state.summary = $("Summary").textContent;
        state.values = Object.fromEntries(values.map(name => [name, input(name).type === "checkbox" ? input(name).checked : input(name).value]));
        return structuredClone(state);
    }
    function save() {
        if (!ready) return Promise.resolve();
        const snapshot = capture();
        saveChain = saveChain.catch(() => {}).then(() => drafts.save(owner, snapshot));
        return saveChain.catch(error => { fields.disabled = true; ready = false; message("No se pudo guardar el borrador en esta tablet. No cierres la página: " + error.message, true); throw error; });
    }
    function redraw() {
        ctx.fillStyle = "white"; ctx.fillRect(0, 0, canvas.width, canvas.height); ctx.strokeStyle = "#15253d"; ctx.lineWidth = 2.8; ctx.lineCap = "round"; ctx.lineJoin = "round";
        for (const line of state.strokes) { ctx.beginPath(); line.forEach((p, i) => i ? ctx.lineTo(p.x, p.y) : ctx.moveTo(p.x, p.y)); ctx.stroke(); }
    }
    function clearSignature() { state.strokes = []; state.signed = ""; input("CustomerAccepted").checked = false; redraw(); }
    function editDetails() { clearSignature(); state.page = 1; state.ended = ""; input("InternalConfirmed").checked = false; render(); }
    async function json(url, options = {}, allowMissing = false) {
        const controller = new AbortController(), timer = setTimeout(() => controller.abort(), options.body ? 90000 : 30000);
        try {
            const response = await fetch(url, { credentials: "same-origin", cache: "no-store", ...options, signal: controller.signal });
            if (response.redirected || response.status === 401 || response.status === 403 || (response.ok && !response.headers.get("content-type")?.includes("application/json"))) {
                $("Login").hidden = false; throw new Error("La sesión necesita renovarse. Tu borrador se conserva en este dispositivo.");
            }
            if (response.status === 404 && allowMissing) return null;
            const body = await response.json().catch(() => ({}));
            if (!response.ok) { const error = new Error(body.message || body.error?.message || "No fue posible confirmar la operación. El borrador sigue guardado."); error.status = response.status; throw error; }
            return body;
        } finally { clearTimeout(timer); }
    }
    const pickers = {};
    for (const key of ["Client", "Destination", "Equipment", "Replacement"]) {
        const field = key.toLowerCase(), search = $(key + "Search");
        pickers[key] = window.CopiersMtoV2Picker.create(search, $(key + "List"), { onSelect(item) {
            if (!ready || state.frozen) return;
            state[field] = item.id; search.value = item.label;
            if (key === "Client") { state.equipment = ""; input("CustomerContactName").value = client()?.contactName || ""; }
            editDetails(); void save();
        } });
        search.addEventListener("input", () => { if (!ready || state.frozen) return; state[field] = ""; if (key === "Client") state.equipment = ""; clearSignature(); updatePickers(false); void save(); });
    }
    function updatePickers(resetText = true) {
        const k = kind(), customers = catalog.clients.filter(x => !x.isInternal);
        pickers.Client.setItems(customers.map(x => ({ id: x.id, label: x.name })));
        pickers.Destination.setItems(catalog.clients.filter(x => x.id !== state.client && (k === "withdrawal" || x.isInternal)).map(x => ({ id: x.id, label: x.name })));
        pickers.Equipment.setItems(catalog.equipment.filter(x => !x.pending && (k === "delivery" || k === "internal" ? localAsset(x) : x.clientId === state.client)).map(x => ({ id: x.id, label: x.serial, description: x.reference + " · " + x.clientName })));
        pickers.Replacement.setItems(catalog.equipment.filter(localAsset).map(x => ({ id: x.id, label: x.serial, description: x.reference + " · " + x.clientName })));
        if (resetText) {
            $("ClientSearch").value = client()?.name || "";
            $("DestinationSearch").value = catalog.clients.find(x => x.id === state.destination)?.name || "";
            $("EquipmentSearch").value = asset()?.serial || "";
            $("ReplacementSearch").value = catalog.equipment.find(x => x.id === state.replacement)?.serial || "";
        }
        $("Email").value = client()?.email || "";
    }
    function render() {
        const k = kind(), isInternal = internal();
        $("Details").hidden = state.page !== 1; $("Review").hidden = state.page !== 2;
        $("PendingWrap").hidden = k !== "receipt"; $("ClientWrap").hidden = k === "internal" || k === "receipt";
        $("DestinationWrap").hidden = k === "delivery" || k === "receipt";
        $("ReplacementWrap").hidden = k !== "replacement"; $("ContactWrap").hidden = isInternal;
        $("EquipmentSearch").readOnly = k === "receipt";
        $("Hint").textContent = k === "receipt" ? "Selecciona una salida pendiente. La asignación al destino solo cambia cuando confirmes su recepción." : k === "withdrawal" ? "Selecciona dónde se retirará y el destino previsto. El cliente firma únicamente su retiro. Si va a otro cliente, ese cliente firmará una recepción aparte." : k === "replacement" ? "El cliente firma el retiro y la entrega del reemplazo. El equipo retirado queda en tránsito hasta confirmar su llegada interna." : k === "internal" ? "Sin certificado ni correo al cliente. El equipo queda en tránsito hasta confirmar su recepción." : "Selecciona un equipo disponible en depósito, oficina o stock. El cliente firma la entrega.";
        $("SignatureWrap").hidden = isInternal; $("InternalWrap").hidden = !isInternal;
        $("ReviewTitle").textContent = isInternal ? "2. Confirmación interna" : "2. Conformidad del cliente";
        $("Consent").textContent = consents[k] || "";
        $("Send").textContent = isInternal ? "Registrar operación interna" : "Enviar certificado y registrar";
        fields.disabled = !ready || !!state.frozen;
        $("Retry").hidden = !state.frozen || busy; $("Another").hidden = !state.done;
        updatePickers(); renderPhotos(); redraw();
    }
    function summary() {
        const k = kind(), a = asset(), replacement = catalog.equipment.find(x => x.id === state.replacement), lines = [internal() ? titles[k] : k === "receipt" ? titles.delivery : titles[k]];
        if (client()) lines.push("Cliente: " + client().name);
        lines.push((k === "withdrawal" || k === "replacement" ? "Equipo retirado: " : "Equipo: ") + a.serial + " · " + a.reference, "Estado: " + input("EquipmentCondition").value, "Accesorios: " + (input("EquipmentAccessories").value || "No relacionados"));
        if (k === "replacement") lines.push("Equipo entregado: " + replacement.serial + " · " + replacement.reference, "Estado: " + input("ReplacementCondition").value, "Accesorios: " + (input("ReplacementAccessories").value || "No relacionados"));
        lines.push("Detalle: " + input("MovementReason").value, "Entrada: " + new Date(input("VisitStart").value).toLocaleString("es-CO"), "Salida: " + new Date(state.ended).toLocaleString("es-CO"));
        if (!internal()) lines.push("Atendió: " + input("CustomerContactName").value, "Correo: " + client().email, "Observaciones: " + (input("CustomerObservations").value || "Sin observaciones"));
        if (internal()) lines.push("Destino: " + catalog.clients.find(x => x.id === (state.destination || state.client))?.name);
        $("Summary").textContent = lines.join("\n");
    }
    function validateDetails() {
        const k = kind(), a = asset();
        if (!a) throw new Error("Selecciona un equipo de la lista.");
        if (k !== "internal" && !client()) throw new Error("Selecciona un cliente de la lista.");
        if (["withdrawal", "replacement", "internal"].includes(k) && !state.destination) throw new Error("Selecciona el destino previsto.");
        if (k === "internal") state.client = state.destination;
        if (k === "replacement" && !catalog.equipment.some(x => x.id === state.replacement && localAsset(x))) throw new Error("Selecciona el equipo de reemplazo disponible.");
        if (k === "receipt" && (!a.pending || a.pending.operationKey !== state.pendingKey)) throw new Error("Selecciona una recepción pendiente.");
        for (const name of ["EquipmentCondition", "MovementReason", ...(k === "replacement" ? ["ReplacementCondition"] : [])]) if (!input(name).value.trim()) throw new Error("Completa el estado del equipo y el detalle de la operación.");
        const started = new Date(input("VisitStart").value).getTime();
        if (!Number.isFinite(started) || started > Date.now() || Date.now() - started > 86400000) throw new Error("Revisa la hora de inicio de la atención.");
        if (!internal() && (!client()?.email || !input("CustomerContactName").value.trim())) throw new Error("Completa la persona que atiende y guarda el correo del encargado con el botón +.");
    }
    input("OperationKind").addEventListener("change", () => { state.client = state.destination = state.equipment = state.replacement = state.pendingKey = ""; $("Pending").value = ""; editDetails(); void save(); });
    $("Pending").addEventListener("change", () => {
        const a = catalog.equipment.find(x => x.id === $("Pending").value && x.pending);
        state.equipment = a?.id || ""; state.client = a?.pending.destinationId || ""; state.destination = state.client; state.pendingKey = a?.pending.operationKey || "";
        input("CustomerContactName").value = client()?.contactName || ""; editDetails(); void save();
    });
    for (const name of values.filter(x => x !== "OperationKind")) input(name).addEventListener("input", () => {
        if (!ready || state.frozen) return;
        if (["SignerName", "SignerRole"].includes(name)) { clearSignature(); }
        else if (!["CustomerAccepted", "InternalConfirmed"].includes(name)) clearSignature();
        void save();
    });
    $("Next").addEventListener("click", async () => { try { validateDetails(); state.ended = new Date().toISOString(); state.page = 2; summary(); render(); await save(); message(internal() ? "Confirma la operación física antes de registrar." : "El cliente debe revisar estos datos y firmar. Las fotografías se pueden agregar después sin borrar la firma."); } catch (error) { message(error.message, true); } });
    $("Back").addEventListener("click", () => { editDetails(); void save(); message("Revisa los datos. Después de un cambio se solicita una firma nueva."); });
    $("ClearSignature").addEventListener("click", () => { clearSignature(); void save(); });
    function point(event) { const rect = canvas.getBoundingClientRect(); return { x: Math.max(0, Math.min(1000, (event.clientX - rect.left) * 1000 / rect.width)), y: Math.max(0, Math.min(260, (event.clientY - rect.top) * 260 / rect.height)) }; }
    canvas.addEventListener("pointerdown", event => { if (!ready || state.frozen || internal() || !event.isPrimary) return; event.preventDefault(); canvas.setPointerCapture(event.pointerId); stroke = [point(event)]; state.strokes.push(stroke); });
    canvas.addEventListener("pointermove", event => { if (!stroke) return; event.preventDefault(); stroke.push(point(event)); redraw(); });
    function finishStroke() { if (!stroke) return; stroke = null; state.signed = new Date().toISOString(); redraw(); void save(); }
    canvas.addEventListener("pointerup", finishStroke); canvas.addEventListener("pointercancel", finishStroke); canvas.addEventListener("lostpointercapture", finishStroke);
    function renderPhotos() {
        $("Photos").replaceChildren();
        state.photos.forEach((file, index) => { const li = document.createElement("li"), button = document.createElement("button"); li.append(document.createTextNode(file.name + " · " + Math.ceil(file.size / 1024) + " KB ")); button.type = "button"; button.textContent = "Quitar"; button.disabled = !!state.frozen; button.addEventListener("click", () => { state.photos.splice(index, 1); renderPhotos(); void save(); }); li.append(button); $("Photos").append(li); });
    }
    async function addPhotos(event) {
        try {
            if (state.frozen) return;
            const files = [...state.photos, ...event.target.files];
            if (files.length > 8 || files.reduce((n, x) => n + x.size, 0) > 20 * 1024 * 1024 || files.some(x => !["image/jpeg", "image/png"].includes(x.type) || x.size > 8 * 1024 * 1024 || !x.size)) throw new Error("Adjunta máximo ocho fotografías JPEG/PNG: 8 MB por foto y 20 MB en total.");
            state.photos = files; renderPhotos(); await save();
        } catch (error) { message(error.message, true); } finally { event.target.value = ""; }
    }
    $("Camera").addEventListener("change", addPhotos); $("Files").addEventListener("change", addPhotos);
    $("EditEmail").addEventListener("click", () => { if (!client()) { message("Selecciona el cliente primero.", true); return; } $("EmailEdit").value = client().email || ""; $("EmailError").textContent = ""; $("EmailDialog").showModal(); });
    $("SaveEmail").addEventListener("click", async () => {
        if (!$("EmailEdit").reportValidity()) return;
        $("SaveEmail").disabled = true;
        try { const result = await json("/CopiersMtoV2/SaveClientEmail", { method: "POST", headers: { "Content-Type": "application/json", RequestVerificationToken: input("__RequestVerificationToken").value }, body: JSON.stringify({ clientId: state.client, email: $("EmailEdit").value.trim() }) }); client().email = result.email; editDetails(); await save(); $("EmailDialog").close(); }
        catch (error) { $("EmailError").textContent = error.message; } finally { $("SaveEmail").disabled = false; }
    });
    async function locate() {
        if (!navigator.geolocation) return {};
        return new Promise(resolve => { const timer = setTimeout(() => resolve({}), 9000); navigator.geolocation.getCurrentPosition(p => { clearTimeout(timer); resolve({ Latitude: p.coords.latitude, Longitude: p.coords.longitude, AccuracyMeters: p.coords.accuracy, LocationCapturedAtUtc: new Date(p.timestamp).toISOString(), LocationSource: "navigator.geolocation" }); }, () => { clearTimeout(timer); resolve({}); }, { enableHighAccuracy: true, timeout: 8000, maximumAge: 0 }); });
    }
    async function freeze() {
        validateDetails();
        if (!internal() && (!input("CustomerAccepted").checked || !input("SignerName").value.trim() || !input("SignerRole").value.trim() || state.strokes.reduce((n, x) => n + x.length, 0) < 12 || !state.signed)) throw new Error("Completa el nombre, cargo, aceptación y firma del cliente.");
        if (internal() && !input("InternalConfirmed").checked) throw new Error("Confirma la salida o recepción física del equipo.");
        const entries = values.filter(x => x !== "VisitStart").map(name => [name, input(name).type === "checkbox" ? String(input(name).checked) : input(name).value]);
        entries.push(["SubmissionKey", state.key], ["ClientId", state.client], ["DestinationId", state.destination], ["EquipmentId", state.equipment], ["ReplacementId", state.replacement], ["PendingKey", state.pendingKey], ["ServiceStartedAtUtc", new Date(input("VisitStart").value).toISOString()], ["ServiceEndedAtUtc", state.ended], ["DeviceSignedAtUtc", state.signed], ["SignaturePointCount", String(state.strokes.reduce((n, x) => n + x.length, 0))]);
        if (!internal()) { const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/jpeg", .94)); if (!blob) throw new Error("No se pudo guardar la imagen de la firma."); entries.push(["Signature", new File([blob], "firma-cliente.jpg", { type: "image/jpeg" })]); }
        state.photos.forEach(file => entries.push(["Attachments", file]));
        Object.entries(await locate()).forEach(([key, value]) => entries.push([key, String(value)]));
        state.internalOnly = internal(); state.frozen = entries; await save(); render();
    }
    function schedule() { clearTimeout(poll); poll = setTimeout(() => { void submit(); }, 12000); }
    async function accepted(receipt) {
        state.received = true;
        if (receipt.result) state.result = receipt.result;
        await save();
        if (!state.result) { message(receipt.message || "Recibido de forma segura; el servidor está procesando la operación.", receipt.status === "needs_review"); if (receipt.status !== "needs_review") schedule(); return; }
        const result = state.result;
        if (result.emailRequired === false) { state.done = true; message("Operación interna registrada: " + result.serviceReference + ". No requiere correo al cliente."); }
        else {
            const status = await json("/CopiersMtoV2/Status?activityKind=movement&recordId=" + encodeURIComponent(result.recordId));
            if (status.emailState === 3) { state.done = true; message("Certificado " + result.serviceReference + " registrado y correo confirmado como enviado."); }
            else { message("Certificado " + result.serviceReference + " guardado. " + (status.emailState === 4 ? "El correo requiere revisión interna; no repitas el registro." : "El correo aún está pendiente de confirmación."), status.emailState === 4); if (status.emailState !== 4) schedule(); }
        }
        await save(); render();
    }
    async function submit() {
        if (busy || !ready || !state.frozen || state.done) return;
        busy = true; clearTimeout(poll); render();
        try {
            const receipt = await json("/CopiersEquipmentOperations/SubmissionStatus?submissionKey=" + encodeURIComponent(state.key), {}, true);
            if (receipt) { await accepted(receipt); return; }
            if (state.received) throw new Error("El servidor había confirmado la recepción y ahora no la encuentra. Se requiere revisión; no se repetirá automáticamente.");
            const body = new FormData(); for (const [key, value] of state.frozen) body.append(key, value);
            body.append("__RequestVerificationToken", input("__RequestVerificationToken").value);
            message("Enviando la operación. Tu copia permanece guardada hasta confirmar el resultado.");
            await accepted(await json("/CopiersEquipmentOperations/Submit", { method: "POST", body, headers: { "Idempotency-Key": state.key } }));
        } catch (error) {
            if (error.status === 400 && !state.received) { state.frozen = null; editDetails(); await save(); message(error.message + " Corrige los datos y solicita una nueva firma; todavía no se recibió el registro.", true); }
            else message((error.name === "AbortError" ? "La conexión no permitió confirmar el envío." : error.message) + " Puedes comprobarlo con la misma clave; no crees un duplicado.", true);
        }
        finally { busy = false; render(); }
    }
    form.addEventListener("submit", async event => { event.preventDefault(); if (!ready || busy) return; busy = true; fields.disabled = true; try { if (!state.frozen) await freeze(); } catch (error) { message(error.message, true); } finally { busy = false; render(); } if (state.frozen) await submit(); });
    $("Retry").addEventListener("click", () => { void submit(); });
    $("Another").addEventListener("click", async () => { if (!state.done || busy) return; await drafts.remove(owner); location.reload(); });
    window.addEventListener("online", () => { if (state?.frozen) void submit(); });
    window.addEventListener("pagehide", () => { if (ready && state) void save(); });
    async function initialize() {
        try {
            state = await drafts.read(owner) || fresh();
            for (const [name, value] of Object.entries(state.values || {})) if (values.includes(name)) { if (input(name).type === "checkbox") input(name).checked = value === true; else input(name).value = value; }
            if (!input("VisitStart").value) input("VisitStart").value = nowLocal();
            // A saved receipt must remain recoverable even if live catalog access is temporarily unavailable.
            ready = true;
            if (state.frozen) { state.done = false; $("Summary").textContent = state.summary || "Envío guardado. Consultando su resultado…"; render(); await submit(); return; }
            catalog = await json("/CopiersEquipmentOperations/Bootstrap");
            for (const a of catalog.equipment.filter(x => x.pending)) { const option = document.createElement("option"); option.value = a.id; option.textContent = a.serial + " · " + a.pending.originName + " → " + a.pending.destinationName; $("Pending").append(option); }
            $("Pending").value = state.equipment;
            if (state.page === 2) { try { validateDetails(); summary(); } catch { state.page = 1; clearSignature(); } }
            await save(); render(); message("Borrador guardado en este dispositivo. Técnico: " + catalog.technician);
        } catch (error) { ready = false; fields.disabled = true; message(error.message, true); }
    }
    if (!navigator.locks || !drafts || !window.indexedDB || !crypto.randomUUID) { message("Este navegador no permite proteger el borrador. Actualiza Edge y utiliza una pestaña normal, no privada.", true); return; }
    void navigator.locks.request(owner, { ifAvailable: true }, async lock => { if (!lock) { message("La gestión de equipos está abierta en otra pestaña. Continúa allí o ciérrala antes de abrir esta.", true); return; } await initialize(); await new Promise(() => {}); });
})();
