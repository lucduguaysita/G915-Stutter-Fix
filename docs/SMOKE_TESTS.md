# Smoke Test Checklist

Use this quick checklist before publishing a release.

## Build and artifacts

- Build succeeds in `Release` mode.
- `releases` folder is refreshed automatically. The build wipes and repopulates it, so files copied
  from `src` keep their original timestamps: an old timestamp there is not staleness.
- `releases` contains exactly:
  - `KeyboardRepeatFilter.exe`
  - `KeyboardHeatmap.exe`
  - `GameListUpdater.exe`
  - `Newtonsoft.Json.dll`
  - `config.json`, `gaming.json`, `WoW.json`
- `releases` contains nothing else.

## App startup and tray

- Launch `KeyboardRepeatFilter.exe` from `releases`.
- Tray icon appears within a few seconds.
- Startup log entry is written to the configured log file.

## Filtering behavior

- Normal typing works in Notepad or another text editor.
- Known problematic key no longer stutters or double-types.
- Rapid but intentional typing remains usable.

## Configuration behavior

- Edit `releases/config.json` and restart app.
- Updated `MinRepeatIntervalMs` is reflected in startup log.
- A key listed in `ExcludedKeys` by name (for example `"Back"`) is not filtered.
- An unrecognized key name produces a `ConfigWarning` line in the log.

## Tray menu and modes

- **Filter mode** submenu shows a checkmark next to the active mode.
- Switching to **Protect held keys** takes effect immediately and writes
  `"FilterMode": "BlockRelease"` to `config.json`.
- With **Protect held keys** active, a held `Ctrl`/`Shift` shortcut survives a bounce.
- **Disable nag popups** toggles `ShowElevatedWindowNotice` in `config.json`.
- **Autostart** toggle reflects and updates the startup registration.

## Elevated-window detection

- Focus a window running as administrator (for example an elevated terminal).
- Tray icon turns yellow and the tooltip explains the paused state.
- A brief popup appears and does **not** steal keyboard focus (typing stays in the elevated window).
- A `HookBypass` line is logged; switching back to a normal window logs `HookActive` and restores
  the icon.
- With **Disable nag popups** checked, no popup appears (icon and log still update).

## KeyboardHeatmap

- Run `KeyboardHeatmap.exe` from `releases` with a valid log file (or use **Tray → Heatmap →
  Generate report**, which always includes the daily section).
- `KeyboardHeatmap.html` is generated in the expected output location and opens in the browser.
- The report renders with the ember color ramp (pale amber → crimson in light mode, dim ember →
  bright gold in dark mode).
- Run `KeyboardHeatmap.exe -v` and confirm the daily event count section appears with
  green→yellow→crimson bars. Without `-v` that section must be absent.
- **Classic layout only:** run with `-classic` and confirm the busiest QWERTY row is flagged with ⚠
  (`warning-row` / `warning-badge` in the markup). The default photo layout draws counts on the
  keyboard photo and has no row badges, so this check does not apply to it.
- If the log contains a `ConfigWarning`, the report shows the warning banner naming the offending
  key; with a clean log there is no banner.
- With the log file missing:
  - from the command line, it exits non-zero with `Error: log file not found` plus usage, and writes
    no HTML;
  - from the tray, a message box explains the log is missing and points at `"LogLevel": "Trace"` and
    `LogFilePath`, rather than failing silently.
