using System.Text.Json.Serialization;

namespace Flint3.Core.Input
{
    /// <summary>
    /// 划词热键与选中文字后显示图标的设置。
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
        /// 选中文字后显示划词图标，默认关闭。
        /// </summary>
        [JsonPropertyName("autoEnabled")]
        public bool AutoEnabled { get; set; } = false;
    }
}
