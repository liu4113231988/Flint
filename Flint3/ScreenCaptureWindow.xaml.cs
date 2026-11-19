using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Flint3.Core.Capture;
using Flint3.Helpers;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.System;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using WinUIEx;
using DrawingPoint = System.Drawing.Point;

namespace Flint3
{
    /// <summary>
    /// 屏幕取词覆盖窗：冻结光标所在显示器的截图，拖选区域或单击文字取词，Esc/右键取消。
    /// 结果回调 (text, cancelled)：cancelled 为 true 表示用户主动取消，不弹任何提示
    /// </summary>
    public sealed partial class ScreenCaptureWindow : WindowEx
    {
        private const double ClickThresholdDip = 6;
        private const double ClickCropHalfWidthDip = 160;
        private const double ClickCropHalfHeightDip = 48;
        private const double WordHitMaxDistanceDip = 24;

        private static ScreenCaptureWindow? _current;

        private readonly ScreenCaptureResult _capture;
        private readonly Action<string?, bool> _onCompleted;
        private MemoryStream? _bmpStream;
        private Point _startDip;
        private bool _dragging;
        private bool _finished;

        public static bool IsOpen => _current is not null;

        private ScreenCaptureWindow(ScreenCaptureResult capture, Action<string?, bool> onCompleted)
        {
            _capture = capture;
            _onCompleted = onCompleted;

            this.InitializeComponent();
            this.IsShownInSwitchers = false;
            this.ExtendsContentIntoTitleBar = true;
            this.IsTitleBarVisible = false;
            this.IsAlwaysOnTop = true;
            this.IsResizable = false;
            this.IsMaximizable = false;
            this.IsMinimizable = false;

            byte[] bmp = ScreenCaptureService.EncodeBmp(capture);
            _bmpStream = new MemoryStream(bmp);
            var image = new BitmapImage();
            image.SetSource(_bmpStream.AsRandomAccessStream());
            ScreenshotImage.Source = image;

            // 关闭 Win11 圆角，避免截图四角被裁掉（DWMWA_WINDOW_CORNER_PREFERENCE = 33，DWMWCP_DONOTROUND = 1）
            int preference = 1;
            DwmSetWindowAttribute(this.GetWindowHandle(), 33, ref preference, sizeof(int));

            this.Closed += OnClosed;
        }

        /// <summary>
        /// 截取光标所在显示器并弹出覆盖窗；已有覆盖窗时再次调用视为取消。
        /// 截屏失败回调 (null, false)
        /// </summary>
        public static void Show(Action<string?, bool> onCompleted)
        {
            if (_current is not null)
            {
                _current.Cancel();
                return;
            }

            PInvoke.GetCursorPos(out DrawingPoint pt);
            var monitor = PInvoke.MonitorFromPoint(pt, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
            MONITORINFO info = new MONITORINFO();
            info.cbSize = 40;
            PInvoke.GetMonitorInfo(monitor, ref info);
            int width = info.rcMonitor.right - info.rcMonitor.left;
            int height = info.rcMonitor.bottom - info.rcMonitor.top;

            ScreenCaptureResult? capture = ScreenCaptureService.CaptureRegion(info.rcMonitor.left, info.rcMonitor.top, width, height);
            if (capture is null)
            {
                onCompleted(null, false);
                return;
            }

            var window = new ScreenCaptureWindow(capture, onCompleted);
            _current = window;
            window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(info.rcMonitor.left, info.rcMonitor.top, width, height));
            window.Activate();
            window.BringToFront();
            window.RootGrid.Focus(FocusState.Programmatic);
        }

        public static void CancelCurrent()
        {
            _current?.Cancel();
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref int value, uint size);

        private void Cancel()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            this.Close();
            _onCompleted(null, true);
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }

            _bmpStream?.Dispose();
            _bmpStream = null;

