using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace Flint3.Dict
{
    /// <summary>
    /// 遍历 key block 时产出的词条：记录偏移指向“所有记录块解压后拼接”的字节位置
    /// </summary>
    public sealed class MdxEntry
    {
        public string Word { get; init; } = "";

        public long RecordOffset { get; init; }

        public int RecordSize { get; init; }
    }

    /// <summary>
    /// mdx 2.0 只读解析器，支持未加密 + 无压缩/zlib 压缩；加密、LZO、1.2 版本会抛 <see cref="MdxUnsupportedException"/>
    /// </summary>
    public sealed class MdxReader : IDisposable
    {
        private readonly FileStream _stream;

        private readonly object _lock = new();

        private readonly List<KeyBlockIndex> _keyBlocks = [];

        private readonly List<RecordBlockIndex> _recordBlocks = [];

        private readonly List<long> _keyBlockOffsets = [];

        private readonly List<long> _recordBlockDecompStarts = [];

        private long _keyBlocksStart;

        private long _recordBlocksStart;

        private byte[]? _cachedRecordBlock;

        private int _cachedRecordBlockIndex = -1;

        private MdxReader(FileStream stream)
        {
            _stream = stream;
            Header = ReadHeader(stream);
            ValidateHeader();
            ReadKeySection();
            ReadRecordSection();
        }

        public MdxHeader Header { get; }

        public int EntryCount { get; private set; }

        public long TotalRecordLength { get; private set; }

        public static MdxReader Open(string path)
        {
            FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            try
            {
                return new MdxReader(stream);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 顺序遍历全部词条，导入建索引时使用
        /// </summary>
        public IEnumerable<MdxEntry> EnumerateEntries(CancellationToken cancellationToken = default)
        {
            string? pendingWord = null;
            long pendingOffset = 0;

            for (int i = 0; i < _keyBlocks.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] data = ReadKeyBlock(i);
                int position = 0;
                while (position + 8 <= data.Length)
                {
                    long offset = BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(position));
                    position += 8;
                    string word = ReadNullTerminatedString(data, ref position);

                    if (pendingWord is not null)
                    {
                        yield return new MdxEntry
                        {
                            Word = pendingWord,
                            RecordOffset = pendingOffset,
                            RecordSize = (int)Math.Clamp(offset - pendingOffset, 0, int.MaxValue),
                        };
                    }

                    pendingWord = word;
                    pendingOffset = offset;
                }
            }

            if (pendingWord is not null)
            {
                yield return new MdxEntry
                {
                    Word = pendingWord,
                    RecordOffset = pendingOffset,
                    RecordSize = (int)Math.Clamp(TotalRecordLength - pendingOffset, 0, int.MaxValue),
                };
            }
        }

        /// <summary>
        /// 按词条里的记录偏移读取一条记录（自动定位并解压所属记录块）
        /// </summary>
        public string ReadRecord(long offset)
        {
            if (offset < 0 || offset >= TotalRecordLength)
            {
                return string.Empty;
            }

            lock (_lock)
            {
                int index = FindRecordBlock(offset);
                if (index < 0)
                {
                    return string.Empty;
                }

                byte[] block = GetRecordBlock(index);
                int local = (int)(offset - _recordBlockDecompStarts[index]);
                if (local < 0 || local >= block.Length)
                {
                    return string.Empty;
                }

                int position = local;
                string text = ReadNullTerminatedString(block, ref position);
                return text;
            }
        }

        public void Dispose()
        {
            _stream.Dispose();
        }

        private void ValidateHeader()
        {
            if (!Header.EngineVersion.StartsWith("2.", StringComparison.Ordinal))
            {
                throw new MdxUnsupportedException($"暂不支持 mdx {Header.EngineVersion} 版本，请使用 2.0 版本的词库");
            }

            if ((Header.Encrypted & 1) != 0)
            {
                throw new MdxUnsupportedException("暂不支持加密的 mdx 词库（Encrypted=1/3）");
            }

            if ((Header.Encrypted & 2) != 0)
            {
                throw new MdxUnsupportedException("暂不支持加密了关键词索引的 mdx 词库（Encrypted=2/3）");
            }
        }

        private static MdxHeader ReadHeader(FileStream stream)
        {
            byte[] lengthBuffer = ReadExactly(stream, 4);
            int length = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
            if (length <= 0 || length > 1 << 20)
            {
                throw new MdxUnsupportedException("mdx 文件头长度异常，可能不是有效的 mdx 文件");
            }

            byte[] headerBuffer = ReadExactly(stream, length);
            ReadExactly(stream, 4); // header_str 的 ADLER32

            string xml = new UnicodeEncoding(false, false).GetString(headerBuffer).TrimEnd('\0', ' ');
            return MdxHeader.Parse(xml);
        }

        private void ReadKeySection()
        {
            byte[] preamble = ReadExactly(_stream, 40);
            int blockCount = checked((int)BinaryPrimitives.ReadInt64BigEndian(preamble.AsSpan(0)));
            EntryCount = checked((int)BinaryPrimitives.ReadInt64BigEndian(preamble.AsSpan(8)));
            long indexDecompLength = BinaryPrimitives.ReadInt64BigEndian(preamble.AsSpan(16));
            long indexCompLength = BinaryPrimitives.ReadInt64BigEndian(preamble.AsSpan(24));
            long keyBlocksLength = BinaryPrimitives.ReadInt64BigEndian(preamble.AsSpan(32));
            ReadExactly(_stream, 4); // 前 40 字节的 ADLER32

            byte[] indexCompressed = ReadExactly(_stream, checked((int)indexCompLength));
            byte[] index = Decompress(indexCompressed, indexDecompLength);
            ParseKeyIndex(index, blockCount);

            _keyBlocksStart = _stream.Position;
            long recordSectionStart = _keyBlocksStart + keyBlocksLength;
            long offset = 0;
            foreach (KeyBlockIndex block in _keyBlocks)
            {
                _keyBlockOffsets.Add(offset);
                offset += block.CompressedSize;
            }

            _stream.Position = recordSectionStart;
        }

        private void ParseKeyIndex(byte[] index, int blockCount)
        {
            // 不同工具写出的索引对“首尾词是否带结束符”不一致，先按不带重试，失败再按带结束符解析
            if (!TryParseKeyIndex(index, blockCount, consumeNull: false, out List<KeyBlockIndex>? blocks))
            {
                if (!TryParseKeyIndex(index, blockCount, consumeNull: true, out blocks))
                {
                    throw new MdxUnsupportedException("mdx 关键词索引结构异常，暂不支持该词库");
                }
            }

            _keyBlocks.AddRange(blocks!);
        }

        private bool TryParseKeyIndex(byte[] index, int blockCount, bool consumeNull, out List<KeyBlockIndex>? blocks)
        {
            blocks = new List<KeyBlockIndex>(Math.Max(blockCount, 0));
            int position = 0;
            int unit = Header.UnitSize;

            for (int i = 0; i < blockCount; i++)
            {
                if (position + 8 + 2 > index.Length)
                {
                    return false;
                }

                BinaryPrimitives.ReadInt64BigEndian(index.AsSpan(position));
                position += 8;

                int firstLength = BinaryPrimitives.ReadUInt16BigEndian(index.AsSpan(position));
                position += 2;
                position += firstLength * unit;
                if (consumeNull)
                {
                    position += unit;
                }

                if (position + 2 > index.Length)
                {
                    return false;
                }

                int lastLength = BinaryPrimitives.ReadUInt16BigEndian(index.AsSpan(position));
                position += 2;
                position += lastLength * unit;
                if (consumeNull)
                {
                    position += unit;
                }

                if (position + 16 > index.Length)
                {
                    return false;
                }

                long compressedSize = BinaryPrimitives.ReadInt64BigEndian(index.AsSpan(position));
                long decompressedSize = BinaryPrimitives.ReadInt64BigEndian(index.AsSpan(position + 8));
                position += 16;

                if (compressedSize <= 0 || decompressedSize <= 0)
                {
                    return false;
                }

                blocks.Add(new KeyBlockIndex(compressedSize, decompressedSize));
            }

            return position == index.Length;
        }

        private void ReadRecordSection()
        {
            long sectionStart = _stream.Position;
            byte[] preamble = ReadExactly(_stream, 32);
            int blockCount = checked((int)BinaryPrimitives.ReadInt64BigEndian(preamble.AsSpan(0)));
            long indexLength = BinaryPrimitives.ReadInt64BigEndian(preamble.AsSpan(16));
            if (blockCount <= 0 || indexLength != blockCount * 16L)
            {
                throw new MdxUnsupportedException("mdx 记录区索引异常，暂不支持该词库");
            }

            byte[] index = ReadExactly(_stream, checked((int)indexLength));
            long decompressedStart = 0;
            long compressedTotal = 0;
            _recordBlocks.Clear();
            _recordBlockDecompStarts.Clear();

            for (int i = 0; i < blockCount; i++)
            {
                long compressedSize = BinaryPrimitives.ReadInt64BigEndian(index.AsSpan(i * 16));
                long decompressedSize = BinaryPrimitives.ReadInt64BigEndian(index.AsSpan(i * 16 + 8));
                _recordBlocks.Add(new RecordBlockIndex(compressedSize, decompressedSize));
                _recordBlockDecompStarts.Add(decompressedStart);
                decompressedStart += decompressedSize;
                compressedTotal += compressedSize;
            }

            TotalRecordLength = decompressedStart;
            _recordBlocksStart = sectionStart + 32 + indexLength;
            _ = compressedTotal;
            _stream.Position = sectionStart;
        }

        private byte[] ReadKeyBlock(int index)
        {
            lock (_lock)
            {
                _stream.Position = _keyBlocksStart + _keyBlockOffsets[index];
                byte[] raw = ReadExactly(_stream, checked((int)_keyBlocks[index].CompressedSize));
                return Decompress(raw, _keyBlocks[index].DecompressedSize);
            }
        }

        private int FindRecordBlock(long offset)
        {
            int low = 0;
            int high = _recordBlockDecompStarts.Count - 1;
            while (low <= high)
            {
                int middle = (low + high) / 2;
                long start = _recordBlockDecompStarts[middle];
                long end = start + _recordBlocks[middle].DecompressedSize;
                if (offset < start)
                {
                    high = middle - 1;
                }
                else if (offset >= end)
                {
                    low = middle + 1;
                }
                else
                {
                    return middle;
                }
            }

            return -1;
        }

        private byte[] GetRecordBlock(int index)
        {
            if (_cachedRecordBlockIndex == index && _cachedRecordBlock is not null)
            {
                return _cachedRecordBlock;
            }

            long compressedStart = _recordBlocksStart;
            for (int i = 0; i < index; i++)
            {
                compressedStart += _recordBlocks[i].CompressedSize;
            }

            _stream.Position = compressedStart;
            byte[] raw = ReadExactly(_stream, checked((int)_recordBlocks[index].CompressedSize));
            _cachedRecordBlock = Decompress(raw, _recordBlocks[index].DecompressedSize);
            _cachedRecordBlockIndex = index;
            return _cachedRecordBlock;
        }

        /// <summary>
        /// 块格式为 comp_type(4, 小端) + adler32(4, 大端) + 数据；未压缩时索引段可能不带这 8 字节头
        /// </summary>
        private static byte[] Decompress(byte[] raw, long expectedLength)
        {
            if (raw.LongLength == expectedLength)
            {
                return raw;
            }

            if (raw.Length < 8)
            {
                throw new MdxUnsupportedException("mdx 数据块长度异常");
            }

            uint type = BinaryPrimitives.ReadUInt32LittleEndian(raw);
            ReadOnlySpan<byte> data = raw.AsSpan(8);
            switch (type)
            {
                case 0:
                    return data.ToArray();
                case 2:
                    using (ZLibStream zlib = new(new MemoryStream(data.ToArray()), CompressionMode.Decompress))
                    using (MemoryStream output = new(Math.Max((int)Math.Min(expectedLength, int.MaxValue), data.Length)))
                    {
                        zlib.CopyTo(output);
                        return output.ToArray();
                    }
                case 1:
                    throw new MdxUnsupportedException("暂不支持 LZO 压缩的 mdx 词库");
                default:
                    throw new MdxUnsupportedException($"mdx 数据块使用了未知压缩类型 {type}");
            }
        }

        private string ReadNullTerminatedString(byte[] data, ref int position)
        {
            int start = position;
            if (Header.UnitSize == 2)
            {
                while (position + 1 < data.Length && !(data[position] == 0 && data[position + 1] == 0))
                {
                    position += 2;
                }

                string text = Header.Encoding.GetString(data, start, position - start);
                position = Math.Min(position + 2, data.Length);
                return text;
            }

            while (position < data.Length && data[position] != 0)
            {
                position++;
            }

            string result = Header.Encoding.GetString(data, start, position - start);
            position = Math.Min(position + 1, data.Length);
            return result;
        }

        private static byte[] ReadExactly(FileStream stream, int count)
        {
            byte[] buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                int current = stream.Read(buffer, read, count - read);
                if (current <= 0)
                {
                    throw new MdxUnsupportedException("mdx 文件不完整");
                }

                read += current;
            }

            return buffer;
        }

        private sealed record KeyBlockIndex(long CompressedSize, long DecompressedSize);

        private sealed record RecordBlockIndex(long CompressedSize, long DecompressedSize);
    }
}
