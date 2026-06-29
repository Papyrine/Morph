/// <summary>
/// Horizontal alignment of a text frame relative to its <see cref="HorizontalAnchor"/>
/// (<c>w:framePr/@w:xAlign</c>). <see cref="None"/> means the frame is positioned by an
/// explicit <c>w:x</c> offset instead.
/// </summary>
enum FrameHorizontalAlignment
{
    None,
    Left,
    Center,
    Right
}
