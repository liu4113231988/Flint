using System;
using System.Drawing;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Flint3.Core.Input;
using Flint3.Core.Serialization;
using Flint3.Helpers;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Flint3.ViewModels
{
    /// <summary>
    /// 划词取词：快捷键直接查词，鼠标划选后显示图标。
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        private SelectionTranslationSettings _selectionSettings = new();

        private ushort _selectionHotkeyHandle;

        private AutoSelectionMonitor? _autoSelectionMonitor;

        private SelectionIconWindow? _selectionIcon;
        private int _selectionVersion;

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
        /// 选中文字后显示划词图标（默认关闭）
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

        public string SelectionShortcutStatus
        {
            get
            {
                if (!SelectionTranslationEnabled)
                {
                    return string.Empty;
                }

                if (HotkeyService.GetHotkeyHandle(SelectionShortcut) ==
                    HotkeyService.GetHotkeyHandle(ActivationShortcut ?? _defaultActivationShortcut))
                {
                    return "与唤醒窗口的快捷键冲突，请修改组合键";
                }

                return _selectionHotkeyHandle != 0 && _hotkeyService?.IsHookInstalled == true
                    ? string.Empty
                    : "划词快捷键未生效";
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
                OnPropertyChanged(nameof(SelectionShortcutStatus));
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
            DismissSelectionIcon();
            int version = _selectionVersion;
            HWND owner = PInvoke.GetForegroundWindow();
            Point? anchor = PInvoke.GetCursorPos(out Point pointer) ? pointer : null;
            string? text = await SelectionCaptureService.CaptureAsync();
            if (SelectionTranslationEnabled && IsCurrentSelection(version, owner))
            {
                ShowSelectionResult(text, anchor: anchor);
            }
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
                        _autoSelectionMonitor.PointerPressed += OnSelectionPointerPressed;
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
            DismissSelectionIcon();
            if (_autoSelectionMonitor is not null)
            {
                _autoSelectionMonitor.SelectionGesture -= OnAutoSelectionGesture;
                _autoSelectionMonitor.PointerPressed -= OnSelectionPointerPressed;
                _autoSelectionMonitor.Dispose();
                _autoSelectionMonitor = null;
            }
        }

        private void OnSelectionPointerPressed(Point pointer)
        {
            if (_selectionIcon?.Contains(pointer) != true)
            {
                int version = ++_selectionVersion;
                if (_selectionIcon is null)
                {
                    return;
                }

                Dispatcher.TryEnqueue(() =>
                {
                    if (version == _selectionVersion)
                    {
                        _selectionIcon?.Dismiss();
                    }
                });
            }
        }

        internal void DismissSelectionIcon()
        {
            _selectionVersion++;
            _selectionIcon?.Dismiss();
        }

        private void OnAutoSelectionGesture(Point pointer)
        {
            int version = _selectionVersion;
            HWND owner = PInvoke.GetForegroundWindow();
            Dispatcher.TryEnqueue(async () => await CaptureForIconAsync(pointer, owner, version));
        }

        private async Task CaptureForIconAsync(Point pointer, HWND owner, int version)
        {
            // 鼠标钩子先于目标窗口收到抬键，给目标窗口提交选区的时间。
            await Task.Delay(50);
            if (!SelectionAutoEnabled || !IsCurrentSelection(version, owner))
            {
                return;
            }

            string? text = await SelectionCaptureService.CaptureAsync();
            if (!string.IsNullOrWhiteSpace(text) && IsCurrentSelection(version, owner))
            {
                _selectionIcon ??= new SelectionIconWindow();
                _selectionIcon.ShowAt(pointer, owner, () => ShowSelectionResult(text, translateOnMiss: true, anchor: pointer));
            }
        }

        private bool IsCurrentSelection(int version, HWND owner)
            => version == _selectionVersion && owner == PInvoke.GetForegroundWindow();

        private void ShowSelectionResult(string? text, bool translateOnMiss = false, Point? anchor = null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowLiteHint("未读取到选区。请先选中文字，或使用屏幕取词、手动复制并粘贴。", anchor);
                return;
            }

            App.ShowLiteWindow(atCursor: true, anchor: anchor);
            App.FlintLiteWindow?.ShowCapturedText(text, translateOnMiss);
        }

        /// <summary>
        /// 在光标旁的简洁窗口显示一条提示（取词失败/不可用）
        /// </summary>
        private void ShowLiteHint(string hint, Point? anchor = null)
        {
            App.ShowLiteWindow(atCursor: true, anchor: anchor);
            App.FlintLiteWindow?.TryClearSearchTextBox();
            LiteSearchHint = hint;
            App.ResizeLiteWindow(expanded: true);
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
                        OnPropertyChanged(nameof(SelectionAutoEnabled));
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
