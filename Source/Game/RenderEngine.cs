using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using VRageMath;
using VRageRender;

namespace SirDiorama
{
    // Everything the plugin uses from the client's render engine
    // (VRage.Render11 and VRage.Render), resolved by reflection once and for
    // all. A single missing name, or an unexpected shape, and Resolve returns
    // null with the name at fault: the effect stops for the session without
    // touching anything.
    //
    // Names read from the decompiled VRage.Render11.dll and VRage.Render.dll
    // of the game.
    internal sealed class RenderEngine
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // VRageRender.MyToneMapping.Run(ISrvBindable, ISrvBindable, ISrvBindable, bool, string, bool)
        public MethodInfo Run;
        public int EnableTonemappingIndex;
        public int AlphaLuminanceIndex;

        // m_cs, m_csAlphaLuminance, m_csSkip: MyComputeShaders.Id values.
        public FieldInfo[] Fields;

        // MyShaderCompiler.Compile(string, ShaderMacro[], MyShaderProfile, string, bool): byte[]
        // Returns null when the shader is refused, without crashing.
        public MethodInfo Compile;

        // MyComputeShaders.Create(string, ShaderMacro[]): MyComputeShaders.Id
        public MethodInfo Create;

        // MyRender11.RC: its DeviceContext, and its ComputeShader stage with
        // SetSrv(int, ISrvBindable) and SetConstantBuffer(int, IConstantBuffer).
        private PropertyInfo m_renderContext;
        private PropertyInfo m_deviceContext;
        private PropertyInfo m_computeStage;
        public MethodInfo SetSrv;
        public MethodInfo SetConstantBuffer;

        // MyGBuffer.Main.ResolvedDepthStencil.SrvDepth
        private FieldInfo m_mainGBuffer;
        private PropertyInfo m_resolvedDepth;
        private PropertyInfo m_depthView;

        // MyManagers.Buffers.CreateConstantBuffer(name, byteSize, data, usage, isGlobal),
        // and IBuffer.Buffer, the Direct3D buffer behind it.
        private FieldInfo m_buffers;
        private MethodInfo m_createConstantBuffer;
        private PropertyInfo m_bufferOf;

        // MyRender11.Environment.Matrices.CameraPosition: the camera of the
        // frame being drawn, in double precision.
        private FieldInfo m_environment;
        private FieldInfo m_matrices;
        private FieldInfo m_cameraPosition;

        // VRage.Render.Scene: MyIDTracker<MyActor>.FindByID(uint), then
        // MyActor.UpdateWorldMatrix() and MyActor.LastWorldMatrix: where a
        // render object is drawn in this very frame.
        private MethodInfo m_findActor;
        private MethodInfo m_updateActorMatrix;
        private PropertyInfo m_actorMatrix;

        // MyRender11.m_debugOverrides, whose Fxaa field lets FXAA run.
        public FieldInfo DebugOverrides;
        public FieldInfo Fxaa;

