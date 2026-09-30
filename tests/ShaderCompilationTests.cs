using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace SirDiorama.Tests
{
    // Compiles the plugin's shader for real, with the Windows HLSL compiler
    // (d3dcompiler_47, the one the game uses through SharpDX) and the game's
    // own headers, for each of the three variants. A variant that does not
    // compile would stop the effect in game.
    //
    // The game's headers are found through SE_SHADERS (the Content\Shaders
    // folder), SE_BIN64 (the game's Bin64 folder), or a copy of the game next
    // to the repository (the studio's sandbox keeps one in jeu-installe). When
    // none is there, the test says so in its output and checks nothing.
    public class ShaderCompilationTests
    {
        private readonly ITestOutputHelper m_output;

        public ShaderCompilationTests(ITestOutputHelper output)
        {
            m_output = output;
        }

        [Fact]
        public void EveryVariantCompilesWithTheGameHeaders()
        {
            var shaders = FindGameShaders();
            if (shaders == null || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                m_output.WriteLine("Game shaders not found (SE_SHADERS, SE_BIN64): compilation not checked.");
                return;
            }

            var source = Flatten(ShaderSource.Text, null, shaders, new List<string>());
            var compiled = 0;
            foreach (Variant variant in Enum.GetValues(typeof(Variant)))
            {
                var macros = ShaderVariants.Macros(variant);
                // Debug, as the game compiles (MyShaderCompiler, optimize: false);
                // then at the highest optimisation level, as a precaution.
                foreach (var flags in new uint[] { 1, 1 | 32768 })
                {
                    string errors;
                    var ok = Compile(source, macros, flags, out errors);
                    Assert.True(ok, variant + " " + string.Join(" ", macros) + "\n" + errors);
                    compiled++;
                }
            }
            m_output.WriteLine(compiled + " compilations against " + shaders);
        }

        [Fact]
        public void TheShaderReadsItsSettingsFromTheConstantBuffer()
        {
            var text = ShaderSource.Text;
            Assert.Contains("register(b" + ShaderSource.ConstantsSlot + ")", text);
            Assert.Contains("register(t" + ShaderSource.DepthSlot + ")", text);
            Assert.InRange(ShaderSource.ConstantsSlot, 1, 7);
            // Ten float4, then three per screen, as the cbuffer declares them.
            Assert.Equal((10 + 3 * LookConstants.MaxScreens) * 16, LookConstants.ByteSize);
            Assert.Contains("float4 LookScreens[" + (3 * LookConstants.MaxScreens) + "];", text);
            Assert.Equal(10 * 4, LookConstants.ScreenStart);
            Assert.Equal(6 * 4 + 3, LookConstants.ScreenCountIndex);
            Assert.Equal(10, Regex.Matches(text, "^\\s*float4 Look\\w+;", RegexOptions.Multiline).Count);
            foreach (var header in ShaderSource.GameHeaders)
                Assert.Contains("#include <" + header + ">", text);
        }

        [Fact]
        public void EachVariantHasTheGameFlags()
        {
            Assert.Equal(new[] { "NUMTHREADS=8" }, ShaderVariants.Macros(Variant.Normal).ConvertAll(m => m.ToString()));
            Assert.Equal(new[] { "NUMTHREADS=8", "FILL_ALPHA_LUMINANCE" }, ShaderVariants.Macros(Variant.AlphaLuminance).ConvertAll(m => m.ToString()));
            Assert.Equal(new[] { "NUMTHREADS=8", "DISABLE_TONEMAPPING" }, ShaderVariants.Macros(Variant.NoTonemapping).ConvertAll(m => m.ToString()));

            Assert.Equal(Variant.NoTonemapping, ShaderVariants.Pick(false, true));
            Assert.Equal(Variant.AlphaLuminance, ShaderVariants.Pick(true, true));
            Assert.Equal(Variant.Normal, ShaderVariants.Pick(true, false));
        }

        private static string FindGameShaders()
        {
            var direct = Environment.GetEnvironmentVariable("SE_SHADERS");
            if (!string.IsNullOrEmpty(direct) && File.Exists(Path.Combine(direct, "Frame.hlsli")))
                return direct;

            var bin64 = Environment.GetEnvironmentVariable("SE_BIN64");
            if (!string.IsNullOrEmpty(bin64))
            {
                var fromBin = Path.GetFullPath(Path.Combine(bin64, "..", "Content", "Shaders"));
                if (File.Exists(Path.Combine(fromBin, "Frame.hlsli")))
                    return fromBin;
            }

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var copy = Path.Combine(dir.FullName, "jeu-installe", "Content", "Shaders");
                if (File.Exists(Path.Combine(copy, "Frame.hlsli")))
                    return copy;
                dir = dir.Parent;
            }
            return null;
        }

        // Inlines the includes as the game's include handler resolves them:
        // angle brackets from the shader root, quotes next to the including
        // file. The game's headers all carry include guards.
        private static string Flatten(string text, string currentDir, string root, List<string> stack)
        {
            var result = new StringBuilder();
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                var m = Regex.Match(line, "^\\s*#include\\s*([<\"])([^>\"]+)[>\"]");
                if (!m.Success)
                {
                    result.Append(line).Append('\n');
                    continue;
                }

                var name = m.Groups[2].Value;
                var path = m.Groups[1].Value == "<" || currentDir == null
                    ? Path.Combine(root, name)
                    : Path.Combine(currentDir, name);
                path = Path.GetFullPath(path);
                if (!File.Exists(path))
                {
                    // Only C headers of inactive branches (float.h in
                    // D3DX_DXGIFormatConvert.inl): left to the preprocessor,
                    // which fails on them only if the branch is active.
                    result.Append(line).Append('\n');
                    continue;
                }
                if (stack.Contains(path))
                    continue;

                stack.Add(path);
                result.Append(Flatten(File.ReadAllText(path), Path.GetDirectoryName(path), root, stack));
                stack.Remove(path);
            }
            return result.ToString();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ShaderMacro
        {
            public IntPtr Name;
            public IntPtr Definition;
        }

        [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ID3DBlob
        {
            [PreserveSig] IntPtr GetBufferPointer();
            [PreserveSig] UIntPtr GetBufferSize();
        }

        [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)]
        private static extern int D3DCompile(
            byte[] source, UIntPtr size, string sourceName, ShaderMacro[] defines, IntPtr include,
            string entryPoint, string target, uint flags1, uint flags2, out ID3DBlob code, out ID3DBlob errors);

        private static bool Compile(string source, List<MacroDefinition> macros, uint flags, out string errors)
        {
            var defines = new ShaderMacro[macros.Count + 1];
            var allocated = new List<IntPtr>();
            try
            {
                for (var i = 0; i < macros.Count; i++)
                {
                    var name = Marshal.StringToHGlobalAnsi(macros[i].Name);
                    var value = Marshal.StringToHGlobalAnsi(macros[i].Value ?? "");
                    allocated.Add(name);
                    allocated.Add(value);
                    defines[i] = new ShaderMacro { Name = name, Definition = value };
                }

                var bytes = Encoding.ASCII.GetBytes(source);
                ID3DBlob code, messages;
                var hr = D3DCompile(bytes, (UIntPtr)bytes.Length, ShaderSource.FileName, defines, IntPtr.Zero,
                    "__compute_shader", "cs_5_0", flags, 0, out code, out messages);

                errors = messages == null ? "" : Marshal.PtrToStringAnsi(messages.GetBufferPointer(), (int)messages.GetBufferSize());
                return hr >= 0 && code != null;
            }
            finally
            {
                foreach (var p in allocated)
                    Marshal.FreeHGlobal(p);
            }
        }
    }
}
