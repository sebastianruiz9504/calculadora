"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const { test } = require("node:test");
const source = fs.readFileSync(path.join(__dirname, "../wwwroot/js/copiers-mto-v2-picker.js"), "utf8");
const css = fs.readFileSync(path.join(__dirname, "../wwwroot/css/copiers-mto-v2.css"), "utf8");

class Element {
    constructor(tag, doc) {
        this.tagName = tag.toUpperCase();
        this.ownerDocument = doc;
        this.children = [];
        this.attributes = new Map();
        this.listeners = new Map();
        this.className = "";
        this.classList = { add: value => { this.className = [this.className, value].filter(Boolean).join(" "); } };
        this.value = "";
        this.hidden = false;
        this.disabled = false;
        this.readOnly = false;
        this.scrollTop = 0;
        this.clientHeight = 192;
        this.offsetHeight = 48;
    }
    get firstChild() { return this.children[0] || null; }
    get offsetTop() { return this.parentNode ? this.parentNode.children.indexOf(this) * 48 : 0; }
    get textContent() { return (this.text || "") + this.children.map(child => child.textContent).join(""); }
    set textContent(value) { this.text = String(value); this.replaceChildren(); }
    setAttribute(name, value) { this.attributes.set(name, String(value)); }
    getAttribute(name) { return this.attributes.get(name) ?? null; }
    removeAttribute(name) { this.attributes.delete(name); }
    appendChild(child) { child.parentNode = this; this.children.push(child); return child; }
    removeChild(child) { this.children.splice(this.children.indexOf(child), 1); child.parentNode = null; }
    replaceChildren() { this.children.forEach(child => { child.parentNode = null; }); this.children = []; }
    contains(target) { return target === this || this.children.some(child => child.contains(target)); }
    addEventListener(name, listener) {
        if (!this.listeners.has(name)) this.listeners.set(name, []);
        this.listeners.get(name).push(listener);
    }
    fire(type, properties = {}) {
        const event = {
            type, target: this, defaultPrevented: false,
            preventDefault() { this.defaultPrevented = true; }, ...properties
        };
        let current = this;
        do {
            for (const listener of current.listeners.get(type) || []) listener(event);
            current = ["blur", "focus"].includes(type) ? null : current.parentNode;
        } while (current);
        return event;
    }
    focus() {
        const previous = this.ownerDocument.activeElement;
        this.ownerDocument.activeElement = this;
        if (previous && previous !== this) previous.fire("blur", { relatedTarget: this });
        if (previous !== this) this.fire("focus");
        this.fire("focusin");
    }
    blur() { this.ownerDocument.activeElement = null; this.fire("blur"); }
}

function harness({ noListId = false, oldDom = false, touchOnly = false } = {}) {
    const doc = new Element("document");
    doc.ownerDocument = doc;
    doc.createElement = tag => new Element(tag, doc);
    const wrapper = doc.appendChild(doc.createElement("div"));
    const input = wrapper.appendChild(doc.createElement("input"));
    const list = wrapper.appendChild(doc.createElement("div"));
    input.setAttribute("list", "legacy-datalist");
    input.setAttribute("aria-label", "Cliente");
    if (!noListId) list.id = "client-choices";
    if (oldDom) list.replaceChildren = undefined;
    const window = touchOnly ? {} : { PointerEvent: function () {} };
    vm.runInNewContext(source, { window });
    const selected = [];
    const picker = window.CopiersMtoV2Picker.create(input, list, { onSelect: item => selected.push(item) });
    const choices = () => list.children.filter(child => child.className === "mto-v2-picker-item");
    const type = value => { input.value = value; input.fire("input"); };
    const key = (value, props) => input.fire("keydown", { key: value, ...props });
    return { doc, wrapper, input, list, picker, selected, choices, type, key, window };
}

const clients = [
    { id: "client-1", label: "Clínica Muñoz", description: "Sede Bogotá" },
    { id: "client-2", label: "POLLO FIESTA S.A.", description: "NIT 900123" },
    { id: "client-3", label: "Digital Tech", description: "Medellín" }
];

