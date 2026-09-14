namespace TinyTranscriber.Tests;

public sealed class ForegroundDeliveryTests
{
    private const nint Target = 123;

    [Theory]
    [InlineData(0, true, 123)]
    [InlineData(123, false, 123)]
    [InlineData(123, true, 456)]
    public async Task InvalidClosedOrBackgroundWindowNeverTouchesClipboardOrActivates(
        int target, bool exists, int foreground)
    {
        var input = new FakeInput { Exists = exists, Foreground = foreground };
        Assert.False(await input.Deliver(target));
        Assert.Equal("original", input.Clipboard);
        Assert.Equal(0, input.Pastes);
    }

    [Fact]
    public async Task ChangedForegroundDuringDelayLeavesClipboardAlone()
    {
        var input = new FakeInput { ChangeOnRead = 2 };
        Assert.False(await input.Deliver(Target));
        Assert.Equal("original", input.Clipboard);
        Assert.Equal(0, input.Pastes);
    }

    [Fact]
    public async Task WindowDisappearingDuringDelayLeavesClipboardAlone()
    {
        var input = new FakeInput { CloseOnCheck = 2 };
        Assert.False(await input.Deliver(Target));
        Assert.Equal("original", input.Clipboard);
        Assert.Equal(0, input.Pastes);
    }

    [Fact]
    public async Task ForegroundRecheckedAfterClipboardImmediatelyBeforeInput()
    {
        var input = new FakeInput { AfterCopy = fake => fake.Foreground = 456 };
        Assert.False(await input.Deliver(Target));
        Assert.Equal("transcript", input.Clipboard);
        Assert.Equal(0, input.Pastes);
    }

    [Fact]
    public async Task HeldHotkeyModifiersDoNotReplaceClipboardOrInjectModifiedPaste()
    {
        var input = new FakeInput { ModifiersReleased = false };
        Assert.False(await input.Deliver(Target));
        Assert.Equal("original", input.Clipboard);
        Assert.Equal(0, input.Pastes);
    }

    [Fact]
    public async Task CancellationDuringDelayDoesNotTouchClipboardOrInput()
    {
        using var cancellation = new CancellationTokenSource();
        var input = new FakeInput();
        var pending = input.Deliver(Target, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal("original", input.Clipboard);
        Assert.Equal(0, input.Pastes);
    }

    [Fact]
    public async Task CancellationAfterClipboardStillPreventsInput()
    {
        using var cancellation = new CancellationTokenSource();
        var input = new FakeInput { AfterCopy = _ => cancellation.Cancel() };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => input.Deliver(Target, cancellation.Token));
        Assert.Equal(0, input.Pastes);
    }

    [Fact]
    public async Task SuccessfulDeliveryNeverActivatesAndChecksBeforeAndAfterClipboard()
    {
        var input = new FakeInput();
        Assert.True(await input.Deliver(Target));
        Assert.Equal(["window", "foreground", "window", "foreground", "modifiers",
            "copy", "window", "foreground", "modifiers", "paste"], input.Operations);
        Assert.Equal(1, input.Pastes);
    }

    [Fact]
    public async Task InputFailureIsNotReportedAsSuccess()
    {
        var input = new FakeInput { FailInput = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => input.Deliver(Target));
        Assert.Equal(0, input.Pastes);
    }

    private sealed class FakeInput : IWindowInput
    {
        public bool Exists { get; set; } = true;
        public nint Foreground { get; set; } = Target;
        public bool ModifiersReleased { get; init; } = true;
        public int ChangeOnRead { get; init; }
        public int CloseOnCheck { get; init; }
        public bool FailInput { get; init; }
        public Action<FakeInput>? AfterCopy { get; init; }
        public string Clipboard { get; private set; } = "original";
        public int Pastes { get; private set; }
        public List<string> Operations { get; } = [];
        private int reads;
        private int checks;
        public Task<bool> Deliver(nint target, CancellationToken token = default) =>
            new ForegroundPaste(this).TryDeliverAsync(target, "transcript", text =>
            {
                Operations.Add("copy");
                Clipboard = text;
                AfterCopy?.Invoke(this);
            }, token);

        public bool IsWindow(nint window)
        {
            Operations.Add("window");
            if (++checks == CloseOnCheck) { Exists = false; }
            return Exists && window == Target;
        }

        public nint GetForegroundWindow()
        {
            Operations.Add("foreground");
            if (++reads == ChangeOnRead) { Foreground = 456; }
            return Foreground;
        }

        public bool ActivateWindow(nint window) => throw new Xunit.Sdk.XunitException("Must never activate a window.");
        public bool AreModifiersReleased()
        {
            Operations.Add("modifiers");
            return ModifiersReleased;
        }

        public void SendPaste()
        {
            Operations.Add("paste");
            if (FailInput) { throw new InvalidOperationException("Input rejected."); }
            Pastes++;
        }
    }
}
