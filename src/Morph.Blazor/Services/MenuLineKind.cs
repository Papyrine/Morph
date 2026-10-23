/// <summary>What a <see cref="MenuLine"/> is.</summary>
enum MenuLineKind
{
    /// <summary>A command or a choice, or with lines of its own a submenu.</summary>
    Item,

    /// <summary>A heading over the lines after it, which is not chosen.</summary>
    Heading,

    /// <summary>A line between groups of lines.</summary>
    Separator
}
