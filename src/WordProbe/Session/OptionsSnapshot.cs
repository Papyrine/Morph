// Word persists most of Application.Options and AutoCorrect to the registry when it quits, so a harness
// that flips them has to put them back — and a harness that crashed has to be able to put them back on
// its next start. The snapshot reads every known scalar member BEFORE anything is changed, is written to
// local application data, and is never overwritten until it has been restored.
//
// Members are addressed by name through IDispatch (LateBound): the embedded interop types only carry the
// members the code names statically, so reflection over them sees next to nothing.
record WordOptionsSnapshot(
    DateTimeOffset TakenAt,
    string WordBuild,
    Dictionary<string, JsonElement> Values,
    List<string> Unreadable);

static class OptionsSnapshot
{
    static readonly JsonSerializerOptions json = new()
    {
        WriteIndented = true
    };

    public static WordOptionsSnapshot Capture(Word.Application application)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var unreadable = new List<string>();
        foreach (var member in ProbeOptions.Known)
        {
            try
            {
                var value = ProbeOptions.Get(application, member);
                values[member] = JsonSerializer.SerializeToElement(Normalise(value));
            }
            catch (Exception)
            {
                unreadable.Add(member);
            }
        }

        return new(DateTimeOffset.Now, application.Build, values, unreadable);
    }

    static object? Normalise(object? value) =>
        value is Enum enumeration ? Convert.ToInt32(enumeration, CultureInfo.InvariantCulture) : value;

    /// <summary>Applies the named members from the snapshot. Returns the members that could not be set.</summary>
    public static List<string> Apply(Word.Application application, WordOptionsSnapshot snapshot, IEnumerable<string> members)
    {
        var failed = new List<string>();
        var owners = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            if (!snapshot.Values.TryGetValue(member, out var element))
            {
                continue;
            }

            try
            {
                ProbeOptions.SetIfDifferent(application, member, element, owners);
            }
            catch (Exception)
            {
                failed.Add(member);
            }
        }

        return failed;
    }

    /// <summary>Saves the snapshot and returns the one in effect: an earlier one never restored wins.</summary>
    public static WordOptionsSnapshot Save(WordOptionsSnapshot snapshot)
    {
        Directory.CreateDirectory(Paths.LocalState);
        if (File.Exists(Paths.OptionsSnapshot) &&
            !File.Exists(Paths.OptionsRestored))
        {
            // A previous run did not restore: its snapshot holds the user's real values, this one would not.
            Log.Warn($"Keeping the earlier options snapshot at {Paths.OptionsSnapshot}; it was never restored.");
            return Load() ?? snapshot;
        }

        File.WriteAllText(Paths.OptionsSnapshot, JsonSerializer.Serialize(snapshot, json));
        if (File.Exists(Paths.OptionsRestored))
        {
            File.Delete(Paths.OptionsRestored);
        }

        return snapshot;
    }

    public static WordOptionsSnapshot? Load()
    {
        if (!File.Exists(Paths.OptionsSnapshot))
        {
            return null;
        }

        return JsonSerializer.Deserialize<WordOptionsSnapshot>(File.ReadAllText(Paths.OptionsSnapshot), json);
    }

    public static void MarkRestored() =>
        File.WriteAllText(Paths.OptionsRestored, DateTimeOffset.Now.ToString("O"));
}

