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
        /// 发送单个按键事件，extraInfo 用于让钩子忽略自己注入的按键
        /// </summary>
        public static unsafe void SendKey(ushort virtualKey, bool keyUp, nuint extraInfo = 0)
        {
            INPUT input = default;
            input.type = INPUT_TYPE.INPUT_KEYBOARD;
            input.Anonymous.ki.wVk = (VIRTUAL_KEY)virtualKey;
            input.Anonymous.ki.dwFlags = keyUp ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0;
            input.Anonymous.ki.dwExtraInfo = extraInfo;

            PInvoke.SendInput(1, &input, Marshal.SizeOf<INPUT>());
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
            return (PInvoke.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }
    }
}
