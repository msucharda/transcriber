namespace TinyTranscriber;

internal interface IWindowInput
{
    bool IsWindow(nint window);
    nint GetForegroundWindow();
    bool ActivateWindow(nint window);
    bool AreModifiersReleased();
    void SendPaste();
}

internal sealed class ForegroundPaste(IWindowInput input)
{
    public async Task<bool> TryDeliverAsync(
        nint targetWindow,
        string text,
        Action<string> copy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsForegroundTarget(targetWindow))
        {
            return false;
        }

        await Task.Delay(75, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsForegroundTarget(targetWindow) || !input.AreModifiersReleased())
        {
            return false;
        }

        copy(text);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsForegroundTarget(targetWindow) || !input.AreModifiersReleased())
        {
            return false;
        }

        input.SendPaste();
        return true;
    }

    public async Task<bool> TryPasteAsync(nint targetWindow, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (targetWindow == nint.Zero || !input.IsWindow(targetWindow))
        {
            return false;
        }

        if (input.GetForegroundWindow() != targetWindow && !input.ActivateWindow(targetWindow))
        {
            return false;
        }

        await Task.Delay(75, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!input.IsWindow(targetWindow) || input.GetForegroundWindow() != targetWindow)
        {
            return false;
        }

        input.SendPaste();
        return true;
    }

    private bool IsForegroundTarget(nint targetWindow) =>
        targetWindow != nint.Zero
        && input.IsWindow(targetWindow)
        && input.GetForegroundWindow() == targetWindow;
}