// The members the harness may change, the UI-noise baseline it forces, and typed setting from a snapshot
// value or a DSL string. Behaviour-bearing options are never changed by the baseline: a scenario that
// depends on one sets it through a toggle, so the record always says which value a result was measured
// under.
static class ProbeOptions
{
    static readonly string[] optionsMembers =
    [
        "AllowAccentedUppercase", "AllowClickAndTypeMouse", "AllowDragAndDrop", "AllowOpenInDraftView", "AllowPixelUnits",
        "AllowReadingMode", "AlwaysUseClearType", "AnimateScreenMovements", "ApplyFarEastFontsToAscii", "AutoCreateNewDrawings",
        "AutoFormatAsYouTypeApplyBorders", "AutoFormatAsYouTypeApplyBulletedLists", "AutoFormatAsYouTypeApplyClosings",
        "AutoFormatAsYouTypeApplyDates", "AutoFormatAsYouTypeApplyFirstIndents", "AutoFormatAsYouTypeApplyHeadings",
        "AutoFormatAsYouTypeApplyNumberedLists", "AutoFormatAsYouTypeApplyTables", "AutoFormatAsYouTypeAutoLetterWizard",
        "AutoFormatAsYouTypeDefineStyles", "AutoFormatAsYouTypeDeleteAutoSpaces", "AutoFormatAsYouTypeFormatListItemBeginning",
        "AutoFormatAsYouTypeInsertClosings", "AutoFormatAsYouTypeInsertOvers", "AutoFormatAsYouTypeMatchParentheses",
        "AutoFormatAsYouTypeReplaceFarEastDashes", "AutoFormatAsYouTypeReplaceFractions", "AutoFormatAsYouTypeReplaceHyperlinks",
        "AutoFormatAsYouTypeReplaceOrdinals", "AutoFormatAsYouTypeReplacePlainTextEmphasis", "AutoFormatAsYouTypeReplaceQuotes",
        "AutoFormatAsYouTypeReplaceSymbols", "AutoKeyboardSwitching", "AutoWordSelection", "BackgroundOpen", "BackgroundSave",
        "CheckGrammarAsYouType", "CheckGrammarWithSpelling", "CheckSpellingAsYouType", "ConfirmConversions", "ContextualSpeller",
        "CreateBackup", "CtrlClickHyperlinkToOpen", "CursorMovement", "DisplayGridLines", "DisplayPasteOptions",
        "DisplaySmartTagButtons", "EnableLiveDrag", "EnableLivePreview", "EnableMisusedWordsDictionary", "EnableSound",
        "FormatScanning", "IgnoreInternetAndFileAddresses", "IgnoreMixedDigits", "IgnoreUppercase", "INSKeyForOvertype",
        "INSKeyForPaste", "InterpretHighAnsi", "LabelSmartTags", "MeasurementUnit", "OMathAutoCorrectEnabled", "Overtype",
        "Pagination", "PasteAdjustParagraphSpacing", "PasteAdjustTableFormatting", "PasteAdjustWordSpacing",
        "PasteFormatBetweenDocuments", "PasteFormatBetweenStyledDocuments", "PasteFormatFromExternalSource",
        "PasteFormatWithinDocument", "PasteMergeFromPPT", "PasteMergeFromXL", "PasteMergeLists", "PasteOptionKeepBulletsAndNumbers",
        "PasteSmartCutPaste", "PasteSmartStyleBehavior", "PictureWrapType", "PrecisePositioning", "PromptUpdateStyle",
        "ReplaceSelection", "RepeatWord", "SaveInterval", "SaveNormalPrompt", "SavePropertiesPrompt", "SequenceCheck",
        "ShortMenuNames", "ShowControlCharacters", "ShowDevTools", "ShowFormatError", "ShowMarkupOpenSave", "ShowMenuFloaties",
        "ShowReadabilityStatistics", "ShowSelectionFloaties", "SmartCursoring", "SmartCutPaste", "SmartParaSelection", "SnapToGrid",
        "SnapToShapes", "StoreRSIDOnSave", "SuggestFromMainDictionaryOnly", "SuggestSpellingCorrections", "TabIndentKey",
        "TypeNReplace", "UpdateFieldsAtPrint", "UpdateLinksAtOpen", "UpdateLinksAtPrint", "UpdateStyleListBehavior",
        "UseCharacterUnit", "UseLocalUserInfo", "UseNormalStyleForList", "VirusProtection", "VisualSelection",
        "WarnBeforeSavingPrintingSendingMarkup", "WPDocNavKeys", "WPHelp"
    ];

    static readonly string[] autoCorrectMembers =
    [
        "CorrectCapsLock", "CorrectDays", "CorrectHangulAndAlphabet", "CorrectInitialCaps", "CorrectKeyboardSetting",
        "CorrectSentenceCaps", "CorrectTableCells", "DisplayAutoCorrectOptions", "FirstLetterAutoAdd", "OtherCorrectionsAutoAdd",
        "ReplaceText", "ReplaceTextFromSpellingChecker", "TwoInitialCapsAutoAdd"
    ];

    public static readonly string[] ApplicationMembers =
    [
        "DisplayAutoCompleteTips",
        "DisplayScreenTips",
        "UserName",
        "UserInitials"
    ];

    /// <summary>Every member the snapshot reads, as "Owner.Name".</summary>
    public static IReadOnlyList<string> Known { get; } =
    [
        .. optionsMembers.Select(_ => $"Options.{_}"),
        .. autoCorrectMembers.Select(_ => $"AutoCorrect.{_}"),
        .. ApplicationMembers.Select(_ => $"Application.{_}")
    ];

