using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace KeyboardHeatmap
{
    /// <summary>
    /// KeyboardHeatmap, parses a KeyboardRepeatFilter log and produces an HTML heatmap.
    ///
    /// Usage:
    ///   KeyboardHeatmap.exe [logFile] [outputFile]
    ///
    ///   logFile   , path to KeyboardRepeatFilter.log  (default: KeyboardRepeatFilter.log in current dir)
    ///   outputFile, path for the generated HTML file   (default: KeyboardHeatmap.html in current dir)
    ///
    /// If a config.json exists in the current directory it is read for defaults:
    ///   { "LogFilePath": "C:/Temp/KeyboardRepeatFilter.log" }
    ///   The output file is placed in the same directory as the log file.
    ///
    /// Flags:
    ///   -v | -V | --v | --V     Include the "Daily filtered event count" section in the output.
    ///   -classic | --classic    Render the classic HTML keyboard layout instead of the
    ///                           G915X photo overlay.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // ── Strip flags from args ──────────────────────────────────────────────
            bool showDaily = false;
            bool classic = false;
            var positional = new System.Collections.Generic.List<string>();
            foreach (string arg in args)
            {
                if (arg == "-v" || arg == "-V" || arg == "--v" || arg == "--V")
                    showDaily = true;
                else if (string.Equals(arg, "-classic", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(arg, "--classic", StringComparison.OrdinalIgnoreCase))
                    classic = true;
                else
                    positional.Add(arg);
            }

            string logPath    = positional.Count >= 1 ? positional[0] : null;
            string outputPath = positional.Count >= 2 ? positional[1] : null;

            // ── Read config.json for defaults ──────────────────────────────────────
            string heatmapDaysRaw = null;
            string configPath = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
            if (File.Exists(configPath))
            {
                string configText = File.ReadAllText(configPath);
                // Simple regex-based read, no external JSON dependency required
                var match = Regex.Match(configText, @"""LogFilePath""\s*:\s*""([^""]+)""");
                if (match.Success)
                {
                    string configLog = match.Groups[1].Value.Replace("\\\\", "\\");
                    if (logPath == null)
                        logPath = configLog;
                    if (outputPath == null)
                        outputPath = Path.Combine(
                            Path.GetDirectoryName(Path.GetFullPath(configLog)),
                            "KeyboardHeatmap.html");
                }

                // HeatmapDays may be a quoted string ("all") or a bare number (7).
                var daysMatch = Regex.Match(configText, @"""HeatmapDays""\s*:\s*""?(?<v>[^"",}\s]+)""?");
                if (daysMatch.Success)
                    heatmapDaysRaw = daysMatch.Groups["v"].Value;
            }

            if (logPath == null)    logPath    = "KeyboardRepeatFilter.log";
            if (outputPath == null) outputPath = "KeyboardHeatmap.html";

            // ── Validate input ─────────────────────────────────────────────────────
            if (!File.Exists(logPath))
            {
                Console.Error.WriteLine($"Error: log file not found: {Path.GetFullPath(logPath)}");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: KeyboardHeatmap.exe [logFile] [outputFile]");
                return 1;
            }

            Console.WriteLine($"Parsing:  {Path.GetFullPath(logPath)}");

            // ── Parse ──────────────────────────────────────────────────────────────
            List<LogEntry> entries;
            try
            {
                entries = LogParser.ParseFile(logPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error parsing log file: {ex.Message}");
                return 2;
            }

            // ── Optional day window (config: HeatmapDays) ──────────────────────────
            int? windowDays = ParseHeatmapDays(heatmapDaysRaw);
            if (windowDays.HasValue)
            {
                DateTime cutoff = DateTime.Now.AddDays(-windowDays.Value);
                int before = entries.Count;
                entries = entries.Where(e => e.Timestamp >= cutoff).ToList();
                Console.WriteLine($"Window:   last {windowDays.Value} day(s); kept {entries.Count} of {before} log entries");
            }

            int filteredCount = 0;
            foreach (var e in entries)
                if (e.Kind == LogEntryKind.Filtered) filteredCount++;

            Console.WriteLine($"Entries:  {entries.Count} total, {filteredCount} filtered key events");

            var configWarnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
                if (e.Kind == LogEntryKind.ConfigWarning && !string.IsNullOrWhiteSpace(e.Message))
                    configWarnings.Add(e.Message.Trim());

            if (configWarnings.Count > 0)
            {
                Console.WriteLine(configWarnings.Count == 1
                    ? "Warning:  1 config warning in log, check key names in config.json."
                    : $"Warning:  {configWarnings.Count} config warnings in log, check key names in config.json.");
            }
            if (!showDaily)
                Console.WriteLine("Tip:      Use -v to include the daily filtered event count in the output.");

            // ── Keyboard photo + key map (embedded in the exe) ─────────────────────
            KeyMap keyMap = null;
            byte[] photoPng = null;
            MouseMap mouseMap = null;
            byte[] mouseTopPng = null;
            byte[] mouseSidePng = null;
            if (classic)
            {
                Console.WriteLine("Layout:   classic HTML keyboard");
            }
            else
            {
                try
                {
                    string mapJson = ReadEmbeddedText("G915X.keymap.json");
                    photoPng = ReadEmbeddedBytes("G915X.png");
                    keyMap = KeyMap.Parse(mapJson, "embedded G915X.keymap.json");
                    Console.WriteLine($"Layout:   G915X keyboard photo ({keyMap.Keys.Count} mapped keys)");
                }
                catch (Exception ex)
                {
                    keyMap = null;
                    photoPng = null;
                    Console.WriteLine($"Warning:  could not load embedded keyboard photo ({ex.Message}), falling back to classic layout.");
                }
            }

            // ── Mouse photo + button map (only meaningful alongside the keyboard
            // photo; failure here just omits the mouse section, it does not fall
            // back to the classic SVG mouse, which is reserved for -classic). ────
            if (keyMap != null)
            {
                try
                {
                    string mouseJson = ReadEmbeddedText("G502X Plus.mousemap.json");
                    mouseTopPng = ReadEmbeddedBytes("G502X Plus top.png");
                    mouseSidePng = ReadEmbeddedBytes("G502X Plus side.png");
                    mouseMap = MouseMap.Parse(mouseJson, "embedded G502X Plus.mousemap.json");
                    Console.WriteLine("Mouse:    G502X Plus photo overlay enabled");
                }
                catch (Exception ex)
                {
                    mouseMap = null;
                    mouseTopPng = null;
                    mouseSidePng = null;
                    Console.WriteLine($"Warning:  could not load embedded mouse photo ({ex.Message}), mouse section will be omitted.");
                }
            }

            // ── Generate HTML ──────────────────────────────────────────────────────
            string html;
            try
            {
                html = HeatmapGenerator.Generate(entries, showDaily, keyMap, photoPng, mouseMap, mouseTopPng, mouseSidePng);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error generating heatmap: {ex.Message}");
                return 3;
            }

            // ── Write output ───────────────────────────────────────────────────────
            try
            {
                File.WriteAllText(outputPath, html, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error writing output file: {ex.Message}");
                return 4;
            }

            Console.WriteLine($"Output:   {Path.GetFullPath(outputPath)}");
            Console.WriteLine("Done. Open the HTML file in any browser.");

            // Optionally open the browser automatically on Windows
            TryOpenBrowser(outputPath);

            return 0;
        }

        // Finds an embedded resource by filename suffix so the lookup is immune to
        // namespace/folder renames changing the manifest name prefix.
        private static System.IO.Stream OpenEmbedded(string nameSuffix)
        {
            var asm = typeof(Program).Assembly;
            string resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(nameSuffix, StringComparison.OrdinalIgnoreCase));
            if (resName == null)
                throw new FileNotFoundException("Embedded resource not found: " + nameSuffix);
            return asm.GetManifestResourceStream(resName);
        }

        private static string ReadEmbeddedText(string nameSuffix)
        {
            using (var reader = new StreamReader(OpenEmbedded(nameSuffix)))
                return reader.ReadToEnd();
        }

        private static byte[] ReadEmbeddedBytes(string nameSuffix)
        {
            using (var stream = OpenEmbedded(nameSuffix))
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        // Returns the day window from the config value, or null for "all"/unset/
        // unrecognized (charts the entire log). Only positive integers limit it.
        private static int? ParseHeatmapDays(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            raw = raw.Trim();
            if (raw.Equals("all", StringComparison.OrdinalIgnoreCase))
                return null;

            if (int.TryParse(raw, out int days) && days > 0)
                return days;

            return null;
        }

        private static void TryOpenBrowser(string htmlPath)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName  = Path.GetFullPath(htmlPath),
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch
            {
                // Non-fatal, user can open manually
            }
        }
    }
}
