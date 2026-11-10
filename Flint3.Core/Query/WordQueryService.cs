using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Flint3.Core.Query
{
    /// <summary>
    /// 聚合多个词典源的查询结果：去重、按“精确 &gt; 前缀 &gt; 其他”稳定排序并补来源标记
    /// </summary>
    public sealed class WordQueryService : IDisposable
    {
        private readonly IReadOnlyList<IWordSource> _sources;

        public WordQueryService(IEnumerable<IWordSource> sources)
        {
            ArgumentNullException.ThrowIfNull(sources);
            _sources = sources.ToArray();
        }

        /// <summary>
        /// 释放持有文件句柄的源（如 mdx），刷新词典列表时由调用方负责
        /// </summary>
        public void Dispose()
        {
            foreach (IWordSource source in _sources)
            {
                if (source is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        public async Task<IReadOnlyList<WordEntry>> QueryAsync(string word, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                return Array.Empty<WordEntry>();
            }

            List<WordEntry> merged = await CollectAsync(
                source => source.QueryAsync(word, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return Rank(merged, word);
        }

        public async Task<IReadOnlyList<WordEntry>> MatchAsync(string word, int limit, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(word) || limit <= 0)
            {
                return Array.Empty<WordEntry>();
            }

            List<WordEntry> merged = await CollectAsync(
                source => source.MatchAsync(word, limit, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return Rank(merged, word);
        }

        public async Task<IReadOnlyList<WordEntry>> PatternAsync(string pattern, int limit, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pattern) || limit <= 0)
            {
                return Array.Empty<WordEntry>();
            }

            List<WordEntry> merged = await CollectAsync(
                source => source.PatternAsync(pattern, limit, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return merged;
        }

        private async Task<List<WordEntry>> CollectAsync(
            Func<IWordSource, Task<IReadOnlyList<WordEntry>>> query,
            CancellationToken cancellationToken)
        {
            Task<IReadOnlyList<WordEntry>>[] tasks = _sources
                .Select(source => SafeQueryAsync(source, query, cancellationToken))
                .ToArray();

            IReadOnlyList<WordEntry>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            List<WordEntry> merged = [];
            for (int i = 0; i < results.Length; i++)
            {
                string sourceName = _sources[i].Name;
                foreach (WordEntry entry in results[i])
                {
                    merged.Add(string.IsNullOrEmpty(entry.Source)
                        ? new WordEntry
                        {
                            Id = entry.Id,
                            Word = entry.Word,
                            StripWord = entry.StripWord,
                            Phonetic = entry.Phonetic,
                            Definition = entry.Definition,
                            Translation = entry.Translation,
                            Exchange = entry.Exchange,
                            Source = sourceName
                        }
                        : entry);
                }
            }

            return merged;
        }

        /// <summary>
        /// 单个词典源失败（文件损坏、库结构不认识等）只丢它自己的结果，不影响其它源
        /// </summary>
        private static async Task<IReadOnlyList<WordEntry>> SafeQueryAsync(
            IWordSource source,
            Func<IWordSource, Task<IReadOnlyList<WordEntry>>> query,
            CancellationToken cancellationToken)
        {
            try
            {
                return await query(source).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"查询词典源 {source.Name} 失败: {ex}");
                return Array.Empty<WordEntry>();
            }
        }

        /// <summary>
        /// 去重（同词大小写不敏感，保留先出现的源）并按精确、前缀、其他稳定分组
        /// </summary>
        private static IReadOnlyList<WordEntry> Rank(List<WordEntry> entries, string word)
        {
            List<WordEntry> ranked = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            for (int group = 0; group < 3; group++)
            {
                foreach (WordEntry entry in entries)
                {
                    if (GroupOf(entry.Word, word) != group || !seen.Add(entry.Word))
                    {
                        continue;
                    }

                    ranked.Add(entry);
                }
            }

            return ranked;
        }

        private static int GroupOf(string entryWord, string query)
        {
            // 精确组沿用原有大小写敏感判断，避免改变已有查询结果
            if (string.Equals(entryWord, query, StringComparison.Ordinal))
            {
                return 0;
            }

            return entryWord.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
        }
    }
}
