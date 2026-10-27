import { initialize as initializeFormDrafts } from "./js/form-drafts.js";
import { initialize as initializeAutoSearch } from "./search-enhancements.js";
import { initialize as initializeDateRangePicker } from "./date-range-picker.js";

export function afterWebStarted(blazor) {
    initializeFormDrafts(blazor);
    initializeAutoSearch(blazor);
    initializeDateRangePicker(blazor);
    let pending = null;
    let progress = 0;
    let active = false;
    let tick = null;
    let fade = null;
    let frame = null;

    const indicator = () => document.getElementById("navigation-progress");
    const bar = () => indicator()?.querySelector(".navigation-progress-bar");
    const paint = () => {
        const fill = bar();
        if (fill) fill.style.transform = `scaleX(${progress})`;
    };
    const stopTimers = () => {
        clearInterval(tick);
        clearTimeout(fade);
        cancelAnimationFrame(frame);
    };

    const startProgress = () => {
        stopTimers();
        const track = indicator();
        const fill = bar();
        if (!track || !fill) return;
        if (!active) {
            progress = 0;
            fill.style.transition = "none";
            paint();
        }
        active = true;
        track.hidden = false;
        track.style.opacity = "1";
        frame = requestAnimationFrame(() => {
            fill.style.transition = "";
            progress = Math.max(progress, 0.12);
            paint();
            // Estimated progress: approach 94%, but complete only when Blazor finishes.
            tick = setInterval(() => {
                progress = Math.min(0.94, progress + (1 - progress) * 0.1);
                paint();
            }, 350);
        });
    };

    const restoreForm = () => {
        if (!pending) return;
        const { form, button, label, originalLabelNodes, originalLabelText, originalDisabled, originalBusy, spinners } = pending;
        if (button) {
            if (label instanceof Element) label.replaceChildren(...originalLabelNodes);
            else if (label) label.textContent = originalLabelText;
            button.disabled = originalDisabled;
        }
        for (const { element, hidden } of spinners) {
            if (hidden === null) element.removeAttribute("hidden");
            else element.setAttribute("hidden", hidden);
        }
        if (originalBusy === null) form.removeAttribute("aria-busy");
        else form.setAttribute("aria-busy", originalBusy);
        pending = null;
    };

    const finishProgress = () => {
        stopTimers();
        active = false;
        progress = 1;
        const fill = bar();
        if (fill) fill.style.transition = "";
        paint();
        restoreForm();
        // Let the bar visibly reach the end before fading it out.
        fade = setTimeout(() => {
            const track = indicator();
            if (track) track.style.opacity = "0";
            fade = setTimeout(() => {
                if (track) track.hidden = true;
            }, 180);
        }, 250);
    };

    blazor.addEventListener("enhancednavigationstart", startProgress);
    blazor.addEventListener("enhancednavigationend", finishProgress);
    document.addEventListener("submit", event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.hasAttribute("data-enhance")
            || form.dataset.enhance === "false" || form.dataset.submitIndication !== "true") return;
        if (pending?.form === form) {
            if (form.method?.toLowerCase() === "get" || form.hasAttribute("data-auto-search")) {
                restoreForm();
            } else {
                event.preventDefault();
                return;
            }
        }
        const button = event.submitter instanceof HTMLButtonElement
            ? event.submitter : form.querySelector('button[type="submit"]');
        // Blazor must capture the form data before its submitter is disabled or changed.
        queueMicrotask(() => {
            restoreForm();
            let spinnerElements = [];
            const selector = form.dataset.spinnerSelector;
            if (selector) {
                try {
                    spinnerElements = [...form.querySelectorAll(selector)];
                    // An explicit selector may also target an indicator outside the form.
                    if (spinnerElements.length === 0) spinnerElements = [...document.querySelectorAll(selector)];
                } catch {
                    console.warn("Invalid data-spinner-selector:", selector);
                }
            }
            const label = button?.hasAttribute("data-pending-text")
                ? button.querySelector("[data-submit-label]")
                    ?? [...button.childNodes].find(node => node.nodeType === Node.TEXT_NODE && node.textContent.trim())
                : null;
            pending = {
                form,
                button,
                label,
                originalLabelNodes: label instanceof Element ? [...label.childNodes] : [],
                originalLabelText: label?.textContent,
                originalDisabled: button?.disabled,
                originalBusy: form.getAttribute("aria-busy"),
                spinners: spinnerElements.map(element => ({ element, hidden: element.getAttribute("hidden") }))
            };
            if (button) {
                // Change only the label, preserving developer-provided spinner/icon markup.
                if (label && !spinnerElements.some(spinner => label === spinner || label.contains?.(spinner)))
                    label.textContent = button.dataset.pendingText;
                button.disabled = true;
            }
            for (const spinner of spinnerElements) spinner.removeAttribute("hidden");
            form.setAttribute("aria-busy", "true");
        });
    }, true);
}
