Pins the document grid (`w:docGrid w:type="lines"`) at an amplified 36pt pitch (`linePitch="720"`).

Cloned from Word's own blank document so the compatibility mode is modern. Every paragraph is
one line of Arial carrying a label, so each one's height is the step from its baseline to the
next, read from the XPS. The same document was also rendered at a 25pt pitch and as
`linesAndChars`; those numbers are recorded here.

| label | paragraph | 36pt grid | 25pt grid | no grid |
| --- | --- | --- | --- | --- |
| A | 12pt | 36 | 25 | 13.8 |
| B | 30pt | 36 | 50 | 34.4 |
| C | 40pt | 72 | 50 | 46.0 |
| D | 72pt | 108 | 100 | 82.8 |
| E | 12pt, `snapToGrid="0"` | 13.8 | 13.8 | 13.8 |
| F | `line="360" auto` | 54 | 37.5 | 20.7 |
| G | `line="480" auto` | 72 | | 27.6 |
| H | `line="400" exact` | 20 | 20 | 20 |
| I | `line="1000" atLeast` | 50 | 50 | 50 |
| J | `before="240"` | 36 + 12 | 25 + 12 | 13.8 + 12 |
| K | `beforeLines="100"` | 36 + 36 | 25 + 25 | 13.8 + 12 |
| L | `afterLines="50"` | 36 + 18 | 25 + 12.5 | 13.8 + 6 |
| M | `afterLines="0" after="480"` | 36 + 24 | 25 + 24 | 13.8 + 24 |
| O | `before="480" beforeLines="50"` | 36 + 18 | 25 + 12.5 | 13.8 + 6 |

The laws:

* An auto line's natural height rounds UP to whole grid pitches, then takes its multiple, and the
  text is centred in the box by its natural height: every baseline lands within 0.15pt of the
  natural ascent plus half the box's slack.
* Exact and at-least lines are laid out as off the grid, and so is a `snapToGrid="0"` paragraph.
* `before` / `after` in twips are not rounded to the grid.
* A line unit (`beforeLines` / `afterLines`, hundredths of a line) is the grid pitch, or 12pt off a
  grid. A positive one outranks the twips value beside it; `0` is ignored.
* The grid sets heights only: lines stack from wherever the block before them ended, not from
  grid lines fixed on the page (`document_grid_table`).
* `linesAndChars` lays lines out exactly as `lines` does — every baseline identical.

Before this fixture Morph parsed the pitch and laid nothing out on it.
