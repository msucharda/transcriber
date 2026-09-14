namespace TinyTranscriber.Tests;

public sealed class ForegroundPasteTests
{
    private static readonly nint Target = 123;
    private static readonly nint Other = 456;

    [Fact]
    public async Task MissingTargetNeverActivatesOrSendsInput()
    {
        var input = new FakeInput();
        Assert.False(await new ForegroundPaste(input).TryPasteAsync(nint.Zero));
        Assert.Equal(0, input.Activations);
        Assert.Equal(0, input.Pastes);
    }

    [Fact]
    public async Task ClosedTargetNeverActivatesOrSendsInput()
    {
        var input = new FakeInput { TargetExists = false };
        Assert.False(await new ForegroundPaste(input).TryPasteAsync(Target));
        Assert.Equal(0, input.Activations);
        Assert.Equal(0, input.Pastes);
    }

    [Fact]
    public async Task DeniedActivationDoesNotPasteIntoCurrentWindow()
    {
        var input = new FakeInput { ActivationAllowed = false };
        Assert.False(await new ForegroundPaste(input).TryPasteAsync(Target));
        Assert.Equal(1, input.Activations);
        Assert.Equal(0, input.Pastes);
    }

    [Fact]
    public async Task FocusMustStillMatchImmediatelyBeforePaste()
    {
        var input = new FakeInput { AfterActivation = fake => fake.Foreground = Other };
        Assert.False(await new ForegroundPaste(input).TryPasteAsync(Target));
        Assert.Equal(2, input.ForegroundReads);
        Assert.Equal(0, input.Pastes);
    }

    [Fact]
    public async Task WindowClosingAfterActivationPreventsPaste()
    {
        var input = new FakeInput { AfterActivation = fake => fake.TargetExists = false };
        Assert.False(await new ForegroundPaste(input).TryPasteAsync(Target));
        Assert.Equal(0, input.Pastes);
    }

    [Fact]
    public async Task VerifiedTargetReceivesOnePaste()
    {
        var input = new FakeInput();
        Assert.True(await new ForegroundPaste(input).TryPasteAsync(Target));
        Assert.Equal(1, input.Activations);
        Assert.Equal(1, input.Pastes);
    }

    [Fact]
    public async Task AlreadyForegroundTargetDoesNotNeedActivation()
    {
        var input = new FakeInput { Foreground = Target, ActivationAllowed = false };
        Assert.True(await new ForegroundPaste(input).TryPasteAsync(Target));
        Assert.Equal(0, input.Activations);
        Assert.Equal(1, input.Pastes);
    }

    [Fact]
    public async Task CancellationAfterActivationPreventsPaste()
    {
        using var cancellation = new CancellationTokenSource();
        var input = new FakeInput { AfterActivation = _ => cancellation.Cancel() };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ForegroundPaste(input).TryPasteAsync(Target, cancellation.Token));
        Assert.Equal(0, input.Pastes);
    }

    [Fact]
    public async Task PreCanceledRequestDoesNotTouchWindows()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var input = new FakeInput();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ForegroundPaste(input).TryPasteAsync(Target, cancellation.Token));
        Assert.Equal(0, input.ForegroundReads);
        Assert.Equal(0, input.Activations);
        Assert.Equal(0, input.Pastes);
    }

    private sealed class FakeInput : IWindowInput
    {
        public bool TargetExists { get; set; } = true;
        public bool ActivationAllowed { get; init; } = true;
        public nint Foreground { get; set; } = Other;
        public Action<FakeInput>? AfterActivation { get; init; }
        public int Activations { get; private set; }
        public int ForegroundReads { get; private set; }
        public int Pastes { get; private set; }

        public bool IsWindow(nint window) => window == Target && TargetExists;

        public nint GetForegroundWindow()
        {
            ForegroundReads++;
            return Foreground;
        }

        public bool ActivateWindow(nint window)
        {
            Activations++;
            if (ActivationAllowed)
            {
                Foreground = window;
                AfterActivation?.Invoke(this);
            }

            return ActivationAllowed;
        }

        public void SendPaste()
        {
            Assert.True(TargetExists);
            Assert.Equal(Target, Foreground);
            Pastes++;
        }
    }
}
