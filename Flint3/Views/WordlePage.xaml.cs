using System;
using Flint3.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Flint3.Views
{
    /// <summary>
    /// 内置 Wordle：猜测面板与键盘只做展示，判定与进度由 WordleViewModel/WordleGame 负责
    /// </summary>
    public sealed partial class WordlePage : Page
    {
        public WordleViewModel ViewModel { get; }

        public WordlePage()
        {
            ViewModel = new WordleViewModel();
            this.InitializeComponent();
            this.PreviewKeyDown += OnPreviewKeyDown;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // 页面自己持有键盘焦点，物理键盘输入不必依赖点击某个控件
            this.Focus(FocusState.Programmatic);
        }

        private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (TryHandleKey(e.Key))
            {
                e.Handled = true;
            }
        }

        private bool TryHandleKey(VirtualKey key)
        {
            int value = (int)key;
            if (value >= (int)VirtualKey.A && value <= (int)VirtualKey.Z)
            {
                ViewModel.InputLetter((char)('a' + (value - (int)VirtualKey.A)));
                return true;
            }

            if (key == VirtualKey.Enter)
            {
                ViewModel.Submit();
                return true;
            }

            if (key == VirtualKey.Back)
            {
                ViewModel.Backspace();
                return true;
            }

            return false;
        }

        private void OnKeyTapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string letter && letter.Length == 1)
            {
                ViewModel.InputLetter(letter[0]);
            }
        }

        private void OnClickSubmit(object sender, RoutedEventArgs e)
        {
            ViewModel.Submit();
        }

        private void OnClickBackspace(object sender, RoutedEventArgs e)
        {
            ViewModel.Backspace();
        }

        private void OnClickReset(object sender, RoutedEventArgs e)
        {
            ViewModel.Reset();
        }

        private void OnClickBackButton(object sender, RoutedEventArgs e)
        {
            try
            {
                if (this.Frame.CanGoBack)
                {
                    this.Frame.GoBack();
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }
    }
}
