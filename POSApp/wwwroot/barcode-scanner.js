let activeStream;
let scanTimer;
let generation = 0;

export function stop() {
    generation++;
    clearTimeout(scanTimer);
    activeStream?.getTracks().forEach(track => track.stop());
    activeStream = undefined;
}

export async function start(video, callback) {
    stop();
    const session = generation;
    if (!('BarcodeDetector' in window)) {
        throw new Error('Camera barcode detection is unavailable in this browser. Use a USB/Bluetooth scanner or type the barcode.');
    }
    const formats = await BarcodeDetector.getSupportedFormats();
    const supported = ['ean_8', 'ean_13', 'code_128', 'upc_a', 'upc_e'].filter(format => formats.includes(format));
    if (!supported.length) {
        throw new Error('No supported retail barcode format is available. Use a hardware scanner instead.');
    }
    const detector = new BarcodeDetector({ formats: supported });
    const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } }, audio: false });
    if (session !== generation) {
        stream.getTracks().forEach(track => track.stop());
        return;
    }
    activeStream = stream;
    video.srcObject = activeStream;
    await video.play();
    const currentStream = activeStream;
    async function scan() {
        if (activeStream !== currentStream) return;
        try {
            const codes = await detector.detect(video);
            if (codes.length) {
                stop();
                await callback.invokeMethodAsync('Scanned', codes[0].rawValue);
                return;
            }
            scanTimer = setTimeout(scan, 150);
        } catch (error) {
            stop();
            await callback.invokeMethodAsync('ScanFailed', 'Camera scanning stopped. Try again or use a hardware scanner.');
        }
    }
    await scan();
}

const keyboardScanners = new WeakMap();

// Keyboard-wedge scanners emit a fast burst. Capture its boundary locally so the
// next scan can begin while the previous barcode lookup is still on the server.
export function attachKeyboard(input, callback) {
    detachKeyboard(input);
    const state = { buffer: '', firstAt: 0, lastAt: 0, timer: undefined, queue: Promise.resolve(), disposed: false };
    function finish() {
        clearTimeout(state.timer);
        const code = state.buffer;
        const fast = code.length >= 3 && (state.lastAt - state.firstAt) / (code.length - 1) < 40;
        state.buffer = '';
        if (!fast || input.value.trim() !== code) return false;
        input.value = '';
        input.dispatchEvent(new Event('input', { bubbles: true }));
        state.queue = state.queue.then(async () => {
            if (!state.disposed) await callback.invokeMethodAsync('KeyboardScanned', code);
        }).catch(error => {
            if (!state.disposed) callback.invokeMethodAsync('ScanFailed', 'Scanning stopped. Search for the item or scan again.');
        });
        return true;
    }
    function keydown(event) {
        if (event.key === 'Enter') {
            if (finish()) {
                event.preventDefault();
                event.stopImmediatePropagation();
            }
            return;
        }
        if (event.key.length !== 1 || event.ctrlKey || event.altKey || event.metaKey) {
            clearTimeout(state.timer);
            state.buffer = '';
            return;
        }
        const now = performance.now();
        if (now - state.lastAt > 60) {
            state.buffer = '';
            state.firstAt = now;
        }
        state.buffer += event.key;
        state.lastAt = now;
        clearTimeout(state.timer);
        state.timer = setTimeout(finish, 90);
    }
    state.keydown = keydown;
    keyboardScanners.set(input, state);
    input.addEventListener('keydown', keydown, true);
}

export function detachKeyboard(input) {
    const state = keyboardScanners.get(input);
    if (!state) return;
    state.disposed = true;
    clearTimeout(state.timer);
    input.removeEventListener('keydown', state.keydown, true);
    keyboardScanners.delete(input);
}
