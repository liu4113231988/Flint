using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Flint3.Core.Query;
using Flint3.Data;
using Flint3.Data.Chinese;
using Flint3.Data.Sources;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Flint3.Data.Tests
{
    /// <summary>
    /// 中文反查：排序契约固定期望、前缀归并去重、结果上限与索引回退。
    /// 期望里的数字都是构造库里的记录 id，改排序键会直接让这些用例失败。
    /// </summary>
    [Collection("StarDict")]
    public class ChineseLookupTests
    {
        private static readonly (string Word, int Collins, long? Frq, string Translation)[] Rows =
        [
            ("appel", 5, 100, "n. 苹果"),
            ("apple", 5, 100, "n. 苹果"),
            ("apples", 5, 100, "n. 苹果"),
            ("pome", 5, 101, "n. 苹果"),
            ("crabapple", 0, 0, "n. 苹果"),
            ("pomaceous", 0, null, "n. 苹果, 苹果酱"),
            ("applesauce", 1, null, "n. 苹果酱"),
            ("application", 5, 200, "n. 应用, 应用程序"),
            ("apply", 4, 150, "v. 应用, 申请"),
            ("app", 3, 400, "n. 应用程序"),
            ("banana", 2, 500, "n. 香蕉"),
        ];

        private readonly StarDictDatabaseFixture _fixture;

        public ChineseLookupTests(StarDictDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void 精确匹配按星级词频词长字母序排列()
        {
            string directory = NewDirectory();
            try
            {
                using ChineseIndexReader reader = BuildIndex(directory);
                Assert.Equal(ChineseIndexStatus.Ready, reader.Status);

                // appel/apple/apples 同为 5 星、frq 100，按词长再按字母序；
                // pome 是 5 星但 frq 101 排后；crabapple 与 pomaceous 同为 0 星、frq 缺失且词长相同，按字母序
                Assert.Equal([1, 2, 3, 4, 5, 6], reader.LookupExact("苹果", 10));
                Assert.Equal([1, 2, 3], reader.LookupExact("苹果", 3));
            }
            finally
            {
                Cleanup(directory);
            }
        }

        [Fact]
        public void 前缀命中排在精确组之后且不重复()
        {
            string directory = NewDirectory();
            try
            {
                using ChineseIndexReader reader = BuildIndex(directory);

                // 精确词条"应用"两位在前，其后才是只命中"应用程序"的 app
                Assert.Equal([8, 9, 10], reader.Lookup("应用", 10));
                Assert.Equal([8, 9], reader.Lookup("应用", 2));
                Assert.Equal([11], reader.Lookup("香", 10));

                // pomaceous 同时含"苹果"和"苹果酱"，单字前缀查询只能出现一次；
                // applesauce（1 星）在无星的 crabapple/pomaceous 之前
                List<int> prefix = reader.Lookup("苹", 10);
                Assert.Equal([1, 2, 3, 4, 7, 5, 6], prefix);
                Assert.Equal(prefix.Count, prefix.Distinct().Count());
            }
            finally
            {
                Cleanup(directory);
            }
        }

        [Fact]
        public void 非纯中文输入不进入反查()
        {
            string directory = NewDirectory();
            try
            {
                using ChineseIndexReader reader = BuildIndex(directory);

                Assert.Empty(reader.Lookup("apple", 10));
                Assert.Empty(reader.Lookup("苹果apple", 10));
                Assert.Empty(reader.Lookup("苹果苹果苹果苹果苹", 10));
                Assert.Empty(reader.LookupExact("苹", 10));
            }
            finally
            {
                Cleanup(directory);
            }
        }

        [Fact]
        public void 词条抽取会去掉括号与词性并跳过停用词()
        {
            string[] terms = ChineseTermExtractor.Extract("n. 苹果, 苹果树（植物）[网络] 水果").ToArray();

            Assert.Contains("苹果", terms);
            Assert.Contains("苹果树", terms);
            Assert.Contains("水果", terms);
            Assert.DoesNotContain("植物", terms);
            Assert.DoesNotContain("网络", terms);
        }

        [Fact]
        public void 索引缺失损坏或版本不符时返回状态而不抛异常()
        {
            string directory = NewDirectory();
            try
            {
                string index = Path.Combine(directory, "stardict.zhx");

                using (ChineseIndexReader missing = ChineseIndexReader.Open(index))
                {
                    Assert.Equal(ChineseIndexStatus.Missing, missing.Status);
                    Assert.Empty(missing.Lookup("苹果", 10));
                }

                File.WriteAllText(index, "not an index");
                using (ChineseIndexReader corrupt = ChineseIndexReader.Open(index))
                {
                    Assert.Equal(ChineseIndexStatus.Corrupt, corrupt.Status);
                    Assert.Empty(corrupt.Lookup("苹果", 10));
                }

                using ChineseIndexReader built = BuildIndex(directory);
                Assert.Equal(ChineseIndexStatus.Ready, built.Status);
                built.Dispose();

                using (FileStream stream = new(index, FileMode.Open, FileAccess.Write))
                {
                    stream.Seek(ChineseIndexFormatHeaderOffset, SeekOrigin.Begin);
                    stream.WriteByte(99);
                    stream.WriteByte(0);
                }

                using ChineseIndexReader stale = ChineseIndexReader.Open(index);
                Assert.Equal(ChineseIndexStatus.VersionMismatch, stale.Status);
                Assert.Empty(stale.Lookup("苹果", 10));
            }
            finally
            {
                Cleanup(directory);
            }
        }

        [Fact]
        public async Task 中文查询经_EcdictSource_与_WordQueryService_返回英文词()
        {
            string directory = NewDirectory();
            try
            {
                string databasePath = CreateDatabase(directory, Rows);
                ChineseIndexBuilder.Build(databasePath, Path.Combine(directory, "stardict.zhx"));

                StarDictDataAccess.CloseDatabase();
                await StarDictDataAccess.InitializeDatabase(databasePath);

                EcdictSource source = new();
                Assert.Equal(
                    ["application", "apply"],
                    (await source.MatchAsync("应用", 2)).Select(entry => entry.Word));
                Assert.Equal(
                    ["appel", "apple", "apples", "pome", "crabapple", "pomaceous"],
                    (await source.QueryAsync("苹果")).Select(entry => entry.Word));

                WordQueryService service = new(new IWordSource[] { source });
                Assert.Equal(
                    ["application", "apply", "app"],
                    (await service.MatchAsync("应用", 10)).Select(entry => entry.Word));

                // 英文查词沿用原路径
                Assert.Contains(await source.MatchAsync("appl", 10), entry => entry.Word == "apple");
            }
            finally
            {
                StarDictDataAccess.CloseDatabase();
                await StarDictDataAccess.InitializeDatabase(_fixture.DatabasePath);
                Cleanup(directory);
            }
        }

        [Fact]
        public async Task 索引缺失时英文仍可用且中文返回空()
        {
            string directory = NewDirectory();
            try
            {
                string databasePath = CreateDatabase(directory, Rows);

                StarDictDataAccess.CloseDatabase();
                await StarDictDataAccess.InitializeDatabase(databasePath);

                EcdictSource source = new();
                Assert.Empty(await source.MatchAsync("苹果", 10));
                Assert.Equal(ChineseIndexStatus.Missing, StarDictDataAccess.ChineseLookupStatus);
                Assert.NotEmpty(StarDictDataAccess.ChineseLookupHint);
                Assert.Contains(await source.MatchAsync("appl", 10), entry => entry.Word == "apple");
            }
            finally
            {
                StarDictDataAccess.CloseDatabase();
                await StarDictDataAccess.InitializeDatabase(_fixture.DatabasePath);
                Cleanup(directory);
            }
        }

        [Fact]
        public async Task 索引与词库记录数不一致时按不匹配降级()
        {
            string indexDirectory = NewDirectory();
            string databaseDirectory = NewDirectory();
            try
            {
                string smallDatabase = CreateDatabase(indexDirectory, Rows.Take(2).ToArray());
                ChineseIndexBuilder.Build(smallDatabase, Path.Combine(indexDirectory, "stardict.zhx"));

                string databasePath = CreateDatabase(databaseDirectory, Rows);
                File.Copy(
                    Path.Combine(indexDirectory, "stardict.zhx"),
                    Path.Combine(databaseDirectory, "stardict.zhx"));

                StarDictDataAccess.CloseDatabase();
                await StarDictDataAccess.InitializeDatabase(databasePath);

                EcdictSource source = new();
                Assert.Empty(await source.MatchAsync("苹果", 10));
                Assert.Equal(ChineseIndexStatus.SourceMismatch, StarDictDataAccess.ChineseLookupStatus);
                Assert.Contains(await source.MatchAsync("appl", 10), entry => entry.Word == "apple");
            }
            finally
            {
                StarDictDataAccess.CloseDatabase();
                await StarDictDataAccess.InitializeDatabase(_fixture.DatabasePath);
                Cleanup(indexDirectory);
                Cleanup(databaseDirectory);
            }
        }

        private const int ChineseIndexFormatHeaderOffset = 4;

        private static ChineseIndexReader BuildIndex(string directory)
        {
            string databasePath = CreateDatabase(directory, Rows);
            string indexPath = Path.Combine(directory, "stardict.zhx");
            ChineseIndexBuilder.Build(databasePath, indexPath);
            return ChineseIndexReader.Open(indexPath);
        }

        private static string CreateDatabase(string directory, (string Word, int Collins, long? Frq, string Translation)[] rows)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "stardict.db");

            using SqliteConnection connection = new($"Filename={path}");
            connection.Open();
            using (SqliteCommand create = connection.CreateCommand())
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
                    "audio TEXT);";
                create.ExecuteNonQuery();
            }

            foreach ((string word, int collins, long? frq, string translation) in rows)
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText =
                    "INSERT INTO stardict (word, sw, phonetic, definition, translation, pos, collins, oxford, tag, bnc, frq, exchange) " +
                    "VALUES ($word, $word, '', '', $translation, '', $collins, 0, '', NULL, $frq, '')";
                insert.Parameters.AddWithValue("$word", word);
                insert.Parameters.AddWithValue("$translation", translation);
                insert.Parameters.AddWithValue("$collins", collins);
                insert.Parameters.AddWithValue("$frq", (object?)frq ?? DBNull.Value);
                insert.ExecuteNonQuery();
            }

            return path;
        }

        private static string NewDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), $"flint3-zh-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void Cleanup(string directory)
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception)
            {
                // 临时目录清理失败不影响测试结论
            }
        }
    }
}
