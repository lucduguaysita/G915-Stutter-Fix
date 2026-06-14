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

## Configuration (`config.json`)

| Field | Default | Description |
|---|---|---|
| `LogLevel` | `Info` | Set to `Trace` to log every filtered event to `LogFilePath`. |
| `LogFilePath` | `C:/Temp/KeyboardRepeatFilter.log` | Where the filter/lifecycle log is written. |
| `FilterMode` | `BlockRepress` | Master switch for how a stutter is filtered (see below). |
| `ShowElevatedWindowNotice` | `true` | Show a brief corner popup when focus moves to an admin window (where filtering is inactive). Set `false` to suppress only the popup; the tray icon still turns yellow and the event is still logged. |
| `MinRepeatIntervalMs` | `28.0` | Repeats faster than this many milliseconds are treated as stutter. |
| `ExcludedKeys` | `["Back", "Return"]` | Keys that are never filtered. Names or numeric codes (see below). |
| `ExcludedVkCodes` | _(none)_ | Legacy numeric-only form of `ExcludedKeys`; still honored and merged. |
| `PerKeyMinRepeatIntervalMs` | `{}` | Per-key threshold overrides, keyed by key name or numeric code. |

### Key names

`ExcludedKeys` and the keys of `PerKeyMinRepeatIntervalMs` accept **friendly key names** instead of raw numbers:

```json
"ExcludedKeys": ["Back", "Return", "Volume_Down", "Volume_Up"],
"PerKeyMinRepeatIntervalMs": { "I": 40.0 }
```

- **Use the name as it appears in the log.** With `"LogLevel": "Trace"`, each filtered key is logged like `VK_LCONTROL=162 filtered`. The part before the `=` is a valid name to put in the config — the log and the config share one lookup table, so anything you see logged you can paste back (with or without the `VK_`).
- The **`VK_` prefix is optional** and matching is **case-insensitive**: `VK_BACK`, `Back`, and `back` are equivalent. Letter and digit keys are their bare character: `"I"`, `"A"`.
- **Numeric codes still work** as a fallback — `8` is the same as `"Back"`. Any whole number is treated as a decimal virtual-key code.

#### Ctrl / Shift / Alt

A generic modifier name covers **both** sides automatically: putting `"Ctrl"` (or `"Control"`) in `ExcludedKeys` excludes left *and* right Control (and the generic code). The same applies to `"Shift"` and `"Alt"` (alias for `Menu`). This is usually what you want, since keyboards differ in whether they report the generic or the left/right-specific code.

To target just **one** side, use the specific name instead: `"LCONTROL"`/`"RCONTROL"`, `"LSHIFT"`/`"RSHIFT"`, `"LMENU"`/`"RMENU"`. The same expansion applies to `PerKeyMinRepeatIntervalMs` — a generic modifier sets the threshold for both sides.

> **Note:** Because plain numbers are read as virtual-key codes, the number keys `0`–`9` can't be referenced by their bare character (`"0"` means VK code 0, not the `0` key). Use their VK code (`48`–`57`) for those.

If a name can't be recognized, it is ignored and a `ConfigWarning` line is written to the log so you can spot the typo.

### `FilterMode`

- **`BlockRepress`** (default) — the original behavior. The spurious key-up is
  allowed through and the duplicate key-down that follows it is blocked. Best for
  character keys: it stops `aa` when you only pressed `a` once.
- **`BlockRelease`** — the spurious key-up is withheld instead. If a bounce
  key-down follows within `MinRepeatIntervalMs`, both are dropped so the key stays
  logically held; otherwise the genuine key-up is re-emitted once the window
  elapses. Best for held modifiers (`Ctrl`/`Shift`) and game movement keys, where a
  phantom release breaks shortcuts. The trade-off is that every release of a
  filtered key is deferred by up to the threshold (~28 ms), adding a small amount
  of input latency.

Set `FilterMode` back to `BlockRepress` to restore the exact original behavior. You can also switch
modes from the tray menu (see below) without editing the file.

## Tray menu

Right-click the tray icon for these options:

- **Filter mode** — choose **Block double presses** (`BlockRepress`) or **Protect held keys**
  (`BlockRelease`). The active mode shows a checkmark. Switching takes effect immediately and is
  saved to `config.json`.
- **Disable nag popups** — when checked, suppresses the brief popup shown when an administrator
  window is focused (equivalent to `"ShowElevatedWindowNotice": false`). The tray icon and log are
  unaffected.
- **Autostart** — launch automatically when you sign in.
- **Keyboard Heatmap → Generate report / Generate report (verbose)** — runs `KeyboardHeatmap.exe`
  against your current log and opens the HTML report; the verbose option adds the daily-activity
  chart. If no log exists yet, a message explains how to enable logging.
- **About…** — version and project information.
- **Exit** — stop filtering and close the app.

Every toggle persists to `config.json`, so the menu and the file never disagree.

## Elevated-window indicator

Windows does not allow a normal-user keyboard filter to see or modify input going to a window running
**as administrator** (this is User Interface Privilege Isolation, a security feature). While such a
window is focused, filtering is inactive for it — and the app makes that state visible instead of
appearing broken:

- the **tray icon turns yellow**, with a tooltip explaining what is happening;
- a **brief, focus-safe popup** appears each time you focus an elevated window (toggle it off with
  **Disable nag popups** or `ShowElevatedWindowNotice`);
- the transition is **logged** as `HookBypass` (entering) and `HookActive` (returning to normal).

It recovers automatically the moment a normal window regains focus. The popup never steals keyboard
focus, and the app never asks you to run as administrator. See `TROUBLESHOOTING.md` for more.

## Run at startup

The app can register itself to start automatically with Windows for your user account.

If startup is enabled, it will launch silently in the background after sign-in.
From the system tray icon context menu you can select "Autostart" to add it to startup.

## KeyboardHeatmap

`KeyboardHeatmap.exe` is a companion CLI tool that parses the filter log and produces a self-contained HTML heatmap of filtered key counts.

| Argument | Default | Description |
|---|---|---|
| `logFile` | `KeyboardRepeatFilter.log` in current dir (or `LogFilePath` from `config.json`) | Path to the filter log file. |
| `outputFile` | `KeyboardHeatmap.html` next to the log file | Path for the generated HTML report. |
| `-v` / `--v` | off | Include the **Daily filtered event count** section in the output. |

**Examples:**

```bash
# Generate a heatmap from the default log file
KeyboardHeatmap.exe

# Generate a heatmap from a specific log file
KeyboardHeatmap.exe "C:\temp\KeyboardRepeatFilter.log"

# Generate a heatmap including the daily filtered-event chart
KeyboardHeatmap.exe -v
```

The report is a single self-contained `.html` file (no external dependencies). On success it opens
in your default browser automatically. Logging must be enabled (`"LogLevel": "Trace"`) for there to
be anything to chart.

### Reading the report

- **Stat cards** summarize total filtered events, the most-filtered key, how many distinct keys were
  affected, and the date of the **last** event.
- **Key heatmap** colors each letter key by how often it was filtered, using a warm ember ramp
  (light and dark themes). The QWERTY row with the most filtered events is flagged with a ⚠.
- **Special & navigation keys** are listed separately with their counts.
- **Daily filtered event count** (with `-v`) shows one bar per day, colored from green (a quiet day)
  through yellow to crimson (the busiest day in the log), relative to your own data.
- A **config-warning banner** appears if the log contains `ConfigWarning` entries — i.e. an
  unrecognized key name in `config.json` that you should fix.
