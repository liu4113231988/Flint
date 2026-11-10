using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Flint3.Data.Models;
using Microsoft.Data.Sqlite;

namespace Flint3.Data
{
    public static class StarDictDataAccess
    {
        private static Task? _initializeTask = null;

        private static readonly object _initializeLock = new();

        private static string? _databasePath = null;

        private static string DatabasePath => _databasePath ?? Path.Combine(AppContext.BaseDirectory, "Data", "stardict.db");

        /// <summary>
        /// 初始化数据库连接，可被重复调用；查询方法内部也会等待此任务完成
        /// </summary>
        /// <param name="databasePath">数据库路径，为空时沿用上次路径（默认输出目录下的 Data/stardict.db，测试可传入自己的库）</param>
        public static Task InitializeDatabase(string? databasePath = null)
        {
            lock (_initializeLock)
            {
                if (databasePath is not null)
                {
                    _databasePath = databasePath;
                }

                // 预热一条连接，失败不在这里抛出，交给具体查询处理
                _initializeTask ??= WarmUpAsync(DatabasePath);
                return _initializeTask;
            }
        }

        public static void CloseDatabase()
        {
            try
            {
                lock (_initializeLock)
                {
                    _initializeTask = null;
                }

                // 每个查询独立连接，关闭时清空连接池以释放文件句柄
                SqliteConnection.ClearAllPools();
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }

        private static async Task WarmUpAsync(string path)
        {
            try
            {
                using SqliteConnection connection = new($"Filename={path}");
                await connection.OpenAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }

        /// <summary>
        /// 每个查询使用独立连接（由 Microsoft.Data.Sqlite 连接池复用），避免共享连接并发冲突
        /// </summary>
        private static async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
        {
            await InitializeDatabase().WaitAsync(cancellationToken).ConfigureAwait(false);

            SqliteConnection connection = new($"Filename={DatabasePath}");
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        private static StarDictWordItem ReadWordItem(SqliteDataReader reader)
        {
            return new StarDictWordItem
            {
                Id = reader.IsDBNull(0) ? -1 : reader.GetInt64(0),
                Word = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                StripWord = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Phonetic = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Definition = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Translation = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                Exchange = reader.IsDBNull(12) ? string.Empty : reader.GetString(12)
            };
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
                using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

                List<StarDictWordItem> results = [];
                using SqliteCommand selectCommand = new($"select * from stardict where word = $word", connection);
                selectCommand.Parameters.AddWithValue("$word", word);
                using SqliteDataReader query = await selectCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await query.ReadAsync(cancellationToken).ConfigureAwait(false) == true)
                {
                    results.Add(ReadWordItem(query));
                }
                return results;
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
                using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

                List<StarDictWordItem> results = [];
                using SqliteCommand selectCommand = new($"select * from stardict where sw >= $word order by sw, word collate nocase limit $limit", connection);
                selectCommand.Parameters.AddWithValue("$word", word);
                selectCommand.Parameters.AddWithValue("$limit", limit);
                using SqliteDataReader query = await selectCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await query.ReadAsync(cancellationToken).ConfigureAwait(false) == true)
                {
                    results.Add(ReadWordItem(query));
                }
                return results;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            return [];
        }

        /// <summary>
        /// 按 LIKE 模式匹配单词；定长模式额外加 length 过滤，避免全表扫描后排序
        /// </summary>
        /// <param name="pattern">已翻译好的 SQL LIKE 模式</param>
        /// <param name="exactLength">模式的定长约束，null 表示不限长度</param>
        /// <param name="limit">查询结果条数限制</param>
        /// <param name="cancellationToken"></param>
        public static async Task<List<StarDictWordItem>> PatternWord(string pattern, int? exactLength, int limit, CancellationToken cancellationToken = default)
        {
            try
            {
                using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

                List<StarDictWordItem> results = [];
                string sql = exactLength.HasValue
                    ? $"select * from stardict where word like $pattern and length(word) = $length order by word collate nocase limit $limit"
                    : $"select * from stardict where word like $pattern order by word collate nocase limit $limit";
                using SqliteCommand selectCommand = new(sql, connection);
                selectCommand.Parameters.AddWithValue("$pattern", pattern);
                if (exactLength.HasValue)
                {
                    selectCommand.Parameters.AddWithValue("$length", exactLength.Value);
                }
                selectCommand.Parameters.AddWithValue("$limit", limit);
                using SqliteDataReader query = await selectCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await query.ReadAsync(cancellationToken).ConfigureAwait(false) == true)
                {
                    results.Add(ReadWordItem(query));
                }
                return results;
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
                using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

                List<StarDictWordItem> results = [];
                using SqliteCommand selectCommand = tag != "oxford"
                    ? new SqliteCommand($"select * from stardict where tag like $tag order by word collate nocase", connection)
                    : new SqliteCommand($"select * from stardict where oxford = 1 order by word collate nocase", connection);

                if (tag != "oxford")
                {
                    if (!string.IsNullOrWhiteSpace(word))
                    {
                        selectCommand.CommandText = "select * from stardict where tag like $tag and word like $word order by word collate nocase";
                        selectCommand.Parameters.AddWithValue("$word", word + "%");
                    }
                    selectCommand.Parameters.AddWithValue("$tag", "%" + tag + "%");
                }
                else if (!string.IsNullOrWhiteSpace(word))
                {
                    selectCommand.CommandText = "select * from stardict where oxford = 1 and word like $word order by word collate nocase";
                    selectCommand.Parameters.AddWithValue("$word", word + "%");
                }

                using SqliteDataReader query = await selectCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await query.ReadAsync(cancellationToken).ConfigureAwait(false) == true)
                {
                    results.Add(ReadWordItem(query));
                }

                return results;
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
                using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

                List<StarDictWordItem> results = [];
                using SqliteCommand selectCommand = tag != "oxford"
                    ? new($"select * from stardict where tag like $tag and id > $id order by word collate nocase limit $limit", connection)
                    : new($"select * from stardict where oxford = 1 and id > $id order by word collate nocase limit $limit", connection);

                if (tag != "oxford")
                {
                    if (!string.IsNullOrWhiteSpace(word))
                    {
                        selectCommand.CommandText = "select * from stardict where tag like $tag and id > $id and word like $word order by word collate nocase limit $limit";
                        selectCommand.Parameters.AddWithValue("$word", word + "%");
                    }
                    selectCommand.Parameters.AddWithValue("$tag", "%" + tag + "%");
                }
                else if (!string.IsNullOrWhiteSpace(word))
                {
                    selectCommand.CommandText = "select * from stardict where oxford = 1 and id > $id and word like $word order by word collate nocase limit $limit";
                    selectCommand.Parameters.AddWithValue("$word", word + "%");
                }

                selectCommand.Parameters.AddWithValue("$id", startId);
                selectCommand.Parameters.AddWithValue("$limit", limit);

                using SqliteDataReader query = await selectCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await query.ReadAsync(cancellationToken).ConfigureAwait(false) == true)
                {
                    results.Add(ReadWordItem(query));
                }

                return results;
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
                using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

                using SqliteCommand selectCommand = tag != "oxford"
                    ? new SqliteCommand($"select count(*) from stardict where tag LIKE $tag;", connection)
                    : new SqliteCommand($"select count(*) from stardict where oxford = 1;", connection);

                if (tag != "oxford")
                {
                    selectCommand.Parameters.AddWithValue("$tag", "%" + tag + "%");
                }

                using SqliteDataReader query = await selectCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await query.ReadAsync(cancellationToken).ConfigureAwait(false) == true)
                {
                    return query.IsDBNull(0) ? -1 : query.GetInt32(0);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            return -1;
        }

        #endregion

    }
}
