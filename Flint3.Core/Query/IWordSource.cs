using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Flint3.Core.Query
{
    /// <summary>
    /// 单个词典源的查询能力；实现方只负责取数，不做聚合与排序
    /// </summary>
    public interface IWordSource
    {
        /// <summary>
        /// 来源名称，用于结果标记与排序权重
        /// </summary>
        string Name { get; }

        Task<IReadOnlyList<WordEntry>> QueryAsync(string word, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<WordEntry>> MatchAsync(string word, int limit, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<WordEntry>> PatternAsync(string pattern, int limit, CancellationToken cancellationToken = default);
    }
}
