using System.Windows.Forms;

namespace TinyTranscriber.Tests;

public sealed class HotkeyDefinitionTests
{
    [Fact]
    public void ParseUsesCtrlShiftSpace()
    {
        var hotkey = HotkeyDefinition.Parse("Ctrl+Shift+Space");

        Assert.Equal("Ctrl+Shift+Space", hotkey.DisplayName);
        Assert.Equal((uint)Keys.Space, hotkey.VirtualKey);
        Assert.Equal(0x4006u, hotkey.Modifiers);
    }

    [Fact]
    public void ParseSupportsAlternativeShortcut()
    {
        var hotkey = HotkeyDefinition.Parse("Win+Alt+F8");

        Assert.Equal("Win+Alt+F8", hotkey.DisplayName);
        Assert.Equal((uint)Keys.F8, hotkey.VirtualKey);
        Assert.Equal(0x4009u, hotkey.Modifiers);
    }

    [Theory]
    [InlineData("Space")]
    [InlineData("Ctrl+Ctrl+Space")]
    [InlineData("Ctrl+UnknownKey")]
    [InlineData("Ctrl+Space+F8")]
    public void ParseRejectsInvalidShortcut(string value)
    {
        Assert.Throws<FormatException>(() => HotkeyDefinition.Parse(value));
    }
}
