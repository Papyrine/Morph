using static Define;

// A — AutoCorrect and AutoFormat As You Type: each correction with the keystroke that fires it, the
// option on and off, and the undo or Backspace right after. Every row starts in an empty paragraph of
// empty-paragraphs (select(P2:0)) unless its fixture says otherwise. Observe.Undo is in the default set
// because AutoCorrect's entries are named in Word's undo list, which is how a correction is told apart
// from plain typing.
static class AutoCorrectFamily
{
    public static FamilyDefinition Definition { get; } = new("autocorrect", "AutoCorrect and AutoFormat as you type",
        "Each correction and automatic format with its trigger, with the option on and off, and the undo right after.",
        Scenarios);

    static IEnumerable<ScenarioBuilder> Scenarios()
    {
        yield return Probe("A-01", "Sentence capitalisation: which key fires it")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"hello\")", "{trigger}")
            .Axis("trigger", "Space", "Enter", "Tab")
            .Toggle("AutoCorrect.CorrectSentenceCaps", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting)
            .Hypotheses("(a) the first letter is capitalised on Space, Enter and Tab alike; (b) only on Space and Enter.")
            .Morph("TODO");

        yield return Probe("A-01b", "Sentence capitalisation: punctuation then Space")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"hello\")", "type(\"{mark}\")", "Space")
            .Axis("mark", ".", ",", "?")
            .Toggle("AutoCorrect.CorrectSentenceCaps", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting)
            .Hypotheses("(a) the capital is applied when the space after the punctuation is typed; (b) when the punctuation is typed.")
            .Morph("TODO");

        yield return Probe("A-01c", "Sentence capitalisation after a sentence end and after an abbreviation")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"{text}\")")
            .Axis("text", "Alpha. beta ", "Alpha? beta ", "Alpha: beta ", "e.g. beta ")
            .Toggle("AutoCorrect.CorrectSentenceCaps", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting)
            .Hypotheses("(a) a full stop and a question mark start a sentence, a colon does not, and 'e.g.' is on the exceptions list so 'beta' stays lower case; (b) every full stop starts a sentence.")
            .Morph("TODO");

        yield return Probe("A-01d", "Sentence capitalisation at the start of a cell")
            .In("table-2x2-empty")
            .Steps("select(C(1,1):0)", "type(\"hello \")")
            .Toggle("AutoCorrect.CorrectSentenceCaps", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Tables)
            .Hypotheses("(a) a cell start is a sentence start and the word is capitalised (CorrectTableCells is a separate option); (b) left alone.")
            .Morph("TODO");

        yield return Probe("A-01e", "Sentence capitalisation at the start of a new list item")
            .In("numbers")
            .Steps("select(P3:end)", "Enter", "type(\"hello \")")
            .Toggle("AutoCorrect.CorrectSentenceCaps", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Lists)
            .Hypotheses("A list item's first word is capitalised like a sentence start.")
            .Morph("TODO");

        yield return Probe("A-02", "TWo INitial CApitals")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"THis \")")
            .Toggle("AutoCorrect.CorrectInitialCaps", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting)
            .Hypotheses("'THis' becomes 'This' on the space, as one undo entry.")
            .Morph("TODO");

        yield return Probe("A-03", "Capitalising the names of days")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"monday \")")
            .Toggle("AutoCorrect.CorrectDays", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting)
            .Hypotheses("'monday' becomes 'Monday' on the space.")
            .Morph("TODO");

        // A-04 (accidental Caps Lock correction) is not probed: the harness refuses to run with Caps Lock on,
        // and the correction fires only on text typed with the lock engaged, so the row cannot be driven.

        yield return Probe("A-05", "Straight quotes to smart quotes: opening, closing, apostrophe, inch marks and quotes inside parentheses")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"\\\"a\\\" 'b' don't 6'2\\\" (\\\"c\\\") \")")
            .Toggle("Options.AutoFormatAsYouTypeReplaceQuotes", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting)
            .Hypotheses("Each quote is turned as it is typed, opening after a space or a parenthesis and closing after a letter or digit; the inch mark after 6'2 becomes a closing quote rather than a prime.")
            .Morph("TODO");

        yield return Probe("A-06", "Ordinals to superscript")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"1st 2nd 3rd 4th 11th 21st 101st 1th \")")
            .Toggle("Options.AutoFormatAsYouTypeReplaceOrdinals", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Hypotheses("The suffix of each correct ordinal is raised to superscript on the space; '1th' is left alone.")
            .Morph("TODO");

        yield return Probe("A-07", "Fractions to fraction characters")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"1/2 1/4 3/4 1/3 \")")
            .Toggle("Options.AutoFormatAsYouTypeReplaceFractions", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting)
            .Hypotheses("1/2, 1/4 and 3/4 become their single characters; 1/3 is left alone.")
            .Morph("TODO");

        yield return Probe("A-08", "Hyphens to dashes")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"{text}\")")
            .Axis("text", "a - b ", "a--b ", "a -- b ", "a-b ")
            .Toggle("Options.AutoFormatAsYouTypeReplaceSymbols", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting)
            .Hypotheses("A spaced hyphen becomes an en dash and a double hyphen an em dash, each when the word after it is finished by the space; 'a-b' is left alone.")
            .Morph("TODO");

        yield return Probe("A-09", "*bold* and _italic_ to real formatting")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"*bold* _it_ \")")
            .Toggle("Options.AutoFormatAsYouTypeReplacePlainTextEmphasis", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting)
            .Hypotheses("(a) the markers are removed and the words made bold and italic on the space after each; (b) left literal.")
            .Morph("TODO");

        yield return Probe("A-10", "URL to hyperlink: which key fires it")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"www.example.com\")", "{trigger}")
            .Axis("trigger", "Space", "Enter", "Tab", "type(\")\")", "type(\",\")", "type(\".\")")
            .Toggle("Options.AutoFormatAsYouTypeReplaceHyperlinks", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Hypotheses("Space, Enter and Tab fire the link; a punctuation mark typed right after the address (a) fires it and stays outside the link, (b) waits for the next delimiter.")
            .Morph("TODO");

        yield return Probe("A-10b", "URL to hyperlink: which forms of address are recognised")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"{text}\")")
            .Axis("text", "www.example.com ", "http://example.com ", "example.com ", "a@b.com ", "\\\\server\\share ")
            .Toggle("Options.AutoFormatAsYouTypeReplaceHyperlinks", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Hypotheses("The www. and http:// forms and a bare e-mail address become links (the address gets mailto:); a bare domain does not; a UNC path becomes a file link.")
            .Morph("TODO");

        yield return Probe("A-11", "Automatic bullets: leader, text, Enter, then each key that could leave the list")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"{leader}\")", "type(\"item\")", "Enter")
            .Axis("leader", "* ", "- ", "> ", "o ", "• ")
            .Toggle("Options.AutoFormatAsYouTypeApplyBulletedLists", "true", "false")
            .Tracked()
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Branch("backspace", S("Backspace"))
            .Branch("undo", S("undo"))
            .Branch("enter", S("Enter"))
            .Branch("tab", S("Tab"))
            .Branch("next", S("type(\"next\")"))
            .Hypotheses("(a) the paragraph becomes a bulleted item on Enter after the text, with the leader removed, and the next paragraph continues the list; (b) on the space after the leader. After Enter, Backspace and Enter on the empty item end the list, Tab demotes it, undo puts the leader back.")
            .Morph("TODO");

        yield return Probe("A-11b", "Automatic bullets inside a table cell")
            .In("table-2x2-empty")
            .Steps("select(C(1,1):0)", "type(\"* item\")", "Enter")
            .Toggle("Options.AutoFormatAsYouTypeApplyBulletedLists", "true", "false")
            .Tracked()
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Tables | Observe.Markup)
            .Hypotheses("The conversion fires in a cell as in the body.")
            .Morph("TODO");

        yield return Probe("A-11c", "Automatic bullets in the paragraph after a heading")
            .In("heading-body")
            .Steps("select(P1:end)", "Enter", "type(\"* x\")", "Enter")
            .Toggle("Options.AutoFormatAsYouTypeApplyBulletedLists", "true", "false")
            .Tracked()
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Hypotheses("The new paragraph (Normal, the heading's next style) is converted like any other; the heading is untouched.")
            .Morph("TODO");

        yield return Probe("A-12", "Automatic numbering: leader, text, Enter, then each key that could leave the list")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"{leader}\")", "type(\"item\")", "Enter")
            .Axis("leader", "1. ", "1) ", "(1) ", "a. ", "i. ", "1- ", "2. ")
            .Toggle("Options.AutoFormatAsYouTypeApplyNumberedLists", "true", "false")
            .Tracked()
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Branch("backspace", S("Backspace"))
            .Branch("undo", S("undo"))
            .Branch("enter", S("Enter"))
            .Branch("tab", S("Tab"))
            .Branch("next", S("type(\"next\")"))
            .Hypotheses("(a) each leader form makes a numbered list of that format on Enter, '2.' starting at 2 and '1-' not recognised; (b) conversion on the space after the leader; (c) the paragraph is kept as text and only the next paragraph is numbered.")
            .Morph("TODO");

        yield return Probe("A-12b", "Automatic numbering with Tab after the number")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"1.\")", "Tab", "type(\"item\")", "Enter")
            .Toggle("Options.AutoFormatAsYouTypeApplyNumberedLists", "true", "false")
            .Tracked()
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Hypotheses("A tab after the number is as good a leader as a space.")
            .Morph("TODO");

        yield return Probe("A-12c", "Automatic numbering after an existing list: continue or restart")
            .In("numbers-two-lists")
            .Steps("select(P3:end)", "Enter", "type(\"{text}\")", "Enter")
            .Axis("text", "3. next", "1. next")
            .Toggle("Options.AutoFormatAsYouTypeApplyNumberedLists", "true", "false")
            .Tracked()
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Hypotheses("(a) '3.' joins the list above and continues at 3, '1.' starts a new list; (b) both start a new list numbered from the typed value; (c) both join the list above.")
            .Morph("TODO");

        yield return Probe("A-13", "Border lines from three repeated characters")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"{leader}\")", "Enter")
            .Axis("leader", "---", "===", "___", "***", "~~~", "--")
            .Toggle("Options.AutoFormatAsYouTypeApplyBorders", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Branch("undo", S("undo"))
            .Hypotheses("Three or more of each character give P1 a bottom border of that style (single, double, thick, thick-thin, wavy) and the typed characters go; the empty P2 is kept or removed; '--' is too short and stays text.")
            .Morph("TODO");

        yield return Probe("A-14", "A table from +--+----+")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"+--+----+\")", "Enter")
            .Toggle("Options.AutoFormatAsYouTypeApplyTables", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Hypotheses("A one-row, two-column table whose column widths follow the hyphen counts, with the caret in the first cell.")
            .Morph("TODO");

        yield return Probe("A-15", "Built-in heading styles from a short line followed by two Enters")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "option(Options.AutoFormatAsYouTypeApplyHeadings=true)", "type(\"Title\")", "Enter", "Enter")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("'Title' is given Heading 1 on the second Enter; the paragraphs after stay Normal.")
            .Morph("TODO");

        yield return Probe("A-16", "Tab at the start of a paragraph: an indent or a tab character, and Backspace after it")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"text\")", "Home", "Tab")
            .Toggle("Options.TabIndentKey", "true", "false")
            .Tracked()
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Branch("backspace", S("Backspace"))
            .Hypotheses("With TabIndentKey a Tab before the first character sets a 0.5in first-line indent and Backspace removes it; without it a tab character is inserted and Backspace deletes that.")
            .Morph("TODO");

        yield return Probe("A-17", "Format the beginning of a list item like the one before it")
            .In("numbers")
            .Steps("select(P1:0-P1:3)", "Ctrl+B", "select(P1:end)", "type(\": text\")", "Enter", "type(\"Next: more\")")
            .Toggle("Options.AutoFormatAsYouTypeFormatListItemBeginning", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Lists)
            .Hypotheses("(a) 'Next:' is made bold to match 'One:' once the colon and the word after it are typed; (b) nothing is copied.")
            .Morph("TODO");

        yield return Probe("A-18", "AutoComplete tips for dates and salutations")
            .In("empty-paragraphs")
            .Steps("option(Application.DisplayAutoCompleteTips=true)", "select(P2:0)", "type(\"{text}\")", "Enter")
            .Axis("text", "Mond", "Octo", "Dear")
            .Tracked()
            .Observing(Observe.Default)
            .Notes("The harness turns DisplayAutoCompleteTips off at start, so the option step is what tests them on; a result read with the option still off records only that Enter made a new paragraph.")
            .Hypotheses("(a) Enter accepts the suggested 'Monday' and 'October' and the paragraph is completed rather than split; (b) no tip is offered and Enter splits the paragraph; 'Dear' has no built-in completion either way.")
            .Morph("TODO");

        yield return Probe("A-19", "Replacement entries and the key that fires them")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"{text}\")", "Space")
            .Axis("text", "(c)", "(r)", "(tm)", "-->", "<--", ":)", "teh", "i")
            .Toggle("AutoCorrect.ReplaceText", "true", "false")
            .Tracked()
            .Trace()
            .Observing(Observe.Default | Observe.Formatting)
            .Hypotheses("(a) the parenthesised entries are replaced on their closing ')' and the arrows on their final character, before any delimiter; (b) every entry waits for the space. The smileys are symbol-font entries and arrive as Wingdings characters.")
            .Morph("TODO");

        yield return Probe("A-20", "Undo right after a correction, twice")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"{text}\")", "undo", "undo")
            .Axis("text", "teh ", "(c) ", "1st ", "www.example.com ", "1/2 ")
            .Tracked()
            .Trace()
            .Observing(Observe.Default | Observe.Formatting | Observe.Lists)
            .Hypotheses("The first undo takes back the correction and leaves the typed text with its space; the second takes back the typing.")
            .Morph("TODO");

        yield return Probe("A-20b", "Undo right after an automatic bullet, twice")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"* x\")", "Enter", "undo", "undo")
            .Tracked()
            .Trace()
            .Observing(Observe.Default | Observe.Formatting | Observe.Lists)
            .Hypotheses("The first undo removes the bullet format and restores '* x' with the new paragraph kept; the second takes back the Enter.")
            .Morph("TODO");

        yield return Probe("A-21", "Backspace right after an automatic list")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"{text}\")", "Enter", "Backspace")
            .Axis("text", "* item", "1. item")
            .Tracked()
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Formatting)
            .Hypotheses("(a) Backspace on the new empty item removes its list format and keeps the paragraph, the first item staying in the list; (b) a plain Backspace that merges the paragraphs.")
            .Morph("TODO");

        yield return Probe("A-21b", "Backspace right after a text correction")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"{text}\")", "Backspace")
            .Axis("text", "hello. world ", "www.example.com ", "teh ")
            .Tracked()
            .Trace()
            .Observing(Observe.Default | Observe.Formatting)
            .Hypotheses("(a) Backspace after a correction reverses it and keeps the text (the capital reverts, the link is removed, 'the' goes back to 'teh'); (b) Backspace deletes the space like any character.")
            .Morph("TODO");

        yield return Probe("A-22", "Sentence capitalisation exceptions: abbreviations and initial-caps words")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"{text}\")")
            .Axis("text", "etc. next ", "e.g. next ", "IDs ")
            .Toggle("AutoCorrect.CorrectSentenceCaps", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting)
            .Hypotheses("'etc.' and 'e.g.' are on the first-letter exceptions list so 'next' stays lower case; 'IDs' is on the initial-caps exceptions list and is not changed to 'Ids'.")
            .Morph("TODO");

        yield return Probe("A-23", "AutoCorrect inside a hyperlink's text")
            .In("hyperlink")
            .Steps("select(P1:find(\"site\"))", "type(\"teh \")")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Story)
            .Hypotheses("The replacement fires inside the link and the new word takes the link's formatting.")
            .Morph("TODO");

        yield return Probe("A-23b", "AutoCorrect right after a field")
            .In("field-page")
            .Steps("select(P2:find(\" end.\"))", "type(\"teh \")")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Story)
            .Hypotheses("The replacement fires with the field result before it as if it were text.")
            .Morph("TODO");

        yield return Probe("A-23c", "AutoCorrect in a comment")
            .In("comment")
            .Steps("select(CMT1:P1:0)", "type(\"teh \")")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Story)
            .Hypotheses("The replacement fires in the comment story.")
            .Morph("TODO");

        yield return Probe("A-23d", "AutoCorrect in a header")
            .In("header-footer")
            .Steps("seek(header)", "select(HDR:P1:end)", "type(\" teh \")")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Story)
            .Hypotheses("The replacement fires in the header story.")
            .Morph("TODO");

        yield return Probe("A-23e", "AutoCorrect in a text box")
            .In("text-box")
            .Steps("select(TB1:P1:end)", "type(\" teh \")")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Story)
            .Hypotheses("The replacement fires in the text box story.")
            .Morph("TODO");

        yield return Probe("A-24", "A replacement under Track Changes: one insertion or a deletion and an insertion")
            .In("empty-paragraphs")
            .Steps("track(on)", "select(P2:0)", "type(\"teh \")")
            .Observing(Observe.Default | Observe.Revisions | Observe.Markup)
            .Hypotheses("(a) the corrected word is a single insertion of 'the ' since the typed letters were never saved; (b) 'teh' is marked deleted and 'the' inserted.")
            .Morph("TODO");

        yield return Probe("A-25", "Hyperlink autoformat on a text-only paste followed by Space")
            .In("empty-paragraphs")
            .Steps("clipboard(\"www.example.com\")", "select(P2:0)", "Ctrl+Shift+V", "Space")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting)
            .Hypotheses("(a) the space after a pasted address fires the link as it does after a typed one; (b) only typed addresses are recognised.")
            .Morph("TODO");

        yield return Probe("A-26", "An asterisk and a space typed before existing text")
            .In("plain")
            .Steps("select(P1:0)", "type(\"* \")")
            .Toggle("Options.AutoFormatAsYouTypeApplyBulletedLists", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Lists)
            .Hypotheses("(a) the paragraph is bulleted at once, since the text after the leader is already there; (b) nothing happens until Enter.")
            .Morph("TODO");

        // A-27 (F3 AutoText) is not probed: no building block name is known to exist in every Word installation,
        // and F3 on an unknown name reports an error rather than inserting anything.
    }
}
