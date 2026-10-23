// The editor's commands (MorphEditor.Commands): each is enabled, shown and checked by one rule, asked of
// one EditorState, whether a button, a menu or a key reaches it. These lay every rule out against the
// states that decide them, so a rule that changes shows in a snapshot's diff, and check that the table
// the script's names are looked up in holds every command declared.
public class EditorCommandTests
{
    static readonly EditorState writable = new() {IsWord = true, CanWrite = true};

    // What the rules turn on, each one thing away from a Word document that can be written.
    static readonly (string Name, EditorState State)[] places =
    [
        ("read-only", new() {IsWord = true}),
        ("writable", writable),
        ("revising", writable with {Revising = true}),
        ("in a table", writable with {InTable = true}),
        ("in a link", writable with {InLink = true}),
        ("in a numbered list", writable with {List = ListKind.Number}),
        ("a picture selected", writable with {PictureSelected = true}),
        ("a table of contents", writable with {HasContents = true}),
        ("tracked changes", writable with {CanResolve = true, HasChanges = true}),
        ("tracking forced", writable with {ForcesTracking = true}),
        ("comments allowed", writable with {CanComment = true})
    ];

    // What the ribbon shows as on.
    static readonly (string Name, EditorState State)[] toggles =
    [
        ("painting", writable with {Painting = true}),
        ("subscript", writable with {Subscript = true}),
        ("superscript", writable with {Superscript = true}),
        ("in a bulleted list", writable with {List = ListKind.Bullet}),
        ("in a numbered list", writable with {List = ListKind.Number}),
        ("marks shown", writable with {MarksShown = true}),
        ("find shown", writable with {FindShown = true}),
        ("replace shown", writable with {ReplaceShown = true}),
        ("tracking", writable with {Tracking = true}),
        ("review shown", writable with {ReviewShown = true}),
        ("pages shown", writable with {PagesShown = true}),
        ("ruler shown", writable with {RulerShown = true}),
        ("presenting", writable with {Presenting = true})
    ];

    // What the host or the document rules out.
    static readonly (string Name, EditorState State)[] hosts =
    [
        ("a workbook", new()),
        ("no review", writable),
        ("review", writable with {OffersReview = true}),
        ("review and resolving", writable with {OffersReview = true, CanResolve = true})
    ];

    // The commands whose rule gives the same answer in each state, together, under the states it allows.
    [Test]
    public Task WhereEachCommandCanBeUsed() =>
        Verify(Grouped(_ => _.Enabled, places));

    // Only what is not on every ribbon: a command that cannot be used is greyed out, not taken away.
    [Test]
    public Task WhereEachCommandIsShown() =>
        Verify(Grouped(_ => _.Visible, hosts, everywhere: false))
            .Snapshot(
                """
                no review, review, review and resolving
                    ruler
                review and resolving
                    accept-all, reject-all
                review, review and resolving
                    new-comment, review-pane
                """);

    // Only the commands that are on or off; the rest have no pressed state at all.
    [Test]
    public Task WhenEachCommandIsOn()
    {
        var builder = new StringBuilder();
        foreach (var command in MorphEditor.Commands.All.OrderBy(_ => _.Name, StringComparer.Ordinal))
        {
            if (toggles.All(_ => command.Checked(_.State) is null))
            {
                continue;
            }

            var on = toggles.Where(_ => command.Checked(_.State) == true).Select(_ => _.Name);
            builder.AppendLine($"{command.Name}: {string.Join(", ", on)}");
        }

        return Verify(builder.ToString());
    }

    // Every command declared is in the table the script's names are looked up in, by its own name.
    [Test]
    public async Task TheTable_HoldsEveryCommand()
    {
        var declared = typeof(MorphEditor.Commands)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(_ => _.FieldType == typeof(EditorCommand))
            .Select(_ => (EditorCommand) _.GetValue(null)!)
            .ToList();

        await Assert.That(declared.Count).IsEqualTo(MorphEditor.Commands.All.Count);
        foreach (var command in declared)
        {
            await Assert.That(MorphEditor.Commands.Named(command.Name)).IsEqualTo(command);
        }
    }

    // A command with a key says so in its tooltip, as Word's do, and to assistive technology.
    [Test]
    public async Task AKey_IsInTheTooltip()
    {
        await Assert.That(MorphEditor.Commands.Bold.Title).IsEqualTo("Bold (Ctrl+B)");
        await Assert.That(MorphEditor.Commands.Bold.KeyShortcuts).IsEqualTo("Control+B");
        await Assert.That(MorphEditor.Commands.Marks.Title).IsEqualTo("Show/Hide ¶ (Ctrl+Shift+8)");
        await Assert.That(MorphEditor.Commands.PageBreak.Title).IsEqualTo("Page break");
        await Assert.That(MorphEditor.Commands.PageBreak.KeyShortcuts).IsNull();
    }

    // Everywhere false leaves out the commands the rule allows in every state.
    static string Grouped(Func<EditorCommand, Func<EditorState, bool>> rule, (string Name, EditorState State)[] states, bool everywhere = true)
    {
        var lines = new List<string>();
        var groups = MorphEditor.Commands.All
            .Select(_ => (Allowed: states.Where(state => rule(_)(state.State)).Select(state => state.Name).ToList(), _.Name))
            .GroupBy(_ => string.Join(", ", _.Allowed))
            .Where(_ => everywhere || _.First().Allowed.Count < states.Length)
            .OrderBy(_ => _.Key, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            lines.Add(group.Key.Length == 0 ? "(none)" : group.Key);
            lines.Add($"    {string.Join(", ", group.Select(_ => _.Name).Order(StringComparer.Ordinal))}");
        }

        return string.Join('\n', lines);
    }
}
