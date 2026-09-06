/// <summary>
/// A picture's outline (<c>pic:spPr/a:ln</c>): Word's "Simple Frame" picture styles are a wide
/// solid line, usually white, stroked around the picture. Word draws it entirely OUTSIDE the
/// picture's extent, whatever <c>a:ln/@algn</c> says — <c>_probe_picln2</c> (2026-09-06): a 12pt
/// line on a 150×112.5pt picture strokes the band from the picture edge to 12pt beyond it for
/// <c>ctr</c> and <c>in</c> alike, and a 24pt line the band to 24pt beyond. The layout room it
/// needs comes from <see cref="ImageEffectExtent"/>, not from the line. newsletters/01's photos carry
/// a 7pt white line on every picture, the frame the engine had been dropping.
/// </summary>
sealed record ImageOutline(double WidthPoints, string ColorHex, double Alpha);
