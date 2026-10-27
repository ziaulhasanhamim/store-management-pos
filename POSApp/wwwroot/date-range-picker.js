// Zero-overhead Vanilla JS DateRangePicker Controller for Blazor Static SSR.

const SHORT_MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
const DAY_NAMES = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];

function pad2(n) {
    return n < 10 ? "0" + n : "" + n;
}

function toIso(year, month, day) {
    return `${year}-${pad2(month)}-${pad2(day)}`;
}

function parseIso(iso) {
    if (!iso || !/^\d{4}-\d{2}-\d{2}$/.test(iso)) return null;
    const parts = iso.split("-").map(Number);
    return new Date(parts[0], parts[1] - 1, parts[2]);
}

function formatDisplay(iso) {
    if (!iso) return "";
    const parts = iso.split("-");
    if (parts.length !== 3) return iso;
    return `${parseInt(parts[2], 10)} ${SHORT_MONTHS[Number(parts[1]) - 1]} ${parts[0]}`;
}

function formatRangeDisplay(startIso, endIso) {
    if (startIso && endIso) {
        return startIso === endIso
            ? formatDisplay(startIso)
            : `${formatDisplay(startIso)} – ${formatDisplay(endIso)}`;
    }
    if (startIso) return `From ${formatDisplay(startIso)}`;
    if (endIso) return `Through ${formatDisplay(endIso)}`;
    return "Select date range";
}

function getTodayIso() {
    const now = new Date();
    return toIso(now.getFullYear(), now.getMonth() + 1, now.getDate());
}

function renderMonthHtml(year, month, startIso, endIso) {
    const firstDay = new Date(year, month - 1, 1);
    const daysInMonth = new Date(year, month, 0).getDate();
    const startDayOfWeek = firstDay.getDay();
    const todayIso = getTodayIso();

    let html = `<div>
        <div class="grid grid-cols-7 gap-1 text-center text-[11px] font-semibold text-slate-400 mb-1">
            ${DAY_NAMES.map(d => `<span>${d}</span>`).join("")}
        </div>
        <div class="grid grid-cols-7 gap-1">`;

    for (let i = 0; i < startDayOfWeek; i++) {
        html += `<div class="size-8"></div>`;
    }

    for (let day = 1; day <= daysInMonth; day++) {
        const dayIso = toIso(year, month, day);
        const isStart = startIso && dayIso === startIso;
        const isEnd = endIso && dayIso === endIso;
        const inRange = startIso && endIso && dayIso >= startIso && dayIso <= endIso;
        const isBetween = startIso && endIso && dayIso > startIso && dayIso < endIso;

        let wrapClass = "relative flex items-center justify-center";
        if (inRange) {
            wrapClass += " bg-blue-50";
            if (isStart) wrapClass += endIso ? " rounded-l-lg" : " rounded-lg";
            else if (isEnd) wrapClass += " rounded-r-lg";
        }

        let btnClass = "size-8 inline-flex items-center justify-center text-xs transition cursor-pointer select-none";
        if (isStart && isEnd) {
            btnClass += " bg-blue-600 text-white font-semibold rounded-lg shadow-sm z-10";
        } else if (isStart) {
            btnClass += ` bg-blue-600 text-white font-semibold ${endIso ? "rounded-l-lg" : "rounded-lg"} shadow-sm z-10`;
        } else if (isEnd) {
            btnClass += " bg-blue-600 text-white font-semibold rounded-r-lg shadow-sm z-10";
        } else if (isBetween) {
            btnClass += " text-blue-900 font-medium bg-blue-100/80";
        } else if (dayIso === todayIso) {
            btnClass += " text-slate-700 hover:bg-slate-100 rounded-lg border border-blue-400 font-semibold";
        } else {
            btnClass += " text-slate-700 hover:bg-slate-100 rounded-lg";
        }

        html += `<div class="${wrapClass}" data-wrap="${dayIso}">
            <button type="button" class="${btnClass}" data-date="${dayIso}" aria-label="${dayIso}">
                ${day}
            </button>
        </div>`;
    }

    html += `</div></div>`;
    return html;
}

