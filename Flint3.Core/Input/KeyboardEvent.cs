using System;

namespace Flint3.Core.Input
{
    /// <summary>
    /// 低级键盘钩子回调携带的按键事件
    /// </summary>
    public readonly struct KeyboardEvent
    {
        public KeyboardEvent(uint message, int key, nuint extraInfo)
        {
            Message = message;
            Key = key;
            ExtraInfo = extraInfo;
        }

        /// <summary>
        /// 窗口消息，如 WM_KEYDOWN / WM_SYSKEYDOWN / WM_KEYUP / WM_SYSKEYUP
        /// </summary>
        public uint Message { get; }

        /// <summary>
        /// 虚拟键码
        /// </summary>
        public int Key { get; }

        /// <summary>
        /// 钩子额外信息，用于区分自己注入的按键
        /// </summary>
        public nuint ExtraInfo { get; }
    }
}
