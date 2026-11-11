using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Flint3.Data.Models;
using Microsoft.Data.Sqlite;

namespace Flint3.Data.StarDict
{
    /// <summary>
    /// 原有 SQLite 实现，保留为可回退路径
    /// </summary>
    internal sealed class SqliteStarDictBackend : IStarDictBackend
    {
        private readonly string _databasePath;

        public SqliteStarDictBackend(string databasePath)
        {
            _databasePath = databasePath;
        }

        /// <summary>
        /// 每个查询使用独立连接（由 Microsoft.Data.Sqlite 连接池复用），避免共享连接并发冲突
        /// </summary>
        private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
        {
            SqliteConnection connection = new($"Filename={_databasePath}");
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
                Exchange = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
            };
        }

        public async Task<List<StarDictWordItem>> QueryWord(string word, CancellationToken cancellationToken)
        {
            using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            List<StarDictWordItem> results = [];
            using SqliteCommand selectCommand = new("select * from stardict where word = $word", connection);
            selectCommand.Parameters.AddWithValue("$word", word);
            using SqliteDataReader query = await selectCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await query.ReadAsync(cancellationToken).ConfigureAwait(false) == true)
            {
                results.Add(ReadWordItem(query));
            }

            return results;
        }

        public async Task<List<StarDictWordItem>> MatchWord(string word, int limit, CancellationToken cancellationToken)
        {
            using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            List<StarDictWordItem> results = [];
            using SqliteCommand selectCommand = new(
                "select * from stardict where sw >= $word order by sw, word collate nocase limit $limit",
                connection);
            selectCommand.Parameters.AddWithValue("$word", word);
            selectCommand.Parameters.AddWithValue("$limit", limit);
            using SqliteDataReader query = await selectCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await query.ReadAsync(cancellationToken).ConfigureAwait(false) == true)
            {
                results.Add(ReadWordItem(query));
            }

            return results;
        }

        public async Task<List<StarDictWordItem>> PatternWord(string pattern, int? exactLength, int limit, CancellationToken cancellationToken)
        {
            using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            List<StarDictWordItem> results = [];
            string sql = exactLength.HasValue
                ? "select * from stardict where word like $pattern and length(word) = $length order by word collate nocase limit $limit"
                : "select * from stardict where word like $pattern order by word collate nocase limit $limit";
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

        public async Task<List<StarDictWordItem>> GetAllExtraGlossaryWords(string tag, string word, CancellationToken cancellationToken)
        {
            using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            List<StarDictWordItem> results = [];
            using SqliteCommand selectCommand = tag != "oxford"
                ? new SqliteCommand("select * from stardict where tag like $tag order by word collate nocase", connection)
                : new SqliteCommand("select * from stardict where oxford = 1 order by word collate nocase", connection);

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

        public async Task<List<StarDictWordItem>> GetIncrementalExtraGlossaryWords(string tag, long startId, int limit, string word, CancellationToken cancellationToken)
        {
            using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            List<StarDictWordItem> results = [];
            using SqliteCommand selectCommand = tag != "oxford"
                ? new SqliteCommand("select * from stardict where tag like $tag and id > $id order by word collate nocase limit $limit", connection)
                : new SqliteCommand("select * from stardict where oxford = 1 and id > $id order by word collate nocase limit $limit", connection);

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

        public async Task<int> GetExtraGlossaryWordsCount(string tag, CancellationToken cancellationToken)
        {
            using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            using SqliteCommand selectCommand = tag != "oxford"
                ? new SqliteCommand("select count(*) from stardict where tag LIKE $tag;", connection)
                : new SqliteCommand("select count(*) from stardict where oxford = 1;", connection);

            if (tag != "oxford")
            {
                selectCommand.Parameters.AddWithValue("$tag", "%" + tag + "%");
            }

            using SqliteDataReader query = await selectCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await query.ReadAsync(cancellationToken).ConfigureAwait(false) == true)
            {
                return query.IsDBNull(0) ? -1 : query.GetInt32(0);
            }

            return -1;
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
        }
    }
}
