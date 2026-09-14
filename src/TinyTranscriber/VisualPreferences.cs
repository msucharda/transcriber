using System.Runtime.InteropServices;

namespace TinyTranscriber;

internal static class VisualPreferences
{
    public static bool AnimationsEnabled()
    {
        if (SystemParametersInfo(0x1042, 0, out var enabled, 0))
        {
            return enabled;
        }

        Console.Error.WriteLine("Could not read Windows animation preference; decorative motion is disabled.");
        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
