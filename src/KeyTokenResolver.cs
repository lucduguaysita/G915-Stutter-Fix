using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;

namespace KeyboardRepeatFilter
{
    /// <summary>
    /// Resolves a config token to one or more virtual-key codes. A token may be
    /// either a decimal VK code (e.g. "8") or a key name as shown in the log
    /// (e.g. "Back", "Return", "I", "LCONTROL"). Name matching is
    /// case-insensitive and the "VK_" prefix is optional.
    ///
    /// A generic modifier name (Ctrl / Shift / Alt) expands to every variant —
    /// generic, left and right — so excluding "Ctrl" covers both Control keys
    /// regardless of which code the keyboard reports. Use the specific name
    /// (e.g. "LCONTROL") to target just one side.
    ///
    /// Names come from the same <see cref="VirtualKeys"/> resource used to label
    /// keys in the log, so any name that appears in the log resolves here too.
    /// </summary>
    internal static class KeyTokenResolver
    {
        private static readonly Dictionary<string, int> NameToVk = BuildNameMap();

        // Generic modifier names expand to { generic, left, right } so that a
        // single entry covers the key no matter which variant is reported.
        private static readonly Dictionary<string, int[]> ModifierGroups =
            new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "CONTROL", new[] { 0x11, 0xA2, 0xA3 } },
                { "CTRL",    new[] { 0x11, 0xA2, 0xA3 } },
                { "SHIFT",   new[] { 0x10, 0xA0, 0xA1 } },
                { "MENU",    new[] { 0x12, 0xA4, 0xA5 } },
                { "ALT",     new[] { 0x12, 0xA4, 0xA5 } },
            };

        private static Dictionary<string, int> BuildNameMap()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                ResourceSet set = VirtualKeys.ResourceManager.GetResourceSet(
                    CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
                if (set != null)
                {
                    foreach (DictionaryEntry entry in set)
                    {
                        // Resource keys are the VK code in hex (e.g. "0D"); values
                        // are the names (e.g. "VK_RETURN").
                        if (!(entry.Key is string hexKey) || !(entry.Value is string name))
                        {
                            continue;
                        }

                        if (int.TryParse(hexKey, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int vk))
                        {
                            map[Normalize(name)] = vk;
                        }
                    }
                }
            }
            catch
            {
                // If the resource can't be read, name resolution is simply
                // unavailable; numeric tokens still work.
            }

            return map;
        }

        private static string Normalize(string token)
        {
            token = token.Trim();
            if (token.StartsWith("VK_", StringComparison.OrdinalIgnoreCase))
            {
                token = token.Substring(3);
            }

            return token;
        }

        /// <summary>
        /// Resolves a token to the virtual-key code(s) it refers to, or an empty
        /// list if it cannot be recognized. Numeric tokens are decimal VK codes;
        /// generic modifiers expand to all their variants; everything else is a
        /// single key name.
        /// </summary>
        public static IReadOnlyList<int> Resolve(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return Array.Empty<int>();
            }

            token = token.Trim();

            // Pure number => decimal VK code (back-compat and escape hatch).
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            {
                return (n >= 0 && n <= 255) ? new[] { n } : Array.Empty<int>();
            }

            string norm = Normalize(token);

            // Generic modifier => generic + left + right.
            if (ModifierGroups.TryGetValue(norm, out int[] group))
            {
                return group;
            }

            // Otherwise, a single named key.
            if (NameToVk.TryGetValue(norm, out int vk))
            {
                return new[] { vk };
            }

            return Array.Empty<int>();
        }
    }
}
