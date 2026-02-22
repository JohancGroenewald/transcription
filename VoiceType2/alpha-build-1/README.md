# Alpha Build 1 — VoiceType2 execution docs

This folder now contains the Alpha 1 implementation kickoff package:

- `src/` contains project scaffolding (`VoiceType2.Core`, `VoiceType2.Infrastructure`, `VoiceType2.ApiHost`, `VoiceType2.App.Cli`)
- `scripts/` contains one-shot build and run helpers
- `alpha-build-1-api-construction.md`: API runtime design and build plan for Alpha 1
- `alpha-build-1-cli-orchestrator.md`: CLI orchestrator design and command contract for Alpha 1
- `RuntimeConfig.sample.json`: runtime defaults starter for future host config

Quick build (one shot):

```powershell
& .\scripts\build-alpha1.ps1 -Configuration Debug
```

Full bootstrap (build + launch both components):

```powershell
& .\scripts\run-alpha1-all.ps1 -Configuration Debug
```

Run unit + integration-style smoke tests:

```powershell
& .\scripts\test-alpha1.ps1 -Configuration Debug
```

Run host:

```powershell
& .\scripts\run-alpha1-api.ps1 -ApiUrl "http://127.0.0.1:5240"
```

Run CLI:

```powershell
& .\scripts\run-alpha1-cli.ps1 -ApiUrl "http://127.0.0.1:5240" -Mode attach
```

Use this set as the default Alpha 1 “build pack” before adding tray/frontend orchestrators.

## Alpha 1 implementation status (2026-02-22)

- API host:
  - in-memory session registry with auth-mode policy support
  - registration/status/start/stop/resolve/session-events endpoints
  - SSE status/transcript/error event stream
  - provider-based transcription path (`ITranscriptionProvider`) with `MockTranscriptionProvider` wired for alpha runtime
- CLI orchestrator:
  - interactive run loop and top-level `status`, `stop`, `resolve`, `api` commands
  - managed API startup mode with best-effort graceful stop fallback
- Validation and docs:
  - runtime config validation for URLs and auth mode
  - closeoff tests expanded around status/authorization behavior and config validation

### Closeoff checks to run

- `dotnet test VoiceType2/alpha-build-1/tests/VoiceType2.Alpha1.Tests/VoiceType2.Alpha1.Tests.csproj --configuration Debug`
- `.\scripts\test-alpha1.ps1 -Configuration Debug`

### Closeoff verification status

- `2026-02-22`: Unit/integration tests and smoke checks passed in `Debug`.
