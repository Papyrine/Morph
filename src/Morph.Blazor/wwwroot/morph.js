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

export function userAgent() {
    return navigator.userAgent;
}
