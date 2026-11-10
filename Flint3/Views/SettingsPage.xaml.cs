using System;
using System.Collections.ObjectModel;
using System.Linq;
using Flint3.Data.Mdx;
using Flint3.Dict;
using Flint3.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Flint3.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        private MainViewModel _viewModel = null;

        private readonly ObservableCollection<MdxDictionaryInfo> _dictionaries = new();

        public SettingsPage()
        {
            _viewModel = MainViewModel.Instance;
            this.InitializeComponent();

            // x:Load 的元素在页面加载后才可访问，列表初始化放到 Loaded
            this.Loaded += OnPageLoaded;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            DictionaryListView.ItemsSource = _dictionaries;
            LoadDictionaries();
        }

        /// <summary>
        /// 从磁盘重新读取 mdx 词库登记信息并刷新列表
        /// </summary>
        private void LoadDictionaries()
        {
            _dictionaries.Clear();
            foreach (MdxDictionaryInfo info in _viewModel.DictionaryStore.Load().Dictionaries)
            {
                _dictionaries.Add(info);
            }

            DictionaryEmptyTextBlock.Visibility = _dictionaries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            DictionarySummaryTextBlock.Text = _dictionaries.Count == 0
                ? "导入 mdx 词库以扩展查词结果"
                : $"已导入 {_dictionaries.Count} 个词库，点击展开管理";
        }

        /// <summary>
        /// 导入 mdx 词库：复制文件、建立 sidecar 索引后热更新查询源
        /// </summary>
        private async void OnClickImportDictionary(object sender, RoutedEventArgs e)
        {
            FileOpenPicker picker = new()
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.Downloads,
            };
            picker.FileTypeFilter.Add(".mdx");

            // 免打包应用必须先绑定窗口句柄，选择器才能显示
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.FlintMainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            StorageFile file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            ImportDictionaryButton.IsEnabled = false;
            ImportProgressBar.Visibility = Visibility.Visible;
            ImportProgressBar.Value = 0;
            DictionaryInfoBar.IsOpen = false;

            try
            {
                Progress<double> progress = new(value => ImportProgressBar.Value = value * 100);
                MdxDictionaryInfo info = await _viewModel.DictionaryStore.ImportAsync(file.Path, progress);

                LoadDictionaries();
                _viewModel.RefreshDictionarySources();
                ShowDictionaryMessage($"已导入「{info.Name}」，共 {info.EntryCount} 个词条", InfoBarSeverity.Success);
            }
            catch (MdxUnsupportedException ex)
            {
                ShowDictionaryMessage(ex.Message, InfoBarSeverity.Warning);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
                ShowDictionaryMessage($"导入失败：{ex.Message}", InfoBarSeverity.Error);
            }
            finally
            {
                ImportDictionaryButton.IsEnabled = true;
                ImportProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 启用/停用某个词库
        /// </summary>
        private void OnDictionaryEnabledToggled(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleSwitch toggle || toggle.Tag is not string id)
            {
                return;
            }

            MdxDictionaryInfo info = _dictionaries.FirstOrDefault(item => item.Id == id);
            // 绑定初始化时也会触发 Toggled，状态一致直接返回
            if (info is null || info.Enabled == toggle.IsOn)
            {
                return;
            }

            info.Enabled = toggle.IsOn;
            _viewModel.DictionaryStore.SetEnabled(info, toggle.IsOn);
            _viewModel.RefreshDictionarySources();
        }

        /// <summary>
        /// 删除词库及其索引
        /// </summary>
        private async void OnClickRemoveDictionary(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string id)
            {
                return;
            }

            MdxDictionaryInfo info = _dictionaries.FirstOrDefault(item => item.Id == id);
            if (info is null)
            {
                return;
            }

            ContentDialog dialog = new()
            {
                Title = "删除词库",
                Content = $"确定要删除「{info.Name}」吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            _viewModel.DictionaryStore.Remove(info);
            LoadDictionaries();
            _viewModel.RefreshDictionarySources();
            ShowDictionaryMessage($"已删除「{info.Name}」", InfoBarSeverity.Informational);
        }

        private void ShowDictionaryMessage(string message, InfoBarSeverity severity)
        {
            DictionaryInfoBar.Severity = severity;
            DictionaryInfoBar.Message = message;
            DictionaryInfoBar.IsOpen = true;
        }

        /// <summary>
        /// 返回
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnClickBackButton(object sender, RoutedEventArgs e)
        {
            if (this.Frame.CanGoBack)
            {
                this.Frame.GoBack();
            }
        }

        /// <summary>
        /// 打分评价
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void OnClickGoToStoreRate(object sender, RoutedEventArgs e)
        {
            try
            {
                await Launcher.LaunchUriAsync(new Uri($"ms-windows-store:REVIEW?PFN={Package.Current.Id.FamilyName}"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// 打开生词本文件目录
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void OnClickDbPath(object sender, RoutedEventArgs e)
        {
            try
            {
                StorageFolder documentsFolder = await StorageFolder.GetFolderFromPathAsync(UserDataPaths.GetDefault().Documents);
                var noMewingFolder = await documentsFolder.CreateFolderAsync("NoMewing", CreationCollisionOption.OpenIfExists);
                var flintFolder = await noMewingFolder.CreateFolderAsync("Flint", CreationCollisionOption.OpenIfExists);
                await Launcher.LaunchFolderAsync(flintFolder);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// 开机自启设置卡加载完成时，获取当前开机自启动状态，更新按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnStartupSettingsCardLoaded(object sender, RoutedEventArgs e)
        {
            UpdateStartupState();
        }

        /// <summary>
        /// 设置开机自启
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void OnStartupToggled(object sender, RoutedEventArgs e)
        {
            try
            {
                var task = await StartupTask.GetAsync("FlintByNoMewing");
                switch (task.State)
                {
                    case StartupTaskState.Disabled:
                        {
                            if (StartupSettingToggleSwitch.IsOn != false)
                            {
                                await task.RequestEnableAsync();
                            }
                        }
                        break;
                    case StartupTaskState.Enabled:
                        {
                            if (StartupSettingToggleSwitch.IsOn != true)
                            {
                                task.Disable();
                            }
                        }
                        break;
                }

                UpdateStartupState();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// 选择搜索框样式
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnSearchBoxStyleChecked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is RadioButton rb)
                {
                    string tag = rb?.Tag?.ToString();
                    switch (tag)
                    {
                        case "新颖":
                            _viewModel.AppSettings.SearchBoxStyle = 0;
                            break;
                        case "圆角":
                            _viewModel.AppSettings.SearchBoxStyle = 1;
                            break;
                        case "经典":
                            _viewModel.AppSettings.SearchBoxStyle = 2;
                            break;
                        default:
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// 访问 ECDICT on GitHub
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void OnClickGitHub(object sender, RoutedEventArgs e)
        {
            try
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/skywind3000/ECDICT"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// 访问 燧石 on GitHub
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void OnClickFlintGitHub(object sender, RoutedEventArgs e)
        {
            try
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/sh0ckj0ckey/Flint"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// 滚动页面时，显露顶部分割线
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                var verticalOffset = scrollViewer.VerticalOffset;
                var maxOffset = Math.Min(160, scrollViewer.ScrollableHeight);

                if (maxOffset <= 0) return;

                // 透明度按滚动比例变化，从全透明到不透明
                double newOpacity = verticalOffset / maxOffset;
                if (newOpacity > 1)
                {
                    newOpacity = 1;
                }
                if (newOpacity < 0)
                {
                    newOpacity = 0;
                }

                SettingsPageHeaderTitleGrid.Opacity = newOpacity;
                SettingsPageHeaderSeperatorLineBorder.Opacity = newOpacity;
            }
        }

        /// <summary>
        /// 展开 TTS 设置卡时，加载声音列表
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnTTSSettingsExpanderExpanded(object sender, EventArgs e)
        {
            var voices = Flint3.Helpers.TextToSpeechHelper.GetAllVoices();
            TTSVoicesComboBox.ItemsSource = voices;
            TTSVoicesComboBox.SelectedIndex = voices.IndexOf(MainViewModel.Instance.AppSettings.TTSVoice);
        }

        /// <summary>
        /// 选择 TTS 语音
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnSelectedVoiceChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                var selectedVoice = e.AddedItems[0].ToString();
                MainViewModel.Instance.AppSettings.TTSVoice = selectedVoice;
            }
        }

        /// <summary>
        /// 点击试听声音
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnClickHearVoice(object sender, RoutedEventArgs e)
        {
            try
            {
                _ = Flint3.Helpers.TextToSpeechHelper.SpeakTextAsync(
                    "The quick brown fox jumps over the lazy dog.",
                    MainViewModel.Instance.AppSettings.TTSVolume / 10.0,
                    MainViewModel.Instance.AppSettings.TTSVoice);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
            }
        }

        /// <summary>
        /// 获取当前开机自启动状态，更新按钮
        /// </summary>
        private async void UpdateStartupState()
        {
            try
            {
                var task = await StartupTask.GetAsync("FlintByNoMewing");
                switch (task.State)
                {
                    case StartupTaskState.Disabled:
                        StartupSettingToggleSwitch.IsEnabled = true;
                        StartupSettingToggleSwitch.IsOn = false;
                        StartupDescTextBlock.Text = "登录到 Windows 时自动启动燧石";
                        break;
                    case StartupTaskState.Enabled:
                        StartupSettingToggleSwitch.IsEnabled = true;
                        StartupSettingToggleSwitch.IsOn = true;
                        StartupDescTextBlock.Text = "登录到 Windows 时自动启动燧石";
                        break;
                    case StartupTaskState.DisabledByUser:
                        StartupSettingToggleSwitch.IsEnabled = false;
                        StartupSettingToggleSwitch.IsOn = false;
                        StartupDescTextBlock.Text = "无法修改，已被系统设置禁用";
                        break;
                    case StartupTaskState.DisabledByPolicy:
                        StartupSettingToggleSwitch.IsEnabled = false;
                        StartupSettingToggleSwitch.IsOn = false;
                        StartupDescTextBlock.Text = "无法修改，已被策略禁用";
                        break;
                    case StartupTaskState.EnabledByPolicy:
                        StartupSettingToggleSwitch.IsEnabled = false;
                        StartupSettingToggleSwitch.IsOn = true;
                        StartupDescTextBlock.Text = "无法修改，已被策略启用";
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex.Message);
            }
        }

    }
}
