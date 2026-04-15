# Usage and Configuration

## What this app does

`G915-Stutter-Fix` runs in user mode and filters invalid keyboard repeat events (for example, impossible rapid duplicate keypresses) before they reach other applications.

It is designed for Logitech G915/G915X stutter behavior but can help with other keyboards showing the same pattern.

## Prerequisites

- Windows 11 x64
- .NET Framework runtime 4.8 required by the app

## Release folder contents

Your `releases` folder should contain:

- `KeyboardRepeatFilter.exe`
- `Newtonsoft.Json.dll`
- `config.json`

## First run

1. Open the `releases` folder.
2. Copy the executable, `config.json` and `Newtonsoft.Json.dll` to a directory of your choice (for example, `C:\Utils\KeyboardRepeatFilter`).
3. Double-click `KeyboardRepeatFilter.exe` to launch the app.
4. Confirm the app appears in the system tray.
5. Type normally and verify stutter/double-press behavior is gone.
6. Open the log file at `LogFilePath` (for example, `C:\Temp\KeyboardRepeatFilter.log`) to see which keys were filtered.

## Run at startup

The app can register itself to start automatically with Windows for your user account.

If startup is enabled, it will launch silently in the background after sign-in.
from the system tray icon context menu you can select "AutoStart" to add it to startup

## Configuration file

The app reads configuration from `config.json` next to the EXE.

Default example:

```json
{
  "LogLevel": "Trace",
  "LogFilePath": "C:/Temp/KeyboardRepeatFilter.log",
  "MinRepeatIntervalMs": 28.0,
  "ExcludedVkCodes": [8, 13, 174, 175],
  "PerKeyMinRepeatIntervalMs": {
    "73": 40.0
  }
}
```

### Config fields

- `LogLevel`
  - Controls logging verbosity.
  - Typical values: `None`, `Trace`.

- `LogFilePath`
  - Full path to the log file.
  - Example: `C:/Temp/KeyboardRepeatFilter.log`.

- `MinRepeatIntervalMs`
  - Global minimum time (milliseconds) allowed between repeated key events.
  - Events below this threshold are treated as invalid repeats and discarded.
  - Recommended starting value: `28.0`.

- `ExcludedVkCodes`
  - Virtual key codes excluded from repeat filtering.
  - Use this for keys that should not be aggressively filtered (for example media keys including the Volume wheel)
  - Check online documentation for VK codes or look into the VirtualKeys.resx file (for example, `8` for Backspace, `13` for Enter, `174` for Volume Down, `175` for Volume Up).


- `PerKeyMinRepeatIntervalMs`
  - Per-key override map: `"<VK code>": <milliseconds>`.
  - Use this when one key needs a stricter threshold than the global value.
  - Example: `"73": 40.0` sets key `73` to `40ms`.

## Tuning guide

If you still see stutter:

- Increase `MinRepeatIntervalMs` slightly (for example from `28` to `30`).

If legitimate very-fast repeats are being filtered:

- Decrease `MinRepeatIntervalMs` slightly (for example from `28` to `26`).

If one key is problematic:

- Add a `PerKeyMinRepeatIntervalMs` override for that key only.

Adjust gradually in small steps and test after each change.

## Logs and troubleshooting

- Check the log at `LogFilePath` for filtered events and key codes.
- A common default location is `C:\Temp\KeyboardRepeatFilter.log`.
- The log file can be deleted safely even if the app is running.

## Updating to a new version

1. Close the running app from the system tray.
2. Replace files in `releases` with the new build output.
3. Keep your tuned `config.json` if you already customized it.
4. Relaunch `KeyboardRepeatFilter.exe`.
