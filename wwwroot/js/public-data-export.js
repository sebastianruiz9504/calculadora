(function () {
    const adminApp = document.getElementById("publicDataExportAdmin");
    if (adminApp) {
        initAdmin(adminApp);
    }

    function initAdmin(root) {
        root.querySelectorAll("[data-pde-admin-dropdown]").forEach(function (container) {
            const counter = container.querySelector("[data-pde-selected-count]");
            const menu = container.querySelector(".pde-column-menu");
            const inputs = Array.from(container.querySelectorAll("input[type='checkbox']"));

            function updateCounter() {
                if (counter) {
                    counter.textContent = String(inputs.filter(function (input) { return input.checked; }).length);
                }
            }

            if (menu) {
                menu.addEventListener("click", function (event) {
                    event.stopPropagation();
                });
            }

            inputs.forEach(function (input) {
                input.addEventListener("change", updateCounter);
            });

            updateCounter();
        });
    }
})();
