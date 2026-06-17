# Changelog

All notable changes to this project are documented in this file.

## [3.0.0] - 2026-06-17

### Added
- **Mouse-button debouncing**: a new low-level mouse hook (`WH_MOUSE_LL`) drops a
  button press that arrives within a threshold of that button's previous release,
  suppressing the phantom double-click produced by a chattering mouse switch. It
  covers the left, right, middle, and both side (X1/X2) buttons, mirroring the
  keyboard filter's `BlockRepress` behavior.
  - Off by default, so keyboard-only setups are unaffected. Enable it from the new
    **"Enable mouse click debounce"** tray menu item, which persists to `config.json`.
  - New config settings: `FilterMouseButtons` (default `false`),
    `MouseMinRepeatIntervalMs` (default `50.0`, kept below an intentional
    double-click so real double-clicks are preserved), and `ExcludedMouseButtons`
    (button names never filtered: `Left`, `Right`, `Middle`, `X1`, `X2`).
  - Filtered clicks are logged as `Mouse_<Button> filtered` when `LogLevel` is `Trace`.
- **Profiles**: keep multiple configurations side by side. Any `*.json` next to the app
  that matches the config signature appears under a new **Profile** tray submenu (the
  startup `config.json` is marked **(default)**); selecting one loads and applies it
  live, and later tray toggles save back into whichever profile is active.
- **Bundled `gaming.json` profile** tuned for movement: `BlockRelease` mode so a
  chattering key can't drop held W/A/S/D mid-run, a tight 12 ms per-key release on
  W/A/S/D and crouch (right Ctrl) for crisp stops, action keys left at the protective
  default, and pop-ups and mouse debouncing off.
- **Sticky "Run as administrator"**: new `RunAsAdmin` config and an **"Always run as
  administrator"** tray toggle. When on, the app relaunches elevated on every launch
  (a UAC prompt each time) so the hook can filter elevated windows. Replaces the
  one-shot "Restart as administrator" menu item.
- **Heatmap mouse graphic**: filtered mouse-button events are now parsed and drawn as
  a stylized mouse with the count on each button (left, right, middle, X1, X2) instead
  of being ignored.
- **Heatmap modifier row**: the keyboard graphic gains a bottom modifier row (Ctrl,
  Win, Alt, Space, Alt, Fn, Menu, Ctrl) plus both Shift keys on row 3, matched by
  side-specific VK code and tinted on one shared intensity scale with the letters.
- **Heatmap day window**: new `HeatmapDays` config (`all`, or a number of days). When
  set, the report charts only entries from that many days back from when it is run.
- **`VK_PHANTOM` name** for virtual-key `0xFF` (255), the phantom/unmappable code some
  keyboards emit, so it shows by name in the log and heatmap instead of a bare `=255`.

### Changed
- Renamed the tray **Keyboard Heatmap** submenu to **Heatmap** (it now shows mouse data
  too).
- Now documented as supporting **Windows 10/11 x64** (previously stated Windows 11 only);
  the APIs used are all available on Windows 10, so no code change was required.

### Fixed
- **Spurious Sticky Keys activation.** In `BlockRepress` mode a swallowed bounce
  key-down left the matching key-up unbalanced, which could arm Windows Sticky Keys
  (notably for Shift, and especially over high-latency RDP, where input arrives in
  bursts). The paired key-up is now swallowed too so the event stream stays balanced;
  `BlockRelease` likewise no longer re-injects an unmatched key-up.
- **Heatmap legend in dark mode** now repaints to match the dark key colors; it
  previously showed the light "white-to-crimson" ramp while the keys used the dark ramp.
- **Dismissable elevated-window toast.** The corner notice now has a close (x) button that
  clears it without relaunching elevated. Previously clicking the toast only triggered the
  "run as administrator" action, so there was no way to dismiss it short of waiting out the
  timer.

## [2.1.0] - 2026-06-14

