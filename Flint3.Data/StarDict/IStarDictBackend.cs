using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Flint3.Data.Models;

namespace Flint3.Data.StarDict
{
    /// <summary>
    /// 词库后端。SQLite 与 CSF 两条实现对外表现必须一致。
    /// </summary>
    internal interface IStarDictBackend : System.IDisposable
    {
        Task<List<StarDictWordItem>> QueryWord(string word, CancellationToken cancellationToken);

        Task<List<StarDictWordItem>> MatchWord(string word, int limit, CancellationToken cancellationToken);

        Task<List<StarDictWordItem>> PatternWord(string pattern, int? exactLength, int limit, CancellationToken cancellationToken);

        Task<List<StarDictWordItem>> GetAllExtraGlossaryWords(string tag, string word, CancellationToken cancellationToken);

        Task<List<StarDictWordItem>> GetIncrementalExtraGlossaryWords(string tag, long startId, int limit, string word, CancellationToken cancellationToken);

        Task<int> GetExtraGlossaryWordsCount(string tag, CancellationToken cancellationToken);
    }
}
