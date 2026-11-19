using System.Collections.Generic;
using Flint3.Core.Capture;
using Xunit;

namespace Flint3.Core.Tests
{
    public class ScreenWordHitTestTests
    {
        private static readonly IReadOnlyList<CapturedWord> Words = new List<CapturedWord>
        {
            new("hello", 0, 0, 50, 20),
            new("world", 60, 0, 50, 20),
            new("below", 0, 40, 50, 20),
        };

        [Fact]
        public void PointInsideWord_ReturnsThatWord()
        {
            var hit = ScreenWordHitTest.FindNearestWord(Words, 70, 10, 30);
            Assert.Equal("world", hit?.Text);
        }

        [Fact]
        public void PointBetweenWords_ReturnsNearest()
        {
            // (55,10) 距 hello 右边缘 5px，距 world 左边缘 5px，平局取先出现者
            var hit = ScreenWordHitTest.FindNearestWord(Words, 55, 10, 30);
            Assert.Equal("hello", hit?.Text);

            var hit2 = ScreenWordHitTest.FindNearestWord(Words, 57, 10, 30);
            Assert.Equal("world", hit2?.Text);
        }

        [Fact]
        public void PointBeyondMaxDistance_ReturnsNull()
        {
            Assert.Null(ScreenWordHitTest.FindNearestWord(Words, 300, 300, 30));
        }

        [Fact]
        public void EmptyList_ReturnsNull()
        {
            Assert.Null(ScreenWordHitTest.FindNearestWord(new List<CapturedWord>(), 0, 0, 30));
        }

        [Fact]
        public void DiagonalDistance_UsesRectEdgeNotCenter()
        {
            // (52,25)：距 hello 右下角 (50,20) 为 sqrt(4+25)≈5.4，距 below 上边缘 y=40 为 15
            var hit = ScreenWordHitTest.FindNearestWord(Words, 52, 25, 10);
            Assert.Equal("hello", hit?.Text);
        }
    }
}