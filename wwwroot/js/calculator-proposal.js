(() => {
    "use strict";

    const proposal = window.calculatorProposal || {};

    function firstDefined(source, names, fallback = undefined) {
        for (const name of names) {
            if (source && source[name] !== undefined && source[name] !== null) return source[name];
        }
        return fallback;
    }

    function numberValue(source, names) {
        const value = Number(firstDefined(source, names, 0));
        return Number.isFinite(value) ? value : 0;
    }

    function normalizePossibility(raw, index) {
        const possibilityLines = Array.isArray(raw?.lines) ? raw.lines : [];
        const title = String(firstDefined(raw, ["title", "name", "scenarioName"], "")).trim();
        return {
            possibilityId: String(firstDefined(raw, ["possibilityId", "scenarioId", "id"], "")),
            title: title || `Escenario ${index + 1}`,
            order: numberValue(raw, ["order", "displayOrder", "sequence"]) || index + 1,
            isRecommended: Boolean(firstDefined(raw, ["isRecommended", "recommended"], false)),
            lines: possibilityLines,
            totalMonthlySale: numberValue(raw, ["totalMonthlySale", "monthlySubtotal"]),
            totalMonthlyVat: numberValue(raw, ["totalMonthlyVat", "monthlyVat"]),
            totalContractSale: numberValue(raw, ["totalContractSale", "contractSubtotal"]),
            totalContractVat: numberValue(raw, ["totalContractVat", "contractVat"]),
            economicHash: String(firstDefined(raw, ["economicHash", "calculationHash"], ""))
        };
    }

    const rawPossibilities = Array.isArray(proposal.possibilities)
        ? proposal.possibilities
        : Array.isArray(proposal.options)
            ? proposal.options
            : [];
    const possibilities = (rawPossibilities.length
        ? rawPossibilities
        : [{
            possibilityId: proposal.scenarioId,
            title: proposal.scenarioName,
            lines: Array.isArray(proposal.lines) ? proposal.lines : [],
            totalMonthlySale: proposal.totalMonthlySale,
            totalMonthlyVat: proposal.totalMonthlyVat,
            totalContractSale: proposal.totalContractSale,
            totalContractVat: proposal.totalContractVat
        }])
        .map(normalizePossibility)
        .sort((left, right) => left.order - right.order);
    const lines = possibilities.flatMap(item => item.lines);
    const persistedConfiguration = firstDefined(
        proposal,
        ["configuration", "latestConfiguration", "latestConfigurationJson", "proposalConfiguration"],
        null);
    let pendingExportIdempotencyKey = "";
    let pendingExportConfigurationJson = "";

    const presets = {
        "Seguridad": [
            ["Microsoft Defender", "Protección avanzada para correo, endpoints e identidades."],
            ["Microsoft Purview", "Gobierno, cumplimiento y protección de información."],
            ["Acronis Cyber Protect", "Backup, antimalware y protección de cargas críticas."],
            ["Implementación", "Acompañamiento técnico, validación y transferencia."]
        ],
        "Licenciamiento": [
            ["Microsoft 365", "Planes empresariales, productividad y colaboración."],
            ["Copilot", "Adopción de IA para productividad y escenarios comerciales."],
            ["Power Platform", "Automatización, Power Apps y analítica con Power BI."],
            ["Adopción", "Capacitación, buenas prácticas y comunicación interna."]
        ],
        "Backup y continuidad": [
            ["Backup M365", "Protección nube a nube para Exchange, OneDrive, SharePoint y Teams."],
            ["Endpoints críticos", "Agentes para equipos con información local relevante."],
            ["Servidores", "Estrategia de respaldo para cargas locales o en la nube."],
            ["DR", "Continuidad, restauración y pruebas controladas."]
        ],
        "Azure / Infraestructura": [
            ["Azure", "Red virtual, almacenamiento, cómputo y servicios corporativos."],
            ["Directorio Activo", "AD DS, DNS, usuarios, grupos y políticas."],
            ["VPN", "Conectividad segura sitio a sitio y validación de acceso."],
            ["Migración", "Ejecución por fases, piloto y cierre."]
        ],
        "Copiers - Hardware": [
            ["Renting de impresora", "Impresora en renting con soporte y suministros incluidos."],
            ["Venta de impresora", "Adquisición de impresora nueva con garantía del fabricante."],
            ["Renting de multifuncional", "Equipo multifuncional en renting mensual."],
            ["Venta Multifuncional", "Compra de equipo multifuncional con garantía y puesta en marcha."],
            ["Arriendo hardware", "Arriendo de hardware según la necesidad del cliente."],
            ["Venta hardware", "Venta de hardware según la necesidad del cliente."]
        ],
        "Mixta": [
            ["Licenciamiento", "Planes y servicios según necesidad del cliente."],
            ["Seguridad", "Protección de identidades, correo y endpoints."],
            ["Backup", "Continuidad y recuperación de información."],
            ["Servicios profesionales", "Implementación, soporte y transferencia."]
        ]
    };

    const moduleKnowledge = {
        "Microsoft Defender": { desc: "Protección avanzada para endpoints, correo e identidades.", deliverables: ["Configuración de políticas base", "Validación de alertas", "Transferencia de conocimiento"] },
        "Microsoft Purview": { desc: "Gobierno, cumplimiento y protección de información.", deliverables: ["Políticas de protección", "Controles iniciales", "Recomendaciones de gobierno"] },
        "Acronis Cyber Protect": { desc: "Backup, recuperación, protección antimalware y continuidad.", deliverables: ["Activación de consola", "Planes de backup", "Pruebas de restauración"] },
        "Implementación": { desc: "Servicios profesionales para puesta en marcha, validación y transferencia.", deliverables: ["Levantamiento técnico", "Configuración", "Documentación y cierre"] },
        "Microsoft 365": { desc: "Productividad, correo, colaboración y servicios Microsoft 365.", deliverables: ["Coadministración", "Revisión de cargas", "Acompañamiento comercial"] },
        "Copilot": { desc: "Adopción de IA para productividad y escenarios comerciales.", deliverables: ["Escenarios de uso", "Sesión de adopción", "Buenas prácticas"] },
        "Power Platform": { desc: "Automatización, aplicaciones y analítica.", deliverables: ["Diagnóstico", "Definición funcional", "Recomendaciones de gobierno"] },
        "Adopción": { desc: "Gestión del cambio y transferencia de conocimiento.", deliverables: ["Capacitación", "Material de apoyo", "Recomendaciones"] },
        "Backup M365": { desc: "Backup nube a nube para Microsoft 365.", deliverables: ["Protección de cargas M365", "Restauración granular", "Panel de administración"] },
        "Endpoints críticos": { desc: "Protección de equipos con información local crítica.", deliverables: ["Instalación de agentes", "Definición de rutas", "Validación de planes"] },
        "Servidores": { desc: "Backup de servidores y cargas críticas.", deliverables: ["Estrategia de backup", "Retención", "Pruebas de recuperación"] },
        "DR": { desc: "Continuidad y recuperación ante contingencias.", deliverables: ["Plan de recuperación", "Prueba controlada", "Documentación"] },
        "Azure": { desc: "Servicios Azure, almacenamiento, cómputo y red.", deliverables: ["Configuración de servicios", "Monitoreo", "Recomendaciones de costo"] },
        "Directorio Activo": { desc: "Administración centralizada de identidades y equipos.", deliverables: ["AD DS y DNS", "Usuarios y grupos", "Políticas iniciales"] },
        "VPN": { desc: "Conectividad segura entre sede, nube y servicios.", deliverables: ["Validación de conectividad", "Configuración del túnel", "Pruebas de acceso"] },
        "Migración": { desc: "Ejecución por fases y validación de operación.", deliverables: ["Plan por fases", "Piloto", "Migración y cierre"] },
        "Renting de impresora": { desc: "Impresora en renting con soporte y suministros incluidos.", deliverables: ["Instalación", "Mantenimiento", "Soporte técnico"] },
        "Venta de impresora": { desc: "Adquisición de impresora nueva con garantía.", deliverables: ["Entrega", "Configuración inicial", "Garantía"] },
        "Renting de multifuncional": { desc: "Equipo multifuncional en renting mensual.", deliverables: ["Instalación", "Mantenimiento", "Soporte"] },
        "Venta Multifuncional": { desc: "Compra de multifuncional con puesta en marcha.", deliverables: ["Entrega", "Configuración", "Garantía"] },
        "Arriendo hardware": { desc: "Arriendo de hardware según necesidad del cliente.", deliverables: ["Entrega", "Soporte", "Retiro al cierre"] },
        "Venta hardware": { desc: "Venta de hardware según necesidad del cliente.", deliverables: ["Entrega", "Configuración", "Garantía"] },
        "Licenciamiento": { desc: "Planes y servicios según necesidad del cliente.", deliverables: ["Matriz de licencias", "Condiciones comerciales", "Activación"] },
        "Seguridad": { desc: "Protección de identidades, correo, endpoints y datos.", deliverables: ["Controles iniciales", "Políticas base", "Recomendaciones"] },
        "Backup": { desc: "Continuidad y recuperación de información.", deliverables: ["Planes de backup", "Retención", "Pruebas de restauración"] },
        "Servicios profesionales": { desc: "Implementación, soporte, transferencia y cierre.", deliverables: ["Levantamiento", "Implementación", "Documentación"] }
    };

    const valueAdded = {
        "General": [
            ["Soporte y escalamiento", "Soporte remoto 7x24 y escalamiento por niveles."],
            ["Acompañamiento consultivo", "Coadministración durante toda la ejecución del proyecto."],
            ["Transferencia de conocimiento", "Capacitación al equipo interno de TI del cliente."],
            ["Continuidad técnica y comercial", "Acompañamiento en fases posteriores y evolución."],
            ["Reunión de kickoff", "Alineación de alcance, responsables y cronograma inicial."],
            ["Documentación de entrega", "Actas, evidencias y documento de cierre del proyecto."]
        ],
        "Licenciamiento": [
            ["Optimización de licenciamiento", "Revisión de planes para aprovechar al máximo las licencias."],
            ["Sesión de adopción", "Buenas prácticas y escenarios de uso para los usuarios."],
            ["Acompañamiento en renovaciones", "Gestión de renovaciones y crecimiento del licenciamiento."]
        ],
        "Seguridad": [
            ["Evaluación de postura inicial", "Diagnóstico de seguridad y recomendaciones priorizadas."],
            ["Configuración de políticas base", "Controles iniciales y validación de alertas."],
            ["Informe de hallazgos", "Reporte del estado de seguridad y plan de mejora."]
        ],
        "Backup y continuidad": [
            ["Prueba de restauración", "Validación controlada de recuperación de información."],
            ["Definición de retención", "Políticas de retención acordes a la necesidad del cliente."],
            ["Monitoreo de respaldos", "Revisión periódica del estado de los backups."]
        ],
        "Azure / Infraestructura": [
            ["Revisión de costos (FinOps)", "Recomendaciones para optimizar el consumo de Azure."],
            ["Monitoreo y alertas", "Configuración de métricas, tableros y notificaciones."],
            ["Buenas prácticas de arquitectura", "Recomendaciones basadas en Azure Well-Architected."]
        ]
    };

    const valueLimits = Object.freeze({
        maxRows: 24,
        front: 80,
        name: 160,
        detail: 600
    });

    function limitedText(value, maxLength) {
        return String(value ?? "").slice(0, maxLength);
    }

    function normalizeValueRow(raw) {
        return {
            front: limitedText(firstDefined(raw, ["front", "category"], "General"), valueLimits.front).trim() || "General",
            name: limitedText(firstDefined(raw, ["name", "valueName"], ""), valueLimits.name).trim(),
            detail: limitedText(firstDefined(raw, ["detail", "description"], ""), valueLimits.detail).trim(),
            modality: String(firstDefined(raw, ["modality", "modalidad"], "Incluido")) === "Opcional"
                ? "Opcional"
                : "Incluido"
        };
    }

    const allCatalogModules = [...new Set(Object.entries(presets)
        .filter(([key]) => key !== "Mixta")
        .flatMap(([, items]) => items.map(([name]) => name)))];
    const selectedModules = new Set();
    const selectedValueModules = new Set();
    const valueRows = [];
    let activeValueType = "General";

    const byId = id => document.getElementById(id);
    const fieldValue = id => (byId(id)?.value || "").trim();
    const roundMoney = value => Math.round((Number(value) + Number.EPSILON) * 100) / 100;

    function possibilityTotals(possibility) {
        const monthlySubtotal = roundMoney(possibility.totalMonthlySale);
        const monthlyVat = roundMoney(possibility.totalMonthlyVat);
        const contractSubtotal = roundMoney(possibility.totalContractSale);
        const contractVat = roundMoney(possibility.totalContractVat);
        return {
            monthlySubtotal,
            monthlyVat,
            monthlyTotal: roundMoney(monthlySubtotal + monthlyVat),
            contractSubtotal,
            contractVat,
            contractTotal: roundMoney(contractSubtotal + contractVat)
        };
    }

    function newIdempotencyKey() {
        if (window.crypto?.randomUUID) return window.crypto.randomUUID();
        return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, character => {
            const random = Math.floor(Math.random() * 16);
            const value = character === "x" ? random : (random & 0x3) | 0x8;
            return value.toString(16);
        });
    }

    function markConfigurationDirty() {
        pendingExportIdempotencyKey = "";
        pendingExportConfigurationJson = "";
    }

    function currency() {
        return fieldValue("currency") || "COP";
    }

    function money(value) {
        return new Intl.NumberFormat("es-CO", {
            style: "currency",
            currency: currency(),
            minimumFractionDigits: 0,
            maximumFractionDigits: 2
        }).format(Number.isFinite(Number(value)) ? Number(value) : 0);
    }

    function setText(id, value) {
        const element = byId(id);
        if (element) element.textContent = value;
    }

    function createModuleCard(type, name, description, selected, onClick, compact = false) {
        const card = document.createElement("button");
        card.type = "button";
        card.className = `module-card${selected ? " selected" : ""}`;
        card.setAttribute("aria-pressed", selected ? "true" : "false");
        card.addEventListener("click", onClick);

        const tag = document.createElement("span");
        tag.className = "module-tag";
        tag.textContent = type;
        const title = document.createElement("h3");
        title.textContent = name;
        const text = document.createElement("p");
        text.textContent = description;
        card.append(tag, title, text);

        if (!compact) {
            const deliverables = moduleKnowledge[name]?.deliverables || [];
            if (deliverables.length) {
                const list = document.createElement("ul");
                list.className = "deliverables";
                deliverables.slice(0, 3).forEach(item => {
                    const li = document.createElement("li");
                    li.textContent = item;
                    list.appendChild(li);
                });
                card.appendChild(list);
            }
        }
        return card;
    }

    function inferInitialSelection() {
        const fronts = lines.map(line => String(line.front || "").toLowerCase());
        const descriptions = lines.map(line => String(line.description || "").toLowerCase()).join(" ");
        const flags = {
            licensing: fronts.some(front => front.includes("licenciamiento")),
            azure: fronts.some(front => front.includes("azure")),
            backup: fronts.some(front => front.includes("backup")),
            copiers: fronts.some(front => front.includes("copier") || front.includes("hardware")),
            services: fronts.some(front => front.includes("servicios"))
        };
        const count = Object.values(flags).filter(Boolean).length;
        let type = "Mixta";
        if (count === 1 && flags.licensing) type = "Licenciamiento";
        else if (count === 1 && flags.azure) type = "Azure / Infraestructura";
        else if (count === 1 && flags.backup) type = "Backup y continuidad";
        else if (count === 1 && flags.copiers) type = "Copiers - Hardware";

        byId("proposalType").value = type;
        if (type === "Mixta") {
            if (flags.licensing) selectedModules.add("Licenciamiento");
            if (flags.backup) selectedModules.add("Backup");
            if (flags.azure || flags.services || flags.copiers) selectedModules.add("Servicios profesionales");
            if (descriptions.includes("defender") || descriptions.includes("seguridad") || descriptions.includes("acronis")) selectedModules.add("Seguridad");
            if (!selectedModules.size) selectedModules.add("Servicios profesionales");
            return;
        }

        if (type === "Licenciamiento") selectedModules.add("Microsoft 365");
        if (type === "Azure / Infraestructura") selectedModules.add("Azure");
        if (type === "Backup y continuidad") selectedModules.add(descriptions.includes("endpoint") ? "Endpoints críticos" : "Backup M365");
        if (type === "Copiers - Hardware") {
            const match = (presets[type] || []).find(([name]) => descriptions.includes(name.toLowerCase().replace("multifuncional", "")));
            selectedModules.add(match?.[0] || (fronts.some(front => front.includes("hardware")) ? "Venta hardware" : "Renting de multifuncional"));
        }
    }

    function renderModules() {
        const grid = byId("moduleGrid");
        grid.replaceChildren();
        const type = fieldValue("proposalType") || "Mixta";
        (presets[type] || []).forEach(([name, description]) => {
            grid.appendChild(createModuleCard(
                type,
                name,
                description,
                selectedModules.has(name),
                () => {
                    if (selectedModules.has(name)) selectedModules.delete(name);
                    else selectedModules.add(name);
                    markConfigurationDirty();
                    renderModules();
                    renderPreview();
                }
            ));
        });
    }

    function renderEconomicTable() {
        const container = byId("economicPossibilities");
        container.replaceChildren();

        possibilities.forEach((possibility, possibilityIndex) => {
            const section = document.createElement("section");
            section.className = "economic-possibility";
            section.style.paddingTop = possibilityIndex === 0 ? "20px" : "26px";
            section.style.borderTop = possibilityIndex === 0 ? "0" : "1px solid #e4edf5";

            const heading = document.createElement("div");
            heading.style.padding = "0 22px";
            heading.style.display = "flex";
            heading.style.alignItems = "center";
            heading.style.justifyContent = "space-between";
            heading.style.gap = "12px";

            const title = document.createElement("h3");
            title.style.margin = "0";
            title.style.color = "#072b4f";
            title.textContent = possibility.title;
            heading.appendChild(title);
            if (possibility.isRecommended) {
                const recommended = document.createElement("span");
                recommended.className = "preview-pill";
                recommended.textContent = "Recomendada";
                heading.appendChild(recommended);
            }
            section.appendChild(heading);

            const tableWrap = document.createElement("div");
            tableWrap.className = "table-wrap";
            const table = document.createElement("table");
            table.dataset.economicLocked = "true";
            const head = document.createElement("thead");
            const headerRow = document.createElement("tr");
            ["Frente", "Descripción", "Cant.", "Valor unitario", "Duración", "IVA", "Venta mensual", "Total contrato"]
                .forEach(label => {
                    const cell = document.createElement("th");
                    cell.textContent = label;
                    headerRow.appendChild(cell);
                });
            head.appendChild(headerRow);
            table.appendChild(head);

            const body = document.createElement("tbody");
            possibility.lines.forEach(line => {
                const row = document.createElement("tr");
                const values = [
                    [line.front || "", ""],
                    [line.description || "", ""],
                    [String(line.quantity || 0), "center"],
                    [money(line.unitSale), "numeric"],
                    [`${line.contractMonths || 0} ${Number(line.contractMonths) === 1 ? "mes" : "meses"}`, "center"],
                    [line.hasVat ? "Sí" : "No", "center"],
                    [money(line.monthlyTotalWithVat), "numeric"],
                    [money(line.contractTotalWithVat), "numeric"]
                ];
                values.forEach(([value, className]) => {
                    const cell = document.createElement("td");
                    cell.textContent = value;
                    if (className) cell.className = className;
                    row.appendChild(cell);
                });
                body.appendChild(row);
            });
            table.appendChild(body);
            tableWrap.appendChild(table);
            section.appendChild(tableWrap);

            const totals = possibilityTotals(possibility);
            const totalsGrid = document.createElement("div");
            totalsGrid.className = "totals-grid";
            const buildTotalsCard = (titleText, subtotal, vat, total) => {
                const card = document.createElement("article");
                card.className = "totals-card";
                const cardTitle = document.createElement("h3");
                cardTitle.textContent = titleText;
                card.appendChild(cardTitle);
                [["Subtotal", subtotal], ["IVA", vat], ["Total", total]].forEach(([label, value], index) => {
                    const row = document.createElement("div");
                    if (index === 2) row.className = "grand";
                    const caption = document.createElement("span");
                    caption.textContent = label;
                    const amount = document.createElement("b");
                    amount.textContent = money(value);
                    row.append(caption, amount);
                    card.appendChild(row);
                });
                return card;
            };
            totalsGrid.append(
                buildTotalsCard("Venta mensual", totals.monthlySubtotal, totals.monthlyVat, totals.monthlyTotal),
                buildTotalsCard("Valor del contrato", totals.contractSubtotal, totals.contractVat, totals.contractTotal));
            section.appendChild(totalsGrid);
            container.appendChild(section);
        });

        setText("heroPossibilityCount", String(possibilities.length));
        setText("heroCurrency", currency());
    }

    function renderValueTabs() {
        const tabs = byId("valueTabs");
        tabs.replaceChildren();
        Object.keys(valueAdded).forEach(type => {
            const button = document.createElement("button");
            button.type = "button";
            button.className = `value-tab${type === activeValueType ? " active" : ""}`;
            button.textContent = type;
            button.addEventListener("click", () => {
                activeValueType = type;
                selectedValueModules.clear();
                renderValueTabs();
                renderValueModules();
            });
            tabs.appendChild(button);
        });
    }

    function renderValueModules() {
        const grid = byId("valueModuleGrid");
        grid.replaceChildren();
        (valueAdded[activeValueType] || []).forEach(([name, description]) => {
            grid.appendChild(createModuleCard(
                activeValueType,
                name,
                description,
                selectedValueModules.has(name),
                () => {
                    if (selectedValueModules.has(name)) selectedValueModules.delete(name);
                    else selectedValueModules.add(name);
                    renderValueModules();
                },
                true
            ));
        });
    }

    function addValueRows() {
        if (!selectedValueModules.size) {
            window.alert("Selecciona primero uno o varios valores agregados.");
            return;
        }
        const candidates = [...selectedValueModules]
            .filter(name => !valueRows.some(row => row.front === activeValueType && row.name === name));
        const availableRows = Math.max(0, valueLimits.maxRows - valueRows.length);
        candidates.slice(0, availableRows).forEach(name => {
            const detail = (valueAdded[activeValueType] || []).find(item => item[0] === name)?.[1] || "";
            valueRows.push(normalizeValueRow({ front: activeValueType, name, detail, modality: "Incluido" }));
        });
        if (candidates.length > availableRows) {
            window.alert(`Puedes incluir hasta ${valueLimits.maxRows} valores agregados por propuesta.`);
        }
        selectedValueModules.clear();
        markConfigurationDirty();
        renderValueModules();
        renderValueRows();
        renderPreview();
    }

    function editableCell(value, maxLength, onInput, wide = false) {
        const cell = document.createElement("td");
        const input = document.createElement("input");
        input.maxLength = maxLength;
        input.value = limitedText(value, maxLength);
        if (wide) input.style.minWidth = "280px";
        input.addEventListener("input", () => {
            onInput(limitedText(input.value, maxLength));
            markConfigurationDirty();
            renderPreview();
        });
        cell.appendChild(input);
        return cell;
    }

    function renderValueRows() {
        const body = byId("valueTable").querySelector("tbody");
        body.replaceChildren();
        valueRows.forEach((item, index) => {
            const row = document.createElement("tr");
            row.appendChild(editableCell(item.front, valueLimits.front, value => { item.front = value; }));
            row.appendChild(editableCell(item.name, valueLimits.name, value => { item.name = value; }));
            row.appendChild(editableCell(item.detail, valueLimits.detail, value => { item.detail = value; }, true));

            const modalityCell = document.createElement("td");
            const select = document.createElement("select");
            ["Incluido", "Opcional"].forEach(value => {
                const option = document.createElement("option");
                option.value = value;
                option.textContent = value;
                option.selected = value === item.modality;
                select.appendChild(option);
            });
            select.addEventListener("change", () => {
                item.modality = select.value;
                markConfigurationDirty();
            });
            modalityCell.appendChild(select);
            row.appendChild(modalityCell);

            const removeCell = document.createElement("td");
            const remove = document.createElement("button");
            remove.type = "button";
            remove.className = "remove-value";
            remove.textContent = "Eliminar";
            remove.addEventListener("click", () => {
                valueRows.splice(index, 1);
                markConfigurationDirty();
                renderValueRows();
                renderPreview();
            });
            removeCell.appendChild(remove);
            row.appendChild(removeCell);
            body.appendChild(row);
        });
        byId("valueTable").hidden = valueRows.length === 0;
        byId("valueEmpty").hidden = valueRows.length > 0;
    }

    function selectedServiceModels() {
        const selected = [...selectedModules];
        if (selected.length) {
            return selected.map(name => ({
                name,
                desc: moduleKnowledge[name]?.desc || "Alcance incluido según la solución seleccionada.",
                range: "Incluido según el alcance económico cotizado.",
                deliverables: moduleKnowledge[name]?.deliverables || []
            }));
        }
        return [...new Set(lines.map(line => line.front).filter(Boolean))].map(name => ({
            name,
            desc: "Frente incluido en la oferta económica de la calculadora.",
            range: "Incluido según el alcance económico cotizado.",
            deliverables: []
        }));
    }

    function renderPreview() {
        const client = fieldValue("clientName") || proposal.scenarioName || "Cliente";
        const duration = fieldValue("contractDuration") || "Según líneas cotizadas";
        setText("heroClient", client);
        setText("previewClient", client);
        setText("previewType", fieldValue("proposalType") || "Mixta");
        setText("previewSummary", fieldValue("summary"));
        setText("previewSeller", fieldValue("sellerName") || "Digital Tech Colombia");
        setText("previewDuration", duration);
        setText("previewPossibilityCount", `${possibilities.length} ${possibilities.length === 1 ? "escenario" : "escenarios"}`);

        const modules = byId("previewModules");
        modules.replaceChildren();
        selectedServiceModels().forEach(service => {
            const chip = document.createElement("span");
            chip.textContent = service.name;
            modules.appendChild(chip);
        });
    }

    function buildQuoteId() {
        const year = new Date().getFullYear();
        const compact = String(firstDefined(proposal, ["groupId", "scenarioGroupId", "scenarioId"], ""))
            .replace(/[^a-z0-9]/gi, "")
            .slice(-8)
            .toUpperCase();
        return `DT-${year}-${compact || "OFERTA"}`;
    }

    function gatherProposalData() {
        const services = selectedServiceModels();
        const selectedNames = new Set(services.map(service => service.name));
        const cross = allCatalogModules
            .filter(name => !selectedNames.has(name))
            .map(name => ({ name, desc: moduleKnowledge[name]?.desc || "Solución disponible en el portafolio Digital Tech." }));
        const year = new Date().getFullYear();
        const mapItems = possibility => possibility.lines.map(line => ({
            front: line.front || "",
            desc: line.description || "",
            qty: Number(line.quantity || 0),
            unitPrice: money(line.unitSale),
            months: Number(line.contractMonths || 0),
            iva: Boolean(line.hasVat),
            monthlyTotal: money(line.monthlyTotalWithVat),
            contractTotal: money(line.contractTotalWithVat)
        }));
        const proposalModels = possibilities.map(possibility => {
            const totals = possibilityTotals(possibility);
            return {
                possibilityId: possibility.possibilityId,
                title: possibility.title,
                isRecommended: possibility.isRecommended,
                items: mapItems(possibility),
                monthlySubtotal: money(totals.monthlySubtotal),
                monthlyIva: money(totals.monthlyVat),
                monthlyTotal: money(totals.monthlyTotal),
                contractSubtotal: money(totals.contractSubtotal),
                contractIva: money(totals.contractVat),
                contractTotal: money(totals.contractTotal)
            };
        });
        const first = proposalModels[0] || {
            items: [],
            monthlySubtotal: money(0),
            monthlyIva: money(0),
            monthlyTotal: money(0),
            contractSubtotal: money(0),
            contractIva: money(0),
            contractTotal: money(0)
        };
        return {
            consecutivo: buildQuoteId(),
            anio: String(year),
            cliente: fieldValue("clientName") || "Cliente",
            nit: fieldValue("clientNit"),
            contacto: fieldValue("clientContact"),
            comercial: fieldValue("sellerName") || "Digital Tech Colombia",
            comercial_mail: fieldValue("sellerEmail"),
            comercial_tel: "",
            tipo: fieldValue("proposalType") || "Mixta",
            moneda: currency(),
            vigencia: fieldValue("validity") || "15 días calendario",
            formaPago: fieldValue("paymentTerms") || "A definir",
            tiempoEntrega: fieldValue("deliveryTime") || "A definir",
            tiempoContrato: fieldValue("contractDuration") || "Según líneas cotizadas",
            resumen: fieldValue("summary"),
            notas: fieldValue("notes"),
            servicios: services,
            items: first.items,
            proposals: proposalModels,
            valoresAgregados: valueRows.slice(0, valueLimits.maxRows).map(normalizeValueRow).map(row => ({
                front: row.front,
                name: row.name,
                detail: row.detail,
                modalidad: row.modality
            })),
            monthlySubtotal: first.monthlySubtotal,
            monthlyIva: first.monthlyIva,
            monthlyTotal: first.monthlyTotal,
            contractSubtotal: first.contractSubtotal,
            contractIva: first.contractIva,
            contractTotal: first.contractTotal,
            cross
        };
    }

    function gatherConfiguration() {
        return {
            schemaVersion: 1,
            clientName: fieldValue("clientName"),
            clientNit: fieldValue("clientNit"),
            clientContact: fieldValue("clientContact"),
            proposalType: fieldValue("proposalType") || "Mixta",
            currency: currency(),
            validity: fieldValue("validity"),
            paymentTerms: fieldValue("paymentTerms"),
            deliveryTime: fieldValue("deliveryTime"),
            summary: fieldValue("summary"),
            notes: fieldValue("notes"),
            selectedModules: [...selectedModules],
            valueAdded: valueRows.slice(0, valueLimits.maxRows).map(normalizeValueRow).map(row => ({
                front: row.front,
                name: row.name,
                detail: row.detail,
                modality: row.modality
            }))
        };
    }

    function parsePersistedConfiguration() {
        if (!persistedConfiguration) return null;
        if (typeof persistedConfiguration === "string") {
            try {
                return JSON.parse(persistedConfiguration);
            } catch {
                return null;
            }
        }
        return persistedConfiguration;
    }

    function hydratePersistedConfiguration() {
        const snapshot = parsePersistedConfiguration();
        if (!snapshot || typeof snapshot !== "object") return false;
        const stored = snapshot.configuration && typeof snapshot.configuration === "object"
            ? snapshot.configuration
            : snapshot;
        const fields = stored.fields && typeof stored.fields === "object" ? stored.fields : stored;
        const mappings = [
            ["clientName", ["clientName", "cliente"]],
            ["clientNit", ["clientNit", "nit"]],
            ["clientContact", ["clientContact", "contacto"]],
            ["proposalType", ["proposalType", "tipo"]],
            ["currency", ["currency", "moneda"]],
            ["validity", ["validity", "vigencia"]],
            ["paymentTerms", ["paymentTerms", "formaPago"]],
            ["deliveryTime", ["deliveryTime", "tiempoEntrega"]],
            ["summary", ["summary", "resumen"]],
            ["notes", ["notes", "notas"]]
        ];
        mappings.forEach(([elementId, names]) => {
            const value = firstDefined(fields, names, undefined);
            const element = byId(elementId);
            if (element && value !== undefined && value !== null) {
                const maxLength = Number(element.maxLength);
                element.value = maxLength > 0 ? limitedText(value, maxLength) : String(value);
            }
        });

        const modules = firstDefined(stored, ["selectedModules", "modules"], []);
        if (Array.isArray(modules)) {
            modules.forEach(module => {
                const name = typeof module === "string" ? module : firstDefined(module, ["name", "moduleName"], "");
                if (name) selectedModules.add(String(name));
            });
        }

        const storedValues = firstDefined(stored, ["valueAdded", "valueRows", "vaRows", "values"], []);
        if (Array.isArray(storedValues)) {
            storedValues.slice(0, valueLimits.maxRows).forEach(item => {
                const normalized = normalizeValueRow(item);
                if (!normalized.name) return;
                valueRows.push(normalized);
            });
        }
        return true;
    }

    function currentGroupId() {
        return String(firstDefined(proposal, ["groupId", "scenarioGroupId", "scenarioId"], "")).trim();
    }

    function currentEconomicHash() {
        const groupHash = String(firstDefined(proposal, ["economicHash", "calculationHash"], "")).trim();
        if (groupHash) return groupHash;
        return possibilities.map(item => item.economicHash).filter(Boolean).join(".");
    }

    async function persistProposalExport(pdfBlob, fileName, configurationJson, idempotencyKey) {
        const form = new FormData();
        form.append("groupId", currentGroupId());
        form.append("scenarioId", String(firstDefined(proposal, ["scenarioId"], "")).trim());
        form.append("crmDealId", String(firstDefined(proposal, ["crmDealId"], "")).trim());
        form.append("economicHash", currentEconomicHash());
        form.append("idempotencyKey", idempotencyKey);
        form.append("configurationJson", configurationJson);
        form.append("fileName", fileName);
        form.append("pdf", pdfBlob, fileName);

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
        const headers = { "Accept": "application/json" };
        if (token) headers.RequestVerificationToken = token;
        const response = await fetch("/Calculator/ProposalExports", {
            method: "POST",
            credentials: "same-origin",
            headers,
            body: form
        });
        const responseText = await response.text();
        let result = null;
        if (responseText) {
            try { result = JSON.parse(responseText); } catch { result = null; }
        }
        if (!response.ok) {
            throw new Error(result?.message || result?.detail || responseText || "No fue posible guardar la exportación.");
        }
        return result || {};
    }

    function downloadPdf(blob, fileName) {
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        window.setTimeout(() => URL.revokeObjectURL(url), 1500);
    }

    function downloadPersistedPdf(downloadUrl, fallbackBlob, fileName) {
        if (!downloadUrl) {
            downloadPdf(fallbackBlob, fileName);
            return;
        }
        const resolved = new URL(downloadUrl, window.location.origin);
        if (resolved.origin !== window.location.origin) {
            throw new Error("El servidor devolvió una ruta de descarga no válida.");
        }
        const link = document.createElement("a");
        link.href = `${resolved.pathname}${resolved.search}${resolved.hash}`;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
    }

    async function generatePdf() {
        const buttons = [byId("generatePdfButton"), byId("heroGeneratePdf")].filter(Boolean);
        const status = byId("generationStatus");
        buttons.forEach(button => { button.disabled = true; });
        status.classList.remove("error");
        status.textContent = "Preparando el PDF profesional…";
        try {
            if (typeof window.generateProposalPdf !== "function") {
                throw new Error("El generador de PDF no está disponible.");
            }
            if (!currentGroupId()) {
                throw new Error("La propuesta no está asociada a un grupo de escenario válido.");
            }
            const proposalData = gatherProposalData();
            const configuration = gatherConfiguration();
            const configurationJson = JSON.stringify(configuration);
            const bytes = await window.generateProposalPdf(proposalData);
            const blob = new Blob([bytes], { type: "application/pdf" });
            const client = (fieldValue("clientName") || "Cliente").replace(/[^a-z0-9]+/gi, "_");
            const defaultFileName = `Cotizacion_${client}_DigitalTech.pdf`;
            if (!pendingExportIdempotencyKey
                || pendingExportConfigurationJson !== configurationJson) {
                pendingExportIdempotencyKey = newIdempotencyKey();
                pendingExportConfigurationJson = configurationJson;
            }
            status.textContent = "Guardando configuración e historial de la exportación…";
            const persisted = await persistProposalExport(
                blob,
                defaultFileName,
                configurationJson,
                pendingExportIdempotencyKey);
            const returnedName = String(persisted.fileName || defaultFileName).split(/[\\/]/).pop();
            downloadPersistedPdf(persisted.downloadUrl, blob, returnedName || defaultFileName);
            pendingExportIdempotencyKey = "";
            pendingExportConfigurationJson = "";
            const versionLabel = persisted.version ? ` (versión ${persisted.version})` : "";
            status.textContent = `Oferta guardada y descargada correctamente${versionLabel}.`;
        } catch (error) {
            status.classList.add("error");
            status.textContent = `No se pudo generar el PDF: ${error?.message || error}`;
        } finally {
            buttons.forEach(button => { button.disabled = false; });
        }
    }

    function initializeDuration() {
        const months = [...new Set(lines.map(line => Number(line.contractMonths || 0)).filter(value => value > 0))].sort((a, b) => a - b);
        byId("contractDuration").value = months.length === 1
            ? `${months[0]} meses`
            : months.length > 1
                ? `De ${months[0]} a ${months.at(-1)} meses, según línea`
                : "Según líneas cotizadas";
    }

    function bindEvents() {
        byId("proposalType").addEventListener("change", () => {
            selectedModules.clear();
            markConfigurationDirty();
            renderModules();
            renderPreview();
        });
        byId("currency").addEventListener("change", () => {
            markConfigurationDirty();
            renderEconomicTable();
            renderPreview();
        });
        ["clientName", "clientNit", "clientContact", "validity", "paymentTerms", "deliveryTime", "summary", "notes"]
            .forEach(id => byId(id)?.addEventListener("input", () => {
                markConfigurationDirty();
                renderPreview();
            }));
        byId("addValueButton").addEventListener("click", addValueRows);
        byId("generatePdfButton").addEventListener("click", generatePdf);
        byId("heroGeneratePdf").addEventListener("click", generatePdf);
    }

    initializeDuration();
    const configurationHydrated = hydratePersistedConfiguration();
    if (!configurationHydrated) inferInitialSelection();
    bindEvents();
    renderModules();
    renderEconomicTable();
    renderValueTabs();
    renderValueModules();
    renderValueRows();
    renderPreview();
})();
