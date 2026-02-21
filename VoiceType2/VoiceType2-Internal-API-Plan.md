# VoiceType2 — Internal API Migration Plan (C#)

## 1) Deep study of the current VoiceType implementation

This repo already has a stable Windows tray dictation app with clear separation between:

- Process bootstrap + CLI + single-instance routing (`VoiceType/Program.cs`)
- Runtime orchestration and dictation flow (`VoiceType/TrayContext.cs`)
- Hotkey handling + overlay + settings reload (`VoiceType/TrayContext.cs`)
- Audio capture (`VoiceType/AudioRecorder.cs`)
- Transcription provider (`VoiceType/TranscriptionService.cs`)
- Paste/injection (`VoiceType/TextInjector.cs`)
- Config persistence + DPAPI key protection (`VoiceType/AppConfig.cs`)
- Remote command policy/dispatch (`VoiceType/RemoteCommandManager.cs`)

Current end-to-end path is:

1. User presses trigger hotkey → `TrayContext` starts/stops recording.
2. Audio buffer is built by `AudioRecorder` (NAudio `WaveInEvent`) and finalized to WAV in `AudioRecorder.Stop()`.
3. `TrayContext` calls `TranscriptionService.TranscribeAsync()` (OpenAI SDK client).
4. Response text is sanitized (`PretextDetector`) then optionally parsed as a voice command (`VoiceCommandParser`).
5. Text is inserted via clipboard/paste (`TextInjector`) after preview countdown.

Constraints that matter for migration:

- `net9.0-windows` + WinForms + user32 interop + native clipboard/paste mechanics.
- Current dependency on OpenAI .NET package is concentrated in one place (`TranscriptionService`), but call site assumptions are embedded in `TrayContext`.
- Config is currently file-based at `%LOCALAPPDATA%\VoiceType\config.json`, with API key decryption via DPAPI on load/save.
- Existing tests cover config normalization, command parsing, preview flow, and prompt sanitization, not API transport internals.

## 2) VoiceType2 objective

Build a new standalone version of the app with the same user goals as VoiceType, while implementing everything in a clean-room C# architecture.

Primary goals:

- Preserve user-facing behavior (tray lifecycle, hotkeys, overlay, preview, injection, commands).
- Put transcription behind an internal API abstraction from day one.
- Maintain testability with injected fake transport clients.
- Keep VoiceType2 independent from VoiceType1 runtime, startup flow, and transport implementation.

## 3) Recommended C# architecture (for `VoiceType2`)

### 3.1 Layered design

- `VoiceType2.App` (WinForms host)
  - Implement independently with lessons learned from VoiceType.
  - Keep Windows-specific concerns in this layer.
- `VoiceType2.Core`
  - Audio pipeline interfaces
  - Command/parser layer interfaces
  - Preview/injection coordinator interfaces
  - Domain models
- `VoiceType2.Infrastructure.Transcription`
  - Internal API client implementation
  - Auth/token plumbing
  - Retry + telemetry + timeout policies
- `VoiceType2.Infrastructure.Config`
  - Config model extension for internal API endpoint/auth
- `VoiceType2.Infrastructure.TestDoubles`
  - Mock/fake implementations for integration-less tests.

### 3.1.1 Internal API data flow (Mermaid)

```mermaid
flowchart TB
    %% Force a wider vertical rhythm for readability
    %% in case VS Code collapses nested labels.
    linkStyle default interpolate basis

    subgraph "VoiceType2.App"
        appStart["App startup and dependency wiring"]
        hotkeys["Global hotkeys and tray actions"]
        overlay["Overlay/popup coordinator"]
        injector["Clipboard/text injection"]
    end

    subgraph "VoiceType2.Core"
        capture["Audio capture service"]
        codec["Audio buffer writer (WAV)"]
        preview["Preview coordinator"]
        parser["Voice command parser"]
        sanitizer["Response sanitizer (prompt + directives)"]
        requestModel["Transcription request model"]
        resultModel["Transcription result model"]
        contract["ITranscriptionProvider interface"]
    end

    subgraph "VoiceType2.Infrastructure"
        provider["InternalApiTranscriptionProvider"]
        auth["Auth handler (API key / bearer / mTLS)"]
        transport["HttpClient transport"]
        retry["Retry and timeout policy"]
        endpoint["Internal API endpoint"]
        testDoubles["Mock/Stub provider implementations"]
    end

    hotkeys --> capture
    capture --> codec
    codec --> contract
    contract -->|"StartTranscription(correlationId, request)"| provider
    provider --> auth
    auth --> transport
    transport --> retry
    retry --> endpoint
    endpoint -->|"Result text + metadata"| resultModel
    resultModel --> sanitizer
    sanitizer --> preview
    preview --> overlay
    preview --> injector
    preview --> parser
    parser --> appStart
    testDoubles --> contract
    requestModel --> contract
```

