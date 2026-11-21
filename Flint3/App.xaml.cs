using System;
using System.Diagnostics;
using System.Drawing;
using Flint3.Core.Capture;
using Flint3.Data;
using Flint3.Helpers;
using Flint3.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using WinUIEx;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Flint3
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
        private bool _activateAfterLaunch;

        /// <summary>
        /// 应用程序系统托盘
        /// </summary>
        public static NotifyIcon NotifyIcon { get; private set; } = null;

        /// <summary>
        /// 燧石的主窗口，点击桌面图标或者右下角托盘时一定打开这个窗口
        /// </summary>
        public static MainWindow FlintMainWindow { get; private set; } = null!;

        /// <summary>
        /// 燧石的简洁搜索窗口，设置中开启简洁窗口后，按下快捷键会唤起这个窗口
        /// </summary>
        public static LiteWindow FlintLiteWindow { get; private set; } = null;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();

            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            UnhandledException += (s, e) =>
            {
                e.Handled = true;
                Trace.WriteLine(e.Message);
            };
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            if (FlintMainWindow is not null)
            {
                return;
            }

            FlintMainWindow = new MainWindow();
            CreateTrayIcon();

            AppActivationArguments activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activatedArgs?.Kind == ExtendedActivationKind.StartupTask && !_activateAfterLaunch)
            {
                HideApp();
            }
            else
            {
                ShowMainWindow();
            }
        }

        internal void ActivateExistingWindow()
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (FlintMainWindow is not MainWindow window)
                {
                    _activateAfterLaunch = true;
                    return;
                }

                if (PInvoke.IsIconic(new HWND(window.GetWindowHandle())))
                {
                    window.Restore();
                    return;
                }

                PInvoke.GetWindowThreadProcessId(PInvoke.GetForegroundWindow(), out uint foregroundProcessId);
                if (foregroundProcessId == Environment.ProcessId)
                {
                    return;
                }

                window.Activate();
            });
        }

        /// <summary>
        /// 创建系统托盘图标
        /// </summary>
        private void CreateTrayIcon()
        {
            try
            {
                if (NotifyIcon is not null)
                {
                    return;
                }

                // 创建常驻托盘图标
                var hwndMain = FlintMainWindow.GetWindowHandle();
                NotifyIcon = new NotifyIcon(hwndMain, @"Assets\Logos\flint_logo.ico");
                NotifyIcon.OnClickShowMainWindow += () =>
                {
                    ShowMainWindow();
                };

                NotifyIcon.OnClickExitApp += () =>
                {
                    ExitApp();
                };

                NotifyIcon.OnClickCloseWindow += () =>
                {
                    if (MainViewModel.Instance.AppSettings.CloseButtonMode == 0)
                    {
                        HideApp();
                    }
                    else
                    {
                        ExitApp();
                    }
                };

                // 托盘菜单与设置页共用划词图标开关。
                NotifyIcon.AutoSelectionStateProvider = () => MainViewModel.Instance.SelectionAutoEnabled;
                NotifyIcon.OnClickToggleAutoSelection += () =>
                {
                    MainViewModel.Instance.SelectionAutoEnabled = !MainViewModel.Instance.SelectionAutoEnabled;
                };

                NotifyIcon.CreateNotifyIcon();
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.ToString());
            }
        }

        /// <summary>
        /// 隐藏燧石的所有窗口
        /// </summary>
        public static void HideApp()
        {
            ScreenCaptureWindow.CancelCurrent();
            MainViewModel.Instance.DismissSelectionIcon();
            FlintLiteWindow?.Dismiss();
            FlintMainWindow?.Hide();

            // 弹出系统通知
            // TrayMessage(hWndMain, null, m_hIcon, m_hBalloonIcon, NIM.MODIFY, NIIF.USER | NIIF.LARGE_ICON, "按下快捷键或点击图标可以唤出窗口", "燧石 已隐藏至系统托盘", 0);
        }

        /// <summary>
        /// 退出燧石
        /// </summary>
        public static void ExitApp()
        {
            try
            {
                HideApp();
                GlossaryDataAccess.CloseDatabase();
                StarDictDataAccess.CloseDatabase();
                NotifyIcon?.Destroy();
                FlintLiteWindow?.Close();
                FlintMainWindow?.Close();
                MainViewModel.Instance.Dispose();
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }

            // Environment.Exit(0);
            Application.Current.Exit();
        }

        /// <summary>
        /// 弹出燧石主窗口
        /// </summary>
        public static void ShowMainWindow()
        {
            ScreenCaptureWindow.CancelCurrent();
            MainViewModel.Instance.DismissSelectionIcon();
            FlintLiteWindow?.Dismiss();

            if (MainViewModel.Instance.AppSettings.AutoClearLastInput)
            {
                MainViewModel.Instance.SearchResultWordItems.Clear();
                FlintMainWindow.TryClearSearchTextBox();
                FlintMainWindow.CenterOnScreen();
            }

            FlintMainWindow.Restore();
            FlintMainWindow.BringToFront();
            FlintMainWindow.Activate();

            if (MainViewModel.Instance.AppSettings.AutoClearLastInput)
            {
                MainViewModel.Instance.SearchResultWordItems.Clear();
                FlintMainWindow.TryClearSearchTextBox();
                FlintMainWindow.CenterOnScreen();
            }
        }

        /// <summary>
        /// 弹出燧石简洁窗口；atCursor 为 true 时贴近鼠标位置（划词翻译），否则在显示器上方居中
        /// </summary>
        public static void ShowLiteWindow(bool atCursor = false, Point? anchor = null)
        {
            ScreenCaptureWindow.CancelCurrent();
            MainViewModel.Instance.DismissSelectionIcon();
            FlintMainWindow?.Hide();

            FlintLiteWindow ??= new LiteWindow();
            FlintLiteWindow.IsShownInSwitchers = atCursor;

            if (MainViewModel.Instance.AppSettings.AutoClearLastInput)
            {
                MainViewModel.Instance.LiteSearchResultWordItems.Clear();
                FlintLiteWindow.TryClearSearchTextBox();
            }

            Point pointer = anchor.GetValueOrDefault();
            if ((anchor.HasValue || PInvoke.GetCursorPos(out pointer)) && TryGetPointerWorkArea(pointer, out Rectangle workArea, out uint dpi))
            {
                bool expanded = atCursor || !string.IsNullOrWhiteSpace(FlintLiteWindow.TryGetSearchTextBoxContent());
                SetLiteWindowBounds(new HWND(FlintLiteWindow.GetWindowHandle()),
                    LiteWindowPlacement.Place(workArea, dpi, expanded, atCursor ? pointer : null));
            }

            if (atCursor)
            {
                FlintLiteWindow.AppWindow.Show(activateWindow: false);
            }
            else
            {
                FlintLiteWindow.Restore();
                FlintLiteWindow.BringToFront();
                FlintLiteWindow.Activate();
            }
        }

        internal static bool TryGetPointerWorkArea(Point pointer, out Rectangle workArea, out uint dpi)
        {
            var monitor = PInvoke.MonitorFromPoint(pointer, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
            if (PInvoke.GetDpiForMonitor(monitor, Windows.Win32.UI.HiDpi.MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out dpi, out _).Failed)
            {
                dpi = 0;
            }

            return TryGetWorkArea(monitor, out workArea);
        }

        internal static unsafe void DisablePopupTransitions(IntPtr handle)
        {
            // 立即从桌面移除浮窗，避免屏幕取词截到淡出的窗口。
            int disabled = 1;
            PInvoke.DwmSetWindowAttribute(new HWND(handle), DWMWINDOWATTRIBUTE.DWMWA_TRANSITIONS_FORCEDISABLED,
                &disabled, sizeof(int));
        }

        internal static void ResizeLiteWindow(bool expanded)
        {
            if (FlintLiteWindow is not LiteWindow window)
            {
                return;
            }

            HWND hwnd = new HWND(window.GetWindowHandle());
            var monitor = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
            if (TryGetWorkArea(monitor, out Rectangle workArea))
            {
                var position = window.AppWindow.Position;
                SetLiteWindowBounds(hwnd, LiteWindowPlacement.Resize(workArea, PInvoke.GetDpiForWindow(hwnd),
                    expanded, new Point(position.X, position.Y)));
            }
        }

        private static bool TryGetWorkArea(HMONITOR monitor, out Rectangle workArea)
        {
            MONITORINFO info = new() { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            bool success = PInvoke.GetMonitorInfo(monitor, ref info);
            workArea = Rectangle.FromLTRB(info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom);
            return success && workArea.Width > 0 && workArea.Height > 0;
        }

        private static void SetLiteWindowBounds(HWND hwnd, Rectangle bounds)
        {
            PInvoke.SetWindowPos(hwnd, default, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
        }

    }
}
