using System.Collections.Generic;
using Xunit;

namespace SirDiorama.Tests
{
    public class PaletteReduiteTests
    {
        private static IEnumerable<int> AllPaletteSizes()
        {
            for (var colors = DioramaSettings.PaletteColorsMin; colors <= DioramaSettings.PaletteColorsMax; colors++)
                yield return colors;
        }

        private static float Luminance(float r, float g, float b)
        {
            return 0.2126f * r + 0.7152f * g + 0.0722f * b;
        }

        // The reduced palette never renders a colour brighter than the original.
        // An area of one colour is drawn over its 4x4 dither cell: averaged
        // over the cell, every channel, and the luminance, stay at or below
        // the original, in linear light as in sRGB. Checked for every palette
        // size the player can choose, on a grid of colours from black to
        // white, dark ones included.
        [Fact]
        public void NEclaircitJamaisUneCouleur()
        {
            const float tolerance = 1e-5f;
            const int grid = 16;

            foreach (var colors in AllPaletteSizes())
            {
                var levels = Palette.LevelsFor(colors);
                for (var ri = 0; ri <= grid; ri++)
                for (var gi = 0; gi <= grid; gi++)
                for (var bi = 0; bi <= grid; bi++)
                {
                    // Squared steps: many dark colours, where it matters most.
                    float r = (ri * ri) / (float)(grid * grid);
                    float g = (gi * gi) / (float)(grid * grid);
                    float b = (bi * bi) / (float)(grid * grid);

                    double sumR = 0, sumG = 0, sumB = 0, sumLinR = 0, sumLinG = 0, sumLinB = 0;
                    for (var y = 0; y < Palette.CellSize; y++)
                    for (var x = 0; x < Palette.CellSize; x++)
                    {
                        var q = Palette.Quantize(r, g, b, levels, x, y);
                        sumR += q[0];
                        sumG += q[1];
                        sumB += q[2];
                        sumLinR += Palette.SrgbToLinear(q[0]);
                        sumLinG += Palette.SrgbToLinear(q[1]);
                        sumLinB += Palette.SrgbToLinear(q[2]);
                    }

                    float n = Palette.CellCount;
                    Assert.True(sumR / n <= r + tolerance, Describe(colors, r, g, b, "red"));
                    Assert.True(sumG / n <= g + tolerance, Describe(colors, r, g, b, "green"));
                    Assert.True(sumB / n <= b + tolerance, Describe(colors, r, g, b, "blue"));

                    float linR = Palette.SrgbToLinear(r), linG = Palette.SrgbToLinear(g), linB = Palette.SrgbToLinear(b);
                    Assert.True(sumLinR / n <= linR + tolerance, Describe(colors, r, g, b, "red light"));
                    Assert.True(sumLinG / n <= linG + tolerance, Describe(colors, r, g, b, "green light"));
                    Assert.True(sumLinB / n <= linB + tolerance, Describe(colors, r, g, b, "blue light"));
                    Assert.True(
                        Luminance((float)(sumLinR / n), (float)(sumLinG / n), (float)(sumLinB / n)) <= Luminance(linR, linG, linB) + tolerance,
                        Describe(colors, r, g, b, "luminance"));
                }
            }
        }

        private static string Describe(int colors, float r, float g, float b, string what)
        {
            return colors + " colours, (" + r + ", " + g + ", " + b + "): " + what + " brighter than the original";
        }

        [Fact]
        public void BlackStaysBlackOnEveryPixelOfTheCell()
        {
            foreach (var colors in AllPaletteSizes())
            {
                var levels = Palette.LevelsFor(colors);
                for (var y = 0; y < Palette.CellSize; y++)
                for (var x = 0; x < Palette.CellSize; x++)
                    Assert.Equal(new[] { 0f, 0f, 0f }, Palette.Quantize(0f, 0f, 0f, levels, x, y));
            }
        }

        [Fact]
        public void AColourOnALevelIsKeptAsIs()
        {
            var levels = Palette.LevelsFor(64);
            for (var i = 0; i < levels.Green; i++)
            {
                var v = i / (float)(levels.Green - 1);
                for (var y = 0; y < Palette.CellSize; y++)
                for (var x = 0; x < Palette.CellSize; x++)
                    Assert.Equal(v, Palette.QuantizeChannel(v, levels.Green, Palette.Threshold(x, y)), 5);
            }
        }

        [Fact]
        public void OnlyPaletteColoursAreDrawn()
        {
            foreach (var colors in AllPaletteSizes())
            {
                var levels = Palette.LevelsFor(colors);
                Assert.True(levels.Colors <= colors);
                var seen = new HashSet<string>();
                for (var i = 0; i <= 40; i++)
                for (var y = 0; y < Palette.CellSize; y++)
                for (var x = 0; x < Palette.CellSize; x++)
                {
                    var v = i / 40f;
                    var q = Palette.Quantize(v, 1 - v, v * v, levels, x, y);
                    seen.Add(q[0].ToString("0.0000") + "|" + q[1].ToString("0.0000") + "|" + q[2].ToString("0.0000"));
                }
                Assert.True(seen.Count <= levels.Colors, colors + " colours: " + seen.Count + " drawn");
            }
        }

        [Theory]
        [InlineData(8, 2, 2, 2)]
        [InlineData(12, 2, 3, 2)]
        [InlineData(16, 2, 4, 2)]
        [InlineData(27, 3, 3, 3)]
        [InlineData(32, 3, 3, 3)]
        [InlineData(36, 3, 4, 3)]
        [InlineData(48, 4, 4, 3)]
        [InlineData(64, 4, 4, 4)]
        public void SplitsTheColoursIntoBalancedLevels(int colors, int red, int green, int blue)
        {
            var levels = Palette.LevelsFor(colors);
            Assert.Equal(red, levels.Red);
            Assert.Equal(green, levels.Green);
            Assert.Equal(blue, levels.Blue);
        }

        [Fact]
        public void MorePaletteColoursNeverMeanFewerLevels()
        {
            for (var colors = DioramaSettings.PaletteColorsMin; colors < DioramaSettings.PaletteColorsMax; colors++)
                Assert.True(Palette.LevelsFor(colors + 1).Colors >= Palette.LevelsFor(colors).Colors);
            Assert.True(Palette.LevelsFor(64).Colors > Palette.LevelsFor(8).Colors);
        }

        [Fact]
        public void TheDitherDependsOnThePositionAlone()
        {
            // Same big pixel, same threshold: nothing depends on time, so a
            // still picture is drawn the same way every frame.
            for (var y = 0; y < 12; y++)
            for (var x = 0; x < 12; x++)
            {
                Assert.Equal(Palette.Threshold(x, y), Palette.Threshold(x + 4, y + 8));
                Assert.InRange(Palette.Threshold(x, y), 1 / 16f, 1f);
            }

            var all = new HashSet<float>();
            for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
                all.Add(Palette.Threshold(x, y));
            Assert.Equal(16, all.Count);
        }
    }
}