### 3.1.2 Proposed application flow (Mermaid)

```mermaid
flowchart TB
    %% App lifecycle and command flow for VoiceType2
    start["Application starts"] --> load["Load configuration and secrets"]
    load --> validate["Validate provider + network settings"]
    validate -->|invalid| failApp["Show startup failure"]
    validate -->|valid| hostReady["Initialize host services (tray, hotkeys, overlay)"]
    hostReady --> idle["Idle state (ready for input)"]

    idle -->|"Global hotkey pressed"| recording["Start recording with selected input device"]
    idle -->|"Remote command / external trigger"| remote["Remote command handler"]
    remote -->|"submit command"| submitNow["Submit without preview"]
    remote -->|"listen command"| recording
    remote -->|"close command"| shuttingDown["Shut down host"]
    submitNow --> injector["Paste/inject preview text"]

    recording --> meter["Update input level + mic UI"]
    meter -->|"Stop hotkey or timeout"| stopCapture["Stop capture + inspect metrics"]
    meter -->|"Error from recorder"| recordFail["Report recorder error"]

    stopCapture -->|no speech detected| noSpeech["Show no-speech overlay"]
    noSpeech --> idle

    stopCapture -->|"Has audio"| normalize["Normalize/prepare audio payload"]
    normalize --> transcribe["Call ITranscriptionProvider.TranscribeAsync()"]
    transcribe -->|provider error| transcriptFail["Show transcription error"]
    transcribe -->|success| sanitize["Sanitize transcript (prompt + directives)"]

    sanitize -->|"Empty text"| idle
    sanitize -->|"Command match"| commandMatch["Dispatch voice command"]
    commandMatch --> idle

    sanitize -->|"Free text"| preview["Show transcribed preview + countdown"]
    preview -->|"Countdown expires"| autoInsert["Insert and optional auto-send"]
    preview -->|"User taps preview"| cancel["Cancel paste"]
    preview -->|"submit during preview"| pasteWithoutEnter["Insert without auto-send"]

    autoInsert --> injector
    pasteWithoutEnter --> injector
    cancel -->|"No insertion performed"| idle
    injector -->|"Paste succeeded / fallback"| idle
    transcriptFail --> idle
    recordFail --> idle
    failApp --> shutdown1["Exit with error"]
    shuttingDown --> idle
```

### 3.1.3 Proposed API-First architecture (headless API + pluggable orchestrators)

```mermaid
flowchart TB
    %% API-first architecture: one service runs independently,
    %% multiple orchestrators can attach and control sessions.

    subgraph "VoiceType2 Runtime API Service"
        apiBoot["API service bootstraps config + DI + providers"]
        apiTransport["HTTP/WS transport + authentication"]
        apiSession["Session and lifecycle manager"]
        apiCapture["Capture pipeline (device + encoding)"]
        apiTranscribe["InternalApiTranscriptionProvider"]
        apiEvents["Result/event broadcaster (WS/SSE)"]
    end

    subgraph "Orchestrator A"
        orchAStart["VoiceType2 Orchestrator starts"]
        orchASetup["Discover API + register session"]
        orchAHotkeys["Register hotkeys and tray UX"]
        orchAPreview["Show preview + user decisions"]
        orchAInject["Inject text/copy fallback"]
    end

    subgraph "Orchestrator B"
        orchBStart["Alternative orchestrator starts"]
        orchBSetup["Discover API + register session"]
        orchBUI["Alternative UI flow"]
    end

    apiBoot --> apiTransport
    apiTransport --> apiSession
    apiSession --> apiCapture
    apiCapture --> apiTranscribe
    apiTranscribe --> apiEvents

    orchAStart --> orchASetup
    orchASetup -->|"POST /v1/sessions (register) + token"| apiSession
    orchASetup --> orchAHotkeys
    orchAHotkeys -->|"POST /v1/sessions/{id}/start"| apiSession
    orchAHotkeys -->|"POST /v1/sessions/{id}/stop"| apiSession
    orchASetup -->|"Event: connected, capabilities"| orchAPreview

    apiEvents -->|"event: transcript ready"| orchAPreview
    apiEvents -->|"event: transcribing / error"| orchAPreview
    orchAPreview -->|"User decision: submit / cancel / retry"| orchAInject
    orchAPreview -->|"User decision: command action"| orchASetup
    orchAInject -->|"POST /v1/sessions/{id}/resolve"| apiSession

    orchBStart --> orchBSetup
    orchBSetup -->|"POST /v1/sessions (register) + token"| apiSession
    orchBSetup --> orchBUI
    orchBUI -->|"POST /v1/sessions/{id}/start/stop"| apiSession
    apiEvents -->|"event: transcript ready"| orchBUI
```

