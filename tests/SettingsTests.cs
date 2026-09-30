using System;
using System.IO;
using Xunit;

namespace SirDiorama.Tests
{
    public class SettingsTests
    {
        [Fact]
        public void TheDefaultsAreMinecraftLike()
        {
            var s = new LookSettings();
            Assert.True(s.Enabled);
            // The values found closest to Minecraft in game.
            Assert.Equal(8, s.TexelDensity);
            Assert.Equal(1.0 / 8, s.TexelSize);
            Assert.Equal(4, s.SmallestTexel);
            Assert.Equal(75, s.ColourBoost);
            Assert.Equal("Alt+F2", s.Hotkey);
        }

        [Fact]
        public void OutOfBoundsValuesAreBroughtBack()
        {
            var s = new LookSettings { TexelDensity = 1000, SmallestTexel = 0, ColourBoost = 400, Hotkey = "nonsense" }.Normalized();
            Assert.Equal(32, s.TexelDensity);
            Assert.Equal(LookSettings.SmallestTexelMin, s.SmallestTexel);
            Assert.Equal(LookSettings.ColourBoostMax, s.ColourBoost);
            Assert.Equal(LookSettings.HotkeyDefault, s.Hotkey);

            Assert.Equal(4, new LookSettings { TexelDensity = -3 }.Normalized().TexelDensity);
            Assert.Equal(8, new LookSettings { TexelDensity = 9 }.Normalized().TexelDensity);
            Assert.Equal(2, LookSettings.DensityIndex(16));
        }

        [Fact]
        public void SettingsSurviveTheFile()
        {
            var folder = Path.Combine(Path.GetTempPath(), "sir-diorama-tests-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(folder, SettingsFile.FileName);
            try
            {
                var saved = new LookSettings { Enabled = false, TexelDensity = 8, SmallestTexel = 6, ColourBoost = 0, Hotkey = "Ctrl+F9" };
                SettingsFile.Save(path, saved);

                string problem;
                var read = SettingsFile.Load(path, out problem);
                Assert.Null(problem);
                Assert.False(read.Enabled);
                Assert.Equal(8, read.TexelDensity);
                Assert.Equal(6, read.SmallestTexel);
                Assert.Equal(0, read.ColourBoost);
                Assert.Equal("Ctrl+F9", read.Hotkey);
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void AMissingOrBrokenFileGivesTheDefaults()
        {
            var folder = Path.Combine(Path.GetTempPath(), "sir-diorama-tests-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(folder, SettingsFile.FileName);
            try
            {
                string problem;
                Assert.True(SettingsFile.Load(path, out problem).Enabled);
                Assert.Null(problem);

                Directory.CreateDirectory(folder);
                File.WriteAllText(path, "<not xml");
                var read = SettingsFile.Load(path, out problem);
                Assert.NotNull(problem);
                Assert.Equal(LookSettings.TexelDensityDefault, read.TexelDensity);
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }
    }

    public class HotkeyTests
    {
        [Fact]
        public void ModifiersMustMatchExactly()
        {
            HotkeyBinding binding;
            Assert.True(HotkeyBinding.TryParse("Alt+F2", out binding));
            Assert.True(binding.Matches("F2", false, true, false));
            Assert.False(binding.Matches("F2", true, false, false));
            Assert.False(binding.Matches("F2", true, true, false));
            Assert.False(binding.Matches("F2", false, false, false));
            Assert.False(binding.Matches("F3", false, true, false));
        }

        [Fact]
        public void ShortcutsAreReadAndWrittenTheSameWay()
        {
            HotkeyBinding binding;
            Assert.True(HotkeyBinding.TryParse(" shift + ctrl+f9 ", out binding));
            Assert.Equal("Ctrl+Shift+F9", binding.ToString());

            Assert.False(HotkeyBinding.TryParse("", out binding));
            Assert.False(HotkeyBinding.TryParse("Alt", out binding));
            Assert.False(HotkeyBinding.TryParse("Alt+W", out binding));
            Assert.False(HotkeyBinding.TryParse("F1+F2", out binding));
            Assert.False(HotkeyBinding.TryParse("Alt+Alt+F2", out binding));
        }
    }

    public class ChatCommandTests
    {
        [Fact]
        public void TheCommandIsRecognised()
        {
            string argument;
            Assert.Equal(CommandAction.Toggle, ChatCommand.Parse("/diorama", out argument));
            Assert.Equal(CommandAction.On, ChatCommand.Parse("/Diorama ON", out argument));
            Assert.Equal(CommandAction.Off, ChatCommand.Parse("  /diorama off ", out argument));
            Assert.Equal(CommandAction.Status, ChatCommand.Parse("/diorama status", out argument));
            Assert.Equal(CommandAction.SetHotkey, ChatCommand.Parse("/diorama key Ctrl+F9", out argument));
            Assert.Equal("Ctrl+F9", argument);
            Assert.Equal(CommandAction.Unknown, ChatCommand.Parse("/diorama what", out argument));
        }

        [Fact]
        public void OtherMessagesAreLeftAlone()
        {
            string argument;
            Assert.Equal(CommandAction.None, ChatCommand.Parse("hello", out argument));
            Assert.Equal(CommandAction.None, ChatCommand.Parse("/dioramas", out argument));
            Assert.Equal(CommandAction.None, ChatCommand.Parse(null, out argument));
        }
    }
}
