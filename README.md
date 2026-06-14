# G915-Stutter-Fix

**A tiny, user-mode keyboard filter that makes a stuttering keyboard feel brand new again.**

Some Logitech G915/G915X units (and other keyboards with the same defect) emit *impossible* HID
sequences — phantom key repeats and double-presses that arrive faster than any human could ever
type. The result is maddening: `thiss becomess thiis`, a held `Ctrl` that randomly lets go
mid-shortcut, a game character that won't keep walking. `G915-Stutter-Fix` sits quietly in your
system tray, watches the keyboard at the lowest user-mode level Windows allows, and silently drops
those invalid events *before* they ever reach your applications.

No drivers. No firmware flashing. No registry surgery. No network access. Close the app and your
system is exactly as it was. User reports confirm it eliminates the stutter/double-keypress problem
on affected G915/G915X units — and it's small enough to read end-to-end in a coffee break.

> **Version 2.1.0**, Windows 11 x64 · .NET Framework 4.8 · MIT licensed · 100% offline

---

## Tools

| Tool | Description |
|---|---|
| `KeyboardRepeatFilter.exe` | Runs in the system tray and silently filters stutter/duplicate keypresses in real time. |
| `KeyboardHeatmap.exe` | Companion CLI that reads the filter log and generates a self-contained HTML heatmap of filtered key counts — great for *seeing* which keys misbehave. |

---

## What's new in 2.1.0

- **Restart as administrator.** A new tray menu item (shown only while running unelevated)
  relaunches the app with administrator rights via a UAC prompt, so the keyboard hook can also
  filter input for **elevated windows** instead of being bypassed for them (UIPI). The
  elevated-window notice is now clickable and triggers the same relaunch.
- **A more readable elevated-window notice.** The popup stays up longer and pauses its
  auto-dismiss countdown while the pointer is over it, so you can actually read and click it.
- **A smoother admin handoff.** The single-instance guard now waits briefly for the mutex instead
  of exiting immediately, so the restart-as-admin handoff no longer trips the singleton and kills
  the new elevated instance.

See [`CHANGELOG.md`](CHANGELOG.md) for the complete list.

---

## What's new in 2.0.0

Version 2.0 is a substantial upgrade over the 1.x line. Highlights:

- **Two filter modes, switchable from the tray.** The classic mode stops duplicate characters;
  the new **Protect held keys** mode keeps `Ctrl`/`Shift` and game movement keys logically held
  through a bounce, so shortcuts and sprint keys stop breaking.
- **Human-readable config.** Exclude keys and set per-key thresholds by **name** (`Back`,
  `Return`, `I`, `Ctrl`) instead of cryptic virtual-key numbers. Typing `Ctrl` covers *both* the
  left and right keys automatically.
- **A genuinely useful heatmap.** A warm "ember" color ramp, a dynamic green→yellow→crimson
  daily-activity chart, an automatic "busiest row" flag, and a banner that warns you about typos
  in your config.
- **It tells you when it can't help.** When you focus a window running **as administrator**,
  Windows forbids a normal-user filter from touching its input. Instead of looking broken, the
  app turns its tray icon yellow, shows a brief (non-focus-stealing) notice, and logs it — then
  silently recovers when you switch back.
- **Everything important is one click away.** Filter mode, the notice popup, and autostart are all
  toggles in the tray menu, and every choice persists to `config.json`.

See [`CHANGELOG.md`](CHANGELOG.md) for the complete list.

---

## Features in detail

### Real-time stutter filtering
A low-level keyboard hook (`WH_KEYBOARD_LL`) inspects every key event and discards repeats that
arrive faster than a configurable threshold (default **28 ms** — below the biomechanical limit of a
real double-tap). Filtering is per-key configurable and certain keys (Backspace, Enter, volume) are
excluded by default so legitimate fast input is never touched.

### Two filter modes
- **Block double presses** (`BlockRepress`, default) — blocks the spurious *re-press*, so one tap
  produces one character. Ideal for normal typing.
- **Protect held keys** (`BlockRelease`) — withholds the spurious *release*, so a held modifier or
  movement key stays down through a bounce. Ideal for `Ctrl`/`Shift` shortcuts and gaming, at the
  cost of a few milliseconds of release latency.

Switch between them live from **Tray → Filter mode**; the choice is saved and applied immediately.

### Friendly, forgiving configuration
`ExcludedKeys` and per-key thresholds accept key **names** exactly as they appear in the log — the
`VK_` prefix is optional and matching is case-insensitive. Generic modifiers (`Ctrl`, `Shift`,
`Alt`) expand to both the left and right keys. Raw numeric virtual-key codes still work as an escape
hatch, and unrecognized names are reported in the log rather than failing silently.

### Elevated-window awareness
Windows security (UIPI) prevents a normal-user hook from filtering input to **administrator**
windows. The app detects this state, turns the tray icon **yellow** with a plain-language tooltip,
shows a brief focus-safe corner notice (toggleable), and records it in the log — recovering
automatically when a normal window regains focus.

### Diagnostic heatmap
`KeyboardHeatmap.exe` turns your filter log into a beautiful, self-contained HTML report so you can
see exactly which keys (and which days) are the worst offenders.

