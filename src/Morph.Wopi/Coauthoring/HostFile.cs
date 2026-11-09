/// <summary>
/// The file as the host has it, as CheckFileInfo says ([MS-WOPI] 3.3.5.1.1): its version, which changes
/// with every change to the file, and when it was last changed, which a host for Collabora Online checks a
/// save against (X-COOL-WOPI-Timestamp). Either may be missing.
/// </summary>
sealed record HostFile(string? Version, string? LastModifiedTime)
{
    public static HostFile Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new(String(root, "Version"), String(root, "LastModifiedTime"));
    }

    static string? String(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            value.GetString() is {Length: > 0} text)
        {
            return text;
        }

        return null;
    }
}
