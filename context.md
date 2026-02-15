Workflow
--------
- After making any repository changes, always commit and push them.

Current request
---------------
- Use a multiline editor field for the pasted-text prefix setting instead of a single-line text box.
- Improve pasted-text prefix spacing so the prefix and dictated text are not merged unintentionally.
- Render remote action notice text in a distinct color in the existing popup overlay.

VoiceType CLI actions (VoiceType.exe)
====================================

This file tracks available command-line actions and how they interact.

Usage
-----
`VoiceType.exe [options]`

General options
--------------
- `--help`, `-h`  
  Show help text and exit.
- `--version`, `-v`  
  Show app version and exit.
- `--test`  
  Run a microphone + configuration dry-run test in console mode.

App launch / remote control actions
----------------------------------
The following actions are treated as the primary launch request and are mutually exclusive:
- `--close`  
  Request graceful shutdown of a running instance.
- `--listen`  
  Trigger dictation on existing instance, or start and start dictation when not already running.
- `--ignore-prefix`  
  Use with `--listen` to skip the configured pasted-text prefix for that listen request.
- `--submit`  
  Send Enter to the active app, or during an active transcribed preview: paste without auto-send.
- `--replace-existing`  
  Close a running instance (if any) and start this process as the active instance.

If no launch action is supplied, launching without options starts VoiceType with default behavior (start app or trigger dictation on existing instance).

Utilities (single-action only)
------------------------------
These actions are utility operations and cannot be combined with each other:
- `--pin-to-taskbar`  
  Best-effort pin the executable to the Windows taskbar.
- `--unpin-from-taskbar`  
  Best-effort unpin from the Windows taskbar.
- `--create-activate-shortcut`  
  Create `VoiceTypeActivate.exe.lnk` configured with `--listen`.
- `--create-submit-shortcut`  
  Create `VoiceTypeSubmit.exe.lnk` configured with `--submit`.
- `--create-listen-ignore-prefix-shortcut`  
  Create `VoiceTypeListenNoPrefix.exe.lnk` configured with `--listen --ignore-prefix`.

Constraint summary
------------------
- `--help` can be combined with `--version`.
- For launch actions: `--close`, `--listen`, `--submit`, `--replace-existing` are mutually exclusive.
- `--ignore-prefix` can only be used with `--listen`.
- For utility actions: one of `--pin-to-taskbar`, `--unpin-from-taskbar`, `--create-activate-shortcut`, `--create-submit-shortcut`, `--create-listen-ignore-prefix-shortcut` may be used at a time.

Remote action popup level
------------------------
Running instance behavior:
- `RemoteActionPopupLevel: 0` (Off): do not show a popup for remote actions.
- `RemoteActionPopupLevel: 1` (Basic): show a short popup for remote `--listen`, `--submit`, and `--close`.
- `RemoteActionPopupLevel: 2` (Detailed): same as Basic, plus action-detail text when available (e.g., `listen --ignore-prefix`).
