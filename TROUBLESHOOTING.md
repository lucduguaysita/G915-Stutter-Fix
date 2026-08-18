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
the window you are using belongs to an app running **as administrator**, for
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
- If you run AutoHotkey or another remapper, see the hook order section below.
- If the stutter happens over Remote Desktop, see the RDP section below.

## Stutter continues while AutoHotkey or another key remapper is running

If you run AutoHotkey, a macro tool, or any other remapper, the order the two
programs started in decides whether this app can see your key bounce at all.

Both programs work the same way: they install a Windows low-level keyboard hook,
which lets them see keystrokes before applications do. Windows calls those hooks
in the order they were installed, and the **most recently installed one runs
first**. Whichever program started last wins.

That matters because a remapper does not just watch a key, it swallows the real
hardware keypress and sends a synthetic replacement. This app deliberately ignores
synthetic keystrokes, because software-generated input cannot be hardware chatter
and debouncing it would corrupt text expanders and similar tools. So when the
remapper runs first, your hardware bounce gets replayed as synthetic input that
this app is designed to pass through:

```
 G915X 'a' switch
   |- a-down  @  0 ms  -+
   +- a-down  @ 12 ms  -+   (the bounce, real hardware)
                        |
                        v
              +---------------------+
              |  Remapper hook      |  'a' is remapped
              |  installed LAST     |  -> swallows BOTH hardware events
              +----------+----------+  -> sends 2 synthetic 'a' presses
                         |
                         v
              +---------------------+
              |  KeyboardRepeat     |  flagged as injected (synthetic)
              |  Filter hook        |  -> passed through untouched
              +----------+----------+
                         v
                       "aa"   <- the bounce survives
```

**The fix:** start `KeyboardRepeatFilter.exe` after the remapper. In practice,
let Windows boot and let the remapper load as normal, then close and restart
`KeyboardRepeatFilter.exe`. It is now the most recent hook and sees real hardware
events first, so the bounce is blocked before the remapper ever gets it.

Two things to know:

- Only remapped keys are affected. A remapper intercepts only the keys it has
  hotkeys or remaps for, so everything else still reaches this app as normal
  hardware regardless of order. Expect a few problem keys, not total failure.
- The order can change while you are logged in. Either program jumps back to the
  front whenever it reinstalls its hook, and Windows silently drops the hook of a
  process that responds too slowly, after which that process re-registers and
  takes the front spot. A PC can therefore boot in the good order and drift into
  the bad one, which looks like the app getting worse over time. Restarting
  `KeyboardRepeatFilter.exe` puts it back in front.

To rule the remapper in or out, exit it completely and type normally for a while.
If the stutter stops, the hook order is the cause.

## Stutter over Remote Desktop (RDP)

Filter on the machine the keyboard is physically attached to.

When you type on a laptop into a Remote Desktop session on another PC, the
keystrokes reach the remote PC as input injected by the RDP client rather than as
local hardware, and their timing has crossed a network by then. A copy of the app
running on the remote PC therefore cannot reliably debounce them: key bounce is a
sub-30 ms event, and network jitter alone can stretch a 12 ms bounce past the
threshold. Stopping or starting the app on the remote PC will usually make no
difference to keys typed from the client.

Install and auto-start the app on the client machine, the one whose keyboard is
producing the chatter. There the app sees genuine hardware events with genuine
timing, and blocks the bounce before it is ever sent over the wire.

Note that a hook only sees input in its own session, so the copy running on the
physical console of a PC does not cover a separate Remote Desktop session on that
same PC.

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
