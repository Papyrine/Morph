// The grid's controller — the browser half of SheetEditor, the editor's view of an Excel workbook. .NET owns
// the workbook: the cells, what they hold and look like, the selection, and every edit. This script owns
// what has to happen inside the browser's own event: the keyboard (a key's default has to be stopped while
// it is being pressed), the typing in a cell (nothing crosses to .NET until it is handed in, as in a Word
// paragraph), the mouse (which cell a point is over, from the geometry .NET hands it), the clipboard (which
// a page may touch only inside a user's gesture) and the scroll position, which .NET is told so it draws
// only the cells in view.
//
// The keyboard's focus sits in one textarea laid over the active cell (.sheet-input). Not editing, it is
// invisible and empty, and a key that is typed into it starts the cell's editing with that key, as Excel's
// does — which is also how an input method's composition starts it. Editing, it shows, set in the cell's
// font, and holds what the cell will hold.
//
// Editing starts in Excel's Enter mode (typed over the cell: the arrows hand the cell in and move on) or
// Edit mode (F2, a double click or the formula bar: the arrows move the caret). In a formula, where a
// reference can go next, a click or a drag on the grid puts in the reference of the cells pointed at.

const headerHeight = 20;
const resizeSlop = 4;
const autoScrollStep = 24;

export function attachSheet(root, dotNet) {
    const sheet = new Sheet(root, dotNet);
    return {
        setGeometry: (lefts, tops, rowHeader, zoom) => sheet.setGeometry(lefts, tops, rowHeader, zoom),
        focus: () => sheet.focus(),
        beginEdit: (text, mode, caret) => sheet.beginEdit(text, mode, caret),
        endEdit: () => sheet.endEdit(),
        reveal: (row, column, lastRow, lastColumn) => sheet.reveal(row, column, lastRow, lastColumn),
        scrollTo: (top, left) => sheet.scrollTo(top, left),
        copyText: text => navigator.clipboard?.writeText(text).catch(() => {}),
        readClipboard: () => navigator.clipboard?.readText().catch(() => null) ?? Promise.resolve(null),
        report: () => sheet.report(),
        handIn: () => sheet.handIn(),
        editing: () => !!sheet.mode,
        dispose: () => sheet.dispose()
    };
}

class Sheet {
    constructor(root, dotNet) {
        this.root = root;
        this.dotNet = dotNet;
        this.abort = new AbortController();
        this.lefts = [0];
        this.tops = [0];
        this.rowHeader = 40;
        this.zoom = 1;
        this.mode = null;
        this.pointing = null;
        this.dragging = null;
        this.pending = false;
        // Counts the cells edited, so a hand-in that finishes late knows another has started since.
        this.generation = 0;
        const signal = this.abort.signal;

        root.addEventListener('keydown', event => this.onKeyDown(event), { signal });
        root.addEventListener('input', event => this.onInput(event), { signal });
        root.addEventListener('compositionstart', event => this.onCompositionStart(event), { signal });
        root.addEventListener('paste', event => this.onPaste(event), { signal });
        root.addEventListener('mousedown', event => this.onMouseDown(event), { signal });
        root.addEventListener('dblclick', event => this.onDoubleClick(event), { signal });
        root.addEventListener('focusin', event => this.onFocusIn(event), { signal });
        root.addEventListener('scroll', () => this.onScroll(), { signal, capture: true });
        window.addEventListener('resize', () => this.onScroll(), { signal });
        document.addEventListener('mousemove', event => this.onMouseMove(event), { signal });
        document.addEventListener('mouseup', event => this.onMouseUp(event), { signal });
        this.observer = new ResizeObserver(() => this.onScroll());
        const scroller = this.scroller();
        if (scroller) {
            this.observer.observe(scroller);
        }
    }

    dispose() {
        this.abort.abort();
        this.observer.disconnect();
        clearInterval(this.autoScroll);
    }

