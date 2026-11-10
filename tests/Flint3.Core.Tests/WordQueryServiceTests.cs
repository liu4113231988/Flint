using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flint3.Core.Query;
using Xunit;

namespace Flint3.Core.Tests
{
    public class WordQueryServiceTests
    {
        [Fact]
        public async Task MatchAsync_排序契约_精确优先于前缀_前缀优先于其他()
        {
            var source = new FakeWordSource("Ecdict", "appendix", "apple", "app", "banana");
            var service = new WordQueryService(new IWordSource[] { source });

            var words = (await service.MatchAsync("app", 10)).Select(x => x.Word);

            Assert.Equal(new[] { "app", "appendix", "apple", "banana" }, words);
        }

        [Fact]
        public async Task MatchAsync_排序契约_前缀必须排在其他之前()
        {
            // 真实库中存在 sw 排序会把非前缀词（如 "a PP"）排在前缀词之前的情况
            var source = new FakeWordSource("Ecdict", "a PP", "APP", "app.");
            var service = new WordQueryService(new IWordSource[] { source });

            var words = (await service.MatchAsync("app", 10)).Select(x => x.Word);

            Assert.Equal(new[] { "APP", "app.", "a PP" }, words);
        }

        [Fact]
        public async Task MatchAsync_多源去重保留先出现的源()
        {
            var first = new FakeWordSource("First", new WordEntry { Word = "apple", Translation = "first" });
            var second = new FakeWordSource("Second", new WordEntry { Word = "Apple", Translation = "second" });
            var service = new WordQueryService(new IWordSource[] { first, second });

            var result = await service.MatchAsync("app", 10);

            var entry = Assert.Single(result);
            Assert.Equal("First", entry.Source);
            Assert.Equal("first", entry.Translation);
        }

        [Fact]
        public async Task MatchAsync_补齐来源标记()
        {
            var service = new WordQueryService(new IWordSource[] { new FakeWordSource("Ecdict", "apple") });

            var entry = Assert.Single(await service.MatchAsync("app", 10));

            Assert.Equal("Ecdict", entry.Source);
        }

        [Fact]
        public async Task MatchAsync_把_limit_透传给源()
        {
            var source = new FakeWordSource("Ecdict", "apple");
            var service = new WordQueryService(new IWordSource[] { source });

            await service.MatchAsync("app", 5);

            Assert.Equal(5, source.LastLimit);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task MatchAsync_空输入不查询源(string word)
        {
            var source = new FakeWordSource("Ecdict", "apple");
            var service = new WordQueryService(new IWordSource[] { source });

            Assert.Empty(await service.MatchAsync(word, 10));
            Assert.Equal(0, source.MatchCalls);
        }

        [Fact]
        public async Task QueryAsync_精确查询也做来源标记()
        {
            var service = new WordQueryService(new IWordSource[] { new FakeWordSource("Ecdict", "apple") });

            var entry = Assert.Single(await service.QueryAsync("apple"));

            Assert.Equal("Ecdict", entry.Source);
        }

        [Fact]
        public async Task PatternAsync_保持源顺序()
        {
            var service = new WordQueryService(new IWordSource[] { new FakeWordSource("Ecdict", "banana", "apple") });

            var words = (await service.PatternAsync("a%", 10)).Select(x => x.Word);

            Assert.Equal(new[] { "banana", "apple" }, words);
        }

        [Fact]
        public async Task MatchAsync_取消令牌透传并抛出取消()
        {
            var source = new FakeWordSource("Ecdict", "apple") { ThrowOnCancel = true };
            var service = new WordQueryService(new IWordSource[] { source });

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.MatchAsync("app", 10, cts.Token));
        }

        [Fact]
        public async Task MatchAsync_单个源抛异常不影响其它源()
        {
            var service = new WordQueryService(new IWordSource[] { new ThrowingSource(), new FakeWordSource("Ecdict", "apple") });

            var words = (await service.MatchAsync("app", 10)).Select(x => x.Word);

            Assert.Equal(new[] { "apple" }, words);
        }

        [Fact]
        public async Task PatternAsync_跨源去重保留先出现的源()
        {
            var first = new FakeWordSource("First", new WordEntry { Word = "cake", Translation = "first" });
            var second = new FakeWordSource("Second", new WordEntry { Word = "CAKE", Translation = "second" });
            var service = new WordQueryService(new IWordSource[] { first, second });

            var result = await service.PatternAsync("?a?e", 10);

            var entry = Assert.Single(result);
            Assert.Equal("First", entry.Source);
        }

        [Fact]
        public async Task PatternAsync_字面量模式沿用精确优先排序()
        {
            var source = new FakeWordSource("Ecdict", "appendix", "apple");
            var service = new WordQueryService(new IWordSource[] { source });

            var words = (await service.PatternAsync("^apple$", 10)).Select(x => x.Word);

            Assert.Equal(new[] { "apple", "appendix" }, words);
        }

        [Fact]
        public async Task PatternAsync_通配符模式保持源顺序()
        {
            var source = new FakeWordSource("Ecdict", "banana", "apple");
            var service = new WordQueryService(new IWordSource[] { source });

            var words = (await service.PatternAsync("?a?e", 10)).Select(x => x.Word);

            Assert.Equal(new[] { "banana", "apple" }, words);
        }

        private sealed class ThrowingSource : IWordSource
        {
            public string Name => "Broken";

            public Task<IReadOnlyList<WordEntry>> QueryAsync(string word, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("source failed");
            }

            public Task<IReadOnlyList<WordEntry>> MatchAsync(string word, int limit, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("source failed");
            }

            public Task<IReadOnlyList<WordEntry>> PatternAsync(string pattern, int limit, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("source failed");
            }
        }

        private sealed class FakeWordSource : IWordSource
        {
            private readonly IReadOnlyList<WordEntry> _entries;

            public FakeWordSource(string name, params string[] words)
                : this(name, words.Select(word => new WordEntry { Word = word }).ToArray())
            {
            }

            public FakeWordSource(string name, params WordEntry[] entries)
            {
                Name = name;
                _entries = entries;
            }

            public string Name { get; }

            public int LastLimit { get; private set; }

            public int MatchCalls { get; private set; }

            public bool ThrowOnCancel { get; init; }

            public Task<IReadOnlyList<WordEntry>> QueryAsync(string word, CancellationToken cancellationToken = default)
            {
                return Respond(cancellationToken);
            }

            public Task<IReadOnlyList<WordEntry>> MatchAsync(string word, int limit, CancellationToken cancellationToken = default)
            {
                MatchCalls++;
                LastLimit = limit;
                return Respond(cancellationToken);
            }

            public Task<IReadOnlyList<WordEntry>> PatternAsync(string pattern, int limit, CancellationToken cancellationToken = default)
            {
                return Respond(cancellationToken);
            }

            private Task<IReadOnlyList<WordEntry>> Respond(CancellationToken cancellationToken)
            {
                if (ThrowOnCancel)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return Task.FromResult(_entries);
            }
        }
    }
}
