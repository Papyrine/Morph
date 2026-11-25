namespace Morph;

/// <summary>
/// The request the browser sends as the editor's page goes away (<see cref="IWopiConnection.LeaveAsync"/>): a
/// POST to <paramref name="Url"/>, with <see cref="Fields"/> as a form. Without <see cref="Headers"/> it is a
/// beacon, which can carry no headers; with them it is a fetch kept alive past the page, for a host that
/// takes a write only with a header of its own, such as a request digest.
/// </summary>
/// <param name="Url">Where the request goes: the page's own origin, or one that allows it.</param>
public sealed record WopiLeave(string Url)
{
    /// <summary>The form the request posts; none when empty, for a fetch with headers.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();

    /// <summary>Headers the request carries; with any, it is sent as a fetch rather than a beacon.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
}
