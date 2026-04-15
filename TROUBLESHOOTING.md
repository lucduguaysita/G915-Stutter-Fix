# Troubleshooting

## App does not start

- Confirm you are on Windows 11 x64.
- Run `KeyboardRepeatFilter.exe` from the `releases` folder.
- Ensure these files are together in `releases`:
  - `KeyboardRepeatFilter.exe`
  - `Newtonsoft.Json.dll`
  - `config.json`
- Check Windows Defender or antivirus quarantine history.

## App starts but filtering does not seem active

- Confirm the app is running in the system tray.
- Open the log file (default: `C:\Temp\KeyboardRepeatFilter.log`).
- Verify events are being recorded while typing.
- Make sure another keyboard utility is not taking exclusive control of input hooks.

## Stutter still happens

- Increase `MinRepeatIntervalMs` in `config.json` by small steps (for example `28` -> `30`).
- Restart the app after changing config.
- If only one key is problematic, add a per-key override in `PerKeyMinRepeatIntervalMs`.

## Legitimate repeats are being filtered

- Decrease `MinRepeatIntervalMs` slightly (for example `28` -> `26`).
- Keep changes small and retest.
- Consider excluding specific keys with `ExcludedVkCodes` if needed.

## Config changes are ignored

- Make sure you edited `releases\config.json` (the one next to the EXE).
- Validate JSON syntax (missing commas or quotes will break parsing).
- Restart the app after any config change.

## Log file is missing

- Verify `LogFilePath` in `config.json`.
- Ensure the folder exists (for example `C:\Temp`).
- Try a writable path under your user profile if needed.

## Startup with Windows does not work

- Re-enable startup from the app if that option is available.
- Check user startup/registry entries for your account.
- Confirm you are not running under a restricted policy that blocks startup entries.

## How to collect useful bug details

When reporting an issue, include:

- App version (`1.1.0`, etc.)
- Windows version/build
- Keyboard model
- Current `config.json` (redact anything sensitive)
- Relevant log excerpt around the issue
- Exact reproduction steps