    scroller() { return this.root.querySelector('.sheet-scroll'); }

    input() { return this.root.querySelector('.sheet-input'); }

    bar() { return this.root.querySelector('.sheet-formula'); }

    cells() { return this.root.querySelector('.sheet-cells'); }

    call(name, ...args) {
        return this.dotNet.invokeMethodAsync(name, ...args).catch(() => null);
    }

    setGeometry(lefts, tops, rowHeader, zoom) {
        this.lefts = lefts;
        this.tops = tops;
        this.rowHeader = rowHeader;
        this.zoom = zoom || 1;
    }

    focus() {
        const input = this.input();
        if (input && document.activeElement !== input && !this.barFocused()) {
            input.focus({ preventScroll: true });
        }
    }

    barFocused() {
        const active = document.activeElement;
        return active && active.matches?.('.sheet-formula, .sheet-name-box, .sheet-find, select, .editor-menu *');
    }

    // Scroll position and viewport in unzoomed pixels of the grid, cells only (headers taken off).
    report() {
        const scroller = this.scroller();
        if (!scroller) {
            return;
        }

        const zoom = this.zoom;
        this.call('OnSheetScroll',
            scroller.scrollTop / zoom,
            scroller.scrollLeft / zoom,
            Math.max(0, scroller.clientWidth / zoom - this.rowHeader),
            Math.max(0, scroller.clientHeight / zoom - headerHeight));
    }

    onScroll() {
        if (this.pending) {
            return;
        }

        this.pending = true;
        requestAnimationFrame(() => {
            this.pending = false;
            this.report();
        });
    }

    scrollTo(top, left) {
        const scroller = this.scroller();
        if (scroller) {
            scroller.scrollTop = top * this.zoom;
            scroller.scrollLeft = left * this.zoom;
        }
    }

    // Scrolls the least that brings the cells into view, past the headers.
    reveal(row, column, lastRow, lastColumn) {
        const scroller = this.scroller();
        if (!scroller) {
            return;
        }

        const zoom = this.zoom;
        const top = this.tops[Math.min(row - 1, this.tops.length - 1)] ?? 0;
        const bottom = this.tops[Math.min(lastRow, this.tops.length - 1)] ?? top;
        const left = this.lefts[Math.min(column - 1, this.lefts.length - 1)] ?? 0;
        const right = this.lefts[Math.min(lastColumn, this.lefts.length - 1)] ?? left;
        const viewTop = scroller.scrollTop / zoom;
        const viewLeft = scroller.scrollLeft / zoom;
        const viewHeight = scroller.clientHeight / zoom - headerHeight;
        const viewWidth = scroller.clientWidth / zoom - this.rowHeader;
        if (top < viewTop) {
            scroller.scrollTop = top * zoom;
        } else if (bottom > viewTop + viewHeight) {
            scroller.scrollTop = Math.min(top, bottom - viewHeight) * zoom;
        }

        if (left < viewLeft) {
            scroller.scrollLeft = left * zoom;
        } else if (right > viewLeft + viewWidth) {
            scroller.scrollLeft = Math.min(left, right - viewWidth) * zoom;
        }
    }

    // Which row or column a point is in, by the edges .NET handed over.
    static find(edges, at) {
        let low = 0;
        let high = edges.length - 1;
        while (low < high) {
            const middle = (low + high + 1) >> 1;
            if (edges[middle] <= at) {
                low = middle;
            } else {
                high = middle - 1;
            }
        }

        return Math.min(low + 1, Math.max(1, edges.length - 1));
    }

    cellAt(event) {
        const cells = this.cells();
        if (!cells) {
            return null;
        }

        const rect = cells.getBoundingClientRect();
        const x = (event.clientX - rect.left) / this.zoom;
        const y = (event.clientY - rect.top) / this.zoom;
        return { row: Sheet.find(this.tops, y), column: Sheet.find(this.lefts, x), x, y };
    }

