/// <summary>
/// Guards the scenario / render tests so a manual host run doesn't fail with a
/// misleading pixel diff. Their Verify baselines are generated inside the pinned
/// linux/amd64 container (<c>./scripts/test.sh</c>) and only match a render
/// produced there — on any other OS/CPU/filesystem the rasterization diverges.
/// Rather than fail, the tests skip themselves unless <c>MORPH_TEST_CONTAINER=1</c>
/// (set by <c>Dockerfile.test</c>).
/// </summary>
static class ContainerOnly
{
    internal static void Require() =>
        Skip.Unless(
            Environment.GetEnvironmentVariable("MORPH_TEST_CONTAINER") == "1",
            "Scenario/render tests run only inside the linux/amd64 container (./scripts/test.sh); " +
            "their Verify baselines are container-specific and will not match a host render. " +
            "Set MORPH_TEST_CONTAINER=1 to override.");
}
