public class BunitTestContext : BunitContext
{
    public BunitTestContext()
    {
        Services.AddScoped<FileDownloadService>();
        // Index injects HttpClient (to fetch the bundled sample document and the PDF fonts); a
        // base-addressed instance is enough for components to resolve and render under bunit.
        Services.AddScoped(_ => new HttpClient { BaseAddress = new("http://localhost/") });
    }
}
