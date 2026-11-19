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
    /// 低级鼠标事件（消息号 + 屏幕坐标 + 注入标记）
    /// </summary>
    public readonly struct MouseEvent
    {
        public MouseEvent(uint message, int x, int y, nuint extraInfo)
        {
            Message = message;
            X = x;
            Y = y;
            ExtraInfo = extraInfo;
        }

        public uint Message { get; }

        public int X { get; }

        public int Y { get; }

        public nuint ExtraInfo { get; }
    }

    /// <summary>
    /// 低级鼠标钩子（WH_MOUSE_LL），被动观察不吞键。必须安装在有消息循环的线程上，回调在该线程执行。
    /// </summary>
    public sealed class MouseHook : IDisposable
    {
        private const int HcAction = 0;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate LRESULT HookProcCallback(int nCode, WPARAM wParam, LPARAM lParam);

        // 原生侧只保存函数指针，必须静态持有委托避免被 GC 回收
        private static readonly List<HookProcCallback> s_hookCallbacks = new List<HookProcCallback>();
        private static readonly object s_hookCallbacksLock = new object();

        private readonly Action<MouseEvent> _callback;
        private readonly HookProcCallback _hookCallback;
        private readonly HOOKPROC _hookProc;

        private HHOOK _hookHandle;
        private bool _installed;
        private bool _disposed;

        public unsafe MouseHook(Action<MouseEvent> callback)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _hookCallback = HookProc;
            _hookProc = (delegate* unmanaged[Stdcall]<int, WPARAM, LPARAM, LRESULT>)(void*)Marshal.GetFunctionPointerForDelegate(_hookCallback);
        }

        public bool IsInstalled => _installed;

        /// <summary>
        /// 安装钩子，失败时返回 false 并写日志（降级为不响应自动划词）
        /// </summary>
        public unsafe bool Start()
        {
            if (_installed)
            {
                return true;
            }

            _hookHandle = Flint3CorePInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _hookProc, default(HINSTANCE), 0);
            if (_hookHandle == default)
            {
                Trace.WriteLine($"SetWindowsHookEx(WH_MOUSE_LL) failed: {Marshal.GetLastWin32Error()}");
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
            if (nCode == HcAction)
            {
                MSLLHOOKSTRUCT* info = (MSLLHOOKSTRUCT*)lParam.Value;
                _callback(new MouseEvent((uint)wParam.Value, info->pt.X, info->pt.Y, info->dwExtraInfo));
            }

            // 被动观察：始终放行，不影响正常鼠标操作
            return Flint3CorePInvoke.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_installed)
            {
                Flint3CorePInvoke.UnhookWindowsHookEx(_hookHandle);
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