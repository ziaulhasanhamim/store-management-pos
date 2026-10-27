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
