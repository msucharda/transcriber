using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TinyTranscriber;

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 1;
    private const int WmHotkey = 0x0312;
    private readonly System.Windows.Forms.Timer releaseTimer = new() { Interval = 15 };
    private readonly HotkeyReleaseTracker releaseTracker;
    private bool disposed;

    public HotkeyWindow(HotkeyDefinition hotkey)
    {
        releaseTracker = new HotkeyReleaseTracker(() => (GetAsyncKeyState((int)hotkey.VirtualKey) & 0x8000) != 0);
        releaseTracker.Pressed += () => Pressed?.Invoke(this, EventArgs.Empty);
        releaseTracker.Released += () => Released?.Invoke(this, EventArgs.Empty);
        releaseTimer.Tick += (_, _) =>
        {
            releaseTracker.Poll();
            if (!releaseTracker.IsPressed)
            {
                releaseTimer.Stop();
            }
        };
        CreateHandle(new CreateParams());

        if (!RegisterHotKey(Handle, HotkeyId, hotkey.Modifiers, hotkey.VirtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            DestroyHandle();
            releaseTimer.Dispose();
            throw new Win32Exception(
                error,
                $"{hotkey.DisplayName} is already registered by another application. "
                + $"Set {HotkeyDefinition.EnvironmentVariable} to another shortcut.");
        }
    }

    public event EventHandler? Pressed;
    public event EventHandler? Released;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        releaseTimer.Stop();
        releaseTimer.Dispose();
        UnregisterHotKey(Handle, HotkeyId);
        DestroyHandle();
    }

    protected override void WndProc(ref Message message)
    {
        if (!disposed && message.Msg == WmHotkey && message.WParam.ToInt32() == HotkeyId)
        {
            releaseTracker.Press();
            if (releaseTracker.IsPressed)
            {
                releaseTimer.Start();
            }
        }

        base.WndProc(ref message);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
