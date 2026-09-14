namespace TinyTranscriber;

internal sealed record DictationPresentation(
    string Title,
    string Detail,
    bool Recording = false,
    bool Processing = false,
    bool BackgroundTranscription = false,
    bool Visible = true)
{
    public static DictationPresentation From(ParagraphQueueStatus status, string shortcut, bool resuming = false)
    {
        var count = $"{status.Unfinished}/{status.Capacity}";
        var blocked = status.DeliveryPaused || status.HasFailure;
        if (status.Microphone != MicrophoneState.Idle)
        {
            var detail = status.Microphone == MicrophoneState.Stopping
                ? status.StartPending ? "Next paragraph starts shortly" : "Finishing microphone capture"
                : status.HasFailure ? "Previous failed - recover from tray"
                : status.DeliveryPaused ? "Delivery paused - recover from tray"
                : $"{shortcut} to stop";
            return new($"Listening  {count}", detail, Recording: true,
                BackgroundTranscription: status.IsTranscribing && !blocked);
        }

        if (status.HasFailure)
        {
            return new("Paragraph failed", $"{count} occupied - retry or discard in tray");
        }

        if (status.DeliveryPaused)
        {
            return new(resuming ? "Resume in 3 seconds" : "Delivery paused",
                resuming ? "Select the original destination"
                : status.CanAcknowledgeCopy ? "Paste, then acknowledge in tray"
                : $"{count} occupied - recover from tray");
        }

        if (status.IsTranscribing)
        {
            return new("Transcribing", status.Unfinished == status.Capacity
                ? $"{count} occupied - waiting for a slot"
                : $"{shortcut} for next paragraph", Processing: true);
        }

        return status.Unfinished > 0
            ? new("Delivering", $"{count} occupied - checking destination")
            : new("Ready", $"{shortcut} to record", Visible: false);
    }
}
