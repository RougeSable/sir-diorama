using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using VRageRender;

namespace SirDiorama
{
    // The prefix, postfix and finalizer put on MyToneMapping.Run, the game's
    // final colour pass (the method proven on Sir Cel Shading).
    //
    // On: the prefix puts our variant in the static field of the variant in
    // use, binds the scene depth in t31 and the two focus buffers in t30 and
    // u1, and keeps FXAA off; the postfix gives the field its game shader back
    // and unbinds the three slots. Off, or stopped: nothing is touched, FXAA
    // gets its setting back, the game draws with its own shaders.
    //
    // Everything below runs on the render thread, and on it alone.
    internal static class FinalColourPass
    {
        // Set once by the plugin, before the patch is applied.
        public static RenderEngine Engine;
        public static SessionStop Stop;
        public static string ShaderPath;

        // Snapshot of the settings, replaced as a whole by the main thread.
        private static volatile DioramaSettings s_settings = new DioramaSettings();

        public static DioramaSettings CurrentSettings
        {
            get { return s_settings; }
            set { s_settings = (value ?? new DioramaSettings()).Normalized(); }
        }

        // Variants already created by the game, per settings signature. An
        // entry is null until that variant is first needed.
        private static readonly Dictionary<string, object[]> s_variants = new Dictionary<string, object[]>();

        // What the prefix changed, for the postfix to give back.
        private static FieldInfo s_replacedField;
        private static object s_gameShader;
        private static object s_boundStage;

        // Focus of the previous frame (read) and of this one (written).
        private static readonly FocusBuffer[] s_focus = new FocusBuffer[2];
        private static int s_focusRead;

        // FXAA: while the effect is on, the renderer works with a copy of the
        // game's debug overrides where Fxaa is false. The game's own object is
        // never modified, and it is put back as soon as the effect is off.
        private static object s_gameOverrides;
        private static object s_ourOverrides;
        private static readonly MethodInfo s_clone =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Prefix(object[] __args)
        {
            try
            {
                if (Stop == null || Engine == null)
                    return;

                var settings = s_settings;
                if (Stop.IsStopped || !settings.Enabled || !settings.HasVisibleEffect)
                {
                    GiveFxaaBack();
                    return;
                }

                var stage = Engine.ComputeShaderStage();
                var depth = Engine.Depth();
                if (stage == null || depth == null)
                    return; // GBuffer not ready yet: this frame stays the game's

                var variant = ShaderVariants.Pick(
                    (bool)__args[Engine.EnableTonemappingIndex],
                    (bool)__args[Engine.AlphaLuminanceIndex]);
                var id = VariantId(settings, variant);
                if (id == null)
                {
                    GiveFxaaBack();
                    return;
                }

                if (!FocusBuffersReady())
                    return;

                var field = Engine.Fields[(int)variant];
                s_gameShader = field.GetValue(null);
                field.SetValue(null, id);
                s_replacedField = field;

                s_boundStage = stage;
                Engine.SetSrv.Invoke(stage, new[] { (object)ShaderSource.DepthSlot, depth });
                Engine.SetSrv.Invoke(stage, new[] { (object)ShaderSource.FocusInSlot, s_focus[s_focusRead].Buffer });
                Engine.SetUav.Invoke(stage, new[] { (object)ShaderSource.FocusOutSlot, s_focus[1 - s_focusRead].Buffer });

                HoldFxaaOff();
            }
            catch (Exception e)
            {
                SafeGiveBack();
                Stop.Stop("fault on the render thread before the final colours: " + e, Texts.StopFault);
            }
        }

        public static void Postfix()
        {
            try
            {
                if (GiveBack())
                    s_focusRead = 1 - s_focusRead;
            }
            catch (Exception e)
            {
                Stop.Stop("fault on the render thread after the final colours: " + e, Texts.StopFault);
            }
        }

        // The game's pass threw: the postfix did not run. The game still gets
        // its shaders back, and the exception goes on its way.
        public static Exception Finalizer(Exception __exception)
        {
            if (__exception != null && s_replacedField != null)
            {
                SafeGiveBack();
                if (Stop != null)
                    Stop.Stop("the final colour pass failed with our variant: " + __exception, Texts.StopFault);
            }
            return __exception;
        }

        private static void SafeGiveBack()
        {
            try { GiveBack(); }
            catch (Exception) { }
            try { GiveFxaaBack(); }
            catch (Exception) { }
        }

