using System;
using System.Threading;
using System.Threading.Tasks;

namespace Flint3.Core.Translation
{
    /// <summary>
    /// 翻译门面：方向识别、prompt 组装、失败归一化。
    /// 不做结果缓存（用户决策）；并发控制（全局单在途 + 取消）由调用方通过独立协调器负责。
    /// </summary>
    public sealed class LlmTranslationService
    {
        private const string EnToZhPrompt =
            "You are a concise translation assistant. Translate the user's English text into natural Simplified Chinese. " +
            "Reply with the translation only, plain text, no quotes, no markdown, no explanations.";

        private const string ZhToEnPrompt =
            "You are a concise translation assistant. Translate the user's Chinese text into natural English. " +
            "Reply with the translation only, plain text, no quotes, no markdown, no explanations.";

        private const string EntryEnToZhPrompt =
            "你是一名简洁的双语词典助手。用户会给出词典词条（词、音标、本地释义），请用简体中文回答：" +
            "先用一两句话概括该词的核心含义（语境释义），再给出一条英文例句及其中文翻译。" +
            "纯文本输出，不要 Markdown，不要复读全部本地释义，总长度控制在 150 字以内。";

        private readonly OpenAiCompatibleClient _client = new();

        public async Task<LlmTranslationOutcome> TranslateAsync(LlmTranslationOptions options, string text, TranslationDirection direction, CancellationToken token, string? referenceContext = null)
        {
            TranslationDirection resolved = direction == TranslationDirection.Auto ? TranslationDirectionDetector.Detect(text) : direction;
            string directionLabel = TranslationDirectionDetector.GetLabel(resolved);

            (string systemPrompt, string userContent) = BuildPrompt(resolved, text, referenceContext);
            LlmTranslationOutcome outcome = await _client.CompleteAsync(options, systemPrompt, userContent, token);
            if (outcome.Success)
            {
                return LlmTranslationOutcome.Ok(outcome.Text, directionLabel, outcome.PromptTokens, outcome.CompletionTokens, outcome.ElapsedMs, outcome.Truncated);
            }

            return outcome;
        }

        private static (string System, string User) BuildPrompt(TranslationDirection direction, string text, string? referenceContext)
        {
            if (direction == TranslationDirection.ZhToEn)
            {
                return (ZhToEnPrompt, text);
            }

            // 词条场景带本地释义上下文；纯句子不带
            if (referenceContext is null)
            {
                return (EnToZhPrompt, text);
            }

            return (EntryEnToZhPrompt, $"词条：{text}{referenceContext}");
        }
    }
}
