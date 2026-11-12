using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Flint3.Core.Query;
using Flint3.Core.Translation;
using Flint3.Data.Models;
using Flint3.Helpers;

namespace Flint3.ViewModels
{
    /// <summary>
    /// AI 翻译：独立于词条查询链路，手动触发，全局单在途。
    /// 取消/序号校验复用 SearchCoordinator 的模式，但使用独立实例，避免与词条查询互斥取消。
    /// </summary>
    public partial class MainViewModel
    {
        private readonly LlmTranslationService _translationService = new();

        private readonly SearchCoordinator _translationCoordinator = new();

        /// <summary>最近一次搜索的原始输入（未经正则清洗，整句翻译要用它）</summary>
        private string _lastRawInput = "";

        private AiTranslationItem? _sentenceTranslation;

        /// <summary>
        /// 当前整句翻译状态；输入变化即置空
        /// </summary>
        public AiTranslationItem? SentenceTranslation
        {
            get => _sentenceTranslation;
            private set
            {
                if (SetProperty(ref _sentenceTranslation, value))
                {
                    OnPropertyChanged(nameof(SentenceResultVisible));
                }
            }
        }

        /// <summary>整句翻译结果卡是否可见（x:Bind 需要的稳定布尔）</summary>
        public bool SentenceResultVisible => SentenceTranslation is not null;

        private bool _canTranslateSentence;

        /// <summary>
        /// 是否出现"翻译整句"入口：输入命中整句判定且 AI 翻译已开启
        /// </summary>
        public bool CanTranslateSentence
        {
            get => _canTranslateSentence;
            private set => SetProperty(ref _canTranslateSentence, value);
        }

        private bool _isAiTranslationMode;

        /// <summary>
        /// 底部独立 AI 翻译面板是否打开（替代结果区显示）
        /// </summary>
        public bool IsAiTranslationMode
        {
            get => _isAiTranslationMode;
            private set
            {
                if (SetProperty(ref _isAiTranslationMode, value))
                {
                    OnPropertyChanged(nameof(IsResultsAreaVisible));
                }
            }
        }

        /// <summary>x:Bind 需要的稳定布尔：AI 面板模式下隐藏查词结果区</summary>
        public bool IsResultsAreaVisible => !IsAiTranslationMode;

        private AiTranslationItem? _panelTranslation;

        /// <summary>独立面板的翻译状态；与整句卡各自独立</summary>
        public AiTranslationItem? PanelTranslation
        {
            get => _panelTranslation;
            private set
            {
                if (SetProperty(ref _panelTranslation, value))
                {
                    OnPropertyChanged(nameof(PanelResultVisible));
                }
            }
        }

        /// <summary>独立面板结果卡是否可见</summary>
        public bool PanelResultVisible => PanelTranslation is not null;

        public void EnterAiTranslationMode()
        {
            IsAiTranslationMode = true;
        }

        public void ExitAiTranslationMode()
        {
            IsAiTranslationMode = false;
        }

        private int _panelSourceIndex = 1;

        /// <summary>面板源语言索引：0 自动检测 / 1 中文 / 2 英文 / 3 德文 / 4 日文，默认中文</summary>
        public int PanelSourceIndex
        {
            get => _panelSourceIndex;
            set => SetProperty(ref _panelSourceIndex, value);
        }

        private int _panelTargetIndex = 1;

        /// <summary>面板目标语言索引：0 中文 / 1 英文 / 2 德文 / 3 日文，默认英文（中译英）</summary>
        public int PanelTargetIndex
        {
            get => _panelTargetIndex;
            set => SetProperty(ref _panelTargetIndex, value);
        }

        /// <summary>
        /// 快捷反转语言方向；源为自动检测时，目标默认镜像为英（英则中）
        /// </summary>
        public void SwapPanelLanguages()
        {
            int source = PanelSourceIndex, target = PanelTargetIndex;
            if (source == 0)
            {
                PanelSourceIndex = target + 1;
                PanelTargetIndex = target == 1 ? 0 : 1;
            }
            else
            {
                PanelSourceIndex = target + 1;
                PanelTargetIndex = source - 1;
            }
        }

