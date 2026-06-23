using System.Collections.Generic;

namespace KeyboardRepeatFilter
{
    public sealed class FilterConfig
    {
        public string LogLevel { get; set; } = "Info";
        public string LogFilePath { get; set; } = "C:\\Temp\\KeyboardRepeatFilter.log";

        // Optional profile to load automatically at startup instead of staying on
        // config.json. The value is a profile file name in the application folder,
        // with or without the ".json" extension, e.g. "WoW" or "WoW.json". When set
        // and the file exists, the app boots straight into that profile (its tray
        // toggles, filter mode, and RunAsAdmin all take effect from launch); later
        // tray edits save back into the activated profile, not config.json. Empty or
        // missing means start on config.json as before. Only read from config.json;
        // it is not chained from within an activated profile.
        public string DefaultProfile { get; set; }

        // Heatmap time window. "all" (default) charts the entire log; otherwise a
        // positive number of days, counted back from the moment the heatmap is run,
        // so older log entries are skipped. Read by KeyboardHeatmap; the main app
        // only needs to round-trip it so the value survives config saves.
        public string HeatmapDays { get; set; } = "all";

        // Master switch for how a stutter is filtered:
        //   "BlockRepress" (default) - the original behaviour. Let the spurious
        //       key-up through and block the duplicate key-down that follows it.
        //       Best for character keys (stops "aa" when you typed "a").
        //   "BlockRelease" - withhold the spurious key-up and, if a bounce
        //       key-down follows within the threshold, drop both so the key stays
        //       logically held. Best for held modifiers (Ctrl/Shift) and game
        //       movement keys, at the cost of deferring every release by up to the
        //       threshold. Any unrecognised value falls back to "BlockRepress".
        public string FilterMode { get; set; } = "BlockRepress";

        // When true (default), a brief corner notification appears each time focus
        // moves to a window running as administrator, where filtering is inactive.
        // Set to false to suppress just the popup; the tray icon still turns yellow,
        // the tooltip still updates, and the event is still logged.
        public bool ShowElevatedWindowNotice { get; set; } = true;

        // Sticky elevation. When true, the app relaunches itself with administrator
        // rights (via a UAC prompt) on every launch if it is not already elevated,
        // so the hook can filter input for elevated windows without re-choosing
        // "Always run as administrator" each time. Toggled from the tray menu. Note
        // that, by design, a UAC consent prompt appears on each unelevated launch.
        public bool RunAsAdmin { get; set; } = false;

        public double MinRepeatIntervalMs { get; set; } = 28.0;

        // Opt-in safeguard for hardware tokens that "type" at machine speed, such as
        // a YubiKey emitting a one-time password. Those devices send dozens of real
        // key events only milliseconds apart, so any repeated character (common in
        // YubiKey modhex OTPs, e.g. "uu") looks exactly like a stutter, gets dropped,
        // and the code fails to authenticate. When true, the filter recognises a
        // sustained burst of key-downs far faster than a human can type and suspends
        // filtering for its duration so the repeats pass through. Off by default and
        // exposed only in config (no tray item): a normal keyboard never reaches the
        // burst threshold, so leaving it off preserves the original behaviour exactly,
        // and only the rare user who types secrets with such a device needs it.
        public bool BurstBypass { get; set; } = false;

        // Master switch for mouse-button debouncing. When true, a separate
        // low-level mouse hook drops a button-down that arrives within
        // MouseMinRepeatIntervalMs of that button's previous button-up, which
        // suppresses the phantom second click produced by a chattering mouse
        // switch. Disabled by default so the app's behaviour is unchanged for
        // users who only need keyboard filtering.
        public bool FilterMouseButtons { get; set; } = false;

        // Debounce window for mouse buttons (milliseconds). Keep this well below
        // the interval of an intentional double-click (typically 100ms+) so real
        // double-clicks are preserved; switch chatter bounces in well under this.
        public double MouseMinRepeatIntervalMs { get; set; } = 50.0;

        // Mouse buttons that are never filtered. Each entry is a button NAME,
        // case-insensitive: "Left", "Right", "Middle", "X1", "X2" (the "LBUTTON"
        // / "RBUTTON" / "MBUTTON" / "XBUTTON1" / "XBUTTON2" forms are also
        // accepted). Empty means every button is debounced.
        public string[] ExcludedMouseButtons { get; set; } = new string[0];

        // Keys that are never filtered. Each entry may be a key NAME or a decimal
        // virtual-key code. Names match the values shown in the log, are
        // case-insensitive, and the "VK_" prefix is optional, e.g. "Back",
        // "Return", "I", "Volume_Down". Numbers are decimal VK codes (e.g. 8 =
        // Backspace). A generic modifier ("Ctrl", "Shift", "Alt") excludes both
        // the left and right keys; use "LCONTROL"/"RCONTROL" to target one side.
        public string[] ExcludedKeys { get; set; } = new[] { "Back", "Return" };

        // Legacy numeric-only form of ExcludedKeys. Still honored and merged with
        // ExcludedKeys so existing configs keep working.
        public int[] ExcludedVkCodes { get; set; }

        // Per-key threshold overrides (milliseconds). Each key may be a name or a
        // decimal VK code, following the same rules as ExcludedKeys, e.g.
        // { "I": 40.0 } or { "73": 40.0 }.
        public Dictionary<string, double> PerKeyMinRepeatIntervalMs { get; set; } = new Dictionary<string, double>();
    }
}
