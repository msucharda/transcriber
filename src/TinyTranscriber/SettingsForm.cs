using System.Text.Json;

namespace TinyTranscriber;

internal sealed class SettingsForm : Form
{
    private readonly ComboBox recordingMode = new()
    {
        Name = "RecordingMode",
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Top
    };
    private readonly ComboBox separator = new()
    {
        Name = "Separator",
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Top
    };
    private readonly Label recordingHelp = new() { AutoSize = true, MaximumSize = new Size(440, 0) };
    private readonly Label saveError = new() { Name = "SaveError", AutoSize = true, MaximumSize = new Size(440, 0), Visible = false };
    private readonly Label busyNotice = new()
    {
        Name = "BusyNotice",
        AutoSize = true,
        Text = "Finish the current recording before saving.",
        Visible = false
    };
    private readonly Button saveButton = new() { Text = "&Save", AutoSize = true };
    private readonly Action<DictationPreferences> save;
    private readonly HotkeyDefinition hotkey;

    public SettingsForm(DictationPreferences preferences, HotkeyDefinition hotkey, Action<DictationPreferences> save)
    {
        this.save = save;
        this.hotkey = hotkey;
        Text = "Tiny Transcriber - Settings";
        Font = SystemFonts.MessageBoxFont;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(24)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 440));
        layout.Controls.Add(new Label { AutoSize = true, Text = $"Global shortcut: {hotkey.DisplayName}" });
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Text = "To change the shortcut, set TINY_TRANSCRIBER_HOTKEY and restart.",
            Margin = new Padding(3, 6, 3, 12)
        });
        layout.Controls.Add(new Label { AutoSize = true, Text = "&Recording mode", Margin = new Padding(3, 8, 3, 6) });
        recordingMode.Items.AddRange(["Press to start / press to stop", "Push to talk (hold to record)"]);
        recordingMode.SelectedIndex = (int)preferences.RecordingMode;
        recordingMode.AccessibleName = "Recording mode";
        layout.Controls.Add(recordingMode);
        recordingHelp.Margin = new Padding(3, 6, 3, 16);
        layout.Controls.Add(recordingHelp);
        layout.Controls.Add(new Label { AutoSize = true, Text = "&Between queued dictations", Margin = new Padding(3, 8, 3, 6) });
        separator.Items.AddRange(["Continue on same line (space)", "New line", "New paragraph (blank line)"]);
        separator.SelectedIndex = (int)preferences.Separator;
        separator.AccessibleName = "Between queued dictations";
        layout.Controls.Add(separator);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Text = "Spacing applies to newly started dictations, including manual copies. Settings are saved on this device.",
            Margin = new Padding(3, 6, 3, 16)
        });
        layout.Controls.Add(busyNotice);
        layout.Controls.Add(saveError);
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 12, 0, 0)
        };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        cancel.Click += (_, _) => Close();
        saveButton.Click += (_, _) => SaveChanges();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(saveButton);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        AcceptButton = saveButton;
        CancelButton = cancel;
        recordingMode.SelectedIndexChanged += (_, _) => UpdateHelp();
        UpdateHelp();
    }

    public DictationPreferences SelectedPreferences
    {
        get
        {
            if (recordingMode.SelectedIndex < 0 || separator.SelectedIndex < 0)
            {
                throw new InvalidOperationException("Choose a recording mode and spacing option.");
            }

            return new((DictationSeparator)separator.SelectedIndex, (RecordingMode)recordingMode.SelectedIndex);
        }
    }

    public void SetRecordingBusy(bool busy)
    {
        saveButton.Enabled = !busy;
        busyNotice.Visible = busy;
    }

    public bool SaveChanges()
    {
        if (!saveButton.Enabled)
        {
            return false;
        }

        try
        {
            save(SelectedPreferences);
            Close();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            saveError.Text = $"Could not save settings. Your previous settings are unchanged.\n{exception.Message}";
            saveError.Visible = true;
            return false;
        }
    }

    private void UpdateHelp()
    {
        recordingHelp.Text = recordingMode.SelectedIndex == (int)RecordingMode.PushToTalk
            ? $"Hold {hotkey.DisplayName} to record. Release {(Keys)hotkey.VirtualKey} to transcribe."
            : $"Press {hotkey.DisplayName} to start recording, then press it again to transcribe.";
    }
}
