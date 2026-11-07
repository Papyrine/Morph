// The in-place editor — one paragraph of a Word document, open for typing where it stands on its page.
//
// The page is a picture, and stays one. Over the paragraph being edited goes a box holding the same text
// in the same face at the same size, set to the same measure, so that it sits on the picture of itself;
// the reader types there, in the browser, at the browser's speed. Nothing reaches .NET until they move
// on. Then the paragraph as it now reads is handed over (commit), .NET writes it into the file and lays
// the file out again, and the box stays up, no longer editable, until the page's new picture arrives to
// take its place — so the text never flickers back to what it was.
//
// What is edited is a model, not the DOM: a paragraph is a list of items, each a stretch of text like
// one of the paragraph's units (formatted as that unit's run is) or a thing that is not text — a field,
// a picture, a note's reference, text a tracked change deleted — which is shown, kept in its place, and
// cannot be typed into or deleted. Every input is taken from the browser before it touches the DOM
// (beforeinput), applied to the model, and the box redrawn from it. The one exception is composition —
// an input method, and nearly all typing on a phone — which cannot be taken from the browser: there
// the DOM is left to change and the model is read back out of it when the composition ends.
//
// A place in the paragraph is {b, o}: block b (the paragraph, and the paragraphs Enter has since split
// from it) and o places into it, where each character is a place and so is each thing that is not text.
// That is the count .NET uses too (EditParagraph.Length), which is how the two agree on where a caret is.

import { faceMetrics, naturalWidth } from './morph-text.js';
import { completion, corrections, numberLeader, smartQuote } from './morph-autocorrect.js';

const bold = 1;
const italic = 2;
const underline = 4;
const strike = 8;
const caps = 16;
const smallCaps = 32;

const faces = { bold, italic, underline, strike };
const alignments = ['left', 'center', 'right', 'justify'];
const historyLimit = 200;

// Room either side of the text, in points, so a caret at the start of a line is not cut off.
const gutter = 2;

// Default tab stops are half an inch apart.
const tabStop = 36;

function copy(blocks) {
    return blocks.map(_ => ({ align: _.align, items: _.items.map(item => ({ ...item })) }));
}

function lengthOf(item) {
    return item.atom ? 1 : item.t.length;
}

function blockLength(block) {
    let length = 0;
    for (const item of block.items) {
        length += lengthOf(item);
    }

    return length;
}

function before(first, second) {
    return first.b < second.b || (first.b === second.b && first.o < second.o);
}

// The place a character on from o in a block's text, or back from it: a surrogate pair — an emoji, a
// letter beyond the Basic Multilingual Plane — is one character, never split.
function stepOn(text, o) {
    const high = text.charCodeAt(o);
    const low = text.charCodeAt(o + 1);
    return o + (high >= 0xD800 && high <= 0xDBFF && low >= 0xDC00 && low <= 0xDFFF ? 2 : 1);
}

function stepBack(text, o) {
    const low = text.charCodeAt(o - 1);
    const high = text.charCodeAt(o - 2);
    return o - (low >= 0xDC00 && low <= 0xDFFF && high >= 0xD800 && high <= 0xDBFF ? 2 : 1);
}

// A Mac's keys: Command holds Word's commands where Windows has Ctrl, Option steps by words, and Command
// with an arrow goes to a line's or the story's end. Control there is the system's own — its Emacs keys,
// Control+A, E and K among them — and is left to the browser.
function onMac() {
    const platform = navigator.userAgentData?.platform || navigator.platform || '';
    return /^(mac|iphone|ipad|ipod)/i.test(platform);
}

// Whether the key Word's commands are held with is down: Command on a Mac, Ctrl elsewhere.
function commandKey(event) {
    return onMac() ? event.metaKey : event.ctrlKey || event.metaKey;
}

// Word's keys that type a character the keyboard has no key for (WordProbe E-98, E-99): Ctrl+Shift+Space
// a non-breaking space, Ctrl+NumMinus an en dash, Ctrl+Alt+. an ellipsis, Ctrl+Alt+E, R and T the euro,
// registered and trade marks. By the key's place, since Alt changes what it types. Null for any other.
function wordCharacter(event) {
    if (!commandKey(event)) {
        return null;
    }

    if (event.altKey) {
        if (event.shiftKey) {
            return null;
        }

        return { Period: '…', KeyE: '€', KeyR: '®', KeyT: '™' }[event.code] ?? null;
    }

    if (event.shiftKey) {
        if (event.code === 'Space') {
            return ' ';
        }

        return null;
    }

    if (event.code === 'NumpadSubtract') {
        return '–';
    }

    return null;
}

function same(first, second) {
    return first.b === second.b && first.o === second.o;
}

// Word's keys for a ribbon command, whether a paragraph is open or text is selected on the pages: a
// `command` the script carries out as the ribbon's button does (data-edit-command), or a `key` .NET
// carries out (MorphViewer.OnEditKey). By the key's place where the character differs by layout or by
// Shift. Null for any other key.
export function wordKey(event) {
    // Alt+Shift+D, P and T: a DATE, PAGE and TIME field at the caret (WordProbe E-100); Alt+Shift+Left
    // and Right, Word's outline keys (WordProbe E-95d, E-95e).
    if (event.altKey && event.shiftKey && !event.ctrlKey && !event.metaKey) {
        const keys = {
            KeyD: 'date-field',
            KeyP: 'page-field',
            KeyT: 'time-field',
            ArrowLeft: 'promote',
            ArrowRight: 'demote',
            ArrowUp: 'move-up',
            ArrowDown: 'move-down'
        };
        if (keys[event.code]) {
            return { key: keys[event.code] };
        }

        return null;
    }

    // Ctrl+Alt+F and D a footnote and an endnote, Ctrl+Alt+M a comment (WordProbe E-103, E-104); on a
    // Mac, Command+Option.
    const other = onMac() ? event.ctrlKey : event.metaKey;
    if (commandKey(event) && event.altKey && !event.shiftKey && !other) {
        return { KeyF: { key: 'footnote' }, KeyD: { key: 'endnote' }, KeyM: { key: 'new-comment' } }[event.code] ?? null;
    }

    if (!commandKey(event) || event.altKey) {
        return null;
    }

    if (event.shiftKey) {
        switch (event.code) {
            case 'Equal':
                return { key: 'superscript' };
            case 'KeyL':
                return { key: 'bullets' };
            // Track Changes on or off (WordProbe E-101).
            case 'KeyE':
                return { command: 'track' };
            // Grow and Shrink Font, along Word's ladder of sizes (WordProbe E-88).
            case 'Period':
                return { key: 'grow' };
            case 'Comma':
                return { key: 'shrink' };
            // All caps, small caps, double and word underline, hidden (WordProbe E-85, E-110).
            case 'KeyA':
                return { key: 'all-caps' };
            case 'KeyK':
                return { key: 'small-caps' };
            case 'KeyD':
                return { key: 'double-underline' };
            case 'KeyW':
                return { key: 'word-underline' };
            case 'KeyH':
                return { key: 'hidden' };
            case 'KeyQ':
                return { key: 'symbol-font' };
            // Normal style, outdent and the hanging indent back (WordProbe E-83, E-91, E-92).
            case 'KeyN':
                return { key: 'normal-style' };
            case 'KeyM':
                return { key: 'decrease-indent' };
            case 'KeyT':
                return { key: 'unhang' };
            // Reset Char, as Ctrl+Space (WordProbe E-110). Cmd+Shift+Z stays the Mac's redo.
            case 'KeyZ':
                if (event.ctrlKey && !event.metaKey) {
                    return { key: 'clear-formatting' };
                }

                return null;
        }

        return null;
    }

    switch (event.code) {
        case 'KeyL':
            return { command: 'align-left' };
        case 'KeyE':
            return { command: 'align-center' };
        case 'KeyR':
            return { command: 'align-right' };
        case 'KeyJ':
            return { command: 'align-justify' };
        // A point at a time (WordProbe E-87).
        case 'BracketLeft':
            return { key: 'shrink-point' };
        case 'BracketRight':
            return { key: 'grow-point' };
        // Indent a stop, a hanging indent a stop, 12pt before on and off (WordProbe E-90, E-91, E-92).
        case 'KeyM':
            return { key: 'increase-indent' };
        case 'KeyT':
            return { key: 'hang' };
        case 'Digit0':
            return { key: 'space-before' };
        // Reset the paragraph's own formatting (WordProbe E-82).
        case 'KeyQ':
            return { key: 'reset-paragraph' };
        case 'Equal':
            return { key: 'subscript' };
        case 'Space':
            return { key: 'clear-formatting' };
        case 'Digit1':
            return { key: 'line-single' };
        case 'Digit2':
            return { key: 'line-double' };
        case 'Digit5':
            return { key: 'line-one-and-half' };
    }

    return null;
}

export class ParagraphEditor {
    /**
     * sheet is the page's own element (.viewer-sheet), session what EditSession wrote, and callbacks
     * { commit(id, payload, then), cancel(id), state(state), leave(direction, x, y), step(paragraph, direction) }.
     */
    constructor(sheet, page, session, callbacks) {
        this.sheet = sheet;
        this.page = page;
        this.session = session;
        this.callbacks = callbacks;
        this.unit = 100 / page.width;
        this.pending = false;
        this.closed = false;
        this.composing = false;
        // When the reader last typed or clicked here: coauthoring waits for a pause (editIdle).
        this.lastInput = performance.now();
        this.handled = false;
        this.pendingFace = null;
        this.undoStack = [];
        this.redoStack = [];
        this.lastKind = '';
        this.lastTime = 0;
        this.abort = new AbortController();

        this.blocks = [{ align: null, items: this.itemsOf(session) }];
        this.original = this.payload();
        this.build();
        this.render();
        this.listen();
        this.focus();
    }

    itemsOf(session) {
        const items = [];
        session.u.forEach((unit, index) => {
            if (unit.k === 0) {
                if (unit.t.length > 0) {
                    items.push({ u: index, t: unit.t, f: unit.f ?? 0 });
                }
            } else {
                items.push({ u: index, atom: true });
            }
        });

        return items;
    }

    // Building

    cq(value) {
        return `${(value * this.unit).toFixed(4)}cqw`;
    }

    build() {
        const session = this.session;
        const layer = document.createElement('div');
        layer.className = 'edit-layer';
        const box = document.createElement('div');
        box.className = 'edit-box';
        box.contentEditable = 'true';
        box.spellcheck = true;
        box.setAttribute('role', 'textbox');
        box.setAttribute('aria-multiline', 'true');
        box.setAttribute('aria-label', 'Paragraph');
        box.dataset.editSession = String(session.id);

        // CSS puts the baseline of a line box of height h at h/2 + (ascent - (ascent + descent)/2)·size
        // below its top, which is not quite where the page drew it; the difference is made up above
        // the first line, so the text sits on its picture.
        const first = session.u.find(_ => _.k === 0 && _.t.length > 0);
        const size = first?.z ?? session.size;
        const { ascent, descent } = faceMetrics((first?.f ?? session.face) & 3);
        // A picture ahead of the text that stands taller than that sits on the baseline from the line
        // box's top, and takes the baseline down with it.
        const lead = first ? session.u.slice(0, session.u.indexOf(first)) : session.u;
        const picture = Math.max(0, ...lead.filter(_ => _.s === 'drawing').map(_ => _.e ?? 0));
        const drawn = Math.max(session.line / 2 + (ascent - (ascent + descent) / 2) * size, picture);
        const shift = session.base - drawn;
        const hang = Math.min(session.indent, 0);
        const top = session.y + Math.min(shift, 0);
        const left = session.x + hang - gutter;

        let css = `left:${this.cq(left)};top:${this.cq(top)};width:${this.cq(session.w - hang + 2 * gutter)};` +
            `padding:${this.cq(Math.max(shift, 0))} ${this.cq(gutter)} 0 ${this.cq(gutter - hang)};` +
            `font-size:${this.cq(session.size)};line-height:${this.cq(session.line)};tab-size:${this.cq(tabStop)};` +
            `color:#${session.color};`;
        const room = Math.max(this.page.height - top, session.h);
        if (session.skip > 0) {
            // The rest of a paragraph begun on the page before: its own lines, and no more room.
            css += `height:${this.cq(session.h + Math.max(shift, 0))};`;
        } else {
            css += `min-height:${this.cq(session.h - Math.min(shift, 0))};max-height:${this.cq(room)};`;
        }

        if (session.fill) {
            css += `background-color:#${session.fill};`;
        }

        box.style.cssText = css;
        layer.append(box);
        this.sheet.append(layer);
        this.layer = layer;
        this.box = box;
        if (session.skip > 0) {
            box.scrollTop = session.skip * layer.clientWidth / this.page.width;
        }
    }

