# Troubleshooting

## App does not start

- Confirm you are on Windows 10/11 x64.
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

## The tray icon turned yellow

The icon goes yellow (and its tooltip reads "paused for this admin window") while
the window you are using belongs to an app running **as administrator** — for
example an elevated terminal, installer, or a game launched with elevated rights.

This is a Windows security rule, not a bug: a keyboard filter running as a normal
user is not allowed to see or change keystrokes going to a higher-privilege
window, so stutter filtering is simply inactive there. The moment you switch back
to a normal window, the icon returns to normal and filtering resumes. Each switch
is noted in the log (`HookBypass` / `HookActive`).

You do not have to do anything. If you want filtering to also cover elevated apps,
the app itself would need to be started with the same elevated rights.

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

- App version (`2.0.0`, etc.)
- Windows version/build
- Keyboard model
- Current `config.json` (redact anything sensitive)
- Relevant log excerpt around the issue
- Exact reproduction steps
