using System;

namespace SirDiorama
{
    // The player's settings, saved in his Storage\sir-diorama folder.
    // No dependency on the game: the class is tested as is.
    //
    // Every value here reaches the shader through a constant buffer, filled
    // every frame: changing a setting never compiles anything.
    public class LookSettings
    {
        // Texel density on surfaces, in texels per metre. Powers of two only,
        // so that every texel edge falls on a block edge: 1/4 m divides the
        // half size of both a small block (0.5 m) and a large one (2.5 m).
        // 8 per metre is the closest to Minecraft on screen, as measured in
        // game: a large block is 20 texels wide, a small one 4.
        public static readonly int[] TexelDensities = { 4, 8, 16, 32 };
        public const int TexelDensityDefault = 8;

        // Far away, texels grow (by powers of two) so that none is smaller
        // than this many screen pixels.
        public const int SmallestTexelMin = 2;
        public const int SmallestTexelMax = 8;
        public const int SmallestTexelDefault = 4;

        // Extra colour saturation, in percent, for the bright and plain
        // colours of Minecraft (75 %, as measured in game).
        public const int ColourBoostMin = 0;
        public const int ColourBoostMax = 100;
        public const int ColourBoostDefault = 75;

        public const string HotkeyDefault = "Alt+F2";

        // The first setting: the whole look, on from installation. The
        // shortcut toggles this one.
        public bool Enabled { get; set; } = true;

        public int TexelDensity { get; set; } = TexelDensityDefault;

        public int SmallestTexel { get; set; } = SmallestTexelDefault;

        public int ColourBoost { get; set; } = ColourBoostDefault;

        // Shortcut that switches the look on and off during play.
        public string Hotkey { get; set; } = HotkeyDefault;

        // Side of one texel, in metres.
        public double TexelSize
        {
            get { return 1.0 / Normalized().TexelDensity; }
        }

        public LookSettings Copy()
        {
            return new LookSettings
            {
                Enabled = Enabled,
                TexelDensity = TexelDensity,
                SmallestTexel = SmallestTexel,
                ColourBoost = ColourBoost,
                Hotkey = Hotkey,
            };
        }

        // Brings every value back within its bounds: a file edited by hand can
        // never produce an absurd look.
        public LookSettings Normalized()
        {
            var s = Copy();
            s.TexelDensity = NearestDensity(TexelDensity);
            s.SmallestTexel = Clamp(SmallestTexel, SmallestTexelMin, SmallestTexelMax);
            s.ColourBoost = Clamp(ColourBoost, ColourBoostMin, ColourBoostMax);

            HotkeyBinding binding;
            s.Hotkey = HotkeyBinding.TryParse(Hotkey, out binding) ? binding.ToString() : HotkeyDefault;
            return s;
        }

        // Index of the density in TexelDensities, for the settings slider.
        public static int DensityIndex(int density)
        {
            return Array.IndexOf(TexelDensities, NearestDensity(density));
        }

        public static int NearestDensity(int density)
        {
            var best = TexelDensities[0];
            foreach (var d in TexelDensities)
            {
                if (Math.Abs(Math.Log(Math.Max(density, 1)) - Math.Log(d)) < Math.Abs(Math.Log(Math.Max(density, 1)) - Math.Log(best)))
                    best = d;
            }
            return best;
        }

        public static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