function updateCalendarViews(popover, year, month) {
    popover.dataset.viewYear = year;
    popover.dataset.viewMonth = month;

    const nextMonth = month === 12 ? 1 : month + 1;
    const nextYear = month === 12 ? year + 1 : year;

    const h1 = popover.querySelector("[data-month-header-1]");
    const h2 = popover.querySelector("[data-month-header-2]");
    if (h1) h1.textContent = `${SHORT_MONTHS[month - 1]} ${year}`;
    if (h2) h2.textContent = `${SHORT_MONTHS[nextMonth - 1]} ${nextYear}`;

    const picker = popover.closest("[data-range-picker]");
    const startIso = popover.dataset.selectionStart || picker?.querySelector("[data-range-start]")?.value || "";
    const endIso = popover.dataset.selectionStart ? "" : picker?.querySelector("[data-range-end]")?.value || "";

    const m1 = popover.querySelector("[data-calendar-month-1]");
    const m2 = popover.querySelector("[data-calendar-month-2]");
    if (m1) m1.innerHTML = renderMonthHtml(year, month, startIso, endIso);
    if (m2) m2.innerHTML = renderMonthHtml(nextYear, nextMonth, startIso, endIso);
}

function commitRange(popover, startIso, endIso, period) {
    const picker = popover.closest("[data-range-picker]");
    if (!picker) return;

    const startInput = picker.querySelector("input[data-range-start]");
    const endInput = picker.querySelector("input[data-range-end]");
    const displaySpan = picker.querySelector("[data-range-display]");

    if (startInput) startInput.value = startIso;
    if (endInput) endInput.value = endIso;
    if (displaySpan) displaySpan.textContent = formatRangeDisplay(startIso, endIso);

    delete popover.dataset.selectionStart;
    popover.classList.add("hidden");

    const trigger = picker.querySelector("[data-range-trigger]");
    if (trigger) trigger.setAttribute("aria-expanded", "false");

    const form = picker.closest("form");
    if (!form) return;

    const periodInput = form.querySelector('input[name="period"]');
    if (periodInput && period) {
        periodInput.value = period;
    }

    if (popover.dataset.autoSubmit !== "false") {
        form.requestSubmit();
    }
}

