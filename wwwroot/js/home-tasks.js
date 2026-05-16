(function () {
    const createModal = document.querySelector("[data-task-create-modal]");
    const closeModal = document.querySelector("[data-task-close-modal]");
    const createForm = document.querySelector("[data-task-create-form]");
    const closeForm = document.querySelector("[data-task-close-form]");
    const userSearch = document.querySelector("[data-task-user-search]");
    const userMenu = document.querySelector("[data-task-user-menu]");
    const userIdInput = document.querySelector("[data-task-user-id]");
    const userEmailInput = document.querySelector("[data-task-user-email]");
    const dueDateInput = document.querySelector("[data-task-due-date]");
    const descriptionInput = document.querySelector("[data-task-description]");
    const createStatus = document.querySelector("[data-task-create-status]");
    const closeStatus = document.querySelector("[data-task-close-status]");
    let searchTimer = null;

    function openModal(modal) {
        if (!modal) return;
        modal.hidden = false;
    }

    function closeModals() {
        if (createModal) createModal.hidden = true;
        if (closeModal) closeModal.hidden = true;
    }

    function setStatus(element, tone, message) {
        if (!element) return;
        element.dataset.tone = tone || "";
        element.textContent = message || "";
    }

    async function readResponse(response) {
        const text = await response.text();
        let payload = null;
        if (text) {
            try {
                payload = JSON.parse(text);
            } catch {
                payload = { message: text };
            }
        }

        if (!response.ok) {
            throw new Error(payload?.message || "No fue posible completar la accion.");
        }

        return payload;
    }

    function renderUserMenu(users) {
        if (!userMenu) return;
        userMenu.innerHTML = "";
        if (!users || users.length === 0) {
            userMenu.innerHTML = "<div class=\"home-lookup-option\">Sin resultados</div>";
            userMenu.hidden = false;
            return;
        }

        users.forEach((user) => {
            const option = document.createElement("button");
            option.type = "button";
            option.className = "home-lookup-option";
            option.textContent = user.name || user.email || "Usuario";
            option.addEventListener("click", () => {
                if (userIdInput) userIdInput.value = user.id || "";
                if (userEmailInput) userEmailInput.value = user.email || "";
                if (userSearch) userSearch.value = user.name || user.email || "";
                userMenu.hidden = true;
            });
            userMenu.appendChild(option);
        });
        userMenu.hidden = false;
    }

    async function searchUsers(query) {
        const trimmed = (query || "").trim();
        if (trimmed.length < 2) {
            if (userMenu) userMenu.hidden = true;
            return;
        }

        const response = await fetch(`/Tasks/UserSearch?q=${encodeURIComponent(trimmed)}`, {
            headers: { "Accept": "application/json" }
        });
        renderUserMenu(await readResponse(response));
    }

    document.querySelectorAll("[data-task-modal-close]").forEach((button) => {
        button.addEventListener("click", closeModals);
    });

    document.querySelector("[data-task-create-open]")?.addEventListener("click", () => {
        if (createForm) createForm.reset();
        if (userIdInput) userIdInput.value = "";
        if (userMenu) userMenu.hidden = true;
        setStatus(createStatus, "", "");
        openModal(createModal);
        userSearch?.focus();
    });

    document.querySelectorAll("[data-task-close-open]").forEach((button) => {
        button.addEventListener("click", () => {
            const row = button.closest("[data-task-id]");
            if (!row) return;
            const idInput = document.querySelector("[data-task-close-id]");
            const title = document.querySelector("[data-task-close-title]");
            const comments = document.querySelector("[data-task-close-comments]");
            const attachment = document.querySelector("[data-task-close-attachment]");
            if (idInput) idInput.value = row.dataset.taskId || "";
            if (title) title.textContent = row.dataset.taskTitle || "";
            if (comments) comments.value = "";
            if (attachment) attachment.value = "";
            setStatus(closeStatus, "", "");
            openModal(closeModal);
        });
    });

    userSearch?.addEventListener("input", () => {
        if (userIdInput) userIdInput.value = "";
        if (userEmailInput) userEmailInput.value = "";
        window.clearTimeout(searchTimer);
        searchTimer = window.setTimeout(() => {
            searchUsers(userSearch.value).catch((error) => {
                if (userMenu) {
                    userMenu.innerHTML = `<div class="home-lookup-option">${error.message}</div>`;
                    userMenu.hidden = false;
                }
            });
        }, 250);
    });

    createForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        setStatus(createStatus, "", "Creando tarea...");
        try {
            const payload = {
                assigneeId: userIdInput?.value || "",
                assigneeEmail: userEmailInput?.value || "",
                assigneeName: userSearch?.value || "",
                dueDateValue: dueDateInput?.value || "",
                description: descriptionInput?.value || ""
            };
            const response = await fetch("/Tasks/CreateManual", {
                method: "POST",
                headers: {
                    "Accept": "application/json",
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(payload)
            });
            await readResponse(response);
            setStatus(createStatus, "success", "Tarea creada.");
            window.setTimeout(() => window.location.reload(), 500);
        } catch (error) {
            setStatus(createStatus, "error", error.message);
        }
    });

    closeForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        setStatus(closeStatus, "", "Cerrando tarea...");
        try {
            const formData = new FormData();
            formData.append("taskId", document.querySelector("[data-task-close-id]")?.value || "");
            formData.append("comments", document.querySelector("[data-task-close-comments]")?.value || "");
            const attachment = document.querySelector("[data-task-close-attachment]")?.files?.[0];
            if (attachment) formData.append("attachment", attachment);
            const response = await fetch("/Tasks/CloseManual", {
                method: "POST",
                headers: { "Accept": "application/json" },
                body: formData
            });
            await readResponse(response);
            setStatus(closeStatus, "success", "Tarea cerrada.");
            window.setTimeout(() => window.location.reload(), 500);
        } catch (error) {
            setStatus(closeStatus, "error", error.message);
        }
    });
})();
