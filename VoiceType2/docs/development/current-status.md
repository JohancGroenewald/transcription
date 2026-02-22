# Current development status

Latest status: `2026-02-22`

- Alpha 1 API and CLI orchestration build is implemented and runnable via existing scripts.
- Runtime config validation and session event model are in place.
- API + CLI integration smoke tests exist and are referenced from:
  - `VoiceType2/alpha-build-1/scripts/test-alpha1.ps1`

Open questions to make explicit before release:

- API production provider strategy (mock vs real transcription provider).
- Security and auth policy defaults for public-facing deployments.
- Local tray/desktop orchestration integration path.
