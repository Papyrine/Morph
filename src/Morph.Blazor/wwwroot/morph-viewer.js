// The viewer's controller — the browser half of MorphViewer. The script owns everything geometric: the page
// and thumbnail elements, the zoom, the scroll position, which pages are visible, and so which pages need
// rendering at what resolution. Geometry has to change in the same frame as a resize or a zoom, which a
// round trip through .NET cannot promise. .NET owns the document: it renders the pages this script asks for
// (the render queue) and pushes the images back, one at a time.
//
// Every push from .NET carries the document id load() was given; anything stamped with an older id is
// dropped, so a render finishing after a new file opened never lands on the new document.
//
// Review (a Word document's comments and tracked changes) follows the same split. .NET knows what the
// comments are and which text each covers, as offsets into a page's text — the offsets find results come
// in; this paints them, reports what the reader has selected in those same offsets, and says which
// character a click landed on. An edit lays the document out afresh, and reload() swaps the new pages in
// under the reader without moving them.
//
// Editing a Word document's text is the third of the kind. The typing itself is the script's
// (morph-edit.js lays an editor over the paragraph, and nothing crosses to .NET while the reader types);
// which paragraph a click is in, what it holds and what becomes of it afterwards are .NET's.

import { ensureFonts, buildTextLayer, clearTextLayer, textRange, textOffset, textLength, isBuilt } from './morph-text.js';
import { ParagraphEditor } from './morph-edit.js';

// PDF.js's zoom steps.
const zoomSteps = [0.25, 0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5];
const minimumScale = 0.1;
const maximumScale = 10;

// Renders are quantised to these DPIs so a small zoom change reuses the image already painted.
const dpiSteps = [48, 72, 96, 120, 144, 192, 240, 288, 384, 480, 576];

// CSS pixels per point at 100%: a point is 1/72 inch and a CSS pixel 1/96.
const pixelsPerPoint = 96 / 72;

const thumbnailWidth = 120;
const maxImages = 24;

// Text layers for every page are built in idle time, so the browser's own find can reach them all. Past
// this many characters of layer data only the pages around the current one are built.
const textBudget = 24000000;

export function attachViewer(root, dotNet, maxDpi, maxPagePixels) {
    const viewer = new Viewer(root, dotNet, maxDpi, maxPagePixels);
    // Plain closures, so the calls work however the interop layer binds `this`.
    return {
        load: (documentId, sizes, texts, pageIndex, zoom, pageNoun) => viewer.load(documentId, sizes, texts, pageIndex, zoom, pageNoun),
        reload: (documentId, sizes, texts) => viewer.reload(documentId, sizes, texts),
        unload: () => viewer.unload(),
        setPageImage: (documentId, pageIndex, png, dpi) => viewer.setPageImage(documentId, pageIndex, png, dpi),
        setThumbnail: (documentId, pageIndex, png) => viewer.setThumbnail(documentId, pageIndex, png),
        setPageError: (documentId, pageIndex, message) => viewer.setPageError(documentId, pageIndex, message),
        goToPage: pageIndex => viewer.goToPage(pageIndex),
        stepPage: delta => viewer.stepPage(delta),
        setZoom: value => viewer.setZoom(value),
        zoomBy: steps => viewer.zoomBy(steps),
        rotate: degrees => viewer.rotate(degrees),
        toggleSidebar: open => viewer.toggleSidebar(open),
        setFindResults: (documentId, matches, current) => viewer.setFindResults(documentId, matches, current),
        clearFind: () => viewer.clearFind(),
        setReviewMode: on => viewer.setReviewMode(on),
        setReview: (documentId, comments, current, scroll) => viewer.setReview(documentId, comments, current, scroll),
        revealReviewCard: key => viewer.revealReviewCard(key),
        setEditMode: on => viewer.setEditMode(on),
        beginEdit: (documentId, session) => viewer.beginEdit(documentId, session),
        // Minutes east of UTC, which is the sign .NET's offsets take and the opposite of the browser's.
        zoneOffset: () => -new Date().getTimezoneOffset(),
        beginPrint: sizes => viewer.beginPrint(sizes),
        addPrintPage: (pageIndex, png) => viewer.addPrintPage(pageIndex, png),
        finishPrint: () => viewer.finishPrint(),
        cancelPrint: () => viewer.cancelPrint(),
        dispose: () => viewer.dispose(),
    };
}

function clamp(value, low, high) {
    return Math.min(high, Math.max(low, value));
}

function capitalise(text) {
    return text.charAt(0).toUpperCase() + text.slice(1);
}

// Resolves once an image can be swapped in. Visible, that is once decoded, so the page never flashes blank
// between its old image and its new one; hidden, the load event is enough — a hidden document never
// settles decode() (it waits for the tab to show), which would stall the whole render queue behind it.
async function whenReady(image) {
    if (document.visibilityState !== 'hidden') {
        try {
            await image.decode();
            return;
        } catch {
            // Fall through: a decode failure still shows whatever the browser can make of it.
        }
    }

    if (!image.complete) {
        await new Promise(resolve => {
            image.addEventListener('load', resolve, { once: true });
            image.addEventListener('error', resolve, { once: true });
        });
    }
}

function isTyping(target) {
    return target instanceof Element &&
        (target.closest('input, textarea, select, [contenteditable=""], [contenteditable="true"]') !== null);
}

// A point's position within a page, as fractions of the page box, between the rotated (on screen) and
// unrotated (the page's own) frames.
function toScreen(u, v, rotation) {
    switch (rotation) {
        case 90: return [1 - v, u];
        case 180: return [1 - u, 1 - v];
        case 270: return [v, 1 - u];
        default: return [u, v];
    }
}

function toPage(x, y, rotation) {
    switch (rotation) {
        case 90: return [y, 1 - x];
        case 180: return [1 - x, 1 - y];
        case 270: return [1 - y, x];
        default: return [x, y];
    }
}

class Viewer {
    constructor(root, dotNet, maxDpi, maxPagePixels) {
        this.root = root;
        this.dotNet = dotNet;
        this.maxDpi = maxDpi;
        this.maxPagePixels = maxPagePixels;
        this.scroller = root.querySelector('.viewer-pages');
        this.thumbList = root.querySelector('.viewer-thumbs');
        this.abort = new AbortController();

        this.documentId = 0;
        this.pageNoun = 'page';
        this.pages = [];
        this.texts = [];
        this.textCharacters = 0;
        this.fontsLoaded = false;

        this.scale = 1;
        this.zoomMode = 'auto';
        this.rotation = 0;
        this.current = 0;
        this.currentLockedUntil = 0;
        this.sidebarOpen = false;
        this.presenting = false;
        this.presentationFullscreen = false;
        this.savedView = null;

        this.visible = new Set();
        this.near = new Set();
        this.visibleThumbs = new Set();
        this.find = null;
        this.print = null;
        this.review = null;
        this.reviewMode = false;
        this.editMode = false;
        this.editor = null;
        this.leaving = [];
        this.selectionTimer = 0;
        this.lastSelection = '';

        this.controlDown = false;
        this.wheelSteps = 0;
        this.pinchScale = null;
        this.pinchPoint = null;
        this.pinchFrame = 0;
        this.presentationWheel = 0;
        this.presentationWheelAt = 0;
        this.swipeStart = null;
        this.swiped = false;

        this.scrollFrame = 0;
        this.stateFrame = 0;
        this.stateInFlight = false;
        this.stateDirty = false;
        this.queueTimer = 0;
        this.queueInFlight = false;
        this.queueDirty = false;
        this.queueLength = 0;
        this.textHandle = 0;

        this.listen();
    }

