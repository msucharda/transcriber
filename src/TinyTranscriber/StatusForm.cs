namespace TinyTranscriber;

internal sealed class StatusForm : Form
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private readonly Label statusLabel;

    public StatusForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(32, 32, 32);
        ClientSize = new Size(310, 52);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(statusLabel);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }

    public void ShowRecording(string shortcut)
    {
        BackColor = Color.FromArgb(180, 35, 35);
        statusLabel.Text = $"RECORDING - {shortcut} to stop";
        ShowStatus();
    }

    public void ShowTranscribing()
    {
        BackColor = Color.FromArgb(35, 90, 170);
        statusLabel.Text = "TRANSCRIBING...";
        ShowStatus();
    }

    public void HideStatus()
    {
        Hide();
    }

    private void ShowStatus()
    {
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(Cursor.Position);
        Location = new Point(
            workingArea.Right - Width - 20,
            workingArea.Bottom - Height - 20);

        if (!Visible)
        {
            Show();
        }

        Invalidate();
    }
}
