namespace Morph;

/// <summary>
/// Makes an ASP.NET Core application a WOPI client that opens files in Morph's editor: a WOPI host (one
/// built on WopiServer, or any other that takes a client's discovery) reads the discovery, opens a file
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
}