    listen() {
        const signal = this.abort.signal;
        this.scroller.addEventListener('scroll', () => this.onScroll(), { passive: true, signal });
        this.scroller.addEventListener('wheel', event => this.onWheel(event), { passive: false, signal });
        this.scroller.addEventListener('pointerdown', event => this.onPointerDown(event), { signal });
        this.scroller.addEventListener('pointerup', event => this.onPointerUp(event), { signal });
        if ('ongesturestart' in window) {
            // Safari reports a trackpad pinch as gesture events rather than ctrl-wheel.
            this.scroller.addEventListener('gesturestart', event => {
                event.preventDefault();
                this.gestureScale = this.scale;
            }, { signal });
            this.scroller.addEventListener('gesturechange', event => {
                event.preventDefault();
                this.zoomMode = 'custom';
                this.applyScale((this.gestureScale ?? this.scale) * event.scale, this.pointOf(event));
            }, { signal });
        }

        this.root.addEventListener('keydown', event => this.onKeyDown(event), { signal });
        this.root.addEventListener('click', event => this.onClick(event), { signal });
        // A press on an editing button must not take the focus, and with it the selection the
        // button is for.
        this.root.addEventListener('mousedown', event => {
            if (event.target instanceof Element && event.target.closest('[data-edit-command]')) {
                event.preventDefault();
            }
        }, { signal });
        document.addEventListener('keydown', event => this.onDocumentKeyDown(event), { capture: true, signal });
        document.addEventListener('keyup', event => {
            if (event.key === 'Control' || event.key === 'Meta') {
                this.controlDown = false;
            }
        }, { signal });
        window.addEventListener('blur', () => {
            this.controlDown = false;
        }, { signal });
        document.addEventListener('fullscreenchange', () => this.onFullscreenChange(), { signal });
        document.addEventListener('selectionchange', () => this.onSelectionChange(), { signal });

        // Dropping a file anywhere on the viewer opens it, through the same file input the Open button uses.
        this.root.addEventListener('dragover', event => {
            if (this.fileInput() && event.dataTransfer?.types?.includes('Files')) {
                event.preventDefault();
                event.dataTransfer.dropEffect = 'copy';
                this.root.classList.add('viewer-dragging');
            }
        }, { signal });
        this.root.addEventListener('dragleave', event => {
            if (!this.root.contains(event.relatedTarget)) {
                this.root.classList.remove('viewer-dragging');
            }
        }, { signal });
        this.root.addEventListener('drop', event => {
            this.root.classList.remove('viewer-dragging');
            const input = this.fileInput();
            if (!input || !event.dataTransfer?.files?.length) {
                return;
            }

            event.preventDefault();
            input.files = event.dataTransfer.files;
            input.dispatchEvent(new Event('change', { bubbles: true }));
        }, { signal });

        this.resizeObserver = new ResizeObserver(() => this.onResize());
        this.resizeObserver.observe(this.scroller);
        this.watchResolution();

        this.visibleObserver = new IntersectionObserver(entries => this.onVisible(entries), { root: this.scroller, threshold: [0, 0.5, 1] });
        this.nearObserver = new IntersectionObserver(entries => this.onNear(entries), { root: this.scroller, rootMargin: '100% 0px' });
        this.thumbObserver = new IntersectionObserver(entries => this.onThumbs(entries), { root: this.thumbList, rootMargin: '50% 0px' });
    }

    fileInput() {
        return this.root.querySelector('.viewer-open input[type=file]');
    }

    // A browser zoom or a move to a monitor with another pixel ratio changes the resolution a page needs.
    watchResolution() {
        const query = matchMedia(`(resolution: ${window.devicePixelRatio}dppx)`);
        query.addEventListener('change', () => {
            if (!this.abort.signal.aborted) {
                this.scheduleQueue(0);
                this.watchResolution();
            }
        }, { once: true, signal: this.abort.signal });
    }

    // Document lifecycle

    load(documentId, sizes, texts, pageIndex, zoom, pageNoun) {
        this.unload();
        this.documentId = documentId;
        this.pageNoun = pageNoun || 'page';
        this.texts = texts;
        this.textCharacters = 0;

        const count = sizes.length / 2;
        const pages = document.createDocumentFragment();
        const thumbs = document.createDocumentFragment();
        for (let index = 0; index < count; index++) {
            const page = this.createPage(index, sizes[2 * index], sizes[2 * index + 1], count);
            pages.append(page.element);
            thumbs.append(page.thumb.element);
            this.pages.push(page);
        }

        this.scroller.replaceChildren(pages);
        this.thumbList.replaceChildren(thumbs);
        for (const page of this.pages) {
            this.observe(page);
        }

        this.root.dataset.pageCount = String(count);
        this.current = clamp(pageIndex, 0, Math.max(0, count - 1));
        this.zoomMode = zoom;
        this.scale = this.fitScale(zoom);
        this.applyGeometry();
        this.scrollToPage(this.current);
        this.markCurrent();
        this.notifyState();
        this.scheduleQueue(0);
        this.startText(documentId);
    }

    createPage(index, width, height, count) {
        const noun = capitalise(this.pageNoun);
        const element = document.createElement('div');
        element.className = 'viewer-page';
        element.dataset.pageNumber = String(index + 1);
        element.setAttribute('role', 'group');
        element.setAttribute('aria-label', `${noun} ${index + 1} of ${count}`);
        element.style.cssText = `--pw:${width};--ph:${height}`;
        const sheet = document.createElement('div');
        sheet.className = 'viewer-sheet';
        const image = document.createElement('img');
        image.className = 'viewer-image';
        image.alt = '';
        image.draggable = false;
        const layer = document.createElement('div');
        layer.className = 'text-layer';
        sheet.append(image, layer);
        element.append(sheet);

        const thumb = document.createElement('button');
        thumb.type = 'button';
        thumb.className = 'viewer-thumb';
        thumb.dataset.pageIndex = String(index);
        thumb.setAttribute('aria-label', `${noun} ${index + 1}`);
        const thumbSheet = document.createElement('span');
        thumbSheet.className = 'viewer-thumb-sheet';
        thumbSheet.style.cssText = `--pw:${width};--ph:${height}`;
        const thumbImage = document.createElement('img');
        thumbImage.alt = '';
        thumbImage.draggable = false;
        thumbSheet.append(thumbImage);
        const label = document.createElement('span');
        label.className = 'viewer-thumb-label';
        label.textContent = String(index + 1);
        thumb.append(thumbSheet, label);

        return {
            index,
            width,
            height,
            element,
            image,
            layer,
            url: null,
            dpi: 0,
            failed: false,
            textBuilt: false,
            thumb: { element: thumb, sheet: thumbSheet, image: thumbImage, url: null, failed: false, stale: false }
        };
    }

    observe(page) {
        this.visibleObserver.observe(page.element);
        this.nearObserver.observe(page.element);
        this.thumbObserver.observe(page.thumb.element);
    }

    // Text layers wait for the fonts they are measured in.
    startText(documentId) {
        ensureFonts().then(() => {
            if (this.documentId !== documentId) {
                return;
            }

            this.fontsLoaded = true;
            let built = false;
            for (const index of this.near) {
                built = this.ensureText(this.pages[index]) || built;
            }

            if (built) {
                this.repaint();
            }

            this.scheduleText();
        });
    }

    // The document changed under the reader — a comment added, a change accepted — and was laid out
    // again. The pages that are still there keep their elements and, until the new render arrives, the
    // image they show: an edit rarely moves more than a line, so swapping in place reads as the page
    // updating rather than the document reopening. The view stays where it was.
    reload(documentId, sizes, texts) {
        if (!this.pages.length) {
            this.load(documentId, sizes, texts, 0, this.zoomMode, this.pageNoun);
            return;
        }

        const anchor = this.centre();
        const saved = this.capture(anchor);
        this.clearFind();
        this.clearReview();
        this.cancelText();

        // A paragraph still open is no longer the paragraph it was opened on.
        if (this.editor && !this.editor.closed && !this.editor.pending) {
            this.editor.cancel();
        }

        this.documentId = documentId;
        this.texts = texts;
        this.textCharacters = 0;

        const count = sizes.length / 2;
        const noun = capitalise(this.pageNoun);
        for (let index = 0; index < count; index++) {
            const width = sizes[2 * index];
            const height = sizes[2 * index + 1];
            const page = this.pages[index];
            if (!page) {
                const added = this.createPage(index, width, height, count);
                this.pages.push(added);
                this.scroller.append(added.element);
                this.thumbList.append(added.thumb.element);
                this.observe(added);
                continue;
            }

            page.width = width;
            page.height = height;
            page.element.style.cssText = `--pw:${width};--ph:${height}`;
            page.element.setAttribute('aria-label', `${noun} ${index + 1} of ${count}`);
            page.thumb.sheet.style.cssText = `--pw:${width};--ph:${height}`;

            // Shown until it is replaced, and first in line to be.
            page.dpi = 0;
            page.failed = false;
            page.thumb.stale = page.thumb.url !== null;
            page.element.classList.remove('viewer-page-failed');
            page.element.querySelector('.viewer-page-message')?.remove();
            delete page.element.dataset.renderedDpi;
            if (page.textBuilt) {
                clearTextLayer(page.layer);
                page.textBuilt = false;
            }

            delete page.element.dataset.textState;
        }

        for (const page of this.pages.splice(count)) {
            this.visibleObserver.unobserve(page.element);
            this.nearObserver.unobserve(page.element);
            this.thumbObserver.unobserve(page.thumb.element);
            this.visible.delete(page.index);
            this.near.delete(page.index);
            this.visibleThumbs.delete(page.index);
            if (page.textBuilt) {
                clearTextLayer(page.layer);
            }

            if (page.url) {
                URL.revokeObjectURL(page.url);
            }

            if (page.thumb.url) {
                URL.revokeObjectURL(page.thumb.url);
            }

            page.element.remove();
            page.thumb.element.remove();
        }

        this.root.dataset.pageCount = String(count);
        this.current = clamp(this.current, 0, Math.max(0, count - 1));
        if (this.zoomMode !== 'custom') {
            this.scale = this.fitScale(this.zoomMode);
        }

        this.applyGeometry();
        if (saved && saved.index < count) {
            this.restore(saved, anchor);
        } else {
            this.scrollToPage(this.current);
        }

        this.markCurrent();
        this.notifyState();
        this.scheduleQueue(0);
        this.startText(documentId);
    }

