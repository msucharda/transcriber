namespace TinyTranscriber.Tests;

public sealed class HotkeyReleaseTrackerTests
{
    [Fact]
    public void ReleaseIsEmittedOnceWhenShortcutKeyGoesUp()
    {
        var down = true;
        var tracker = new HotkeyReleaseTracker(() => down);
        var events = new List<string>();
        tracker.Pressed += () => events.Add("press");
        tracker.Released += () => events.Add("release");
        tracker.Press();
        tracker.Poll();
        Assert.Equal(["press"], events);
        down = false;
        tracker.Poll();
        tracker.Poll();
        Assert.Equal(["press", "release"], events);
        Assert.False(tracker.IsPressed);
    }

    [Fact]
    public void VeryShortTapAlreadyReleasedBeforeHotkeyDispatchIsNotLeftRecording()
    {
        var tracker = new HotkeyReleaseTracker(() => false);
        var events = new List<string>();
        tracker.Pressed += () => events.Add("press");
        tracker.Released += () => events.Add("release");
        tracker.Press();
        Assert.Equal(["press", "release"], events);
    }

    [Fact]
    public void FreshNonRepeatingHotkeyBetweenPollsEndsPreviousGestureFirst()
    {
        var tracker = new HotkeyReleaseTracker(() => true);
        var events = new List<string>();
        tracker.Pressed += () => events.Add("press");
        tracker.Released += () => events.Add("release");
        tracker.Press();
        tracker.Press();
        Assert.Equal(["press", "release", "press"], events);
    }
}
