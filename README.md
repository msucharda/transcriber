# Tiny Transcriber

A small Windows tray app for bilingual Czech/English dictation using
Azure Speech's `MAI-Transcribe-2` model.

Press `Ctrl+Shift+Space` once to start recording and again to stop. The app:

1. Records the default microphone as a 16 kHz mono WAV file.
2. Sends it to the Azure Speech fast transcription REST API.
3. Optionally polishes the transcript with an Azure text model.
4. Copies the result to the clipboard.
5. Pastes it into the window where dictation was stopped.

An always-on-top status pill appears without taking keyboard focus. Its live
waveform responds to microphone volume while recording, then changes to a
processing animation while Azure transcribes the audio. A separate **Polishing**
stage appears when optional text cleanup is enabled.

The request intentionally does not set `locales`, so MAI-Transcribe-2 stays in
multilingual mode and can automatically identify Czech and English while
handling switches between them. Transcription style is set to `clean` to
remove fillers and format dictated text for readability.

> MAI-Transcribe-2 is currently an Azure public preview feature.

## Prerequisites

- Windows 10 or newer.
- An Azure Speech resource in a
  [region that supports LLM Speech](https://learn.microsoft.com/azure/ai-services/speech-service/regions?tabs=llmspeech).
- The resource endpoint and permission to use it through Microsoft Entra ID.
- Azure CLI signed in with `az login`.
- .NET 10 SDK for local builds.

## Configure

Sign in and set the Azure Speech resource endpoint as a user environment
variable:

```powershell
az login
setx AZURE_SPEECH_ENDPOINT "https://ais-tiny-transcriber-msucharda-neu.cognitiveservices.azure.com"
```

Restart the terminal and Tiny Transcriber after changing this variable. The app
uses `DefaultAzureCredential`, which can reuse the Azure CLI session. Your
identity needs the `Cognitive Services User` role on the Azure resource.

For resources that allow local API-key authentication, `AZURE_SPEECH_KEY` is
still supported. When set, that key takes precedence over Entra authentication.

### Conservative text cleanup

MAI's `clean` style already removes some fillers. The optional second step uses
a text model to remove remaining hesitations, accidental repetitions, abandoned
starts, and unambiguous self-corrections. It is instructed to keep your wording,
meaning, uncertainty, negations, numbers, technical identifiers, and Czech/English
switching rather than translating, summarizing, or answering your dictation.
Ambiguous corrections should be left unchanged. As with any model, mistakes
remain possible; check important names, numbers, and negations before sending.

The current Azure resource has a **`dictation-cleanup`** deployment of
**GPT-5.4-nano**, version `2026-03-17`, using **DataZoneStandard** in the EU.
This is token-billed, not provisioned/reserved capacity. It is configured with
10 capacity units (10,000 tokens/minute and 10 requests/minute).

To use a compatible GPT-5.4-nano deployment in the **same resource** as Speech:

```powershell
setx TINY_TRANSCRIBER_CLEANUP_DEPLOYMENT "dictation-cleanup"
```

Restart Tiny Transcriber and its launching terminal after setting this variable.
Cleanup starts enabled when a deployment is configured. Right-click the tray
icon and toggle **Polish dictation** to disable or re-enable it; the choice is
saved in `%LOCALAPPDATA%\TinyTranscriber\preferences.json`. Without a deployment,
the app retains its original speech-only behavior and this menu item is disabled.
The toggle is unavailable while dictation is in progress.

For example, "Send it on Tuesday, no, Wednesday" should become
"Send it on Wednesday." This is a conservative editing instruction, not a
guarantee that every spoken correction is understood.

**Copy last original transcript** recovers the most recent Speech result before
this second editing step; it is still MAI's `clean` output, not a verbatim recording.
The app keeps this text only in memory until the next successful transcription or
exit, never in the preferences file. Copying it overwrites the clipboard but does
not automatically paste it.

Cleanup sends the transcript to Azure in addition to the audio already sent to
Speech, adds latency and token charges, and uses the same Entra identity or
explicitly configured key. Requests do not enable stored completions (`store:
false`); Azure's normal service/abuse-monitoring policies still apply.
Cleanup has a 30-second timeout and an input-sized output budget capped at 4,096
tokens (short dictations reserve less of the deployment's rate limit). On an HTTP/auth
failure, timeout, empty response, refusal, malformed response, or truncated output,
the app uses the original transcript and shows a warning instead of silently
losing the dictation. It does not automatically retry or duplicate the paste.

## Run

```powershell
dotnet run --project .\src\TinyTranscriber\TinyTranscriber.csproj
```

Tiny Transcriber has no main window. It appears in the notification area and
can be closed from its tray icon menu. When launched with `dotnet run` from a
terminal, `Ctrl+C` also exits the app cleanly.

Look for the waveform icon in the tray (it may be under Windows' **Show hidden
icons** arrow). White bars mean ready, coral bars mean recording, and blue dots
mean transcribing or polishing. Hover for the status or shortcut; right-click for
**Polish dictation**, **Copy last original transcript**, and **Exit**.
The icons change shape as well as color and match the recording pill.

The shortcut can be changed without recompiling:

```powershell
setx TINY_TRANSCRIBER_HOTKEY "Ctrl+Alt+Space"
```

Restart Tiny Transcriber after changing it. Supported modifiers are `Ctrl`,
`Shift`, `Alt`, and `Win`; the final component can be a key such as `Space`,
`F8`, or `D`. If the configured shortcut is already reserved, Tiny Transcriber
shows an error and exits.

## Build a standalone executable

```powershell
dotnet publish .\src\TinyTranscriber\TinyTranscriber.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

The executable is created under:

```text
src\TinyTranscriber\bin\Release\net10.0-windows\win-x64\publish\
```

## Test

```powershell
dotnet test .\TinyTranscriber.slnx --configuration Release
```

The live text-cleanup check is skipped by default. To exercise the real app client
against your configured Azure deployment with synthetic Czech/English samples
(billable; allow about two minutes for rate-limit spacing):

```powershell
$env:TINY_TRANSCRIBER_LIVE_TESTS = "1"
dotnet test .\TinyTranscriber.slnx --configuration Release --filter "Category=AzureIntegration"
Remove-Item Env:\TINY_TRANSCRIBER_LIVE_TESTS
```

The original icon artwork is generated by `tools\Generate-Icons.ps1`; the
multi-resolution `.ico` files are checked in and embedded in the executable,
so no asset-generation step or loose icon files are needed to run the app.
To regenerate them on Windows:

```powershell
.\tools\Generate-Icons.ps1
```

## API details

The app uses the official endpoint:

```text
POST {endpoint}/speechtotext/transcriptions:transcribe?api-version=2025-10-15
```

Its multipart `definition` enables `MAI-Transcribe-2`, clean transcription,
and no timestamps. See the
[MAI-Transcribe documentation](https://learn.microsoft.com/azure/ai-services/speech-service/mai-transcribe?pivots=programming-language-rest)
for model availability and current preview limitations.

Optional text cleanup calls `POST {endpoint}/openai/v1/chat/completions` with
the configured deployment, isolated editing instructions and transcript, no
reasoning, and a strict JSON schema containing just `text`. See
[Azure chat completions](https://learn.microsoft.com/azure/ai-foundry/openai/how-to/chatgpt).