    cancelText() {
        if (this.textHandle) {
            ('cancelIdleCallback' in window ? cancelIdleCallback : clearTimeout)(this.textHandle);
            this.textHandle = 0;
        }
    }

    unload() {
        this.clearFind();
        this.clearReview();
        this.dropEditors();
        this.lastSelection = '';
        this.visibleObserver.disconnect();
        this.nearObserver.disconnect();
        this.thumbObserver.disconnect();
        for (const page of this.pages) {
            if (page.textBuilt) {
                clearTextLayer(page.layer);
            }

            if (page.url) {
                URL.revokeObjectURL(page.url);
            }

            if (page.thumb.url) {
                URL.revokeObjectURL(page.thumb.url);
            }
        }

        this.cancelText();
        this.pages = [];
        this.texts = [];
        this.visible.clear();
        this.near.clear();
        this.visibleThumbs.clear();
        this.scroller.replaceChildren();
        this.thumbList.replaceChildren();
        this.documentId = 0;
        delete this.root.dataset.pageCount;
        delete this.root.dataset.currentPage;
        delete this.root.dataset.busy;
    }

    dispose() {
        this.cancelPrint();
        if (this.presenting) {
            this.exitPresentation();
        }

        this.unload();
        this.abort.abort();
        this.resizeObserver.disconnect();
        clearTimeout(this.queueTimer);
        clearTimeout(this.selectionTimer);
    }

    // Images pushed from .NET

    async setPageImage(documentId, pageIndex, png, dpi) {
        const page = documentId === this.documentId ? this.pages[pageIndex] : null;
        if (!page) {
            return;
        }

        const url = URL.createObjectURL(new Blob([png], { type: 'image/png' }));
        const image = new Image();
        image.className = 'viewer-image';
        image.alt = '';
        image.draggable = false;
        image.src = url;
        await whenReady(image);
        if (documentId !== this.documentId) {
            URL.revokeObjectURL(url);
            return;
        }

        page.image.replaceWith(image);
        page.image = image;
        if (page.url) {
            URL.revokeObjectURL(page.url);
        }

        page.url = url;
        page.dpi = dpi;
        page.failed = false;
        page.element.dataset.renderedDpi = String(dpi);
        page.element.classList.remove('viewer-page-failed');
        this.settle(page);
        this.evict();
        this.scheduleQueue(0);
    }

    async setThumbnail(documentId, pageIndex, png) {
        const page = documentId === this.documentId ? this.pages[pageIndex] : null;
        if (!page) {
            return;
        }

        const url = URL.createObjectURL(new Blob([png], { type: 'image/png' }));
        page.thumb.image.src = url;
        if (page.thumb.url) {
            URL.revokeObjectURL(page.thumb.url);
        }

        page.thumb.url = url;
        page.thumb.stale = false;

        // A page whose full image was evicted shows the thumbnail meanwhile.
        if (!page.url) {
            page.image.src = url;
        }

        this.scheduleQueue(0);
    }

    setPageError(documentId, pageIndex, message) {
        const page = documentId === this.documentId ? this.pages[pageIndex] : null;
        if (!page) {
            return;
        }

        page.failed = true;
        page.element.classList.add('viewer-page-failed');
        let note = page.element.querySelector('.viewer-page-message');
        if (!note) {
            note = document.createElement('div');
            note.className = 'viewer-page-message';
            page.element.append(note);
        }

        note.textContent = message;
        this.scheduleQueue(0);
    }

    // Keeps the most recently useful full-resolution images and lets the rest fall back to their
    // thumbnails — decoded page images are the bulk of the page's memory.
    evict() {
        const rendered = this.pages.filter(_ => _.url);
        let excess = rendered.length - maxImages;
        if (excess <= 0) {
            return;
        }

        // Furthest from the current page first; never one near it.
        rendered.sort((first, second) => Math.abs(second.index - this.current) - Math.abs(first.index - this.current));
        for (const page of rendered) {
            if (excess <= 0) {
                break;
            }

            if (Math.abs(page.index - this.current) <= 5 || this.near.has(page.index)) {
                continue;
            }

            URL.revokeObjectURL(page.url);
            page.url = null;
            page.dpi = 0;
            delete page.element.dataset.renderedDpi;
            if (page.thumb.url) {
                page.image.src = page.thumb.url;
            } else {
                page.image.removeAttribute('src');
            }

            excess--;
        }
    }

    // The render queue: what .NET should paint next, most wanted first, as flattened
    // (kind, page, dpi) triples — kind 0 a page, 1 a thumbnail.

    targetDpi(page) {
        const wanted = 96 * this.scale * (window.devicePixelRatio || 1);
        const stepped = dpiSteps.find(_ => _ >= wanted) ?? dpiSteps[dpiSteps.length - 1];
        const pixelCap = Math.floor(72 * Math.sqrt(this.maxPagePixels / (page.width * page.height)));
        return Math.max(24, Math.min(stepped, this.maxDpi, pixelCap));
    }

    thumbnailDpi(page) {
        const wanted = Math.round(thumbnailWidth * (window.devicePixelRatio || 1) * 72 / page.width);
        return clamp(wanted, 8, this.maxDpi);
    }

    computeQueue() {
        if (!this.documentId || this.print || this.pages.length === 0) {
            return [];
        }

        const order = [this.current];
        if (this.presenting) {
            order.push(this.current + 1, this.current - 1);
        } else {
            const visible = [...this.visible].sort((first, second) => Math.abs(first - this.current) - Math.abs(second - this.current));
            order.push(...visible, this.current + 1, this.current - 1, this.current + 2, this.current - 2);
        }

        const jobs = [];
        const seen = new Set();
        for (const index of order) {
            if (index < 0 || index >= this.pages.length || seen.has(index)) {
                continue;
            }

            seen.add(index);
            const page = this.pages[index];
            const target = this.targetDpi(page);
            if (!page.failed && page.dpi < target * 0.85) {
                jobs.push(0, index, target);
            }
        }

        if (this.sidebarOpen && !this.presenting) {
            for (const index of [...this.visibleThumbs].sort((first, second) => first - second)) {
                const page = this.pages[index];
                if (page && (!page.thumb.url || page.thumb.stale) && !page.failed) {
                    jobs.push(1, index, this.thumbnailDpi(page));
                }
            }
        }

        return jobs;
    }

    scheduleQueue(delay = 50) {
        clearTimeout(this.queueTimer);
        this.queueTimer = setTimeout(() => this.sendQueue(), delay);
    }

    // Single-flight: one call in flight at a time, then the latest queue again if anything changed.
    async sendQueue() {
        if (this.queueInFlight) {
            this.queueDirty = true;
            return;
        }

        this.queueInFlight = true;
        try {
            do {
                this.queueDirty = false;
                const jobs = this.computeQueue();
                this.queueLength = jobs.length;
                this.updateBusy();
                await this.dotNet.invokeMethodAsync('OnRenderQueue', jobs);
            } while (this.queueDirty && !this.abort.signal.aborted);
        } catch {
            // The component is gone; nothing left to render for.
        } finally {
            this.queueInFlight = false;
        }
    }

    updateBusy() {
        const pendingText = this.pages.some(_ => !_.textBuilt && this.texts[_.index]);
        if (this.queueLength > 0 || (pendingText && this.withinTextBudget())) {
            this.root.dataset.busy = '';
        } else {
            delete this.root.dataset.busy;
        }
    }

    // Text layers

