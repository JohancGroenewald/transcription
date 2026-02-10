# VoiceType

VoiceType is a lightweight Windows tray app for push-to-talk dictation using OpenAI speech-to-text.

Press a hotkey, speak, and VoiceType transcribes your audio and pastes text into the active app.

## LLM-Friendly Repo Facts

- Platform: Windows only (`net9.0-windows`, WinForms).
- Main project: `VoiceType/VoiceType.csproj`.
- App type: tray app with global hotkeys and single-instance signaling.
- Default dictation hotkey: `Ctrl+Shift+Space`.
- Config file: `%LOCALAPPDATA%\VoiceType\config.json`.
- No API keys are stored in the repository. API keys are entered in Settings and encrypted with DPAPI.

## Requirements

- Windows 10 or Windows 11
- .NET 9 SDK (for build/run from source)
- Working microphone
- OpenAI API key

## Build From Fresh Clone

```powershell
git clone https://github.com/JohancGroenewald/transcription.git
cd transcription
dotnet restore VoiceType/VoiceType.csproj
dotnet build VoiceType/VoiceType.csproj -c Debug
dotnet run --project VoiceType/VoiceType.csproj
```

### Optional: Enable Repo Git Hooks

This repo includes `.githooks` scripts used by the maintainer workflow (for example auto-version bump and app restart after commit).

```powershell
git config core.hooksPath .githooks
```

## First Run Setup

1. Right-click the VoiceType tray icon and open `Settings...`.
2. Enter your OpenAI API key.
3. Choose a transcription model.
4. Test dictation with `Ctrl+Shift+Space` (press once to start, once to stop).

## Features

- Global hotkey dictation: `Ctrl+Shift+Space`
- Tray UX with on-screen HUD notifications
- Listening HUD with mic meter or simple spinner mode
- Clipboard-based text injection with optional auto-send (Enter)
- Voice commands (optional, per-command toggle): `open settings`, `exit app`, `auto-send yes`, `auto-send no`, `send`, `show voice commands`
- Optional Surface Pen secondary hotkey (`F13`-`F24`, `LaunchApp1`, `LaunchApp2`)
- Auto-generated launcher links for hardware buttons (`VoiceTypeActivate.exe.lnk`, `VoiceTypeSubmit.exe.lnk`)
- Single-instance behavior with remote close/listen/submit/replace flags
- Settings UI with API key, model, logging, HUD, and voice-command controls
- Version and uptime display (tray + settings)
- API key encryption at rest via DPAPI

## Build and Publish

Debug build:

```powershell
dotnet build VoiceType/VoiceType.csproj -c Debug
```

Self-contained Release publish (single file, win-x64):

```powershell
dotnet publish VoiceType/VoiceType.csproj -c Release
```

Release output:

- `VoiceType/bin/Release/net9.0-windows/win-x64/publish/VoiceType.exe`

## Markdown Linting

If `markdownlint` is available in your environment, use it to keep docs consistent.

Check availability (VS Code extension):

```powershell
code --list-extensions | findstr /i markdownlint
```

Run markdown lint (CLI):

```powershell
npx -y markdownlint-cli README.md docs/archive/transcription-notes.md
```

Auto-fix supported issues:

```powershell
npx -y markdownlint-cli --fix README.md docs/archive/transcription-notes.md
```

Notes:

- Repo rules are in `.markdownlint.json`.
- `MD013` (line length) is intentionally disabled for readability of technical docs.

## Settings

VoiceType settings are stored at:

- `%LOCALAPPDATA%\VoiceType\config.json`

Available settings:

- `API Key`: OpenAI API key
- `Transcription model`: `whisper-1`, `gpt-4o-transcribe`, `gpt-4o-mini-transcribe`
- `Press Enter after pasting text`
- `Enable file logging (debug only)`
- `Show popup notifications`
- `Popup duration (ms)` (500-60000)
- `HUD opacity (%)` (50-100)
- `HUD width (%)` (35-90)
- `HUD font size (pt)` (9-22)
- `Show HUD border line`
- `Use simple mic spinner (instead of level meter)`
- `Enable Surface Pen hotkey`
- `Surface Pen key` (`F13`-`F24`, `LaunchApp1`, `LaunchApp2`; default `F20`)
- `Pen button validator` (shows last detected pen key while Settings is focused)
- `Voice command: "open settings"`
- `Voice command: "exit app"`
- `Voice commands: "auto-send yes/no"`
- `Voice command: "send"` (sends Enter key)
- `Voice command: "show voice commands"`
- `App Info`: version, process start time, and uptime

