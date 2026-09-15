# Troubleshooting

The queue/recovery instructions describe next-version source. Published v0.1.0
still requires waiting for a transcription before recording the next paragraph.

## The shortcut is already registered

Another program or input method may own `Ctrl+Shift+Space`. Exit Tiny Transcriber,
choose another combination, and restart:

```powershell
setx TINY_TRANSCRIBER_HOTKEY "Ctrl+Alt+Space"
```

New user environment variables affect newly launched processes. Restart your
terminal as well as the app. Make sure you are not starting a second copy;
this version does not yet prevent multiple instances automatically.

## Nothing appears when I launch the EXE

There is no main window. Look in the notification area, including **Show hidden
icons**, for the waveform icon. Hover it for the current shortcut or use its
right-click **Exit** action. Check for a startup error dialog.

## Microphone or empty-transcript errors

Enable microphone access for desktop apps in Windows and select your intended
default input device before launching. The app has no microphone picker.
Record a short, clearly spoken sentence to distinguish setup problems from
recognition quality. Silence or a failed recording is not a successful transcript.

## Azure 401 or 403

Run `az login` for the tenant containing your Speech resource. Check that
`AZURE_SPEECH_ENDPOINT` refers to that resource and that your user has
**Cognitive Services User** on the account. Role assignments can take time to
propagate.

An old `AZURE_SPEECH_KEY` takes precedence over Entra ID. Remove it if you intend
to use the identity-based setup. Never post its value in an issue.
The [Azure setup guide](azure-setup.md) covers provisioning and permissions.

## Unsupported region, policy rejection, throttling, or timeout

MAI-Transcribe-2 is a preview and is not available in every Speech region.
Check [current region availability](https://learn.microsoft.com/azure/ai-services/speech-service/regions?tabs=llmspeech).
Respect policy denials; ask your administrator rather than bypassing policy or
enabling API keys. HTTP 429 indicates service throttling/quota constraints.
Shorter recordings and waiting before trying again may help, but do not fix
missing permissions or unsupported regions.

Failed transcriptions show a brief **Dictation failed** notification, clean up
their WAV, and release their slot. There is no retry screen or failed item to
clear. Press the shortcut to dictate again; any already recording or waiting
paragraph continues normally. Correct the underlying service/permission problem
if every attempt fails. A new recording makes a new paid request, and a timed-out
previous request may still have incurred Azure charges.

If deleting a WAV fails, the cleanup error is reported rather than blocking
dictation. A valid transcript is still delivered normally; no second
transcription request is made just to retry file cleanup.

## "All 5 dictation slots are busy"

The maximum is **five unfinished dictations including any recording**. You can
record B through E while A is still transcribing; stopped recordings wait in
order. Stopping an accepted recording always succeeds, but a sixth cannot start
until a slot is free. Text awaiting paused delivery also counts;
failed transcriptions are removed automatically.
Your already accepted audio is retained. Wait for a slot or recover work from
the tray; repeatedly pressing the shortcut cannot expand the backlog.

A shortcut pressed during microphone stopping reserves at most one next capture
if a slot is free; further presses during this brief transition are ignored.
Wait for the Listening state before speaking. This is not automatic chunking.

## "Delivery paused"

The destination was not safely available, the
clipboard was busy, or Windows rejected input. The app retains ordered text,
does not reactivate the old window, and never pastes later paragraphs ahead of it.

To keep the original destination, choose **Resume delivery in 3 seconds**, then
select the intended field in that original window and release shortcut keys.
If that window was closed, automatic delivery will not retarget another one.

For manual recovery, choose **Copy ready paragraphs (pauses)**, select a field
and paste, then choose **I pasted the copied paragraphs**. Only the successfully
copied snapshot is removed; results that finish afterward remain pending.
Delivery stays paused until you explicitly resume it. Copying again replaces
the clipboard with the current ready block, so do not acknowledge a copy that
you have not actually pasted. A failed copy keeps all results.

Input failure may mean partial injection, so inspect the field before retrying
to avoid duplicates. A successful input call is not confirmation of insertion
by the target application. Other applications may
restrict synthetic keyboard input, including elevated/admin applications;
do not run Tiny Transcriber as administrator just to work around this.

If a successful paste goes to the wrong field or browser tab within the same
window, undo it there and check the destination before trying again.
Window checks cannot distinguish individual fields or tabs.
The clipboard is not changed when an initial destination check fails, but focus
can change during a clipboard write or after the final input check. In that case
text may be on the clipboard without a paste; prior clipboard data is not restored.

## Exiting with unfinished paragraphs

Tray **Exit (discard pending work)** and terminal Ctrl+C cancel requests, discard
pending in-memory text, and delete owned recordings. There is no save prompt,
recording archive, or recovery on the next launch. Failed transcriptions clean up
their audio immediately. A crash, force termination, or cleanup error can leave
a WAV behind; see [privacy and cleanup guidance](privacy.md).

## Push-to-talk or spacing does not behave as expected

Open tray **Settings...**, choose the recording mode and the separator under
**Between queued dictations**, and choose **Save**. The default is toggle
recording with a space between overlapping dictations, not a new paragraph.
Separator changes apply only to recordings accepted after the change.

Push-to-talk stops when you release the main shortcut key (Space by default),
not when you release only its modifiers. Wait for Listening before speaking.
If you release before the previous recording has released the microphone, that
empty reservation is canceled and reported; no recording starts afterward.
**Ready to insert** means the result may be waiting for shortcut modifiers to
be released. Release Ctrl/Shift/Alt/Win as well; no clipboard change or modified
paste is attempted while they remain held.

Save is disabled while a recording is active/stopping. Failed saves keep the
settings window open and leave the previous choices unchanged. If the saved
preferences cannot be read, the app reports this and uses toggle/space defaults
for that session; save your choices again after resolving the indicated file
problem. The preferences file holds no audio or transcript history.

## Windows or your organization blocks the executable

Releases are not Authenticode-signed. Do not disable security tools or bypass
Windows/organizational trust warnings. Verify the source and release checksum,
ask your administrator when applicable, or build the reviewed source in an
approved development environment.

## Report a problem

Open a [GitHub issue](https://github.com/msucharda/transcriber/issues) with the app
version, Windows version, expected behavior, error status, and steps using
synthetic text. Redact identifiers when appropriate. Do not attach keys,
access tokens, real recordings, or private transcripts. For a vulnerability,
follow [SECURITY.md](../SECURITY.md) instead.
