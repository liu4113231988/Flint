using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Flint3.Core.Wordle;
using Xunit;

namespace Flint3.Core.Tests
{
    public class WordleWordListTests
    {
        [Fact]
        public void Load_读取嵌入词表并做允许校验()
        {
            WordleWordList list = WordleWordList.Default;

            Assert.True(list.Answers.Length > 3000);
            Assert.True(list.Allowed.Length > 40000);
            Assert.All(list.Answers, word => Assert.Equal(5, word.Length));
            Assert.All(list.Allowed, word => Assert.Equal(5, word.Length));
            Assert.Contains("apple", list.Allowed);
            Assert.Contains("which", list.Answers);
            Assert.True(list.IsAllowed("which"));

            // 出题词必须来自允许词表
            HashSet<string> allowed = new(list.Allowed);
            Assert.All(list.Answers, word => Assert.Contains(word, allowed));
            Assert.False(list.IsAllowed(""));
        }

        [Fact]
        public void Load_从流解析()
        {
            const string json = "{\"answers\":[\"apple\"],\"allowed\":[\"apple\",\"brick\"]}";
            using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));

            WordleWordList list = WordleWordList.Load(stream);

            Assert.True(list.IsAllowed("brick"));
            Assert.False(list.IsAllowed("crane"));
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"answers\":[],\"allowed\":[]}")]
        public void Load_空词表报错(string json)
        {
            using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));

            Assert.ThrowsAny<Exception>(() => WordleWordList.Load(stream));
        }

        [Fact]
        public void Load_词表序列化后仍可读取()
        {
            WordleWordList original = new()
            {
                Answers = ["apple"],
                Allowed = ["apple", "brick"],
            };

            string json = JsonSerializer.Serialize(original, Serialization.FlintJsonContext.Default.WordleWordList);
            using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));

            WordleWordList loaded = WordleWordList.Load(stream);

            Assert.Equal(original.Answers, loaded.Answers);
            Assert.Equal(original.Allowed, loaded.Allowed);
        }
    }
}
