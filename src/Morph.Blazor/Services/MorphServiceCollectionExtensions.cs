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
    /// the Blazor WebAssembly template already does.
    /// </summary>
    public static IServiceCollection AddMorph(this IServiceCollection services)
    {
        services.AddScoped<MorphInterop>();
        return services;
    }
}
