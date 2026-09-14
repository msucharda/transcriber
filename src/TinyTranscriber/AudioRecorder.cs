using NAudio.Wave;

namespace TinyTranscriber;

internal sealed class AudioRecorder(
    Func<IWaveIn>? createInput = null,
    string? temporaryDirectory = null) : IParagraphRecorder
{
    private readonly object gate = new();
    private Capture? capture;
    private Capture? finishedCapture;
    private bool disposed;

    public bool IsRecording
    {
        get { lock (gate) { return capture is not null; } }
    }

    public event Action<float>? LevelChanged;

    public void Start()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (capture is not null || finishedCapture is not null)
            {
                throw new InvalidOperationException("Recording is already in progress.");
            }

            var input = createInput?.Invoke() ?? new WaveInEvent { BufferMilliseconds = 100 };
            var path = Path.Combine(
                temporaryDirectory ?? Path.GetTempPath(), $"tiny-transcriber-{Guid.NewGuid():N}.wav");
            try
            {
                input.WaveFormat = new WaveFormat(16000, 16, 1);
                capture = new Capture(input, new WaveFileWriter(path, input.WaveFormat), path);
                input.DataAvailable += OnDataAvailable;
                input.RecordingStopped += OnRecordingStopped;
                input.StartRecording();
            }
            catch
            {
                if (capture is not null)
                {
                    Detach(capture);
                    capture.Writer.Dispose();
                    capture.Completion.TrySetCanceled();
                    capture = null;
                }

                input.Dispose();
                File.Delete(path);
                throw;
            }
        }
    }

    public Task<string> StopAsync()
    {
        Capture current;
        lock (gate)
        {
            if (finishedCapture is not null)
            {
                var finished = finishedCapture.Completion.Task;
                finishedCapture = null;
                return finished;
            }

            current = capture ?? throw new InvalidOperationException("No recording is in progress.");
            if (current.StopRequested)
            {
                return current.Completion.Task;
            }

            current.StopRequested = true;
        }

        var completion = current.Completion.Task;
        current.Input.StopRecording();
        return completion;
    }

    public void Dispose()
    {
        Capture? current;
        Capture? finished;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            current = capture;
            finished = finishedCapture;
            capture = null;
            finishedCapture = null;
            if (current is not null)
            {
                Detach(current);
                current.Completion.TrySetCanceled();
                current.Writer.Dispose();
            }
        }

        if (finished is not null)
        {
            File.Delete(finished.Path);
        }

        if (current is not null)
        {
            try
            {
                current.Input.Dispose();
            }
            finally
            {
                File.Delete(current.Path);
            }
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        Capture current;
        lock (gate)
        {
            if (capture is null || sender != capture.Input)
            {
                return;
            }

            current = capture;
            try
            {
                current.Writer.Write(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
                current.Writer.Flush();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                current.Failure = exception;
            }
        }

        if (current.Failure is not null)
        {
            current.Input.StopRecording();
            return;
        }

        LevelChanged?.Invoke(
            AudioLevelCalculator.Calculate(eventArgs.Buffer.AsSpan(0, eventArgs.BytesRecorded)));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        Capture current;
        lock (gate)
        {
            if (capture is null || sender != capture.Input)
            {
                return;
            }

            current = capture;
            capture = null;
            if (!current.StopRequested)
            {
                finishedCapture = current;
            }

            Detach(current);
        }

        try
        {
            current.Writer.Dispose();
            current.Input.Dispose();
            var failure = current.Failure ?? eventArgs.Exception;
            if (failure is not null)
            {
                throw failure;
            }

            // The completed task now owns this WAV, not this recorder or a later capture.
            current.Completion.TrySetResult(current.Path);
        }
        catch (Exception exception)
        {
            try
            {
                File.Delete(current.Path);
            }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
            {
                exception = new AggregateException(exception, cleanupException);
            }

            current.Completion.TrySetException(exception);
        }
    }

    private void Detach(Capture current)
    {
        current.Input.DataAvailable -= OnDataAvailable;
        current.Input.RecordingStopped -= OnRecordingStopped;
    }

    private sealed class Capture(IWaveIn input, WaveFileWriter writer, string path)
    {
        public IWaveIn Input { get; } = input;
        public WaveFileWriter Writer { get; } = writer;
        public string Path { get; } = path;
        public bool StopRequested { get; set; }
        public Exception? Failure { get; set; }
        public TaskCompletionSource<string> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
