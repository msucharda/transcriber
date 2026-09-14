using Azure.Identity;
using System.Text.Json;

namespace TinyTranscriber;

internal sealed class DictationApplicationContext : ApplicationContext
{
    private readonly NotifyIcon notifyIcon;
    private readonly HotkeyWindow hotkeyWindow;
    private readonly TrayIcons trayIcons;
    private readonly HotkeyDefinition hotkey = HotkeyDefinition.Load();
    private readonly StatusForm statusForm = new();
    private readonly Control dispatcher = new();
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly MaiTranscriptionClient transcriptionClient;
    private readonly ParagraphQueue queue;
    private readonly DictationHotkeyController hotkeyController;
    private readonly System.Windows.Forms.Timer resumeTimer = new() { Interval = 3000 };
    private readonly ToolStripMenuItem queueItem = new() { Enabled = false };
    private readonly ToolStripMenuItem pauseItem = new("Pause automatic delivery");
    private readonly ToolStripMenuItem copyItem = new("Copy ready paragraphs (pauses)");
    private readonly ToolStripMenuItem acknowledgeItem = new("I pasted the copied paragraphs");
    private readonly ToolStripMenuItem resumeItem = new("Resume delivery in 3 seconds");
    private readonly ToolStripMenuItem settingsItem = new("Settings...");
    private readonly DictationPreferencesStore preferencesStore = new(DictationPreferencesStore.DefaultPath);
    private DictationPreferences preferences = DictationPreferences.Default;
    private SettingsForm? settingsForm;
    private readonly ToolStripMenuItem exitItem = new("Exit");
    private AudioRecorder? activeRecorder;
    private volatile bool exiting;

    public DictationApplicationContext()
    {
        _ = dispatcher.Handle;
        transcriptionClient = new MaiTranscriptionClient(httpClient, new DefaultAzureCredential());
        queue = new ParagraphQueue(CreateRecorder, TranscribeAsync, new WindowsDelivery(), File.Delete);
        hotkeyController = new DictationHotkeyController(queue, NativeInput.GetActiveWindow);
        hotkeyWindow = new HotkeyWindow(hotkey);
        trayIcons = new TrayIcons();
        notifyIcon = new NotifyIcon
        {
            Icon = trayIcons.Idle,
            ContextMenuStrip = new ContextMenuStrip(),
            Visible = true
        };
        notifyIcon.ContextMenuStrip.Items.AddRange(
        [
            queueItem, new ToolStripSeparator(), pauseItem, copyItem, acknowledgeItem, resumeItem,
            new ToolStripSeparator(), settingsItem, new ToolStripSeparator(), exitItem
        ]);
        var preferencesLoaded = LoadPreferences();
        settingsItem.Click += (_, _) => OpenSettings();

        pauseItem.Click += (_, _) => { CancelResume(); queue.PauseDelivery(); };
        copyItem.Click += (_, _) => { CancelResume(); queue.CopyReadyParagraphs(); };
        acknowledgeItem.Click += (_, _) => { CancelResume(); queue.AcknowledgeCopiedParagraphs(); };
        resumeItem.Click += (_, _) => { resumeTimer.Start(); RefreshStatus(); };
        exitItem.Click += (_, _) => ExitThread();
        resumeTimer.Tick += (_, _) =>
        {
            resumeTimer.Stop();
            queue.ResumeDelivery();
        };
        queue.Changed += RefreshStatus;
        queue.Error += OnQueueError;
        hotkeyWindow.Pressed += OnHotkeyPressed;
        hotkeyWindow.Released += OnHotkeyReleased;
        RefreshStatus();
        if (preferencesLoaded)
        {
            ShowMessage(
                "Tiny Transcriber is ready",
                preferences.RecordingMode == RecordingMode.PushToTalk
                    ? $"Hold {hotkey.DisplayName} to record; release {(Keys)hotkey.VirtualKey} to transcribe."
                    : $"Press {hotkey.DisplayName} to start or stop dictation. You can record the next while one transcribes.",
                ToolTipIcon.Info);
        }
    }

    public void RequestExit() => Dispatch(ExitThread);

    private void OpenSettings()
    {
        if (settingsForm is not null)
        {
            settingsForm.Activate();
            return;
        }

        settingsForm = new SettingsForm(preferences, hotkey, next =>
        {
            if (queue.Status.Microphone != MicrophoneState.Idle)
            {
                throw new InvalidOperationException("Finish the current recording before saving.");
            }

            preferencesStore.Save(next);
            preferences = next;
            ApplyPreferences();
            RefreshStatus();
        });
        settingsForm.FormClosed += (_, _) => settingsForm = null;
        settingsForm.SetRecordingBusy(queue.Status.Microphone != MicrophoneState.Idle);
        settingsForm.Show();
    }

    private bool LoadPreferences()
    {
        var loaded = true;
        try
        {
            preferences = preferencesStore.Load();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            loaded = false;
            ShowMessage("Could not load settings",
                $"Using toggle recording and same-line continuation. Open Settings to save your choices again.\n{exception.Message}",
                ToolTipIcon.Warning);
        }

        ApplyPreferences();
        return loaded;
    }

