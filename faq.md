# FAQ - G915 Stutter Fix

## What does this utility do?

It filters out impossible HID sequences and rapid duplicate keypresses produced by some Logitech G915/G915X keyboards.

The result is clean, stable, predictable typing without stutters or phantom repeats.

## Which operating systems are supported?

This fix is **Windows 11 x64 only**.

It will not run on:

- Linux
- macOS
- Windows 10 (maybe, but not tested)
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

## Do I need admin rights to run it?

No.

It runs as a normal user-mode process and does not require elevation.

## Does it run in the background?

Yes.

It sits quietly in the system tray and filters keyboard events in real time.

## Is this safe?

Yes.

It does not inject into processes, install drivers, or modify system files.
It simply listens to keyboard events and discards invalid ones.

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
