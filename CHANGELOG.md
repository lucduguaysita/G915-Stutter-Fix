one # Changelog

All notable changes to this project are documented in this file.

## [2.0.0] - 2026-06-13

### Added
- **Elevated-window detection** — when a window running as administrator is focused,
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
- **KeyboardHeatmap surfaces config warnings** — `ConfigWarning` log lines (emitted when
  `config.json` contains an unrecognized key name) are now parsed and shown as a banner in
  the HTML report and a line in the console output, so typos in key names are noticed instead
  of silently ignored.
- **Friendly key names in `config.json`** — `ExcludedKeys` and `PerKeyMinRepeatIntervalMs`
  now accept key names (e.g. `"Back"`, `"Return"`, `"I"`, `"LCONTROL"`) as shown in the log,
  with the `VK_` prefix optional and case-insensitive matching. A generic modifier
  (`"Ctrl"`, `"Shift"`, `"Alt"`) expands to both the left and right keys; use
  `"LCONTROL"`/`"RCONTROL"` etc. to target one side. Numeric VK codes still work, and the
  legacy `ExcludedVkCodes` array is still honored. Unrecognized names are ignored and
  reported as a `ConfigWarning` in the log.
- **`FilterMode` config switch** — choose how a stutter is filtered:
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
- **KeyboardHeatmap** — new companion CLI tool that parses `KeyboardRepeatFilter.log` and generates a self-contained HTML heatmap report.
  - Displays filtered key counts across all QWERTY letter keys using a purple colour-ramp (5-stop light/dark theme).
  - Parses `Startup`, `Shutdown`, and `Filtered` log entries via `LogParser`.
  - Supports an optional `-v` / `--v` flag to include the **Daily filtered event count** section in the output.
  - Reads `config.json` (`LogFilePath`) from the current directory to resolve default log and output paths automatically.
  - Outputs a single self-contained `.html` file (`KeyboardHeatmap.html`) — no external dependencies.
  - `KeyboardHeatmap.exe` is now copied to the `releases/` folder automatically on Release builds alongside `KeyboardRepeatFilter.exe`.