    private void ApplyPreferences()
    {
        queue.SetSeparator(preferences.Separator);
        hotkeyController.Mode = preferences.RecordingMode;
    }

    protected override async void ExitThreadCore()
    {
        if (exiting)
        {
            return;
        }

        exiting = true;
        resumeTimer.Stop();
        resumeTimer.Dispose();
        hotkeyWindow.Pressed -= OnHotkeyPressed;
        hotkeyWindow.Released -= OnHotkeyReleased;
        hotkeyWindow.Dispose();
        settingsForm?.Dispose();
        settingsForm = null;
        queue.Changed -= RefreshStatus;
        queue.Error -= OnQueueError;
        statusForm.HideStatus();
        notifyIcon.Visible = false;
        try
        {
            // Keep the UI context alive while canceled requests release their WAV streams.
            await queue.ShutdownAsync();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Shutdown cleanup failed: {exception.Message}");
        }
        finally
        {
            statusForm.Dispose();
            notifyIcon.ContextMenuStrip?.Dispose();
            notifyIcon.Dispose();
            trayIcons.Dispose();
            httpClient.Dispose();
            dispatcher.Dispose();
            base.ExitThreadCore();
        }
    }

    private void OnHotkeyPressed(object? sender, EventArgs eventArgs)
    {
        if (exiting)
        {
            return;
        }

        if (settingsForm?.ContainsFocus == true && queue.Status.Microphone == MicrophoneState.Idle)
        {
            return;
        }

        var result = hotkeyController.Press();
        if (result == HotkeyResult.Full)
        {
            ShowMessage(
                "Both paragraph slots are busy",
                "Wait for a paragraph to finish, or recover pending work from the tray. Your accepted audio is kept.",
                ToolTipIcon.Info);
        }
    }

    private void OnHotkeyReleased(object? sender, EventArgs eventArgs)
    {
        if (!exiting)
        {
            hotkeyController.Release();
        }
    }

    private IParagraphRecorder CreateRecorder()
    {
        if (!AppSettings.TryLoad(out _, out var error))
        {
            throw new InvalidOperationException(error);
        }

        var source = new AudioRecorder();
        activeRecorder = source;
        source.LevelChanged += level => Dispatch(() =>
        {
            if (ReferenceEquals(activeRecorder, source)
                && queue.Status.Microphone == MicrophoneState.Recording)
            {
                statusForm.SetAudioLevel(level);
            }
        });
        return source;
    }

    private Task<string> TranscribeAsync(string audioPath, CancellationToken token)
    {
        if (!AppSettings.TryLoad(out var settings, out var error) || settings is null)
        {
            throw new InvalidOperationException(error);
        }

        return transcriptionClient.TranscribeAsync(audioPath, settings, token);
    }

    private void RefreshStatus()
    {
        if (exiting)
        {
            return;
        }

        var status = queue.Status;
        var presentation = DictationPresentation.From(status, hotkey.DisplayName, resumeTimer.Enabled, preferences.RecordingMode);
        notifyIcon.Icon = presentation.Recording
            ? trayIcons.Recording
            : presentation.Processing ? trayIcons.Transcribing : trayIcons.Idle;
        notifyIcon.Text = $"Tiny Transcriber - {presentation.Title} - {status.Unfinished}/{status.Capacity}";
        statusForm.UpdateStatus(presentation);
        queueItem.Text = $"{status.Unfinished}/{status.Capacity} unfinished paragraphs";
        pauseItem.Enabled = !status.DeliveryPaused || resumeTimer.Enabled;
        copyItem.Enabled = status.CanCopy;
        acknowledgeItem.Enabled = status.CanAcknowledgeCopy;
        resumeItem.Enabled = status.DeliveryPaused && !status.CanAcknowledgeCopy && !resumeTimer.Enabled;
        settingsForm?.SetRecordingBusy(status.Microphone != MicrophoneState.Idle);
        exitItem.Text = status.Unfinished > 0 ? "Exit (discard pending work)" : "Exit";
    }

    private void CancelResume()
    {
        resumeTimer.Stop();
    }

    private void OnQueueError(string title, string message) => ShowMessage(title, message, ToolTipIcon.Warning);

    private void ShowMessage(string title, string text, ToolTipIcon icon)
    {
        if (!exiting)
        {
            notifyIcon.ShowBalloonTip(5000, title, text, icon);
        }
    }

    private void Dispatch(Action action)
    {
        if (exiting)
        {
            return;
        }

        try
        {
            if (dispatcher.InvokeRequired)
            {
                dispatcher.BeginInvoke(() => { if (!exiting) { action(); } });
            }
            else
            {
                action();
            }
        }
        catch (InvalidOperationException) when (exiting)
        {
            // An already-posted meter/console callback can race with dispatcher disposal.
        }
    }

    private sealed class WindowsDelivery : IParagraphDelivery
    {
        public Task<bool> TryDeliverAsync(nint target, string text, CancellationToken cancellationToken) =>
            NativeInput.TryDeliverAsync(target, text, cancellationToken);
        public void Copy(string text) => Clipboard.SetText(text);
    }
}
