using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TinyTranscriber;

internal static class NativeInput
{
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;
    private const uint KeyUp = 0x0002;
    private static readonly ForegroundPaste Paste = new(new WindowsInput());

    public static nint GetActiveWindow()
    {
        return GetForegroundWindow();
    }

    public static Task<bool> TryDeliverAsync(
        nint targetWindow, string text, CancellationToken cancellationToken = default) =>
        Paste.TryDeliverAsync(targetWindow, text, Clipboard.SetText, cancellationToken);

    internal static bool IsExternalWindow(nint window) => IsWindow(window)
        && GetWindowThreadProcessId(window, out var processId) != 0
        && processId != Environment.ProcessId;

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    private static Input Key(ushort key, uint flags = 0) => new()
    {
        Type = 1,
        Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = flags } }
    };

    private sealed class WindowsInput : IWindowInput
    {
        public bool IsWindow(nint window) => IsExternalWindow(window);
        public nint GetForegroundWindow() => NativeInput.GetForegroundWindow();
        public bool ActivateWindow(nint window) => SetForegroundWindow(window);
        public bool AreModifiersReleased() =>
            new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.All(key => (GetAsyncKeyState(key) & 0x8000) == 0);

        public void SendPaste()
        {
            Input[] inputs = [Key(VkControl), Key(VkV), Key(VkV, KeyUp), Key(VkControl, KeyUp)];
            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
            if (sent != inputs.Length)
            {
                var error = Marshal.GetLastWin32Error();
                // Release modifiers after a partial injection; never retry the paste.
                if (sent > 0)
                {
                    SendInput(2, [Key(VkV, KeyUp), Key(VkControl, KeyUp)], Marshal.SizeOf<Input>());
                }

                throw new Win32Exception(error, "Windows did not accept the complete paste input. Check the field before retrying.");
            }
        }
    }
}
