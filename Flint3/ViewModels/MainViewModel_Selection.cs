using System;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Flint3.Core.Input;
using Flint3.Core.Serialization;
using Flint3.Helpers;

namespace Flint3.ViewModels
{
    /// <summary>
    /// 划词翻译：全局热键触发取词，单词/短语在光标旁的简洁窗口查词典，整句走已开启的 AI 翻译
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        private SelectionTranslationSettings _selectionSettings = new();

        private ushort _selectionHotkeyHandle;

        private AutoSelectionMonitor? _autoSelectionMonitor;

        private long _lastAutoCaptureTick;

        private const long AutoCaptureDebounceMs = 800;

        /// <summary>
        /// 是否启用划词翻译
        /// </summary>
        public bool SelectionTranslationEnabled
        {
            get => _selectionSettings.Enabled;
            set
            {
                if (_selectionSettings.Enabled != value)
                {
                    _selectionSettings.Enabled = value;
                    OnPropertyChanged();
                    UpdateSelectionRegistration();
                    _ = SaveSelectionSettings();
                }
            }
        }

        /// <summary>
        /// 划词取词热键，默认 Ctrl+Alt+D
        /// </summary>
        public HotkeySettings SelectionShortcut
        {
            get => _selectionSettings.Hotkey;
            set
            {
                HotkeySettings newValue = (value == null || !value.IsValid() || value.IsEmpty())
                    ? new SelectionTranslationSettings().Hotkey
                    : value;
                if (_selectionSettings.Hotkey?.ToString() != newValue.ToString())
                {
                    _selectionSettings.Hotkey = newValue;
                    OnPropertyChanged();
                    UpdateSelectionRegistration();
                    _ = SaveSelectionSettings();
                }
            }
        }

        /// <summary>
        /// 自动划词：选中文字后自动取词查询，无需按快捷键（默认关闭）
        /// </summary>
        public bool SelectionAutoEnabled
        {
            get => _selectionSettings.AutoEnabled;
            set
            {
                if (_selectionSettings.AutoEnabled != value)
                {
                    _selectionSettings.AutoEnabled = value;
                    OnPropertyChanged();
                    UpdateAutoSelectionMonitor();
                    _ = SaveSelectionSettings();
                }
            }
        }

        private async void InitViewModel4Selection()
        {
            await ReadSelectionSettings();
            UpdateSelectionRegistration();
            UpdateAutoSelectionMonitor();
        }

        /// <summary>
        /// 按当前开关与热键重新注册；与唤醒热键冲突时让位（HotkeyService 同组合键只保留一个回调）
        /// </summary>
        private void UpdateSelectionRegistration()
        {
            try
            {
                ushort activationHandle = HotkeyService.GetHotkeyHandle(ActivationShortcut ?? _defaultActivationShortcut);

                if (_selectionHotkeyHandle != 0 && _selectionHotkeyHandle != activationHandle)
                {
                    _hotkeyService?.Unregister(_selectionHotkeyHandle);
                }

                _selectionHotkeyHandle = 0;

                if (!SelectionTranslationEnabled || SelectionShortcut?.IsValid() != true)
                {
                    return;
                }

                ushort handle = HotkeyService.GetHotkeyHandle(SelectionShortcut);
                if (handle == activationHandle)
                {
                    return;
                }

                _hotkeyService ??= new HotkeyService();
                _selectionHotkeyHandle = _hotkeyService.Register(SelectionShortcut, OnSelectionHotkey);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
            }
            finally
            {
                // 划词热键变化后校准屏幕取词热键（同组合键冲突时取词让位）
                UpdateScreenCaptureRegistration();
            }
        }

        /// <summary>
        /// 钩子线程回调，切回 UI 线程发起取词，不阻塞钩子
        /// </summary>
        private void OnSelectionHotkey()
        {
            this.Dispatcher.TryEnqueue(async () => await CaptureAndShowSelectionAsync());
        }

