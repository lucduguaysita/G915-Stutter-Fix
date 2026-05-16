# G915-Stutter-Fix

User‑mode keyboard event filter that removes invalid HID sequences and rapid duplicate keypresses on Logitech G915/G915X keyboards or others.

Early user reports confirm the fix eliminates stuttering/double‑keypress issues on affected G915/G915X units.

## Tools

| Tool | Description |
|---|---|
| `KeyboardRepeatFilter.exe` | Runs in the system tray and silently filters stutter/duplicate keypresses in real time. |
| `KeyboardHeatmap.exe` | CLI tool that reads the filter log and generates a self-contained HTML heatmap of filtered key counts. |
## Keyboard Heatmap (New!)

<img width="856" height="505" alt="image" src="https://github.com/user-attachments/assets/dcdec3ad-ca1b-488b-8958-08de4103a487" />


A diagnostic visualization showing which keys generate filtered or duplicate events…

## Quick Start

### KeyboardRepeatFilter

1. Build the solution in `Release` mode.
2. Open the `releases` folder after the build completes.
3. Ensure it contains `KeyboardRepeatFilter.exe`, `Newtonsoft.Json.dll`, and `config.json`.
4. Run `KeyboardRepeatFilter.exe`.
5. Confirm the tray icon appears and test your keyboard normally.

### KeyboardHeatmap

`KeyboardHeatmap.exe` is a companion CLI that parses `KeyboardRepeatFilter.log` and produces a single self-contained `.html` heatmap — no dependencies required.

Open the generated `KeyboardHeatmap.html` in any browser to view the heatmap.

## Documentation

- Usage and configuration: `docs/USAGE.md`
- Frequently asked questions: `FAQ.md`
- Change history: `CHANGELOG.md`
- Troubleshooting: `TROUBLESHOOTING.md`
- Security policy: `SECURITY.md`
- Smoke test checklist: `docs/SMOKE_TESTS.md`
- Release process: `docs/RELEASE.md`
- Config template: `config.template.json`

## Build Environment

- IDE: Visual Studio 2026
- Target framework: .NET Framework 4.8
- OS used for development: Windows 11 x64

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

# Generate a heatmap with daily filtered event count
KeyboardHeatmap.exe -v
```
