using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;

namespace Flint3.Core.Input
{
    /// <summary>通过 UI Automation 读取前台选区，保留剪贴板内容。</summary>
    public static class SelectionCaptureService
    {
        private static int _busy;

        public static async Task<string?> CaptureAsync()
        {
            var foreground = Flint3CorePInvoke.GetForegroundWindow();
            if (foreground == default)
            {
                return null;
            }

            Flint3CorePInvoke.GetWindowThreadProcessId(foreground, out uint pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId || Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                return null;
            }

            Task<string?> read = Task.Run(() =>
            {
                try
                {
                    if (foreground != Flint3CorePInvoke.GetForegroundWindow())
                    {
                        return null;
                    }

                    // shortcut: 不支持 TextPattern 的控件请用 OCR，待有可靠的无剪贴板读取方式再扩展。
                    string? text = UiaSelectionReader.ReadSelection();
                    return foreground == Flint3CorePInvoke.GetForegroundWindow() && !string.IsNullOrWhiteSpace(text)
                        ? text.Trim()
                        : null;
                }
                finally
                {
                    // UIA 超时后原生调用可能仍在运行，保持单在途直到它返回。
                    Interlocked.Exchange(ref _busy, 0);
                }
            });

            try
            {
                return await read.WaitAsync(TimeSpan.FromMilliseconds(1500)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return null;
            }
        }
    }
}
