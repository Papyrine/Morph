/// <summary>
/// The discovery a WOPI host reads to open files in Morph ([MS-WOPI] 3.1.5.1): what Morph opens, at which URL,
/// and the keys its requests are signed with.
/// </summary>
static class WopiDiscoveryXml
{
    // A Word document is viewed and edited; a workbook and a deck, which the editor only reads, are viewed.
    static (string App, string Extension, string[] Actions)[] apps =
    [
        ("Word", "docx", ["view", "edit"]),
        ("Excel", "xlsx", ["view"]),
        ("PowerPoint", "pptx", ["view"])
    ];

    public static IResult Handle(HttpRequest request, IOptions<MorphWopiOptions> options, WopiProofs proofs)
    {
        var baseUrl = BaseUrl(request, options.Value);
        return Results.Content(Build(baseUrl, proofs).ToString(SaveOptions.DisableFormatting), "text/xml", Encoding.UTF8);
    }

    public static string BaseUrl(HttpRequest request, MorphWopiOptions options)
    {
        if (options.BaseUrl is { } baseUrl)
        {
            return baseUrl.TrimEnd('/');
        }

        return $"{request.Scheme}://{request.Host}{request.PathBase}";
    }

    public static XDocument Build(string baseUrl, WopiProofs proofs)
    {
        var zone = "external-http";
        if (baseUrl.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            zone = "external-https";
        }

        var netZone = new XElement("net-zone", new XAttribute("name", zone));
        foreach (var (app, extension, actions) in apps)
        {
            var element = new XElement("app", new XAttribute("name", app));
            foreach (var action in actions)
            {
                var each = new XElement(
                    "action",
                    new XAttribute("name", action),
                    new XAttribute("ext", extension),
                    new XAttribute("urlsrc", $"{baseUrl}{MorphWopiExtensions.ActionPath}/{action}?<ui=UI_LLCC&>"));
                // the editor locks the file while it is open, and saves it over itself
                if (action == "edit")
                {
                    each.SetAttributeValue("requires", "locks,update");
                }

                element.Add(each);
            }

            netZone.Add(element);
        }

        return new(new XElement("wopi-discovery", netZone, proofs.Element()));
    }
}
