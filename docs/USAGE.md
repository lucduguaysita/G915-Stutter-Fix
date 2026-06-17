# Usage and Configuration

## What this app does

`G915-Stutter-Fix` runs in user mode and filters invalid keyboard repeat events (for example, impossible rapid duplicate keypresses) before they reach other applications. It can optionally do the same for chattering mouse buttons (see [Mouse-button debouncing](#mouse-button-debouncing)).

It is designed for Logitech G915/G915X stutter behavior but can help with other keyboards showing the same pattern.

## Prerequisites

- Windows 10/11 x64
- .NET Framework runtime 4.8 required by the app

## Release folder contents

Your `releases` folder should contain:

- `KeyboardRepeatFilter.exe`
- `KeyboardHeatmap.exe`
- `Newtonsoft.Json.dll`
- `config.json`
- `gaming.json` (bundled profile)

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
| `HeatmapDays` | `all` | Heatmap time window: `all` charts the whole log; a positive number charts only entries from that many days back from when the heatmap is run. |
| `FilterMode` | `BlockRepress` | Master switch for how a stutter is filtered (see below). |
| `ShowElevatedWindowNotice` | `true` | Show a brief corner popup when focus moves to an admin window (where filtering is inactive). Set `false` to suppress only the popup; the tray icon still turns yellow and the event is still logged. |
| `RunAsAdmin` | `false` | When `true`, the app relaunches itself elevated on every launch if not already elevated (a UAC prompt appears each time). Toggle from the tray menu. |
| `MinRepeatIntervalMs` | `28.0` | Repeats faster than this many milliseconds are treated as stutter. |
| `ExcludedKeys` | `["Back", "Return"]` | Keys that are never filtered. Names or numeric codes (see below). |
| `ExcludedVkCodes` | _(none)_ | Legacy numeric-only form of `ExcludedKeys`; still honored and merged. |
| `PerKeyMinRepeatIntervalMs` | `{}` | Per-key threshold overrides, keyed by key name or numeric code. |
| `FilterMouseButtons` | `false` | Master switch for mouse-button debouncing (see below). |
| `MouseMinRepeatIntervalMs` | `50.0` | Mouse clicks faster than this many milliseconds are treated as switch chatter. |
| `ExcludedMouseButtons` | `[]` | Mouse buttons that are never filtered. Names: `Left`, `Right`, `Middle`, `X1`, `X2`. |

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

## Mouse-button debouncing

A worn or chattering mouse switch turns a single physical click into a phantom double-click. When
`FilterMouseButtons` is `true`, a separate low-level mouse hook (`WH_MOUSE_LL`) applies the same idea
as `BlockRepress` to clicks: a button press that arrives within `MouseMinRepeatIntervalMs` of that
button's previous release is treated as chatter and dropped, so one click stays one click. It covers
the left, right, middle, and both side (X1/X2) buttons.

- **Disabled by default.** Mouse filtering is off unless you turn it on, so keyboard-only setups are
  unchanged. Enable it from the **Enable mouse click debounce** tray item or by setting
  `"FilterMouseButtons": true`.
- **Threshold.** `MouseMinRepeatIntervalMs` (default **50 ms**) is deliberately well below the
  interval of an intentional double-click (typically 100 ms or more), so real double-clicks are
  preserved while chatter, which bounces in well under that, is removed. Raise it if chatter still
  gets through; lower it if fast intentional double-clicks are being eaten.
- **Exclusions.** `ExcludedMouseButtons` lists buttons that are never filtered, by name: `Left`,
  `Right`, `Middle`, `X1`, `X2` (the `LBUTTON`/`RBUTTON`/`MBUTTON`/`XBUTTON1`/`XBUTTON2` forms are
  also accepted). Matching is case-insensitive; an empty list means every button is debounced.
- **Logging.** With `"LogLevel": "Trace"`, each dropped click is logged as `Mouse_<Button> filtered`
  (for example, `Mouse_Left filtered`). Unrecognized button names are reported as a `ConfigWarning`.

> **Note:** like the keyboard hook, the mouse hook is bypassed for windows running as administrator
> (UIPI) unless the app itself is elevated.

## Gaming and anti-cheat

For games, use the **Protect held keys** filter mode (`"FilterMode": "BlockRelease"`). It is the only
mode that helps movement keys: when a chattering key emits a phantom *release* of a held key
(W/A/S/D), this mode withholds that release so your character keeps moving, instead of letting the
release through and only blocking the re-press (which is all the default `BlockRepress` mode does).

A ready-made **`gaming.json`** profile ships next to the app. Activate it from
**Tray → Profile → gaming**. It is tuned for movement:

- **`FilterMode: BlockRelease`** so a chattering key can't drop your held W/A/S/D mid-run.
- **Per-key 12 ms release** on the movement and crouch keys via
  `"PerKeyMinRepeatIntervalMs": { "W": 12, "A": 12, "S": 12, "D": 12, "RCONTROL": 12 }`. In
  BlockRelease mode a genuine release is held back by exactly the key's threshold (to confirm it
  isn't a bounce), so a low value keeps stops crisp. 12 ms still absorbs the typical sub-22 ms
  chatter; lower it further for a tighter stop, or raise it if chatter starts leaking through.
- **Action keys stay protected.** Tapped keys (E, abilities, numbers) keep the default 28 ms, which
  gives stronger protection against one tap registering twice — exactly what a tap key wants.
- **Pop-ups off** (`ShowElevatedWindowNotice: false`) and **mouse debouncing off** so it cannot
  swallow rapid intentional clicks.

`RCONTROL` targets right Ctrl only (a common crouch bind); use `LCONTROL`, or `Ctrl` for both sides,
if your game crouches on the left.

**What no setting can fix.** The filter is a user-mode low-level keyboard hook, and some games never
let keystrokes reach it:

- **Kernel-level anti-cheat** (e.g. Vanguard, Easy Anti-Cheat, BattlEye, FACEIT) can block or bypass
  low-level hooks entirely.
- **Raw Input / DirectInput**: many games read the device directly, below the hook chain, so the
  filter never sees those keys.
- **Elevated game or anti-cheat process**: Windows (UIPI) bypasses a normal-user hook for a
  higher-privilege window. For this case only, running the app elevated can help — enable
  **Tray → Always run as administrator** before launching the game.

If the keystrokes never reach the hook, no profile or threshold will catch them. That is a Windows
security/architecture limit, not a bug.

## Tray menu

Right-click the tray icon for these options:

- **Always run as administrator** — when checked, the app relaunches elevated immediately (if not
  already) and auto-elevates on every future launch, so the hook can also filter input for
  administrator windows (which Windows otherwise bypasses; see below). Persisted as `RunAsAdmin` in
  `config.json`. A UAC consent prompt appears on each unelevated launch; unchecking it takes effect
  on the next launch.
- **Filter mode** — choose **Block double presses** (`BlockRepress`) or **Protect held keys**
  (`BlockRelease`). The active mode shows a checkmark. Switching takes effect immediately and is
  saved to `config.json`.
- **Enable mouse click debounce** — when checked, debounces chattering mouse buttons (equivalent to
  `"FilterMouseButtons": true`). Toggling it starts or stops the mouse hook immediately and is saved
  to `config.json`.
- **Profile** — lists every `*.json` file next to the app that looks like a config (it shares at
  least two of our setting names). Selecting one loads it as the live configuration and applies it
  immediately; a checkmark shows the active one. To add a profile, drop another config file (e.g.
  `gaming.json`) in the app folder and restart. While a profile is active, the other tray toggles
  save back into **that** file. The active choice is per-session: on the next launch the app starts
  from `config.json` again.
- **Disable nag popups** — when checked, suppresses the brief popup shown when an administrator
  window is focused (equivalent to `"ShowElevatedWindowNotice": false`). The tray icon and log are
  unaffected.
- **Autostart** — launch automatically when you sign in.
- **Heatmap → Generate report / Generate report (verbose)** — runs `KeyboardHeatmap.exe`
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

By default it charts the entire log. Set `HeatmapDays` in `config.json` to a positive number to chart
only the last *N* days (counted back from when the report is generated) and skip older entries; `all`
(the default) keeps everything. When a window is applied, the console prints how many entries were kept.

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
- **Key heatmap** colors each key by how often it was filtered, using a warm ember ramp (light and
  dark themes). It shows the three QWERTY letter rows plus a bottom modifier row (Ctrl, Win, Alt,
  Space, Alt, Fn, Menu, Ctrl) and both Shift keys on row 3; modifiers are matched by their
  side-specific code so left and right are shown separately. The row with the most filtered events is
  flagged with a ⚠.
- **Special & navigation keys** are listed separately with their counts.
- **Mouse buttons** (shown only when mouse debouncing has filtered anything) appear as a small mouse
  graphic with the filter count printed on each button (left, right, middle/wheel, X1, X2), tinted on
  the same intensity ramp.
- **Daily filtered event count** (with `-v`) shows one bar per day, colored from green (a quiet day)
  through yellow to crimson (the busiest day in the log), relative to your own data.
- A **config-warning banner** appears if the log contains `ConfigWarning` entries — i.e. an
  unrecognized key name in `config.json` that you should fix.
