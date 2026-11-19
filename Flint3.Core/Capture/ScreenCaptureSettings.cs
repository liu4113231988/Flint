using Flint3.Core.Input;
using System.Text.Json.Serialization;

namespace Flint3.Core.Capture
{
    /// <summary>
    /// 屏幕取词设置：开关 + 取词热键，整体 JSON 持久化
    /// </summary>
    public class ScreenCaptureSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 默认 Ctrl+Alt+S
        /// </summary>
        [JsonPropertyName("hotkey")]
        public HotkeySettings Hotkey { get; set; } = new(false, true, true, false, 0x53);
    }
}