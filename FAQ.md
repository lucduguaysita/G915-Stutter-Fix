# FAQ - G915 Stutter Fix

## What does this utility do?

It filters out impossible HID sequences and rapid duplicate keypresses produced by some Logitech G915/G915X keyboards.

The result is clean, stable, predictable typing without stutters or phantom repeats.

## What are the two filter modes?

Version 2.0 and later adds a **Filter mode** choice (right-click the tray icon → **Filter mode**):

- **Block double presses** (default) — stops a stutter from registering a key twice, so one tap
  produces one character. This is the classic behavior and is best for everyday typing.
- **Protect held keys (Ctrl, Shift)** — instead of blocking the duplicate press, it suppresses the
  phantom *release*, so a key you are holding stays held through a bounce. This is best when the
  stutter keeps breaking held-modifier shortcuts (`Ctrl`+something) or game movement keys. The
  trade-off is that each key release is delayed by a few milliseconds.

Your choice is saved to `config.json` and applied immediately — no restart needed.

## How do I stop a specific key from being filtered, or filter one key more aggressively?

Edit `config.json`. As of 2.0 you can use **key names** instead of numbers:

```json
"ExcludedKeys": ["Back", "Return", "Volume_Down", "Volume_Up"],
"PerKeyMinRepeatIntervalMs": { "I": 40.0 }
```

Names match what you see in the log (the `VK_` prefix is optional and case doesn't matter). Typing
`Ctrl`, `Shift`, or `Alt` automatically covers both the left and right keys. Plain numbers (virtual-
key codes) still work too. See `docs/USAGE.md` for the full reference.

## Which operating systems are supported?

This fix supports **Windows 10/11 x64**. It is developed and tested on Windows 11; the APIs it uses
are all available on Windows 10, so it runs there too (Windows 10 1903+ ships .NET Framework 4.8;
older builds may need the 4.8 runtime installed).

It will not run on:

- Linux
- macOS
- Windows on ARM

## Does this modify drivers or firmware?

No.

It runs entirely in user mode and does not touch:

- system drivers
- registry entries
- firmware
- kernel components

It is safe and reversible: just close the app.

## Does this fix hardware debounce issues?

Not at the hardware level.

It removes invalid or impossible key sequences before they reach applications, effectively eliminating the symptoms even if the keyboard hardware is misbehaving.

## Will this work with other keyboards?

Yes, if the keyboard produces similar invalid HID sequences.

It was built for the G915/G915X, but it may help with other models that exhibit the same behavior.

## Will it catch my movement keys (WASD) in games?

Sometimes. Use the **Protect held keys** filter mode — the ready-made **gaming** profile (Tray →
Profile → gaming) sets this. It is the mode that keeps a held key down through a chatter bounce,
which is exactly what movement keys need; the default mode does not help a key you are holding.

But the filter is a user-mode keyboard hook, and some games keep keystrokes away from it no matter
how it is configured: kernel-level anti-cheat (Vanguard, EAC, BattlEye, FACEIT) can block hooks,
many games read the device through Raw Input / DirectInput below the hook, and elevated game
processes bypass a normal-user hook (try **Always run as administrator** for that last case). When
the keystrokes never reach the hook, no setting can help.

## Do I need admin rights to run it?

No.

It runs as a normal user-mode process and does not require elevation. Running without admin rights
is the safe, recommended way to use it.

There is one caveat, and the app is honest about it: Windows security forbids a normal-user keyboard
filter from touching input that goes to a window running **as administrator** (an elevated terminal,
installer, etc.). While such a window is focused, filtering is simply inactive there — see the next
question.

## Why did the tray icon turn yellow?

The icon turns yellow (with a tooltip reading "paused for this admin window") whenever the window you
are using belongs to an app running **as administrator**. This is a Windows security rule, not a
bug: a filter running as a normal user is not allowed to see or change keystrokes going to a higher-
privilege window, so stutter filtering is inactive there.

You do not have to do anything. The moment you switch back to a normal window, the icon returns to
normal and filtering resumes. Each switch is recorded in the log (`HookBypass` / `HookActive`).

A brief popup also appears each time you focus an elevated window. If you find it chatty, turn it off
with **Tray → Disable nag popups** (or set `"ShowElevatedWindowNotice": false` in `config.json`). The
icon and log still work — only the popup is suppressed.

## Can I change settings without editing config.json?

Yes. Right-click the tray icon for the common toggles:

- **Filter mode** — switch between "Block double presses" and "Protect held keys".
- **Disable nag popups** — silence the elevated-window popup.
- **Autostart** — launch automatically when you sign in.
- **Heatmap** — generate the diagnostic report (normal or verbose).

Every toggle is written back to `config.json`, so the menu and the file always agree.

## Does it run in the background?

Yes.

It sits quietly in the system tray and filters keyboard events in real time.

## Is this safe?

Yes.

It does not inject into processes, install drivers, or modify system files.
It simply listens to keyboard events and discards invalid ones.
This is not a keylogger, only duplicated key are log in c:\temp.
There is no phone home, no update checks, no network connectivity is required.
It works perfectly in a air-gapped environment.

## Why is `Newtonsoft.Json.dll` included?

The utility uses JSON for configuration, and .NET Framework does not embed this dependency into the EXE.

The DLL must be in the same folder as the EXE.

## Is the source code included?

Yes. The full C# source is in the `src` folder under the MIT license.

## Can I modify or redistribute this?

Yes.

The project is released under the MIT License, which allows full reuse, modification, and redistribution.

## Minor inconvenience?

A log file is written to `C:\temp\KeyboardRepeatFilter.log` showing which virtual key was filtered.

It can be safely deleted and/or not created when "LogLevel": "None" in the config.json file

## Why is the debounce threshold set to 28 ms?

The 28 ms threshold is chosen because it cleanly separates real human keypress timing from the invalid HID sequences produced by the G915/G915X stutter issue.

In testing:

- Real, intentional double keypresses from humans occur at 35-50 ms or slower.
- G915/G915X stutter events occur at 1-10 ms, sometimes up to about 22 ms.
- 28 ms is the boundary where glitch events are filtered without affecting legitimate typing.

This value is not arbitrary. It reflects the biomechanical limit of how fast a person can physically press the same key twice (down -> up -> down).

Anything below about 30 ms is not humanly possible and is therefore safe to discard.

## Why I do not see the keyboard heatmap when invoking `KeyboardHeatmap.exe`?
`KeyboardHeatmap.exe` is a CLI tool that reads the filter log and generates a self-contained HTML heatmap of filtered key counts.
By default the log file is expected to be in `C:\temp`. If the log file is not found, it will not generate the heatmap.\
To fix this, you can either:

Create the default c:\temp directory and ensure the log file is being written there

Or 

Alter the config.json to point to the correct log file path by changing the `LogFilePath` value. This way, `KeyboardHeatmap.exe` will read from the correct location and generate the heatmap successfully.
