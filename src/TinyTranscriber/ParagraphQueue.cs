namespace TinyTranscriber;

internal interface IParagraphRecorder : IDisposable
{
    void Start();
    Task<string> StopAsync();
}

internal interface IParagraphDelivery
{
    Task<bool> TryDeliverAsync(nint target, string text, CancellationToken cancellationToken);
    void Copy(string text);
}

internal enum MicrophoneState
{
    Idle,
    Recording,
    Stopping
}

internal enum HotkeyResult
{
    Started,
    Stopping,
    StartPending,
    AlreadyStopping,
    Full,
    Failed,
    Exiting
}

internal sealed record ParagraphQueueStatus(
    MicrophoneState Microphone,
    int Unfinished,
    int Capacity,
    bool IsTranscribing,
    bool DeliveryPaused,
    bool CanCopy,
    bool CanAcknowledgeCopy,
    bool StartPending);

// All commands and continuations run on the owning (UI) synchronization context.
internal sealed class ParagraphQueue(
    Func<IParagraphRecorder> createRecorder,
    Func<string, CancellationToken, Task<string>> transcribe,
    IParagraphDelivery delivery,
    Action<string> deleteClip,
    int capacity = ParagraphQueue.DefaultCapacity)
{
    public const int DefaultCapacity = 5;
    private string separator = DictationPreferences.Default.SeparatorText;
    private readonly int capacity = capacity > 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly List<Paragraph> paragraphs = [];
    private readonly CancellationTokenSource shutdown = new();
    private CancellationTokenSource? deliveryAttempt;
    private IParagraphRecorder? recorder;
    private Paragraph? recording;
    private Paragraph? nextRecording;
    private Task stoppingTask = Task.CompletedTask;
    private Task processingTask = Task.CompletedTask;
    private Task deliveryTask = Task.CompletedTask;
    private bool processing;
    private bool delivering;
    private bool paused;
    private bool exiting;
    private long burst;
    private (long Burst, nint Target)? lastDelivered;
    private Paragraph[] copied = [];
    private Task? shutdownTask;

    public event Action? Changed;
    public event Action<string, string>? Error;

    public ParagraphQueueStatus Status => new(
        recording?.Stage switch
        {
            Stage.Recording => MicrophoneState.Recording,
            Stage.Stopping => MicrophoneState.Stopping,
            _ => MicrophoneState.Idle
        },
        paragraphs.Count,
        capacity,
        paragraphs.Any(item => item.Stage == Stage.Transcribing),
        paused,
        paragraphs.FirstOrDefault()?.Stage == Stage.Ready,
        copied.Length > 0,
        nextRecording is not null);

    public HotkeyResult HandleHotkey(Func<nint> captureTarget)
    {
        if (exiting)
        {
            return HotkeyResult.Exiting;
        }

        if (recording?.Stage == Stage.Recording)
        {
            recording.Target = captureTarget();
            recording.Stage = Stage.Stopping;
            Notify();
            stoppingTask = FinishRecordingAsync(recording, recorder!);
            return HotkeyResult.Stopping;
        }

        if (nextRecording is not null)
        {
            return HotkeyResult.AlreadyStopping;
        }

        if (paragraphs.Count == capacity)
        {
            return HotkeyResult.Full;
        }

        if (paragraphs.Count == 0)
        {
            burst++;
            lastDelivered = null;
        }

        var item = new Paragraph(burst, separator);
        paragraphs.Add(item);
        if (recording is not null)
        {
            nextRecording = item;
            Notify();
            return HotkeyResult.StartPending;
        }

        return StartRecording(item) ? HotkeyResult.Started : HotkeyResult.Failed;
    }

    public void SetSeparator(DictationSeparator choice)
    {
        separator = new DictationPreferences(choice).SeparatorText;
    }

    public void StopOrCancelPendingRecording(Func<nint> captureTarget)
    {
        if (exiting)
        {
            return;
        }

        if (nextRecording is not null)
        {
            paragraphs.Remove(nextRecording);
            nextRecording = null;
            Notify();
            Report("Recording not started", new InvalidOperationException(
                "The previous recording was still stopping. Hold the shortcut again when the microphone is ready."));
        }
        else if (recording?.Stage == Stage.Recording)
        {
            HandleHotkey(captureTarget);
        }
    }

    public void PauseDelivery()
    {
        if (exiting)
        {
            return;
        }

        paused = true;
        deliveryAttempt?.Cancel();
        Notify();
    }

    public void ResumeDelivery()
    {
        if (exiting || copied.Length > 0)
        {
            return;
        }

        paused = false;
        Notify();
        Pump();
    }

    public void CopyReadyParagraphs()
    {
        if (exiting)
        {
            return;
        }

        PauseDelivery();
        var ready = paragraphs.TakeWhile(item => item.Stage == Stage.Ready).ToArray();
        if (ready.Length == 0)
        {
            return;
        }

        try
        {
            // Manual recovery is a fresh block, never a leading blank paragraph.
            delivery.Copy(string.Concat(ready.Select((item, index) =>
                (index == 0 ? string.Empty : item.Separator) + item.Text)));
            copied = ready;
            Notify();
        }
        catch (Exception exception)
        {
            Report("Could not copy paragraphs", exception);
        }
    }

    public void AcknowledgeCopiedParagraphs()
    {
        if (exiting)
        {
            return;
        }

        foreach (var item in copied)
        {
            paragraphs.Remove(item);
        }

        copied = [];
        lastDelivered = null;
        Notify();
        Pump();
    }

    public Task ShutdownAsync() => shutdownTask ??= ShutdownCoreAsync();

    private bool StartRecording(Paragraph item)
    {
        try
        {
            recorder = createRecorder();
            recording = item;
            recorder.Start();
            item.Stage = Stage.Recording;
            Notify();
            return true;
        }
        catch (Exception exception)
        {
            ReleaseRecorder(recorder);
            recorder = null;
            recording = null;
            paragraphs.Remove(item);
            Notify();
            Report("Could not start recording", exception);
            return false;
        }
    }

    private async Task FinishRecordingAsync(Paragraph item, IParagraphRecorder source)
    {
        try
        {
            item.AudioPath = await source.StopAsync();
            item.Stage = Stage.Queued;
        }
        catch (Exception exception)
        {
            paragraphs.Remove(item);
            if (!exiting)
            {
                Report("Recording failed", exception);
            }
        }
        finally
        {
            ReleaseRecorder(source);
            recorder = null;
            recording = null;
            if (!exiting)
            {
                var next = nextRecording;
                nextRecording = null;
                if (next is not null)
                {
                    StartRecording(next);
                }

                Notify();
                Pump();
            }
        }
    }

    private void Pump()
    {
        if (exiting)
        {
            return;
        }

        if (!processing)
        {
            processing = true;
            processingTask = ProcessAsync();
        }

        if (!delivering && !paused)
        {
            delivering = true;
            deliveryTask = DeliverAsync();
        }
    }

    private async Task ProcessAsync()
    {
        try
        {
            while (!exiting)
            {
                var item = paragraphs.FirstOrDefault(item => item.Stage != Stage.Ready);
                if (item?.Stage != Stage.Queued)
                {
                    break;
                }

                try
                {
                    item.Stage = Stage.Transcribing;
                    Notify();
                    item.Text = await transcribe(item.AudioPath!, shutdown.Token);

                    if (exiting)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(item.Text))
                    {
                        throw new InvalidOperationException("No speech was recognized.");
                    }
                }
                catch (Exception exception)
                {
                    if (exiting)
                    {
                        break;
                    }

                    paragraphs.Remove(item);
                    CleanupAudio(item);
                    Report("Dictation failed", new InvalidOperationException(
                        $"This paragraph was not transcribed. You can record again.\n{exception.Message}", exception));
                    continue;
                }
                finally
                {
                    Notify();
                }

                CleanupAudio(item);
                item.Stage = Stage.Ready;
                Notify();
                if (!delivering && !paused)
                {
                    delivering = true;
                    deliveryTask = DeliverAsync();
                }
            }
        }
        finally
        {
            processing = false;
        }
    }

    private async Task DeliverAsync()
    {
        try
        {
            while (!exiting && !paused && paragraphs.FirstOrDefault() is { Stage: Stage.Ready } item)
            {
                deliveryAttempt = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                try
                {
                    var prefix = lastDelivered == (item.Burst, item.Target) ? item.Separator : string.Empty;
                    if (!await delivery.TryDeliverAsync(item.Target, prefix + item.Text, deliveryAttempt.Token))
                    {
                        throw new InvalidOperationException(
                            "The original destination must already be foreground, with hotkey modifiers released. "
                            + "Nothing further will be pasted until you resume or copy from the tray.");
                    }

                    if (exiting)
                    {
                        break;
                    }

                    paragraphs.RemoveAt(0);
                    lastDelivered = (item.Burst, item.Target);
                    Notify();
                }
                catch (OperationCanceledException) when (deliveryAttempt.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    if (!exiting)
                    {
                        paused = true;
                        Notify();
                        Report("Delivery paused", exception);
                    }

                    break;
                }
                finally
                {
                    deliveryAttempt.Dispose();
                    deliveryAttempt = null;
                }
            }
        }
        finally
        {
            delivering = false;
            // Resume may have happened while a canceled attempt was winding down.
            if (!paused && !exiting && paragraphs.FirstOrDefault()?.Stage == Stage.Ready)
            {
                Pump();
            }
        }
    }

    private async Task ShutdownCoreAsync()
    {
        exiting = true;
        shutdown.Cancel();
        ReleaseRecorder(recorder);
        await Task.WhenAll(stoppingTask, processingTask, deliveryTask);
        foreach (var item in paragraphs)
        {
            CleanupAudio(item);
        }

        paragraphs.Clear();
        copied = [];
        recorder = null;
        recording = null;
        nextRecording = null;
        shutdown.Dispose();
    }

    private void ReleaseRecorder(IParagraphRecorder? source)
    {
        try
        {
            source?.Dispose();
        }
        catch (Exception exception)
        {
            if (exiting)
            {
                Console.Error.WriteLine($"Microphone cleanup failed: {exception.Message}");
            }
            else
            {
                Report("Microphone cleanup failed", exception);
            }
        }
    }

    private void CleanupAudio(Paragraph item)
    {
        if (item.AudioPath is not null)
        {
            try
            {
                deleteClip(item.AudioPath);
                item.AudioPath = null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var message = $"Could not delete owned temporary recording {item.AudioPath}: {exception.Message}";
                Console.Error.WriteLine(message);
                Report("Recording cleanup failed", new IOException(message, exception));
            }
        }
    }

    private void Notify()
    {
        if (!exiting)
        {
            Changed?.Invoke();
        }
    }

    private void Report(string title, Exception exception)
    {
        if (!exiting)
        {
            Error?.Invoke(title, exception.Message);
        }
    }

    private enum Stage { Reserved, Recording, Stopping, Queued, Transcribing, Ready }

    private sealed class Paragraph(long burst, string separator)
    {
        public long Burst { get; } = burst;
        public string Separator { get; } = separator;
        public Stage Stage { get; set; }
        public nint Target { get; set; }
        public string? AudioPath { get; set; }
        public string? Text { get; set; }
    }
}
