# Privacy and data flow

Tiny Transcriber listens for its keyboard shortcut, not continuously for speech.
It opens the default microphone when you start recording and stops it when you
press the shortcut again, or release the main shortcut key in push-to-talk mode.
While the shortcut is held, the app checks that key's pressed state; it does not
record typed text or install a keyboard-logging hook.

This describes the next-version source's queued dictation workflow, not the
published v0.1.0 binary.

1. Audio is recorded as a 16 kHz, 16-bit, mono WAV in your Windows temporary
   directory, with a name like `tiny-transcriber-<random-id>.wav`.
2. At most five unfinished dictations are kept, counting the active recording.
   A stopped WAV may wait for the previous request. Only one WAV at a time is
   sent over HTTPS to the Azure Speech endpoint you configured.
   Microsoft Entra ID supplies a Cognitive Services access token. If you
   explicitly configure an API key instead, that key takes precedence.
3. Azure Speech returns text. There is no additional chat model or rewriting
   service in the app.
4. The app checks that the window where you stopped dictating still exists and
   is **already foreground** before replacing the clipboard and sending Ctrl+V.
   It never reactivates that window. A failed check leaves the clipboard alone
   and pauses delivery; ordered text stays in memory for tray recovery.
   Explicit **Copy ready paragraphs (pauses)** also replaces the clipboard and
   keeps delivery paused until acknowledgement and explicit resume.
5. Successful and failed transcription both clean up the owned WAV. A failed
   paragraph is reported and removed, freeing its slot for new dictation without
   retry or restarting the app. Clipboard/input failure instead retains the
   completed text rather than silently dropping it; that pending text continues
   to occupy a slot.
6. **Exit** and console Ctrl+C cancel work and clean up only this instance's
   owned files after pending requests release their streams. Pending text,
   and recordings are not recoverable after exit. There is no
   saved queue, transcript database, automatic retry, or application analytics.
7. Recording mode and separator choices are saved in
   `%LOCALAPPDATA%\TinyTranscriber\preferences.json`. This small preferences file
   contains no audio, transcripts, Azure endpoint, or credentials.

## Important limits

- **Cloud processing:** Azure's service terms, data handling, retention, and
  monitoring rules still apply. This app's lack of a history database does not
  imply zero retention by Azure or Windows. Consult
  [Azure Speech documentation](https://learn.microsoft.com/azure/ai-services/speech-service/)
  and your organization's policies.
- **Region:** deploy Speech in a currently supported region acceptable to you.
  Do not assume that your Azure resource group's location determines the
  Speech account's processing region.
- **Local audio:** WAV files are not encrypted by the app. User-profile/temp
  permissions and device encryption are OS protections, not app guarantees.
  A crash, forced stop, or cleanup failure can leave a WAV in `%TEMP%`.
  Cleanup failures are reported and can leave a WAV even though the paragraph
  no longer occupies a slot. Failed recordings are not intentionally archived.
  Inspect only files with this app's `tiny-transcriber-` prefix when cleaning up;
  do not delete your entire temporary directory.
- **Clipboard:** Windows clipboard history, cross-device sync, remote-desktop
  clipboard forwarding, and other applications may retain or read dictated
  text. The previous clipboard contents are not restored. A focus change or
  cancellation during the clipboard write can leave copied text without a
  paste; the final foreground check prevents the subsequent input when possible.
- **Paste destination:** checking a window is not checking a field or browser
  tab, nor authenticating the window against handle reuse. No foreground check
  can eliminate the final input race or confirm actual insertion into a field.
  Keep your intended destination selected until completion. Review text
  before submitting it, especially commands, numbers, and names.
- **Credentials:** `DefaultAzureCredential` can use supported developer
  credentials other than Azure CLI. Confirm you are using the intended Azure
  identity and tenant. Environment-variable API keys are not a secure secret
  store; prefer Entra ID and do not share terminal dumps or `.env` files.

See [the queue and recovery guide](../README.md#dictate-the-next-paragraph-without-waiting)
for exact ordering, separators, copy acknowledgement, and resume semantics.

The app requires permission to use your microphone, Azure resource, and target
application. It does not obtain other people's consent for you; obtain any
required permission before recording them.
