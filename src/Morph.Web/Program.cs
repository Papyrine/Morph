var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services
    .AddScoped(_ =>
    new HttpClient
    {
        BaseAddress = new(builder.HostEnvironment.BaseAddress)
    });
// The converter component's own services (JS module bridge). The HttpClient above is the other half of
// what it needs — it fetches the bundled fonts and samples out of the package's static web assets.
builder.Services.AddMorph();
builder.Services.AddScoped<ThemePreferenceService>();

await builder.Build().RunAsync();
