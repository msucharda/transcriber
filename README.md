# Tiny Transcriber

Windows dictation for Czech, English, and speech that switches between them.
Press **Ctrl+Shift+Space** to record, press it again to stop, and get the
transcript in your current application.

Tiny Transcriber is a small WinForms tray app using Azure Speech
**MAI-Transcribe-2**. It has a microphone-responsive status pill, state-aware tray
icons, and no main window. Audio is recorded only after you start dictation.

> **Next-version source:** the queued paragraph workflow below is a follow-up
> to v0.1.0. It is not included in the already published v0.1.0 binaries.

> This is an experimental, cloud-based tool. MAI-Transcribe-2 is an Azure public
> preview. You need your own Azure resource and pay Azure's usage charges.
> It is not offline, and transcription accuracy is not guaranteed.

## Download

Get a Windows x64 build from
[GitHub Releases](https://github.com/msucharda/transcriber/releases).
The ZIP includes the standalone EXE, instructions, licenses, and the Azure CLI +
Bicep setup files. A separate EXE is also provided. The self-contained build
includes .NET, so end users do not need to install the .NET SDK or runtime.

Releases are **not Authenticode-signed**. Verify downloaded bytes against the
release's SHA-256 checksum, but do not treat a checksum as a publisher signature.
Do not bypass Windows security warnings or your organization's restrictions.

```powershell
Get-FileHash .\TinyTranscriber.exe -Algorithm SHA256
```

Compare the hash for the exact file you downloaded with its entry in the
release checksum file. See [troubleshooting](docs/troubleshooting.md) if Windows
blocks the executable.

## Set up your own Azure Speech service

You need Windows 10/11 x64, a microphone, Azure CLI, and an Azure subscription
that supports MAI transcription in your chosen region.

Follow **[Azure setup: CLI + Bicep](docs/azure-setup.md)** to create your own
Speech-capable Foundry resource and assign access to your user. The template
uses **Microsoft Entra ID**, disables API-key authentication, and scopes the
**Cognitive Services User** role to that resource. No account, subscription,
endpoint, or credential belonging to this project's author is needed.

If your administrator already provided a suitable resource and permission,
skip provisioning and use its endpoint.

## First run

1. Sign in to the tenant containing your Azure Speech resource:

   ```powershell
   az login
   ```

2. Configure **your own** resource endpoint:

   ```powershell
   setx AZURE_SPEECH_ENDPOINT "https://YOUR-RESOURCE-NAME.cognitiveservices.azure.com"
   ```

3. Restart the terminal or launcher so it inherits the new environment variable,
   then launch `TinyTranscriber.exe`. Find its waveform icon in the notification
   area, possibly under **Show hidden icons**.
4. Open a text field, for example in Notepad. Press **Ctrl+Shift+Space**, speak a
   short Czech or English sentence, then press the shortcut again.
5. Keep the intended field selected until transcription finishes. The app checks
   that the destination is already foreground before copying and pasting. If it
   cannot deliver safely, it retains the text and pauses delivery; use the tray
   recovery actions below.

The window where you **stop** recording is the paste target. Window-level
checks cannot distinguish browser tabs or individual fields, and focus can still
change after a check. Review the result before sending or executing it.

## Dictate the next paragraph without waiting

Press **Ctrl+Shift+Space** to start paragraph A, again to stop it, and again to
record B while A transcribes. There is one microphone capture and at most **one
Speech request** at a time. **Two unfinished paragraphs total** are allowed,
including recording, stopping, waiting, transcribing, and undelivered
work. This is not a long recording backlog or an interview mode.

Capacity is reserved when recording is accepted. Stopping B while A is still
busy always keeps B as the single waiting paragraph. A new C is refused with a
visible waiting notification until a slot becomes free; accepted audio is never
dropped to make room. If you press again during the brief microphone-stopping
transition, one next recording is reserved when capacity permits and starts
when the device is released. Further presses during that transition do not
queue more toggles. Wait for **Listening** before speaking.

The real volume bars and recording tray icon stay primary. When a previous
paragraph is genuinely transcribing, the Listening pill also shows a subtle blue
tint, a **Transcribing** label, and small moving dots on the right. Its completion
does not reset the meter or hide a newer recording. Paused delivery is
static; Windows reduced-animation and high-contrast preferences are respected
at startup. No completion sound is played into a recording.

**If dictation fails:** a brief notification explains the failure, the failed
paragraph is removed and its WAV is cleaned up, and its slot is immediately
available again. There is no retry screen or failed-recording backlog. You can
dictate again without restarting; an already recording or waiting paragraph
continues normally. An empty transcript counts as a failure. Completed text
awaiting safe delivery is different: it stays available for recovery below.

**Order and separators:** accepted paragraphs are transcribed and delivered
FIFO. Each one's destination is captured when you press to **stop** it, before
the asynchronous device stop. A burst continues while any paragraph remains
unfinished; a recording accepted after the count reaches zero starts a new
burst. Consecutive automatic deliveries in the same burst to the same captured
window are separated by exactly two Windows newlines (`\r\n\r\n`). The first
delivery, a different target, and a new burst have no added leading separator.
The returned transcript itself is not rewritten or trimmed.

### Recover pending work from the tray

Automatic delivery **never activates an old window**. The original window must
still exist and already be foreground, with keyboard modifiers released. A failed
destination check, clipboard operation, or input operation pauses delivery and
retains the result; later paragraphs cannot paste ahead of it. Already accepted
audio may finish transcribing while delivery is paused, within the two-slot
limit. A failed transcription is removed without blocking subsequent requests.

Right-click the tray icon:

| Action | Effect |
|---|---|
| **Pause automatic delivery** | Keep results without any further automatic clipboard changes or paste attempts. |
| **Copy ready paragraphs (pauses)** | Copy the consecutive ready results at the front as one block, separated by blank lines, regardless of their original targets. No leading blank line is added. Not-yet-ready audio is not skipped. Work remains counted and automatic delivery stays paused. |
| **I pasted the copied paragraphs** | After you paste manually, remove **only the last successfully copied snapshot**, not results completed afterward. This does not resume delivery. |
| **Resume delivery in 3 seconds** | Explicitly re-enable delivery after a short delay to select the original field. It does not retarget or activate anything. Unacknowledged copied text must be acknowledged first. |

If copying fails, results remain available. Copying never authorizes automatic
delivery to overwrite your clipboard before your manual paste. Acknowledging a
manual copy resets automatic separator continuity; add spacing manually when
combining separately copied blocks. Review the destination before retrying an
input failure: Windows can accept only part of the key sequence, and the app
cannot confirm whether a text field actually inserted the text.

Window checks **cannot isolate fields, documents, or browser tabs inside one
HWND**, detect a reused handle, or eliminate the final focus race. When a check
already fails, the clipboard is untouched. A focus/cancellation change during
the clipboard write can still leave the transcript on the clipboard without
pasting. Check the field and clipboard rather than assuming insertion.

Right-click **Exit** (**Exit (discard pending work)** when busy) to quit. When
running from a terminal with `dotnet run`, Ctrl+C requests the same shutdown.
Neither prompts to save: pending audio/text is discarded, requests are canceled,
and owned temporary WAVs are cleaned up after the request releases them. There
is **no recovery after app exit** or permanent transcript/audio archive.

## Settings

Settings use environment variables; restart the app after changing them.

| Variable | Purpose |
|---|---|
| `AZURE_SPEECH_ENDPOINT` | Required HTTPS endpoint for your own Speech resource. |
| `TINY_TRANSCRIBER_HOTKEY` | Optional shortcut; default `Ctrl+Shift+Space`. |
| `AZURE_SPEECH_KEY` | Optional legacy key authentication. When set, it overrides Entra ID. Not used by the supplied deployment. |

For example, change a conflicting shortcut:

```powershell
setx TINY_TRANSCRIBER_HOTKEY "Ctrl+Alt+Space"
```

Supported modifiers are `Ctrl`, `Shift`, `Alt`, and `Win`, followed by a key such
as `Space`, `F8`, or `D`. A shortcut already reserved by another app causes a
startup error. There is currently no settings window or microphone picker;
recording uses your Windows default input device.

The app uses `DefaultAzureCredential`, which can reuse your Azure CLI login
and other supported developer credentials. Prefer Entra ID; do not distribute
keys or enable local authentication to get around organizational policy.

## What happens to your audio and text

The app records a temporary 16 kHz mono WAV, sends it over HTTPS to your
configured Azure Speech service, and puts the returned text on the Windows
clipboard only for guarded delivery or explicit copy. Both successful and failed
transcription clean up their WAV; failed recordings are not kept for retry.
Pending text exists only in memory until delivery, manual
acknowledgement, or exit. Abnormal termination or cleanup failure can leave a WAV.

MAI's `clean` transcription style is enabled, and `locales` is intentionally
omitted for automatic language detection and code switching. There is **no
separate text-editing/chat-model step**.

Clipboard history, cloud clipboard sync, other applications, and Azure's service
policies affect confidentiality and retention. The app does not restore the
previous clipboard. Read **[privacy and data flow](docs/privacy.md)** before
dictating sensitive content.

## Build from source

Install the .NET SDK version in [`global.json`](global.json), then run:

```powershell
dotnet restore .\TinyTranscriber.slnx --locked-mode
dotnet test .\TinyTranscriber.slnx --configuration Release --no-restore
dotnet run --project .\src\TinyTranscriber\TinyTranscriber.csproj
```

The restore audits direct and transitive NuGet packages. Vulnerabilities and
unavailable audit data fail the build instead of silently passing.
The published EXE contains its own runtime; a new build is needed to include
runtime security updates.

See **[maintainer release instructions](docs/releasing.md)** for packaging and
the tag-driven GitHub Actions workflow. No Azure credentials are required in CI,
and CI does not deploy a Speech service or make paid transcription calls.

The original tray artwork is generated by `tools\Generate-Icons.ps1`. Its
multi-resolution icons are embedded in the executable; no loose image files are
required at runtime.

## Help, security, and license

- [Troubleshooting](docs/troubleshooting.md): shortcut conflicts, microphone
  setup, Azure errors, queued-paragraph recovery, and Windows trust warnings.
- [Report a bug](https://github.com/msucharda/transcriber/issues) using synthetic
  text and without credentials or recordings.
- [Security policy](SECURITY.md): private vulnerability reporting and limitations.
- [MIT license](LICENSE), with [third-party notices](THIRD-PARTY-NOTICES.md).

For the service contract and current regions, see Microsoft's
[MAI-Transcribe documentation](https://learn.microsoft.com/azure/ai-services/speech-service/mai-transcribe?pivots=programming-language-rest).
