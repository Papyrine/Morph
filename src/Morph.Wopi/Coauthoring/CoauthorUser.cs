/// <summary>Who a token is for, and what it may do, as the host's CheckFileInfo says.</summary>
sealed record CoauthorUser(string Id, string Name, bool CanWrite)
{
    public static CoauthorUser Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var id = String(root, "UserId") ?? "";
        var name = String(root, "UserFriendlyName") ?? id;
        var canWrite = Boolean(root, "UserCanWrite") &&
                       !Boolean(root, "ReadOnly") &&
                       Boolean(root, "SupportsLocks") &&
                       Boolean(root, "SupportsUpdate");
        return new(id, name, canWrite);
    }

    static string? String(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    static bool Boolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.True;
}
