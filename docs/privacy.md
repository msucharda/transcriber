# Privacy and data flow

Tiny Transcriber listens for its keyboard shortcut, not continuously for speech.
It opens the default microphone when you start recording and stops it when you
press the shortcut again.

1. Audio is recorded as a 16 kHz, 16-bit, mono WAV in your Windows temporary
   directory, with a name like `tiny-transcriber-<random-id>.wav`.
2. The WAV is sent over HTTPS to the Azure Speech endpoint you configured.
   Microsoft Entra ID supplies a Cognitive Services access token. If you
   explicitly configure an API key instead, that key takes precedence.
3. Azure Speech returns text. There is no additional chat model or rewriting
   service in the app.
4. The text replaces the current clipboard contents. The app attempts to
   reactivate and check the window where you stopped dictating, then sends
   Ctrl+V. If the destination cannot be verified, it skips automatic paste and
   asks you to paste manually.
5. The temporary recording is deleted on the normal path, including handled
   transcription failures. The app does not intentionally keep a transcript
   history or send application analytics.

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
  Inspect only files with this app's `tiny-transcriber-` prefix when cleaning up;
  do not delete your entire temporary directory.
- **Clipboard:** Windows clipboard history, cross-device sync, remote-desktop
  clipboard forwarding, and other applications may retain or read dictated
  text. The previous clipboard contents are not restored.
- **Paste destination:** checking a window is not checking a field or browser
  tab. Keep your intended destination selected until completion. Review text
  before submitting it, especially commands, numbers, and names.
- **Credentials:** `DefaultAzureCredential` can use supported developer
  credentials other than Azure CLI. Confirm you are using the intended Azure
  identity and tenant. Environment-variable API keys are not a secure secret
  store; prefer Entra ID and do not share terminal dumps or `.env` files.

The app requires permission to use your microphone, Azure resource, and target
application. It does not obtain other people's consent for you; obtain any
required permission before recording them.