            if (!_finished)
            {
                _finished = true;
                _onCompleted(null, true);
            }
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(RootGrid);
            if (point.Properties.IsRightButtonPressed)
            {
                Cancel();
                return;
            }

            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            _startDip = point.Position;
            _dragging = true;
            RootGrid.CapturePointer(e.Pointer);
            UpdateSelectionBorder(_startDip, _startDip);
            e.Handled = true;
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_dragging)
            {
                UpdateSelectionBorder(_startDip, e.GetCurrentPoint(RootGrid).Position);
            }
        }

        private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            Point end = e.GetCurrentPoint(RootGrid).Position;
            RootGrid.ReleasePointerCapture(e.Pointer);

            double width = Math.Abs(end.X - _startDip.X);
            double height = Math.Abs(end.Y - _startDip.Y);
            if (width < ClickThresholdDip && height < ClickThresholdDip)
            {
                FinishAsync(new Rect(end.X, end.Y, 0, 0), isClick: true);
            }
            else
            {
                FinishAsync(new Rect(Math.Min(_startDip.X, end.X), Math.Min(_startDip.Y, end.Y), width, height), isClick: false);
            }

            e.Handled = true;
        }

        private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            _dragging = false;
            SelectionBorder.Visibility = Visibility.Collapsed;
        }

        private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _dragging = false;
            SelectionBorder.Visibility = Visibility.Collapsed;
        }

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                Cancel();
            }
        }

        private void UpdateSelectionBorder(Point a, Point b)
        {
            double left = Math.Min(a.X, b.X);
            double top = Math.Min(a.Y, b.Y);
            SelectionBorder.Margin = new Thickness(left, top, 0, 0);
            SelectionBorder.Width = Math.Abs(a.X - b.X);
            SelectionBorder.Height = Math.Abs(a.Y - b.Y);
            SelectionBorder.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 裁剪选中区域（单击时为光标邻域）→ OCR → 词命中/整段文本 → 关窗回调
        /// </summary>
        private async void FinishAsync(Rect dipRect, bool isClick)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            string? text = null;
            try
            {
                // 覆盖窗内容按 DPI 缩放，用实际渲染比例把 DIP 换算回截图物理像素
                double sx = _capture.Width / RootGrid.ActualWidth;
                double sy = _capture.Height / RootGrid.ActualHeight;

                if (isClick)
                {
                    int cx = (int)(dipRect.X * sx);
                    int cy = (int)(dipRect.Y * sy);
                    int halfWidth = (int)(ClickCropHalfWidthDip * sx);
                    int halfHeight = (int)(ClickCropHalfHeightDip * sy);
                    int originX = Math.Clamp(cx - halfWidth, 0, _capture.Width);
                    int originY = Math.Clamp(cy - halfHeight, 0, _capture.Height);
                    ScreenCaptureResult? crop = ScreenCaptureService.Crop(_capture, cx - halfWidth, cy - halfHeight, halfWidth * 2, halfHeight * 2);
                    if (crop is not null)
                    {
                        OcrCaptureResult? ocr = await OcrService.RecognizeAsync(crop);
                        CapturedWord? hit = ocr is null
                            ? null
                            : ScreenWordHitTest.FindNearestWord(ocr.Words, cx - originX, cy - originY, WordHitMaxDistanceDip * sx);
                        text = hit?.Text;
                    }
                }
                else
                {
                    ScreenCaptureResult? crop = ScreenCaptureService.Crop(_capture,
                        (int)(dipRect.X * sx), (int)(dipRect.Y * sy),
                        (int)(dipRect.Width * sx), (int)(dipRect.Height * sy));
                    if (crop is not null)
                    {
                        OcrCaptureResult? ocr = await OcrService.RecognizeAsync(crop);
                        if (ocr is not null)
                        {
                            text = ocr.Words.Count == 1 ? ocr.Words[0].Text : ocr.Text;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
            }

            this.Close();
            _onCompleted(string.IsNullOrWhiteSpace(text) ? null : text.Trim(), false);
        }
    }

    /// <summary>
    /// 十字光标根容器（ProtectedCursor 只能在派生类内设置）
    /// </summary>
    public sealed class CrosshairGrid : Microsoft.UI.Xaml.Controls.Grid
    {
        public CrosshairGrid()
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
        }
    }
}