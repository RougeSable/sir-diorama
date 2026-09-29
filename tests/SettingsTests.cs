using System;
using System.IO;
using Xunit;

namespace SirDiorama.Tests
{
    public class SettingsTests : IDisposable
    {
        private readonly string m_folder = Path.Combine(Path.GetTempPath(), "sir-diorama-tests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(m_folder))
                Directory.Delete(m_folder, true);
        }

        [Fact]
        public void DefaultsAreThoseOfTheTicket()
        {
            var s = new DioramaSettings();
            Assert.True(s.Enabled);
            Assert.True(s.PixelsEnabled);
            Assert.True(s.MiniatureEnabled);
            Assert.Equal(4, s.PixelSize);
            Assert.Equal(32, s.PaletteColors);
            Assert.Equal(2, DioramaSettings.PixelSizeMin);
            Assert.Equal(8, DioramaSettings.PixelSizeMax);
            Assert.Equal(8, DioramaSettings.PaletteColorsMin);
            Assert.Equal(64, DioramaSettings.PaletteColorsMax);
            Assert.Equal("Alt+F2", s.Hotkey);
        }

        [Fact]
        public void OutOfBoundsValuesAreBroughtBack()
        {
            var s = new DioramaSettings { PixelSize = 99, PaletteColors = 2, BlurStrength = -5, Hotkey = "nonsense" }.Normalized();

            Assert.Equal(DioramaSettings.PixelSizeMax, s.PixelSize);
            Assert.Equal(DioramaSettings.PaletteColorsMin, s.PaletteColors);
            Assert.Equal(DioramaSettings.BlurStrengthMin, s.BlurStrength);
            Assert.Equal(DioramaSettings.HotkeyDefault, s.Hotkey);
        }

        [Fact]
        public void EachEffectCanBeSwitchedOffOnItsOwn()
        {
            Assert.True(new DioramaSettings { PixelsEnabled = false }.HasVisibleEffect);
            Assert.True(new DioramaSettings { MiniatureEnabled = false }.HasVisibleEffect);
            Assert.False(new DioramaSettings { PixelsEnabled = false, MiniatureEnabled = false }.HasVisibleEffect);
            Assert.False(new DioramaSettings { PixelsEnabled = false, BlurStrength = 0 }.HasVisibleEffect);
        }

        [Fact]
        public void AMissingFileGivesTheDefaults()
        {
            string problem;
            var s = SettingsFile.Load(Path.Combine(m_folder, "missing.xml"), out problem);

            Assert.Null(problem);
            Assert.True(s.Enabled);
            Assert.Equal(DioramaSettings.PixelSizeDefault, s.PixelSize);
        }

        [Fact]
        public void SettingsSurviveARoundTrip()
        {
            var path = Path.Combine(m_folder, "sub", SettingsFile.FileName);
            SettingsFile.Save(path, new DioramaSettings
            {
                Enabled = false,
                PixelsEnabled = false,
                PixelSize = 6,
                PaletteColors = 48,
                MiniatureEnabled = false,
                BlurStrength = 80,
                Hotkey = "Ctrl+Shift+F7",
            });

            string problem;
            var s = SettingsFile.Load(path, out problem);

            Assert.Null(problem);
            Assert.False(s.Enabled);
            Assert.False(s.PixelsEnabled);
            Assert.Equal(6, s.PixelSize);
            Assert.Equal(48, s.PaletteColors);
            Assert.False(s.MiniatureEnabled);
            Assert.Equal(80, s.BlurStrength);
            Assert.Equal("Ctrl+Shift+F7", s.Hotkey);
            Assert.False(File.Exists(path + ".tmp"));
        }

        [Fact]
        public void AnUnreadableFileDoesNotBlockTheGame()
        {
            Directory.CreateDirectory(m_folder);
            var path = Path.Combine(m_folder, SettingsFile.FileName);
            File.WriteAllText(path, "<not xml");

            string problem;
            var s = SettingsFile.Load(path, out problem);

            Assert.NotNull(problem);
            Assert.True(s.Enabled);
        }

        [Fact]
        public void AFileEditedByHandIsBroughtBack()
        {
            Directory.CreateDirectory(m_folder);
            var path = Path.Combine(m_folder, SettingsFile.FileName);
            File.WriteAllText(path, "<?xml version=\"1.0\"?><DioramaSettings><PixelSize>500</PixelSize></DioramaSettings>");

            string problem;
            var s = SettingsFile.Load(path, out problem);

            Assert.Null(problem);
            Assert.Equal(DioramaSettings.PixelSizeMax, s.PixelSize);
        }
    }

    public class HotkeyTests
    {
        [Theory]
        [InlineData("Alt+F2", "Alt+F2")]
        [InlineData("alt + f2", "Alt+F2")]
        [InlineData("Shift+Ctrl+F7", "Ctrl+Shift+F7")]
        [InlineData("Control+Alt+Pause", "Ctrl+Alt+Pause")]
        [InlineData("F9", "F9")]
        public void ReadsAndWritesShortcuts(string text, string expected)
        {
            HotkeyBinding binding;
            Assert.True(HotkeyBinding.TryParse(text, out binding));
            Assert.Equal(expected, binding.ToString());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Alt")]
        [InlineData("Alt+W")]
        [InlineData("Alt+F2+F3")]
        [InlineData("Alt+Alt+F2")]
        [InlineData("Alt++F2")]
        public void RefusesWhatIsNotAShortcut(string text)
        {
            HotkeyBinding binding;
            Assert.False(HotkeyBinding.TryParse(text, out binding));
        }

        [Fact]
        public void ModifiersMustMatchExactly()
        {
            HotkeyBinding binding;
            HotkeyBinding.TryParse(DioramaSettings.HotkeyDefault, out binding);

            Assert.True(binding.Matches("F2", false, true, false));
            // Ctrl+F2, and Ctrl+Alt+F2, belong to someone else.
            Assert.False(binding.Matches("F2", true, false, false));
            Assert.False(binding.Matches("F2", true, true, false));
            Assert.False(binding.Matches("F2", false, false, false));
            Assert.False(binding.Matches("F3", false, true, false));
        }
    }

    public class ChatCommandTests
    {
        [Theory]
        [InlineData("/diorama", CommandAction.Toggle)]
        [InlineData("  /DIORAMA  ", CommandAction.Toggle)]
        [InlineData("/diorama on", CommandAction.On)]
        [InlineData("/diorama off", CommandAction.Off)]
        [InlineData("/diorama status", CommandAction.Status)]
        [InlineData("/diorama key Alt+F3", CommandAction.SetHotkey)]
        [InlineData("/diorama whatever", CommandAction.Unknown)]
        [InlineData("/cel", CommandAction.None)]
        [InlineData("/dioramas", CommandAction.None)]
        [InlineData("hello", CommandAction.None)]
        [InlineData("", CommandAction.None)]
        [InlineData(null, CommandAction.None)]
        public void Parses(string text, CommandAction expected)
        {
            string argument;
            Assert.Equal(expected, ChatCommand.Parse(text, out argument));
        }

        [Fact]
        public void TheKeyCommandCarriesTheShortcut()
        {
            string argument;
            ChatCommand.Parse("/diorama key  Ctrl+F9 ", out argument);
            Assert.Equal("Ctrl+F9", argument);
        }
    }
}
