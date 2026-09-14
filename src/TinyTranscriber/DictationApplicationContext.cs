using Azure.Identity;
using System.Media;

namespace TinyTranscriber;

internal sealed class DictationApplicationContext : ApplicationContext
{
    private readonly NotifyIcon notifyIcon;
    private readonly HotkeyWindow hotkeyWindow;
    private readonly TrayIcons trayIcons;
    private readonly HotkeyDefinition hotkey = HotkeyDefinition.Load();
    private readonly StatusForm statusForm = new();
    private readonly AudioRecorder recorder = new();
    private readonly MaiTranscriptionClient transcriptionClient = new(
        new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        },
        new DefaultAzureCredential());
    private DictationState state;

    public DictationApplicationContext()
    {
        hotkeyWindow = new HotkeyWindow(hotkey);
        trayIcons = new TrayIcons();
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitThread();

        notifyIcon = new NotifyIcon
        {
            Icon = trayIcons.Idle,
            Text = $"Tiny Transcriber - {hotkey.DisplayName} to record",
            ContextMenuStrip = new ContextMenuStrip(),
            Visible = true
        };
        notifyIcon.ContextMenuStrip.Items.Add(exitItem);

        hotkeyWindow.Pressed += OnHotkeyPressed;
        recorder.LevelChanged += OnAudioLevelChanged;

        ShowMessage(
            "Tiny Transcriber is ready",
            $"Press {hotkey.DisplayName} to start recording, then press it again to transcribe and paste.",
            ToolTipIcon.Info);
    }

    protected override void ExitThreadCore()
    {
        hotkeyWindow.Pressed -= OnHotkeyPressed;
        recorder.LevelChanged -= OnAudioLevelChanged;
        hotkeyWindow.Dispose();
        recorder.Dispose();
        statusForm.Dispose();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        trayIcons.Dispose();
        base.ExitThreadCore();
    }

    private async void OnHotkeyPressed(object? sender, EventArgs eventArgs)
    {
        try
        {
            switch (state)
            {
                case DictationState.Idle:
                    StartRecording();
                    break;
                case DictationState.Recording:
                    await StopTranscribeAndPasteAsync();
                    break;
                case DictationState.Transcribing:
                    SystemSounds.Beep.Play();
                    break;
            }
        }
        catch (Exception exception)
        {
            SetState(DictationState.Idle);
            statusForm.HideStatus();
            ShowMessage("Dictation failed", exception.Message, ToolTipIcon.Error);
        }
    }

    private void StartRecording()
    {
        if (!AppSettings.TryLoad(out _, out var error))
        {
            throw new InvalidOperationException(error);
        }

        recorder.Start();
        SetState(DictationState.Recording);
        statusForm.ShowRecording(hotkey.DisplayName);
        SystemSounds.Asterisk.Play();
    }

    private async Task StopTranscribeAndPasteAsync()
    {
        SetState(DictationState.Transcribing);
        statusForm.ShowTranscribing();
        var targetWindow = NativeInput.GetActiveWindow();
        string? audioPath = null;

        try
        {
            audioPath = await recorder.StopAsync();

            if (!AppSettings.TryLoad(out var settings, out var error) || settings is null)
            {
                throw new InvalidOperationException(error);
            }

            var transcript = await transcriptionClient.TranscribeAsync(audioPath, settings);
            Clipboard.SetText(transcript);
            await NativeInput.PasteAsync(targetWindow);
            SystemSounds.Exclamation.Play();
        }
        finally
        {
            if (audioPath is not null && File.Exists(audioPath))
            {
                File.Delete(audioPath);
            }

            SetState(DictationState.Idle);
            statusForm.HideStatus();
        }
    }

    private void SetState(DictationState nextState)
    {
        var (icon, text) = nextState switch
        {
            DictationState.Idle => (trayIcons.Idle, $"Tiny Transcriber - {hotkey.DisplayName} to record"),
            DictationState.Recording => (trayIcons.Recording, "Tiny Transcriber - recording"),
            DictationState.Transcribing => (trayIcons.Transcribing, "Tiny Transcriber - transcribing"),
            _ => throw new ArgumentOutOfRangeException(nameof(nextState))
        };
        state = nextState;
        notifyIcon.Icon = icon;
        notifyIcon.Text = text;
    }

    private void ShowMessage(string title, string text, ToolTipIcon icon)
    {
        notifyIcon.ShowBalloonTip(5000, title, text, icon);
    }

    private void OnAudioLevelChanged(float level)
    {
        statusForm.SetAudioLevel(level);
    }

    private enum DictationState
    {
        Idle,
        Recording,
        Transcribing
    }
}
