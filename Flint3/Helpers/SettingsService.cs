using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Flint3.Core.Settings;

namespace Flint3.Helpers
{
    public class SettingsService : ObservableObject
    {
        private const string FLINT_MAINWINDOW_WIDTH = "FlintMainWindowWidth";
        private const string FLINT_MAINWINDOW_HEIGHT = "FlintMainWindowHeight";

        private const string SETTING_NAME_APPEARANCEINDEX = "AppearanceIndex";
        private const string SETTING_NAME_BACKDROPINDEX = "BackdropIndex";
        private const string SETTING_NAME_ENABLEENGDEF = "EnableEngDefinition";
        private const string SETTING_NAME_ENABLEGLOSSARY = "EnableGlossary";
        private const string SETTING_NAME_ENABLETTS = "EnableTTS";
        private const string SETTING_NAME_TTSVOICE = "TTSVoice";
        private const string SETTING_NAME_TTSVOLUME = "TTSVolume";
        private const string SETTING_NAME_WINDOWMODE = "WindowMode";
        private const string SETTING_NAME_AUTOCLEARLASTINPUT = "AutoClearLastInput";
        private const string SETTING_NAME_CLOSEBUTTONMODE = "CloseButtonMode";
        private const string SETTING_NAME_SEARCHBOXSTYLE = "SearchBoxStyle";
        private const string SETTING_NAME_ENABLEAITRANSLATION = "EnableAiTranslation";
        private const string SETTING_NAME_AIBASEURL = "AiBaseUrl";
        private const string SETTING_NAME_AIMODEL = "AiModel";
        private const string SETTING_NAME_AIAPIKEYPROTECTED = "AiApiKeyProtected";
        private const string SETTING_NAME_AIEXTRAHEADERS = "AiExtraHeaders";

        private readonly ISettingsStore _store;

        public SettingsService(ISettingsStore? store = null)
        {
            _store = store ?? new ApplicationDataSettingsStore();
        }

        /// <summary>
        /// 读取设置项（打包运行时来自 ApplicationData，免打包运行时来自本地文件）
        /// </summary>
        public object? GetValue(string key) => _store[key];

        /// <summary>
        /// 写入设置项
        /// </summary>
        public void SetValue(string key, object? value) => _store[key] = value;

        private double _mainWindowWidth = -1;

        private double _mainWindowHeight = -1;

        private int _appearanceIndex = -1;

        private int _backdropIndex = -1;

        private bool? _enableEngDefinition = null;

        private bool? _enableGlossary = null;

        private bool? _enableTTS = null;

        private string? _ttsVoice = null;

        private int _ttsVolume = -1;

        private int _windowMode = -1;

        private bool? _autoClearLastInput = null;

        private int _closeButtonMode = -1;

        private int _searchBoxStyle = -1;

        private bool? _enableAiTranslation = null;

        private string? _aiBaseUrl = null;

        private string? _aiModel = null;

        private string? _aiApiKeyProtected = null;

        private string? _aiExtraHeaders = null;

        public event EventHandler<int>? AppearanceSettingChanged = null;

        public event EventHandler<int>? BackdropSettingChanged = null;

