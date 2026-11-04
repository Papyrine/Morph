// The JavaScript half of Morph's Blazor components, loaded as an ES module by MorphInterop — so a
// consuming app needs no <script> tag and nothing lands in the global scope. The selectable text layer and
// the viewer live in their own modules (morph-text.js, morph-viewer.js), fetched only when used.

// Bytes arrive as a Uint8Array: Blazor marshals a byte[] argument natively, with no base64 round trip.
function toBlob(contentType, bytes) {
    return new Blob([bytes], { type: contentType });
}

export function download(fileName, contentType, bytes) {
    const url = URL.createObjectURL(toBlob(contentType, bytes));
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
}

// Wraps conversion output in a blob URL an <iframe> can load (the browser's PDF viewer needs a real URL,
// and an HTML result needs a document of its own). The caller revokes it when done.
export function createObjectUrl(contentType, bytes) {
    return URL.createObjectURL(toBlob(contentType, bytes));
}

export function revokeObjectUrl(url) {
    URL.revokeObjectURL(url);
}

// Reports whether the viewport is at least minWidth CSS pixels wide, and notifies dotNetReference on
// every crossing of that threshold — drives the result pane, which only exists on wide screens.
export function watchWide(dotNetReference, minWidth) {
    const query = window.matchMedia(`(min-width: ${minWidth}px)`);
    query.addEventListener('change', event =>
        dotNetReference.invokeMethodAsync('OnViewportWideChanged', event.matches));
    return query.matches;
}

// Opens a file dropped anywhere on root through the file input findInput returns, as if it had been
// picked: the component's InputFile then reads it like any other. root carries draggingClass while a
// file is held over it. Only the first of several files is handed on, since the input takes one and
// Blazor's InputFileChangeEventArgs.File throws on more. Returns nothing to undo: the listeners go with
// root, or with signal when one is given.
export function acceptFileDrops(root, findInput, draggingClass, signal) {
    const holdsFiles = event => event.dataTransfer?.types?.includes('Files');
    const leave = () => root.classList.remove(draggingClass);

    root.addEventListener('dragover', event => {
        if (holdsFiles(event) && findInput()) {
            event.preventDefault();
            event.dataTransfer.dropEffect = 'copy';
            root.classList.add(draggingClass);
        }
    }, { signal });
    root.addEventListener('dragleave', event => {
        if (!root.contains(event.relatedTarget)) {
            leave();
        }
    }, { signal });
    root.addEventListener('drop', event => {
        leave();
        const input = findInput();
        const file = event.dataTransfer?.files?.[0];
        if (!input || !file) {
            return;
        }

        event.preventDefault();
        const transfer = new DataTransfer();
        transfer.items.add(file);
        input.files = transfer.files;
        input.dispatchEvent(new Event('change', { bubbles: true }));
    }, { signal });
}

// The converter's drop: anywhere on it opens the file through the upload box's own input, which is
// what .NET hands in (the converter's root carries no element reference of its own).
export function acceptConverterDrops(input) {
    const root = input.closest('.converter');
    if (root) {
        acceptFileDrops(root, () => input.isConnected ? input : null, 'converter-dragging');
    }
}

export function userAgent() {
    return navigator.userAgent;
}

// MorphWopi: the file a WOPI host opened Morph for, which Morph.Wopi writes into the page as JSON. Taken
// out of the page once read, so the access token is not left in the document.
export function wopiLaunch() {
    const element = document.getElementById('morph-wopi');
    if (!element) {
        return null;
    }

    element.remove();
    return element.textContent;
}

// Unlocks the file as the page goes away, when there is no time left to ask .NET: a beacon outlives the
// page, and sends a form, which the proxy reads in place of its headers.
let leaving = null;

export function unlockOnLeave(url, fields) {
    forgetLeave();
    leaving = () => {
        const form = new FormData();
        for (const [name, value] of Object.entries(fields)) {
            form.append(name, value);
        }

        navigator.sendBeacon(url, form);
    };
    window.addEventListener('pagehide', leaving);
}

export function forgetLeave() {
    if (leaving) {
        window.removeEventListener('pagehide', leaving);
        leaving = null;
    }
}

// Leaves the editor for the host's page (CloseUrl): the top window's, since the editor runs in its frame.
export function navigateTop(url) {
    window.top.location.href = url;
}
