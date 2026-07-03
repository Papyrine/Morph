window.statePreference = {
    get: function (key) {
        return localStorage.getItem(key);
    },
    set: function (key, value) {
        localStorage.setItem(key, value);
    },
    remove: function (key) {
        localStorage.removeItem(key);
    }
};

window.fileDownload = {
    downloadBlob: function (filename, contentType, base64Content) {
        const byteCharacters = atob(base64Content);
        const byteNumbers = new Array(byteCharacters.length);
        for (let i = 0; i < byteCharacters.length; i++) {
            byteNumbers[i] = byteCharacters.charCodeAt(i);
        }
        const byteArray = new Uint8Array(byteNumbers);
        const blob = new Blob([byteArray], { type: contentType });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = filename;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(url);
    }
};

window.resultPreview = {
    // Wraps conversion output in a blob URL an <iframe> can load (the browser's PDF viewer needs a real
    // URL, and an HTML result needs a document of its own). The caller revokes it when done.
    createUrl: function (contentType, base64Content) {
        const byteCharacters = atob(base64Content);
        const byteNumbers = new Array(byteCharacters.length);
        for (let i = 0; i < byteCharacters.length; i++) {
            byteNumbers[i] = byteCharacters.charCodeAt(i);
        }
        const byteArray = new Uint8Array(byteNumbers);
        const blob = new Blob([byteArray], { type: contentType });
        return URL.createObjectURL(blob);
    },
    revokeUrl: function (url) {
        URL.revokeObjectURL(url);
    }
};

window.viewport = {
    // Reports whether the viewport is at least minWidth CSS pixels wide, and notifies dotNetReference on
    // every crossing of that threshold — drives the result pane, which only exists on wide screens.
    watchWide: function (dotNetReference, minWidth) {
        const query = window.matchMedia(`(min-width: ${minWidth}px)`);
        query.addEventListener('change', event =>
            dotNetReference.invokeMethodAsync('OnViewportWideChanged', event.matches));
        return query.matches;
    }
};

window.appInfo = {
    userAgent: function () {
        return navigator.userAgent;
    },
    // Totals the app's boot download. Waits for the load event (and web fonts) so every framework/asset
    // request has finished first, then sums Resource Timing: encodedBodySize is the compressed bytes over
    // the wire, decodedBodySize the uncompressed bytes. Accurate because nothing re-serves the responses
    // through a service worker (which would report body sizes as 0, a known Resource Timing spec gap).
    downloadSize: async function () {
        if (document.readyState !== 'complete') {
            await new Promise(resolve => window.addEventListener('load', resolve, { once: true }));
        }
        try {
            await document.fonts.ready;
        } catch {
        }

        let zipped = 0;
        let unzipped = 0;
        const add = entry => {
            zipped += entry.encodedBodySize || 0;
            unzipped += entry.decodedBodySize || 0;
        };
        performance.getEntriesByType('navigation').forEach(add);
        performance.getEntriesByType('resource').forEach(add);
        return { zipped, unzipped };
    },
    // Approximate RAM the app occupies. Blazor's managed heap lives in WebAssembly linear memory, so the
    // WASM buffer size is the real footprint; fall back to Chromium's JS heap when the runtime handle isn't
    // exposed, and 0 when neither is available (so the caller can hide the figure).
    ramBytes: function () {
        try {
            const buffer = globalThis.getDotnetRuntime?.(0)?.Module?.HEAP8?.buffer;
            if (buffer) {
                return buffer.byteLength;
            }
        } catch {
        }
        return performance.memory?.usedJSHeapSize ?? 0;
    }
};

window.themeManager = {
    applyTheme: function (themeName) {
        document.documentElement.setAttribute('data-theme', themeName.toLowerCase());
    },
    initializeTheme: function () {
        const savedTheme = localStorage.getItem('selectedTheme');
        if (savedTheme) {
            document.documentElement.setAttribute('data-theme', savedTheme.toLowerCase());
        }
    }
};