// Global click handling
document.addEventListener("click", (e) => {
    const target = e.target;

    // Toggle popover
    const trigger = target.closest("[data-range-trigger]");
    if (trigger) {
        const picker = trigger.closest("[data-range-picker]");
        const popover = picker?.querySelector("[data-calendar-popover]");
        if (popover) {
            const isHidden = popover.classList.contains("hidden");
            // Close any other open popovers
            document.querySelectorAll("[data-calendar-popover]").forEach(p => {
                if (p !== popover) p.classList.add("hidden");
            });

            if (isHidden) {
                popover.classList.remove("hidden");
                trigger.setAttribute("aria-expanded", "true");

                // Initialize view year/month from current start or today
                const startVal = picker.querySelector("input[data-range-start]")?.value;
                const d = parseIso(startVal) || new Date();
                updateCalendarViews(popover, d.getFullYear(), d.getMonth() + 1);
            } else {
                popover.classList.add("hidden");
                trigger.setAttribute("aria-expanded", "false");
                delete popover.dataset.selectionStart;
            }
        }
        return;
    }

    // Close button
    if (target.closest("[data-range-close]")) {
        const popover = target.closest("[data-calendar-popover]");
        if (popover) {
            popover.classList.add("hidden");
            delete popover.dataset.selectionStart;
            const picker = popover.closest("[data-range-picker]");
            picker?.querySelector("[data-range-trigger]")?.setAttribute("aria-expanded", "false");
        }
        return;
    }

    // Preset button
    const presetBtn = target.closest("[data-range-preset]");
    if (presetBtn) {
        const popover = presetBtn.closest("[data-calendar-popover]");
        if (!popover) return;
        const preset = presetBtn.dataset.rangePreset;
        const now = new Date();
        const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());

        let start, end, periodName = preset;
        if (preset === "today") {
            start = end = today;
        } else if (preset === "yesterday") {
            start = new Date(today);
            start.setDate(start.getDate() - 1);
            end = start;
        } else if (preset === "last7") {
            start = new Date(today);
            start.setDate(start.getDate() - 6);
            end = today;
            periodName = "week";
        } else if (preset === "thisMonth") {
            start = new Date(today.getFullYear(), today.getMonth(), 1);
            end = new Date(today.getFullYear(), today.getMonth() + 1, 0);
            periodName = "month";
        } else if (preset === "lastMonth") {
            start = new Date(today.getFullYear(), today.getMonth() - 1, 1);
            end = new Date(today.getFullYear(), today.getMonth(), 0);
            periodName = "lastmonth";
        }

        if (start && end) {
            const startIso = toIso(start.getFullYear(), start.getMonth() + 1, start.getDate());
            const endIso = toIso(end.getFullYear(), end.getMonth() + 1, end.getDate());
            commitRange(popover, startIso, endIso, periodName);
        }
        return;
    }

    // Month Navigation
    const navBtn = target.closest("[data-month-nav]");
    if (navBtn) {
        const popover = navBtn.closest("[data-calendar-popover]");
        if (!popover) return;
        let year = parseInt(popover.dataset.viewYear || `${new Date().getFullYear()}`, 10);
        let month = parseInt(popover.dataset.viewMonth || `${new Date().getMonth() + 1}`, 10);

        if (navBtn.dataset.monthNav === "prev") {
            month--;
            if (month < 1) { month = 12; year--; }
        } else {
            month++;
            if (month > 12) { month = 1; year++; }
        }
        updateCalendarViews(popover, year, month);
        return;
    }

    // Reset selection
    if (target.closest("[data-range-reset]")) {
        const popover = target.closest("[data-calendar-popover]");
        if (popover) {
            delete popover.dataset.selectionStart;
            const statusEl = popover.querySelector("[data-status-instruction]");
            if (statusEl) statusEl.innerHTML = "<span>Click a date to select start date</span>";
            target.closest("[data-range-reset]")?.classList.add("hidden");
            const y = parseInt(popover.dataset.viewYear, 10);
            const m = parseInt(popover.dataset.viewMonth, 10);
            updateCalendarViews(popover, y, m);
        }
        return;
    }

    // Day button clicked
    const dayBtn = target.closest("[data-date]");
    if (dayBtn) {
        const popover = dayBtn.closest("[data-calendar-popover]");
        if (!popover) return;
        const clickedIso = dayBtn.dataset.date;
        const currentStart = popover.dataset.selectionStart;

        if (!currentStart) {
            // First click: select start
            popover.dataset.selectionStart = clickedIso;
            const statusEl = popover.querySelector("[data-status-instruction]");
            if (statusEl) {
                statusEl.innerHTML = `Selected start: <strong class="text-blue-600">${formatDisplay(clickedIso)}</strong> · Click end date`;
            }
            popover.querySelector("[data-range-reset]")?.classList.remove("hidden");
            const y = parseInt(popover.dataset.viewYear, 10);
            const m = parseInt(popover.dataset.viewMonth, 10);
            updateCalendarViews(popover, y, m);
        } else {
            // Second click: commit range
            let start = currentStart;
            let end = clickedIso;
            if (end < start) {
                const tmp = start;
                start = end;
                end = tmp;
            }
            commitRange(popover, start, end, "custom");
        }
        return;
    }

    // Click outside to close
    if (!target.closest("[data-range-picker]")) {
        document.querySelectorAll("[data-calendar-popover]").forEach(popover => {
            if (!popover.classList.contains("hidden")) {
                popover.classList.add("hidden");
                delete popover.dataset.selectionStart;
                const picker = popover.closest("[data-range-picker]");
                picker?.querySelector("[data-range-trigger]")?.setAttribute("aria-expanded", "false");
            }
        });
    }
});

// Escape key to close
document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") {
        document.querySelectorAll("[data-calendar-popover]:not(.hidden)").forEach(popover => {
            popover.classList.add("hidden");
            delete popover.dataset.selectionStart;
            const picker = popover.closest("[data-range-picker]");
            picker?.querySelector("[data-range-trigger]")?.setAttribute("aria-expanded", "false");
        });
    }
});

// Hover preview during active selection
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

    const statusEl = popover.querySelector("[data-status-instruction]");
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
            const statusEl = popover.querySelector("[data-status-instruction]");
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
