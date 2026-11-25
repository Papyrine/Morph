using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Morph;

/// <summary>Registers everything <see cref="MorphConverter"/> and its parts resolve from DI.</summary>
public static class MorphServiceCollectionExtensions
{
    /// <summary>
    /// Adds Morph's Blazor services. Call it once at startup:
    /// <code>
    /// builder.Services.AddMorph();
    /// </code>
    /// The components also inject <see cref="HttpClient"/> — to fetch the bundled fonts and samples out
    /// of this package's static web assets — so the host app must register a base-addressed one, which
    /// the Blazor WebAssembly template already does. <see cref="MorphWopi"/> keeps a file's lock by the
    /// <see cref="TimeProvider"/>: the system's, unless one is registered.
    /// </summary>
    public static IServiceCollection AddMorph(this IServiceCollection services)
    {
        services.AddScoped<MorphInterop>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
