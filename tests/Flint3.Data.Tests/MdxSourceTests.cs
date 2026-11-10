using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flint3.Core.Query;
using Flint3.Data.Mdx;
using Flint3.Dict;
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
    }
}
