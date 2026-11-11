using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Flint3.Data.Csf;
using Flint3.Data.Models;

namespace Flint3.Data.StarDict
{
    /// <summary>
    /// CSF 只读词典后端：单文件 mmap + 按块解压，语义与 SQLite 版本逐项对齐
    /// </summary>
    internal sealed class CsfStarDictBackend : IStarDictBackend
    {
        private readonly CsfReader _reader;

        public CsfStarDictBackend(string csfPath)
        {
            _reader = new CsfReader(csfPath);
        }

        public Task<List<StarDictWordItem>> QueryWord(string word, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_reader.QueryWord(word));
        }

        public Task<List<StarDictWordItem>> MatchWord(string word, int limit, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_reader.MatchWord(word, limit));
        }

        public Task<List<StarDictWordItem>> PatternWord(string pattern, int? exactLength, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult(_reader.PatternWord(pattern, exactLength, limit, cancellationToken));
        }

        public Task<List<StarDictWordItem>> GetAllExtraGlossaryWords(string tag, string word, CancellationToken cancellationToken)
        {
            return Task.FromResult(_reader.GetAllGlossaryWords(tag, word, cancellationToken));
        }

        public Task<List<StarDictWordItem>> GetIncrementalExtraGlossaryWords(string tag, long startId, int limit, string word, CancellationToken cancellationToken)
        {
            return Task.FromResult(_reader.GetIncrementalGlossaryWords(tag, startId, limit, word, cancellationToken));
        }

        public Task<int> GetExtraGlossaryWordsCount(string tag, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_reader.GetGlossaryWordsCount(tag));
        }

        public void Dispose()
        {
            _reader.Dispose();
        }
    }
}
