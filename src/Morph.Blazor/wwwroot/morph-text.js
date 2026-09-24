// The selectable text layer — the technique PDF.js uses to make a rendered page selectable. The page stays
// a picture; over it sits a transparent copy of every run of text, placed where the painter drew it, so the
// browser can select, copy and find it. The runs arrive from .NET (TextLayerBuilder) as JSON in points.
//
// Layout rules, each learned the hard way:
// - One absolutely positioned element per LINE, its runs inline. The browser's find cannot match across
//   two absolutely positioned elements, so a word-per-element layer would make every phrase unfindable.
// - Every length is baked in cqw of the layer (a size container), so the layer scales with the image in
//   the converter's fit-to-width preview and at every viewer zoom without any script running on resize.
// - Widths are fitted with letter-spacing on the gaps, never with transforms (inline boxes cannot take
//   them) and never on ordinary words (that would break ligatures): the text is Aptos, the same face the
//   raster was drawn in, so natural advances already agree and only removed spaces need re-spacing.
// - The layer owns copying (see onCopy), so every engine yields the same plain text — and no rich-text
//   flavour, which would paste invisible transparent-coloured text into an editor.

const family = 'Morph Text';
const faces = [
    { file: 'Aptos_400.ttf', weight: '400', style: 'normal' },
    { file: 'Aptos_400_Italic.ttf', weight: '400', style: 'italic' },
    { file: 'Aptos_700.ttf', weight: '700', style: 'normal' },
    { file: 'Aptos_700_Italic.ttf', weight: '700', style: 'italic' },
];

// Aptos's own vertical metrics (hhea and typo agree: 1923/-577 on a 2048 em), used if measuring fails.
const fallbackAscent = 1923 / 2048;
const fallbackDescent = 577 / 2048;

// Widths are measured at this size and scaled: text width is linear in font size.
const referenceSize = 100;

let fontsReady = null;

// Loads the four faces once per page load. fetch() rather than a CSS @font-face so the request matches a
// host's <link rel="preload" as="fetch" crossorigin> (a font request would not reuse it). A failure falls
// back to the browser's default face: the layer still works, only less precisely.
export function ensureFonts() {
    fontsReady ??= Promise.all(faces.map(loadFace)).then(
        () => undefined,
        error => console.warn('Morph: the text layer fonts did not load; selection geometry falls back to the default face.', error));
    return fontsReady;
}

async function loadFace(face) {
    const response = await fetch(new URL(`fonts/${face.file}`, import.meta.url));
    if (!response.ok) {
        throw new Error(`${response.status} fetching ${face.file}`);
    }

    const font = new FontFace(family, await response.arrayBuffer(), { weight: face.weight, style: face.style });
    await font.load();
    document.fonts.add(font);
}

let measureContext = null;

function measurer() {
    if (!measureContext) {
        measureContext = document.createElement('canvas').getContext('2d');
        // The DOM kerns by default; so must the measurement, or every fitted gap drifts by the kerning.
        if ('fontKerning' in measureContext) {
            measureContext.fontKerning = 'normal';
        }

        if ('textRendering' in measureContext) {
            measureContext.textRendering = 'geometricPrecision';
        }
    }

    return measureContext;
}

// face: bit 1 bold, bit 2 italic — the same bits as a span's flags.
function fontFor(face) {
    return `${face & 2 ? 'italic' : 'normal'} ${face & 1 ? 700 : 400} ${referenceSize}px "${family}", sans-serif`;
}

const widths = new Map();

// Natural width of text in ems of the given face.
function widthOf(face, text) {
    const key = face + text;
    let width = widths.get(key);
    if (width === undefined) {
        if (widths.size > 100000) {
            widths.clear();
        }

        const context = measurer();
        context.font = fontFor(face);
        width = context.measureText(text).width / referenceSize;
        widths.set(key, width);
    }

    return width;
}

const metricsByFace = [];

