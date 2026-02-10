# VoiceType

VoiceType is a lightweight Windows tray app for push-to-talk dictation using OpenAI speech-to-text.

Press a hotkey, speak, and VoiceType transcribes your audio and pastes the text into the active app.

## Features

- Global hotkey dictation: `Ctrl+Shift+Space`
- Tray app UX with status overlay notifications
- Listening HUD stays visible while recording (live mic level meter or simple spinner mode)
- Clipboard-based text injection with optional auto-Enter
- Voice commands (optional, per-command toggle): `open settings`, `exit app`, `enable auto-enter`, `disable auto-enter`, `send`
- Optional Surface Pen secondary hotkey (`F13`-`F24`, `LaunchApp1`, `LaunchApp2`)
- Single-instance behavior with remote close/replace flags
- Settings UI with API key, model, logging, and voice command toggles
- Version and uptime display (tray menu + settings)
- API key protection at rest via Windows DPAPI (`CurrentUser`)
- Debug file logging toggle (off by default)

## Requirements

- Windows 10 or Windows 11
- .NET 9 SDK (or compatible .NET 9 runtime for published builds)
- Working microphone
- OpenAI API key

## Quick Start

1. Build:

```powershell
dotnet build VoiceType/VoiceType.csproj
```

2. Run:

```powershell
dotnet run --project VoiceType/VoiceType.csproj
```

3. In the tray icon menu, open `Settings...` and configure your API key, model, and optional behavior toggles.

4. Use dictation by pressing `Ctrl+Shift+Space` to start recording, then pressing it again to stop and transcribe.

### Standalone Release Build

Publish a self-contained Windows x64 release (single-file):

```powershell
dotnet publish VoiceType/VoiceType.csproj -c Release
```

Publish output:

- `VoiceType/bin/Release/net9.0-windows/win-x64/publish/VoiceType.exe`

## Settings

VoiceType settings are stored at:

- `%LOCALAPPDATA%\VoiceType\config.json`

Available settings:

- `API Key`: OpenAI API key
- `Transcription model`: `whisper-1`, `gpt-4o-transcribe`, `gpt-4o-mini-transcribe`
- `Press Enter after pasting text`
- `Enable file logging (debug only)`
- `Show popup notifications` (enable/disable)
- `Popup duration (ms)` (500-60000)
- `HUD opacity (%)` (50-100)
- `HUD width (%)` (35-90)
- `HUD font size (pt)` (9-22)
- `Use simple mic spinner (instead of level meter)`
- `Enable Surface Pen hotkey` (enable/disable)
- `Surface Pen key` (`F13`-`F24`, `LaunchApp1`, `LaunchApp2`; default `F20`)
- `Pen button validator` (shows last detected pen key while Settings is focused)
- `Voice command: "open settings"` (enable/disable)
- `Voice command: "exit app"` (enable/disable)
- `Voice commands: "enable/disable auto-enter"` (enable/disable)
- `Voice command: "send"` (enable/disable; sends Enter key)
- `App Info`: version, process start time, and live uptime

Voice commands are matched as exact phrases after trimming punctuation and case normalization.

## CLI Flags

- `--test`: dry-run microphone and transcription test in console mode
- `--help`, `-h`: show CLI usage text and exit
- `--version`, `-v`: print app version and exit
- `--listen`: trigger dictation in an existing instance (or start app and begin listening)
- `--submit`: send Enter key through an existing VoiceType instance
- `--close`: request graceful shutdown of an existing VoiceType instance
- `--replace-existing`: close running instance and start this one
- `--pin-to-taskbar`: best-effort pin of current VoiceType executable to Windows taskbar
- `--unpin-from-taskbar`: best-effort unpin of current VoiceType executable from Windows taskbar
- `--create-activate-shortcut`: create `VoiceTypeActivate.exe.lnk` next to the executable (targets `--listen`)
- `--create-submit-shortcut`: create `VoiceTypeSubmit.exe.lnk` next to the executable (targets `--submit`)

Build automation:

- On Windows `.exe` builds, VoiceType now auto-creates both `VoiceTypeActivate.exe.lnk` and `VoiceTypeSubmit.exe.lnk` in the output folder.

## Single-Instance Behavior

- Launching VoiceType normally while it is already running triggers dictation in the existing instance.
- Use `--listen` to trigger dictation in the running instance.
- Use `--close` for remote graceful shutdown (finishes in-flight recording/transcription first).
- Use `--replace-existing` when you explicitly want handoff/restart behavior.

## Data and Security Notes

- API keys are stored encrypted with DPAPI for the current Windows user.
- Transcribed text is not logged by default.
- Debug logs are written only when enabled, at `%LOCALAPPDATA%\VoiceType\voicetype.log`.
- Audio is sent to OpenAI for transcription when dictation is submitted.

## Troubleshooting

- Hotkey does not work: another app may already own `Ctrl+Shift+Space`.
- Text is copied but not pasted: no valid paste target was focused. Use `Ctrl+V` manually.
- Tray icon appears stuck after crash/force kill: this is a Windows tray refresh artifact. Hovering over the icon usually clears it.

## Project Layout

- `VoiceType/Program.cs`: app entrypoint, single-instance and CLI flags
- `VoiceType/TrayContext.cs`: tray app lifecycle, hotkey flow, voice commands
- `VoiceType/AudioRecorder.cs`: microphone capture (NAudio)
- `VoiceType/TranscriptionService.cs`: OpenAI transcription client
- `VoiceType/TextInjector.cs`: clipboard/paste injection
- `VoiceType/SettingsForm.cs`: settings UI
- `VoiceType/AppConfig.cs`: config load/save and API key protection
- `VoiceType/OverlayForm.cs`: on-screen status notifications
