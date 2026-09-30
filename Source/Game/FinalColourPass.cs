using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using VRageMath;
using VRageRender;

namespace SirDiorama
{
    // The prefix, postfix and finalizer put on MyToneMapping.Run, the game's
    // final colour pass (the method proven on Sir Cel Shading).
    //
    // On: the prefix puts our variant in the static field of the variant in
    // use, fills and binds the plugin's constant buffer (b6) and the scene
    // depth (t31), and keeps FXAA off; the postfix gives the field its game
    // shader back and unbinds both. Off, or stopped: nothing is touched, FXAA
    // gets its setting back, the game draws with its own shaders.
    //
    // Everything below runs on the render thread, and on it alone, except the
    // two volatile snapshots written by the main thread.
    internal static class FinalColourPass
    {
        // Set once by the plugin, before the patch is applied.
        public static RenderEngine Engine;
        public static SessionStop Stop;
        public static string ShaderPath;

        // Snapshots replaced as a whole by the main thread.
        private static volatile LookSettings s_settings = new LookSettings();
        private static volatile Anchor s_anchor = Anchor.Default;
        private static volatile ScreenSet s_screens = ScreenSet.Empty;

        public static LookSettings CurrentSettings
        {
            get { return s_settings; }
            set { s_settings = (value ?? new LookSettings()).Normalized(); }
        }

        public static Anchor CurrentAnchor
        {
            get { return s_anchor; }
            set { s_anchor = value ?? Anchor.Default; }
        }

        // The LCD screens near the player, left as the game draws them.
        public static ScreenSet CurrentScreens
        {
            get { return s_screens; }
            set { s_screens = value ?? ScreenSet.Empty; }
        }

        // The three variants, created by the game the first time each one is
        // needed, then kept for the session.
        private static readonly object[] s_variants = new object[ShaderVariants.VariantCount];

        private static object s_constants;
        private static readonly float[] s_values = new float[LookConstants.FloatCount];

        // Grid poses read for the frame being drawn, by render object.
        private static readonly Dictionary<uint, GridPose> s_poses = new Dictionary<uint, GridPose>();

        // What the prefix changed, for the postfix to give back.
        private static FieldInfo s_replacedField;
        private static object s_gameShader;
        private static object s_boundStage;

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
                if (Stop.IsStopped || !settings.Enabled)
                {
                    GiveFxaaBack();
                    return;
                }

                var context = Engine.RenderContext();
                var stage = Engine.ComputeShaderStage(context);
                var depth = Engine.Depth();
                if (stage == null || depth == null)
                    return; // GBuffer not ready yet: this frame stays the game's

                var variant = ShaderVariants.Pick(
                    (bool)__args[Engine.EnableTonemappingIndex],
                    (bool)__args[Engine.AlphaLuminanceIndex]);
                var id = VariantId(variant);
                if (id == null)
                {
                    GiveFxaaBack();
                    return;
                }

                if (!Engine.IsAlive(s_constants))
                {
                    s_constants = Engine.CreateConstantBuffer("SirDiorama.LookConstants", LookConstants.ByteSize);
                    if (!Engine.IsAlive(s_constants))
                        return; // buffer manager not ready: this frame stays the game's
                }
                FillConstants(settings);
                Engine.Write(context, s_constants, s_values);

                var field = Engine.Fields[(int)variant];
                s_gameShader = field.GetValue(null);
                field.SetValue(null, id);
                s_replacedField = field;

                s_boundStage = stage;
                Engine.SetSrv.Invoke(stage, new[] { (object)ShaderSource.DepthSlot, depth });
                Engine.SetConstantBuffer.Invoke(stage, new[] { (object)ShaderSource.ConstantsSlot, s_constants });

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
                GiveBack();
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

