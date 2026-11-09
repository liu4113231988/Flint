using System;
using System.Collections.Generic;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Flint3.Core.Input
{
    /// <summary>
    /// 把虚拟键码解析为可显示的按键名：普通按键按键盘布局取字符，特殊键使用固定名称
    /// </summary>
    public sealed class KeyNameResolver
    {
        private const uint NoKeyboardStateChange = 1u << 2;
        private const int VkCapital = 0x14;

        public static KeyNameResolver Default { get; } = new KeyNameResolver();

        private readonly object _lock = new object();
        private readonly Dictionary<nint, Dictionary<uint, string>> _layoutMaps = new Dictionary<nint, Dictionary<uint, string>>();

        public string GetKeyName(uint key)
        {
            return GetKeyName(key, Flint3CorePInvoke.GetKeyboardLayout(0));
        }

        public string GetKeyName(uint key, HKL layout)
        {
            Dictionary<uint, string> map = GetLayoutMap(layout);
            return map.TryGetValue(key, out string? name) ? name : "Undefined";
        }

        private unsafe Dictionary<uint, string> GetLayoutMap(HKL layout)
        {
            nint key = (nint)layout.Value;
            lock (_lock)
            {
                if (!_layoutMaps.TryGetValue(key, out Dictionary<uint, string>? map))
                {
                    map = BuildLayoutMap(layout);
                    _layoutMaps[key] = map;
                }

                return map;
            }
        }

        private static unsafe Dictionary<uint, string> BuildLayoutMap(HKL layout)
        {
            Dictionary<uint, string> map = new Dictionary<uint, string>(256);

            // 只把 Caps Lock 置为按下，让字母键名显示为大写
            byte* keyboardState = stackalloc byte[256];
            keyboardState[VkCapital] = 1;
            char* buffer = stackalloc char[3];

            for (uint vk = 1; vk < 256; vk++)
            {
                uint scanCode = Flint3CorePInvoke.MapVirtualKeyEx(vk, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC, layout);
                int count = Flint3CorePInvoke.ToUnicodeEx(vk, scanCode, keyboardState, buffer, 3, NoKeyboardStateChange, layout);
                if (count != 0)
                {
                    int length = 0;
                    while (length < 3 && buffer[length] != '\0')
                    {
                        length++;
                    }

                    map[vk] = length > 0 ? new string(buffer, 0, length) : $"VK {vk}";
                }
                else
                {
                    map[vk] = $"VK {vk}";
                }
            }

            ApplySpecialKeyNames(map);
            return map;
        }

        private static void ApplySpecialKeyNames(Dictionary<uint, string> map)
        {
            map[0x03] = "Break";        // VK_CANCEL
            map[0x08] = "Backspace";    // VK_BACK
            map[0x09] = "Tab";          // VK_TAB
            map[0x0C] = "Clear";        // VK_CLEAR
            map[0x0D] = "Enter";        // VK_RETURN
            map[0x10] = "Shift";        // VK_SHIFT
            map[0x11] = "Ctrl";         // VK_CONTROL
            map[0x12] = "Alt";          // VK_MENU
            map[0x13] = "Pause";        // VK_PAUSE
            map[0x14] = "Caps Lock";    // VK_CAPITAL
            map[0x15] = "IME Kana";     // VK_KANA / VK_HANGUL
            map[0x17] = "IME Junja";    // VK_JUNJA
            map[0x18] = "IME Final";    // VK_FINAL
            map[0x19] = "IME Hanja";    // VK_HANJA / VK_KANJI
            map[0x1B] = "Esc";          // VK_ESCAPE
            map[0x1C] = "IME Convert";  // VK_CONVERT
            map[0x1D] = "IME Non-Convert"; // VK_NONCONVERT
            map[0x1E] = "IME Kana";     // VK_ACCEPT
            map[0x1F] = "IME Mode Change"; // VK_MODECHANGE
            map[0x20] = "Space";        // VK_SPACE
            map[0x21] = "PgUp";         // VK_PRIOR
            map[0x22] = "PgDn";         // VK_NEXT
            map[0x23] = "End";          // VK_END
            map[0x24] = "Home";         // VK_HOME
            map[0x25] = "Left";         // VK_LEFT
            map[0x26] = "Up";           // VK_UP
            map[0x27] = "Right";        // VK_RIGHT
            map[0x28] = "Down";         // VK_DOWN
            map[0x29] = "Select";       // VK_SELECT
            map[0x2A] = "Print";        // VK_PRINT
            map[0x2B] = "Execute";      // VK_EXECUTE
            map[0x2C] = "Print Screen"; // VK_SNAPSHOT
            map[0x2D] = "Insert";       // VK_INSERT
            map[0x2E] = "Delete";       // VK_DELETE
            map[0x2F] = "Help";         // VK_HELP
            map[0x5B] = "Win (Left)";   // VK_LWIN
            map[0x5C] = "Win (Right)";  // VK_RWIN
            map[0x5D] = "Apps/Menu";    // VK_APPS
            map[0x5F] = "Sleep";        // VK_SLEEP
            map[0x6C] = "Separator";    // VK_SEPARATOR
            map[0x90] = "Num Lock";     // VK_NUMLOCK
            map[0x91] = "Scroll Lock";  // VK_SCROLL
            map[0xA0] = "Shift (Left)"; // VK_LSHIFT
            map[0xA1] = "Shift (Right)"; // VK_RSHIFT
            map[0xA2] = "Ctrl (Left)";  // VK_LCONTROL
            map[0xA3] = "Ctrl (Right)"; // VK_RCONTROL
            map[0xA4] = "Alt (Left)";   // VK_LMENU
            map[0xA5] = "Alt (Right)";  // VK_RMENU
            map[0xA6] = "Browser Back"; // VK_BROWSER_BACK
            map[0xA7] = "Browser Forward";
            map[0xA8] = "Browser Refresh";
            map[0xA9] = "Browser Stop";
            map[0xAA] = "Browser Search";
            map[0xAB] = "Browser Favorites";
            map[0xAC] = "Browser Home";
            map[0xAD] = "Volume Mute";  // VK_VOLUME_MUTE
            map[0xAE] = "Volume Down";
            map[0xAF] = "Volume Up";
            map[0xB0] = "Next Track";   // VK_MEDIA_NEXT_TRACK
            map[0xB1] = "Previous Track";
            map[0xB2] = "Stop Media";
            map[0xB3] = "Play/Pause Media";
            map[0xB4] = "Start Mail";   // VK_LAUNCH_MAIL
            map[0xB5] = "Select Media";
            map[0xB6] = "Start App 1";
            map[0xB7] = "Start App 2";
            map[0xE7] = "Packet";       // VK_PACKET
            map[0xF6] = "Attn";         // VK_ATTN
            map[0xF7] = "CrSel";        // VK_CRSEL
            map[0xF8] = "ExSel";        // VK_EXSEL
            map[0xF9] = "Erase EOF";    // VK_EREOF
            map[0xFA] = "Play";         // VK_PLAY
            map[0xFB] = "Zoom";         // VK_ZOOM
            map[0xFD] = "PA1";          // VK_PA1
            map[0xFE] = "Clear";        // VK_OEM_CLEAR
            map[0xFF] = "Undefined";    // 用于抑制开始菜单的 dummy 键

            for (uint i = 0; i < 10; i++)
            {
                map[0x60 + i] = $"NumPad {i}"; // VK_NUMPAD0 - VK_NUMPAD9
            }

            for (uint i = 0; i < 24; i++)
            {
                map[0x70 + i] = $"F{i + 1}"; // VK_F1 - VK_F24
            }
        }
    }
}
