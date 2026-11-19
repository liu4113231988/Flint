using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using Flint3.Data;
using Flint3.Helpers;
using Flint3.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.Win32;
using Windows.Win32.Foundation;
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

        /// <summary>
        /// 应用程序系统托盘
        /// </summary>
        public static NotifyIcon NotifyIcon { get; private set; } = null;

        /// <summary>
        /// 燧石的主窗口，点击桌面图标或者右下角托盘时一定打开这个窗口
        /// </summary>
        public static MainWindow FlintMainWindow { get; private set; } = new MainWindow();

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

            CreateTrayIcon();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            AppActivationArguments activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activatedArgs?.Kind == ExtendedActivationKind.StartupTask)
            {
                HideApp();
            }
            else
            {
                await ShowMainWindowFromRedirectAsync();
            }
        }

        /// <summary>
        /// 重定向唤起主窗口
        /// </summary>
        /// <returns></returns>
        public async Task ShowMainWindowFromRedirectAsync()
        {
            while (FlintMainWindow == null)
            {
                await Task.Delay(100);
            }

            _dispatcherQueue.TryEnqueue(() =>
            {
                ShowMainWindow();
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

                // 右键菜单"自动划词"勾选开关（菜单在 UI 线程弹出，直接切换即可）
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
            FlintLiteWindow?.Hide();
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
            FlintLiteWindow?.Hide();

            FlintMainWindow ??= new MainWindow();

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
        public static void ShowLiteWindow(bool atCursor = false)
        {
            FlintMainWindow?.Hide();

            FlintLiteWindow ??= new LiteWindow();

            if (MainViewModel.Instance.AppSettings.AutoClearLastInput)
            {
                MainViewModel.Instance.LiteSearchResultWordItems.Clear();
                FlintLiteWindow.TryClearSearchTextBox();
            }

            // 更新窗口位置
            {
                bool searchEmpty = string.IsNullOrWhiteSpace(FlintLiteWindow.TryGetSearchTextBoxContent());

                // 获取鼠标当前所在的显示器
                PInvoke.GetCursorPos(out Point pt);
                var hwndDesktop = PInvoke.MonitorFromPoint(pt, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);

                // 获取当前激活窗口所在的显示器
                //IntPtr focusedWindow = PInvoke.GetForegroundWindow();
                //var hwndDesktop = PInvoke.MonitorFromWindow(new(focusedWindow), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);

                MONITORINFO info = new MONITORINFO();
                info.cbSize = 40;
                PInvoke.GetMonitorInfo(hwndDesktop, ref info);
                int width = 540;
                int height = searchEmpty ? 64 : 386;

                PInvoke.GetDpiForMonitor(hwndDesktop, Windows.Win32.UI.HiDpi.MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
                var scalingFactor = dpiX / 96d;

                //var dpi = PInvoke.GetDpiForWindow(new HWND(focusedWindow));
                //var scalingFactor = dpi / 96d;

                var w = (int)(width * scalingFactor);
                var h = (int)(height * scalingFactor);
                int left, top;
                if (atCursor)
                {
                    // 光标右下方弹出，超出工作区时翻转到另一侧
                    left = pt.X + 16;
                    top = pt.Y + 16;
                    if (left + w > info.rcWork.right)
                    {
                        left = Math.Max(info.rcWork.left, pt.X - w - 16);
                    }

                    if (top + h > info.rcWork.bottom)
                    {
                        top = Math.Max(info.rcWork.top, pt.Y - h - 16);
                    }
                }
                else
                {
                    var cx = (info.rcMonitor.left + info.rcMonitor.right) / 2;
                    var cy = (info.rcMonitor.bottom + info.rcMonitor.top) / 4;
                    left = cx - (w / 2);
                    top = cy;
                }

                IntPtr hwnd = FlintLiteWindow.GetWindowHandle();
                bool result = PInvoke.SetWindowPos(new HWND(hwnd), new HWND(), left, top, w, h, (SET_WINDOW_POS_FLAGS)0);
                //if (!result)
                //{
                //    Marshal.ThrowExceptionForHR(Marshal.GetLastWin32Error());
                //}
            }

            FlintLiteWindow.Restore();
            FlintLiteWindow.BringToFront();
            FlintLiteWindow.Activate();

            if (MainViewModel.Instance.AppSettings.AutoClearLastInput)
            {
                MainViewModel.Instance.LiteSearchResultWordItems.Clear();
                FlintLiteWindow.TryClearSearchTextBox();
            }
        }

    }
}
