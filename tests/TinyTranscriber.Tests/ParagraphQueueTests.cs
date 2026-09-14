namespace TinyTranscriber.Tests;

public sealed class ParagraphQueueTests
{
    private const nint Target = 123;

    [Fact]
    public async Task RecordingOverlapsNetworkButRequestsAndDeliveryStayFifo()
    {
        await using var fixture = new Fixture();
        fixture.StartAndStop();
        await Until(() => fixture.Requests.Count == 1);

        Assert.Equal(HotkeyResult.Started, fixture.Press());
        Assert.Equal(MicrophoneState.Recording, fixture.Queue.Status.Microphone);
        Assert.True(fixture.Queue.Status.IsTranscribing);
        Assert.Equal(2, fixture.Queue.Status.Unfinished);
        fixture.Press();
        await Until(() => fixture.Queue.Status.Microphone == MicrophoneState.Idle);
        Assert.Equal(HotkeyResult.Full, fixture.Press());
        Assert.Single(fixture.Requests);

        fixture.Requests[0].Complete("First.");
        await Until(() => fixture.Requests.Count == 2);
        fixture.Requests[1].Complete("Second.");
        await Until(() => fixture.Queue.Status.Unfinished == 0);

        Assert.Equal(["clip-1", "clip-2"], fixture.Requests.Select(request => request.Path));
        Assert.Equal(["First.", " Second."], fixture.Delivery.Pastes.Select(paste => paste.Text));
        Assert.Equal(1, fixture.MaxRequests);
        Assert.Equal(["clip-1", "clip-2"], fixture.Deleted);
    }

    [Fact]
    public async Task OldCompletionCannotResetOrDisposeNewRecording()
    {
        await using var fixture = new Fixture();
        fixture.StartAndStop();
        fixture.Press();
        var second = fixture.Recorders[1];
        fixture.Requests[0].Complete("First.");
        await Until(() => fixture.Queue.Status.Unfinished == 1);
        Assert.Equal(MicrophoneState.Recording, fixture.Queue.Status.Microphone);
        Assert.False(fixture.Queue.Status.IsTranscribing);
        Assert.False(second.Disposed);
        Assert.Equal(["clip-1"], fixture.Deleted);
    }

    [Fact]
    public async Task StoppingReservesOneRestartAndIgnoresFurtherRepeats()
    {
        await using var fixture = new Fixture { DelayStop = true };
        Assert.Equal(HotkeyResult.Started, fixture.Press());
        Assert.Equal(HotkeyResult.Stopping, fixture.Press());
        Assert.Equal(HotkeyResult.StartPending, fixture.Press());
        Assert.Equal(HotkeyResult.AlreadyStopping, fixture.Press());
        Assert.Equal(2, fixture.Queue.Status.Unfinished);
        Assert.Single(fixture.Recorders);
        Assert.Single(fixture.Recorders).FinishStop();
        await Until(() => fixture.Recorders.Count == 2);
        Assert.Equal(MicrophoneState.Recording, fixture.Queue.Status.Microphone);
        Assert.False(fixture.Queue.Status.StartPending);
        Assert.Equal(1, fixture.Recorders[0].Stops);
        Assert.True(fixture.Recorders[0].Disposed);
    }

