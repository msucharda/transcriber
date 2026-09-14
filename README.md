# Tiny Transcriber

A small Windows tray app for bilingual Czech/English dictation using
Azure Speech's `MAI-Transcribe-2` model.

Press `Ctrl+Shift+Space` once to start recording and again to stop. The app:

1. Records the default microphone as a 16 kHz mono WAV file.
2. Sends it to the Azure Speech fast transcription REST API.
3. Copies the transcript to the clipboard.
4. Pastes it into the window where dictation was stopped.

An always-on-top status pill appears without taking keyboard focus. Its live
waveform responds to microphone volume while recording, then changes to a
processing animation while Azure transcribes the audio.

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
still supported as an optional fallback.

## Run

```powershell
dotnet run --project .\src\TinyTranscriber\TinyTranscriber.csproj
```

Tiny Transcriber has no main window. It appears in the notification area and
can be closed from its tray icon menu.

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

## API details

The app uses the official endpoint:

```text
POST {endpoint}/speechtotext/transcriptions:transcribe?api-version=2025-10-15
```

Its multipart `definition` enables `MAI-Transcribe-2`, clean transcription,
and no timestamps. See the
[MAI-Transcribe documentation](https://learn.microsoft.com/azure/ai-services/speech-service/mai-transcribe?pivots=programming-language-rest)
for model availability and current preview limitations.