    ensureText(page) {
        if (!page || page.textBuilt || !this.fontsLoaded) {
            return false;
        }

        const json = this.texts[page.index];
        if (!json) {
            return false;
        }

        buildTextLayer(page.layer, JSON.parse(json));
        page.textBuilt = true;
        this.textCharacters += json.length;
        this.texts[page.index] = null;
        page.element.dataset.textState = 'ready';
        return true;
    }

    withinTextBudget() {
        return this.textCharacters < textBudget;
    }

    // Builds the layers of every page nearest-first in idle time, so the browser's own find reaches the
    // whole document; past the budget, only pages within fifty of the current one.
    scheduleText() {
        if (this.textHandle || !this.fontsLoaded) {
            return;
        }

        const run = deadline => {
            this.textHandle = 0;
            const started = performance.now();
            const order = this.pages
                .filter(_ => !_.textBuilt && this.texts[_.index] && (this.withinTextBudget() || Math.abs(_.index - this.current) <= 50))
                .sort((first, second) => Math.abs(first.index - this.current) - Math.abs(second.index - this.current));
            let built = false;
            for (const page of order) {
                built = this.ensureText(page) || built;
                const exhausted = deadline ? deadline.timeRemaining() < 2 : performance.now() - started > 8;
                if (exhausted) {
                    break;
                }
            }

            for (const page of this.pages) {
                if (!page.textBuilt && this.texts[page.index] && !this.withinTextBudget() && Math.abs(page.index - this.current) > 50) {
                    page.element.dataset.textState = 'deferred';
                }
            }

            if (built) {
                this.repaint();
            }

            this.updateBusy();
            if (order.length > 1) {
                this.scheduleText();
            }
        };

        this.textHandle = 'requestIdleCallback' in window
            ? requestIdleCallback(run, { timeout: 300 })
            : setTimeout(() => run(null), 16);
    }

    // Geometry

    padding() {
        const style = getComputedStyle(this.scroller);
        return {
            x: parseFloat(style.paddingLeft) + parseFloat(style.paddingRight),
            y: parseFloat(style.paddingTop) + parseFloat(style.paddingBottom),
            top: parseFloat(style.paddingTop)
        };
    }

    fitScale(mode) {
        const page = this.pages[this.current] ?? this.pages[0];
        if (!page) {
            return 1;
        }

        if (mode === 'page-actual') {
            return 1;
        }

        const turned = this.rotation % 180 !== 0;
        const pageWidth = (turned ? page.height : page.width) * pixelsPerPoint;
        const pageHeight = (turned ? page.width : page.height) * pixelsPerPoint;
        const padding = this.padding();
        const width = Math.max(1, this.scroller.clientWidth - padding.x) / pageWidth;
        const fit = Math.min(width, Math.max(1, this.scroller.clientHeight - padding.y) / pageHeight);
        switch (mode) {
            case 'page-width':
                return width;
            case 'page-fit':
                return fit;
            default:
                // PDF.js's automatic zoom: page width for a portrait page, the whole page for a landscape
                // one, never above 125%.
                return Math.min(1.25, pageWidth <= pageHeight ? width : fit);
        }
    }

    applyGeometry() {
        this.scroller.style.setProperty('--z', String(this.scale));
        this.root.dataset.scale = this.scale.toFixed(4);
        this.root.dataset.zoomMode = this.zoomMode;
        this.root.dataset.rotation = String(this.rotation);
    }

    centre() {
        return { x: this.scroller.clientWidth / 2, y: this.scroller.clientHeight / 2 };
    }

    pointOf(event) {
        const box = this.scroller.getBoundingClientRect();
        return { x: event.clientX - box.left - this.scroller.clientLeft, y: event.clientY - box.top - this.scroller.clientTop };
    }

    // The page under a content-space y: pages stack in one column, in order.
    pageAt(y) {
        let low = 0;
        let high = this.pages.length - 1;
        while (low < high) {
            const middle = (low + high + 1) >> 1;
            if (this.pages[middle].element.offsetTop <= y) {
                low = middle;
            } else {
                high = middle - 1;
            }
        }

        return this.pages.length ? low : -1;
    }

    // Where a viewport point sits, as a fraction of its page in the page's own (unrotated) frame.
    capture(point) {
        const x = this.scroller.scrollLeft + point.x;
        const y = this.scroller.scrollTop + point.y;
        const index = this.presenting ? this.current : this.pageAt(y);
        if (index < 0) {
            return null;
        }

        const element = this.pages[index].element;
        const [u, v] = toPage(
            (x - element.offsetLeft) / Math.max(1, element.offsetWidth),
            (y - element.offsetTop) / Math.max(1, element.offsetHeight),
            this.rotation);
        return { index, u, v };
    }

    // Scrolls so the captured point is back under the same viewport point.
    restore(saved, point) {
        if (!saved || this.presenting) {
            return;
        }

        const element = this.pages[saved.index].element;
        const [x, y] = toScreen(saved.u, saved.v, this.rotation);
        this.scroller.scrollLeft = element.offsetLeft + x * element.offsetWidth - point.x;
        this.scroller.scrollTop = element.offsetTop + y * element.offsetHeight - point.y;
    }

    applyScale(scale, point) {
        const anchor = point ?? this.centre();
        const saved = this.capture(anchor);
        this.scale = clamp(scale, minimumScale, maximumScale);
        this.applyGeometry();
        // Reading offsets in restore() forces the one layout the new scale needs.
        this.restore(saved, anchor);
        this.notifyState();
        this.scheduleQueue(150);
    }

    setZoom(value) {
        if (typeof value === 'number') {
            this.zoomMode = 'custom';
            this.applyScale(value);
        } else {
            this.zoomMode = value;
            this.applyScale(this.fitScale(value));
        }
    }

    zoomBy(steps, point) {
        let scale = this.scale;
        for (let step = 0; step < Math.abs(steps); step++) {
            scale = steps > 0
                ? zoomSteps.find(_ => _ > scale + 0.001) ?? zoomSteps[zoomSteps.length - 1]
                : [...zoomSteps].reverse().find(_ => _ < scale - 0.001) ?? zoomSteps[0];
        }

        this.zoomMode = 'custom';
        this.applyScale(scale, point);
    }

    rotate(degrees) {
        const anchor = this.centre();
        const saved = this.capture(anchor);
        this.rotation = (((this.rotation + degrees) % 360) + 360) % 360;
        if (this.zoomMode !== 'custom') {
            this.scale = this.fitScale(this.zoomMode);
        }

        this.applyGeometry();
        this.restore(saved, anchor);
        this.notifyState();
        this.scheduleQueue(150);
    }

    onResize() {
        if (!this.pages.length) {
            return;
        }

        if (this.zoomMode !== 'custom') {
            const scale = this.fitScale(this.zoomMode);
            if (Math.abs(scale - this.scale) > 0.001) {
                this.applyScale(scale);
                return;
            }
        }

        this.scheduleQueue(150);
    }

    // Navigation and the current page

    scrollToPage(index) {
        const page = this.pages[index];
        if (!page || this.presenting) {
            return;
        }

        this.scroller.scrollTop = page.element.offsetTop - this.padding().top;
    }

    goToPage(index) {
        if (!this.pages.length) {
            return;
        }

        this.current = clamp(index, 0, this.pages.length - 1);
        // The scroll this causes must not immediately hand "current" to a neighbour: the last page, say,
        // cannot scroll to the top, so the page above it may show more of itself.
        this.currentLockedUntil = performance.now() + 400;
        this.scrollToPage(this.current);
        this.markCurrent();
        this.notifyState();
        this.scheduleQueue(0);
    }

    stepPage(delta) {
        this.goToPage(this.current + delta);
    }

    onScroll() {
        if (this.scrollFrame) {
            return;
        }

        this.scrollFrame = requestAnimationFrame(() => {
            this.scrollFrame = 0;
            this.updateCurrent();
            this.scheduleQueue(50);
        });
    }

    onVisible(entries) {
        for (const entry of entries) {
            const index = Number(entry.target.dataset.pageNumber) - 1;
            if (entry.isIntersecting) {
                this.visible.add(index);
            } else {
                this.visible.delete(index);
            }
        }

        this.updateCurrent();
        this.scheduleQueue(50);
    }

    onNear(entries) {
        let built = false;
        for (const entry of entries) {
            const index = Number(entry.target.dataset.pageNumber) - 1;
            if (entry.isIntersecting) {
                this.near.add(index);
                built = this.ensureText(this.pages[index]) || built;
            } else {
                this.near.delete(index);
            }
        }

        if (built) {
            this.repaint();
        }
    }

