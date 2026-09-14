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

    public StatusForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(23, 25, 29);
        ClientSize = new Size(410, 74);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        waveform = new WaveformControl
        {
            BackColor = Color.Transparent,
            Location = new Point(20, 15),
            Size = new Size(104, 44)
        };
        titleLabel = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI Semibold", 11.5f),
            ForeColor = Color.FromArgb(248, 249, 251),
            Location = new Point(142, 13),
            Size = new Size(242, 27),
            TextAlign = ContentAlignment.MiddleLeft
        };
        detailLabel = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9.25f),
            ForeColor = Color.FromArgb(180, 185, 195),
            Location = new Point(142, 38),
            Size = new Size(242, 23),
            TextAlign = ContentAlignment.MiddleLeft
        };

        Controls.Add(waveform);
        Controls.Add(titleLabel);
        Controls.Add(detailLabel);
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

    public void ShowRecording(string shortcut)
    {
        titleLabel.Text = "Listening";
        detailLabel.Text = $"{shortcut} to transcribe";
        waveform.StartRecording();
        ShowStatus();
    }

    public void ShowTranscribing()
    {
        titleLabel.Text = "Transcribing";
        detailLabel.Text = "Sending audio to MAI-Transcribe-2";
        waveform.StartTranscribing();
        ShowStatus();
    }

    public void ShowPolishing()
    {
        titleLabel.Text = "Polishing";
        detailLabel.Text = "Lightly editing your transcript";
        waveform.StartTranscribing();
        ShowStatus();
    }

    public void SetAudioLevel(float level)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => SetAudioLevel(level));
            return;
        }

        waveform.SetAudioLevel(level);
    }

    public void HideStatus()
    {
        waveform.StopAnimation();
        Hide();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var backgroundPath = CreateRoundedPath(ClientRectangle, CornerRadius);
        using var backgroundBrush = new SolidBrush(BackColor);
        eventArgs.Graphics.FillPath(backgroundBrush, backgroundPath);

        var borderBounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        using var borderPath = CreateRoundedPath(borderBounds, CornerRadius - 1);
        using var borderPen = new Pen(Color.FromArgb(62, 66, 75));
        eventArgs.Graphics.DrawPath(borderPen, borderPath);

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

    private void UpdateRoundedRegion()
    {
        using var path = CreateRoundedPath(ClientRectangle, CornerRadius);
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