    static readonly Dictionary<string, object> baseline = new(StringComparer.Ordinal)
    {
        ["Options.ShowSelectionFloaties"] = false,
        ["Options.ShowMenuFloaties"] = false,
        ["Options.DisplayPasteOptions"] = false,
        ["Options.EnableLivePreview"] = false,
        ["Options.AnimateScreenMovements"] = false,
        ["Options.CheckSpellingAsYouType"] = false,
        ["Options.CheckGrammarAsYouType"] = false,
        ["Options.ContextualSpeller"] = false,
        ["Options.SaveInterval"] = 0,
        ["Options.BackgroundSave"] = false,
        ["Options.SaveNormalPrompt"] = false,
        ["Options.SavePropertiesPrompt"] = false,
        ["Options.Pagination"] = true,
        ["AutoCorrect.DisplayAutoCorrectOptions"] = false,
        ["Application.UserName"] = "Probe",
        ["Application.UserInitials"] = "WP"
    };

    /// <summary>Set once a document window exists; Word refuses it before.</summary>
    public static void ApplyWindowBaseline(Word.Application application)
    {
        try
        {
            Set(application, "Application.DisplayAutoCompleteTips", false);
        }
        catch (Exception exception)
        {
            Log.Warn($"DisplayAutoCompleteTips could not be set: {Describe(exception)}");
        }
    }

    /// <summary>The behaviour-bearing options whose values the record states (read, never set by the baseline).</summary>
    public static readonly string[] Recorded =
    [
        "Options.AutoWordSelection",
        "Options.SmartParaSelection",
        "Options.SmartCutPaste",
        "Options.PasteSmartCutPaste",
        "Options.AllowClickAndTypeMouse",
        "Options.ReplaceSelection",
        "Options.Overtype",
        "Options.INSKeyForOvertype",
        "Options.INSKeyForPaste",
        "Options.AllowDragAndDrop",
        "Options.CtrlClickHyperlinkToOpen",
        "Options.TabIndentKey",
        "Options.SmartCursoring",
        "Options.AllowAccentedUppercase",
        "Options.CursorMovement",
        "Options.VisualSelection",
        "Options.SnapToGrid",
        "Options.PasteFormatWithinDocument",
        "Options.PasteFormatBetweenDocuments",
        "Options.PasteFormatBetweenStyledDocuments",
        "Options.PasteFormatFromExternalSource",
        "Options.PasteAdjustWordSpacing",
        "Options.PasteAdjustParagraphSpacing",
        "Options.PasteAdjustTableFormatting",
        "Options.PasteSmartStyleBehavior",
        "Options.PasteMergeLists",
        "Options.PasteOptionKeepBulletsAndNumbers",
        "Options.AutoFormatAsYouTypeApplyBulletedLists",
        "Options.AutoFormatAsYouTypeApplyNumberedLists",
        "Options.AutoFormatAsYouTypeApplyBorders",
        "Options.AutoFormatAsYouTypeApplyTables",
        "Options.AutoFormatAsYouTypeApplyHeadings",
        "Options.AutoFormatAsYouTypeApplyDates",
        "Options.AutoFormatAsYouTypeApplyFirstIndents",
        "Options.AutoFormatAsYouTypeApplyClosings",
        "Options.AutoFormatAsYouTypeAutoLetterWizard",
        "Options.AutoFormatAsYouTypeDefineStyles",
        "Options.AutoFormatAsYouTypeDeleteAutoSpaces",
        "Options.AutoFormatAsYouTypeFormatListItemBeginning",
        "Options.AutoFormatAsYouTypeInsertClosings",
        "Options.AutoFormatAsYouTypeInsertOvers",
        "Options.AutoFormatAsYouTypeMatchParentheses",
        "Options.AutoFormatAsYouTypeReplaceFarEastDashes",
        "Options.AutoFormatAsYouTypeReplaceFractions",
        "Options.AutoFormatAsYouTypeReplaceHyperlinks",
        "Options.AutoFormatAsYouTypeReplaceOrdinals",
        "Options.AutoFormatAsYouTypeReplacePlainTextEmphasis",
        "Options.AutoFormatAsYouTypeReplaceQuotes",
        "Options.AutoFormatAsYouTypeReplaceSymbols",
        "AutoCorrect.CorrectSentenceCaps",
        "AutoCorrect.CorrectInitialCaps",
        "AutoCorrect.CorrectCapsLock",
        "AutoCorrect.CorrectDays",
        "AutoCorrect.CorrectHangulAndAlphabet",
        "AutoCorrect.CorrectKeyboardSetting",
        "AutoCorrect.CorrectTableCells",
        "AutoCorrect.ReplaceText",
        "AutoCorrect.ReplaceTextFromSpellingChecker",
        "AutoCorrect.TwoInitialCapsAutoAdd",
        "AutoCorrect.FirstLetterAutoAdd",
        "AutoCorrect.OtherCorrectionsAutoAdd"
    ];