    [Fact]
    public async Task TargetIsCapturedAtStopNotAtStartOrAfterStopping()
    {
        await using var fixture = new Fixture { DelayStop = true };
        nint target = 100;
        var reads = 0;
        nint Capture() { reads++; return target; }
        fixture.Queue.HandleHotkey(Capture);
        Assert.Equal(0, reads);
        target = Target;
        fixture.Queue.HandleHotkey(Capture);
        target = 456;
        fixture.Recorders[0].FinishStop();
        await Until(() => fixture.Requests.Count == 1);
        fixture.Requests[0].Complete("Exact text.");
        await Until(() => fixture.Delivery.Pastes.Count == 1);
        Assert.Equal(Target, fixture.Delivery.Pastes[0].Target);
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task StandaloneBurstsAndDifferentTargetsHaveNoLeadingSeparator()
    {
        await using var fixture = new Fixture();
        fixture.StartAndStop();
        fixture.Requests[0].Complete("One.");
        await Until(() => fixture.Queue.Status.Unfinished == 0);
        fixture.StartAndStop();
        fixture.Press();
        fixture.Queue.HandleHotkey(() => 456);
        fixture.Requests[1].Complete("Two.");
        await Until(() => fixture.Requests.Count == 3);
        fixture.Requests[2].Complete("Three.");
        await Until(() => fixture.Queue.Status.Unfinished == 0);
        Assert.Equal(["One.", "Two.", "Three."], fixture.Delivery.Pastes.Select(paste => paste.Text));
    }

    [Fact]
    public async Task FailedDeliveryKeepsOrderedResultsAndClipboardUntilExplicitRecovery()
    {
        await using var fixture = new Fixture();
        fixture.Delivery.AllowPaste = false;
        fixture.StartAndStop();
        fixture.StartAndStop();
        fixture.Requests[0].Complete("One.");
        await Until(() => fixture.Queue.Status.DeliveryPaused && fixture.Requests.Count == 2);
        fixture.Requests[1].Complete("Two.");
        await Until(() => !fixture.Queue.Status.IsTranscribing);
        Assert.Equal("original clipboard", fixture.Delivery.Clipboard);
        Assert.Empty(fixture.Delivery.Pastes);
        Assert.Single(fixture.Errors);
        Assert.Equal(HotkeyResult.Full, fixture.Press());

        fixture.Queue.CopyReadyParagraphs();
        Assert.Equal("One. Two.", fixture.Delivery.Clipboard);
        Assert.Equal(2, fixture.Queue.Status.Unfinished);
        fixture.Delivery.AllowPaste = true;
        fixture.Queue.ResumeDelivery();
        Assert.Empty(fixture.Delivery.Pastes);
        fixture.Queue.AcknowledgeCopiedParagraphs();
        Assert.Equal(0, fixture.Queue.Status.Unfinished);
        Assert.True(fixture.Queue.Status.DeliveryPaused);
        fixture.StartAndStop();
        fixture.Requests[2].Complete("Three.");
        await Until(() => fixture.Queue.Status.CanCopy);
        Assert.Equal("One. Two.", fixture.Delivery.Clipboard);
        fixture.Queue.ResumeDelivery();
        await Until(() => fixture.Queue.Status.Unfinished == 0);
        Assert.Equal("Three.", Assert.Single(fixture.Delivery.Pastes).Text);
    }

    [Fact]
    public async Task CopyAcknowledgesOnlyTheSnapshotNotLaterCompletion()
    {
        await using var fixture = new Fixture();
        fixture.Queue.PauseDelivery();
        fixture.StartAndStop();
        fixture.StartAndStop();
        fixture.Requests[0].Complete("One.");
        await Until(() => fixture.Queue.Status.CanCopy);
        fixture.Queue.CopyReadyParagraphs();
        fixture.Requests[1].Complete("Two.");
        await Until(() => !fixture.Queue.Status.IsTranscribing);
        fixture.Queue.AcknowledgeCopiedParagraphs();
        Assert.Equal(1, fixture.Queue.Status.Unfinished);
        Assert.Equal("One.", fixture.Delivery.Clipboard);
        Assert.True(fixture.Queue.Status.DeliveryPaused);
        fixture.Queue.CopyReadyParagraphs();
        Assert.Equal("Two.", fixture.Delivery.Clipboard);
    }

    [Fact]
    public async Task CopyCancelsAnInFlightDeliveryBeforeClipboardOrInput()
    {
        await using var fixture = new Fixture();
        fixture.Delivery.DelayPaste = true;
        fixture.StartAndStop();
        fixture.Requests[0].Complete("One.");
        await Until(() => fixture.Delivery.Attempts == 1);
        fixture.Queue.CopyReadyParagraphs();
        fixture.Delivery.ReleasePaste();
        await Until(() => fixture.Delivery.Canceled == 1);
        Assert.Empty(fixture.Delivery.Pastes);
        Assert.Equal("One.", fixture.Delivery.Clipboard);
        Assert.True(fixture.Queue.Status.CanAcknowledgeCopy);
    }

    [Fact]
    public async Task ClipboardAndInputExceptionsPauseWithoutLosingText()
    {
        await using var fixture = new Fixture();
        fixture.Delivery.Failure = new InvalidOperationException("Clipboard busy.");
        fixture.StartAndStop();
        fixture.Requests[0].Complete("One.");
        await Until(() => fixture.Queue.Status.DeliveryPaused);
        fixture.Queue.CopyReadyParagraphs();
        Assert.False(fixture.Queue.Status.CanAcknowledgeCopy);
        Assert.Equal(1, fixture.Queue.Status.Unfinished);
        Assert.Equal(2, fixture.Errors.Count);
        fixture.Delivery.Failure = null;
        fixture.Queue.ResumeDelivery();
        await Until(() => fixture.Queue.Status.Unfinished == 0);
        Assert.Equal("One.", Assert.Single(fixture.Delivery.Pastes).Text);
    }

    [Fact]
    public async Task FailedTranscriptionReleasesSlotAndAutomaticallyProcessesNextParagraph()
    {
        await using var fixture = new Fixture();
        fixture.StartAndStop();
        fixture.StartAndStop();
        fixture.Requests[0].Fail();
        await Until(() => fixture.Requests.Count == 2);
        Assert.Equal(["clip-1"], fixture.Deleted);
        Assert.Equal(1, fixture.Queue.Status.Unfinished);
        Assert.False(fixture.Queue.Status.DeliveryPaused);
        Assert.Equal("original clipboard", fixture.Delivery.Clipboard);
        Assert.Contains("You can record again.", Assert.Single(fixture.Errors));
        Assert.Equal("clip-2", fixture.Requests[1].Path);
        fixture.Requests[1].Complete("Next.");
        await Until(() => fixture.Queue.Status.Unfinished == 0);
        Assert.Equal("Next.", Assert.Single(fixture.Delivery.Pastes).Text);
        Assert.Equal(1, fixture.MaxRequests);
        Assert.Equal(HotkeyResult.Started, fixture.Press());
    }

    [Fact]
    public async Task RepeatedFailedDictationsReturnToReadyWithoutRestartOrClipboardChanges()
    {
        await using var fixture = new Fixture();
        fixture.StartAndStop();
        fixture.StartAndStop();
        fixture.Requests[0].Fail();
        await Until(() => fixture.Requests.Count == 2);
        fixture.Requests[1].Fail();
        await Until(() => fixture.Queue.Status.Unfinished == 0);
        Assert.Equal(["clip-1", "clip-2"], fixture.Deleted);
        Assert.Equal(2, fixture.Errors.Count);
        Assert.False(fixture.Queue.Status.DeliveryPaused);
        Assert.False(DictationPresentation.From(fixture.Queue.Status, "Ctrl+Shift+Space").Visible);
        Assert.Empty(fixture.Delivery.Pastes);
        Assert.Equal("original clipboard", fixture.Delivery.Clipboard);
        fixture.StartAndStop();
        fixture.Requests[2].Complete("Fresh dictation.");
        await Until(() => fixture.Queue.Status.Unfinished == 0);
        Assert.Equal("Fresh dictation.", Assert.Single(fixture.Delivery.Pastes).Text);
    }

    [Fact]
    public async Task CleanupFailureIsReportedWithoutBlockingValidTextOrNewRecording()
    {
        await using var fixture = new Fixture { FailDelete = true };
        fixture.StartAndStop();
        fixture.Requests[0].Complete("One.");
        await Until(() => fixture.Queue.Status.Unfinished == 0);
        Assert.Single(fixture.Requests);
        Assert.Equal("One.", Assert.Single(fixture.Delivery.Pastes).Text);
        Assert.Contains("Recording cleanup failed", Assert.Single(fixture.Errors));
        Assert.Equal(HotkeyResult.Started, fixture.Press());
    }

    [Fact]
    public async Task FailedBackgroundTranscriptionDoesNotInterruptNewerRecording()
    {
        await using var fixture = new Fixture();
        fixture.StartAndStop();
        fixture.Press();
        fixture.Requests[0].Fail();
        await Until(() => fixture.Errors.Count == 1);
        Assert.Equal(MicrophoneState.Recording, fixture.Queue.Status.Microphone);
        Assert.False(fixture.Recorders[1].Disposed);
        Assert.Equal(1, fixture.Queue.Status.Unfinished);
        var presentation = DictationPresentation.From(fixture.Queue.Status, "Ctrl+Shift+Space");
        Assert.True(presentation.Recording);
        Assert.False(presentation.BackgroundTranscription);
        Assert.Equal(["clip-1"], fixture.Deleted);
        fixture.Press();
        await Until(() => fixture.Requests.Count == 2);
        fixture.Requests[1].Complete("Still recording.");
        await Until(() => fixture.Queue.Status.Unfinished == 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n ")]
    public async Task EmptyTranscriptFreesItsSlotAndNotifiesUser(string text)
    {
        await using var fixture = new Fixture();
        fixture.StartAndStop();
        fixture.Requests[0].Complete(text);
        await Until(() => fixture.Queue.Status.Unfinished == 0);
        Assert.Contains("No speech was recognized.", Assert.Single(fixture.Errors));
        Assert.Equal(["clip-1"], fixture.Deleted);
        Assert.Equal(HotkeyResult.Started, fixture.Press());
        Assert.Equal("original clipboard", fixture.Delivery.Clipboard);
    }

    [Fact]
    public async Task RequestTimeoutIsANonblockingFailureNotAppShutdown()
    {
        await using var fixture = new Fixture();
        fixture.StartAndStop();
        fixture.Requests[0].Completion.SetCanceled();
        await Until(() => fixture.Queue.Status.Unfinished == 0);
        Assert.Single(fixture.Errors);
        Assert.Equal(["clip-1"], fixture.Deleted);
        Assert.Equal(HotkeyResult.Started, fixture.Press());
    }

    [Fact]
    public async Task FailedLaterParagraphPreservesEarlierCopiedTextAndItsPause()
    {
        await using var fixture = new Fixture();
        fixture.Queue.PauseDelivery();
        fixture.StartAndStop();
        fixture.StartAndStop();
        fixture.Requests[0].Complete("Keep this.");
        await Until(() => fixture.Requests.Count == 2);
        fixture.Queue.CopyReadyParagraphs();
        fixture.Requests[1].Fail();
        await Until(() => fixture.Errors.Count == 1);
        Assert.Equal("Keep this.", fixture.Delivery.Clipboard);
        Assert.True(fixture.Queue.Status.CanAcknowledgeCopy);
        Assert.True(fixture.Queue.Status.DeliveryPaused);
        Assert.Equal(1, fixture.Queue.Status.Unfinished);
        Assert.Equal(["clip-1", "clip-2"], fixture.Deleted);
        Assert.Equal(HotkeyResult.Started, fixture.Press());
    }

    [Fact]
    public async Task FailedTranscriptionWithCleanupErrorStillReleasesCapacity()
    {
        await using var fixture = new Fixture { FailDelete = true };
        fixture.StartAndStop();
        fixture.Requests[0].Fail();
        await Until(() => fixture.Queue.Status.Unfinished == 0);
        Assert.Equal(2, fixture.Errors.Count);
        Assert.Equal(HotkeyResult.Started, fixture.Press());
        Assert.Empty(fixture.Delivery.Pastes);
    }

    [Fact]
    public async Task StartAndStopFailuresReleaseOnlyTheirReservation()
    {
        await using var fixture = new Fixture();
        fixture.StartAndStop();
        fixture.FailStart = true;
        Assert.Equal(HotkeyResult.Failed, fixture.Press());
        Assert.Equal(1, fixture.Queue.Status.Unfinished);
        fixture.FailStart = false;
        fixture.Press();
        fixture.Recorders[^1].FailStop = true;
        fixture.Press();
        await Until(() => fixture.Queue.Status.Microphone == MicrophoneState.Idle);
        Assert.Equal(1, fixture.Queue.Status.Unfinished);
        Assert.Equal(2, fixture.Errors.Count);
        Assert.Single(fixture.Requests);
    }

    [Fact]
    public async Task ShutdownCancelsProcessingAndRecordingWithoutLateEffects()
    {
        await using var fixture = new Fixture { DelayStop = true };
        fixture.StartAndStop();
        fixture.Recorders[0].FinishStop();
        await Until(() => fixture.Requests.Count == 1);
        fixture.StartAndStop();
        var changes = 0;
        fixture.Queue.Changed += () => changes++;
        await fixture.Queue.ShutdownAsync();
        Assert.All(fixture.Recorders, recorder => Assert.True(recorder.Disposed));
        Assert.Equal(["clip-1"], fixture.Deleted);
        Assert.Empty(fixture.Delivery.Pastes);
        Assert.Empty(fixture.Errors);
        Assert.Equal(0, changes);
        Assert.Equal(HotkeyResult.Exiting, fixture.Press());
    }

    [Fact]
    public async Task ShutdownOwnsAClipWhoseStopCompletedBeforeContinuation()
    {
        await using var fixture = new Fixture { DelayStop = true };
        fixture.StartAndStop();
        fixture.Recorders[0].FinishStop();
        await fixture.Queue.ShutdownAsync();
        Assert.Equal(["clip-1"], fixture.Deleted);
        Assert.Empty(fixture.Delivery.Pastes);
    }

    [Fact]
    public async Task ShutdownCancelsDelayedDeliveryAndPreventsClipboardChanges()
    {
        await using var fixture = new Fixture();
        fixture.Delivery.DelayPaste = true;
        fixture.StartAndStop();
        fixture.Requests[0].Complete("One.");
        await Until(() => fixture.Delivery.Attempts == 1);
        await fixture.Queue.ShutdownAsync();
        Assert.Equal("original clipboard", fixture.Delivery.Clipboard);
        Assert.Empty(fixture.Delivery.Pastes);
    }

    [Fact]
    public async Task CapacityParameterIsEnforcedAndInvalidCapacityRejected()
    {
        await using var fixture = new Fixture(1);
        fixture.StartAndStop();
        Assert.Equal(HotkeyResult.Full, fixture.Press());
        Assert.Equal(2, ParagraphQueue.DefaultCapacity);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Fixture(0));
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    [Fact]
    public async Task ImmediateResumeAfterPauseWaitsForCanceledDeliveryToUnwind()
    {
        await using var fixture = new Fixture();
        fixture.Delivery.DelayPaste = true;
        fixture.StartAndStop();
        fixture.Requests[0].Complete("One.");
        await Until(() => fixture.Delivery.Attempts == 1);
        fixture.Queue.PauseDelivery();
        fixture.Queue.ResumeDelivery();
        await Until(() => fixture.Delivery.Attempts == 2);
        fixture.Delivery.ReleasePaste();
        await Until(() => fixture.Queue.Status.Unfinished == 0);
        Assert.Single(fixture.Delivery.Pastes);
        Assert.Empty(fixture.Errors);
        Assert.False(fixture.Queue.Status.DeliveryPaused);
    }

    [Theory]
    [InlineData(0, " ")]
    [InlineData(1, "\r\n")]
    [InlineData(2, "\r\n\r\n")]
    public async Task SeparatorChoiceAppliesToAutomaticDeliveryAndManualCopy(int choice, string separator)
    {
        await using var fixture = new Fixture();
        fixture.Queue.SetSeparator((DictationSeparator)choice);
        fixture.StartAndStop();
        fixture.StartAndStop();
        fixture.Requests[0].Complete("First.");
        await Until(() => fixture.Requests.Count == 2);
        fixture.Requests[1].Complete("Second.");
        await Until(() => fixture.Queue.Status.Unfinished == 0);
        Assert.Equal(["First.", separator + "Second."], fixture.Delivery.Pastes.Select(paste => paste.Text));

        fixture.Queue.PauseDelivery();
        fixture.StartAndStop();
        fixture.StartAndStop();
        fixture.Requests[2].Complete("Manual first.");
        await Until(() => fixture.Requests.Count == 4);
        fixture.Requests[3].Complete("Manual second.");
        await Until(() => !fixture.Queue.Status.IsTranscribing);
        fixture.Queue.CopyReadyParagraphs();
        Assert.Equal("Manual first." + separator + "Manual second.", fixture.Delivery.Clipboard);
    }

    [Fact]
    public async Task ChangedSeparatorOnlyAffectsRecordingsAcceptedAfterTheChange()
    {
        await using var fixture = new Fixture();
        fixture.StartAndStop();
        fixture.StartAndStop();
        fixture.Queue.SetSeparator(DictationSeparator.Paragraph);
        fixture.Requests[0].Complete("First.");
        await Until(() => fixture.Queue.Status.Unfinished == 1);
        fixture.StartAndStop();
        fixture.Requests[1].Complete("Second.");
        await Until(() => fixture.Requests.Count == 3);
        fixture.Requests[2].Complete("Third.");
        await Until(() => fixture.Queue.Status.Unfinished == 0);
        Assert.Equal(["First.", " Second.", "\r\n\r\nThird."], fixture.Delivery.Pastes.Select(paste => paste.Text));
    }

    [Fact]
    public async Task PushToTalkStartsOnPressStopsOnReleaseAndIgnoresRepeat()
    {
        await using var fixture = new Fixture();
        var targetReads = 0;
        var input = new DictationHotkeyController(fixture.Queue, () => { targetReads++; return Target; })
        {
            Mode = RecordingMode.PushToTalk
        };
        Assert.Equal(HotkeyResult.Started, input.Press());
        Assert.Null(input.Press());
        Assert.Equal(MicrophoneState.Recording, fixture.Queue.Status.Microphone);
        Assert.Empty(fixture.Requests);
        Assert.Equal(0, targetReads);
        input.Release();
        input.Release();
        await Until(() => fixture.Requests.Count == 1);
        Assert.Equal(1, targetReads);
        Assert.Equal(1, fixture.Recorders[0].Stops);
        fixture.Requests[0].Complete("Held dictation.");
        await Until(() => fixture.Queue.Status.Unfinished == 0);
    }

    [Fact]
    public async Task PushToTalkCanOverlapTranscriptionWithoutDroppingAnAcceptedClip()
    {
        await using var fixture = new Fixture();
        var input = new DictationHotkeyController(fixture.Queue, () => Target) { Mode = RecordingMode.PushToTalk };
        input.Press();
        input.Release();
        Assert.Equal(HotkeyResult.Started, input.Press());
        Assert.True(fixture.Queue.Status.IsTranscribing);
        Assert.Equal(MicrophoneState.Recording, fixture.Queue.Status.Microphone);
        input.Release();
        Assert.Equal(HotkeyResult.Full, input.Press());
        input.Release();
        Assert.Equal(2, fixture.Queue.Status.Unfinished);
        fixture.Requests[0].Complete("First.");
        await Until(() => fixture.Requests.Count == 2);
        fixture.Requests[1].Complete("Second.");
        await Until(() => fixture.Queue.Status.Unfinished == 0);
        Assert.Equal(1, fixture.MaxRequests);
        Assert.Equal(["First.", " Second."], fixture.Delivery.Pastes.Select(paste => paste.Text));
    }

    [Fact]
    public async Task ReleasingPushToTalkBeforeMicrophoneIsReadyCancelsOnlyEmptyReservation()
    {
        await using var fixture = new Fixture { DelayStop = true };
        var input = new DictationHotkeyController(fixture.Queue, () => Target) { Mode = RecordingMode.PushToTalk };
        input.Press();
        input.Release();
        Assert.Equal(HotkeyResult.StartPending, input.Press());
        input.Release();
        Assert.False(fixture.Queue.Status.StartPending);
        Assert.Equal(1, fixture.Queue.Status.Unfinished);
        fixture.Recorders[0].FinishStop();
        await Until(() => fixture.Requests.Count == 1);
        Assert.Single(fixture.Recorders);
        Assert.Equal(MicrophoneState.Idle, fixture.Queue.Status.Microphone);
        fixture.Requests[0].Complete("Keep the accepted clip.");
        await Until(() => fixture.Queue.Status.Unfinished == 0);
    }

    [Fact]
    public async Task HeldPushToTalkReservationStartsAfterStopAndItsReleaseStopsOnlyTheNewClip()
    {
        await using var fixture = new Fixture { DelayStop = true };
        var input = new DictationHotkeyController(fixture.Queue, () => Target) { Mode = RecordingMode.PushToTalk };
        input.Press();
        input.Release();
        Assert.Equal(HotkeyResult.StartPending, input.Press());
        fixture.Recorders[0].FinishStop();
        await Until(() => fixture.Recorders.Count == 2);
        Assert.Equal(MicrophoneState.Recording, fixture.Queue.Status.Microphone);
        input.Release();
        Assert.Equal(1, fixture.Recorders[1].Stops);
        fixture.Recorders[1].FinishStop();
        await Until(() => fixture.Queue.Status.Microphone == MicrophoneState.Idle);
        fixture.Requests[0].Complete("First.");
        await Until(() => fixture.Requests.Count == 2);
        fixture.Requests[1].Complete("Second.");
        await Until(() => fixture.Queue.Status.Unfinished == 0);
        Assert.Equal(["clip-1", "clip-2"], fixture.Deleted);
        Assert.Equal(["First.", " Second."], fixture.Delivery.Pastes.Select(paste => paste.Text));
    }

    [Fact]
    public async Task ToggleModeDoesNotStopOnReleaseAndStillUsesSecondPress()
    {
        await using var fixture = new Fixture();
        var input = new DictationHotkeyController(fixture.Queue, () => Target);
        input.Press();
        input.Release();
        Assert.Equal(MicrophoneState.Recording, fixture.Queue.Status.Microphone);
        Assert.Equal(HotkeyResult.Stopping, input.Press());
        await Until(() => fixture.Requests.Count == 1);
    }

    [Fact]
    public async Task PushToTalkReleaseAfterShutdownHasNoLateEffects()
    {
        await using var fixture = new Fixture();
        var input = new DictationHotkeyController(fixture.Queue, () => throw new InvalidOperationException("Late target read"))
        {
            Mode = RecordingMode.PushToTalk
        };
        input.Press();
        await fixture.Queue.ShutdownAsync();
        input.Release();
        Assert.Empty(fixture.Requests);
        Assert.Empty(fixture.Errors);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ParagraphQueue Queue { get; }
        public List<FakeRecorder> Recorders { get; } = [];
        public List<Request> Requests { get; } = [];
        public List<string> Deleted { get; } = [];
        public List<string> Errors { get; } = [];
        public FakeDelivery Delivery { get; } = new();
        public bool DelayStop { get; init; }
        public bool FailStart { get; set; }
        public bool FailDelete { get; set; }
        public int MaxRequests { get; private set; }
        private int activeRequests;

        public Fixture(int capacity = ParagraphQueue.DefaultCapacity)
        {
            Queue = new ParagraphQueue(
                () =>
                {
                    var recorder = new FakeRecorder($"clip-{Recorders.Count + 1}", DelayStop, FailStart);
                    Recorders.Add(recorder);
                    return recorder;
                },
                Transcribe,
                Delivery,
                path =>
                {
                    if (FailDelete) { throw new IOException("File locked."); }
                    Deleted.Add(path);
                },
                capacity);
            Queue.Error += (title, error) => Errors.Add($"{title}: {error}");
        }

        public HotkeyResult Press() => Queue.HandleHotkey(() => Target);
        public void StartAndStop()
        {
            Assert.Equal(HotkeyResult.Started, Press());
            Assert.Equal(HotkeyResult.Stopping, Press());
        }

        private async Task<string> Transcribe(string path, CancellationToken token)
        {
            var request = new Request(path);
            Requests.Add(request);
            activeRequests++;
            MaxRequests = Math.Max(MaxRequests, activeRequests);
            try { return await request.Completion.Task.WaitAsync(token); }
            finally { activeRequests--; }
        }

        public async ValueTask DisposeAsync() => await Queue.ShutdownAsync();
    }

    private sealed class Request(string path)
    {
        public string Path { get; } = path;
        public TaskCompletionSource<string> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(string text) => Completion.SetResult(text);
        public void Fail() => Completion.SetException(new HttpRequestException("Service unavailable."));
    }

    private sealed class FakeRecorder(string path, bool delayStop, bool failStart) : IParagraphRecorder
    {
        private readonly TaskCompletionSource<string> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public int Stops { get; private set; }
        public bool FailStop { get; set; }
        public void Start()
        {
            if (failStart) { throw new InvalidOperationException("Microphone unavailable."); }
        }

        public Task<string> StopAsync()
        {
            Stops++;
            if (FailStop) { throw new IOException("Capture failed."); }
            return delayStop ? stopped.Task : Task.FromResult(path);
        }

        public void FinishStop() => stopped.SetResult(path);
        public void Dispose()
        {
            Disposed = true;
            stopped.TrySetCanceled();
        }
    }

    private sealed class FakeDelivery : IParagraphDelivery
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<(nint Target, string Text)> Pastes { get; } = [];
        public bool AllowPaste { get; set; } = true;
        public bool DelayPaste { get; set; }
        public int Attempts { get; private set; }
        public int Canceled { get; private set; }
        public string Clipboard { get; private set; } = "original clipboard";
        public Exception? Failure { get; set; }
        public void ReleasePaste() => release.SetResult();
        public async Task<bool> TryDeliverAsync(nint target, string text, CancellationToken cancellationToken)
        {
            Attempts++;
            if (DelayPaste)
            {
                try { await release.Task.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) { Canceled++; throw; }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!AllowPaste) { return false; }
            Copy(text);
            Pastes.Add((target, text));
            return true;
        }

        public void Copy(string text)
        {
            if (Failure is not null) { throw Failure; }
            Clipboard = text;
        }
    }
}