        /// <summary>
        /// 独立面板翻译：按源/目标语言对，整段文本
        /// </summary>
        public async void TranslatePanel(string rawText)
        {
            string text = rawText?.Trim() ?? "";
            if (text.Length == 0 || IsPatternInput(text))
            {
                return;
            }

            if (BuildAiOptions(out string? configError) is not LlmTranslationOptions options)
            {
                AiTranslationItem failState = new();
                PanelTranslation = failState;
                ShowTranslationFailure(failState, LlmFailureKind.NotConfigured, configError ?? "未配置", retryable: false);
                return;
            }

            TranslationLanguage source = (TranslationLanguage)Math.Clamp(PanelSourceIndex, 0, 4);
            TranslationLanguage target = (TranslationLanguage)Math.Clamp(PanelTargetIndex + 1, 1, 4);

            SearchRequest request = _translationCoordinator.BeginRequest();
            AiTranslationItem panelState = new();
            PanelTranslation = panelState;
            ShowTranslationRunning(panelState);

            LlmTranslationOutcome outcome;
            try
            {
                outcome = await _translationService.TranslateAsync(options, text, source, target, request.Token);
            }
            catch (Exception ex)
            {
                // client 已归一化常见错误；此处兜底，避免状态卡在"加载中"
                System.Diagnostics.Trace.WriteLine($"AI translate failed: {ex.GetType().Name}");
                this.Dispatcher.TryEnqueue(() => ShowTranslationFailure(panelState, LlmFailureKind.Network, "请求失败，请检查接口配置后重试", retryable: true));
                return;
            }

            if (!_translationCoordinator.IsCurrent(request.Sequence))
            {
                this.Dispatcher.TryEnqueue(() =>
                {
                    panelState.Phase = AiTranslationPhase.Idle;
                    panelState.IsVisible = false;
                });
                return;
            }

            this.Dispatcher.TryEnqueue(() => ApplyTranslationOutcome(panelState, outcome, options.Model));
        }

        /// <summary>
        /// 每次搜索输入变化时同步整句翻译入口；新输入作废上一次整句翻译
        /// </summary>
        private void UpdateSentenceState(string rawInput)
        {
            _lastRawInput = rawInput?.Trim() ?? "";

            // 新输入作废旧结果
            SentenceTranslation = null;
            CanTranslateSentence = IsSentenceInput(_lastRawInput) && this.AppSettings.EnableAiTranslation;
        }

        /// <summary>
        /// 整句判定：含空白，或 CJK 字符 ≥ 8；通配符输入不视为整句
        /// </summary>
        private static bool IsSentenceInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || IsPatternInput(input))
            {
                return false;
            }

            if (input.Contains(' ') || input.Contains('\t'))
            {
                return true;
            }