    render() {
        this.unframe();
        const fragment = document.createDocumentFragment();
        this.blocks.forEach((block, index) => fragment.append(this.renderBlock(block, index)));
        this.box.replaceChildren(fragment);
    }

    renderBlock(block, index) {
        const element = document.createElement('p');
        element.className = 'edit-p';
        const align = block.align ?? this.session.align;
        let css = `text-align:${alignments[align] ?? 'left'};`;
        if (this.session.indent !== 0) {
            css += `text-indent:${this.cq(this.session.indent)};`;
        }

        element.style.cssText = css;
        element.dataset.b = String(index);
        block.items.forEach((item, position) => element.append(this.renderItem(item, position)));

        // A line that ends the paragraph, and a paragraph with nothing in it, have no height
        // unless something stands on them.
        const last = block.items[block.items.length - 1];
        if (!last || last.atom || last.t.endsWith('\n')) {
            const fill = document.createElement('br');
            fill.className = 'edit-fill';
            element.append(fill);
        }

        return element;
    }

    renderItem(item, position) {
        const span = document.createElement('span');
        span.dataset.i = String(position);
        if (item.atom) {
            this.renderAtom(span, this.session.u[item.u]);
            return span;
        }

        const like = this.session.u[item.u];
        // Text the page did not draw (hidden text) is shown as Word shows it with its marks on.
        span.className = like?.h ? 'edit-run edit-undrawn' : 'edit-run';
        span.style.cssText = this.faceCss(item.f, like?.z ?? this.session.size, like?.c ?? this.session.color, like?.g, like?.v) +
            this.spacingCss(item, like);
        span.append(document.createTextNode(item.t));
        return span;
    }

    // The page set this text's characters wider or narrower than the face does left to itself — a
    // face standing in for another is set to the other's width — and text that is to break where
    // the page's did has to be spaced to match. One spacing for all the paragraph's text of a size
    // and a face, from all of it: a run of a word or two is too little to measure by, and runs
    // that look alike should be spaced alike. What is typed is spaced as the text it is typed in.
    spacingCss(item, like) {
        if (!like || like.k !== 0) {
            return '';
        }

        this.spacings ??= new Map();
        const key = `${like.z}:${like.f & 3}`;
        let spacing = this.spacings.get(key);
        if (spacing === undefined) {
            let drawn = 0;
            let natural = 0;
            let characters = 0;
            for (const unit of this.session.u) {
                if (unit.k === 0 && unit.a > 0 && unit.t.length > 0 && unit.z === like.z && (unit.f & 3) === (like.f & 3)) {
                    const text = unit.f & caps ? unit.t.toUpperCase() : unit.t;
                    drawn += unit.a * text.length;
                    natural += naturalWidth(unit.f & 3, text) * unit.z;
                    characters += text.length;
                }
            }

            spacing = characters > 0 ? (drawn - natural) / characters : 0;
            if (!Number.isFinite(spacing) || Math.abs(spacing) < 0.01 * like.z) {
                spacing = 0;
            }

            this.spacings.set(key, spacing);
        }

        return spacing === 0 ? '' : `letter-spacing:${this.cq(spacing)};`;
    }

    faceCss(face, size, color, highlight, vertical) {
        let css = `font-size:${this.cq(vertical ? size * 0.65 : size)};color:#${color};`;
        css += `font-weight:${face & bold ? 700 : 400};font-style:${face & italic ? 'italic' : 'normal'};`;
        const lines = [];
        if (face & underline) {
            lines.push('underline');
        }

        if (face & strike) {
            lines.push('line-through');
        }

        css += `text-decoration:${lines.length ? lines.join(' ') : 'none'};`;
        if (face & caps) {
            css += 'text-transform:uppercase;';
        }

        if (face & smallCaps) {
            css += 'font-variant-caps:small-caps;';
        }

        if (highlight) {
            css += `background-color:#${highlight};`;
        }

        if (vertical) {
            css += `vertical-align:${vertical === 1 ? 'super' : 'sub'};`;
        }

        return css;
    }

    renderAtom(span, unit) {
        span.contentEditable = 'false';
        if (unit.k === 2) {
            span.className = 'edit-atom edit-hidden';
            return;
        }

        span.className = `edit-atom edit-${unit.s}`;
        switch (unit.s) {
            case 'drawing':
                if (unit.w > 0 && unit.e > 0) {
                    span.style.cssText = `width:${this.cq(unit.w)};height:${this.cq(unit.e)}`;
                    span.title = 'Picture';
                    if (unit.img) {
                        span.classList.add('edit-picture');
                        span.style.backgroundImage = `url("${unit.img}")`;
                    }
                } else {
                    // Anchored here and drawn elsewhere: it takes a place in the text and no room.
                    span.classList.add('edit-hidden');
                }

                break;
            case 'break':
                span.textContent = 'Page break';
                break;
            default:
                span.style.cssText = this.faceCss(unit.f ?? 0, unit.z ?? this.session.size, unit.c ?? this.session.color, unit.g, unit.v);
                span.textContent = unit.t;
                if (unit.s === 'field') {
                    span.title = 'Field';
                }

                break;
        }
    }

    // Places and the DOM

    blockElement(index) {
        return this.box.children[index];
    }

    toDom(place) {
        const block = this.blocks[place.b];
        const element = this.blockElement(place.b);
        let at = 0;
        for (let index = 0; index < block.items.length; index++) {
            const item = block.items[index];
            const length = lengthOf(item);
            const span = element.children[index];
            if (!item.atom && place.o > at && place.o <= at + length) {
                return [span.firstChild, place.o - at];
            }

            if (place.o === at) {
                // After something that is not text, or at the paragraph's start.
                return item.atom ? [element, index] : [span.firstChild, 0];
            }

            at += length;
        }

        return [element, block.items.length];
    }

    toPlace(node, offset) {
        if (!node || !this.box.contains(node)) {
            return null;
        }

        if (node === this.box) {
            if (offset >= this.blocks.length) {
                const last = this.blocks.length - 1;
                return { b: last, o: blockLength(this.blocks[last]) };
            }

            return { b: offset, o: 0 };
        }

        let element = node;
        while (element.parentNode !== this.box) {
            element = element.parentNode;
        }

        const b = Array.prototype.indexOf.call(this.box.children, element);
        const block = this.blocks[b];
        if (!block) {
            return null;
        }

        if (node === element) {
            let o = 0;
            for (let index = 0; index < offset && index < block.items.length; index++) {
                o += lengthOf(block.items[index]);
            }

            return { b, o };
        }

        let child = node;
        while (child.parentNode !== element) {
            child = child.parentNode;
        }

        const index = Array.prototype.indexOf.call(element.children, child);
        let o = 0;
        for (let earlier = 0; earlier < index && earlier < block.items.length; earlier++) {
            o += lengthOf(block.items[earlier]);
        }

        const item = block.items[index];
        if (!item) {
            // The filler at the paragraph's end.
            return { b, o };
        }

        if (item.atom) {
            return { b, o: o + (offset > 0 ? 1 : 0) };
        }

        if (node.nodeType === Node.TEXT_NODE) {
            return { b, o: o + Math.min(offset, item.t.length) };
        }

        return { b, o: o + (offset > 0 ? item.t.length : 0) };
    }

    selection() {
        const selection = getSelection();
        if (!selection || selection.rangeCount === 0) {
            return null;
        }

        const anchor = this.toPlace(selection.anchorNode, selection.anchorOffset);
        const focus = this.toPlace(selection.focusNode, selection.focusOffset);
        if (!anchor || !focus) {
            return null;
        }

        return { anchor, focus };
    }

    range(selected = this.selection()) {
        if (!selected) {
            return null;
        }

        return before(selected.focus, selected.anchor)
            ? { from: selected.focus, to: selected.anchor }
            : { from: selected.anchor, to: selected.focus };
    }

    select(anchor, focus = anchor) {
        const selection = getSelection();
        if (!selection || this.closed) {
            return;
        }

        const [anchorNode, anchorOffset] = this.toDom(this.clamp(anchor));
        const [focusNode, focusOffset] = this.toDom(this.clamp(focus));
        selection.setBaseAndExtent(anchorNode, anchorOffset, focusNode, focusOffset);
        this.notify();
    }

    clamp(place) {
        const b = Math.min(Math.max(place.b, 0), this.blocks.length - 1);
        return { b, o: Math.min(Math.max(place.o, 0), blockLength(this.blocks[b])) };
    }

    focus() {
        this.box.focus({ preventScroll: true });
        this.select({ b: 0, o: this.session.from }, { b: 0, o: this.session.to });
        if (this.session.skip <= 0) {
            this.box.scrollIntoView({ block: 'nearest', inline: 'nearest' });
        }
    }

    // The model

    // What text typed at a place is like: the text before it, or failing that the text after it,
    // or failing that the paragraph's own mark.
    faceAt(place) {
        const block = this.blocks[place.b];
        let at = 0;
        let after = null;
        for (const item of block.items) {
            const length = lengthOf(item);
            if (!item.atom) {
                if (place.o > at && place.o <= at + length) {
                    return { u: item.u, f: item.f };
                }

                if (place.o === at && !after) {
                    after = { u: item.u, f: item.f };
                }
            }

            at += length;
        }

        if (after) {
            return after;
        }

        const near = block.items.findLast(_ => !_.atom);
        return near ? { u: near.u, f: near.f } : { u: -1, f: this.session.face };
    }

    // Divides the item a place falls inside, so that the place is between two items, and says
    // which item it is now before.
    divide(place) {
        const block = this.blocks[place.b];
        let at = 0;
        for (let index = 0; index < block.items.length; index++) {
            const item = block.items[index];
            const length = lengthOf(item);
            if (place.o === at) {
                return index;
            }

            if (place.o < at + length) {
                block.items.splice(index, 1, { ...item, t: item.t.slice(0, place.o - at) }, { ...item, t: item.t.slice(place.o - at) });
                return index + 1;
            }

            at += length;
        }

        return block.items.length;
    }

    tidy() {
        for (const block of this.blocks) {
            const items = [];
            for (const item of block.items) {
                const last = items[items.length - 1];
                if (item.atom) {
                    items.push(item);
                } else if (item.t.length === 0) {
                    continue;
                } else if (last && !last.atom && last.u === item.u && last.f === item.f) {
                    last.t += item.t;
                } else {
                    items.push(item);
                }
            }

            block.items = items;
        }
    }

    // Whether an item that is not text can be deleted: a picture, a page break, a field or a note's
    // reference can, and goes from the file when the paragraph is handed in. Text a tracked change
    // deleted is settled in the review pane, and text the page never drew is not the reader's to see.
    removable(item) {
        const unit = this.session.u[item.u];
        return item.atom === true && unit?.k === 1 && unit.s !== 'deleted';
    }

    // The item that takes up the place after o in a block, or null.
    itemAt(b, o) {
        let at = 0;
        for (const item of this.blocks[b].items) {
            const length = lengthOf(item);
            if (o >= at && o < at + length) {
                return item;
            }

            at += length;
        }

        return null;
    }

    // Takes out what is between two places: the text, and what is not text that can go.
    remove(from, to) {
        if (same(from, to)) {
            return;
        }

        const start = this.divide(from);
        const first = this.blocks[from.b];
        if (from.b === to.b) {
            const end = this.divide(to);
            first.items.splice(start, end - start, ...first.items.slice(start, end).filter(_ => _.atom && !this.removable(_)));
            return;
        }

        const stays = _ => _.atom && !this.removable(_);
        const kept = first.items.slice(start).filter(stays);
        for (let index = from.b + 1; index < to.b; index++) {
            kept.push(...this.blocks[index].items.filter(stays));
        }

        const last = this.blocks[to.b];
        const end = this.divide(to);
        kept.push(...last.items.slice(0, end).filter(stays), ...last.items.slice(end));
        first.items.splice(start, first.items.length - start, ...kept);
        this.blocks.splice(from.b + 1, to.b - from.b);
    }

