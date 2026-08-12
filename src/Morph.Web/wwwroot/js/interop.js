// The app shell's JavaScript: theme persistence and the footer's payload/RAM figures. Everything the
// converter needs ships with the Morph.Blazor package as an ES module (_content/Morph.Blazor/morph.js)
// and is imported by its components, so nothing for it belongs here.

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
