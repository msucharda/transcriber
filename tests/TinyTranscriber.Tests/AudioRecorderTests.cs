using NAudio.Wave;

namespace TinyTranscriber.Tests;

public sealed class AudioRecorderTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("tiny-transcriber-tests-");

    [Fact]
    public async Task SynchronousStopTransfersFinalizedWavAndDisposeCannotDeleteIt()
    {
        var input = new FakeWaveIn();
        using var recorder = Create(input);
        recorder.Start();
        input.EmitData();
        var path = await recorder.StopAsync();
        recorder.Dispose();
        Assert.True(File.Exists(path));
        using var reader = new WaveFileReader(path);
        Assert.Equal(16000, reader.WaveFormat.SampleRate);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.Equal(8, reader.Length);
        Assert.True(input.Disposed);
    }

    [Fact]
    public async Task DisposingSecondCaptureOnlyDeletesItsOwnWav()
    {
        var first = new FakeWaveIn();
        var second = new FakeWaveIn();
        var inputs = new Queue<FakeWaveIn>([first, second]);
        using var recorder = new AudioRecorder(() => inputs.Dequeue(), directory.FullName);
        recorder.Start();
        var firstPath = await recorder.StopAsync();
        recorder.Start();
        Assert.Equal(2, directory.GetFiles().Length);
        recorder.Dispose();
        Assert.Equal(firstPath, Assert.Single(directory.GetFiles()).FullName);
    }

    [Fact]
    public async Task DisposeCancelsPendingStopAndLateDeviceEventsCannotTransferDeletedAudio()
    {
        var input = new FakeWaveIn { CompleteOnStop = false };
        using var recorder = Create(input);
        recorder.Start();
        var stop = recorder.StopAsync();
        Assert.Same(stop, recorder.StopAsync());
        Assert.Equal(1, input.Stops);
        recorder.Dispose();
        input.Complete();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stop);
        Assert.Empty(directory.GetFiles());
    }

    [Fact]
    public async Task FailedCaptureSurfacesErrorAndDeletesOnlyOwnedAudio()
    {
        var input = new FakeWaveIn { Failure = new IOException("Capture failed.") };
        using var recorder = Create(input);
        recorder.Start();
        await Assert.ThrowsAsync<IOException>(() => recorder.StopAsync());
        Assert.Empty(directory.GetFiles());
    }

    [Fact]
    public void StartFailureCleansOwnedFileAndDevice()
    {
        var input = new FakeWaveIn { FailStart = true };
        using var recorder = Create(input);
        Assert.Throws<InvalidOperationException>(recorder.Start);
        Assert.Empty(directory.GetFiles());
        Assert.True(input.Disposed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnexpectedDeviceStopRetainsOwnershipUntilStopOrDispose(bool consume)
    {
        var input = new FakeWaveIn();
        using var recorder = Create(input);
        recorder.Start();
        input.Complete();
        Assert.Single(directory.GetFiles());
        if (consume)
        {
            var path = await recorder.StopAsync();
            recorder.Dispose();
            Assert.True(File.Exists(path));
        }
        else
        {
            recorder.Dispose();
            Assert.Empty(directory.GetFiles());
        }
    }

    private AudioRecorder Create(FakeWaveIn input) => new(() => input, directory.FullName);
    public void Dispose() => directory.Delete(recursive: true);

    private sealed class FakeWaveIn : IWaveIn
    {
        public WaveFormat WaveFormat { get; set; } = new(16000, 16, 1);
        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;
        public bool CompleteOnStop { get; init; } = true;
        public bool FailStart { get; init; }
        public Exception? Failure { get; init; }
        public bool Disposed { get; private set; }
        public int Stops { get; private set; }
        public void StartRecording()
        {
            if (FailStart) { throw new InvalidOperationException("Device unavailable."); }
        }

        public void StopRecording()
        {
            Stops++;
            if (CompleteOnStop) { Complete(); }
        }

        public void EmitData() => DataAvailable?.Invoke(this, new WaveInEventArgs(new byte[8], 8));
        public void Complete() => RecordingStopped?.Invoke(this, new StoppedEventArgs(Failure));
        public void Dispose() => Disposed = true;
    }
}
