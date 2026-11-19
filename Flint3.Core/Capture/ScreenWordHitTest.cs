using System;
using System.Collections.Generic;

namespace Flint3.Core.Capture
{
    /// <summary>
    /// OCR 识别出的一个词及其在截图内的矩形（物理像素）
    /// </summary>
    public readonly record struct CapturedWord(string Text, double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;

        public double Bottom => Y + Height;
    }

    /// <summary>
    /// 词级命中：给定点击位置，找覆盖该点或距离最近的词
    /// </summary>
    public static class ScreenWordHitTest
    {
        /// <summary>
        /// 返回距离点击位置不超过 maxDistance（像素）的最近词；无命中返回 null。
        /// 点在词框内时距离为 0；多个词距离相同取先出现者（阅读顺序）
        /// </summary>
        public static CapturedWord? FindNearestWord(IReadOnlyList<CapturedWord> words, double x, double y, double maxDistance)
        {
            ArgumentNullException.ThrowIfNull(words);

            CapturedWord? best = null;
            double bestDistance = double.MaxValue;
            foreach (var word in words)
            {
                double dx = Math.Max(Math.Max(word.X - x, 0), x - word.Right);
                double dy = Math.Max(Math.Max(word.Y - y, 0), y - word.Bottom);
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = word;
                }
            }

            return bestDistance <= maxDistance ? best : null;
        }
    }
}