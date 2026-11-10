using System;
using System.Diagnostics;
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

        /// <summary>
        /// 索引结构版本；写入 SQLite 的 user_version，用于识别旧索引并触发自动重建
        /// </summary>
        public const int SchemaVersion = 2;

        public static long Build(
            string mdxPath,
            string indexPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            string tempPath = indexPath + ".tmp";
            TryDelete(tempPath);

            try
            {
                long count = BuildCore(mdxPath, tempPath, progress, cancellationToken);
                Publish(tempPath, indexPath);
                return count;
            }
            catch
            {
                // 只清理半成品，保留调用方原有的索引文件，便于重建失败时继续用旧索引
                TryDelete(tempPath);
                throw;
            }
        }

        /// <summary>
        /// 索引文件是否为当前结构版本；文件缺失、不是 SQLite 或版本不符都视为需要重建
        /// </summary>
        public static bool IsCurrent(string indexPath) => GetVersion(indexPath) == SchemaVersion;

        public static int GetVersion(string indexPath)
        {
            try
            {
                using SqliteConnection connection = new($"Data Source={indexPath};Mode=ReadOnly;Pooling=False");
                connection.Open();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version;";
                return Convert.ToInt32(command.ExecuteScalar());
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
                return -1;
            }
        }

        private static long BuildCore(
            string mdxPath,
            string indexPath,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
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
                // WITHOUT ROWID + 复合主键把“表”和“按小写词头查找”合并成一棵 B 树，避免同一词头存三份
                create.CommandText =
                    $"CREATE TABLE {WordsTable} (word TEXT NOT NULL, word_lower TEXT NOT NULL, record_offset INTEGER NOT NULL, " +
                    $"PRIMARY KEY (word_lower, word, record_offset)) WITHOUT ROWID;";
                create.ExecuteNonQuery();
            }

            long count = 0;
            using (SqliteTransaction transaction = connection.BeginTransaction())
            using (SqliteCommand insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = $"INSERT INTO {WordsTable} (word, word_lower, record_offset) VALUES ($word, $lower, $offset);";
                SqliteParameter wordParameter = insert.Parameters.Add("$word", SqliteType.Text);
                SqliteParameter lowerParameter = insert.Parameters.Add("$lower", SqliteType.Text);
                SqliteParameter offsetParameter = insert.Parameters.Add("$offset", SqliteType.Integer);

                foreach (MdxEntry entry in reader.EnumerateEntries(cancellationToken))
                {
                    if (entry.Word.Length == 0)
                    {
                        continue;
                    }

                    wordParameter.Value = entry.Word;
                    lowerParameter.Value = entry.Word.ToLowerInvariant();
                    offsetParameter.Value = entry.RecordOffset;
                    insert.ExecuteNonQuery();

                    count++;
                    if (count % 5000 == 0)
                    {
                        progress?.Report(reader.EntryCount > 0 ? (double)count / reader.EntryCount : 0);
                    }
                }

                transaction.Commit();
            }

            using (SqliteCommand version = connection.CreateCommand())
            {
                version.CommandText = $"PRAGMA user_version={SchemaVersion};";
                version.ExecuteNonQuery();
            }

            progress?.Report(1);
            return count;
        }

        /// <summary>
        /// 原子替换目标索引；正在查询的连接可能短暂占用文件，重试几次再放弃
        /// </summary>
        private static void Publish(string tempPath, string indexPath)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(tempPath, indexPath, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 3)
                {
                    Thread.Sleep(100);
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
            }
        }
    }
}
