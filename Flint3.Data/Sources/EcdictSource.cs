using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Flint3.Core.Query;
using Flint3.Data.Chinese;
using Flint3.Data.Models;

namespace Flint3.Data.Sources
{
    /// <summary>
    /// ECDICT（stardict.db）词源，直接复用现有 SQL 与结果。
    /// 中文输入走旁挂反查索引，英文路径完全不受影响。
    /// </summary>
    public sealed class EcdictSource : IWordSource
    {
        public string Name => "Ecdict";

        public async Task<IReadOnlyList<WordEntry>> QueryAsync(string word, CancellationToken cancellationToken = default)
        {
            if (ChineseTermExtractor.TryNormalizeQuery(word, out _))
            {
                return Map(await StarDictDataAccess.QueryChineseWord(word, cancellationToken).ConfigureAwait(false));
            }

            return Map(await StarDictDataAccess.QueryWord(word, cancellationToken).ConfigureAwait(false));
        }

        public async Task<IReadOnlyList<WordEntry>> MatchAsync(string word, int limit, CancellationToken cancellationToken = default)
        {
            if (ChineseTermExtractor.TryNormalizeQuery(word, out _))
            {
                return Map(await StarDictDataAccess.MatchChineseWord(word, limit, cancellationToken).ConfigureAwait(false));
            }

            return Map(await StarDictDataAccess.MatchWord(word, limit, cancellationToken).ConfigureAwait(false));
        }

        public async Task<IReadOnlyList<WordEntry>> PatternAsync(string pattern, int limit, CancellationToken cancellationToken = default)
        {
            if (!WordPattern.TryTranslate(pattern, out WordPattern translated))
            {
                return [];
            }

            return Map(await StarDictDataAccess
                .PatternWord(translated.Like, translated.ExactLength, limit, cancellationToken)
                .ConfigureAwait(false));
        }

        private IReadOnlyList<WordEntry> Map(List<StarDictWordItem> items)
        {
            if (items.Count == 0)
            {
                return [];
            }

            List<WordEntry> entries = new(items.Count);
            foreach (StarDictWordItem item in items)
            {
                entries.Add(new WordEntry
                {
                    Id = item.Id,
                    Word = item.Word,
                    StripWord = item.StripWord,
                    Phonetic = item.Phonetic,
                    Definition = item.Definition,
                    Translation = item.Translation,
                    Exchange = item.Exchange,
                    Source = Name
                });
            }

            return entries;
        }
    }
}