        public static RenderEngine Resolve(out string missing)
        {
            missing = null;
            var r = new RenderEngine();
            var render11 = typeof(MyShaderCompiler).Assembly;
            var render = typeof(MyRenderProxy).Assembly;

            var toneMapping = render11.GetType("VRageRender.MyToneMapping");
            if (toneMapping == null) { missing = "VRageRender.MyToneMapping"; return null; }

            r.Run = toneMapping.GetMethod("Run", Static);
            if (r.Run == null) { missing = "MyToneMapping.Run"; return null; }

            var parameters = r.Run.GetParameters();
            if (parameters.Length != 6
                || parameters[3].ParameterType != typeof(bool)
                || parameters[5].ParameterType != typeof(bool))
            {
                missing = "MyToneMapping.Run(src, avgLum, bloom, bool enableTonemapping, string, bool needsAlphaLuminance)";
                return null;
            }
            r.EnableTonemappingIndex = 3;
            r.AlphaLuminanceIndex = 5;

            var computeShaders = render11.GetType("VRageRender.MyComputeShaders");
            var idType = render11.GetType("VRageRender.MyComputeShaders+Id");
            if (computeShaders == null || idType == null) { missing = "VRageRender.MyComputeShaders.Id"; return null; }

            r.Fields = new FieldInfo[ShaderVariants.VariantCount];
            for (var i = 0; i < r.Fields.Length; i++)
            {
                var name = ShaderVariants.GameFields[i];
                r.Fields[i] = toneMapping.GetField(name, Static);
                if (r.Fields[i] == null || r.Fields[i].IsInitOnly || r.Fields[i].FieldType != idType)
                {
                    missing = "MyToneMapping." + name + " of type MyComputeShaders.Id";
                    return null;
                }
            }

            r.Create = computeShaders.GetMethod("Create", Static, null,
                new[] { typeof(string), typeof(ShaderMacro[]) }, null);
            if (r.Create == null || r.Create.ReturnType != idType) { missing = "MyComputeShaders.Create(string, ShaderMacro[])"; return null; }

            r.Compile = typeof(MyShaderCompiler).GetMethod("Compile", Static, null,
                new[] { typeof(string), typeof(ShaderMacro[]), typeof(MyShaderProfile), typeof(string), typeof(bool) }, null);
            if (r.Compile == null || r.Compile.ReturnType != typeof(byte[])) { missing = "MyShaderCompiler.Compile(string, ShaderMacro[], MyShaderProfile, string, bool)"; return null; }

            var myRender11 = render11.GetType("VRageRender.MyRender11");
            r.m_renderContext = myRender11 == null ? null : myRender11.GetProperty("RC", Static);
            if (r.m_renderContext == null) { missing = "MyRender11.RC"; return null; }

            r.m_deviceContext = r.m_renderContext.PropertyType.GetProperty("DeviceContext", Instance);
            if (r.m_deviceContext == null || !typeof(DeviceContext).IsAssignableFrom(r.m_deviceContext.PropertyType))
            {
                missing = "MyRenderContext.DeviceContext";
                return null;
            }

            r.m_computeStage = r.m_renderContext.PropertyType.GetProperty("ComputeShader", Instance);
            if (r.m_computeStage == null) { missing = "MyRenderContext.ComputeShader"; return null; }

            var srvBindable = render11.GetType("VRage.Render11.Resources.ISrvBindable");
            var constantBuffer = render11.GetType("VRage.Render11.Resources.IConstantBuffer");
            var buffer = render11.GetType("VRage.Render11.Resources.IBuffer");
            if (srvBindable == null || constantBuffer == null || buffer == null)
            {
                missing = "VRage.Render11.Resources.ISrvBindable, IConstantBuffer, IBuffer";
                return null;
            }

            r.SetSrv = r.m_computeStage.PropertyType.GetMethod("SetSrv", Instance, null,
                new[] { typeof(int), srvBindable }, null);
            if (r.SetSrv == null) { missing = "MyComputeStage.SetSrv(int, ISrvBindable)"; return null; }

            r.SetConstantBuffer = r.m_computeStage.PropertyType.GetMethod("SetConstantBuffer", Instance, null,
                new[] { typeof(int), constantBuffer }, null);
            if (r.SetConstantBuffer == null) { missing = "MyComputeStage.SetConstantBuffer(int, IConstantBuffer)"; return null; }

            r.m_bufferOf = buffer.GetProperty("Buffer", Instance);
            if (r.m_bufferOf == null || r.m_bufferOf.PropertyType != typeof(SharpDX.Direct3D11.Buffer))
            {
                missing = "IBuffer.Buffer";
                return null;
            }

            var gbuffer = render11.GetType("VRage.Render11.Resources.MyGBuffer");
            r.m_mainGBuffer = gbuffer == null ? null : gbuffer.GetField("Main", Static);
            if (r.m_mainGBuffer == null) { missing = "MyGBuffer.Main"; return null; }

            r.m_resolvedDepth = gbuffer.GetProperty("ResolvedDepthStencil", Instance);
            if (r.m_resolvedDepth == null) { missing = "MyGBuffer.ResolvedDepthStencil"; return null; }

            // Declared on the IDepthStencil interface.
            r.m_depthView = r.m_resolvedDepth.PropertyType.GetProperty("SrvDepth", Instance);
            if (r.m_depthView == null || !srvBindable.IsAssignableFrom(r.m_depthView.PropertyType))
            {
                missing = "IDepthStencil.SrvDepth";
                return null;
            }

            var managers = render11.GetType("VRage.Render11.Common.MyManagers");
            r.m_buffers = managers == null ? null : managers.GetField("Buffers", Static);
            if (r.m_buffers == null) { missing = "MyManagers.Buffers"; return null; }

            r.m_createConstantBuffer = r.m_buffers.FieldType.GetMethods(Instance).FirstOrDefault(m =>
            {
                if (m.Name != "CreateConstantBuffer" || !constantBuffer.IsAssignableFrom(m.ReturnType))
                    return false;
                var p = m.GetParameters();
                return p.Length == 5 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(int)
                    && p[2].ParameterType == typeof(IntPtr?) && p[3].ParameterType == typeof(ResourceUsage)
                    && p[4].ParameterType == typeof(bool);
            });
            if (r.m_createConstantBuffer == null) { missing = "MyBufferManager.CreateConstantBuffer(string, int, IntPtr?, ResourceUsage, bool)"; return null; }

            r.m_environment = myRender11.GetField("Environment", Static);
            r.m_matrices = r.m_environment == null ? null : r.m_environment.FieldType.GetField("Matrices", Instance);
            r.m_cameraPosition = r.m_matrices == null ? null : r.m_matrices.FieldType.GetField("CameraPosition", Instance);
            if (r.m_cameraPosition == null || r.m_cameraPosition.FieldType != typeof(Vector3D))
            {
                missing = "MyRender11.Environment.Matrices.CameraPosition";
                return null;
            }

            var actor = render.GetType("VRage.Render.Scene.MyActor");
            var tracker = render.GetType("VRage.Render.Scene.MyIDTracker`1");
            if (actor == null || tracker == null) { missing = "VRage.Render.Scene.MyActor, MyIDTracker"; return null; }

            r.m_findActor = tracker.MakeGenericType(actor).GetMethod("FindByID", Static, null, new[] { typeof(uint) }, null);
            if (r.m_findActor == null || r.m_findActor.ReturnType != actor) { missing = "MyIDTracker<MyActor>.FindByID(uint)"; return null; }

            r.m_updateActorMatrix = actor.GetMethod("UpdateWorldMatrix", Instance, null, Type.EmptyTypes, null);
            r.m_actorMatrix = actor.GetProperty("LastWorldMatrix", Instance);
            if (r.m_updateActorMatrix == null || r.m_actorMatrix == null || r.m_actorMatrix.PropertyType != typeof(MatrixD))
            {
                missing = "MyActor.UpdateWorldMatrix(), MyActor.LastWorldMatrix";
                return null;
            }

            r.DebugOverrides = myRender11.GetField("m_debugOverrides", Static);
            if (r.DebugOverrides == null) { missing = "MyRender11.m_debugOverrides"; return null; }

            r.Fxaa = r.DebugOverrides.FieldType.GetField("Fxaa", Instance);
            if (r.Fxaa == null || r.Fxaa.FieldType != typeof(bool)) { missing = "MyRenderDebugOverrides.Fxaa"; return null; }

            return r;
        }

