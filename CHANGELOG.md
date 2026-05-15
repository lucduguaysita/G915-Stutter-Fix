# Changelog

All notable changes to this project are documented in this file.

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

