// DrawingML's preset colour names (a:prstClr, ECMA-376 §20.1.10.47): CSS's, with dark, light and medium
// shortened and grey spelt either way.
public class PresetColorTests
{
    [Test]
    [Arguments("black", "000000")]
    [Arguments("white", "FFFFFF")]
    [Arguments("red", "FF0000")]
    [Arguments("dkGray", "A9A9A9")]
    [Arguments("dkGrey", "A9A9A9")]
    [Arguments("ltSlateGray", "778899")]
    [Arguments("medAquamarine", "66CDAA")]
    [Arguments("ltGoldenrodYellow", "FAFAD2")]
    public async Task APreset_IsItsColour(string name, string hex) =>
        await Assert.That(PresetColors.Hex(name)).IsEqualTo(hex);

    // A system colour's name is no preset's, and neither is nonsense.
    [Test]
    [Arguments("window")]
    [Arguments("notAColour")]
    [Arguments("")]
    public async Task AnythingElse_IsNone(string name) =>
        await Assert.That(PresetColors.Hex(name)).IsNull();
}
