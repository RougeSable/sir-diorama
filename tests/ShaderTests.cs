using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SirDiorama.Tests
{
    public class ShaderTests
    {
        [Fact]
        public void OnlyFreeSlotsAreDeclared()
        {
            // The game's pass binds t0 to t3, u0 and s0 to s3 itself; the
            // variant declares nothing else than the depth, and the focus of the
            // previous and of this frame.
            var slots = Regex.Matches(ShaderSource.Text, @"register\s*\(\s*(\w+)\s*\)")
                .Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
            Assert.Equal(new[] { "t31", "t30", "u1" }, slots);
        }

        [Fact]
        public void GameHeadersAreIncludedBetweenAngleBrackets()
        {
            // Between quotes, the game's compiler would look for them next to
            // our file, in the player's folder, and fail.
            foreach (var header in ShaderSource.GameHeaders)
                Assert.Contains("#include <" + header + ">", ShaderSource.Text);
            Assert.DoesNotContain("#include \"", ShaderSource.Text);
        }

        [Fact]
        public void TheEntryPointIsTheOneTheGameExpects()
        {
            Assert.Contains("void __compute_shader(uint3 dispatchThreadID : SV_DispatchThreadID", ShaderSource.Text);
            Assert.Contains("[numthreads(NUMTHREADS_X, NUMTHREADS_Y, 1)]", ShaderSource.Text);
        }

        [Fact]
        public void TheGameFlagsAreHonoured()
        {
            Assert.Contains("#ifndef DISABLE_TONEMAPPING", ShaderSource.Text);
            Assert.Contains("#ifdef FILL_ALPHA_LUMINANCE", ShaderSource.Text);
            Assert.Contains("#ifndef DISABLE_COLOR_FILTERS", ShaderSource.Text);
        }

        [Fact]
        public void TheDitherDoesNotDependOnTime()
        {
            // The palette only reads the position of the big pixel.
            var palette = Regex.Match(ShaderSource.Text, @"float3 DioramaPalette\(.*?\n}\n", RegexOptions.Singleline).Value;
            Assert.NotEmpty(palette);
            Assert.DoesNotContain("frameTime", palette);
            Assert.DoesNotContain("random", palette.ToLowerInvariant());
        }

        [Fact]
        public void TheShaderUsesTheSameConstantsAsTheTestedCode()
        {
            Assert.Contains("#define DIORAMA_SHARP_BAND " + ShaderVariants.Float(Focus.SharpBand), ShaderSource.Text);
            Assert.Contains("#define DIORAMA_BLUR_GAIN " + ShaderVariants.Float(Focus.BlurGain), ShaderSource.Text);
            Assert.Contains("#define DIORAMA_FOLLOW_TIME " + ShaderVariants.Float(Focus.FollowTime), ShaderSource.Text);
            var array = Regex.Match(ShaderSource.Text, @"DioramaBayer\[16\] =\s*\{(.*?)\}", RegexOptions.Singleline).Groups[1].Value;
            var inShader = Regex.Matches(array, @"\d+").Cast<Match>().Select(m => int.Parse(m.Value)).ToArray();
            Assert.Equal(Palette.Bayer, inShader);
            Assert.Contains("(DioramaBayer[(block.y & 3) * 4 + (block.x & 3)] + 1.0f) / 16.0f", ShaderSource.Text);
        }

        [Fact]
        public void TheFileIsWrittenWithUnixLineEndings()
        {
            Assert.DoesNotContain("\r", ShaderSource.Text);
        }
    }

    public class ShaderVariantsTests
    {
        [Theory]
        [InlineData(true, false, Variant.Normal)]
        [InlineData(true, true, Variant.AlphaLuminance)]
        [InlineData(false, false, Variant.NoTonemapping)]
        [InlineData(false, true, Variant.NoTonemapping)]
        public void PicksLikeTheGame(bool tonemapping, bool alphaLuminance, Variant expected)
        {
            Assert.Equal(expected, ShaderVariants.Pick(tonemapping, alphaLuminance));
        }

        [Fact]
        public void FieldsFollowTheOrderOfTheVariants()
        {
            Assert.Equal("m_cs", ShaderVariants.GameFields[(int)Variant.Normal]);
            Assert.Equal("m_csAlphaLuminance", ShaderVariants.GameFields[(int)Variant.AlphaLuminance]);
            Assert.Equal("m_csSkip", ShaderVariants.GameFields[(int)Variant.NoTonemapping]);
        }

        [Fact]
        public void EachVariantCarriesTheGameMacros()
        {
            var s = new DioramaSettings();
            var normal = ShaderVariants.Macros(Variant.Normal, s).Select(m => m.ToString()).ToArray();
            var alpha = ShaderVariants.Macros(Variant.AlphaLuminance, s).Select(m => m.ToString()).ToArray();
            var skip = ShaderVariants.Macros(Variant.NoTonemapping, s).Select(m => m.ToString()).ToArray();

            Assert.Equal("NUMTHREADS=8", normal[0]);
            Assert.DoesNotContain("FILL_ALPHA_LUMINANCE", normal);
            Assert.DoesNotContain("DISABLE_TONEMAPPING", normal);
            Assert.Contains("FILL_ALPHA_LUMINANCE", alpha);
            Assert.Contains("DISABLE_TONEMAPPING", skip);
        }

        [Fact]
        public void TheSettingsReachTheShader()
        {
            var macros = ShaderVariants.Macros(Variant.Normal, new DioramaSettings { PixelSize = 6, PaletteColors = 64, BlurStrength = 100 })
                .ToDictionary(m => m.Name, m => m.Value);
            Assert.Equal("1", macros["DIORAMA_PIXELS"]);
            Assert.Equal("6", macros["DIORAMA_PIXEL_SIZE"]);
            Assert.Equal("4", macros["DIORAMA_LEVELS_R"]);
            Assert.Equal("4", macros["DIORAMA_LEVELS_G"]);
            Assert.Equal("4", macros["DIORAMA_LEVELS_B"]);
            Assert.Equal("1", macros["DIORAMA_MINIATURE"]);
            Assert.Equal("0.03f", macros["DIORAMA_BLUR_RADIUS"]);
            Assert.Equal("96", macros["DIORAMA_TAPS"]);

            var off = ShaderVariants.Macros(Variant.Normal, new DioramaSettings { PixelsEnabled = false, MiniatureEnabled = false })
                .ToDictionary(m => m.Name, m => m.Value);
            Assert.Equal("0", off["DIORAMA_PIXELS"]);
            Assert.Equal("1", off["DIORAMA_PIXEL_SIZE"]);
            Assert.Equal("0", off["DIORAMA_MINIATURE"]);
        }

        [Fact]
        public void NumbersAreWrittenWithAPointWhateverTheLanguage()
        {
            var previous = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
                var macros = ShaderVariants.Macros(Variant.Normal, new DioramaSettings { BlurStrength = 50 })
                    .ToDictionary(m => m.Name, m => m.Value);
                Assert.Equal("0.015f", macros["DIORAMA_BLUR_RADIUS"]);
                Assert.Contains("#define DIORAMA_SHARP_BAND 0.06f", ShaderSource.Text);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void TheSwitchDoesNotAskForARecompilation()
        {
            Assert.Equal(
                ShaderVariants.Signature(new DioramaSettings { Enabled = true }),
                ShaderVariants.Signature(new DioramaSettings { Enabled = false }));
            Assert.NotEqual(
                ShaderVariants.Signature(new DioramaSettings { PixelSize = 4 }),
                ShaderVariants.Signature(new DioramaSettings { PixelSize = 5 }));
            Assert.NotEqual(
                ShaderVariants.Signature(new DioramaSettings { BlurStrength = 40 }),
                ShaderVariants.Signature(new DioramaSettings { BlurStrength = 60 }));
        }
    }
}
