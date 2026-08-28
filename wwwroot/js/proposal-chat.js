(function () {
    const app = document.querySelector("[data-proposal-chat-app]");
    if (!app) return;

    const chatUrl = app.dataset.chatUrl || "/ProposalChat/Chat";
    const exportPdfUrl = app.dataset.exportPdfUrl || "/ProposalChat/ExportPdf";
    const exportWordUrl = app.dataset.exportWordUrl || "/ProposalChat/ExportWord";
    const form = app.querySelector("[data-proposal-form]");
    const input = app.querySelector("[data-proposal-input]");
    const messagesEl = app.querySelector("[data-proposal-messages]");
    const statusEl = app.querySelector("[data-proposal-status]");
    const sendBtn = app.querySelector("[data-proposal-send]");
    const clearBtn = app.querySelector("[data-proposal-clear]");
    const downloadPdfBtn = app.querySelector("[data-proposal-download-pdf]");
    const downloadWordBtn = app.querySelector("[data-proposal-download-word]");
    const previewFrame = app.querySelector("[data-proposal-preview]");
    const previewEmpty = app.querySelector("[data-proposal-preview-empty]");
    const previewTitle = app.querySelector("[data-proposal-preview-title]");
    const promptButtons = app.querySelectorAll("[data-proposal-prompt]");

    let history = [];
    let currentDocumentTitle = "";
    let currentDocumentHtml = "";
    let currentDocumentText = "";

    function setStatus(message) {
        if (!statusEl) return;
        statusEl.textContent = message || "";
    }

    function appendMessage(role, content, questions = []) {
        const article = document.createElement("article");
        article.className = `proposal-chat-message proposal-chat-message--${role}`;

        const roleEl = document.createElement("div");
        roleEl.className = "proposal-chat-message__role";
        roleEl.textContent = role === "assistant" ? "Agente" : "Tu";

        const bubble = document.createElement("div");
        bubble.className = "proposal-chat-message__bubble";
        if (questions.length > 0) {
            const title = document.createElement("div");
            title.textContent = content || "Preguntas pendientes:";
            bubble.appendChild(title);

            const list = document.createElement("ul");
            list.className = "proposal-chat-question-list";
            questions.forEach((question) => {
                const item = document.createElement("li");
                item.textContent = question;
                list.appendChild(item);
            });
            bubble.appendChild(list);
        } else {
            bubble.textContent = content;
        }

        article.appendChild(roleEl);
        article.appendChild(bubble);
        messagesEl.appendChild(article);
        messagesEl.scrollTop = messagesEl.scrollHeight;
    }

    function syncDownloadButtons() {
        const hasDocument = Boolean(currentDocumentHtml || currentDocumentText);
        if (downloadPdfBtn) downloadPdfBtn.disabled = !hasDocument;
        if (downloadWordBtn) downloadWordBtn.disabled = !hasDocument;
    }

    function escapeHtml(value) {
        return (value || "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;");
    }

    function textToPreviewHtml(text) {
        const safe = escapeHtml(text || "").replaceAll("\n", "<br>");
        return `<section class="proposal-page">${safe}</section>`;
    }

    function stripHtmlShell(html) {
        const value = html || "";
        const bodyMatch = value.match(/<body[^>]*>([\s\S]*?)<\/body>/i);
        return bodyMatch ? bodyMatch[1] : value;
    }

    function buildPreviewHtml(dynamicHtml) {
        const content = stripHtmlShell(dynamicHtml || "");
        return `<!DOCTYPE html>
<html lang="es">
<head>
  <meta charset="utf-8">
  <style>
    body{margin:0;background:#e9eef5;font-family:Arial,sans-serif;color:#102033;line-height:1.5}
    .proposal-static-page,.proposal-page{box-sizing:border-box;width:min(820px,calc(100% - 32px));margin:18px auto;background:#fff;box-shadow:0 14px 34px rgba(15,31,54,.12);overflow:hidden}
    .proposal-static-page{aspect-ratio:1191/1685}
    .proposal-static-page img{display:block;width:100%;height:100%;object-fit:contain}
    .proposal-page{min-height:1160px;padding:48px}
    h1,h2,h3{color:#061943}
    table{width:100%;border-collapse:collapse}
    th,td{border:1px solid #d9e2ee;padding:8px;text-align:left}
    th{background:#061943;color:#fff}
  </style>
</head>
<body>
  <section class="proposal-static-page"><img src="/img/proposals/proposal-cover.png" alt="Portada propuesta comercial"></section>
  <section class="proposal-static-page"><img src="/img/proposals/proposal-about.png" alt="Sobre Digital Tech"></section>
  ${content}
</body>
</html>`;
    }

    function renderPreview() {
        const html = currentDocumentHtml
            ? buildPreviewHtml(currentDocumentHtml)
            : (currentDocumentText ? buildPreviewHtml(textToPreviewHtml(currentDocumentText)) : "");
        if (previewTitle) previewTitle.textContent = currentDocumentTitle || (html ? "Propuesta comercial" : "Sin documento");
        if (previewFrame) previewFrame.srcdoc = html;
        if (previewEmpty) previewEmpty.hidden = Boolean(html);
    }

    function resetChat() {
        history = [];
        currentDocumentTitle = "";
        currentDocumentHtml = "";
        currentDocumentText = "";
        syncDownloadButtons();
        renderPreview();
        messagesEl.innerHTML = "";
        appendMessage("assistant", "Listo. Genero el preview y solo te muestro preguntas pendientes.");
        setStatus("");
        input.focus();
    }

    async function readErrorResponseMessage(response) {
        const text = await response.text();
        if (!text) return "No fue posible responder con el agente.";

        try {
            const json = JSON.parse(text);
            return json?.message || json?.detail || text;
        } catch {
            return text;
        }
    }

    async function sendMessage(message) {
        appendMessage("user", currentDocumentHtml || currentDocumentText ? "Corrección enviada." : "Contenido enviado.");
        setStatus("Generando preview...");
        sendBtn.disabled = true;
        input.disabled = true;

        try {
            const response = await fetch(chatUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json", "Accept": "application/json" },
                body: JSON.stringify({
                    message,
                    history,
                    currentDocumentTitle,
                    currentDocumentHtml,
                    currentDocumentText
                })
            });

            if (!response.ok) {
                throw new Error(await readErrorResponseMessage(response));
            }

            const data = await response.json();
            const answer = (data?.answer || "").trim() || "Preview actualizado.";
            const questions = Array.isArray(data?.pendingQuestions)
                ? data.pendingQuestions.filter(Boolean)
                : [];

            currentDocumentTitle = (data?.documentTitle || currentDocumentTitle || "Propuesta comercial").trim();
            currentDocumentHtml = (data?.documentHtml || currentDocumentHtml || "").trim();
            currentDocumentText = (data?.documentText || currentDocumentText || "").trim();
            renderPreview();
            syncDownloadButtons();
            appendMessage("assistant", questions.length ? "Preguntas pendientes:" : answer, questions);

            history.push({ role: "user", content: message });
            history.push({
                role: "assistant",
                content: questions.length
                    ? `Preguntas pendientes: ${questions.join(" | ")}`
                    : answer
            });
            history = history.slice(-12);
            setStatus("");
        } catch (error) {
            const messageText = error?.message || "No fue posible responder con el agente.";
            appendMessage("assistant", messageText);
            setStatus("");
        } finally {
            sendBtn.disabled = false;
            input.disabled = false;
            input.focus();
        }
    }

    async function downloadDocument(url, fallbackExtension) {
        if (!currentDocumentHtml && !currentDocumentText) return;

        setStatus("Preparando descarga...");
        const response = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                documentTitle: currentDocumentTitle,
                documentHtml: currentDocumentHtml,
                documentText: currentDocumentText
            })
        });

        if (!response.ok) {
            throw new Error(await readErrorResponseMessage(response));
        }

        const blob = await response.blob();
        const objectUrl = window.URL.createObjectURL(blob);
        const link = document.createElement("a");
        const contentDisposition = response.headers.get("content-disposition") || "";
        const match = contentDisposition.match(/filename="?([^";]+)"?/i);
        link.href = objectUrl;
        link.download = match?.[1] || `propuesta-${new Date().toISOString().slice(0, 10)}.${fallbackExtension}`;
        document.body.appendChild(link);
        link.click();
        link.remove();
        window.URL.revokeObjectURL(objectUrl);
        setStatus("");
    }

    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        const message = (input.value || "").trim();
        if (!message) {
            input.focus();
            return;
        }

        input.value = "";
        await sendMessage(message);
    });

    promptButtons.forEach((button) => {
        button.addEventListener("click", () => {
            const prompt = button.dataset.proposalPrompt || "";
            input.value = prompt;
            input.focus();
            input.setSelectionRange(input.value.length, input.value.length);
        });
    });

    if (clearBtn) {
        clearBtn.addEventListener("click", resetChat);
    }

    if (downloadPdfBtn) {
        downloadPdfBtn.addEventListener("click", async () => {
            try {
                await downloadDocument(exportPdfUrl, "pdf");
            } catch (error) {
                appendMessage("assistant", error?.message || "No fue posible descargar el PDF.");
                setStatus("");
            }
        });
    }

    if (downloadWordBtn) {
        downloadWordBtn.addEventListener("click", async () => {
            try {
                await downloadDocument(exportWordUrl, "doc");
            } catch (error) {
                appendMessage("assistant", error?.message || "No fue posible descargar Word.");
                setStatus("");
            }
        });
    }

    renderPreview();
    syncDownloadButtons();
})();
