using System;
using System.Drawing;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Flint3.Core.Capture;
using Flint3.Core.Input;
using Flint3.Core.Serialization;
using Flint3.Helpers;

namespace Flint3.ViewModels
{
    /// <summary>
    /// 屏幕取词：全局热键冻结屏幕，拖选区域或单击文字 OCR 取词，结果复用划词翻译的路由
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        private ScreenCaptureSettings _screenCaptureSettings = new();

        private ushort _screenCaptureHotkeyHandle;

        public string ScreenCaptureShortcutStatus
        {
            get
            {
                if (!ScreenCaptureEnabled)
                {
                    return string.Empty;
                }

                ushort handle = HotkeyService.GetHotkeyHandle(ScreenCaptureShortcut);
                if (handle == HotkeyService.GetHotkeyHandle(ActivationShortcut ?? _defaultActivationShortcut))
                {
                    return "与唤醒窗口的快捷键冲突，请修改组合键";
                }

                if (SelectionTranslationEnabled && handle == HotkeyService.GetHotkeyHandle(SelectionShortcut))
                {
                    return "与划词快捷键冲突，请修改组合键";
                }

                return _screenCaptureHotkeyHandle != 0 && _hotkeyService?.IsHookInstalled == true
                    ? string.Empty
                    : "屏幕取词快捷键未生效";
            }
        }

        /// <summary>
        /// 是否启用屏幕取词
        /// </summary>
        public bool ScreenCaptureEnabled
        {
            get => _screenCaptureSettings.Enabled;
            set
            {
                if (_screenCaptureSettings.Enabled != value)
                {
                    _screenCaptureSettings.Enabled = value;
                    OnPropertyChanged();
                    UpdateScreenCaptureRegistration();
                    _ = SaveScreenCaptureSettings();
                }
            }
        }

        /// <summary>
        /// 屏幕取词热键，默认 Ctrl+Alt+S
        /// </summary>
        public HotkeySettings ScreenCaptureShortcut
        {
            get => _screenCaptureSettings.Hotkey;
            set
            {
                HotkeySettings newValue = (value == null || !value.IsValid() || value.IsEmpty())
                    ? new ScreenCaptureSettings().Hotkey
                    : value;
                if (_screenCaptureSettings.Hotkey?.ToString() != newValue.ToString())
                {
                    _screenCaptureSettings.Hotkey = newValue;
                    OnPropertyChanged();
                    UpdateScreenCaptureRegistration();
                    _ = SaveScreenCaptureSettings();
                }
            }
        }

        private async void InitViewModel4ScreenCapture()
        {
            await ReadScreenCaptureSettings();
            UpdateScreenCaptureRegistration();
        }

        /// <summary>
        /// 按当前开关与热键重新注册；与唤醒/划词热键冲突时让位（优先级：唤醒 > 划词 > 屏幕取词）
        /// </summary>
        private void UpdateScreenCaptureRegistration()
        {
            try
            {
                ushort activationHandle = HotkeyService.GetHotkeyHandle(ActivationShortcut ?? _defaultActivationShortcut);
                ushort selectionHandle = (SelectionTranslationEnabled && SelectionShortcut?.IsValid() == true)
                    ? HotkeyService.GetHotkeyHandle(SelectionShortcut)
                    : (ushort)0;

                if (_screenCaptureHotkeyHandle != 0
                    && _screenCaptureHotkeyHandle != activationHandle
                    && _screenCaptureHotkeyHandle != selectionHandle)
                {
                    _hotkeyService?.Unregister(_screenCaptureHotkeyHandle);
                }

                _screenCaptureHotkeyHandle = 0;

                if (!ScreenCaptureEnabled || ScreenCaptureShortcut?.IsValid() != true)
                {
                    return;
                }

                ushort handle = HotkeyService.GetHotkeyHandle(ScreenCaptureShortcut);
                if (handle == activationHandle || (selectionHandle != 0 && handle == selectionHandle))
                {
                    return;
                }

                _hotkeyService ??= new HotkeyService();
                _screenCaptureHotkeyHandle = _hotkeyService.Register(ScreenCaptureShortcut, OnScreenCaptureHotkey);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
            }
            finally
            {
                OnPropertyChanged(nameof(ScreenCaptureShortcutStatus));
            }
        }

        /// <summary>
        /// 钩子线程回调，切回 UI 线程发起取词，不阻塞钩子
        /// </summary>
        private void OnScreenCaptureHotkey()
        {
            this.Dispatcher.TryEnqueue(StartScreenCapture);
        }

        private void StartScreenCapture()
        {
            DismissSelectionIcon();
            if (ScreenCaptureWindow.IsOpen)
            {
                ScreenCaptureWindow.CancelCurrent();
                return;
            }

            try
            {
                var engine = OcrService.CreateEngine();
                if (engine is null)
                {
                    ShowLiteHint("屏幕取词不可用：请在 Windows 设置的语言选项中安装 OCR 语言包。");
                    return;
                }

                App.FlintLiteWindow?.Dismiss();
                if (!ScreenCaptureWindow.TryShow(engine, OnScreenCaptureCompleted))
                {
                    ShowLiteHint("无法截取当前屏幕。请在要取词的显示器上重试。");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
                ScreenCaptureWindow.CancelCurrent();
                ShowLiteHint("无法启动屏幕取词，请重试。");
            }
        }

        private void OnScreenCaptureCompleted(ScreenRecognitionResult result, string? text, Point? anchor)
        {
            switch (result)
            {
                case ScreenRecognitionResult.Success:
                    ShowSelectionResult(text, anchor: anchor);
                    break;
                case ScreenRecognitionResult.NoText:
                    ShowLiteHint("未识别到文字。请框选包含文字的区域，或单击文字所在位置。", anchor);
                    break;
                case ScreenRecognitionResult.Failed:
                    ShowLiteHint("屏幕文字识别失败。请重试或缩小选区。", anchor);
                    break;
            }
        }

        #region 屏幕取词设置存取

        private async Task ReadScreenCaptureSettings()
        {
            try
            {
                string json = await StorageFilesService.ReadFileAsync("screencapturesettings");
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var settings = JsonSerializer.Deserialize(json, FlintJsonContext.Default.ScreenCaptureSettings);
                    if (settings is not null)
                    {
                        _screenCaptureSettings = settings;
                        _screenCaptureSettings.Hotkey ??= new ScreenCaptureSettings().Hotkey;
                        OnPropertyChanged(nameof(ScreenCaptureEnabled));
                        OnPropertyChanged(nameof(ScreenCaptureShortcut));
                        return;
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }

        private async Task SaveScreenCaptureSettings()
        {
            try
            {
                string json = JsonSerializer.Serialize(_screenCaptureSettings, FlintJsonContext.Default.ScreenCaptureSettings);
                await StorageFilesService.WriteFileAsync("screencapturesettings", json);
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }

        #endregion
    }
}
