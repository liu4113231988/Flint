using System;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Flint3.Core.Input;
using Flint3.Core.Serialization;
using Flint3.Helpers;
using Flint3.Models;

namespace Flint3.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        /// <summary>
        /// 默认快捷键 Alt+Space
        /// </summary>
        private HotkeySettings _defaultActivationShortcut => new(false, false, true, false, 0x20);

        private HotkeyService _hotkeyService = null;

        private ushort _hotkeyHandle;

        private HotkeySettings _activationShortcut;

        /// <summary>
        /// 快捷键
        /// </summary>
        public HotkeySettings ActivationShortcut
        {
            get => _activationShortcut;
            set
            {
                if (_activationShortcut?.ToString() != value?.ToString())
                {
                    HotkeySettings newValue = (value == null || !value.IsValid() || value.IsEmpty()) ? _defaultActivationShortcut : value;
                    HotkeySettings previousValue = _activationShortcut?.Clone();
                    SetProperty(ref _activationShortcut, newValue);

                    UpdateActivationShortcut(previousValue, _activationShortcut);
                }
            }
        }

        /// <summary>
        /// 进行对快捷键相关的初始化
        /// </summary>
        private async void InitViewModel4ShortcutKeys()
        {
            await ReadShortcutSettings();

            if (this.ActivationShortcut?.IsValid() == true)
            {
                RegisterShortcut(this.ActivationShortcut, OnShortcutActivated);
            }
        }

        /// <summary>
        /// 取消注册旧的快捷键，注册新的快捷键
        /// </summary>
        /// <param name="previousHotkey"></param>
        /// <param name="newHotkey"></param>
        private async void UpdateActivationShortcut(HotkeySettings previousHotkey, HotkeySettings newHotkey)
        {
            if (previousHotkey != null && !previousHotkey.IsEmpty())
            {
                if (_hotkeyHandle != 0)
                {
                    _hotkeyService?.Unregister(_hotkeyHandle);
                    _hotkeyHandle = 0;
                }
            }

            if (newHotkey?.IsValid() == true)
            {
                RegisterShortcut(newHotkey, OnShortcutActivated);
            }

            await SaveShortcutSettings();
        }

        private void RegisterShortcut(HotkeySettings hotkeySettings, Action action)
        {
            try
            {
                if (_hotkeyHandle != 0)
                {
                    _hotkeyService?.Unregister(_hotkeyHandle);
                    _hotkeyHandle = 0;
                    System.Diagnostics.Trace.WriteLine("Unregistering previous low level key handler");
                }

                _hotkeyService ??= new HotkeyService();
                _hotkeyHandle = _hotkeyService.Register(hotkeySettings, action);
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.WriteLine(e);
            }
        }

        /// <summary>
        /// 按下快捷键，唤出主窗口或简洁窗口。钩子线程回调，统一切回 UI 线程且不阻塞钩子
        /// </summary>
        private void OnShortcutActivated()
        {
            this.Dispatcher.TryEnqueue(() =>
            {
                if (this.AppSettings.WindowMode == 1)
                {
                    App.ShowLiteWindow();
                }
                else
                {
                    App.ShowMainWindow();
                }
            });
        }

        #region 快捷键设置存取

        private async Task ReadShortcutSettings()
        {
            try
            {
                string json = await StorageFilesService.ReadFileAsync("shortcutsettings");
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var shortcut = JsonSerializer.Deserialize(json, FlintJsonContext.Default.HotkeySettings);
                    this.ActivationShortcut = shortcut;
                    return;
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            this.ActivationShortcut = _defaultActivationShortcut;
        }

        private async Task SaveShortcutSettings()
        {
            try
            {
                string json = JsonSerializer.Serialize(this.ActivationShortcut, FlintJsonContext.Default.HotkeySettings);
                bool ret = await StorageFilesService.WriteFileAsync("shortcutsettings", json);
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }

        #endregion

        #region Dispose

        private static bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_hotkeyHandle != 0)
                    {
                        _hotkeyService?.Unregister(_hotkeyHandle);
                    }

                    _hotkeyService?.Dispose();
                    _searchCoordinator.Dispose();
                    _disposed = true;
                }
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion

    }
}
