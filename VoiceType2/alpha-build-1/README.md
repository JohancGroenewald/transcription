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
