using static EditFixtures;

// The ribbon gives up room group by group as it narrows, as Word's does (foldRibbon in morph-viewer.js):
// measured in the browser, so tested in one. Widths depend on the fonts the browser has, so these hold
// the rules rather than the pixels: every width fits on one line, groups give up room lowest priority
// first and never take it back as the ribbon narrows, and a folded group opens under its button with its
// commands — and its menus — working from there.
public partial class SnapshotTests
{
    // How much room a group has given up: whole, then squeezed, then folded.
    static readonly string[] forms = ["whole", "squeezed", "folded"];

    // A group's form, and whether folding it would save room — a group of one command, or of two icons,
    // is no wider than the button it would fold into.
    record RibbonGroup(string Name, int Priority, string Form, bool FoldSaves);

    // The showing tab's groups at a width, once the ribbon has refolded, and whether they are on one line.
    static async Task<(IReadOnlyList<RibbonGroup> Groups, bool OneLine)> RibbonAtAsync(IPage page, int width)
    {
        await page.SetViewportSizeAsync(width, 800);
        var read = await page.EvaluateAsync<string[]>(
            """
            () => new Promise(resolve => {
                // The resize's observer, then the fold's frame, then one to spare.
                let frames = 3;
                const next = () => {
                    if (--frames > 0) {
                        requestAnimationFrame(next);
                        return;
                    }

                    const groups = [...document.querySelectorAll('.editor-tools > .editor-group')];
                    const lines = new Set(groups.map(_ => Math.round(_.getBoundingClientRect().top))).size;
                    const saves = group => {
                        const form = group.dataset.fold;
                        const now = group.getBoundingClientRect().width;
                        group.dataset.fold = 'folded';
                        const folded = group.getBoundingClientRect().width;
                        if (form) {
                            group.dataset.fold = form;
                        } else {
                            delete group.dataset.fold;
                        }

                        return folded < now;
                    };
                    resolve([String(lines === 1), ...groups.map(_ => `${_.getAttribute('aria-label')}|${_.dataset.priority}|${_.dataset.fold ?? 'whole'}|${saves(_)}`)]);
                };
                requestAnimationFrame(next);
            })
            """);
        var groups = read.Skip(1)
            .Select(_ => _.Split('|'))
            .Select(_ => new RibbonGroup(_[0], int.Parse(_[1]), _[2], _[3] == "true"))
            .ToList();
        return (groups, read[0] == "true");
    }

    [Test]
    public async Task Ribbon_GivesUpRoom_GroupByGroup_AsItNarrows()
    {
        var page = await OpenEditAsync(Paragraphs);

        var (wide, _) = await RibbonAtAsync(page, 1900);
        await Assert.That(wide.All(_ => _.Form == "whole")).IsTrue();

        var before = wide;
        var folded = false;
        foreach (var width in new[] {1500, 1300, 1150, 1000, 850, 700, 560})
        {
            var (groups, oneLine) = await RibbonAtAsync(page, width);
            await Assert.That(oneLine).IsTrue().Because($"at {width}px the groups fit on one line");

            // Never more room than at the width before, wider.
            for (var index = 0; index < groups.Count; index++)
            {
                await Assert.That(Array.IndexOf(forms, groups[index].Form)).IsGreaterThanOrEqualTo(Array.IndexOf(forms, before[index].Form));
            }

            // A group folds only once every group below it that folding would make smaller has:
            // lowest priority first.
            foreach (var group in groups.Where(_ => _.Form == "folded"))
            {
                await Assert.That(
                        groups.Where(_ => _.Priority < group.Priority &&
                                          _.FoldSaves)
                            .All(_ => _.Form == "folded"))
                    .IsTrue()
                    .Because($"{group.Name} folded at {width}px");
            }

            folded |= groups.Any(_ => _.Form == "folded");
            before = groups;
        }

        await Assert.That(folded).IsTrue();
        // Word's order on Home: the style gallery folds before the Font group, which holds out longest.
        await Assert.That(before.First(_ => _.Name == "Styles").Form).IsEqualTo("folded");
        await Assert.That(before.First(_ => _.Name == "Editing").Form).IsNotEqualTo("whole");

        // Wider again, every group takes its room back.
        var (again, _) = await RibbonAtAsync(page, 1900);
        await Assert.That(again.All(_ => _.Form == "whole")).IsTrue();
    }

    // A folded group's button opens the group whole under it, and keeps the paragraph being typed in
    // open; Escape closes it, and so does a command run from it.
    [Test]
    public async Task AFoldedGroup_OpensUnderItsButton_AndItsCommandsRunFromThere()
    {
        var page = await OpenEditAsync(Paragraphs);
        await page.SetViewportSizeAsync(700, 800);
        const string styles = ".editor-group[aria-label='Styles']";
        const string heading = $"{styles} .editor-gallery-item[title='Heading 1']";
        await page.WaitForSelectorAsync($"{styles}[data-fold=folded] > .editor-fold");
        await ClickInAsync(page, "and last");

        await page.ClickAsync($"{styles} > .editor-fold");
        await page.WaitForSelectorAsync($"{styles}[data-open] .editor-gallery-item[title='Heading 1']");
        await Assert.That(await page.GetAttributeAsync($"{styles} > .editor-fold", "aria-expanded")).IsEqualTo("true");

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForSelectorAsync(
            ".editor-group[data-open]",
            new()
            {
                State = WaitForSelectorState.Detached
            });
        await Assert.That(await page.Locator(editor).CountAsync()).IsEqualTo(1);

        await page.ClickAsync($"{styles} > .editor-fold");
        await page.ClickAsync(heading);
        await WaitForBodyAsync(page, _ => _.Contains("w:val=\"Heading1\""));
        await Assert.That(await page.Locator(".editor-group[data-open]").CountAsync()).IsEqualTo(0);
    }

    // A menu in a folded group opens over the group, and choosing from it runs and closes both.
    [Test]
    public async Task AFoldedGroup_ItsMenusWorkFromIt()
    {
        var page = await OpenEditAsync(Paragraphs);
        await page.SetViewportSizeAsync(420, 800);
        await ClickInAsync(page, "and last");
        await ChooseTabAsync(page, "Insert");
        const string headers = ".editor-group[aria-label='Header and footer']";
        await page.WaitForSelectorAsync($"{headers}[data-fold=folded] > .editor-fold");

        await page.ClickAsync($"{headers} > .editor-fold");
        await page.ClickAsync($"{headers} .editor-tool:has-text('Page number')");
        await page.ClickAsync($"{headers} .editor-menu .editor-menu-item:text-is('Page number')");

        await WaitForBodyAsync(page, _ => _.Contains(" PAGE "));
        await Assert.That(await page.Locator(".editor-group[data-open]").CountAsync()).IsEqualTo(0);
        await Assert.That(await page.Locator(".editor-menu").CountAsync()).IsEqualTo(0);
    }
}
