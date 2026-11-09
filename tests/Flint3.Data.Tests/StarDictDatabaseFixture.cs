using System;
using System.IO;
using Flint3.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Flint3.Data.Tests
{
    /// <summary>
    /// 用临时目录里的小型 fixture 库替代真实的 stardict.db，保证测试不依赖数百 MB 的发布数据
    /// </summary>
    public sealed class StarDictDatabaseFixture : IDisposable
    {
        public string DatabasePath { get; }

        public StarDictDatabaseFixture()
        {
            DatabasePath = Path.Combine(Path.GetTempPath(), $"flint3-tests-{Guid.NewGuid():N}", "stardict.db");
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

            CreateDatabase();

            StarDictDataAccess.CloseDatabase();
            StarDictDataAccess.InitializeDatabase(DatabasePath).GetAwaiter().GetResult();
        }

        private void CreateDatabase()
        {
            using var connection = new SqliteConnection($"Filename={DatabasePath}");
            connection.Open();

            using (var create = connection.CreateCommand())
            {
                create.CommandText =
                    "CREATE TABLE stardict (" +
                    "id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL UNIQUE," +
                    "word VARCHAR(64) COLLATE NOCASE NOT NULL UNIQUE," +
                    "sw VARCHAR(64) COLLATE NOCASE NOT NULL," +
                    "phonetic VARCHAR(64)," +
                    "definition TEXT," +
                    "translation TEXT," +
                    "pos VARCHAR(16)," +
                    "collins INTEGER DEFAULT(0)," +
                    "oxford INTEGER DEFAULT(0)," +
                    "tag VARCHAR(64)," +
                    "bnc INTEGER DEFAULT(NULL)," +
                    "frq INTEGER DEFAULT(NULL)," +
                    "exchange TEXT," +
                    "detail TEXT," +
                    "audio TEXT);" +
                    "CREATE INDEX stardict_3 ON stardict(sw, word collate nocase);";
                create.ExecuteNonQuery();
            }

            // 5 条固定数据：sw 排序后依次为 app < apple < application < apply < banana
            Insert(connection, "apple", "apple", "'æpl", "n. fruit", "n. 苹果", "n", 5, 1, "zk gk", 100, 200,
                "p:apples/d:appled/i:appling/3:apples/s:apples");
            Insert(connection, "application", "application", ",æpli'keiʃn", "n. request", "n. 申请", "n", 5, 1, "gk", 150, 300,
                "s:applications");
            Insert(connection, "apply", "apply", "ə'plai", "v. use", "v. 应用", "v", 4, 0, "gk cet4", 80, 120,
                "p:applied/d:applied/i:applying/3:applies");
            Insert(connection, "banana", "banana", "bə'nɑ:nə", "n. fruit", "n. 香蕉", "n", 2, 0, "ky", 400, 500,
                "s:bananas");
            Insert(connection, "app", "app", "æp", "n. program", "n. 应用程序", "n", 3, 1, "zk", 300, 100, "");
        }

        private static void Insert(SqliteConnection connection, string word, string sw, string phonetic,
            string definition, string translation, string pos, int collins, int oxford, string tag,
            int bnc, int frq, string exchange)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText =
                "INSERT INTO stardict (word, sw, phonetic, definition, translation, pos, collins, oxford, tag, bnc, frq, exchange) " +
                "VALUES ($word, $sw, $phonetic, $definition, $translation, $pos, $collins, $oxford, $tag, $bnc, $frq, $exchange)";
            insert.Parameters.AddWithValue("$word", word);
            insert.Parameters.AddWithValue("$sw", sw);
            insert.Parameters.AddWithValue("$phonetic", phonetic);
            insert.Parameters.AddWithValue("$definition", definition);
            insert.Parameters.AddWithValue("$translation", translation);
            insert.Parameters.AddWithValue("$pos", pos);
            insert.Parameters.AddWithValue("$collins", collins);
            insert.Parameters.AddWithValue("$oxford", oxford);
            insert.Parameters.AddWithValue("$tag", tag);
            insert.Parameters.AddWithValue("$bnc", bnc);
            insert.Parameters.AddWithValue("$frq", frq);
            insert.Parameters.AddWithValue("$exchange", exchange);
            insert.ExecuteNonQuery();
        }

        public void Dispose()
        {
            StarDictDataAccess.CloseDatabase();

            try
            {
                Directory.Delete(Path.GetDirectoryName(DatabasePath)!, true);
            }
            catch (Exception)
            {
                // 临时目录清理失败不影响测试结论
            }
        }
    }

    [CollectionDefinition("StarDict", DisableParallelization = true)]
    public sealed class StarDictCollection : ICollectionFixture<StarDictDatabaseFixture>
    {
    }
}
