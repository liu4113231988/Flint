using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flint3.Data;
using Flint3.Data.Csf;
using Flint3.Data.Models;
using Xunit;

namespace Flint3.Data.Tests
{
    /// <summary>
    /// 用同一份 fixture 库派生 CSF 文件，逐方法与 SQLite 后端对拍，确保切换后端不改变任何语义
    /// </summary>
    [Collection("StarDict")]
    public class CsfBackendEquivalenceTests : IDisposable
    {
        private readonly string _csfPath;

        public CsfBackendEquivalenceTests(StarDictDatabaseFixture fixture)
        {
            _csfPath = Path.Combine(Path.GetDirectoryName(fixture.DatabasePath)!, "stardict.csf");
            CsfWriter.Build(fixture.DatabasePath, _csfPath);
        }

        public void Dispose()
        {
            // 先切回 SQLite 再删文件，避免后续测试类的 Auto 探测命中残留 CSF
            StarDictDataAccess.SetBackend(StarDictBackendKind.Sqlite);
            StarDictDataAccess.SetBackend(StarDictBackendKind.Auto);

            try
            {
                File.Delete(_csfPath);
            }
            catch (Exception)
            {
                // 临时文件清理失败不影响测试结论
            }
        }

        private static async Task<T> Run<T>(StarDictBackendKind kind, Func<Task<T>> query)
        {
            StarDictDataAccess.SetBackend(kind);
            return await query();
        }

        private static string Describe(StarDictWordItem item)
        {
            return $"{item.Id}|{item.Word}|{item.StripWord}|{item.Phonetic}|{item.Definition}|{item.Translation}|{item.Exchange}";
        }

        private static async Task AssertSameAsync(Func<Task<List<StarDictWordItem>>> query)
        {
            List<string> expected = (await Run(StarDictBackendKind.Sqlite, query)).Select(Describe).ToList();
            List<string> actual = (await Run(StarDictBackendKind.Csf, query)).Select(Describe).ToList();

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("apple")]
        [InlineData("APPLE")]
        [InlineData("ApPlE")]
        [InlineData("app")]
        [InlineData("appl")]
        [InlineData("banana")]
        [InlineData("bananas")]
        [InlineData("zzz")]
        [InlineData("")]
        public async Task QueryWord_与SQLite一致(string word)
        {
            await AssertSameAsync(() => StarDictDataAccess.QueryWord(word));
        }

        [Theory]
        [InlineData("app", 10)]
        [InlineData("APP", 10)]
        [InlineData("app", 2)]
        [InlineData("appl", 10)]
        [InlineData("banana", 3)]
        [InlineData("zzz", 10)]
        [InlineData("", 10)]
        [InlineData("app", 0)]
        public async Task MatchWord_与SQLite一致(string word, int limit)
        {
            await AssertSameAsync(() => StarDictDataAccess.MatchWord(word, limit));
        }

        [Theory]
        [InlineData("app%", null, 100)]
        [InlineData("APP%", null, 100)]
        [InlineData("app%", 3, 100)]
        [InlineData("a%", null, 2)]
        [InlineData("%pl%", null, 100)]
        [InlineData("%pl%", 5, 100)]
        [InlineData("a_p%", null, 100)]
        [InlineData("a_p%", 5, 100)]
        [InlineData("ap_", null, 100)]
        [InlineData("banana", null, 100)]
        [InlineData("banana", 6, 100)]
        [InlineData("banana", 5, 100)]
        [InlineData("%e", null, 100)]
        [InlineData("%", null, 100)]
        [InlineData("zzz%", null, 100)]
        public async Task PatternWord_与SQLite一致(string pattern, int? exactLength, int limit)
        {
            await AssertSameAsync(() => StarDictDataAccess.PatternWord(pattern, exactLength, limit));
        }

        public static TheoryData<string, string> GlossaryCases => new()
        {
            { "gk", "" },
            { "gk", "appl" },
            { "gk", "APP" },
            { "gk", "z" },
            { "zk", "" },
            { "zk", "app" },
            { "cet4", "" },
            { "nope", "" },
            { "", "" },
            { "", "app" },
            { "oxford", "" },
            { "oxford", "appl" },
            { "oxford", "BANANA" },
            { "oxford", "z" },
        };

        [Theory]
        [MemberData(nameof(GlossaryCases))]
        public async Task GetAllExtraGlossaryWords_与SQLite一致(string tag, string word)
        {
            await AssertSameAsync(() => StarDictDataAccess.GetAllExtraGlossaryWords(tag, word, CancellationToken.None));
        }

        public static TheoryData<string, long, int, string> IncrementalCases => new()
        {
            { "gk", 0, 10, "" },
            { "gk", 1, 10, "" },
            { "gk", 2, 10, "" },
            { "gk", 0, 2, "" },
            { "gk", 1, 2, "appl" },
            { "zk", 0, 10, "" },
            { "zk", 4, 10, "" },
            { "cet4", 0, 1, "" },
            { "", 3, 10, "" },
            { "oxford", 0, 10, "" },
            { "oxford", 1, 10, "" },
            { "oxford", 0, 2, "app" },
            { "oxford", 3, 10, "" },
        };

        [Theory]
        [MemberData(nameof(IncrementalCases))]
        public async Task GetIncrementalExtraGlossaryWords_与SQLite一致(string tag, long startId, int limit, string word)
        {
            await AssertSameAsync(() => StarDictDataAccess.GetIncrementalExtraGlossaryWords(tag, startId, limit, word, CancellationToken.None));
        }

        [Theory]
        [InlineData("gk")]
        [InlineData("zk")]
        [InlineData("cet4")]
        [InlineData("nope")]
        [InlineData("")]
        [InlineData("oxford")]
        public async Task GetExtraGlossaryWordsCount_与SQLite一致(string tag)
        {
            int expected = await Run(StarDictBackendKind.Sqlite, () => StarDictDataAccess.GetExtraGlossaryWordsCount(tag));
            int actual = await Run(StarDictBackendKind.Csf, () => StarDictDataAccess.GetExtraGlossaryWordsCount(tag));

            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task Auto_探测到同目录CSF时使用CSF后端()
        {
            // 移走 db：若 Auto 未选中 CSF，SQLite 会打开失败并返回空
            string databasePath = _csfPath[..^"stardict.csf".Length] + "stardict.db";
            string moved = databasePath + ".bak";
            File.Move(databasePath, moved);
            try
            {
                StarDictDataAccess.SetBackend(StarDictBackendKind.Auto);

                List<StarDictWordItem> results = await StarDictDataAccess.QueryWord("APPLE");

                StarDictWordItem item = Assert.Single(results);
                Assert.Equal("apple", item.Word);
                Assert.Equal("n. 苹果", item.Translation);
            }
            finally
            {
                File.Move(moved, databasePath);
            }
        }

        [Fact]
        public async Task Auto_CSF缺失时回退SQLite()
        {
            string moved = _csfPath + ".bak";
            File.Move(_csfPath, moved);
            try
            {
                StarDictDataAccess.SetBackend(StarDictBackendKind.Auto);

                List<StarDictWordItem> results = await StarDictDataAccess.QueryWord("banana");

                Assert.Single(results);
            }
            finally
            {
                File.Move(moved, _csfPath);
            }
        }
    }
}