test("initialization owns the combobox instead of native datalist and keeps it closed", () => {
    const h = harness();
    assert.equal(h.input.getAttribute("list"), null);
    assert.equal(h.input.getAttribute("role"), "combobox");
    assert.equal(h.input.getAttribute("aria-controls"), h.list.id);
    assert.equal(h.input.getAttribute("aria-autocomplete"), "list");
    assert.equal(h.input.getAttribute("aria-expanded"), "false");
    assert.equal(h.list.getAttribute("role"), "listbox");
    assert.equal(h.list.getAttribute("aria-label"), "Cliente");
    assert.equal(h.list.hidden, true);
});

test("lists without an existing ID receive unique IDs within the page", () => {
    const h = harness({ noListId: true });
    const nextInput = h.doc.createElement("input");
    const nextList = h.doc.createElement("div");
    h.window.CopiersMtoV2Picker.create(nextInput, nextList);
    assert.ok(h.list.id);
    assert.notEqual(h.list.id, nextList.id);
});

test("focusing an empty field immediately presents available catalog choices", () => {
    const h = harness();
    h.picker.setItems(clients);
    h.input.focus();
    assert.equal(h.list.hidden, false);
    assert.equal(h.input.getAttribute("aria-expanded"), "true");
    assert.equal(h.choices().length, 3);
});

test("filtering is case-insensitive and accent-insensitive with contains matching", () => {
    const h = harness();
    h.picker.setItems(clients);
    h.type("  NICA MUNOZ ");
    assert.equal(h.choices().length, 1);
    assert.equal(h.choices()[0].getAttribute("data-picker-id"), "client-1");
    h.type("fiesta");
    assert.equal(h.choices()[0].getAttribute("data-picker-id"), "client-2");
    h.type("bogota");
    assert.equal(h.choices()[0].getAttribute("data-picker-id"), "client-1");
});

test("async catalog updates render immediately when the field is open", () => {
    const h = harness();
    h.input.focus();
    h.picker.setStatus("Buscando clientes Copiers…");
    assert.match(h.list.textContent, /Buscando/);
    h.picker.setItems(clients);
    assert.equal(h.choices().length, 3);
    assert.doesNotMatch(h.list.textContent, /Buscando/);
});

test("loading and failure messages are safely visible instead of a silent list", () => {
    const h = harness();
    h.picker.setItems(clients);
    h.input.focus();
    h.picker.setStatus("No se pudo cargar. Reintenta.");
    assert.equal(h.choices().length, 0);
    assert.equal(h.list.textContent, "No se pudo cargar. Reintenta.");
    assert.equal(h.list.firstChild.getAttribute("aria-live"), "polite");
    h.picker.setStatus("");
    assert.equal(h.choices().length, 3);
});

test("empty data and unmatched text both give explicit messages", () => {
    const h = harness();
    h.input.focus();
    assert.match(h.list.textContent, /No hay opciones/);
    h.picker.setItems(clients);
    h.type("no existe");
    assert.match(h.list.textContent, /No hay coincidencias/);
});

test("only fifty choices render, and narrowing the search considers the full catalog", () => {
    const h = harness();
    h.picker.setItems(Array.from({ length: 73 }, (_, index) => ({ id: index, label: `Cliente ${index}` })));
    h.input.focus();
    assert.equal(h.choices().length, 50);
    assert.match(h.list.textContent, /primeras 50 de 73/);
    h.type("Cliente 72");
    assert.equal(h.choices().length, 1);
    assert.equal(h.choices()[0].getAttribute("data-picker-id"), "72");
    assert.doesNotMatch(h.list.textContent, /refinar/);
});

test("arrow keys expose active option IDs and Enter selects without synthetic field events", () => {
    const h = harness();
    let inputEvents = 0;
    let changeEvents = 0;
    h.input.addEventListener("input", () => inputEvents++);
    h.input.addEventListener("change", () => changeEvents++);
    h.picker.setItems(clients);
    assert.equal(h.key("ArrowDown").defaultPrevented, true);
    assert.equal(h.input.getAttribute("aria-activedescendant"), "client-choices-option-0");
    h.key("ArrowDown");
    assert.equal(h.choices()[1].getAttribute("aria-selected"), "true");
    assert.equal(h.choices()[0].getAttribute("aria-selected"), "false");
    assert.equal(h.key("Enter").defaultPrevented, true);
    assert.equal(h.selected[0], clients[1]);
    assert.equal(h.input.value, "");
    assert.equal(inputEvents, 0);
    assert.equal(changeEvents, 0);
    assert.equal(h.list.hidden, true);
    assert.equal(h.input.getAttribute("aria-activedescendant"), null);
});

