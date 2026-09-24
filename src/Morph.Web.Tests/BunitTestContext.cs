public class BunitTestContext : BunitContext
{
    public BunitTestContext()
    {
        // The component package's own services (its JS module bridge).
        Services.AddMorph();
        // MorphConverter injects HttpClient (to fetch the bundled sample document and the PDF fonts); a
        // base-addressed instance is enough for components to resolve and render under bunit.
        Services.AddScoped(_ => new HttpClient { BaseAddress = new("http://localhost/") });
    }

    // MorphViewer attaches a JavaScript controller object (morph-viewer.js's attachViewer) and drives it
    // for every page, zoom and find. Planning the call hands the test that controller, whose invocations
    // record what the component asked the browser to do.
    protected BunitJSModuleInterop SetupViewerController()
    {
        var module = JSInterop.SetupModule($"./{MorphAssets.ViewerScript}");
        var controller = module.SetupModule("attachViewer", _ => true);
        controller.Mode = JSRuntimeMode.Loose;
        return controller;
    }
}
