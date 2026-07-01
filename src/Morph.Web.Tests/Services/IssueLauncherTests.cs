public class IssueLauncherTests
{
    [Test]
    public async Task ForException_BuildsPrefilledIssueUrl()
    {
        // A constructed (never thrown) exception has a null stack trace, so ToString() is deterministic.
        var exception = new InvalidOperationException("boom");

        var url = IssueLauncher.ForException("Could not read the document", exception, "* Morph version: 1.2.3");

        await Assert.That(url).StartsWith("https://github.com/Papyrine/Morph/issues/new?title=");

        var decoded = WebUtility.UrlDecode(url);
        await Assert.That(decoded).Contains("Could not read the document: InvalidOperationException");
        await Assert.That(decoded).Contains("* Action: Could not read the document");
        await Assert.That(decoded).Contains("* Morph version: 1.2.3");
        await Assert.That(decoded).Contains("InvalidOperationException: boom");
    }

    [Test]
    public async Task ForException_WithoutEnvironment_OmitsEnvironmentBlock()
    {
        var url = IssueLauncher.ForException("Boom", new("oops"));

        var decoded = WebUtility.UrlDecode(url);
        await Assert.That(decoded).Contains("* Action: Boom");
        await Assert.That(decoded).DoesNotContain("* Morph version:");
    }
}
