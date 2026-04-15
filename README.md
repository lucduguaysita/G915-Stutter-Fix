# G915-Stutter-Fix
User‑mode keyboard event filter that removes invalid HID sequences and rapid duplicate keypresses on Logitech G915/G915X keyboards or others

Early user reports confirm the fix eliminates stuttering/double‑keypress issues on affected G915/G915X units.

## Quick Start

1. Build the project in `Release` mode.
2. Open the `releases` folder after the build completes.
3. Ensure it contains `KeyboardRepeatFilter.exe`, `Newtonsoft.Json.dll`, and `config.json`.
4. Run `KeyboardRepeatFilter.exe`.
5. Confirm the tray icon appears and test your keyboard normally.

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
