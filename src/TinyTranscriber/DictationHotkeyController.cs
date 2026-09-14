namespace TinyTranscriber;

internal sealed class DictationHotkeyController(ParagraphQueue queue, Func<nint> captureTarget)
{
    private bool held;
    private bool accepted;

    public RecordingMode Mode { get; set; }

    public HotkeyResult? Press()
    {
        if (Mode == RecordingMode.Toggle)
        {
            return queue.HandleHotkey(captureTarget);
        }

        if (held)
        {
            return null;
        }

        held = true;
        var result = queue.HandleHotkey(captureTarget);
        accepted = result is HotkeyResult.Started or HotkeyResult.StartPending;
        return result;
    }

    public void Release()
    {
        if (!held)
        {
            return;
        }

        held = false;
        if (accepted)
        {
            accepted = false;
            queue.StopOrCancelPendingRecording(captureTarget);
        }
    }
}
