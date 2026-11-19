using System;
using Flint3.Core.Capture;
using Xunit;

namespace Flint3.Core.Tests
{
    public class ScreenCaptureServiceTests
    {
        private static ScreenCaptureResult MakeImage(int width, int height)
        {
            byte[] pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = (byte)(i % 251);
            }

            return new ScreenCaptureResult { BgraPixels = pixels, Width = width, Height = height };
        }

        [Fact]
        public void Crop_ReturnsExactSubRect()
        {
            var source = MakeImage(4, 4);
            var crop = ScreenCaptureService.Crop(source, 1, 1, 2, 2);

            Assert.NotNull(crop);
            Assert.Equal(2, crop!.Width);
            Assert.Equal(2, crop.Height);
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 2; col++)
                {
                    int srcIndex = ((1 + row) * 4 + (1 + col)) * 4;
                    int dstIndex = (row * 2 + col) * 4;
                    for (int b = 0; b < 4; b++)
                    {
                        Assert.Equal(source.BgraPixels[srcIndex + b], crop.BgraPixels[dstIndex + b]);
                    }
                }
            }
        }

        [Fact]
        public void Crop_ClampsToImageBounds()
        {
            var source = MakeImage(4, 4);
            var crop = ScreenCaptureService.Crop(source, -2, -2, 100, 100);

            Assert.NotNull(crop);
            Assert.Equal(4, crop!.Width);
            Assert.Equal(4, crop.Height);
            Assert.Equal(source.BgraPixels, crop.BgraPixels);
        }

        [Theory]
        [InlineData(10, 0, 5, 5)]  // 完全在右侧外
        [InlineData(0, 10, 5, 5)]  // 完全在下方外
        [InlineData(2, 2, 0, 5)]   // 零宽
        [InlineData(2, 2, 5, -1)]  // 负高
        public void Crop_InvalidRegionReturnsNull(int x, int y, int w, int h)
        {
            var source = MakeImage(4, 4);
            Assert.Null(ScreenCaptureService.Crop(source, x, y, w, h));
        }

        [Fact]
        public void CaptureRegion_InvalidSizeReturnsNull()
        {
            Assert.Null(ScreenCaptureService.CaptureRegion(0, 0, 0, 10));
            Assert.Null(ScreenCaptureService.CaptureRegion(0, 0, 10, -1));
        }

        [Fact]
        public void EncodeBmp_HeaderAndFlippedRows()
        {
            var source = MakeImage(3, 2);
            byte[] bmp = ScreenCaptureService.EncodeBmp(source);

            Assert.Equal(14 + 40 + 3 * 2 * 4, bmp.Length);
            Assert.Equal((byte)'B', bmp[0]);
            Assert.Equal((byte)'M', bmp[1]);
            Assert.Equal(54, BitConverter.ToInt32(bmp, 10)); // 像素偏移
            Assert.Equal(3, BitConverter.ToInt32(bmp, 18));  // 宽
            Assert.Equal(2, BitConverter.ToInt32(bmp, 22));  // 高（正 = 自下而上）
            Assert.Equal(32, bmp[28]);                       // 位深

            // BMP 第一行应是源图最后一行
            for (int i = 0; i < 3 * 4; i++)
            {
                Assert.Equal(source.BgraPixels[3 * 4 + i], bmp[54 + i]);
            }
        }
    }
}