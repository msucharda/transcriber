using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;

namespace TinyTranscriber.Tests;

public sealed class StatusFormTests
{
    private const string Shortcut = "Ctrl+Shift+Space";

    [Fact]
    public void RecordingRemainsPrimaryWhenBackgroundTranscriptionFinishesOrFails()
    {
        var status = Status(MicrophoneState.Recording, transcribing: true);
        var before = DictationPresentation.From(status, Shortcut);
        var after = DictationPresentation.From(status with { IsTranscribing = false }, Shortcut);
        var failed = DictationPresentation.From(status with { IsTranscribing = false, HasFailure = true }, Shortcut);
        Assert.All(new[] { before, after, failed }, presentation =>
        {
            Assert.True(presentation.Recording);
            Assert.True(presentation.Visible);
            Assert.StartsWith("Listening", presentation.Title);
            Assert.False(presentation.Processing);
        });
        Assert.True(before.BackgroundTranscription);
        Assert.False(after.BackgroundTranscription);
        Assert.False(failed.BackgroundTranscription);
    }

    [Fact]
    public void PausedAndFailedStatesNeverPretendToBeActivelyProcessing()
    {
        var status = Status(transcribing: true);
        foreach (var blocked in new[]
        {
            status with { DeliveryPaused = true },
            status with { HasFailure = true },
            status with { Microphone = MicrophoneState.Recording, DeliveryPaused = true }
        })
        {
            var presentation = DictationPresentation.From(blocked, Shortcut);
            Assert.False(presentation.Processing);
            Assert.False(presentation.BackgroundTranscription);
        }
    }

