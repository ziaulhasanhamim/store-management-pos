// Universal Keyboard Shortcuts for Point of Sale (POS) and Purchase (Receiving)
// Full Mac (Option / Command) and Windows / Linux (Alt / Ctrl) compatibility.
//
// On macOS, Option + key generates special typographical characters (e.g. Option+S = ß, Option+Q = œ, Option+P = π, Option+M = µ).
// By checking event.code in the capture phase and invoking event.preventDefault(), we completely suppress the character insertion
// and reliably trigger our shortcuts across all operating systems.
//
// Shortcuts Map:
// - F1 or ?: Open Shortcuts Guide Modal
// - F2 or Option/Alt + S: Focus Product Search Box / Barcode
// - F3 or Option/Alt + Q: Edit Quantity of Last Item
// - F4 or Option/Alt + P: Edit Unit Price / Cost of Last Item
// - F8 or Option/Alt + M: Jump to Payment Section
// - F9 or Ctrl/Cmd + Enter: Complete Sale / Record Purchase
// - Esc: Close Shortcuts Guide / Dialogs

window.addEventListener('keydown', event => {
    // Check if we are on a page that has POS or Receiving elements
    const isPOSOrReceiving = document.getElementById('pos-search-input') ||
                             document.getElementById('receiving-search-input') ||
                             document.getElementById('pos-shortcuts-btn') ||
                             document.getElementById('receiving-shortcuts-btn');
    if (!isPOSOrReceiving) return;

    const activeEl = document.activeElement;
    const isInput = activeEl && ['INPUT', 'TEXTAREA', 'SELECT'].includes(activeEl.tagName);

    // 1. F1, ?, Cmd/Ctrl+/ or Option+/: Toggle Shortcuts Reference
    const isHelpKey = event.key === 'F1' ||
                      (event.key === '?' && !isInput) ||
                      ((event.ctrlKey || event.metaKey) && (event.code === 'Slash' || event.key === '/')) ||
                      (event.altKey && (event.code === 'Slash' || event.key === '÷'));
    if (isHelpKey) {
        const btn = document.getElementById('pos-shortcuts-btn') || document.getElementById('receiving-shortcuts-btn');
        if (btn) {
            event.preventDefault();
            event.stopPropagation();
            btn.click();
            return;
        }
    }

    // 2. F2 or Option/Alt+S or Cmd/Ctrl+Shift+S: Focus Search
    const isSearchKey = event.key === 'F2' ||
                        (event.altKey && (event.code === 'KeyS' || event.key === 's' || event.key === 'S' || event.key === 'ß')) ||
                        ((event.ctrlKey || event.metaKey) && event.shiftKey && event.code === 'KeyS');
    if (isSearchKey) {
        const searchInput = document.getElementById('pos-search-input') || document.getElementById('receiving-search-input');
        if (searchInput) {
            event.preventDefault();
            event.stopPropagation();
            searchInput.focus();
            if (typeof searchInput.select === 'function') searchInput.select();
            return;
        }
    }

    // 3. F3 or Option/Alt+Q or Cmd/Ctrl+Shift+Q: Edit Last Item Quantity
    const isQtyKey = event.key === 'F3' ||
                     (event.altKey && (event.code === 'KeyQ' || event.key === 'q' || event.key === 'Q' || event.key === 'œ')) ||
                     ((event.ctrlKey || event.metaKey) && event.shiftKey && event.code === 'KeyQ');
    if (isQtyKey) {
        const qtyInput = document.getElementById('pos-last-line-qty') || document.getElementById('receiving-last-line-qty');
        if (qtyInput) {
            event.preventDefault();
            event.stopPropagation();
            qtyInput.focus();
            if (typeof qtyInput.select === 'function') qtyInput.select();
            return;
        }
    }

    // 4. F4 or Option/Alt+P / Option/Alt+C or Cmd/Ctrl+Shift+P: Edit Last Item Price / Cost
    const isPriceKey = event.key === 'F4' ||
                       (event.altKey && (event.code === 'KeyP' || event.key === 'p' || event.key === 'P' || event.key === 'π' ||
                                         event.code === 'KeyC' || event.key === 'c' || event.key === 'C' || event.key === 'ç')) ||
                       ((event.ctrlKey || event.metaKey) && event.shiftKey && event.code === 'KeyP');
    if (isPriceKey) {
        const priceInput = document.getElementById('pos-last-line-price') || document.getElementById('receiving-last-line-cost');
        if (priceInput) {
            event.preventDefault();
            event.stopPropagation();
            priceInput.focus();
            if (typeof priceInput.select === 'function') priceInput.select();
            return;
        }
    }

    // 5. F8 or Option/Alt+M or Cmd/Ctrl+Shift+M: Jump to Payment Amount
    const isPayKey = event.key === 'F8' ||
                     (event.altKey && (event.code === 'KeyM' || event.key === 'm' || event.key === 'M' || event.key === 'µ')) ||
                     ((event.ctrlKey || event.metaKey) && event.shiftKey && event.code === 'KeyM');
    if (isPayKey) {
        const payInput = document.getElementById('pos-payment-amount') || document.getElementById('receiving-payment-amount');
        if (payInput) {
            event.preventDefault();
            event.stopPropagation();
            payInput.focus();
            if (typeof payInput.select === 'function') payInput.select();
            return;
        }
    }

    // 6. F9 or Ctrl/Cmd + Enter: Complete Sale / Record Purchase
    const isSubmitKey = event.key === 'F9' ||
                        ((event.ctrlKey || event.metaKey) && (event.key === 'Enter' || event.code === 'Enter'));
    if (isSubmitKey) {
        const submitBtn = document.getElementById('pos-submit-btn') || document.getElementById('receiving-submit-btn');
        if (submitBtn && !submitBtn.disabled) {
            event.preventDefault();
            event.stopPropagation();
            submitBtn.click();
            return;
        }
    }
}, true); // Use capture phase so event.preventDefault() stops character insertion into inputs on Mac

// Master checkbox helper: toggle all row checkboxes in a table
document.addEventListener('change', event => {
    if (event.target && event.target.id === 'select-all-items') {
        const checked = event.target.checked;
        const checkboxes = document.querySelectorAll('input[type="checkbox"][name="Input.ProductIds"], input[type="checkbox"][name="productIds"], input[type="checkbox"][name="ProductIds"]');
        checkboxes.forEach(cb => { cb.checked = checked; });
    }
});

// Bulk edit selected button click handler
document.addEventListener('click', event => {
    const btn = event.target?.closest?.('#bulk-edit-selected-btn');
    if (!btn) return;
    event.preventDefault();
    const checked = Array.from(document.querySelectorAll('input[type="checkbox"][name="Input.ProductIds"]:checked, input[type="checkbox"][name="productIds"]:checked, input[type="checkbox"][name="ProductIds"]:checked'))
        .map(cb => cb.value)
        .filter(val => val && parseInt(val, 10) > 0);
    if (checked.length > 0) {
        window.location.href = '/items/bulk-edit?ids=' + checked.join(',');
    } else {
        window.location.href = '/items/bulk-edit';
    }
});
