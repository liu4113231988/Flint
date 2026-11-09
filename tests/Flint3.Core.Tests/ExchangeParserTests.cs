using System.Linq;
using Flint3.Core.Query;
using Xunit;

namespace Flint3.Core.Tests
{
    public class ExchangeParserTests
    {
        [Fact]
        public void Parse_按原有顺序映射全部标签()
        {
            var result = ExchangeParser.Parse("p:apples/d:appled/i:appling/3:apples/r:applier/t:appliest/s:apples/0:app");

            Assert.Equal(new[]
            {
                "过去式:apples",
                "过去分词:appled",
                "现在分词:appling",
                "第三人称单数:apples",
                "比较级:applier",
                "最高级:appliest",
                "复数:apples",
                "原型:app"
            }, result.Select(x => $"{x.Type}:{x.Word}"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Parse_空输入返回空集合(string? exchange)
        {
            Assert.Empty(ExchangeParser.Parse(exchange));
        }

        [Fact]
        public void Parse_忽略未知标签与空值()
        {
            var result = ExchangeParser.Parse("1:unknown/x:value/p:/s:apples");

            var exchange = Assert.Single(result);
            Assert.Equal("复数", exchange.Type);
            Assert.Equal("apples", exchange.Word);
        }
    }
}