            int cjk = 0;
            foreach (char c in input)
            {
                if (c >= '\u4E00' && c <= '\u9FFF' && ++cjk >= 8)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 从设置构造翻译配置；密钥在此处（App 层）解密
        /// </summary>
        private LlmTranslationOptions? BuildAiOptions(out string? configError)
        {
            configError = null;
            var settings = this.AppSettings;
            if (!settings.EnableAiTranslation)
            {
                configError = "AI 翻译未开启，请在设置中启用";
                return null;
            }

            if (string.IsNullOrWhiteSpace(settings.AiBaseUrl) || string.IsNullOrWhiteSpace(settings.AiModel))
            {
                configError = "请先在设置中填写接口地址与模型";
                return null;
            }

            string apiKey = "";
            string protectedKey = settings.AiApiKeyProtected;
            if (protectedKey.Length > 0)
            {
                apiKey = LlmSecretProtector.Unprotect(protectedKey) ?? "";
                if (apiKey.Length == 0)
                {
                    configError = "API Key 解密失败（换机或换用户后需重新输入），请在设置中重新填写";
                    return null;
                }
            }

            return new LlmTranslationOptions
            {
                BaseUrl = settings.AiBaseUrl.Trim(),
                ApiKey = apiKey,
                Model = settings.AiModel.Trim(),
                ExtraHeaders = ParseExtraHeaders(settings.AiExtraHeaders),
            };
        }

        /// <summary>
        /// 解析自定义 Header，每行一条 name: value，最多 3 条；不写日志
        /// </summary>
        private static Dictionary<string, string> ParseExtraHeaders(string raw)
        {
            Dictionary<string, string> headers = [];
            if (string.IsNullOrWhiteSpace(raw))
            {
                return headers;
            }

            foreach (string line in raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int sep = line.IndexOf(':');
                if (sep > 0 && sep < line.Length - 1)
                {
                    headers[line[..sep].Trim()] = line[(sep + 1)..].Trim();
                }

                if (headers.Count >= 3)
                {
                    break;
                }
            }

            return headers;
        }

        /// <summary>
        /// 翻译一个词条：文本为词条本身，音标与本地释义作为参考上下文
        /// </summary>
        public async void TranslateWord(StarDictWordItem item)
        {
            if (item?.AiTranslation is not AiTranslationItem state || string.IsNullOrWhiteSpace(item.Word))
            {
                return;
            }

            if (BuildAiOptions(out string? configError) is not LlmTranslationOptions options)
            {
                ShowTranslationFailure(state, LlmFailureKind.NotConfigured, configError ?? "未配置", retryable: false);
                return;
            }

            SearchRequest request = _translationCoordinator.BeginRequest();
            ShowTranslationRunning(state);

            string? referenceContext = null;
            if (TranslationDirectionDetector.Detect(item.Word) == TranslationDirection.EnToZh)
            {
                string reference = "";
                if (!string.IsNullOrWhiteSpace(item.Phonetic))
                {
                    reference += $"  /{item.Phonetic.Trim('/')}/";
                }

                if (!string.IsNullOrWhiteSpace(item.Translation))
                {
                    reference += $"\n本地释义：{TruncateText(item.Translation, 300)}";
                }

                referenceContext = reference.Length > 0 ? reference : null;
            }

            LlmTranslationOutcome outcome;
            try
            {
                outcome = await _translationService.TranslateAsync(options, item.Word, TranslationDirection.Auto, request.Token, referenceContext);
            }
            catch (Exception ex)
            {
                // client 已归一化常见错误；此处兜底，避免状态卡在"加载中"
                System.Diagnostics.Trace.WriteLine($"AI translate failed: {ex.GetType().Name}");
                this.Dispatcher.TryEnqueue(() => ShowTranslationFailure(state, LlmFailureKind.Network, "请求失败，请检查接口配置后重试", retryable: true));
                return;
            }

            if (!_translationCoordinator.IsCurrent(request.Sequence))
            {
                // 已被新请求顶掉，静默收起
                this.Dispatcher.TryEnqueue(() =>
                {
                    state.Phase = AiTranslationPhase.Idle;
                    state.IsVisible = false;
                });
                return;
            }

            this.Dispatcher.TryEnqueue(() => ApplyTranslationOutcome(state, outcome, options.Model));
        }

        /// <summary>
        /// 翻译搜索框中的整句（由主窗代码后置传入原始文本）
        /// </summary>
        public async void TranslateSentence(string rawText)
        {
            string text = rawText?.Trim() ?? "";
            if (text.Length == 0 || IsPatternInput(text))
            {
                return;
            }

            if (BuildAiOptions(out string? configError) is not LlmTranslationOptions options)
            {
                AiTranslationItem state = new();
                SentenceTranslation = state;
                ShowTranslationFailure(state, LlmFailureKind.NotConfigured, configError ?? "未配置", retryable: false);
                CanTranslateSentence = false;
                return;
            }

            SearchRequest request = _translationCoordinator.BeginRequest();
            AiTranslationItem sentenceState = new();
            SentenceTranslation = sentenceState;
            CanTranslateSentence = false;
            ShowTranslationRunning(sentenceState);

            LlmTranslationOutcome outcome;
            try
            {
                outcome = await _translationService.TranslateAsync(options, text, TranslationDirection.Auto, request.Token);
            }
            catch (Exception ex)
            {
                // client 已归一化常见错误；此处兜底，避免状态卡在"加载中"
                System.Diagnostics.Trace.WriteLine($"AI translate failed: {ex.GetType().Name}");
                this.Dispatcher.TryEnqueue(() => ShowTranslationFailure(sentenceState, LlmFailureKind.Network, "请求失败，请检查接口配置后重试", retryable: true));
                return;
            }

            if (!_translationCoordinator.IsCurrent(request.Sequence))
            {
                this.Dispatcher.TryEnqueue(() =>
                {
                    sentenceState.Phase = AiTranslationPhase.Idle;
                    sentenceState.IsVisible = false;
                });
                return;
            }

            this.Dispatcher.TryEnqueue(() =>
            {
                ApplyTranslationOutcome(sentenceState, outcome, options.Model);
                CanTranslateSentence = IsSentenceInput(_lastRawInput);
            });
        }

        /// <summary>
        /// 设置页"测试连接"：用固定短语做一次完整请求
        /// </summary>
        public async Task<LlmTranslationOutcome> TestAiConnectionAsync()
        {
            if (BuildAiOptions(out string? configError) is not LlmTranslationOptions options)
            {
                return LlmTranslationOutcome.Fail(LlmFailureKind.NotConfigured, configError ?? "未配置", retryable: false);
            }

            try
            {
                return await _translationService.TranslateAsync(options, "Hello, world!", TranslationDirection.EnToZh, CancellationToken.None);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"AI test connection failed: {ex.GetType().Name}");
                return LlmTranslationOutcome.Fail(LlmFailureKind.BadResponse, "测试请求异常", retryable: true);
            }
        }

