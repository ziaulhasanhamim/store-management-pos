// SSR business forms opt in. Interactive editors use EditorDraft in C#.
export function initialize(blazor) {
    const attached = new WeakMap();
    const tabKey = 'pos:draft-tab';
    let tabId;
    try {
        tabId = sessionStorage.getItem(tabKey) ?? crypto.randomUUID();
        sessionStorage.setItem(tabKey, tabId);
    } catch {
        tabId = crypto.randomUUID();
    }
    let identity;
    const owner = () => document.querySelector('[data-draft-owner]')?.dataset.draftOwner
        ? Promise.resolve(document.querySelector('[data-draft-owner]').dataset.draftOwner)
        : identity ??= fetch('/api/drafts/identity', { credentials: 'same-origin' })
        .then(response => {
            if (!response.ok) throw new Error('Sign in again to save drafts.');
            return response.json();
        });
    const read = key => {
        try { return JSON.parse(localStorage.getItem(key) ?? 'null'); }
        catch { return null; }
    };
    const localSave = (key, value, status) => {
        try { localStorage.setItem(key, JSON.stringify(value)); }
        catch { status.textContent = 'Browser recovery unavailable. Wait for server save before leaving.'; }
    };
    const serverRead = async kind => {
        const response = await fetch(`/api/drafts/${encodeURIComponent(kind)}`, { credentials: 'same-origin', cache: 'no-store' });
        if (!response.ok) throw new Error('Server draft unavailable. Local data is kept.');
        return response.status === 204 ? null : response.json();
    };
    const serverSave = async (kind, input) => {
        const tokenResponse = await fetch('/api/entries/antiforgery', { credentials: 'same-origin', cache: 'no-store' });
        if (!tokenResponse.ok) throw new Error('Sign in again to save this draft.');
        const token = await tokenResponse.json();
        const response = await fetch(`/api/drafts/${encodeURIComponent(kind)}`, {
            method: 'POST', credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token.token },
            body: JSON.stringify(input)
        });
        if (!response.ok) {
            const error = await response.json().catch(() => null);
            throw new Error(error?.message ?? 'Draft changed in another tab. Local data is kept; refresh to review.');
        }
        return response.json();
    };
    const fields = form => [...form.elements].filter(element => element.name
        && !['__RequestVerificationToken', '__WorkingDay', '_handler'].includes(element.name)
        && !['password', 'file', 'submit'].includes(element.type) && !element.disabled);
    const capture = form => JSON.stringify(fields(form).map(element => ({
        name: element.name, value: element.value, checked: element.checked
    })));
    const restore = (form, payload) => {
        if (payload === 'null') return;
        for (const saved of JSON.parse(payload)) {
            if (/\.(WorkingDate|Date|Month)$/.test(saved.name)) continue;
            const element = fields(form).find(field => field.name === saved.name);
            if (!element) continue;
            element.value = saved.value;
            if (element.type === 'checkbox' || element.type === 'radio') element.checked = saved.checked;
            element.dispatchEvent(new Event('input', { bubbles: true }));
            element.dispatchEvent(new Event('change', { bubbles: true }));
        }
    };
    const attach = async form => {
        const instance = form.dataset.draftInstance;
        const previous = attached.get(form);
        if (previous?.instance === instance && form.querySelector('[data-form-draft-status]')) return;
        if (previous) {
            previous.disposed = true;
            clearTimeout(previous.timer);
            previous.controller.abort();
        }
        form.querySelectorAll('[data-form-draft-control]').forEach(element => element.remove());
        const state = { instance, controller: new AbortController(), disposed: false,
            revision: 0, timer: null, queue: Promise.resolve(), ready: false, conflict: false, latest: null };
        attached.set(form, state);
        const status = document.createElement('p');
        status.className = 'mt-3 text-xs text-slate-500';
        status.setAttribute('role', 'status');
        status.dataset.formDraftStatus = 'true';
        status.dataset.formDraftControl = 'true';
        form.append(status);
        const initialPayload = capture(form);
        const savedRequest = new URLSearchParams(location.search).get('savedRequest');
        const matchesSaved = draft => draft?.payload && draft.payload !== 'null'
            && JSON.parse(draft.payload).some(field => field.name.endsWith('.RequestId') && field.value === savedRequest);
        let key;
        let local;
        let dirty = false;
        let restoring = false;
        form.addEventListener('input', () => { if (!restoring) dirty = true; }, { capture: true, signal: state.controller.signal });
        try {
            key = `pos:form:${await owner()}:${form.dataset.draftKey}:${tabId}`;
            const kind = form.dataset.draftKey;
            if (state.disposed) return;
            local = read(key);
            if (local?.payload && local.payload !== 'null' && !matchesSaved(local)) {
                restoring = true;
                restore(form, local.payload);
                restoring = false;
            }
            const server = await serverRead(kind);
            if (state.disposed || !form.isConnected) return;
            state.revision = server?.revision ?? 0;
            const save = payload => {
                const envelope = { revision: state.revision, payload, updatedAt: new Date().toISOString(), saveId: crypto.randomUUID(), pending: true };
                state.latest = envelope;
                localSave(key, envelope, status);
                clearTimeout(state.timer);
                state.timer = setTimeout(() => {
                    state.queue = state.queue.then(async () => {
                        if (state.disposed || state.conflict || state.latest !== envelope) return;
                        try {
                            const saved = await serverSave(kind, { revision: state.revision, payload, saveId: envelope.saveId });
                            state.revision = saved.revision;
                            if (!state.disposed && state.latest === envelope) {
                                localSave(key, saved, status);
                                status.textContent = payload === 'null' ? 'Ready for a new entry' : 'Draft saved';
                            }
                        } catch (error) {
                            state.conflict = true;
                            status.textContent = error.message;
                        }
                    });
                }, 800);
            };
            const recovered = local && (!server || local.pending || local.updatedAt > server.updatedAt) ? local : server;
            if (savedRequest && matchesSaved(recovered)) {
                restoring = true;
                restore(form, initialPayload);
                restoring = false;
                save('null');
            } else {
                if (local && server && local.revision !== server.revision && local.payload !== server.payload && (local.pending || local.updatedAt > server.updatedAt)) {
                    state.conflict = true;
                    status.textContent = 'A newer server draft exists. Your browser copy was recovered; it will not overwrite that draft.';
                }
                if (!dirty && recovered?.payload && recovered.payload !== 'null') {
                    restoring = true;
                    restore(form, recovered.payload);
                    restoring = false;
                    const interactive = form.querySelector('[data-form-draft-ready]');
                    if (interactive && interactive.dataset.formDraftReady.toLowerCase() !== 'true') {
                        const observer = new MutationObserver(() => {
                            if (interactive.dataset.formDraftReady.toLowerCase() === 'true') {
                                observer.disconnect();
                                if (!dirty && form.isConnected) restore(form, recovered.payload);
                            }
                        });
                        observer.observe(interactive, { attributes: true, attributeFilter: ['data-form-draft-ready'] });
                    }
                    if (!state.conflict) status.textContent = 'Draft recovered';
                }
            }
            const reset = document.createElement('button');
            reset.type = 'button';
            reset.dataset.formDraftControl = 'true';
            reset.className = 'ml-3 text-xs underline';
            reset.textContent = 'New entry';
            reset.title = 'Discard this form draft and reset its fields';
            reset.addEventListener('click', () => {
                restoring = true;
                restore(form, initialPayload);
                restoring = false;
                clearTimeout(state.timer);
                if (state.conflict) {
                    localSave(key, { revision: state.revision, payload: 'null', updatedAt: new Date().toISOString(), saveId: crypto.randomUUID() }, status);
                    status.textContent = 'New local entry · the other server draft has been kept';
                } else {
                    save('null');
                    status.textContent = 'Ready for a new entry';
                }
            });
            form.append(reset);
            state.ready = true;
            const changed = () => {
                if (!state.ready) return;
                save(capture(form));
                if (!state.conflict) status.textContent = 'Saved on this browser · syncing…';
            };
            form.addEventListener('input', changed, { signal: state.controller.signal });
            form.addEventListener('change', changed, { signal: state.controller.signal });
            if (dirty) changed();
        } catch (error) {
            if (state.disposed || !form.isConnected) return;
            if (local?.payload) restore(form, local.payload);
            status.textContent = error.message;
            // Continue browser recovery while the server is unavailable.
            form.addEventListener('input', () => {
                if (key) localSave(key, { revision: state.revision, payload: capture(form), updatedAt: new Date().toISOString(), saveId: crypto.randomUUID() }, status);
            }, { signal: state.controller.signal });
        }
    };
    const scan = () => document.querySelectorAll('form[data-draft-key]').forEach(form => void attach(form));
    document.addEventListener('submit', event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || form.method.toLowerCase() !== 'post') return;
        const date = document.querySelector('[data-working-date]')?.dataset.workingDate;
        if (!date) return;
        let input = form.querySelector('input[name="__WorkingDay"]');
        if (!input) {
            input = document.createElement('input');
            input.type = 'hidden';
            input.name = '__WorkingDay';
            form.append(input);
        }
        input.value = date;
    }, true);
    scan();
    blazor.addEventListener('enhancedload', scan);
}
