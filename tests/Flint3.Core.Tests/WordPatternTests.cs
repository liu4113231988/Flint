using Flint3.Core.Query;
using Xunit;

namespace Flint3.Core.Tests
{
    public class WordPatternTests
    {
        [Theory]
        [InlineData("?a?e", "_a_e", 4)]
        [InlineData("_a_e", "_a_e", 4)]
        [InlineData("a?ple", "a_ple", 5)]
        [InlineData("apple", "apple", 5)]
        [InlineData("^apple$", "apple", 5)]
        public void TryTranslate_整词模式带定长约束(string input, string like, int length)
        {
            Assert.True(WordPattern.TryTranslate(input, out WordPattern pattern));

            Assert.Equal(like, pattern.Like);
            Assert.Equal(length, pattern.ExactLength);
        }

        [Theory]
        [InlineData("^app", "app%")]
        [InlineData("ing$", "%ing")]
        [InlineData("app*", "app%")]
        [InlineData("*ing", "%ing")]
        [InlineData("a*p?e", "a%p_e")]
        public void TryTranslate_锚点与星号不限定长度(string input, string like)
        {
            Assert.True(WordPattern.TryTranslate(input, out WordPattern pattern));

            Assert.Equal(like, pattern.Like);
            Assert.Null(pattern.ExactLength);
            Assert.False(pattern.IsLiteral);
        }

        [Theory]
        [InlineData("apple", "apple")]
        [InlineData("^apple$", "apple")]
        [InlineData("APPLE", "APPLE")]
        public void TryTranslate_纯字母模式标记为字面量(string input, string literal)
        {
            Assert.True(WordPattern.TryTranslate(input, out WordPattern pattern));

            Assert.True(pattern.IsLiteral);
            Assert.Equal(literal, pattern.Literal);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("^")]
        [InlineData("$")]
        [InlineData("^$")]
        [InlineData("app-le")]
        [InlineData("app le")]
        [InlineData("中文")]
        public void TryTranslate_非法输入返回false(string input)
        {
            Assert.False(WordPattern.TryTranslate(input, out _));
        }

        [Fact]
        public void TryTranslate_去掉首尾空白()
        {
            Assert.True(WordPattern.TryTranslate("  ?a?e  ", out WordPattern pattern));

            Assert.Equal("_a_e", pattern.Like);
        }
    }
}
