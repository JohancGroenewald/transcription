Workflow
--------
- After making any repository changes, always commit and push them.

Latest applied change
---------------------
- Remote action indicators now render in a dedicated overlay window (`_actionOverlay`) that is positioned above the live listening HUD, so they do not get repositioned by listening text updates and do not obstruct the listening text.
- Action overlay is short-lived (carry-over duration) and is hidden when recording stops.
- Main listening overlay updates now always render without embedded remote action text (`includeRemoteAction: false`) while listening remains on.
- Added `.gitattributes` for repo-level text normalization and explicit EOL preferences to reduce LF/CRLF commit warnings.
- `context.md` updated first in this iteration before code changes, then changes are ready for commit/push.
- Prefix editor now uses a dedicated multiline control with `AutoSize = false` and a fixed five-line minimum height.
- Prefix preview text is rendered in a faded color and omitted when target application already contains text.
- Prefix injection now inserts a newline separator between prefix and transcribed text.
- Requested: normalize repository line endings (`git add --renormalize .`) and verify remaining mixed EOL state.
- Requested: debug why pasted prefix line no longer appears in preview after the latest prefix detection changes.
- Renormalization pass now completes with no mixed `w/mixed` files reported by `git ls-files --eol`; mixed-state warnings are no longer present in the tracked index view.
- Fixed prefix visibility regression by changing detection to only skip prepends when a detected **text-input control** appears to already contain content (instead of treating any focused window with title text as occupied).
- User reported: pasted prefix appears twice in preview; plan is to keep prefix only in dedicated faded preview line and remove duplicate from main preview body.
- New request: when existing text is detected in focused field, show transcribed preview in a yellow tone (instead of green) to indicate prefix was skipped.
- Existing-text detection was updated to use foreground-thread focus information and child-control fallback instead of thread-local `GetFocus`.

Current request
---------------
- Use a multiline editor field for the pasted-text prefix setting instead of a single-line text box.
- Improve pasted-text prefix spacing so the prefix and dictated text are not merged unintentionally.
- Render remote action notice text in a distinct color in the existing popup overlay.
- When a remote action occurs during the listening overlay, draw the action notice stacked on top of the listening overlay so it is visible briefly and then disappears, not blocking listening text.
- Make the pasted text prefix editor five lines high.
- Keep the remote action overlay line visually stacked above the listening text (not right-aligned; not a side toast effect).
- Make the listener overlay stack the action line as a full-width top strip (left-aligned), then render the listening lines below it.
- Keep remote action action notices in a dedicated popup overlay stacked above the listening overlay and dismissing shortly after the carry-over period.
- Add repo-level line-ending normalization (for example `.gitattributes`) so LF/CRLF conversion warnings stop appearing during commits.
- Prefix handling now uses best-effort target-text detection before prefixing so existing target text can suppress the prefix.
- Prefix insertion now forces a newline separator between prefix and dictated text and supports faded-prefix preview rendering in the transcribed preview HUD.
- Prefix input is a multiline 5-line-capable editor with Enter accepted for new lines.
- Make the pasted-text prefix editor explicitly five editable lines high (`AutoSize = false`).
- Print a newline between inserted prefix and dictation text.
- Render the prefix preview in a faded color.
- Skip prefix insertion when active target already appears to have existing text.
- Verify line-ending normalization status after renormalize.
- Verify why the pasted-text prefix preview is not visible and adjust detection if needed.
- Fix prefix preview duplication by displaying raw dictated text in the body and prefix only in the separate preview line.
 - If existing text is detected in the target field, display pasted-text preview with a non-green color (yellow).
 - Current issue: prefix still applies when target text already exists, likely because focus detection for existing-text checks uses `GetFocus` (thread-local) rather than the active foreground thread focus.
 - Planned update: switch target-text detection to `GetGUIThreadInfo` on the foreground thread, and continue to suppress prefix when that focused control reports existing text.
 - Completed: `TextInjector` now resolves the focused control using `GetGUIThreadInfo` from the foreground thread, with a direct-child text-input fallback for reliable existing-text detection.
- Commit requested: finalize these changes by committing `TextInjector` detection updates and pushing for immediate local test.


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


