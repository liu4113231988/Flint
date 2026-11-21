using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Flint3.Core.Capture;
using Flint3.Helpers;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using WinUIEx;
using DrawingPoint = System.Drawing.Point;

namespace Flint3
{
    internal enum ScreenRecognitionResult
    {
        Success,
        NoText,
        Failed
    }

    /// <summary>
    /// 屏幕取词覆盖窗：冻结光标所在显示器的截图，拖选区域或单击文字取词，Esc/右键取消。
    /// </summary>
    public sealed partial class ScreenCaptureWindow : WindowEx
    {
        private const double ClickThresholdDip = 6;
        private const double ClickCropHalfWidthDip = 160;
        private const double ClickCropHalfHeightDip = 48;
        private const double WordHitMaxDistanceDip = 24;

        private static ScreenCaptureWindow? _current;

        private readonly ScreenCaptureResult _capture;
        private readonly OcrEngine _engine;
        private readonly Action<ScreenRecognitionResult, string?, DrawingPoint?> _onCompleted;
        private CancellationTokenSource? _recognitionCancellation;
        private Point _startDip;
        private bool _dragging;
        private bool _closed;

        public static bool IsOpen => _current is not null;

        private ScreenCaptureWindow(ScreenCaptureResult capture, OcrEngine engine, Action<ScreenRecognitionResult, string?, DrawingPoint?> onCompleted)
        {
            _capture = capture;
            _engine = engine;
            _onCompleted = onCompleted;

            this.InitializeComponent();
            this.IsShownInSwitchers = false;
            this.ExtendsContentIntoTitleBar = true;
            this.IsTitleBarVisible = false;
            this.IsAlwaysOnTop = true;
            this.IsResizable = false;
            this.IsMaximizable = false;
            this.IsMinimizable = false;

            using var bmp = new MemoryStream(ScreenCaptureService.EncodeBmp(capture));
            using IRandomAccessStream stream = bmp.AsRandomAccessStream();
            var image = new BitmapImage();
            image.SetSource(stream);
            ScreenshotImage.Source = image;

            // 关闭 Win11 圆角，避免截图四角被裁掉（DWMWA_WINDOW_CORNER_PREFERENCE = 33，DWMWCP_DONOTROUND = 1）
            int preference = 1;
            DwmSetWindowAttribute(this.GetWindowHandle(), 33, ref preference, sizeof(int));

            this.Closed += OnClosed;
        }

        /// <summary>
        /// 截取光标所在显示器并弹出覆盖窗。
        /// </summary>
        internal static bool TryShow(OcrEngine engine, Action<ScreenRecognitionResult, string?, DrawingPoint?> onCompleted)
        {
            if (!PInvoke.GetCursorPos(out DrawingPoint pt))
            {
                return false;
            }

            var monitor = PInvoke.MonitorFromPoint(pt, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
            MONITORINFO info = new() { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            if (!PInvoke.GetMonitorInfo(monitor, ref info))
            {
                return false;
            }

            int width = info.rcMonitor.right - info.rcMonitor.left;
            int height = info.rcMonitor.bottom - info.rcMonitor.top;

            // 等待刚隐藏的取词窗口从桌面画面移除，再读取屏幕像素。
            PInvoke.DwmFlush();
            ScreenCaptureResult? capture = ScreenCaptureService.CaptureRegion(info.rcMonitor.left, info.rcMonitor.top, width, height);
            if (capture is null)
            {
                return false;
            }

            var window = new ScreenCaptureWindow(capture, engine, onCompleted);
            _current = window;
            window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(info.rcMonitor.left, info.rcMonitor.top, width, height));
            window.Activate();
            window.BringToFront();
            return true;
        }

        public static void CancelCurrent()
        {
            _current?.Close();
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref int value, uint size);

        private void OnClosed(object sender, WindowEventArgs args)
        {
            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }

            _closed = true;
            _recognitionCancellation?.Cancel();
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(RootGrid);
            if (point.Properties.IsRightButtonPressed)
            {
                Close();
                return;
            }

            if (_recognitionCancellation is not null || !point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            _startDip = point.Position;
            _dragging = CaptureSurface.CapturePointer(e.Pointer);
            if (!_dragging)
            {
                return;
            }

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
            CaptureSurface.ReleasePointerCapture(e.Pointer);

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
            if (_dragging)
            {
                OnPointerCanceled(sender, e);
            }
        }

        private void OnRootLoaded(object sender, RoutedEventArgs e)
        {
            CancelButton.Focus(FocusState.Programmatic);
        }

        private void OnActivated(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
            {
                CancelButton?.Focus(FocusState.Programmatic);
            }
        }

        private void OnCancelInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs e)
        {
            e.Handled = true;
            Close();
        }

        private void OnClickCancel(object sender, RoutedEventArgs e) => Close();

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
            if (_closed || _recognitionCancellation is not null)
            {
                return;
            }

            using var cancellation = new CancellationTokenSource();
            _recognitionCancellation = cancellation;
            DrawingPoint? anchor = PInvoke.GetCursorPos(out DrawingPoint pointer) ? pointer : null;
            InstructionText.Text = "正在识别…";
            ScreenRecognitionResult result = ScreenRecognitionResult.NoText;
            string? text = null;
            try
            {
                // 覆盖窗内容按 DPI 缩放，用实际渲染比例把 DIP 换算回截图物理像素
                double sx = _capture.Width / RootGrid.ActualWidth;
                double sy = _capture.Height / RootGrid.ActualHeight;

                int x = (int)(dipRect.X * sx);
                int y = (int)(dipRect.Y * sy);
                int halfWidth = isClick ? (int)(ClickCropHalfWidthDip * sx) : 0;
                int halfHeight = isClick ? (int)(ClickCropHalfHeightDip * sy) : 0;
                int left = x - halfWidth;
                int top = y - halfHeight;
                int width = isClick ? halfWidth * 2 : (int)Math.Ceiling(dipRect.Right * sx) - x;
                int height = isClick ? halfHeight * 2 : (int)Math.Ceiling(dipRect.Bottom * sy) - y;
                ScreenCaptureResult? crop = ScreenCaptureService.Crop(_capture, left, top, width, height);
                if (crop is not null)
                {
                    OcrCaptureResult ocr = await OcrService.RecognizeAsync(_engine, crop, cancellation.Token);
                    text = isClick
                        ? ScreenWordHitTest.FindNearestWord(ocr.Words, x - Math.Max(0, left), y - Math.Max(0, top),
                            WordHitMaxDistanceDip * sx)?.Text
                        : ocr.Text;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        result = ScreenRecognitionResult.Success;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
                result = ScreenRecognitionResult.Failed;
            }
            finally
            {
                _recognitionCancellation = null;
            }

            if (!_closed)
            {
                Close();
                _onCompleted(result, text?.Trim(), anchor);
            }
        }
    }

    /// <summary>
    /// 十字光标根容器（ProtectedCursor 只能在派生类内设置）
    /// </summary>
    public sealed partial class CrosshairGrid : Microsoft.UI.Xaml.Controls.Grid
    {
        public CrosshairGrid()
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
        }
    }
}
