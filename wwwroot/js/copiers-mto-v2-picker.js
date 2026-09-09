(function (global) {
    "use strict";

    var nextId = 0;
    var maxVisible = 50;

    function searchText(value) {
        return String(value || "").normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLocaleLowerCase("es").trim();
    }

    function create(input, list, options) {
        if (!input || !list) throw new Error("El selector necesita un campo y una lista.");
        options = options || {};
        var doc = input.ownerDocument;
        var listId = list.id || "mto-v2-picker-list-" + (++nextId);
        var items = [];
        var visibleItems = [];
        var optionElements = [];
        var activeIndex = -1;
        var opened = false;
        var status = "";
        var pointerInList = false;

        list.id = listId;
        list.classList.add("mto-v2-picker-list");
        list.setAttribute("role", "listbox");
        list.setAttribute("aria-label", input.getAttribute("aria-label") || "Opciones disponibles");
        input.removeAttribute("list");
        input.setAttribute("role", "combobox");
        input.setAttribute("aria-autocomplete", "list");
        input.setAttribute("aria-haspopup", "listbox");
        input.setAttribute("aria-controls", listId);
        input.setAttribute("autocomplete", "off");
        close();

        function close() {
            opened = false;
            activeIndex = -1;
            list.hidden = true;
            input.setAttribute("aria-expanded", "false");
            input.removeAttribute("aria-activedescendant");
            optionElements.forEach(function (element) { element.setAttribute("aria-selected", "false"); });
        }

        function addStatus(message) {
            var element = doc.createElement("div");
            element.className = "mto-v2-picker-status";
            element.setAttribute("role", "option");
            element.setAttribute("aria-disabled", "true");
            element.setAttribute("aria-live", "polite");
            element.textContent = message;
            list.appendChild(element);
        }

        function render() {
            if (typeof list.replaceChildren === "function") list.replaceChildren();
            else while (list.firstChild) list.removeChild(list.firstChild);
            optionElements = [];
            visibleItems = [];
            activeIndex = -1;
            input.removeAttribute("aria-activedescendant");
            if (status) {
                addStatus(status);
                return;
            }
            var query = searchText(input.value);
            var matches = items.filter(function (item) {
                return searchText(item.label + " " + (item.description || "")).includes(query);
            });
            visibleItems = matches.slice(0, maxVisible);
            visibleItems.forEach(function (item, index) {
                var element = doc.createElement("div");
                element.id = listId + "-option-" + index;
                element.className = "mto-v2-picker-item";
                element.setAttribute("role", "option");
                element.setAttribute("aria-selected", "false");
                element.setAttribute("data-picker-id", String(item.id));
                var label = doc.createElement("span");
                label.className = "mto-v2-picker-item__label";
                label.textContent = item.label;
                element.appendChild(label);
                if (item.description) {
                    var description = doc.createElement("span");
                    description.className = "mto-v2-picker-item__description";
                    description.textContent = item.description;
                    element.appendChild(description);
                }
                element.addEventListener("click", function () {
                    if (element.parentNode === list) select(index, item);
                });
                list.appendChild(element);
                optionElements.push(element);
            });
            if (matches.length === 0) addStatus(query ? "No hay coincidencias. Prueba con otro nombre o serial." : "No hay opciones disponibles.");
            else if (matches.length > maxVisible) addStatus("Se muestran las primeras " + maxVisible + " de " + matches.length + " opciones. Escribe más para refinar la búsqueda.");
        }

        function open() {
            if (input.disabled || input.readOnly) return;
            opened = true;
            render();
            list.hidden = false;
            input.setAttribute("aria-expanded", "true");
        }

        function select(index, expectedItem) {
            if (!opened || input.disabled || input.readOnly || !visibleItems[index]) return;
            if (expectedItem && visibleItems[index] !== expectedItem) return;
            var item = visibleItems[index];
            pointerInList = false;
            // Keep keyboard focus on the combobox, then let the caller update its
            // selected record and value. No synthetic input/change events are sent.
            input.focus({ preventScroll: true });
            close();
            if (typeof options.onSelect === "function") options.onSelect(item);
        }

        function activate(index) {
            if (!visibleItems.length) return;
            activeIndex = Math.max(0, Math.min(index, visibleItems.length - 1));
            optionElements.forEach(function (element, optionIndex) {
                element.setAttribute("aria-selected", optionIndex === activeIndex ? "true" : "false");
            });
            var activeElement = optionElements[activeIndex];
            input.setAttribute("aria-activedescendant", activeElement.id);
            // Scroll only the list, never the whole form behind the software keyboard.
            var top = activeElement.offsetTop;
            var bottom = top + activeElement.offsetHeight;
            if (top < list.scrollTop) list.scrollTop = top;
            else if (bottom > list.scrollTop + list.clientHeight) list.scrollTop = bottom - list.clientHeight;
        }

        input.addEventListener("focus", open);
        input.addEventListener("click", function () { if (!opened) open(); });
        input.addEventListener("input", open);
        input.addEventListener("keydown", function (event) {
            if (event.isComposing || event.keyCode === 229) return;
            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                if (input.disabled || input.readOnly) return;
                event.preventDefault();
                if (!opened) open();
                var next = activeIndex < 0 ? (event.key === "ArrowDown" ? 0 : visibleItems.length - 1) : activeIndex + (event.key === "ArrowDown" ? 1 : -1);
                activate(next);
            } else if (event.key === "Enter" && opened) {
                event.preventDefault();
                if (activeIndex >= 0) select(activeIndex);
            } else if (event.key === "Escape" && opened) {
                event.preventDefault();
                close();
            } else if (event.key === "Tab") close();
        });
        input.addEventListener("blur", function () {
            if (!pointerInList) close();
        });
        list.addEventListener("pointerdown", function (event) {
            pointerInList = true;
            // A mouse must not blur the input before its click. Touch keeps its
            // native scrolling; the pointer guard retains the list until the tap.
            if (event.pointerType === "mouse") event.preventDefault();
        });
        function releasePointer() {
            pointerInList = false;
            // A touch scroll may cancel its pointer after blurring the input.
            // Keep the choices visible; the next outside interaction closes them.
        }
        doc.addEventListener("pointerup", releasePointer);
        doc.addEventListener("pointercancel", releasePointer);
        doc.addEventListener("pointerdown", function (event) {
            if (event.target !== input && !list.contains(event.target)) {
                pointerInList = false;
                close();
            }
        });
        doc.addEventListener("focusin", function (event) {
            if (event.target !== input && !list.contains(event.target)) close();
        });

        return {
            setItems: function (newItems) {
                items = Array.isArray(newItems) ? newItems.filter(function (item) { return item && item.id !== null && item.id !== undefined && typeof item.label === "string" && item.label.trim(); }) : [];
                status = "";
                if (opened) render();
            },
            setStatus: function (message) {
                status = String(message || "");
                if (opened) render();
            },
            close: close
        };
    }

    global.CopiersMtoV2Picker = Object.freeze({ create: create });
})(window);
