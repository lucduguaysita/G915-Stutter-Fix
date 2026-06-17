using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KeyboardHeatmap
{
    /// <summary>
    /// Turns a list of parsed log entries into a self-contained HTML heatmap page,
    /// matching the purple-ramp design from the interactive widget.
    /// </summary>
    public static class HeatmapGenerator
    {
        // Specific virtual-key codes drawn on the keyboard chrome (the modifier row
        // plus the two Shift keys) so they appear on the keyboard instead of the
        // special-key grid. Keyed by code because the log records the side-specific
        // VK (e.g. LCONTROL=162) next to the name.
        private const int VkSpace = 0x20, VkLWin = 0x5B, VkApps = 0x5D,
                          VkLShift = 0xA0, VkRShift = 0xA1, VkLCtrl = 0xA2, VkRCtrl = 0xA3,
                          VkLAlt = 0xA4, VkRAlt = 0xA5;

        private static readonly HashSet<int> ChromeVks = new HashSet<int>
        {
            VkSpace, VkLWin, VkApps, VkLShift, VkRShift, VkLCtrl, VkRCtrl, VkLAlt, VkRAlt
        };

        private static readonly string[] RowLabels = { "Row 1", "Row 2", "Row 3", "Row 4" };

        // Mouse filtered events are logged as "Mouse_<Button>"; this prefix routes
        // them to the dedicated mouse graphic instead of the special-key grid.
        private const string MousePrefix = "Mouse_";

        // Ember ramp — cool to hot (5 stops). Each stop is fill / text / border,
        // with the text colour chosen for contrast against its fill so key labels
        // stay readable at every intensity.
        // Light mode: pale amber -> amber -> orange -> red -> crimson
        private static readonly string[][] LightRamp = new[]
        {
            new[] { "#FFE7A8", "#6E3B0B", "#F3CB6E" },
            new[] { "#FFC04F", "#6E3B0B", "#F0A02E" },
            new[] { "#F8843C", "#431A04", "#D2641F" },
            new[] { "#E0431F", "#FFFFFF", "#B0300F" },
            new[] { "#A81457", "#FFFFFF", "#7E0E3F" }
        };

        // Dark mode: dim ember -> bright gold (glows brighter as the count rises)
        private static readonly string[][] DarkRamp = new[]
        {
            new[] { "#4A2E0E", "#F2CB80", "#6B4316" },
            new[] { "#8A4E14", "#FFDC93", "#A8651E" },
            new[] { "#E07B22", "#2A1505", "#B86018" },
            new[] { "#F59E2D", "#2A1505", "#C97E20" },
            new[] { "#FFD24D", "#3A2406", "#E0A82E" }
        };

        public static string Generate(List<LogEntry> entries, bool showDaily = false)
        {
            // ── Aggregate ──────────────────────────────────────────────────────────
            var filtered = entries.Where(e => e.Kind == LogEntryKind.Filtered).ToList();

            // Distinct config warnings (the same warning repeats on every startup).
            var configWarnings = entries
                .Where(e => e.Kind == LogEntryKind.ConfigWarning && !string.IsNullOrWhiteSpace(e.Message))
                .Select(e => e.Message.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var letterCounts  = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var specialCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var mouseCounts   = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var chromeCounts  = new Dictionary<int, int>(); // modifier row + Shift keys, by VK code

            foreach (var e in filtered)
            {
                if (e.KeyName == null) continue;

                if (e.KeyName.StartsWith(MousePrefix, StringComparison.OrdinalIgnoreCase))
                    Increment(mouseCounts, e.KeyName.Substring(MousePrefix.Length));
                // Modifier/shift/space keys drawn on the keyboard chrome (matched by
                // side-specific VK code), kept out of the special-key grid.
                else if (e.KeyCode.HasValue && ChromeVks.Contains(e.KeyCode.Value))
                    Increment(chromeCounts, e.KeyCode.Value);
                // Single letter = letter key; anything else = special key
                else if (e.KeyName.Length == 1 && char.IsLetter(e.KeyName[0]))
                    Increment(letterCounts, e.KeyName.ToUpperInvariant());
                else
                    Increment(specialCounts, e.KeyName);
            }

            int maxMouseCount = mouseCounts.Count > 0 ? mouseCounts.Values.Max() : 1;

            int totalFiltered = filtered.Count;
            int maxLetterCount = letterCounts.Count > 0 ? letterCounts.Values.Max() : 1;
            int maxChromeCount = chromeCounts.Count > 0 ? chromeCounts.Values.Max() : 0;
            // One shared intensity scale for the whole keyboard (letters + chrome).
            int maxKeyboardCount = Math.Max(1, Math.Max(maxLetterCount, maxChromeCount));

            // Build the key layout and per-row totals so we can flag whichever row
            // captured the most events (nothing is flagged when there are none).
            var kbRows = BuildKeyboardRows();
            int CountFor(KbKey k) =>
                k.Letter != null ? (letterCounts.TryGetValue(k.Letter, out int lc) ? lc : 0)
              : k.Vk >= 0        ? (chromeCounts.TryGetValue(k.Vk, out int cc) ? cc : 0)
              : 0;

            var rowTotals = new int[kbRows.Length];
            for (int ri = 0; ri < kbRows.Length; ri++)
                foreach (var k in kbRows[ri]) rowTotals[ri] += CountFor(k);
            int maxRowTotal = rowTotals.Max();

            string topKey = letterCounts.Count > 0
                ? letterCounts.OrderByDescending(kv => kv.Value).First().Key
                : "—";
            int topKeyCount = letterCounts.Count > 0
                ? letterCounts.OrderByDescending(kv => kv.Value).First().Value
                : 0;

            string dateFrom = filtered.Count > 0
                ? filtered.Min(e => e.Timestamp).ToString("MMM d, yyyy")
                : "—";
            string dateTo = filtered.Count > 0
                ? filtered.Max(e => e.Timestamp).ToString("MMM d, yyyy")
                : "—";

            int uniqueKeys = letterCounts.Count + specialCounts.Count + mouseCounts.Count + chromeCounts.Count;

            // ── Build HTML ─────────────────────────────────────────────────────────
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset=\"UTF-8\">");
            sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine("<title>Keyboard Repeat Filter — Heatmap</title>");
            sb.AppendLine("<style>");
            sb.AppendLine(GetCss());
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<div class=\"page\">");

            // Header
            sb.AppendLine("<h1>Keyboard Repeat Filter — Heatmap</h1>");
            sb.AppendLine($"<p class=\"subtitle\">Double-typed key events intercepted by KeyboardRepeatFilter &nbsp;|&nbsp; {dateFrom} – {dateTo}</p>");

            // Config warning banner
            if (configWarnings.Count > 0)
            {
                string heading = configWarnings.Count == 1
                    ? "⚠ 1 config warning in log"
                    : $"⚠ {configWarnings.Count} config warnings in log";

                sb.AppendLine("<div class=\"config-warn\">");
                sb.AppendLine($"  <strong>{heading}</strong>");
                sb.AppendLine("  <p>Check the key names in <code>config.json</code> — unrecognized entries are ignored.</p>");
                sb.AppendLine("  <ul>");
                foreach (var msg in configWarnings)
                {
                    sb.AppendLine($"    <li>{EscapeHtml(msg)}</li>");
                }
                sb.AppendLine("  </ul>");
                sb.AppendLine("</div>");
            }

            // Stat cards
            sb.AppendLine("<div class=\"stats\">");
            AppendStat(sb, totalFiltered.ToString(), "Total filtered events");
            AppendStat(sb, topKeyCount > 0 ? $"{topKey} ({topKeyCount}×)" : "—", "Most filtered key");
            AppendStat(sb, uniqueKeys.ToString(), "Unique keys affected");
            AppendStat(sb, $"{dateTo}", "Last event");
            sb.AppendLine("</div>");

            // Legend
            sb.AppendLine("<p class=\"section-label\">Keys — color intensity = filter count &nbsp;|&nbsp; row labels show PCB matrix row</p>");
            sb.AppendLine("<div class=\"legend\">");
            sb.AppendLine("<span class=\"legend-text\">0</span>");
            sb.AppendLine("<div class=\"legend-bar\">");
            for (int i = 0; i < 5; i++)
            {
                string fill = LightRamp[i][0];
                sb.AppendLine($"<span style=\"background:{fill}\"></span>");
            }
            sb.AppendLine("</div>");
            sb.AppendLine($"<span class=\"legend-text\">{maxKeyboardCount}</span>");
            sb.AppendLine("<span class=\"legend-unit\">filtered events</span>");
            sb.AppendLine("</div>");

            // Keyboard rows
            sb.AppendLine("<div class=\"keyboard\">");
            int[] offsets = { 0, 12, 0, 0 }; // px left-padding per row (3 & 4 anchor left)

            for (int ri = 0; ri < kbRows.Length; ri++)
            {
                bool isWarningRow = rowTotals[ri] > 0 && rowTotals[ri] == maxRowTotal;
                string rowClass = isWarningRow ? "kb-row-wrap warning-row" : "kb-row-wrap";
                string badgeClass = isWarningRow ? "row-badge warning-badge" : "row-badge";
                string label = isWarningRow ? RowLabels[ri] + " ⚠" : RowLabels[ri];
                string badgeTitle = isWarningRow ? " title=\"This row has the most filtered events\"" : "";

                sb.AppendLine($"<div class=\"{rowClass}\">");
                sb.AppendLine($"<span class=\"{badgeClass}\"{badgeTitle}>{label}</span>");
                sb.AppendLine($"<div class=\"kb-row\" style=\"padding-left:{offsets[ri]}px\">");

                foreach (var k in kbRows[ri])
                {
                    int count = CountFor(k);
                    string[] colors = GetColors(count, maxKeyboardCount, dark: false);
                    string tooltip = $"{k.Tip}: {count} filtered event{(count != 1 ? "s" : "")}";

                    sb.AppendLine($"<div class=\"key\" title=\"{EscapeHtml(tooltip)}\" data-count=\"{count}\" " +
                                  $"style=\"width:{k.Width}px;background:{colors[0]};border-color:{colors[2]};color:{colors[1]}\">");
                    sb.AppendLine($"  <span class=\"klabel\">{EscapeHtml(k.Label)}</span>");
                    if (count > 0)
                        sb.AppendLine($"  <span class=\"kcount\">{count}</span>");
                    sb.AppendLine("</div>");
                }

                sb.AppendLine("</div>"); // kb-row
                sb.AppendLine("</div>"); // kb-row-wrap
            }

            sb.AppendLine("</div>"); // keyboard

            // Special keys
            if (specialCounts.Count > 0)
            {
                sb.AppendLine("<p class=\"section-label\">Special &amp; navigation keys</p>");
                sb.AppendLine("<div class=\"special-grid\">");

                int maxSpecial = specialCounts.Values.Max();
                foreach (var kv in specialCounts.OrderByDescending(x => x.Value))
                {
                    // Scale special keys into the lower portion of the ramp
                    int scaledCount = (int)Math.Round((double)kv.Value / maxSpecial * maxLetterCount * 0.25);
                    string[] colors = GetColors(scaledCount, maxLetterCount, dark: false);
                    string tooltip = $"{kv.Key}: {kv.Value} filtered event{(kv.Value != 1 ? "s" : "")}";

                    sb.AppendLine($"<div class=\"skey\" title=\"{tooltip}\" " +
                                  $"style=\"background:{colors[0]};border-color:{colors[2]};color:{colors[1]}\">");
                    sb.AppendLine($"  <span class=\"skname\">{EscapeHtml(kv.Key)}</span>");
                    sb.AppendLine($"  <span class=\"skcount\">{kv.Value}×</span>");
                    sb.AppendLine("</div>");
                }

                sb.AppendLine("</div>"); // special-grid
            }

            // Mouse buttons (only when the mouse filter has caught anything)
            if (mouseCounts.Count > 0)
            {
                sb.AppendLine("<p class=\"section-label\">Mouse buttons — color intensity = filter count</p>");
                AppendMouse(sb, mouseCounts, maxMouseCount);
            }

            // Per-day chart data table (opt-in via -v flag)
            if (showDaily)
            {
                sb.AppendLine("<p class=\"section-label\">Daily filtered event count</p>");
                AppendDailyTable(sb, filtered);
            }

            sb.AppendLine("</div>"); // page
            sb.AppendLine("<script>");
            sb.AppendLine(GetDarkModeScript(letterCounts, specialCounts, maxLetterCount, maxMouseCount, maxKeyboardCount));
            sb.AppendLine("</script>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private static void Increment(Dictionary<string, int> dict, string key)
        {
            if (dict.ContainsKey(key)) dict[key]++;
            else dict[key] = 1;
        }

        private static void Increment(Dictionary<int, int> dict, int key)
        {
            if (dict.ContainsKey(key)) dict[key]++;
            else dict[key] = 1;
        }

        // One key on the rendered keyboard. A letter key looks up its count by
        // letter name; a "chrome" key (modifier/Shift/Space) looks up by VK code;
        // Fn has neither (no VK is generated) and always shows zero.
        private sealed class KbKey
        {
            public string Label;   // text on the cap
            public string Tip;     // tooltip name (disambiguates left/right)
            public string Letter;  // letter-count key, or null
            public int Vk;         // chrome VK code, or -1
            public int Width;      // px

            public KbKey(string label, string letter, int vk, int width, string tip = null)
            {
                Label = label; Letter = letter; Vk = vk; Width = width; Tip = tip ?? label;
            }
        }

        // The four rendered rows: three QWERTY letter rows (Shift keys bracketing
        // row 3) and a bottom modifier row.
        private static KbKey[][] BuildKeyboardRows()
        {
            KbKey Letter(string s) => new KbKey(s, s, -1, 52);
            KbKey Chrome(string label, int vk, int width, string tip) => new KbKey(label, null, vk, width, tip);

            return new[]
            {
                new[] { Letter("Q"), Letter("W"), Letter("E"), Letter("R"), Letter("T"),
                        Letter("Y"), Letter("U"), Letter("I"), Letter("O"), Letter("P") },
                new[] { Letter("A"), Letter("S"), Letter("D"), Letter("F"), Letter("G"),
                        Letter("H"), Letter("J"), Letter("K"), Letter("L") },
                new[] { Chrome("Shift", VkLShift, 86, "Left Shift"),
                        Letter("Z"), Letter("X"), Letter("C"), Letter("V"), Letter("B"), Letter("N"), Letter("M"),
                        Chrome("Shift", VkRShift, 86, "Right Shift") },
                new[] { Chrome("Ctrl", VkLCtrl, 60, "Left Ctrl"),
                        Chrome("Win", VkLWin, 54, "Windows"),
                        Chrome("Alt", VkLAlt, 54, "Left Alt"),
                        Chrome("Space", VkSpace, 196, "Space"),
                        Chrome("Alt", VkRAlt, 54, "Right Alt"),
                        Chrome("Fn", -1, 48, "Fn (no key event)"),
                        Chrome("Menu", VkApps, 54, "Menu (Apps)"),
                        Chrome("Ctrl", VkRCtrl, 60, "Right Ctrl") }
            };
        }

        /// <summary>Returns [fill, text, border] for the given count relative to max.</summary>
        private static string[] GetColors(int count, int max, bool dark)
        {
            if (count == 0)
                return dark
                    ? new[] { "#1e1e2e", "#555577", "#333355" }
                    : new[] { "#F5F5F8", "#AAAACC", "#DDDDEE" };

            int idx = Math.Min((int)Math.Floor((double)count / max * 5), 4);
            return dark ? DarkRamp[idx] : LightRamp[idx];
        }

        // Renders a simple, stylish top-down mouse SVG with each button tinted by
        // its filter count and the count printed on it. Light-mode colours are
        // inline; the dark-mode script repaints the .mbtn groups to match the keys.
        private static void AppendMouse(StringBuilder sb, Dictionary<string, int> mouseCounts, int max)
        {
            int Count(string name) => mouseCounts.TryGetValue(name, out int c) ? c : 0;
            int cLeft = Count("Left"), cRight = Count("Right"), cMid = Count("Middle"),
                cX1 = Count("X1"), cX2 = Count("X2");

            sb.AppendLine("<div class=\"mouse-wrap\">");
            sb.AppendLine("<svg class=\"mouse-svg\" viewBox=\"0 0 200 300\" width=\"176\" height=\"264\" " +
                          "role=\"img\" aria-label=\"Mouse button filter counts\">");

            // Body silhouette (neutral; adapts to theme via CSS).
            sb.AppendLine("<path class=\"mouse-body\" d=\"M100,16 C61,16 36,46 36,96 L36,206 " +
                          "C36,250 62,282 100,282 C138,282 164,250 164,206 L164,96 C164,46 139,16 100,16 Z\" />");

            // Left and right primary buttons.
            AppendMouseRegion(sb, $"Left button: {cLeft} filtered event{Plural(cLeft)}", cLeft, max,
                "<path d=\"M100,20 C64,20 40,48 40,95 L40,118 L100,118 Z\" style=\"fill:{F};stroke:{B};stroke-width:1.5\" />",
                new[] { ("mcount", 68, 74, cLeft.ToString()), ("mlabel", 68, 96, "L") });

            AppendMouseRegion(sb, $"Right button: {cRight} filtered event{Plural(cRight)}", cRight, max,
                "<path d=\"M100,20 C136,20 160,48 160,95 L160,118 L100,118 Z\" style=\"fill:{F};stroke:{B};stroke-width:1.5\" />",
                new[] { ("mcount", 132, 74, cRight.ToString()), ("mlabel", 132, 96, "R") });

            // Seam between the buttons, drawn over them but under the wheel.
            sb.AppendLine("<line class=\"mouse-seam\" x1=\"100\" y1=\"24\" x2=\"100\" y2=\"116\" />");

            // Scroll wheel = middle button.
            AppendMouseRegion(sb, $"Middle button: {cMid} filtered event{Plural(cMid)}", cMid, max,
                "<rect x=\"91\" y=\"34\" width=\"18\" height=\"44\" rx=\"9\" style=\"fill:{F};stroke:{B};stroke-width:1.5\" />",
                new[] { ("mcount-sm", 100, 56, cMid.ToString()) });

            // Side (thumb) buttons.
            AppendMouseRegion(sb, $"X1 (back): {cX1} filtered event{Plural(cX1)}", cX1, max,
                "<rect x=\"22\" y=\"136\" width=\"24\" height=\"22\" rx=\"5\" style=\"fill:{F};stroke:{B};stroke-width:1.5\" />",
                new[] { ("mcount-sm", 34, 147, cX1.ToString()) });
            sb.AppendLine("<text class=\"mside-label\" x=\"52\" y=\"147\">X1</text>");

            AppendMouseRegion(sb, $"X2 (forward): {cX2} filtered event{Plural(cX2)}", cX2, max,
                "<rect x=\"22\" y=\"162\" width=\"24\" height=\"22\" rx=\"5\" style=\"fill:{F};stroke:{B};stroke-width:1.5\" />",
                new[] { ("mcount-sm", 34, 173, cX2.ToString()) });
            sb.AppendLine("<text class=\"mside-label\" x=\"52\" y=\"173\">X2</text>");

            sb.AppendLine("</svg>");
            sb.AppendLine("</div>"); // mouse-wrap
        }

        // Emits one <g class="mbtn"> region: a tinted shape plus its text labels.
        // The shape template uses {F} (fill) and {B} (border); texts are
        // (cssClass, x, y, content) and all share the ramp's text colour.
        private static void AppendMouseRegion(StringBuilder sb, string tooltip, int count, int max,
            string shapeTemplate, (string cls, int x, int y, string text)[] texts)
        {
            string[] colors = GetColors(count, max, dark: false);
            string shape = shapeTemplate.Replace("{F}", colors[0]).Replace("{B}", colors[2]);

            sb.AppendLine($"<g class=\"mbtn\" data-count=\"{count}\">");
            sb.AppendLine($"  <title>{EscapeHtml(tooltip)}</title>");
            sb.AppendLine("  " + shape);
            foreach (var t in texts)
            {
                sb.AppendLine($"  <text class=\"{t.cls}\" x=\"{t.x}\" y=\"{t.y}\" style=\"fill:{colors[1]}\">{EscapeHtml(t.text)}</text>");
            }
            sb.AppendLine("</g>");
        }

        private static string Plural(int n) => n != 1 ? "s" : "";

        private static void AppendStat(StringBuilder sb, string value, string label)
        {
            sb.AppendLine("<div class=\"stat\">");
            sb.AppendLine($"  <div class=\"stat-val\">{EscapeHtml(value)}</div>");
            sb.AppendLine($"  <div class=\"stat-lbl\">{EscapeHtml(label)}</div>");
            sb.AppendLine("</div>");
        }

        private static void AppendDailyTable(StringBuilder sb, List<LogEntry> filtered)
        {
            var byDay = filtered
                .GroupBy(e => e.Timestamp.Date)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.Count());

            if (byDay.Count == 0) return;

            int dayMax = byDay.Values.Max();

            sb.AppendLine("<div class=\"daily-table\">");
            foreach (var kv in byDay)
            {
                // Severity is relative to the busiest day in the log: a quiet day
                // stays green, a middling day reaches yellow, the worst day(s) run
                // to crimson. The bar's length and colour share the same ratio.
                double ratio = dayMax > 0 ? (double)kv.Value / dayMax : 0;
                double pct = ratio * 100;
                string gradient = DayBarGradient(ratio);
                sb.AppendLine("<div class=\"day-row\">");
                sb.AppendLine($"  <span class=\"day-label\">{kv.Key:MMM d}</span>");
                sb.AppendLine($"  <div class=\"day-bar-wrap\">");
                sb.AppendLine($"    <div class=\"day-bar\" style=\"width:{pct:F1}%;background:{gradient}\"></div>");
                sb.AppendLine($"  </div>");
                sb.AppendLine($"  <span class=\"day-count\">{kv.Value}</span>");
                sb.AppendLine("</div>");
            }
            sb.AppendLine("</div>");
        }

        // Builds a green -> (yellow) -> hot gradient for a daily bar. The hot end
        // is the severity colour for this day's ratio (0 = green, 0.5 = yellow,
        // 1 = crimson); when the day is in the upper half the gradient passes
        // through yellow at the right spot so it never muddies to brown.
        private static string DayBarGradient(double ratio)
        {
            ratio = Math.Max(0, Math.Min(1, ratio));
            const string green = "#2EA84F";
            const string yellow = "#F2C200";
            string end = SeverityColor(ratio);

            if (ratio > 0.5)
            {
                double yellowPos = 0.5 / ratio * 100.0; // yellow's spot within this bar
                return $"linear-gradient(90deg,{green} 0%,{yellow} {yellowPos:F0}%,{end} 100%)";
            }

            return $"linear-gradient(90deg,{green} 0%,{end} 100%)";
        }

        // Maps a 0..1 ratio onto a green -> yellow -> crimson scale.
        private static string SeverityColor(double ratio)
        {
            ratio = Math.Max(0, Math.Min(1, ratio));
            int[] green   = { 46, 168, 79 };   // #2EA84F
            int[] yellow  = { 242, 194, 0 };   // #F2C200
            int[] crimson = { 200, 24, 73 };   // #C81849

            int[] a, b;
            double t;
            if (ratio < 0.5) { a = green;  b = yellow;  t = ratio / 0.5; }
            else             { a = yellow; b = crimson; t = (ratio - 0.5) / 0.5; }

            int r  = (int)Math.Round(a[0] + (b[0] - a[0]) * t);
            int g  = (int)Math.Round(a[1] + (b[1] - a[1]) * t);
            int bl = (int)Math.Round(a[2] + (b[2] - a[2]) * t);
            return $"#{r:X2}{g:X2}{bl:X2}";
        }

        private static string EscapeHtml(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        // ── CSS ────────────────────────────────────────────────────────────────────

        private static string GetCss()
        {
            return @"
*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }

body {
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
    font-size: 14px;
    background: #f7f7fa;
    color: #1a1a2e;
    padding: 2rem;
}

.page {
    max-width: 860px;
    margin: 0 auto;
}

h1 {
    font-size: 22px;
    font-weight: 500;
    margin-bottom: 4px;
    color: #1a1a2e;
}

.subtitle {
    font-size: 13px;
    color: #666688;
    margin-bottom: 1.5rem;
}

/* ── Stat cards ── */
.stats {
    display: grid;
    grid-template-columns: repeat(4, 1fr);
    gap: 10px;
    margin-bottom: 1.5rem;
}

.stat {
    background: #ffffff;
    border: 0.5px solid #ddddef;
    border-radius: 8px;
    padding: 0.75rem 1rem;
}

.stat-val {
    font-size: 20px;
    font-weight: 500;
    color: #1a1a2e;
}

.stat-lbl {
    font-size: 11px;
    color: #666688;
    margin-top: 2px;
}

/* ── Config warning banner ── */
.config-warn {
    background: rgba(186,117,23,0.08);
    border: 0.5px solid #BA7517;
    border-left: 3px solid #BA7517;
    border-radius: 6px;
    padding: 0.75rem 1rem;
    margin-bottom: 1.5rem;
    color: #854F0B;
}

.config-warn strong { font-size: 13px; }
.config-warn p { font-size: 12px; margin-top: 2px; color: #7a6a52; }
.config-warn code { font-family: 'Consolas', 'Courier New', monospace; font-size: 11px; }
.config-warn ul { margin: 6px 0 0 1.1rem; }
.config-warn li { font-size: 12px; margin-top: 2px; }

/* ── Section labels ── */
.section-label {
    font-size: 12px;
    color: #666688;
    margin: 1.2rem 0 6px;
}

/* ── Legend ── */
.legend {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-bottom: 0.5rem;
    font-size: 12px;
    color: #666688;
}

.legend-text { min-width: 12px; }

.legend-bar {
    display: flex;
    height: 10px;
    width: 140px;
    border-radius: 3px;
    overflow: hidden;
}

.legend-bar span { flex: 1; }

.legend-unit { color: #999ab0; }

/* ── Keyboard ── */
.keyboard {
    display: flex;
    flex-direction: column;
    gap: 4px;
    margin-bottom: 0.5rem;
}

.kb-row-wrap {
    display: flex;
    align-items: center;
    gap: 8px;
}

.row-badge {
    font-size: 10px;
    color: #999ab0;
    width: 52px;
    text-align: right;
    flex-shrink: 0;
    white-space: nowrap;
}

.warning-badge { color: #854F0B; }

.warning-row {
    border-left: 3px solid #BA7517;
    background: rgba(186,117,23,0.04);
    border-radius: 0;
    padding: 3px 0 3px 6px;
    margin-left: -6px;
}

.kb-row {
    display: flex;
    gap: 4px;
}

.key {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    width: 52px;
    min-height: 44px;
    border-radius: 7px;
    border: 0.5px solid #ddddef;
    cursor: default;
    flex-shrink: 0;
    transition: transform 0.12s, box-shadow 0.12s;
    background: #f0f0f8;
    color: #aaaacc;
    box-shadow: 0 1px 2px rgba(120,60,10,0.06);
}

.key:hover { transform: scale(1.13); box-shadow: 0 3px 8px rgba(180,80,20,0.22); }

.klabel { font-size: 11px; font-weight: 500; line-height: 1; }
.kcount { font-size: 10px; margin-top: 2px; opacity: 0.9; }

/* ── Special keys ── */
.special-grid {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
}

.skey {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    padding: 6px 10px;
    min-width: 84px;
    min-height: 40px;
    border-radius: 6px;
    border: 0.5px solid #ddddef;
    cursor: default;
    background: #f0f0f8;
    color: #aaaacc;
    transition: transform 0.12s;
}

.skey:hover { transform: scale(1.07); }

.skname { font-size: 10px; font-weight: 500; }
.skcount { font-size: 10px; margin-top: 2px; }

/* ── Mouse graphic ── */
.mouse-wrap { display: flex; justify-content: center; margin: 0.25rem 0 0.75rem; }
.mouse-body { fill: #f0f0f8; stroke: #d0d0e4; stroke-width: 2; }
.mouse-seam { stroke: #d0d0e4; stroke-width: 2; }
.mbtn { cursor: default; }
.mbtn path, .mbtn rect { transition: fill 0.12s; }
.mcount { font-size: 16px; font-weight: 600; text-anchor: middle; dominant-baseline: middle; }
.mcount-sm { font-size: 9px; font-weight: 600; text-anchor: middle; dominant-baseline: middle; }
.mlabel { font-size: 9px; text-anchor: middle; dominant-baseline: middle; opacity: 0.75; }
.mside-label { font-size: 9px; fill: #999ab0; dominant-baseline: middle; }

/* ── Daily table ── */
.daily-table {
    display: flex;
    flex-direction: column;
    gap: 4px;
    margin-top: 4px;
}

.day-row {
    display: flex;
    align-items: center;
    gap: 8px;
}

.day-label {
    font-size: 11px;
    color: #666688;
    width: 44px;
    flex-shrink: 0;
    text-align: right;
}

.day-bar-wrap {
    flex: 1;
    background: #ebebf4;
    border-radius: 3px;
    height: 12px;
    overflow: hidden;
}

.day-bar {
    height: 100%;
    background: #2EA84F; /* fallback; actual green->hot gradient is set inline per day */
    border-radius: 3px;
    transition: width 0.3s ease;
}

.day-count {
    font-size: 11px;
    color: #666688;
    width: 28px;
    text-align: right;
}

/* ── Dark mode ── */
@media (prefers-color-scheme: dark) {
    body { background: #13131f; color: #e0e0f0; }
    h1 { color: #e0e0f0; }
    .subtitle { color: #8888aa; }
    .stat { background: #1e1e2e; border-color: #2e2e4e; }
    .stat-val { color: #e0e0f0; }
    .stat-lbl { color: #8888aa; }
    .section-label { color: #8888aa; }
    .legend { color: #8888aa; }
    .legend-unit { color: #555577; }
    .row-badge { color: #555577; }
    .warning-badge { color: #FAC775; }
    .warning-row { border-left-color: #FAC775; background: rgba(250,199,117,0.06); }
    .config-warn { background: rgba(250,199,117,0.08); border-color: #BA7517; color: #FAC775; }
    .config-warn p { color: #c9b48f; }
    .key { background: #1a1a2e; border-color: #2e2e4e; color: #555577; }
    .skey { background: #1a1a2e; border-color: #2e2e4e; color: #555577; }
    .day-bar-wrap { background: #2a2a3e; }
    .day-label { color: #8888aa; }
    .day-count { color: #8888aa; }
    .mouse-body { fill: #1a1a2e; stroke: #2e2e4e; }
    .mouse-seam { stroke: #2e2e4e; }
    .mside-label { fill: #555577; }
}

@media (max-width: 600px) {
    .stats { grid-template-columns: repeat(2, 1fr); }
    body { padding: 1rem; }
}
";
        }

        // ── Dark-mode JS color patch ───────────────────────────────────────────────
        // Inline CSS @media handles layout; this patches the inline key colors.

        private static string GetDarkModeScript(
            Dictionary<string, int> letterCounts,
            Dictionary<string, int> specialCounts,
            int maxLetterCount,
            int maxMouseCount,
            int maxKeyboardCount)
        {
            // Build JS arrays for dark-mode ramp application
            var sb = new StringBuilder();
            sb.AppendLine("(function() {");
            sb.AppendLine("  var dark = window.matchMedia('(prefers-color-scheme: dark)').matches;");
            sb.AppendLine("  if (!dark) return;");

            sb.AppendLine("  var lightRamp = [");
            foreach (var stop in LightRamp)
                sb.AppendLine($"    ['{stop[0]}','{stop[1]}','{stop[2]}'],");
            sb.AppendLine("  ];");

            sb.AppendLine("  var darkRamp = [");
            foreach (var stop in DarkRamp)
                sb.AppendLine($"    ['{stop[0]}','{stop[1]}','{stop[2]}'],");
            sb.AppendLine("  ];");

            // The legend bar is rendered server-side with the light ramp; repaint
            // its stops with the dark ramp so the scale matches the recoloured keys.
            sb.AppendLine("  var legendStops = document.querySelectorAll('.legend-bar span');");
            sb.AppendLine("  for (var li = 0; li < legendStops.length && li < darkRamp.length; li++) {");
            sb.AppendLine("    legendStops[li].style.background = darkRamp[li][0];");
            sb.AppendLine("  }");

            sb.AppendLine($"  var maxCount = {maxLetterCount};");

            sb.AppendLine("  function applyColors(el, count, maxC) {");
            sb.AppendLine("    if (count === 0) {");
            sb.AppendLine("      el.style.background = '#1a1a2e';");
            sb.AppendLine("      el.style.borderColor = '#2e2e4e';");
            sb.AppendLine("      el.style.color = '#555577';");
            sb.AppendLine("      return;");
            sb.AppendLine("    }");
            sb.AppendLine("    var idx = Math.min(Math.floor(count / maxC * 5), 4);");
            sb.AppendLine("    var r = darkRamp[idx];");
            sb.AppendLine("    el.style.background = r[0];");
            sb.AppendLine("    el.style.color = r[1];");
            sb.AppendLine("    el.style.borderColor = r[2];");
            sb.AppendLine("    var spans = el.querySelectorAll('span');");
            sb.AppendLine("    for (var i = 0; i < spans.length; i++) spans[i].style.color = r[1];");
            sb.AppendLine("  }");

            // Keyboard keys carry their count in data-count, so a single pass
            // repaints letters and chrome (modifier/Shift/Space) keys alike.
            sb.AppendLine($"  var maxKb = {Math.Max(1, maxKeyboardCount)};");
            sb.AppendLine("  var keys = document.querySelectorAll('.key');");
            sb.AppendLine("  keys.forEach(function(el) {");
            sb.AppendLine("    var c = parseInt(el.getAttribute('data-count'), 10) || 0;");
            sb.AppendLine("    applyColors(el, c, maxKb);");
            sb.AppendLine("  });");

            // Special key counts
            int maxSpecial = specialCounts.Count > 0 ? specialCounts.Values.Max() : 1;
            sb.Append("  var sc = {");
            foreach (var kv in specialCounts)
                sb.Append($"'{kv.Key}':{kv.Value},");
            sb.AppendLine("};");

            sb.AppendLine($"  var maxSp = {maxSpecial};");
            sb.AppendLine("  var skeys = document.querySelectorAll('.skey');");
            sb.AppendLine("  skeys.forEach(function(el) {");
            sb.AppendLine("    var label = el.querySelector('.skname');");
            sb.AppendLine("    if (!label) return;");
            sb.AppendLine("    var k = label.textContent.trim();");
            sb.AppendLine("    var cnt = sc[k] || 0;");
            sb.AppendLine($"    var scaled = Math.round(cnt / maxSp * {maxLetterCount} * 0.25);");
            sb.AppendLine("    applyColors(el, scaled, maxCount);");
            sb.AppendLine("  });");

            // Mouse button groups: repaint the shape + its text(s) with the dark ramp.
            sb.AppendLine($"  var maxMouse = {Math.Max(1, maxMouseCount)};");
            sb.AppendLine("  var mbtns = document.querySelectorAll('.mbtn');");
            sb.AppendLine("  mbtns.forEach(function(g) {");
            sb.AppendLine("    var c = parseInt(g.getAttribute('data-count'), 10) || 0;");
            sb.AppendLine("    var shape = g.querySelector('path, rect');");
            sb.AppendLine("    var fill, stroke, tcol;");
            sb.AppendLine("    if (c === 0) { fill = '#1a1a2e'; stroke = '#2e2e4e'; tcol = '#555577'; }");
            sb.AppendLine("    else { var idx = Math.min(Math.floor(c / maxMouse * 5), 4); var r = darkRamp[idx]; fill = r[0]; stroke = r[2]; tcol = r[1]; }");
            sb.AppendLine("    if (shape) { shape.style.fill = fill; shape.style.stroke = stroke; }");
            sb.AppendLine("    g.querySelectorAll('text').forEach(function(t) { t.style.fill = tcol; });");
            sb.AppendLine("  });");

            sb.AppendLine("})();");
            return sb.ToString();
        }
    }
}
