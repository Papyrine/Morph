// Word's AutoCorrect and AutoFormat as you type, as the probes recorded them (src/word-behavior.md,
// R-A-1 to R-A-6): what a paragraph's text becomes as a word is finished. Pure functions over the text, so
// the box (morph-edit.js) decides where they run and makes each correction an undo entry of its own,
// which is how Word's first Ctrl+Z takes the correction back and leaves what was typed.
//
// Left out: superscript ordinals (a superscript look belongs to a run, .NET's), links typed as text, border
// lines and tables typed as text, AutoComplete, and Word's arrows and smiley, which it writes in Wingdings —
// here they are Unicode's.

const letter = /[\p{L}\p{N}'’]/u;

const days = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday'];

// Word's replacement entries that the probes saw fire (A-19), less those it draws in Wingdings.
const replacements = new Map([
    ['(c)', '©'],
    ['(r)', '®'],
    ['(tm)', '™'],
    ['-->', '→'],
    ['<--', '←'],
    ['teh', 'the'],
    ['adn', 'and'],
    ['i', 'I']
]);

const fractions = new Map([
    ['1/2', '½'],
    ['1/4', '¼'],
    ['3/4', '¾']
]);

// The typed leaders that start a list on the Space after them (A-11, A-12); `• `, `o ` and `2. ` do not.
const bulletLeaders = ['*', '-', '>'];
const numberLeaders = /^(1[.)-]|\(1\)|a\.|i\.)$/;

/**
 * A straight quote typed after `before` as Word's smart quotes write it (A-05): an opening quote at a
 * paragraph's start or after a space or an opening bracket or quote, otherwise a closing one — which is
 * also the apostrophe in `don't`.
 */
export function smartQuote(before, quote) {
    const opening = before.length === 0 || /[\s([{“‘—–]$/.test(before);
    if (quote === '"') {
        if (opening) {
            return '“';
        }

        return '”';
    }

    if (opening) {
        return '‘';
    }

    return '’';
}

/**
 * The corrections a word finished at `end` of `text` makes, the delimiter that finished it being
 * `trigger` (a space, a punctuation mark, or `\n` for Enter): each `{ from, to, text }` replaces that
 * stretch, `face` adds a look to what it writes, and `list` asks for the paragraph to become a bulleted
 * or numbered one. Offsets are into the text as it is when that correction runs, after the ones before it.
 */
export function corrections(text, end, trigger) {
    const found = [];
    let current = text;
    const apply = correction => {
        found.push(correction);
        current = current.slice(0, correction.from) + correction.text + current.slice(correction.to);
        end += correction.text.length - (correction.to - correction.from);
    };

    // An automatic list (A-11, A-12, A-26): a leader alone before the Space at the paragraph's start.
    if (trigger === ' ') {
        const leader = current.slice(0, end);
        if (bulletLeaders.includes(leader)) {
            found.push({ from: 0, to: end + 1, text: '', list: 'bullets' });
            return found;
        }

        if (numberLeaders.test(leader)) {
            found.push({ from: 0, to: end + 1, text: '', list: 'numbering' });
            return found;
        }
    }

    const tokenStart = start(current, end, _ => !/\s/.test(_));
    const token = current.slice(tokenStart, end);
    if (token.length === 0) {
        return found;
    }

    // Replacement entries and fractions (A-08, A-19), the whole token.
    const replacement = replacements.get(token) ?? fractions.get(token);
    if (replacement) {
        apply({ from: tokenStart, to: end, text: replacement });
    }

    // *bold* and _italic_ (A-09): the marks go and the text between takes the look.
    const marked = /^([*_])([^*_\s][^*_]*)\1$/.exec(current.slice(tokenStart, end));
    if (marked) {
        apply({ from: tokenStart, to: end, text: marked[2], face: marked[1] === '*' ? 'bold' : 'italic' });
    }

    // An em dash between two words typed as `--` (A-07), and ` - ` before the word just finished as an
    // en dash (A-07): `a-b` stays.
    const doubled = /^([\p{L}\p{N}]+)--([\p{L}\p{N}]+)$/u.exec(current.slice(tokenStart, end));
    if (doubled) {
        apply({ from: tokenStart + doubled[1].length, to: tokenStart + doubled[1].length + 2, text: '—' });
    }

    const wordStart = start(current, end, _ => letter.test(_));
    if (wordStart >= 4 &&
        current.slice(wordStart - 3, wordStart) === ' - ' &&
        !/\s/.test(current[wordStart - 4])) {
        apply({ from: wordStart - 2, to: wordStart - 1, text: '–' });
    }

    const word = current.slice(wordStart, end);
    if (word.length === 0) {
        return found;
    }

    // TWo INitial CApitals (A-02), and a day's name (A-03).
    if (/^\p{Lu}{2}\p{Ll}{2,}$/u.test(word)) {
        apply({ from: wordStart + 1, to: wordStart + 2, text: word[1].toLowerCase() });
    } else if (days.includes(word)) {
        apply({ from: wordStart, to: wordStart + 1, text: word[0].toUpperCase() });
    }

    // The first word of a sentence (A-01): at the paragraph's start, or after a sentence's end — but not
    // after an abbreviation such as `e.g.` or after a colon.
    if (/^\p{Ll}/u.test(current[wordStart]) && startsSentence(current.slice(0, wordStart))) {
        apply({ from: wordStart, to: wordStart + 1, text: current[wordStart].toUpperCase() });
    }

    return found;
}

// Where a run of characters that pass `test` ends at `end` starts.
function start(text, end, test) {
    let from = end;
    while (from > 0 && test(text[from - 1])) {
        from--;
    }

    return from;
}

function startsSentence(before) {
    const trimmed = before.replace(/[\s ]+$/, '');
    if (trimmed.length === 0) {
        return true;
    }

    if (trimmed.length === before.length || !/[.?!]$/.test(trimmed)) {
        return false;
    }

    return !/(^|\s)(\p{L}\.){2,}$/u.test(trimmed);
}
