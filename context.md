Workflow
--------
- After making any repository changes, always commit and push them.
Latest applied change
---------------------
- ProseMirror placeholder detection now explicitly treats the VS Code "Ask for follow-up changes" prompt as empty (this was the `textLen=26` phantom content in UIA `TextPattern` when the box looked empty).
- ProseMirror placeholder detection was broadened to cover VS Code chat-style placeholders that include keybinding hints (for example `Ask Copilot (Ctrl+...)`) even when UIA does not expose a matching `HelpText`/`Name`/`ItemStatus`, so empty inputs no longer show as "has existing text".
- Debug log now rolls on app startup: existing `%LOCALAPPDATA%\\VoiceType\\voicetype.log` is renamed to a timestamped archive and a fresh log begins for the new run (with simple retention to avoid unbounded growth).
- ProseMirror placeholder text is now treated as empty for existing-text detection (UIA `TextPattern` can expose placeholder as text); this allows empty VS Code chat inputs to correctly show the "empty" state (green) while real typed content still shows as "has text" (yellow).
- Audio recorder stop no longer blocks for seconds waiting for `RecordingStopped`; we now do a short best-effort wait (100ms) and continue without logging a timeout, reducing noisy stop-timeout log spam during debugging.
- UIA existing-text detection now recognizes ProseMirror contenteditable editors exposed as `ControlType.Group`/`ControlType.Document`/`ControlType.Pane` with `TextPattern` when keyboard-focused, enabling existing-text detection in VS Code chat input.
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
- Follow-up: still seeing false positives on empty fields; tighten existing-text detection to trust only high-confidence input controls for suppressing prefix.
- User now reports a false-positive: target field appears empty but is detected as containing text.
- Interim fix: only treat a control as "non-empty" when queried text contains non-whitespace characters, not just a nonzero reported length.
- Fix now implemented in `TextInjector`: read actual window text via `GetWindowText` and suppress prefix only when non-whitespace text is present.
- Additional hardening: require non-whitespace, non-control, non-invisible characters before considering a field non-empty, to avoid phantom text flags in empty controls.
Current request
---------------
- New request (2026-02-15): re-enable pasted-text prefix injection, but add a Settings checkbox so the prefix feature can be enabled/disabled without deleting the prefix text.
- New request (2026-02-15): add a dark mode toggle for the Settings screen and persist it in config.
- New request (2026-02-15): make ComboBox dropdown lists (the opened list items) dark-mode friendly as well, not just the closed control.
- New request (2026-02-15): fix pasted-text prefix enable/disable checkbox not being honored at runtime.
- New request (2026-02-15): fix the Settings screen GroupBox frame overlap artifact between "Voice Commands" and the group below it (border line segments appear to overlap/linger).
- New request (2026-02-15): fix the "Surface Pen key" label in Settings not being readable in dark mode when the pen hotkey feature is disabled (WinForms disabled label rendering ignores our theme).
- Implemented (2026-02-15): updated Settings so labels are never disabled (WinForms draws disabled labels with non-theme-aware gray). Instead, we keep labels enabled and switch to the theme muted color when their associated setting is disabled (fixes the "Surface Pen key" label in dark mode).
- Implemented (2026-02-15): ComboBox dropdown items are now drawn with the active theme and we apply best-effort Windows dark-mode theming (`DarkMode_Explorer`) to the native dropdown list window on open, improving dark-mode consistency inside dropdown lists.
- Implemented (2026-02-15): added detailed logging around pasted-text prefix decisions (disabled via settings vs ignored via `--ignore-prefix` vs suppressed due to existing target text) to debug cases where the prefix enable/disable toggle appears not to apply.
- Implemented (2026-02-15): replaced WinForms `GroupBox` controls in Settings with a custom double-buffered, background-painting `ThemedGroupBox` (UserPaint) to eliminate border-line artifacts/overlaps in dark mode.
- Implemented (2026-02-15): re-enabled pasted-text prefix injection and added a Settings checkbox (`Enable pasted text prefix`) that controls whether the prefix is applied (prefix text is still stored even when disabled).
- Implemented (2026-02-15): added a Settings dark mode toggle (`Dark mode (settings window)`) that re-themes the Settings form immediately and persists via `EnableSettingsDarkMode` in config.
- Implemented (2026-02-15): explicitly ignore the VS Code Copilot Chat empty-prompt text "Ask for follow-up changes" returned by UIA `TextPattern` so empty inputs no longer get misdetected as containing text.
- Implemented (2026-02-15): broaden ProseMirror placeholder detection beyond strict equality against UIA `HelpText`/`Name`/`ItemStatus` by normalizing invisible characters/whitespace and handling common placeholder+keybinding formats, so empty VS Code chat inputs stop being misdetected as non-empty (`textLen=26` placeholder case).
- Implemented (2026-02-15): roll file logging on VoiceType startup so each app run gets its own `voicetype.log` and previous logs are preserved as timestamped archives.
- Implemented (2026-02-15): ProseMirror empty inputs were being misdetected as non-empty because UIA `TextPattern` returns placeholder text (for example ~26 chars) even when the editor has no user-typed content; we now detect and strip placeholder by comparing `TextPattern` text against UIA `HelpText`/`Name`/`ItemStatus` for ProseMirror, so empty inputs return `TargetHasExistingText=false`.
- Implemented (2026-02-15): `TargetHasExistingText()` now treats keyboard-focused ProseMirror contenteditable UIA elements (often `ControlType.Group`) as editable for `TextPattern`, so VS Code/Chromium chat input can be detected as empty vs non-empty.
- New request (2026-02-15): clarify git hook behavior. Solution: repo sets `core.hooksPath=.githooks` so commits run `.githooks/pre-commit` (bumps `<Version>` in `VoiceType/VoiceType.csproj` via `bump-version.ps1`, skippable with `SKIP_VERSION_BUMP=1`) and `.githooks/post-commit` (runs `restart-voicetype.ps1` to close any repo-running VoiceType.exe, build Debug, and relaunch).
- New request (2026-02-15): add detailed debug logging for `TextInjector.TargetHasExistingText()` so we can see which target window/control is being evaluated (HWND/class/process), whether Win32 text APIs or UI Automation are used, and what text-length/meaningful checks are returning (without logging actual field contents).
- New request (2026-02-15): disable pasted-text prefix (pre-text paste) injection temporarily to simplify testing. Keep existing-text detection and render the transcribed preview color as: yellow when the focused target already contains text, green when it does not.
- New request (2026-02-15): implement a "pretext detector" for transcriptions: strip any `<flow>...</flow>` directive blocks from the transcribed text before voice-command parsing, preview display, and injection. Success criteria: `<flow>` content never appears in the preview/injected text.
- New request (2026-02-15): `VoiceType/TextInjector.cs` is not reliably detecting whether the destination input already contains text (common in non-native controls like Chromium/Electron text fields). Solution: keep the current Win32 `HWND`-based detection for strong text input classes (Edit/RichEdit/etc), but add a UI Automation fallback that inspects the **focused** UIA element in the foreground app (ValuePattern/TextPattern) and only returns "has existing text" when non-whitespace, non-control characters are present (skip password fields and ignore weak/unrelated focused elements). Implementation details: enable `<UseWPF>true</UseWPF>` in `VoiceType/VoiceType.csproj` to reference UIA assemblies, and add `VoiceType/GlobalUsings.cs` for `System.IO` which is no longer implicitly included under the WindowsDesktop implicit-usings profile.
- This is a pretext prefill test run (`pretext pretext tests`): validate whether prefix is now only added when destination truly has existing text.
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
- New request: fix false positives where empty fields are reported as non-empty by tightening `TextInjector` text-content check to verify actual non-whitespace text.
- Completed: `TextInjector` now uses `GetWindowText` content checks and only considers non-whitespace text as existing content.
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