    [Fact]
    public void BackgroundTransitionPreservesMeterEnergyAndPhase()
    {
        RunSta(() =>
        {
            using var form = new StatusForm(decorativeMotion: true);
            var recording = DictationPresentation.From(Status(MicrophoneState.Recording), Shortcut);
            form.UpdateStatus(recording, showWindow: false);
            var waveform = Assert.IsType<WaveformControl>(form.Controls["Waveform"]);
            waveform.SetAudioLevel(0.8f);
            Invoke(waveform, "OnAnimationTick", null, EventArgs.Empty);
            var level = Field<float>(waveform, "displayedLevel");
            var phase = Field<float>(waveform, "phase");
            form.UpdateStatus(recording with { BackgroundTranscription = true }, showWindow: false);
            for (var tick = 0; tick < 12; tick++) { Invoke(form, "OnActivityTick", null, EventArgs.Empty); }
            Assert.Equal(Color.FromArgb(23, 34, 47), form.BackColor);
            form.UpdateStatus(recording, showWindow: false);
            for (var tick = 0; tick < 12; tick++) { Invoke(form, "OnActivityTick", null, EventArgs.Empty); }
            Assert.Equal(Color.FromArgb(23, 25, 29), form.BackColor);
            Assert.Equal(level, Field<float>(waveform, "displayedLevel"));
            Assert.Equal(phase, Field<float>(waveform, "phase"));
            Assert.False(Field<System.Windows.Forms.Timer>(form, "activityTimer").Enabled);
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void ReducedMotionAndHighContrastRetainSemanticBackgroundStatus()
    {
        RunSta(() =>
        {
            var presentation = DictationPresentation.From(Status(MicrophoneState.Recording, true), Shortcut);
            using var reduced = new StatusForm(decorativeMotion: false, highContrast: false);
            reduced.UpdateStatus(presentation, showWindow: false);
            Assert.Equal(1, Field<float>(reduced, "activityEmphasis"));
            Assert.False(Field<System.Windows.Forms.Timer>(reduced, "activityTimer").Enabled);
            using var contrast = new StatusForm(decorativeMotion: true, highContrast: true);
            contrast.UpdateStatus(presentation, showWindow: false);
            Assert.Equal(SystemColors.Window, contrast.BackColor);
            Assert.Equal(SystemColors.WindowText, contrast.Controls["BackgroundActivity"]!.ForeColor);
            Assert.False(Field<System.Windows.Forms.Timer>(contrast, "activityTimer").Enabled);
            var waveform = Assert.IsType<WaveformControl>(contrast.Controls["Waveform"]);
            Assert.Equal(SystemColors.WindowText, waveform.AccentColor);
            contrast.HideStatus();
            Assert.False(Field<System.Windows.Forms.Timer>(waveform, "animationTimer").Enabled);
        });
    }

    [Fact]
    public void PillKeepsNoActivateAndToolWindowStyles()
    {
        RunSta(() =>
        {
            using var form = new StatusForm();
            var parameters = (CreateParams)typeof(StatusForm)
                .GetProperty("CreateParams", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            Assert.NotEqual(0, parameters.ExStyle & 0x08000000);
            Assert.NotEqual(0, parameters.ExStyle & 0x00000080);
            Assert.False(form.ShowInTaskbar);
        });
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void SyntheticStatesRenderOffscreenWithoutClippedLabels(float scale)
    {
        RunSta(() =>
        {
            var statuses = new[]
            {
                Status(MicrophoneState.Recording, unfinished: 1),
                Status(MicrophoneState.Recording, transcribing: true),
                Status(MicrophoneState.Recording) with { HasFailure = true },
                Status(transcribing: true),
                Status() with { DeliveryPaused = true },
                Status() with { HasFailure = true },
                Status(MicrophoneState.Stopping) with { StartPending = true }
            };
            var directory = Environment.GetEnvironmentVariable("TINY_TRANSCRIBER_TEST_RENDER_DIRECTORY");
            for (var index = 0; index < statuses.Length; index++)
            {
                using var form = new StatusForm(decorativeMotion: false, highContrast: false);
                form.UpdateStatus(DictationPresentation.From(statuses[index], Shortcut), showWindow: false);
                form.Scale(new SizeF(scale, scale));
                foreach (var label in form.Controls.OfType<Label>())
                {
                    var measured = TextRenderer.MeasureText(label.Text, label.Font, Size.Empty,
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    Assert.True(measured.Width <= label.ClientSize.Width,
                        $"{scale}: '{label.Text}' needs {measured.Width}px, has {label.ClientSize.Width}px");
                    Assert.True(label.Right <= form.ClientSize.Width);
                    Assert.True(label.Bottom <= form.ClientSize.Height);
                }

                using var image = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(image, form.ClientRectangle);
                using (var graphics = Graphics.FromImage(image))
                {
                    // Hidden top-level forms omit children from WM_PRINT. Render each
                    // child explicitly, without showing a window or taking focus.
                    foreach (Control control in form.Controls)
                    {
                        if (control.Name == "BackgroundActivity" && !statuses[index].IsTranscribing)
                        {
                            continue;
                        }

                        if (control is WaveformControl waveform)
                        {
                            waveform.SetAudioLevel(0.65f);
                            Invoke(waveform, "OnAnimationTick", null, EventArgs.Empty);
                        }

                        using var child = new Bitmap(control.Width, control.Height);
                        control.DrawToBitmap(child, control.ClientRectangle);
                        graphics.DrawImageUnscaled(child, control.Location);
                    }
                }

                Assert.False(form.Visible);
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);
                    image.Save(Path.Combine(directory, $"pill-{index}-{scale:0.0}.png"), ImageFormat.Png);
                }
            }
        });
    }

    private static ParagraphQueueStatus Status(
        MicrophoneState microphone = MicrophoneState.Idle, bool transcribing = false, int unfinished = 2) =>
        new(microphone, unfinished, 2, transcribing, false, false, false, false, false);

    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;

    private static void Invoke(object target, string name, params object?[] arguments) =>
        target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, arguments);

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Offscreen UI test did not finish.");
        if (failure is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw(); }
    }
}
