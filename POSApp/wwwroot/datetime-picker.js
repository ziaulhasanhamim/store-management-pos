// Custom DateTimePicker Popover with embedded in-calendar Done button
// Replaces browser native popovers with a consistent calendar and time selector containing an in-calendar Done button.

(function () {
    const MONTH_NAMES = [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    ];
    const DAY_NAMES = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];

    function pad2(n) {
        return n < 10 ? "0" + n : "" + n;
    }

    function formatIsoDate(year, month, day) {
        return `${year}-${pad2(month)}-${pad2(day)}`;
    }

    let activePopover = null;
    let activeInput = null;

    function closeActivePopover() {
        if (activePopover) {
            activePopover.remove();
            activePopover = null;
            activeInput = null;
        }
    }

    function parseInputValue(val) {
        if (!val) {
            const now = new Date();
            return {
                year: now.getFullYear(),
                month: now.getMonth() + 1,
                day: now.getDate(),
                hour: now.getHours(),
                minute: now.getMinutes()
            };
        }
        const parts = val.split("T");
        const dateParts = parts[0].split("-").map(Number);
        let hour = 12;
        let minute = 0;
        if (parts[1]) {
            const timeParts = parts[1].split(":").map(Number);
            hour = timeParts[0] || 0;
            minute = timeParts[1] || 0;
        }
        return {
            year: dateParts[0],
            month: dateParts[1],
            day: dateParts[2],
            hour: hour,
            minute: minute
        };
    }

    function openDateTimePicker(input) {
        if (activeInput === input && activePopover) {
            return;
        }
        closeActivePopover();

        const current = parseInputValue(input.value);
        let viewYear = current.year;
        let viewMonth = current.month;
        let selectedDateIso = formatIsoDate(current.year, current.month, current.day);

        let hour12 = current.hour % 12 === 0 ? 12 : current.hour % 12;
        let isPm = current.hour >= 12;
        let currentMinute = current.minute;

        const popover = document.createElement("div");
        popover.className = "jas-datetime-popover absolute z-[9999] mt-1 w-72 rounded-xl border border-slate-200 bg-white p-3.5 shadow-2xl transition-all select-none text-slate-800 text-xs font-sans";
        popover.setAttribute("data-datetime-popover", "true");

        function renderCalendar() {
            const firstDay = new Date(viewYear, viewMonth - 1, 1);
            const daysInMonth = new Date(viewYear, viewMonth, 0).getDate();
            const startDayOfWeek = firstDay.getDay();

            let daysHtml = "";
            for (let i = 0; i < startDayOfWeek; i++) {
                daysHtml += `<div class="size-7"></div>`;
            }

            for (let d = 1; d <= daysInMonth; d++) {
                const dayIso = formatIsoDate(viewYear, viewMonth, d);
                const isSelected = dayIso === selectedDateIso;
                const cls = isSelected
                    ? "size-7 rounded-lg bg-blue-600 text-white font-semibold flex items-center justify-center cursor-pointer shadow-sm"
                    : "size-7 rounded-lg hover:bg-slate-100 text-slate-700 flex items-center justify-center cursor-pointer font-medium";
                daysHtml += `<button type="button" class="${cls}" data-date="${dayIso}">${d}</button>`;
            }

            popover.innerHTML = `
                <!-- Calendar Header -->
                <div class="flex items-center justify-between pb-2 mb-2 border-b border-slate-100">
                    <button type="button" class="jas-dt-prev p-1 rounded-md hover:bg-slate-100 text-slate-600 font-bold" title="Previous month">
                        <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7"/></svg>
                    </button>
                    <span class="font-bold text-slate-900">${MONTH_NAMES[viewMonth - 1]} ${viewYear}</span>
                    <button type="button" class="jas-dt-next p-1 rounded-md hover:bg-slate-100 text-slate-600 font-bold" title="Next month">
                        <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7"/></svg>
                    </button>
                </div>

                <!-- Days of Week Header -->
                <div class="grid grid-cols-7 gap-1 text-center font-semibold text-slate-400 text-[10px] mb-1">
                    ${DAY_NAMES.map(d => `<span>${d}</span>`).join("")}
                </div>

                <!-- Days Grid -->
                <div class="grid grid-cols-7 gap-1 mb-3">
                    ${daysHtml}
                </div>

                <!-- Time Selector -->
                <div class="pt-2.5 pb-2.5 border-t border-slate-100 flex items-center justify-between gap-1 text-xs">
                    <span class="text-slate-500 font-medium text-[11px]">Time:</span>
                    <div class="flex items-center gap-1">
                        <select class="jas-dt-hour rounded border border-slate-300 py-1 px-1.5 text-xs bg-white text-slate-800 font-medium focus:ring-1 focus:ring-blue-500">
                            ${[12, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11].map(h => `<option value="${h}" ${h === hour12 ? "selected" : ""}>${pad2(h)}</option>`).join("")}
                        </select>
                        <span class="font-bold text-slate-400">:</span>
                        <select class="jas-dt-minute rounded border border-slate-300 py-1 px-1.5 text-xs bg-white text-slate-800 font-medium focus:ring-1 focus:ring-blue-500">
                            ${Array.from({ length: 60 }, (_, m) => `<option value="${m}" ${m === currentMinute ? "selected" : ""}>${pad2(m)}</option>`).join("")}
                        </select>
                        <button type="button" class="jas-dt-ampm px-2 py-1 rounded border border-slate-300 text-xs font-bold ${isPm ? "bg-slate-800 text-white border-slate-800" : "bg-white text-slate-700"}">
                            ${isPm ? "PM" : "AM"}
                        </button>
                    </div>
                </div>

                <!-- Footer with embedded Done Button on Calendar -->
                <div class="pt-2 border-t border-slate-100 flex items-center justify-between gap-2">
                    <button type="button" class="jas-dt-now text-[11px] text-blue-600 hover:text-blue-800 font-medium underline px-1 py-1">Set to Now</button>
                    <button type="button" class="jas-dt-done px-3.5 py-1.5 rounded-lg bg-blue-600 hover:bg-blue-700 text-white font-semibold text-xs flex items-center gap-1.5 shadow-sm transition">
                        <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2.5" d="M5 13l4 4L19 7"/></svg>
                        Done
                    </button>
                </div>
            `;

            attachEvents();
        }

        function attachEvents() {
            popover.querySelector(".jas-dt-prev").onclick = (e) => {
                e.stopPropagation();
                viewMonth--;
                if (viewMonth < 1) {
                    viewMonth = 12;
                    viewYear--;
                }
                renderCalendar();
            };

            popover.querySelector(".jas-dt-next").onclick = (e) => {
                e.stopPropagation();
                viewMonth++;
                if (viewMonth > 12) {
                    viewMonth = 1;
                    viewYear++;
                }
                renderCalendar();
            };

            popover.querySelectorAll("[data-date]").forEach(btn => {
                btn.onclick = (e) => {
                    e.stopPropagation();
                    selectedDateIso = btn.dataset.date;
                    renderCalendar();
                };
            });

            const hourSelect = popover.querySelector(".jas-dt-hour");
            hourSelect.onchange = () => {
                hour12 = Number(hourSelect.value);
            };

            const minSelect = popover.querySelector(".jas-dt-minute");
            minSelect.onchange = () => {
                currentMinute = Number(minSelect.value);
            };

            const ampmBtn = popover.querySelector(".jas-dt-ampm");
            ampmBtn.onclick = (e) => {
                e.stopPropagation();
                isPm = !isPm;
                renderCalendar();
            };

            popover.querySelector(".jas-dt-now").onclick = (e) => {
                e.stopPropagation();
                const now = new Date();
                viewYear = now.getFullYear();
                viewMonth = now.getMonth() + 1;
                selectedDateIso = formatIsoDate(viewYear, viewMonth, now.getDate());
                const h = now.getHours();
                hour12 = h % 12 === 0 ? 12 : h % 12;
                isPm = h >= 12;
                currentMinute = now.getMinutes();
                renderCalendar();
            };

            // Done / Set Date & Time Button inside Calendar
            popover.querySelector(".jas-dt-done").onclick = (e) => {
                e.stopPropagation();
                applySelection();
            };
        }

        function applySelection() {
            let finalHour24 = hour12 % 12;
            if (isPm) finalHour24 += 12;
            const finalIso = `${selectedDateIso}T${pad2(finalHour24)}:${pad2(currentMinute)}`;

            input.value = finalIso;
            input.dispatchEvent(new Event("input", { bubbles: true }));
            input.dispatchEvent(new Event("change", { bubbles: true }));

            if (input.dataset.onchangeCallback && typeof window[input.dataset.onchangeCallback] === "function") {
                try {
                    var cbLocId = input.dataset.locationId ? Number(input.dataset.locationId) : 0;
                    window[input.dataset.onchangeCallback](finalIso, cbLocId);
                } catch (err) {
                    console.error("Callback error:", err);
                }
            }

            closeActivePopover();
        }

        const parent = input.parentElement;
        if (getComputedStyle(parent).position === "static") {
            parent.style.position = "relative";
        }

        renderCalendar();
        parent.appendChild(popover);
        activePopover = popover;
        activeInput = input;
    }

    // Document-level event delegation: handles ANY datetime-local or data-datetime-picker input
    document.addEventListener("click", function (e) {
        if (activePopover && activePopover.contains(e.target)) {
            return;
        }

        const target = e.target;
        const input = target.closest('input[type="datetime-local"], input[data-datetime-picker]');
        if (input) {
            e.preventDefault();
            e.stopPropagation();
            openDateTimePicker(input);
            return;
        }

        const trigger = target.closest('[data-datetime-trigger]');
        if (trigger) {
            e.preventDefault();
            e.stopPropagation();
            const targetId = trigger.dataset.datetimeTrigger;
            const targetInput = document.getElementById(targetId) || trigger.previousElementSibling;
            if (targetInput) {
                openDateTimePicker(targetInput);
            }
            return;
        }

        if (activePopover && !activePopover.contains(target) && target !== activeInput) {
            closeActivePopover();
        }
    });

    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape" && activePopover) {
            closeActivePopover();
        }
    });

    // Date Corrections Select-All Support (Enhanced Navigation resilient)
    function toggleSelectAllDateCorrections(master) {
        var boxes = document.querySelectorAll('.record-select-cb');
        for (var i = 0; i < boxes.length; i++) {
            boxes[i].checked = master.checked;
        }
        updateSelectedDateCorrectionCount();
    }

    function updateSelectedDateCorrectionCount() {
        var checked = document.querySelectorAll('.record-select-cb:checked');
        var all = document.querySelectorAll('.record-select-cb');
        var badge = document.getElementById('selected-count-badge');
        var btn = document.getElementById('btn-apply-correction');
        var master = document.getElementById('select-all-records');
        if (badge) badge.textContent = checked.length;
        if (btn) btn.textContent = checked.length > 0 ? ('Apply Date Correction (' + checked.length + ')') : 'Apply Date Correction';
        if (master && all.length > 0) {
            master.checked = checked.length === all.length;
            master.indeterminate = checked.length > 0 && checked.length < all.length;
        }
    }

    window.toggleSelectAllDateCorrections = toggleSelectAllDateCorrections;
    window.updateSelectedDateCorrectionCount = updateSelectedDateCorrectionCount;

    // Physical Cash Count Backdating & Expected Cash Fetcher
    function toggleBackdateCount(checkbox, locId) {
        var p = document.getElementById('backdate-count-box');
        if (p) p.classList.toggle('hidden', !checkbox.checked);
        if (checkbox.checked) {
            var inputEl = document.getElementById('count-time');
            if (inputEl && !inputEl.value) {
                var now = new Date();
                var yr = now.getFullYear();
                var mo = ('0' + (now.getMonth() + 1)).slice(-2);
                var da = ('0' + now.getDate()).slice(-2);
                var hr = ('0' + now.getHours()).slice(-2);
                var mi = ('0' + now.getMinutes()).slice(-2);
                inputEl.value = yr + '-' + mo + '-' + da + 'T' + hr + ':' + mi;
                inputEl.dispatchEvent(new Event('input', { bubbles: true }));
                inputEl.dispatchEvent(new Event('change', { bubbles: true }));
            }
            fetchHistoricalExpectedCash(inputEl ? inputEl.value : '', locId);
        }
    }

    function fetchHistoricalExpectedCash(timeVal, locId) {
        var banner = document.getElementById('historical-expected-cash-banner');
        var amountEl = document.getElementById('historical-expected-amount');
        var noteEl = document.getElementById('historical-expected-note');
        var inputEl = document.getElementById('count-time');

        if (!timeVal && inputEl) {
            timeVal = inputEl.value;
        }
        if ((!locId || isNaN(Number(locId))) && inputEl && inputEl.dataset && inputEl.dataset.locationId) {
            locId = Number(inputEl.dataset.locationId);
        }
        if (!locId || isNaN(Number(locId))) {
            var hiddenLoc = document.querySelector('[name="CountInput.CashDrawerId"]') || document.querySelector('[name="CountInput.LocationId"]');
            if (hiddenLoc && !isNaN(Number(hiddenLoc.value))) {
                locId = Number(hiddenLoc.value);
            }
        }
        locId = locId || 0;
        if (!timeVal) {
            if (banner) banner.classList.add('hidden');
            return;
        }
        if (banner && amountEl) {
            banner.classList.remove('hidden');
            amountEl.textContent = 'Calculating…';
            if (noteEl) noteEl.textContent = 'Querying expected book cash as of selected moment…';
        }
        fetch('/api/finance/cash-drawer/expected-at?locationId=' + encodeURIComponent(locId) + '&asOf=' + encodeURIComponent(timeVal), {
            headers: { 'Accept': 'application/json' },
            credentials: 'same-origin'
        })
            .then(function (res) {
                if (!res.ok) throw new Error('HTTP ' + res.status);
                return res.json();
            })
            .then(function (data) {
                if (banner && amountEl && data.expectedCash !== undefined) {
                    amountEl.textContent = 'BDT ' + Number(data.expectedCash).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
                    if (noteEl) noteEl.textContent = 'Based on the accepted anchor before this timestamp and net movements up to this moment.';
                    banner.classList.remove('hidden');
                }
            })
            .catch(function (err) {
                if (banner && amountEl) {
                    amountEl.textContent = 'Unavailable';
                    if (noteEl) noteEl.textContent = 'Could not calculate expected cash: ' + err.message;
                }
            });
    }

    window.toggleBackdateCount = toggleBackdateCount;
    window.fetchHistoricalExpectedCash = fetchHistoricalExpectedCash;

    // Document-level listeners for date correction selection
    document.addEventListener("click", function (e) {
        var th = e.target.closest("#th-date-corrections-select-all");
        if (th) {
            var master = document.getElementById("select-all-records");
            if (master) {
                if (e.target !== master) {
                    master.checked = !master.checked;
                }
                toggleSelectAllDateCorrections(master);
            }
        }
    });

    document.addEventListener("change", function (e) {
        if (e.target && e.target.classList.contains("record-select-cb")) {
            updateSelectedDateCorrectionCount();
        }
        if (e.target && e.target.id === "select-all-records") {
            toggleSelectAllDateCorrections(e.target);
        }
    });

    window.JasDateTimePicker = {
        open: openDateTimePicker,
        close: closeActivePopover
    };
})();
