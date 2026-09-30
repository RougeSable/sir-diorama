using System;
using System.IO;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.FileSystem;
using VRage.Input;
using VRage.Plugins;
using VRage.Utils;
using VRageRender;

namespace SirDiorama
{
    // Sir Diorama, a player side plugin loaded by Pulsar: the world in the
    // manner of Minecraft. It only changes the look of the game on the
    // player's own machine: nothing goes through the server, and a player
    // without the plugin sees the game's rendering.
    public class DioramaPlugin : IPlugin
    {
        public const string Id = "sir-diorama";

        // How many updates between two looks at who else is hooked on the
        // final colour pass (60 updates per second).
        private const int CoexistenceInterval = 600;

        // How many updates between two choices of what the texels are
        // fastened to.
        private const int AnchorInterval = 10;

        // How many updates between two looks for the LCD screens nearby.
        private const int ScreenInterval = 30;

        private const string Step = "MyToneMapping.Run";

        private Harmony m_harmony;
        private RenderEngine m_engine;
        private SessionStop m_stop;
        private Coexistence m_coexistence;
        private readonly AnchorPicker m_anchors = new AnchorPicker();
        private readonly ScreenPicker m_screens = new ScreenPicker();
        private string m_settingsPath;
        private bool m_patched;
        private bool m_commandHooked;
        private int m_coexistenceCounter;
        private int m_anchorCounter;
        private int m_screenCounter;

        public static DioramaPlugin Instance { get; private set; }

        public SessionStop Stop
        {
            get { return m_stop; }
        }

        public LookSettings Settings
        {
            get { return FinalColourPass.CurrentSettings.Copy(); }
        }

        public static void Log(string text)
        {
            MyLog.Default.WriteLine("[" + Id + "] " + text);
        }

        public void Init(object gameInstance)
        {
            Instance = this;
            m_stop = new SessionStop(Log);
            m_coexistence = new Coexistence(Id, m_stop);
            FinalColourPass.Stop = m_stop;

            // Player's settings folder: %AppData%\SpaceEngineers\Storage\sir-diorama
            var folder = Path.Combine(MyFileSystem.UserDataPath, "Storage", Id);
            m_settingsPath = Path.Combine(folder, SettingsFile.FileName);

            string problem;
            FinalColourPass.CurrentSettings = SettingsFile.Load(m_settingsPath, out problem);
            if (problem != null)
                Log(problem + "; using the defaults");

            Log("loaded, blocky look " + (FinalColourPass.CurrentSettings.Enabled ? "on" : "off")
                + ", shortcut " + FinalColourPass.CurrentSettings.Hotkey);

            try
            {
                Prepare(folder);
            }
            catch (Exception e)
            {
                m_stop.Stop("could not get ready: " + e, Texts.StopPatch);
            }
        }

        private void Prepare(string folder)
        {
            string missing;
            m_engine = RenderEngine.Resolve(out missing);
            if (m_engine == null)
            {
                m_stop.Stop("unexpected render engine, missing: " + missing, Texts.StopUnexpectedEngine);
                return;
            }
            FinalColourPass.Engine = m_engine;

            var header = FinalColourPass.MissingHeader(MyShaderCompiler.ShadersPath);
            if (header != null)
            {
                m_stop.Stop("game header not found: " + header + " in " + MyShaderCompiler.ShadersPath,
                    Texts.StopMissingHeader);
                return;
            }

            var shaderPath = Path.Combine(folder, "Shaders", ShaderSource.FileName);
            try
            {
                WriteShader(shaderPath);
            }
            catch (Exception e)
            {
                m_stop.Stop("could not write " + shaderPath + ": " + e.Message, Texts.StopShaderFile);
                return;
            }
            FinalColourPass.ShaderPath = shaderPath;

            // Two plugins never fight over the same step of the game: if
            // another one (Sir Cel Shading, for instance) got there first, we
            // yield and say so.
            if (YieldIfTaken())
                return;

            m_harmony = new Harmony(Id);
            var pass = typeof(FinalColourPass);
            m_harmony.Patch(m_engine.Run,
                prefix: new HarmonyMethod(pass.GetMethod("Prefix")),
                postfix: new HarmonyMethod(pass.GetMethod("Postfix")),
                finalizer: new HarmonyMethod(pass.GetMethod("Finalizer")));
            m_patched = true;
            Log("hooked on " + Step + ", depth in t" + ShaderSource.DepthSlot
                + ", constants in b" + ShaderSource.ConstantsSlot + ", effect written to " + shaderPath);
        }

        private static void WriteShader(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var text = ShaderSource.Text;
            if (File.Exists(path) && File.ReadAllText(path) == text)
                return;
            File.WriteAllText(path, text);
        }

        // True if another plugin is hooked on the final colour pass: the
        // effect then stops for the session. Our own patch, if applied, stays
        // inert (removing it while the render thread runs it could leave a
        // game field replaced).
        private bool YieldIfTaken()
        {
            var info = Harmony.GetPatchInfo(m_engine.Run);
            return m_coexistence.YieldIfTaken(info == null ? null : info.Owners, Step);
        }

