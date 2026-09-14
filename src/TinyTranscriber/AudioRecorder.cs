using NAudio.Wave;

namespace TinyTranscriber;

internal sealed class AudioRecorder(
    Func<IWaveIn>? createInput = null,
    string? temporaryDirectory = null) : IParagraphRecorder
{
    private readonly object gate = new();
    private Capture? capture;
    private Capture? closingCapture;
    private Capture? finishedCapture;
    private volatile bool disposed;

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
            if (capture is not null || closingCapture is not null || finishedCapture is not null)
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
            catch (Exception exception)
            {
                Exception? cleanupFailure;
                if (capture is not null)
                {
                    Detach(capture);
                    capture.Completion.TrySetCanceled();
                    cleanupFailure = ReleaseCapture(capture, deleteAudio: true);
                    capture = null;
                }
                else
                {
                    cleanupFailure = TryCleanup(input.Dispose);
                    cleanupFailure = TryCleanup(() => File.Delete(path), cleanupFailure);
                }

                if (cleanupFailure is not null)
                {
                    throw new AggregateException(exception, cleanupFailure);
                }

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

            if (closingCapture is not null)
            {
                closingCapture.StopRequested = true;
                return closingCapture.Completion.Task;
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
            }
        }

        var failure = current is not null ? ReleaseCapture(current, deleteAudio: true) : null;
        if (finished is not null)
        {
            failure = TryCleanup(() => File.Delete(finished.Path), failure);
        }

        if (failure is not null)
        {
            throw failure;
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
            try
            {
                current.Input.StopRecording();
            }
            catch (InvalidOperationException) when (disposed)
            {
                // Shutdown already released this capture.
            }

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
            closingCapture = current;
            Detach(current);
        }

        var failure = ReleaseCapture(current, deleteAudio: false, current.Failure ?? eventArgs.Exception);
        if (failure is not null)
        {
            failure = TryCleanup(() => File.Delete(current.Path), failure);
        }

        lock (gate)
        {
            closingCapture = null;
            if (!current.StopRequested)
            {
                if (disposed)
                {
                    failure = TryCleanup(() => File.Delete(current.Path), failure);
                }
                else
                {
                    finishedCapture = current;
                }
            }

            if (failure is not null)
            {
                if (disposed && !current.StopRequested)
                {
                    Console.Error.WriteLine($"Recording cleanup failed: {failure.Message}");
                }

                current.Completion.TrySetException(failure);
            }
            else
            {
                // A requested stop transfers ownership through its task, never to a later capture.
                current.Completion.TrySetResult(current.Path);
            }
        }
    }

    private static Exception? ReleaseCapture(Capture current, bool deleteAudio, Exception? failure = null)
    {
        failure = TryCleanup(current.Writer.Dispose, failure);
        failure = TryCleanup(current.Input.Dispose, failure);
        return deleteAudio ? TryCleanup(() => File.Delete(current.Path), failure) : failure;
    }

    private static Exception? TryCleanup(Action cleanup, Exception? failure = null)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            return failure is null ? exception : new AggregateException(failure, exception);
        }

        return failure;
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