    // Editing

    beginEdit(text, mode, caret) {
        const input = this.input();
        if (!input) {
            return;
        }

        this.mode = mode;
        this.generation++;
        input.value = text;
        input.classList.add('editing');
        this.grow();
        const bar = this.bar();
        if (bar) {
            bar.value = text;
        }

        if (!this.barFocused()) {
            input.focus({ preventScroll: true });
            const at = caret ?? text.length;
            input.setSelectionRange(at, at);
        }
    }

    endEdit() {
        this.mode = null;
        this.pointing = null;
        const input = this.input();
        if (input) {
            input.value = '';
            input.classList.remove('editing');
            input.style.width = '';
            input.style.height = '';
        }
    }

    // The box widens with what is typed, as far as the grid shows, as Excel's does.
    grow() {
        const input = this.input();
        if (!input || !this.mode) {
            return;
        }

        input.style.width = '';
        const scroller = this.scroller();
        const most = scroller ? scroller.clientWidth / this.zoom : 2000;
        if (input.scrollWidth > input.clientWidth) {
            input.style.width = `${Math.min(most, input.scrollWidth + 8)}px`;
        }

        input.style.height = '';
        if (input.scrollHeight > input.clientHeight) {
            input.style.height = `${input.scrollHeight}px`;
        }
    }

    // What is typed into the box while not editing starts the cell's editing with it, in Enter mode.
    onInput(event) {
        const input = this.input();
        const bar = this.bar();
        if (event.target === input) {
            if (!this.mode) {
                this.mode = 'enter';
                this.generation++;
                input.classList.add('editing');
                this.call('OnSheetEditStart', 'enter');
            }

            if (bar) {
                bar.value = input.value;
            }

            this.pointing = null;
            this.grow();
            return;
        }

        if (event.target === bar) {
            if (!this.mode) {
                this.mode = 'edit';
                this.generation++;
                input?.classList.add('editing');
                this.call('OnSheetEditStart', 'edit');
            }

            if (input) {
                input.value = bar.value;
                this.grow();
            }

            this.pointing = null;
        }
    }

    onCompositionStart(event) {
        if (event.target === this.input() && !this.mode) {
            this.mode = 'enter';
            this.generation++;
            this.input().classList.add('editing');
            this.call('OnSheetEditStart', 'enter');
        }
    }

    // The formula bar taken: the cell's editing starts, in Edit mode, from what the cell holds.
    onFocusIn(event) {
        if (event.target === this.bar() && !this.mode) {
            this.call('OnSheetBarFocus');
        }
    }

    value() {
        return this.input()?.value ?? '';
    }

    // What is typed is taken out of the box as it is handed in, so the next cell's typing starts afresh
    // there at once, while .NET is still setting this one (it does what it is handed in order). A hand-in
    // refused — a formula that cannot be read — puts the text back, unless typing has started since.
    async commit(move) {
        const input = this.input();
        const text = this.value();
        const mode = this.mode;
        const generation = ++this.generation;
        this.endEdit();
        const ok = await this.call('OnSheetCommit', text, move);
        if (generation !== this.generation) {
            return ok;
        }

        if (!ok) {
            this.mode = mode;
            if (input) {
                input.value = text;
                input.classList.add('editing');
                this.grow();
                input.focus({ preventScroll: true });
            }

            return false;
        }

        this.focus();
        return ok;
    }

    cancel() {
        this.endEdit();
        this.call('OnSheetCancel');
        this.focus();
    }

