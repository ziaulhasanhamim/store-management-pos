document.addEventListener("click", event => {
    // Format View Switcher (A4 vs Thermal preview on screen)
    const viewBtn = event.target.closest("[data-view-mode]");
    if (viewBtn) {
        const mode = viewBtn.getAttribute("data-view-mode");
        const a4Sheet = document.getElementById("invoice-a4-sheet");
        const thermalSheet = document.getElementById("invoice-thermal-sheet");
        const allBtns = document.querySelectorAll("[data-view-mode]");

        allBtns.forEach(btn => {
            if (btn === viewBtn) {
                btn.classList.add("bg-white", "text-slate-900", "shadow-sm");
                btn.classList.remove("text-slate-600", "hover:text-slate-900");
            } else {
                btn.classList.remove("bg-white", "text-slate-900", "shadow-sm");
                btn.classList.add("text-slate-600", "hover:text-slate-900");
            }
        });

        if (mode === "thermal") {
            if (a4Sheet) a4Sheet.style.setProperty("display", "none", "important");
            if (thermalSheet) thermalSheet.style.setProperty("display", "block", "important");
        } else {
            if (a4Sheet) a4Sheet.style.setProperty("display", "block", "important");
            if (thermalSheet) thermalSheet.style.setProperty("display", "none", "important");
        }
    }

    // Print Button Action (A4 vs Thermal roll)
    const printBtn = event.target.closest("[data-print]");
    if (printBtn) {
        const mode = printBtn.getAttribute("data-print") || "a4";
        document.body.classList.remove("print-mode-thermal", "print-mode-a4");
        document.body.classList.add(`print-mode-${mode}`);
        window.print();
    }
});