        public void Update()
        {
            try
            {
                var worldOpen = MyAPIGateway.Session != null && MyAPIGateway.Utilities != null;

                if (!m_commandHooked && MyAPIGateway.Utilities != null)
                {
                    MyAPIGateway.Utilities.MessageEntered += OnMessage;
                    m_commandHooked = true;
                }

                if (worldOpen)
                    CheckHotkey();

                m_stop.Deliver(worldOpen, ShowNotification);

                if (!m_patched || m_stop.IsStopped)
                    return;

                if (++m_coexistenceCounter >= CoexistenceInterval)
                {
                    m_coexistenceCounter = 0;
                    YieldIfTaken();
                }

                if (worldOpen && FinalColourPass.CurrentSettings.Enabled && ++m_anchorCounter >= AnchorInterval)
                {
                    m_anchorCounter = 0;
                    FinalColourPass.CurrentAnchor = m_anchors.Pick();
                }

                if (worldOpen && FinalColourPass.CurrentSettings.Enabled && ++m_screenCounter >= ScreenInterval)
                {
                    m_screenCounter = 0;
                    FinalColourPass.CurrentScreens = m_screens.Pick();
                }
            }
            catch (Exception e)
            {
                // A fault here must never bring the game down.
                m_stop.Stop("fault on the main thread: " + e, Texts.StopFault);
            }
        }

        // The shortcut, in play only: not while typing in the chat, nor while
        // a screen with a mouse cursor (terminal, menu) is open.
        private void CheckHotkey()
        {
            var input = MyAPIGateway.Input;
            var gui = MyAPIGateway.Gui;
            if (input == null || gui == null || gui.ChatEntryVisible || gui.IsCursorVisible)
                return;

            HotkeyBinding binding;
            MyKeys key;
            if (!HotkeyBinding.TryParse(FinalColourPass.CurrentSettings.Hotkey, out binding)
                || !Enum.TryParse(binding.Key, out key)
                || !input.IsNewKeyPressed(key))
                return;

            if (!binding.Matches(binding.Key, input.IsAnyCtrlKeyPressed(), input.IsAnyAltKeyPressed(), input.IsAnyShiftKeyPressed()))
                return;

            var settings = Settings;
            settings.Enabled = !settings.Enabled;
            Apply(settings);
            m_stop.Notify(Texts.Status(settings.Enabled, m_stop.Reason, settings.Hotkey));
        }

        private static void ShowNotification(string message)
        {
            var red = message.StartsWith(Texts.StopPrefix, StringComparison.Ordinal);
            MyAPIGateway.Utilities.ShowNotification(message, red ? 10000 : 3000, red ? "Red" : "White");
        }

        private void OnMessage(string text, ref bool sendToOthers)
        {
            string argument;
            var action = ChatCommand.Parse(text, out argument);
            if (action == CommandAction.None)
                return;

            // The command stays on the player's machine.
            sendToOthers = false;

            var settings = Settings;
            switch (action)
            {
                case CommandAction.Toggle:
                    settings.Enabled = !settings.Enabled;
                    break;
                case CommandAction.On:
                    settings.Enabled = true;
                    break;
                case CommandAction.Off:
                    settings.Enabled = false;
                    break;
                case CommandAction.Status:
                    m_stop.Notify(Texts.Status(settings.Enabled, m_stop.Reason, settings.Hotkey));
                    return;
                case CommandAction.SetHotkey:
                    HotkeyBinding binding;
                    if (!HotkeyBinding.TryParse(argument, out binding))
                    {
                        m_stop.Notify(Texts.HotkeyInvalid(argument));
                        return;
                    }
                    settings.Hotkey = binding.ToString();
                    Apply(settings);
                    m_stop.Notify(Texts.HotkeyChanged(settings.Hotkey));
                    return;
                default:
                    m_stop.Notify(Texts.CommandHelp);
                    return;
            }

            Apply(settings);
            m_stop.Notify(Texts.Status(settings.Enabled, m_stop.Reason, settings.Hotkey));
        }

        // Taken into account at once, without restarting the game: the next
        // frame is drawn with the new settings.
        public void Apply(LookSettings settings)
        {
            FinalColourPass.CurrentSettings = settings;
            m_anchorCounter = AnchorInterval;
            m_screenCounter = ScreenInterval;
            try
            {
                SettingsFile.Save(m_settingsPath, FinalColourPass.CurrentSettings);
            }
            catch (Exception e)
            {
                Log("settings not saved: " + e.Message);
            }
        }

        // Called by Pulsar, from the plugin's settings button.
        public void OpenConfigDialog()
        {
            MyGuiSandbox.AddScreen(new SettingsScreen(this));
        }

        public void Dispose()
        {
            try
            {
                if (m_commandHooked && MyAPIGateway.Utilities != null)
                    MyAPIGateway.Utilities.MessageEntered -= OnMessage;
                m_commandHooked = false;

                // Only our patches, never anyone else's.
                if (m_harmony != null)
                    m_harmony.UnpatchAll(Id);
            }
            catch (Exception e)
            {
                Log("incomplete shutdown: " + e.Message);
            }
            Instance = null;
        }
    }
}
