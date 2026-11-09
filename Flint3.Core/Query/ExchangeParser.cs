using System;
using System.Collections.Generic;

namespace Flint3.Core.Query
{
    public readonly record struct WordExchange(string Type, string Word);

    /// <summary>
    /// ECDICT exchange 字段（p:/d:/i:/3:/r:/t:/s:/0: 用 / 分隔）解析
    /// </summary>
    public static class ExchangeParser
    {
        public static IReadOnlyList<WordExchange> Parse(string? exchange)
        {
            if (string.IsNullOrWhiteSpace(exchange))
            {
                return Array.Empty<WordExchange>();
            }

            List<WordExchange> results = [];
            foreach (string item in exchange.Split('/'))
            {
                if (TryMapTag(item, "p:", "过去式", out WordExchange mapped)
                    || TryMapTag(item, "d:", "过去分词", out mapped)
                    || TryMapTag(item, "i:", "现在分词", out mapped)
                    || TryMapTag(item, "3:", "第三人称单数", out mapped)
                    || TryMapTag(item, "r:", "比较级", out mapped)
                    || TryMapTag(item, "t:", "最高级", out mapped)
                    || TryMapTag(item, "s:", "复数", out mapped)
                    || TryMapTag(item, "0:", "原型", out mapped))
                {
                    results.Add(mapped);
                }
            }

            return results;
        }

        private static bool TryMapTag(string item, string tag, string type, out WordExchange exchange)
        {
            exchange = default;
            if (!item.Contains(tag, StringComparison.Ordinal))
            {
                return false;
            }

            string word = item.Replace(tag, "");
            if (string.IsNullOrWhiteSpace(word))
            {
                return false;
            }

            exchange = new WordExchange(type, word);
            return true;
        }
    }
}
