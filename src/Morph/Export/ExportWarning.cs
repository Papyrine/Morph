namespace Morph;

/// <summary>
/// A notice that a feature in the source document was either dropped or degraded when producing
/// the chosen output format. Delivered to <see cref="ExportOptions.OnWarning"/>.
/// </summary>
/// <param name="Kind">Category of the loss (use for filtering / counting).</param>
/// <param name="Message">Human-readable detail.</param>
public sealed record ExportWarning(WarningKind Kind, string Message);
