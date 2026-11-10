using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Flint3.Dict;
using Xunit;

namespace Flint3.Dict.Tests
{
    /// <summary>
    /// 样本由 mdict 格式参考实现 writemdict 生成，覆盖 UTF-8/UTF-16、zlib/无压缩与各类不支持的变体
    /// </summary>
    public class MdxReaderTests
    {
        private static string Fixture(string name)
        {
            return Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        }

        [Theory]
        [InlineData("basic.mdx")]
        [InlineData("no_compression.mdx")]
        [InlineData("utf16.mdx")]
        public void 能读取中英词条与记录(string fileName)
        {
            using MdxReader reader = MdxReader.Open(Fixture(fileName));

            Assert.Equal(3, reader.EntryCount);
            List<MdxEntry> entries = reader.EnumerateEntries().ToList();
            Assert.Equal(new[] { "alpha", "beta", "gamma" }, entries.Select(e => e.Word));
            Assert.All(entries, e => Assert.True(e.RecordSize > 0));

            Assert.Equal("<i>alpha</i>", reader.ReadRecord(entries[0].RecordOffset));
            Assert.Equal("Letter <b>beta</b>", reader.ReadRecord(entries[1].RecordOffset));
            Assert.Equal("Capital version is Γ &lt;", reader.ReadRecord(entries[2].RecordOffset));
        }

        [Fact]
        public void 能读取UTF16的BMP外字符()
        {
            using MdxReader reader = MdxReader.Open(Fixture("utf16nonbmp.mdx"));

            MdxEntry entry = Assert.Single(reader.EnumerateEntries());
            Assert.Equal("𩷶", entry.Word);
            Assert.Equal("A fish", reader.ReadRecord(entry.RecordOffset));
        }

        [Fact]
        public void 记录偏移越界时返回空串()
        {
            using MdxReader reader = MdxReader.Open(Fixture("basic.mdx"));

            Assert.Equal("", reader.ReadRecord(-1));
            Assert.Equal("", reader.ReadRecord(reader.TotalRecordLength));
        }

        [Theory]
        [InlineData("encrypted_internal_regcode.mdx", "加密")]
        [InlineData("key_index_encryption.mdx", "加密")]
        [InlineData("gbk.mdx", "编码")]
        [InlineData("version12.mdx", "版本")]
        public void 不支持的变体给出明确提示(string fileName, string expectedFragment)
        {
            MdxUnsupportedException exception = Assert.Throws<MdxUnsupportedException>(() => MdxReader.Open(Fixture(fileName)));
            Assert.Contains(expectedFragment, exception.Message);
        }
    }

    public class MdxTextExtractorTests
    {
        [Fact]
        public void 去掉标签保留文本()
        {
            Assert.Equal("alpha", MdxTextExtractor.Extract("<i>alpha</i>"));
            Assert.Equal("Letter beta", MdxTextExtractor.Extract("Letter <b>beta</b>"));
            Assert.Equal("Capital version is Γ <", MdxTextExtractor.Extract("Capital version is Γ &lt;"));
        }

        [Fact]
        public void 跳过脚本样式并把块级标签转成换行()
        {
            string html = "<style>p{color:red}</style><p>first</p><script>alert(1)</script><div>second<br/>line</div>";
            Assert.Equal("first\n\nsecond\nline", MdxTextExtractor.Extract(html));
        }

        [Fact]
        public void 空内容返回空串()
        {
            Assert.Equal("", MdxTextExtractor.Extract(null));
            Assert.Equal("", MdxTextExtractor.Extract(""));
        }
    }
}
