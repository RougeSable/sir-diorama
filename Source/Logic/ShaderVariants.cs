using System.Collections.Generic;

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

        // Same macros as the game for the same variant (MyToneMapping.Init).
        // The settings are not macros: they go through the constant buffer,
        // so each variant is compiled once per game session.
        public static List<MacroDefinition> Macros(Variant variant)
        {
            var macros = new List<MacroDefinition> { new MacroDefinition("NUMTHREADS", "8") };
            if (variant == Variant.AlphaLuminance)
                macros.Add(new MacroDefinition("FILL_ALPHA_LUMINANCE", null));
            else if (variant == Variant.NoTonemapping)
                macros.Add(new MacroDefinition("DISABLE_TONEMAPPING", null));
            return macros;
        }
    }
}
