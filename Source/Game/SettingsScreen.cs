using System;
using System.Text;
using Sandbox.Graphics.GUI;
using VRage.Utils;
using VRageMath;

namespace SirDiorama
{
    // The plugin's settings, opened from Pulsar. Every change applies to the
    // next frame and is saved at once: the player sees the effect behind the
    // screen, without restarting the game. No setting compiles anything.
    internal sealed class SettingsScreen : MyGuiScreenBase
    {
        private const float Width = 0.62f;
        private const float Height = 0.72f;
        private const float LabelColumn = -0.27f;
        private const float ControlColumn = 0.10f;
        private const float ValueColumn = 0.27f;
        private const float LineHeight = 0.062f;

        // A slider being dragged changes value at every notch; the file is
        // written once the player pauses for a moment.
        private static readonly TimeSpan SliderDelay = TimeSpan.FromMilliseconds(300);

        private readonly DioramaPlugin m_plugin;
        private LookSettings m_settings;
        private DateTime? m_applyAt;

        private MyGuiControlCombobox m_hotkeyKey;
        private MyGuiControlCheckbox m_hotkeyCtrl;
        private MyGuiControlCheckbox m_hotkeyAlt;
        private MyGuiControlCheckbox m_hotkeyShift;

        public SettingsScreen(DioramaPlugin plugin)
            : base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(Width, Height), false, null, 0.9f, 0.9f)
        {
            m_plugin = plugin;
            m_settings = plugin.Settings;
            EnabledBackgroundFade = true;
            CloseButtonEnabled = true;
            RecreateControls(true);
        }

