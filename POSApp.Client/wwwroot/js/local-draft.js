export function get(key) {
    try {
        return localStorage.getItem(key);
    } catch {
        return null;
    }
}

export function set(key, value) {
    try {
        localStorage.setItem(key, value);
    } catch (e) {
        console.warn('LocalStorage unavailable or quota exceeded', e);
    }
}

export function remove(key) {
    try {
        localStorage.removeItem(key);
    } catch {}
}