test("ArrowUp from a closed field activates the final option and arrows clamp at bounds", () => {
    const h = harness();
    h.picker.setItems(clients);
    h.key("ArrowUp");
    assert.equal(h.input.getAttribute("aria-activedescendant"), "client-choices-option-2");
    h.key("ArrowDown");
    assert.equal(h.input.getAttribute("aria-activedescendant"), "client-choices-option-2");
    h.key("ArrowUp"); h.key("ArrowUp"); h.key("ArrowUp");
    assert.equal(h.input.getAttribute("aria-activedescendant"), "client-choices-option-0");
});

test("keyboard movement scrolls the list without scrolling the page", () => {
    const h = harness();
    h.picker.setItems(Array.from({ length: 20 }, (_, id) => ({ id, label: String(id) })));
    for (let index = 0; index < 8; index++) h.key("ArrowDown");
    assert.equal(h.list.scrollTop, 192);
    for (let index = 0; index < 7; index++) h.key("ArrowUp");
    assert.equal(h.list.scrollTop, 0);
});

test("Escape closes without selection and Tab preserves native focus traversal", () => {
    const h = harness();
    h.picker.setItems(clients);
    h.key("ArrowDown");
    assert.equal(h.key("Escape").defaultPrevented, true);
    assert.equal(h.list.hidden, true);
    h.key("ArrowDown");
    assert.equal(h.key("Tab").defaultPrevented, false);
    assert.equal(h.list.hidden, true);
    assert.equal(h.selected.length, 0);
});

test("Enter does not accidentally select the first result or submit during IME composition", () => {
    const h = harness();
    h.picker.setItems(clients);
    h.input.focus();
    assert.equal(h.key("Enter").defaultPrevented, true);
    assert.equal(h.selected.length, 0);
    h.key("ArrowDown");
    assert.equal(h.key("Enter", { isComposing: true }).defaultPrevented, false);
    assert.equal(h.selected.length, 0);
});

test("touch selection survives blur before click and returns the original record ID once", () => {
    const h = harness();
    h.picker.setItems(clients);
    h.input.focus();
    const chosen = h.choices()[1];
    assert.equal(chosen.fire("pointerdown", { pointerType: "touch" }).defaultPrevented, false);
    h.input.blur();
    assert.equal(h.list.hidden, false);
    chosen.fire("pointerup", { pointerType: "touch" });
    chosen.fire("click");
    chosen.fire("click");
    assert.equal(h.selected.length, 1);
    assert.equal(h.selected[0].id, "client-2");
    assert.equal(h.list.hidden, true);
});

test("touch scrolling and pointer cancellation do not dismiss the choices prematurely", () => {
    const h = harness();
    h.picker.setItems(clients);
    h.input.focus();
    h.list.fire("pointerdown", { pointerType: "touch" });
    h.input.blur();
    h.list.fire("pointercancel", { pointerType: "touch" });
    assert.equal(h.list.hidden, false);
    assert.equal(h.selected.length, 0);
    h.doc.fire("pointerdown", { pointerType: "touch" });
    assert.equal(h.list.hidden, true);
});

test("mouse press preserves input focus until selecting the choice", () => {
    const h = harness();
    h.picker.setItems(clients);
    h.input.focus();
    const choice = h.choices()[0];
    assert.equal(choice.fire("pointerdown", { pointerType: "mouse" }).defaultPrevented, true);
    choice.fire("pointerup", { pointerType: "mouse" });
    choice.fire("click");
    assert.equal(h.selected[0], clients[0]);
});

test("outside pointer and outside keyboard focus dismiss the list", () => {
    const h = harness();
    h.picker.setItems(clients);
    h.input.focus();
    h.doc.fire("pointerdown");
    assert.equal(h.list.hidden, true);
    h.input.fire("click");
    assert.equal(h.list.hidden, false);
    h.doc.appendChild(h.doc.createElement("button")).focus();
    assert.equal(h.list.hidden, true);
});

test("duplicate labels preserve their distinct record identity", () => {
    const h = harness();
    h.picker.setItems([{ id: "first", label: "Cliente", description: "Sede A" }, { id: "second", label: "Cliente", description: "Sede B" }]);
    h.input.focus();
    h.choices()[1].fire("click");
    assert.equal(h.selected[0].id, "second");
});

