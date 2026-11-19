using System.Text.Json;
using Flint3.Core.Input;
using Flint3.Core.Serialization;
using Xunit;

namespace Flint3.Core.Tests
{
    public class SelectionTranslationSettingsTests
    {
        [Fact]
        public void Defaults_EnabledWithCtrlAltD()
        {
            var settings = new SelectionTranslationSettings();

            Assert.True(settings.Enabled);
            Assert.Equal(new HotkeySettings(false, true, true, false, 0x44).ToString(), settings.Hotkey.ToString());
            Assert.True(settings.Hotkey.IsValid());
            Assert.False(settings.AutoEnabled);
        }

        [Fact]
        public void JsonRoundtrip_PreservesEnabledAndHotkey()
        {
            var settings = new SelectionTranslationSettings
            {
                Enabled = false,
                Hotkey = new HotkeySettings(true, false, false, true, 0x70),
                AutoEnabled = true
            };

            string json = JsonSerializer.Serialize(settings, FlintJsonContext.Default.SelectionTranslationSettings);
            var restored = JsonSerializer.Deserialize(json, FlintJsonContext.Default.SelectionTranslationSettings);

            Assert.NotNull(restored);
            Assert.False(restored.Enabled);
            Assert.Equal(settings.Hotkey.ToString(), restored.Hotkey.ToString());
            Assert.True(restored.AutoEnabled);
        }

        [Fact]
        public void Deserialize_MissingHotkeyKeepsDefault()
        {
            var restored = JsonSerializer.Deserialize("{\"enabled\":true}", FlintJsonContext.Default.SelectionTranslationSettings);

            Assert.NotNull(restored);
            Assert.True(restored.Enabled);
            Assert.Equal(new SelectionTranslationSettings().Hotkey.ToString(), restored.Hotkey.ToString());
            Assert.False(restored.AutoEnabled);
        }
    }
}