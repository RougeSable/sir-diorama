namespace SirDiorama
{
    // Everything the player reads, in one place.
    public static class Texts
    {
        public const string DisplayName = "Sir Diorama";

        public const string On = "Sir Diorama: blocky look on.";
        public const string Off = "Sir Diorama: blocky look off, back to the game's own rendering.";
        public const string CommandHelp =
            "Sir Diorama: /diorama toggles the look, /diorama on, /diorama off, /diorama status, /diorama key Alt+F2.";

        public const string StopPrefix = "Sir Diorama is stopped for this session: ";
        public const string StopUnexpectedEngine = StopPrefix + "the game's render engine has changed. The game's rendering is kept.";
        public const string StopMissingHeader = StopPrefix + "an image effect of the game is missing. The game's rendering is kept.";
        public const string StopShaderFile = StopPrefix + "its image effect could not be written to the player's folder.";
        public const string StopVariantRefused = StopPrefix + "the game refused to compile its image effect. The game's rendering is kept.";
        public const string StopFault = StopPrefix + "fault while rendering. The game's rendering is kept.";
        public const string StopPatch = StopPrefix + "it could not hook into the game's final colour pass.";

        public static string StopCoexistence(string otherPlugins)
        {
            return StopPrefix + "another plugin already uses the game's final colour pass (" + otherPlugins
                + "). Sir Diorama yields to it.";
        }

        public static string Status(bool on, string stopReason, string hotkey)
        {
            if (stopReason != null)
                return stopReason;
            return (on ? On : Off) + " Shortcut: " + hotkey + ".";
        }

        public static string HotkeyChanged(string hotkey)
        {
            return "Sir Diorama: shortcut set to " + hotkey + ".";
        }

        public static string HotkeyInvalid(string text)
        {
            return "Sir Diorama: '" + text + "' is not a valid shortcut. Example: /diorama key Alt+F2.";
        }

        // Settings screen, opened from Pulsar.
        public const string ScreenTitle = "Sir Diorama";
        public const string EnableBox = "Enable plugin";
        public const string EnableBoxHelp =
            "The blocky look, in the manner of Minecraft. Unticked, the game gets its own rendering back, exactly.";
        public const string TexelDensity = "Texels per metre";
        public const string TexelDensityHelp =
            "How many texels cover one metre of surface. 8 by default, the closest to Minecraft: a large block is 20 texels wide, a small one 4.";
        public const string SmallestTexel = "Smallest texel";
        public const string SmallestTexelHelp =
            "Far away, texels grow so that none is smaller than this many screen pixels.";
        public const string ColourBoost = "Colour boost";
        public const string ColourBoostHelp = "Extra saturation, for plain and bright colours. 0 %: the game's colours.";
        public const string Hotkey = "Shortcut";
        public const string HotkeyHelp = "Switches the look on and off during play. Chat: /diorama key Alt+F2.";
        public const string CtrlBox = "Ctrl";
        public const string AltBox = "Alt";
        public const string ShiftBox = "Shift";
        public const string DefaultsButton = "Defaults";
        public const string CloseButton = "Close";
        public const string ScreenStopped = "Stopped for this session, see the game log.";

        public static string DensityValue(int density)
        {
            return density + " (" + (100.0 / density).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " cm)";
        }
    }
}
