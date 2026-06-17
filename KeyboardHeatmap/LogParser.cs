using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace KeyboardHeatmap
{
    /// <summary>Parses KeyboardRepeatFilter log files into <see cref="LogEntry"/> objects.</summary>
    public static class LogParser
    {
        // 2026-04-15 13:52:17.725 - Startup: version=1.1.0.0, pid=5136, minRepeatIntervalMs=28
        private static readonly Regex StartupRx = new Regex(
            @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+) - Startup: version=(?<ver>[^,]+), pid=(?<pid>\d+), minRepeatIntervalMs=(?<ms>\d+)",
            RegexOptions.Compiled);

        // 2026-04-16 15:28:01.784 - Shutdown: reason=UserExit, uptimeSec=30787.1, pid=21300
        private static readonly Regex ShutdownRx = new Regex(
            @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+) - Shutdown: reason=(?<reason>[^,]+), uptimeSec=(?<uptime>[^,]+), pid=(?<pid>\d+)",
            RegexOptions.Compiled);

        // 2026-06-13 18:55:01.123 - ConfigWarning: Unrecognized key name(s) in config (ignored): foo
        private static readonly Regex ConfigWarningRx = new Regex(
            @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+) - ConfigWarning: (?<msg>.+)$",
            RegexOptions.Compiled);

        // 2026-04-15 16:53:41.439 - I=73 filtered          (BlockRepress mode)
        // 2026-04-15 17:48:11.156 - VK_DELETE=46 filtered  (BlockRepress mode)
        // 2026-05-15 09:01:22.010 - LSHIFT=160 release-held (BlockRelease mode)
        private static readonly Regex FilteredRx = new Regex(
            @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+) - (?<key>[A-Za-z0-9_]+)=(?<code>\d+) (?<action>filtered|release-held)",
            RegexOptions.Compiled);

        // 2026-06-17 12:56:36.884 - Mouse_Left filtered   (mouse buttons have no VK code)
        private static readonly Regex MouseFilteredRx = new Regex(
            @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+) - Mouse_(?<button>[A-Za-z0-9]+) (?<action>filtered)",
            RegexOptions.Compiled);

        public static List<LogEntry> ParseFile(string path)
        {
            var entries = new List<LogEntry>();

            foreach (var line in File.ReadLines(path))
            {
                var entry = ParseLine(line.Trim());
                if (entry != null)
                    entries.Add(entry);
            }

            return entries;
        }

        private static LogEntry ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            Match m;

            m = FilteredRx.Match(line);
            if (m.Success)
            {
                return new LogEntry
                {
                    Timestamp = ParseTimestamp(m.Groups["ts"].Value),
                    Kind      = LogEntryKind.Filtered,
                    KeyName   = m.Groups["key"].Value,
                    KeyCode   = int.Parse(m.Groups["code"].Value),
                    Action    = m.Groups["action"].Value
                };
            }

            m = MouseFilteredRx.Match(line);
            if (m.Success)
            {
                // Keep the "Mouse_" prefix on KeyName so the heatmap can route these
                // to the mouse graphic instead of the keyboard/special-key buckets.
                return new LogEntry
                {
                    Timestamp = ParseTimestamp(m.Groups["ts"].Value),
                    Kind      = LogEntryKind.Filtered,
                    KeyName   = "Mouse_" + m.Groups["button"].Value,
                    Action    = m.Groups["action"].Value
                };
            }

            m = StartupRx.Match(line);
            if (m.Success)
            {
                return new LogEntry
                {
                    Timestamp          = ParseTimestamp(m.Groups["ts"].Value),
                    Kind               = LogEntryKind.Startup,
                    Version            = m.Groups["ver"].Value,
                    Pid                = int.Parse(m.Groups["pid"].Value),
                    MinRepeatIntervalMs = int.Parse(m.Groups["ms"].Value)
                };
            }

            m = ShutdownRx.Match(line);
            if (m.Success)
            {
                return new LogEntry
                {
                    Timestamp      = ParseTimestamp(m.Groups["ts"].Value),
                    Kind           = LogEntryKind.Shutdown,
                    ShutdownReason = m.Groups["reason"].Value,
                    UptimeSec      = double.Parse(m.Groups["uptime"].Value,
                                        System.Globalization.CultureInfo.InvariantCulture),
                    Pid            = int.Parse(m.Groups["pid"].Value)
                };
            }

            m = ConfigWarningRx.Match(line);
            if (m.Success)
            {
                return new LogEntry
                {
                    Timestamp = ParseTimestamp(m.Groups["ts"].Value),
                    Kind      = LogEntryKind.ConfigWarning,
                    Message   = m.Groups["msg"].Value
                };
            }

            return new LogEntry
            {
                Timestamp = DateTime.MinValue,
                Kind      = LogEntryKind.Unknown
            };
        }

        private static DateTime ParseTimestamp(string s)
        {
            return DateTime.ParseExact(s, "yyyy-MM-dd HH:mm:ss.fff",
                System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
