# Alpha Build 1 — VoiceType2 execution docs

This folder now contains the Alpha 1 implementation kickoff package:

- `src/` contains project scaffolding (`VoiceType2.Core`, `VoiceType2.Infrastructure`, `VoiceType2.ApiHost`, `VoiceType2.App.Cli`)
- `scripts/` contains one-shot build and run helpers
- `alpha-build-1-api-construction.md`: API runtime design and build plan for Alpha 1
- `alpha-build-1-cli-orchestrator.md`: CLI orchestrator design and command contract for Alpha 1
- `RuntimeConfig.sample.json`: runtime defaults starter for future host config

Use wrappers to avoid PowerShell execution policy issues:

```powershell
cd .\scripts
.\build-alpha1.cmd -Configuration Debug
```

## Start modes (copy/paste)

## All startup modes

Start the app in one of these modes:

- `service` (API host mode): API host only.
- `attach` (CLI mode): CLI connects to an existing API host.
- `managed` (CLI mode): CLI starts API host for you if needed.
- `all` (bootstrap mode): Start API host and CLI in one command.

`service` is an API-host only mode and uses `--mode service`.

`attach` and `managed` are CLI `run` modes and use `--mode attach` or `--mode managed`.

`all` is not a CLI mode flag; it is the `run-alpha1-all` bootstrap script.

There are two CLI interface styles:

- `run` (default): plain text session menu in the terminal.
- `tui`: Spectre.Console-powered interactive menu in the terminal.

Service mode examples:

```powershell
.\scripts\run-alpha1-api.cmd -ApiUrl "http://127.0.0.1:5240"
```

```powershell
.\src\VoiceType2.ApiHost\publish\win-x64\VoiceType2.ApiHost.exe --mode service --urls "http://127.0.0.1:5240"
```

Attach mode examples:

```powershell
.\scripts\run-alpha1-cli.cmd -ApiUrl "http://127.0.0.1:5240" -Mode attach
```

```powershell
.\src\VoiceType2.App.Cli\publish\win-x64\VoiceType2.App.Cli.exe run --mode attach --api-url "http://127.0.0.1:5240"
```

Managed mode examples:

```powershell
.\scripts\run-alpha1-cli.cmd -Mode managed -ApiUrl "http://127.0.0.1:5240"
```

```powershell
.\scripts\run-alpha1-all.cmd -Configuration Debug
```

All-mode TUI examples:

```powershell
.\scripts\run-alpha1-all-tui.cmd -Configuration Debug
```

TUI mode examples:

```powershell
.\scripts\run-alpha1-tui.cmd -Mode managed -ApiUrl "http://127.0.0.1:5240"
```

```powershell
.\scripts\run-alpha1-tui.cmd -Mode attach -ApiUrl "http://127.0.0.1:5240"
```

If you prefer PowerShell directly, keep the existing calls:

```powershell
& .\build-alpha1.ps1 -Configuration Debug
```

Run all services (debug) with wrappers:

```powershell
.\run-alpha1-all.cmd -Configuration Debug
```

Run host and CLI in managed mode with wrappers:

```powershell
.\run-alpha1-api.cmd -ApiUrl "http://127.0.0.1:5240"
```

```powershell
.\run-alpha1-cli.cmd -ApiUrl "http://127.0.0.1:5240" -Mode attach
```

Recommended defaults:

Use service mode when you want explicit control over host lifetime:

```powershell
.\scripts\run-alpha1-api.cmd -ApiUrl "http://127.0.0.1:5240"
.\scripts\run-alpha1-cli.cmd -ApiUrl "http://127.0.0.1:5240" -Mode attach
```

Use managed auto mode for quick local iteration:

```powershell
.\scripts\run-alpha1-all.cmd -Configuration Debug
```

Use managed TUI bootstrap for quick iteration:

```powershell
.\scripts\run-alpha1-all-tui.cmd -Configuration Debug
```

Audio behavior (capture vs confirmation tone):

- `recordingDeviceId` controls which input device is used for live capture.
- `playbackDeviceId` controls where the confirmation tone is played.
- Audio device IDs are normalized at session start; unsupported platform, missing/invalid IDs, or devices not found result in a safe fallback path.
- On fallback, transcription still runs with an empty audio payload so the session remains functional.
- Confirmation tone is only requested when playback selection resolves to a valid playback device.

Run unit + integration-style smoke tests:

```powershell
.\test-alpha1.cmd -Configuration Debug
```

Equivalent direct PowerShell commands:

```powershell
& .\run-alpha1-all.ps1 -Configuration Debug
& .\run-alpha1-api.ps1 -ApiUrl "http://127.0.0.1:5240"
& .\run-alpha1-all-tui.ps1 -Configuration Debug
& .\run-alpha1-cli.ps1 -ApiUrl "http://127.0.0.1:5240" -Mode attach
& .\run-alpha1-tui.ps1 -ApiUrl "http://127.0.0.1:5240" -Mode managed
& .\test-alpha1.ps1 -Configuration Debug
```

