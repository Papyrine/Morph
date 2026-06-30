/// <summary>
/// Vertical alignment of a text frame relative to its <see cref="VerticalAnchor"/>
/// (<c>w:framePr/@w:yAlign</c>). <see cref="Inline"/> keeps the frame at the position it would
/// occupy in normal flow; <see cref="None"/> means the frame is positioned by an explicit
/// <c>w:y</c> offset instead.
/// </summary>
enum FrameVerticalAlignment
{
    None,
    Top,
    Center,
    Bottom,
    Inline
}