---

## Keyboard Heatmap

<img width="880" alt="Keyboard Repeat Filter heatmap report — 2.0 ember theme" src="docs/heatmap.png" />

A diagnostic visualization showing which keys generate filtered/duplicate events, rendered with a
warm ember intensity ramp (light and dark themes), a "busiest row" flag, summary stat cards, an
optional daily-activity chart, and a banner that surfaces any configuration warnings found in the
log.

| Argument | Default | Description |
|---|---|---|
| `logFile` | `KeyboardRepeatFilter.log` in the current dir (or `LogFilePath` from `config.json`) | Path to the filter log file. |
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

The report is a single `.html` file with no external dependencies — open it in any browser. On
success, `KeyboardHeatmap.exe` opens it for you automatically.

---

## Quick Start

### KeyboardRepeatFilter

1. Build the solution in `Release` mode (or download a release).
2. Open the `releases` folder after the build completes.
3. Ensure it contains `KeyboardRepeatFilter.exe`, `KeyboardHeatmap.exe`, `Newtonsoft.Json.dll`, and
   `config.json`.
4. Copy those files to a writable folder of your choice (for example `C:\Utils\KeyboardRepeatFilter`).
5. Run `KeyboardRepeatFilter.exe`.
6. Confirm the tray icon appears and type normally — the stutter should be gone.

Right-click the tray icon to switch **Filter mode**, toggle the notice popup, enable
**Autostart**, or launch the heatmap.

### KeyboardHeatmap

`KeyboardHeatmap.exe` parses `KeyboardRepeatFilter.log` and produces a single self-contained `.html`
heatmap — no dependencies required. You can run it directly, or launch it from
**Tray → Keyboard Heatmap → Generate report**. (Logging must be enabled — see below.)

> **Tip:** the heatmap is built from the log, so set `"LogLevel": "Trace"` in `config.json` and make
> sure `LogFilePath` points somewhere writable. If no log exists yet, the tray launcher explains
> exactly what to do instead of failing silently.

---

## "Unknown publisher" is normal

When you first run `KeyboardRepeatFilter.exe`, Windows may show one or both of these prompts:

- **User Account Control** ("Do you want to allow this app to make changes?") listing
  **Publisher: Unknown**, with a yellow banner.
- **SmartScreen** ("Windows protected your PC") with a **Run anyway** option hidden behind
  **More info**.

This is expected and harmless. The executables are not code-signed, so Windows cannot display a
verified publisher name. Code-signing certificates cost money and need renewing every year, which
isn't justified for a tiny, open-source, fully offline utility. Nothing about these warnings
indicates the app is unsafe.

To run it:

- On the **UAC** prompt, click **Yes**.
- On the **SmartScreen** prompt, click **More info**, then **Run anyway**.

If you'd rather verify before trusting it: the complete C# source is in the `src` folder, the app
makes no network access, and you can build the executables yourself from source (see
[Build Environment](#build-environment)).

---

## Configuration at a glance

Everything is controlled by `config.json` next to the executable. Full reference and examples live in
[`docs/USAGE.md`](docs/USAGE.md); the short version:

| Setting | Default | Purpose |
|---|---|---|
| `LogLevel` | `Info` | `Trace` logs every filtered key (needed for the heatmap). |
| `LogFilePath` | `C:/Temp/KeyboardRepeatFilter.log` | Where the log is written. |
| `FilterMode` | `BlockRepress` | `BlockRepress` (stop double presses) or `BlockRelease` (protect held keys). |
| `ShowElevatedWindowNotice` | `true` | Show the brief popup when an admin window is focused. |
| `MinRepeatIntervalMs` | `28.0` | Repeats faster than this are treated as stutter. |
| `ExcludedKeys` | `["Back", "Return"]` | Keys never filtered, by name or number. |
| `PerKeyMinRepeatIntervalMs` | `{}` | Per-key threshold overrides, by name or number. |

The **Filter mode** and **Disable nag popups** tray toggles write straight back to this file, so the
GUI and the config file never disagree.

---

## Documentation

- Usage and configuration: [`docs/USAGE.md`](docs/USAGE.md)
- Frequently asked questions: [`FAQ.md`](FAQ.md)
- Change history: [`CHANGELOG.md`](CHANGELOG.md)
- Troubleshooting: [`TROUBLESHOOTING.md`](TROUBLESHOOTING.md)
- Security policy: [`SECURITY.md`](SECURITY.md)
- Smoke test checklist: [`docs/SMOKE_TESTS.md`](docs/SMOKE_TESTS.md)
- Release process: [`docs/RELEASE.md`](docs/RELEASE.md)
- Config template: [`config.template.json`](config.template.json)

## Build Environment

- IDE: Visual Studio 2026
- Target framework: .NET Framework 4.8
- OS used for development: Windows 11 x64
- The solution builds `KeyboardHeatmap` first and copies it next to the main app automatically, so
  the **Generate report** menu item works straight from a fresh build (Debug or Release).

## License

Released under the **MIT License** — full reuse, modification, and redistribution permitted. The
complete C# source is in the `src` folder.