### Added
- **Restart as administrator**: a new tray menu item (shown only while running
  unelevated) relaunches the app with administrator rights via a UAC prompt, so the
  keyboard hook can also filter input for elevated windows instead of being bypassed
  for them (UIPI). The elevated-window notification is now clickable and triggers the
  same relaunch.

### Changed
- The elevated-window notification stays up longer and pauses its auto-dismiss
  countdown while the pointer is over it, so it can be read and clicked.
- The single-instance guard now waits briefly for the mutex instead of exiting
  immediately, so the "Restart as administrator" handoff (old instance releasing as
  the elevated one starts) no longer trips the singleton and kills the new instance.

## [2.0.0] - 2026-06-13

### Added
- **Elevated-window detection**: when a window running as administrator is focused,
  the filter's low-level hook is bypassed for it by Windows (UIPI). The app now
  detects this and reflects it non-disruptively: the tray icon turns yellow with a
  plain-language tooltip, a brief self-drawn corner notification appears each time
  focus moves to an elevated window (it does not steal focus and does not depend on
  Windows notification settings), and the transition is recorded in the log
  (`HookBypass` / `HookActive`). It returns to normal automatically when a
  non-elevated window is focused. The popup can be turned off with the
  `ShowElevatedWindowNotice` config setting (default on) or the new
  **"Disable nag popups"** tray menu item, which persists the choice to
  `config.json`; the icon and log are unaffected.
- **KeyboardHeatmap surfaces config warnings**: `ConfigWarning` log lines (emitted when
  `config.json` contains an unrecognized key name) are now parsed and shown as a banner in
  the HTML report and a line in the console output, so typos in key names are noticed instead
  of silently ignored.
- **Friendly key names in `config.json`**: `ExcludedKeys` and `PerKeyMinRepeatIntervalMs`
  now accept key names (e.g. `"Back"`, `"Return"`, `"I"`, `"LCONTROL"`) as shown in the log,
  with the `VK_` prefix optional and case-insensitive matching. A generic modifier
  (`"Ctrl"`, `"Shift"`, `"Alt"`) expands to both the left and right keys; use
  `"LCONTROL"`/`"RCONTROL"` etc. to target one side. Numeric VK codes still work, and the
  legacy `ExcludedVkCodes` array is still honored. Unrecognized names are ignored and
  reported as a `ConfigWarning` in the log.
- **`FilterMode` config switch**: choose how a stutter is filtered:
  - `BlockRepress` (default) blocks the duplicate key-down, preserving the
    original behavior. Best for character keys.
  - `BlockRelease` withholds the spurious key-up so a held key stays logically
    pressed through a bounce. Best for held modifiers (`Ctrl`/`Shift`) and game
    movement keys, at the cost of deferring each release by up to the threshold.
  - Switchable at runtime from the **Filter mode** tray submenu (radio choice),
    which persists to `config.json` and applies immediately.

## [1.0.0] - 2026-04-14
- Initial release.

## [1.1.0] - 2026-04-15

### Changed
- Application icon handling was updated so the icon is embedded directly in the executable.

### Notes
- Version `1.1.0` does **not** require a separate `.ico` file next to the `.exe`.

## [1.2.0] - 2026-04-16

### Changed
- Add a Singleton pattern to prevent the application from starting multiple instances.

## [1.3.0] - 2026-05-15

### Added
- **KeyboardHeatmap**: new companion CLI tool that parses `KeyboardRepeatFilter.log` and generates a self-contained HTML heatmap report.
  - Displays filtered key counts across all QWERTY letter keys using a purple colour-ramp (5-stop light/dark theme).
  - Parses `Startup`, `Shutdown`, and `Filtered` log entries via `LogParser`.
  - Supports an optional `-v` / `--v` flag to include the **Daily filtered event count** section in the output.
  - Reads `config.json` (`LogFilePath`) from the current directory to resolve default log and output paths automatically.
  - Outputs a single self-contained `.html` file (`KeyboardHeatmap.html`), no external dependencies.
  - `KeyboardHeatmap.exe` is now copied to the `releases/` folder automatically on Release builds alongside `KeyboardRepeatFilter.exe`.

