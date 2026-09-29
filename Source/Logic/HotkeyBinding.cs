using System;
using System.Collections.Generic;

namespace SirDiorama
{
    // A keyboard shortcut such as "Alt+F2": modifiers, then one key named as
    // in the game's key list (VRage.Input.MyKeys). Modifiers must match
    // exactly: Ctrl+F2 does not trigger Alt+F2, and the other way round.
    public struct HotkeyBinding
    {
        public readonly string Key;
        public readonly bool Ctrl;
        public readonly bool Alt;
        public readonly bool Shift;

        public HotkeyBinding(string key, bool ctrl, bool alt, bool shift)
        {
            Key = key;
            Ctrl = ctrl;
            Alt = alt;
            Shift = shift;
        }

        // Keys a shortcut may use alone with its modifiers. Letters and digits
        // are left out on purpose: they move, build and use the toolbar.
        public static readonly string[] Keys =
        {
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
            "Pause", "ScrollLock",
        };

        public static bool TryParse(string text, out HotkeyBinding binding)
        {
            binding = default(HotkeyBinding);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            bool ctrl = false, alt = false, shift = false;
            string key = null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in text.Split('+'))
            {
                var part = raw.Trim();
                if (part.Length == 0 || !seen.Add(part))
                    return false;

                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                    || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    ctrl = true;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    alt = true;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    shift = true;
                else if (key == null)
                    key = CanonicalKey(part);
                else
                    return false;

                if (key == string.Empty)
                    return false;
            }

            if (key == null)
                return false;

            binding = new HotkeyBinding(key, ctrl, alt, shift);
            return true;
        }

        private static string CanonicalKey(string name)
        {
            foreach (var k in Keys)
            {
                if (k.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return k;
            }
            return string.Empty;
        }

        public bool Matches(string pressedKey, bool ctrl, bool alt, bool shift)
        {
            return Key != null
                && string.Equals(Key, pressedKey, StringComparison.OrdinalIgnoreCase)
                && Ctrl == ctrl && Alt == alt && Shift == shift;
        }

        public override string ToString()
        {
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            parts.Add(Key ?? "?");
            return string.Join("+", parts.ToArray());
        }
    }
}
