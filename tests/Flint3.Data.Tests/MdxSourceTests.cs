using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flint3.Core.Query;
using Flint3.Data.Mdx;
using Flint3.Dict;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Flint3.Data.Tests
{
    /// <summary>
    /// 用 writemdict 生成的样本 mdx 验证导入、索引与查询链路
    /// </summary>
    public sealed class MdxSourceTests : IDisposable
    {
        private readonly string _baseDirectory;

        public MdxSourceTests()
        {
            _baseDirectory = Path.Combine(Path.GetTempPath(), "flint-mdx-tests", Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            if (Directory.Exists(_baseDirectory))
            {
                Directory.Delete(_baseDirectory, recursive: true);
            }
        }

        private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

        [Fact]
        public async Task 导入后能按精确与前缀查询并返回纯文本记录()
        {
            MdxDictionaryStore store = new(_baseDirectory);
            MdxDictionaryInfo info = await store.ImportAsync(Fixture("basic.mdx"));

            Assert.Equal("Basic dictionary", info.Name);
            Assert.Equal(3, info.EntryCount);
            Assert.True(File.Exists(store.GetDictionaryPath(info)));
            Assert.True(File.Exists(store.GetIndexPath(info)));

            MdxSource source = Assert.IsType<MdxSource>(Assert.Single(store.CreateSources()));

            using (source)
            {
                WordEntry exact = Assert.Single(await source.QueryAsync("BETA"));
                Assert.Equal("beta", exact.Word);
                Assert.Equal("Letter beta", exact.Translation);

                WordEntry prefix = Assert.Single(await source.MatchAsync("al", 10));
                Assert.Equal("alpha", prefix.Word);
                Assert.Equal("alpha", prefix.Translation);

                WordEntry gamma = Assert.Single(await source.MatchAsync("g", 10));
                Assert.Equal("Capital version is Γ <", gamma.Translation);
            }
        }

        [Fact]
        public async Task 重复导入同一文件复用已有索引()
        {
            MdxDictionaryStore store = new(_baseDirectory);
            MdxDictionaryInfo first = await store.ImportAsync(Fixture("basic.mdx"));
            MdxDictionaryInfo second = await store.ImportAsync(Fixture("basic.mdx"));

            Assert.Equal(first.Id, second.Id);
            Assert.Single(store.Load().Dictionaries);
        }

        [Fact]
        public async Task 导入前已取消时不留文件与登记()
        {
            MdxDictionaryStore store = new(_baseDirectory);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => store.ImportAsync(Fixture("basic.mdx"), cancellationToken: cancellation.Token));

            Assert.Empty(store.Load().Dictionaries);
            Assert.False(Directory.Exists(store.DictionariesDirectory) && Directory.EnumerateFiles(store.DictionariesDirectory).Any());
        }

        [Fact]
        public async Task 导入过程中取消会清理半成品()
        {
            MdxDictionaryStore store = new(_baseDirectory);
            using CancellationTokenSource cancellation = new();
            CancelOnFirstProgress progress = new(cancellation);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => store.ImportAsync(Fixture("basic.mdx"), progress, cancellation.Token));

            Assert.Empty(store.Load().Dictionaries);
            Assert.False(Directory.Exists(store.DictionariesDirectory) && Directory.EnumerateFiles(store.DictionariesDirectory).Any());
        }

        [Fact]
        public async Task 导入加密词库时报明确错误且不留下文件()
        {
            MdxDictionaryStore store = new(_baseDirectory);

            MdxUnsupportedException exception = await Assert.ThrowsAsync<MdxUnsupportedException>(
                () => store.ImportAsync(Fixture("encrypted_internal_regcode.mdx")));

            Assert.Contains("加密", exception.Message);
            Assert.Empty(store.Load().Dictionaries);
            Assert.False(Directory.Exists(store.DictionariesDirectory) && Directory.EnumerateFiles(store.DictionariesDirectory).Any());
        }

        [Fact]
        public async Task 删除词库同时清掉副本索引与登记()
        {
            MdxDictionaryStore store = new(_baseDirectory);
            MdxDictionaryInfo info = await store.ImportAsync(Fixture("basic.mdx"));

            store.Remove(info);

            Assert.False(File.Exists(store.GetDictionaryPath(info)));
            Assert.False(File.Exists(store.GetIndexPath(info)));
            Assert.Empty(store.Load().Dictionaries);
        }

        [Fact]
        public async Task 多源查询仍保持精确大于前缀大于其他的排序契约()
        {
            MdxDictionaryStore store = new(_baseDirectory);
            await store.ImportAsync(Fixture("basic.mdx"));

            List<IWordSource> sources = [new EcdictLikeSource("alphabet", "alpine")];
            sources.AddRange(store.CreateSources());
            using WordQueryService service = new(sources);

            string[] words = (await service.MatchAsync("alpha", 10)).Select(entry => entry.Word).ToArray();

            Assert.Equal(new[] { "alpha", "alphabet", "alpine" }, words);
        }

        [Fact]
        public async Task 索引使用精简结构并写入版本号()
        {
            MdxDictionaryStore store = new(_baseDirectory);
            MdxDictionaryInfo info = await store.ImportAsync(Fixture("basic.mdx"));
            string indexPath = store.GetIndexPath(info);

            Assert.True(MdxIndexDatabase.IsCurrent(indexPath));
            Assert.Equal(MdxIndexDatabase.SchemaVersion, MdxIndexDatabase.GetVersion(indexPath));

            using SqliteConnection connection = new($"Data Source={indexPath};Mode=ReadOnly;Pooling=False");
            connection.Open();

            using SqliteCommand schema = connection.CreateCommand();
            schema.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'words';";
            Assert.Contains("WITHOUT ROWID", Assert.IsType<string>(schema.ExecuteScalar()));

            using SqliteCommand columns = connection.CreateCommand();
            columns.CommandText = "SELECT name FROM pragma_table_info('words') ORDER BY cid;";
            List<string> names = [];
            using (SqliteDataReader reader = columns.ExecuteReader())
            {
                while (reader.Read())
                {
                    names.Add(reader.GetString(0));
                }
            }
            Assert.Equal(new[] { "word", "word_lower", "record_offset" }, names);

            using SqliteCommand index = connection.CreateCommand();
            index.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'index' AND name = 'idx_words_lower';";
            Assert.Equal(0L, Convert.ToInt64(index.ExecuteScalar()));
        }

        [Fact]
        public async Task 旧版本索引在重建后结构升级且查询结果不变()
        {
            MdxDictionaryStore store = new(_baseDirectory);
            MdxDictionaryInfo info = await store.ImportAsync(Fixture("basic.mdx"));
            string indexPath = store.GetIndexPath(info);

            SetVersion(indexPath, 1);
            Assert.False(MdxIndexDatabase.IsCurrent(indexPath));

            await store.RebuildIndexAsync(info);

            Assert.True(MdxIndexDatabase.IsCurrent(indexPath));
            MdxSource source = Assert.IsType<MdxSource>(Assert.Single(store.CreateSources()));
            using (source)
            {
                WordEntry exact = Assert.Single(await source.QueryAsync("BETA"));
                Assert.Equal("beta", exact.Word);
                Assert.Equal("Letter beta", exact.Translation);
            }
        }

        [Fact]
        public async Task 打开词库时后台自动重建旧索引()
        {
            MdxDictionaryStore store = new(_baseDirectory);
            MdxDictionaryInfo info = await store.ImportAsync(Fixture("basic.mdx"));
            string indexPath = store.GetIndexPath(info);
            SetVersion(indexPath, 0);

            Assert.Single(store.CreateSources());

            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (!MdxIndexDatabase.IsCurrent(indexPath) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.True(MdxIndexDatabase.IsCurrent(indexPath));
        }

        [Fact]
        public async Task 重建失败时保留旧索引文件()
        {
            MdxDictionaryStore store = new(_baseDirectory);
            MdxDictionaryInfo info = await store.ImportAsync(Fixture("basic.mdx"));
            string indexPath = store.GetIndexPath(info);
            string mdxPath = store.GetDictionaryPath(info);
            SetVersion(indexPath, 0);
            File.Delete(mdxPath);

            await Assert.ThrowsAsync<FileNotFoundException>(() => store.RebuildIndexAsync(info));

            Assert.True(File.Exists(indexPath));
            Assert.Equal(0, MdxIndexDatabase.GetVersion(indexPath));
        }

        [Fact]
        public async Task 同一词头的多条记录都能查出来()
        {
            MdxDictionaryStore store = new(_baseDirectory);
            MdxDictionaryInfo info = await store.ImportAsync(Fixture("basic.mdx"));
            string indexPath = store.GetIndexPath(info);

            long offset;
            using (SqliteConnection connection = new($"Data Source={indexPath};Mode=ReadWrite;Pooling=False"))
            {
                connection.Open();
                using SqliteCommand select = connection.CreateCommand();
                select.CommandText = "SELECT record_offset FROM words WHERE word_lower = 'beta';";
                offset = Convert.ToInt64(select.ExecuteScalar());

                using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO words (word, word_lower, record_offset) VALUES ('beta', 'beta', $offset);";
                insert.Parameters.AddWithValue("$offset", offset + 1);
                insert.ExecuteNonQuery();
            }

            MdxSource source = Assert.IsType<MdxSource>(Assert.Single(store.CreateSources()));
            using (source)
            {
                Assert.Equal(2, (await source.QueryAsync("beta")).Count);
            }
        }

        private static void SetVersion(string indexPath, int version)
        {
            using SqliteConnection connection = new($"Data Source={indexPath};Mode=ReadWrite;Pooling=False");
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version={version};";
            command.ExecuteNonQuery();
        }

        private sealed class EcdictLikeSource(params string[] words) : IWordSource
        {
            public string Name => "Ecdict";

            public Task<IReadOnlyList<WordEntry>> QueryAsync(string word, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<WordEntry>>(words.Select(w => new WordEntry { Word = w }).ToArray());

            public Task<IReadOnlyList<WordEntry>> MatchAsync(string word, int limit, CancellationToken cancellationToken = default)
                => QueryAsync(word, cancellationToken);

            public Task<IReadOnlyList<WordEntry>> PatternAsync(string pattern, int limit, CancellationToken cancellationToken = default)
                => QueryAsync(pattern, cancellationToken);
        }

        /// <summary>
        /// 首次进度回调就取消，用于验证导入中途取消的清理路径
        /// </summary>
        private sealed class CancelOnFirstProgress(CancellationTokenSource cancellation) : IProgress<double>
        {
            public void Report(double value) => cancellation.Cancel();
        }
    }
}
