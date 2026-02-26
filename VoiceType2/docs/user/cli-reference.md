# CLI reference

The CLI parses command arguments in the format:

```text
vt2 <command> [flags...]
```

For local development you usually run:

```powershell
dotnet run --project VoiceType2\alpha-build-1\src\VoiceType2.App.Cli\VoiceType2.App.Cli.csproj -- <command> [flags...]
```

Global help:

- `--help`, `-h`: print usage.

## Quick copy/paste examples

Run one complete dictation session with local managed startup:

```powershell
dotnet run --project VoiceType2\alpha-build-1\src\VoiceType2.App.Cli\VoiceType2.App.Cli.csproj -- run --managed-start true
```

Start the CLI and force use of a local API host at a custom URL:

```powershell
dotnet run --project VoiceType2\alpha-build-1\src\VoiceType2.App.Cli\VoiceType2.App.Cli.csproj -- run --mode attach --api-url http://127.0.0.1:5240
```

Start managed mode with a custom startup timeout:

```powershell
dotnet run --project VoiceType2\alpha-build-1\src\VoiceType2.App.Cli\VoiceType2.App.Cli.csproj -- run --mode managed --managed-start true --api-timeout-ms 30000 --shutdown-timeout-ms 15000
```

Use the installed CLI (`vt2`) if available on PATH:

```powershell
vt2 run --managed-start true
vt2 run --mode attach --api-url http://127.0.0.1:5240
```

Check status and stop a live session:

```powershell
vt2 status --session-id <session-id>
vt2 stop --session-id <session-id>
```

Resolve an active session:

```powershell
vt2 resolve submit --session-id <session-id>
vt2 resolve retry --session-id <session-id>
vt2 resolve cancel --session-id <session-id>
```

## `vt2 run`

```text
vt2 run [--api-url <url>] [--mode attach|managed] [--api-token <token>] [--api-timeout-ms <ms>] [--shutdown-timeout-ms <ms>] [--managed-start true|false] [--api-config <path>]
```

Defaults:

- `--api-url`: `http://127.0.0.1:5240`
- `--mode`: `attach`
- `--api-timeout-ms`: `15000`
- `--shutdown-timeout-ms`: `10000`
- `--managed-start`: `true`

`mode` behavior:

- `attach`: connect to an existing API host.
- `managed`: start a local API host process if none is running.

`managed-start` controls whether managed start is allowed.

`--api-config` is passed to managed API host startup as `--config`.

When connected, the CLI starts one dictation session and opens interactive session
controls:

- `s` / `submit`
- `c` / `cancel`
- `r` / `retry`
- `status`
- `q` / `quit`

## `vt2 status`

```text
vt2 status --session-id <id> [--api-url <url>] [--api-token <token>]
```

Gets the current state for a session and prints JSON.

## `vt2 stop`

```text
vt2 stop --session-id <id> [--api-url <url>] [--api-token <token>]
```

Stops an active session.

## `vt2 resolve`

```text
vt2 resolve <submit|cancel|retry> --session-id <id> [--api-url <url>] [--api-token <token>]
```

Sends a resolve action to an active session:

- `submit`
- `cancel`
- `retry`

## `vt2 api`

```text
vt2 api [status]
```

`status` probes API host readiness and exits with status code:

- `0`: ready
- `1`: not ready or unreachable
