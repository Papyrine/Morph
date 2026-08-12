namespace Morph;

/// <summary>
/// Shows a failed conversion to the user, optionally inviting a bug report through a pre-filled GitHub
/// issue link (see <see cref="IssueLauncher"/>).
/// </summary>
public partial class ErrorPanel
{
    /// <summary>The user-facing description of what went wrong.</summary>
    [Parameter]
    public string Message { get; set; } = "";

    // When set, the error came from an unexpected exception rather than user input, so we invite a bug
    // report pre-filled at this GitHub "new issue" URL.
    /// <summary>A pre-filled "new issue" URL to offer as a report link, or null to show none.</summary>
    [Parameter]
    public string? IssueUrl { get; set; }
}
