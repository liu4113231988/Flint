using System;
using System.Drawing;

namespace Flint3.Core.Capture
{
    /// <summary>按显示器工作区与 DPI 计算简洁窗的屏幕像素坐标。</summary>
    public static class LiteWindowPlacement
    {
        public static Rectangle Place(Rectangle workArea, uint dpi, bool expanded, Point? cursor)
        {
            Size size = GetSize(workArea, dpi, expanded);
            if (cursor is not Point pointer)
            {
                return Fit(workArea, size, new Point(
                    workArea.Left + (workArea.Width - size.Width) / 2,
                    workArea.Top + workArea.Height / 4));
            }

            return PlaceNearPointer(workArea, size, pointer, Scale(16, dpi));
        }

        public static Rectangle PlaceSelectionIcon(Rectangle workArea, uint dpi, Point pointer)
        {
            int edge = Scale(44, dpi);
            Size size = new(Math.Min(workArea.Width, edge), Math.Min(workArea.Height, edge));
            return PlaceNearPointer(workArea, size, pointer, Scale(8, dpi));
        }

        private static Rectangle PlaceNearPointer(Rectangle workArea, Size size, Point pointer, int gap)
        {
            int left = pointer.X + gap;
            int top = pointer.Y + gap;
            if (left + size.Width > workArea.Right)
            {
                left = pointer.X - size.Width - gap;
            }

            if (top + size.Height > workArea.Bottom)
            {
                top = pointer.Y - size.Height - gap;
            }

            return Fit(workArea, size, new Point(left, top));
        }

        public static Rectangle Resize(Rectangle workArea, uint dpi, bool expanded, Point currentLocation)
            => Fit(workArea, GetSize(workArea, dpi, expanded), currentLocation);

        private static Size GetSize(Rectangle workArea, uint dpi, bool expanded)
            => new(Math.Min(workArea.Width, Scale(540, dpi)), Math.Min(workArea.Height, Scale(expanded ? 386 : 64, dpi)));

        private static int Scale(int size, uint dpi)
            => (int)Math.Round(size * ((dpi == 0 ? 96 : dpi) / 96d), MidpointRounding.AwayFromZero);

        private static Rectangle Fit(Rectangle workArea, Size size, Point location)
            => new(Math.Clamp(location.X, workArea.Left, workArea.Right - size.Width),
                Math.Clamp(location.Y, workArea.Top, workArea.Bottom - size.Height),
                size.Width, size.Height);
    }
}
