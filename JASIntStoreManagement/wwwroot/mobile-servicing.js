// Real-time calculation and interaction for Mobile Servicing daily totals.
(function () {
    function getStoreCurrency() {
        if (window.__STORE_CURRENCY__) return window.__STORE_CURRENCY__;
        var metaEl = document.getElementById('servicing-entries-metadata');
        if (metaEl && metaEl.dataset.currency) return metaEl.dataset.currency;
        return 'BDT';
    }

    function getEntriesData() {
        if (window.__TODAY_SERVICING_ENTRIES__) {
            return window.__TODAY_SERVICING_ENTRIES__;
        }
        var metaEl = document.getElementById('servicing-entries-metadata');
        if (metaEl && metaEl.dataset.entries) {
            try {
                return JSON.parse(metaEl.dataset.entries);
            } catch (e) {
                return {};
            }
        }
        return {};
    }

    function formatMoney(amount, currency) {
        currency = currency || getStoreCurrency();
        var num = Number(amount) || 0;
        return num.toLocaleString('en-US', {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        }) + ' ' + currency;
    }

    function updateServicingCalculations() {
        var totalInput = document.getElementById('entry-total');
        var costInput = document.getElementById('entry-cost');
        var commInput = document.getElementById('entry-commission');

        if (!totalInput || !costInput || !commInput) return;

        var total = parseFloat(totalInput.value) || 0;
        var cost = parseFloat(costInput.value) || 0;
        var comm = parseFloat(commInput.value);
        if (isNaN(comm)) comm = 50;

        var profit = Math.max(0, total - cost);
        // Round away from zero to 2 decimals
        var techCut = Math.round(profit * (comm / 100) * 100) / 100;
        var shopProfit = Math.round((profit - techCut) * 100) / 100;

        var currency = getStoreCurrency();

        var profitEl = document.getElementById('calc-profit');
        var techCutEl = document.getElementById('calc-tech-cut');
        var shopProfitEl = document.getElementById('calc-shop-profit');
        var commLabelEl = document.getElementById('calc-comm-label');
        var warningEl = document.getElementById('calc-warning');
        var badgeEl = document.getElementById('calc-badge');
        var hintEl = document.getElementById('calc-hint');

        if (profitEl) profitEl.textContent = formatMoney(profit, currency);
        if (techCutEl) techCutEl.textContent = formatMoney(techCut, currency);
        if (shopProfitEl) shopProfitEl.textContent = formatMoney(shopProfit, currency);
        if (commLabelEl) commLabelEl.textContent = 'Technician Cut (' + (comm % 1 === 0 ? comm.toFixed(0) : comm.toFixed(1)) + '% taken upfront):';

        if (badgeEl) {
            if (total > 0) {
                badgeEl.textContent = 'Calculated';
                badgeEl.className = 'badge badge-success text-[10px]';
            } else {
                badgeEl.textContent = 'Live Preview';
                badgeEl.className = 'badge badge-neutral text-[10px]';
            }
        }

        if (hintEl) {
            if (total > 0) {
                hintEl.classList.add('hidden');
            } else {
                hintEl.classList.remove('hidden');
            }
        }

        if (warningEl) {
            if (cost > total && total > 0) {
                warningEl.textContent = 'Parts/material cost cannot exceed total servicing amount.';
                warningEl.classList.remove('hidden');
            } else {
                warningEl.classList.add('hidden');
            }
        }
    }

    function onTechnicianChanged() {
        var techSelect = document.getElementById('entry-tech');
        if (!techSelect) return;

        var selectedOption = techSelect.options[techSelect.selectedIndex];
        var techId = parseInt(techSelect.value, 10);
        var commInput = document.getElementById('entry-commission');
        var locInput = document.getElementById('entry-location-id');
        var locHint = document.getElementById('tech-assigned-loc');

        if (selectedOption) {
            if (locHint && selectedOption.dataset.locationName) {
                locHint.textContent = selectedOption.dataset.locationName;
            }
            if (locInput && selectedOption.dataset.locationId) {
                locInput.value = selectedOption.dataset.locationId;
            }
        }

        // Auto-fill default commission if not in custom edit mode
        var isEditing = document.getElementById('entry-id') ? document.getElementById('entry-id').value : '';
        if (!isEditing && selectedOption && selectedOption.dataset.commission && commInput) {
            commInput.value = selectedOption.dataset.commission;
        }

        // Check if an existing entry exists for this technician today
        var entriesData = getEntriesData();
        var existing = entriesData[techId] || entriesData[techId.toString()];
        if (existing && !existing.isDeposited) {
            editServicingEntry(
                existing.id,
                existing.technicianId,
                existing.totalAmount,
                existing.totalCost,
                existing.commissionRate,
                existing.notes || '',
                existing.technicianName || ''
            );
            return;
        }

        if (!isEditing) {
            var totalInput = document.getElementById('entry-total');
            var costInput = document.getElementById('entry-cost');
            if (totalInput && totalInput.value === '') totalInput.value = '';
            if (costInput && costInput.value === '') costInput.value = '';
        }

        updateServicingCalculations();
    }

    function editServicingEntry(id, techId, total, cost, commission, notes, techName) {
        var idInput = document.getElementById('entry-id');
        var techSelect = document.getElementById('entry-tech');
        var totalInput = document.getElementById('entry-total');
        var costInput = document.getElementById('entry-cost');
        var commInput = document.getElementById('entry-commission');
        var notesInput = document.getElementById('entry-notes');
        var titleEl = document.getElementById('entry-form-title');
        var submitBtn = document.getElementById('entry-submit-btn');
        var cancelBtn = document.getElementById('entry-cancel-btn');

        if (idInput) idInput.value = id || '';
        if (techSelect) techSelect.value = techId;
        if (totalInput) totalInput.value = total > 0 ? total : '';
        if (costInput) costInput.value = cost > 0 ? cost : '';
        if (commInput) commInput.value = commission;
        if (notesInput) notesInput.value = notes || '';

        if (id) {
            if (titleEl) titleEl.textContent = 'Edit daily servicing' + (techName ? ': ' + techName : '');
            if (submitBtn) submitBtn.textContent = 'Update daily servicing';
            if (cancelBtn) cancelBtn.classList.remove('hidden');
        } else {
            if (titleEl) titleEl.textContent = 'Record technician daily totals';
            if (submitBtn) submitBtn.textContent = 'Save daily servicing';
            if (cancelBtn) cancelBtn.classList.add('hidden');
        }

        updateServicingCalculations();

        var formSec = document.getElementById('servicing-form-section');
        if (formSec) {
            formSec.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
    }

    function cancelServicingEdit() {
        var idInput = document.getElementById('entry-id');
        var totalInput = document.getElementById('entry-total');
        var costInput = document.getElementById('entry-cost');
        var notesInput = document.getElementById('entry-notes');
        var titleEl = document.getElementById('entry-form-title');
        var submitBtn = document.getElementById('entry-submit-btn');
        var cancelBtn = document.getElementById('entry-cancel-btn');

        if (idInput) idInput.value = '';
        if (totalInput) totalInput.value = '';
        if (costInput) costInput.value = '';
        if (notesInput) notesInput.value = '';

        if (titleEl) titleEl.textContent = 'Record technician daily totals';
        if (submitBtn) submitBtn.textContent = 'Save daily servicing';
        if (cancelBtn) cancelBtn.classList.add('hidden');

        var techSelect = document.getElementById('entry-tech');
        if (techSelect) {
            var opt = techSelect.options[techSelect.selectedIndex];
            if (opt && opt.dataset.commission) {
                var commInput = document.getElementById('entry-commission');
                if (commInput) commInput.value = opt.dataset.commission;
            }
        }

        updateServicingCalculations();
    }

    // Expose all functions on window
    window.updateServicingCalculations = updateServicingCalculations;
    window.onTechnicianChanged = onTechnicianChanged;
    window.editServicingEntry = editServicingEntry;
    window.cancelServicingEdit = cancelServicingEdit;

    // Delegated event listeners for instant real-time calculation
    function handleInputEvent(event) {
        var id = event.target ? event.target.id : '';
        if (id === 'entry-total' || id === 'entry-cost' || id === 'entry-commission') {
            updateServicingCalculations();
        }
    }

    document.addEventListener('input', handleInputEvent, true);
    document.addEventListener('keyup', handleInputEvent, true);
    document.addEventListener('paste', function () {
        setTimeout(updateServicingCalculations, 10);
    }, true);

    document.addEventListener('change', function (event) {
        var id = event.target ? event.target.id : '';
        if (id === 'entry-tech') {
            onTechnicianChanged();
        } else if (id === 'entry-total' || id === 'entry-cost' || id === 'entry-commission') {
            updateServicingCalculations();
        }
    }, true);

    // Initial run & Blazor enhanced navigation binding
    function attachBlazor() {
        if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
            window.Blazor.addEventListener('enhancedload', function () {
                updateServicingCalculations();
            });
            return true;
        }
        return false;
    }

    if (!attachBlazor()) {
        var attempts = 0;
        var interval = setInterval(function () {
            attempts++;
            if (attachBlazor() || attempts > 30) {
                clearInterval(interval);
            }
        }, 150);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', updateServicingCalculations);
    } else {
        updateServicingCalculations();
    }
    window.addEventListener('pageshow', updateServicingCalculations);
})();
