using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Flint3.Core.Query;
using Flint3.Data;
using Flint3.Data.Sources;
using Xunit;

namespace Flint3.Data.Tests
{
    /// <summary>
    /// 50 条查询语料的黄金输出（固定期望）。
    /// 排序契约：精确命中 &gt; 前缀命中 &gt; 其他，同组保持数据源顺序。
    /// 该契约在 P2 由 WordQueryService 统一定义，允许与重构前“纯 SQL 顺序”不同。
    /// </summary>
    [Collection("StarDict")]
    public class WordQueryGoldenTests
    {
        private const int Limit = 10;

        [Theory]
        [InlineData("app", "app|apple|application|apply|banana")]
        [InlineData("APP", "app|apple|application|apply|banana")]
        [InlineData("App", "app|apple|application|apply|banana")]
        [InlineData("aPp", "app|apple|application|apply|banana")]
        [InlineData("appl", "apple|application|apply|banana")]
        [InlineData("Appl", "apple|application|apply|banana")]
        [InlineData("apple", "apple|application|apply|banana")]
        [InlineData("APPLE", "apple|application|apply|banana")]
        [InlineData("Apple", "apple|application|apply|banana")]
        [InlineData("apples", "application|apply|banana")]
        [InlineData("Apples", "application|apply|banana")]
        [InlineData("appleS", "application|apply|banana")]
        [InlineData("apply", "apply|banana")]
        [InlineData("Apply", "apply|banana")]
        [InlineData("applied", "apply|banana")]
        [InlineData("applying", "banana")]
        [InlineData("applies", "apply|banana")]
        [InlineData("application", "application|apply|banana")]
        [InlineData("Application", "application|apply|banana")]
        [InlineData("applications", "apply|banana")]
        [InlineData("appli", "application|apply|banana")]
        [InlineData("appliC", "application|apply|banana")]
        [InlineData("applic", "application|apply|banana")]
        [InlineData("applica", "application|apply|banana")]
        [InlineData("applicat", "application|apply|banana")]
        [InlineData("applicati", "application|apply|banana")]
        [InlineData("applicatio", "application|apply|banana")]
        [InlineData("applesx", "application|apply|banana")]
        [InlineData("ban", "banana")]
        [InlineData("BAN", "banana")]
        [InlineData("banana", "banana")]
        [InlineData("BANANA", "banana")]
        [InlineData("Banana", "banana")]
        [InlineData("bananas", "")]
        [InlineData("Bananas", "")]
        [InlineData("b", "banana")]
        [InlineData("B", "banana")]
        [InlineData("a", "app|apple|application|apply|banana")]
        [InlineData("A", "app|apple|application|apply|banana")]
        [InlineData("ap", "app|apple|application|apply|banana")]
        [InlineData("AP", "app|apple|application|apply|banana")]
        [InlineData("aP", "app|apple|application|apply|banana")]
        [InlineData("zzz", "")]
        [InlineData("ZZZ", "")]
        [InlineData("zz", "")]
        [InlineData("bana", "banana")]
        [InlineData("banan", "banana")]
        [InlineData("bananaa", "")]
        [InlineData("nothing", "")]
        [InlineData("0", "app|apple|application|apply|banana")]
        public async Task 查询输出符合排序契约(string query, string expected)
        {
            var service = new WordQueryService(new IWordSource[] { new EcdictSource() });

            List<string> actual = (await service.MatchAsync(query, Limit)).Select(x => x.Word).ToList();
            string[] want = expected.Length == 0 ? [] : expected.Split('|');

            Assert.Equal(want, actual);
        }

        [Fact]
        public async Task 结果都带_Ecdict_来源标记()
        {
            var service = new WordQueryService(new IWordSource[] { new EcdictSource() });

            var result = await service.MatchAsync("app", Limit);

            Assert.NotEmpty(result);
            Assert.All(result, entry => Assert.Equal("Ecdict", entry.Source));
        }
    }
}
