using System;
using System.Text;

namespace Flint3.Data.Csf
{
    /// <summary>
    /// 复刻 SQLite 默认 LIKE 语义：% 匹配任意（含零）字符、_ 匹配单个字符、仅 ASCII 大小写不敏感、两端锚定、无转义符。
    /// 由于入参来自 WordPattern（字面量只允许 ASCII 字母），匹配在折叠后的 UTF-8 字节上进行即可。
    /// </summary>
    internal sealed class LikePattern
    {
        private readonly byte[] _pattern;

        private LikePattern(byte[] pattern, byte[]? literalPrefix)
        {
            _pattern = pattern;
            LiteralPrefix = literalPrefix;
        }

        /// <summary>当模式形如“字面量%”时给出可直接定位的折叠前缀</summary>
        public byte[]? LiteralPrefix { get; }

        public static LikePattern Parse(string like)
        {
            byte[] pattern = Encoding.UTF8.GetBytes(like);
            CsfText.FoldInPlace(pattern);

            byte[]? literalPrefix = null;
            if (pattern.Length > 0 && pattern[^1] == (byte)'%')
            {
                bool plain = true;
                for (int i = 0; i < pattern.Length - 1; i++)
                {
                    if (pattern[i] == (byte)'%' || pattern[i] == (byte)'_')
                    {
                        plain = false;
                        break;
                    }
                }

                if (plain && pattern.Length > 1)
                {
                    literalPrefix = pattern[..^1];
                }
            }

            return new LikePattern(pattern, literalPrefix);
        }

        public bool IsMatch(ReadOnlySpan<byte> value)
        {
            ReadOnlySpan<byte> pattern = _pattern;
            int patternIndex = 0;
            int valueIndex = 0;
            int starPatternIndex = -1;
            int starValueIndex = 0;

            while (valueIndex < value.Length)
            {
                if (patternIndex < pattern.Length && pattern[patternIndex] == (byte)'%')
                {
                    starPatternIndex = patternIndex++;
                    starValueIndex = valueIndex;
                    continue;
                }

                if (patternIndex < pattern.Length && pattern[patternIndex] == (byte)'_')
                {
                    valueIndex = SkipChar(value, valueIndex);
                    patternIndex++;
                    continue;
                }

                if (patternIndex < pattern.Length && ReadChar(pattern, patternIndex, out int patternChar, out int patternLength)
                    && ReadChar(value, valueIndex, out int valueChar, out int valueLength)
                    && patternChar == valueChar)
                {
                    patternIndex += patternLength;
                    valueIndex += valueLength;
                    continue;
                }

                if (starPatternIndex < 0)
                {
                    return false;
                }

                starValueIndex = SkipChar(value, starValueIndex);
                valueIndex = starValueIndex;
                patternIndex = starPatternIndex + 1;
            }

            while (patternIndex < pattern.Length && pattern[patternIndex] == (byte)'%')
            {
                patternIndex++;
            }

            return patternIndex == pattern.Length;
        }

        private static int SkipChar(ReadOnlySpan<byte> source, int index)
        {
            index++;
            while (index < source.Length && (source[index] & 0xC0) == 0x80)
            {
                index++;
            }

            return index;
        }

        private static bool ReadChar(ReadOnlySpan<byte> source, int index, out int value, out int length)
        {
            byte first = source[index];
            if (first < 0x80)
            {
                value = first;
                length = 1;
                return true;
            }

            int charLength = (first & 0xE0) == 0xC0 ? 2 : (first & 0xF0) == 0xE0 ? 3 : (first & 0xF8) == 0xF0 ? 4 : 1;
            if (index + charLength > source.Length)
            {
                value = -1;
                length = 1;
                return false;
            }

            int result = first & (0xFF >> (charLength + 1));
            for (int i = 1; i < charLength; i++)
            {
                result = (result << 6) | (source[index + i] & 0x3F);
            }

            value = result;
            length = charLength;
            return true;
        }
    }
}
