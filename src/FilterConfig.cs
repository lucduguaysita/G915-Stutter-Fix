using System.Collections.Generic;

namespace KeyboardRepeatFilter
{
    public sealed class FilterConfig
    {
        public string LogLevel { get; set; } = "Info";
        public string LogFilePath { get; set; } = "C:\\Temp\\KeyboardRepeatFilter.log";

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

        public double MinRepeatIntervalMs { get; set; } = 28.0;

        // Keys that are never filtered. Each entry may be a key NAME or a decimal
        // virtual-key code. Names match the values shown in the log, are
        // case-insensitive, and the "VK_" prefix is optional — e.g. "Back",
        // "Return", "I", "Volume_Down". Numbers are decimal VK codes (e.g. 8 =
        // Backspace). A generic modifier ("Ctrl", "Shift", "Alt") excludes both
        // the left and right keys; use "LCONTROL"/"RCONTROL" to target one side.
        public string[] ExcludedKeys { get; set; } = new[] { "Back", "Return" };

        // Legacy numeric-only form of ExcludedKeys. Still honored and merged with
        // ExcludedKeys so existing configs keep working.
        public int[] ExcludedVkCodes { get; set; }

        // Per-key threshold overrides (milliseconds). Each key may be a name or a
        // decimal VK code, following the same rules as ExcludedKeys — e.g.
        // { "I": 40.0 } or { "73": 40.0 }.
        public Dictionary<string, double> PerKeyMinRepeatIntervalMs { get; set; } = new Dictionary<string, double>();
    }
}
