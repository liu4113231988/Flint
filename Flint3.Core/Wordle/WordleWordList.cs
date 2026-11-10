using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flint3.Core.Serialization;

namespace Flint3.Core.Wordle
{
    /// <summary>
    /// 预生成的 Wordle 词表：Answers 是可用于出题的常用词，Allowed 是允许输入的完整 5 字母词表。
    /// 词表在构建期从 ECDICT 生成并作为嵌入资源，运行时不再扫库。
    /// </summary>
    public sealed class WordleWordList
    {
        private const string ResourceSuffix = "wordle_words.json";

        private static readonly Lazy<WordleWordList> LazyDefault = new(LoadCore, isThreadSafe: true);

        private HashSet<string>? _allowedSet;

        [JsonPropertyName("answers")]
        public string[] Answers { get; set; } = [];

        [JsonPropertyName("allowed")]
        public string[] Allowed { get; set; } = [];

        public static WordleWordList Default => LazyDefault.Value;

        public bool IsAllowed(string word)
        {
            if (string.IsNullOrEmpty(word))
            {
                return false;
            }

            _allowedSet ??= new HashSet<string>(Allowed, StringComparer.Ordinal);
            return _allowedSet.Contains(word);
        }

        public static WordleWordList Load(Stream stream)
        {
            WordleWordList? list = JsonSerializer.Deserialize(stream, FlintJsonContext.Default.WordleWordList);
            if (list is null || list.Answers.Length == 0 || list.Allowed.Length == 0)
            {
                throw new InvalidOperationException("Wordle 词表为空或格式不正确");
            }

            return list;
        }

        private static WordleWordList LoadCore()
        {
            System.Reflection.Assembly assembly = typeof(WordleWordList).Assembly;
            string? name = Array.Find(
                assembly.GetManifestResourceNames(),
                item => item.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase));

            if (name is null)
            {
                throw new InvalidOperationException("找不到 Wordle 词表资源");
            }

            using Stream stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException("无法打开 Wordle 词表资源");
            return Load(stream);
        }
    }
}
