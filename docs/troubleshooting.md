# Troubleshooting

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

Failed recordings are normally deleted; there is no automatic retry or saved
audio queue. Record again when the underlying issue is resolved.

## "Transcript copied, not pasted"

The app could not verify the destination window. It deliberately did not send
Ctrl+V. Select the intended field and paste manually. Other applications may
restrict synthetic keyboard input, including elevated/admin applications;
do not run Tiny Transcriber as administrator just to work around this.

If a successful paste goes to the wrong field or browser tab within the same
window, undo it there and check the destination before trying again.
Window checks cannot distinguish individual fields or tabs.

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
