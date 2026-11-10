using System.Linq;
using System.Threading.Tasks;
using Flint3.Data.Sources;
using Xunit;

namespace Flint3.Data.Tests
{
    /// <summary>
    /// 通配符查词在真实 ECDICT 库上的行为（定长、锚点、上限）
    /// </summary>
    [Collection("StarDict")]
    public class PatternWordTests
    {
        [Fact]
        public async Task PatternWord_问号定长只返回匹配长度的词()
        {
            var words = await StarDictDataAccess.PatternWord("_p_l_", 5, 50);

            Assert.NotEmpty(words);
            Assert.All(words, word =>
            {
                string lower = word.Word.ToLowerInvariant();
                Assert.Equal(5, lower.Length);
                Assert.Equal('p', lower[1]);
                Assert.Equal('l', lower[3]);
            });
        }

        [Fact]
        public async Task PatternWord_星号不限长度()
        {
            var words = await StarDictDataAccess.PatternWord("app%", null, 20);

            Assert.NotEmpty(words);
            Assert.All(words, word => Assert.StartsWith("app", word.Word, System.StringComparison.OrdinalIgnoreCase));
            Assert.Contains(words, word => word.Word.Length > 5);
        }

        [Fact]
        public async Task PatternWord_后缀匹配()
        {
            var words = await StarDictDataAccess.PatternWord("%tion", null, 50);

            Assert.NotEmpty(words);
            Assert.All(words, word => Assert.EndsWith("tion", word.Word, System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task PatternWord_尊重条数上限()
        {
            var words = await StarDictDataAccess.PatternWord("%a%", null, 3);

            Assert.Equal(3, words.Count);
        }

        [Fact]
        public async Task EcdictSource_PatternAsync_走同一套语法()
        {
            // 语法翻译在源内部完成，保证 mdx 与 ECDICT 行为一致
            var source = new EcdictSource();
            var words = await source.PatternAsync("?p?l?", 20);

            Assert.NotEmpty(words);
            Assert.All(words, entry =>
            {
                string lower = entry.Word.ToLowerInvariant();
                Assert.Equal(5, lower.Length);
                Assert.Equal('p', lower[1]);
                Assert.Equal('l', lower[3]);
            });
        }
    }
}
