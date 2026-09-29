using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using SharpDX.Direct3D;
using VRageRender;

namespace SirDiorama
{
    // Everything the plugin uses from the client's render engine
    // (VRage.Render11), resolved by reflection once and for all. A single
    // missing name, or an unexpected shape, and Resolve returns null with the
    // name at fault: the effect stops for the session without touching
    // anything.
    //
    // Names read from the decompiled VRage.Render11.dll of the game.
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
        public Type IdType;

        // MyShaderCompiler.Compile(string, ShaderMacro[], MyShaderProfile, string, bool): byte[]
        // Returns null when the shader is refused, without crashing.
        public MethodInfo Compile;

        // MyComputeShaders.Create(string, ShaderMacro[]): MyComputeShaders.Id
        public MethodInfo Create;

        // MyRender11.RC.ComputeShader: SetSrv(int, ISrvBindable), SetUav(int, IUavBindable)
        public PropertyInfo RenderContext;
        public PropertyInfo ComputeStage;
        public MethodInfo SetSrv;
        public MethodInfo SetUav;

        // MyGBuffer.Main.ResolvedDepthStencil.SrvDepth
        public FieldInfo MainGBuffer;
        public PropertyInfo ResolvedDepth;
        public PropertyInfo DepthView;

        // MyManagers.Buffers.CreateSrvUav(name, elements, stride, data, uavType, usage, isGlobal)
        public FieldInfo Buffers;
        public MethodInfo CreateSrvUav;
        public PropertyInfo SrvOf;
        public PropertyInfo UavOf;

        // MyRender11.m_debugOverrides, whose Fxaa field lets FXAA run.
        public FieldInfo DebugOverrides;
        public FieldInfo Fxaa;

        public static RenderEngine Resolve(out string missing)
        {
            missing = null;
            var r = new RenderEngine();
            var render = typeof(MyShaderCompiler).Assembly;

            var toneMapping = render.GetType("VRageRender.MyToneMapping");
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

            r.Fields = new FieldInfo[ShaderVariants.VariantCount];
            for (var i = 0; i < r.Fields.Length; i++)
            {
                var name = ShaderVariants.GameFields[i];
                r.Fields[i] = toneMapping.GetField(name, Static);
                if (r.Fields[i] == null || r.Fields[i].IsInitOnly) { missing = "MyToneMapping." + name; return null; }
            }

            var computeShaders = render.GetType("VRageRender.MyComputeShaders");
            r.IdType = render.GetType("VRageRender.MyComputeShaders+Id");
            if (computeShaders == null || r.IdType == null) { missing = "VRageRender.MyComputeShaders.Id"; return null; }
            if (r.Fields.Any(f => f.FieldType != r.IdType)) { missing = "MyToneMapping.m_cs of type MyComputeShaders.Id"; return null; }

            r.Create = computeShaders.GetMethod("Create", Static, null,
                new[] { typeof(string), typeof(ShaderMacro[]) }, null);
            if (r.Create == null || r.Create.ReturnType != r.IdType) { missing = "MyComputeShaders.Create(string, ShaderMacro[])"; return null; }

            r.Compile = typeof(MyShaderCompiler).GetMethod("Compile", Static, null,
                new[] { typeof(string), typeof(ShaderMacro[]), typeof(MyShaderProfile), typeof(string), typeof(bool) }, null);
            if (r.Compile == null || r.Compile.ReturnType != typeof(byte[])) { missing = "MyShaderCompiler.Compile(string, ShaderMacro[], MyShaderProfile, string, bool)"; return null; }

            var render11 = render.GetType("VRageRender.MyRender11");
            r.RenderContext = render11 == null ? null : render11.GetProperty("RC", Static);
            if (r.RenderContext == null) { missing = "MyRender11.RC"; return null; }

            r.ComputeStage = r.RenderContext.PropertyType.GetProperty("ComputeShader", Instance);
            if (r.ComputeStage == null) { missing = "MyRenderContext.ComputeShader"; return null; }

            var srvBindable = render.GetType("VRage.Render11.Resources.ISrvBindable");
            var uavBindable = render.GetType("VRage.Render11.Resources.IUavBindable");
            if (srvBindable == null || uavBindable == null) { missing = "VRage.Render11.Resources.ISrvBindable, IUavBindable"; return null; }

            r.SetSrv = r.ComputeStage.PropertyType.GetMethod("SetSrv", Instance, null,
                new[] { typeof(int), srvBindable }, null);
            if (r.SetSrv == null) { missing = "MyCommonStage.SetSrv(int, ISrvBindable)"; return null; }

            r.SetUav = r.ComputeStage.PropertyType.GetMethod("SetUav", Instance, null,
                new[] { typeof(int), uavBindable }, null);
            if (r.SetUav == null) { missing = "MyComputeStage.SetUav(int, IUavBindable)"; return null; }

            r.SrvOf = srvBindable.GetProperty("Srv", Instance);
            r.UavOf = uavBindable.GetProperty("Uav", Instance);
            if (r.SrvOf == null || r.UavOf == null) { missing = "ISrvBindable.Srv, IUavBindable.Uav"; return null; }

            var gbuffer = render.GetType("VRage.Render11.Resources.MyGBuffer");
            r.MainGBuffer = gbuffer == null ? null : gbuffer.GetField("Main", Static);
            if (r.MainGBuffer == null) { missing = "MyGBuffer.Main"; return null; }

            r.ResolvedDepth = gbuffer.GetProperty("ResolvedDepthStencil", Instance);
            if (r.ResolvedDepth == null) { missing = "MyGBuffer.ResolvedDepthStencil"; return null; }

            // Declared on the IDepthStencil interface.
            r.DepthView = r.ResolvedDepth.PropertyType.GetProperty("SrvDepth", Instance);
            if (r.DepthView == null || !srvBindable.IsAssignableFrom(r.DepthView.PropertyType))
            {
                missing = "IDepthStencil.SrvDepth";
                return null;
            }

            var managers = render.GetType("VRage.Render11.Common.MyManagers");
            r.Buffers = managers == null ? null : managers.GetField("Buffers", Static);
            if (r.Buffers == null) { missing = "MyManagers.Buffers"; return null; }

            r.CreateSrvUav = r.Buffers.FieldType.GetMethods(Instance).FirstOrDefault(m =>
            {
                if (m.Name != "CreateSrvUav")
                    return false;
                var p = m.GetParameters();
                return p.Length == 7 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(int)
                    && p[2].ParameterType == typeof(int) && p[3].ParameterType == typeof(IntPtr?)
                    && p[4].ParameterType.IsEnum && p[5].ParameterType.IsEnum && p[6].ParameterType == typeof(bool)
                    && srvBindable.IsAssignableFrom(m.ReturnType) && uavBindable.IsAssignableFrom(m.ReturnType);
            });
            if (r.CreateSrvUav == null) { missing = "MyBufferManager.CreateSrvUav(string, int, int, IntPtr?, MyUavType, ResourceUsage, bool)"; return null; }

            r.DebugOverrides = render11.GetField("m_debugOverrides", Static);
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
            var main = MainGBuffer.GetValue(null);
            if (main == null)
                return null;
            var depth = ResolvedDepth.GetValue(main, null);
            if (depth == null)
                return null;
            return DepthView.GetValue(depth, null);
        }

