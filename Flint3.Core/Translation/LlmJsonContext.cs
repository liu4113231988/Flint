using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Flint3.Core.Translation
{
    // OpenAI 兼容 /chat/completions 的最小请求/响应模型。
    // 走源生成序列化：Flint3.Core 标记 IsAotCompatible，禁止反射式序列化。
    // 字段名统一由 SnakeCaseLower 命名策略输出（max_tokens、finish_reason 等）。

    public sealed class ChatMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }

    public sealed class ChatCompletionRequest
    {
        public string Model { get; set; } = "";
        public List<ChatMessage> Messages { get; set; } = [];
        public int? MaxTokens { get; set; }
        public double? Temperature { get; set; }
        public bool Stream { get; set; }
    }

    public sealed class ChatResponseMessage
    {
        public string? Role { get; set; }
        public string? Content { get; set; }
    }

    public sealed class ChatChoice
    {
        public ChatResponseMessage? Message { get; set; }
        public string? FinishReason { get; set; }
    }

    public sealed class ChatUsage
    {
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
    }

    public sealed class ChatCompletionResponse
    {
        public List<ChatChoice>? Choices { get; set; }
        public ChatUsage? Usage { get; set; }
    }

    public sealed class ChatErrorDetail
    {
        public string? Message { get; set; }
        public string? Type { get; set; }
        public string? Code { get; set; }
    }

    public sealed class ChatErrorEnvelope
    {
        public ChatErrorDetail? Error { get; set; }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(ChatCompletionRequest))]
    [JsonSerializable(typeof(ChatCompletionResponse))]
    [JsonSerializable(typeof(ChatErrorEnvelope))]
    public partial class LlmJsonContext : JsonSerializerContext
    {
    }
}
