using static Define;

// P — what becomes of the selection when its text is formatted: the same change made by a key, by a
// ribbon control, through a dialog and from the selection made the other way round, inside one paragraph
// and across two. The Typing family's formatting keys (E-80 to E-92) record the selection after each key
// too; this family varies only how the change is made, so what it shows is whether that matters.
static class KeepFamily
{
    public static FamilyDefinition Definition { get; } = new("keep", "Formatting a selection",
        "Whether the selection survives formatting, made by a key, a ribbon control (CommandBars.ExecuteMso, as a click runs it) or a dialog accepted with Enter. Recorded on Word build 16.0.19127 on a 1920x1200 display, not the build and display in Environment.",
        Scenarios);

    const Observe keep = Observe.Default | Observe.Formatting;

    static IEnumerable<ScenarioBuilder> Scenarios()
    {
        yield return Probe("P-01", "Formatting text selected inside a paragraph, made each way")
            .In("three-paragraphs")
            .Steps("select(P1:6-P1:15)", "{how}")
            .Axis("how",
                "Ctrl+B", "Ctrl+I", "Ctrl+]", "Ctrl+Shift+>", "Ctrl+Space", "Shift+F3", "Ctrl+E", "Ctrl+Alt+1",
                "ribbon(Bold)", "ribbon(Italic)", "ribbon(FontSizeIncreaseWord)", "ribbon(ClearFormatting)",
                "ribbon(AlignCenter)", "ribbon(BulletsGalleryWord)", "ribbon(IndentIncreaseWord)",
                "dialog(Ctrl+D, ok)")
            .Tracked()
            .Observing(keep)
            .Hypotheses("The selection is the same text afterwards, whichever way the change was made: a character change and a paragraph change alike leave it selected, end-active as it was made.")
            .Morph("PARTIAL");

        yield return Probe("P-02", "Formatting a selection made backwards, by Shift+Left")
            .In("three-paragraphs")
            .Steps("select(P1:15)", "Shift+Left x9", "{how}")
            .Axis("how", "Ctrl+B", "ribbon(Bold)", "Ctrl+E", "ribbon(AlignCenter)", "dialog(Ctrl+D, ok)")
            .Observing(keep)
            .Hypotheses("The selection stays, start-active: formatting does not turn it round.")
            .Morph("PARTIAL");

        yield return Probe("P-03", "Formatting a selection across two paragraphs")
            .In("three-paragraphs")
            .Steps("select(P1:6-P2:6)", "{how}")
            .Axis("how", "Ctrl+B", "ribbon(Bold)", "Ctrl+]", "ribbon(FontSizeIncreaseWord)", "Ctrl+E", "ribbon(AlignCenter)", "dialog(Ctrl+D, ok)")
            .Tracked()
            .Observing(keep)
            .Hypotheses("The selection still runs from the first paragraph into the second.")
            .Morph("PARTIAL");
    }
}
