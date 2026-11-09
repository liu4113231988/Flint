using System;
using System.IO;
using System.Text.Json;
using Flint3.Core.Input;
using Flint3.Core.Serialization;
using Flint3.Core.Settings;
using Xunit;

namespace Flint3.Core.Tests
{
    public class SettingsStoreTests
    {
        [Fact]
        public void JsonFileStore_写入后可读取且重启后保留()
        {
            string path = Path.Combine(Path.GetTempPath(), $"flint3-settings-{Guid.NewGuid():N}.json");
            try
            {
                var store = new JsonFileSettingsStore(path);
                store["WindowMode"] = 1;
                store["EnableTTS"] = true;

                Assert.Equal("1", store["WindowMode"]);
                Assert.Equal("True", store["EnableTTS"]);
                Assert.Null(store["Missing"]);

                var reloaded = new JsonFileSettingsStore(path);
                Assert.Equal("1", reloaded["WindowMode"]);
                Assert.Equal("True", reloaded["EnableTTS"]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void JsonFileStore_赋值_null_存为空字符串()
        {
            string path = Path.Combine(Path.GetTempPath(), $"flint3-settings-{Guid.NewGuid():N}.json");
            try
            {
                var store = new JsonFileSettingsStore(path);
                store["Voice"] = null;

                Assert.Equal(string.Empty, store["Voice"]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void HotkeySettings_源生成上下文可往返序列化()
        {
            var settings = new HotkeySettings(false, true, true, true, 0x7B);

            string json = JsonSerializer.Serialize(settings, FlintJsonContext.Default.HotkeySettings);
            HotkeySettings? restored = JsonSerializer.Deserialize(json, FlintJsonContext.Default.HotkeySettings);

            Assert.NotNull(restored);
            Assert.False(restored!.Win);
            Assert.True(restored.Ctrl);
            Assert.True(restored.Alt);
            Assert.True(restored.Shift);
            Assert.Equal(0x7B, restored.Code);
            Assert.Contains("\"code\":123", json);
        }
    }
}
