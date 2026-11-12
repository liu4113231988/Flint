using System;
using System.Collections.Generic;

namespace Flint3.Core.Translation
{
    /// <summary>
    /// 翻译方向；Auto 表示按 CJK 字符占比自动识别
    /// </summary>
    public enum TranslationDirection
    {
        Auto,
        EnToZh,
        ZhToEn,
    }

    /// <summary>
    /// 翻译失败的归一化类别（与具体厂商协议无关）
    /// </summary>
    public enum LlmFailureKind
    {
        None,
        /// <summary>未启用或未填 baseUrl/model/key</summary>
        NotConfigured,
        /// <summary>DNS/连接失败等网络层错误</summary>
        Network,
        /// <summary>401/403，Key 无效或无权限</summary>
        Unauthorized,
        /// <summary>429 限流</summary>
        RateLimited,
        /// <summary>额度耗尽（insufficient_quota / RESOURCE_EXHAUSTED）</summary>
        QuotaExceeded,
        /// <summary>请求超时</summary>
        Timeout,
        /// <summary>5xx 服务端错误</summary>
        ServerError,
        /// <summary>400 等请求被拒</summary>
        BadRequest,
        /// <summary>内容被安全策略拦截</summary>
        Blocked,
        /// <summary>返回体缺失/结构不符</summary>
        BadResponse,
        /// <summary>被新请求取消</summary>
        Cancelled,
    }

    /// <summary>
    /// 一次翻译请求的完整配置（由调用方从设置构造；密钥在 App 层解密后传入）
    /// </summary>
    public sealed class LlmTranslationOptions
    {
        public string BaseUrl { get; init; } = "";
        public string ApiKey { get; init; } = "";
        public string Model { get; init; } = "";
        public IReadOnlyDictionary<string, string> ExtraHeaders { get; init; } = new Dictionary<string, string>();
        public int MaxTokens { get; init; } = 512;
        /// <summary>单次请求总超时（毫秒）</summary>
        public int TimeoutMs { get; init; } = 30_000;
    }

    /// <summary>
    /// 翻译结果：成功或失败的统一出口，UI 与服务都不感知具体协议
    /// </summary>
    public sealed class LlmTranslationOutcome
    {
        public bool Success { get; private init; }
        public string Text { get; private init; } = "";
        /// <summary>如 "英 → 中"</summary>
        public string DirectionLabel { get; private init; } = "";
        public int PromptTokens { get; private init; }
        public int CompletionTokens { get; private init; }
        public long ElapsedMs { get; private init; }
        /// <summary>输出被 max_tokens 截断</summary>
        public bool Truncated { get; private init; }

        public LlmFailureKind FailureKind { get; private init; } = LlmFailureKind.None;
        public string FailureMessage { get; private init; } = "";
        public bool Retryable { get; private init; }

        public static LlmTranslationOutcome Ok(string text, string directionLabel, int promptTokens, int completionTokens, long elapsedMs, bool truncated = false)
            => new() { Success = true, Text = text, DirectionLabel = directionLabel, PromptTokens = promptTokens, CompletionTokens = completionTokens, ElapsedMs = elapsedMs, Truncated = truncated };

        public static LlmTranslationOutcome Fail(LlmFailureKind kind, string message, bool retryable)
            => new() { Success = false, FailureKind = kind, FailureMessage = message, Retryable = retryable };
    }

    public static class TranslationDirectionDetector
    {
        /// <summary>
        /// CJK 字符占比 ≥ 30% 视为中文，走中→英；否则英→中
        /// </summary>
        public static TranslationDirection Detect(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return TranslationDirection.EnToZh;
            }

            int cjk = 0;
            int total = 0;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                total++;
                if (c >= '\u4E00' && c <= '\u9FFF')
                {
                    cjk++;
                }
            }

            return total > 0 && cjk * 10 >= total * 3 ? TranslationDirection.ZhToEn : TranslationDirection.EnToZh;
        }

        public static string GetLabel(TranslationDirection direction) => direction == TranslationDirection.ZhToEn ? "中 → 英" : "英 → 中";
    }
}
