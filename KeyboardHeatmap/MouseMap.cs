using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace KeyboardHeatmap
{
    /// <summary>
    /// Loads a *.mousemap.json (e.g. Assets/G502X Plus.mousemap.json) describing button
    /// hit boxes split across a top-down photo and a side photo. Unlike the keyboard's
    /// single-image KeyMap, a mouse map covers two images, so each button records which
    /// one it belongs to.
    /// </summary>
    public sealed class MouseMap
    {
        public string TopImage { get; private set; }
        public int TopWidth { get; private set; }
        public int TopHeight { get; private set; }
        public string SideImage { get; private set; }
        public int SideWidth { get; private set; }
        public int SideHeight { get; private set; }

        public IReadOnlyList<KeyBox> TopButtons { get { return _top; } }
        public IReadOnlyList<KeyBox> SideButtons { get { return _side; } }

        private List<KeyBox> _top = new List<KeyBox>();
        private List<KeyBox> _side = new List<KeyBox>();

        public static MouseMap Load(string path)
        {
            return Parse(File.ReadAllText(path), path);
        }

        /// <summary>Parses mouse-map JSON text (e.g. read from an embedded resource).</summary>
        public static MouseMap Parse(string json, string source = "mouse map")
        {
            var map = new MouseMap
            {
                TopImage = KeyMap.MatchString(json, "topImage"),
                TopWidth = KeyMap.MatchInt(json, "topWidth"),
                TopHeight = KeyMap.MatchInt(json, "topHeight"),
                SideImage = KeyMap.MatchString(json, "sideImage"),
                SideWidth = KeyMap.MatchInt(json, "sideWidth"),
                SideHeight = KeyMap.MatchInt(json, "sideHeight")
            };
            if (map.TopWidth <= 0 || map.TopHeight <= 0 || map.SideWidth <= 0 || map.SideHeight <= 0)
                throw new InvalidDataException("Mouse map is missing image dimensions: " + source);

            foreach (Match m in Regex.Matches(json, @"\{[^{}]*""name""[^{}]*\}"))
            {
                string obj = m.Value;
                string name = KeyMap.MatchString(obj, "name");
                string image = KeyMap.MatchString(obj, "image");
                if (name == null || image == null) continue;

                var box = new KeyBox(name, null,
                    KeyMap.MatchInt(obj, "x"), KeyMap.MatchInt(obj, "y"),
                    KeyMap.MatchInt(obj, "width"), KeyMap.MatchInt(obj, "height"));

                if (string.Equals(image, "top", StringComparison.OrdinalIgnoreCase))
                    map._top.Add(box);
                else if (string.Equals(image, "side", StringComparison.OrdinalIgnoreCase))
                    map._side.Add(box);
            }

            if (map._top.Count == 0 && map._side.Count == 0)
                throw new InvalidDataException("No buttons found in mouse map: " + source);
            return map;
        }
    }
}
