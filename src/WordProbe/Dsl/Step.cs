// A scenario's steps, as the scenarios write them and as the markdown prints them:
//
//   keys    Ctrl+Shift+Right x3 · Enter · F8 x2 · Alt+Shift+Up · Esc · Ins · Del · PgUp
//   text    type("abc")                       no \n or \t inside: Enter, Tab and Shift+Enter are their own steps
//   mouse   click(P1:5) · click(P1:5, side=right) · dblclick(P2:9) · tripleclick(P1:0) · shift+click(P3:4)
//           ctrl+click(P1:2) · rightclick(P1:5) · drag(P1:2→P2:4) · alt+drag(P1:2→P3:6) · ctrl+drag(P1:0-P1:5→P3:0)
//           click(selectionbar P1) · dblclick(selectionbar P1) · ctrl+click(selectionbar P1)
//           click(cellLeftEdge C(1,1)) · click(columnTop C(1,2)) · click(rowBar R1) · click(tableHandle T1)
//           click(IMG1) · click(SHP1) · click(below P3) · hover(P1:3)
//   other   select(P1:2-7)                    COM Range.Select, set-up only
//           option(Options.Overtype=true)     mid-scenario option change
//           track(on) / track(off)            Document.TrackRevisions
//           seek(header|footer|main|footnotes) View.SeekView
//           view(all|simple|none)             revisions markup
//           wait(300)
//           dialog(Ctrl+K)                    the inner key is expected to open a dialog, which is recorded and dismissed
//           dialog(Ctrl+D, ok)                the same, the dialog accepted with Enter rather than dismissed
//           ribbon(Bold)                      a ribbon control pressed, CommandBars.ExecuteMso as a click runs it
//           clipboard("text")                 put plain text on the clipboard
//           undo · redo                       Ctrl+Z / Ctrl+Y as keys, kept as words for readability
enum MouseAction
{
    Click,
    DoubleClick,
    TripleClick,
    RightClick,
    Drag,
    Hover
}

enum Side
{
    Left,
    Right,
    Centre
}

enum Hotspot
{
    None,
    SelectionBar,
    CellLeftEdge,
    ColumnTop,
    RowBar,
    TableHandle
}

abstract record Step(string Text)
{
    public override string ToString() =>
        Text;
}

record KeyStep(string Text, KeyChord Chord, int Repeat) : Step(Text);

record TypeStep(string Text, string Characters) : Step(Text);

record MouseStep(string Text, MouseAction Action, Position? At, PositionRange? From, Position? To, Modifiers Modifiers, Side Side, Hotspot Hotspot) : Step(Text);

record SelectStep(string Text, PositionRange Range) : Step(Text);

record OptionStep(string Text, string Member, string Value) : Step(Text);

record TrackStep(string Text, bool On) : Step(Text);

record SeekStep(string Text, Word.WdSeekView View) : Step(Text);

record ViewStep(string Text, Word.WdRevisionsMarkup Markup) : Step(Text);

record WaitStep(string Text, int Milliseconds) : Step(Text);

record ExpectDialogStep(string Text, Step Inner, bool Accept = false) : Step(Text);

record RibbonStep(string Text, string IdMso) : Step(Text);

record ClipboardStep(string Text, string Content) : Step(Text);