Run with explicit device selection in launch scripts:

```powershell
.\scripts\run-alpha1-cli.ps1 -ApiUrl "http://127.0.0.1:5240" -Mode managed -RecordingDeviceId "rec:0" -PlaybackDeviceId "play:0"
.\scripts\run-alpha1-tui.ps1 -ApiUrl "http://127.0.0.1:5240" -Mode managed -RecordingDeviceId "rec:0" -PlaybackDeviceId "play:0"
.\scripts\run-alpha1-all.ps1 -ApiUrl "http://127.0.0.1:5240" -RecordingDeviceId "rec:0" -PlaybackDeviceId "play:0"
```

Equivalent `.cmd` wrappers:

```cmd
.\run-alpha1-cli.cmd -ApiUrl "http://127.0.0.1:5240" -Mode managed -RecordingDeviceId rec:0 -PlaybackDeviceId play:0
.\run-alpha1-tui.cmd -ApiUrl "http://127.0.0.1:5240" -Mode managed -RecordingDeviceId rec:0 -PlaybackDeviceId play:0
.\run-alpha1-all.cmd -ApiUrl "http://127.0.0.1:5240" -RecordingDeviceId rec:0 -PlaybackDeviceId play:0
```

After building the latest API binary, you can run the host directly and query devices manually to verify hardware discovery (without CLI):

```powershell
cd .\src\VoiceType2.ApiHost
dotnet run -- --mode service --urls "http://127.0.0.1:5240"
```

Then test discovery with:

```powershell
Invoke-RestMethod http://127.0.0.1:5240/v1/devices
```

Use this set as the default Alpha 1 “build pack” before adding tray/frontend orchestrators.

## Alpha 1 implementation status (2026-02-26)

- API host:
  - in-memory session registry with auth-mode policy support
  - registration/status/start/stop/resolve/session-events endpoints
  - SSE status/transcript/error event stream
  - provider-based transcription path (`ITranscriptionProvider`) with `MockTranscriptionProvider` wired for alpha runtime
- CLI orchestrator:
  - interactive run loop and top-level `status`, `stop`, `resolve`, `api` commands
  - managed API startup mode with best-effort graceful stop fallback
  - Spectre.Console-powered `tui` mode via `vt2 tui` and `vt2 --tui`
- Script support:
  - single-command launchers for run, tui, and all variants:
    - `run-alpha1-api.*`
    - `run-alpha1-cli.*`
    - `run-alpha1-tui.*`
    - `run-alpha1-all-tui.*`
- Validation and docs:
  - runtime config validation for URLs and auth mode
  - closeoff tests expanded around status/authorization behavior and config validation

## Alpha 1 feature checklist

Implemented:

- [x] API host startup mode: `service` only.
- [x] CLI run modes: `attach`, `managed`.
- [x] API bootstrapping scripts and .cmd launchers.
- [x] Session lifecycle endpoints (`POST /v1/sessions`, start/stop/resolve/get events).
- [x] SSE session event stream for status/transcript/error updates.
- [x] Runtime config parsing + validation (`RuntimeConfig`).
- [x] Auth mode handling (`none`, `token-optional`, `token-required`).
- [x] Top-level CLI commands: `run`, `status`, `stop`, `resolve`, `api`.
- [x] Top-level command `tui` and alias `--tui` for Spectre.Console TUI.
- [x] Interactive run controls: `submit`, `cancel`, `retry`, `status`, `quit`.
- [x] Run-style wrapper scripts and TUI wrapper scripts (`run-alpha1-tui`, `run-alpha1-all-tui`).
- [x] In-memory auth token generation and session registry.
- [x] Cleanup of stale/expired sessions.

Not started yet:

- [ ] Real transcription provider (currently mock only; returns fixed text).
- [ ] Tray app / desktop orchestrator integration.
- [x] Audio capture and playback pipeline scaffolding (host-device bootstrap + playback probe plumbing; recorder still mocked for transcript output).
- [ ] Persistent sessions or cross-restart durability.
- [ ] `vt2 api` management verbs beyond `status`.
- [ ] API key/credential integration for external providers.

### Closeoff checks to run

- `dotnet test VoiceType2/alpha-build-1/tests/VoiceType2.Alpha1.Tests/VoiceType2.Alpha1.Tests.csproj --configuration Debug`
- `.\scripts\test-alpha1.ps1 -Configuration Debug`

### Closeoff verification status

- `2026-02-26`: Unit/integration tests and smoke checks pass in `Debug`.

## Documentation (local readthedocs-style)

From `VoiceType2` root:

```powershell
.\scripts\run-readthedocs.ps1
```

Then open `http://localhost:8000`.

Primary pages:

- `VoiceType2/docs/user/cli-reference.md`
- `VoiceType2/docs/user/api-host-reference.md`
- `VoiceType2/docs/development/current-status.md`