function metricsOf(face) {
    let metrics = metricsByFace[face];
    if (!metrics) {
        const context = measurer();
        context.font = fontFor(face);
        const measured = context.measureText('Hg');
        const ascent = measured.fontBoundingBoxAscent / referenceSize;
        const descent = measured.fontBoundingBoxDescent / referenceSize;
        metrics = Number.isFinite(ascent) && ascent > 0 && Number.isFinite(descent)
            ? { ascent, descent }
            : { ascent: fallbackAscent, descent: fallbackDescent };
        metricsByFace[face] = metrics;
    }

    return metrics;
}

// Every built layer, with its offset index (character offset → DOM node, for find highlights) and its
// end-of-content element. Pruned of disconnected layers as selection events arrive.
const layers = new Map();

/**
 * Builds a page's text layer into element (an empty, absolutely positioned overlay the size of the page
 * image). json is the TextLayerBuilder output, as a string or already parsed.
 */
export async function renderTextLayer(element, json) {
    await ensureFonts();
    buildTextLayer(element, typeof json === 'string' ? JSON.parse(json) : json);
}

/** Synchronous build; the caller has awaited ensureFonts(). Replaces anything already in the element. */
export function buildTextLayer(element, page) {
    const state = { unit: 100 / page.w, index: [], length: 0, end: null };
    const fragment = document.createDocumentFragment();
    buildItems(page.i, fragment, state);

    // The end-of-content element: while a selection is being dragged it covers the empty parts of the page,
    // so dragging over a gap between lines keeps extending the selection instead of jumping to the start.
    const end = document.createElement('div');
    end.className = 'text-end';
    end.setAttribute('aria-hidden', 'true');
    fragment.append(end);
    state.end = end;

    element.replaceChildren(fragment);
    element.dataset.textState = 'ready';
    layers.set(element, state);
    installListeners();
}

export function clearTextLayer(element) {
    layers.delete(element);
    element.replaceChildren();
    delete element.dataset.textState;
}

/** A DOM range over [start, start + length) of the layer's text — the offsets TextSearch reports. */
export function textRange(element, start, length) {
    const state = layers.get(element);
    if (!state || state.index.length === 0) {
        return null;
    }

    const from = locate(state, start, false);
    const to = locate(state, start + length, true);
    const range = document.createRange();
    range.setStart(from[0], from[1]);
    range.setEnd(to[0], to[1]);
    return range;
}

/** Whether element currently holds a built text layer. */
export function isBuilt(element) {
    return layers.has(element);
}

function locate(state, offset, isEnd) {
    const index = state.index;
    let low = 0;
    let high = index.length - 1;
    while (low < high) {
        const middle = (low + high + 1) >> 1;
        if (index[middle].start <= offset) {
            low = middle;
        } else {
            high = middle - 1;
        }
    }

    // An end landing exactly on a node's first character belongs to the end of the node before it.
    let entry = index[low];
    if (isEnd && offset === entry.start && low > 0) {
        entry = index[low - 1];
    }

    const local = offset - entry.start;
    if (entry.node.nodeType === Node.TEXT_NODE) {
        return [entry.node, Math.max(0, Math.min(local, entry.node.data.length))];
    }

    // A <br>: before it at its own offset, after it past it.
    const parent = entry.node.parentNode;
    const position = Array.prototype.indexOf.call(parent.childNodes, entry.node);
    return [parent, local <= 0 ? position : position + 1];
}

function cq(state, value) {
    return `${(value * state.unit).toFixed(4)}cqw`;
}

function buildItems(items, parent, state) {
    for (const item of items) {
        switch (item.k) {
            case 'r':
            case 'c':
                buildFrame(item, parent, state);
                break;
            case 'b':
                buildBox(item, parent, state);
                break;
            default:
                buildLine(item, parent, state);
                break;
        }
    }
}

