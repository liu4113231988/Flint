using System;
using System.Text;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Accessibility;

namespace Flint3.Core.Input
{
    /// <summary>
    /// UIA 兜底取词：直接读焦点控件的选中文本（TextPattern），不经过剪贴板。
    /// 独立后台线程执行并带超时，目标进程无响应时放弃等待，不阻塞取词主流程。
    /// </summary>
    public static unsafe class UiaSelectionReader
    {
        private const int DefaultTimeoutMs = 1500;
        private const int MaxRanges = 32;

        /// <summary>
        /// 读取当前焦点控件的选中文本；无选区、不支持 TextPattern 或超时返回 null
        /// </summary>
        public static string? TryReadSelection(int timeoutMs = DefaultTimeoutMs)
        {
            string? result = null;
            using ManualResetEventSlim done = new(false);
            Thread thread = new(() =>
            {
                try
                {
                    result = ReadSelectionCore();
                }
                catch (Exception ex)
                {
                    // 控件不支持 TextPattern 时 COM 调用抛异常，属常见情况
                    System.Diagnostics.Trace.WriteLine(ex);
                }
                finally
                {
                    done.Set();
                }
            });
            thread.IsBackground = true;
            thread.Name = "FlintUiaSelection";
            thread.Start();

            // UIA 跨进程调用可能随目标进程挂起而无限阻塞：超时即放弃该线程
            return done.Wait(timeoutMs) ? result : null;
        }

        private static string? ReadSelectionCore()
        {
            if (Flint3CorePInvoke.CoInitializeEx(COINIT.COINIT_MULTITHREADED).Failed)
            {
                return null;
            }

            try
            {
                if (CUIAutomation.CreateInstance(out IUIAutomation* automation).Failed || automation == null)
                {
                    return null;
                }

                try
                {
                    IUIAutomationElement* element = automation->GetFocusedElement();
                    if (element == null)
                    {
                        return null;
                    }

                    try
                    {
                        void* patternPtr = element->GetCurrentPatternAs(UIA_PATTERN_ID.UIA_TextPatternId, IUIAutomationTextPattern.IID_Guid);
                        if (patternPtr == null)
                        {
                            return null;
                        }

                        IUIAutomationTextPattern* pattern = (IUIAutomationTextPattern*)patternPtr;
                        try
                        {
                            return ReadSelection(pattern);
                        }
                        finally
                        {
                            pattern->Release();
                        }
                    }
                    finally
                    {
                        element->Release();
                    }
                }
                finally
                {
                    automation->Release();
                }
            }
            finally
            {
                Flint3CorePInvoke.CoUninitialize();
            }
        }

        private static string? ReadSelection(IUIAutomationTextPattern* pattern)
        {
            IUIAutomationTextRangeArray* ranges = pattern->GetSelection();
            if (ranges == null)
            {
                return null;
            }

            try
            {
                int count = Math.Min(ranges->Length, MaxRanges);
                StringBuilder text = new();
                for (int i = 0; i < count; i++)
                {
                    IUIAutomationTextRange* range = ranges->GetElement(i);
                    if (range == null)
                    {
                        continue;
                    }

                    try
                    {
                        BSTR rangeText = range->GetText(-1);
                        if (rangeText.Value != null)
                        {
                            if (text.Length > 0)
                            {
                                text.Append('\n');
                            }

                            text.Append(rangeText.ToString());
                            Flint3CorePInvoke.SysFreeString(rangeText);
                        }
                    }
                    finally
                    {
                        range->Release();
                    }
                }

                string result = text.ToString();
                return string.IsNullOrWhiteSpace(result) ? null : result;
            }
            finally
            {
                ranges->Release();
            }
        }
    }
}