        public override string GetFriendlyName()
        {
            return "SirDioramaSettingsScreen";
        }

        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);
            AddCaption(Texts.ScreenTitle);

            var y = -Height / 2 + 0.13f;

            // First setting, always: the box that switches everything on or off.
            AddCheckbox(Texts.EnableBox, Texts.EnableBoxHelp, y, m_settings.Enabled, v => m_settings.Enabled = v);
            y += LineHeight;

            AddSlider(Texts.TexelDensity, Texts.TexelDensityHelp, y, 0, LookSettings.TexelDensities.Length - 1,
                LookSettings.DensityIndex(m_settings.TexelDensity),
                v => m_settings.TexelDensity = LookSettings.TexelDensities[v],
                v => Texts.DensityValue(LookSettings.TexelDensities[v]));
            y += LineHeight;
            AddSlider(Texts.SmallestTexel, Texts.SmallestTexelHelp, y, LookSettings.SmallestTexelMin, LookSettings.SmallestTexelMax,
                m_settings.SmallestTexel, v => m_settings.SmallestTexel = v, v => v + " px");
            y += LineHeight;
            AddSlider(Texts.ColourBoost, Texts.ColourBoostHelp, y, LookSettings.ColourBoostMin, LookSettings.ColourBoostMax,
                m_settings.ColourBoost, v => m_settings.ColourBoost = v, v => v + " %");
            y += LineHeight;

            AddHotkey(y);
            y += 2 * LineHeight;

            if (m_plugin.Stop != null && m_plugin.Stop.IsStopped)
            {
                Controls.Add(new MyGuiControlLabel(new Vector2(LabelColumn, y), null, Texts.ScreenStopped, null, 0.8f, "Red"));
            }

            var buttonsY = Height / 2 - 0.07f;
            Controls.Add(new MyGuiControlButton(new Vector2(-0.12f, buttonsY), text: new StringBuilder(Texts.DefaultsButton),
                onButtonClick: b =>
                {
                    var enabled = m_settings.Enabled;
                    m_settings = new LookSettings { Enabled = enabled };
                    Apply();
                    RecreateControls(false);
                }));
            Controls.Add(new MyGuiControlButton(new Vector2(0.12f, buttonsY), text: new StringBuilder(Texts.CloseButton),
                onButtonClick: b => CloseScreen()));
        }

        private void AddLabel(string text, string help, float y)
        {
            var label = new MyGuiControlLabel(new Vector2(LabelColumn, y), null, text);
            label.SetToolTip(help);
            Controls.Add(label);
        }

        private void AddCheckbox(string text, string help, float y, bool value, Action<bool> set)
        {
            AddLabel(text, help, y);
            var box = new MyGuiControlCheckbox(new Vector2(ControlColumn, y), null, help, value);
            box.IsCheckedChanged = c =>
            {
                set(c.IsChecked);
                Apply();
            };
            Controls.Add(box);
        }

        private void AddSlider(string text, string help, float y, int min, int max, int value,
            Action<int> set, Func<int, string> display)
        {
            AddLabel(text, help, y);

            var shown = new MyGuiControlLabel(new Vector2(ValueColumn, y), null, display(value),
                null, 0.8f, "White", MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);

            var slider = new MyGuiControlSlider(new Vector2(ControlColumn, y), min, max, 0.2f, value,
                toolTip: help, intValue: true);
            slider.Value = value;
            slider.ValueChanged = c =>
            {
                var v = (int)Math.Round(c.Value);
                shown.Text = display(v);
                set(v);
                // Seen at once; saved once the slider rests.
                FinalColourPassPreview();
                m_applyAt = DateTime.UtcNow + SliderDelay;
            };

            Controls.Add(slider);
            Controls.Add(shown);
        }

        private void FinalColourPassPreview()
        {
            FinalColourPass.CurrentSettings = m_settings.Copy();
        }

        // The shortcut: one key, and its modifiers on the line below. It must
        // differ from Sir Cel Shading's, and from the game's own keys.
        private void AddHotkey(float y)
        {
            HotkeyBinding binding;
            if (!HotkeyBinding.TryParse(m_settings.Hotkey, out binding))
                HotkeyBinding.TryParse(LookSettings.HotkeyDefault, out binding);

            AddLabel(Texts.Hotkey, Texts.HotkeyHelp, y);
            m_hotkeyKey = new MyGuiControlCombobox(new Vector2(ControlColumn, y), new Vector2(0.2f, 0.04f), toolTip: Texts.HotkeyHelp);
            for (var i = 0; i < HotkeyBinding.Keys.Length; i++)
                m_hotkeyKey.AddItem(i, HotkeyBinding.Keys[i], sort: false);
            m_hotkeyKey.SelectItemByKey(Math.Max(0, Array.IndexOf(HotkeyBinding.Keys, binding.Key)), false);
            m_hotkeyKey.ItemSelected += OnHotkeyChanged;
            Controls.Add(m_hotkeyKey);

            var y2 = y + LineHeight;
            m_hotkeyCtrl = AddModifier(Texts.CtrlBox, ControlColumn - 0.12f, y2, binding.Ctrl);
            m_hotkeyAlt = AddModifier(Texts.AltBox, ControlColumn, y2, binding.Alt);
            m_hotkeyShift = AddModifier(Texts.ShiftBox, ControlColumn + 0.12f, y2, binding.Shift);
        }

        private MyGuiControlCheckbox AddModifier(string text, float x, float y, bool value)
        {
            var label = new MyGuiControlLabel(new Vector2(x - 0.035f, y), null, text, null, 0.8f, "White",
                MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
            label.SetToolTip(Texts.HotkeyHelp);
            Controls.Add(label);

            var box = new MyGuiControlCheckbox(new Vector2(x, y), null, Texts.HotkeyHelp, value);
            box.IsCheckedChanged = c => OnHotkeyChanged();
            Controls.Add(box);
            return box;
        }

        private void OnHotkeyChanged()
        {
            if (m_hotkeyKey == null || m_hotkeyCtrl == null || m_hotkeyAlt == null || m_hotkeyShift == null)
                return;

            var index = (int)m_hotkeyKey.GetSelectedKey();
            if (index < 0 || index >= HotkeyBinding.Keys.Length)
                return;

            m_settings.Hotkey = new HotkeyBinding(HotkeyBinding.Keys[index],
                m_hotkeyCtrl.IsChecked, m_hotkeyAlt.IsChecked, m_hotkeyShift.IsChecked).ToString();
            Apply();
        }

        public override bool Update(bool hasFocus)
        {
            if (m_applyAt.HasValue && DateTime.UtcNow >= m_applyAt.Value)
                Apply();
            return base.Update(hasFocus);
        }

        public override bool CloseScreen(bool isUnloading = false)
        {
            if (m_applyAt.HasValue)
                Apply();
            return base.CloseScreen(isUnloading);
        }

        private void Apply()
        {
            m_applyAt = null;
            m_plugin.Apply(m_settings.Copy());
        }
    }
}