    insert(place, text, like) {
        if (text.length === 0) {
            return place;
        }

        const index = this.divide(place);
        this.blocks[place.b].items.splice(index, 0, { u: like.u, t: text, f: like.f });
        return { b: place.b, o: place.o + text.length };
    }

    split(place) {
        const index = this.divide(place);
        const block = this.blocks[place.b];
        const rest = block.items.splice(index, block.items.length - index);
        this.blocks.splice(place.b + 1, 0, { align: block.align, items: rest });
        return { b: place.b + 1, o: 0 };
    }

    // Word's keys the browser does differently, or not at all. Says whether the key was one of them.
    onWordKey(event) {
        const ctrl = commandKey(event);
        const mac = onMac();
        const selected = this.range();
        if (!selected) {
            return false;
        }

        // An arrow key ends a burst of typing, so what is typed after it is an undo entry of its own
        // (WordProbe U-01).
        // A look waiting for what is typed next is dropped by it too (WordProbe E-09b).
        if (event.key.startsWith('Arrow') || event.key === 'Home' || event.key === 'End') {
            this.lastKind = '';
            this.pendingFace = null;
        }

        // Between a table's cells (WordProbe T-01, T-09): Tab and Shift+Tab, Alt+Home, Alt+End,
        // Alt+PgUp and Alt+PgDn.
        if (this.session.cell && !ctrl) {
            const moves = { Home: 'row-start', End: 'row-end', PageUp: 'column-top', PageDown: 'column-bottom' };
            if (event.key === 'Tab' && !event.altKey) {
                this.callbacks.key(event.shiftKey ? 'previous-cell' : 'next-cell');
                return true;
            }

            if (event.altKey && !event.shiftKey && moves[event.key]) {
                this.callbacks.key(moves[event.key]);
                return true;
            }
        }

        // Ctrl+Home and Ctrl+End: the story's ends (WordProbe N-11); on a Mac Command+Up and Down too.
        const ends = mac
            ? { Home: 'story-start', End: 'story-end', ArrowUp: 'story-start', ArrowDown: 'story-end' }
            : { Home: 'story-start', End: 'story-end' };
        if (ctrl && !event.shiftKey && !event.altKey && ends[event.key]) {
            this.callbacks.key(ends[event.key]);
            return true;
        }

        // F4 repeats the last action, as Ctrl+Y does with nothing to redo (WordProbe U-06).
        if (event.key === 'F4' && !ctrl && !event.shiftKey && !event.altKey) {
            this.repeat();
            return true;
        }

        // Ctrl+Shift+V is Paste Text Only (WordProbe C-24): the clipboard's text, its look the caret's.
        if (ctrl && event.shiftKey && !event.altKey && event.code === 'KeyV') {
            this.pastePlain();
            return true;
        }

        // Ctrl+Left and Ctrl+Right step by Word's word stops, with Shift extending (WordProbe N-02, S-05);
        // Ctrl+Shift+Up and Down select to the paragraph's start and end (S-06). On a Mac it is Option
        // that steps by words, and Command+Left and Right are the browser's: the line's start and end;
        // Command+Shift+Up and Down select to the paragraph's ends, as Word's for the Mac do.
        const byWords = mac ? event.altKey && !event.metaKey && !event.ctrlKey : ctrl && !event.altKey;
        if (byWords && (event.key === 'ArrowLeft' || event.key === 'ArrowRight')) {
            const selection = getSelection();
            const focus = this.toPlace(selection.focusNode, selection.focusOffset);
            const anchor = this.toPlace(selection.anchorNode, selection.anchorOffset);
            if (!focus || !anchor) {
                return false;
            }

            const backward = event.key === 'ArrowLeft';
            const step = this.wordRange(focus, backward);
            if (!step) {
                // No stop left in this paragraph: on into the next one's start, or back to the end of the
                // one before, as Word's Ctrl+Right stops before the mark and then at the next paragraph
                // (WordProbe N-12, R-N-10) — across what Enter split here too, and no further than the story.
                const b = focus.b + (backward ? -1 : 1);
                if (this.blocks[b]) {
                    const to = { b, o: backward ? blockLength(this.blocks[b]) : 0 };
                    this.select(event.shiftKey ? anchor : to, to);
                } else if (!event.shiftKey && !(backward ? this.session.first : this.session.last)) {
                    this.commit(backward ? 3 : 4);
                }

                return true;
            }

            const to = backward ? step.from : step.to;
            if (event.shiftKey) {
                this.select(anchor, to);
            } else {
                this.select(to);
            }

            return true;
        }

        if (ctrl && event.shiftKey && !event.altKey && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
            const selection = getSelection();
            const anchor = this.toPlace(selection.anchorNode, selection.anchorOffset);
            if (!anchor) {
                return false;
            }

            const b = selected.from.b;
            this.select(anchor, { b, o: event.key === 'ArrowUp' ? 0 : blockLength(this.blocks[b]) });
            return true;
        }

        // A picture selected in the line (WordProbe I-03, X-03e): Esc leaves the caret before it, Tab does
        // nothing, Enter breaks the line before it, and what is typed goes in before it.
        const picture = this.pictureAt(selected);
        if (picture && !ctrl && !event.altKey) {
            if (event.key === 'Escape') {
                event.stopPropagation();
                this.select(picture);
                return true;
            }

            if (event.key === 'Tab') {
                return true;
            }

            if (event.key === 'Enter' && !event.shiftKey) {
                this.paragraphBreak({ from: picture, to: picture });
                return true;
            }

            if (event.key === 'Enter') {
                this.lineBreak({ from: picture, to: picture });
                return true;
            }
        }

        return false;
    }

    // The place of a picture in the line that is what is selected, or null.
    pictureAt(range) {
        if (range.from.b !== range.to.b || range.to.o !== range.from.o + 1) {
            return null;
        }

        const item = this.itemAt(range.from.b, range.from.o);
        if (!item?.atom || this.session.u[item.u]?.s !== 'drawing') {
            return null;
        }

        return range.from;
    }

    // Word's Paste Text Only: the clipboard's plain text, typed in with the caret's look.
    async pastePlain() {
        const range = this.range();
        let text = '';
        try {
            text = await navigator.clipboard.readText();
        } catch {
            this.callbacks.note?.('The browser did not let the page read the clipboard.');
            return;
        }

        if (this.pending || this.closed || !range) {
            return;
        }

        this.replace(range, this.spaced(range, text), 'paste');
    }

    // Word's smart cut and paste (WordProbe C-01): a word pasted against another gets a space between
    // them, so it stands apart. Only a paste of one line is spaced.
    spaced(range, text) {
        if (text.length === 0 || /[\r\n]/.test(text)) {
            return text;
        }

        const block = this.blockText(range.from.b);
        const before = block[range.from.o - 1] ?? '';
        const after = range.from.b === range.to.b ? block[range.to.o] ?? '' : '';
        const word = /[\p{L}\p{N}]/u;
        let spaced = text;
        if (word.test(before) && word.test(text[0])) {
            spaced = ' ' + spaced;
        }

        if (word.test(after) && word.test(text[text.length - 1])) {
            spaced += ' ';
        }

        return spaced;
    }

    // Word's extend mode (WordProbe S-10). The first F8 turns it on where the caret is, without
    // selecting; each F8 after takes in more — the word with its trailing space, the sentence, the
    // paragraph, the document; in a cell or a list item the sentence is not a step. Shift+F8 steps
    // back; an arrow extends from the anchor and a typed character extends to that character's next
    // occurrence; Esc leaves the mode with the selection kept, and Delete, Backspace or a click leave it
    // as they do what they do. Says whether the key was the mode's.
    onExtendKey(event) {
        const plain = !event.ctrlKey && !event.metaKey && !event.altKey;
        if (event.key === 'F8' && plain) {
            const selected = this.range();
            if (!selected) {
                return true;
            }

            if (!this.extend) {
                this.extendAnchor = selected.from;
                this.setExtend(1);
                return true;
            }

            this.extend = Math.max(1, this.extend + (event.shiftKey ? -1 : 1));
            const steps = this.session.cell || this.session.list !== undefined && this.session.list !== null
                ? ['word', 'paragraph', 'document']
                : ['word', 'sentence', 'paragraph', 'document'];
            this.extend = Math.min(this.extend, steps.length + 1);
            if (this.extend === 1) {
                this.select(this.extendAnchor);
                return true;
            }

            const step = steps[this.extend - 2];
            if (step === 'document') {
                this.setExtend(0);
                this.callbacks.selectAll?.();
                return true;
            }

            const [from, to] = this.unitAround(this.extendAnchor, step);
            this.select(from, to);
            return true;
        }

        if (!this.extend) {
            return false;
        }

        if (event.key === 'Escape') {
            event.stopPropagation();
            this.setExtend(0);
            return true;
        }

        // What is selected is deleted as it would be without the mode, and the mode is over: the
        // letters typed after are text again.
        if (event.key === 'Delete' || event.key === 'Backspace') {
            this.setExtend(0);
            return false;
        }

        const moves = {
            ArrowLeft: ['backward', 'character'],
            ArrowRight: ['forward', 'character'],
            ArrowUp: ['backward', 'line'],
            ArrowDown: ['forward', 'line'],
            Home: ['backward', 'lineboundary'],
            End: ['forward', 'lineboundary']
        };
        if (moves[event.key] && plain && !event.shiftKey) {
            getSelection()?.modify('extend', ...moves[event.key]);
            return true;
        }

        if (event.key.length === 1 && plain) {
            this.extendTo(event.key);
            return true;
        }

        return false;
    }

    // In extend mode, a typed character extends the selection to its next occurrence.
    extendTo(character) {
        const selection = getSelection();
        const focus = this.toPlace(selection.focusNode, selection.focusOffset);
        if (!focus || !character) {
            return;
        }

        const found = this.blockText(focus.b).indexOf(character, focus.o);
        if (found >= 0) {
            this.select(this.extendAnchor, { b: focus.b, o: found + 1 });
        }
    }

    // Extend mode on (a step, 1 and up) or off (0). The status bar shows it while it is on, as Word's
    // shows "Extend Selection": the state handed to the page says so (data-edit-show="extend").
    setExtend(value) {
        const was = Boolean(this.extend);
        this.extend = value;
        if (was === Boolean(value)) {
            return;
        }

        if (!this.closed && this.range()) {
            this.notify();
        } else {
            this.callbacks.state({ extend: Boolean(value) });
        }
    }

    // A block's text, with each thing that is not text as one object character, so its places are
    // the block's own.
    blockText(b) {
        return this.blocks[b].items.map(_ => _.atom ? '\uFFFC' : _.t).join('');
    }

