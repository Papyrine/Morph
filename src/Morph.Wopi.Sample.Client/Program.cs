using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Morph;

// The editor: the file a WOPI host opened, which Morph.Wopi writes into the page, opened by MorphWopi.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<MorphWopi>("#app");
builder.Services.AddScoped(
    _ => new HttpClient
    {
        BaseAddress = new(builder.HostEnvironment.BaseAddress)
    });
builder.Services.AddMorph();
await builder.Build().RunAsync();
