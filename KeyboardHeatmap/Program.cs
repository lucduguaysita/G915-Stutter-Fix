using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace KeyboardHeatmap
{
    /// <summary>
    /// KeyboardHeatmap — parses a KeyboardRepeatFilter log and produces an HTML heatmap.
    ///
    /// Usage:
    ///   KeyboardHeatmap.exe [logFile] [outputFile]
    ///
    ///   logFile    — path to KeyboardRepeatFilter.log  (default: KeyboardRepeatFilter.log in current dir)
    ///   outputFile — path for the generated HTML file   (default: KeyboardHeatmap.html in current dir)
    ///
    /// If a config.json exists in the current directory it is read for defaults:
    ///   { "LogFilePath": "C:/Temp/KeyboardRepeatFilter.log" }
    ///   The output file is placed in the same directory as the log file.
    ///
    /// Flags:
    ///   -v | -V | --v | --V   Include the "Daily filtered event count" section in the output.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // ── Strip flags from args ──────────────────────────────────────────────
            bool showDaily = false;
            var positional = new System.Collections.Generic.List<string>();
            foreach (string arg in args)
            {
                if (arg == "-v" || arg == "-V" || arg == "--v" || arg == "--V")
                    showDaily = true;
                else
                    positional.Add(arg);
            }

            string logPath    = positional.Count >= 1 ? positional[0] : null;
            string outputPath = positional.Count >= 2 ? positional[1] : null;

            // ── Read config.json for defaults ──────────────────────────────────────
            string configPath = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
            if (File.Exists(configPath))
            {
                string configText = File.ReadAllText(configPath);
                // Simple regex-based read — no external JSON dependency required
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

            int filteredCount = 0;
            foreach (var e in entries)
                if (e.Kind == LogEntryKind.Filtered) filteredCount++;

            Console.WriteLine($"Entries:  {entries.Count} total, {filteredCount} filtered key events");
            if (!showDaily)
                Console.WriteLine("Tip:      Use -v to include the daily filtered event count in the output.");

            // ── Generate HTML ──────────────────────────────────────────────────────
            string html;
            try
            {
                html = HeatmapGenerator.Generate(entries, showDaily);
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
                // Non-fatal — user can open manually
            }
        }
    }
}
