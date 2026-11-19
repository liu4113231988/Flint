using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;

namespace Flint3.Core.Input
{
    /// <summary>
    /// 划词取词：注入 Ctrl+C 让前台应用复制选中文本，读取剪贴板后恢复原内容；
    /// 复制失败时用 UIA TextPattern 直接读选区兜底。
    /// 全程 Win32/COM API，不依赖 STA，可在工作线程执行。
    /// </summary>
    public static class SelectionCaptureService
    {
        private const uint CfUnicodeText = 13;
        private const uint CfDib = 8;
        private const int PollIntervalMs = 50;
        private const int PollTimeoutMs = 600;
        private const int MaxDibBytes = 32 * 1024 * 1024;

        private static int _busy;

        /// <summary>
        /// 取当前前台应用选中的文本；无选中、复制失败或已有取词在进行时返回 null
        /// </summary>
        public static async Task<string?> CaptureAsync()
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                return null;
            }

            try
            {
                return await Task.Run(CaptureCore).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        private static string? CaptureCore()
        {
            // 前台是自己的窗口时不取词，避免弹窗抢焦点后复制到自己的搜索框
            HWND foreground = Flint3CorePInvoke.GetForegroundWindow();
            if (foreground != default)
            {
                Flint3CorePInvoke.GetWindowThreadProcessId(foreground, out uint pid);
                if (pid == (uint)Environment.ProcessId)
                {
                    return null;
                }
            }

            uint baseline = Flint3CorePInvoke.GetClipboardSequenceNumber();
            (string? oldText, byte[]? oldDib) = ReadClipboard();

            InputSimulator.ReleaseHeldModifiers();
            InputSimulator.SendCtrlC();

            string? captured = WaitForText(baseline);

            // 复制动作改动了剪贴板才恢复现场；未改动则不碰，避免多一条剪贴板历史
            if (Flint3CorePInvoke.GetClipboardSequenceNumber() != baseline)
            {
                RestoreClipboard(oldText, oldDib);
            }

            // Ctrl+C 没取到文本（应用不响应复制快捷键等）：UIA 直接读选区兜底，不碰剪贴板
            if (string.IsNullOrWhiteSpace(captured))
            {
                captured = UiaSelectionReader.TryReadSelection();
            }

            return string.IsNullOrWhiteSpace(captured) ? null : captured.Trim();
        }

        /// <summary>
        /// 目标应用异步写剪贴板，按序号变化轮询；序号变了但文本为空时继续等（可能分两步写入）
        /// </summary>
        private static string? WaitForText(uint baseline)
        {
            for (int waited = 0; waited < PollTimeoutMs; waited += PollIntervalMs)
            {
                Thread.Sleep(PollIntervalMs);
                if (Flint3CorePInvoke.GetClipboardSequenceNumber() == baseline)
                {
                    continue;
                }

                (string? text, _) = ReadClipboard();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            return null;
        }

        private static (string? Text, byte[]? Dib) ReadClipboard()
        {
            if (!OpenClipboardWithRetry())
            {
                return (null, null);
            }

            try
            {
                return (ReadText(), ReadDib());
            }
            finally
            {
                Flint3CorePInvoke.CloseClipboard();
            }
        }

        private static unsafe string? ReadText()
        {
            if (!Flint3CorePInvoke.IsClipboardFormatAvailable(CfUnicodeText))
            {
                return null;
            }

            HANDLE handle = Flint3CorePInvoke.GetClipboardData(CfUnicodeText);
            if (handle == default)
            {
                return null;
            }

            HGLOBAL global = new(handle.Value);
            void* ptr = Flint3CorePInvoke.GlobalLock(global);
            if (ptr == null)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni((IntPtr)ptr);
            }
            finally
            {
                Flint3CorePInvoke.GlobalUnlock(global);
            }
        }

        private static unsafe byte[]? ReadDib()
        {
            if (!Flint3CorePInvoke.IsClipboardFormatAvailable(CfDib))
            {
                return null;
            }

            HANDLE handle = Flint3CorePInvoke.GetClipboardData(CfDib);
            if (handle == default)
            {
                return null;
            }

            HGLOBAL global = new(handle.Value);
            nuint size = Flint3CorePInvoke.GlobalSize(global);
            if (size == 0 || size > MaxDibBytes)
            {
                return null;
            }

            void* ptr = Flint3CorePInvoke.GlobalLock(global);
            if (ptr == null)
            {
                return null;
            }

            try
            {
                byte[] data = new byte[(int)size];
                Marshal.Copy((IntPtr)ptr, data, 0, data.Length);
                return data;
            }
            finally
            {
                Flint3CorePInvoke.GlobalUnlock(global);
            }
        }

        private static void RestoreClipboard(string? text, byte[]? dib)
        {
            if (!OpenClipboardWithRetry())
            {
                return;
            }

            try
            {
                Flint3CorePInvoke.EmptyClipboard();
                if (text is not null)
                {
                    SetClipboardBytes(CfUnicodeText, Encoding.Unicode.GetBytes(text + "\0"));
                }

                if (dib is not null)
                {
                    SetClipboardBytes(CfDib, dib);
                }

                // 恢复的内容不进 Win+V 剪贴板历史，避免同一条内容出现两次
                uint excludeFormat = Flint3CorePInvoke.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
                if (excludeFormat != 0)
                {
                    SetClipboardBytes(excludeFormat, new byte[4]);
                }
            }
            finally
            {
                Flint3CorePInvoke.CloseClipboard();
            }
        }

        private static unsafe void SetClipboardBytes(uint format, byte[] data)
        {
            HGLOBAL global = Flint3CorePInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, (nuint)data.Length);
            if (global == default)
            {
                return;
            }

            void* ptr = Flint3CorePInvoke.GlobalLock(global);
            if (ptr == null)
            {
                Flint3CorePInvoke.GlobalFree(global);
                return;
            }

            Marshal.Copy(data, 0, (IntPtr)ptr, data.Length);
            Flint3CorePInvoke.GlobalUnlock(global);

            // SetClipboardData 成功后内存归系统所有，不能再释放
            if (Flint3CorePInvoke.SetClipboardData(format, new HANDLE(global.Value)) == default)
            {
                Flint3CorePInvoke.GlobalFree(global);
            }
        }

        private static bool OpenClipboardWithRetry()
        {
            for (int i = 0; i < 6; i++)
            {
                if (Flint3CorePInvoke.OpenClipboard(default))
                {
                    return true;
                }

                Thread.Sleep(30);
            }

            return false;
        }
    }
}