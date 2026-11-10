using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Flint3.Core.Query;
using Flint3.Dict;
using Microsoft.Data.Sqlite;

namespace Flint3.Data.Mdx
{
    /// <summary>
    /// 以 sidecar 索引查词、从 mdx 读记录并转成纯文本的词库源
    /// </summary>
    public sealed class MdxSource : IWordSource, IDisposable
    {
        private readonly string _mdxPath;

        private readonly string _indexPath;

        private readonly object _lock = new();

        private MdxReader? _reader;

        public MdxSource(string name, string mdxPath, string indexPath)
        {
            Name = name;
            _mdxPath = mdxPath;
            _indexPath = indexPath;
        }

        public string Name { get; }

        public async Task<IReadOnlyList<WordEntry>> QueryAsync(string word, CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT word, record_offset FROM words WHERE word_lower = $lower ORDER BY word LIMIT 20;";
            List<WordRow> rows = await ReadRowsAsync(sql, command =>
            {
                command.Parameters.AddWithValue("$lower", word.ToLowerInvariant());
            }, cancellationToken).ConfigureAwait(false);

            return ToEntries(rows);
        }

        public async Task<IReadOnlyList<WordEntry>> MatchAsync(string word, int limit, CancellationToken cancellationToken = default)
        {
            // 前缀区间用 [lower, lower+U+FFFF) 表达，避免 LIKE 无法命中索引
            const string sql = "SELECT word, record_offset FROM words WHERE word_lower >= $lower AND word_lower < $upper ORDER BY word_lower, word LIMIT $limit;";
            string lower = word.ToLowerInvariant();
            List<WordRow> rows = await ReadRowsAsync(sql, command =>
            {
                command.Parameters.AddWithValue("$lower", lower);
                command.Parameters.AddWithValue("$upper", lower + "\uFFFF");
                command.Parameters.AddWithValue("$limit", limit);
            }, cancellationToken).ConfigureAwait(false);

            return ToEntries(rows);
        }

        public async Task<IReadOnlyList<WordEntry>> PatternAsync(string pattern, int limit, CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT word, record_offset FROM words WHERE word_lower LIKE $pattern ORDER BY word_lower, word LIMIT $limit;";
            List<WordRow> rows = await ReadRowsAsync(sql, command =>
            {
                command.Parameters.AddWithValue("$pattern", pattern.ToLowerInvariant());
                command.Parameters.AddWithValue("$limit", limit);
            }, cancellationToken).ConfigureAwait(false);

            return ToEntries(rows);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _reader?.Dispose();
                _reader = null;
            }
        }

        private async Task<List<WordRow>> ReadRowsAsync(
            string sql,
            Action<SqliteCommand> bind,
            CancellationToken cancellationToken)
        {
            List<WordRow> rows = [];
            SqliteConnectionStringBuilder connectionString = new()
            {
                DataSource = _indexPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            };

            await using SqliteConnection connection = new(connectionString.ToString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            bind(command);

            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new WordRow(reader.GetString(0), reader.GetInt64(1)));
            }

            return rows;
        }

        private IReadOnlyList<WordEntry> ToEntries(List<WordRow> rows)
        {
            List<WordEntry> entries = new(rows.Count);
            foreach (WordRow row in rows)
            {
                entries.Add(new WordEntry
                {
                    Word = row.Word,
                    // mdx 通常是单语词典，正文放 Translation 保证不依赖“英英释义”开关也能显示
                    Translation = ReadRecord(row.RecordOffset),
                    Source = Name,
                });
            }

            return entries;
        }

        private string ReadRecord(long offset)
        {
            try
            {
                MdxReader reader;
                lock (_lock)
                {
                    reader = _reader ??= MdxReader.Open(_mdxPath);
                }

                return MdxTextExtractor.Extract(reader.ReadRecord(offset));
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
                return string.Empty;
            }
        }

        private readonly record struct WordRow(string Word, long RecordOffset);
    }
}
