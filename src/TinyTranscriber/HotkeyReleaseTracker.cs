namespace TinyTranscriber;

internal sealed class HotkeyReleaseTracker(Func<bool> isKeyDown)
{
    public bool IsPressed { get; private set; }
    public event Action? Pressed;
    public event Action? Released;

    public void Press()
    {
        // MOD_NOREPEAT means another WM_HOTKEY is a fresh gesture even when
        // release/repress happened between timer ticks.
        Release();
        IsPressed = true;
        Pressed?.Invoke();
        Poll();
    }

    public void Poll()
    {
        if (IsPressed && !isKeyDown())
        {
            Release();
        }
    }

    private void Release()
    {
        if (IsPressed)
        {
            IsPressed = false;
            Released?.Invoke();
        }
    }
}