Voice commands are matched as exact phrases after trimming punctuation and normalizing case.

## CLI Flags

- `--test`: dry-run microphone and transcription test in console mode
- `--help`, `-h`: show CLI usage and exit
- `--version`, `-v`: print app version and exit
- `--listen`: trigger dictation in an existing instance (or start app and begin listening)
- `--submit`: send Enter through an existing VoiceType instance
- `--close`: request graceful shutdown of an existing instance
- `--replace-existing`: close running instance and start this one
- `--pin-to-taskbar`: best-effort pin current executable to taskbar
- `--unpin-from-taskbar`: best-effort unpin current executable from taskbar
- `--create-activate-shortcut`: create `VoiceTypeActivate.exe.lnk` next to the executable (`--listen`)
- `--create-submit-shortcut`: create `VoiceTypeSubmit.exe.lnk` next to the executable (`--submit`)

Build automation:

- On Windows `.exe` builds, VoiceType auto-creates both `.lnk` files in the output folder.
- `VoiceTypeLinks.bat` is included in build/publish output to regenerate both links with one command.

## Hardware Button Linking

You can bind generated `.lnk` files to Surface Pen or any programmable input device (mouse buttons, macro keyboards, Stream Deck, foot pedals, etc.).

1. Build VoiceType in your chosen configuration (`Debug` or `Release`).
1. In that output folder, use:

   - `VoiceTypeActivate.exe.lnk` -> `VoiceType.exe --listen`
   - `VoiceTypeSubmit.exe.lnk` -> `VoiceType.exe --submit`

1. If links are missing, regenerate from that same folder:

```powershell
.\VoiceTypeLinks.bat
```

Or run the flags directly:

```powershell
.\VoiceType.exe --create-activate-shortcut
.\VoiceType.exe --create-submit-shortcut
```

1. Map device buttons to those `.lnk` files:

   - Surface Pen: in Windows Pen settings, set action to `Open a program` and choose the `.lnk`.
   - Other devices: in device software, set action to launch the `.lnk`.

Recommended mapping:

- Primary button -> `VoiceTypeActivate.exe.lnk` (start/stop listening)
- Secondary button -> `VoiceTypeSubmit.exe.lnk` (send Enter)

Note: `--submit` targets a running VoiceType instance. If VoiceType is not running, no Enter is sent.

## Single-Instance Behavior

- Launching VoiceType normally while already running triggers dictation in the existing instance.
- `--listen` triggers dictation in the running instance.
- `--close` requests graceful shutdown (finishes in-flight recording/transcription first).
- `--replace-existing` performs explicit handoff/restart behavior.

## Data and Security Notes

- API keys are encrypted with Windows DPAPI for the current user account.
- Transcribed text is not logged by default.
- Debug logs are written only when enabled: `%LOCALAPPDATA%\VoiceType\voicetype.log`.
- Audio is sent to OpenAI when dictation is submitted.

## Troubleshooting

- Hotkey does not work: another app may already own `Ctrl+Shift+Space`.
- Text is copied but not pasted: no valid target had focus. Use `Ctrl+V` manually.
- Tray icon appears stuck after crash/force kill: this is a Windows tray refresh artifact.

## Project Layout

- `VoiceType/Program.cs`: app entrypoint, single-instance routing, CLI flags
- `VoiceType/TrayContext.cs`: tray lifecycle, hotkeys, dictation flow, voice commands
- `VoiceType/AudioRecorder.cs`: microphone capture (NAudio)
- `VoiceType/TranscriptionService.cs`: OpenAI transcription client
- `VoiceType/TextInjector.cs`: clipboard and paste/send-key injection
- `VoiceType/SettingsForm.cs`: settings UI
- `VoiceType/AppConfig.cs`: config load/save and API-key protection
- `VoiceType/OverlayForm.cs`: on-screen HUD notifications
