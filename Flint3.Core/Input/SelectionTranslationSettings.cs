using System.Text.Json.Serialization;

namespace Flint3.Core.Input
{
    /// <summary>
    /// 划词翻译设置：开关 + 取词热键 + 自动划词开关，整体 JSON 持久化
    /// </summary>
    public class SelectionTranslationSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 默认 Ctrl+Alt+D
        /// </summary>
        [JsonPropertyName("hotkey")]
        public HotkeySettings Hotkey { get; set; } = new(false, true, true, false, 0x44);

        /// <summary>
        /// 自动划词：选中文字后自动取词，无需按快捷键。默认关闭（打扰性强，opt-in）
        /// </summary>
        [JsonPropertyName("autoEnabled")]
        public bool AutoEnabled { get; set; } = false;
    }
}