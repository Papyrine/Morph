/// <summary>
/// A formula that cannot be read. An <see cref="InvalidOperationException"/>, as the editor's other
/// refusals are, so the editor shows its message rather than reporting a fault.
/// </summary>
sealed class FormulaSyntaxException(string message, int position) :
    InvalidOperationException($"There's a problem with this formula: {message}")
{
    /// <summary>Where in the formula's text the problem was found.</summary>
    public int Position { get; } = position;
}
