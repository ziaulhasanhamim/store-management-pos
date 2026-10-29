// Real-time calculation and interaction for Mobile Servicing daily totals.
function formatMoney(amount, currency = 'BDT') {
    const num = Number(amount) || 0;
    return num.toLocaleString('en-US', {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    }) + ' ' + currency;
}

export function updateServicingCalculations() {
    const totalInput = document.getElementById('entry-total');
    const costInput = document.getElementById('entry-cost');
    const commInput = document.getElementById('entry-commission');

    if (!totalInput || !costInput || !commInput) return;

    const total = parseFloat(totalInput.value) || 0;
    const cost = parseFloat(costInput.value) || 0;
    const comm = parseFloat(commInput.value) || 0;

    const profit = Math.max(0, total - cost);
    // Round away from zero to 2 decimals
    const techCut = Math.round(profit * (comm / 100) * 100) / 100;
    const shopProfit = Math.round((profit - techCut) * 100) / 100;

    const currency = window.__STORE_CURRENCY__ || 'BDT';

    const profitEl = document.getElementById('calc-profit');
    const techCutEl = document.getElementById('calc-tech-cut');
    const shopProfitEl = document.getElementById('calc-shop-profit');
    const commLabelEl = document.getElementById('calc-comm-label');
    const warningEl = document.getElementById('calc-warning');

    if (profitEl) profitEl.textContent = formatMoney(profit, currency);
    if (techCutEl) techCutEl.textContent = formatMoney(techCut, currency);
    if (shopProfitEl) shopProfitEl.textContent = formatMoney(shopProfit, currency);
    if (commLabelEl) commLabelEl.textContent = `Technician Cut (${comm.toFixed(comm % 1 === 0 ? 0 : 2)}% taken upfront):`;

    if (warningEl) {
        if (cost > total && total > 0) {
            warningEl.textContent = 'Parts/material cost cannot exceed total servicing amount.';
            warningEl.classList.remove('hidden');
        } else {
            warningEl.classList.add('hidden');
        }
    }
}

export function onTechnicianChanged() {
    const techSelect = document.getElementById('entry-tech');
    if (!techSelect) return;

    const selectedOption = techSelect.options[techSelect.selectedIndex];
    const techId = parseInt(techSelect.value, 10);
    const commInput = document.getElementById('entry-commission');

    // Auto-fill default commission if not in custom edit mode
    const isEditing = document.getElementById('entry-id')?.value;
    if (!isEditing && selectedOption && selectedOption.dataset.commission && commInput) {
        commInput.value = selectedOption.dataset.commission;
    }

    // Check if an existing entry exists for this technician today
    const entriesData = window.__TODAY_SERVICING_ENTRIES__ || {};
    const existing = entriesData[techId];
    if (existing && !existing.isDeposited) {
        window.editServicingEntry(
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
        const totalInput = document.getElementById('entry-total');
        const costInput = document.getElementById('entry-cost');
        if (totalInput && totalInput.value === '') totalInput.value = '';
        if (costInput && costInput.value === '') costInput.value = '';
    }

    updateServicingCalculations();
}

window.editServicingEntry = function(id, techId, total, cost, commission, notes, techName) {
    const idInput = document.getElementById('entry-id');
    const techSelect = document.getElementById('entry-tech');
    const totalInput = document.getElementById('entry-total');
    const costInput = document.getElementById('entry-cost');
    const commInput = document.getElementById('entry-commission');
    const notesInput = document.getElementById('entry-notes');
    const titleEl = document.getElementById('entry-form-title');
    const submitBtn = document.getElementById('entry-submit-btn');
    const cancelBtn = document.getElementById('entry-cancel-btn');

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

    const formSec = document.getElementById('servicing-form-section');
    if (formSec) {
        formSec.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
};

window.cancelServicingEdit = function() {
    const idInput = document.getElementById('entry-id');
    const totalInput = document.getElementById('entry-total');
    const costInput = document.getElementById('entry-cost');
    const notesInput = document.getElementById('entry-notes');
    const titleEl = document.getElementById('entry-form-title');
    const submitBtn = document.getElementById('entry-submit-btn');
    const cancelBtn = document.getElementById('entry-cancel-btn');

    if (idInput) idInput.value = '';
    if (totalInput) totalInput.value = '';
    if (costInput) costInput.value = '';
    if (notesInput) notesInput.value = '';

    if (titleEl) titleEl.textContent = 'Record technician daily totals';
    if (submitBtn) submitBtn.textContent = 'Save daily servicing';
    if (cancelBtn) cancelBtn.classList.add('hidden');

    const techSelect = document.getElementById('entry-tech');
    if (techSelect) {
        const opt = techSelect.options[techSelect.selectedIndex];
        if (opt && opt.dataset.commission) {
            const commInput = document.getElementById('entry-commission');
            if (commInput) commInput.value = opt.dataset.commission;
        }
    }

    updateServicingCalculations();
};

// Delegated listeners for instant real-time calculation on input
document.addEventListener('input', event => {
    const id = event.target?.id;
    if (id === 'entry-total' || id === 'entry-cost' || id === 'entry-commission') {
        updateServicingCalculations();
    }
});

document.addEventListener('change', event => {
    const id = event.target?.id;
    if (id === 'entry-tech') {
        onTechnicianChanged();
    } else if (id === 'entry-total' || id === 'entry-cost' || id === 'entry-commission') {
        updateServicingCalculations();
    }
});

// Run calculation on enhanced navigation and page load
if (typeof window !== 'undefined') {
    window.addEventListener('DOMContentLoaded', updateServicingCalculations);
    if (window.Blazor) {
        window.Blazor.addEventListener('enhancedload', updateServicingCalculations);
    }
}
