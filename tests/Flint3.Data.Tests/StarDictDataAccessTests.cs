using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Flint3.Data.Tests
{
    /// <summary>
    /// 以 fixture 库锁定 StarDictDataAccess 的现有查询行为，作为后续重构的回归护栏
    /// </summary>
    [Collection("StarDict")]
    public class StarDictDataAccessTests
    {
        [Fact]
        public async Task QueryWord_精确命中返回单条()
        {
            var results = await StarDictDataAccess.QueryWord("apple");

            Assert.Single(results);
            Assert.Equal("apple", results[0].Word);
            Assert.Equal("apple", results[0].StripWord);
            Assert.Equal("n. 苹果", results[0].Translation);
        }

        [Fact]
        public async Task QueryWord_大小写不敏感且未命中返回空()
        {
            Assert.Single(await StarDictDataAccess.QueryWord("APPLE"));
            Assert.Empty(await StarDictDataAccess.QueryWord("nothing-here"));
        }

        [Fact]
        public async Task QueryWord_返回原始_exchange_字符串()
        {
            var item = Assert.Single(await StarDictDataAccess.QueryWord("apple"));

            Assert.Equal("p:apples/d:appled/i:appling/3:apples/s:apples", item.Exchange);
        }

        [Fact]
        public async Task MatchWord_从定位点向后返回并按_sw_排序()
        {
            var words = (await StarDictDataAccess.MatchWord("app", 10)).Select(x => x.Word);

            // 现有行为：sw >= 输入词即返回（不止前缀），这是"输入即查"的定位语义
            Assert.Equal(new[] { "app", "apple", "application", "apply", "banana" }, words);
        }

        [Fact]
        public async Task MatchWord_遵守_limit()
        {
            var words = (await StarDictDataAccess.MatchWord("app", 2)).Select(x => x.Word);

            Assert.Equal(new[] { "app", "apple" }, words);
        }

        [Fact]
        public async Task MatchWord_输入词之后无数据时返回空()
        {
            Assert.Empty(await StarDictDataAccess.MatchWord("zzz", 10));
        }

        [Fact]
        public async Task GetAllExtraGlossaryWords_按_tag_子串过滤()
        {
            var words = (await StarDictDataAccess.GetAllExtraGlossaryWords("gk", "", CancellationToken.None)).Select(x => x.Word);

            Assert.Equal(new[] { "apple", "application", "apply" }, words);
        }

        [Fact]
        public async Task GetAllExtraGlossaryWords_tag_为_oxford_时按牛津标记过滤()
        {
            var words = (await StarDictDataAccess.GetAllExtraGlossaryWords("oxford", "", CancellationToken.None)).Select(x => x.Word);

            Assert.Equal(new[] { "app", "apple", "application" }, words);
        }

        [Fact]
        public async Task GetAllExtraGlossaryWords_带前缀词过滤()
        {
            var words = (await StarDictDataAccess.GetAllExtraGlossaryWords("gk", "appl", CancellationToken.None)).Select(x => x.Word);

            Assert.Equal(new[] { "apple", "application", "apply" }, words);
        }

        [Fact]
        public async Task GetExtraGlossaryWordsCount_两种标签均正确()
        {
            Assert.Equal(3, await StarDictDataAccess.GetExtraGlossaryWordsCount("gk"));
            Assert.Equal(3, await StarDictDataAccess.GetExtraGlossaryWordsCount("oxford"));
        }

        [Fact]
        public async Task GetIncrementalExtraGlossaryWords_按_id_向后翻页()
        {
            var words = (await StarDictDataAccess.GetIncrementalExtraGlossaryWords("gk", 1, 10, "", CancellationToken.None)).Select(x => x.Word);
            Assert.Equal(new[] { "application", "apply" }, words);

            var oxfordWords = (await StarDictDataAccess.GetIncrementalExtraGlossaryWords("oxford", 1, 10, "", CancellationToken.None)).Select(x => x.Word);
            Assert.Equal(new[] { "app", "application" }, oxfordWords);
        }

        [Fact]
        public async Task 查询会自动等待数据库初始化()
        {
            // 模拟"启动即输入"：关闭连接后直接查询，查询方法内部应自行完成初始化
            StarDictDataAccess.CloseDatabase();

            var results = await StarDictDataAccess.QueryWord("banana");

            Assert.Single(results);
            Assert.Equal("n. 香蕉", results[0].Translation);
        }
    }
}
