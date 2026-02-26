# API host reference

The API host is the runtime service for sessions and transcription orchestration.

```powershell
dotnet run --project VoiceType2\alpha-build-1\src\VoiceType2.ApiHost\VoiceType2.ApiHost.csproj -- --mode service [--urls <url>] [--config <path>] [--help]
```

## Options

- `--mode`: only `service` is supported in Alpha 1.
- `--urls <url>`: HTTP URL(s) to bind, defaults to `http://127.0.0.1:5240`.
- `--config <path>`: path to runtime config file.
- `--help`, `-h`: print usage.

If you pass `--urls`, it overrides `RuntimeConfig.HostBinding.Urls` from the loaded config.

If `--config` is omitted, the host looks for `RuntimeConfig.json`.
If that file does not exist, it is created automatically from
`RuntimeConfig.sample.json`.
If `RuntimeConfig.sample.json` is missing, startup fails.

When loading a config file, these defaults are expected:

- `HostBinding.Urls`: `http://127.0.0.1:5240`
- `SessionPolicy.MaxConcurrentSessions`: `4`
- `SessionPolicy.DefaultSessionTimeoutMs`: `300000`
- `SessionPolicy.SessionIdleTimeoutMs`: `120000`
- `RuntimeSecurity.AuthMode`: `token-optional`
- `RuntimeSecurity.EnableCorrelationIds`: `true`
- `RuntimeSecurity.StructuredErrorEnvelope`: `true`
- `TranscriptionDefaults.Provider`: `mock`
- `TranscriptionDefaults.DefaultLanguage`: `en-US`
- `TranscriptionDefaults.DefaultPrompt`: `""`
- `TranscriptionDefaults.DefaultTimeoutMs`: `120000`

## Core endpoints (Alpha 1)

- `GET /health/live`
- `GET /health/ready`
- `POST /v1/sessions`
- `GET /v1/sessions/{sessionId}`
- `POST /v1/sessions/{sessionId}/start`
- `POST /v1/sessions/{sessionId}/stop`
- `POST /v1/sessions/{sessionId}/resolve`
- `GET /v1/devices`
- `POST /v1/sessions/{sessionId}/devices`
- `GET /v1/sessions/{sessionId}/events` (SSE)

`RuntimeSecurity.AuthMode` controls token usage:

- `none`
- `token-optional` (default)
- `token-required`
