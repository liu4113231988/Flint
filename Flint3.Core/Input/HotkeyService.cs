using System;
using System.Collections.Generic;

namespace Flint3.Core.Input
{
    /// <summary>
    /// 全局快捷键：基于 WH_KEYBOARD_LL 低级键盘钩子，命中已注册的组合键后触发回调。
    /// </summary>
    public sealed class HotkeyService : IDisposable
    {
        private const uint WmKeyDown = 0x0100;
        private const uint WmSysKeyDown = 0x0104;
        private const int VkShift = 0x10;
        private const int VkControl = 0x11;
        private const int VkMenu = 0x12;
        private const int VkLWin = 0x5B;
        private const int VkRWin = 0x5C;

        private readonly object _lock = new object();
        private readonly Dictionary<ushort, Action> _hotkeys = new Dictionary<ushort, Action>();
        private readonly KeyboardHook _hook;

        // 当前按键状态由钩子线程持续更新，回调也在同一线程读取
        private bool _pressedWin;
        private bool _pressedCtrl;
        private bool _pressedShift;
        private bool _pressedAlt;
        private ushort _pressedHandle;

        public HotkeyService()
        {
            _hook = new KeyboardHook(OnKeyboardEvent, () => true, FilterKeyboardEvent);
            _hook.Start();
        }

        /// <summary>
        /// 钩子是否安装成功；安装失败时快捷键失效，但注册/注销仍可用
        /// </summary>
        public bool IsHookInstalled => _hook.IsInstalled;

        /// <summary>
        /// 注册快捷键，相同组合键会覆盖之前的回调
        /// </summary>
        public ushort Register(HotkeySettings hotkey, Action callback)
        {
            ArgumentNullException.ThrowIfNull(hotkey);
            ArgumentNullException.ThrowIfNull(callback);

            ushort handle = GetHotkeyHandle(hotkey);
            lock (_lock)
            {
                _hotkeys[handle] = callback;
            }

            return handle;
        }

        public void Unregister(ushort handle)
        {
            lock (_lock)
            {
                _hotkeys.Remove(handle);
            }
        }

        public static ushort GetHotkeyHandle(HotkeySettings hotkey)
        {
            return Encode(hotkey.Win, hotkey.Ctrl, hotkey.Shift, hotkey.Alt, (byte)hotkey.Code);
        }

        private static ushort Encode(bool win, bool ctrl, bool shift, bool alt, byte key)
        {
            ushort handle = key;
            if (win)
            {
                handle |= 1 << 8;
            }

            if (ctrl)
            {
                handle |= 1 << 9;
            }

            if (shift)
            {
                handle |= 1 << 10;
            }

            if (alt)
            {
                handle |= 1 << 11;
            }

            return handle;
        }

        private bool FilterKeyboardEvent(KeyboardEvent ev)
        {
            _pressedWin = InputSimulator.IsKeyDown(VkLWin) || InputSimulator.IsKeyDown(VkRWin);
            _pressedCtrl = InputSimulator.IsKeyDown(VkControl);
            _pressedAlt = InputSimulator.IsKeyDown(VkMenu);
            _pressedShift = InputSimulator.IsKeyDown(VkShift);
            _pressedHandle = Encode(_pressedWin, _pressedCtrl, _pressedShift, _pressedAlt, (byte)ev.Key);

            if (ev.Message != WmKeyDown && ev.Message != WmSysKeyDown)
            {
                return false;
            }

            lock (_lock)
            {
                return _hotkeys.ContainsKey(_pressedHandle);
            }
        }

        private void OnKeyboardEvent(KeyboardEvent ev)
        {
            Action? callback;
            lock (_lock)
            {
                if (!_hotkeys.TryGetValue(_pressedHandle, out callback))
                {
                    return;
                }
            }

            callback();

            // 命中后吞掉按键，并补一个 dummy keyup，避免 Win 组合键弹出开始菜单
            InputSimulator.SuppressStartMenu();
        }

        public void Dispose()
        {
            _hook.Dispose();
        }
    }
}
