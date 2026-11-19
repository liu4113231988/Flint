using Flint3.Core.Input;
using Xunit;

namespace Flint3.Core.Tests
{
    public class SelectionGestureDetectorTests
    {
        private const uint Move = 0x0200;
        private const uint LDown = 0x0201;
        private const uint LUp = 0x0202;

        [Fact]
        public void ClickWithoutDrag_NoGesture()
        {
            var d = new SelectionGestureDetector();

            Assert.False(d.Process(LDown, 100, 100));
            Assert.False(d.Process(LUp, 100, 100));
        }

        [Fact]
        public void TinyJitterBelowThreshold_NoGesture()
        {
            var d = new SelectionGestureDetector();

            d.Process(LDown, 100, 100);
            d.Process(Move, 103, 104); // 距离 5 < 阈值 6
            Assert.False(d.Process(LUp, 103, 104));
        }

        [Fact]
        public void DragBeyondThreshold_Gesture()
        {
            var d = new SelectionGestureDetector();

            d.Process(LDown, 100, 100);
            d.Process(Move, 140, 100);
            Assert.True(d.Process(LUp, 140, 100));
        }

        [Fact]
        public void DiagonalDrag_Gesture()
        {
            var d = new SelectionGestureDetector();

            d.Process(LDown, 0, 0);
            d.Process(Move, 5, 5); // 平方和 50 >= 36
            Assert.True(d.Process(LUp, 5, 5));
        }

        [Fact]
        public void MoveWithoutDown_NoGesture()
        {
            var d = new SelectionGestureDetector();

            d.Process(Move, 200, 200);
            Assert.False(d.Process(LUp, 200, 200));
        }

        [Fact]
        public void StateResetsBetweenGestures()
        {
            var d = new SelectionGestureDetector();

            d.Process(LDown, 0, 0);
            d.Process(Move, 50, 0);
            Assert.True(d.Process(LUp, 50, 0));

            // 第二次：纯单击不应触发（拖动标记已复位）
            d.Process(LDown, 10, 10);
            Assert.False(d.Process(LUp, 10, 10));

            // 第三次：再次拖动应触发
            d.Process(LDown, 20, 20);
            d.Process(Move, 80, 20);
            Assert.True(d.Process(LUp, 80, 20));
        }

        [Fact]
        public void RightButtonMessages_Ignored()
        {
            var d = new SelectionGestureDetector();

            d.Process(0x0204, 0, 0); // RBUTTONDOWN
            d.Process(Move, 50, 0);
            Assert.False(d.Process(0x0205, 50, 0)); // RBUTTONUP
        }
    }
}