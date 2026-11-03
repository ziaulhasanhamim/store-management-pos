document.addEventListener("click", event => {
    const printBtn = event.target.closest("[data-print]");
    if (printBtn) {
        const mode = printBtn.getAttribute("data-print") || "thermal";
        document.body.classList.remove("print-mode-thermal", "print-mode-a4");
        document.body.classList.add(`print-mode-${mode}`);
        window.print();
    }
});