        /// <summary>
        /// 主窗口的宽度
        /// </summary>
        public double MainWindowWidth
        {
            get
            {
                try
                {
                    if (_mainWindowWidth < 0)
                    {
                        if (double.TryParse(_store[FLINT_MAINWINDOW_WIDTH]?.ToString(), out double width))
                        {
                            _mainWindowWidth = width;
                        }
                        else
                        {
                            _mainWindowWidth = 580;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                if (_mainWindowWidth <= 0) _mainWindowWidth = 580;
                return _mainWindowWidth <= 0 ? 580 : _mainWindowWidth;
            }
            set
            {
                SetProperty(ref _mainWindowWidth, value);
                _store[FLINT_MAINWINDOW_WIDTH] = _mainWindowWidth;
            }
        }

        /// <summary>
        /// 主窗口的高度
        /// </summary>
        public double MainWindowHeight
        {
            get
            {
                try
                {
                    if (_mainWindowHeight < 0)
                    {
                        if (double.TryParse(_store[FLINT_MAINWINDOW_HEIGHT]?.ToString(), out double height))
                        {
                            _mainWindowHeight = height;
                        }
                        else
                        {
                            _mainWindowHeight = 386;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                if (_mainWindowHeight <= 0) _mainWindowHeight = 580;
                return _mainWindowHeight <= 0 ? 580 : _mainWindowHeight;
            }
            set
            {
                SetProperty(ref _mainWindowHeight, value);
                _store[FLINT_MAINWINDOW_HEIGHT] = _mainWindowHeight;
            }
        }

        /// <summary>
        /// 设置的应用程序的主题 0-System 1-Dark 2-Light
        /// </summary>
        public int AppearanceIndex
        {
            get
            {
                try
                {
                    if (_appearanceIndex < 0)
                    {
                        if (_store[SETTING_NAME_APPEARANCEINDEX] == null)
                        {
                            _appearanceIndex = 0;
                        }
                        else if (_store[SETTING_NAME_APPEARANCEINDEX]?.ToString() == "0")
                        {
                            _appearanceIndex = 0;
                        }
                        else if (_store[SETTING_NAME_APPEARANCEINDEX]?.ToString() == "1")
                        {
                            _appearanceIndex = 1;
                        }
                        else if (_store[SETTING_NAME_APPEARANCEINDEX]?.ToString() == "2")
                        {
                            _appearanceIndex = 2;
                        }
                        else
                        {
                            _appearanceIndex = 0;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                if (_appearanceIndex < 0) _appearanceIndex = 0;
                return _appearanceIndex < 0 ? 0 : _appearanceIndex;
            }
            set
            {
                SetProperty(ref _appearanceIndex, value);
                _store[SETTING_NAME_APPEARANCEINDEX] = _appearanceIndex;
                AppearanceSettingChanged?.Invoke(this, _appearanceIndex);
            }
        }

        /// <summary>
        /// 设置的应用程序的背景材质 0-Mica 1-Acrylic
        /// </summary>
        public int BackdropIndex
        {
            get
            {
                try
                {
                    if (_backdropIndex < 0)
                    {
                        if (_store[SETTING_NAME_BACKDROPINDEX] == null)
                        {
                            _backdropIndex = 0;
                        }
                        else if (_store[SETTING_NAME_BACKDROPINDEX]?.ToString() == "0")
                        {
                            _backdropIndex = 0;
                        }
                        else if (_store[SETTING_NAME_BACKDROPINDEX]?.ToString() == "1")
                        {
                            _backdropIndex = 1;
                        }
                        else
                        {
                            _backdropIndex = 0;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                if (_backdropIndex < 0) _backdropIndex = 0;
                return _backdropIndex < 0 ? 0 : _backdropIndex;
            }
            set
            {
                SetProperty(ref _backdropIndex, value);
                _store[SETTING_NAME_BACKDROPINDEX] = _backdropIndex;
                BackdropSettingChanged?.Invoke(this, _backdropIndex);
            }
        }

        /// <summary>
        /// 是否显示英英释义
        /// </summary>
        public bool EnableEngDefinition
        {
            get
            {
                try
                {
                    if (_enableEngDefinition is null)
                    {
                        if (_store[SETTING_NAME_ENABLEENGDEF] == null)
                        {
                            _enableEngDefinition = true;
                        }
                        else if (_store[SETTING_NAME_ENABLEENGDEF]?.ToString() == "False")
                        {
                            _enableEngDefinition = false;
                        }
                        else
                        {
                            _enableEngDefinition = true;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                if (_enableEngDefinition is null) _enableEngDefinition = true;
                return _enableEngDefinition != false;
            }
            set
            {
                SetProperty(ref _enableEngDefinition, value);
                _store[SETTING_NAME_ENABLEENGDEF] = _enableEngDefinition;
            }
        }

        /// <summary>
        /// 是否启用生词本
        /// </summary>
        public bool EnableGlossary
        {
            get
            {
                try
                {
                    if (_enableGlossary is null)
                    {
                        if (_store[SETTING_NAME_ENABLEGLOSSARY] == null)
                        {
                            _enableGlossary = true;
                        }
                        else if (_store[SETTING_NAME_ENABLEGLOSSARY]?.ToString() == "False")
                        {
                            _enableGlossary = false;
                        }
                        else
                        {
                            _enableGlossary = true;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                if (_enableGlossary is null) _enableGlossary = true;
                return _enableGlossary != false;
            }
            set
            {
                SetProperty(ref _enableGlossary, value);
                _store[SETTING_NAME_ENABLEGLOSSARY] = _enableGlossary;
            }
        }

        /// <summary>
        /// 是否启用朗读单词
        /// </summary>
        public bool EnableTTS
        {
            get
            {
                try
                {
                    if (_enableTTS is null)
                    {
                        if (_store[SETTING_NAME_ENABLETTS]?.ToString() == "True")
                        {
                            _enableTTS = true;
                        }
                        else
                        {
                            _enableTTS = false;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                if (_enableTTS is null) _enableTTS = false;
                return _enableTTS == true;
            }
            set
            {
                SetProperty(ref _enableTTS, value);
                _store[SETTING_NAME_ENABLETTS] = _enableTTS;
            }
        }

        /// <summary>
        /// 朗读单词使用的声音名称
        /// </summary>
        public string TTSVoice
        {
            get
            {
                try
                {
                    if (_ttsVoice is null)
                    {
                        if (_store[SETTING_NAME_TTSVOICE] is null)
                        {
                            _ttsVoice = "";
                        }
                        else
                        {
                            _ttsVoice = _store[SETTING_NAME_TTSVOICE].ToString();
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                if (_ttsVoice is null) _ttsVoice = "";
                return _ttsVoice ?? "";
            }
            set
            {
                SetProperty(ref _ttsVoice, value);
                _store[SETTING_NAME_TTSVOICE] = _ttsVoice;
            }
        }

        /// <summary>
        /// 朗读单词的音量 1~10
        /// </summary>
        public int TTSVolume
        {
            get
            {
                try
                {
                    if (_ttsVolume < 0)
                    {
                        if (int.TryParse(_store[SETTING_NAME_TTSVOLUME]?.ToString(), out int volume))
                        {
                            _ttsVolume = volume;
                        }
                        else
                        {
                            _ttsVolume = 5;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                if (_ttsVolume <= 0) _ttsVolume = 5;
                return _ttsVolume <= 0 ? 5 : _ttsVolume;
            }
            set
            {
                SetProperty(ref _ttsVolume, value);
                _store[SETTING_NAME_TTSVOLUME] = _ttsVolume;
            }
        }

        /// <summary>
        /// 快捷键唤起的窗口模式 0-完整 1-简洁
        /// </summary>
        public int WindowMode
        {
            get
            {
                try
                {
                    if (_windowMode < 0)
                    {
                        if (_store[SETTING_NAME_WINDOWMODE] == null)
                        {
                            _windowMode = 0;
                        }
                        else if (_store[SETTING_NAME_WINDOWMODE]?.ToString() == "0")
                        {
                            _windowMode = 0;
                        }
                        else if (_store[SETTING_NAME_WINDOWMODE]?.ToString() == "1")
                        {
                            _windowMode = 1;
                        }
                        else
                        {
                            _windowMode = 0;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                if (_windowMode < 0) _windowMode = 0;
                return _windowMode;
            }
            set
            {
                SetProperty(ref _windowMode, value);
                _store[SETTING_NAME_WINDOWMODE] = _windowMode;
            }
        }

        /// <summary>
        /// 是否自动清除上次输入
        /// </summary>
        public bool AutoClearLastInput
        {
            get
            {
                try
                {
                    if (_autoClearLastInput is null)
                    {
                        if (_store[SETTING_NAME_AUTOCLEARLASTINPUT] == null)
                        {
                            _autoClearLastInput = false;
                        }
                        else if (_store[SETTING_NAME_AUTOCLEARLASTINPUT]?.ToString() == "True")
                        {
                            _autoClearLastInput = true;
                        }
                        else
                        {
                            _autoClearLastInput = false;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                if (_autoClearLastInput is null) _autoClearLastInput = false;
                return _autoClearLastInput == true;
            }
            set
            {
                SetProperty(ref _autoClearLastInput, value);
                _store[SETTING_NAME_AUTOCLEARLASTINPUT] = _autoClearLastInput;
            }
        }

        /// <summary>
        /// 设置关闭按钮的作用 0-隐藏 1-退出
        /// </summary>
        public int CloseButtonMode
        {
            get
            {
                try
                {
                    if (_closeButtonMode < 0)
                    {
                        if (_store[SETTING_NAME_CLOSEBUTTONMODE] == null)
                        {
                            _closeButtonMode = 0;
                        }
                        else if (_store[SETTING_NAME_CLOSEBUTTONMODE]?.ToString() == "0")
                        {
                            _closeButtonMode = 0;
                        }
                        else if (_store[SETTING_NAME_CLOSEBUTTONMODE]?.ToString() == "1")
                        {
                            _closeButtonMode = 1;
                        }
                        else
                        {
                            _closeButtonMode = 0;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                if (_closeButtonMode < 0) _closeButtonMode = 0;
                return _closeButtonMode < 0 ? 0 : _closeButtonMode;
            }
            set
            {
                SetProperty(ref _closeButtonMode, value);
                _store[SETTING_NAME_CLOSEBUTTONMODE] = _closeButtonMode;
            }
        }

        /// <summary>
        /// 搜索框样式
        /// </summary>
        public int SearchBoxStyle
        {
            get
            {
                try
                {
                    if (_searchBoxStyle < 0)
                    {
                        if (_store[SETTING_NAME_SEARCHBOXSTYLE] == null)
                        {
                            _searchBoxStyle = 2;
                        }
                        else if (_store[SETTING_NAME_SEARCHBOXSTYLE]?.ToString() == "0")
                        {
                            _searchBoxStyle = 0;
                        }
                        else if (_store[SETTING_NAME_SEARCHBOXSTYLE]?.ToString() == "1")
                        {
                            _searchBoxStyle = 1;
                        }
                        else if (_store[SETTING_NAME_SEARCHBOXSTYLE]?.ToString() == "2")
                        {
                            _searchBoxStyle = 2;
                        }
                        else
                        {
                            _searchBoxStyle = 2;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                if (_searchBoxStyle < 0) _searchBoxStyle = 2;
                return _searchBoxStyle < 0 ? 2 : _searchBoxStyle;
            }
            set
            {
                SetProperty(ref _searchBoxStyle, value);
                _store[SETTING_NAME_SEARCHBOXSTYLE] = _searchBoxStyle;
            }
        }

        /// <summary>
        /// 是否启用 AI 翻译（默认关闭；涉及把查询文本发送到用户配置的第三方接口）
        /// </summary>
        public bool EnableAiTranslation
        {
            get
            {
                try
                {
                    if (_enableAiTranslation is null)
                    {
                        _enableAiTranslation = _store[SETTING_NAME_ENABLEAITRANSLATION]?.ToString() == "True";
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                return _enableAiTranslation == true;
            }
            set
            {
                SetProperty(ref _enableAiTranslation, value);
                _store[SETTING_NAME_ENABLEAITRANSLATION] = _enableAiTranslation;
            }
        }

        /// <summary>
        /// AI 接口地址（OpenAI 兼容 baseUrl，通常已含 /v1）
        /// </summary>
        public string AiBaseUrl
        {
            get
            {
                try
                {
                    _aiBaseUrl ??= _store[SETTING_NAME_AIBASEURL]?.ToString() ?? "";
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                return _aiBaseUrl ?? "";
            }
            set
            {
                SetProperty(ref _aiBaseUrl, value);
                _store[SETTING_NAME_AIBASEURL] = _aiBaseUrl;
            }
        }

        /// <summary>
        /// AI 模型名
        /// </summary>
        public string AiModel
        {
            get
            {
                try
                {
                    _aiModel ??= _store[SETTING_NAME_AIMODEL]?.ToString() ?? "";
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                return _aiModel ?? "";
            }
            set
            {
                SetProperty(ref _aiModel, value);
                _store[SETTING_NAME_AIMODEL] = _aiModel;
            }
        }

        /// <summary>
        /// DPAPI 加密后的 API Key（Base64），设置页之外不还原明文
        /// </summary>
        public string AiApiKeyProtected
        {
            get
            {
                try
                {
                    _aiApiKeyProtected ??= _store[SETTING_NAME_AIAPIKEYPROTECTED]?.ToString() ?? "";
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                return _aiApiKeyProtected ?? "";
            }
            set
            {
                SetProperty(ref _aiApiKeyProtected, value);
                _store[SETTING_NAME_AIAPIKEYPROTECTED] = _aiApiKeyProtected;
            }
        }

        /// <summary>
        /// 自定义 HTTP Header，每行一条 name: value（不写日志）
        /// </summary>
        public string AiExtraHeaders
        {
            get
            {
                try
                {
                    _aiExtraHeaders ??= _store[SETTING_NAME_AIEXTRAHEADERS]?.ToString() ?? "";
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                return _aiExtraHeaders ?? "";
            }
            set
            {
                SetProperty(ref _aiExtraHeaders, value);
                _store[SETTING_NAME_AIEXTRAHEADERS] = _aiExtraHeaders;
            }
        }
    }
}
