using NAudio.Wave;

namespace TinyTranscriber;

internal sealed class AudioRecorder : IDisposable
{
    private WaveInEvent? waveIn;
    private WaveFileWriter? writer;
    private TaskCompletionSource<string>? stopped;
    private string? outputPath;

    public bool IsRecording => waveIn is not null;

    public event Action<float>? LevelChanged;

    public void Start()
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("Recording is already in progress.");
        }

        outputPath = Path.Combine(Path.GetTempPath(), $"tiny-transcriber-{Guid.NewGuid():N}.wav");
        writer = new WaveFileWriter(outputPath, new WaveFormat(16000, 16, 1));
        stopped = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        waveIn = new WaveInEvent
        {
            WaveFormat = writer.WaveFormat,
            BufferMilliseconds = 100
        };
        waveIn.DataAvailable += OnDataAvailable;
        waveIn.RecordingStopped += OnRecordingStopped;

        try
        {
            waveIn.StartRecording();
        }
        catch
        {
            CleanupRecording();
            DeleteOutput();
            throw;
        }
    }

    public Task<string> StopAsync()
    {
        if (waveIn is null || stopped is null)
        {
            throw new InvalidOperationException("No recording is in progress.");
        }

        waveIn.StopRecording();
        return stopped.Task;
    }

    public void Dispose()
    {
        if (waveIn is not null)
        {
            waveIn.StopRecording();
        }

        CleanupRecording();
        DeleteOutput();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        writer?.Write(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
        writer?.Flush();
        LevelChanged?.Invoke(
            AudioLevelCalculator.Calculate(eventArgs.Buffer.AsSpan(0, eventArgs.BytesRecorded)));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        var path = outputPath;
        var completion = stopped;
        CleanupRecording();

        if (completion is null || path is null)
        {
            return;
        }

        if (eventArgs.Exception is not null)
        {
            DeleteOutput();
            completion.TrySetException(eventArgs.Exception);
            return;
        }

        completion.TrySetResult(path);
    }

    private void CleanupRecording()
    {
        if (waveIn is not null)
        {
            waveIn.DataAvailable -= OnDataAvailable;
            waveIn.RecordingStopped -= OnRecordingStopped;
            waveIn.Dispose();
            waveIn = null;
        }

        writer?.Dispose();
        writer = null;
        stopped = null;
    }

    private void DeleteOutput()
    {
        if (outputPath is not null && File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        outputPath = null;
    }
}
