using Azure.Identity;
using System.Media;

namespace TinyTranscriber;

internal sealed class DictationApplicationContext : ApplicationContext
{
    private readonly NotifyIcon notifyIcon;
    private readonly HotkeyWindow hotkeyWindow;
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
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitThread();

        notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = $"Tiny Transcriber - {hotkey.DisplayName} to record",
            ContextMenuStrip = new ContextMenuStrip(),
            Visible = true
        };
        notifyIcon.ContextMenuStrip.Items.Add(exitItem);

        hotkeyWindow = new HotkeyWindow(hotkey);
        hotkeyWindow.Pressed += OnHotkeyPressed;

        ShowMessage(
            "Tiny Transcriber is ready",
            $"Press {hotkey.DisplayName} to start recording, then press it again to transcribe and paste.",
            ToolTipIcon.Info);
    }

    protected override void ExitThreadCore()
    {
        hotkeyWindow.Pressed -= OnHotkeyPressed;
        hotkeyWindow.Dispose();
        recorder.Dispose();
        statusForm.Dispose();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
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
            state = DictationState.Idle;
            notifyIcon.Text = $"Tiny Transcriber - {hotkey.DisplayName} to record";
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
        state = DictationState.Recording;
        notifyIcon.Text = "Tiny Transcriber - recording";
        statusForm.ShowRecording(hotkey.DisplayName);
        SystemSounds.Asterisk.Play();
    }

    private async Task StopTranscribeAndPasteAsync()
    {
        state = DictationState.Transcribing;
        notifyIcon.Text = "Tiny Transcriber - transcribing";
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

            state = DictationState.Idle;
            notifyIcon.Text = $"Tiny Transcriber - {hotkey.DisplayName} to record";
            statusForm.HideStatus();
        }
    }

    private void ShowMessage(string title, string text, ToolTipIcon icon)
    {
        notifyIcon.ShowBalloonTip(5000, title, text, icon);
    }

    private enum DictationState
    {
        Idle,
        Recording,
        Transcribing
    }
}
