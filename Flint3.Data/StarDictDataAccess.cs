using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Flint3.Data.Models;
using Flint3.Data.StarDict;

namespace Flint3.Data
{
    /// <summary>词库后端选择。Auto 优先使用同目录下的 stardict.csf，缺失时回退 SQLite。</summary>
    public enum StarDictBackendKind
    {
        Auto,
        Sqlite,
        Csf,
    }

    public static class StarDictDataAccess
    {
        private static readonly object _initializeLock = new();

        private static string? _databasePath = null;

        private static StarDictBackendKind _backendKind = StarDictBackendKind.Auto;

        private static IStarDictBackend? _backend = null;

        private static string DatabasePath => _databasePath ?? Path.Combine(AppContext.BaseDirectory, "Data", "stardict.db");

        /// <summary>
        /// 初始化数据库连接，可被重复调用；真正的后端在第一次查询时惰性创建
        /// </summary>
        /// <param name="databasePath">数据库路径，为空时沿用上次路径（默认输出目录下的 Data/stardict.db，测试可传入自己的库）</param>
        public static Task InitializeDatabase(string? databasePath = null)
        {
            lock (_initializeLock)
            {
                if (databasePath is not null && !string.Equals(databasePath, _databasePath, StringComparison.Ordinal))
                {
                    _databasePath = databasePath;
                    DisposeBackend();
                }

                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// 显式指定后端。用于“CSF 出问题时切回 SQLite”的开关，未指定时按 Auto 自动探测。
        /// </summary>
        public static void SetBackend(StarDictBackendKind backendKind)
        {
            lock (_initializeLock)
            {
                _backendKind = backendKind;
                DisposeBackend();
            }
        }

        public static void CloseDatabase()
        {
            try
            {
                lock (_initializeLock)
                {
                    DisposeBackend();
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }

        private static void DisposeBackend()
        {
            _backend?.Dispose();
            _backend = null;
        }

        private static IStarDictBackend GetBackend()
        {
            IStarDictBackend? backend = _backend;
            if (backend is not null)
            {
                return backend;
            }

            lock (_initializeLock)
            {
                if (_backend is null)
                {
                    _backend = CreateBackend();
                }

                return _backend;
            }
        }

        private static IStarDictBackend CreateBackend()
        {
            string databasePath = DatabasePath;
            StarDictBackendKind kind = _backendKind;

            if (kind == StarDictBackendKind.Auto)
            {
                string? environment = Environment.GetEnvironmentVariable("FLINT_STARDICT_BACKEND");
                kind = environment?.Trim().ToLowerInvariant() switch
                {
                    "sqlite" => StarDictBackendKind.Sqlite,
                    "csf" => StarDictBackendKind.Csf,
                    _ => databasePath.EndsWith(".csf", StringComparison.OrdinalIgnoreCase) ? StarDictBackendKind.Csf : StarDictBackendKind.Auto,
                };
            }

            if (kind == StarDictBackendKind.Csf)
            {
                string csfPath = databasePath.EndsWith(".csf", StringComparison.OrdinalIgnoreCase)
                    ? databasePath
                    : ResolveCsfPath(databasePath);
                return new CsfStarDictBackend(csfPath);
            }

            if (kind == StarDictBackendKind.Auto)
            {
                string csfPath = ResolveCsfPath(databasePath);
                if (File.Exists(csfPath))
                {
                    try
                    {
                        return new CsfStarDictBackend(csfPath);
                    }
                    catch (Exception ex)
                    {
                        // CSF 损坏或版本不匹配时回退原有 SQLite 路径，不改变产品行为
                        System.Diagnostics.Trace.WriteLine(ex);
                    }
                }

                return new SqliteStarDictBackend(databasePath);
            }

            return new SqliteStarDictBackend(databasePath);
        }

        private static string ResolveCsfPath(string databasePath)
        {
            string? environment = Environment.GetEnvironmentVariable("FLINT_STARDICT_CSF");
            if (!string.IsNullOrEmpty(environment))
            {
                return environment;
            }

            string? directory = Path.GetDirectoryName(databasePath);
            return string.IsNullOrEmpty(directory) ? "stardict.csf" : Path.Combine(directory, "stardict.csf");
        }

        /// <summary>
        /// 查找单词
        /// </summary>
        /// <param name="word">精确查找的单词文本</param>
        /// <param name="cancellationToken"></param>
        /// <returns>所有的完全匹配的单词</returns>
        public static async Task<List<StarDictWordItem>> QueryWord(string word, CancellationToken cancellationToken = default)
        {
            try
            {
                return await GetBackend().QueryWord(word, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            return [];
        }

        /// <summary>
        /// 匹配单词
        /// </summary>
        /// <param name="word">前缀匹配的单词文本</param>
        /// <param name="limit">查询结果条数限制</param>
        /// <param name="cancellationToken"></param>
        /// <returns>特定数量的前缀匹配的单词</returns>
        public static async Task<List<StarDictWordItem>> MatchWord(string word, int limit, CancellationToken cancellationToken = default)
        {
            try
            {
                return await GetBackend().MatchWord(word, limit, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            return [];
        }

        /// <summary>
        /// 按 LIKE 模式匹配单词
        /// </summary>
        /// <param name="pattern">已翻译好的 SQL LIKE 模式</param>
        /// <param name="exactLength">模式的定长约束，null 表示不限长度</param>
        /// <param name="limit">查询结果条数限制</param>
        /// <param name="cancellationToken"></param>
        public static async Task<List<StarDictWordItem>> PatternWord(string pattern, int? exactLength, int limit, CancellationToken cancellationToken = default)
        {
            try
            {
                return await GetBackend().PatternWord(pattern, exactLength, limit, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            return [];
        }

        #region 扩展生词本

        /// <summary>
        /// 获取内置生词本的所有单词
        /// </summary>
        /// <param name="tag">内置生词本标签</param>
        /// <param name="word">需要匹配的单词文本，为空时表示获取所有单词</param>
        /// <param name="cancellationToken"></param>
        /// <returns>完整的单词列表</returns>
        public static async Task<List<StarDictWordItem>> GetAllExtraGlossaryWords(string tag, string word, CancellationToken cancellationToken)
        {
            try
            {
                return await GetBackend().GetAllExtraGlossaryWords(tag, word, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            return [];
        }

        /// <summary>
        /// 增量获取内置生词本指定数量的单词
        /// </summary>
        /// <param name="tag">内置生词本标签</param>
        /// <param name="startId">增量起点单词的最小ID</param>
        /// <param name="limit">增量获取的单词最大数量</param>
        /// <param name="word">需要匹配的单词文本，为空时表示获取所有单词</param>
        /// <param name="cancellationToken"></param>
        /// <returns>增量获取的单词列表</returns>
        public static async Task<List<StarDictWordItem>> GetIncrementalExtraGlossaryWords(string tag, long startId, int limit, string word, CancellationToken cancellationToken)
        {
            try
            {
                return await GetBackend().GetIncrementalExtraGlossaryWords(tag, startId, limit, word, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            return [];
        }

        /// <summary>
        /// 获取指定生词本的单词总数
        /// </summary>
        /// <param name="tag">内置生词本标签</param>
        /// <returns>单词数量</returns>
        public static async Task<int> GetExtraGlossaryWordsCount(string tag, CancellationToken cancellationToken = default)
        {
            try
            {
                return await GetBackend().GetExtraGlossaryWordsCount(tag, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            return -1;
        }

        #endregion
    }
}
