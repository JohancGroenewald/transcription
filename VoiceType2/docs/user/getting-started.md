# Getting started

VoiceType2 is run as two cooperating components:

- API host (`VoiceType2.ApiHost`)
- CLI orchestrator (`VoiceType2.App.Cli`)

Use this flow for local testing:

1. Start API host:

```powershell
dotnet run --project VoiceType2\alpha-build-1\src\VoiceType2.ApiHost\VoiceType2.ApiHost.csproj -- --mode service --urls "http://127.0.0.1:5240"
```

1. Start CLI in run mode (in a second terminal):

```powershell
dotnet run --project VoiceType2\alpha-build-1\src\VoiceType2.App.Cli\VoiceType2.App.Cli.csproj -- run --api-url "http://127.0.0.1:5240"
```

1. Use interactive CLI controls:

- `s` or `submit`
- `c` or `cancel`
- `r` or `retry`
- `status`
- `q` or `quit`

You can also run the existing helper script:

```powershell
.\scripts\run-alpha1-all.ps1 -Configuration Debug
```

from:

- `VoiceType2\alpha-build-1\scripts`

If you only need status checks, use:

```powershell
dotnet run --project VoiceType2\alpha-build-1\src\VoiceType2.ApiHost\VoiceType2.ApiHost.csproj -- --mode service --help
dotnet run --project VoiceType2\alpha-build-1\src\VoiceType2.App.Cli\VoiceType2.App.Cli.csproj -- -h
```
