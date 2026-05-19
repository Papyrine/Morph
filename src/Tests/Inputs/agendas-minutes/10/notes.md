### Top-right flower silhouette is dropped (no custGeom path renderer)

The decorative pink leaf cluster in the top-right is a `wsp` with `<a:custGeom>` containing 2442 cubic Bézier segments and a solid fill. Without a path renderer, the only options were drawing the bounding rectangle (a large green rectangle covering the artwork — see git history before commit) or skipping the shape. We chose to skip when `custGeom` has > 50 beziers (heuristic in `Morph.OpenXml/Parsing/DocumentParser.cs:ParseSolidFillShape`). Simple polygon backgrounds and accent strips have ≤ 20 beziers and still render as bounding-box rectangles.

To fully render this scenario, parse `<a:pathLst>` into an actual `SKPath` / ImageSharp `IPath` and fill that.
