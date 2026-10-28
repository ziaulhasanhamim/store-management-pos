// Automatic debounced search with focus and cursor retention across Blazor enhanced navigations.
let debounceTimer = null;
let activeSearchState = null;
const DEBOUNCE_DELAY_MS = 300;

function isAutoSearchInput(element) {
    if (!(element instanceof HTMLInputElement)) return false;
    if (element.dataset.autoSearch === "false" || element.closest("form")?.dataset.autoSearch === "false") {
        return false;
    }
    return element.dataset.autoSearch === "true" || element.closest("form")?.dataset.autoSearch === "true";
}

function getAutoSearchForm(input) {
    return input.closest("form");
}

function saveActiveInputState(input) {
    activeSearchState = {
        id: input.id || null,
        name: input.name || null,
        value: input.value,
        selectionStart: input.selectionStart,
        selectionEnd: input.selectionEnd,
        form: input.form
    };
}

function restoreActiveInputState() {
    if (!activeSearchState) return;

    let input = null;
    if (activeSearchState.id) {
        input = document.getElementById(activeSearchState.id);
    }
    if (!input && activeSearchState.name) {
        input = document.querySelector(`input[name="${CSS.escape(activeSearchState.name)}"]`);
    }

    if (input && input instanceof HTMLInputElement) {
        // Prevent FocusOnNavigate from stealing focus away
        if (document.activeElement !== input) {
            input.focus({ preventScroll: true });
            const start = Math.min(activeSearchState.selectionStart ?? input.value.length, input.value.length);
            const end = Math.min(activeSearchState.selectionEnd ?? input.value.length, input.value.length);
            try {
                input.setSelectionRange(start, end);
            } catch {
                // Some input types don't support setSelectionRange
            }
        }

        // Check if the current input value matches the URL search parameter
        const currentUrl = new URL(window.location.href);
        const urlSearchParam = currentUrl.searchParams.get(input.name || "search") || "";
        if (input.value.trim() !== urlSearchParam.trim()) {
            triggerDebouncedSearch(input);
        }
    }

    activeSearchState = null;
}

function triggerDebouncedSearch(input) {
    clearTimeout(debounceTimer);
    debounceTimer = setTimeout(() => {
        const form = getAutoSearchForm(input);
        if (!form) return;

        saveActiveInputState(input);
        form.requestSubmit();
    }, DEBOUNCE_DELAY_MS);
}

export function initialize(blazor) {
    // Input typing with debouncing
    document.addEventListener("input", (event) => {
        const target = event.target;
        if (!isAutoSearchInput(target)) return;

        triggerDebouncedSearch(target);
    }, true);

    // Dropdowns (select) inside auto-search forms trigger immediate submit
    document.addEventListener("change", (event) => {
        const target = event.target;
        if (target instanceof HTMLSelectElement && target.closest("form")?.dataset.autoSearch === "true") {
            clearTimeout(debounceTimer);
            target.closest("form").requestSubmit();
        }
    }, true);

    // Keyboard shortcuts: Enter (immediate), Escape (clear & submit)
    document.addEventListener("keydown", (event) => {
        const target = event.target;
        if (!isAutoSearchInput(target)) return;

        if (event.key === "Enter") {
            clearTimeout(debounceTimer);
            saveActiveInputState(target);
            // Form submission proceeds naturally on Enter
        } else if (event.key === "Escape") {
            clearTimeout(debounceTimer);
            target.value = "";
            saveActiveInputState(target);
            const form = getAutoSearchForm(target);
            if (form) form.requestSubmit();
        }
    }, true);

    // Enhanced navigation lifecycle hooks
    if (blazor) {
        blazor.addEventListener("enhancednavigationstart", () => {
            const active = document.activeElement;
            if (isAutoSearchInput(active)) {
                saveActiveInputState(active);
            }
        });

        blazor.addEventListener("enhancednavigationend", () => {
            // Restore active search input focus and cursor after Blazor renders DOM updates
            requestAnimationFrame(() => {
                restoreActiveInputState();
            });
        });
    }
}