        private async Task CaptureAndShowSelectionAsync()
        {
            string? text = await SelectionCaptureService.CaptureAsync();
            this.Dispatcher.TryEnqueue(() => ShowSelectionResult(text));
        }

        /// <summary>
        /// 按自动划词开关启停鼠标监视器；钩子须装在 UI 线程（本方法均在 UI 线程调用）
        /// </summary>
        private void UpdateAutoSelectionMonitor()
        {
            try
            {
                if (SelectionAutoEnabled)
                {
                    if (_autoSelectionMonitor is null)
                    {
                        _autoSelectionMonitor = new AutoSelectionMonitor();
                        _autoSelectionMonitor.SelectionGesture += OnAutoSelectionGesture;
                    }

                    _autoSelectionMonitor.Start();
                }
                else
                {
                    DisposeAutoSelectionMonitor();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
            }
        }

        private void DisposeAutoSelectionMonitor()
        {
            if (_autoSelectionMonitor is not null)
            {
                _autoSelectionMonitor.SelectionGesture -= OnAutoSelectionGesture;
                _autoSelectionMonitor.Dispose();
                _autoSelectionMonitor = null;
            }
        }

        /// <summary>
        /// 选词手势回调（钩子线程=UI 线程）：防抖后发起取词，不阻塞钩子。
        /// 前台属本进程时 CaptureAsync 自会跳过，故无需额外判断覆盖窗/简洁窗。
        /// </summary>
        private void OnAutoSelectionGesture()
        {
            long now = Environment.TickCount64;
            if (now - _lastAutoCaptureTick < AutoCaptureDebounceMs)
            {
                return;
            }

            _lastAutoCaptureTick = now;
            this.Dispatcher.TryEnqueue(async () => await CaptureAndShowAutoAsync());
        }

        /// <summary>
        /// 自动划词取词：取不到文本时静默（区别于热键主动取词的失败提示），避免每次划选都弹窗打扰
        /// </summary>
        private async Task CaptureAndShowAutoAsync()
        {
            string? text = await SelectionCaptureService.CaptureAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            this.Dispatcher.TryEnqueue(() => ShowSelectionResult(text));
        }

        private void ShowSelectionResult(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowLiteHint("未获取到选中文本：请先在其他应用中选中文字，再按快捷键");
                return;
            }

            if (IsSentenceInput(text) && this.AppSettings.EnableAiTranslation)
            {
                App.ShowMainWindow();
                App.FlintMainWindow?.TryTranslateText(text);
                return;
            }

            App.ShowLiteWindow(atCursor: true);
            App.FlintLiteWindow?.TrySetSearchTextBoxText(text);
        }

        /// <summary>
        /// 在光标旁的简洁窗口显示一条提示（取词失败/不可用）
        /// </summary>
        private void ShowLiteHint(string hint)
        {
            App.ShowLiteWindow(atCursor: true);
            App.FlintLiteWindow?.TryClearSearchTextBox();
            SearchHint = hint;
            if (App.FlintLiteWindow is not null)
            {
                App.FlintLiteWindow.Height = 386;
            }
        }

        #region 划词设置存取

        private async Task ReadSelectionSettings()
        {
            try
            {
                string json = await StorageFilesService.ReadFileAsync("selectionsettings");
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var settings = JsonSerializer.Deserialize(json, FlintJsonContext.Default.SelectionTranslationSettings);
                    if (settings is not null)
                    {
                        _selectionSettings = settings;
                        _selectionSettings.Hotkey ??= new SelectionTranslationSettings().Hotkey;
                        OnPropertyChanged(nameof(SelectionTranslationEnabled));
                        OnPropertyChanged(nameof(SelectionShortcut));
                        return;
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }

        private async Task SaveSelectionSettings()
        {
            try
            {
                string json = JsonSerializer.Serialize(_selectionSettings, FlintJsonContext.Default.SelectionTranslationSettings);
                await StorageFilesService.WriteFileAsync("selectionsettings", json);
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }

        #endregion
    }
}