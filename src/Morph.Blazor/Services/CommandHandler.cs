/// <summary>What carries an <see cref="EditorCommand"/> out when its button is pressed.</summary>
enum CommandHandler
{
    /// <summary>.NET, from the press.</summary>
    Component,

    /// <summary>
    /// The editing script (<c>data-edit-command</c> in morph-viewer.js), inside the press itself: the
    /// paragraph open for typing takes it with no round trip, and a browser lets a page use the clipboard
    /// only from a user's gesture. With no paragraph open, the script hands the name to .NET.
    /// </summary>
    Script,

    /// <summary>The viewer's script (<c>data-viewer-action</c>), as fullscreen is granted only to a gesture.</summary>
    Viewer
}
