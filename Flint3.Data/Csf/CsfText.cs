using System;
using System.Text;

namespace Flint3.Data.Csf
{
    /// <summary>
    /// 文本键的归一化与比较规则，必须与 SQLite 的 NOCASE 语义逐字节一致
    /// </summary>
    internal static class CsfText
    {
        /// <summary>
        /// SQLite NOCASE 只折叠 ASCII A-Z，这里直接在 UTF-8 字节上折叠，多字节序列不含 ASCII 字母，不受影响
        /// </summary>
        public static void FoldInPlace(Span<byte> utf8)
        {
            for (int i = 0; i < utf8.Length; i++)
            {
                byte value = utf8[i];
                if (value >= (byte)'A' && value <= (byte)'Z')
                {
                    utf8[i] = (byte)(value + 32);
                }
            }
        }

        public static int Fold(string text, Span<byte> destination)
        {
            int length = Encoding.UTF8.GetBytes(text, destination);
            FoldInPlace(destination[..length]);
            return length;
        }

        /// <summary>
        /// 与 ECDICT 构建期一致：逐字符小写化后只保留字母与数字
        /// </summary>
        public static string DeriveStripWord(string word)
        {
            StringBuilder builder = new(word.Length);
            foreach (char c in word)
            {
                char lower = char.ToLowerInvariant(c);
                if (char.IsLetterOrDigit(lower))
                {
                    builder.Append(lower);
                }
            }

            return builder.ToString();
        }

        /// <summary>无符号字节序比较，与 SQLite memcmp 一致</summary>
        public static int Compare(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            int common = Math.Min(left.Length, right.Length);
            for (int i = 0; i < common; i++)
            {
                int diff = left[i] - right[i];
                if (diff != 0)
                {
                    return diff;
                }
            }

            return left.Length - right.Length;
        }

        public static bool StartsWith(ReadOnlySpan<byte> value, ReadOnlySpan<byte> prefix)
        {
            return value.Length >= prefix.Length && value[..prefix.Length].SequenceEqual(prefix);
        }

        /// <summary>等价于 SQLite 的 length(text)：按 Unicode 码点数计数</summary>
        public static int CountChars(ReadOnlySpan<byte> utf8)
        {
            int count = 0;
            for (int i = 0; i < utf8.Length; i++)
            {
                if ((utf8[i] & 0xC0) != 0x80)
                {
                    count++;
                }
            }

            return count;
        }
    }

    internal static class VarInt
    {
        public static int Write(Span<byte> destination, uint value)
        {
            int index = 0;
            while (value >= 0x80)
            {
                destination[index++] = (byte)(value | 0x80);
                value >>= 7;
            }

            destination[index++] = (byte)value;
            return index;
        }

        public static uint Read(ReadOnlySpan<byte> source, ref int offset)
        {
            uint result = 0;
            int shift = 0;
            while (true)
            {
                byte current = source[offset++];
                result |= (uint)(current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                {
                    return result;
                }

                shift += 7;
            }
        }
    }

    /// <summary>可增长的字节缓冲区，构建期使用</summary>
    internal sealed class ByteBuffer
    {
        private byte[] _buffer;

        public ByteBuffer(int capacity = 4096)
        {
            _buffer = new byte[capacity];
        }

        public int Length { get; private set; }

        public ReadOnlySpan<byte> Span => _buffer.AsSpan(0, Length);

        public void Clear()
        {
            Length = 0;
        }

        public void WriteByte(byte value)
        {
            Ensure(1);
            _buffer[Length++] = value;
        }

        public void Write(ReadOnlySpan<byte> data)
        {
            Ensure(data.Length);
            data.CopyTo(_buffer.AsSpan(Length));
            Length += data.Length;
        }

        public void WriteVarInt(uint value)
        {
            Ensure(5);
            Length += VarInt.Write(_buffer.AsSpan(Length), value);
        }

        private void Ensure(int extra)
        {
            if (Length + extra <= _buffer.Length)
            {
                return;
            }

            int size = _buffer.Length;
            while (size < Length + extra)
            {
                size *= 2;
            }

            Array.Resize(ref _buffer, size);
        }
    }

    /// <summary>只追加的字节区域，构建期存放全部折叠键</summary>
    internal sealed class ByteArena
    {
        private byte[] _buffer;

        public ByteArena(int capacity = 1 << 20)
        {
            _buffer = new byte[capacity];
        }

        public int Length { get; private set; }

        public ReadOnlySpan<byte> Slice(int offset, int length) => _buffer.AsSpan(offset, length);

        public int Append(ReadOnlySpan<byte> data)
        {
            int offset = Length;
            if (Length + data.Length > _buffer.Length)
            {
                int size = _buffer.Length;
                while (size < Length + data.Length)
                {
                    size *= 2;
                }

                Array.Resize(ref _buffer, size);
            }

            data.CopyTo(_buffer.AsSpan(Length));
            Length += data.Length;
            return offset;
        }
    }
}
