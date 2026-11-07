// What keeps the editor's work from being lost: a prompt before the page is left while there are edits
// not saved, and a draft of the document in this browser's own storage (IndexedDB), from which the start
// page offers to recover it after a reload, a crash or a closed tab. Nothing leaves the device.
//
// Each tab keeps its own draft, under a key the tab holds for as long as it is open, through reloads
// (sessionStorage): a document edited in another tab no longer takes this one's place. The start page
// offers this tab's draft, or else the newest another tab left — closed, or crashed — and recovering
// it makes it this tab's. Storage can be refused — a private window, blocked site data — and then
// there is simply no draft: every call here is safe to make, and fails quietly.

const databaseName = 'morph-editor';
const storeName = 'drafts';

// The drafts kept, beyond which the oldest go: tabs closed with work in them each leave one.
const keptDrafts = 8;

// This tab's key, made the first time and kept in the tab's own session storage.
function tabKey() {
    const name = 'morph-draft-key';
    try {
        let key = sessionStorage.getItem(name);
        if (!key) {
            key = `tab-${crypto.randomUUID?.() ?? `${Date.now()}-${Math.random()}`}`;
            sessionStorage.setItem(name, key);
        }

        return key;
    } catch {
        return 'last';
    }
}

// Every draft kept, with its key.
async function readAll() {
    const database = await openDatabase();
    try {
        return await new Promise((resolve, reject) => {
            const transaction = database.transaction(storeName, 'readonly');
            const store = transaction.objectStore(storeName);
            const keys = store.getAllKeys();
            const values = store.getAll();
            transaction.oncomplete = () => resolve(keys.result.map((key, index) => ({ key, draft: values.result[index] })));
            transaction.onerror = () => reject(transaction.error);
            transaction.onabort = () => reject(transaction.error);
        });
    } finally {
        database.close();
    }
}

// Writes wait this long for the reader to stop, so a run of edits is one write.
const writeDelay = 500;

function openAt(version) {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(databaseName, version);
        request.onupgradeneeded = () => {
            if (!request.result.objectStoreNames.contains(storeName)) {
                request.result.createObjectStore(storeName);
            }
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

// A database of the name made without the store — opened by something else first — is opened a
// version on, which makes the store.
async function openDatabase() {
    const database = await openAt(undefined);
    if (database.objectStoreNames.contains(storeName)) {
        return database;
    }

    const version = database.version + 1;
    database.close();
    return openAt(version);
}

async function transact(mode, act) {
    const database = await openDatabase();
    try {
        return await new Promise((resolve, reject) => {
            const transaction = database.transaction(storeName, mode);
            const request = act(transaction.objectStore(storeName));
            transaction.oncomplete = () => resolve(request.result);
            transaction.onerror = () => reject(transaction.error);
            transaction.onabort = () => reject(transaction.error);
        });
    } finally {
        database.close();
    }
}

export class DraftKeeper {
    constructor(unsavedTyping) {
        // Whether a paragraph is open with typing not yet handed in, which is unsaved too.
        this.unsavedTyping = unsavedTyping;
        this.unsaved = false;
        this.pending = null;
        this.timer = 0;
        this.key = tabKey();
        // The key of the draft the start page offered (describe), which read and drop act on.
        this.offered = null;
        this.onBeforeUnload = event => {
            this.flush();
            if (this.unsaved || this.unsavedTyping()) {
                event.preventDefault();
                // Older browsers ask for this as well.
                event.returnValue = '';
            }
        };
        window.addEventListener('beforeunload', this.onBeforeUnload);
    }

    setUnsaved(unsaved) {
        this.unsaved = unsaved;
    }

    keep(name, bytes) {
        this.pending = { name, bytes, savedAt: Date.now() };
        clearTimeout(this.timer);
        this.timer = setTimeout(() => this.flush(), writeDelay);
    }

    flush() {
        clearTimeout(this.timer);
        const draft = this.pending;
        this.pending = null;
        if (!draft) {
            return Promise.resolve();
        }

        return transact('readwrite', store => store.put(draft, this.key)).catch(() => {});
    }

    // This tab's draft goes — the document saved, or its edits undone or discarded — and so does the
    // one the start page offered, discarded there.
    async drop() {
        clearTimeout(this.timer);
        this.pending = null;
        const offered = this.offered;
        this.offered = null;
        try {
            await transact('readwrite', store => {
                if (offered !== null && offered !== this.key) {
                    store.delete(offered);
                }

                return store.delete(this.key);
            });
        } catch {
            // No storage, so no draft.
        }
    }

    // The draft the start page offers — this tab's, or else the newest another left — by its name and
    // when it was written (milliseconds since 1970), or null. Beyond the few newest, drafts go.
    async describe() {
        try {
            const drafts = (await readAll())
                .filter(_ => _.draft?.bytes)
                .sort((first, second) => (second.draft.savedAt ?? 0) - (first.draft.savedAt ?? 0));
            const stale = drafts.slice(keptDrafts).filter(_ => _.key !== this.key);
            if (stale.length > 0) {
                await transact('readwrite', store => {
                    let request = null;
                    for (const { key } of stale) {
                        request = store.delete(key);
                    }

                    return request;
                });
            }

            const chosen = drafts.find(_ => _.key === this.key) ?? drafts[0];
            this.offered = chosen?.key ?? null;
            return chosen ? { name: chosen.draft.name, savedAt: chosen.draft.savedAt } : null;
        } catch {
            return null;
        }
    }

    // The offered draft's file; another tab's is made this one's as it is recovered, so it is kept
    // and dropped from here on.
    async read() {
        try {
            const key = this.offered ?? this.key;
            const draft = await transact('readonly', store => store.get(key));
            if (draft?.bytes && key !== this.key) {
                await transact('readwrite', store => {
                    store.delete(key);
                    return store.put(draft, this.key);
                });
                this.offered = this.key;
            }

            return draft?.bytes ?? null;
        } catch {
            return null;
        }
    }

    dispose() {
        this.flush();
        window.removeEventListener('beforeunload', this.onBeforeUnload);
    }
}