### 3.2 Core interface contract

```csharp
public interface ITranscriptionProvider
{
    Task<TranscriptionResult> TranscribeAsync(
        Stream audioWav,
        string correlationId,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default);
}

public sealed record TranscriptionResult(
    string Text,
    string Provider,
    TimeSpan ProcessingLatency,
    bool IsSuccess,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    string? RawPayload = null);

public sealed record TranscriptionOptions(
    string? Language = null,
    string? Prompt = null,
    bool EnablePrompt = true,
    int? MaxTokens = null);
```

### 3.3 Internal API implementation concept

- `InternalApiTranscriptionProvider : ITranscriptionProvider`
- Sends:
  - `POST /v1/voice/transcriptions`
  - multipart form or JSON+base64 payload
  - metadata: `correlationId`, `language`, `prompt`, `format` (`wav`), `sampleRate`.
- Returns:
  - `text` and optional provider metadata.
- Security options:
  - Bearer token in `Authorization` header
  - mTLS/client cert if needed
  - short-lived token from local helper service.

Use retry policy (e.g. `HttpClient` + exponential backoff) and strict timeout around `TranscribeAsync`.

## 4) Config changes

Add to `AppConfig` in VoiceType2:

- `TranscriptionProvider` (`"InternalApi"` | `"Mock"` | `"Stub"`)
- `InternalApiBaseUrl`
- `InternalApiApiPath`
- `InternalApiAuthMode` (`"apikey"` / `"bearer"` / `"none"`)
- `InternalApiApiKeyOrToken` (stored using DPAPI semantics, same model as current `ApiKey`)
- `InternalApiTimeoutMs`

Behavior:

- If provider is `InternalApi`, instantiate `InternalApiTranscriptionProvider`.
- If provider is `Mock`/`Stub`, inject test implementations from configuration.
- Preserve existing prompt and audio normalization behavior.

## 5) Migration plan (practical 5-phase path)

1. **Scaffold VoiceType2**
   - Create `VoiceType2` solution folder with `VoiceType2.Core`, `VoiceType2.App`, `VoiceType2.Infrastructure`.
   - Keep shared helpers only if explicitly extracted to avoid coupling to VoiceType1.
2. **Introduce provider abstraction**
   - Implement `ITranscriptionProvider`.
   - Build a fresh transcription flow around the interface.
   - Refactor host logic to call abstraction only.
3. **Build internal API client**
   - Add `HttpClient` typed client, options, auth handler, request/response DTOs.
   - Add retry and timeout policy.
   - Add structured logs with correlation IDs.
4. **Configuration + runtime behavior**
   - Add config schema + migration defaults.
   - Add provider selection startup validation.
   - Fail fast for unsupported provider modes, except when running in explicit test/fallback stubs.
5. **Verification + cutover**
   - Add VoiceType2 unit/integration tests with provider/test doubles.
   - Add end-to-end smoke checks for:
     - hotkey start/stop,
     - silence/short clip handling,
     - preview cancel/submit,
     - injection path.
   - Validate against your internal API contract before broad rollout.

## 6) Risks and controls

- **Windows-only API surface**: keep `TextInjector` + overlay behavior in host app (same restrictions).
- **Audio format drift**: ensure `AudioRecorder` still emits exact WAV PCM mono 16-bit and preserves the current fallback/sample-rate behavior.
- **API contract brittleness**: version responses (`v1` contracts) and explicit error schema needed.
- **Security drift**: keep DPAPI for local secrets; avoid writing raw tokens in logs.
- **Latency**: preview duration is user-facing; expose provider latency in logs and tune timeout.

## 7) Expected outcome

By splitting VoiceType2 around `ITranscriptionProvider`, you get:

- zero rewrite of UI/hotkey/overlay/paste flow,
- a clean separation between host and transcription transport,
- and a system that is independent of VoiceType1 design debt.

The first deliverable is this design + minimal refactor pass to get abstraction points in place; productionization is then incremental.
