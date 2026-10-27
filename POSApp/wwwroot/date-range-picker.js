// Helper for Blazor interactive DateRangePicker component.

const SHORT_MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function formatDisplay(iso) {
    if (!iso || !/^\d{4}-\d{2}-\d{2}$/.test(iso)) return "";
    const parts = iso.split("-");
    return `${parts[2]} ${SHORT_MONTHS[Number(parts[1]) - 1]} ${parts[0]}`;
}

// Called by Blazor component when range or preset is committed
window.DateRangePicker_UpdateForm = (id, start, end, period, autoSubmit) => {
    const el = document.getElementById(id);
    const form = el?.closest("form");
    if (!form) return;

    const periodInput = form.querySelector('input[name="period"]');
    if (periodInput && period) {
        periodInput.value = period;
    }

    const startInput = form.querySelector('input[data-range-start]');
    if (startInput) startInput.value = start;

    const endInput = form.querySelector('input[data-range-end]');
    if (endInput) endInput.value = end;

    if (autoSubmit) {
        form.requestSubmit();
    }
};

// Smooth, non-blocking hover preview for Blazor calendar
document.addEventListener("mouseover", (e) => {
    const popover = e.target.closest("[data-calendar-popover]");
    if (!popover) return;
    const startIso = popover.dataset.selectionStart;
    if (!startIso) return;
    const dayBtn = e.target.closest("[data-date]");
    if (!dayBtn) return;
    const hoverIso = dayBtn.dataset.date;
    if (!hoverIso) return;

    const min = startIso <= hoverIso ? startIso : hoverIso;
    const max = startIso <= hoverIso ? hoverIso : startIso;

    const statusEl = popover.querySelector(".status-instruction");
    if (statusEl) {
        statusEl.innerHTML = `Selected range: <strong class="text-blue-600">${formatDisplay(min)} – ${formatDisplay(max)}</strong> · Click to confirm`;
    }

    popover.querySelectorAll("[data-date]").forEach((btn) => {
        const iso = btn.dataset.date;
        const wrap = btn.closest("[data-wrap]");
        const isStart = iso === startIso;
        const isHover = iso === hoverIso;
        const isBetween = iso > min && iso < max;

        if (isStart) {
            btn.className = "size-8 inline-flex items-center justify-center text-xs transition cursor-pointer select-none bg-blue-600 text-white font-semibold rounded-l-lg shadow-sm z-10";
            if (wrap) wrap.className = "relative flex items-center justify-center bg-blue-50 rounded-l-lg";
        } else if (isHover) {
            btn.className = "size-8 inline-flex items-center justify-center text-xs transition cursor-pointer select-none bg-blue-500 text-white font-semibold rounded-r-lg shadow-sm z-10";
            if (wrap) wrap.className = "relative flex items-center justify-center bg-blue-50 rounded-r-lg";
        } else if (isBetween) {
            btn.className = "size-8 inline-flex items-center justify-center text-xs transition cursor-pointer select-none text-blue-900 font-medium bg-blue-100/80";
            if (wrap) wrap.className = "relative flex items-center justify-center bg-blue-50";
        } else {
            btn.className = "size-8 inline-flex items-center justify-center text-xs transition cursor-pointer select-none text-slate-700 hover:bg-slate-100 rounded-lg";
            if (wrap) wrap.className = "relative flex items-center justify-center";
        }
    });
});

document.addEventListener("mouseout", (e) => {
    const popover = e.target.closest("[data-calendar-popover]");
    if (!popover) return;
    const related = e.relatedTarget?.closest?.("[data-calendar-popover]");
    if (!related) {
        const startIso = popover.dataset.selectionStart;
        if (startIso) {
            const statusEl = popover.querySelector(".status-instruction");
            if (statusEl) {
                statusEl.innerHTML = `Selected start: <strong class="text-blue-600">${formatDisplay(startIso)}</strong> · Click end date`;
            }
            popover.querySelectorAll("[data-date]").forEach((btn) => {
                const iso = btn.dataset.date;
                const wrap = btn.closest("[data-wrap]");
                if (iso === startIso) {
                    btn.className = "size-8 inline-flex items-center justify-center text-xs transition cursor-pointer select-none bg-blue-600 text-white font-semibold rounded-lg shadow-sm z-10";
                    if (wrap) wrap.className = "relative flex items-center justify-center bg-blue-50 rounded-lg";
                } else {
                    btn.className = "size-8 inline-flex items-center justify-center text-xs transition cursor-pointer select-none text-slate-700 hover:bg-slate-100 rounded-lg";
                    if (wrap) wrap.className = "relative flex items-center justify-center";
                }
            });
        }
    }
});

export function initialize(blazor) {
    // No-op hook for module startup
}
