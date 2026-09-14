# Tiny Transcriber

Windows dictation for Czech, English, and speech that switches between them.
Press **Ctrl+Shift+Space** to record, press it again to stop, and get the
transcript in your current application.

Tiny Transcriber is a small WinForms tray app using Azure Speech
**MAI-Transcribe-2**. It has a microphone-responsive status pill, state-aware tray
icons, and no main window. Audio is recorded only after you start dictation.

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
5. Keep the intended field selected until transcription finishes. The app copies
   the text and attempts to paste it. If it cannot verify the destination window,
   it leaves the text on the clipboard and tells you to paste manually.

The window where you **stop** recording is the paste target. Window-level
checks cannot distinguish browser tabs or individual fields, and focus can still
change after a check. Review the result before sending or executing it.

Right-click the tray icon and choose **Exit** to quit. When running from a
terminal with `dotnet run`, Ctrl+C requests shutdown.

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
clipboard. Normal completion and handled failures delete the WAV; abnormal
termination can leave a recording behind.

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
  setup, Azure errors, clipboard fallback, and Windows trust warnings.
- [Report a bug](https://github.com/msucharda/transcriber/issues) using synthetic
  text and without credentials or recordings.
- [Security policy](SECURITY.md): private vulnerability reporting and limitations.
- [MIT license](LICENSE), with [third-party notices](THIRD-PARTY-NOTICES.md).

For the service contract and current regions, see Microsoft's
[MAI-Transcribe documentation](https://learn.microsoft.com/azure/ai-services/speech-service/mai-transcribe?pivots=programming-language-rest).