    // The word (with its trailing spaces), the sentence (to its stop and the spaces after) or the
    // paragraph around a place, as two places in its block.
    unitAround(place, step) {
        const text = this.blockText(place.b);
        if (step === 'paragraph') {
            return [{ b: place.b, o: 0 }, { b: place.b, o: text.length }];
        }

        let start = place.o;
        let end = place.o;
        if (step === 'word') {
            const wordy = /[\p{L}\p{N}_'\u2019]/u;
            const alike = character => wordy.test(character) === wordy.test(text[place.o] ?? ' ');
            while (start > 0 && text[start - 1] !== ' ' && alike(text[start - 1])) {
                start--;
            }

            while (end < text.length && text[end] !== ' ' && alike(text[end])) {
                end++;
            }
        } else {
            while (start > 0 && !/[.!?]\s/.test(text.slice(start - 2, start))) {
                start--;
            }

            while (end < text.length && !/[.!?]/.test(text[end])) {
                end++;
            }

            end = Math.min(text.length, end + 1);
        }

        while (end < text.length && text[end] === ' ') {
            end++;
        }

        return [{ b: place.b, o: start }, { b: place.b, o: end }];
    }

    // What typing replaces: in Overtype, a caret takes in a place for each character typed — a
    // character, a tab, a picture or a whole field alike — stopping at the paragraph's end (a cell's
    // too, since the box holds one paragraph). Under Track Changes Overtype overwrites nothing and the
    // text is inserted; a selection is replaced either way (WordProbe E-11 to E-11e, E-105). A character
    // beyond the Basic Multilingual Plane is one character, typed or typed over. What takes a place but
    // is not seen — a floating picture's anchor — is passed over rather than typed over: what is typed
    // goes in after it, and it stops the overtyping of a longer stretch.
    overtyped(range, text) {
        if (!this.overtype || this.session.tracked || !same(range.from, range.to)) {
            return range;
        }

        const block = this.blockText(range.from.b);
        const from = this.pastInvisible(range.from, false);
        let to = from;
        for (let count = [...text].length; count > 0 && to.o < block.length; count--) {
            const item = this.itemAt(to.b, to.o);
            if (item?.atom && (!this.removable(item) || this.invisible(item))) {
                break;
            }

            to = { b: to.b, o: stepOn(block, to.o) };
        }

        return { from, to };
    }

    // Whether an item takes a place in the text but is not seen in the box: a picture anchored here
    // and drawn elsewhere, or what the page never drew (renderAtom gives both no room).
    invisible(item) {
        if (!item?.atom) {
            return false;
        }

        const unit = this.session.u[item.u];
        return unit?.k === 2 || (unit?.s === 'drawing' && !(unit.w > 0 && unit.e > 0));
    }

    // A place moved past whatever unseen items are beside it, going back or forward: what a key that
    // acts beside the caret acts on is what the reader can see.
    pastInvisible(place, backward) {
        const length = blockLength(this.blocks[place.b]);
        let o = place.o;
        while (backward
            ? o > 0 && this.invisible(this.itemAt(place.b, o - 1))
            : o < length && this.invisible(this.itemAt(place.b, o))) {
            o += backward ? -1 : 1;
        }

        return { b: place.b, o };
    }

    // Replaces what is selected with text; a line's end in the text ends the paragraph.
    replace(range, text, kind) {
        const typing = this.record(kind);
        this.lastAction = null;
        if (kind === 'type') {
            this.typed = typing ? (this.typed ?? '') + text : text;
        }

        const like = this.faceAt(same(range.from, range.to) ? range.from : { b: range.from.b, o: range.from.o + 1 });
        if (this.pendingFace !== null) {
            like.f = this.pendingFace;
        }

        this.remove(range.from, range.to);
        let place = range.from;
        const lines = text.replace(/\r\n?/g, '\n').split('\n');
        lines.forEach((line, index) => {
            if (index > 0) {
                place = this.split(place);
            }

            place = this.insert(place, line, like);
        });

        this.pendingFace = null;
        this.tidy();
        this.render();
        this.select(place);
    }

    // HTML on the clipboard is read by .NET (PastedHtml, DocumentEditor.Paste), which keeps its
    // headings, lists, links and emphasis: what is selected goes here first, the caret is put where
    // the paste goes, and the paragraph is handed in for the file to take the paste. Plain text — the
    // pages' own copy, Ctrl+Shift+V — is typed in here like anything else.
    pasteHtml(range, data) {
        const html = data?.getData('text/html');
        if (!html || !this.callbacks.paste) {
            return false;
        }

        if (same(range.from, range.to)) {
            this.select(range.from);
        } else {
            this.replace(range, '', 'delete');
        }

        this.callbacks.paste(html, data.getData('text/plain') ?? '');
        return true;
    }

    // The ribbon's Paste: what Ctrl+V would have pasted, read from the clipboard (the browser may ask
    // the reader first, and a refusal pastes nothing). HTML goes to .NET as a pasted one does.
    async pasteFromClipboard() {
        const range = this.range();
        if (!range || !navigator.clipboard) {
            return;
        }

        let html = '';
        let text = '';
        try {
            if (navigator.clipboard.read) {
                for (const item of await navigator.clipboard.read()) {
                    if (item.types.includes('text/html')) {
                        html = await (await item.getType('text/html')).text();
                    }

                    if (item.types.includes('text/plain')) {
                        text = await (await item.getType('text/plain')).text();
                    }
                }
            } else {
                text = await navigator.clipboard.readText();
            }
        } catch {
            this.callbacks.note?.('The browser did not let the page read the clipboard: paste with Ctrl+V.');
            return;
        }

        if (this.pending || this.closed) {
            return;
        }

        const data = { getData: type => ({ 'text/html': html, 'text/plain': text })[type] ?? '' };
        if (!this.pasteHtml(range, data)) {
            this.replace(range, text, 'paste');
        }

        this.box.focus({ preventScroll: true });
    }

    lineBreak(range) {
        this.record('break');
        const like = this.faceAt(range.from);
        this.remove(range.from, range.to);
        const place = this.insert(range.from, '\n', like);
        this.tidy();
        this.render();
        this.select(place);
    }

    paragraphBreak(range) {
        this.record('break');
        this.remove(range.from, range.to);
        const place = this.split(range.from);
        this.tidy();
        this.render();
        this.select(place);
    }

    // As a word processor's buttons do: on, unless all of what is selected already is.
    toggle(bit) {
        const selected = this.range();
        if (!selected) {
            return;
        }

        // A picture selected: what is typed next, before it, takes the look; the picture keeps its own
        // (WordProbe I-03).
        const picture = this.pictureAt(selected);
        if (picture) {
            this.select(picture);
            this.pendingFace = (this.pendingFace ?? this.faceAt(picture).f) ^ bit;
            this.notify();
            return;
        }

        let range = selected;
        if (same(selected.from, selected.to)) {
            // In or beside a word, the word with its trailing space, the caret staying where it is;
            // between spaces, what is typed next (WordProbe E-09, E-09b).
            const text = this.blockText(selected.from.b);
            const letter = /[\p{L}\p{N}'\u2019]/u;
            let at = null;
            if (letter.test(text[selected.from.o] ?? '')) {
                at = selected.from;
            } else if (selected.from.o > 0 && letter.test(text[selected.from.o - 1])) {
                at = { b: selected.from.b, o: selected.from.o - 1 };
            }

            if (!at) {
                const face = this.pendingFace ?? this.faceAt(selected.from).f;
                this.pendingFace = face ^ bit;
                this.notify();
                return;
            }

            const [from, to] = this.unitAround(at, 'word');
            range = { from, to };
        }

        this.record('format');
        this.lastAction = { toggle: bit };
        const touched = this.textBetween(range.from, range.to);
        const on = !touched.every(_ => _.f & bit);
        for (const item of touched) {
            item.f = on ? item.f | bit : item.f & ~bit;
        }

        this.tidy();
        this.render();
        this.select(selected.from, selected.to);
    }

    // The text items between two places, divided at both so that they hold nothing else.
    textBetween(from, to) {
        const found = [];
        for (let b = from.b; b <= to.b; b++) {
            // The start first: dividing there moves what follows it along by one.
            const block = this.blocks[b];
            const start = b === from.b ? this.divide(from) : 0;
            const end = b === to.b ? this.divide(to) : block.items.length;
            found.push(...block.items.slice(start, end).filter(_ => !_.atom));
        }

        return found;
    }

    align(value) {
        const selected = this.range();
        if (!selected) {
            return;
        }

        this.record('format');
        for (let b = selected.from.b; b <= selected.to.b; b++) {
            this.blocks[b].align = value;
        }

        this.render();
        this.select(selected.from, selected.to);
    }

    // Word's Ctrl+Y with nothing to redo repeats the last action (WordProbe U-05a, U-06a): here, the
    // last burst of typing, at the caret, over what is selected.
    repeat() {
        const range = this.range();
        if (!range) {
            return;
        }

        if (this.lastAction?.toggle) {
            this.toggle(this.lastAction.toggle);
            return;
        }

        if (this.lastAction && 'delete' in this.lastAction) {
            const backward = this.lastAction.delete;
            if (same(range.from, range.to)) {
                const text = this.blockText(range.from.b);
                const place = this.pastInvisible(range.from, backward);
                if (backward ? place.o > 0 : place.o < text.length) {
                    this.deleteCharacter(place, backward);
                }
            } else {
                this.replace(range, '', 'delete');
            }

            this.lastAction = { delete: backward };
            return;
        }

        if (!this.typed) {
            return;
        }

        const typed = this.typed;
        this.replace(range, typed, 'repeat');
        this.typed = typed;
    }

    // A character typed: a straight quote is typed as Word's smart quote, and a delimiter that finishes
    // a word runs Word's AutoCorrect over it (morph-autocorrect.js).
    typeText(range, text) {
        const picture = this.pictureAt(range);
        if (picture) {
            range = { from: picture, to: picture };
        }

        let typed = text;
        if (text === '"' || text === "'") {
            typed = smartQuote(this.blockText(range.from.b).slice(0, range.from.o), text);
        }

        this.replace(this.overtyped(range, typed), typed, 'type');
        if (typed.length === 1 && /[\s.,?!;:]/.test(typed)) {
            const caret = this.range();
            if (caret && same(caret.from, caret.to)) {
                this.autoCorrect({ b: caret.from.b, o: caret.from.o - 1 }, typed);
            }
        }
    }

    // Word's corrections of the word that ends at `end`, finished by `trigger`, each an undo entry of its
    // own — so the first Ctrl+Z after one takes the correction back and keeps what was typed (A-20).
    // Only the paragraph's first block starts a list; a correction that would cross something that is
    // not text is left alone.
    autoCorrect(end, trigger) {
        const text = this.blockText(end.b);
        let caret = this.range()?.from ?? end;
        for (const correction of corrections(text, end.o, trigger)) {
            if (correction.list && end.b !== 0) {
                continue;
            }

            const from = { b: end.b, o: correction.from };
            const to = { b: end.b, o: correction.to };
            if (this.blockText(end.b).slice(correction.from, correction.to).includes('\uFFFC')) {
                continue;
            }

            this.record('autocorrect');
            const like = this.faceAt({ b: end.b, o: correction.from + 1 });
            this.remove(from, to);
            const written = this.insert(from, correction.text, like);
            if (correction.face) {
                for (const item of this.textBetween(from, written)) {
                    item.f |= faces[correction.face];
                }
            }

            const shift = correction.text.length - (correction.to - correction.from);
            if (caret.b === end.b && caret.o >= correction.to) {
                caret = { b: caret.b, o: caret.o + shift };
            }

            this.tidy();
            this.render();
            this.select(caret);
            if (correction.list) {
                this.callbacks.key(correction.list);
            }
        }
    }

    // Word's alignment keys toggle against Left: Ctrl+E, Ctrl+R and Ctrl+J pressed on a paragraph
    // already so aligned put it back to the left, and Ctrl+L on a left-aligned one justifies it
    // (WordProbe E-89, E-89b).
    alignByKey(name) {
        const selected = this.range();
        if (!selected) {
            return;
        }

        const now = this.blocks[selected.from.b].align ?? this.session.align;
        let value = alignments.indexOf(name.slice(6));
        if (value === now) {
            value = value === 0 ? alignments.indexOf('justify') : 0;
        }

        this.align(value);
    }

    // Word's Shift+F3 (WordProbe E-80, E-80b): the selection, or the word the caret is in, goes to
    // lower case when it is all capitals, to Title Case when it is all lower case, and to capitals
    // otherwise. At a word's end, with no word to take, nothing changes.
    changeCase() {
        let range = this.range();
        if (!range) {
            return;
        }

        if (same(range.from, range.to)) {
            const text = this.blockText(range.from.b);
            const letter = /[\p{L}\p{N}'\u2019]/u;
            if (!letter.test(text[range.from.o] ?? '')) {
                return;
            }

            const [from, to] = this.unitAround(range.from, 'word');
            let end = to.o;
            while (end > from.o && !letter.test(text[end - 1])) {
                end--;
            }

            range = { from, to: { b: from.b, o: end } };
        }

        const items = this.textBetween(range.from, range.to);
        const selected = items.map(_ => _.t).join('');
        let mode = 'upper';
        if (selected === selected.toUpperCase() && selected !== selected.toLowerCase()) {
            mode = 'lower';
        } else if (selected === selected.toLowerCase()) {
            mode = 'title';
        }

        this.record('case');
        // Title Case runs across items: a word's first letter may begin a later one.
        let starts = true;
        for (const item of items) {
            if (mode === 'upper') {
                item.t = item.t.toUpperCase();
            } else if (mode === 'lower') {
                item.t = item.t.toLowerCase();
            } else {
                item.t = [...item.t].map(character => {
                    const changed = starts ? character.toUpperCase() : character;
                    starts = /\s/.test(character);
                    return changed;
                }).join('');
            }
        }

        this.tidy();
        this.render();
        this.select(range.from, range.to);
    }

    // Word's Ctrl+Backspace and Ctrl+Delete (WordProbe E-70, E-71): a word with its trailing spaces is
    // one unit, a run of punctuation with its spaces another, a hyphen one of its own, a line break one
    // of its own, and what is not text one. Null at the paragraph's edge, where the keys join paragraphs
    // as Backspace and Delete do.
    wordRange(place, backward) {
        const text = this.blockText(place.b);
        const kind = character => {
            if (character === ' ' || character === '\t' || character === '\u00A0') {
                return 'space';
            }

            if (character === '\uFFFC' || character === '-' || character === '\n') {
                return character;
            }

            if (/[\p{L}\p{N}_'\u2019]/u.test(character)) {
                return 'word';
            }

            return 'mark';
        };

        let o = place.o;
        if (backward) {
            if (o === 0) {
                return null;
            }

            while (o > 0 && kind(text[o - 1]) === 'space') {
                o--;
            }

            if (o > 0) {
                const first = kind(text[o - 1]);
                o--;
                if (first === 'word' || first === 'mark') {
                    while (o > 0 && kind(text[o - 1]) === first) {
                        o--;
                    }
                }
            }

            return { from: { b: place.b, o }, to: place };
        }

        if (o >= text.length) {
            return null;
        }

        const first = kind(text[o]);
        o++;
        if (first === 'word' || first === 'mark' || first === 'space') {
            while (o < text.length && kind(text[o]) === first) {
                o++;
            }
        }

        while (o < text.length && kind(text[o]) === 'space') {
            o++;
        }

        return { from: place, to: { b: place.b, o } };
    }

    // History: the paragraph as it was before each change, typing taken a burst at a time.

    record(kind) {
        const now = performance.now();
        const typing = kind === 'type' && this.lastKind === 'type' && now - this.lastTime < 1000;
        this.lastKind = kind;
        this.lastTime = now;
        // What Ctrl+Y repeats: the last burst of typing, while nothing else has been done since.
        if (kind !== 'type') {
            this.typed = null;
        }

        if (typing) {
            return true;
        }

        this.undoStack.push(this.snapshot());
        if (this.undoStack.length > historyLimit) {
            this.undoStack.shift();
        }

        this.redoStack = [];
    }

    snapshot() {
        return { blocks: copy(this.blocks), selection: this.selection() };
    }

    restore(from, to) {
        const state = from.pop();
        if (!state) {
            return false;
        }

        to.push(this.snapshot());
        this.blocks = state.blocks;
        this.lastKind = '';
        this.pendingFace = null;
        this.render();
        if (state.selection) {
            this.select(state.selection.anchor, state.selection.focus);
        } else {
            this.select({ b: 0, o: 0 });
        }

        return true;
    }

    undo() {
        return this.restore(this.undoStack, this.redoStack);
    }

    redo() {
        return this.restore(this.redoStack, this.undoStack);
    }

    // Reads the model back out of the DOM, after the browser changed it itself.
    adopt() {
        const selected = getSelection();
        const anchor = selected?.anchorNode;
        const blocks = [];
        let caret = null;
        for (const element of this.box.children) {
            const was = this.blocks[Number(element.dataset.b)] ?? this.blocks[this.blocks.length - 1];
            const block = { align: was?.align ?? null, items: [] };
            let length = 0;
            const read = node => {
                if (node.nodeType === Node.TEXT_NODE) {
                    if (node === anchor) {
                        caret = { b: blocks.length, o: length + selected.anchorOffset };
                    }

                    const like = this.likeOf(node, was) ?? block.items.findLast(_ => !_.atom) ?? { u: -1, f: this.session.face };
                    block.items.push({ u: like.u, f: like.f, t: node.data });
                    length += node.data.length;
                    return;
                }

                if (node.nodeType !== Node.ELEMENT_NODE || node.classList.contains('edit-fill')) {
                    return;
                }

                if (node.classList.contains('edit-atom')) {
                    const item = was?.items[Number(node.dataset.i)];
                    if (item?.atom) {
                        block.items.push({ ...item });
                        length += 1;
                    }

                    return;
                }

                if (node.nodeName === 'BR') {
                    const like = block.items.findLast(_ => !_.atom) ?? { u: -1, f: this.session.face };
                    block.items.push({ u: like.u, f: like.f, t: '\n' });
                    length += 1;
                    return;
                }

                node.childNodes.forEach(read);
            };

            element.childNodes.forEach(read);
            blocks.push(block);
        }

        // What is not text has to have come through, all of it and in its order.
        const kept = list => list.flatMap(_ => _.items).filter(_ => _.atom).map(_ => _.u).join(',');
        if (blocks.length > 0 && kept(blocks) === kept(this.blocks)) {
            this.blocks = blocks;
            this.tidy();
        }

        this.render();
        this.select(caret ?? { b: this.blocks.length - 1, o: blockLength(this.blocks[this.blocks.length - 1]) });
    }

    likeOf(node, block) {
        const span = node.parentElement?.closest('.edit-run');
        const item = span && this.box.contains(span) ? block?.items[Number(span.dataset.i)] : null;
        return item && !item.atom ? item : null;
    }

    // Events

    listen() {
        const signal = this.abort.signal;
        const box = this.box;
        box.addEventListener('beforeinput', event => this.onBeforeInput(event), { signal });
        for (const type of ['keydown', 'pointerdown', 'compositionupdate']) {
            box.addEventListener(type, () => {
                this.lastInput = performance.now();
            }, { signal, capture: true });
        }
        box.addEventListener('input', () => this.onInput(), { signal });
        box.addEventListener('compositionstart', () => {
            this.record('compose');
            this.composing = true;
            this.composedFrom = this.range()?.from ?? null;
            this.composedFace = this.pendingFace;
        }, { signal });
        box.addEventListener('compositionend', () => this.onComposed(), { signal });
        box.addEventListener('keydown', event => this.onKeyDown(event), { signal });
        box.addEventListener('copy', event => this.onCopy(event, false), { signal });
        box.addEventListener('cut', event => this.onCopy(event, true), { signal });
        box.addEventListener('dragstart', event => this.onDragStart(event), { signal });
        box.addEventListener('dragend', event => this.onDragEnd(event), { signal });
        box.addEventListener('mousedown', event => this.onPictureDown(event), { signal });
        box.addEventListener('mousedown', event => {
            // A click ends extend mode, as Esc does.
            this.setExtend(0);
            this.lastKind = '';
            this.pendingFace = null;
            this.wordDrag = null;
            if (event.button === 0 && event.detail === 2 && !event.shiftKey && !event.altKey && !event.ctrlKey &&
                !event.metaKey && !(event.target instanceof Element && event.target.closest('.edit-drawing, .edit-atom'))) {
                this.startWordDrag(event);
            }
        }, { signal });
        document.addEventListener('mousemove', event => {
            if (this.wordDrag && (event.buttons & 1)) {
                this.extendWordDrag(event);
            }
        }, { signal });
        box.addEventListener('click', event => this.onClickCount(event), { signal });
        box.addEventListener('focusout', () => this.onFocusOut(), { signal });
        document.addEventListener('selectionchange', () => {
            if (!this.closed && !this.pending && this.box.contains(getSelection()?.anchorNode ?? null)) {
                this.notify();
            }
        }, { signal });
    }

    // Word's selections by the mouse (WordProbe M-10, M-20, M-24): a double-click takes the word with
    // every space after it, a triple-click the paragraph, a Ctrl+click the sentence with its spaces.
    onClickCount(event) {
        if (this.pending || this.closed || event.button !== 0 ||
            (event.target instanceof Element && event.target.closest('.edit-drawing, .edit-atom'))) {
            return;
        }

        const caret = this.range();
        if (!caret) {
            return;
        }

        let unit = null;
        if (event.detail === 2) {
            // The press took the word already, and a drag since may have taken more (startWordDrag).
            if (this.wordDrag) {
                this.wordDrag = null;
                return;
            }

            unit = 'word';
        } else if (event.detail === 3) {
            unit = 'paragraph';
        } else if (event.detail === 1 && commandKey(event) && same(caret.from, caret.to)) {
            unit = 'sentence';
        }

        if (!unit) {
            return;
        }

        // The paragraph of a triple-click is the one under the pointer: the browser's own selection by
        // then may run on to the start of the next block Enter made.
        const at = event.detail === 3 ? this.characterAt(event.clientX, event.clientY) ?? caret.from : caret.from;
        const [from, to] = this.unitAround(at, unit);
        this.select(from, to);
    }

    // A plain drag is by character: Morph follows Word with automatic word selection off (WordProbe M-34,
    // M-34b). A double-click held and dragged is by whole words, whatever that option (M-34c, M-34d): the
    // press takes the word the double-click is on, and as the drag goes the selection runs from that
    // word's far side to the near side of the word under the pointer — its end with the spaces after it
    // going forwards, its start going backwards. Out of the paragraph the selection stays as it last was.
    startWordDrag(event) {
        const at = this.characterAt(event.clientX, event.clientY);
        if (this.pending || this.closed || !at) {
            return;
        }

        event.preventDefault();
        const [from, to] = this.unitAround(at, 'word');
        this.wordDrag = { from, to };
        this.select(from, to);
    }

    extendWordDrag(event) {
        const at = this.characterAt(event.clientX, event.clientY);
        const { from, to } = this.wordDrag;
        if (this.pending || this.closed || !at || at.b !== from.b) {
            return;
        }

        const [wordFrom, wordTo] = this.unitAround(at, 'word');
        if (before(at, from)) {
            this.select(to, wordFrom);
        } else if (before(at, to)) {
            this.select(from, to);
        } else {
            this.select(from, wordTo);
        }
    }

    // The place of the character under a point in the box (a caret after a character the point is still
    // over is moved before it, as Word takes the character a double-click is on), or null off the box.
    characterAt(x, y) {
        let node = null;
        let offset = 0;
        if (document.caretPositionFromPoint) {
            const position = document.caretPositionFromPoint(x, y);
            node = position?.offsetNode ?? null;
            offset = position?.offset ?? 0;
        } else {
            const range = document.caretRangeFromPoint?.(x, y);
            node = range?.startContainer ?? null;
            offset = range?.startOffset ?? 0;
        }

        const place = this.toPlace(node, offset);
        if (!place || place.o === 0) {
            return place;
        }

        const box = this.characterBox({ b: place.b, o: place.o - 1 });
        if (x >= box.left && x <= box.right && y >= box.top && y <= box.bottom) {
            return { b: place.b, o: place.o - 1 };
        }

        return place;
    }

    // The box of the character at place (the one before it at the block's end).
    characterBox(place) {
        const length = blockLength(this.blocks[place.b]);
        const from = place.o < length ? place.o : Math.max(0, place.o - 1);
        const range = document.createRange();
        range.setStart(...this.toDom({ b: place.b, o: from }));
        range.setEnd(...this.toDom({ b: place.b, o: Math.min(from + 1, length) }));
        return range.getClientRects()[0] ?? range.getBoundingClientRect();
    }

    // Pictures

    // A picture in the line, clicked, is selected whole and framed, and its handles size it: a corner
    // keeps its proportions, a side stretches it one way. Let go, the size is the file's to take
    // (DocumentEditor.ResizePicture), in points.
    onPictureDown(event) {
        const span = event.target instanceof Element ? event.target.closest('.edit-drawing') : null;
        if (!span || span.classList.contains('edit-hidden') || !this.box.contains(span) || this.pending) {
            return;
        }

        event.preventDefault();
        const b = Number(span.parentElement?.dataset.b);
        const index = Number(span.dataset.i);
        const block = this.blocks[b];
        if (!block) {
            return;
        }

        let o = 0;
        for (let i = 0; i < index; i++) {
            o += lengthOf(block.items[i]);
        }

        this.box.focus({ preventScroll: true });
        this.select({ b, o }, { b, o: o + 1 });
        this.framePicture(span, { b, o }, this.session.u[block.items[index].u]);
    }

    framePicture(span, place, unit) {
        this.unframe();
        if (!unit || !(unit.w > 0) || !(unit.e > 0)) {
            return;
        }

        const element = document.createElement('div');
        element.className = 'edit-frame';
        const layer = this.layer.getBoundingClientRect();
        const rect = span.getBoundingClientRect();
        const scale = 100 / Math.max(1, layer.width);
        element.style.left = `${((rect.left - layer.left) * scale).toFixed(4)}cqw`;
        element.style.top = `${((rect.top - layer.top) * scale).toFixed(4)}cqw`;
        for (const edge of ['nw', 'n', 'ne', 'e', 'se', 's', 'sw', 'w']) {
            const handle = document.createElement('span');
            handle.className = `edit-handle edit-handle-${edge}`;
            handle.addEventListener('pointerdown', event => this.onHandleDown(event, edge), { signal: this.abort.signal });
            element.append(handle);
        }

        const label = document.createElement('span');
        label.className = 'edit-frame-size';
        const reset = document.createElement('button');
        reset.type = 'button';
        reset.className = 'edit-frame-reset';
        reset.textContent = 'Reset size';
        reset.addEventListener('mousedown', event => event.preventDefault(), { signal: this.abort.signal });
        reset.addEventListener('click', () => {
            this.unframe();
            this.callbacks.key('reset-picture');
        }, { signal: this.abort.signal });
        element.append(label, reset);
        this.layer.append(element);
        this.frame = { element, label, place, w: unit.w, h: unit.e };
        this.sizeFrame(unit.w, unit.e);
    }

    sizeFrame(w, h) {
        const frame = this.frame;
        frame.element.style.width = this.cq(w);
        frame.element.style.height = this.cq(h);
        frame.label.textContent = `${(w / 72 * 2.54).toFixed(1)} × ${(h / 72 * 2.54).toFixed(1)} cm`;
    }

    unframe() {
        this.frame?.element.remove();
        this.frame = null;
    }

    onHandleDown(event, edge) {
        const frame = this.frame;
        if (!frame || this.pending) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        const handle = event.currentTarget;
        handle.setPointerCapture(event.pointerId);
        const perPixel = this.page.width / Math.max(1, this.layer.getBoundingClientRect().width);
        const start = { x: event.clientX, y: event.clientY, w: frame.w, h: frame.h };
        const corner = edge.length === 2;
        const move = moved => {
            const dx = (moved.clientX - start.x) * perPixel * (edge.includes('w') ? -1 : 1);
            const dy = (moved.clientY - start.y) * perPixel * (edge.includes('n') ? -1 : 1);
            let w = start.w;
            let h = start.h;
            if (corner) {
                const grow = Math.abs(dx / start.w) > Math.abs(dy / start.h) ? dx / start.w : dy / start.h;
                w = Math.max(4, start.w * (1 + grow));
                h = w * start.h / start.w;
            } else if (edge === 'e' || edge === 'w') {
                w = Math.max(4, start.w + dx);
            } else {
                h = Math.max(4, start.h + dy);
            }

            frame.w = w;
            frame.h = h;
            this.sizeFrame(w, h);
        };
        const up = () => {
            handle.removeEventListener('pointermove', move);
            handle.removeEventListener('pointerup', up);
            handle.removeEventListener('pointercancel', up);
            if (Math.abs(frame.w - start.w) > 0.5 || Math.abs(frame.h - start.h) > 0.5) {
                this.callbacks.resize(Math.round(frame.w * 100) / 100, Math.round(frame.h * 100) / 100);
            }
        };
        handle.addEventListener('pointermove', move);
        handle.addEventListener('pointerup', up);
        handle.addEventListener('pointercancel', up);
    }

    // The clipboard and dragging

    // What is between two places, as blocks of items — the model left as it is.
    contentBetween(from, to) {
        const blocks = [];
        for (let b = from.b; b <= to.b; b++) {
            const start = b === from.b ? from.o : 0;
            const end = b === to.b ? to.o : Infinity;
            const items = [];
            let at = 0;
            for (const item of this.blocks[b].items) {
                const length = lengthOf(item);
                const first = Math.max(start, at);
                const last = Math.min(end, at + length);
                if (last > first) {
                    items.push(item.atom ? { ...item } : { ...item, t: item.t.slice(first - at, last - at) });
                }

                at += length;
            }

            blocks.push({ align: this.blocks[b].align, items });
        }

        return blocks;
    }

    // What an item that is not text reads as, copied: a field its result, a note's reference its mark,
    // a page break the end of a line. A picture, and text a tracked change deleted, read as nothing.
    atomText(item) {
        const unit = this.session.u[item.u];
        switch (unit?.s) {
            case 'field':
            case 'note':
                return unit.t ?? '';
            case 'break':
                return '\n';
            default:
                return '';
        }
    }

    plainText(blocks) {
        return blocks.map(block => block.items.map(item => item.atom ? this.atomText(item) : item.t).join('')).join('\n');
    }

    // The same as HTML, as Merge Formatting reads it back (PastedHtml): paragraphs when it holds more
    // than one, their alignment, and the emphasis — none of the box's own styling, which is set to the
    // page on screen rather than to the text.
    htmlText(blocks) {
        const escape = text => text.replace(/[&<>"]/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[character]);
        const runs = block => block.items.map(item => {
            const unit = this.session.u[item.u];
            let html = escape(item.atom ? this.atomText(item) : item.t).replace(/\n/g, '<br>');
            const face = item.atom ? unit?.f ?? 0 : item.f;
            for (const [bit, tag] of [[bold, 'b'], [italic, 'i'], [underline, 'u'], [strike, 's']]) {
                if (face & bit) {
                    html = `<${tag}>${html}</${tag}>`;
                }
            }

            if (unit?.v) {
                const tag = unit.v === 1 ? 'sup' : 'sub';
                html = `<${tag}>${html}</${tag}>`;
            }

            return html;
        }).join('');
        if (blocks.length === 1) {
            return runs(blocks[0]);
        }

        return blocks.map(block => {
            const align = alignments[block.align ?? this.session.align] ?? 'left';
            return align === 'left' ? `<p>${runs(block)}</p>` : `<p style="text-align:${align}">${runs(block)}</p>`;
        }).join('');
    }

    // Copy and Cut write the clipboard from the model, as the paragraph reads rather than as the box
    // draws it: a field as its result, a page break as a line's end, never the box's own words for
    // what is not text. Cut then deletes what was copied.
    onCopy(event, cut) {
        const range = this.range();
        if (!range || same(range.from, range.to) || !event.clipboardData) {
            return;
        }

        event.preventDefault();
        const content = this.contentBetween(range.from, range.to);
        event.clipboardData.setData('text/plain', this.plainText(content));
        event.clipboardData.setData('text/html', this.htmlText(content));
        if (cut && !this.pending && !this.closed) {
            this.setExtend(0);
            this.replace(range, '', 'delete');
        }
    }

    // Text dragged from the box carries the model's text, and the drag remembers where it came from:
    // dropped back in the box it is moved there, by the model (moveDragged), not by what the browser
    // would make of its HTML.
    onDragStart(event) {
        const range = this.range();
        if (!range || same(range.from, range.to) || this.pending) {
            this.dragged = null;
            return;
        }

        this.dragged = { from: range.from, to: range.to, moving: false };
        const content = this.contentBetween(range.from, range.to);
        event.dataTransfer?.setData('text/plain', this.plainText(content));
        event.dataTransfer?.setData('text/html', this.htmlText(content));
    }

    // Dropped elsewhere, in something that takes a moved text, the text has gone from here; dropped
    // where nothing takes it, it stays.
    onDragEnd(event) {
        const dragged = this.dragged;
        this.dragged = null;
        if (dragged?.moving && event.dataTransfer?.dropEffect === 'move' && !this.pending && !this.closed) {
            this.replace({ from: dragged.from, to: dragged.to }, '', 'delete');
        }
    }

    // Text dragged within the box to another place in it: moved, as Word moves it, what is not text in
    // it with it — so long as what is not text keeps its order, which is all the file can take
    // (DocumentEditor.Rewrite). A drag with Ctrl held copies the text, without what is not text.
    moveDragged(drop) {
        const { from, to, moving } = this.dragged;
        this.dragged = null;
        if (!moving) {
            const text = this.contentBetween(from, to).map(block => block.items.filter(_ => !_.atom));
            this.record('paste');
            const end = this.insertContent(drop, text);
            this.tidy();
            this.render();
            this.select(drop, end);
            return;
        }

        if (!before(drop, from) && !before(to, drop)) {
            // Dropped on itself: nothing moves.
            this.select(from, to);
            return;
        }

        const blocks = copy(this.blocks);
        const order = list => list.flatMap(_ => _.items).filter(_ => _.atom).map(_ => _.u).join(',');
        this.record('move');
        // What is moved is what taking it out takes out: its text, and what is not text that can go.
        const moved = this.contentBetween(from, to).map(block => block.items.filter(_ => !_.atom || this.removable(_)));
        // A mark where it goes, so that taking it out of where it was does not lose the place.
        const mark = { u: -2, atom: true, mark: true };
        this.blocks[drop.b].items.splice(this.divide(drop), 0, mark);
        const shifted = place => place.b === drop.b && !before(place, drop) ? { b: place.b, o: place.o + 1 } : place;
        this.remove(shifted(from), shifted(to));
        let place = null;
        this.blocks.forEach((block, b) => {
            const index = block.items.indexOf(mark);
            if (index >= 0) {
                let o = 0;
                for (let earlier = 0; earlier < index; earlier++) {
                    o += lengthOf(block.items[earlier]);
                }

                block.items.splice(index, 1);
                place = { b, o };
            }
        });

        const end = this.insertContent(place, moved);
        if (order(this.blocks) !== order(blocks)) {
            this.blocks = blocks;
            this.undoStack.pop();
            this.render();
            this.select(from, to);
            this.callbacks.note?.('A picture, a field or a note can be moved past text, but not past another.');
            return;
        }

        this.tidy();
        this.render();
        this.select(place, end);
    }

    // Puts blocks of items in at a place, the first joining the block there and each after it a
    // block of its own, and says the place after them.
    insertContent(place, blocks) {
        let at = place;
        blocks.forEach((items, index) => {
            if (index > 0) {
                at = this.split(at);
            }

            const position = this.divide(at);
            this.blocks[at.b].items.splice(position, 0, ...items.map(_ => ({ ..._ })));
            let length = 0;
            for (const item of items) {
                length += lengthOf(item);
            }

            at = { b: at.b, o: at.o + length };
        });

        return at;
    }

    targetRange(event) {
        const target = event.getTargetRanges?.()[0];
        if (target) {
            const from = this.toPlace(target.startContainer, target.startOffset);
            const to = this.toPlace(target.endContainer, target.endOffset);
            if (from && to) {
                return before(to, from) ? { from: to, to: from } : { from, to };
            }
        }

        return this.range();
    }

    onBeforeInput(event) {
        if (this.pending || this.closed) {
            event.preventDefault();
            return;
        }

        const type = event.inputType;
        if (this.composing || event.isComposing || type.endsWith('CompositionText') ||
            type === 'insertFromComposition' || type === 'deleteByComposition') {
            // Not the editor's to take: the DOM changes, and is read back when the composition ends.
            this.handled = false;
            return;
        }

        if (!event.cancelable) {
            // A change the browser will make whatever is done here: it is read back (onInput).
            this.handled = false;
            return;
        }

        event.preventDefault();
        this.handled = true;
        if (type === 'deleteByDrag' && this.dragged) {
            // Text dragged out of the box is taken out when it is dropped, here (moveDragged) or
            // elsewhere (onDragEnd), so that the drop's place is still where it was.
            this.dragged.moving = true;
            return;
        }

        const range = this.targetRange(event);
        if (!range) {
            return;
        }

        switch (type) {
            case 'insertText':
            case 'insertReplacementText':
            case 'insertFromYank':
                if (type === 'insertText' && this.extend) {
                    this.extendTo((event.data ?? '').slice(0, 1));
                } else if (type === 'insertText') {
                    const text = event.data ?? '';
                    this.typeText(range, text);
                } else {
                    this.replace(range, event.data ?? event.dataTransfer?.getData('text/plain') ?? '', 'paste');
                }

                break;
            case 'insertFromDrop':
                if (this.dragged) {
                    this.moveDragged(range.from);
                    break;
                }

                if (!this.pasteHtml(range, event.dataTransfer)) {
                    this.replace(range, this.spaced(range, event.dataTransfer?.getData('text/plain') ?? event.data ?? ''), 'paste');
                }

                break;
            case 'insertFromPaste':
            case 'insertFromPasteAsQuotation':
                if (!this.pasteHtml(range, event.dataTransfer)) {
                    this.replace(range, this.spaced(range, event.dataTransfer?.getData('text/plain') ?? event.data ?? ''), 'paste');
                }

                break;
            case 'insertParagraph': {
                const rest = this.finishWord(range);
                if (rest) {
                    this.paragraphBreak(rest);
                }

                break;
            }
            case 'insertLineBreak':
                this.lineBreak(range);
                break;
            case 'formatBold':
                this.toggle(bold);
                break;
            case 'formatItalic':
                this.toggle(italic);
                break;
            case 'formatUnderline':
                this.toggle(underline);
                break;
            case 'formatStrikeThrough':
                this.toggle(strike);
                break;
            case 'historyUndo':
                this.undo();
                break;
            case 'historyRedo':
                this.redo();
                break;
            case 'deleteWordBackward':
            case 'deleteWordForward': {
                const backward = type.endsWith('Backward');
                const caret = this.range();
                if (caret && same(caret.from, caret.to)) {
                    // From beyond an anchor beside the caret, which the reader cannot see to delete.
                    const at = this.pastInvisible(caret.from, backward);
                    let word = this.wordRange(at, backward) ?? { from: at, to: at };
                    const text = this.blockText(at.b);
                    if (backward && word.from.o > 0 && text[word.from.o - 1] === ' ' && /[,.;:!?]/.test(text[at.o] ?? '')) {
                        word = { from: { b: word.from.b, o: word.from.o - 1 }, to: word.to };
                    }

                    this.onDelete(word, backward);
                    this.lastAction = { delete: backward };
                } else {
                    this.onDelete(range, backward);
                }

                break;
            }
            default:
                if (type.startsWith('delete')) {
                    const backward = type.endsWith('Backward');
                    if (!this.overInvisible(backward) && !this.selectAtom(backward)) {
                        this.onDelete(range, backward);
                    }
                }

                break;
        }
    }

    // Enter finishes the word before it, as a space does (WordProbe A-01, A-19): an AutoComplete
    // suggestion is taken instead of a break, or else the word is corrected. Says what the break then
    // takes the place of, or null when the suggestion took the key.
    finishWord(range) {
        if (!same(range.from, range.to)) {
            return range;
        }

        const rest = completion(this.blockText(range.from.b), range.from.o);
        if (rest) {
            this.replace(range, rest, 'autocorrect');
            return null;
        }

        this.autoCorrect(range.from, '\n');
        return this.range() ?? range;
    }

    // Backspace or Delete beside what takes a place but is not seen — a floating picture's anchor —
    // passes over it to what is beyond, which is what the reader sees beside the caret: the anchor is
    // neither selected nor deleted, and stays where it is. Says whether there was one.
    overInvisible(backward) {
        const caret = this.range();
        if (!caret || !same(caret.from, caret.to)) {
            return false;
        }

        const place = this.pastInvisible(caret.from, backward);
        if (same(place, caret.from)) {
            return false;
        }

        this.select(place);
        if (this.selectAtom(backward)) {
            return true;
        }

        const length = blockLength(this.blocks[place.b]);
        if (backward ? place.o > 0 : place.o < length) {
            this.deleteCharacter(place, backward);
        } else if (backward && place.b > 0) {
            this.replace({ from: { b: place.b - 1, o: blockLength(this.blocks[place.b - 1]) }, to: place }, '', 'delete');
        } else if (!backward && place.b < this.blocks.length - 1) {
            this.replace({ from: place, to: { b: place.b + 1, o: 0 } }, '', 'delete');
        } else {
            this.onDelete({ from: place, to: place }, backward);
        }

        return true;
    }

    // Deletes the character beside a place in its block, a surrogate pair whole, or what is not text
    // there.
    deleteCharacter(place, backward) {
        const text = this.blockText(place.b);
        const other = backward ? stepBack(text, place.o) : stepOn(text, place.o);
        const [from, to] = backward ? [other, place.o] : [place.o, other];
        this.replace({ from: { b: place.b, o: from }, to: { b: place.b, o: to } }, '', 'delete');
    }

    // Word selects a field, a picture or a note's reference beside the caret before it deletes it: the
    // first Backspace after it (or Delete before it) selects it, the next removes it (WordProbe E-43,
    // E-44, E-61b). A tab, a break and a space go in one press. Says whether it selected one.
    selectAtom(backward) {
        const caret = this.range();
        if (!caret || !same(caret.from, caret.to)) {
            return false;
        }

        const beside = backward ? caret.from.o - 1 : caret.from.o;
        const item = beside >= 0 ? this.itemAt(caret.from.b, beside) : null;
        if (!item || !this.removable(item) || this.invisible(item) ||
            !['field', 'note', 'drawing'].includes(this.session.u[item.u]?.s)) {
            return false;
        }

        this.select({ b: caret.from.b, o: beside }, { b: caret.from.b, o: beside + 1 });
        return true;
    }

    // Deleting at the very start of the paragraph, or at its very end, is deleting the break
    // between it and its neighbour: the two are joined, which is the file's to do.
    onDelete(range, backward) {
        if (!same(range.from, range.to)) {
            this.replace(range, '', 'delete');
            return;
        }

        // A browser that hands in no range around something that is not text: the place beside the
        // caret is the one to go.
        const beside = backward ? range.from.o - 1 : range.from.o;
        const item = beside >= 0 ? this.itemAt(range.from.b, beside) : null;
        if (item && this.removable(item) && !this.invisible(item)) {
            this.replace({ from: { b: range.from.b, o: beside }, to: { b: range.from.b, o: beside + 1 } }, '', 'delete');
            return;
        }

        const last = this.blocks.length - 1;
        if (backward && range.from.o === 0) {
            // Word strips before it joins: centring (or right alignment) goes first (WordProbe E-41 P4:0).
            const align = this.blocks[range.from.b].align ?? this.session.align;
            if (align === alignments.indexOf('center') || align === alignments.indexOf('right')) {
                this.align(0);
                return;
            }
        }

        if (backward && range.from.b === 0 && range.from.o === 0) {
            if (this.session.previous && this.session.skip <= 0) {
                this.commit(1);
            }

            return;
        }

        if (!backward && range.from.b === last && range.from.o === blockLength(this.blocks[last]) && this.session.next) {
            this.commit(2);
        }
    }

    onInput() {
        if (this.handled || this.composing) {
            this.handled = false;
            return;
        }

        // The browser made a change the editor was not asked about — a spelling correction, a drop it
        // would not let be taken from it. The model is still the paragraph as it was before, and that
        // is what Undo goes back to.
        const was = {
            blocks: copy(this.blocks),
            selection: this.lastRange ? { anchor: this.lastRange.from, focus: this.lastRange.to } : null
        };
        const payload = this.payload();
        this.adopt();
        if (this.payload() !== payload) {
            this.lastKind = '';
            this.typed = null;
            this.undoStack.push(was);
            if (this.undoStack.length > historyLimit) {
                this.undoStack.shift();
            }

            this.redoStack = [];
        }
    }

    // An input method's composition ended: the model is read back out of the DOM, and a look waiting
    // for what is typed next (Ctrl+B between spaces) is what was composed takes.
    onComposed() {
        this.composing = false;
        const from = this.composedFrom;
        const face = this.composedFace;
        this.composedFrom = null;
        this.composedFace = null;
        this.adopt();
        const caret = this.range();
        if (face !== null && face !== undefined && from && caret && same(caret.from, caret.to) &&
            caret.from.b === from.b && caret.from.o > from.o) {
            for (const item of this.textBetween(from, caret.from)) {
                item.f = face;
            }

            this.tidy();
            this.render();
            this.select(caret.from);
        }

        this.pendingFace = null;
    }

    onKeyDown(event) {
        if (this.pending || this.closed || event.isComposing) {
            return;
        }

        if (this.onExtendKey(event)) {
            event.preventDefault();
            return;
        }

        if (this.onWordKey(event)) {
            event.preventDefault();
            return;
        }

        // Word's Ctrl+Alt+1, 2 and 3: Heading 1, 2 and 3. By the key's place, since Alt changes what
        // it types.
        if (commandKey(event) && event.altKey && !event.shiftKey && /^Digit[1-3]$/.test(event.code)) {
            event.preventDefault();
            this.callbacks.key(`heading${event.code.slice(-1)}`);
            return;
        }

        const character = wordCharacter(event);
        if (character) {
            event.preventDefault();
            const range = this.range();
            if (range) {
                this.replace(range, character, 'type');
            }

            return;
        }

        const word = wordKey(event);
        if (word) {
            event.preventDefault();
            if (word.command?.startsWith('align-')) {
                this.alignByKey(word.command);
            } else if (word.command) {
                // One the box cannot carry out itself, such as Track Changes, is the page's: it hands
                // the paragraph in first.
                if (!this.command(word.command)) {
                    this.callbacks.command?.(word.command);
                }
            } else {
                this.callbacks.key(word.key);
            }

            return;
        }

        const command = commandKey(event) && !event.altKey;
        const key = event.key.toLowerCase();
        if (command) {
            switch (key) {
                case 'b':
                case 'i':
                case 'u':
                    event.preventDefault();
                    this.toggle({ b: bold, i: italic, u: underline }[key]);
                    return;
                case 'z':
                    event.preventDefault();
                    if (event.shiftKey) {
                        this.redo();
                    } else {
                        this.undo();
                    }

                    return;
                case 'y':
                    event.preventDefault();
                    if (!this.redo()) {
                        this.repeat();
                    }

                    return;
                case 'enter':
                    // Hands the paragraph in and goes on typing in it once the page is drawn again:
                    // the caret is not lost to the hand-in.
                    event.preventDefault();
                    this.commit(5);
                    return;
                case 'k':
                    event.preventDefault();
                    this.callbacks.key('link');
                    return;
                // The whole document, in a cell too, as Word's Ctrl+A (WordProbe T-12); in a header, a
                // footer or a note, the story's text, which is the paragraph open (H-07, H-09).
                case 'a':
                    if (!event.shiftKey) {
                        event.preventDefault();
                        this.setExtend(0);
                        if (this.session.story) {
                            const last = this.blocks.length - 1;
                            this.select({ b: 0, o: 0 }, { b: last, o: blockLength(this.blocks[last]) });
                        } else {
                            this.callbacks.selectAll?.();
                        }
                    }

                    return;
            }

            return;
        }

        if (!event.altKey && this.onParagraphKey(event)) {
            return;
        }

        switch (event.key) {
            case 'Escape':
                event.preventDefault();
                event.stopPropagation();
                this.cancel();
                return;
            case 'F3':
                if (event.shiftKey && !event.altKey) {
                    event.preventDefault();
                    this.changeCase();
                }

                return;
            // Overtype on or off; Shift+Insert is the browser's paste.
            case 'Insert':
                if (!event.shiftKey && !event.altKey) {
                    event.preventDefault();
                    this.callbacks.overtype?.();
                }

                return;
            case 'Tab':
                event.preventDefault();
                if (this.range() && !event.shiftKey) {
                    const caret = this.range();
                    const before = this.blockText(caret.from.b).slice(0, caret.from.o);
                    if (caret.from.b === 0 && same(caret.from, caret.to) && numberLeader(before)) {
                        this.record('autocorrect');
                        this.remove({ b: 0, o: 0 }, caret.from);
                        this.tidy();
                        this.render();
                        this.select({ b: 0, o: 0 });
                        this.callbacks.key('numbering');
                        return;
                    }

                    this.replace(this.overtyped(caret, '\t'), '\t', 'type');
                    const after = this.range();
                    if (after && same(after.from, after.to)) {
                        this.autoCorrect({ b: after.from.b, o: after.from.o - 1 }, '\t');
                    }
                }

                return;
            case 'ArrowUp':
            case 'ArrowDown':
            case 'ArrowLeft':
            case 'ArrowRight':
                if (!event.shiftKey && !event.altKey) {
                    this.onArrow(event);
                }

                return;
        }
    }

    // Word's keys for a list's paragraph and a heading, which change what the paragraph is rather than
    // what it says — the file's to do, so the paragraph is handed in and .NET carries them out.
    // Word-probed 2026-10-19 by keys sent to Word 365: in a list, Tab anywhere moves the paragraph a
    // level in and Shift+Tab a level out, with no tab typed; Enter in an empty paragraph takes it out
    // of the list; Backspace at a paragraph's start takes its number off, and at an indented one's a
    // step of its indent (DocumentEditor.Unindent). Enter at the end of a
    // heading starts the paragraph after in the style the heading names next, so text typed there
    // is body text.
    onParagraphKey(event) {
        const selected = this.range();
        if (!selected || event.ctrlKey || event.metaKey) {
            return false;
        }

        const caret = same(selected.from, selected.to);
        const listed = this.session.list !== undefined && this.session.list !== null;
        let name = null;
        switch (event.key) {
            case 'Tab':
                if (listed) {
                    name = event.shiftKey ? 'outdent' : 'indent';
                } else if (caret && !event.shiftKey && selected.from.b === 0 && selected.from.o === 0 &&
                    this.session.skip <= 0 && blockLength(this.blocks[0]) > 0 && this.blocks.length === 1 &&
                    !this.session.cell) {
                    // At the start of a paragraph with text, Tab indents its first line rather than
                    // typing a tab, as with Word's TabIndentKey on (WordProbe E-30, A-16).
                    name = 'tab-indent';
                }

                break;
            case 'Enter':
                if (event.shiftKey || !caret) {
                    break;
                }

                if (listed && blockLength(this.blocks[selected.from.b]) === 0) {
                    name = 'end-list';
                } else if (this.session.follow &&
                    selected.from.b === this.blocks.length - 1 &&
                    selected.from.o === blockLength(this.blocks[selected.from.b]) &&
                    selected.from.o > 0) {
                    // The word before it is finished first, as any Enter finishes it, and a suggestion
                    // taken up is all the key does. Then the break is typed here, and the paragraph after
                    // it opened once the file has it.
                    const rest = this.finishWord(selected);
                    if (!rest) {
                        event.preventDefault();
                        return true;
                    }

                    this.paragraphBreak(rest);
                    name = 'follow-on';
                }

                break;
            case 'Backspace':
                if (this.session.indented && caret && !event.shiftKey && selected.from.o === 0 &&
                    (selected.from.b > 0 || this.session.skip <= 0)) {
                    name = 'unindent';
                }

                break;
        }

        if (!name) {
            return false;
        }

        event.preventDefault();
        this.callbacks.key(name);
        return true;
    }

    // An arrow key that would take the caret out of the paragraph takes the reader to the next one.
    onArrow(event) {
        const selected = this.range();
        const selection = getSelection();
        if (!selected || !same(selected.from, selected.to) || !selection?.rangeCount) {
            return;
        }

        const last = this.blocks.length - 1;
        const atStart = selected.from.b === 0 && selected.from.o === 0;
        const atEnd = selected.from.b === last && selected.from.o === blockLength(this.blocks[last]);
        const caret = this.caretBox(selection);
        const bounds = this.box.getBoundingClientRect();
        const line = this.session.line * bounds.width / (this.session.w - Math.min(this.session.indent, 0) + 2 * gutter);
        let direction = 0;
        switch (event.key) {
            case 'ArrowUp':
                direction = caret && caret.top - bounds.top < line * 0.9 && this.box.scrollTop <= 0 ? -1 : 0;
                break;
            case 'ArrowDown':
                direction = caret && bounds.bottom - caret.bottom < line * 0.9 &&
                    this.box.scrollTop + this.box.clientHeight >= this.box.scrollHeight - 1 ? 1 : 0;
                break;
            case 'ArrowLeft':
                direction = atStart ? -1 : 0;
                break;
            default:
                direction = atEnd ? 1 : 0;
                break;
        }

        if (direction === 0) {
            return;
        }

        event.preventDefault();
        // At a story's first or last paragraph the arrows go no further: Left at its start and Up on its
        // first line, Right at its end and Down on its last line, do nothing (WordProbe R-N-1, R-N-2).
        if (direction < 0 ? this.session.first : this.session.last) {
            return;
        }

        const sideways = event.key === 'ArrowLeft' || event.key === 'ArrowRight';
        if (sideways) {
            // One position on, as Word's Left and Right cross a mark: the end of the paragraph before, or
            // the start of the one after (R-N-1), handed in first if it was changed.
            this.commit(direction < 0 ? 3 : 4);
            return;
        }

        const x = caret?.left ?? bounds.left + 1;
        this.callbacks.leave(direction, x, direction < 0 ? bounds.top : bounds.bottom, line);
    }

    caretBox(selection) {
        const range = selection.getRangeAt(0).cloneRange();
        const rects = range.getClientRects();
        if (rects.length > 0) {
            return rects[0];
        }

        // A caret in an empty line has no box of its own; the line's element does.
        const node = range.startContainer.nodeType === Node.ELEMENT_NODE ? range.startContainer : range.startContainer.parentElement;
        return node?.getBoundingClientRect() ?? null;
    }

    // Focus that goes elsewhere on the page hands the paragraph in. A window that loses focus has
    // not gone anywhere: the reader is coming back.
    onFocusOut() {
        setTimeout(() => {
            if (this.closed || this.pending || !document.hasFocus()) {
                return;
            }

            const active = document.activeElement;
            if (this.box.contains(active) || active?.closest?.('[data-edit-command]')) {
                return;
            }

            this.commit(0);
        }, 0);
    }

    // Commands from the toolbar

    command(name) {
        if (this.pending || this.closed) {
            return false;
        }

        if (name in faces) {
            this.toggle(faces[name]);
        } else if (name.startsWith('align-')) {
            this.align(alignments.indexOf(name.slice(6)));
        } else if (name === 'undo') {
            return this.undo();
        } else if (name === 'redo') {
            return this.redo();
        } else if (name === 'copy') {
            // What is selected in the box, as Ctrl+C copies it.
            document.execCommand('copy');
        } else if (name === 'cut') {
            const range = this.range();
            if (range && !same(range.from, range.to)) {
                document.execCommand('copy');
                this.replace(range, '', 'delete');
            }
        } else if (name === 'paste') {
            this.pasteFromClipboard();
        } else if (name === 'change-case') {
            // The ribbon's Change case is Shift+F3's.
            this.changeCase();
        } else {
            return false;
        }

        // A press on the toolbar must not leave the caret behind.
        this.box.focus({ preventScroll: true });
        return true;
    }

    notify() {
        const selected = this.range();
        if (!selected || this.closed) {
            return;
        }

        // The frame is a picture's while the picture is what is selected.
        if (this.frame && !(same(selected.from, this.frame.place) &&
            same(selected.to, { b: this.frame.place.b, o: this.frame.place.o + 1 }))) {
            this.unframe();
        }

        this.lastRange = selected;

        let face;
        if (same(selected.from, selected.to)) {
            face = this.pendingFace ?? this.faceAt(selected.from).f;
        } else {
            face = bold | italic | underline | strike;
            let text = false;
            for (let b = selected.from.b; b <= selected.to.b; b++) {
                let at = 0;
                const from = b === selected.from.b ? selected.from.o : 0;
                const to = b === selected.to.b ? selected.to.o : Infinity;
                for (const item of this.blocks[b].items) {
                    const length = lengthOf(item);
                    if (!item.atom && at < to && at + length > from) {
                        face &= item.f;
                        text = true;
                    }

                    at += length;
                }
            }

            // What is selected has no text — a picture alone — and looks as the text around it does.
            if (!text) {
                face = this.faceAt(selected.from).f;
            }
        }

        // The font and size shown are those of the character before a caret, or of the first one selected.
        let look = selected.from;
        if (!same(selected.from, selected.to)) {
            look = this.clamp({ b: selected.from.b, o: selected.from.o + 1 });
        }

        const unit = this.session.u[this.faceAt(look).u];
        this.callbacks.state({
            font: unit?.n ?? this.session.font,
            size: unit?.z ?? this.session.size,
            bold: (face & bold) !== 0,
            italic: (face & italic) !== 0,
            underline: (face & underline) !== 0,
            strike: (face & strike) !== 0,
            // A unit's v is its vertical alignment, 1 raised and 2 lowered (EditSession).
            superscript: unit?.v === 1,
            subscript: unit?.v === 2,
            align: this.blocks[selected.from.b].align ?? this.session.align,
            // Something is selected rather than a caret: Cut and Copy have nothing to act on otherwise.
            selected: !same(selected.from, selected.to),
            // A picture selected alone, framed: what Layout > Wrap Text acts on.
            picture: Boolean(this.frame),
            // F8's extend mode on, which the status bar shows.
            extend: Boolean(this.extend)
        });
    }

    /**
     * Where the reader is in the paragraph, in the document's paragraphs rather than this editor's
     * blocks: [first, last, caret paragraph, caret place, start paragraph, start place, end paragraph,
     * end place]. Each block Enter divided off is the paragraph after the one before it, once the
     * paragraph is handed in. Null when the reader has been nowhere in it.
     */
    target() {
        const selected = (!this.closed && this.range()) || this.lastRange;
        const p = this.session.p;
        if (!selected || typeof p !== 'number') {
            return null;
        }

        return [
            p + selected.from.b, p + selected.to.b,
            p + selected.to.b, selected.to.o,
            p + selected.from.b, selected.from.o,
            p + selected.to.b, selected.to.o
        ];
    }

    // Handing in

    payload() {
        return JSON.stringify(this.blocks.map(block => ({
            a: block.align,
            i: block.items.map(item => item.atom ? [item.u] : [item.u, item.t, item.f])
        })));
    }

    get changed() {
        return this.payload() !== this.original;
    }

    /**
     * Hands the paragraph in, and says whether there was anything to hand. then is 0, or 1 or 2 to
     * have the paragraph joined to the one before or after it, 3 or 4 to go on to the paragraph
     * before or after it, or 5 to go on typing where the caret was. The box stays, no longer
     * editable, until the page is drawn again.
     */
    commit(then = 0) {
        if (this.pending) {
            // Already on its way: whoever asks again waits for the same answer.
            return this.handing;
        }

        if (this.closed) {
            return Promise.resolve(false);
        }

        if (this.composing) {
            this.onComposed();
        }

        this.setExtend(0);
        this.tidy();
        const payload = this.payload();
        if (then === 5 && payload === this.original) {
            // Nothing to hand in: typing goes on where it was.
            return Promise.resolve(false);
        }

        if (payload === this.original && (then === 0 || then >= 3)) {
            this.callbacks.cancel(this.session.id);
            this.dispose();
            // Left or Right out of a paragraph left as it was: no edit, only the step.
            if (then >= 3) {
                this.callbacks.step(this.session.p, then === 3 ? -1 : 1);
            }

            return Promise.resolve(false);
        }

        this.pending = true;
        this.box.contentEditable = 'false';
        this.box.classList.add('edit-pending');
        this.box.setAttribute('aria-busy', 'true');
        this.callbacks.state(null);
        this.handing = Promise.resolve(this.callbacks.commit(this, payload, then)).then(() => true, () => true);
        return this.handing;
    }

    cancel() {
        if (this.closed) {
            return;
        }

        this.callbacks.cancel(this.session.id);
        this.dispose();
    }

    dispose() {
        if (this.closed) {
            return;
        }

        this.setExtend(0);
        this.closed = true;
        this.abort.abort();
        this.callbacks.state(null);
        this.layer.remove();
    }
}
