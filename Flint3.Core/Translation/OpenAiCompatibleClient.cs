using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Flint3.Core.Translation
{
    /// <summary>
    /// OpenAI 兼容协议的最小客户端：单轮对话、非流式、纯文本回复。
    /// 兼容 DeepSeek / 通义 compatible-mode / Kimi / 智谱 / 硅基流动 / OneAPI 网关 / Ollama 等实现。
    /// 日志纪律：只记录状态码与异常类型，不记录请求体、响应体与鉴权头。
    /// </summary>
    public sealed class OpenAiCompatibleClient
    {
        // 进程级单例；不引入 IHttpClientFactory，避免拉依赖
        private static readonly HttpClient s_http = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            // 总超时交给每次请求的链接 CTS 控制，避免 HttpClient.Timeout 与重试互相干扰
            Timeout = Timeout.InfiniteTimeSpan,
        };

        public async Task<LlmTranslationOutcome> CompleteAsync(LlmTranslationOptions options, string systemPrompt, string userContent, CancellationToken requestToken)
        {
            long start = Stopwatch.GetTimestamp();

            ChatCompletionRequest payload = new()
            {
                Model = options.Model,
                MaxTokens = options.MaxTokens,
                Temperature = 0.2,
                Stream = false,
                Messages = [new ChatMessage { Role = "system", Content = systemPrompt }, new ChatMessage { Role = "user", Content = userContent }],
            };

            string json = JsonSerializer.Serialize(payload, LlmJsonContext.Default.ChatCompletionRequest);

            // 429 与 5xx 重试 1 次（遵守 Retry-After，上限 5 秒）；其余错误码不重试
            for (int attempt = 1; ; attempt++)
            {
                using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
                linked.CancelAfter(options.TimeoutMs);

                try
                {
                    using HttpRequestMessage request = new(HttpMethod.Post, BuildEndpoint(options.BaseUrl));
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    if (!string.IsNullOrWhiteSpace(options.ApiKey))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
                    }
                    foreach ((string name, string value) in options.ExtraHeaders)
                    {
                        request.Headers.TryAddWithoutValidation(name, value);
                    }

                    using HttpResponseMessage response = await s_http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);

                    if (!response.IsSuccessStatusCode)
                    {
                        string body = await ReadBodyAsync(response.Content, linked.Token);
                        LlmTranslationOutcome failure = MapHttpError(response.StatusCode, body);

                        bool retryable = (int)response.StatusCode == 429 || (int)response.StatusCode >= 500;
                        if (retryable && attempt == 1)
                        {
                            TimeSpan delay = GetRetryDelay(response.Headers.RetryAfter, fallback: TimeSpan.FromSeconds(1));
                            if (delay <= TimeSpan.FromSeconds(5))
                            {
                                await Task.Delay(delay, requestToken);
                                continue;
                            }
                        }

                        return failure;
                    }

                    ChatCompletionResponse? parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(await ReadBodyAsync(response.Content, linked.Token), LlmJsonContext.Default.ChatCompletionResponse);
                    string? text = parsed?.Choices is { Count: > 0 } choices ? choices[0].Message?.Content : null;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return LlmTranslationOutcome.Fail(LlmFailureKind.BadResponse, "返回内容为空", retryable: true);
                    }

                    long elapsed = (Stopwatch.GetTimestamp() - start) * 1000 / Stopwatch.Frequency;
                    bool truncated = string.Equals(parsed!.Choices![0].FinishReason, "length", StringComparison.OrdinalIgnoreCase);
                    return LlmTranslationOutcome.Ok(text.Trim(), "", parsed.Usage?.PromptTokens ?? 0, parsed.Usage?.CompletionTokens ?? 0, elapsed, truncated);
                }
                catch (OperationCanceledException) when (!requestToken.IsCancellationRequested)
                {
                    // 仅链接 CTS 到期：本次请求超时
                    return LlmTranslationOutcome.Fail(LlmFailureKind.Timeout, "请求超时", retryable: true);
                }
                catch (OperationCanceledException)
                {
                    // 用户侧取消，静默
                    return LlmTranslationOutcome.Fail(LlmFailureKind.Cancelled, "", retryable: false);
                }
                catch (HttpRequestException ex)
                {
                    Debug.WriteLine($"AI translate network error: {ex.GetType().Name}");
                    return LlmTranslationOutcome.Fail(LlmFailureKind.Network, "无法连接到翻译服务，请检查网络或代理", retryable: true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AI translate error: {ex.GetType().Name}");
                    return LlmTranslationOutcome.Fail(LlmFailureKind.BadResponse, "返回内容无法解析", retryable: true);
                }
            }
        }

        private static string BuildEndpoint(string baseUrl)
        {
            string trimmed = baseUrl.Trim().TrimEnd('/');
            return trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + "/chat/completions";
        }

        private static async Task<string> ReadBodyAsync(HttpContent content, CancellationToken token)
        {
            using StreamReader reader = new(await content.ReadAsStreamAsync(token), Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            return await reader.ReadToEndAsync(token);
        }

        private static LlmTranslationOutcome MapHttpError(System.Net.HttpStatusCode status, string body)
        {
            string? message = null;
            string? code = null;
            try
            {
                ChatErrorEnvelope? envelope = JsonSerializer.Deserialize(body, LlmJsonContext.Default.ChatErrorEnvelope);
                message = envelope?.Error?.Message;
                code = envelope?.Error?.Code;
            }
            catch (JsonException)
            {
                // 非 JSON 错误体（网关 HTML 等），保持 message 为空
            }

            string detail = string.IsNullOrWhiteSpace(message) ? $"HTTP {(int)status}" : message!;
            bool quotaHint = !string.IsNullOrEmpty(code) && code.Contains("quota", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("quota", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("额度", StringComparison.Ordinal)
                || detail.Contains("余额", StringComparison.Ordinal);

            return (int)status switch
            {
                401 or 403 => LlmTranslationOutcome.Fail(LlmFailureKind.Unauthorized, $"API Key 无效或无权限（{detail}）", retryable: false),
                429 => quotaHint
                    ? LlmTranslationOutcome.Fail(LlmFailureKind.QuotaExceeded, $"账户额度不足（{detail}）", retryable: false)
                    : LlmTranslationOutcome.Fail(LlmFailureKind.RateLimited, $"请求过于频繁（{detail}）", retryable: true),
                _ when (int)status >= 500 => LlmTranslationOutcome.Fail(LlmFailureKind.ServerError, $"服务端错误（{detail}）", retryable: true),
                _ when quotaHint => LlmTranslationOutcome.Fail(LlmFailureKind.QuotaExceeded, $"账户额度不足（{detail}）", retryable: false),
                _ => LlmTranslationOutcome.Fail(LlmFailureKind.BadRequest, $"请求被拒绝（{detail}）", retryable: false),
            };
        }

        private static TimeSpan GetRetryDelay(RetryConditionHeaderValue? retryAfter, TimeSpan fallback)
        {
            if (retryAfter?.Delta is TimeSpan delta)
            {
                return delta;
            }

            if (retryAfter?.Date is DateTimeOffset date)
            {
                return date - DateTimeOffset.UtcNow;
            }

            return fallback;
        }
    }
}
