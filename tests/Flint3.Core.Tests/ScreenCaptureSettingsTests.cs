using System.Text.Json;
using Flint3.Core.Capture;
using Flint3.Core.Serialization;
using Xunit;

namespace Flint3.Core.Tests
{
    public class ScreenCaptureSettingsTests
    {
        [Fact]
        public void Defaults_EnabledWithCtrlAltS()
        {
            var settings = new ScreenCaptureSettings();

            Assert.True(settings.Enabled);
            Assert.Equal("Ctrl + Alt + S", settings.Hotkey.ToString());
            Assert.True(settings.Hotkey.IsValid());
        }

        [Fact]
        public void JsonRoundtrip_PreservesEnabledAndHotkey()
        {
            var settings = new ScreenCaptureSettings
            {
                Enabled = false,
                Hotkey = new(false, true, false, true, 0x70),
            };

            string json = JsonSerializer.Serialize(settings, FlintJsonContext.Default.ScreenCaptureSettings);
            var restored = JsonSerializer.Deserialize(json, FlintJsonContext.Default.ScreenCaptureSettings);

            Assert.NotNull(restored);
            Assert.False(restored!.Enabled);
            Assert.Equal("Ctrl + Shift + F1", restored.Hotkey.ToString());
        }

        [Fact]
        public void Deserialize_MissingHotkeyKeepsDefault()
        {
            var restored = JsonSerializer.Deserialize("{\"enabled\":true}", FlintJsonContext.Default.ScreenCaptureSettings);

            Assert.NotNull(restored);
            Assert.Equal("Ctrl + Alt + S", restored!.Hotkey.ToString());
        }
    }
}