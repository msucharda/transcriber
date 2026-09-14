using System.Drawing.Drawing2D;

namespace TinyTranscriber;

internal sealed class WaveformControl : Control
{
    private const int BarCount = 15;
    private readonly System.Windows.Forms.Timer animationTimer;
    private float targetLevel;
    private float displayedLevel;
    private float phase;
    private WaveformMode mode;
    private readonly bool decorativeMotion;
    private readonly bool highContrast;

    public WaveformControl(bool decorativeMotion = true, bool highContrast = false)
    {
        this.decorativeMotion = decorativeMotion;
        this.highContrast = highContrast;
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint,
            true);

        animationTimer = new System.Windows.Forms.Timer
        {
            Interval = 33
        };
        animationTimer.Tick += OnAnimationTick;
    }

    public Color AccentColor { get; private set; } = Color.FromArgb(255, 111, 97);

    public void StartRecording()
    {
        if (mode == WaveformMode.Recording)
        {
            return;
        }

        mode = WaveformMode.Recording;
        AccentColor = highContrast ? SystemColors.WindowText : Color.FromArgb(255, 111, 97);
        targetLevel = 0.08f;
        displayedLevel = 0.08f;
        StartAnimation();
    }

    public void StartTranscribing()
    {
        if (mode == WaveformMode.Transcribing)
        {
            return;
        }

        mode = WaveformMode.Transcribing;
        AccentColor = highContrast ? SystemColors.WindowText : Color.FromArgb(111, 181, 255);
        targetLevel = 0;
        displayedLevel = 0;
        StartAnimation();
    }

    public void SetAudioLevel(float level)
    {
        if (mode != WaveformMode.Recording)
        {
            return;
        }

        targetLevel = Math.Clamp(level, 0, 1);
    }

    public void StopAnimation()
    {
        animationTimer.Stop();
        mode = WaveformMode.Hidden;
        targetLevel = 0;
        displayedLevel = 0;
        phase = 0;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animationTimer.Tick -= OnAnimationTick;
            animationTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);

        if (mode == WaveformMode.Hidden)
        {
            return;
        }

        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var spacing = ClientSize.Width / (float)BarCount;
        var centerY = ClientSize.Height / 2f;

        using var pen = new Pen(AccentColor, 3.2f * ClientSize.Height / 44f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        for (var index = 0; index < BarCount; index++)
        {
            var distanceFromCenter = Math.Abs(index - ((BarCount - 1) / 2f))
                / ((BarCount - 1) / 2f);
            var envelope = 1f - (distanceFromCenter * 0.38f);
            var height = mode == WaveformMode.Recording
                ? RecordingBarHeight(index, envelope)
                : TranscribingBarHeight(index, envelope);
            var x = (spacing * index) + (spacing / 2f);

            eventArgs.Graphics.DrawLine(
                pen,
                x,
                centerY - (height / 2f),
                x,
                centerY + (height / 2f));
        }
    }

    private float RecordingBarHeight(int index, float envelope)
    {
        var organicVariation = 0.78f + (0.22f * MathF.Sin(phase + (index * 0.82f)));
        var energy = 0.14f + (displayedLevel * 0.86f);
        return 6f + ((ClientSize.Height - 12f) * energy * envelope * organicVariation);
    }

    private float TranscribingBarHeight(int index, float envelope)
    {
        var travelingPulse = (MathF.Sin(phase - (index * 0.68f)) + 1f) / 2f;
        return 7f + ((ClientSize.Height - 13f) * travelingPulse * envelope * 0.72f);
    }

    private void StartAnimation()
    {
        phase = 0;
        if (mode == WaveformMode.Recording || decorativeMotion)
        {
            animationTimer.Start();
        }
        else
        {
            animationTimer.Stop();
        }

        Invalidate();
    }

    private void OnAnimationTick(object? sender, EventArgs eventArgs)
    {
        if (mode == WaveformMode.Recording)
        {
            var smoothing = targetLevel > displayedLevel ? 0.42f : 0.14f;
            displayedLevel += (targetLevel - displayedLevel) * smoothing;
            targetLevel *= 0.88f;
            phase += 0.18f;
        }
        else
        {
            phase += 0.16f;
        }

        Invalidate();
    }

    private enum WaveformMode
    {
        Hidden,
        Recording,
        Transcribing
    }
}
