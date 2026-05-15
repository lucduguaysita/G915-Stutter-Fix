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
- `KeyboardHeatmap.exe`
- `Newtonsoft.Json.dll`
- `config.json`

## First run

1. Open the `releases` folder.
2. Copy the executables, `config.json` and `Newtonsoft.Json.dll` to a directory of your choice (for example, `C:\Utils\KeyboardRepeatFilter`).
3. Double-click `KeyboardRepeatFilter.exe` to launch the app.
4. Confirm the app appears in the system tray.
5. Type normally and verify stutter/double-press behavior is gone.
6. Open the log file at `LogFilePath` (for example, `C:\Temp\KeyboardRepeatFilter.log`) to see which keys were filtered.

## Run at startup

The app can register itself to start automatically with Windows for your user account.

If startup is enabled, it will launch silently in the background after sign-in.
From the system tray icon context menu you can select "AutoStart" to add it to startup.

## KeyboardHeatmap

`KeyboardHeatmap.exe` is a companion CLI tool that parses the filter log and produces a self-contained HTML heatmap of filtered key counts.

| Argument | Default | Description |
|---|---|---|
| `logFile` | `KeyboardRepeatFilter.log` in current dir (or `LogFilePath` from `config.json`) | Path to the filter log file. |
| `outputFile` | `KeyboardHeatmap.html` next to the log file | Path for the generated HTML report. |
| `-v` / `--v` | off | Include the **Daily filtered event count** section in the output. |

**Examples:**
