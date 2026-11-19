using System;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Flint3.Core.Capture
{
    /// <summary>
    /// 一次屏幕截图的结果：32 位 BGRA 像素（自上而下），物理像素坐标
    /// </summary>
    public sealed class ScreenCaptureResult
    {
        public required byte[] BgraPixels { get; init; }

        public required int Width { get; init; }

        public required int Height { get; init; }

        public int Stride => Width * 4;
    }

    /// <summary>
    /// GDI BitBlt 截屏：对屏幕 DC 拷贝指定矩形（物理像素），免打包、AOT 友好
    /// </summary>
    public static class ScreenCaptureService
    {
        private const uint CaptureBlt = 0x40000000;

        /// <summary>
        /// 截取屏幕矩形区域；失败返回 null。left/top 可为负（多显示器虚拟屏幕坐标）
        /// </summary>
        public static unsafe ScreenCaptureResult? CaptureRegion(int left, int top, int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            HDC screenDc = Flint3CorePInvoke.GetDC(HWND.Null);
            if (screenDc.IsNull)
            {
                return null;
            }

            HDC memDc = Flint3CorePInvoke.CreateCompatibleDC(screenDc);
            if (memDc.IsNull)
            {
                Flint3CorePInvoke.ReleaseDC(HWND.Null, screenDc);
                return null;
            }

            DeleteObjectSafeHandle? dibHandle = null;
            HGDIOBJ oldObject = default;
            try
            {
                BITMAPINFO bmi = new();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
                bmi.bmiHeader.biWidth = width;
                bmi.bmiHeader.biHeight = -height; // 负值 = 自上而下，与数组行序一致
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0; // BI_RGB

                void* bits;
                dibHandle = Flint3CorePInvoke.CreateDIBSection(memDc, &bmi, DIB_USAGE.DIB_RGB_COLORS, out bits, null, 0);
                if (dibHandle is null || dibHandle.IsInvalid || bits == null)
                {
                    return null;
                }

                HBITMAP dib = new HBITMAP(dibHandle.DangerousGetHandle());
                oldObject = Flint3CorePInvoke.SelectObject(memDc, dib);
                bool ok = Flint3CorePInvoke.BitBlt(memDc, 0, 0, width, height, screenDc, left, top,
                    ROP_CODE.SRCCOPY | (ROP_CODE)CaptureBlt);
                if (!ok)
                {
                    return null;
                }

                byte[] pixels = new byte[width * height * 4];
                Marshal.Copy((IntPtr)bits, pixels, 0, pixels.Length);
                return new ScreenCaptureResult { BgraPixels = pixels, Width = width, Height = height };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
                return null;
            }
            finally
            {
                if (!oldObject.IsNull)
                {
                    Flint3CorePInvoke.SelectObject(memDc, oldObject);
                }

                dibHandle?.Dispose();
                Flint3CorePInvoke.DeleteDC(memDc);
                Flint3CorePInvoke.ReleaseDC(HWND.Null, screenDc);
            }
        }

        /// <summary>
        /// 从截图中裁剪子矩形（自动夹取到边界）；区域无效返回 null
        /// </summary>
        public static ScreenCaptureResult? Crop(ScreenCaptureResult source, int x, int y, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(source);

            int x0 = Math.Clamp(x, 0, source.Width);
            int y0 = Math.Clamp(y, 0, source.Height);
            int x1 = Math.Clamp(x + width, 0, source.Width);
            int y1 = Math.Clamp(y + height, 0, source.Height);
            int w = x1 - x0;
            int h = y1 - y0;
            if (w <= 0 || h <= 0)
            {
                return null;
            }

            byte[] pixels = new byte[w * h * 4];
            for (int row = 0; row < h; row++)
            {
                Buffer.BlockCopy(source.BgraPixels, ((y0 + row) * source.Width + x0) * 4, pixels, row * w * 4, w * 4);
            }

            return new ScreenCaptureResult { BgraPixels = pixels, Width = w, Height = h };
        }

        /// <summary>
        /// 编码为 BMP 文件字节（32 位 BI_RGB，自下而上行序），供 BitmapImage/BitmapDecoder 直接解码
        /// </summary>
        public static byte[] EncodeBmp(ScreenCaptureResult source)
        {
            ArgumentNullException.ThrowIfNull(source);

            const int fileHeaderSize = 14;
            const int infoHeaderSize = 40;
            int pixelBytes = source.BgraPixels.Length;
            byte[] bmp = new byte[fileHeaderSize + infoHeaderSize + pixelBytes];

            bmp[0] = (byte)'B';
            bmp[1] = (byte)'M';
            WriteInt32(bmp, 2, bmp.Length);
            WriteInt32(bmp, 10, fileHeaderSize + infoHeaderSize);

            WriteInt32(bmp, 14, infoHeaderSize);
            WriteInt32(bmp, 18, source.Width);
            WriteInt32(bmp, 22, source.Height); // 正值 = 自下而上
            bmp[26] = 1; // biPlanes
            bmp[28] = 32; // biBitCount
            WriteInt32(bmp, 34, pixelBytes);

            int stride = source.Stride;
            for (int row = 0; row < source.Height; row++)
            {
                int srcOffset = row * stride;
                int dstOffset = fileHeaderSize + infoHeaderSize + (source.Height - 1 - row) * stride;
                Buffer.BlockCopy(source.BgraPixels, srcOffset, bmp, dstOffset, stride);
            }

            return bmp;
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }
    }
}