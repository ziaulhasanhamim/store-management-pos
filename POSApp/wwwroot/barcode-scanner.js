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
                playBeep('success');
                await callback.invokeMethodAsync('Scanned', codes[0].rawValue);
                return;
            }
            scanTimer = setTimeout(scan, 150);
        } catch (error) {
            stop();
            playBeep('error');
            await callback.invokeMethodAsync('ScanFailed', 'Camera scanning stopped. Try again or use a hardware scanner.');
        }
    }
    await scan();
}

let audioCtx = null;
function getAudioContext() {
    if (!audioCtx) {
        const AudioContext = window.AudioContext || window.webkitAudioContext;
        if (AudioContext) {
            audioCtx = new AudioContext();
        }
    }
    if (audioCtx && audioCtx.state === 'suspended') {
        audioCtx.resume();
    }
    return audioCtx;
}

// Audio beep removed per user preference (silent operation)
export function playBeep() {
}

const keyboardScanners = new WeakMap();

// Hardware keyboard-wedge barcode scanner interceptor.
// USB and Bluetooth scanners emulate rapid keystrokes (< 35ms between characters) terminated by Enter.
// Intercepting at the window capture phase prevents characters from bleeding into whatever input
// element is currently focused (notes, discounts, payments, or quantity fields).
export function attachKeyboard(input, callback) {
    detachKeyboard(input);
    const state = {
        buffer: '',
        firstAt: 0,
        lastAt: 0,
        isScannerBurst: false,
        timer: undefined,
        queue: Promise.resolve(),
        disposed: false,
        pollutedElement: null,
        originalValue: null
    };

    function finish() {
        clearTimeout(state.timer);
        const code = state.buffer.trim();
        const count = code.length;
        const totalDuration = state.lastAt - state.firstAt;
        const avgInterval = count > 1 ? totalDuration / (count - 1) : 999;
        const wasScanner = count >= 3 && (avgInterval < 45 || state.isScannerBurst);

        // Reset state
        state.buffer = '';
        state.isScannerBurst = false;

        if (!wasScanner) {
            state.pollutedElement = null;
            state.originalValue = null;
            return false;
        }

        // Clean up any text field polluted by the first character before scanner speed was confirmed
        if (state.pollutedElement && state.originalValue !== null) {
            try {
                if (state.pollutedElement.value !== state.originalValue) {
                    state.pollutedElement.value = state.originalValue;
                    state.pollutedElement.dispatchEvent(new Event('input', { bubbles: true }));
                }
            } catch {
                // Ignore detached or read-only elements
            }
            state.pollutedElement = null;
            state.originalValue = null;
        }

        if (input && input.value && input.value.trim() === code) {
            input.value = '';
            input.dispatchEvent(new Event('input', { bubbles: true }));
        }

        state.queue = state.queue.then(async () => {
            if (!state.disposed) {
                await callback.invokeMethodAsync('KeyboardScanned', code);
            }
        }).catch(() => {
            if (!state.disposed) {
                callback.invokeMethodAsync('ScanFailed', 'Scanner reading interrupted. Try scanning again.');
            }
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
            state.isScannerBurst = false;
            return;
        }

        const now = performance.now();
        const delta = now - state.lastAt;

        // If delay is greater than 60ms, this is a fresh sequence
        if (delta > 60) {
            state.buffer = '';
            state.firstAt = now;
            state.isScannerBurst = false;
            const active = document.activeElement;
            if (active && (active.tagName === 'INPUT' || active.tagName === 'TEXTAREA')) {
                state.pollutedElement = active;
                state.originalValue = active.value;
            } else {
                state.pollutedElement = null;
                state.originalValue = null;
            }
        } else if (delta < 35 && state.buffer.length >= 1) {
            // Scanner speed confirmed (< 35ms between characters)
            state.isScannerBurst = true;
        }

        // If confirmed as scanner burst, prevent keystroke bleed into active element
        if (state.isScannerBurst) {
            event.preventDefault();
            event.stopImmediatePropagation();
        }

        state.buffer += event.key;
        state.lastAt = now;
        clearTimeout(state.timer);

        // Fallback for hardware scanners that do not emit Enter suffix: complete after 50ms silence
        state.timer = setTimeout(finish, 55);
    }

    state.keydown = keydown;
    keyboardScanners.set(input, state);
    window.addEventListener('keydown', keydown, true);
}

export function detachKeyboard(input) {
    const state = keyboardScanners.get(input);
    if (!state) return;
    state.disposed = true;
    clearTimeout(state.timer);
    window.removeEventListener('keydown', state.keydown, true);
    keyboardScanners.delete(input);
}