// A rotated text box or cell (rotated about its centre, as the painter does), or a clipped table cell.
// Children are in the frame's own coordinates.
function buildFrame(item, parent, state) {
    const frame = document.createElement('div');
    frame.className = item.k === 'r' ? 'text-frame' : item.cx ? 'text-frame text-clip' : 'text-frame text-clip-y';
    let css = `left:${cq(state, item.x)};top:${cq(state, item.y)};width:${cq(state, item.w)};height:${cq(state, item.h)}`;
    if (item.k === 'r' && item.r) {
        css += `;transform:rotate(${item.r}deg)`;
    }

    frame.style.cssText = css;
    buildItems(item.i, frame, state);
    parent.append(frame);
}

function faceClass(base, face) {
    let name = base;
    if (face & 1) {
        name += ' b';
    }

    if (face & 2) {
        name += ' i';
    }

    return name.trim();
}

function buildLine(line, parent, state) {
    const spans = line.s;
    if (spans.length === 0) {
        if (line.e === 2) {
            appendBreak(parent, state);
        } else if (line.e === 1 || line.e === 3) {
            // An empty table cell: nothing is drawn, but its tab keeps a pasted row's columns in place.
            const size = Math.min(line.h, 10);
            const element = document.createElement('span');
            element.className = 'text-line text-empty';
            element.style.cssText = `left:${cq(state, line.x)};top:${cq(state, line.y)};font-size:${cq(state, size)};line-height:${cq(state, line.h)}`;
            appendEnd(element, line.e, state);
            parent.append(element);
        }

        return;
    }

    let size = 0;
    for (const span of spans) {
        if (span.z > size) {
            size = span.z;
        }
    }

    const lineFace = (spans[0].f ?? 0) & 3;

    // CSS puts the baseline of a line-height-h box h/2 + (ascent - (ascent + descent)/2)·size below its
    // top. Solving for the top that lands it on the drawn baseline keeps the glyph box on the raster glyphs
    // — which matters: Firefox paints a selection over that box, not the line box.
    const { ascent, descent } = metricsOf(lineFace);
    const top = line.b - line.h / 2 - (ascent - (ascent + descent) / 2) * size;

    const element = document.createElement('span');
    element.className = faceClass('text-line', lineFace);
    element.style.cssText = `left:${cq(state, spans[0].x)};top:${cq(state, top)};font-size:${cq(state, size)};line-height:${cq(state, line.h)}`;

    let pen = spans[0].x;
    for (const span of spans) {
        const flags = span.f ?? 0;
        const face = flags & 3;
        const separator = (flags & 4) !== 0;
        const tab = separator && span.t === '\t';

        // A tab separator shows as a space and copies as a tab (onCopy): a literal tab would render at the
        // element's tab-size and could not be fitted to its gap.
        const text = tab ? ' ' : span.t;
        const natural = widthOf(face, text) * span.z;
        let spacing = 0;
        let margin = 0;
        let start = pen;
        let advance = natural;
        if (separator) {
            // Stretch (or squeeze) the space so the next word starts exactly where it was drawn.
            spacing = span.x + span.w - pen - natural;
            advance = natural + spacing;
        } else {
            // Re-anchor a run that drifted from its drawn x (the layout's pen grid against the browser's
            // unquantised advances), and fit a run whose glyphs came from a fallback font.
            if (Math.abs(span.x - pen) > 0.1) {
                margin = span.x - pen;
                start = span.x;
            }

            const error = span.w - natural;
            if (Math.abs(error) > Math.max(0.5, 0.03 * span.w)) {
                spacing = error / [...text].length;
                advance = span.w;
            }
        }

        const own = separator || face !== lineFace || span.z !== size || span.d || margin !== 0 || spacing !== 0;
        if (own) {
            const child = document.createElement('span');
            const name = faceClass(tab ? 'text-tab' : '', face);
            if (name) {
                child.className = name;
            }

            let css = '';
            if (span.z !== size) {
                css += `font-size:${cq(state, span.z)};`;
            }

            if (span.d) {
                css += `vertical-align:${cq(state, span.d)};`;
            }

            if (margin !== 0) {
                css += `margin-left:${cq(state, margin)};`;
            }

            if (spacing !== 0) {
                css += `letter-spacing:${cq(state, spacing)};`;
            }

            if (css) {
                child.style.cssText = css;
            }

            appendText(child, text, state);
            element.append(child);
        } else {
            appendText(element, text, state);
        }

        pen = start + advance;
    }

    appendEnd(element, line.e, state);
    parent.append(element);
    if (line.e === 2) {
        appendBreak(parent, state);
    }
}

