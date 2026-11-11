using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace Flint3.Data.Chinese
{
    /// <summary>
    /// 旁挂中文反查索引读取器。只读 mmap，查询不分配大对象；
    /// 文件缺失、损坏或版本不符时返回带状态的实例而不是抛异常，保证英文查词不受影响。
    /// </summary>
    public sealed class ChineseIndexReader : IDisposable
    {
        private readonly MemoryMappedFile? _mappedFile;
        private readonly MemoryMappedViewAccessor? _view;
        private unsafe byte* _base;

        private readonly int _termCount;
        private readonly int _postingCount;
        private readonly int _recordCount;
        private readonly long _termOffsetsOffset;
        private readonly long _entryDataOffset;
        private readonly long _rankIdsOffset;

        private bool _disposed;

        public ChineseIndexStatus Status { get; }

        public string ErrorMessage { get; }

        public int TermCount => _termCount;

        public int PostingCount => _postingCount;

        public int RecordCount => _recordCount;

        private ChineseIndexReader(ChineseIndexStatus status, string message)
        {
            Status = status;
            ErrorMessage = message;
        }

        private ChineseIndexReader(string path, Header header)
        {
            Status = ChineseIndexStatus.Ready;
            ErrorMessage = string.Empty;
            _termCount = header.TermCount;
            _postingCount = header.PostingCount;
            _recordCount = header.RecordCount;
            _termOffsetsOffset = header.TermOffsetsOffset;
            _entryDataOffset = header.EntryDataOffset;
            _rankIdsOffset = header.RankIdsOffset;

            _mappedFile = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            _view = _mappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            unsafe
            {
                byte* pointer = null;
                _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
                _base = pointer + _view.PointerOffset;
            }
        }

        public static ChineseIndexReader Open(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new ChineseIndexReader(ChineseIndexStatus.Missing, "未找到中文反查索引");
            }

            try
            {
                Header header;
                using (FileStream probe = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (probe.Length < ChineseIndexFormat.HeaderSize)
                    {
                        return new ChineseIndexReader(ChineseIndexStatus.Corrupt, "中文反查索引文件过短");
                    }

                    byte[] buffer = new byte[ChineseIndexFormat.HeaderSize];
                    probe.ReadExactly(buffer);

                    uint magic = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(ChineseIndexFormat.OffMagic));
                    if (magic != ChineseIndexFormat.Magic)
                    {
                        return new ChineseIndexReader(ChineseIndexStatus.Corrupt, "中文反查索引不是已知格式");
                    }

                    ushort version = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(ChineseIndexFormat.OffVersion));
                    if (version != ChineseIndexFormat.Version)
                    {
                        return new ChineseIndexReader(ChineseIndexStatus.VersionMismatch, $"中文反查索引版本不匹配：{version}");
                    }

                    header = new Header
                    {
                        TermCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(ChineseIndexFormat.OffTermCount)),
                        PostingCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(ChineseIndexFormat.OffPostingCount)),
                        RecordCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(ChineseIndexFormat.OffRecordCount)),
                        TermOffsetsOffset = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(ChineseIndexFormat.OffTermOffsetsOffset)),
                        EntryDataOffset = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(ChineseIndexFormat.OffEntryDataOffset)),
                        RankIdsOffset = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(ChineseIndexFormat.OffRankIdsOffset)),
                        FileLength = probe.Length,
                    };
                }

                string? error = Validate(header);
                if (error is not null)
                {
                    return new ChineseIndexReader(ChineseIndexStatus.Corrupt, error);
                }

                return new ChineseIndexReader(path, header);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"打开中文反查索引失败: {ex}");
                return new ChineseIndexReader(ChineseIndexStatus.Corrupt, "中文反查索引无法读取");
            }
        }

        private static string? Validate(Header header)
        {
            long expectedLength = header.RankIdsOffset + 4L * header.RecordCount;
            if (header.TermCount <= 0 || header.RecordCount <= 0 || header.PostingCount < header.TermCount)
            {
                return "中文反查索引计数异常";
            }

            if (header.TermOffsetsOffset < ChineseIndexFormat.HeaderSize
                || header.EntryDataOffset <= header.TermOffsetsOffset
                || header.RankIdsOffset <= header.EntryDataOffset
                || expectedLength != header.FileLength)
            {
                return "中文反查索引布局异常";
            }

            return null;
        }

        /// <summary>
        /// 精确 + 前缀查询，返回按排序契约排好的记录 id。
        /// 顺序：精确词条整组在前，其后是其余前缀词条，组内按 Collins 降序 → frq 升序 → 词长 → 字母序。
        /// </summary>
        public List<int> Lookup(string query, int limit)
        {
            return LookupCore(query, limit, true);
        }

        /// <summary>只取精确词条，不做前缀扩展</summary>
        public List<int> LookupExact(string query, int limit)
        {
            return LookupCore(query, limit, false);
        }

        private List<int> LookupCore(string query, int limit, bool includePrefix)
        {
            List<int> results = [];
            if (Status != ChineseIndexStatus.Ready || limit <= 0 || _disposed)
            {
                return results;
            }

            if (!ChineseTermExtractor.TryNormalizeQuery(query, out string term))
            {
                return results;
            }

            byte[] key = Encoding.UTF8.GetBytes(term);
            int exact = FindTerm(key);
            HashSet<uint> seen = [];
            List<uint> ranks = new(Math.Min(limit, 64));
            if (exact >= 0)
            {
                ReadTermRanks(exact, limit, ranks, seen);
            }

            if (includePrefix && ranks.Count < limit)
            {
                int rangeStart = exact >= 0 ? exact : LowerBound(key);
                int rangeEnd = rangeStart;
                while (rangeEnd < _termCount && StartsWith(TermAt(rangeEnd), key))
                {
                    rangeEnd++;
                }

                if (rangeEnd > rangeStart)
                {
                    MergePrefix(rangeStart, rangeEnd, exact, limit, ranks, seen);
                }
            }

            foreach (uint rank in ranks)
            {
                results.Add(ReadRecordId(rank));
            }

            return results;
        }

        private void ReadTermRanks(int termIndex, int limit, List<uint> ranks, HashSet<uint> seen)
        {
            PostingCursor cursor = OpenCursor(termIndex);
            while (cursor.Remaining > 0 && ranks.Count < limit)
            {
                if (cursor.Current < (uint)_recordCount && seen.Add(cursor.Current))
                {
                    ranks.Add(cursor.Current);
                }

                Advance(ref cursor);
            }
        }

        private void MergePrefix(int rangeStart, int rangeEnd, int exact, int limit, List<uint> ranks, HashSet<uint> seen)
        {
            PriorityQueue<int, uint> queue = new();
            PostingCursor[] cursors = new PostingCursor[rangeEnd - rangeStart];
            int cursorCount = 0;

            for (int index = rangeStart; index < rangeEnd; index++)
            {
                if (index == exact)
                {
                    continue;
                }

                PostingCursor cursor = OpenCursor(index);
                if (cursor.Remaining == 0)
                {
                    continue;
                }

                cursors[cursorCount] = cursor;
                queue.Enqueue(cursorCount, cursor.Current);
                cursorCount++;
            }

            while (ranks.Count < limit && queue.TryDequeue(out int cursorIndex, out _))
            {
                ref PostingCursor cursor = ref cursors[cursorIndex];
                if (cursor.Current < (uint)_recordCount && seen.Add(cursor.Current))
                {
                    ranks.Add(cursor.Current);
                }

                Advance(ref cursor);
                if (cursor.Remaining > 0)
                {
                    queue.Enqueue(cursorIndex, cursor.Current);
                }
            }
        }

        private PostingCursor OpenCursor(int termIndex)
        {
            long offset = _entryDataOffset + ReadUInt32(_termOffsetsOffset + 4L * termIndex);
            int termLength = ReadByte(offset);
            offset += 1 + termLength;

            uint count = ReadVarInt(ref offset);
            PostingCursor cursor = new()
            {
                Offset = offset,
                Remaining = (int)Math.Min(count, int.MaxValue),
                Previous = 0,
                Current = 0,
            };

            if (cursor.Remaining > 0)
            {
                cursor.Current = ReadVarInt(ref cursor.Offset);
            }

            return cursor;
        }

        private struct PostingCursor
        {
            public long Offset;
            public int Remaining;
            public uint Previous;
            public uint Current;
        }

        /// <summary>读取下一条 delta 并还原排名；倒排按排名升序存，delta 基本是两位数，varint 只有 1~3 字节</summary>
        private void Advance(ref PostingCursor cursor)
        {
            cursor.Previous = cursor.Current;
            cursor.Remaining--;
            if (cursor.Remaining > 0)
            {
                cursor.Current = cursor.Previous + ReadVarInt(ref cursor.Offset);
            }
        }

        private int ReadRecordId(uint rank)
        {
            return (int)ReadUInt32(_rankIdsOffset + 4L * rank);
        }

        private ReadOnlySpan<byte> TermAt(int index)
        {
            long offset = _entryDataOffset + ReadUInt32(_termOffsetsOffset + 4L * index);
            int length = ReadByte(offset);
            unsafe
            {
                return new ReadOnlySpan<byte>(_base + offset + 1, length);
            }
        }

        private int FindTerm(ReadOnlySpan<byte> key)
        {
            int index = LowerBound(key);
            return index < _termCount && TermAt(index).SequenceEqual(key) ? index : -1;
        }

        private int LowerBound(ReadOnlySpan<byte> key)
        {
            int low = 0;
            int high = _termCount;
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                if (CsfCompare(TermAt(middle), key) < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        private static bool StartsWith(ReadOnlySpan<byte> value, ReadOnlySpan<byte> prefix)
        {
            return value.Length >= prefix.Length && value[..prefix.Length].SequenceEqual(prefix);
        }

        private static int CsfCompare(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            int common = Math.Min(left.Length, right.Length);
            for (int i = 0; i < common; i++)
            {
                int difference = left[i] - right[i];
                if (difference != 0)
                {
                    return difference;
                }
            }

            return left.Length - right.Length;
        }

        private uint ReadVarInt(ref long offset)
        {
            uint result = 0;
            int shift = 0;
            while (true)
            {
                byte current = ReadByte(offset++);
                result |= (uint)(current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                {
                    return result;
                }

                shift += 7;
            }
        }

        private byte ReadByte(long offset)
        {
            unsafe
            {
                return _base[offset];
            }
        }

        private uint ReadUInt32(long offset)
        {
            unsafe
            {
                return BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(_base + offset, 4));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            unsafe
            {
                if (_view is not null)
                {
                    _view.SafeMemoryMappedViewHandle.ReleasePointer();
                }

                _base = null;
            }

            _view?.Dispose();
            _mappedFile?.Dispose();
        }

        private struct Header
        {
            public int TermCount;
            public int PostingCount;
            public int RecordCount;
            public long TermOffsetsOffset;
            public long EntryDataOffset;
            public long RankIdsOffset;
            public long FileLength;
        }
    }
}
