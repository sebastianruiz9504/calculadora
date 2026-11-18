(() => {
    "use strict";
    const root = document.querySelector("#directCashFlowApp");
    const legacy = document.querySelector("#conciliacionApp");
    if (!root && !legacy) return;
    const $ = (s, p = document) => p.querySelector(s);
    const escape = s => String(s ?? "").replace(/[&<>"']/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));
    const money = v => v == null ? "—" : new Intl.NumberFormat("es-CO", {maximumFractionDigits: 0}).format(v);
    const precise = v => v == null ? "—" : new Intl.NumberFormat("es-CO", {minimumFractionDigits: 2, maximumFractionDigits: 2}).format(v);
    const monthNames = ["Enero","Febrero","Marzo","Abril","Mayo","Junio","Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre"];
    let catalogPromise, report, summarySerial = 0, summaryAbort, liveTimer, detailReload;
    const initialMonth = Number(root?.dataset.month || legacy?.dataset.month || 10);
    let period = {year: Number(root?.dataset.year || legacy?.dataset.year || new Date().getFullYear()), first: initialMonth, last: initialMonth};
    const token = () => $('input[name="__RequestVerificationToken"]')?.value || "";
    async function api(action, params = {}, body, signal) {
        const response = await fetch(`/DirectCashFlow/${action}?${new URLSearchParams(params)}`, {
            method: body === undefined ? "GET" : "POST", credentials: "same-origin", cache: "no-store", signal,
            headers: body === undefined ? {} : {"Content-Type":"application/json", "RequestVerificationToken": token()},
            body: body === undefined ? undefined : JSON.stringify(body)
        });
        const value = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(value.message || "No fue posible completar la operación. Actualiza la sesión e intenta nuevamente.");
        return value;
    }
    const concepts = () => catalogPromise ||= api("Concepts").catch(e => {catalogPromise = null; throw e;});
    const options = (catalog, direction, selected = "") => '<option value="">Selecciona número y concepto</option>' + catalog.filter(c => c.direction === direction).map(c => `<option value="${escape(c.key)}" ${c.key === selected ? "selected" : ""}>${escape(c.code)} · ${escape(c.label)} — ${escape(c.group)}</option>`).join("");
    function status(text = "") { if (root) $("[data-dcf-status]", root).textContent = text; }
    function dialog(title, subtitle = "", wide = false) {
        const d = document.createElement("dialog"); d.className = `dcf-dialog ${wide ? "wide" : ""}`;
        d.innerHTML = `<header class="dcf-dialog-header"><div><h2>${escape(title)}</h2><p>${escape(subtitle)}</p></div><button class="dcf-close" aria-label="Cerrar" type="button">×</button></header><div class="dcf-dialog-body"></div>`;
        document.body.append(d); const previous = document.activeElement;
        $(".dcf-close", d).onclick = () => d.close();
        d.addEventListener("click", e => { if (e.target === d && (e.clientX < d.getBoundingClientRect().left || e.clientX > d.getBoundingClientRect().right || e.clientY < d.getBoundingClientRect().top || e.clientY > d.getBoundingClientRect().bottom)) d.close(); });
        d.addEventListener("close", () => { d.remove(); if (previous?.isConnected) previous.focus(); }, {once: true});
        d.showModal(); return d;
    }
    function errorBox(container, text) {
        let box = $("[data-error]", container); if (!box) {box = document.createElement("p");box.className = "dcf-status";box.dataset.error = "";box.setAttribute("role","status");container.append(box);}
        box.textContent = text;
    }
    function updateLegacy(row) {
        document.querySelectorAll("[data-cnc-v2-row]").forEach(el => {
            if (el.dataset.recordId !== row.recordId) return;
            el.dataset.directConcept = row.conceptKey; el.dataset.directVersion = row.version;
            const label = $("[data-dcf-concept-label]",el); if(label)label.textContent = row.conceptLabel || "Asignar concepto";
        });
    }
    async function changed() { if (root) await refresh(); if (detailReload) await detailReload(); }
    async function mount(container, rowElement) {
        const host = document.createElement("div"); host.className = "dcf-inline"; host.innerHTML = '<small>Cargando concepto del flujo de caja…</small>'; container.append(host);
        try {
            const [catalog, row] = await Promise.all([concepts(), api("Movement", {sourceKind: rowElement.dataset.sourceKind || "Movimiento", recordId: rowElement.dataset.cashflowRecordId || rowElement.dataset.recordId})]);
            if (!host.isConnected) return;
            renderClassification(host, row, catalog);
        } catch(e) { if(host.isConnected)errorBox(host,e.message); }
    }
    function renderClassification(host, row, catalog) {
        if(row.isInternalTransfer) {host.innerHTML='<label>Flujo de caja directo</label><small>Traslado entre cuentas propias · no suma ingresos ni egresos del consolidado.</small>';return;}
        host.innerHTML = `<label>Flujo de caja directo · ${escape(row.direction)}</label><select aria-label="Número y concepto de flujo de caja">${options(catalog,row.direction,row.conceptKey)}</select><small>${escape(row.status === "Sugerido" ? "Sugerencia de octubre · " + row.reason : row.reason || "Selecciona el concepto al que corresponde este registro.")}</small><button class="dcf-button dcf-button-primary" type="button">Guardar concepto</button>`;
        const button = $("button",host);
        button.onclick = async () => {
            const key = $("select",host).value; if(!key){errorBox(host,"Selecciona un concepto.");return;}
            button.disabled = true;
            try {
                await api("Assignment",{}, {recordId:row.recordId,sourceKind:row.sourceKind,conceptKey:key,version:row.version});
                const saved = await api("Movement",{sourceKind:row.sourceKind,recordId:row.recordId});
                Object.assign(row,saved);updateLegacy(saved);renderClassification(host,row,catalog);errorBox(host,"Concepto guardado.");await changed();
            } catch(e) {errorBox(host,e.message);button.disabled=false;}
        };
    }
    async function openRow(sourceKind, recordId) {
        const d = dialog("Clasificar registro", "La clasificación alimenta el flujo de caja directo.");const body=$(".dcf-dialog-body",d);body.textContent="Cargando registro…";
        try {
            const [row,catalog] = await Promise.all([api("Movement",{sourceKind,recordId}),concepts()]); if(!d.isConnected)return;
            body.innerHTML=`<span class="dcf-pill ${row.isManual?"manual":""}">${escape(row.date)} · ${escape(row.bank)} · ${escape(row.direction)}</span><p class="dcf-detail-title">${escape(row.description)}</p><h2>$ ${precise(row.amount)}</h2><div data-classification></div>`;
            renderClassification($("[data-classification]",body),row,catalog);
            if(row.isStatement){const source=document.createElement("p");source.textContent=`Importado del extracto · ${row.reference} · Solo banco, sin envío a Siigo.`;body.append(source);}
            if(row.isManual&&!row.isStatement){const actions=document.createElement("div");actions.className="dcf-dialog-actions";actions.innerHTML='<button class="dcf-button" data-edit>Editar registro</button><button class="dcf-button" data-void>Anular registro</button>';body.append(actions);
                $("[data-edit]",actions).onclick=()=>{d.close();manual(row);};
                $("[data-void]",actions).onclick=()=>{
                    const confirm=dialog("Anular registro manual","Se conservará el registro para consulta, sin incluirlo en los totales.");
                    const area=$(".dcf-dialog-body",confirm);area.innerHTML=`<p>${escape(row.description)} · $ ${precise(row.amount)}</p><button class="dcf-button dcf-button-primary">Anular este registro</button>`;
                    $("button",area).onclick=async e=>{e.target.disabled=true;try{await api("VoidManual",{},{recordId:row.recordId,version:row.version});confirm.close();d.close();await changed();if(legacy)location.reload();}catch(error){errorBox(area,error.message);e.target.disabled=false;}};
                };
            }
        } catch(e){body.textContent=e.message;}
    }
    async function manual(existing) {
        const d=dialog(existing?"Editar registro manual":"Nuevo registro manual","Solo banco · alimenta el flujo de caja y no se envía a Siigo.");const body=$(".dcf-dialog-body",d);
        let catalog;try{catalog=await concepts();}catch(e){body.textContent=e.message;return;}if(!d.isConnected)return;
        const month=Number(root?.dataset.month || legacy?.dataset.month || 10);const date=existing?.date || `${period.year}-${String(month).padStart(2,"0")}-01`;
        const id=existing?.recordId || crypto.randomUUID();
        body.innerHTML=`<form><div class="dcf-form-grid"><label>Fecha<input name="date" type="date" value="${escape(date)}" required></label><label>Cuenta bancaria<select name="bank"><option ${existing?.bank==="Cloud"?"selected":""}>Cloud</option><option ${existing?.bank==="Copiers"?"selected":""}>Copiers</option></select></label><label>Dirección<select name="direction"><option value="Salida" ${existing?.direction!=="Entrada"?"selected":""}>Salida</option><option value="Entrada" ${existing?.direction==="Entrada"?"selected":""}>Ingreso</option></select></label><label>Valor (COP)<input name="amount" type="number" min="0.01" max="100000000000" step="0.01" value="${existing?.amount||""}" required></label><label class="full">Descripción<textarea name="description" minlength="3" maxlength="500" required>${escape(existing?.description)}</textarea></label><label class="full">Referencia bancaria (opcional)<input name="reference" maxlength="200" value="${escape(existing?.reference)}"></label><label class="full">Número y concepto<select name="concept">${options(catalog,existing?.direction||"Salida",existing?.conceptKey)}</select></label></div><div class="dcf-dialog-actions"><button class="dcf-button dcf-button-primary" type="submit">Guardar registro</button></div></form>`;
        const form=$("form",body);form.elements.direction.onchange=()=>form.elements.concept.innerHTML=options(catalog,form.elements.direction.value);
        form.onsubmit=async e=>{e.preventDefault();if(!form.reportValidity())return;const button=$('button[type="submit"]',form);button.disabled=true;
            const f=form.elements;
            try{await api("Manual",{},{recordId:id,date:f.date.value,bank:f.bank.value,direction:f.direction.value,amount:Number(f.amount.value),description:f.description.value,reference:f.reference.value,conceptKey:f.concept.value,version:existing?.version||""});d.close();await changed();if(legacy)location.reload();}
            catch(error){errorBox(body,error.message);button.disabled=false;}
        };
    }
    function balances(month) {
        const info=report.months.find(m=>m.month===month);if(!info)return;const b=info.balances;
        const d=dialog(`Saldos · ${monthNames[month-1]} ${report.year}`,"Escribe cero cuando el saldo sea cero. Un campo vacío queda pendiente.");const body=$(".dcf-dialog-body",d);
        const fields=[["opening","Saldo inicial consolidado",b.opening],["cloud","Saldo final cuenta Cloud",b.cloud],["copiers","Saldo final cuenta Copiers",b.copiers],["pockets","Saldo final Bolsillos",b.pockets],["fiduciary","Saldo final Fiduren­ta",b.fiduciary]];
        body.innerHTML=`<form><div class="dcf-form-grid">${fields.map(([key,label,value],i)=>`<label class="${i===0?"full":""}">${escape(label)}<input name="${key}" type="number" step="0.01" min="-100000000000" max="100000000000" value="${value??""}"></label>`).join("")}</div><p>El saldo inicial incluye todas las cuentas, bolsillos y Fiduren­ta. Si queda vacío se toma el cierre completo del mes anterior del reporte o los saldos disponibles de conciliación.</p><div class="dcf-dialog-actions"><button class="dcf-button dcf-button-primary">Guardar saldos</button></div></form>`;
        $("form",body).onsubmit=async e=>{e.preventDefault();const form=e.target;if(!form.reportValidity())return;const button=$("button",form);button.disabled=true;const payload={year:report.year,month,version:b.version};fields.forEach(([key])=>payload[key]=form.elements[key].value===""?null:Number(form.elements[key].value));try{await api("Balances",{},payload);d.close();await changed();}catch(error){errorBox(body,error.message);button.disabled=false;}};
    }
    async function list(month,concept="",initialStatus="") {
        const d=dialog("Registros del flujo de caja",`${monthNames[month-1]} ${period.year}`,true);const body=$(".dcf-dialog-body",d);let page=1,serial=0;
        body.innerHTML=`<div class="dcf-list-filters"><input type="search" maxlength="100" placeholder="Buscar descripción o concepto" aria-label="Buscar registros"><select aria-label="Estado"><option value="">Todos los estados</option><option>Pendiente</option><option>Sugerido</option><option>Confirmado</option><option>Manual</option><option>Extracto</option><option>Traslado</option></select></div><div style="overflow:auto" data-items></div><div class="dcf-pagination"><button class="dcf-button" data-prev>← Anterior</button><span data-count></span><button class="dcf-button" data-next>Siguiente →</button></div>`;
        $("select",body).value=initialStatus;let debounce;
        async function load(){const request=++serial;const host=$("[data-items]",body);host.setAttribute("aria-busy","true");
            try{const result=await api("Movements",{year:period.year,month,concept,status:$("select",body).value,search:$("input",body).value,page,pageSize:50});if(request!==serial||!d.isConnected)return;
                host.innerHTML=`<table><thead><tr><th>Fecha / cuenta</th><th>Descripción</th><th>Concepto</th><th>Valor COP</th><th>Estado</th><th></th></tr></thead><tbody>${result.items.map(r=>`<tr class="${r.isManual?"is-manual":""}"><td>${escape(r.date)}<br><small>${escape(r.bank)}</small></td><td>${escape(r.description)}</td><td>${escape(r.conceptLabel)}</td><td>${precise(r.amount)}<br><small>${escape(r.direction)}</small></td><td>${escape(r.isStatement?"Extracto · "+r.status:r.isManual?"Manual · "+r.status:r.status)}</td><td><button class="dcf-button" data-row="${escape(r.recordId)}" data-kind="${escape(r.sourceKind)}">Abrir</button></td></tr>`).join("")||'<tr><td colspan="6">No hay registros para este filtro.</td></tr>'}</tbody></table>`;
                $("[data-count]",body).textContent=`${result.total} registros · Página ${page} de ${Math.max(1,Math.ceil(result.total/50))}`;$("[data-prev]",body).disabled=page<=1;$("[data-next]",body).disabled=page*50>=result.total;
                host.querySelectorAll("[data-row]").forEach(button=>button.onclick=()=>openRow(button.dataset.kind,button.dataset.row));
            }catch(e){if(d.isConnected)errorBox(body,e.message);}finally{host.removeAttribute("aria-busy");}
        }
        detailReload=load;d.addEventListener("close",()=>{if(detailReload===load)detailReload=null;clearTimeout(debounce);});
        $("select",body).onchange=()=>{page=1;load();};$("input",body).oninput=()=>{clearTimeout(debounce);debounce=setTimeout(()=>{page=1;load();},300);};
        $("[data-prev]",body).onclick=()=>{page--;load();};$("[data-next]",body).onclick=()=>{page++;load();};await load();
    }
    function render() {
        const months=report.months;const active=months.filter(m=>m.hasActivity);const last=active.at(-1);const first=active[0];
        const entry=active.reduce((s,m)=>s+m.entries,0);const exit=active.reduce((s,m)=>s+m.exits,0);const pending=months.reduce((s,m)=>s+m.unclassifiedCount,0);const suggested=months.reduce((s,m)=>s+m.suggestedCount,0);
        const bankChecks=report.bankChecks||[];
        const finalChecks=bankChecks.filter(b=>b.period===`${report.year}-${String(last?.month).padStart(2,"0")}`);
        const bankClosing=finalChecks.length===2?finalChecks.reduce((s,b)=>s+b.statementClosing,0):null;
        const cards=[["Saldo inicial bancos",first?.openingBalance,first?`${first.label} · Apertura del período`:"Pendiente de saldos",""],["Ingresos externos",active.length?entry:null,"Sin traslados entre cuentas propias","income"],["Egresos externos",active.length?exit:null,"Sin traslados entre cuentas propias","expense"],[bankClosing==null?"Saldo según conceptos":"Saldo final bancos",bankClosing??last?.closingBalance,bankClosing==null?(pending?"Clasificación en proceso":"Según registros clasificados"):`${last.label} · Cierre de los extractos`,"closing"]];
        $("[data-dcf-kpis]",root).innerHTML=cards.map(([label,value,note,cls])=>`<article class="dcf-kpi ${cls}"><span class="dcf-kpi-label">${label}</span><strong>${value==null?"Por completar":"$ "+money(value)}</strong><small>${escape(note)}</small></article>`).join("");
        $("[data-dcf-banks]",root).innerHTML=bankChecks.length?`<div class="dcf-bank-heading"><h2>Control de extractos</h2><p>Saldo inicial + ingresos − egresos = saldo final</p></div><div class="dcf-bank-grid">${bankChecks.map(b=>`<article class="dcf-bank-card ${b.matches?"":"has-difference"}"><div class="dcf-bank-top"><div class="dcf-bank-name">Bancolombia · ${escape(b.bank)}<small>Cuenta ···· ${escape(b.account.slice(-4))} &nbsp; / &nbsp; ${escape(b.period)}</small></div><span class="dcf-badge ${b.matches?"":"warning"}">${b.matches?"✓ Cuadrado":"Revisar diferencias"}</span></div><div class="dcf-bank-values"><div><span>Saldo inicial</span><strong>$ ${precise(b.opening)}</strong></div><div><span>Ingresos</span><strong>$ ${precise(b.statementCredits)}</strong></div><div><span>Egresos</span><strong>$ ${precise(b.statementDebits)}</strong></div><div><span>Movimientos</span><strong>${b.registeredCount} / ${b.statementCount}</strong></div><div class="closing"><span>Saldo final del extracto</span><strong>$ ${precise(b.statementClosing)}</strong></div></div><footer class="dcf-bank-footer"><span>${b.matches?"Todos los movimientos coinciden":`${b.missingCount} faltantes · ${b.unexpectedCount} adicionales · ${b.changedCount} modificados`}</span><strong>Diferencia $ ${precise(b.difference)}</strong></footer></article>`).join("")}</div>`:"";
        $("[data-dcf-head]",root).innerHTML=`<tr><th>N.º</th><th>Concepto</th>${months.map(m=>`<th>${escape(m.label)} ${report.year}</th>`).join("")}</tr>`;
        $("[data-dcf-body]",root).innerHTML=report.rows.map(r=>`<tr class="dcf-${r.kind}"><td>${escape(r.code)}</td><td>${escape(r.label)}</td>${r.values.map((v,i)=>{
            const editable=r.kind==="input"||r.row===7;const detail=r.kind==="detail";
            const label=v==null?(editable?"Completar":"—"):r.row===90?precise(v):money(v);
            return `<td class="${v<0?"dcf-negative":""}">${editable||detail?`<button type="button" class="dcf-cell ${v==null?"dcf-empty":""}" data-month="${months[i].month}" ${editable?'data-balance':`data-concept="${escape(r.conceptKey)}"`} aria-label="${escape(r.label)} ${escape(months[i].label)}: ${escape(label)}">${label}</button>`:label}</td>`;
        }).join("")}</tr>`).join("");
        root.querySelectorAll("[data-concept]").forEach(b=>b.onclick=()=>list(Number(b.dataset.month),b.dataset.concept));root.querySelectorAll("[data-balance]").forEach(b=>b.onclick=()=>balances(Number(b.dataset.month)));
        $("[data-dcf-review]",root).innerHTML=`<div class="dcf-review-title">${pending?`${pending} movimientos por categorizar`:"Clasificación al día"}<small>${pending?"Asigna un concepto para incorporarlos al detalle del flujo.":"Todos los movimientos tienen un concepto asignado."}${suggested?` ${suggested} sugerencias pendientes de revisión.`:""}</small></div><div class="dcf-review-actions">${months.filter(m=>m.hasActivity).map(m=>`${m.unclassifiedCount?`<button class="dcf-button" data-pending-month="${m.month}">Categorizar ${escape(m.label)} · ${m.unclassifiedCount} →</button>`:""}${m.suggestedCount?`<button class="dcf-button" data-suggested-month="${m.month}">Revisar sugerencias · ${m.suggestedCount}</button>`:""}${m.manualCount?`<button class="dcf-button" data-imported-month="${m.month}">Ver extracto</button>`:""}`).join("")}</div>`;
        root.querySelectorAll("[data-imported-month]").forEach(b=>b.onclick=()=>list(Number(b.dataset.importedMonth),"","Extracto"));
        root.querySelectorAll("[data-suggested-month]").forEach(b=>b.onclick=()=>list(Number(b.dataset.suggestedMonth),"","Sugerido"));
        root.querySelectorAll("[data-list-month]").forEach(b=>b.onclick=()=>list(Number(b.dataset.listMonth)));root.querySelectorAll("[data-pending-month]").forEach(b=>b.onclick=()=>list(Number(b.dataset.pendingMonth),"","Pendiente"));
        $("[data-suggest]",root)?.addEventListener("click",async e=>{e.target.disabled=true;status("Preparando sugerencias de octubre…");try{const result=await api("SuggestOctober",{},{});await refresh();status(`${result.created} sugerencias nuevas guardadas. Las clasificaciones existentes se conservan.`);}catch(error){status(error.message);e.target.disabled=false;}});
        const issues=[];
        if(bankChecks.some(b=>!b.matches))issues.push("El control de extractos tiene diferencias. Revisa los movimientos faltantes, adicionales o modificados.");
        if(months.some(m=>m.internalTransferNet!==0))issues.push("Hay traslados sin contrapartida por revisar.");
        if(months.some(m=>m.hasActivity&&m.openingBalance==null))issues.push("Completa el saldo inicial para calcular la disponibilidad.");
        status(issues.join(" "));
        $("[data-dcf-export]",root).href=`/DirectCashFlow/Export?${new URLSearchParams(period)}`;
        $("[data-dcf-live]",root).textContent=`Actualizado ${new Date(report.capturedAt).toLocaleTimeString("es-CO",{hour:"2-digit",minute:"2-digit"})}`;
    }
    async function refresh() {
        if(!root)return;const serial=++summarySerial;summaryAbort?.abort();summaryAbort=new AbortController();
        try{const data=await api("Summary",period,undefined,summaryAbort.signal);if(serial!==summarySerial)return;report=data;render();}
        catch(e){if(e.name!=="AbortError"){status(e.message);$("[data-dcf-live]",root).textContent="Actualización pendiente";}}
    }
    window.DirectCashFlow={mount,openRow,manual};
    document.querySelectorAll("[data-dcf-open-row]").forEach(b=>b.addEventListener("click",e=>{e.stopPropagation();const r=b.closest("[data-cnc-v2-row]");openRow(r.dataset.sourceKind,r.dataset.recordId);}));
    document.querySelectorAll("[data-dcf-manual]").forEach(b=>b.addEventListener("click",()=>manual()));
    if(!root)return;
    for(const selector of ["[data-dcf-first]","[data-dcf-last]"])$(selector,root).innerHTML=monthNames.map((m,i)=>`<option value="${i+1}">${m}</option>`).join("");
    $("[data-dcf-first]",root).value=period.first;$("[data-dcf-last]",root).value=period.last;
    $("[data-dcf-apply]",root).onclick=()=>{const next={year:Number($("[data-dcf-year]",root).value),first:Number($("[data-dcf-first]",root).value),last:Number($("[data-dcf-last]",root).value)};if(next.year<2020||next.year>2100||next.last<next.first){status("Selecciona un período válido.");return;}period=next;refresh();};
    $("[data-dcf-refresh]",root).onclick=refresh;$("[data-dcf-all]",root).onclick=()=>list(report?.months.find(m=>m.hasActivity)?.month||period.first);
    function schedule(){clearTimeout(liveTimer);if(!document.hidden)liveTimer=setTimeout(async()=>{if(!document.querySelector(".dcf-dialog[open]"))await refresh();schedule();},25000);}
    document.addEventListener("visibilitychange",()=>{if(document.hidden){clearTimeout(liveTimer);summaryAbort?.abort();}else{refresh();schedule();}});
    window.addEventListener("pagehide",()=>{clearTimeout(liveTimer);summaryAbort?.abort();});refresh();schedule();
})();
