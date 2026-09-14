using System.Drawing.Drawing2D;

namespace TinyTranscriber;

internal sealed class StatusForm : Form
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int CsDropShadow = 0x00020000;
    private const int CornerRadius = 24;
    private readonly WaveformControl waveform;
    private readonly Label titleLabel;
    private readonly Label detailLabel;
    private readonly Label activityLabel;
    private readonly System.Windows.Forms.Timer activityTimer = new() { Interval = 33 };
    private readonly bool decorativeMotion;
    private readonly bool highContrast;
    private bool backgroundTranscription;
    private float activityEmphasis;
    private float activityPhase;

    public StatusForm(bool? decorativeMotion = null, bool? highContrast = null)
    {
        this.highContrast = highContrast ?? SystemInformation.HighContrast;
        this.decorativeMotion = (decorativeMotion ?? VisualPreferences.AnimationsEnabled())
            && !this.highContrast;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = this.highContrast ? SystemColors.Window : Color.FromArgb(23, 25, 29);
        ClientSize = new Size(410, 74);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        waveform = new WaveformControl(this.decorativeMotion, this.highContrast)
        {
            Name = "Waveform",
            BackColor = Color.Transparent,
            Location = new Point(20, 15),
            Size = new Size(104, 44)
        };
        titleLabel = new Label
        {
            Name = "StatusTitle",
            AutoSize = false,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI Semibold", 11.5f),
            ForeColor = this.highContrast ? SystemColors.WindowText : Color.FromArgb(248, 249, 251),
            Location = new Point(142, 13),
            Size = new Size(242, 27),
            TextAlign = ContentAlignment.MiddleLeft
        };
        detailLabel = new Label
        {
            Name = "StatusDetail",
            AutoSize = false,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9.25f),
            ForeColor = this.highContrast ? SystemColors.WindowText : Color.FromArgb(180, 185, 195),
            Location = new Point(142, 38),
            Size = new Size(242, 23),
            TextAlign = ContentAlignment.MiddleLeft
        };
        activityLabel = new Label
        {
            Name = "BackgroundActivity",
            AutoSize = false,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(276, 18),
            Size = new Size(94, 21),
            TextAlign = ContentAlignment.MiddleRight,
            Text = "Transcribing",
            Visible = false
        };

        Controls.Add(waveform);
        Controls.Add(titleLabel);
        Controls.Add(detailLabel);
        Controls.Add(activityLabel);
        activityTimer.Tick += OnActivityTick;
        UpdateRoundedRegion();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
            parameters.ClassStyle |= CsDropShadow;
            return parameters;
        }
    }

    public void UpdateStatus(DictationPresentation presentation, bool showWindow = true)
    {
        if (!presentation.Visible)
        {
            HideStatus();
            return;
        }

        titleLabel.Text = presentation.Title;
        detailLabel.Text = presentation.Detail;
        titleLabel.Width = (int)(detailLabel.Width * (presentation.Recording ? 132f / 242 : 1));
        if (presentation.Recording)
        {
            waveform.StartRecording();
        }
        else if (presentation.Processing)
        {
            waveform.StartTranscribing();
        }
        else
        {
            waveform.StopAnimation();
        }

        backgroundTranscription = presentation.BackgroundTranscription;
        if (!decorativeMotion)
        {
            activityEmphasis = backgroundTranscription ? 1 : 0;
        }

        if (showWindow)
        {
            ShowStatus();
        }

        UpdateActivity();
    }

    public void SetAudioLevel(float level)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        waveform.SetAudioLevel(level);
    }

    public void HideStatus()
    {
        waveform.StopAnimation();
        activityTimer.Stop();
        backgroundTranscription = false;
        activityEmphasis = 0;
        activityPhase = 0;
        UpdateActivity();
        Hide();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            activityTimer.Stop();
            activityTimer.Tick -= OnActivityTick;
            activityTimer.Dispose();
            titleLabel.Font.Dispose();
            detailLabel.Font.Dispose();
            activityLabel.Font.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnVisibleChanged(EventArgs eventArgs)
    {
        base.OnVisibleChanged(eventArgs);
        if (!Visible)
        {
            activityTimer.Stop();
            waveform.StopAnimation();
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var radius = (int)(CornerRadius * ClientSize.Height / 74f);
        using var backgroundPath = CreateRoundedPath(ClientRectangle, radius);
        using var backgroundBrush = new SolidBrush(BackColor);
        eventArgs.Graphics.FillPath(backgroundBrush, backgroundPath);

        var borderBounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        using var borderPath = CreateRoundedPath(borderBounds, radius - 1);
        using var borderPen = new Pen(highContrast ? SystemColors.WindowText : Color.FromArgb(62, 66, 75));
        eventArgs.Graphics.DrawPath(borderPen, borderPath);

        if (activityEmphasis > 0)
        {
            var scale = ClientSize.Width / 410f;
            using var dotBrush = new SolidBrush(activityLabel.ForeColor);
            for (var index = 0; index < 3; index++)
            {
                var offset = decorativeMotion ? MathF.Sin(activityPhase - index * 0.7f) * 1.5f : 0;
                eventArgs.Graphics.FillEllipse(dotBrush,
                    (377 + index * 5) * scale, (27 + offset) * scale, 2.5f * scale, 2.5f * scale);
            }
        }

        base.OnPaint(eventArgs);
    }

    protected override void OnSizeChanged(EventArgs eventArgs)
    {
        base.OnSizeChanged(eventArgs);
        UpdateRoundedRegion();
    }

    private void ShowStatus()
    {
        var workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(
            workingArea.Left + ((workingArea.Width - Width) / 2),
            workingArea.Bottom - Height - 24);

        if (!Visible)
        {
            Show();
        }

        Invalidate();
    }

    private void OnActivityTick(object? sender, EventArgs eventArgs)
    {
        var target = backgroundTranscription ? 1f : 0f;
        activityEmphasis += (target - activityEmphasis) * 0.4f;
        if (Math.Abs(target - activityEmphasis) < 0.02f)
        {
            activityEmphasis = target;
        }

        activityPhase += 0.12f;
        UpdateActivity();
    }

    private void UpdateActivity()
    {
        BackColor = highContrast ? SystemColors.Window : Mix(
            Color.FromArgb(23, 25, 29), Color.FromArgb(23, 34, 47), activityEmphasis);
        activityLabel.ForeColor = highContrast ? SystemColors.WindowText : Mix(
            BackColor, Color.FromArgb(152, 198, 239), activityEmphasis);
        activityLabel.Visible = activityEmphasis > 0;
        detailLabel.ForeColor = highContrast ? SystemColors.WindowText : Mix(
            Color.FromArgb(180, 185, 195), Color.FromArgb(180, 199, 217), activityEmphasis);
        if (decorativeMotion && Visible && (backgroundTranscription || activityEmphasis > 0))
        {
            activityTimer.Start();
        }
        else
        {
            activityTimer.Stop();
        }

        Invalidate();
    }

    private static Color Mix(Color from, Color to, float amount) => Color.FromArgb(
        (int)(from.R + (to.R - from.R) * amount),
        (int)(from.G + (to.G - from.G) * amount),
        (int)(from.B + (to.B - from.B) * amount));

    private void UpdateRoundedRegion()
    {
        using var path = CreateRoundedPath(ClientRectangle, (int)(CornerRadius * ClientSize.Height / 74f));
        var previousRegion = Region;
        Region = new Region(path);
        previousRegion?.Dispose();
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(
            bounds.Right - diameter,
            bounds.Bottom - diameter,
            diameter,
            diameter,
            0,
            90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
