using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
using ZstdSharp;

namespace Flint3.Data.Csf
{
    public sealed class CsfBuildStats
    {
        public int RecordCount { get; init; }
        public int RecordBlockCount { get; init; }
        public int WordIndexBlockCount { get; init; }
        public int SwIndexBlockCount { get; init; }
        public long RecordBytes { get; init; }
        public long WordIndexBytes { get; init; }
        public long SwIndexBytes { get; init; }
        public long AuxiliaryBytes { get; init; }
        public long FileBytes { get; init; }
        public TimeSpan Elapsed { get; init; }
    }

    /// <summary>
    /// 从 ECDICT 的 stardict.db 构建 CSF 只读词典文件。离线工具，不参与运行时查询。
    /// </summary>
    public static class CsfWriter
    {
        public static CsfBuildStats Build(
            string databasePath,
            string csfPath,
            int compressionLevel = 12,
            int recordsPerBlock = 2048,
            int indexBlockSize = 2048)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            using SqliteConnection connection = new($"Filename={databasePath};Mode=ReadOnly;Pooling=False");
            connection.Open();

            int recordCount = ReadRecordCount(connection);
            if (recordCount <= 0)
            {
                throw new InvalidDataException("源库为空");
            }

            int[] wordOffsets = new int[recordCount];
            int[] wordLengths = new int[recordCount];
            int[] swOffsets = new int[recordCount];
            int[] swLengths = new int[recordCount];
            byte[] oxford = new byte[(recordCount + 7) / 8];
            byte[] tagIndex = new byte[recordCount];
            List<string> tagValues = [string.Empty];
            Dictionary<string, int> tagLookup = new(StringComparer.Ordinal);

            ByteArena wordArena = new(64 * 1024 * 1024);
            ByteArena swArena = new(48 * 1024 * 1024);

            List<BlockEntry> recordDirectories = [];
            ByteBuffer recordPayload = new(256 * 1024);
            ByteBuffer lengthBuffer = new(16 * 1024);
            ByteBuffer dataBuffer = new(256 * 1024);
            byte[] scratch = new byte[1024];

            using FileStream stream = new(csfPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
            stream.Position = CsfFormat.HeaderSize;

            using Compressor compressor = new(compressionLevel);
            long recordBytes = 0;

            void FlushRecords(int count)
            {
                if (count == 0)
                {
                    return;
                }

                recordPayload.Clear();
                recordPayload.WriteVarInt((uint)count);
                recordPayload.Write(lengthBuffer.Span);
                recordPayload.Write(dataBuffer.Span);

                long offset = stream.Position;
                Span<byte> compressed = compressor.Wrap(recordPayload.Span);
                stream.Write(compressed);
                recordDirectories.Add(new BlockEntry(offset, compressed.Length, recordPayload.Length, XxHash32.HashToUInt32(recordPayload.Span)));
                recordBytes += compressed.Length;

                lengthBuffer.Clear();
                dataBuffer.Clear();
            }

            int pendingCount = 0;
            int expectedId = 1;

            using (SqliteCommand command = new(
                "select id, word, sw, phonetic, definition, translation, exchange, oxford, tag from stardict order by id",
                connection))
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    long id = reader.GetInt64(0);
                    if (id != expectedId)
                    {
                        throw new InvalidDataException($"id 不连续：期望 {expectedId}，实际 {id}");
                    }

                    string word = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    string sw = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);

                    string derivedSw = CsfText.DeriveStripWord(word);
                    if (!string.Equals(derivedSw, sw, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"sw 无法由 word 派生：id={id} word={word} sw={sw} derived={derivedSw}");
                    }

                    int index = expectedId - 1;

                    int wordLength = CsfText.Fold(word, EnsureScratch(ref scratch, word));
                    wordOffsets[index] = wordArena.Append(scratch.AsSpan(0, wordLength));
                    wordLengths[index] = wordLength;

                    int swLength = CsfText.Fold(sw, EnsureScratch(ref scratch, sw));
                    swOffsets[index] = swArena.Append(scratch.AsSpan(0, swLength));
                    swLengths[index] = swLength;

                    if (!reader.IsDBNull(7) && reader.GetInt64(7) != 0)
                    {
                        oxford[index >> 3] |= (byte)(1 << (index & 7));
                    }

                    string tag = reader.IsDBNull(8) ? string.Empty : reader.GetString(8);
                    if (tag.Length > 0)
                    {
                        if (!tagLookup.TryGetValue(tag, out int tagValue))
                        {
                            tagValue = tagValues.Count;
                            if (tagValue > byte.MaxValue)
                            {
                                throw new InvalidDataException("tag 取值过多，超出单字节索引范围");
                            }

                            tagValues.Add(tag);
                            tagLookup[tag] = tagValue;
                        }

                        tagIndex[index] = (byte)tagValue;
                    }

