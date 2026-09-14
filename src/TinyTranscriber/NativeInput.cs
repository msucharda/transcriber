using System.Runtime.InteropServices;

namespace TinyTranscriber;

internal static class NativeInput
{
    private const byte VkControl = 0x11;
    private const byte VkV = 0x56;
    private const uint KeyUp = 0x0002;

    public static nint GetActiveWindow()
    {
        return GetForegroundWindow();
    }

    public static async Task PasteAsync(nint targetWindow, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (targetWindow != nint.Zero)
        {
            SetForegroundWindow(targetWindow);
            await Task.Delay(75, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        keybd_event(VkV, 0, 0, UIntPtr.Zero);
        keybd_event(VkV, 0, KeyUp, UIntPtr.Zero);
        keybd_event(VkControl, 0, KeyUp, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
