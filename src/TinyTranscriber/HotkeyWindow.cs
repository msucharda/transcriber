using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TinyTranscriber;

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 1;
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint VkSpace = 0x20;
    private bool disposed;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams());

        if (!RegisterHotKey(Handle, HotkeyId, ModControl, VkSpace))
        {
            DestroyHandle();
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Ctrl+Space is already registered by another application.");
        }
    }

    public event EventHandler? Pressed;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        UnregisterHotKey(Handle, HotkeyId);
        DestroyHandle();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey && message.WParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        base.WndProc(ref message);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);
}
