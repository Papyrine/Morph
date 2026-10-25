// Real keystrokes through SendInput. Letters and punctuation are sent as the virtual keys the en-US
// layout puts them on (VkKeyScanExW), because AutoFormat and AutoCorrect key off key presses; a character
// the layout lacks is sent as a Unicode packet instead.
[Flags]
enum Modifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4
}

record KeyChord(Modifiers Modifiers, ushort VirtualKey, string Name)
{
    static readonly Dictionary<string, ushort> named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = 0x0D,
        ["Return"] = 0x0D,
        ["Tab"] = 0x09,
        ["Esc"] = 0x1B,
        ["Escape"] = 0x1B,
        ["Backspace"] = 0x08,
        ["Space"] = 0x20,
        ["Delete"] = 0x2E,
        ["Del"] = 0x2E,
        ["Insert"] = 0x2D,
        ["Ins"] = 0x2D,
        ["Home"] = 0x24,
        ["End"] = 0x23,
        ["PgUp"] = 0x21,
        ["PageUp"] = 0x21,
        ["PgDn"] = 0x22,
        ["PageDown"] = 0x22,
        ["Left"] = 0x25,
        ["Up"] = 0x26,
        ["Right"] = 0x27,
        ["Down"] = 0x28,
        ["Num5"] = 0x0C,
        ["NumMinus"] = 0x6D,
        ["Minus"] = 0xBD,
        ["-"] = 0xBD,
        ["Plus"] = 0xBB,
        ["="] = 0xBB,
        ["["] = 0xDB,
        ["]"] = 0xDD,
        [","] = 0xBC,
        ["."] = 0xBE,
        ["Period"] = 0xBE,
        ["/"] = 0xBF,
        [";"] = 0xBA,
        ["'"] = 0xDE,
        ["`"] = 0xC0,
        ["\\"] = 0xDC,
        ["Apps"] = 0x5D,
        ["Menu"] = 0x5D
    };

    static readonly HashSet<ushort> extended =
    [
        0x2D, 0x2E, 0x24, 0x23, 0x21, 0x22, 0x25, 0x26, 0x27, 0x28, 0x90, 0xA3, 0xA5, 0x6F, 0x5B, 0x5C, 0x5D
    ];

    public bool IsExtended => extended.Contains(VirtualKey);

    /// <summary>"Ctrl+Shift+Right", "F8", "Alt+Shift+Up", "Enter", "Ctrl+=", "Ctrl+Shift+>".</summary>
    public static KeyChord Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = Modifiers.None;
        string? key = null;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0 &&
                i == parts.Length - 1)
            {
                // A trailing empty part means the key was '+' itself, as in "Ctrl++".
                key = "=";
                break;
            }

            if (i < parts.Length - 1)
            {
                modifiers |= part.ToLowerInvariant() switch
                {
                    "ctrl" or "control" => Modifiers.Ctrl,
                    "shift" => Modifiers.Shift,
                    "alt" => Modifiers.Alt,
                    _ => throw new ArgumentException($"Unknown modifier '{part}' in '{text}'.")
                };
            }
            else
            {
                key = part;
            }
        }

        if (key == null)
        {
            throw new ArgumentException($"No key in '{text}'.");
        }

        return new(modifiers, Resolve(key, ref modifiers, text), text);
    }

    static ushort Resolve(string key, ref Modifiers modifiers, string text)
    {
        if (named.TryGetValue(key, out var code))
        {
            return code;
        }

        if (key is ['F' or 'f', _] &&
            char.IsDigit(key[1]))
        {
            return (ushort) (0x70 + key[1] - '1');
        }

        if (key is ['F' or 'f', '1', _] &&
            char.IsDigit(key[2]))
        {
            return (ushort) (0x79 + key[2] - '0');
        }

        if (key.Length == 1)
        {
            var character = key[0];
            if (char.IsLetter(character))
            {
                return char.ToUpperInvariant(character);
            }

            if (char.IsDigit(character))
            {
                return character;
            }

            // Shifted punctuation such as '>' or '*' is the base key with Shift.
            var scan = NativeMethods.VkKeyScanExW(character, NativeMethods.GetKeyboardLayout(0));
            if (scan != -1)
            {
                if ((scan & 0x0100) != 0)
                {
                    modifiers |= Modifiers.Shift;
                }

                return (ushort) (scan & 0xFF);
            }
        }

        throw new ArgumentException($"Unknown key '{key}' in '{text}'.");
    }
}

sealed class Keyboard(IntPtr wordWindow, Action? beforeInput)
{
    const ushort shift = 0x10;
    const ushort control = 0x11;
    const ushort alt = 0x12;

