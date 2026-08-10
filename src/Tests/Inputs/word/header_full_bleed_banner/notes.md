Regression scenario for a header banner that bleeds off BOTH paper edges, and for the body
clearing a header taller than the top margin. Companion to `header_banner_table`, which isolates
the same banner shape at text width and short enough that the body never has to move.

Covers three former bugs, all found together in one render and none of them caught by any other
scenario:

1. **A band table ignored `w:tblInd`.** The banner is a one-column table of 12792 twips with
   `w:tblInd w:w="-1593"`, so against this document's 720-twip margins it starts 79.65pt off the
   left edge and ends level with the right edge of the A4 sheet. `Fragmenter.LayoutBand` laid it
   out at the band's own left edge, which inset the bar by the margin on one side and ran it off
   the paper on the other. The width itself was never the problem: the table is `tblLayout` fixed,
   and `CalculateColumnWidths` only squeezes an over-wide table back to the column when it is
   autofit.

2. **A header table's height was not reserved above the body.** `HeaderReservedTop` summed only
   the header's `ParagraphElement`s, so this header reserved its two lines of marking text and
   none of the 33pt banner below them. The first body heading ("Attachment 5A", a right-aligned
   Heading 1) landed 40pt high, inside the bar. `FooterBand` had always measured tables too.

3. **A table style's `w:tblBorders` did not inherit through `w:basedOn`.** `HouseOfRepsTable` and
   `SenateTable` are both based on `ChamberTable`, which carries top/bottom/insideH; each derived
   style's own `w:tblPr` is empty and adds only a `w:tblStylePr` firstRow fill. Reading just the
   leaf dropped all three rules while the coloured header band still painted, so the table read as
   deliberately borderless. Word draws them at y=508, 562 and 694 on page 1.

Also exercised, incidental but worth having covered:

- Two sibling table styles differing ONLY in their firstRow conditional fill — green
  (`HouseOfRepsTable`, page 1) and red (`SenateTable`, page 2) — over an identical inherited grid.
- A4 (`w:pgSz` 11907x16840, `w:code="9"`) with 720-twip margins all round, `w:header="425"`.
- An explicit `<w:br w:type="page"/>` between the two chamber tables.
- Distinct first/even/default headers AND footers with no `w:titlePg`, so page 1 takes the DEFAULT
  pair — the case where a first-page variant exists but must NOT be selected.
- A `PAGE`/`NUMPAGES` footer ("Page 1 of 2") below a repeated marking line.

Known gap, visible in the comparison: Word renders the header row bold, from the `w:b` in each
style's `w:tblStylePr/w:rPr`. Morph cascades only the run COLOUR from a conditional region — see
`src/todo.md` #10, which records why the bold half was reverted.

Provenance: derived from a public-sector parliamentary report generator's own test fixture, whose
data is already placeholder ("Bill 1", "Minister 1", "TBC"). The protective marking it carried in
its header, footer and `docProps/custom.xml` was replaced with the same equal-length placeholder
`nonstandard_main_part_name` uses, `SENSITIVE//EXAMPLE`. The empty bibliography data island under
`customXml/` was stripped by `DocumentCleaner`.
