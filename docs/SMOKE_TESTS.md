# Smoke Test Checklist

Use this quick checklist before publishing a release.

## Build and artifacts

- Build succeeds in `Release` mode.
- `releases` folder is refreshed automatically.
- `releases` contains:
  - `KeyboardRepeatFilter.exe`
  - `Newtonsoft.Json.dll`
  - `config.json`
- `releases` does not contain stale files from previous builds.

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
- Per-key override (`PerKeyMinRepeatIntervalMs`) changes behavior for that key.
- Excluded key (`ExcludedVkCodes`) is not filtered.

## Exit and lifecycle logs

- Exit app using tray menu.
- Shutdown log entry is written with reason and uptime.
- Restart app and verify new startup entry is appended.

## Startup registration

- Toggle `Autostart` from tray menu.
- Sign out/in (or reboot) and confirm app starts automatically when enabled.
