// Cash Drawer Count, Live Shortage / Over Calculation, and Historical Backdating
(function () {
    var historicalExpectedCash = null;

    function getBookCash() {
        var form = document.getElementById('cash-count-form');
        if (form && form.dataset && form.dataset.bookCash !== undefined) {
            var val = parseFloat(form.dataset.bookCash);
            if (!isNaN(val)) return val;
        }
        var el = document.querySelector('[data-current-book-cash]');
        if (el && el.dataset && el.dataset.currentBookCash !== undefined) {
            var val = parseFloat(el.dataset.currentBookCash);
            if (!isNaN(val)) return val;
        }
        if (typeof window.currentBookCash === 'number') {
            return window.currentBookCash;
        }
        return 0;
    }

    function recalculateCountVariance() {
        var countInput = document.getElementById('counted-cash');
        var card = document.getElementById('count-variance-card');
        var iconEl = document.getElementById('count-variance-icon');
        var titleEl = document.getElementById('count-variance-title');
        var amountEl = document.getElementById('count-variance-amount');
        var detailEl = document.getElementById('count-variance-detail');
        var notesEl = document.getElementById('count-notes');

        var backdateCheckbox = document.querySelector('input[name="CountInput.IsBackdate"]') || document.getElementById('count-backdate-toggle');
        var isBackdate = backdateCheckbox && backdateCheckbox.checked;

        var histCompRow = document.getElementById('historical-comparison-row');
        var histCompVal = document.getElementById('historical-comparison-val');

        if (!countInput) return;
        var rawVal = countInput.value ? countInput.value.trim() : '';
        var val = parseFloat(rawVal);

        var currentBookCash = getBookCash();
        var expected = (isBackdate && historicalExpectedCash !== null) ? historicalExpectedCash : currentBookCash;

        // If counted cash is empty or invalid
        if (rawVal === '' || isNaN(val) || val < 0) {
            if (card) card.classList.add('hidden');
            if (histCompRow) {
                if (isBackdate && historicalExpectedCash !== null) {
                    histCompRow.classList.remove('hidden');
                    if (histCompVal) {
                        histCompVal.innerHTML = '<span class="text-amber-800 text-xs font-normal">(Enter counted cash above to see shortage/over)</span>';
                    }
                } else {
                    histCompRow.classList.add('hidden');
                }
            }
            return;
        }

        var diff = Math.round((val - expected) * 100) / 100;

        if (card) {
            card.classList.remove('hidden', 'bg-emerald-50', 'border-emerald-300', 'text-emerald-950', 'bg-rose-50', 'border-rose-300', 'text-rose-950', 'bg-blue-50', 'border-blue-300', 'text-blue-950');

            if (diff === 0) {
                card.classList.add('bg-emerald-50', 'border-emerald-300', 'text-emerald-950');
                if (iconEl) iconEl.innerHTML = '<svg class="w-4 h-4 text-emerald-600 inline shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2.5" d="M5 13l4 4L19 7"/></svg>';
                if (titleEl) titleEl.textContent = isBackdate ? 'Balanced (Historical Count)' : 'Cash Count Balanced';
                if (amountEl) amountEl.innerHTML = '<span class="text-emerald-700 font-extrabold text-base">BDT 0.00</span>';
                if (detailEl) {
                    detailEl.textContent = isBackdate
                        ? 'Counted cash exactly matches historical expected cash at the selected count time.'
                        : 'Counted cash exactly matches current book cash balance.';
                }
                if (notesEl && !notesEl.value) {
                    notesEl.placeholder = 'Optional: physical cash matches book cash.';
                }
            } else if (diff < 0) {
                var missingAmount = Math.abs(diff).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
                card.classList.add('bg-rose-50', 'border-rose-300', 'text-rose-950');
                if (iconEl) iconEl.innerHTML = '<svg class="w-4 h-4 text-rose-600 inline shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z"/></svg>';
                if (titleEl) titleEl.textContent = isBackdate ? 'Shortage (Historical Count)' : 'Cash Shortage (Missing Cash)';
                if (amountEl) amountEl.innerHTML = '<span class="text-rose-700 font-extrabold text-base">Shortage: BDT ' + missingAmount + ' missing</span>';
                if (detailEl) {
                    detailEl.textContent = isBackdate
                        ? 'Counted BDT ' + val.toFixed(2) + ' vs Historical Expected BDT ' + expected.toFixed(2) + ' at that time (-BDT ' + missingAmount + ' missing)'
                        : 'Counted BDT ' + val.toFixed(2) + ' vs Current Book BDT ' + expected.toFixed(2) + ' (-BDT ' + missingAmount + ' missing)';
                }
                if (notesEl && !notesEl.value) {
                    notesEl.placeholder = 'Required: explain why cash is short by BDT ' + missingAmount + '...';
                }
            } else {
                var extraAmount = diff.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
                card.classList.add('bg-blue-50', 'border-blue-300', 'text-blue-950');
                if (iconEl) iconEl.innerHTML = '<svg class="w-4 h-4 text-blue-600 inline shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"/></svg>';
                if (titleEl) titleEl.textContent = isBackdate ? 'Over (Historical Count)' : 'Cash Over (Extra Cash)';
                if (amountEl) amountEl.innerHTML = '<span class="text-blue-700 font-extrabold text-base">Over: BDT ' + extraAmount + ' extra</span>';
                if (detailEl) {
                    detailEl.textContent = isBackdate
                        ? 'Counted BDT ' + val.toFixed(2) + ' vs Historical Expected BDT ' + expected.toFixed(2) + ' at that time (+BDT ' + extraAmount + ' extra)'
                        : 'Counted BDT ' + val.toFixed(2) + ' vs Current Book BDT ' + expected.toFixed(2) + ' (+BDT ' + extraAmount + ' extra)';
                }
                if (notesEl && !notesEl.value) {
                    notesEl.placeholder = 'Required: explain why cash has an extra BDT ' + extraAmount + '...';
                }
            }
        }

        // Update the discrepancy row in the backdate banner
        if (histCompRow) {
            if (isBackdate && historicalExpectedCash !== null) {
                histCompRow.classList.remove('hidden');
                if (histCompVal) {
                    if (diff === 0) {
                        histCompVal.innerHTML = '<span class="text-emerald-700 font-bold">Balanced (BDT 0.00)</span>';
                    } else if (diff < 0) {
                        var m = Math.abs(diff).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
                        histCompVal.innerHTML = '<span class="text-rose-700 font-bold">Shortage: BDT ' + m + ' missing</span>';
                    } else {
                        var e = diff.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
                        histCompVal.innerHTML = '<span class="text-blue-700 font-bold">Over: BDT ' + e + ' extra</span>';
                    }
                }
            } else {
                histCompRow.classList.add('hidden');
            }
        }
    }

    function toggleBackdateCount(checkbox, locId) {
        if (!checkbox) {
            checkbox = document.querySelector('input[name="CountInput.IsBackdate"]') || document.getElementById('count-backdate-toggle');
        }
        var p = document.getElementById('backdate-count-box');
        var isChecked = checkbox && checkbox.checked;
        if (p) p.classList.toggle('hidden', !isChecked);

        if (isChecked) {
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
        } else {
            historicalExpectedCash = null;
            var hiddenExp = document.getElementById('historical-expected-cash-input');
            if (hiddenExp) hiddenExp.value = '';
            var banner = document.getElementById('historical-expected-cash-banner');
            if (banner) banner.classList.add('hidden');
            var histCompRow = document.getElementById('historical-comparison-row');
            if (histCompRow) histCompRow.classList.add('hidden');
            recalculateCountVariance();
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
            historicalExpectedCash = null;
            if (banner) banner.classList.add('hidden');
            recalculateCountVariance();
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
            .then(function(res) {
                if (!res.ok) throw new Error('HTTP ' + res.status);
                return res.json();
            })
            .then(function(data) {
                if (data.expectedCash !== undefined) {
                    historicalExpectedCash = Number(data.expectedCash);
                    var hiddenExp = document.getElementById('historical-expected-cash-input');
                    if (hiddenExp) hiddenExp.value = historicalExpectedCash;
                    if (banner && amountEl) {
                        amountEl.textContent = 'BDT ' + Number(data.expectedCash).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
                        if (noteEl) noteEl.textContent = 'Based on the accepted anchor before this timestamp and net movements up to this moment.';
                        banner.classList.remove('hidden');
                    }
                    recalculateCountVariance();
                }
            })
            .catch(function(err) {
                historicalExpectedCash = null;
                if (banner && amountEl) {
                    amountEl.textContent = 'Unavailable';
                    if (noteEl) noteEl.textContent = 'Could not calculate expected cash: ' + err.message;
                }
                recalculateCountVariance();
            });
    }

    function initCashDrawerCount() {
        var countInput = document.getElementById('counted-cash');
        if (!countInput) return;

        var backdateCheckbox = document.querySelector('input[name="CountInput.IsBackdate"]') || document.getElementById('count-backdate-toggle');
        if (backdateCheckbox && backdateCheckbox.checked) {
            var inputEl = document.getElementById('count-time');
            if (inputEl && inputEl.value) {
                fetchHistoricalExpectedCash(inputEl.value);
            }
        }
        recalculateCountVariance();
    }

    // Global event delegation (Enhanced Navigation Resilient)
    document.addEventListener('input', function (e) {
        if (!e.target) return;
        if (e.target.id === 'counted-cash') {
            recalculateCountVariance();
        } else if (e.target.id === 'count-time') {
            fetchHistoricalExpectedCash(e.target.value);
        }
    });

    document.addEventListener('change', function (e) {
        if (!e.target) return;
        if (e.target.name === 'CountInput.IsBackdate' || e.target.id === 'count-backdate-toggle') {
            toggleBackdateCount(e.target);
        } else if (e.target.id === 'count-time') {
            fetchHistoricalExpectedCash(e.target.value);
        } else if (e.target.id === 'counted-cash') {
            recalculateCountVariance();
        }
    });

    // Support both initial load and Blazor enhanced navigation
    document.addEventListener('DOMContentLoaded', initCashDrawerCount);
    window.addEventListener('blazor:navigated', initCashDrawerCount);
    window.addEventListener('blazor:enhancedload', initCashDrawerCount);

    // Export to window
    window.recalculateCountVariance = recalculateCountVariance;
    window.toggleBackdateCount = toggleBackdateCount;
    window.fetchHistoricalExpectedCash = fetchHistoricalExpectedCash;
    window.initCashDrawerCount = initCashDrawerCount;
})();
