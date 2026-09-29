using System;

namespace SirDiorama
{
    // The player's settings, saved in his Storage\sir-diorama folder.
    // No dependency on the game: the class is tested as is.
    public class DioramaSettings
    {
        public const int PixelSizeMin = 2;
        public const int PixelSizeMax = 8;
        public const int PixelSizeDefault = 4;

        public const int PaletteColorsMin = 8;
        public const int PaletteColorsMax = 64;
        public const int PaletteColorsDefault = 32;

        public const int BlurStrengthMin = 0;
        public const int BlurStrengthMax = 100;
        public const int BlurStrengthDefault = 50;

        public const string HotkeyDefault = "Alt+F2";

        // The first setting: the whole diorama look, on from installation.
        // The hotkey toggles this one.
        public bool Enabled { get; set; } = true;

        // Big pixels: the picture is drawn in square blocks of PixelSize screen
        // pixels, with a reduced palette and a stable ordered dither.
        public bool PixelsEnabled { get; set; } = true;

        public int PixelSize { get; set; } = PixelSizeDefault;

        public int PaletteColors { get; set; } = PaletteColorsDefault;

        // Miniature: sharp at the distance of what sits in the middle of the
        // screen, blurred in front and behind, lights spreading into discs.
        public bool MiniatureEnabled { get; set; } = true;

        // Blur strength, in percent: 100 gives the largest discs.
        public int BlurStrength { get; set; } = BlurStrengthDefault;

        // Shortcut that switches the look on and off during play.
        public string Hotkey { get; set; } = HotkeyDefault;

        public DioramaSettings Copy()
        {
            return new DioramaSettings
            {
                Enabled = Enabled,
                PixelsEnabled = PixelsEnabled,
                PixelSize = PixelSize,
                PaletteColors = PaletteColors,
                MiniatureEnabled = MiniatureEnabled,
                BlurStrength = BlurStrength,
                Hotkey = Hotkey,
            };
        }

        // Brings every value back within its bounds: a file edited by hand can
        // never produce an absurd shader variant.
        public DioramaSettings Normalized()
        {
            var s = Copy();
            s.PixelSize = Clamp(PixelSize, PixelSizeMin, PixelSizeMax);
            s.PaletteColors = Clamp(PaletteColors, PaletteColorsMin, PaletteColorsMax);
            s.BlurStrength = Clamp(BlurStrength, BlurStrengthMin, BlurStrengthMax);

            HotkeyBinding binding;
            s.Hotkey = HotkeyBinding.TryParse(Hotkey, out binding) ? binding.ToString() : HotkeyDefault;
            return s;
        }

        // True when the shader has something to draw with these settings.
        public bool HasVisibleEffect
        {
            get { return PixelsEnabled || MiniatureVisible; }
        }

        // A zero blur strength draws no blur at all.
        public bool MiniatureVisible
        {
            get { return MiniatureEnabled && BlurStrength > 0; }
        }

        public static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
