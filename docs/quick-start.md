# VoiceType Quick Start

## Prerequisites

- Windows 10 or Windows 11
- .NET 9 SDK
- Working microphone
- OpenAI API key

## Setup

```powershell
dotnet --version
```

You should see a 9.x SDK before building.

If you want a single command that checks dependencies, restores, builds, and writes a report, run:

```powershell
.\scripts\install-voiceType.ps1 -InstallDotnet
```

Use `-RunTests` to include test execution and `-ConfigureHooks` to set `core.hooksPath`.

Use `-FailOnWarning` for CI-style strictness. Optional failures (such as tests/build flags marked non-blocking) will make the script exit non-zero when this flag is set.

The script writes `install-report.json` in the repo root by default.

## Build and run

```powershell
dotnet restore VoiceType/VoiceType.csproj
dotnet build VoiceType/VoiceType.csproj -c Debug
```

## VoiceType2 quick build (API-first alpha)

When implementing the new architecture, use this command set:

```powershell
dotnet restore VoiceType2/VoiceType2.ApiHost/VoiceType2.ApiHost.csproj
dotnet build VoiceType2/VoiceType2.ApiHost/VoiceType2.ApiHost.csproj -c Debug
dotnet run --project VoiceType2/VoiceType2.ApiHost/VoiceType2.ApiHost.csproj -- --mode service --urls "http://127.0.0.1:5240"
```

CLI entrypoint target for the first orchestrator:

```powershell
dotnet run --project VoiceType2/VoiceType2.App.Cli/VoiceType2.App.Cli.csproj -- run --api-url "http://127.0.0.1:5240" --mode managed
```

## Fast validation

```powershell
dotnet run --project VoiceType/VoiceType.csproj -- --help
```

Use normal run for interactive usage:

```powershell
dotnet run --project VoiceType/VoiceType.csproj
```

## Tests

```powershell
dotnet test VoiceType.Tests/VoiceType.Tests.csproj
```

Optional coverage:

```powershell
dotnet test VoiceType.Tests/VoiceType.Tests.csproj --collect:"XPlat Code Coverage"
```

## Notes

- Repo-level hooks are optional:

```powershell
git config core.hooksPath .githooks
```