        public static ShaderMacro[] ToSharpDX(IList<MacroDefinition> macros)
        {
            var result = new ShaderMacro[macros.Count];
            for (var i = 0; i < macros.Count; i++)
                result[i] = new ShaderMacro(macros[i].Name, macros[i].Value);
            return result;
        }

        // The scene depth, or null while the GBuffer is not ready.
        public object Depth()
        {
            var main = m_mainGBuffer.GetValue(null);
            if (main == null)
                return null;
            var depth = m_resolvedDepth.GetValue(main, null);
            if (depth == null)
                return null;
            return m_depthView.GetValue(depth, null);
        }

        public object RenderContext()
        {
            return m_renderContext.GetValue(null, null);
        }

        public object ComputeShaderStage(object renderContext)
        {
            return renderContext == null ? null : m_computeStage.GetValue(renderContext, null);
        }

        public Vector3D CameraPosition()
        {
            var environment = m_environment.GetValue(null);
            var matrices = environment == null ? null : m_matrices.GetValue(environment);
            return matrices == null ? Vector3D.Zero : (Vector3D)m_cameraPosition.GetValue(matrices);
        }

        // Where a render object is drawn in this frame; false if the render
        // thread does not know it (yet, or any more).
        public bool TryGetActorMatrix(uint renderObjectId, out MatrixD matrix)
        {
            matrix = MatrixD.Identity;
            var actor = m_findActor.Invoke(null, new object[] { renderObjectId });
            if (actor == null)
                return false;
            m_updateActorMatrix.Invoke(actor, null);
            matrix = (MatrixD)m_actorMatrix.GetValue(actor, null);
            return true;
        }

        // A dynamic constant buffer of this size, written by the CPU every
        // frame. Null while the game's buffer manager is not ready.
        public object CreateConstantBuffer(string name, int byteSize)
        {
            var manager = m_buffers.GetValue(null);
            if (manager == null)
                return null;
            return m_createConstantBuffer.Invoke(manager, new object[]
            {
                name, byteSize, null, ResourceUsage.Dynamic, true,
            });
        }

        // False once the Direct3D buffer behind it is gone (a device reset
        // releases every buffer): the plugin then creates a new one.
        public bool IsAlive(object constantBuffer)
        {
            if (constantBuffer == null)
                return false;
            var buffer = m_bufferOf.GetValue(constantBuffer, null) as SharpDX.Direct3D11.Buffer;
            return buffer != null && !buffer.IsDisposed && buffer.NativePointer != IntPtr.Zero;
        }

        // Writes the values into the buffer, discarding what it held: the GPU
        // keeps the previous frame's copy for as long as it needs it.
        public void Write(object renderContext, object constantBuffer, float[] values)
        {
            var context = (DeviceContext)m_deviceContext.GetValue(renderContext, null);
            var buffer = (SharpDX.Direct3D11.Buffer)m_bufferOf.GetValue(constantBuffer, null);
            var box = context.MapSubresource(buffer, 0, MapMode.WriteDiscard, MapFlags.None);
            try
            {
                Marshal.Copy(values, 0, box.DataPointer, values.Length);
            }
            finally
            {
                context.UnmapSubresource(buffer, 0);
            }
        }
    }
}
