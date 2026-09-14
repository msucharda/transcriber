namespace TinyTranscriber;

internal sealed record HotkeyDefinition(uint Modifiers, uint VirtualKey, string DisplayName)
{
    public const string EnvironmentVariable = "TINY_TRANSCRIBER_HOTKEY";
    public const string DefaultValue = "Ctrl+Shift+Space";

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    public static HotkeyDefinition Load()
    {
        var configuredValue = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return Parse(string.IsNullOrWhiteSpace(configuredValue) ? DefaultValue : configuredValue);
    }

    public static HotkeyDefinition Parse(string value)
    {
        var parts = value.Split(
            '+',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            throw InvalidHotkey(value);
        }

        uint modifiers = ModNoRepeat;
        var modifierNames = new List<string>();
        Keys? key = null;

        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    AddModifier(ref modifiers, ModControl, "Ctrl", modifierNames, value);
                    break;
                case "SHIFT":
                    AddModifier(ref modifiers, ModShift, "Shift", modifierNames, value);
                    break;
                case "ALT":
                    AddModifier(ref modifiers, ModAlt, "Alt", modifierNames, value);
                    break;
                case "WIN":
                case "WINDOWS":
                    AddModifier(ref modifiers, ModWin, "Win", modifierNames, value);
                    break;
                default:
                    if (key is not null
                        || !Enum.TryParse<Keys>(part, true, out var parsedKey)
                        || parsedKey is Keys.None
                        || (parsedKey & Keys.Modifiers) != Keys.None)
                    {
                        throw InvalidHotkey(value);
                    }

                    key = parsedKey & Keys.KeyCode;
                    break;
            }
        }

        if (key is null || modifierNames.Count == 0)
        {
            throw InvalidHotkey(value);
        }

        return new HotkeyDefinition(
            modifiers,
            (uint)key.Value,
            $"{string.Join("+", modifierNames)}+{key.Value}");
    }

    private static void AddModifier(
        ref uint modifiers,
        uint modifier,
        string displayName,
        List<string> modifierNames,
        string originalValue)
    {
        if ((modifiers & modifier) != 0)
        {
            throw InvalidHotkey(originalValue);
        }

        modifiers |= modifier;
        modifierNames.Add(displayName);
    }

    private static FormatException InvalidHotkey(string value)
    {
        return new FormatException(
            $"{EnvironmentVariable} value '{value}' is invalid. Example: {DefaultValue}.");
    }
}