        public object ComputeShaderStage()
        {
            var context = RenderContext.GetValue(null, null);
            return context == null ? null : ComputeStage.GetValue(context, null);
        }

        // A structured buffer of one float2, readable and writable by a
        // compute shader, filled with zeros (no valid focus yet).
        public FocusBuffer CreateFocusBuffer(string name)
        {
            var manager = Buffers.GetValue(null);
            if (manager == null)
                return null;

            var zeros = new float[2];
            var handle = GCHandle.Alloc(zeros, GCHandleType.Pinned);
            try
            {
                var p = CreateSrvUav.GetParameters();
                var buffer = CreateSrvUav.Invoke(manager, new object[]
                {
                    name, 1, 2 * sizeof(float), (IntPtr?)handle.AddrOfPinnedObject(),
                    Enum.ToObject(p[4].ParameterType, 0), Enum.ToObject(p[5].ParameterType, 0), true,
                });
                if (buffer == null)
                    return null;
                return new FocusBuffer(buffer, SrvOf.GetValue(buffer, null), UavOf.GetValue(buffer, null));
            }
            finally
            {
                handle.Free();
            }
        }

        // False once the game has released or reused the buffer (a device
        // reset releases every buffer): the plugin then creates a new one.
        public bool IsAlive(FocusBuffer buffer)
        {
            return buffer != null && buffer.Srv != null && buffer.Uav != null
                && ReferenceEquals(SrvOf.GetValue(buffer.Buffer, null), buffer.Srv)
                && ReferenceEquals(UavOf.GetValue(buffer.Buffer, null), buffer.Uav);
        }
    }

    // One of the two focus buffers, with the views it had at creation.
    internal sealed class FocusBuffer
    {
        public readonly object Buffer;
        public readonly object Srv;
        public readonly object Uav;

        public FocusBuffer(object buffer, object srv, object uav)
        {
            Buffer = buffer;
            Srv = srv;
            Uav = uav;
        }
    }
}
