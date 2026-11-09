using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Flint3.Core.Input
{
    /// <summary>
    /// 低级键盘钩子（WH_KEYBOARD_LL）。必须安装在有消息循环的线程上，回调在该线程执行。
    /// </summary>
    public sealed class KeyboardHook : IDisposable
    {
        private const int HcAction = 0;

        public delegate bool FilterKeyboardEvent(KeyboardEvent ev);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate LRESULT HookProcCallback(int nCode, WPARAM wParam, LPARAM lParam);

        // 原生侧只保存函数指针，必须静态持有委托避免被 GC 回收
        private static readonly List<HookProcCallback> s_hookCallbacks = new List<HookProcCallback>();
        private static readonly object s_hookCallbacksLock = new object();

        private readonly Action<KeyboardEvent> _callback;
        private readonly Func<bool>? _isActive;
        private readonly FilterKeyboardEvent? _filter;
        private readonly HookProcCallback _hookCallback;
        private readonly HOOKPROC _hookProc;

        private HHOOK _hookHandle;
        private bool _installed;
        private bool _disposed;

        public unsafe KeyboardHook(Action<KeyboardEvent> callback, Func<bool>? isActive = null, FilterKeyboardEvent? filter = null)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _isActive = isActive;
            _filter = filter;

            _hookCallback = HookProc;
            _hookProc = (delegate* unmanaged[Stdcall]<int, WPARAM, LPARAM, LRESULT>)(void*)Marshal.GetFunctionPointerForDelegate(_hookCallback);
        }

        public bool IsInstalled => _installed;

        /// <summary>
        /// 安装钩子，失败时返回 false 并写日志（降级为不响应快捷键）
        /// </summary>
        public unsafe bool Start()
        {
            if (_installed)
            {
                return true;
            }

            _hookHandle = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _hookProc, default(HINSTANCE), 0);
            if (_hookHandle == default)
            {
                Trace.WriteLine($"SetWindowsHookEx(WH_KEYBOARD_LL) failed: {Marshal.GetLastWin32Error()}");
                return false;
            }

            lock (s_hookCallbacksLock)
            {
                s_hookCallbacks.Add(_hookCallback);
            }

            _installed = true;
            return true;
        }

        private unsafe LRESULT HookProc(int nCode, WPARAM wParam, LPARAM lParam)
        {
            if (nCode == HcAction && (_isActive is null || _isActive()))
            {
                KBDLLHOOKSTRUCT* info = (KBDLLHOOKSTRUCT*)lParam.Value;
                KeyboardEvent ev = new KeyboardEvent((uint)wParam.Value, (int)info->vkCode, info->dwExtraInfo);

                if (_filter is not null && !_filter(ev))
                {
                    return PInvoke.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                }

                _callback(ev);
                return new LRESULT(1);
            }

            return PInvoke.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_installed)
            {
                PInvoke.UnhookWindowsHookEx(_hookHandle);
                lock (s_hookCallbacksLock)
                {
                    s_hookCallbacks.Remove(_hookCallback);
                }

                _installed = false;
            }

            _disposed = true;
        }
    }
}