test("a stale touch click cannot select a different row after async catalog replacement", () => {
    const h = harness();
    h.picker.setItems(clients);
    h.input.focus();
    const staleChoice = h.choices()[0];
    staleChoice.fire("pointerdown", { pointerType: "touch" });
    h.picker.setItems([{ id: "replacement", label: "Otro cliente" }]);
    staleChoice.fire("click");
    assert.equal(h.selected.length, 0);
    h.choices()[0].fire("pointerdown", { pointerType: "mouse" });
    h.choices()[0].fire("click");
    assert.equal(h.selected[0].id, "replacement");
});

test("catalog strings are text, never interpreted as markup", () => {
    const h = harness();
    const label = '<img src=x onerror="alert(1)">';
    h.picker.setItems([{ id: 'id"unsafe', label, description: "<script>alert(1)</script>" }]);
    h.input.focus();
    assert.equal(h.choices()[0].children[0].textContent, label);
    assert.equal(h.choices()[0].children[0].tagName, "SPAN");
    assert.equal(h.choices()[0].getAttribute("data-picker-id"), 'id"unsafe');
    assert.doesNotMatch(source, /innerHTML|insertAdjacentHTML|dispatchEvent/);
});

test("older DOM implementations without replaceChildren retain working lookup updates", () => {
    const h = harness({ oldDom: true });
    h.picker.setItems(clients);
    h.input.focus();
    h.type("Digital");
    assert.equal(h.choices().length, 1);
    h.picker.setItems([{ id: "new", label: "Digital nueva" }]);
    assert.equal(h.choices().length, 1);
    assert.equal(h.choices()[0].getAttribute("data-picker-id"), "new");
});

test("closed updates do not unexpectedly open the list and the next focus sees fresh data", () => {
    const h = harness();
    h.picker.setItems(clients);
    h.input.focus();
    h.picker.close();
    h.picker.setItems([{ id: "new", label: "Nuevo cliente" }]);
    assert.equal(h.list.hidden, true);
    h.input.fire("click");
    assert.equal(h.choices().length, 1);
    assert.equal(h.choices()[0].getAttribute("data-picker-id"), "new");
});

test("disabled or read-only inputs do not open and disabled inputs cannot select", () => {
    for (const property of ["disabled", "readOnly"]) {
        const h = harness();
        h.picker.setItems(clients);
        h.input[property] = true;
        h.input.focus();
        h.key("ArrowDown");
        assert.equal(h.list.hidden, true);
    }
    const h = harness();
    h.picker.setItems(clients);
    h.input.focus();
    h.input.disabled = true;
    h.choices()[0].fire("click");
    assert.equal(h.selected.length, 0);
});

test("invalid catalog entries are ignored and a non-array results in a visible empty state", () => {
    const h = harness();
    h.picker.setItems([null, { id: null, label: "Bad" }, { id: "x", label: " " }, clients[0]]);
    h.input.focus();
    assert.equal(h.choices().length, 1);
    h.picker.setItems(null);
    assert.equal(h.choices().length, 0);
    assert.match(h.list.textContent, /No hay opciones/);
});

