(function () {
    const numberFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });

    const percentFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });

    const moduleRoot = document.getElementById("soporteCloudModuleShell");
    if (moduleRoot) {
        initializeSupportCloudModuleTabs(moduleRoot);
    }

    const adminRoot = document.getElementById("soporteCloudSurveyAdmin");
    if (adminRoot) {
        initializeSurveyAdmin(adminRoot);
    }

    const publicRoot = document.getElementById("soporteCloudPublicSurvey");
    if (publicRoot) {
        initializePublicSurvey(publicRoot);
    }

    function initializeSupportCloudModuleTabs(root) {
        const tabs = Array.from(root.querySelectorAll("[data-scs-module-tab]"));
        const panels = Array.from(root.querySelectorAll("[data-scs-module-panel]"));
        if (!tabs.length || !panels.length) {
            return;
        }

        const resolveInitialKey = () => {
            const hash = normalizeText(window.location.hash.replace("#", ""));
            if (hash === "capacitaciones") {
                return "capacitaciones";
            }
            if (hash === "reportes") {
                return "reportes";
            }

            return "soporte";
        };

        const setActive = (key, updateHash) => {
            const activeKey = key === "capacitaciones" || key === "reportes" ? key : "soporte";
            tabs.forEach(tab => {
                const isActive = tab.dataset.scsModuleTab === activeKey;
                tab.classList.toggle("is-active", isActive);
                tab.setAttribute("aria-selected", isActive ? "true" : "false");
            });
            panels.forEach(panel => {
                const isActive = panel.dataset.scsModulePanel === activeKey;
                panel.classList.toggle("is-active", isActive);
                panel.hidden = !isActive;
            });
            if (updateHash) {
                const nextHash = activeKey === "capacitaciones"
                    ? "#capacitaciones"
                    : activeKey === "reportes"
                        ? "#reportes"
                        : "#soporte-cloud";
                if (window.location.hash !== nextHash) {
                    history.replaceState(null, "", nextHash);
                }
            }

            root.dispatchEvent(new CustomEvent("supportcloud:modulechange", {
                detail: { activeKey }
            }));
        };

        tabs.forEach(tab => {
            tab.addEventListener("click", () => {
                setActive(tab.dataset.scsModuleTab || "soporte", true);
            });
        });

        window.addEventListener("hashchange", () => setActive(resolveInitialKey(), false));
        setActive(resolveInitialKey(), false);
    }

    function initializeSurveyAdmin(root) {
        const urls = {
            board: root.dataset.boardUrl || "",
            saveTopic: root.dataset.saveTopicUrl || "",
            saveQuestion: root.dataset.saveQuestionUrl || "",
            saveSession: root.dataset.saveSessionUrl || "",
            closeSession: root.dataset.closeSessionUrl || "",
            sessionDetail: root.dataset.sessionDetailUrl || "",
            export: root.dataset.exportUrl || "",
            clientSearch: root.dataset.clientSearchUrl || ""
        };

        const els = {
            status: root.querySelector("[data-scs-status]"),
            refresh: root.querySelector("[data-scs-refresh]"),
            totalSessions: root.querySelector("[data-scs-total-sessions]"),
            openSessions: root.querySelector("[data-scs-open-sessions]"),
            totalResponses: root.querySelector("[data-scs-total-responses]"),
            averageScore: root.querySelector("[data-scs-average-score]"),
            sessionRows: root.querySelector("[data-scs-session-rows]"),
            sessionDetail: root.querySelector("[data-scs-session-detail]"),
            bestQuestions: root.querySelector("[data-scs-best-questions]"),
            weakQuestions: root.querySelector("[data-scs-weak-questions]"),
            qrPanel: root.querySelector("[data-scs-qr-panel]"),
            qrImage: root.querySelector("[data-scs-qr-image]"),
            publicLink: root.querySelector("[data-scs-public-link]"),
            exportLink: root.querySelector("[data-scs-export]"),
            closeSession: root.querySelector("[data-scs-close-session]"),
            sessionForm: root.querySelector("[data-scs-session-form]"),
            sessionId: root.querySelector("[data-scs-session-id]"),
            sessionName: root.querySelector("[data-scs-session-name]"),
            sessionDate: root.querySelector("[data-scs-session-date]"),
            sessionTopic: root.querySelector("[data-scs-session-topic]"),
            sessionMeta: root.querySelector("[data-scs-session-meta]"),
            clientId: root.querySelector("[data-scs-client-id]"),
            clientName: root.querySelector("[data-scs-client-name]"),
            clientOptions: root.querySelector("[data-scs-client-options]"),
            resetSession: root.querySelector("[data-scs-reset-session]"),
            topicForm: root.querySelector("[data-scs-topic-form]"),
            topicId: root.querySelector("[data-scs-topic-id]"),
            topicName: root.querySelector("[data-scs-topic-name]"),
            topicDescription: root.querySelector("[data-scs-topic-description]"),
            topicActive: root.querySelector("[data-scs-topic-active]"),
            topicMeta: root.querySelector("[data-scs-topic-meta]"),
            resetTopic: root.querySelector("[data-scs-reset-topic]"),
            topicsList: root.querySelector("[data-scs-topics-list]"),
            questionForm: root.querySelector("[data-scs-question-form]"),
            questionId: root.querySelector("[data-scs-question-id]"),
            questionComponent: root.querySelector("[data-scs-question-component]"),
            questionTopic: root.querySelector("[data-scs-question-topic]"),
            questionType: root.querySelector("[data-scs-question-type]"),
            questionPoints: root.querySelector("[data-scs-question-points]"),
            questionOrder: root.querySelector("[data-scs-question-order]"),
            questionActive: root.querySelector("[data-scs-question-active]"),
            questionText: root.querySelector("[data-scs-question-text]"),
            questionMeta: root.querySelector("[data-scs-question-meta]"),
            options: root.querySelector("[data-scs-options]"),
            addOption: root.querySelector("[data-scs-add-option]"),
            resetQuestion: root.querySelector("[data-scs-reset-question]"),
            questionsList: root.querySelector("[data-scs-questions-list]")
        };

        const state = {
            board: null,
            selectedSessionId: "",
            detail: null,
            optionsDraft: [],
            clientSuggestions: [],
            clientTimer: 0
        };

        els.refresh?.addEventListener("click", () => loadBoard());
        els.sessionForm?.addEventListener("submit", event => {
            event.preventDefault();
            saveSession();
        });
        els.topicForm?.addEventListener("submit", event => {
            event.preventDefault();
            saveTopic();
        });
        els.questionForm?.addEventListener("submit", event => {
            event.preventDefault();
            saveQuestion();
        });
        els.resetSession?.addEventListener("click", resetSessionForm);
        els.resetTopic?.addEventListener("click", resetTopicForm);
        els.resetQuestion?.addEventListener("click", resetQuestionForm);
        els.addOption?.addEventListener("click", () => {
            state.optionsDraft.push(createEmptyOption());
            renderOptionsDraft();
        });
        els.questionComponent?.addEventListener("change", syncQuestionControls);
        els.questionType?.addEventListener("change", syncQuestionControls);
        els.closeSession?.addEventListener("click", closeSelectedSession);
        els.sessionRows?.addEventListener("click", event => {
            const row = event.target instanceof HTMLElement ? event.target.closest("[data-scs-session-row]") : null;
            if (!row) {
                return;
            }
            selectSession(row.dataset.scsSessionRow || "");
        });
        els.sessionRows?.addEventListener("keydown", event => {
            if (event.key !== "Enter" && event.key !== " ") {
                return;
            }
            const row = event.target instanceof HTMLElement ? event.target.closest("[data-scs-session-row]") : null;
            if (!row) {
                return;
            }
            event.preventDefault();
            selectSession(row.dataset.scsSessionRow || "");
        });
        els.topicsList?.addEventListener("click", event => {
            const button = event.target instanceof HTMLElement ? event.target.closest("[data-scs-edit-topic]") : null;
            if (!button) {
                return;
            }
            fillTopicForm(button.dataset.scsEditTopic || "");
        });
        els.questionsList?.addEventListener("click", event => {
            const button = event.target instanceof HTMLElement ? event.target.closest("[data-scs-edit-question]") : null;
            if (!button) {
                return;
            }
            fillQuestionForm(button.dataset.scsEditQuestion || "");
        });
        els.clientName?.addEventListener("input", handleClientLookup);
        els.clientName?.addEventListener("change", syncClientSelection);
        els.clientName?.addEventListener("blur", syncClientSelection);

        resetSessionForm();
        resetTopicForm();
        resetQuestionForm();
        loadBoard();

        async function loadBoard() {
            if (!urls.board) {
                return;
            }

            setStatus("info", "Cargando encuestas de capacitacion...");
            try {
                state.board = await fetchJson(urls.board);
                renderBoard();
                setStatus("success", state.board?.message || "Encuestas cargadas.");
            } catch (error) {
                setStatus("error", buildErrorMessage(error));
            }
        }

        function renderBoard() {
            const board = state.board || {};
            setText(els.totalSessions, numberFormatter.format(Number(board.totalSessions || 0)));
            setText(els.openSessions, numberFormatter.format(Number(board.openSessions || 0)));
            setText(els.totalResponses, numberFormatter.format(Number(board.totalResponses || 0)));
            setText(els.averageScore, `${percentFormatter.format(Number(board.averageScorePercent || 0))}%`);
            renderTopicSelects();
            renderPickLists();
            renderSessions();
            renderQuestionBreakdown(els.bestQuestions, board.bestQuestions, "Sin preguntas calificadas.");
            renderQuestionBreakdown(els.weakQuestions, board.weakQuestions, "Sin preguntas calificadas.");

            const sessions = Array.isArray(board.sessions) ? board.sessions : [];
            if (!state.selectedSessionId && sessions.length) {
                state.selectedSessionId = sessions[0].sessionId || "";
            }
            if (state.selectedSessionId) {
                selectSession(state.selectedSessionId, { preserveForm: true });
            }
        }

        function renderTopicSelects() {
            const topics = Array.isArray(state.board?.topics) ? state.board.topics : [];
            const options = [
                '<option value="">Selecciona...</option>',
                ...topics.filter(topic => topic.isActive !== false).map(topic => `<option value="${escapeHtml(topic.topicId)}">${escapeHtml(topic.name)}</option>`)
            ].join("");
            if (els.sessionTopic) {
                const current = els.sessionTopic.value;
                els.sessionTopic.innerHTML = options;
                els.sessionTopic.value = current;
            }
            if (els.questionTopic) {
                const current = els.questionTopic.value;
                els.questionTopic.innerHTML = options;
                els.questionTopic.value = current;
            }
        }

        function renderPickLists() {
            const topics = Array.isArray(state.board?.topics) ? state.board.topics : [];
            if (els.topicsList) {
                els.topicsList.innerHTML = topics.length
                    ? topics.map(topic => `
                        <button type="button" class="support-cloud-survey-pick" data-scs-edit-topic="${escapeHtml(topic.topicId)}">
                            <strong>${escapeHtml(topic.name || "Tema")}</strong>
                            <span>${escapeHtml(numberFormatter.format(Number(topic.knowledgeQuestionCount || 0)))} preguntas · ${topic.isActive === false ? "Inactivo" : "Activo"}</span>
                        </button>
                    `).join("")
                    : '<div class="support-cloud-placeholder support-cloud-placeholder--compact">Sin temas.</div>';
            }

            const questions = Array.isArray(state.board?.questions) ? state.board.questions : [];
            if (els.questionsList) {
                els.questionsList.innerHTML = questions.length
                    ? questions.map(question => `
                        <button type="button" class="support-cloud-survey-pick" data-scs-edit-question="${escapeHtml(question.questionId)}">
                            <strong>${escapeHtml(question.text || "Pregunta")}</strong>
                            <span>${escapeHtml(question.componentLabel || "")} · ${escapeHtml(question.topicName || "Estandar")} · ${question.isActive === false ? "Inactiva" : "Activa"}</span>
                        </button>
                    `).join("")
                    : '<div class="support-cloud-placeholder support-cloud-placeholder--compact">Sin preguntas.</div>';
            }
        }

        function renderSessions() {
            const sessions = Array.isArray(state.board?.sessions) ? state.board.sessions : [];
            if (!els.sessionRows) {
                return;
            }

            if (!sessions.length) {
                els.sessionRows.innerHTML = '<tr><td colspan="7" class="support-cloud-table__empty">No hay sesiones registradas.</td></tr>';
                return;
            }

            els.sessionRows.innerHTML = sessions.map(session => `
                <tr class="support-cloud-table__row ${session.sessionId === state.selectedSessionId ? "is-selected" : ""}" tabindex="0" data-scs-session-row="${escapeHtml(session.sessionId)}">
                    <td data-label="Fecha">${escapeHtml(session.dateDisplay || "-")}</td>
                    <td data-label="Sesion">
                        <div class="support-cloud-table__ticket">
                            <div class="support-cloud-table__ticket-title">${escapeHtml(session.name || "-")}</div>
                            <div class="support-cloud-table__ticket-description">${escapeHtml(session.code || "")}</div>
                        </div>
                    </td>
                    <td data-label="Cliente">${escapeHtml(session.clientName || "-")}</td>
                    <td data-label="Tema">${renderPill(session.topicName || "Sin tema")}</td>
                    <td data-label="Estado">${renderPill(session.stateLabel || "-")}</td>
                    <td data-label="Respuestas" class="text-end support-cloud-table__hours">${escapeHtml(numberFormatter.format(Number(session.completedCount || 0)))}</td>
                    <td data-label="Promedio" class="text-end support-cloud-table__hours">${escapeHtml(percentFormatter.format(Number(session.averageScorePercent || 0)))}%</td>
                </tr>
            `).join("");
        }

        async function selectSession(sessionId, options = {}) {
            const session = findSession(sessionId);
            if (!session) {
                return;
            }

            state.selectedSessionId = session.sessionId;
            renderSessions();
            renderQr(session);
            if (!options.preserveForm) {
                fillSessionForm(session);
            }
            await loadSessionDetail(session.sessionId);
        }

        function renderQr(session) {
            if (!els.qrPanel || !els.qrImage || !els.publicLink || !els.exportLink) {
                return;
            }

            const publicUrl = session.publicUrl || "";
            els.qrPanel.hidden = !publicUrl;
            els.qrImage.src = publicUrl ? `${publicUrl}/Qr` : "";
            els.publicLink.href = publicUrl || "#";
            els.publicLink.textContent = publicUrl || "Sin enlace";
            els.exportLink.href = buildUrl(urls.export, { sessionId: session.sessionId });
            if (els.closeSession) {
                els.closeSession.disabled = Number(session.stateValue || 0) === 645250002;
            }
        }

        async function loadSessionDetail(sessionId) {
            if (!urls.sessionDetail || !sessionId) {
                return;
            }

            try {
                state.detail = await fetchJson(buildUrl(urls.sessionDetail, { sessionId }));
                renderSessionDetail();
            } catch (error) {
                if (els.sessionDetail) {
                    els.sessionDetail.innerHTML = `<div class="support-cloud-placeholder">${escapeHtml(buildErrorMessage(error))}</div>`;
                }
            }
        }

        function renderSessionDetail() {
            const detail = state.detail || {};
            const leaderboard = Array.isArray(detail.leaderboard) ? detail.leaderboard : [];
            const knowledgeStats = Array.isArray(detail.knowledgeQuestionStats) ? detail.knowledgeQuestionStats : [];
            const satisfactionStats = Array.isArray(detail.satisfactionQuestionStats) ? detail.satisfactionQuestionStats : [];
            if (!els.sessionDetail) {
                return;
            }

            els.sessionDetail.innerHTML = `
                <div class="support-cloud-survey-stat-columns">
                    <div>
                        <div class="support-cloud-kicker">Ganadores</div>
                        ${renderLeaderboard(leaderboard)}
                    </div>
                    <div>
                        <div class="support-cloud-kicker">Preguntas</div>
                        ${renderStatsList(knowledgeStats, "Sin respuestas de conocimiento.")}
                    </div>
                    <div>
                        <div class="support-cloud-kicker">Satisfaccion</div>
                        ${renderSatisfactionStats(satisfactionStats)}
                    </div>
                </div>
            `;
        }

        function renderLeaderboard(items) {
            if (!items.length) {
                return '<div class="support-cloud-placeholder support-cloud-placeholder--compact">Sin participantes.</div>';
            }

            return `
                <ol class="support-cloud-leaderboard">
                    ${items.map((item, index) => `
                        <li>
                            <span>${index + 1}</span>
                            <strong>${escapeHtml(item.fullName || "Participante")}</strong>
                            <em>${escapeHtml(percentFormatter.format(Number(item.scorePercent || 0)))}%</em>
                        </li>
                    `).join("")}
                </ol>
            `;
        }

        function renderStatsList(items, emptyMessage) {
            if (!items.length) {
                return `<div class="support-cloud-placeholder support-cloud-placeholder--compact">${escapeHtml(emptyMessage)}</div>`;
            }

            return items.map(item => `
                <div class="support-cloud-survey-stat-row">
                    <strong>${escapeHtml(item.questionText || "-")}</strong>
                    <span>${escapeHtml(numberFormatter.format(Number(item.totalAnswers || 0)))} resp. · ${escapeHtml(percentFormatter.format(Number(item.correctPercent || 0)))}% correcto</span>
                </div>
            `).join("");
        }

        function renderSatisfactionStats(items) {
            if (!items.length) {
                return '<div class="support-cloud-placeholder support-cloud-placeholder--compact">Sin respuestas de satisfaccion.</div>';
            }

            return items.map(item => `
                <div class="support-cloud-survey-stat-row">
                    <strong>${escapeHtml(item.questionText || "-")}</strong>
                    <span>${escapeHtml(numberFormatter.format(Number(item.averageRating || 0)))} / 5 · ${escapeHtml(numberFormatter.format(Number(item.totalAnswers || 0)))} resp.</span>
                </div>
            `).join("");
        }

        function renderQuestionBreakdown(container, items, emptyMessage) {
            if (!container) {
                return;
            }

            const rows = Array.isArray(items) ? items : [];
            if (!rows.length) {
                container.innerHTML = `<div class="support-cloud-placeholder">${escapeHtml(emptyMessage)}</div>`;
                return;
            }

            container.innerHTML = rows.map(item => {
                const width = Math.max(6, Math.min(100, Math.round(Number(item.correctPercent || 0))));
                return `
                    <div class="support-cloud-breakdown__row">
                        <div class="support-cloud-breakdown__head">
                            <span class="support-cloud-breakdown__label">${escapeHtml(item.questionText || "Pregunta")}</span>
                            <span class="support-cloud-breakdown__value">${escapeHtml(percentFormatter.format(Number(item.correctPercent || 0)))}% · ${escapeHtml(numberFormatter.format(Number(item.totalAnswers || 0)))} resp.</span>
                        </div>
                        <div class="support-cloud-breakdown__track">
                            <span class="support-cloud-breakdown__fill" style="width:${width}%"></span>
                        </div>
                    </div>
                `;
            }).join("");
        }

        async function saveTopic() {
            const payload = {
                topicId: els.topicId?.value || "",
                name: (els.topicName?.value || "").trim(),
                description: (els.topicDescription?.value || "").trim(),
                isActive: (els.topicActive?.value || "true") === "true"
            };
            await saveAndRefresh(urls.saveTopic, payload, resetTopicForm);
        }

        async function saveQuestion() {
            const componentValue = Number(els.questionComponent?.value || 645250000);
            const typeValue = Number(els.questionType?.value || 645250000);
            const payload = {
                questionId: els.questionId?.value || "",
                topicId: componentValue === 645250000 ? (els.questionTopic?.value || "") : "",
                componentValue,
                inputTypeValue: typeValue,
                text: (els.questionText?.value || "").trim(),
                sortOrder: Number(els.questionOrder?.value || 0),
                maxPoints: Number(els.questionPoints?.value || 0),
                isActive: (els.questionActive?.value || "true") === "true",
                options: typeValue === 645250000 ? readOptionsDraft() : []
            };
            await saveAndRefresh(urls.saveQuestion, payload, resetQuestionForm);
        }

        async function saveSession() {
            const payload = {
                sessionId: els.sessionId?.value || "",
                name: (els.sessionName?.value || "").trim(),
                topicId: els.sessionTopic?.value || "",
                clientId: els.clientId?.value || "",
                clientName: (els.clientName?.value || "").trim(),
                dateValue: els.sessionDate?.value || ""
            };
            await saveAndRefresh(urls.saveSession, payload, resetSessionForm);
        }

        async function saveAndRefresh(url, payload, reset) {
            if (!url) {
                return;
            }

            setStatus("info", "Guardando...");
            try {
                const result = await fetchJson(url, {
                    method: "POST",
                    body: JSON.stringify(payload)
                });
                state.board = result?.board || state.board;
                reset();
                renderBoard();
                setStatus("success", result?.message || "Guardado correctamente.");
            } catch (error) {
                setStatus("error", buildErrorMessage(error));
            }
        }

        async function closeSelectedSession() {
            const session = findSession(state.selectedSessionId);
            if (!session || !urls.closeSession) {
                return;
            }

            setStatus("info", "Cerrando encuesta...");
            try {
                const result = await fetchJson(buildUrl(urls.closeSession, { sessionId: session.sessionId }), { method: "POST" });
                state.board = result?.board || state.board;
                renderBoard();
                setStatus("success", result?.message || "Encuesta cerrada.");
            } catch (error) {
                setStatus("error", buildErrorMessage(error));
            }
        }

        function resetSessionForm() {
            setValue(els.sessionId, "");
            setValue(els.sessionName, "");
            setValue(els.sessionDate, new Date().toISOString().slice(0, 10));
            setValue(els.sessionTopic, "");
            setValue(els.clientId, "");
            setValue(els.clientName, "");
            setText(els.sessionMeta, "Nueva sesion");
        }

        function fillSessionForm(session) {
            setValue(els.sessionId, session.sessionId || "");
            setValue(els.sessionName, session.name || "");
            setValue(els.sessionDate, session.dateValue || "");
            setValue(els.sessionTopic, session.topicId || "");
            setValue(els.clientId, session.clientId || "");
            setValue(els.clientName, session.clientName || "");
            setText(els.sessionMeta, `Codigo ${session.code || "-"}`);
        }

        function resetTopicForm() {
            setValue(els.topicId, "");
            setValue(els.topicName, "");
            setValue(els.topicDescription, "");
            setValue(els.topicActive, "true");
            setText(els.topicMeta, "Nuevo tema");
        }

        function fillTopicForm(topicId) {
            const topic = (Array.isArray(state.board?.topics) ? state.board.topics : [])
                .find(item => item.topicId === topicId);
            if (!topic) {
                return;
            }

            setValue(els.topicId, topic.topicId || "");
            setValue(els.topicName, topic.name || "");
            setValue(els.topicDescription, topic.description || "");
            setValue(els.topicActive, topic.isActive === false ? "false" : "true");
            setText(els.topicMeta, "Editando tema");
        }

        function resetQuestionForm() {
            setValue(els.questionId, "");
            setValue(els.questionComponent, "645250000");
            setValue(els.questionTopic, "");
            setValue(els.questionType, "645250000");
            setValue(els.questionPoints, "1");
            setValue(els.questionOrder, "0");
            setValue(els.questionActive, "true");
            setValue(els.questionText, "");
            setText(els.questionMeta, "Nueva pregunta");
            state.optionsDraft = [createEmptyOption(true), createEmptyOption(false)];
            renderOptionsDraft();
            syncQuestionControls();
        }

        function fillQuestionForm(questionId) {
            const question = (Array.isArray(state.board?.questions) ? state.board.questions : [])
                .find(item => item.questionId === questionId);
            if (!question) {
                return;
            }

            setValue(els.questionId, question.questionId || "");
            setValue(els.questionComponent, String(question.componentValue || 645250000));
            setValue(els.questionTopic, question.topicId || "");
            setValue(els.questionType, String(question.inputTypeValue || 645250000));
            setValue(els.questionPoints, String(question.maxPoints ?? 0));
            setValue(els.questionOrder, String(question.sortOrder || 0));
            setValue(els.questionActive, question.isActive === false ? "false" : "true");
            setValue(els.questionText, question.text || "");
            setText(els.questionMeta, "Editando pregunta");
            state.optionsDraft = Array.isArray(question.options) && question.options.length
                ? question.options.map(option => ({ ...option }))
                : [createEmptyOption(true), createEmptyOption(false)];
            renderOptionsDraft();
            syncQuestionControls();
        }

        function renderOptionsDraft() {
            if (!els.options) {
                return;
            }

            els.options.innerHTML = state.optionsDraft.map((option, index) => `
                <div class="support-cloud-survey-option" data-scs-option-index="${index}">
                    <input type="hidden" data-scs-option-id value="${escapeHtml(option.optionId || "")}" />
                    <input type="text" class="form-control" placeholder="Opcion" data-scs-option-text value="${escapeHtml(option.text || "")}" />
                    <label class="support-cloud-survey-option__check">
                        <input type="checkbox" data-scs-option-correct ${option.isCorrect ? "checked" : ""} />
                        Correcta
                    </label>
                    <input type="number" min="0" step="0.01" class="form-control" data-scs-option-points value="${escapeHtml(option.points ?? 0)}" />
                    <button type="button" class="btn btn-outline-secondary" data-scs-remove-option>Quitar</button>
                </div>
            `).join("");

            els.options.querySelectorAll("[data-scs-remove-option]").forEach(button => {
                button.addEventListener("click", () => {
                    const row = button.closest("[data-scs-option-index]");
                    const index = Number(row?.dataset.scsOptionIndex || -1);
                    if (index >= 0) {
                        state.optionsDraft.splice(index, 1);
                        renderOptionsDraft();
                    }
                });
            });
        }

        function readOptionsDraft() {
            return Array.from(els.options?.querySelectorAll("[data-scs-option-index]") || []).map((row, index) => ({
                optionId: row.querySelector("[data-scs-option-id]")?.value || "",
                text: (row.querySelector("[data-scs-option-text]")?.value || "").trim(),
                isCorrect: Boolean(row.querySelector("[data-scs-option-correct]")?.checked),
                points: Number(row.querySelector("[data-scs-option-points]")?.value || 0),
                sortOrder: index + 1,
                isActive: true
            }));
        }

        function syncQuestionControls() {
            const isSatisfaction = Number(els.questionComponent?.value || 0) === 645250001;
            const isSingleChoice = Number(els.questionType?.value || 0) === 645250000;
            if (els.questionTopic) {
                els.questionTopic.disabled = isSatisfaction;
            }
            if (els.questionPoints) {
                els.questionPoints.disabled = isSatisfaction;
                if (isSatisfaction) {
                    els.questionPoints.value = "0";
                }
            }
            if (els.options) {
                els.options.hidden = !isSingleChoice;
            }
            if (els.addOption) {
                els.addOption.hidden = !isSingleChoice;
            }
        }

        function handleClientLookup() {
            if (!urls.clientSearch) {
                return;
            }

            setValue(els.clientId, "");
            const query = (els.clientName?.value || "").trim();
            window.clearTimeout(state.clientTimer);
            if (query.length < 2) {
                state.clientSuggestions = [];
                renderClientSuggestions();
                return;
            }

            state.clientTimer = window.setTimeout(async () => {
                try {
                    const items = await fetchJson(buildUrl(urls.clientSearch, { q: query }));
                    state.clientSuggestions = Array.isArray(items) ? items : [];
                    renderClientSuggestions();
                    syncClientSelection();
                } catch {
                    state.clientSuggestions = [];
                    renderClientSuggestions();
                }
            }, 200);
        }

        function renderClientSuggestions() {
            if (!els.clientOptions) {
                return;
            }

            els.clientOptions.innerHTML = state.clientSuggestions.map(item => `
                <option value="${escapeHtml(item.name || "")}" data-id="${escapeHtml(item.id || "")}"></option>
            `).join("");
        }

        function syncClientSelection() {
            const value = normalizeText(els.clientName?.value || "");
            const match = state.clientSuggestions.find(item => normalizeText(item.name || "") === value);
            setValue(els.clientId, match?.id || "");
        }

        function findSession(sessionId) {
            return (Array.isArray(state.board?.sessions) ? state.board.sessions : [])
                .find(session => session.sessionId === sessionId);
        }

        function createEmptyOption(isCorrect) {
            return {
                optionId: "",
                text: "",
                isCorrect: Boolean(isCorrect),
                points: isCorrect ? 1 : 0,
                sortOrder: 0,
                isActive: true
            };
        }

        function setStatus(type, message) {
            if (!els.status) {
                return;
            }

            els.status.className = `support-cloud-status is-visible is-${type}`;
            els.status.textContent = message || "";
        }
    }

    function initializePublicSurvey(root) {
        const form = root.querySelector("[data-scs-public-form]");
        const status = root.querySelector("[data-scs-public-status]");
        if (!form) {
            return;
        }

        form.addEventListener("submit", async event => {
            event.preventDefault();
            const payload = {
                code: root.dataset.code || "",
                fullName: root.querySelector("[data-scs-full-name]")?.value || "",
                email: root.querySelector("[data-scs-email]")?.value || "",
                company: root.querySelector("[data-scs-company]")?.value || "",
                answers: collectPublicAnswers(root)
            };

            setPublicStatus(status, "info", "Guardando respuestas...");
            try {
                const result = await fetchJson(root.dataset.submitUrl || "", {
                    method: "POST",
                    body: JSON.stringify(payload)
                });
                form.hidden = true;
                setPublicStatus(status, "success", result?.message || "Respuestas guardadas.");
            } catch (error) {
                setPublicStatus(status, "error", buildErrorMessage(error));
            }
        });
    }

    function collectPublicAnswers(root) {
        return Array.from(root.querySelectorAll("[data-scs-question]")).map(question => {
            const questionId = question.dataset.scsQuestion || "";
            const type = question.dataset.scsQuestionType || "";
            if (type === "single") {
                const checked = question.querySelector("input[type='radio']:checked");
                return { questionId, optionId: checked?.value || "" };
            }
            if (type === "rating") {
                return {
                    questionId,
                    numericValue: Number(question.querySelector("select")?.value || 0)
                };
            }
            return {
                questionId,
                textValue: question.querySelector("textarea")?.value || ""
            };
        });
    }

    async function fetchJson(url, options = {}) {
        const headers = {
            Accept: "application/json",
            ...(options.headers || {})
        };
        if (options.body && !headers["Content-Type"]) {
            headers["Content-Type"] = "application/json";
        }

        const response = await fetch(url, {
            method: options.method || "GET",
            headers,
            body: options.body
        });
        const contentType = response.headers.get("content-type") || "";
        if (!response.ok) {
            const raw = await response.text();
            if (contentType.includes("application/json")) {
                try {
                    const payload = JSON.parse(raw);
                    throw new Error(payload?.message || payload?.detail || raw);
                } catch (error) {
                    if (error instanceof Error && error.message !== raw) {
                        throw error;
                    }
                }
            }
            throw new Error(raw || "No fue posible completar la solicitud.");
        }
        return contentType.includes("application/json") ? response.json() : response.text();
    }

    function buildUrl(baseUrl, params) {
        const url = new URL(baseUrl || window.location.pathname, window.location.origin);
        Object.entries(params || {}).forEach(([key, value]) => {
            if (value !== undefined && value !== null && value !== "") {
                url.searchParams.set(key, value);
            }
        });
        return `${url.pathname}${url.search}`;
    }

    function setPublicStatus(target, type, message) {
        if (!target) {
            return;
        }

        target.className = `support-cloud-status is-visible is-${type}`;
        target.textContent = message || "";
    }

    function setText(element, value) {
        if (element) {
            element.textContent = value ?? "";
        }
    }

    function setValue(element, value) {
        if (element) {
            element.value = value ?? "";
        }
    }

    function renderPill(text) {
        return `<span class="support-cloud-pill">${escapeHtml(text || "-")}</span>`;
    }

    function buildErrorMessage(error) {
        return error instanceof Error ? error.message : "Ocurrio un error inesperado.";
    }

    function normalizeText(value) {
        return (value ?? "")
            .toString()
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
})();
