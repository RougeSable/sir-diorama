using System.Collections.Generic;
using System.Globalization;

namespace SirDiorama
{
    // The three variants of the game's final colour pass, in the order of
    // their static fields in MyToneMapping: m_cs, m_csAlphaLuminance, m_csSkip.
    public enum Variant
    {
        Normal = 0,
        AlphaLuminance = 1,
        NoTonemapping = 2,
    }

    // A macro definition, with no dependency on SharpDX: the game side turns
    // it into a ShaderMacro. A null value defines the macro without a value,
    // exactly as the game does for its flags.
    public struct MacroDefinition
    {
        public readonly string Name;
        public readonly string Value;

        public MacroDefinition(string name, string value)
        {
            Name = name;
            Value = value;
        }

        public override string ToString()
        {
            return Value == null ? Name : Name + "=" + Value;
        }
    }

    public static class ShaderVariants
    {
        public static readonly string[] GameFields = { "m_cs", "m_csAlphaLuminance", "m_csSkip" };

        public const int VariantCount = 3;

        // What MyToneMapping.Run picks, reproduced as is:
        // (!enableTonemapping) ? m_csSkip : (needsAlphaLuminance ? m_csAlphaLuminance : m_cs)
        public static Variant Pick(bool enableTonemapping, bool needsAlphaLuminance)
        {
            if (!enableTonemapping)
                return Variant.NoTonemapping;
            return needsAlphaLuminance ? Variant.AlphaLuminance : Variant.Normal;
        }

        public static List<MacroDefinition> Macros(Variant variant, DioramaSettings settings)
        {
            var s = settings.Normalized();
            var macros = new List<MacroDefinition>
            {
                // Same macros as the game for the same variant (MyToneMapping.Init).
                new MacroDefinition("NUMTHREADS", "8"),
            };

            if (variant == Variant.AlphaLuminance)
                macros.Add(new MacroDefinition("FILL_ALPHA_LUMINANCE", null));
            else if (variant == Variant.NoTonemapping)
                macros.Add(new MacroDefinition("DISABLE_TONEMAPPING", null));

            var levels = Palette.LevelsFor(s.PaletteColors);
            macros.Add(new MacroDefinition("DIORAMA_PIXELS", s.PixelsEnabled ? "1" : "0"));
            macros.Add(new MacroDefinition("DIORAMA_PIXEL_SIZE", Integer(s.PixelsEnabled ? s.PixelSize : 1)));
            macros.Add(new MacroDefinition("DIORAMA_LEVELS_R", Integer(levels.Red)));
            macros.Add(new MacroDefinition("DIORAMA_LEVELS_G", Integer(levels.Green)));
            macros.Add(new MacroDefinition("DIORAMA_LEVELS_B", Integer(levels.Blue)));
            macros.Add(new MacroDefinition("DIORAMA_MINIATURE", s.MiniatureVisible ? "1" : "0"));
            macros.Add(new MacroDefinition("DIORAMA_BLUR_RADIUS", Float(Focus.MaxRadiusShare(s.BlurStrength))));
            macros.Add(new MacroDefinition("DIORAMA_TAPS", Integer(Focus.Taps(s.BlurStrength))));
            return macros;
        }

        // Two settings with the same signature give the same variants: no need
        // to compile again. The on/off switch is not part of it.
        public static string Signature(DioramaSettings settings)
        {
            var s = settings.Normalized();
            return string.Join("|", new[]
            {
                s.PixelsEnabled ? "p" : "-",
                Integer(s.PixelSize),
                Integer(s.PaletteColors),
                s.MiniatureEnabled ? "m" : "-",
                Integer(s.BlurStrength),
            });
        }

        private static string Integer(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        // Always with a decimal point and an f suffix: the HLSL compiler does
        // not know the decimal comma, whatever the player's language.
        public static string Float(float value)
        {
            return value.ToString("0.0#####", CultureInfo.InvariantCulture) + "f";
        }
    }
}
