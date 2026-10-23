/// <summary>
/// One page as SVG (<see cref="SvgPainter"/>): the markup, whose <c>&lt;image data-i="n"&gt;</c> elements
/// stand for <see cref="Images"/>[n], and what on the page the markup leaves out.
/// </summary>
sealed record SvgPage(string Markup, IReadOnlyList<byte[]> Images, IReadOnlySet<string> Unsupported)
{
    public bool Complete => Unsupported.Count == 0;
}