    /// <summary>Every member the harness may set, so restore knows what to put back.</summary>
    public static HashSet<string> Touched { get; } = [with(StringComparer.Ordinal), .. baseline.Keys];

    public static void ApplyBaseline(Word.Application application)
    {
        foreach (var (member, value) in baseline)
        {
            try
            {
                Set(application, member, value);
            }
            catch (Exception exception)
            {
                Log.Warn($"Baseline option {member} could not be set: {Describe(exception)}");
            }
        }
    }

    static string Describe(Exception exception)
    {
        if (exception is TargetInvocationException
            {
                InnerException: { } wrapped
            })
        {
            return wrapped.Message;
        }

        return exception.Message;
    }

    public static Dictionary<string, string> ReadRecorded(Word.Application application)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in Recorded)
        {
            try
            {
                values[member] = Format(Get(application, member));
            }
            catch (Exception)
            {
                values[member] = "(unavailable)";
            }
        }

        return values;
    }

    static string Format(object? value) =>
        value switch
        {
            null => "null",
            bool flag => flag ? "on" : "off",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
        };

    public static object? Get(Word.Application application, string member)
    {
        var (target, name) = Locate(application, member);
        return LateBound.Get(target, name);
    }

    public static void Set(Word.Application application, string member, object value)
    {
        Touched.Add(member);
        var (target, name) = Locate(application, member);
        var current = LateBound.Get(target, name);
        LateBound.Set(target, name, Coerce(value, current));
    }

    // The reset after every case: a member is written only where it differs, since nearly every one is as the
    // snapshot holds it and writing an option is far dearer than reading it (358 ms a case when every member
    // was written, lists 2026-10-27). owners keeps Options and AutoCorrect across the members of one reset.
    public static void SetIfDifferent(Word.Application application, string member, object value, Dictionary<string, object> owners)
    {
        Touched.Add(member);
        var (target, name) = Locate(application, member, owners);
        var current = LateBound.Get(target, name);
        var wanted = Coerce(value, current);
        if (!Equals(wanted, current))
        {
            LateBound.Set(target, name, wanted);
        }
    }

    static (object Target, string Name) Locate(Word.Application application, string member, Dictionary<string, object>? owners = null)
    {
        var dot = member.IndexOf('.');
        var owner = member[..dot];
        var name = member[(dot + 1)..];
        if (owners != null &&
            owners.TryGetValue(owner, out var known))
        {
            return (known, name);
        }

        object target = owner switch
        {
            "Options" => application.Options,
            "AutoCorrect" => application.AutoCorrect,
            "Application" => application,
            _ => throw new ArgumentException($"Unknown option owner '{owner}' in '{member}'.")
        };
        owners?.Add(owner, target);
        return (target, name);
    }

    /// <summary>Converts a DSL string or a snapshot JSON value to the type the property currently holds.</summary>
    public static object Coerce(object value, object? current)
    {
        if (value is JsonElement element)
        {
            value = element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => element.TryGetInt32(out var integer) ? integer : element.GetSingle(),
                JsonValueKind.String => element.GetString()!,
                _ => throw new ArgumentException($"Unsupported snapshot value {element}")
            };
        }

        var type = current?.GetType() ?? value.GetType();
        if (value is string text)
        {
            if (type == typeof(bool))
            {
                return text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "on" || text == "1";
            }

            if (type == typeof(int))
            {
                return int.Parse(text, CultureInfo.InvariantCulture);
            }

            if (type == typeof(float) || type == typeof(double))
            {
                return Convert.ChangeType(float.Parse(text, CultureInfo.InvariantCulture), type, CultureInfo.InvariantCulture);
            }

            return text;
        }

        if (type == typeof(string))
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
    }
}
