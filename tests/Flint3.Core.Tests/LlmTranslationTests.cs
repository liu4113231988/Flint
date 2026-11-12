using System;
using System.Text.Json;
using Flint3.Core.Translation;
using Xunit;

namespace Flint3.Core.Tests
{
    public class LlmTranslationTests
    {
        [Theory]
        [InlineData("hello world", TranslationDirection.EnToZh)]
        [InlineData("The quick brown fox jumps over the lazy dog", TranslationDirection.EnToZh)]
        [InlineData("这是一段足够长的中文句子", TranslationDirection.ZhToEn)]
        [InlineData("混合内容 english words 占多数", TranslationDirection.ZhToEn)]
        public void Detect_按CJK占比判断方向(string text, TranslationDirection expected)
        {
            Assert.Equal(expected, TranslationDirectionDetector.Detect(text));
        }

        [Fact]
        public void JsonContext_蛇形命名且可往返解析()
        {
            var request = new ChatCompletionRequest
            {
                Model = "deepseek-chat",
                MaxTokens = 512,
                Messages = [new ChatMessage { Role = "user", Content = "hello" }],
            };

            string json = JsonSerializer.Serialize(request, LlmJsonContext.Default.ChatCompletionRequest);
            Assert.Contains("\"max_tokens\"", json, StringComparison.Ordinal);
            Assert.Contains("\"deepseek-chat\"", json, StringComparison.Ordinal);

            var response = JsonSerializer.Deserialize<ChatCompletionResponse>(
                """
                {"choices":[{"message":{"role":"assistant","content":"你好"},"finish_reason":"stop"}],"usage":{"prompt_tokens":12,"completion_tokens":3}}
                """, LlmJsonContext.Default.ChatCompletionResponse)!;

            Assert.Equal("你好", response.Choices![0].Message!.Content);
            Assert.Equal("stop", response.Choices[0].FinishReason);
            Assert.Equal(12, response.Usage!.PromptTokens);
        }

        [Fact]
        public void Outcome_工厂方法携带字段()
        {
            var ok = LlmTranslationOutcome.Ok("你好", "英 → 中", 10, 5, 100);
            Assert.True(ok.Success);
            Assert.Equal("英 → 中", ok.DirectionLabel);

            var fail = LlmTranslationOutcome.Fail(LlmFailureKind.Unauthorized, "Key 无效", retryable: false);
            Assert.False(fail.Success);
            Assert.False(fail.Retryable);
            Assert.Equal(LlmFailureKind.Unauthorized, fail.FailureKind);
        }
    }
}
