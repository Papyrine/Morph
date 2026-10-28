record ProbeCase(
    string Id,
    Scenario Scenario,
    string Fixture,
    IReadOnlyList<string> StepTexts,
    IReadOnlyDictionary<string, string> Options,
    bool Tracked,
    IReadOnlyList<KeyValuePair<string, string>> AxisValues,
    string? BranchPath,
    string DefinitionHash)
{
    public IReadOnlyList<Step> Steps() =>
        StepTexts.Select(StepParser.Parse).ToList();
}