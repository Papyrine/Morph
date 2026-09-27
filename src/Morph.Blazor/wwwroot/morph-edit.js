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

function same(first, second) {
    return first.b === second.b && first.o === second.o;
}

export class ParagraphEditor {
    /**
     * sheet is the page's own element (.viewer-sheet), session what EditSession wrote, and callbacks
     * { commit(id, payload, then), cancel(id), state(state), leave(direction, x, y) }.
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
        const drawn = session.line / 2 + (ascent - (ascent + descent) / 2) * size;
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
        span.className = 'edit-run';
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

    // Takes out the text between two places. What is not text stays: it is not the editor's to delete.
    remove(from, to) {
        if (same(from, to)) {
            return;
        }

        const start = this.divide(from);
        const first = this.blocks[from.b];
        if (from.b === to.b) {
            const end = this.divide(to);
            first.items.splice(start, end - start, ...first.items.slice(start, end).filter(_ => _.atom));
            return;
        }

        const kept = first.items.slice(start).filter(_ => _.atom);
        for (let index = from.b + 1; index < to.b; index++) {
            kept.push(...this.blocks[index].items.filter(_ => _.atom));
        }

        const last = this.blocks[to.b];
        const end = this.divide(to);
        kept.push(...last.items.slice(0, end).filter(_ => _.atom), ...last.items.slice(end));
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

    // Replaces what is selected with text; a line's end in the text ends the paragraph.
    replace(range, text, kind) {
        this.record(kind);
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

        if (same(selected.from, selected.to)) {
            // Nothing selected: what is typed next.
            const face = this.pendingFace ?? this.faceAt(selected.from).f;
            this.pendingFace = face ^ bit;
            this.notify();
            return;
        }

        this.record('format');
        const touched = this.textBetween(selected.from, selected.to);
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

    // History: the paragraph as it was before each change, typing taken a burst at a time.

    record(kind) {
        const now = performance.now();
        const typing = kind === 'type' && this.lastKind === 'type' && now - this.lastTime < 1000;
        this.lastKind = kind;
        this.lastTime = now;
        if (typing) {
            return;
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
        box.addEventListener('input', () => this.onInput(), { signal });
        box.addEventListener('compositionstart', () => {
            this.record('compose');
            this.composing = true;
        }, { signal });
        box.addEventListener('compositionend', () => {
            this.composing = false;
            this.adopt();
        }, { signal });
        box.addEventListener('keydown', event => this.onKeyDown(event), { signal });
        box.addEventListener('focusout', () => this.onFocusOut(), { signal });
        document.addEventListener('selectionchange', () => {
            if (!this.closed && !this.pending && this.box.contains(getSelection()?.anchorNode ?? null)) {
                this.notify();
            }
        }, { signal });
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

        event.preventDefault();
        this.handled = true;
        const range = this.targetRange(event);
        if (!range) {
            return;
        }

        switch (type) {
            case 'insertText':
            case 'insertReplacementText':
            case 'insertFromYank':
                this.replace(range, event.data ?? event.dataTransfer?.getData('text/plain') ?? '', type === 'insertText' ? 'type' : 'paste');
                break;
            case 'insertFromPaste':
            case 'insertFromPasteAsQuotation':
            case 'insertFromDrop':
                this.replace(range, event.dataTransfer?.getData('text/plain') ?? event.data ?? '', 'paste');
                break;
            case 'insertParagraph':
                this.paragraphBreak(range);
                break;
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
            default:
                if (type.startsWith('delete')) {
                    this.onDelete(range, type.endsWith('Backward'));
                }

                break;
        }
    }

    // Deleting at the very start of the paragraph, or at its very end, is deleting the break
    // between it and its neighbour: the two are joined, which is the file's to do.
    onDelete(range, backward) {
        if (!same(range.from, range.to)) {
            this.replace(range, '', 'delete');
            return;
        }

        const last = this.blocks.length - 1;
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

        // The browser made a change the editor was not asked about.
        this.adopt();
    }

    onKeyDown(event) {
        if (this.pending || this.closed || event.isComposing) {
            return;
        }

        const command = (event.ctrlKey || event.metaKey) && !event.altKey;
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
                    this.redo();
                    return;
                case 'enter':
                    event.preventDefault();
                    this.commit(0);
                    return;
            }

            return;
        }

        switch (event.key) {
            case 'Escape':
                event.preventDefault();
                event.stopPropagation();
                this.cancel();
                return;
            case 'Tab':
                event.preventDefault();
                if (this.range()) {
                    this.replace(this.range(), '\t', 'type');
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
        const sideways = event.key === 'ArrowLeft' || event.key === 'ArrowRight';
        const x = sideways
            ? (direction < 0 ? bounds.right - 1 : bounds.left + 1)
            : caret?.left ?? bounds.left + 1;
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

        let face;
        if (same(selected.from, selected.to)) {
            face = this.pendingFace ?? this.faceAt(selected.from).f;
        } else {
            face = bold | italic | underline | strike;
            for (let b = selected.from.b; b <= selected.to.b; b++) {
                let at = 0;
                const from = b === selected.from.b ? selected.from.o : 0;
                const to = b === selected.to.b ? selected.to.o : Infinity;
                for (const item of this.blocks[b].items) {
                    const length = lengthOf(item);
                    if (!item.atom && at < to && at + length > from) {
                        face &= item.f;
                    }

                    at += length;
                }
            }
        }

        this.callbacks.state({
            bold: (face & bold) !== 0,
            italic: (face & italic) !== 0,
            underline: (face & underline) !== 0,
            strike: (face & strike) !== 0,
            align: this.blocks[selected.from.b].align ?? this.session.align
        });
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
     * have the paragraph joined to the one before or after it. The box stays, no longer editable,
     * until the page is drawn again.
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
            this.composing = false;
            this.adopt();
        }

        this.tidy();
        const payload = this.payload();
        if (payload === this.original && then === 0) {
            this.callbacks.cancel(this.session.id);
            this.dispose();
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

        this.closed = true;
        this.abort.abort();
        this.callbacks.state(null);
        this.layer.remove();
    }
}
