using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Flint3.Dict;
using Microsoft.Data.Sqlite;

namespace Flint3.Data.Mdx
{
    /// <summary>
    /// 为 mdx 词库生成 sidecar SQLite 索引，查询时不再顺序扫描 mdx
    /// </summary>
    public static class MdxIndexDatabase
    {
        public const string WordsTable = "words";

        public static long Build(
            string mdxPath,
            string indexPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (File.Exists(indexPath))
            {
                File.Delete(indexPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);

            try
            {
                using MdxReader reader = MdxReader.Open(mdxPath);
                SqliteConnectionStringBuilder connectionString = new()
                {
                    DataSource = indexPath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false,
                };

                using SqliteConnection connection = new(connectionString.ToString());
                connection.Open();

                using (SqliteCommand setup = connection.CreateCommand())
                {
                    setup.CommandText = "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF;";
                    setup.ExecuteNonQuery();
                }

                using (SqliteCommand create = connection.CreateCommand())
                {
                    create.CommandText = $"CREATE TABLE {WordsTable} (word TEXT NOT NULL, word_lower TEXT NOT NULL, record_offset INTEGER NOT NULL, record_size INTEGER NOT NULL);";
                    create.ExecuteNonQuery();
                }

                long count = 0;
                using (SqliteTransaction transaction = connection.BeginTransaction())
                using (SqliteCommand insert = connection.CreateCommand())
                {
                    insert.Transaction = transaction;
                    insert.CommandText = $"INSERT INTO {WordsTable} (word, word_lower, record_offset, record_size) VALUES ($word, $lower, $offset, $size);";
                    SqliteParameter wordParameter = insert.Parameters.Add("$word", SqliteType.Text);
                    SqliteParameter lowerParameter = insert.Parameters.Add("$lower", SqliteType.Text);
                    SqliteParameter offsetParameter = insert.Parameters.Add("$offset", SqliteType.Integer);
                    SqliteParameter sizeParameter = insert.Parameters.Add("$size", SqliteType.Integer);

                    foreach (MdxEntry entry in reader.EnumerateEntries(cancellationToken))
                    {
                        if (entry.Word.Length == 0)
                        {
                            continue;
                        }

                        wordParameter.Value = entry.Word;
                        lowerParameter.Value = entry.Word.ToLowerInvariant();
                        offsetParameter.Value = entry.RecordOffset;
                        sizeParameter.Value = entry.RecordSize;
                        insert.ExecuteNonQuery();

                        count++;
                        if (count % 5000 == 0)
                        {
                            progress?.Report(reader.EntryCount > 0 ? (double)count / reader.EntryCount : 0);
                        }
                    }

                    transaction.Commit();
                }

                using (SqliteCommand index = connection.CreateCommand())
                {
                    index.CommandText = $"CREATE INDEX idx_{WordsTable}_lower ON {WordsTable} (word_lower);";
                    index.ExecuteNonQuery();
                }

                progress?.Report(1);
                return count;
            }
            catch
            {
                if (File.Exists(indexPath))
                {
                    File.Delete(indexPath);
                }

                throw;
            }
        }
    }
}
