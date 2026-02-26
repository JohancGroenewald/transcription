# VoiceType2 Alpha1 Voice Capture To-Do

## Current goal

Add real voice capture with explicit device selection flow across CLI and API runtime.

## Done in this pass

- [x] Add runtime `AudioDeviceSelection` contract fields to session registration.
- [x] Wire device selection into CLI register payload (`/v1/sessions`).
- [x] Add CLI flags `--recording-device-id` and `--playback-device-id`.
- [x] Add device discovery for Windows recording and playback devices.
- [x] Add run/menu and TUI interactions to select devices before/while session runs.
- [x] Add list output for discovered devices in both text and TUI flows.
- [x] Add API `/v1/sessions/{sessionId}/devices` endpoint for active-session device updates.
- [x] Sync CLI menu device updates to API session state.

## Next (execution plan)

- [x] Add fallback/non-Windows discovery providers (Linux/macOS basic attempt).
- [x] Persist selected devices in `ClientConfig` defaults (`DefaultRecordingDeviceId` / `DefaultPlaybackDeviceId`).
- [x] Expose discovered devices in API `/v1/devices`.
- [x] Add API validation for invalid device IDs using host-discovered IDs (when discoverable).
- [x] Integrate selected `playback` and `recording` device values into transcription provider call surface.
- [ ] Add full device command acceptance tests for run-mode and TUI mode.
- [ ] Add docs for hardware setup and examples for device selection in CLI reference and launch scripts.
- [x] Execute hardware-device discovery + menu sync in a single source path: have CLI fetch `/v1/devices` for both run and tui menus, and fallback to local detection when API is unavailable.
- [ ] Add `ITranscriptionProvider` host capture wiring so selected devices flow into live capture/recording initialization.
- [ ] Add playback device plumbing (e.g., optional TTS validation/replay for confirmation) after capture path exists.
- [ ] Add integration test that list-device output reflects `/v1/devices` payload shape.
- [ ] Add user-facing checklist for required permissions (mic/camera/speaker) per OS.