        // True if something was bound for this frame.
        private static bool GiveBack()
        {
            if (s_replacedField != null)
            {
                var field = s_replacedField;
                s_replacedField = null;
                field.SetValue(null, s_gameShader);
                s_gameShader = null;
            }

            if (s_boundStage == null)
                return false;

            var stage = s_boundStage;
            s_boundStage = null;
            Engine.SetSrv.Invoke(stage, new object[] { ShaderSource.DepthSlot, null });
            Engine.SetSrv.Invoke(stage, new object[] { ShaderSource.FocusInSlot, null });
            Engine.SetUav.Invoke(stage, new object[] { ShaderSource.FocusOutSlot, null });
            return true;
        }

        private static void HoldFxaaOff()
        {
            var current = Engine.DebugOverrides.GetValue(null);
            if (current == null || ReferenceEquals(current, s_ourOverrides))
                return;

            // The game has (re)installed its own overrides.
            s_gameOverrides = current;
            s_ourOverrides = null;
            if (!(bool)Engine.Fxaa.GetValue(current))
                return;

            var copy = s_clone.Invoke(current, null);
            Engine.Fxaa.SetValue(copy, false);
            Engine.DebugOverrides.SetValue(null, copy);
            s_ourOverrides = copy;
        }

        private static void GiveFxaaBack()
        {
            if (s_ourOverrides == null || Engine == null)
                return;

            var current = Engine.DebugOverrides.GetValue(null);
            if (ReferenceEquals(current, s_ourOverrides))
                Engine.DebugOverrides.SetValue(null, s_gameOverrides);
            s_ourOverrides = null;
            s_gameOverrides = null;
        }

        private static bool FocusBuffersReady()
        {
            for (var i = 0; i < s_focus.Length; i++)
            {
                if (Engine.IsAlive(s_focus[i]))
                    continue;

                s_focus[i] = Engine.CreateFocusBuffer("SirDiorama.Focus" + i);
                if (s_focus[i] == null)
                {
                    Stop.Stop("could not create the focus buffers", Texts.StopFault);
                    return false;
                }
            }
            return true;
        }

        // The variant for these settings, compiled by the game the first time
        // it is needed. Null if it is refused: the effect is then stopped for
        // the session, never drawn half way.
        private static object VariantId(DioramaSettings settings, Variant variant)
        {
            var signature = ShaderVariants.Signature(settings);
            object[] ids;
            if (!s_variants.TryGetValue(signature, out ids))
            {
                ids = new object[ShaderVariants.VariantCount];
                s_variants[signature] = ids;
            }
            if (ids[(int)variant] != null)
                return ids[(int)variant];

            var definitions = ShaderVariants.Macros(variant, settings);
            var macros = RenderEngine.ToSharpDX(definitions);
            var description = variant + " (" + string.Join(" ", definitions) + ")";

            // First the game's compiler, which refuses without crashing: null,
            // or an exception from the HLSL compiler.
            byte[] code;
            try
            {
                code = (byte[])Engine.Compile.Invoke(null, new object[]
                {
                    ShaderPath, macros, MyShaderProfile.cs_5_0, ShaderPath, false,
                });
            }
            catch (TargetInvocationException e)
            {
                Stop.Stop("variant " + description + " refused by the game's compiler: " + e.InnerException,
                    Texts.StopVariantRefused);
                return null;
            }

            if (code == null || code.Length == 0)
            {
                Stop.Stop("variant " + description + " refused by the game's compiler (details in the render log)",
                    Texts.StopVariantRefused);
                return null;
            }

            // Then the game creates it, finding it in its own cache.
            try
            {
                ids[(int)variant] = Engine.Create.Invoke(null, new object[] { ShaderPath, macros });
            }
            catch (TargetInvocationException e)
            {
                Stop.Stop("variant " + description + " refused at creation: " + e.InnerException,
                    Texts.StopVariantRefused);
                return null;
            }
            return ids[(int)variant];
        }

        // Checked before the patch is applied: a missing header would make the
        // compilation fail in the game's preprocessor.
        public static string MissingHeader(string shadersFolder)
        {
            foreach (var header in ShaderSource.GameHeaders)
            {
                if (!File.Exists(Path.Combine(shadersFolder, header)))
                    return header;
            }
            return null;
        }
    }
}
