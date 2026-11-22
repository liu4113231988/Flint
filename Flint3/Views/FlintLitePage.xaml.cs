using Flint3.Controls;
using Flint3.Data.Models;
using Flint3.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Flint3.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class FlintLitePage : Page
    {
        public MainViewModel ViewModel { get; set; } = null;

        private Flyout _addToGlossaryFlyout = null;

        private AddToGlossaryControl _addToGlossaryControl = null;

        public FlintLitePage()
        {
            ViewModel = MainViewModel.Instance;
            this.InitializeComponent();

        }

        /// <summary>
        /// 文本框有内容时窗口变大，无内容时只显示搜索框
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchTextBox_TextChanging(TextBox sender, TextBoxTextChangingEventArgs e)
        {
            bool hasText = !string.IsNullOrWhiteSpace(sender.Text);
            TranslateOriginalButton.IsEnabled = hasText;
            if (_settingCapturedText)
            {
                return;
            }

            _capturedText = null;
            CancelPendingTranslation();
            App.ResizeLiteWindow(expanded: hasText);
            ViewModel.MatchWord(sender.Text, false);
        }

        private string? _capturedText;
        private bool _settingCapturedText;

        internal void CancelPendingTranslation()
        {
            ViewModel.ClearLiteTranslation();
        }

        internal void ClearInput()
        {
            if (SearchTextBox.Text.Length == 0)
            {
                CancelPendingTranslation();
                ViewModel.MatchWord(string.Empty, false);
            }
            else
            {
                SearchTextBox.Text = string.Empty;
            }
        }

        internal async void ShowCapturedText(string text)
        {
            _settingCapturedText = true;
            try
            {
                SearchTextBox.Text = text.ReplaceLineEndings(" ");
            }
            finally
            {
                _settingCapturedText = false;
            }

            _capturedText = text;
            CancelPendingTranslation();
            await ViewModel.PreviewCapturedTextAsync(text);
        }

        private async void OnClickTranslateOriginal(object sender, RoutedEventArgs e)
        {
            await ViewModel.TranslateLiteTextAsync(_capturedText ?? SearchTextBox.Text);
        }

        private static void TranslateInMainWindow(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                App.ShowMainWindow();
                App.FlintMainWindow?.TranslateText(text);
            }
        }

        /// <summary>
        /// 点击显示主窗口
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnClickShowMainWindow(object sender, RoutedEventArgs e)
        {
            App.ShowMainWindow();
        }

        private void OnClickClosePreview(object sender, RoutedEventArgs e)
        {
            App.FlintLiteWindow?.Dismiss();
        }

        /// <summary>
        /// 在主窗中 AI 翻译该词条：打开主窗、回填搜索框并直接发起翻译
        /// </summary>
        private void OnClickTranslateInMainWindow(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is StarDictWordItem item && !string.IsNullOrWhiteSpace(item.Word))
            {
                TranslateInMainWindow(item.Word);
            }
        }

        private void OnClickAddWordToGlossary(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is StarDictWordItem item)
            {
                _addToGlossaryControl ??= new AddToGlossaryControl(() => { _addToGlossaryFlyout?.Hide(); });
                _addToGlossaryFlyout ??= new Flyout() { Content = _addToGlossaryControl };
                _addToGlossaryControl.PrepareToAddWord(item);

                FlyoutBase.SetAttachedFlyout(btn, _addToGlossaryFlyout);
                FlyoutBase.ShowAttachedFlyout(btn);
            }
        }

        /// <summary>
        /// 朗读单词
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnClickReadWord(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.DataContext is StarDictWordItem item && !string.IsNullOrWhiteSpace(item.Word))
                {
                    _ = Flint3.Helpers.TextToSpeechHelper.SpeakTextAsync(
                        item.Word,
                        MainViewModel.Instance.AppSettings.TTSVolume / 10.0,
                        MainViewModel.Instance.AppSettings.TTSVoice);
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
            }
        }
    }
}
