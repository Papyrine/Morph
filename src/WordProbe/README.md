# WordProbe

Drives a visible Microsoft Word 365 with real keyboard and mouse input plus COM interop, runs the
scenario catalogue under `Scenarios/Families/`, and records what Word did in `Results/<family>.json`.
`src/word-behavior.md` is rendered from those results; its hand-written parts explain the method and the
notation, so start there.

The project is Windows-only and out of Release solution builds (see `src/Morph.slnx`), like RenderHelper.

```bash
dotnet run --project src/WordProbe -- smoke                 # ten go/no-go gates: geometry, keys, double-click,
                                                            # AutoFormat, undo names, a dialog, hot spots, options
dotnet run --project src/WordProbe -- fixtures [--force]    # build (or rebuild) Fixtures/*.docx over COM
dotnet run --project src/WordProbe -- describe [fixture...] # print fixtures in the notation, with positions
dotnet run --project src/WordProbe -- list [family]         # the catalogue; validates every step's DSL
dotnet run --project src/WordProbe -- run [family ...] [--scenario ID] [--force] [--max-minutes N] [--markup]
dotnet run --project src/WordProbe -- render                # src/word-behavior.md from Results/, no Word
dotnet run --project src/WordProbe -- restore-options       # put Word's options back after a crashed run
```

While `smoke`, `describe` or `run` is going, the keyboard and mouse belong to Word. When `run` sees a
modifier or a mouse button held, or another window take the foreground, it pauses, waits until the machine
has been idle for 10 s, and runs the case again; it gives up only after three losses on one case or 30
minutes paused in all. `smoke` and `describe` still abort. Caps Lock has to be off.

`run` records the Word build each case ran on, and a case counts as done only on the build running now
(one recorded before cases carried a build takes its family's). So a plain `run` resumes wherever a run
stopped, and after a Word update re-runs everything, with no `--force`. If Word itself crashes on a case,
the case is run once more in a new Word and recorded `word-crashed` if that one falls over too.

Progress is in Word's title bar (`WordProbe · tables 40/330 · 1240/4391 · ETA 14:05 · <case>`) and in
the log: each case line carries `[n/total]`, a progress line with the ETA comes every 25 cases, and each
family ends with the median and total of every phase of a case (open, its parts, options, steps,
observe, reset, close), which each case also records as `Phases`.

The run is not parallel, and cannot be: every case sends real input, and a desktop has one foreground
window. The time goes in fixed overhead around each case rather than in its steps. Writing back only the
options a case changed, and only the view settings a new window lacks, took a case from a median of
1535 ms to 995 ms with identical results (159 selection cases, 2026-10-27). Time a harness change on a
`--scenario` slice of 20-30 cases, not on whole families.

## Layout

| folder | what |
| --- | --- |
| `Session/` | starting, placing and quitting Word; the options snapshot; the COM message filter; the dialog watchdog; Win32 |
| `Input/` | `SendInput` keyboard and mouse, settling, the hot spots (selection bar, cell edge, column top, row bar, table handle) |
| `Dsl/` | positions (`P2:5`, `C(1,2):end`, `IMG1`, `HDR:P1:0`) and steps (`click(...)`, `Shift+Right x3`, `type("x")`, `select(...)`) |
| `Scenarios/` | the builder (`Probe(id, title).In(...).Steps(...).Axis(...).Branch(...).Toggle(...).Tracked().Trace()`), the catalogue, one file per family |
| `Fixtures/` | the fixture builder, the normaliser, the committed `.docx` files and `square.png` |
| `Observation/` | reading the selection, text, runs, tables, shapes, fields, revisions and the undo list |
| `Notation/` | the one-line-per-paragraph notation `src/word-behavior.md` uses |
| `Runner/` | case expansion (axes, branches, toggles, tracked), step execution, the run loop |
| `Results/` | per-family JSON (committed), `environment.json`, markup diffs, run logs (ignored) |
| `Rendering/` | the markdown renderer and the test-coverage scan |
| `Smoke/` | the smoke test |

## Adding a scenario

1. Add a `Probe(...)` to the family's class (or a new family: one class plus an entry in `Catalog.Families`
   and a marker pair in `src/word-behavior.md`). Ids are stable; a renamed scenario keeps its old id in
   `.Alias(...)`. Write the competing expectations in `.Hypotheses(...)` before running.
2. `dotnet run --project src/WordProbe -- list <family>` to check the DSL and the case count.
3. `dotnet run --project src/WordProbe -- run <family>`: only cases without a recorded result for the
   same definition run. The markdown is rendered when the family ends. `--scenario <id>` narrows the run
   to one scenario (or every case under an id prefix), `--force` re-runs what is recorded, and `--skip
   <id>` leaves a case out — the way round a case that takes Word's foreground away for good, such as
   the one that opens a hyperlink in the browser (F-02c/backspace~tracked).
