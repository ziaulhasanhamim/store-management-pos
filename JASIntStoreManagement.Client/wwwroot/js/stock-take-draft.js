export function read(key) {
    const text = localStorage.getItem(key);
    return text ? JSON.parse(text) : null;
}

export function save(key, draft) {
    localStorage.setItem(key, JSON.stringify(draft));
}

export function clear(key, version) {
    const draft = read(key);
    if (draft?.version === version) {
        localStorage.removeItem(key);
    }
}

export function tabId() {
    const key = 'pos:draft-tab';
    let id = sessionStorage.getItem(key);
    if (!id) {
        id = crypto.randomUUID();
        sessionStorage.setItem(key, id);
    }
    return id;
}
