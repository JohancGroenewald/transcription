# Alpha Build 2

Alpha 2 is a fresh VoiceType2 runtime with a simpler lifecycle than Alpha 1:

- register session
- start recording
- stop recording
- transcribe captured audio
- complete or cancel session

Default runtime behavior is developer-safe:

- audio source: `fake`
- transcription provider: `mock`

That keeps local builds and tests runnable without microphone hardware or an API key.

## Real microphone and OpenAI transcription

To use the Windows microphone and the OpenAI transcription provider, update
`RuntimeConfig.sample.json` or pass a separate config file with:

- `AudioCapture.Source`: `wave-in`
- `Transcription.Provider`: `openai`
- `OPENAI_API_KEY` set in the environment

## API host

```powershell
dotnet run --project VoiceType2\alpha-build-2\src\VoiceType2.Alpha2.ApiHost\VoiceType2.Alpha2.ApiHost.csproj -- --config VoiceType2\alpha-build-2\RuntimeConfig.sample.json
```

## CLI

```powershell
dotnet run --project VoiceType2\alpha-build-2\src\VoiceType2.Alpha2.App.Cli\VoiceType2.Alpha2.App.Cli.csproj -- run --api-url http://127.0.0.1:5250 --mode managed --api-config VoiceType2\alpha-build-2\RuntimeConfig.sample.json
```

The CLI starts one session, begins recording, waits for Enter, then stops and prints the final session JSON.
