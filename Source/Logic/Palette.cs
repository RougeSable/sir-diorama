using System;

namespace SirDiorama
{
    // Number of levels per channel of the reduced palette. Red x Green x Blue
    // is the number of colours actually drawn.
    public struct PaletteLevels
    {
        public readonly int Red;
        public readonly int Green;
        public readonly int Blue;

        public PaletteLevels(int red, int green, int blue)
        {
            Red = red;
            Green = green;
            Blue = blue;
        }

        public int Colors
        {
            get { return Red * Green * Blue; }
        }
    }

    // The reduced palette with its ordered dither, exactly as the shader draws
    // it (DioramaPalette in ShaderSource). This C# copy is what the tests check.
    //
    // Each channel is quantized in sRGB, on evenly spaced levels. Between the
    // level just below and the level just above, the 4x4 Bayer threshold of
    // the big pixel picks one of the two. The share of big pixels that take the
    // upper level is ROUNDED DOWN to the sixteenth, and measured in linear
    // light: over its dither cell, an area is therefore never brighter than
    // the original colour, neither in light nor in sRGB. Black stays black.
    public static class Palette
    {
        // Classic 4x4 Bayer matrix, values 0 to 15, row by row.
        public static readonly int[] Bayer =
        {
            0, 8, 2, 10,
            12, 4, 14, 6,
            3, 11, 1, 9,
            15, 7, 13, 5,
        };

        public const int CellSize = 4;
        public const int CellCount = CellSize * CellSize;

        // Splits a colour count into levels per channel. Balanced first (the
        // smallest channel gets as many levels as possible), then as many
        // colours as allowed, extra levels going to green, then red, then blue:
        // 8 -> 2x2x2, 32 -> 3x3x3, 48 -> 4x4x3, 64 -> 4x4x4.
        public static PaletteLevels LevelsFor(int colors)
        {
            colors = DioramaSettings.Clamp(colors, DioramaSettings.PaletteColorsMin, DioramaSettings.PaletteColorsMax);

            var best = new PaletteLevels(2, 2, 2);
            var bestMin = 2;
            for (var green = 2; green <= colors; green++)
            {
                for (var red = 2; red <= green; red++)
                {
                    for (var blue = 2; blue <= red; blue++)
                    {
                        var product = red * green * blue;
                        if (product > colors)
                            break;

                        var min = blue;
                        var candidate = new PaletteLevels(red, green, blue);
                        if (min > bestMin
                            || (min == bestMin && product > best.Colors)
                            || (min == bestMin && product == best.Colors && Prefer(candidate, best)))
                        {
                            best = candidate;
                            bestMin = min;
                        }
                    }
                }
            }
            return best;
        }

        // Same size and same smallest channel: the one closest to balanced
        // wins, then the one giving more to green.
        private static bool Prefer(PaletteLevels a, PaletteLevels b)
        {
            var spreadA = a.Green - a.Blue;
            var spreadB = b.Green - b.Blue;
            if (spreadA != spreadB)
                return spreadA < spreadB;
            return a.Green > b.Green;
        }

        // Threshold of the big pixel (x, y) of the screen grid: 1/16 to 16/16.
        // It depends on the position alone, never on time: the dither does not
        // flicker while nothing moves.
        public static float Threshold(int x, int y)
        {
            var i = (y & (CellSize - 1)) * CellSize + (x & (CellSize - 1));
            return (Bayer[i] + 1) / (float)CellCount;
        }

        public static float SrgbToLinear(float s)
        {
            return s <= 0.04045f ? s / 12.92f : (float)Math.Pow((s + 0.055f) / 1.055f, 2.4f);
        }

        public static float LinearToSrgb(float l)
        {
            return l <= 0.0031308f ? l * 12.92f : (float)(Math.Pow(Math.Abs(l), 1 / 2.4) * 1.055 - 0.055);
        }

        // One sRGB channel, in [0, 1], on 'levels' levels.
        public static float QuantizeChannel(float srgb, int levels, float threshold)
        {
            var value = Math.Max(0f, Math.Min(1f, srgb));
            float steps = levels - 1;
            var lower = (float)Math.Floor(value * steps);
            if (lower >= steps)
                return 1f;

            var low = lower / steps;
            var high = (lower + 1) / steps;
            var span = SrgbToLinear(high) - SrgbToLinear(low);
            var share = (SrgbToLinear(value) - SrgbToLinear(low)) / span;
            return share >= threshold ? high : low;
        }

        // A whole colour, sRGB in [0, 1], at the big pixel (x, y).
        public static float[] Quantize(float red, float green, float blue, PaletteLevels levels, int x, int y)
        {
            var t = Threshold(x, y);
            return new[]
            {
                QuantizeChannel(red, levels.Red, t),
                QuantizeChannel(green, levels.Green, t),
                QuantizeChannel(blue, levels.Blue, t),
            };
        }
    }
}
