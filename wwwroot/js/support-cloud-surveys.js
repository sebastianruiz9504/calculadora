(function () {
    const numberFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });

    const percentFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });

    const surveyInputSingleChoice = 645250000;
    const surveyInputRating = 645250001;
    const surveyInputText = 645250002;
    const surveyInputMultipleChoice = 645250003;
    const surveyInputMatching = 645250004;
    const surveyMatchingSeparator = "|||";
    const surveyComponentKnowledge = 645250000;
    const surveyComponentSatisfaction = 645250001;
    const liveWheelEnabled = false;

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

    const livePublicRoot = document.getElementById("soporteCloudLivePublicSurvey");
    if (livePublicRoot) {
        initializeLivePublicSurvey(livePublicRoot);
    }

    function initializeSupportCloudModuleTabs(root) {
        const tabs = Array.from(root.querySelectorAll("[data-scs-module-tab]"));
        const panels = Array.from(root.querySelectorAll("[data-scs-module-panel]"));
        if (!tabs.length || !panels.length) {
            return;
        }

        const resolveInitialKey = () => {
            const hash = normalizeText(window.location.hash.replace("#", ""));
            if (hash === "encuestas") {
                return "encuestas";
            }
            if (hash === "capacitaciones") {
                return "capacitaciones";
            }
            if (hash === "reportes") {
                return "reportes";
            }

            return "soporte";
        };

        const setActive = (key, updateHash) => {
            const activeKey = key === "encuestas" || key === "capacitaciones" || key === "reportes" ? key : "soporte";
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
                const nextHash = activeKey === "encuestas"
                    ? "#encuestas"
                    : activeKey === "capacitaciones"
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
            deleteQuestion: root.dataset.deleteQuestionUrl || "",
            saveSession: root.dataset.saveSessionUrl || "",
            startLive: root.dataset.startLiveUrl || "",
            liveState: root.dataset.liveStateUrl || "",
            liveAdvance: root.dataset.liveAdvanceUrl || "",
            liveClose: root.dataset.liveCloseUrl || "",
            liveRemoveParticipant: root.dataset.liveRemoveParticipantUrl || "",
            closeSession: root.dataset.closeSessionUrl || "",
            sessionDetail: root.dataset.sessionDetailUrl || "",
            export: root.dataset.exportUrl || "",
            clientSearch: root.dataset.clientSearchUrl || ""
        };

        const els = {
            status: root.querySelector("[data-scs-status]"),
            surveyTabs: Array.from(root.querySelectorAll("[data-scs-survey-tab]")),
            surveyPanels: Array.from(root.querySelectorAll("[data-scs-survey-panel]")),
            refresh: root.querySelector("[data-scs-refresh]"),
            totalSessions: root.querySelector("[data-scs-total-sessions]"),
            openSessions: root.querySelector("[data-scs-open-sessions]"),
            totalResponses: root.querySelector("[data-scs-total-responses]"),
            averageScore: root.querySelector("[data-scs-average-score]"),
            sessionRows: root.querySelector("[data-scs-session-rows]"),
            sessionDetail: root.querySelector("[data-scs-session-detail]"),
            bestQuestions: root.querySelector("[data-scs-best-questions]"),
            weakQuestions: root.querySelector("[data-scs-weak-questions]"),
            openSession: root.querySelector("[data-scs-open-session]"),
            sessionModal: root.querySelector("[data-scs-session-modal]"),
            closeSessionModal: Array.from(root.querySelectorAll("[data-scs-close-session-modal]")),
            openTopics: root.querySelector("[data-scs-open-topics]"),
            topicsModal: root.querySelector("[data-scs-topics-modal]"),
            closeTopicsModal: Array.from(root.querySelectorAll("[data-scs-close-topics-modal]")),
            newTopic: root.querySelector("[data-scs-new-topic]"),
            qrModal: root.querySelector("[data-scs-qr-modal]"),
            closeQrModal: Array.from(root.querySelectorAll("[data-scs-close-qr-modal]")),
            qrSessionTitle: root.querySelector("[data-scs-qr-session-title]"),
            scanCount: root.querySelector("[data-scs-scan-count]"),
            completedCount: root.querySelector("[data-scs-completed-count]"),
            winnersModal: root.querySelector("[data-scs-winners-modal]"),
            closeWinners: Array.from(root.querySelectorAll("[data-scs-close-winners]")),
            winnersRows: root.querySelector("[data-scs-winners-rows]"),
            satisfactionPreview: root.querySelector("[data-scs-satisfaction-preview]"),
            selectedTopicTitle: root.querySelector("[data-scs-selected-topic-title]"),
            selectedTopicPreview: root.querySelector("[data-scs-selected-topic-preview]"),
            qrPanel: root.querySelector("[data-scs-qr-panel]"),
            qrImage: root.querySelector("[data-scs-qr-image]"),
            publicLink: root.querySelector("[data-scs-public-link]"),
            exportLink: root.querySelector("[data-scs-export]"),
            closeSession: root.querySelector("[data-scs-close-session]"),
            closeDurationMinutes: root.querySelector("[data-scs-close-duration-minutes]"),
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
            topicBuilderTitle: root.querySelector("[data-scs-topic-builder-title]"),
            resetTopic: root.querySelector("[data-scs-reset-topic]"),
            saveTopicButton: root.querySelector("[data-scs-save-topic]"),
            topicLockNotice: root.querySelector("[data-scs-topic-lock-notice]"),
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
            optionsCard: root.querySelector("[data-scs-options-card]"),
            options: root.querySelector("[data-scs-options]"),
            addOption: root.querySelector("[data-scs-add-option]"),
            resetQuestion: root.querySelector("[data-scs-reset-question]"),
            deleteQuestion: root.querySelector("[data-scs-delete-question]"),
            questionsList: root.querySelector("[data-scs-questions-list]"),
            topicQuestionsPanel: root.querySelector("[data-scs-topic-questions-panel]"),
            topicQuestionsTitle: root.querySelector("[data-scs-topic-questions-title]"),
            topicQuestionsList: root.querySelector("[data-scs-topic-questions-list]"),
            liveForm: root.querySelector("[data-scs-live-form]"),
            liveSessionId: root.querySelector("[data-scs-live-session-id]"),
            liveName: root.querySelector("[data-scs-live-name]"),
            liveDate: root.querySelector("[data-scs-live-date]"),
            liveTopic: root.querySelector("[data-scs-live-topic]"),
            liveClientId: root.querySelector("[data-scs-live-client-id]"),
            liveClientName: root.querySelector("[data-scs-live-client-name]"),
            liveClientOptions: root.querySelector("[data-scs-live-client-options]"),
            liveSatisfactionPreview: root.querySelector("[data-scs-live-satisfaction-preview]"),
            liveSelectedTopicTitle: root.querySelector("[data-scs-live-selected-topic-title]"),
            liveSelectedTopicPreview: root.querySelector("[data-scs-live-selected-topic-preview]"),
            liveFormMeta: root.querySelector("[data-scs-live-form-meta]"),
            liveReset: root.querySelector("[data-scs-live-reset]"),
            liveConsole: root.querySelector("[data-scs-live-console]"),
            liveTitle: root.querySelector("[data-scs-live-title]"),
            liveMessage: root.querySelector("[data-scs-live-message]"),
            liveRefresh: root.querySelector("[data-scs-live-refresh]"),
            liveTrigger: root.querySelector("[data-scs-live-trigger]"),
            liveClose: root.querySelector("[data-scs-live-close]"),
            liveQrImage: root.querySelector("[data-scs-live-qr-image]"),
            livePublicLink: root.querySelector("[data-scs-live-public-link]"),
            liveRegisteredCard: root.querySelector("[data-scs-live-registered-card]"),
            liveRegistered: root.querySelector("[data-scs-live-registered]"),
            liveCompleted: root.querySelector("[data-scs-live-completed]"),
            livePhase: root.querySelector("[data-scs-live-phase]"),
            liveProgress: root.querySelector("[data-scs-live-progress]"),
            liveCurrentTitle: root.querySelector("[data-scs-live-current-title]"),
            liveCurrentText: root.querySelector("[data-scs-live-current-text]"),
            liveResponses: root.querySelector("[data-scs-live-responses]"),
            liveParticipantsModal: root.querySelector("[data-scs-live-participants-modal]"),
            closeLiveParticipants: Array.from(root.querySelectorAll("[data-scs-close-live-participants]")),
            liveParticipantsStatus: root.querySelector("[data-scs-live-participants-status]"),
            liveParticipantsRows: root.querySelector("[data-scs-live-participants-rows]")
        };

        const state = {
            board: null,
            selectedSessionId: "",
            selectedTopicId: "",
            detail: null,
            optionsDraft: [],
            clientSuggestions: [],
            clientTimer: 0,
            qrRefreshTimer: 0,
            liveSessionId: "",
            liveState: null,
            liveClientSuggestions: [],
            liveClientTimer: 0,
            livePollTimer: 0,
            livePollInFlight: false,
            livePollPending: false,
            liveStateRequestId: 0,
            liveStateAppliedRequestId: 0,
            liveAdvanceBusy: false,
            liveWinnersRenderKey: ""
        };

        initializeSurveySubtabs();
        els.refresh?.addEventListener("click", () => loadBoard());
        els.openSession?.addEventListener("click", () => {
            resetSessionForm();
            openModal(els.sessionModal);
        });
        els.closeSessionModal.forEach(button => button.addEventListener("click", () => closeModal(els.sessionModal)));
        els.openTopics?.addEventListener("click", () => {
            renderPickLists();
            openModal(els.topicsModal);
        });
        els.closeTopicsModal.forEach(button => button.addEventListener("click", () => closeModal(els.topicsModal)));
        els.closeQrModal.forEach(button => button.addEventListener("click", () => closeQrModal()));
        els.closeWinners.forEach(button => button.addEventListener("click", () => closeModal(els.winnersModal)));
        els.newTopic?.addEventListener("click", () => {
            openTopicBuilder();
        });
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
        els.resetTopic?.addEventListener("click", () => {
            resetTopicForm();
            resetQuestionForm();
            renderPickLists();
        });
        els.resetQuestion?.addEventListener("click", resetQuestionForm);
        els.addOption?.addEventListener("click", () => {
            state.optionsDraft.push(createEmptyOption());
            renderOptionsDraft();
        });
        els.deleteQuestion?.addEventListener("click", () => deleteQuestion(els.questionId?.value || ""));
        els.questionComponent?.addEventListener("change", syncQuestionControls);
        els.questionType?.addEventListener("change", syncQuestionControls);
        els.sessionTopic?.addEventListener("change", renderQuestionPreviews);
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
            state.selectedTopicId = button.dataset.scsEditTopic || "";
            fillTopicForm(state.selectedTopicId);
            setValue(els.questionTopic, state.selectedTopicId);
            resetQuestionForm({ preserveTopic: true });
            renderPickLists();
            openModal(els.topicsModal);
        });
        els.questionsList?.addEventListener("click", event => {
            const target = event.target instanceof HTMLElement ? event.target : null;
            const deleteButton = target?.closest("[data-scs-delete-question-row]");
            if (deleteButton) {
                deleteQuestion(deleteButton.dataset.scsDeleteQuestionRow || "");
                return;
            }

            const button = target?.closest("[data-scs-edit-question]");
            if (!button) {
                return;
            }
            fillQuestionForm(button.dataset.scsEditQuestion || "");
            fillTopicForm(state.selectedTopicId);
            renderPickLists();
            openModal(els.topicsModal);
        });
        els.topicQuestionsList?.addEventListener("click", event => {
            const target = event.target instanceof HTMLElement ? event.target : null;
            const deleteButton = target?.closest("[data-scs-delete-question-row]");
            if (deleteButton) {
                deleteQuestion(deleteButton.dataset.scsDeleteQuestionRow || "");
                return;
            }

            const button = target?.closest("[data-scs-edit-question]");
            if (!button) {
                return;
            }
            fillQuestionForm(button.dataset.scsEditQuestion || "");
            renderPickLists();
        });
        els.liveForm?.addEventListener("submit", event => {
            event.preventDefault();
            startLiveSurvey();
        });
        els.liveReset?.addEventListener("click", resetLiveForm);
        els.liveTopic?.addEventListener("change", renderLiveQuestionPreviews);
        els.liveRefresh?.addEventListener("click", () => loadLiveState());
        els.liveTrigger?.addEventListener("click", advanceLiveSurvey);
        els.liveClose?.addEventListener("click", closeLiveSurvey);
        els.liveRegisteredCard?.addEventListener("click", openLiveParticipantsModal);
        els.liveRegisteredCard?.addEventListener("keydown", event => {
            if (event.key === "Enter" || event.key === " ") {
                event.preventDefault();
                openLiveParticipantsModal();
            }
        });
        els.closeLiveParticipants.forEach(button => button.addEventListener("click", () => closeModal(els.liveParticipantsModal)));
        els.liveParticipantsRows?.addEventListener("click", event => {
            const button = event.target instanceof HTMLElement
                ? event.target.closest("[data-scs-remove-live-participant]")
                : null;
            if (!button) {
                return;
            }
            removeLiveParticipant(button.dataset.scsRemoveLiveParticipant || "");
        });
        els.liveClientName?.addEventListener("input", handleLiveClientLookup);
        els.liveClientName?.addEventListener("change", syncLiveClientSelection);
        els.liveClientName?.addEventListener("blur", syncLiveClientSelection);
        els.clientName?.addEventListener("input", handleClientLookup);
        els.clientName?.addEventListener("change", syncClientSelection);
        els.clientName?.addEventListener("blur", syncClientSelection);

        resetSessionForm();
        resetTopicForm();
        resetQuestionForm();
        resetLiveForm();
        loadBoard();

        function initializeSurveySubtabs() {
            if (!els.surveyTabs.length || !els.surveyPanels.length) {
                return;
            }

            const setActive = key => {
                const activeKey = key === "tipos" || key === "iniciar" ? key : "historico";
                els.surveyTabs.forEach(tab => {
                    const isActive = tab.dataset.scsSurveyTab === activeKey;
                    tab.classList.toggle("is-active", isActive);
                    tab.setAttribute("aria-selected", isActive ? "true" : "false");
                });
                els.surveyPanels.forEach(panel => {
                    const isActive = panel.dataset.scsSurveyPanel === activeKey;
                    panel.classList.toggle("is-active", isActive);
                    panel.hidden = !isActive;
                });
            };

            els.surveyTabs.forEach(tab => {
                tab.addEventListener("click", () => setActive(tab.dataset.scsSurveyTab || "historico"));
            });
            setActive("historico");
        }

        function openTopicBuilder(topicId = "") {
            state.selectedTopicId = topicId || "";
            if (state.selectedTopicId) {
                fillTopicForm(state.selectedTopicId);
                setValue(els.questionTopic, state.selectedTopicId);
                resetQuestionForm({ preserveTopic: true });
            } else {
                resetTopicForm();
                resetQuestionForm();
            }
            renderPickLists();
            openModal(els.topicsModal);
        }

        async function loadBoard(options = {}) {
            if (!urls.board) {
                return;
            }

            if (!options.silent) {
                setStatus("info", "Cargando encuestas de capacitacion...");
            }
            try {
                state.board = await fetchJson(urls.board);
                renderBoard();
                if (!options.silent) {
                    setStatus("success", state.board?.message || "Encuestas cargadas.");
                }
            } catch (error) {
                if (!options.silent) {
                    setStatus("error", buildErrorMessage(error));
                }
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
            renderQuestionPreviews();
            renderLiveQuestionPreviews();
            renderSessions();
            renderQuestionBreakdown(els.bestQuestions, board.bestQuestions, "Sin preguntas calificadas.");
            renderQuestionBreakdown(els.weakQuestions, board.weakQuestions, "Sin preguntas calificadas.");

            const sessions = Array.isArray(board.sessions) ? board.sessions : [];
            if (state.selectedSessionId && !sessions.some(session => session.sessionId === state.selectedSessionId)) {
                state.selectedSessionId = "";
            }
            if (!state.selectedSessionId && sessions.length) {
                state.selectedSessionId = sessions[0].sessionId || "";
            }
            if (state.selectedSessionId && els.sessionDetail) {
                selectSession(state.selectedSessionId, { preserveForm: true });
            }
        }

        function renderTopicSelects() {
            const topics = Array.isArray(state.board?.topics) ? state.board.topics : [];
            const editableTopics = topics.filter(topic => topic.isActive !== false && topic.isLocked !== true);
            const options = [
                '<option value="">Selecciona...</option>',
                ...editableTopics.map(topic => `<option value="${escapeHtml(topic.topicId)}">${escapeHtml(topic.name)}</option>`)
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
            if (els.liveTopic) {
                const current = els.liveTopic.value;
                els.liveTopic.innerHTML = options;
                els.liveTopic.value = current;
            }
        }

        function renderPickLists() {
            const topics = Array.isArray(state.board?.topics) ? state.board.topics : [];
            if (els.topicsList) {
                els.topicsList.innerHTML = topics.length
                    ? topics.map(topic => `
                        <tr class="${topic.topicId === state.selectedTopicId ? "is-selected" : ""}">
                            <td data-label="Tema">
                                <div class="support-cloud-table__ticket">
                                    <div class="support-cloud-table__ticket-title">${escapeHtml(topic.name || "Tema")}</div>
                                    <div class="support-cloud-table__ticket-description">${escapeHtml(topic.description || "")}</div>
                                </div>
                            </td>
                            <td data-label="Preguntas" class="text-end support-cloud-table__hours">${escapeHtml(numberFormatter.format(Number(topic.knowledgeQuestionCount || 0)))}</td>
                            <td data-label="Estado">${renderPill(topic.isLocked ? "Fijo" : topic.isActive === false ? "Inactivo" : "Activo")}</td>
                            <td data-label="Acciones" class="text-end support-cloud-table__actions">
                                ${topic.isLocked
                                    ? '<span class="support-cloud-table__muted">Tema fijo</span>'
                                    : `<button type="button" class="btn btn-outline-primary btn-sm" data-scs-edit-topic="${escapeHtml(topic.topicId)}">Editar</button>`}
                            </td>
                        </tr>
                    `).join("")
                    : '<tr><td colspan="4" class="support-cloud-table__empty">Sin temas creados.</td></tr>';
            }

            const questions = Array.isArray(state.board?.questions) ? state.board.questions : [];
            if (els.questionsList) {
                const selectedTopic = findTopic(state.selectedTopicId);
                const visibleQuestions = selectedTopic
                    ? questions.filter(question => selectedTopic.isLocked
                        ? question.isLocked === true
                        : question.isLocked !== true && question.topicId === selectedTopic.topicId)
                    : questions.filter(question => question.isLocked !== true);
                els.questionsList.innerHTML = visibleQuestions.length
                    ? visibleQuestions.sort(bySortOrder).map(question => {
                        const correctLabel = resolveCorrectAnswerLabel(question);
                        return `
                            <tr>
                                <td data-label="Orden" class="text-end support-cloud-table__hours">${escapeHtml(numberFormatter.format(Number(question.sortOrder || 0)))}</td>
                                <td data-label="Pregunta">
                                    <div class="support-cloud-table__ticket">
                                        <div class="support-cloud-table__ticket-title">${escapeHtml(question.text || "Pregunta")}</div>
                                        <div class="support-cloud-table__ticket-description">${question.isActive === false ? "Inactiva" : "Activa"}</div>
                                    </div>
                                </td>
                                <td data-label="Tema">${escapeHtml(question.topicName || "Sin tema")}</td>
                                <td data-label="Tipo de pregunta">${escapeHtml(question.inputTypeLabel || "-")}</td>
                                <td data-label="Respuesta correcta">${escapeHtml(correctLabel)}</td>
                                <td data-label="Acciones" class="text-end support-cloud-table__actions">
                                    ${question.isLocked
                                        ? '<span class="support-cloud-table__muted">Fija</span>'
                                        : `<button type="button" class="btn btn-outline-primary btn-sm" data-scs-edit-question="${escapeHtml(question.questionId)}">Editar</button>
                                           ${question.isActive === false ? "" : `<button type="button" class="btn btn-outline-danger btn-sm" data-scs-delete-question-row="${escapeHtml(question.questionId)}">Eliminar</button>`}`}
                                </td>
                            </tr>
                        `;
                    }).join("")
                    : '<tr><td colspan="6" class="support-cloud-table__empty">Sin preguntas para este tema.</td></tr>';
            }
            renderTopicQuestionList();
        }

        function renderTopicQuestionList() {
            if (!els.topicQuestionsPanel || !els.topicQuestionsList) {
                return;
            }

            const selectedTopic = findTopic(state.selectedTopicId);
            if (!selectedTopic || selectedTopic.isLocked) {
                els.topicQuestionsPanel.hidden = true;
                els.topicQuestionsList.innerHTML = "";
                return;
            }

            const questions = (Array.isArray(state.board?.questions) ? state.board.questions : [])
                .filter(question => question.isLocked !== true && question.isActive !== false && question.topicId === selectedTopic.topicId)
                .sort(bySortOrder);
            els.topicQuestionsPanel.hidden = false;
            setText(els.topicQuestionsTitle, `Preguntas creadas para ${selectedTopic.name || "este tema"}`);
            els.topicQuestionsList.innerHTML = questions.length
                ? questions.map(question => `
                    <article class="support-cloud-survey-topic-question ${question.questionId === (els.questionId?.value || "") ? "is-selected" : ""}">
                        <div class="support-cloud-survey-topic-question__body">
                            <span class="support-cloud-survey-topic-question__order">${escapeHtml(numberFormatter.format(Number(question.sortOrder || 0)))}</span>
                            <div>
                                <strong>${escapeHtml(question.text || "Pregunta")}</strong>
                                <span>${escapeHtml(question.inputTypeLabel || "-")} · ${escapeHtml(resolveCorrectAnswerLabel(question))}</span>
                            </div>
                        </div>
                        <div class="support-cloud-survey-topic-question__actions">
                            <button type="button" class="btn btn-outline-primary btn-sm" data-scs-edit-question="${escapeHtml(question.questionId)}">Editar</button>
                            <button type="button" class="btn btn-outline-danger btn-sm" data-scs-delete-question-row="${escapeHtml(question.questionId)}">Eliminar</button>
                        </div>
                    </article>
                `).join("")
                : '<div class="support-cloud-placeholder support-cloud-placeholder--compact">Este tema aun no tiene preguntas activas. Crea la primera con el formulario.</div>';
        }

        function renderQuestionPreviews() {
            const questions = Array.isArray(state.board?.questions) ? state.board.questions : [];
            const satisfactionQuestions = questions
                .filter(question => question.isLocked === true || Number(question.componentValue || 0) === surveyComponentSatisfaction)
                .sort(bySortOrder);
            if (els.satisfactionPreview) {
                els.satisfactionPreview.innerHTML = renderMiniQuestionList(satisfactionQuestions, "Sin preguntas fijas.");
            }

            const selectedTopic = findTopic(els.sessionTopic?.value || "");
            if (els.selectedTopicTitle) {
                els.selectedTopicTitle.textContent = selectedTopic?.name || "Conocimiento";
            }
            const selectedQuestions = selectedTopic
                ? questions.filter(question => question.isLocked !== true && question.isActive !== false && question.topicId === selectedTopic.topicId).sort(bySortOrder)
                : [];
            if (els.selectedTopicPreview) {
                els.selectedTopicPreview.innerHTML = renderMiniQuestionList(selectedQuestions, "Selecciona un tema para ver sus preguntas.");
            }
        }

        function renderLiveQuestionPreviews() {
            const questions = Array.isArray(state.board?.questions) ? state.board.questions : [];
            const satisfactionQuestions = questions
                .filter(question => question.isLocked === true || Number(question.componentValue || 0) === surveyComponentSatisfaction)
                .sort(bySortOrder);
            if (els.liveSatisfactionPreview) {
                els.liveSatisfactionPreview.innerHTML = renderMiniQuestionList(satisfactionQuestions, "Sin preguntas fijas.");
            }

            const selectedTopic = findTopic(els.liveTopic?.value || "");
            if (els.liveSelectedTopicTitle) {
                els.liveSelectedTopicTitle.textContent = selectedTopic?.name || "Conocimiento";
            }
            const selectedQuestions = selectedTopic
                ? questions.filter(question => question.isLocked !== true && question.isActive !== false && question.topicId === selectedTopic.topicId).sort(bySortOrder)
                : [];
            if (els.liveSelectedTopicPreview) {
                els.liveSelectedTopicPreview.innerHTML = renderMiniQuestionList(selectedQuestions, "Selecciona un tema para ver sus preguntas.");
            }
        }

        function renderMiniQuestionList(items, emptyMessage) {
            if (!items.length) {
                return `<div class="support-cloud-placeholder support-cloud-placeholder--compact">${escapeHtml(emptyMessage)}</div>`;
            }

            return items.map(item => `
                <div class="support-cloud-survey-mini-list__item">
                    <strong>${escapeHtml(item.text || "Pregunta")}</strong>
                    <span>${escapeHtml(item.inputTypeLabel || "")}</span>
                </div>
            `).join("");
        }

        function renderSessions() {
            const sessions = Array.isArray(state.board?.sessions) ? state.board.sessions : [];
            if (!els.sessionRows) {
                return;
            }

            if (!sessions.length) {
                els.sessionRows.innerHTML = '<tr><td colspan="5" class="support-cloud-table__empty">No hay encuestas registradas.</td></tr>';
                return;
            }

            els.sessionRows.innerHTML = sessions.map(session => `
                <tr>
                    <td data-label="Fecha">${escapeHtml(session.dateDisplay || "-")}</td>
                    <td data-label="Cantidad de asistentes" class="text-end support-cloud-table__hours">${escapeHtml(numberFormatter.format(Number(session.registeredCount || session.completedCount || 0)))}</td>
                    <td data-label="Tema">${renderPill(session.topicName || "Sin tema")}</td>
                    <td data-label="Link a encuesta">${renderSurveyLink(session)}</td>
                    <td data-label="Cliente">${escapeHtml(session.clientName || "-")}</td>
                </tr>
            `).join("");
        }

        function renderSurveyLink(session) {
            const publicUrl = session?.publicUrl || "";
            if (!publicUrl) {
                return '<span class="support-cloud-table__muted">Sin link</span>';
            }

            return `<a class="support-cloud-table__link" href="${escapeHtml(publicUrl)}" target="_blank" rel="noopener">${escapeHtml(session.code || "Abrir encuesta")}</a>`;
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
            setText(els.qrSessionTitle, session.name || "Encuesta");
            setText(els.scanCount, numberFormatter.format(Number(session.scanCount || 0)));
            setText(els.completedCount, numberFormatter.format(Number(session.completedCount || 0)));
            if (els.closeDurationMinutes && !els.closeDurationMinutes.value) {
                els.closeDurationMinutes.value = "60";
            }
            if (els.closeDurationMinutes) {
                els.closeDurationMinutes.disabled = Number(session.stateValue || 0) === 645250002;
            }
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
            if (!urls.saveTopic) {
                return;
            }

            setStatus("info", "Guardando tema...");
            try {
                const result = await fetchJson(urls.saveTopic, {
                    method: "POST",
                    body: JSON.stringify(payload)
                });
                state.board = result?.board || state.board;
                const savedTopic = findSavedTopic(payload);
                state.selectedTopicId = savedTopic?.topicId || payload.topicId || "";
                renderTopicSelects();
                renderPickLists();
                if (state.selectedTopicId) {
                    fillTopicForm(state.selectedTopicId);
                    setValue(els.questionTopic, state.selectedTopicId);
                    resetQuestionForm({ preserveTopic: true });
                }
                setStatus("success", result?.message || "Tema guardado correctamente.");
            } catch (error) {
                setStatus("error", buildErrorMessage(error));
            }
        }

        async function saveQuestion() {
            const componentValue = Number(els.questionComponent?.value || surveyComponentKnowledge);
            const typeValue = Number(els.questionType?.value || surveyInputSingleChoice);
            const options = isOptionQuestionType(typeValue) ? readOptionsDraft(typeValue) : [];
            const topicId = componentValue === surveyComponentKnowledge ? (els.questionTopic?.value || "") : "";
            if (!topicId) {
                setStatus("error", "Guarda o selecciona un tema antes de crear preguntas.");
                return;
            }
            if ((typeValue === surveyInputSingleChoice || typeValue === surveyInputMultipleChoice)
                && !options.some(option => option.isCorrect && option.text)) {
                setStatus("error", "Selecciona la respuesta correcta.");
                return;
            }
            if (typeValue === surveyInputMatching
                && !options.every(option => option.text && parseMatchingOptionText(option.text).target)) {
                setStatus("error", "Cada elemento para arrastrar debe tener texto y campo asignado.");
                return;
            }
            const payload = {
                questionId: els.questionId?.value || "",
                topicId,
                componentValue,
                inputTypeValue: typeValue,
                text: (els.questionText?.value || "").trim(),
                sortOrder: Number(els.questionOrder?.value || 0),
                maxPoints: Number(els.questionPoints?.value || 0),
                isActive: (els.questionActive?.value || "true") === "true",
                options
            };
            await saveAndRefresh(urls.saveQuestion, payload, () => resetQuestionForm({ preserveTopic: true }));
        }

        async function deleteQuestion(questionId) {
            const normalizedQuestionId = (questionId || "").trim();
            if (!normalizedQuestionId || !urls.deleteQuestion) {
                return;
            }

            const question = (Array.isArray(state.board?.questions) ? state.board.questions : [])
                .find(item => item.questionId === normalizedQuestionId);
            if (!question || question.isLocked) {
                setStatus("error", "Esta pregunta no se puede eliminar.");
                return;
            }

            const confirmed = window.confirm(`Eliminar la pregunta "${question.text || "seleccionada"}"?`);
            if (!confirmed) {
                return;
            }

            setStatus("info", "Eliminando pregunta...");
            try {
                const result = await fetchJson(buildUrl(urls.deleteQuestion, { questionId: normalizedQuestionId }), {
                    method: "POST"
                });
                state.board = result?.board || state.board;
                if ((els.questionId?.value || "") === normalizedQuestionId) {
                    resetQuestionForm({ preserveTopic: true });
                }
                renderBoard();
                setStatus("success", result?.message || "Pregunta eliminada correctamente.");
            } catch (error) {
                setStatus("error", buildErrorMessage(error));
            }
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
            if (!urls.saveSession) {
                return;
            }

            setStatus("info", "Guardando sesion...");
            try {
                const result = await fetchJson(urls.saveSession, {
                    method: "POST",
                    body: JSON.stringify(payload)
                });
                state.board = result?.board || state.board;
                const session = findSavedSession(payload);
                if (session) {
                    state.selectedSessionId = session.sessionId || "";
                }
                resetSessionForm();
                renderBoard();
                closeModal(els.sessionModal);
                const selected = findSession(state.selectedSessionId);
                if (selected) {
                    await selectSession(selected.sessionId, { preserveForm: true });
                    openQrModal();
                }
                setStatus("success", result?.message || "Sesion guardada correctamente.");
            } catch (error) {
                setStatus("error", buildErrorMessage(error));
            }
        }

        async function startLiveSurvey() {
            if (!urls.startLive) {
                return;
            }

            const payload = {
                sessionId: els.liveSessionId?.value || "",
                name: (els.liveName?.value || "").trim(),
                topicId: els.liveTopic?.value || "",
                clientId: els.liveClientId?.value || "",
                clientName: (els.liveClientName?.value || "").trim(),
                dateValue: els.liveDate?.value || ""
            };
            if (!payload.name || !payload.topicId) {
                setStatus("error", "Indica nombre de sesion y tema antes de iniciar.");
                return;
            }

            setStatus("info", "Creando sesion live...");
            try {
                const result = await fetchJson(urls.startLive, {
                    method: "POST",
                    body: JSON.stringify(payload)
                });
                state.board = result?.board || state.board;
                state.liveSessionId = result?.session?.sessionId || "";
                setValue(els.liveSessionId, state.liveSessionId);
                renderBoard();
                renderLiveState(result?.state || null);
                startLivePolling();
                setStatus("success", result?.message || "Sesion live iniciada.");
            } catch (error) {
                setStatus("error", buildErrorMessage(error));
            }
        }

        async function loadLiveState(options = {}) {
            const sessionId = state.liveSessionId || els.liveSessionId?.value || "";
            if (!urls.liveState || !sessionId) {
                return;
            }
            if (state.livePollInFlight) {
                state.livePollPending = true;
                return;
            }

            const requestId = ++state.liveStateRequestId;
            state.livePollInFlight = true;
            try {
                const result = await fetchJson(buildUrl(urls.liveState, { sessionId }));
                if (requestId >= state.liveStateAppliedRequestId) {
                    state.liveStateAppliedRequestId = requestId;
                    renderLiveState(result);
                }
                if (!options.silent) {
                    setStatus("success", "Estado live actualizado.");
                }
            } catch (error) {
                if (!options.silent) {
                    setStatus("error", buildErrorMessage(error));
                }
            } finally {
                state.livePollInFlight = false;
                if (state.livePollPending) {
                    state.livePollPending = false;
                    window.setTimeout(() => loadLiveState({ silent: true }), 0);
                }
            }
        }

        async function advanceLiveSurvey() {
            const sessionId = state.liveSessionId || els.liveSessionId?.value || "";
            if (!urls.liveAdvance || !sessionId || state.liveAdvanceBusy) {
                return;
            }

            state.liveAdvanceBusy = true;
            if (els.liveTrigger) {
                els.liveTrigger.disabled = true;
            }
            setStatus("info", "Avanzando encuesta live...");
            try {
                const result = await fetchJson(buildUrl(urls.liveAdvance, { sessionId }), { method: "POST" });
                const requestId = ++state.liveStateRequestId;
                state.liveStateAppliedRequestId = requestId;
                renderLiveState(result);
                setStatus("success", result?.message || "Trigger enviado.");
            } catch (error) {
                setStatus("error", buildErrorMessage(error));
            } finally {
                state.liveAdvanceBusy = false;
                if (els.liveTrigger && state.liveState) {
                    els.liveTrigger.disabled = state.liveState.canAdvance === false;
                    els.liveTrigger.textContent = resolveLiveTriggerText(state.liveState);
                }
            }
        }

        async function closeLiveSurvey() {
            const sessionId = state.liveSessionId || els.liveSessionId?.value || "";
            if (!urls.liveClose || !sessionId) {
                return;
            }

            setStatus("info", "Cerrando sesion live...");
            try {
                const result = await fetchJson(buildUrl(urls.liveClose, { sessionId }), {
                    method: "POST",
                    body: JSON.stringify({})
                });
                state.board = result?.board || state.board;
                renderBoard();
                await loadLiveState({ silent: true });
                stopLivePolling();
                setStatus("success", result?.message || "Sesion live cerrada.");
            } catch (error) {
                setStatus("error", buildErrorMessage(error));
            }
        }

        function renderLiveState(liveState) {
            if (!liveState) {
                return;
            }

            state.liveState = liveState;
            state.liveSessionId = liveState.sessionId || state.liveSessionId;
            setValue(els.liveSessionId, state.liveSessionId);
            if (els.liveConsole) {
                els.liveConsole.hidden = false;
            }

            const publicUrl = liveState.publicUrl || "";
            setText(els.liveTitle, liveState.sessionName || "Encuesta live");
            setText(els.liveMessage, liveState.message || "");
            setText(els.liveRegistered, numberFormatter.format(Number(liveState.registeredCount || 0)));
            setText(els.liveCompleted, numberFormatter.format(Number(liveState.completedCount || 0)));
            setText(els.livePhase, liveState.phaseLabel || "-");
            if (els.liveQrImage) {
                els.liveQrImage.src = publicUrl ? `${publicUrl}/Qr` : "";
            }
            if (els.livePublicLink) {
                els.livePublicLink.href = publicUrl || "#";
                els.livePublicLink.textContent = publicUrl || "Sin link";
            }

            const question = liveState.currentQuestion || null;
            if (liveState.phase === "ranking") {
                setText(els.liveProgress, buildLiveAnsweredLabel(liveState));
                setText(els.liveCurrentTitle, "Ranking parcial");
                if (els.liveCurrentText) {
                    els.liveCurrentText.innerHTML = renderLiveRanking(liveState);
                }
            } else if (liveState.phase === "winners") {
                setText(els.liveProgress, "Antes de la encuesta final");
                setText(els.liveCurrentTitle, "Top 3 ganadores");
                if (els.liveCurrentText) {
                    const winnersKey = `${liveState.sequence || 0}:${(Array.isArray(liveState.ranking) ? liveState.ranking : [])
                        .slice(0, 3)
                        .map(item => `${item.participantKey || item.fullName || ""}:${Number(item.score || 0)}`)
                        .join("|")}`;
                    if (state.liveWinnersRenderKey !== winnersKey) {
                        state.liveWinnersRenderKey = winnersKey;
                        els.liveCurrentText.innerHTML = renderLiveWinners(liveState);
                    }
                }
            } else if (liveState.phase === "survey") {
                state.liveWinnersRenderKey = "";
                setText(els.liveProgress, "Encuesta final en curso");
                setText(els.liveCurrentTitle, liveWheelEnabled ? "Ranking de ruleta" : "Encuesta final");
                if (els.liveCurrentText) {
                    els.liveCurrentText.innerHTML = liveWheelEnabled
                        ? renderLiveWheelRanking(liveState)
                        : `<span class="support-cloud-placeholder support-cloud-placeholder--compact">Los participantes estan completando la encuesta final. Al terminar veran el mensaje de agradecimiento.</span>`;
                }
            } else if (question) {
                setText(els.liveProgress, `Pregunta ${Number(liveState.currentQuestionIndex || 0) + 1} de ${Number(liveState.totalQuestions || 0)}`);
                setText(els.liveCurrentTitle, question.text || "Pregunta");
                if (els.liveCurrentText) {
                    els.liveCurrentText.innerHTML = renderLivePresenterQuestion(liveState, question);
                }
            } else {
                setText(els.liveProgress, liveState.phaseLabel || "Registro");
                setText(els.liveCurrentTitle, resolveLivePresenterTitle(liveState));
                setText(els.liveCurrentText, liveState.message || "");
            }
            renderLiveResponses(liveState);
            if (els.liveParticipantsModal && !els.liveParticipantsModal.hidden) {
                renderLiveParticipants(liveState);
            }

            if (els.liveTrigger) {
                els.liveTrigger.disabled = state.liveAdvanceBusy || liveState.canAdvance === false;
                els.liveTrigger.textContent = resolveLiveTriggerText(liveState);
            }
            if (els.liveClose) {
                els.liveClose.disabled = liveState.isClosed === true;
            }
        }

        function buildLiveAnsweredLabel(liveState) {
            const answered = Number(liveState.currentQuestionAnsweredCount || 0);
            const registered = Number(liveState.registeredCount || 0);
            return `Respondieron ${numberFormatter.format(answered)} de ${numberFormatter.format(registered)} registrados`;
        }

        function openLiveParticipantsModal() {
            renderLiveParticipants(state.liveState || {});
            openModal(els.liveParticipantsModal);
        }

        function renderLiveParticipants(liveState) {
            if (!els.liveParticipantsRows) {
                return;
            }

            const participants = Array.isArray(liveState?.participants) ? liveState.participants : [];
            if (!participants.length) {
                els.liveParticipantsRows.innerHTML = `
                    <div class="support-cloud-placeholder support-cloud-placeholder--compact">
                        Aun no hay participantes registrados.
                    </div>
                `;
                return;
            }

            els.liveParticipantsRows.innerHTML = participants.map(participant => {
                const participantKey = participant.participantKey || "";
                const wheel = Number(participant.wheelNumber || 0);
                return `
                    <article class="support-cloud-live-participant-row">
                        <div>
                            <strong>${escapeHtml(participant.fullName || "Participante")}</strong>
                            <span>${escapeHtml(participant.email || "Sin correo")}</span>
                            <small>${escapeHtml([participant.company || "", participant.role || ""].filter(Boolean).join(" - ") || "Sin empresa/rol")}</small>
                        </div>
                        <div class="support-cloud-live-participant-row__meta">
                            <span>${escapeHtml(numberFormatter.format(Number(participant.score || 0)))} pts</span>
                            <span>${escapeHtml(numberFormatter.format(Number(participant.answeredCount || 0)))} resp.</span>
                            ${liveWheelEnabled && wheel > 0 ? `<span>Ruleta ${escapeHtml(numberFormatter.format(wheel))}</span>` : ""}
                        </div>
                        <button type="button" class="btn btn-outline-danger btn-sm" data-scs-remove-live-participant="${escapeHtml(participantKey)}">
                            Eliminar
                        </button>
                    </article>
                `;
            }).join("");
        }

        async function removeLiveParticipant(participantKey) {
            const sessionId = state.liveSessionId || els.liveSessionId?.value || "";
            if (!urls.liveRemoveParticipant || !sessionId || !participantKey) {
                return;
            }

            const participant = (Array.isArray(state.liveState?.participants) ? state.liveState.participants : [])
                .find(item => item.participantKey === participantKey);
            const name = participant?.fullName || "este participante";
            if (!window.confirm(`Retirar a ${name} de la sesion live?`)) {
                return;
            }

            setLiveParticipantsStatus("info", "Retirando participante...");
            try {
                const result = await fetchJson(buildUrl(urls.liveRemoveParticipant, { sessionId, participantKey }), { method: "POST" });
                renderLiveState(result);
                renderLiveParticipants(result);
                setLiveParticipantsStatus("success", "Participante retirado de la sesion.");
                setStatus("success", "Participante retirado de la sesion live.");
            } catch (error) {
                setLiveParticipantsStatus("error", buildErrorMessage(error));
            }
        }

        function setLiveParticipantsStatus(type, message) {
            if (!els.liveParticipantsStatus) {
                return;
            }

            els.liveParticipantsStatus.className = `support-cloud-status is-visible is-${type}`;
            els.liveParticipantsStatus.textContent = message || "";
        }

        function renderLivePresenterQuestion(liveState, question) {
            const endsAt = Date.parse(liveState.questionEndsOnUtc || "");
            const now = Date.parse(liveState.serverNowUtc || "") || Date.now();
            const duration = Math.max(1, Number(liveState.questionDurationSeconds || 20));
            const remaining = Number.isFinite(endsAt) ? Math.max(0, Math.ceil((endsAt - now) / 1000)) : duration;
            const remainingPercent = Math.max(0, Math.min(100, (remaining * 100) / duration));
            const options = (Array.isArray(question.options) ? question.options : [])
                .filter(option => option.isActive !== false)
                .map((option, index) => `
                    <span class="support-cloud-live-presenter-option is-tone-${(index % 4) + 1}">
                        <b>${escapeHtml(String.fromCharCode(65 + index))}</b>
                        ${escapeHtml(option.text || "Opcion")}
                    </span>
                `).join("");

            return `
                <div class="support-cloud-live-presenter-question">
                    <div>
                        <strong>${escapeHtml(buildLiveAnsweredLabel(liveState))}</strong>
                        <span>${escapeHtml(numberFormatter.format(remaining))} s restantes</span>
                    </div>
                    <div class="support-cloud-live-presenter-timer">
                        <i style="width:${remainingPercent}%"></i>
                    </div>
                    <div class="support-cloud-live-presenter-options">${options || escapeHtml(question.inputTypeLabel || "")}</div>
                </div>
            `;
        }

        function renderLiveRanking(liveState) {
            const ranking = Array.isArray(liveState.ranking) ? liveState.ranking : [];
            if (!ranking.length) {
                return `<span class="support-cloud-placeholder support-cloud-placeholder--compact">Aun no hay respuestas para rankear.</span>`;
            }

            const topScore = Math.max(...ranking.map(item => Number(item.score || 0)), 1);
            return `
                <ol class="support-cloud-live-ranking support-cloud-live-ranking--show">
                    ${ranking.slice(0, 5).map((item, index) => `
                        <li class="is-rank-${index + 1}">
                            <span>${index + 1}</span>
                            <strong>${escapeHtml(item.fullName || "Participante")}</strong>
                            <div class="support-cloud-live-ranking-bar"><i style="width:${Math.max(4, Math.round((Number(item.score || 0) * 100) / topScore))}%"></i></div>
                            <em>${escapeHtml(numberFormatter.format(Number(item.score || 0)))} pts</em>
                        </li>
                    `).join("")}
                </ol>
            `;
        }

        function renderLiveWheelRanking(liveState) {
            const ranking = Array.isArray(liveState.wheelRanking) ? liveState.wheelRanking : [];
            if (!ranking.length) {
                return `<span class="support-cloud-placeholder support-cloud-placeholder--compact">Aun no hay giros de ruleta registrados.</span>`;
            }

            return `
                <ol class="support-cloud-live-wheel-board">
                    ${ranking.map((item, index) => `
                        <li>
                            <span>${index + 1}</span>
                            <strong>${escapeHtml(item.fullName || "Participante")}</strong>
                            <em>${escapeHtml(numberFormatter.format(Number(item.number || 0)))}</em>
                        </li>
                    `).join("")}
                </ol>
            `;
        }

        function renderLiveWinners(liveState) {
            const winners = resolveLiveWinners(liveState);
            if (!winners.length) {
                return `<span class="support-cloud-placeholder support-cloud-placeholder--compact">Aun no hay ganadores para mostrar.</span>`;
            }

            return `
                <div class="support-cloud-live-winners">
                    <div class="support-cloud-live-ranking-title">Podio final de aprendizaje</div>
                    <div class="support-cloud-live-winners__head">
                        <span>Puesto</span>
                        <span>Participante</span>
                        <span>Puntos</span>
                    </div>
                    ${winners.map(item => `
                        <div class="support-cloud-live-winners__row is-rank-${item.rank}">
                            <span>${item.rankLabel}</span>
                            <strong>${escapeHtml(item.fullName || "Participante")}</strong>
                            <em>${escapeHtml(numberFormatter.format(Number(item.score || 0)))} pts</em>
                        </div>
                    `).join("")}
                </div>
            `;
        }

        function resolveLiveWinners(liveState) {
            const ranking = Array.isArray(liveState.ranking) ? liveState.ranking : [];
            return ranking.slice(0, 3)
                .map((item, index) => ({
                    ...item,
                    rank: index + 1,
                    rankLabel: index === 0 ? "1er" : (index === 1 ? "2do" : "3er")
                }))
                .reverse();
        }

        function renderLiveResponses(liveState) {
            if (!els.liveResponses) {
                return;
            }

            const responses = Array.isArray(liveState.questionResponses) ? liveState.questionResponses : [];
            if (!responses.length) {
                els.liveResponses.innerHTML = "";
                return;
            }

            const currentIndex = Number(liveState.currentQuestionIndex ?? -1);
            els.liveResponses.innerHTML = `
                <div class="support-cloud-live-responses__title">Respuestas por pregunta</div>
                ${responses.map(item => {
                    const answered = Number(item.answeredCount || 0);
                    const registered = Number(item.registeredCount || 0);
                    const percent = registered > 0 ? Math.min(100, Math.round((answered * 100) / registered)) : 0;
                    const isActive = Number(item.questionIndex || 0) === currentIndex;
                    return `
                        <article class="${isActive ? "is-active" : ""}">
                            <div>
                                <strong>${escapeHtml(`${Number(item.questionIndex || 0) + 1}. ${item.questionText || "Pregunta"}`)}</strong>
                                <span>${escapeHtml(numberFormatter.format(answered))}/${escapeHtml(numberFormatter.format(registered))} respondieron</span>
                            </div>
                            <div class="support-cloud-live-responses__track"><span style="width:${percent}%"></span></div>
                        </article>
                    `;
                }).join("")}
            `;
        }

        function resolveLivePresenterTitle(liveState) {
            if (liveState.phase === "registration") {
                return "Registro por QR";
            }
            if (liveState.phase === "intro") {
                return "Listo para la primera pregunta";
            }
            if (liveState.phase === "survey") {
                return "Encuesta final en curso";
            }
            if (liveState.phase === "winners") {
                return "Top 3 ganadores";
            }
            if (liveState.phase === "closed") {
                return "Sesion cerrada";
            }

            return "Encuesta live";
        }

        function resolveLiveTriggerText(liveState) {
            if (liveState.phase === "registration") {
                return "Iniciar preguntas";
            }
            if (liveState.phase === "intro") {
                return "Mostrar pregunta 1";
            }
            if (liveState.phase === "question") {
                const current = Number(liveState.currentQuestionIndex || 0) + 1;
                const total = Number(liveState.totalQuestions || 0);
                return current >= total ? "Esperando cierre" : "Esperando cierre";
            }
            if (liveState.phase === "ranking") {
                const current = Number(liveState.currentQuestionIndex || 0) + 1;
                const total = Number(liveState.totalQuestions || 0);
                return current >= total ? "Mostrar podio final" : "Mostrar siguiente pregunta";
            }
            if (liveState.phase === "winners") {
                return "Pasar a encuesta final";
            }
            if (liveState.phase === "survey") {
                return "Concluir";
            }
            return "Trigger";
        }

        function startLivePolling() {
            stopLivePolling();
            state.livePollTimer = window.setInterval(() => loadLiveState({ silent: true }), 1000);
        }

        function stopLivePolling() {
            window.clearInterval(state.livePollTimer);
            state.livePollTimer = 0;
            state.livePollPending = false;
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

            const durationMinutes = parseDecimal(els.closeDurationMinutes?.value || "0");
            if (durationMinutes < 0) {
                setStatus("error", "La duracion no puede ser negativa.");
                return;
            }

            setStatus("info", "Cerrando encuesta...");
            try {
                const result = await fetchJson(buildUrl(urls.closeSession, { sessionId: session.sessionId }), {
                    method: "POST",
                    body: JSON.stringify({ durationMinutes })
                });
                state.board = result?.board || state.board;
                renderBoard();
                await selectSession(session.sessionId, { preserveForm: true });
                closeQrModal();
                openWinnersModal();
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

        function resetLiveForm() {
            stopLivePolling();
            state.liveSessionId = "";
            state.liveState = null;
            state.liveClientSuggestions = [];
            setValue(els.liveSessionId, "");
            setValue(els.liveName, "");
            setValue(els.liveDate, new Date().toISOString().slice(0, 10));
            setValue(els.liveTopic, "");
            setValue(els.liveClientId, "");
            setValue(els.liveClientName, "");
            setText(els.liveFormMeta, "Lista para iniciar");
            if (els.liveConsole) {
                els.liveConsole.hidden = true;
            }
            renderLiveClientSuggestions();
            renderLiveQuestionPreviews();
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
            state.selectedTopicId = "";
            setValue(els.topicId, "");
            setValue(els.topicName, "");
            setValue(els.topicDescription, "");
            setValue(els.topicActive, "true");
            setText(els.topicMeta, "Nuevo tema");
            setText(els.topicBuilderTitle, "Nuevo tema");
            setTopicFormLocked(false);
            if (els.questionForm) {
                els.questionForm.hidden = true;
            }
            if (els.topicQuestionsPanel) {
                els.topicQuestionsPanel.hidden = true;
            }
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
            setText(els.topicMeta, topic.isLocked ? "Tema fijo" : "Editando tema");
            setText(els.topicBuilderTitle, topic.isLocked ? "Tema fijo" : "Editar tema");
            setTopicFormLocked(topic.isLocked === true);
            if (els.questionForm) {
                els.questionForm.hidden = topic.isLocked === true;
            }
            renderTopicQuestionList();
        }

        function resetQuestionForm(options = {}) {
            setValue(els.questionId, "");
            setValue(els.questionComponent, String(surveyComponentKnowledge));
            setValue(els.questionTopic, options.preserveTopic ? state.selectedTopicId : "");
            setValue(els.questionType, String(surveyInputSingleChoice));
            setValue(els.questionPoints, "1");
            setValue(els.questionOrder, "0");
            setValue(els.questionActive, "true");
            setValue(els.questionText, "");
            setText(els.questionMeta, "Nueva pregunta");
            if (els.deleteQuestion) {
                els.deleteQuestion.hidden = true;
                els.deleteQuestion.disabled = true;
            }
            state.optionsDraft = [createEmptyOption(true), createEmptyOption(false)];
            renderOptionsDraft();
            syncQuestionControls();
            renderTopicQuestionList();
            if (els.questionForm) {
                const selectedTopic = findTopic(state.selectedTopicId);
                els.questionForm.hidden = !selectedTopic || selectedTopic.isLocked === true;
            }
        }

        function fillQuestionForm(questionId) {
            const question = (Array.isArray(state.board?.questions) ? state.board.questions : [])
                .find(item => item.questionId === questionId);
            if (!question) {
                return;
            }
            if (question.isLocked) {
                setStatus("info", "Las preguntas de Satisfaccion son fijas.");
                return;
            }

            state.selectedTopicId = question.topicId || state.selectedTopicId;
            setValue(els.questionId, question.questionId || "");
            setValue(els.questionComponent, String(question.componentValue || surveyComponentKnowledge));
            setValue(els.questionTopic, question.topicId || "");
            setValue(els.questionType, String(question.inputTypeValue || surveyInputSingleChoice));
            setValue(els.questionPoints, String(question.maxPoints ?? 0));
            setValue(els.questionOrder, String(question.sortOrder || 0));
            setValue(els.questionActive, question.isActive === false ? "false" : "true");
            setValue(els.questionText, question.text || "");
            setText(els.questionMeta, "Editando pregunta");
            if (els.deleteQuestion) {
                els.deleteQuestion.hidden = question.isActive === false;
                els.deleteQuestion.disabled = question.isActive === false;
            }
            state.optionsDraft = Array.isArray(question.options) && question.options.length
                ? question.options.map(option => hydrateOptionDraft(option, Number(question.inputTypeValue || surveyInputSingleChoice)))
                : [createEmptyOption(true), createEmptyOption(false)];
            renderOptionsDraft();
            syncQuestionControls();
            renderTopicQuestionList();
        }

        function renderOptionsDraft() {
            if (!els.options) {
                return;
            }

            const typeValue = Number(els.questionType?.value || surveyInputSingleChoice);
            els.options.innerHTML = state.optionsDraft.map((option, index) => renderOptionDraftRow(option, index, typeValue)).join("");

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

        function renderOptionDraftRow(option, index, typeValue) {
            const controlType = typeValue === surveyInputMultipleChoice ? "checkbox" : "radio";
            const matching = typeValue === surveyInputMatching;
            return `
                <div class="support-cloud-survey-option ${matching ? "support-cloud-survey-option--matching" : ""}" data-scs-option-index="${index}">
                    <span class="support-cloud-survey-option__index">${escapeHtml(numberFormatter.format(index + 1))}</span>
                    <input type="hidden" data-scs-option-id value="${escapeHtml(option.optionId || "")}" />
                    <label class="support-cloud-survey-option__field">
                        <span>${matching ? "Texto para arrastrar" : "Respuesta"}</span>
                        <input type="text" class="form-control" placeholder="${matching ? "Ej. Dataverse" : "Escribe la opcion"}" data-scs-option-text value="${escapeHtml(option.text || "")}" />
                    </label>
                    ${matching
                        ? `<label class="support-cloud-survey-option__field">
                            <span>Campo asignado</span>
                            <input type="text" class="form-control" placeholder="Ej. Plataforma de datos" data-scs-option-target value="${escapeHtml(option.targetText || "")}" />
                        </label>`
                        : `<label class="support-cloud-survey-option__check">
                            <input type="${controlType}" name="supportCloudSurveyCorrectOption" data-scs-option-correct ${option.isCorrect ? "checked" : ""} />
                            <span>Correcta</span>
                        </label>`}
                    <button type="button" class="btn btn-outline-secondary" data-scs-remove-option>Quitar</button>
                </div>
            `;
        }

        function readOptionsDraft(typeValue) {
            const maxPoints = Number(els.questionPoints?.value || 0);
            return Array.from(els.options?.querySelectorAll("[data-scs-option-index]") || []).map((row, index) => ({
                optionId: row.querySelector("[data-scs-option-id]")?.value || "",
                text: buildOptionTextValue(row, typeValue),
                isCorrect: typeValue === surveyInputMatching ? true : Boolean(row.querySelector("[data-scs-option-correct]")?.checked),
                points: typeValue === surveyInputMatching || row.querySelector("[data-scs-option-correct]")?.checked ? maxPoints : 0,
                sortOrder: index + 1,
                isActive: true
            }));
        }

        function syncQuestionControls() {
            const isSatisfaction = Number(els.questionComponent?.value || 0) === surveyComponentSatisfaction;
            const typeValue = Number(els.questionType?.value || surveyInputSingleChoice);
            const isOptionBased = isOptionQuestionType(typeValue);
            if (els.questionTopic) {
                els.questionTopic.disabled = isSatisfaction;
            }
            if (els.questionPoints) {
                els.questionPoints.disabled = isSatisfaction;
                if (isSatisfaction) {
                    els.questionPoints.value = "0";
                }
            }
            if (els.optionsCard) {
                els.optionsCard.hidden = !isOptionBased;
                const title = els.optionsCard.querySelector(".support-cloud-survey-options-title");
                if (title) {
                    title.textContent = resolveOptionsTitle(typeValue);
                }
            }
            if (els.options) {
                els.options.hidden = !isOptionBased;
            }
            if (els.addOption) {
                els.addOption.hidden = !isOptionBased;
                els.addOption.textContent = typeValue === surveyInputMatching ? "Agregar par" : "Agregar respuesta";
            }
            renderOptionsDraft();
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

        function handleLiveClientLookup() {
            if (!urls.clientSearch) {
                return;
            }

            setValue(els.liveClientId, "");
            const query = (els.liveClientName?.value || "").trim();
            window.clearTimeout(state.liveClientTimer);
            if (query.length < 2) {
                state.liveClientSuggestions = [];
                renderLiveClientSuggestions();
                return;
            }

            state.liveClientTimer = window.setTimeout(async () => {
                try {
                    const items = await fetchJson(buildUrl(urls.clientSearch, { q: query }));
                    state.liveClientSuggestions = Array.isArray(items) ? items : [];
                    renderLiveClientSuggestions();
                    syncLiveClientSelection();
                } catch {
                    state.liveClientSuggestions = [];
                    renderLiveClientSuggestions();
                }
            }, 200);
        }

        function renderLiveClientSuggestions() {
            if (!els.liveClientOptions) {
                return;
            }

            els.liveClientOptions.innerHTML = state.liveClientSuggestions.map(item => `
                <option value="${escapeHtml(item.name || "")}" data-id="${escapeHtml(item.id || "")}"></option>
            `).join("");
        }

        function syncLiveClientSelection() {
            const value = normalizeText(els.liveClientName?.value || "");
            const match = state.liveClientSuggestions.find(item => normalizeText(item.name || "") === value);
            setValue(els.liveClientId, match?.id || "");
        }

        function openQrModal() {
            const session = findSession(state.selectedSessionId);
            if (session) {
                renderQr(session);
            }
            openModal(els.qrModal);
            window.clearInterval(state.qrRefreshTimer);
            state.qrRefreshTimer = window.setInterval(() => {
                if (!els.qrModal || els.qrModal.hidden) {
                    window.clearInterval(state.qrRefreshTimer);
                    return;
                }
                loadBoard({ silent: true });
            }, 10000);
        }

        function closeQrModal() {
            window.clearInterval(state.qrRefreshTimer);
            closeModal(els.qrModal);
        }

        function openWinnersModal() {
            renderWinners();
            openModal(els.winnersModal);
        }

        function renderWinners() {
            if (!els.winnersRows) {
                return;
            }

            const winners = Array.isArray(state.detail?.leaderboard) ? state.detail.leaderboard : [];
            els.winnersRows.innerHTML = winners.length
                ? winners.map((item, index) => `
                    <tr>
                        <td data-label="Posicion">${index + 1}</td>
                        <td data-label="Participante">${escapeHtml(item.fullName || "Participante")}</td>
                        <td data-label="Empresa">${escapeHtml(item.company || "-")}</td>
                        <td data-label="Puntaje" class="text-end support-cloud-table__hours">${escapeHtml(numberFormatter.format(Number(item.score || 0)))}</td>
                        <td data-label="Porcentaje" class="text-end support-cloud-table__hours">${escapeHtml(percentFormatter.format(Number(item.scorePercent || 0)))}%</td>
                    </tr>
                `).join("")
                : '<tr><td colspan="5" class="support-cloud-table__empty">Sin ganadores para mostrar.</td></tr>';
        }

        function openModal(modal) {
            if (!modal) {
                return;
            }

            modal.hidden = false;
            document.body.classList.add("support-cloud-modal-open");
        }

        function closeModal(modal) {
            if (!modal) {
                return;
            }

            modal.hidden = true;
            if (!root.querySelector(".support-cloud-modal:not([hidden])")) {
                document.body.classList.remove("support-cloud-modal-open");
            }
        }

        function setTopicFormLocked(isLocked) {
            [els.topicName, els.topicDescription, els.topicActive].forEach(element => {
                if (element) {
                    element.disabled = isLocked;
                }
            });
            if (els.saveTopicButton) {
                els.saveTopicButton.disabled = isLocked;
            }
            if (els.topicLockNotice) {
                els.topicLockNotice.hidden = !isLocked;
            }
        }

        function findTopic(topicId) {
            return (Array.isArray(state.board?.topics) ? state.board.topics : [])
                .find(topic => topic.topicId === topicId);
        }

        function findSession(sessionId) {
            return (Array.isArray(state.board?.sessions) ? state.board.sessions : [])
                .find(session => session.sessionId === sessionId);
        }

        function findSavedSession(payload) {
            if (payload.sessionId) {
                return findSession(payload.sessionId);
            }

            const sessions = Array.isArray(state.board?.sessions) ? state.board.sessions : [];
            return sessions.find(session =>
                normalizeText(session.name || "") === normalizeText(payload.name || "")
                && session.topicId === payload.topicId
                && (!payload.dateValue || session.dateValue === payload.dateValue))
                || sessions[0]
                || null;
        }

        function findSavedTopic(payload) {
            const topics = Array.isArray(state.board?.topics) ? state.board.topics : [];
            if (payload.topicId) {
                return topics.find(topic => topic.topicId === payload.topicId) || null;
            }

            return topics.find(topic => normalizeText(topic.name || "") === normalizeText(payload.name || ""))
                || null;
        }

        function createEmptyOption(isCorrect) {
            return {
                optionId: "",
                text: "",
                targetText: "",
                isCorrect: Boolean(isCorrect),
                points: isCorrect ? 1 : 0,
                sortOrder: 0,
                isActive: true
            };
        }

        function hydrateOptionDraft(option, typeValue) {
            const parsed = typeValue === surveyInputMatching
                ? parseMatchingOptionText(option.text || "")
                : { text: option.text || "", target: "" };
            return {
                ...option,
                text: parsed.text,
                targetText: parsed.target
            };
        }

        function buildOptionTextValue(row, typeValue) {
            const text = (row.querySelector("[data-scs-option-text]")?.value || "").trim();
            if (typeValue !== surveyInputMatching) {
                return text;
            }

            const target = (row.querySelector("[data-scs-option-target]")?.value || "").trim();
            return target ? `${text}${surveyMatchingSeparator}${target}` : text;
        }

        function parseMatchingOptionText(value) {
            const parts = String(value || "").split(surveyMatchingSeparator);
            return {
                text: (parts[0] || "").trim(),
                target: (parts.slice(1).join(surveyMatchingSeparator) || "").trim()
            };
        }

        function isOptionQuestionType(typeValue) {
            return typeValue === surveyInputSingleChoice
                || typeValue === surveyInputMultipleChoice
                || typeValue === surveyInputMatching;
        }

        function resolveOptionsTitle(typeValue) {
            if (typeValue === surveyInputMultipleChoice) {
                return "Selecciona una o varias respuestas correctas";
            }
            if (typeValue === surveyInputMatching) {
                return "Define el texto y su campo asignado";
            }

            return "Selecciona la respuesta correcta";
        }

        function resolveCorrectAnswerLabel(question) {
            const typeValue = Number(question?.inputTypeValue || surveyInputSingleChoice);
            const options = Array.isArray(question?.options) ? question.options.filter(option => option.isActive !== false) : [];
            if (typeValue === surveyInputRating) {
                return "Escala 1 a 5";
            }
            if (typeValue === surveyInputMatching) {
                return options.map(option => {
                    const parsed = parseMatchingOptionText(option.text || "");
                    return parsed.target ? `${parsed.text} -> ${parsed.target}` : parsed.text;
                }).filter(Boolean).join(" | ") || "Sin pares";
            }
            if (typeValue === surveyInputMultipleChoice) {
                return options.filter(option => option.isCorrect).map(option => option.text).join(" | ") || "Sin seleccionar";
            }

            return options.find(option => option.isCorrect)?.text || "Sin seleccionar";
        }

        function bySortOrder(left, right) {
            const leftOrder = Number(left?.sortOrder || 0);
            const rightOrder = Number(right?.sortOrder || 0);
            if (leftOrder !== rightOrder) {
                return leftOrder - rightOrder;
            }

            return String(left?.text || "").localeCompare(String(right?.text || ""), "es");
        }

        function setStatus(type, message) {
            if (!els.status) {
                return;
            }

            els.status.className = `support-cloud-status is-visible is-${type}`;
            els.status.textContent = message || "";
        }
    }

    function initializeLivePublicSurvey(root) {
        const payloadElement = document.getElementById("supportCloudLiveSurveyPayload");
        const payload = payloadElement ? JSON.parse(payloadElement.textContent || "{}") : {};
        const els = {
            messages: root.querySelector("[data-sclp-messages]"),
            topbar: root.querySelector("[data-sclp-topbar]"),
            progress: root.querySelector("[data-sclp-progress]"),
            phase: root.querySelector("[data-sclp-phase]"),
            score: root.querySelector("[data-sclp-score]"),
            scoreValue: root.querySelector("[data-sclp-score-value]"),
            bottom: root.querySelector("[data-sclp-bottom]"),
            inputContext: root.querySelector("[data-sclp-input-context]"),
            inputPanel: root.querySelector("[data-sclp-input-panel]"),
            input: root.querySelector("[data-sclp-input]"),
            send: root.querySelector("[data-sclp-send]"),
            wheelPanel: root.querySelector("[data-sclp-wheel-panel]"),
            wheel: root.querySelector("[data-sclp-wheel]"),
            spin: root.querySelector("[data-sclp-spin]"),
            result: root.querySelector("[data-sclp-result]"),
            wheelRanking: root.querySelector("[data-sclp-wheel-ranking]")
        };
        const storageKey = `supportCloudLiveParticipant:${payload.code || root.dataset.code || ""}`;
        const snapshotVersion = 3;
        const savedSnapshot = readLiveSnapshot();
        const canRestoreSnapshot = Number(savedSnapshot.version || 0) === snapshotVersion;
        const savedAnswers = canRestoreSnapshot && Array.isArray(savedSnapshot.answers)
            ? savedSnapshot.answers.filter(item => item && item.questionId).map(item => [item.questionId, item])
            : [];
        const savedAnsweredIds = canRestoreSnapshot && Array.isArray(savedSnapshot.answeredQuestionIds) && savedSnapshot.answeredQuestionIds.length
            ? savedSnapshot.answeredQuestionIds
            : savedAnswers.map(([questionId]) => questionId);
        const state = {
            participantKey: savedSnapshot.participantKey || "",
            participant: savedSnapshot.participant && typeof savedSnapshot.participant === "object" ? { ...savedSnapshot.participant } : {},
            registrationStep: 0,
            mode: "consent",
            pollTimer: 0,
            pollInFlight: false,
            pollPending: false,
            lastSequence: -1,
            shownIntro: false,
            shownSurvey: canRestoreSnapshot && Boolean(savedSnapshot.shownSurvey),
            shownRankingSequence: -1,
            shownWinnersSequence: -1,
            shownClosed: false,
            savingFinalSurvey: false,
            activeInputPrompt: "",
            currentTextQuestion: null,
            shownQuestionIds: new Set(),
            answeredQuestionIds: new Set(savedAnsweredIds),
            answers: new Map(savedAnswers),
            score: canRestoreSnapshot ? Number(savedSnapshot.score || 0) : 0,
            maxScore: canRestoreSnapshot ? Number(savedSnapshot.maxScore || 0) : 0,
            scoreMaxQuestions: canRestoreSnapshot ? Number(savedSnapshot.scoreMaxQuestions || 0) : 0,
            correctAnswers: canRestoreSnapshot ? Number(savedSnapshot.correctAnswers || 0) : 0,
            totalQuestions: canRestoreSnapshot ? Number(savedSnapshot.totalQuestions || 0) : 0,
            hasSpun: canRestoreSnapshot && Boolean(savedSnapshot.hasSpun),
            wheelNumber: canRestoreSnapshot ? Number(savedSnapshot.wheelNumber || 0) : 0,
            submittedFinalSurvey: canRestoreSnapshot && Boolean(savedSnapshot.submittedFinalSurvey),
            restoreRegisterAttempted: false,
            currentQuestionIndex: canRestoreSnapshot ? Number(savedSnapshot.currentQuestionIndex ?? -1) : -1,
            currentQuestionEndsAt: 0,
            currentQuestionStartedAt: 0,
            questionTimer: 0
        };
        const registrationFields = [
            { key: "fullName", label: "nombre completo", question: "Para comenzar, escribe tu nombre completo." },
            { key: "company", label: "empresa", question: "Indica la empresa a la que perteneces." },
            { key: "role", label: "rol", question: "Escribe tu rol dentro de la empresa." },
            { key: "email", label: "correo", question: "Por ultimo, escribe tu correo empresarial." }
        ];

        els.send?.addEventListener("click", submitLiveText);
        els.input?.addEventListener("keydown", event => {
            if (event.key === "Enter" && !event.shiftKey) {
                event.preventDefault();
                submitLiveText();
            }
        });
        els.input?.addEventListener("input", () => {
            els.input.style.height = "auto";
            els.input.style.height = `${Math.min(els.input.scrollHeight, 120)}px`;
        });
        els.input?.addEventListener("focus", () => {
            root.classList.add("is-keyboard-entry");
            updateInputContext();
            window.setTimeout(() => els.inputContext?.scrollIntoView({ block: "nearest" }), 80);
        });
        els.input?.addEventListener("blur", () => {
            root.classList.remove("is-keyboard-entry");
        });
        els.spin?.addEventListener("click", spinLiveWheel);

        if (state.maxScore > 0 || state.score > 0) {
            updateLiveScore();
        }

        if (payload.isClosed) {
            showClosedResults();
        } else if (state.participantKey) {
            resumeLiveParticipant();
        } else {
            startLiveConsent();
        }

        function readLiveSnapshot() {
            try {
                const raw = window.localStorage.getItem(storageKey)
                    || window.sessionStorage.getItem(storageKey)
                    || "";
                if (!raw) {
                    return {};
                }

                const trimmed = raw.trim();
                if (!trimmed.startsWith("{")) {
                    return { participantKey: trimmed };
                }

                const parsed = JSON.parse(trimmed);
                return parsed && typeof parsed === "object" ? parsed : {};
            } catch {
                return {};
            }
        }

        function persistLiveSnapshot() {
            const snapshot = {
                version: snapshotVersion,
                participantKey: state.participantKey || "",
                participant: state.participant || {},
                answers: Array.from(state.answers.values()),
                answeredQuestionIds: Array.from(state.answeredQuestionIds),
                score: state.score,
                maxScore: state.maxScore,
                scoreMaxQuestions: state.scoreMaxQuestions,
                correctAnswers: state.correctAnswers,
                totalQuestions: state.totalQuestions,
                currentQuestionIndex: state.currentQuestionIndex,
                shownSurvey: state.shownSurvey,
                submittedFinalSurvey: state.submittedFinalSurvey,
                hasSpun: state.hasSpun,
                wheelNumber: state.wheelNumber,
                updatedAt: new Date().toISOString()
            };

            try {
                window.localStorage.setItem(storageKey, JSON.stringify(snapshot));
            } catch {
            }

            try {
                if (state.participantKey) {
                    window.sessionStorage.setItem(storageKey, state.participantKey);
                }
            } catch {
            }
        }

        function restoreParticipantProgress(progress) {
            if (!progress || typeof progress !== "object") {
                return;
            }

            if (progress.participantKey) {
                state.participantKey = progress.participantKey;
            }

            state.participant = {
                ...state.participant,
                fullName: progress.fullName || state.participant.fullName || "",
                identification: progress.identification || state.participant.identification || "",
                company: progress.company || state.participant.company || "",
                role: progress.role || state.participant.role || "",
                email: progress.email || state.participant.email || ""
            };

            const serverAnswers = Array.isArray(progress.answers) ? progress.answers : [];
            if (serverAnswers.length) {
                serverAnswers.forEach(answer => {
                    const questionId = answer?.questionId || "";
                    if (!questionId) {
                        return;
                    }

                    state.answers.set(questionId, {
                        questionId,
                        optionId: answer.optionId || "",
                        numericValue: answer.numericValue,
                        textValue: answer.textValue || "",
                        points: Number(answer.points || 0),
                        maxPoints: Number(answer.maxPoints || 0),
                        isCorrect: Boolean(answer.isCorrect),
                        responseSeconds: Number(answer.responseSeconds || 0)
                    });
                    state.answeredQuestionIds.add(questionId);
                });
            }

            if (serverAnswers.length || Number(progress.maxScore || 0) > 0 || Number(progress.score || 0) > 0 || !state.answers.size) {
                state.score = Number(progress.score || 0);
                state.maxScore = Math.max(Number(progress.maxScore || 0), Number(state.maxScore || 0), Number(state.scoreMaxQuestions || 0) * 10);
            }
            state.correctAnswers = Number(progress.correctAnswers || 0);
            state.wheelNumber = Number(progress.wheelNumber || state.wheelNumber || 0);
            state.submittedFinalSurvey = Boolean(progress.completed) || state.submittedFinalSurvey;
            if (state.maxScore > 0 || state.score > 0) {
                updateLiveScore();
            }
            persistLiveSnapshot();
        }

        function updateScoreWindowFromLiveState(liveState) {
            if (!liveState || typeof liveState !== "object") {
                return;
            }

            const total = Number(liveState.totalQuestions || state.totalQuestions || 0);
            state.totalQuestions = total;
            const currentIndex = Number(liveState.currentQuestionIndex ?? -1);
            if (Number.isFinite(currentIndex)) {
                state.currentQuestionIndex = currentIndex;
            }

            const phase = liveState.phase || "";
            if ((phase === "question" || phase === "ranking") && currentIndex >= 0) {
                state.scoreMaxQuestions = Math.max(Number(state.scoreMaxQuestions || 0), currentIndex + 1);
            }
            if (phase === "winners" || phase === "survey" || phase === "closed") {
                state.scoreMaxQuestions = Math.max(Number(state.scoreMaxQuestions || 0), total);
            }

            const expectedMaxScore = Math.max(0, Number(state.scoreMaxQuestions || 0) * 10);
            if (expectedMaxScore > 0) {
                state.maxScore = Math.max(Number(state.maxScore || 0), expectedMaxScore);
            }
        }

        function resumeLiveParticipant() {
            state.mode = "live";
            setLiveScreen("waiting", true);
            showPanel("none");
            addBot("Recuperando tu progreso guardado...");
            showLiveTopbar(null);
            startLivePolling();
        }

        function startLiveConsent() {
            setLiveScreen("registration", false);
            const bubble = addBot(`Bienvenido a *${payload.sessionName || "la capacitacion"}*.

La informacion suministrada sera usada para registrar asistencia, evaluar la capacitacion y fortalecer la mejora continua. Al continuar autorizas el tratamiento de tus datos para estos fines.`);
            const actions = document.createElement("div");
            actions.className = "support-cloud-live-consent";
            actions.innerHTML = `
                <button type="button" class="support-cloud-live-primary" data-sclp-accept>Acepto</button>
                <button type="button" class="support-cloud-live-secondary" data-sclp-reject>No acepto</button>
            `;
            bubble.appendChild(actions);
            actions.querySelector("[data-sclp-accept]")?.addEventListener("click", () => {
                actions.remove();
                addUser("Acepto");
                state.mode = "registration";
                askRegistration();
            });
            actions.querySelector("[data-sclp-reject]")?.addEventListener("click", () => {
                actions.remove();
                addUser("No acepto");
                addBot("Entendemos tu decision. Sin autorizacion no podemos continuar con el registro.");
                showPanel("none");
            });
        }

        function askRegistration() {
            const field = registrationFields[state.registrationStep];
            if (!field) {
                registerParticipant();
                return;
            }

            addBot(field.question);
            state.activeInputPrompt = field.question;
            showPanel("input");
            if (els.input) {
                els.input.placeholder = `Escribe tu ${field.label}...`;
                els.input.focus();
            }
        }

        async function registerParticipant() {
            showPanel("none");
            addBot("Registrando tu asistencia...");
            try {
                const result = await fetchJson(payload.liveRegisterUrl || "", {
                    method: "POST",
                    body: JSON.stringify({
                        participantKey: state.participantKey,
                        fullName: state.participant.fullName || "",
                        identification: state.participant.identification || "",
                        company: state.participant.company || "",
                        role: state.participant.role || "",
                        email: state.participant.email || ""
                    })
                });
                state.participantKey = result?.participantKey || state.participantKey;
                if (result?.state?.participantProgress) {
                    restoreParticipantProgress(result.state.participantProgress);
                } else {
                    persistLiveSnapshot();
                }
                setLiveScreen("waiting", true);
                addBot(result?.message || "Registro recibido. Espera al presentador.");
                showLiveTopbar(result?.state || null);
                startLivePolling();
            } catch (error) {
                addFeedback(false, buildErrorMessage(error));
                state.registrationStep = Math.max(0, registrationFields.length - 1);
                askRegistration();
            }
        }

        function startLivePolling() {
            stopLivePolling();
            loadLiveParticipantState();
            state.pollTimer = window.setInterval(loadLiveParticipantState, 1000);
        }

        function stopLivePolling() {
            window.clearInterval(state.pollTimer);
            state.pollTimer = 0;
            state.pollPending = false;
            stopLiveQuestionTimer();
        }

        async function loadLiveParticipantState() {
            if (!payload.liveStateUrl) {
                return;
            }
            if (state.pollInFlight) {
                state.pollPending = true;
                return;
            }

            state.pollInFlight = true;
            try {
                const liveStateUrl = state.participantKey
                    ? buildUrl(payload.liveStateUrl, { participantKey: state.participantKey })
                    : payload.liveStateUrl;
                const liveState = await fetchJson(liveStateUrl);
                handleLiveParticipantState(liveState);
            } catch (error) {
                setLivePhase("Sin conexion", buildErrorMessage(error));
            } finally {
                state.pollInFlight = false;
                if (state.pollPending) {
                    state.pollPending = false;
                    window.setTimeout(loadLiveParticipantState, 0);
                }
            }
        }

        function handleLiveParticipantState(liveState) {
            if (!liveState) {
                return;
            }

            if (liveState.phase === "removed") {
                handleRemovedParticipant(liveState);
                return;
            }

            restoreParticipantProgress(liveState.participantProgress);
            updateScoreWindowFromLiveState(liveState);
            updateLiveScore();
            persistLiveSnapshot();
            if (liveWheelEnabled && Array.isArray(liveState.wheelRanking)) {
                renderLiveWheelRanking(liveState);
            }
            if (!liveState.participantProgress
                && state.participantKey
                && !state.restoreRegisterAttempted) {
                state.restoreRegisterAttempted = true;
                if (state.participant.fullName) {
                    registerParticipant();
                } else {
                    state.participantKey = "";
                    persistLiveSnapshot();
                    addBot("No encontramos tus datos guardados. Vamos a registrarte de nuevo para continuar.");
                    state.mode = "registration";
                    askRegistration();
                }
                return;
            }
            showLiveTopbar(liveState);
            if (liveState.sequence === state.lastSequence && liveState.phase !== "question") {
                return;
            }
            state.lastSequence = liveState.sequence;

            if (liveState.phase === "registration") {
                return;
            }
            if (liveState.phase === "intro" && !state.shownIntro) {
                state.shownIntro = true;
                setLiveScreen("intro", true);
                addBot("Mantente atento a las preguntas que haremos durante la capacitacion.");
                return;
            }
            if (liveState.phase === "question" && liveState.currentQuestion) {
                const questionId = liveState.currentQuestion.questionId || "";
                if (!state.shownQuestionIds.has(questionId) && !state.answeredQuestionIds.has(questionId)) {
                    state.shownQuestionIds.add(questionId);
                    showLiveQuestion(liveState.currentQuestion, liveState.currentQuestionIndex, liveState.totalQuestions, liveState);
                }
                return;
            }
            if (liveState.phase === "ranking") {
                showPanel("none");
                stopLiveQuestionTimer();
                if (state.shownRankingSequence !== liveState.sequence) {
                    state.shownRankingSequence = liveState.sequence;
                    showLiveRanking(liveState);
                }
                return;
            }
            if (liveState.phase === "winners") {
                showPanel("none");
                stopLiveQuestionTimer();
                if (state.shownWinnersSequence !== liveState.sequence) {
                    state.shownWinnersSequence = liveState.sequence;
                    showLiveWinners(liveState);
                }
                return;
            }
            if (liveState.phase === "survey") {
                if (state.submittedFinalSurvey) {
                    if (liveWheelEnabled) {
                        setLiveScreen("wheel", true);
                        showPanel("wheel");
                    } else {
                        showFinalThanksAfterSurvey("Gracias por completar la encuesta final.");
                    }
                    return;
                }
                if (!state.shownSurvey) {
                    state.shownSurvey = true;
                    setLiveScreen("survey", true);
                    addBot("Gracias por responder las preguntas. Para finalizar, completa la encuesta de satisfaccion.");
                    persistLiveSnapshot();
                }
                if (state.mode !== "satisfactionText") {
                    state.mode = "survey";
                    askSatisfactionQuestion();
                }
                return;
            }
            if (liveState.phase === "closed") {
                stopLivePolling();
                stopLiveQuestionTimer();
                showPanel("none");
                if (!state.shownClosed) {
                    state.shownClosed = true;
                    setLiveScreen("closed", true);
                    addBot(renderLiveFinalThanks(liveState));
                }
            }
        }

        function handleRemovedParticipant(liveState) {
            stopLivePolling();
            stopLiveQuestionTimer();
            state.participantKey = "";
            state.participant = {};
            state.activeInputPrompt = "";
            try {
                window.localStorage.removeItem(storageKey);
                window.sessionStorage.removeItem(storageKey);
            } catch {
            }
            showPanel("none");
            setLiveScreen("closed", true);
            showLiveTopbar(liveState);
            addBot("Tu registro fue retirado por el organizador de la sesion. Si crees que fue un error, solicita un nuevo codigo o pide apoyo al presentador.");
        }

        function renderLiveFinalThanks(liveState) {
            if (!liveWheelEnabled) {
                return "*Gracias por participar.*\n\nEsperamos que sigas participando en nuestras sesiones y que visites las redes sociales de Digital Tech para conocer proximas actividades.";
            }

            const ranking = Array.isArray(liveState?.wheelRanking) ? liveState.wheelRanking : [];
            const wheelLeader = ranking[0];
            const wheelText = wheelLeader
                ? `\n\nNumero mas alto de la ruleta: ${wheelLeader.fullName || "Participante"} con ${numberFormatter.format(Number(wheelLeader.number || 0))}.`
                : "";
            return `*Gracias por participar.*\n\nEsperamos que sigas participando en nuestras sesiones y que visites las redes sociales de Digital Tech para conocer proximas actividades.${wheelText}`;
        }

        function showLiveQuestion(question, index, total, liveState) {
            state.mode = "question";
            setLiveScreen("question", true);
            showPanel("none");
            state.currentQuestionIndex = Number(index ?? 0);
            state.totalQuestions = Number(total || state.totalQuestions || 0);
            state.scoreMaxQuestions = Math.max(Number(state.scoreMaxQuestions || 0), state.currentQuestionIndex + 1);
            state.maxScore = Math.max(Number(state.maxScore || 0), Number(state.scoreMaxQuestions || 0) * 10);
            const clientNow = Date.now();
            const serverNow = Date.parse(liveState?.serverNowUtc || "");
            const serverStartedAt = Date.parse(liveState?.questionStartedOnUtc || "");
            const serverEndsAt = Date.parse(liveState?.questionEndsOnUtc || "");
            const durationMs = Math.max(1000, Number(liveState?.questionDurationSeconds || 20) * 1000);
            const durationSeconds = Math.max(1, Math.round(durationMs / 1000));
            const elapsedFromServer = Number.isFinite(serverNow) && Number.isFinite(serverStartedAt)
                ? Math.max(0, Math.min(durationMs, serverNow - serverStartedAt))
                : 0;
            const remainingFromServer = Number.isFinite(serverNow) && Number.isFinite(serverEndsAt)
                ? Math.max(0, serverEndsAt - serverNow)
                : durationMs;
            state.currentQuestionStartedAt = clientNow - elapsedFromServer;
            state.currentQuestionEndsAt = clientNow + remainingFromServer;
            updateLiveScore();
            persistLiveSnapshot();
            setLivePhase(`Pregunta ${Number(index || 0) + 1} de ${Number(total || 0)}`, `Tienes ${durationSeconds} segundos para responder.`);
            const bubble = addBot("");
            bubble.classList.add("support-cloud-live-msg--question");
            bubble.innerHTML = `
                <div class="support-cloud-live-question-meta">
                    <span>Pregunta ${numberFormatter.format(Number(index || 0) + 1)} de ${numberFormatter.format(Number(total || 0))}</span>
                    <span data-sclp-question-countdown>${durationSeconds}.0 s</span>
                </div>
                <strong>${escapeHtml(question.text || "Pregunta")}</strong>
                <div class="support-cloud-live-timer" aria-hidden="true">
                    <span data-sclp-question-timer></span>
                </div>
            `;
            startLiveQuestionTimer(bubble, question.questionId || "");
            const inputType = Number(question.inputTypeValue || 0);
            if (inputType === surveyInputRating) {
                renderRatingOptions(bubble, question, value => answerLiveQuestion(question, { numericValue: value }, `${value}/5`));
                return;
            }
            if (inputType === surveyInputText) {
                state.currentTextQuestion = question;
                state.mode = "questionText";
                state.activeInputPrompt = question.text || "Pregunta";
                showPanel("input");
                if (els.input) {
                    els.input.placeholder = "Escribe tu respuesta...";
                    els.input.focus();
                }
                return;
            }

            if (inputType === surveyInputMultipleChoice) {
                renderLiveMultipleChoiceQuestion(bubble, question);
                return;
            }

            if (inputType === surveyInputMatching) {
                renderLiveMatchingQuestion(bubble, question);
                return;
            }

            renderLiveSingleChoiceQuestion(bubble, question);
        }

        function getLiveAnswerLetter(index) {
            const letters = ["A", "B", "C", "D", "E", "F", "G", "H"];
            return letters[index] || String(index + 1);
        }

        function createLiveAnswerButton(option, index, extraClass = "") {
            const button = document.createElement("button");
            button.type = "button";
            button.className = `support-cloud-live-option support-cloud-live-option--answer support-cloud-live-option--tone-${(index % 4) + 1} ${extraClass}`.trim();
            button.innerHTML = `
                <span class="support-cloud-live-answer-badge">${escapeHtml(getLiveAnswerLetter(index))}</span>
                <span class="support-cloud-live-answer-text">${escapeHtml(option?.text || "Opcion")}</span>
            `;
            return button;
        }

        function startLiveQuestionTimer(bubble, questionId) {
            stopLiveQuestionTimer();
            const bar = bubble.querySelector("[data-sclp-question-timer]");
            const label = bubble.querySelector("[data-sclp-question-countdown]");
            const duration = Math.max(1, state.currentQuestionEndsAt - state.currentQuestionStartedAt);
            const tick = () => {
                const remaining = Math.max(0, state.currentQuestionEndsAt - Date.now());
                const percent = Math.max(0, Math.min(100, (remaining * 100) / duration));
                if (bar) {
                    bar.style.width = `${percent}%`;
                }
                if (label) {
                    label.textContent = `${(remaining / 1000).toFixed(1)} s`;
                }
                if (remaining <= 0) {
                    stopLiveQuestionTimer();
                    disableLiveQuestionInputs(bubble);
                    if (!state.answeredQuestionIds.has(questionId)) {
                        addFeedback(false, "Tiempo finalizado.");
                    }
                    loadLiveParticipantState();
                }
            };

            tick();
            state.questionTimer = window.setInterval(tick, 100);
        }

        function stopLiveQuestionTimer() {
            window.clearInterval(state.questionTimer);
            state.questionTimer = 0;
        }

        function disableLiveQuestionInputs(scope) {
            scope?.querySelectorAll("button, textarea, input").forEach(item => {
                item.disabled = true;
                item.classList.add("is-disabled");
            });
            if (state.mode === "questionText") {
                showPanel("none");
            }
        }

        function isLiveQuestionExpired() {
            return Boolean(state.currentQuestionEndsAt) && Date.now() > state.currentQuestionEndsAt;
        }

        function calculateClientTimedPoints() {
            const elapsedSeconds = Math.max(0, (Date.now() - state.currentQuestionStartedAt) / 1000);
            if (elapsedSeconds <= 6) {
                return 10;
            }
            if (elapsedSeconds <= 10) {
                return 7;
            }
            if (elapsedSeconds <= 15) {
                return 4;
            }

            return 0;
        }

        function renderLiveSingleChoiceQuestion(bubble, question) {
            const options = document.createElement("div");
            options.className = "support-cloud-live-options support-cloud-live-options--quiz";
            (Array.isArray(question.options) ? question.options : []).forEach((option, optionIndex) => {
                const button = createLiveAnswerButton(option, optionIndex);
                button.addEventListener("click", () => {
                    answerLiveQuestion(question, { optionId: option.optionId || "" }, option.text || "Opcion", options, option);
                });
                options.appendChild(button);
            });
            bubble.appendChild(options);
            scrollDown();
        }

        function renderLiveMultipleChoiceQuestion(bubble, question) {
            const options = document.createElement("div");
            options.className = "support-cloud-live-options support-cloud-live-options--quiz";
            const activeOptions = Array.isArray(question.options) ? question.options : [];
            activeOptions.forEach((option, optionIndex) => {
                const button = createLiveAnswerButton(option, optionIndex, "support-cloud-live-option--multi");
                button.dataset.optionId = option.optionId || "";
                button.setAttribute("aria-pressed", "false");
                button.addEventListener("click", () => {
                    const selected = !button.classList.contains("is-selected");
                    button.classList.toggle("is-selected", selected);
                    button.setAttribute("aria-pressed", selected ? "true" : "false");
                });
                options.appendChild(button);
            });

            const submit = document.createElement("button");
            submit.type = "button";
            submit.className = "support-cloud-live-confirm";
            submit.textContent = "Enviar respuestas";
            submit.addEventListener("click", () => {
                const selectedButtons = Array.from(options.querySelectorAll(".support-cloud-live-option.is-selected"));
                if (!selectedButtons.length) {
                    addFeedback(false, "Selecciona al menos una respuesta.");
                    return;
                }

                const selectedIds = selectedButtons.map(button => button.dataset.optionId || "").filter(Boolean);
                const selectedOptions = activeOptions.filter(option => selectedIds.includes(option.optionId || ""));
                const correctIds = activeOptions.filter(option => option.isCorrect).map(option => option.optionId || "").filter(Boolean);
                const isCorrect = selectedIds.length === correctIds.length
                    && selectedIds.every(id => correctIds.includes(id));
                const correctText = activeOptions.filter(option => option.isCorrect).map(option => option.text).join(" / ");
                answerLiveQuestion(
                    question,
                    {
                        textValue: JSON.stringify(selectedIds),
                        isCorrect,
                        correctText
                    },
                    selectedOptions.map(option => option.text || "Opcion").join(" / "),
                    options);
            });
            bubble.appendChild(options);
            bubble.appendChild(submit);
            scrollDown();
        }

        function renderLiveMatchingQuestion(bubble, question) {
            const pairs = (Array.isArray(question.options) ? question.options : [])
                .map(option => {
                    const parsed = parseSurveyMatchingOptionText(option.text || "");
                    return {
                        optionId: option.optionId || "",
                        text: parsed.text,
                        target: parsed.target
                    };
                })
                .filter(item => item.optionId && item.text && item.target);

            if (!pairs.length) {
                addFeedback(false, "Esta pregunta no tiene pares configurados.");
                return;
            }

            const wrapper = document.createElement("div");
            wrapper.className = "support-cloud-live-matching";
            const bank = document.createElement("div");
            bank.className = "support-cloud-live-match-bank";
            const targets = document.createElement("div");
            targets.className = "support-cloud-live-match-targets";
            const assignments = new Map();
            let draggingId = "";

            pairs.forEach(pair => {
                const item = document.createElement("button");
                item.type = "button";
                item.className = "support-cloud-live-drag-item";
                item.draggable = true;
                item.dataset.optionId = pair.optionId;
                item.textContent = pair.text;
                item.addEventListener("dragstart", event => {
                    draggingId = pair.optionId;
                    event.dataTransfer?.setData("text/plain", pair.optionId);
                    item.classList.add("is-dragging");
                });
                item.addEventListener("dragend", () => {
                    draggingId = "";
                    item.classList.remove("is-dragging");
                });
                bank.appendChild(item);
            });

            pairs.forEach(pair => {
                const row = document.createElement("div");
                row.className = "support-cloud-live-drop-row";
                row.dataset.target = pair.target;
                row.innerHTML = `
                    <span>${escapeHtml(pair.target)}</span>
                    <div class="support-cloud-live-dropzone" data-drop-target="${escapeHtml(pair.target)}">Suelta aqui</div>
                `;
                const zone = row.querySelector("[data-drop-target]");
                zone?.addEventListener("dragover", event => {
                    event.preventDefault();
                    zone.classList.add("is-over");
                });
                zone?.addEventListener("dragleave", () => zone.classList.remove("is-over"));
                zone?.addEventListener("drop", event => {
                    event.preventDefault();
                    zone.classList.remove("is-over");
                    const optionId = event.dataTransfer?.getData("text/plain") || draggingId;
                    assignLiveMatch(optionId, pair.target, bank, zone, assignments);
                });
                targets.appendChild(row);
            });

            const submit = document.createElement("button");
            submit.type = "button";
            submit.className = "support-cloud-live-confirm";
            submit.textContent = "Enviar asignaciones";
            submit.addEventListener("click", () => {
                if (assignments.size < pairs.length) {
                    addFeedback(false, "Arrastra cada texto a un campo asignado.");
                    return;
                }

                const submitted = pairs.map(pair => ({
                    optionId: pair.optionId,
                    target: assignments.get(pair.optionId) || ""
                }));
                const isCorrect = pairs.every(pair => normalizeText(assignments.get(pair.optionId) || "") === normalizeText(pair.target));
                wrapper.querySelectorAll(".support-cloud-live-drop-row").forEach(row => {
                    const target = row.dataset.target || "";
                    const assignedPair = pairs.find(pair => assignments.get(pair.optionId) === target);
                    row.classList.toggle("is-correct", Boolean(assignedPair) && normalizeText(assignedPair.target) === normalizeText(target));
                    row.classList.toggle("is-wrong", !assignedPair || normalizeText(assignedPair.target) !== normalizeText(target));
                });
                wrapper.querySelectorAll(".support-cloud-live-drag-item").forEach(item => {
                    item.draggable = false;
                    item.classList.add("is-disabled");
                });
                submit.disabled = true;
                answerLiveQuestion(
                    question,
                    {
                        textValue: JSON.stringify(submitted),
                        isCorrect,
                        correctText: pairs.map(pair => `${pair.text} -> ${pair.target}`).join(" / ")
                    },
                    submitted.map(item => {
                        const pair = pairs.find(candidate => candidate.optionId === item.optionId);
                        return `${pair?.text || "Texto"} -> ${item.target || "sin asignar"}`;
                    }).join(" / "),
                    wrapper);
            });

            wrapper.appendChild(bank);
            wrapper.appendChild(targets);
            bubble.appendChild(wrapper);
            bubble.appendChild(submit);
            scrollDown();
        }

        function assignLiveMatch(optionId, target, bank, zone, assignments) {
            if (!optionId || !zone) {
                return;
            }

            const wrapper = bank.parentElement;
            const item = Array.from(wrapper?.querySelectorAll(".support-cloud-live-drag-item") || [])
                .find(candidate => candidate.dataset.optionId === optionId);
            if (!item) {
                return;
            }

            const previousTargetForItem = assignments.get(optionId) || "";
            if (previousTargetForItem && previousTargetForItem !== target) {
                const previousZone = Array.from(wrapper?.querySelectorAll(".support-cloud-live-dropzone") || [])
                    .find(candidate => candidate.dataset.dropTarget === previousTargetForItem);
                if (previousZone) {
                    previousZone.classList.remove("is-filled");
                    previousZone.textContent = "Suelta aqui";
                }
            }

            const previousOptionId = Array.from(assignments.entries())
                .find(([, assignedTarget]) => assignedTarget === target)?.[0] || "";
            if (previousOptionId) {
                assignments.delete(previousOptionId);
                const previousItem = Array.from(wrapper?.querySelectorAll(".support-cloud-live-drag-item") || [])
                    .find(candidate => candidate.dataset.optionId === previousOptionId);
                if (previousItem) {
                    bank.appendChild(previousItem);
                }
            }

            assignments.set(optionId, target);
            zone.textContent = "";
            zone.appendChild(item);
            zone.classList.add("is-filled");
        }

        function answerLiveQuestion(question, answer, displayText, optionsContainer, selectedOption) {
            const questionId = question.questionId || "";
            if (!questionId || state.answeredQuestionIds.has(questionId)) {
                return;
            }

            if (optionsContainer) {
                Array.from(optionsContainer.querySelectorAll(".support-cloud-live-option")).forEach((button, optionIndex) => {
                    const option = question.options?.[optionIndex];
                    button.classList.add("is-disabled");
                    if (option?.isCorrect) {
                        button.classList.add("is-correct");
                    } else if (option === selectedOption || button.classList.contains("is-selected")) {
                        button.classList.add("is-wrong");
                    }
                });
            }

            const maxPoints = Number(question.maxPoints || 0);
            const hasComputedCorrectness = typeof answer.isCorrect === "boolean";
            const isCorrect = hasComputedCorrectness ? answer.isCorrect : Boolean(selectedOption?.isCorrect);
            const points = isCorrect ? calculateClientTimedPoints() : 0;
            state.scoreMaxQuestions = Math.max(
                Number(state.scoreMaxQuestions || 0),
                Number(state.currentQuestionIndex || 0) + 1,
                state.answeredQuestionIds.size + 1
            );
            state.maxScore = Math.max(Number(state.maxScore || 0), Number(state.scoreMaxQuestions || 0) * 10);
            state.answeredQuestionIds.add(questionId);
            state.answers.set(questionId, {
                questionId,
                optionId: answer.optionId || "",
                numericValue: answer.numericValue,
                textValue: answer.textValue || "",
                points,
                maxPoints,
                isCorrect
            });
            submitLiveAnswer(question, answer);
            if (isCorrect) {
                state.score += points;
                state.correctAnswers++;
                addFeedback(true, `Correcto. ${numberFormatter.format(points)} puntos.`);
                launchLiveCelebration();
            } else if (selectedOption || hasComputedCorrectness) {
                const correctText = answer.correctText || (question.options || []).filter(option => option.isCorrect).map(option => option.text).join(" / ");
                addFeedback(false, correctText ? `Respuesta correcta: ${correctText}` : "Respuesta registrada.");
            } else {
                addFeedback(true, "Respuesta registrada.");
            }
            updateLiveScore();
            persistLiveSnapshot();
        }

        async function submitLiveAnswer(question, answer) {
            if (!payload.liveAnswerUrl || !state.participantKey || !question?.questionId) {
                return;
            }

            try {
                const result = await fetchJson(payload.liveAnswerUrl, {
                    method: "POST",
                    body: JSON.stringify({
                        participantKey: state.participantKey,
                        questionId: question.questionId,
                        optionId: answer.optionId || "",
                        numericValue: answer.numericValue,
                        textValue: answer.textValue || ""
                    })
                });
                restoreParticipantProgress(result?.participantProgress);
            } catch (error) {
                addFeedback(false, `No pudimos actualizar el ranking live: ${buildErrorMessage(error)}`);
            }
        }

        function launchLiveCelebration() {
            if (!root) {
                return;
            }

            const colors = ["#f97316", "#0ea5e9", "#22c55e", "#e11d48", "#facc15", "#8b5cf6"];
            for (let index = 0; index < 20; index++) {
                const particle = document.createElement("span");
                particle.className = "support-cloud-live-confetti";
                particle.style.setProperty("--x", `${Math.round((Math.random() * 220) - 110)}px`);
                particle.style.setProperty("--y", `${Math.round((Math.random() * -180) - 40)}px`);
                particle.style.setProperty("--r", `${Math.round(Math.random() * 280)}deg`);
                particle.style.background = colors[index % colors.length];
                root.appendChild(particle);
                window.setTimeout(() => particle.remove(), 950);
            }
        }

        function showLiveRanking(liveState) {
            setLiveScreen("ranking", true);
            const bubble = addBot("");
            bubble.classList.add("support-cloud-live-msg--ranking-card");
            bubble.innerHTML = renderLivePublicRanking(liveState);
            scrollDown();
        }

        function renderLivePublicRanking(liveState) {
            const ranking = Array.isArray(liveState.ranking) ? liveState.ranking : [];
            const answered = Number(liveState.currentQuestionAnsweredCount || 0);
            const registered = Number(liveState.registeredCount || 0);
            if (!ranking.length) {
                return `
                    <div class="support-cloud-live-ranking-panel">
                        <div class="support-cloud-live-ranking-title">Tiempo cerrado</div>
                        <p>Respondieron ${escapeHtml(numberFormatter.format(answered))} de ${escapeHtml(numberFormatter.format(registered))} registrados.</p>
                    </div>
                `;
            }

            const topScore = Math.max(...ranking.map(item => Number(item.score || 0)), 1);
            return `
                <div class="support-cloud-live-ranking-panel">
                    <div class="support-cloud-live-ranking-title">Ranking de aprendizaje</div>
                    <p>Respondieron ${escapeHtml(numberFormatter.format(answered))} de ${escapeHtml(numberFormatter.format(registered))} registrados.</p>
                    <ol class="support-cloud-live-ranking support-cloud-live-ranking--show">
                        ${ranking.slice(0, 5).map((item, index) => {
                            const score = Number(item.score || 0);
                            const percent = Math.max(4, Math.round((score * 100) / topScore));
                            return `
                                <li class="is-rank-${index + 1}">
                                    <span>${index + 1}</span>
                                    <strong>${escapeHtml(item.fullName || "Participante")}</strong>
                                    <div class="support-cloud-live-ranking-bar"><i style="width:${percent}%"></i></div>
                                    <em>${escapeHtml(numberFormatter.format(score))} pts</em>
                                </li>
                            `;
                        }).join("")}
                    </ol>
                </div>
            `;
        }

        function showLiveWinners(liveState) {
            setLiveScreen("winners", true);
            const bubble = addBot("");
            bubble.classList.add("support-cloud-live-msg--podium");
            const winners = document.createElement("div");
            winners.innerHTML = renderLivePublicWinners(liveState);
            bubble.appendChild(winners);
            scrollDown();
        }

        function renderLivePublicWinners(liveState) {
            const winners = resolveLivePublicWinners(liveState);
            if (!winners.length) {
                return `<span class="support-cloud-placeholder support-cloud-placeholder--compact">Aun no hay ganadores para mostrar.</span>`;
            }

            return `
                <div class="support-cloud-live-winners">
                    <div class="support-cloud-live-winners__head">
                        <span>Puesto</span>
                        <span>Participante</span>
                        <span>Puntos</span>
                    </div>
                    ${winners.map(item => `
                        <div class="support-cloud-live-winners__row is-rank-${item.rank}">
                            <span>${item.rankLabel}</span>
                            <strong>${escapeHtml(item.fullName || "Participante")}</strong>
                            <em>${escapeHtml(numberFormatter.format(Number(item.score || 0)))} pts</em>
                        </div>
                    `).join("")}
                </div>
            `;
        }

        function resolveLivePublicWinners(liveState) {
            const ranking = Array.isArray(liveState.ranking) ? liveState.ranking : [];
            return ranking.slice(0, 3)
                .map((item, index) => ({
                    ...item,
                    rank: index + 1,
                    rankLabel: index === 0 ? "1er" : (index === 1 ? "2do" : "3er")
                }))
                .reverse();
        }

        function renderLiveWheelRanking(liveState) {
            if (!els.wheelRanking || !liveState) {
                return;
            }

            const ranking = Array.isArray(liveState.wheelRanking) ? liveState.wheelRanking : [];
            els.wheelRanking.innerHTML = ranking.length
                ? `
                    <div class="support-cloud-live-ranking-title">Ranking de ruleta</div>
                    <ol class="support-cloud-live-wheel-board">
                        ${ranking.map((item, index) => `
                            <li class="${String(item.participantKey || "") === state.participantKey ? "is-current" : ""}">
                                <span>${index + 1}</span>
                                <strong>${escapeHtml(item.fullName || "Participante")}</strong>
                                <em>${escapeHtml(numberFormatter.format(Number(item.number || 0)))}</em>
                            </li>
                        `).join("")}
                    </ol>
                `
                : "";
        }

        function askSatisfactionQuestion() {
            const next = (payload.satisfactionQuestions || []).find(question => !state.answers.has(question.questionId || ""));
            if (!next) {
                submitLiveSurvey();
                return;
            }

            const textKey = normalizeText(next.text || "");
            if (textKey === "nombre completo") {
                state.answers.set(next.questionId, { questionId: next.questionId, textValue: state.participant.fullName || "" });
                persistLiveSnapshot();
                askSatisfactionQuestion();
                return;
            }
            if (textKey === "empresa") {
                state.answers.set(next.questionId, { questionId: next.questionId, textValue: state.participant.company || "" });
                persistLiveSnapshot();
                askSatisfactionQuestion();
                return;
            }

            setLiveScreen("survey", true);
            const inputType = Number(next.inputTypeValue || 0);
            const bubble = addBot(`*${next.text || "Pregunta"}*`);
            if (inputType === surveyInputRating) {
                renderRatingOptions(bubble, next, value => {
                    state.answers.set(next.questionId, { questionId: next.questionId, numericValue: value });
                    addUser(`${value}`);
                    persistLiveSnapshot();
                    askSatisfactionQuestion();
                });
                return;
            }
            if (inputType === surveyInputSingleChoice && Array.isArray(next.options) && next.options.length) {
                const options = document.createElement("div");
                options.className = "support-cloud-live-options support-cloud-live-options--quiz";
                next.options.forEach((option, optionIndex) => {
                    const button = createLiveAnswerButton(option, optionIndex);
                    button.addEventListener("click", () => {
                        state.answers.set(next.questionId, { questionId: next.questionId, optionId: option.optionId || "" });
                        addUser(option.text || "Opcion");
                        persistLiveSnapshot();
                        askSatisfactionQuestion();
                    });
                    options.appendChild(button);
                });
                bubble.appendChild(options);
                scrollDown();
                return;
            }

            state.currentTextQuestion = next;
            state.mode = "satisfactionText";
            state.activeInputPrompt = next.text || "Pregunta";
            showPanel("input");
            if (els.input) {
                els.input.placeholder = "Escribe aqui...";
                els.input.focus();
            }
        }

        function renderRatingOptions(bubble, question, onSelect) {
            const options = document.createElement("div");
            options.className = "support-cloud-live-options support-cloud-live-options--rating";
            for (let rating = 1; rating <= 5; rating++) {
                const button = document.createElement("button");
                button.type = "button";
                button.className = `support-cloud-live-option support-cloud-live-option--rating support-cloud-live-option--rating-star support-cloud-live-option--tone-${((rating - 1) % 4) + 1}`;
                button.setAttribute("aria-label", `${rating} de 5`);
                button.innerHTML = `<span class="support-cloud-live-rating-star">${numberFormatter.format(rating)}</span>`;
                button.addEventListener("click", () => {
                    Array.from(options.querySelectorAll(".support-cloud-live-option")).forEach(item => item.classList.add("is-disabled"));
                    button.classList.add("is-selected");
                    onSelect(rating, question);
                });
                options.appendChild(button);
            }
            bubble.appendChild(options);
            scrollDown();
        }

        async function submitLiveSurvey() {
            if (state.savingFinalSurvey || state.submittedFinalSurvey) {
                return;
            }

            state.savingFinalSurvey = true;
            state.mode = "saving";
            state.currentTextQuestion = null;
            state.activeInputPrompt = "";
            setLiveScreen("saving", true);
            showPanel("none");
            addBot("Guardando tus respuestas...");
            try {
                const result = await fetchJson(payload.submitUrl || "", {
                    method: "POST",
                    timeoutMs: 15000,
                    body: JSON.stringify({
                        code: payload.code || "",
                        participantKey: state.participantKey || "",
                        fullName: state.participant.fullName || "",
                        email: state.participant.email || "",
                        company: state.participant.company || "",
                        answers: Array.from(state.answers.values())
                    })
                });
                state.score = Number(result?.score ?? state.score);
                state.maxScore = Math.max(
                    Number(result?.maxScore ?? 0),
                    Number(state.maxScore || 0),
                    Number(state.scoreMaxQuestions || 0) * 10
                );
                updateLiveScore();
                try {
                    const liveCompleteState = await fetchJson(payload.liveCompleteUrl || "", {
                        method: "POST",
                        timeoutMs: 8000,
                        body: JSON.stringify({
                            participantKey: state.participantKey,
                            fullName: state.participant.fullName || "",
                            email: state.participant.email || ""
                        })
                    });
                    restoreParticipantProgress(liveCompleteState?.participantProgress);
                } catch {
                    // La respuesta ya quedo guardada; este contador live es solo informativo.
                }
                showAfterFinalSurvey(result?.message || "Respuestas guardadas.");
            } catch (error) {
                try {
                    const liveCompleteState = await fetchJson(payload.liveCompleteUrl || "", {
                        method: "POST",
                        timeoutMs: 8000,
                        body: JSON.stringify({
                            participantKey: state.participantKey,
                            fullName: state.participant.fullName || "",
                            email: state.participant.email || ""
                        })
                    });
                    restoreParticipantProgress(liveCompleteState?.participantProgress);
                } catch {
                }
                showAfterFinalSurvey("Gracias por completar la encuesta final.");
            }
        }

        function showAfterFinalSurvey(message) {
            if (liveWheelEnabled) {
                showWheelAfterFinalSurvey(message);
            } else {
                showFinalThanksAfterSurvey(message);
            }
        }

        function showWheelAfterFinalSurvey(message) {
            state.submittedFinalSurvey = true;
            state.savingFinalSurvey = false;
            state.mode = "wheel";
            state.currentTextQuestion = null;
            state.activeInputPrompt = "";
            persistLiveSnapshot();
            setLiveScreen("wheel", true);
            addBot(message || "Respuestas guardadas.");
            addBot("Gracias por participar. Gira la ruleta para descubrir tu numero de la suerte.");
            showPanel("wheel");
        }

        function showFinalThanksAfterSurvey(message) {
            if (state.shownClosed) {
                return;
            }

            state.submittedFinalSurvey = true;
            state.savingFinalSurvey = false;
            state.shownClosed = true;
            state.mode = "closed";
            state.currentTextQuestion = null;
            state.activeInputPrompt = "";
            persistLiveSnapshot();
            stopLiveQuestionTimer();
            stopLivePolling();
            setLiveScreen("closed", true);
            showPanel("none");
            addBot(message || "Respuestas guardadas.");
            addBot(renderLiveFinalThanks(null));
        }

        function submitLiveText() {
            const value = (els.input?.value || "").trim();
            if (!value) {
                return;
            }

            if (state.mode === "registration") {
                const field = registrationFields[state.registrationStep];
                if (field?.key === "email") {
                    const validation = validateCorporateEmail(value);
                    if (!validation.ok) {
                        addFeedback(false, validation.message);
                        if (els.input) {
                            els.input.focus();
                        }
                        return;
                    }
                }
            }

            addUser(value);
            if (els.input) {
                els.input.value = "";
                els.input.style.height = "48px";
            }

            if (state.mode === "registration") {
                const field = registrationFields[state.registrationStep];
                if (field) {
                    state.participant[field.key] = value;
                    state.registrationStep++;
                    persistLiveSnapshot();
                }
                if (state.registrationStep < registrationFields.length) {
                    askRegistration();
                } else {
                    state.activeInputPrompt = "";
                    registerParticipant();
                }
                return;
            }

            if ((state.mode === "questionText" || state.mode === "satisfactionText") && state.currentTextQuestion) {
                const question = state.currentTextQuestion;
                state.answers.set(question.questionId, {
                    questionId: question.questionId,
                    textValue: value
                });
                if (state.mode === "questionText") {
                    state.answeredQuestionIds.add(question.questionId);
                    state.scoreMaxQuestions = Math.max(
                        Number(state.scoreMaxQuestions || 0),
                        Number(state.currentQuestionIndex || 0) + 1,
                        state.answeredQuestionIds.size
                    );
                    state.maxScore = Math.max(Number(state.maxScore || 0), Number(state.scoreMaxQuestions || 0) * 10);
                    submitLiveAnswer(question, { textValue: value });
                    addFeedback(true, "Respuesta registrada.");
                    updateLiveScore();
                    state.activeInputPrompt = "";
                    showPanel("none");
                    persistLiveSnapshot();
                } else {
                    persistLiveSnapshot();
                    askSatisfactionQuestion();
                }
                state.currentTextQuestion = null;
            }
        }

        function validateCorporateEmail(value) {
            const email = String(value || "").trim().toLowerCase();
            const personalDomains = new Set([
                "gmail.com",
                "googlemail.com",
                "hotmail.com",
                "hotmail.es",
                "outlook.com",
                "outlook.es",
                "live.com",
                "live.com.co",
                "msn.com",
                "yahoo.com",
                "yahoo.es",
                "icloud.com",
                "me.com",
                "aol.com",
                "proton.me",
                "protonmail.com"
            ]);
            const validFormat = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(email);
            if (!validFormat) {
                return { ok: false, message: "Ingresa un correo valido, por ejemplo nombre@empresa.com." };
            }

            const domain = email.split("@").pop() || "";
            if (personalDomains.has(domain)) {
                return { ok: false, message: "Usa tu correo corporativo. No se permiten correos personales como Gmail, Hotmail u Outlook." };
            }

            return { ok: true, message: "" };
        }

        function showClosedResults() {
            setLiveScreen("closed", true);
            showPanel("none");
            addBot("*Gracias por participar.*\n\nEsperamos que sigas participando en nuestras sesiones y que visites las redes sociales de Digital Tech para conocer proximas actividades.");
            const leaders = Array.isArray(payload.leaderboard) ? payload.leaderboard : [];
            if (leaders.length) {
                addBot(`Resultados:
${leaders.map((item, index) => `${index + 1}. ${item.fullName || "Participante"} - ${percentFormatter.format(Number(item.scorePercent || 0))}%`).join("\n")}`);
            }
        }

        async function spinLiveWheel() {
            if (state.hasSpun) {
                await loadLiveParticipantState();
                return;
            }

            setText(els.result, "Girando...");
            if (els.spin) {
                els.spin.disabled = true;
            }
            if (els.wheel) {
                els.wheel.classList.add("is-spinning");
            }
            let number = 0;
            let liveState = null;
            try {
                const result = await fetchJson(payload.liveWheelUrl || "", {
                    method: "POST",
                    body: JSON.stringify({
                        participantKey: state.participantKey,
                        fullName: state.participant.fullName || "",
                        email: state.participant.email || ""
                    })
                });
                number = Number(result?.number || 0);
                liveState = result?.state || null;
            } catch (error) {
                if (els.wheel) {
                    els.wheel.classList.remove("is-spinning");
                }
                if (els.spin) {
                    els.spin.disabled = false;
                }
                addFeedback(false, buildErrorMessage(error));
                return;
            }

            state.hasSpun = true;
            state.wheelNumber = number;
            persistLiveSnapshot();
            const rotation = (360 * 4) + (number * 3.6);
            if (els.wheel) {
                els.wheel.classList.remove("is-spinning");
                void els.wheel.offsetWidth;
                els.wheel.style.transform = `rotate(${rotation}deg)`;
            }
            window.setTimeout(() => {
                setText(els.result, `${state.participant.fullName || "Tu"} numero es ${number}`);
                renderLiveWheelRanking(liveState);
                loadLiveParticipantState();
            }, 3200);
        }

        function showLiveTopbar(liveState) {
            if (els.topbar) {
                els.topbar.hidden = false;
            }
            if (liveState) {
                setLivePhase(liveState.phaseLabel || "Registro", liveState.message || "");
            }
        }

        function setLivePhase(progress, phase) {
            setText(els.progress, progress || "");
            setText(els.phase, phase || "");
        }

        function setLiveScreen(screen, clearMessages = false) {
            root.dataset.liveScreen = screen || "";
            if (clearMessages && els.messages) {
                els.messages.innerHTML = "";
            }
        }

        function showPanel(which) {
            if (els.bottom) {
                els.bottom.hidden = which === "none";
            }
            if (els.inputPanel) {
                els.inputPanel.hidden = which !== "input";
            }
            updateInputContext(which === "input");
            if (els.wheelPanel) {
                els.wheelPanel.hidden = which !== "wheel";
            }
            if (which === "wheel" && state.wheelNumber > 0) {
                if (els.spin) {
                    els.spin.disabled = true;
                }
                setText(els.result, `${state.participant.fullName || "Tu"} numero es ${state.wheelNumber}`);
            }
        }

        function updateInputContext(forceVisible = false) {
            if (!els.inputContext) {
                return;
            }

            const prompt = (state.activeInputPrompt || "").trim();
            const shouldShow = Boolean(prompt) && (forceVisible || root.classList.contains("is-keyboard-entry"));
            els.inputContext.hidden = !shouldShow;
            if (shouldShow) {
                els.inputContext.textContent = prompt;
            }
        }

        function updateLiveScore() {
            if (els.score) {
                els.score.hidden = false;
            }
            const displayedMaxScore = Math.max(Number(state.maxScore || 0), Number(state.scoreMaxQuestions || 0) * 10);
            const display = displayedMaxScore > 0
                ? `${numberFormatter.format(state.score)}/${numberFormatter.format(displayedMaxScore)} pts`
                : `${numberFormatter.format(state.score)} pts`;
            setText(els.scoreValue, display);
        }

        function addBot(text) {
            const bubble = document.createElement("div");
            bubble.className = "support-cloud-live-msg support-cloud-live-msg--bot";
            bubble.innerHTML = formatLiveText(text);
            els.messages?.appendChild(bubble);
            scrollDown();
            return bubble;
        }

        function addUser(text) {
            const bubble = document.createElement("div");
            bubble.className = "support-cloud-live-msg support-cloud-live-msg--user";
            bubble.textContent = text;
            els.messages?.appendChild(bubble);
            scrollDown();
        }

        function addFeedback(ok, text) {
            const bubble = document.createElement("div");
            bubble.className = `support-cloud-live-msg ${ok ? "support-cloud-live-msg--ok" : "support-cloud-live-msg--bad"}`;
            bubble.innerHTML = formatLiveText(text);
            els.messages?.appendChild(bubble);
            scrollDown();
        }

        function formatLiveText(text) {
            return escapeHtml(text || "")
                .replace(/\*(.+?)\*/g, "<strong>$1</strong>")
                .replace(/\n/g, "<br>");
        }

        function scrollDown() {
            if (!els.messages) {
                return;
            }

            window.requestAnimationFrame(() => {
                els.messages.scrollTop = els.messages.scrollHeight;
            });
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
            if (type === "multiple") {
                const selected = Array.from(question.querySelectorAll("input[type='checkbox']:checked"))
                    .map(input => input.value)
                    .filter(Boolean);
                return { questionId, textValue: JSON.stringify(selected) };
            }
            if (type === "matching") {
                const assignments = Array.from(question.querySelectorAll("[data-scs-match-option]"))
                    .map(input => ({
                        optionId: input.dataset.scsMatchOption || "",
                        target: input.value || ""
                    }))
                    .filter(item => item.optionId);
                return { questionId, textValue: JSON.stringify(assignments) };
            }
            if (type === "rating") {
                const checked = question.querySelector("input[type='radio']:checked");
                return {
                    questionId,
                    numericValue: Number(checked?.value || question.querySelector("select")?.value || 0)
                };
            }
            return {
                questionId,
                textValue: question.querySelector("input[type='text'], textarea")?.value || ""
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

        const controller = options.timeoutMs ? new AbortController() : null;
        const timeoutId = controller
            ? window.setTimeout(() => controller.abort(), Number(options.timeoutMs || 0))
            : 0;

        let response;
        try {
            response = await fetch(url, {
                method: options.method || "GET",
                headers,
                body: options.body,
                signal: controller?.signal
            });
        } catch (error) {
            if (error?.name === "AbortError") {
                throw new Error("La solicitud tardo demasiado. Continuaremos con la experiencia live.");
            }
            throw error;
        } finally {
            if (timeoutId) {
                window.clearTimeout(timeoutId);
            }
        }

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

    function parseSurveyMatchingOptionText(value) {
        const parts = String(value || "").split(surveyMatchingSeparator);
        return {
            text: (parts[0] || "").trim(),
            target: (parts.slice(1).join(surveyMatchingSeparator) || "").trim()
        };
    }

    function parseDecimal(value) {
        const parsed = Number.parseFloat(String(value || "").replace(",", "."));
        return Number.isFinite(parsed) ? parsed : 0;
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
