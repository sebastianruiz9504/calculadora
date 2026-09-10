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
        var gesture = null;
        var ignoreClickUntil = 0;

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
            gesture = null;
            pointerInList = false;
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
                element.addEventListener("click", function (event) {
                    // Touch is committed on release, before Android synthesizes
                    // mouse/focus events. A delayed click must not select twice
                    // or turn the end of a scroll into a selection.
                    if (Date.now() < ignoreClickUntil) {
                        event.preventDefault();
                        return;
                    }
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
        function optionFor(target) {
            return optionElements.find(function (element) { return element.contains(target); }) || null;
        }

        function beginGesture(event, point, pointerId) {
            pointerInList = true;
            var element = optionFor(event.target);
            var index = optionElements.indexOf(element);
            gesture = {
                pointerId: pointerId, element: element, index: index, item: visibleItems[index],
                x: point.clientX || 0, y: point.clientY || 0, scrollTop: list.scrollTop, moved: false
            };
            ignoreClickUntil = Date.now() + 1000;
        }

        function moveGesture(point, pointerId) {
            if (!gesture || gesture.pointerId !== pointerId) return;
            if (Math.abs((point.clientX || 0) - gesture.x) > 10
                || Math.abs((point.clientY || 0) - gesture.y) > 10
                || Math.abs(list.scrollTop - gesture.scrollTop) > 2) gesture.moved = true;
        }

        function endGesture(event, point, pointerId, cancelled) {
            if (!gesture || gesture.pointerId !== pointerId) return;
            moveGesture(point, pointerId);
            var ended = gesture;
            gesture = null;
            pointerInList = false;
            ignoreClickUntil = Date.now() + 1000;
            if (!cancelled && !ended.moved && ended.element && ended.element.parentNode === list
                && ended.element.contains(event.target)) {
                event.preventDefault();
                select(ended.index, ended.item);
            }
            // Scrolling leaves choices open. The next outside interaction closes them.
        }

        list.addEventListener("pointerdown", function (event) {
            if (event.pointerType === "mouse") {
                pointerInList = true;
                gesture = null;
                ignoreClickUntil = 0;
                event.preventDefault();
            } else if (event.isPrimary !== false) beginGesture(event, event, event.pointerId);
            else if (gesture) gesture.moved = true;
        });
        doc.addEventListener("pointermove", function (event) { moveGesture(event, event.pointerId); });
        doc.addEventListener("pointerup", function (event) {
            if (event.pointerType === "mouse") pointerInList = false;
            else endGesture(event, event, event.pointerId, false);
        });
        doc.addEventListener("pointercancel", function (event) { endGesture(event, event, event.pointerId, true); });
        if (!global.PointerEvent) {
            list.addEventListener("touchstart", function (event) {
                if (event.touches.length === 1) beginGesture(event, event.touches[0], event.touches[0].identifier);
                else { gesture = null; pointerInList = false; }
            }, { passive: true });
            doc.addEventListener("touchmove", function (event) {
                if (event.touches.length === 1) moveGesture(event.touches[0], event.touches[0].identifier);
                else if (gesture) gesture.moved = true;
            }, { passive: true });
            doc.addEventListener("touchend", function (event) {
                Array.from(event.changedTouches).forEach(function (point) { endGesture(event, point, point.identifier, false); });
            }, { passive: false });
            doc.addEventListener("touchcancel", function (event) {
                Array.from(event.changedTouches).forEach(function (point) { endGesture(event, point, point.identifier, true); });
            });
        }
        doc.addEventListener("pointerdown", function (event) {
            if (event.target !== input && !list.contains(event.target)) {
                pointerInList = false;
                close();
            }
        });
        doc.addEventListener("focusin", function (event) {
            if (!pointerInList && event.target !== input && !list.contains(event.target)) close();
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
