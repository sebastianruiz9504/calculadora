(function (global) {
    "use strict";
    // IndexedDB stores blobs without base64 inflation. Never store auth/CSRF tokens.
    // One recovery slot per tenant/user; receipts stay server-side after success.
    function open() {
        return new Promise((resolve, reject) => {
            const request = global.indexedDB.open("copiers-mto-v2-drafts", 1);
            request.onupgradeneeded = () => request.result.createObjectStore("drafts");
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
            request.onblocked = () => reject(new Error("El guardado local está bloqueado por otra pestaña."));
        });
    }
    async function transaction(owner, mode, action) {
        if (!owner) throw new Error("No fue posible identificar al usuario para recuperar el borrador.");
        const database = await open();
        try {
            return await new Promise((resolve, reject) => {
                const tx = database.transaction("drafts", mode);
                const request = action(tx.objectStore("drafts"), owner);
                let result;
                request.onsuccess = () => { result = request.result; };
                tx.oncomplete = () => resolve(result);
                tx.onabort = tx.onerror = () => reject(tx.error || new Error("No fue posible guardar el borrador en este dispositivo."));
            });
        } finally { database.close(); }
    }
    global.CopiersMtoV2Drafts = {
        read: owner => transaction(owner, "readonly", (store, key) => store.get(key)),
        save: (owner, value) => transaction(owner, "readwrite", (store, key) => store.put(value, key)),
        remove: owner => transaction(owner, "readwrite", (store, key) => store.delete(key))
    };
})(window);
