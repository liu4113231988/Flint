using System;
using System.Drawing;
using Flint3.Core.Capture;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using WinUIEx;

namespace Flint3
{
    internal sealed partial class SelectionIconWindow : WindowEx
    {
        private readonly DispatcherQueueTimer _timer;
        private Rectangle _bounds;
        private HWND _owner;
        private long _expiresAt;
        private Action? _openResult;

        public SelectionIconWindow()
        {
            Title = "燧石划词";
            IsShownInSwitchers = false;
            var presenter = OverlappedPresenter.CreateForToolWindow();
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.PreferredMinimumWidth = 1;
            presenter.PreferredMinimumHeight = 1;
            AppWindow.SetPresenter(presenter);
            App.DisablePopupTransitions(this.GetWindowHandle());
            MinWidth = 1;
            MinHeight = 1;

            Button button = new()
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Padding = new Thickness(0),
                Content = new Image
                {
                    Width = 24,
                    Height = 24,
                    Source = new BitmapImage(new Uri("ms-appx:///Assets/Logos/flint_logo.png"))
                }
            };
            AutomationProperties.SetName(button, "打开划词翻译");
            ToolTipService.SetToolTip(button, "打开划词翻译");
            button.Click += (_, _) => OpenResult();
            Content = button;

            _timer = DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(250);
            _timer.Tick += (_, _) =>
            {
                if (!IsCurrent())
                {
                    Dismiss();
                }
            };
        }

        internal void ShowAt(Point pointer, HWND owner, Action openResult)
        {
            if (!App.TryGetPointerWorkArea(pointer, out Rectangle workArea, out uint dpi))
            {
                return;
            }

            Rectangle preferred = LiteWindowPlacement.PlaceSelectionIcon(workArea, dpi, pointer);
            _owner = owner;
            _openResult = openResult;
            _expiresAt = Environment.TickCount64 + 15_000;
            var hwnd = new HWND(this.GetWindowHandle());
            // 普通窗口的最小跟踪尺寸会把划词图标撑宽。
            if (!PInvoke.SetWindowPos(hwnd, default, preferred.X, preferred.Y, preferred.Width, preferred.Height,
                    SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER |
                    SET_WINDOW_POS_FLAGS.SWP_NOSENDCHANGING | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW))
            {
                Dismiss();
                return;
            }
            var location = AppWindow.Position;
            var size = AppWindow.Size;
            _bounds = new Rectangle(location.X, location.Y, size.Width, size.Height);
            _timer.Start();
        }

        internal bool Contains(Point pointer) => _bounds.Contains(pointer) && IsCurrent();

        private bool IsCurrent()
        {
            if (_openResult is null || Environment.TickCount64 >= _expiresAt)
            {
                return false;
            }

            HWND foreground = PInvoke.GetForegroundWindow();
            return foreground == _owner || foreground == new HWND(this.GetWindowHandle());
        }

        internal void Dismiss()
        {
            _timer.Stop();
            _openResult = null;
            AppWindow.Hide();
        }

        private void OpenResult()
        {
            Action? openResult = IsCurrent() ? _openResult : null;
            Dismiss();
            openResult?.Invoke();
        }
    }
}
