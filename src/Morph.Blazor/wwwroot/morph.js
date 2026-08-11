// The JavaScript half of Morph's Blazor components, loaded as an ES module by MorphInterop — so a
// consuming app needs no <script> tag and nothing lands in the global scope.

function toBlob(contentType, base64Content) {
    const characters = atob(base64Content);
    const bytes = new Uint8Array(characters.length);
    for (let i = 0; i < characters.length; i++) {
        bytes[i] = characters.charCodeAt(i);
    }
    return new Blob([bytes], { type: contentType });
}

export function download(fileName, contentType, base64Content) {
    const url = URL.createObjectURL(toBlob(contentType, base64Content));
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
export function createObjectUrl(contentType, base64Content) {
    return URL.createObjectURL(toBlob(contentType, base64Content));
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
