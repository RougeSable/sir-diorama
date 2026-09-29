namespace SirDiorama
{
    // Everything the player reads, in one place.
    public static class Texts
    {
        public const string DisplayName = "Sir Diorama";

        public const string On = "Sir Diorama: diorama look on.";
        public const string Off = "Sir Diorama: diorama look off, back to the game's own rendering.";
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
            "Big pixels and miniature look. Unticked, the game gets its own rendering back, exactly.";
        public const string PixelsBox = "Big pixels";
        public const string PixelsBoxHelp = "Square pixels, reduced palette and a stable dither, in the spirit of 8 and 16 bit games.";
        public const string PixelSize = "Pixel size";
        public const string PixelSizeHelp = "Size of one big pixel, in screen pixels.";
        public const string PaletteColors = "Palette colours";
        public const string PaletteColorsHelp = "Number of colours of the palette. The dither never makes an area brighter.";
        public const string MiniatureBox = "Miniature blur";
        public const string MiniatureBoxHelp =
            "Sharp where you look, blurred in front and behind. Out of focus lights spread into small discs.";
        public const string BlurStrength = "Blur strength";
        public const string BlurStrengthHelp = "0 %: no blur; 100 %: the largest discs.";
        public const string Hotkey = "Shortcut";
        public const string HotkeyHelp = "Switches the look on and off during play. Chat: /diorama key Alt+F2.";
        public const string CtrlBox = "Ctrl";
        public const string AltBox = "Alt";
        public const string ShiftBox = "Shift";
        public const string DefaultsButton = "Defaults";
        public const string CloseButton = "Close";
        public const string ScreenStopped = "Stopped for this session, see the game log.";

        public static string PaletteValue(int requested, PaletteLevels levels)
        {
            return levels.Colors + " (" + levels.Red + "x" + levels.Green + "x" + levels.Blue + ")";
        }
    }
}
