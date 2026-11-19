using System;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Flint3.Core.Input
{
    /// <summary>
    /// 按键注入与按键状态查询
    /// </summary>
    public static class InputSimulator
    {
        /// <summary>
        /// 注入按键的 dwExtraInfo 标记（"FLIN"），钩子据此忽略自己注入的按键
        /// </summary>
        public const nuint InjectedKeyMarker = 0x464C494E;

        private const int VkShift = 0x10;
        private const int VkControl = 0x11;
        private const int VkMenu = 0x12;
        private const int VkLWin = 0x5B;
        private const int VkRWin = 0x5C;
        private const int VkC = 0x43;

        /// <summary>
        /// 发送单个按键事件，extraInfo 用于让钩子忽略自己注入的按键
        /// </summary>
        public static unsafe void SendKey(ushort virtualKey, bool keyUp, nuint extraInfo = 0)
        {
            INPUT input = default;
            input.type = INPUT_TYPE.INPUT_KEYBOARD;
            input.Anonymous.ki.wVk = (VIRTUAL_KEY)virtualKey;
            input.Anonymous.ki.dwFlags = keyUp ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0;
            input.Anonymous.ki.dwExtraInfo = extraInfo;

            Flint3CorePInvoke.SendInput(1, &input, Marshal.SizeOf<INPUT>());
        }

        /// <summary>
        /// Win 组合键触发后发送 dummy keyup，阻止开始菜单弹出
        /// </summary>
        public static void SuppressStartMenu()
        {
            SendKey(0xFF, keyUp: true);
        }

        public static bool IsKeyDown(int virtualKey)
        {
            return (Flint3CorePInvoke.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        /// <summary>
        /// 注入 Ctrl+C 复制当前选中文本；全部带注入标记，避免触发自家热键
        /// </summary>
        public static void SendCtrlC()
        {
            SendKey(VkControl, keyUp: false, InjectedKeyMarker);
            SendKey(VkC, keyUp: false, InjectedKeyMarker);
            SendKey(VkC, keyUp: true, InjectedKeyMarker);
            SendKey(VkControl, keyUp: true, InjectedKeyMarker);
        }

        /// <summary>
        /// 抬起用户仍按住的修饰键，保证注入的 Ctrl+C 不被物理按住的 Alt/Win 等污染
        /// </summary>
        public static void ReleaseHeldModifiers()
        {
            bool winHeld = false;
            foreach (int vk in new[] { VkShift, VkControl, VkMenu, VkLWin, VkRWin })
            {
                if (IsKeyDown(vk))
                {
                    SendKey((ushort)vk, keyUp: true, InjectedKeyMarker);
                    winHeld |= vk == VkLWin || vk == VkRWin;
                }
            }

            if (winHeld)
            {
                SuppressStartMenu();
            }
        }
    }
}