    // A highlight can only be painted in a layer that is built; each one that gets built may hold some.
    repaint() {
        this.paintFind(false);
        this.paintReview(false);
    }

    onThumbs(entries) {
        for (const entry of entries) {
            const index = Number(entry.target.dataset.pageIndex);
            if (entry.isIntersecting) {
                this.visibleThumbs.add(index);
            } else {
                this.visibleThumbs.delete(index);
            }
        }

        if (this.sidebarOpen) {
            this.scheduleQueue(50);
        }
    }

    // The current page is the one showing the most of itself; it stays current while wholly visible, so
    // a zoomed-out view of several whole pages doesn't flicker between them.
    updateCurrent() {
        if (this.presenting || !this.pages.length) {
            return;
        }

        const box = this.scroller.getBoundingClientRect();
        const visibleArea = page => {
            const rect = page.element.getBoundingClientRect();
            const width = Math.min(rect.right, box.right) - Math.max(rect.left, box.left);
            const height = Math.min(rect.bottom, box.bottom) - Math.max(rect.top, box.top);
            return width > 0 && height > 0 ? { area: width * height, fraction: (width * height) / (rect.width * rect.height) } : { area: 0, fraction: 0 };
        };

        const current = visibleArea(this.pages[this.current]);
        if (current.fraction > 0.999 || (current.area > 0 && performance.now() < this.currentLockedUntil)) {
            return;
        }

        let best = -1;
        let bestArea = 0;
        for (const index of this.visible) {
            const { area } = visibleArea(this.pages[index]);
            if (area > bestArea + 0.5 || (Math.abs(area - bestArea) <= 0.5 && index < best)) {
                best = index;
                bestArea = area;
            }
        }

        if (best >= 0 && best !== this.current) {
            this.current = best;
            this.markCurrent();
            this.notifyState();
        }
    }

    markCurrent() {
        for (const page of this.pages) {
            if (page.index === this.current) {
                page.element.dataset.current = '';
                page.thumb.element.setAttribute('aria-current', 'page');
            } else if ('current' in page.element.dataset) {
                delete page.element.dataset.current;
                page.thumb.element.removeAttribute('aria-current');
            }
        }

        this.root.dataset.currentPage = String(this.current + 1);
        if (this.sidebarOpen) {
            this.pages[this.current]?.thumb.element.scrollIntoView({ block: 'nearest' });
        }
    }

    toggleSidebar(open) {
        this.sidebarOpen = open;
        if (open) {
            this.root.dataset.sidebar = 'open';
            requestAnimationFrame(() => this.pages[this.current]?.thumb.element.scrollIntoView({ block: 'nearest' }));
        } else {
            delete this.root.dataset.sidebar;
        }

        this.notifyState();
        this.scheduleQueue(0);
    }

    // Coalesced to a frame and single-flight, like the queue.
    notifyState() {
        if (this.stateFrame) {
            return;
        }

        this.stateFrame = requestAnimationFrame(() => {
            this.stateFrame = 0;
            this.sendState();
        });
    }

    async sendState() {
        if (this.stateInFlight) {
            this.stateDirty = true;
            return;
        }

        this.stateInFlight = true;
        try {
            do {
                this.stateDirty = false;
                await this.dotNet.invokeMethodAsync('OnViewerState', this.current + 1, this.scale, this.zoomMode, this.presenting, this.sidebarOpen);
            } while (this.stateDirty && !this.abort.signal.aborted);
        } catch {
            // The component is gone.
        } finally {
            this.stateInFlight = false;
        }
    }

    call(method) {
        this.dotNet.invokeMethodAsync(method).catch(() => {});
    }

    // Input

    onWheel(event) {
        if (this.presenting) {
            event.preventDefault();
            const now = performance.now();
            if (now - this.presentationWheelAt > 400) {
                this.presentationWheel = 0;
            }

            this.presentationWheel += event.deltaY;
            if (Math.abs(this.presentationWheel) >= 50 && now - this.presentationWheelAt > 100) {
                this.stepPage(Math.sign(this.presentationWheel));
                this.presentationWheel = 0;
                this.presentationWheelAt = now;
            }

            return;
        }

        if (!event.ctrlKey && !event.metaKey) {
            return;
        }

        event.preventDefault();
        const point = this.pointOf(event);
        const pixels = event.deltaMode === 1 ? event.deltaY * 16 : event.deltaMode === 2 ? event.deltaY * 400 : event.deltaY;
        if (!this.controlDown && Math.abs(pixels) < 50) {
            // A trackpad pinch (the browser sets ctrlKey without a key held): zoom continuously, once a frame.
            this.pinchScale = (this.pinchScale ?? this.scale) * Math.exp(-pixels / 100);
            this.pinchPoint = point;
            if (!this.pinchFrame) {
                this.pinchFrame = requestAnimationFrame(() => {
                    this.pinchFrame = 0;
                    this.zoomMode = 'custom';
                    this.applyScale(this.pinchScale, this.pinchPoint);
                    this.pinchScale = null;
                });
            }

            return;
        }

        // A wheel notch is one step; a finer device accumulates to one.
        this.wheelSteps += pixels;
        if (Math.abs(this.wheelSteps) >= 30) {
            const steps = -Math.sign(this.wheelSteps);
            this.wheelSteps = 0;
            this.zoomBy(steps, point);
        }
    }

    onPointerDown(event) {
        if (this.presenting && event.pointerType !== 'mouse') {
            this.swipeStart = { x: event.clientX, y: event.clientY };
        }
    }

    onPointerUp(event) {
        if (!this.presenting || !this.swipeStart) {
            return;
        }

        const distance = event.clientX - this.swipeStart.x;
        this.swipeStart = null;
        if (Math.abs(distance) > 50) {
            this.swiped = true;
            this.stepPage(distance < 0 ? 1 : -1);
        }
    }

    onClick(event) {
        const target = event.target instanceof Element ? event.target : null;
        if (!target) {
            return;
        }

        const action = target.closest('[data-viewer-action]')?.dataset.viewerAction;
        if (action === 'presentation') {
            // Handled here, inside the click, because the browser grants fullscreen only to a user gesture.
            this.togglePresentation();
            return;
        }

        if (action === 'open') {
            this.fileInput()?.click();
            return;
        }

        const thumb = target.closest('.viewer-thumb');
        if (thumb && this.thumbList.contains(thumb)) {
            this.goToPage(Number(thumb.dataset.pageIndex));
            return;
        }

        const command = target.closest('[data-edit-command]');
        if (command && !command.disabled) {
            this.editCommand(command.dataset.editCommand);
            return;
        }

        if (this.editMode && !this.presenting && this.scroller.contains(target)) {
            // A click in the editor is the editor's; one anywhere else on a page opens what is there.
            if (!target.closest('.edit-box') && target.closest('.viewer-page')) {
                this.onEditClick(event);
            }

            return;
        }

        if (this.reviewMode && !this.presenting) {
            const layer = target.closest('.text-layer');
            if (layer && isBuilt(layer)) {
                this.hitTest(event, layer);
            }
        }

        // A click or tap advances a presentation — unless it ended a swipe, which already moved.
        if (this.presenting && this.scroller.contains(target)) {
            if (this.swiped) {
                this.swiped = false;
            } else if (!getSelection()?.toString()) {
                this.stepPage(1);
            }
        }
    }