        // The frames the texels are fastened to, for the very frame being
        // drawn: the grids' matrices are read from the render thread's own
        // copy, and the camera is the one of this frame. Everything is
        // computed in double precision, then handed to the shader relative to
        // the camera: the frame of the surroundings, the grid's frame with its
        // box, which only the points inside that box take, and the boxes of
        // the LCD screens near the player.
        private static void FillConstants(LookSettings settings)
        {
            s_poses.Clear();
            var anchor = s_anchor;
            var grid = anchor.Grid;
            var pose = new GridPose(Vec3d.Zero, Vec3d.UnitX, Vec3d.UnitY, Vec3d.UnitZ);
            if (grid != null)
                pose = PoseOf(grid.RenderObjectId, new GridPose(grid.Origin, grid.AxisX, grid.AxisY, grid.AxisZ));

            var camera = ToVec(Engine.CameraPosition());
            LookConstants.Pack(s_values, anchor, camera, pose.Origin, pose.X, pose.Y, pose.Z, settings);

            var count = 0;
            foreach (var screen in s_screens.Screens)
            {
                var p = PoseOf(screen.GridRenderObjectId,
                    new GridPose(screen.GridOrigin, screen.GridX, screen.GridY, screen.GridZ));
                if (LookConstants.PackScreen(s_values, count, p.Origin, p.X, p.Y, p.Z, screen.Min, screen.Max, camera))
                    count++;
            }
            LookConstants.SetScreenCount(s_values, count);
        }

        // Where a grid is drawn in this frame, read once per frame; the main
        // thread's pose if the render thread does not know it.
        private static GridPose PoseOf(uint renderObjectId, GridPose fallback)
        {
            GridPose pose;
            if (s_poses.TryGetValue(renderObjectId, out pose))
                return pose;

            pose = fallback;
            MatrixD matrix;
            if (Engine.TryGetActorMatrix(renderObjectId, out matrix))
            {
                pose = new GridPose(ToVec(matrix.Translation), ToVec(matrix.Right).Normalized(),
                    ToVec(matrix.Up).Normalized(), ToVec(matrix.Backward).Normalized());
            }
            s_poses[renderObjectId] = pose;
            return pose;
        }

        private struct GridPose
        {
            public readonly Vec3d Origin;
            public readonly Vec3d X;
            public readonly Vec3d Y;
            public readonly Vec3d Z;

            public GridPose(Vec3d origin, Vec3d x, Vec3d y, Vec3d z)
            {
                Origin = origin;
                X = x;
                Y = y;
                Z = z;
            }
        }

        private static Vec3d ToVec(Vector3D v)
        {
            return new Vec3d(v.X, v.Y, v.Z);
        }

        private static void SafeGiveBack()
        {
            try { GiveBack(); }
            catch (Exception) { }
            try { GiveFxaaBack(); }
            catch (Exception) { }
        }

        private static void GiveBack()
        {
            if (s_replacedField != null)
            {
                var field = s_replacedField;
                s_replacedField = null;
                field.SetValue(null, s_gameShader);
                s_gameShader = null;
            }

            if (s_boundStage == null)
                return;

            var stage = s_boundStage;
            s_boundStage = null;
            Engine.SetSrv.Invoke(stage, new object[] { ShaderSource.DepthSlot, null });
            Engine.SetConstantBuffer.Invoke(stage, new object[] { ShaderSource.ConstantsSlot, null });
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

        // The variant, compiled by the game the first time it is needed. Null
        // if it is refused: the effect is then stopped for the session, never
        // drawn half way.
        private static object VariantId(Variant variant)
        {
            if (s_variants[(int)variant] != null)
                return s_variants[(int)variant];

            var definitions = ShaderVariants.Macros(variant);
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
                s_variants[(int)variant] = Engine.Create.Invoke(null, new object[] { ShaderPath, macros });
            }
            catch (TargetInvocationException e)
            {
                Stop.Stop("variant " + description + " refused at creation: " + e.InnerException,
                    Texts.StopVariantRefused);
                return null;
            }
            return s_variants[(int)variant];
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