4. Distil the rule into the family's `### Rules` section by hand and set the scenario's `.Morph(...)`.

## What the smoke test settled (2026-10-25)

- `Documents.Add(Template: <fixture>.docx)` opens a fixture as an unsaved new document.
- Undo entry names come from `CommandBars.FindControl(Id: 128, Visible: false)` (the hidden split dropdown
  on the legacy Standard bar; the visible QAT control with the same id is a plain button).
- Word's Insert Hyperlink dialog has window class `bosa_sdm_msword`.
- A click in the grey area left of the page acts as the selection bar; the harness parks the pointer to
  the right of the page and activates Word, when it must, with a click on the empty ribbon tab row.
- A single selected cell reports `Selection.Type` as `Column`.
- `Range.Text` leaves out floating anchors, content control edges and some field marks, and answers for a
  lone space with a neighbour's formatting; the observer reads those positions one at a time and shows
  such a space with the run before it.

## What the first full run settled (2026-10-25)

- Reading a list paragraph's `ListFormat` over COM leaves a "Bullets and Numbering" entry on top of the
  undo list, so the undo names are read before anything else touches the document.
- The undo control answers nothing for stretches of a run (the undo family's own cases came back with
  depths only, the lists family before it with names); the depth fallback is per read, never sticky.
- A key can change an option the case never named: Insert toggles `Options.Overtype` and the next case
  ran in overtype until the runner started re-applying every recorded option after each case.
- After Ctrl+Alt+M the comment card holds focus and `Document.TrackRevisions` is refused ("comment card is
  selected in pane"); the observer notes the refusal and reads on. In other runs the card did not take the
  keys at all. Comments need a probe that drives the Comments pane on purpose.
- A recorded dialog carried its window handle into the JSON and crashed the save; `DialogInfo.Window` is
  `[JsonIgnore]`.
- Tracked moves (`wdRevisionMovedFrom` / `MovedTo`) and formatting revisions are their own revision types
  and get their own brackets (`{<…<}` `{>…>}` `{~…~}`), so a drag is not mistaken for a deletion plus an
  insertion.
- Run marks carry size and face only since the second build (`[b 14 Georgia:…]`); results observed before
  that have the letters only, which is why the formatting-key scenarios were re-run.
- A browser notification stole the foreground once (`foreground-lost`); the run resumes the case on the
  next `run`.
- `Options.PasteFormatWithinDocument` refuses 3 (`wdUseDestinationStylesRecovery`); the toggle offers 0,
  1 and 2.

## What the second run settled (2026-10-25, the open questions)

- The comment card opens asynchronously: a letter sent 40 ms after Ctrl+Alt+M lands in the body. The
  comment rows wait 1.5 s. And `Trace` is poison there: reading `Selection` over COM between steps takes
  focus off the card, so a traced Ctrl+Enter never posts and a traced Enter splits the body paragraph.
  Comment scenarios are not traced; the observation at the end reads the posted comment through
  `Document.Comments`, and `Esc` on a draft leaves no comment at all.
- A skipped case stays pending: `run fields` without `--skip F-02c/backspace~tracked` starts with it, and
  the browser it opens aborts the run (the follow arrives late, so the foreground loss is charged to the
  NEXT case). Hyperlink probes use `hyperlink-internal`, whose link points at a bookmark, so following it
  is a caret move the observer can read.
- `DialogInfo.Text` reads a dialog's child controls with `WM_GETTEXT`; it answers nothing for Word's own
  `bosa_sdm_msword` dialogs and `NUIDialog` message boxes, which draw their controls themselves.

## What the ribbon run settled (2026-10-25)

- `Observe.Ribbon` reads `CommandBars.GetEnabledMso` / `GetPressedMso` for the idMsos in `RibbonReader.Controls`,
  each mapped to the `EditorCommand` it stands for. It is read straight after the undo list, because the
  `ListFormat` read that follows adds an undo entry and would enable Undo.
- `GetVisibleMso` answers true for a contextual tab wherever the caret is, so whether a tab shows is not
  readable this way. Galleries and split buttons throw on `GetPressedMso`.
- The DPI guard asks Windows for the process's awareness. It no longer requires a screen wider than 2000px, which
  only the 3440px display the catalogue was first recorded on satisfied.

## What the keep run settled (2026-10-25)

- `ribbon(idMso)` presses a ribbon control through `CommandBars.ExecuteMso`, late bound as `RibbonReader` reads
  the ribbon, and `dialog(key, ok)` accepts a dialog with Enter where `dialog(key)` dismisses it with Esc.
- Formatting a selection leaves it selected and its active end where it was, whichever way the change is made;
  `keep.json` holds the 51 cases.

Digests of a family's JSON for distilling rules were made with two scratch scripts (one line per
paragraph with the caret or a selection, plus the undo part of the summary); `Results/<family>.json` holds
everything they printed.
