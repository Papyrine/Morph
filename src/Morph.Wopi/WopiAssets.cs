using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace Morph;

/// <summary>
/// Morph.Blazor's static web assets under the editor's base path. An application serves the assets of every
/// library it references at the root, <c>/_content/Morph.Blazor/...</c>, while a WebAssembly app served
/// under a path asks for them under its base: <c>/morph/_content/Morph.Blazor/morph.css</c> for an app at
/// <c>/morph/</c>, from its page's stylesheet link, its script imports and its fetches of the fonts and
/// samples. These endpoints answer there with the files the web root has at the root.
/// </summary>
static class WopiAssets
{
    public const string ContentRoot = "_content/Morph.Blazor";

    /// <summary>A file every build of the package has; its presence says the assets are in the web root.</summary>
    public const string StyleSheet = ContentRoot + "/morph.css";

    static readonly FileExtensionContentTypeProvider contentTypes = new();

    public static IResult Handle(string path, IWebHostEnvironment environment)
    {
        // A segment that leaves the package's folder names no file of its own. The file providers refuse
        // it as well; this keeps the refusal the same whichever of them answers.
        if (path.Split('/').Any(_ => _ == ".."))
        {
            return Results.NotFound();
        }

        var file = environment.WebRootFileProvider.GetFileInfo($"{ContentRoot}/{path}");
        if (!file.Exists || file.IsDirectory)
        {
            return Results.NotFound();
        }

        if (!contentTypes.TryGetContentType(path, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        // Validated on every request, as the host serves the file at the root: a new build of the app is
        // seen at once, and an unchanged file costs one round trip.
        var tag = new EntityTagHeaderValue($"\"{file.Length:x}-{file.LastModified.UtcTicks:x}\"");
        return Results.File(file.CreateReadStream(), contentType, lastModified: file.LastModified, entityTag: tag);
    }
}
