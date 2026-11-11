// Date Corrections multi-selection helper with Blazor Enhanced Navigation support
(function () {
    function updateSelectedDateCorrectionCount() {
        var checked = document.querySelectorAll('.record-select-cb:checked');
        var all = document.querySelectorAll('.record-select-cb');
        var badge = document.getElementById('selected-count-badge');
        var btn = document.getElementById('btn-apply-correction');
        var master = document.getElementById('select-all-records');

        if (badge) {
            badge.textContent = checked.length;
        }
        if (btn) {
            btn.textContent = checked.length > 0 ? ('Apply Date Correction (' + checked.length + ')') : 'Apply Date Correction';
        }
        if (master && all.length > 0) {
            master.checked = checked.length === all.length;
            master.indeterminate = checked.length > 0 && checked.length < all.length;
        }
    }

    function toggleSelectAll(master) {
        var boxes = document.querySelectorAll('.record-select-cb');
        boxes.forEach(function (b) {
            b.checked = master.checked;
        });
        updateSelectedDateCorrectionCount();
    }

    // Global event delegation
    document.addEventListener('change', function (e) {
        if (!e.target) return;
        if (e.target.id === 'select-all-records') {
            toggleSelectAll(e.target);
        } else if (e.target.classList && e.target.classList.contains('record-select-cb')) {
            updateSelectedDateCorrectionCount();
        }
    });

    document.addEventListener('click', function (e) {
        if (!e.target) return;
        var th = e.target.closest('#th-date-corrections-select-all');
        if (th && e.target.id !== 'select-all-records') {
            var master = document.getElementById('select-all-records');
            if (master) {
                master.checked = !master.checked;
                toggleSelectAll(master);
            }
        }
    });

    // Support Blazor enhanced navigation updates
    document.addEventListener('DOMContentLoaded', updateSelectedDateCorrectionCount);
    window.addEventListener('blazor:navigated', updateSelectedDateCorrectionCount);

    window.updateSelectedDateCorrectionCount = updateSelectedDateCorrectionCount;
    window.toggleSelectAllDateCorrections = toggleSelectAll;
})();
