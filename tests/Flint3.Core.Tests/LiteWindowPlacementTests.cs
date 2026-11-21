using System.Drawing;
using Flint3.Core.Capture;
using Xunit;

namespace Flint3.Core.Tests
{
    public class LiteWindowPlacementTests
    {
        [Theory]
        [InlineData(-1040, -780)]
        [InlineData(1120, 1380)]
        public void Place_UsesWorkAreaOriginForVerticallyArrangedDisplays(int workTop, int expectedTop)
        {
            Rectangle workArea = new(0, workTop, 1920, 1040);

            Rectangle bounds = LiteWindowPlacement.Place(workArea, 96, expanded: false, cursor: null);

            Assert.Equal(new Rectangle(690, expectedTop, 540, 64), bounds);
        }

        [Fact]
        public void Place_FlipsAtTheEdgesOfANegativeOriginHighDpiDisplay()
        {
            Rectangle workArea = new(-1920, -1080, 1920, 1040);

            Rectangle bounds = LiteWindowPlacement.Place(workArea, 144, expanded: true, cursor: new Point(-30, -65));

            Assert.Equal(new Rectangle(-864, -668, 810, 579), bounds);
        }

        [Fact]
        public void Place_FitsAWindowLargerThanTheWorkArea()
        {
            Rectangle workArea = new(100, 100, 380, 500);

            Rectangle bounds = LiteWindowPlacement.Place(workArea, 192, expanded: true, cursor: new Point(102, 105));

            Assert.Equal(workArea, bounds);
        }

        [Fact]
        public void Place_UsesStandardDpiWhenTheMonitorDpiIsUnavailable()
        {
            Rectangle workArea = new(0, 0, 1920, 1040);

            Rectangle bounds = LiteWindowPlacement.Place(workArea, 0, expanded: false, cursor: new Point(10, 10));

            Assert.Equal(new Rectangle(26, 26, 540, 64), bounds);
        }

        [Fact]
        public void Resize_PushesAnExpandingWindowAboveTheTaskbar()
        {
            Rectangle workArea = new(0, 40, 1000, 760);

            Rectangle bounds = LiteWindowPlacement.Resize(workArea, 120, expanded: true, currentLocation: new Point(325, 700));

            Assert.Equal(new Rectangle(325, 317, 675, 483), bounds);
        }

        [Fact]
        public void PlaceSelectionIcon_ScalesAndFlipsAtTheWorkAreaEdges()
        {
            Rectangle workArea = new(-1920, -1080, 1920, 1040);

            Rectangle bounds = LiteWindowPlacement.PlaceSelectionIcon(workArea, 144, new Point(-30, -65));

            Assert.Equal(new Rectangle(-108, -143, 66, 66), bounds);
        }
    }
}