test("touch choices have at least 44px targets, bounded scrolling and explicit hidden styling", () => {
    assert.match(css, /\.mto-v2-picker-item\s*\{[^}]*min-height:\s*48px/s);
    assert.match(css, /\.mto-v2-picker-list\s*\{[^}]*overflow-y:\s*auto/s);
    assert.match(css, /\.mto-v2-picker-list\[hidden\]\s*\{\s*display:\s*none/s);
    assert.match(css, /\.mto-v2-picker\s*\{[^}]*position:\s*relative/s);
});

for (const pointerType of ["touch", "pen"]) {
    test(`${pointerType} release selects before Android compatibility blur/click and works with no click`, () => {
        const h = harness();
        h.picker.setItems(clients);
        h.input.focus();
        h.type("fiesta");
        const label = h.choices()[0].children[0];
        label.fire("pointerdown", { pointerType, pointerId: 12, clientX: 110, clientY: 200 });
        h.input.blur();
        h.doc.fire("focusin");
        assert.equal(h.list.hidden, false, "body focus while pointer is down must not remove the pressed option");
        const release = label.fire("pointerup", { pointerType, pointerId: 12, clientX: 114, clientY: 201 });
        assert.equal(release.defaultPrevented, true);
        assert.equal(h.selected.length, 1, "no click is required to select on touch/pen");
        assert.equal(h.selected[0].id, "client-2");
        h.input.blur();
        label.fire("mousedown");
        label.fire("mouseup");
        label.fire("click");
        assert.equal(h.selected.length, 1);
        assert.equal(h.list.hidden, true);
    });
}

test("a pointer drag and its delayed click do not select even if the finger returns to its origin", () => {
    const h = harness();
    h.picker.setItems(clients);
    h.input.focus();
    const choice = h.choices()[0];
    choice.fire("pointerdown", { pointerType: "touch", pointerId: 1, clientX: 10, clientY: 100 });
    choice.fire("pointermove", { pointerType: "touch", pointerId: 1, clientX: 10, clientY: 140 });
    choice.fire("pointermove", { pointerType: "touch", pointerId: 1, clientX: 10, clientY: 100 });
    choice.fire("pointerup", { pointerType: "touch", pointerId: 1, clientX: 10, clientY: 100 });
    choice.fire("click");
    assert.equal(h.selected.length, 0);
    assert.equal(h.list.hidden, false);
});

test("native list scrolling cancels selection even if there are no pointermove events", () => {
    const h = harness();
    h.picker.setItems(clients);
    h.input.focus();
    const choice = h.choices()[0];
    choice.fire("pointerdown", { pointerType: "touch", pointerId: 1, clientY: 100 });
    h.list.scrollTop = 45;
    choice.fire("pointerup", { pointerType: "touch", pointerId: 1, clientY: 100 });
    choice.fire("click");
    assert.equal(h.selected.length, 0);
});

test("pointercancel and a multi-finger gesture cannot turn into a synthetic-click selection", () => {
    for (const cancel of [true, false]) {
        const h = harness();
        h.picker.setItems(clients);
        h.input.focus();
        const choice = h.choices()[0];
        choice.fire("pointerdown", { pointerType: "touch", pointerId: 1 });
        if (cancel) choice.fire("pointercancel", { pointerType: "touch", pointerId: 1 });
        else choice.fire("pointerdown", { pointerType: "touch", pointerId: 2, isPrimary: false });
        choice.fire("pointerup", { pointerType: "touch", pointerId: 1 });
        choice.fire("click");
        assert.equal(h.selected.length, 0);
    }
});

test("a second deliberate touch selects after a scroll without waiting for click suppression", () => {
    const h = harness();
    h.picker.setItems(clients);
    h.input.focus();
    const choice = h.choices()[1];
    choice.fire("pointerdown", { pointerType: "touch", pointerId: 1 });
    choice.fire("pointercancel", { pointerType: "touch", pointerId: 1 });
    choice.fire("pointerdown", { pointerType: "touch", pointerId: 2 });
    choice.fire("pointerup", { pointerType: "touch", pointerId: 2 });
    assert.equal(h.selected[0].id, "client-2");
});

test("touch-only engines select on touchend without preventing native touchstart scrolling", () => {
    const h = harness({ touchOnly: true });
    h.picker.setItems(clients);
    h.input.focus();
    const choice = h.choices()[2];
    const point = { identifier: 5, clientX: 10, clientY: 100 };
    assert.equal(choice.fire("touchstart", { touches: [point] }).defaultPrevented, false);
    h.input.blur();
    assert.equal(choice.fire("touchend", { changedTouches: [point] }).defaultPrevented, true);
    choice.fire("click");
    assert.equal(h.selected.length, 1);
    assert.equal(h.selected[0].id, "client-3");
});

test("touch-only scrolling keeps the popup open without a false selection", () => {
    const h = harness({ touchOnly: true });
    h.picker.setItems(clients);
    h.input.focus();
    const choice = h.choices()[0];
    const start = { identifier: 1, clientX: 10, clientY: 100 };
    const end = { ...start, clientY: 150 };
    choice.fire("touchstart", { touches: [start] });
    choice.fire("touchmove", { touches: [end] });
    choice.fire("touchend", { changedTouches: [end] });
    choice.fire("click");
    assert.equal(h.selected.length, 0);
    assert.equal(h.list.hidden, false);
});