    IntPtr layout = NativeMethods.GetKeyboardLayout(NativeMethods.ThreadOf(wordWindow));
    Action? beforeInput = beforeInput;

    public IntPtr Layout => layout;

    public void Press(KeyChord chord, int repeat = 1)
    {
        for (var i = 0; i < repeat; i++)
        {
            beforeInput?.Invoke();
            var inputs = new List<NativeMethods.Input>();
            ModifiersDown(chord.Modifiers, inputs);
            inputs.Add(Key(chord.VirtualKey, up: false, chord.IsExtended));
            inputs.Add(Key(chord.VirtualKey, up: true, chord.IsExtended));
            ModifiersUp(chord.Modifiers, inputs);
            Send(inputs);
            if (i < repeat - 1)
            {
                Thread.Sleep(40);
            }
        }
    }

    public void Type(string text)
    {
        foreach (var character in text)
        {
            beforeInput?.Invoke();
            var inputs = new List<NativeMethods.Input>();
            var scan = NativeMethods.VkKeyScanExW(character, layout);
            if (scan == -1 ||
                (scan & 0x0600) != 0)
            {
                inputs.Add(Unicode(character, up: false));
                inputs.Add(Unicode(character, up: true));
            }
            else
            {
                var key = (ushort) (scan & 0xFF);
                var shifted = (scan & 0x0100) != 0;
                if (shifted)
                {
                    inputs.Add(Key(shift, up: false, extended: false));
                }

                inputs.Add(Key(key, up: false, extended: false));
                inputs.Add(Key(key, up: true, extended: false));
                if (shifted)
                {
                    inputs.Add(Key(shift, up: true, extended: false));
                }
            }

            Send(inputs);
            Thread.Sleep(25);
        }
    }

    public static void ModifierDown(Modifiers modifiers)
    {
        var inputs = new List<NativeMethods.Input>();
        ModifiersDown(modifiers, inputs);
        Send(inputs);
    }

    public static void ModifierUp(Modifiers modifiers)
    {
        var inputs = new List<NativeMethods.Input>();
        ModifiersUp(modifiers, inputs);
        Send(inputs);
    }

    public static void PressEscape() =>
        Send([Key(0x1B, up: false, extended: false), Key(0x1B, up: true, extended: false)]);

    static void ModifiersDown(Modifiers modifiers, List<NativeMethods.Input> inputs)
    {
        if (modifiers.HasFlag(Modifiers.Ctrl))
        {
            inputs.Add(Key(control, up: false, extended: false));
        }

        if (modifiers.HasFlag(Modifiers.Shift))
        {
            inputs.Add(Key(shift, up: false, extended: false));
        }

        if (modifiers.HasFlag(Modifiers.Alt))
        {
            inputs.Add(Key(alt, up: false, extended: false));
        }
    }

    static void ModifiersUp(Modifiers modifiers, List<NativeMethods.Input> inputs)
    {
        if (modifiers.HasFlag(Modifiers.Alt))
        {
            inputs.Add(Key(alt, up: true, extended: false));
        }

        if (modifiers.HasFlag(Modifiers.Shift))
        {
            inputs.Add(Key(shift, up: true, extended: false));
        }

        if (modifiers.HasFlag(Modifiers.Ctrl))
        {
            inputs.Add(Key(control, up: true, extended: false));
        }
    }

    static NativeMethods.Input Key(ushort virtualKey, bool up, bool extended)
    {
        var flags = up ? NativeMethods.KeyEventKeyUp : 0;
        if (extended)
        {
            flags |= NativeMethods.KeyEventExtendedKey;
        }

        return new()
        {
            Type = NativeMethods.InputKeyboard,
            Union = new()
            {
                Keyboard = new()
                {
                    VirtualKey = virtualKey,
                    Scan = (ushort) NativeMethods.MapVirtualKeyW(virtualKey, 0),
                    Flags = flags
                }
            }
        };
    }

    static NativeMethods.Input Unicode(char character, bool up) =>
        new()
        {
            Type = NativeMethods.InputKeyboard,
            Union = new()
            {
                Keyboard = new()
                {
                    VirtualKey = 0,
                    Scan = character,
                    Flags = NativeMethods.KeyEventUnicode | (up ? NativeMethods.KeyEventKeyUp : 0)
                }
            }
        };

    static void Send(List<NativeMethods.Input> inputs)
    {
        var array = inputs.ToArray();
        var sent = NativeMethods.SendInput((uint) array.Length, array, Marshal.SizeOf<NativeMethods.Input>());
        if (sent != array.Length)
        {
            throw new InvalidOperationException($"SendInput sent {sent} of {array.Length} inputs (error {Marshal.GetLastWin32Error()}).");
        }
    }
}
