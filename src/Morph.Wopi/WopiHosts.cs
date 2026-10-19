/// <summary>
/// Which WOPISrc Morph follows: an http or https URL on one of <see cref="MorphWopiOptions.TrustedHosts"/>,
/// so that neither the page nor the proxy can be pointed at any other server.
/// </summary>
static class WopiHosts
{
    public static Uri? Trusted(string? source, MorphWopiOptions options)
    {
        if (string.IsNullOrEmpty(source) ||
            !Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length > 0 ||
            uri.Fragment.Length > 0)
        {
            return null;
        }

        var origin = uri.GetLeftPart(UriPartial.Authority);
        if (options.TrustedHosts.Any(_ => string.Equals(_.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase)))
        {
            return uri;
        }

        return null;
    }

    /// <summary>
    /// The Content-Security-Policy that lets only the trusted hosts frame the editor.
    /// </summary>
    public static string FrameAncestors(MorphWopiOptions options)
    {
        var origins = options.TrustedHosts.Select(_ => _.TrimEnd('/'));
        return $"frame-ancestors 'self' {string.Join(' ', origins)}".TrimEnd();
    }
}