static class StepParser
{
    static readonly Regex call = new(@"^(?<name>[a-z+]+)\((?<args>.*)\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex repeat = new(@"^(?<chord>.+?)\s+x(?<n>\d+)$", RegexOptions.Compiled);

    public static Step Parse(string text)
    {
        text = text.Trim();
        var match = call.Match(text);
        if (!match.Success)
        {
            var count = 1;
            var chordText = text;
            var repeatMatch = repeat.Match(text);
            if (repeatMatch.Success)
            {
                chordText = repeatMatch.Groups["chord"].Value;
                count = int.Parse(repeatMatch.Groups["n"].Value, CultureInfo.InvariantCulture);
            }

            chordText = chordText switch
            {
                "undo" => "Ctrl+Z",
                "redo" => "Ctrl+Y",
                _ => chordText
            };
            return new KeyStep(text, KeyChord.Parse(chordText), count);
        }

        var name = match.Groups["name"].Value.ToLowerInvariant();
        var arguments = match.Groups["args"].Value.Trim();
        switch (name)
        {
            case "type":
                return new TypeStep(text, Unquote(arguments));
            case "select":
                return new SelectStep(text, PositionParser.ParseRange(arguments));
            case "option":
            {
                var equals = arguments.IndexOf('=');
                return new OptionStep(text, arguments[..equals].Trim(), arguments[(equals + 1)..].Trim());
            }
            case "track":
                return new TrackStep(text, arguments is "on" or "true");
            case "seek":
                return new SeekStep(text, arguments.ToLowerInvariant() switch
                {
                    "header" => Word.WdSeekView.wdSeekCurrentPageHeader,
                    "footer" => Word.WdSeekView.wdSeekCurrentPageFooter,
                    "footnotes" => Word.WdSeekView.wdSeekFootnotes,
                    "endnotes" => Word.WdSeekView.wdSeekEndnotes,
                    "main" => Word.WdSeekView.wdSeekMainDocument,
                    _ => throw new ArgumentException($"Unknown seek target in '{text}'.")
                });
            case "view":
                return new ViewStep(text, arguments.ToLowerInvariant() switch
                {
                    "all" => Word.WdRevisionsMarkup.wdRevisionsMarkupAll,
                    "simple" => Word.WdRevisionsMarkup.wdRevisionsMarkupSimple,
                    "none" => Word.WdRevisionsMarkup.wdRevisionsMarkupNone,
                    _ => throw new ArgumentException($"Unknown markup view in '{text}'.")
                });
            case "wait":
                return new WaitStep(text, int.Parse(arguments, CultureInfo.InvariantCulture));
            case "dialog":
                if (arguments.EndsWith(", ok", StringComparison.OrdinalIgnoreCase))
                {
                    return new ExpectDialogStep(text, Parse(arguments[..^4]), Accept: true);
                }

                return new ExpectDialogStep(text, Parse(arguments));
            case "ribbon":
                return new RibbonStep(text, arguments);
            case "clipboard":
                return new ClipboardStep(text, Unquote(arguments));
        }

        return ParseMouse(text, name, arguments);
    }

    static Step ParseMouse(string text, string name, string arguments)
    {
        var modifiers = Modifiers.None;
        var action = name;
        while (true)
        {
            if (action.StartsWith("shift+", StringComparison.Ordinal))
            {
                modifiers |= Modifiers.Shift;
                action = action[6..];
            }
            else if (action.StartsWith("ctrl+", StringComparison.Ordinal))
            {
                modifiers |= Modifiers.Ctrl;
                action = action[5..];
            }
            else if (action.StartsWith("alt+", StringComparison.Ordinal))
            {
                modifiers |= Modifiers.Alt;
                action = action[4..];
            }
            else
            {
                break;
            }
        }

        var mouseAction = action switch
        {
            "click" => MouseAction.Click,
            "dblclick" => MouseAction.DoubleClick,
            "tripleclick" => MouseAction.TripleClick,
            "rightclick" => MouseAction.RightClick,
            "drag" => MouseAction.Drag,
            "hover" => MouseAction.Hover,
            _ => throw new ArgumentException($"Unknown step '{text}'.")
        };

        if (mouseAction == MouseAction.Drag)
        {
            var arrow = arguments.IndexOf('→');
            if (arrow < 0)
            {
                arrow = arguments.IndexOf("->", StringComparison.Ordinal);
                if (arrow < 0)
                {
                    throw new ArgumentException($"drag needs 'from→to': '{text}'.");
                }

                var fromRange = PositionParser.ParseRange(arguments[..arrow]);
                var toPosition = PositionParser.Parse(arguments[(arrow + 2)..]);
                return new MouseStep(text, mouseAction, null, fromRange, toPosition, modifiers, Side.Centre, Hotspot.None);
            }

            var from = PositionParser.ParseRange(arguments[..arrow]);
            var to = PositionParser.Parse(arguments[(arrow + 1)..]);
            return new MouseStep(text, mouseAction, null, from, to, modifiers, Side.Centre, Hotspot.None);
        }

        var side = Side.Centre;
        var parts = arguments.Split(',');
        var target = arguments;
        // "P1:5, side=right" — the side option is the last comma part; a cell's "C(1,2)" has its own comma.
        var last = parts[^1].Trim();
        if (last.StartsWith("side=", StringComparison.OrdinalIgnoreCase))
        {
            side = last[5..].ToLowerInvariant() switch
            {
                "left" or "l" => Side.Left,
                "right" or "r" => Side.Right,
                _ => Side.Centre
            };
            target = arguments[..arguments.LastIndexOf(',')].Trim();
        }

        var hotspot = Hotspot.None;
        var space = target.IndexOf(' ');
        if (space > 0)
        {
            var head = target[..space];
            var known = head.ToLowerInvariant() switch
            {
                "selectionbar" => Hotspot.SelectionBar,
                "cellleftedge" => Hotspot.CellLeftEdge,
                "columntop" => Hotspot.ColumnTop,
                "rowbar" => Hotspot.RowBar,
                "tablehandle" => Hotspot.TableHandle,
                _ => Hotspot.None
            };
            if (known != Hotspot.None)
            {
                hotspot = known;
                target = target[(space + 1)..].Trim();
            }
        }

        return new MouseStep(text, mouseAction, PositionParser.Parse(target), null, null, modifiers, side, hotspot);
    }

    static string Unquote(string arguments)
    {
        arguments = arguments.Trim();
        if (arguments is ['"', _, ..] &&
            arguments[^1] == '"')
        {
            arguments = arguments[1..^1];
        }

        return arguments.Replace("\\\"", "\"");
    }
}
