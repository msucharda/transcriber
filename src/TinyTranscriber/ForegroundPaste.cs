namespace TinyTranscriber;

internal interface IWindowInput
{
    bool IsWindow(nint window);
    nint GetForegroundWindow();
    bool ActivateWindow(nint window);
    void SendPaste();
}

internal sealed class ForegroundPaste(IWindowInput input)
{
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
}
