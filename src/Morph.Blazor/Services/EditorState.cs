/// <summary>
/// Where the reader is and what the editor shows, as its commands are decided on (<see cref="EditorCommand"/>):
/// worked out once each time the ribbon is drawn, and again when a command is run, so the button, its key
/// and a menu that offers it all ask the one question of the same answer.
///
/// Only what is cheap to know goes in: what needs the file read (the caret's page setup, a picture's wrap)
/// is asked by the menu that shows it, while that menu is open.
/// </summary>
sealed record EditorState
{
    /// <summary>A Word document is open: the only kind the editor edits.</summary>
    public bool IsWord { get; init; }

    /// <summary>It can be written: the editor is not read-only, and the document's protection allows it.</summary>
    public bool CanWrite { get; init; }

    /// <summary>An edit is under way: the file is being written and laid out again.</summary>
    public bool Revising { get; init; }

    /// <summary>What most commands ask: a document that can be written, and no edit under way.</summary>
    public bool Editable => CanWrite &&
                            !Revising;

    /// <summary>The caret's paragraph is in a table.</summary>
    public bool InTable { get; init; }

    /// <summary>The caret's paragraph has a link in it.</summary>
    public bool InLink { get; init; }

    /// <summary>The list the caret's paragraph is in, if any.</summary>
    public ListKind List { get; init; }

    public bool Subscript { get; init; }

    public bool Superscript { get; init; }

    /// <summary>
    /// A picture is selected: a floating one framed on its page, or an inline one selected alone in the
    /// paragraph being typed.
    /// </summary>
    public bool PictureSelected { get; init; }

    /// <summary>The body has a table of contents.</summary>
    public bool HasContents { get; init; }

    public bool Tracking { get; init; }

    /// <summary>The document's protection tracks every change, so tracking cannot be turned off.</summary>
    public bool ForcesTracking { get; init; }

    public bool CanComment { get; init; }

    /// <summary>Tracked changes can be accepted and rejected here.</summary>
    public bool CanResolve { get; init; }

    /// <summary>The document has tracked changes.</summary>
    public bool HasChanges { get; init; }

    /// <summary>The host offers the review pane and its commands.</summary>
    public bool OffersReview { get; init; }

    /// <summary>Format Painter has formatting picked up, to paint the next text selected.</summary>
    public bool Painting { get; init; }

    public bool MarksShown { get; init; }

    public bool RulerShown { get; init; }

    public bool PagesShown { get; init; }

    public bool ReviewShown { get; init; }

    public bool FindShown { get; init; }

    public bool ReplaceShown { get; init; }

    public bool Presenting { get; init; }
}