        private void ShowTranslationRunning(AiTranslationItem state)
        {
            void Apply()
            {
                state.Phase = AiTranslationPhase.Running;
                state.IsVisible = true;
                state.ResultText = "";
                state.ErrorText = "";
                state.DetailText = "正在请求翻译接口...";
                state.CanRetry = false;
            }

            if (this.Dispatcher is null)
            {
                Apply();
                return;
            }

            this.Dispatcher.TryEnqueue(Apply);
        }

        private void ShowTranslationFailure(AiTranslationItem state, LlmFailureKind kind, string message, bool retryable)
        {
            void Apply()
            {
                state.Phase = AiTranslationPhase.Failed;
                state.IsVisible = true;
                state.ResultText = "";
                state.ErrorText = message;
                state.CanRetry = retryable;
                state.DetailText = "";
            }

            if (this.Dispatcher is null)
            {
                Apply();
                return;
            }

            this.Dispatcher.TryEnqueue(Apply);
        }

        /// <summary>
        /// 回填请求结果；被取消的请求静默收起，不打扰
        /// </summary>
        private void ApplyTranslationOutcome(AiTranslationItem state, LlmTranslationOutcome outcome, string model)
        {
            System.Diagnostics.Trace.WriteLine($"AI translate outcome: success={outcome.Success}, kind={outcome.FailureKind}");

            if (outcome.Success)
            {
                state.Phase = AiTranslationPhase.Done;
                state.IsVisible = true;
                state.ResultText = outcome.Text;
                state.ErrorText = "";
                state.CanRetry = false;
                state.DetailText = BuildDetailText(outcome, model);
            }
            else if (outcome.FailureKind == LlmFailureKind.Cancelled)
            {
                state.IsVisible = false;
                state.Phase = AiTranslationPhase.Idle;
            }
            else
            {
                state.Phase = AiTranslationPhase.Failed;
                state.IsVisible = true;
                state.ResultText = "";
                state.ErrorText = outcome.FailureMessage;
                state.CanRetry = outcome.Retryable;
                state.DetailText = "";
            }
        }

        private static string BuildDetailText(LlmTranslationOutcome outcome, string model)
        {
            string detail = $"{outcome.DirectionLabel} · {outcome.ElapsedMs / 1000.0:F1}s · {model}";
            if (outcome.Truncated)
            {
                detail += " · 可能被截断";
            }

            if (outcome.PromptTokens > 0 || outcome.CompletionTokens > 0)
            {
                detail += $" · {outcome.PromptTokens}+{outcome.CompletionTokens} tok";
            }

            return detail;
        }

        private static string TruncateText(string text, int maxLength)
        {
            text = text.Trim();
            return text.Length <= maxLength ? text : text[..maxLength] + "…";
        }
    }
}
