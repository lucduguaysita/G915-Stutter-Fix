using System;

namespace KeyboardHeatmap
{
    /// <summary>Represents a single parsed line from the KeyboardRepeatFilter log.</summary>
    public sealed class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogEntryKind Kind { get; set; }

        // For Filtered entries
        public string KeyName { get; set; }      // e.g. "I", "VK_DELETE"
        public int? KeyCode { get; set; }         // e.g. 73, 46
        public string Action { get; set; }        // e.g. "filtered" (BlockRepress), "release-held" (BlockRelease)

        // For Startup entries
        public string Version { get; set; }
        public int? Pid { get; set; }
        public int? MinRepeatIntervalMs { get; set; }

        // For Shutdown entries
        public string ShutdownReason { get; set; }
        public double? UptimeSec { get; set; }

        // For ConfigWarning entries
        public string Message { get; set; }
    }

    public enum LogEntryKind { Filtered, Startup, Shutdown, ConfigWarning, Unknown }
}
