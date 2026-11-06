namespace Morph;

/// <summary>
/// Makes an ASP.NET Core application a WOPI client that opens files in Morph's editor: a WOPI host (one
/// built on OfficeHost, or any other that takes a client's discovery) reads the discovery, opens a file
/// by posting its access token to the action URL in a frame, and the editor gets, locks and saves the file
/// through the host's WOPI endpoints, every request signed with a proof key.
///
/// The editor itself is a Blazor WebAssembly app whose root component is Morph.Blazor's <c>MorphWopi</c>,
/// served from the same application: the action URL answers with its page (<see cref="MorphWopiOptions.Page"/>).
/// </summary>
public static class MorphWopiExtensions
{
    internal const string ActionPath = "/morph/wopi";
    internal const string ProxyPath = "/morph/wopi/proxy";

    /// <summary>
    /// Registers what the WOPI endpoints of <see cref="MapMorphWopi"/> need.
    /// </summary>
    public static IServiceCollection AddMorphWopi(this IServiceCollection services, Action<MorphWopiOptions>? configure = null)
    {
        var options = services.AddOptions<MorphWopiOptions>();
        if (configure != null)
        {
            options.Configure(configure);
        }

        // A host answers WOPI itself: a redirect would take the token somewhere else.
        services.AddHttpClient(WopiProxy.ClientName)
            .ConfigurePrimaryHttpMessageHandler(
                () => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false
                });
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<WopiProofs>();
        return services;
    }

    /// <summary>
    /// Maps the discovery at <c>/hosting/discovery</c>, where the WOPI hosts are pointed; the action URLs
    /// <c>/morph/wopi/view</c> and <c>/morph/wopi/edit</c>, which answer with the editor's page; and
    /// <c>/morph/wopi/proxy/{operation}</c>, which the editor calls the host through.
    /// </summary>
    public static IEndpointRouteBuilder MapMorphWopi(this IEndpointRouteBuilder endpoints)
    {
        var page = endpoints.ServiceProvider.GetRequiredService<IOptions<MorphWopiOptions>>().Value.Page;
        AddWebRootAssets(endpoints.ServiceProvider, page);
        // The page's folder is the editor app's base: an app at /morph/ has its page at morph/index.html.
        // The app asks for Morph.Blazor's assets under its base, where the application does not serve them.
        var folder = Path.GetDirectoryName(page)?.Replace('\\', '/').Trim('/');
        if (!string.IsNullOrEmpty(folder))
        {
            endpoints.MapMorphAssets($"/{folder}");
        }

        endpoints.MapGet("/hosting/discovery", WopiDiscoveryXml.Handle);
        foreach (var action in (string[]) ["view", "edit"])
        {
            endpoints.MapMethods(
                    $"{ActionPath}/{action}",
                    [HttpMethods.Get, HttpMethods.Post],
                    (HttpContext context, IOptions<MorphWopiOptions> options, IWebHostEnvironment environment) =>
                        WopiPage.Handle(context, action, options, environment))
                .DisableAntiforgery();
        }

        endpoints.MapPost($"{ProxyPath}/{{operation}}", WopiProxy.Handle)
            .DisableAntiforgery();
        return endpoints;
    }

    /// <summary>
    /// Serves Morph.Blazor's static web assets under <paramref name="basePath"/>, for a WebAssembly app
    /// served there: one whose <c>StaticWebAssetBasePath</c> and <c>&lt;base href&gt;</c> are it. An
    /// application serves the assets of every library it references at the root,
    /// <c>/_content/Morph.Blazor/...</c>, while the app asks for them under its base,
    /// <c>/morph/_content/Morph.Blazor/...</c> for an app at <c>/morph/</c>: this answers there with the
    /// same files, from the web root. <see cref="MapMorphWopi"/> does it for the folder of
    /// <see cref="MorphWopiOptions.Page"/>, so an application that serves the editor through it needs
    /// nothing more, and an app at the root needs nothing at all.
    /// </summary>
    public static IEndpointConventionBuilder MapMorphAssets(this IEndpointRouteBuilder endpoints, PathString basePath)
    {
        AddWebRootAssets(endpoints.ServiceProvider, WopiAssets.StyleSheet);
        var prefix = basePath.Value?.TrimEnd('/');
        return endpoints.MapGet($"{prefix}/{WopiAssets.ContentRoot}/{{**path}}", WopiAssets.Handle);
    }

    /// <summary>
    /// The editor's page and Morph.Blazor's assets are read from the web root. An application run from its
    /// build output has the files of the projects it references, the editor's WebAssembly app among them,
    /// in the web root only in Development, though MapStaticAssets serves them in any environment: outside
    /// it, they are added from the same manifest when <paramref name="file"/> is not in the web root. A
    /// published application has them in its wwwroot, and no manifest.
    /// </summary>
    static void AddWebRootAssets(IServiceProvider services, string file)
    {
        var environment = services.GetRequiredService<IWebHostEnvironment>();
        if (environment.WebRootFileProvider.GetFileInfo(file).Exists)
        {
            return;
        }

        StaticWebAssetsLoader.UseStaticWebAssets(environment, services.GetRequiredService<IConfiguration>());
    }
}