    onKeyDown(event) {
        const target = event.target;
        if (!(target instanceof HTMLElement) || !this.root.contains(target)) {
            return;
        }

        if (target.matches('.sheet-name-box')) {
            if (event.key === 'Enter') {
                event.preventDefault();
                this.call('OnSheetGoTo', target.value).then(() => this.focus());
            } else if (event.key === 'Escape') {
                event.preventDefault();
                this.focus();
            }

            return;
        }

        if (target.matches('.sheet-find')) {
            if (event.key === 'Enter') {
                event.preventDefault();
                this.call('OnSheetFind', target.value, !event.shiftKey);
            } else if (event.key === 'Escape') {
                event.preventDefault();
                this.focus();
            }

            return;
        }

        const inInput = target === this.input();
        const inBar = target === this.bar();
        if (!inInput && !inBar) {
            return;
        }

        const ctrl = event.ctrlKey || event.metaKey;
        if (this.mode) {
            this.onEditingKey(event, ctrl, inBar);
            return;
        }

        if (inBar) {
            return;
        }

        this.onGridKey(event, ctrl);
    }

    onEditingKey(event, ctrl, inBar) {
        const key = event.key;
        if (event.isComposing) {
            return;
        }

        if (key === 'Enter' && event.altKey && !ctrl) {
            event.preventDefault();
            const field = inBar ? this.bar() : this.input();
            field.setRangeText('\n', field.selectionStart, field.selectionEnd, 'end');
            field.dispatchEvent(new Event('input', { bubbles: true }));
            return;
        }

        if (key === 'Enter') {
            event.preventDefault();
            this.commit(ctrl ? 'fill' : event.shiftKey ? 'up' : 'down');
            return;
        }

        if (key === 'Tab') {
            event.preventDefault();
            this.commit(event.shiftKey ? 'left' : 'right');
            return;
        }

        if (key === 'Escape') {
            event.preventDefault();
            this.cancel();
            return;
        }

        if (key === 'F2' && !inBar) {
            event.preventDefault();
            this.mode = this.mode === 'enter' ? 'edit' : 'enter';
            this.call('OnSheetEditStart', this.mode);
            return;
        }

        if (key === 'F4') {
            event.preventDefault();
            this.cycleAbsolute(inBar ? this.bar() : this.input());
            return;
        }

        // In Enter mode the arrows hand the cell in and go on, unless a reference is being pointed at.
        const arrows = { ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right' };
        if (this.mode === 'enter' && !inBar && arrows[key] && !event.shiftKey && !ctrl) {
            if (this.canPoint()) {
                event.preventDefault();
                this.pointByKey(arrows[key]);
                return;
            }

            event.preventDefault();
            this.commit(arrows[key]);
            return;
        }

        if (ctrl && key.toLowerCase() === 's') {
            event.preventDefault();
            this.commit('none').then(ok => ok && this.call('OnSheetKey', 'Save', false, false));
        }
    }

    onGridKey(event, ctrl) {
        const key = event.key;
        const shift = event.shiftKey;
        const named = {
            ArrowUp: 'Up', ArrowDown: 'Down', ArrowLeft: 'Left', ArrowRight: 'Right',
            Home: 'Home', End: 'End', PageUp: 'PageUp', PageDown: 'PageDown',
            Enter: 'Enter', Tab: 'Tab', Delete: 'Delete', Backspace: 'Backspace', F2: 'F2', Escape: 'Escape'
        };
        if (named[key] && !event.altKey) {
            event.preventDefault();
            this.call('OnSheetKey', named[key], shift, ctrl);
            return;
        }

        if (event.altKey && !ctrl && key === '=') {
            event.preventDefault();
            this.call('OnSheetKey', 'AutoSum', false, false);
            return;
        }

        if (!ctrl || event.altKey) {
            // A character: it goes into the box, which starts the editing (onInput).
            return;
        }

        const letter = key.toLowerCase();
        switch (letter) {
            case 'c':
            case 'x':
                event.preventDefault();
                this.call('OnSheetCopy', letter === 'x').then(text => {
                    if (text != null) {
                        navigator.clipboard?.writeText(text).catch(() => {});
                    }
                });
                return;
            case 'v':
                // The paste event that follows carries the clipboard.
                return;
            case 'z':
                event.preventDefault();
                this.call('OnSheetKey', shift ? 'Redo' : 'Undo', false, false);
                return;
            case 'y':
                event.preventDefault();
                this.call('OnSheetKey', 'Redo', false, false);
                return;
            case 'a':
            case 'b':
            case 'i':
            case 'u':
            case 's':
            case 'f':
            case '1':
            case ';':
            case 'd':
            case 'r':
                event.preventDefault();
                this.call('OnSheetKey', { a: 'SelectAll', b: 'Bold', i: 'Italic', u: 'Underline', s: 'Save', f: 'Find', 1: 'Format', ';': 'Today', d: 'FillDown', r: 'FillRight' }[letter], shift, ctrl);
                return;
            case 'pageup':
            case 'pagedown':
                return;
        }

        if (key === '`') {
            event.preventDefault();
            this.call('OnSheetKey', 'ShowFormulas', false, false);
        }
    }

    onPaste(event) {
        if (this.mode || event.target !== this.input()) {
            return;
        }

        event.preventDefault();
        const text = event.clipboardData?.getData('text/plain') ?? '';
        this.call('OnSheetPaste', text);
    }

    // F4 cycles the reference at the caret through $A$1, A$1, $A1 and A1.
    cycleAbsolute(field) {
        const text = field.value;
        const caret = field.selectionStart;
        const pattern = /(\$?)([A-Za-z]{1,3})(\$?)(\d{1,7})/g;
        let match;
        while ((match = pattern.exec(text)) !== null) {
            const start = match.index;
            const end = start + match[0].length;
            if (caret < start || caret > end) {
                continue;
            }

            const absoluteColumn = match[1] === '$';
            const absoluteRow = match[3] === '$';
            let next;
            if (absoluteColumn && absoluteRow) {
                next = `${match[2]}$${match[4]}`;
            } else if (!absoluteColumn && absoluteRow) {
                next = `$${match[2]}${match[4]}`;
            } else if (absoluteColumn && !absoluteRow) {
                next = `${match[2]}${match[4]}`;
            } else {
                next = `$${match[2]}$${match[4]}`;
            }

            field.setRangeText(next, start, end, 'end');
            field.dispatchEvent(new Event('input', { bubbles: true }));
            return;
        }
    }

    // Pointing: a formula's next reference typed by clicking or with the arrows.

    canPoint() {
        if (this.pointing) {
            return true;
        }

        const field = this.input();
        const text = field?.value ?? '';
        if (!text.startsWith('=') || field.selectionStart !== field.selectionEnd) {
            return false;
        }

        const before = text.slice(0, field.selectionStart).trimEnd();
        return /[=(,+\-*/^&<>:;]$/.test(before);
    }

    point(row, column, lastRow, lastColumn) {
        const field = this.input();
        const reference = lastRow != null && (lastRow !== row || lastColumn !== column)
            ? `${name(Math.min(column, lastColumn))}${Math.min(row, lastRow)}:${name(Math.max(column, lastColumn))}${Math.max(row, lastRow)}`
            : `${name(column)}${row}`;
        if (!this.pointing) {
            const at = field.selectionStart;
            this.pointing = { start: at, end: at, row, column, lastRow: row, lastColumn: column };
        }

        field.setRangeText(reference, this.pointing.start, this.pointing.end, 'end');
        this.pointing.end = this.pointing.start + reference.length;
        this.pointing.lastRow = lastRow ?? row;
        this.pointing.lastColumn = lastColumn ?? column;
        if (lastRow == null) {
            this.pointing.row = row;
            this.pointing.column = column;
        }

        const bar = this.bar();
        if (bar) {
            bar.value = field.value;
        }

        this.call('OnSheetPoint', this.pointing.row, this.pointing.column, this.pointing.lastRow, this.pointing.lastColumn);
    }

    pointByKey(direction) {
        let row = this.pointing?.row;
        let column = this.pointing?.column;
        if (row == null) {
            row = Number(this.input().dataset.row);
            column = Number(this.input().dataset.column);
        }

        switch (direction) {
            case 'up': row = Math.max(1, row - 1); break;
            case 'down': row += 1; break;
            case 'left': column = Math.max(1, column - 1); break;
            default: column += 1; break;
        }

        this.point(row, column);
    }

    // The mouse

    onMouseDown(event) {
        if (event.button !== 0) {
            return;
        }

        const target = event.target;
        if (!(target instanceof Element)) {
            return;
        }

        const columnHead = target.closest('.sheet-column-heads');
        const rowHead = target.closest('.sheet-row-heads');
        const corner = target.closest('.sheet-corner');
        const cells = target.closest('.sheet-cells');
        if (!columnHead && !rowHead && !corner && !cells) {
            return;
        }

        const at = this.cellAt(event);
        if (!at) {
            return;
        }

        if (corner) {
            event.preventDefault();
            this.handIn().then(() => this.call('OnSheetKey', 'SelectAll', false, true));
            return;
        }

        if (columnHead) {
            event.preventDefault();
            const edge = this.edgeNear(this.lefts, at.x, at.column);
            if (edge != null) {
                this.dragging = { kind: 'column', index: edge, start: event.clientX, size: this.lefts[edge] - this.lefts[edge - 1] };
                this.showGuide('column', this.lefts[edge]);
                return;
            }

            this.handIn().then(() => {
                this.call('OnSheetHeader', 'column', at.column, event.shiftKey, 'down');
                this.dragging = { kind: 'columns' };
                this.focus();
            });
            return;
        }

        if (rowHead) {
            event.preventDefault();
            const edge = this.edgeNear(this.tops, at.y, at.row);
            if (edge != null) {
                this.dragging = { kind: 'row', index: edge, start: event.clientY, size: this.tops[edge] - this.tops[edge - 1] };
                this.showGuide('row', this.tops[edge]);
                return;
            }

            this.handIn().then(() => {
                this.call('OnSheetHeader', 'row', at.row, event.shiftKey, 'down');
                this.dragging = { kind: 'rows' };
                this.focus();
            });
            return;
        }

        if (target.closest('.sheet-input.editing')) {
            // A click in the box being typed in places its caret.
            return;
        }

        event.preventDefault();
        if (this.mode && this.canPoint()) {
            this.point(at.row, at.column);
            this.dragging = { kind: 'point', row: at.row, column: at.column };
            return;
        }

        this.handIn().then(ok => {
            if (ok === false) {
                return;
            }

            this.call('OnSheetPointer', at.row, at.column, event.shiftKey, 'down');
            this.dragging = { kind: 'cells', row: at.row, column: at.column };
            this.focus();
        });
    }

    // The edge a press on a header is on, if it is within reach of one: the column or row it ends.
    edgeNear(edges, at, index) {
        const end = edges[index];
        const start = edges[index - 1];
        if (end != null && Math.abs(at - end) <= resizeSlop) {
            return index;
        }

        if (start != null && index > 1 && Math.abs(at - start) <= resizeSlop) {
            return index - 1;
        }

        return null;
    }

    showGuide(kind, at) {
        const guide = this.root.querySelector('.sheet-guide');
        if (!guide) {
            return;
        }

        guide.dataset.kind = kind;
        guide.hidden = false;
        if (kind === 'column') {
            guide.style.left = `${at}px`;
            guide.style.top = '0px';
        } else {
            guide.style.top = `${at}px`;
            guide.style.left = '0px';
        }
    }

    // What is being typed is handed in before the selection moves, as a click elsewhere in Excel does.
    async handIn() {
        if (!this.mode) {
            return true;
        }

        return await this.commit('none');
    }

    onMouseMove(event) {
        const drag = this.dragging;
        if (!drag) {
            return;
        }

        if (drag.kind === 'column' || drag.kind === 'row') {
            const delta = (drag.kind === 'column' ? event.clientX - drag.start : event.clientY - drag.start) / this.zoom;
            drag.next = Math.max(0, drag.size + delta);
            const edges = drag.kind === 'column' ? this.lefts : this.tops;
            this.showGuide(drag.kind, edges[drag.index - 1] + drag.next);
            return;
        }

        if ((event.buttons & 1) === 0) {
            this.onMouseUp(event);
            return;
        }

        const at = this.cellAt(event);
        if (!at) {
            return;
        }

        this.edgeScroll(event);
        if (drag.kind === 'point') {
            if (at.row !== drag.lastRow || at.column !== drag.lastColumn) {
                drag.lastRow = at.row;
                drag.lastColumn = at.column;
                this.point(drag.row, drag.column, at.row, at.column);
            }

            return;
        }

        if (drag.kind === 'columns' || drag.kind === 'rows') {
            const index = drag.kind === 'columns' ? at.column : at.row;
            if (index !== drag.last) {
                drag.last = index;
                this.call('OnSheetHeader', drag.kind === 'columns' ? 'column' : 'row', index, true, 'drag');
            }

            return;
        }

        if (at.row !== drag.lastRow || at.column !== drag.lastColumn) {
            drag.lastRow = at.row;
            drag.lastColumn = at.column;
            this.call('OnSheetPointer', at.row, at.column, true, 'drag');
        }
    }

    // Dragged past the grid's edge, the selection carries the grid along with it.
    edgeScroll(event) {
        const scroller = this.scroller();
        clearInterval(this.autoScroll);
        if (!scroller) {
            return;
        }

        const rect = scroller.getBoundingClientRect();
        const dx = event.clientX > rect.right - 8 ? autoScrollStep : event.clientX < rect.left + this.rowHeader ? -autoScrollStep : 0;
        const dy = event.clientY > rect.bottom - 8 ? autoScrollStep : event.clientY < rect.top + headerHeight ? -autoScrollStep : 0;
        if (dx || dy) {
            this.autoScroll = setInterval(() => {
                scroller.scrollLeft += dx;
                scroller.scrollTop += dy;
            }, 50);
        }
    }

    onMouseUp() {
        const drag = this.dragging;
        clearInterval(this.autoScroll);
        if (!drag) {
            return;
        }

        this.dragging = null;
        if (drag.kind === 'column' || drag.kind === 'row') {
            const guide = this.root.querySelector('.sheet-guide');
            if (guide) {
                guide.hidden = true;
            }

            if (drag.next != null && Math.abs(drag.next - drag.size) >= 1) {
                this.call('OnSheetResize', drag.kind, drag.index, drag.next);
            }

            this.focus();
            return;
        }

        if (drag.kind !== 'point') {
            this.focus();
        }
    }

    onDoubleClick(event) {
        const target = event.target;
        if (!(target instanceof Element)) {
            return;
        }

        if (target.closest('.sheet-column-heads, .sheet-row-heads')) {
            const at = this.cellAt(event);
            const columnHead = !!target.closest('.sheet-column-heads');
            const edge = at && (columnHead ? this.edgeNear(this.lefts, at.x, at.column) : this.edgeNear(this.tops, at.y, at.row));
            if (edge != null) {
                this.call('OnSheetFit', columnHead ? 'column' : 'row', edge);
            }

            return;
        }

        if (!target.closest('.sheet-cells') || this.mode) {
            return;
        }

        const at = this.cellAt(event);
        if (at) {
            event.preventDefault();
            this.call('OnSheetPointer', at.row, at.column, false, 'double');
        }
    }
}

// A column's letters: 1 is A, 27 AA.
function name(column) {
    let letters = '';
    for (let remaining = column; remaining > 0; remaining = Math.floor((remaining - 1) / 26)) {
        letters = String.fromCharCode(65 + (remaining - 1) % 26) + letters;
    }

    return letters;
}
