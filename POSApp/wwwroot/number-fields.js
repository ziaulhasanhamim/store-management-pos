const selectAfterMouseUp = new WeakSet();

function selectableInput(target) {
    return target instanceof HTMLInputElement
        && target.dataset.selectOnFocus === "true";
}

document.addEventListener("mousedown", event => {
    if (selectableInput(event.target) && document.activeElement !== event.target) {
        selectAfterMouseUp.add(event.target);
    }
});

document.addEventListener("focusin", event => {
    if (selectableInput(event.target)) {
        event.target.select();
    }
});

document.addEventListener("mouseup", event => {
    if (selectableInput(event.target) && selectAfterMouseUp.has(event.target)) {
        selectAfterMouseUp.delete(event.target);
        event.preventDefault();
        event.target.select();
    }
});

// Live cash count difference calculation for drawer closing
document.addEventListener("input", event => {
    const input = event.target;
    if (!(input instanceof HTMLInputElement) || !("cashCount" in input.dataset)) {
        return;
    }
    const container = input.closest("form") || input.parentElement;
    const diffEl = container?.querySelector("[data-cash-count-diff]");
    if (!diffEl) return;

    const val = parseFloat(input.value);
    const expected = parseFloat(input.dataset.expected || "0");
    const currency = input.dataset.currency || "BDT";

    if (isNaN(val) || val < 0) {
        diffEl.classList.add("hidden");
        return;
    }

    const diff = Math.round((val - expected) * 100) / 100;
    diffEl.classList.remove("hidden", "text-emerald-700", "text-amber-700");

    if (diff === 0) {
        diffEl.classList.add("text-emerald-700");
        diffEl.textContent = `Balanced · ${currency} 0.00`;
    } else if (diff < 0) {
        diffEl.classList.add("text-amber-700");
        diffEl.textContent = `Shortage · ${currency} ${Math.abs(diff).toFixed(2)}`;
    } else {
        diffEl.classList.add("text-amber-700");
        diffEl.textContent = `Overage · ${currency} ${Math.abs(diff).toFixed(2)}`;
    }
});

// Payroll live employee preview and after-payment remaining calculation
function updatePayrollPreview(container) {
    if (!container) return;
    const empSelect = container.querySelector("[data-payroll-employee]");
    const summary = container.querySelector("[data-payroll-summary]");
    const warning = container.querySelector("[data-summary-warning]");
    const preview = container.querySelector("[data-payroll-preview]");
    const kindSelect = container.querySelector("[data-payroll-kind]");
    const amountInput = container.querySelector("[data-payroll-amount]");
    const currency = container.dataset.currency || "BDT";

    const opt = empSelect?.selectedOptions?.[0];
    if (!opt || !opt.value) {
        if (summary) summary.classList.add("hidden");
        if (preview) preview.classList.add("hidden");
        return;
    }

    if (summary) summary.classList.remove("hidden");

    const salary = parseFloat(opt.dataset.salary || "0");
    const hasSalary = opt.dataset.hasSalary === "true";
    const broughtForward = parseFloat(opt.dataset.broughtForward || "0");
    const advance = parseFloat(opt.dataset.advance || "0");
    const paid = parseFloat(opt.dataset.paid || "0");
    const remaining = parseFloat(opt.dataset.remaining || "0");

    const salEl = summary.querySelector("[data-summary-salary]");
    if (salEl) salEl.textContent = `${currency} ${salary.toFixed(2)}${hasSalary ? "" : " (not created)"}`;

    const bfEl = summary.querySelector("[data-summary-brought-forward]");
    if (bfEl) bfEl.textContent = `${currency} ${broughtForward.toFixed(2)}`;

    const advEl = summary.querySelector("[data-summary-advance]");
    if (advEl) advEl.textContent = `${currency} ${advance.toFixed(2)}`;

    const paidEl = summary.querySelector("[data-summary-paid]");
    if (paidEl) paidEl.textContent = `${currency} ${paid.toFixed(2)}`;

    const remLabel = summary.querySelector("[data-summary-remaining-label]");
    if (remLabel) remLabel.textContent = remaining < 0 ? "Advance carried forward" : "Salary left to pay";

    const remEl = summary.querySelector("[data-summary-remaining]");
    if (remEl) remEl.textContent = `${currency} ${Math.abs(remaining).toFixed(2)}`;

    if (warning) {
        if (hasSalary) warning.classList.add("hidden");
        else warning.classList.remove("hidden");
    }

    // After payment preview
    const amountVal = parseFloat(amountInput?.value || "0");
    const kind = kindSelect?.value || "Salary";

    if (!isNaN(amountVal) && amountVal > 0 && preview) {
        const afterRemaining = kind === "Bonus" ? remaining : remaining - amountVal;
        preview.classList.remove("hidden");
        preview.textContent = `After this payment: ${afterRemaining < 0 ? "Advance " + currency + " " + Math.abs(afterRemaining).toFixed(2) : "Salary left " + currency + " " + afterRemaining.toFixed(2)}`;
    } else if (preview) {
        preview.classList.add("hidden");
    }
}

document.addEventListener("change", event => {
    const target = event.target;
    if (target instanceof HTMLSelectElement && (target.matches("[data-payroll-employee]") || target.matches("[data-payroll-kind]"))) {
        updatePayrollPreview(target.closest("[data-payroll-container]"));
    }
});

document.addEventListener("input", event => {
    const target = event.target;
    if (target instanceof HTMLInputElement && target.matches("[data-payroll-amount]")) {
        updatePayrollPreview(target.closest("[data-payroll-container]"));
    }
});
