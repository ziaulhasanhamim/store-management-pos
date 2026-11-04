// Universal Keyboard Shortcuts for Point of Sale (POS) and Purchase (Receiving)
// F1 or ?: Toggle Shortcuts Reference Guide
// F2 or Alt+S: Focus Product Search Box / Barcode
// F3 or Alt+Q: Edit Quantity of Last Item
// F4 or Alt+P: Edit Unit Price / Cost of Last Item
// F8 or Alt+M: Jump to Payment Section
// F9 or Ctrl+Enter / Cmd+Enter: Complete Sale / Record Purchase

document.addEventListener('keydown', event => {
    // Only handle if we are on POS or Receiving pages
    const isPOSOrReceiving = document.getElementById('pos-search-input') ||
                             document.getElementById('receiving-search-input') ||
                             document.getElementById('pos-shortcuts-btn') ||
                             document.getElementById('receiving-shortcuts-btn');
    if (!isPOSOrReceiving) return;

    const activeEl = document.activeElement;
    const isInput = activeEl && ['INPUT', 'TEXTAREA', 'SELECT'].includes(activeEl.tagName);

    // F1 or ? (outside input): Toggle Shortcuts Reference
    if (event.key === 'F1' || (event.key === '?' && !isInput)) {
        const btn = document.getElementById('pos-shortcuts-btn') || document.getElementById('receiving-shortcuts-btn');
        if (btn) {
            event.preventDefault();
            btn.click();
            return;
        }
    }

    // F2 or Alt+S: Focus Search
    if (event.key === 'F2' || (event.altKey && (event.key === 's' || event.key === 'S'))) {
        const searchInput = document.getElementById('pos-search-input') || document.getElementById('receiving-search-input');
        if (searchInput) {
            event.preventDefault();
            searchInput.focus();
            if (typeof searchInput.select === 'function') searchInput.select();
            return;
        }
    }

    // F3 or Alt+Q: Edit Last Item Quantity
    if (event.key === 'F3' || (event.altKey && (event.key === 'q' || event.key === 'Q'))) {
        const qtyInput = document.getElementById('pos-last-line-qty') || document.getElementById('receiving-last-line-qty');
        if (qtyInput) {
            event.preventDefault();
            qtyInput.focus();
            if (typeof qtyInput.select === 'function') qtyInput.select();
            return;
        }
    }

    // F4 or Alt+P / Alt+C: Edit Last Item Price / Cost
    if (event.key === 'F4' || (event.altKey && (event.key === 'p' || event.key === 'P' || event.key === 'c' || event.key === 'C'))) {
        const priceInput = document.getElementById('pos-last-line-price') || document.getElementById('receiving-last-line-cost');
        if (priceInput) {
            event.preventDefault();
            priceInput.focus();
            if (typeof priceInput.select === 'function') priceInput.select();
            return;
        }
    }

    // F8 or Alt+M: Jump to Payment Amount
    if (event.key === 'F8' || (event.altKey && (event.key === 'm' || event.key === 'M'))) {
        const payInput = document.getElementById('pos-payment-amount') || document.getElementById('receiving-payment-amount');
        if (payInput) {
            event.preventDefault();
            payInput.focus();
            if (typeof payInput.select === 'function') payInput.select();
            return;
        }
    }

    // F9 or Ctrl+Enter / Cmd+Enter: Complete Sale / Record Purchase
    if (event.key === 'F9' || ((event.ctrlKey || event.metaKey) && event.key === 'Enter')) {
        const submitBtn = document.getElementById('pos-submit-btn') || document.getElementById('receiving-submit-btn');
        if (submitBtn && !submitBtn.disabled) {
            event.preventDefault();
            submitBtn.click();
            return;
        }
    }
});
