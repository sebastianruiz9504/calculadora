// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(function () {
    const storageKey = "cotizador-interno-theme";
    const darkTheme = "dark";
    const lightTheme = "light";

    function getStoredTheme() {
        try {
            return localStorage.getItem(storageKey) === darkTheme ? darkTheme : lightTheme;
        } catch (error) {
            return lightTheme;
        }
    }

    function saveTheme(theme) {
        try {
            localStorage.setItem(storageKey, theme);
        } catch (error) {
            // Some browsers can disable localStorage; the live toggle should still work.
        }
    }

    function applyTheme(theme) {
        const isDark = theme === darkTheme;
        const nextLabel = isDark ? "Activar modo claro" : "Activar modo oscuro";

        document.documentElement.setAttribute("data-bs-theme", theme);
        document.documentElement.classList.toggle("app-theme-dark", isDark);

        if (document.body) {
            document.body.classList.toggle("app-shell--dark", isDark);
        }

        document.querySelectorAll("[data-theme-toggle]").forEach((button) => {
            button.setAttribute("aria-pressed", String(isDark));
            button.setAttribute("aria-label", nextLabel);
            button.setAttribute("title", nextLabel);

            const label = button.querySelector("[data-theme-toggle-label]");
            if (label) {
                label.textContent = nextLabel;
            }
        });
    }

    applyTheme(getStoredTheme());

    document.addEventListener("DOMContentLoaded", () => {
        applyTheme(getStoredTheme());

        document.querySelectorAll("[data-theme-toggle]").forEach((button) => {
            button.addEventListener("click", () => {
                const currentTheme = document.documentElement.getAttribute("data-bs-theme") === darkTheme
                    ? darkTheme
                    : lightTheme;
                const nextTheme = currentTheme === darkTheme ? lightTheme : darkTheme;

                saveTheme(nextTheme);
                applyTheme(nextTheme);
            });
        });
    });
})();
