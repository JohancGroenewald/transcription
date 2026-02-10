# Transcription

**OpenAI’s speech-to-text (STT) and text-to-speech (TTS) models are available via APIs**, and you can absolutely use them as the “engine” behind a **Windows keyboard / dictation / speech-to-text translator** style app. The main design choice is **batch (“record then send”) vs realtime streaming**.

## What OpenAI gives you (today) for STT + TTS

### Speech-to-text (dictation / transcription)

You can transcribe audio with the **Audio API** (`POST /v1/audio/transcriptions`) using models like **`gpt-4o-transcribe`**, **`gpt-4o-mini-transcribe`**, or **`whisper-1`**. ([platform.openai.com][1])

There’s also a **Realtime API transcription mode** that streams partial text as the user speaks (more “live captions / dictation”). ([platform.openai.com][2])

### Text-to-speech (voice output)

You can synthesize speech with `POST /v1/audio/speech` using models like **`gpt-4o-mini-tts`**, **`tts-1`**, and **`tts-1-hd`** with built-in voices. ([platform.openai.com][1])

## How this maps to a Windows “keyboard app”

A Windows “keyboard app” usually means one of these patterns:

1. **IME-style integration (TSF)**: You implement an input method (Text Services Framework) and feed recognized text into the composition buffer.
2. **Hotkey + paste** (much easier): User holds a hotkey, speaks, you transcribe, then inject text via clipboard/paste or `SendInput`.

Either way, OpenAI is just the cloud recognizer / voice generator. Your app handles:

* microphone capture (WASAPI / Media Foundation / etc.)
* sending audio to the API
* receiving text
* inserting text into the focused app

## Batch vs realtime (the big decision)

### Option A — “Record then send” (simplest prototype)

* Record 3–30 seconds
* Send the file to `/v1/audio/transcriptions`
* Insert returned text

This is straightforward and works great for push-to-talk dictation. ([platform.openai.com][3])

### Option B — Realtime streaming dictation (best UX, more moving parts)

* Keep a WebSocket/WebRTC connection open
* Stream PCM audio frames
* Receive incremental transcript “deltas” and update the live composition text

OpenAI documents this as **Realtime transcription sessions**, including the streaming events you listen for. ([platform.openai.com][4])

## “Speech-to-text translator” specifically (speech → text in another language)

There are two practical approaches:

1. **Speech → text (original language) → translate (text model) → insert translated text**
   This gives you translation into *any* target language, and you can preserve punctuation/style.

2. **Speech → English translation via `/v1/audio/translations`**
   OpenAI’s audio translation endpoint is currently **Whisper-only** and translates **into English**. ([platform.openai.com][3])

If you want *live* interpreter vibes, OpenAI also has a Realtime cookbook example for one-way speech translation workflows. ([developers.openai.com][5])

## Shipping this to users safely: don’t leak your API key

If this is a distributable Windows app, **you should not embed a long-lived OpenAI API key in the client**.

For realtime client connections, OpenAI supports **ephemeral client secrets**: your server requests a short-lived token and hands it to the client for connecting to Realtime securely. ([platform.openai.com][6])

(For non-realtime/batch, the usual pattern is: client uploads audio to *your* backend → backend calls OpenAI.)

## Privacy / data handling realities (important for microphone apps)

OpenAI provides **data retention controls** for qualifying orgs (including **Zero Data Retention** options in some cases). ([platform.openai.com][7])
OpenAI’s enterprise privacy materials describe default retention and monitoring posture (commonly referenced as up to 30 days for API abuse monitoring, with exceptions and controls). ([OpenAI][8])

## Windows dev notes: SDKs

If you’re building in .NET, there is an **official OpenAI .NET library** you can use (or call raw HTTPS endpoints directly). ([GitHub][9])

## Practical recommendation

* **Prototype** with batch transcription (`/v1/audio/transcriptions`) + hotkey/paste.
* If the UX needs to feel like real dictation, switch to **Realtime transcription**.
* For “translator keyboard”, do **STT → translate → insert** (don’t rely on audio translation unless English output is fine). ([platform.openai.com][3])

If you want to sanity-check feasibility for your exact concept: the only “make-or-break” detail is whether you mean a true **TSF IME** or a **hotkey overlay**—both work, but the engineering effort is wildly different.

[1]: https://platform.openai.com/docs/guides/audio?utm_source=chatgpt.com "Audio and speech | OpenAI API"
[2]: https://platform.openai.com/docs/guides/realtime?utm_source=chatgpt.com "Realtime API | OpenAI API"
[3]: https://platform.openai.com/docs/guides/speech-to-text?utm_source=chatgpt.com "Speech to text - OpenAI API"
[4]: https://platform.openai.com/docs/guides/realtime-transcription?utm_source=chatgpt.com "Realtime transcription | OpenAI API"
[5]: https://developers.openai.com/cookbook/examples/voice_solutions/one_way_translation_using_realtime_api?utm_source=chatgpt.com "Multi-Language One-Way Translation with the Realtime API"
[6]: https://platform.openai.com/docs/guides/realtime-webrtc?utm_source=chatgpt.com "Realtime API with WebRTC | OpenAI API"
[7]: https://platform.openai.com/docs/guides/your-data?utm_source=chatgpt.com "Data controls in the OpenAI platform"
[8]: https://openai.com/enterprise-privacy/?utm_source=chatgpt.com "Enterprise privacy at OpenAI"
[9]: https://github.com/openai/openai-dotnet?utm_source=chatgpt.com "openai/openai-dotnet: The official .NET library for the OpenAI API - GitHub"
