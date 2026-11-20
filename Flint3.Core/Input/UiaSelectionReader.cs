using System;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Accessibility;

namespace Flint3.Core.Input
{
    /// <summary>
    /// 直接读焦点控件的选中文本（TextPattern），由调用方控制等待时间。
    /// </summary>
    internal static unsafe class UiaSelectionReader
    {
        private const int MaxRanges = 32;

        /// <summary>
        /// 读取当前焦点控件的选中文本；无选区或不支持 TextPattern 时返回 null
        /// </summary>
        internal static string? ReadSelection()
        {
            try
            {
                return ReadSelectionCore();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
                return null;
            }
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