// Warped WordArt: one figure with no line geometry, so the text is sized to span the box.
function buildBox(item, parent, state) {
    const natural = widthOf(0, item.t) || 1;
    const size = Math.min(item.h, item.w / natural);
    const element = document.createElement('span');
    element.className = 'text-line text-box';
    element.style.cssText = `left:${cq(state, item.x)};top:${cq(state, item.y + (item.h - size) / 2)};font-size:${cq(state, size)};line-height:${cq(state, size)}`;
    appendText(element, item.t, state);
    appendEnd(element, item.e, state);
    parent.append(element);
    if (item.e === 2) {
        appendBreak(parent, state);
    }
}

// What a line copies as after its text: 1 a space (a wrap), 3 a tab (a table cell); 2's line break is
// the <br> placed after the line element. Each marker is a span of its own, so the line's drawn text
// ends where its last span does.
function appendEnd(element, end, state) {
    if (end === 1 || end === 3) {
        const marker = document.createElement('span');
        marker.className = end === 1 ? 'text-wrap' : 'text-tab';
        appendText(marker, ' ', state);
        element.append(marker);
    }
}

function appendText(parent, text, state) {
    const node = document.createTextNode(text);
    parent.append(node);
    state.index.push({ node, start: state.length });
    state.length += text.length;
}

function appendBreak(parent, state) {
    const lineBreak = document.createElement('br');
    parent.append(lineBreak);
    state.index.push({ node: lineBreak, start: state.length });
    state.length += 1;
}

// Selection and copy handling, installed once for every layer on the page.

let listening = false;
let pointerLayer = null;
let previousRange = null;

// Gecko hit-tests positioned text well enough on its own; moving the end-of-content element is for
// Blink and WebKit.
const isGecko = typeof CSS !== 'undefined' && CSS.supports('-moz-appearance', 'none');

function installListeners() {
    if (listening) {
        return;
    }

    listening = true;
    document.addEventListener('copy', onCopy);
    document.addEventListener('selectionchange', onSelectionChange);
    document.addEventListener('pointerdown', onPointerDown, true);
    document.addEventListener('pointerup', onPointerUp, true);
    document.addEventListener('keyup', () => {
        if (!pointerLayer) {
            resetEnds();
        }
    });
    window.addEventListener('blur', onPointerUp);
}

function onPointerDown(event) {
    const layer = event.target instanceof Element ? event.target.closest('.text-layer') : null;
    if (layer && layers.has(layer)) {
        pointerLayer = layer;
        layer.classList.add('selecting');
    }
}

function onPointerUp() {
    pointerLayer = null;
    resetEnds();
}

function onSelectionChange() {
    const selection = document.getSelection();
    if (!selection || selection.rangeCount === 0) {
        resetEnds();
        return;
    }

    const touched = new Set();
    for (let rangeIndex = 0; rangeIndex < selection.rangeCount; rangeIndex++) {
        const range = selection.getRangeAt(rangeIndex);
        for (const layer of layers.keys()) {
            if (!layer.isConnected) {
                layers.delete(layer);
            } else if (range.intersectsNode(layer)) {
                touched.add(layer);
            }
        }
    }

    for (const layer of layers.keys()) {
        layer.classList.toggle('selecting', touched.has(layer) || layer === pointerLayer);
    }

    if (!isGecko && pointerLayer) {
        moveEnd(selection.getRangeAt(0));
    }
}

