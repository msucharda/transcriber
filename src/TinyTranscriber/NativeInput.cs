using System.Runtime.InteropServices;

namespace TinyTranscriber;

internal static class NativeInput
{
    private const byte VkControl = 0x11;
    private const byte VkV = 0x56;
    private const uint KeyUp = 0x0002;
    private static readonly ForegroundPaste Paste = new(new WindowsInput());

    public static nint GetActiveWindow()
    {
        return GetForegroundWindow();
    }

    public static Task<bool> TryPasteAsync(nint targetWindow, CancellationToken cancellationToken = default) =>
        Paste.TryPasteAsync(targetWindow, cancellationToken);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    private sealed class WindowsInput : IWindowInput
    {
        public bool IsWindow(nint window) => NativeInput.IsWindow(window);
        public nint GetForegroundWindow() => NativeInput.GetForegroundWindow();
        public bool ActivateWindow(nint window) => SetForegroundWindow(window);

        public void SendPaste()
        {
            keybd_event(VkControl, 0, 0, UIntPtr.Zero);
            keybd_event(VkV, 0, 0, UIntPtr.Zero);
            keybd_event(VkV, 0, KeyUp, UIntPtr.Zero);
            keybd_event(VkControl, 0, KeyUp, UIntPtr.Zero);
        }
    }
}