                    WriteRecord(
                        lengthBuffer,
                        dataBuffer,
                        ref scratch,
                        word,
                        reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                        reader.IsDBNull(6) ? string.Empty : reader.GetString(6));

                    pendingCount++;
                    if (pendingCount == recordsPerBlock)
                    {
                        FlushRecords(pendingCount);
                        pendingCount = 0;
                    }

                    expectedId++;
                }
            }

            FlushRecords(pendingCount);

            int[] wordOrder = new int[recordCount];
            int[] swOrder = new int[recordCount];
            for (int i = 0; i < recordCount; i++)
            {
                wordOrder[i] = i;
                swOrder[i] = i;
            }

            Array.Sort(wordOrder, (left, right) =>
            {
                int compare = CsfText.Compare(
                    wordArena.Slice(wordOffsets[left], wordLengths[left]),
                    wordArena.Slice(wordOffsets[right], wordLengths[right]));
                return compare != 0 ? compare : left.CompareTo(right);
            });

            Array.Sort(swOrder, (left, right) =>
            {
                int compare = CsfText.Compare(
                    swArena.Slice(swOffsets[left], swLengths[left]),
                    swArena.Slice(swOffsets[right], swLengths[right]));
                if (compare != 0)
                {
                    return compare;
                }

                compare = CsfText.Compare(
                    wordArena.Slice(wordOffsets[left], wordLengths[left]),
                    wordArena.Slice(wordOffsets[right], wordLengths[right]));
                return compare != 0 ? compare : left.CompareTo(right);
            });

            long recordDirectoryOffset;
            long wordDirectoryOffset;
            long swDirectoryOffset;

            long wordIndexOffset = stream.Position;
            (List<BlockEntry> wordDirectories, List<byte[]> wordFirstKeys) =
                WriteIndex(stream, compressor, wordOrder, wordOffsets, wordLengths, wordArena, indexBlockSize);
            long wordIndexLength = stream.Position - wordIndexOffset;

            long swIndexOffset = stream.Position;
            (List<BlockEntry> swDirectories, List<byte[]> swFirstKeys) =
                WriteIndex(stream, compressor, swOrder, swOffsets, swLengths, swArena, indexBlockSize);
            long swIndexLength = stream.Position - swIndexOffset;

            long wordFirstKeysOffset = stream.Position;
            byte[] wordFirstKeysRaw = EncodeFirstKeys(wordFirstKeys);
            stream.Write(wordFirstKeysRaw);

            long swFirstKeysOffset = stream.Position;
            byte[] swFirstKeysRaw = EncodeFirstKeys(swFirstKeys);
            stream.Write(swFirstKeysRaw);

            long oxfordOffset = stream.Position;
            Span<byte> oxfordCompressed = compressor.Wrap(oxford);
            stream.Write(oxfordCompressed);

            long tagValuesOffset = stream.Position;
            ByteBuffer tagValuesBuffer = new(4096);
            tagValuesBuffer.WriteVarInt((uint)tagValues.Count);
            foreach (string value in tagValues)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                tagValuesBuffer.WriteVarInt((uint)bytes.Length);
                tagValuesBuffer.Write(bytes);
            }

            byte[] tagValuesRaw = tagValuesBuffer.Span.ToArray();
            Span<byte> tagValuesCompressed = compressor.Wrap(tagValuesRaw);
            stream.Write(tagValuesCompressed);

            long tagIndexOffset = stream.Position;
            Span<byte> tagIndexCompressed = compressor.Wrap(tagIndex);
            stream.Write(tagIndexCompressed);

            recordDirectoryOffset = stream.Position;
            WriteDirectory(stream, recordDirectories);
            wordDirectoryOffset = stream.Position;
            WriteDirectory(stream, wordDirectories);
            swDirectoryOffset = stream.Position;
            WriteDirectory(stream, swDirectories);

            long fileLength = stream.Position;

            Span<byte> header = stackalloc byte[CsfFormat.HeaderSize];
            BinaryPrimitives.WriteUInt32LittleEndian(header[CsfFormat.OffMagic..], CsfFormat.Magic);
            BinaryPrimitives.WriteUInt16LittleEndian(header[CsfFormat.OffVersion..], CsfFormat.Version);
            BinaryPrimitives.WriteUInt16LittleEndian(header[CsfFormat.OffHeaderSize..], (ushort)CsfFormat.HeaderSize);
            BinaryPrimitives.WriteUInt32LittleEndian(header[CsfFormat.OffRecordsPerBlock..], (uint)recordsPerBlock);
            BinaryPrimitives.WriteUInt32LittleEndian(header[CsfFormat.OffIndexBlockSize..], (uint)indexBlockSize);
            BinaryPrimitives.WriteUInt32LittleEndian(header[CsfFormat.OffRecordCount..], (uint)recordCount);
            BinaryPrimitives.WriteInt32LittleEndian(header[CsfFormat.OffRecordBlockCount..], recordDirectories.Count);
            BinaryPrimitives.WriteInt32LittleEndian(header[CsfFormat.OffWordIndexBlockCount..], wordDirectories.Count);
            BinaryPrimitives.WriteInt32LittleEndian(header[CsfFormat.OffSwIndexBlockCount..], swDirectories.Count);
            BinaryPrimitives.WriteInt32LittleEndian(header[CsfFormat.OffTagValueCount..], tagValues.Count);
            BinaryPrimitives.WriteInt64LittleEndian(header[CsfFormat.OffRecordDirOffset..], recordDirectoryOffset);
            BinaryPrimitives.WriteInt64LittleEndian(header[CsfFormat.OffWordDirOffset..], wordDirectoryOffset);
            BinaryPrimitives.WriteInt64LittleEndian(header[CsfFormat.OffSwDirOffset..], swDirectoryOffset);
            BinaryPrimitives.WriteInt64LittleEndian(header[CsfFormat.OffWordIndexOffset..], wordIndexOffset);
            BinaryPrimitives.WriteInt64LittleEndian(header[CsfFormat.OffWordIndexLength..], wordIndexLength);
            BinaryPrimitives.WriteInt64LittleEndian(header[CsfFormat.OffSwIndexOffset..], swIndexOffset);
            BinaryPrimitives.WriteInt64LittleEndian(header[CsfFormat.OffSwIndexLength..], swIndexLength);
            BinaryPrimitives.WriteInt64LittleEndian(header[CsfFormat.OffWordFirstKeysOffset..], wordFirstKeysOffset);
            BinaryPrimitives.WriteInt32LittleEndian(header[CsfFormat.OffWordFirstKeysLength..], wordFirstKeysRaw.Length);
            BinaryPrimitives.WriteInt64LittleEndian(header[CsfFormat.OffSwFirstKeysOffset..], swFirstKeysOffset);
            BinaryPrimitives.WriteInt32LittleEndian(header[CsfFormat.OffSwFirstKeysLength..], swFirstKeysRaw.Length);
            BinaryPrimitives.WriteInt64LittleEndian(header[CsfFormat.OffOxfordOffset..], oxfordOffset);
            BinaryPrimitives.WriteInt32LittleEndian(header[CsfFormat.OffOxfordLength..], oxfordCompressed.Length);
            BinaryPrimitives.WriteInt32LittleEndian(header[CsfFormat.OffOxfordRawLength..], oxford.Length);
            BinaryPrimitives.WriteInt64LittleEndian(header[CsfFormat.OffTagValuesOffset..], tagValuesOffset);
            BinaryPrimitives.WriteInt32LittleEndian(header[CsfFormat.OffTagValuesLength..], tagValuesCompressed.Length);
            BinaryPrimitives.WriteInt32LittleEndian(header[CsfFormat.OffTagValuesRawLength..], tagValuesRaw.Length);
            BinaryPrimitives.WriteInt64LittleEndian(header[CsfFormat.OffTagIndexOffset..], tagIndexOffset);
            BinaryPrimitives.WriteInt32LittleEndian(header[CsfFormat.OffTagIndexLength..], tagIndexCompressed.Length);
            BinaryPrimitives.WriteInt32LittleEndian(header[CsfFormat.OffTagIndexRawLength..], tagIndex.Length);
            BinaryPrimitives.WriteInt64LittleEndian(header[CsfFormat.OffFileLength..], fileLength);

            stream.Position = 0;
            stream.Write(header);

            long wordIndexBytes = wordDirectories.Sum(static entry => (long)entry.CompressedLength);
            long swIndexBytes = swDirectories.Sum(static entry => (long)entry.CompressedLength);
            long auxiliary = fileLength - recordBytes - wordIndexBytes - swIndexBytes;

            return new CsfBuildStats
            {
                RecordCount = recordCount,
                RecordBlockCount = recordDirectories.Count,
                WordIndexBlockCount = wordDirectories.Count,
                SwIndexBlockCount = swDirectories.Count,
                RecordBytes = recordBytes,
                WordIndexBytes = wordIndexBytes,
                SwIndexBytes = swIndexBytes,
                AuxiliaryBytes = auxiliary,
                FileBytes = fileLength,
                Elapsed = stopwatch.Elapsed,
            };
        }

        private static int ReadRecordCount(SqliteConnection connection)
        {
            using SqliteCommand command = new("select count(*) from stardict", connection);
            return Convert.ToInt32(command.ExecuteScalar());
        }

        private static Span<byte> EnsureScratch(ref byte[] scratch, string value)
        {
            int byteCount = Encoding.UTF8.GetByteCount(value);
            if (scratch.Length < byteCount)
            {
                scratch = new byte[Math.Max(byteCount, scratch.Length * 2)];
            }

            return scratch.AsSpan(0, byteCount);
        }

        private static void WriteRecord(
            ByteBuffer lengths,
            ByteBuffer data,
            ref byte[] scratch,
            string word,
            string phonetic,
            string definition,
            string translation,
            string exchange)
        {
            WriteField(lengths, data, ref scratch, word);
            WriteField(lengths, data, ref scratch, phonetic);
            WriteField(lengths, data, ref scratch, definition);
            WriteField(lengths, data, ref scratch, translation);
            WriteField(lengths, data, ref scratch, exchange);
        }

        private static void WriteField(ByteBuffer lengths, ByteBuffer data, ref byte[] scratch, string value)
        {
            int byteCount = Encoding.UTF8.GetByteCount(value);
            lengths.WriteVarInt((uint)byteCount);
            if (byteCount == 0)
            {
                return;
            }

            Span<byte> buffer = EnsureScratch(ref scratch, value);
            Encoding.UTF8.GetBytes(value, buffer);
            data.Write(buffer);
        }

        private static (List<BlockEntry> Directories, List<byte[]> FirstKeys) WriteIndex(
            FileStream stream,
            Compressor compressor,
            int[] order,
            int[] keyOffsets,
            int[] keyLengths,
            ByteArena arena,
            int indexBlockSize)
        {
            List<BlockEntry> directories = [];
            List<byte[]> firstKeys = [];
            ByteBuffer payload = new(64 * 1024);

            for (int start = 0; start < order.Length; start += indexBlockSize)
            {
                int count = Math.Min(indexBlockSize, order.Length - start);
                int firstRecord = order[start];
                firstKeys.Add(arena.Slice(keyOffsets[firstRecord], keyLengths[firstRecord]).ToArray());

                payload.Clear();
                payload.WriteVarInt((uint)count);

                ReadOnlySpan<byte> previous = default;
                bool hasPrevious = false;
                for (int i = 0; i < count; i++)
                {
                    int record = order[start + i];
                    ReadOnlySpan<byte> key = arena.Slice(keyOffsets[record], keyLengths[record]);
                    int prefixLength = hasPrevious ? CommonPrefix(previous, key) : 0;
                    payload.WriteVarInt((uint)prefixLength);
                    payload.WriteVarInt((uint)(key.Length - prefixLength));
                    payload.Write(key[prefixLength..]);
                    payload.WriteVarInt((uint)record + 1);
                    previous = key;
                    hasPrevious = true;
                }

                long offset = stream.Position;
                Span<byte> compressed = compressor.Wrap(payload.Span);
                stream.Write(compressed);
                directories.Add(new BlockEntry(offset, compressed.Length, payload.Length, XxHash32.HashToUInt32(payload.Span)));
            }

            return (directories, firstKeys);
        }

        private static int CommonPrefix(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            int limit = Math.Min(left.Length, right.Length);
            int index = 0;
            while (index < limit && left[index] == right[index])
            {
                index++;
            }

            return index;
        }

        private static byte[] EncodeFirstKeys(List<byte[]> keys)
        {
            ByteBuffer buffer = new(64 * 1024);
            buffer.WriteVarInt((uint)keys.Count);
            byte[] previous = [];
            foreach (byte[] key in keys)
            {
                int prefixLength = CommonPrefix(previous, key);
                buffer.WriteVarInt((uint)prefixLength);
                buffer.WriteVarInt((uint)(key.Length - prefixLength));
                buffer.Write(key.AsSpan(prefixLength));
                previous = key;
            }

            return buffer.Span.ToArray();
        }

        private static void WriteDirectory(FileStream stream, List<BlockEntry> entries)
        {
            Span<byte> buffer = stackalloc byte[CsfFormat.DirectoryEntrySize];
            foreach (BlockEntry entry in entries)
            {
                BinaryPrimitives.WriteInt64LittleEndian(buffer, entry.Offset);
                BinaryPrimitives.WriteInt32LittleEndian(buffer[8..], entry.CompressedLength);
                BinaryPrimitives.WriteInt32LittleEndian(buffer[12..], entry.RawLength);
                BinaryPrimitives.WriteUInt32LittleEndian(buffer[16..], entry.Checksum);
                stream.Write(buffer);
            }
        }
    }
}
