Pins paragraph spacing counted in lines (`w:spacing/@w:beforeLines`, `@w:afterLines`) off a
document grid, beside the line rules for reference.

The paragraphs of `document_grid_lines` under a bare `<w:docGrid w:linePitch="360"/>` with no
`@w:type`, which Word writes into most documents as a stored default and lays nothing out on.

* A line unit is 12pt off a grid: `beforeLines="100"` adds 12pt, `afterLines="50"` 6pt.
* A positive line unit outranks the twips value beside it: `before="480" beforeLines="50"` adds
  6pt, not 24.
* `0` is ignored rather than zeroing the spacing: `afterLines="0" after="480"` adds 24pt.
  agendas-minutes/17's style declares `afterLines="0"` beside `after="40"`, and keeps its 2pt.
* The untyped `linePitch="360"` changes no line: a 12pt Arial line stays 13.8pt.
