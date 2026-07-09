using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace KeyboardHeatmap
{
    /// <summary>One key's hit box on the keyboard photo, in image pixels.</summary>
    public sealed class KeyBox
    {
        public string Name { get; }
        public int? Vk { get; }        // Windows virtual-key code; null for G-keys / Fn
        public int X { get; }          // top-left of the keycap face
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public int LabelX { get; }     // where count text belongs (center of the painted box)
        public int LabelY { get; }

        public KeyBox(string name, int? vk, int x, int y, int width, int height,
                      int labelX = 0, int labelY = 0)
        {
            Name = name; Vk = vk; X = x; Y = y; Width = width; Height = height;
            LabelX = labelX > 0 ? labelX : x + width / 2;
            LabelY = labelY > 0 ? labelY : y + height / 2;
        }
    }

    /// <summary>
    /// Loads a *.keymap.json produced for a keyboard photo (e.g. Assets/G915X.keymap.json)
    /// and scales its boxes to whatever size the photo is displayed at.
    /// Coordinates in the file are always in original image pixels; call Scale/ScaleToWidth
    /// to get coordinates for a zoomed view.
    /// </summary>
    public sealed class KeyMap
    {
        public string Image { get; private set; }
        public int ImageWidth { get; private set; }
        public int ImageHeight { get; private set; }
        public IReadOnlyList<KeyBox> Keys { get { return _keys; } }

        private List<KeyBox> _keys = new List<KeyBox>();
        private Dictionary<int, KeyBox> _byVk = new Dictionary<int, KeyBox>();

        /// <summary>Box for a virtual-key code, or null if the key is not on the map.</summary>
        public KeyBox FindByVk(int vk)
        {
            KeyBox k;
            return _byVk.TryGetValue(vk, out k) ? k : null;
        }

        /// <summary>
        /// Returns a copy with every coordinate multiplied by <paramref name="zoom"/>
        /// (1.0 = original size, 0.5 = zoomed out to half, 2.0 = zoomed in to double).
        /// </summary>
        public KeyMap Scale(double zoom)
        {
            if (zoom <= 0) throw new ArgumentOutOfRangeException("zoom", "zoom must be positive");

            var scaled = new KeyMap
            {
                Image = Image,
                ImageWidth = (int)Math.Round(ImageWidth * zoom),
                ImageHeight = (int)Math.Round(ImageHeight * zoom)
            };
            foreach (var k in _keys)
            {
                // Round edges, not width/height, so adjacent boxes stay aligned at any zoom.
                int x0 = (int)Math.Round(k.X * zoom);
                int y0 = (int)Math.Round(k.Y * zoom);
                int x1 = (int)Math.Round((k.X + k.Width) * zoom);
                int y1 = (int)Math.Round((k.Y + k.Height) * zoom);
                scaled.Add(new KeyBox(k.Name, k.Vk, x0, y0, x1 - x0, y1 - y0,
                    (int)Math.Round(k.LabelX * zoom), (int)Math.Round(k.LabelY * zoom)));
            }
            return scaled;
        }

        /// <summary>Scales the map to a view where the photo is drawn displayWidth pixels wide.</summary>
        public KeyMap ScaleToWidth(int displayWidth)
        {
            return Scale((double)displayWidth / ImageWidth);
        }

        public static KeyMap Load(string path)
        {
            return Parse(File.ReadAllText(path), path);
        }

        /// <summary>Parses key-map JSON text (e.g. read from an embedded resource).</summary>
        public static KeyMap Parse(string json, string source = "key map")
        {
            var map = new KeyMap
            {
                Image = MatchString(json, "image"),
                ImageWidth = MatchInt(json, "imageWidth"),
                ImageHeight = MatchInt(json, "imageHeight")
            };
            if (map.ImageWidth <= 0 || map.ImageHeight <= 0)
                throw new InvalidDataException("Key map is missing imageWidth/imageHeight: " + source);

            // Each key is a flat object; match them one by one (property order independent).
            foreach (Match m in Regex.Matches(json, @"\{[^{}]*""name""[^{}]*\}"))
            {
                string obj = m.Value;
                string name = MatchString(obj, "name");
                if (name == null) continue;

                string vkRaw = Regex.Match(obj, @"""vk""\s*:\s*(null|\d+)").Groups[1].Value;
                int? vk = vkRaw == "" || vkRaw == "null"
                    ? (int?)null
                    : int.Parse(vkRaw, CultureInfo.InvariantCulture);

                map.Add(new KeyBox(name, vk,
                    MatchInt(obj, "x"), MatchInt(obj, "y"),
                    MatchInt(obj, "width"), MatchInt(obj, "height"),
                    MatchInt(obj, "labelX"), MatchInt(obj, "labelY")));
            }

            if (map._keys.Count == 0)
                throw new InvalidDataException("No keys found in key map: " + source);
            return map;
        }

        private void Add(KeyBox k)
        {
            _keys.Add(k);
            // First entry wins on VK collisions (Enter vs NumpadEnter both carry VK 13).
            if (k.Vk.HasValue && !_byVk.ContainsKey(k.Vk.Value))
                _byVk[k.Vk.Value] = k;
        }

        // Shared with MouseMap, which uses the same flat-JSON-object regex parsing.
        internal static string MatchString(string json, string prop)
        {
            var m = Regex.Match(json, "\"" + prop + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        internal static int MatchInt(string json, string prop)
        {
            var m = Regex.Match(json, "\"" + prop + "\"\\s*:\\s*(-?\\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }
    }
}