    onKeyDown(event) {
        if (event.defaultPrevented) {
            return;
        }

        if (event.key === 'Control' || event.key === 'Meta') {
            this.controlDown = true;
            return;
        }

        const typing = isTyping(event.target);
        if (this.editMode && !typing && !this.presenting && this.onEditKey(event)) {
            return;
        }

        if ((event.ctrlKey || event.metaKey) && !event.altKey) {
            switch (event.key.toLowerCase()) {
                case 'f':
                    event.preventDefault();
                    this.call('OnFindRequested');
                    return;
                case 's':
                    event.preventDefault();
                    // What is being typed is part of what is saved.
                    this.closeEditor().then(() => this.call('OnSaveRequested'));
                    return;
                case 'o':
                    event.preventDefault();
                    this.fileInput()?.click();
                    return;
                case 'a':
                    if (!typing && this.pages.length) {
                        event.preventDefault();
                        const range = document.createRange();
                        range.selectNodeContents(this.scroller);
                        const selection = getSelection();
                        selection.removeAllRanges();
                        selection.addRange(range);
                    }

                    return;
                case '+':
                case '=':
                    event.preventDefault();
                    this.zoomBy(1);
                    return;
                case '-':
                case '_':
                    event.preventDefault();
                    this.zoomBy(-1);
                    return;
                case '0':
                    event.preventDefault();
                    this.setZoom('auto');
                    return;
            }

            return;
        }

        if (typing || event.altKey || !this.pages.length) {
            return;
        }

        if (this.presenting) {
            this.onPresentationKey(event);
            return;
        }

        const noHorizontalScroll = this.scroller.scrollWidth <= this.scroller.clientWidth + 1;
        const fitsPage = this.zoomMode === 'page-fit';
        switch (event.key) {
            case 'ArrowRight':
                if (noHorizontalScroll) {
                    event.preventDefault();
                    this.stepPage(1);
                }

                break;
            case 'ArrowLeft':
                if (noHorizontalScroll) {
                    event.preventDefault();
                    this.stepPage(-1);
                }

                break;
            case 'Home':
                event.preventDefault();
                this.goToPage(0);
                break;
            case 'End':
                event.preventDefault();
                this.goToPage(this.pages.length - 1);
                break;
            case 'PageDown':
                if (fitsPage) {
                    event.preventDefault();
                    this.stepPage(1);
                }

                break;
            case 'PageUp':
                if (fitsPage) {
                    event.preventDefault();
                    this.stepPage(-1);
                }

                break;
        }
    }