// Places the end-of-content element just after the line the moving end of the selection is in (or just
// before it, when the start is moving), so dragging into empty space resolves next to that line rather
// than to the page's first or last line.
function moveEnd(range) {
    const startMoved = previousRange !== null &&
        (range.compareBoundaryPoints(Range.END_TO_END, previousRange) === 0 ||
         range.compareBoundaryPoints(Range.START_TO_END, previousRange) === 0);
    previousRange = range.cloneRange();

    let anchor = startMoved ? range.startContainer : range.endContainer;
    if (anchor.nodeType === Node.TEXT_NODE) {
        anchor = anchor.parentNode;
    }

    if (!startMoved && range.endOffset === 0) {
        // The end sits before the first character of anchor: the text actually selected ends in the
        // nearest earlier node that has any.
        do {
            while (anchor && !anchor.previousSibling) {
                anchor = anchor.parentNode;
            }

            anchor = anchor?.previousSibling;
        } while (anchor && !anchor.childNodes.length);
    }

    const layer = anchor instanceof Element ? anchor.closest('.text-layer') : anchor?.parentElement?.closest('.text-layer');
    const state = layer ? layers.get(layer) : null;
    if (!state || anchor === state.end) {
        return;
    }

    // Climb to the item that sits directly in the layer or in a frame.
    let item = anchor;
    while (item.parentElement && item.parentElement !== layer && !item.parentElement.classList.contains('text-frame')) {
        item = item.parentElement;
    }

    if (!item.parentElement) {
        return;
    }

    state.end.style.cssText = 'user-select:text;width:100%;height:100%';
    item.parentElement.insertBefore(state.end, startMoved ? item : item.nextSibling);
}

function resetEnds() {
    previousRange = null;
    for (const [layer, state] of layers) {
        layer.classList.remove('selecting');
        if (state.end.parentElement !== layer || state.end.nextSibling !== null) {
            layer.append(state.end);
        }

        state.end.style.cssText = '';
    }
}

// Serialises the selection's text-layer content: text nodes, a newline per <br>, a tab for each tab
// separator, and a newline where the selection crosses into another page.
function onCopy(event) {
    const selection = document.getSelection();
    if (!selection || selection.isCollapsed || selection.rangeCount === 0) {
        return;
    }

    const pieces = [];
    let handled = false;
    let lastLayer = null;
    for (let rangeIndex = 0; rangeIndex < selection.rangeCount; rangeIndex++) {
        const range = selection.getRangeAt(rangeIndex);
        let root = range.commonAncestorContainer;
        if (root.nodeType !== Node.ELEMENT_NODE) {
            root = root.parentNode;
        }

        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT, {
            acceptNode: node => node.nodeType === Node.TEXT_NODE || node.nodeName === 'BR'
                ? NodeFilter.FILTER_ACCEPT
                : NodeFilter.FILTER_SKIP
        });

        for (let node = walker.nextNode(); node; node = walker.nextNode()) {
            if (!range.intersectsNode(node)) {
                continue;
            }

            const element = node.nodeType === Node.TEXT_NODE ? node.parentElement : node;
            const layer = element?.closest('.text-layer');
            if (!layer) {
                continue;
            }

            handled = true;
            if (lastLayer && layer !== lastLayer && pieces.length > 0 && !pieces[pieces.length - 1].endsWith('\n')) {
                pieces.push('\n');
            }

            lastLayer = layer;
            if (node.nodeName === 'BR') {
                pieces.push('\n');
                continue;
            }

            let text = node.data;
            const start = node === range.startContainer ? range.startOffset : 0;
            const end = node === range.endContainer ? range.endOffset : text.length;
            text = text.slice(start, end);
            if (element.classList.contains('text-tab')) {
                text = text.replaceAll(' ', '\t');
            }

            pieces.push(text);
        }
    }

    if (!handled) {
        return;
    }

    event.clipboardData.setData('text/plain', pieces.join(''));
    event.preventDefault();
}
