using Azure.Identity;
using System.Media;
using System.Runtime.InteropServices;

namespace TinyTranscriber;

internal sealed class DictationApplicationContext : ApplicationContext
{
    private readonly NotifyIcon notifyIcon;
    private readonly HotkeyWindow hotkeyWindow;
    private readonly TrayIcons trayIcons;
    private readonly HotkeyDefinition hotkey = HotkeyDefinition.Load();
    private readonly StatusForm statusForm = new();
    private readonly AudioRecorder recorder = new();
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly CancellationTokenSource shutdown = new();
    private readonly MaiTranscriptionClient transcriptionClient;
    private readonly TranscriptCleanupClient cleanupClient;
    private readonly PreferencesStore preferencesStore = PreferencesStore.ForCurrentUser();
    private readonly ToolStripMenuItem polishItem;
    private readonly ToolStripMenuItem copyOriginalItem;
    private readonly bool cleanupConfigured;
    private UserPreferences preferences;
    private string? lastOriginalTranscript;
    private bool exiting;
    private DictationState state;

    public DictationApplicationContext()
    {
        preferences = preferencesStore.Load();
        cleanupConfigured = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(AppSettings.CleanupDeploymentVariable));
        var credential = new DefaultAzureCredential();
        transcriptionClient = new MaiTranscriptionClient(httpClient, credential);
        cleanupClient = new TranscriptCleanupClient(httpClient, credential);
        hotkeyWindow = new HotkeyWindow(hotkey);
        trayIcons = new TrayIcons();
        polishItem = new ToolStripMenuItem(cleanupConfigured
            ? "Polish dictation"
            : "Polish dictation (not configured)")
        {
            Checked = cleanupConfigured && preferences.PolishDictation,
            Enabled = cleanupConfigured,
            ToolTipText = cleanupConfigured
                ? "Conservative text cleanup in Azure after transcription."
                : $"Set {AppSettings.CleanupDeploymentVariable}, then restart."
        };
        polishItem.Click += OnPolishClicked;
        copyOriginalItem = new ToolStripMenuItem("Copy last original transcript") { Enabled = false };
        copyOriginalItem.Click += OnCopyOriginalClicked;
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitThread();

        notifyIcon = new NotifyIcon
        {
            Icon = trayIcons.Idle,
            Text = $"Tiny Transcriber - {hotkey.DisplayName} to record",
            ContextMenuStrip = new ContextMenuStrip(),
            Visible = true
        };
        notifyIcon.ContextMenuStrip.Items.AddRange(
            [polishItem, copyOriginalItem, new ToolStripSeparator(), exitItem]);

        hotkeyWindow.Pressed += OnHotkeyPressed;
        recorder.LevelChanged += OnAudioLevelChanged;

        ShowMessage(
            "Tiny Transcriber is ready",
            $"Press {hotkey.DisplayName} to start recording, then press it again to transcribe and paste.",
            ToolTipIcon.Info);
    }

    protected override void ExitThreadCore()
    {
        if (exiting)
        {
            return;
        }

        exiting = true;
        shutdown.Cancel();
        hotkeyWindow.Pressed -= OnHotkeyPressed;
        recorder.LevelChanged -= OnAudioLevelChanged;
        hotkeyWindow.Dispose();
        recorder.Dispose();
        statusForm.Dispose();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        trayIcons.Dispose();
        httpClient.Dispose();
        shutdown.Dispose();
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
                case DictationState.Polishing:
                    SystemSounds.Beep.Play();
                    break;
            }
        }
        catch (OperationCanceledException) when (exiting)
        {
            // Exiting cancels pending requests without pasting into another application.
        }
        catch (Exception exception)
        {
            if (!exiting)
            {
                SetState(DictationState.Idle);
                statusForm.HideStatus();
                var recovery = lastOriginalTranscript is null
                    ? ""
                    : " The last original transcript is available from the tray menu.";
                ShowMessage("Dictation failed", exception.Message + recovery, ToolTipIcon.Error);
            }
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
        var cancellationToken = shutdown.Token;
        var polishEnabled = polishItem.Checked;
        string? audioPath = null;

        try
        {
            audioPath = await recorder.StopAsync();
            cancellationToken.ThrowIfCancellationRequested();

            if (!AppSettings.TryLoad(out var settings, out var error) || settings is null)
            {
                throw new InvalidOperationException(error);
            }

            lastOriginalTranscript = await transcriptionClient.TranscribeAsync(audioPath, settings, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (polishEnabled)
            {
                SetState(DictationState.Polishing);
                statusForm.ShowPolishing();
            }

            var result = await TranscriptProcessor.ProcessAsync(
                lastOriginalTranscript,
                polishEnabled,
                (text, token) => cleanupClient.CleanAsync(text, settings, token),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Clipboard.SetText(result.Text);
            await NativeInput.PasteAsync(targetWindow, cancellationToken);
            if (result.CleanupError is not null)
            {
                ShowMessage(
                    "Polishing unavailable",
                    $"Using the original transcript instead. {result.CleanupError} "
                        + "You can also copy it from the tray menu.",
                    ToolTipIcon.Warning);
            }
            else
            {
                SystemSounds.Exclamation.Play();
            }
        }
        finally
        {
            if (audioPath is not null && File.Exists(audioPath))
            {
                File.Delete(audioPath);
            }

            if (!exiting)
            {
                SetState(DictationState.Idle);
                statusForm.HideStatus();
            }
        }
    }

    private void SetState(DictationState nextState)
    {
        var (icon, text) = nextState switch
        {
            DictationState.Idle => (trayIcons.Idle, $"Tiny Transcriber - {hotkey.DisplayName} to record"),
            DictationState.Recording => (trayIcons.Recording, "Tiny Transcriber - recording"),
            DictationState.Transcribing => (trayIcons.Transcribing, "Tiny Transcriber - transcribing"),
            DictationState.Polishing => (trayIcons.Transcribing, "Tiny Transcriber - polishing"),
            _ => throw new ArgumentOutOfRangeException(nameof(nextState))
        };
        state = nextState;
        notifyIcon.Icon = icon;
        notifyIcon.Text = text;
        polishItem.Enabled = cleanupConfigured && state == DictationState.Idle;
        copyOriginalItem.Enabled = lastOriginalTranscript is not null && state == DictationState.Idle;
    }

    private void OnPolishClicked(object? sender, EventArgs eventArgs)
    {
        var updated = preferences with { PolishDictation = !preferences.PolishDictation };
        try
        {
            preferencesStore.Save(updated);
            preferences = updated;
            polishItem.Checked = updated.PolishDictation;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowMessage("Could not save preference", exception.Message, ToolTipIcon.Error);
        }
    }

    private void OnCopyOriginalClicked(object? sender, EventArgs eventArgs)
    {
        if (lastOriginalTranscript is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(lastOriginalTranscript);
            ShowMessage("Original transcript copied", "Use Ctrl+V to paste it where you need it.", ToolTipIcon.Info);
        }
        catch (ExternalException exception)
        {
            ShowMessage("Could not copy transcript", exception.Message, ToolTipIcon.Error);
        }
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
        Transcribing,
        Polishing
    }
}