    // Ctrl+P while focus is in the viewer prints the document rather than the host page around it.
    onDocumentKeyDown(event) {
        if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === 'p' &&
            this.pages.length && (this.presenting || this.root.contains(document.activeElement))) {
            event.preventDefault();
            event.stopPropagation();
            this.call('OnPrintRequested');
        }
    }

    // Presentation mode: the current page alone, fitted to the screen.

    togglePresentation() {
        if (this.presenting) {
            this.exitPresentation();
            return;
        }

        if (!this.pages.length) {
            return;
        }

        if (document.fullscreenEnabled && this.root.requestFullscreen) {
            this.root.requestFullscreen().catch(() => this.enterPresentation(false));
        } else {
            // No element fullscreen (an iPhone): a fixed overlay stands in.
            this.enterPresentation(false);
        }
    }

    onFullscreenChange() {
        const fullscreen = document.fullscreenElement === this.root;
        if (fullscreen && !this.presenting) {
            this.enterPresentation(true);
        } else if (!fullscreen && this.presenting && this.presentationFullscreen) {
            this.leavePresentation();
        }
    }

    enterPresentation(fullscreen) {
        this.presenting = true;
        this.presentationFullscreen = fullscreen;
        this.savedView = { zoomMode: this.zoomMode, scale: this.scale };
        this.root.dataset.presenting = fullscreen ? 'fullscreen' : 'overlay';
        this.zoomMode = 'page-fit';
        this.scroller.focus({ preventScroll: true });
        requestAnimationFrame(() => {
            this.scale = this.fitScale('page-fit');
            this.applyGeometry();
            this.markCurrent();
            this.notifyState();
            this.scheduleQueue(0);
        });
    }

    exitPresentation() {
        if (this.presentationFullscreen && document.fullscreenElement === this.root) {
            // fullscreenchange finishes the exit.
            document.exitFullscreen().catch(() => this.leavePresentation());
        } else {
            this.leavePresentation();
        }
    }

    leavePresentation() {
        this.presenting = false;
        this.presentationFullscreen = false;
        delete this.root.dataset.presenting;
        const saved = this.savedView ?? { zoomMode: 'auto', scale: 1 };
        this.zoomMode = saved.zoomMode;
        this.scale = saved.zoomMode === 'custom' ? saved.scale : this.fitScale(saved.zoomMode);
        this.applyGeometry();
        this.goToPage(this.current);
    }

    onPresentationKey(event) {
        switch (event.key) {
            case 'ArrowRight':
            case 'ArrowDown':
            case 'PageDown':
            case ' ':
            case 'Enter':
            case 'n':
            case 'j':
                event.preventDefault();
                this.stepPage(1);
                break;
            case 'ArrowLeft':
            case 'ArrowUp':
            case 'PageUp':
            case 'Backspace':
            case 'p':
            case 'k':
                event.preventDefault();
                this.stepPage(-1);
                break;
            case 'Home':
                event.preventDefault();
                this.goToPage(0);
                break;
            case 'End':
                event.preventDefault();
                this.goToPage(this.pages.length - 1);
                break;
            case 'Escape':
                // Fullscreen handles its own Escape; the overlay stand-in needs this.
                if (!this.presentationFullscreen) {
                    event.preventDefault();
                    this.leavePresentation();
                }

                break;
        }
    }

    // Find: .NET searches the pages' text and sends (page, start, length) triples; this paints them.

    setFindResults(documentId, matches, current) {
        if (documentId !== this.documentId) {
            return;
        }

        this.find = { matches, current };
        if (current >= 0) {
            // The current match's page must have its layer before a range can be made in it.
            this.ensureText(this.pages[matches[3 * current]]);
        }

        this.paintFind(true);
    }

    clearFind() {
        this.find = null;
        if (typeof CSS !== 'undefined' && CSS.highlights) {
            CSS.highlights.delete('morph-find');
            CSS.highlights.delete('morph-find-current');
        }
    }

    paintFind(scroll) {
        const find = this.find;
        if (!find) {
            return;
        }

        const count = find.matches.length / 3;
        const centre = Math.max(0, find.current);
        const from = Math.max(0, centre - 500);
        const to = Math.min(count, centre + 500);
        const others = [];
        let currentRange = null;
        let currentPage = null;
        for (let match = from; match < to; match++) {
            const page = this.pages[find.matches[3 * match]];
            if (!page || !isBuilt(page.layer)) {
                continue;
            }

            const range = textRange(page.layer, find.matches[3 * match + 1], find.matches[3 * match + 2]);
            if (!range) {
                continue;
            }

            if (match === find.current) {
                currentRange = range;
                currentPage = page;
            } else {
                others.push(range);
            }
        }

        if (typeof CSS !== 'undefined' && CSS.highlights && typeof Highlight !== 'undefined') {
            CSS.highlights.set('morph-find', new Highlight(...others));
            CSS.highlights.set('morph-find-current', currentRange ? new Highlight(currentRange) : new Highlight());
        } else if (currentRange && scroll) {
            // No highlight API: select the current match instead.
            const selection = getSelection();
            selection.removeAllRanges();
            selection.addRange(currentRange);
        }

        if (scroll && currentPage) {
            this.revealMatch(currentRange, currentPage);
        }
    }

    revealMatch(range, page) {
        if (this.presenting) {
            this.goToPage(page.index);
            return;
        }

        if (page.index !== this.current) {
            this.current = page.index;
            this.currentLockedUntil = performance.now() + 400;
            this.markCurrent();
            this.notifyState();
        }

        const box = this.scroller.getBoundingClientRect();
        const rect = range.getBoundingClientRect();
        if (rect.width === 0 && rect.height === 0) {
            this.scrollToPage(page.index);
            return;
        }

        if (rect.top < box.top || rect.bottom > box.bottom) {
            this.scroller.scrollTop += rect.top - box.top - (this.scroller.clientHeight - rect.height) / 2;
        }

        if (rect.left < box.left || rect.right > box.right) {
            this.scroller.scrollLeft += rect.left - box.left - Math.max(0, (this.scroller.clientWidth - rect.width) / 2);
        }

        this.scheduleQueue(0);
    }

    // Review: .NET sends the comments' ranges and the chosen item's as (page, start, length) triples,
    // like find results, and this paints them. While review is on it also reports what the reader has
    // selected, as [first page, start, last page, end] in the same offsets, and the character a click
    // landed on.

    setReviewMode(on) {
        this.reviewMode = on;
        this.lastSelection = '';
        if (on) {
            this.root.dataset.review = 'open';
            this.onSelectionChange();
        } else {
            delete this.root.dataset.review;
            this.clearReview();
        }

        // The pane takes its width from the pages.
        this.onResize();
    }

    setReview(documentId, comments, current, scroll) {
        if (documentId !== this.documentId) {
            return;
        }

        this.review = { comments, current };
        if (current.length) {
            this.ensureText(this.pages[current[0]]);
        }

        this.paintReview(scroll);
    }

    clearReview() {
        this.review = null;
        if (typeof CSS !== 'undefined' && CSS.highlights) {
            CSS.highlights.delete('morph-comment');
            CSS.highlights.delete('morph-review-current');
        }
    }

    reviewRanges(triples) {
        const ranges = [];
        for (let index = 0; index + 2 < triples.length; index += 3) {
            const page = this.pages[triples[index]];
            if (!page || !isBuilt(page.layer)) {
                continue;
            }

            const range = textRange(page.layer, triples[index + 1], triples[index + 2]);
            if (range) {
                ranges.push(range);
            }
        }

        return ranges;
    }

    paintReview(scroll) {
        const review = this.review;
        if (!review) {
            return;
        }

        if (typeof CSS !== 'undefined' && CSS.highlights && typeof Highlight !== 'undefined') {
            // Under find's highlights, which say what the reader is looking for right now.
            const comments = new Highlight(...this.reviewRanges(review.comments));
            comments.priority = -2;
            const current = new Highlight(...this.reviewRanges(review.current));
            current.priority = -1;
            CSS.highlights.set('morph-comment', comments);
            CSS.highlights.set('morph-review-current', current);
        }

        if (!scroll || review.current.length < 3) {
            return;
        }

        const page = this.pages[review.current[0]];
        if (!page) {
            return;
        }

        // A place rather than a stretch — a comment on a point — still needs a box to scroll to.
        const range = isBuilt(page.layer)
            ? textRange(page.layer, review.current[1], Math.max(1, review.current[2]))
            : null;
        if (range) {
            this.revealMatch(range, page);
        } else {
            this.goToPage(page.index);
        }
    }

    revealReviewCard(key) {
        for (const card of this.root.querySelectorAll('.review-card[data-review-key]')) {
            if (card.dataset.reviewKey === key) {
                card.scrollIntoView({ block: 'nearest' });
                return;
            }
        }
    }

    onSelectionChange() {
        if (!this.reviewMode && !this.editMode) {
            return;
        }

        clearTimeout(this.selectionTimer);
        this.selectionTimer = setTimeout(() => this.sendSelection(), 120);
    }

    sendSelection() {
        if ((!this.reviewMode && !this.editMode) || this.abort.signal.aborted) {
            return;
        }

        const selected = this.selectedText();
        const key = selected ? selected.join(',') : '';
        if (key === this.lastSelection) {
            return;
        }

        this.lastSelection = key;
        this.dotNet.invokeMethodAsync('OnReviewSelection', selected ?? []).catch(() => {});
    }

    // The selection as [first page, start, last page, end], or null when none of it is page text.
    selectedText() {
        const selection = getSelection();
        if (!selection || selection.rangeCount === 0 || selection.isCollapsed) {
            return null;
        }

        const range = selection.getRangeAt(0);
        if (!range.intersectsNode(this.scroller)) {
            return null;
        }

        let first = null;
        let last = null;
        for (const page of this.pages) {
            if (isBuilt(page.layer) && range.intersectsNode(page.layer)) {
                first ??= page;
                last = page;
            }
        }

        if (!first) {
            return null;
        }

        // An end outside the layers — a selection dragged past the last page, or made by Ctrl+A — takes
        // in the whole of the page it reaches.
        const start = first.layer.contains(range.startContainer)
            ? textOffset(first.layer, range.startContainer, range.startOffset)
            : 0;
        const end = last.layer.contains(range.endContainer)
            ? textOffset(last.layer, range.endContainer, range.endOffset)
            : textLength(last.layer);
        if (start < 0 || end < 0 || (first === last && end <= start)) {
            return null;
        }

        return [first.index, start, last.index, end];
    }

    // A click puts the caret beside the nearest character even when it lands on blank paper, so the
    // character counts only if the click was on it.
    hitTest(event, layer) {
        const selection = getSelection();
        const page = this.pages.find(_ => _.layer === layer);
        if (!page || !selection || selection.rangeCount === 0 || !selection.isCollapsed) {
            return;
        }

        const offset = textOffset(layer, selection.anchorNode, selection.anchorOffset);
        if (offset < 0) {
            return;
        }

        for (const candidate of [offset, offset - 1]) {
            if (candidate < 0 || candidate >= textLength(layer)) {
                continue;
            }

            const range = textRange(layer, candidate, 1);
            for (const box of range?.getClientRects() ?? []) {
                if (event.clientX >= box.left - 1 && event.clientX <= box.right + 1 &&
                    event.clientY >= box.top - 1 && event.clientY <= box.bottom + 1) {
                    this.dotNet.invokeMethodAsync('OnReviewHit', page.index, candidate).catch(() => {});
                    return;
                }
            }
        }
    }

    // Editing: a click on a page, while editing is on, is sent to .NET as the point it landed on and
    // the text it selected; .NET answers with the paragraph there (beginEdit), and an editor is laid
    // over it. What is typed is handed in when the reader moves on (OnEditCommit), and the editor
    // stays up until the page has been drawn again.

    async setEditMode(on) {
        if (!on) {
            await this.closeEditor();
        }

        this.editMode = on;
        this.lastSelection = '';
        if (on) {
            this.root.dataset.edit = 'on';
            this.onSelectionChange();
        } else {
            delete this.root.dataset.edit;
            this.showEditState(null);
        }

        // The editing bar takes its height from the pages.
        this.onResize();
    }

    // Hands in what is being typed, if anything is, and resolves once .NET has dealt with it.
    async closeEditor() {
        const editor = this.editor;
        if (editor && !editor.closed) {
            await editor.commit(0);
        }
    }

    dropEditors() {
        for (const editor of [this.editor, ...this.leaving]) {
            editor?.dispose();
        }

        this.editor = null;
        this.leaving = [];
        this.showEditState(null);
    }

    beginEdit(documentId, json) {
        if (documentId !== this.documentId || !this.editMode) {
            return;
        }

        const session = JSON.parse(json);
        const page = this.pages[session.page];
        if (!page) {
            return;
        }

        if (this.editor && !this.editor.closed && !this.editor.pending) {
            this.editor.cancel();
        }

        getSelection()?.removeAllRanges();
        this.editor = new ParagraphEditor(page.element.querySelector('.viewer-sheet'), page, session, {
            commit: (editor, payload, then) => this.handIn(editor, payload, then),
            cancel: id => {
                this.dotNet.invokeMethodAsync('OnEditCancel', id).catch(() => {});
            },
            state: state => this.showEditState(state),
            leave: (direction, x, y, line) => this.leaveEditor(direction, x, y, line)
        });
    }

    // The editor stays where it is, showing what was typed, until the page under it shows the same.
    async handIn(editor, payload, then) {
        const before = this.documentId;
        this.leaving.push(editor);
        try {
            await this.dotNet.invokeMethodAsync('OnEditCommit', editor.session.id, payload, then);
        } catch {
            // The component is gone.
        }

        if (this.documentId === before) {
            // Nothing was laid out again: the edit was not made, and there is nothing to wait for.
            this.release(editor);
            return;
        }

        editor.replaced = true;
        setTimeout(() => this.release(editor), 20000);
        if (this.pages[editor.page.index]?.dpi > 0) {
            this.release(editor);
        }
    }

    release(editor) {
        editor.dispose();
        this.leaving = this.leaving.filter(_ => _ !== editor);
        if (this.editor === editor) {
            this.editor = null;
        }
    }

    // A page has been drawn: what was typed on it is now part of the picture.
    settle(page) {
        for (const editor of this.leaving) {
            if (editor.replaced && editor.page.index === page.index) {
                this.release(editor);
            }
        }
    }

    // Which of the editing bar's buttons are pressed: null when no paragraph is open.
    showEditState(state) {
        for (const button of this.root.querySelectorAll('[data-edit-command]')) {
            const name = button.dataset.editCommand;
            if (name === 'track' || name === 'undo' || name === 'redo') {
                continue;
            }

            if (!state) {
                button.removeAttribute('aria-pressed');
            } else if (name.startsWith('align-')) {
                button.setAttribute('aria-pressed', String(['left', 'center', 'right', 'justify'][state.align] === name.slice(6)));
            } else {
                button.setAttribute('aria-pressed', String(state[name] === true));
            }
        }
    }

    // A button of the editing bar, or its key. With a paragraph open it is the editor's, if the
    // editor knows it; otherwise it is for what is selected on the pages, which is .NET's.
    async editCommand(name) {
        const editor = this.editor;
        if (editor && !editor.closed && !editor.pending) {
            if (editor.command(name)) {
                return;
            }

            await editor.commit(0);
        }

        this.dotNet.invokeMethodAsync('OnEditCommand', name).catch(() => {});
    }

    // Keys while editing is on and no paragraph is open. Says whether the key was one of them.
    onEditKey(event) {
        const command = (event.ctrlKey || event.metaKey) && !event.altKey;
        const key = event.key.toLowerCase();
        let name = null;
        if (command) {
            name = { b: 'bold', i: 'italic', u: 'underline', y: 'redo', z: event.shiftKey ? 'redo' : 'undo' }[key] ?? null;
        } else if ((event.key === 'Delete' || event.key === 'Backspace') && this.selectedText()) {
            name = 'delete';
        }

        if (!name) {
            return false;
        }

        event.preventDefault();
        this.editCommand(name);
        return true;
    }

    pageOf(target) {
        const element = target instanceof Element ? target.closest('.viewer-page') : null;
        return element ? this.pages[Number(element.dataset.pageNumber) - 1] : null;
    }

    // A point of the window as a point of a page, in points from its top left as it was laid out.
    pagePoint(page, clientX, clientY) {
        const box = page.element.getBoundingClientRect();
        const [u, v] = toPage(
            (clientX - box.left) / Math.max(1, box.width),
            (clientY - box.top) / Math.max(1, box.height),
            this.rotation);
        return [u * page.width, v * page.height];
    }

    // The offset into a page's text of the character nearest a point; -1 where there is none to find.
    caretOffset(page, clientX, clientY) {
        if (!isBuilt(page.layer)) {
            return -1;
        }

        let node = null;
        let offset = 0;
        if (document.caretPositionFromPoint) {
            const position = document.caretPositionFromPoint(clientX, clientY);
            node = position?.offsetNode ?? null;
            offset = position?.offset ?? 0;
        } else if (document.caretRangeFromPoint) {
            const range = document.caretRangeFromPoint(clientX, clientY);
            node = range?.startContainer ?? null;
            offset = range?.startOffset ?? 0;
        }

        return node && page.layer.contains(node) ? textOffset(page.layer, node, offset) : -1;
    }

    async onEditClick(event) {
        const page = this.pageOf(event.target);
        if (!page) {
            return;
        }

        // What the click selected, read before anything is awaited: a click puts the caret at the
        // nearest character, and a drag or a double click selects.
        let start = -1;
        let end = -1;
        const selection = getSelection();
        if (selection && selection.rangeCount > 0 && isBuilt(page.layer)) {
            const range = selection.getRangeAt(0);
            if (page.layer.contains(range.startContainer) && page.layer.contains(range.endContainer)) {
                start = textOffset(page.layer, range.startContainer, range.startOffset);
                end = textOffset(page.layer, range.endContainer, range.endOffset);
            } else if (!selection.isCollapsed) {
                // Text selected across pages, or out of them: for the toolbar, not for typing.
                return;
            }
        }

        const before = this.documentId;
        await this.closeEditor();
        if (this.documentId !== before) {
            // The pages were laid out again, and their text with them.
            this.ensureText(page);
            start = end = this.caretOffset(page, event.clientX, event.clientY);
        }

        await this.sendHit(page, event.clientX, event.clientY, start, end);
    }

    async sendHit(page, clientX, clientY, start, end) {
        const [x, y] = this.pagePoint(page, clientX, clientY);
        try {
            return await this.dotNet.invokeMethodAsync('OnEditHit', page.index, x, y, start, end);
        } catch {
            return false;
        }
    }

    // An arrow key took the caret out of the top or the bottom of a paragraph: the paragraph that way
    // is the first text a point moved that way, half a line at a time, comes to.
    async leaveEditor(direction, clientX, clientY, line) {
        await this.closeEditor();
        const step = Math.max(4, line / 2);
        for (let tries = 1; tries <= 16; tries++) {
            const y = clientY + direction * step * tries;
            const element = document.elementFromPoint(clientX, y);
            const page = element ? this.pageOf(element) : null;
            if (!page || element.closest('.edit-box')) {
                continue;
            }

            this.ensureText(page);
            const offset = this.caretOffset(page, clientX, y);
            if (await this.sendHit(page, clientX, y, offset, offset)) {
                return;
            }
        }
    }

    // Printing: .NET renders every page at the print resolution and streams them here; the pages go into
    // a container of their own that print CSS shows alone.

    beginPrint(sizes) {
        this.cancelPrint();
        const count = sizes.length / 2;
        const names = new Map();
        const pageNames = [];
        for (let index = 0; index < count; index++) {
            const key = `${sizes[2 * index]}x${sizes[2 * index + 1]}`;
            if (!names.has(key)) {
                names.set(key, `morph-print-${names.size}`);
            }

            pageNames.push(names.get(key));
        }

        // One @page size for a uniform document; mixed sizes (a landscape section) need named pages,
        // which the browsers without them ignore in favour of the first size.
        let rules = `@page { size: ${sizes[0]}pt ${sizes[1]}pt; margin: 0; }`;
        if (names.size > 1) {
            let named = '';
            for (const [key, name] of names) {
                const [width, height] = key.split('x');
                named += `@page ${name} { size: ${width}pt ${height}pt; margin: 0; }`;
            }

            rules += `@supports (page: auto) { ${named} }`;
        }

        // A constructed sheet, so a strict style-src policy does not block it.
        const sheet = new CSSStyleSheet();
        sheet.replaceSync(rules);
        document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];

        const container = document.createElement('div');
        container.className = 'print-container';
        container.dataset.pageCount = String(count);
        document.body.append(container);
        this.print = { container, sheet, sizes, pageNames, named: names.size > 1, urls: [], images: [], started: performance.now() };
        this.queueLength = 0;
        this.updateBusy();
    }

    addPrintPage(pageIndex, png) {
        const print = this.print;
        if (!print) {
            return;
        }

        const url = URL.createObjectURL(new Blob([png], { type: 'image/png' }));
        print.urls.push(url);
        const page = document.createElement('div');
        page.className = 'print-page';
        let css = `width:${print.sizes[2 * pageIndex]}pt;height:${print.sizes[2 * pageIndex + 1]}pt`;
        if (print.named) {
            css += `;page:${print.pageNames[pageIndex]}`;
        }

        page.style.cssText = css;
        const image = document.createElement('img');
        image.alt = '';
        image.src = url;
        page.append(image);
        print.container.append(page);
        print.images.push(image);
    }

    async finishPrint() {
        const print = this.print;
        if (!print) {
            return;
        }

        await Promise.all(print.images.map(_ => _.complete ? null : new Promise(resolve => {
            _.onload = resolve;
            _.onerror = resolve;
        })));

        // Safari only lets a page print from a recent user gesture; after a long preparation, ask for one.
        const isSafari = /^((?!chrome|chromium|android).)*safari/i.test(navigator.userAgent);
        if (isSafari && performance.now() - print.started > 2000 && !(await this.confirmPrint())) {
            this.cancelPrint();
            return;
        }

        if (this.print !== print) {
            return;
        }

        const closed = new Promise(resolve => {
            const media = matchMedia('print');
            const finish = () => {
                window.removeEventListener('afterprint', finish);
                media.removeEventListener('change', onChange);
                resolve();
            };
            const onChange = event => {
                if (!event.matches) {
                    finish();
                }
            };
            window.addEventListener('afterprint', finish);
            media.addEventListener('change', onChange);
        });

        document.documentElement.dataset.morphPrinting = '';
        // Chromium blocks inside print() and fires afterprint before it returns; the other engines return
        // at once and fire it when the dialog closes.
        window.print();
        await closed;
        if (this.print === print) {
            this.cancelPrint();
        }
    }

    confirmPrint() {
        return new Promise(resolve => {
            const panel = document.createElement('div');
            panel.className = 'viewer-print-ready';
            const text = document.createElement('span');
            text.textContent = 'Ready to print';
            const go = document.createElement('button');
            go.type = 'button';
            go.textContent = 'Print';
            const cancel = document.createElement('button');
            cancel.type = 'button';
            cancel.textContent = 'Cancel';
            const finish = answer => {
                panel.remove();
                resolve(answer);
            };
            go.addEventListener('click', () => finish(true));
            cancel.addEventListener('click', () => finish(false));
            panel.append(text, go, cancel);
            this.root.append(panel);
            go.focus();
        });
    }

    cancelPrint() {
        const print = this.print;
        if (!print) {
            return;
        }

        this.print = null;
        delete document.documentElement.dataset.morphPrinting;
        print.container.remove();
        document.adoptedStyleSheets = document.adoptedStyleSheets.filter(_ => _ !== print.sheet);
        for (const url of print.urls) {
            URL.revokeObjectURL(url);
        }

        this.scheduleQueue(0);
    }
}
