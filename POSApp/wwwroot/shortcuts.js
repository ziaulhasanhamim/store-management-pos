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

function updateBulkEditLinkHref() {
    const link = document.getElementById('bulk-edit-selected-btn');
    if (!link) return;
    const checked = Array.from(document.querySelectorAll('input[type="checkbox"][name="Input.ProductIds"]:checked, input[type="checkbox"][name="productIds"]:checked, input[type="checkbox"][name="ProductIds"]:checked'))
        .map(cb => cb.value)
        .filter(val => val && parseInt(val, 10) > 0);
    const targetUrl = checked.length > 0 ? ('/items/bulk-edit?ids=' + checked.join(',')) : '/items/bulk-edit';
    link.setAttribute('href', targetUrl);
    link.href = targetUrl;
}

// Master checkbox helper: toggle all row checkboxes in a table
document.addEventListener('change', event => {
    if (event.target && event.target.id === 'select-all-items') {
        const checked = event.target.checked;
        const checkboxes = document.querySelectorAll('input[type="checkbox"][name="Input.ProductIds"], input[type="checkbox"][name="productIds"], input[type="checkbox"][name="ProductIds"]');
        checkboxes.forEach(cb => { cb.checked = checked; });
    }
    updateBulkEditLinkHref();
});

// Bulk edit selected button click handler: ensures href is set in capture phase so Blazor enhanced navigation handles it smoothly
document.addEventListener('click', event => {
    const link = event.target?.closest?.('#bulk-edit-selected-btn');
    if (!link) return;
    updateBulkEditLinkHref();
}, true);

// Sidebar Single-Open Accordion: when a group is opened, close sibling groups
document.addEventListener('toggle', event => {
    const targetGroup = event.target;
    if (targetGroup && targetGroup.matches && targetGroup.matches('.sidebar-group') && targetGroup.open) {
        const allGroups = document.querySelectorAll('.sidebar-group[open]');
        allGroups.forEach(group => {
            if (group !== targetGroup) {
                group.open = false;
            }
        });
    }
}, true); // Capture phase is required because 'toggle' event does not bubble

// Mobile Drawer Auto-Close: dismiss drawer overlay upon selecting any navigation link
document.addEventListener('click', event => {
    const link = event.target?.closest?.('.sidebar nav a[href]');
    if (!link) return;
    if (window.innerWidth < 1024) {
        const toggle = document.getElementById('mobile-nav-toggle');
        if (toggle && toggle.checked) {
            toggle.checked = false;
        }
    }
});

// Column Filter Popover: close open popovers on outside click or Escape
document.addEventListener('click', event => {
    const openFilters = document.querySelectorAll('.col-filter-details[open]');
    openFilters.forEach(filter => {
        if (!filter.contains(event.target)) {
            filter.removeAttribute('open');
        }
    });
});

document.addEventListener('keydown', event => {
    if (event.key === 'Escape') {
        const openFilters = document.querySelectorAll('.col-filter-details[open]');
        openFilters.forEach(filter => {
            filter.removeAttribute('open');
        });
    }
});

// Column Filter Popover: Apply filter by reading input and updating query params via Blazor enhanced navigation
document.addEventListener('click', event => {
    const applyBtn = event.target?.closest?.('.col-filter-apply');
    if (!applyBtn) return;
    const card = applyBtn.closest('.col-filter-card');
    const input = card?.querySelector?.('[data-col-filter]');
    if (!input) return;
    const param = input.getAttribute('data-col-filter');
    const val = input.value.trim();
    const url = new URL(window.location.href);
    if (val) {
        url.searchParams.set(param, val);
    } else {
        url.searchParams.delete(param);
    }
    url.searchParams.set('page', '1');
    if (window.Blazor && window.Blazor.navigateTo) {
        window.Blazor.navigateTo(url.pathname + url.search);
    } else {
        window.location.href = url.pathname + url.search;
    }
});

document.addEventListener('keydown', event => {
    if (event.key === 'Enter') {
        const input = event.target?.closest?.('.col-filter-card [data-col-filter]');
        if (input) {
            event.preventDefault();
            const card = input.closest('.col-filter-card');
            const applyBtn = card?.querySelector?.('.col-filter-apply');
            if (applyBtn) {
                applyBtn.click();
            }
        }
    }
});

// Synchronized top horizontal scrollbar for wide tables
function initTableScrollSync() {
    const tableScrollContainers = document.querySelectorAll('.table-scroll');
    tableScrollContainers.forEach(container => {
        let topScroll = container.parentElement ? container.parentElement.querySelector(':scope > .table-top-scroll') : null;

        if (container.scrollWidth <= container.clientWidth + 2) {
            if (topScroll) topScroll.style.display = 'none';
            return;
        }

        if (!topScroll) {
            topScroll = document.createElement('div');
            topScroll.className = 'table-top-scroll';
            const inner = document.createElement('div');
            inner.className = 'table-top-scroll-inner';
            topScroll.appendChild(inner);
            container.parentElement.insertBefore(topScroll, container);
        } else {
            topScroll.style.display = 'block';
        }

        const inner = topScroll.firstElementChild;
        if (inner) {
            inner.style.width = container.scrollWidth + 'px';
        }

        let isSyncingTop = false;
        let isSyncingBottom = false;

        topScroll.onscroll = () => {
            if (isSyncingBottom) { isSyncingBottom = false; return; }
            isSyncingTop = true;
            container.scrollLeft = topScroll.scrollLeft;
        };

        container.onscroll = () => {
            if (isSyncingTop) { isSyncingTop = false; return; }
            isSyncingBottom = true;
            topScroll.scrollLeft = container.scrollLeft;
        };
    });
}

window.addEventListener('DOMContentLoaded', initTableScrollSync);
window.addEventListener('load', initTableScrollSync);
window.addEventListener('resize', initTableScrollSync);
document.addEventListener('blazor:navigated', () => {
    setTimeout(initTableScrollSync, 60);
});


