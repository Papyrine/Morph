// What keeps the editor's work from being lost: a prompt before the page is left while there are edits
// not saved, and a draft of the document in this browser's own storage (IndexedDB), from which the start
// page offers to recover it after a reload, a crash or a closed tab. Nothing leaves the device.
//
// There is one draft, the last document edited and not saved; a document edited in another tab takes
// its place. Storage can be refused — a private window, blocked site data — and then there is simply no
// draft: every call here is safe to make, and fails quietly.

const databaseName = 'morph-editor';
const storeName = 'drafts';
const draftKey = 'last';

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

        return transact('readwrite', store => store.put(draft, draftKey)).catch(() => {});
    }

    async drop() {
        clearTimeout(this.timer);
        this.pending = null;
        try {
            await transact('readwrite', store => store.delete(draftKey));
        } catch {
            // No storage, so no draft.
        }
    }

    // The draft's name and when it was written (milliseconds since 1970), or null.
    async describe() {
        try {
            const draft = await transact('readonly', store => store.get(draftKey));
            if (!draft?.bytes) {
                return null;
            }

            return { name: draft.name, savedAt: draft.savedAt };
        } catch {
            return null;
        }
    }

    async read() {
        try {
            const draft = await transact('readonly', store => store.get(draftKey));
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
