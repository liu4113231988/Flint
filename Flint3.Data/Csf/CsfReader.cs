using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;
using Flint3.Data.Models;
using ZstdSharp;

namespace Flint3.Data.Csf
{
    /// <summary>文件内的三套块目录</summary>
    internal enum CsfSection
    {
        Record = 0,
        WordIndex = 1,
        SwIndex = 2,
    }

    internal readonly struct BlockEntry
    {
        public readonly long Offset;
        public readonly int CompressedLength;
        public readonly int RawLength;
        public readonly uint Checksum;

        public BlockEntry(long offset, int compressedLength, int rawLength, uint checksum)
        {
            Offset = offset;
            CompressedLength = compressedLength;
            RawLength = rawLength;
            Checksum = checksum;
        }
    }

    /// <summary>解码后的索引块：键区拼接在 Keys 中，用 KeyOffset 定位</summary>
    internal sealed class IndexBlock
    {
        public int Count;
        public byte[] Keys = new byte[64 * 1024];
        public int[] KeyOffset = new int[2049];
        public int[] Ids = new int[2048];

        public ReadOnlySpan<byte> Key(int index) => Keys.AsSpan(KeyOffset[index], KeyOffset[index + 1] - KeyOffset[index]);
    }

    /// <summary>解码后的记录块：5 个字段的长度表 + 拼接正文</summary>
    internal sealed class RecordBlock
    {
        public int Count;
        public int[] Lengths = new int[2048 * 5];
        public byte[] Data = new byte[256 * 1024];
        public int DataLength;
    }

    /// <summary>索引项访问器；键是折叠后的 UTF-8 字节，返回 false 表示提前结束</summary>
    public delegate bool CsfWordEntryVisitor(ReadOnlySpan<byte> key, int id);

    /// <summary>
    /// CSF 只读词典读取器：MemoryMappedFile 只读映射 + 按块 zstd 解压 + 块级缓存。
    /// 查询语义与 SQLite 版本对齐（NOCASE 排序/比较、LIKE、tag 子串、oxford 标记）。
    /// </summary>
    public sealed class CsfReader : IDisposable
    {
        private const int MaxCachedBytes = 8 * 1024 * 1024;

        [ThreadStatic]
        private static RecordBlock? _threadRecordBlock;

        private readonly object _sync = new();
        private readonly Dictionary<long, byte[]> _cache = [];
        private readonly Queue<long> _cacheOrder = new();
        private readonly Decompressor _decompressor = new();

        private int _cachedBytes;
        private bool _disposed;

        private MemoryMappedFile? _mappedFile;
        private MemoryMappedViewAccessor? _view;
        private unsafe byte* _base;

        private readonly int _recordsPerBlock;
        private readonly int _recordCount;
        private readonly int _recordBlockCount;

        private readonly BlockEntry[] _recordDirectories;
        private readonly BlockEntry[] _wordDirectories;
        private readonly BlockEntry[] _swDirectories;

        private readonly byte[][] _wordFirstKeys;
        private readonly byte[][] _swFirstKeys;

        private readonly long _oxfordOffset;
        private readonly int _oxfordCompressedLength;
        private readonly int _oxfordRawLength;
        private readonly long _tagValuesOffset;
        private readonly int _tagValuesCompressedLength;
        private readonly int _tagValuesRawLength;
        private readonly long _tagIndexOffset;
        private readonly int _tagIndexCompressedLength;
        private readonly int _tagIndexRawLength;

        private byte[]? _oxford;
        private string[]? _tagValues;
        private byte[]? _tagIndex;

