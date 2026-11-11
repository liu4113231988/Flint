using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Flint3.Data.Chinese
{
    /// <summary>
    /// 中文词条抽取与查询归一化。构建期与查询期必须共用同一套规则，否则会出现"建了索引却查不到"。
    /// 规则与 docs/chinese-lookup-decision.md 的实测口径一致：按换行与中英文标点切分释义，
    /// 去掉括号注释与词性前缀后，取 2~8 个汉字的连续片段。
    /// </summary>
    public static partial class ChineseTermExtractor
    {
        public const int MinTermLength = 2;
        public const int MaxTermLength = 8;

        /// <summary>
        /// 释义里高频出现但不构成"词"的片段，保留它们只会让结果里塞满噪声
        /// </summary>
        private static readonly HashSet<string> StopTerms = new(StringComparer.Ordinal)
        {
            "网络", "地名", "医", "化", "计", "经", "法", "人名", "的复数", "的变形", "的复数形式",
            "亦作", "英", "美", "俄", "德", "数", "建", "机", "电", "商", "律", "农", "生物", "化学",
        };

        [GeneratedRegex(@"[\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff]+")]
        private static partial Regex ChineseRunRegex();

        [GeneratedRegex(@"\[[^\]]{1,10}\]")]
        private static partial Regex BracketRegex();

        [GeneratedRegex(@"[（(][^）)]{0,30}[）)]")]
        private static partial Regex ParenthesisRegex();

        [GeneratedRegex(@"^(?:n|v|vt|vi|a|ad|adv|prep|pron|conj|num|art|int|aux|abbr|pl|past|pp)\.\s*", RegexOptions.IgnoreCase)]
        private static partial Regex PartOfSpeechRegex();

        [GeneratedRegex(@"[\n\r,，;；、/|]+")]
        private static partial Regex SeparatorRegex();

        public static bool ContainsChinese(string text)
        {
            return !string.IsNullOrEmpty(text) && ChineseRunRegex().IsMatch(text);
        }

        private static bool IsChineseChar(char value)
        {
            return (value >= '\u3400' && value <= '\u4dbf')
                || (value >= '\u4e00' && value <= '\u9fff')
                || (value >= '\uf900' && value <= '\ufaff');
        }

        /// <summary>
        /// 只有整串都是汉字、长度 1~8 的输入才走中文反查；混合输入仍按英文查词处理。
        /// 单字输入按前缀处理（索引里没有单字词条），多字输入先精确后前缀。
        /// </summary>
        public static bool TryNormalizeQuery(string query, out string term)
        {
            term = string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                return false;
            }

            string trimmed = query.Trim();
            if (trimmed.Length < 1 || trimmed.Length > MaxTermLength)
            {
                return false;
            }

            foreach (char value in trimmed)
            {
                if (!IsChineseChar(value))
                {
                    return false;
                }
            }

            term = trimmed;
            return true;
        }

        /// <summary>
        /// 从一条英文释义里抽取中文词条。返回的序列已按记录内去重。
        /// </summary>
        public static IEnumerable<string> Extract(string translation)
        {
            if (string.IsNullOrEmpty(translation) || !ContainsChinese(translation))
            {
                yield break;
            }

            HashSet<string> seen = new(StringComparer.Ordinal);
            string text = BracketRegex().Replace(translation, " ");
            text = ParenthesisRegex().Replace(text, " ");

            foreach (string rawSegment in SeparatorRegex().Split(text))
            {
                string segment = PartOfSpeechRegex().Replace(rawSegment.Trim(), string.Empty);
                foreach (Match match in ChineseRunRegex().Matches(segment))
                {
                    string run = match.Value;
                    if (run.Length >= MinTermLength && run.Length <= MaxTermLength && !StopTerms.Contains(run))
                    {
                        if (seen.Add(run))
                        {
                            yield return run;
                        }
                    }
                }
            }
        }
    }
}