        public CsfReader(string path)
        {
            _mappedFile = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            _view = _mappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            unsafe
            {
                byte* pointer = null;
                _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
                _base = pointer + _view.PointerOffset;
            }

            Span<byte> header = stackalloc byte[CsfFormat.HeaderSize];
            ReadBytes(0, header);

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header[CsfFormat.OffMagic..]);
            if (magic != CsfFormat.Magic)
            {
                throw new InvalidDataException("不是 CSF 文件（magic 不匹配）");
            }

            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(header[CsfFormat.OffVersion..]);
            if (version != CsfFormat.Version)
            {
                throw new InvalidDataException($"不支持的 CSF 版本：{version}");
            }

            _recordsPerBlock = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[CsfFormat.OffRecordsPerBlock..]);
            _recordCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[CsfFormat.OffRecordCount..]);
            _recordBlockCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[CsfFormat.OffRecordBlockCount..]);
            int wordBlockCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[CsfFormat.OffWordIndexBlockCount..]);
            int swBlockCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[CsfFormat.OffSwIndexBlockCount..]);

            _recordDirectories = ReadDirectory(header, CsfFormat.OffRecordDirOffset, _recordBlockCount);
            _wordDirectories = ReadDirectory(header, CsfFormat.OffWordDirOffset, wordBlockCount);
            _swDirectories = ReadDirectory(header, CsfFormat.OffSwDirOffset, swBlockCount);
            _wordFirstKeys = ReadFirstKeys(
                BinaryPrimitives.ReadInt64LittleEndian(header[CsfFormat.OffWordFirstKeysOffset..]),
                BinaryPrimitives.ReadInt32LittleEndian(header[CsfFormat.OffWordFirstKeysLength..]));
            _swFirstKeys = ReadFirstKeys(
                BinaryPrimitives.ReadInt64LittleEndian(header[CsfFormat.OffSwFirstKeysOffset..]),
                BinaryPrimitives.ReadInt32LittleEndian(header[CsfFormat.OffSwFirstKeysLength..]));

            _oxfordOffset = BinaryPrimitives.ReadInt64LittleEndian(header[CsfFormat.OffOxfordOffset..]);
            _oxfordCompressedLength = BinaryPrimitives.ReadInt32LittleEndian(header[CsfFormat.OffOxfordLength..]);
            _oxfordRawLength = BinaryPrimitives.ReadInt32LittleEndian(header[CsfFormat.OffOxfordRawLength..]);

            _tagValuesOffset = BinaryPrimitives.ReadInt64LittleEndian(header[CsfFormat.OffTagValuesOffset..]);
            _tagValuesCompressedLength = BinaryPrimitives.ReadInt32LittleEndian(header[CsfFormat.OffTagValuesLength..]);
            _tagValuesRawLength = BinaryPrimitives.ReadInt32LittleEndian(header[CsfFormat.OffTagValuesRawLength..]);

            _tagIndexOffset = BinaryPrimitives.ReadInt64LittleEndian(header[CsfFormat.OffTagIndexOffset..]);
            _tagIndexCompressedLength = BinaryPrimitives.ReadInt32LittleEndian(header[CsfFormat.OffTagIndexLength..]);
            _tagIndexRawLength = BinaryPrimitives.ReadInt32LittleEndian(header[CsfFormat.OffTagIndexRawLength..]);
        }

        public int RecordCount => _recordCount;

        /// <summary>按词头折叠序枚举记录 id，用于构建期/发布前的顺序对拍</summary>
        public IEnumerable<int> EnumerateWordOrderIds()
        {
            IndexBlock index = new();
            for (int block = 0; block < _wordDirectories.Length; block++)
            {
                DecodeIndexBlock(CsfSection.WordIndex, block, index);
                for (int i = 0; i < index.Count; i++)
                {
                    yield return index.Ids[i];
                }
            }
        }

        /// <summary>按 (sw, word) 折叠序枚举记录 id，用于构建期/发布前的顺序对拍</summary>
        public IEnumerable<int> EnumerateSwOrderIds()
        {
            IndexBlock index = new();
            for (int block = 0; block < _swDirectories.Length; block++)
            {
                DecodeIndexBlock(CsfSection.SwIndex, block, index);
                for (int i = 0; i < index.Count; i++)
                {
                    yield return index.Ids[i];
                }
            }
        }

        /// <summary>遍历索引项（键为折叠后的 UTF-8 字节），返回 false 可提前结束</summary>
        public void ForEachWordIndexEntry(CsfWordEntryVisitor visitor)
        {
            ForEachIndexEntry(CsfSection.WordIndex, _wordDirectories, visitor);
        }

        public void ForEachSwIndexEntry(CsfWordEntryVisitor visitor)
        {
            ForEachIndexEntry(CsfSection.SwIndex, _swDirectories, visitor);
        }

        private void ForEachIndexEntry(CsfSection section, BlockEntry[] directories, CsfWordEntryVisitor visitor)
        {
            IndexBlock index = new();
            for (int block = 0; block < directories.Length; block++)
            {
                DecodeIndexBlock(section, block, index);
                for (int i = 0; i < index.Count; i++)
                {
                    if (!visitor(index.Key(i), index.Ids[i]))
                    {
                        return;
                    }
                }
            }
        }

        #region 查询入口

        public List<StarDictWordItem> QueryWord(string word)
        {
            List<StarDictWordItem> results = [];
            byte[] key = FoldKey(word);
            int block = FindBlock(_wordFirstKeys, key);
            IndexBlock index = new();
            for (; block < _wordDirectories.Length; block++)
            {
                DecodeIndexBlock(CsfSection.WordIndex, block, index);
                for (int i = 0; i < index.Count; i++)
                {
                    int compare = CsfText.Compare(index.Key(i), key);
                    if (compare == 0)
                    {
                        results.Add(GetRecordById(index.Ids[i]));
                        return results;
                    }

                    if (compare > 0)
                    {
                        return results;
                    }
                }
            }

            return results;
        }

        public List<StarDictWordItem> MatchWord(string word, int limit)
        {
            List<StarDictWordItem> results = [];
            if (limit <= 0)
            {
                return results;
            }

            byte[] key = FoldKey(word);
            int block = FindBlock(_swFirstKeys, key);
            IndexBlock index = new();
            for (; block < _swDirectories.Length && results.Count < limit; block++)
            {
                DecodeIndexBlock(CsfSection.SwIndex, block, index);
                for (int i = 0; i < index.Count && results.Count < limit; i++)
                {
                    if (CsfText.Compare(index.Key(i), key) < 0)
                    {
                        continue;
                    }

                    results.Add(GetRecordById(index.Ids[i]));
                }
            }

            return results;
        }

        public List<StarDictWordItem> PatternWord(string pattern, int? exactLength, int limit, CancellationToken cancellationToken)
        {
            List<StarDictWordItem> results = [];
            if (limit <= 0)
            {
                return results;
            }

            LikePattern like = LikePattern.Parse(pattern);
            IndexBlock index = new();

            if (like.LiteralPrefix is null && IsPlainLiteral(pattern, out byte[] literal))
            {
                foreach (int id in FindExactWord(literal))
                {
                    StarDictWordItem item = GetRecordById(id);
                    if (!exactLength.HasValue || CsfText.CountChars(FoldKey(item.Word)) == exactLength.Value)
                    {
                        results.Add(item);
                    }

                    if (results.Count >= limit)
                    {
                        break;
                    }
                }

                return results;
            }

            byte[]? prefix = like.LiteralPrefix;
            int block = prefix is null ? 0 : FindBlock(_wordFirstKeys, prefix);
            for (; block < _wordDirectories.Length && results.Count < limit; block++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DecodeIndexBlock(CsfSection.WordIndex, block, index);
                for (int i = 0; i < index.Count && results.Count < limit; i++)
                {
                    ReadOnlySpan<byte> key = index.Key(i);
                    if (prefix is not null)
                    {
                        int compare = CsfText.Compare(key, prefix);
                        if (compare < 0)
                        {
                            continue;
                        }

                        if (!CsfText.StartsWith(key, prefix))
                        {
                            // 前缀区间已经走完，后续块不可能再命中
                            return results;
                        }
                    }

                    if (exactLength.HasValue && CsfText.CountChars(key) != exactLength.Value)
                    {
                        continue;
                    }

                    if (like.IsMatch(key))
                    {
                        results.Add(GetRecordById(index.Ids[i]));
                    }
                }
            }

            return results;
        }

        public List<StarDictWordItem> GetAllGlossaryWords(string tag, string word, CancellationToken cancellationToken)
        {
            return ScanGlossary(tag, word, 0, 0, cancellationToken);
        }

        public List<StarDictWordItem> GetIncrementalGlossaryWords(string tag, long startId, int limit, string word, CancellationToken cancellationToken)
        {
            return ScanGlossary(tag, word, startId, limit, cancellationToken);
        }

        public int GetGlossaryWordsCount(string tag)
        {
            if (tag == "oxford")
            {
                ReadOnlySpan<byte> bitmap = OxfordBitmap();
                int total = 0;
                for (int i = 0; i < _recordCount; i++)
                {
                    if ((bitmap[i >> 3] & (1 << (i & 7))) != 0)
                    {
                        total++;
                    }
                }

                return total;
            }

            bool[] matched = MatchTagValues(tag);
            ReadOnlySpan<byte> tagIndex = TagIndex();
            int count = 0;
            for (int i = 0; i < _recordCount; i++)
            {
                if (matched[tagIndex[i]])
                {
                    count++;
                }
            }

            return count;
        }

        #endregion

        #region 生词本扫描

        private List<StarDictWordItem> ScanGlossary(string tag, string word, long startId, int limit, CancellationToken cancellationToken)
        {
            List<StarDictWordItem> results = [];
            bool isOxford = tag == "oxford";
            bool[]? matched = isOxford ? null : MatchTagValues(tag);
            ReadOnlySpan<byte> oxford = isOxford ? OxfordBitmap() : default;
            ReadOnlySpan<byte> tagIndex = isOxford ? default : TagIndex();

            LikePattern? wordPattern = string.IsNullOrEmpty(word) ? null : LikePattern.Parse(word + "%");
            byte[]? wordPrefix = wordPattern?.LiteralPrefix;

            IndexBlock index = new();
            int block = wordPrefix is null ? 0 : FindBlock(_wordFirstKeys, wordPrefix);
            for (; block < _wordDirectories.Length; block++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DecodeIndexBlock(CsfSection.WordIndex, block, index);
                for (int i = 0; i < index.Count; i++)
                {
                    ReadOnlySpan<byte> key = index.Key(i);
                    if (wordPrefix is not null)
                    {
                        int compare = CsfText.Compare(key, wordPrefix);
                        if (compare < 0)
                        {
                            continue;
                        }

                        if (!CsfText.StartsWith(key, wordPrefix))
                        {
                            return results;
                        }
                    }

                    int id = index.Ids[i];
                    if (isOxford)
                    {
                        int bit = id - 1;
                        if ((oxford[bit >> 3] & (1 << (bit & 7))) == 0)
                        {
                            continue;
                        }
                    }
                    else if (!matched![tagIndex[id - 1]])
                    {
                        continue;
                    }

                    if (id <= startId)
                    {
                        continue;
                    }

                    if (wordPattern is not null && !wordPattern.IsMatch(key))
                    {
                        continue;
                    }

                    results.Add(GetRecordById(id));
                    if (limit > 0 && results.Count >= limit)
                    {
                        return results;
                    }
                }
            }

            return results;
        }

        private bool[] MatchTagValues(string tag)
        {
            bool[] matched = new bool[256];
            byte[] needle = FoldKey(tag);
            string[] values = TagValues();
            for (int i = 0; i < values.Length; i++)
            {
                byte[] value = Encoding.UTF8.GetBytes(values[i]);
                CsfText.FoldInPlace(value);
                matched[i] = value.AsSpan().IndexOf(needle) >= 0;
            }

            return matched;
        }

        private string[] TagValues()
        {
            if (_tagValues is not null)
            {
                return _tagValues;
            }

            lock (_sync)
            {
                if (_tagValues is null)
                {
                    byte[] raw = Decompress(_tagValuesOffset, _tagValuesCompressedLength, _tagValuesRawLength);
                    ReadOnlySpan<byte> span = raw;
                    int offset = 0;
                    int count = (int)VarInt.Read(span, ref offset);
                    string[] values = new string[count];
                    for (int i = 0; i < count; i++)
                    {
                        int length = (int)VarInt.Read(span, ref offset);
                        values[i] = Encoding.UTF8.GetString(span.Slice(offset, length));
                        offset += length;
                    }

                    _tagValues = values;
                }

                return _tagValues;
            }
        }

        private ReadOnlySpan<byte> OxfordBitmap()
        {
            if (_oxford is not null)
            {
                return _oxford;
            }

            lock (_sync)
            {
                _oxford ??= Decompress(_oxfordOffset, _oxfordCompressedLength, _oxfordRawLength);
                return _oxford;
            }
        }

        private ReadOnlySpan<byte> TagIndex()
        {
            if (_tagIndex is not null)
            {
                return _tagIndex;
            }

            lock (_sync)
            {
                _tagIndex ??= Decompress(_tagIndexOffset, _tagIndexCompressedLength, _tagIndexRawLength);
                return _tagIndex;
            }
        }

        #endregion

        #region 记录与索引读取

        /// <summary>按记录 id 取单条记录，供旁挂中文索引回读英文词条</summary>
        public StarDictWordItem GetRecordById(int id)
        {
            RecordBlock block = _threadRecordBlock ??= new RecordBlock();
            int blockIndex = (id - 1) / _recordsPerBlock;
            int local = (id - 1) % _recordsPerBlock;
            DecodeRecordBlock(blockIndex, block);
            if (local >= block.Count)
            {
                throw new InvalidDataException($"记录 {id} 越界（块内 {block.Count} 条）");
            }

            int dataOffset = 0;
            for (int i = 0; i < local; i++)
            {
                for (int field = 0; field < 5; field++)
                {
                    dataOffset += block.Lengths[i * 5 + field];
                }
            }

            int lengthOffset = local * 5;
            string[] fields = new string[5];
            for (int field = 0; field < 5; field++)
            {
                int length = block.Lengths[lengthOffset + field];
                fields[field] = length == 0 ? string.Empty : Encoding.UTF8.GetString(block.Data, dataOffset, length);
                dataOffset += length;
            }

            string word = fields[0];
            return new StarDictWordItem
            {
                Id = id,
                Word = word,
                StripWord = CsfText.DeriveStripWord(word),
                Phonetic = fields[1],
                Definition = fields[2],
                Translation = fields[3],
                Exchange = fields[4],
            };
        }

        private void DecodeRecordBlock(int blockIndex, RecordBlock block)
        {
            byte[] raw = ReadBlock(CsfSection.Record, blockIndex);
            ReadOnlySpan<byte> span = raw;
            int offset = 0;
            int count = (int)VarInt.Read(span, ref offset);
            if (block.Lengths.Length < count * 5)
            {
                block.Lengths = new int[count * 5];
            }

            for (int i = 0; i < count * 5; i++)
            {
                block.Lengths[i] = (int)VarInt.Read(span, ref offset);
            }

            int dataLength = raw.Length - offset;
            if (block.Data.Length < dataLength)
            {
                block.Data = new byte[dataLength];
            }

            span[offset..].CopyTo(block.Data);
            block.Count = count;
            block.DataLength = dataLength;
        }

        private void DecodeIndexBlock(CsfSection section, int blockIndex, IndexBlock block)
        {
            byte[] raw = ReadBlock(section, blockIndex);
            ReadOnlySpan<byte> span = raw;
            int offset = 0;
            int count = (int)VarInt.Read(span, ref offset);

            if (block.KeyOffset.Length < count + 1)
            {
                block.KeyOffset = new int[count + 1];
            }

            if (block.Ids.Length < count)
            {
                block.Ids = new int[count];
            }

            int keyOffset = 0;
            int previousOffset = 0;
            for (int i = 0; i < count; i++)
            {
                int prefixLength = (int)VarInt.Read(span, ref offset);
                int suffixLength = (int)VarInt.Read(span, ref offset);
                int totalLength = prefixLength + suffixLength;

                if (keyOffset + totalLength > block.Keys.Length)
                {
                    Array.Resize(ref block.Keys, Math.Max(block.Keys.Length * 2, keyOffset + totalLength));
                }

                block.KeyOffset[i] = keyOffset;
                if (prefixLength > 0)
                {
                    // 前缀来自上一个键已重建的字节（两段不重叠：目标起点等于上键末尾）
                    block.Keys.AsSpan(previousOffset, prefixLength).CopyTo(block.Keys.AsSpan(keyOffset));
                }

                span.Slice(offset, suffixLength).CopyTo(block.Keys.AsSpan(keyOffset + prefixLength));
                offset += suffixLength;
                previousOffset = keyOffset;
                keyOffset += totalLength;
                block.Ids[i] = (int)VarInt.Read(span, ref offset);
            }

            block.KeyOffset[count] = keyOffset;
            block.Count = count;
        }

        private IEnumerable<int> FindExactWord(byte[] key)
        {
            int block = FindBlock(_wordFirstKeys, key);
            IndexBlock index = new();
            for (; block < _wordDirectories.Length; block++)
            {
                DecodeIndexBlock(CsfSection.WordIndex, block, index);
                for (int i = 0; i < index.Count; i++)
                {
                    int compare = CsfText.Compare(index.Key(i), key);
                    if (compare == 0)
                    {
                        yield return index.Ids[i];
                        yield break;
                    }

                    if (compare > 0)
                    {
                        yield break;
                    }
                }
            }
        }

        /// <summary>返回首键不大于 <paramref name="key"/> 的最后一个块；key 比首块还小时返回 0</summary>
        private static int FindBlock(byte[][] firstKeys, ReadOnlySpan<byte> key)
        {
            if (firstKeys.Length == 0)
            {
                return 0;
            }

            int low = 0;
            int high = firstKeys.Length - 1;
            int result = 0;
            while (low <= high)
            {
                int middle = (low + high) / 2;
                if (CsfText.Compare(firstKeys[middle], key) <= 0)
                {
                    result = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return result;
        }

        private static bool IsPlainLiteral(string pattern, out byte[] literal)
        {
            if (pattern.Length == 0 || pattern.IndexOf('%') >= 0 || pattern.IndexOf('_') >= 0)
            {
                literal = [];
                return false;
            }

            literal = FoldKey(pattern);
            return true;
        }

        #endregion

        #region 底层 IO

        private byte[] ReadBlock(CsfSection section, int blockIndex)
        {
            BlockEntry[] directory = section switch
            {
                CsfSection.Record => _recordDirectories,
                CsfSection.WordIndex => _wordDirectories,
                _ => _swDirectories,
            };

            if ((uint)blockIndex >= (uint)directory.Length)
            {
                throw new InvalidDataException($"块索引越界：{section}/{blockIndex}");
            }

            long cacheKey = ((long)section << 40) | (uint)blockIndex;
            lock (_sync)
            {
                if (_cache.TryGetValue(cacheKey, out byte[]? cached))
                {
                    return cached;
                }

                BlockEntry entry = directory[blockIndex];
                byte[] block = Decompress(entry.Offset, entry.CompressedLength, entry.RawLength);
                if (XxHash32.HashToUInt32(block) != entry.Checksum)
                {
                    throw new InvalidDataException($"块校验失败：{section}/{blockIndex}");
                }

                _cache[cacheKey] = block;
                _cacheOrder.Enqueue(cacheKey);
                _cachedBytes += block.Length;
                while (_cachedBytes > MaxCachedBytes && _cacheOrder.Count > 1)
                {
                    long evicted = _cacheOrder.Dequeue();
                    if (_cache.Remove(evicted, out byte[]? removed))
                    {
                        _cachedBytes -= removed.Length;
                    }
                }

                return block;
            }
        }

        private unsafe byte[] Decompress(long offset, int compressedLength, int rawLength)
        {
            if (rawLength <= 0)
            {
                return [];
            }

            ReadOnlySpan<byte> compressed = new(_base + offset, compressedLength);
            byte[] raw = new byte[rawLength];
            int written = _decompressor.Unwrap(compressed, raw);
            if (written != rawLength)
            {
                throw new InvalidDataException($"块解压长度不符：期望 {rawLength}，实际 {written}");
            }

            return raw;
        }

        private BlockEntry[] ReadDirectory(Span<byte> header, int offsetField, int count)
        {
            long offset = BinaryPrimitives.ReadInt64LittleEndian(header[offsetField..]);
            BlockEntry[] entries = new BlockEntry[count];
            Span<byte> entry = stackalloc byte[CsfFormat.DirectoryEntrySize];
            for (int i = 0; i < count; i++)
            {
                ReadBytes(offset + (long)i * CsfFormat.DirectoryEntrySize, entry);
                entries[i] = new BlockEntry(
                    BinaryPrimitives.ReadInt64LittleEndian(entry),
                    BinaryPrimitives.ReadInt32LittleEndian(entry[8..]),
                    BinaryPrimitives.ReadInt32LittleEndian(entry[12..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(entry[16..]));
            }

            return entries;
        }

        private byte[][] ReadFirstKeys(long offset, int length)
        {
            byte[] raw = new byte[length];
            ReadBytes(offset, raw);
            ReadOnlySpan<byte> span = raw;
            int cursor = 0;
            int count = (int)VarInt.Read(span, ref cursor);
            byte[][] keys = new byte[count][];
            byte[] previous = [];
            for (int i = 0; i < count; i++)
            {
                int prefixLength = (int)VarInt.Read(span, ref cursor);
                int suffixLength = (int)VarInt.Read(span, ref cursor);
                byte[] key = new byte[prefixLength + suffixLength];
                previous.AsSpan(0, prefixLength).CopyTo(key);
                span.Slice(cursor, suffixLength).CopyTo(key.AsSpan(prefixLength));
                cursor += suffixLength;
                keys[i] = key;
                previous = key;
            }

            return keys;
        }

        private unsafe void ReadBytes(long offset, Span<byte> destination)
        {
            ReadOnlySpan<byte> source = new(_base + offset, destination.Length);
            source.CopyTo(destination);
        }

        private static byte[] FoldKey(string text)
        {
            byte[] buffer = new byte[Encoding.UTF8.GetByteCount(text)];
            int length = CsfText.Fold(text, buffer);
            return length == buffer.Length ? buffer : buffer[..length];
        }

        #endregion

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            lock (_sync)
            {
                _cache.Clear();
                _cacheOrder.Clear();
                _cachedBytes = 0;
                _decompressor.Dispose();
            }

            unsafe
            {
                if (_base != null)
                {
                    _view?.SafeMemoryMappedViewHandle.ReleasePointer();
                    _base = null;
                }
            }

            _view?.Dispose();
            _mappedFile?.Dispose();
            _view = null;
            _mappedFile = null;
        }
    }
}
